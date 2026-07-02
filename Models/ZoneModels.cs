    using System;
    using System.Collections.Generic;

    namespace NQOrderFlowV1.Models
    {
        public enum ZoneType
        {
            BullishOB,
            BearishOB,
            BullishFVG,
            BearishFVG
        }

        public sealed class TradingZone
        {
            public ZoneType Type { get; init; }

            /// <summary>
            /// 区块来源K线（OB来源K线或FVG起点）
            /// </summary>
            public int StartBar { get; init; }

            /// <summary>
            /// 区块被识别创建时所在Bar
            /// </summary>
            public int CreatedBar { get; init; }

            public decimal Low { get; init; }

            public decimal High { get; init; }

            /// <summary>
            /// 价格是否触碰过该区块（与区间发生重叠即触碰）
            /// </summary>
            public bool IsTouched { get; set; }
            public int TouchCount { get; set; }

            /// <summary>
            /// 缓解/回补状态（用于 FVG：触及 50% 记为已缓解，但不一定失效移除）
            /// </summary>
            public bool IsMitigated { get; set; }

            /// <summary>
            /// VP过滤拒绝标记：不满足 POC/VAH/VAL/VWAP 距离条件。
            /// 仅用于“可视化保留但禁止交易/信号”的灰色状态。
            /// </summary>
            public bool IsVpRejected { get; set; }

            /// <summary>
            /// 失效/无效（从 ActiveZones 中移除 & 不再绘制）
            /// </summary>
            public bool IsInvalidated { get; set; }

            public string Text { get; init; } = string.Empty;

            public decimal Mid => (Low + High) / 2m;

            public bool Contains(decimal price)
            {
                return price >= Low && price <= High;
            }

            public string ToShortText()
            {
                var name = Type switch
                {
                    ZoneType.BullishOB => "BullOB",
                    ZoneType.BearishOB => "BearOB",
                    ZoneType.BullishFVG => "BullFVG",
                    ZoneType.BearishFVG => "BearFVG",
                    _ => "Zone"
                };

                // 标记优先级：Invalidated > VPRejected > Mitigated > Touched
                var flag = string.Empty;

                if (IsInvalidated)
                    flag = " X";
                else if (IsVpRejected)
                    flag = " R";
                else if (IsMitigated)
                    flag = " M";
                else if (IsTouched)
                    flag = " T";

                return $"{name} {Low:0.00}-{High:0.00}{flag}";
            }
        }

        public sealed class ZoneUpdateResult
        {
            public IReadOnlyList<TradingZone> NewZones { get; init; }
                = Array.Empty<TradingZone>();

            public IReadOnlyList<TradingZone> ActiveZones { get; init; }
                = Array.Empty<TradingZone>();

            public bool HasEvents => NewZones.Count > 0;
        }
    }
