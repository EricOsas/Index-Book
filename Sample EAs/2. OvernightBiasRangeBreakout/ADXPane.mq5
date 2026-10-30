//+------------------------------------------------------------------+
//|                                                      ADXPane.mq5 |
//| Copyright 2026-2027, Osamwonyi Eric (You_FoundEric)              |
//| https://x.com/You_FoundEric                                      |
//+------------------------------------------------------------------+
#property copyright   "Copyright 2026-2027, Osamwonyi Eric (You_FoundEric)"
#property link        "https://x.com/You_FoundEric"
#property strict
#property indicator_separate_window
#property indicator_buffers 2
#property indicator_plots 1
#property indicator_type1 DRAW_COLOR_HISTOGRAM
#property indicator_color1 clrDimGray,clrSeaGreen
#property indicator_width1 2
#property indicator_label1 "Trend strength"
#property indicator_minimum 0
#property indicator_level1 20
#property indicator_levelcolor clrOrange
input int PeriodADX=15;
input string Caption="Overnight Bias trend strength";
double values[],colors[];
int handle=INVALID_HANDLE;
datetime SourceTime(datetime chartBar)
{
   int seconds=PeriodSeconds(_Period);
   return seconds<=15*60 ? chartBar : (datetime)MathMin((double)TimeCurrent(),(double)(chartBar+seconds-1));
}
int OnInit()
{
   SetIndexBuffer(0,values,INDICATOR_DATA);ArraySetAsSeries(values,true);
   SetIndexBuffer(1,colors,INDICATOR_COLOR_INDEX);ArraySetAsSeries(colors,true);
   IndicatorSetString(INDICATOR_SHORTNAME,Caption);
   handle=iADXWilder(_Symbol,PERIOD_M15,PeriodADX); // Model source remains M15 on every viewed chart timeframe.
   return handle==INVALID_HANDLE ? INIT_FAILED : INIT_SUCCEEDED;
}
int OnCalculate(const int total,const int previous,const datetime &time[],const double &open[],const double &high[],const double &low[],const double &close[],const long &tick_volume[],const long &volume[],const int &spread[])
{
   int available=BarsCalculated(handle);
   if(total<2 || available<PeriodADX+2) return previous;
   ArraySetAsSeries(time,true);
   static int previousAvailable=0;
   bool rebuild=previous==0 || previous>total || available>previousAvailable+1 || available<previousAvailable;
   int count=rebuild ? total : MathMin(total,total-previous+2);
   datetime priorSource=iTime(_Symbol,PERIOD_M15,1);
   int refreshThrough=iBarShift(_Symbol,_Period,priorSource,false);
   if(refreshThrough>=0) count=MathMax(count,MathMin(total,refreshThrough+1));
   int oldest=iBarShift(_Symbol,PERIOD_M15,SourceTime(time[count-1]),false);
   int need=oldest<0 ? available : MathMin(available,oldest+1);
   double source[];ArraySetAsSeries(source,true);
   if(CopyBuffer(handle,0,0,need,source)!=need) return previous;
   for(int i=0;i<count;i++)
   {
      int shift=iBarShift(_Symbol,PERIOD_M15,SourceTime(time[i]),false);
      values[i]=shift>=0 && shift<need ? source[shift] : EMPTY_VALUE;
      colors[i]=values[i]!=EMPTY_VALUE && values[i]>20 ? 1 : 0;
   }
   previousAvailable=available;
   return total;
}
void OnDeinit(const int reason){if(handle!=INVALID_HANDLE) IndicatorRelease(handle);}
