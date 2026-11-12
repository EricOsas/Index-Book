"""
FVG Duo backtester engine — spec-exact implementation.
Sections 2–9 of Spec v1.txt.

CHANGES FROM DRAFT:
  - Vectorized build_bars (ET-floor via numpy, not apply-lambda per row)
  - run_backtest pre-groups 1m/10m data by ET date before the day loop
  - od_signal_for_date accepts pre-grouped dicts
  - compute_trend_signals uses numpy arrays throughout the loop
  - Added compatibility aliases: is_fvg, is_consecutive_3, NY_TZ
"""
import json, math, hashlib, csv
from pathlib import Path
from datetime import datetime, timedelta, date as date_t
from zoneinfo import ZoneInfo
from typing import Optional
from collections import defaultdict
import numpy as np
import pandas as pd

ET   = ZoneInfo("America/New_York")
NY_TZ = ET   # alias for tests

# ══════════════════════════════════════════════════════════════════════════════
# DEFAULT CONFIG  (Section 10)
# ══════════════════════════════════════════════════════════════════════════════
DEFAULT_CONFIG = {
    "tick": 0.25,
    "point_value": 20,
    "fee_per_side": 2.14,
    "slip_ticks": 1,          # 1-tick slip per side (default per spec)
    "od_stop_pct": 0.015,
    "trend_ema_fast": 50,
    "trend_ema_slow": 100,
    "trend_atr_len": 14,
    "trend_atr_mult": 1.5,
    "trend_rr": 3.0,
    "trend_first_entry": "09:30",
    "trend_last_entry": "15:30",
    "atr_method": "rma",
    "atr_ref": "c2",
    "fvg_strict": True,
    "merge_anchor": "trend_entry",
    "trend_after_od_stopout": False,
    "warmup_bars": 1000,
    "enable_od": True,
    "enable_trend": True,
}

def make_config(**overrides) -> dict:
    cfg = dict(DEFAULT_CONFIG)
    cfg.update(overrides)
    return cfg

def config_hash(cfg: dict) -> str:
    s = json.dumps(cfg, sort_keys=True, default=str)
    return hashlib.md5(s.encode()).hexdigest()[:8]

# ══════════════════════════════════════════════════════════════════════════════
# DATA LOADING
# ══════════════════════════════════════════════════════════════════════════════

def load_1m_csv(csv_path: str) -> pd.DataFrame:
    """
    Load NQ_1m_merged.csv.  Returns DataFrame with:
      open_ts  : ET-aware datetime (DatetimeTZDtype America/New_York)
      open, high, low, close, volume, contract columns
    """
    df = pd.read_csv(csv_path)
    # Column may be 'timestamp_et_wall' or 'open_ts'
    ts_col = "timestamp_et_wall" if "timestamp_et_wall" in df.columns else "open_ts"
    df["open_ts"] = pd.to_datetime(df[ts_col]).dt.tz_localize("America/New_York")
    if ts_col != "open_ts":
        df = df.drop(columns=[ts_col])
    df = df.sort_values("open_ts").reset_index(drop=True)
    return df

# ══════════════════════════════════════════════════════════════════════════════
# SECTION 2: BAR BUILDING  (vectorized ET floor)
# ══════════════════════════════════════════════════════════════════════════════

def build_bars(df1m: pd.DataFrame, n: int) -> pd.DataFrame:
    """
    Build N-minute bars from 1m DataFrame.
    open_ts must be ET-aware. bar_start = ET wall-clock floor to N minutes.
    """
    # Vectorized floor: strip tz → compute floor minutes → re-localize
    local_naive = df1m["open_ts"].dt.tz_localize(None)   # values are ET wall-clock
    minutes = local_naive.dt.hour * 60 + local_naive.dt.minute
    floored_min = (minutes // n) * n
    floored_naive = local_naive.dt.normalize() + pd.to_timedelta(floored_min, unit="min")
    # Re-localize; ambiguous fall-back hours treated as DST-on (consistent grouping)
    bar_start = floored_naive.dt.tz_localize(
        "America/New_York", ambiguous=True, nonexistent="shift_forward"
    )
    df = df1m.copy()
    df["bar_start"] = bar_start

    agg = df.groupby("bar_start", sort=True).agg(
        open=("open", "first"),
        high=("high", "max"),
        low=("low", "min"),
        close=("close", "last"),
        volume=("volume", "sum"),
    ).reset_index()
    agg["bar_end"] = agg["bar_start"] + timedelta(minutes=n)
    return agg[["bar_start", "bar_end", "open", "high", "low", "close", "volume"]].reset_index(drop=True)

def _hm_to_min(s: str) -> int:
    h, m = s.split(":")
    return int(h) * 60 + int(m)

# ══════════════════════════════════════════════════════════════════════════════
# SECTION 3: FAIR VALUE GAP
# ══════════════════════════════════════════════════════════════════════════════

def is_bullish_fvg(c1_high: float, c3_low: float, strict: bool) -> bool:
    return c3_low > c1_high if strict else c3_low >= c1_high

def is_bearish_fvg(c3_high: float, c1_low: float, strict: bool) -> bool:
    return c3_high < c1_low if strict else c3_high <= c1_low

def is_fvg(c1_h: float, c1_l: float, c3_h: float, c3_l: float, strict: bool) -> int:
    """Compatibility alias used in tests.  Returns +1 bullish, -1 bearish, 0 none."""
    if is_bullish_fvg(c1_h, c3_l, strict):
        return 1
    if is_bearish_fvg(c3_h, c1_l, strict):
        return -1
    return 0

def is_consecutive_3(t1, t2, t3, n: int) -> bool:
    """True if t2 - t1 == n minutes and t3 - t2 == n minutes."""
    dt = timedelta(minutes=n)
    return (t2 - t1) == dt and (t3 - t2) == dt

# ══════════════════════════════════════════════════════════════════════════════
# SECTION 4: INDICATORS (on 15m all-hours bar series)
# ══════════════════════════════════════════════════════════════════════════════

def compute_ema(closes: np.ndarray, span: int) -> np.ndarray:
    """pandas ewm(span=span, adjust=False), seeded with first close."""
    alpha = 2.0 / (span + 1)
    ema = np.empty(len(closes))
    if len(closes) == 0:
        return ema
    ema[0] = closes[0]
    for i in range(1, len(closes)):
        ema[i] = closes[i] * alpha + ema[i - 1] * (1 - alpha)
    return ema

def compute_tr(high: np.ndarray, low: np.ndarray, close: np.ndarray) -> np.ndarray:
    """True range; first bar: H-L."""
    n = len(high)
    tr = np.empty(n)
    if n == 0:
        return tr
    tr[0] = high[0] - low[0]
    for i in range(1, n):
        tr[i] = max(high[i] - low[i],
                    abs(high[i] - close[i - 1]),
                    abs(low[i]  - close[i - 1]))
    return tr

def compute_atr(tr: np.ndarray, length: int, method: str) -> np.ndarray:
    atr = np.full(len(tr), np.nan)
    if len(tr) < length:
        return atr
    if method == "rma":
        atr[length - 1] = np.mean(tr[:length])
        for i in range(length, len(tr)):
            atr[i] = (atr[i - 1] * (length - 1) + tr[i]) / length
    else:  # sma
        for i in range(length - 1, len(tr)):
            atr[i] = np.mean(tr[i - length + 1 : i + 1])
    return atr

def compute_indicators(bars15: pd.DataFrame, cfg: dict) -> pd.DataFrame:
    b = bars15.copy()
    h = b["high"].values
    l = b["low"].values
    c = b["close"].values
    tr = compute_tr(h, l, c)
    b["tr"]       = tr
    b["atr"]      = compute_atr(tr, cfg["trend_atr_len"], cfg["atr_method"])
    b["ema_fast"] = compute_ema(c, cfg["trend_ema_fast"])
    b["ema_slow"] = compute_ema(c, cfg["trend_ema_slow"])
    return b

# ══════════════════════════════════════════════════════════════════════════════
# SECTION 5: OPENING DRIVE FVG
# ══════════════════════════════════════════════════════════════════════════════

def _build_bars10_index(bars10: pd.DataFrame) -> dict:
    """Pre-group 10m bars around the 09:10-09:40 check window by ET date."""
    idx: dict = {}
    target_times = {"09:10", "09:20", "09:30", "09:40"}
    for bs, o, h, l, c in zip(bars10["bar_start"], bars10["open"], bars10["high"], bars10["low"], bars10["close"]):
        t_str = bs.strftime("%H:%M")
        if t_str in target_times:
            d = bs.date()
            if d not in idx:
                idx[d] = {}
            idx[d][t_str] = {
                "bar_start": bs,
                "open": float(o),
                "high": float(h),
                "low": float(l),
                "close": float(c),
            }
    return idx

def _build_bars5_index(bars5: pd.DataFrame) -> dict:
    """Pre-group 5m opens by (ET_date, hhmm_int)."""
    idx: dict = {}
    for bs, o in zip(bars5["bar_start"], bars5["open"]):
        d = bs.date()
        if d not in idx:
            idx[d] = {}
        idx[d][bs.hour * 60 + bs.minute] = float(o)
    return idx

def od_signal_for_date(
    b10_day: dict,          # {"HH:MM": row}
    b5_day: dict,           # {hhmm_int: open_price}
    cfg: dict,
    et_date: date_t,
) -> Optional[dict]:
    """
    Returns OD signal dict or None.
    Keys: check_no, side, entry_time, entry_price, stop_price, target_price
    """
    tick     = cfg["tick"]
    strict   = cfg["fvg_strict"]
    stop_pct = cfg["od_stop_pct"]

    checks = [("09:10", "09:20", "09:30", "09:40"),
              ("09:20", "09:30", "09:40", "09:50")]

    for check_no, (c1_key, c2_key, c3_key, entry_key) in enumerate(checks, start=1):
        if c1_key not in b10_day or c2_key not in b10_day or c3_key not in b10_day:
            continue
        c1 = b10_day[c1_key]
        c2 = b10_day[c2_key]
        c3 = b10_day[c3_key]

        # Consecutive check
        if (c2["bar_start"] - c1["bar_start"] != timedelta(minutes=10) or
                c3["bar_start"] - c2["bar_start"] != timedelta(minutes=10)):
            continue

        # FVG
        bull = is_bullish_fvg(float(c1["high"]), float(c3["low"]), strict)
        bear = is_bearish_fvg(float(c3["high"]), float(c1["low"]), strict)
        if not bull and not bear:
            continue   # no FVG → try check 2

        # Entry bar (5m)
        entry_hm   = entry_key.split(":")
        entry_hhmm = int(entry_hm[0]) * 60 + int(entry_hm[1])
        if entry_hhmm not in b5_day:
            continue   # no_entry_bar → try check 2
        entry_price = b5_day[entry_hhmm]

        entry_dt = datetime(et_date.year, et_date.month, et_date.day,
                            int(entry_hm[0]), int(entry_hm[1]), tzinfo=ET)

        if bull:
            raw_stop   = entry_price * (1 - stop_pct)
            stop_price = round_tick(raw_stop, tick)
            return {"check_no": check_no, "side": "LONG",
                    "entry_time": entry_dt, "entry_price": entry_price,
                    "stop_price": stop_price, "target_price": None}
        else:  # bear
            raw_stop   = entry_price * (1 + stop_pct)
            stop_price = round_tick(raw_stop, tick)
            return {"check_no": check_no, "side": "SHORT",
                    "entry_time": entry_dt, "entry_price": entry_price,
                    "stop_price": stop_price, "target_price": None}

    return None

# ══════════════════════════════════════════════════════════════════════════════
# SECTION 6: TREND FVG SIGNALS  (pre-computed vectorized where possible)
# ══════════════════════════════════════════════════════════════════════════════

def compute_trend_signals(bars15: pd.DataFrame, b5_index: dict, cfg: dict) -> list:
    """
    b5_index : output of _build_bars5_index(bars5)
    Returns list of signal dicts sorted by entry_time.
    """
    if not cfg.get("enable_trend", True):
        return []

    b = bars15.reset_index(drop=True)

    # Extract numpy arrays and Python lists for maximum loop speed
    h_arr   = b["high"].values.astype(float)
    l_arr   = b["low"].values.astype(float)
    atr_arr = b["atr"].values.astype(float)
    ef_arr  = b["ema_fast"].values.astype(float)
    es_arr  = b["ema_slow"].values.astype(float)
    bs_arr  = b["bar_start"].tolist()
    be_arr  = b["bar_end"].tolist()

    strict     = cfg["fvg_strict"]
    atr_ref    = cfg["atr_ref"]
    atr_mult   = cfg["trend_atr_mult"]
    rr         = cfg["trend_rr"]
    warmup     = cfg["warmup_bars"]
    tick       = cfg["tick"]
    first_min  = _hm_to_min(cfg["trend_first_entry"])
    last_min   = _hm_to_min(cfg["trend_last_entry"])

    dt15 = timedelta(minutes=15)
    signals = []

    for k in range(max(2, warmup), len(bs_arr)):
        c1_start = bs_arr[k - 2]
        c2_start = bs_arr[k - 1]
        c3_start = bs_arr[k]
        if (c2_start - c1_start) != dt15 or (c3_start - c2_start) != dt15:
            continue

        # Bullish FVG: c3.low > c1.high
        c1_high = h_arr[k - 2]
        c3_low  = l_arr[k]
        if strict:
            if c3_low <= c1_high:
                continue
        else:
            if c3_low < c1_high:
                continue

        # Displacement: c2 bar range >= atr_mult * ATR
        atr_val = atr_arr[k - 1] if atr_ref == "c2" else atr_arr[k - 2]
        if np.isnan(atr_val):
            continue
        c2_range = h_arr[k - 1] - l_arr[k - 1]
        if c2_range < atr_mult * atr_val:
            continue

        # Trend: ema_fast[k] > ema_slow[k] (strict)
        if np.isnan(ef_arr[k]) or np.isnan(es_arr[k]):
            continue
        if ef_arr[k] <= es_arr[k]:
            continue

        # Entry time = c3.bar_end
        entry_time = be_arr[k]
        entry_mm   = entry_time.hour * 60 + entry_time.minute
        if entry_mm < first_min or entry_mm > last_min:
            continue

        # 5m entry bar
        entry_date = entry_time.date()
        b5_day     = b5_index.get(entry_date)
        if b5_day is None or entry_mm not in b5_day:
            continue   # no_entry_bar
        entry_price = b5_day[entry_mm]

        # Stop = c1.low - tick
        stop_price = float(l_arr[k - 2]) - tick
        if entry_price <= stop_price:
            continue   # entry_at_or_below_stop

        R            = entry_price - stop_price
        target_price = entry_price + rr * R

        signals.append({
            "date":         entry_date,
            "entry_time":   entry_time,
            "entry_price":  entry_price,
            "stop_price":   stop_price,
            "target_price": target_price,
            "bar_idx":      k,
        })

    return signals

# ══════════════════════════════════════════════════════════════════════════════
# SECTION 7: TRADE SIMULATION ON 1m BARS
# ══════════════════════════════════════════════════════════════════════════════

def round_tick(price: float, tick: float = 0.25) -> float:
    """Round to nearest tick, halves round up."""
    return math.floor(price / tick + 0.5) * tick

def simulate_trade(
    side: str,
    entry_time: datetime,
    entry_price: float,
    stop_price: float,
    target_price: Optional[float],
    bars1m_day,   # list of (open_ts, open, high, low, close) or pd.DataFrame
    cfg: dict,
    cutoff_time: Optional[datetime] = None,
) -> dict:
    """
    Simulate one trade on 1m bars for the day.
    bars1m_day: list of (open_ts, open, high, low, close) or DataFrame.
    """
    tick = cfg["tick"]
    slip = cfg["slip_ticks"] * tick

    if side == "LONG":
        entry_fill = entry_price + slip
        exit_slip  = slip
    else:
        entry_fill = entry_price - slip
        exit_slip  = slip

    day_end = datetime(entry_time.year, entry_time.month, entry_time.day,
                       16, 0, tzinfo=ET)
    effective_cutoff = min(day_end, cutoff_time) if cutoff_time is not None else day_end

    if isinstance(bars1m_day, pd.DataFrame):
        day_bars = list(zip(bars1m_day["open_ts"], bars1m_day["open"],
                            bars1m_day["high"], bars1m_day["low"], bars1m_day["close"]))
    else:
        day_bars = bars1m_day

    exit_price_raw: Optional[float] = None
    exit_time:      Optional[datetime] = None
    exit_reason:    Optional[str]  = None

    for bar_ts, o, h, l, c in day_bars:
        if bar_ts < entry_time:
            continue
        if bar_ts >= effective_cutoff:
            break

        o = float(o); h = float(h); l = float(l)

        if side == "LONG":
            if o <= stop_price:
                exit_price_raw = o;          exit_reason = "STOP"; exit_time = bar_ts; break
            if l <= stop_price:
                exit_price_raw = stop_price; exit_reason = "STOP"; exit_time = bar_ts; break
            if target_price is not None:
                if o >= target_price:
                    exit_price_raw = o;            exit_reason = "TARGET"; exit_time = bar_ts; break
                if h >= target_price:
                    exit_price_raw = target_price; exit_reason = "TARGET"; exit_time = bar_ts; break
        else:  # SHORT
            if o >= stop_price:
                exit_price_raw = o;          exit_reason = "STOP"; exit_time = bar_ts; break
            if h >= stop_price:
                exit_price_raw = stop_price; exit_reason = "STOP"; exit_time = bar_ts; break

    if exit_reason is None:
        if cutoff_time is not None:
            return {"exit_price_raw": None, "exit_time": None, "exit_reason": None,
                    "entry_fill": entry_fill, "open": True}
        rth = [b for b in day_bars if b[0] < day_end]
        if not rth:
            return {"exit_price_raw": None, "exit_time": None, "exit_reason": "NO_BARS",
                    "entry_fill": entry_fill, "open": False}
        last_bar       = rth[-1]
        exit_price_raw = float(last_bar[4]) # close
        exit_time      = last_bar[0] + timedelta(minutes=1)
        exit_reason    = "TIME"

    if side == "LONG":
        exit_fill = exit_price_raw - exit_slip
    else:
        exit_fill = exit_price_raw + exit_slip

    if side == "LONG":
        points = exit_fill - entry_fill
    else:
        points = entry_fill - exit_fill

    fees       = cfg["fee_per_side"] * 2
    gross_usd  = points * cfg["point_value"]
    net_usd    = gross_usd - fees

    exit_bar_close = (exit_time + timedelta(minutes=1)
                      if exit_reason in ("STOP", "TARGET") else exit_time)

    return {
        "exit_price_raw": exit_price_raw,
        "exit_fill":      exit_fill,
        "exit_time":      exit_time,
        "exit_bar_close": exit_bar_close,
        "exit_reason":    exit_reason,
        "entry_fill":     entry_fill,
        "points":         points,
        "gross_usd":      gross_usd,
        "fees_usd":       fees,
        "net_usd":        net_usd,
        "open":           False,
    }

# Alias used in some tests
simulate_trade_1m = simulate_trade

# ══════════════════════════════════════════════════════════════════════════════
# SECTION 8: DUO ARBITRATION ENGINE
# ══════════════════════════════════════════════════════════════════════════════

def run_day(
    et_date:      date_t,
    od_sig:       Optional[dict],
    trend_sigs:   list,          # sorted by entry_time, all for this date
    bars1m_day:   pd.DataFrame,
    cfg:          dict,
    roll_dates:   set,
) -> tuple:
    """Returns (trades_list, signals_log_list) for one trading day."""

    enable_od    = cfg["enable_od"]
    enable_trend = cfg["enable_trend"]
    tick         = cfg["tick"]
    near_roll    = int(et_date in roll_dates)

    trades   = []
    sig_log  = []
    _id      = [0]

    def _next_id():
        _id[0] += 1
        return _id[0]

    def _record(source, side, entry_time, entry_fill, stop_price, target_price,
                res, check_no=None, after_od_stopout=0, merge_entry_time=None):
        """Append a completed trade to trades list."""
        if res.get("open") or res.get("exit_reason") in (None, "NO_BARS"):
            return
        trades.append({
            "trade_id":          _next_id(),
            "date":              et_date.isoformat(),
            "source":            source,
            "side":              side,
            "entry_ts":          (merge_entry_time or entry_time).isoformat(),
            "entry_price":       round(entry_fill, 2),
            "exit_ts":           res["exit_time"].isoformat(),
            "exit_price":        round(res["exit_fill"], 2),
            "exit_reason":       res["exit_reason"],
            "stop_price":        round(stop_price, 2),
            "target_price":      round(target_price, 2) if target_price is not None else "",
            "points":            round(res["points"], 2),
            "gross_usd":         round(res["gross_usd"], 2),
            "fees_usd":          round(res["fees_usd"], 2),
            "net_usd":           round(res["net_usd"], 2),
            "signal_check_no":   str(check_no) if check_no is not None else "",
            "after_od_stopout":  after_od_stopout,
            "near_roll":         near_roll,
        })

    # ── TREND-ONLY day (no OD signal) ──────────────────────────────────────
    if not enable_od or od_sig is None:
        if not enable_trend:
            return trades, sig_log
        prev_exit_close = None
        for ts in trend_sigs:
            if prev_exit_close and ts["entry_time"] < prev_exit_close:
                sig_log.append({"date": et_date, "type": "TREND",
                                 "entry_time": ts["entry_time"],
                                 "reason": "blocked_by_open_position"})
                continue
            res = simulate_trade("LONG", ts["entry_time"], ts["entry_price"],
                                 ts["stop_price"], ts["target_price"], bars1m_day, cfg)
            _record("TREND", "LONG", ts["entry_time"], res["entry_fill"],
                    ts["stop_price"], ts["target_price"], res)
            if not res.get("open") and res.get("exit_reason") not in (None, "NO_BARS"):
                prev_exit_close = res.get("exit_bar_close")
        return trades, sig_log

    # ── OD day ─────────────────────────────────────────────────────────────
    T_od    = od_sig["entry_time"]
    od_side = od_sig["side"]
    od_stop = od_sig["stop_price"]

    # 3a: Trend signals that start BEFORE T_od
    open_trend   = None
    prev_ec      = None
    if enable_trend:
        pre_sigs = [s for s in trend_sigs if s["entry_time"] < T_od]
        for ts in pre_sigs:
            if prev_ec and ts["entry_time"] < prev_ec:
                sig_log.append({"date": et_date, "type": "TREND",
                                 "entry_time": ts["entry_time"],
                                 "reason": "blocked_by_open_position"})
                continue
            res = simulate_trade("LONG", ts["entry_time"], ts["entry_price"],
                                 ts["stop_price"], ts["target_price"],
                                 bars1m_day, cfg, cutoff_time=T_od)
            if res.get("open"):
                # Trend trade still open when OD fires
                open_trend = {"sig": ts, "entry_fill": res["entry_fill"]}
                break
            _record("TREND", "LONG", ts["entry_time"], res["entry_fill"],
                    ts["stop_price"], ts["target_price"], res)
            if not res.get("open") and res.get("exit_reason") not in (None, "NO_BARS"):
                prev_ec = res.get("exit_bar_close")

    # 1m bar open at T_od
    if isinstance(bars1m_day, pd.DataFrame):
        od_bar_rows = bars1m_day[bars1m_day["open_ts"] == T_od]
        od_bar_open = float(od_bar_rows.iloc[0]["open"]) if len(od_bar_rows) else od_sig["entry_price"]
    else:
        matching = [b for b in bars1m_day if b[0] == T_od]
        od_bar_open = float(matching[0][1]) if matching else od_sig["entry_price"]

    final_res = None

    if open_trend is None:
        # 3b: Simple OD entry (no open trend)
        res = simulate_trade(od_side, T_od, od_sig["entry_price"], od_stop, None,
                             bars1m_day, cfg)
        _record("OD", od_side, T_od, res["entry_fill"],
                od_stop, None, res, check_no=od_sig["check_no"])
        final_res = res

    elif od_side == "LONG":
        # 3c: MERGE — both are LONG, fold into one trade
        ts = open_trend["sig"]
        if cfg["merge_anchor"] == "trend_entry":
            anchor = open_trend["entry_fill"]
        else:   # "od_signal"
            anchor = od_sig["entry_price"]

        raw_merge_stop = anchor * (1 - cfg["od_stop_pct"])
        merge_stop     = round_tick(raw_merge_stop, tick)

        if od_bar_open <= merge_stop:
            # Already through merge stop at T_od bar open → exit at bar open
            slip       = cfg["slip_ticks"] * tick
            exit_fill  = od_bar_open - slip
            points     = exit_fill - open_trend["entry_fill"]
            fees       = cfg["fee_per_side"] * 2
            gross      = points * cfg["point_value"]
            net        = gross - fees
            trades.append({
                "trade_id":         _next_id(),
                "date":             et_date.isoformat(),
                "source":           "MERGED",
                "side":             "LONG",
                "entry_ts":         ts["entry_time"].isoformat(),
                "entry_price":      round(open_trend["entry_fill"], 2),
                "exit_ts":          T_od.isoformat(),
                "exit_price":       round(exit_fill, 2),
                "exit_reason":      "STOP",
                "stop_price":       round(merge_stop, 2),
                "target_price":     "",
                "points":           round(points, 2),
                "gross_usd":        round(gross, 2),
                "fees_usd":         round(fees, 2),
                "net_usd":          round(net, 2),
                "signal_check_no":  "",
                "after_od_stopout": 0,
                "near_roll":        near_roll,
            })
            final_res = {"exit_reason": "STOP",
                         "exit_bar_close": T_od + timedelta(minutes=1),
                         "net_usd": net}
        else:
            # Simulate from T_od with new merged stop (no target)
            res = simulate_trade("LONG", T_od, od_bar_open, merge_stop, None,
                                 bars1m_day, cfg)
            slip      = cfg["slip_ticks"] * tick
            exit_fill = res.get("exit_fill", od_bar_open - slip)
            points    = exit_fill - open_trend["entry_fill"]
            fees      = cfg["fee_per_side"] * 2
            gross     = points * cfg["point_value"]
            net       = gross - fees
            if res.get("exit_reason") not in (None, "NO_BARS"):
                trades.append({
                    "trade_id":         _next_id(),
                    "date":             et_date.isoformat(),
                    "source":           "MERGED",
                    "side":             "LONG",
                    "entry_ts":         ts["entry_time"].isoformat(),
                    "entry_price":      round(open_trend["entry_fill"], 2),
                    "exit_ts":          res["exit_time"].isoformat(),
                    "exit_price":       round(exit_fill, 2),
                    "exit_reason":      res["exit_reason"],
                    "stop_price":       round(merge_stop, 2),
                    "target_price":     "",
                    "points":           round(points, 2),
                    "gross_usd":        round(gross, 2),
                    "fees_usd":         round(fees, 2),
                    "net_usd":          round(net, 2),
                    "signal_check_no":  "",
                    "after_od_stopout": 0,
                    "near_roll":        near_roll,
                })
            final_res = res

    else:
        # 3d: FLIP — open Trend LONG conflicts with OD SHORT
        slip       = cfg["slip_ticks"] * tick
        flip_exit  = od_bar_open - slip    # long closes with sell slip
        points     = flip_exit - open_trend["entry_fill"]
        fees       = cfg["fee_per_side"] * 2
        gross      = points * cfg["point_value"]
        net        = gross - fees
        ts         = open_trend["sig"]
        trades.append({
            "trade_id":         _next_id(),
            "date":             et_date.isoformat(),
            "source":           "TREND",
            "side":             "LONG",
            "entry_ts":         ts["entry_time"].isoformat(),
            "entry_price":      round(open_trend["entry_fill"], 2),
            "exit_ts":          T_od.isoformat(),
            "exit_price":       round(flip_exit, 2),
            "exit_reason":      "FLIP",
            "stop_price":       round(ts["stop_price"], 2),
            "target_price":     round(ts["target_price"], 2) if ts.get("target_price") else "",
            "points":           round(points, 2),
            "gross_usd":        round(gross, 2),
            "fees_usd":         round(fees, 2),
            "net_usd":          round(net, 2),
            "signal_check_no":  "",
            "after_od_stopout": 0,
            "near_roll":        near_roll,
        })
        # OD short enters at same od_bar_open (the flip price)
        raw_od_stop  = od_bar_open * (1 + cfg["od_stop_pct"])
        od_short_stop = round_tick(raw_od_stop, tick)
        res = simulate_trade("SHORT", T_od, od_bar_open, od_short_stop, None,
                             bars1m_day, cfg)
        _record("OD", "SHORT", T_od, res["entry_fill"],
                od_short_stop, None, res, check_no=od_sig["check_no"])
        final_res = res

    # 3e/3f: After OD stopout, allow more Trend signals (if flag set)
    if enable_trend and cfg["trend_after_od_stopout"]:
        if final_res and final_res.get("exit_reason") == "STOP":
            after_close = final_res.get("exit_bar_close")
            post_sigs   = [s for s in trend_sigs
                           if s["entry_time"] > T_od and
                           (after_close is None or s["entry_time"] >= after_close)]
            prev_ec = after_close
            for ts in post_sigs:
                if prev_ec and ts["entry_time"] < prev_ec:
                    sig_log.append({"date": et_date, "type": "TREND",
                                     "entry_time": ts["entry_time"],
                                     "reason": "blocked_by_open_position"})
                    continue
                res = simulate_trade("LONG", ts["entry_time"], ts["entry_price"],
                                     ts["stop_price"], ts["target_price"], bars1m_day, cfg)
                _record("TREND", "LONG", ts["entry_time"], res["entry_fill"],
                        ts["stop_price"], ts["target_price"], res, after_od_stopout=1)
                if not res.get("open") and res.get("exit_reason") not in (None, "NO_BARS"):
                    prev_ec = res.get("exit_bar_close")

    return trades, sig_log

# ══════════════════════════════════════════════════════════════════════════════
# METRICS  (Section 13)
# ══════════════════════════════════════════════════════════════════════════════

def compute_metrics(trades: list, cfg: dict) -> dict:
    if not trades:
        return {"trade_count": 0, "net_usd": 0, "gross_points": 0,
                "win_rate": 0, "profit_factor": 0, "per_year": {},
                "green_years": 0, "max_dd_closed": 0,
                "net_last_365": 0, "net_last_100": 0,
                "net_first_half": 0, "net_second_half": 0,
                "net_near_roll": 0, "net_no_roll": 0,
                "od_long_net": 0, "od_short_net": 0}

    net = [t["net_usd"] for t in trades]
    pts = [t["points"]  for t in trades]

    wins         = sum(1 for n in net if n > 0)
    gross_profit = sum(n for n in net if n > 0)
    gross_loss   = sum(n for n in net if n < 0)
    pf           = abs(gross_profit / gross_loss) if gross_loss != 0 else float("inf")

    cum  = np.cumsum(net)
    peak = np.maximum.accumulate(np.concatenate([[0.0], cum]))
    dd   = peak[:-1] - cum
    max_dd = float(np.max(dd)) if len(dd) else 0.0

    # Per-year
    per_year: dict = {}
    for t in trades:
        yr = t["date"][:4]
        per_year.setdefault(yr, {"net": 0.0, "count": 0})
        per_year[yr]["net"]   += t["net_usd"]
        per_year[yr]["count"] += 1
    green_years = sum(1 for v in per_year.values() if v["net"] > 0)

    # Last 365 days
    last_date   = datetime.strptime(max(t["date"] for t in trades), "%Y-%m-%d").date()
    cutoff_365  = last_date - timedelta(days=365)
    net_365     = sum(t["net_usd"] for t in trades
                      if datetime.strptime(t["date"], "%Y-%m-%d").date() > cutoff_365)

    net_100 = sum(t["net_usd"] for t in trades[-100:])

    n       = len(trades)
    net_fh  = sum(t["net_usd"] for t in trades[: n // 2])
    net_sh  = sum(t["net_usd"] for t in trades[n // 2 :])

    net_nr  = sum(t["net_usd"] for t in trades if t.get("near_roll") == 1)
    net_no  = sum(t["net_usd"] for t in trades if t.get("near_roll") == 0)

    od_long  = sum(t["net_usd"] for t in trades if t["source"] == "OD" and t["side"] == "LONG")
    od_short = sum(t["net_usd"] for t in trades if t["source"] == "OD" and t["side"] == "SHORT")

    return {
        "trade_count":    len(trades),
        "net_usd":        round(sum(net), 2),
        "gross_points":   round(sum(pts), 2),
        "win_rate":       round(wins / len(trades) * 100, 2),
        "profit_factor":  round(pf, 3),
        "per_year":       per_year,
        "green_years":    green_years,
        "max_dd_closed":  round(max_dd, 2),
        "net_last_365":   round(net_365, 2),
        "net_last_100":   round(net_100, 2),
        "net_first_half": round(net_fh, 2),
        "net_second_half":round(net_sh, 2),
        "net_near_roll":  round(net_nr, 2),
        "net_no_roll":    round(net_no, 2),
        "od_long_net":    round(od_long, 2),
        "od_short_net":   round(od_short, 2),
    }

# ══════════════════════════════════════════════════════════════════════════════
# MAIN BACKTEST RUNNER
# ══════════════════════════════════════════════════════════════════════════════

def prepare_backtest_data(df1m: pd.DataFrame) -> dict:
    """Pre-compute bars, indices, and daily partitions once for repeated runs."""
    print("  Building 5m bars …")
    bars5  = build_bars(df1m, 5)
    print("  Building 10m bars …")
    bars10 = build_bars(df1m, 10)
    print("  Building 15m bars …")
    bars15 = build_bars(df1m, 15)

    print("  Indexing bar lookups …")
    b5_index  = _build_bars5_index(bars5)
    b10_index = _build_bars10_index(bars10)

    print("  Grouping 1m bars by date …")
    df1m_sorted = df1m.sort_values("open_ts").reset_index(drop=True)
    df1m_by_date = {}
    rth_min_start = 9 * 60 + 30
    rth_min_end   = 16 * 60
    rth_dates = set()

    for ts, o, h, l, c in zip(df1m_sorted["open_ts"], df1m_sorted["open"],
                              df1m_sorted["high"], df1m_sorted["low"], df1m_sorted["close"]):
        d = ts.date()
        if d not in df1m_by_date:
            df1m_by_date[d] = []
        df1m_by_date[d].append((ts, float(o), float(h), float(l), float(c)))
        m = ts.hour * 60 + ts.minute
        if rth_min_start <= m < rth_min_end:
            rth_dates.add(d)

    trading_dates = sorted(rth_dates)
    print(f"  Prepared data: {len(trading_dates)} trading dates.")

    return {
        "bars5": bars5,
        "bars10": bars10,
        "bars15": bars15,
        "b5_index": b5_index,
        "b10_index": b10_index,
        "df1m_by_date": df1m_by_date,
        "trading_dates": trading_dates,
    }

def run_backtest_with_bars(
    data: dict,
    cfg: dict,
    roll_dates_set: set,
    output_dir: Optional[Path] = None,
) -> dict:
    """Run backtest using pre-built bars (very fast for parameter sweeps)."""
    enable_od    = cfg.get("enable_od", True)
    enable_trend = cfg.get("enable_trend", True)

    b5_index      = data["b5_index"]
    b10_index     = data["b10_index"]
    df1m_by_date  = data["df1m_by_date"]
    trading_dates = data["trading_dates"]

    if enable_trend:
        bars15 = compute_indicators(data["bars15"], cfg)
        all_trend_sigs = compute_trend_signals(bars15, b5_index, cfg)
        trend_by_date: dict = defaultdict(list)
        for s in all_trend_sigs:
            trend_by_date[s["date"]].append(s)
    else:
        trend_by_date = {}

    all_trades: list = []
    all_sig_log: list = []

    for et_date in trading_dates:
        bars1m_day = df1m_by_date.get(et_date, [])
        if len(bars1m_day) == 0:
            continue

        od_sig = None
        if cfg["enable_od"]:
            b10_day = b10_index.get(et_date, {})
            b5_day  = b5_index.get(et_date, {})
            od_sig  = od_signal_for_date(b10_day, b5_day, cfg, et_date)

        t_sigs = sorted(trend_by_date.get(et_date, []),
                        key=lambda s: s["entry_time"])

        trades_day, sl_day = run_day(et_date, od_sig, t_sigs, bars1m_day,
                                     cfg, roll_dates_set)
        all_trades.extend(trades_day)
        all_sig_log.extend(sl_day)

    for i, t in enumerate(all_trades, 1):
        t["trade_id"] = i

    result = compute_metrics(all_trades, cfg)
    result["trades"] = all_trades
    result["signals_log"] = all_sig_log

    if output_dir is not None:
        h = config_hash(cfg)
        run_dir = output_dir / f"run_{h}"
        run_dir.mkdir(parents=True, exist_ok=True)

        (run_dir / "config.json").write_text(json.dumps(cfg, indent=2, default=str))

        if all_trades:
            keys = list(all_trades[0].keys())
            with open(run_dir / "trades.csv", "w", newline="") as f:
                w = csv.DictWriter(f, fieldnames=keys)
                w.writeheader()
                w.writerows(all_trades)

        if all_sig_log:
            with open(run_dir / "signals_log.csv", "w", newline="") as f:
                w = csv.DictWriter(f, fieldnames=["date", "type", "entry_time", "reason"],
                                   extrasaction="ignore")
                w.writeheader()
                w.writerows(all_sig_log)

        summary = {k: v for k, v in result.items() if k not in ("trades", "signals_log")}
        (run_dir / "summary.json").write_text(json.dumps(summary, indent=2, default=str))

        lines = ["# FVG Duo Run Summary\n",
                 f"Config hash: `{h}`\n",
                 "| Metric | Value |", "|---|---|"]
        for k, v in summary.items():
            if k == "per_year":
                continue
            lines.append(f"| {k} | {v} |")
        (run_dir / "summary.md").write_text("\n".join(lines))

        result["run_dir"] = str(run_dir)

    return result

def run_backtest(
    df1m:           pd.DataFrame,
    cfg:            dict,
    roll_dates_set: set,
    output_dir:     Optional[Path] = None,
) -> dict:
    data = prepare_backtest_data(df1m)
    return run_backtest_with_bars(data, cfg, roll_dates_set, output_dir)
