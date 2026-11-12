import glob, os, re, sys
from datetime import datetime
sys.path.append(r"C:\Users\Administrator\Documents\NinjaTrader 8\bin\Custom\Strategies\3. Index Book\9. FVG_Duo_NT\python")
from vendor import ncd

base = r"C:\Users\Administrator\Documents\NinjaTrader 8\db\minute"
dirs = glob.glob(os.path.join(base, "NQ *"))

def contract_key(d):
    cname = os.path.basename(d)
    m = re.match(r"NQ (\d{2})-(\d{2})", cname)
    return (2000 + int(m.group(2)), int(m.group(1)), cname, d)

contracts = sorted([contract_key(d) for d in dirs])

for y, m, cname, dpath in contracts[:5]:
    files = sorted(glob.glob(os.path.join(dpath, "*.Last.ncd")))
    f_last = files[-1]
    data = ncd.decode_file(f_last, tz="UTC")
    t0 = datetime.fromtimestamp(data["utc_us"][0]/1e6)
    t1 = datetime.fromtimestamp(data["utc_us"][-1]/1e6)
    print(f"{cname} last file {os.path.basename(f_last)}: {t0} to {t1}, {len(data['utc_us'])} bars")

for y, m, cname, dpath in contracts[30:35]:
    files = sorted(glob.glob(os.path.join(dpath, "*.Last.ncd")))
    f_last = files[-1]
    data = ncd.decode_file(f_last, tz="UTC")
    t0 = datetime.fromtimestamp(data["utc_us"][0]/1e6)
    t1 = datetime.fromtimestamp(data["utc_us"][-1]/1e6)
    print(f"{cname} last file {os.path.basename(f_last)}: {t0} to {t1}, {len(data['utc_us'])} bars")
