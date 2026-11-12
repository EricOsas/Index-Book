"""Regression checks for the MT5 M15 ATR/EMA bar alignment.

The numerical test ports ComputeATRAtShift's series-array traversal and compares
it with the Python oracle. The source guard catches accidental reintroduction
of a short rolling window or use of the forming M15 bar for the EMA filter.
"""
from pathlib import Path
import importlib.util

import numpy as np


ROOT = Path(__file__).resolve().parents[1]
ORACLE_PATH = ROOT / "FVG Duo" / "tests" / "python" / "fvg_duo_engine.py"
MT5_PATH = ROOT / "9. FVG_Duo" / "StrategyEngine.mqh"

_spec = importlib.util.spec_from_file_location("fvg_duo_oracle", ORACLE_PATH)
_oracle = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(_oracle)


def _series_at_shift(high, low, close, period, method, target_shift):
    # MQL rates[] has shift 0 newest, while the oracle arrays are oldest first.
    high, low, close = high[::-1], low[::-1], close[::-1]
    count = len(high)
    if count <= period or count - target_shift < period:
        return 0.0

    def true_range(shift):
        if shift == count - 1:
            return high[shift] - low[shift]
        return max(
            high[shift] - low[shift],
            abs(high[shift] - close[shift + 1]),
            abs(low[shift] - close[shift + 1]),
        )

    if method == "rma":
        seed = 0.0
        seen = 0
        atr = 0.0
        for shift in range(count - 1, target_shift - 1, -1):
            tr = true_range(shift)
            if seen < period:
                seed += tr
                seen += 1
                if seen == period:
                    atr = seed / period
            else:
                atr = (atr * (period - 1) + tr) / period
        return atr if seen == period else 0.0

    return sum(true_range(s) for s in range(target_shift, target_shift + period)) / period


def test_series_atr_matches_oracle_for_c1_and_c2():
    rng = np.random.default_rng(20261112)
    close = 18000 + np.cumsum(rng.normal(0, 18, 600))
    high = close + rng.uniform(1, 14, len(close))
    low = close - rng.uniform(1, 14, len(close))
    period = 14

    for method in ("rma", "sma"):
        oracle = _oracle.compute_atr(
            _oracle.compute_tr(high, low, close), period, method
        )
        # MT5 shifts: c1=3, c2=2. Include several older and newer target bars.
        for chronological_index in range(period - 1, len(close)):
            shift = len(close) - 1 - chronological_index
            actual = _series_at_shift(high, low, close, period, method, shift)
            assert np.isclose(actual, oracle[chronological_index], rtol=0, atol=1e-10)


def test_mt5_uses_full_history_and_completed_c3_ema():
    source = MT5_PATH.read_text(encoding="utf-8")
    assert "int count = Bars(_Symbol, PERIOD_M15);" in source
    assert "ComputeATRAtShift(r15, 14, actAtrMethod, copied, atrShift)" in source
    assert "int atrShift = (actAtrRef == ATR_REF_C1) ? 3 : 2;" in source
    assert "int emaCount = copied - 1;" in source
    assert "ComputeEMA(closes, actTrendEmaFast, emaCount)" in source
