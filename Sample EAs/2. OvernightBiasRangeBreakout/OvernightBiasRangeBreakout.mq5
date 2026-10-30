// OvernightBiasRangeBreakout.mq5 - strategy information
#property copyright "Copyright 2026-2027, Osamwonyi Eric (@You_FoundEric)"
#property link "https://x.com/You_FoundEric"
#property version "1.00"
#property description "The 08:30 Central (09:30 New York) open within the overnight range selects upper/lower-third directional bias."
#property description "ADX-filtered breakout of the 08:30-08:45 CT range on a completed M15 candle closing after 09:00 CT; ATR risk, max two attempts daily (one same-side retry after a losing stop)."
#property description "Developed by Osamwonyi Eric (@You_FoundEric)."
#property description "Unauthorized redistribution prohibited."
#property strict

#include "StrategyEngine.mqh"
