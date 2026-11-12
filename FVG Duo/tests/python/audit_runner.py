import sys, os, json, pickle
from pathlib import Path
from datetime import datetime, date, timedelta
import pandas as pd
import numpy as np

ROOT = Path(__file__).parent.resolve()
sys.path.insert(0, str(ROOT))

from fvg_duo_engine import make_config, run_backtest_with_bars, ET

with open(ROOT / "cache" / "prepared_data.pkl", "rb") as f:
    data = pickle.load(f)

rolls_csv = ROOT / "roll_dates.csv"
rolls_df = pd.read_csv(rolls_csv)
trading_sorted = sorted(data["trading_dates"])

near_roll_set = set()
for _, row in rolls_df.iterrows():
    r_date = datetime.strptime(str(row["date"])[:10], "%Y-%m-%d").date()
    sess_indices = [i for i, d in enumerate(trading_sorted) if d >= r_date]
    if sess_indices:
        idx0 = sess_indices[0]
        for k in range(3):
            if idx0 + k < len(trading_sorted):
                near_roll_set.add(trading_sorted[idx0 + k])

# Base configs
base_rma = dict(atr_method="rma", atr_ref="c1", fvg_strict=True)

# -------------------------------------------------------------
# AUDIT ITEM 1: Slip consistency & $5 discrepancy explanation
# -------------------------------------------------------------
print("\n" + "="*80)
print("AUDIT ITEM 1: SLIPPAGE CONSISTENCY ACROSS RUNS")
print("="*80)

def audit_slip_consistency(name, cfg_base):
    c0 = make_config(**cfg_base, slip_ticks=0)
    c1 = make_config(**cfg_base, slip_ticks=1)
    res0 = run_backtest_with_bars(data, c0, near_roll_set)
    res1 = run_backtest_with_bars(data, c1, near_roll_set)
    df0 = pd.read_csv(res0["run_dir"] / "trades.csv")
    df1 = pd.read_csv(res1["run_dir"] / "trades.csv")
    
    t_cnt = len(df0)
    fees = df0["fees_usd"].sum()
    diff = round(res0["net_usd"] - res1["net_usd"], 2)
    expected_diff = t_cnt * 10.00
    discrepancy = round(expected_diff - diff, 2)
    
    print(f"Run: {name}")
    print(f"  trade_count:       {t_cnt}")
    print(f"  sum(fees):         ${fees:,.2f}")
    print(f"  net_0slip:         ${res0['net_usd']:,.2f}")
    print(f"  net_1slip:         ${res1['net_usd']:,.2f}")
    print(f"  actual_slip_cost:  ${diff:,.2f}")
    print(f"  expected_slip_cost:${expected_diff:,.2f} ($10.00 x {t_cnt})")
    print(f"  discrepancy:       ${discrepancy:,.2f}")
    
    # 10y window: 2016-10-20 to 2026-10-20
    df0["d"] = pd.to_datetime(df0["date"]).dt.date
    df1["d"] = pd.to_datetime(df1["date"]).dt.date
    w_start = date(2016, 10, 20)
    w_end = date(2026, 10, 20)
    w0 = df0[(df0["d"] >= w_start) & (df0["d"] <= w_end)]
    w1 = df1[(df1["d"] >= w_start) & (df1["d"] <= w_end)]
    w_cnt = len(w0)
    w_net0 = w0["net_usd"].sum()
    w_net1 = w1["net_usd"].sum()
    w_diff = round(w_net0 - w_net1, 2)
    w_exp = w_cnt * 10.00
    print(f"  --- 10-Year Window (2016-10-20 to 2026-10-20) ---")
    print(f"  10y trade_count:   {w_cnt}")
    print(f"  10y net_0slip:     ${w_net0:,.2f}")
    print(f"  10y net_1slip:     ${w_net1:,.2f}")
    print(f"  10y slip_cost:     ${w_diff:,.2f}")
    print(f"  10y expected_cost: ${w_exp:,.2f} ($10.00 x {w_cnt})")
    print(f"  10y discrepancy:   ${round(w_exp - w_diff, 2):,.2f}")
    return res0, res1

res_rma_b1_0, res_rma_b1_1 = audit_slip_consistency("RMA B1 (mult 1.5, ema 50/100, stop 1.5%)", base_rma)
res_rma_b3a_0, res_rma_b3a_1 = audit_slip_consistency("RMA B3a (mult 1.25, ema 50/100, stop 1.25%)", 
    dict(base_rma, trend_atr_mult=1.25, od_stop_pct=0.0125))

# -------------------------------------------------------------
# AUDIT ITEM 2: Per-calendar-year table (2010-2026) for RMA B1
# -------------------------------------------------------------
print("\n" + "="*80)
print("AUDIT ITEM 2: PER-CALENDAR-YEAR TABLE (2010-2026)")
print("="*80)

def generate_annual_table(trades_df, title):
    df = trades_df.copy()
    df["d"] = pd.to_datetime(df["date"]).dt.date
    df["year"] = pd.to_datetime(df["date"]).dt.year
    
    # Also need daily average index level for NQ
    # From bars1m_by_day, compute average close per year
    yearly_avg_index = {}
    for d, bars in data["bars1m_by_day"].items():
        yr = d.year
        if yr not in yearly_avg_index:
            yearly_avg_index[yr] = []
        for b in bars:
            yearly_avg_index[yr].append(b[4]) # close
    yearly_avg_nq = {yr: np.mean(vals) for yr, vals in yearly_avg_index.items()}

    rows = []
    print(f"\n--- {title} ---")
    print(f"{'Year':<6} | {'Trades':<6} | {'Net P&L ($)':<12} | {'Max DD ($)':<11} | {'Avg Risk ($)':<12} | {'Net (R)':<8} | {'Avg NQ Level':<12} | {'Net % of NQ':<11}")
    print("-" * 95)
    
    for yr in sorted(df["year"].unique()):
        ydf = df[df["year"] == yr].sort_values("entry_ts")
        cnt = len(ydf)
        net = ydf["net_usd"].sum()
        
        # Max DD within the year
        cum = np.cumsum(ydf["net_usd"].values)
        peak = np.maximum.accumulate(np.concatenate([[0.0], cum]))
        dd = peak[:-1] - cum
        max_dd = float(np.max(dd)) if len(dd) else 0.0
        
        # Risk per trade: |entry_price - stop_price| * 20
        risks = np.abs(ydf["entry_price"] - ydf["stop_price"]) * 20.0
        avg_risk = float(np.mean(risks)) if len(risks) else 1.0
        net_r = net / avg_risk if avg_risk > 0 else 0.0
        
        avg_nq = yearly_avg_nq.get(yr, 1.0)
        net_pct_nq = (net / (avg_nq * 20.0)) * 100.0 # 1 contract notional value = avg_nq * 20
        
        yr_label = f"{yr}*" if yr == 2026 else f"{yr}"
        print(f"{yr_label:<6} | {cnt:<6d} | ${net:>10,.2f} | ${max_dd:>9,.2f} | ${avg_risk:>10,.2f} | {net_r:>7.2f}R | {avg_nq:>11,.1f} | {net_pct_nq:>10.2f}%")

df_b1_1 = pd.read_csv(res_rma_b1_1["run_dir"] / "trades.csv")
generate_annual_table(df_b1_1, "RMA B1 (1-Slip) Full History 2010-2026")

# Check 2010-2016 period specifically
df_pre = df_b1_1[df_b1_1["d"] < date(2017, 1, 1)]
print(f"\n2010-2016 (Pre-2017) Total Trades: {len(df_pre)}")
print(f"2010-2016 Total Net P&L: ${df_pre['net_usd'].sum():,.2f}")
print(f"2010-2016 Average Trades/Year: {len(df_pre)/7:.1f}")
print(f"2010-2016 Average NQ Level: {np.mean([yearly_avg_nq[y] for y in range(2010, 2017)]):,.1f}")
print(f"2017-2026 Average NQ Level: {np.mean([yearly_avg_nq[y] for y in range(2017, 2027)]):,.1f}")

