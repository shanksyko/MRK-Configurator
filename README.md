# MRK-Configurator

Mieruka Configurator centralizes the setup of monitors, window bindings, and browser scenes
used by the Mieruka digital signage environment. The application ships with a background tray
agent, preview helpers, and automation scripts that depend on diagnostic data to troubleshoot
field issues.

## Support diagnostics

- **Log location / Local dos logs**: every launch writes daily files to
  `%LOCALAPPDATA%\Mieruka\Logs`. Files follow the pattern `mieruka-YYYYMMDD.log`
  (text) and `mieruka-YYYYMMDD.json` (structured JSON).
- **Retention / Retenção**: Serilog keeps the last 7 log files and removes older ones
  automatically. If you need a longer history, copy the folder before launching the app.
- **Trace mode / Modo detalhado**: set the environment variable `MIERUKA_TRACE` to `1`,
  `true`, or `verbose` before starting the Configurator to switch the log level to `Verbose`
  without rebuilding the binaries.
- **Support package / Pacote de suporte**: compress the entire
  `%LOCALAPPDATA%\Mieruka\Logs` directory when sending information to the support team. This
  includes daily log files and any crash dumps generated in the same location.

Additional troubleshooting steps and Windows-specific collection tips are available in
[`docs/Troubleshooting.md`](docs/Troubleshooting.md).
