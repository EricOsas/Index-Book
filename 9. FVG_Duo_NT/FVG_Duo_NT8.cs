#region Using declarations
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Xml.Serialization;
using NinjaTrader.Cbi;
using NinjaTrader.Gui;
using NinjaTrader.Gui.Chart;
using NinjaTrader.Gui.SuperDom;
using NinjaTrader.Gui.Tools;
using NinjaTrader.Data;
using NinjaTrader.NinjaScript;
using NinjaTrader.Core.FloatingPoint;
using NinjaTrader.NinjaScript.Indicators;
using NinjaTrader.NinjaScript.DrawingTools;
#endregion

namespace NinjaTrader.NinjaScript.Strategies
{
    public enum ModelPresetNT8
    {
        [Display(Name = "Optimal B3a (SMA/c2, Mult 1.25, Stop 1.25%, Return/DD 14.38)")]
        OptimalB3a,
        [Display(Name = "Parity RMA c1 (RMA/c1, Mult 1.50, Stop 1.50%, $440k Net)")]
        ParityRmaC1,
        [Display(Name = "Video Base B1 (SMA/c2, Mult 1.50, Stop 1.50%, $411k Net)")]
        VideoBaseB1,
        [Display(Name = "Custom (User Configuration Below)")]
        Custom
    }

    public enum SizingModeNT8
    {
        [Display(Name = "Fixed Contracts / Lots")]
        FixedLots,
        [Display(Name = "Risk % of Live Equity")]
        RiskPercent,
        [Display(Name = "Cash Risk USD")]
        CashRisk
    }

    public class FVG_Duo_NT8 : Strategy
    {
        #region Inputs
        [NinjaScriptProperty]
        [Display(Name = "Model Preset", Order = 1, GroupName = "1. Model Preset")]
        public ModelPresetNT8 Preset { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Enable Opening Drive", Order = 2, GroupName = "2. Strategy Logic")]
        public bool EnableOD { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Enable Trend FVG", Order = 3, GroupName = "2. Strategy Logic")]
        public bool EnableTrend { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Trend ATR Multiplier", Order = 4, GroupName = "2. Strategy Logic")]
        public double TrendAtrMult { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Trend EMA Fast", Order = 5, GroupName = "2. Strategy Logic")]
        public int TrendEmaFast { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Trend EMA Slow", Order = 6, GroupName = "2. Strategy Logic")]
        public int TrendEmaSlow { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Trend RR Target", Order = 7, GroupName = "2. Strategy Logic")]
        public double TrendRR { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "OD Stop Loss %", Order = 8, GroupName = "2. Strategy Logic")]
        public double ODStopPct { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Trend Trade After OD Stopout", Order = 9, GroupName = "2. Strategy Logic")]
        public bool TrendAfterODStopout { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Strict FVG Inequality", Order = 10, GroupName = "2. Strategy Logic")]
        public bool StrictFvg { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Position Sizing Mode", Order = 11, GroupName = "3. Sizing & Risk")]
        public SizingModeNT8 SizingMode { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Fixed Lots / Contracts", Order = 12, GroupName = "3. Sizing & Risk")]
        public int FixedLots { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Risk % per Trade", Order = 13, GroupName = "3. Sizing & Risk")]
        public double RiskPercent { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Cash Risk USD", Order = 14, GroupName = "3. Sizing & Risk")]
        public double CashRiskUSD { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Show On-Chart HUD", Order = 15, GroupName = "4. Visuals")]
        public bool ShowHUD { get; set; }
        #endregion

        #region Internal State
        private EMA _emaFast15m;
        private EMA _emaSlow15m;
        private ATR _atr14_15m;

        private static readonly TimeZoneInfo NewYorkTimeZone = TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");
        private TimeZoneInfo _platformZone;

        private DateTime _currentTradingDate;
        private bool _odCheck1Done;
        private bool _odCheck2Done;
        private bool _odFiredToday;
        private DateTime _odStopoutTime;
        private string _statusText;

        private int _activeSide;
        private string _activeSource;
        private double _activeEntryPrice;
        private double _activeStopPrice;
        private double _activeTargetPrice;

        private double _actAtrMult;
        private int _actEmaFast;
        private int _actEmaSlow;
        private double _actStopPct;
        private bool _actAfterStopout;
        #endregion

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Name = "FVG_Duo_NT8";
                Description = "FVG Duo - Opening Drive FVG + Trend FVG for Nasdaq Futures (NQ, MNQ)";
                Calculate = Calculate.OnBarClose;
                EntriesPerDirection = 1;
                EntryHandling = EntryHandling.AllEntries;
                IsExitOnSessionCloseStrategy = true;
                ExitOnSessionCloseSeconds = 30;
                BarsRequiredToTrade = 150;

                Preset = ModelPresetNT8.OptimalB3a;
                EnableOD = true;
                EnableTrend = true;
                TrendAtrMult = 1.25;
                TrendEmaFast = 50;
                TrendEmaSlow = 100;
                TrendRR = 3.0;
                ODStopPct = 0.0125;
                TrendAfterODStopout = true;
                StrictFvg = true;
                SizingMode = SizingModeNT8.FixedLots;
                FixedLots = 1;
                RiskPercent = 1.0;
                CashRiskUSD = 1000.0;
                ShowHUD = false;
            }
            else if (State == State.Configure)
            {
                AddDataSeries(BarsPeriodType.Minute, 1);  // BarsArray[1]: 1-minute
                AddDataSeries(BarsPeriodType.Minute, 10); // BarsArray[2]: 10-minute
                AddDataSeries(BarsPeriodType.Minute, 15); // BarsArray[3]: 15-minute

                ResolvePresetValues();
            }
            else if (State == State.DataLoaded)
            {
                _emaFast15m = EMA(BarsArray[3], _actEmaFast);
                _emaSlow15m = EMA(BarsArray[3], _actEmaSlow);
                _atr14_15m  = ATR(BarsArray[3], 14);

                _platformZone = Core.Globals.GeneralOptions.TimeZoneInfo;
                Print("[FVG_DUO] NinjaTrader application timezone: " + _platformZone.Id + "; strategy clock: America/New_York");
                _currentTradingDate = DateTime.MinValue;
                _odStopoutTime = DateTime.MinValue;
                _statusText = "INITIALIZED";
            }
        }

        private void ResolvePresetValues()
        {
            if (Preset == ModelPresetNT8.OptimalB3a)
            {
                _actAtrMult = 1.25;
                _actEmaFast = 50;
                _actEmaSlow = 100;
                _actStopPct = 0.0125;
                _actAfterStopout = true;
            }
            else if (Preset == ModelPresetNT8.ParityRmaC1)
            {
                _actAtrMult = 1.50;
                _actEmaFast = 50;
                _actEmaSlow = 100;
                _actStopPct = 0.0150;
                _actAfterStopout = false;
            }
            else if (Preset == ModelPresetNT8.VideoBaseB1)
            {
                _actAtrMult = 1.50;
                _actEmaFast = 50;
                _actEmaSlow = 100;
                _actStopPct = 0.0150;
                _actAfterStopout = false;
            }
            else
            {
                _actAtrMult = TrendAtrMult;
                _actEmaFast = TrendEmaFast;
                _actEmaSlow = TrendEmaSlow;
                _actStopPct = ODStopPct;
                _actAfterStopout = TrendAfterODStopout;
            }
        }

        private DateTime ToNewYork(DateTime time)
        {
            // NinjaTrader bar and execution timestamps are expressed in the
            // application timezone (Tools > Options > General > Time zone),
            // not necessarily the Windows machine timezone. Treat the
            // timestamp as wall time in that configured zone, then convert
            // through the Windows timezone database to New York.
            if (_platformZone == null)
                _platformZone = Core.Globals.GeneralOptions.TimeZoneInfo;

            DateTime platformTime = DateTime.SpecifyKind(time, DateTimeKind.Unspecified);
            return TimeZoneInfo.ConvertTime(platformTime, _platformZone, NewYorkTimeZone);
        }

        protected override void OnBarUpdate()
        {
            if (BarsInProgress != 1) return;

            if (CurrentBars[1] < 20 || CurrentBars[2] < 5 || CurrentBars[3] < _actEmaSlow + 10)
                return;

            DateTime nyTime = ToNewYork(Times[1][0]);
            DateTime nyDate = nyTime.Date;

            // Daily reset
            if (nyDate != _currentTradingDate)
            {
                _currentTradingDate = nyDate;
                _odCheck1Done = false;
                _odCheck2Done = false;
                _odFiredToday = false;
                _odStopoutTime = DateTime.MinValue;
                _statusText = "PRE-MARKET";
            }

            int minuteOfDay = nyTime.Hour * 60 + nyTime.Minute;

            // 1. Session Close Flatten (16:00 ET = minute 960)
            if (minuteOfDay >= 16 * 60)
            {
                if (Position.MarketPosition != MarketPosition.Flat)
                {
                    ExitLong(1, "Exit 16:00 Close", "");
                    ExitShort(1, "Exit 16:00 Close", "");
                    _activeSide = 0;
                    _statusText = "SESSION CLOSED";
                }
                return;
            }

            // 2. OPENING DRIVE CHECK 1 (09:40 ET = minute 580)
            if (EnableOD && !_odCheck1Done && minuteOfDay == 9 * 60 + 40)
            {
                _odCheck1Done = true;
                if (CurrentBars[2] >= 3)
                {
                    double c1High = Highs[2][3], c1Low = Lows[2][3];
                    double c3High = Highs[2][1], c3Low = Lows[2][1];

                    bool bull = StrictFvg ? (c3Low > c1High) : (c3Low >= c1High);
                    bool bear = StrictFvg ? (c3High < c1Low) : (c3High <= c1Low);

                    if (bull || bear)
                    {
                        _odFiredToday = true;
                        _statusText = "OD CHECK 1 FIRED";
                        HandleODSignal(bull ? 1 : -1);
                    }
                }
            }

            // 3. OPENING DRIVE CHECK 2 (09:50 ET = minute 590, only if Check 1 did not fire)
            if (EnableOD && !_odFiredToday && !_odCheck2Done && minuteOfDay == 9 * 60 + 50)
            {
                _odCheck2Done = true;
                if (CurrentBars[2] >= 3)
                {
                    double c1High = Highs[2][3], c1Low = Lows[2][3];
                    double c3High = Highs[2][1], c3Low = Lows[2][1];

                    bool bull = StrictFvg ? (c3Low > c1High) : (c3Low >= c1High);
                    bool bear = StrictFvg ? (c3High < c1Low) : (c3High <= c1Low);

                    if (bull || bear)
                    {
                        _odFiredToday = true;
                        _statusText = "OD CHECK 2 FIRED";
                        HandleODSignal(bull ? 1 : -1);
                    }
                }
            }

            // 4. TREND FVG (On completed 15m bars, between 09:30 and 15:30 ET)
            if (EnableTrend && (nyTime.Minute % 15 == 0) && minuteOfDay >= 9 * 60 + 30 && minuteOfDay <= 15 * 60 + 30)
            {
                bool canTradeTrend = (Position.MarketPosition == MarketPosition.Flat);
                if (_odFiredToday && !canTradeTrend)
                {
                    if (_actAfterStopout && _odStopoutTime > DateTime.MinValue && nyTime >= _odStopoutTime && Position.MarketPosition == MarketPosition.Flat)
                        canTradeTrend = true;
                }
                else if (!_odFiredToday && Position.MarketPosition == MarketPosition.Flat)
                {
                    canTradeTrend = true;
                }

                if (canTradeTrend)
                {
                    CheckAndExecuteTrendFVG();
                }
            }
        }

        private void HandleODSignal(int odSide)
        {
            double entryPrice = Closes[1][0];
            double tick = TickSize;

            // Merge Case: open Trend Long meets OD Long
            if (Position.MarketPosition == MarketPosition.Long && odSide > 0)
            {
                double rawStop = _activeEntryPrice * (1.0 - _actStopPct);
                _activeStopPrice = Math.Floor(rawStop / tick) * tick;
                _activeSource = "MERGED";
                // Cancel existing managed orders & reset stop only
                ExitLongStopMarket(1, true, Position.Quantity, _activeStopPrice, "Merged_SL", "Trend Long");
                _statusText = "MERGED TO OD RULES";
                return;
            }

            // Flip Case: open Trend Long meets OD Short
            if (Position.MarketPosition == MarketPosition.Long && odSide < 0)
            {
                ExitLong(1, "Flip Long to Short", "");
                double rawStop = entryPrice * (1.0 + _actStopPct);
                _activeStopPrice = Math.Ceiling(rawStop / tick) * tick;
                _activeSource = "OD";
                _activeSide = -1;
                _activeEntryPrice = entryPrice;
                EnterShort(1, FixedLots, "OD Short");
                ExitShortStopMarket(1, true, FixedLots, _activeStopPrice, "OD_SL", "OD Short");
                _statusText = "FLIPPED TO OD SHORT";
                return;
            }

            // Flat Case: enter OD trade
            if (Position.MarketPosition == MarketPosition.Flat)
            {
                if (odSide > 0)
                {
                    double rawStop = entryPrice * (1.0 - _actStopPct);
                    _activeStopPrice = Math.Floor(rawStop / tick) * tick;
                    _activeSource = "OD";
                    _activeSide = 1;
                    _activeEntryPrice = entryPrice;
                    EnterLong(1, FixedLots, "OD Long");
                    ExitLongStopMarket(1, true, FixedLots, _activeStopPrice, "OD_SL", "OD Long");
                    _statusText = "OD LONG ENTERED";
                }
                else
                {
                    double rawStop = entryPrice * (1.0 + _actStopPct);
                    _activeStopPrice = Math.Ceiling(rawStop / tick) * tick;
                    _activeSource = "OD";
                    _activeSide = -1;
                    _activeEntryPrice = entryPrice;
                    EnterShort(1, FixedLots, "OD Short");
                    ExitShortStopMarket(1, true, FixedLots, _activeStopPrice, "OD_SL", "OD Short");
                    _statusText = "OD SHORT ENTERED";
                }
            }
        }

        private void CheckAndExecuteTrendFVG()
        {
            if (CurrentBars[3] < _actEmaSlow + 10) return;

            if (Times[3][1].Subtract(Times[3][2]).TotalMinutes != 15 || Times[3][2].Subtract(Times[3][3]).TotalMinutes != 15)
                return;

            double c1High = Highs[3][3], c1Low = Lows[3][3];
            double c3Low  = Lows[3][1];

            bool bull = StrictFvg ? (c3Low > c1High) : (c3Low >= c1High);
            if (!bull) return;

            double atr = _atr14_15m[1];
            if (double.IsNaN(atr) || atr <= 0) return;
            double c2Range = Highs[3][2] - Lows[3][2];
            if (c2Range < _actAtrMult * atr) return;

            double emaFast = _emaFast15m[1];
            double emaSlow = _emaSlow15m[1];
            if (double.IsNaN(emaFast) || double.IsNaN(emaSlow) || emaFast <= emaSlow) return;

            double entryPrice = Closes[1][0];
            double stopPrice  = c1Low - TickSize;
            if (entryPrice <= stopPrice) return;

            double R = entryPrice - stopPrice;
            double targetPrice = entryPrice + TrendRR * R;

            _activeSide = 1;
            _activeSource = "TREND";
            _activeEntryPrice = entryPrice;
            _activeStopPrice = stopPrice;
            _activeTargetPrice = targetPrice;

            EnterLong(1, FixedLots, "Trend Long");
            ExitLongStopMarket(1, true, FixedLots, stopPrice, "Trend_SL", "Trend Long");
            ExitLongLimit(1, true, FixedLots, targetPrice, "Trend_TP", "Trend Long");
            _statusText = "TREND LONG ENTERED";
        }

        protected override void OnExecutionUpdate(Execution execution, string executionId, double price, int quantity, MarketPosition marketPosition, string orderId, DateTime time)
        {
            if (execution.Order != null && execution.Order.OrderState == OrderState.Filled)
            {
                if (execution.Name != null && execution.Name.Contains("SL") && _activeSource == "OD")
                {
                    _odStopoutTime = ToNewYork(time);
                }
            }
        }
    }
}