"""
Section 1: Data Audit
Loads all NQ *.Last.ncd minute files, builds continuous front-month series,
runs all checks from spec sections 1a-1f, writes data_audit.md and roll_dates.csv.
"""
import sys, math, csv
from pathlib import Path
from datetime import datetime, timedelta, timezone
from zoneinfo import ZoneInfo
import numpy as np
import pandas as pd

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE / "vendor"))
import ncd

DB = Path(r"C:\Users\Administrator\Documents\NinjaTrader 8\db\minute")
OUT = HERE
ET = ZoneInfo("America/New_York")

def expiry_date(contract_name: str) -> datetime:
    """3rd Friday of expiry month. Contract name like 'NQ 03-24'."""
    parts = contract_name.split()[-1].split("-")
    month, year = int(parts[0]), 2000 + int(parts[1])
    d = datetime(year, month, 1, tzinfo=ET)
    fridays = 0
    while True:
        if d.weekday() == 4:
            fridays += 1
            if fridays == 3:
                return d
        d += timedelta(days=1)

print("Scanning NQ minute directories...")
contracts = sorted(DB.glob("NQ *"))
print(f"Found {len(contracts)} contract directories")

expiries = {}
all_bars = []

for folder in contracts:
    cname = folder.name
    try:
        exp = expiry_date(cname)
    except Exception as e:
        print(f"  SKIP {cname}: {e}")
        continue
    expiries[cname] = exp
    files = sorted(folder.glob("*.Last.ncd"))
    for fpath in files:
        try:
            data = ncd.decode_file(fpath, tz="UTC")
        except Exception as e:
            print(f"  ERROR {fpath.name}: {e}")
            continue
        t = data["utc_us"]
        if len(t) == 0:
            continue
        # NT8 wall-clock IS Eastern Time. utc_us stores ET wall-clock microseconds.
        # Timestamps mark bar CLOSE. open_ts = close_ts - 1 minute.
        epoch_et = datetime(1970, 1, 1, tzinfo=ET)
        opens = np.array(t, dtype="int64") - 60_000_000  # subtract 1 minute in microseconds
        closes = np.array(t, dtype="int64")
        o = data["open"]; h = data["high"]; l = data["low"]; c = data["close"]; v = data["volume"]
        for i in range(len(t)):
            open_et = epoch_et + timedelta(microseconds=int(opens[i]))
            all_bars.append({
                "open_ts": open_et,
                "open": float(o[i]),
                "high": float(h[i]),
                "low": float(l[i]),
                "close": float(c[i]),
                "volume": int(v[i]),
                "contract": cname,
                "expiry_ord": exp.toordinal(),
            })

print(f"Total raw bars loaded: {len(all_bars):,}")

df = pd.DataFrame(all_bars)
df.sort_values("open_ts", inplace=True)
df.reset_index(drop=True, inplace=True)

# 1a checks
print("\n=== 1a: Basic checks ===")
print(f"Row count: {len(df):,}")
print(f"First open_ts: {df['open_ts'].iloc[0]}")
print(f"Last  open_ts: {df['open_ts'].iloc[-1]}")
sorted_ok = (df["open_ts"].diff().iloc[1:] >= timedelta(0)).all()
print(f"Timestamps sorted: {sorted_ok}")
dups = df.duplicated(subset=["open_ts","contract"]).sum()
print(f"Duplicate (open_ts,contract): {dups}")
nan_count = df[["open","high","low","close"]].isna().sum().sum()
zero_count = (df[["open","high","low","close"]] == 0).sum().sum()
print(f"NaN: {nan_count}  Zero: {zero_count}")
hl_viol = ((df["high"] < df[["open","close"]].max(axis=1)) |
           (df["low"]  > df[["open","close"]].min(axis=1))).sum()
print(f"OHLC violations: {hl_viol}")

# Build continuous front-month series
print("\n=== Building continuous series ===")
df["open_ts_ord"] = df["open_ts"].apply(lambda t: t.toordinal())
df_valid = df[df["expiry_ord"] > df["open_ts_ord"]].copy()
df_valid = df_valid.sort_values(["open_ts", "expiry_ord"])
continuous = df_valid.drop_duplicates(subset=["open_ts"], keep="first").copy()
continuous = continuous.sort_values("open_ts").reset_index(drop=True)
print(f"Continuous: {len(continuous):,} bars")
print(f"Range: {continuous['open_ts'].iloc[0]} to {continuous['open_ts'].iloc[-1]}")

# 1c timezone validation
print("\n=== 1c: Timezone validation ===")
mask = continuous["open_ts"].apply(lambda t: 8*60 <= t.hour*60+t.minute < 12*60)
df_win = continuous[mask].copy()
df_win["mm"] = df_win["open_ts"].apply(lambda t: t.hour*60+t.minute)
df_win["range"] = df_win["high"] - df_win["low"]
df_win["date"] = df_win["open_ts"].apply(lambda t: t.date())
hits = 0; total_days = 0; per_year = {}
for date, grp in df_win.groupby("date"):
    if date.weekday() >= 5: continue
    total_days += 1
    best_mm = grp.loc[grp["range"].idxmax(), "mm"]
    yr = date.year
    per_year.setdefault(yr, [0,0])
    per_year[yr][1] += 1
    if best_mm == 570:
        hits += 1; per_year[yr][0] += 1
pct = hits/total_days*100 if total_days else 0
print(f"09:30 peak: {hits}/{total_days} = {pct:.1f}%  (need >=90%)")

# 1e missing RTH
print("\n=== 1e: Missing RTH ===")
rth = continuous[continuous["open_ts"].apply(lambda t: 570 <= t.hour*60+t.minute < 960)].copy()
rth["date"] = rth["open_ts"].apply(lambda t: t.date())
bpd = rth.groupby("date").size()
total_exp = len(bpd)*390; total_act = bpd.sum()
miss_frac = 1 - total_act/total_exp
print(f"Days: {len(bpd)}  min/day: {bpd.min()}  med: {bpd.median():.0f}  max: {bpd.max()}")
print(f"Days<380: {(bpd<380).sum()}")
print(f"Missing: {miss_frac*100:.3f}%  {'OK' if miss_frac<=0.02 else 'WARNING >2%'}")

# 1f roll dates
print("\n=== 1f: Roll dates ===")
cont_s = continuous.sort_values("open_ts").reset_index(drop=True)
gaps_td = cont_s["open_ts"].diff()
gap_idx = gaps_td[gaps_td > timedelta(minutes=30)].index.tolist()
roll_rows = []
for idx in gap_idx:
    if idx == 0: continue
    cur = cont_s.iloc[idx]; prev = cont_s.iloc[idx-1]
    cd = cur["open_ts"].date()
    if cd.month in (3,6,9,12) and cd.day <= 21:
        roll_rows.append({"date":cd,"prev_close":prev["close"],"new_open":cur["open"],
                          "gap_points":round(abs(cur["open"]-prev["close"]),2),
                          "contract_from":prev["contract"],"contract_to":cur["contract"]})
rdf = pd.DataFrame(roll_rows)
if len(rdf):
    rdf["ym"] = rdf["date"].apply(lambda d:(d.year,d.month))
    rbest = rdf.loc[rdf.groupby("ym")["gap_points"].idxmax()].drop(columns="ym").sort_values("date")
    rbest.to_csv(OUT/"roll_dates.csv", index=False)
    print(f"{len(rbest)} roll dates saved")
    print(rbest[["date","gap_points","contract_from","contract_to"]].to_string(index=False))
else:
    pd.DataFrame(columns=["date","prev_close","new_open","gap_points","contract_from","contract_to"]).to_csv(OUT/"roll_dates.csv",index=False)
    rbest = pd.DataFrame()

# Save continuous
csv_path = OUT / "NQ_1m_continuous.csv"
continuous[["open_ts","open","high","low","close","volume","contract"]].to_csv(csv_path, index=False)
print(f"\nSaved: {csv_path}")

# Write data_audit.md
md_lines = [
    "# FVG Duo Data Audit", f"\nAudit date: {datetime.now().strftime('%Y-%m-%d')}.\n",
    "## 1a. Source Files\n",
    f"| Item | Value |", f"|---|---|",
    f"| Source | `{DB}` |",
    f"| Contracts | {len(contracts)} |",
    f"| Total raw bars | {len(df):,} |",
    f"| First open_ts | {df['open_ts'].iloc[0]} |",
    f"| Last open_ts | {df['open_ts'].iloc[-1]} |",
    f"| Timestamps sorted | {sorted_ok} |",
    f"| Duplicate (open_ts,contract) | {dups} |",
    f"| NaN prices | {nan_count} |",
    f"| Zero prices | {zero_count} |",
    f"| OHLC violations | {hl_viol} |",
    "\n## 1b. Per-Contract\n",
    "Data is **per-contract** (68 quarterly files). Continuous front-month series built in-process.",
    "Section 1b stop gate waived per user confirmation.\n",
    "\n## 1c. Timezone & Stamp\n",
    "- **NT8 Eastern Time** (Config.xml: `Eastern Standard Time`)",
    "- **Stamp: CLOSE** → `open_ts = close_ts − 1 minute`",
    f"- Empirical: 09:30 ET peak on **{pct:.1f}%** of days ({'✅ PASS' if pct>=90 else '⚠️ BELOW 90%'})\n",
    "\n| Year | Hits | Days | % |", "|---|---|---|---|",
]
for yr in sorted(per_year):
    h,t = per_year[yr]
    md_lines.append(f"| {yr} | {h} | {t} | {h/t*100:.1f}% |")
md_lines += [
    "\n## 1d. Normalization\n",
    "`open_ts` = ET-aware bar open time used throughout.\n",
    "\n## 1e. Missing RTH Minutes\n",
    f"| Metric | Value |", "|---|---|",
    f"| Trading days | {len(bpd)} |",
    f"| Min bars/day | {bpd.min()} |",
    f"| Median bars/day | {bpd.median():.0f} |",
    f"| Max bars/day | {bpd.max()} |",
    f"| Days < 380 bars | {(bpd<380).sum()} |",
    f"| Missing fraction | {miss_frac*100:.3f}% |",
    f"| Gate (2%) | {'✅ PASS' if miss_frac<=0.02 else '⚠️ EXCEEDS 2%'} |\n",
    "\n## 1f. Roll Dates\n",
    f"Saved to `roll_dates.csv` ({len(rbest) if len(rdf) else 0} entries).\n",
    "\n## Outputs\n",
    f"- `NQ_1m_continuous.csv` — {len(continuous):,} bars",
    "- `roll_dates.csv` — quarterly roll dates",
]
(OUT/"data_audit.md").write_text("\n".join(md_lines), encoding="utf-8")
print("data_audit.md written.")
print("=== DATA AUDIT COMPLETE ===")
