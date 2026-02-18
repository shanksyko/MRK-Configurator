# Auditoria de Código — MRK-Configurator

**Data**: 2025  
**Escopo**: `src/Mieruka.App/Config/`, `Services/`, `Tray/`, `Forms/`, `src/Mieruka.Automation/`  
**Categorias**: Thread safety, Performance, Error handling, Dispose/Resource management, Dead code, CancellationToken support, WinForms anti-patterns, Selenium/WebDriver leaks, Race conditions

---

## Resumo de Severidade

| Severidade | Quantidade |
|------------|-----------|
| CRITICAL   | 4         |
| HIGH       | 14        |
| MEDIUM     | 18        |
| LOW        | 12        |

---

## CRITICAL

### 1. WatchdogService.StopInternal — Bloqueio síncrono com risco de deadlock
**Arquivo**: `src/Mieruka.App/Services/WatchdogService.cs` (linhas ~830–850)  
**Categoria**: Thread safety / Deadlock  
**Descrição**: `StopInternal()` chama `monitor!.GetAwaiter().GetResult()` para aguardar Tasks de monitoramento. Se uma dessas Tasks estiver tentando postar no thread de UI (via `BeginInvoke`) que está bloqueado aqui, ocorre deadlock clássico.  
**Recomendação**: Substituir por `await` propagando assincronicidade, ou usar `Task.WhenAny(monitor, Task.Delay(timeout))` seguido de fallback de cancelamento forçado.

### 2. DiagnosticsService.Dispose — Bloqueio síncrono idêntico
**Arquivo**: `src/Mieruka.App/Services/DiagnosticsService.cs` (linhas ~310–320)  
**Categoria**: Thread safety / Deadlock  
**Descrição**: `Dispose()` chama `StopAsync().GetAwaiter().GetResult()`. Se chamado a partir do UI thread durante shutdown do form, e `StopAsync` tentar acessar `SynchronizationContext`, ocorre deadlock.  
**Recomendação**: Implementar `IAsyncDisposable` e usar `await DisposeAsync()`, ou usar `StopAsync().ConfigureAwait(false).GetAwaiter().GetResult()` como mitigação mínima.

### 3. SiteTestService.ApplyWhitelist — Task fire-and-forget com CTS vazado
**Arquivo**: `src/Mieruka.App/Services/Testing/SiteTestService.cs` (linhas ~200–220)  
**Categoria**: Resource leak / CancellationToken  
**Descrição**: `ApplyWhitelist` cria um `CancellationTokenSource` e dispara `Task.Run` com `TabManager.MonitorAsync`, mas **nunca** dispõe o CTS e a Task é fire-and-forget. Múltiplas chamadas em sequência vazam múltiplos CTS e Tasks de monitoramento acumulam sem limpeza.  
**Recomendação**: Armazenar o CTS e a Task como campos da classe; cancelar e aguardar a Task anterior antes de criar uma nova; dispor o CTS no `Dispose` ou em `finally`.

### 4. MainForm.DisposeMonitorCards — GetAwaiter().GetResult() no UI thread
**Arquivo**: `src/Mieruka.App/Forms/MainForm.cs` (linhas ~1200–1210)  
**Categoria**: Deadlock  
**Descrição**: `DisposeMonitorCards()` chama `context.CloseTestWindowAsync().GetAwaiter().GetResult()` dentro de um loop. Essa chamada síncrona no UI thread pode fazer deadlock se a Task internamente fizer `BeginInvoke` ou postar no `SynchronizationContext` do UI.  
**Recomendação**: Transformar `DisposeMonitorCards` em `async Task` ou usar `ContinueWith(TaskScheduler.Default)` para evitar bloqueio de sincronização.

---

## HIGH

### 5. HotkeyManager.HotkeySink — GetAwaiter().GetResult() cruzando threads
**Arquivo**: `src/Mieruka.App/Services/HotkeyManager.cs` (linhas ~350–400)  
**Categoria**: Thread safety / Deadlock  
**Descrição**: `Register()` e `Unregister()` usam `tcs.Task.GetAwaiter().GetResult()` que bloqueia o thread chamador enquanto posta mensagem via `PostMessage` ao thread STA do `HotkeySink`. Se chamado do UI thread, e o STA thread precisar sincronizar com o UI thread, deadlock.  
**Recomendação**: Expor métodos `RegisterAsync` / `UnregisterAsync` com `await tcs.Task` e converter os chamadores para async.

### 6. ConfiguratorWorkspace._monitors — Acesso não thread-safe
**Arquivo**: `src/Mieruka.App/Config/ConfiguratorWorkspace.cs`  
**Categoria**: Thread safety  
**Descrição**: `_monitors` (`List<MonitorInfo>`) é lido/escrito sem sincronização. `UpdateMonitors()` pode ser chamado de eventos de topologia (background thread) enquanto o UI thread lê a lista.  
**Recomendação**: Usar `lock` ao redor de acessos a `_monitors`, ou trocar por `ImmutableList<MonitorInfo>` com swap atômico via `Interlocked.Exchange`.

### 7. JsonStore.AcquireLockAsync — Spin infinito sem timeout
**Arquivo**: `src/Mieruka.App/Config/JsonStore.cs` (linhas ~80–120)  
**Categoria**: Error handling / Livelock  
**Descrição**: O loop de aquisição de lock via arquivo `.lock` não tem limite de tentativas nem timeout total. Se o lock file ficar órfão (crash durante hold), o método gira indefinidamente.  
**Recomendação**: Adicionar `maxRetries` ou `TimeSpan timeout`; após expirar o prazo, deletar o lock file órfão (verificando a idade do arquivo) e re-tentar uma vez.

### 8. UpdaterService — Volatile.Read/Write em tipo referência Version
**Arquivo**: `src/Mieruka.App/Services/UpdaterService.cs`  
**Categoria**: Thread safety  
**Descrição**: `Volatile.Read(ref _currentVersion)` e `Volatile.Write(ref _currentVersion, …)` são usados num campo `Version` (tipo referência). Para referências, basta a atomicidade natural do CLR; o problema real é que o padrão read-then-write não é atômico e não substitui um lock quando há escrita concorrente.  
**Recomendação**: Usar `Interlocked.Exchange` para escrita e `Interlocked.CompareExchange` para leitura atômica, ou simplificar com `lock`.

### 9. BrowserLauncher — DriverService não disposto em caso de falha
**Arquivo**: `src/Mieruka.Automation/Drivers/BrowserLauncher.cs` (linhas ~80–120)  
**Categoria**: Resource leak  
**Descrição**: `ChromeDriverService` / `EdgeDriverService` são criados imediatamente antes da criação do driver. Se `new ChromeDriver(service, options)` lançar exceção, o `service` nunca é disposto, vazando o processo `chromedriver.exe`.  
**Recomendação**: Encapsular `service` em bloco `using` e reatribuir a `null` após sucesso, ou usar try/catch para `service.Dispose()` no bloco de erro.

### 10. BindingTrayService.OnTopologyChanged — CTS vazado em debounce repetido
**Arquivo**: `src/Mieruka.App/Services/BindingTrayService.cs` (linhas ~350–400)  
**Categoria**: Resource leak / Race condition  
**Descrição**: A cada mudança de topologia, `Task.Run` é disparado com `Task.Delay` para debounce. O CTS anterior (`_topologyDebounceCts`) é cancelado mas o resultado de `Task.Run` nunca é aguardado, e se a topologia mudar rapidamente, múltiplas Tasks podem acumular.  
**Recomendação**: Aguardar (ou cancelar e descartar) a Task anterior antes de iniciar nova, e garantir que o CTS é disposto via `finally`.

### 11. CycleManager — Timer callback acessando Win32 APIs de thread pool
**Arquivo**: `src/Mieruka.App/Services/CycleManager.cs` (linhas ~300–400)  
**Categoria**: Thread safety / WinForms anti-pattern  
**Descrição**: `OnTimerTick` (callback de `System.Threading.Timer`) chama `TryActivateWithFallback` que invoca `SetForegroundWindow`, `MoveWindow`, e métodos de `_bindingService` — tudo a partir de um thread pool thread. Chamadas Win32 de janela devem ocorrer do thread proprietário da janela.  
**Recomendação**: Usar `Control.BeginInvoke` ou `SynchronizationContext.Post` para marshalling ao UI thread antes de invocar Win32 APIs de janela.

### 12. AppEditorForm — 4636 linhas, God Class
**Arquivo**: `src/Mieruka.App/Forms/AppEditorForm.cs`  
**Categoria**: Manutenibilidade / Code smell  
**Descrição**: A classe tem 4636 linhas, ~30 campos de estado mutable, múltiplos `bool _suppress*` flags de reentrância. Isso é uma violação grave de SRP e torna o comportamento difícil de raciocinar.  
**Recomendação**: Extrair lógica em classes menores: `MonitorPreviewController`, `CycleSimulationCoordinator`, `WindowBoundsEditor`, `ProfileItemsManager`.

### 13. MainForm.StopAutomaticPreviews — Fire-and-forget sem observação
**Arquivo**: `src/Mieruka.App/Forms/MainForm.cs` (linhas ~1170–1180)  
**Categoria**: Error handling  
**Descrição**: `_ = PausePreviewsAsync()` descarta a Task. Embora haja um `ContinueWith(OnlyOnFaulted)` para log, exceções de `AggregateException` com múltiplas falhas internas podem ser parcialmente perdidas.  
**Recomendação**: Usar `ContinueWith` que processa `t.Exception.InnerExceptions` (plural) para logar todas as falhas internas.

### 14. ConfigValidator — Scan de PATH completo a cada validação
**Arquivo**: `src/Mieruka.App/Config/ConfigValidator.cs`  
**Categoria**: Performance  
**Descrição**: A validação de drivers Selenium percorre todo o `PATH` do sistema a cada chamada de validação. Em ambientes com muitos diretórios no PATH, isso pode levar segundos.  
**Recomendação**: Cachear o resultado por alguns minutos ou até que a configuração relevante mude.

### 15. MainForm.BackCompat.CloseTestWindow — Fire-and-forget sem await
**Arquivo**: `src/Mieruka.App/Forms/MainForm.BackCompat.cs` (linhas ~20–35)  
**Categoria**: Race condition  
**Descrição**: `_ = ResumeHostFromTestWindowAsync()` é chamado como fire-and-forget. Se `SetTestWindowAsync` for chamado imediatamente após, a retomada pendente pode conflitar com uma nova pausa.  
**Recomendação**: Armazenar e aguardar a Task retornada, ou usar um `SemaphoreSlim` para serializar operações de pause/resume.

### 16. LoginAutoTab.btnDetectarCampos_Click — ConfigureAwait(false) em handler de UI
**Arquivo**: `src/Mieruka.App/Forms/Controls/Sites/LoginAutoTab.cs` (linhas ~80–85)  
**Categoria**: WinForms anti-pattern  
**Descrição**: `await DetectarCamposAsync().ConfigureAwait(false)` devolve a continuação a um thread pool thread. Porém, `DetectarCamposAsync()` acessa `btnDetectarCampos.Enabled` e `MessageBox.Show(this, ...)` — essas chamadas de UI não devem ocorrer fora do UI thread.  
**Recomendação**: Usar `.ConfigureAwait(true)` (ou omitir, que é o padrão) para manter a continuação no UI thread.

### 17. MainForm — multiple `async void` event handlers
**Arquivo**: `src/Mieruka.App/Forms/MainForm.cs` (diversas linhas)  
**Categoria**: Error handling  
**Descrição**: `MainForm_Shown`, `MainForm_Resize`, `OnMonitorPreviewClicked`, `OnMonitorCardStopRequested`, `OnMonitorCardTestRequested`, `btnExecutar_Click`, `btnParar_Click`, etc. são todos `async void`. Exceções não capturadas em `async void` crasham o processo.  
**Recomendação**: Encapsular o corpo dos handlers `async void` em try/catch abrangente, ou usar um padrão como `async void Handler(s,e) { try { await DoWork(); } catch (Exception ex) { Log(ex); } }`.

### 18. TrayMenuManager — `_operationInProgress` bool não volatile
**Arquivo**: `src/Mieruka.App/Tray/TrayMenuManager.cs`  
**Categoria**: Thread safety  
**Descrição**: `_operationInProgress` é lido e escrito sem barreira de memória. Em cenários de multi-threading (ex: `StateChanged` event marshalled), o valor pode não ser visível entre threads.  
**Recomendação**: Declarar como `volatile` ou trocar por `Interlocked` access pattern.

---

## MEDIUM

### 19. ConfigForm — Campo `_testGate` nunca utilizado
**Arquivo**: `src/Mieruka.App/ConfigForm.cs` (linha ~90)  
**Categoria**: Dead code  
**Descrição**: `private readonly object _testGate = new();` é declarado mas jamais usado em `lock(_testGate)` ou qualquer outra construção.  
**Recomendação**: Remover o campo ou implementar a sincronização pretendida.

### 20. SiteTestService.CheckConnectivityAsync — HttpClient por chamada
**Arquivo**: `src/Mieruka.App/Services/Testing/SiteTestService.cs`  
**Categoria**: Performance / Resource leak  
**Descrição**: Um novo `HttpClient` é criado a cada chamada de `CheckConnectivityAsync`, o que causa socket exhaustion em cenários de muitas validações.  
**Recomendação**: Injetar ou reutilizar uma instância `static readonly HttpClient` ou usar `IHttpClientFactory`.

### 21. ConfigForm — StringFormat sem disposal em métodos de desenho
**Arquivo**: `src/Mieruka.App/ConfigForm.cs` (vários pontos)  
**Categoria**: Resource leak  
**Descrição**: Objetos `StringFormat` são criados em métodos de pintura sem `using`, vazando handles GDI.  
**Recomendação**: Encapsular em `using var sf = new StringFormat(...)`.

### 22. ConfigForm.OnTopologyChanged — Possível cross-thread
**Arquivo**: `src/Mieruka.App/ConfigForm.cs` (linha ~1450)  
**Categoria**: WinForms anti-pattern  
**Descrição**: O handler de evento `OnTopologyChanged` pode ser disparado de um thread de background (notificação do sistema). Se o corpo acessa controles WinForms sem `InvokeRequired` check, haverá `InvalidOperationException` em Debug ou corrupção silenciosa em Release.  
**Recomendação**: Adicionar `if (InvokeRequired) { BeginInvoke(...); return; }` no início do handler.

### 23. InstalledAppsProvider.ResolveShortcutTarget — COM interop sem `dynamic` safety
**Arquivo**: `src/Mieruka.App/Services/InstalledAppsProvider.cs` (linhas ~370–400)  
**Categoria**: Error handling  
**Descrição**: Usa `dynamic` para invocar `WScript.Shell.CreateShortcut`. Se o COM server não estiver registrado (ex: em containers/sandboxes), `Activator.CreateInstance` retorna `null` e o código já trata isso. Porém, `TryReleaseComObject` pode receber objetos `dynamic` que não são COM — `Marshal.IsComObject` mitiga, mas o padrão é frágil.  
**Recomendação**: Adicionar `catch (COMException)` explícito ao redor da chamada entire para robustez.

### 24. ProfileExecutor._executionCts — Potencial double dispose
**Arquivo**: `src/Mieruka.Automation/Execution/ProfileExecutor.cs`  
**Categoria**: Resource management  
**Descrição**: `_executionCts` é disposto no `finally` do método `Start` e também no `Dispose()` da classe. Se `Dispose` for chamado enquanto `Start` está em andamento, o CTS pode ser disposto duas vezes.  
**Recomendação**: Usar `Interlocked.Exchange(ref _executionCts, null)?.Dispose()` em ambos os pontos para garantir dispose-once.

### 25. CredentialVaultPanel — new CredentialVault() no construtor
**Arquivo**: `src/Mieruka.App/Forms/Controls/CredentialVaultPanel.cs` (linhas ~35–50)  
**Categoria**: Design / Testability  
**Descrição**: O painel cria diretamente `new CredentialVault()` e `new CookieSafeStore()` no construtor sem injeção de dependência, tornando impossível testar unitariamente sem acessar o Windows Credential Manager.  
**Recomendação**: Receber interfaces `ICredentialVault` e `ICookieStore` via construtor ou property injection.

### 26. SecuritySettingsForm._cleanupTimer — Timer sem cleanup em caso de exceção no construtor
**Arquivo**: `src/Mieruka.App/Forms/SecuritySettingsForm.cs` (linhas ~180–185)  
**Categoria**: Resource leak  
**Descrição**: Se uma exceção ocorrer no construtor *após* `_cleanupTimer.Start()` mas *antes* do fim do construtor, o timer continua ativo sem que `Dispose` seja chamado (o objeto nunca é atribuído).  
**Recomendação**: Mover `_cleanupTimer.Start()` para o handler `Load` ou `Shown`, ou envolver o construtor em try/catch que para o timer em caso de falha.

### 27. LoginAutoTab.btnTestarLogin_Click — Operação síncrona bloqueando UI
**Arquivo**: `src/Mieruka.App/Forms/Controls/Sites/LoginAutoTab.cs` (linhas ~140–160)  
**Categoria**: Performance / WinForms anti-pattern  
**Descrição**: `_orchestrator.EnsureLoggedIn(_site)` é chamado sincronamente no handler de click. Isso bloqueia o UI thread durante toda a automação de login Selenium.  
**Recomendação**: Tornar o método assíncrono: `await Task.Run(() => _orchestrator.EnsureLoggedIn(_site))` com desabilitação do botão durante a operação.

### 28. AppEditorForm — Dois loggers estáticos redundantes
**Arquivo**: `src/Mieruka.App/Forms/AppEditorForm.cs` (linhas 37–38)  
**Categoria**: Dead code  
**Descrição**: `static readonly ILogger Logger` e `readonly ILogger _logger` são ambos criados com `Log.ForContext<AppEditorForm>()`. Um deles é redundante.  
**Recomendação**: Remover um dos dois e unificar o uso.

### 29. MainForm.btnExecutar_Click — ConfigureAwait(false) em handler de UI
**Arquivo**: `src/Mieruka.App/Forms/MainForm.cs` (linhas ~1350–1360)  
**Categoria**: WinForms anti-pattern  
**Descrição**: `await ExecutarOrchestratorAsync().ConfigureAwait(false)` e `await PararOrchestratorAsync().ConfigureAwait(false)` em handlers de botão. O corpo de `ExecutarOrchestratorAsync` acessa controles WinForms (UI thread). Se a continuação rodar em thread pool, crash.  
**Recomendação**: Usar `.ConfigureAwait(true)` (ou omitir) nesses handlers.

### 30. LayoutHelpers.TryGetRefreshRate — DEVMODE com dmSize como `short`
**Arquivo**: `src/Mieruka.App/Forms/Controls/LayoutHelpers.cs` (linhas ~240–250)  
**Categoria**: Correctness  
**Descrição**: `DEVMODE.dmSize` é declarado como `short` mas inicializado com `(short)Marshal.SizeOf<DEVMODE>()`. Se o tamanho da struct ultrapassar `short.MaxValue` (32767), ocorre overflow silencioso. Enquanto o tamanho real (~160 bytes) está longe do limite, o tipo correto da API é `WORD` (ushort).  
**Recomendação**: Declarar `dmSize` como `ushort` (como feito em `WindowPlacementHelper`).

### 31. MainForm.ResumeMonitorPreviewsAsync — Task.Run para ResumeCapture
**Arquivo**: `src/Mieruka.App/Forms/MainForm.cs` (linhas ~2290–2300)  
**Categoria**: WinForms anti-pattern  
**Descrição**: `await Task.Run(() => host.ResumeCapture()).ConfigureAwait(true)` executa `ResumeCapture` em thread pool e retorna ao UI thread. Se `ResumeCapture` manipular handles GDI/DWM que dependem de afinidade de thread, pode causar problemas.  
**Recomendação**: Verificar se `ResumeCapture` requer afinidade de thread, e se sim, executá-lo diretamente no UI thread.

### 32. TabEditCoordinator — uso de Reflection para chamar ApplyAppTypeUI
**Arquivo**: `src/Mieruka.App/Services/Ui/TabEditCoordinator.cs` (linhas ~80, ~390)  
**Categoria**: Performance / Manutenibilidade  
**Descrição**: `_applyAppTypeUiMethod = root.GetType().GetMethod("ApplyAppTypeUI", ...)` usa reflection para invocar um método no controle raiz. Isso é frágil (pode quebrar silenciosamente se o método for renomeado) e mais lento.  
**Recomendação**: Usar um delegate ou interface (`IApplyAppTypeUi`) para chamada direta.

### 33. DoubleBufferingHelper — Reflection em SetStyle/DoubleBuffered
**Arquivo**: `src/Mieruka.App/Services/Ui/WindowStyles.cs` (linhas ~70–100)  
**Categoria**: Performance / Fragilidade  
**Descrição**: Usa `typeof(Control).GetMethod("SetStyle", NonPublic)` e `typeof(Control).GetProperty("DoubleBuffered", NonPublic)` via reflection em cada controle. APIs internas podem mudar entre versões do .NET.  
**Recomendação**: Para controles próprios, sobrescrever no construtor diretamente. Para controles de terceiros, documentar o risco de quebra.

### 34. WatchdogService — Health check DOM via IndexOf
**Arquivo**: `src/Mieruka.App/Services/WatchdogService.cs`  
**Categoria**: Correctness / Security  
**Descrição**: A verificação de saúde de sites usa `string.IndexOf` no corpo HTML retornado para validar presença de marcadores. Isso é frágil e pode ser enganado por conteúdo dentro de atributos ou comentários.  
**Recomendação**: Validar via HTTP status code (200) como check primário; para verificação de conteúdo, usar um parser mínimo ou regex com boundaries.

### 35. MainForm — Criação repetida de NetworkAvailabilityService
**Arquivo**: `src/Mieruka.App/Forms/MainForm.cs` (linhas ~1855)  
**Categoria**: Design  
**Descrição**: `CreateProfileExecutor()` cria `new NetworkAvailabilityService()` a cada chamada. Se o serviço subscreve eventos de rede, múltiplas instâncias podem competir.  
**Recomendação**: Criar a instância uma vez e reutilizar.

### 36. CredentialVault P/Invoke — Password bytes não zerados
**Arquivo**: `src/Mieruka.Automation/Login/CredentialVault.cs` (linhas ~220–240)  
**Categoria**: Security  
**Descrição**: `passwordBytes` (contendo a senha em Unicode) é passado ao `Marshal.Copy` mas nunca é zerado após uso. O array fica na memória gerenciada até ser coletado pelo GC, expondo a senha a dumps de memória.  
**Recomendação**: Usar `Array.Clear(passwordBytes, 0, passwordBytes.Length)` no bloco `finally`.

---

## LOW

### 37. ConfigMigrator — Sem validação de schema após migração
**Arquivo**: `src/Mieruka.App/Config/ConfigMigrator.cs`  
**Categoria**: Error handling  
**Descrição**: Após migrar V1→V2, o resultado não passa por validação. Se a migração produzir JSON mal-formado, o erro só aparecerá mais tarde.  
**Recomendação**: Chamar `ConfigValidator.Validate` após migração.

### 38. PreviewGraphicsOptions — Record sem validação de enum
**Arquivo**: `src/Mieruka.App/Config/PreviewGraphicsOptions.cs`  
**Categoria**: Error handling  
**Descrição**: `Mode` pode conter valores inválidos do enum `PreviewGraphicsMode` se deserializado de JSON corrompido.  
**Recomendação**: `Normalize()` já mitiga, mas considerar adicionar `Enum.IsDefined` check.

### 39. AppRunner — Eventos BeforeMoveWindow/AfterMoveWindow
**Arquivo**: `src/Mieruka.App/Services/AppRunner.cs`  
**Categoria**: Design  
**Descrição**: Eventos `BeforeMoveWindow`/`AfterMoveWindow` são disparados sincronamente. Se um subscriber fizer trabalho pesado, a movimentação da janela é atrasada.  
**Recomendação**: Documentar que os handlers devem ser leves, ou disparar assincronamente.

### 40. AppsTab — Lista de apps carregada sincronamente
**Arquivo**: `src/Mieruka.App/Forms/Controls/Apps/AppsTab.cs`  
**Categoria**: Performance  
**Descrição**: A enumeração de aplicativos instalados (registry) pode ser lenta. Se feita no UI thread, causa congelamento perceptível.  
**Recomendação**: Carregar via `Task.Run` com indicador de progresso.

### 41. MonitorTestForm — Font criada sem disposal explícito
**Arquivo**: `src/Mieruka.App/Forms/MonitorTestForm.cs` (linhas ~22–25)  
**Categoria**: Resource leak  
**Descrição**: `boldFont` é criada no construtor e atribuída a `_messageLabel.Font`, mas quando o form é disposto, a font pode não ser liberada (WinForms não garante disposal de Font atribuída a controle).  
**Recomendação**: Sobrescrever `Dispose(bool)` para dispor `boldFont`, ou usar padrão `Disposed +=` como feito em `LayoutHelpers`.

### 42. WindowPlacementHelper.PlaceWindow — Thread.Sleep em loop
**Arquivo**: `src/Mieruka.App/Services/WindowPlacementHelper.cs` (linhas ~600–630)  
**Categoria**: Performance  
**Descrição**: `PlaceWindow` usa `Thread.Sleep(50)` em loop de retry, bloqueando o thread chamador por até `timeout` milissegundos.  
**Recomendação**: Se chamado de thread pool, considerar versão async com `Task.Delay`. Se necessariamente síncrono, documentar que não deve ser chamado do UI thread.

### 43. TabManager — _staleKeys reutilizado mas nunca trimmed
**Arquivo**: `src/Mieruka.Automation/Tabs/TabManager.cs`  
**Categoria**: Performance (menor)  
**Descrição**: `_staleKeys` (`List<string>`) é `Clear()`'d a cada iteração mas nunca perde capacidade. Em cenários com picos de tabs fechadas, a lista interna mantém array grande.  
**Recomendação**: Não urgente; se necessário, criar novo `List` quando `_staleKeys.Capacity > 100` (por exemplo).

### 44. SecuritySettingsForm.ApplyPolicy — Hardcoded policy values
**Arquivo**: `src/Mieruka.App/Forms/SecuritySettingsForm.cs` (linhas ~260–300)  
**Categoria**: Manutenibilidade  
**Descrição**: Os valores de `SecurityPolicyOverrides` para cada perfil de segurança estão hardcoded no form. Mudanças nos perfis de segurança requerem alterar a UI.  
**Recomendação**: Mover para uma classe `SecurityPolicyDefaults` ou arquivo de configuração.

### 45. LoginForm — Password em plaintext na memória do TextBox
**Arquivo**: `src/Mieruka.App/Forms/Security/LoginForm.cs`  
**Categoria**: Security (menor)  
**Descrição**: `txtPassword.Text` retorna a senha como `string` imutável que fica na memória gerenciada até GC. `UseSystemPasswordChar` só obscurece visualmente.  
**Recomendação**: Limitação do WinForms. Documentar. Se necessário, usar `SecureString` com P/Invoke para extrair o texto do `TextBox` handle diretamente.

### 46. ChangePasswordForm — Comparação de senhas via String.Equals
**Arquivo**: `src/Mieruka.App/Forms/Security/ChangePasswordForm.cs` (linha ~130)  
**Categoria**: Security (menor)  
**Descrição**: `string.Equals(newPassword, confirmPassword, StringComparison.Ordinal)` é uma comparação correta para este cenário (não é timing-sensitive pois ambas as senhas vêm do mesmo usuário local).  
**Recomendação**: OK como está. Apenas mencionado para completude.

### 47. MainForm.UiTelemetry — Usa Debug.WriteLine apenas
**Arquivo**: `src/Mieruka.App/Forms/MainForm.cs` (linhas ~2345–2360)  
**Categoria**: Observability  
**Descrição**: A implementação `UiTelemetry` apenas escreve em `Debug.WriteLine`, que é descartado em builds Release. Erros reais serão invisíveis em produção.  
**Recomendação**: Delegar para `Serilog.Log` ou outra implementação que persista em produção.

### 48. AppEditorForm — _simRectsDepth e _simOverlaysDepth como campos static
**Arquivo**: `src/Mieruka.App/Forms/AppEditorForm.cs` (linhas ~140–141)  
**Categoria**: Thread safety (menor)  
**Descrição**: `_simRectsDepth` e `_simOverlaysDepth` são `static int` usados como reentrance guards. Se duas instâncias de `AppEditorForm` existirem simultaneamente, compartilham o guard incorretamente.  
**Recomendação**: Converter para campos de instância.

---

## Resumo de Ações Prioritárias

1. **Eliminar `.GetAwaiter().GetResult()`** em `WatchdogService`, `DiagnosticsService`, `HotkeyManager` e `MainForm.DisposeMonitorCards` — todos são candidatos a deadlock.
2. **Corrigir fire-and-forget em `SiteTestService.ApplyWhitelist`** — vazamento de CTS e Tasks.
3. **Adicionar timeout a `JsonStore.AcquireLockAsync`** — risco de hang infinito.
4. **Garantir cross-thread safety** em `CycleManager` (timer callback), `ConfiguratorWorkspace._monitors`, `BindingTrayService` (topology debounce).
5. **Envolver todos `async void` handlers em try/catch** — especialmente em `MainForm` e `TrayMenuManager`.
6. **Zerar `passwordBytes` após uso** em `CredentialVault` — mitigação de vazamento de credenciais.
7. **Corrigir `.ConfigureAwait(false)` em handlers de UI** — `btnExecutar_Click`, `btnParar_Click`, `btnDetectarCampos_Click`.
