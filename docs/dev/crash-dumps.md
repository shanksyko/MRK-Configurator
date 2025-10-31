# Coleta de dumps (WER)

Para habilitar a coleta automática de dumps do `Mieruka.App.exe`, execute o prompt de comando como administrador e aplique as chaves abaixo:

```cmd
reg add "HKLM\SOFTWARE\Microsoft\Windows\Windows Error Reporting\LocalDumps\Mieruka.App.exe" /v DumpType /t REG_DWORD /d 2 /f
reg add "HKLM\SOFTWARE\Microsoft\Windows\Windows Error Reporting\LocalDumps\Mieruka.App.exe" /v DumpCount /t REG_DWORD /d 2 /f
reg add "HKLM\SOFTWARE\Microsoft\Windows\Windows Error Reporting\LocalDumps\Mieruka.App.exe" /v DumpFolder /t REG_EXPAND_SZ /d "%LOCALAPPDATA%\Mieruka\Dumps" /f
```

Os dumps serão gravados em `%LOCALAPPDATA%\Mieruka\Dumps` e sobrescreverão os arquivos mais antigos quando o limite de dois dumps for atingido.

## Símbolos e PDBs

- Certifique-se de que o build **Debug** gere PDBs para `Mieruka.App`, `Mieruka.Preview` e `Mieruka.Core`.
- Configure o Visual Studio ou o WinDbg para procurar símbolos no diretório `build\artifacts\symbols` (ou no diretório configurado no pipeline) antes de consultar os servidores públicos.
- Ao analisar um dump local, adicione também a pasta de saída do build (`build\artifacts\bin\Debug\net8.0-windows`) como caminho adicional de símbolos para carregar os binários correspondentes.

Isso garante que os dumps gerados contenham símbolos adequados para investigações de StackOverflowException ou outras falhas críticas.
