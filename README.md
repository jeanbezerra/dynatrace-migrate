# A2D Alert Migrator

Aplicação desktop Windows para transformar regras exportadas do AppDynamics em
detectores de anomalia DQL e perfis de alertas no Dynatrace SaaS.

O MVP em construção já possui tela desktop para importar e validar uma pasta
JSON. O código está separado em `Domain`, `Application`, `Infrastructure` e
`Desktop`. O desenho técnico está em
[`specs/architecture.md`](specs/architecture.md). Os contratos de entrada estão
em [`specs/csv-contract.md`](specs/csv-contract.md) e
[`specs/json-folder-contract.md`](specs/json-folder-contract.md).

## Objetivo do MVP

- selecionar um tenant Dynatrace salvo localmente;
- importar qualquer CSV por meio de um assistente de mapeamento de colunas;
- importar uma pasta de JSON, com um arquivo versionado por aplicação;
- normalizar e validar todas as regras antes de alterar o tenant;
- resolver serviços por ID ou nome, rejeitando correspondências ambíguas;
- gerar somente consultas DQL baseadas em `timeseries`, com `interval:1m`;
- criar/atualizar `builtin:davis.anomaly-detectors` e
  `builtin:alerting.profile` pela Settings API;
- permitir janelas de atividade por regra, sem alterar o timeframe solicitado
  pelo detector;
- mostrar o diff e exigir confirmação antes da implantação;
- executar de forma idempotente, com manifesto de auditoria e rollback da
  execução;
- manter logs JSONL estruturados e legíveis, sem registrar segredos.

## Decisão de tecnologia

**C# + .NET 10 LTS + WPF**, com arquitetura MVVM.

Essa combinação foi escolhida porque o ambiente observado é Windows e a
aplicação precisa funcionar sem servidor web local. O pacote final será
`win-x64`, self-contained e sem telemetria ou atualização automática. Em tempo
de execução, a única comunicação de rede necessária é HTTPS de saída para o
Dynatrace e, quando usado OAuth, para o endpoint SSO.

O histórico será mantido em SQLite local. A exportação produzirá um pacote
portátil `.a2dmigrate` sem credenciais. Por decisão do projeto, as chaves dos
ambientes DEV, HML e PRD ficam em texto simples no `settings.json` local e nunca
são copiadas para o banco ou para os logs.

## Organização

```text
src/
  A2D.AlertMigrator.Domain/
  A2D.AlertMigrator.Application/
  A2D.AlertMigrator.Infrastructure/
  A2D.AlertMigrator.Desktop/          # próxima etapa
tests/
docs/
specs/
schemas/
samples/
```

Consulte a [arquitetura SOLID](docs/ARCHITECTURE.md) e a
[preparação da máquina](docs/SETUP.md).

Em ambientes com Windows App Control, distribua somente o pacote de arquivo
único assinado por um publicador autorizado. Consulte o guia de
[assinatura e liberação](docs/CODE-SIGNING.md).

## Abrir a tela

```powershell
./scripts/run-desktop.ps1
```

O uso da interface está resumido em [`docs/USAGE.md`](docs/USAGE.md).

Para testar pesquisa e ordenação em volume, selecione
[`samples/json/generated-300`](samples/json/generated-300), com 300 aplicações e
600 regras válidas.

A tela **Configurações** controla a política de BOM UTF-8, recursão, limites de
importação e logs ISO 8601 com verbosidade e rotação configuráveis; JSON com
bytes UTF-8 inválidos é sempre bloqueado.

Ela também controla o banco SQLite local, exibe o arquivo em uso e permite gerar
uma cópia consistente para compartilhamento. Consulte
[`docs/DATABASE.md`](docs/DATABASE.md).

A seção **Configuração do HTTP Client** controla resiliência, certificados e
cabeçalhos. O proxy possui uma seção própria, enquanto as URLs ficam nas áreas
específicas do Dynatrace e do AppDynamics. Consulte
[`docs/HTTP-CLIENT.md`](docs/HTTP-CLIENT.md).

O menu **Ajuda** possui guias separados para Dynatrace e AppDynamics. Cada guia
resume as permissões mínimas, o processo de liberação, um modelo de chamado e
links para a documentação oficial.

A URL-base e o teste recomendado da Environment API V2 estão documentados em
[`docs/DYNATRACE.md`](docs/DYNATRACE.md).

Em **Configurações**, os itens **Aplicativo**, **Dynatrace** e **AppDynamics**
separam as preferências locais da gestão de ambientes DEV, HML e PRD. Consulte
[`docs/INTEGRATIONS.md`](docs/INTEGRATIONS.md).

Em **Gestão de Alertas > Dynatrace > Perfis de alertas**, a aplicação sincroniza
sob demanda os `builtin:alerting.profile` Classic do tenant selecionado. A tabela
possui pesquisa, ordenação, detalhes do JSON e histórico no SQLite. Consulte
[`docs/DYNATRACE.md`](docs/DYNATRACE.md).

Em **Gestão de Alertas > Dynatrace > Anomaly Detection**, a aplicação mantém um
inventário dos detectores DQL `builtin:davis.anomaly-detectors`. A tela identifica
o modelo de análise, permite abrir a consulta e o JSON e destaca detectores cuja
consulta não começa com `timeseries`.

Em **Gestão de Alertas > Dynatrace > Eventos de alertas**, a aplicação consulta
`dt.davis.events` no Grail para as últimas 24 horas, 7 dias ou 30 dias. Os
resultados ficam no SQLite e podem ser filtrados por estado, prioridade, nome,
entidade, origem ou detector.

Em **Gestão de Alertas > Dynatrace > Problemas**, a aplicação consulta
`dt.davis.problems` sem duplicados e preserva o ID `P-…`, causa-raiz, impacto,
entidades afetadas e Davis Events correlacionados.

## Fluxo de uso

1. **Tenant** — selecionar, cadastrar e testar conexão/permissões.
2. **Importar** — abrir um CSV ou selecionar uma pasta de aplicações JSON.
3. **Mapear** — para CSV, associar cabeçalhos AppDynamics ao modelo canônico;
   para JSON, validar estrutura, versão, aplicação e regras.
4. **Validar** — resolver serviços, gerar DQL, verificar sintaxe no Grail e
   validar os schemas do próprio tenant.
5. **Revisar** — visualizar `CREATE`, `UPDATE`, `UNCHANGED`, `CONFLICT` e o diff
   de cada objeto.
6. **Implantar** — aplicar somente os itens selecionados, com progresso e
   cancelamento seguro.
7. **Resultado** — exportar relatório e, se necessário, reverter apenas os
   objetos alterados pela execução.

## Próximo insumo necessário

Uma amostra anonimizada do CSV e de um JSON real de aplicação é necessária para
fechar os adaptadores AppDynamics → modelo canônico. Enquanto isso, o projeto
define um formato JSON canônico versionado em
[`schemas/application-rules-v1.schema.json`](schemas/application-rules-v1.schema.json).

## Verificação do motor JSON

Com o SDK .NET 10 disponível:

```powershell
dotnet build A2D.AlertMigrator.slnx
dotnet run --project tests/A2D.AlertMigrator.Infrastructure.SmokeTests -- $PWD.Path
```

Os testes não usam banco, rede nem dependências NuGet de terceiros.
