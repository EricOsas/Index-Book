import pandas as pd

df = pd.read_csv('runs/oracle_RMA_B1/trades.csv')
for d in ['2024-11-07', '2026-08-04', '2026-06-29', '2026-10-16']:
    sub = df[df['date'] == d]
    print('='*60)
    print('Date:', d)
    for _, r in sub.iterrows():
        print(f"  id={r['trade_id']} | src={r['source']} | side={r['side']} | entry_ts={r['entry_ts']} | entry_p={r['entry_price']} | exit_ts={r['exit_ts']} | exit_p={r['exit_price']} | exit_reason={r['exit_reason']} | pts={r['points']} | net=${r['net_usd']:,.2f}")
