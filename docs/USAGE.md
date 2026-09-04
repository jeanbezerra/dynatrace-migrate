# Uso da tela

Abra `A2D.AlertMigrator.exe` ou execute:

```powershell
./scripts/run-desktop.ps1
```

1. Clique em **Selecionar pasta**.
2. Escolha a pasta com um JSON por aplicação.
3. Marque **Incluir subpastas**, se necessário.
4. Pesquise por aplicação, ID, arquivo ou regra.
5. Clique em um cabeçalho para ordenar a grade.
6. Marque **Continuar?** como **Sim** ou **Não**; **Todos** e **Nenhum** atuam somente nas linhas exibidas.
7. Selecione uma aplicação e consulte **Regras**, **Diagnósticos** e **Atividade**.

Aplicações com erro ficam bloqueadas em **Não**.

## Configurações

Abra **Configurações > Aplicativo** no menu para definir política de BOM UTF-8,
recursão, limites de importação e logs. Os logs usam JSON Lines, timestamp UTC
ISO 8601, nível configurável e rotação opcional na pasta escolhida. Clique em
**Gravar evento de teste** para validar a leitura por outra ferramenta. Consulte
[`LOGGING.md`](LOGGING.md).

Clique em **Salvar configurações**; o arquivo local é gravado em UTF-8 sem BOM.
As opções de log são aplicadas imediatamente e as de importação valem na próxima
execução.

Na seção **Banco de dados local (SQLite)**, escolha a pasta, WAL e timeout. O
caminho do arquivo em uso é exibido na tela. Use **Exportar cópia** para gerar um
`.db` consistente e compartilhável; consulte [`DATABASE.md`](DATABASE.md).

Na seção **Configuração do HTTP Client**, configure limites de tempo, novas
tentativas, TLS e cabeçalhos não sensíveis. A seção **Configuração de proxy**
controla o acesso à rede. Em **Testes de conexão e API**, informe a URL completa,
a autenticação e o status esperado de cada plataforma. Consulte
[`HTTP-CLIENT.md`](HTTP-CLIENT.md).

Use **Configurações > Dynatrace** e **Configurações > AppDynamics** para gerir
separadamente os ambientes DEV, HML e PRD. As chaves dessas páginas são salvas
em texto simples no `settings.json`. Consulte [`INTEGRATIONS.md`](INTEGRATIONS.md).

## Ajuda de acesso

No menu **Ajuda**, abra **Dynatrace** ou **AppDynamics** para consultar as
permissões mínimas e o processo de emissão da credencial. Os modelos de chamado
podem ser selecionados e copiados. Os segredos devem permanecer fora dos JSON,
do SQLite e dos logs.

## Inventários do Dynatrace

Em **Gestão de Alertas > Dynatrace**, use **Perfis de alertas** para consultar os
perfis Classic e **Anomaly Detection** para consultar detectores DQL. Selecione
um ambiente salvo e inicie o sincronismo manualmente. Pesquisa, ordenação e
detalhes funcionam sobre o snapshot local no SQLite.

Na tela de Anomaly Detection, **Somente fora do padrão** isola consultas que não
começam com `timeseries`. Um duplo clique abre o DQL e o JSON original.

Em **Eventos de alertas**, escolha 24 horas, 7 dias ou 30 dias e clique em
**Consultar**. Use **Somente ativos** ou **Críticos e altos** para triagem. Um
duplo clique mostra contexto, entidade, detector, DQL e JSON do evento.

Em **Problemas**, use **Somente ativos** ou **Com causa-raiz** para triagem. O
detalhe reúne impacto, causa-raiz, entidades afetadas, Davis Events relacionados
e o JSON retornado pelo Grail.

Para recriar a massa com 300 aplicações:

```powershell
./scripts/generate-sample-applications.ps1
```

O gerador valida e grava todos os exemplos em UTF-8 sem BOM.
