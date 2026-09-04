# HTTP Client remoto

Em **Configurações > Configuração do HTTP Client**, defina os limites de tempo,
as novas tentativas com intervalo progressivo, a renovação das conexões e o
limite de conexões simultâneas. As URLs pertencem às seções específicas do
Dynatrace e do AppDynamics.

TLS usa a confiança do Windows por padrão. Para redes corporativas, prefira
**CA corporativa** ou **Pin SHA-256**. **Ignorar validação** existe apenas para
diagnóstico temporário e mostra um alerta na tela. Redirecionamentos e cookies
ficam desativados para reduzir vazamento de credenciais.

Cabeçalhos não sensíveis podem ser escritos como `Nome: valor`, um por linha.
`Authorization`, cookies e cabeçalhos de transporte são bloqueados porque
`settings.json` não é um cofre.

Em **Testes de conexão e API**, Dynatrace e AppDynamics possuem painéis
independentes. Cada um aceita URL completa, GET ou HEAD, autenticação e status
esperado. Dynatrace oferece API token ou Bearer/OAuth; AppDynamics oferece
Bearer/OAuth ou Basic legado. O resultado informa transporte, status, latência
e versão HTTP.

Os testes são exibidos em cartões verticais. O resultado usa verde para sucesso,
amarelo para respostas válidas que exigem atenção e vermelho para falhas. Códigos
`3xx`, incluindo `307`, são classificados como redirecionamento e mostram a URL
de destino. O aplicativo não segue o redirect automaticamente para impedir o
reenvio de uma credencial a um host não confirmado.

Para Dynatrace, **Platform Token ou OAuth (Bearer)** é a opção padrão. A URL-base
validada para o ambiente SaaS é:

```text
https://<environment-id>.live.dynatrace.com
```

Para testar autenticação e leitura de perfis pela Environment API V2, use:

```text
https://<environment-id>.live.dynatrace.com/api/v2/settings/objects?schemaIds=builtin:alerting.profile&pageSize=1
```

O resultado esperado é HTTP 200 com JSON. O Platform Token precisa do escopo
`settings:objects:read`, além da permissão correspondente do usuário associado.
A URL-base isolada valida o acesso ao host, mas não comprova permissão na API.
Selecione **API Token legado** somente para endpoints que exigem
`Authorization: Api-Token`.

Os tokens dos testes avulsos desta página permanecem apenas na memória. As
chaves cadastradas nas páginas **Dynatrace** e **AppDynamics** são persistidas
em texto simples no `settings.json`, por decisão explícita do projeto. Os logs
não recebem URL completa, usuário ou segredo.

Em **Configuração de proxy**, escolha entre as configurações do Windows, uma
conexão direta ou um servidor de proxy específico. Esta configuração vale para
todas as integrações remotas.

Implementação: `HttpClient`/`SocketsHttpHandler` com
`Microsoft.Extensions.Http.Resilience` 10.9.0. O pacote oficial substitui a
integração antiga `Microsoft.Extensions.Http.Polly`.
