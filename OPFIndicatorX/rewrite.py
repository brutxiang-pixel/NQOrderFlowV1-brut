from pathlib import Path
p=Path('OPFIndicatorX/OpfResearchIndicator.cs'); s=p.read_text()
s=s.replace('    private int _lastAlertBar = -1;\n','')
start=s.index('        // Stage A probe does not emit candidate alerts.')
end=s.index('    }\n\n    protected override void OnRender',start)
body='''        // Stage A probe does not emit candidate alerts.
        if (ShowHud && bar == CurrentBar)
        {
            var text = $"OPF X / GEX\\nStatus: {_status}\\nInstrument: {InstrumentInfo?.Instrument ?? "-"}\\nPrice: {_underlyingPrice?.ToString("0.##") ?? "-"}\\nOptions: series={_optionSeriesCount} contracts={_optionCount} subscribed={_subscribedCount}\\nExecution: indicator-only";
            AddText("OPF_X_STATUS", text, true, bar, candle.High, Color.White, Color.FromArgb(190, 25, 30, 40), 10, TextAlign.Left, true);
        }
'''
s=s[:start]+body+s[end:]
start=s.index('    protected override void OnRender')
end=s.index('    private async void TryStartOptionsProbe()',start)
s=s[:start]+s[end:]
p.write_text(s)
