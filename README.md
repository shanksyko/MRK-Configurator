# MRK-Configurator

O **MRK-Configurator** é a ferramenta central para configurar monitores, janelas e cenas de
navegador no ambiente de sinalização digital Mieruka.

## O que este programa faz?

- Centraliza a configuração de telas e posicionamento de janelas.
- Ajuda a preparar cenas de navegador para exibição.
- Inclui agente em background (tray), recursos de preview e automações para operação diária.
- Gera dados de diagnóstico para facilitar suporte e resolução de problemas em campo.

## Projeto feito com IA

Este projeto foi desenvolvido com **apoio integral de Inteligência Artificial (IA)**, com
assistência na geração de código, documentação e sugestões de implementação.

## Diagnóstico e suporte

- **Local dos logs**: a cada execução, o app grava arquivos diários em
  `%LOCALAPPDATA%\Mieruka\Logs\<yyyy-MM>`. Cada mês fica em sua própria pasta e o arquivo do dia
  atual é salvo como `<yyyy-MM-dd>.log`.
- **Retenção**: logs com mais de 14 dias são removidos automaticamente na inicialização.
  Se precisar manter histórico maior, copie a pasta antes de abrir o app.
- **Modo detalhado (trace)**: defina a variável de ambiente `MIERUKA_TRACE` como `1`, `true`
  ou `verbose` antes de iniciar o Configurator para usar nível de log `Verbose` sem recompilar.
- **Pacote para suporte**: compacte toda a pasta `%LOCALAPPDATA%\Mieruka\Logs` ao enviar
  informações para suporte. Isso inclui pastas mensais e eventuais crash dumps no mesmo local.

Mais dicas de troubleshooting e coleta no Windows em
[`docs/Troubleshooting.md`](docs/Troubleshooting.md).
