# FVG Duo execution and validation contract

Date: 2026-11-11. Source: `Spec v1.txt`, SHA-256 `76e4b9e6d4d9ecf8a61b449970ae3aca4d77a82c45a218e4ea0b80dbdc206595`.

## Latest authorized scope

The user requested unattended completion, reuse of local NT8 contract data, both specified Trend variants, selection emphasizing drawdown/profit/Sharpe/PF, Python-to-MQL5/C# parity, platform tester runs, equity curves, and historical FTMO 2-Step challenge simulations. This supersedes the original prohibition on ports and model selection and the requirement to stop for contract-separated input. No live account orders, login changes, purchased challenges, or claims of guaranteed success are authorized.

## Data boundary

- NQ: native Last 1-minute `.ncd`, all hours, unadjusted prices, one contract per source date. Reuse the corrected unsigned-time decoder in `Documents/Strategies/NQ Manipulation scalper/nq_validation/data.py`, not the obsolete bias-subtracting time decoder in `engine/data/ncd_decoder.py`.
- Existing canonical roll rule: roll eight days before the third Friday in each quarterly expiry month. No fallback contract and no hidden deduplication. Missing selected files are logged. Contract offsets are never applied.
- File stamps are audited empirically. DST ambiguities/nonexistent stamps are reported; no guesses, silent drop, or forward fill. Verified local NT8 records are NY bar-close labels; normalization subtracts one minute.
- Research comparison window: 2017-01-01 through the last verified available bar. Earlier available bars are used only for indicator warm-up. Report full-year and partial-year coverage separately. This date choice is a research decision, not the video's exact unknown window.
- USTEC: actual broker-exported M1 bid history, verified symbol point/tick value/volume constraints and clock. Do not relabel daily/coarse history as M1. Benchmark CFD and NQ separately; they are not two independent markets.

## Faithful oracle

Implement Spec sections 2–12 literally: wall-clock 5/10/15-minute aggregation, strict/non-strict true gaps, EMA first-close seeds, exact ATR conventions, 1000-bar warm-up, OD check priority, Trend c2 displacement, signal-independent arbitration, same-minute stop-first exits, MERGE/FLIP, time exit, adverse fill slippage, and costs. Tests T1–T12 precede strategy runs. NumPy/pandas arrays for bar calculations; loops only over dates/signals/trades, not iterrows or per-bar iloc.

Native execution and oracle fill semantics are distinct: closed-minute OHLC cannot establish tick order. Native stop/target behavior is measured, never misreported as exact adverse-first oracle parity. Core replay fixtures compare identical data and rules; tester executions compare actual requests/fills. Compiling is not runtime validation.

## Selection

Phase A convention selection remains count parity with the video, not profitability. Run all Phase B tests. Select between VIDEO (1.5 ATR, 50/100, 3R) and VARIANT (1.25 ATR, 40/50, 3R) with the same locked convention and baseline 1.5% OD stop. Report both years, annualized daily-PnL Sharpe (zero inactive sessions included), PF, profit, closed-trade and minute-equity drawdown. Prefer Pareto dominance; otherwise rank primarily lower minute-equity drawdown, then profit/drawdown, daily Sharpe and PF. State selection is retrospective development, not untouched validation.

## Risk and charts

Initial balance $10,000, no compounding. NQ oracle: one full NQ contract. MNQ display: identical NQ trade geometry valued at $2/point, one micro equivalent; disclose commission treatment and that this is scaling, not independent MNQ feed evidence. USTEC: fixed $100 initial planned stop risk (1% of initial balance), round lots down to broker volume step, reject under-minimum volume, retain original exposure across MERGE. MERGE can widen risk and is not sized again.

## FTMO historical scenario

Refresh official standard 2-Step objectives. Apply daily and total equity loss constraints including open PnL/costs; Prague midnight resets; phase profit targets on flat/closed balance; required distinct trading days; reset equity for phase 2, begin next tradable session. Start one challenge on every available trading date; report successes, breaches, and right-censored starts separately, plus pass proportion among all starts and among resolved starts. Include closed-minute and adverse OHLC excursion sensitivity, no synthetic probability or guarantee.

## Safety and release boundary

Ports default to no trading outside explicit enablement, unique instance/order ownership, protection, persistence, restart/reconnect reconciliation, no account-wide flatten/cancel, and no historical entry replay on live attach. NT8 `StartBehavior.ImmediatelySubmit` is mandatory, owned historical order references must transition correctly. Report native compile/core fixture/tester evidence independently. Playback, broker disconnect/reboot, Sim-forward and small-live acceptance remain release gates if they cannot actually be run.

## Persistent workflow documentation

After verification, document decoder time flags, raw-price stitching, clock normalization, audit caveats and reproducible commands in relevant installed trading skills. Do not claim an unverified path or parser is proved merely because another session referenced it.
