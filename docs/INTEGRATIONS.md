# Gestão de ambientes

O menu **Configurações** possui três itens:

1. **Aplicativo**: importação, UTF-8, logs, SQLite, HTTP Client e proxy.
2. **Dynatrace**: tenants DEV, HML e PRD.
3. **AppDynamics**: Controllers DEV, HML e PRD.

Cada ambiente possui apelido, identificador, URL-base, URL de teste,
autenticação, usuário opcional, chave, status esperado, observações e opção para
ativar ou desativar a conexão.

No Dynatrace, quando o ID estiver preenchido e a URL-base estiver vazia, a
aplicação gera `https://<environment-id>.live.dynatrace.com`. Quando a URL de
teste estiver vazia, o teste usa a Settings API V2 para consultar um perfil de
alerta. No AppDynamics, a URL-base é usada como teste quando nenhuma rota
específica for informada.

## Armazenamento das chaves

As chaves são gravadas em texto simples na propriedade `integrations` do
`settings.json`. A própria tela exibe o caminho desse arquivo. Não existe DPAPI,
cofre ou criptografia implementada pelo aplicativo.

O arquivo pertence ao perfil local do Windows. Não o envie por e-mail, Drive,
chamados, logs ou junto com o banco SQLite. As rotinas de log registram apenas a
plataforma, o ambiente, o resultado HTTP e a duração do teste.
