# Logs locais

O aplicativo grava `a2d-alert-migrator.jsonl` em UTF-8 sem BOM. Cada linha é
um JSON independente e recebe `timestamp` UTC em ISO 8601, `level`, `event`,
`sessionId`, processo, thread, mensagem e propriedades estruturadas.

Na tela **Configurações > Logs estruturados** é possível escolher:

- pasta de destino e nível mínimo (`Trace` a `Critical`, ou desativado);
- rotação por tamanho e quantidade de arquivos históricos mantidos;
- gravação de um evento de teste para validar um coletor externo.

As alterações passam a valer ao salvar, sem reiniciar o aplicativo. Cada evento
é liberado imediatamente e o arquivo aceita leitura concorrente. Um leitor
Windows próprio deve usar compartilhamento `ReadWrite | Delete` para não impedir
a rotação.

Exemplo simples de acompanhamento:

```powershell
Get-Content "$env:LOCALAPPDATA\A2DAlertMigrator\logs\a2d-alert-migrator.jsonl" -Wait
```

Coletores podem monitorar `a2d-alert-migrator*.jsonl`. Tokens, credenciais e o
conteúdo integral dos arquivos importados não devem ser adicionados aos eventos.
