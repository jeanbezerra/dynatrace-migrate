# Política de Privacidade do A2D Alert Migrator

**Versão 1.0 — última atualização: 4 de setembro de 2026**

Esta política descreve como o A2D Alert Migrator, publicado como aplicativo
para Windows, trata dados durante a importação, análise e migração de
configurações entre AppDynamics e Dynatrace. O responsável pelo aplicativo é o
publicador identificado na ficha do produto na Microsoft Store.

## Resumo

O A2D Alert Migrator funciona localmente no computador do usuário. O aplicativo
não possui servidor próprio, não exibe anúncios, não vende dados e não utiliza
dados para publicidade ou rastreamento de comportamento. As conexões externas
ocorrem somente quando configuradas ou iniciadas pelo usuário.

## Dados tratados

O aplicativo pode tratar:

- arquivos CSV e JSON escolhidos pelo usuário, incluindo aplicações, serviços,
  regras de alerta, horários, identificadores e consultas DQL;
- informações de conexão cadastradas pelo usuário, como apelido do ambiente,
  URL, tenant, nome de usuário, token, chave, segredo, cabeçalhos HTTP, proxy e
  opções de TLS;
- dados obtidos de ambientes Dynatrace e AppDynamics, como perfis de alerta,
  detectores de anomalias, eventos, problemas, serviços, entidades, estados,
  descrições, horários e respostas JSON das APIs;
- histórico de importações, migrações e sincronizações;
- registros técnicos contendo data e hora em ISO 8601, nível, evento, versão do
  aplicativo, sessão, processo, thread, avisos, erros e informações necessárias
  ao diagnóstico.

O conteúdo processado pode conter dados pessoais ou confidenciais inseridos
pela organização nos arquivos ou nas plataformas integradas. Mensagens de erro
de serviços externos também podem incluir identificadores ou partes da resposta
recebida.

O aplicativo não solicita acesso a localização, câmera, microfone, contatos ou
outros arquivos que não tenham sido selecionados pelo usuário.

## Finalidades

Os dados são usados exclusivamente para:

- importar, validar, correlacionar e transformar regras de monitoramento;
- testar autenticação e conectividade;
- consultar e sincronizar objetos dos ambientes selecionados;
- preparar e executar migrações autorizadas pelo usuário;
- manter histórico local e apoiar auditoria e diagnóstico;
- gerar arquivos e cópias do banco exportados pelo usuário.

## Armazenamento e retenção

Os dados permanecem no computador do usuário. Por padrão, são utilizados os
seguintes locais dentro do perfil do Windows:

- configurações: `%LOCALAPPDATA%\A2DAlertMigrator\settings.json`;
- banco SQLite: `%LOCALAPPDATA%\A2DAlertMigrator\data\a2d-alert-migrator.db`;
- logs: `%LOCALAPPDATA%\A2DAlertMigrator\logs`.

O local dos logs pode ser alterado, e o banco SQLite pode ser exportado para uma
pasta escolhida pelo usuário. Em instalações empacotadas, o Windows também pode
redirecionar esses arquivos para a área de dados do pacote.

As configurações e o banco permanecem armazenados até serem apagados pelo
usuário. A retenção e a rotação dos logs seguem as opções configuradas no
aplicativo. Cópias exportadas permanecem no destino escolhido e são de
responsabilidade do usuário. A desinstalação pode não remover arquivos
exportados ou salvos em locais personalizados.

## Credenciais e segurança

Tokens, chaves e outros segredos são armazenados localmente, sem criptografia,
no arquivo de configurações. Essa escolha permite que a organização administre
o arquivo com seus próprios controles de segurança. O usuário deve proteger sua
conta do Windows, restringir o acesso à pasta, preferir tokens de menor
privilégio e curta duração, revogar credenciais que não sejam mais necessárias
e nunca compartilhar o arquivo `settings.json`.

As integrações aceitam somente conexões HTTPS e oferecem controles de proxy,
validação de certificado, revogação, autoridade certificadora personalizada e
fixação de certificado quando configurados. Cabeçalhos sensíveis reservados são
bloqueados na área de cabeçalhos personalizados. Os logs são projetados para
não registrar credenciais intencionalmente; ainda assim, logs, banco, arquivos
importados e exportações devem ser revisados antes de serem compartilhados.

Nenhum método de armazenamento ou transmissão oferece segurança absoluta. A
proteção do computador, das pastas, das credenciais e dos ambientes conectados
é responsabilidade da organização que opera o aplicativo.

## Transmissão e compartilhamento

Quando o usuário testa ou sincroniza uma conexão, o aplicativo envia dados
diretamente ao endereço Dynatrace, AppDynamics ou provedor de autenticação
configurado. Esses serviços tratam os dados de acordo com os contratos e as
políticas aplicáveis à organização do usuário.

Um proxy corporativo configurado pode intermediar as conexões e observar dados
de rede. Caso a organização utilize inspeção TLS ou uma autoridade certificadora
personalizada, o conteúdo também poderá ser inspecionado conforme suas próprias
políticas.

O publicador não recebe os arquivos, credenciais, dados sincronizados, banco ou
logs por meio do aplicativo. Nenhum desses dados é vendido, alugado ou
compartilhado para publicidade. A Microsoft pode tratar dados de aquisição,
licenciamento, instalação e uso da Store conforme os termos e a política de
privacidade da própria Microsoft.

## Controles do usuário

O usuário pode:

- escolher os arquivos, pastas, ambientes e operações utilizadas;
- consultar, alterar ou remover conexões e credenciais nas configurações;
- definir nível, pasta, retenção e rotação dos logs;
- exportar o banco SQLite e excluir os arquivos locais;
- revogar tokens diretamente no Dynatrace, AppDynamics ou provedor de identidade;
- solicitar acesso, correção ou exclusão dos dados mantidos nas plataformas
  externas ao respectivo administrador ou fornecedor.

Para apagar os dados locais, feche o aplicativo e remova a pasta
`%LOCALAPPDATA%\A2DAlertMigrator`, além de eventuais pastas personalizadas e
cópias exportadas. Essa ação é irreversível e deve ser precedida de backup
quando necessário.

## Crianças

O aplicativo é uma ferramenta técnica destinada a profissionais e não é
direcionado a crianças. O publicador não coleta intencionalmente dados de
crianças por meio do aplicativo.

## Transferências internacionais

Os destinos e as regiões de processamento das conexões externas dependem dos
ambientes escolhidos pela organização. O usuário deve confirmar que o uso do
Dynatrace, AppDynamics, proxy e provedor de identidade atende às regras de sua
organização e à legislação aplicável.

## Alterações desta política

Esta política poderá ser atualizada quando as funções ou práticas de tratamento
de dados forem alteradas. A versão atualizada será apresentada na ficha do
produto na Microsoft Store, acompanhada da nova data de revisão.

## Contato

Questões sobre privacidade podem ser enviadas pelo canal de suporte informado
na ficha do A2D Alert Migrator na Microsoft Store. Não envie tokens, chaves,
senhas, arquivos de configuração, banco SQLite, logs completos ou respostas de
API sem antes remover informações confidenciais.

