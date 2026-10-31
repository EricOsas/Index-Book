// FlashTriggerORB.mq5
#property copyright "Copyright 2026-2027, Osamwonyi Eric (@You_FoundEric)"
#property link      "https://x.com/You_FoundEric"
#property version   "1.00"
#property description "Flash Trigger ORB — M15 14:30 UTC target candle breakout, long only."
#property description "SL = target LOW. TP = confirming close + 3*(close - target LOW)."
#property description "M1 HIGH trail: activate +70 pts, trail best-high minus 25 pts."
#property description "CFD: no overnight forced flat. One entry per UTC date. No retry."
#property description "Developed by Osamwonyi Eric (@You_FoundEric). Unauthorized redistribution prohibited."
#property strict

#include "StrategyEngine.mqh"
