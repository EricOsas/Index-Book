"""
Run B6 + B7 + Oracle export (B1 and B2).
All prior Phase A and B1-B5 results are taken from the already-written runs/ directories.
B1 params: sma/c2/strict=True, mult=1.5, EMA 50/100, rr=3, od_stop=1.5%, slip=1-tick (DEFAULT)
B2 params: sma/c2/strict=True, mult=1.25, EMA 40/50, rr=3, od_stop=1.5%, slip=1-tick (DEFAULT)
"""
import sys, time, pickle
from pathlib import Path
ROOT = Path(__file__).parent.resolve()
sys.path.insert(0, str(ROOT))

from fvg_duo_engine import make_config, run_backtest_with_bars, config_hash
import pandas as pd

RUNS_DIR = ROOT / "runs"
CACHE_DIR = ROOT / "cache"

# ── Load cache ────────────────────────────────────────────────────────────────
print("Loading prepared data from cache …")
t0 = time.time()
with open(CACHE_DIR / "prepared_data.pkl", "rb") as f:
    data = pickle.load(f)
print(f"Loaded in {time.time()-t0:.1f}s ({len(data['trading_dates'])} trading days)")

rolls_csv = ROOT / "roll_dates.csv"
rolls_df = pd.read_csv(rolls_csv)
trading_sorted = sorted(data["trading_dates"])

near_roll_set = set()
from datetime import datetime
for _, row in rolls_df.iterrows():
    r_date = datetime.strptime(str(row["date"])[:10], "%Y-%m-%d").date()
    sess_indices = [i for i, d in enumerate(trading_sorted) if d >= r_date]
    if sess_indices:
        idx0 = sess_indices[0]
        for k in range(3):
            if idx0 + k < len(trading_sorted):
                near_roll_set.add(trading_sorted[idx0 + k])

print(f"{len(near_roll_set)} near-roll dates loaded.")

# ── Locked convention ─────────────────────────────────────────────────────────
# Selected by Phase A: sma_c2_strict=True (smallest |count-1038|)
base_locked = dict(
    atr_method="sma",
    atr_ref="c2",
    fvg_strict=True,
    # slip_ticks intentionally not here — each call sets it
)

# ── B1 / B2 configs (1-tick slip = default) ───────────────────────────────────
cfg_b1 = make_config(**base_locked, trend_atr_mult=1.5,  trend_ema_fast=50, trend_ema_slow=100, trend_rr=3.0, od_stop_pct=0.015)
cfg_b2 = make_config(**base_locked, trend_atr_mult=1.25, trend_ema_fast=40, trend_ema_slow=50,  trend_rr=3.0, od_stop_pct=0.015)

# Recompute B1 and B2 (should hit run cache by hash)
print("\nRecomputing B1 (should use cached run) …")
res_b1 = run_backtest_with_bars(data, cfg_b1, near_roll_set, output_dir=RUNS_DIR)
print(f"B1: trades={res_b1['trade_count']}, net=${res_b1['net_usd']:,.2f}, PF={res_b1['profit_factor']:.2f}, WR={res_b1['win_rate']:.1f}%, DD=${res_b1['max_dd_closed']:,.2f}")

print("Recomputing B2 …")
res_b2 = run_backtest_with_bars(data, cfg_b2, near_roll_set, output_dir=RUNS_DIR)
print(f"B2: trades={res_b2['trade_count']}, net=${res_b2['net_usd']:,.2f}, PF={res_b2['profit_factor']:.2f}, WR={res_b2['win_rate']:.1f}%, DD=${res_b2['max_dd_closed']:,.2f}")

# ── B6: 0-tick vs 1-tick slip on B1 params ───────────────────────────────────
print("\n--- B6: Slippage comparison (0-tick vs 1-tick, B1 params) ---")
cfg_b6_0 = make_config(**base_locked, trend_atr_mult=1.5, trend_ema_fast=50, trend_ema_slow=100, trend_rr=3.0, od_stop_pct=0.015, slip_ticks=0)
res_b6_0 = run_backtest_with_bars(data, cfg_b6_0, near_roll_set, output_dir=RUNS_DIR)
print(f"  B6 0-tick slip: trades={res_b6_0['trade_count']:5d}, net=${res_b6_0['net_usd']:10,.2f}, PF={res_b6_0['profit_factor']:.2f}, DD=${res_b6_0['max_dd_closed']:8,.2f}")
print(f"  B6 1-tick slip: trades={res_b1['trade_count']:5d},     net=${res_b1['net_usd']:10,.2f}, PF={res_b1['profit_factor']:.2f}, DD=${res_b1['max_dd_closed']:8,.2f}")
print(f"  Slip cost delta: ${res_b6_0['net_usd'] - res_b1['net_usd']:,.2f}")

# ── B7: merge_anchor test ─────────────────────────────────────────────────────
print("\n--- B7: merge_anchor='trend_entry' vs 'od_signal' (B1 params, 1-tick slip) ---")
cfg_b7_te = make_config(**base_locked, trend_atr_mult=1.5, trend_ema_fast=50, trend_ema_slow=100, trend_rr=3.0, od_stop_pct=0.015, merge_anchor="trend_entry")
res_b7_te = run_backtest_with_bars(data, cfg_b7_te, near_roll_set, output_dir=RUNS_DIR)
cfg_b7_od = make_config(**base_locked, trend_atr_mult=1.5, trend_ema_fast=50, trend_ema_slow=100, trend_rr=3.0, od_stop_pct=0.015, merge_anchor="od_signal")
res_b7_od = run_backtest_with_bars(data, cfg_b7_od, near_roll_set, output_dir=RUNS_DIR)
print(f"  merge_anchor='trend_entry': trades={res_b7_te['trade_count']:5d}, net=${res_b7_te['net_usd']:10,.2f}, PF={res_b7_te['profit_factor']:.2f}")
print(f"  merge_anchor='od_signal':   trades={res_b7_od['trade_count']:5d}, net=${res_b7_od['net_usd']:10,.2f}, PF={res_b7_od['profit_factor']:.2f}")
print(f"  Net delta (od_signal - trend_entry): ${res_b7_od['net_usd'] - res_b7_te['net_usd']:,.2f}")

# ── Oracle export ─────────────────────────────────────────────────────────────
print("\n--- Writing Oracle CSV files ---")
oracle_b1_dir = RUNS_DIR / "oracle_B1_video"
oracle_b2_dir = RUNS_DIR / "oracle_B2_variant"
run_backtest_with_bars(data, cfg_b1, near_roll_set, output_dir=oracle_b1_dir)
run_backtest_with_bars(data, cfg_b2, near_roll_set, output_dir=oracle_b2_dir)
print(f"  Oracle B1 → {oracle_b1_dir / 'trades.csv'}")
print(f"  Oracle B2 → {oracle_b2_dir / 'trades.csv'}")

print("\n=== B6 + B7 + ORACLE COMPLETE ===")
print(f"\nSUMMARY (all at 1-tick slip unless noted):")
print(f"  B1 (video params, 1-slip): trades={res_b1['trade_count']}, net=${res_b1['net_usd']:,.0f}, PF={res_b1['profit_factor']:.2f}, WR={res_b1['win_rate']:.1f}%")
print(f"  B2 (variant, 1-slip):      trades={res_b2['trade_count']}, net=${res_b2['net_usd']:,.0f}, PF={res_b2['profit_factor']:.2f}, WR={res_b2['win_rate']:.1f}%")
print(f"  B6 (B1 at 0-slip):         trades={res_b6_0['trade_count']}, net=${res_b6_0['net_usd']:,.0f}")
print(f"  B7 te:                     trades={res_b7_te['trade_count']}, net=${res_b7_te['net_usd']:,.0f}")
print(f"  B7 od:                     trades={res_b7_od['trade_count']}, net=${res_b7_od['net_usd']:,.0f}")
