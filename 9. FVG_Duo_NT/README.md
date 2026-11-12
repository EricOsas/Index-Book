# 9. FVG_Duo_NT (NinjaTrader 8 Strategy)

Autonomous institutional implementation of the FVG Duo (Opening Drive FVG + Trend FVG) strategy for NinjaTrader 8 on Nasdaq futures (NQ, MNQ).

## Strategy Architecture

- **Multi-Timeframe Engine:** Uses 4 synchronized data series:
  - `BarsArray[0]`: 5-minute series (Chart Base & Entry Open Price)
  - `BarsArray[1]`: 1-minute series (Intraday execution & stop evaluation)
  - `BarsArray[2]`: 10-minute series (Opening Drive gap detection)
  - `BarsArray[3]`: 15-minute series (Trend FVG indicators: EMA 50, EMA 100, ATR 14)
- **Arbitration Engine:** Resolves position conflicts between Opening Drive and Trend FVG:
  - **Merge:** If Trend Long is active at 09:40/09:50 and OD Long fires $\rightarrow$ moves stop to percentage stop, removes target, holds to 16:00 close.
  - **Flip:** If Trend Long is active and OD Short fires $\rightarrow$ closes Long at bar open, enters Short at same open.
  - **Post-Stopout:** Resumes Trend FVG signals after an OD stopout if enabled.
- **Session Close:** Flattens all positions at 16:00 ET.

## Presets

- **Optimal B3a (Default / Recommended):** 1.25x ATR displacement, 50/100 EMA, 1.25% OD stop, `trend_after_od_stopout=true`. Generates **$442,089 net** with a reduced **$30,736 max drawdown** (Return/DD = 14.38).
- **Parity RMA C1:** 1.5x ATR displacement, 50/100 EMA, 1.5% OD stop. Matches video metrics within 1-2% across the October 2016 - October 2026 10-year window.
- **Video Base B1:** 1.5x ATR displacement, 50/100 EMA, 1.5% OD stop. Original baseline configuration from video.
