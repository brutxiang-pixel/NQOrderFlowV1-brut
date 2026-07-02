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
        // File Logging
        // =========================
        private void EnsureLogInitialized()
        {
            if (!EnableFileLog)
                return;

            if (!string.IsNullOrWhiteSpace(_logPath))
                return;

            try
            {
                var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                var dir = Path.Combine(appData, "ATAS", string.IsNullOrWhiteSpace(LogFolderName) ? "StrategyLogs" : LogFolderName);

                Directory.CreateDirectory(dir);

                var file = string.IsNullOrWhiteSpace(LogFileName) ? "NQOrderFlowV1.log" : LogFileName;
                _logPath = Path.Combine(dir, file);

                AppendLog($"=== Strategy Start ===");
                AppendLog($"LogPath={_logPath}");
            }
            catch
            {
                _logPath = null;
            }
        }

        private void AppendLog(string message)
        {
            if (!EnableFileLog)
                return;

            if (string.IsNullOrWhiteSpace(_logPath))
                return;

            try
            {
                var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} | {message}{Environment.NewLine}";
                File.AppendAllText(_logPath!, line);
            }
            catch
            {
                // ignore
            }
        }

        private void MaybeLogPhaseChange(int bar, IndicatorCandle cur)
        {
            if (!EnableFileLog)
                return;

            if (_phase == _lastLoggedPhase)
                return;

            var t = TryGetBaseCandleTime(cur);
            var time = t.HasValue ? t.Value.ToString("yyyy-MM-dd HH:mm") : "-";

            var q = _lockedZoneQualityScore >= 0 ? $"{_lockedZoneQualityScore}/10" : "-";

            AppendLog($"PHASE bar={bar} time={time} phase={_phase} bias={_htfBias}({_htfBiasReason}) zone={(_confirmZoneKey is null ? "-" : FormatZoneKeyCN(_confirmZoneKey))}{(_confirmZoneUsingShadow ? "(shadow)" : "")} q={q} ref={_confirmRefText}{(_confirmRefIsFallback ? "(FB)" : "")} armed={_ofArmed} pending={(_pendingEntry is null ? "-" : _pendingEntry.LimitPrice.ToString("0.00"))} live={(_live is null ? "-" : _live.TradeId)} text={_confirmText}");

            _lastLoggedPhase = _phase;
        }

        private void MaybeLogTriggerBlock(int bar, IndicatorCandle cur)
        {
            if (!EnableFileLog)
                return;

            var sameReasonN = Math.Max(1, LogSameReasonEveryNBars);

            if (_barBlockReason == _lastLoggedBlockReason)
            {
                _sameReasonRun++;
                if (_sameReasonRun < sameReasonN)
                    return;

                _sameReasonRun = 0;
            }
            else
            {
                _sameReasonRun = 0;
            }

            if (_planState != _lastLoggedPlanState)
            {
                AppendLog($"PLAN_STATE bar={bar} state={_planState}");
                _lastLoggedPlanState = _planState;
            }

            if (_lastLoggedBar == bar && _barBlockReason == _lastLoggedBlockReason)
                return;

            var t = TryGetBaseCandleTime(cur);
            var time = t.HasValue ? t.Value.ToString("yyyy-MM-dd HH:mm") : "-";

            AppendLog($"BLOCK bar={bar} time={time} reason={_barBlockReason} detail={_barBlockDetail}");

            _lastLoggedBlockReason = _barBlockReason;
            _lastLoggedBar = bar;
        }
    }
}