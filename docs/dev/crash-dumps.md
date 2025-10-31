# Coleta de dumps (WER)
Reg Add HKLM\SOFTWARE\Microsoft\Windows\Windows Error Reporting\LocalDumps\Mieruka.App.exe /v DumpType /t REG_DWORD /d 2 /f
Reg Add HKLM\SOFTWARE\Microsoft\Windows\Windows Error Reporting\LocalDumps\Mieruka.App.exe /v DumpCount /t REG_DWORD /d 2 /f
Reg Add HKLM\SOFTWARE\Microsoft\Windows\Windows Error Reporting\LocalDumps\Mieruka.App.exe /v DumpFolder /t REG_EXPAND_SZ /d "%LOCALAPPDATA%\Mieruka\Dumps" /f
