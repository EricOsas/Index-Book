
 // Commission is learned only from a fully closed, owned broker position.
 // Tester fees are simulation settings, never evidence of a broker schedule.
 double FlashEntryChargesPerLot(ulong positionId)
 {
    if(!HistorySelectByPosition(positionId)) return 0;
    double volume=0,charges=0;
    for(int i=0;i<HistoryDealsTotal();i++)
    {
       ulong d=HistoryDealGetTicket(i);
       if(HistoryDealGetInteger(d,DEAL_ENTRY)!=DEAL_ENTRY_IN) continue;
       volume+=HistoryDealGetDouble(d,DEAL_VOLUME);
       charges-=HistoryDealGetDouble(d,DEAL_COMMISSION)+HistoryDealGetDouble(d,DEAL_FEE);
    }
    return volume>0 ? MathMax(0,charges/volume) : 0;
 }
 double FlashCommissionEstimate()
 {
    if(MQLInfoInteger(MQL_TESTER)) return CommissionRTPerLot;
    static datetime checked=0;static double observed=0;static bool known=false;
    static long login=0;static string server="";
    if(login!=AccountInfoInteger(ACCOUNT_LOGIN) || server!=AccountInfoString(ACCOUNT_SERVER))
    {login=AccountInfoInteger(ACCOUNT_LOGIN);server=AccountInfoString(ACCOUNT_SERVER);checked=0;known=false;observed=0;}
    datetime now=TimeCurrent();
    if(checked>0 && now-checked<60) return known ? observed : CommissionRTPerLot;
    checked=now;
    if(!HistorySelect(0,now+1)) return known ? observed : CommissionRTPerLot;
    ulong candidates[];int n=0;
    for(int i=HistoryDealsTotal()-1;i>=0 && n<128;i--)
    {
       ulong d=HistoryDealGetTicket(i);
       long direction=HistoryDealGetInteger(d,DEAL_ENTRY);
       if(HistoryDealGetString(d,DEAL_SYMBOL)!=_Symbol || (ulong)HistoryDealGetInteger(d,DEAL_MAGIC)!=Magic ||
          (direction!=DEAL_ENTRY_OUT && direction!=DEAL_ENTRY_OUT_BY)) continue;
       ulong id=(ulong)HistoryDealGetInteger(d,DEAL_POSITION_ID);bool duplicate=false;
       for(int j=0;j<n;j++) if(candidates[j]==id){duplicate=true;break;}
       if(!duplicate){ArrayResize(candidates,n+1);candidates[n++]=id;}
    }
    for(int j=0;j<n;j++)
    {
       if(!HistorySelectByPosition(candidates[j])) continue;
       double opened=0,closed=0,charges=0;bool valid=true;
       for(int i=0;i<HistoryDealsTotal();i++)
       {
          ulong d=HistoryDealGetTicket(i);long direction=HistoryDealGetInteger(d,DEAL_ENTRY);
          if(HistoryDealGetString(d,DEAL_SYMBOL)!=_Symbol || (ulong)HistoryDealGetInteger(d,DEAL_MAGIC)!=Magic){valid=false;break;}
          if(direction==DEAL_ENTRY_IN) opened+=HistoryDealGetDouble(d,DEAL_VOLUME);
          else if(direction==DEAL_ENTRY_OUT || direction==DEAL_ENTRY_OUT_BY) closed+=HistoryDealGetDouble(d,DEAL_VOLUME);
          else {valid=false;break;}
          charges-=HistoryDealGetDouble(d,DEAL_COMMISSION)+HistoryDealGetDouble(d,DEAL_FEE);
       }
       if(!valid || opened<=0 || MathAbs(opened-closed)>1e-8 || !MathIsValidNumber(charges)) continue;
       double rate=MathMax(0,charges/opened);
       if(!known || MathAbs(rate-observed)>1e-8)
          PrintFormat("FLASH_COMMISSION_SOURCE broker completedPosition=%I64u roundTripPerLot=%.8f",candidates[j],rate);
       known=true;observed=rate;break; // Zero is a valid observed schedule.
    }
    return known ? observed : CommissionRTPerLot;
 }
 bool FlashCostCoveredPrice(int side,double entry,double commission,double &price)
 {
    double tick=SymbolInfoDouble(_Symbol,SYMBOL_TRADE_TICK_SIZE),cash=0,slip=0;
    ENUM_ORDER_TYPE type=side==1 ? ORDER_TYPE_BUY : ORDER_TYPE_SELL;
    if(tick<=0 || !OrderCalcProfit(type,_Symbol,1.0,entry,entry+side*tick,cash) ||
       !OrderCalcProfit(type,_Symbol,1.0,entry,entry-side*SlipPrice*(MQLInfoInteger(MQL_TESTER) ? 2.0 : 1.0),slip) ||
       cash<=0 || !MathIsValidNumber(cash) || commission<0 || !MathIsValidNumber(commission)) return false;
    double raw=entry+side*(commission+MathAbs(slip))*tick/cash;
    price=NormalizeDouble((side==1 ? MathCeil(raw/tick-1e-9) : MathFloor(raw/tick+1e-9))*tick,_Digits);
    return MathIsValidNumber(price);
 }

#ifndef FLASH_STRATEGY_TRADE_EXPORT
#define FLASH_STRATEGY_TRADE_EXPORT
datetime FlashServerUTC(datetime server)
{
   return FlashClockUTC(server);
}
bool FlashExportTrades(string path)
{
   datetime begin=0,end=TimeCurrent()+1;
   string stage="TESTER";
   if(!HistorySelect(begin,end-1)) return false;
   int f=FileOpen(path,FILE_COMMON|FILE_WRITE|FILE_CSV|FILE_ANSI,',');
   if(f==INVALID_HANDLE) return false;
   FileWrite(f,"model","variant","feed","stage","run_id","position_id","exit_deal","entry_server","exit_server","entry_utc","exit_utc","central_date","side","volume","entry_price","exit_price","original_sl","original_tp","broker_net","modeled_cost","adjusted_net","risk_budget_cash","net_over_budget","real_entry_ordinal","model_attempt","exit_reason");
   string variant=SyntheticFirst ? "synthetic_first_cap"+IntegerToString(VaultAttemptCap) : Management==M1RStep ? "M1_Rstep_TP"+DoubleToString(TargetR,1) : "fixed_baseline";
   datetime entryTime=0,previousDay=0;double entryPrice=0,entryVolume=0,sl=0,tp=0,costLot=0;
   long positionId=0;int side=0,ordinal=0,exits=0;double reconstructed=0;
   bool ok=true;
   for(int i=0;i<HistoryDealsTotal();i++)
   {
      ulong deal=HistoryDealGetTicket(i);
      if((ulong)HistoryDealGetInteger(deal,DEAL_MAGIC)!=Magic || HistoryDealGetString(deal,DEAL_SYMBOL)!=_Symbol) continue;
      long direction=HistoryDealGetInteger(deal,DEAL_ENTRY);
      datetime t=(datetime)HistoryDealGetInteger(deal,DEAL_TIME);
      double qty=HistoryDealGetDouble(deal,DEAL_VOLUME),price=HistoryDealGetDouble(deal,DEAL_PRICE);
      if(direction==DEAL_ENTRY_IN)
      {
         if(entryVolume>1e-8){ok=false;break;}
         entryTime=t;entryPrice=price;entryVolume=qty;
         positionId=HistoryDealGetInteger(deal,DEAL_POSITION_ID);
         side=HistoryDealGetInteger(deal,DEAL_TYPE)==DEAL_TYPE_BUY ? 1 : -1;
         ulong order=(ulong)HistoryDealGetInteger(deal,DEAL_ORDER);
         sl=HistoryOrderGetDouble(order,ORDER_SL);tp=HistoryOrderGetDouble(order,ORDER_TP);
         double slipLoss=0;
         if(!OrderCalcProfit(ORDER_TYPE_BUY,_Symbol,1.0,price,price-2*SlipPrice,slipLoss)){ok=false;break;}
         costLot=CommissionRTPerLot+MathAbs(slipLoss);
         datetime d=FlashDay(t);if(d!=previousDay){ordinal=0;previousDay=d;}ordinal++;
      }
      else if(direction==DEAL_ENTRY_OUT || direction==DEAL_ENTRY_OUT_BY)
      {
         if(entryVolume<=0 || HistoryDealGetInteger(deal,DEAL_POSITION_ID)!=positionId || qty>entryVolume+1e-8 || FlashDay(t)!=FlashDay(entryTime)){ok=false;break;}
         double broker=HistoryDealGetDouble(deal,DEAL_PROFIT)+HistoryDealGetDouble(deal,DEAL_COMMISSION)+HistoryDealGetDouble(deal,DEAL_SWAP)+HistoryDealGetDouble(deal,DEAL_FEE);
         double entryCharge=FlashEntryChargesPerLot((ulong)positionId)*qty;
         // The history helper changes selection: restore the full export window.
         HistorySelect(begin,end-1);
         double exitCharge=HistoryDealGetDouble(deal,DEAL_COMMISSION)+HistoryDealGetDouble(deal,DEAL_FEE);
         double nativeCommission=MathMax(0,entryCharge-exitCharge);
         double cost=costLot*qty-MathMin(CommissionRTPerLot*qty,nativeCommission),net=broker-entryCharge-cost;
         FileWrite(f,FlashOutputFamily,variant,_Symbol,stage,RunId,positionId,deal,
            TimeToString(entryTime,TIME_DATE|TIME_SECONDS),TimeToString(t,TIME_DATE|TIME_SECONDS),
            TimeToString(FlashServerUTC(entryTime),TIME_DATE|TIME_SECONDS),TimeToString(FlashServerUTC(t),TIME_DATE|TIME_SECONDS),
            TimeToString(FlashDay(t),TIME_DATE),side==1 ? "LONG" : "SHORT",qty,entryPrice,price,sl,tp,
            broker,cost,net,UseFixedSize ? 0 : RiskCash,UseFixedSize ? 0 : net/RiskCash,ordinal,ordinal+(SyntheticFirst ? 1 : 0),(int)HistoryDealGetInteger(deal,DEAL_REASON));
         entryVolume-=qty;exits++;reconstructed+=net;
      }
   }
   FileClose(f);
   double expected=TesterStatistics(STAT_PROFIT)-totalCost;
   ok=ok && entryVolume<1e-8 && exits==(int)TesterStatistics(STAT_TRADES) && MathAbs(reconstructed-expected)<.01;
   PrintFormat("FLASH_TRADE_EXPORT_%s rows=%d net=%.2f expected=%.2f",ok ? "PASS" : "FAIL",exits,reconstructed,expected);
   return ok;
}
#endif

#ifndef FLASH_STRATEGY_RUNTIME
#define FLASH_STRATEGY_RUNTIME
// Owned-position lifecycle for production EAs and archived research fixtures.
bool runtimeBlocked=false;
bool runtimeRecoveryHold=false;
bool runtimeEntryPending=false;
datetime runtimeIntentTime=0;
ulong runtimeIntentNonce=0;
double runtimeIntentStop=0,runtimeIntentTarget=0;
bool FlashRecoveryIntentMatches(bool pending,datetime sent,datetime filled,double plannedStop,double plannedTarget,double brokerStop,double brokerTarget,double tick)
{
   return pending && sent>0 && filled>=sent && filled<=sent+60 && tick>0
      && plannedStop>0 && plannedTarget>0 && MathIsValidNumber(plannedStop) && MathIsValidNumber(plannedTarget)
      && MathAbs(plannedStop-brokerStop)<tick*1e-6 && MathAbs(plannedTarget-brokerTarget)<tick*1e-6;
}
bool FlashRecoveryPositionMatches(double hi,double lo,ulong actual,datetime savedTime,datetime actualTime,double risk)
{
   return MathIsValidNumber(hi) && MathIsValidNumber(lo) && hi>=0 && hi<=4294967295.0 && lo>=0 && lo<=4294967295.0
      && hi==MathFloor(hi) && lo==MathFloor(lo) && actual>0 && (((ulong)hi<<32)|(ulong)lo)==actual
      && savedTime>0 && savedTime==actualTime && MathIsValidNumber(risk) && risk>0;
}
datetime runtimeTrailLastBar=0;
bool runtimeJournalOK=true;
bool runtimeJournalTesting=false;
long runtimeAccountId=0;
long runtimeAccountMode=0;
string runtimeAccountServer="";
string runtimeAccountKey="";
int runtimeLockFile=INVALID_HANDLE;
datetime runtimeInitialSource=0;
bool runtimeHistoryReady=false;
string FlashStateKey(){return runtimeJournalTesting ? "FLASH.fixture."+RunId+"." : runtimeAccountKey;}
string FlashRuntimeOwnershipKey(long login,string server,long mode,string symbol,ulong magic)
{
   string identity=StringFormat("%I64d|%s|%I64d|%s|%I64u",login,server,mode,symbol,magic);
   ulong hash=14695981039346656037;
   for(int i=0;i<StringLen(identity);i++){hash^=(ulong)StringGetCharacter(identity,i);hash*=1099511628211;}
   return "FLASH."+StringFormat("%I64u",hash)+".";
}
void FlashStatePut(string k,double value)
{
   if(GlobalVariableSet(k,value)==0){runtimeJournalOK=false;runtimeBlocked=true;Print("FLASH JOURNAL WRITE FAILED: entries blocked");}
}
// BEGIN SELF-CONTAINED CLOSE RECOVERY
#ifndef NQ_CLOSE_RECOVERY
#define NQ_CLOSE_RECOVERY
// Exact-position close intents are separate from the original-risk checkpoint:
// a damaged risk journal must not erase an unacknowledged broker request.
bool runtimeClosePending=false,runtimeCloseAttention=false;
bool runtimeCloseRejected=false;
bool RecoveryClosePut(string key,double value)
{
   if(GlobalVariableSet(key,value)>0)return true;
   runtimeBlocked=true;runtimeCloseAttention=true;
   Print("RECOVERY ATTENTION: close-intent journal write failed; no unjournaled close request");
   return false;
}
bool RecoveryCloseReconcile(string stateKey)
{
   if(MQLInfoInteger(MQL_TESTER))return true;
   string k=stateKey+"close.";
   runtimeCloseRejected=GlobalVariableCheck(k+"retryafter") && GlobalVariableGet(k+"retryafter")>TimeCurrent();
   if(!GlobalVariableCheck(k+"valid") || GlobalVariableGet(k+"valid")==0)
   {runtimeClosePending=false;runtimeCloseAttention=false;return true;}
   runtimeClosePending=true;
   if(GlobalVariableGet(k+"valid")!=1){runtimeCloseAttention=true;return false;}
   string fields[]={"hi","lo","orderhi","orderlo","qty","time"};
   for(int i=0;i<ArraySize(fields);i++)if(!GlobalVariableCheck(k+fields[i])){runtimeCloseAttention=true;return false;}
   for(int i=0;i<4;i++)
   {
      double value=GlobalVariableGet(k+fields[i]);
      if(!MathIsValidNumber(value) || value<0 || value>4294967295.0 || value!=MathFloor(value))
      {runtimeCloseAttention=true;return false;}
   }
   double stamped=GlobalVariableGet(k+"time");
   if(!MathIsValidNumber(stamped) || stamped<=0 || stamped!=MathFloor(stamped))
   {runtimeCloseAttention=true;return false;}
   ulong position=((ulong)GlobalVariableGet(k+"hi")<<32)|(ulong)GlobalVariableGet(k+"lo");
   ulong order=((ulong)GlobalVariableGet(k+"orderhi")<<32)|(ulong)GlobalVariableGet(k+"orderlo");
   bool orderKnown=order>0;
   double requested=GlobalVariableGet(k+"qty"),remaining=0;
   long sent=(long)GlobalVariableGet(k+"time");bool present=false;
   for(int i=PositionsTotal()-1;i>=0;i--)
   {
      if(PositionGetTicket(i)==0 || (ulong)PositionGetInteger(POSITION_IDENTIFIER)!=position)continue;
      if(PositionGetString(POSITION_SYMBOL)!=_Symbol || (ulong)PositionGetInteger(POSITION_MAGIC)!=Magic)return false;
      present=true;remaining=PositionGetDouble(POSITION_VOLUME);break;
   }
   bool verified=false;
   if(position>0 && requested>0 && MathIsValidNumber(requested) && sent>0 && HistorySelectByPosition(position))
   {
      double exited=0;bool found=false,ambiguous=false;
      for(int i=0;i<HistoryDealsTotal();i++)
      {
         ulong deal=HistoryDealGetTicket(i);
         if(HistoryDealGetString(deal,DEAL_SYMBOL)!=_Symbol || HistoryDealGetInteger(deal,DEAL_ENTRY)!=DEAL_ENTRY_OUT
            || HistoryDealGetInteger(deal,DEAL_TIME_MSC)<sent)continue;
         if(!present){verified=true;break;}
         if((ulong)HistoryDealGetInteger(deal,DEAL_MAGIC)!=Magic)continue;
         ulong candidate=(ulong)HistoryDealGetInteger(deal,DEAL_ORDER);
         if(orderKnown && candidate!=order)continue;
         if(!found){order=candidate;found=true;}
         else if(order!=candidate){ambiguous=true;break;}
         exited+=HistoryDealGetDouble(deal,DEAL_VOLUME);
      }
      if(present && found && !ambiguous)
      {
         long state=HistoryOrderGetInteger(order,ORDER_STATE);
         bool terminal=state==ORDER_STATE_FILLED || state==ORDER_STATE_CANCELED || state==ORDER_STATE_EXPIRED;
         double step=SymbolInfoDouble(_Symbol,SYMBOL_VOLUME_STEP);
         verified=terminal && step>0 && exited>0 && exited<=requested+step*1e-6
            && remaining<=requested-exited+step*1e-6;
      }
      if(present && !ambiguous && orderKnown && order>0 && HistoryOrderSelect(order)
         && HistoryOrderGetInteger(order,ORDER_POSITION_ID)==(long)position
         && (HistoryOrderGetInteger(order,ORDER_STATE)==ORDER_STATE_REJECTED
            || HistoryOrderGetInteger(order,ORDER_STATE)==ORDER_STATE_CANCELED)
         && HistoryOrderGetDouble(order,ORDER_VOLUME_CURRENT)==HistoryOrderGetDouble(order,ORDER_VOLUME_INITIAL))verified=true;
   }
   if(verified && RecoveryClosePut(k+"valid",0))
   {GlobalVariablesFlush();runtimeClosePending=false;runtimeCloseAttention=false;return true;}
   if(!runtimeCloseAttention)Print("RECOVERY ATTENTION: pending close is unconfirmed; protection retained, no duplicate request");
   runtimeCloseAttention=true;return false;
}
bool RecoveryCloseBegin(string stateKey,ulong ticket)
{
   if(MQLInfoInteger(MQL_TESTER))return true;
   if(!TerminalInfoInteger(TERMINAL_CONNECTED) || !RecoveryCloseReconcile(stateKey) || runtimeCloseRejected || !PositionSelectByTicket(ticket))return false;
   if(PositionGetString(POSITION_SYMBOL)!=_Symbol || (ulong)PositionGetInteger(POSITION_MAGIC)!=Magic)return false;
   ulong position=(ulong)PositionGetInteger(POSITION_IDENTIFIER);string k=stateKey+"close.";
   bool ok=RecoveryClosePut(k+"valid",-1); // Interrupted intent construction must also fail closed.
   ok=RecoveryClosePut(k+"hi",(double)(position>>32)) && ok;ok=RecoveryClosePut(k+"lo",(double)(position&0xffffffff)) && ok;
   ok=RecoveryClosePut(k+"qty",PositionGetDouble(POSITION_VOLUME)) && ok;
   ok=RecoveryClosePut(k+"time",(double)((long)TimeCurrent()*1000)) && ok;
   ok=RecoveryClosePut(k+"orderhi",0) && ok;ok=RecoveryClosePut(k+"orderlo",0) && ok;
   if(ok)ok=RecoveryClosePut(k+"valid",1);
   GlobalVariablesFlush();runtimeClosePending=true;return ok;
}
void RecoveryCloseResult(string stateKey,uint ret,ulong order)
{
   if(MQLInfoInteger(MQL_TESTER))return;
   string k=stateKey+"close.";
   // Only explicit rejection retires intent immediately. Even DONE/partial
   // requires history and the position snapshot to acknowledge the quantity.
   bool terminal=ret==10004 || ret==10006 || ret==10007 || (ret>=10013 && ret<=10022)
      || ret==10024 || ret==10026 || ret==10027 || ret==10029 || ret==10030
      || ret==10032 || ret==10033 || ret==10034 || ret==10035 || ret==10038 || ret==10040;
   if(terminal && RecoveryClosePut(k+"valid",0))
   {
      if(ret!=TRADE_RETCODE_DONE && ret!=TRADE_RETCODE_DONE_PARTIAL && ret!=10036)
      {RecoveryClosePut(k+"retryafter",(double)(TimeCurrent()+5));runtimeCloseRejected=true;Print("CLOSE REJECTED: protection retained; retries paused for five seconds");}
      GlobalVariablesFlush();runtimeClosePending=false;runtimeCloseAttention=false;return;
   }
   RecoveryClosePut(k+"orderhi",(double)(order>>32));RecoveryClosePut(k+"orderlo",(double)(order&0xffffffff));GlobalVariablesFlush();
   RecoveryCloseReconcile(stateKey);
}
#endif
// END SELF-CONTAINED CLOSE RECOVERY
void FlashStateSave(int reserved=-1)
{
   if(runtimeRecoveryHold) return;
   if((MQLInfoInteger(MQL_TESTER) && !runtimeJournalTesting) || day==0) return;
   if(!runtimeJournalTesting && !FlashRuntimeAccountContextValid()) return;
   string k=FlashStateKey();
   runtimeJournalOK=true;
   FlashStatePut(k+"valid",0);
   FlashStatePut(k+"day",(double)day);
   FlashStatePut(k+"entries",reserved>=0 ? reserved : entries);
   FlashStatePut(k+"firstside",firstSide);
   FlashStatePut(k+"retryarmed",retryArmed ? 1 : 0);
   FlashStatePut(k+"retryafter",(double)retryEligibleAfter);
   FlashStatePut(k+"signal",(double)lastSignal);
   FlashStatePut(k+"virtual",virtualActive ? 1 : 0);
   FlashStatePut(k+"vstart",(double)virtualStart);
   FlashStatePut(k+"vlast",(double)virtualLastBar);
   FlashStatePut(k+"vsl",virtualStop);FlashStatePut(k+"vtp",virtualTarget);
   FlashStatePut(k+"risk",initialRisk);FlashStatePut(k+"peak",barPeakR);
   FlashStatePut(k+"costlot",modelCostPerLot);
   FlashStatePut(k+"commlot",entryCommissionPerLot);
   FlashStatePut(k+"entrytime",(double)ownedEntryTime);
   ulong position=(!runtimeEntryPending && OwnPosition()) ? (ulong)PositionGetInteger(POSITION_IDENTIFIER) : 0;
   FlashStatePut(k+"poshi",(double)(position>>32));FlashStatePut(k+"poslo",(double)(position&0xffffffff));
   FlashStatePut(k+"pending",runtimeEntryPending ? 1 : 0);FlashStatePut(k+"trailbar",(double)runtimeTrailLastBar);
   FlashStatePut(k+"intenttime",(double)runtimeIntentTime);FlashStatePut(k+"intentsl",runtimeIntentStop);FlashStatePut(k+"intenttp",runtimeIntentTarget);
   FlashStatePut(k+"flash_peakh",flashPeakM1High);FlashStatePut(k+"flash_active",flashTrailActive ? 1 : 0);
   FlashStatePut(k+"flash_lastbar",(double)flashTrailLastBar);FlashStatePut(k+"flash_stop",flashTrailStop);
   FlashStatePut(k+"intentnonce",(double)runtimeIntentNonce);
   FlashStatePut(k+"blocked",runtimeBlocked ? 1 : 0);
   FlashStatePut(k+"valid",runtimeJournalOK ? 1 : 0);GlobalVariablesFlush();
}
int FlashHistoryEntries(datetime now)
{
   if(!HistorySelect(FlashSessionTime(FlashDay(now)),now)) return -1;
   ulong seen[];int n=0;
   for(int i=0;i<HistoryDealsTotal();i++)
   {
      ulong deal=HistoryDealGetTicket(i);
      if(HistoryDealGetString(deal,DEAL_SYMBOL)!=_Symbol || (ulong)HistoryDealGetInteger(deal,DEAL_MAGIC)!=Magic || HistoryDealGetInteger(deal,DEAL_ENTRY)!=DEAL_ENTRY_IN) continue;
      ulong order=(ulong)HistoryDealGetInteger(deal,DEAL_ORDER);bool found=false;
      for(int j=0;j<n;j++) if(seen[j]==order){found=true;break;}
      if(!found){ArrayResize(seen,n+1);seen[n++]=order;}
   }
   return n;
}
bool FlashLegacyPositionRiskVerified()
{
   if(initialRisk<=0 || ownedEntryTime!=(datetime)PositionGetInteger(POSITION_TIME)) return false;
   ulong position=(ulong)PositionGetInteger(POSITION_IDENTIFIER);
   double entry=PositionGetDouble(POSITION_PRICE_OPEN),tick=SymbolInfoDouble(_Symbol,SYMBOL_TRADE_TICK_SIZE);
   if(tick<=0 || !HistorySelectByPosition(position)) return false;
   for(int i=0;i<HistoryDealsTotal();i++)
   {
      ulong deal=HistoryDealGetTicket(i);
      if(HistoryDealGetInteger(deal,DEAL_ENTRY)!=DEAL_ENTRY_IN || HistoryDealGetString(deal,DEAL_SYMBOL)!=_Symbol
         || (ulong)HistoryDealGetInteger(deal,DEAL_MAGIC)!=Magic) continue;
      ulong order=(ulong)HistoryDealGetInteger(deal,DEAL_ORDER);
      double stop=HistoryOrderGetDouble(order,ORDER_SL);
      return stop>0 && MathAbs(MathAbs(entry-stop)-initialRisk)<tick*1e-6;
   }
   return false;
}
void FlashStateRestore(datetime now)
{
   if(MQLInfoInteger(MQL_TESTER) && !runtimeJournalTesting) return;
   RecoveryCloseReconcile(FlashStateKey());
   string k=FlashStateKey();int actual=FlashHistoryEntries(now);
   if(actual<0){runtimeBlocked=true;runtimeRecoveryHold=true;initialRisk=0;Note("RECOVERY_BLOCK","History unavailable; broker protection retained");return;}
   bool same=GlobalVariableCheck(k+"day") && (datetime)GlobalVariableGet(k+"day")==day;
   bool ownPos=OwnPosition();
   ulong position=ownPos ? (ulong)PositionGetInteger(POSITION_IDENTIFIER) : 0;
   bool posMatches = ownPos && GlobalVariableCheck(k+"poshi") && GlobalVariableCheck(k+"poslo")
      && FlashRecoveryPositionMatches(GlobalVariableGet(k+"poshi"),GlobalVariableGet(k+"poslo"),position,
         (datetime)GlobalVariableGet(k+"entrytime"),(datetime)PositionGetInteger(POSITION_TIME),
         GlobalVariableGet(k+"risk"));
   if(same || posMatches)
   {
      string fields[]={"valid","entries","signal","virtual","vstart","vlast","vsl","vtp","risk","peak","costlot","entrytime","blocked"};
      for(int i=0;i<ArraySize(fields);i++)
         if(!GlobalVariableCheck(k+fields[i]))
         {runtimeBlocked=true;runtimeRecoveryHold=true;initialRisk=0;Note("RECOVERY_BLOCK","Missing journal field; broker protection retained");return;}
   }
   if(same && GlobalVariableGet(k+"valid")!=1)
   {runtimeBlocked=true;runtimeRecoveryHold=true;initialRisk=0;entries=Model==VaultBreak ? VaultAttemptCap : 1;Note("RECOVERY_BLOCK","Incomplete state commit; broker protection retained");return;}
   if(same)
   {
      entries=(int)GlobalVariableGet(k+"entries");
      firstSide=GlobalVariableCheck(k+"firstside") ? (int)GlobalVariableGet(k+"firstside") : 0;
      retryArmed=GlobalVariableCheck(k+"retryarmed") && GlobalVariableGet(k+"retryarmed")==1;
      retryEligibleAfter=GlobalVariableCheck(k+"retryafter") ? (datetime)GlobalVariableGet(k+"retryafter") : 0;
      lastSignal=(datetime)GlobalVariableGet(k+"signal");
      virtualActive=GlobalVariableGet(k+"virtual")==1;
      virtualStart=(datetime)GlobalVariableGet(k+"vstart");
      virtualLastBar=(datetime)GlobalVariableGet(k+"vlast");
      virtualStop=GlobalVariableGet(k+"vsl");virtualTarget=GlobalVariableGet(k+"vtp");
      initialRisk=GlobalVariableGet(k+"risk");barPeakR=GlobalVariableGet(k+"peak");
      modelCostPerLot=GlobalVariableGet(k+"costlot");
      entryCommissionPerLot=GlobalVariableCheck(k+"commlot") ? GlobalVariableGet(k+"commlot") : CommissionRTPerLot;
      ownedEntryTime=(datetime)GlobalVariableGet(k+"entrytime");
      runtimeTrailLastBar=GlobalVariableCheck(k+"trailbar") ? (datetime)GlobalVariableGet(k+"trailbar") : 0;
      runtimeBlocked=GlobalVariableGet(k+"blocked")==1;
      if(GlobalVariableCheck(k+"flash_peakh")) flashPeakM1High=GlobalVariableGet(k+"flash_peakh");
      if(GlobalVariableCheck(k+"flash_active")) flashTrailActive=GlobalVariableGet(k+"flash_active")==1;
      if(GlobalVariableCheck(k+"flash_lastbar")) flashTrailLastBar=(datetime)GlobalVariableGet(k+"flash_lastbar");
      if(GlobalVariableCheck(k+"flash_stop")) flashTrailStop=GlobalVariableGet(k+"flash_stop");
   }
   else
   {
      runtimeBlocked=false;entries=actual;
      if(SyntheticFirst && actual>0) entries++;
   }
   entries=MathMax(entries,actual+(SyntheticFirst && actual>0 ? 1 : 0));
   // Attachment cannot replay an old signal, even when no journal exists.
   lastSignal=MathMax(lastSignal,runtimeInitialSource);
   if(same && !OwnPosition() && GlobalVariableCheck(k+"pending") && GlobalVariableGet(k+"pending")==1 && actual<entries)
   {runtimeBlocked=true;runtimeRecoveryHold=true;Note("RECOVERY_BLOCK","Unresolved pre-send entry reservation; no replacement order");}
   if(OwnPosition())
   {
	  if(firstSide==0) firstSide=PositionGetInteger(POSITION_TYPE)==POSITION_TYPE_BUY ? 1 : -1;
      ulong position=(ulong)PositionGetInteger(POSITION_IDENTIFIER);
      if(same && !GlobalVariableCheck(k+"poshi") && !GlobalVariableCheck(k+"poslo") && FlashLegacyPositionRiskVerified())
      {
         runtimeTrailLastBar=ownedEntryTime/60*60-60;
         FlashStateSave();Note("RECOVERY_LEGACY_VERIFIED","Entry-order stop verifies original risk; replay completed closes from entry");
      }
      bool pending=GlobalVariableCheck(k+"pending") && GlobalVariableGet(k+"pending")==1;
      if(same && pending && GlobalVariableCheck(k+"intentnonce") && PositionGetString(POSITION_COMMENT)=="FLASH#"+IntegerToString((long)GlobalVariableGet(k+"intentnonce"))
         && GlobalVariableCheck(k+"intenttime") && GlobalVariableCheck(k+"intentsl") && GlobalVariableCheck(k+"intenttp")
         && FlashRecoveryIntentMatches(pending,(datetime)GlobalVariableGet(k+"intenttime"),(datetime)PositionGetInteger(POSITION_TIME),
            GlobalVariableGet(k+"intentsl"),GlobalVariableGet(k+"intenttp"),PositionGetDouble(POSITION_SL),PositionGetDouble(POSITION_TP),SymbolInfoDouble(_Symbol,SYMBOL_TRADE_TICK_SIZE)))
      {
         initialRisk=MathAbs(PositionGetDouble(POSITION_PRICE_OPEN)-PositionGetDouble(POSITION_SL));
         ownedEntryTime=(datetime)PositionGetInteger(POSITION_TIME);barPeakR=0;runtimeTrailLastBar=0;runtimeEntryPending=false;
         FlashStateSave();Note("RECOVERY_FILLED_INTENT","Broker bracket matches pre-send reservation");
      }
      bool bound=(same || posMatches) && GlobalVariableCheck(k+"poshi") && GlobalVariableCheck(k+"poslo")
         && FlashRecoveryPositionMatches(GlobalVariableGet(k+"poshi"),GlobalVariableGet(k+"poslo"),position,
            ownedEntryTime,(datetime)PositionGetInteger(POSITION_TIME),initialRisk);
      if(!bound || !GlobalVariableCheck(k+"trailbar") || !MathIsValidNumber(barPeakR) || barPeakR<0
         || runtimeTrailLastBar>now || !MathIsValidNumber(modelCostPerLot) || modelCostPerLot<0
         || !MathIsValidNumber(entryCommissionPerLot) || entryCommissionPerLot<0 || initialRisk<=0 || !MathIsValidNumber(initialRisk)
         || ownedEntryTime!=(datetime)PositionGetInteger(POSITION_TIME))
      {runtimeBlocked=true;runtimeRecoveryHold=true;initialRisk=0;Note("RECOVERY_BLOCK","Original-risk/trail journal cannot identify owned position; broker protection retained");}
   }
   FlashStateSave();
}
datetime FlashDate(int year,int month,int date)
{
   MqlDateTime p={};p.year=year;p.mon=month;p.day=date;return StructToTime(p);
}
int FlashWeekday(datetime t){MqlDateTime p;TimeToStruct(t,p);return p.day_of_week;}
datetime FlashNthSunday(int year,int month,int n)
{
   datetime first=FlashDate(year,month,1);
   return first+((7-FlashWeekday(first))%7+7*(n-1))*86400;
}
bool FlashBrokerClockValid()
{
   return FlashClockRefresh(MQLInfoInteger(MQL_TESTER) ? TimeCurrent() : TimeTradeServer());
}
bool FlashRuntimeInit()
{
   if(MQLInfoInteger(MQL_TESTER)) return true;
   if(AccountInfoInteger(ACCOUNT_MARGIN_MODE)!=ACCOUNT_MARGIN_MODE_RETAIL_HEDGING)
   {Print("FLASH EA BLOCK: hedging account required for independent USTEC sleeves");return false;}
   if(Magic==0 || Management==VWAPClose || Management==CostCoveredBE || Management==ATRTrail)
   {Print("FLASH EA BLOCK: invalid model identity or unsupported management rule");return false;}
   if(!FlashBrokerClockValid()) Print("FLASH CLOCK_WAIT: requesting history for automatic weekly-opening clock detection");
   runtimeAccountId=AccountInfoInteger(ACCOUNT_LOGIN);
   runtimeAccountMode=AccountInfoInteger(ACCOUNT_TRADE_MODE);
   runtimeAccountServer=AccountInfoString(ACCOUNT_SERVER);
   runtimeAccountKey=FlashRuntimeOwnershipKey(runtimeAccountId,runtimeAccountServer,runtimeAccountMode,_Symbol,Magic);
   FolderCreate("FlashTriggerORBLocks",FILE_COMMON);
   runtimeLockFile=FileOpen("FlashTriggerORBLocks/"+FlashStateKey()+"lock",FILE_COMMON|FILE_BIN|FILE_READ|FILE_WRITE);
   if(runtimeLockFile==INVALID_HANDLE){Print("FLASH EA BLOCK: duplicate instance or exclusive ownership lock unavailable");return false;}
   // Indicator calculations/history load asynchronously after handles are created.
   // Keep the EA attached; the entry path waits without disabling recovery/timer protection.
   FlashRuntimeHistoryReady();
   EventSetTimer(1);
   return true;
}
bool FlashRuntimeHistoryReady()
{
   if(MQLInfoInteger(MQL_TESTER)) return true;
   static string runtimeWaitReason="";
   if(!TerminalInfoInteger(TERMINAL_CONNECTED) || (runtimeHistoryReady && !SeriesInfoInteger(_Symbol,PERIOD_M1,SERIES_SYNCHRONIZED)))
   {
      dayWaiting=true;
      string reason=TerminalInfoInteger(TERMINAL_CONNECTED) ? "M1_SYNC_WAIT" : "CONNECTION_WAIT";
      dataWaitReason=reason;
      if(runtimeWaitReason!=reason)
      {runtimeWaitReason=reason;Print("FLASH DATA_WAIT: "+reason+"; new entries paused, broker protection retained");}
      return false;
   }
   if(!FlashBrokerClockValid()) return false;
   if(runtimeHistoryReady)
   {
      if(runtimeWaitReason!="")
      {
         runtimeWaitReason="";Print("FLASH DATA_READY: connection and M1 synchronization restored");
         if(dataWaitReason=="M1_SYNC_WAIT" || dataWaitReason=="CONNECTION_WAIT" || dataWaitReason=="M1_HISTORY_WAIT")
         {lastSignal=MathMax(lastSignal,iTime(_Symbol,sourceTF,1));dayWaiting=false;dataWaitReason="";}
      }
      return true;
   }
   datetime source=iTime(_Symbol,sourceTF,1);
   MqlRates warm[];
   datetime from=FlashSessionTime(FlashDay(TimeCurrent()))-(Model==FlashTriggerORB ? 3600 : 0);
   int minutes=CopyRates(_Symbol,PERIOD_M1,from,TimeCurrent(),warm);
   bool ready=source>0 && minutes>0 && SeriesInfoInteger(_Symbol,PERIOD_M1,SERIES_SYNCHRONIZED)
      && (Model==VaultBreak || BarsCalculated(adxHandle)>=ADXPeriod+2);
   double atr[],adx[];
   double sessionMean=0;
   if(ready) ready=FlashSessionATRMean(FlashDay(TimeCurrent()),sessionMean);
   if(ready && Model!=VaultBreak) ready=CopyBuffer(adxHandle,0,1,2,adx)==2;
   if(!ready)
   {
      static bool announced=false;
      if(!announced){Print("FLASH HISTORY_WAIT: loading M1/source/daily indicators; new entries blocked, protection active");announced=true;}
      return false;
   }
   runtimeInitialSource=source;
   lastSignal=MathMax(lastSignal,source); // Do not replay a candle that closed during attachment/loading.
   runtimeHistoryReady=true;
   runtimeWaitReason="";
   Print("FLASH HISTORY_READY: data loaded; waiting for a fresh completed-bar signal");
   return true;
}
bool FlashRuntimeAccountAllowed(long mode,long margin,bool enabled)
{
   // The toggle controls new entries, not initialization/recovery or account type.
   return margin==ACCOUNT_MARGIN_MODE_RETAIL_HEDGING;
}
bool FlashRuntimeAccountContextValid()
{
   if(AccountInfoInteger(ACCOUNT_LOGIN)!=runtimeAccountId) return false;
   return AccountInfoInteger(ACCOUNT_TRADE_MODE)==runtimeAccountMode
      && AccountInfoString(ACCOUNT_SERVER)==runtimeAccountServer
      && AccountInfoInteger(ACCOUNT_MARGIN_MODE)==ACCOUNT_MARGIN_MODE_RETAIL_HEDGING;
}
bool FlashRuntimeSelfTest()
{
   if(!MQLInfoInteger(MQL_TESTER)) return false;
   string owner=FlashRuntimeOwnershipKey(1,"server-a",ACCOUNT_TRADE_MODE_DEMO,"USTEC",Magic);
   if(owner==FlashRuntimeOwnershipKey(1,"server-b",ACCOUNT_TRADE_MODE_DEMO,"USTEC",Magic)
      || owner==FlashRuntimeOwnershipKey(1,"server-a",ACCOUNT_TRADE_MODE_REAL,"USTEC",Magic)
      || owner==FlashRuntimeOwnershipKey(2,"server-a",ACCOUNT_TRADE_MODE_DEMO,"USTEC",Magic)
      || owner==FlashRuntimeOwnershipKey(1,"server-a",ACCOUNT_TRADE_MODE_DEMO,"USTEC",Magic+1)) return false;
   if(!FlashRuntimeAccountAllowed(ACCOUNT_TRADE_MODE_REAL,ACCOUNT_MARGIN_MODE_RETAIL_HEDGING,true)
      || !FlashRuntimeAccountAllowed(ACCOUNT_TRADE_MODE_DEMO,ACCOUNT_MARGIN_MODE_RETAIL_HEDGING,false)
      || FlashRuntimeAccountAllowed(ACCOUNT_TRADE_MODE_REAL,ACCOUNT_MARGIN_MODE_RETAIL_NETTING,true)) return false;
   datetime oldDay=day,oldSignal=lastSignal,oldStart=virtualStart,oldLast=virtualLastBar,oldEntry=ownedEntryTime;
   int oldEntries=entries;bool oldVirtual=virtualActive;
   double oldStop=virtualStop,oldTarget=virtualTarget,oldRisk=initialRisk,oldPeak=barPeakR;
   bool oldHold=runtimeRecoveryHold,oldPending=runtimeEntryPending;
   runtimeJournalTesting=true;runtimeBlocked=false;
   day=D'2022.01.24';entries=2;lastSignal=day+18*3600;
   virtualActive=true;virtualStart=lastSignal;virtualLastBar=lastSignal;
   virtualStop=100;virtualTarget=210;initialRisk=70;barPeakR=1.5;ownedEntryTime=0;
   string key=FlashStateKey();FlashStateSave();
   entries=0;lastSignal=0;virtualActive=false;initialRisk=0;barPeakR=0;
   FlashStateRestore(day+19*3600);
   bool ok=runtimeJournalOK && !runtimeBlocked && entries==2 && lastSignal==day+18*3600
      && virtualActive && virtualStop==100 && virtualTarget==210 && initialRisk==70 && barPeakR==1.5;
   GlobalVariableSet(key+"valid",0);runtimeBlocked=false;FlashStateRestore(day+19*3600);
   ok=ok && runtimeBlocked;
   GlobalVariableSet(key+"valid",1);GlobalVariableDel(key+"risk");runtimeBlocked=false;FlashStateRestore(day+19*3600);
   ok=ok && runtimeBlocked;
   GlobalVariablesDeleteAll(key);
   FolderCreate("FlashTriggerORBLocks",FILE_COMMON);
   string lock="FlashTriggerORBLocks/fixture_"+RunId+".lock";
   int first=FileOpen(lock,FILE_COMMON|FILE_BIN|FILE_READ|FILE_WRITE);
   int second=FileOpen(lock,FILE_COMMON|FILE_BIN|FILE_READ|FILE_WRITE);
   ok=ok && first!=INVALID_HANDLE && second==INVALID_HANDLE;
   if(second!=INVALID_HANDLE) FileClose(second);
   if(first!=INVALID_HANDLE) FileClose(first);
   FileDelete(lock,FILE_COMMON);
   day=oldDay;entries=oldEntries;lastSignal=oldSignal;virtualActive=oldVirtual;
   virtualStart=oldStart;virtualLastBar=oldLast;ownedEntryTime=oldEntry;
   virtualStop=oldStop;virtualTarget=oldTarget;initialRisk=oldRisk;barPeakR=oldPeak;
   runtimeBlocked=false;runtimeJournalTesting=false;runtimeRecoveryHold=oldHold;runtimeEntryPending=oldPending;
   Print(ok ? "FLASH_RUNTIME_SELFTEST_PASS account guard / restart / corruption / exclusive ownership" : "FLASH_RUNTIME_SELFTEST_FAILED");
   return ok;
}
bool FlashRuntimeEntryAllowed(double entry,double stop,double target,double volume,int side)
{
   if(!EnableTrading) return false;
   if(MQLInfoInteger(MQL_TESTER)) return true;
   if(runtimeBlocked || !FlashRuntimeAccountContextValid() || !FlashBrokerClockValid() || !RecoveryCloseReconcile(FlashStateKey())) return false;
   if(!TerminalInfoInteger(TERMINAL_CONNECTED) || !TerminalInfoInteger(TERMINAL_TRADE_ALLOWED) || !MQLInfoInteger(MQL_TRADE_ALLOWED) || !AccountInfoInteger(ACCOUNT_TRADE_ALLOWED)) return false;
   MqlTick q;if(!SymbolInfoTick(_Symbol,q) || TimeCurrent()-q.time>30) return false;
   double reference=side==1 ? q.bid : q.ask;
   if(stop>0.0 || target>0.0)
   {
      double bound=SymbolInfoInteger(_Symbol,SYMBOL_TRADE_STOPS_LEVEL)*_Point;
      if(stop>0.0 && side*(reference-stop)<bound) return false;
      if(target>0.0 && side*(target-reference)<bound) return false;
   }
   double margin=0;
   if(!OrderCalcMargin(side==1 ? ORDER_TYPE_BUY : ORDER_TYPE_SELL,_Symbol,volume,entry,margin) || margin>AccountInfoDouble(ACCOUNT_MARGIN_FREE)) return false;
   // Reserve the attempt before submission: interrupted requests cannot be replayed.
   runtimeEntryPending=true;runtimeIntentTime=TimeCurrent();runtimeIntentNonce=(ulong)runtimeIntentTime*1000+GetTickCount()%1000;
   runtimeIntentStop=stop;runtimeIntentTarget=target;FlashStateSave(entries+1);
   return !runtimeBlocked && runtimeJournalOK;
}
void FlashRuntimeFilled(int side,double stop,double friction)
{
   if(MQLInfoInteger(MQL_TESTER)) return;
   if(!OwnPosition()){runtimeBlocked=true;Note("FILL_BLOCK","Accepted request has no owned position");FlashStateSave();return;}
   double volume=PositionGetDouble(POSITION_VOLUME),price=PositionGetDouble(POSITION_PRICE_OPEN),loss=0;
   initialRisk=MathAbs(price-stop);ownedEntryTime=(datetime)PositionGetInteger(POSITION_TIME);
   runtimeEntryPending=false;runtimeTrailLastBar=0;
   bool valid=PositionGetDouble(POSITION_SL)>0 && PositionGetDouble(POSITION_TP)>0
      && OrderCalcProfit(side==1 ? ORDER_TYPE_BUY : ORDER_TYPE_SELL,_Symbol,volume,price,stop,loss)
      && (UseFixedSize || MathAbs(loss)+friction*volume<=RiskCash+0.01);
   if(!valid){runtimeBlocked=true;CloseOwned("FILLED_RISK_OR_PROTECTION");Note("FILL_BLOCK","Actual fill breached planned budget or protection");}
   FlashStateSave();
}
void OnTimer()
{
   if(MQLInfoInteger(MQL_TESTER)) return;
   if(!FlashRuntimeAccountContextValid()){FlashDraw();return;}
   RecoveryCloseReconcile(FlashStateKey());
   datetime now=TimeTradeServer();
   if(OwnPosition() && (PositionGetDouble(POSITION_SL)<=0 || PositionGetDouble(POSITION_TP)<=0))
   {runtimeBlocked=true;CloseOwned("PROTECTION_MISSING");FlashStateSave();}
   FlashRuntimeCutoffCheck(now);
   FlashRuntimeHistoryReady();
   FlashRefreshLiveVisuals(now);
   FlashDraw();
}
void FlashRuntimeCutoffCheck(datetime now)
{
   if(!FlashBrokerClockValid()) return;
   if(!OwnPosition()) return;
   datetime posTime = (datetime)PositionGetInteger(POSITION_TIME);
   int cutoff = FlashSessionCutoff(now, (int)Model);
   int minute = FlashMinute(now);
   if(FlashDay(posTime) < FlashDay(now) - 2*86400)
   {
      CloseOwned("TIMER_OVERDUE_RECOVERY");
   }
   else if(cutoff < 1439 && minute >= cutoff)
   {
      CloseOwned("TIMER_CUTOFF_RECOVERY");
   }
}
#ifdef FLASH_LIFECYCLE_PROBE
bool gtLifecycleDone=false;
bool FlashNativeLifecycleProbe()
{
   if(!MQLInfoInteger(MQL_TESTER)) return false;
   if(gtLifecycleDone || FlashMinute(TimeCurrent())<600 || FlashMinute(TimeCurrent())>=870) return false;
   gtLifecycleDone=true;bool ok=AccountInfoInteger(ACCOUNT_MARGIN_MODE)==ACCOUNT_MARGIN_MODE_RETAIL_HEDGING;
   MqlTick q;ok=SymbolInfoTick(_Symbol,q) && ok;
   double minlot=SymbolInfoDouble(_Symbol,SYMBOL_VOLUME_MIN);
   CTrade foreign;foreign.SetExpertMagicNumber(Magic+999);
   foreign.SetTypeFillingBySymbol(_Symbol);
   ok=foreign.Buy(minlot,_Symbol,0,RoundPrice(q.ask-StopPoints),RoundPrice(q.ask+TargetPoints),"FLASH fixture foreign") && ok;
   ulong foreignTicket=0;double foreignStop=0,foreignTarget=0,foreignVolume=0;
   for(int i=PositionsTotal()-1;i>=0;i--)
   {
      ulong ticket=PositionGetTicket(i);
      if((ulong)PositionGetInteger(POSITION_MAGIC)==Magic+999)
      {foreignTicket=ticket;foreignStop=PositionGetDouble(POSITION_SL);foreignTarget=PositionGetDouble(POSITION_TP);foreignVolume=PositionGetDouble(POSITION_VOLUME);}
   }
   ok=foreignTicket>0 && !OwnPosition() && ok;
   ok=Open(1,RoundPrice(q.ask-StopPoints),RoundPrice(q.ask+TargetPoints),0) && ok;
   ok=OwnPosition() && PositionGetDouble(POSITION_SL)>0 && PositionGetDouble(POSITION_TP)>0 && ok;
   ulong ownTicket=(ulong)PositionGetInteger(POSITION_TICKET);
   day=FlashDay(TimeCurrent());lastSignal=iTime(_Symbol,sourceTF,1);
   runtimeJournalTesting=true;runtimeBlocked=false;string journal=FlashStateKey();FlashStateSave();
   initialRisk=0;ownedEntryTime=0;entries=0;
   FlashStateRestore(TimeCurrent());
   ok=!runtimeBlocked && initialRisk>0 && ownedEntryTime>0 && OwnPosition()
      && (ulong)PositionGetInteger(POSITION_TICKET)==ownTicket && ok;
   bool rejected=!trade.Buy(0,_Symbol,0,0,0,"FLASH invalid-volume fixture");
   bool budgetBlocked=!Open(1,SymbolInfoDouble(_Symbol,SYMBOL_TRADE_TICK_SIZE),q.ask+TargetPoints,0);
   ok=rejected && budgetBlocked && OwnPosition() && ok;
   double ownedVolume=PositionGetDouble(POSITION_VOLUME);
   double step=SymbolInfoDouble(_Symbol,SYMBOL_VOLUME_STEP);
   double partial=MathFloor(ownedVolume/2/step)*step;
   if(partial>=minlot && ownedVolume-partial>=minlot)
   {
      ok=trade.PositionClosePartial(ownTicket,partial) && OwnPosition()
         && MathAbs(PositionGetDouble(POSITION_VOLUME)-(ownedVolume-partial))<1e-8
         && PositionGetDouble(POSITION_SL)>0 && PositionGetDouble(POSITION_TP)>0 && ok;
   }
   else ok=false;
   FlashRuntimeCutoffCheck(FlashSessionTime(day)+870*60);
   ok=!OwnPosition() && PositionSelectByTicket(foreignTicket)
      && PositionGetDouble(POSITION_VOLUME)==foreignVolume
      && PositionGetDouble(POSITION_SL)==foreignStop && PositionGetDouble(POSITION_TP)==foreignTarget && ok;
   GlobalVariablesDeleteAll(journal);runtimeJournalTesting=false;
   // Only this fixture's own deliberately foreign-tagged position is cleaned up here.
   if(foreignTicket>0) ok=foreign.PositionClose(foreignTicket) && ok;
   int f=FileOpen(FlashOutputRoot+"/native_lifecycle_"+RunId+".txt",FILE_COMMON|FILE_WRITE|FILE_TXT|FILE_ANSI);
   if(f!=INVALID_HANDLE)
   {FileWriteString(f,ok ? "PASS: native hedging ownership, staged SL/TP, original-risk restart, invalid-volume rejection, minimum-risk refusal, partial close protection, timer cutoff and foreign protection preserved." : "FAIL: native lifecycle fixture");FileClose(f);}
   Print(ok ? "FLASH_NATIVE_LIFECYCLE_PASS" : "FLASH_NATIVE_LIFECYCLE_FAILED");
   TesterStop();return true;
}
#endif
#endif
