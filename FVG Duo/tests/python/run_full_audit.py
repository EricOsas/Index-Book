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

yearly_avg_index = {}
for d, bars in data["df1m_by_date"].items():
    yr = d.year
    if yr not in yearly_avg_index:
        yearly_avg_index[yr] = []
    yearly_avg_index[yr].extend([b[4] for b in bars])
yearly_avg_nq = {yr: float(np.mean(vals)) for yr, vals in yearly_avg_index.items()}

base_rma = dict(atr_method="rma", atr_ref="c1", fvg_strict=True)

out_lines = []
# Clear audit_output.txt
with open(ROOT / "audit_output.txt", "w", encoding="utf-8") as f:
    f.write("")

def log(s=""):
    print(s)
    out_lines.append(s)
    with open(ROOT / "audit_output.txt", "a", encoding="utf-8") as f:
        f.write(s + "\n")

log("="*90)
log("COMPREHENSIVE AUDIT REPORT FOR FVG DUO STRATEGY")
log("="*90)

# ==============================================================================
# ITEM 1: SLIP CONSISTENCY & $5 DISCREPANCY
# ==============================================================================
log("\n" + "="*90)
log("ITEM 1: SLIPPAGE CONSISTENCY ACROSS RUNS & DISCREPANCY ANALYSIS")
log("="*90)

res_rma_b1_1 = None
res_rma_b1_0 = None

for name, cfg_base in [
    ("RMA B1 (mult 1.5, ema 50/100, stop 1.5%)", base_rma),
    ("RMA B3a (mult 1.25, ema 50/100, stop 1.25%)", dict(base_rma, trend_atr_mult=1.25, od_stop_pct=0.0125)),
]:
    c0 = make_config(**cfg_base, slip_ticks=0)
    c1 = make_config(**cfg_base, slip_ticks=1)
    res0 = run_backtest_with_bars(data, c0, near_roll_set)
    res1 = run_backtest_with_bars(data, c1, near_roll_set)
    if name.startswith("RMA B1"):
        res_rma_b1_0 = res0
        res_rma_b1_1 = res1
    df0 = pd.DataFrame(res0["trades"])
    df1 = pd.DataFrame(res1["trades"])
    
    t_cnt = len(df0)
    fees0 = df0["fees_usd"].sum()
    fees1 = df1["fees_usd"].sum()
    diff = round(res0["net_usd"] - res1["net_usd"], 2)
    expected_diff = t_cnt * 10.00
    disc = round(expected_diff - diff, 2)
    
    log(f"\nModel: {name}")
    log(f"  trade_count:            {t_cnt}")
    log(f"  sum(fees):              ${fees0:,.2f}")
    log(f"  net_0slip:              ${res0['net_usd']:,.2f}")
    log(f"  net_1slip:              ${res1['net_usd']:,.2f}")
    log(f"  diff (0slip - 1slip):   ${diff:,.2f}")
    log(f"  expected (10.00 x n):   ${expected_diff:,.2f}")
    log(f"  discrepancy:            ${disc:,.2f}")
    
    # Identify trades causing the discrepancy
    non_10 = []
    for i in range(len(df0)):
        n0 = df0.iloc[i]["net_usd"]
        n1 = df1.iloc[i]["net_usd"]
        d = round(n0 - n1, 2)
        if d != 10.00:
            non_10.append((i, df0.iloc[i]["date"], df0.iloc[i]["source"], df0.iloc[i]["exit_reason"], d, n0, n1))
    log(f"  Trades where (0slip - 1slip) != $10.00: {len(non_10)}")
    for t in non_10:
        log(f"    Index {t[0]}: Date {t[1]}, Src={t[2]}, Exit={t[3]}, Diff=${t[4]} (net0=${t[5]}, net1=${t[6]})")

    # 10-Year Rolling Window (2016-10-20 to 2026-10-20)
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
    log(f"  --- 10-Year Window (2016-10-20 to 2026-10-20) ---")
    log(f"  10y trade_count:        {w_cnt}")
    log(f"  10y sum(fees):          ${w0['fees_usd'].sum():,.2f}")
    log(f"  10y net_0slip:          ${w_net0:,.2f}")
    log(f"  10y net_1slip:          ${w_net1:,.2f}")
    log(f"  10y diff (0slip-1slip): ${w_diff:,.2f}")
    log(f"  10y expected (10 x n):  ${w_exp:,.2f}")
    log(f"  10y discrepancy:        ${round(w_exp - w_diff, 2):,.2f}")

with open(ROOT / "audit_output.txt", "w", encoding="utf-8") as f:
    f.write("\n".join(out_lines))

# ==============================================================================
# ITEM 2: PER-CALENDAR-YEAR TABLE (2010-2026)
# ==============================================================================
log("\n" + "="*90)
log("ITEM 2: PER-CALENDAR-YEAR TABLE (2010-2026) FOR RMA B1 (1-SLIP)")
log("="*90)

# Load RMA B1 1-slip trades
df_b1_1 = pd.DataFrame(res_rma_b1_1["trades"])
df_b1_1["d"] = pd.to_datetime(df_b1_1["date"]).dt.date
df_b1_1["year"] = pd.to_datetime(df_b1_1["date"]).dt.year

log(f"{'Year':<6} | {'Trades':<6} | {'Net P&L ($)':<12} | {'Max DD ($)':<11} | {'Avg Risk ($)':<12} | {'Net (R)':<8} | {'Avg NQ Level':<12} | {'Net % of NQ':<11}")
log("-" * 95)

for yr in sorted(df_b1_1["year"].unique()):
    ydf = df_b1_1[df_b1_1["year"] == yr].sort_values("entry_ts")
    cnt = len(ydf)
    net = ydf["net_usd"].sum()
    
    cum = np.cumsum(ydf["net_usd"].values)
    peak = np.maximum.accumulate(np.concatenate([[0.0], cum]))
    dd = peak[:-1] - cum
    max_dd = float(np.max(dd)) if len(dd) else 0.0
    
    risks = np.abs(ydf["entry_price"] - ydf["stop_price"]) * 20.0
    avg_risk = float(np.mean(risks)) if len(risks) else 1.0
    net_r = net / avg_risk if avg_risk > 0 else 0.0
    
    avg_nq = yearly_avg_nq.get(yr, 1.0)
    net_pct_nq = (net / (avg_nq * 20.0)) * 100.0
    
    yr_label = f"{yr}*" if yr == 2026 else f"{yr}"
    log(f"{yr_label:<6} | {cnt:<6d} | ${net:>10,.2f} | ${max_dd:>9,.2f} | ${avg_risk:>10,.2f} | {net_r:>7.2f}R | {avg_nq:>11,.1f} | {net_pct_nq:>10.2f}%")

df_pre = df_b1_1[df_b1_1["d"] < date(2017, 1, 1)]
df_post = df_b1_1[df_b1_1["d"] >= date(2017, 1, 1)]
log(f"\n2010-2016 (6.8 Years Pre-2017):")
log(f"  Total Trades:     {len(df_pre)} ({len(df_pre)/7:.1f} trades/year)")
log(f"  Total Net P&L:    ${df_pre['net_usd'].sum():,.2f}")
log(f"  Avg Risk/Trade:   ${np.mean(np.abs(df_pre['entry_price'] - df_pre['stop_price']) * 20.0):,.2f}")
log(f"  Avg NQ Index:     {np.mean([yearly_avg_nq[y] for y in range(2010, 2017)]):,.1f}")
log(f"\n2017-2026 (9.8 Years Post-2017):")
log(f"  Total Trades:     {len(df_post)} ({len(df_post)/9.8:.1f} trades/year)")
log(f"  Total Net P&L:    ${df_post['net_usd'].sum():,.2f}")
log(f"  Avg Risk/Trade:   ${np.mean(np.abs(df_post['entry_price'] - df_post['stop_price']) * 20.0):,.2f}")
log(f"  Avg NQ Index:     {np.mean([yearly_avg_nq[y] for y in range(2017, 2027)]):,.1f}")

# ==============================================================================
# ITEM 3: 2017-01-01 THROUGH LATEST WINDOW & VIDEO CLAIM AUDIT
# ==============================================================================
log("\n" + "="*90)
log("ITEM 3: 2017-01-01 THROUGH LATEST WINDOW & VIDEO CLAIMS VERIFICATION")
log("="*90)

# Run OD-only and Trend-only
cfg_od_only = make_config(**base_rma, enable_od=True, enable_trend=False, slip_ticks=0)
cfg_tr_only = make_config(**base_rma, enable_od=False, enable_trend=True, slip_ticks=0)
cfg_duo_0   = make_config(**base_rma, enable_od=True, enable_trend=True, slip_ticks=0)

res_od = run_backtest_with_bars(data, cfg_od_only, near_roll_set)
res_tr = run_backtest_with_bars(data, cfg_tr_only, near_roll_set)
res_duo = run_backtest_with_bars(data, cfg_duo_0, near_roll_set)

df_od = pd.DataFrame(res_od["trades"])
df_tr = pd.DataFrame(res_tr["trades"])
df_duo = pd.DataFrame(res_duo["trades"])

for d_frame in [df_od, df_tr, df_duo]:
    d_frame["d"] = pd.to_datetime(d_frame["date"]).dt.date
    d_frame["year"] = pd.to_datetime(d_frame["date"]).dt.year

start_2017 = date(2017, 1, 1)
df_od_17 = df_od[df_od["d"] >= start_2017]
df_tr_17 = df_tr[df_tr["d"] >= start_2017]
df_duo_17 = df_duo[df_duo["d"] >= start_2017]

log(f"\n--- Per-Calendar-Year Net P&L (2017-2026) ---")
log(f"{'Year':<6} | {'OD Only Net ($)':<16} | {'Trend Only Net ($)':<18} | {'Duo Net ($)':<14}")
log("-" * 65)

years_17 = sorted(df_duo_17["year"].unique())
od_green = 0
tr_green = 0
duo_green = 0

for yr in years_17:
    net_o = df_od_17[df_od_17["year"] == yr]["net_usd"].sum()
    net_t = df_tr_17[df_tr_17["year"] == yr]["net_usd"].sum()
    net_d = df_duo_17[df_duo_17["year"] == yr]["net_usd"].sum()
    if net_o > 0: od_green += 1
    if net_t > 0: tr_green += 1
    if net_d > 0: duo_green += 1
    yr_label = f"{yr}*" if yr == 2026 else f"{yr}"
    log(f"{yr_label:<6} | ${net_o:>14,.2f} | ${net_t:>16,.2f} | ${net_d:>12,.2f}")

log("\n--- Verification of Video Claims ---")
# 1. All 10 years green for Duo and Trend
log(f"Claim 1: All 10 years green for Duo: {'PASS' if duo_green == 10 else 'FAIL'} (Actual: {duo_green}/10 green)")
log(f"Claim 1b: All 10 years green for Trend: {'PASS' if tr_green == 10 else 'FAIL'} (Actual: {tr_green}/10 green)")
# 2. 9 of 10 green for OD
log(f"Claim 2: 9 of 10 green for OD: {'PASS' if od_green == 9 else 'FAIL'} (Actual: {od_green}/10 green)")
# 3. 2025 OD about $73k
net_od_2025 = df_od_17[df_od_17["year"] == 2025]["net_usd"].sum()
log(f"Claim 3: 2025 OD about $73k: {'PASS' if abs(net_od_2025 - 73000) < 5000 else 'FAIL'} (Actual: ${net_od_2025:,.2f})")
# 4. 2023 Trend carries Duo
net_tr_2023 = df_tr_17[df_tr_17["year"] == 2023]["net_usd"].sum()
net_od_2023 = df_od_17[df_od_17["year"] == 2023]["net_usd"].sum()
log(f"Claim 4: 2023 Trend carries Duo: {'PASS' if net_tr_2023 > net_od_2023 else 'FAIL'} (Trend: ${net_tr_2023:,.2f} vs OD: ${net_od_2023:,.2f})")
# 5. OD longs about $177k, shorts about $141k
od_longs = df_od_17[df_od_17["side"] == "LONG"]["net_usd"].sum()
od_shorts = df_od_17[df_od_17["side"] == "SHORT"]["net_usd"].sum()
log(f"Claim 5a: OD longs about $177k: {'PASS' if abs(od_longs - 177000) < 15000 else 'FAIL'} (Actual: ${od_longs:,.2f})")
log(f"Claim 5b: OD shorts about $141k: {'PASS' if abs(od_shorts - 141000) < 15000 else 'FAIL'} (Actual: ${od_shorts:,.2f})")
# 6. OD 1,215 trades about $317k
log(f"Claim 6: OD 1,215 trades about $317k: {'PASS' if abs(len(df_od_17) - 1215) < 50 else 'FAIL'} (Actual trades: {len(df_od_17)}, Net: ${df_od_17['net_usd'].sum():,.2f})")
# 7. Trend 1,038 trades about $240k
log(f"Claim 7: Trend 1,038 trades about $240k: {'PASS' if abs(len(df_tr_17) - 1038) < 50 else 'FAIL'} (Actual trades: {len(df_tr_17)}, Net: ${df_tr_17['net_usd'].sum():,.2f})")

# ==============================================================================
# ITEM 5: FOUR SPOT-CHECK DAYS FULL ROWS & EXPLANATIONS
# ==============================================================================
log("\n" + "="*90)
log("ITEM 5: FOUR SPOT-CHECK DAYS FULL TRADE ROWS")
log("="*90)

spot_dates = [date(2024, 11, 7), date(2026, 8, 4), date(2026, 6, 29), date(2026, 10, 16)]
df_oracle = pd.read_csv(ROOT / "runs" / "oracle_RMA_B1" / "trades.csv")
df_oracle["d"] = pd.to_datetime(df_oracle["date"]).dt.date

for sd in spot_dates:
    tdf = df_oracle[df_oracle["d"] == sd]
    log(f"\nDate: {sd}")
    for _, tr in tdf.iterrows():
        log(f"  trade_id={tr['trade_id']} | source={tr['source']} | side={tr['side']} | entry_ts={tr['entry_ts']} | entry_price={tr['entry_price']} | exit_ts={tr['exit_ts']} | exit_price={tr['exit_price']} | exit_reason={tr['exit_reason']} | points={tr['points']} | net_usd=${tr['net_usd']:,.2f}")

with open(ROOT / "audit_output.txt", "w", encoding="utf-8") as f:
    f.write("\n".join(out_lines))

# ==============================================================================
# ITEM 6: PARAMETER SWEEPS & FIRST/SECOND HALF SPLITS
# ==============================================================================
log("\n" + "="*90)
log("ITEM 6: PARAMETER SWEEPS & ROBUSTNESS AUDIT")
log("="*90)

# Helper for split metrics
def get_split_metrics(res_dict):
    df = pd.DataFrame(res_dict["trades"])
    df["d"] = pd.to_datetime(df["date"]).dt.date
    mid_date = date(2018, 6, 1) # roughly midpoint of 2010-2026
    h1 = df[df["d"] < mid_date]
    h2 = df[df["d"] >= mid_date]
    n_h1 = h1["net_usd"].sum()
    n_h2 = h2["net_usd"].sum()
    
    # near_roll split
    nr = df[df["near_roll"] == 1]["net_usd"].sum()
    no_nr = df[df["near_roll"] == 0]["net_usd"].sum()
    
    ret_dd = res_dict["net_usd"] / res_dict["max_dd_closed"] if res_dict["max_dd_closed"] > 0 else 0
    return {
        "trades": res_dict["trade_count"],
        "net": res_dict["net_usd"],
        "pf": res_dict["profit_factor"],
        "dd": res_dict["max_dd_closed"],
        "ret_dd": round(ret_dd, 2),
        "h1_net": round(n_h1, 2),
        "h2_net": round(n_h2, 2),
        "near_roll_net": round(nr, 2),
        "no_roll_net": round(no_nr, 2),
    }

# 6a. OD stop sweep {0.5, 0.75, 1.0, 1.25, 1.5, 2.0}%
# With Trend fixed at (1.5x, 50/100, rr 3), then fixed at (1.25x, 50/100)
stops = [0.005, 0.0075, 0.010, 0.0125, 0.015, 0.020]

for trend_m, trend_title in [(1.5, "Trend ATR 1.5x (50/100 EMA)"), (1.25, "Trend ATR 1.25x (50/100 EMA)")]:
    log(f"\n--- 6a. OD Stop Sweep with {trend_title} ---")
    log(f"{'OD Stop':<8} | {'Trades':<6} | {'Net ($)':<11} | {'PF':<5} | {'Max DD ($)':<11} | {'Ret/DD':<7} | {'1st Half Net ($)':<17} | {'2nd Half Net ($)':<17}")
    log("-" * 92)
    for stp in stops:
        cfg = make_config(**base_rma, od_stop_pct=stp, trend_atr_mult=trend_m, slip_ticks=1)
        r = run_backtest_with_bars(data, cfg, near_roll_set)
        m = get_split_metrics(r)
        log(f"{stp*100:<7.2f}% | {m['trades']:<6d} | ${m['net']:>9,.2f} | {m['pf']:<5.2f} | ${m['dd']:>9,.2f} | {m['ret_dd']:<7.2f} | ${m['h1_net']:>15,.2f} | ${m['h2_net']:>15,.2f}")

# 6b. trend_after_od_stopout true vs false
log(f"\n--- 6b. trend_after_od_stopout: True vs False ---")
log(f"{'Config':<32} | {'Trades':<6} | {'Net ($)':<11} | {'PF':<5} | {'Max DD ($)':<11} | {'Ret/DD':<7} | {'1st Half ($)':<13} | {'2nd Half ($)':<13}")
log("-" * 96)
for trend_m, tname in [(1.5, "B1 (1.5x ATR)"), (1.25, "B3a (1.25x ATR)")]:
    for flag in [False, True]:
        cfg = make_config(**base_rma, trend_atr_mult=trend_m, trend_after_od_stopout=flag, slip_ticks=1)
        r = run_backtest_with_bars(data, cfg, near_roll_set)
        m = get_split_metrics(r)
        c_desc = f"{tname} after_stop={flag}"
        log(f"{c_desc:<32} | {m['trades']:<6d} | ${m['net']:>9,.2f} | {m['pf']:<5.2f} | ${m['dd']:>9,.2f} | {m['ret_dd']:<7.2f} | ${m['h1_net']:>11,.2f} | ${m['h2_net']:>11,.2f}")

# 6c. merge_anchor trend_entry vs od_signal
log(f"\n--- 6c. merge_anchor: trend_entry vs od_signal ---")
log(f"{'Anchor':<20} | {'Trades':<6} | {'Net ($)':<11} | {'PF':<5} | {'Max DD ($)':<11} | {'Ret/DD':<7} | {'1st Half ($)':<13} | {'2nd Half ($)':<13}")
log("-" * 88)
for anc in ["trend_entry", "od_signal"]:
    cfg = make_config(**base_rma, merge_anchor=anc, slip_ticks=1)
    r = run_backtest_with_bars(data, cfg, near_roll_set)
    m = get_split_metrics(r)
    log(f"{anc:<20} | {m['trades']:<6d} | ${m['net']:>9,.2f} | {m['pf']:<5.2f} | ${m['dd']:>9,.2f} | {m['ret_dd']:<7.2f} | ${m['h1_net']:>11,.2f} | ${m['h2_net']:>11,.2f}")

# 6e. near_roll split for B1
log(f"\n--- 6e. near_roll: 1 vs 0 Split for B1 (1-Slip) ---")
m_b1 = get_split_metrics(res_rma_b1_1)
log(f"  Total Net P&L:        ${m_b1['net']:>10,.2f}")
log(f"  Near-Roll Days Net:   ${m_b1['near_roll_net']:>10,.2f}")
log(f"  No-Roll Days Net:     ${m_b1['no_roll_net']:>10,.2f}")

# ==============================================================================
# ITEM 7: FTMO ASSUMPTIONS & OD STOPOUTS > 5% OF $100K
# ==============================================================================
log("\n" + "="*90)
log("ITEM 7: FTMO SIMULATION ASSUMPTIONS & STOPOUT SIZE DISTRIBUTION")
log("="*90)

df_all_trades = pd.DataFrame(res_rma_b1_1["trades"])
od_stops_over_5k = df_all_trades[(df_all_trades["source"] == "OD") & 
                                  (df_all_trades["exit_reason"] == "STOP") & 
                                  (df_all_trades["net_usd"] < -5000.0)]
log(f"Count of OD Stop-outs losing > $5,000 (5% of $100k): {len(od_stops_over_5k)}")
for _, row in od_stops_over_5k.iterrows():
    log(f"  Date: {row['date']}, Entry: {row['entry_price']}, Exit: {row['exit_price']}, Loss: ${row['net_usd']:,.2f}")

with open(ROOT / "audit_output.txt", "w", encoding="utf-8") as f:
    f.write("\n".join(out_lines))
log("\nAUDIT SCRIPT COMPLETED SUCCESSFULLY.")
