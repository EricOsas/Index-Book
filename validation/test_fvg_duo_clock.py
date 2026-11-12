"""Regression fixtures for the New York DST calendar used by FVG Duo.

These tests validate the calendar boundaries and broker-offset arithmetic
mirrored by StrategyEngine.mqh. They do not replace MetaTrader compilation
or Strategy Tester verification.
"""
from datetime import datetime, timedelta, timezone
import unittest
from zoneinfo import ZoneInfo


def nth_sunday(year: int, month: int, n: int) -> datetime:
    first = datetime(year, month, 1, tzinfo=timezone.utc)
    first_sunday = first + timedelta(days=(6 - first.weekday()) % 7)
    return first_sunday + timedelta(days=7 * (n - 1))


def us_dst_bounds_utc(year: int):
    # 02:00 EST == 07:00 UTC; 02:00 EDT == 06:00 UTC.
    start = nth_sunday(year, 3, 2).replace(hour=7)
    end = nth_sunday(year, 11, 1).replace(hour=6)
    return start, end


def is_new_york_dst(utc: datetime) -> bool:
    start, end = us_dst_bounds_utc(utc.year)
    return start <= utc < end


def broker_stamp_to_new_york(utc: datetime, broker_offset_hours: int):
    server_stamp = utc + timedelta(hours=broker_offset_hours)
    ny_offset = -4 if is_new_york_dst(utc) else -5
    return server_stamp - timedelta(hours=broker_offset_hours - ny_offset)


class NewYorkClockTests(unittest.TestCase):
    def test_transition_boundaries(self):
        cases = [
            (2024, datetime(2024, 3, 10, 6, 59, tzinfo=timezone.utc), False),
            (2024, datetime(2024, 3, 10, 7, 0, tzinfo=timezone.utc), True),
            (2024, datetime(2024, 11, 3, 5, 59, tzinfo=timezone.utc), True),
            (2024, datetime(2024, 11, 3, 6, 0, tzinfo=timezone.utc), False),
            (2025, datetime(2025, 3, 9, 7, 0, tzinfo=timezone.utc), True),
            (2025, datetime(2025, 11, 2, 6, 0, tzinfo=timezone.utc), False),
            (2026, datetime(2026, 3, 8, 7, 0, tzinfo=timezone.utc), True),
            (2026, datetime(2026, 11, 1, 6, 0, tzinfo=timezone.utc), False),
        ]
        for year, stamp, expected in cases:
            with self.subTest(year=year, stamp=stamp):
                self.assertEqual(is_new_york_dst(stamp), expected)

    def test_broker_offsets_convert_to_same_new_york_instant(self):
        samples = [
            datetime(2024, 1, 15, 14, 30, tzinfo=timezone.utc),
            datetime(2024, 7, 15, 13, 30, tzinfo=timezone.utc),
            datetime(2024, 3, 10, 7, 0, tzinfo=timezone.utc),
            datetime(2024, 11, 3, 6, 0, tzinfo=timezone.utc),
        ]
        ny = ZoneInfo("America/New_York")
        for utc in samples:
            expected = utc.astimezone(ny).replace(tzinfo=None)
            for broker_offset in (2, 3, 0, -5):
                with self.subTest(utc=utc, broker_offset=broker_offset):
                    actual = broker_stamp_to_new_york(utc, broker_offset)
                    self.assertEqual(actual, expected)


if __name__ == "__main__":
    unittest.main(verbosity=2)
