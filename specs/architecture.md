# Arquitetura do A2D Alert Migrator

Status: proposta do MVP  
Plataforma alvo: Windows 10/11 x64  
Runtime: .NET 10 LTS, publicação self-contained

## 1. Decisões principais

### 1.1 Aplicação desktop, sem servidor local

A interface será WPF. O processo não abrirá portas TCP, não hospedará HTTP em
`localhost`, não dependerá de navegador e não fará download em tempo de
execução. O executável fará apenas conexões HTTPS de saída:

- `https://sso.dynatrace.com`, quando a autenticação for OAuth Client
  Credentials;
- `https://{environment-id}.live.dynatrace.com`, para a Environment API V2;
- `https://{environment-id}.apps.dynatrace.com`, para APIs da plataforma,
  incluindo o Grail.

O proxy padrão do Windows será respeitado. Uma configuração explícita de proxy
HTTPS poderá ser adicionada se o ambiente não disponibilizar o proxy do sistema.

### 1.2 C#/.NET 10 + WPF

Motivos:

- WPF não requer WebView, Node.js nem backend web local;
- `HttpClient` atende às APIs REST do Dynatrace;
- .NET 10 é LTS e pode ser empacotado com o runtime;
- persistência simples e auditável das conexões no `settings.json`, sem camada
  criptográfica dentro do aplicativo;
- tipagem forte para o contrato CSV, payloads, validação e diff;
- boa operação em ambientes corporativos com assinatura de código e MSI/MSIX.

Caso surja um requisito real de Linux/macOS, somente a camada de UI e o cofre de
segredos devem ser substituídos (por Avalonia e um cofre por plataforma). O
domínio e os clientes REST não dependerão de WPF.

### 1.3 Schemas descobertos no tenant

Os IDs conhecidos orientam o processo, mas a forma final do payload não será
assumida cegamente. Na conexão, a ferramenta lê e armazena em cache a versão dos
schemas do tenant:

- `builtin:davis.anomaly-detectors`;
- `builtin:alerting.profile`;
- `builtin:alerting.maintenance-window`, somente para o modo de supressão
  avançado.

Isso protege a ferramenta contra campos obrigatórios ou versões diferentes
entre tenants.

## 2. Visão de componentes

```text
WPF / MVVM
  ├── Tenant e credenciais
  ├── Importação CSV ou pasta JSON
  ├── Mapeamento de colunas e diagnóstico por arquivo
  ├── Grade de regras + editor/diff
  └── Execução, progresso, logs e relatório
          │
Application
  ├── ImportSourceUseCase
  ├── ValidateMigrationUseCase
  ├── BuildPlanUseCase
  ├── ApplyPlanUseCase
  └── RollbackRunUseCase
          │
Domain
  ├── ApplicationImportDocument
  ├── CanonicalAlertRule
  ├── DqlTimeseriesSpec / DqlGenerator
  ├── DetectorSpec / AlertingProfileSpec
  ├── ActiveSchedule
  ├── ValidationIssue
  └── MigrationPlan / PlanOperation
          │
Infrastructure
  ├── DynatraceAuthClient
  ├── DynatraceSettingsClient
  ├── DynatraceGrailQueryClient
  ├── DynatraceEntitiesClient
  ├── CsvImportAdapter / ColumnMappingStore
  ├── JsonFolderImportAdapter / JsonVersionReader
  ├── JsonIntegrationSettingsStore
  ├── JsonRunManifestStore
  └── StructuredLogger
```

Dependências apontam para dentro: Infrastructure e UI implementam portas
definidas pela camada Application/Domain. O domínio pode ser testado sem WPF e
sem acesso a um tenant.

## 3. Pipeline de migração

### 3.1 Motor unificado de importação

CSV e pasta JSON são adaptadores de entrada, não pipelines diferentes. Ambos
produzem o mesmo modelo hierárquico:

```text
ImportBatch
  └── ApplicationImportDocument [1..N]
        ├── ApplicationIdentity
        ├── SourceSnapshot
        ├── Defaults
        └── CanonicalAlertRule [1..N]
```

`SourceSnapshot` preserva caminho relativo, tamanho, última modificação e hash
SHA-256. Cada diagnóstico carrega origem, aplicação, regra e JSON Pointer ou
linha/coluna do CSV. A troca da fonte invalida o plano já calculado.

O contrato da porta de entrada é equivalente a:

```text
IImportSourceAdapter
  CanRead(ImportSourceDescriptor)
  ReadAsync(ImportSourceDescriptor, ImportLimits, CancellationToken)
    -> ImportBatch
```

Os adaptadores existentes serão `CsvImportAdapter` e `JsonFolderImportAdapter`.
Leitores de novas versões JSON são registrados por `schemaVersion`, sem colocar
condições específicas da versão no restante do motor.

#### 3.1.1 CSV

1. Detectar BOM/encoding e sugerir delimitador (`,`, `;` ou tab).
2. Exibir as primeiras linhas antes de aceitar o arquivo.
3. Mapear os cabeçalhos para o contrato canônico.
4. Agrupar linhas por `application_id`.
5. Preservar número da linha e um hash SHA-256 do registro de origem.
6. Nunca interpretar fórmulas nem executar conteúdo vindo do CSV.

#### 3.1.2 Pasta JSON

1. Selecionar uma pasta e escolher busca somente no nível atual ou recursiva.
2. Enumerar apenas arquivos `*.json`, em ordem determinística por caminho
   relativo.
3. Rejeitar links simbólicos/reparse points e confirmar que cada caminho
   resolvido continua dentro da raiz selecionada.
4. Aplicar limites configuráveis antes de alocar memória: padrão de 10 MiB por
   arquivo, 1.000 arquivos, 5.000 regras por aplicação e profundidade 64.
5. Detectar JSON inválido, propriedades duplicadas, campos desconhecidos e
   versão de schema não suportada.
6. Validar o arquivo contra o schema correspondente antes de normalizá-lo.
7. Preservar a relação um arquivo → uma aplicação → várias regras.
8. Rejeitar `application.id` duplicado entre arquivos e `rule.id` duplicado
   dentro da aplicação.
9. Calcular o hash a partir dos mesmos bytes analisados, evitando diferença
   entre o conteúdo validado e o conteúdo planejado.

O erro de um arquivo não impede visualizar ou validar outras aplicações. Por
padrão, porém, uma aplicação é atômica: nenhuma de suas regras é habilitada se
qualquer regra do arquivo estiver bloqueada. Os detalhes do formato estão em
[`json-folder-contract.md`](json-folder-contract.md).

### 3.2 Normalização

- `application_id` e `source_rule_id` são obrigatórios e estáveis;
- defaults da aplicação são aplicados antes da validação, e a regra pode
  sobrescrever somente os campos documentados;
- nomes têm espaços normalizados, mas o valor original fica no relatório;
- números usam cultura explícita configurada no template do CSV;
- horários são convertidos para `HH:mm` e sempre associados a um fuso IANA;
- severidade, condição e tipo do detector são convertidos para enums internos;
- nomes de serviço não são persistidos na DQL final: são resolvidos para IDs;
- regras explicitamente agrupadas compartilham detector somente quando sinal,
  modelo, condição, janela, evento e roteamento são semanticamente equivalentes.

### 3.3 Resolução de serviços

O formato recomendado é `SERVICE-...`. Quando o CSV contém somente nome:

1. consultar o tenant com seleção exata e não sensível a maiúsculas/minúsculas;
2. zero resultados = erro `SERVICE_NOT_FOUND`;
3. um resultado = armazenar o ID resolvido no plano;
4. mais de um resultado = erro `SERVICE_AMBIGUOUS`, exigindo escolha na UI.

A resolução fica vinculada ao tenant e ao plano. Trocar de tenant invalida todas
as resoluções e validações anteriores.

### 3.4 Geração e validação DQL

A base gerada para uma métrica de serviço é equivalente a:

```dql
timeseries value = avg(dt.service.request.response_time),
  by: { dt.smartscape.service },
  filter: { dt.smartscape.service == toSmartscapeId("SERVICE-0123456789ABCDEF") },
  interval: 1m
| fieldsKeep value, dt.smartscape.service, timeframe, interval
```

Regras obrigatórias do validador local:

- o resultado deve nascer de `timeseries` ou `makeTimeseries`;
- o intervalo deve ser exatamente um minuto;
- `from:`, `to:` e sobrescrita de `timeframe` são proibidos;
- `sort` e `limit` são proibidos;
- a identidade deve conter `dt.smartscape.service` e apenas dimensões estáveis;
- todo serviço deve estar explicitamente filtrado por ID;
- a janela deve satisfazer `1 <= violating_samples <= sliding_window <= 60`;
- `1 <= dealerting_samples <= 60`;
- combinações de modelo/condição devem ser válidas.

Além da verificação local, a DQL será enviada ao endpoint Grail
`/platform/storage/query/v1/query:verify`. Opcionalmente, o usuário pode executar
uma prévia curta no próprio tenant para confirmar tipos, dimensões e presença de
dados. A prévia é explicitamente indicada como uma operação que consome consulta
Grail.

### 3.5 Horários de atividade

O modo padrão mascara os pontos fora do horário dentro da própria `timeseries`.
A consulta adiciona uma série de timestamp derivada de `start()` e usa
`getDayOfWeek`/`getHour` com o fuso configurado. Ela não define `from:` ou `to:`.

Exemplo conceitual para dias úteis, 09:00–18:00:

```dql
timeseries {
    value = avg(dt.service.request.response_time),
    point_time = start()
  },
  by: { dt.smartscape.service },
  filter: { dt.smartscape.service == toSmartscapeId("SERVICE-0123456789ABCDEF") },
  interval: 1m
| fieldsAdd value = if(
    in(getDayOfWeek(point_time[], timezone: "America/Sao_Paulo"), {1, 2, 3, 4, 5})
      and getHour(point_time[], timezone: "America/Sao_Paulo") * 60
        + getMinute(point_time[], timezone: "America/Sao_Paulo") >= 540
      and getHour(point_time[], timezone: "America/Sao_Paulo") * 60
        + getMinute(point_time[], timezone: "America/Sao_Paulo") < 1080,
    value[])
| fieldsKeep value, dt.smartscape.service, timeframe, interval
```

O gerador terá testes específicos para janelas que atravessam meia-noite,
mudança de dia e horário de verão.

Restrições:

- janela de atividade e `alert_on_missing_data=true` são incompatíveis no MVP,
  porque os pontos mascarados podem representar ausência intencional;
- uma janela de manutenção por serviço é oferecida somente como modo avançado,
  com aviso de impacto, pois pode suprimir outros problemas do mesmo serviço;
- se “horário do alarme” significar apenas horário de notificação, isso deve ser
  modelado em Workflows e não no detector. Esse caso fica separado para não
  confundir detecção com entrega.

### 3.6 Plano e diff

Nenhum `POST`, `PUT` ou `DELETE` é feito durante importação/validação. O
`BuildPlanUseCase` consulta o estado remoto e produz operações imutáveis:

- `CREATE` — objeto ainda não gerenciado;
- `UPDATE` — objeto gerenciado existe e mudou;
- `UNCHANGED` — estado remoto é semanticamente igual;
- `CONFLICT` — nome colide com objeto que não tem a identidade esperada;
- `BLOCKED` — validação falhou.

O diff compara JSON canônico, ignorando ordem de propriedades e metadados
gerados pelo servidor. A tela mostra antes/depois e o payload final.

### 3.7 Idempotência e propriedade

A identidade lógica é:

```text
tenant_environment_id + source_system + application_id + source_rule_id + object_kind
```

Os detectores recebem propriedades de evento que identificam a origem, por
exemplo `migration.tool`, `migration.source_rule_id` e
`migration.source_hash`. Os IDs retornados pelo Dynatrace ficam no manifesto
local.

Objetos com mesmo nome, mas sem identidade comprovada, nunca são atualizados
automaticamente. Eles aparecem como conflito. A adoção de um objeto existente
exige uma ação explícita e fica registrada.

### 3.8 Aplicação e rollback

A ordem padrão é:

1. criar/atualizar perfis da aplicação;
2. criar/atualizar seus detectores inicialmente desabilitados;
3. confirmar leitura de todos os objetos da aplicação;
4. habilitar os detectores da aplicação somente se o conjunto estiver íntegro;
5. avançar para a próxima aplicação;
6. finalizar manifesto e relatório.

Antes de cada atualização, o JSON remoto e sua revisão são salvos no manifesto.
Se uma operação falhar, as demais regras independentes podem continuar, conforme
a opção do usuário. Cancelamento impede novas operações, mas não interrompe uma
requisição já aceita pelo tenant.

O rollback de uma execução:

- remove somente objetos criados por ela e cuja identidade ainda confere;
- restaura o snapshot anterior de objetos atualizados, usando controle de
  revisão quando disponível;
- para e sinaliza conflito se o objeto foi alterado por outra pessoa depois da
  execução.

Não existe botão de exclusão em massa por nome ou prefixo.

## 4. Autenticação e segurança

Métodos aceitos:

1. **OAuth Client Credentials** (recomendado): client ID, secret e scopes; token
   de acesso mantido somente em memória e renovado antes de expirar.
2. **Platform token**: opção mais simples para uso pessoal/interno.

Por decisão operacional, as chaves dos ambientes DEV, HML e PRD são persistidas
em texto simples no `settings.json` do perfil local. Elas não entram no SQLite,
nos pacotes exportados nem nos logs. A tela avisa sobre o risco e exibe o caminho
do arquivo para que o acesso seja controlado pelo perfil do Windows.

Permissões mínimas esperadas para detectores DQL:

- `settings:schemas:read`;
- `settings:objects:read`;
- `settings:objects:write` para implantar;
- `storage:buckets:read` e `storage:metrics:read` para métricas;
- `iam:service-users:use` quando o ator for service user;
- `davis:analyzers:execute` quando não for usado service user.

Permissões de entidade e schemas clássicos devem ser testadas na conexão, pois
dependem da rota e da política do tenant. A aplicação informa exatamente qual
checagem falhou; nunca solicita permissões amplas silenciosamente.

## 5. Interface

A janela principal usa navegação por etapas e mantém uma barra de contexto com o
tenant, arquivo e quantidade de regras. Na etapa de revisão:

```text
┌ Regras ───────────────────────────────┬ Detalhes ─────────────────────────┐
│ ✓ Pagamentos - latência       UPDATE  │ [DQL] [Detector] [Perfil] [JSON] │
│ ! Checkout - erros           CONFLICT │                                  │
│ ✕ Catálogo - throughput       BLOCKED │ consulta/Diff/erros da seleção   │
└───────────────────────────────────────┴──────────────────────────────────┘
  18 válidas · 2 avisos · 1 erro               [Dry-run] [Implantar 18]
```

Princípios:

- estado nunca depende apenas de cor; ícone e texto acompanham a cor;
- erros incluem linha, coluna canônica e correção sugerida;
- ações destrutivas mostram o alvo e não ficam como ação padrão;
- DQL e JSON podem ser copiados, mas segredos nunca são exibidos em logs;
- durante a implantação, a navegação que invalidaria o plano fica bloqueada.

## 6. Logs e auditoria

Cada execução recebe `run_id`; cada linha recebe `row_id`; cada chamada recebe
`operation_id`. Há dois arquivos:

1. `run-<id>.log`: texto legível, indentado por escopos;
2. `run-<id>.jsonl`: eventos estruturados para busca e automação.

Exemplo humano:

```text
[19:42:11.032 INF] run=01J... Tenant validado: prod-br
  [19:42:11.114 INF] row=27 Regra "Checkout - erros"
    [19:42:11.118 DBG] Serviço resolvido: SERVICE-A1B2...
    [19:42:11.241 INF] DQL válida em 123 ms
    [19:42:11.660 INF] Detector criado: vu9U3...
  [19:42:11.665 INF] row=27 Concluída em 551 ms
```

Níveis:

- `TRACE`: protocolo sanitizado e decisões internas detalhadas;
- `DEBUG`: payloads sanitizados, schemas, resolução e tempos;
- `INFO`: início/fim, planos e resultados por regra;
- `WARN`: condição recuperável, retry, depreciação ou ambiguidade;
- `ERROR`: falha de regra/operação com contexto e resposta sanitizada;
- `FATAL`: execução abortada ou manifesto não pôde ser preservado.

Política padrão:

- console visual em `INFO`;
- arquivo humano em `DEBUG`;
- JSONL em `DEBUG`;
- rotação por 20 MiB e retenção configurável, padrão de 30 dias;
- `Authorization`, client secret e tokens sempre substituídos por `[REDACTED]`;
- DQL completa somente em `DEBUG`; em `INFO` aparece nome e hash;
- payloads de resposta têm limite de tamanho;
- nenhuma telemetria sai da máquina.

O manifesto é separado do log e contém o plano, snapshots, revisões, IDs remotos,
hashes, horários e resultados. Ele é escrito de forma atômica após cada operação.

## 7. Estrutura de solução proposta

```text
src/
  A2D.AlertMigrator.Domain/          # entidades e regras puras
  A2D.AlertMigrator.Application/     # interfaces e casos de uso
  A2D.AlertMigrator.Infrastructure/  # JSON, CSV, SQLite e Dynatrace
  A2D.AlertMigrator.Desktop/
tests/
  A2D.AlertMigrator.Infrastructure.SmokeTests/
specs/
samples/
```

Pacotes externos devem ser poucos, fixados por versão e restaurados apenas no
build. Candidatos: um leitor CSV compatível com RFC 4180, logging estruturado e
um toolkit MVVM. O pacote publicado inclui todas as dependências.

## 8. Critérios de aceite do MVP

- nenhum socket em estado `LISTENING` é criado pela aplicação;
- importar 5.000 regras mantém a UI responsiva;
- CSV com vírgula dentro de campo, aspas, quebra de linha e UTF-8 é interpretado
  corretamente;
- pasta JSON mantém a relação arquivo → aplicação → regras e usa ordem
  determinística;
- JSON malformado, chave duplicada ou schema não suportado bloqueia somente a
  aplicação correspondente;
- recursão nunca segue links/reparse points nem permite escapar da pasta raiz;
- CSV e JSON semanticamente equivalentes produzem o mesmo modelo canônico;
- uma regra nunca é enviada antes de passar pelas validações local e remota;
- toda DQL gerada é `timeseries`/`makeTimeseries`, tem `interval:1m`, serviço por
  ID e não define timeframe;
- segunda execução do mesmo arquivo produz `UNCHANGED`, salvo mudança real;
- colisões por nome não sobrescrevem objetos alheios;
- queda de rede deixa o manifesto consistente e permite retomar/reconciliar;
- tokens e secrets não aparecem em nenhum nível de log;
- rollback não remove nem sobrescreve objetos modificados fora da execução;
- pacote `win-x64` roda sem SDK/runtime .NET instalado.

## 9. Referências técnicas

- Dynatrace, DQL custom alerts via API:
  https://docs.dynatrace.com/docs/dynatrace-intelligence/anomaly-detection/set-up-anomaly-detectors-via-api
- Dynatrace, guia de DQL para Anomaly Detection:
  https://docs.dynatrace.com/docs/dynatrace-intelligence/anomaly-detection/anomaly-detection-app/anomaly-detection-dql-best-practice
- Dynatrace, Grail Query API:
  https://developer.dynatrace.com/develop/platform-services/services/grail-service/
- Dynatrace, perfis de alerta:
  https://docs.dynatrace.com/docs/analyze-explore-automate/notifications-and-alerting/alerting-profiles
- Dynatrace, schema de manutenção:
  https://docs.dynatrace.com/docs/dynatrace-api/environment-api/settings/schemas/builtin-alerting-maintenance-window
- Microsoft, política de suporte .NET:
  https://dotnet.microsoft.com/platform/support/policy
- Microsoft, single-file deployment:
  https://learn.microsoft.com/dotnet/core/deploying/single-file/overview
