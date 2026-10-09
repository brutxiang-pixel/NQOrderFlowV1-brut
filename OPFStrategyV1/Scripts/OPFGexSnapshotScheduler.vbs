Set shell = CreateObject("WScript.Shell")
shell.Run """C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe"" -NoProfile -NonInteractive -ExecutionPolicy Bypass -WindowStyle Hidden -File ""C:\Users\Administrator\source\repos\NQOrderFlowV10629\NQOrderFlowV1\OPFStrategyV1\Scripts\Start-OPFGexSnapshotScheduler.ps1""", 0, False
