// Sigma Breakout NT8 — N3 / 1.7 candidate, unmanaged execution with durable restart recovery.
//+------------------------------------------------------------------+
//|                             SigmaBreakoutNT8.cs    |
//| Sigma Breakout Strategy on Nasdaq                                |
//| Developed by Osamwonyi Eric (You_FoundEric)                      |
//| Copyright 2026-2027, Osamwonyi Eric (@You_FoundEric)             |
//+------------------------------------------------------------------+

using NinjaTrader.Cbi;
using NinjaTrader.Core;
using NinjaTrader.Data;
using NinjaTrader.Gui.Chart;
using NinjaTrader.Gui;
using NinjaTrader.NinjaScript.DrawingTools;
using NinjaTrader.NinjaScript.Indicators;
using SharpDX.Direct2D1;
using SharpDX.DirectWrite;
using SharpDX;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Timers;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using System.Windows;
using System.Xml.Serialization;
using System;

namespace NinjaTrader.NinjaScript.Strategies
{
public class SigmaBreakoutNT8 : Strategy
{
	private sealed class ExecutionView
	{
		internal string Tag;

		internal int Side;

		internal double Entry;

		internal double InitialStop;

		internal double Stop;

		internal double Exit;
		internal double InitialTarget;

		internal DateTime Entered;

		internal DateTime Exited;

		internal DateTime TrailStart;

		internal bool Trailing;
	}

	private static class StrategyExecutionVisual
	{
		private sealed class PendingChartDraws
		{
			internal readonly Dictionary<string, Action> Latest = new Dictionary<string, Action>();

			internal readonly Queue<string> Order = new Queue<string>();

			internal bool Queued;
		}

		public static readonly System.Windows.Media.Brush ClrEntryLine = MakeBrush(214, 218, 224);

		public static readonly System.Windows.Media.Brush ClrSLBox = MakeBrush(55, 57, 68);

		public static readonly System.Windows.Media.Brush ClrTPBox = MakeBrush(22, 55, 120);

		public static readonly System.Windows.Media.Brush ClrTrailingStop = MakeBrush(162, 138, 88);

		private static int reportedDrawFailure;

		private static readonly ConditionalWeakTable<Strategy, PendingChartDraws> pendingChartDraws = new ConditionalWeakTable<Strategy, PendingChartDraws>();

		private static bool ValidTime(DateTime t)
		{
			return t > new DateTime(2000, 1, 1);
		}

		public static System.Windows.Media.Brush MakeBrush(byte r, byte g, byte b)
		{
			System.Windows.Media.SolidColorBrush solidColorBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(r, g, b));
			solidColorBrush.Freeze();
			return solidColorBrush;
		}

		private static void Guard(Strategy strategy, string tag, Action draw)
		{
			try
			{
				ChartControl chartControl = strategy.ChartControl;
				if (chartControl == null || chartControl.Dispatcher.HasShutdownStarted || chartControl.Dispatcher.HasShutdownFinished)
				{
					return;
				}
				PendingChartDraws value = pendingChartDraws.GetValue(strategy, (Strategy key) => new PendingChartDraws());
				lock (value)
				{
					if (!value.Latest.ContainsKey(tag))
					{
						value.Order.Enqueue(tag);
					}
					value.Latest[tag] = () =>
					{
						if (strategy.State == State.Terminated || ((SigmaBreakoutNT8)strategy).VisualsSuppressed)
						{
							return;
						}
						try
						{
							draw();
						}
						catch (Exception ex2)
						{
							Report(strategy, ex2);
						}
					};
					if (value.Queued)
					{
						return;
					}
					value.Queued = true;
				}
				QueueChartDraws(strategy, chartControl, value);
			}
			catch (Exception ex)
			{
				Report(strategy, ex);
			}
		}

		private static void QueueChartDraws(Strategy strategy, ChartControl chart, PendingChartDraws pending)
		{
			try
			{
				chart.Dispatcher.BeginInvoke(DispatcherPriority.Background, (Action)(() =>
				{
					List<Action> list = new List<Action>();
					lock (pending)
					{
						if (strategy.State == State.Terminated || chart != strategy.ChartControl)
						{
							pending.Latest.Clear();
							pending.Order.Clear();
							pending.Queued = false;
							return;
						}
						while (pending.Order.Count > 0 && list.Count < 32)
						{
							string key = pending.Order.Dequeue();
							list.Add(pending.Latest[key]);
							pending.Latest.Remove(key);
						}
					}
					foreach (Action item in list)
					{
						item();
					}
					lock (pending)
					{
						if (pending.Latest.Count == 0)
						{
							pending.Queued = false;
							return;
						}
					}
					QueueChartDraws(strategy, chart, pending);
				}));
			}
			catch (Exception ex)
			{
				lock (pending)
				{
					pending.Latest.Clear();
					pending.Order.Clear();
					pending.Queued = false;
				}
				Report(strategy, ex);
			}
		}

		private static void Report(Strategy strategy, Exception ex)
		{
			if (Interlocked.Exchange(ref reportedDrawFailure, 1) == 0)
			{
				strategy.Print("[Execution visuals] Drawing update failed: " + ex.Message);
			}
		}

		private static void Sink(DrawingTool dt)
		{
			try
			{
				if (dt != null && dt.ZOrder != 1)
				{
					dt.ZOrder = 1;
				}
			}
			catch
			{
			}
		}

		public static void DrawBoundedLine(Strategy strategy, string tag, DateTime t1, double p1, DateTime t2, double p2, System.Windows.Media.Brush brush, DashStyleHelper dash, int width)
		{
			if (ValidTime(t1))
			{
				if (t2 <= t1)
				{
					t2 = t1.AddMinutes(1.0);
				}
				Guard(strategy, tag, () =>
				{
					Sink(Draw.Line(strategy, tag, isAutoScale: false, t1, p1, t2, p2, brush, dash, width));
				});
			}
		}

		public static void DrawExecutionTool(Strategy strategy, string tagBase, bool isLong, double entryPx, double slPx, double tpPx, double levelPx, DateTime levelTime, DateTime entryTime, DateTime exitTime, bool trailing, DateTime trailStartTime, bool synthetic)
		{
			if (!ValidTime(entryTime))
			{
				return;
			}
			if (!ValidTime(levelTime))
			{
				levelTime = entryTime;
			}
			DateTime right = ((exitTime < entryTime) ? entryTime : exitTime);
			int fillOpacity = (synthetic ? 25 : 40);
			DrawBoundedLine(strategy, tagBase + "_ENT", levelTime, levelPx, entryTime, levelPx, ClrEntryLine, synthetic ? DashStyleHelper.Dash : DashStyleHelper.Solid, 2);
			if (!trailing)
			{
				Guard(strategy, tagBase + "_SL", () =>
				{
					Sink(Draw.Rectangle(strategy, tagBase + "_SL", isAutoScale: false, entryTime, entryPx, right, slPx, Brushes.Transparent, ClrSLBox, fillOpacity));
				});
			}
			else
			{
				Guard(strategy, tagBase + "_SL", () =>
				{
					strategy.RemoveDrawObject(tagBase + "_SL");
				});
			}
			if (tpPx > 0.0)
			{
				Guard(strategy, tagBase + "_TP", () =>
				{
					Sink(Draw.Rectangle(strategy, tagBase + "_TP", isAutoScale: false, entryTime, entryPx, right, tpPx, Brushes.Transparent, ClrTPBox, fillOpacity));
				});
			}
			if (trailing && ValidTime(trailStartTime))
			{
				DrawBoundedLine(strategy, tagBase + "_TRAIL", (trailStartTime < entryTime) ? entryTime : trailStartTime, slPx, right, slPx, ClrTrailingStop, DashStyleHelper.Dot, 2);
				return;
			}
			Guard(strategy, tagBase + "_TRAIL", () =>
			{
				strategy.RemoveDrawObject(tagBase + "_TRAIL");
			});
		}
	}

	internal sealed class ModelDashState
	{
		internal string Clock;

		internal string Status;

		internal string RangeHigh;

		internal string RangeLow;

		internal string RangeWidth;

		internal string Position;

		internal string Stop;

		internal string Risk;

		internal string History;

		internal string Costs;

		internal string Title;

		internal string RangeCaption;

		internal string HighCaption;

		internal string LowCaption;

		internal bool InTrade;

		internal bool NewsBlocked;
	}

	internal sealed class ModelDashboard
	{
		private readonly object sync = new object();

		private ModelDashState state;

		private bool collapsed;

		private bool dragging;

		private double offsetX;

		private double offsetY;

		private double dragX;

		private double dragY;

		private Rect shellRect;

		private Rect headerRect;

		private Rect toggleRect;

		private const float Width = 258f;

		private const float FullHeight = 267f;

		private const float HeaderHeight = 64f;

		internal void Publish(ModelDashState next)
		{
			lock (sync)
			{
				state = next;
			}
		}

		internal bool MouseDown(double x, double y)
		{
			lock (sync)
			{
				if (state == null)
				{
					return false;
				}
				if (toggleRect.Contains(x, y))
				{
					collapsed = !collapsed;
					return true;
				}
				if (!headerRect.Contains(x, y))
				{
					return false;
				}
				dragging = true;
				dragX = x - shellRect.X;
				dragY = y - shellRect.Y;
				return true;
			}
		}

		internal bool MouseMove(double x, double y)
		{
			lock (sync)
			{
				if (!dragging)
				{
					return false;
				}
				double num = x - dragX - shellRect.X;
				double num2 = y - dragY - shellRect.Y;
				offsetX += num;
				offsetY += num2;
				shellRect.Offset(num, num2);
				headerRect.Offset(num, num2);
				toggleRect.Offset(num, num2);
				return true;
			}
		}

		internal bool MouseUp()
		{
			lock (sync)
			{
				bool result = dragging;
				dragging = false;
				return result;
			}
		}

		internal void Render(RenderTarget rt, ChartControl chart)
		{
			ModelDashState modelDashState;
			bool flag;
			double num;
			double num2;
			lock (sync)
			{
				modelDashState = state;
				flag = collapsed;
				num = offsetX;
				num2 = offsetY;
			}
			if (modelDashState == null || rt == null || chart == null)
			{
				return;
			}
			float num3 = (float)((double)((float)chart.CanvasRight - 258f - 12f) + num);
			float num4 = (float)(42.0 + num2);
			float num5 = (flag ? 64f : 267f);
			float num6 = num3 + 258f - 16f;
			RoundedRectangle roundedRect = new RoundedRectangle
			{
				Rect = new RectangleF(num3, num4, 258f, num5),
				RadiusX = 9.75f,
				RadiusY = 9.75f
			};
			using (SharpDX.Direct2D1.SolidColorBrush brush = new SharpDX.Direct2D1.SolidColorBrush(rt, new Color4(12f / 255f, 1f / 15f, 27f / 255f, 1f)))
			{
				using SharpDX.Direct2D1.SolidColorBrush brush2 = new SharpDX.Direct2D1.SolidColorBrush(rt, new Color4(182f / 255f, 205f / 255f, 235f / 255f, 110f / 255f));
				using SharpDX.Direct2D1.SolidColorBrush solidColorBrush = new SharpDX.Direct2D1.SolidColorBrush(rt, new Color4(14f / 15f, 244f / 255f, 250f / 255f, 1f));
				using SharpDX.Direct2D1.SolidColorBrush brush3 = new SharpDX.Direct2D1.SolidColorBrush(rt, new Color4(165f / 255f, 181f / 255f, 0.8f, 1f));
				using SharpDX.Direct2D1.SolidColorBrush brush4 = new SharpDX.Direct2D1.SolidColorBrush(rt, new Color4(115f / 255f, 171f / 255f, 1f, 1f));
				using SharpDX.Direct2D1.SolidColorBrush solidColorBrush2 = new SharpDX.Direct2D1.SolidColorBrush(rt, new Color4(91f / 255f, 215f / 255f, 164f / 255f, 1f));
				using SharpDX.Direct2D1.SolidColorBrush solidColorBrush3 = new SharpDX.Direct2D1.SolidColorBrush(rt, new Color4(1f, 125f / 255f, 128f / 255f, 1f));
				using SharpDX.DirectWrite.Factory dw = new SharpDX.DirectWrite.Factory();
				rt.FillRoundedRectangle(roundedRect, brush);
				rt.DrawRoundedRectangle(roundedRect, brush2, 1f);
				rt.FillRectangle(new RectangleF(num3 + 12f, num4 + 14f, 2f, 39f), brush4);
				Text(rt, dw, modelDashState.Title, 13f, solidColorBrush, num3 + 24f, num4 + 13f, 198f, bold: true);
				Text(rt, dw, modelDashState.Clock, 9f, brush3, num3 + 24f, num4 + 34f, 210f);
				RoundedRectangle roundedRect2 = new RoundedRectangle
				{
					Rect = new RectangleF(num6 - 12f, num4 + 13f, 19f, 19f),
					RadiusX = 6f,
					RadiusY = 6f
				};
				rt.FillRoundedRectangle(roundedRect2, brush2);
				Text(rt, dw, flag ? "+" : "-", 13f, solidColorBrush, num6 - 12f, num4 + 14f, 19f, bold: false, right: false, center: true);
				if (!flag)
				{
					Rule(rt, brush2, num3 + 20f, num4 + 61f, 222f);
					Text(rt, dw, modelDashState.Status, 11.5f, modelDashState.NewsBlocked ? solidColorBrush3 : solidColorBrush, num3 + 20f, num4 + 69f, 222f);
					Text(rt, dw, modelDashState.RangeCaption, 9f, brush3, num3 + 20f, num4 + 95f, 184f);
					Text(rt, dw, modelDashState.RangeWidth, 13f, solidColorBrush, num6 - 55f, num4 + 92f, 55f, bold: true, right: true);
					Text(rt, dw, modelDashState.HighCaption, 9f, solidColorBrush2, num3 + 20f, num4 + 122f, 30f);
					Text(rt, dw, modelDashState.RangeHigh, 13f, solidColorBrush, num3 + 57f, num4 + 119f, 65f, bold: true, right: true);
					Text(rt, dw, modelDashState.LowCaption, 9f, solidColorBrush3, num3 + 140f, num4 + 122f, 30f);
					Text(rt, dw, modelDashState.RangeLow, 13f, solidColorBrush, num3 + 177f, num4 + 119f, 65f, bold: true, right: true);
					Rule(rt, brush2, num3 + 20f, num4 + 149f, 222f);
					Text(rt, dw, "POSITION", 9f, brush3, num3 + 20f, num4 + 159f, 106f);
					Text(rt, dw, "HARD STOP", 9f, brush3, num3 + 139f, num4 + 159f, num6 - num3 - 139f, bold: false, right: true);
					Text(rt, dw, modelDashState.Position, 13f, modelDashState.InTrade ? solidColorBrush2 : solidColorBrush, num3 + 20f, num4 + 174f, 106f, bold: true);
					Text(rt, dw, modelDashState.Stop, 13f, solidColorBrush3, num3 + 139f, num4 + 174f, num6 - num3 - 139f, bold: true, right: true);
					Rule(rt, brush2, num3 + 20f, num4 + 199f, 222f);
					Text(rt, dw, modelDashState.Risk, 11.5f, solidColorBrush, num3 + 20f, num4 + 210f, 222f);
					Text(rt, dw, modelDashState.Costs, 9.5f, brush3, num3 + 20f, num4 + 229f, 222f);
					Text(rt, dw, modelDashState.History, 8.5f, brush3, num3 + 20f, num4 + 248f, 222f);
				}
			}
			lock (sync)
			{
				shellRect = new Rect(num3, num4, 258.0, num5);
				headerRect = new Rect(num3, num4, 258.0, 64.0);
				toggleRect = new Rect(num6 - 15f, num4 + 10f, 25.0, 25.0);
			}
		}

		private static void Text(RenderTarget rt, SharpDX.DirectWrite.Factory dw, string value, float size, SharpDX.Direct2D1.SolidColorBrush brush, float x, float y, float width, bool bold = false, bool right = false, bool center = false)
		{
			if (string.IsNullOrEmpty(value))
			{
				return;
			}
			using TextFormat textFormat = new TextFormat(dw, "Segoe UI", bold ? SharpDX.DirectWrite.FontWeight.DemiBold : SharpDX.DirectWrite.FontWeight.Normal, SharpDX.DirectWrite.FontStyle.Normal, size);
			textFormat.TextAlignment = (right ? SharpDX.DirectWrite.TextAlignment.Trailing : (center ? SharpDX.DirectWrite.TextAlignment.Center : SharpDX.DirectWrite.TextAlignment.Leading));
			textFormat.WordWrapping = WordWrapping.NoWrap;
			rt.DrawText(value, textFormat, new RectangleF(x, y, width, size + 7f), brush, DrawTextOptions.Clip);
		}

		private static void Rule(RenderTarget rt, SharpDX.Direct2D1.SolidColorBrush brush, float x, float y, float width)
		{
			rt.DrawLine(new Vector2(x, y), new Vector2(x + width, y), brush, 0.5f);
		}
	}

	private sealed class RecoverySnapshot
	{
		internal int Quantity;

		internal int Filled;

		internal double Limit;

		internal double Stop;

		internal double Average;

		internal OrderState State;

		internal DateTime Time;

		internal ErrorCode Error;

		internal string Comment;
	}

	private sealed class RecoveryRequest
	{
		internal string Name;

		internal string Id;

		internal string Oco;

		internal OrderAction Action;

		internal OrderType Type;

		internal int Quantity;

		internal int Processed;

		internal double Limit;

		internal double Stop;

		internal double Value;
	}

	private sealed class FeeSlice
	{
		internal Execution Execution;

		internal int Quantity;

		internal bool Entry;
	}

	private sealed class FeeTrade
	{
		internal string Signal;

		internal bool Closed;

		internal readonly Dictionary<string, FeeSlice> Slices = new Dictionary<string, FeeSlice>();

		internal int EntryQuantity => Slices.Values.Where((FeeSlice x) => x.Entry).Sum((FeeSlice x) => x.Quantity);

		internal int ExitQuantity => Slices.Values.Where((FeeSlice x) => !x.Entry).Sum((FeeSlice x) => x.Quantity);

		internal double RoundTurn => (Closed && EntryQuantity > 0 && EntryQuantity == ExitQuantity) ? (Slices.Values.Sum((FeeSlice x) => Charge(x.Execution)) / (double)EntryQuantity) : 0.0;

		internal static double Charge(Execution e)
		{
			double commission = e.Commission;
			double fee = e.Fee;
			return ((double.IsNaN(commission) || double.IsInfinity(commission)) ? 0.0 : Math.Abs(commission)) + ((double.IsNaN(fee) || double.IsInfinity(fee)) ? 0.0 : Math.Abs(fee));
		}
	}

private static class ProductExposure
	{
		private const string Slot = "IndependentEquityDirectionExposure.v1";
		private const string ExactSlot = "IndependentExactContractDirectionExposure.v1";
		private const string ExitSlot = "IndependentVerifiedExitOrders.v1";
		private static readonly HashSet<string> Equity = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
		{
			"ES", "MES", "NES", "NQ", "MNQ", "NNQ", "YM", "MYM", "RTY", "M2K",
			"EMD", "NKD", "FDAX", "FDXM", "FDXS", "FESX", "FSXE", "FXXP"
		};

		internal static void ObserveExit(Order order)
		{
			lock (AppDomain.CurrentDomain)
			{
				var exits = AppDomain.CurrentDomain.GetData(ExitSlot) as HashSet<Order>;
				if (exits == null) AppDomain.CurrentDomain.SetData(ExitSlot, exits = new HashSet<Order>());
				exits.RemoveWhere(o => Terminal(o));
				exits.Add(order);
			}
		}

		internal static bool Reserve(string ownerId, int side, Account account, Instrument instrument, bool broad, out string reason, HashSet<Order> ownEntries = null, long ownNet = 0)
		{
			return ReserveCore(ownerId, side, account, instrument, broad, false, out reason, ownEntries, ownNet);
		}

		internal static bool ReserveBoth(string ownerId, Account account, Instrument instrument, bool broad, out string reason, HashSet<Order> ownEntries = null, long ownNet = 0)
		{
			return ReserveCore(ownerId, 0, account, instrument, broad, true, out reason, ownEntries, ownNet);
		}

		private static bool ReserveCore(string ownerId, int side, Account ownAccount, Instrument ownInstrument, bool broad, bool both, out string reason, HashSet<Order> ownEntries, long ownNet)
		{
			if (string.IsNullOrEmpty(ownerId) || ownAccount == null || ownInstrument == null || (!both && side != 1 && side != -1))
			{
				reason = "Cannot verify shared account/contract; entry blocked";
				return false;
			}
			// FX/metals reserve their own exact contract; they never represent index direction.
			bool index = Equity.Contains(ownInstrument.MasterInstrument.Name);
			broad = broad && index;
			var snapshots = new Dictionary<Account, Tuple<Position[], Order[]>>();
			var managedExits = new HashSet<Order>();
			try
			{
				Account[] accounts;
				lock (Account.All) accounts = broad ? Account.All.ToArray() : new[] { ownAccount };
				foreach (Account account in accounts)
				{
					Position[] positions; Order[] orders; Execution[] executions;
					lock (account.Positions) positions = account.Positions.ToArray();
					lock (account.Orders) orders = account.Orders.ToArray();
					lock (account.Executions) executions = account.Executions.ToArray();
					// Only exact object references supplied by the owned executor may be excluded.
					orders = orders.Where(o => !(ReferenceEquals(account, ownAccount) && ownEntries != null && ownEntries.Contains(o))).ToArray();
					snapshots[account] = Tuple.Create(positions, orders);
					foreach (Order order in orders) if (ManagedExit(order, executions)) managedExits.Add(order);
				}
			}
			catch (Exception) { reason = "Cannot snapshot account exposure; entry blocked"; return false; }
			lock (AppDomain.CurrentDomain)
			{
				var directions = AppDomain.CurrentDomain.GetData(Slot) as Dictionary<string, int>;
				if (directions == null) AppDomain.CurrentDomain.SetData(Slot, directions = new Dictionary<string, int>());
				var exact = AppDomain.CurrentDomain.GetData(ExactSlot) as Dictionary<string, Tuple<Account, string, int>>;
				if (exact == null) AppDomain.CurrentDomain.SetData(ExactSlot, exact = new Dictionary<string, Tuple<Account, string, int>>());
				if ((broad && directions.Any(x => x.Key != ownerId && (both || x.Value != side))) ||
					exact.Any(x => x.Key != ownerId && ReferenceEquals(x.Value.Item1, ownAccount) && x.Value.Item2 == ownInstrument.FullName && (both || x.Value.Item3 != side)))
				{
					reason = "Opposing entry reserved; same-contract netting blocked";
					return false;
				}
				try
				{
					foreach (var snapshot in snapshots)
					{
						Account account = snapshot.Key;
						Position[] positions = snapshot.Value.Item1;
						Order[] orders = snapshot.Value.Item2;
						var instruments = positions.Select(p => p.Instrument).Concat(orders.Select(o => o.Instrument))
							.Concat(ReferenceEquals(account, ownAccount) ? new[] { ownInstrument } : new Instrument[0])
							.Where(i => i != null && ((ReferenceEquals(account, ownAccount) && i.FullName == ownInstrument.FullName) ||
								(broad && (Equity.Contains(i.MasterInstrument.Name) || i.MasterInstrument.Name == "FVS"))))
							.GroupBy(i => i.FullName).Select(g => g.First());
						foreach (Instrument instrument in instruments)
						{
							long net = positions.Where(p => p.Instrument.FullName == instrument.FullName)
								.Sum(p => (long)p.Quantity * (p.MarketPosition == MarketPosition.Long ? 1 : p.MarketPosition == MarketPosition.Short ? -1 : 0));
							Order[] active = orders.Where(o => o.Instrument.FullName == instrument.FullName && !Terminal(o)).ToArray();
							long foreignNet = net;
							if (ReferenceEquals(account, ownAccount) && instrument.FullName == ownInstrument.FullName && ownNet != 0)
							{
								if (Math.Sign(net) != Math.Sign(ownNet) || Math.Abs(net) < Math.Abs(ownNet))
								{
									reason = "Owned/account quantity disagreement; entry blocked";
									return false;
								}
								foreignNet -= ownNet;
							}
							foreach (int wantedSide in both ? new[] { 1, -1 } : new[] { side })
							{
								if (instrument.MasterInstrument.Name == "FVS" && wantedSide < 0) continue;
								int wanted = instrument.MasterInstrument.Name == "FVS" ? -1 : wantedSide;
								if (Opposes(active, net, wanted, managedExits, out reason, foreignNet))
								{
									reason += ": " + account.Name + " / " + instrument.FullName;
									return false;
								}
							}
						}
					}
				}
				catch (Exception) { reason = "Cannot verify account product exposure; entry blocked"; return false; }
				// BOTH is published once, under this same lock: no one-sided OCO reservation window.
				int reserved = ownNet != 0 && ownNet * side < 0 ? 0 : side;
				if (index) directions[ownerId] = reserved; else directions.Remove(ownerId);
				exact[ownerId] = Tuple.Create(ownAccount, ownInstrument.FullName, reserved);
				reason = null;
				return true;
			}
		}

		private static bool Terminal(Order order)
		{
			return order.OrderState == OrderState.Filled || order.OrderState == OrderState.Cancelled || order.OrderState == OrderState.Rejected;
		}

		private static bool ManagedExit(Order order, Execution[] executions)
		{
			if (string.IsNullOrEmpty(order.FromEntrySignal)) return false;
			return executions.Any(e => e.Order != null && e.Quantity > 0 && ReferenceEquals(e.Order.Account, order.Account) &&
				e.Order.Instrument.FullName == order.Instrument.FullName && e.Order.Name == order.FromEntrySignal &&
				((order.OrderAction == OrderAction.Sell && e.Order.OrderAction == OrderAction.Buy) ||
				 ((order.OrderAction == OrderAction.Buy || order.OrderAction == OrderAction.BuyToCover) && e.Order.OrderAction == OrderAction.SellShort)));
		}

		internal static bool Opposes(Order[] orders, long net, int wanted, HashSet<Order> managedExits, out string reason, long? directionalNet = null)
		{
			if ((directionalNet ?? net) * wanted < 0) { reason = "Opposing account position; entry blocked"; return true; }
			var protective = new List<Order>();
			foreach (Order order in orders)
			{
				int direction = order.OrderAction == OrderAction.Buy || order.OrderAction == OrderAction.BuyToCover ? 1 : -1;
				var exitsProof = AppDomain.CurrentDomain.GetData(ExitSlot) as HashSet<Order>;
				bool exit = managedExits.Contains(order) || (exitsProof != null && exitsProof.Contains(order));
				if (!exit && order.OrderAction == OrderAction.BuyToCover)
				{
					reason = "Unverified BuyToCover order; entry blocked (" + order.Name + ")";
					return true;
				}
				if (exit)
				{
					if (net == 0 || Math.Sign(net) == direction) { reason = "Uncovered protective order; entry blocked (" + order.Name + ")"; return true; }
					protective.Add(order);
				}
				else if (direction != wanted) { reason = "Opposing or unverified order; entry blocked (" + order.Name + ")"; return true; }
			}
			long exits = protective.GroupBy(o => string.IsNullOrEmpty(o.Oco) ? (object)o : o.Oco)
				.Sum(g => g.Max(o => (long)Math.Max(0, o.Quantity - o.Filled)));
			if (exits > Math.Abs(net)) { reason = "Uncovered protective orders; entry blocked"; return true; }
			reason = null;
			return false;
		}

		internal static void Release(string ownerId)
		{
			lock (AppDomain.CurrentDomain)
			{
				if (ownerId == null) return;
				var directions = AppDomain.CurrentDomain.GetData(Slot) as Dictionary<string, int>;
				var exact = AppDomain.CurrentDomain.GetData(ExactSlot) as Dictionary<string, Tuple<Account, string, int>>;
				if (directions != null) directions.Remove(ownerId);
				if (exact != null) exact.Remove(ownerId);
			}
		}
	}


	internal sealed class Candle
	{
		public DateTime Start;
		public DateTime Last;
		public double Open;
		public double High;
		public double Low;
		public double Close;
		public double Volume;
		public Candle(DateTime t, double o, double h, double l, double c, double v = 0.0)
		{
			Start = Last = t;
			Open = o; High = h; Low = l; Close = c; Volume = Math.Max(0.0, v);
		}
		public Candle Clone()
		{
			return new Candle(Start, Open, High, Low, Close, Volume) { Last = Last };
		}
		public void Merge(Candle b)
		{
			High = Math.Max(High, b.High);
			Low = Math.Min(Low, b.Low);
			Close = b.Close;
			Volume += Math.Max(0.0, b.Volume);
			Last = b.Last;
		}
	}


	private sealed class Bracket
	{
		public string StopName;

		public string TargetName;

		public Order Stop;

		public Order Target;
	}

	internal sealed class WilderAdx
	{
		private Candle previous;

		private int index = -1;

		private double tr;

		private double plus;

		private double minus;

		public double Value { get; private set; }

		public bool Ready => index >= 30;

		public void Reset()
		{
			previous = null;
			index = -1;
			double num = (Value = 0.0);
			tr = (plus = (minus = num));
		}

		public void Push(Candle bar)
		{
			index++;
			if (previous == null)
			{
				previous = bar.Clone();
				return;
			}
			double num = Math.Max(0.0, bar.High - previous.High);
			double num2 = Math.Max(0.0, previous.Low - bar.Low);
			if (num == num2)
			{
				num = (num2 = 0.0);
			}
			else if (num < num2)
			{
				num = 0.0;
			}
			else
			{
				num2 = 0.0;
			}
			double num3 = Math.Max(bar.High - bar.Low, Math.Max(Math.Abs(bar.High - previous.Close), Math.Abs(bar.Low - previous.Close)));
			if (index >= 15)
			{
				tr = (tr * 14.0 + num3) / 15.0;
				plus = (plus * 14.0 + num) / 15.0;
				minus = (minus * 14.0 + num2) / 15.0;
			}
			double num4 = ((plus + minus > 0.0) ? (100.0 * Math.Abs(plus - minus) / (plus + minus)) : 0.0);
			if (index >= 14)
			{
				Value = (Value * 14.0 + num4) / 15.0;
			}
			previous = bar.Clone();
		}
	}

	private static readonly object ownerLock = new object();

	private static readonly HashSet<string> activeOwners = new HashSet<string>();

	private string ownerKey;

	private string exposureKey;

	private bool ownsGuard;

	private string legacyExternalCloseConfirmation = string.Empty;

	private SortedDictionary<DateTime, Candle> minutes;

	private Dictionary<string, Order> orders;

	private Dictionary<string, int> processed;

	private HashSet<string> executions;

	private List<Bracket> brackets;

	private Queue<double> trueRanges;

	private Queue<double> dailyATRs;

	private WilderAdx adx;


	private TimeZoneInfo platformZone;

	private TimeZoneInfo easternZone;

	private TimeZoneInfo nyZone;

	private System.Timers.Timer timer;

	private StreamWriter log;

	private DateTime lastMinute;

	private DateTime latestNewYork;

	private DateTime sessionDay;

	private DateTime startupFloor;

	private DateTime lastExecutionMinute;

	private int consumedSourceIndex = -1;

	private BarsRequest historyRepair;

	private readonly SortedDictionary<DateTime, Candle> providerBars = new SortedDictionary<DateTime, Candle>();

	private DateTime repairRetry;

	private DateTime recoveryFloor;

	private bool sourceFresh = true;

	private int executionSeries = 1;

	private int signalSeries = 1;

	private string signalContract;

	private DateTime dailyKey;

	private DateTime bucketKey;

	private Candle daily;

	private Candle bucket;

	private int direction;

	private int entryCount;

	private const int MaxAttempts = 2;

	private const double RetryStopFraction = 0.5;

	private int entryDirection;

	private bool retryArmed;

	private DateTime retryEligibleAt;

	private bool targetFilled;

	private int ownedNet;

	private int entered;

	private int exited;

	private int sequence;

	private bool bucketFinalized;

	private double previousDailyClose = double.NaN;

	private double averageATR;

	private double nightHigh;

	private double nightLow;

	private double rangeHigh;

	private double rangeLow;

	private double plannedStop;

	private double plannedTarget;

	private double entrySum;

	private double exitSum;

	private double initialRisk;

	private double firstStopDistance;

	private bool stopExitFilled;

	private double peakR;

	private double actualFees;

	private double tradeModeledCost;

	private double recoveredCommissionFloor;

	private bool validDay;

	private bool readyRange;

	private bool suspended;

	private bool closing;

	private bool closingDrive;

	private bool entryPending;

	private bool startupArmed;

	private bool closeRejected;

	private string pendingCloseName;

	private int pendingCloseQuantity;

	private int pendingCloseExecuted;

	private int pendingCloseFinal = -1;

	private string owner;

	private string entryName;

	private string closeReason;

	private string status = "Loading completed M1 history";

	private bool nativeDisplayQueued;

	private bool historicalVisualsPainted;

	private readonly Dictionary<IDrawingTool, bool> hiddenVisuals = new Dictionary<IDrawingTool, bool>();

	private bool visualSyncQueued;

	private bool visualHistoryWaiting;

	private ExecutionView executionVisual;

	private readonly List<ExecutionView> closedVisuals = new List<ExecutionView>();

	private readonly ModelDashboard dashboard = new ModelDashboard();

	private ChartControl dashboardChart;

	private bool dashboardMouseHooked;

	private bool dashboardFaultLogged;

	private bool dashboardLayered;

	private int dashboardRedrawQueued;

	private readonly Dictionary<string, RecoverySnapshot> recoverySnapshots = new Dictionary<string, RecoverySnapshot>();

	private readonly Dictionary<string, RecoveryRequest> recoveryRequests = new Dictionary<string, RecoveryRequest>();

	private readonly HashSet<string> recoveredOrderNames = new HashSet<string>();

	private string recoveryPath;

	private string recoveryIdentity;

	private bool recoveryPending;

	private bool recoveredTrade;

	private bool reconcilingRecovery;

	private bool recoveryFault;

	private bool recoveryReady;

	private Account recoveryAccount;

	private bool repairingProtection;

	private string applyingRecoveryExecution;

	private readonly Dictionary<string, int> reportedFilled = new Dictionary<string, int>();

	private FileStream recoveryLock;

	private string externalRecoveryCaseReported;

	private string externalRecoveryRefusalReported;

	private bool connectionPaused;

	private DateTime recoveryNextPollUtc;

	private string lastBiasAudit;

	private static readonly object FeeGate = new object();

	private static readonly Dictionary<string, List<FeeTrade>> FeeHistory = new Dictionary<string, List<FeeTrade>>();

	private FeeTrade feeTrade;

	// ---- Model state (times are internally New York wall clock; UTC conversions are explicit) ----
	private string instanceId;
	private string modelIdentity;
	private string strategyPrefix;
	private int sourceBucketCount;
	private DateTime sourceBucketKeyUtc = DateTime.MinValue;
	private DateTime sourceBucketKeyEt = DateTime.MinValue;
	private Candle sourceBucket;
	private double sourceBucketPv;
	private double sourceBucketVolume;
	private DateTime lastModelBarEnd = DateTime.MinValue;
	private int currentTradeSide;
	private double lastSignalClose;
	private double riskBudgetSelected;
	private bool budgetFilteredThisEntry;
	private int skippedByRiskFilter;
	private double entryQuoteAtSignal;

	// Sigma-specific state. Histories are per exact M30 clock slot and contain prior sessions only.
	private readonly Dictionary<int, Queue<double>> sigmaSlotHistory = new Dictionary<int, Queue<double>>();
	private double rthOpen;
	private bool haveRthOpen;
	private double sigmaUpper;
	private double sigmaLower;
	private double sigmaValue;
	private double sigmaReference;
	private double sessionPv;
	private double sessionVolume;
	private double previousCompletedVwap;
	private bool havePreviousVwap;
	// private int sigmaSignalsToday;
	private int pendingSigmaFlipSide;
	private double pendingSigmaFlipSignalClose;
	private DateTime pendingSigmaFlipEndEt = DateTime.MinValue;



	// Shared visual/panel strings.
	private string modelHistoryStatus = "Waiting for complete M1 history";

	[NinjaScriptProperty]
	[Display(Name = "Enable new trades", GroupName = "01 Trading", Order = 0)]
	public bool EnableTrading { get; set; }

	[NinjaScriptProperty]
	[Display(Name = "Instance ID", Description = "Unique within the same strategy/account/instrument. Distinct strategies may run on the same instrument.", GroupName = "01 Trading", Order = 2)]
	public string InstanceId { get; set; }

	[NinjaScriptProperty]
	[Range(1.0, 100000000.0)]
	[Display(Name = "Analyzer initial sizing equity ($)", Description = "Used only when NT8 account equity is unavailable in Strategy Analyzer.", GroupName = "03 Risk", Order = 9)]
	public double InitialSizingEquity { get; set; }

	[NinjaScriptProperty]
	[Range(2, 20)]
	[Display(Name = "N Sigma Days", GroupName = "02 Sigma", Order = 0)]
	public int NSigmaDays { get; set; }

	[NinjaScriptProperty]
	[Range(0.1, 5.0)]
	[Display(Name = "Band Multiplier", GroupName = "02 Sigma", Order = 1)]
	public double BandMultiplier { get; set; }

	[NinjaScriptProperty]
	[Display(Name = "Use prior completed M30 VWAP as reference", Description = "Off = RTH open. Active candidate default is OFF.", GroupName = "02 Sigma", Order = 2)]
	public bool UseVwapReference { get; set; }

	[NinjaScriptProperty]
	[Range(0.0, 20.0)]
	[Display(Name = "Stop Loss % (0 = off)", Description = "Active N3/1.7 candidate defaults to no protective stop.", GroupName = "03 Risk", Order = 0)]
	public double SlPct { get; set; }

	[NinjaScriptProperty]
	[Range(1, 100)]
	[Display(Name = "Fixed Lot (used when Stop Loss % = 0)", GroupName = "03 Risk", Order = 1)]
	public int FixedLot { get; set; }

	[NinjaScriptProperty]
	[Range(0.1, 20.0)]
	[Display(Name = "Risk % of Net Liquidation", GroupName = "03 Risk", Order = 2)]
	public double RiskPct { get; set; }

	[NinjaScriptProperty]
	[Range(0.0, 1000000.0)]
	[Display(Name = "Fixed Risk $ per Trade (0 = use Risk %)", GroupName = "03 Risk", Order = 3)]
	public double FixedRiskUsd { get; set; }

	[NinjaScriptProperty]
	[Display(Name = "Allow minimum one contract in variable sizing", Description = "When ON, percentage sizing rounding below one still permits one MNQ/NQ contract. The optional dollar-risk filter can reject it.", GroupName = "03 Risk", Order = 4)]
	public bool AllowMinimumOneContract { get; set; }

	[NinjaScriptProperty]
	[Display(Name = "Skip setups above fixed $ risk budget", Description = "Optional; default OFF. Applies only when SlPct > 0 and quantity is variable. SlPct=0 fixed-lot sizing bypasses the filter.", GroupName = "03 Risk", Order = 5)]
	public bool SkipSetupsAboveRiskBudget { get; set; }

	[NinjaScriptProperty]
	[Range(0.0, 1000000.0)]
	[Display(Name = "Fixed Risk Budget ($)", Description = "Planned initial stop risk plus cost reserve. Inert unless the optional filter is enabled in variable sizing.", GroupName = "03 Risk", Order = 6)]
	public double RiskBudgetUsd { get; set; }


	[Browsable(false)]
	[XmlIgnore]
	private string ExternalCloseConfirmation
	{
		get
		{
			return legacyExternalCloseConfirmation ?? string.Empty;
		}
		set
		{
			legacyExternalCloseConfirmation = value ?? string.Empty;
		}
	}




	[NinjaScriptProperty]
	[Range(0.0, double.MaxValue)]
	[Display(Name = "Round-trip commission fallback / contract ($)", Description = "Used until a completed live trade supplies nonzero NT execution charges. These may come from your commission template.", GroupName = "03 Costs", Order = 0)]
	public double CommissionPerContract { get; set; }

	[NinjaScriptProperty]
	[Range(0, int.MaxValue)]
	[Display(Name = "Slippage/side (x0.01; 3=0.03)", Description = "Cost reserve in 0.01 price units: 3=0.03, 25=0.25 (one NQ/MNQ tick), 100=1.00 price. This does not change native historical fill slippage, which NT8 specifies separately in ticks.", GroupName = "03 Costs", Order = 1)]
	public int SlippagePoints { get; set; }

	[NinjaScriptProperty]
	[Display(Name = "Show levels, trade markers and information", GroupName = "04 Display", Order = 0)]
	public bool ShowVisuals { get; set; }

	[NinjaScriptProperty]
	[Display(Name = "Write detailed decision and trade logs", GroupName = "05 Logging", Order = 0)]
	public bool WriteTradeLog { get; set; }

	[NinjaScriptProperty]
	[Display(Name = "Block opposing index exposure", Description = "Reject opposing index positions or pending entries across locally visible NT8 accounts. Exact-instrument ownership isolation remains active.", GroupName = "01 Trading", Order = 1)]
	public bool BlockOpposingExposure { get; set; }

	private bool VisualsSuppressed => VisualFaultReason() != null;

	private string FeeScope => ((Account == null) ? "unknown" : Account.Name) + "|" + ExecutionInstrument.FullName;

	private bool CanLearnFee
	{
		get
		{
			if (State != State.Realtime || Account == null || Connection.PlaybackConnection != null)
			{
				return false;
			}
			string name = Account.Name ?? "";
			return name.Length > 0 && !new string[4] { "Sim", "Playback", "Replay", "Backtest" }.Any((string prefix) => name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
		}
	}

	private double EffectiveCommission
	{
		get
		{
			if (!CanLearnFee)
			{
				return CommissionPerContract;
			}
			lock (FeeGate)
			{
				if (FeeHistory.TryGetValue(FeeScope, out var value))
				{
					for (int num = value.Count - 1; num >= 0; num--)
					{
						double roundTurn = value[num].RoundTurn;
						if (roundTurn > 0.0)
						{
							return roundTurn;
						}
					}
				}
				return CommissionPerContract;
			}
		}
	}

	private double SlippagePrice => (double)SlippagePoints * 0.01;

	// Cost-covered stop floors reserve at least one native tick of slippage per side (live: exit side only, entry slippage is in the fill; historical: both sides).
	private double CoverSlippagePerSide => Math.Max(SlippagePrice, ExecutionInstrument.MasterInstrument.TickSize);

	// Tradeify published round-turn all-in fees (NQ $5.76, MNQ $1.82). Lower bound only: never reduces a larger input or learned live fee. NNQ has no published Tradeify fee, so it uses the input/learned value only.
	private static double PublishedRoundTurn(string root)
	{
		switch ((root ?? "").Trim().ToUpperInvariant())
		{
		case "NQ":
			return 5.76;
		case "MNQ":
			return 1.82;
		default:
			return 0.0;
		}
	}

	private double CoverCommission(double commission)
	{
		return Math.Max(commission, PublishedRoundTurn(ExecutionInstrument.MasterInstrument.Name));
	}

	private double CostPerContract => EffectiveCommission + 2.0 * SlippagePrice * ExecutionInstrument.MasterInstrument.PointValue;

	private Instrument ExecutionInstrument => BarsArray[0].Instrument;


	private string VisualFaultReason()
	{
		if (State != State.Realtime) return null;
		if (Account == null || Account.ConnectionStatus != ConnectionStatus.Connected) return "Account disconnected; orders paused";
		if (recoveryFault) return status;
		if (recoveryPending || reconcilingRecovery) return "Reconciling owned broker orders; trading paused";
		if (suspended || closeRejected) return status;
		if (CurrentBars == null || signalSeries >= CurrentBars.Length || executionSeries >= CurrentBars.Length || CurrentBars[signalSeries] < 0 || CurrentBars[executionSeries] < 0)
			return "Required M1 history unavailable; trading paused";
		if (visualHistoryWaiting || historyRepair != null) return status;
		if (startupArmed && !sourceFresh) return "Waiting for a fresh completed model signal";
		return null;
	}

	private void SyncFaultVisuals()
	{
		ChartControl chart = ChartControl;
		if (chart == null || visualSyncQueued)
		{
			return;
		}
		visualSyncQueued = true;
		Action action = () =>
		{
			visualSyncQueued = false;
			if (State != State.Terminated)
			{
				bool visualsSuppressed = VisualsSuppressed;
				if (visualsSuppressed)
				{
					IDrawingTool[] array = DrawObjects.ToArray();
					foreach (IDrawingTool drawingTool in array)
					{
						if (drawingTool != null && drawingTool.DrawnBy == this)
						{
							if (!hiddenVisuals.ContainsKey(drawingTool))
							{
								hiddenVisuals[drawingTool] = drawingTool.IsVisible;
							}
							drawingTool.IsVisible = false;
						}
					}
				}
				else
				{
					foreach (KeyValuePair<IDrawingTool, bool> hiddenVisual in hiddenVisuals)
					{
						hiddenVisual.Key.IsVisible = hiddenVisual.Value;
					}
					hiddenVisuals.Clear();
				}
				chart.InvalidateVisual();
			}
		};
		if (chart.Dispatcher.CheckAccess())
		{
			action();
		}
		else
		{
			chart.Dispatcher.BeginInvoke(DispatcherPriority.Background, action);
		}
	}

	private void EnableNativeExecutionDisplay()
	{
		ChartControl chart = ChartControl;
		ChartBars bars = ChartBars;
		if (nativeDisplayQueued || chart == null || bars == null)
		{
			return;
		}
		nativeDisplayQueued = true;
		chart.Dispatcher.InvokeAsync(() =>
		{
			if (State == State.Terminated)
			{
				return;
			}
			try
			{
				bars.Properties.PlotExecutions = ChartExecutionStyle.MarkersOnly;
				bars.Properties.ShowTradeLines = true;
				chart.InvalidateVisual();
			}
			catch (Exception ex)
			{
				nativeDisplayQueued = false;
				Print("[Execution visuals] Native execution display unavailable: " + ex.Message);
			}
		});
	}


	private void DrawExecution(ExecutionView trade, DateTime now)
	{
		if (!ShowVisuals || trade == null || trade.Entry <= 0) return;
		bool closed = trade.Exited != DateTime.MinValue;
		DateTime right = closed ? trade.Exited : now;
		if (trade.InitialStop > 0)
		{
			double pnlMove = trade.Side * (trade.Exit - trade.Entry);
			double stop = (closed && pnlMove <= 0) ? trade.Exit : trade.InitialStop;
			double target = (closed && pnlMove > 0) ? trade.Exit : trade.InitialTarget;
			StrategyExecutionVisual.DrawExecutionTool(this, trade.Tag, trade.Side > 0, trade.Entry, stop, target, trade.Entry,
				Platform(trade.Entered), Platform(trade.Entered), Platform(right), trade.Trailing, trade.TrailStart, synthetic: false);
			if (trade.Trailing && trade.TrailStart != DateTime.MinValue)
				StrategyExecutionVisual.DrawBoundedLine(this, trade.Tag + "_TRAIL", Platform(trade.TrailStart), trade.Stop, Platform(right), trade.Stop, StrategyExecutionVisual.ClrTrailingStop, DashStyleHelper.Dot, 2);
		}
		else
		{
			StrategyExecutionVisual.DrawBoundedLine(this, trade.Tag + "_ENT", Platform(trade.Entered), trade.Entry, Platform(right), trade.Entry, StrategyExecutionVisual.ClrEntryLine, DashStyleHelper.Solid, 2);
			if (closed && trade.Exit > 0)
				Draw.Rectangle(this, trade.Tag + "_TRADE", false, Platform(trade.Entered), trade.Entry, Platform(right), trade.Exit, Brushes.Transparent,
					(trade.Side * (trade.Exit - trade.Entry) >= 0 ? StrategyExecutionVisual.ClrTPBox : StrategyExecutionVisual.ClrSLBox), 25);
		}
	}


	private void PaintHistoricalExecutions(DateTime now)
	{
		if (ShowVisuals) EnableNativeExecutionDisplay();
		if (!ShowVisuals) return;
		if (!historicalVisualsPainted && State != State.Historical && ChartControl != null)
		{
			HashSet<string> tags = new HashSet<string>(DrawObjects.Where(x => x != null).Select(x => x.Tag));
			historicalVisualsPainted = true;
			foreach (ExecutionView trade in closedVisuals)
			{
				string marker = trade.InitialStop > 0 ? trade.Tag + "_SL" : trade.Tag + "_TRADE";
				if (!tags.Contains(marker)) { DrawExecution(trade, trade.Exited); historicalVisualsPainted = false; }
			}
		}
		if (executionVisual == null) return;
		Order stop = brackets.Where(b => b.Stop != null).Select(b => b.Stop).FirstOrDefault();
		if (stop != null && Math.Abs(stop.StopPrice - executionVisual.InitialStop) > TickSize / 2.0)
		{
			if (!executionVisual.Trailing) executionVisual.TrailStart = now;
			executionVisual.Trailing = true;
			executionVisual.Stop = stop.StopPrice;
		}
		DrawExecution(executionVisual, now);
	}


	protected override void OnStateChange()
	{
		if (State == State.SetDefaults)
		{
			Name = "SigmaBreakoutNT8";
			Description = @"Sigma Breakout N3 / 1.7. Completed M30 slot-specific prior-session sigma bands; M1 execution; RTH open reference; Tradeify flat at 15:44 ET.";
			Calculate = Calculate.OnEachTick;
			IsUnmanaged = true;
			StartBehavior = StartBehavior.ImmediatelySubmit;
			IsExitOnSessionCloseStrategy = false;
			RealtimeErrorHandling = RealtimeErrorHandling.IgnoreAllErrors;
			ConnectionLossHandling = ConnectionLossHandling.KeepRunning;
			BarsRequiredToTrade = 0;
			Slippage = 0;
			IsInstantiatedOnEachOptimizationIteration = true;
			MaximumBarsLookBack = MaximumBarsLookBack.Infinite;
			OrderFillResolution = OrderFillResolution.Standard;
			IsFillLimitOnTouch = true;
			StopTargetHandling = StopTargetHandling.PerEntryExecution;
			TimeInForce = TimeInForce.Gtc;
			IsOverlay = true;
			DrawOnPricePanel = true;
			IncludeTradeHistoryInBacktest = true;

				EnableTrading = true;
				InstanceId = "default";
				BlockOpposingExposure = false;
				NSigmaDays = 3;
				BandMultiplier = 1.7;
				UseVwapReference = false;
				SlPct = 0.0;
				FixedLot = 1;
				RiskPct = 1.0;
				FixedRiskUsd = 0.0;
				AllowMinimumOneContract = true;
				SkipSetupsAboveRiskBudget = false;
				RiskBudgetUsd = 250.0;
				InitialSizingEquity = 10000.0;
				CommissionPerContract = 0.50;
				SlippagePoints = 0;
				ShowVisuals = true;
				WriteTradeLog = false;

		}
		else if (State == State.Configure)
		{
			if (StartBehavior != StartBehavior.ImmediatelySubmit)
				throw new InvalidOperationException("ImmediatelySubmit is mandatory for independent owned-order recovery.");
			if (string.IsNullOrWhiteSpace(InstanceId) || InstanceId.Length > 16 || InstanceId.Any(c => !(char.IsLetterOrDigit(c) || c == '_' || c == '-')))
				throw new InvalidOperationException("Instance ID must be 1-16 letters, digits, '_' or '-'.");
			if (CommissionPerContract < 0 || SlippagePoints < 0)
				throw new InvalidOperationException("Invalid quantity or cost input.");
			if (NSigmaDays < 2 || BandMultiplier <= 0 || SlPct < 0 || FixedLot < 1 || RiskPct <= 0 || FixedRiskUsd < 0 || !FinitePositive(InitialSizingEquity))
				throw new InvalidOperationException("Invalid Sigma parameter.");
			if (SlPct > 0 && SkipSetupsAboveRiskBudget && (!FinitePositive(RiskBudgetUsd)))
				throw new InvalidOperationException("Enabled variable-sizing risk filter requires a positive finite dollar budget.");
			IncludeTradeHistoryInBacktest = true;
			signalContract = SourceContract(Instrument);
			executionSeries = (signalSeries = 1);
			AddDataSeries(Instrument.FullName, new BarsPeriod { BarsPeriodType = BarsPeriodType.Minute, Value = 1 }, "CME US Index Futures ETH", true);
		}
		else if (State == State.DataLoaded)
		{
			minutes = new SortedDictionary<DateTime, Candle>();
			orders = new Dictionary<string, Order>();
			processed = new Dictionary<string, int>();
			executions = new HashSet<string>();
			brackets = new List<Bracket>();
			trueRanges = new Queue<double>();
			dailyATRs = new Queue<double>();
			adx = new WilderAdx();
			platformZone = Globals.GeneralOptions.TimeZoneInfo;
			easternZone = TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");
			nyZone = easternZone;
			owner = StrategyPrefix() + "_" + Guid.NewGuid().ToString("N").Substring(0, 8);
			PrepareRecovery();
			string instrumentName = ExecutionInstrument.MasterInstrument.Name.ToUpperInvariant();
			if (instrumentName != "NQ" && instrumentName != "MNQ" && instrumentName != "NNQ")
				throw new InvalidOperationException("Use NQ/MNQ/NNQ; selected " + ExecutionInstrument.FullName + ".");
			if (ExecutionInstrument.MasterInstrument.PointValue <= 0 || ExecutionInstrument.MasterInstrument.TickSize <= 0)
				throw new InvalidOperationException("Invalid contract specification.");
			if (WriteTradeLog)
			{
				string folder = Path.Combine(Globals.UserDataDir, StrategyFolder(), "logs");
				Directory.CreateDirectory(folder);
				log = new StreamWriter(Path.Combine(folder, owner + ".csv")) { AutoFlush = true };
				log.WriteLine("event,ny_time,detail");
			}
		}
		else if (State == State.Transition)
		{
			if (ownedNet != 0 || entryPending || orders.Values.Any(o => !Settled(o) || !ExecutionsCaughtUp(o)))
				throw new InvalidOperationException("Historical owned exposure must be flat before realtime.");
		}
		else if (State == State.Realtime)
		{
			startupArmed = false;
			ownerKey = ((Account == null) ? "" : Account.Name) + "|" + ExecutionInstrument.FullName + "|" + Name + "|" + InstanceId;
			exposureKey = ((Account == null) ? "unknown" : Account.Name) + "|" + ExecutionInstrument.FullName + "|" + Name + "|" + InstanceId;
			lock (ownerLock) ownsGuard = activeOwners.Add(ownerKey);
			if (!ownsGuard) { suspended = true; status = "Duplicate Instance ID already active for this account/instrument"; }
			startupFloor = NewYork(NativeNow());
			StartRecovery();
			if (ownedNet != 0) currentTradeSide = Math.Sign(ownedNet);
			// Do not blanket-block an account position here. ProductExposure below
			// attributes owned orders and permits same-direction independent systems.
			timer = new System.Timers.Timer(1000.0) { AutoReset = true };
			timer.Elapsed += (s, e) => { try { TriggerCustomEvent(o => TimerStep(), null); } catch { } };
			timer.Start();
		}
		else if (State == State.Terminated)
		{
			StopRecovery();
			HookDashboardMouse(attach: false);
			if (historyRepair != null) { historyRepair.Dispose(); historyRepair = null; }
			if (ownedNet == 0 && !entryPending && (orders == null || orders.Values.All(o => Settled(o) && ExecutionsCaughtUp(o)))) ReleaseExposure();
			if (ownsGuard) { lock (ownerLock) activeOwners.Remove(ownerKey); ownsGuard = false; }
			if (timer != null) { timer.Stop(); timer.Dispose(); timer = null; }
			if (log != null) { log.Dispose(); log = null; }
		}
	}

	internal static bool NativeHistorySufficient(List<DateTime> starts, int atrPeriod, int meanPeriod, int windowStart, int windowEnd, out string reason, Func<DateTime, int> requiredEnd = null)
	{
		if (starts.Count == 0)
		{
			reason = "NNQ M1 history unavailable";
			return false;
		}
		DateTime[] array = (from t in starts.Select((DateTime t) => (t.TimeOfDay.TotalMinutes >= 960.0) ? t.Date.AddDays(1.0) : t.Date).Distinct()
			orderby t
			select t).ToArray();
		int num = atrPeriod + meanPeriod + 1;
		if (array.Length < num)
		{
			reason = "NNQ ATR history: " + array.Length + "/" + num + " daily buckets";
			return false;
		}
		DateTime readyFrom = array[num - 1];
		HashSet<DateTime> hashSet = new HashSet<DateTime>(starts);
		DateTime dateTime = starts[starts.Count - 1].AddMinutes(1.0);
		DateTime[] array2 = (from d in (from t in starts
				select (windowStart < 0 && t.TimeOfDay.TotalMinutes >= (double)(1440 + windowStart)) ? t.Date.AddDays(1.0) : t.Date into d
				where d >= readyFrom && d.DayOfWeek != DayOfWeek.Saturday && d.DayOfWeek != DayOfWeek.Sunday
				select d).Distinct()
			orderby d
			select d).ToArray();
		bool flag = false;
		DateTime[] array3 = array2;
		for (int num2 = 0; num2 < array3.Length; num2++)
		{
			DateTime arg = array3[num2];
			DateTime dateTime2 = arg.AddMinutes(windowStart);
			DateTime dateTime3 = arg.AddMinutes((requiredEnd == null) ? windowEnd : Math.Min(windowEnd, requiredEnd(arg)));
			if (dateTime3 > dateTime)
			{
				dateTime3 = dateTime;
			}
			if (dateTime3 <= dateTime2)
			{
				continue;
			}
			flag = true;
			DateTime dateTime4 = dateTime2;
			while (dateTime4 < dateTime3)
			{
				if (!hashSet.Contains(dateTime4))
				{
					reason = "NNQ missing required M1 at " + dateTime4.ToString("yyyy-MM-dd HH:mm") + " Central";
					return false;
				}
				dateTime4 = dateTime4.AddMinutes(1.0);
			}
		}
		reason = (flag ? null : "NNQ required session history unavailable");
		return flag;
	}

	internal static string SourceContract(Instrument instrument)
	{
		string text = instrument.MasterInstrument.Name.Trim().ToUpperInvariant();
		if (text != "NQ" && text != "MNQ" && text != "NNQ")
		{
			throw new InvalidOperationException("Use NQ, MNQ or NNQ; selected " + instrument.FullName + ".");
		}
		return (text == "NNQ") ? ("NQ" + instrument.FullName.Substring(instrument.MasterInstrument.Name.Length)) : instrument.FullName;
	}

	private DateTime NewYork(DateTime platform)
	{
		return TimeZoneInfo.ConvertTime(DateTime.SpecifyKind(platform, DateTimeKind.Unspecified), platformZone, easternZone);
	}

	private DateTime Platform(DateTime eastern)
	{
		return TimeZoneInfo.ConvertTime(DateTime.SpecifyKind(eastern, DateTimeKind.Unspecified), easternZone, platformZone);
	}

	private static DateTime NativeNow()
	{
		return (Connection.PlaybackConnection != null) ? Connection.PlaybackConnection.Now : Globals.Now;
	}

	protected override void OnRender(ChartControl chartControl, ChartScale chartScale)
	{
		if (!VisualsSuppressed)
		{
			base.OnRender(chartControl, chartScale);
		}
		if (ShowVisuals)
		{
			EnableNativeExecutionDisplay();
		}
		if (!ShowVisuals || dashboard == null || chartControl == null)
		{
			return;
		}
		try
		{
			try
			{
				if (!dashboardLayered)
				{
					SetZOrder(int.MaxValue);
					dashboardLayered = true;
				}
			}
			catch
			{
			}
			HookDashboardMouse(attach: true);
			dashboard.Render(RenderTarget, chartControl);
		}
		catch (Exception ex)
		{
			if (!dashboardFaultLogged)
			{
				dashboardFaultLogged = true;
				Print("[Dashboard] Dashboard render unavailable: " + ex.Message);
			}
		}
	}

	private void RequestDashboardRender()
	{
		ChartControl chart = ChartControl;
		if (!ShowVisuals || chart == null || chart.Dispatcher.HasShutdownStarted || chart.Dispatcher.HasShutdownFinished || Interlocked.CompareExchange(ref dashboardRedrawQueued, 1, 0) != 0)
		{
			return;
		}
		try
		{
			chart.Dispatcher.InvokeAsync(() =>
			{
				Interlocked.Exchange(ref dashboardRedrawQueued, 0);
				if (State != State.Terminated && ShowVisuals && chart == ChartControl)
				{
					chart.InvalidateVisual();
				}
			}, DispatcherPriority.Background);
		}
		catch (InvalidOperationException)
		{
			Interlocked.Exchange(ref dashboardRedrawQueued, 0);
		}
	}

	private void HookDashboardMouse(bool attach)
	{
		if (!attach)
		{
			dashboardLayered = false;
			dashboard.MouseUp();
		}
		ChartControl chart = (attach ? ChartControl : dashboardChart);
		if (chart == null || dashboardMouseHooked == attach)
		{
			return;
		}
		dashboardMouseHooked = attach;
		dashboardChart = (attach ? chart : null);
		chart.Dispatcher.InvokeAsync(() =>
		{
			if (attach && State != State.Terminated)
			{
				chart.PreviewMouseLeftButtonDown += DashboardMouseDown;
				chart.PreviewMouseMove += DashboardMouseMove;
				chart.PreviewMouseLeftButtonUp += DashboardMouseUp;
			}
			else
			{
				chart.PreviewMouseLeftButtonDown -= DashboardMouseDown;
				chart.PreviewMouseMove -= DashboardMouseMove;
				chart.PreviewMouseLeftButtonUp -= DashboardMouseUp;
			}
		});
	}

	private System.Windows.Point DashboardPoint(ChartControl chart, System.Windows.Point point)
	{
		return PresentationSource.FromVisual(chart)?.CompositionTarget.TransformToDevice.Transform(point) ?? point;
	}

	private void DashboardMouseDown(object sender, MouseButtonEventArgs e)
	{
		try
		{
			ChartControl chartControl = ChartControl;
			if (ShowVisuals && State != State.Terminated && chartControl != null && dashboard != null)
			{
				System.Windows.Point point = DashboardPoint(chartControl, e.GetPosition(chartControl));
				if (dashboard.MouseDown((float)point.X, (float)point.Y))
				{
					e.Handled = true;
					chartControl.InvalidateVisual();
				}
			}
		}
		catch (Exception)
		{
		}
	}

	private void DashboardMouseMove(object sender, MouseEventArgs e)
	{
		try
		{
			ChartControl chartControl = ChartControl;
			if (chartControl == null || dashboard == null)
			{
				return;
			}
			if (!ShowVisuals || State == State.Terminated || e.LeftButton != MouseButtonState.Pressed)
			{
				dashboard.MouseUp();
				return;
			}
			System.Windows.Point point = DashboardPoint(chartControl, e.GetPosition(chartControl));
			if (dashboard.MouseMove((float)point.X, (float)point.Y))
			{
				e.Handled = true;
				chartControl.InvalidateVisual();
			}
		}
		catch (Exception)
		{
		}
	}

	private void DashboardMouseUp(object sender, MouseButtonEventArgs e)
	{
		try
		{
			if (dashboard != null && dashboard.MouseUp() && ChartControl != null)
			{
				e.Handled = true;
				ChartControl.InvalidateVisual();
			}
		}
		catch (Exception)
		{
		}
	}

	private static int RecoveryStateRank(OrderState state)
	{
		if (state == OrderState.Filled || state == OrderState.Cancelled || state == OrderState.Rejected)
		{
			return 3;
		}
		if (state == OrderState.CancelPending || state == OrderState.CancelSubmitted)
		{
			return 2;
		}
		return (state != OrderState.Unknown) ? 1 : 0;
	}

	private static RecoverySnapshot SelectRecoverySnapshot(RecoverySnapshot prior, RecoverySnapshot candidate)
	{
		if (prior == null)
		{
			return candidate;
		}
		if (candidate.Filled < prior.Filled)
		{
			return prior;
		}
		if (candidate.Filled > prior.Filled)
		{
			return candidate;
		}
		int num = RecoveryStateRank(prior.State);
		int num2 = RecoveryStateRank(candidate.State);
		RecoverySnapshot recoverySnapshot = ((num2 > num || (num2 == num && candidate.Time >= prior.Time)) ? candidate : prior);
		if (recoverySnapshot.Error == ErrorCode.NoError)
		{
			RecoverySnapshot recoverySnapshot2 = ((recoverySnapshot == prior) ? candidate : prior);
			if (recoverySnapshot2.Error != ErrorCode.NoError)
			{
				recoverySnapshot.Error = recoverySnapshot2.Error;
				recoverySnapshot.Comment = recoverySnapshot2.Comment;
			}
		}
		return recoverySnapshot;
	}


	private void PrepareRecovery()
	{
		if (!IsInStrategyAnalyzer && Account != null && Connection.PlaybackConnection == null)
		{
			string connection = (Account.Connection == null) ? "" : (Account.Connection.Options.Name + "|" + Account.Connection.Options.Provider);
			modelIdentity = "SigmaBreakout_N3_1.7_v2" + "|" + InstanceId + "|" + ExecutionInstrument.FullName;
			recoveryIdentity = connection + "|" + Account.Name + "|" + modelIdentity;
			string safe = recoveryIdentity;
			foreach (char c in Path.GetInvalidFileNameChars()) safe = safe.Replace(c, '_');
			recoveryPath = Path.Combine(Globals.UserDataDir, "SigmaBreakoutNT8", "recovery", safe + ".journal");
			recoveryPending = File.Exists(recoveryPath);
		}
	}

	private void StartRecovery()
	{
		if (recoveryPath == null || !ownsGuard)
		{
			return;
		}
		try
		{
			Directory.CreateDirectory(Path.GetDirectoryName(recoveryPath));
			recoveryLock = new FileStream(recoveryPath + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
		}
		catch (Exception ex)
		{
			RecoveryBlock("Another process owns this strategy recovery journal: " + ex.Message);
			return;
		}
		recoveryAccount = Account;
		recoveryAccount.OrderUpdate += RecoveryOrderUpdate;
		recoveryAccount.ExecutionUpdate += RecoveryExecutionUpdate;
		if (!recoveryPending)
		{
			recoveryReady = true;
			SaveRecovery();
			return;
		}
		try
		{
			using (BinaryReader binaryReader = new BinaryReader(File.OpenRead(recoveryPath)))
			{
				string recoveryVersion = binaryReader.ReadString();
				bool hasRetryState = recoveryVersion == "IndependentNT8.v2" || recoveryVersion == "IndependentNT8.SigmaN3.v3";
				bool hasRetryDistanceState = recoveryVersion == "IndependentNT8.SigmaN3.v3";
				if ((recoveryVersion != "IndependentNT8.SigmaN3.v2" && recoveryVersion != "IndependentNT8.v2" && !hasRetryDistanceState) || binaryReader.ReadString() != recoveryIdentity)
				{
					throw new InvalidDataException("Recovery account, connection, instrument or model mismatch");
				}
				owner = binaryReader.ReadString();
				sequence = binaryReader.ReadInt32();
				orders.Clear();
				processed.Clear();
				executions.Clear();
				recoveryRequests.Clear();
				suspended = binaryReader.ReadBoolean();
				closing = binaryReader.ReadBoolean();
				closeRejected = binaryReader.ReadBoolean();
				pendingCloseName = binaryReader.ReadString();
				if (pendingCloseName == "")
				{
					pendingCloseName = null;
				}
				pendingCloseQuantity = binaryReader.ReadInt32();
				pendingCloseExecuted = binaryReader.ReadInt32();
				pendingCloseFinal = binaryReader.ReadInt32();
				ownedNet = binaryReader.ReadInt32();
				entryName = binaryReader.ReadString();
				if (entryName == "")
				{
					entryName = null;
				}
				sessionDay = new DateTime(binaryReader.ReadInt64());
				entryCount = binaryReader.ReadInt32();
				entryDirection = hasRetryState ? binaryReader.ReadInt32() : 0;
				retryArmed = hasRetryState && binaryReader.ReadBoolean();
				retryEligibleAt = hasRetryState ? new DateTime(binaryReader.ReadInt64()) : DateTime.MinValue;
				targetFilled = hasRetryState && binaryReader.ReadBoolean();
				stopExitFilled = hasRetryDistanceState && binaryReader.ReadBoolean();
				firstStopDistance = hasRetryDistanceState ? binaryReader.ReadDouble() : 0.0;
				entered = binaryReader.ReadInt32();
				exited = binaryReader.ReadInt32();
				entryPending = binaryReader.ReadBoolean();
				entrySum = binaryReader.ReadDouble();
				exitSum = binaryReader.ReadDouble();
				plannedStop = binaryReader.ReadDouble();
				plannedTarget = binaryReader.ReadDouble();
				initialRisk = binaryReader.ReadDouble();
				actualFees = binaryReader.ReadDouble();
				recoveredCommissionFloor = binaryReader.ReadDouble();
				tradeModeledCost = binaryReader.ReadDouble();
				closeReason = binaryReader.ReadString();
				if (closeReason == "")
				{
					closeReason = null;
				}
				peakR = binaryReader.ReadDouble();
				pendingSigmaFlipSide = binaryReader.ReadInt32();
				pendingSigmaFlipSignalClose = binaryReader.ReadDouble();
				pendingSigmaFlipEndEt = new DateTime(binaryReader.ReadInt64());
				if (pendingSigmaFlipSide < -1 || pendingSigmaFlipSide > 1 || (pendingSigmaFlipSide != 0 && (!FinitePositive(pendingSigmaFlipSignalClose) || pendingSigmaFlipEndEt == DateTime.MinValue)))
					throw new InvalidDataException("Invalid pending Sigma reversal state");
				brackets.Clear();
				int num = binaryReader.ReadInt32();
				if (num < 0 || num > 1000)
				{
					throw new InvalidDataException("Invalid bracket count");
				}
				for (int i = 0; i < num; i++)
				{
					brackets.Add(new Bracket
					{
						StopName = binaryReader.ReadString(),
						TargetName = binaryReader.ReadString()
					});
				}
				int num2 = binaryReader.ReadInt32();
				if (num2 < 0 || num2 > 100000)
				{
					throw new InvalidDataException("Invalid order count");
				}
				for (int j = 0; j < num2; j++)
				{
					RecoveryRequest recoveryRequest = new RecoveryRequest
					{
						Name = binaryReader.ReadString(),
						Id = binaryReader.ReadString(),
						Oco = binaryReader.ReadString(),
						Action = (OrderAction)binaryReader.ReadInt32(),
						Type = (OrderType)binaryReader.ReadInt32(),
						Quantity = binaryReader.ReadInt32(),
						Limit = binaryReader.ReadDouble(),
						Stop = binaryReader.ReadDouble(),
						Processed = binaryReader.ReadInt32(),
						Value = binaryReader.ReadDouble()
					};
					if (!recoveryRequest.Name.StartsWith(owner + "_", StringComparison.Ordinal) || recoveryRequest.Quantity < 1 || recoveryRequest.Processed < 0 || recoveryRequest.Processed > recoveryRequest.Quantity)
					{
						throw new InvalidDataException("Invalid owned order journal");
					}
					recoveryRequests.Add(recoveryRequest.Name, recoveryRequest);
					processed[recoveryRequest.Name] = recoveryRequest.Processed;
					lock (recoveredOrderNames)
					{
						recoveredOrderNames.Add(recoveryRequest.Name);
					}
				}
				num2 = binaryReader.ReadInt32();
				if (num2 < 0 || num2 > 1000000)
				{
					throw new InvalidDataException("Invalid execution count");
				}
				for (int k = 0; k < num2; k++)
				{
					executions.Add(binaryReader.ReadString());
				}
				if (binaryReader.BaseStream.Position != binaryReader.BaseStream.Length)
				{
					throw new InvalidDataException("Unexpected journal data");
				}
			}
			if (!hasRetryDistanceState && entryCount == 1 && ownedNet != 0)
			{
				if (entryDirection == 0) entryDirection = Math.Sign(ownedNet);
				if (SlPct > 0.0 && initialRisk > 0.0) firstStopDistance = initialRisk;
			}
			if (retryArmed && (SlPct <= 0.0 || firstStopDistance <= 0.0 || entryDirection == 0 || entryCount != 1 || targetFilled))
			{
				retryArmed = false;
				retryEligibleAt = DateTime.MinValue;
				Print(Name + ": disarmed legacy retry because its original stop distance or target state cannot be verified");
			}
			recoveredTrade = ownedNet != 0 || entryPending || closing;
			recoveryReady = true;
			if (!string.IsNullOrEmpty(closeReason) && closeReason.StartsWith("BrokerAccountRestricted:", StringComparison.Ordinal))
			{
				RecoveryBlock("Broker account restriction remains latched; verify broker account and owned protection: " + closeReason);
				return;
			}
			ReconcileRecovery();
		}
		catch (Exception ex2)
		{
			RecoveryBlock("Recovery journal could not be read: " + ex2.Message);
		}
	}

	private void RecoveryBlock(string reason)
	{
		recoveryFault = true;
		suspended = true;
		status = reason;
		Print(Name + ": " + reason);
		NinjaScript.Log(Name + ": " + reason, LogLevel.Error);
	}

	private void SaveRecovery(bool allowDuringReconciliation = false)
	{
		if (recoveryPath == null || recoveryLock == null || !recoveryReady || recoveryFault || (reconcilingRecovery && !allowDuringReconciliation) || (State != State.Realtime && State != State.Terminated))
		{
			return;
		}
		try
		{
			Directory.CreateDirectory(Path.GetDirectoryName(recoveryPath));
			string text = recoveryPath + ".tmp";
			using (FileStream fileStream = new FileStream(text, FileMode.Create, FileAccess.Write, FileShare.None))
			{
				using BinaryWriter binaryWriter = new BinaryWriter(fileStream);
				binaryWriter.Write("IndependentNT8.SigmaN3.v3");
				binaryWriter.Write(recoveryIdentity);
				binaryWriter.Write(owner);
				binaryWriter.Write(sequence);
				binaryWriter.Write(suspended);
				binaryWriter.Write(closing);
				binaryWriter.Write(closeRejected);
				binaryWriter.Write(pendingCloseName ?? "");
				binaryWriter.Write(pendingCloseQuantity);
				binaryWriter.Write(pendingCloseExecuted);
				binaryWriter.Write(pendingCloseFinal);
				binaryWriter.Write(ownedNet);
				binaryWriter.Write(entryName ?? "");
				binaryWriter.Write(sessionDay.Ticks);
				binaryWriter.Write(entryCount);
				binaryWriter.Write(entryDirection);
				binaryWriter.Write(retryArmed);
				binaryWriter.Write(retryEligibleAt.Ticks);
				binaryWriter.Write(targetFilled);
				binaryWriter.Write(stopExitFilled);
				binaryWriter.Write(firstStopDistance);
				binaryWriter.Write(entered);
				binaryWriter.Write(exited);
				binaryWriter.Write(entryPending);
				binaryWriter.Write(entrySum);
				binaryWriter.Write(exitSum);
				binaryWriter.Write(plannedStop);
				binaryWriter.Write(plannedTarget);
				binaryWriter.Write(initialRisk);
				binaryWriter.Write(actualFees);
				binaryWriter.Write(recoveredCommissionFloor);
				binaryWriter.Write(tradeModeledCost);
				binaryWriter.Write(closeReason ?? "");
				binaryWriter.Write(peakR);
				binaryWriter.Write(pendingSigmaFlipSide);
				binaryWriter.Write(pendingSigmaFlipSignalClose);
				binaryWriter.Write(pendingSigmaFlipEndEt.Ticks);
				binaryWriter.Write(brackets.Count);
				foreach (Bracket bracket in brackets)
				{
					binaryWriter.Write(bracket.StopName);
					binaryWriter.Write(bracket.TargetName);
				}
				binaryWriter.Write(recoveryRequests.Count);
				foreach (RecoveryRequest value2 in recoveryRequests.Values)
				{
					processed.TryGetValue(value2.Name, out var value);
					value2.Processed = value;
					binaryWriter.Write(value2.Name);
					binaryWriter.Write(value2.Id ?? "");
					binaryWriter.Write(value2.Oco ?? "");
					binaryWriter.Write((int)value2.Action);
					binaryWriter.Write((int)value2.Type);
					binaryWriter.Write(value2.Quantity);
					binaryWriter.Write(value2.Limit);
					binaryWriter.Write(value2.Stop);
					binaryWriter.Write(value2.Processed);
					binaryWriter.Write(value2.Value);
				}
				binaryWriter.Write(executions.Count);
				foreach (string execution in executions)
				{
					binaryWriter.Write(execution);
				}
				binaryWriter.Flush();
				fileStream.Flush(flushToDisk: true);
			}
			if (File.Exists(recoveryPath))
			{
				File.Replace(text, recoveryPath, null);
			}
			else
			{
				File.Move(text, recoveryPath);
			}
		}
		catch (Exception ex)
		{
			RecoveryBlock("Durable recovery write failed; new orders blocked: " + ex.Message);
		}
	}

	private string RecoveryCaseId()
	{
		using MemoryStream memoryStream = new MemoryStream();
		using BinaryWriter binaryWriter = new BinaryWriter(memoryStream);
		binaryWriter.Write(recoveryIdentity ?? "");
		binaryWriter.Write(owner ?? "");
		binaryWriter.Write(entryName ?? "");
		binaryWriter.Write(sequence);
		binaryWriter.Write(ownedNet);
		foreach (RecoveryRequest item in recoveryRequests.Values.OrderBy((RecoveryRequest r) => r.Name, StringComparer.Ordinal))
		{
			processed.TryGetValue(item.Name, out var value);
			binaryWriter.Write(item.Name);
			binaryWriter.Write(item.Id ?? "");
			binaryWriter.Write(item.Quantity);
			binaryWriter.Write(value);
			binaryWriter.Write(item.Value);
		}
		binaryWriter.Flush();
		using SHA256 sHA = SHA256.Create();
		return BitConverter.ToString(sHA.ComputeHash(memoryStream.ToArray())).Replace("-", "").Substring(0, 32);
	}

	private bool RefuseExternalSettlement(string reason)
	{
		if (externalRecoveryRefusalReported != reason)
		{
			externalRecoveryRefusalReported = reason;
			Print(Name + ": external settlement refused: " + reason);
		}
		return false;
	}

	private bool TryConfirmExternalSettlement(Order[] brokerOrders)
	{
		if (State != State.Realtime || IsInStrategyAnalyzer || !recoveryReady || recoveryFault || recoveryLock == null || !recoveryPending || ownedNet == 0 || string.IsNullOrEmpty(entryName))
		{
			return false;
		}
		string text = RecoveryCaseId();
		string text2 = "CLOSED " + text + " NO_ORDERS";
		if (externalRecoveryCaseReported != text)
		{
			externalRecoveryCaseReported = text;
			externalRecoveryRefusalReported = null;
			Print(Name + ": RECOVERY_CASE " + text + " | " + recoveryIdentity + " | entry=" + entryName + " | owned=" + ownedNet + " | Automatic reconciliation continues; new orders stay paused until exact owned broker evidence arrives.");
		}
		if (string.IsNullOrWhiteSpace(ExternalCloseConfirmation))
		{
			return false;
		}
		if (!string.Equals(ExternalCloseConfirmation.Trim(), text2, StringComparison.Ordinal))
		{
			return RefuseExternalSettlement("confirmation belongs to a different recovery case");
		}
		if (EnableTrading)
		{
			return RefuseExternalSettlement("disable Enable new trades before confirming");
		}
		if (Account == null || Account.ConnectionStatus != ConnectionStatus.Connected)
		{
			return RefuseExternalSettlement("account must be connected");
		}
		lock (Account.Positions)
		{
			if (Account.Positions.Any((Position p) => p.Instrument.FullName == ExecutionInstrument.FullName && p.Quantity != 0))
			{
				return RefuseExternalSettlement("contract still has a non-flat account position");
			}
		}
		processed.TryGetValue(entryName, out var value);
		if (!recoveryRequests.TryGetValue(entryName, out var value2) || value != value2.Quantity)
		{
			return RefuseExternalSettlement("entry quantity is not fully reconciled");
		}
		foreach (Order order in brokerOrders)
		{
			if (order.Name != null && recoveryRequests.ContainsKey(order.Name) && order.OrderState != OrderState.Unknown && !Settled(order))
			{
				return RefuseExternalSettlement("an owned order still has an active broker state: " + order.Name);
			}
		}
		try
		{
			SaveRecovery(allowDuringReconciliation: true);
			if (recoveryFault)
			{
				return true;
			}
			string text3 = owner;
			string text4 = owner.Substring(0, owner.LastIndexOf('_') + 1) + Guid.NewGuid().ToString("N").Substring(0, 12);
			string text5 = Path.Combine(Path.GetDirectoryName(recoveryPath), "settlements");
			Directory.CreateDirectory(text5);
			string text6 = Path.Combine(text5, text + "-" + Guid.NewGuid().ToString("N").Substring(0, 8));
			byte[] array = File.ReadAllBytes(recoveryPath);
			using (FileStream fileStream = new FileStream(text6 + ".before", FileMode.CreateNew, FileAccess.Write, FileShare.None))
			{
				fileStream.Write(array, 0, array.Length);
				fileStream.Flush(flushToDisk: true);
			}
			string s = "Operator-confirmed external closure\r\nUTC=" + DateTime.UtcNow.ToString("o") + "\r\nIdentity=" + recoveryIdentity + "\r\nEntry=" + entryName + "\r\nOwnedQuantity=" + ownedNet + "\r\nConfirmation=" + text2 + "\r\nPreviousOwner=" + text3 + "\r\nNextOwner=" + text4 + "\r\nBroker facts attested by operator: owned trade fully closed; all its orders inactive.\r\nNative exit ID, exit price and PnL were not reconstructed. Existing execution IDs remain unchanged.\r\nReceipt precedes checkpoint commit; the journal owner establishes whether commit completed.\r\n";
			byte[] bytes = Encoding.UTF8.GetBytes(s);
			using (FileStream fileStream2 = new FileStream(text6 + ".confirmation.txt", FileMode.CreateNew, FileAccess.Write, FileShare.None))
			{
				fileStream2.Write(bytes, 0, bytes.Length);
				fileStream2.Flush(flushToDisk: true);
			}
			owner = text4;
			ownedNet = 0;
			entryName = null;
			closing = false;
			closeRejected = false;
			pendingCloseName = null;
			pendingCloseQuantity = 0;
			pendingCloseExecuted = 0;
			pendingCloseFinal = -1;
			orders.Clear();
			processed.Clear();
			reportedFilled.Clear();
			recoveryRequests.Clear();
			recoverySnapshots.Clear();
			lock (recoveredOrderNames)
			{
				recoveredOrderNames.Clear();
			}
			entered = 0;
			exited = 0;
			entrySum = 0.0;
			exitSum = 0.0;
			entryPending = false;
			closeReason = null;
			brackets.Clear();
			executionVisual = null;
			recoveredTrade = true;
			connectionPaused = true;
			SaveRecovery(allowDuringReconciliation: true);
			if (recoveryFault)
			{
				return true;
			}
			recoveryPending = false;
			ReleaseExposure(text3);
			status = "External close confirmed; new entries disabled";
			Print(Name + ": EXTERNAL_SETTLEMENT_COMMITTED case=" + text + "; backup=" + text6 + ".before; owned position flat; no broker orders submitted/cancelled; keep the confirmation receipt.");
			return true;
		}
		catch (Exception ex)
		{
			RecoveryBlock("External settlement could not be committed; review journal and backup: " + ex.Message);
			return true;
		}
	}

	private bool RecoveryTracksOrder(string name)
	{
		if (name == null)
		{
			return false;
		}
		lock (recoveredOrderNames)
		{
			return recoveredOrderNames.Contains(name);
		}
	}

	private void WaitForRecoveryEvidence()
	{
		recoveryPending = true;
		if (status != "Recovery pending: awaiting broker evidence")
		{
			status = "Recovery pending: awaiting broker evidence";
			Print(Name + ": Recovery pending: awaiting broker evidence");
		}
		SaveRecovery();
	}

	private void StopRecovery()
	{
		SaveRecovery();
		if (recoveryAccount != null)
		{
			recoveryAccount.OrderUpdate -= RecoveryOrderUpdate;
			recoveryAccount.ExecutionUpdate -= RecoveryExecutionUpdate;
			recoveryAccount = null;
		}
		if (recoveryLock != null)
		{
			recoveryLock.Dispose();
			recoveryLock = null;
		}
	}

	private void RememberOrder(Order order, int quantity, double limit, double stop)
	{
		if (State == State.Realtime && order != null)
		{
			if (!recoveryRequests.TryGetValue(order.Name, out var value))
			{
				Dictionary<string, RecoveryRequest> dictionary = recoveryRequests;
				string name = order.Name;
				RecoveryRequest recoveryRequest = new RecoveryRequest
				{
					Name = order.Name,
					Action = order.OrderAction,
					Type = order.OrderType,
					Oco = order.Oco
				};
				value = recoveryRequest;
				dictionary[name] = recoveryRequest;
			}
			value.Id = order.OrderId ?? "";
			value.Quantity = quantity;
			value.Limit = limit;
			value.Stop = stop;
			// This exact object is observed only through our submission/verified recovery path.
			if (order.Name != entryName)
				ProductExposure.ObserveExit(order);
		}
	}

	private void RememberExecution(Order order, int quantity, double price)
	{
		if (State == State.Realtime && recoveryRequests.TryGetValue(order.Name, out var value))
		{
			value.Value += (double)quantity * price;
		}
	}

	private bool RecoveryPositionConsistent()
	{
		if (State != State.Realtime || ownedNet == 0)
		{
			return true;
		}
		Position position;
		lock (Account.Positions)
		{
			position = Account.Positions.FirstOrDefault((Position p) => p.Instrument.FullName == ExecutionInstrument.FullName && p.Quantity != 0);
		}
		int value = ((position != null) ? (((position.MarketPosition == MarketPosition.Long) ? 1 : (-1)) * position.Quantity) : 0);
		return Math.Sign(value) == Math.Sign(ownedNet) && Math.Abs(value) >= Math.Abs(ownedNet);
	}

	private void CancelOppositeOwnedProtection()
	{
		if (Account == null || Account.ConnectionStatus != ConnectionStatus.Connected || ownedNet == 0)
		{
			return;
		}
		Position position;
		lock (Account.Positions)
		{
			position = Account.Positions.FirstOrDefault((Position p) => p.Instrument.FullName == ExecutionInstrument.FullName && p.Quantity != 0);
		}
		if (position == null)
		{
			return;
		}
		int value = ((position.MarketPosition == MarketPosition.Long) ? position.Quantity : (-position.Quantity));
		if (Math.Sign(value) == Math.Sign(ownedNet))
		{
			return;
		}
		Order[] array = orders.Values.ToArray();
		foreach (Order order in array)
		{
			if (recoveryRequests.TryGetValue(order.Name, out var value2) && !Settled(order) && order.OrderType != OrderType.Market && order.OrderState != OrderState.CancelPending && order.OrderState != OrderState.CancelSubmitted && !(order.Instrument.FullName != ExecutionInstrument.FullName) && (string.IsNullOrEmpty(value2.Id) || !(value2.Id != order.OrderId)))
			{
				try
				{
					Account.Cancel(new Order[1] { order });
				}
				catch (Exception ex)
				{
					Print(Name + " could not cancel incompatible owned protection: " + ex.Message);
				}
			}
		}
	}

	private bool RefreshOwnedProviderIdentity(Order order, int quantity, double limit, double stop)
	{
		if (!recoveryRequests.TryGetValue(order.Name, out var value) || !orders.TryGetValue(order.Name, out var value2) || value2 != order)
		{
			return true;
		}
		if (order.Account != Account || order.Instrument.FullName != ExecutionInstrument.FullName || !SameBrokerAction(order.OrderAction.ToString(), value.Action.ToString()) || order.OrderType != value.Type || order.Oco != (value.Oco ?? ""))
		{
			RecoveryBlock(RecoveryIdentityError(order, value));
			return false;
		}
		if (!string.IsNullOrEmpty(value.Id) && value.Id != order.OrderId && !Guid.TryParseExact(value.Id, "N", out var _))
		{
			RecoveryBlock(RecoveryIdentityError(order, value));
			return false;
		}
		RememberOrder(order, quantity, limit, stop);
		SaveRecovery(allowDuringReconciliation: true);
		return !recoveryFault;
	}

	private void UpgradeProvisionalProtectionIds(List<Order> observed)
	{
		Dictionary<RecoveryRequest, string> dictionary = new Dictionary<RecoveryRequest, string>();
		foreach (RecoveryRequest request in recoveryRequests.Values)
		{
			if (string.IsNullOrEmpty(request.Id) || !Guid.TryParseExact(request.Id, "N", out var temporary) || string.IsNullOrEmpty(request.Oco) || (request.Type != OrderType.StopMarket && request.Type != OrderType.Limit) || !request.Name.StartsWith(owner + "_", StringComparison.Ordinal))
			{
				continue;
			}
			List<Order> source = observed.Where((Order o) => o != null && o.Name == request.Name && o.Account == Account && o.Instrument.FullName == ExecutionInstrument.FullName).ToList();
			List<Order> source2 = source.Where((Order o) => o.OrderState != OrderState.Initialized && o.OrderState != OrderState.Unknown && !string.IsNullOrEmpty(o.OrderId) && !Guid.TryParseExact(o.OrderId, "N", out temporary)).ToList();
			List<string> list = source2.Select((Order o) => o.OrderId).Distinct(StringComparer.Ordinal).ToList();
			if (list.Count != 0)
			{
				if (list.Count != 1)
				{
					throw new InvalidDataException("Multiple assigned broker IDs for provisional owned protection: " + request.Name);
				}
				Order candidate = source2.OrderByDescending((Order o) => o.Filled).First();
				if (source.Any((Order o) => !string.IsNullOrEmpty(o.OrderId) && o.OrderId != request.Id && o.OrderId != candidate.OrderId) || !SameBrokerAction(request.Action.ToString(), candidate.OrderAction.ToString()) || candidate.OrderType != request.Type || candidate.Oco != request.Oco || candidate.Quantity != request.Quantity || candidate.LimitPrice != request.Limit || candidate.StopPrice != request.Stop || candidate.Filled < request.Processed || candidate.Filled > request.Quantity)
				{
					throw new InvalidDataException(RecoveryIdentityError(candidate, request));
				}
				dictionary.Add(request, candidate.OrderId);
			}
		}
		if (dictionary.Count == 0)
		{
			return;
		}
		if (!string.IsNullOrEmpty(recoveryPath) && File.Exists(recoveryPath))
		{
			File.Copy(recoveryPath, recoveryPath + ".before_provider_id_" + DateTime.UtcNow.Ticks, overwrite: false);
		}
		foreach (KeyValuePair<RecoveryRequest, string> item in dictionary)
		{
			item.Key.Id = item.Value;
		}
		SaveRecovery(allowDuringReconciliation: true);
		if (recoveryFault)
		{
			throw new InvalidDataException("Assigned broker IDs could not be saved durably.");
		}
		foreach (KeyValuePair<RecoveryRequest, string> item2 in dictionary)
		{
			NinjaScript.Log(Name + ": RECOVERY_PROVIDER_ID_BOUND " + item2.Key.Name + " id=" + item2.Value, LogLevel.Information);
		}
	}

	private string RecoveryIdentityError(Order order, RecoveryRequest request)
	{
		return "Broker order identity differs from owned journal: " + request.Name + " | expected: instrument=" + ExecutionInstrument.FullName + ", id=" + request.Id + ", action=" + request.Action.ToString() + ", type=" + request.Type.ToString() + ", OCO=" + request.Oco + " | observed: instrument=" + order.Instrument.FullName + ", id=" + order.OrderId + ", action=" + order.OrderAction.ToString() + ", type=" + order.OrderType.ToString() + ", OCO=" + order.Oco;
	}

	private static bool SameBrokerAction(string expected, string observed)
	{
		return ((expected == "Buy" || expected == "BuyToCover") && (observed == "Buy" || observed == "BuyToCover")) || ((expected == "Sell" || expected == "SellShort") && (observed == "Sell" || observed == "SellShort"));
	}

	private Order[] RecoveryBrokerOrders()
	{
		List<Order> list = new List<Order>();
		lock (Account.Orders)
		{
			list.AddRange(Account.Orders);
		}
		lock (Account.Executions)
		{
			foreach (Execution execution in Account.Executions)
			{
				if (execution != null && execution.Order != null)
				{
					list.Add(execution.Order);
				}
			}
		}
		UpgradeProvisionalProtectionIds(list);
		Dictionary<string, Order> dictionary = new Dictionary<string, Order>();
		foreach (Order item in list)
		{
			if (item == null || item.Name == null || !recoveryRequests.TryGetValue(item.Name, out var value))
			{
				continue;
			}
			if (item.Instrument.FullName != ExecutionInstrument.FullName || !SameBrokerAction(item.OrderAction.ToString(), value.Action.ToString()) || item.OrderType != value.Type || (!string.IsNullOrEmpty(value.Id) && value.Id != item.OrderId) || item.Oco != (value.Oco ?? ""))
			{
				throw new InvalidDataException(RecoveryIdentityError(item, value));
			}
			if (!dictionary.TryGetValue(item.Name, out var value2))
			{
				dictionary[item.Name] = item;
				continue;
			}
			if (!string.IsNullOrEmpty(value2.OrderId) && !string.IsNullOrEmpty(item.OrderId) && value2.OrderId != item.OrderId)
			{
				throw new InvalidDataException("Multiple broker IDs for owned order: " + value.Name);
			}
			bool flag = item.OrderState == OrderState.Filled || item.OrderState == OrderState.Cancelled || item.OrderState == OrderState.Rejected;
			bool flag2 = value2.OrderState == OrderState.Filled || value2.OrderState == OrderState.Cancelled || value2.OrderState == OrderState.Rejected;
			if (item.Filled > value2.Filled || (item.Filled == value2.Filled && ((value2.OrderState == OrderState.Unknown && item.OrderState != OrderState.Unknown) || (string.IsNullOrEmpty(value2.OrderId) && !string.IsNullOrEmpty(item.OrderId)) || (flag && !flag2))))
			{
				dictionary[item.Name] = item;
			}
		}
		return dictionary.Values.ToArray();
	}

	private bool ResumeTechnicalProtectionClose()
	{
		if (!closing || closeReason != "ProtectionCancelled")
		{
			return true;
		}
		if (State != State.Realtime || ownedNet == 0 || recoveryFault || closeRejected || !string.IsNullOrEmpty(pendingCloseName) || pendingCloseQuantity != 0 || pendingCloseExecuted != 0 || pendingCloseFinal > 0 || exited != 0 || entered != Math.Abs(ownedNet) || !RecoveryPositionConsistent() || orders.Values.Any((Order o) => !ExecutionsCaughtUp(o)))
		{
			WaitForRecoveryEvidence();
			return false;
		}
		closing = false;
		closeReason = null;
		suspended = false;
		SaveRecovery(allowDuringReconciliation: true);
		if (recoveryFault)
		{
			return false;
		}
		Print(Name + ": TECHNICAL_PROTECTION_CLOSE_RESUMED reason=ProtectionCancelled; verified owned quantity=" + ownedNet);
		return true;
	}

	private void ReconcileRecovery()
	{
		if (!recoveryPending || recoveryFault || reconcilingRecovery || Account == null || Account.ConnectionStatus != ConnectionStatus.Connected)
		{
			return;
		}
		reconcilingRecovery = true;
		try
		{
			bool flag = false;
			Order[] array = RecoveryBrokerOrders();
			Order[] array2 = array;
			foreach (Order order in array2)
			{
				if (order.Name == null || !recoveryRequests.TryGetValue(order.Name, out var value))
				{
					continue;
				}
				if (order.Instrument.FullName != ExecutionInstrument.FullName || !SameBrokerAction(order.OrderAction.ToString(), value.Action.ToString()) || order.OrderType != value.Type || (!string.IsNullOrEmpty(value.Id) && value.Id != order.OrderId) || order.Oco != (value.Oco ?? ""))
				{
					throw new InvalidDataException(RecoveryIdentityError(order, value));
				}
				if (order.OrderState == OrderState.Unknown)
				{
					flag = true;
					continue;
				}
				recoverySnapshots.TryGetValue(order.Name, out var value2);
				value2 = SelectRecoverySnapshot(value2, new RecoverySnapshot
				{
					Quantity = order.Quantity,
					Filled = order.Filled,
					Limit = order.LimitPrice,
					Stop = order.StopPrice,
					Average = order.AverageFillPrice,
					State = order.OrderState,
					Time = order.Time,
					Error = ErrorCode.NoError,
					Comment = ""
				});
				recoverySnapshots[order.Name] = value2;
				lock (recoveredOrderNames)
				{
					recoveredOrderNames.Add(order.Name);
				}
				RememberOrder(order, value2.Quantity, value2.Limit, value2.Stop);
				orders[order.Name] = order;
				reportedFilled.TryGetValue(order.Name, out var value3);
				reportedFilled[order.Name] = Math.Max(value3, value2.Filled);
				foreach (Bracket bracket in brackets)
				{
					if (order.Name == bracket.StopName)
					{
						bracket.Stop = order;
					}
					if (order.Name == bracket.TargetName)
					{
						bracket.Target = order;
					}
				}
				processed.TryGetValue(order.Name, out var value4);
				if (value2.Filled < value4)
				{
					throw new InvalidDataException("Broker filled quantity regressed: " + order.Name);
				}
				if (value2.Filled > value4)
				{
					int num = value2.Filled - value4;
					double num2 = (value2.Average * (double)value2.Filled - value.Value) / (double)num;
					if (double.IsNaN(num2) || double.IsInfinity(num2) || num2 <= 0.0)
					{
						throw new InvalidDataException("Invalid recovered fill price");
					}
					Execution execution = new Execution
					{
						Account = Account,
						Instrument = order.Instrument,
						Order = order,
						Price = num2,
						Quantity = num,
						Time = value2.Time,
						ExecutionId = "RECOVERY_" + order.OrderId + "_" + value2.Filled,
						Commission = 0.0
					};
					applyingRecoveryExecution = execution.ExecutionId;
					try
					{
						OnExecutionUpdate(execution, execution.ExecutionId, num2, num, MarketPosition.Flat, order.OrderId, value2.Time);
					}
					finally
					{
						applyingRecoveryExecution = null;
					}
				}
				OnOrderUpdate(order, value2.Limit, value2.Stop, value2.Quantity, value2.Filled, value2.Average, value2.State, value2.Time, value2.Error, value2.Comment);
			}
			if (!flag && entryName != null && recoveryRequests.TryGetValue(entryName, out var value5) && value5.Processed < value5.Quantity && !orders.ContainsKey(entryName))
			{
				throw new InvalidDataException("Pending entry was not returned by broker; submission outcome is unknown");
			}
			bool flag2 = ownedNet != 0 && !RecoveryPositionConsistent();
			if (flag | flag2)
			{
				if (!TryConfirmExternalSettlement(array))
				{
					if (flag2)
					{
						CancelOppositeOwnedProtection();
					}
					recoveryPending = true;
					string text = (flag ? "Recovery pending: order status" : "Recovery pending: owned fills");
					if (status != text)
					{
						status = text;
						Print(Name + ": " + text + "; saved owned quantity=" + ownedNet);
					}
				}
				return;
			}
			if (!ResumeTechnicalProtectionClose())
			{
				return;
			}
			recoveryPending = false;
			if (recoveryFault)
			{
				return;
			}
			status = ((ownedNet == 0) ? "Recovered owned state; waiting for fresh signal" : "Recovered owned position and broker protection");
		}
		catch (Exception ex)
		{
			CancelOppositeOwnedProtection();
			RecoveryBlock("Broker recovery blocked: " + ex.Message);
		}
		finally
		{
			reconcilingRecovery = false;
			SaveRecovery();
		}
		if (recoveryFault || recoveryPending)
		{
			return;
		}
		if (ownedNet == 0)
		{
			Order[] array3 = orders.Values.ToArray();
			foreach (Order order2 in array3)
			{
				if (order2.OrderType != OrderType.Market && !Settled(order2))
				{
					RecoveryCancel(order2);
				}
			}
		}
		else if (closing && !closeRejected)
		{
			DriveClose();
		}
		else
		{
			RepairOwnedProtection();
		}
	}


	private bool ReadyForProtectionRepair(Order stopOrder, Order targetOrder, string stopName, string targetName)
	{
		bool needStop = !string.IsNullOrEmpty(stopName);
		bool needTarget = !string.IsNullOrEmpty(targetName);
		Order[] required = new[] { needStop ? stopOrder : null, needTarget ? targetOrder : null }.Where(o => o != null).ToArray();
		if ((needStop && stopOrder == null && recoveryRequests.ContainsKey(stopName)) || (needTarget && targetOrder == null && recoveryRequests.ContainsKey(targetName)) ||
			required.Any(o => o.OrderState == OrderState.Unknown || o.OrderState == OrderState.CancelPending || o.OrderState == OrderState.CancelSubmitted))
		{
			WaitForRecoveryEvidence();
			return false;
		}
		if (needStop && needTarget && Settled(stopOrder) != Settled(targetOrder))
		{
			WaitForRecoveryEvidence();
			return false;
		}
		return true;
	}


	private bool RepairOwnedProtection()
	{
		if (repairingProtection || !BrokerReady() || recoveryFault || (closing && !closeRejected)) return false;
		if (ownedNet == 0) return true;
		if (!RecoveryPositionConsistent() || orders.Values.Any(o => !ExecutionsCaughtUp(o))) { WaitForRecoveryEvidence(); return false; }
		if (!RequiresStopProtection() && !RequiresTargetProtection()) return true;
		repairingProtection = true;
		try
		{
			int remaining = Math.Abs(ownedNet);
			int side = Math.Sign(ownedNet);
			if (brackets.Count == 0)
			{
				Bracket newBracket = new Bracket();
				if (RequiresStopProtection()) newBracket.StopName = owner + "_RecStop_" + (++sequence);
				if (RequiresTargetProtection()) newBracket.TargetName = owner + "_RecTgt_" + (++sequence);
				brackets.Add(newBracket);
			}
			foreach (Bracket b in brackets)
			{
				bool needStop = RequiresStopProtection() && !string.IsNullOrEmpty(b.StopName);
				bool needTarget = RequiresTargetProtection() && !string.IsNullOrEmpty(b.TargetName);
				if (!needStop && !needTarget) continue;
				int stopFilled = b.Stop == null ? 0 : b.Stop.Filled;
				int targetFilled = b.Target == null ? 0 : b.Target.Filled;
				int covered = stopFilled + targetFilled;
				int bracketQty = needStop && recoveryRequests.TryGetValue(b.StopName, out RecoveryRequest req) ? req.Quantity : remaining;
				int desired = Math.Min(remaining, Math.Max(0, bracketQty - covered));
				if (desired <= 0) continue;
				remaining -= desired;
				if (needStop && !Settled(b.Stop) && b.Stop.Quantity - b.Stop.Filled != desired) { if (b.Stop.Quantity - b.Stop.Filled > desired) RecoveryCancel(b.Stop); throw new InvalidDataException("Owned stop quantity differs from remaining bracket"); }
				if (needTarget && !Settled(b.Target) && b.Target.Quantity - b.Target.Filled != desired) { if (b.Target.Quantity - b.Target.Filled > desired) RecoveryCancel(b.Target); throw new InvalidDataException("Owned target quantity differs from remaining bracket"); }
				if (!ReadyForProtectionRepair(b.Stop, b.Target, needStop ? b.StopName : null, needTarget ? b.TargetName : null)) return false;
				double quote = side > 0 ? GetCurrentBid(executionSeries) : GetCurrentAsk(executionSeries);
				if (quote <= 0) throw new InvalidDataException("Unavailable quote while restoring owned protection");
				string oco = owner + "_RepOco_" + (++sequence);
				if (needStop && Settled(b.Stop))
				{
					if (side * (quote - plannedStop) <= 0) throw new InvalidDataException("Stop level already crossed; closing/recovery required");
					b.StopName = owner + "_RepStop_" + (++sequence);
					b.Stop = RecoverySubmit(executionSeries, side > 0 ? OrderAction.Sell : OrderAction.BuyToCover, OrderType.StopMarket, desired, 0, plannedStop, oco, b.StopName);
					if (b.Stop == null || b.Stop.OrderState == OrderState.Rejected) throw new InvalidDataException("Recovered stop not accepted");
				}
				if (needTarget && Settled(b.Target))
				{
					if (side * (plannedTarget - quote) <= 0) throw new InvalidDataException("Target level already crossed; closing/recovery required");
					b.TargetName = owner + "_RepTgt_" + (++sequence);
					b.Target = RecoverySubmit(executionSeries, side > 0 ? OrderAction.Sell : OrderAction.BuyToCover, OrderType.Limit, desired, plannedTarget, 0, oco, b.TargetName);
					if (b.Target == null || b.Target.OrderState == OrderState.Rejected) throw new InvalidDataException("Recovered target not accepted");
				}
			}
			if (remaining != 0) throw new InvalidDataException("Owned exposure remains without a journalled bracket allocation");
			SaveRecovery();
			return true;
		}
		catch (Exception ex)
		{
			RecoveryBlock("Owned protection requires attention: " + ex.Message);
			return false;
		}
		finally { repairingProtection = false; }
	}

	private void RecoveryOrderUpdate(object sender, OrderEventArgs args)
	{
		Order order = args.Order;
		if (order == null || order.Name == null || !RecoveryTracksOrder(order.Name))
		{
			return;
		}
		double limit = args.LimitPrice;
		double stop = args.StopPrice;
		double average = args.AverageFillPrice;
		int quantity = args.Quantity;
		int filled = args.Filled;
		OrderState state = args.OrderState;
		DateTime time = args.Time;
		ErrorCode error = args.Error;
		string comment = args.Comment;
		TriggerCustomEvent((object ignored) =>
		{
			if (State == State.Realtime && !recoveryFault && RecoveryTracksOrder(order.Name) && RefreshOwnedProviderIdentity(order, quantity, limit, stop))
			{
				recoverySnapshots.TryGetValue(order.Name, out var value);
				RecoverySnapshot prior = SelectRecoverySnapshot(value, new RecoverySnapshot
				{
					Quantity = quantity,
					Filled = filled,
					Limit = limit,
					Stop = stop,
					Average = average,
					State = state,
					Time = time,
					Error = error,
					Comment = comment
				});
				recoverySnapshots[order.Name] = SelectRecoverySnapshot(prior, new RecoverySnapshot
				{
					Quantity = order.Quantity,
					Filled = order.Filled,
					Limit = order.LimitPrice,
					Stop = order.StopPrice,
					Average = order.AverageFillPrice,
					State = order.OrderState,
					Time = order.Time,
					Error = ErrorCode.NoError,
					Comment = ""
				});
				recoveryPending = true;
				ReconcileRecovery();
			}
		}, null);
	}

	private bool ReconcileOwnedExecution(Execution execution, string executionId)
	{
		if (State != State.Realtime || execution == null || execution.Order == null || executionId == applyingRecoveryExecution)
		{
			return false;
		}
		lock (recoveredOrderNames)
		{
			if (!recoveredOrderNames.Contains(execution.Order.Name))
			{
				return false;
			}
		}
		recoveryPending = true;
		ReconcileRecovery();
		return true;
	}

	private void RecoveryExecutionUpdate(object sender, ExecutionEventArgs args)
	{
		Execution execution = args.Execution;
		if (execution == null || execution.Order == null || execution.Order.Name == null || !RecoveryTracksOrder(execution.Order.Name))
		{
			return;
		}
		string executionId = args.ExecutionId;
		string orderId = args.OrderId;
		double price = args.Price;
		int quantity = args.Quantity;
		DateTime time = args.Time;
		MarketPosition marketPosition = args.MarketPosition;
		TriggerCustomEvent((object ignored) =>
		{
			if (State == State.Realtime && !recoveryFault && RecoveryTracksOrder(execution.Order.Name))
			{
				recoveryPending = true;
				ReconcileRecovery();
			}
		}, null);
	}

	private Order RecoverySubmit(int series, OrderAction action, OrderType type, int quantity, double limit, double stop, string oco, string name)
	{
		if (State == State.Historical)
		{
			return SubmitOrderUnmanaged(series, action, type, quantity, limit, stop, oco, name);
		}
		if (!BrokerReady() || recoveryFault)
		{
			return null;
		}
		recoveryRequests[name] = new RecoveryRequest
		{
			Name = name,
			Id = "",
			Oco = oco,
			Action = action,
			Type = type,
			Quantity = quantity,
			Limit = limit,
			Stop = stop
		};
		SaveRecovery();
		if (recoveryFault)
		{
			return null;
		}
		Order order;
		if (recoveredTrade)
		{
			order = Account.CreateOrder(ExecutionInstrument, action, type, OrderEntry.Automated, TimeInForce.Gtc, quantity, limit, stop, oco, name, DateTime.MaxValue, null);
			lock (recoveredOrderNames)
			{
				recoveredOrderNames.Add(name);
			}
			orders[name] = order;
			RememberOrder(order, quantity, limit, stop);
			SaveRecovery();
			if (!recoveryFault)
			{
				Account.Submit(new Order[1] { order });
			}
		}
		else
		{
			order = SubmitOrderUnmanaged(series, action, type, quantity, limit, stop, oco, name);
		}
		if (order != null)
		{
			RememberOrder(order, quantity, limit, stop);
		}
		SaveRecovery();
		return order;
	}

	private void RecoveryCancel(Order order)
	{
		if (!BrokerReady() || order == null || recoveryFault)
		{
			return;
		}
		SaveRecovery();
		if (!recoveryFault)
		{
			if (recoveredOrderNames.Contains(order.Name))
			{
				Account.Cancel(new Order[1] { order });
			}
			else
			{
				CancelOrder(order);
			}
		}
	}

	private void RecoveryChange(Order order, int quantity, double limit, double stop)
	{
		if (!BrokerReady() || order == null || recoveryFault)
		{
			return;
		}
		RememberOrder(order, quantity, limit, stop);
		SaveRecovery();
		if (!recoveryFault)
		{
			if (recoveredOrderNames.Contains(order.Name))
			{
				order.QuantityChanged = quantity;
				order.LimitPriceChanged = limit;
				order.StopPriceChanged = stop;
				Account.Change(new Order[1] { order });
			}
			else
			{
				ChangeOrder(order, quantity, limit, stop);
			}
		}
	}

	private bool BrokerReady()
	{
		return State == State.Historical || (State == State.Realtime && !reconcilingRecovery && !recoveryPending && !recoveryFault && Account != null && Account.ConnectionStatus == ConnectionStatus.Connected);
	}

	private bool ObserveBrokerConnection(DateTime now)
	{
		if (State == State.Realtime && Account != null && Account.ConnectionStatus != ConnectionStatus.Connected && !recoveryPending)
		{
			recoveryPending = true;
			SaveRecovery();
		}
		DateTime utcNow = DateTime.UtcNow;
		if (utcNow >= recoveryNextPollUtc)
		{
			recoveryNextPollUtc = utcNow.AddSeconds(1.0);
			if (State == State.Realtime && recoveryReady && !recoveryFault && !recoveryPending && Account != null && Account.ConnectionStatus == ConnectionStatus.Connected && ((ownedNet != 0 && !RecoveryPositionConsistent()) || orders.Values.Any((Order o) => o != null && o.OrderState == OrderState.Unknown)))
			{
				WaitForRecoveryEvidence();
			}
			ReconcileRecovery();
		}
		if (!BrokerReady())
		{
			connectionPaused = true;
			recoveryFloor = now;
			return false;
		}
		if (connectionPaused)
		{
			connectionPaused = false;
			recoveryFloor = now;
			Print(Name + " connection restored; waiting for a fresh completed signal");
		}
		return true;
	}

	private Order SubmitOwnedOrder(int series, OrderAction action, OrderType type, int quantity, double limit, double stop, string oco, string signal)
	{
		return BrokerReady() ? RecoverySubmit(series, action, type, quantity, limit, stop, oco, signal) : null;
	}

	private void CancelOwnedOrder(Order order)
	{
		if (BrokerReady() && order != null)
		{
			RecoveryCancel(order);
		}
	}

	private void ChangeOwnedOrder(Order order, int quantity, double limit, double stop)
	{
		if (BrokerReady() && order != null)
		{
			RecoveryChange(order, quantity, limit, stop);
		}
	}

	protected override void OnBarUpdate()
	{
		if (State == State.Realtime && !ObserveBrokerConnection(NewYork(NativeNow())))
		{
			UpdatePanel();
			return;
		}
		if (BarsInProgress == 0)
		{
			if (CurrentBars[0] >= 0)
			{
				UpdatePanel();
			}
			return;
		}
		if (executionSeries == 2 && BarsInProgress == executionSeries)
		{
			int num = ((State != State.Historical) ? 1 : 0);
			if (CurrentBars[executionSeries] < num || (State != State.Historical && !IsFirstTickOfBar))
			{
				return;
			}
			DateTime dateTime = NewYork(Times[executionSeries][num]);
			if (dateTime <= lastExecutionMinute)
			{
				return;
			}
			lastExecutionMinute = dateTime;
			Trail(new Candle(dateTime.AddMinutes(-1.0), Opens[executionSeries][num], Highs[executionSeries][num], Lows[executionSeries][num], Closes[executionSeries][num]), dateTime);
			if (signalSeries != executionSeries)
			{
				return;
			}
		}
		if (BarsInProgress == signalSeries && CurrentBars[signalSeries] >= 0)
		{
			ConsumeSource();
		}
	}

	private void ConsumeSource()
	{
		if (historyRepair != null || State == State.Terminated)
		{
			return;
		}
		int num = ((State == State.Historical) ? CurrentBars[signalSeries] : (BarsArray[signalSeries].Count - 2));
		for (int i = consumedSourceIndex + 1; i <= num; i++)
		{
			DateTime dateTime = NewYork(BarsArray[signalSeries].GetTime(i));
			if (dateTime <= lastMinute)
			{
				consumedSourceIndex = i;
				continue;
			}
			DateTime dateTime2 = dateTime.AddMinutes(-1.0);
			bool flag = lastMinute != DateTime.MinValue && dateTime2 > lastMinute && lastMinute >= dateTime2.Date.AddHours(-1.0) && lastMinute < dateTime2.Date.AddMinutes(Cutoff(dateTime2.Date));
			if ((State == State.Realtime) & flag)
			{
				RequestSourceRepair((dateTime2 < dateTime2.Date.AddMinutes(Cutoff(dateTime2.Date))) ? dateTime2 : dateTime2.Date.AddMinutes(Cutoff(dateTime2.Date)));
				break;
			}
			DateTime dateTime3 = NewYork(NativeNow());
			bool fresh = State == State.Historical || (i == num && dateTime > recoveryFloor && dateTime >= new DateTime(dateTime3.Year, dateTime3.Month, dateTime3.Day, dateTime3.Hour, dateTime3.Minute, 0));
			ProcessSource(new Candle(dateTime2, BarsArray[signalSeries].GetOpen(i), BarsArray[signalSeries].GetHigh(i), BarsArray[signalSeries].GetLow(i), BarsArray[signalSeries].GetClose(i), BarsArray[signalSeries].GetVolume(i)), fresh);
			consumedSourceIndex = i;
			if (historyRepair != null)
			{
				break;
			}
		}
	}



	private void ProcessSource(Candle bar, bool fresh)
	{
		sourceFresh = fresh;
		DateTime end = bar.Start.AddMinutes(1.0);
		lastMinute = end;
		latestNewYork = end;
		minutes[bar.Start] = bar;
		DateTime[] expired = minutes.Keys.TakeWhile(k => k < end.AddDays(-5.0)).ToArray();
		foreach (DateTime key in expired) minutes.Remove(key);
		if (end.Date != sessionDay) ResetDay(end.Date);
		SaveRecovery();
		if (executionSeries == 1 && (State == State.Historical || fresh)) Trail(bar, end);
		ProcessSigmaM1(bar, end);
		if (end.TimeOfDay.TotalMinutes >= Cutoff(sessionDay)) BeginClose("Time");
		if (ShowVisuals) Levels(end);
		if (State == State.Realtime && !startupArmed)
		{
			startupFloor = end > startupFloor ? end : startupFloor;
			startupArmed = true;
			status = "Waiting for a fresh completed model signal";
		}
		if (fresh) sourceFresh = true;
		UpdatePanel();
	}

	private void RequestSourceRepair(DateTime missingRight, DateTime historicalLeft = default(DateTime))
	{
		visualHistoryWaiting = true;
		DateTime dateTime = NewYork(NativeNow());
		DateTime missingLeft = ((historicalLeft == DateTime.MinValue) ? lastMinute : historicalLeft);
		status = "Reloading provider M1 " + missingLeft.ToString("HH:mm") + " to " + missingRight.ToString("HH:mm") + " Central; new entries paused";
		UpdatePanel();
		if (historyRepair != null || dateTime < repairRetry)
		{
			return;
		}
		repairRetry = dateTime.AddSeconds(30.0);
		BarsRequest request = new BarsRequest(BarsArray[signalSeries].Instrument, 3000);
		request.BarsPeriod = new BarsPeriod
		{
			BarsPeriodType = BarsPeriodType.Minute,
			Value = 1
		};
		request.TradingHours = TradingHours.Get("CME US Index Futures ETH");
		request.LookupPolicy = LookupPolicies.Provider;
		historyRepair = request;
		Print("Model M1 reload: " + missingLeft.ToString("s") + " to " + missingRight.ToString("s") + " Central");
		try
		{
			request.Request((BarsRequest reply, ErrorCode error, string message) =>
			{
				if (State == State.Realtime && historyRepair == request)
				{
					List<Candle> recovered = new List<Candle>();
					if (error == ErrorCode.NoError)
					{
						for (int i = 0; i < reply.Bars.Count; i++)
						{
							DateTime dateTime2 = NewYork(reply.Bars.GetTime(i)).AddMinutes(-1.0);
							if (dateTime2 >= missingLeft && dateTime2 < missingRight)
							{
								recovered.Add(new Candle(dateTime2, reply.Bars.GetOpen(i), reply.Bars.GetHigh(i), reply.Bars.GetLow(i), reply.Bars.GetClose(i), reply.Bars.GetVolume(i)));
							}
						}
					}
					TriggerCustomEvent((object ignored) =>
					{
						if (historyRepair == request && State == State.Realtime)
						{
							historyRepair = null;
							request.Dispose();
							DateTime dateTime3 = missingLeft;
							bool flag = error == ErrorCode.NoError;
							foreach (Candle item in recovered)
							{
								if (item.Start != dateTime3)
								{
									flag = false;
									break;
								}
								dateTime3 = item.Start.AddMinutes(1.0);
							}
							if (!flag || dateTime3 != missingRight)
							{
								status = "Provider M1 unavailable at " + dateTime3.ToString("yyyy-MM-dd HH:mm") + " Central; retrying";
								Print("Model " + status + " " + message);
							}
							else
							{
								visualHistoryWaiting = false;
								recoveryFloor = NewYork(NativeNow());
								foreach (Candle item2 in recovered)
								{
									providerBars[item2.Start] = item2;
								}
								if (historicalLeft != DateTime.MinValue)
								{
									RebuildSource(recovered);
								}
								else
								{
									foreach (Candle item3 in recovered)
									{
										ProcessSource(item3, fresh: false);
									}
								}
								status = "M1 history recovered; waiting for a fresh M15 close";
								Print("Model M1 recovered " + recovered.Count + " bars; old signals suppressed");
								ConsumeSource();
							}
						}
					}, null);
				}
			});
		}
		catch (Exception ex)
		{
			historyRepair = null;
			request.Dispose();
			status = "Provider history request failed; retrying";
			Print("Model " + status + ": " + ex.Message);
		}
	}


	private void RebuildSource(List<Candle> recovered)
	{
		SortedDictionary<DateTime, Candle> all = new SortedDictionary<DateTime, Candle>();
		int last = BarsArray[signalSeries].Count - 2;
		for (int i = 0; i <= last; i++)
		{
			DateTime start = NewYork(BarsArray[signalSeries].GetTime(i)).AddMinutes(-1.0);
			all[start] = new Candle(start, BarsArray[signalSeries].GetOpen(i), BarsArray[signalSeries].GetHigh(i), BarsArray[signalSeries].GetLow(i), BarsArray[signalSeries].GetClose(i), BarsArray[signalSeries].GetVolume(i));
		}
		foreach (Candle c in providerBars.Values) all[c.Start] = c;
		minutes.Clear();
		ResetModelHistory();
		lastMinute = DateTime.MinValue;
		latestNewYork = DateTime.MinValue;
		sourceFresh = false;
		foreach (Candle c in all.Values)
		{
			DateTime end = c.Start.AddMinutes(1.0);
			if (end >= recoveryFloor.AddDays(-4.0)) minutes[c.Start] = c;
			ProcessSource(c, false);
		}
		consumedSourceIndex = last;
		startupFloor = recoveryFloor;
		startupArmed = false;
		status = "M1 history rebuilt; waiting for a fresh completed signal";
	}


	private void ResetDay(DateTime day)
	{
		sessionDay = day.Date;
		direction = 0;
		entryCount = 0;
		entryDirection = 0;
		firstStopDistance = 0.0;
		retryArmed = false;
		retryEligibleAt = DateTime.MinValue;
		targetFilled = false;
		stopExitFilled = false;
		

		pendingSigmaFlipSide = 0;
		pendingSigmaFlipSignalClose = 0;
		pendingSigmaFlipEndEt = DateTime.MinValue;
		rthOpen = 0;
		haveRthOpen = false;
		sigmaUpper = sigmaLower = sigmaValue = 0;
		sigmaReference = 0;
		sessionPv = sessionVolume = 0;
		previousCompletedVwap = 0;
		havePreviousVwap = false;
		sourceBucket = null;
		sourceBucketCount = 0;
		sourceBucketPv = sourceBucketVolume = 0;
		sourceBucketKeyUtc = sourceBucketKeyEt = DateTime.MinValue;
		if (ownedNet == 0 && !entryPending) { closing = false; closeReason = null; }
		if (ownedNet != 0 || entryPending) { suspended = true; BeginClose("SessionBoundary"); }
		modelHistoryStatus = "Waiting for current session window";
	}

	private bool Window(DateTime from, DateTime to, out Candle aggregate)
	{
		aggregate = null;
		DateTime dateTime = from;
		while (dateTime < to)
		{
			if (!minutes.TryGetValue(dateTime, out var value))
			{
				if (State == State.Realtime && historyRepair == null)
				{
					RequestSourceRepair(to, dateTime);
				}
				return false;
			}
			if (aggregate == null)
			{
				aggregate = value.Clone();
			}
			else
			{
				aggregate.Merge(value);
			}
			dateTime = dateTime.AddMinutes(1.0);
		}
		return aggregate != null;
	}










	private bool CanReplayCompletedSession(DateTime day)
	{
		if (State != State.Historical || IsInStrategyAnalyzer) return true;
		Bars bars = BarsArray[executionSeries];
		return day != DateTime.MinValue && bars.Count > 0 && NewYork(bars.GetTime(bars.Count - 1)) >= day.Date.AddMinutes(Cutoff(day) + 2);
	}




	private bool FinitePositive(double x) => x > 0 && !double.IsNaN(x) && !double.IsInfinity(x);
	private string StrategyPrefix() => "Sigma" + (string.IsNullOrWhiteSpace(InstanceId) || InstanceId == "default" ? "" : "_" + InstanceId);
	private string StrategyFolder() => Name.Replace(" ", "");
	private DateTime EasternToUtc(DateTime eastern) => TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(eastern, DateTimeKind.Unspecified), nyZone);
	private DateTime UtcToEastern(DateTime utc) => TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), nyZone);
	private int FlatMinute => Cutoff(sessionDay);
	private bool RequiresStopProtection() => SlPct > 0.0;
	private bool RequiresTargetProtection() => false;
	private bool HasVariableRiskSizing() => SlPct > 0.0;
	private double GetSizingEquity()
	{
		try { if (Account != null) { double eq = Account.Get(AccountItem.NetLiquidation, Currency.UsDollar); if (FinitePositive(eq)) return eq; } } catch { }
		return IsInStrategyAnalyzer && FinitePositive(InitialSizingEquity) ? InitialSizingEquity : 0.0;
	}
	private void ResetModelHistory()
	{
		sigmaSlotHistory.Clear(); sessionDay = DateTime.MinValue; sourceBucket = null; sourceBucketCount = 0;
		sourceBucketPv = sourceBucketVolume = 0.0;
		rthOpen = 0; haveRthOpen = false;
		sessionPv = sessionVolume = 0; previousCompletedVwap = 0; havePreviousVwap = false;
	}

	private int CalculateSigmaQuantity(double entryQuote, int side, double customStopDistance = 0.0)
	{
		if (SlPct <= 0.0 && customStopDistance <= 0.0) return FixedLot;
		double stopDistance = customStopDistance > 0.0 ? customStopDistance : entryQuote * SlPct / 100.0;
		if (!FinitePositive(stopDistance)) return 0;
		plannedStop = Round(entryQuote - side * stopDistance);
		plannedTarget = 0.0;
		double perContract = Math.Abs(entryQuote - plannedStop) * ExecutionInstrument.MasterInstrument.PointValue + CostPerContract;
		if (!FinitePositive(perContract)) return 0;
		double riskCash = FixedRiskUsd > 0 ? FixedRiskUsd : GetSizingEquity() * RiskPct / 100.0;
		if (!FinitePositive(riskCash)) return 0;
		int qty = (int)Math.Min(int.MaxValue, Math.Floor(riskCash / perContract));
		if (qty < 1 && AllowMinimumOneContract) qty = 1;
		if (qty < 1) return 0;
		if (SkipSetupsAboveRiskBudget && qty * perContract > RiskBudgetUsd + 0.01)
		{
			skippedByRiskFilter++;
			Record("SKIP_RISK_BUDGET", latestNewYork, "qty=" + qty + " risk=" + F(qty * perContract) + " cap=" + F(RiskBudgetUsd));
			return 0;
		}
		return qty;
	}

	private void ProcessSigmaM1(Candle bar, DateTime end)
	{
		int minute = bar.Start.Hour * 60 + bar.Start.Minute;
		if (minute == 570)
		{
			rthOpen = bar.Open;
			haveRthOpen = true;
			sessionPv = sessionVolume = 0;
			previousCompletedVwap = rthOpen;
			havePreviousVwap = false;
		}
		if (!haveRthOpen || minute < 570 || minute >= 960) return;
		DateTime open = sessionDay.Date.AddHours(9).AddMinutes(30);
		int offset = minute - 570;
		int slotOffset = (offset / 30) * 30;
		DateTime key = open.AddMinutes(slotOffset);
		if (sourceBucket == null || sourceBucketKeyEt != key)
		{
			sourceBucket = bar.Clone();
			sourceBucketKeyEt = key;
			sourceBucketKeyUtc = EasternToUtc(key);
			sourceBucketCount = 1;
			sourceBucketPv = ((bar.High + bar.Low + bar.Close) / 3.0) * bar.Volume;
			sourceBucketVolume = bar.Volume;
		}
		else
		{
			sourceBucket.Merge(bar);
			sourceBucketCount++;
			sourceBucketPv += ((bar.High + bar.Low + bar.Close) / 3.0) * bar.Volume;
			sourceBucketVolume += bar.Volume;
		}
		if (end != key.AddMinutes(30)) return;
		if (sourceBucketCount == 30) ProcessSigmaM30(sourceBucket.Clone(), key, end);
		else modelHistoryStatus = "Incomplete M30 slot; waiting for repaired M1 history";
		sourceBucket = null;
		sourceBucketCount = 0;
		sourceBucketPv = sourceBucketVolume = 0;
	}

	private void ProcessSigmaM30(Candle bar, DateTime slotStartEt, DateTime endEt)
	{
		int slot = slotStartEt.Hour * 100 + slotStartEt.Minute;
		if (!sigmaSlotHistory.TryGetValue(slot, out Queue<double> hist)) sigmaSlotHistory[slot] = hist = new Queue<double>();
		sigmaValue = hist.Count == NSigmaDays ? hist.Average() : 0.0;
		sigmaReference = UseVwapReference ? (havePreviousVwap ? previousCompletedVwap : rthOpen) : rthOpen;
		bool ready = hist.Count >= NSigmaDays && sigmaValue > 0 && FinitePositive(sigmaReference);
		if (ready)
		{
			sigmaUpper = sigmaReference + BandMultiplier * sigmaValue;
			sigmaLower = sigmaReference - BandMultiplier * sigmaValue;
			modelHistoryStatus = "Slot " + slot.ToString("0000") + " ready " + hist.Count + "/" + NSigmaDays;
			if (sourceFresh && (State != State.Realtime || (startupArmed && endEt > startupFloor)) && CanReplayCompletedSession(sessionDay) && endEt.TimeOfDay.TotalMinutes < 944)
			{
				int side = bar.Close > sigmaUpper ? 1 : (bar.Close < sigmaLower ? -1 : 0);
				if (side != 0)
				{
					if (ownedNet != 0)
					{
						if (Math.Sign(ownedNet) != side)
						{
							pendingSigmaFlipSide = side;
							pendingSigmaFlipSignalClose = bar.Close;
							pendingSigmaFlipEndEt = endEt;
							SaveRecovery();
							BeginClose("OppositeSigmaBandBreak");
						}
						else if (pendingSigmaFlipSide != 0)
						{
							pendingSigmaFlipSide = 0;
							pendingSigmaFlipSignalClose = 0;
							pendingSigmaFlipEndEt = DateTime.MinValue;
							SaveRecovery();
						}
					}
					else if (!entryPending && !closing && EnableTrading)
					{
						if (entryCount == 0)
							SubmitSigmaEntry(side, bar.Close, endEt);
						else if (entryCount == 1 && retryArmed && side == entryDirection && SlPct > 0.0 && firstStopDistance > 0.0 && !targetFilled && endEt > retryEligibleAt)
							SubmitSigmaEntry(side, bar.Close, endEt, true);
					}
				}
			}
		}
		else
		{
			modelHistoryStatus = "Slot " + slot.ToString("0000") + " warming " + hist.Count + "/" + NSigmaDays;
			sigmaUpper = sigmaLower = 0;
		}
		// Only after the current slot's signal decision: current day must never enter its own band history.
		hist.Enqueue(Math.Abs(bar.Close - rthOpen));
		while (hist.Count > NSigmaDays) hist.Dequeue();
		if (sourceBucketVolume > 0)
		{
			sessionPv += sourceBucketPv;
			sessionVolume += sourceBucketVolume;
			if (sessionVolume > 0) { previousCompletedVwap = sessionPv / sessionVolume; havePreviousVwap = true; }
		}
	}

	private void SubmitSigmaEntry(int side, double signalClose, DateTime signalEndEt, bool retry = false)
	{
		if (signalEndEt.Date != sessionDay || signalEndEt.TimeOfDay.TotalMinutes >= 944 || entryPending || ownedNet != 0 || closing) return;
		if (retry && (entryCount != 1 || !retryArmed || side != entryDirection || SlPct <= 0.0 || firstStopDistance <= 0.0 || targetFilled || signalEndEt <= retryEligibleAt)) return;
		if (CurrentBars[executionSeries] < 0) { status = "Waiting for M1 execution history"; return; }
		double quote = State == State.Realtime ? (side > 0 ? GetCurrentAsk(executionSeries) : GetCurrentBid(executionSeries)) : signalClose;
		if (!FinitePositive(quote)) return;
		int qty = CalculateSigmaQuantity(quote, side, retry ? firstStopDistance * RetryStopFraction : 0.0);
		if (qty < 1) { status = "Sigma setup skipped by sizing/risk filter"; return; }
		if (entryCount == 0)
		{
			entryDirection = side;
			firstStopDistance = 0.0;
			retryArmed = false;
			retryEligibleAt = DateTime.MinValue;
		}
		if (retry)
		{
			retryArmed = false;
			retryEligibleAt = DateTime.MinValue;
		}
		lastSignalClose = signalClose;
		currentTradeSide = side;
		entryQuoteAtSignal = quote;
		tradeModeledCost = CostPerContract;
		if (SlPct <= 0) { plannedStop = 0; plannedTarget = 0; }
		entryPending = true;
		entryCount++;
		targetFilled = false;
		stopExitFilled = false;
		entered = exited = ownedNet = 0;
		entrySum = exitSum = actualFees = peakR = 0;
		brackets.Clear();
		closeReason = null;
		recoveredCommissionFloor = 0;
		string signal = owner + "_Entry_" + (++sequence);
		entryName = signal;
		status = "Sigma entry submitted";
		if (State != State.Realtime || AcquireExposure(side))
			SubmitOwnedOrder(executionSeries, side > 0 ? OrderAction.Buy : OrderAction.SellShort, OrderType.Market, qty, 0, 0, "", entryName);
		else { entryPending = false; status = "Sigma exposure reservation refused"; }
		Record("SIGMA_SIGNAL", signalEndEt, "slot=" + F(slotStartValue(signalEndEt)) + " close=" + F(signalClose) + " upper=" + F(sigmaUpper) + " lower=" + F(sigmaLower) + " side=" + side + " qty=" + qty);
	}
	private double slotStartValue(DateTime t) => t.Hour * 100 + t.Minute;


	private void ObserveFee(Execution execution, string executionId, int quantity, bool isEntry)
	{
		if (!CanLearnFee || string.IsNullOrEmpty(executionId) || quantity <= 0)
		{
			return;
		}
		lock (FeeGate)
		{
			if (!FeeHistory.TryGetValue(FeeScope, out var value))
			{
				value = (FeeHistory[FeeScope] = new List<FeeTrade>());
			}
			FeeTrade feeTrade = value.FirstOrDefault((FeeTrade t) => t.Slices.ContainsKey(executionId));
			if (feeTrade != null)
			{
				feeTrade.Slices[executionId].Execution = execution;
				return;
			}
			if (isEntry && (this.feeTrade == null || this.feeTrade.Signal != entryName))
			{
				this.feeTrade = new FeeTrade
				{
					Signal = entryName
				};
				value.Add(this.feeTrade);
				if (value.Count > 32)
				{
					value.RemoveAt(0);
				}
			}
			if (this.feeTrade != null && !this.feeTrade.Closed)
			{
				this.feeTrade.Slices[executionId] = new FeeSlice
				{
					Execution = execution,
					Quantity = quantity,
					Entry = isEntry
				};
			}
		}
	}

	internal static double FillRiskCost(double reservedCost, double entryFees, int entryQuantity, double twoWaySlippage)
	{
		return (entryQuantity > 0) ? Math.Max(reservedCost, 2.0 * entryFees / (double)entryQuantity + twoWaySlippage) : reservedCost;
	}


	private void CheckActualFillRisk()
	{
		if (!((SlPct > 0.0 && SkipSetupsAboveRiskBudget)) || entered <= 0 || ownedNet == 0 || closing || initialRisk <= 0) return;
		double entryFees = 0;
		int entryQuantity = 0;
		if (CanLearnFee && feeTrade != null)
		{
			lock (FeeGate)
			{
				entryQuantity = feeTrade.EntryQuantity;
				entryFees = feeTrade.Slices.Values.Where(x => x.Entry).Sum(x => FeeTrade.Charge(x.Execution));
			}
		}
		double reserve = FillRiskCost(tradeModeledCost, entryFees, entryQuantity, 2.0 * SlippagePrice * ExecutionInstrument.MasterInstrument.PointValue);
		double actual = entered * (initialRisk * ExecutionInstrument.MasterInstrument.PointValue + reserve);
		if (actual > RiskBudgetUsd + 0.01)
		{
			suspended = true;
			BeginClose("ActualFillRiskExceeded");
		}
	}

	private double Round(double p)
	{
		return ExecutionInstrument.MasterInstrument.RoundToTickSize(p);
	}

	internal static int Bias(double open, double low, double high)
	{
		if (high <= low || open <= low || open >= high)
		{
			return 0;
		}
		return (open > low + 2.0 * (high - low) / 3.0) ? 1 : ((open < low + (high - low) / 3.0) ? (-1) : 0);
	}

	internal static bool SignalTime(double minute)
	{
		return minute > 540.0;
	}

	internal static int SignalSide(int side, double close, double high, double low, double trend)
	{
		return (!(trend <= 20.0)) ? ((side == 1 && close > high) ? 1 : ((side == -1 && close < low) ? (-1) : 0)) : 0;
	}

	internal static double TrailLock(double peak)
	{
		return (peak < 1.5) ? double.NaN : (Math.Floor((peak - 1.5) / 0.5 + 1E-09) * 0.5);
	}

	internal static int Quantity(double budget, double unit)
	{
		return (!(unit <= 0.0) && !double.IsNaN(unit) && !double.IsInfinity(unit)) ? ((int)Math.Min(2147483647.0, Math.Floor(budget / unit + 1E-09))) : 0;
	}

	internal static double CoveredStop(int side, double entry, double commission, double pointValue, double slip, double tick)
	{
		double num = entry + (double)side * (commission / pointValue + slip);
		return (side > 0) ? (Math.Ceiling(num / tick - 1E-09) * tick) : (Math.Floor(num / tick + 1E-09) * tick);
	}

	internal static double BracketPrice(int side, double quote, double distance, bool target)
	{
		return quote + (double)side * distance * (double)(target ? 3 : (-1));
	}


	private void Trail(Candle bar, DateTime end)
	{
		// Sigma N3/1.7 has no stop by default. If SlPct is enabled, the broker-held
		// initial stop remains active; this candidate does not add a profit trail.
		return;
	}

	private void MoveRetryBreakEven(Candle bar)
	{
		int side = Math.Sign(ownedNet);
		double entry = entrySum / (double)entered;
		peakR = Math.Max(peakR, (double)side * (bar.Close - entry) / initialRisk);
		if (peakR < 0.5)
		{
			return;
		}
		double commission = Math.Max(EffectiveCommission, recoveredCommissionFloor);
		if (CanLearnFee && feeTrade != null && feeTrade.EntryQuantity > 0)
		{
			commission = Math.Max(commission, 2.0 * feeTrade.Slices.Values.Where((FeeSlice x) => x.Entry).Sum((FeeSlice x) => FeeTrade.Charge(x.Execution)) / (double)feeTrade.EntryQuantity);
		}
		recoveredCommissionFloor = Math.Max(recoveredCommissionFloor, commission);
		double stop = CoveredStop(side, entry, CoverCommission(commission), ExecutionInstrument.MasterInstrument.PointValue, CoverSlippagePerSide * (double)((State != State.Historical) ? 1 : 2), ExecutionInstrument.MasterInstrument.TickSize);
		double quote = (State == State.Realtime) ? ((side > 0) ? GetCurrentBid(executionSeries) : GetCurrentAsk(executionSeries)) : bar.Close;
		if ((double)side * (quote - stop) <= 0.0)
		{
			BeginClose("RetryBreakEvenCrossed");
			return;
		}
		foreach (Bracket bracket in brackets)
		{
			if (bracket.Stop != null && !Settled(bracket.Stop) && (double)side * (stop - bracket.Stop.StopPrice) > 0.0 && (bracket.Stop.OrderState == OrderState.Working || bracket.Stop.OrderState == OrderState.Accepted))
			{
				ChangeOwnedOrder(bracket.Stop, bracket.Stop.Quantity, 0.0, stop);
			}
		}
	}

	private static bool Settled(Order o)
	{
		return o == null || o.OrderState == OrderState.Filled || o.OrderState == OrderState.Cancelled || o.OrderState == OrderState.Rejected;
	}

	private bool ExecutionsCaughtUp(Order o)
	{
		if (o == null)
		{
			return true;
		}
		processed.TryGetValue(o.Name, out var value);
		reportedFilled.TryGetValue(o.Name, out var value2);
		return value >= Math.Max(o.Filled, value2);
	}

	protected override void OnOrderUpdate(Order order, double limitPrice, double stopPrice, int quantity, int filled, double averageFillPrice, OrderState orderState, DateTime time, ErrorCode error, string nativeError)
	{
		if (owner == null || !order.Name.StartsWith(owner + "_"))
		{
			return;
		}
		orders[order.Name] = order;
		reportedFilled.TryGetValue(order.Name, out var value);
		reportedFilled[order.Name] = Math.Max(value, filled);
		RememberOrder(order, quantity, limitPrice, stopPrice);
		if (order.Name == pendingCloseName && (orderState == OrderState.Filled || orderState == OrderState.Cancelled || orderState == OrderState.Rejected))
		{
			pendingCloseFinal = ((orderState == OrderState.Filled) ? pendingCloseQuantity : filled);
		}
		foreach (Bracket bracket2 in brackets)
		{
			if (order.Name == bracket2.StopName)
			{
				bracket2.Stop = order;
			}
			if (order.Name == bracket2.TargetName)
			{
				bracket2.Target = order;
			}
		}
		if (!closing && !repairingProtection && !recoveryFault && orderState == OrderState.Cancelled && ownedNet != 0)
		{
			Bracket bracket = brackets.FirstOrDefault((Bracket b) => b.StopName == order.Name || b.TargetName == order.Name);
			if (bracket != null)
			{
				Order order2 = ((bracket.StopName == order.Name) ? bracket.Target : bracket.Stop);
				if (order2 == null || order2.Filled < order2.Quantity)
				{
					if (State == State.Realtime)
					{
						WaitForRecoveryEvidence();
					}
					else
					{
						suspended = true;
						BeginClose("ProtectionCancelled");
					}
				}
			}
		}
		if ((error != ErrorCode.NoError || orderState == OrderState.Rejected) &&
			!string.IsNullOrEmpty(nativeError) &&
			(nativeError.IndexOf("liquidation only", StringComparison.OrdinalIgnoreCase) >= 0 ||
			 nativeError.IndexOf("liquidation-only", StringComparison.OrdinalIgnoreCase) >= 0 ||
			 nativeError.IndexOf("low net liquidating value", StringComparison.OrdinalIgnoreCase) >= 0))
		{
			// An account restriction cannot be repaired with another close/replace request.
			// Persist the entry latch before faulting; retain any accepted broker protection.
			suspended = true;
			closeReason = "BrokerAccountRestricted: " + nativeError + " | priorCloseReason=" + (closeReason ?? "");
			SaveRecovery();
			if (ownedNet == 0 && recoveryRequests.Keys.All(name => orders.ContainsKey(name)) && orders.Values.All(o => Settled(o) && ExecutionsCaughtUp(o)))
				ReleaseExposure();
			RecoveryBlock("Broker account restricted; new orders paused; verify broker position/protection: " + nativeError);
			return;
		}
		if ((error != ErrorCode.NoError || orderState == OrderState.Rejected) && order.Name.Contains("_Repair"))
		{
			if (!reconcilingRecovery || orderState != OrderState.Rejected || filled != 0 || order.Filled != 0)
			{
				RecoveryBlock("Broker rejected recovered protection: " + order.Name + " id=" + order.OrderId + " state=" + orderState.ToString() + " error=" + error.ToString() + " OCO=" + order.Oco + " detail=" + nativeError);
			}
			return;
		}
		if (error != ErrorCode.NoError || orderState == OrderState.Rejected)
		{
			if (order.Name == pendingCloseName)
			{
				closeRejected = true;
			}
			suspended = true;
			Print(Name + " owned order error: " + error.ToString() + " " + nativeError);
			BeginClose("OrderError");
		}
		SaveRecovery();
		DriveClose();
		if (closeRejected && order.Name == pendingCloseName)
		{
			RepairOwnedProtection();
		}
		if (entered > 0 && ownedNet == 0 && !entryPending && orders.Values.All((Order o) => Settled(o) && ExecutionsCaughtUp(o)))
		{
			Complete(NewYork(time));
		}
	}


	protected override void OnExecutionUpdate(Execution execution, string executionId, double price, int quantity, MarketPosition marketPosition, string orderId, DateTime time)
	{
		if (execution == null || execution.Order == null || owner == null || !execution.Order.Name.StartsWith(owner + "_", StringComparison.Ordinal) || ReconcileOwnedExecution(execution, executionId)) return;
		ObserveFee(execution, executionId, quantity, execution.Order.Name == entryName);
		if (!executions.Add(executionId)) { CheckActualFillRisk(); return; }
		RememberExecution(execution.Order, quantity, price);
		Order order = execution.Order;
		processed.TryGetValue(order.Name, out int priorProcessed);
		processed[order.Name] = priorProcessed + quantity;
		if (order.Name == pendingCloseName) pendingCloseExecuted += quantity;
		int side = (order.OrderAction == OrderAction.Buy || order.OrderAction == OrderAction.BuyToCover) ? 1 : -1;
		ownedNet += side * quantity;
		actualFees += FeeTrade.Charge(execution);
		if (order.Name == entryName)
		{
			currentTradeSide = side;
			entered += quantity;
			entrySum += price * quantity;
			double avgEntry = entrySum / entered;
			bool stopRequired = RequiresStopProtection();
			bool targetRequired = RequiresTargetProtection();
			initialRisk = stopRequired ? Math.Abs(avgEntry - plannedStop) : 0.0;
			if (entryCount == 1) firstStopDistance = (SlPct > 0.0 && stopRequired) ? initialRisk : 0.0;
			if ((stopRequired && (initialRisk <= 0 || side * (price - plannedStop) <= 0)) || (targetRequired && side * (plannedTarget - price) <= 0))
			{
				suspended = true;
				BeginClose("InvalidEntryFill");
			}
			else if (!closing && (stopRequired || targetRequired))
			{
				Bracket bracket = new Bracket();
				if (stopRequired) bracket.StopName = owner + "_Stop_" + (++sequence);
				if (targetRequired) bracket.TargetName = owner + "_Target_" + (++sequence);
				brackets.Add(bracket);
				string oco = owner + "_OCO_" + sequence;
				if (stopRequired)
					bracket.Stop = SubmitOwnedOrder(executionSeries, side > 0 ? OrderAction.Sell : OrderAction.BuyToCover, OrderType.StopMarket, quantity, 0.0, plannedStop, oco, bracket.StopName);
				if (targetRequired && !closing)
					bracket.Target = SubmitOwnedOrder(executionSeries, side > 0 ? OrderAction.Sell : OrderAction.BuyToCover, OrderType.Limit, quantity, plannedTarget, 0.0, oco, bracket.TargetName);
			}
			if (executionVisual == null)
			{
				executionVisual = new ExecutionView { Tag = entryName + "_EXEC", Side = side, InitialStop = stopRequired ? plannedStop : 0.0,
					Stop = stopRequired ? plannedStop : 0.0, InitialTarget = targetRequired ? plannedTarget : 0.0, Entered = NewYork(time) };
			}
			executionVisual.InitialTarget = targetRequired ? plannedTarget : 0.0;
			executionVisual.Entry = avgEntry;
			DrawExecution(executionVisual, NewYork(time));
			CheckActualFillRisk();
			Record("ENTRY", NewYork(time), "price=" + F(price) + " qty=" + quantity + ";tradeId=" + entryName + ";executionId=" + executionId + ";side=" + side + ";stop=" + F(plannedStop) + ";target=" + F(plannedTarget));
			status = (closing ? "Closing owned position" : ((stopRequired || targetRequired) ? "Owned position protected" : "Owned position tracked; no stop configured"));
		}
		else
		{
			if (brackets.Any(b => b.StopName == order.Name)) stopExitFilled = true;
			if (brackets.Any(b => b.TargetName == order.Name)) targetFilled = true;
			exited += quantity;
			exitSum += price * quantity;
			Record("EXIT", NewYork(time), order.Name + " price=" + F(price) + " qty=" + quantity + ";tradeId=" + entryName + ";executionId=" + executionId);
			if (ownedNet != 0 && !closing) BeginClose("PartialExit");
		}
		if (orders.TryGetValue(entryName ?? "", out Order entryOrder) && Settled(entryOrder) && ExecutionsCaughtUp(entryOrder)) entryPending = false;
		if (exited > entered) { suspended = true; BeginClose("OcoRace"); }
		DriveClose();
		if (entered > 0 && ownedNet == 0 && !entryPending && orders.Values.All(o => Settled(o) && ExecutionsCaughtUp(o))) Complete(NewYork(time));
	}


	private void Complete(DateTime time)
	{
		bool completedByStop = stopExitFilled;
		string completedCloseReason = closeReason;
		if (feeTrade != null) feeTrade.Closed = feeTrade.EntryQuantity > 0 && feeTrade.EntryQuantity == feeTrade.ExitQuantity;
		if (executionVisual != null)
		{
			executionVisual.Exit = (exited > 0) ? exitSum / exited : entrySum / Math.Max(1, entered);
			executionVisual.Exited = time;
			closedVisuals.Add(executionVisual);
			historicalVisualsPainted = false;
			DrawExecution(executionVisual, time);
			executionVisual = null;
		}
		double gross = currentTradeSide * (exitSum - entrySum) * ExecutionInstrument.MasterInstrument.PointValue;
		double modeledNet = gross - entered * tradeModeledCost;
		Print(Name + " trade closed: gross=" + F(gross) + " modeled net=" + F(modeledNet) + " actual commission=" + F(actualFees));
		Record("CLOSE", time, "gross=" + F(gross) + " modeledNet=" + F(modeledNet));
		entered = exited = 0;
		brackets.Clear();
		closing = false;
		closeReason = null;
		status = "Trade closed; waiting for next valid signal";
		currentTradeSide = 0;
		initialRisk = 0;
		plannedStop = plannedTarget = 0;
		if (completedByStop) ArmSameSideRetry(time, true, modeledNet < 0.0, completedCloseReason);
		stopExitFilled = false;

		int flipSide = pendingSigmaFlipSide;
		double flipClose = pendingSigmaFlipSignalClose;
		DateTime flipEnd = pendingSigmaFlipEndEt;
		pendingSigmaFlipSide = 0;
		pendingSigmaFlipSignalClose = 0;
		pendingSigmaFlipEndEt = DateTime.MinValue;
		ReleaseExposure();
		SaveRecovery();
		// Opposite-band break is a true flip: reopen only after the old owned
		// position and every bracket leg have fully reconciled.
		if (flipSide != 0 && EnableTrading && !suspended && time.Date == flipEnd.Date
			&& flipEnd.TimeOfDay.TotalMinutes < Cutoff(flipEnd.Date)
			&& time.TimeOfDay.TotalMinutes < Cutoff(time.Date))
			SubmitSigmaEntry(flipSide, flipClose, flipEnd);
	}

	private void ArmSameSideRetry(DateTime time, bool stopExit, bool losingExit, string exitCloseReason)
	{
		// Only a losing protective-stop exit qualifies. Time, reversal, manual, partial, and recovery closes never arm a retry.
		retryArmed = stopExit && SlPct > 0.0 && firstStopDistance > 0.0 && entryCount == 1 && entryDirection != 0 && losingExit && !targetFilled && string.IsNullOrEmpty(exitCloseReason);
		retryEligibleAt = retryArmed ? time : DateTime.MinValue;
		if (retryArmed) Record("RETRY_ARMED", time, "side=" + entryDirection + ";firstStopDistance=" + F(firstStopDistance) + ";retryStopDistance=" + F(firstStopDistance * RetryStopFraction));
	}

	private void BeginClose(string reason)
	{
		closing = true;
		closeReason = reason;
		SaveRecovery();
		DriveClose();
	}

	private void DriveClose()
	{
		if (!BrokerReady() || !closing || closingDrive || closeRejected)
		{
			return;
		}
		closingDrive = true;
		try
		{
			if (ownedNet != 0 && orders.Values.All(ExecutionsCaughtUp) && !RecoveryPositionConsistent())
			{
				WaitForRecoveryEvidence();
				return;
			}
			Order[] array = orders.Values.ToArray();
			foreach (Order order in array)
			{
				if (!Settled(order) && (order.Name == entryName || order.OrderType != OrderType.Market) && order.OrderState != OrderState.CancelPending && order.OrderState != OrderState.CancelSubmitted)
				{
					CancelOwnedOrder(order);
				}
			}
			if (orders.Values.Any((Order o) => !Settled(o) || !ExecutionsCaughtUp(o)))
			{
				return;
			}
			entryPending = false;
			if (ownedNet == 0)
			{
				ReleaseExposure();
			}
			else if (pendingCloseName == null || (pendingCloseFinal >= 0 && pendingCloseExecuted >= pendingCloseFinal))
			{
				if (!RecoveryPositionConsistent())
				{
					WaitForRecoveryEvidence();
					return;
				}
				string text = owner;
				int num = ++sequence;
				pendingCloseName = text + "_Close_" + num;
				pendingCloseQuantity = Math.Abs(ownedNet);
				pendingCloseExecuted = 0;
				pendingCloseFinal = -1;
				SubmitOwnedOrder(executionSeries, (ownedNet <= 0) ? OrderAction.BuyToCover : OrderAction.Sell, OrderType.Market, Math.Abs(ownedNet), 0.0, 0.0, string.Empty, pendingCloseName);
			}
		}
		finally
		{
			closingDrive = false;
		}
	}

	private bool AcquireExposure(int side)
	{
		if (State != State.Realtime)
			return true;
		// Durable executor/instance leases remain separate from this direction guard.
		// Same-direction strategies may share a contract; opposite netting is always blocked.
		if (!ProductExposure.Reserve(owner, side, Account, ExecutionInstrument, BlockOpposingExposure, out var reason))
		{
			status = reason;
			return false;
		}
		return true;
	}

	private void ReleaseExposure(string releasingOwner = null)
	{
		string text = releasingOwner ?? owner;
		if (exposureKey != null && text != null)
		{
			ExposureRelease(exposureKey, text);
			ProductExposure.Release(text);
		}
	}

	internal static bool ExposureAcquire(string key, string ownerId)
	{
		lock (AppDomain.CurrentDomain)
		{
			Dictionary<string, string> dictionary = AppDomain.CurrentDomain.GetData("NQIndependentModelExposure.v1") as Dictionary<string, string>;
			if (dictionary == null)
			{
				dictionary = new Dictionary<string, string>();
				AppDomain.CurrentDomain.SetData("NQIndependentModelExposure.v1", dictionary);
			}
			if (dictionary.TryGetValue(key, out var value))
			{
				return value == ownerId;
			}
			dictionary[key] = ownerId;
			return true;
		}
	}

	internal static void ExposureRelease(string key, string ownerId)
	{
		lock (AppDomain.CurrentDomain)
		{
			if (AppDomain.CurrentDomain.GetData("NQIndependentModelExposure.v1") is Dictionary<string, string> dictionary && dictionary.TryGetValue(key, out var value) && value == ownerId)
			{
				dictionary.Remove(key);
			}
		}
	}


	private void TimerStep()
	{
		if (State != State.Realtime) return;
		DateTime now = NewYork(NativeNow());
		bool ready = ObserveBrokerConnection(now);
		if (ownedNet != 0 && now >= sessionDay.AddMinutes(Cutoff(sessionDay))) BeginClose("Time");
		if (ready)
		{
			CheckActualFillRisk();
			DriveClose();
			ConsumeSource();
		}
		UpdatePanel();
	}

	private void Record(string evt, DateTime ct, string detail)
	{
		if (log != null)
		{
			log.WriteLine(evt + "," + ct.ToString("yyyy-MM-dd HH:mm:ss") + "," + detail.Replace(",", ";"));
		}
	}

	private static string F(double value)
	{
		return value.ToString("0.########", CultureInfo.InvariantCulture);
	}


	private void UpdatePanel()
	{
		SyncFaultVisuals();
		if (!ShowVisuals) return;
		DateTime nowEt = (State == State.Realtime) ? NewYork(NativeNow()) : latestNewYork;
		DateTime nowUtc = nowEt == DateTime.MinValue ? DateTime.MinValue : EasternToUtc(nowEt);
		PaintHistoricalExecutions(nowEt);
		dashboard.Publish(new ModelDashState
		{
			Title = "Sigma Breakout | N3 / 1.7",
			Clock = "NY " + nowEt.ToString("HH:mm:ss") + " | UTC " + (nowUtc == DateTime.MinValue ? "--" : nowUtc.ToString("HH:mm:ss")),
			Status = VisualFaultReason() ?? status,
			NewsBlocked = connectionPaused || suspended,
			RangeCaption = "SIGMA BANDS | M30",
			HighCaption = "HIGH / UPPER",
			LowCaption = "LOW / LOWER",
			RangeHigh = ((sigmaUpper > 0) ? F(sigmaUpper) : "--"),
			RangeLow = ((sigmaLower > 0) ? F(sigmaLower) : "--"),
			RangeWidth = ((sigmaValue > 0) ? F(2.0 * BandMultiplier * sigmaValue) : "--"),
			InTrade = ownedNet != 0 && !recoveryPending && !recoveryFault,
			Position = (recoveryPending || recoveryFault) ? "UNVERIFIED" : (ownedNet == 0 ? "FLAT" : (Math.Abs(ownedNet) + (ownedNet > 0 ? " LONG" : " SHORT"))),
			Stop = (!recoveryPending && !recoveryFault && brackets.Count > 0 && brackets[0].Stop != null) ? F(brackets[0].Stop.StopPrice) : (plannedStop > 0 ? F(plannedStop) : "--"),
			Risk = (SlPct == 0 ? ("Fixed lot " + FixedLot) : ("SL " + F(SlPct) + "% | " + (FixedRiskUsd > 0 ? ("Fixed risk $" + F(FixedRiskUsd)) : ("Risk " + F(RiskPct) + "%")))) + (SkipSetupsAboveRiskBudget && SlPct > 0 ? " | Cap $" + F(RiskBudgetUsd) : " | Cap OFF") + " | Entries " + entryCount + " | Skipped " + skippedByRiskFilter,
			Costs = "Commission/contract $" + F(EffectiveCommission) + " | Slip/side " + SlippagePoints,
			History = "N=" + NSigmaDays + " / B=" + F(BandMultiplier) + " | " + modelHistoryStatus
		});
		if (State == State.Realtime) RequestDashboardRender();
	}




	private void Level(string tag, DateTime left, DateTime right, double price, System.Windows.Media.Brush brush)
	{
		if (!VisualsSuppressed && right > left && price > 0)
			Draw.Line(this, StrategyPrefix() + "_" + sessionDay.ToString("yyyyMMdd") + "_" + tag, false, Platform(left), price, Platform(right), price, brush, DashStyleHelper.Dash, 1);
	}

	private void Levels(DateTime now)
	{
		DateTime right = sessionDay.AddMinutes(Cutoff(sessionDay));

		if (haveRthOpen)
		{
			Level("RTH open", sessionDay.AddHours(9.5), right, rthOpen, Brushes.Goldenrod);
			if (sigmaUpper > 0 && sigmaLower > 0)
			{
				Level("Sigma upper", sessionDay.AddHours(9.5), right, sigmaUpper, Brushes.MediumSeaGreen);
				Level("Sigma lower", sessionDay.AddHours(9.5), right, sigmaLower, Brushes.Crimson);
				Level("Sigma reference", sessionDay.AddHours(9.5), right, sigmaReference, Brushes.Gold);
			}
		}

		if (ownedNet != 0)
		{
			if (plannedStop > 0) Level("Original stop", now, right, plannedStop, Brushes.OrangeRed);
			if (plannedTarget > 0) Level("Original target", now, right, plannedTarget, Brushes.DodgerBlue);
			Level("Owned entry", now, right, entrySum / Math.Max(1, entered), Brushes.LimeGreen);
		}
	}

	internal static int Cutoff(DateTime d)
	{
		return 944; // New York local time; TimeZoneInfo performs EST/EDT conversion.
	}

	private static DateTime Observed(DateTime d)
	{
		DateTime result;
		if (d.DayOfWeek == DayOfWeek.Saturday)
		{
			result = d.AddDays(-1.0);
		}
		else
		{
			result = ((d.DayOfWeek == DayOfWeek.Sunday) ? d.AddDays(1.0) : d);
		}
		return result;
	}

	private static DateTime Nth(DateTime d, DayOfWeek day, int n)
	{
		return d.AddDays((day - d.DayOfWeek + 7) % 7 + 7 * (n - 1));
	}

	private static bool FutureSpecial(DateTime d)
	{
		int year = d.Year;
		int num = year % 19;
		int num2 = year / 100;
		int num3 = year % 100;
		int num4 = num2 % 4;
		int num5 = (num2 + 8) / 25;
		int num6 = (num2 - num5 + 1) / 3;
		int num7 = (19 * num + num2 - num2 / 4 - num6 + 15) % 30;
		int num8 = num3 / 4;
		int num9 = num3 % 4;
		int num10 = (32 + 2 * num4 + 2 * num8 - num7 - num9) % 7;
		int num11 = (num + 11 * num7 + 22 * num10) / 451;
		int num12 = num7 + num10 - 7 * num11 + 114;
		DateTime dateTime = new DateTime(year, num12 / 31, num12 % 31 + 1).AddDays(-2.0);
		DateTime dateTime2 = new DateTime(year, 6, 1).AddDays(-1.0);
		DateTime dateTime3 = dateTime2.AddDays((0 - (dateTime2.DayOfWeek + 6)) % 7);
		DateTime dateTime4 = Nth(new DateTime(year, 11, 1), DayOfWeek.Thursday, 4);
		return d == Observed(new DateTime(year, 1, 1)) || d == Nth(new DateTime(year, 1, 1), DayOfWeek.Monday, 3) || d == Nth(new DateTime(year, 2, 1), DayOfWeek.Monday, 3) || d == dateTime || d == dateTime3 || d == Observed(new DateTime(year, 6, 19)) || d == new DateTime(year, 7, 3) || d == Observed(new DateTime(year, 7, 4)) || d == Nth(new DateTime(year, 9, 1), DayOfWeek.Monday, 1) || d == dateTime4 || d == dateTime4.AddDays(1.0) || d == new DateTime(year, 12, 24) || d == Observed(new DateTime(year, 12, 25)) || d == new DateTime(year, 12, 31);
	}
}
}
