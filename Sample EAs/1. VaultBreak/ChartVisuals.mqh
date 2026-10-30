#ifndef VB_STRATEGY_VISUALS
#define VB_STRATEGY_VISUALS
long gtVisualChart=0;
int gtPane=INVALID_HANDLE;
string gtPaneName="";
double gtLastEntry=0,gtLastStop=0,gtLastTarget=0;
datetime gtLastBracketTime=0;
void VBRememberBracket(double entry,double stop,double target){gtLastEntry=entry;gtLastStop=stop;gtLastTarget=target;gtLastBracketTime=ownedEntryTime>0 ? ownedEntryTime : TimeCurrent();}
void VBVisualFit(datetime reference=0)
{
   double low=DBL_MAX,high=-DBL_MAX;
   MqlRates b[];
   int n=reference>0 ? CopyRates(_Symbol,sourceTF,reference,96,b) : CopyRates(_Symbol,sourceTF,0,96,b);
   for(int i=0;i<n;i++){low=MathMin(low,b[i].low);high=MathMax(high,b[i].high);}
   double levels[]={midnight,vwap,orHigh,orLow,overnightHigh,overnightLow,gtLastEntry,gtLastStop,gtLastTarget,
                    virtualActive ? virtualStop : 0,virtualActive ? virtualTarget : 0,
                    Model==VaultBreak ? midnight+NoiseFraction*averageATR : 0};
   for(int i=0;i<ArraySize(levels);i++) if(levels[i]>0){low=MathMin(low,levels[i]);high=MathMax(high,levels[i]);}
   if(high<=low || low==DBL_MAX) return;
   double pad=(high-low)*.08;
   ChartSetInteger(gtVisualChart,CHART_SCALEFIX,true);
   ChartSetDouble(gtVisualChart,CHART_FIXED_MIN,low-pad);ChartSetDouble(gtVisualChart,CHART_FIXED_MAX,high+pad);
   ChartSetInteger(gtVisualChart,CHART_SCALE,3);ChartNavigate(gtVisualChart,CHART_END,reference>0 ? -iBarShift(_Symbol,sourceTF,reference,false) : 0);
   ChartRedraw(gtVisualChart);
}
string VBVisualPrefix(){return "VB_"+IntegerToString((long)Magic)+"_";}
datetime VBVisualDay(datetime now){return VBDay(now);}
bool VBVisualEnabled(){return ShowVisuals && (!MQLInfoInteger(MQL_TESTER) || MQLInfoInteger(MQL_VISUAL_MODE));}
bool gtVisualSuspended=false;
string VBVisualFault()
{
   bool tester=MQLInfoInteger(MQL_TESTER);
   if(!tester && !TerminalInfoInteger(TERMINAL_CONNECTED))return "Disconnected | waiting for server";
   if(!tester && !TerminalInfoInteger(TERMINAL_TRADE_ALLOWED))return "Terminal trading disabled";
   if(!tester && !MQLInfoInteger(MQL_TRADE_ALLOWED))return "EA trading permission disabled";
   if(!tester && !AccountInfoInteger(ACCOUNT_TRADE_ALLOWED))return "Server account trading disabled";
   if(!tester && !VBRuntimeAccountContextValid())return "Account context changed | trading blocked";
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
bool VBVisualDataEnabled(){return VBVisualEnabled() && VBVisualFault()=="";}
void VBVisualSuspend(bool blocked)
{
   if(blocked==gtVisualSuspended)return;
   gtVisualSuspended=blocked;
   // Preserve chart history for restoration; touch only this EA's own namespace.
   string prefix=VBVisualPrefix(),panel=VBPanelPrefix();
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
void VBInitVisual()
{
   if(!VBVisualEnabled()) return;
   string oldRows[]={"Title","Clock","Metrics","State"};
   for(int i=0;i<ArraySize(oldRows);i++)ObjectDelete(gtVisualChart,VBVisualPrefix()+oldRows[i]);
   ChartSetInteger(gtVisualChart,CHART_EVENT_OBJECT_CREATE,true);

   // Rebuild the curve at the new view's resolution; retain the other owned levels.
   ObjectsDeleteAll(gtVisualChart,VBVisualPrefix()+"VWAP_");
   // Remove only objects owned by this EA under its former object namespace.
   ObjectsDeleteAll(gtVisualChart,"GT_"+IntegerToString((long)Magic)+"_");
   // Repair retained owned drawings from builds that used the foreground layer.
   for(int i=ObjectsTotal(gtVisualChart,-1,-1)-1;i>=0;i--)
   {
      string name=ObjectName(gtVisualChart,i,-1,-1);
      if(StringFind(name,VBVisualPrefix())==0 && StringFind(name,VBPanelPrefix())!=0)
      {
         ObjectSetInteger(gtVisualChart,name,OBJPROP_BACK,true);
         if(ObjectGetInteger(gtVisualChart,name,OBJPROP_TYPE)==OBJ_RECTANGLE)
         {ObjectSetInteger(gtVisualChart,name,OBJPROP_FILL,false);ObjectSetInteger(gtVisualChart,name,OBJPROP_WIDTH,1);}
      }
   }
   ChartSetInteger(gtVisualChart,CHART_MODE,CHART_CANDLES);
   ChartSetInteger(gtVisualChart,CHART_COLOR_BACKGROUND,clrBlack);
   ChartSetInteger(gtVisualChart,CHART_COLOR_FOREGROUND,clrSilver);
   ChartSetInteger(gtVisualChart,CHART_COLOR_CANDLE_BULL,clrWhite);
   ChartSetInteger(gtVisualChart,CHART_COLOR_CANDLE_BEAR,clrDarkOrange);
   ChartSetInteger(gtVisualChart,CHART_COLOR_CHART_UP,clrWhite);
   ChartSetInteger(gtVisualChart,CHART_COLOR_CHART_DOWN,clrDarkOrange);
   ChartSetInteger(gtVisualChart,CHART_SHOW_GRID,false);
   ChartSetInteger(gtVisualChart,CHART_AUTOSCROLL,true);ChartSetInteger(gtVisualChart,CHART_SHIFT,true);
}

const int PX=10,PY=20,PW=210,PH=198,HH=32;
bool vbPanelCollapsed=false,vbPanelRestack=false;
string VBPanelPrefix(){return VBVisualPrefix()+"PANEL_";}
void VBPanelRect(string name, int x, int y, int w, int h, color bg, color bdr)
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

void VBPanelLbl(string name, int x, int y, string text, int fs, color clr)
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

void VBPanelShell()
{
    if(!VBVisualEnabled()) return;
    int height=vbPanelCollapsed ? HH : PH;
    VBPanelRect(VBPanelPrefix()+"SHD2", PX+5, PY+5, PW, height, C'6,6,6',    C'6,6,6');
    VBPanelRect(VBPanelPrefix()+"SHD1", PX+2, PY+2, PW, height, C'12,12,12', C'12,12,12');
    VBPanelRect(VBPanelPrefix()+"BG",   PX,   PY,   PW, height, clrBlack,    C'52,68,92');
    VBPanelRect(VBPanelPrefix()+"HDR",  PX,   PY,   PW, HH, C'18,22,28', C'52,68,92');
    VBPanelRect(VBPanelPrefix()+"HAIR", PX,   PY+HH, PW, 1, C'52,68,92', C'52,68,92');
    VBPanelLbl(VBPanelPrefix()+"TTL",   PX+8, PY+8, "Vault Break", 9, C'190,210,236');
    string button=VBPanelPrefix()+"BTN";
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
    ObjectSetString(gtVisualChart,button,OBJPROP_TEXT,vbPanelCollapsed ? "+" : "-");
    if(vbPanelCollapsed)
    {
        string rows[]={"SRV","HI","LO","SZ","BUY","SEL","CST","COV","SL"};
        for(int i=0;i<ArraySize(rows);i++) ObjectDelete(gtVisualChart,VBPanelPrefix()+rows[i]);
    }
}

void VBPanelRows(datetime clock,string state,double adx,double nub)
{
   if(vbPanelRestack){vbPanelRestack=false;ObjectsDeleteAll(gtVisualChart,VBPanelPrefix());}
   VBPanelShell();
   if(vbPanelCollapsed)return;
   string p=VBPanelPrefix();
   VBPanelLbl(p+"SRV",PX+8,PY+40,clockReady ? "NY: "+TimeToString(VBNewYork(clock),TIME_SECONDS)+" CT: "+TimeToString(VBCentral(clock),TIME_SECONDS) : "Clock history loading",8,C'140,158,185');
   VBPanelLbl(p+"HI",PX+8,PY+59,"NUB: "+(nub>0?DoubleToString(nub,3):"--"),8,C'120,210,150');
   VBPanelLbl(p+"LO",PX+8,PY+76,"SOP: "+(midnight>0?DoubleToString(midnight,2):"--"),8,C'210,100,100');
   VBPanelLbl(p+"SZ",PX+8,PY+93,"VWAP: "+(vwap>0?DoubleToString(vwap,2):"--"),8,C'140,158,185');
   VBPanelLbl(p+"BUY",PX+8,PY+112,"Trades "+IntegerToString(entries)+"/3",8,C'120,210,150');
   VBPanelLbl(p+"SEL",PX+112,PY+112,OwnPosition()?"Position: long":"Position: flat",8,C'210,100,100');
   VBPanelLbl(p+"CST",PX+8,PY+132,StringFormat("Comm/lot: $%.2f | Slip: %dp",VBCommissionEstimate(),SlippagePoints),8,C'140,200,240');
   VBPanelLbl(p+"COV",PX+8,PY+149,"Fixed SL / TP: 60 / 40",8,C'190,180,120');
   string display=VBSizingLabel()+" | "+state;
   VBPanelLbl(p+"SL",PX+8,PY+166,StringSubstr(display,0,31),8,C'200,70,70');
   ObjectSetString(gtVisualChart,p+"SL",OBJPROP_TOOLTIP,display);
}
void OnChartEvent(const int id,const long &lparam,const double &dparam,const string &sparam)
{
   if(!VBVisualEnabled())return;
   if(id==CHARTEVENT_OBJECT_CREATE && StringFind(sparam,VBPanelPrefix())!=0)
   {vbPanelRestack=true;return;}
   if(id==CHARTEVENT_OBJECT_CLICK && sparam==VBPanelPrefix()+"BTN")
   {vbPanelCollapsed=!vbPanelCollapsed;VBDraw();ChartRedraw(gtVisualChart);}
}


datetime VBHistoryFloor=0,VBHistoryRendered=0;
bool vbBrokerVisualDirty=true;
bool vbBrokerVisualInitialized=false;
datetime vbVirtualExitTime=0;
struct VBBrokerView
{
   ulong id;int side;datetime entered,exited;double entryValue,volume,exitValue,exitVolume,stop;bool mixed;
};
VBBrokerView vbBrokerView[];
void VBTradeBox(string key,datetime left,datetime right,double entry,double edge,color ink)
{
   if(!VBVisualDataEnabled())return;
   string id=VBVisualPrefix()+key;
   if(right<=left)right=left+1;
   if(ObjectFind(gtVisualChart,id)<0)ObjectCreate(gtVisualChart,id,OBJ_RECTANGLE,0,left,entry,right,edge);
   ObjectMove(gtVisualChart,id,0,left,entry);ObjectMove(gtVisualChart,id,1,right,edge);
   ObjectSetInteger(gtVisualChart,id,OBJPROP_COLOR,ink);
   ObjectSetInteger(gtVisualChart,id,OBJPROP_FILL,false);
   ObjectSetInteger(gtVisualChart,id,OBJPROP_WIDTH,1);
   ObjectSetInteger(gtVisualChart,id,OBJPROP_BACK,true);
   ObjectSetInteger(gtVisualChart,id,OBJPROP_SELECTABLE,false);
   ObjectSetInteger(gtVisualChart,id,OBJPROP_TIMEFRAMES,OBJ_ALL_PERIODS);
   ObjectSetString(gtVisualChart,id,OBJPROP_TOOLTIP,"Vault Break | Broker execution | "+key);
}
void VBPaintBrokerTrades(datetime now)
{
   bool rebuild=vbBrokerVisualDirty;
   if(rebuild)
   {
      int bars=Bars(_Symbol,_Period);
      datetime begin=!vbBrokerVisualInitialized && bars>0?iTime(_Symbol,_Period,bars-1):now-45*86400;
      begin=now-90*86400; // Include owned openings preceding the visible history window.
      if(!HistorySelect(begin,now))return;
      vbBrokerVisualInitialized=true;
      ArrayResize(vbBrokerView,0);
      // Retire average-price arrows from earlier builds; exact deal tickets replace them.
      ObjectsDeleteAll(gtVisualChart,VBVisualPrefix()+"Entry_");
      ObjectsDeleteAll(gtVisualChart,VBVisualPrefix()+"Exit_");
      for(int i=0;i<HistoryDealsTotal();i++)
      {
         ulong d=HistoryDealGetTicket(i);
         if(HistoryDealGetString(d,DEAL_SYMBOL)!=_Symbol)continue;
         long kind=HistoryDealGetInteger(d,DEAL_ENTRY);
         ulong id=(ulong)HistoryDealGetInteger(d,DEAL_POSITION_ID);int k=-1;
         for(int j=0;j<ArraySize(vbBrokerView);j++)if(vbBrokerView[j].id==id){k=j;break;}
         if(kind==DEAL_ENTRY_IN)
         {
            if((ulong)HistoryDealGetInteger(d,DEAL_MAGIC)!=Magic)continue;
            if(k<0){k=ArraySize(vbBrokerView);ArrayResize(vbBrokerView,k+1);ZeroMemory(vbBrokerView[k]);vbBrokerView[k].id=id;vbBrokerView[k].entered=(datetime)HistoryDealGetInteger(d,DEAL_TIME);}
            double qty=HistoryDealGetDouble(d,DEAL_VOLUME);
            vbBrokerView[k].side=HistoryDealGetInteger(d,DEAL_TYPE)==DEAL_TYPE_BUY?1:-1;
            vbBrokerView[k].entryValue+=qty*HistoryDealGetDouble(d,DEAL_PRICE);vbBrokerView[k].volume+=qty;
            ulong order=(ulong)HistoryDealGetInteger(d,DEAL_ORDER);
            double sl=HistoryOrderGetDouble(order,ORDER_SL);if(sl>0)vbBrokerView[k].stop=sl;
         }
         else if(k>=0 && (kind==DEAL_ENTRY_OUT || kind==DEAL_ENTRY_OUT_BY))
         {
            double qty=HistoryDealGetDouble(d,DEAL_VOLUME);
            vbBrokerView[k].exitValue+=qty*HistoryDealGetDouble(d,DEAL_PRICE);vbBrokerView[k].exitVolume+=qty;
            vbBrokerView[k].exited=(datetime)HistoryDealGetInteger(d,DEAL_TIME);
         }
      }
      // Shared/netting positions with mixed opening ownership or reversals are not attributed.
      for(int i=0;i<HistoryDealsTotal();i++){
         ulong deal=HistoryDealGetTicket(i);long kind=HistoryDealGetInteger(deal,DEAL_ENTRY);
         if(kind!=DEAL_ENTRY_IN && kind!=DEAL_ENTRY_INOUT)continue;
         ulong id=(ulong)HistoryDealGetInteger(deal,DEAL_POSITION_ID);
         for(int k=0;k<ArraySize(vbBrokerView);k++)if(vbBrokerView[k].id==id
            && (kind==DEAL_ENTRY_INOUT || (ulong)HistoryDealGetInteger(deal,DEAL_MAGIC)!=Magic))vbBrokerView[k].mixed=true;
      }
      // Exact deal prices/timestamps, including partial fills and manual exits of owned positions.
      for(int i=0;i<HistoryDealsTotal();i++)
      {
         ulong deal=HistoryDealGetTicket(i);
         if(HistoryDealGetString(deal,DEAL_SYMBOL)!=_Symbol)continue;
         ulong position=(ulong)HistoryDealGetInteger(deal,DEAL_POSITION_ID);
         bool owned=false;
         for(int k=0;k<ArraySize(vbBrokerView);k++)if(vbBrokerView[k].id==position && !vbBrokerView[k].mixed){owned=true;break;}
         datetime at=(datetime)HistoryDealGetInteger(deal,DEAL_TIME);
         long type=HistoryDealGetInteger(deal,DEAL_TYPE);
         if(!owned || at<VBHistoryFloor || (type!=DEAL_TYPE_BUY && type!=DEAL_TYPE_SELL))continue;
         VBDrawFill(type==DEAL_TYPE_BUY?1:-1,at,HistoryDealGetDouble(deal,DEAL_PRICE),"Deal "+IntegerToString((long)deal));
      }
      vbBrokerVisualDirty=false;
   }
   for(int i=0;i<ArraySize(vbBrokerView);i++)
   {
      if(vbBrokerView[i].volume<=0 || vbBrokerView[i].mixed)continue;
      double entry=vbBrokerView[i].entryValue/vbBrokerView[i].volume;
      bool closed=vbBrokerView[i].exitVolume>=vbBrokerView[i].volume-1e-8;
      if(closed && (!rebuild || vbBrokerView[i].exited<VBHistoryFloor))continue;
      datetime right=closed?vbBrokerView[i].exited:(datetime)MathMax((double)now,(double)(vbBrokerView[i].entered+3*PeriodSeconds(_Period)));
      double exit=closed?vbBrokerView[i].exitValue/vbBrokerView[i].exitVolume:0;
      double stop=vbBrokerView[i].stop>0?vbBrokerView[i].stop:0;
      // Keep original order SL as risk geometry; an exit fill is not the initial stop.
      string tag="Broker "+IntegerToString((long)vbBrokerView[i].id);
      if(stop>0)VBTradeBox(tag+" risk",vbBrokerView[i].entered,right,entry,stop,C'55,57,68');
      VBLine(tag+" entry",entry,C'214,218,224',STYLE_SOLID,vbBrokerView[i].entered,right);
      if(closed && vbBrokerView[i].side*(exit-entry)>0)VBTradeBox(tag+" profit",vbBrokerView[i].entered,right,entry,exit,C'22,55,120');
      // Per-deal arrows above are authoritative; averages only define the position box.
   }
}

void VBDrawFill(int side,datetime time,double price,string label)
{
   if(!VBVisualDataEnabled()) return;
   string id=VBVisualPrefix()+label+"_"+IntegerToString((long)time);
   ObjectCreate(gtVisualChart,id,side==1 ? OBJ_ARROW_BUY : OBJ_ARROW_SELL,0,time,price);
   ObjectSetString(gtVisualChart,id,OBJPROP_TOOLTIP,VBDisplayName+" | "+label+" "+TimeToString(time,TIME_DATE|TIME_SECONDS));
   ObjectSetInteger(gtVisualChart,id,OBJPROP_COLOR,clrWhite);
   ObjectSetInteger(gtVisualChart,id,OBJPROP_BACK,true);
   ObjectSetInteger(gtVisualChart,id,OBJPROP_SELECTABLE,false);
   ObjectSetInteger(gtVisualChart,id,OBJPROP_TIMEFRAMES,OBJ_ALL_PERIODS);
}
void VBLine(string key,double price,color ink,ENUM_LINE_STYLE style=STYLE_SOLID,datetime left=0,datetime right=0)
{
   if(!VBVisualDataEnabled())return;
   string id=VBVisualPrefix()+key;
   if(left==0)
   {
      left=VBSessionTime(day);
      if(StringFind(key,"Opening range")==0) left+=510*60;
      else if(StringFind(key,"Overnight")==0 || StringFind(key,"Bias")==0)
         left-=(OvernightStart==23 ? 3600 : 0);
      else if(StringFind(key,"Synthetic")==0) left=virtualStart;
      else if(key=="Entry" || key=="Stop" || key=="Target")
         left=ownedEntryTime>0 ? ownedEntryTime : (datetime)PositionGetInteger(POSITION_TIME);
      else if(StringFind(key,"Last ")==0) left=gtLastBracketTime;
   }
   if(right==0) right=VBSessionTime(day)+VBSessionCutoff(VBSessionTime(day),(int)Model)*60;
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
   ObjectSetString(gtVisualChart,id,OBJPROP_TOOLTIP,VBDisplayName+" | "+key+" | "+DoubleToString(price,_Digits)
      +" | "+TimeToString(left,TIME_DATE|TIME_MINUTES)+" to "+TimeToString(right,TIME_MINUTES)+" (broker time)");
}

void VBHistoryLine(string key,datetime left,double a,datetime right,double b,color ink)
{
   if(!VBVisualDataEnabled() || right<=left || a<=0 || b<=0)return;
   string id=VBVisualPrefix()+"HISTORY_"+key;
   if(ObjectFind(gtVisualChart,id)<0)ObjectCreate(gtVisualChart,id,OBJ_TREND,0,left,a,right,b);
   ObjectMove(gtVisualChart,id,0,left,a);ObjectMove(gtVisualChart,id,1,right,b);
   ObjectSetInteger(gtVisualChart,id,OBJPROP_COLOR,ink);ObjectSetInteger(gtVisualChart,id,OBJPROP_WIDTH,1);
   ObjectSetInteger(gtVisualChart,id,OBJPROP_STYLE,STYLE_DOT);ObjectSetInteger(gtVisualChart,id,OBJPROP_BACK,true);
   ObjectSetInteger(gtVisualChart,id,OBJPROP_SELECTABLE,false);ObjectSetInteger(gtVisualChart,id,OBJPROP_RAY_LEFT,false);
   ObjectSetInteger(gtVisualChart,id,OBJPROP_RAY_RIGHT,false);ObjectSetInteger(gtVisualChart,id,OBJPROP_TIMEFRAMES,OBJ_ALL_PERIODS);
   ObjectSetString(gtVisualChart,id,OBJPROP_TOOLTIP,"Source-bar session history | "+key);
}
void VBHistoricalSessions(datetime now)
{
   if(!VBVisualDataEnabled() || MQLInfoInteger(MQL_TESTER))return;
   datetime today=VBVisualDay(now);static datetime retry=0;
   if(VBHistoryRendered==today || now<retry)return;
   retry=now+60;
   MqlRates b[];ArraySetAsSeries(b,false);
   int count=CopyRates(_Symbol,PERIOD_M1,now-14*86400,now-60,b);
   if(count<=0 || !SeriesInfoInteger(_Symbol,PERIOD_M1,SERIES_SYNCHRONIZED))return;
   datetime dates[3];int found=0;datetime last=0;
   for(int i=count-1;i>=0 && found<3;i--){
      datetime key=VBVisualDay(b[i].time);
      datetime ny=VBNewYork(VBSessionTime(key));MqlDateTime d;TimeToStruct(ny,d);
      if(d.day_of_week==0 || d.day_of_week==6)continue;
      if(key==last)continue;last=key;dates[found++]=key;
   }
   if(found==0)return;
   bool complete=found==3;
   VBHistoryFloor=VBSessionTime(dates[found-1]);
   ObjectsDeleteAll(gtVisualChart,VBVisualPrefix()+"HISTORY_");
   for(int n=found-1;n>=0;n--){
      datetime sd=dates[n],anchor=VBSessionTime(sd);
      if(sd==today)continue;
      datetime right=anchor+VBSessionCutoff(anchor,(int)Model)*60;
      string tag=IntegerToString((long)sd)+"_";
      double pv=0,vol=0,prior=0,hi=-DBL_MAX,lo=DBL_MAX,op=0,nh=-DBL_MAX,nl=DBL_MAX;
      datetime priorTime=0,segment=0,segmentLeft=0;double segmentValue=0;int opened=0;
      int seconds=MathMax(60,PeriodSeconds((ENUM_TIMEFRAMES)ChartPeriod(gtVisualChart)));
      int start=0,openingLength=30;
      for(int i=0;i<count;i++){
         datetime t=b[i].time;
         if(t>=anchor-3600 && t<anchor+510*60){nh=MathMax(nh,b[i].high);nl=MathMin(nl,b[i].low);}
         if(t<anchor+start*60 || t>=right)continue;
         if(t<anchor+(start+openingLength)*60){if(t==anchor+start*60)op=b[i].open;opened++;hi=MathMax(hi,b[i].high);lo=MathMin(lo,b[i].low);}
         double q=(double)(b[i].real_volume>0?b[i].real_volume:b[i].tick_volume);
         pv+=(b[i].high+b[i].low+b[i].close)/3*q;vol+=q;double value=vol>0?pv/vol:0;
         if(priorTime>0 && t==priorTime+60){
            datetime slot=(datetime)((long)t/seconds*seconds+seconds);
            if(slot!=segment){segment=slot;segmentLeft=priorTime+60;segmentValue=prior;}
            VBHistoryLine(tag+"VWAP_"+IntegerToString((long)slot),segmentLeft,segmentValue,t+60,value,clrDeepSkyBlue);
         }
         priorTime=t;prior=value;
      }
      
      if(opened!=openingLength || op<=0)complete=false;
      if(opened==openingLength && op>0 ){
         VBHistoryLine(tag+"Session Opening Price (SOP)",anchor,op,right,op,clrSilver);
         double mean=0;if(VBSessionATRMean(sd,mean,true))VBHistoryLine(tag+"Noise Up Barrier (NUB)",anchor,op+NoiseFraction*mean,right,op+NoiseFraction*mean,clrOrange);
         else complete=false;
      }
   }
   if(complete)VBHistoryRendered=today;
   vbBrokerVisualDirty=true;
}
void VBDraw()
{
   if(!VBVisualEnabled())return;
   string fault=VBVisualFault();
   VBVisualSuspend(fault!="");
   if(fault!="")
   {
      datetime now=MQLInfoInteger(MQL_TESTER)?TimeCurrent():TimeTradeServer();
      VBPanelRows(now,fault,0,0);
      ChartRedraw(gtVisualChart);return;
   }
   VBHistoricalSessions(TimeCurrent());
   if(!VBVisualEnabled()) return;
   string p=VBVisualPrefix();
   if(dayValid && day==VBVisualDay(TimeCurrent()))
   {
      VBLine("Session Opening Price (SOP)",midnight,clrSilver,STYLE_DOT);
      if(Model==VaultBreak) VBLine("Noise Up Barrier (NUB)",midnight+NoiseFraction*averageATR,clrOrange,STYLE_DOT);
      else if(rangeReady)
      {
         VBLine("Opening range high",orHigh,clrOrange,STYLE_DOT);
         VBLine("Opening range low",orLow,clrOrange,STYLE_DOT);
         VBLine("Overnight high",overnightHigh,clrDeepSkyBlue,STYLE_DOT);
         VBLine("Overnight low",overnightLow,clrDeepSkyBlue,STYLE_DOT);
         VBLine("Bias upper third",overnightLow+2*(overnightHigh-overnightLow)/3,clrSlateGray,STYLE_DOT);
         VBLine("Bias lower third",overnightLow+(overnightHigh-overnightLow)/3,clrSlateGray,STYLE_DOT);
      }
      ObjectDelete(gtVisualChart,p+"VWAP current"); // The VWAP is its bounded curve, never a horizontal ray.
   }
   string state=virtualActive ? "Synthetic first trade: waiting for TP / SL" : "Waiting for a fresh closed-bar signal";
   if(virtualActive || vbVirtualExitTime>virtualStart)
   {
      datetime end=virtualActive?VBSessionTime(day)+VBSessionCutoff(VBSessionTime(day),(int)Model)*60:vbVirtualExitTime;
      VBLine("Synthetic entry",virtualStop+StopPoints,clrSlateGray,STYLE_DASH,virtualStart,end);
      VBLine("Synthetic SL",virtualStop,clrTomato,STYLE_DASH,virtualStart,end);
      VBLine("Synthetic TP",virtualTarget,clrLimeGreen,STYLE_DASH,virtualStart,end);
   }
   else {ObjectDelete(gtVisualChart,p+"Synthetic entry");ObjectDelete(gtVisualChart,p+"Synthetic SL");ObjectDelete(gtVisualChart,p+"Synthetic TP");}
   if(OwnPosition())
   {
      state="Broker position protected";
      VBRememberBracket(PositionGetDouble(POSITION_PRICE_OPEN),PositionGetDouble(POSITION_SL),PositionGetDouble(POSITION_TP));
      VBLine("Entry",PositionGetDouble(POSITION_PRICE_OPEN),clrWhite,STYLE_DOT);
      VBLine("Stop",PositionGetDouble(POSITION_SL),clrTomato);
      VBLine("Target",PositionGetDouble(POSITION_TP),clrLimeGreen);
   }
   else {ObjectDelete(gtVisualChart,p+"Entry");ObjectDelete(gtVisualChart,p+"Stop");ObjectDelete(gtVisualChart,p+"Target");}
   if(!OwnPosition() && gtLastEntry>0)
   {
      VBLine("Last entry (reference)",gtLastEntry,clrWhite,STYLE_DOT);
      VBLine("Last stop (reference)",gtLastStop,clrTomato,STYLE_DASH);
      VBLine("Last target (reference)",gtLastTarget,clrLimeGreen,STYLE_DASH);
   }
   else
   {
      ObjectDelete(gtVisualChart,p+"Last entry (reference)");ObjectDelete(gtVisualChart,p+"Last stop (reference)");ObjectDelete(gtVisualChart,p+"Last target (reference)");
   }
   double a[];ArraySetAsSeries(a,true);double adx=0;
   if(CopyBuffer(adxHandle,0,1,1,a)==1) adx=a[0];
   if(!dayValid) state="Waiting for session history / indicators";
   if(day!=VBVisualDay(TimeCurrent()) || (!dayValid && VBMinute(TimeCurrent())<1)) state="Session forming | waiting for completed source data";
   if(!MQLInfoInteger(MQL_TESTER) && !runtimeHistoryReady) state="Loading history | new entries blocked | protection active";
   if(!EnableTrading) state="New trades disabled | existing trades remain protected";
   datetime clock=MQLInfoInteger(MQL_TESTER) ? TimeCurrent() : TimeTradeServer();
   if(!clockReady) state="Loading clock history | waiting for a verified session anchor";
   if(runtimeRecoveryHold) state="Recovery needs attention | broker SL/TP retained";
   if(runtimeCloseAttention) state="Close unconfirmed | no duplicate request";
   if(runtimeCloseRejected) state="Close rejected | retry paused";
   double nub=dayValid && day==VBVisualDay(clock) ? midnight+NoiseFraction*averageATR : 0;
   string level=VBVisualPrefix()+"Noise Up Barrier (NUB)";
   if(nub<=0 && ObjectFind(gtVisualChart,level)>=0
      && (datetime)ObjectGetInteger(gtVisualChart,level,OBJPROP_TIME,0)==VBSessionTime(VBVisualDay(clock)))
      nub=ObjectGetDouble(gtVisualChart,level,OBJPROP_PRICE,0);
   VBPaintBrokerTrades(clock);
   VBPanelRows(clock,state,adx,nub);
   static datetime fittedBar=0;
   static double fittedStop=0,fittedTarget=0;
   datetime currentBar=iTime(_Symbol,sourceTF,0);
   double fitStop=virtualActive ? virtualStop : gtLastStop,fitTarget=virtualActive ? virtualTarget : gtLastTarget;
   if(currentBar>0 && (currentBar!=fittedBar || fitStop!=fittedStop || fitTarget!=fittedTarget))
   {
      VBVisualFit();fittedBar=currentBar;fittedStop=fitStop;fittedTarget=fitTarget;
   }
   ChartRedraw(gtVisualChart);
}
void VBDrawVWAP(datetime t,double value)
{
   if(!VBVisualDataEnabled() || value<=0) return;
   static datetime previous=0;static double old=0;
   static datetime segment=0,left=0;static double leftValue=0;
   int seconds=MathMax(60,PeriodSeconds((ENUM_TIMEFRAMES)ChartPeriod(gtVisualChart)));
   // Each completed M1 value remains the endpoint. A viewed-bar boundary ends
   // the previous segment, so dotted strokes span pixels rather than subpixels.
   datetime end=(datetime)(((long)t-1)/seconds*seconds+seconds);
   if(previous>0 && (t<=previous || VBDay(previous)!=VBDay(t)))
   {previous=0;segment=0;left=0;old=0;leftValue=0;}
   if(previous>0 && VBDay(previous)==VBDay(t) && t>previous)
   {
      if(segment!=end){segment=end;left=previous;leftValue=old;}
      string id=VBVisualPrefix()+"VWAP_"+IntegerToString((long)segment);
      if(ObjectFind(gtVisualChart,id)<0) ObjectCreate(gtVisualChart,id,OBJ_TREND,0,left,leftValue,t,value);
      ObjectMove(gtVisualChart,id,0,left,leftValue);ObjectMove(gtVisualChart,id,1,t,value);
      ObjectSetInteger(gtVisualChart,id,OBJPROP_COLOR,clrDeepSkyBlue);
      ObjectSetInteger(gtVisualChart,id,OBJPROP_BACK,true);
      ObjectSetInteger(gtVisualChart,id,OBJPROP_STYLE,STYLE_DOT);
      ObjectSetInteger(gtVisualChart,id,OBJPROP_WIDTH,1);
      ObjectSetInteger(gtVisualChart,id,OBJPROP_RAY_LEFT,false);ObjectSetInteger(gtVisualChart,id,OBJPROP_RAY_RIGHT,false);ObjectSetInteger(gtVisualChart,id,OBJPROP_SELECTABLE,false);
      ObjectSetInteger(gtVisualChart,id,OBJPROP_TIMEFRAMES,OBJ_ALL_PERIODS);
      ObjectSetString(gtVisualChart,id,OBJPROP_TOOLTIP,VBDisplayName+" | Midnight Central VWAP | "+TimeToString(t,TIME_DATE|TIME_MINUTES)+" | "+DoubleToString(value,_Digits));
   }
   previous=t;old=value;
}
void VBRefreshLiveVisuals(datetime now)
{
   if(MQLInfoInteger(MQL_TESTER) || !VBVisualEnabled() || !VBRuntimeHistoryReady()) return;
   datetime visualDay=VBVisualDay(now);
   if(dayValid && day==visualDay) return;
   static datetime refreshed=0,previousDay=0;
   datetime minute=(datetime)((long)now/60*60);
   if(minute==refreshed) return;
   datetime anchor=VBSessionTime(visualDay);
   if(minute<=anchor) return; // No completed M1 bar in this session yet.
   datetime right=anchor+VBSessionCutoff(anchor,(int)Model)*60;
   // Date-keyed session history and broker executions survive session rollover.
   MqlRates bars[];ArraySetAsSeries(bars,false);
   if(!M1HistoryAvailable(anchor)) return;
   int count=CopyRates(_Symbol,PERIOD_M1,anchor,minute-60,bars);
   if(count<=0 || !NativeBarsValid(bars,anchor,minute)) return;
   previousDay=visualDay;refreshed=minute;
   double pv=0,volume=0;
   double sessionOpen=0;
   if(OpeningPrice(anchor,30,sessionOpen))
   {
      VBLine("Session Opening Price (SOP)",sessionOpen,clrSilver,STYLE_DOT,anchor,right);
      double mean=0;
      if(VBSessionATRMean(visualDay,mean))
      {
         VBLine("Noise Up Barrier (NUB)",sessionOpen+NoiseFraction*mean,clrOrange,STYLE_DOT,anchor,right);
      }
   }
   for(int i=0;i<count;i++)
   {
      double weight=(double)(bars[i].real_volume>0 ? bars[i].real_volume : bars[i].tick_volume);
      pv+=(bars[i].high+bars[i].low+bars[i].close)/3.0*weight;volume+=weight;
      if(volume>0) VBDrawVWAP(bars[i].time,pv/volume);
   }
   // Display projection never changes trading day, bias, attempts, readiness, or indicator accumulators.
   static bool reported=false;
   if(!reported)
   {
      int levels=0,curve=0;
      string prefix=VBVisualPrefix();
      for(int index=ObjectsTotal(gtVisualChart,0,-1)-1;index>=0;index--)
      {
         string name=ObjectName(gtVisualChart,index,0,-1);
         if(StringFind(name,prefix)!=0 || ObjectGetInteger(gtVisualChart,name,OBJPROP_TYPE)!=OBJ_TREND) continue;
         if(StringFind(name,prefix+"VWAP_")==0) curve++;else levels++;
      }
      if(levels>0 || curve>0)
      {
         PrintFormat("VB_LIVE_DISPLAY_READY levels=%d curveSegments=%d viewed=%s source=M1 session=%s",levels,curve,EnumToString((ENUM_TIMEFRAMES)Period()),TimeToString(visualDay,TIME_DATE));
         reported=true;
      }
   }
   VBVisualFit();
}

#endif
