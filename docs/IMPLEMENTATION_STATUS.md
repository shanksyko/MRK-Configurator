# Status de Implementação - Automação de Dashboards

**Data**: 2024-12-29  
**Status**: Funcionalidades existentes validadas, próximas implementações documentadas

---

## ✅ Funcionalidades JÁ IMPLEMENTADAS

### 1. **Auto-Refresh Parcial** ⚠️
**Status**: PARCIALMENTE implementado

**O que existe**:
```csharp
// src/Mieruka.Core/Models/SiteConfig.cs - linha 58
public int? ReloadIntervalSeconds { get; init; }
```

**Problema**: Propriedade existe mas **não está sendo usada** no código.

**Onde deveria ser usado**: `src/Mieruka.App/Services/WatchdogService.cs` no método `MonitorSiteAsync`

**Impacto**: Usuário pode configurar mas não funciona.

---

### 2. **Reload On Activate** ✅
**Status**: FUNCIONA

```csharp
// src/Mieruka.Core/Models/SiteConfig.cs - linha 53
public bool ReloadOnActivate { get; init; }
```

**Como funciona**: Quando site se torna ativo no ciclo, recarrega a página.

**Limitação**: Só recarrega ao trocar de item no ciclo, não periodicamente.

**Workaround atual**: Criar ciclo com 1 item de duração = intervalo desejado (ex: 3 minutos).

---

### 3. **Cycle Management** ✅
**Status**: FUNCIONA PERFEITAMENTE

**Componente**: `src/Mieruka.App/Services/CycleManager.cs`

**Funcionalidades**:
- ✅ Rotação automática de conteúdo
- ✅ Duração configurável por item
- ✅ Shuffle opcional
- ✅ Hotkeys (Play/Pause/Next/Previous)

**Configuração**:
```json
{
  "cycle": {
    "enabled": true,
    "defaultDurationSeconds": 180,
    "shuffle": false,
    "items": [
      { "kind": "site", "targetId": "zabbix", "durationSeconds": 180 },
      { "kind": "site", "targetId": "nagios", "durationSeconds": 180 }
    ]
  }
}
```

---

### 4. **Browser Automation (Selenium)** ✅
**Status**: FUNCIONA

**Componente**: `src/Mieruka.Automation/Login/LoginService.cs`

**Funcionalidades**:
- ✅ Auto-login em dashboards
- ✅ Selenium WebDriver (Chrome/Edge)
- ✅ Seletores CSS para username/password
- ✅ Submit automático
- ✅ Wait for navigation

**Configuração**:
```json
{
  "sites": [
    {
      "id": "zabbix",
      "url": "http://192.168.1.100/zabbix",
      "browser": "Chrome",
      "login": {
        "usernameSelector": "#name",
        "passwordSelector": "#password",
        "submitSelector": "#enter"
      }
    }
  ]
}
```

**Credenciais**: Armazenadas com DPAPI em `%LOCALAPPDATA%\Mieruka\secrets\`

---

### 5. **Watchdog Service** ✅
**Status**: FUNCIONA

**Componente**: `src/Mieruka.App/Services/WatchdogService.cs`

**Funcionalidades**:
- ✅ Monitora processos a cada 5 segundos
- ✅ Detecta crashes e reinicia automaticamente
- ✅ Health checks HTTP
- ✅ Health checks DOM (busca texto/seletor)
- ✅ Backoff exponencial em falhas
- ✅ Binding automático de janelas

**Configuração**:
```json
{
  "sites": [
    {
      "id": "dashboard",
      "url": "http://exemplo.com/dashboard",
      "watchdog": {
        "enabled": true,
        "healthCheck": {
          "type": "HTTP",
          "timeoutSeconds": 10
        }
      }
    }
  ]
}
```

---

### 6. **Window Management** ✅
**Status**: FUNCIONA

**Componente**: `src/Mieruka.App/Services/WindowPlacementHelper.cs`

**Funcionalidades**:
- ✅ Posiciona janelas em monitores específicos
- ✅ Suporta múltiplas zonas por monitor
- ✅ Always-on-top configurável
- ✅ Detecção automática de monitores
- ✅ Suporta múltiplos monitores

---

### 7. **Profile Executor** ✅
**Status**: FUNCIONA

**Componente**: `src/Mieruka.Automation/Execution/ProfileExecutor.cs`

**Funcionalidades**:
- ✅ Executa sequências de apps
- ✅ Wait for window handle
- ✅ Posicionamento automático
- ✅ Network availability check
- ✅ Events (AppStarted, AppPositioned, Failed)

---

## ❌ Funcionalidades FALTANDO

### 1. **Auto-Refresh Periódico Funcional** 🔴
**Prioridade**: ALTA

**Problema**: `ReloadIntervalSeconds` existe mas não é usado.

**Solução necessária**:
```csharp
// src/Mieruka.App/Services/WatchdogService.cs
// Adicionar em SiteWatchContext:
public DateTimeOffset LastReload { get; set; }

// Adicionar em MonitorSiteAsync após linha 216:
if (context.Config.ReloadIntervalSeconds.HasValue && 
    context.Config.ReloadIntervalSeconds.Value > 0)
{
    var interval = TimeSpan.FromSeconds(context.Config.ReloadIntervalSeconds.Value);
    if (now - context.LastReload >= interval)
    {
        try
        {
            // Precisa de acesso ao WebDriver
            // Atualmente não é mantido no WatchdogService
            // Requer refatoração arquitetural
            context.LastReload = now;
            _telemetry.Info($"Auto-refreshed site '{context.Config.Id}'");
        }
        catch (Exception ex)
        {
            _telemetry.Warn($"Failed to auto-refresh: {ex.Message}");
        }
    }
}
```

**Bloqueio**: WatchdogService não mantém referência ao WebDriver do Selenium, só ao Process do browser.

**Alternativa mais simples**: Usar SendKeys para F5
```csharp
// Alternativa: SendKeys F5 na janela do browser
[DllImport("user32.dll")]
static extern bool SetForegroundWindow(IntPtr hWnd);

[DllImport("user32.dll")]
static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

const byte VK_F5 = 0x74;
SetForegroundWindow(context.LastHandle);
Thread.Sleep(100);
keybd_event(VK_F5, 0, 0, UIntPtr.Zero); // Key down
keybd_event(VK_F5, 0, 2, UIntPtr.Zero); // Key up
```

**Estimativa**: 4-6 horas de implementação

---

### 2. **Automação de Teclado/Mouse para Apps Nativos** 🔴
**Prioridade**: ALTA

**Caso de uso**: "Abrir app, clicar próximo 4x, reiniciar a cada 3 minutos"

**O que falta**:
- SendKeys para enviar teclas
- Mouse clicks em coordenadas
- Wait for window/control
- FindWindow by title/class

**Solução**: Criar `src/Mieruka.Automation/Native/WindowsAutomation.cs`

**Código exemplo já documentado em**: `docs/DASHBOARD_AUTOMATION_IMPROVEMENTS.md`

**Estimativa**: 2-3 dias de implementação e testes

---

### 3. **Action Sequences** 🔴
**Prioridade**: ALTA

**O que falta**: Sistema para definir sequências de ações em JSON

```json
{
  "id": "app1",
  "executablePath": "C:\\MeuApp.exe",
  "onActivate": {
    "actions": [
      { "type": "wait", "delayMs": 1000 },
      { "type": "click", "x": 100, "y": 200, "delayMs": 500 },
      { "type": "keys", "keys": "{Enter}", "delayMs": 0 },
      { "type": "keys", "keys": "{Tab}{Tab}{Enter}", "delayMs": 500 }
    ]
  }
}
```

**Requer**:
1. Novos modelos: `ActionSequence`, `AutomationAction`, `ClickAction`, `KeysAction`
2. Executor de ações
3. Integração com CycleManager

**Estimativa**: 1 semana de implementação

---

### 4. **Visual Error Detection** 🟡
**Prioridade**: MÉDIA

**O que falta**: OCR ou pattern matching para detectar erros visuais

**Casos**: 
- "Error 500" na tela
- "Connection timeout"
- Dashboard com gráficos vazios

**Solução**: Integrar Tesseract.NET ou Windows.Media.Ocr

**Estimativa**: 1-2 semanas

---

### 5. **Templates para Dashboards Comuns** 🟡
**Prioridade**: MÉDIA

**O que falta**: Configs pré-feitas para Zabbix, Nagios, SolarWinds, etc.

**Localização sugerida**: `config/templates/`

**Estimativa**: 2-3 dias (requer testar com cada dashboard)

---

## 📋 Configuração Atual Funcional

### Exemplo Completo: Dashboard Zabbix com Ciclo

```json
{
  "general": {
    "applications": [],
    "sites": [
      {
        "id": "zabbix",
        "url": "http://192.168.1.100/zabbix",
        "browser": "Chrome",
        "appMode": false,
        "kioskMode": false,
        "reloadOnActivate": true,
        "reloadIntervalSeconds": 180,
        "login": {
          "usernameSelector": "#name",
          "passwordSelector": "#password",
          "submitSelector": "#enter"
        },
        "watchdog": {
          "enabled": true,
          "healthCheck": {
            "type": "HTTP",
            "timeoutSeconds": 10
          }
        },
        "window": {
          "alwaysOnTop": false,
          "zone": { "x": 0, "y": 0, "width": 1, "height": 1 }
        },
        "targetMonitorStableId": "MONITOR1"
      }
    ],
    "cycle": {
      "enabled": true,
      "defaultDurationSeconds": 180,
      "shuffle": false,
      "items": [
        { "kind": "site", "targetId": "zabbix", "durationSeconds": 180 }
      ],
      "hotkeys": {
        "playPause": "Ctrl+Shift+P",
        "next": "Ctrl+Shift+N",
        "previous": "Ctrl+Shift+B"
      }
    }
  }
}
```

**Como funciona atualmente**:
1. ✅ Abre Zabbix no Chrome
2. ✅ Faz login automaticamente
3. ✅ Posiciona janela no monitor correto
4. ✅ A cada 3 minutos (duração do ciclo), recarrega página (via ReloadOnActivate)
5. ✅ Se travar, watchdog reinicia automaticamente

**Limitação**: `reloadIntervalSeconds: 180` está configurado mas não funciona. Use ciclo como workaround.

---

## 🔧 Como Usar Hoje

### Cenário 1: Dashboard único com refresh a cada 3 minutos

**Solução**: Ciclo de 1 item com `reloadOnActivate: true`

```json
{
  "cycle": {
    "enabled": true,
    "items": [
      { "kind": "site", "targetId": "zabbix", "durationSeconds": 180 }
    ]
  }
}
```

### Cenário 2: Múltiplos dashboards rodando

**Solução**: Múltiplos sites sem ciclo, cada um em monitor diferente

```json
{
  "sites": [
    { "id": "zabbix", "url": "...", "targetMonitorStableId": "MONITOR1" },
    { "id": "nagios", "url": "...", "targetMonitorStableId": "MONITOR2" },
    { "id": "solarwinds", "url": "...", "targetMonitorStableId": "MONITOR3" }
  ],
  "cycle": { "enabled": false }
}
```

### Cenário 3: Rotação entre dashboards (a cada 5 minutos)

```json
{
  "cycle": {
    "enabled": true,
    "items": [
      { "kind": "site", "targetId": "zabbix", "durationSeconds": 300 },
      { "kind": "site", "targetId": "nagios", "durationSeconds": 300 },
      { "kind": "site", "targetId": "solarwinds", "durationSeconds": 300 }
    ]
  }
}
```

---

## 📈 Próximos Passos

### Implementação Imediata (pode ser feito agora)
1. ✅ Documentar status atual (este arquivo)
2. ✅ Exemplos de configuração funcionais
3. ✅ Workarounds para limitações

### Curto Prazo (1-2 semanas)
1. 🔧 Implementar auto-refresh periódico (usar SendKeys F5)
2. 🔧 Automação básica de teclado (SendKeys)
3. 🔧 Action sequences simples

### Médio Prazo (1 mês)
1. 🔧 Mouse automation
2. 🔧 Templates para dashboards comuns
3. 🔧 UI visual para configuração

### Longo Prazo (2-3 meses)
1. 🔧 Visual error detection (OCR)
2. 🔧 Conditional logic em actions
3. 🔧 Recording de ações

---

## ✅ Conclusão

**Status Atual**: Sistema **FUNCIONAL** para casos básicos

**Funciona para**:
- ✅ Abrir dashboards web com auto-login
- ✅ Rotação entre múltiplos dashboards
- ✅ Refresh via ciclo (workaround)
- ✅ Monitoramento e restart automático
- ✅ Multi-monitor

**NÃO funciona para**:
- ❌ Auto-refresh periódico sem ciclo
- ❌ Clicar em botões de apps nativos
- ❌ Sequências complexas de ações
- ❌ Detecção visual de erros

**Recomendação**: Use o sistema agora com os workarounds documentados. Implemente features faltantes conforme necessidade do negócio.

---

**Criado por**: GitHub Copilot Agent  
**Data**: 2024-12-29  
**Versão**: 1.0
