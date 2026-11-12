"""Regression checks for the real NT8 data boundary; no random data."""
import importlib.util
from pathlib import Path
import sys
import unittest
import pandas as pd

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT))


class DataTests(unittest.TestCase):
    def test_unsigned_native_time_delta(self):
        import fvg_data as data
        frame = data.read_native(Path(r'C:\Users\Administrator\Documents\NinjaTrader 8\db\minute\NQ 06-24\20240615.Last.ncd'))
        self.assertEqual(frame.timestamp.astype(str).tolist(), ['2024-06-15 08:46:00', '2024-06-15 17:58:00'])
        self.assertEqual(frame.close.tolist(), [19696.25, 19696.25])

    def test_timezone_winter_summer_and_dst_errors(self):
        import fvg_data as data
        stamps = pd.Series(pd.to_datetime(['2024-01-08 14:30', '2024-07-08 13:30']))
        converted, invalid = data.convert_stamps(stamps, 'UTC')
        self.assertEqual(converted.dt.strftime('%H:%M').tolist(), ['09:30', '09:30'])
        self.assertEqual(len(invalid), 0)
        stamps = pd.Series(pd.to_datetime(['2024-03-10 02:30', '2024-11-03 01:30', '2024-11-04 09:30']))
        converted, invalid = data.convert_stamps(stamps, 'America/New_York')
        self.assertEqual(len(invalid), 2)
        self.assertEqual(converted.isna().sum(), 2)

    def test_roll_contract_keeps_raw_prices(self):
        import fvg_data as data
        self.assertEqual(data.selected_contract(pd.Timestamp('2024-03-06').date()), 'NQ 03-24')
        self.assertEqual(data.selected_contract(pd.Timestamp('2024-03-07').date()), 'NQ 06-24')


if __name__ == '__main__':
    unittest.main(verbosity=2)
