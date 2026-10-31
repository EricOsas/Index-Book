// SigmaBreakout.mq5
#property copyright "Copyright 2026-2027, Osamwonyi Eric (@You_FoundEric)"
#property link      "https://x.com/You_FoundEric"
#property version   "1.00"
#property description "Sigma N3/1.7 — M30 completed-bar band breakout, RTH 09:30-16:00 ET."
#property description "Upper/lower band from N prior sessions per slot x BandMult multiplier."
#property description "Fixed lot 1 default. Same-side retry after losing stop. M1 R-step trail."
#property description "Developed by Osamwonyi Eric (@You_FoundEric). Unauthorized redistribution prohibited."
#property strict

#include "StrategyEngine.mqh"
