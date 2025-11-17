# Catálogo de eventos
- PreviewStart/Stop (Information): backend, featureLevel (quando aplicável), targetFps, uptimeMs
- BackendDecision (Information): backend=GDI/WGC, motivos
- Fallback (Warning rate-limited): motivo simplificado
- PreviewStats (Debug): targetFps, fps, frames, dropped, invalid, frameProcessingAvgMs/maxMs (se disponível)
- ReentrancyBlocked (Warning): contagem coalescida
- PaintException (Error sampling): exceção e contagem
- UserAction (Information/Debug): toggles e edições relevantes
- Exceptions (Error): stack completo
