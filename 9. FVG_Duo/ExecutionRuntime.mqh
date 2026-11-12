//+------------------------------------------------------------------+
//|                                         ExecutionRuntime.mqh     |
//| Execution, order tracking, recovery and sizing for FVG Duo       |
//| Developed by Osamwonyi Eric (@You_FoundEric)                     |
//+------------------------------------------------------------------+
#ifndef FVG_EXECUTION_RUNTIME
#define FVG_EXECUTION_RUNTIME

#include <Trade/Trade.mqh>

CTrade extTrade;

// State of owned position
ulong    ownedPositionId   = 0;
ulong    ownedTicket       = 0;
int      ownedSide         = 0;       // +1 = LONG, -1 = SHORT
string   ownedSource       = "";      // "OD", "TREND", "MERGED"
double   ownedEntryPrice   = 0.0;
double   ownedStopPrice    = 0.0;
double   ownedTargetPrice  = 0.0;
datetime ownedEntryTime    = 0;
bool     ownedActive       = false;

// Sizing calculation
double CalculateLotSize(double entryPrice, double stopPrice)
{
   if(InpSizingMode == SIZING_FIXED)
      return InpFixedLots;

   double riskUSD = 0.0;
   if(InpSizingMode == SIZING_PERCENT)
   {
      double equity = AccountInfoDouble(ACCOUNT_EQUITY);
      riskUSD = equity * (InpRiskPercent / 100.0);
   }
   else if(InpSizingMode == SIZING_CASH)
   {
      riskUSD = InpCashRiskUSD;
   }

   double stopPoints = MathAbs(entryPrice - stopPrice);
   if(stopPoints <= 0) return InpFixedLots;

   // Point value in deposit currency
   double tickSize = SymbolInfoDouble(_Symbol, SYMBOL_TRADE_TICK_SIZE);
   double tickVal  = SymbolInfoDouble(_Symbol, SYMBOL_TRADE_TICK_VALUE);
   if(tickSize <= 0) tickSize = _Point;
   if(tickVal <= 0)  tickVal = 1.0;

   double costPerPointPerLot = (tickVal / tickSize);
   double riskPerLot = stopPoints * costPerPointPerLot;

   if(riskPerLot <= 0) return InpFixedLots;
   double lots = riskUSD / riskPerLot;

   double minLot  = SymbolInfoDouble(_Symbol, SYMBOL_VOLUME_MIN);
   double maxLot  = SymbolInfoDouble(_Symbol, SYMBOL_VOLUME_MAX);
   double lotStep = SymbolInfoDouble(_Symbol, SYMBOL_VOLUME_STEP);

   if(lotStep > 0)
      lots = MathFloor(lots / lotStep) * lotStep;
   lots = MathMax(minLot, MathMin(maxLot, lots));
   return lots;
}

// Reconcile and recover owned position after restart
void ReconcileOwnedPosition()
{
   ownedActive = false;
   ownedTicket = 0;
   ownedPositionId = 0;

   int total = PositionsTotal();
   for(int i = 0; i < total; i++)
   {
      ulong ticket = PositionGetTicket(i);
      if(ticket <= 0) continue;
      if(PositionGetString(POSITION_SYMBOL) != _Symbol) continue;
      if(PositionGetInteger(POSITION_MAGIC) != (long)InpMagic) continue;

      ownedTicket     = ticket;
      ownedPositionId = (ulong)PositionGetInteger(POSITION_IDENTIFIER);
      long type       = PositionGetInteger(POSITION_TYPE);
      ownedSide       = (type == POSITION_TYPE_BUY) ? 1 : -1;
      ownedEntryPrice = PositionGetDouble(POSITION_PRICE_OPEN);
      ownedStopPrice  = PositionGetDouble(POSITION_SL);
      ownedTargetPrice= PositionGetDouble(POSITION_TP);
      ownedEntryTime  = (datetime)PositionGetInteger(POSITION_TIME);
      string comment  = PositionGetString(POSITION_COMMENT);
      
      if(StringFind(comment, "MERGED") >= 0)      ownedSource = "MERGED";
      else if(StringFind(comment, "TREND") >= 0)   ownedSource = "TREND";
      else                                         ownedSource = "OD";

      ownedActive = true;
      PrintFormat("[FVG_DUO] Recovered owned position ticket=%I64u id=%I64u side=%s entry=%.2f sl=%.2f tp=%.2f source=%s",
                  ownedTicket, ownedPositionId, (ownedSide > 0 ? "BUY" : "SELL"), ownedEntryPrice, ownedStopPrice, ownedTargetPrice, ownedSource);
      return;
   }
}

// Send Market Order
bool ExecuteEntry(int side, double price, double sl, double tp, string source)
{
   extTrade.SetExpertMagicNumber(InpMagic);
   extTrade.SetDeviationInPoints(InpDeviation);

   double lots = CalculateLotSize(price, sl);
   string comment = "FVG_" + source;

   bool res = false;
   if(side > 0)
   {
      res = extTrade.Buy(lots, _Symbol, 0.0, sl, tp, comment);
   }
   else
   {
      res = extTrade.Sell(lots, _Symbol, 0.0, sl, tp, comment);
   }

   if(res)
   {
      ulong ticket = extTrade.ResultOrder();
      PrintFormat("[FVG_DUO] Order executed successfully ticket=%I64u source=%s side=%s lots=%.2f price=%.2f sl=%.2f tp=%.2f",
                  ticket, source, (side > 0 ? "BUY" : "SELL"), lots, extTrade.ResultPrice(), sl, tp);
      ReconcileOwnedPosition();
      return true;
   }
   else
   {
      PrintFormat("[FVG_DUO] Order failed retcode=%u (%s)", extTrade.ResultRetcode(), extTrade.ResultRetcodeDescription());
      return false;
   }
}

// Close owned position
bool ExecuteClose(string reason)
{
   if(!ownedActive || ownedTicket == 0) return true;

   extTrade.SetExpertMagicNumber(InpMagic);
   extTrade.SetDeviationInPoints(InpDeviation);

   bool res = extTrade.PositionClose(ownedTicket);
   if(res)
   {
      PrintFormat("[FVG_DUO] Position ticket=%I64u closed reason=%s", ownedTicket, reason);
      ownedActive = false;
      ownedTicket = 0;
      ownedPositionId = 0;
      return true;
   }
   else
   {
      PrintFormat("[FVG_DUO] PositionClose failed ticket=%I64u retcode=%u (%s)",
                  ownedTicket, extTrade.ResultRetcode(), extTrade.ResultRetcodeDescription());
      return false;
   }
}

// Modify stop & target (e.g. for MERGE: move stop, remove target)
bool ExecuteModify(double newSL, double newTP, string newComment)
{
   if(!ownedActive || ownedTicket == 0) return false;

   extTrade.SetExpertMagicNumber(InpMagic);
   bool res = extTrade.PositionModify(ownedTicket, newSL, newTP);
   if(res)
   {
      ownedStopPrice   = newSL;
      ownedTargetPrice = newTP;
      ownedSource      = newComment;
      PrintFormat("[FVG_DUO] Position ticket=%I64u modified to sl=%.2f tp=%.2f status=%s",
                  ownedTicket, newSL, newTP, newComment);
      return true;
   }
   else
   {
      PrintFormat("[FVG_DUO] PositionModify failed ticket=%I64u retcode=%u", ownedTicket, extTrade.ResultRetcode());
      return false;
   }
}

#endif // FVG_EXECUTION_RUNTIME
