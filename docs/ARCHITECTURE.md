# Arquitetura SOLID

```text
Desktop -> Application -> Domain
             ^
             |
       Infrastructure
```

- `Domain`: modelos e regras sem dependências externas.
- `Application`: casos de uso, opções e interfaces de entrada.
- `Infrastructure`: arquivos, JSON, SQLite e clientes Dynatrace.
- `Desktop`: WPF/MVVM e composição das dependências.

O importador JSON foi dividido em descoberta segura, leitura/hash, parsing e validação do lote. Novas fontes implementam `IImportSourceAdapter<T>` sem alterar os casos de uso.

O contrato `IApplicationLogger` fica em `Application`; a implementação de
arquivo JSONL, rotação e retenção fica em `Infrastructure`; a composição e as
preferências do usuário ficam em `Desktop`. Assim, integrações futuras podem
usar o mesmo contrato sem depender da interface WPF.

O contrato `IRemoteHttpClientFactory` fica em `Application`; a implementação
`ResilientRemoteHttpClientFactory` fica em `Infrastructure`. A UI persiste apenas
opções não secretas e não conhece detalhes de `HttpClient`, TLS ou Polly.

O contrato `ILocalDatabaseService` também fica em `Application`. A implementação
`SqliteLocalDatabaseService`, o esquema versionado, a verificação de integridade
e o backup consistente ficam em `Infrastructure`. A UI apenas configura e
orquestra esses contratos.

O sincronismo de perfis Classic usa `IDynatraceAlertingProfileClient` e
`IDynatraceAlertingProfileStore`. O caso de uso pagina a Settings API e a
infraestrutura aplica o snapshot no SQLite de forma transacional. Inventários
sem acesso administrativo não removem nem ocultam objetos que podem apenas estar
fora da permissão do token.

O inventário DQL usa contratos próprios,
`IDynatraceAnomalyDetectorClient` e `IDynatraceAnomalyDetectorStore`, sem acoplar
a UI ao HTTP ou ao SQLite. `SyncDynatraceAnomalyDetectorsUseCase` coordena o
snapshot transacional, enquanto a camada Desktop aplica pesquisa, ordenação e a
validação visual do comando inicial `timeseries`.

A leitura operacional dos alertas usa `IDynatraceDavisEventClient` e
`IDynatraceDavisEventStore`. O cliente transforma a URL Environment na URL da
Plataforma, executa e acompanha a consulta DQL. O caso de uso persiste o lote
somente depois da resposta completa, mantendo credenciais fora do banco.

Problemas usam `IDynatraceProblemClient` e `IDynatraceProblemStore`. Eventos e
problemas compartilham `DynatraceDqlQueryExecutor`, responsável somente pelo
ciclo HTTP `query:execute` e `query:poll`; cada adaptador mantém seu próprio
parser e modelo de persistência.

## Tema visual

O Fluent é ativado uma única vez por `ThemeMode="Light"`. Cores e estados de
`TextBox`, `PasswordBox`, `ComboBox`, `CheckBox`, `Button` e `DataGrid` ficam no
dicionário global `FluentTokens.xaml`; telas não devem redefinir cores de estado
localmente.
