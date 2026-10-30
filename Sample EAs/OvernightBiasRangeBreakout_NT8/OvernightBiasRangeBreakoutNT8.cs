//+------------------------------------------------------------------+
//|                             OvernightBiasRangeBreakoutNT8.cs    |
//| Overnight Bias Range Breakout Strategy on Nasdaq                 |
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
public class OvernightBiasRangeBreakoutNT8 : Strategy
{
	private sealed class ExecutionView
	{
		internal string Tag;

		internal int Side;

		internal double Entry;

		internal double InitialStop;

		internal double Stop;

		internal double Exit;

		internal DateTime Entered;

		internal DateTime Exited;

		internal DateTime TrailStart;

		internal bool Trailing;
	}

	private static class OvernightExecutionVisual
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
						if (strategy.State == State.Terminated || ((OvernightBiasRangeBreakoutNT8)strategy).VisualsSuppressed)
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

		public Candle(DateTime t, double o, double h, double l, double c)
		{
			Start = (Last = t);
			Open = o;
			High = h;
			Low = l;
			Close = c;
		}

		public Candle Clone()
		{
			return new Candle(Start, Open, High, Low, Close)
			{
				Last = Last
			};
		}

		public void Merge(Candle b)
		{
			High = Math.Max(High, b.High);
			Low = Math.Min(Low, b.Low);
			Close = b.Close;
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

	private OvernightBiasRangeBreakoutADX displayAdx;

	private TimeZoneInfo platformZone;

	private TimeZoneInfo centralZone;

	private TimeZoneInfo nyZone;

	private System.Timers.Timer timer;

	private StreamWriter log;

	private DateTime lastMinute;

	private DateTime latestCentral;

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

	[NinjaScriptProperty]
	[Display(Name = "Enable new trades", GroupName = "01 Trading", Order = 0)]
	public bool EnableTrading { get; set; }

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
	[Range(1.0, double.MaxValue)]
	[Display(Name = "Risk per trade, including costs ($)", GroupName = "02 Risk", Order = 0)]
	public double RiskMoney { get; set; }

	[NinjaScriptProperty]
	[Display(Name = "Use a fixed number of contracts", Description = "When enabled, contract quantity replaces the dollar risk budget. Stop/target and ownership protection remain active.", GroupName = "02 Risk", Order = 1)]
	public bool UseFixedSize { get; set; }

	[NinjaScriptProperty]
	[Range(1, int.MaxValue)]
	[Display(Name = "Fixed contract quantity", Description = "Whole contracts per entry when fixed sizing is enabled.", GroupName = "02 Risk", Order = 2)]
	public int FixedContracts { get; set; }

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
		if (State != State.Realtime)
		{
			return null;
		}
		if (Account == null || Account.ConnectionStatus != ConnectionStatus.Connected)
		{
			return "Account disconnected; orders paused";
		}
		if (recoveryFault)
		{
			return status;
		}
		if (recoveryPending || reconcilingRecovery)
		{
			return (status != null && status.StartsWith("Recovery pending:", StringComparison.Ordinal)) ? status : "Reconciling owned broker orders; trading paused";
		}
		if (suspended || closeRejected)
		{
			return status;
		}
		if (CurrentBars == null || signalSeries >= CurrentBars.Length || executionSeries >= CurrentBars.Length || CurrentBars[signalSeries] < 0 || CurrentBars[executionSeries] < 0)
		{
			return "Required M1 history unavailable; trading paused";
		}
		if (visualHistoryWaiting || historyRepair != null)
		{
			return status;
		}
		if (dailyATRs.Count < 15)
		{
			return "ATR warmup: " + dailyATRs.Count + "/15 session values; load 45 days";
		}
		if (!validDay && latestCentral.TimeOfDay.TotalMinutes >= 510.0 && latestCentral.TimeOfDay.TotalMinutes < (double)Cutoff(sessionDay))
		{
			return status;
		}
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
				if (displayAdx != null)
				{
					displayAdx.IsVisible = !visualsSuppressed;
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
		if (ShowVisuals && trade != null)
		{
			bool flag = trade.Exited != DateTime.MinValue;
			DateTime ct = (flag ? trade.Exited : now);
			double num = (double)trade.Side * (trade.Exit - trade.Entry);
			double slPx = ((flag && num <= 0.0) ? trade.Exit : trade.InitialStop);
			OvernightExecutionVisual.DrawExecutionTool(this, trade.Tag, trade.Side > 0, trade.Entry, slPx, (flag && num > 0.0) ? trade.Exit : 0.0, trade.Entry, Platform(trade.Entered), Platform(trade.Entered), Platform(ct), trailing: false, DateTime.MinValue, synthetic: false);
			if (trade.Trailing)
			{
				OvernightExecutionVisual.DrawBoundedLine(this, trade.Tag + "_TRAIL", Platform(trade.TrailStart), trade.Stop, Platform(ct), trade.Stop, OvernightExecutionVisual.ClrTrailingStop, DashStyleHelper.Dot, 2);
			}
		}
	}

	private void PaintHistoricalExecutions(DateTime now)
	{
		if (ShowVisuals)
		{
			EnableNativeExecutionDisplay();
		}
		if (!ShowVisuals)
		{
			return;
		}
		if (!historicalVisualsPainted && State != State.Historical && ChartControl != null)
		{
			HashSet<string> hashSet = new HashSet<string>(from x in DrawObjects
				where x != null
				select x.Tag);
			historicalVisualsPainted = true;
			foreach (ExecutionView closedVisual in closedVisuals)
			{
				if (!hashSet.Contains(closedVisual.Tag + "_SL"))
				{
					DrawExecution(closedVisual, closedVisual.Exited);
					historicalVisualsPainted = false;
				}
			}
		}
		if (executionVisual == null)
		{
			return;
		}
		Order order = (from b in brackets
			where b.Stop != null
			select b.Stop).FirstOrDefault();
		if (order != null && Math.Abs(order.StopPrice - executionVisual.InitialStop) > TickSize / 2.0)
		{
			if (!executionVisual.Trailing)
			{
				executionVisual.TrailStart = now;
			}
			executionVisual.Trailing = true;
			executionVisual.Stop = order.StopPrice;
		}
		DrawExecution(executionVisual, now);
	}

	protected override void OnStateChange()
	{
		if (State == State.SetDefaults)
		{
			Name = "OvernightBiasRangeBreakoutNT8";
			Description = "OvernightBiasRangeBreakoutNT8 - Overnight range and directional-bias breakout strategy. Requires completed one-minute breakout confirmation after 09:00 Central, applies an ADX filter, sizes risk including modeled costs and closes at its session cutoff. Copyright 2026-2027, Osamwonyi Eric (@You_FoundEric). Developed by @You_FoundEric. Unauthorized redistribution prohibited.";
			Calculate = Calculate.OnEachTick;
			IsUnmanaged = true;
			StartBehavior = StartBehavior.ImmediatelySubmit;
			IsExitOnSessionCloseStrategy = false;
			RealtimeErrorHandling = RealtimeErrorHandling.IgnoreAllErrors;
			ConnectionLossHandling = ConnectionLossHandling.KeepRunning;
			BarsRequiredToTrade = 0;
			Slippage = 1.0;
			IsInstantiatedOnEachOptimizationIteration = true;
			MaximumBarsLookBack = MaximumBarsLookBack.Infinite;
			OrderFillResolution = OrderFillResolution.Standard;
			IsFillLimitOnTouch = true;
			StopTargetHandling = StopTargetHandling.PerEntryExecution;
			TimeInForce = TimeInForce.Gtc;
			IsOverlay = true;
			DrawOnPricePanel = true;
			EnableTrading = true;
			BlockOpposingExposure = true;
			RiskMoney = 500.0;
			UseFixedSize = true;
			FixedContracts = 1;
			CommissionPerContract = 3.0;
			SlippagePoints = 3;
			ShowVisuals = true;
			WriteTradeLog = false;
		}
		else if (State == State.Configure)
		{
			if (StartBehavior != StartBehavior.ImmediatelySubmit)
			{
				throw new InvalidOperationException("ImmediatelySubmit is required for ownership isolation.");
			}
			if (RiskMoney <= 0.0 || double.IsNaN(RiskMoney) || double.IsInfinity(RiskMoney) || FixedContracts <= 0)
			{
				throw new InvalidOperationException("Risk must be positive and finite; fixed contract quantity must be positive.");
			}
			IncludeTradeHistoryInBacktest = true;
			signalContract = SourceContract(Instrument);
			executionSeries = (signalSeries = 1);
			if (Instrument.MasterInstrument.Name.Trim().Equals("NNQ", StringComparison.OrdinalIgnoreCase))
			{
				AddDataSeries(signalContract, new BarsPeriod
				{
					BarsPeriodType = BarsPeriodType.Minute,
					Value = 1
				}, "CME US Index Futures ETH", true);
				AddDataSeries(Instrument.FullName, new BarsPeriod
				{
					BarsPeriodType = BarsPeriodType.Minute,
					Value = 1
				}, "CME US Index Futures ETH", true);
				executionSeries = 2;
			}
			else
			{
				AddDataSeries(Instrument.FullName, new BarsPeriod
				{
					BarsPeriodType = BarsPeriodType.Minute,
					Value = 1
				}, "CME US Index Futures ETH", true);
			}
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
			centralZone = TimeZoneInfo.FindSystemTimeZoneById("Central Standard Time");
			nyZone = TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");
			owner = "OBR_" + Guid.NewGuid().ToString("N").Substring(0, 12);
			PrepareRecovery();
			string text = ExecutionInstrument.MasterInstrument.Name.ToUpperInvariant();
			if (text != "NQ" && text != "MNQ" && text != "NNQ")
			{
				throw new InvalidOperationException("Use NQ, MNQ or NNQ; selected " + ExecutionInstrument.FullName + ".");
			}
			if (ExecutionInstrument.MasterInstrument.PointValue <= 0.0 || ExecutionInstrument.MasterInstrument.TickSize <= 0.0)
			{
				throw new InvalidOperationException("Invalid contract specification.");
			}
			if (executionSeries == 2 && (BarsArray.Length < 3 || BarsArray[1].Instrument.FullName != signalContract || BarsArray[2].Instrument.FullName != ExecutionInstrument.FullName))
			{
				throw new InvalidOperationException("NQ signal / NNQ execution series mismatch; trading blocked.");
			}
			if (executionSeries == 2)
			{
				List<DateTime> starts = (from i in Enumerable.Range(0, BarsArray[2].Count)
					select Central(BarsArray[2].GetTime(i)).AddMinutes(-1.0)).ToList();
				if (NativeHistorySufficient(starts, 10, 15, -60, 525, out var reason))
				{
					signalSeries = 2;
					signalContract = ExecutionInstrument.FullName;
				}
				else
				{
					Print("Overnight NQ fallback: " + reason);
				}
			}
			if (ShowVisuals)
			{
				displayAdx = OvernightBiasModelDisplay();
				displayAdx.IsOverlay = false;
				displayAdx.Panel = 1;
				AddChartIndicator(displayAdx);
			}
			if (WriteTradeLog)
			{
				string text2 = Path.Combine(Globals.UserDataDir, "OvernightBiasRangeBreakout", "logs");
				Directory.CreateDirectory(text2);
				log = new StreamWriter(Path.Combine(text2, owner + ".csv"))
				{
					AutoFlush = true
				};
				log.WriteLine("event,central_time,detail");
			}
		}
		else if (State == State.Transition)
		{
			if (ownedNet != 0 || entryPending || orders.Values.Any((Order o) => !Settled(o) || !ExecutionsCaughtUp(o)))
			{
				throw new InvalidOperationException("Historical owned exposure must be flat before realtime.");
			}
		}
		else if (State == State.Realtime)
		{
			startupArmed = false;
			ownerKey = ((Account == null) ? "" : Account.Name) + "|" + ExecutionInstrument.FullName;
			exposureKey = ((Account == null) ? "unknown" : Account.Name) + "|" + ExecutionInstrument.FullName;
			lock (ownerLock)
			{
				ownsGuard = activeOwners.Add(ownerKey);
			}
			if (!ownsGuard)
			{
				suspended = true;
				status = "Another Overnight instance owns this account/instrument";
			}
			startupFloor = Central(NativeNow());
			StartRecovery();
			if (!recoveredTrade && Account != null && Account.Positions.Any((Position p) => p.Instrument.FullName == ExecutionInstrument.FullName && p.Quantity != 0))
			{
				suspended = true;
				status = "Existing account exposure: new entries blocked";
			}
			timer = new System.Timers.Timer(1000.0);
			timer.AutoReset = true;
			timer.Elapsed += (object s, ElapsedEventArgs e) =>
			{
				try
				{
					TriggerCustomEvent((object o) =>
					{
						TimerStep();
					}, null);
				}
				catch
				{
				}
			};
			timer.Start();
		}
		else
		{
			if (State != State.Terminated)
			{
				return;
			}
			StopRecovery();
			HookDashboardMouse(attach: false);
			if (historyRepair != null)
			{
				historyRepair.Dispose();
				historyRepair = null;
			}
			if (ownedNet == 0 && !entryPending && (orders == null || orders.Values.All((Order o) => Settled(o) && ExecutionsCaughtUp(o))))
			{
				ReleaseExposure();
			}
			if (ownsGuard)
			{
				lock (ownerLock)
				{
					activeOwners.Remove(ownerKey);
				}
				ownsGuard = false;
			}
			if (timer != null)
			{
				timer.Stop();
				timer.Dispose();
				timer = null;
			}
			if (log != null)
			{
				log.Dispose();
				log = null;
			}
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

	private DateTime Central(DateTime platform)
	{
		return TimeZoneInfo.ConvertTime(DateTime.SpecifyKind(platform, DateTimeKind.Unspecified), platformZone, centralZone);
	}

	private DateTime Platform(DateTime ct)
	{
		return TimeZoneInfo.ConvertTime(DateTime.SpecifyKind(ct, DateTimeKind.Unspecified), centralZone, platformZone);
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
			string text = ((Account.Connection == null) ? "" : (Account.Connection.Options.Name + "|" + Account.Connection.Options.Provider));
			recoveryIdentity = text + "|" + Account.Name + "|" + ExecutionInstrument.FullName + "|OvernightBias_ATR3R_M15";
			string text2 = recoveryIdentity;
			char[] invalidFileNameChars = Path.GetInvalidFileNameChars();
			foreach (char oldChar in invalidFileNameChars)
			{
				text2 = text2.Replace(oldChar, '_');
			}
			recoveryPath = Path.Combine(Globals.UserDataDir, "OvernightBiasRangeBreakout", "recovery", text2 + ".journal");
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
				if ((recoveryVersion != "IndependentNT8.v1" && recoveryVersion != "IndependentNT8.v2") || binaryReader.ReadString() != recoveryIdentity)
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
				entryDirection = recoveryVersion == "IndependentNT8.v2" ? binaryReader.ReadInt32() : 0;
				retryArmed = recoveryVersion == "IndependentNT8.v2" && binaryReader.ReadBoolean();
				retryEligibleAt = recoveryVersion == "IndependentNT8.v2" ? new DateTime(binaryReader.ReadInt64()) : DateTime.MinValue;
				targetFilled = recoveryVersion == "IndependentNT8.v2" && binaryReader.ReadBoolean();
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
				binaryWriter.Write("IndependentNT8.v2");
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
		if ((stopOrder == null && recoveryRequests.ContainsKey(stopName)) || (targetOrder == null && recoveryRequests.ContainsKey(targetName)) || new Order[2] { stopOrder, targetOrder }.Any((Order o) => o != null && (o.OrderState == OrderState.Unknown || o.OrderState == OrderState.CancelPending || o.OrderState == OrderState.CancelSubmitted)))
		{
			WaitForRecoveryEvidence();
			return false;
		}
		if (Settled(stopOrder) != Settled(targetOrder))
		{
			WaitForRecoveryEvidence();
			return false;
		}
		return true;
	}

	private bool RepairOwnedProtection()
	{
		if (repairingProtection || !BrokerReady() || recoveryFault || (closing && !closeRejected))
		{
			return false;
		}
		if (ownedNet == 0)
		{
			return true;
		}
		if (!RecoveryPositionConsistent() || orders.Values.Any((Order o) => !ExecutionsCaughtUp(o)))
		{
			WaitForRecoveryEvidence();
			return false;
		}
		repairingProtection = true;
		try
		{
			int num = Math.Abs(ownedNet);
			int num2 = Math.Sign(ownedNet);
			if (brackets.Count == 0)
			{
				List<Bracket> list = brackets;
				Bracket bracket = new Bracket();
				string text = owner;
				int num3 = ++sequence;
				bracket.StopName = text + "_RecoveredStop_" + num3;
				bracket.TargetName = owner + "_RecoveredTarget_" + sequence;
				list.Add(bracket);
			}
			foreach (Bracket bracket2 in brackets)
			{
				int num4 = (recoveryRequests.TryGetValue(bracket2.StopName, out var value) ? value.Quantity : num);
				int num5 = ((bracket2.Stop != null) ? bracket2.Stop.Filled : 0) + ((bracket2.Target != null) ? bracket2.Target.Filled : 0);
				int num6 = Math.Min(num, Math.Max(0, num4 - num5));
				if (num6 <= 0)
				{
					continue;
				}
				num -= num6;
				if (!Settled(bracket2.Stop) && bracket2.Stop.Quantity - bracket2.Stop.Filled != num6)
				{
					if (bracket2.Stop.Quantity - bracket2.Stop.Filled > num6)
					{
						RecoveryCancel(bracket2.Stop);
					}
					throw new InvalidDataException("Owned working stop quantity differs from remaining bracket");
				}
				if (!Settled(bracket2.Target) && bracket2.Target.Quantity - bracket2.Target.Filled != num6)
				{
					if (bracket2.Target.Quantity - bracket2.Target.Filled > num6)
					{
						RecoveryCancel(bracket2.Target);
					}
					throw new InvalidDataException("Owned working target quantity differs from remaining bracket");
				}
				if (!ReadyForProtectionRepair(bracket2.Stop, bracket2.Target, bracket2.StopName, bracket2.TargetName))
				{
					return false;
				}
				if (Settled(bracket2.Stop) || Settled(bracket2.Target))
				{
					double num7 = value?.Stop ?? plannedStop;
					double num8 = ((num2 > 0) ? GetCurrentBid(executionSeries) : GetCurrentAsk(executionSeries));
					if (num8 <= 0.0 || (double)num2 * (num8 - num7) <= 0.0 || (double)num2 * (plannedTarget - num8) <= 0.0)
					{
						throw new InvalidDataException("Missing protection cannot be restored at crossed or unavailable market levels");
					}
					string oco = owner + "_RepairOco_" + ++sequence;
					if (Settled(bracket2.Stop))
					{
						string text2 = owner;
						int num3 = ++sequence;
						bracket2.StopName = text2 + "_RepairStop_" + num3;
						bracket2.Stop = RecoverySubmit(executionSeries, (num2 <= 0) ? OrderAction.BuyToCover : OrderAction.Sell, OrderType.StopMarket, num6, 0.0, num7, oco, bracket2.StopName);
					}
					if (bracket2.Stop == null || bracket2.Stop.OrderState == OrderState.Rejected)
					{
						throw new InvalidDataException("Recovered owned stop was not accepted");
					}
					if (Settled(bracket2.Target))
					{
						string text3 = owner;
						int num3 = ++sequence;
						bracket2.TargetName = text3 + "_RepairTarget_" + num3;
						bracket2.Target = RecoverySubmit(executionSeries, (num2 <= 0) ? OrderAction.BuyToCover : OrderAction.Sell, OrderType.Limit, num6, plannedTarget, 0.0, oco, bracket2.TargetName);
					}
				}
			}
			if (num != 0)
			{
				throw new InvalidDataException("Remaining owned exposure has no journalled bracket");
			}
			SaveRecovery();
			return true;
		}
		catch (Exception ex)
		{
			RecoveryBlock("Owned protection requires attention: " + ex.Message);
			return false;
		}
		finally
		{
			repairingProtection = false;
		}
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
		if (State == State.Realtime && !ObserveBrokerConnection(Central(NativeNow())))
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
			DateTime dateTime = Central(Times[executionSeries][num]);
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
			DateTime dateTime = Central(BarsArray[signalSeries].GetTime(i));
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
			DateTime dateTime3 = Central(NativeNow());
			bool fresh = State == State.Historical || (i == num && dateTime > recoveryFloor && dateTime >= new DateTime(dateTime3.Year, dateTime3.Month, dateTime3.Day, dateTime3.Hour, dateTime3.Minute, 0));
			ProcessSource(new Candle(dateTime2, BarsArray[signalSeries].GetOpen(i), BarsArray[signalSeries].GetHigh(i), BarsArray[signalSeries].GetLow(i), BarsArray[signalSeries].GetClose(i)), fresh);
			consumedSourceIndex = i;
			if (historyRepair != null)
			{
				break;
			}
		}
	}

	private void ProcessSource(Candle bar, bool fresh)
	{
		DateTime end = bar.Start.AddMinutes(1.0);
		sourceFresh = fresh;
		lastMinute = end;
		latestCentral = end;
		minutes[bar.Start] = bar;
		DateTime[] array = minutes.Keys.TakeWhile((DateTime k) => k < end.AddDays(-3.0)).ToArray();
		foreach (DateTime key in array)
		{
			minutes.Remove(key);
		}
		Daily(bar, end);
		if (executionSeries == 1 && ((State == State.Historical) | fresh))
		{
			Trail(bar, end);
		}
		if (end.Date != sessionDay)
		{
			ResetDay(end.Date);
		}
		SaveRecovery();
		if (!validDay && end.TimeOfDay >= TimeSpan.FromHours(8.5))
		{
			PrepareDay();
		}
		if (!readyRange && end.TimeOfDay >= TimeSpan.FromHours(8.75))
		{
			PrepareRange();
		}
		bool flag = Aggregate(bar, end);
		if (displayAdx != null)
		{
			displayAdx.Publish(adx.Value, end);
		}
		if (State == State.Realtime && !startupArmed)
		{
			startupFloor = ((end > startupFloor) ? end : startupFloor);
			startupArmed = true;
			status = "Waiting for a fresh completed M15 signal";
		}
		if (end.TimeOfDay >= TimeSpan.FromMinutes(Cutoff(sessionDay)))
		{
			BeginClose("Time");
		}
		if (flag)
		{
			Signal(end, bucket.Close);
		}
		if (ShowVisuals)
		{
			Levels(end);
		}
	}

	private void RequestSourceRepair(DateTime missingRight, DateTime historicalLeft = default(DateTime))
	{
		visualHistoryWaiting = true;
		DateTime dateTime = Central(NativeNow());
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
		Print("Overnight M1 reload: " + missingLeft.ToString("s") + " to " + missingRight.ToString("s") + " Central");
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
							DateTime dateTime2 = Central(reply.Bars.GetTime(i)).AddMinutes(-1.0);
							if (dateTime2 >= missingLeft && dateTime2 < missingRight)
							{
								recovered.Add(new Candle(dateTime2, reply.Bars.GetOpen(i), reply.Bars.GetHigh(i), reply.Bars.GetLow(i), reply.Bars.GetClose(i)));
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
								Print("Overnight " + status + " " + message);
							}
							else
							{
								visualHistoryWaiting = false;
								recoveryFloor = Central(NativeNow());
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
								Print("Overnight M1 recovered " + recovered.Count + " bars; old signals suppressed");
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
			Print("Overnight " + status + ": " + ex.Message);
		}
	}

	private void RebuildSource(List<Candle> recovered)
	{
		SortedDictionary<DateTime, Candle> sortedDictionary = new SortedDictionary<DateTime, Candle>();
		int num = BarsArray[signalSeries].Count - 2;
		for (int i = 0; i <= num; i++)
		{
			DateTime dateTime = Central(BarsArray[signalSeries].GetTime(i)).AddMinutes(-1.0);
			sortedDictionary[dateTime] = new Candle(dateTime, BarsArray[signalSeries].GetOpen(i), BarsArray[signalSeries].GetHigh(i), BarsArray[signalSeries].GetLow(i), BarsArray[signalSeries].GetClose(i));
		}
		foreach (Candle value in providerBars.Values)
		{
			sortedDictionary[value.Start] = value;
		}
		trueRanges.Clear();
		dailyATRs.Clear();
		previousDailyClose = double.NaN;
		daily = null;
		bucket = null;
		bucketFinalized = false;
		adx = new WilderAdx();
		minutes.Clear();
		sourceFresh = false;
		if (displayAdx != null)
		{
			displayAdx.ResetPublished();
		}
		foreach (Candle value2 in sortedDictionary.Values)
		{
			DateTime dateTime2 = value2.Start.AddMinutes(1.0);
			if (dateTime2 >= recoveryFloor.AddDays(-3.0))
			{
				minutes[value2.Start] = value2;
			}
			Daily(value2, dateTime2);
			Aggregate(value2, dateTime2);
			if (displayAdx != null)
			{
				displayAdx.Publish(adx.Value, dateTime2, paint: false);
			}
			lastMinute = (latestCentral = dateTime2);
		}
		consumedSourceIndex = num;
		startupFloor = recoveryFloor;
		validDay = false;
		readyRange = false;
		PrepareDay();
		PrepareRange();
		if (displayAdx != null)
		{
			displayAdx.RefreshPublishedPlots();
		}
	}

	private void ResetDay(DateTime day)
	{
		sessionDay = day;
		validDay = false;
		readyRange = false;
		direction = 0;
		entryCount = 0;
		entryDirection = 0;
		retryArmed = false;
		retryEligibleAt = DateTime.MinValue;
		targetFilled = false;
		averageATR = 0.0;
		if (ownedNet == 0 && !entryPending)
		{
			closing = false;
			closeReason = null;
		}
		if (ownedNet != 0 || entryPending)
		{
			suspended = true;
			BeginClose("SessionBoundary");
		}
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

	private void PrepareDay()
	{
		if (dailyATRs.Count < 15)
		{
			status = "ATR warmup: " + dailyATRs.Count + "/15 session values; load 45 days";
			return;
		}
		if (!Window(sessionDay.AddHours(-1.0), sessionDay.AddHours(8.5), out var aggregate))
		{
			status = "Incomplete overnight M1 history; use full-session data";
			return;
		}
		if (!minutes.TryGetValue(sessionDay.AddHours(8.5), out var value))
		{
			status = "Waiting for the 08:30 Central opening bar";
			return;
		}
		averageATR = dailyATRs.Average();
		if (!(averageATR <= 0.0) && !double.IsNaN(averageATR))
		{
			nightHigh = aggregate.High;
			nightLow = aggregate.Low;
			direction = Bias(value.Open, nightLow, nightHigh);
			validDay = true;
			string text = "day=" + sessionDay.ToString("yyyy-MM-dd") + " source=" + BarsArray[signalSeries].Instrument.FullName + " overnightFromCentral=" + sessionDay.AddHours(-1.0).ToString("s") + " overnightToCentral=" + sessionDay.AddHours(8.5).ToString("s") + " openingStartCentral=" + value.Start.ToString("s") + " openingM1EndCentral=" + value.Start.AddMinutes(1.0).ToString("s") + " open=" + value.Open.ToString("R", CultureInfo.InvariantCulture) + " nightLow=" + nightLow.ToString("R", CultureInfo.InvariantCulture) + " nightHigh=" + nightHigh.ToString("R", CultureInfo.InvariantCulture) + " bias=" + direction;
			if ((State == State.Realtime || sessionDay == Central(NativeNow()).Date) && lastBiasAudit != text)
			{
				lastBiasAudit = text;
				Print("OBR_BIAS_INPUTS " + text);
				NinjaScript.Log("OBR_BIAS_INPUTS " + text, LogLevel.Information);
			}
			status = ((direction == 0) ? "Neutral opening: no trade" : "Waiting for a fresh directed M15 close");
			Record("DAY", latestCentral, text + " ATR=" + F(averageATR));
		}
	}

	private void PrepareRange()
	{
		if (Window(sessionDay.AddHours(8.5), sessionDay.AddHours(8.75), out var aggregate))
		{
			rangeHigh = aggregate.High;
			rangeLow = aggregate.Low;
			readyRange = true;
			Record("RANGE", latestCentral, F(rangeLow) + "/" + F(rangeHigh));
		}
	}

	private void Daily(Candle bar, DateTime end)
	{
		DateTime dateTime = ((bar.Start.TimeOfDay >= TimeSpan.FromHours(16.0)) ? bar.Start.Date.AddDays(1.0) : bar.Start.Date);
		if (daily == null)
		{
			dailyKey = dateTime;
			daily = bar.Clone();
		}
		else if (dateTime != dailyKey)
		{
			if (!double.IsNaN(previousDailyClose))
			{
				double item = Math.Max(daily.High - daily.Low, Math.Max(Math.Abs(daily.High - previousDailyClose), Math.Abs(daily.Low - previousDailyClose)));
				trueRanges.Enqueue(item);
				if (trueRanges.Count > 10)
				{
					trueRanges.Dequeue();
				}
				if (trueRanges.Count == 10)
				{
					dailyATRs.Enqueue(trueRanges.Average());
					if (dailyATRs.Count > 15)
					{
						dailyATRs.Dequeue();
					}
				}
			}
			previousDailyClose = daily.Close;
			dailyKey = dateTime;
			daily = bar.Clone();
		}
		else
		{
			daily.Merge(bar);
		}
	}

	private bool Aggregate(Candle bar, DateTime end)
	{
		DateTime dateTime = bar.Start.Date.AddMinutes(Math.Floor(bar.Start.TimeOfDay.TotalMinutes / 15.0) * 15.0);
		if (bucket == null || dateTime != bucketKey)
		{
			if (bucket != null && !bucketFinalized)
			{
				adx.Push(bucket);
				Signal(bucketKey.AddMinutes(15.0), bucket.Close);
			}
			bucketKey = dateTime;
			bucket = bar.Clone();
			bucketFinalized = false;
		}
		else
		{
			bucket.Merge(bar);
		}
		if (end != dateTime.AddMinutes(15.0))
		{
			return false;
		}
		adx.Push(bucket);
		bucketFinalized = true;
		return true;
	}

	private bool CanReplayCompletedSession(DateTime day)
	{
		if (State != State.Historical || IsInStrategyAnalyzer)
		{
			return true;
		}
		Bars bars = BarsArray[executionSeries];
		return day != DateTime.MinValue && bars.Count > 0 && Central(bars.GetTime(bars.Count - 1)) >= day.Date.AddMinutes(Cutoff(day) + 2);
	}

	private void Signal(DateTime end, double close)
	{
		if ((State == State.Realtime && (recoveryPending || recoveryFault)) || !CanReplayCompletedSession(sessionDay) || suspended || closing || !EnableTrading || !sourceFresh || !validDay || !readyRange || entryCount >= MaxAttempts || entryPending || ownedNet != 0 || !adx.Ready || !SignalTime(end.TimeOfDay.TotalMinutes) || end.Date != sessionDay || end.TimeOfDay.TotalMinutes >= (double)Cutoff(sessionDay) || latestCentral.TimeOfDay.TotalMinutes >= (double)Cutoff(sessionDay) || (State == State.Realtime && (!startupArmed || end <= startupFloor)))
		{
			return;
		}
		if (State == State.Realtime && Account != null && Account.Positions.Any((Position p) => p.Instrument.FullName == ExecutionInstrument.FullName && p.Quantity != 0))
		{
			status = "Other account exposure: new entry blocked";
			return;
		}
		int num = SignalSide(direction, close, rangeHigh, rangeLow, adx.Value);
		Record("SIGNAL", end, "close=" + F(close) + " ADX=" + F(adx.Value) + " side=" + num);
		if (num == 0)
		{
			return;
		}
		if (entryCount == 1 && !(retryArmed && num == entryDirection && end > retryEligibleAt))
		{
			return;
		}
		if (CurrentBars[executionSeries] < 0)
		{
			status = "Waiting for execution-instrument M1 history";
			return;
		}
		double num2;
		if (State != State.Realtime)
		{
			num2 = Closes[executionSeries][0];
		}
		else
		{
			num2 = ((num > 0) ? GetCurrentAsk(executionSeries) : GetCurrentBid(executionSeries));
		}
		if (!(num2 <= 0.0))
		{
			double retryDistance = 0.3 * averageATR * ((entryCount == 1) ? RetryStopFraction : 1.0);
			plannedStop = Round(BracketPrice(num, num2, retryDistance, target: false));
			plannedTarget = Round(BracketPrice(num, num2, retryDistance, target: true));
			double num3 = Math.Abs(num2 - plannedStop) * ExecutionInstrument.MasterInstrument.PointValue + CostPerContract;
			int num4 = (UseFixedSize ? FixedContracts : Quantity(RiskMoney, num3));
			if (num4 < 1)
			{
				status = "One contract needs $" + num3.ToString("0.00", CultureInfo.InvariantCulture) + "; risk $" + F(RiskMoney);
				Record("SKIP_RISK", end, F(num3));
			}
			else if (State != State.Realtime || AcquireExposure(num))
			{
				entryCount++;
				entryDirection = num;
				retryArmed = false;
				targetFilled = false;
				entryPending = true;
				entered = (exited = (ownedNet = 0));
				entrySum = (exitSum = (actualFees = (peakR = 0.0)));
				tradeModeledCost = CostPerContract;
				brackets.Clear();
				closeReason = null;
				recoveredCommissionFloor = 0.0;
				string text = owner;
				int num5 = ++sequence;
				entryName = text + "_Entry_" + num5;
				status = "Entry submitted";
				SaveRecovery();
				SubmitOwnedOrder(executionSeries, (num <= 0) ? OrderAction.SellShort : OrderAction.Buy, OrderType.Market, num4, 0.0, 0.0, string.Empty, entryName);
			}
		}
	}

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
		if (UseFixedSize || entered <= 0 || ownedNet == 0 || closing)
		{
			return;
		}
		double entryFees = 0.0;
		int entryQuantity = 0;
		if (CanLearnFee && feeTrade != null)
		{
			lock (FeeGate)
			{
				entryQuantity = feeTrade.EntryQuantity;
				entryFees = feeTrade.Slices.Values.Where((FeeSlice x) => x.Entry).Sum((FeeSlice x) => FeeTrade.Charge(x.Execution));
			}
		}
		double num = FillRiskCost(tradeModeledCost, entryFees, entryQuantity, 2.0 * SlippagePrice * ExecutionInstrument.MasterInstrument.PointValue);
		if ((double)entered * (initialRisk * ExecutionInstrument.MasterInstrument.PointValue + num) > RiskMoney + 1E-07)
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
		if (!BrokerReady() || ownedNet == 0 || initialRisk <= 0.0 || closing || entered <= 0)
		{
			return;
		}
		if (entryCount == 2)
		{
			MoveRetryBreakEven(bar);
			return;
		}
		int num = Math.Sign(ownedNet);
		double num2 = entrySum / (double)entered;
		peakR = Math.Max(peakR, (double)num * (bar.Close - num2) / initialRisk);
		double num3 = TrailLock(peakR);
		if (double.IsNaN(num3))
		{
			return;
		}
		double num4 = Math.Max(EffectiveCommission, recoveredCommissionFloor);
		if (CanLearnFee && feeTrade != null && feeTrade.EntryQuantity > 0)
		{
			num4 = Math.Max(num4, 2.0 * feeTrade.Slices.Values.Where((FeeSlice x) => x.Entry).Sum((FeeSlice x) => FeeTrade.Charge(x.Execution)) / (double)feeTrade.EntryQuantity);
		}
		recoveredCommissionFloor = Math.Max(recoveredCommissionFloor, num4);
		double val = CoveredStop(num, num2, CoverCommission(num4), ExecutionInstrument.MasterInstrument.PointValue, CoverSlippagePerSide * (double)((State != State.Historical) ? 1 : 2), ExecutionInstrument.MasterInstrument.TickSize);
		double val2 = Round(num2 + (double)num * num3 * initialRisk);
		double num5 = ((num > 0) ? Math.Max(val, val2) : Math.Min(val, val2));
		double num6;
		if (State != State.Realtime)
		{
			num6 = bar.Close;
		}
		else
		{
			num6 = ((num > 0) ? GetCurrentBid(executionSeries) : GetCurrentAsk(executionSeries));
		}
		if ((double)num * (num6 - num5) <= 0.0)
		{
			BeginClose("M1TrailCrossed");
			return;
		}
		foreach (Bracket bracket in brackets)
		{
			if (bracket.Stop != null && !Settled(bracket.Stop) && !((double)num * (num5 - bracket.Stop.StopPrice) <= 0.0) && (bracket.Stop.OrderState == OrderState.Working || bracket.Stop.OrderState == OrderState.Accepted))
			{
				ChangeOwnedOrder(bracket.Stop, bracket.Stop.Quantity, 0.0, num5);
			}
		}
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
			Complete(Central(time));
		}
	}

	protected override void OnExecutionUpdate(Execution execution, string executionId, double price, int quantity, MarketPosition marketPosition, string orderId, DateTime time)
	{
		if (execution == null || execution.Order == null || !execution.Order.Name.StartsWith(owner + "_") || ReconcileOwnedExecution(execution, executionId))
		{
			return;
		}
		ObserveFee(execution, executionId, quantity, execution.Order.Name == entryName);
		if (!executions.Add(executionId))
		{
			CheckActualFillRisk();
			return;
		}
		RememberExecution(execution.Order, quantity, price);
		Order order = execution.Order;
		processed.TryGetValue(order.Name, out var value);
		processed[order.Name] = value + quantity;
		if (order.Name == pendingCloseName)
		{
			pendingCloseExecuted += quantity;
		}
		int num = ((order.OrderAction == OrderAction.Buy || order.OrderAction == OrderAction.BuyToCover) ? 1 : (-1));
		ownedNet += num * quantity;
		actualFees += FeeTrade.Charge(execution);
		if (order.Name == entryName)
		{
			if (State == State.Historical && entered == 0)
			{
				double retryDistance = 0.3 * averageATR * ((entryCount == 2) ? RetryStopFraction : 1.0);
				plannedStop = Round(BracketPrice(num, price, retryDistance, target: false));
				plannedTarget = Round(BracketPrice(num, price, retryDistance, target: true));
			}
			entered += quantity;
			entrySum += price * (double)quantity;
			initialRisk = Math.Abs(entrySum / (double)entered - plannedStop);
			if (initialRisk <= 0.0 || (double)num * (price - plannedStop) <= 0.0 || (double)num * (plannedTarget - price) <= 0.0)
			{
				suspended = true;
				BeginClose("InvalidFill");
			}
			else if (!closing)
			{
				Bracket bracket = new Bracket();
				string text = owner;
				int num2 = ++sequence;
				bracket.StopName = text + "_Stop_" + num2;
				bracket.TargetName = owner + "_Target_" + sequence;
				Bracket bracket2 = bracket;
				brackets.Add(bracket2);
				string oco = owner + "_OCO_" + sequence;
				bracket2.Stop = SubmitOwnedOrder(executionSeries, (num <= 0) ? OrderAction.BuyToCover : OrderAction.Sell, OrderType.StopMarket, quantity, 0.0, plannedStop, oco, bracket2.StopName);
				if (!closing)
				{
					bracket2.Target = SubmitOwnedOrder(executionSeries, (num <= 0) ? OrderAction.BuyToCover : OrderAction.Sell, OrderType.Limit, quantity, plannedTarget, 0.0, oco, bracket2.TargetName);
				}
			}
			if (executionVisual == null)
			{
				executionVisual = new ExecutionView
				{
					Tag = entryName + "_EXEC",
					Side = num,
					InitialStop = plannedStop,
					Stop = plannedStop,
					Entered = Central(time)
				};
			}
			executionVisual.Entry = entrySum / (double)entered;
			DrawExecution(executionVisual, Central(time));
			CheckActualFillRisk();
			Record("ENTRY", Central(time), "price=" + F(price) + " qty=" + quantity + " stop=" + F(plannedStop) + ";tradeId=" + entryName + ";executionId=" + executionId + ";time=" + Central(time).ToString("s") + ";direction=" + ((num > 0) ? "Long" : "Short") + ";quantity=" + quantity + ";initialStop=" + F(plannedStop) + ";initialTarget=" + F(plannedTarget));
			status = (closing ? "Closing owned position" : "Owned position protected");
		}
		else
		{
			targetFilled |= brackets.Any((Bracket b) => b.TargetName == order.Name);
			exited += quantity;
			exitSum += price * (double)quantity;
			Record("EXIT", Central(time), order.Name + " price=" + F(price) + " qty=" + quantity + ";tradeId=" + entryName + ";executionId=" + executionId + ";time=" + Central(time).ToString("s") + ";direction=" + ((plannedStop < entrySum / (double)entered) ? "Long" : "Short") + ";quantity=" + quantity + ";initialStop=" + F(plannedStop));
			if (ownedNet != 0 && !closing)
			{
				BeginClose("PartialExit");
			}
		}
		if (orders.TryGetValue(entryName ?? "", out var value2) && Settled(value2) && ExecutionsCaughtUp(value2))
		{
			entryPending = false;
		}
		if (exited > entered)
		{
			suspended = true;
			BeginClose("OcoRace");
		}
		DriveClose();
		if (entered > 0 && ownedNet == 0 && !entryPending && orders.Values.All((Order o) => Settled(o) && ExecutionsCaughtUp(o)))
		{
			Complete(Central(time));
		}
	}

	private void Complete(DateTime time)
	{
		if (feeTrade != null)
		{
			feeTrade.Closed = feeTrade.EntryQuantity > 0 && feeTrade.EntryQuantity == feeTrade.ExitQuantity;
		}
		if (executionVisual != null)
		{
			executionVisual.Exit = exitSum / (double)exited;
			executionVisual.Exited = time;
			closedVisuals.Add(executionVisual);
			historicalVisualsPainted = false;
			DrawExecution(executionVisual, time);
			executionVisual = null;
		}
		double num = exitSum - entrySum;
		int num2 = ((plannedStop < entrySum / (double)entered) ? 1 : (-1));
		double num3 = (double)num2 * num * ExecutionInstrument.MasterInstrument.PointValue;
		double value = num3 - (double)entered * tradeModeledCost;
		Print(Name + " trade closed: gross=" + F(num3) + " modeled net=" + F(value) + " actual commission=" + F(actualFees));
		Record("CLOSE", time, "gross=" + F(num3) + " modeledNet=" + F(value));
		string completedCloseReason = closeReason;
		entered = (exited = 0);
		brackets.Clear();
		closing = false;
		closeReason = null;
		ArmSameSideRetry(time, value < 0.0, completedCloseReason);
		status = retryArmed ? "Same-side retry armed" : "Daily trade consumed";
		ReleaseExposure();
		SaveRecovery();
	}

	private void ArmSameSideRetry(DateTime time, bool losingExit, string exitCloseReason)
	{
		// Retry only after a losing broker protective-stop exit: any strategy-initiated close (time, partial, recovery, trail-cross, invalid fill) leaves exitCloseReason set.
		retryArmed = entryCount == 1 && entryDirection != 0 && losingExit && !targetFilled && string.IsNullOrEmpty(exitCloseReason);
		retryEligibleAt = retryArmed ? time : DateTime.MinValue;
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
		if (State == State.Realtime)
		{
			DateTime dateTime = Central(NativeNow());
			bool flag = ObserveBrokerConnection(dateTime);
			if (ownedNet != 0 && dateTime >= sessionDay.AddMinutes(Cutoff(sessionDay)))
			{
				BeginClose("Time");
			}
			if (flag)
			{
				CheckActualFillRisk();
				DriveClose();
				ConsumeSource();
			}
			UpdatePanel();
		}
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
		if (ShowVisuals)
		{
			DateTime dateTime = ((State == State.Realtime) ? Central(NativeNow()) : latestCentral);
			DateTime dateTime2 = TimeZoneInfo.ConvertTime(DateTime.SpecifyKind(dateTime, DateTimeKind.Unspecified), centralZone, nyZone);
			PaintHistoricalExecutions(dateTime);
			dashboard.Publish(new ModelDashState
			{
				Title = "Overnight Bias Breakout",
				Clock = "NY: " + dateTime2.ToString("HH:mm:ss") + " CT: " + dateTime.ToString("HH:mm:ss"),
				Status = (VisualFaultReason() ?? status),
				NewsBlocked = (connectionPaused || suspended),
				RangeCaption = "OPENING RANGE | 08:30 CT",
				HighCaption = "HIGH",
				LowCaption = "LOW",
				RangeHigh = ((readyRange && direction != 0) ? F(rangeHigh) : "--"),
				RangeLow = ((readyRange && direction != 0) ? F(rangeLow) : "--"),
				RangeWidth = ((readyRange && direction != 0) ? F(rangeHigh - rangeLow) : "--"),
				InTrade = (ownedNet != 0 && !recoveryPending && !recoveryFault),
				Position = ((recoveryPending || recoveryFault) ? "UNVERIFIED" : ((ownedNet == 0) ? "FLAT" : (Math.Abs(ownedNet) + ((ownedNet > 0) ? " LONG" : " SHORT")))),
				Stop = ((!recoveryPending && !recoveryFault && brackets.Count > 0 && brackets[0].Stop != null) ? F(brackets[0].Stop.StopPrice) : "--"),
				Risk = "Trades " + entryCount + "/1 | " + (UseFixedSize ? ("Fixed " + FixedContracts + " contracts") : ("Risk $" + F(RiskMoney))),
				Costs = "Cost/contract: $" + F(CostPerContract) + " | Slip " + SlippagePoints,
				History = "ADX " + adx.Value.ToString("0.000", CultureInfo.InvariantCulture) + "/20 | Bias " + ((direction > 0) ? "LONG" : ((direction < 0) ? "SHORT" : "NEUTRAL")) + ((signalSeries != executionSeries) ? (" | NQ -> " + ExecutionInstrument.MasterInstrument.Name) : "")
			});
			if (State == State.Realtime)
			{
				RequestDashboardRender();
			}
		}
	}

	private void Level(string tag, DateTime left, DateTime right, double price, System.Windows.Media.Brush brush)
	{
		if (!VisualsSuppressed && right > left && price > 0.0)
		{
			Draw.Line(this, "OBR_" + sessionDay.ToString("yyyyMMdd") + "_" + tag, isAutoScale: false, Platform(left), price, Platform(right), price, brush, DashStyleHelper.Dash, 1);
		}
	}

	private void Levels(DateTime now)
	{
		DateTime right = sessionDay.AddMinutes(Cutoff(sessionDay));
		if (Window(sessionDay.AddHours(-1.0), (now < sessionDay.AddHours(8.5)) ? now : sessionDay.AddHours(8.5), out var aggregate))
		{
			double num = (validDay ? nightHigh : aggregate.High);
			double num2 = (validDay ? nightLow : aggregate.Low);
			Level("Overnight high", sessionDay.AddHours(-1.0), right, num, Brushes.DeepSkyBlue);
			Level("Overnight low", sessionDay.AddHours(-1.0), right, num2, Brushes.DeepSkyBlue);
			Level("Upper third", sessionDay.AddHours(-1.0), right, num2 + 2.0 * (num - num2) / 3.0, Brushes.SlateGray);
			Level("Lower third", sessionDay.AddHours(-1.0), right, num2 + (num - num2) / 3.0, Brushes.SlateGray);
		}
		if (validDay && direction != 0 && readyRange)
		{
			Level("Opening range high", sessionDay.AddHours(8.5), right, rangeHigh, Brushes.Goldenrod);
			Level("Opening range low", sessionDay.AddHours(8.5), right, rangeLow, Brushes.Goldenrod);
		}
		else if (validDay && direction != 0 && now > sessionDay.AddHours(8.5) && Window(sessionDay.AddHours(8.5), (now < sessionDay.AddHours(8.75)) ? now : sessionDay.AddHours(8.75), out aggregate))
		{
			Level("Opening range high", sessionDay.AddHours(8.5), right, aggregate.High, Brushes.Goldenrod);
			Level("Opening range low", sessionDay.AddHours(8.5), right, aggregate.Low, Brushes.Goldenrod);
		}
		if (ownedNet != 0)
		{
			Level("Original stop", now, right, plannedStop, Brushes.OrangeRed);
			Level("Entry", now, right, entrySum / (double)entered, Brushes.LimeGreen);
		}
	}

	internal static int Cutoff(DateTime d)
	{
		return ("|2020.01.01|2020.01.20|2020.02.17|2020.04.10|2020.05.25|2020.07.03|2020.09.07|2020.11.26|2020.11.27|2020.12.24|2020.12.25|2020.12.31|2021.01.01|2021.01.18|2021.02.15|2021.04.02|2021.05.31|2021.07.05|2021.09.06|2021.11.25|2021.11.26|2021.12.24|2021.12.31|2022.01.17|2022.02.21|2022.04.15|2022.05.30|2022.06.20|2022.07.04|2022.09.05|2022.11.24|2022.11.25|2022.12.24|2022.12.26|2022.12.31|2023.01.02|2023.01.16|2023.02.20|2023.04.07|2023.05.29|2023.06.19|2023.07.03|2023.07.04|2023.09.04|2023.11.23|2023.11.24|2023.12.24|2023.12.25|2023.12.31|2024.01.01|2024.01.15|2024.02.19|2024.03.29|2024.05.27|2024.06.19|2024.07.03|2024.07.04|2024.09.02|2024.11.28|2024.11.29|2024.12.24|2024.12.25|2024.12.31|2025.01.01|2025.01.09|2025.01.20|2025.02.17|2025.04.18|2025.05.26|2025.06.19|2025.07.03|2025.07.04|2025.09.01|2025.11.27|2025.11.28|2025.12.24|2025.12.25|2025.12.31|".Contains("|" + d.ToString("yyyy.MM.dd") + "|") || (d.Year >= 2026 && FutureSpecial(d))) ? 715 : 870;
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
namespace NinjaTrader.NinjaScript.Indicators
{

public class OvernightBiasRangeBreakoutADX : Indicator
{
	internal sealed class AdxHistory
	{
		private readonly SortedList<DateTime, double> values = new SortedList<DateTime, double>();

		public void Clear()
		{
			values.Clear();
		}

		public void Store(DateTime end, double value)
		{
			int count = values.Count;
			if (count <= 0 || !(end > values.Keys[count - 1]) || value != values.Values[count - 1])
			{
				values[end] = value;
			}
		}

		public double At(DateTime end, double standalone)
		{
			if (values.Count == 0)
			{
				return standalone;
			}
			int num = 0;
			int num2 = values.Count;
			while (num < num2)
			{
				int num3 = num + (num2 - num) / 2;
				if (values.Keys[num3] <= end)
				{
					num = num3 + 1;
				}
				else
				{
					num2 = num3;
				}
			}
			return (num == 0) ? 0.0 : values.Values[num - 1];
		}
	}

	private NinjaTrader.NinjaScript.Strategies.OvernightBiasRangeBreakoutNT8.WilderAdx adx;

	private NinjaTrader.NinjaScript.Strategies.OvernightBiasRangeBreakoutNT8.Candle bucket;

	private TimeZoneInfo platformZone;

	private TimeZoneInfo centralZone;

	private DateTime lastMinute;

	private DateTime bucketKey;

	private int consumedSourceIndex = -1;

	private int signalSeries = 1;

	private bool bucketFinalized;

	private readonly AdxHistory published = new AdxHistory();

	internal void Publish(double value, DateTime sourceEnd, bool paint = true)
	{
		published.Store(sourceEnd, value);
		if (paint)
		{
			PaintCurrent();
		}
	}

	private void PaintCurrent()
	{
		if (platformZone != null && base.CurrentBars != null && base.CurrentBars[0] >= 0)
		{
			base.Values[0][0] = published.At(Central(base.Times[0][0]), (adx == null) ? 0.0 : adx.Value);
		}
	}

	internal void ResetPublished()
	{
		published.Clear();
	}

	internal void RefreshPublishedPlots()
	{
		if (platformZone != null && base.CurrentBars != null && base.CurrentBars[0] >= 0)
		{
			for (int i = 0; i <= base.CurrentBars[0]; i++)
			{
				base.Values[0][base.CurrentBars[0] - i] = published.At(Central(base.BarsArray[0].GetTime(i)), 0.0);
			}
		}
	}

	protected override void OnStateChange()
	{
		if (base.State == State.SetDefaults)
		{
			base.Name = "Overnight Bias ADX";
			base.Description = "Overnight model ADX(15): completed M1 data aggregated into fixed M15 bars, independent of the viewed timeframe.";
			base.Calculate = Calculate.OnEachTick;
			base.IsOverlay = false;
			base.IsSuspendedWhileInactive = false;
			base.BarsRequiredToPlot = 0;
			base.MaximumBarsLookBack = MaximumBarsLookBack.Infinite;
			AddPlot(new NinjaTrader.Gui.Stroke(System.Windows.Media.Brushes.SeaGreen, 2f), PlotStyle.Bar, "ADX15");
			AddLine(System.Windows.Media.Brushes.Goldenrod, 20.0, "Trend threshold");
		}
		else if (base.State == State.Configure)
		{
			if (base.Instrument.MasterInstrument.Name.Trim().Equals("NNQ", StringComparison.OrdinalIgnoreCase))
			{
				AddDataSeries(NinjaTrader.NinjaScript.Strategies.OvernightBiasRangeBreakoutNT8.SourceContract(base.Instrument), new BarsPeriod
				{
					BarsPeriodType = BarsPeriodType.Minute,
					Value = 1
				}, "CME US Index Futures ETH", true);
				AddDataSeries(base.Instrument.FullName, new BarsPeriod
				{
					BarsPeriodType = BarsPeriodType.Minute,
					Value = 1
				}, "CME US Index Futures ETH", true);
			}
			else
			{
				AddDataSeries(base.Instrument.FullName, new BarsPeriod
				{
					BarsPeriodType = BarsPeriodType.Minute,
					Value = 1
				}, "CME US Index Futures ETH", true);
			}
		}
		else
		{
			if (base.State != State.DataLoaded)
			{
				return;
			}
			adx = new NinjaTrader.NinjaScript.Strategies.OvernightBiasRangeBreakoutNT8.WilderAdx();
			bucket = null;
			lastMinute = (bucketKey = DateTime.MinValue);
			bucketFinalized = false;
			platformZone = NinjaTrader.Core.Globals.GeneralOptions.TimeZoneInfo;
			centralZone = TimeZoneInfo.FindSystemTimeZoneById("Central Standard Time");
			signalSeries = 1;
			if (base.Instrument.MasterInstrument.Name.Trim().Equals("NNQ", StringComparison.OrdinalIgnoreCase))
			{
				List<DateTime> starts = (from i in Enumerable.Range(0, base.BarsArray[2].Count)
					select Central(base.BarsArray[2].GetTime(i)).AddMinutes(-1.0)).ToList();
				if (NinjaTrader.NinjaScript.Strategies.OvernightBiasRangeBreakoutNT8.NativeHistorySufficient(starts, 10, 15, -60, 525, out var _))
				{
					signalSeries = 2;
				}
			}
		}
	}

	private DateTime Central(DateTime platform)
	{
		return TimeZoneInfo.ConvertTime(DateTime.SpecifyKind(platform, DateTimeKind.Unspecified), platformZone, centralZone);
	}

	protected override void OnBarUpdate()
	{
		if (base.BarsInProgress == 0)
		{
			PaintCurrent();
		}
		else if (base.BarsInProgress == signalSeries && base.CurrentBars[signalSeries] >= 0)
		{
			int num = base.CurrentBars[signalSeries] - ((base.State != State.Historical) ? 1 : 0);
			for (int i = consumedSourceIndex + 1; i <= num; i++)
			{
				ProcessSource(base.CurrentBars[signalSeries] - i);
				consumedSourceIndex = i;
			}
		}
	}

	private void ProcessSource(int ago)
	{
		DateTime dateTime = Central(base.Times[signalSeries][ago]);
		if (dateTime <= lastMinute)
		{
			return;
		}
		lastMinute = dateTime;
		NinjaTrader.NinjaScript.Strategies.OvernightBiasRangeBreakoutNT8.Candle candle = new NinjaTrader.NinjaScript.Strategies.OvernightBiasRangeBreakoutNT8.Candle(dateTime.AddMinutes(-1.0), base.Opens[signalSeries][ago], base.Highs[signalSeries][ago], base.Lows[signalSeries][ago], base.Closes[signalSeries][ago]);
		DateTime dateTime2 = candle.Start.Date.AddMinutes(Math.Floor(candle.Start.TimeOfDay.TotalMinutes / 15.0) * 15.0);
		if (bucket == null || dateTime2 != bucketKey)
		{
			if (bucket != null && !bucketFinalized)
			{
				adx.Push(bucket);
			}
			bucketKey = dateTime2;
			bucket = candle.Clone();
			bucketFinalized = false;
		}
		else
		{
			bucket.Merge(candle);
		}
		if (dateTime == dateTime2.AddMinutes(15.0))
		{
			adx.Push(bucket);
			bucketFinalized = true;
		}
		if (base.CurrentBars[0] >= 0 && Central(base.Times[0][0]) >= dateTime)
		{
			PaintCurrent();
		}
	}
}
}

namespace NinjaTrader.NinjaScript.Strategies
{
    public partial class Strategy : NinjaTrader.Gui.NinjaScript.StrategyRenderBase
    {
        public Indicators.OvernightBiasRangeBreakoutADX OvernightBiasModelDisplay()
        {
            return indicator.OvernightBiasModelDisplay(Input);
        }
    }
}
namespace NinjaTrader.NinjaScript.Indicators
{
    public partial class Indicator : NinjaTrader.Gui.NinjaScript.IndicatorRenderBase
    {
        private OvernightBiasRangeBreakoutADX[] cacheOvernightBiasModelDisplay;
        public OvernightBiasRangeBreakoutADX OvernightBiasModelDisplay(ISeries<double> input)
        {
            return CacheIndicator<OvernightBiasRangeBreakoutADX>(new OvernightBiasRangeBreakoutADX(), input, ref cacheOvernightBiasModelDisplay);
        }
    }
}
