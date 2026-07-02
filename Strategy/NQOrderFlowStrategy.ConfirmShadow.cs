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
        // Fix A：shadow invalidation（HTF close 口径复刻）
        // - Only applies to HTF zones (non-M5).
        // =========================
        private void UpdateConfirmShadowInvalidationByHtfClose(int htfIndex)
        {
            if (_confirmZoneShadow is null || _confirmZoneShadow.IsInvalidated)
                return;

            if (IsM5ZoneText(_confirmZoneShadow.Text))
                return;

            if (htfIndex < 0 || htfIndex >= _htfSeries.Count)
                return;

            var cur = _htfSeries[htfIndex];
            var prev = htfIndex > 0 ? _htfSeries[htfIndex - 1] : null;

            if (cur.BaseEndBar <= _confirmZoneShadow.CreatedBar)
                return;

            var halfTick = TickSizeNq / 2m;

            var invalidatedNow = false;

            switch (_confirmZoneShadow.Type)
            {
                case ZoneType.BullishOB:
                    if (cur.Close < _confirmZoneShadow.Low - halfTick)
                        invalidatedNow = true;
                    break;

                case ZoneType.BearishOB:
                    if (prev is not null &&
                        prev.BaseEndBar > _confirmZoneShadow.CreatedBar &&
                        prev.Close > _confirmZoneShadow.High + halfTick &&
                        cur.Close > _confirmZoneShadow.High + halfTick)
                    {
                        invalidatedNow = true;
                    }
                    break;

                case ZoneType.BullishFVG:
                    if (cur.Close < _confirmZoneShadow.Low - halfTick)
                        invalidatedNow = true;
                    break;

                case ZoneType.BearishFVG:
                    if (cur.Close > _confirmZoneShadow.High + halfTick)
                        invalidatedNow = true;
                    break;
            }

            if (!invalidatedNow)
                return;

            _confirmZoneShadow.IsInvalidated = true;

            if (_confirmZoneKey is not null &&
                ZoneMatchesKey(_confirmZoneShadow, _confirmZoneKey) &&
                _phase is ConfirmPhase.WaitM5Bos or ConfirmPhase.Confirmed)
            {
                AppendLog($"LOCKED_ZONE_INVALIDATED_BY_HTF closeBar={cur.BaseEndBar} zone={_confirmZoneShadow.ToShortText()} X -> reset confirm");
                ResetConfirm(cur.BaseEndBar, "锁定区块HTF收盘失效");
                _phase = ConfirmPhase.WaitZoneTouch;
            }
        }

        
        private void UpdateConfirmShadowStateByBaseBar(int bar, IndicatorCandle? prevBase, IndicatorCandle curBase)
        {
            if (_confirmZoneShadow is null || _confirmZoneShadow.IsInvalidated)
                return;

            if (bar <= _confirmZoneShadow.CreatedBar)
                return;

            var eps = TickSizeNq / 2m;
            var fuzzyPts = M5CloseFuzzyTicks > 0
                ? M5CloseFuzzyTicks * GetTickSize(bar, "M5CloseFuzzy")
                : 0m;

            var overlapped =
                curBase.High >= _confirmZoneShadow.Low - eps &&
                curBase.Low <= _confirmZoneShadow.High + eps;

            if (overlapped)
            {
                _confirmZoneShadow.IsTouched = true;
                _confirmZoneShadow.TouchCount++;
            }

            // mitigated (FVG only) - 75% fill
            if (_confirmZoneShadow.IsTouched && !_confirmZoneShadow.IsMitigated)
            {
                var range = _confirmZoneShadow.High - _confirmZoneShadow.Low;
                if (range > 0m)
                {
                    const decimal fill = 0.75m;

                    if (_confirmZoneShadow.Type == ZoneType.BullishFVG)
                    {
                        var level = _confirmZoneShadow.High - range * fill;
                        if (curBase.Low <= level + eps)
                            _confirmZoneShadow.IsMitigated = true;
                    }
                    else if (_confirmZoneShadow.Type == ZoneType.BearishFVG)
                    {
                        var level = _confirmZoneShadow.Low + range * fill;
                        if (curBase.High >= level - eps)
                            _confirmZoneShadow.IsMitigated = true;
                    }
                }
            }

            var invalidatedNow = false;

            switch (_confirmZoneShadow.Type)
            {
                case ZoneType.BullishOB:
                    if (curBase.Close < _confirmZoneShadow.Low - eps - fuzzyPts)
                        invalidatedNow = true;
                    break;

                case ZoneType.BearishOB:
                    if (prevBase is not null &&
                        prevBase.Close > _confirmZoneShadow.High + eps + fuzzyPts &&
                        curBase.Close > _confirmZoneShadow.High + eps + fuzzyPts)
                    {
                        invalidatedNow = true;
                    }
                    break;

                case ZoneType.BullishFVG:
                    if (curBase.Close < _confirmZoneShadow.Low - eps - fuzzyPts)
                        _invalidCloseBars++;
                    else
                        _invalidCloseBars = 0;
                    if (_invalidCloseBars >= 2)
                        invalidatedNow = true;
                    break;

                case ZoneType.BearishFVG:
                    if (curBase.Close > _confirmZoneShadow.High + eps + fuzzyPts)
                        _invalidCloseBars++;
                    else
                        _invalidCloseBars = 0;
                    if (_invalidCloseBars >= 2)
                        invalidatedNow = true;
                    break;
            }

            // 2.2 区块质量过滤：被触碰超过3次 → 失效重置
            if (!invalidatedNow && _confirmZoneShadow.TouchCount > 3)
            {
                AppendLog($"ZONE_OVER_TOUCHED bar={bar} zone={_confirmZoneShadow.ToShortText()} touches={_confirmZoneShadow.TouchCount} -> reset confirm");
                ResetConfirm(bar, "区块被触碰超过3次");
                _phase = ConfirmPhase.WaitZoneTouch;
                return;
            }

            if (invalidatedNow &&
                _confirmZoneKey is not null &&
                ZoneMatchesKey(_confirmZoneShadow, _confirmZoneKey) &&
                _phase is ConfirmPhase.WaitM5Bos or ConfirmPhase.Confirmed)
            {
                AppendLog($"LOCKED_ZONE_INVALIDATED_BY_BASE bar={bar} zone={_confirmZoneShadow.ToShortText()} X -> reset confirm");
                ResetConfirm(bar, "锁定区块M5收盘失效");
                _phase = ConfirmPhase.WaitZoneTouch;
            }
        }
    }
}
