#ifndef FLASH_STRATEGY_VISUALS
#define FLASH_STRATEGY_VISUALS
long gtVisualChart=0;
int gtPane=INVALID_HANDLE;
string gtPaneName="";
double gtLastEntry=0,gtLastStop=0,gtLastTarget=0;
datetime gtLastBracketTime=0;
void FlashRememberBracket(double entry,double stop,double target){gtLastEntry=entry;gtLastStop=stop;gtLastTarget=target;gtLastBracketTime=ownedEntryTime>0 ? ownedEntryTime : TimeCurrent();}
void FlashVisualFit(datetime reference=0)
{
   double low=DBL_MAX,high=-DBL_MAX;
   MqlRates b[];
   int n=reference>0 ? CopyRates(_Symbol,sourceTF,reference,96,b) : CopyRates(_Symbol,sourceTF,0,96,b);
   for(int i=0;i<n;i++){low=MathMin(low,b[i].low);high=MathMax(high,b[i].high);}
   double levels[]={orHigh,orLow,overnightHigh,overnightLow,gtLastEntry,gtLastStop,gtLastTarget,
                    virtualActive ? virtualStop : 0,virtualActive ? virtualTarget : 0};
   for(int i=0;i<ArraySize(levels);i++) if(levels[i]>0){low=MathMin(low,levels[i]);high=MathMax(high,levels[i]);}
   if(high<=low || low==DBL_MAX) return;
   double pad=(high-low)*.08;
   ChartSetInteger(gtVisualChart,CHART_SCALEFIX,true);
   ChartSetDouble(gtVisualChart,CHART_FIXED_MIN,low-pad);ChartSetDouble(gtVisualChart,CHART_FIXED_MAX,high+pad);
   ChartSetInteger(gtVisualChart,CHART_SCALE,3);ChartNavigate(gtVisualChart,CHART_END,reference>0 ? -iBarShift(_Symbol,sourceTF,reference,false) : 0);
   ChartRedraw(gtVisualChart);
}
string FlashVisualPrefix(){return "FLASH_"+IntegerToString((long)Magic)+"_";}
datetime FlashVisualDay(datetime now){return FlashDay(now)+(FlashMinute(now)>=23*60 ? 86400 : 0);}
bool FlashVisualEnabled(){return ShowVisuals && (!MQLInfoInteger(MQL_TESTER) || MQLInfoInteger(MQL_VISUAL_MODE));}
bool gtVisualSuspended=false;
string FlashVisualFault()
{
   bool tester=MQLInfoInteger(MQL_TESTER);
   if(!tester && !TerminalInfoInteger(TERMINAL_CONNECTED))return "Disconnected | waiting for server";
   if(!tester && !TerminalInfoInteger(TERMINAL_TRADE_ALLOWED))return "Terminal trading disabled";
   if(!tester && !MQLInfoInteger(MQL_TRADE_ALLOWED))return "EA trading permission disabled";
   if(!tester && !AccountInfoInteger(ACCOUNT_TRADE_ALLOWED))return "Server account trading disabled";
   if(!tester && !FlashRuntimeAccountContextValid())return "Account context changed | trading blocked";
   if(runtimeCloseRejected)return "Close rejected | retry paused";
   if(runtimeCloseAttention)return "Close unconfirmed | recovery pending";
   if(runtimeRecoveryHold)return "Recovery needs attention | broker SL/TP retained";
   if(runtimeBlocked)return "Recovery / server order error | trading blocked";
   if(!clockReady)return "Clock history loading";
   if(!SeriesInfoInteger(_Symbol,PERIOD_M1,SERIES_SYNCHRONIZED) || iTime(_Symbol,PERIOD_M1,1)<=0)
      return "M1 history unavailable";
   if(!tester && !runtimeHistoryReady)return "History / indicators loading";
   if(dayWaiting && dataWaitReason!="")return "Data unavailable | "+dataWaitReason;
   return "";
}
bool FlashVisualDataEnabled(){return FlashVisualEnabled() && FlashVisualFault()=="";}
void FlashVisualSuspend(bool blocked)
{
   if(blocked==gtVisualSuspended)return;
   gtVisualSuspended=blocked;
   // Preserve chart history for restoration; touch only this EA's own namespace.
   string prefix=FlashVisualPrefix(),panel=FlashPanelPrefix();
   for(int index=ObjectsTotal(gtVisualChart,-1,-1)-1;index>=0;index--)
   {
      string name=ObjectName(gtVisualChart,index,-1,-1);
      if(StringFind(name,prefix)!=0 || StringFind(name,panel)==0)continue;
      ObjectSetInteger(gtVisualChart,name,OBJPROP_TIMEFRAMES,blocked ? OBJ_NO_PERIODS : OBJ_ALL_PERIODS);
   }
   if(gtPaneName!="")
   {
      if(blocked)
      {
         for(int window=(int)ChartGetInteger(gtVisualChart,CHART_WINDOWS_TOTAL)-1;window>=1;window--)
            ChartIndicatorDelete(gtVisualChart,window,gtPaneName);
      }
      else if(gtPane!=INVALID_HANDLE)
         ChartIndicatorAdd(gtVisualChart,(int)ChartGetInteger(gtVisualChart,CHART_WINDOWS_TOTAL),gtPane);
   }
   ChartRedraw(gtVisualChart);
}
void FlashInitVisual()
{
   if(!FlashVisualEnabled()) return;
   string oldRows[]={"Title","Clock","Metrics","State"};
   for(int i=0;i<ArraySize(oldRows);i++)ObjectDelete(gtVisualChart,FlashVisualPrefix()+oldRows[i]);
   ChartSetInteger(gtVisualChart,CHART_EVENT_OBJECT_CREATE,true);

   // Remove only objects owned by this EA under its former object namespace.
   ObjectsDeleteAll(gtVisualChart,"GT_"+IntegerToString((long)Magic)+"_");
   // Repair retained owned drawings from builds that used the foreground layer.
   for(int i=ObjectsTotal(gtVisualChart,-1,-1)-1;i>=0;i--)
   {
      string name=ObjectName(gtVisualChart,i,-1,-1);
      if(StringFind(name,FlashVisualPrefix())==0 && StringFind(name,FlashPanelPrefix())!=0)
      {
         ObjectSetInteger(gtVisualChart,name,OBJPROP_BACK,true);
         if(ObjectGetInteger(gtVisualChart,name,OBJPROP_TYPE)==OBJ_RECTANGLE)
         {ObjectSetInteger(gtVisualChart,name,OBJPROP_FILL,false);ObjectSetInteger(gtVisualChart,name,OBJPROP_WIDTH,1);}
      }
   }
   ObjectsDeleteAll(gtVisualChart,FlashVisualPrefix()+"VWAP");
   ObjectDelete(gtVisualChart,FlashVisualPrefix()+"Session Opening Price (SOP)");
   ObjectDelete(gtVisualChart,FlashVisualPrefix()+"Noise Up Barrier (NUB)");
   ChartSetInteger(gtVisualChart,CHART_MODE,CHART_CANDLES);
   ChartSetInteger(gtVisualChart,CHART_COLOR_BACKGROUND,clrBlack);
   ChartSetInteger(gtVisualChart,CHART_COLOR_FOREGROUND,clrSilver);
   ChartSetInteger(gtVisualChart,CHART_COLOR_CANDLE_BULL,clrWhite);
   ChartSetInteger(gtVisualChart,CHART_COLOR_CANDLE_BEAR,clrDarkOrange);
   ChartSetInteger(gtVisualChart,CHART_COLOR_CHART_UP,clrWhite);
   ChartSetInteger(gtVisualChart,CHART_COLOR_CHART_DOWN,clrDarkOrange);
   ChartSetInteger(gtVisualChart,CHART_SHOW_GRID,false);
   ChartSetInteger(gtVisualChart,CHART_AUTOSCROLL,true);ChartSetInteger(gtVisualChart,CHART_SHIFT,true);
   if(Model==FlashTriggerORB)
   {
      gtPaneName=FlashOutputFamily+" ADX "+IntegerToString((long)Magic);
      for(int window=(int)ChartGetInteger(gtVisualChart,CHART_WINDOWS_TOTAL)-1;window>=1;window--)
         for(int index=ChartIndicatorsTotal(gtVisualChart,window)-1;index>=0;index--)
            if(ChartIndicatorName(gtVisualChart,window,index)==gtPaneName) ChartIndicatorDelete(gtVisualChart,window,gtPaneName);
      gtPane=iCustom(_Symbol,(ENUM_TIMEFRAMES)Period(),"::ADXPane.ex5",ADXPeriod,gtPaneName);
      if(gtPane!=INVALID_HANDLE && !ChartIndicatorAdd(gtVisualChart,(int)ChartGetInteger(gtVisualChart,CHART_WINDOWS_TOTAL),gtPane))
         PrintFormat("FLASH_DISPLAY_ADX_ADD_FAILED error=%d",GetLastError());
   }
}

const int PX=10,PY=20,PW=210,PH=198,HH=32;
bool flashPanelCollapsed=false,flashPanelRestack=false;
string FlashPanelPrefix(){return FlashVisualPrefix()+"PANEL_";}
void FlashPanelRect(string name, int x, int y, int w, int h, color bg, color bdr)
{
    if(ObjectFind(gtVisualChart, name) < 0)
    {
        ObjectCreate(gtVisualChart, name, OBJ_RECTANGLE_LABEL, 0, 0, 0);
        ObjectSetInteger(gtVisualChart, name, OBJPROP_CORNER,     CORNER_LEFT_UPPER);
        ObjectSetInteger(gtVisualChart, name, OBJPROP_SELECTABLE, false);
        ObjectSetInteger(gtVisualChart, name, OBJPROP_HIDDEN,     true);
        ObjectSetInteger(gtVisualChart, name, OBJPROP_BACK,       false);  // foreground â€” blocks chart
    }
    ObjectSetInteger(gtVisualChart, name, OBJPROP_ZORDER,     10000);
    ObjectSetInteger(gtVisualChart, name, OBJPROP_BACK,       false);
    ObjectSetInteger(gtVisualChart, name, OBJPROP_XDISTANCE,   x);
    ObjectSetInteger(gtVisualChart, name, OBJPROP_YDISTANCE,   y);
    ObjectSetInteger(gtVisualChart, name, OBJPROP_XSIZE,       w);
    ObjectSetInteger(gtVisualChart, name, OBJPROP_YSIZE,       h);
    ObjectSetInteger(gtVisualChart, name, OBJPROP_BGCOLOR,     (long)bg);
    ObjectSetInteger(gtVisualChart, name, OBJPROP_COLOR,       (long)bdr);
    ObjectSetInteger(gtVisualChart, name, OBJPROP_BORDER_TYPE, BORDER_FLAT);
}

void FlashPanelLbl(string name, int x, int y, string text, int fs, color clr)
{
    if(ObjectFind(gtVisualChart, name) < 0)
    {
        ObjectCreate(gtVisualChart, name, OBJ_LABEL, 0, 0, 0);
        ObjectSetInteger(gtVisualChart, name, OBJPROP_CORNER,     CORNER_LEFT_UPPER);
        ObjectSetInteger(gtVisualChart, name, OBJPROP_SELECTABLE, false);
        ObjectSetInteger(gtVisualChart, name, OBJPROP_HIDDEN,     true);
        ObjectSetInteger(gtVisualChart, name, OBJPROP_BACK,       false);
        ObjectSetString(gtVisualChart,  name, OBJPROP_FONT,       "Segoe UI Semibold");
    }
    ObjectSetInteger(gtVisualChart, name, OBJPROP_ZORDER,     10001);
    if((int)ObjectGetInteger(gtVisualChart, name, OBJPROP_XDISTANCE) != x) ObjectSetInteger(gtVisualChart, name, OBJPROP_XDISTANCE, x);
    if((int)ObjectGetInteger(gtVisualChart, name, OBJPROP_YDISTANCE) != y) ObjectSetInteger(gtVisualChart, name, OBJPROP_YDISTANCE, y);
    if((int)ObjectGetInteger(gtVisualChart, name, OBJPROP_FONTSIZE)  != fs) ObjectSetInteger(gtVisualChart, name, OBJPROP_FONTSIZE, fs);
    if(ObjectGetInteger(gtVisualChart, name, OBJPROP_COLOR) != (long)clr) ObjectSetInteger(gtVisualChart, name, OBJPROP_COLOR, (long)clr);
    if(ObjectGetString(gtVisualChart, name, OBJPROP_TEXT) != text) ObjectSetString(gtVisualChart, name, OBJPROP_TEXT, text);
}

void FlashPanelShell()
{
    if(!FlashVisualEnabled()) return;
    int height=flashPanelCollapsed ? HH : PH;
    FlashPanelRect(FlashPanelPrefix()+"SHD2", PX+5, PY+5, PW, height, C'6,6,6',    C'6,6,6');
    FlashPanelRect(FlashPanelPrefix()+"SHD1", PX+2, PY+2, PW, height, C'12,12,12', C'12,12,12');
    FlashPanelRect(FlashPanelPrefix()+"BG",   PX,   PY,   PW, height, clrBlack,    C'52,68,92');
    FlashPanelRect(FlashPanelPrefix()+"HDR",  PX,   PY,   PW, HH, C'18,22,28', C'52,68,92');
    FlashPanelRect(FlashPanelPrefix()+"HAIR", PX,   PY+HH, PW, 1, C'52,68,92', C'52,68,92');
    FlashPanelLbl(FlashPanelPrefix()+"TTL",   PX+8, PY+8, "Flash Trigger ORB", 9, C'190,210,236');
    string button=FlashPanelPrefix()+"BTN";
    if(ObjectFind(gtVisualChart,button)<0)
    {
        ObjectCreate(gtVisualChart,button,OBJ_BUTTON,0,0,0);
        ObjectSetInteger(gtVisualChart,button,OBJPROP_CORNER,CORNER_LEFT_UPPER);
        ObjectSetInteger(gtVisualChart,button,OBJPROP_HIDDEN,true);
        ObjectSetString(gtVisualChart,button,OBJPROP_FONT,"Segoe UI Semibold");
    }
    ObjectSetInteger(gtVisualChart,button,OBJPROP_XDISTANCE,PX+PW-29);
    ObjectSetInteger(gtVisualChart,button,OBJPROP_ZORDER,10002);
    ObjectSetInteger(gtVisualChart,button,OBJPROP_YDISTANCE,PY+5);
    ObjectSetInteger(gtVisualChart,button,OBJPROP_XSIZE,22);
    ObjectSetInteger(gtVisualChart,button,OBJPROP_YSIZE,22);
    ObjectSetInteger(gtVisualChart,button,OBJPROP_FONTSIZE,12);
    ObjectSetInteger(gtVisualChart,button,OBJPROP_COLOR,(long)C'190,210,236');
    ObjectSetInteger(gtVisualChart,button,OBJPROP_BGCOLOR,(long)C'18,22,28');
    ObjectSetInteger(gtVisualChart,button,OBJPROP_BORDER_COLOR,(long)C'52,68,92');
    ObjectSetInteger(gtVisualChart,button,OBJPROP_STATE,false);
    ObjectSetString(gtVisualChart,button,OBJPROP_TEXT,flashPanelCollapsed ? "+" : "-");
    if(flashPanelCollapsed)
    {
        string rows[]={"SRV","HI","LO","SZ","BUY","SEL","CST","COV","SL"};
        for(int i=0;i<ArraySize(rows);i++) ObjectDelete(gtVisualChart,FlashPanelPrefix()+rows[i]);
    }
}

void FlashPanelRows(datetime clock,string state,double adx,double nub)
{
   if(flashPanelRestack){flashPanelRestack=false;ObjectsDeleteAll(gtVisualChart,FlashPanelPrefix());}
   FlashPanelShell();
   if(flashPanelCollapsed)return;
   string p=FlashPanelPrefix();
   FlashPanelLbl(p+"SRV",PX+8,PY+40,clockReady ? "NY: "+TimeToString(FlashNewYork(clock),TIME_SECONDS)+" CT: "+TimeToString(FlashCentral(clock),TIME_SECONDS) : "Clock history loading",8,C'140,158,185');
   FlashPanelLbl(p+"HI",PX+8,PY+59,"Range high: "+(bias!=0 && orHigh>0?DoubleToString(orHigh,2):"--"),8,C'120,210,150');
   FlashPanelLbl(p+"LO",PX+8,PY+76,"Range low: "+(bias!=0 && orLow>0?DoubleToString(orLow,2):"--"),8,C'210,100,100');
   FlashPanelLbl(p+"SZ",PX+8,PY+93,StringFormat("ADX: %.3f / 20 | Bias: %s",adx,bias>0?"Long":bias<0?"Short":"Neutral"),8,C'140,158,185');
   FlashPanelLbl(p+"BUY",PX+8,PY+112,"Trades "+IntegerToString(entries)+"/1",8,C'120,210,150');
   FlashPanelLbl(p+"SEL",PX+112,PY+112,OwnPosition()?"Position: open":"Position: flat",8,C'210,100,100');
   FlashPanelLbl(p+"CST",PX+8,PY+132,StringFormat("Comm/lot: $%.2f | Slip: %dp",FlashCommissionEstimate(),SlippagePoints),8,C'140,200,240');
   FlashPanelLbl(p+"COV",PX+8,PY+149,"BE / Trail: costs covered",8,C'190,180,120');
   string display=FlashSizingLabel()+" | "+state;
   FlashPanelLbl(p+"SL",PX+8,PY+166,StringSubstr(display,0,31),8,C'200,70,70');
   ObjectSetString(gtVisualChart,p+"SL",OBJPROP_TOOLTIP,display);
}
void OnChartEvent(const int id,const long &lparam,const double &dparam,const string &sparam)
{
   if(!FlashVisualEnabled())return;
   if(id==CHARTEVENT_OBJECT_CREATE && StringFind(sparam,FlashPanelPrefix())!=0)
   {flashPanelRestack=true;return;}
   if(id==CHARTEVENT_OBJECT_CLICK && sparam==FlashPanelPrefix()+"BTN")
   {flashPanelCollapsed=!flashPanelCollapsed;FlashDraw();ChartRedraw(gtVisualChart);}
}

datetime FlashHistoryFloor=0,FlashHistoryRendered=0;
bool flashBrokerVisualDirty=true;
bool flashBrokerVisualInitialized=false;
struct FlashBrokerView
{
   ulong id;int side;datetime entered,exited;double entryValue,volume,exitValue,exitVolume,stop;bool mixed;
};
FlashBrokerView flashBrokerView[];
void FlashTradeBox(string key,datetime left,datetime right,double entry,double edge,color ink)
{
   if(!FlashVisualDataEnabled())return;
   string id=FlashVisualPrefix()+key;
   if(right<=left)right=left+1;
   if(ObjectFind(gtVisualChart,id)<0)ObjectCreate(gtVisualChart,id,OBJ_RECTANGLE,0,left,entry,right,edge);
   ObjectMove(gtVisualChart,id,0,left,entry);ObjectMove(gtVisualChart,id,1,right,edge);
   ObjectSetInteger(gtVisualChart,id,OBJPROP_COLOR,ink);
   ObjectSetInteger(gtVisualChart,id,OBJPROP_FILL,false);
   ObjectSetInteger(gtVisualChart,id,OBJPROP_WIDTH,1);
   ObjectSetInteger(gtVisualChart,id,OBJPROP_BACK,true);
   ObjectSetInteger(gtVisualChart,id,OBJPROP_SELECTABLE,false);
   ObjectSetInteger(gtVisualChart,id,OBJPROP_TIMEFRAMES,OBJ_ALL_PERIODS);
   ObjectSetString(gtVisualChart,id,OBJPROP_TOOLTIP,"Flash Trigger ORB | Broker execution | "+key);
}
void FlashPaintBrokerTrades(datetime now)
{
   bool rebuild=flashBrokerVisualDirty;
   if(rebuild)
   {
      int bars=Bars(_Symbol,_Period);
      datetime begin=!flashBrokerVisualInitialized && bars>0?iTime(_Symbol,_Period,bars-1):now-45*86400;
      begin=now-90*86400; // Include owned openings preceding the visible history window.
      if(!HistorySelect(begin,now))return;
      flashBrokerVisualInitialized=true;
      ArrayResize(flashBrokerView,0);
      // Retire average-price arrows from earlier builds; exact deal tickets replace them.
      ObjectsDeleteAll(gtVisualChart,FlashVisualPrefix()+"Entry_");
      ObjectsDeleteAll(gtVisualChart,FlashVisualPrefix()+"Exit_");
      for(int i=0;i<HistoryDealsTotal();i++)
      {
         ulong d=HistoryDealGetTicket(i);
         if(HistoryDealGetString(d,DEAL_SYMBOL)!=_Symbol)continue;
         long kind=HistoryDealGetInteger(d,DEAL_ENTRY);
         ulong id=(ulong)HistoryDealGetInteger(d,DEAL_POSITION_ID);int k=-1;
         for(int j=0;j<ArraySize(flashBrokerView);j++)if(flashBrokerView[j].id==id){k=j;break;}
         if(kind==DEAL_ENTRY_IN)
         {
            if((ulong)HistoryDealGetInteger(d,DEAL_MAGIC)!=Magic)continue;
            if(k<0){k=ArraySize(flashBrokerView);ArrayResize(flashBrokerView,k+1);ZeroMemory(flashBrokerView[k]);flashBrokerView[k].id=id;flashBrokerView[k].entered=(datetime)HistoryDealGetInteger(d,DEAL_TIME);}
            double qty=HistoryDealGetDouble(d,DEAL_VOLUME);
            flashBrokerView[k].side=HistoryDealGetInteger(d,DEAL_TYPE)==DEAL_TYPE_BUY?1:-1;
            flashBrokerView[k].entryValue+=qty*HistoryDealGetDouble(d,DEAL_PRICE);flashBrokerView[k].volume+=qty;
            ulong order=(ulong)HistoryDealGetInteger(d,DEAL_ORDER);
            double sl=HistoryOrderGetDouble(order,ORDER_SL);if(sl>0)flashBrokerView[k].stop=sl;
         }
         else if(k>=0 && (kind==DEAL_ENTRY_OUT || kind==DEAL_ENTRY_OUT_BY))
         {
            double qty=HistoryDealGetDouble(d,DEAL_VOLUME);
            flashBrokerView[k].exitValue+=qty*HistoryDealGetDouble(d,DEAL_PRICE);flashBrokerView[k].exitVolume+=qty;
            flashBrokerView[k].exited=(datetime)HistoryDealGetInteger(d,DEAL_TIME);
         }
      }
      // Shared/netting positions with mixed opening ownership or reversals are not attributed.
      for(int i=0;i<HistoryDealsTotal();i++){
         ulong deal=HistoryDealGetTicket(i);long kind=HistoryDealGetInteger(deal,DEAL_ENTRY);
         if(kind!=DEAL_ENTRY_IN && kind!=DEAL_ENTRY_INOUT)continue;
         ulong id=(ulong)HistoryDealGetInteger(deal,DEAL_POSITION_ID);
         for(int k=0;k<ArraySize(flashBrokerView);k++)if(flashBrokerView[k].id==id
            && (kind==DEAL_ENTRY_INOUT || (ulong)HistoryDealGetInteger(deal,DEAL_MAGIC)!=Magic))flashBrokerView[k].mixed=true;
      }
      // Exact deal prices/timestamps, including partial fills and manual exits of owned positions.
      for(int i=0;i<HistoryDealsTotal();i++)
      {
         ulong deal=HistoryDealGetTicket(i);
         if(HistoryDealGetString(deal,DEAL_SYMBOL)!=_Symbol)continue;
         ulong position=(ulong)HistoryDealGetInteger(deal,DEAL_POSITION_ID);
         bool owned=false;
         for(int k=0;k<ArraySize(flashBrokerView);k++)if(flashBrokerView[k].id==position && !flashBrokerView[k].mixed){owned=true;break;}
         datetime at=(datetime)HistoryDealGetInteger(deal,DEAL_TIME);
         long type=HistoryDealGetInteger(deal,DEAL_TYPE);
         if(!owned || at<FlashHistoryFloor || (type!=DEAL_TYPE_BUY && type!=DEAL_TYPE_SELL))continue;
         FlashDrawFill(type==DEAL_TYPE_BUY?1:-1,at,HistoryDealGetDouble(deal,DEAL_PRICE),"Deal "+IntegerToString((long)deal));
      }
      flashBrokerVisualDirty=false;
   }
   for(int i=0;i<ArraySize(flashBrokerView);i++)
   {
      if(flashBrokerView[i].volume<=0 || flashBrokerView[i].mixed)continue;
      double entry=flashBrokerView[i].entryValue/flashBrokerView[i].volume;
      bool closed=flashBrokerView[i].exitVolume>=flashBrokerView[i].volume-1e-8;
      if(closed && (!rebuild || flashBrokerView[i].exited<FlashHistoryFloor))continue;
      datetime right=closed?flashBrokerView[i].exited:(datetime)MathMax((double)now,(double)(flashBrokerView[i].entered+3*PeriodSeconds(_Period)));
      double exit=closed?flashBrokerView[i].exitValue/flashBrokerView[i].exitVolume:0;
      double stop=flashBrokerView[i].stop>0?flashBrokerView[i].stop:0;
      // Keep original order SL as risk geometry; an exit fill is not the initial stop.
      string tag="Broker "+IntegerToString((long)flashBrokerView[i].id);
      if(stop>0)FlashTradeBox(tag+" risk",flashBrokerView[i].entered,right,entry,stop,C'55,57,68');
      FlashLine(tag+" entry",entry,C'214,218,224',STYLE_SOLID,flashBrokerView[i].entered,right);
      if(closed && flashBrokerView[i].side*(exit-entry)>0)FlashTradeBox(tag+" profit",flashBrokerView[i].entered,right,entry,exit,C'22,55,120');
      // Per-deal arrows above are authoritative; averages only define the position box.
   }
}

void FlashDrawFill(int side,datetime time,double price,string label)
{
   if(!FlashVisualDataEnabled()) return;
   string id=FlashVisualPrefix()+label+"_"+IntegerToString((long)time);
   ObjectCreate(gtVisualChart,id,side==1 ? OBJ_ARROW_BUY : OBJ_ARROW_SELL,0,time,price);
   ObjectSetString(gtVisualChart,id,OBJPROP_TOOLTIP,FlashDisplayName+" | "+label+" "+TimeToString(time,TIME_DATE|TIME_SECONDS));
   ObjectSetInteger(gtVisualChart,id,OBJPROP_COLOR,clrWhite);
   ObjectSetInteger(gtVisualChart,id,OBJPROP_BACK,true);
   ObjectSetInteger(gtVisualChart,id,OBJPROP_SELECTABLE,false);
   ObjectSetInteger(gtVisualChart,id,OBJPROP_TIMEFRAMES,OBJ_ALL_PERIODS);
}
void FlashLine(string key,double price,color ink,ENUM_LINE_STYLE style=STYLE_SOLID,datetime left=0,datetime right=0)
{
   if(!FlashVisualDataEnabled())return;
   string id=FlashVisualPrefix()+key;
   if(left==0)
   {
      left=FlashSessionTime(day);
      if(StringFind(key,"Opening range")==0) left+=510*60;
      else if(StringFind(key,"Overnight")==0 || StringFind(key,"Bias")==0)
         left-=(OvernightStart==23 ? 3600 : 0);
      else if(StringFind(key,"Synthetic")==0) left=virtualStart;
      else if(key=="Entry" || key=="Stop" || key=="Target")
         left=ownedEntryTime>0 ? ownedEntryTime : (datetime)PositionGetInteger(POSITION_TIME);
      else if(StringFind(key,"Last ")==0) left=gtLastBracketTime;
   }
   if(right==0) right=FlashSessionTime(day)+FlashSessionCutoff(FlashSessionTime(day),(int)Model)*60;
   if(left<=0 || right<=left || price<=0) {ObjectDelete(gtVisualChart,id);return;}
   if(ObjectFind(gtVisualChart,id)>=0 && ObjectGetInteger(gtVisualChart,id,OBJPROP_TYPE)!=OBJ_TREND)
      ObjectDelete(gtVisualChart,id);
   if(ObjectFind(gtVisualChart,id)<0) ObjectCreate(gtVisualChart,id,OBJ_TREND,0,left,price,right,price);
   ObjectMove(gtVisualChart,id,0,left,price);ObjectMove(gtVisualChart,id,1,right,price);
   ObjectSetInteger(gtVisualChart,id,OBJPROP_RAY_LEFT,false);ObjectSetInteger(gtVisualChart,id,OBJPROP_RAY_RIGHT,false);
   ObjectSetInteger(gtVisualChart,id,OBJPROP_COLOR,ink);ObjectSetInteger(gtVisualChart,id,OBJPROP_STYLE,style);
   ObjectSetInteger(gtVisualChart,id,OBJPROP_BACK,true);
   ObjectSetInteger(gtVisualChart,id,OBJPROP_SELECTABLE,false);
   ObjectSetInteger(gtVisualChart,id,OBJPROP_TIMEFRAMES,OBJ_ALL_PERIODS);
   ObjectSetString(gtVisualChart,id,OBJPROP_TEXT,key);
   ObjectSetString(gtVisualChart,id,OBJPROP_TOOLTIP,FlashDisplayName+" | "+key+" | "+DoubleToString(price,_Digits)
      +" | "+TimeToString(left,TIME_DATE|TIME_MINUTES)+" to "+TimeToString(right,TIME_MINUTES)+" (broker time)");
}

void FlashHistoryLine(string key,datetime left,double a,datetime right,double b,color ink)
{
   if(!FlashVisualDataEnabled() || right<=left || a<=0 || b<=0)return;
   string id=FlashVisualPrefix()+"HISTORY_"+key;
   if(ObjectFind(gtVisualChart,id)<0)ObjectCreate(gtVisualChart,id,OBJ_TREND,0,left,a,right,b);
   ObjectMove(gtVisualChart,id,0,left,a);ObjectMove(gtVisualChart,id,1,right,b);
   ObjectSetInteger(gtVisualChart,id,OBJPROP_COLOR,ink);ObjectSetInteger(gtVisualChart,id,OBJPROP_WIDTH,1);
   ObjectSetInteger(gtVisualChart,id,OBJPROP_STYLE,STYLE_DOT);ObjectSetInteger(gtVisualChart,id,OBJPROP_BACK,true);
   ObjectSetInteger(gtVisualChart,id,OBJPROP_SELECTABLE,false);ObjectSetInteger(gtVisualChart,id,OBJPROP_RAY_LEFT,false);
   ObjectSetInteger(gtVisualChart,id,OBJPROP_RAY_RIGHT,false);ObjectSetInteger(gtVisualChart,id,OBJPROP_TIMEFRAMES,OBJ_ALL_PERIODS);
   ObjectSetString(gtVisualChart,id,OBJPROP_TOOLTIP,"Source-bar session history | "+key);
}
void FlashHistoricalSessions(datetime now)
{
   if(!FlashVisualDataEnabled() || MQLInfoInteger(MQL_TESTER))return;
   datetime today=FlashVisualDay(now);static datetime retry=0;
   if(FlashHistoryRendered==today || now<retry)return;
   retry=now+60;
   MqlRates b[];ArraySetAsSeries(b,false);
   int count=CopyRates(_Symbol,PERIOD_M1,now-14*86400,now-60,b);
   if(count<=0 || !SeriesInfoInteger(_Symbol,PERIOD_M1,SERIES_SYNCHRONIZED))return;
   datetime dates[3];int found=0;datetime last=0;
   for(int i=count-1;i>=0 && found<3;i--){
      datetime key=FlashVisualDay(b[i].time);
      datetime ny=FlashNewYork(FlashSessionTime(key));MqlDateTime d;TimeToStruct(ny,d);
      if(d.day_of_week==0 || d.day_of_week==6)continue;
      if(key==last)continue;last=key;dates[found++]=key;
   }
   if(found==0)return;
   bool complete=found==3;
   FlashHistoryFloor=FlashSessionTime(dates[found-1]);
   ObjectsDeleteAll(gtVisualChart,FlashVisualPrefix()+"HISTORY_");
   for(int n=found-1;n>=0;n--){
      datetime sd=dates[n],anchor=FlashSessionTime(sd);
      if(sd==today)continue;
      datetime right=anchor+FlashSessionCutoff(anchor,(int)Model)*60;
      string tag=IntegerToString((long)sd)+"_";
      double pv=0,vol=0,prior=0,hi=-DBL_MAX,lo=DBL_MAX,op=0,nh=-DBL_MAX,nl=DBL_MAX;
      datetime priorTime=0,segment=0,segmentLeft=0;double segmentValue=0;int opened=0;
      int seconds=MathMax(60,PeriodSeconds((ENUM_TIMEFRAMES)ChartPeriod(gtVisualChart)));
      int start=510,openingLength=15;
      for(int i=0;i<count;i++){
         datetime t=b[i].time;
         if(t>=anchor-3600 && t<anchor+510*60){nh=MathMax(nh,b[i].high);nl=MathMin(nl,b[i].low);}
         if(t<anchor+start*60 || t>=right)continue;
         if(t<anchor+(start+openingLength)*60){if(t==anchor+start*60)op=b[i].open;opened++;hi=MathMax(hi,b[i].high);lo=MathMin(lo,b[i].low);}
         double q=(double)(b[i].real_volume>0?b[i].real_volume:b[i].tick_volume);
         pv+=(b[i].high+b[i].low+b[i].close)/3*q;vol+=q;double value=vol>0?pv/vol:0;
         // Flash has no session VWAP.
         priorTime=t;prior=value;
      }
      if(nh>nl){
         FlashHistoryLine(tag+"Overnight high",anchor-3600,nh,right,nh,clrDeepSkyBlue);
         FlashHistoryLine(tag+"Overnight low",anchor-3600,nl,right,nl,clrDeepSkyBlue);
         FlashHistoryLine(tag+"Bias upper third",anchor-3600,nl+2*(nh-nl)/3,right,nl+2*(nh-nl)/3,clrSlateGray);
         FlashHistoryLine(tag+"Bias lower third",anchor-3600,nl+(nh-nl)/3,right,nl+(nh-nl)/3,clrSlateGray);
      }
      if(opened!=openingLength || op<=0)complete=false;
      if(opened==openingLength && op>0 && nh>nl && FlashBias(op,nl,nh)!=0){
         FlashHistoryLine(tag+"Opening range high",anchor+510*60,hi,right,hi,clrOrange);
         FlashHistoryLine(tag+"Opening range low",anchor+510*60,lo,right,lo,clrOrange);
      }
   }
   if(complete)FlashHistoryRendered=today;
   flashBrokerVisualDirty=true;
}
void FlashDraw()
{
   if(!FlashVisualEnabled())return;
   string fault=FlashVisualFault();
   FlashVisualSuspend(fault!="");
   if(fault!="")
   {
      datetime now=MQLInfoInteger(MQL_TESTER)?TimeCurrent():TimeTradeServer();
      FlashPanelRows(now,fault,0,0);
      ChartRedraw(gtVisualChart);return;
   }
   FlashHistoricalSessions(TimeCurrent());
   if(!FlashVisualEnabled()) return;
   string p=FlashVisualPrefix();
   if(dayValid && day==FlashVisualDay(TimeCurrent()))
   {
      if(rangeReady && bias!=0)
      {
         FlashLine("Opening range high",orHigh,clrOrange,STYLE_DOT);
         FlashLine("Opening range low",orLow,clrOrange,STYLE_DOT);
      }
      else
      {
         ObjectDelete(gtVisualChart,p+"Opening range high");
         ObjectDelete(gtVisualChart,p+"Opening range low");
      }
      FlashLine("Overnight high",overnightHigh,clrDeepSkyBlue,STYLE_DOT);
      FlashLine("Overnight low",overnightLow,clrDeepSkyBlue,STYLE_DOT);
      FlashLine("Bias upper third",overnightLow+2*(overnightHigh-overnightLow)/3,clrSlateGray,STYLE_DOT);
      FlashLine("Bias lower third",overnightLow+(overnightHigh-overnightLow)/3,clrSlateGray,STYLE_DOT);
   }
   string state=virtualActive ? "Synthetic first trade: waiting for TP / SL" : "Waiting for a fresh closed-bar signal";
   if(dayValid && bias==0) state="Neutral opening: no trade";
   if(virtualActive) {FlashLine("Synthetic SL",virtualStop,clrTomato,STYLE_DASH);FlashLine("Synthetic TP",virtualTarget,clrLimeGreen,STYLE_DASH);}
   else {ObjectDelete(gtVisualChart,p+"Synthetic SL");ObjectDelete(gtVisualChart,p+"Synthetic TP");}
   if(OwnPosition())
   {
      state="Broker position protected";
      FlashRememberBracket(PositionGetDouble(POSITION_PRICE_OPEN),PositionGetDouble(POSITION_SL),PositionGetDouble(POSITION_TP));
      FlashLine("Entry",PositionGetDouble(POSITION_PRICE_OPEN),clrWhite,STYLE_DOT);
      FlashLine("Stop",PositionGetDouble(POSITION_SL),clrTomato);
      FlashLine("Target",PositionGetDouble(POSITION_TP),clrLimeGreen);
   }
   else {ObjectDelete(gtVisualChart,p+"Entry");ObjectDelete(gtVisualChart,p+"Stop");ObjectDelete(gtVisualChart,p+"Target");}
   if(!OwnPosition() && gtLastEntry>0)
   {
      FlashLine("Last entry (reference)",gtLastEntry,clrWhite,STYLE_DOT);
      FlashLine("Last stop (reference)",gtLastStop,clrTomato,STYLE_DASH);
      FlashLine("Last target (reference)",gtLastTarget,clrLimeGreen,STYLE_DASH);
   }
   else
   {
      ObjectDelete(gtVisualChart,p+"Last entry (reference)");ObjectDelete(gtVisualChart,p+"Last stop (reference)");ObjectDelete(gtVisualChart,p+"Last target (reference)");
   }
   double a[];ArraySetAsSeries(a,true);double adx=0;
   // Match the pane's current M15 value; entry confirmation still uses closed bars.
   if(CopyBuffer(adxHandle,0,0,1,a)==1) adx=a[0];
   if(!dayValid) state="Waiting for session history / indicators";
   if(day!=FlashVisualDay(TimeCurrent()) || (!dayValid && FlashMinute(TimeCurrent())<510)) state="Session forming | waiting for completed source data";
   if(!MQLInfoInteger(MQL_TESTER) && !runtimeHistoryReady) state="Loading history | new entries blocked | protection active";
   if(!EnableTrading) state="New trades disabled | existing trades remain protected";
   datetime clock=MQLInfoInteger(MQL_TESTER) ? TimeCurrent() : TimeTradeServer();
   if(!clockReady) state="Loading clock history | waiting for a verified session anchor";
   if(runtimeRecoveryHold) state="Recovery needs attention | broker SL/TP retained";
   if(runtimeCloseAttention) state="Close unconfirmed | no duplicate request";
   if(runtimeCloseRejected) state="Close rejected | retry paused";
   double nub=0;
   FlashPaintBrokerTrades(clock);
   FlashPanelRows(clock,state,adx,nub);
   static datetime fittedBar=0;
   static double fittedStop=0,fittedTarget=0;
   datetime currentBar=iTime(_Symbol,sourceTF,0);
   double fitStop=virtualActive ? virtualStop : gtLastStop,fitTarget=virtualActive ? virtualTarget : gtLastTarget;
   if(currentBar>0 && (currentBar!=fittedBar || fitStop!=fittedStop || fitTarget!=fittedTarget))
   {
      FlashVisualFit();fittedBar=currentBar;fittedStop=fitStop;fittedTarget=fitTarget;
   }
   ChartRedraw(gtVisualChart);
}
void FlashRefreshLiveVisuals(datetime now)
{
   if(MQLInfoInteger(MQL_TESTER) || !FlashVisualEnabled() || !FlashRuntimeHistoryReady()) return;
   datetime visualDay=FlashVisualDay(now);
   if(dayValid && day==visualDay && rangeReady) return;
   static datetime refreshed=0,previousDay=0;
   datetime minute=(datetime)((long)now/60*60);
   if(minute==refreshed) return;
   datetime anchor=FlashSessionTime(visualDay);
   if(minute<=anchor-3600) return; // No completed M1 bar in this session yet.
   datetime right=anchor+FlashSessionCutoff(anchor,(int)Model)*60;
   // Date-keyed session history and broker executions survive session rollover.
   MqlRates bars[];ArraySetAsSeries(bars,false);
   if(!M1HistoryAvailable(anchor-3600)) return;
   int count=CopyRates(_Symbol,PERIOD_M1,anchor-3600,minute-60,bars);
   if(count<=0 || !NativeBarsValid(bars,anchor-3600,minute)) return;
   previousDay=visualDay;refreshed=minute;
   double nightHigh=-DBL_MAX,nightLow=DBL_MAX,openingHigh=-DBL_MAX,openingLow=DBL_MAX;
   double biasOpen=0;bool hasBiasOpen=false;
   datetime opening=anchor+510*60,locked=opening+15*60;
   for(int i=0;i<count;i++)
   {
      if(bars[i].time<opening)
      {
         nightHigh=MathMax(nightHigh,bars[i].high);nightLow=MathMin(nightLow,bars[i].low);
      }
      if(bars[i].time>=opening && bars[i].time<locked){openingHigh=MathMax(openingHigh,bars[i].high);openingLow=MathMin(openingLow,bars[i].low);}
   }
   if(nightHigh>nightLow)
   {
      FlashLine("Overnight high",nightHigh,clrDeepSkyBlue,STYLE_DOT,anchor-3600,right);
      FlashLine("Overnight low",nightLow,clrDeepSkyBlue,STYLE_DOT,anchor-3600,right);
      FlashLine("Bias upper third",nightLow+2*(nightHigh-nightLow)/3,clrSlateGray,STYLE_DOT,anchor-3600,right);
      FlashLine("Bias lower third",nightLow+(nightHigh-nightLow)/3,clrSlateGray,STYLE_DOT,anchor-3600,right);
   }
   hasBiasOpen=minute>=opening && OpeningPrice(opening,15,biasOpen);
   bool eligibleBias=hasBiasOpen
      && nightHigh>nightLow && FlashBias(biasOpen,nightLow,nightHigh)!=0;
   if(eligibleBias && openingHigh>openingLow)
   {
      FlashLine("Opening range high",openingHigh,clrOrange,STYLE_DOT,opening,right);
      FlashLine("Opening range low",openingLow,clrOrange,STYLE_DOT,opening,right);
   }
   else
   {
      ObjectDelete(gtVisualChart,FlashVisualPrefix()+"Opening range high");
      ObjectDelete(gtVisualChart,FlashVisualPrefix()+"Opening range low");
   }
   // Display projection never changes trading day, bias, attempts, readiness, or indicator accumulators.
   static bool reported=false;
   if(!reported)
   {
      int levels=0,curve=0;
      string prefix=FlashVisualPrefix();
      for(int index=ObjectsTotal(gtVisualChart,0,-1)-1;index>=0;index--)
      {
         string name=ObjectName(gtVisualChart,index,0,-1);
         if(StringFind(name,prefix)!=0 || ObjectGetInteger(gtVisualChart,name,OBJPROP_TYPE)!=OBJ_TREND) continue;
         if(StringFind(name,prefix+"VWAP_")==0) curve++;else levels++;
      }
      if(levels>0 || curve>0)
      {
         PrintFormat("FLASH_LIVE_DISPLAY_READY levels=%d curveSegments=%d viewed=%s source=M1 session=%s",levels,curve,EnumToString((ENUM_TIMEFRAMES)Period()),TimeToString(visualDay,TIME_DATE));
         reported=true;
      }
   }
   FlashVisualFit();
}

#endif
