#property strict
#property tester_no_cache
#property version "1.00"
#property description "Sigma N3/1.7 M30 band breakout. RTH 09:30-16:00 ET. Same-side retry. M1 R-step trail."
#include <Trade/Trade.mqh>
#ifndef OBR_STRATEGY_CORE
#define OBR_STRATEGY_CORE
// Clock: live server/GMT detection; tester weekly-open anchor.
// No broker whitelist or fixed tester fallback.
const int CLOCK_UNKNOWN=100000;
int clockServerNY=0;
bool clockReady=false;
datetime clockObserved=0;
datetime clockWeeks[];
int clockWeekOffsets[];
int OBRWeekendOffset(datetime previous,datetime opening)
{
   int raw=(int)((long)opening%86400)-18*3600;
   int offset=(int)MathRound((double)raw/900.0)*900;
   for(int wrap=-1;wrap<=1;wrap++)
   {
      int candidate=offset+wrap*86400;
      if(candidate< -8*3600 || candidate>19*3600) continue;
      MqlDateTime before,after;
      TimeToStruct(previous-candidate,before);TimeToStruct(opening-candidate,after);
      if(before.day_of_week==5 && after.day_of_week==0) return candidate;
   }
   return CLOCK_UNKNOWN;
}
int OBRWeekAnchorOffset(datetime stamp)
{
   MqlDateTime d;TimeToStruct(stamp,d);d.hour=0;d.min=0;d.sec=0;
   datetime week=StructToTime(d)-d.day_of_week*86400;
   for(int k=0;k<ArraySize(clockWeeks);k++)
      if(clockWeeks[k]==week) return clockWeekOffsets[k];
   MqlRates hours[];ArraySetAsSeries(hours,false);
   int count=CopyRates(_Symbol,PERIOD_H1,stamp-22*86400,stamp,hours);
   for(int i=count-1;i>0;i--)
   {
      if(hours[i].time-hours[i-1].time<40*3600) continue;
      MqlRates minutes[];ArraySetAsSeries(minutes,false);
      datetime last=MathMin(stamp,hours[i].time+3599);
      if(CopyRates(_Symbol,PERIOD_M1,hours[i].time,last,minutes)<=0) continue;
      datetime opening=minutes[0].time;
      int offset=OBRWeekendOffset(hours[i-1].time,opening);
      if(offset==CLOCK_UNKNOWN) continue;
      if(hours[i].time<week
         && OBRExpectedBrokerUTC(opening-offset+5*3600)!=OBRExpectedBrokerUTC(stamp-offset+5*3600)) continue;
      int n=ArraySize(clockWeeks);ArrayResize(clockWeeks,n+1);ArrayResize(clockWeekOffsets,n+1);
      clockWeeks[n]=week;clockWeekOffsets[n]=offset;return offset;
   }
   return CLOCK_UNKNOWN;
}
int OBRServerNYOffsetSec(datetime stamp)
{
   if(MQLInfoInteger(MQL_TESTER)) return OBRWeekAnchorOffset(stamp);
   datetime utc=TimeGMT(),server=TimeTradeServer();
   if(server<=0 || utc<=0) return CLOCK_UNKNOWN;
   int raw=(int)(server-utc),offset=(int)MathRound((double)raw/900.0)*900;
   if(MathAbs(raw-offset)>120 || offset< -12*3600 || offset>14*3600) return CLOCK_UNKNOWN;
   if(MathAbs((double)(stamp-server))>7*86400)
      return OBRWeekAnchorOffset(stamp);
   return offset-(OBRExpectedBrokerUTC(stamp-offset)-7)*3600;
}
bool OBRClockRefresh(datetime stamp)
{
   if(clockReady && stamp>=clockObserved && stamp-clockObserved<60) return true;
   int offset=OBRServerNYOffsetSec(stamp);
   bool announce=!clockReady || offset!=clockServerNY;
   clockReady=offset!=CLOCK_UNKNOWN;
   if(!clockReady) return false;
   if(announce) PrintFormat("OBR_CLOCK_READY serverAheadOfNYMinutes=%d source=%s",offset/60,MQLInfoInteger(MQL_TESTER) ? "weekly opening anchor" : "live server/GMT");
   clockServerNY=offset;clockObserved=stamp;return true;
}
datetime OBRNewYorkAtOffset(datetime server,int offset){return server-offset;}
datetime OBRSessionAtOffset(datetime centralDay,int minute,int offset){return centralDay+offset+3600+minute*60;}
datetime OBRNewYork(datetime server)
{
   int offset=OBRServerNYOffsetSec(server);
   return offset==CLOCK_UNKNOWN ? 0 : OBRNewYorkAtOffset(server,offset);
}
datetime OBRNewYorkUTC(datetime utc){return utc+(OBRExpectedBrokerUTC(utc)-7)*3600;}
datetime OBRCentralUTC(datetime utc){return utc+(OBRExpectedBrokerUTC(utc-3600)-8)*3600;}
datetime OBRClockUTC(datetime server)
{
   if(!MQLInfoInteger(MQL_TESTER) && MathAbs((double)(server-TimeTradeServer()))<=7*86400)
   {
      int offset=(int)MathRound((double)(TimeTradeServer()-TimeGMT())/900.0)*900;
      return server-offset;
   }
   datetime ny=OBRNewYork(server);
   if(ny<=0) return 0;
   datetime utc=ny+5*3600;
   return ny-(OBRExpectedBrokerUTC(utc)-7)*3600;
}
datetime OBRCentral(datetime server){datetime utc=OBRClockUTC(server);return utc>0 ? OBRCentralUTC(utc) : 0;}
datetime OBRSessionTime(datetime centralDay,int minute=0)
{
   int offset=OBRServerNYOffsetSec(centralDay+minute*60+clockServerNY+3600);
   if(offset==CLOCK_UNKNOWN && clockReady && MathAbs((double)(centralDay-TimeCurrent()))<3*86400) offset=clockServerNY;
   if(offset==CLOCK_UNKNOWN) return 0;
   datetime result=OBRSessionAtOffset(centralDay,minute,offset);
   int resolved=OBRServerNYOffsetSec(result);
   return OBRSessionAtOffset(centralDay,minute,resolved==CLOCK_UNKNOWN ? offset : resolved);
}
datetime OBRDay(datetime server)
{
   MqlDateTime p; TimeToStruct(OBRNewYork(server),p);
   p.hour=0; p.min=0; p.sec=0;
   return StructToTime(p);
}
int OBRMinute(datetime server)
{
   MqlDateTime p; TimeToStruct(OBRNewYork(server),p);
   return p.hour*60+p.min;
}
bool OBRAllowed(datetime t) {return true;}
bool OBREligible(int minute,int cutoff) {return minute>=0 && minute<cutoff;}
datetime OBRCalendarDate(int y,int m,int d)
{
   MqlDateTime p={};p.year=y;p.mon=m;p.day=d;return StructToTime(p);
}
int OBRCalendarWeekday(datetime t){MqlDateTime p;TimeToStruct(t,p);return p.day_of_week;}
datetime OBRObserved(datetime t)
{
   int w=OBRCalendarWeekday(t);return t+(w==6 ? -86400 : w==0 ? 86400 : 0);
}
datetime OBRMonday(int y,int month,int n)
{
   datetime t=OBRCalendarDate(y,month,1);
   return t+((8-OBRCalendarWeekday(t))%7+7*(n-1))*86400;
}
bool OBRFutureSpecialDay(datetime d)
{
   MqlDateTime p;TimeToStruct(d,p);int y=p.year;
   datetime mayEnd=OBRCalendarDate(y,6,1)-86400;
   datetime memorial=mayEnd-((OBRCalendarWeekday(mayEnd)+6)%7)*86400;
   int a=y%19,b=y/100,c=y%100,e=b%4,f=(b+8)/25,g=(b-f+1)/3;
   int h=(19*a+b-b/4-g+15)%30,ii=c/4,k=c%4,l=(32+2*e+2*ii-h-k)%7;
   int mm=(a+11*h+22*l)/451,nn=h+l-7*mm+114;
   datetime friday=OBRCalendarDate(y,nn/31,nn%31+1)-2*86400;
   datetime nov=OBRCalendarDate(y,11,1);
   datetime thanks=nov+((11-OBRCalendarWeekday(nov))%7+21)*86400;
   return d==OBRObserved(OBRCalendarDate(y,1,1)) || d==OBRMonday(y,1,3)
      || d==OBRMonday(y,2,3) || d==friday || d==memorial
      || d==OBRObserved(OBRCalendarDate(y,6,19)) || d==OBRCalendarDate(y,7,3)
      || d==OBRObserved(OBRCalendarDate(y,7,4)) || d==OBRMonday(y,9,1)
      || d==thanks || d==thanks+86400 || d==OBRCalendarDate(y,12,24)
      || d==OBRObserved(OBRCalendarDate(y,12,25)) || d==OBRCalendarDate(y,12,31);
}
int OBRExpectedBrokerUTC(datetime utc)
{
   MqlDateTime p;TimeToStruct(utc,p);
   datetime march=OBRCalendarDate(p.year,3,1),nov=OBRCalendarDate(p.year,11,1);
   datetime start=march+((7-OBRCalendarWeekday(march))%7+7)*86400+7*3600;
   datetime finish=nov+(7-OBRCalendarWeekday(nov))%7*86400+6*3600;
   return utc>=start && utc<finish ? 3 : 2;
}
bool OBRCalendarSelfTest()
{
   return OBRNewYorkAtOffset(D'2022.01.24 08:00',7*3600)==D'2022.01.24 01:00'
      && OBRNewYorkAtOffset(D'2022.01.24 08:00',8*3600)==D'2022.01.24 00:00'
      && OBRNewYorkAtOffset(D'2022.07.12 08:00',7*3600)==D'2022.07.12 01:00'
      && OBRNewYorkAtOffset(D'2022.07.12 08:00',8*3600)==D'2022.07.12 00:00'
      && OBRSessionAtOffset(D'2022.01.24',510,7*3600)==D'2022.01.24 16:30'
      && OBRSessionAtOffset(D'2022.07.12',600,7*3600)==D'2022.07.12 18:00'
      && OBRSessionAtOffset(D'2022.07.12',870,7*3600)==D'2022.07.12 22:30'
      && OBRSessionAtOffset(D'2022.07.12',0,7*3600)==D'2022.07.12 08:00'
      && OBRFutureSpecialDay(D'2026.04.03') && OBRFutureSpecialDay(D'2027.03.26')
      && OBRFutureSpecialDay(D'2026.11.26') && OBRFutureSpecialDay(D'2026.11.27')
      && !OBRFutureSpecialDay(D'2026.11.25')
      && OBRExpectedBrokerUTC(D'2026.03.08 06:59:59')==2
      && OBRExpectedBrokerUTC(D'2026.03.08 07:00:00')==3
      && OBRExpectedBrokerUTC(D'2026.11.01 05:59:59')==3
      && OBRExpectedBrokerUTC(D'2026.11.01 06:00:00')==2
      && OBRNewYorkUTC(D'2026.03.08 06:59:59')==D'2026.03.08 01:59:59'
      && OBRNewYorkUTC(D'2026.03.08 07:00:00')==D'2026.03.08 03:00:00'
      && OBRCentralUTC(D'2026.03.08 07:59:59')==D'2026.03.08 01:59:59'
      && OBRCentralUTC(D'2026.03.08 08:00:00')==D'2026.03.08 03:00:00'
      && OBRNewYorkUTC(D'2026.11.01 06:00:00')==D'2026.11.01 01:00:00'
      && OBRCentralUTC(D'2026.11.01 07:00:00')==D'2026.11.01 01:00:00'
      && OBRWeekendOffset(D'2022.07.08 20:00',D'2022.07.10 22:05')==4*3600
      && OBRWeekendOffset(D'2022.01.22 11:00',D'2022.01.24 13:00')==19*3600;
}
// Sigma cutoff: 16:00 ET = 960 minutes CT. Special days cut off at 11:55 CT = 715 min.
// Note: NT8/Tradeify uses 15:44 ET (944 min) as a Tradeify platform rule; MQL5 uses true RTH close.
int OBRSessionCutoff(datetime server,int model)
{
   string dates=
      "|2020.01.01|2020.01.20|2020.02.17|2020.04.10|2020.05.25|2020.07.03|2020.09.07|2020.11.26|2020.11.27|2020.12.24|2020.12.25|2020.12.31"
      "|2021.01.01|2021.01.18|2021.02.15|2021.04.02|2021.05.31|2021.07.05|2021.09.06|2021.11.25|2021.11.26|2021.12.24|2021.12.31"
      "|2022.01.17|2022.02.21|2022.04.15|2022.05.30|2022.06.20|2022.07.04|2022.09.05|2022.11.24|2022.11.25|2022.12.24|2022.12.26|2022.12.31"
      "|2023.01.02|2023.01.16|2023.02.20|2023.04.07|2023.05.29|2023.06.19|2023.07.03|2023.07.04|2023.09.04|2023.11.23|2023.11.24|2023.12.24|2023.12.25|2023.12.31"
      "|2024.01.01|2024.01.15|2024.02.19|2024.03.29|2024.05.27|2024.06.19|2024.07.03|2024.07.04|2024.09.02|2024.11.28|2024.11.29|2024.12.24|2024.12.25|2024.12.31"
      "|2025.01.01|2025.01.09|2025.01.20|2025.02.17|2025.04.18|2025.05.26|2025.06.19|2025.07.03|2025.07.04|2025.09.01|2025.11.27|2025.11.28|2025.12.24|2025.12.25|2025.12.31|";
   if(StringFind(dates,"|"+TimeToString(OBRDay(server),TIME_DATE)+"|")>=0) return 715;
   if(OBRDay(server)>=D'2026.01.01' && OBRFutureSpecialDay(OBRDay(server))) return 715;
   return 944; // 15:44 ET safety cutoff (Tradeify / NT8 parity) — MQL5 Sigma model cutoff
}
bool OBRBracket(int side,double entry,double stop,double target)
{
   return side*(entry-stop)>0 && side*(target-entry)>0;
}
int OBRTradeCap(int model) {return 1;} // Sigma: 1 initial + 1 retry = cap 2 enforced via OBRMaxAttempts
// Closed M1 long-side path. A double touch resolves as stop first.
int OBRVirtualExit(double low,double high,double stop,double target)
{
   if(low<=stop) return -1;
   if(high>=target) return 1;
   return 0;
}
bool OBRCanEnter(int minute,int start,int cutoff,int count,int cap)
{
   return minute>=start && OBREligible(minute,cutoff) && count<cap;
}
bool OBRSignalCloseEligible(int closeMinute)
{
   // Sigma: M30 bars. First complete M30 closes at 10:00 ET = minute 600 CT.
   return closeMinute>=600;
}
bool OBRSignalTimeSelfTest()
{
   // Sigma M30: first complete M30 after 09:30 ET closes at 10:00 ET = 600 min CT.
   return !OBRSignalCloseEligible(570) && !OBRSignalCloseEligible(599)
      && OBRSignalCloseEligible(600) && OBRSignalCloseEligible(630);
}
// Sigma: close strictly above upper band = long; strictly below lower band = short.
// upper/lower are passed in from the band state variables.
int OBRSignalSide(int model,int direction,double close,double upper,double lower,double dummy)
{
   if(upper<=lower || upper<=0) return 0;
   if(close>upper) return 1;
   if(close<lower) return -1;
   return 0;
}
#endif
#ifndef OBR_STRATEGY_BAR_TRAIL
#define OBR_STRATEGY_BAR_TRAIL
bool OBRBarTrailLock(double peakR,double activateR,double distanceR,double stepR,double &lockR)
{
   if(activateR<=0 || distanceR<=0 || stepR<=0 || peakR<activateR) return false;
   lockR=activateR+MathFloor((peakR-activateR)/stepR+1e-9)*stepR-distanceR;
   return true;
}
bool OBRBarTrailSelfTest()
{
   double lock=0;
   if(OBRBarTrailLock(1.499,1.5,1.5,.5,lock)) return false;
   double peaks[]={1.5,1.999,2.0,2.499,2.5,3.0};
   double expected[]={0.0,0.0,.5,.5,1.0,1.5};
   for(int i=0;i<ArraySize(peaks);i++)
      if(!OBRBarTrailLock(peaks[i],1.5,1.5,.5,lock) || MathAbs(lock-expected[i])>1e-9) return false;
   return !OBRBarTrailLock(2.0,1.5,1.5,0,lock)
      && OBRVirtualExit(99,111,100,110)==-1
      && OBRVirtualExit(100,109,100,110)==-1
      && OBRVirtualExit(101,110,100,110)==1
      && OBRVirtualExit(101,109,100,110)==0;
}
#endif

enum OBRModel { VaultBreak=0, VWAPPullback=1, OvernightBiasRangeBreakout=2 };
enum OBRGeometry { SourceGeometry=0, ATRGeometry=1 };
enum OBRManagement { FixedBracket=0, VWAPClose=1, CostCoveredBE=2, ATRTrail=3, M1RStep=4 };
const OBRModel Model=(OBRModel)2;
#ifndef OBR_STRATEGY_PRODUCTION_INPUTS
#define OBR_STRATEGY_PRODUCTION_INPUTS

input group "==========  1 - Trading  =========="
input bool   EnableTrading             = true;   // Enable new trades (open trades stay protected when off)

input group " "
input group "==========  2 - Risk  =========="
input double SlPct                     = 0.0;    // Stop loss percentage (0 = no synthetic stop; flip/session exit)
input bool   UseFixedSize              = true;   // Use fixed lots (default ON for Sigma)
input double FixedLots                 = 1.0;    // Lots per trade when fixed sizing is enabled
input double RiskMoney                 = 250.0;  // Risk per trade in account currency (used when UseFixedSize=false)
input bool   SkipSetupsAboveRiskBudget = false;  // Skip entries where total initial risk > RiskBudgetUsd
input double RiskBudgetUsd             = 250.0;  // Cap (account currency); only active when SkipSetupsAboveRiskBudget=true

input group "  "
input group "==========  3 - Model Parameters  =========="
input int    N_Sigma                   = 3;      // Prior RTH sessions per slot for band calculation
input double BandMult                  = 1.7;    // Band multiplier applied to mean absolute deviation

input group "   "
input group "==========  4 - Trading Costs  =========="
input double CommissionPerLot          = 7.0;    // Fallback round-trip commission per lot
input int    SlippagePoints            = 3;      // Slippage per side in points

input group "    "
input group "==========  5 - Display  =========="
input bool   ShowVisuals               = true;   // Show chart levels, trade markers and information panel

input group "     "
input group "==========  6 - Execution & Logging  =========="
input bool   WriteTradeLog             = false;  // Write a detailed trade and decision log

// Frozen Sigma rules — not user-facing options.
const ulong  MagicNumber               = 91008061; // Fixed model identity; preserves owned-position recovery
const int    OBRMaxAttempts            = 2;         // 1 initial entry + 1 same-side retry
const double OBRRetryStopFraction      = 0.5;       // Retry stop = 50% of first-attempt original stop distance
const OBRManagement Management         = FixedBracket;
const double BarTrailActivateR         = 1.5;
const double BarTrailDistanceR         = 1.5;
const double BarTrailStepR             = 0.5;
const double BreakEvenR                = 0.5;
const string OBRDisplayName            = "Sigma Breakout N3/1.7";

// Private adapter names matching execution engine expectations.
const int    StartMinute               = 600;  // 10:00 ET — first complete M30 slot
const double RiskCash                  = RiskMoney;
const double CommissionRTPerLot        = CommissionPerLot;
const double SlipPrice                 = SlippagePoints*_Point;
const ulong  Magic                     = MagicNumber;
const bool   DetailedLog               = WriteTradeLog;
const OBRGeometry Geometry             = SourceGeometry;
const double TrailR                    = 1.0;
const double BarTrailStepR_            = BarTrailStepR; // alias used by engine
string RunId = "";
#endif

// ---- Compatibility stubs for ChartVisuals.mqh and ExecutionRuntime.mqh ----
// These OBRB globals are referenced by the shared modules; Sigma doesn't use them but declares stubs.
double orHigh=0,orLow=0,overnightHigh=0,overnightLow=0;
int    bias=0;
const int    ADXPeriod=15;         // Stub: no ADX in Sigma; required by ChartVisuals panel header
const int    OvernightStart=23;    // Stub: required by ChartVisuals panel
const int    VaultAttemptCap=3;    // Stub: required by ExecutionRuntime recovery path
const bool   SyntheticFirst=false; // Stub: required by ExecutionRuntime
const double TargetR=3.0;          // Stub: required by ExecutionRuntime trade export label
int    adxHandle=INVALID_HANDLE;   // Stub: ExecutionRuntime history-ready check references this
bool OBRSessionATRMean(datetime sessionDay,double &mean){mean=1.0;return true;} // Stub: always ready
// dayValid: Sigma tracks this as a local in OnTick; ChartVisuals needs a global copy.
bool dayValid=false;
int  sigmaFlipPendingSide=0;
// OBRBias stub: ChartVisuals calls OBRBias(open,low,high) — return 0 (no bias in Sigma)
int OBRBias(double open,double low,double high){return 0;}

const string OBROutputFamily = "SigmaBreakout";
const string OBROutputRoot   = OBROutputFamily+"/logs";
void OBRPrepareOutput()
{
   FolderCreate(OBROutputFamily,FILE_COMMON);
   FolderCreate(OBROutputRoot,FILE_COMMON);
}

CTrade trade;
int ledger=INVALID_HANDLE;
ENUM_TIMEFRAMES sourceTF=PERIOD_M30;
datetime lastMinute=0,lastSignal=0,day=0;
double initialRisk=0,modelCost=0,totalCost=0;
double modelCostPerLot=0,entryCommissionPerLot=0;
datetime ownedEntryTime=0;
int overnightExits=0;
string carryDetails="";
int entries=0,daysReady=0,daysRejected=0,invalidBrackets=0,failedOrders=0;
int firstSide=0;
bool retryArmed=false;
datetime retryEligibleAfter=0;
bool rangeReady=false,armed=false,beDone=false;
bool dayWaiting=false,dayRejected=false;
string dataWaitReason="";
bool virtualActive=false;
double virtualStop=0,virtualTarget=0;
datetime virtualStart=0;
datetime virtualLastBar=0;
int virtualStarts=0,virtualStops=0,virtualTargets=0,virtualCollisions=0;
int virtualExpired=0,virtualGaps=0;
double barPeakR=0;
double yearProfit[7],worstRisk=0,netWins=0,netLosses=0,adjustedPeak=0,adjustedDD=0;
int yearTrades[7];
int resultsFile=INVALID_HANDLE;
double dailyNet[2557];
string rejectedDetails="";

// ---- Sigma-specific band state ----
double rthOpen=0;           // RTH 09:30 ET opening price
bool rthOpenReady=false;

// Per-slot history: 13 slots (slot 0=09:30-10:00, slot 1=10:00-10:30, ..., slot 12=15:30-16:00)
// slotObservations[slot*MAX_SLOTS_N + k] = k-th prior abs(M30close - rthOpen) for that slot
// slotCounts[slot] = number of valid observations recorded for that slot
const int MAX_SLOT_IDX    = 13;
double slotObservations[];  // size MAX_SLOT_IDX * N_Sigma
int    slotCounts[];        // size MAX_SLOT_IDX

// Current live band (recomputed per completed M30 slot during session)
double sigmaUpperBand=0;
double sigmaLowerBand=0;
bool   sigmaBandReady=false;
int    sigmaCurrentSlot=-1; // which slot (0-based) the band is current for

#ifdef OBR_VISUAL_PROBE
bool gtPreviewRequested=false;
ulong gtPreviewRequestedMs=0;
#endif
#include "ChartVisuals.mqh"
#include "ExecutionRuntime.mqh"

bool OwnPosition()
{
   for(int i=PositionsTotal()-1;i>=0;i--)
   {
      ulong ticket=PositionGetTicket(i);
      if(ticket>0 && PositionGetString(POSITION_SYMBOL)==_Symbol && (ulong)PositionGetInteger(POSITION_MAGIC)==Magic)
         return true;
   }
   return false;
}
bool DateAllowed(datetime t) {return true;}
double RoundPrice(double price)
{
   double tick=SymbolInfoDouble(_Symbol,SYMBOL_TRADE_TICK_SIZE);
   return NormalizeDouble(MathRound(price/tick)*tick,_Digits);
}
void CloseOwned(string why)
{
   if(!MQLInfoInteger(MQL_TESTER) && !OBRRuntimeAccountContextValid()){OBRDraw();return;}
   if(!MQLInfoInteger(MQL_TESTER) && !TerminalInfoInteger(TERMINAL_CONNECTED)) return;
   for(int i=PositionsTotal()-1;i>=0;i--)
   {
      ulong ticket=PositionGetTicket(i);
      if(ticket>0 && PositionGetString(POSITION_SYMBOL)==_Symbol && (ulong)PositionGetInteger(POSITION_MAGIC)==Magic)
      {
         if(!RecoveryCloseBegin(OBRStateKey(),ticket))continue;
         bool closed=trade.PositionClose(ticket);uint ret=trade.ResultRetcode();
         RecoveryCloseResult(OBRStateKey(),ret,trade.ResultOrder());
         if(!closed || (ret!=TRADE_RETCODE_DONE && ret!=TRADE_RETCODE_DONE_PARTIAL))
         { failedOrders++; PrintFormat("OBR_CLOSE_REJECT %s ret=%u",why,trade.ResultRetcode()); }
      }
   }
}
void Note(string kind,string text)
{
   if(kind=="RECOVERY_BLOCK")Print("OBR RECOVERY ATTENTION: ",text);
   if(DetailedLog && ledger!=INVALID_HANDLE)
   {
      FileWrite(ledger,TimeToString(TimeCurrent(),TIME_DATE|TIME_SECONDS),kind,text);
      FileFlush(ledger);
   }
}
bool M1HistoryAvailable(datetime from)
{
   if(!MQLInfoInteger(MQL_TESTER) && !TerminalInfoInteger(TERMINAL_CONNECTED)) return false;
   long first=SeriesInfoInteger(_Symbol,PERIOD_M1,SERIES_FIRSTDATE);
   if(first<=0 || first>from || !SeriesInfoInteger(_Symbol,PERIOD_M1,SERIES_SYNCHRONIZED))
   {
      MqlRates preceding[];
      CopyRates(_Symbol,PERIOD_M1,from-86400,from,preceding);
      return false;
   }
   return SeriesInfoInteger(_Symbol,PERIOD_M1,SERIES_SYNCHRONIZED) && first>0 && first<=from;
}
bool NativeBarsValid(MqlRates &bars[],datetime from,datetime to)
{
   int n=ArraySize(bars);
   if(n<=0 || bars[0].time<from || bars[n-1].time>=to) return false;
   for(int i=0;i<n;i++)
   {
      if((i>0 && bars[i].time<=bars[i-1].time) || !MathIsValidNumber(bars[i].open)
         || !MathIsValidNumber(bars[i].high) || !MathIsValidNumber(bars[i].low)
         || !MathIsValidNumber(bars[i].close) || bars[i].low>bars[i].high
         || bars[i].open<bars[i].low || bars[i].open>bars[i].high
         || bars[i].close<bars[i].low || bars[i].close>bars[i].high) return false;
   }
   return true;
}
bool Window(datetime from,datetime to,MqlRates &bars[],int minimum)
{
   ArraySetAsSeries(bars,false);
   if(to<=from || !M1HistoryAvailable(from)) return false;
   int n=CopyRates(_Symbol,PERIOD_M1,from,to-60,bars);
   return n>0 && NativeBarsValid(bars,from,to);
}
bool OpeningPrice(datetime anchor,int minutes,double &price)
{
   if(!M1HistoryAvailable(anchor)) return false;
   datetime last=(datetime)MathMin((double)(anchor+(minutes-1)*60),(double)((long)TimeCurrent()/60*60));
   if(last<anchor) return false;
   MqlRates bars[];ArraySetAsSeries(bars,false);
   int n=CopyRates(_Symbol,PERIOD_M1,anchor,last,bars);
   if(n<=0 || !NativeBarsValid(bars,anchor,anchor+minutes*60)) return false;
   price=bars[0].open;
   return true;
}
bool RejectDay(string reason)
{
   if(!dayRejected){dayRejected=true;daysRejected++;}
   dayWaiting=true;
   if(dataWaitReason!=reason)
   {
      dataWaitReason=reason;
      rejectedDetails+=TimeToString(day,TIME_DATE)+","+reason+"\r\n";
      PrintFormat("SIGMA DATA_WAIT day=%s reason=%s synchronized=%d firstM1=%s; retrying, protection active",
         TimeToString(day,TIME_DATE),reason,(int)SeriesInfoInteger(_Symbol,PERIOD_M1,SERIES_SYNCHRONIZED),
         TimeToString((datetime)SeriesInfoInteger(_Symbol,PERIOD_M1,SERIES_FIRSTDATE),TIME_DATE|TIME_MINUTES));
   }
   return false;
}
void ResetDay(datetime now)
{
   day=OBRDay(now); entries=0; firstSide=0; retryArmed=false; retryEligibleAfter=0;
   rangeReady=false; armed=false; beDone=false;
   rthOpenReady=false; rthOpen=0;
   dayValid=false;
   sigmaBandReady=false;sigmaFlipPendingSide=0; sigmaUpperBand=0; sigmaLowerBand=0; sigmaCurrentSlot=-1;
   gtLastEntry=0;gtLastStop=0;gtLastTarget=0;gtLastBracketTime=0;
   if(OBRVisualEnabled()) ObjectsDeleteAll(0,OBRVisualPrefix());
   dayWaiting=false; dayRejected=false; dataWaitReason="";
   virtualActive=false; virtualStart=0; virtualLastBar=0;
   lastSignal=0;
}

// Load prior-session slot observations for current day.
// Collects N_Sigma completed RTH sessions (working backwards) for each of 13 M30 slots.
// Slot s starts at server time = OBRSessionTime(sessionDay, s*30)  [09:30 CT + s*30 min]
// Returns true when all 13 slots have N_Sigma observations.
bool LoadPriorSlotHistory(datetime today)
{
   ArrayInitialize(slotObservations,0.0);
   ArrayInitialize(slotCounts,0);
   int sessionsFound=0;
   datetime scan=today-86400;
   for(int attempt=0;attempt<30 && sessionsFound<N_Sigma;attempt++,scan-=86400)
   {
      // Skip weekends
      MqlDateTime wd;TimeToStruct(scan,wd);
      if(wd.day_of_week==0 || wd.day_of_week==6) continue;
      // Get RTH open for this session
      datetime rthOpenServer=OBRSessionTime(scan,0); // 09:30 ET server time
      if(rthOpenServer<=0) continue;
      double sessOpen=0;
      if(!OpeningPrice(rthOpenServer,5,sessOpen)) continue;
      // Check all 13 slots are available
      bool sessionOk=true;
      double devs[13];
      for(int s=0;s<MAX_SLOT_IDX;s++)
      {
         datetime slotFrom=OBRSessionTime(scan,s*30);      // slot start server time
         datetime slotTo  =slotFrom+30*60;
         MqlRates sb[];
         if(!Window(slotFrom,slotTo,sb,30)){sessionOk=false;break;}
         double m30Close=sb[ArraySize(sb)-1].close;
         devs[s]=MathAbs(m30Close-sessOpen);
      }
      if(!sessionOk) continue;
      // Store observation (append in discovery order — oldest first as we walk backwards)
      for(int s=0;s<MAX_SLOT_IDX;s++)
      {
         if(slotCounts[s]<N_Sigma)
         {
            slotObservations[s*N_Sigma+slotCounts[s]]=devs[s];
            slotCounts[s]++;
         }
      }
      sessionsFound++;
   }
   // All 13 slots must have N_Sigma observations
   for(int s=0;s<MAX_SLOT_IDX;s++)
      if(slotCounts[s]<N_Sigma) return false;
   return true;
}

// Compute band for slot s from stored observations. Returns true on success.
bool ComputeSlotBand(int s,double rthOpenPrice,double &upper,double &lower)
{
   if(s<0 || s>=MAX_SLOT_IDX || slotCounts[s]<N_Sigma) return false;
   double sum=0;
   for(int k=0;k<N_Sigma;k++)
      sum+=slotObservations[s*N_Sigma+k];
   double bandWidth=(sum/N_Sigma)*BandMult;
   if(bandWidth<=0 || !MathIsValidNumber(bandWidth)) return false;
   upper=rthOpenPrice+bandWidth;
   lower=rthOpenPrice-bandWidth;
   return true;
}

bool NewDay(datetime now)
{
   // Get RTH open (09:30 ET)
   datetime anchor=OBRSessionTime(day,0);
   if(anchor<=0) return RejectDay("SESSION_TIME_UNAVAILABLE");
   rthOpenReady=false;
   rthOpen=0;
   if(!OpeningPrice(anchor,5,rthOpen)) return RejectDay("RTH_OPEN_UNAVAILABLE");
   rthOpenReady=true;
   // Load prior slot history (requires M1 history going back N_Sigma RTH sessions)
   if(!LoadPriorSlotHistory(day)) return RejectDay("SLOT_HISTORY_INCOMPLETE");
   sigmaBandReady=false;sigmaFlipPendingSide=0; sigmaUpperBand=0; sigmaLowerBand=0; sigmaCurrentSlot=-1;
   Note("DAY",StringFormat("rthOpen=%f N=%d mult=%f",rthOpen,N_Sigma,BandMult));
   if(dayWaiting && !MQLInfoInteger(MQL_TESTER))
      lastSignal=MathMax(lastSignal,iTime(_Symbol,sourceTF,1));
   if(dayRejected){daysRejected--;dayRejected=false;}
   dayWaiting=false;dataWaitReason="";
   return true;
}

// SetRange: for Sigma, update the live band for the most recently completed M30 slot.
bool SetRange(datetime now)
{
   // Sigma: source is M30 from 09:30 ET. Slot s corresponds to server window
   // [OBRSessionTime(day, s*30), OBRSessionTime(day, s*30)+30min).
   // We need at least one completed slot (minute >= 600 CT = 10:00 ET).
   int minute=OBRMinute(now);
   if(minute<600) return false; // First M30 not yet complete
   // Which slot just completed?
   // Slot 0 completes at 10:00 ET = minute 600 CT; slot s completes at (600+s*30) min CT.
   int completedSlot=(minute-600)/30; // 0-based index of last fully completed slot
   if(completedSlot<0) return false;
   if(completedSlot>=MAX_SLOT_IDX) completedSlot=MAX_SLOT_IDX-1;
   if(completedSlot==sigmaCurrentSlot) return true; // Already set for this slot
   // Update band for completedSlot
   double upper=0,lower=0;
   if(!ComputeSlotBand(completedSlot,rthOpen,upper,lower)) return false;
   sigmaUpperBand=upper;
   sigmaLowerBand=lower;
   sigmaBandReady=true;
   sigmaCurrentSlot=completedSlot;
   Note("SIGMA_BAND",StringFormat("slot=%d upper=%f lower=%f",completedSlot,upper,lower));
   return true;
}

double OBRRequestedVolume(bool fixedSize,double lots,double budget,double unitRisk,double step,double minimum,double maximum)
{
   if(unitRisk<=0 || !MathIsValidNumber(unitRisk) || step<=0 || minimum<=0 || maximum<minimum) return 0;
   if(fixedSize)
   {
      if(!MathIsValidNumber(lots) || lots<minimum || lots>maximum || MathAbs(lots/step-MathRound(lots/step))>1e-7) return 0;
      return lots;
   }
   return MathMin(MathFloor(budget/unitRisk/step+1e-9)*step,maximum);
}
string OBRSizingLabel()
{return UseFixedSize ? "Lots "+DoubleToString(FixedLots,4) : "Risk "+DoubleToString(RiskMoney,2);}
// Swap-aware cost-covered price. Reads accrued swap from the live broker position
// and adds it (as an additional one-time cost per lot) to the trail cost floor.
// Called only when OwnPosition() is true; safe to query POSITION_SWAP directly.
// The initial trade entry does not block on swap — swap is unknown at entry time.
// Only the trailing ratchet incorporates swap after it has accrued intraday or overnight.
bool OBRCostCoveredPriceSwapAware(int side,double entry,double commission,double volume,double &price)
{
   double accruedSwap=OwnPosition() ? PositionGetDouble(POSITION_SWAP) : 0.0;
   // accruedSwap is negative when broker debits (short CFDs, overnight financing).
   // Treat it as an additional cost: if swap < 0, floor must recover it.
   double swapPerLot=volume>0 && accruedSwap<0 ? MathAbs(accruedSwap)/volume : 0.0;
   double totalCommission=commission+swapPerLot;
   if(!OBRCostCoveredPrice(side,entry,totalCommission,price)) return false;
   if(swapPerLot>0)
      Note("SWAP_FLOOR",StringFormat("accruedSwap=%f swapPerLot=%f newFloor=%f",accruedSwap,swapPerLot,price));
   return true;
}

bool Open(int side,double stop,double target,double distance)
{
   MqlTick q;if(!SymbolInfoTick(_Symbol,q)) return false;
   double entry=(side==1 ? q.ask : q.bid);
   double commission=OBRCommissionEstimate();
   double friction=commission;
   double slipLoss=0;
   if(!OrderCalcProfit(ORDER_TYPE_BUY,_Symbol,1.0,entry,entry-2*SlipPrice,slipLoss)) return false;
   friction+=MathAbs(slipLoss);
   double step=SymbolInfoDouble(_Symbol,SYMBOL_VOLUME_STEP),minimum=SymbolInfoDouble(_Symbol,SYMBOL_VOLUME_MIN);
   if(step<=0 || minimum<=0) {Note("RISK_INVALID","Invalid volume contract");return false;}
   
   double volume=1.0;
   double unitRisk=0;
   
   if(SlPct>0.0)
   {
      double stopDist = entry * (SlPct / 100.0);
      stop = RoundPrice(entry - side * stopDist);
      target = RoundPrice(entry + side * stopDist * 3.0);
      if(!OBRBracket(side,entry,stop,target)) {invalidBrackets++;Note("INVALID_BRACKET",StringFormat("%d %f %f %f",side,entry,stop,target));return false;}
      double loss=0;
      if(!OrderCalcProfit(side==1 ? ORDER_TYPE_BUY : ORDER_TYPE_SELL,_Symbol,1.0,entry,stop,loss)) return false;
      unitRisk=MathAbs(loss)+friction;
      if(unitRisk<=0 || !MathIsValidNumber(unitRisk)) {Note("RISK_INVALID","Invalid cash risk");return false;}
      volume=OBRRequestedVolume(UseFixedSize,FixedLots,RiskCash,unitRisk,step,minimum,SymbolInfoDouble(_Symbol,SYMBOL_VOLUME_MAX));
      if(volume<minimum || unitRisk<=0) {Note(UseFixedSize ? "FIXED_LOTS_INVALID" : "RISK_TOO_SMALL",DoubleToString(unitRisk*minimum,2));return false;}
      if(SkipSetupsAboveRiskBudget && unitRisk*volume>RiskBudgetUsd)
      {
         Note("RISK_CAP_SKIP",StringFormat("risk=%f cap=%f",unitRisk*volume,RiskBudgetUsd));
         return false;
      }
      double stops=SymbolInfoInteger(_Symbol,SYMBOL_TRADE_STOPS_LEVEL)*_Point;
      if(side*(entry-stop)<stops || side*(target-entry)<stops){invalidBrackets++;return false;}
   }
   else
   {
      stop=0;target=0;
      volume=UseFixedSize ? MathMax(minimum, FixedLots) : minimum;
      volume=NormalizeDouble(MathFloor(volume/step)*step, 2);
   }
   
   entryCommissionPerLot=commission;modelCostPerLot=friction;
   if(!OBRRuntimeEntryAllowed(entry,stop,target,volume,side)) {Note("ENTRY_BLOCK","EA safety, clock, margin or terminal gate");return false;}
   string comment=MQLInfoInteger(MQL_TESTER) ? OBRDisplayName : "OBR#"+IntegerToString((long)runtimeIntentNonce);
   bool ok=(side==1 ? trade.Buy(volume,_Symbol,0,stop,target,comment) : trade.Sell(volume,_Symbol,0,stop,target,comment));
   uint ret=trade.ResultRetcode();
   if(!ok || (ret!=TRADE_RETCODE_DONE && ret!=TRADE_RETCODE_DONE_PARTIAL))
   {
      failedOrders++;Note("ENTRY_REJECT",IntegerToString((int)ret));
      if(!MQLInfoInteger(MQL_TESTER)){entries++;runtimeBlocked=true;OBRStateSave();}
      return false;
   }
   initialRisk=(stop>0 ? MathAbs(trade.ResultPrice()-stop) : 0);beDone=false;barPeakR=0;runtimeTrailLastBar=0;
   double filledVolume=trade.ResultVolume()>0 ? trade.ResultVolume() : volume;
   entryCommissionPerLot=commission;modelCostPerLot=friction;modelCost=friction*filledVolume;totalCost+=modelCost;ownedEntryTime=TimeCurrent();
   entries++;
   if(entries==1) firstSide=side;
   retryArmed=false;retryEligibleAfter=0;
   worstRisk=MathMax(worstRisk,unitRisk*volume);
   OBRRememberBracket(trade.ResultPrice(),stop,target);
   OBRDrawFill(side,TimeCurrent(),trade.ResultPrice(),"Entry");
   OBRRuntimeFilled(side,stop,friction);
   Note("ENTRY",StringFormat("model=Sigma side=%d lots=%f fill=%f sl=%f tp=%f count=%d modeledCost=%f",side,volume,trade.ResultPrice(),stop,target,entries,modelCost));
   return true;
}
void OBRObserveRecoveryMinute(datetime source,double close,int side,double entry)
{
   if(initialRisk<=0 || source<=runtimeTrailLastBar || source+60<=ownedEntryTime) return;
   barPeakR=MathMax(barPeakR,side*(close-entry)/initialRisk);
   runtimeTrailLastBar=source;
}
void ManageClosedMinute()
{
   if(Management!=M1RStep || runtimeRecoveryHold || runtimeClosePending || !OwnPosition() || initialRisk<=0) return;
   if(!MQLInfoInteger(MQL_TESTER) && (!OBRRuntimeAccountContextValid() || !TerminalInfoInteger(TERMINAL_CONNECTED))) return;
   MqlRates b[];
   datetime completed=iTime(_Symbol,PERIOD_M1,1);
   if(completed<=0 || completed<=runtimeTrailLastBar) return;
   ArraySetAsSeries(b,false);
   datetime from=runtimeTrailLastBar>0 ? runtimeTrailLastBar+60 : completed;
   if(CopyRates(_Symbol,PERIOD_M1,from,completed,b)<=0) return;
   int side=PositionGetInteger(POSITION_TYPE)==POSITION_TYPE_BUY ? 1 : -1;
   double entry=PositionGetDouble(POSITION_PRICE_OPEN);
   double sl=PositionGetDouble(POSITION_SL),tp=PositionGetDouble(POSITION_TP);
   ulong ticket=(ulong)PositionGetInteger(POSITION_TICKET);
   for(int i=0;i<ArraySize(b);i++)
   {
      OBRObserveRecoveryMinute(b[i].time,b[i].close,side,entry);
   }
   if(runtimeTrailLastBar!=completed) return;
   OBRStateSave();
   if(entries==2){OBRManageRetryBreakEven();return;}
   double lockR=0;
   if(!OBRBarTrailLock(barPeakR,BarTrailActivateR,BarTrailDistanceR,BarTrailStepR,lockR)) return;
   double covered=0;
   double commission=MathMax(entryCommissionPerLot,2*OBREntryChargesPerLot((ulong)PositionGetInteger(POSITION_IDENTIFIER)));
   if(!OBRCostCoveredPriceSwapAware(side,entry,commission,PositionGetDouble(POSITION_VOLUME),covered)){Note("M1_COST_INVALID","Cannot calculate a cost-covered stop");return;}
   double candidate=entry+side*lockR*initialRisk;
   double tick=SymbolInfoDouble(_Symbol,SYMBOL_TRADE_TICK_SIZE);
   double raw=side==1 ? MathMax(covered,candidate) : MathMin(covered,candidate);
   double desired=NormalizeDouble((side==1 ? MathCeil(raw/tick-1e-9) : MathFloor(raw/tick+1e-9))*tick,_Digits);
   if(side*(desired-sl)<=0) return;
   MqlTick q;if(!SymbolInfoTick(_Symbol,q)) return;
   double current=side==1 ? q.bid : q.ask;
   if(side*(current-desired)<=0) {CloseOwned("M1_TRAIL_CROSSED");return;}
   double bound=MathMax(SymbolInfoInteger(_Symbol,SYMBOL_TRADE_STOPS_LEVEL),SymbolInfoInteger(_Symbol,SYMBOL_TRADE_FREEZE_LEVEL))*_Point;
   if(side*(current-desired)<=bound) return;
   if(trade.PositionModify(ticket,desired,tp) && trade.ResultRetcode()==TRADE_RETCODE_DONE)
      Note("M1_STOP_CHANGE",StringFormat("bar=%s peakR=%f lockR=%f stop=%f",TimeToString(b[0].time,TIME_DATE|TIME_SECONDS),barPeakR,lockR,desired));
   else {failedOrders++;Note("M1_STOP_REJECT",IntegerToString((int)trade.ResultRetcode()));}
}
void OBRManageRetryBreakEven()
{
   if(entries!=2 || barPeakR<BreakEvenR) return;
   int side=PositionGetInteger(POSITION_TYPE)==POSITION_TYPE_BUY ? 1 : -1;
   double entry=PositionGetDouble(POSITION_PRICE_OPEN),sl=PositionGetDouble(POSITION_SL),tp=PositionGetDouble(POSITION_TP);
   double commission=MathMax(entryCommissionPerLot,2*OBREntryChargesPerLot((ulong)PositionGetInteger(POSITION_IDENTIFIER)));
   double covered=0;
   if(!OBRCostCoveredPriceSwapAware(side,entry,commission,PositionGetDouble(POSITION_VOLUME),covered)){Note("RETRY_COST_INVALID","Cannot calculate retry cost-covered stop");return;}
   if(side*(covered-sl)<=0) return;
   MqlTick q;if(!SymbolInfoTick(_Symbol,q)) return;
   double current=side==1 ? q.bid : q.ask;
   if(side*(current-covered)<=0){CloseOwned("RETRY_BE_CROSSED");return;}
   double bound=MathMax(SymbolInfoInteger(_Symbol,SYMBOL_TRADE_STOPS_LEVEL),SymbolInfoInteger(_Symbol,SYMBOL_TRADE_FREEZE_LEVEL))*_Point;
   if(side*(current-covered)<=bound) return;
   ulong ticket=(ulong)PositionGetInteger(POSITION_TICKET);
   if(trade.PositionModify(ticket,covered,tp) && trade.ResultRetcode()==TRADE_RETCODE_DONE)
      Note("RETRY_BE","Cost-covered retry stop "+DoubleToString(covered,_Digits));
   else {failedOrders++;Note("RETRY_BE_REJECT",IntegerToString((int)trade.ResultRetcode()));}
}
int OnInit()
{
#ifdef OBR_VISUAL_PROBE
   if(!MQLInfoInteger(MQL_TESTER)) return INIT_FAILED;
#endif
#ifdef OBR_LIFECYCLE_PROBE
   if(!MQLInfoInteger(MQL_TESTER)) return INIT_FAILED;
#endif
   if(!OBRBarTrailSelfTest() || !OBRCalendarSelfTest() || !OBRSignalTimeSelfTest()) {Print("SIGMA_CALC_SELFTEST_FAILED");return INIT_FAILED;}
   Print("SIGMA_CALC_SELFTEST_PASS");
   RunId=OBROutputFamily+(MQLInfoInteger(MQL_TESTER) ? "_test_"+IntegerToString((long)GetTickCount64()) : "_trades");
   // Parameter validation
   if(N_Sigma<1 || N_Sigma>10) return INIT_PARAMETERS_INCORRECT;
   if(BandMult<=0) return INIT_PARAMETERS_INCORRECT;
   if(!UseFixedSize && RiskCash<=0) return INIT_PARAMETERS_INCORRECT;
   if(UseFixedSize && FixedLots<=0) return INIT_PARAMETERS_INCORRECT;
   if(CommissionRTPerLot<0 || SlipPrice<0 || Magic==0) return INIT_PARAMETERS_INCORRECT;
   if(BarTrailActivateR<=0 || BarTrailDistanceR<=0 || BarTrailStepR<=0) return INIT_PARAMETERS_INCORRECT;
   // Allocate slot history arrays
   ArrayResize(slotObservations,MAX_SLOT_IDX*N_Sigma);
   ArrayInitialize(slotObservations,0.0);
   ArrayResize(slotCounts,MAX_SLOT_IDX);
   ArrayInitialize(slotCounts,0);
   OBRClockRefresh(TimeCurrent());
   sourceTF=PERIOD_M30;
   TesterHideIndicators(true);
   if(MQLInfoInteger(MQL_TESTER) && !OBRRuntimeSelfTest()) return INIT_FAILED;
   if(!OBRRuntimeInit()) return INIT_FAILED;
   trade.SetExpertMagicNumber(Magic);trade.SetTypeFillingBySymbol(_Symbol);trade.SetDeviationInPoints(30);
   OBRInitVisual();
   if(DetailedLog)
   {
      OBRPrepareOutput();
      string name=OBROutputRoot+"/ledger_"+RunId+".csv";
      bool append=false;
      if(!MQLInfoInteger(MQL_TESTER))
      {append=true;name=OBROutputRoot+"/ledger_"+RunId+"_"+IntegerToString(runtimeAccountId)+"_"+IntegerToString((long)Magic)+".csv";}
      ledger=FileOpen(name,FILE_COMMON|FILE_WRITE|FILE_SHARE_READ|(append ? FILE_READ : 0)|FILE_CSV|FILE_ANSI,',');
      if(ledger==INVALID_HANDLE){Print("SIGMA_LOG_BLOCK: requested ledger unavailable");return INIT_FAILED;}
      if(!append || FileSize(ledger)==0) FileWrite(ledger,"server_time","event","detail");
      else FileSeek(ledger,0,SEEK_END);
   }
   PrintFormat("%s ready | new trades=%s | %s | clock=automatic server/GMT or tester weekly anchor",OBRDisplayName,EnableTrading ? "enabled" : "disabled",OBRSizingLabel());
   PrintFormat("SIGMA_SPEC tick=%f tickvalue=%f minlot=%f contract=%f N=%d mult=%f",SymbolInfoDouble(_Symbol,SYMBOL_TRADE_TICK_SIZE),SymbolInfoDouble(_Symbol,SYMBOL_TRADE_TICK_VALUE),SymbolInfoDouble(_Symbol,SYMBOL_VOLUME_MIN),SymbolInfoDouble(_Symbol,SYMBOL_TRADE_CONTRACT_SIZE),N_Sigma,BandMult);
   OBRRefreshLiveVisuals(TimeTradeServer());OBRDraw();
   return INIT_SUCCEEDED;
}
void OnTick()
{
   if(!MQLInfoInteger(MQL_TESTER) && !OBRRuntimeAccountContextValid()) return;
#ifdef OBR_LIFECYCLE_PROBE
   OBRNativeLifecycleProbe();return;
#endif
   OBRDraw();
   datetime now=TimeCurrent();
   if(!OBRClockRefresh(now)){OBRDraw();return;}
   if(!DateAllowed(now)) {CloseOwned("PHASE_LOCK");return;}
   double adjustedEquity=AccountInfoDouble(ACCOUNT_EQUITY)-totalCost;
   adjustedPeak=MathMax(adjustedPeak,adjustedEquity);adjustedDD=MathMax(adjustedDD,adjustedPeak-adjustedEquity);
   int minute=OBRMinute(now),cutoff=OBRSessionCutoff(now,(int)Model);
   if(OwnPosition() && OBRDay((datetime)PositionGetInteger(POSITION_TIME))<OBRDay(now)) {CloseOwned("OVERDUE_DAY");return;}
   if(minute>=cutoff)
   {
      if(virtualActive)
      {
         virtualActive=false;virtualExpired++;Note("VIRTUAL_CUTOFF","Unresolved first attempt expires without a broker trade");
         OBRStateSave();
      }
      CloseOwned("CUTOFF");return;
   }
   if(!OBRRuntimeHistoryReady()) return;
   datetime m=iTime(_Symbol,PERIOD_M1,0);
   if(m==lastMinute) return;lastMinute=m;
#ifdef OBR_VISUAL_PROBE
   int completed_=0;for(int y=0;y<7;y++) completed_+=yearTrades[y];
   if(completed_>=1)
   {
      string snapshot="SigmaBreakoutPreview_"+OBROutputFamily+".png";
      if(!gtPreviewRequested)
      {
         OBRDraw();OBRVisualFit();
         ResetLastError();
         gtPreviewRequested=ChartScreenShot(0,snapshot,1280,800,ALIGN_RIGHT);
         gtPreviewRequestedMs=GetTickCount64();
         PrintFormat("SIGMA_VISUAL_DIAG requested=%d error=%d chart=%I64d width=%d height=%d visual=%d path=%s",gtPreviewRequested,GetLastError(),ChartID(),ChartGetInteger(0,CHART_WIDTH_IN_PIXELS),ChartGetInteger(0,CHART_HEIGHT_IN_PIXELS),MQLInfoInteger(MQL_VISUAL_MODE),TerminalInfoString(TERMINAL_DATA_PATH));
         if(!gtPreviewRequested){Print("SIGMA_VISUAL_SCREENSHOT_FAILED");TesterStop();}
      }
      else if(FileIsExist(snapshot) || GetTickCount64()-gtPreviewRequestedMs>5000)
      {Print(FileIsExist(snapshot) ? "SIGMA_VISUAL_SCREENSHOT_PASS" : "SIGMA_VISUAL_SCREENSHOT_TIMEOUT");TesterStop();}
      return;
   }
#endif
   ManageClosedMinute();
   if(virtualActive && OBRDay(now)!=day){virtualActive=false;virtualExpired++;}
   if(virtualActive)
   {
      MqlRates closed[];ArraySetAsSeries(closed,false);
      datetime begin=virtualLastBar>0 ? virtualLastBar+60 : virtualStart;
      if(!M1HistoryAvailable(begin)) return;
      int count=CopyRates(_Symbol,PERIOD_M1,begin,m-60,closed);
      if(count<=0) return;
      if(!NativeBarsValid(closed,begin,m)) return;
      for(int i=0;i<count && virtualActive;i++)
      {
         virtualLastBar=closed[i].time;
         int outcome=OBRVirtualExit(closed[i].low,closed[i].high,virtualStop,virtualTarget);
         if(outcome!=0)
         {
            if(closed[i].low<=virtualStop && closed[i].high>=virtualTarget) virtualCollisions++;
            if(outcome<0) virtualStops++;else virtualTargets++;
            virtualActive=false;
            Note("VIRTUAL_EXIT",StringFormat("bar=%s outcome=%d sl=%f tp=%f",TimeToString(closed[i].time,TIME_DATE|TIME_SECONDS),outcome,virtualStop,virtualTarget));
         }
      }
      OBRStateSave();
   }
   // Sigma: M30 bars begin at 09:30 ET (minute=570 CT). First complete M30 = minute 600.
   if(minute<600) return;
   if(OBRDay(now)!=day)
   {
      ResetDay(now); // resets dayValid=false via rthOpenReady=false
      OBRStateRestore(now);
   }
   dayValid=rthOpenReady;
   if(!dayValid)
   {
      dayValid=NewDay(now);
      if(dayValid) daysReady++;
   }
   if(!dayValid) return;
   if(!M1HistoryAvailable(OBRSessionTime(day)-3600))
   {
      dayWaiting=true;
      bool connected=MQLInfoInteger(MQL_TESTER) || TerminalInfoInteger(TERMINAL_CONNECTED);
      bool synchronized=SeriesInfoInteger(_Symbol,PERIOD_M1,SERIES_SYNCHRONIZED);
      string reason=!connected ? "CONNECTION_WAIT" : !synchronized ? "M1_SYNC_WAIT" : "M1_BOUNDARY_WAIT";
      if(dataWaitReason!=reason)
      {
         dataWaitReason=reason;
         PrintFormat("SIGMA DATA_WAIT: %s; firstM1=%s requiredFrom=%s; new entries paused, broker protection retained",reason,
            TimeToString((datetime)SeriesInfoInteger(_Symbol,PERIOD_M1,SERIES_FIRSTDATE),TIME_DATE|TIME_SECONDS),
            TimeToString(OBRSessionTime(day)-3600,TIME_DATE|TIME_SECONDS));
      }
      return;
   }
   // Update the live band for the current slot
   if(!rangeReady)
   {
      rangeReady=SetRange(now);
      if(!rangeReady)
      {
         if(minute>=630)
         {
            dayWaiting=true;
            if(dataWaitReason!="SLOT_BAND_WAIT")
            {dataWaitReason="SLOT_BAND_WAIT";Print("SIGMA DATA_WAIT: slot band unavailable; retrying");}
         }
         return;
      }
   }
   else
   {
      // Keep band current as new slots complete
      SetRange(now);
   }
   if(dayWaiting && !MQLInfoInteger(MQL_TESTER))
      lastSignal=MathMax(lastSignal,iTime(_Symbol,sourceTF,1));
   dayWaiting=false;dataWaitReason="";
   // 1. Session Cutoff flatten at 16:00 ET (minute >= 960)
   if(OwnPosition() && minute>=cutoff)
   {
      CloseOwned("SIGMA_SESSION_CLOSE");
      return;
   }
   // 2. Band readiness and M30 signal
   if(!sigmaBandReady) return;
   MqlRates b[];ArraySetAsSeries(b,true);
   int count=2;
   if(CopyRates(_Symbol,sourceTF,1,count,b)!=count || b[0].time+PeriodSeconds(sourceTF)<=m-60) return;
   if(b[0].time<=lastSignal) return;lastSignal=b[0].time;
   OBRStateSave();
   if(!MQLInfoInteger(MQL_TESTER) && runtimeBlocked) return;
   if(!OBRSignalCloseEligible(OBRMinute(b[0].time+PeriodSeconds(sourceTF)))) return;
   if(!EnableTrading) return;
   
   int side=OBRSignalSide(2,0,b[0].close,sigmaUpperBand,sigmaLowerBand,0);
   
   // 3. Position Flip Check
   if(OwnPosition())
   {
      long posType=PositionGetInteger(POSITION_TYPE);
      if(posType==POSITION_TYPE_BUY && side==-1)
      {
         Note("SIGMA_FLIP_SIGNAL",StringFormat("Flip Long->Short at %f (lowerBand=%f)",b[0].close,sigmaLowerBand));
         sigmaFlipPendingSide=-1;
         CloseOwned("SIGMA_FLIP_CLOSE");
         return;
      }
      else if(posType==POSITION_TYPE_SELL && side==1)
      {
         Note("SIGMA_FLIP_SIGNAL",StringFormat("Flip Short->Long at %f (upperBand=%f)",b[0].close,sigmaUpperBand));
         sigmaFlipPendingSide=1;
         CloseOwned("SIGMA_FLIP_CLOSE");
         return;
      }
      return;
   }
   
   if(virtualActive) return;
   
   // 4. Pending Flip execution after flat
   if(sigmaFlipPendingSide!=0)
   {
      if(minute<cutoff)
      {
         int flip=sigmaFlipPendingSide;
         sigmaFlipPendingSide=0;
         Note("SIGMA_FLIP_EXEC",StringFormat("Executing flip entry side=%d",flip));
         Open(flip,0,0,0);
         return;
      }
      else
      {
         sigmaFlipPendingSide=0;
         return;
      }
   }
   
   // 5. Normal breakout entry
   if(side!=0 && minute<cutoff && OBRCanEnter(minute,StartMinute,cutoff,entries,OBRMaxAttempts))
   {
      Note("SIGMA_SIGNAL",StringFormat("sourceOpen=%s sourceClose=%s close=%f upperBand=%f lowerBand=%f side=%d slot=%d",
           TimeToString(b[0].time,TIME_DATE|TIME_SECONDS),TimeToString(b[0].time+PeriodSeconds(sourceTF),TIME_DATE|TIME_SECONDS),
           b[0].close,sigmaUpperBand,sigmaLowerBand,side,sigmaCurrentSlot));
      Open(side,0,0,0);
   }
}
void OnTradeTransaction(const MqlTradeTransaction &tx,const MqlTradeRequest &request,const MqlTradeResult &result)
{
   if(tx.type!=TRADE_TRANSACTION_DEAL_ADD || !HistoryDealSelect(tx.deal)) return;
   if((ulong)HistoryDealGetInteger(tx.deal,DEAL_MAGIC)!=Magic || HistoryDealGetString(tx.deal,DEAL_SYMBOL)!=_Symbol) return;
   obrBrokerVisualDirty=true;
   long entry=HistoryDealGetInteger(tx.deal,DEAL_ENTRY);
   if(entry!=DEAL_ENTRY_OUT && entry!=DEAL_ENTRY_OUT_BY) return;
   datetime t=(datetime)HistoryDealGetInteger(tx.deal,DEAL_TIME);MqlDateTime p;TimeToStruct(OBRDay(t),p);
   if(ownedEntryTime>0 && OBRDay(t)!=OBRDay(ownedEntryTime))
   {
      overnightExits++;
      carryDetails+=TimeToString(ownedEntryTime-8*3600,TIME_DATE|TIME_SECONDS)+","+TimeToString(t-8*3600,TIME_DATE|TIME_SECONDS)+"\r\n";
   }
   int y=p.year-2020;
   double qty=HistoryDealGetDouble(tx.deal,DEAL_VOLUME),exitPrice=HistoryDealGetDouble(tx.deal,DEAL_PRICE);
   int exitReason=(int)HistoryDealGetInteger(tx.deal,DEAL_REASON);
   int exitSide=HistoryDealGetInteger(tx.deal,DEAL_TYPE)==DEAL_TYPE_BUY ? 1 : -1;
   ulong pid=(ulong)HistoryDealGetInteger(tx.deal,DEAL_POSITION_ID);
   double exitCharges=HistoryDealGetDouble(tx.deal,DEAL_COMMISSION)+HistoryDealGetDouble(tx.deal,DEAL_FEE);
   double brokerNet=HistoryDealGetDouble(tx.deal,DEAL_PROFIT)+exitCharges+HistoryDealGetDouble(tx.deal,DEAL_SWAP);
   double entryCharges=OBREntryChargesPerLot(pid)*qty;
   double slipCost=MathMax(0,modelCostPerLot-entryCommissionPerLot)*qty;
   double extraCommission=MQLInfoInteger(MQL_TESTER) ? MathMax(0,entryCommissionPerLot*qty-entryCharges+exitCharges) : 0;
   double net=brokerNet-entryCharges-slipCost-extraCommission;
   if(MQLInfoInteger(MQL_TESTER)) totalCost-=MathMin(entryCommissionPerLot*qty,MathMax(0,entryCharges-exitCharges));
   OBRStateSave();
   if(y>=0 && y<7){yearProfit[y]+=net;yearTrades[y]++;}
   int di=(int)((OBRDay(t)-D'2020.01.01')/86400);
   if(di>=0 && di<ArraySize(dailyNet)) dailyNet[di]+=net;
   if(net>0)netWins+=net;else netLosses-=net;
   if(entries==1 && !OwnPosition() && exitReason==DEAL_REASON_SL)
   {
      OBRArmSameSideRetry(t,net<0,exitReason);
   }
   Note("EXIT",StringFormat("ticket=%I64u price=%f netAdjusted=%f reason=%d",tx.deal,exitPrice,net,exitReason));
   OBRDrawFill(exitSide,t,exitPrice,"Exit");
}
void OBRArmSameSideRetry(datetime t,bool losingExit,int exitReason)
{
   retryArmed=entries==1 && firstSide!=0 && losingExit && exitReason==DEAL_REASON_SL;
   retryEligibleAfter=retryArmed ? t : 0;
   if(retryArmed) Note("RETRY_ARMED",StringFormat("side=%d eligibleAfter=%s",firstSide,TimeToString(t,TIME_DATE|TIME_SECONDS)));
   OBRStateSave();
}
double OnTester()
{
#ifdef OBR_LIFECYCLE_PROBE
   return 0;
#endif
   double net=TesterStatistics(STAT_PROFIT)-totalCost;
   double pf=netLosses>0 ? netWins/netLosses : 0;
   OBRPrepareOutput();
   string path=OBROutputRoot+"/"+RunId+".csv";
   int out=FileOpen(path,FILE_COMMON|FILE_WRITE|FILE_CSV|FILE_ANSI,',');
   if(out==INVALID_HANDLE) return -1000000;
   FileWrite(out,"model","net","pf","equityDD","trades","days","rejectedDays","invalidBrackets","failedOrders","worstRisk");
   FileWrite(out,OBRDisplayName,net,pf,adjustedDD,TesterStatistics(STAT_TRADES),daysReady,daysRejected,invalidBrackets,failedOrders,worstRisk);
   FileClose(out);
   bool exported=!DetailedLog || OBRExportTrades(path+".trades.csv");
   PrintFormat("%s TESTER_RESULT net=%.2f PF=%.6f trades=%d export=%s path=%s",OBRDisplayName,net,pf,(int)TesterStatistics(STAT_TRADES),!DetailedLog ? "DISABLED" : exported ? "PASS" : "FAIL",path);
   return exported && failedOrders==0 && invalidBrackets==0 ? (adjustedDD>0 ? net/adjustedDD : 0) : -1000000;
}
void OnDeinit(const int reason)
{
   OBRStateSave();EventKillTimer();
   if(runtimeLockFile!=INVALID_HANDLE) FileClose(runtimeLockFile);
   if(reason!=REASON_CHARTCHANGE || ChartSymbol(0)!=_Symbol || !ShowVisuals)
      ObjectsDeleteAll(0,OBRVisualPrefix());
   if(gtPane!=INVALID_HANDLE)
   {
      for(int window=(int)ChartGetInteger(0,CHART_WINDOWS_TOTAL)-1;window>=1;window--) ChartIndicatorDelete(0,window,gtPaneName);
      IndicatorRelease(gtPane);
   }
   if(ledger!=INVALID_HANDLE) FileClose(ledger);
}
