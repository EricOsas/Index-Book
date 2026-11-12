//+------------------------------------------------------------------+
//|                                             StrategyEngine.mqh   |
//| FVG Duo Strategy Engine (Opening Drive + Trend FVG)              |
//| Developed by Osamwonyi Eric (@You_FoundEric)                     |
//| Copyright 2026-2027, Osamwonyi Eric (@You_FoundEric)             |
//+------------------------------------------------------------------+
#property strict
#property tester_no_cache

#include <Trade/Trade.mqh>

//--- Enums
enum ENUM_MODEL_PRESET
{
   PRESET_OPTIMAL_B3A,   // Optimal B3a (SMA/c2, Mult 1.25x, EMA 50/100, Stop 1.25%, Ret/DD 14.38)
   PRESET_RMA_C1,        // Parity RMA (RMA/c1, Mult 1.5x, EMA 50/100, Stop 1.5%, $440k Net)
   PRESET_VIDEO_B1,      // Video Base B1 (SMA/c2, Mult 1.5x, EMA 50/100, Stop 1.5%, $411k Net)
   PRESET_CUSTOM         // Custom (User settings below)
};

enum ENUM_ATR_METHOD
{
   ATR_METHOD_SMA,       // Simple Moving Average (SMA)
   ATR_METHOD_RMA        // Wilder's Smoothed Average (RMA)
};

enum ENUM_ATR_REF
{
   ATR_REF_C2,           // ATR window includes c2 (bar k-1)
   ATR_REF_C1            // ATR window ends at c1 (bar k-2)
};

enum ENUM_SIZING_MODE
{
   SIZING_FIXED,         // Fixed Contracts / Lots
   SIZING_PERCENT,       // Risk % of Live Equity
   SIZING_CASH           // Fixed Cash Risk (USD)
};

enum ENUM_MERGE_ANCHOR
{
   MERGE_TREND_ENTRY,    // Stop anchored to Trend entry price
   MERGE_OD_SIGNAL       // Stop anchored to OD signal bar open
};

//--- Inputs
input group "=== Model Presets ==="
input ENUM_MODEL_PRESET InpModelPreset         = PRESET_OPTIMAL_B3A; // Active Preset

input group "=== Strategy Parameters (Custom Mode) ==="
input bool              InpEnableOD            = true;               // Enable Opening Drive
input bool              InpEnableTrend         = true;               // Enable Trend FVG
input ENUM_ATR_METHOD   InpAtrMethod           = ATR_METHOD_SMA;     // ATR Calculation Method
input ENUM_ATR_REF      InpAtrRef              = ATR_REF_C2;         // ATR Reference Bar
input bool              InpFvgStrict           = true;               // Strict FVG (strict inequality)
input double            InpTrendAtrMult        = 1.25;               // Trend ATR Displacement Multiplier
input int               InpTrendEmaFast        = 50;                 // Trend EMA Fast Period
input int               InpTrendEmaSlow        = 100;                // Trend EMA Slow Period
input double            InpTrendRR             = 3.0;                // Trend Risk:Reward Ratio
input double            InpODStopPct           = 0.0125;             // Opening Drive Stop % (0.0125 = 1.25%)
input bool              InpTrendAfterODStopout = true;               // Allow Trend trades after OD stopout
input ENUM_MERGE_ANCHOR InpMergeAnchor         = MERGE_TREND_ENTRY;  // Merge Stop Anchor
input double            InpTickSize            = 0.25;               // Futures/CFD Tick Size

input group "=== Position Sizing & Risk ==="
input ENUM_SIZING_MODE  InpSizingMode          = SIZING_FIXED;       // Sizing Mode
input double            InpFixedLots           = 1.0;                // Fixed Contracts/Lots
input double            InpRiskPercent         = 1.0;                // Risk % per Trade
input double            InpCashRiskUSD         = 1000.0;             // Cash Risk USD per Trade

input group "=== Execution & Gateway ==="
input ulong             InpMagic               = 909150;             // EA Magic Number
input ulong             InpDeviation           = 10;                 // Max Slippage Points
input double            InpCommissionRTPerLot  = 4.28;               // Round-turn Commission per Contract

input group "=== Chart Visuals ==="
input bool              ShowVisuals            = true;               // Draw FVG Boxes and Markers
input bool              ShowDashboard          = true;               // Display Performance HUD

// Forward includes
#include "ExecutionRuntime.mqh"
#include "ChartVisuals.mqh"

//+------------------------------------------------------------------+
//| BROKER-IDEMPOTENT NEW YORK CLOCK ENGINE (OSC / RBE ANCHOR)       |
//+------------------------------------------------------------------+
const int CLOCK_UNKNOWN = 100000;
int       clockServerNY = 0;
bool      clockReady    = false;
datetime  clockObserved = 0;
datetime  clockWeeks[];
int       clockWeekOffsets[];

int VBWeekendOffset(datetime previous, datetime opening)
{
   int raw = (int)((long)opening % 86400) - 18 * 3600;
   int offset = (int)MathRound((double)raw / 900.0) * 900;
   for(int wrap = -1; wrap <= 1; wrap++)
   {
      int candidate = offset + wrap * 86400;
      if(candidate < -8 * 3600 || candidate > 19 * 3600) continue;
      MqlDateTime before, after;
      TimeToStruct(previous - candidate, before);
      TimeToStruct(opening - candidate, after);
      if(before.day_of_week == 5 && after.day_of_week == 0) return candidate;
   }
   return CLOCK_UNKNOWN;
}

// US Eastern DST: starts 02:00 EST on the second Sunday in March
// (07:00 UTC), and ends 02:00 EDT on the first Sunday in November
// (06:00 UTC). The input is a UTC timestamp, not broker/server time.
bool VBIsNewYorkDST(datetime utc)
{
   MqlDateTime parts;
   TimeToStruct(utc, parts);
   int year = parts.year;

   MqlDateTime dateParts;
   ZeroMemory(dateParts);
   dateParts.year = year;
   dateParts.mon = 3;
   dateParts.day = 1;
   datetime marchFirst = StructToTime(dateParts);
   TimeToStruct(marchFirst, dateParts);
   int daysToFirstSundayMarch = (7 - dateParts.day_of_week) % 7;
   datetime secondSundayMarch = marchFirst + (daysToFirstSundayMarch + 7) * 86400;
   datetime dstStartUtc = secondSundayMarch + 7 * 3600;

   ZeroMemory(dateParts);
   dateParts.year = year;
   dateParts.mon = 11;
   dateParts.day = 1;
   datetime novemberFirst = StructToTime(dateParts);
   TimeToStruct(novemberFirst, dateParts);
   int daysToFirstSundayNovember = (7 - dateParts.day_of_week) % 7;
   datetime firstSundayNovember = novemberFirst + daysToFirstSundayNovember * 86400;
   datetime dstEndUtc = firstSundayNovember + 6 * 3600;

   return (utc >= dstStartUtc && utc < dstEndUtc);
}

int VBWeekAnchorOffset(datetime stamp)
{
   MqlDateTime d;
   TimeToStruct(stamp, d);
   d.hour = 0; d.min = 0; d.sec = 0;
   datetime week = StructToTime(d) - d.day_of_week * 86400;
   for(int k = 0; k < ArraySize(clockWeeks); k++)
      if(clockWeeks[k] == week) return clockWeekOffsets[k];

   MqlRates hours[];
   ArraySetAsSeries(hours, false);
   int count = CopyRates(_Symbol, PERIOD_H1, stamp - 22 * 86400, stamp, hours);
   for(int i = count - 1; i > 0; i--)
   {
      if(hours[i].time - hours[i - 1].time < 40 * 3600) continue;
      MqlRates minutes[];
      ArraySetAsSeries(minutes, false);
      datetime last = MathMin(stamp, hours[i].time + 3599);
      if(CopyRates(_Symbol, PERIOD_M1, hours[i].time, last, minutes) <= 0) continue;
      datetime opening = minutes[0].time;
      int offset = VBWeekendOffset(hours[i - 1].time, opening);
      if(offset == CLOCK_UNKNOWN) continue;

      int n = ArraySize(clockWeeks);
      ArrayResize(clockWeeks, n + 1);
      ArrayResize(clockWeekOffsets, n + 1);
      clockWeeks[n] = week;
      clockWeekOffsets[n] = offset;
      return offset;
   }
   return CLOCK_UNKNOWN;
}

int VBServerNYOffsetSec(datetime stamp)
{
   // The Strategy Tester weekend anchor already returns server-time minus
   // New York-time. Do not interpret it as a broker UTC offset.
   if(MQLInfoInteger(MQL_TESTER))
      return VBWeekAnchorOffset(stamp);

   datetime utcNow = TimeGMT();
   datetime serverNow = TimeTradeServer();
   if(serverNow <= 0 || utcNow <= 0) return CLOCK_UNKNOWN;

   int raw = (int)(serverNow - utcNow);
   int brokerUtcOffsetSec = (int)MathRound((double)raw / 900.0) * 900;
   if(MathAbs(raw - brokerUtcOffsetSec) > 120 ||
      brokerUtcOffsetSec < -12 * 3600 || brokerUtcOffsetSec > 14 * 3600)
      return CLOCK_UNKNOWN;

   // Historical timestamps cannot safely reuse today's live offset. The
   // weekend anchor returns the final server-to-New-York offset directly.
   if(MathAbs((double)(stamp - serverNow)) > 7 * 86400)
      return VBWeekAnchorOffset(stamp);

   // Convert this broker timestamp to UTC, then use the exact New York
   // offset (-04:00 during US DST, -05:00 otherwise). Broker DST may differ.
   datetime utcStamp = stamp - brokerUtcOffsetSec;
   int newYorkUtcOffsetSec = VBIsNewYorkDST(utcStamp) ? -4 * 3600 : -5 * 3600;
   return brokerUtcOffsetSec - newYorkUtcOffsetSec;
}

datetime NYTime(datetime serverTime)
{
   int offset = VBServerNYOffsetSec(serverTime);
   if(offset == CLOCK_UNKNOWN) return 0;
   return serverTime - offset;
}

//+------------------------------------------------------------------+
//| INDICATOR CALCULATIONS ON M15 COMPLETED BARS                     |
//+------------------------------------------------------------------+
double ComputeEMA(const double &prices[], int period, int count)
{
   if(count < period) return 0.0;
   double alpha = 2.0 / (period + 1.0);
   double ema = prices[0];
   for(int i = 1; i < count; i++)
      ema = prices[i] * alpha + ema * (1.0 - alpha);
   return ema;
}

// rates[] is series-oriented: shift 0 is newest. Calculate ATR in
// chronological order and stop at the requested completed-bar shift.
double ComputeATRAtShift(const MqlRates &rates[], int period,
                         ENUM_ATR_METHOD method, int count, int targetShift)
{
   if(count <= period || targetShift < 0 || targetShift >= count ||
      count - targetShift < period)
      return 0.0;

   if(method == ATR_METHOD_RMA)
   {
      double seed = 0.0;
      int seen = 0;
      double atr = 0.0;

      // Oldest -> newest, ending at targetShift. This matches the Python
      // oracle: first ATR is the mean of the first 'period' true ranges.
      for(int shift = count - 1; shift >= targetShift; shift--)
      {
         double tr;
         if(shift == count - 1)
            tr = rates[shift].high - rates[shift].low;
         else
         {
            double hl = rates[shift].high - rates[shift].low;
            double hc = MathAbs(rates[shift].high - rates[shift + 1].close);
            double lc = MathAbs(rates[shift].low  - rates[shift + 1].close);
            tr = MathMax(hl, MathMax(hc, lc));
         }

         if(seen < period)
         {
            seed += tr;
            seen++;
            if(seen == period) atr = seed / period;
         }
         else
            atr = (atr * (period - 1) + tr) / period;
      }
      return (seen == period) ? atr : 0.0;
   }

   // SMA ATR at targetShift: the period true ranges ending at that bar.
   double sum = 0.0;
   for(int shift = targetShift; shift < targetShift + period; shift++)
   {
      double tr;
      if(shift == count - 1)
         tr = rates[shift].high - rates[shift].low;
      else
      {
         double hl = rates[shift].high - rates[shift].low;
         double hc = MathAbs(rates[shift].high - rates[shift + 1].close);
         double lc = MathAbs(rates[shift].low  - rates[shift + 1].close);
         tr = MathMax(hl, MathMax(hc, lc));
      }
      sum += tr;
   }
   return sum / period;
}

//+------------------------------------------------------------------+
//| STATE VARIABLES                                                  |
//+------------------------------------------------------------------+
datetime g_lastM1BarTime       = 0;
datetime g_currentTradingDate  = 0;
bool     g_odCheck1Done        = false;
bool     g_odCheck2Done        = false;
bool     g_odFiredToday        = false;
datetime g_odStopoutBarClose   = 0;
double   g_todayPnL            = 0.0;
string   g_statusText          = "INITIALIZING";

// Active parameters resolved from preset
double   actTrendAtrMult;
int      actTrendEmaFast;
int      actTrendEmaSlow;
double   actODStopPct;
bool     actTrendAfterStopout;
ENUM_ATR_METHOD actAtrMethod;
ENUM_ATR_REF    actAtrRef;

void ResolvePresets()
{
   if(InpModelPreset == PRESET_OPTIMAL_B3A)
   {
      actTrendAtrMult      = 1.25;
      actTrendEmaFast      = 50;
      actTrendEmaSlow      = 100;
      actODStopPct         = 0.0125;
      actTrendAfterStopout = true;
      actAtrMethod         = ATR_METHOD_SMA;
      actAtrRef            = ATR_REF_C2;
   }
   else if(InpModelPreset == PRESET_RMA_C1)
   {
      actTrendAtrMult      = 1.5;
      actTrendEmaFast      = 50;
      actTrendEmaSlow      = 100;
      actODStopPct         = 0.015;
      actTrendAfterStopout = false;
      actAtrMethod         = ATR_METHOD_RMA;
      actAtrRef            = ATR_REF_C1;
   }
   else if(InpModelPreset == PRESET_VIDEO_B1)
   {
      actTrendAtrMult      = 1.5;
      actTrendEmaFast      = 50;
      actTrendEmaSlow      = 100;
      actODStopPct         = 0.015;
      actTrendAfterStopout = false;
      actAtrMethod         = ATR_METHOD_SMA;
      actAtrRef            = ATR_REF_C2;
   }
   else
   {
      actTrendAtrMult      = InpTrendAtrMult;
      actTrendEmaFast      = InpTrendEmaFast;
      actTrendEmaSlow      = InpTrendEmaSlow;
      actODStopPct         = InpODStopPct;
      actTrendAfterStopout = InpTrendAfterODStopout;
      actAtrMethod         = InpAtrMethod;
      actAtrRef            = InpAtrRef;
   }
}

//+------------------------------------------------------------------+
//| INITIALIZATION                                                   |
//+------------------------------------------------------------------+
int OnInit()
{
   ResolvePresets();
   ReconcileOwnedPosition();

   PrintFormat("[FVG_DUO] Initialized on %s. Preset=%s Mult=%.2f FastEMA=%d SlowEMA=%d ODStop=%.4f",
               _Symbol, EnumToString(InpModelPreset), actTrendAtrMult, actTrendEmaFast, actTrendEmaSlow, actODStopPct);
   return INIT_SUCCEEDED;
}

void OnDeinit(const int reason)
{
   RemoveVisuals();
}

//+------------------------------------------------------------------+
//| ON TICK / COMPLETED BAR PROCESSING                               |
//+------------------------------------------------------------------+
void OnTick()
{
   datetime m1Time = iTime(_Symbol, PERIOD_M1, 0);
   if(m1Time == 0) return;

   // Execute on new completed M1 bar
   bool isNewBar = (m1Time != g_lastM1BarTime);
   if(isNewBar)
   {
      g_lastM1BarTime = m1Time;
      OnBarCloseM1(m1Time);
   }
}

//+------------------------------------------------------------------+
//| EXECUTION ON COMPLETED 1-MINUTE BAR                              |
//+------------------------------------------------------------------+
void OnBarCloseM1(datetime currentM1BarOpen)
{
   datetime nyCurrent = NYTime(currentM1BarOpen);
   if(nyCurrent <= 0) return;

   MqlDateTime dtNY;
   TimeToStruct(nyCurrent, dtNY);
   datetime todayDate = nyCurrent - (dtNY.hour * 3600 + dtNY.min * 60 + dtNY.sec);

   // New trading day reset
   if(todayDate != g_currentTradingDate)
   {
      g_currentTradingDate = todayDate;
      g_odCheck1Done       = false;
      g_odCheck2Done       = false;
      g_odFiredToday       = false;
      g_odStopoutBarClose  = 0;
      g_todayPnL           = 0.0;
      g_statusText         = "PRE-MARKET";
   }

   int nyMinuteOfDay = dtNY.hour * 60 + dtNY.min;

   // 1. Session Close Flatten (16:00 NY = 960 minutes)
   if(nyMinuteOfDay >= 16 * 60)
   {
      if(ownedActive)
      {
         ExecuteClose("TIME_1600");
         g_statusText = "SESSION CLOSED";
      }
      UpdateDashboard(g_statusText, TimeToString(nyCurrent, TIME_SECONDS), "FLAT", g_todayPnL);
      return;
   }

   // Maintain owned position state
   ReconcileOwnedPosition();

   // 2. OPENING DRIVE CHECK 1 (At 09:40 NY open = minute 580)
   if(InpEnableOD && !g_odCheck1Done && nyMinuteOfDay == 9 * 60 + 40)
   {
      g_odCheck1Done = true;
      // 10m candles: c1=09:10, c2=09:20, c3=09:30 (closing at 09:40)
      MqlRates r10[];
      ArraySetAsSeries(r10, true);
      int copied = CopyRates(_Symbol, PERIOD_M10, currentM1BarOpen, 4, r10);
      if(copied >= 4)
      {
         // r10[1] is c3 (09:30-09:40), r10[2] is c2 (09:20-09:30), r10[3] is c1 (09:10-09:20)
         double c1High = r10[3].high, c1Low = r10[3].low;
         double c3High = r10[1].high, c3Low = r10[1].low;

         bool bull = InpFvgStrict ? (c3Low > c1High) : (c3Low >= c1High);
         bool bear = InpFvgStrict ? (c3High < c1Low) : (c3High <= c1Low);

         if(bull || bear)
         {
            g_odFiredToday = true;
            g_statusText = "OD CHECK 1 FIRED";
            HandleODSignal(bull ? 1 : -1, currentM1BarOpen);
         }
      }
   }

   // 3. OPENING DRIVE CHECK 2 (At 09:50 NY open = minute 590, only if Check 1 didn't fire)
   if(InpEnableOD && !g_odFiredToday && !g_odCheck2Done && nyMinuteOfDay == 9 * 60 + 50)
   {
      g_odCheck2Done = true;
      MqlRates r10[];
      ArraySetAsSeries(r10, true);
      int copied = CopyRates(_Symbol, PERIOD_M10, currentM1BarOpen, 4, r10);
      if(copied >= 4)
      {
         // r10[1] is c3 (09:40-09:50), r10[2] is c2 (09:30-09:40), r10[3] is c1 (09:20-09:30)
         double c1High = r10[3].high, c1Low = r10[3].low;
         double c3High = r10[1].high, c3Low = r10[1].low;

         bool bull = InpFvgStrict ? (c3Low > c1High) : (c3Low >= c1High);
         bool bear = InpFvgStrict ? (c3High < c1Low) : (c3High <= c1Low);

         if(bull || bear)
         {
            g_odFiredToday = true;
            g_statusText = "OD CHECK 2 FIRED";
            HandleODSignal(bull ? 1 : -1, currentM1BarOpen);
         }
      }
   }

   // 4. TREND FVG (On completed 15m bars, between 09:30 and 15:30 NY)
   if(InpEnableTrend && (dtNY.min % 15 == 0) && nyMinuteOfDay >= 9 * 60 + 30 && nyMinuteOfDay <= 15 * 60 + 30)
   {
      // Check if position blocked
      bool canTradeTrend = !ownedActive;
      if(g_odFiredToday && !canTradeTrend)
      {
         // If OD stopped out and flag is enabled
         if(actTrendAfterStopout && g_odStopoutBarClose > 0 && currentM1BarOpen >= g_odStopoutBarClose && !ownedActive)
            canTradeTrend = true;
      }
      else if(!g_odFiredToday && !ownedActive)
      {
         canTradeTrend = true;
      }

      if(canTradeTrend)
      {
         CheckAndExecuteTrendFVG(currentM1BarOpen);
      }
   }

   // Update HUD Dashboard
   string posText = "FLAT";
   if(ownedActive)
      posText = StringFormat("%s %s @ %.2f (SL: %.2f)", ownedSource, (ownedSide > 0 ? "LONG" : "SHORT"), ownedEntryPrice, ownedStopPrice);

   UpdateDashboard(g_statusText, TimeToString(nyCurrent, TIME_SECONDS), posText, g_todayPnL);
}

//+------------------------------------------------------------------+
//| HANDLE OPENING DRIVE SIGNAL (MERGE / FLIP / ENTRY)               |
//+------------------------------------------------------------------+
void HandleODSignal(int odSide, datetime barOpenTime)
{
   double currentPrice = (odSide > 0) ? SymbolInfoDouble(_Symbol, SYMBOL_ASK) : SymbolInfoDouble(_Symbol, SYMBOL_BID);
   double tick = InpTickSize;

   // Case 3c: MERGE — open Trend LONG conflicts with OD LONG
   if(ownedActive && ownedSide > 0 && odSide > 0)
   {
      // Anchor stop
      double anchorPrice = (InpMergeAnchor == MERGE_TREND_ENTRY) ? ownedEntryPrice : currentPrice;
      double rawStop = anchorPrice * (1.0 - actODStopPct);
      double newSL = MathFloor(rawStop / tick) * tick;
      ExecuteModify(newSL, 0.0, "MERGED");
      g_statusText = "MERGED TO OD RULES";
      return;
   }

   // Case 3d: FLIP — open Trend LONG conflicts with OD SHORT
   if(ownedActive && ownedSide > 0 && odSide < 0)
   {
      ExecuteClose("FLIP_TO_SHORT");
      double rawStop = currentPrice * (1.0 + actODStopPct);
      double sl = MathCeil(rawStop / tick) * tick;
      ExecuteEntry(-1, currentPrice, sl, 0.0, "OD");
      g_statusText = "FLIPPED TO OD SHORT";
      return;
   }

   // Case 3b: Flat -> enter OD trade
   if(!ownedActive)
   {
      if(odSide > 0)
      {
         double rawStop = currentPrice * (1.0 - actODStopPct);
         double sl = MathFloor(rawStop / tick) * tick;
         ExecuteEntry(1, currentPrice, sl, 0.0, "OD");
         g_statusText = "OD LONG ENTERED";
      }
      else
      {
         double rawStop = currentPrice * (1.0 + actODStopPct);
         double sl = MathCeil(rawStop / tick) * tick;
         ExecuteEntry(-1, currentPrice, sl, 0.0, "OD");
         g_statusText = "OD SHORT ENTERED";
      }
   }
}

//+------------------------------------------------------------------+
//| CHECK AND EXECUTE TREND FVG ON COMPLETED M15 BAR                 |
//+------------------------------------------------------------------+
void CheckAndExecuteTrendFVG(datetime currentBarOpen)
{
   MqlRates r15[];
   ArraySetAsSeries(r15, true);
   // RMA is recursive from the first available bar in the oracle dataset.
   // Fetch the full available M15 history rather than reseeding on a short
   // rolling window, otherwise ATR drifts from the Python reference.
   int count = Bars(_Symbol, PERIOD_M15);
   if(count <= 0) return;
   int copied = CopyRates(_Symbol, PERIOD_M15, 0, count, r15);
   if(copied < count) return;

   // r15[1] is c3 (just closed), r15[2] is c2, r15[3] is c1
   // Check consecutive
   if(r15[2].time - r15[3].time != 15 * 60 || r15[1].time - r15[2].time != 15 * 60)
      return;

   // Rule b: Bullish FVG (c3.low > c1.high)
   double c1High = r15[3].high, c1Low = r15[3].low;
   double c3Low  = r15[1].low;
   bool bullFvg = InpFvgStrict ? (c3Low > c1High) : (c3Low >= c1High);
   if(!bullFvg) return;

   // Rule c: Displacement (c2 range >= mult * ATR)
   int atrShift = (actAtrRef == ATR_REF_C1) ? 3 : 2;
   double atr = ComputeATRAtShift(r15, 14, actAtrMethod, copied, atrShift);
   if(atr <= 0) return;
   double c2Range = r15[2].high - r15[2].low;
   if(c2Range < actTrendAtrMult * atr) return;

   // Rule d: Trend (EMA_fast > EMA_slow)
   double closes[];
   ArrayResize(closes, copied);
   for(int i = 0; i < copied; i++) closes[i] = r15[copied - 1 - i].close; // Oldest first
   double emaFast = ComputeEMA(closes, actTrendEmaFast, copied);
   double emaSlow = ComputeEMA(closes, actTrendEmaSlow, copied);
   if(emaFast <= emaSlow) return;

   // Geometry: Entry at current market, Stop at c1.low - 1 tick, Target = entry + 3R
   double tick = InpTickSize;
   double entryPrice = SymbolInfoDouble(_Symbol, SYMBOL_ASK);
   double stopPrice  = c1Low - tick;
   if(entryPrice <= stopPrice) return;

   double R = entryPrice - stopPrice;
   double targetPrice = entryPrice + InpTrendRR * R;

   // Execute Long
   ExecuteEntry(1, entryPrice, stopPrice, targetPrice, "TREND");
   g_statusText = "TREND LONG ENTERED";

   // Draw visual FVG box
   DrawFvgBox("FVG_" + TimeToString(r15[2].time), r15[3].time, c1High, r15[1].time + 15 * 60, c3Low, C'56,189,248');
}

//+------------------------------------------------------------------+
//| ON TESTER: DETAILED TRADE EXPORT & PARITY HARNESS                |
//+------------------------------------------------------------------+
double OnTester()
{
   if(!HistorySelect(0, TimeCurrent())) return 0;
   
   FolderCreate("FVG_Duo", FILE_COMMON);
   FolderCreate("FVG_Duo/logs", FILE_COMMON);
   int handle = FileOpen("FVG_Duo/logs/trades_mt5.csv", FILE_COMMON | FILE_WRITE | FILE_CSV | FILE_ANSI, ',');
   if(handle == INVALID_HANDLE)
   {
      Print("[FVG_DUO] Failed to open Common/Files/FVG_Duo/logs/trades_mt5.csv");
      return 0;
   }
   
   FileWrite(handle, "trade_id", "date", "source", "side", "entry_server", "exit_server", "entry_ny", "exit_ny", "entry_price", "exit_price", "points", "commission", "gross_usd", "net_usd", "comment");
   
   int totalDeals = HistoryDealsTotal();
   int tradeCount = 0;
   double totalNet = 0.0;
   
   for(int i = 0; i < totalDeals; i++)
   {
      ulong exitTicket = HistoryDealGetTicket(i);
      if(exitTicket <= 0) continue;
      if(HistoryDealGetInteger(exitTicket, DEAL_ENTRY) != DEAL_ENTRY_OUT) continue;
      if(HistoryDealGetInteger(exitTicket, DEAL_MAGIC) != (long)InpMagic) continue;
      
      ulong posId = (ulong)HistoryDealGetInteger(exitTicket, DEAL_POSITION_ID);
      
      ulong entryTicket = 0;
      for(int j = 0; j < i; j++)
      {
         ulong dtick = HistoryDealGetTicket(j);
         if(HistoryDealGetInteger(dtick, DEAL_POSITION_ID) == (long)posId &&
            HistoryDealGetInteger(dtick, DEAL_ENTRY) == DEAL_ENTRY_IN)
         {
            entryTicket = dtick;
            break;
         }
      }
      
      if(entryTicket == 0) continue;
      
      tradeCount++;
      long dealType = HistoryDealGetInteger(entryTicket, DEAL_TYPE);
      string side = (dealType == DEAL_TYPE_BUY) ? "LONG" : "SHORT";
      
      datetime entryServer = (datetime)HistoryDealGetInteger(entryTicket, DEAL_TIME);
      datetime exitServer  = (datetime)HistoryDealGetInteger(exitTicket, DEAL_TIME);
      datetime entryNY     = NYTime(entryServer);
      datetime exitNY      = NYTime(exitServer);
      
      double entryPrice = HistoryDealGetDouble(entryTicket, DEAL_PRICE);
      double exitPrice  = HistoryDealGetDouble(exitTicket, DEAL_PRICE);
      
      double points = (side == "LONG") ? (exitPrice - entryPrice) : (entryPrice - exitPrice);
      double profit = HistoryDealGetDouble(exitTicket, DEAL_PROFIT);
      double comm   = HistoryDealGetDouble(entryTicket, DEAL_COMMISSION) + HistoryDealGetDouble(exitTicket, DEAL_COMMISSION);
      double swap   = HistoryDealGetDouble(exitTicket, DEAL_SWAP);
      double net    = profit + comm + swap;
      totalNet += net;
      
      string comment = HistoryDealGetString(exitTicket, DEAL_COMMENT);
      string entryComment = HistoryDealGetString(entryTicket, DEAL_COMMENT);
      string src = "OD";
      if(StringFind(entryComment, "TREND") >= 0) src = "TREND";
      if(StringFind(entryComment, "MERGED") >= 0 || StringFind(comment, "MERGED") >= 0) src = "MERGED";
      
      MqlDateTime dt;
      TimeToStruct(entryNY, dt);
      string dateStr = StringFormat("%04d-%02d-%02d", dt.year, dt.mon, dt.day);
      
      FileWrite(handle, tradeCount, dateStr, src, side, 
                TimeToString(entryServer, TIME_DATE|TIME_SECONDS), TimeToString(exitServer, TIME_DATE|TIME_SECONDS),
                TimeToString(entryNY, TIME_DATE|TIME_SECONDS), TimeToString(exitNY, TIME_DATE|TIME_SECONDS),
                DoubleToString(entryPrice, 2), DoubleToString(exitPrice, 2),
                DoubleToString(points, 2), DoubleToString(comm, 2), DoubleToString(profit, 2), DoubleToString(net, 2),
                comment);
   }
   
   FileClose(handle);
   PrintFormat("[FVG_DUO] TESTER_COMPLETE: exported %d trades, Net P&L: %.2f USD", tradeCount, totalNet);
   return totalNet;
}
