using System.ComponentModel;
using ATAS.DataFeedsCore;

namespace OPFStrategyV1.Strategy;

public sealed partial class OpeningPullbackFailureStrategy
{
    private readonly StartupTestOrderLatch _startupTestOrder = new();
    private DateTime _startupTestOrderDeadlineUtc;
    private string _startupTestOrderWaitingReason = string.Empty;

    [Category("OPF Execution")]
    [DisplayName("Startup Test Order (Once)")]
    [Description("Default off. Enable while stopped, then start: attempt one Long contract on NQ/MNQ with 4-point SL and 6-point TP. Resets automatically; blocked requests are not retried.")]
    [DefaultValue(false)]
    public bool StartupTestOrderEnabled { get; set; }

    private void TryExecuteStartupTestOrder(int bar)
    {
        if (_snapshot is null || _lastResearchCandle is null || !_startupTestOrder.IsPending)
            return;

        var time = GetCandle(bar)?.Time ?? _lastResearchCandle.Time;
        var reason = string.Empty;
        if (!ActualOrdersEnabled || _manualAlertEnabled) reason = "AutoModeRequired";
        else if (_esOptionsChartRoot is not ("NQ" or "MNQ")) reason = "NqMnqChartRequired";
        else if (_isStoppingActualExecution) reason = "StrategyStopping";
        else if (Portfolio is null || Security is null) reason = "ContextMissing";
        else if (_liveReadinessBlocked) reason = $"LiveReadinessBlocked:{_liveReadinessBlockReason}";
        else if (_dailyPnlScopeUnresolved) reason = "LegacyPnlScopeUnresolved";
        else if (_manualInterventionLatch) reason = "ManualInterventionDetected";
        else if (IsLiveLatencyBlocked(time, out var latency)) reason = latency;
        else if (IsGlobexCloseoutLockWindow(time, out var closeout)) reason = closeout;
        else if (IsUsCashOpenBlackout(time, out var blackout)) reason = blackout;
        else if (ActiveReplayExecutions().Count > 0 || GetCurrentAccountPosition() != 0m ||
                 Connector?.Orders.Any(o => IsWorkingExecutionOrder(o) && Equals(o.Portfolio, Portfolio) && Equals(o.Security, Security)) == true) reason = "AccountNotFlatOrWorkingOrdersPresent";
        else if (_actualDailyLossLimitDollars > 0m && _liveAccountDailyNetPnlDollars <= -_actualDailyLossLimitDollars) reason = "DailyLossLimit";
        else if (ReplayMaxTradesPerDay > 0 && _replayTradesToday >= ReplayMaxTradesPerDay) reason = "DailyTradeLimit";

        var entry = CurrentExecutionMarketReference(_latestCalculatedMarketPrice);
        if (reason.Length == 0 && (entry <= 0m || !TryValidateExecutionQuotePair(entry, out _)))
            reason = "QuoteUnavailable";
        if (reason.Length > 0)
        {
            if (StartupTestOrderLatch.CanWait(reason, DateTime.UtcNow, _startupTestOrderDeadlineUtc))
            {
                if (_startupTestOrderWaitingReason != reason)
                {
                    _startupTestOrderWaitingReason = reason;
                    LogExecutionInfo($"STARTUP_TEST_ORDER_WAIT reason={reason} deadline={_startupTestOrderDeadlineUtc:O}");
                }
                return;
            }
            _startupTestOrder.TryConsume();
            LogExecutionInfo($"STARTUP_TEST_ORDER_BLOCKED reason={reason} consumed=true");
            RaiseShowNotification($"OPF test order blocked: {reason}. Switch reset to off; enable and restart to retry.", "OPFStrategyV1");
            return;
        }

        if (!_startupTestOrder.TryConsume()) return;
        const string path = "StartupTestOrder";
        var tradeId = $"TEST-{DateTime.UtcNow:yyyyMMdd-HHmmssfff}-{_snapshot.SnapshotId}";
        var stop = AlignToTick(entry - 4m, _snapshot.InstrumentProfile.TickSize);
        var target = AlignToTick(entry + 6m, _snapshot.InstrumentProfile.TickSize);
        var order = new Order
        {
            Portfolio = Portfolio, Security = Security, Type = OrderTypes.Market,
            Direction = OrderDirections.Buy, QuantityToFill = 1m,
            TimeInForce = ReplayTimeInForce, AutoCancel = false,
            Comment = $"OPF|{tradeId}|ENTRY|{path}|{tradeId}"
        };
        var execution = new ReplayExecutionState(tradeId, tradeId, Core.Signals.TradeSide.Long, path,
            bar, time, entry, 1m, stop, target, 4m, 6m, 1.5m, order);
        _replayExecution = execution;
        TrackRecentReplayExecution(execution);
        _replayTradesToday++;
        AppendExecutionEvent(execution, "STARTUP_TEST_ORDER_CONSUMED", "ENTRY", entry, 1m,
            "switch=false|side=Long|plannedSL=4pt|plannedTP=6pt");
        EnqueueExecutionAction("StartupTestOrder", async () => await OpenReplayEntryAfterQuotePreflightAsync(execution));
    }
}
