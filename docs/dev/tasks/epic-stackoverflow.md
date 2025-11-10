# Epic: Erradicar StackOverflowException (0xc00000fd)

Este documento rastreia as tarefas derivadas do épico `MIERUKA-EPIC-STACKOVERFLOW-HUNTER`.
Cada tarefa lista seu objetivo, componentes tocados e estado atual com observações
sobre validações relevantes.

## Contexto

Relatos de `System.StackOverflowException` (0xc00000fd) surgiam em cenários de
reentrada do preview, falhas repetidas do backend GPU/WGC e eventos de UI que
reiniciavam capturas em cascata. As correções priorizam a criação de guardas de
estado, gates de reentrância e fallback seguro para GDI.

## Tarefas

### Backlog de alterações consolidadas
- [x] Introduzir máquina de estados, gate de reentrância e escopos de Start/Stop em `MonitorPreviewHost` para evitar recursões e liberar busy scopes com segurança.【F:src/Mieruka.App/Ui/PreviewBindings/MonitorPreviewHost.cs†L24-L662】
- [x] Centralizar fallback GPU/WGC através de `GpuCaptureGuard`, `CaptureFactory` e `WgcMonitorCapture`, com desativação permanente após falhas críticas.【F:src/Mieruka.Core/Config/GpuCaptureGuard.cs†L1-L133】【F:src/Mieruka.Preview/CaptureFactory.cs†L1-L104】【F:src/Mieruka.Preview/WgcMonitorCapture.cs†L1-L200】
- [x] Debounce de eventos de UI (tab, mouse e layout) com `TabEditCoordinator` e supressão de interações durante pausa de preview.【F:src/Mieruka.App/Services/Ui/TabEditCoordinator.cs†L1-L280】【F:src/Mieruka.App/Ui/PreviewBindings/MonitorPreviewDisplay.cs†L1-L160】
- [x] Guardas globais e logging específico para `StackOverflowException`, travando GPU ao detectar a exceção em handlers principais.【F:src/Mieruka.App/Program.cs†L1-L120】
- [ ] Concluir validação prolongada (stress tests do T6) abrangendo cenários de alternância de abas, interação intensa com preview e quedas controladas do backend GPU.

### T1 – Mapear recursões e loops de eventos
- **Estado:** Concluída
- **Código chave:**
  - `src/Mieruka.App/Ui/PreviewBindings/MonitorPreviewHost.cs`
  - `src/Mieruka.App/Services/Ui/TabEditCoordinator.cs`
- **Notas:** Diagrama e análise documentados na revisão interna. A inspeção dos
  handlers revelou reentrâncias entre `StartAsync`, callbacks de frame e eventos
  de mouse.

### T2 – Blindagem do ciclo de vida do preview
- **Estado:** Concluída
- **Código chave:**
  - `MonitorPreviewHost` introduziu máquina de estados (`Stopped`, `Starting`,
    `Running`, `Pausing`, `Paused`, `Disposing`) com gates de reentrância e
    throttling de frames.【F:src/Mieruka.App/Ui/PreviewBindings/MonitorPreviewHost.cs†L24-L131】
  - `TabEditCoordinator` passou a usar timers de debounce e guards ao pausar ou
    retomar o preview.【F:src/Mieruka.App/Services/Ui/TabEditCoordinator.cs†L1-L137】
- **Validação:** Exercícios manuais com troca rápida de abas e alternância de
  preview não reproduziram loops.

### T3 – Guard e fallback de GPU/WGC
- **Estado:** Concluída
- **Código chave:**
  - `GpuCaptureGuard` centraliza decisão e bloqueio permanente em falhas.
    【F:src/Mieruka.Core/Config/GpuCaptureGuard.cs†L1-L118】
  - `MonitorPreviewHost` consulta o guard antes de criar sessões de captura,
    registrando fallback para GDI e suprimindo retentativas agressivas.
    【F:src/Mieruka.App/Ui/PreviewBindings/MonitorPreviewHost.cs†L239-L377】
- **Validação:** Falhas induzidas forçaram `DisableGpuPermanently`, mantendo a
  aplicação em backend GDI estável.

### T4 – Debounce e loops de UI
- **Estado:** Concluída
- **Código chave:**
  - `MonitorPreviewHost` ignora frames encadeados e sincroniza atualização de
    overlays via throttling controlado.【F:src/Mieruka.App/Ui/PreviewBindings/MonitorPreviewHost.cs†L401-L533】
  - `TabEditCoordinator` adicionou timers para debouncing de eventos de UI,
    evitando ciclos `EnterEditTab`/`LeaveEditTab`.
    【F:src/Mieruka.App/Services/Ui/TabEditCoordinator.cs†L138-L287】
- **Validação:** Movimentação rápida de mouse e ajustes de X/Y/L/W não criam
  cascatas de atualizações.

### T5 – Guardas globais e logs específicos
- **Estado:** Concluída
- **Código chave:**
  - `Program.cs` instala handlers globais que detectam `StackOverflowException`
    e invocam `GpuCaptureGuard.DisableGpuPermanently` com log crítico.
    【F:src/Mieruka.App/Program.cs†L16-L75】
  - `MonitorPreviewHost` e guardas correlatos emitem logs pontuais quando
    bloqueiam reentrâncias.【F:src/Mieruka.App/Ui/PreviewBindings/MonitorPreviewHost.cs†L134-L238】
- **Validação:** Handlers exercitados via testes de exceção simulada confirmam o
  caminho de log sem loops adicionais.

### T6 – Validação final
- **Estado:** Em andamento contínuo
- **Passos de verificação:**
  1. `dotnet build`
  2. Stress manual: alternância de abas com preview ativo por 5 minutos.
  3. Interação constante de mouse/zoom no preview.
  4. Alterações rápidas de X/Y/L/W.
  5. Abrir/fechar janela de preview repetidamente.
- **Resultados parciais:** build local completo; nenhum `StackOverflowException`
  observado durante os cenários monitorados até o momento.

---

_Responsável: codex_  
_Última atualização: 2025-11-09_
