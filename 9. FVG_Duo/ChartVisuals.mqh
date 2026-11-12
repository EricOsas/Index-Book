//+------------------------------------------------------------------+
//|                                             ChartVisuals.mqh     |
//| Chart dashboard and FVG visual markers for FVG Duo               |
//| Developed by Osamwonyi Eric (@You_FoundEric)                     |
//+------------------------------------------------------------------+
#ifndef FVG_CHART_VISUALS
#define FVG_CHART_VISUALS

#define DASH_BG      "FVG_DASH_BG"
#define DASH_TITLE   "FVG_DASH_TITLE"
#define DASH_INFO1   "FVG_DASH_INFO1"
#define DASH_INFO2   "FVG_DASH_INFO2"
#define DASH_INFO3   "FVG_DASH_INFO3"
#define DASH_INFO4   "FVG_DASH_INFO4"
#define DASH_INFO5   "FVG_DASH_INFO5"

void CreateLabel(string name, int x, int y, string text, color clr, int fontSize = 9, string font = "Consolas")
{
   if(ObjectFind(0, name) < 0)
   {
      ObjectCreate(0, name, OBJ_LABEL, 0, 0, 0);
      ObjectSetInteger(0, name, OBJPROP_CORNER, CORNER_LEFT_UPPER);
      ObjectSetInteger(0, name, OBJPROP_XDISTANCE, x);
      ObjectSetInteger(0, name, OBJPROP_YDISTANCE, y);
      ObjectSetInteger(0, name, OBJPROP_SELECTABLE, false);
   }
   ObjectSetString(0, name, OBJPROP_TEXT, text);
   ObjectSetInteger(0, name, OBJPROP_COLOR, clr);
   ObjectSetInteger(0, name, OBJPROP_FONTSIZE, fontSize);
   ObjectSetString(0, name, OBJPROP_FONT, font);
}

void UpdateDashboard(string status, string nyClockStr, string posStr, double todayNet)
{
   if(!ShowDashboard || (MQLInfoInteger(MQL_TESTER) && !MQLInfoInteger(MQL_VISUAL_MODE)))
      return;

   // Dark slate panel background
   if(ObjectFind(0, DASH_BG) < 0)
   {
      ObjectCreate(0, DASH_BG, OBJ_RECTANGLE_LABEL, 0, 0, 0);
      ObjectSetInteger(0, DASH_BG, OBJPROP_CORNER, CORNER_LEFT_UPPER);
      ObjectSetInteger(0, DASH_BG, OBJPROP_XDISTANCE, 15);
      ObjectSetInteger(0, DASH_BG, OBJPROP_YDISTANCE, 25);
      ObjectSetInteger(0, DASH_BG, OBJPROP_XSIZE, 340);
      ObjectSetInteger(0, DASH_BG, OBJPROP_YSIZE, 135);
      ObjectSetInteger(0, DASH_BG, OBJPROP_BGCOLOR, C'11,15,25'); // Deep obsidian
      ObjectSetInteger(0, DASH_BG, OBJPROP_BORDER_TYPE, BORDER_FLAT);
      ObjectSetInteger(0, DASH_BG, OBJPROP_COLOR, C'56,189,248'); // Electric sky blue border
      ObjectSetInteger(0, DASH_BG, OBJPROP_SELECTABLE, false);
   }

   CreateLabel(DASH_TITLE, 25, 32, "=== FVG DUO STRATEGY (INDEX BOOK) ===", C'56,189,248', 10, "Trebuchet MS");
   CreateLabel(DASH_INFO1, 25, 52, "NY Wall Clock : " + nyClockStr, C'248,250,252');
   CreateLabel(DASH_INFO2, 25, 70, "Model Status  : " + status, (status == "OD TRIGGERED" || status == "TREND ACTIVE") ? C'74,222,128' : C'148,163,184');
   CreateLabel(DASH_INFO3, 25, 88, "Position      : " + posStr, (posStr != "FLAT") ? C'56,189,248' : C'203,213,225');
   
   color pnlClr = (todayNet >= 0) ? C'74,222,128' : C'239,68,68';
   CreateLabel(DASH_INFO4, 25, 106, StringFormat("Session Net   : %+.2f USD", todayNet), pnlClr);
   CreateLabel(DASH_INFO5, 25, 124, StringFormat("Config: Preset=%s | Mult=%.2f | EMA=%d/%d", 
               EnumToString(InpModelPreset), InpTrendAtrMult, InpTrendEmaFast, InpTrendEmaSlow), C'148,163,184', 8);

   ChartRedraw(0);
}

void DrawFvgBox(string name, datetime t1, double p1, datetime t2, double p2, color boxClr)
{
   if(!ShowVisuals || (MQLInfoInteger(MQL_TESTER) && !MQLInfoInteger(MQL_VISUAL_MODE)))
      return;

   if(ObjectFind(0, name) < 0)
   {
      ObjectCreate(0, name, OBJ_RECTANGLE, 0, t1, p1, t2, p2);
      ObjectSetInteger(0, name, OBJPROP_COLOR, boxClr);
      ObjectSetInteger(0, name, OBJPROP_STYLE, STYLE_SOLID);
      ObjectSetInteger(0, name, OBJPROP_WIDTH, 1);
      ObjectSetInteger(0, name, OBJPROP_FILL, true);
      ObjectSetInteger(0, name, OBJPROP_BACK, true);
      ObjectSetInteger(0, name, OBJPROP_SELECTABLE, false);
   }
}

void RemoveVisuals()
{
   ObjectDelete(0, DASH_BG);
   ObjectDelete(0, DASH_TITLE);
   ObjectDelete(0, DASH_INFO1);
   ObjectDelete(0, DASH_INFO2);
   ObjectDelete(0, DASH_INFO3);
   ObjectDelete(0, DASH_INFO4);
   ObjectDelete(0, DASH_INFO5);

   // Clean up any FVG objects
   for(int i = ObjectsTotal(0, -1, -1) - 1; i >= 0; i--)
   {
      string name = ObjectName(0, i, -1, -1);
      if(StringFind(name, "FVG_") == 0)
         ObjectDelete(0, name);
   }
   ChartRedraw(0);
}

#endif // FVG_CHART_VISUALS
