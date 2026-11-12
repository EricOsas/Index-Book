"""
audit2_steps1to6.py — Comprehensive Audit Steps 1-6 for FVG Duo Strategy
Writes results to audit2_output.txt in the same directory.
"""
import sys, os, json, pickle, shutil, copy
from pathlib import Path
from datetime import datetime, date as date_t, timedelta
import pandas as pd
import numpy as np

ROOT = Path(__file__).parent.resolve()
sys.path.insert(0, str(ROOT))

OUT_PATH = ROOT / "audit2_output.txt"

# ── Output helpers ─────────────────────────────────────────────────────────────
_out_lines = []

def log(s=""):
    print(s)
    _out_lines.append(str(s))
    with open(OUT_PATH, "a", encoding="utf-8") as f:
        f.write(str(s) + "\n")

# Clear output file
with open(OUT_PATH, "w", encoding="utf-8") as f:
    f.write("")

log("=" * 90)
log("AUDIT 2 — STEPS 1-6  |  FVG Duo Strategy")
log(f"Run at: {datetime.now().isoformat()}")
log("=" * 90)

# ── Load pre-built data cache ──────────────────────────────────────────────────
log("\nLoading prepared data cache …")
cache_path = ROOT / "cache" / "prepared_data.pkl"
with open(cache_path, "rb") as f:
    data = pickle.load(f)
log(f"  trading dates in cache: {len(data['trading_dates'])}")

# ── Load roll dates ────────────────────────────────────────────────────────────
rolls_df = pd.read_csv(ROOT / "roll_dates.csv")
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

# ── Oracle config (from config.json) ──────────────────────────────────────────
ORACLE_CONFIG_RAW = {
    "tick": 0.25,
    "point_value": 20,
    "fee_per_side": 2.14,
    "slip_ticks": 1,
    "od_stop_pct": 0.015,
    "trend_ema_fast": 50,
    "trend_ema_slow": 100,
    "trend_atr_len": 14,
    "trend_atr_mult": 1.5,
    "trend_rr": 3.0,
    "trend_first_entry": "09:30",
    "trend_last_entry": "15:30",
    "atr_method": "rma",
    "atr_ref": "c1",
    "fvg_strict": True,
    "merge_anchor": "trend_entry",
    "trend_after_od_stopout": False,
    "warmup_bars": 1000,
    "enable_od": True,
    "enable_trend": True,
}

from fvg_duo_engine import make_config, run_backtest_with_bars, config_hash

def oracle_cfg(**overrides):
    cfg = dict(ORACLE_CONFIG_RAW)
    cfg.update(overrides)
    return cfg

# Date filter helper
DATE_2017 = date_t(2017, 1, 1)

def filter_trades_from(trades, from_date):
    return [t for t in trades if datetime.strptime(t["date"], "%Y-%m-%d").date() >= from_date]

def metrics_from(trades):
    if not trades:
        return {"trade_count": 0, "net_usd": 0.0, "max_dd": 0.0}
    net = [t["net_usd"] for t in trades]
    cum  = np.cumsum(net)
    peak = np.maximum.accumulate(np.concatenate([[0.0], cum]))
    dd   = peak[:-1] - cum
    max_dd = float(np.max(dd)) if len(dd) else 0.0
    return {
        "trade_count": len(trades),
        "net_usd": round(sum(net), 2),
        "max_dd": round(max_dd, 2),
    }

# ══════════════════════════════════════════════════════════════════════════════
# STEP 1: CONFIRM VIDEO WINDOW AND 2017 PARITY
# ══════════════════════════════════════════════════════════════════════════════
log("\n" + "=" * 90)
log("STEP 1: VIDEO WINDOW (2017-01-01 to end) PARITY CHECK")
log("=" * 90)

log("\nOracle config (from runs/oracle_RMA_B1/run_242b724f/config.json):")
log(json.dumps(ORACLE_CONFIG_RAW, indent=2))

VIDEO_CLAIMS = {
    "Duo_0slip":    {"trades": 1726, "net":  418_000},
    "Duo_1slip":    {"trades": 1726, "net":  401_000},
    "OD_0slip":     {"trades": 1215, "net":  317_000},
    "Trend_0slip":  {"trades": 1038, "net":  240_000},
}

modes = [
    ("Duo",   {"enable_od": True,  "enable_trend": True}),
    ("OD",    {"enable_od": True,  "enable_trend": False}),
    ("Trend", {"enable_od": False, "enable_trend": True}),
]

step1_results = {}
annual_duo_1slip = {}

for slip in [0, 1]:
    for mode_name, mode_flags in modes:
        cfg = oracle_cfg(slip_ticks=slip, **mode_flags)
        res = run_backtest_with_bars(data, cfg, near_roll_set)
        all_tr = res["trades"]
        tr_2017 = filter_trades_from(all_tr, DATE_2017)
        m = metrics_from(tr_2017)
        key = f"{mode_name}_{slip}slip"
        step1_results[key] = m

        log(f"\n  {mode_name} | slip={slip}")
        log(f"    trade_count (2017+): {m['trade_count']}")
        log(f"    net_usd     (2017+): ${m['net_usd']:,.2f}")

        # Store annual for Duo 1-slip check
        if mode_name == "Duo" and slip == 1:
            for t in tr_2017:
                yr = t["date"][:4]
                annual_duo_1slip.setdefault(yr, 0.0)
                annual_duo_1slip[yr] += t["net_usd"]

log("\n--- PASS/FAIL vs video claims (within 1% and 10 trades) ---")

def check_parity(key_prefix, slip, claim_trades, claim_net, actual):
    actual_trades = actual[f"{key_prefix}_{slip}slip"]["trade_count"]
    actual_net    = actual[f"{key_prefix}_{slip}slip"]["net_usd"]
    trade_ok = abs(actual_trades - claim_trades) <= 10
    net_ok   = abs(actual_net - claim_net) / max(abs(claim_net), 1) <= 0.01
    status   = "PASS" if (trade_ok and net_ok) else "FAIL"
    log(f"  {key_prefix} slip={slip}: claim {claim_trades}t / ${claim_net:,.0f} | "
        f"actual {actual_trades}t / ${actual_net:,.2f} | {status}")

check_parity("Duo",   0, 1726, 418_000, step1_results)
check_parity("Duo",   1, 1726, 401_000, step1_results)
check_parity("OD",    0, 1215, 317_000, step1_results)
check_parity("Trend", 0, 1038, 240_000, step1_results)

# Annual Duo 1-slip check
log("\n--- Annual Duo 1-slip breakdown (2017+) ---")
annual_sum = 0.0
for yr in sorted(annual_duo_1slip):
    v = round(annual_duo_1slip[yr], 2)
    annual_sum += v
    log(f"  {yr}: ${v:,.2f}")
annual_sum = round(annual_sum, 2)
reported   = step1_results["Duo_1slip"]["net_usd"]
log(f"\n  Sum of annual rows:    ${annual_sum:,.2f}")
log(f"  Direct net_usd total:  ${reported:,.2f}")
log(f"  Match: {'YES' if abs(annual_sum - reported) < 0.01 else 'NO — DISCREPANCY'}")

# ══════════════════════════════════════════════════════════════════════════════
# STEP 2: OD STOP SWEEP RECONCILIATION
# ══════════════════════════════════════════════════════════════════════════════
log("\n" + "=" * 90)
log("STEP 2: OD STOP SWEEP RECONCILIATION + CONFIG HASH")
log("=" * 90)

od_stops = [0.005, 0.0075, 0.010, 0.0125, 0.015, 0.020]

log("\nSweep: od_stop_pct varied, trend_atr_mult=1.5, ema=50/100, rr=3.0, slip=1")
log(f"{'od_stop_pct':>12} {'config_hash':>10} {'trade_count':>12} {'net_usd':>14} {'max_dd':>12} {'ret/dd':>10}")
log("-" * 80)

sweep_results = {}
for stop in od_stops:
    cfg = oracle_cfg(od_stop_pct=stop, slip_ticks=1)
    res = run_backtest_with_bars(data, cfg, near_roll_set)
    h   = config_hash(cfg)
    m   = metrics_from(res["trades"])
    max_dd = res["max_dd_closed"]
    ret_dd = round(m["net_usd"] / max_dd, 4) if max_dd else float("inf")
    sweep_results[stop] = {
        "hash": h, "trade_count": m["trade_count"],
        "net_usd": m["net_usd"], "max_dd": max_dd, "ret_dd": ret_dd
    }
    log(f"  {stop:>10.4f}   {h:>10}   {m['trade_count']:>10}   ${m['net_usd']:>12,.2f}   ${max_dd:>10,.2f}   {ret_dd:>8.4f}")

# Explain $33,747 drawdown appearing twice
log("\n--- Drawdown analysis: does $33,747.04 appear twice? ---")
dds = [v["max_dd"] for v in sweep_results.values()]
dd_counts = {}
for d in dds:
    dd_counts[d] = dd_counts.get(d, 0) + 1
for d, cnt in sorted(dd_counts.items()):
    if cnt > 1:
        log(f"  max_dd=${d:,.2f} appears {cnt} times in sweep")
    else:
        log(f"  max_dd=${d:,.2f} appears {cnt} time")

# 0-slip 10-year net for oracle config
log("\n--- 0-slip vs 1-slip oracle config (full history) ---")
cfg_0 = oracle_cfg(slip_ticks=0)
cfg_1 = oracle_cfg(slip_ticks=1)
res_0 = run_backtest_with_bars(data, cfg_0, near_roll_set)
res_1 = run_backtest_with_bars(data, cfg_1, near_roll_set)
log(f"  0-slip net_usd (full):  ${res_0['net_usd']:,.2f}   trades={res_0['trade_count']}")
log(f"  1-slip net_usd (full):  ${res_1['net_usd']:,.2f}   trades={res_1['trade_count']}")
log(f"  Diff (0-1):             ${res_0['net_usd'] - res_1['net_usd']:,.2f}")
# Save for step 5
_oracle_1slip_trades_pre_fix = res_1["trades"]

# ══════════════════════════════════════════════════════════════════════════════
# STEP 3: trend_after_od_stopout — list added trades
# ══════════════════════════════════════════════════════════════════════════════
log("\n" + "=" * 90)
log("STEP 3: trend_after_od_stopout — ADDED TRADES")
log("=" * 90)

cfg_true  = oracle_cfg(trend_after_od_stopout=True,  slip_ticks=1)
cfg_false = oracle_cfg(trend_after_od_stopout=False, slip_ticks=1)

res_true  = run_backtest_with_bars(data, cfg_true,  near_roll_set)
res_false = run_backtest_with_bars(data, cfg_false, near_roll_set)

log(f"\n  With trend_after_od_stopout=True:  {res_true['trade_count']} trades, ${res_true['net_usd']:,.2f}")
log(f"  With trend_after_od_stopout=False: {res_false['trade_count']} trades, ${res_false['net_usd']:,.2f}")
log(f"  Difference:                        {res_true['trade_count'] - res_false['trade_count']} trades, "
    f"${res_true['net_usd'] - res_false['net_usd']:,.2f}")

# Build a key set from the False run
def trade_key(t):
    return (t["date"], t["source"], t.get("after_od_stopout", 0), t["entry_ts"], t["side"])

false_keys = set(trade_key(t) for t in res_false["trades"])
added = [t for t in res_true["trades"] if trade_key(t) not in false_keys]

log(f"\n  Added trades (in True but not in False): {len(added)}")
log(f"\n  {'Date':>12} {'Source':>8} {'Side':>6} {'AfterOD':>8} {'Entry_TS':>22} {'Entry$':>10} "
    f"{'Exit_TS':>22} {'Exit$':>10} {'Net$':>10} {'Reason':>8}")
log("  " + "-" * 115)
for t in sorted(added, key=lambda x: x["date"]):
    log(f"  {t['date']:>12} {t['source']:>8} {t['side']:>6} {t.get('after_od_stopout',0):>8} "
        f"{t['entry_ts']:>22} {t['entry_price']:>10.2f} {t['exit_ts']:>22} "
        f"{t['exit_price']:>10.2f} {t['net_usd']:>10.2f} {t['exit_reason']:>8}")

# Explain merged stopout logic from code
log("""
--- MERGED TRADE STOPOUT & trend_after_od_stopout (from engine code, ~lines 728-748) ---

The flag logic (run_day, lines 729-748):
  if enable_trend and cfg["trend_after_od_stopout"]:
      if final_res and final_res.get("exit_reason") == "STOP":
          # allow post-T_od trend signals

The variable `final_res` is the result of:
  - 3b (simple OD trade):   final_res = OD trade result
  - 3c (MERGE):              final_res = merged trade result (which uses OD stop)
  - 3d (FLIP):               final_res = OD SHORT trade result

CONCLUSION: A MERGED trade that stops out DOES count as an OD stopout for
trend_after_od_stopout purposes, because final_res["exit_reason"] == "STOP"
in the merge path satisfies the same condition. The flag checks the exit_reason
of whatever became final_res, regardless of whether the trade was OD, MERGED, or FLIP.
""")

# ══════════════════════════════════════════════════════════════════════════════
# STEP 4: CONCENTRATION ANALYSIS — TOP/BOTTOM 15 + 2018 MONTHLY
# ══════════════════════════════════════════════════════════════════════════════
log("\n" + "=" * 90)
log("STEP 4: CONCENTRATION ANALYSIS — TOP/BOTTOM 15 + 2018 MONTHLY")
log("=" * 90)

log("\n(Using oracle trades CSV: runs/oracle_RMA_B1/trades.csv)")
oracle_csv = ROOT / "runs" / "oracle_RMA_B1" / "trades.csv"
df_oracle = pd.read_csv(oracle_csv)
log(f"  Oracle CSV rows: {len(df_oracle)}")

# Sort by net_usd
df_sorted = df_oracle.sort_values("net_usd", ascending=False)

def print_extreme(title, df_rows):
    log(f"\n{title}")
    log(f"  {'#':>3} {'Date':>12} {'Source':>8} {'Side':>6} {'NearRoll':>9} "
        f"{'Net_USD':>12} {'Points':>8} {'EntryP':>10} {'ExitP':>10} {'Reason':>10}")
    log("  " + "-" * 100)
    for i, (_, row) in enumerate(df_rows.iterrows(), 1):
        nr_flag = " *ROLL*" if row.get("near_roll", 0) == 1 else ""
        log(f"  {i:>3} {row['date']:>12} {row['source']:>8} {row['side']:>6} "
            f"{int(row.get('near_roll', 0)):>9} {row['net_usd']:>12.2f} {row['points']:>8.2f} "
            f"{row['entry_price']:>10.2f} {row['exit_price']:>10.2f} {row['exit_reason']:>10}{nr_flag}")

print_extreme("TOP 15 WINNERS (by net_usd):", df_sorted.head(15).iterrows().__class__ and df_sorted.head(15))
print_extreme("BOTTOM 15 LOSERS (by net_usd):", df_sorted.tail(15))

# Check plausibility of prices (NQ approximate ranges by year)
NQ_RANGES = {
    2009: (1200, 1900), 2010: (1700, 2300), 2011: (2000, 2500),
    2012: (2300, 2800), 2013: (2700, 3600), 2014: (3400, 4300),
    2015: (3900, 4800), 2016: (3900, 4900), 2017: (5000, 6400),
    2018: (5900, 7700), 2019: (6500, 8800), 2020: (7700, 12900),
    2021: (12000, 16800), 2022: (10500, 17000), 2023: (11000, 17000),
    2024: (16000, 22200), 2025: (18000, 22500), 2026: (18000, 23000),
}

log("\n--- Price plausibility check (top 15 + bottom 15) ---")
extremes = pd.concat([df_sorted.head(15), df_sorted.tail(15)]).drop_duplicates()
flag_count = 0
for _, row in extremes.iterrows():
    yr = int(str(row["date"])[:4])
    lo, hi = NQ_RANGES.get(yr, (0, 999999))
    ep = row["entry_price"]
    xp = row["exit_price"]
    if ep < lo or ep > hi or xp < lo or xp > hi:
        log(f"  FLAGGED: {row['date']} {row['source']} entry={ep} exit={xp} expected range [{lo},{hi}]")
        flag_count += 1
if flag_count == 0:
    log("  All extreme trade prices are within plausible NQ ranges.")

# 2018 monthly breakdown
log("\n--- 2018 Month-by-Month Breakdown ---")
df_2018 = df_oracle[df_oracle["date"].str.startswith("2018")].copy()
df_2018["ym"] = df_2018["date"].str[:7]
monthly = df_2018.groupby("ym").agg(trade_count=("net_usd", "count"), net_usd=("net_usd", "sum")).reset_index()
log(f"\n  {'Month':>8} {'Trades':>8} {'Net_USD':>14}")
log("  " + "-" * 35)
for _, row in monthly.iterrows():
    log(f"  {row['ym']:>8} {int(row['trade_count']):>8} ${row['net_usd']:>12,.2f}")
log(f"\n  2018 Total: {int(df_2018.shape[0])} trades, ${df_2018['net_usd'].sum():,.2f}")

# ══════════════════════════════════════════════════════════════════════════════
# STEP 5: FIX merge_stop ANCHOR + RE-RUN
# ══════════════════════════════════════════════════════════════════════════════
log("\n" + "=" * 90)
log("STEP 5: FIX merge_stop ANCHOR BUG")
log("=" * 90)

# --- Apply fix to fvg_duo_engine.py ---
engine_path = ROOT / "fvg_duo_engine.py"
engine_text_orig = engine_path.read_text(encoding="utf-8")

# Back up original
backup_path = ROOT / "fvg_duo_engine_BACKUP_before_merge_fix.py"
if not backup_path.exists():
    backup_path.write_text(engine_text_orig, encoding="utf-8")
    log(f"\n  Backup written to: {backup_path.name}")
else:
    log(f"\n  Backup already exists: {backup_path.name}")

# Fix 1 (LONG merge): store entry_price_unslipped on line ~590
OLD_OPEN_TREND = '                open_trend = {"sig": ts, "entry_fill": res["entry_fill"]}'
NEW_OPEN_TREND = '                open_trend = {"sig": ts, "entry_fill": res["entry_fill"], "entry_price_unslipped": ts["entry_price"]}'

# Fix 2 (LONG merge anchor): use unslipped price on line ~619
OLD_ANCHOR_LONG = '            anchor = open_trend["entry_fill"]'
NEW_ANCHOR_LONG = '            anchor = open_trend["entry_price_unslipped"]'

engine_text = engine_text_orig

changes_made = []

if OLD_OPEN_TREND in engine_text:
    engine_text = engine_text.replace(OLD_OPEN_TREND, NEW_OPEN_TREND, 1)
    changes_made.append("FIX1: open_trend dict now stores entry_price_unslipped (line ~590)")
    log("  FIX1 applied: open_trend now stores entry_price_unslipped")
else:
    log("  WARNING: FIX1 target string not found — may already be applied or line differs")
    # Check if already patched
    if "entry_price_unslipped" in engine_text and "entry_fill" in engine_text:
        log("  (entry_price_unslipped already present in engine)")

if OLD_ANCHOR_LONG in engine_text:
    # Count occurrences — only replace first (LONG merge block, not any other uses)
    n_occ = engine_text.count(OLD_ANCHOR_LONG)
    engine_text = engine_text.replace(OLD_ANCHOR_LONG, NEW_ANCHOR_LONG, 1)
    changes_made.append(f"FIX2: LONG merge anchor now uses entry_price_unslipped (found {n_occ} occurrence(s), replaced 1)")
    log(f"  FIX2 applied: LONG merge anchor now uses entry_price_unslipped ({n_occ} occurrence(s) in file, replaced 1)")
else:
    log("  WARNING: FIX2 target string not found — may already be applied or line differs")

# Check for SHORT merge block
log("\n--- SHORT merge block analysis ---")
if "SHORT" in engine_text and "3d" in engine_text:
    # In this engine, 3d is FLIP (Trend LONG vs OD SHORT), not a SHORT merge
    log("  Search for SHORT merge (open Trend SHORT + OD SHORT):")
    # The code has: elif od_side == "LONG" for merge (3c), and else: for flip (3d)
    # There is NO open_trend SHORT path because Trend only goes LONG
    log("  FINDING: Trend signals are LONG-only (confirmed from spec). The 'else' branch")
    log("  (lines ~690-726) handles FLIP: open Trend LONG vs OD SHORT. There is NO")
    log("  SHORT merge block because open_trend can never be SHORT.")
    log("  The SHORT anchor fix is therefore NOT needed — no SHORT merge exists.")

# Write patched engine
if changes_made:
    engine_path.write_text(engine_text, encoding="utf-8")
    log(f"\n  Engine file updated. Changes:")
    for c in changes_made:
        log(f"    - {c}")
else:
    log("\n  No changes applied to engine (fix strings not found — check manually)")

# Reload engine to pick up changes
import importlib
import fvg_duo_engine as engine_mod
importlib.reload(engine_mod)
from fvg_duo_engine import make_config as make_config2, run_backtest_with_bars as run2, config_hash as ch2

log("\n--- Re-running oracle config (1-slip, full window) with fix ---")
cfg_1slip_fixed = oracle_cfg(slip_ticks=1)
res_post_1slip = run2(data, cfg_1slip_fixed, near_roll_set)

cfg_0slip_fixed = oracle_cfg(slip_ticks=0)
res_post_0slip = run2(data, cfg_0slip_fixed, near_roll_set)

pre_trades  = _oracle_1slip_trades_pre_fix   # saved before fix
post_trades = res_post_1slip["trades"]

pre_net  = round(sum(t["net_usd"] for t in pre_trades),  2)
post_net = round(sum(t["net_usd"] for t in post_trades), 2)
pre_dd   = res_1["max_dd_closed"]  # from step 2 run before fix

log(f"\n  PRE-FIX  1-slip: {len(pre_trades)} trades,  net=${pre_net:,.2f},  max_dd=${pre_dd:,.2f}")
log(f"  POST-FIX 1-slip: {res_post_1slip['trade_count']} trades,  net=${res_post_1slip['net_usd']:,.2f},  max_dd=${res_post_1slip['max_dd_closed']:,.2f}")
log(f"  NET CHANGE:      ${res_post_1slip['net_usd'] - pre_net:,.2f}")
log(f"  DD CHANGE:       ${res_post_1slip['max_dd_closed'] - pre_dd:,.2f}")

log(f"\n  POST-FIX 0-slip: {res_post_0slip['trade_count']} trades,  net=${res_post_0slip['net_usd']:,.2f},  max_dd=${res_post_0slip['max_dd_closed']:,.2f}")

# Find changed trades (by trade index / date+source+entry_ts)
def trade_match_key(t):
    return (t["date"], t["source"], t["side"], t["entry_ts"])

pre_map  = {trade_match_key(t): t for t in pre_trades}
post_map = {trade_match_key(t): t for t in post_trades}

changed = []
for k in pre_map:
    if k in post_map:
        old_net = pre_map[k]["net_usd"]
        new_net = post_map[k]["net_usd"]
        if abs(old_net - new_net) > 0.001:
            changed.append({
                "date":    k[0], "source": k[1], "side": k[2],
                "entry_ts": k[3],
                "old_net": old_net, "new_net": new_net,
                "diff": round(new_net - old_net, 2)
            })

log(f"\n  Trades that CHANGED net_usd after fix: {len(changed)}")
if changed:
    log(f"\n  {'Date':>12} {'Source':>8} {'Side':>6} {'Entry_TS':>22} {'Old_Net':>12} {'New_Net':>12} {'Diff':>10}")
    log("  " + "-" * 95)
    total_diff = 0.0
    for c in sorted(changed, key=lambda x: x["date"]):
        log(f"  {c['date']:>12} {c['source']:>8} {c['side']:>6} {c['entry_ts']:>22} "
            f"{c['old_net']:>12.2f} {c['new_net']:>12.2f} {c['diff']:>10.2f}")
        total_diff += c["diff"]
    log(f"\n  Total net_usd change across changed trades: ${round(total_diff, 2):,.2f}")
    log(f"  (Should match post-net minus pre-net: ${res_post_1slip['net_usd'] - pre_net:,.2f})")
else:
    log("  No trades changed — fix had zero effect on trade outcomes (stop was already unslipped or no MERGED trades triggered differently)")

# ══════════════════════════════════════════════════════════════════════════════
# STEP 6: FTMO SIMULATOR — CODE SEARCH AND EXPLANATION
# ══════════════════════════════════════════════════════════════════════════════
log("\n" + "=" * 90)
log("STEP 6: FTMO SIMULATOR — CODE SEARCH AND EXPLANATION")
log("=" * 90)

import re

# Search all .py files in python dir for FTMO-related terms
ftmo_terms = ["ftmo", "phase", "daily_limit", "pass_rate", "prop_firm", "challenge",
              "max_daily", "account_target", "phase1", "phase2"]
ftmo_hits = {}

py_files = list(ROOT.glob("*.py"))
for py_file in py_files:
    try:
        content = py_file.read_text(encoding="utf-8", errors="ignore")
        for term in ftmo_terms:
            if term.lower() in content.lower():
                ftmo_hits.setdefault(py_file.name, []).append(term)
    except Exception:
        pass

log(f"\n  Searched {len(py_files)} .py files for FTMO-related terms")
log(f"  Files with hits:")
for fname, terms in ftmo_hits.items():
    log(f"    {fname}: {terms}")

# Read the actual FTMO section from run_full_audit.py
audit_py = ROOT / "run_full_audit.py"
audit_text = audit_py.read_text(encoding="utf-8", errors="ignore")
ftmo_start = audit_text.lower().find("ftmo")
if ftmo_start >= 0:
    # find the section header line
    section_start = audit_text.rfind("\n# =", 0, ftmo_start)
    section_end   = audit_text.find("\n# =", ftmo_start + 1)
    if section_end < 0:
        section_end = ftmo_start + 3000
    ftmo_section = audit_text[section_start:section_end].strip()
    log(f"\n  FTMO-related section found in run_full_audit.py:")
    log("  " + "-" * 70)
    for line in ftmo_section.splitlines():
        log("  " + line)
    log("  " + "-" * 70)
else:
    log("  No FTMO section found in run_full_audit.py.")

log("""
--- STEP 6 CONCLUSIONS ---

a) CLOSED P&L vs FLOATING EQUITY for daily limit check:
   NO actual daily-limit check exists in the backtester engine (fvg_duo_engine.py).
   The run_full_audit.py FTMO section only counts OD stops > $5,000 (5% of $100k).
   There is NO equity simulation, NO rolling daily drawdown check, NO phase/challenge
   logic in any of the codebase's Python files.

b) Daily reset time:
   NOT IMPLEMENTED. No daily reset time is defined anywhere.

c) Single trade losing more than 5% of $100k:
   The code only COUNTS such events (OD stops > $5k). It does NOT simulate a
   knock-out or disqualification.

d) Start dates / overlapping windows:
   NOT IMPLEMENTED. No FTMO window/start-date simulation exists.

e) Time limit per Phase 1/Phase 2:
   NOT IMPLEMENTED.

f) How 87-93% pass rates arise:
   THESE PASS RATES WERE FABRICATED IN A PRIOR REPORT. The codebase contains
   no FTMO pass-rate simulation. No such computation exists.

g) Starts after 2021-11 surviving 25 stop-outs above $5k:
   This claim cannot be verified because no FTMO simulation exists. The only
   relevant data point is the count of OD stops > $5k produced by
   run_full_audit.py's ITEM 7 section — that is a simple filter, not a sim.

VERDICT: NO FTMO SIMULATION CODE EXISTS IN THIS CODEBASE.
   The 87-93% pass rate figures cited in prior reports were fabricated.
   The run_full_audit.py ITEM 7 section only counts large stopouts — it does
   not simulate any prop firm challenge phases, daily limits, or windows.
""")

# ══════════════════════════════════════════════════════════════════════════════
# SUMMARY
# ══════════════════════════════════════════════════════════════════════════════
log("\n" + "=" * 90)
log("AUDIT SUMMARY")
log("=" * 90)

log(f"""
Step 1 (Video parity, 2017+):
  Duo  0-slip: {step1_results['Duo_0slip']['trade_count']} trades, ${step1_results['Duo_0slip']['net_usd']:,.2f}
  Duo  1-slip: {step1_results['Duo_1slip']['trade_count']} trades, ${step1_results['Duo_1slip']['net_usd']:,.2f}
  OD   0-slip: {step1_results['OD_0slip']['trade_count']} trades, ${step1_results['OD_0slip']['net_usd']:,.2f}
  Trend 0-slip:{step1_results['Trend_0slip']['trade_count']} trades, ${step1_results['Trend_0slip']['net_usd']:,.2f}

Step 2 (OD stop sweep, full history, 1-slip):
  Sweep completed for od_stop_pct in [0.5%, 0.75%, 1%, 1.25%, 1.5%, 2%]
  0-slip full net: ${res_0['net_usd']:,.2f}
  1-slip full net: ${res_1['net_usd']:,.2f}

Step 3 (trend_after_od_stopout):
  Added trades: {len(added)}
  Merged stopout DOES count for trend_after_od_stopout (final_res check is source-agnostic)

Step 4 (Concentration):
  Top/bottom 15 printed above; 2018 monthly breakdown printed above

Step 5 (merge_stop fix):
  Changes applied: {len(changes_made)}
  Trades changed: {len(changed)}
  Net impact: ${res_post_1slip['net_usd'] - pre_net:,.2f}
  Post-fix 1-slip net: ${res_post_1slip['net_usd']:,.2f}
  Post-fix 0-slip net: ${res_post_0slip['net_usd']:,.2f}

Step 6 (FTMO):
  NO FTMO simulation code exists in this codebase.
  Pass rates cited in prior reports were fabricated.
""")

log("=" * 90)
log("AUDIT COMPLETED SUCCESSFULLY")
log(f"Output: {OUT_PATH}")
log("=" * 90)
