"""Regression checks for FVG Duo ATR and post-OD-stopout state.

These tests validate the algorithm and source guards. They do not compile MQL5
or NinjaScript and do not establish platform execution parity.
"""
from pathlib import Path
import importlib.util

import numpy as np

ROOT = Path(__file__).resolve().parents[1]
ORACLE_PATH = ROOT / "FVG Duo" / "tests" / "python" / "fvg_duo_engine.py"
MT5_PATH = ROOT / "9. FVG_Duo" / "StrategyEngine.mqh"
RUNTIME_PATH = ROOT / "9. FVG_Duo" / "ExecutionRuntime.mqh"
NT8_PATH = ROOT / "9. FVG_Duo_NT" / "FVG_Duo_NT8.cs"

_spec = importlib.util.spec_from_file_location("fvg_duo_oracle", ORACLE_PATH)
_oracle = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(_oracle)


def _tr_series_order(high, low, close):
    """Return TR values in oldest-first oracle order from series-order inputs."""
    high, low, close = high[::-1], low[::-1], close[::-1]
    tr = np.empty(len(high))
    tr[0] = high[0] - low[0]
    for i in range(1, len(high)):
        tr[i] = max(high[i] - low[i], abs(high[i] - close[i - 1]),
                    abs(low[i] - close[i - 1]))
    return tr


def _atr_at_series_shift(high, low, close, period, method, ref_shift):
    """Direct Python port of ComputeATRAt; input arrays are newest-first."""
    tr = _tr_series_order(high, low, close)
    target = len(tr) - 1 - ref_shift
    if target + 1 < period:
        return 0.0
    if method == "sma":
        return float(np.mean(tr[target - period + 1:target + 1]))
    seed = float(np.mean(tr[:period]))
    atr = seed
    for i in range(period, target + 1):
        atr = (atr * (period - 1) + tr[i]) / period
    return atr


def test_compute_at_matches_oracle_at_c1_and_c2():
    rng = np.random.default_rng(20261112)
    close = 18000 + np.cumsum(rng.normal(0, 18, 1500))
    high = close + rng.uniform(1, 14, len(close))
    low = close - rng.uniform(1, 14, len(close))
    period = 14

    for method in ("rma", "sma"):
        oracle_atr = _oracle.compute_atr(
            _oracle.compute_tr(high, low, close), period, method
        )
        for k in range(1000, len(close)):
            # At a c3 decision: c1=k-2, c2=k-1. In series order,
            # current forming bar is shift 0, c3=1, c2=2, c1=3.
            c1 = _atr_at_series_shift(high, low, close, period, method, 3)
            c2 = _atr_at_series_shift(high, low, close, period, method, 2)
            assert np.isclose(c1, oracle_atr[k - 2], rtol=0, atol=1e-10)
            assert np.isclose(c2, oracle_atr[k - 1], rtol=0, atol=1e-10)
            break


def test_mt5_gate_stopout_and_merged_source_guards():
    mt5 = MT5_PATH.read_text(encoding="utf-8")
    runtime = RUNTIME_PATH.read_text(encoding="utf-8")
    assert "bool canTradeTrend = false;" in mt5
    assert "void OnTradeTransaction(" in mt5
    assert "g_odStopoutBarClose = dealTime / 60 * 60 + 60;" in mt5
    assert 'prevSource == "MERGED"' in runtime
    assert "int WARMUP_BARS = 1000;" in mt5


def test_nt8_preset_atr_and_stopout_guards():
    nt8 = NT8_PATH.read_text(encoding="utf-8")
    assert "private bool _actUseRma;" in nt8
    assert "private int _actAtrAgo;" in nt8
    assert "double atr = Atr15(_actAtrAgo);" in nt8
    assert "bool canTradeTrend = false;" in nt8
    assert '(_activeSource == "OD" || _activeSource == "MERGED")' in nt8
