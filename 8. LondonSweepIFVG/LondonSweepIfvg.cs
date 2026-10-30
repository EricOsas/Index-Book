#region Using declarations
using System;
using System.Collections.Generic;
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
using NinjaTrader.NinjaScript.Indicators;
#endregion

namespace NinjaTrader.NinjaScript.Strategies
{
    public class LondonSweepIfvg : Strategy
    {
        #region Constants
        private const int MAX_TRACKED_FVG = 64;
        private static readonly TimeZoneInfo EasternZone = TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");
        #endregion

        #region Helper Struct: Tracked FVG
        private class TrackedFvg
        {
            public DateTime StartTimeUtc { get; set; }
            public double Lower { get; set; }
            public double Upper { get; set; }
            public bool IsBullish { get; set; }
            public int DayStamp { get; set; }
        }
        #endregion

        #region Parameters
        // ---- Session window (ET) ----
        [NinjaScriptProperty]
        [Range(0, 2359)]
        [Display(Name = "London Session Start (ET hhmm)", GroupName = "Session Window", Order = 1)]
        public int LondonStartHhmm { get; set; }

        [NinjaScriptProperty]
        [Range(0, 2359)]
        [Display(Name = "London Session End (ET hhmm)", GroupName = "Session Window", Order = 2)]
        public int LondonEndHhmm { get; set; }

        [NinjaScriptProperty]
        [Range(0, 2359)]
        [Display(Name = "LS Time Limit Start (ET hhmm)", GroupName = "Session Window", Order = 3)]
        public int LsLimitStartHhmm { get; set; }

        [NinjaScriptProperty]
        [Range(0, 2359)]
        [Display(Name = "LS Time Limit End (ET hhmm)", GroupName = "Session Window", Order = 4)]
        public int LsLimitEndHhmm { get; set; }

        [NinjaScriptProperty]
        [Range(1, 50)]
        [Display(Name = "Session Line Duration (bars)", GroupName = "Session Window", Order = 5)]
        public int LineDurationBars { get; set; }

        [NinjaScriptProperty]
        [Range(1, 168)]
        [Display(Name = "Max IFVG Age (hours)", GroupName = "Session Window", Order = 6)]
        public int MaxIfvgAgeHours { get; set; }

        // ---- Strategy settings ----
        [NinjaScriptProperty]
        [Range(1, 50)]
        [Display(Name = "Swing Lookback", GroupName = "Strategy Settings", Order = 7)]
        public int SwingLookback { get; set; }

        // slType: 0 = Swing, 1 = ATR Multiplier
        [NinjaScriptProperty]
        [Range(0, 1)]
        [Display(Name = "SL Type (0=Swing, 1=ATR Multiplier)", GroupName = "Strategy Settings", Order = 8)]
        public int SlType { get; set; }

        // tpType: 0 = Range High/Low, 1 = Risk:Reward, 2 = ATR Multiplier
        [NinjaScriptProperty]
        [Range(0, 2)]
        [Display(Name = "TP Type (0=Range, 1=R:R, 2=ATR Multiplier)", GroupName = "Strategy Settings", Order = 9)]
        public int TpType { get; set; }

        [NinjaScriptProperty]
        [Range(0.1, 10.0)]
        [Display(Name = "Risk:Reward", GroupName = "Strategy Settings", Order = 10)]
        public double RiskReward { get; set; }

        [NinjaScriptProperty]
        [Range(1, 100)]
        [Display(Name = "ATR Length", GroupName = "Strategy Settings", Order = 11)]
        public int AtrLength { get; set; }

        [NinjaScriptProperty]
        [Range(0.1, 10.0)]
        [Display(Name = "SL ATR Multiplier", GroupName = "Strategy Settings", Order = 12)]
        public double SlAtrMult { get; set; }

        [NinjaScriptProperty]
        [Range(0.1, 10.0)]
        [Display(Name = "TP ATR Multiplier", GroupName = "Strategy Settings", Order = 13)]
        public double TpAtrMult { get; set; }

        // sizingMode: 0 = Fixed Lots, 1 = Risk $ Per Trade, 2 = Risk % of Account
        [NinjaScriptProperty]
        [Range(0, 2)]
        [Display(Name = "Position Sizing Mode (0=Fixed, 1=Risk $, 2=Risk %)", GroupName = "Position Sizing", Order = 14)]
        public int SizingMode { get; set; }

        [NinjaScriptProperty]
        [Range(1, 1000)]
        [Display(Name = "Fixed Lot Size (contracts)", GroupName = "Position Sizing", Order = 15)]
        public int FixedLots { get; set; }

        [NinjaScriptProperty]
        [Range(1.0, 100000.0)]
        [Display(Name = "Risk Per Trade ($)", GroupName = "Position Sizing", Order = 16)]
        public double RiskPerTradeUsd { get; set; }

        [NinjaScriptProperty]
        [Range(0.01, 100.0)]
        [Display(Name = "Risk Per Trade (% of Account)", GroupName = "Position Sizing", Order = 17)]
        public double RiskPerTradePct { get; set; }

        // ---- Day filter ----
        [NinjaScriptProperty]
        [Display(Name = "Use Day Filter", GroupName = "Day Filter", Order = 18)]
        public bool UseDayFilter { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Trade Monday", GroupName = "Day Filter", Order = 19)]
        public bool TradeMon { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Trade Tuesday", GroupName = "Day Filter", Order = 20)]
        public bool TradeTue { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Trade Wednesday", GroupName = "Day Filter", Order = 21)]
        public bool TradeWed { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Trade Thursday", GroupName = "Day Filter", Order = 22)]
        public bool TradeThu { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Trade Friday", GroupName = "Day Filter", Order = 23)]
        public bool TradeFri { get; set; }
        #endregion

        #region Internal State
        private ATR atr;
        private bool sessionActive;
        private double sessionHigh;
        private double sessionLow;
        private DateTime sessionStartTimeUtc;

        private double highLevel;
        private double lowLevel;
        private bool linesActive;
        private int lineStartBar;
        private int lineDeadlineBar;

        private bool highSwept;
        private bool lowSwept;
        private bool lsCreated;
        private DateTime lsMarkTimeUtc;
        private int lsDayStamp;
        private int lsDirection; // -1 short LS (high swept), 1 long LS (low swept), 0 none
        private double lsPrice;
        private bool ifvgMarkedAfterLs;

        private int lastDayStamp;
        private readonly List<TrackedFvg> fvgs = new List<TrackedFvg>();
        #endregion

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description                 = @"London Liquidity / IFVG Strategy by Shujat: Tracks London range, liquidity sweeps, and opposing IFVG confirmation.";
                Name                        = "LondonSweepIfvg";
                Calculate                   = Calculate.OnBarClose;
                EntriesPerDirection         = 1;
                EntryHandling               = EntryHandling.AllEntries;
                IsExitOnSessionCloseStrategy = false;
                BarsRequiredToTrade         = 20;

                LondonStartHhmm             = 1300;
                LondonEndHhmm               = 1900;
                LsLimitStartHhmm            = 1915;
                LsLimitEndHhmm              = 2345;
                LineDurationBars            = 6;
                MaxIfvgAgeHours             = 24;

                SwingLookback               = 5;
                SlType                      = 1; // 1 = ATR Multiplier
                TpType                      = 2; // 2 = ATR Multiplier
                RiskReward                  = 2.0;
                AtrLength                   = 14;
                SlAtrMult                   = 1.5;
                TpAtrMult                   = 1.5;

                SizingMode                  = 0; // 0 = Fixed Lots
                FixedLots                   = 1;
                RiskPerTradeUsd             = 100.0;
                RiskPerTradePct             = 1.0;

                UseDayFilter                = false;
                TradeMon                    = true;
                TradeTue                    = true;
                TradeWed                    = true;
                TradeThu                    = true;
                TradeFri                    = true;
            }
            else if (State == State.DataLoaded)
            {
                atr = ATR(AtrLength);
                ResetAllState();
            }
        }

        private void ResetAllState()
        {
            sessionActive = false;
            sessionHigh = double.NegativeInfinity;
            sessionLow = double.PositiveInfinity;
            sessionStartTimeUtc = DateTime.MinValue;

            highLevel = double.NaN;
            lowLevel = double.NaN;
            linesActive = false;
            lineStartBar = -1;
            lineDeadlineBar = -1;

            highSwept = false;
            lowSwept = false;
            lsCreated = false;
            lsMarkTimeUtc = DateTime.MinValue;
            lsDayStamp = 0;
            lsDirection = 0;
            lsPrice = double.NaN;
            ifvgMarkedAfterLs = false;

            lastDayStamp = 0;
            fvgs.Clear();
        }

        private static bool IsInWindow(int hhmm, int start, int end)
        {
            if (start <= end)
                return hhmm >= start && hhmm < end;
            return hhmm >= start || hhmm < end;
        }

        private static bool IsLargeBody(double open, double high, double low, double close)
        {
            double range = high - low;
            if (range <= 0.0) return false;
            double body = Math.Abs(open - close);
            return (body * 100.0) / range > 50.0;
        }

        protected override void OnBarUpdate()
        {
            if (CurrentBar < 2)
                return;

            DateTime barTimeUtc = Time[0].ToUniversalTime();
            DateTime barTimeEt = TimeZoneInfo.ConvertTimeFromUtc(barTimeUtc, EasternZone);

            int etHhmm = barTimeEt.Hour * 100 + barTimeEt.Minute;
            int todayStamp = barTimeEt.Year * 10000 + barTimeEt.Month * 100 + barTimeEt.Day;

            // Hard backstop: no session box may ever stay open more than 24h of clock time
            if (sessionActive && sessionStartTimeUtc != DateTime.MinValue)
            {
                if ((barTimeUtc - sessionStartTimeUtc).TotalHours > 24)
                {
                    sessionActive = false;
                    sessionHigh = double.NegativeInfinity;
                    sessionLow = double.PositiveInfinity;
                }
            }

            // Day rollover reset
            if (lastDayStamp != 0 && todayStamp != lastDayStamp)
            {
                sessionActive = false;
                sessionHigh = double.NegativeInfinity;
                sessionLow = double.PositiveInfinity;
                highSwept = false;
                lowSwept = false;
                linesActive = false;
                lsCreated = false;
                lsMarkTimeUtc = DateTime.MinValue;
                lsDayStamp = 0;
                lsDirection = 0;
                lsPrice = double.NaN;
                ifvgMarkedAfterLs = false;
            }
            lastDayStamp = todayStamp;

            bool inSession = IsInWindow(etHhmm, LondonStartHhmm, LondonEndHhmm);

            // Safety on data gaps jumping past sessions
            if (sessionActive && (barTimeUtc - sessionStartTimeUtc).TotalHours > 20)
            {
                sessionActive = false;
                sessionHigh = double.NegativeInfinity;
                sessionLow = double.PositiveInfinity;
                inSession = IsInWindow(etHhmm, LondonStartHhmm, LondonEndHhmm);
            }

            // ---- Session Start / Update / End ----
            if (inSession && !sessionActive)
            {
                sessionActive = true;
                sessionHigh = High[0];
                sessionLow = Low[0];
                sessionStartTimeUtc = barTimeUtc;
                linesActive = false;
                lsCreated = false;
                lsMarkTimeUtc = DateTime.MinValue;
                lsDayStamp = 0;
                lsDirection = 0;
                lsPrice = double.NaN;
                ifvgMarkedAfterLs = false;
            }
            else if (inSession && sessionActive)
            {
                sessionHigh = Math.Max(sessionHigh, High[0]);
                sessionLow = Math.Min(sessionLow, Low[0]);
            }
            else if (!inSession && sessionActive)
            {
                highLevel = sessionHigh;
                lowLevel = sessionLow;
                sessionActive = false;

                lineStartBar = CurrentBar;
                lineDeadlineBar = lineStartBar + LineDurationBars;
                linesActive = true;
            }

            // Stop extending lines after duration elapses
            if (linesActive && lineDeadlineBar >= 0 && CurrentBar >= lineDeadlineBar)
            {
                linesActive = false;
            }

            // ---- Liquidity Sweep Detection ----
            bool lsTimeOk = IsInWindow(etHhmm, LsLimitStartHhmm, LsLimitEndHhmm);
            bool hasHighLevel = !double.IsNaN(highLevel);
            bool hasLowLevel = !double.IsNaN(lowLevel);

            if (hasHighLevel && linesActive && !lsCreated && lsTimeOk)
            {
                if (Close[0] > highLevel)
                {
                    highSwept = true;
                }
                if (highSwept && Close[0] <= highLevel && IsLargeBody(Open[0], High[0], Low[0], Close[0]))
                {
                    lsCreated = true;
                    lsMarkTimeUtc = barTimeUtc;
                    lsDayStamp = todayStamp;
                    lsDirection = -1; // Bearish sweep
                    lsPrice = High[0];
                    highSwept = false;
                }
            }

            if (hasLowLevel && linesActive && !lsCreated && lsTimeOk)
            {
                if (Close[0] < lowLevel)
                {
                    lowSwept = true;
                }
                if (lowSwept && Close[0] >= lowLevel && IsLargeBody(Open[0], High[0], Low[0], Close[0]))
                {
                    lsCreated = true;
                    lsMarkTimeUtc = barTimeUtc;
                    lsDayStamp = todayStamp;
                    lsDirection = 1; // Bullish sweep
                    lsPrice = Low[0];
                    lowSwept = false;
                }
            }

            // ---- IFVG: Detect New FVGs (needs 2 bars back + current) ----
            bool isBullishFvg = High[2] < Low[0];
            bool isBearishFvg = Low[2] > High[0];

            bool trackFromSession = inSession && !ifvgMarkedAfterLs;
            bool trackAfterLs = lsCreated
                && !ifvgMarkedAfterLs
                && lsDayStamp == todayStamp
                && lsMarkTimeUtc != DateTime.MinValue
                && barTimeUtc > lsMarkTimeUtc;
            bool trackFvg = trackFromSession || trackAfterLs;

            if (trackFvg && isBullishFvg && fvgs.Count < MAX_TRACKED_FVG)
            {
                fvgs.Add(new TrackedFvg
                {
                    StartTimeUtc = Time[2].ToUniversalTime(),
                    Lower = High[2],
                    Upper = Low[0],
                    IsBullish = true,
                    DayStamp = todayStamp
                });
            }
            if (trackFvg && isBearishFvg && fvgs.Count < MAX_TRACKED_FVG)
            {
                fvgs.Add(new TrackedFvg
                {
                    StartTimeUtc = Time[2].ToUniversalTime(),
                    Lower = Low[0],
                    Upper = Low[2],
                    IsBullish = false,
                    DayStamp = todayStamp
                });
            }

            // ---- IFVG: Check Inversions ----
            bool bearConfirmed = false;
            bool bullConfirmed = false;
            double bearSl = double.NaN;
            double bearTp = double.NaN;
            double bullSl = double.NaN;
            double bullTp = double.NaN;

            if (fvgs.Count > 0)
            {
                double atrVal = atr[0];
                for (int i = fvgs.Count - 1; i >= 0; i--)
                {
                    TrackedFvg fvg = fvgs[i];
                    bool sameDay = todayStamp == fvg.DayStamp;
                    bool tooOld = (barTimeUtc - fvg.StartTimeUtc).TotalHours > MaxIfvgAgeHours;

                    if (tooOld || !sameDay || ifvgMarkedAfterLs)
                    {
                        fvgs.RemoveAt(i);
                        continue;
                    }

                    bool inverted = fvg.IsBullish ? (Close[0] < fvg.Lower) : (Close[0] > fvg.Upper);
                    bool isBearishIfvg = fvg.IsBullish;
                    bool isBullishIfvg = !fvg.IsBullish;

                    bool directionMatchesLs = (lsDirection == -1 && isBearishIfvg) || (lsDirection == 1 && isBullishIfvg);
                    bool positionValidVsLs = isBearishIfvg
                        ? (double.IsNaN(lsPrice) || fvg.Lower <= lsPrice)
                        : (double.IsNaN(lsPrice) || fvg.Upper >= lsPrice);

                    bool canMarkForSession = lsCreated && !inSession && lsTimeOk;

                    if (inverted && canMarkForSession && directionMatchesLs && positionValidVsLs)
                    {
                        int swingN = Math.Max(1, SwingLookback);
                        double swingHigh = double.NegativeInfinity;
                        double swingLow = double.PositiveInfinity;
                        for (int k = 0; k < swingN; k++)
                        {
                            swingHigh = Math.Max(swingHigh, High[k]);
                            swingLow = Math.Min(swingLow, Low[k]);
                        }

                        if (isBearishIfvg)
                        {
                            bearConfirmed = true;
                            bearSl = SlType == 1 ? (Close[0] + atrVal * SlAtrMult) : swingHigh;
                            double risk = bearSl - Close[0];
                            bearTp = TpType switch
                            {
                                1 => Close[0] - risk * RiskReward,
                                2 => Close[0] - atrVal * TpAtrMult,
                                _ => lowLevel
                            };
                        }
                        else
                        {
                            bullConfirmed = true;
                            bullSl = SlType == 1 ? (Close[0] - atrVal * SlAtrMult) : swingLow;
                            double risk = Close[0] - bullSl;
                            bullTp = TpType switch
                            {
                                1 => Close[0] + risk * RiskReward,
                                2 => Close[0] + atrVal * TpAtrMult,
                                _ => highLevel
                            };
                        }

                        ifvgMarkedAfterLs = true;
                        fvgs.RemoveAt(i);
                    }
                }
            }

            // ---- Day Filter ----
            DayOfWeek dow = barTimeEt.DayOfWeek;
            bool selectedDay = dow switch
            {
                DayOfWeek.Monday => TradeMon,
                DayOfWeek.Tuesday => TradeTue,
                DayOfWeek.Wednesday => TradeWed,
                DayOfWeek.Thursday => TradeThu,
                DayOfWeek.Friday => TradeFri,
                _ => false
            };
            bool tradingDayAllowed = !UseDayFilter || selectedDay;

            bool shortReady = tradingDayAllowed
                && bearConfirmed
                && !double.IsNaN(bearTp)
                && !double.IsNaN(bearSl)
                && bearSl > Close[0]
                && bearTp < Close[0];

            bool longReady = tradingDayAllowed
                && bullConfirmed
                && !double.IsNaN(bullTp)
                && !double.IsNaN(bullSl)
                && bullSl < Close[0]
                && bullTp > Close[0];

            if (shortReady && Position.MarketPosition == MarketPosition.Flat)
            {
                int qty = CalculateQuantity(Close[0], bearSl);
                if (qty > 0)
                {
                    EnterShort(qty, "ShortEntry");
                    SetStopLoss("ShortEntry", CalculationMode.Price, bearSl, false);
                    SetProfitTarget("ShortEntry", CalculationMode.Price, bearTp, false);
                    Print(string.Format("{0} SHORT entry: IFVG confirmed after bearish LS at {1:F2}, SL={2:F2}, TP={3:F2}, Qty={4}",
                        barTimeEt.ToString("HHmm"), Close[0], bearSl, bearTp, qty));
                }
            }

            if (longReady && Position.MarketPosition == MarketPosition.Flat)
            {
                int qty = CalculateQuantity(Close[0], bullSl);
                if (qty > 0)
                {
                    EnterLong(qty, "LongEntry");
                    SetStopLoss("LongEntry", CalculationMode.Price, bullSl, false);
                    SetProfitTarget("LongEntry", CalculationMode.Price, bullTp, false);
                    Print(string.Format("{0} LONG entry: IFVG confirmed after bullish LS at {1:F2}, SL={2:F2}, TP={3:F2}, Qty={4}",
                        barTimeEt.ToString("HHmm"), Close[0], bullSl, bullTp, qty));
                }
            }
        }

        private int CalculateQuantity(double entryPx, double stopPx)
        {
            double pointVal = Instrument.MasterInstrument.PointValue;
            double riskPerUnit = Math.Abs(entryPx - stopPx) * pointVal;

            if (SizingMode == 1 && riskPerUnit > 0.0)
            {
                return (int)Math.Max(1, Math.Floor(RiskPerTradeUsd / riskPerUnit));
            }
            if (SizingMode == 2 && riskPerUnit > 0.0)
            {
                double acctEquity = Account.Get(AccountItem.CashValue, Currency.UsDollar);
                if (acctEquity <= 0.0) acctEquity = 100000.0;
                double riskUsd = acctEquity * (RiskPerTradePct / 100.0);
                return (int)Math.Max(1, Math.Floor(riskUsd / riskPerUnit));
            }

            return Math.Max(1, FixedLots);
        }
    }
}
