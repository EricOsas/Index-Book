//+------------------------------------------------------------------+
//|                                               VaultBreakNT8.cs  |
//| Institutional Volatility Contraction & Vault Breakout            |
//| Developed by Osamwonyi Eric (You_FoundEric)                      |
//| Copyright 2026-2027, Osamwonyi Eric (@You_FoundEric)             |
//+------------------------------------------------------------------+

using NinjaTrader.Cbi;
using NinjaTrader.Core;
using NinjaTrader.Data;
using NinjaTrader.Gui.Chart;
using NinjaTrader.Gui;
using NinjaTrader.NinjaScript.DrawingTools;
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
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using System.Windows;
using System.Xml.Serialization;
using System;

namespace NinjaTrader.NinjaScript.Strategies
{
public class VaultBreakNT8 : Strategy
{
	private sealed class LevelSnapshot
	{
		internal DateTime From;

		internal DateTime To;

		internal double Price;

		internal System.Windows.Media.Brush Ink;

		internal DashStyleHelper Dash;
	}

	private sealed class Leg
	{
		internal string StopName;

		internal string TargetName;

		internal int Quantity;

		internal int Exited;

		internal double Entry;

		internal double Stop;

		internal double Target;

		internal double ExitValue;

		internal DateTime Entered;

		internal DateTime ExitTime;

		internal string VisualTag;
	}

	private static class VaultExecutionVisual
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
						if (strategy.State == State.Terminated || ((VaultBreakNT8)strategy).VisualsSuppressed)
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

		internal string FirstSetup;

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

		private const float FullHeight = 287f;

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
			float num5 = (flag ? 64f : 287f);
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
					Text(rt, dw, modelDashState.RangeCaption, 9f, brush3, num3 + 20f, num4 + 95f, 146f);
					Text(rt, dw, modelDashState.RangeWidth, 13f, solidColorBrush, num6 - 76f, num4 + 92f, 76f, bold: true, right: true);
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
					Text(rt, dw, modelDashState.FirstSetup, 8.5f, brush3, num3 + 20f, num4 + 248f, 222f);
					Text(rt, dw, modelDashState.History, 8.5f, brush3, num3 + 20f, num4 + 268f, 222f);
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

	public sealed class Minute
	{
		public DateTime Start;

		public double Open;

		public double High;

		public double Low;

		public double Close;

		public double Volume;
	}

	public sealed class Model
	{
		public const double StopDistance = 60.0;

		public const double TargetDistance = 40.0;

		public static readonly TimeZoneInfo CentralZone = TimeZoneInfo.FindSystemTimeZoneById("Central Standard Time");

		public DateTime Day;

		public DateTime LatestMinute;

		public DateTime PreviousVwapTime;

		public DateTime VirtualStart;

		public DateTime MissingStart;

		public DateTime MissingEnd;

		public double Sop;

		public double NoiseUp;

		public double Vwap;

		public double PreviousVwap;

		public double SignalClose;

		public double VirtualStop;

		public double VirtualTarget;

		public double VirtualFill;

		public double VirtualExit;

		public DateTime VirtualExitBar = DateTime.MinValue;

		public string VirtualDisplay = "waiting";

		public bool Ready;

		public bool VirtualActive;

		public bool VirtualPending;

		public bool HasSessionOpen;

		public string BlockReason = "Waiting for completed M1 history";

		public int Attempts;

		public int RejectedDays;

		private double pv;

		private double volume;

		private DateTime previousMinute;

		private DateTime dailyDay;

		private DateTime virtualExpected;

		private DateTime sourceStart;

		private double dailyHigh;

		private double dailyLow;

		private double dailyClose;

		private double priorDailyClose;

		private double sourceClose;

		private int sourceCount;

		private readonly Queue<double> trueRanges = new Queue<double>();

		private readonly Queue<double> atrValues = new Queue<double>();

		private double trueRangeTotal;

		private bool initialDailyBucket = true;

		private const string SpecialDates = "|2020.01.01|2020.01.20|2020.02.17|2020.04.10|2020.05.25|2020.07.03|2020.09.07|2020.11.26|2020.11.27|2020.12.24|2020.12.25|2020.12.31|2021.01.01|2021.01.18|2021.02.15|2021.04.02|2021.05.31|2021.07.05|2021.09.06|2021.11.25|2021.11.26|2021.12.24|2021.12.31|2022.01.17|2022.02.21|2022.04.15|2022.05.30|2022.06.20|2022.07.04|2022.09.05|2022.11.24|2022.11.25|2022.12.24|2022.12.26|2022.12.31|2023.01.02|2023.01.16|2023.02.20|2023.04.07|2023.05.29|2023.06.19|2023.07.03|2023.07.04|2023.09.04|2023.11.23|2023.11.24|2023.12.24|2023.12.25|2023.12.31|2024.01.01|2024.01.15|2024.02.19|2024.03.29|2024.05.27|2024.06.19|2024.07.03|2024.07.04|2024.09.02|2024.11.28|2024.11.29|2024.12.24|2024.12.25|2024.12.31|2025.01.01|2025.01.09|2025.01.20|2025.02.17|2025.04.18|2025.05.26|2025.06.19|2025.07.03|2025.07.04|2025.09.01|2025.11.27|2025.11.28|2025.12.24|2025.12.25|2025.12.31|";

		public double AverageAtr => (atrValues.Count == 15) ? (atrValues.Sum() / 15.0) : 0.0;

		public static int MigrateAttempts(int saved, bool legacy)
		{
			if (saved < 0 || saved > (legacy ? 4 : 3))
			{
				throw new ArgumentOutOfRangeException("saved");
			}
			return legacy ? Math.Max(0, saved - 1) : saved;
		}

		public static int Size(double budget, double unit)
		{
			return (!(budget <= 0.0) && !(unit <= 0.0) && !double.IsNaN(unit) && !double.IsInfinity(unit)) ? ((int)Math.Min(2147483647.0, Math.Floor(budget / unit + 1E-09))) : 0;
		}

		public void BeginLive(DateTime now)
		{
			Attempts = 0;
			VirtualActive = (VirtualPending = false);
			VirtualStart = DateTime.MinValue;
			VirtualFill = (VirtualExit = 0.0);
			VirtualExitBar = DateTime.MinValue;
			VirtualDisplay = "waiting";
			if (Day != now.Date)
			{
				Ready = false;
			}
		}

		public void ExpireVirtual()
		{
			if (VirtualActive || VirtualPending)
			{
				VirtualDisplay = "expired";
			}
			VirtualActive = (VirtualPending = false);
		}

		public void RestoreVirtual(bool active, bool pending, DateTime start, double stop, double target)
		{
			VirtualActive = active;
			VirtualPending = pending;
			VirtualStart = start;
			VirtualStop = stop;
			VirtualTarget = target;
			virtualExpected = LatestMinute.AddMinutes(1.0);
		}

		public bool MinuteClosed(Minute b, bool enabled)
		{
			DateTime dateTime = b.Start.AddMinutes(1.0);
			if (previousMinute != DateTime.MinValue && b.Start <= previousMinute)
			{
				return false;
			}
			if (!Finite(b.Open) || !Finite(b.High) || !Finite(b.Low) || !Finite(b.Close) || !Finite(b.Volume) || b.Volume < 0.0 || b.High < b.Low || b.Open < b.Low || b.Open > b.High || b.Close < b.Low || b.Close > b.High)
			{
				Ready = false;
				BlockReason = "Invalid M1 OHLC/volume; new entries blocked";
				VirtualActive = (VirtualPending = false);
				previousMinute = b.Start;
				return false;
			}
			UpdateDaily(b);
			if (Day != b.Start.Date)
			{
				Day = b.Start.Date;
				Attempts = 0;
				pv = (volume = (Vwap = 0.0));
				PreviousVwapTime = DateTime.MinValue;
				VirtualActive = (VirtualPending = false);
				VirtualFill = (VirtualExit = 0.0);
				VirtualExitBar = DateTime.MinValue;
				VirtualDisplay = "waiting";
				HasSessionOpen = b.Start.TimeOfDay == TimeSpan.Zero;
				MissingStart = (MissingEnd = DateTime.MinValue);
				Ready = HasSessionOpen && AverageAtr > 0.0;
				ref string blockReason = ref BlockReason;
				string text;
				if (HasSessionOpen)
				{
					text = ((AverageAtr <= 0.0) ? ("ATR warm-up " + atrValues.Count + " / 15 daily samples; load at least 45 calendar days of ETH M1 history") : "Waiting for a fresh completed M30 signal");
				}
				else
				{
					text = "Missing Central midnight M1 session open; use ETH history";
				}
				blockReason = text;
				Sop = b.Open;
				NoiseUp = Sop + 0.3 * AverageAtr;
				sourceCount = 0;
				sourceStart = DateTime.MinValue;
				if (!Ready)
				{
					RejectedDays++;
				}
			}
			bool flag = b.Start.TimeOfDay == TimeSpan.Zero || previousMinute == DateTime.MinValue || b.Start == previousMinute.AddMinutes(1.0);
			if (!flag && b.Start.TimeOfDay.TotalMinutes < (double)Cutoff(Day))
			{
				if (Ready)
				{
					RejectedDays++;
				}
				Ready = false;
				BlockReason = "Missing M1 " + previousMinute.AddMinutes(1.0).ToString("yyyy-MM-dd HH:mm") + " to " + b.Start.ToString("HH:mm") + " Central; source history incomplete";
				if (MissingStart == DateTime.MinValue)
				{
					MissingStart = previousMinute.AddMinutes(1.0);
					MissingEnd = b.Start;
				}
				VirtualActive = (VirtualPending = false);
			}
			previousMinute = (LatestMinute = b.Start);
			PreviousVwapTime = ((flag && volume > 0.0 && b.Start.TimeOfDay != TimeSpan.Zero) ? b.Start.AddMinutes(-1.0) : DateTime.MinValue);
			PreviousVwap = Vwap;
			pv += (b.High + b.Low + b.Close) / 3.0 * b.Volume;
			volume += b.Volume;
			if (volume > 0.0)
			{
				Vwap = pv / volume;
			}
			if (dateTime.TimeOfDay.TotalMinutes >= (double)Cutoff(Day))
			{
				ExpireVirtual();
				return false;
			}
			if (VirtualPending)
			{
				if (b.Start != VirtualStart)
				{
					Ready = false;
					VirtualPending = false;
					RejectedDays++;
					return false;
				}
				VirtualStop = b.Open - 60.0;
				VirtualTarget = b.Open + 40.0;
				VirtualFill = b.Open;
				VirtualExit = 0.0;
				VirtualExitBar = DateTime.MinValue;
				VirtualDisplay = "open";
				VirtualPending = false;
				VirtualActive = true;
				virtualExpected = b.Start;
			}
			if (VirtualActive)
			{
				if (b.Start != virtualExpected)
				{
					Ready = false;
					VirtualActive = false;
					RejectedDays++;
					return false;
				}
				virtualExpected = b.Start.AddMinutes(1.0);
				int num = VirtualOutcome(b.Low, b.High, VirtualStop, VirtualTarget);
				if (num != 0)
				{
					VirtualActive = false;
					VirtualExit = ((num < 0) ? VirtualStop : VirtualTarget);
					VirtualDisplay = ((num < 0) ? "SL" : "TP");
					VirtualExitBar = b.Start.AddMinutes(1.0);
				}
			}
			DateTime dateTime2 = b.Start.Date.AddMinutes(Math.Floor(b.Start.TimeOfDay.TotalMinutes / 30.0) * 30.0);
			if (sourceStart != dateTime2)
			{
				sourceStart = dateTime2;
				sourceCount = 0;
			}
			sourceCount++;
			sourceClose = b.Close;
			if (dateTime.TimeOfDay.TotalMinutes % 30.0 != 0.0 || sourceCount != 30)
			{
				return false;
			}
			SignalClose = sourceClose;
			if (!enabled || !Ready || volume <= 0.0 || VirtualActive || VirtualPending || Attempts >= 3 || dateTime.TimeOfDay.TotalMinutes < 600.0 || dateTime.TimeOfDay.TotalMinutes >= (double)Cutoff(Day) || sourceClose <= NoiseUp || sourceClose <= Vwap)
			{
				return false;
			}
			return true;
		}

		private void UpdateDaily(Minute b)
		{
			DateTime dateTime = b.Start.Date.AddHours(16.0);
			if (b.Start < dateTime)
			{
				dateTime = dateTime.AddDays(-1.0);
			}
			if (dailyDay != dateTime)
			{
				if (dailyDay != DateTime.MinValue)
				{
					double num = Math.Max(dailyHigh - dailyLow, (priorDailyClose == 0.0) ? 0.0 : Math.Max(Math.Abs(dailyHigh - priorDailyClose), Math.Abs(dailyLow - priorDailyClose)));
					if (!initialDailyBucket)
					{
						trueRanges.Enqueue(num);
						trueRangeTotal += num;
						if (trueRanges.Count > 15)
						{
							trueRangeTotal -= trueRanges.Dequeue();
						}
						if (trueRanges.Count == 15)
						{
							atrValues.Enqueue(trueRangeTotal / 15.0);
							if (atrValues.Count > 15)
							{
								atrValues.Dequeue();
							}
						}
					}
					initialDailyBucket = false;
					priorDailyClose = dailyClose;
				}
				dailyDay = dateTime;
				dailyHigh = b.High;
				dailyLow = b.Low;
			}
			dailyHigh = Math.Max(dailyHigh, b.High);
			dailyLow = Math.Min(dailyLow, b.Low);
			dailyClose = b.Close;
		}

		public static int VirtualOutcome(double low, double high, double stop, double target)
		{
			return (low <= stop) ? (-1) : ((high >= target) ? 1 : 0);
		}

		private static bool Finite(double value)
		{
			return !double.IsNaN(value) && !double.IsInfinity(value);
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

		private static DateTime Monday(int y, int m, int n)
		{
			DateTime dateTime = new DateTime(y, m, 1);
			return dateTime.AddDays((int)(8 - dateTime.DayOfWeek) % 7 + 7 * (n - 1));
		}

		private static DateTime GoodFriday(int y)
		{
			int num = y % 19;
			int num2 = y / 100;
			int num3 = y % 100;
			int num4 = (num2 + 8) / 25;
			int num5 = (num2 - num4 + 1) / 3;
			int num6 = (19 * num + num2 - num2 / 4 - num5 + 15) % 30;
			int num7 = num3 / 4;
			int num8 = num3 % 4;
			int num9 = (32 + 2 * (num2 % 4) + 2 * num7 - num6 - num8) % 7;
			int num10 = (num + 11 * num6 + 22 * num9) / 451;
			int num11 = num6 + num9 - 7 * num10 + 114;
			return new DateTime(y, num11 / 31, num11 % 31 + 1).AddDays(-2.0);
		}

		public static bool FutureSpecial(DateTime d)
		{
			int year = d.Year;
			DateTime dateTime = new DateTime(year, 6, 1).AddDays(-1.0);
			DateTime dateTime2 = dateTime.AddDays((0 - (dateTime.DayOfWeek + 6)) % 7);
			DateTime dateTime3 = new DateTime(year, 11, 1);
			DateTime dateTime4 = dateTime3.AddDays((int)(11 - dateTime3.DayOfWeek) % 7 + 21);
			return d == Observed(new DateTime(year, 1, 1)) || d == Monday(year, 1, 3) || d == Monday(year, 2, 3) || d == GoodFriday(year) || d == dateTime2 || d == Observed(new DateTime(year, 6, 19)) || d == new DateTime(year, 7, 3) || d == Observed(new DateTime(year, 7, 4)) || d == Monday(year, 9, 1) || d == dateTime4 || d == dateTime4.AddDays(1.0) || d == new DateTime(year, 12, 24) || d == Observed(new DateTime(year, 12, 25)) || d == new DateTime(year, 12, 31);
		}

		public static int Cutoff(DateTime day)
		{
			string value = "|" + day.ToString("yyyy.MM.dd", CultureInfo.InvariantCulture) + "|";
			return ("|2020.01.01|2020.01.20|2020.02.17|2020.04.10|2020.05.25|2020.07.03|2020.09.07|2020.11.26|2020.11.27|2020.12.24|2020.12.25|2020.12.31|2021.01.01|2021.01.18|2021.02.15|2021.04.02|2021.05.31|2021.07.05|2021.09.06|2021.11.25|2021.11.26|2021.12.24|2021.12.31|2022.01.17|2022.02.21|2022.04.15|2022.05.30|2022.06.20|2022.07.04|2022.09.05|2022.11.24|2022.11.25|2022.12.24|2022.12.26|2022.12.31|2023.01.02|2023.01.16|2023.02.20|2023.04.07|2023.05.29|2023.06.19|2023.07.03|2023.07.04|2023.09.04|2023.11.23|2023.11.24|2023.12.24|2023.12.25|2023.12.31|2024.01.01|2024.01.15|2024.02.19|2024.03.29|2024.05.27|2024.06.19|2024.07.03|2024.07.04|2024.09.02|2024.11.28|2024.11.29|2024.12.24|2024.12.25|2024.12.31|2025.01.01|2025.01.09|2025.01.20|2025.02.17|2025.04.18|2025.05.26|2025.06.19|2025.07.03|2025.07.04|2025.09.01|2025.11.27|2025.11.28|2025.12.24|2025.12.25|2025.12.31|".Contains(value) || (day.Year >= 2026 && FutureSpecial(day.Date))) ? 715 : 870;
		}

		public static bool SelfTest()
		{
			return VirtualOutcome(99.0, 111.0, 100.0, 110.0) == -1 && VirtualOutcome(101.0, 110.0, 100.0, 110.0) == 1 && Size(250.0, 1408.2) == 0 && Size(250.0, 147.12) == 1 && Cutoff(new DateTime(2025, 1, 9)) == 715 && Cutoff(new DateTime(2022, 1, 24)) == 870;
		}
	}

	private Model model;

	private TimeZoneInfo platformZone;

	private DateTime activation;

	private DateTime latest;

	private DateTime lastEnd;

	private int consumedSourceIndex = -1;

	private BarsRequest historyRepair;

	private readonly SortedDictionary<DateTime, Minute> providerBars = new SortedDictionary<DateTime, Minute>();

	private DateTime repairRetry;

	private DateTime recoveryFloor;

	private bool historyWaiting;

	private string owner;

	private string ownerKey;

	private string statePath;

	private string status = "Loading completed M1 history";

	private string brokerRestrictionReason;

	private bool acquired;

	private bool suspended;

	private bool closing;

	private bool driving;

	private bool exposureLease;

	private bool closeRejected;

	private string pendingCloseName;

	private int pendingCloseQuantity;

	private int pendingCloseExecuted;

	private int pendingCloseFinal = -1;

	private int ownedNet;

	private int sequence;

	private int fundedTrades;

	private int executionSeries = 1;

	private int signalSeries = 1;

	private string signalContract;

	private Order entryOrder;

	private Order closeOrder;

	private string entryName;

	private double requestStop;

	private double requestTarget;

	private double grossNet;

	private double modelCosts;

	private readonly Dictionary<string, Order> orders = new Dictionary<string, Order>();

	private readonly Dictionary<string, int> processed = new Dictionary<string, int>();

	private readonly HashSet<string> executions = new HashSet<string>();

	private readonly List<Leg> legs = new List<Leg>();

	private static readonly object OwnerGate = new object();

	private static readonly HashSet<string> Owners = new HashSet<string>();

	private Timer timer;

	private StreamWriter audit;

	private Dictionary<string, LevelSnapshot> visualLevels;

	private DateTime plotSession = DateTime.MinValue;

	private int plotBreakBar = -1;

	private int writtenPlotBar = -1;

	private double writtenPlotValue = double.NaN;

	private System.Windows.Media.Brush writtenPlotInk;

	private bool nativeDisplayQueued;

	private bool historicalVisualsPainted;

	private readonly Dictionary<IDrawingTool, bool> hiddenVisuals = new Dictionary<IDrawingTool, bool>();

	private bool visualSyncQueued;

	private readonly List<Leg> closedVisuals = new List<Leg>();

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

	private static readonly object FeeGate = new object();

	private static readonly Dictionary<string, List<FeeTrade>> FeeHistory = new Dictionary<string, List<FeeTrade>>();

	private FeeTrade feeTrade;

	private const string ExposureSlot = "NQIndependentModelExposure.v1";

	private string legacyExternalCloseConfirmation = string.Empty;

	private const int StrategyId = 91008010;

	private bool VisualsSuppressed => VisualFaultReason() != null;

	private Instrument ExecutionInstrument => BarsArray[0].Instrument;

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

	private double Friction => EffectiveCommission + 2.0 * SlippagePrice * ExecutionInstrument.MasterInstrument.PointValue;

	private string ExposureKey => ((Account == null) ? "unknown" : Account.Name) + "|" + ExecutionInstrument.FullName;

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
	[Display(Name = "Block opposing index exposure", Description = "Reject opposing index positions or pending entries across locally visible NT8 accounts. Exact-instrument ownership isolation remains active.", GroupName = "01 Trading", Order = 1)]
	public bool BlockOpposingExposure { get; set; }

	[NinjaScriptProperty]
	[Range(0.01, double.MaxValue)]
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
	[Display(Name = "Show chart levels and information", GroupName = "04 Display", Order = 0)]
	public bool ShowVisuals { get; set; }

	[NinjaScriptProperty]
	[Display(Name = "Write detailed decision and trade log", GroupName = "05 Logging", Order = 0)]
	public bool WriteTradeLog { get; set; }

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
		if (historyWaiting || historyRepair != null)
		{
			return status;
		}
		if (model != null && !model.Ready)
		{
			return model.BlockReason;
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
				if (VisualsSuppressed)
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

	private void DrawFundedTrade(Leg leg, DateTime now)
	{
		if (ShowVisuals && leg != null && !string.IsNullOrEmpty(leg.VisualTag))
		{
			bool flag = leg.Exited >= leg.Quantity;
			if (flag && !closedVisuals.Contains(leg))
			{
				closedVisuals.Add(leg);
				historicalVisualsPainted = false;
			}
			double num = (flag ? (leg.ExitValue / (double)leg.Exited) : 0.0);
			DateTime t = (flag ? leg.ExitTime : now);
			double slPx = ((flag && num <= leg.Entry) ? num : leg.Stop);
			VaultExecutionVisual.DrawExecutionTool(this, leg.VisualTag, isLong: true, leg.Entry, slPx, (flag && num > leg.Entry) ? num : 0.0, leg.Entry, Platform(leg.Entered), Platform(leg.Entered), Platform(t), trailing: false, DateTime.MinValue, synthetic: false);
		}
	}

	protected override void OnStateChange()
	{
		if (State == State.SetDefaults)
		{
			Name = "VaultBreak";
			Description = "VaultBreak - Long-only session breakout strategy. Requires a completed 30-minute close above session VWAP and the session-open plus 0.3x average daily ATR noise band, from 10:00 Central until cutoff. Uses risk-based brackets, cost-aware sizing and persistent owned-trade recovery. Copyright 2026-2027, Osamwonyi Eric (@You_FoundEric). Developed by @You_FoundEric. Unauthorized redistribution prohibited.";
			Calculate = Calculate.OnEachTick;
			StartBehavior = StartBehavior.ImmediatelySubmit;
			IsAdoptAccountPositionAware = false;
			IsExitOnSessionCloseStrategy = false;
			IsUnmanaged = true;
			RealtimeErrorHandling = RealtimeErrorHandling.IgnoreAllErrors;
			ConnectionLossHandling = ConnectionLossHandling.KeepRunning;
			BarsRequiredToTrade = 0;
			IncludeCommission = false;
			Slippage = 1.0;
			MaximumBarsLookBack = MaximumBarsLookBack.Infinite;
			IsFillLimitOnTouch = true;
			StopTargetHandling = StopTargetHandling.PerEntryExecution;
			TimeInForce = TimeInForce.Gtc;
			OrderFillResolution = OrderFillResolution.Standard;
			IsOverlay = true;
			DrawOnPricePanel = true;
			IsInstantiatedOnEachOptimizationIteration = true;
			AddPlot(new Stroke(Brushes.DeepSkyBlue, DashStyleHelper.Dot, 1f), PlotStyle.Line, "Session VWAP");
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
				throw new InvalidOperationException("VaultBreak requires ImmediatelySubmit.");
			}
			if (RiskMoney <= 0.0 || double.IsNaN(RiskMoney) || double.IsInfinity(RiskMoney) || FixedContracts <= 0 || CommissionPerContract < 0.0 || SlippagePoints < 0)
			{
				throw new InvalidOperationException("Invalid risk, costs or ownership identifier.");
			}
			IncludeCommission = false;
			IncludeTradeHistoryInBacktest = true;
			signalContract = SourceContract(Instrument);
			executionSeries = (signalSeries = 1);
			if (!Instrument.MasterInstrument.Name.Trim().Equals("NQ", StringComparison.OrdinalIgnoreCase))
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
			string text = ExecutionInstrument.MasterInstrument.Name.ToUpperInvariant();
			if (text != "NQ" && text != "MNQ" && text != "NNQ")
			{
				throw new InvalidOperationException("VaultBreak supports NQ/MNQ/NNQ; selected " + ExecutionInstrument.FullName + ".");
			}
			if (ExecutionInstrument.MasterInstrument.PointValue <= 0.0 || ExecutionInstrument.MasterInstrument.TickSize <= 0.0)
			{
				throw new InvalidOperationException("Invalid contract specification.");
			}
			if (executionSeries == 2 && (BarsArray.Length < 3 || BarsArray[1].Instrument.FullName != signalContract || BarsArray[2].Instrument.FullName != ExecutionInstrument.FullName))
			{
				throw new InvalidOperationException("NQ signal / selected-contract execution series mismatch; trading blocked.");
			}
			platformZone = Globals.GeneralOptions.TimeZoneInfo;
			model = new Model();
			signalSeries = 1;
			visualLevels = new Dictionary<string, LevelSnapshot>();
			plotSession = DateTime.MinValue;
			plotBreakBar = (writtenPlotBar = -1);
			writtenPlotValue = double.NaN;
			writtenPlotInk = null;
			owner = "VB" + 91008010 + "_" + Guid.NewGuid().ToString("N").Substring(0, 12);
			PrepareRecovery();
			if (WriteTradeLog)
			{
				string text2 = Path.Combine(Globals.UserDataDir, "VaultBreak", "logs");
				Directory.CreateDirectory(text2);
				audit = new StreamWriter(Path.Combine(text2, owner + ".csv"));
				audit.AutoFlush = true;
				audit.WriteLine("central_time,event,detail");
			}
			if (!Model.SelfTest())
			{
				throw new InvalidOperationException("VaultBreak model self-test failed.");
			}
		}
		else if (State == State.Transition)
		{
			if (ownedNet != 0 || orders.Values.Any(Working))
			{
				throw new InvalidOperationException("Historical owned exposure must be flat before realtime.");
			}
		}
		else if (State == State.Realtime)
		{
			activation = Central(NativeNow());
			ownerKey = ((Account == null) ? "unknown" : Account.Name) + "|" + ExecutionInstrument.FullName + "|" + 91008010;
			lock (OwnerGate)
			{
				acquired = Owners.Add(ownerKey);
			}
			if (!acquired)
			{
				suspended = true;
				status = "Duplicate strategy owner: new orders blocked";
			}
			string text3 = Path.Combine(Globals.UserDataDir, "VaultBreak", "state");
			Directory.CreateDirectory(text3);
			string text4 = ownerKey;
			char[] invalidFileNameChars = Path.GetInvalidFileNameChars();
			foreach (char oldChar in invalidFileNameChars)
			{
				text4 = text4.Replace(oldChar, '_');
			}
			statePath = Path.Combine(text3, text4 + ".state");
			model.BeginLive(activation);
			if (acquired)
			{
				RestoreState();
			}
			StartRecovery();
			if (model.MissingStart != DateTime.MinValue && model.Day == activation.Date)
			{
				RequestSourceRepair(model.MissingEnd, model.MissingStart);
			}
			if (!recoveredTrade && Account != null && Account.Orders.Any((Order o) => o.Instrument.FullName == ExecutionInstrument.FullName && o.Name != null && o.Name.StartsWith("VB" + 91008010 + "_", StringComparison.Ordinal) && Working(o)))
			{
				suspended = true;
				status = "Previous-instance protection exists: reconcile before enabling";
			}
			SaveState();
			timer = new Timer((object o) =>
			{
				if (State == State.Realtime)
				{
					TriggerCustomEvent((object ignored) =>
					{
						if (State == State.Realtime)
						{
							TimerStep();
						}
					}, null);
				}
			}, null, 1000, 1000);
		}
		else
		{
			if (State != State.Terminated)
			{
				return;
			}
			HookDashboardMouse(attach: false);
			if (historyRepair != null)
			{
				historyRepair.Dispose();
				historyRepair = null;
			}
			StopRecovery();
			if (timer != null)
			{
				timer.Dispose();
				timer = null;
			}
			SaveState();
			if (acquired)
			{
				lock (OwnerGate)
				{
					Owners.Remove(ownerKey);
				}
				acquired = false;
			}
			ReleaseExposureIfSettled();
			if (audit != null)
			{
				audit.Dispose();
				audit = null;
			}
			if (model != null)
			{
				Print("VaultBreak SUMMARY funded=" + fundedTrades + " gross=" + F(grossNet) + " modeledCosts=" + F(modelCosts) + " adjusted=" + F(grossNet - modelCosts) + " rejectedDays=" + model.RejectedDays + "; Analyzer/Playback parity remains a separate gate.");
			}
		}
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
			recoveryIdentity = text + "|" + Account.Name + "|" + ExecutionInstrument.FullName + "|Vault60_40_funded3";
			string text2 = recoveryIdentity;
			char[] invalidFileNameChars = Path.GetInvalidFileNameChars();
			foreach (char oldChar in invalidFileNameChars)
			{
				text2 = text2.Replace(oldChar, '_');
			}
			recoveryPath = Path.Combine(Globals.UserDataDir, "VaultBreak", "recovery", text2 + ".journal");
			recoveryPending = File.Exists(recoveryPath);
		}
	}

	private void StartRecovery()
	{
		if (recoveryPath == null || !acquired)
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
				if (binaryReader.ReadString() != "IndependentNT8.v1" || binaryReader.ReadString() != recoveryIdentity)
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
				model.Day = new DateTime(binaryReader.ReadInt64());
				model.Attempts = binaryReader.ReadInt32();
				requestStop = binaryReader.ReadDouble();
				requestTarget = binaryReader.ReadDouble();
				legs.Clear();
				int num = binaryReader.ReadInt32();
				if (num < 0 || num > 1000)
				{
					throw new InvalidDataException("Invalid leg count");
				}
				for (int i = 0; i < num; i++)
				{
					legs.Add(new Leg
					{
						Entry = binaryReader.ReadDouble(),
						Stop = binaryReader.ReadDouble(),
						Target = binaryReader.ReadDouble(),
						Quantity = binaryReader.ReadInt32(),
						Exited = binaryReader.ReadInt32(),
						ExitValue = binaryReader.ReadDouble(),
						Entered = new DateTime(binaryReader.ReadInt64()),
						ExitTime = new DateTime(binaryReader.ReadInt64()),
						StopName = binaryReader.ReadString(),
						TargetName = binaryReader.ReadString(),
						VisualTag = binaryReader.ReadString()
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
				// Old v1 journals end here; only a restricted checkpoint carries this tagged extension.
				brokerRestrictionReason = null;
				if (binaryReader.BaseStream.Position < binaryReader.BaseStream.Length)
				{
					if (binaryReader.ReadString() != "BrokerAccountRestriction.v1")
						throw new InvalidDataException("Unknown journal extension");
					brokerRestrictionReason = binaryReader.ReadString();
					if (string.IsNullOrEmpty(brokerRestrictionReason))
						throw new InvalidDataException("Empty broker account restriction");
				}
				if (binaryReader.BaseStream.Position != binaryReader.BaseStream.Length)
				{
					throw new InvalidDataException("Unexpected journal data");
				}
			}
			recoveredTrade = ownedNet != 0 || !Settled(entryOrder) || closing;
			recoveryReady = true;
			if (!string.IsNullOrEmpty(brokerRestrictionReason))
			{
				RecoveryBlock("Broker account restriction remains latched; verify broker account and owned protection: " + brokerRestrictionReason);
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
				binaryWriter.Write("IndependentNT8.v1");
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
				binaryWriter.Write(model.Day.Ticks);
				binaryWriter.Write(model.Attempts);
				binaryWriter.Write(requestStop);
				binaryWriter.Write(requestTarget);
				binaryWriter.Write(legs.Count);
				foreach (Leg leg in legs)
				{
					binaryWriter.Write(leg.Entry);
					binaryWriter.Write(leg.Stop);
					binaryWriter.Write(leg.Target);
					binaryWriter.Write(leg.Quantity);
					binaryWriter.Write(leg.Exited);
					binaryWriter.Write(leg.ExitValue);
					binaryWriter.Write(leg.Entered.Ticks);
					binaryWriter.Write(leg.ExitTime.Ticks);
					binaryWriter.Write(leg.StopName);
					binaryWriter.Write(leg.TargetName);
					binaryWriter.Write(leg.VisualTag ?? "");
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
				if (!string.IsNullOrEmpty(brokerRestrictionReason))
				{
					binaryWriter.Write("BrokerAccountRestriction.v1");
					binaryWriter.Write(brokerRestrictionReason);
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
			entryOrder = null;
			closeOrder = null;
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
			legs.Clear();
			recoveredTrade = true;
			connectionPaused = true;
			SaveRecovery(allowDuringReconciliation: true);
			if (recoveryFault)
			{
				return true;
			}
			recoveryPending = false;
			ReleaseExposureIfSettled(text3);
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
				if (order.Name == entryName)
				{
					entryOrder = order;
				}
				if (order.Name.Contains("_CLOSE_"))
				{
					closeOrder = order;
				}
				processed.TryGetValue(order.Name, out var value3);
				if (value2.Filled < value3)
				{
					throw new InvalidDataException("Broker filled quantity regressed: " + order.Name);
				}
				if (value2.Filled > value3)
				{
					int num = value2.Filled - value3;
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
			if (!flag && entryName != null && recoveryRequests.TryGetValue(entryName, out var value4) && value4.Processed < value4.Quantity && !orders.ContainsKey(entryName))
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
			if (ownedNet != 0)
			{
				lock (AppDomain.CurrentDomain)
				{
					if (AppDomain.CurrentDomain.GetData("NQIndependentModelExposure.v1") is Dictionary<string, string> dictionary && dictionary.TryGetValue(ExposureKey, out var value5) && value5 == owner)
					{
						exposureLease = true;
					}
				}
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
			foreach (Leg item in legs.Where((Leg l) => l.Quantity > l.Exited))
			{
				int num2 = Math.Min(num, item.Quantity - item.Exited);
				if (num2 <= 0)
				{
					continue;
				}
				num -= num2;
				orders.TryGetValue(item.StopName, out var value);
				orders.TryGetValue(item.TargetName, out var value2);
				if (Working(value) && value.Quantity - value.Filled != num2)
				{
					if (value.Quantity - value.Filled > num2)
					{
						RecoveryCancel(value);
					}
					throw new InvalidDataException("Owned working stop quantity differs from remaining leg");
				}
				if (Working(value2) && value2.Quantity - value2.Filled != num2)
				{
					if (value2.Quantity - value2.Filled > num2)
					{
						RecoveryCancel(value2);
					}
					throw new InvalidDataException("Owned working target quantity differs from remaining leg");
				}
				if (!ReadyForProtectionRepair(value, value2, item.StopName, item.TargetName))
				{
					return false;
				}
				if (!Working(value) || !Working(value2))
				{
					double currentBid = GetCurrentBid(executionSeries);
					if (currentBid <= 0.0 || item.Stop >= currentBid || item.Target <= currentBid)
					{
						throw new InvalidDataException("Missing protection cannot be restored at crossed or unavailable market levels");
					}
					string oco = owner + "_RepairOco_" + ++sequence;
					if (!Working(value))
					{
						string text = owner;
						int num3 = ++sequence;
						item.StopName = text + "_RepairStop_" + num3;
						value = RecoverySubmit(executionSeries, OrderAction.Sell, OrderType.StopMarket, num2, 0.0, item.Stop, oco, item.StopName);
					}
					if (value == null || value.OrderState == OrderState.Rejected)
					{
						throw new InvalidDataException("Recovered owned stop was not accepted");
					}
					if (!Working(value2))
					{
						string text2 = owner;
						int num3 = ++sequence;
						item.TargetName = text2 + "_RepairTarget_" + num3;
						RecoverySubmit(executionSeries, OrderAction.Sell, OrderType.Limit, num2, item.Target, 0.0, oco, item.TargetName);
					}
				}
			}
			if (num != 0)
			{
				throw new InvalidDataException("Remaining owned exposure has no journalled leg");
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

	private void TimerStep()
	{
		if (State == State.Realtime)
		{
			DateTime dateTime = Central(NativeNow());
			bool flag = ObserveBrokerConnection(dateTime);
			HandleDeadline(dateTime);
			if (flag)
			{
				CheckProtection();
				DriveClose();
				ReleaseExposureIfSettled();
				ConsumeSource();
			}
			Paint(dateTime);
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
			Paint(Central(NativeNow()));
		}
		else if (model != null)
		{
			if (BarsInProgress == 0)
			{
				RefreshVwapPlot();
			}
			else if (BarsInProgress == signalSeries && CurrentBars[signalSeries] >= 0)
			{
				ConsumeSource();
			}
		}
	}

	private void ConsumeSource()
	{
		if (historyRepair != null || State == State.Terminated)
		{
			return;
		}
		if (State == State.Realtime && model.MissingStart != DateTime.MinValue && model.Day == Central(NativeNow()).Date)
		{
			RequestSourceRepair(model.MissingEnd, model.MissingStart);
			return;
		}
		int num = ((State == State.Realtime) ? (BarsArray[signalSeries].Count - 2) : CurrentBars[signalSeries]);
		for (int i = consumedSourceIndex + 1; i <= num; i++)
		{
			DateTime dateTime = Central(BarsArray[signalSeries].GetTime(i));
			if (dateTime <= lastEnd)
			{
				consumedSourceIndex = i;
				continue;
			}
			DateTime dateTime2 = dateTime.AddMinutes(-1.0);
			if (State == State.Realtime && lastEnd != DateTime.MinValue && lastEnd <= dateTime2.Date && dateTime2 > dateTime2.Date && dateTime2.TimeOfDay.TotalMinutes < (double)Model.Cutoff(dateTime2.Date))
			{
				RequestSourceRepair(dateTime2, dateTime2.Date);
				break;
			}
			if (State == State.Realtime && lastEnd != DateTime.MinValue && dateTime2 > lastEnd && dateTime2.Date == model.Day && lastEnd.TimeOfDay.TotalMinutes < (double)Model.Cutoff(dateTime2.Date) && dateTime2.TimeOfDay != TimeSpan.Zero)
			{
				RequestSourceRepair((dateTime2 < dateTime2.Date.AddMinutes(Model.Cutoff(dateTime2.Date))) ? dateTime2 : dateTime2.Date.AddMinutes(Model.Cutoff(dateTime2.Date)));
				break;
			}
			Minute bar = new Minute
			{
				Start = dateTime2,
				Open = BarsArray[signalSeries].GetOpen(i),
				High = BarsArray[signalSeries].GetHigh(i),
				Low = BarsArray[signalSeries].GetLow(i),
				Close = BarsArray[signalSeries].GetClose(i),
				Volume = BarsArray[signalSeries].GetVolume(i)
			};
			DateTime dateTime3 = Central(NativeNow());
			bool fresh = State != State.Realtime || (i == num && dateTime > activation && dateTime > recoveryFloor && dateTime >= new DateTime(dateTime3.Year, dateTime3.Month, dateTime3.Day, dateTime3.Hour, dateTime3.Minute, 0));
			ProcessSource(bar, fresh);
			consumedSourceIndex = i;
			if (historyRepair != null)
			{
				break;
			}
		}
	}

	private void ProcessSource(Minute bar, bool fresh)
	{
		DateTime dateTime = bar.Start.AddMinutes(1.0);
		lastEnd = (latest = dateTime);
		HandleDeadline(dateTime);
		bool flag = model.MinuteClosed(bar, fresh && EnableTrading);
		RefreshVwapPlot();
		Log("MINUTE", "sourceEnd=" + dateTime.ToString("s") + ";vwap=" + F(model.Vwap) + ";nub=" + F(model.NoiseUp));
		if (flag && !suspended && !closing && ownedNet == 0 && !Working(entryOrder))
		{
			SubmitEntry();
		}
		CheckProtection();
		DriveClose();
		ReleaseExposureIfSettled();
		SaveState();
		Paint(dateTime);
	}

	private void RequestSourceRepair(DateTime missingRight, DateTime historicalLeft = default(DateTime))
	{
		DateTime dateTime = Central(NativeNow());
		DateTime missingLeft = ((historicalLeft == DateTime.MinValue) ? lastEnd : historicalLeft);
		historyWaiting = true;
		status = "Reloading provider M1 " + missingLeft.ToString("HH:mm") + " to " + missingRight.ToString("HH:mm") + " Central; new entries paused";
		Paint(dateTime);
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
		Print("VaultBreak M1 reload: " + missingLeft.ToString("s") + " to " + missingRight.ToString("s") + " Central");
		try
		{
			request.Request((BarsRequest reply, ErrorCode error, string message) =>
			{
				if (State == State.Realtime && historyRepair == request)
				{
					List<Minute> recovered = new List<Minute>();
					if (error == ErrorCode.NoError)
					{
						for (int i = 0; i < reply.Bars.Count; i++)
						{
							DateTime dateTime2 = Central(reply.Bars.GetTime(i)).AddMinutes(-1.0);
							if (dateTime2 >= missingLeft && dateTime2 < missingRight)
							{
								recovered.Add(new Minute
								{
									Start = dateTime2,
									Open = reply.Bars.GetOpen(i),
									High = reply.Bars.GetHigh(i),
									Low = reply.Bars.GetLow(i),
									Close = reply.Bars.GetClose(i),
									Volume = reply.Bars.GetVolume(i)
								});
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
							foreach (Minute item in recovered)
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
								Print("VaultBreak " + status + " " + message);
							}
							else
							{
								recoveryFloor = Central(NativeNow());
								historyWaiting = false;
								foreach (Minute item2 in recovered)
								{
									providerBars[item2.Start] = item2;
								}
								if (historicalLeft != DateTime.MinValue)
								{
									RebuildSource(recovered);
								}
								else
								{
									foreach (Minute item3 in recovered)
									{
										ProcessSource(item3, fresh: false);
									}
								}
								status = "M1 history recovered; waiting for a fresh M30 close";
								Print("VaultBreak M1 recovered " + recovered.Count + " bars; old signals suppressed");
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
			Print("VaultBreak " + status + ": " + ex.Message);
		}
	}

	private void RebuildSource(List<Minute> recovered)
	{
		int attempts = ((model.Day == recoveryFloor.Date) ? model.Attempts : 0);
		bool flag = model.VirtualActive;
		bool flag2 = model.VirtualPending;
		DateTime virtualStart = model.VirtualStart;
		double num = model.VirtualStop;
		double num2 = model.VirtualTarget;
		double num3 = model.VirtualFill;
		double virtualExit = model.VirtualExit;
		string text = model.VirtualDisplay;
		DateTime virtualExitBar = model.VirtualExitBar;
		SortedDictionary<DateTime, Minute> sortedDictionary = new SortedDictionary<DateTime, Minute>();
		int num4 = BarsArray[signalSeries].Count - 2;
		for (int i = 0; i <= num4; i++)
		{
			DateTime dateTime = Central(BarsArray[signalSeries].GetTime(i)).AddMinutes(-1.0);
			sortedDictionary[dateTime] = new Minute
			{
				Start = dateTime,
				Open = BarsArray[signalSeries].GetOpen(i),
				High = BarsArray[signalSeries].GetHigh(i),
				Low = BarsArray[signalSeries].GetLow(i),
				Close = BarsArray[signalSeries].GetClose(i),
				Volume = BarsArray[signalSeries].GetVolume(i)
			};
		}
		foreach (Minute value in providerBars.Values)
		{
			sortedDictionary[value.Start] = value;
		}
		model = new Model();
		foreach (Minute value2 in sortedDictionary.Values)
		{
			model.MinuteClosed(value2, enabled: false);
		}
		model.BeginLive(recoveryFloor);
		model.Attempts = attempts;
		if (flag | flag2)
		{
			foreach (Minute item in sortedDictionary.Values.Where((Minute b) => b.Start >= virtualStart))
			{
				if (flag2)
				{
					if (item.Start != virtualStart)
					{
						model.Ready = false;
						model.BlockReason = "Synthetic opening bar unavailable; new entries paused";
						break;
					}
					num = item.Open - 60.0;
					num2 = item.Open + 40.0;
					flag2 = false;
					flag = true;
				}
				if (flag)
				{
					int num5 = Model.VirtualOutcome(item.Low, item.High, num, num2);
					if (num5 != 0)
					{
						flag = false;
						virtualExit = ((num5 < 0) ? num : num2);
						text = ((num5 < 0) ? "SL" : "TP");
						virtualExitBar = item.Start.AddMinutes(1.0);
					}
				}
				if (num3 <= 0.0 && !flag2 && num > 0.0)
				{
					num3 = num + 60.0;
				}
			}
			model.RestoreVirtual(flag, flag2, virtualStart, num, num2);
			model.VirtualFill = num3;
			model.VirtualExit = virtualExit;
			model.VirtualDisplay = (flag ? "open" : text);
			model.VirtualExitBar = virtualExitBar;
		}
		if (sortedDictionary.Count > 0)
		{
			lastEnd = (latest = sortedDictionary.Keys.Last().AddMinutes(1.0));
		}
		consumedSourceIndex = num4;
		RefreshVwapPlot();
		Paint(recoveryFloor);
	}

	private DateTime Central(DateTime t)
	{
		return TimeZoneInfo.ConvertTime(DateTime.SpecifyKind(t, DateTimeKind.Unspecified), platformZone, Model.CentralZone);
	}

	private static DateTime NativeNow()
	{
		return (Connection.PlaybackConnection != null) ? Connection.PlaybackConnection.Now : Globals.Now;
	}

	private DateTime Platform(DateTime t)
	{
		return TimeZoneInfo.ConvertTime(DateTime.SpecifyKind(t, DateTimeKind.Unspecified), Model.CentralZone, platformZone);
	}

	private double Round(double p)
	{
		return ExecutionInstrument.MasterInstrument.RoundToTickSize(p);
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

	private static string SourceContract(Instrument instrument)
	{
		string text = instrument.MasterInstrument.Name.Trim().ToUpperInvariant();
		if (text != "NQ" && text != "MNQ" && text != "NNQ")
		{
			throw new InvalidOperationException("VaultBreak supports NQ/MNQ/NNQ; selected " + instrument.FullName + ".");
		}
		return "NQ" + instrument.FullName.Substring(instrument.MasterInstrument.Name.Length);
	}

	private bool CanReplayCompletedSession(DateTime day)
	{
		if (State != State.Historical || IsInStrategyAnalyzer)
		{
			return true;
		}
		Bars bars = BarsArray[executionSeries];
		return day != DateTime.MinValue && bars.Count > 0 && Central(bars.GetTime(bars.Count - 1)) >= day.Date.AddMinutes(Model.Cutoff(day) + 2);
	}

	private void SubmitEntry()
	{
		if ((State == State.Realtime && (recoveryPending || recoveryFault)) || !CanReplayCompletedSession(model.Day) || !EnableTrading || (State == State.Realtime && (!acquired || Account == null)))
		{
			return;
		}
		if (CurrentBars[executionSeries] < 0)
		{
			status = "Waiting for execution-instrument M1 history";
			return;
		}
		double num = ((State == State.Realtime) ? GetCurrentAsk(executionSeries) : Closes[executionSeries][0]);
		if (num <= 0.0 || double.IsNaN(num) || double.IsInfinity(num))
		{
			status = "Waiting for executable quote";
			return;
		}
		requestStop = Round(num - 60.0);
		requestTarget = Round(num + 40.0);
		double num2 = (num - requestStop) * ExecutionInstrument.MasterInstrument.PointValue + Friction;
		int num3 = (UseFixedSize ? FixedContracts : Model.Size(RiskMoney, num2));
		if (num3 == 0)
		{
			status = "Risk below one contract; signal skipped";
			Log("RISK_TOO_SMALL", "minimum=" + F(num2));
		}
		else if (AcquireExposure())
		{
			model.Attempts++;
			SaveState();
			if (!suspended)
			{
				string text = owner;
				int num4 = ++sequence;
				entryName = text + "_ENTRY_" + num4;
				Log("ENTRY_REQUEST", "attempt=" + model.Attempts + ";quantity=" + num3 + ";sl=" + F(requestStop) + ";tp=" + F(requestTarget) + ";close=" + F(model.SignalClose) + ";vwap=" + F(model.Vwap));
				entryOrder = SubmitOwnedOrder(executionSeries, OrderAction.Buy, OrderType.Market, num3, 0.0, 0.0, string.Empty, entryName);
				status = "Entry submitted; waiting for fill";
			}
		}
	}

	private static bool Working(Order o)
	{
		return o != null && o.OrderState != OrderState.Cancelled && o.OrderState != OrderState.Rejected && o.OrderState != OrderState.Filled;
	}

	private bool Settled(Order o)
	{
		if (o == null)
		{
			return true;
		}
		processed.TryGetValue(o.Name, out var value);
		return !Working(o) && value == o.Filled;
	}

	private void CancelSafe(Order o)
	{
		if (Working(o) && o.OrderState != OrderState.CancelPending && o.OrderState != OrderState.CancelSubmitted)
		{
			CancelOwnedOrder(o);
		}
	}

	private bool AcquireExposure()
	{
		if (State != State.Realtime)
			return true;
		// Durable executor/instance leases remain separate from this direction guard.
		// Same-direction strategies may share a contract; opposite netting is always blocked.
		if (!ProductExposure.Reserve(owner, 1, Account, ExecutionInstrument, BlockOpposingExposure, out var reason))
		{
			status = reason;
			return false;
		}
		exposureLease = true;
		return true;
	}

	private void ReleaseExposureIfSettled(string releasingOwner = null)
	{
		if (!exposureLease || ownedNet != 0 || !Settled(entryOrder) || recoveryRequests.Keys.Any(name => !orders.ContainsKey(name)) || orders.Values.Any((Order o) => !Settled(o) || !ExecutionsCaughtUp(o)))
		{
			return;
		}
		lock (AppDomain.CurrentDomain)
		{
			Dictionary<string, string> dictionary = AppDomain.CurrentDomain.GetData("NQIndependentModelExposure.v1") as Dictionary<string, string>;
			string text = releasingOwner ?? owner;
			if (dictionary != null && dictionary.TryGetValue(ExposureKey, out var value) && value == text)
			{
				dictionary.Remove(ExposureKey);
			}
			ProductExposure.Release(text);
			exposureLease = false;
		}
	}

	private void HandleDeadline(DateTime now)
	{
		if (model != null && (now.TimeOfDay.TotalMinutes >= (double)Model.Cutoff(now.Date) || (legs.Count > 0 && legs[0].Entered.Date < now.Date)))
		{
			model.ExpireVirtual();
			SaveState();
			if (ownedNet != 0 || Working(entryOrder))
			{
				Flatten("Time exit");
			}
		}
	}

	private void Flatten(string why)
	{
		closing = true;
		status = why + ": closing owned exposure";
		SaveRecovery();
		DriveClose();
		ReleaseExposureIfSettled();
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

	private void DriveClose()
	{
		if (!BrokerReady() || !closing || driving || closeRejected)
		{
			return;
		}
		driving = true;
		try
		{
			if (ownedNet != 0 && orders.Values.All(ExecutionsCaughtUp) && !RecoveryPositionConsistent())
			{
				WaitForRecoveryEvidence();
				return;
			}
			CancelSafe(entryOrder);
			Order[] array = orders.Values.ToArray();
			foreach (Order order in array)
			{
				if (order.OrderType != OrderType.Market)
				{
					CancelSafe(order);
				}
			}
			if (!Settled(entryOrder) || orders.Values.Any((Order o) => !Settled(o) || !ExecutionsCaughtUp(o)))
			{
				return;
			}
			if (ownedNet == 0)
			{
				closing = false;
				ClearLegs();
				status = "Owned exposure closed; flat";
			}
			else if (!Working(closeOrder) && (pendingCloseName == null || (pendingCloseFinal >= 0 && pendingCloseExecuted >= pendingCloseFinal)))
			{
				if (!RecoveryPositionConsistent())
				{
					WaitForRecoveryEvidence();
					return;
				}
				string text = owner;
				int num = ++sequence;
				pendingCloseName = text + "_CLOSE_" + num;
				pendingCloseQuantity = Math.Abs(ownedNet);
				pendingCloseExecuted = 0;
				pendingCloseFinal = -1;
				closeOrder = SubmitOwnedOrder(executionSeries, (ownedNet <= 0) ? OrderAction.BuyToCover : OrderAction.Sell, OrderType.Market, Math.Abs(ownedNet), 0.0, 0.0, string.Empty, pendingCloseName);
			}
		}
		finally
		{
			driving = false;
		}
	}

	private void CheckProtection()
	{
		if (!BrokerReady() || ownedNet == 0 || closing || legs.Count == 0)
		{
			return;
		}
		foreach (Leg leg in legs)
		{
			if (leg.Exited >= leg.Quantity || (orders.TryGetValue(leg.StopName, out var value) && orders.TryGetValue(leg.TargetName, out var value2) && (Working(value) || !Settled(value)) && (Working(value2) || !Settled(value2))))
			{
				continue;
			}
			suspended = true;
			SaveState();
			Flatten("Protection unavailable");
			break;
		}
	}

	protected override void OnOrderUpdate(Order order, double limitPrice, double stopPrice, int quantity, int filled, double averageFillPrice, OrderState orderState, DateTime time, ErrorCode error, string nativeError)
	{
		if (owner == null || order == null || !order.Name.StartsWith(owner + "_", StringComparison.Ordinal))
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
		if (order.Name == entryName)
		{
			entryOrder = order;
		}
		if (order.Name.Contains("_CLOSE_"))
		{
			closeOrder = order;
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
			brokerRestrictionReason = nativeError;
			SaveState();
			SaveRecovery();
			ReleaseExposureIfSettled();
			RecoveryBlock("Broker account restricted; new orders paused; verify broker position/protection: " + nativeError);
			return;
		}
		if ((error != ErrorCode.NoError || orderState == OrderState.Rejected) && order.Name.Contains("_Repair"))
		{
			RecoveryBlock("Broker rejected recovered protection: " + order.Name + " id=" + order.OrderId + " state=" + orderState.ToString() + " error=" + error.ToString() + " OCO=" + order.Oco + " detail=" + nativeError);
			return;
		}
		if (error != ErrorCode.NoError || orderState == OrderState.Rejected)
		{
			if (order.Name == pendingCloseName)
			{
				closeRejected = true;
			}
			suspended = true;
			SaveState();
			Print("VaultBreak owned order error: " + error.ToString() + " " + nativeError);
			Flatten("Owned order rejected");
		}
		SaveRecovery();
		DriveClose();
		ReleaseExposureIfSettled();
		if (closeRejected && order.Name == pendingCloseName)
		{
			RepairOwnedProtection();
		}
	}

	protected override void OnExecutionUpdate(Execution execution, string executionId, double price, int quantity, MarketPosition marketPosition, string orderId, DateTime time)
	{
		if (execution == null || execution.Order == null || owner == null || !execution.Order.Name.StartsWith(owner + "_", StringComparison.Ordinal) || ReconcileOwnedExecution(execution, executionId))
		{
			return;
		}
		ObserveFee(execution, executionId, quantity, execution.Order.Name == entryName);
		if (!executions.Add(executionId))
		{
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
		ownedNet += ((order.OrderAction == OrderAction.Buy || order.OrderAction == OrderAction.BuyToCover) ? quantity : (-quantity));
		DateTime dateTime = Central(time);
		if (order.Name == entryName)
		{
			bool flag = legs.Count == 0;
			double num = ((State == State.Historical) ? Round(price - 60.0) : requestStop);
			double num2 = ((State == State.Historical) ? Round(price + 40.0) : requestTarget);
			string text = owner + "_LEG_" + ++sequence;
			Leg leg = new Leg
			{
				Entry = price,
				Stop = num,
				Target = num2,
				Quantity = quantity,
				Entered = dateTime,
				StopName = text + "_STOP",
				TargetName = text + "_TARGET",
				VisualTag = text
			};
			legs.Add(leg);
			DrawFundedTrade(leg, dateTime);
			if (flag)
			{
				fundedTrades++;
			}
			modelCosts += Friction * (double)quantity;
			if (!(num < price) || !(num2 > price) || (!UseFixedSize && !(legs.Sum((Leg l) => ((l.Entry - l.Stop) * ExecutionInstrument.MasterInstrument.PointValue + Friction) * (double)l.Quantity) <= RiskMoney + 0.01)) || closing || suspended)
			{
				suspended = true;
				SaveState();
				Flatten("Actual fill risk breach");
			}
			else
			{
				SubmitOwnedOrder(executionSeries, OrderAction.Sell, OrderType.StopMarket, quantity, 0.0, num, text + "_OCO", leg.StopName);
				if (!closing)
				{
					SubmitOwnedOrder(executionSeries, OrderAction.Sell, OrderType.Limit, quantity, num2, 0.0, text + "_OCO", leg.TargetName);
				}
				status = "Owned position protected";
			}
			Log("ENTRY_FILL", "time=" + dateTime.ToString("s") + ";price=" + F(price) + ";quantity=" + quantity + ";tradeId=" + entryName + ";executionId=" + executionId + ";direction=Long;initialStop=" + F(num) + ";initialTarget=" + F(num2));
		}
		else
		{
			Leg leg2 = legs.FirstOrDefault((Leg l) => l.StopName == order.Name || l.TargetName == order.Name);
			if (leg2 != null)
			{
				leg2.Exited += quantity;
				leg2.ExitValue += price * (double)quantity;
				leg2.ExitTime = dateTime;
				DrawFundedTrade(leg2, dateTime);
				grossNet += (price - leg2.Entry) * (double)quantity * ExecutionInstrument.MasterInstrument.PointValue;
			}
			else if (order.Name.Contains("_CLOSE_"))
			{
				int num3 = quantity;
				foreach (Leg leg3 in legs)
				{
					int num4 = Math.Min(num3, leg3.Quantity - leg3.Exited);
					if (num4 > 0)
					{
						leg3.Exited += num4;
						leg3.ExitValue += price * (double)num4;
						leg3.ExitTime = dateTime;
						DrawFundedTrade(leg3, dateTime);
						num3 -= num4;
						grossNet += (price - leg3.Entry) * (double)num4 * ExecutionInstrument.MasterInstrument.PointValue;
					}
				}
			}
			Log("EXIT_FILL", "order=" + order.Name + ";price=" + F(price) + ";quantity=" + quantity + ";ownedNet=" + ownedNet + ";time=" + dateTime.ToString("s") + ";tradeId=" + entryName + ";executionId=" + executionId + ";direction=Long");
			if (ownedNet < 0)
			{
				suspended = true;
				SaveState();
				Flatten("OCO race: close owned overfill");
			}
			else if (leg2 != null && leg2.Exited < leg2.Quantity)
			{
				suspended = true;
				SaveState();
				Flatten("Partial exit: reconcile protection");
			}
			if (ownedNet == 0)
			{
				Order[] array = orders.Values.ToArray();
				foreach (Order order2 in array)
				{
					if (order2.OrderType != OrderType.Market)
					{
						CancelSafe(order2);
					}
				}
			}
		}
		if (ownedNet == 0 && feeTrade != null)
		{
			feeTrade.Closed = feeTrade.EntryQuantity > 0 && feeTrade.EntryQuantity == feeTrade.ExitQuantity;
		}
		if (ownedNet == 0 && !closing && Settled(entryOrder))
		{
			ClearLegs();
		}
		SaveRecovery();
		DriveClose();
		ReleaseExposureIfSettled();
		SaveState();
	}

	private void SaveState()
	{
		SaveRecovery();
		if (State != State.Realtime || !acquired || statePath == null || model == null)
		{
			return;
		}
		try
		{
			string text = statePath + ".tmp";
			File.WriteAllText(text, model.Day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + "|" + model.Attempts + "|" + (suspended ? "1" : "0") + "|60_40_funded3_v2");
			if (File.Exists(statePath))
			{
				File.Replace(text, statePath, null);
			}
			else
			{
				File.Move(text, statePath);
			}
		}
		catch (Exception ex)
		{
			suspended = true;
			Print("VaultBreak state write failed; new entries blocked: " + ex.Message);
		}
	}

	private void RestoreState()
	{
		if (!File.Exists(statePath))
		{
			return;
		}
		try
		{
			string[] array = File.ReadAllText(statePath).Split('|');
			bool flag = array.Length == 3;
			if (!flag && (array.Length != 4 || array[3] != "60_40_funded3_v2"))
			{
				throw new InvalidDataException("Invalid journal revision");
			}
			DateTime dateTime = DateTime.ParseExact(array[0], "yyyy-MM-dd", CultureInfo.InvariantCulture);
			if (!(dateTime != activation.Date))
			{
				int saved = int.Parse(array[1], CultureInfo.InvariantCulture);
				if (array[2] != "0" && array[2] != "1")
				{
					throw new InvalidDataException("Invalid journal");
				}
				model.Attempts = Model.MigrateAttempts(saved, flag);
				if (array[2] == "1")
				{
					suspended = true;
					status = "Saved blocked session retained";
				}
				if (flag)
				{
					Log("STATE_MIGRATED", "model=60_40_funded3_v2;oldAttempts=" + saved + ";fundedAttempts=" + model.Attempts);
				}
			}
		}
		catch (Exception ex)
		{
			suspended = true;
			Print("VaultBreak recovery failed; new entries blocked: " + ex.Message);
		}
	}

	private void Log(string evt, string detail)
	{
		if (audit != null)
		{
			audit.WriteLine(latest.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) + "," + evt + ",\"" + detail.Replace("\"", "\"\"") + "\"");
		}
	}

	private static string F(double n)
	{
		return n.ToString("0.########", CultureInfo.InvariantCulture);
	}

	private void Line(string tag, DateTime from, DateTime to, double price, System.Windows.Media.Brush ink, DashStyleHelper dash)
	{
		if (!VisualsSuppressed && !(to <= from) && !(price <= 0.0) && (!visualLevels.TryGetValue(tag, out var value) || !(value.From == from) || !(value.To == to) || value.Price != price || value.Ink != ink || value.Dash != dash))
		{
			Draw.Line(this, owner + "_" + tag, isAutoScale: false, Platform(from), price, Platform(to), price, ink, dash, 1);
			visualLevels[tag] = new LevelSnapshot
			{
				From = from,
				To = to,
				Price = price,
				Ink = ink,
				Dash = dash
			};
		}
	}

	private void RemoveLevel(string tag)
	{
		if (visualLevels != null && visualLevels.Remove(tag))
		{
			RemoveDrawObject(owner + "_" + tag);
		}
	}

	private void ClearLegs()
	{
		foreach (Leg leg in legs)
		{
			RemoveLevel(leg.StopName);
			RemoveLevel(leg.TargetName);
		}
		legs.Clear();
	}

	private void RefreshVwapPlot()
	{
		if (CurrentBars[0] >= 0 && model != null && (State != State.Historical || !(Central(Times[0][0]) < lastEnd)))
		{
			if (model.Day != plotSession || model.PreviousVwapTime == DateTime.MinValue)
			{
				plotSession = model.Day;
				plotBreakBar = CurrentBars[0];
			}
			double num = ((ShowVisuals && model.HasSessionOpen) ? model.Vwap : double.NaN);
			System.Windows.Media.Brush brush = ((CurrentBars[0] == plotBreakBar) ? Brushes.Transparent : Brushes.DeepSkyBlue);
			if (writtenPlotBar != CurrentBars[0] || !writtenPlotValue.Equals(num) || writtenPlotInk != brush)
			{
				Values[0][0] = num;
				PlotBrushes[0][0] = brush;
				writtenPlotBar = CurrentBars[0];
				writtenPlotValue = num;
				writtenPlotInk = brush;
			}
		}
	}

	private void PaintHistoricalExecutions()
	{
		if (ShowVisuals)
		{
			EnableNativeExecutionDisplay();
		}
		if (!ShowVisuals || historicalVisualsPainted || State == State.Historical || ChartControl == null)
		{
			return;
		}
		HashSet<string> hashSet = new HashSet<string>(from x in DrawObjects
			where x != null
			select x.Tag);
		historicalVisualsPainted = true;
		foreach (Leg closedVisual in closedVisuals)
		{
			if (!hashSet.Contains(closedVisual.VisualTag + "_SL"))
			{
				DrawFundedTrade(closedVisual, closedVisual.ExitTime);
				historicalVisualsPainted = false;
			}
		}
	}

	private void Paint(DateTime ct)
	{
		SyncFaultVisuals();
		PaintHistoricalExecutions();
		if (!ShowVisuals || model == null)
		{
			return;
		}
		DateTime dateTime = model.Day.AddMinutes(Model.Cutoff(model.Day));
		if (model.HasSessionOpen)
		{
			Line("Session Opening Price (SOP)", model.Day, dateTime, model.Sop, Brushes.Silver, DashStyleHelper.Dot);
			if (model.AverageAtr > 0.0)
			{
				Line("Noise Up Barrier (NUB)", model.Day, dateTime, model.NoiseUp, Brushes.Orange, DashStyleHelper.Dot);
			}
		}
		foreach (Leg leg in legs)
		{
			if (leg.Exited < leg.Quantity)
			{
				DrawFundedTrade(leg, ct);
			}
		}
		if (model.VirtualActive || (model.VirtualExit > 0.0 && model.VirtualExitBar != DateTime.MinValue))
		{
			DateTime to = (model.VirtualActive ? dateTime : model.VirtualExitBar);
			Line("Synthetic entry", model.VirtualStart, to, model.VirtualStop + 60.0, Brushes.SlateGray, DashStyleHelper.Dash);
			Line("Synthetic stop", model.VirtualStart, to, model.VirtualStop, Brushes.Tomato, DashStyleHelper.Dash);
			Line("Synthetic target", model.VirtualStart, to, model.VirtualTarget, Brushes.LimeGreen, DashStyleHelper.Dash);
		}
		else
		{
			RemoveLevel("Synthetic entry");
			RemoveLevel("Synthetic stop");
			RemoveLevel("Synthetic target");
		}
		foreach (Leg leg2 in legs)
		{
			Line(leg2.StopName, leg2.Entered, dateTime, leg2.Stop, Brushes.Tomato, DashStyleHelper.Solid);
			Line(leg2.TargetName, leg2.Entered, dateTime, leg2.Target, Brushes.LimeGreen, DashStyleHelper.Solid);
		}
		if (State != State.Historical || CurrentBars[signalSeries] + 2 >= BarsArray[signalSeries].Count || ct.Minute % 30 == 0)
		{
			string text;
			if (suspended)
			{
				text = status;
			}
			else if (EnableTrading)
			{
				if (historyWaiting)
				{
					text = status;
				}
				else if (model.Ready)
				{
					if (model.VirtualActive || model.VirtualPending)
					{
						text = "Synthetic first attempt: waiting for SL / TP";
					}
					else
					{
						text = ((ownedNet > 0) ? "Owned position protected" : status);
					}
				}
				else
				{
					text = model.BlockReason;
				}
			}
			else
			{
				text = "New trades disabled; owned protection active";
			}
			dashboard.Publish(new ModelDashState
			{
				Title = "Vault Break",
				Clock = "NY: " + ct.AddHours(1.0).ToString("HH:mm:ss") + " CT: " + ct.ToString("HH:mm:ss"),
				Status = (VisualFaultReason() ?? text),
				NewsBlocked = (connectionPaused || suspended),
				RangeCaption = "NOISE RANGE | ATR15",
				HighCaption = "NUB",
				LowCaption = "SOP",
				RangeHigh = (model.Ready ? model.NoiseUp.ToString("0.000", CultureInfo.InvariantCulture) : "--"),
				RangeLow = (model.Ready ? F(model.Sop) : "--"),
				RangeWidth = (model.Ready ? (model.NoiseUp - model.Sop).ToString("0.00", CultureInfo.InvariantCulture) : "--"),
				InTrade = (ownedNet != 0 && !recoveryPending && !recoveryFault),
				Position = ((recoveryPending || recoveryFault) ? "UNVERIFIED" : ((ownedNet == 0) ? "FLAT" : (Math.Abs(ownedNet) + " LONG"))),
				Stop = ((!recoveryPending && !recoveryFault && legs.Count > 0) ? F(legs[0].Stop) : "--"),
				Risk = "Trades " + model.Attempts + "/3 | " + (UseFixedSize ? ("Fixed " + FixedContracts + " contracts") : ("Risk $" + F(RiskMoney))),
				Costs = "Cost/contract: $" + F(Friction) + " | Slip " + SlippagePoints,
				FirstSetup = "Funded 60/40 | first entry enabled",
				History = "VWAP " + F(model.Vwap) + ((signalSeries != executionSeries) ? (" | NQ -> " + ExecutionInstrument.MasterInstrument.Name) : " | Fixed exits")
			});
			if (State == State.Realtime)
			{
				RequestDashboardRender();
			}
		}
	}
}
}
