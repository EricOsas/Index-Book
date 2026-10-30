#region Using declarations
using System;
using System.Collections.Generic;
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
#endregion

namespace NinjaTrader.NinjaScript.Strategies
{
    public class SigmaBreakout : Strategy
    {
        #region Constants
        private const int SESSION_OPEN_ET = 930;
        private const int SESSION_CLOSE_ET = 1600;
        // Sigma signals use 30-minute bars. A bar starting at 15:30 ET closes
        // at 16:00 ET, so it cannot generate an entry before the 14:45 CT cutoff.
        private const int ENTRY_CUTOFF_BAR_START_ET = 1530;
        // Submit the timed exit on the 15:44 ET M1 close. With NT's close-stamped
        // minute bars, the market order is eligible at the 15:44 ET next-bar open
        // (14:44 CT), one minute before the user's 14:45 CT deadline.
        private const int TRADEIFY_FLATTEN_BAR_END_ET = 1544;
        private static readonly TimeZoneInfo EasternZone = TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");
        #endregion

        #region Helper Class: Rolling Window
        private class RollingQueue
        {
            private readonly Queue<double> queue;
            private readonly int capacity;
            private double sum;

            public RollingQueue(int capacity)
            {
                this.capacity = Math.Max(2, capacity);
                this.queue = new Queue<double>(this.capacity);
                this.sum = 0.0;
            }

            public bool IsFull => queue.Count >= capacity;

            public double Mean => queue.Count > 0 ? (sum / queue.Count) : 0.0;

            public void Push(double value)
            {
                if (queue.Count >= capacity)
                {
                    sum -= queue.Dequeue();
                }
                queue.Enqueue(value);
                sum += value;
            }
        }
        #endregion

        #region Internal State
        private int currentDayDateNum = 0;
        private int cutoffExitDateNum = 0;
        private TimeZoneInfo platformZone;
        private Order longEntryOrder;
        private Order shortEntryOrder;
        private double dayOpen = 0.0;
        private bool haveDayOpen = false;
        private bool tradingEnabled = false;
        private bool warmupLogged = false;
        private bool pendingEntry = false;

        // Causal session VWAP (as-of t-1)
        private double vwapPv = 0.0;
        private double vwapVol = 0.0;
        private double vwapPrev = 0.0;
        private bool haveVwapPrev = false;

        // Per-slot rolling history: key = HHMM bucket
        private Dictionary<int, RollingQueue> slotHist;
        #endregion

        #region Parameters
        [NinjaScriptProperty]
        [Range(2, 60)]
        [Display(Name = "N Sigma Days", GroupName = "Parameters", Order = 1)]
        public int NSigmaDays { get; set; }

        [NinjaScriptProperty]
        [Range(0.1, 5.0)]
        [Display(Name = "Band Multiplier", GroupName = "Parameters", Order = 2)]
        public double BandMult { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Use VWAP as Reference", Description = "False = RTH Open, True = Causal VWAP (t-1)", GroupName = "Parameters", Order = 3)]
        public bool UseVwap { get; set; }

        [NinjaScriptProperty]
        [Range(0.0, 20.0)]
        [Display(Name = "Stop Loss %", Description = "0 = Off", GroupName = "Risk Management", Order = 4)]
        public double SlPct { get; set; }

        [NinjaScriptProperty]
        [Range(1, 100)]
        [Display(Name = "Fixed Lot", Description = "Used when Stop Loss is 0", GroupName = "Risk Management", Order = 5)]
        public int FixedLot { get; set; }

        [NinjaScriptProperty]
        [Range(0.1, 20.0)]
        [Display(Name = "Risk % of Equity", Description = "Used when SL is on and Fixed Risk USD is 0", GroupName = "Risk Management", Order = 6)]
        public double RiskPct { get; set; }

        [NinjaScriptProperty]
        [Range(0.0, 10000.0)]
        [Display(Name = "Fixed Risk $ per Trade", Description = "0 = Off. Takes precedence over Risk % when set", GroupName = "Risk Management", Order = 7)]
        public double FixedRiskUsd { get; set; }
        #endregion

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description                                 = @"Sigma Breakout: Intraday momentum / volatility-range breakout based on historical slot sigma.";
                Name                                        = "SigmaBreakout";
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

                // Match the uploaded RoboQuant Sigma Breakout preset.
                NSigmaDays                                  = 2;
                BandMult                                    = 1.5;
                UseVwap                                     = false;
                SlPct                                       = 0.0;
                FixedLot                                    = 1;
                RiskPct                                     = 1.0;
                FixedRiskUsd                                = 0.0;

                AddPlot(new Stroke(Brushes.Gold, 2), PlotStyle.Line, "Reference");
                AddPlot(new Stroke(Brushes.MediumSeaGreen, 2), PlotStyle.Line, "Upper Band");
                AddPlot(new Stroke(Brushes.Crimson, 2), PlotStyle.Line, "Lower Band");
            }
            else if (State == State.Configure)
            {
                // Primary must be M1 for NT's first-Bars-context managed orders.
                // The added M30 series supplies Sigma signals and slot history.
                AddDataSeries(BarsPeriodType.Minute, 30);
            }
            else if (State == State.DataLoaded)
            {
                if (BarsPeriod.BarsPeriodType != BarsPeriodType.Minute || BarsPeriod.Value != 1)
                    throw new InvalidOperationException("SigmaBreakout requires a 1-minute primary execution series.");
                if (BarsArray[1].BarsPeriod.BarsPeriodType != BarsPeriodType.Minute || BarsArray[1].BarsPeriod.Value != 30)
                    throw new InvalidOperationException("SigmaBreakout adds a fixed 30-minute signal series.");

                platformZone = Globals.GeneralOptions.TimeZoneInfo;
                if (platformZone == null)
                    throw new InvalidOperationException("NinjaTrader platform time zone is unavailable.");

                ValidateNqContract();

                slotHist = new Dictionary<int, RollingQueue>();
                currentDayDateNum = 0;
                dayOpen = 0.0;
                haveDayOpen = false;
                tradingEnabled = false;
                warmupLogged = false;
                pendingEntry = false;
                vwapPv = 0.0;
                vwapVol = 0.0;
                vwapPrev = 0.0;
                haveVwapPrev = false;
                longEntryOrder = null;
                shortEntryOrder = null;
                cutoffExitDateNum = 0;
                Print(string.Format("{0} ready: {1}, point value=${2:F2}/point, M1 execution / M30 signals, 14:44 CT safety flatten.",
                    Name, Instrument.FullName, Instrument.MasterInstrument.PointValue));
            }
        }

        protected override void OnBarUpdate()
        {
            // BIP 0 is the primary M1 execution series. All managed entries,
            // reversals and exits are routed here so NT's first Bars context
            // owns fills and any optional SetStopLoss orders.
            if (BarsInProgress == 0)
            {
                if (CurrentBars[0] < 0)
                    return;

                DateTime minuteEndEt = ToEastern(Times[0][0]);
                int minuteHhmm = minuteEndEt.Hour * 100 + minuteEndEt.Minute;
                int minuteDateNum = minuteEndEt.Year * 10000 + minuteEndEt.Month * 100 + minuteEndEt.Day;

                if (minuteHhmm >= TRADEIFY_FLATTEN_BAR_END_ET && cutoffExitDateNum != minuteDateNum)
                {
                    cutoffExitDateNum = minuteDateNum;
                    CancelPendingEntryOrders();
                    if (Position.MarketPosition != MarketPosition.Flat)
                    {
                        Print(string.Format("{0} Tradeify safety flatten (14:44 CT / 15:44 ET).", minuteEndEt));
                        FlattenPosition(0, "TradeifyFlatten");
                    }
                }
                return;
            }

            // BIP 1 is the completed 30-minute signal series. Its timestamps
            // are close-stamped by NT; the RQ slot key is the bar start.
            if (BarsInProgress != 1 || CurrentBars[1] < 1)
                return;

            DateTime barEndEt = ToEastern(Times[1][0]);
            DateTime barStartEt = barEndEt.AddMinutes(-BarsArray[1].BarsPeriod.Value);
            int hhmm = barStartEt.Hour * 100 + barStartEt.Minute;
            int barEndHhmm = barEndEt.Hour * 100 + barEndEt.Minute;
            int dateNum = barStartEt.Year * 10000 + barStartEt.Month * 100 + barStartEt.Day;

            bool inRth = (hhmm >= SESSION_OPEN_ET && hhmm < SESSION_CLOSE_ET);

            // ---- New RTH day rollover bookkeeping ----
            if (dateNum != currentDayDateNum)
            {
                CancelPendingEntryOrders();
                if (Position.MarketPosition != MarketPosition.Flat)
                {
                    Print(string.Format("{0} Fallback flatten at day rollover (missed Tradeify cutoff).", barEndEt));
                    FlattenPosition(0, "RolloverFlatten");
                }

                currentDayDateNum = dateNum;
                cutoffExitDateNum = 0;
                pendingEntry = false;
                haveDayOpen = false;
                vwapPv = 0.0;
                vwapVol = 0.0;
                haveVwapPrev = false;
            }

            // Fallback only. The normal Tradeify exit is submitted from the
            // primary M1 series at 14:44 CT. This protects against a missing bar.
            if (barEndHhmm >= SESSION_CLOSE_ET && Position.MarketPosition != MarketPosition.Flat)
            {
                CancelPendingEntryOrders();
                Print(string.Format("{0} Fallback flatten at RTH close.", barEndEt));
                FlattenPosition(0, "SessionFallbackFlatten");
            }

            if (!inRth)
                return;

            // Capture today's RTH open on first in-session bar
            if (!haveDayOpen)
            {
                dayOpen = Opens[1][0];
                haveDayOpen = true;
            }

            // Reference for THIS bar's band: causal (as-of t-1)
            double reference;
            if (UseVwap)
            {
                reference = haveVwapPrev ? vwapPrev : dayOpen;
            }
            else
            {
                reference = dayOpen;
            }

            // Look up or allocate slot history
            if (!slotHist.TryGetValue(hhmm, out RollingQueue hist))
            {
                hist = new RollingQueue(NSigmaDays);
                slotHist[hhmm] = hist;
            }

            bool haveFullHistory = hist.IsFull;
            double sigma = hist.Mean;

            double upper = reference + BandMult * sigma;
            double lower = reference - BandMult * sigma;

            Values[0][0] = reference;
            if (haveFullHistory)
            {
                Values[1][0] = upper;
                Values[2][0] = lower;
            }

            // Warm-up gate: need full N-day history for THIS slot
            if (!tradingEnabled && haveFullHistory)
            {
                tradingEnabled = true;
            }

            if (tradingEnabled && !warmupLogged)
            {
                Print(string.Format("{0} Warm-up complete: full N-day sigma history available. Trading begins.", barEndEt));
                warmupLogged = true;
            }

            // ---- Entries on band break ----
            // The 15:30-16:00 ET bar closes after the 14:45 CT deadline.
            // Its signal must not be used in a Tradeify-compatible run.
            if (tradingEnabled && !pendingEntry && hhmm < ENTRY_CUTOFF_BAR_START_ET)
            {
                bool brokeUpper = Closes[1][0] > upper;
                bool brokeLower = Closes[1][0] < lower;

                if (brokeUpper && Position.MarketPosition != MarketPosition.Long)
                {
                    if (Position.MarketPosition == MarketPosition.Short)
                    {
                        Print(string.Format("{0} Opposite band break: closing short before flipping long.", barEndEt));
                    }
                    EnterLongPosition(barEndEt, Closes[1][0], reference, upper, lower);
                }
                else if (brokeLower && Position.MarketPosition != MarketPosition.Short)
                {
                    if (Position.MarketPosition == MarketPosition.Long)
                    {
                        Print(string.Format("{0} Opposite band break: closing long before flipping short.", barEndEt));
                    }
                    EnterShortPosition(barEndEt, Closes[1][0], reference, upper, lower);
                }
            }

            // ---- Update causal VWAP with THIS bar (becomes "prev" for NEXT bar) ----
            double signalClose = Closes[1][0];
            double typical = (Highs[1][0] + Lows[1][0] + signalClose) / 3.0;
            vwapPv += typical * Volumes[1][0];
            vwapVol += Volumes[1][0];
            if (vwapVol > 0.0)
            {
                vwapPrev = vwapPv / vwapVol;
                haveVwapPrev = true;
            }

            // ---- Push today's |close - day_open| into slot history for FUTURE days ----
            double moveAbs = Math.Abs(Closes[1][0] - dayOpen);
            hist.Push(moveAbs);
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
            if (IsCancelable(longEntryOrder))
                CancelOrder(longEntryOrder);
            if (IsCancelable(shortEntryOrder))
                CancelOrder(shortEntryOrder);
        }

        private void FlattenPosition(int barsInProgressIndex, string signalName)
        {
            if (Position.MarketPosition == MarketPosition.Long)
                ExitLong(barsInProgressIndex, Position.Quantity, signalName, "LongEntry");
            else if (Position.MarketPosition == MarketPosition.Short)
                ExitShort(barsInProgressIndex, Position.Quantity, signalName, "ShortEntry");
        }

        protected override void OnOrderUpdate(Order order, double limitPrice, double stopPrice,
            int quantity, int filled, double averageFillPrice, OrderState orderState,
            DateTime time, ErrorCode error, string comment)
        {
            if (order == null)
                return;

            if (order.Name == "LongEntry")
                longEntryOrder = IsCancelable(order) ? order : null;
            else if (order.Name == "ShortEntry")
                shortEntryOrder = IsCancelable(order) ? order : null;

            if ((order.Name == "LongEntry" || order.Name == "ShortEntry")
                && (orderState == OrderState.Filled || orderState == OrderState.Cancelled || orderState == OrderState.Rejected))
                pendingEntry = false;
        }

        protected override void OnExecutionUpdate(Execution execution, string executionId,
            double price, int quantity, MarketPosition marketPosition, string orderId, DateTime time)
        {
            if (execution == null || execution.Order == null || platformZone == null)
                return;

            DateTime executionEt = ToEastern(time);
            int hhmm = executionEt.Hour * 100 + executionEt.Minute;
            if (hhmm >= TRADEIFY_FLATTEN_BAR_END_ET && marketPosition != MarketPosition.Flat)
            {
                CancelPendingEntryOrders();
                FlattenPosition(0, "TradeifyLateFillFlatten");
            }
        }

        private void EnterLongPosition(DateTime barEndEt, double signalClose, double reference, double upper, double lower)
        {
            int qty = CalculateOrderQuantity(signalClose);
            if (qty <= 0)
                return;

            if (SlPct > 0.0)
            {
                double stopPrice = signalClose * (1.0 - SlPct / 100.0);
                SetStopLoss("LongEntry", CalculationMode.Price, stopPrice, false);
            }

            pendingEntry = true;
            longEntryOrder = EnterLong(0, qty, "LongEntry");
            if (longEntryOrder == null)
                pendingEntry = false;

            Print(string.Format("{0} ENTRY LONG close={1:F2} ref={2:F2} upper={3:F2} lower={4:F2} broke=UPPER qty={5}",
                barEndEt.ToString("HHmm"), signalClose, reference, upper, lower, qty));
        }

        private void EnterShortPosition(DateTime barEndEt, double signalClose, double reference, double upper, double lower)
        {
            int qty = CalculateOrderQuantity(signalClose);
            if (qty <= 0)
                return;

            if (SlPct > 0.0)
            {
                double stopPrice = signalClose * (1.0 + SlPct / 100.0);
                SetStopLoss("ShortEntry", CalculationMode.Price, stopPrice, false);
            }

            pendingEntry = true;
            shortEntryOrder = EnterShort(0, qty, "ShortEntry");
            if (shortEntryOrder == null)
                pendingEntry = false;

            Print(string.Format("{0} ENTRY SHORT close={1:F2} ref={2:F2} upper={3:F2} lower={4:F2} broke=LOWER qty={5}",
                barEndEt.ToString("HHmm"), signalClose, reference, upper, lower, qty));
        }

        private int CalculateOrderQuantity(double closePx)
        {
            if (SlPct > 0.0)
            {
                double stopDistance = closePx * (SlPct / 100.0);
                double pointValue = Instrument.MasterInstrument.PointValue;
                double lossPerContract = Math.Abs(stopDistance) * pointValue;

                if (lossPerContract <= 0.0)
                    return 0;

                double accountEquity = Account.Get(AccountItem.CashValue, Currency.UsDollar);
                if (accountEquity <= 0.0)
                    accountEquity = 100000.0;

                double riskUsd = FixedRiskUsd > 0.0 ? FixedRiskUsd : accountEquity * (RiskPct / 100.0);
                return (int)Math.Floor(riskUsd / lossPerContract);
            }

            return FixedLot;
        }

        protected override void OnPositionUpdate(Position position, double averagePrice, int quantity, MarketPosition marketPosition)
        {
            if (marketPosition == MarketPosition.Flat && SystemPerformance.AllTrades.Count > 0)
            {
                Trade lastTrade = SystemPerformance.AllTrades[SystemPerformance.AllTrades.Count - 1];
                Print(string.Format("EXIT pnl={0:F2}", lastTrade.ProfitCurrency));
            }
        }
    }
}
