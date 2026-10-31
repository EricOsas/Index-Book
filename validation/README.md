# Validation and manual test handoff

Run the static source checks from the repository root:

```bash
python validation/static_validate.py
```

The validator checks C# lexical balance, duplicate method signatures, copied sample components, strategy contract markers, and the Sigma retry wiring in both NT8 and MQL5 source. It does **not** compile either platform or run a backtest. The old static results file is deliberately marked pending until this command is run against the updated commit.

## Required manual tests

### Sigma — NT8 and MT5

1. With `SlPct=0`, confirm the original no-synthetic-stop behavior remains unchanged and no retry is armed.
2. With `SlPct>0`, open the first attempt and verify its original stop is placed.
3. Close the first attempt at a losing broker protective stop. Confirm the retry arms once.
4. Confirm an opposite-side breakout does not consume the retry.
5. Confirm the next completed same-side M30 breakout enters with a stop distance equal to 50% of the first attempt's original stop distance.
6. Confirm target, session cutoff, reversal, manual close, partial close, recovery close, rejected order, and non-losing stop exit do not arm a retry.
7. Restart/reattach after the first stop exit but before the retry signal. Confirm the retry flag, direction, eligible timestamp, and original stop distance survive.
8. Confirm the retry can only be consumed once. Keep separate existing opposite-band flip behavior distinct from retry-count checks.

### Flash — MT5 cross-day recovery

1. Carry a managed position across the broker/server date boundary.
2. Restart or reattach the EA after the date boundary.
3. Confirm the saved M1-high peak, active trail flag, last processed M1 bar, and trail stop restore against the same broker position.
4. Confirm the trailing stop never widens and ordinary date reset does not clear the active position's trail state.

### Compile and results evidence

- Compile the exact current Sigma and Flash `.mq5` sources in MetaEditor. Save fresh compile logs and replace the `.ex5` files only from those builds.
- Compile the exact current Sigma and Flash NT8 `.cs` sources in NinjaTrader. The checked-in historical NT8 log reports 0 errors but 901 warnings. It is not a clean-build receipt for this updated commit.
- Rerun Strategy Analyzer with the intended instruments, data series, commissions, slippage, dates, and session settings. Export fresh trade CSVs and record the exact test configuration.
- The prior agent-reported NT8 aggregate metrics did not match the currently committed CSV exports. Treat prior summaries and the old CSVs as stale until reconciled from the new run.
- The previously mentioned MT5 HTML tester reports are not present in the repository. Commit fresh reports or trade-level exports after rerunning.

## Deployment

Deploy only while flat: flatten all owned and unrelated positions on the target instrument, cancel/settle all pending orders, detach old EA/strategy instances, then deploy the new build. There is no automatic migration of legacy OBR journal/lock keys into the Sigma or Flash namespaces.
