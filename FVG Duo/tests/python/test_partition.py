import glob, os, re, sys
from datetime import datetime

base = r"C:\Users\Administrator\Documents\NinjaTrader 8\db\minute"
dirs = glob.glob(os.path.join(base, "NQ *"))

def contract_key(d):
    cname = os.path.basename(d)
    m = re.match(r"NQ (\d{2})-(\d{2})", cname)
    return (2000 + int(m.group(2)), int(m.group(1)), cname, d)

contracts = sorted([contract_key(d) for d in dirs])

ranges = []
for i, (y, m, cname, dpath) in enumerate(contracts):
    files = sorted(glob.glob(os.path.join(dpath, "*.Last.ncd")))
    f_first = os.path.basename(files[0]).split(".")[0]
    f_last = os.path.basename(files[-1]).split(".")[0]
    ranges.append({
        "cname": cname,
        "dpath": dpath,
        "raw_first": f_first,
        "raw_last": f_last,
        "files": {os.path.basename(f).split(".")[0]: f for f in files}
    })

# Compute assigned active date range for each contract
for i in range(len(ranges)):
    if i == 0:
        ranges[i]["active_start"] = ranges[i]["raw_first"]
    else:
        # starts the day after previous contract's raw_last
        prev_last = ranges[i-1]["raw_last"]
        # find the first file in ranges[i]["files"] that is > prev_last
        avail = sorted([d for d in ranges[i]["files"].keys() if d > prev_last])
        if not avail:
            print(f"ERROR: No files for {ranges[i]['cname']} after {prev_last}!")
        ranges[i]["active_start"] = avail[0]
    ranges[i]["active_end"] = ranges[i]["raw_last"]

print("Contract active ranges check:")
all_good = True
for i in range(len(ranges)):
    c = ranges[i]
    # Check if active_start <= active_end
    if c["active_start"] > c["active_end"]:
        print(f"ERROR: {c['cname']} start {c['active_start']} > end {c['active_end']}")
        all_good = False
    # Check gap with next
    if i < len(ranges) - 1:
        next_c = ranges[i+1]
        # check if next_c has files right after c['active_end']
        avail_next = sorted([d for d in next_c["files"].keys() if d > c["active_end"]])
        if not avail_next:
            print(f"ERROR: No files in {next_c['cname']} after {c['active_end']}")
            all_good = False

print(f"All good: {all_good}")
print(f"First contract: {ranges[0]['cname']} {ranges[0]['active_start']}..{ranges[0]['active_end']}")
print(f"Last contract: {ranges[-1]['cname']} {ranges[-1]['active_start']}..{ranges[-1]['active_end']}")
