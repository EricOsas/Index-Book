import sys, time, pickle
from pathlib import Path
ROOT = Path(__file__).parent.resolve()
sys.path.insert(0, str(ROOT))

from fvg_duo_engine import make_config, run_backtest_with_bars
import pandas as pd

RUNS_DIR = ROOT / "runs"
CACHE_DIR = ROOT / "cache"

with open(CACHE_DIR / "prepared_data.pkl", "rb") as f:
    data = pickle.load(f)

rolls_csv = ROOT / "roll_dates.csv"
rolls_df = pd.read_csv(rolls_csv)
trading_sorted = sorted(data["trading_dates"])

from datetime import datetime
near_roll_set = set()
for _, row in rolls_df.iterrows():
    r_date = datetime.strptime(str(row["date"])[:10], "%Y-%m-%d").date()
    sess_indices = [i for i, d in enumerate(trading_sorted) if d >= r_date]
    if sess_indices:
        idx0 = sess_indices[0]
        for k in range(3):
            if idx0 + k < len(trading_sorted):
                near_roll_set.add(trading_sorted[idx0 + k])

# Base RMA / c1 / strict=True
base_rma = dict(
    atr_method="rma",
    atr_ref="c1",
    fvg_strict=True,
)

variants = [
    ("RMA_B1_0slip", make_config(**base_rma, trend_atr_mult=1.5, trend_ema_fast=50, trend_ema_slow=100, slip_ticks=0)),
    ("RMA_B1_1slip", make_config(**base_rma, trend_atr_mult=1.5, trend_ema_fast=50, trend_ema_slow=100, slip_ticks=1)),
    ("RMA_B3a_0slip", make_config(**base_rma, trend_atr_mult=1.25, trend_ema_fast=50, trend_ema_slow=100, slip_ticks=0)),
    ("RMA_B3a_1slip", make_config(**base_rma, trend_atr_mult=1.25, trend_ema_fast=50, trend_ema_slow=100, slip_ticks=1)),
    ("RMA_B2_0slip", make_config(**base_rma, trend_atr_mult=1.25, trend_ema_fast=40, trend_ema_slow=50, slip_ticks=0)),
    ("RMA_B2_1slip", make_config(**base_rma, trend_atr_mult=1.25, trend_ema_fast=40, trend_ema_slow=50, slip_ticks=1)),
]

print("="*80)
print("RMA / C1 / STRICT=TRUE VARIANT MATRIX (Full Period 2010-2026)")
print("="*80)

for name, cfg in variants:
    res = run_backtest_with_bars(data, cfg, near_roll_set, output_dir=RUNS_DIR)
    ret_dd = res["net_usd"] / res["max_dd_closed"] if res["max_dd_closed"] > 0 else 0
    print(f"{name:15s}: trades={res['trade_count']:5d}, net=${res['net_usd']:10,.2f}, PF={res['profit_factor']:.2f}, WR={res['win_rate']:.1f}%, DD=${res['max_dd_closed']:8,.2f}, Return/DD={ret_dd:5.2f}")

# Also write the dedicated RMA oracle directory
print("\nWriting RMA B1 and RMA B3a oracle directories ...")
oracle_rma_b1_dir = RUNS_DIR / "oracle_RMA_B1"
oracle_rma_b3a_dir = RUNS_DIR / "oracle_RMA_B3a"

cfg_rma_b1_1slip = make_config(**base_rma, trend_atr_mult=1.5, trend_ema_fast=50, trend_ema_slow=100, slip_ticks=1)
cfg_rma_b3a_1slip = make_config(**base_rma, trend_atr_mult=1.25, trend_ema_fast=50, trend_ema_slow=100, slip_ticks=1)

res_o1 = run_backtest_with_bars(data, cfg_rma_b1_1slip, near_roll_set, output_dir=oracle_rma_b1_dir)
res_o2 = run_backtest_with_bars(data, cfg_rma_b3a_1slip, near_roll_set, output_dir=oracle_rma_b3a_dir)

# Copy trades.csv to root of oracle directory for easy access
import shutil
shutil.copy2(list(oracle_rma_b1_dir.glob("run_*/trades.csv"))[0], oracle_rma_b1_dir / "trades.csv")
shutil.copy2(list(oracle_rma_b3a_dir.glob("run_*/trades.csv"))[0], oracle_rma_b3a_dir / "trades.csv")

# Also copy SMA oracle trades.csv to root of their dirs
for odir in [RUNS_DIR / "oracle_B1_video", RUNS_DIR / "oracle_B2_variant"]:
    tfiles = list(odir.glob("run_*/trades.csv"))
    if tfiles:
        shutil.copy2(tfiles[0], odir / "trades.csv")

print("All Oracle trades.csv placed in root of their oracle directories.")
