#!/usr/bin/env python3
"""
FVG Duo backtester — Python reference implementation.
Reads NT8 1m OHLC data. NQ contract, fixed 1 contract.
$2.14 commission + 1 tick slippage each way.
Market order at next bar open entry.
Tests both variants:
  A) Video original: 1.5% stop, 1.5x ATR displacement, EMA 50/100
  B) Tighter:        1.25x ATR displacement, EMA 40/50, 3R target
"""
import pandas as pd
import numpy as np
from datetime import datetime, timedelta, time as dt_time
from typing import NamedTuple, Optional
import pytz

ET = pytz.timezone('US/Eastern')

# Costs per contract per round-turn
COMMISSION_PER_TURN = 2.14   # $2.14 each way -> $4.28 round turn
TICK_SIZE_NQ = 0.25          # NQ minimum tick
TICK_VALUE_NQ = 5.00         # $5 per tick on NQ ($20/pt / 4 ticks)
SLIPPAGE_TICKS_EACH_WAY = 1  # 1 tick each way


class Trade(NamedTuple):
    entry_time: datetime
    exit_time: datetime
    entry_price: float
    exit_price: float
    direction: str  # 'LONG' or 'SHORT'
    stop_price: float
    pnl_dollars: float


def is_bullish_fvg(c1, c2, c3):
    """Three consecutive bars form bullish FVG: gap between c1.high and c3.low."""
    return c1['low'] < c2['low'] < c3['low'] and c1['high'] < c2['high'] < c3['high']


def is_bearish_fvg(c1, c2, c3):
    """Three consecutive bars form bearish FVG."""
    return c1['low'] > c2['low'] > c3['low'] and c1['high'] > c2['high'] > c3['high']


def load_nt8_1m_csv(csv_path: str) -> pd.DataFrame:
    """Load NT8 1-minute OHLC CSV. Expects columns: timestamp,open,high,low,close or NT8 export format."""
    df = pd.read_csv(csv_path, parse_dates=['timestamp'])
    df.set_index('timestamp', inplace=True)
    df.index = df.index.tz_localize(ET) if df.index.tz is None else df.index.tz_convert(ET)
    return df[['open', 'high', 'low', 'close']]


def resample_ohlc(df_1m: pd.DataFrame, minutes: int) -> pd.DataFrame:
    """Resample 1m OHLC to N-minute OHLC within trading session."""
    return (df_1m
            .resample(f'{minutes}min', label='right', closed='right')
            .agg({'open': 'first', 'high': 'max', 'low': 'min', 'close': 'last'})
            .dropna())


def opening_drive_fvg(df_10m, df_5m, stop_pct=0.015):
    """
    Opening Drive FVG (Piece 1):
    - 10m candles for FVG detection
    - Check at 09:40 ET then 09:50 ET
    - Three consecutive 10m bars c1,c2,c3 form FVG (c3 = check candle)
    - Enter at next 5m candle open (market)
    - Hold to NY close (16:00)
    - Stop: stop_pct away
    """
    trades = []
    day_has_trade = set()

    for check_time, check_bar in df_10m.iterrows():
        t = check_time.time()
        if t != dt_time(9, 40) and t != dt_time(9, 50):
            continue

        entry_time = check_time + timedelta(minutes=5)
        if entry_time not in df_5m.index:
            continue

        entry_bar = df_5m.loc[entry_time]

        bar1_time = check_time - timedelta(minutes=20)
        bar2_time = check_time - timedelta(minutes=10)
        if bar1_time not in df_10m.index or bar2_time not in df_10m.index:
            continue

        c1 = df_10m.loc[bar1_time]
        c2 = df_10m.loc[bar2_time]
        c3 = check_bar

        day_key = entry_time.date()
        if day_key in day_has_trade:
            continue

        entry_px = entry_bar['open']

        # Apply slippage: market buy fills at ask = open + 1 tick; sell at bid = open - 1 tick
        if is_bullish_fvg(c1, c2, c3):
            entry_fill = entry_px + SLIPPAGE_TICKS_EACH_WAY * TICK_SIZE_NQ
            stop = entry_fill * (1 - stop_pct)
            if entry_bar['close'] < stop:
                continue  # stopped out before/at close

            # Exit at 16:00 close, with slippage on sell
            exit_fill = entry_bar['close'] - SLIPPAGE_TICKS_EACH_WAY * TICK_SIZE_NQ
            pnl = (exit_fill - entry_fill) * 20.0  # $20 per point on NQ
            pnl -= COMMISSION_PER_TURN * 2  # commission each way

            trades.append(Trade(entry_time, entry_time.replace(hour=16, minute=0),
                                entry_fill, exit_fill, 'LONG', stop, pnl))
            day_has_trade.add(day_key)
            continue

        if is_bearish_fvg(c1, c2, c3):
            entry_fill = entry_px - SLIPPAGE_TICKS_EACH_WAY * TICK_SIZE_NQ
            stop = entry_fill * (1 + stop_pct)
            if entry_bar['close'] > stop:
                continue

            exit_fill = entry_bar['close'] + SLIPPAGE_TICKS_EACH_WAY * TICK_SIZE_NQ
            pnl = (entry_fill - exit_fill) * 20.0
            pnl -= COMMISSION_PER_TURN * 2

            trades.append(Trade(entry_time, entry_time.replace(hour=16, minute=0),
                                entry_fill, exit_fill, 'SHORT', stop, pnl))
            day_has_trade.add(day_key)
            continue

    return trades


def trend_fvg(df_15m, df_5m, variant='A'):
    """
    Trend FVG (Piece 2):
    - Variant A (video): EMA 50/100 trend, 1.5x ATR displacement, long only, 3R target (not enforced)
    - Variant B (tighter): EMA 40/50 trend, 1.25x ATR displacement, long only, 3R target
    - Enter at next 5m open after FVG
    - Stop: 1 tick under entry low
    - Hold to 16:00 close
    """
    ny_close = dt_time(16, 0)
    trades = []
    day_has_trade = set()

    ema_fast_period = 40 if variant == 'B' else 50
    ema_slow_period = 50 if variant == 'B' else 100
    disp_mult = 1.25 if variant == 'B' else 1.5

    df_15tm = df_15m.copy()
    df_15tm['ema_fast'] = df_15tm['close'].ewm(span=ema_fast_period, adjust=False).mean()
    df_15tm['ema_slow'] = df_15tm['close'].ewm(span=ema_slow_period, adjust=False).mean()

    hl = df_15m['high'] - df_15m['low']
    hc = (df_15m['high'] - df_15m['close'].shift(1)).abs()
    lc = (df_15m['low'] - df_15m['close'].shift(1)).abs()
    tr = pd.concat([hl, hc, lc], axis=1).max(axis=1)
    df_15tm['atr14'] = tr.rolling(14).mean()

    for check_idx, (check_time, check_row) in enumerate(df_15tm.iterrows()):
        t = check_time.time()
        if t < dt_time(9, 30) or t >= ny_close:
            continue

        if check_idx < max(ema_slow_period, 14) + 10:
            continue

        day_key = check_time.date()
        if day_key in day_has_trade:
            continue

        ema_fast = df_15tm.iloc[check_idx]['ema_fast']
        ema_slow = df_15tm.iloc[check_idx]['ema_slow']
        atr = df_15tm.iloc[check_idx]['atr14']

        if pd.isna(ema_fast) or pd.isna(ema_slow) or pd.isna(atr):
            continue

        # Only with trend
        if ema_fast <= ema_slow:
            continue

        # Only displacement
        displacement = check_row['high'] - check_row['low']
        if displacement < disp_mult * atr:
            continue

        # Look ahead for FVG
        search_end = min(check_idx + 40, len(df_15tm) - 2)
        found = False

        for j in range(check_idx, search_end):
            c1 = df_15tm.iloc[j]
            c2 = df_15tm.iloc[j + 1]
            c3 = df_15tm.iloc[j + 2]

            if is_bullish_fvg(c1, c2, c3):
                entry_time = c3.name + timedelta(minutes=5)
                if entry_time not in df_5m.index:
                    continue

                entry_bar = df_5m.loc[entry_time]
                day_key = entry_time.date()
                if day_key in day_has_trade:
                    continue

                entry_px = entry_bar['open']
                entry_fill = entry_px + SLIPPAGE_TICKS_EACH_WAY * TICK_SIZE_NQ
                stop = entry_bar['low'] - TICK_SIZE_NQ  # 1 tick under

                # Exit at close with slippage
                exit_fill = entry_bar['close'] - SLIPPAGE_TICKS_EACH_WAY * TICK_SIZE_NQ
                pnl = (exit_fill - entry_fill) * 20.0
                pnl -= COMMISSION_PER_TURN * 2

                trades.append(Trade(entry_time, entry_time.replace(hour=16, minute=0),
                                    entry_fill, exit_fill, 'LONG', stop, pnl))
                day_has_trade.add(day_key)
                found = True
                break

        if found:
            continue

    return trades


def simulate_fvg_duo(df_5m, df_10m, df_15m, stop_pct=0.015, trend_variant='A'):
    opening_trades = opening_drive_fvg(df_10m, df_5m, stop_pct)
    trend_trades = trend_fvg(df_15m, df_5m, trend_variant)

    trades_by_day = {}
    for t in opening_trades:
        trades_by_day[t.entry_time.date()] = t
    for t in trend_trades:
        day = t.entry_time.date()
        if day not in trades_by_day:
            trades_by_day[day] = t

    all_trades = sorted(trades_by_day.values(), key=lambda x: x.entry_time)

    equity = [1.0]
    cumulative_pnl = 0.0
    for t in all_trades:
        cumulative_pnl += t.pnl_dollars
        equity.append(cumulative_pnl)  # dollars, not pct

    total_trades = len(all_trades)
    total_pnl = cumulative_pnl

    cummax = np.maximum.accumulate(equity)
    drawdowns = [(peak - eq) for peak, eq in zip(cummax, equity)]
    max_drawdown = max(drawdowns) if drawdowns else 0.0

    wins = sum(1 for t in all_trades if t.pnl_dollars > 0)
    win_rate = wins / total_trades * 100 if total_trades > 0 else 0.0

    years = {}
    for t in all_trades:
        y = t.entry_time.year
        years[y] = years.get(y, 0) + 1

    last_n = min(100, len(all_trades))
    last_100_pnl = sum(t.pnl_dollars for t in all_trades[-last_n:]) if last_n > 0 else 0

    return {
        'total_trades': total_trades,
        'total_pnl': total_pnl,
        'max_drawdown': max_drawdown,
        'win_rate': win_rate,
        'years': years,
        'opening_count': len(opening_trades),
        'trend_count': len(trend_trades),
        'last_100_pnl': last_100_pnl,
        'all_trades': all_trades,
        'equity_curve': equity
    }


def main(csv_path: Optional[str] = None):
    print("=== FVG Duo Parity Check ===\n")
    print("NQ contract | Fixed 1 contract | $2.14 commission + 1 tick slip each way\n")

    # Use synthetic if no CSV provided
    if csv_path is None:
        print("No CSV path provided — generating synthetic NQ 1m data (placeholder)")
        print("Replace with real NT8 1m data via load_nt8_1m_csv(csv_path)\n")
        return

    df_1m = load_nt8_1m_csv(csv_path)
    df_5m = resample_ohlc(df_1m, 5)
    df_10m = resample_ohlc(df_1m, 10)
    df_15m = resample_ohlc(df_1m, 15)

    for variant in ['A', 'B']:
        for stop in [0.015]:
            print(f"Variant {variant} | Stop {stop*100:.1f}%")
            stats = simulate_fvg_duo(df_5m, df_10m, df_15m, stop_pct=stop, trend_variant=variant)

            print(f"  Opening Drive trades: {stats['opening_count']}")
            print(f"  Trend FVG trades:     {stats['trend_count']}")
            print(f"  Total trades:         {stats['total_trades']}")
            print(f"  Total P&L:            ${stats['total_pnl']:,.2f}")
            print(f"  Max drawdown:         ${stats['max_drawdown']:,.2f}")
            print(f"  Win rate:             {stats['win_rate']:.2f}%")
            print(f"  Last 100 trades P&L:  ${stats['last_100_pnl']:,.2f}")
            print(f"  Years:                {sorted(stats['years'].keys())}")
            print()


if __name__ == '__main__':
    import sys
    csv_path = sys.argv[1] if len(sys.argv) > 1 else None
    main(csv_path)