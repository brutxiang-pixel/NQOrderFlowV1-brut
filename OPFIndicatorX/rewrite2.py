from pathlib import Path
import re
p=Path('OPFIndicatorX/OpfResearchIndicator.cs'); s=p.read_text()
s=s.replace('    private int _lastAlertBar = -1;\n','')
# replace escaped-literal block until method close
pat=r'        // Stage A probe does not emit candidate alerts\?n.*?\n    \}\n\n    protected override void OnRender.*?\n    \}\n\n    private async void TryStartOptionsProbe'
rep='''        // Stage A probe does not emit candidate alerts.
        if (ShowHud && bar == CurrentBar)
        {
            var text = $"OPF X / GEX\\nStatus: {_status}\\nInstrument: {InstrumentInfo?.Instrument ?? "-"}\\nPrice: {_underlyingPrice?.ToString("0.##") ?? "-"}\\nOptions: series={_optionSeriesCount} contracts={_optionCount} subscribed={_subscribedCount}\\nExecution: indicator-only";
            AddText("OPF_X_STATUS", text, true, bar, candle.High, Color.White, Color.FromArgb(190, 25, 30, 40), 10, TextAlign.Left, true);
        }
    }

    private async void TryStartOptionsProbe'''
s2,n=re.subn(pat,rep,s,flags=re.S)
print('replaced',n)
if n!=1: raise SystemExit(1)
p.write_text(s2)
