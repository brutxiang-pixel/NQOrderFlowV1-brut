from pathlib import Path
p=Path('OPFIndicatorX/OpfResearchIndicator.cs')
s=p.read_text()
start=s.index('        if (EnableCandidateAlerts && bar != _lastAlertBar && bar > 0)')
end=s.index('    }\n\n    protected override void OnRender', start)
s=s[:start]+'        // Stage A probe does not emit candidate alerts.\n'+s[end:]
s=s.replace('public bool EnableCandidateAlerts { get; set; } = true;', 'public bool EnableCandidateAlerts { get; set; } = false;')
p.write_text(s)
