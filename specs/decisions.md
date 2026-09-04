# Registro de decisões

## ADR-001 — Aplicação desktop WPF

Decisão: usar C#/.NET 10 LTS com WPF e MVVM.

Consequências:

- nenhuma porta de entrada ou servidor local;
- pacote Windows self-contained;
- conexões locais persistidas em JSON sem componente criptográfico próprio;
- para suportar Linux/macOS no futuro, a UI e o cofre deverão ser substituídos.

Alternativas avaliadas:

- Python + PySide: bom para protótipo, porém o empacotamento, cold start,
  assinatura e controle de dependências são menos previsíveis no ambiente
  corporativo;
- Electron: pacote maior e inclui runtime de navegador desnecessário;
- Angular + backend local: adiciona um processo HTTP e uma porta, contrariando a
  restrição principal;
- Tauri: não exige servidor em produção, mas adiciona Rust/WebView2 e dois
  ecossistemas de build sem benefício suficiente para um aplicativo Windows.

## ADR-002 — Sem banco no MVP

Status: substituída pela ADR-006.

Decisão: usar JSON para configurações/manifests e JSONL para eventos de log.

Consequências:

- não existe serviço ou porta de banco;
- instalação e recuperação são simples;
- histórico pode ser exportado ou copiado facilmente;
- se o volume histórico crescer além do esperado, SQLite pode ser introduzido
mantendo a mesma interface de persistência.

## ADR-003 — Horário no valor da timeseries

Decisão: para horário de detecção, mascarar pontos fora da janela na DQL e
preservar o timeframe controlado pelo detector.

Consequências:

- a programação é específica da regra;
- não suprime outros detectores do serviço;
- requer impedir alerta por ausência no mesmo detector;
- horário de notificação continua sendo responsabilidade de Workflow.

## ADR-004 — Plano antes de escrita

Decisão: separar estritamente importação, validação, planejamento e aplicação.

Consequências:

- dry-run não altera o tenant;
- o usuário revisa payload e diff;
- cada aplicação parte de um snapshot e gera manifesto de rollback;
- qualquer mudança de tenant, CSV ou mapeamento invalida o plano.

## ADR-005 — Adaptadores de entrada convergem no mesmo modelo

Decisão: CSV e pasta JSON implementam a mesma porta de importação e produzem
`ApplicationImportDocument`.

Consequências:

- validação, DQL, diff, implantação e rollback não conhecem o formato original;
- cada JSON representa uma aplicação e preserva suas regras como unidade;
- o formato A2D JSON é versionado; JSON externo usa template de caminhos sem
  scripts executáveis;
- novas versões ou formatos entram como adaptadores, sem bifurcar o domínio.

## ADR-006 — Histórico portátil em SQLite

Decisão: persistir projetos e execuções em SQLite local e exportar uma cópia
consistente no pacote `.a2dmigrate`.

Consequências:

- não existe serviço ou porta de banco;
- o arquivo aberto não deve ficar em pasta sincronizada;
- credenciais permanecem fora do banco e são mantidas no `settings.json` local;
- schema e migrações do banco serão versionados pela aplicação.

## ADR-007 — Chaves das integrações em texto simples

Decisão: armazenar as chaves dos ambientes DEV, HML e PRD em uma área dedicada
do `settings.json`, sem DPAPI, cofre embutido ou criptografia implementada pelo
aplicativo.

Consequências:

- a configuração é portátil e fácil de inspecionar por ferramentas corporativas;
- o arquivo não pode ser compartilhado, anexado a chamados ou incluído em logs;
- a proteção depende das permissões do perfil local do Windows;
- exportações do SQLite e pacotes de projeto não incluem o `settings.json`;
- tokens usados somente nos testes avulsos continuam sem persistência.
