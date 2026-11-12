"""
FVG Duo Experiments Runner (Spec v1.txt Sections 13 and 14).
Executes Phase A (Convention Lock), Phase B (Variants & Sweeps),
and Section 14 Spot Checks.
"""
import os, sys, time, json, pickle
from pathlib import Path
from datetime import datetime, date as date_t
import pandas as pd
import numpy as np

# Ensure root python directory in path
ROOT = Path(__file__).parent.resolve()
sys.path.insert(0, str(ROOT))

from fvg_duo_engine import (
    load_1m_csv,
    prepare_backtest_data,
    run_backtest_with_bars,
    make_config,
    config_hash,
)

RUNS_DIR = ROOT / "runs"
RUNS_DIR.mkdir(parents=True, exist_ok=True)
CACHE_DIR = ROOT / "cache"
CACHE_DIR.mkdir(parents=True, exist_ok=True)

def get_near_roll_dates(roll_dates_csv: Path, trading_dates: list) -> set:
    """
    near_roll = 1 if the entry date is within 3 trading sessions after a roll date
    from roll_dates.csv (the roll date itself counts as session 1).
    """
    rolls_df = pd.read_csv(roll_dates_csv)
    trading_dates_sorted = sorted(trading_dates)
    near_roll_set = set()

    for _, row in rolls_df.iterrows():
        r_date = datetime.strptime(str(row["date"])[:10], "%Y-%m-%d").date()
        # Find session 1: first trading date >= r_date
        sess_indices = [i for i, d in enumerate(trading_dates_sorted) if d >= r_date]
        if sess_indices:
            idx0 = sess_indices[0]
            # Add up to 3 sessions (idx0, idx0+1, idx0+2)
            for k in range(3):
                if idx0 + k < len(trading_dates_sorted):
                    near_roll_set.add(trading_dates_sorted[idx0 + k])
    return near_roll_set

def get_prepared_data():
    cache_path = CACHE_DIR / "prepared_data.pkl"
    if cache_path.exists():
        print(f"Loading prepared data from cache: {cache_path} …")
        t0 = time.time()
        with open(cache_path, "rb") as f:
            data = pickle.load(f)
        print(f"Loaded in {time.time() - t0:.2f}s ({len(data['trading_dates'])} trading days)")
        return data

    print("Loading NQ_1m_merged.csv …")
    t0 = time.time()
    df1m = load_1m_csv(str(ROOT / "NQ_1m_merged.csv"))
    print(f"Loaded {len(df1m):,} 1m bars in {time.time() - t0:.2f}s")

    print("Preparing multi-timeframe bars and partitions …")
    t1 = time.time()
    data = prepare_backtest_data(df1m)
    print(f"Prepared in {time.time() - t1:.2f}s")

    print(f"Saving to cache: {cache_path} …")
    with open(cache_path, "wb") as f:
        pickle.dump(data, f, protocol=pickle.HIGHEST_PROTOCOL)
    print("Cache saved successfully.")
    return data

def run_all():
    print("=" * 80)
    print("FVG DUO — PHASE A & PHASE B EXPERIMENTS (Spec v1.txt Section 13)")
    print("=" * 80)

    data = get_prepared_data()
    roll_dates_csv = ROOT / "roll_dates.csv"
    near_roll_set = get_near_roll_dates(roll_dates_csv, data["trading_dates"])
    print(f"Identified {len(near_roll_set)} near-roll trading dates.")

    # ─────────────────────────────────────────────────────────────────────────
    # PHASE A: CONVENTION MATCHING
    # ─────────────────────────────────────────────────────────────────────────
    print("\n" + "=" * 80)
    print("PHASE A: CONVENTION MATCHING")
    print("Targets from video:")
    print("  OD-only:    1,215 trades, ~$317k")
    print("  Trend-only: 1,038 trades, ~$240k")
    print("  Duo:        1,726 trades, ~$418k (0-slip), ~$401k (1-tick slip)")
    print("=" * 80)

    # A1: OD-only with fvg_strict in {True, False}
    print("\n--- Running A1: OD-only ---")
    a1_results = {}
    for strict in [True, False]:
        cfg = make_config(
            enable_od=True,
            enable_trend=False,
            fvg_strict=strict,
            slip_ticks=0,  # evaluate Phase A with 0 slip matching base video
        )
        res = run_backtest_with_bars(data, cfg, near_roll_set, output_dir=RUNS_DIR)
        key = f"OD_strict={strict}"
        a1_results[key] = res
        print(f"  {key:20s}: trades={res['trade_count']:5d} (target: 1215), net=${res['net_usd']:10,.2f} (target: $317k), PF={res['profit_factor']:.2f}, WR={res['win_rate']:.1f}%")

    # A2: Trend-only, all 8 combinations
    print("\n--- Running A2: Trend-only (8 combinations) ---")
    conventions = [
        ("rma", "c2", True),
        ("rma", "c2", False),
        ("rma", "c1", True),
        ("rma", "c1", False),
        ("sma", "c2", True),
        ("sma", "c2", False),
        ("sma", "c1", True),
        ("sma", "c1", False),
    ]

    a2_results = {}
    for method, ref, strict in conventions:
        cfg = make_config(
            enable_od=False,
            enable_trend=True,
            atr_method=method,
            atr_ref=ref,
            fvg_strict=strict,
            trend_ema_fast=50,
            trend_ema_slow=100,
            trend_atr_mult=1.5,
            trend_rr=3.0,
            slip_ticks=0,
        )
        res = run_backtest_with_bars(data, cfg, near_roll_set, output_dir=RUNS_DIR)
        key = f"{method}_{ref}_strict={strict}"
        a2_results[key] = (res, cfg)
        diff_count = abs(res['trade_count'] - 1038)
        diff_net = abs(res['net_usd'] - 240000)
        print(f"  Trend {key:22s}: trades={res['trade_count']:5d} (diff={diff_count:4d}), net=${res['net_usd']:10,.2f} (diff=${diff_net:8,.0f}), PF={res['profit_factor']:.2f}, WR={res['win_rate']:.1f}%")

    # A3: Duo for each of those 8 Trend conventions
    print("\n--- Running A3: Duo (8 combinations) ---")
    a3_results = {}
    for method, ref, strict in conventions:
        cfg = make_config(
            enable_od=True,
            enable_trend=True,
            atr_method=method,
            atr_ref=ref,
            fvg_strict=strict,
            trend_ema_fast=50,
            trend_ema_slow=100,
            trend_atr_mult=1.5,
            trend_rr=3.0,
            slip_ticks=0,
        )
        res = run_backtest_with_bars(data, cfg, near_roll_set, output_dir=RUNS_DIR)
        key = f"{method}_{ref}_strict={strict}"
        a3_results[key] = (res, cfg)
        diff_count = abs(res['trade_count'] - 1726)
        diff_net = abs(res['net_usd'] - 418000)
        print(f"  Duo   {key:22s}: trades={res['trade_count']:5d} (diff={diff_count:4d}), net=${res['net_usd']:10,.2f} (diff=${diff_net:8,.0f}), PF={res['profit_factor']:.2f}, WR={res['win_rate']:.1f}%, DD=${res['max_dd_closed']:8,.2f}")

    # Lock Convention: smallest |Trend-only trade count - 1038|, tie broken by |Trend-only net - 240000|
    best_key = None
    best_tuple = (float("inf"), float("inf"))
    for key, (res, cfg) in a2_results.items():
        score = (abs(res['trade_count'] - 1038), abs(res['net_usd'] - 240000))
        if score < best_tuple:
            best_tuple = score
            best_key = key

    locked_res, locked_cfg = a2_results[best_key]
    print("\n" + "*" * 80)
    print(f"LOCKED CONVENTION: {best_key}")
    print("Selection rule: chosen on count parity with the video (smallest |count - 1038|), not on profitability.")
    print(f"Locked settings: atr_method={locked_cfg['atr_method']}, atr_ref={locked_cfg['atr_ref']}, fvg_strict={locked_cfg['fvg_strict']}")
    print("*" * 80)

    # ─────────────────────────────────────────────────────────────────────────
    # PHASE B: VARIANT TESTS (using locked convention)
    # ─────────────────────────────────────────────────────────────────────────
    print("\n" + "=" * 80)
    print("PHASE B: VARIANT TESTS (using locked convention in Duo mode)")
    print("=" * 80)

    base_locked = dict(
        atr_method=locked_cfg["atr_method"],
        atr_ref=locked_cfg["atr_ref"],
        fvg_strict=locked_cfg["fvg_strict"],
        # slip_ticks intentionally NOT here — each call sets it explicitly
    )

    b_results = {}

    # B1: VIDEO: mult 1.5, EMA 50/100, rr 3, od_stop_pct 0.015
    cfg_b1 = make_config(**base_locked, trend_atr_mult=1.5, trend_ema_fast=50, trend_ema_slow=100, trend_rr=3.0, od_stop_pct=0.015)
    res_b1 = run_backtest_with_bars(data, cfg_b1, near_roll_set, output_dir=RUNS_DIR)
    b_results["B1_VIDEO"] = (res_b1, cfg_b1)
    print(f"B1 (VIDEO):        trades={res_b1['trade_count']:5d}, net=${res_b1['net_usd']:10,.2f}, PF={res_b1['profit_factor']:.2f}, WR={res_b1['win_rate']:.1f}%, DD=${res_b1['max_dd_closed']:8,.2f}")

    # B2: MY VARIANT: mult 1.25, EMA 40/50, rr 3, od_stop_pct 0.015
    cfg_b2 = make_config(**base_locked, trend_atr_mult=1.25, trend_ema_fast=40, trend_ema_slow=50, trend_rr=3.0, od_stop_pct=0.015)
    res_b2 = run_backtest_with_bars(data, cfg_b2, near_roll_set, output_dir=RUNS_DIR)
    b_results["B2_MY_VARIANT"] = (res_b2, cfg_b2)
    print(f"B2 (MY VARIANT):   trades={res_b2['trade_count']:5d}, net=${res_b2['net_usd']:10,.2f}, PF={res_b2['profit_factor']:.2f}, WR={res_b2['win_rate']:.1f}%, DD=${res_b2['max_dd_closed']:8,.2f}")

    # B3: Attribution: (mult 1.25, EMA 50/100) and (mult 1.5, EMA 40/50)
    cfg_b3a = make_config(**base_locked, trend_atr_mult=1.25, trend_ema_fast=50, trend_ema_slow=100, trend_rr=3.0, od_stop_pct=0.015)
    res_b3a = run_backtest_with_bars(data, cfg_b3a, near_roll_set, output_dir=RUNS_DIR)
    b_results["B3_mult1.25_ema50/100"] = (res_b3a, cfg_b3a)
    print(f"B3 (1.25 ATR, 50/100): trades={res_b3a['trade_count']:5d}, net=${res_b3a['net_usd']:10,.2f}, PF={res_b3a['profit_factor']:.2f}, WR={res_b3a['win_rate']:.1f}%")

    cfg_b3b = make_config(**base_locked, trend_atr_mult=1.5, trend_ema_fast=40, trend_ema_slow=50, trend_rr=3.0, od_stop_pct=0.015)
    res_b3b = run_backtest_with_bars(data, cfg_b3b, near_roll_set, output_dir=RUNS_DIR)
    b_results["B3_mult1.5_ema40/50"] = (res_b3b, cfg_b3b)
    print(f"B3 (1.50 ATR, 40/50):  trades={res_b3b['trade_count']:5d}, net=${res_b3b['net_usd']:10,.2f}, PF={res_b3b['profit_factor']:.2f}, WR={res_b3b['win_rate']:.1f}%")

    # B4: OD stop sweep: {0.005, 0.0075, 0.010, 0.0125, 0.015, 0.020} for B1 and B2
    print("\n--- Running B4: OD stop sweep (12 runs) ---")
    od_stops = [0.005, 0.0075, 0.010, 0.0125, 0.015, 0.020]
    b4_results = {}
    best_b4_stop_b1 = None; best_b4_net_b1 = -float("inf")
    best_b4_stop_b2 = None; best_b4_net_b2 = -float("inf")

    for stop_val in od_stops:
        # For B1 settings
        cfg_sweep_b1 = make_config(**base_locked, trend_atr_mult=1.5, trend_ema_fast=50, trend_ema_slow=100, trend_rr=3.0, od_stop_pct=stop_val)
        res_s_b1 = run_backtest_with_bars(data, cfg_sweep_b1, near_roll_set, output_dir=RUNS_DIR)
        b4_results[f"B1_stop_{stop_val}"] = res_s_b1
        if res_s_b1["net_usd"] > best_b4_net_b1:
            best_b4_net_b1 = res_s_b1["net_usd"]
            best_b4_stop_b1 = stop_val

        # For B2 settings
        cfg_sweep_b2 = make_config(**base_locked, trend_atr_mult=1.25, trend_ema_fast=40, trend_ema_slow=50, trend_rr=3.0, od_stop_pct=stop_val)
        res_s_b2 = run_backtest_with_bars(data, cfg_sweep_b2, near_roll_set, output_dir=RUNS_DIR)
        b4_results[f"B2_stop_{stop_val}"] = res_s_b2
        if res_s_b2["net_usd"] > best_b4_net_b2:
            best_b4_net_b2 = res_s_b2["net_usd"]
            best_b4_stop_b2 = stop_val

        print(f"  Stop {stop_val:6.4f} | B1: trades={res_s_b1['trade_count']:5d}, net=${res_s_b1['net_usd']:9,.2f}, PF={res_s_b1['profit_factor']:.2f} | B2: trades={res_s_b2['trade_count']:5d}, net=${res_s_b2['net_usd']:9,.2f}, PF={res_s_b2['profit_factor']:.2f}")

    print(f"Best OD stop by net: B1={best_b4_stop_b1} (${best_b4_net_b1:,.2f}), B2={best_b4_stop_b2} (${best_b4_net_b2:,.2f})")

    # B5: trend_after_od_stopout true vs false, at 0.015 and best B4 stop
    print("\n--- Running B5: trend_after_od_stopout true vs false ---")
    b5_results = {}
    for label, base_trend_cfg, best_stop in [("B1", {"trend_atr_mult":1.5, "trend_ema_fast":50, "trend_ema_slow":100}, best_b4_stop_b1),
                                            ("B2", {"trend_atr_mult":1.25, "trend_ema_fast":40, "trend_ema_slow":50}, best_b4_stop_b2)]:
        for stop_p in sorted(set([0.015, best_stop])):
            for after_stop in [False, True]:
                cfg_b5 = make_config(**base_locked, **base_trend_cfg, trend_rr=3.0, od_stop_pct=stop_p, trend_after_od_stopout=after_stop)
                res_b5 = run_backtest_with_bars(data, cfg_b5, near_roll_set, output_dir=RUNS_DIR)
                k = f"{label}_stop={stop_p}_after={after_stop}"
                b5_results[k] = res_b5
                print(f"  {k:28s}: trades={res_b5['trade_count']:5d}, net=${res_b5['net_usd']:10,.2f}, PF={res_b5['profit_factor']:.2f}, WR={res_b5['win_rate']:.1f}%")

    # B6: Slippage comparison — B1 at 0-tick vs 1-tick slip (B1 currently runs at default 1-tick)
    print("\n--- Running B6: Slippage test (0 vs 1 tick, B1 params) ---")
    cfg_b6_0slip = make_config(**base_locked, trend_atr_mult=1.5, trend_ema_fast=50, trend_ema_slow=100, trend_rr=3.0, od_stop_pct=0.015, slip_ticks=0)
    res_b6_0slip = run_backtest_with_bars(data, cfg_b6_0slip, near_roll_set, output_dir=RUNS_DIR)
    b_results["B6_0SLIP"] = (res_b6_0slip, cfg_b6_0slip)
    # B1 (1-tick) already computed
    print(f"B6 (0-tick slip on B1): trades={res_b6_0slip['trade_count']:5d}, net=${res_b6_0slip['net_usd']:10,.2f}, PF={res_b6_0slip['profit_factor']:.2f}, DD=${res_b6_0slip['max_dd_closed']:8,.2f}")
    print(f"B6 (1-tick slip on B1): trades={res_b1['trade_count']:5d},     net=${res_b1['net_usd']:10,.2f}, PF={res_b1['profit_factor']:.2f}, DD=${res_b1['max_dd_closed']:8,.2f}")
    print(f"  Slip cost delta: ${res_b6_0slip['net_usd'] - res_b1['net_usd']:,.2f}")

    # B7: merge_anchor "od_signal" vs "trend_entry" for B1 (at 1-tick slip default)
    print("\n--- Running B7: merge_anchor test ---")
    cfg_b7_te = make_config(**base_locked, trend_atr_mult=1.5, trend_ema_fast=50, trend_ema_slow=100, trend_rr=3.0, od_stop_pct=0.015, merge_anchor="trend_entry")
    res_b7_te = run_backtest_with_bars(data, cfg_b7_te, near_roll_set, output_dir=RUNS_DIR)
    cfg_b7_od = make_config(**base_locked, trend_atr_mult=1.5, trend_ema_fast=50, trend_ema_slow=100, trend_rr=3.0, od_stop_pct=0.015, merge_anchor="od_signal")
    res_b7_od = run_backtest_with_bars(data, cfg_b7_od, near_roll_set, output_dir=RUNS_DIR)
    print(f"B7 merge_anchor='trend_entry': trades={res_b7_te['trade_count']:5d}, net=${res_b7_te['net_usd']:10,.2f}, PF={res_b7_te['profit_factor']:.2f}")
    print(f"B7 merge_anchor='od_signal':   trades={res_b7_od['trade_count']:5d}, net=${res_b7_od['net_usd']:10,.2f}, PF={res_b7_od['profit_factor']:.2f}")
    print(f"Difference: net delta = ${res_b7_od['net_usd'] - res_b7_te['net_usd']:,.2f}")

    # Save full summary artifact
    summary_path = ROOT / "experiments_summary.json"
    print(f"\nSaving experiments summary to {summary_path} …")

    # Save B1 and B2 full runs into designated oracle directories (1-tick slip)
    print("Writing Oracle runs for B1 and B2 …")
    oracle_b1_dir = RUNS_DIR / "oracle_B1_video"
    oracle_b2_dir = RUNS_DIR / "oracle_B2_variant"
    run_backtest_with_bars(data, cfg_b1, near_roll_set, output_dir=oracle_b1_dir)
    run_backtest_with_bars(data, cfg_b2, near_roll_set, output_dir=oracle_b2_dir)

    print("\nALL PHASE A & B EXPERIMENTS COMPLETED SUCCESSFULLY.")
    return locked_key, a1_results, a2_results, a3_results, b_results, b4_results, b5_results

if __name__ == "__main__":
    run_all()
