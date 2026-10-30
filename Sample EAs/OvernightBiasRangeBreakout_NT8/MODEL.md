# Overnight Bias Range Breakout — NinjaTrader 8

## Cost-corrected V2 implementation — 2026-10-30

The production model now permits one strictly later same-side retry after the first attempt has fully exited through a losing protective stop. It never retries after a target, time/manual/recovery close, rejected entry, partial exit, or a direction change. The first bracket remains 0.30 ATR stop / 3R target with the existing completed-M1 trail. The retry uses 0.15 ATR stop / 3R target, then one cost-covered break-even move after a completed M1 has reached +0.5R; it has no later ratchet. Retry direction, eligibility time, target fill state and attempt count are persisted in the V2 recovery journal; V1 journals remain readable and cannot invent a retry.

Native chart history correction 2026-10-17: an existing recovery journal must not suppress historical strategy orders. The recovery pending/fault entry gate now applies only in realtime. Completed chart sessions execute through NT8's existing native M1 fill engine, supplying platform execution arrows and trade lines; these are simulated historical fills, not reconstructed broker fills. As in RBE, the incomplete tail session is excluded from chart order simulation until the loaded execution bars extend at least two minutes beyond the strategy cutoff, preventing historical exposure from entering live recovery. Analyzer behavior is unchanged. The existing realtime journal load, ownership checks and historical-flat transition refusal remain intact. Chart/VPS visual acceptance remains separate from compilation and gate fixtures.

Recovery lifecycle correction 2026-10-16: never add a replacement leg to an existing OCO group. Retain surviving protection and wait for missing/cancelling broker evidence. Restore a fully settled bracket stop-first with a fresh durable OCO intent only after owned fills reconcile. Live technical protective cancellation requests recovery rather than a forced early exit. A saved ProtectionCancelled state resumes only with consistent owned fills and no pending/partial/rejected close; genuine strategy exits retain precedence. Delayed equal-fill callbacks cannot regress terminal/cancelling state. Manual closes lacking attributable execution evidence remain pending on shared accounts; account flatness does not fabricate an exit. Native compilation and fixtures are separate from VPS restart acceptance.

Recovery update 2026-10-16: temporary Account.CreateOrder IDs are replaced with provider IDs from the exact registered order callback before reconciliation. Legacy temporary-GUID protective bracket IDs may bind only to one matching acknowledged broker order, with exact account/contract/owner/OCO, direction, type, quantity, prices and fill bounds. The original journal is backed up as .before_provider_id_<UTC ticks>; success logs RECOVERY_PROVIDER_ID_BOUND. Binding sends no broker orders. Assigned-ID conflicts remain blocked. Source fixtures and native compilation do not establish VPS runtime acceptance. The supplied October 16 native audit confirms opening 30024.25 versus overnight low 30023.50, a valid SHORT bias; 29853.50 is the later opening-range low.

Current runtime recovery (2026-10-15): [RECOVERY.md](RECOVERY.md) specifies automatic mismatch detection, retry on delayed owned broker evidence, and the case-specific external-close confirmation fallback. This supersedes older restart/ownership limitations below. Trading model parameters are unchanged.

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
| Limits | At most two funded attempts/day. Attempt two must be the same direction and a strictly later fresh signal after a losing protective-stop exit. No synthetic first trade or VWAP. |
| Time exit | 14:30 CT normally, 11:55 under the preserved special-day policy shared with Vault. |

The special-day policy retains the explicit 2020–2025 source dates, including January 9, 2025. From 2026: observed New Year, third-Monday January/February holidays, Good Friday, last Monday in May, observed June 19, July 3 and observed July 4, first Monday in September, fourth Thursday in November and following day, December 24, observed December 25, and December 31. This is a fixed conservative model policy, not a verified exchange-hours calendar.

NT8 uses native NQ/MNQ source data; NNQ uses its own adequate history, with same-expiry NQ fallback when required. Orders and costs stay on the attached contract. MT5 uses its attached Nasdaq symbol. ADX is fixed M15, not an ADX recalculated on the viewed chart period.

Cash risk includes the configured cost reserve; fixed quantity replaces the cash cap and defaults off. Stop protection, broker constraints, ownership and connection checks still apply. MT5 defaults: cash 100, fixed lots 1, commission fallback 7/lot, slippage 3 broker points each way. NT8 defaults: cash $250, fixed quantity 1, commission fallback $4/contract, slippage 3 × 0.01 price each way. Native Analyzer costs and explicit modeled-net costs are separate reports.

The user's current OBRB comparison is satisfactory. No rules are changed to chase the author's trade count. Matching-feed execution parity, cost-adjusted results and forward/live readiness remain distinct from that comparison.
Independent faithful port of the current MT5 candidate. One self-contained strategy source; no loose indicator, helper, configuration or data file is a runtime dependency. Install through NinjaTrader's native NinjaScript import/compile/export process, not by treating a source file as an executable.

Supported native instrument names: NQ, MNQ and NNQ. On NNQ, both NNQ and same-expiry NQ ETH M1 histories are requested over the selected chart/Analyzer range, without a fixed bar-count cap. NNQ is preferred when loaded history satisfies ATR warmup and exact overnight/opening-range coverage; NQ is the fallback otherwise. Source is fixed for the loaded run and reconsidered on reload. Bias/range/ATR/ADX and confirmations share the selection, including hosted ADX. Every order and completed-M1 trail uses NNQ. Sizing, rounding and commission scope use NNQ. The panel identifies both contracts. NQ/MNQ behaviour is retained. Native loading and NNQ result parity remain separate gates.

**Block opposing index exposure** is a user option, default **On**. When enabled, entry rejects opposing Equity Index positions/pending orders across visible NT8 accounts and coordinates pending direction with Vault. Turning it off bypasses that guard; exact-instrument ownership isolation remains active. No foreign exposure is closed. Other PCs and external order races are outside its scope.

Opening-range visuals form from the first completed minute after08:30 Central and finalise at08:45, only after day readiness and a nonzero08:30 overnight bias. Neutral middle-third opens hide these lines. Overnight boundaries and thirds remain visible. Lines anchor at08:30 and end at cutoff; missing range bars are never invented. This supersedes the prior independent-of-bias visual behaviour; entry rules are unchanged.

## Fixed model

M1 loading and recovery use an explicit ETH source, completed native-index catchup and provider repair for required live gaps/windows. Repair preserves entry count/owned brackets and suppresses old signals. Hosted ADX publishes the corrected strategy calculation. See [current validation status](validation/README.md). The one-second timer also reads available completed source bars when callbacks are queued. Use an ETH primary chart for the supported full-session live configuration; unavailable underlying/provider bars remain blocked. Native chart/reconnect observation remains required.
Completed M1 bars supply every price window and trailing decision, independent of the viewed chart timeframe. NinjaTrader minute bar timestamps label closes; conversion to Central Standard Time uses Windows DST rules and the configured NinjaTrader platform timezone. M15 indicator candles aggregate available M1 bars as MT5 does; missing minutes do not invent bars or reset Wilder state. Only the overnight and opening-range windows require exact minute continuity.
Startup, panel and no-tick cutoff clocks use NinjaTrader's Playback connection clock when present, otherwise its native platform clock. Playback is not compared to the PC's present-day wall clock.
- Overnight window: previous 23:00 through current 08:30 Central; exact consecutive minutes required.
- 08:30 opening price strictly inside the overnight range selects long above its upper third, short below its lower third; middle third and boundaries abstain.
- Opening range: 08:30–08:45. Completed M15 closes must break its directed boundary with MT5-style Wilder ADX(15) strictly above 20.
- Confirming close strictly after 09:00; first eligible close is 09:15 Central.
- One broker trade per day. Cutoff 14:30, or 11:55 on the same explicitly preserved MT5 special-day policy.
- Initial stop: 30% of the mean of fifteen completed daily ATR(10) values. Daily candle boundaries are Central 16:00 (NY 17:00), not NinjaTrader session-template Daily bars. ATR is rolling SMA of true ranges, not NinjaTrader Wilder ATR.
- Target: three ATR-derived stop distances from the submission quote, rounded to exchange ticks and retained after execution, matching MT5. Original trail R uses actual fill to original stop. Historical dollar-risk quantity is estimated from the completed signal close; stop/target geometry is established at the native next-open first execution, matching MT5's next-quote geometry. Realtime stop/target geometry retains the submission quote. In dollar-risk mode, a gap that takes actual planned risk over budget triggers owned-only closing.
- Completed M1 trail: activate at 1.5R; best close follows 1.5R behind in 0.5R increments. Original R is actual average entry to original rounded stop. Never loosen.
- The active stop is the more profitable of the original R-step stop and a cost-covered floor, rounded toward profit. Live fills embed entry slippage, so the floor covers round-trip fees plus one future exit slippage allowance. Historical native fills simulate neither slippage side, so their floor covers both modeled allowances. This keeps a historical cost-covered exit nonnegative after the modeled-net deductions. Current live entry charges can raise the reserve above a previous trade's rate.
- No VWAP. Fixed M15 ADX, bounded range/third lines and clock panel.

## Options
| Section | Option | Default | Meaning |
|---|---|---:|---|
| Trading | Enable new trades | On | Blocks entries when off; existing owned protection/exits continue. |
| Risk | Risk including costs | $250 | Planned initial loss plus modeled round-trip costs. |
| Risk | Use a fixed number of contracts | Off | Fixed quantity replaces the dollar risk budget when enabled. |
| Risk | Fixed contract quantity | 1 | Positive whole contracts per entry, used only in fixed-size mode. |
| Costs | RT commission fallback / contract | $4 | ORB_NT default; latest positive completed live NT execution charges replace it. |
| Costs | Slippage each way in points | 3 | One point is 0.01 price on supported NQ/MNQ, so the default allowance is 0.03 price each way, matching MT5. Exchange order rounding still uses native ticks. |
| Display | Levels/information | On | Strategy price levels, clock panel and one automatically attached ADX pane. Execution markers remain native NinjaTrader. |
| Logging | Detailed logs | Off | Writes model-local decision/trade CSV when enabled. |

### Analyzer fill audit fields - 2026-10-12

Enable **Write detailed decision and trade log** before running Analyzer. Each run writes a separate `C:/Users/Administrator/Documents/NinjaTrader 8/OvernightBiasRangeBreakout/logs/OBR_<run-id>.csv`. Real fills are `ENTRY` and `EXIT`; join them by the stable `tradeId` (entry order name). `executionId` distinguishes partial executions. The row timestamp and detail `time` are execution callback times in Central time. `price`, `qty`/`quantity`, and `direction` describe each fill; exit direction remains the original trade direction, not the closing order side. `initialStop` is the executable initial bracket stop captured at entry, never the later breakeven/trailing stop; `initialTarget` is recorded on entry. Repeated exit `initialStop` preserves that original value. Existing logs are unchanged; rerun to obtain these new fields.

NQ and MNQ have different point values. In the default dollar-risk mode, quantity rounds down to whole contracts; if one contract exceeds the budget, skip. Fixed-size mode instead submits the specified contract count independently of the dollar budget and disables actual-fill dollar-budget rejection. Bracket validity, protective orders, ownership checks and execution-failure handling remain active. The panel shows fixed contracts when enabled and dollar risk otherwise. Native NinjaTrader Slippage is zero: commission and two-sided slippage remain included in the explicit modeled-net report. In dollar-risk mode, these costs are also reserved in sizing. Native execution commission is reported separately; it is not subtracted twice. Configure the account commission template when assessing native Analyzer reports.

Commission gathering is keyed by execution ID: duplicate callbacks update fee references without replaying fill quantity. Only fully closed, volume-matched live trades on accounts outside Sim/Playback/Replay/Backtest contribute. Finite absolute Commission and Fee are included; retained execution references allow later native fee updates to change the next sizing and BE estimate. The latest positive completed-trade rate is used, scoped by account and full contract, retained across chart timeframe reloads within the same NT process. A zero native fee has no availability flag, so it does not replace the fallback. Restarting NinjaTrader resets this in-memory cache; fallback applies until another completed live trade. Native charges may come from commission templates and are not independently verified broker statements. No external file is required.

In dollar-risk mode, the actual-fill check also reserves the greater of planned round-trip commission and twice the observed live entry charge per contract, plus the fixed two-way slippage allowance. Partial-entry quantities are weighted correctly. Duplicate or delayed fee updates refresh this owned-position check without replaying fill quantity; the existing strategy timer also rechecks retained native execution references. Fixed-size mode bypasses the dollar-budget check.

## ADX chart pane and timeframe changes

With display enabled, the strategy automatically attaches **Overnight Bias ADX** during `State.DataLoaded`, using the same native cached-indicator and `AddChartIndicator` pattern as WayWard_NT. No manual Indicators step or loose indicator file is required. It creates one green histogram pane with the fixed threshold at 20. The period is fixed at 15, and completed M1 bars are aggregated into fixed M15 candles using the strategy's exact candle and Wilder calculation types. Changing the viewed chart timeframe rebuilds this source history and changes only how values are displayed. Keep the loaded historical date range and trading-hours template consistent when comparing values.

The hosted pane follows the strategy lifecycle: it is attached again automatically when the strategy loads, and removed when the strategy terminates. If a timeframe change disables the strategy, normal strategy re-enabling restores the pane without manual indicator attachment. The strategy has no separate ADX plot, so its automatic display creates only one pane. Its trading filter still calculates the same fixed M15 ADX internally when display is off. Remove any separate manual copy added under the previous setup instructions to avoid a duplicate.

## Execution ownership
Chart and Analyzer history both execute simulated historical orders. Execution arrows and performance connecting lines belong exclusively to native NinjaTrader; this strategy draws no custom fill arrows or trade connectors. Historical orders are not broker orders. An open historical position or unsettled historical order blocks transition to realtime; it is never adopted or submitted to the account. Startup establishes a fresh completed-source floor and never sends a historical signal at attachment. Existing account exposure on this instrument blocks new entries; foreign account positions are never flattened.

The panel matches RBE exactly: TopRight, Consolas 11, LightSteelBlue text, DarkSlateGray outline, Black background, opacity 85. Overnight starts at 23:00 Central = 00:00 New York; its opening range starts at 08:30 Central = 09:30 New York. These differ intentionally from Vault's midnight-Central anchor. No session rules were shifted by the clock audit.
Vault and Overnight also share one process-local exposure lease per account/instrument. Acquisition is atomic before a live entry submission; it remains held while an entry is pending or owned exposure/protection is unsettled, preventing simultaneous netting races. Only its owner can release it after flat/settled completion. Removing an instance with exposure leaves the lease fail-closed; there is no automatic adoption of that old position. Other strategies do not participate in this protocol: use a separate account when trading unrelated strategies on the same instrument. A separate duplicate-instance guard prevents two Overnight instances on the same account/instrument.
Each partial entry fill receives its own unmanaged OCO stop/target pair. Rejection suspends entries and closes only the strategy's exact executed remainder. Timed closing waits for cancellation acknowledgements and processed executions to avoid closing a stale quantity. A realtime timer handles cutoff with no new ticks while the terminal/connection remain operational. Broker-side OCO capability depends on the actual connection provider. Restart with exposure is not an automatic adoption mechanism: retain existing broker protection and do not enable another instance over open exposure.
If an exit only closes part of the owned trade, the remaining quantity is conservatively closed after order cancellation and execution reconciliation. This avoids leaving a remainder unprotected when a provider cancels an entire OCO sibling after a partial fill; it may differ from an uninterrupted ideal bracket in that uncommon execution case.

## Evidence limits
Native compilation and model-function contract tests are separate from Analyzer/Playback execution proof and feed parity. Earlier frozen counts predate the after-09:00 timing and cost-covered floor corrections. The user is now comparing native results through September 24, 2026; those retrospective comparisons are permitted and must retain their exact build, instrument, rollover, session and cost settings. Missing overnight or opening-range M1 windows fail closed. USTEC versus NQ is corroboration, not matching-feed parity. No live-readiness claim is made.


## Ownership and history loading

Execution identity, point value, tick rounding and the BE tick allowance always use the primary order contract, independently of NQ fallback callbacks. Commission/exposure keys and the panel use that contract. Separate signal/order labels are shown only when those sources differ.

The hosted ADX plot keeps timestamped strategy values and shows only the most recent value available at each chart bar's end. It never paints earlier bars with the final run value. Chart-period reloads retain fixed M1-to-M15 calculation; NNQ source selection follows the same native/fallback contract as the strategy. Provider-history repairs rebuild this display history without changing the entry-filter calculation or submitting old signals.

Ownership is internal and fixed per model. Run one instance of each model per account/instrument; both models retain separate identities and foreign-order isolation. ETH M1 requests cover the selected chart/Analyzer dates without a fixed bar-count cap. Load at least 45 calendar days before a comparison period for ATR warmup; sparse data may need more. The active tests are in tests/, supplied reports and the current verdict are in validation/, and superseded repair evidence is archived outside bin/Custom. This revision changes data loading and the public input surface, not model trading rules. No new 2026 run was performed.


## RBE panel and connection handling — 2026-10-10

The panel retains RBE NT8 geometry, Segoe UI sizes, colors, top-right position, drag header and collapse button. The full NY/CT clock occupies one header row: NY: HH:MM:SS CT: HH:MM:SS. Trades share the sizing row. Panel strings use plain ASCII separators and collapse symbols to avoid encoding corruption during transfers. The headers are Vault Break and Overnight Bias Breakout. It is embedded in this strategy; no RBE installation is required. Overnight displays the eligible opening range, ADX/bias, trade count, owned position/stop, sizing and costs. The neutral-opening status is shortened to fit its row; display ADX is rounded to three decimals without changing calculations.

New strategy instances default to native ConnectionLossHandling.KeepRunning. Existing saved instances may retain their previous connection-loss setting; select Keep Running once or recreate the instance. This setting cannot undo an account auto-close/manual disable. While the account is disconnected, all owned submit/cancel/change requests are withheld. Due-close intent remains queued; cancellation and exact owned closure resume after reconnect. The reconnect time becomes the freshness floor, preventing old signals from being submitted. Historical simulation does not require a connected account.

Verification: the earlier build passed full Custom compilation and broker/source-recovery checks. The latest panel-only patch passed ASCII, single-clock, model-caption, ADX precision and conditional fallback-route source checks. A fresh full compile is blocked by stale project paths after folder renames and the missing native Strategies/@Strategy.cs file. Native compilation and actual chart rendering remain unconfirmed for this patch. No performance rerun or model-rule change was made.


## Execution visuals

Real historical and live fills use native NinjaTrader execution markers and trade lines, plus RBE/ORB execution boxes: grey stop-risk area, blue realized profit area and a bounded entry line. Completed boxes retain actual entry/exit times and weighted fill prices; partial exits remain open until fully resolved. Completed drawings are cached and retried when a chart attaches. No custom execution arrows are fabricated. All drawing helpers are embedded in this strategy file; copy the single .cs file directly, without a VPS package or RBE dependency.

Verification: both complete strategy sources passed an isolated platform-API compile with zero errors/warnings using a temporary standard-factory adapter. The installed workspace cannot currently complete a full project compile because its project references include stale paths and the platform-generated Strategies/@Strategy.cs is missing. This isolated compile does not certify native chart rendering. Reload/chart visual verification remains required.
