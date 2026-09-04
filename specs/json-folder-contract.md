# Contrato de pasta JSON

Cada arquivo JSON representa exatamente uma aplicação e contém uma ou mais
regras de alerta. O motor aceita dois modos de leitura:

1. **A2D JSON v1** — formato canônico validado diretamente pelo JSON Schema;
2. **JSON mapeado** — formato existente do AppDynamics, associado ao modelo
   canônico por um template visual de caminhos.

Depois da leitura, os dois modos produzem o mesmo `ApplicationImportDocument`.
Nenhuma decisão de DQL, detector ou implantação depende do formato de origem.

## 1. Seleção e descoberta

A tela de importação permite:

- escolher a pasta;
- habilitar recursão, desabilitada por padrão;
- escolher um template de mapeamento ou detecção automática do A2D JSON v1;
- visualizar arquivos aceitos, ignorados e bloqueados antes de continuar.

Arquivos são ordenados pelo caminho relativo com comparação ordinal. Somente a
extensão `.json`, sem distinção entre maiúsculas e minúsculas, é considerada.
Links simbólicos e reparse points são bloqueados. A ferramenta não segue atalhos
nem caminhos que escapem da pasta selecionada.

O conteúdo deve ser UTF-8 válido. A política de BOM é configurável: aceitar com
ou sem BOM (padrão), exigir BOM ou exigir ausência de BOM. O hash SHA-256 sempre
usa os bytes originais; somente o BOM aceito é removido antes do parsing.

Limites padrão:

| Limite | Padrão |
|---|---:|
| Tamanho por arquivo | 10 MiB |
| Arquivos por lote | 1.000 |
| Regras por aplicação | 5.000 |
| Regras totais | 50.000 |
| Profundidade JSON | 64 |
| Tamanho de uma DQL | 64 KiB |

Os limites são verificados antes de materializar coleções grandes. O usuário
pode reduzi-los, mas aumentos ficam visíveis no log da execução.

## 2. A2D JSON v1

O arquivo possui quatro elementos no nível raiz:

```json
{
  "$schema": "https://a2d.local/schemas/application-rules-v1.schema.json",
  "schemaVersion": "1.0",
  "application": {},
  "defaults": {},
  "rules": []
}
```

O schema executável está em
[`../schemas/application-rules-v1.schema.json`](../schemas/application-rules-v1.schema.json)
e um exemplo completo em
[`../samples/json/checkout.json`](../samples/json/checkout.json).

### Aplicação

`application.id` é a identidade estável e deve ser única em toda a pasta.
`application.name` é o nome humano. Descrição, responsáveis e labels são
metadados de auditoria e podem ser usados no relatório.

### Defaults

`defaults` reduz repetição. A precedência é:

```text
valor explícito da regra > default da aplicação > default seguro do motor
```

Os objetos `profile` e `schedule` são substituídos por inteiro quando aparecem
na regra; não existe merge profundo implícito. Depois da aplicação dos defaults,
o documento resolvido passa por todas as validações canônicas.

### Regras e alvos

Uma regra pode ter vários serviços em `targets`. Quando sinal, condição e
roteamento são iguais, essa é a forma preferida: o motor cria um detector, usa
`by: {dt.smartscape.service}` e inclui todos os IDs no mesmo filtro.

`groupId` permite consolidar regras diferentes do mesmo arquivo. Regras com o
mesmo `groupId` somente são unidas se todos os campos, exceto `id`, `name` e
`targets`, forem semanticamente equivalentes. Caso contrário, o grupo inteiro
recebe `GROUP_SEMANTIC_MISMATCH` e não é implantado.

Cada alvo usa:

```json
{ "selectorType": "ENTITY_ID", "value": "SERVICE-0123456789ABCDEF" }
```

ou:

```json
{ "selectorType": "NAME", "value": "checkout-service" }
```

Nomes são resolvidos no tenant e precisam resultar em exatamente um serviço.
Todos os filtros DQL finais usam `dt.smartscape.service` e convertem IDs com
`toSmartscapeId()`.

### Sinal

O sinal pode ser declarativo:

```json
{
  "kind": "METRIC",
  "metricKey": "dt.service.request.failure_count",
  "aggregation": "SUM"
}
```

ou fornecer DQL controlada:

```json
{
  "kind": "DQL",
  "expression": "timeseries ..."
}
```

A DQL fornecida não é confiada automaticamente. Ela precisa produzir
`timeseries`, usar `interval:1m`, usar somente serviços resolvidos, evitar
dimensões voláteis e passar pela validação Grail.

### Detector, evento, perfil e horário

`detector` representa modelo, condição e janelas de violação/recuperação.
`event` define nome, descrição, tipo e `alertGroup`. `profile` define o filtro de
notificação clássico. `schedule` diferencia horário de detecção
(`ACTIVE_WINDOW`) de horário de entrega (`NOTIFICATION_ONLY`).

Um `alertGroup` comum é recomendado para o detector e o Workflow responsável
por notificar a equipe. O manifesto registra todas as regras de origem que
contribuíram para cada detector consolidado.

## 3. JSON mapeado

Quando os JSON existentes não seguem o A2D JSON v1, o assistente solicita:

- caminho do ID da aplicação;
- caminho do nome da aplicação;
- caminho do array de regras;
- caminhos relativos de cada campo dentro de uma regra;
- conversões de enums, booleanos, números e unidades;
- caminhos opcionais para profile, horário e lista de serviços.

Exemplo conceitual:

```text
application.id        <- /application/id
application.name      <- /application/name
rules[]                <- /healthRules
rule.id                <- /id
rule.name              <- /name
rule.targets[].value   <- /affectedEntities/*/name
detector.threshold     <- /conditions/0/threshold/value
```

O template usa JSON Pointer e um curinga simples para arrays. Não aceita
JavaScript, expressões dinâmicas, chamadas de função, filtros executáveis ou
acesso a arquivos. Conversões são escolhidas de uma lista fechada e auditável.

O assistente é criado usando um arquivo representativo e testado contra todos os
arquivos da pasta. Diferenças de estrutura aparecem por arquivo e JSON Pointer.
O template recebe versão própria e hash; ambos entram no manifesto.

## 4. Validação e isolamento

As fases são:

```text
descobrir → limitar → ler bytes → calcular hash → validar JSON léxico
→ validar schema/mapeamento → normalizar → validar regras → planejar
```

O leitor rejeita propriedades duplicadas, mesmo que um parser JSON normalmente
aceitasse a última ocorrência. Campos desconhecidos no A2D JSON são erros para
detectar erros de digitação. Valores originais são preservados apenas no
snapshot/relatório; nunca são executados.

Diagnósticos previstos:

| Código | Escopo | Significado |
|---|---|---|
| `JSON_FILE_TOO_LARGE` | arquivo | Excede o limite antes da leitura |
| `JSON_PATH_OUTSIDE_ROOT` | arquivo | Caminho escaparia da raiz selecionada |
| `JSON_REPARSE_POINT` | arquivo | Link/reparse point não permitido |
| `JSON_SYNTAX_ERROR` | arquivo | JSON malformado, com linha e byte |
| `JSON_DUPLICATE_PROPERTY` | arquivo | Objeto contém chave repetida |
| `JSON_SCHEMA_UNSUPPORTED` | arquivo | `schemaVersion` não registrada |
| `JSON_SCHEMA_VIOLATION` | arquivo/regra | Campo, tipo ou restrição inválida |
| `JSON_MAPPING_MISSING_PATH` | arquivo/regra | Template não encontrou caminho obrigatório |
| `APPLICATION_ID_DUPLICATE` | lote | Mais de um arquivo representa a mesma aplicação |
| `RULE_ID_DUPLICATE` | aplicação | ID de regra repetido |
| `GROUP_SEMANTIC_MISMATCH` | grupo | Regras agrupadas não são equivalentes |
| `SOURCE_CHANGED_AFTER_IMPORT` | arquivo | Hash mudou antes da confirmação |

## 5. Unidade de aplicação e rollback

Cada aplicação é planejada isoladamente. A implantação padrão:

1. grava o snapshot e plano da aplicação no manifesto;
2. cria/atualiza perfis necessários;
3. cria/atualiza detectores desabilitados;
4. relê e compara todos os objetos;
5. habilita os detectores somente se a aplicação inteira estiver consistente;
6. marca a aplicação como concluída.

Se houver falha antes da habilitação, o motor reverte somente o que modificou
para aquela aplicação. Outras aplicações continuam conforme a política de erro
escolhida (`STOP_BATCH` ou `CONTINUE_NEXT_APPLICATION`). Não há transação remota
real; a atomicidade é implementada por staging, manifesto e compensação.

## 6. Interface

A tela da pasta apresenta uma árvore:

```text
Pasta selecionada
  ✓ checkout.json             12 regras · válida
  ! payments.json              8 regras · 2 avisos
  ✕ catalog.json               bloqueada · schema 2.0 não suportado
```

Ao selecionar um arquivo, a lateral mostra aplicação, hash, schema/template,
defaults resolvidos, regras e diagnósticos. A revisão final mantém agrupamento
por aplicação, e o progresso informa `aplicação 3/20 · regra 7/18`.
