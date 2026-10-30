"""
Standalone Python Reproduction Engine for Roboquant (.rq) Strategies.
Deterministically reproduces:
1. Sigma Breakout (sigma_breakout.rq)
2. Crazy ORB (crazy_orb.rq)

Fills:
- Standard OHLCV mode fills market orders at the NEXT bar's Open.
- Bracket SL/TP evaluated intrabar against High/Low.
- Multiplier-aware PnL and commissions.
"""

from dataclasses import dataclass, field
from datetime import datetime
from typing import List, Optional, Dict
import zoneinfo
import numpy as np
import pandas as pd

NY_TZ = zoneinfo.ZoneInfo("America/New_York")


@dataclass
class Bar:
    time: datetime
    open: float
    high: float
    low: float
    close: float
    volume: float

    @property
    def et_time(self) -> datetime:
        return self.time.astimezone(NY_TZ)

    @property
    def et_hhmm(self) -> int:
        et = self.et_time
        return et.hour * 100 + et.minute

    @property
    def et_date_num(self) -> int:
        et = self.et_time
        return et.year * 10000 + et.month * 100 + et.day


@dataclass
class Trade:
    direction: str  # 'LONG' or 'SHORT'
    qty: int
    entry_bar_idx: int
    entry_time: datetime
    entry_price: float
    exit_bar_idx: int
    exit_time: datetime
    exit_price: float
    pnl: float
    reason: str


class SimpleATR:
    """Wilder's ATR matching talib / standard ta ATR."""
    def __init__(self, period: int):
        self.period = max(1, period)
        self.prev_close: Optional[float] = None
        self.count = 0
        self.tr_sum = 0.0
        self.current_atr: Optional[float] = None

    def update(self, bar: Bar) -> Optional[float]:
        if self.prev_close is None:
            tr = bar.high - bar.low
        else:
            tr = max(
                bar.high - bar.low,
                abs(bar.high - self.prev_close),
                abs(bar.low - self.prev_close),
            )
        self.prev_close = bar.close

        if self.current_atr is None:
            self.tr_sum += tr
            self.count += 1
            if self.count == self.period:
                self.current_atr = self.tr_sum / self.period
        else:
            self.current_atr = (self.current_atr * (self.period - 1) + tr) / self.period

        return self.current_atr


class SigmaBreakoutEngine:
    def __init__(
        self,
        n_sigma_days: int = 14,
        band_mult: float = 1.0,
        use_vwap: bool = False,
        sl_pct: float = 0.0,
        fixed_lot: int = 1,
        risk_pct: float = 1.0,
        fixed_risk_usd: float = 0.0,
        initial_capital: float = 100_000.0,
        contract_multiplier: float = 20.0,
        commission_per_contract: float = 0.75,
    ):
        self.n_sigma_days = n_sigma_days
        self.band_mult = band_mult
        self.use_vwap = use_vwap
        self.sl_pct = sl_pct
        self.fixed_lot = fixed_lot
        self.risk_pct = risk_pct
        self.fixed_risk_usd = fixed_risk_usd
        self.initial_capital = initial_capital
        self.multiplier = contract_multiplier
        self.commission = commission_per_contract

        self.slot_hist: Dict[int, List[float]] = {}
        self.current_day = 0
        self.day_open = 0.0
        self.have_day_open = False
        self.vwap_pv = 0.0
        self.vwap_vol = 0.0
        self.vwap_prev = 0.0
        self.have_vwap_prev = False
        self.trading_enabled = False

        self.position = 0  # +qty long, -qty short
        self.entry_price = 0.0
        self.entry_time: Optional[datetime] = None
        self.entry_idx = 0
        self.sl_price: Optional[float] = None
        self.trades: List[Trade] = []
        self.equity = initial_capital

    def _size(self, close_px: float) -> int:
        if self.sl_pct > 0.0:
            stop_dist = close_px * (self.sl_pct / 100.0)
            loss_per_contract = stop_dist * self.multiplier
            if loss_per_contract <= 0:
                return 0
            risk = self.fixed_risk_usd if self.fixed_risk_usd > 0 else (self.equity * (self.risk_pct / 100.0))
            return max(1, int(np.floor(risk / loss_per_contract)))
        return max(1, self.fixed_lot)

    def _close_pos(self, bar_idx: int, bar: Bar, exit_px: float, reason: str):
        if self.position == 0:
            return
        direction = 'LONG' if self.position > 0 else 'SHORT'
        qty = abs(self.position)
        pnl = (exit_px - self.entry_price) * qty * self.multiplier if direction == 'LONG' else (self.entry_price - exit_px) * qty * self.multiplier
        pnl -= 2 * self.commission * qty
        self.equity += pnl
        self.trades.append(Trade(
            direction=direction,
            qty=qty,
            entry_bar_idx=self.entry_idx,
            entry_time=self.entry_time,
            entry_price=self.entry_price,
            exit_bar_idx=bar_idx,
            exit_time=bar.time,
            exit_price=exit_px,
            pnl=pnl,
            reason=reason,
        ))
        self.position = 0
        self.sl_price = None

    def run(self, bars: List[Bar]) -> pd.DataFrame:
        SESSION_OPEN = 930
        SESSION_CLOSE = 1600

        for idx, bar in enumerate(bars):
            date_num = bar.et_date_num
            hhmm = bar.et_hhmm

            # Rollover
            if date_num != self.current_day:
                if self.position != 0:
                    self._close_pos(idx, bar, bar.open, "rollover_flatten")
                self.current_day = date_num
                self.have_day_open = False
                self.vwap_pv = 0.0
                self.vwap_vol = 0.0
                self.have_vwap_prev = False

            # Check Stop Loss intrabar
            if self.position > 0 and self.sl_price is not None:
                if bar.low <= self.sl_price:
                    self._close_pos(idx, bar, min(bar.open, self.sl_price), "stop_loss")
            elif self.position < 0 and self.sl_price is not None:
                if bar.high >= self.sl_price:
                    self._close_pos(idx, bar, max(bar.open, self.sl_price), "stop_loss")

            # Flatten at RTH Close
            if hhmm >= SESSION_CLOSE:
                if self.position != 0:
                    self._close_pos(idx, bar, bar.close, "session_close")
                continue

            if hhmm < SESSION_OPEN:
                continue

            if not self.have_day_open:
                self.day_open = bar.open
                self.have_day_open = True

            ref = (self.vwap_prev if self.have_vwap_prev else self.day_open) if self.use_vwap else self.day_open
            hist = self.slot_hist.setdefault(hhmm, [])
            is_full = len(hist) >= self.n_sigma_days
            sigma = (sum(hist[-self.n_sigma_days:]) / min(len(hist), self.n_sigma_days)) if hist else 0.0
            upper = ref + self.band_mult * sigma
            lower = ref - self.band_mult * sigma

            if not self.trading_enabled and is_full:
                self.trading_enabled = True

            # Entry condition evaluated on bar close
            if self.trading_enabled:
                if bar.close > upper and self.position <= 0:
                    if self.position < 0:
                        self._close_pos(idx, bar, bar.close, "flip_long")
                    qty = self._size(bar.close)
                    self.position = qty
                    self.entry_price = bar.close
                    self.entry_time = bar.time
                    self.entry_idx = idx
                    self.sl_price = bar.close * (1.0 - self.sl_pct / 100.0) if self.sl_pct > 0 else None
                elif bar.close < lower and self.position >= 0:
                    if self.position > 0:
                        self._close_pos(idx, bar, bar.close, "flip_short")
                    qty = self._size(bar.close)
                    self.position = -qty
                    self.entry_price = bar.close
                    self.entry_time = bar.time
                    self.entry_idx = idx
                    self.sl_price = bar.close * (1.0 + self.sl_pct / 100.0) if self.sl_pct > 0 else None

            # Update VWAP
            typ = (bar.high + bar.low + bar.close) / 3.0
            self.vwap_pv += typ * bar.volume
            self.vwap_vol += bar.volume
            if self.vwap_vol > 0:
                self.vwap_prev = self.vwap_pv / self.vwap_vol
                self.have_vwap_prev = True

            # Push |close - day_open| for future days
            move_abs = abs(bar.close - self.day_open)
            hist.append(move_abs)

        return pd.DataFrame([t.__dict__ for t in self.trades])


class CrazyOrbEngine:
    def __init__(
        self,
        range_start_hhmm: int = 900,
        range_end_hhmm: int = 915,
        atr_period: int = 14,
        atr_mult: float = 1.5,
        rr: float = 2.0,
        risk_usd: float = 500.0,
        min_size: int = 1,
        max_trades: int = 2,
        trade_start_hhmm: int = 930,
        trade_end_hhmm: int = 1545,
        flatten_hhmm: int = 1555,
        initial_capital: float = 100_000.0,
        contract_multiplier: float = 20.0,
        commission_per_contract: float = 0.75,
    ):
        self.range_start = range_start_hhmm
        self.range_end = range_end_hhmm
        self.atr_indicator = SimpleATR(atr_period)
        self.atr_mult = atr_mult
        self.rr = rr
        self.risk_usd = risk_usd
        self.min_size = max(1, min_size)
        self.max_trades = max_trades
        self.trade_start = trade_start_hhmm
        self.trade_end = trade_end_hhmm
        self.flatten_hhmm = flatten_hhmm
        self.multiplier = contract_multiplier
        self.commission = commission_per_contract

        self.current_day = 0
        self.range_high = 0.0
        self.range_low = 0.0
        self.building = False
        self.range_done = False
        self.ready = False
        self.long_used = False
        self.short_used = False
        self.trades_today = 0

        self.position = 0
        self.entry_price = 0.0
        self.entry_time: Optional[datetime] = None
        self.entry_idx = 0
        self.sl_price = 0.0
        self.tp_price = 0.0
        self.trades: List[Trade] = []
        self.equity = initial_capital

    def _close_pos(self, bar_idx: int, bar: Bar, exit_px: float, reason: str):
        if self.position == 0:
            return
        direction = 'LONG' if self.position > 0 else 'SHORT'
        qty = abs(self.position)
        pnl = (exit_px - self.entry_price) * qty * self.multiplier if direction == 'LONG' else (self.entry_price - exit_px) * qty * self.multiplier
        pnl -= 2 * self.commission * qty
        self.equity += pnl
        self.trades.append(Trade(
            direction=direction,
            qty=qty,
            entry_bar_idx=self.entry_idx,
            entry_time=self.entry_time,
            entry_price=self.entry_price,
            exit_bar_idx=bar_idx,
            exit_time=bar.time,
            exit_price=exit_px,
            pnl=pnl,
            reason=reason,
        ))
        self.position = 0

    def run(self, bars: List[Bar]) -> pd.DataFrame:
        for idx, bar in enumerate(bars):
            date_num = bar.et_date_num
            hhmm = bar.et_hhmm
            atr = self.atr_indicator.update(bar)

            # 1. Day Rollover
            if date_num != self.current_day:
                if self.position != 0:
                    self._close_pos(idx, bar, bar.open, "rollover_flatten")
                self.current_day = date_num
                self.range_high = 0.0
                self.range_low = 0.0
                self.building = False
                self.range_done = False
                self.ready = False
                self.long_used = False
                self.short_used = False
                self.trades_today = 0

            # 2. Check Intrabar SL / TP for active trade
            if self.position > 0:
                if bar.low <= self.sl_price:
                    self._close_pos(idx, bar, min(bar.open, self.sl_price), "stop_loss")
                elif bar.high >= self.tp_price:
                    self._close_pos(idx, bar, max(bar.open, self.tp_price), "take_profit")
            elif self.position < 0:
                if bar.high >= self.sl_price:
                    self._close_pos(idx, bar, max(bar.open, self.sl_price), "stop_loss")
                elif bar.low <= self.tp_price:
                    self._close_pos(idx, bar, min(bar.open, self.tp_price), "take_profit")

            # 3. Absorb / Lock Range
            if hhmm >= self.range_start and hhmm < self.range_end:
                if not self.building:
                    self.building = True
                    self.range_high = bar.high
                    self.range_low = bar.low
                else:
                    self.range_high = max(self.range_high, bar.high)
                    self.range_low = min(self.range_low, bar.low)
            elif not self.range_done and hhmm >= self.range_end:
                self.range_done = True
                self.ready = self.building

            # 4. Flatten Session
            if hhmm >= self.flatten_hhmm:
                if self.position != 0:
                    self._close_pos(idx, bar, bar.close, "session_flatten")
                continue

            # 5. Check Breakout Signal
            if self.ready and self.position == 0 and self.trades_today < self.max_trades:
                if hhmm >= self.trade_start and hhmm < self.trade_end and atr is not None:
                    stop_dist = atr * self.atr_mult
                    loss_per_contract = stop_dist * self.multiplier
                    if loss_per_contract > 0:
                        planned_qty = int(np.floor(self.risk_usd / loss_per_contract))
                        qty = max(self.min_size, planned_qty)

                        if bar.close > self.range_high and not self.long_used:
                            self.position = qty
                            self.entry_price = bar.close
                            self.entry_time = bar.time
                            self.entry_idx = idx
                            self.sl_price = bar.close - stop_dist
                            self.tp_price = bar.close + self.rr * stop_dist
                            self.long_used = True
                            self.trades_today += 1
                        elif bar.close < self.range_low and not self.short_used:
                            self.position = -qty
                            self.entry_price = bar.close
                            self.entry_time = bar.time
                            self.entry_idx = idx
                            self.sl_price = bar.close + stop_dist
                            self.tp_price = bar.close - self.rr * stop_dist
                            self.short_used = True
                            self.trades_today += 1

        return pd.DataFrame([t.__dict__ for t in self.trades])


# =============================================================================
# Flash Trigger ORB Engine
# =============================================================================

class FlashTriggerOrbEngine:
    def __init__(
        self,
        candle_tf_min: int = 15,
        target_hour: int = 14,
        target_minute: int = 30,
        utc_offset: int = 0,
        risk_reward: float = 3.0,
        risk_mode: int = 0,         # 0=Fixed Lot, 1=Fixed $, 2=% Account
        lot_size: float = 1.0,
        risk_usd: float = 100.0,
        risk_percent: float = 1.0,
        eod_close_hour: int = 23,
        eod_close_minute: int = 59,
        activate_trailing: bool = False,
        trail_activate_points: float = 50.0,
        trail_buffer_points: float = 20.0,
        day_filter: bool = False,
        trade_days: Optional[Dict[int, bool]] = None,  # weekday 0=Mon..4=Fri
        initial_capital: float = 100000.0,
        multiplier: float = 2.0,    # e.g. MNQ=2, NQ=20
        commission: float = 0.75,   # per side
    ):
        self.candle_tf_min = candle_tf_min
        self.target_hour = target_hour
        self.target_minute = target_minute
        self.utc_offset = utc_offset
        self.risk_reward = risk_reward
        self.risk_mode = risk_mode
        self.lot_size = lot_size
        self.risk_usd = risk_usd
        self.risk_percent = risk_percent
        self.eod_close_hour = eod_close_hour
        self.eod_close_minute = eod_close_minute
        self.activate_trailing = activate_trailing
        self.trail_activate_points = trail_activate_points
        self.trail_buffer_points = trail_buffer_points
        self.day_filter = day_filter
        self.trade_days = trade_days or {0: True, 1: True, 2: True, 3: True, 4: True}

        self.initial_capital = initial_capital
        self.equity = initial_capital
        self.multiplier = multiplier
        self.commission = commission

        # State
        self.session_date = 0
        self.utc_session_date = 0
        self.closed_for_day = False
        self.range_high = 0.0
        self.range_low = 0.0
        self.range_active = False
        self.range_start_bar = -1
        self.traded_this_range = False

        self.position = 0
        self.entry_price = 0.0
        self.entry_time = None
        self.entry_idx = -1
        self.sl_price = 0.0
        self.tp_price = 0.0
        self.trailing_active = False
        self.best_price = 0.0

        self.trades: List[Trade] = []

    def _get_local_time(self, bar: Bar) -> Tuple[int, int, int]:
        shifted = bar.time + timedelta(hours=self.utc_offset)
        date_num = shifted.year * 10000 + shifted.month * 100 + shifted.day
        hhmm = shifted.hour * 100 + shifted.minute
        return date_num, hhmm, shifted.weekday()

    def _day_allowed(self, weekday: int) -> bool:
        if not self.day_filter:
            return True
        return self.trade_days.get(weekday, False)

    def _calc_size(self, stop_distance: float) -> int:
        if self.risk_mode == 1:
            loss_per_contract = abs(stop_distance) * self.multiplier
            if loss_per_contract > 0:
                return int(np.floor(self.risk_usd / loss_per_contract))
            return 0
        elif self.risk_mode == 2:
            loss_per_contract = abs(stop_distance) * self.multiplier
            if loss_per_contract > 0:
                risk_amt = self.equity * (self.risk_percent / 100.0)
                return int(np.floor(risk_amt / loss_per_contract))
            return 0
        else:
            return max(1, int(round(self.lot_size)))

    def _close_pos(self, idx: int, bar: Bar, exit_px: float, reason: str):
        contracts = abs(self.position)
        gross_pnl = (exit_px - self.entry_price) * contracts * self.multiplier if self.position > 0 else (self.entry_price - exit_px) * contracts * self.multiplier
        comm = self.commission * 2 * contracts
        pnl = gross_pnl - comm
        self.equity += pnl

        self.trades.append(Trade(
            entry_time=self.entry_time,
            exit_time=bar.time,
            direction="LONG" if self.position > 0 else "SHORT",
            entry_price=self.entry_price,
            exit_price=exit_px,
            pnl=pnl,
            reason=reason,
        ))
        self.position = 0
        self.trailing_active = False
        self.best_price = 0.0

    def run(self, bars: List[Bar]) -> pd.DataFrame:
        target_hhmm = self.target_hour * 100 + self.target_minute
        eod_hhmm = self.eod_close_hour * 100 + self.eod_close_minute

        for idx, bar in enumerate(bars):
            local_date, local_hhmm, local_weekday = self._get_local_time(bar)
            utc_date = bar.time.year * 10000 + bar.time.month * 100 + bar.time.day
            utc_hhmm = bar.time.hour * 100 + bar.time.minute

            # 1. New Local Day
            if local_date != self.session_date:
                self.session_date = local_date
                self.range_active = False
                self.traded_this_range = False

            # 2. New UTC Day
            if utc_date != self.utc_session_date:
                self.utc_session_date = utc_date
                self.closed_for_day = False

            # 3. EOD Flatten (UTC)
            if not self.closed_for_day and utc_hhmm >= eod_hhmm:
                if self.position != 0:
                    self._close_pos(idx, bar, bar.close, "eod_flatten")
                self.closed_for_day = True

            # 4. Trailing Stop Management & SL / TP Check
            if self.position > 0:
                if bar.high > self.best_price:
                    self.best_price = bar.high
                if self.activate_trailing:
                    if not self.trailing_active and (self.best_price - self.entry_price) >= self.trail_activate_points:
                        self.trailing_active = True
                    if self.trailing_active:
                        self.sl_price = max(self.sl_price, self.best_price - self.trail_buffer_points)

                if bar.low <= self.sl_price:
                    self._close_pos(idx, bar, min(bar.open, self.sl_price), "stop_loss")
                elif bar.high >= self.tp_price:
                    self._close_pos(idx, bar, max(bar.open, self.tp_price), "take_profit")

            elif self.position < 0:
                if bar.low < self.best_price:
                    self.best_price = bar.low
                if self.activate_trailing:
                    if not self.trailing_active and (self.entry_price - self.best_price) >= self.trail_activate_points:
                        self.trailing_active = True
                    if self.trailing_active:
                        self.sl_price = min(self.sl_price, self.best_price + self.trail_buffer_points)

                if bar.high >= self.sl_price:
                    self._close_pos(idx, bar, max(bar.open, self.sl_price), "stop_loss")
                elif bar.low <= self.tp_price:
                    self._close_pos(idx, bar, min(bar.open, self.tp_price), "take_profit")

            # 5. Targeted Candle Formation
            if not self.range_active and local_hhmm == target_hhmm and self._day_allowed(local_weekday):
                self.range_high = bar.high
                self.range_low = bar.low
                self.range_active = True
                self.traded_this_range = False
                self.range_start_bar = idx
                continue

            # 6. Check Range Trigger
            if not self.range_active or self.traded_this_range:
                continue

            if idx <= self.range_start_bar or self.position != 0:
                continue

            if bar.close > self.range_high:
                sl = self.range_low
                stop_dist = bar.close - sl
                if stop_dist > 0:
                    qty = self._calc_size(stop_dist)
                    if qty > 0:
                        self.position = qty
                        self.entry_price = bar.close
                        self.entry_time = bar.time
                        self.entry_idx = idx
                        self.sl_price = sl
                        self.tp_price = bar.close + stop_dist * self.risk_reward
                        self.best_price = bar.close
                        self.trailing_active = False
                        self.traded_this_range = True

            elif bar.close < self.range_low:
                sl = self.range_high
                stop_dist = sl - bar.close
                if stop_dist > 0:
                    qty = self._calc_size(stop_dist)
                    if qty > 0:
                        self.position = -qty
                        self.entry_price = bar.close
                        self.entry_time = bar.time
                        self.entry_idx = idx
                        self.sl_price = sl
                        self.tp_price = bar.close - stop_dist * self.risk_reward
                        self.best_price = bar.close
                        self.trailing_active = False
                        self.traded_this_range = True

        return pd.DataFrame([t.__dict__ for t in self.trades])


# =====================================================================
# 4. LONDON LIQUIDITY / IFVG ENGINE (by Shujat)
# =====================================================================

@dataclass
class EngineFvg:
    start_time_us: int
    lower: float
    upper: float
    is_bullish: bool
    day_stamp: int


class LondonSweepIfvgEngine:
    def __init__(
        self,
        london_start_hhmm: int = 1300,
        london_end_hhmm: int = 1900,
        ls_limit_start_hhmm: int = 1915,
        ls_limit_end_hhmm: int = 2345,
        line_duration_bars: int = 6,
        max_ifvg_age_hours: int = 24,
        swing_lookback: int = 5,
        sl_type: int = 1,  # 0=Swing, 1=ATR Multiplier
        tp_type: int = 2,  # 0=Range, 1=R:R, 2=ATR Multiplier
        risk_reward: float = 2.0,
        atr_length: int = 14,
        sl_atr_mult: float = 1.5,
        tp_atr_mult: float = 1.5,
        sizing_mode: int = 0,  # 0=Fixed Lots, 1=Risk $, 2=Risk %
        fixed_lots: int = 1,
        risk_per_trade_usd: float = 100.0,
        risk_per_trade_pct: float = 1.0,
        use_day_filter: bool = False,
        trade_days: Optional[List[int]] = None,  # Mon=0, Tue=1, ..., Fri=4
        point_value: float = 2.0,  # e.g., MNQ = $2/pt
        initial_equity: float = 100000.0,
        commission_per_contract: float = 0.75,
    ):
        self.london_start = london_start_hhmm
        self.london_end = london_end_hhmm
        self.ls_start = ls_limit_start_hhmm
        self.ls_end = ls_limit_end_hhmm
        self.line_duration_bars = line_duration_bars
        self.max_ifvg_age_hours = max_ifvg_age_hours
        self.swing_lookback = swing_lookback
        self.sl_type = sl_type
        self.tp_type = tp_type
        self.risk_reward = risk_reward
        self.sl_atr_mult = sl_atr_mult
        self.tp_atr_mult = tp_atr_mult
        self.sizing_mode = sizing_mode
        self.fixed_lots = fixed_lots
        self.risk_per_trade_usd = risk_per_trade_usd
        self.risk_per_trade_pct = risk_per_trade_pct
        self.use_day_filter = use_day_filter
        self.trade_days = trade_days if trade_days is not None else [0, 1, 2, 3, 4]
        self.point_value = point_value
        self.equity = initial_equity
        self.commission = commission_per_contract

        self.atr_indicator = AtrIndicator(atr_length)

        # State tracking
        self.session_active = False
        self.session_high = float("-inf")
        self.session_low = float("inf")
        self.session_start_time_us = -1
        self.high_level = float("nan")
        self.low_level = float("nan")
        self.lines_active = False
        self.line_deadline_bar = -1

        self.high_swept = False
        self.low_swept = False
        self.ls_created = False
        self.ls_mark_time_us = -1
        self.ls_day_stamp = 0
        self.ls_direction = 0  # -1 short (high swept), 1 long (low swept)
        self.ls_price = float("nan")
        self.ifvg_marked_after_ls = False

        self.last_day_stamp = 0
        self.fvgs: List[EngineFvg] = []

        # Position tracking
        self.position = 0
        self.entry_price = 0.0
        self.entry_time = None
        self.entry_idx = 0
        self.sl_price = 0.0
        self.tp_price = 0.0
        self.trades: List[Trade] = []

    def _in_window(self, hhmm: int, start: int, end: int) -> bool:
        if start <= end:
            return start <= hhmm < end
        return hhmm >= start or hhmm < end

    def _is_large_body(self, bar: Bar) -> bool:
        rng = bar.high - bar.low
        if rng <= 0:
            return False
        body = abs(bar.open - bar.close)
        return (body * 100.0) / rng > 50.0

    def _calc_size(self, entry_px: float, stop_px: float) -> int:
        risk_per_unit = abs(entry_px - stop_px) * self.point_value
        if self.sizing_mode == 1 and risk_per_unit > 0:
            return max(1, int(math.floor(self.risk_per_trade_usd / risk_per_unit)))
        if self.sizing_mode == 2 and risk_per_unit > 0:
            risk_usd = self.equity * (self.risk_per_trade_pct / 100.0)
            return max(1, int(math.floor(risk_usd / risk_per_unit)))
        return max(1, self.fixed_lots)

    def _close_pos(self, idx: int, bar: Bar, exit_px: float, reason: str):
        qty = abs(self.position)
        pnl = (exit_px - self.entry_price) * self.position * self.point_value - (2 * self.commission * qty)
        self.equity += pnl
        self.trades.append(Trade(
            entry_time=self.entry_time,
            entry_index=self.entry_idx,
            exit_time=bar.time,
            exit_index=idx,
            direction="long" if self.position > 0 else "short",
            quantity=qty,
            entry_price=self.entry_price,
            exit_price=exit_px,
            pnl=pnl,
            reason=reason,
        ))
        self.position = 0

    def run(self, bars: List[Bar]) -> pd.DataFrame:
        for idx, bar in enumerate(bars):
            atr_val = self.atr_indicator.update(bar)
            today_stamp = bar.et_date_num
            et_hhmm = bar.et_hhmm
            bar_time_us = int(bar.time.timestamp() * 1_000_000)

            # 1. Manage Active Positions (Bracket SL / TP)
            if self.position > 0:
                if bar.low <= self.sl_price:
                    self._close_pos(idx, bar, min(bar.open, self.sl_price), "stop_loss")
                elif bar.high >= self.tp_price:
                    self._close_pos(idx, bar, max(bar.open, self.tp_price), "take_profit")
            elif self.position < 0:
                if bar.high >= self.sl_price:
                    self._close_pos(idx, bar, max(bar.open, self.sl_price), "stop_loss")
                elif bar.low <= self.tp_price:
                    self._close_pos(idx, bar, min(bar.open, self.tp_price), "take_profit")

            # 2. Hard backstop check (>24h open session)
            if self.session_active and self.session_start_time_us >= 0:
                if (bar_time_us - self.session_start_time_us) > 24 * 3_600_000_000:
                    self.session_active = False
                    self.session_high = float("-inf")
                    self.session_low = float("inf")

            # 3. Day rollover reset
            if self.last_day_stamp != 0 and today_stamp != self.last_day_stamp:
                self.session_active = False
                self.session_high = float("-inf")
                self.session_low = float("inf")
                self.high_swept = False
                self.low_swept = False
                self.lines_active = False
                self.ls_created = False
                self.ls_mark_time_us = -1
                self.ls_day_stamp = 0
                self.ls_direction = 0
                self.ls_price = float("nan")
                self.ifvg_marked_after_ls = False
            self.last_day_stamp = today_stamp

            in_session = self._in_window(et_hhmm, self.london_start, self.london_end)

            # 4. Session Start / Update / End
            if in_session and not self.session_active:
                self.session_active = True
                self.session_high = bar.high
                self.session_low = bar.low
                self.session_start_time_us = bar_time_us
                self.lines_active = False
                self.ls_created = False
                self.ls_mark_time_us = -1
                self.ls_day_stamp = 0
                self.ls_direction = 0
                self.ls_price = float("nan")
                self.ifvg_marked_after_ls = False
            elif in_session and self.session_active:
                self.session_high = max(self.session_high, bar.high)
                self.session_low = min(self.session_low, bar.low)
            elif not in_session and self.session_active:
                self.high_level = self.session_high
                self.low_level = self.session_low
                self.session_active = False
                self.line_deadline_bar = idx + self.line_duration_bars
                self.lines_active = True

            # 5. Stop extending lines after duration elapses
            if self.lines_active and self.line_deadline_bar >= 0 and idx >= self.line_deadline_bar:
                self.lines_active = False

            # 6. Liquidity Sweep Detection
            ls_time_ok = self._in_window(et_hhmm, self.ls_start, self.ls_end)
            has_high = not math.isnan(self.high_level)
            has_low = not math.isnan(self.low_level)

            if has_high and self.lines_active and not self.ls_created and ls_time_ok:
                if bar.close > self.high_level:
                    self.high_swept = True
                if self.high_swept and bar.close <= self.high_level and self._is_large_body(bar):
                    self.ls_created = True
                    self.ls_mark_time_us = bar_time_us
                    self.ls_day_stamp = today_stamp
                    self.ls_direction = -1
                    self.ls_price = bar.high
                    self.high_swept = False

            if has_low and self.lines_active and not self.ls_created and ls_time_ok:
                if bar.close < self.low_level:
                    self.low_swept = True
                if self.low_swept and bar.close >= self.low_level and self._is_large_body(bar):
                    self.ls_created = True
                    self.ls_mark_time_us = bar_time_us
                    self.ls_day_stamp = today_stamp
                    self.ls_direction = 1
                    self.ls_price = bar.low
                    self.low_swept = False

            # 7. IFVG Detection (needs 2 bars back)
            if idx >= 2:
                b2 = bars[idx - 2]
                is_bull_fvg = b2.high < bar.low
                is_bear_fvg = b2.low > bar.high

                track_from_session = in_session and not self.ifvg_marked_after_ls
                track_after_ls = (
                    self.ls_created
                    and not self.ifvg_marked_after_ls
                    and self.ls_day_stamp == today_stamp
                    and self.ls_mark_time_us >= 0
                    and bar_time_us > self.ls_mark_time_us
                )
                track_fvg = track_from_session or track_after_ls

                if track_fvg and is_bull_fvg and len(self.fvgs) < 64:
                    self.fvgs.append(EngineFvg(
                        start_time_us=int(b2.time.timestamp() * 1_000_000),
                        lower=b2.high,
                        upper=bar.low,
                        is_bullish=True,
                        day_stamp=today_stamp,
                    ))
                if track_fvg and is_bear_fvg and len(self.fvgs) < 64:
                    self.fvgs.append(EngineFvg(
                        start_time_us=int(b2.time.timestamp() * 1_000_000),
                        lower=bar.low,
                        upper=b2.low,
                        is_bullish=False,
                        day_stamp=today_stamp,
                    ))

            # 8. Check Inversions & Signals
            bear_confirmed = False
            bull_confirmed = False
            bear_sl = float("nan")
            bear_tp = float("nan")
            bull_sl = float("nan")
            bull_tp = float("nan")

            if self.fvgs and atr_val is not None:
                max_age_us = self.max_ifvg_age_hours * 3_600_000_000
                i = len(self.fvgs)
                while i > 0:
                    i -= 1
                    fvg = self.fvgs[i]
                    same_day = today_stamp == fvg.day_stamp
                    too_old = (bar_time_us - fvg.start_time_us) > max_age_us

                    if too_old or not same_day or self.ifvg_marked_after_ls:
                        self.fvgs.pop(i)
                        continue

                    inverted = (bar.close < fvg.lower) if fvg.is_bullish else (bar.close > fvg.upper)
                    is_bearish_ifvg = fvg.is_bullish
                    is_bullish_ifvg = not fvg.is_bullish

                    direction_matches_ls = (
                        (self.ls_direction == -1 and is_bearish_ifvg)
                        or (self.ls_direction == 1 and is_bullish_ifvg)
                    )
                    position_valid_vs_ls = (
                        (math.isnan(self.ls_price) or fvg.lower <= self.ls_price)
                        if is_bearish_ifvg
                        else (math.isnan(self.ls_price) or fvg.upper >= self.ls_price)
                    )
                    can_mark_for_session = self.ls_created and not in_session and ls_time_ok

                    if inverted and can_mark_for_session and direction_matches_ls and position_valid_vs_ls:
                        swing_n = max(1, self.swing_lookback)
                        swing_high = float("-inf")
                        swing_low = float("inf")
                        for k in range(min(swing_n, idx + 1)):
                            swing_high = max(swing_high, bars[idx - k].high)
                            swing_low = min(swing_low, bars[idx - k].low)

                        if is_bearish_ifvg:
                            bear_confirmed = True
                            bear_sl = bar.close + atr_val * self.sl_atr_mult if self.sl_type == 1 else swing_high
                            risk = bear_sl - bar.close
                            if self.tp_type == 1:
                                bear_tp = bar.close - risk * self.risk_reward
                            elif self.tp_type == 2:
                                bear_tp = bar.close - atr_val * self.tp_atr_mult
                            else:
                                bear_tp = self.low_level
                        else:
                            bull_confirmed = True
                            bull_sl = bar.close - atr_val * self.sl_atr_mult if self.sl_type == 1 else swing_low
                            risk = bar.close - bull_sl
                            if self.tp_type == 1:
                                bull_tp = bar.close + risk * self.risk_reward
                            elif self.tp_type == 2:
                                bull_tp = bar.close + atr_val * self.tp_atr_mult
                            else:
                                bull_tp = self.high_level

                        self.ifvg_marked_after_ls = True
                        self.fvgs.pop(i)

            # 9. Day Filter & Order Execution
            dow = bar.time.astimezone(ZoneInfo("America/New_York")).weekday()
            trading_day_allowed = not self.use_day_filter or (dow in self.trade_days)

            short_ready = (
                trading_day_allowed
                and bear_confirmed
                and not math.isnan(bear_tp)
                and not math.isnan(bear_sl)
                and bear_sl > bar.close
                and bear_tp < bar.close
            )
            long_ready = (
                trading_day_allowed
                and bull_confirmed
                and not math.isnan(bull_tp)
                and not math.isnan(bull_sl)
                and bull_sl < bar.close
                and bull_tp > bar.close
            )

            if short_ready and self.position == 0:
                qty = self._calc_size(bar.close, bear_sl)
                if qty > 0:
                    self.position = -qty
                    self.entry_price = bar.close
                    self.entry_time = bar.time
                    self.entry_idx = idx
                    self.sl_price = bear_sl
                    self.tp_price = bear_tp

            elif long_ready and self.position == 0:
                qty = self._calc_size(bar.close, bull_sl)
                if qty > 0:
                    self.position = qty
                    self.entry_price = bar.close
                    self.entry_time = bar.time
                    self.entry_idx = idx
                    self.sl_price = bull_sl
                    self.tp_price = bull_tp

        return pd.DataFrame([t.__dict__ for t in self.trades])


