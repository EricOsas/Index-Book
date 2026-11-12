# 9. FVG_Duo (MT5 Expert Advisor)

Autonomous institutional implementation of the FVG Duo (Opening Drive FVG + Trend FVG) strategy for MetaTrader 5 on Nasdaq futures (NQ, MNQ) and CFD equivalents (USTEC).

## Key Features

- **Broker-Idempotent New York Clock:** Utilizes the Sunday 18:00 NY weekly opening anchor pattern with automated US/EU DST phase-shift detection. Runs identically across any broker timezone (UTC+2, UTC+3, UTC+0, etc.) without manual server offset parameters.
- **Integrated Arbitration Engine:** Flawlessly resolves position conflicts between Opening Drive and Trend FVG (Merge, Flip, Post-stopout resumption).
- **Self-Contained Indicators:** Independent native calculation of EMA and ATR (SMA/RMA) on completed bars without external file or chart indicator dependencies.
- **Institutional On-Chart HUD:** Clean dark obsidian dashboard tracking current NY wall-clock time, model phase, active position status, and live session PnL.
- **Flexible Position Sizing:** Supports fixed lots/contracts (1 fixed contract for futures/CFDs), percentage of live equity risk, or fixed cash risk USD.

## Compilation

Compile directly via MetaEditor64 CLI:
```powershell
$metaeditor = "C:\Program Files\MetaTrader 5\MetaEditor64.exe"
$src = "C:\Users\Administrator\AppData\Roaming\MetaQuotes\Terminal\D0E8209F77C8CF37AD8BF550E51FF075\MQL5\Experts\4. Index Book\9. FVG_Duo\FVG_Duo.mq5"
$inc = "C:\Users\Administrator\AppData\Roaming\MetaQuotes\Terminal\D0E8209F77C8CF37AD8BF550E51FF075\MQL5"
$log = "C:\Users\Administrator\AppData\Roaming\MetaQuotes\Terminal\D0E8209F77C8CF37AD8BF550E51FF075\MQL5\Experts\4. Index Book\9. FVG_Duo\compile.log"

Start-Process -FilePath $metaeditor -ArgumentList "/compile:`"$src`" /inc:`"$inc`" /log:`"$log`"" -Wait
```
Result: 0 errors, 0 warnings.
