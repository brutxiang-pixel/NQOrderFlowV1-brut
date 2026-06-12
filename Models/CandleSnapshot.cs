using System;
using System.Collections.Generic;
using System.Text;

namespace NQOrderFlowV1.Models
{
    public sealed class CandleSnapshot
    {
        public int Bar { get; init; }

        public decimal Open { get; init; }
        public decimal High { get; init; }
        public decimal Low { get; init; }
        public decimal Close { get; init; }

        public decimal Volume { get; init; }
        public decimal Delta { get; init; }

        public decimal POC { get; init; }
        public decimal VAH { get; init; }
        public decimal VAL { get; init; }

        public IReadOnlyList<PriceLevelSnapshot> TopLevels { get; init; } = Array.Empty<PriceLevelSnapshot>();

        public string ToProbeText()
        {
            var sb = new StringBuilder();

            sb.Append($"BAR={Bar} ");
            sb.Append($"O={Open} H={High} L={Low} C={Close} ");
            sb.Append($"V={Volume} D={Delta} ");
            sb.Append($"VAH={VAH} VAL={VAL} POC={POC}");

            if (TopLevels.Count > 0)
            {
                sb.Append(" | TOP:");
                foreach (var level in TopLevels)
                {
                    sb.Append($" [{level.Price} B{level.Bid} A{level.Ask} V{level.Volume}]");
                }
            }

            return sb.ToString();
        }
    }

    public sealed class PriceLevelSnapshot
    {
        public decimal Price { get; init; }
        public decimal Bid { get; init; }
        public decimal Ask { get; init; }
        public decimal Volume { get; init; }
        public int Ticks { get; init; }
        public decimal Between { get; init; }
        public int Time { get; init; }
    }
}