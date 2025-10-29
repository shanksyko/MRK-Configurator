# Troubleshooting / Solução de Problemas

## Log location / Localização dos logs
- **EN**: Open `%LOCALAPPDATA%\Mieruka\Logs` and pick the subfolder that matches the current month
  (for example, `2025-03`). Daily files are named `<yyyy-MM-dd>.log`.
- **PT-BR**: Abra `%LOCALAPPDATA%\Mieruka\Logs` e escolha a pasta do mês atual (por exemplo,
  `2025-03`). Os arquivos diários seguem o padrão `<yyyy-MM-dd>.log`.

## Retention policy / Política de retenção
- **EN**: The Configurator removes files older than 14 days when it starts. Copy the folder before
  launching the application if you need to archive older diagnostics.
- **PT-BR**: O Configurator remove arquivos com mais de 14 dias ao iniciar. Copie a pasta antes de
  abrir o aplicativo caso precise arquivar diagnósticos antigos.

## Enable trace logging / Ativar log detalhado
1. **EN**: Close the Configurator, set the environment variable `MIERUKA_TRACE` to `1`, `true`, or
   `verbose`, and launch the app again. All new log entries will use the `Verbose` level until the
   process exits.
2. **PT-BR**: Feche o Configurator, defina a variável de ambiente `MIERUKA_TRACE` como `1`, `true`
   ou `verbose` e abra o aplicativo novamente. As próximas entradas serão registradas em nível
   `Verbose` até o encerramento do processo.

## Collect a support package / Coletar pacote de suporte
1. **EN**: Open `%LOCALAPPDATA%\Mieruka` and include both the `Logs` directory and any `Crashes`
   folder present.
2. **EN**: Compress the selection into a `.zip` file. Tools such as File Explorer (Send To →
   Compressed folder) or `tar.exe -acf support.zip Logs Crashes` work well.
3. **PT-BR**: Abra `%LOCALAPPDATA%\Mieruka` e selecione a pasta `Logs` junto com a pasta
   `Crashes`, se existir.
4. **PT-BR**: Compacte os itens em um arquivo `.zip`. Você pode usar o Explorador de Arquivos
   (Enviar para → Pasta compactada) ou o comando `tar.exe -acf suporte.zip Logs Crashes`.

## Quick checklist / Checklist rápido
- **EN**: Capture the exact timestamp of the issue, note the monitor or app affected, and attach the
  zipped logs.
- **PT-BR**: Registre o horário exato do incidente, informe qual monitor ou aplicativo foi afetado e
  anexe os logs compactados.
