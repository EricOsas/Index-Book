import sys, json
from pathlib import Path
from datetime import datetime, date, timedelta
import pandas as pd
import numpy as np

ROOT = Path(__file__).parent.resolve()
RUNS_DIR = ROOT / "runs"

def find_best_window(trades_file):
    df = pd.read_csv(trades_file)
    df["d"] = pd.to_datetime(df["date"]).dt.date
    dates_sorted = sorted(df["d"].unique())
    
    # Test end dates between 2024-06-01 and 2026-10-30 at 1-month intervals
    best_match = None
    min_dist = float("inf")
    results = []
    
    for end_d in dates_sorted:
        if end_d < date(2024, 1, 1):
            continue
        # 10 years window: [end_d - 10 years, end_d]
        start_10y = date(end_d.year - 10, end_d.month, min(end_d.day, 28))
        df_10y = df[(df["d"] >= start_10y) & (df["d"] <= end_d)]
        
        # 12m window
        start_12m = date(end_d.year - 1, end_d.month, min(end_d.day, 28))
        df_12m = df[(df["d"] >= start_12m) & (df["d"] <= end_d)]
        
        # Last 100 trades up to end_d
        df_up_to_end = df[df["d"] <= end_d]
        df_100 = df_up_to_end.iloc[-100:] if len(df_up_to_end) >= 100 else df_up_to_end

        if len(df_10y) == 0:
            continue
            
        cnt_10y = len(df_10y)
        net_10y = df_10y["net_usd"].sum()
        net_12m = df_12m["net_usd"].sum() if len(df_12m) else 0
        net_100 = df_100["net_usd"].sum() if len(df_100) else 0
        
        # Max DD in 10y
        cum = np.cumsum(df_10y["net_usd"].values)
        peak = np.maximum.accumulate(np.concatenate([[0.0], cum]))
        dd = peak[:-1] - cum
        max_dd_10y = float(np.max(dd)) if len(dd) else 0.0

        # Distance score vs video targets:
        # Video: cnt_10y=1726, net_10y=418000, net_12m=119000, net_100=81000, dd_10y=30000
        dist = (abs(cnt_10y - 1726) / 1726 + 
                abs(net_10y - 418000) / 418000 + 
                abs(net_12m - 119000) / 119000 +
                abs(net_100 - 81000) / 81000 +
                abs(max_dd_10y - 30000) / 30000)
                
        results.append({
            "end_date": str(end_d),
            "start_10y": str(start_10y),
            "cnt_10y": cnt_10y,
            "net_10y": round(net_10y, 0),
            "max_dd_10y": round(max_dd_10y, 0),
            "net_12m": round(net_12m, 0),
            "net_100": round(net_100, 0),
            "dist": round(dist, 4)
        })

    df_out = pd.DataFrame(results).sort_values("dist")
    return df_out

def main():
    # Check both B1 run (run_8c49706f) and RMA/c1 run (run_31c52fea / check all)
    # Find the trade files
    runs_to_check = {
        "SMA_c2_strict (B1 params)": RUNS_DIR / "run_8c49706f" / "trades.csv",
    }
    # Find RMA/c1 trade file
    for p in RUNS_DIR.glob("**/config.json"):
        with open(p, "r") as f:
            c = json.load(f)
        if c.get("atr_method") == "rma" and c.get("atr_ref") == "c1" and c.get("fvg_strict") == True and c.get("enable_od") == True and c.get("enable_trend") == True:
            runs_to_check["RMA_c1_strict (B1 params)"] = p.parent / "trades.csv"
            break

    for name, tfile in runs_to_check.items():
        if not tfile.exists():
            continue
        print("="*80)
        print(f"Top 5 Closest 10-Year Windows for {name}:")
        print("Video Targets: 10y Trades=1,726 | 10y Net=$418,000 | Max DD=$30,000 | 12m Net=$119,000 | Last 100=$81,000")
        print("="*80)
        res = find_best_window(tfile)
        print(res.head(8).to_string(index=False))
        print("\n")

if __name__ == "__main__":
    main()
