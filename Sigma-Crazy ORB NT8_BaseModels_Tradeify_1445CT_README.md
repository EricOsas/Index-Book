# NT8 base-model patch: Tradeify 14:45 CT

Files:
- `SigmaBreakout.cs`
- `CrazyOrb.cs`

## Strategy Analyzer setup

Both strategies now require a **1-minute primary series**. Each adds its signal timeframe internally:
- Sigma Breakout: M30 signal series
- Crazy ORB: M15 signal series

Orders use the primary M1 series. This keeps entries, exits, and managed stop/target orders on the first Bars context and gives the Analyzer M1 OHLC execution granularity.

For Crazy ORB, select an **ETH trading-hours template** on the primary M1 series. The 09:15-09:30 ET opening range is pre-RTH. If the required bar is missing, the strategy logs a warning and does not trade.

## Time handling and cutoff

- All timestamps convert from NinjaTrader's configured platform timezone to Eastern Time. No PC-local `ToUniversalTime()` conversion remains.
- Tradeify deadline: 14:45 CT = 15:45 ET.
- Safety flatten: 14:44 CT = 15:44 ET, triggered on the M1 series. This leaves one minute before the deadline.
- Sigma excludes the M30 bar starting 15:30 ET because it would only close at 16:00 ET, after the cutoff. Its last eligible M30 signal closes at 15:30 ET and enters on the next M1 open.
- Crazy ORB builds the opening range from M15 bar-start timestamps. It blocks signals at/after 15:45 ET; the last eligible signal bar closes at 15:30 ET.

## Base-model defaults

Sigma Breakout defaults now match the uploaded RoboQuant preset: N=2, band multiplier=1.5, RTH-open reference, stop loss off, fixed quantity 1.

Crazy ORB defaults now match the uploaded RoboQuant run: 09:15-09:30 ET range, ATR(14), 1.0 ATR stop, 0.35R target, $2,001 risk, min size 1, max 2 trades, entry cutoff 15:45 ET. The Tradeify safety flatten is 15:44 ET rather than the original 15:55 ET.

## NQ / MNQ accounting

Both strategies validate the native instrument point value:
- NQ: $20 per point
- MNQ: $2 per point

Sigma uses fixed quantity, so identical trades at identical quantity produce 10x the NQ dollar P&L versus MNQ. Crazy ORB sizes by dollar risk, so NQ quantity will be lower; it is not intended to produce 10x the MNQ dollar P&L.

## Costs and validation limits

Commission is not hardcoded. Apply the correct NinjaTrader commission template in Strategy Analyzer. `Slippage` remains 0 by default; NinjaTrader's Analyzer slippage is tick-based, while RoboQuant's 0.005% is price-dependent, so it is not reproduced exactly by this patch.

Static checks passed for delimiter balance, defaults, timeframe routing, cutoff values, and native point-value checks. These files were **not compiled or run inside NinjaTrader 8** because the NT8 assemblies/runtime are not available in this environment. Compile in NT8 and run a short date-range smoke test before using them live.
