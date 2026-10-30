#region Using declarations
using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Windows.Media;
using System.Xml.Serialization;
using NinjaTrader.Cbi;
using NinjaTrader.Core.FloatingPoint;
using NinjaTrader.Data;
using NinjaTrader.Gui;
using NinjaTrader.Gui.Chart;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.DrawingTools;
#endregion

namespace NinjaTrader.NinjaScript.Strategies
{
    public class FlashTriggerOrb : Strategy
    {
        private const int LINE_EXTEND_BARS = 5;

        #region Internal State
        private int sessionDate = 0;          // local (UTC + offset) date YYYYMMDD
        private double rangeHigh = 0.0;
        private double rangeLow = 0.0;
        private bool rangeActive = false;
        private int rangeStartBar = -1;
        private bool tradedThisRange = false;

        private int lineFreezeBar = -1;
        private bool linesFrozen = false;
        private string currentHighLineTag = string.Empty;
        private string currentLowLineTag = string.Empty;

        private int utcSessionDate = 0;        // raw UTC date YYYYMMDD
        private bool closedForDay = false;

        private double activeStopLoss = 0.0;
        private double activeTrailingStop = 0.0;
        private bool trailingActivated = false;
        private double bestPriceSinceEntry = 0.0;
        #endregion

        #region Parameters
        [NinjaScriptProperty]
        [Range(1, 240)]
        [Display(Name = "Targeted Candle Timeframe (min)", GroupName = "Parameters", Order = 1)]
        public int CandleTfMin { get; set; }

        [NinjaScriptProperty]
        [Range(0, 23)]
        [Display(Name = "Targeted Candle Hour", GroupName = "Parameters", Order = 2)]
        public int TargetHour { get; set; }

        [NinjaScriptProperty]
        [Range(0, 59)]
        [Display(Name = "Targeted Candle Minute", GroupName = "Parameters", Order = 3)]
        public int TargetMinute { get; set; }

        [NinjaScriptProperty]
        [Range(-12, 14)]
        [Display(Name = "UTC Offset (hours)", GroupName = "Parameters", Order = 4)]
        public int UtcOffset { get; set; }

        [NinjaScriptProperty]
        [Range(0.1, 20.0)]
        [Display(Name = "Risk:Reward", GroupName = "Parameters", Order = 5)]
        public double RiskReward { get; set; }

        [NinjaScriptProperty]
        [Range(0, 2)]
        [Display(Name = "Risk Per Trade Mode (0=Fixed Lot, 1=Fixed $, 2=% Account)", GroupName = "Risk Management", Order = 6)]
        public int RiskMode { get; set; }

        [NinjaScriptProperty]
        [Range(1.0, 1000.0)]
        [Display(Name = "Fixed Lot Size", GroupName = "Risk Management", Order = 7)]
        public double LotSize { get; set; }

        [NinjaScriptProperty]
        [Range(1.0, 1000000.0)]
        [Display(Name = "Risk Per Trade ($)", GroupName = "Risk Management", Order = 8)]
        public double RiskUsd { get; set; }

        [NinjaScriptProperty]
        [Range(0.01, 100.0)]
        [Display(Name = "Risk Per Trade (% of Account)", GroupName = "Risk Management", Order = 9)]
        public double RiskPercent { get; set; }

        [NinjaScriptProperty]
        [Range(0, 23)]
        [Display(Name = "Close All Trades Hour (UTC)", GroupName = "Session & Filters", Order = 10)]
        public int EodCloseHour { get; set; }

        [NinjaScriptProperty]
        [Range(0, 59)]
        [Display(Name = "Close All Trades Minute (UTC)", GroupName = "Session & Filters", Order = 11)]
        public int EodCloseMinute { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Activate Trailing SL", GroupName = "Trailing Stop", Order = 12)]
        public bool ActivateTrailing { get; set; }

        [NinjaScriptProperty]
        [Range(0.1, 10000.0)]
        [Display(Name = "Trail Trigger (points in profit)", GroupName = "Trailing Stop", Order = 13)]
        public double TrailActivatePoints { get; set; }

        [NinjaScriptProperty]
        [Range(0.1, 10000.0)]
        [Display(Name = "Trailing Buffer (points)", GroupName = "Trailing Stop", Order = 14)]
        public double TrailBufferPoints { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Day Filter", GroupName = "Day Filter", Order = 15)]
        public bool DayFilter { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Trade Monday", GroupName = "Day Filter", Order = 16)]
        public bool TradeMonday { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Trade Tuesday", GroupName = "Day Filter", Order = 17)]
        public bool TradeTuesday { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Trade Wednesday", GroupName = "Day Filter", Order = 18)]
        public bool TradeWednesday { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Trade Thursday", GroupName = "Day Filter", Order = 19)]
        public bool TradeThursday { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Trade Friday", GroupName = "Day Filter", Order = 20)]
        public bool TradeFriday { get; set; }
        #endregion

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description                                 = @"The Flash Trigger ORB: Breakout of a single targeted candle with risk-reward, custom UTC offsets, and trailing stops.";
                Name                                        = "FlashTriggerOrb";
                Calculate                                   = Calculate.OnBarClose;
                EntriesPerDirection                         = 1;
                EntryHandling                               = EntryHandling.AllEntries;
                IsExitOnSessionCloseStrategy                = false;
                ExitOnSessionCloseSeconds                   = 30;
                IsFillLimitOnTouch                          = false;
                MaximumBarsLookBack                         = MaximumBarsLookBack.TwoHundredFiftySix;
                OrderFillResolution                         = OrderFillResolution.Standard;
                Slippage                                    = 0;
                StartBehavior                               = StartBehavior.WaitUntilFlat;
                TimeInForce                                 = TimeInForce.Gtc;
                TraceOrders                                 = false;
                RealtimeErrorHandling                       = RealtimeErrorHandling.StopCancelClose;
                StopTargetHandling                          = StopTargetHandling.PerEntryExecution;
                BarsRequiredToTrade                         = 1;

                CandleTfMin                                 = 15;
                TargetHour                                  = 14;
                TargetMinute                                = 30;
                UtcOffset                                   = 0;
                RiskReward                                  = 3.0;
                RiskMode                                    = 0;
                LotSize                                     = 1.0;
                RiskUsd                                     = 100.0;
                RiskPercent                                 = 1.0;
                EodCloseHour                                = 23;
                EodCloseMinute                              = 59;
                ActivateTrailing                            = false;
                TrailActivatePoints                         = 50.0;
                TrailBufferPoints                           = 20.0;
                DayFilter                                   = false;
                TradeMonday                                 = true;
                TradeTuesday                                = true;
                TradeWednesday                              = true;
                TradeThursday                               = true;
                TradeFriday                                 = true;
            }
            else if (State == State.DataLoaded)
            {
                sessionDate = 0;
                rangeHigh = 0.0;
                rangeLow = 0.0;
                rangeActive = false;
                rangeStartBar = -1;
                tradedThisRange = false;
                lineFreezeBar = -1;
                linesFrozen = false;
                currentHighLineTag = string.Empty;
                currentLowLineTag = string.Empty;
                utcSessionDate = 0;
                closedForDay = false;
                activeStopLoss = 0.0;
                activeTrailingStop = 0.0;
                trailingActivated = false;
                bestPriceSinceEntry = 0.0;
            }
        }

        private (int dateNum, int hhmm, DayOfWeek dow) GetLocalTime(DateTime barUtc)
        {
            DateTime shifted = barUtc.AddHours(UtcOffset);
            int dateNum = shifted.Year * 10000 + shifted.Month * 100 + shifted.Day;
            int hhmm = shifted.Hour * 100 + shifted.Minute;
            return (dateNum, hhmm, shifted.DayOfWeek);
        }

        private bool IsDayAllowed(DayOfWeek dow)
        {
            if (!DayFilter)
                return true;

            switch (dow)
            {
                case DayOfWeek.Monday: return TradeMonday;
                case DayOfWeek.Tuesday: return TradeTuesday;
                case DayOfWeek.Wednesday: return TradeWednesday;
                case DayOfWeek.Thursday: return TradeThursday;
                case DayOfWeek.Friday: return TradeFriday;
                default: return false;
            }
        }

        private int CalculateSizeForTrade(double stopDistance)
        {
            double pointValue = Instrument.MasterInstrument.PointValue;
            if (RiskMode == 1) // Fixed $ risk
            {
                double lossPerContract = Math.Abs(stopDistance) * pointValue;
                if (lossPerContract > 0.0)
                    return (int)Math.Floor(RiskUsd / lossPerContract);
                return 0;
            }
            else if (RiskMode == 2) // % Account equity
            {
                double lossPerContract = Math.Abs(stopDistance) * pointValue;
                if (lossPerContract <= 0.0)
                    return 0;

                double equity = Account.Get(AccountItem.CashValue, Currency.UsDollar);
                if (equity <= 0.0)
                    equity = 100000.0; // fallback if uninitialized

                double totalRiskUsd = equity * (RiskPercent / 100.0);
                return (int)Math.Floor(totalRiskUsd / lossPerContract);
            }
            else // Fixed lot
            {
                return (int)Math.Max(1, Math.Round(LotSize));
            }
        }

        protected override void OnBarUpdate()
        {
            if (CurrentBar < 1)
                return;

            DateTime barUtc = Time[0].ToUniversalTime();
            var (localDate, localHhmm, localDow) = GetLocalTime(barUtc);

            // New local day -> reset per-session state
            if (localDate != sessionDate)
            {
                sessionDate = localDate;
                rangeActive = false;
                tradedThisRange = false;
                currentHighLineTag = string.Empty;
                currentLowLineTag = string.Empty;
                linesFrozen = false;
            }

            // New UTC day -> reset the EOD flatten flag
            int utcDate = barUtc.Year * 10000 + barUtc.Month * 100 + barUtc.Day;
            if (utcDate != utcSessionDate)
            {
                utcSessionDate = utcDate;
                closedForDay = false;
            }

            // End-of-day flatten (UTC)
            int utcHhmm = barUtc.Hour * 100 + barUtc.Minute;
            int eodHhmm = EodCloseHour * 100 + EodCloseMinute;
            if (!closedForDay && utcHhmm >= eodHhmm)
            {
                if (Position.MarketPosition != MarketPosition.Flat)
                {
                    FlattenPosition();
                    Print(string.Format("{0} Flash Trigger ORB: EOD flatten (UTC)", barUtc));
                }
                closedForDay = true;
            }

            // Manage dynamic trailing stop if in position
            UpdateTrailingStop();

            // Detect targeted candle by matching local time-of-day
            int targetHhmm = TargetHour * 100 + TargetMinute;
            if (!rangeActive && localHhmm == targetHhmm && IsDayAllowed(localDow))
            {
                rangeHigh = High[0];
                rangeLow = Low[0];
                rangeActive = true;
                tradedThisRange = false;
                rangeStartBar = CurrentBar;
                lineFreezeBar = rangeStartBar + LINE_EXTEND_BARS;
                linesFrozen = false;

                currentHighLineTag = "ORB_High_" + CurrentBar;
                currentLowLineTag = "ORB_Low_" + CurrentBar;

                Draw.Line(this, currentHighLineTag, false, 0, rangeHigh, -LINE_EXTEND_BARS, rangeHigh, Brushes.LimeGreen, DashStyleHelper.Solid, 2);
                Draw.Line(this, currentLowLineTag, false, 0, rangeLow, -LINE_EXTEND_BARS, rangeLow, Brushes.Red, DashStyleHelper.Solid, 2);

                Print(string.Format("{0} Flash Trigger ORB: range confirmed High={1:F2} Low={2:F2}", barUtc, rangeHigh, rangeLow));
                return;
            }

            // Freeze the lines once extended LINE_EXTEND_BARS
            if (rangeActive && !linesFrozen && CurrentBar >= lineFreezeBar)
            {
                linesFrozen = true;
            }

            // Check trade eligibility
            if (!rangeActive || tradedThisRange)
                return;

            if (CurrentBar <= rangeStartBar)
                return;

            if (Position.MarketPosition != MarketPosition.Flat)
                return;

            // Breakout triggers
            if (Close[0] > rangeHigh)
            {
                double sl = rangeLow;
                double stopDistance = Close[0] - sl;
                if (stopDistance > 0.0)
                {
                    int qty = CalculateSizeForTrade(stopDistance);
                    if (qty > 0)
                    {
                        double tp = Close[0] + stopDistance * RiskReward;
                        activeStopLoss = sl;
                        activeTrailingStop = sl;
                        trailingActivated = false;
                        bestPriceSinceEntry = Close[0];

                        EnterLong(qty, "OrbLong");
                        SetStopLoss("OrbLong", CalculationMode.Price, sl, false);
                        SetProfitTarget("OrbLong", CalculationMode.Price, tp);

                        tradedThisRange = true;
                        Print(string.Format("{0} Flash Trigger ORB: LONG entry at {1:F2}, SL={2:F2}, TP={3:F2}, Qty={4}",
                            barUtc, Close[0], sl, tp, qty));
                    }
                }
            }
            else if (Close[0] < rangeLow)
            {
                double sl = rangeHigh;
                double stopDistance = sl - Close[0];
                if (stopDistance > 0.0)
                {
                    int qty = CalculateSizeForTrade(stopDistance);
                    if (qty > 0)
                    {
                        double tp = Close[0] - stopDistance * RiskReward;
                        activeStopLoss = sl;
                        activeTrailingStop = sl;
                        trailingActivated = false;
                        bestPriceSinceEntry = Close[0];

                        EnterShort(qty, "OrbShort");
                        SetStopLoss("OrbShort", CalculationMode.Price, sl, false);
                        SetProfitTarget("OrbShort", CalculationMode.Price, tp);

                        tradedThisRange = true;
                        Print(string.Format("{0} Flash Trigger ORB: SHORT entry at {1:F2}, SL={2:F2}, TP={3:F2}, Qty={4}",
                            barUtc, Close[0], sl, tp, qty));
                    }
                }
            }
        }

        private void UpdateTrailingStop()
        {
            if (!ActivateTrailing || Position.MarketPosition == MarketPosition.Flat)
                return;

            double entryPrice = Position.AveragePrice;
            if (Position.MarketPosition == MarketPosition.Long)
            {
                if (High[0] > bestPriceSinceEntry)
                    bestPriceSinceEntry = High[0];

                double profitPoints = bestPriceSinceEntry - entryPrice;
                if (!trailingActivated && profitPoints >= TrailActivatePoints)
                {
                    trailingActivated = true;
                }

                if (trailingActivated)
                {
                    double newTrailStop = bestPriceSinceEntry - TrailBufferPoints;
                    if (newTrailStop > activeTrailingStop)
                    {
                        activeTrailingStop = newTrailStop;
                        SetStopLoss("OrbLong", CalculationMode.Price, activeTrailingStop, false);
                    }
                }
            }
            else if (Position.MarketPosition == MarketPosition.Short)
            {
                if (Low[0] < bestPriceSinceEntry)
                    bestPriceSinceEntry = Low[0];

                double profitPoints = entryPrice - bestPriceSinceEntry;
                if (!trailingActivated && profitPoints >= TrailActivatePoints)
                {
                    trailingActivated = true;
                }

                if (trailingActivated)
                {
                    double newTrailStop = bestPriceSinceEntry + TrailBufferPoints;
                    if (newTrailStop < activeTrailingStop || activeTrailingStop == 0.0)
                    {
                        activeTrailingStop = newTrailStop;
                        SetStopLoss("OrbShort", CalculationMode.Price, activeTrailingStop, false);
                    }
                }
            }
        }

        private void FlattenPosition()
        {
            if (Position.MarketPosition == MarketPosition.Long)
                ExitLong("Flatten", "OrbLong");
            else if (Position.MarketPosition == MarketPosition.Short)
                ExitShort("Flatten", "OrbShort");
        }
    }
}
