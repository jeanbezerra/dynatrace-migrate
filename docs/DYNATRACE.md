# Conexão com o Dynatrace

## URL do ambiente

A URL-base confirmada para o teste do tenant SaaS é:

```text
https://<environment-id>.live.dynatrace.com
```

Substitua `<environment-id>` pelo identificador do ambiente, sem barras ou
caracteres de escape.

## Teste recomendado da Environment API V2

Para validar conectividade, autenticação e permissão de leitura dos perfis de
alerta, faça um `GET` em:

```text
https://<environment-id>.live.dynatrace.com/api/v2/settings/objects?schemaIds=builtin:alerting.profile&pageSize=500&adminAccess=true
```

Com Platform Token, selecione **Platform Token ou OAuth (Bearer)**. A requisição
usa `Authorization: Bearer <token>` e requer `settings:objects:read`. O resultado
esperado é HTTP 200 com um documento JSON. O inventário completo com
`adminAccess=true` também requer `settings:objects:admin`. Sem esse parâmetro, a
aplicação coleta somente os objetos permitidos e não marca os demais como
ausentes.

## Sincronizar perfis Classic

Abra **Gestão de Alertas > Dynatrace > Perfis de alertas**, selecione DEV, HML
ou PRD e clique em **Sincronizar**. A operação lê todas as páginas, atualiza o
SQLite em uma única transação e mantém o último JSON de cada objeto. O token
permanece apenas no `settings.json` da conexão e não é gravado no inventário.

## Sincronizar detectores de anomalia DQL

Abra **Gestão de Alertas > Dynatrace > Anomaly Detection**, escolha o ambiente e
clique em **Sincronizar detectores**. A tela consulta o schema
`builtin:davis.anomaly-detectors`, classifica os modelos estático, adaptativo,
sazonal e de registros e grava o snapshot no SQLite.

A opção **Somente fora do padrão** mostra regras cuja consulta não começa com
`timeseries`. Abra uma regra para consultar e copiar o DQL ou o JSON original.
Esse inventário cobre os detectores DQL modernos; configurações Classic de
anomalia pertencem a outros schemas e não são misturadas nesta tela.

Para testar a mesma permissão diretamente na Environment API V2, use:

```text
https://<environment-id>.live.dynatrace.com/api/v2/settings/objects?schemaIds=builtin:davis.anomaly-detectors&pageSize=500&adminAccess=true
```

O Platform Token precisa de `settings:objects:read`. Para um inventário
administrativo completo, habilite **Visão administrativa** e conceda também
`settings:objects:admin`. A tela não grava o token, executa alterações no tenant
nem sincroniza automaticamente.

## Consultar eventos de alertas

Abra **Gestão de Alertas > Dynatrace > Eventos de alertas**, selecione o tenant e
o período e clique em **Consultar**. A aplicação executa `fetch dt.davis.events`,
acompanha a consulta até a conclusão e mantém o estado mais recente de cada
`event.id` no SQLite.

A consulta aceita até 5.000 eventos por execução. Quando esse limite é atingido,
a tela recomenda reduzir o período para evitar uma visão cortada. Nenhum dado é
consultado automaticamente.

A URL da Plataforma é derivada da URL cadastrada:

```text
https://<environment-id>.apps.dynatrace.com/platform/storage/query/v1/query:execute
https://<environment-id>.apps.dynatrace.com/platform/storage/query/v1/query:poll
```

Use **Platform Token ou OAuth (Bearer)**. A política IAM e o token precisam de
`storage:events:read` e acesso de leitura ao bucket correspondente por
`storage:buckets:read`. API Token legado não autentica essa API.

## Consultar problemas

Abra **Gestão de Alertas > Dynatrace > Problemas**, escolha o período e clique em
**Consultar**. A aplicação executa `fetch dt.davis.problems`, descarta registros
marcados como duplicados e armazena o estado mais recente por `event.id`.

A tabela prioriza o `display_id`, estado, categoria, impacto e causa-raiz. O
detalhe mostra usuários e entidades afetados, serviços envolvidos e os Davis
Events correlacionados. A consulta usa as mesmas permissões do histórico de
eventos: `storage:events:read` e `storage:buckets:read`.

A URL-base isolada confirma que o host está acessível. Ela não substitui o teste
da rota `/api/v2/settings/objects`, que também verifica token e autorização.

A API DQL do Grail continua usando o domínio de plataforma:

```text
https://<environment-id>.apps.dynatrace.com/platform/storage/query/v1/query:execute
```

Referências oficiais: [Environment API](https://docs.dynatrace.com/docs/dynatrace-api/environment-api),
[GET settings objects](https://docs.dynatrace.com/docs/dynatrace-api/environment-api/settings/objects/get-objects)
e [Platform tokens](https://docs.dynatrace.com/docs/manage/identity-access-management/access-tokens-and-oauth-clients/platform-tokens).
Para os detectores DQL, consulte também o
[schema oficial](https://docs.dynatrace.com/docs/dynatrace-api/environment-api/settings/schemas/builtin-davis-anomaly-detectors)
e a [automação por API](https://docs.dynatrace.com/docs/dynatrace-intelligence/anomaly-detection/set-up-anomaly-detectors-via-api).
Para eventos, consulte a [Grail DQL Query API](https://developer.dynatrace.com/develop/sdks/client-query/v1/)
e o [modelo Davis](https://docs.dynatrace.com/docs/semantic-dictionary/model/davis). A visão de incidentes segue a
[documentação de Problemas](https://docs.dynatrace.com/docs/dynatrace-intelligence/problems-app).
