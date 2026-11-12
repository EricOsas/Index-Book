"""
Unit tests T1-T12 for FVG Duo Engine — Spec v1.txt Section 11.
All tests use the actual fvg_duo_engine API.
"""
import os, sys, math, glob
from datetime import datetime, timedelta, date as date_t, timezone as _tz_mod
from zoneinfo import ZoneInfo
import numpy as np
import pandas as pd

sys.path.insert(0, os.path.abspath(os.path.join(os.path.dirname(__file__), "..")))

from fvg_duo_engine import (
    is_fvg, is_bullish_fvg, is_bearish_fvg, is_consecutive_3,
    round_tick, simulate_trade, run_backtest,
    build_bars, compute_indicators, compute_trend_signals,
    od_signal_for_date, _build_bars5_index, _build_bars10_index,
    DEFAULT_CONFIG, NY_TZ, ET,
)
sys.path.insert(0, os.path.abspath(os.path.join(os.path.dirname(__file__), "..", "vendor")))
import ncd as ncd_mod

# ─────────────────────────────────────────────────────────────────────────────
# HELPERS
# ─────────────────────────────────────────────────────────────────────────────

def make_test_cfg(**overrides) -> dict:
    cfg = dict(DEFAULT_CONFIG)
    cfg["warmup_bars"] = 0   # synthetic data: skip warmup
    cfg["slip_ticks"]  = 0   # zero slip for deterministic P&L checks
    cfg.update(overrides)
    return cfg


def _make_1m_day(date_str: str, bars_dict: dict,
                 default_price: float = 1000.0,
                 start_hm: tuple = (8, 30),
                 end_hm:   tuple = (16, 0)) -> pd.DataFrame:
    """
    Build a 1m DataFrame for one trading day with ET-aware open_ts.
    bars_dict: {"HH:MM": (open, high, low, close, volume)}
    """
    rows = []
    base = datetime.strptime(date_str, "%Y-%m-%d")
    cur  = base.replace(hour=start_hm[0], minute=start_hm[1], tzinfo=ET)
    end  = base.replace(hour=end_hm[0],   minute=end_hm[1],   tzinfo=ET)
    while cur < end:
        key = cur.strftime("%H:%M")
        if key in bars_dict:
            o, h, l, c, v = bars_dict[key]
        else:
            o = default_price; h = default_price + 0.25
            l = default_price - 0.25; c = default_price; v = 100
        rows.append({"open_ts": cur,
                     "open": float(o), "high": float(h),
                     "low":  float(l), "close": float(c),
                     "volume": int(v), "contract": "TEST"})
        cur += timedelta(minutes=1)
    return pd.DataFrame(rows)


def _run_one_day(df_day: pd.DataFrame, cfg: dict) -> list:
    """Run the full engine on a single synthetic day; return list of trades."""
    return run_backtest(df_day, cfg, set())["trades"]


# ─────────────────────────────────────────────────────────────────────────────
# T1: FVG strict vs non-strict
# ─────────────────────────────────────────────────────────────────────────────
def test_t1_fvg_strict_vs_nonstrict():
    assert is_fvg(100, 90, 110, 101, strict=True)  ==  1, "T1 bullish gap strict"
    assert is_fvg(100, 90, 110, 101, strict=False) ==  1, "T1 bullish gap non-strict"
    assert is_fvg(100, 90, 110, 100, strict=True)  ==  0, "T1 bullish touch strict → 0"
    assert is_fvg(100, 90, 110, 100, strict=False) ==  1, "T1 bullish touch non-strict → 1"
    assert is_fvg(110, 100,  99,  90, strict=True)  == -1, "T1 bearish gap strict"
    assert is_fvg(110, 100,  99,  90, strict=False) == -1, "T1 bearish gap non-strict"
    assert is_fvg(110, 100, 100,  90, strict=True)  ==  0, "T1 bearish touch strict → 0"
    assert is_fvg(110, 100, 100,  90, strict=False) == -1, "T1 bearish touch non-strict → -1"
    print("PASS: T1")


# ─────────────────────────────────────────────────────────────────────────────
# T2: rising highs/lows with NO gap → not an FVG
# ─────────────────────────────────────────────────────────────────────────────
def test_t2_rising_highs_lows_no_gap():
    assert is_fvg(100, 95, 104, 99, strict=True)  == 0, "T2 strict overlap"
    assert is_fvg(100, 95, 104, 99, strict=False) == 0, "T2 non-strict overlap"
    print("PASS: T2")


# ─────────────────────────────────────────────────────────────────────────────
# T3: consecutive check
# ─────────────────────────────────────────────────────────────────────────────
def test_t3_consecutive():
    t1 = datetime(2024, 1, 15,  9, 10, tzinfo=ET)
    t2 = datetime(2024, 1, 15,  9, 20, tzinfo=ET)
    t3 = datetime(2024, 1, 15,  9, 30, tzinfo=ET)
    assert is_consecutive_3(t1, t2,  t3,  10) is True,  "T3 valid"
    assert is_consecutive_3(t1, t2 + timedelta(minutes=5), t3, 10) is False, "T3 bad t2"
    assert is_consecutive_3(t1, t2,  t3 + timedelta(minutes=10), 10) is False, "T3 bad t3"
    print("PASS: T3")


# ─────────────────────────────────────────────────────────────────────────────
# T4: OD check-1 fires first; check-2 fallback at 09:50
# ─────────────────────────────────────────────────────────────────────────────
def test_t4_od_checks():
    cfg = make_test_cfg(enable_od=True, enable_trend=False)

    # Case A: Check 1 fires (c1=09:10 high=1000, c3=09:30 low=1002)
    bars_a: dict = {}
    for m in range(10, 20): bars_a[f"09:{m:02d}"] = (998, 1000, 995, 999, 50)
    for m in range(20, 30): bars_a[f"09:{m:02d}"] = (1000, 1001, 999, 1001, 50)
    for m in range(30, 40): bars_a[f"09:{m:02d}"] = (1002, 1005, 1002, 1004, 50)
    for m in range(40, 50): bars_a[f"09:{m:02d}"] = (1005, 1008, 1005, 1007, 50)
    trades_a = _run_one_day(_make_1m_day("2024-01-15", bars_a), cfg)
    assert len(trades_a) == 1,                           f"T4-A trades {len(trades_a)}"
    assert trades_a[0]["signal_check_no"] == "1",        f"T4-A check# {trades_a[0]['signal_check_no']}"
    assert "09:40:00" in trades_a[0]["entry_ts"],        f"T4-A entry {trades_a[0]['entry_ts']}"

    # Case B: Check 1 no FVG (c1 high=1005 overlaps c3 low=1002); Check 2 fires (c1=09:20 high=1000, c3=09:40 low=1002)
    bars_b: dict = {}
    for m in range(10, 20): bars_b[f"09:{m:02d}"] = (998, 1005, 995, 999, 50)
    for m in range(20, 30): bars_b[f"09:{m:02d}"] = (999, 1000, 998, 999, 50)
    for m in range(30, 40): bars_b[f"09:{m:02d}"] = (1002, 1004, 1002, 1003, 50)
    for m in range(40, 50): bars_b[f"09:{m:02d}"] = (1002, 1006, 1002, 1005, 50)
    trades_b = _run_one_day(_make_1m_day("2024-01-16", bars_b), cfg)
    assert len(trades_b) == 1,                           f"T4-B trades {len(trades_b)}"
    assert trades_b[0]["signal_check_no"] == "2",        f"T4-B check# {trades_b[0]['signal_check_no']}"
    assert "09:50:00" in trades_b[0]["entry_ts"],        f"T4-B entry {trades_b[0]['entry_ts']}"
    print("PASS: T4")


# ─────────────────────────────────────────────────────────────────────────────
# T5: Stop and target in same bar → STOP wins
# ─────────────────────────────────────────────────────────────────────────────
def test_t5_stop_beats_target_same_bar():
    cfg  = make_test_cfg()
    base = datetime(2024, 1, 15, 9, 30, tzinfo=ET)
    bars = _make_1m_day("2024-01-15", {"09:30": (100, 115, 90, 100, 1)},
                        default_price=100.0, start_hm=(9, 30))
    res = simulate_trade("LONG", base, 100.0, 95.0, 105.0, bars, cfg)
    assert res["exit_reason"]    == "STOP", f"T5 reason {res['exit_reason']}"
    assert res["exit_price_raw"] == 95.0,   f"T5 price  {res['exit_price_raw']}"
    print("PASS: T5")


# ─────────────────────────────────────────────────────────────────────────────
# T6: Gap through stop → exit at bar.open
# ─────────────────────────────────────────────────────────────────────────────
def test_t6_gap_through_stop():
    cfg  = make_test_cfg()
    base = datetime(2024, 1, 15, 9, 30, tzinfo=ET)
    bars = _make_1m_day("2024-01-15", {"09:30": (92, 93, 88, 89, 1)},
                        default_price=92.0, start_hm=(9, 30))
    res = simulate_trade("LONG", base, 100.0, 95.0, 110.0, bars, cfg)
    assert res["exit_reason"]    == "STOP", f"T6 reason {res['exit_reason']}"
    assert res["exit_price_raw"] == 92.0,   f"T6 price  {res['exit_price_raw']}"
    print("PASS: T6")


# ─────────────────────────────────────────────────────────────────────────────
# T7: Time exit
# ─────────────────────────────────────────────────────────────────────────────
def test_t7_time_exit():
    cfg     = make_test_cfg()
    entry_t = datetime(2024, 1, 15, 9, 30, tzinfo=ET)

    # Normal day: 15:59 bar close = 105.25
    bars_n = _make_1m_day("2024-01-15", {"15:59": (100, 101, 99, 105.25, 1)},
                           default_price=100.0)
    res_n  = simulate_trade("LONG", entry_t, 100.0, 50.0, None, bars_n, cfg)
    assert res_n["exit_reason"] == "TIME",  f"T7-normal {res_n['exit_reason']}"
    assert abs(res_n["exit_price_raw"] - 105.25) < 1e-9
    assert res_n["exit_time"] == datetime(2024, 1, 15, 16, 0, tzinfo=ET)

    # Early-close: last bar 12:59 close = 102.50
    bars_e = _make_1m_day("2024-01-15", {"12:59": (100, 101, 99, 102.50, 1)},
                           default_price=100.0, end_hm=(13, 0))
    res_e  = simulate_trade("LONG", entry_t, 100.0, 50.0, None, bars_e, cfg)
    assert res_e["exit_reason"] == "TIME",  f"T7-early {res_e['exit_reason']}"
    assert abs(res_e["exit_price_raw"] - 102.50) < 1e-9
    assert res_e["exit_time"] == datetime(2024, 1, 15, 13, 0, tzinfo=ET)
    print("PASS: T7")


# ─────────────────────────────────────────────────────────────────────────────
# T8: MERGE — Trend long open + OD long → one MERGED trade
# ─────────────────────────────────────────────────────────────────────────────
def test_t8_merge():
    bars: dict = {}
    for m in range(45, 60): bars[f"08:{m:02d}"] = (992, 995, 990, 994, 100)   # 15m c1 high=995
    for m in range( 0, 15): bars[f"09:{m:02d}"] = (994, 1010, 993, 1008, 100) # 15m c2 displacement
    for m in range(15, 30): bars[f"09:{m:02d}"] = (1005, 1012, 996, 1010, 100)# 15m c3 low=996>995
    for m in range(30, 35): bars[f"09:{m:02d}"] = (1000, 1002, 998, 1001, 100)# 5m at 09:30 → entry 1000
    # 10m OD check-2 bars (c1=09:20 h=1000, c3=09:40 low=1002→FVG with c1)
    for m in range(20, 30): bars[f"09:{m:02d}"] = (999, 1000, 998, 1000, 100)
    for m in range(30, 40): bars[f"09:{m:02d}"] = (1000, 1004, 999, 1003, 100)
    for m in range(40, 50): bars[f"09:{m:02d}"] = (1004, 1008, 1003, 1006, 100)
    # rest of day: stable above stop (985)
    for h in range(10, 16):
        for m in range(60): bars[f"{h:02d}:{m:02d}"] = (1050, 1051, 1049, 1050, 50)
    bars["15:59"] = (1050, 1051, 1049, 1050, 50)

    cfg = make_test_cfg(enable_od=True, enable_trend=True,
                        trend_atr_len=1, trend_ema_fast=2, trend_ema_slow=5,
                        trend_atr_mult=0.5, merge_anchor="trend_entry", od_stop_pct=0.015)
    trades = _run_one_day(_make_1m_day("2024-01-15", bars, start_hm=(8, 30)), cfg)

    merged = [t for t in trades if t["source"] == "MERGED"]
    assert len(merged) == 1, f"T8: got {[t['source'] for t in trades]}"
    tr = merged[0]
    assert tr["target_price"] == "", f"T8 target {tr['target_price']}"
    expected_stop = round_tick(1000.0 * (1 - 0.015))
    assert abs(float(tr["stop_price"]) - expected_stop) < 1e-6, \
        f"T8 stop {tr['stop_price']} ≠ {expected_stop}"
    print("PASS: T8")


# ─────────────────────────────────────────────────────────────────────────────
# T9: FLIP — Trend LONG + OD SHORT → 2 trades
# ─────────────────────────────────────────────────────────────────────────────
def test_t9_flip():
    bars: dict = {}
    for m in range(45, 60): bars[f"08:{m:02d}"] = (992, 995, 990, 994, 100)
    for m in range( 0, 15): bars[f"09:{m:02d}"] = (994, 1010, 993, 1008, 100)
    for m in range(15, 30): bars[f"09:{m:02d}"] = (1005, 1012, 996, 1010, 100)
    for m in range(30, 35): bars[f"09:{m:02d}"] = (1000, 1002, 998, 1000, 100)# 5m entry=1000
    # OD check-1 bearish: c1=09:10 low=1010, c3=09:30 high=1004 < 1010 → bearish FVG
    for m in range(10, 20): bars[f"09:{m:02d}"] = (1012, 1015, 1010, 1013, 100)
    for m in range(20, 30): bars[f"09:{m:02d}"] = (1008, 1011, 1005, 1007, 100)
    for m in range(30, 40): bars[f"09:{m:02d}"] = (1002, 1004, 998, 1000, 100)
    bars["09:40"] = (1005, 1006, 1004, 1005, 100)
    for h in range(10, 16):
        for m in range(60): bars[f"{h:02d}:{m:02d}"] = (1005, 1006, 1004, 1005, 50)
    bars["15:59"] = (1005, 1006, 1004, 1005, 50)

    cfg    = make_test_cfg(enable_od=True, enable_trend=True,
                           trend_atr_len=1, trend_ema_fast=2, trend_ema_slow=5,
                           trend_atr_mult=0.5)
    trades = _run_one_day(_make_1m_day("2024-01-15", bars, start_hm=(8, 30)), cfg)

    trend_t = [t for t in trades if t["source"] == "TREND"]
    od_t    = [t for t in trades if t["source"] == "OD"]
    assert len(trend_t) >= 1,                       f"T9 no TREND: {[t['source'] for t in trades]}"
    assert trend_t[0]["exit_reason"] == "FLIP",     f"T9 TREND exit {trend_t[0]['exit_reason']}"
    assert "09:40:00" in trend_t[0]["exit_ts"],     f"T9 TREND exit_ts {trend_t[0]['exit_ts']}"
    assert len(od_t) >= 1,                          f"T9 no OD"
    assert od_t[0]["side"] == "SHORT",              f"T9 OD side {od_t[0]['side']}"
    assert "09:40:00" in od_t[0]["entry_ts"],       f"T9 OD entry_ts {od_t[0]['entry_ts']}"
    print("PASS: T9")


# ─────────────────────────────────────────────────────────────────────────────
# T10: trend_after_od_stopout gating
# ─────────────────────────────────────────────────────────────────────────────
def test_t10_trend_after_od_stopout():
    bars: dict = {}
    for m in range(10, 20): bars[f"09:{m:02d}"] = (988, 990, 985, 989, 50)
    for m in range(20, 30): bars[f"09:{m:02d}"] = (992, 998, 991, 997, 50)
    for m in range(30, 40): bars[f"09:{m:02d}"] = (1000, 1004, 1000, 1002, 50)
    bars["09:40"] = (1000, 1002, 998, 1001, 50)
    bars["10:05"] = (990, 992, 980, 982, 50)      # low 980 → OD stop at 985
    for m in range( 0, 15): bars[f"10:{m:02d}"] = (982, 984, 980, 983, 50)
    for m in range(15, 30): bars[f"10:{m:02d}"] = (984, 1005, 983, 1004, 50)
    for m in range(30, 45): bars[f"10:{m:02d}"] = (1002, 1008, 986, 1006, 50)
    for h in range(11, 16):
        for m in range(60): bars[f"{h:02d}:{m:02d}"] = (990, 991, 989, 990, 50)
    bars["15:59"] = (990, 991, 989, 990, 50)

    df       = _make_1m_day("2024-01-15", bars, default_price=990.0, start_hm=(8, 30))
    base_kw  = dict(enable_od=True, enable_trend=True,
                    trend_atr_len=1, trend_ema_fast=2, trend_ema_slow=5, trend_atr_mult=0.5)

    cfg_f    = make_test_cfg(trend_after_od_stopout=False, **base_kw)
    trades_f = _run_one_day(df, cfg_f)
    assert len([t for t in trades_f if t["source"] == "TREND"]) == 0, \
        f"T10-False: got Trend trades"

    cfg_t    = make_test_cfg(trend_after_od_stopout=True, **base_kw)
    trades_t = _run_one_day(df, cfg_t)
    after    = [t for t in trades_t if t["source"] == "TREND" and t["after_od_stopout"] == 1]
    assert len(after) >= 1, f"T10-True: no after-stopout Trend trade in {trades_t}"
    print("PASS: T10")


# ─────────────────────────────────────────────────────────────────────────────
# T11: Fee accounting
# ─────────────────────────────────────────────────────────────────────────────
def test_t11_fees():
    assert abs(DEFAULT_CONFIG["fee_per_side"] * 2 - 4.28) < 1e-9, "T11 round-trip fee"
    assert abs(DEFAULT_CONFIG["fee_per_side"] * 4 - 8.56) < 1e-9, "T11 flip day fees"
    print("PASS: T11")


# ─────────────────────────────────────────────────────────────────────────────
# T12: Timezone — a known 09:30 ET open volume spike lands at 09:30 after conversion,
#                in a winter date and a summer date (Spec Section 11 T12)
# ─────────────────────────────────────────────────────────────────────────────
def test_t12_timezone_spike():
    winter_ncd = r"C:\Users\Administrator\Documents\NinjaTrader 8\db\minute\NQ 03-24\20240102.Last.ncd"
    summer_ncd = r"C:\Users\Administrator\Documents\NinjaTrader 8\db\minute\NQ 09-24\20240701.Last.ncd"

    for label, path in [("winter (2024-01-02)", winter_ncd), ("summer (2024-07-01)", summer_ncd)]:
        assert os.path.exists(path), f"T12: File not found {path}"
        data = ncd_mod.decode_file(path, tz="UTC")
        close_us = data["utc_us"]
        vol = data["volume"]
        # In NT8 NCD, close_us encodes ET wall-clock as unix-epoch microseconds.
        # Stamp marks bar CLOSE, so open_ts = close_ts - 60s.
        open_dts = [datetime.fromtimestamp((ts - 60_000_000) / 1e6, tz=_tz_mod.utc)
                    for ts in close_us]
        # Look at 08:00 to 12:00 window
        window = [(dt, v) for dt, v in zip(open_dts, vol)
                  if 8 <= dt.hour < 12]
        assert len(window) > 0, f"T12 {label}: no bars in 08-12 window"
        peak_dt, peak_vol = max(window, key=lambda x: x[1])
        assert peak_dt.hour == 9 and peak_dt.minute == 30, \
            f"T12 {label}: peak volume landed at {peak_dt.strftime('%H:%M')} ET wall-clock, expected 09:30"
        print(f"PASS: T12 {label} peak volume at {peak_dt.strftime('%H:%M')} (vol={peak_vol})")



# ─────────────────────────────────────────────────────────────────────────────
# RUN ALL
# ─────────────────────────────────────────────────────────────────────────────
def run_all():
    print("=" * 60)
    print("RUNNING UNIT TESTS T1–T12")
    print("=" * 60)
    test_t1_fvg_strict_vs_nonstrict()
    test_t2_rising_highs_lows_no_gap()
    test_t3_consecutive()
    test_t4_od_checks()
    test_t5_stop_beats_target_same_bar()
    test_t6_gap_through_stop()
    test_t7_time_exit()
    test_t8_merge()
    test_t9_flip()
    test_t10_trend_after_od_stopout()
    test_t11_fees()
    test_t12_timezone_spike()
    print("=" * 60)
    print("ALL 12 UNIT TESTS PASSED")
    print("=" * 60)

if __name__ == "__main__":
    run_all()
