#region Using declarations
using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Windows.Media;
using System.Xml.Serialization;
using NinjaTrader.Cbi;
using NinjaTrader.Core;
using NinjaTrader.Core.FloatingPoint;
using NinjaTrader.Data;
using NinjaTrader.Gui;
using NinjaTrader.Gui.Chart;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.DrawingTools;
using NinjaTrader.NinjaScript.Indicators;
#endregion

namespace NinjaTrader.NinjaScript.Strategies
{
    public class CrazyOrb : Strategy
    {
        #region Constants
        // NT minute bars are close-stamped. The M1 series submits the safety
        // flatten at 15:44 ET (14:44 CT), before the 14:45 CT Tradeify deadline.
        private static readonly TimeZoneInfo EasternZone = TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");
        #endregion

        #region Internal State
        private int currentDayDateNum = 0;
        private int cutoffExitDateNum = 0;
        private TimeZoneInfo platformZone;
        private Order entryOrder;
        private bool pendingEntry = false;
        private bool entryCountedForPending = false;
        private double rangeHigh = 0.0;
        private double rangeLow = 0.0;
        private bool building = false;
        private bool rangeDone = false;
        private bool ready = false;

        private bool longUsed = false;
        private bool shortUsed = false;
        private int tradesToday = 0;

        private ATR atrIndicator;
        #endregion

        #region Parameters
        [NinjaScriptProperty]
        [Range(0, 2359)]
        [Display(Name = "Range Start (HHMM ET)", GroupName = "Opening Range Window", Order = 1)]
        public int RangeStartHhmm { get; set; }

        [NinjaScriptProperty]
        [Range(0, 2359)]
        [Display(Name = "Range End (HHMM ET)", GroupName = "Opening Range Window", Order = 2)]
        public int RangeEndHhmm { get; set; }

        [NinjaScriptProperty]
        [Range(2, 200)]
        [Display(Name = "ATR Period", GroupName = "Risk Model", Order = 3)]
        public int AtrPeriod { get; set; }

        [NinjaScriptProperty]
        [Range(0.1, 10.0)]
        [Display(Name = "Stop Distance (ATR x)", GroupName = "Risk Model", Order = 4)]
        public double AtrMult { get; set; }

        [NinjaScriptProperty]
        [Range(0.25, 10.0)]
        [Display(Name = "Take Profit (R multiple)", GroupName = "Risk Model", Order = 5)]
        public double Rr { get; set; }

        [NinjaScriptProperty]
        [Range(25.0, 50000.0)]
        [Display(Name = "Risk per Trade (USD)", GroupName = "Risk Model", Order = 6)]
        public double RiskUsd { get; set; }

        [NinjaScriptProperty]
        [Range(0, 200)]
        [Display(Name = "Min Size (Contracts)", GroupName = "Risk Model", Order = 7)]
        public int MinSize { get; set; }

        [NinjaScriptProperty]
        [Range(1, 20)]
        [Display(Name = "Max Trades per Day", GroupName = "Trade Management", Order = 8)]
        public int MaxTrades { get; set; }

        [NinjaScriptProperty]
        [Range(0, 2359)]
        [Display(Name = "Trading Window Start (HHMM ET)", GroupName = "Trade Management", Order = 9)]
        public int TradeStartHhmm { get; set; }

        [NinjaScriptProperty]
        [Range(0, 1545)]
        [Display(Name = "No Entries At/After (HHMM ET)", Description = "15:45 ET = 14:45 CT Tradeify deadline. Signals are evaluated on completed 15-minute bars.", GroupName = "Trade Management", Order = 10)]
        public int TradeEndHhmm { get; set; }

        [NinjaScriptProperty]
        [Range(0, 1544)]
        [Display(Name = "Safety Flatten (HHMM ET)", Description = "M1 bar-close trigger. 15:44 ET = 14:44 CT, one minute before the 14:45 CT Tradeify cutoff.", GroupName = "Trade Management", Order = 11)]
        public int FlattenHhmm { get; set; }
        #endregion

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description                 = @"Crazy ORB: Close-confirmed opening-range breakout with ATR stops, R multiple targets, fixed dollar risk, and a Tradeify-safe 14:44 CT flatten.";
                Name                        = "CrazyOrb";
                Calculate                   = Calculate.OnBarClose;
                EntriesPerDirection         = 1;
                EntryHandling               = EntryHandling.AllEntries;
                IsExitOnSessionCloseStrategy= false;
                ExitOnSessionCloseSeconds   = 30;
                IsFillLimitOnTouch          = false;
                MaximumBarsLookBack         = MaximumBarsLookBack.TwoHundredFiftySix;
                OrderFillResolution         = OrderFillResolution.Standard;
                Slippage                    = 0;
                StartBehavior               = StartBehavior.WaitUntilFlat;
                TimeInForce                 = TimeInForce.Gtc;
                TraceOrders                 = false;
                RealtimeErrorHandling       = RealtimeErrorHandling.StopCancelClose;
                StopTargetHandling          = StopTargetHandling.PerEntryExecution;
                BarsRequiredToTrade         = 1;

                // Match the uploaded RoboQuant Crazy ORB preset.
                RangeStartHhmm              = 915;
                RangeEndHhmm                = 930;
                AtrPeriod                   = 14;
                AtrMult                     = 1.0;
                Rr                          = 0.35;
                RiskUsd                     = 2001.0;
                MinSize                     = 1;
                MaxTrades                   = 2;
                TradeStartHhmm              = 930;
                TradeEndHhmm                = 1545;
                FlattenHhmm                 = 1544;

                AddPlot(new Stroke(Brushes.RoyalBlue, 2), PlotStyle.Line, "ORB High");
                AddPlot(new Stroke(Brushes.RoyalBlue, 2), PlotStyle.Line, "ORB Low");
            }
            else if (State == State.Configure)
            {
                // Primary M1 is the execution series so managed brackets use
                // NT's first Bars context. The added M15 series supplies ORB signals.
                AddDataSeries(BarsPeriodType.Minute, 15);
            }
            else if (State == State.DataLoaded)
            {
                if (BarsPeriod.BarsPeriodType != BarsPeriodType.Minute || BarsPeriod.Value != 1)
                    throw new InvalidOperationException("CrazyOrb requires a 1-minute primary execution series.");
                if (BarsArray[1].BarsPeriod.BarsPeriodType != BarsPeriodType.Minute || BarsArray[1].BarsPeriod.Value != 15)
                    throw new InvalidOperationException("CrazyOrb adds a fixed 15-minute signal series.");

                platformZone = Globals.GeneralOptions.TimeZoneInfo;
                if (platformZone == null)
                    throw new InvalidOperationException("NinjaTrader platform time zone is unavailable.");

                ValidateNqContract();

                atrIndicator = ATR(Closes[1], AtrPeriod);
                entryOrder = null;
                cutoffExitDateNum = 0;
                ResetDayState(0);
                Print(string.Format("{0} ready: {1}, point value=${2:F2}/point, M1 execution / M15 signals, {3:00}:{4:00} ET safety flatten.",
                    Name, Instrument.FullName, Instrument.MasterInstrument.PointValue,
                    FlattenHhmm / 100, FlattenHhmm % 100));
            }
        }

        private void ResetDayState(int newDateNum)
        {
            CancelPendingEntryOrders();
            if (Position.MarketPosition != MarketPosition.Flat)
                FlattenPosition(0, "RolloverFlatten");

            currentDayDateNum = newDateNum;
            cutoffExitDateNum = 0;
            rangeHigh = 0.0;
            rangeLow = 0.0;
            building = false;
            rangeDone = false;
            ready = false;
            longUsed = false;
            shortUsed = false;
            tradesToday = 0;
            pendingEntry = false;
            entryCountedForPending = false;
            entryOrder = null;
        }

        private void ValidateNqContract()
        {
            string root = Instrument.MasterInstrument.Name.ToUpperInvariant();
            double pointValue = Instrument.MasterInstrument.PointValue;
            bool validNq = root == "NQ" && Math.Abs(pointValue - 20.0) < 0.000001;
            bool validMnq = root == "MNQ" && Math.Abs(pointValue - 2.0) < 0.000001;
            if (!validNq && !validMnq)
                throw new InvalidOperationException("Use native NQ ($20/point) or MNQ ($2/point). Detected "
                    + Instrument.FullName + " with $" + pointValue.ToString("F2") + "/point.");
        }

        private DateTime ToEastern(DateTime platformTime)
        {
            return TimeZoneInfo.ConvertTime(
                DateTime.SpecifyKind(platformTime, DateTimeKind.Unspecified),
                platformZone,
                EasternZone);
        }

        private static bool IsCancelable(Order order)
        {
            return order != null && (order.OrderState == OrderState.Submitted
                || order.OrderState == OrderState.Accepted
                || order.OrderState == OrderState.Working
                || order.OrderState == OrderState.PartFilled);
        }

        private void CancelPendingEntryOrders()
        {
            if (IsCancelable(entryOrder))
                CancelOrder(entryOrder);
        }

        private void FlattenPosition(int barsInProgressIndex, string signalName)
        {
            if (Position.MarketPosition == MarketPosition.Long)
                ExitLong(barsInProgressIndex, Position.Quantity, signalName, "ORB_Long");
            else if (Position.MarketPosition == MarketPosition.Short)
                ExitShort(barsInProgressIndex, Position.Quantity, signalName, "ORB_Short");
        }

        protected override void OnOrderUpdate(Order order, double limitPrice, double stopPrice,
            int quantity, int filled, double averageFillPrice, OrderState orderState,
            DateTime time, ErrorCode error, string comment)
        {
            if (order == null || (order.Name != "ORB_Long" && order.Name != "ORB_Short"))
                return;

            entryOrder = IsCancelable(order) ? order : null;
            if (orderState == OrderState.Filled || orderState == OrderState.Cancelled || orderState == OrderState.Rejected)
                pendingEntry = false;
        }

        protected override void OnExecutionUpdate(Execution execution, string executionId,
            double price, int quantity, MarketPosition marketPosition, string orderId, DateTime time)
        {
            if (execution == null || execution.Order == null || platformZone == null)
                return;

            string orderName = execution.Order.Name;
            if ((orderName == "ORB_Long" || orderName == "ORB_Short") && !entryCountedForPending)
            {
                if (orderName == "ORB_Long")
                    longUsed = true;
                else
                    shortUsed = true;

                tradesToday++;
                entryCountedForPending = true;
            }

            DateTime executionEt = ToEastern(time);
            int executionHhmm = executionEt.Hour * 100 + executionEt.Minute;
            if (executionHhmm >= FlattenHhmm && marketPosition != MarketPosition.Flat)
            {
                CancelPendingEntryOrders();
                FlattenPosition(0, "TradeifyLateFillFlatten");
            }
        }

        protected override void OnBarUpdate()
        {
            // BIP 0 is the primary M1 execution series. This is the first Bars
            // context for the instrument, so managed entries and Set() brackets
            // are submitted here and can resolve on M1 historical bars.
            if (BarsInProgress == 0)
            {
                if (CurrentBars[0] < 0)
                    return;

                DateTime minuteEndEt = ToEastern(Times[0][0]);
                int minuteHhmm = minuteEndEt.Hour * 100 + minuteEndEt.Minute;
                int minuteDateNum = minuteEndEt.Year * 10000 + minuteEndEt.Month * 100 + minuteEndEt.Day;

                if (minuteHhmm >= FlattenHhmm && cutoffExitDateNum != minuteDateNum)
                {
                    cutoffExitDateNum = minuteDateNum;
                    CancelPendingEntryOrders();
                    if (Position.MarketPosition != MarketPosition.Flat)
                    {
                        Print(string.Format("{0} Tradeify safety flatten ({1:00}:{2:00} ET).", minuteEndEt,
                            FlattenHhmm / 100, FlattenHhmm % 100));
                        FlattenPosition(0, "TradeifyFlatten");
                    }
                }
                return;
            }

            // BIP 1 is the completed M15 signal series. NT stamps each bar at
            // its close; the configured opening range is based on bar-start time.
            if (BarsInProgress != 1 || CurrentBars[1] < AtrPeriod)
                return;

            DateTime barEndEt = ToEastern(Times[1][0]);
            DateTime barStartEt = barEndEt.AddMinutes(-BarsArray[1].BarsPeriod.Value);
            int barStartHhmm = barStartEt.Hour * 100 + barStartEt.Minute;
            int barEndHhmm = barEndEt.Hour * 100 + barEndEt.Minute;
            int dateNum = barStartEt.Year * 10000 + barStartEt.Month * 100 + barStartEt.Day;
            double signalClose = Closes[1][0];

            // 1. Rollover Check
            if (dateNum != currentDayDateNum)
                ResetDayState(dateNum);

            // 2. Range Window Absorbing & Locking (bar-start time)
            if (barStartHhmm >= RangeStartHhmm && barStartHhmm < RangeEndHhmm)
            {
                if (!building)
                {
                    building = true;
                    rangeHigh = Highs[1][0];
                    rangeLow = Lows[1][0];
                }
                else
                {
                    rangeHigh = Math.Max(rangeHigh, Highs[1][0]);
                    rangeLow = Math.Min(rangeLow, Lows[1][0]);
                }
            }
            else if (!rangeDone && barStartHhmm >= RangeEndHhmm)
            {
                rangeDone = true;
                ready = building;
                if (!building)
                    Print(string.Format("{0} No bar found in {1:00}:{2:00}-{3:00}:{4:00} ET range; check that the M1 primary uses ETH hours.",
                        barEndEt, RangeStartHhmm / 100, RangeStartHhmm % 100,
                        RangeEndHhmm / 100, RangeEndHhmm % 100));
            }

            // Update Plots once range is building/locked
            if (building)
            {
                Values[0][0] = rangeHigh;
                Values[1][0] = rangeLow;
            }

            // Fallback if the primary M1 cutoff bar is missing from the feed.
            if (barEndHhmm >= FlattenHhmm)
            {
                CancelPendingEntryOrders();
                if (Position.MarketPosition != MarketPosition.Flat)
                {
                    Print(string.Format("{0} Fallback flatten from M15 signal series.", barEndEt));
                    FlattenPosition(0, "SignalFallbackFlatten");
                }
                return;
            }

            // 3. Signal Evaluation on completed M15 bar
            if (!ready || Position.MarketPosition != MarketPosition.Flat || pendingEntry || tradesToday >= MaxTrades)
                return;

            if (barEndHhmm < TradeStartHhmm || barEndHhmm >= TradeEndHhmm)
                return;

            double atr = atrIndicator[0];
            double stopDist = atr * AtrMult;
            if (stopDist <= 0.0)
                return;

            double pointValue = Instrument.MasterInstrument.PointValue;
            double lossPerContract = stopDist * pointValue;
            if (lossPerContract <= 0.0)
                return;

            int plannedQty = (int)Math.Floor(RiskUsd / lossPerContract);
            int qty = Math.Max(MinSize, Math.Max(1, plannedQty));
            if (qty * lossPerContract > RiskUsd)
            {
                Print(string.Format("{0} WARNING: minimum size {1} risks ${2:F2}, above ${3:F2} budget.",
                    barEndEt, qty, qty * lossPerContract, RiskUsd));
            }

            // Long Breakout: candle MUST close above range high.
            if (signalClose > rangeHigh && !longUsed)
            {
                double stopPx = signalClose - stopDist;
                double targetPx = signalClose + Rr * stopDist;
                pendingEntry = true;
                entryCountedForPending = false;
                SetStopLoss("ORB_Long", CalculationMode.Price, stopPx, false);
                SetProfitTarget("ORB_Long", CalculationMode.Price, targetPx, false);
                entryOrder = EnterLong(0, qty, "ORB_Long");
                if (entryOrder == null)
                    pendingEntry = false;
                Print(string.Format("{0} ENTRY LONG qty={1} close={2:F2} SL={3:F2} TP={4:F2}",
                    barEndEt, qty, signalClose, stopPx, targetPx));
            }
            // Short Breakout: candle MUST close below range low.
            else if (signalClose < rangeLow && !shortUsed)
            {
                double stopPx = signalClose + stopDist;
                double targetPx = signalClose - Rr * stopDist;
                pendingEntry = true;
                entryCountedForPending = false;
                SetStopLoss("ORB_Short", CalculationMode.Price, stopPx, false);
                SetProfitTarget("ORB_Short", CalculationMode.Price, targetPx, false);
                entryOrder = EnterShort(0, qty, "ORB_Short");
                if (entryOrder == null)
                    pendingEntry = false;
                Print(string.Format("{0} ENTRY SHORT qty={1} close={2:F2} SL={3:F2} TP={4:F2}",
                    barEndEt, qty, signalClose, stopPx, targetPx));
            }
        }
    }
}
