# Vault Break — NT8

Native chart history correction 2026-10-17: an existing recovery journal must not suppress historical strategy orders. The recovery pending/fault entry gate now applies only in realtime. Completed chart sessions execute through NT8's existing native M1 fill engine, supplying platform execution arrows and trade lines; these are simulated historical fills, not reconstructed broker fills. As in RBE, the incomplete tail session is excluded from chart order simulation until the loaded execution bars extend at least two minutes beyond the strategy cutoff, preventing historical exposure from entering live recovery. Analyzer behavior is unchanged. The existing realtime journal load, ownership checks and historical-flat transition refusal remain intact. Chart/VPS visual acceptance remains separate from compilation and gate fixtures.

Recovery lifecycle correction 2026-10-16: never add a replacement leg to an existing OCO group. Retain surviving protection and wait for missing/cancelling broker evidence. Restore a fully settled bracket stop-first with a fresh durable OCO intent only after owned fills reconcile. A genuine active close takes priority; delayed equal-fill callbacks cannot regress terminal/cancelling state. A manual close outside owned execution evidence remains pending on shared accounts; account flatness does not fabricate an exit. Native compilation and fixtures are separate from VPS restart acceptance.

Recovery update 2026-10-16: temporary Account.CreateOrder IDs are replaced with provider IDs from the exact registered order callback before reconciliation. Legacy temporary-GUID protective bracket IDs may bind only to one matching acknowledged broker order, with exact account/contract/owner/OCO, direction, type, quantity, prices and fill bounds. The original journal is backed up as .before_provider_id_<UTC ticks>; success logs RECOVERY_PROVIDER_ID_BOUND. Binding sends no broker orders. Assigned-ID conflicts remain blocked. Source fixtures and native compilation do not establish VPS runtime acceptance.

Current runtime recovery (2026-10-15): [RECOVERY.md](RECOVERY.md) specifies automatic mismatch detection, retry on delayed owned broker evidence, and the case-specific external-close confirmation fallback. This supersedes older restart/ownership limitations below. Trading model parameters are unchanged.

## Implemented specification — 2026-10-13

All rule times below are Central (CT), with DST applied automatically. This is the current candidate, not an assertion of author parity.

| Component | Exact rule |
|---|---|
| Source | Matching-expiry NQ completed M1 bars for NQ/MNQ/NNQ execution; fixed civil M30 signal candles, independent of chart timeframe. |
| SOP | Open of the 00:00 CT minute, equivalent to 01:00 NY; not that minute's close. |
| Daily volatility | Daily boundary 16:00 CT / 17:00 NY. ATR(15) is a rolling simple mean of true ranges; average the last 15 completed ATR values. NUB = SOP + 0.30 × that average. |
| Warmup | For reconstructed daily history, exclude the first partial bucket and use its close as the seed. Then require 29 completed daily true-range observations to obtain 15 ATR(15) values. Roughly 45 calendar days is a loading guideline, not a guaranteed coverage test. |
| VWAP | Reset at 00:00 CT; cumulative completed-M1 HLC3 × volume / volume. NT8 uses exchange bar volume; MT5 prefers real volume and otherwise uses tick volume. |
| Signal | Completed M30 close strictly above both NUB and current completed-M1 VWAP. Confirming close must be at/after 10:00 CT and strictly before the day's cutoff. No crossing-from-below requirement. |
| First entry | The first fresh qualifying M30 close funds a long at the next executable M1 quote/open. No synthetic trade. |
| Daily cap | At most three funded attempts/day; no simultaneous owned trade or replay of an old signal. |
| Funded exits | Fixed 60-point stop / 40-point target; no ADX, BE or trailing. Time exit 14:30 CT normally, 11:55 on special days. |
| Data rejection | Exact opening minute and complete required M1/M30 windows. Missing required source minutes block entries rather than fabricate candles. |
| Counting | Panel and logs count actual funded attempts directly, from zero to three. |

The special-day cutoff is a preserved model policy, not inferred from the chosen chart trading-hours template. The source retains its explicit 2020–2025 dates (including January 9, 2025). From 2026 it applies observed New Year, third-Monday January/February holidays, Good Friday, last Monday in May, observed June 19, July 3 and observed July 4, first Monday in September, fourth Thursday in November and the following day, December 24, observed December 25, and December 31.

NT8 pins matching-expiry NQ signals for all three execution sizes, matching the tested research candidate. Orders, tick rounding, quantity and costs belong to the attached order contract. MT5 uses the attached Nasdaq symbol and its broker specifications; this source promotion does not establish CFD/futures feed parity. Cash/fixed sizing, costs, ownership and connection guards remain active.

### Promotion and recovery — 2026-10-13

The supplied native 60/40 run reconciles 1,060 entry/exit pairs exactly with its audit. For January 21, 2022–September 24, 2026, one MNQ contract before commissions: $11,863.50, PF 1.3054, realized closed-equity DD $1,209, annualized weekday daily Sharpe 1.7327. The old live model has lower DD/higher PF; the selected candidate has higher profit/Sharpe and improves every compared metric over 75/40. These are retrospective native results, not unseen or live execution proof.

The prior synthetic-first 70/40 source is archived outside the compiled strategy tree in `research/vault_break/archive/live_70_40_before_20261013/`. Journal revision `60_40_funded3_v2` subtracts the old synthetic reservation once, rejects invalid counts/revisions, and preserves saved blocked status. Unknown or previous-instance protection is not silently adopted or rewritten. Already working protection is not resized by a model update. Existing request-quote live brackets and actual historical fill anchoring are preserved; live slippage can change realized entry-to-stop distance, and the actual-fill risk guard remains active.

An extracted-source test executes the promoted live and selected research models on identical completed M1 data: funded signal timing, ATR/VWAP, daily cap and arithmetic agree. New-source native Analyzer reproduction and a controlled reload/reconnect check remain the next runtime gates. Existing risk settings are retained: cash sizing can choose a different quantity because the new stop is smaller; the supplied comparison used fixed one-contract sizing.

### Superseded 70/40 reference differences and supplied Analyzer audit

The author screenshot covers **2022-01-21–2026-09-24**: 1,151 trades, 779 wins, 371 losses, one flat, average trade $112.48. The supplied NT8 CSV reconciles to **682 funded NQ trades**, 452 wins, 228 losses, two flats, and $78,560 gross. One contract throughout; native commission and slippage are zero in this run.

Our stop is **70**, while the author's reported $1,500 loss per NQ contract implies **75**. Our synthetic-first gate excludes the first attempt from funded counts and affects later eligibility; whether the author has an equivalent first-attempt gate remains unconfirmed. Keep confirmed specification differences separate from assumptions about the author's implementation.

The first exported fill is March 8, 2022. January 21–March 7 contains 32 weekdays: even three funded entries every weekday could explain at most **96** of the **469** count difference through initial warmup. At least 330 synthetic attempts preceded the 330 days with funded trades; these are absent from the CSV, as are virtual-only days and rejected-session reasons. Neither warmup nor exact author parity is established by this export.

Your planned **2026-01-21–2026-09-24** comparison is separate. Filtering the existing, already-warm export to that interval yields **151 trades and $14,180 gross**. A new run starting January 21 must first build its own warmup and is not directly comparable to that filtered result.

### Completed warm-log audit - 2026-10-11

Latest NT8 log `C:/Users/Administrator/Documents/NinjaTrader 8/VaultBreak/logs/VB91008010_8a98ef1702fc.csv` loads October 31, 2021 through September 24, 2026. All 715 funded entry fills reconcile with 715 exit fills; no residual quantity remains. Filtering entries to January 21, 2022 through September 24, 2026 gives **705 trades and $76,775 gross**, versus the original CSV's 682 / $78,560. The warmed comparison has 23 more trades and $1,785 less gross; do not infer that more trades improve the model. The gap to the author's 1,151 is now **446**, so initial warmup does not explain the remaining difference. January 21-September 24, 2026 alone still gives **151 / $14,180 gross**. The log records MINUTE, ENTRY_REQUEST, ENTRY_FILL and EXIT_FILL only; it has no rejected-session or synthetic-lifecycle reason breakdown, so the remaining gap cannot yet be attributed from this log. Prices/quantities yield gross NQ PnL; costs are not deducted. No Vault strategy rule was changed in this audit. Full audit: `C:/Users/Administrator/Documents/Strategies/Conte_Research_Strategies/research/vault_break/VAULT_WARM_RUN_AUDIT.md`.

For a warm 2026 comparison, run from at least 2025-11-01 through 2026-09-24 with full ETH history, fixed one NQ contract, unchanged merge/roll settings, and detailed logging enabled; count only entries dated 2026-01-21 onward in the exported platform timezone. Verify daily-history readiness rather than assuming the loading date guarantees it. Keep native commission/slippage zero only for matching this gross baseline; cost-adjusted performance is a separate comparison. Decision logs are needed to distinguish ATR waits, incomplete sessions, synthetic waits and funded entries.

Independent standalone NT8 port of the current MT5 VaultBreak model. One VaultBreakNT8.cs contains strategy-owned calculation, clock/calendar, execution and chart display. It imports no custom indicator or helper. NinjaTrader itself and its built-in assemblies remain required. Export/import through NinjaTrader's native strategy distribution workflow; do not replace another PC's entire Custom DLL.

Supported native instrument names: NQ, MNQ and NNQ. On NNQ, both NNQ and same-expiry NQ ETH M1 histories are requested over the selected chart/Analyzer range, without a fixed bar-count cap. NNQ is preferred when loaded history meets the original ATR warmup and exact-minute session requirements; NQ supplies fallback signals/visuals otherwise. Source selection is pinned for the loaded run and reassessed on reload, keeping active-position calculations consistent. All orders use NNQ M1 and its native contract specifications. The panel identifies signal and order contracts. NQ/MNQ behaviour is retained. Native historical trade history is retained explicitly. The panel displays `Trades 0/3` and NUB with three decimal places. Native loading and NNQ result parity remain separate gates.

**Block opposing index exposure** is a user option, default **On**. When enabled, entry rejects opposing Equity Index positions/pending orders across visible NT8 accounts and coordinates pending direction with Overnight. Turning it off bypasses that product-direction guard. Exact-instrument ownership isolation remains active either way; foreign positions are never flattened. Other PCs, hidden accounts and external order races are outside its scope. See [current validation status](validation/README.md).

## Frozen rules

M1 loading and recovery use an explicit ETH source, completed native-index catchup and provider repair for missing required live intervals. Recovered bars cannot submit old signals; owned exposure and attempts remain isolated. See [current validation status](validation/README.md). The one-second timer also reads available completed source bars when callbacks are queued. Use an ETH primary chart for the supported full-session live configuration; unavailable underlying/provider bars remain blocked. Native chart/reconnect observation remains required.

Central midnight Session Opening Price (SOP). Noise Up Barrier (NUB) = SOP + 30% of the mean of the last 15 completed daily ATR(15) values. Each daily ATR uses a **simple rolling average of true range**, matching MT5 iATR instead of NinjaTrader's Wilder ATR.

VWAP accumulates completed M1 HLC3 times traded volume from Central midnight. Every complete civil-clock M30 bucket uses 30 consecutive M1 bars. A completed M30 close strictly above NUB and VWAP qualifies during 10:00 inclusive to 14:30 exclusive Central. Special-day cutoff is 11:55, preserving the exact MT5 2020–2025 list and calendar algorithm thereafter.

Attempt 1 is synthetic: next M1 open, SL 70 and TP 40 index price units. No order. Completed M1 path resolves stop first on a double touch. Only after resolution may a later completed M30 signal fund attempts 2–4, maximum three funded entries. Fixed brackets; no trailing, ADX or opening-range rules.

Native minute timestamps denote bar **end**. The strategy converts the platform timezone using Windows Central DST rules and subtracts one minute. Calculations always use the added M1 series, independent of the viewed chart timeframe.

Live startup/cutoff use the native platform clock; Playback uses its connection clock. Wall-clock calendar time is not substituted into a historical Playback session.

## Data contract and parity limits

Use full NQ/MNQ electronic-session M1 history. RTH-only templates cannot provide midnight SOP/VWAP and reject days. Missing M1 bars from midnight through entry cutoff block that day. Daily OHLC is reconstructed from supplied M1 data on civil intervals from 16:00 Central to the next 16:00 Central, matching ICMarkets broker midnight (NY17). Exchange breaks produce no bars. The first supplied daily bucket is excluded as potentially partial; its close seeds the next TR. At least 29 subsequent completed daily observations produce the 15 daily ATR(15) samples; load a wider warmup window and audit coverage.

This reproduces the daily boundary/calculation, **not broker OHLC**. NQ traded volume versus USTEC tick-proxy volume, roll policy, missing bars and broker feed prices change signals. Compare daily OHLC/ATR/NUB/VWAP before claiming parity.

Analyzer submits market orders on the M1 series for the next native executable M1 open. Historical brackets use that fill as the request-quote proxy; realtime brackets retain the pre-submission ask reference, with risk checked again after every fill. MT5 also retains pre-request geometry. The historical proxy and native fill timestamp/ordering require reconciliation; historical executions can be stamped with the fill bar's end even when its open price is used.

Native historical funded stop/target collision ordering remains an engine gate. Synthetic collisions alone are explicitly adverse-first. No tick data, Tick Replay, 2026 rerun or optimization was used to implement this port.

## Inputs and defaults

| Option | Default | Meaning |
|---|---:|---|
| Enable new trades | On | Off consumes signals and continues owned exits/protection. |
| Risk per trade, including costs | $250 | Planned initial stop loss plus modeled round-trip costs. |
| Use a fixed number of contracts | Off | When enabled, fixed quantity replaces the dollar risk budget. |
| Fixed contract quantity | 1 | Positive whole contracts per entry; used only when fixed sizing is enabled. |
| Round-trip commission fallback / contract | $4 | ORB_NT default. Completed live NT execution charges replace this fallback; native charges may be template-derived. |
| Modeled slippage each way (points) | 3 | One point is 0.01 price on supported NQ/MNQ, so the default allowance is 0.03 price each way, matching MT5. Exchange order rounding still uses native ticks. |
| Show chart levels and information | On | Bounded SOP/NUB and active brackets, one dotted native VWAP plot and NY/Central clocks. Native execution markers are controlled by NinjaTrader. |
| Write detailed decision/trade log | Off | Optional generated CSV; no input/configuration file required. |

NQ ($20 per 1.00 price) minimum planned risk is 70×20 + 4 + .06×20 = **$1,405.20**. The $250 default skips funded NQ entries. MNQ ($2 per 1.00 price) minimum is **$144.12**, allowing one contract. Unaffordable minimum size is never rounded up.

Charts should load at least 45 calendar days of ETH M1 history, more around closures: the nested ATR15/mean15 calculation requires roughly 30 completed market sessions. SOP and VWAP display once today's Central-midnight session open is present; NUB waits for real ATR history. The panel reports the warm-up count or missing-source reason. Native order signal names stay within NT8's 50-character limit.

VWAP uses one native dotted plot, sampled from the existing completed-M1 calculation onto the viewed chart bars. It creates no per-minute drawing objects, breaks across session/source gaps and prevents later historical M1 values from being written into earlier viewed bars. Unchanged plot values and level geometry are not rewritten; inactive bracket objects are removed. Historical panel refresh is bounded rather than performed on every minute. Both panels match RBE: TopRight, Consolas 11, LightSteelBlue text, DarkSlateGray outline, Black background, opacity 85. Fresh GUI responsiveness still requires native runtime observation.

The retained session anchor is midnight Central = 01:00 New York, using that M1 bar's opening price. The NY chart's end-stamped candles do not change the source opening-price calculation. Overnight is a different model: its 23:00 Central overnight start corresponds to 00:00 New York.

Fixed sizing is an explicit alternative: it submits the specified contract count independently of the dollar budget, including when one NQ contract exceeds $250. It disables only dollar-budget sizing and actual-fill budget rejection. Bracket validity, stop/target protection, ownership checks and execution-failure handling remain active. Costs remain included in modeled results. The panel displays the fixed contract count when enabled and the dollar budget otherwise.

Commission gathering is keyed by execution ID: duplicate callbacks update fee references without replaying fill quantity. Only fully closed, volume-matched live trades on accounts outside Sim/Playback/Replay/Backtest contribute. Finite absolute Commission and Fee are included; retained execution references allow later native fee updates to change the next sizing estimate. The latest positive completed-trade rate is used, scoped by account and full contract, retained across chart timeframe reloads within the same NT process. A zero native fee has no availability flag, so it does not replace the fallback. Restarting NinjaTrader resets this in-memory cache; fallback applies until another completed live trade. Native charges can come from commission templates and are not independently verified broker statements. No external file is required.

Native Slippage is zero and IncludeCommission is disabled. The explicit allowance is included in sizing and deducted once from raw fill-price PnL in the printed adjusted summary. The native Analyzer gross report is not this separately adjusted result. Broker actual realized statements remain independent evidence; executed gaps/slippage can exceed the planned budget.

## Ownership and lifecycle

Unmanaged OCO brackets are installed for each partial entry execution. In dollar-risk mode, actual fill risk is checked against the whole budget. Owned rejection, missing protection, partial exit or OCO overfill blocks entries and reconciles only this instance's orders/net fills. Foreign events are ignored; no account-wide flatten. A one-second strategy-thread timer drives cutoff without waiting for price ticks. Disconnect/platform shutdown prevents a scheduled market exit; broker-held protection is the last defense.

Chart and Analyzer history both execute simulated historical orders. Execution arrows and performance connecting lines belong exclusively to native NinjaTrader; this strategy draws no custom fill arrows or trade connectors. Historical orders are not broker orders. An open historical position or working historical order blocks transition to realtime; it is never adopted or submitted to the account. Realtime activation consumes pre-activation source closes and clears chart-historical hypothetical attempts. Generated atomic state preserves attempt reservations. Interrupted synthetic paths block that session instead of retrospective replay. Previous-instance matching-ID working orders block adoption and remain untouched. Restart with existing owned exposure requires reconciliation; orphaned broker positions are not automatically adopted. A process lock prevents duplicate account/instrument/ID instances.

Vault and Overnight share an atomic process exposure lease by account/instrument, acquired before an entry request and released only after owned exposure and all native order/execution callbacks settle. This prevents the two independent models from concurrently attaching conflicting protection to one netted position. Existing same-instrument account positions/orders block entries. Other strategies do not participate in this lease; use separate accounts for concurrent sleeves on the same instrument. An interrupted instance with unresolved exposure keeps its lease fail-closed; reconcile the account before restarting.

## Proof and remaining gates

tests/test_model.py compiles and executes the actual embedded C# model using generated 2020–2025 data only. It verifies ATR/VWAP, M30 cadence, next-open synthetic path, gaps, disabled/stale signals, caps, calendar/DST and NQ/MNQ minimum sizing.

Native compile, Analyzer comparison on matched 2020–2025 data, Playback/Sim partial fills/rejections/OCO/restart/cutoff, chart display and clean-machine import are separate gates. Source/model tests do not establish live readiness. The consumed 2026 holdout is not reopened.

## Ownership and history loading

Execution identity, contract point value and tick rounding always use the primary order contract, independently of the active NQ/NNQ callback. An NNQ fallback panel shows `Signals NQ ... | Orders NNQ ...`; native NNQ source runs omit the redundant split label. Commission and exposure keys use that same order contract.

Ownership is internal and fixed per model. Run one instance of each model per account/instrument; both models retain separate identities and foreign-order isolation. ETH M1 requests cover the selected chart/Analyzer dates without a fixed bar-count cap. Load at least 45 calendar days before a comparison period for ATR warmup; sparse data may need more. The active tests are in tests/, supplied reports and the current verdict are in validation/, and superseded repair evidence is archived outside bin/Custom. This revision changes data loading and the public input surface, not model trading rules. No new 2026 run was performed.


## RBE panel and connection handling — 2026-10-10

The panel retains RBE NT8 geometry, Segoe UI sizes, colors, top-right position, drag header and collapse button. The full NY/CT clock occupies one header row: NY: HH:MM:SS CT: HH:MM:SS. Trades share the sizing row. Panel strings use plain ASCII separators and collapse symbols to avoid encoding corruption during transfers. The headers are Vault Break and Overnight Bias Breakout. It is embedded in this strategy; no RBE installation is required. Vault displays NUB (three decimals), SOP, VWAP, funded trade count, owned position/stop, sizing and costs.

New strategy instances default to native ConnectionLossHandling.KeepRunning. Existing saved instances may retain their previous connection-loss setting; select Keep Running once or recreate the instance. This setting cannot undo an account auto-close/manual disable. While the account is disconnected, all owned submit/cancel/change requests are withheld. Due-close intent remains queued; cancellation and exact owned closure resume after reconnect. The reconnect time becomes the freshness floor, preventing old signals from being submitted. Historical simulation does not require a connected account.

Verification: the earlier build passed full Custom compilation and broker/source-recovery checks. The latest panel-only patch passed ASCII, single-clock and model-caption source checks. A fresh full compile is blocked by stale project paths after folder renames and the missing native Strategies/@Strategy.cs file. Native compilation and actual chart rendering remain unconfirmed for this patch. No performance rerun or model-rule change was made.


## Execution visuals

Real historical and live fills use native NinjaTrader execution markers and trade lines, plus RBE/ORB execution boxes: grey stop-risk area, blue realized profit area and a bounded entry line. Completed boxes retain actual entry/exit times and weighted fill prices; partial exits remain open until fully resolved. Completed drawings are cached and retried when a chart attaches. No custom execution arrows are fabricated. All drawing helpers are embedded in this strategy file; copy the single .cs file directly, without a VPS package or RBE dependency.

The first attempt is simulated. Its dashed entry/stop/target lines remain separate from real executions and stop at its resolved M1 endpoint. The panel reports Sim #1 fill and outcome in a separate footer row; the expanded panel is 287 pixels high.

### Analyzer fill audit fields - 2026-10-12

Enable **Write detailed decision and trade log** before running Analyzer. Each run writes a separate `C:/Users/Administrator/Documents/NinjaTrader 8/VaultBreak/logs/VB<ownership-id>_<run-id>.csv`. Real fills are `ENTRY_FILL` and `EXIT_FILL`; join them by the stable `tradeId` (entry order name). `executionId` identifies each execution, including partial fills. Read `time` in the detail field for the actual execution callback timestamp in Central time; `central_time` is the latest processed source-bar timestamp and may differ. `price`, `quantity`, and `direction` describe each fill and the original trade direction. Entry `initialStop` and `initialTarget` are the executable bracket prices captured for that filled leg before later management; partial entry fills can have different brackets. Join exits to their entry fills instead of assuming a global close has one stop. Existing logs are unchanged; rerun to obtain these new fields. The synthetic first attempt is not a funded fill and does not appear in these fill events.

Verification: both complete strategy sources passed an isolated platform-API compile with zero errors/warnings using a temporary standard-factory adapter. The installed workspace cannot currently complete a full project compile because its project references include stale paths and the platform-generated Strategies/@Strategy.cs is missing. This isolated compile does not certify native chart rendering. Reload/chart visual verification remains required.

### Shared account and broker restriction checkpoint - 2026-10-22

Same-direction strategies may share an exact futures contract while preserving their own order identities and journals. Opposing exposure on the same account and exact contract is always blocked, including when the optional broader equity-product guard is off. An unknown sell/buy order is not considered protective solely because its quantity fits the account position. Known protective orders require covering net quantity, with OCO alternatives counted once.

A broker liquidation-only/low-net-liquidating-value rejection latches execution, preserves accepted protection and avoids a guessed close/replacement retry. The journal persists the restriction reason in an optional tagged trailing extension, `BrokerAccountRestriction.v1`. Existing v1 checkpoints without this extension remain readable; unrestricted checkpoints keep their existing binary format. Restricted checkpoints require this updated reader: older builds refuse their extra data safely. Unknown, truncated, empty or trailing-corrupt restriction extensions refuse recovery. Restarting does not clear this broker restriction or establish that the account has been unlocked.

Focused proof: `../validation/test_shared_contract_option_b.py` executes the production guard, restriction branches and actual binary extension reader/writer. Broker/VPS runtime acceptance remains separate.
