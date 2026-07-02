// File: Strategy/NQOrderFlowStrategy.cs
// Version intent (2026-06):
// - Stage B Live Orders: real orders via OpenOrderAsync/CancelOrderAsync/ModifyOrderAsync,
//   driven by OnNewMyTrade/OnOrderChanged callbacks.
// - Q-Freeze: Zone Quality only evaluated at lock time; no dynamic Q reset while pending/confirmed.
// - Fix: Do NOT reset locked/confirmed session when candidate list becomes empty (quality filter/topN view).
// - Pending lifecycle: controlled by OF window (pre-armed) + pending effWait/expired/runaway.
// - Stop: Swing/Rolling candidates on wrong side are INVALID (no "-> Corrected").
// - Place Distance Gate: avoid placing limit when price already too far from zone (ENTRY_ORDER_SKIP PlaceTooFar).
// - Inner Anchor (Q25/T33): reduce Mid-too-deep expirations.
// - Mitigated policy M1: DisallowMitigatedZones applies only pre-armed (before OF_ARMED / before pending). After armed/pending: keep setup; log once.
// - Adaptive OF wait window: qLock>=HighScore => allow longer wait (e.g. 12), else base (e.g. 8).
//
// NEW (M5 Zones integration):
// - HTF15 zones remain for reference/drawing (optional).
// - Trading candidate pool can switch to M5 FVG zones for higher frequency, still gated by HTF bias.
// - Shadow lifecycle updated on EACH base bar (touched/mitigated/invalidated), not only HTF close.
//
// BUGFIXES (requested):
// 1) ResetConfirm duplicate ENTRY_ORDER_CANCEL log -> fixed via PendingEntry.CancelLogged + clearing pending before reset on fill.
// 2) Limit AntiChase branch returned without clearing pending -> now cancel+reset.
//
// Tick alignment (required for live):
// - Direction-safe tick alignment using Floor/Ceil for Entry/SL/TP/BE.

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
    public partial class NQOrderFlowStrategy : ChartStrategy
    {
        public NQOrderFlowStrategy()
        {
            EnableCustomDrawing = true;
            SubscribeToDrawingEvents(DrawingLayouts.Final | DrawingLayouts.LatestBar);
        }
    }
}