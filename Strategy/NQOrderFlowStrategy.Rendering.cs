using ATAS.DataFeedsCore;
using ATAS.Indicators;
using ATAS.Strategies;
using ATAS.Strategies.Chart;
using NQOrderFlowV1.Engines;
using NQOrderFlowV1.Models;
using NQOrderFlowV1.Services;
using NQOrderFlowV1.Zones;
using OFT.Rendering.Context;
using OFT.Rendering.Tools;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace NQOrderFlowV1.Strategy
{
    public partial class NQOrderFlowStrategy
    {
        // =========================
        // RENDER
        // =========================
        protected override void OnRender(RenderContext context, DrawingLayouts layout)
        {
            if (ChartInfo is null)
                return;

            List<TradingZone> zones;
            string hud;
            TradePlan? plan;

            lock (_renderLock)
            {
                zones = _activeZonesForRender.ToList();
                hud = _hudText;
                plan = _planForRender;
            }

            DrawZones(context, zones);
            DrawPlanLines(context, plan);

            if (ShowHud)
                DrawHudTopCenter(context, hud);
        }

        private void DrawZones(RenderContext context, List<TradingZone> zones)
        {
            if (ChartInfo is null || zones.Count == 0)
                return;

            var rightBar = LastVisibleBarNumber;

            foreach (var zone in zones)
            {
                if (zone.IsInvalidated)
                    continue;

                var x1 = ChartInfo.GetXByBar(zone.StartBar, true);
                var x2 = ChartInfo.GetXByBar(rightBar, false);
                if (x2 <= x1)
                    continue;

                var yHigh = ChartInfo.GetYByPrice(zone.High, false);
                var yLow = ChartInfo.GetYByPrice(zone.Low, false);

                var top = (int)Math.Min(yHigh, yLow);
                var bottom = (int)Math.Max(yHigh, yLow);

                var height = bottom - top;
                if (height <= 1)
                    continue;

                var rect = new Rectangle(
                    x: (int)x1,
                    y: top,
                    width: (int)(x2 - x1),
                    height: height);

                var (fillColor, borderColor) = GetZoneColors(zone);

                // visually distinguish M5 zones slightly (more transparent)
                if (IsM5ZoneText(zone.Text))
                {
                    fillColor = Color.FromArgb(Math.Max(6, fillColor.A / 2), fillColor);
                    borderColor = Color.FromArgb(Math.Max(18, borderColor.A / 2), borderColor);
                }

                if (zone.IsVpRejected)
                {
                    fillColor = Color.FromArgb(Math.Max(6, fillColor.A / 2), fillColor);
                    borderColor = Color.FromArgb(Math.Max(18, borderColor.A / 2), borderColor);
                }
                else
                {
                    if (zone.IsMitigated)
                    {
                        fillColor = Color.FromArgb(Math.Max(8, fillColor.A / 4), fillColor);
                        borderColor = Color.FromArgb(Math.Max(20, borderColor.A / 2), borderColor);
                    }
                    else if (zone.IsTouched)
                    {
                        fillColor = Color.FromArgb(Math.Max(12, fillColor.A / 2), fillColor);
                        borderColor = Color.FromArgb(Math.Max(30, borderColor.A / 2), borderColor);
                    }
                }

                context.FillRectangle(fillColor, rect);
                context.DrawRectangle(new RenderPen(borderColor, 1), rect);
            }
        }

        private void DrawPlanLines(RenderContext context, TradePlan? plan)
        {
            if (ChartInfo is null || plan is null)
                return;

            var x1 = ChartArea.X;
            var x2 = ChartArea.X + ChartArea.Width;

            void DrawHLine(decimal price, Color color, string label)
            {
                var y = (int)ChartInfo.GetYByPrice(price, false);
                var pen = new RenderPen(color, 2);

                context.DrawLine(pen, x1, y, x2, y);

                var font = new RenderFont("Consolas", 11);
                var size = context.MeasureString(label, font);
                var rect = new Rectangle(x1 + 6, y - (int)size.Height - 2, (int)size.Width + 8, (int)size.Height + 4);

                context.FillRectangle(Color.FromArgb(120, 0, 0, 0), rect);
                context.DrawString(label, font, color, rect.X + 4, rect.Y + 2);
            }

            DrawHLine(plan.Entry, Color.DeepSkyBlue, $"{plan.Side} ENTRY {plan.Entry:0.00}");
            DrawHLine(plan.Stop, Color.OrangeRed, $"SL {plan.Stop:0.00}");
            DrawHLine(plan.Target, Color.LimeGreen, $"TP {plan.Target:0.00} ({RiskRewardR:0.0}R)");
        }

        private void DrawHudTopCenter(RenderContext context, string hud)
        {
            if (string.IsNullOrWhiteSpace(hud))
                return;

            var font = new RenderFont("SimSun", 11);
            var size = context.MeasureString(hud, font);

            const int padX = 10;
            const int padY = 8;

            var boxW = (int)Math.Ceiling((double)size.Width) + padX * 2;
            var boxH = (int)Math.Ceiling((double)size.Height) + padY * 2;

            var x = ChartArea.X + (ChartArea.Width - boxW) / 2;
            var y = ChartArea.Y + 6;

            var rect = new Rectangle(x, y, boxW, boxH);

            context.FillRectangle(Color.FromArgb(110, 0, 0, 0), rect);
            context.DrawRectangle(new RenderPen(Color.FromArgb(160, 40, 40, 40), 1), rect);

            context.DrawString(hud, font, Color.DeepSkyBlue, x + padX, y + padY);
        }

        private static bool IsM5ZoneText(string? text)
            => !string.IsNullOrWhiteSpace(text) && text.StartsWith("M5", StringComparison.OrdinalIgnoreCase);

        private static (Color Fill, Color Border) GetZoneColors(TradingZone zone)
        {
            if (zone.IsVpRejected)
                return (Color.FromArgb(35, Color.Gray), Color.FromArgb(90, Color.Gray));

            return zone.Type switch
            {
                ZoneType.BullishOB => (Color.FromArgb(70, Color.LimeGreen), Color.FromArgb(160, Color.LimeGreen)),
                ZoneType.BearishOB => (Color.FromArgb(70, Color.Red), Color.FromArgb(160, Color.Red)),
                ZoneType.BullishFVG => (Color.FromArgb(55, Color.DodgerBlue), Color.FromArgb(140, Color.DodgerBlue)),
                ZoneType.BearishFVG => (Color.FromArgb(55, Color.Orange), Color.FromArgb(140, Color.Orange)),
                _ => (Color.FromArgb(50, Color.Gray), Color.FromArgb(120, Color.Gray))
            };
        }
    }
}