#property strict
#property tester_no_cache
#property version "1.00"
#resource "ADXPane.ex5"
#property description "Independent USTEC strategy. Completed-bar decisions, cost-inclusive cash risk and owned-position recovery."
#include <Trade/Trade.mqh>
#ifndef OBR_STRATEGY_CORE
#define OBR_STRATEGY_CORE
// ORB_Scalper clock: live server/GMT detection; tester weekly-open anchor.
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
   // Validate in NY time so an eastern server's Saturday close is accepted.
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
      // Same 18:00 New York weekly reopening reference as ORB_Scalper.
      MqlRates minutes[];ArraySetAsSeries(minutes,false);
      datetime last=MathMin(stamp,hours[i].time+3599);
      if(CopyRates(_Symbol,PERIOD_M1,hours[i].time,last,minutes)<=0) continue;
      datetime opening=minutes[0].time;
      int offset=OBRWeekendOffset(hours[i-1].time,opening);
      if(offset==CLOCK_UNKNOWN) continue;
      // A holiday can remove the current Fri->Sun gap. Reuse an older anchor
      // only inside the same NY DST season; do not freeze last season's offset.
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
   MqlDateTime p; TimeToStruct(OBRCentral(server),p);
   p.hour=0; p.min=0; p.sec=0;
   return StructToTime(p);
}
int OBRMinute(datetime server)
{
   MqlDateTime p; TimeToStruct(OBRCentral(server),p);
   return p.hour*60+p.min;
}
bool OBRAllowed(datetime server) {return server<D'2026.01.01' && server>=D'2019.09.01';}
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
   // Gregorian Easter, then Good Friday. Calendar-only; no market-data inputs.
   int a=y%19,b=y/100,c=y%100,e=b%4,f=(b+8)/25,g=(b-f+1)/3;
   int h=(19*a+b-b/4-g+15)%30,i=c/4,k=c%4,l=(32+2*e+2*i-h-k)%7;
   int m=(a+11*h+22*l)/451,n=h+l-7*m+114;
   datetime friday=OBRCalendarDate(y,n/31,n%31+1)-2*86400;
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
int OBRSessionCutoff(datetime server,int model)
{
   // Policy v1: 11:55 Central on published US cash-market holidays/early
   // closes, plus conservative Christmas Eve and year-end handling.
   // This is an earlier research deadline, not the CFD's exact close time.
   string dates=
      "|2020.01.01|2020.01.20|2020.02.17|2020.04.10|2020.05.25|2020.07.03|2020.09.07|2020.11.26|2020.11.27|2020.12.24|2020.12.25|2020.12.31"
      "|2021.01.01|2021.01.18|2021.02.15|2021.04.02|2021.05.31|2021.07.05|2021.09.06|2021.11.25|2021.11.26|2021.12.24|2021.12.31"
      "|2022.01.17|2022.02.21|2022.04.15|2022.05.30|2022.06.20|2022.07.04|2022.09.05|2022.11.24|2022.11.25|2022.12.24|2022.12.26|2022.12.31"
      "|2023.01.02|2023.01.16|2023.02.20|2023.04.07|2023.05.29|2023.06.19|2023.07.03|2023.07.04|2023.09.04|2023.11.23|2023.11.24|2023.12.24|2023.12.25|2023.12.31"
      "|2024.01.01|2024.01.15|2024.02.19|2024.03.29|2024.05.27|2024.06.19|2024.07.03|2024.07.04|2024.09.02|2024.11.28|2024.11.29|2024.12.24|2024.12.25|2024.12.31"
      "|2025.01.01|2025.01.09|2025.01.20|2025.02.17|2025.04.18|2025.05.26|2025.06.19|2025.07.03|2025.07.04|2025.09.01|2025.11.27|2025.11.28|2025.12.24|2025.12.25|2025.12.31|";
   if(StringFind(dates,"|"+TimeToString(OBRDay(server),TIME_DATE)+"|")>=0) return 715;
   if(OBRDay(server)>=D'2026.01.01' && OBRFutureSpecialDay(OBRDay(server))) return 715;
   return model==1 ? 955 : 870;
}
int OBRBias(double open,double low,double high)
{
   if(high<=low || open<=low || open>=high) return 0;
   if(open>low+2*(high-low)/3) return 1;
   if(open<low+(high-low)/3) return -1;
   return 0;
}
bool OBRBracket(int side,double entry,double stop,double target)
{
   return side*(entry-stop)>0 && side*(target-entry)>0;
}
int OBRTradeCap(int model) {return model==2 ? 1 : 3;}
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
   return closeMinute>9*60;
}
bool OBRSignalTimeSelfTest()
{
   return !OBRSignalCloseEligible(525) && !OBRSignalCloseEligible(540)
      && OBRSignalCloseEligible(555) && OBRSignalCloseEligible(570);
}
int OBRSignalSide(int model,int direction,double close,double high,double low,double adx)
{
   if(model!=2 || adx<=20) return 0;
   if(direction==1 && close>high) return 1;
   if(direction==-1 && close<low) return -1;
   return 0;
}
#endif
#ifndef OBR_STRATEGY_BAR_TRAIL
#define OBR_STRATEGY_BAR_TRAIL
// Original R = actual fill to original stop. Only completed M1 closes advance peak.
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
input bool   EnableTrading       = true;  // Enable new trades (open trades stay protected when off)

input group " "
input group "==========  2 - Risk  =========="
input double RiskMoney           = 100.0; // Risk per trade, including costs (account currency)
input bool   UseFixedSize        = false; // Use fixed lots instead of the cash risk budget
input double FixedLots           = 1.0;   // Lots per trade when fixed sizing is enabled

input group "  "
input group "==========  3 - Trading Costs  =========="
input double CommissionPerLot    = 7.0;   // Fallback round-trip commission per lot until broker history is available
input int    SlippagePoints      = 3;     // Slippage/side (3=0.03 if broker Point=0.01)

// Frozen strategy rules: not user-facing options or alternate models.
const int ATRPeriod=10,ADXPeriod=15,OvernightStart=23,VaultAttemptCap=3;
const double NoiseFraction=0.30,StopPoints=75.0,TargetPoints=40.0,TargetR=3.0;
const int OBRMaxAttempts=2;
const double OBRRetryStopFraction=0.5;
const bool SyntheticFirst=false;
const OBRManagement Management=M1RStep;
const double BarTrailActivateR=1.5,BarTrailDistanceR=1.5;
const string OBRDisplayName="Overnight Bias Range Breakout";

input group "   "
input group "==========  4 - Display  =========="
input bool   ShowVisuals         = true;  // Show chart levels, trade markers and information

input group "    "
input group "==========  5 - Execution & Logging  =========="
const ulong  MagicNumber         = 91008012; // Fixed model identity; preserves owned-position recovery
input bool   WriteTradeLog       = false; // Write a detailed trade and decision log


// Private adapter to the existing, validated execution engine.
const int StartMinute=600;
const double RiskCash=RiskMoney,CommissionRTPerLot=CommissionPerLot,SlipPrice=SlippagePoints*_Point;
const ulong Magic=MagicNumber;
const bool DetailedLog=WriteTradeLog;
const OBRGeometry Geometry=SourceGeometry;
const double BreakEvenR=0.5,TrailR=1.0,BarTrailStepR=0.5;
string RunId="";
#endif

const string OBROutputFamily="OvernightBiasRangeBreakout";
const string OBROutputRoot=OBROutputFamily+"/logs";
void OBRPrepareOutput()
{
   FolderCreate(OBROutputFamily,FILE_COMMON);
   FolderCreate(OBROutputRoot,FILE_COMMON);
}

CTrade trade;
int atrHandle=INVALID_HANDLE, adxHandle=INVALID_HANDLE, ledger=INVALID_HANDLE;
ENUM_TIMEFRAMES sourceTF=PERIOD_M30;
datetime lastMinute=0,lastSignal=0,day=0;
double averageATR=0,orHigh=0,orLow=0,overnightHigh=0,overnightLow=0;
double initialRisk=0,modelCost=0,totalCost=0;
double modelCostPerLot=0,entryCommissionPerLot=0;
datetime ownedEntryTime=0;
int overnightExits=0;
string carryDetails="";
int bias=0,entries=0,daysReady=0,daysRejected=0,invalidBrackets=0,failedOrders=0;
int firstSide=0;
bool retryArmed=false;
datetime retryEligibleAfter=0;
bool rangeReady=false,armed=false,touched=false,waitingFresh=false,dayValid=false,beDone=false;
bool previousORAbove=false;
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
bool DateAllowed(datetime t)
{
   return true; // Tester dates belong to MT5; live operation has no research-stage lock.
}
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
      FileFlush(ledger); // Make decision events readable during visual tester diagnostics too.
   }
}
bool M1HistoryAvailable(datetime from)
{
   if(!MQLInfoInteger(MQL_TESTER) && !TerminalInfoInteger(TERMINAL_CONNECTED)) return false;
   long first=SeriesInfoInteger(_Symbol,PERIOD_M1,SERIES_FIRSTDATE);
   if(first<=0 || first>from || !SeriesInfoInteger(_Symbol,PERIOD_M1,SERIES_SYNCHRONIZED))
   {
      MqlRates preceding[];
      CopyRates(_Symbol,PERIOD_M1,from-86400,from,preceding); // Request the boundary's preceding history; retry asynchronously.
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
   // Native minute series omit minutes without a trade/quote. Use observed bars only.
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
   price=bars[0].open; // First observed trade in the nominal opening interval.
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
      PrintFormat("OBR DATA_WAIT day=%s reason=%s synchronized=%d firstM1=%s; retrying, protection active",
         TimeToString(day,TIME_DATE),reason,(int)SeriesInfoInteger(_Symbol,PERIOD_M1,SERIES_SYNCHRONIZED),
         TimeToString((datetime)SeriesInfoInteger(_Symbol,PERIOD_M1,SERIES_FIRSTDATE),TIME_DATE|TIME_MINUTES));
   }
   return false;
}
void ResetDay(datetime now)
{
   day=OBRDay(now); entries=0; firstSide=0; retryArmed=false; retryEligibleAfter=0; rangeReady=false; armed=false; touched=false;
   gtLastEntry=0;gtLastStop=0;gtLastTarget=0;gtLastBracketTime=0;
   if(OBRVisualEnabled()) ObjectsDeleteAll(0,OBRVisualPrefix());
   waitingFresh=false; previousORAbove=false; bias=0; beDone=false;
   virtualActive=false;virtualStart=0;virtualLastBar=0;
   lastSignal=0;averageATR=0;
   dayValid=false;dayRejected=false;dataWaitReason=""; // Preserve an outstanding history/connection wait across the day boundary.
}
// Model daily candles end at 17:00 New York, independent of broker midnight.
bool OBRATRMean(MqlRates &daily[],int period,double &mean)
{
   int n=ArraySize(daily);mean=0;
   if(n<period+15) return false;
   for(int end=n-1;end>=n-15;end--)
   {
      double atr=0;
      for(int i=end-period+1;i<=end;i++)
      {
         if(daily[i].high<daily[i].low || daily[i-1].close<=0) return false;
         double tr=MathMax(daily[i].high-daily[i].low,
            MathMax(MathAbs(daily[i].high-daily[i-1].close),MathAbs(daily[i].low-daily[i-1].close)));
         if(!MathIsValidNumber(tr)) return false;
         atr+=tr/period;
      }
      mean+=atr/15.0;
   }
   return mean>0 && MathIsValidNumber(mean);
}
bool OBRSessionATRMean(datetime sessionDay,double &mean)
{
   static datetime cachedDay=0;
   static double cachedMean=0;
   if(cachedDay==sessionDay && cachedMean>0){mean=cachedMean;return true;}
   // Preserve native results on feeds whose D1 candles already use the model close.
   bool aligned=true;
   for(int i=1;i<=ATRPeriod+15;i++)
   {
      datetime open=iTime(_Symbol,PERIOD_D1,i),ny=OBRNewYork(open);
      if(open<=0 || ny<=0 || (long)ny%86400!=17*3600){aligned=false;break;}
   }
   if(aligned)
   {
      double values[];mean=0;
      if(CopyBuffer(atrHandle,0,1,15,values)!=15) return false;
      for(int i=0;i<15;i++)
      {
         if(values[i]<=0 || !MathIsValidNumber(values[i])) return false;
         mean+=values[i]/15.0;
      }
   }
   else
   {
      // H1 OHLC preserves a daily high/low/close at an hour-aligned rollover.
      // Use M1 on fractional-hour server zones so no bar crosses that rollover.
      ENUM_TIMEFRAMES tf=clockServerNY%3600==0 ? PERIOD_H1 : PERIOD_M1;
      datetime finish=OBRSessionTime(sessionDay-86400,960);
      datetime begin=finish-90*86400;
      if(begin<=0 || finish<=begin) return false;
      MqlRates source[],daily[];ArraySetAsSeries(source,false);
      int count=CopyRates(_Symbol,tf,begin,finish-1,source);
      if(count<=0 || !SeriesInfoInteger(_Symbol,tf,SERIES_SYNCHRONIZED)) return false;
      datetime previous=0;
      for(int i=0;i<count;i++)
      {
         datetime local=OBRCentral(source[i].time);
         if(local<=0) return false;
         MqlDateTime d;TimeToStruct(local,d);
         bool afterClose=d.hour>=16;d.hour=0;d.min=0;d.sec=0;
         datetime key=StructToTime(d)+(afterClose ? 86400 : 0);
         if(key>=sessionDay) continue;
         if(key!=previous)
         {
            int n=ArraySize(daily);ArrayResize(daily,n+1);
            daily[n]=source[i];daily[n].time=key;previous=key;
         }
         else
         {
            int n=ArraySize(daily)-1;
            daily[n].high=MathMax(daily[n].high,source[i].high);
            daily[n].low=MathMin(daily[n].low,source[i].low);daily[n].close=source[i].close;
         }
      }
      // The first bucket may start partway through a day; only its closing price seeds TR.
      if(!OBRATRMean(daily,ATRPeriod,mean)) return false;
   }
   cachedDay=sessionDay;cachedMean=mean;return true;
}
bool NewDay(datetime now)
{
   averageATR=0;
   if(!OBRSessionATRMean(day,averageATR)) return RejectDay("NY_CLOSE_ATR_NOT_READY");
   datetime anchor=OBRSessionTime(day);
   if(Model==OvernightBiasRangeBreakout)
   {
      datetime begin=anchor-(OvernightStart==23 ? 3600 : 0);
      datetime end=anchor+510*60;
      MqlRates b[];
      if(now<end || !Window(begin,end,b,(int)((end-begin)/60))) return RejectDay("OVERNIGHT_WINDOW_INCOMPLETE");
      overnightHigh=b[0].high;overnightLow=b[0].low;
      for(int j=1;j<ArraySize(b);j++) {overnightHigh=MathMax(overnightHigh,b[j].high);overnightLow=MathMin(overnightLow,b[j].low);}
      double openingPrice=0;
      if(!OpeningPrice(end,15,openingPrice)) return RejectDay("BIAS_OPEN_UNAVAILABLE");
      bias=OBRBias(openingPrice,overnightLow,overnightHigh);
      if(!MQLInfoInteger(MQL_TESTER))
         PrintFormat("OBR_BIAS_INPUTS day=%s source=%s overnightFromServer=%s overnightToServer=%s openingWindowServer=%s open=%.10f nightLow=%.10f nightHigh=%.10f bias=%d serverAheadOfNYMinutes=%d",
            TimeToString(day,TIME_DATE),_Symbol,TimeToString(begin,TIME_DATE|TIME_SECONDS),TimeToString(end,TIME_DATE|TIME_SECONDS),
            TimeToString(end,TIME_DATE|TIME_SECONDS),openingPrice,overnightLow,overnightHigh,bias,clockServerNY/60);
   }
   Note("DAY",StringFormat("ATR=%f overnight=%f/%f bias=%d",averageATR,overnightLow,overnightHigh,bias));
   if(dayWaiting && !MQLInfoInteger(MQL_TESTER))
      lastSignal=MathMax(lastSignal,iTime(_Symbol,sourceTF,1));
   if(dayRejected){daysRejected--;dayRejected=false;}
   dayWaiting=false;dataWaitReason="";
   return true;
}
bool SetRange(datetime now)
{
   if(Model==VaultBreak) return true;
   int minutes=(Model==OvernightBiasRangeBreakout ? 15 : 30);
   datetime from=OBRSessionTime(day,510), end=from+minutes*60;
   if(now<end) return false;
   MqlRates b[];
   if(!Window(from,end,b,minutes)) return false;
   orHigh=b[0].high;orLow=b[0].low;
   for(int i=1;i<ArraySize(b);i++){orHigh=MathMax(orHigh,b[i].high);orLow=MathMin(orLow,b[i].low);}
   Note("RANGE",StringFormat("%f/%f",orLow,orHigh));
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
bool Open(int side,double stop,double target,double distance)
{
   MqlTick q;if(!SymbolInfoTick(_Symbol,q)) return false;
   double entry=(side==1 ? q.ask : q.bid);
   if(distance>0) {stop=entry-side*distance;target=entry+side*distance*TargetR;}
   stop=RoundPrice(stop);target=RoundPrice(target);
   if(!OBRBracket(side,entry,stop,target)) {invalidBrackets++;Note("INVALID_BRACKET",StringFormat("%d %f %f %f",side,entry,stop,target));return false;}
   double loss=0;
   if(!OrderCalcProfit(side==1 ? ORDER_TYPE_BUY : ORDER_TYPE_SELL,_Symbol,1.0,entry,stop,loss)) return false;
   double commission=OBRCommissionEstimate();
   double friction=commission;
   double slipLoss=0;
   if(!OrderCalcProfit(ORDER_TYPE_BUY,_Symbol,1.0,entry,entry-2*SlipPrice,slipLoss)) return false;
   friction+=MathAbs(slipLoss);
   double unitRisk=MathAbs(loss)+friction;
   double step=SymbolInfoDouble(_Symbol,SYMBOL_VOLUME_STEP),minimum=SymbolInfoDouble(_Symbol,SYMBOL_VOLUME_MIN);
   if(unitRisk<=0 || !MathIsValidNumber(unitRisk) || step<=0 || minimum<=0) {Note("RISK_INVALID","Invalid cash risk or volume contract");return false;}
   double volume=OBRRequestedVolume(UseFixedSize,FixedLots,RiskCash,unitRisk,step,minimum,SymbolInfoDouble(_Symbol,SYMBOL_VOLUME_MAX));
   if(volume<minimum || unitRisk<=0) {Note(UseFixedSize ? "FIXED_LOTS_INVALID" : "RISK_TOO_SMALL",DoubleToString(unitRisk*minimum,2));return false;}
   double stops=SymbolInfoInteger(_Symbol,SYMBOL_TRADE_STOPS_LEVEL)*_Point;
   if(side*(entry-stop)<stops || side*(target-entry)<stops){invalidBrackets++;return false;}
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
   initialRisk=MathAbs(trade.ResultPrice()-stop);beDone=false;barPeakR=0;runtimeTrailLastBar=0;
   double filledVolume=trade.ResultVolume()>0 ? trade.ResultVolume() : volume;
   entryCommissionPerLot=commission;modelCostPerLot=friction;modelCost=friction*filledVolume;totalCost+=modelCost;ownedEntryTime=TimeCurrent();
   entries++;
   if(entries==1) firstSide=side;
   retryArmed=false;retryEligibleAfter=0;
   worstRisk=MathMax(worstRisk,unitRisk*volume);
   OBRRememberBracket(trade.ResultPrice(),stop,target);
   OBRDrawFill(side,TimeCurrent(),trade.ResultPrice(),"Entry");
   OBRRuntimeFilled(side,stop,friction);
   Note("ENTRY",StringFormat("model=%d side=%d lots=%f fill=%f sl=%f tp=%f atr=%f count=%d modeledCost=%f",Model,side,volume,trade.ResultPrice(),stop,target,averageATR,entries,modelCost));
   return true;
}
void Manage()
{
   if(Management==M1RStep) return;
   if(Management<CostCoveredBE || !OwnPosition() || initialRisk<=0) return;
   MqlTick q;SymbolInfoTick(_Symbol,q);
   int side=(PositionGetInteger(POSITION_TYPE)==POSITION_TYPE_BUY ? 1 : -1);
   double entry=PositionGetDouble(POSITION_PRICE_OPEN),sl=PositionGetDouble(POSITION_SL),tp=PositionGetDouble(POSITION_TP);
   double current=(side==1 ? q.bid : q.ask);
   double favorable=side*(current-entry);
   double desired=sl;
   if(Management==CostCoveredBE && !beDone && favorable>=BreakEvenR*initialRisk)
   {
      double volume=PositionGetDouble(POSITION_VOLUME),unit=0;
      if(!OrderCalcProfit(ORDER_TYPE_BUY,_Symbol,1.0,entry,entry+1,unit)) return;
      desired=entry+side*(unit!=0 ? modelCost/volume/MathAbs(unit) : 0);
   }
   if(Management==ATRTrail && favorable>=TrailR*initialRisk) desired=current-side*averageATR*NoiseFraction;
   desired=RoundPrice(desired);
   if(side*(desired-sl)<=0 || side*(current-desired)<=SymbolInfoInteger(_Symbol,SYMBOL_TRADE_STOPS_LEVEL)*_Point) return;
   if(trade.PositionModify((ulong)PositionGetInteger(POSITION_TICKET),desired,tp)) {beDone=true;Note("STOP_CHANGE",DoubleToString(desired,_Digits));}
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
   // Entry slippage is already in the fill; reserve only exit slippage here.
   double commission=MathMax(entryCommissionPerLot,2*OBREntryChargesPerLot((ulong)PositionGetInteger(POSITION_IDENTIFIER)));
   if(!OBRCostCoveredPrice(side,entry,commission,covered)){Note("M1_COST_INVALID","Cannot calculate a cost-covered stop");return;}
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
   if(!OBRCostCoveredPrice(side,entry,commission,covered)){Note("RETRY_COST_INVALID","Cannot calculate retry cost-covered stop");return;}
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
   if(!OBRBarTrailSelfTest() || !OBRCalendarSelfTest() || !OBRSignalTimeSelfTest()) {Print("OBR_CALC_SELFTEST_FAILED");return INIT_FAILED;}
   Print("OBR_CALC_SELFTEST_PASS virtual collision/equality and M1 R milestones");
   RunId=OBROutputFamily+(MQLInfoInteger(MQL_TESTER) ? "_test_"+IntegerToString((long)GetTickCount64()) : "_trades");
   if(ATRPeriod<2 || ADXPeriod<2 || (!UseFixedSize && RiskCash<=0) || (UseFixedSize && FixedLots<=0) || NoiseFraction<=0 || TargetR<=0 || StopPoints<=0 || TargetPoints<=0 || CommissionRTPerLot<0 || SlipPrice<0 || Magic==0) return INIT_PARAMETERS_INCORRECT;
   if(VaultAttemptCap<2 || VaultAttemptCap>4 || (SyntheticFirst && (Model!=VaultBreak || Geometry!=SourceGeometry || Management!=FixedBracket))) return INIT_PARAMETERS_INCORRECT;
   if(Management==M1RStep && (Model!=OvernightBiasRangeBreakout || BarTrailActivateR<=0 || BarTrailDistanceR<=0 || BarTrailStepR<=0)) return INIT_PARAMETERS_INCORRECT;
   OBRClockRefresh(TimeCurrent());
   sourceTF=PERIOD_M15;
   // The visual tester otherwise auto-attaches these handles, duplicating our explicit ADX pane.
   TesterHideIndicators(true);
   atrHandle=iATR(_Symbol,PERIOD_D1,ATRPeriod);
   adxHandle=iADXWilder(_Symbol,sourceTF,ADXPeriod);
   if(atrHandle==INVALID_HANDLE || adxHandle==INVALID_HANDLE) return INIT_FAILED;
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
      if(ledger==INVALID_HANDLE){Print("OBR_LOG_BLOCK: requested ledger unavailable");return INIT_FAILED;}
      if(!append || FileSize(ledger)==0) FileWrite(ledger,"server_time","event","detail");
      else FileSeek(ledger,0,SEEK_END);
   }
   PrintFormat("%s ready | new trades=%s | %s | clock=automatic server/GMT or tester weekly anchor",OBRDisplayName,EnableTrading ? "enabled" : "disabled",OBRSizingLabel());
   PrintFormat("OBR_SPEC tick=%f tickvalue=%f minlot=%f contract=%f",SymbolInfoDouble(_Symbol,SYMBOL_TRADE_TICK_SIZE),SymbolInfoDouble(_Symbol,SYMBOL_TRADE_TICK_VALUE),SymbolInfoDouble(_Symbol,SYMBOL_VOLUME_MIN),SymbolInfoDouble(_Symbol,SYMBOL_TRADE_CONTRACT_SIZE));
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
   Manage();
   if(!OBRRuntimeHistoryReady()) return; // Timer and broker protection remain active while loading.
   datetime m=iTime(_Symbol,PERIOD_M1,0);
   if(m==lastMinute) return;lastMinute=m;
#ifdef OBR_VISUAL_PROBE
   int completed=0;for(int y=0;y<7;y++) completed+=yearTrades[y];
   if(completed>=1)
   {
      string snapshot="OvernightBiasRangeBreakoutPreview_"+OBROutputFamily+".png";
      if(!gtPreviewRequested)
      {
         OBRDraw();OBRVisualFit();
         ResetLastError();
         gtPreviewRequested=ChartScreenShot(0,snapshot,1280,800,ALIGN_RIGHT);
         gtPreviewRequestedMs=GetTickCount64();
         PrintFormat("OBR_VISUAL_DIAG requested=%d error=%d chart=%I64d width=%d height=%d visual=%d path=%s",gtPreviewRequested,GetLastError(),ChartID(),ChartGetInteger(0,CHART_WIDTH_IN_PIXELS),ChartGetInteger(0,CHART_HEIGHT_IN_PIXELS),MQLInfoInteger(MQL_VISUAL_MODE),TerminalInfoString(TERMINAL_DATA_PATH));
         if(!gtPreviewRequested){Print("OBR_VISUAL_SCREENSHOT_FAILED");TesterStop();}
      }
      else if(FileIsExist(snapshot) || GetTickCount64()-gtPreviewRequestedMs>5000)
      {Print(FileIsExist(snapshot) ? "OBR_VISUAL_SCREENSHOT_PASS" : "OBR_VISUAL_SCREENSHOT_TIMEOUT");TesterStop();}
      return;
   }
#endif
   ManageClosedMinute();
   if(virtualActive && OBRDay(now)!=day){virtualActive=false;virtualExpired++;}
   if(virtualActive)
   {
      MqlRates closed[];ArraySetAsSeries(closed,false);
      datetime begin=virtualLastBar>0 ? virtualLastBar+60 : virtualStart;
      if(!M1HistoryAvailable(begin)) return; // A download can retry without erasing attempt state.
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
   // No indicator/CopyRates calls before source windows are complete.
   int prepare=(Model==OvernightBiasRangeBreakout ? 510 : 1);
   if(minute<prepare) return;
   if(OBRDay(now)!=day)
   {
      ResetDay(now);
      OBRStateRestore(now); // Restore owned exposure/attempts once, never on preparation retries.
   }
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
         PrintFormat("OBR DATA_WAIT: %s; firstM1=%s requiredFrom=%s; new entries paused, broker protection retained",reason,
            TimeToString((datetime)SeriesInfoInteger(_Symbol,PERIOD_M1,SERIES_FIRSTDATE),TIME_DATE|TIME_SECONDS),
            TimeToString(OBRSessionTime(day)-3600,TIME_DATE|TIME_SECONDS));
      }
      return;
   }
   if(!rangeReady)
   {
      rangeReady=SetRange(now);
      if(!rangeReady)
      {
         if(minute>=525)
         {
            dayWaiting=true;
            if(dataWaitReason!="OPENING_RANGE_WAIT")
            {dataWaitReason="OPENING_RANGE_WAIT";Print("OBR DATA_WAIT: opening-range native bars unavailable; retrying");}
         }
         return;
      }
   }
   if(dayWaiting && !MQLInfoInteger(MQL_TESTER))
      lastSignal=MathMax(lastSignal,iTime(_Symbol,sourceTF,1));
   dayWaiting=false;dataWaitReason="";
   MqlRates b[];ArraySetAsSeries(b,true);
   int count=2;
   if(CopyRates(_Symbol,sourceTF,1,count,b)!=count || b[0].time+PeriodSeconds(sourceTF)>m) return;
   if(b[0].time<=lastSignal) return;lastSignal=b[0].time; // Older or already-consumed candles cannot replay.
   OBRStateSave();
   if(!MQLInfoInteger(MQL_TESTER) && runtimeBlocked) return;
   // Eligibility belongs to the confirming candle's close, not merely the current quote time.
   if(!OBRSignalCloseEligible(OBRMinute(b[0].time+PeriodSeconds(sourceTF)))) return;
   int start=555;
   int cap=OBRMaxAttempts;
   if(!EnableTrading) return; // Signal timestamp has been consumed; owned-trade management already ran.
   if(!OBRCanEnter(minute,start,cutoff,entries,OBRMaxAttempts) || OwnPosition() || virtualActive) return;
   double adx[];ArraySetAsSeries(adx,true);
   if(CopyBuffer(adxHandle,0,1,2,adx)!=2) return;
   int side=0;double stop=0,target=0,distance=0;
   side=OBRSignalSide(2,bias,b[0].close,orHigh,orLow,adx[0]);
   if(entries==1 && (!retryArmed || side!=firstSide || b[0].time<=retryEligibleAfter)) return;
   double attemptDistance=NoiseFraction*averageATR*(entries==1 ? OBRRetryStopFraction : 1.0);
   distance=attemptDistance;
   if(side!=0)
   {
      Note("SIGNAL",StringFormat("sourceOpen=%s sourceClose=%s close=%f rangeLow=%f rangeHigh=%f adx=%f bias=%d side=%d",
           TimeToString(b[0].time,TIME_DATE|TIME_SECONDS),TimeToString(b[0].time+PeriodSeconds(sourceTF),TIME_DATE|TIME_SECONDS),
           b[0].close,orLow,orHigh,adx[0],bias,side));
      Open(side,stop,target,distance);
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
   // Broker-net already contains exit charges; include allocated entry charges once.
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
   FileWrite(out,Model,net,pf,adjustedDD,TesterStatistics(STAT_TRADES),daysReady,daysRejected,invalidBrackets,failedOrders,worstRisk);
   FileClose(out);
   bool exported=!DetailedLog || OBRExportTrades(path+".trades.csv");
   PrintFormat("%s TESTER_RESULT net=%.2f PF=%.6f trades=%d export=%s path=%s",OBRDisplayName,net,pf,(int)TesterStatistics(STAT_TRADES),!DetailedLog ? "DISABLED" : exported ? "PASS" : "FAIL",path);
   return exported && failedOrders==0 && invalidBrackets==0 ? (adjustedDD>0 ? net/adjustedDD : 0) : -1000000;
}
void OnDeinit(const int reason)
{
   OBRStateSave();EventKillTimer();
   if(runtimeLockFile!=INVALID_HANDLE) FileClose(runtimeLockFile);
   // A timeframe reload must not erase the session levels while history warms up.
   if(reason!=REASON_CHARTCHANGE || ChartSymbol(0)!=_Symbol || !ShowVisuals)
      ObjectsDeleteAll(0,OBRVisualPrefix());
   if(gtPane!=INVALID_HANDLE)
   {
      for(int window=(int)ChartGetInteger(0,CHART_WINDOWS_TOTAL)-1;window>=1;window--) ChartIndicatorDelete(0,window,gtPaneName);
      IndicatorRelease(gtPane);
   }
   if(ledger!=INVALID_HANDLE) FileClose(ledger);
   if(atrHandle!=INVALID_HANDLE) IndicatorRelease(atrHandle);
   if(adxHandle!=INVALID_HANDLE) IndicatorRelease(adxHandle);
}
