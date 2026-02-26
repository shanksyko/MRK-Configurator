# Tasks Concluídas — MRK-Configurator

Registro completo de todas as tarefas implementadas no projeto.

---

## 1. Planejamento e Infraestrutura

- [x] Definir escopo e arquitetura do projeto (multi-módulo .NET 9)
- [x] Configurar repositório GitHub com `.gitignore` e estrutura de pastas
- [x] Criar solução `Mieruka.sln` com 7 projetos (App, Core, Automation, Preview, Preview.Host, Preview.Ipc, Tests)
- [x] Configurar `Directory.Build.props` (Nullable, TreatWarningsAsErrors, LangVersion latest)
- [x] Configurar `Directory.Packages.props` para centralização de versões NuGet
- [x] Configurar publish single-file para Windows x64 (.NET 9 self-contained)
- [x] Configurar otimizações de build Release (ReadyToRun, TieredCompilation, GC tuning)
- [x] Configurar CI/CD via GitHub Actions
- [x] Definir padrão de desenvolvimento (branch, code review, changelog)

---

## 2. Autenticação e Controle de Acesso (ACL)

### Modelos de segurança
- [x] `User.cs` — entidade de usuário com campos de auditoria (`CreatedAt`, `UpdatedAt`, `LastLoginAt`, `MustChangePassword`)
- [x] `UserRole.cs` — enum de papéis (`Admin`, `Operator`, `Viewer`)
- [x] `AuditLogEntry.cs` — registro completo de ações por usuário
- [x] `DashboardCredential.cs` — credenciais criptografadas de dashboards
- [x] `Session.cs` — tokens de sessão
- [x] `ResourcePermission.cs` — permissão granular por recurso (Site, Application, Profile, Monitor)

### Banco de dados de segurança
- [x] `SecurityDbContext.cs` — DbContext EF Core com SQLite
- [x] Seed automático de usuário admin padrão (`admin` / `admin123`, `MustChangePassword = true`)
- [x] Configuração WAL mode + busy_timeout para concorrência
- [x] Criação automática de tabelas ausentes sem migrations
- [x] Registro de `ResourcePermissions` com índice único composto

### Serviços de autenticação
- [x] `IAuthenticationService` + `AuthenticationService` — autenticação, hashing PBKDF2-SHA256 (100k iterações), retry com backoff exponencial
- [x] `IAuditLogService` + `AuditLogService` — registro de todas as ações com retry
- [x] `IUserManagementService` + `UserManagementService` — CRUD de usuários (criar, editar, desativar, resetar senha, excluir)
- [x] `IAccessControlService` + `AccessControlService` — ACL com níveis View/Operate/Edit/Admin, permissões por recurso, expiração, fallback por role

### UI de segurança
- [x] `LoginForm` — tela de login com senha mascarada, submissão por Enter, mensagens de status
- [x] `ChangePasswordForm` — obrigatória no primeiro login
- [x] `UserManagementForm` — listagem de usuários com filtro por role e busca, DataGridView otimizado
- [x] `UserEditorForm` — criação e edição de usuários com ComboBox de roles
- [x] `ResetPasswordForm` — definição de nova senha com confirmação
- [x] `AuditLogViewerForm` — visualização de logs com filtros por data/usuário/ação e exportação CSV
- [x] `CredentialsManagementForm` — gerenciamento de credenciais de dashboards (CRUD + CredentialVault)
- [x] Botão "Excluir" (vermelho) no UserManagementForm com confirmação de segurança

### Fluxo de inicialização
- [x] `Program.cs` integrado com LoginForm — aplicação não abre sem autenticação
- [x] `LauncherForm` — seletor de módulo (Configurador vs Inventário) com link "Gerenciar Usuários" visível somente para Admin

---

## 3. Funcionalidades Core — Configurador

### Configuração de produtos
- [x] `GeneralConfig` — modelo raiz de configuração (aplicativos, sites, monitores, perfis, ciclo)
- [x] `AppConfig` — configuração de aplicativos nativos (path, args, autoStart, monitor alvo)
- [x] `SiteConfig` — configuração de sites/dashboards (URL, browser, appMode, kioskMode, watchdog, schedule)
- [x] `ProfileConfig` — perfis de layout reutilizáveis
- [x] `CycleConfig` + `CycleItem` — configuração de ciclos de exibição com hotkeys
- [x] `MonitorInfo` / `MonitorIdentifier` — informações e estabilidade de monitores
- [x] `WindowConfig` + `ZonePreset` — posicionamento por zona percentual no monitor

### Workspace e persistência
- [x] `ConfiguratorWorkspace` — estado editável da configuração em memória
- [x] `JsonStore<T>` — leitura/escrita de configuração JSON com backup automático
- [x] `ConfigMigrator` — migração automática de schemas antigos
- [x] `ConfigValidator` (parcial) — validação de drivers, paths, URLs, seletores, permissões

### Interface do configurador
- [x] `ConfigForm` — janela principal com abas (Aplicativos / Sites), busca, presets e validação
- [x] `AppEditorForm` — editor completo de aplicativos (12 arquivos partial: Monitor, Preview, CycleMetadata, CycleSimulation, SimOverlay, WindowOverlay, Testing, Save, etc.)
- [x] `SiteEditorDialog` — editor de sites/dashboards
- [x] `PresetSelectionDialog` — seleção e aplicação de presets de zona
- [x] `BrowserArgumentsPanel` + `WhitelistTab` + `LoginAutoTab` — painéis de configuração de browser
- [x] `CoordinateInputDialog` — entrada de coordenadas de zona

### Histórico e snapshots
- [x] `ConfigSnapshotService` — criação e restauração de snapshots de configuração
- [x] `ConfigHistoryForm` — visualização de histórico com restore
- [x] `JsonMigrationService` — migração de configuração JSON para banco SQLite

---

## 4. Automação de Browsers e Aplicativos

### Automação Selenium
- [x] `LoginService` — auto-login via Selenium (Chrome/Edge) com seletores CSS configuráveis
- [x] `LoginOrchestrator` — orquestração do fluxo de login
- [x] `SeleniumManager` — gerenciamento do ciclo de vida do WebDriver
- [x] `BrowserLauncher` — inicialização de browsers com parâmetros customizados
- [x] `WebDriverFactory` — fábrica de WebDrivers por tipo de browser
- [x] `TabManager` — gerenciamento de abas do browser

### Automação nativa
- [x] `NativeAppAutomator` — automação de aplicativos nativos (SendKeys, clicks, FindWindow)
- [x] `ProfileExecutor` — execução de sequências de aplicativos com posicionamento

### Serviços de suporte
- [x] `BrowserRegistry` — registro de browsers instalados
- [x] `BrowserArgumentBuilder` — construção de argumentos de linha de comando por browser
- [x] `WinFormsDialogHost` — ponte entre serviços e UI para diálogos

---

## 5. Watchdog e Orquestração

- [x] `WatchdogService` — monitoramento de processos a cada 5s, health checks HTTP e DOM, restart automático com backoff exponencial (2s→5min)
- [x] `CycleManager` — rotação de conteúdo com timer threadpool, play/pause/next/previous, shuffle
- [x] `HotkeyManager` — hotkeys globais de sistema (Win32 RegisterHotKey)
- [x] `Orchestrator` — coordenação de todos os serviços de execução
- [x] `SchedulerService` — start/stop agendado do Orchestrator por horário e dia da semana
- [x] `ScheduleEditorForm` — UI de configuração de agendamento
- [x] `BindingTrayService` — vinculação de janelas aos monitores/zonas configurados
- [x] `TrayMenuManager` — ícone na bandeja do sistema com menu de controle
- [x] `AppRunner` / `AppTestRunner` — inicialização e teste de aplicativos configurados

---

## 6. Preview de Monitores (Captura de Tela)

- [x] `GraphicsCaptureProvider` — captura via Windows Graphics Capture (GPU, WGC API)
- [x] `GdiCapture` / `GdiMonitorCapture` — captura via GDI (fallback para ambientes sem GPU)
- [x] `MonitorCaptureFactory` / `ResilientMonitorCapture` — seleção automática de provedor com fallback
- [x] `PreviewFrameScheduler` — agendamento de frames de preview com controle de FPS
- [x] `GpuCaptureGuard` — detecção de ambiente GPU (DWM, sessão remota, disponibilidade WGC) e desativação graceful
- [x] IPC Preview Host ↔ App (`PreviewIpcServer`, `PreviewIpcClient`, `PreviewIpcChannel`)
- [x] Preview Host embutido como recurso do App (distribuição single-file)

---

## 7. Módulo de Inventário

### UI do Inventário
- [x] `InventoryForm` — listagem com TreeView de categorias, busca, filtro de status, ToolStrip com ações
- [x] `InventoryDashboardForm` — dashboard com métricas e gráficos de inventário
- [x] `InventoryItemEditorForm` — criação e edição de itens com validação
- [x] `CategoryEditorForm` — gerenciamento de categorias de inventário
- [x] `MovementHistoryForm` — histórico de movimentações (entrada, saída, transferência)
- [x] `SqlServerConnectionDialog` — configuração de conexão com SQL Server remoto

### Serviços de inventário
- [x] `InventoryService` — CRUD de itens com filtros por categoria e status
- [x] `InventoryCategoryService` — gerenciamento de categorias com validação de duplicidade
- [x] `InventoryMovementService` — registro de movimentações (entrada, saída, transferência, ajuste)
- [x] `MaintenanceRecordService` — registro de manutenções com datas e custo
- [x] `InventoryExportService` — exportação para CSV (BOM UTF-8, separador `;` para Excel pt-BR), Access (.accdb) e SQL Server
- [x] `InventoryImportService` — importação de Access (.accdb) e SQL Server para SQLite

---

## 8. Banco de Dados Principal (MierukaDbContext)

### Entidades e mapeamento
- [x] `ApplicationEntity`, `SiteEntity`, `MonitorEntity`, `ProfileEntity`, `CycleItemEntity` — entidades de configuração
- [x] `InventoryItemEntity`, `InventoryCategoryEntity`, `InventoryMovementEntity`, `MaintenanceRecordEntity` — entidades de inventário
- [x] `ConfigSnapshotEntity` — snapshots de configuração versionados
- [x] `AppSettingEntity` — configurações genéricas chave-valor

### Infraestrutura de dados
- [x] `Repository<T>` — padrão repository genérico com operações CRUD e FindAsync
- [x] `ConfigurationService` — CRUD de apps, sites, monitores, perfis e ciclos via EF Core SQLite
- [x] `DatabaseBackupService` — backup e restore de banco SQLite com checkpoint WAL
- [x] `DataRetentionService` — purgação de logs e movimentações por política de retenção
- [x] `DataRetentionForm` — UI de configuração de retenção de dados

---

## 9. Monitoramento de Hardware e Serviços do Sistema

- [x] `MonitorService` — enumeração e detecção de monitores físicos
- [x] `MonitorCoordinateMapper` — mapeamento de coordenadas entre zonas percentuais e pixels absolutos
- [x] `DisplayService` / `DisplayUtils` — utilitários de display (DPI, bounds, área de trabalho)
- [x] `GdiMonitorEnumerator` — enumeração de monitores via Win32 EnumDisplayMonitors
- [x] `ZonePresetService` — presets de zona (full, half-left/right, quadrants, etc.)
- [x] `PerformanceMetricsService` — análise de frames, drift de input e drift de ciclo
- [x] `NetworkAvailabilityService` — verificação de disponibilidade de rede
- [x] `SessionChecker` — detecção de sessão remota (RDP) e estado do DWM
- [x] `StatusDashboardForm` — painel de status em tempo real de apps e sites monitorados
- [x] `DiagnosticsService` — coleta de diagnósticos do sistema

---

## 10. Segurança Adicional

- [x] `CredentialVault` — armazenamento DPAPI de segredos com versionamento e migração automática
- [x] `CookieSafeStore` — armazenamento seguro de cookies por host com TTL
- [x] `SecretsProvider` — abstração unificada de credenciais (username, password, TOTP)
- [x] `IntegrityService` — validação de integridade de arquivos via SHA-256
- [x] `InputSanitizer` — sanitização de inputs (CSS selectors, hosts, paths) com proteção contra traversal e injeção
- [x] `Redaction` — redação automática de dados sensíveis em logs
- [x] `UrlAllowlist` — lista de hosts permitidos com auditoria de bloqueios
- [x] `SandboxArgsBuilder` — construção de argumentos de sandbox para o browser
- [x] `SecurityPolicy` + perfis (Relaxed, Standard, Strict) com overrides por site
- [x] `SecuritySettingsForm` — UI de configuração de política de segurança, allowlist e cookies
- [x] `ExceptionShield` — captura e log de exceções não tratadas com mini dump
- [x] `DriverVersionGuard` — validação de compatibilidade de versão do driver de browser

---

## 11. Atualização Automática

- [x] `UpdaterService` — verificação periódica de atualizações via manifest HTTP, download e instalação
- [x] Suporte a hash SHA-256 de artefatos de atualização
- [x] Configuração de intervalo mínimo entre verificações (UpdateConfig)

---

## 12. QA e Testes Automatizados

### Testes de segurança
- [x] `SecurityTests` — CredentialVault (roundtrip, migração, corrupção), SecretsProvider, Redaction, UrlAllowlist, IntegrityService, DriverVersionGuard
- [x] `InputSanitizerTests` — SanitizeSelector, SanitizeHost, SanitizePath (XSS, path traversal, caracteres inválidos)
- [x] `UserManagementTests` — CreateUser, UpdateUser, DeleteUser, ResetPassword, Authenticate (senha errada, usuário inativo, desconhecido), Logout
- [x] `AccessControlTests` — permissões de Admin, Operator, Viewer, grant/revoke explícito, usuário inativo, GetUserPermissions, nível inválido

### Testes funcionais
- [x] `ConfigValidatorTests` — validação de configuração
- [x] `ConfigSamplesTests` — parse de exemplos de configuração
- [x] `BrowserArgumentBuilderTests` — construção de argumentos de browser
- [x] `BrowserAndSecurityTests` — testes integrados de browser e segurança
- [x] `TabManagerTests` — gerenciamento de abas
- [x] `MonitorsTests` — enumeração e detecção de monitores
- [x] `DisplayUtilsTests` — utilitários de display
- [x] `JsonStoreTests` — leitura/escrita de JSON com backup
- [x] `WindowPlacementHelperTests` — posicionamento de janelas
- [x] `PerformanceMetricsServiceTests` — análise de métricas de performance
- [x] `AppsProviderTests` — provider de aplicativos instalados
- [x] `RegexSmokeTest` — smoke tests de expressões regulares
- [x] `ScenarioScriptsTests` — scripts de cenário de uso

### Testes de estresse e integração
- [x] `InventoryStressTests` — testes de carga no inventário
- [x] `ImportExportDeadlockTests` — testes de deadlock em import/export
- [x] `RemoteSqlServerTests` — testes de integração com SQL Server remoto

---

## 13. Documentação

- [x] `README.md` — documentação geral do projeto com logs, configuração e uso
- [x] `CHANGELOG.md` — histórico de versões com formato Keep a Changelog
- [x] `docs/SECURITY_SYSTEM_DESIGN.md` — design completo do sistema de segurança (schema SQL, exemplos de código, UI templates)
- [x] `docs/SECURITY_IMPLEMENTATION_PROGRESS.md` — progresso de implementação da segurança por fase
- [x] `docs/IMPLEMENTATION_STATUS.md` — status de funcionalidades implementadas vs faltando
- [x] `docs/DASHBOARD_AUTOMATION_IMPROVEMENTS.md` — melhorias planejadas para automação de dashboards
- [x] `docs/PERFORMANCE_OPTIMIZATION.md` — otimizações de performance documentadas
- [x] `docs/QUICK_PERFORMANCE_WINS.md` — melhorias rápidas de performance
- [x] `docs/RESUMO_PERFORMANCE.md` — resumo de performance
- [x] `docs/RESUMO_VALIDACAO.md` — resumo de validação
- [x] `docs/VALIDATION_REPORT.md` — relatório de validação
- [x] `docs/CODE_AUDIT.md` — auditoria de código
- [x] `docs/Troubleshooting.md` — guia de resolução de problemas

---

## 14. Deploy e Release

- [x] Release inicial `v1.5.3` — single-file publish Windows x64 self-contained (.NET 9)
- [x] Release `v1.6.0` — correções críticas (CSV Excel, caminho de banco, logs, versão hardcoded)
- [x] Preview Host embutido no App como recurso (distribui único executável)
- [x] Correção de caminhos para `%LOCALAPPDATA%\Mieruka\` (banco, logs, segredos)
- [x] Serilog configurado com rolagem diária, retenção 7 dias, JSON estruturado + texto
- [x] Mini dump automático em crash (`MiniDumpWriteDump`)
- [x] Versão lida automaticamente do assembly (sem hardcode)
