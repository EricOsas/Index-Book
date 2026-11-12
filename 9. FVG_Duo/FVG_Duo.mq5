//+------------------------------------------------------------------+
//|                                                   FVG_Duo.mq5    |
//| FVG Duo - Opening Drive FVG + Trend FVG Strategy                 |
//| Developed by Osamwonyi Eric (@You_FoundEric)                     |
//| Copyright 2026-2027, Osamwonyi Eric (@You_FoundEric)             |
//+------------------------------------------------------------------+
#property copyright "Copyright 2026-2027, Osamwonyi Eric (@You_FoundEric)"
#property link      "https://x.com/You_FoundEric"
#property version   "5.00"
#property description "FVG Duo - Opening Drive FVG + Trend FVG Strategy on Index Futures/CFDs (NQ, MNQ, USTEC)"
#property description "Opening Drive: 10m FVG at 09:40/09:50 NY, enters next M5 bar open, held to 16:00 NY close."
#property description "Trend FVG: 15m Trend (EMA 50/100) + Displacement (ATR 1.25x/1.5x), Long-only, 3R target."
#property description "Fully broker/timezone-idempotent NY clock engine with automated DST handling."
#property strict

#include "StrategyEngine.mqh"
