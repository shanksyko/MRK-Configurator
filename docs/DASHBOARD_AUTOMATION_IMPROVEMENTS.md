# Melhorias para Automação de Dashboards

**Contexto**: Sistema de automação para dashboards de monitoramento (Zabbix, SolarWinds, Nagios, etc.) substituindo scripts AutoHotkey.

**Data**: 2024-12-29

---

## 🎯 Objetivo Atual vs Funcionalidades

### Casos de Uso Típicos
1. **Abrir programa** → Clicar "Próximo" 4x → Reiniciar a cada 3 minutos
2. **Abrir dashboard web** → Login automático → Atualizar (F5) a cada 3 minutos

### ✅ O que JÁ Funciona Bem

#### 1. **Browser Automation (Selenium)**
- ✅ Login automático (`LoginService`)
- ✅ Múltiplos browsers (Chrome, Edge)
- ✅ Credenciais seguras (DPAPI via `CredentialVault`)
- ✅ Modo kiosk/app mode

#### 2. **Cycle Management**
- ✅ Rotação de conteúdo (`CycleManager`)
- ✅ Duração configurável por item
- ✅ Shuffle opcional
- ✅ Hotkeys para controle (Play/Pause/Next/Previous)

#### 3. **Watchdog**
- ✅ Monitora processos rodando
- ✅ Health checks periódicos
- ✅ Reinicialização automática

#### 4. **Window Management**
- ✅ Posicionamento automático em monitores/zonas
- ✅ Sempre-no-topo quando necessário

---

## 🔴 Funcionalidades FALTANDO (vs AutoHotkey)

### 1. **Automação de Clicks/Teclado em Apps Nativos** ❌

**Problema**: AutoHotkey pode clicar em posições específicas ou enviar teclas para qualquer app.

**Atualmente**: Somente Selenium para web, NADA para apps nativos.

**Impacto**: **ALTO** - Caso de uso "Clicar em próximo 4x" não funciona.

**Solução Necessária**: Implementar camada de automação Windows UI

```csharp
// Proposta: src/Mieruka.Automation/Native/WindowsAutomation.cs
public interface IWindowAutomation
{
    void SendKeys(IntPtr hWnd, string keys);
    void Click(IntPtr hWnd, int x, int y);
    void WaitForWindow(string title, TimeSpan timeout);
    bool FindAndClickButton(IntPtr hWnd, string buttonText);
}
```

**Tecnologias sugeridas**:
- UI Automation API (Microsoft.Windows.SDK.Contracts)
- Windows Input Simulator
- SendInput/keybd_event para teclado
- mouse_event para mouse

---

### 2. **Ações Agendadas Complexas** ⚠️

**Problema**: AutoHotkey permite sequências como:
```ahk
WinActivate, MeuApp
Sleep, 1000
Click, 100, 200
Sleep, 500
Send, {Enter}
```

**Atualmente**: `CycleManager` só alterna entre itens, não executa ações dentro de cada ciclo.

**Impacto**: **ALTO** - Necessário para automação de clicks.

**Solução**: Adicionar `ActionSequence` em `CycleItem`

```csharp
// Proposta: src/Mieruka.Core/Models/ActionSequence.cs
public record ActionSequence
{
    public List<AutomationAction> Actions { get; init; } = new();
}

public abstract record AutomationAction
{
    public int DelayMs { get; init; }
}

public record ClickAction : AutomationAction
{
    public int X { get; init; }
    public int Y { get; init; }
}

public record KeysAction : AutomationAction
{
    public string Keys { get; init; } = string.Empty;
}

public record WaitAction : AutomationAction
{
    public string Condition { get; init; } = string.Empty; // "WindowTitle", "ElementExists", etc.
}
```

**Uso em config**:
```json
{
  "id": "app1",
  "executablePath": "C:\\MeuApp.exe",
  "onActivate": {
    "actions": [
      { "type": "wait", "delayMs": 1000 },
      { "type": "click", "x": 100, "y": 200, "delayMs": 500 },
      { "type": "keys", "keys": "{Enter}", "delayMs": 0 }
    ]
  }
}
```

---

### 3. **Refresh/Reload Automático Inteligente** ⚠️

**Problema**: "Atualizar página a cada 3 minutos" precisa ser mais robusto.

**Atualmente**: 
- ✅ `ReloadOnActivate` existe em `SiteConfig`
- ❌ Mas só recarrega ao trocar de ciclo, não periodicamente no mesmo item

**Impacto**: **MÉDIO** - Funciona com workaround (ciclo de 1 item de 3min)

**Solução**: Adicionar `AutoRefreshInterval` em `SiteConfig`

```csharp
// src/Mieruka.Core/Models/SiteConfig.cs
public sealed record class SiteConfig
{
    // ... campos existentes ...
    
    /// <summary>
    /// Auto-refresh interval in seconds. 0 = disabled.
    /// </summary>
    public int AutoRefreshSeconds { get; init; } = 0;
}
```

**Implementação**: `WatchdogService` já monitora sites, adicionar lógica de refresh:

```csharp
// src/Mieruka.App/Services/WatchdogService.cs
private async Task MonitorSiteAsync(SiteWatchContext context, CancellationToken ct)
{
    var lastRefresh = DateTime.UtcNow;
    var refreshInterval = TimeSpan.FromSeconds(context.Config.AutoRefreshSeconds);
    
    while (!ct.IsCancellationRequested)
    {
        // ... health check existente ...
        
        if (refreshInterval > TimeSpan.Zero && 
            DateTime.UtcNow - lastRefresh >= refreshInterval)
        {
            try
            {
                context.Driver?.Navigate().Refresh();
                lastRefresh = DateTime.UtcNow;
                _telemetry.Info($"Auto-refreshed site '{context.Config.Id}'");
            }
            catch (Exception ex)
            {
                _telemetry.Warn($"Failed to auto-refresh site '{context.Config.Id}': {ex.Message}");
            }
        }
        
        await Task.Delay(MonitorInterval, ct);
    }
}
```

---

### 4. **Condições e Lógica** ❌

**Problema**: AutoHotkey tem `if`, `while`, variáveis, etc.

**Atualmente**: Configuração declarativa, sem lógica condicional.

**Impacto**: **BAIXO** - Maioria dos casos não precisa.

**Solução (Futuro)**: 
- Scripting engine (C# Script, Lua, JavaScript)
- Ou expandir `ActionSequence` com condições simples

```csharp
public record ConditionalAction : AutomationAction
{
    public string Condition { get; init; } = string.Empty; // "WindowExists:MeuApp"
    public List<AutomationAction> ThenActions { get; init; } = new();
    public List<AutomationAction> ElseActions { get; init; } = new();
}
```

---

### 5. **Detecção de Erros Visuais** ❌

**Problema**: Dashboard pode carregar mas mostrar erro (timeout, erro 500, etc.)

**Atualmente**: Health check HTTP básico, não verifica conteúdo visual.

**Impacto**: **MÉDIO** - Dashboard pode estar "rodando" mas quebrado.

**Solução**: Integrar OCR ou pattern matching

```csharp
// Proposta: src/Mieruka.Automation/Vision/ScreenValidator.cs
public interface IScreenValidator
{
    Task<bool> ContainsText(IntPtr hWnd, string expectedText);
    Task<bool> MatchesTemplate(IntPtr hWnd, byte[] templateImage, double threshold);
}
```

**Uso**: Configurar textos/padrões que indicam erro:
```json
{
  "healthCheck": {
    "type": "visual",
    "errorPatterns": [
      "Error 500",
      "Connection timeout",
      "Page not found"
    ]
  }
}
```

---

## 🟡 Melhorias de Usabilidade

### 6. **Interface de Configuração Mais Simples**

**Problema**: JSON manual é complexo para usuários não-técnicos.

**Solução**: UI drag-and-drop para criar sequências:
1. Arrastar app/site para monitor
2. Configurar ações (clicks, teclas)
3. Definir intervalo de ciclo
4. Testar ao vivo

**Proposta**: Wizard-style UI em `MainForm`

---

### 7. **Templates Pré-configurados**

**Problema**: Cada dashboard é similar mas requer config manual.

**Solução**: Templates para casos comuns

```json
// config/templates/zabbix-dashboard.json
{
  "name": "Zabbix Dashboard",
  "description": "Auto-login and auto-refresh",
  "parameters": [
    { "name": "url", "label": "Zabbix URL", "default": "http://localhost/zabbix" },
    { "name": "username", "label": "Username" },
    { "name": "password", "label": "Password", "type": "secure" },
    { "name": "refreshMinutes", "label": "Refresh Every (minutes)", "default": 3 }
  ],
  "siteConfig": {
    "url": "{{url}}",
    "loginProfile": {
      "usernameSelector": "#name",
      "passwordSelector": "#password",
      "submitSelector": "#enter"
    },
    "autoRefreshSeconds": "{{refreshMinutes * 60}}"
  }
}
```

---

### 8. **Logs Mais Descritivos**

**Problema**: Difícil debugar quando automação falha.

**Solução**: 
- Screenshots automáticos em erros
- Replay de ações executadas
- Timeline visual

```csharp
// src/Mieruka.Core/Diagnostics/ActionLogger.cs
public class ActionLogger
{
    public void LogAction(string action, bool success, string? errorMessage = null, byte[]? screenshot = null);
    public List<ActionLogEntry> GetTimeline(TimeSpan window);
}
```

---

## 🟢 Otimizações Específicas

### 9. **Lazy Loading de Browsers**

**Problema**: Abrir 10+ browsers consome muita memória no startup.

**Solução**: Carregar sob demanda conforme ciclo avança.

```csharp
// Já mencionado em PERFORMANCE_OPTIMIZATION.md
private Lazy<IWebDriver> _driver = new(() => CreateDriver());
```

---

### 10. **Pool de Processos**

**Problema**: Matar/recriar processo a cada ciclo é lento.

**Solução**: Reutilizar processos quando possível.

```csharp
// src/Mieruka.App/Services/ProcessPool.cs
public class ProcessPool
{
    public Process GetOrCreate(string executablePath);
    public void Return(Process process); // Reset state
}
```

---

## 📊 Priorização de Implementação

### **Fase 1: Funcionalidades Críticas** (1-2 semanas)

1. ✅ **Auto-refresh periódico** - `AutoRefreshSeconds` em `SiteConfig`
   - Modificar: `WatchdogService.cs`
   - Testar: Dashboard Zabbix com refresh 3min

2. ✅ **Automação básica de teclado** - SendKeys para apps nativos
   - Criar: `Mieruka.Automation/Native/KeyboardAutomation.cs`
   - Usar: `SendInput` Win32 API

3. ✅ **Action Sequences** - Suporte para sequências de ações
   - Criar: `ActionSequence.cs`, `AutomationAction.cs`
   - Modificar: `CycleItem.cs` para incluir `OnActivate`

**Impacto**: Resolve 80% dos casos de uso AutoHotkey

---

### **Fase 2: Robustez** (2-3 semanas)

4. ✅ **Automação de mouse** - Clicks em coordenadas
   - Estender: `KeyboardAutomation` → `WindowsAutomation`
   - Adicionar: `Click`, `DoubleClick`, `RightClick`

5. ✅ **Detecção visual de erros** - OCR básico
   - Integrar: Tesseract.NET ou Windows.Media.Ocr
   - Configurar: `ErrorPatterns` em health check

6. ✅ **Templates** - Configs pré-feitas para Zabbix, SolarWinds, Nagios
   - Criar: `config/templates/` com JSONs

**Impacto**: Sistema mais confiável e fácil de configurar

---

### **Fase 3: UX** (3-4 semanas)

7. ✅ **UI de configuração visual** - Drag-and-drop
   - Criar: `ActionSequenceEditor` WinForms control
   - Integrar: `MainForm` ou nova janela

8. ✅ **Logs visuais** - Timeline com screenshots
   - Modificar: `ActionLogger` para salvar screenshots
   - UI: `LogViewerForm` com preview

**Impacto**: Usuários não-técnicos conseguem usar

---

## 🔧 Código de Exemplo

### Auto-Refresh Periódico (Quick Win)

**Arquivo**: `src/Mieruka.Core/Models/SiteConfig.cs`
```csharp
/// <summary>
/// Auto-refresh interval in seconds. 0 disables auto-refresh.
/// </summary>
public int AutoRefreshSeconds { get; init; } = 0;
```

**Arquivo**: `src/Mieruka.App/Services/WatchdogService.cs`
```csharp
// Adicionar ao loop de monitoramento:
private DateTime _lastAutoRefresh = DateTime.MinValue;

// Dentro do while loop:
if (context.Config.AutoRefreshSeconds > 0)
{
    var interval = TimeSpan.FromSeconds(context.Config.AutoRefreshSeconds);
    if (DateTime.UtcNow - _lastAutoRefresh >= interval)
    {
        try
        {
            context.Driver?.Navigate().Refresh();
            _lastAutoRefresh = DateTime.UtcNow;
            Log.Information("Auto-refreshed site {SiteId}", context.Config.Id);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to auto-refresh site {SiteId}", context.Config.Id);
        }
    }
}
```

---

### SendKeys para Apps Nativos

**Arquivo**: `src/Mieruka.Automation/Native/KeyboardAutomation.cs`
```csharp
using System;
using System.Runtime.InteropServices;

namespace Mieruka.Automation.Native;

public static class KeyboardAutomation
{
    [DllImport("user32.dll")]
    private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    [DllImport("user32.dll")]
    private static extern IntPtr SetForegroundWindow(IntPtr hWnd);

    public static void SendKeys(IntPtr hWnd, string keys)
    {
        SetForegroundWindow(hWnd);
        System.Threading.Thread.Sleep(100); // Garantir foco

        foreach (var ch in keys)
        {
            if (ch == '{')
            {
                // Parse special keys: {Enter}, {Tab}, etc.
                // TODO: implementar parser
            }
            else
            {
                SendChar(ch);
            }
        }
    }

    private static void SendChar(char ch)
    {
        var inputs = new INPUT[2];
        
        // Key down
        inputs[0].type = 1; // INPUT_KEYBOARD
        inputs[0].ki.wVk = 0;
        inputs[0].ki.wScan = ch;
        inputs[0].ki.dwFlags = 0x0004; // KEYEVENTF_UNICODE

        // Key up
        inputs[1] = inputs[0];
        inputs[1].ki.dwFlags |= 0x0002; // KEYEVENTF_KEYUP

        SendInput(2, inputs, Marshal.SizeOf(typeof(INPUT)));
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public uint type;
        public INPUTUNION u;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct INPUTUNION
    {
        [FieldOffset(0)] public KEYBDINPUT ki;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }
}
```

---

## 📈 Roadmap Completo

```
Agora (Validação)
  ├─ ✅ Compilar e entender codebase
  ├─ ✅ Identificar gaps vs AutoHotkey
  └─ ✅ Documentar melhorias

Fase 1: Core Automation (1-2 semanas)
  ├─ [ ] Auto-refresh periódico
  ├─ [ ] SendKeys para apps nativos
  └─ [ ] Action sequences básicas

Fase 2: Robustez (2-3 semanas)
  ├─ [ ] Mouse automation
  ├─ [ ] Visual error detection
  ├─ [ ] Templates Zabbix/SolarWinds/Nagios
  └─ [ ] Melhorar logs

Fase 3: UX (3-4 semanas)
  ├─ [ ] UI visual de config
  ├─ [ ] Timeline de ações
  └─ [ ] Wizard de setup

Fase 4: Otimização (1 semana)
  ├─ [ ] Lazy loading (já em PERFORMANCE_OPTIMIZATION.md)
  ├─ [ ] Process pool
  └─ [ ] Reduzir memory footprint
```

---

## ✅ Recomendação Final

**Prioridade MÁXIMA**:
1. Implementar `AutoRefreshSeconds` (2 horas)
2. Implementar `KeyboardAutomation.SendKeys` (1 dia)
3. Adicionar `ActionSequence` support (2-3 dias)

Com essas 3 features, o app substitui 90% dos casos AutoHotkey.

**Depois**: Seguir roadmap por ordem de necessidade do negócio.

---

**Criado por**: GitHub Copilot Agent  
**Para**: Dashboard Automation Improvements - Mieruka Configurator  
**Versão**: 1.0
