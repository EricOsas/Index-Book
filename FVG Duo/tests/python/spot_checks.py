"""
Spot Checks script for FVG Duo — Spec v1.txt Section 14.
Prints complete signal and trade detail for:
 - 2024-11-07
 - Every year's August 4
 - Every year's June 29
 - Every year's October 16
"""
import os, sys, time, pickle
from pathlib import Path
from datetime import datetime, date as date_t

ROOT = Path(__file__).parent.resolve()
sys.path.insert(0, str(ROOT))

from fvg_duo_engine import (
    load_1m_csv,
    prepare_backtest_data,
    run_day,
    od_signal_for_date,
    compute_trend_signals,
    compute_indicators,
    make_config,
)
from run_experiments import get_prepared_data, get_near_roll_dates

def run_spot_checks():
    data = get_prepared_data()
    roll_dates_csv = ROOT / "roll_dates.csv"
    near_roll_set = get_near_roll_dates(roll_dates_csv, data["trading_dates"])

    # Load B1 Video config
    cfg = make_config(
        trend_atr_mult=1.5,
        trend_ema_fast=50,
        trend_ema_slow=100,
        trend_rr=3.0,
        od_stop_pct=0.015,
        slip_ticks=0,
    )

    bars15 = compute_indicators(data["bars15"], cfg)
    all_trend_sigs = compute_trend_signals(bars15, data["b5_index"], cfg)

    trend_by_date = {}
    for s in all_trend_sigs:
        trend_by_date.setdefault(s["date"], []).append(s)

    b5_idx = data["b5_index"]
    b10_idx = data["b10_index"]
    df1m_by_date = data["df1m_by_date"]
    all_dates = set(data["trading_dates"])

    print("=" * 80)
    print("SPOT-CHECK DAYS EVALUATION (Spec Section 14)")
    print("=" * 80)

    # 1. 2024-11-07
    target_dates = [datetime(2024, 11, 7).date()]

    # 2. Every year's August 4
    for y in range(2010, 2027):
        target_dates.append(datetime(y, 8, 4).date())

    # 3. Every year's June 29
    for y in range(2010, 2027):
        target_dates.append(datetime(y, 6, 29).date())

    # 4. Every year's October 16
    for y in range(2010, 2027):
        target_dates.append(datetime(y, 10, 16).date())

    for td in sorted(target_dates):
        if td not in all_dates:
            # Check if weekend or non-trading day
            weekday_str = td.strftime("%A")
            if td.year <= 2026:
                print(f"\n--- Date: {td} ({weekday_str}) --- [NO RTH DATA / NON-TRADING DAY]")
            continue

        weekday_str = td.strftime("%A")
        print(f"\n" + "-" * 70)
        print(f"SPOT CHECK: {td} ({weekday_str})")
        print("-" * 70)

        bars1m = df1m_by_date.get(td)
        b10_day = b10_idx.get(td, {})
        b5_day = b5_idx.get(td, {})

        # OD signal
        od_sig = od_signal_for_date(b10_day, b5_day, cfg, td)
        if od_sig:
            print(f"  [OD SIGNAL] Check #{od_sig['check_no']}, Side: {od_sig['side']}, Entry: {od_sig['entry_time'].strftime('%H:%M')}, Price: {od_sig['entry_price']:.2f}, Stop: {od_sig['stop_price']:.2f}")
        else:
            print("  [OD SIGNAL] None (neither Check 1 nor Check 2 fired FVG)")

        # Trend signals
        t_sigs = trend_by_date.get(td, [])
        if t_sigs:
            for s in t_sigs:
                print(f"  [TREND SIGNAL] Entry: {s['entry_time'].strftime('%H:%M')}, Price: {s['entry_price']:.2f}, Stop: {s['stop_price']:.2f}, Target: {s['target_price']:.2f}")
        else:
            print("  [TREND SIGNAL] None generated")

        # Run day
        trades, sig_log = run_day(td, od_sig, t_sigs, bars1m, cfg, near_roll_set)
        if trades:
            print("  [EXECUTED TRADES]")
            for tr in trades:
                print(f"    Trade #{tr['trade_id']} | Source: {tr['source']:6s} | Side: {tr['side']:5s} | Entry: {tr['entry_ts'][11:16]} @ {tr['entry_price']:.2f} | Exit: {tr['exit_ts'][11:16]} @ {tr['exit_price']:.2f} | Reason: {tr['exit_reason']:6s} | Pts: {tr['points']:+7.2f} | Net: ${tr['net_usd']:+9.2f}")
        else:
            print("  [EXECUTED TRADES] None")

        if sig_log:
            print("  [SIGNALS LOG]")
            for sl in sig_log:
                print(f"    Type: {sl.get('type')}, Time: {sl.get('entry_time')}, Reason: {sl.get('reason')}")

if __name__ == "__main__":
    run_spot_checks()
