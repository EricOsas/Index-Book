import pickle
from datetime import datetime, date
from fvg_duo_engine import make_config, simulate_trade_1m, ET

with open("cache/prepared_data.pkl", "rb") as f:
    data = pickle.load(f)

d = date(2017, 3, 21)
day_bars = data["bars1m_by_day"][d]

cfg0 = make_config(atr_method="rma", atr_ref="c1", fvg_strict=True, slip_ticks=0)
cfg1 = make_config(atr_method="rma", atr_ref="c1", fvg_strict=True, slip_ticks=1)

# What was the trade?
# entry_ts=2017-03-21T09:30:00-04:00, merge_stop was 5354.75 or something?
# Let's inspect the bars around 12:52
for b in day_bars:
    if b[0] >= datetime(2017, 3, 21, 12, 50, tzinfo=ET) and b[0] <= datetime(2017, 3, 21, 12, 55, tzinfo=ET):
        print(b)
