from pathlib import Path
p=Path('OPFIndicatorX/OpfResearchIndicator.cs')
s=p.read_text()
s=s.replace('public bool EnableCandidateAlerts { get; set; } = true;', 'public bool EnableCandidateAlerts { get; set; } = false;')
start=s.index('        if (EnableCandidateAlerts && bar != _lastAlertBar && bar > 0)')
end=s.index('    }\n\n    protected override void OnRender', start)
new='''        // Historical recalculation must never generate live alerts. Only the current bar is eligible.
        if (EnableCandidateAlerts && bar == CurrentBar && bar != _lastAlertBar && bar > 0)
        {
            var range = candle.High - candle.Low;
            var body = Math.Abs(candle.Close - candle.Open);
            if (range > 0m && body >= range * 0.65m)
            {
                _lastAlertBar = bar;
                _signalSeries[bar] = candle.Close;
                provider.AddAlert("OPF X candidate", $"Strong candle {candle.Time:yyyy-MM-dd HH:mm} close={candle.Close:0.##}", "OPF X", Color.DodgerBlue, Color.White, candle.Time);
            }
        }
        UpdateHudText(bar, candle);
'''
s=s[:start]+new+s[end:]
start=s.index('    protected override void OnRender')
end=s.index('    private async void TryStartOptionsProbe()', start)
new='''    private void UpdateHudText(int bar, IndicatorCandle candle)
    {
        if (!ShowHud || bar != CurrentBar)
            return;

        var text = $"OPF X / GEX\\nStatus: {_status}\\nInstrument: {InstrumentInfo?.Instrument ?? "-"}\\nPrice: {_underlyingPrice?.ToString("0.##") ?? "-"}\\nOptions: series={_optionSeriesCount} contracts={_optionCount} subscribed={_subscribedCount}\\nExecution: indicator-only";
        AddText("OPF_X_STATUS", text, true, bar, candle.High, Color.White, Color.FromArgb(190, 25, 30, 40), 10, TextAlign.Left, true);
    }

'''
s=s[:start]+new+s[end:]
p.write_text(s)
