# Changelog

Todas as alterações notáveis do projeto serão documentadas neste arquivo.

## [1.6.0] - 2026-02-23

### Corrigido
- **Exportação CSV zoada no Excel** — separador alterado de `,` para `;` (compatível com locale pt-BR) e arquivo agora inclui BOM UTF-8 para correta detecção de acentos (ã, ç, é, ô).
- **App não abria em outros PCs** — banco de dados do inventário usava `AppContext.BaseDirectory` que em single-file aponta para pasta temporária. Corrigido para `%LOCALAPPDATA%\Mieruka\mieruka.db`.
- **Logs gravados em local errado** — Serilog agora escreve em `%LOCALAPPDATA%\Mieruka\Logs` em vez de `AppContext.BaseDirectory\logs`.
- **Botão "Abrir Logs"** abria pasta incorreta — corrigido para apontar para `%LOCALAPPDATA%\Mieruka\Logs`.
- **Versão hardcoded** `v1.5.0` no log de inicialização — agora lê do assembly automaticamente.
- **AssemblyVersion/FileVersion** dessincronizdos (`1.5.2.0`) — alinhados com a versão do pacote.
- **Logger.cs** usava diretório `MierukaConfiguratorPro` — unificado para `Mieruka` (consistente com o restante do app).

### Melhorado
- **Preview Host embutido** — `Mieruka.Preview.Host.exe` pode ser embutido como recurso no App e extraído automaticamente, permitindo distribuir um único executável.
- **README.md** — documentação de logs atualizada (formato correto de arquivos, retenção de 7 dias).
- **.gitignore** — adicionadas entradas para `publish-host/`, `nul`, `tmpclaude-*`.

## [1.5.3] - 2026-02-23

### Lançamento inicial
- Release inicial com single-file publish para Windows x64.
- Inclui `Mieruka.App.exe` e `Mieruka.Preview.Host.exe` self-contained (.NET 9).
