# Vault Break

## Restart and outage recovery

The live journal is isolated by broker server, account, account mode, symbol and magic, with an exclusive local instance lock. Committed state binds original risk and entry time to the immutable broker position identifier, stored as two exact 32-bit halves. Entry intent (nonce, bracket, time, costs and consumed attempt) is flushed before submission. A broker fill accepted before the final commit is adopted only when its comment, fill time and unchanged broker bracket verify that intent. Existing SL/TP are retained on normal recovery; they are not replaced by current defaults.

Close-recovery code is embedded in this folder's `ExecutionRuntime.mqh`; no parent-folder helper is required. It writes a separate bound-position close intent before any close request. Even an accepted transport result waits for the broker position and execution/order history to acknowledge the quantity. Timeout, connection failure and unknown acceptance cannot blindly resend; ambiguous closes display attention and block entries/trailing. Terminal partial fills permit only the actual remaining quantity; definite rejection retains protection and pauses retries for five seconds.

Legacy journals migrate only if the original entry-order stop independently verifies the saved original risk and entry time. Corrupt, missing or ambiguous metadata retains broker protection, blocks new entries and unverifiable management, preserves the evidence, and displays `Recovery needs attention`. A model's normal time exit and missing-protection safety still apply to explicitly owned exposure. No orders are sent while disconnected. Recovery never starts the terminal after a VPS reboot, and local locks do not coordinate two VPS machines. Native remove/re-add, reconnect and restart verification remains required; compile and extracted fixtures alone do not prove those runtime cases.

## Implemented specification — 2026-10-13

All rule times below are Central (CT), with DST applied automatically. The promoted model is the funded-first 60/40 candidate. Promotion follows the native MNQ audit; it does not establish MT5 CFD price/volume parity with NQ.

| Component | Exact rule |
|---|---|
| Source | Completed M1 bars; fixed civil M30 signal candles, independent of chart timeframe. |
| SOP | Open of the 00:00 CT minute, equivalent to 01:00 NY; not that minute's close. |
| Daily volatility | Daily boundary 16:00 CT / 17:00 NY. ATR(15) is a rolling simple mean of true ranges; average the last 15 completed ATR values. NUB = SOP + 0.30 × that average. |
| Warmup | For reconstructed daily history, exclude the first partial bucket and use its close as the seed. Then require 29 completed daily true-range observations to obtain 15 ATR(15) values. Roughly 45 calendar days is a loading guideline, not a guaranteed coverage test. |
| VWAP | Reset at 00:00 CT; cumulative completed-M1 HLC3 × volume / volume. NT8 uses exchange bar volume; MT5 prefers real volume and otherwise uses tick volume. |
| Signal | Completed M30 close strictly above both NUB and current completed-M1 VWAP. Confirming close must be at/after 10:00 CT and strictly before the day's cutoff. No crossing-from-below requirement. |
| Attempt 1 | First eligible confirmed M30 signal funds a real long at the next available quote. There is no synthetic first trade or waiting gate. |
| Attempts 2–3 | A later fresh qualifying M30 close may fund another long after the prior owned trade exits. At most three funded attempts/day; no simultaneous owned trade or replay of an old signal. Failed reserved live requests retain the existing conservative block. |
| Funded exits | Fixed 60-point stop / 40-point target; no ADX, BE or trailing. Time exit 14:30 CT normally, 11:55 on special days. MT5 submits its initial bracket relative to the request quote, preserving the existing broker execution/risk checks; slippage can change actual fill-to-stop distance. |
| Data rejection | Exact opening minute and complete required M1/M30 windows. Missing required source minutes block entries rather than fabricate candles. |
| Counting | Funded attempts include reserved submissions for restart safety; broker/Analyzer trade totals count actual fills. |

The special-day cutoff is a preserved model policy, not inferred from the chosen chart trading-hours template. The source retains its explicit 2020–2025 dates (including January 9, 2025). From 2026 it applies observed New Year, third-Monday January/February holidays, Good Friday, last Monday in May, observed June 19, July 3 and observed July 4, first Monday in September, fourth Thursday in November and the following day, December 24, observed December 25, and December 31.

NT8 uses canonical same-expiry NQ signals for NQ/MNQ/NNQ execution. Orders, tick rounding, quantity and costs belong to the attached order contract. MT5 uses the attached Nasdaq CFD symbol and its broker specifications. Cash/fixed sizing, costs, ownership and connection guards are execution controls; they do not change these signal rules.

### Upgrade and preserved evidence

The previous synthetic-first 70/40 production sources and binary are archived under `research/vault_break/archive/live_70_40_before_20261013` in `Conte_Research_Strategies`. Historical results below describe that older model unless explicitly dated otherwise.

The MT5 ownership ID remains 91008010. Journal revision 2 stores funded-first attempts. A valid same-day legacy journal migrates its count to the greater of actual distinct broker entry orders and `max(0, oldAttempts - 1)`; this removes the obsolete synthetic attempt while retaining reserved funded attempts. Genuine blocked status, original trade risk, entry time and costs are preserved. Obsolete virtual state is cleared. Migration does not modify an existing owned position's SL/TP or close foreign positions. Missing or incomplete journals retain the existing safety blocks; an unknown revision blocks new entries.

### Reference differences and supplied Analyzer audit

The author screenshot covers **2022-01-21–2026-09-24**: 1,151 trades, 779 wins, 371 losses, one flat, average trade $112.48. The supplied NT8 CSV reconciles to **682 funded NQ trades**, 452 wins, 228 losses, two flats, and $78,560 gross. One contract throughout; native commission and slippage are zero in this run.

Our stop is **70**, while the author's reported $1,500 loss per NQ contract implies **75**. Our synthetic-first gate excludes the first attempt from funded counts and affects later eligibility; whether the author has an equivalent first-attempt gate remains unconfirmed. Keep confirmed specification differences separate from assumptions about the author's implementation.

The first exported fill is March 8, 2022. January 21–March 7 contains 32 weekdays: even three funded entries every weekday could explain at most **96** of the **469** count difference through initial warmup. At least 330 synthetic attempts preceded the 330 days with funded trades; these are absent from the CSV, as are virtual-only days and rejected-session reasons. Neither warmup nor exact author parity is established by this export.

Your planned **2026-01-21–2026-09-24** comparison is separate. Filtering the existing, already-warm export to that interval yields **151 trades and $14,180 gross**. A new run starting January 21 must first build its own warmup and is not directly comparable to that filtered result.

### Completed warm-log audit - 2026-10-11

Latest NT8 log `C:/Users/Administrator/Documents/NinjaTrader 8/VaultBreak/logs/VB91008010_8a98ef1702fc.csv` loads October 31, 2021 through September 24, 2026. All 715 funded entry fills reconcile with 715 exit fills; no residual quantity remains. Filtering entries to January 21, 2022 through September 24, 2026 gives **705 trades and $76,775 gross**, versus the original CSV's 682 / $78,560. The warmed comparison has 23 more trades and $1,785 less gross; do not infer that more trades improve the model. The gap to the author's 1,151 is now **446**, so initial warmup does not explain the remaining difference. January 21-September 24, 2026 alone still gives **151 / $14,180 gross**. The log records MINUTE, ENTRY_REQUEST, ENTRY_FILL and EXIT_FILL only; it has no rejected-session or synthetic-lifecycle reason breakdown, so the remaining gap cannot yet be attributed from this log. Prices/quantities yield gross NQ PnL; costs are not deducted. No Vault strategy rule was changed in this audit. Full audit: `C:/Users/Administrator/Documents/Strategies/Conte_Research_Strategies/research/vault_break/VAULT_WARM_RUN_AUDIT.md`.

For a warm 2026 comparison, run from at least 2025-11-01 through 2026-09-24 with full ETH history, fixed one NQ contract, unchanged merge/roll settings, and detailed logging enabled; count only entries dated 2026-01-21 onward in the exported platform timezone. Verify daily-history readiness rather than assuming the loading date guarantees it. Keep native commission/slippage zero only for matching this gross baseline; cost-adjusted performance is a separate comparison. Decision logs are needed to distinguish ATR waits, incomplete sessions, synthetic waits and funded entries.

This folder owns one independent MT5 EA, its includes, preset, documentation and validation artifacts. VWAP Pullback is now a separate model in its own folder.

## Fixed model

- Central-time midnight open; noise level = that open + 30% of the mean of the last 15 completed daily ATR(15) values.
- A completed 30-minute candle must close strictly above both the noise level and the midnight-anchored session VWAP. Entry is attempted on the next available quote after that candle closes, during 10:00–14:30 Central.
- First qualifying attempt is simulated, with a 70-point stop and 40-point target. No broker order is sent. Only after its simulated exit can a later, fresh 30-minute signal fund an entry.
- Attempts 2–4 may be funded: at most three broker trades per day. Each uses a fixed 70-point stop and 40-point target. No trailing stop.
- The simulated trade is checked with completed M1 OHLC. When stop and target both lie within one bar, the stop wins. Incomplete simulated paths block the day.

Both models use a conservative 11:55 Central cutoff on specified special calendar days rather than 14:30. It is a research safety rule, not a verified exchange-hours calendar or a rule demonstrated by the author. Owned positions are closed at cutoff when the terminal has a tradable quote; a disconnected terminal cannot execute a scheduled market close.

VWAP resets at Central midnight and accumulates completed M1 typical price (high + low + close)/3 weighted by real volume where available, otherwise tick volume. USTEC tick volume is a broker activity proxy, not exchange traded volume. The broker clock is detected automatically using the ORB_Scalper server/GMT and weekly-opening approach; see Broker clock below. A clock awaiting history blocks new entries without removing the EA.

## Inputs

The strategy rules above are private constants, not research menus.

| Section / option | Default | Meaning |
|---|---:|---|
| Trading: Enable new trades | On | Allows new orders. Off still manages and protects owned open positions; it does not pause their exits. |
| Risk: Risk per trade, including costs | 100 | Maximum planned initial stop loss plus the configured commission and slippage allowance, in account currency. On a USD account this is $100. |
| Risk: Use fixed lots instead of cash risk | Off | Off uses the cash budget above. On uses the requested lots below and disables the cash budget; stop protection, broker volume limits and margin checks still apply. |
| Risk: Lots per trade | 1.0 | Used only when fixed sizing is on. Must match the broker's minimum, maximum and volume step; invalid sizes are rejected, never rounded up. |
| Costs: Fallback round-trip commission per lot | 7 | Used until a fully closed position on this account, symbol and EA magic supplies broker commission plus fees per lot. The latest complete position replaces the fallback, including a valid zero. Tester runs retain the configured simulation fallback. |
| Costs: Modeled slippage each way | 3 | Broker points on each side (`3 × _Point` in price). This is not 3 index price units or 3 exchange ticks. The cash equivalent of both sides is included in sizing and adjusted tester results. |
| Display: Show chart levels, trade markers and information | On | Draws bounded session levels, the midnight VWAP curve, owned trade markers and status. |
| Logging: Write a detailed trade and decision log | Off | When enabled, writes decisions and detailed tester trade exports to Common/Files/VaultBreak/logs. |

Cash sizing includes configured costs before rounding volume down. Trades below broker minimum volume or over the planned budget are rejected. Fixed sizing uses the requested lots instead; it does not promise a $100 loss limit. A gap or execution beyond the reserved allowance can still make realized loss exceed a cash budget. Fixed-size detailed exports use zero in the legacy cash-budget and cash-budget-normalized fields because no cash cap is active.

## Chart anchors and entry timing

Session Opening Price (SOP) and Noise Up Barrier (NUB) levels begin at Central midnight and end at that day's cutoff. Opening-range levels begin at 08:30; overnight levels begin at the configured overnight start. Each is a bounded trend segment with both rays disabled. Trade brackets begin at entry. The VWAP curve consists of completed M1 points from midnight to its latest completed point, including history reconstructed on attachment. There is no horizontal current-VWAP line. Changing the viewed chart timeframe leaves the M1-derived curve visible; the EA retains its objects during a timeframe reload and marks them visible on all chart periods. Calculation still uses completed M1 bars.

The current panel shows NY and CT on one row, without the bracketed broker clock. New York is one hour ahead of Central on trading days. Display clocks do not shift entry windows or anchors. Tooltips identify only this model and its level. On initialization, the EA removes its own objects using the former object prefix for this strategy ID, leaving other chart objects alone.

A candle timestamp identifies its open. A 20:00 M30 candle closes at 20:30 broker time. In the completed September 22 user test, our first simulated trade actually began at 18:30, resolved at target at 20:53, and enabled a funded entry at 21:00. The author first enters at 20:30 and next at 21:30. This is an earlier-qualification parity difference followed by intentional synthetic waiting, not an order delayed by one bar. See validation/AUTHOR_SEP22_SEQUENCE.md. Vault’s unrelated ADX-readiness dependency has been removed; it did not cause this example.

## Changes from the transcript

The transcript's $1,500 stop and $800 target for one NQ contract imply 75 and 40 index points. Development selected 70 points for the stop and introduced the synthetic first attempt at your request. This intentionally changes entries and counts. ATR(15) was reconstructed and tested because the author's indicator period was unspecified.

Cost-inclusive sizing, ownership isolation, restart journals, protective-order checks, calendar cutoff handling and chart/logging controls were added for execution. These controls are separate from the author's strategy description.

## Historical evidence and current build

| USTEC trade counts | IS 2020–2022 | Development 2023–2025 | Frozen 2026 Jan 1–Sep 9 | Total |
|---|---:|---:|---:|---:|
| The Vault Break | 331 | 428 | 149 | 908 |

The historical USTEC runs used $50 planned risk. Imported NQ tests used $5,000 planned risk as a different instrument/risk contract inside MT5. Neither is the current $100 default. The 2026 column belongs to the preserved frozen candidate and was run once; no settings were tuned afterward. Counts of 149 Vault and 58 Overnight are 2026-only, not three-year IS totals.

The current interface/visual cleanup and Vault readiness correction compile successfully. A native 2022 reference comparison, $100 test, disabled-trading test and lifecycle fixtures are prepared in this model's validation directory. They must not be described as passed until their receipts exist. The running user tester is not interrupted to execute these checks.

Author screenshots are reference material, not demonstrated parity: source code, settings, feed, costs and matching trade lists are unavailable, and the author ends September 24 rather than our frozen September 9 endpoint. Imported NQ-versus-USTEC corroboration does not prove reproduction of the author's engine.

## Broker clock

Clock settings are internal; there are no user-facing timezone inputs. Live mode measures TimeTradeServer against TimeGMT, preserving quarter-hour offsets and applying New York and Central DST at their respective UTC transition hours. Tester mode uses the ORB_Scalper 18:00 New York weekly-opening reference, located from a genuine Friday-to-Sunday/Monday gap. M1 data identifies the actual reopening within the H1 bar. The weekly offset is cached; an older anchor is accepted only in the same DST season, avoiding a stale offset across the transition weekend. Missing history is requested again automatically. There is no fixed broker fallback.

Broker names and symbol suffixes do not gate initialization: USTECm and other Nasdaq CFD naming conventions use the attached symbol's native tick size, volume limits, filling policy and OrderCalcProfit for cash sizing. This changes broker portability, not the asset hypothesis: the model remains a Nasdaq model. Hedging-mode accounts remain required for independent sleeve ownership.

ATR candles end at 17:00 New York. Broker D1 ATR is retained when its daily opens already align to that boundary; otherwise completed H1 OHLC is aggregated into the same daily buckets (M1 for fractional-hour clocks). The 15-session mean of rolling simple ATR keeps the existing period: 15 for Vault, 10 for Overnight. The first partial bucket only supplies its closing price for the next true range. Fewer than the required completed sessions causes a history wait, never invented prices.

Automatic tester inference is designed for the Nasdaq overnight feed's 18:00 New York reopening, as in ORB_Scalper. A different instrument/session schedule is not established by this inference. Clock detection cannot infer an arbitrary historical timezone from price timestamps alone without a known session anchor; such feeds need separate evidence, not a guessed broker offset.

Native builds and deterministic clock/ATR fixtures are recorded in validation. No additional 2026 strategy backtest was run for this repair. Exness VPS attachment and live/feed parity are not verified locally. Copy the compiled EX5 alone; its code and Overnight ADX resource are embedded.

## Installation and validation

Use VaultBreak.mq5 / VaultBreak.ex5 and VaultBreak.set from this folder. Attach only one instance per account/symbol. The model's ownership identity is fixed internally. Trading is enabled by default for tester, demo and supported live accounts; this cleanup has not activated it on any account.

Upgrade while this EA has no owned open position. The ownership journal key now distinguishes account, server and account mode; old journals are not automatically migrated. Do not remove another EA's journal or protection.

History and indicator calculation are asynchronous. Attachment succeeds while data loads, but new entries remain blocked. The timer still checks owned protection and daily cutoff. Once ready, the latest already-closed source candle becomes the startup floor: no closed candle from before readiness can trigger a retroactive entry, including one saved in the old journal. Only a newly completed source candle may qualify. Existing owned exposure is managed rather than used to open a new trade.

Run this folder's validation/run_native.ps1 with a model-local config filename. It uses the installed MT5 Default profile and Every Tick, with chart trading disabled during validation. It refuses to interrupt an open terminal and refuses a new 2026 test. Each model writes its reports and inputs under its own validation directory.

## Source layout

The EA and its three model-owned helpers live together in this directory:

- `StrategyEngine.mqh`: model inputs, clock/calendar rules, closed-bar signals and EA lifecycle.
- `ExecutionRuntime.mqh`: position sizing, execution/recovery, ownership checks and trade exports.
- `ChartVisuals.mqh`: chart objects, panels and indicator display.

Validation scripts, configurations, receipts and historical evidence remain in `validation/`. There is no separate `include` directory.

## Transfer to another PC

Copy only this model's production `.ex5` into the other terminal's `MQL5/Experts` directory. The `.mq5`, `.mqh`, `.set` and validation files are not runtime dependencies. Vault has no custom indicator dependency.

The destination still needs MT5, broker access/history, trading permissions and a hedging-mode account with the symbol's broker contract specifications available. Logs, ownership locks and recovery state are generated locally by the EA; they are not bundled configuration files. Defaults are compiled into the EA; the `.set` file is optional. Native compilation and source dependency checks pass; a clean second-PC runtime test has not been performed.

Future NT8 delivery must likewise package strategy-owned helpers/indicators in the strategy distribution without loose runtime support files. This requirement does not constitute a new NT8 build.

## Timing and cost-default revision — 2026-10-08

Vault signal timing is unchanged. Defaults now model 7 account-currency units of round-trip commission per lot and 3 broker points of slippage per side, with detailed logging off. Summary tester receipts and essential protection/error messages remain enabled. The previous `SlippagePrice` option has been replaced by `SlippagePoints`; use the current preset or reset inputs rather than loading an old saved input schema. Model-local diagnostic presets explicitly retain their research assumptions and enable logging where required. No 2026 performance test or tuning was performed for this revision.


## Adaptive commission and cost-covered stop — 2026-10-08

Live sizing reserves commission plus fixed slippage on both sides. Broker commission and deal fees are learned from fully matched entry/exit volumes, including partial fills and exits; incomplete positions do not qualify. The cache refreshes at most once per minute and resets when the account or server changes. Restarting rebuilds it from broker history. Separate balance charges and charges not attributed to position deals cannot be inferred. Essential commission-source changes remain visible even when detailed logs are disabled.

Adjusted live trade results include entry and exit broker charges once and subtract the fixed modeled slippage reserve; learned commission is not deducted a second time. Tester reporting reserves only the portion of fallback commission not already charged by native simulated deals. These accounting changes do not simulate actual fills or guarantee a zero-loss stop.

## Native minute history and preparation recovery — 2026-10-08

Session calculations use synchronized native M1 bars in their actual timestamp order. Minutes without recorded trades or quotes need no fabricated OHLC or volume. History must begin at or before the requested window start; disconnected live feeds, asynchronous downloads, empty windows and invalid/out-of-order bars cannot enable entries. When preceding history is unavailable the EA requests it and retries on later minute processing. A synchronized provider series cannot independently prove whether an interior absence was a quiet minute or a provider omission; this is no claim of reconstructed ticks or complete provider data.

The session resets and broker/journal recovery run once. ATR/opening-window preparation can retry without erasing trade attempts or recovery state. Live recovery consumes the latest completed source candle so a signal observed while history was unavailable cannot trigger a stale fill. Default essential DATA_WAIT messages identify each changed reason; detailed trade logging remains disabled. Tester rejectedDays now counts sessions still unresolved after retries rather than permanently counting an initial temporary failure.

Opening prices use the first actual recorded opening trade inside the nominal anchor interval: Vault's midnight Central M30 interval; Overnight's08:30 Central M15 interval. Nominal chart anchors remain unchanged. Overnight middle-third bias hides the opening range. Vault VWAP and its synthetic attempt process actual native bars, with stop-first resolution when a bar spans both stop and target. Costs, sizing, owned-position protection, entry conditions and trailing settings remain unchanged. Loading and connection checks do not reconstruct an unobserved price path.

Verification: both production EAs compile cleanly; validation/test_sparse_history.py executes extracted production history/anchor helpers against sparse, truncated, unsynchronized, disconnected, duplicate, reversed and invalid-OHLC fixtures. It also checks retry ownership and stale-signal guards. Existing input, sizing and commission checks pass. No new2026 or strategy-performance run was used.



## RBE panel and connection handling — 2026-10-10

The panel retains RBE MT5 position (10,20), height (198), colors, shadows, collapse control, Segoe UI Semibold 9-point title and 8-point rows. Width is the original RBE 210 pixels. The clock row reads: NY: HH:MM:SS CT: HH:MM:SS. Full status is available in the last-row tooltip. Vault displays NUB (three decimals), SOP, VWAP, funded attempt count out of three, owned position state, commission/slippage, fixed 60/40 exits and sizing/status. There is no simulated-first setup row in the promoted model.

Verification: production EA compiled with zero errors and warnings. The original eight public inputs and model/execution rules remain unchanged. EX5 remains standalone. Actual live-chart appearance remains a forward visual check.


## Execution visuals

Owned broker fills are reconstructed from available loaded chart history and grouped by position ID. Grey risk and blue realized profit boxes use actual entry/exit times and volume-weighted prices. Initial stop levels come from the opening order; historical Vault trades without a recorded initial stop do not invent a risk box or infer a 60-point stop for old trades. Profit boxes and actual entry/exit objects remain available. When the original stop exists, a closed loss box extends to its actual exit as in RBE. Open boxes extend with the chart; completed history is cached and refreshed when an owned deal arrives. White entry/exit arrows follow the RBE style. Risk/Lots labels are capitalized; the panel remains 210 by 198 pixels. New detailed trade exports identify this model as `funded_60_40_cap3`.

Sim #1 is the initial simulated attempt, not a broker position. Dashed entry/stop/target lines and the panel state distinguish it from subsequent real fills. Resolved simulated lines end at the completed M1 bar boundary.

Source-contract checks pass. No entry, sizing, cost or signal-clock rules were changed by the execution-visual update.
