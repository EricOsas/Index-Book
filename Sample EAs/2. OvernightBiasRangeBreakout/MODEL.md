# The Overnight Bias Range Breakout

## Cost-corrected V2 implementation — 2026-10-30

The EA now permits one strictly later same-side retry after the first attempt has fully exited through a losing broker protective stop. It does not retry after a target, time/manual/recovery close, rejected entry, partial exit, or an opposite signal. The first bracket remains 0.30 ATR stop / 3R target with the existing completed-M1 trail. The retry is 0.15 ATR stop / 3R target and receives one broker-cost-covered break-even move after a completed M1 reaches +0.5R; it has no later ratchet. Retry state is saved in the existing strategy-owned journal. Older journals load with retry disabled rather than assuming missing state.

## Restart and outage recovery

The journal is isolated by broker server, account, account mode, symbol and magic; an exclusive local instance lock prevents duplicate local ownership. Original risk, entry time, peak R and last processed M1 source are committed with the exact immutable broker position identifier (two integral 32-bit halves). The entry nonce, bracket, time, costs and consumed attempt are flushed before submission. An accepted but uncommitted fill is hydrated only when its broker comment, fill time and unchanged SL/TP verify that intent.

Close-recovery code is embedded in this folder's `ExecutionRuntime.mqh`; no parent-folder helper is required. It durably stages exact-position close requests separately from risk state. Accepted replies require position-volume and deal/order acknowledgment; timeouts and unclear acceptance block duplicates and display attention. A terminal partial fill allows only the actual remaining quantity to be closed. Definitive rejection leaves protection intact and persists a five-second retry pause. New entries and trailing wait while a close is unresolved.

Reinitialization restores the peak and source checkpoint, then observes genuine missed completed-M1 closes without repeating already processed bars. Only the current broker quote can execute a tighter stop; an existing tighter broker stop is never loosened. Legacy state migrates only when the original entry-order SL verifies original risk; the missing M1 checkpoint is reconstructed from the entry boundary. Missing/corrupt/ambiguous state retains broker SL/TP, blocks new entries and unverifiable trailing, preserves evidence and displays `Recovery needs attention`. Normal owned time exits and missing-protection safety still apply. No orders are sent disconnected. Terminal/VPS startup and independent-VPS coordination are outside a local EA's guarantees. Native remove/re-add, restart and reconnect tests remain required separately from compilation and extracted fixtures.

## Implemented specification — 2026-10-11

All rule times are Central (CT), with automatic DST conversion. Fixed M1 source calculations are independent of the displayed chart timeframe.

| Component | Exact rule |
|---|---|
| Overnight range | Previous day 23:00 CT through current day 08:30 CT, end exclusive; require complete consecutive M1 coverage. |
| Bias | Use the 08:30 opening price. Strictly inside upper third = long; strictly inside lower third = short; middle third, exact boundaries, and opens outside the overnight range abstain. |
| Opening range | 08:30–08:45 CT, end exclusive; 15 completed M1 bars. Draw only when the initial bias gate passes. |
| Confirmation | Completed M15 close strictly beyond the directed opening-range boundary, with MT5-style Wilder ADX(15) strictly >20. No falling-ADX condition. |
| Entry window | Confirming close strictly after 09:00 CT; earliest is 09:15. Next available quote/M1 open, strictly before cutoff. |
| Volatility | Daily boundary 16:00 CT / 17:00 NY. Rolling simple ATR(10), then mean of last 15 completed ATR values; 24 daily true-range observations plus initial close seed for reconstructed history. |
| Initial bracket | Stop distance 0.30 × average ATR; target 3 × that distance. Establish executable rounded levels from submission quote (historical NT8: next-open fill); retain target after fill. |
| Trail | Original R = actual fill to initial rounded stop. Activate on a completed M1 close reaching 1.5R; follow best completed close at 1.5R distance, advancing in 0.5R steps. Never loosen. |
| Cost coverage | Active trail also applies the cost-covered floor. Account for round-trip fees plus future exit slippage live; both modeled slippage sides in historical fills that do not simulate them. |
| Limits | At most two funded attempts/day. Attempt two must be same-side and a strictly later fresh signal after a losing protective-stop exit. No synthetic first trade or VWAP. |
| Time exit | 14:30 CT normally, 11:55 under the preserved special-day policy shared with Vault. |

The special-day policy retains the explicit 2020–2025 source dates, including January 9, 2025. From 2026: observed New Year, third-Monday January/February holidays, Good Friday, last Monday in May, observed June 19, July 3 and observed July 4, first Monday in September, fourth Thursday in November and following day, December 24, observed December 25, and December 31. This is a fixed conservative model policy, not a verified exchange-hours calendar.

NT8 uses native NQ/MNQ source data; NNQ uses its own adequate history, with same-expiry NQ fallback when required. Orders and costs stay on the attached contract. MT5 uses its attached Nasdaq symbol. ADX is fixed M15, not an ADX recalculated on the viewed chart period.

Cash risk includes the configured cost reserve; fixed quantity replaces the cash cap and defaults off. Stop protection, broker constraints, ownership and connection checks still apply. MT5 defaults: cash 100, fixed lots 1, commission fallback 7/lot, slippage 3 broker points each way. NT8 defaults: cash $250, fixed quantity 1, commission fallback $4/contract, slippage 3 × 0.01 price each way. Native Analyzer costs and explicit modeled-net costs are separate reports.

The user's current OBRB comparison is satisfactory. No rules are changed to chase the author's trade count. Matching-feed execution parity, cost-adjusted results and forward/live readiness remain distinct from that comparison.

This folder owns one independent MT5 EA, its includes, preset, documentation and validation artifacts. VWAP Pullback is now a separate model in its own folder.

## Fixed model

- Overnight range is previous-day 23:00 to current-day 08:30 Central. The transcript describes this 23:00 start in its detailed explanation, but later chart narration says midnight; the exact author code is unavailable.
- The 08:30 open selects direction from the outer thirds of that range; opens in the middle third do not trade.
- Opening range uses completed 08:30–08:45 M1 bars. A completed 15-minute candle must close beyond the opening-range boundary in the selected direction, with ADX(15) strictly above 20.
- The confirming M15 candle must close strictly after 09:00 Central. The first eligible source candle is 09:00–09:15; earliest entry is 09:15 Central (17:15 on the verified ICMarkets broker clock). A late quote cannot make the earlier 08:45–09:00 candle eligible.
- One broker trade per day, before the session cutoff. Initial stop distance = 30% of the mean of the last 15 completed daily ATR(10) values; target = 3 times that original distance.
- After a completed M1 close reaches 1.5 times original risk, the stop follows the best completed M1 close, 1.5 times original risk behind it, advancing in 0.5-risk steps. The stop only tightens.
- Trailing decisions use completed M1 bars. Broker stop execution still occurs when price reaches the protective order.

Both models use a conservative 11:55 Central cutoff on specified special calendar days rather than 14:30. It is a research safety rule, not a verified exchange-hours calendar or a rule demonstrated by the author. Owned positions are closed at cutoff when the terminal has a tradable quote; a disconnected terminal cannot execute a scheduled market close.

This model does not use VWAP, Session Opening Price (SOP) or Noise Up Barrier (NUB). Those belong to Vault. The broker clock is detected automatically using the ORB_Scalper server/GMT and weekly-opening approach; see Broker clock below. A clock awaiting history blocks new entries without removing the EA.

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
| Display: Show chart levels, trade markers and information | On | Draws bounded overnight/opening-range levels, the M15 ADX pane, owned trade markers and status. |
| Logging: Write a detailed trade and decision log | Off | When enabled, writes decisions and detailed tester trade exports to Common/Files/OvernightBiasRangeBreakout/logs. |

Cash sizing includes configured costs before rounding volume down. Trades below broker minimum volume or over the planned budget are rejected. Fixed sizing uses the requested lots instead; it does not promise a $100 loss limit. A gap or execution beyond the reserved allowance can still make realized loss exceed a cash budget. Fixed-size detailed exports use zero in the legacy cash-budget and cash-budget-normalized fields because no cash cap is active.

## Chart anchors and entry timing

Overnight high/low and bias thirds run from the previous 23:00 Central start to the daily cutoff. Opening-range levels run from08:30 to cutoff only when the first observed opening trade in the08:30–08:45 interval selects a nonzero overnight bias. Neutral middle-third opens hide them. Live projection uses synchronized native M1 history with coverage preceding the overnight start; quiet minutes do not require invented bars. It never changes trading state or enables entries. Each level is a bounded trend segment with both rays disabled; drawings survive chart-timeframe reloads. The ADX pane always derives its values from M15 and maps them onto the viewed chart timeframe. Initialization removes only duplicate panes with this EA’s exact indicator name, preserving unrelated indicators.

The current panel shows NY and CT on one row, without the bracketed broker clock. New York is one hour ahead of Central on trading days. Display clocks do not shift entry windows or anchors. Tooltips identify only this model and its level. On initialization, the EA removes its own objects using the former object prefix for this strategy ID, leaving other chart objects alone.

A candle timestamp identifies its open. A completed M15 candle confirms direction only at its close; entry is attempted on the next available quote. M1 trailing decisions use completed closes and cannot anticipate an incomplete bar.

## Changes from the transcript

The transcript contains both a detailed 23:00 overnight-start description and a later midnight chart description. The model implements 23:00; exact author parity is not established. An early baseline used a 2-risk target; the final candidate restores the transcript's 3-risk target and adds the completed-M1 trailing rule. ATR(10), ADX(15), and strict directed-close confirmation are reconstructed choices; their exact author implementation is unavailable.

Cost-inclusive sizing, ownership isolation, restart journals, protective-order checks, calendar cutoff handling and chart/logging controls were added for execution. These controls are separate from the author's strategy description.

## Historical evidence and current build

| USTEC trade counts | IS 2020–2022 | Development 2023–2025 | Frozen 2026 Jan 1–Sep 9 | Total |
|---|---:|---:|---:|---:|
| The Overnight Bias Range Breakout | 279 | 287 | 58 | 624 |

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

Use OvernightBiasRangeBreakout.mq5 / OvernightBiasRangeBreakout.ex5 and OvernightBiasRangeBreakout.set from this folder. Attach only one instance per account/symbol. The model's ownership identity is fixed internally. Trading is enabled by default for tester, demo and supported live accounts; this cleanup has not activated it on any account.

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

Copy only this model's production `.ex5` into the other terminal's `MQL5/Experts` directory. The `.mq5`, `.mqh`, `.set` and validation files are not runtime dependencies. The custom ADX display indicator is embedded in the EA binary and loaded from its internal resource; no separate indicator file is needed.

The destination still needs MT5, broker access/history, trading permissions and a hedging-mode account with the symbol's broker contract specifications available. Logs, ownership locks and recovery state are generated locally by the EA; they are not bundled configuration files. Defaults are compiled into the EA; the `.set` file is optional. Native compilation and source dependency checks pass; a clean second-PC runtime test has not been performed.

Future NT8 delivery must likewise package strategy-owned helpers/indicators in the strategy distribution without loose runtime support files. This requirement does not constitute a new NT8 build.

## Timing and cost-default revision — 2026-10-08

The after-09:00 confirming-close gate corrects the later transcript chart narration at the user's request. Historical frozen performance results predate this correction and are not evidence for this revised build. Defaults now model 7 account-currency units of round-trip commission per lot and 3 broker points of slippage per side, with detailed logging off. Summary tester receipts and essential protection/error messages remain enabled. The previous `SlippagePrice` option has been replaced by `SlippagePoints`; use the current preset or reset inputs rather than loading an old saved input schema. Model-local diagnostic presets explicitly retain their research assumptions and enable logging where required. No 2026 performance test or tuning was performed for this revision.


## Adaptive commission and cost-covered stop — 2026-10-08

Live sizing reserves commission plus fixed slippage on both sides. Broker commission and deal fees are learned from fully matched entry/exit volumes, including partial fills and exits; incomplete positions do not qualify. The cache refreshes at most once per minute and resets when the account or server changes. Restarting rebuilds it from broker history. Separate balance charges and charges not attributed to position deals cannot be inferred. Essential commission-source changes remain visible even when detailed logs are disabled.

Adjusted live trade results include entry and exit broker charges once and subtract the fixed modeled slippage reserve; learned commission is not deducted a second time. Tester reporting reserves only the portion of fallback commission not already charged by native simulated deals. These accounting changes do not simulate actual fills or guarantee a zero-loss stop.

## Native minute history and preparation recovery — 2026-10-08

Session calculations use synchronized native M1 bars in their actual timestamp order. Minutes without recorded trades or quotes need no fabricated OHLC or volume. History must begin at or before the requested window start; disconnected live feeds, asynchronous downloads, empty windows and invalid/out-of-order bars cannot enable entries. When preceding history is unavailable the EA requests it and retries on later minute processing. A synchronized provider series cannot independently prove whether an interior absence was a quiet minute or a provider omission; this is no claim of reconstructed ticks or complete provider data.

The session resets and broker/journal recovery run once. ATR/opening-window preparation can retry without erasing trade attempts or recovery state. Live recovery consumes the latest completed source candle so a signal observed while history was unavailable cannot trigger a stale fill. Default essential DATA_WAIT messages identify each changed reason; detailed trade logging remains disabled. Tester rejectedDays now counts sessions still unresolved after retries rather than permanently counting an initial temporary failure.

Opening prices use the first actual recorded opening trade inside the nominal anchor interval: Vault's midnight Central M30 interval; Overnight's08:30 Central M15 interval. Nominal chart anchors remain unchanged. Overnight middle-third bias hides the opening range. Vault VWAP and its synthetic attempt process actual native bars, with stop-first resolution when a bar spans both stop and target. Costs, sizing, owned-position protection, entry conditions and trailing settings remain unchanged. Loading and connection checks do not reconstruct an unobserved price path.

Verification: both production EAs compile cleanly; validation/test_sparse_history.py executes extracted production history/anchor helpers against sparse, truncated, unsynchronized, disconnected, duplicate, reversed and invalid-OHLC fixtures. It also checks retry ownership and stale-signal guards. Existing input, sizing and commission checks pass. No new2026 or strategy-performance run was used.


At the existing 1.5-original-risk activation, the live initial trailing stop covers round-trip commission plus one exit-slippage allowance above the actual fill for a long, or below it for a short. Entry slippage is already reflected in a live fill. Tester stops cover both modeled slippage sides because simulated entry slippage is not embedded in the fill. Subsequent 0.5-risk steps retain this minimum cost floor and can only tighten; the original-risk scale and 3-risk target stay unchanged. Broker tick rounding is outward toward the profitable side. Invalid native cash conversion blocks the stop change. Current entry charges can raise the reserve if they imply a higher round-trip rate. The $100 default cash-mode planned risk and three-broker-point fixed slippage remain unchanged.


## RBE panel and connection handling — 2026-10-10

The panel retains RBE MT5 position (10,20), height (198), colors, shadows, collapse control, Segoe UI Semibold 9-point title and 8-point rows. Width is the original RBE 210 pixels. The clock row reads: NY: HH:MM:SS CT: HH:MM:SS. Full status is available in the last-row tooltip. The header is Overnight Bias Breakout. Rows show the eligible opening range, ADX/bias, trades/position, commission/slippage and cost-covered BE/trail. Display ADX uses the current M15 Wilder(15) value, matching the histogram to three decimals on any viewed chart timeframe. Entry confirmation continues to use completed M15 bars.

Verification: production EA compiled with zero errors and warnings. The original eight public inputs and model/execution rules remain unchanged. EX5 remains standalone. Actual live-chart appearance remains a forward visual check.


## Execution visuals

Owned broker fills are reconstructed from available loaded chart history and grouped by position ID. Grey risk and blue realized profit boxes use actual entry/exit times and volume-weighted prices. Initial stop levels come from the opening order; historical Overnight trades without a recorded initial stop do not invent a risk box. Open boxes extend with the chart; completed history is cached and refreshed when an owned deal arrives. White entry/exit arrows follow the RBE style. Risk/Lots labels are capitalized; the panel remains 210 by 198 pixels.

Source-contract checks pass. No entry, sizing, cost or signal-clock rules were changed by the execution-visual update.
