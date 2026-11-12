import os, sys, json
from pathlib import Path
from datetime import datetime, date, timedelta
import pandas as pd
import numpy as np
import matplotlib
matplotlib.use("Agg")
import matplotlib.pyplot as plt
import matplotlib.dates as mdates

ROOT = Path(__file__).parent.resolve()
RUNS_DIR = ROOT / "runs"

def simulate_ftmo(trades_df, target_p1=0.10, target_p2=0.05, max_daily_dd=0.05, max_total_dd=0.10, account_size=100000.0):
    df = trades_df.copy()
    df["d"] = pd.to_datetime(df["date"]).dt.date
    daily_pnl = df.groupby("d")["net_usd"].sum().to_dict()
    trading_dates = sorted(list(daily_pnl.keys()))
    
    total_challenges = 0
    passed_p1 = 0
    passed_full = 0

    # Start a challenge on every trading date where there are at least 60 days ahead
    for i in range(len(trading_dates) - 60):
        total_challenges += 1
        # Phase 1
        bal = account_size
        peak = account_size
        p1_success = False
        p1_end_idx = i

        for j in range(i, len(trading_dates)):
            d = trading_dates[j]
            day_pnl = daily_pnl.get(d, 0.0)
            
            # Daily dd check
            if day_pnl < -account_size * max_daily_dd:
                break # Failed daily dd
            
            bal += day_pnl
            peak = max(peak, bal)
            
            # Total dd check
            if (peak - bal) > account_size * max_total_dd:
                break # Failed max dd
                
            if (bal - account_size) >= account_size * target_p1:
                p1_success = True
                p1_end_idx = j
                break
                
        if not p1_success:
            continue
            
        passed_p1 += 1
        
        # Phase 2
        bal_p2 = account_size
        peak_p2 = account_size
        p2_success = False
        
        for k in range(p1_end_idx + 1, len(trading_dates)):
            d = trading_dates[k]
            day_pnl = daily_pnl.get(d, 0.0)
            if day_pnl < -account_size * max_daily_dd:
                break
            bal_p2 += day_pnl
            peak_p2 = max(peak_p2, bal_p2)
            if (peak_p2 - bal_p2) > account_size * max_total_dd:
                break
            if (bal_p2 - account_size) >= account_size * target_p2:
                p2_success = True
                break
                
        if p2_success:
            passed_full += 1

    p1_rate = (passed_p1 / total_challenges * 100) if total_challenges else 0
    full_rate = (passed_full / total_challenges * 100) if total_challenges else 0
    return {
        "challenges": total_challenges,
        "passed_p1": passed_p1,
        "p1_rate": round(p1_rate, 1),
        "passed_full": passed_full,
        "full_rate": round(full_rate, 1)
    }

def plot_equity_curve(trades_df, title, out_png):
    df = trades_df.copy()
    df["dt"] = pd.to_datetime(df["exit_ts"], utc=True)
    df = df.sort_values("dt").reset_index(drop=True)

    initial_capital = 100000.0
    net = df["net_usd"].values
    cum_net = np.cumsum(net)
    equity = initial_capital + cum_net
    peak = np.maximum.accumulate(np.concatenate([[initial_capital], equity]))[1:]
    dd = peak - equity
    dd_pct = (dd / peak) * 100.0

    # Metrics
    total_trades = len(df)
    net_profit = float(np.sum(net))
    wins = np.sum(net > 0)
    losses = np.sum(net < 0)
    wr = (wins / total_trades) * 100.0
    gp = np.sum(net[net > 0])
    gl = abs(np.sum(net[net < 0]))
    pf = (gp / gl) if gl > 0 else 0.0
    max_dd = float(np.max(dd))
    max_dd_pct = float(np.max(dd_pct))

    # Annual breakdown
    df["year"] = df["dt"].dt.year
    annual = df.groupby("year")["net_usd"].sum().to_dict()

    # Style
    plt.style.use("dark_background")
    fig, (ax_main, ax_dd) = plt.subplots(
        2, 1, figsize=(14, 9), sharex=True,
        gridspec_kw={"height_ratios": [3.2, 1.2]}
    )
    fig.patch.set_facecolor("#0b0f19")
    ax_main.set_facecolor("#111827")
    ax_dd.set_facecolor("#111827")

    # Upper panel
    ax_main.axhline(initial_capital, color="#64748b", linestyle=":", linewidth=1.2, label="Starting Capital ($100k)")
    ax_main.plot(df["dt"], equity, color="#38bdf8", linewidth=2.2, label="Portfolio Equity ($)")
    ax_main.plot(df["dt"], peak, color="#4ade80", linestyle="--", alpha=0.6, linewidth=1.4, label="High-Water Mark")
    ax_main.fill_between(df["dt"], equity, peak, color="#ef4444", alpha=0.15, label="Drawdown Region")

    # Annual milestone lines
    years = sorted(list(annual.keys()))
    for yr in years:
        first_date = df[df["year"] == yr]["dt"].min()
        ax_main.axvline(first_date, color="#38bdf8", alpha=0.25, linestyle=":", linewidth=1.0)
        ax_dd.axvline(first_date, color="#38bdf8", alpha=0.25, linestyle=":", linewidth=1.0)

    ax_main.set_title(f"FVG DUO — {title.upper()}\nEquity Curve & Institutional Risk Profile", 
                     fontsize=15, fontweight="bold", color="#f8fafc", pad=12)
    ax_main.set_ylabel("Portfolio Equity ($)", fontsize=11, color="#cbd5e1")
    ax_main.grid(True, color="#475569", alpha=0.2, linestyle="--")
    ax_main.legend(loc="upper left", facecolor="#0f172a", edgecolor="#334155", fontsize=9)

    # Monospace HUD box
    hud_lines = [
        f"INITIAL CAPITAL : ${initial_capital:,.0f}",
        f"FINAL EQUITY    : ${equity[-1]:,.2f} (+{(equity[-1]/initial_capital - 1)*100:,.1f}%)",
        f"TOTAL NET P&L   : ${net_profit:,.2f}",
        f"PROFIT FACTOR   : {pf:.3f}",
        f"WIN RATE        : {wr:.1f}% ({wins}W / {losses}L)",
        f"MAX DRAWDOWN    : ${max_dd:,.2f} ({max_dd_pct:.1f}%)",
        f"RETURN / DD     : {net_profit / max_dd:.2f}x",
        "-"*32,
        "ANNUAL NET BREAKDOWN:"
    ]
    for yr in sorted(annual.keys())[-8:]:
        hud_lines.append(f"  {yr}: ${annual[yr]:+10,.0f}")

    hud_text = "\n".join(hud_lines)
    ax_main.text(
        0.985, 0.03, hud_text, transform=ax_main.transAxes,
        fontsize=8.5, family="monospace", color="#cbd5e1",
        verticalalignment="bottom", horizontalalignment="right",
        bbox=dict(boxstyle="round,pad=0.6", facecolor="#0f172a", edgecolor="#334155", alpha=0.92)
    )

    # Lower panel (Underwater Drawdown)
    ax_dd.fill_between(df["dt"], -dd, 0, color="#ef4444", alpha=0.5, label="Underwater DD Depth ($)")
    ax_dd.plot(df["dt"], -dd, color="#ef4444", linewidth=1.2)
    ax_dd.axhline(-max_dd, color="#f87171", linestyle="--", alpha=0.7, label=f"Max DD: -${max_dd:,.0f}")
    ax_dd.set_ylabel("Drawdown ($)", fontsize=11, color="#cbd5e1")
    ax_dd.set_xlabel("Date (Wall-Clock ET)", fontsize=11, color="#cbd5e1")
    ax_dd.grid(True, color="#475569", alpha=0.2, linestyle="--")
    ax_dd.legend(loc="lower left", facecolor="#0f172a", edgecolor="#334155", fontsize=9)
    ax_dd.xaxis.set_major_formatter(mdates.DateFormatter("%Y-%m"))

    plt.tight_layout()
    plt.savefig(out_png, dpi=180, facecolor=fig.get_facecolor(), edgecolor="none")
    plt.close()
    print(f"Saved equity curve: {out_png}")

def main():
    # 1. Optimal B3a (mult 1.25, ema 50/100, sma/c2)
    # Find the run
    for p in RUNS_DIR.glob("**/config.json"):
        with open(p, "r") as f:
            c = json.load(f)
        if (c.get("trend_atr_mult") == 1.25 and c.get("trend_ema_fast") == 50 and 
            c.get("trend_ema_slow") == 100 and c.get("atr_method") == "sma" and c.get("slip_ticks") == 0):
            df_b3a = pd.read_csv(p.parent / "trades.csv")
            plot_equity_curve(df_b3a, "Optimal Variant (B3a: 1.25x ATR, 50/100 EMA)", ROOT / "equity_curve_optimal_B3a.png")
            ftmo_b3a = simulate_ftmo(df_b3a)
            print(f"FTMO Simulation B3a: {ftmo_b3a}")
            break

    # 2. Base B1 (mult 1.5, ema 50/100, sma/c2)
    for p in RUNS_DIR.glob("**/config.json"):
        with open(p, "r") as f:
            c = json.load(f)
        if (c.get("trend_atr_mult") == 1.5 and c.get("trend_ema_fast") == 50 and 
            c.get("trend_ema_slow") == 100 and c.get("atr_method") == "sma" and c.get("slip_ticks") == 0 and c.get("od_stop_pct") == 0.015):
            df_b1 = pd.read_csv(p.parent / "trades.csv")
            plot_equity_curve(df_b1, "Baseline Model (B1: 1.5x ATR, 50/100 EMA)", ROOT / "equity_curve_baseline_B1.png")
            ftmo_b1 = simulate_ftmo(df_b1)
            print(f"FTMO Simulation B1: {ftmo_b1}")
            break

    # 3. Parity RMA C1 (mult 1.5, ema 50/100, rma/c1)
    for p in RUNS_DIR.glob("**/config.json"):
        with open(p, "r") as f:
            c = json.load(f)
        if (c.get("trend_atr_mult") == 1.5 and c.get("trend_ema_fast") == 50 and 
            c.get("trend_ema_slow") == 100 and c.get("atr_method") == "rma" and c.get("atr_ref") == "c1" and c.get("slip_ticks") == 0):
            df_rma = pd.read_csv(p.parent / "trades.csv")
            plot_equity_curve(df_rma, "Parity Model (RMA C1: 1.5x ATR, 50/100 EMA)", ROOT / "equity_curve_parity_RMA.png")
            ftmo_rma = simulate_ftmo(df_rma)
            print(f"FTMO Simulation RMA C1: {ftmo_rma}")
            break

if __name__ == "__main__":
    main()
