using System;
using System.Collections.Generic;

namespace NQOrderFlowV1.Models
{
    public enum TrendDirection
    {
        Neutral,
        Bullish,
        Bearish
    }

    public enum SwingPointType
    {
        High,
        Low
    }

    public enum StructureBreakType
    {
        None,
        BOS,
        CHOCH
    }

    public enum BreakDirection
    {
        None,
        Up,
        Down
    }

    public enum StructureEventType
    {
        SwingHigh,
        SwingLow,
        BOS,
        CHOCH
    }

    public sealed class SwingPoint
    {
        public int Bar { get; init; }
        public decimal Price { get; init; }
        public SwingPointType Type { get; init; }
    }

    public sealed class StructureEvent
    {
        public int Bar { get; init; }
        public decimal Price { get; init; }
        public StructureEventType EventType { get; init; }
        public string Text { get; init; } = string.Empty;
    }

    public sealed class StructureSnapshot
    {
        public int CurrentBar { get; init; }
        public decimal CurrentClose { get; init; }

        public TrendDirection Trend { get; init; } = TrendDirection.Neutral;

        public SwingPoint? LastSwingHigh { get; init; }
        public SwingPoint? PrevSwingHigh { get; init; }

        public SwingPoint? LastSwingLow { get; init; }
        public SwingPoint? PrevSwingLow { get; init; }

        public string HighLabel { get; init; } = "-";
        public string LowLabel { get; init; } = "-";

        public StructureBreakType BreakType { get; init; } = StructureBreakType.None;
        public BreakDirection BreakDirection { get; init; } = BreakDirection.None;
        public string BreakText { get; init; } = string.Empty;
    }

    public sealed class StructureUpdateResult
    {
        public StructureSnapshot Snapshot { get; init; } = new();
        public IReadOnlyList<StructureEvent> Events { get; init; } = Array.Empty<StructureEvent>();

        public bool HasEvents => Events.Count > 0;
    }
}