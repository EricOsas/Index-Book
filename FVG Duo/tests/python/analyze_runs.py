import os, sys, json, glob
from pathlib import Path
from datetime import datetime, date, timedelta
import pandas as pd
import numpy as np

ROOT = Path(__file__).parent.resolve()
RUNS_DIR = ROOT / "runs"

def compute_metrics_for_trades(df_trades, start_date=None, end_date=None):
    if df_trades.empty:
        return {"trades": 0, "net": 0, "pf": 0, "wr": 0, "max_dd": 0}
    
    df = df_trades.copy()
    if "date" in df.columns:
        df["d"] = pd.to_datetime(df["date"]).dt.date
    else:
        df["d"] = pd.to_datetime(df["entry_ts"]).dt.date

    if start_date:
        df = df[df["d"] >= start_date]
    if end_date:
        df = df[df["d"] <= end_date]

    if df.empty:
        return {"trades": 0, "net": 0, "pf": 0, "wr": 0, "max_dd": 0}

    net = df["net_usd"].values
    wins = np.sum(net > 0)
    gp = np.sum(net[net > 0])
    gl = abs(np.sum(net[net < 0]))
    pf = (gp / gl) if gl > 0 else float("inf")
    wr = (wins / len(net)) * 100

    cum = np.cumsum(net)
    peak = np.maximum.accumulate(np.concatenate([[0.0], cum]))
    dd = peak[:-1] - cum
    max_dd = float(np.max(dd)) if len(dd) else 0.0

    return {
        "trades": len(df),
        "net": float(np.sum(net)),
        "pf": float(pf),
        "wr": float(wr),
        "max_dd": float(max_dd),
        "return_dd_ratio": float(np.sum(net) / max_dd) if max_dd > 0 else 0.0
    }

def main():
    # Scan all run directories (including nested oracle runs)
    run_dirs = []
    for p in RUNS_DIR.rglob("config.json"):
        run_dirs.append(p.parent)

    print(f"Found {len(run_dirs)} runs to analyze.")

    records = []
    for rdir in run_dirs:
        cfg_file = rdir / "config.json"
        trades_file = rdir / "trades.csv"
        if not cfg_file.exists() or not trades_file.exists():
            continue
        try:
            with open(cfg_file, "r") as f:
                cfg = json.load(f)
            df_trades = pd.read_csv(trades_file)
        except Exception as e:
            continue

        # Date range of trades
        dates = pd.to_datetime(df_trades["date"]).dt.date
        min_date, max_date = dates.min(), dates.max()

        # Windows to evaluate
        full_m = compute_metrics_for_trades(df_trades)
        
        # 10-year candidate windows:
        # Window 1: 2014-01-01 to 2023-12-31 (10 years)
        w_2014_2023 = compute_metrics_for_trades(df_trades, date(2014, 1, 1), date(2023, 12, 31))
        # Window 2: 2015-01-01 to 2024-12-31 (10 years)
        w_2015_2024 = compute_metrics_for_trades(df_trades, date(2015, 1, 1), date(2024, 12, 31))
        # Window 3: 2016-01-01 to 2025-12-31 (10 years)
        w_2016_2025 = compute_metrics_for_trades(df_trades, date(2016, 1, 1), date(2025, 12, 31))
        # Window 4: 2017-01-01 to 2026-10-30 (holding period benchmark in video: "start of 2017")
        w_2017_2026 = compute_metrics_for_trades(df_trades, date(2017, 1, 1), date(2026, 10, 30))
        # Window 5: Last 365 days of data
        cutoff_12m = max_date - timedelta(days=365)
        w_12m = compute_metrics_for_trades(df_trades, cutoff_12m, max_date)

        records.append({
            "run_dir": rdir.name,
            "path": str(rdir),
            "enable_od": cfg.get("enable_od"),
            "enable_trend": cfg.get("enable_trend"),
            "atr_method": cfg.get("atr_method"),
            "atr_ref": cfg.get("atr_ref"),
            "fvg_strict": cfg.get("fvg_strict"),
            "trend_atr_mult": cfg.get("trend_atr_mult"),
            "trend_ema_fast": cfg.get("trend_ema_fast"),
            "trend_ema_slow": cfg.get("trend_ema_slow"),
            "od_stop_pct": cfg.get("od_stop_pct"),
            "slip_ticks": cfg.get("slip_ticks"),
            "trend_after_od_stopout": cfg.get("trend_after_od_stopout"),
            "merge_anchor": cfg.get("merge_anchor"),
            # Full metrics
            "full_trades": full_m["trades"],
            "full_net": full_m["net"],
            "full_pf": full_m["pf"],
            "full_wr": full_m["wr"],
            "full_dd": full_m["max_dd"],
            "full_ret_dd": full_m["return_dd_ratio"],
            # 2015-2024 10y window
            "w1524_trades": w_2015_2024["trades"],
            "w1524_net": w_2015_2024["net"],
            "w1524_pf": w_2015_2024["pf"],
            "w1524_dd": w_2015_2024["max_dd"],
            # 2016-2025 10y window
            "w1625_trades": w_2016_2025["trades"],
            "w1625_net": w_2016_2025["net"],
            "w1625_pf": w_2016_2025["pf"],
            "w1625_dd": w_2016_2025["max_dd"],
            # 2017-2026 window
            "w1726_trades": w_2017_2026["trades"],
            "w1726_net": w_2017_2026["net"],
            "w1726_dd": w_2017_2026["max_dd"],
            # 12m
            "w12m_trades": w_12m["trades"],
            "w12m_net": w_12m["net"],
        })

    df_res = pd.DataFrame(records)
    df_res.to_csv(ROOT / "all_runs_analyzed.csv", index=False)
    print("Saved all_runs_analyzed.csv successfully.")

    # 1. Print Phase A Duo comparison (0 slip)
    print("\n" + "="*90)
    print("PHASE A DUO COMPARISON (0 slip, full period + 10-year windows)")
    print("="*90)
    duo_a = df_res[(df_res["enable_od"] == True) & (df_res["enable_trend"] == True) & 
                   (df_res["slip_ticks"] == 0) & (df_res["od_stop_pct"] == 0.015) & 
                   (df_res["trend_atr_mult"] == 1.5) & (df_res["trend_ema_fast"] == 50) & 
                   (df_res["trend_ema_slow"] == 100) & (df_res["trend_after_od_stopout"] == False)]
    cols = ["atr_method", "atr_ref", "fvg_strict", "full_trades", "full_net", "full_dd", "full_ret_dd", "w1524_trades", "w1524_net", "w1524_dd", "w1625_trades", "w1625_net", "w1625_dd"]
    print(duo_a[cols].sort_values("full_net", ascending=False).to_string(index=False))

    # 2. Print B1, B2, B3a, B3b comparison with Max DD
    print("\n" + "="*90)
    print("PHASE B MODEL VARIANT COMPARISON (including B3 with Max DD)")
    print("="*90)
    duo_b = df_res[(df_res["enable_od"] == True) & (df_res["enable_trend"] == True) & 
                   (df_res["atr_method"] == "sma") & (df_res["atr_ref"] == "c2") & 
                   (df_res["fvg_strict"] == True) & (df_res["od_stop_pct"] == 0.015) & 
                   (df_res["trend_after_od_stopout"] == False)]
    cols_b = ["slip_ticks", "trend_atr_mult", "trend_ema_fast", "trend_ema_slow", "full_trades", "full_net", "full_pf", "full_wr", "full_dd", "full_ret_dd"]
    print(duo_b[cols_b].sort_values("full_net", ascending=False).to_string(index=False))

    # 3. Print RMA c1 strict=True vs SMA c2 strict=True comparison across all variants
    print("\n" + "="*90)
    print("RMA / C1 / STRICT=TRUE vs SMA / C2 / STRICT=TRUE HEAD-TO-HEAD")
    print("="*90)
    h2h = df_res[((df_res["atr_method"] == "rma") & (df_res["atr_ref"] == "c1") & (df_res["fvg_strict"] == True)) |
                 ((df_res["atr_method"] == "sma") & (df_res["atr_ref"] == "c2") & (df_res["fvg_strict"] == True))]
    print(h2h[cols_b + ["atr_method", "atr_ref"]].to_string(index=False))

if __name__ == "__main__":
    main()
