# Contrato canônico de importação

O usuário não precisa alterar o CSV original. O assistente visual associa os
cabeçalhos da origem aos campos abaixo. Um template de mapeamento registra
delimitador, encoding, cultura numérica, nomes das colunas e valores convertidos.

## 1. Identidade e controle

| Campo | Obrigatório | Exemplo | Observação |
|---|---:|---|---|
| `application_id` | sim | `checkout` | Agrupa as linhas na aplicação de origem |
| `application_name` | sim | `Checkout` | Nome humano da aplicação |
| `source_rule_id` | sim | `APPD-HR-1042` | Identidade estável da regra na origem |
| `source_rule_name` | sim | `Checkout - error rate` | Nome exibido e base do título |
| `detector_group_id` | não | `checkout-error-rate` | Consolida serviços sob um detector equivalente |
| `enabled` | sim | `true` | Aceita conversões configuradas no template |
| `description` | não | `Taxa de erro...` | Texto do evento e da auditoria |

## 2. Serviço e métrica

| Campo | Obrigatório | Exemplo | Observação |
|---|---:|---|---|
| `service_ref_type` | sim | `NAME` | `ENTITY_ID` ou `NAME` |
| `service_ref` | sim | `checkout-service` | Nome deve resolver exatamente um serviço |
| `metric_key` | sim* | `dt.service.request.failure_count` | Obrigatória sem `dql_override` |
| `aggregation` | sim* | `sum` | `avg`, `sum`, `min`, `max`, `count` etc. |
| `dql_override` | não | `timeseries ...` | Ainda sujeito a todas as validações obrigatórias |

`dql_override` é uma saída controlada para métricas ou expressões que o gerador
ainda não cobre. Ela não permite ignorar as regras `timeseries`, intervalo,
timeframe e escopo de serviço.

## 3. Detector

| Campo | Obrigatório | Exemplo | Observação |
|---|---:|---|---|
| `detector_model` | sim | `STATIC` | `STATIC`, `AUTO_ADAPTIVE`, `SEASONAL` |
| `condition` | sim | `ABOVE` | `ABOVE`, `BELOW`; `OUTSIDE` onde suportado |
| `threshold` | condicional | `5` | Unidade base da métrica; somente estático |
| `signal_fluctuations` | condicional | `2` | Auto-adaptive |
| `tolerance` | condicional | `4` | Seasonal |
| `violating_samples` | sim | `3` | Entre 1 e 60 |
| `sliding_window` | sim | `5` | Entre `violating_samples` e 60 |
| `dealerting_samples` | sim | `3` | Entre 1 e 60 |
| `alert_on_missing_data` | sim | `false` | Incompatível com janela de atividade no MVP |
| `event_type` | sim | `CUSTOM_ALERT` | Validado contra valores do schema |

## 4. Alerting profile

| Campo | Obrigatório | Exemplo | Observação |
|---|---:|---|---|
| `profile_name` | sim | `NOC - Pagamentos` | Perfil alvo/criado |
| `profile_severity` | sim | `ERROR` | Convertido para severidade Dynatrace |
| `profile_delay_minutes` | sim | `0` | Atraso antes da notificação |
| `profile_tag_filter` | não | `team:payments` | Filtro adicional, se necessário |

O perfil não é uma propriedade direta do detector. Ele filtra problemas para
notificações. A UI mostra essa separação e cria ambos os objetos, mas não afirma
que um detector está “anexado” ao perfil sem existir uma integração/notificação
que utilize esse perfil.

## 5. Horário

| Campo | Obrigatório | Exemplo | Observação |
|---|---:|---|---|
| `schedule_mode` | sim | `ACTIVE_WINDOW` | `ALWAYS`, `ACTIVE_WINDOW`, `NOTIFICATION_ONLY` |
| `schedule_timezone` | condicional | `America/Sao_Paulo` | Nome IANA, não abreviação como BRT |
| `active_days` | condicional | `MON,TUE,WED,THU,FRI` | Dias em inglês, separados por vírgula |
| `active_start` | condicional | `09:00` | Inclusivo |
| `active_end` | condicional | `18:00` | Exclusivo; pode atravessar meia-noite |

- `ALWAYS`: o detector avalia continuamente.
- `ACTIVE_WINDOW`: os pontos fora da janela são mascarados na DQL.
- `NOTIFICATION_ONLY`: o detector continua criando eventos; a entrega deve ser
  implementada por Workflow. O MVP identifica e separa essa necessidade.

## 6. Exemplo

O CSV precisa citar campos que contêm vírgulas. Como `active_days` contém
vírgulas, ele aparece entre aspas:

```csv
application_id,application_name,source_rule_id,source_rule_name,detector_group_id,enabled,service_ref_type,service_ref,metric_key,aggregation,detector_model,condition,threshold,violating_samples,sliding_window,dealerting_samples,alert_on_missing_data,event_type,profile_name,profile_severity,profile_delay_minutes,schedule_mode,schedule_timezone,active_days,active_start,active_end
checkout,Checkout,APPD-HR-1042,Checkout - error rate,checkout-error-rate,true,NAME,checkout-service,dt.service.request.failure_count,sum,STATIC,ABOVE,5,3,5,3,false,CUSTOM_ALERT,NOC - Pagamentos,ERROR,0,ACTIVE_WINDOW,America/Sao_Paulo,"MON,TUE,WED,THU,FRI",09:00,18:00
```

## 7. Validações de linha

Cada problema inclui código estável, severidade, linha, campo e correção. Alguns
códigos previstos:

| Código | Severidade | Significado |
|---|---|---|
| `CSV_REQUIRED_FIELD` | erro | Campo obrigatório ausente |
| `CSV_INVALID_NUMBER` | erro | Número não corresponde à cultura do template |
| `DUPLICATE_SOURCE_ID` | erro | ID repetido com conteúdo diferente |
| `SERVICE_NOT_FOUND` | erro | Serviço não existe no tenant |
| `SERVICE_AMBIGUOUS` | erro | Nome corresponde a mais de um serviço |
| `DQL_NOT_TIMESERIES` | erro | Resultado não é timeseries |
| `DQL_INTERVAL_REQUIRED` | erro | `interval:1m` ausente |
| `DQL_TIMEFRAME_FORBIDDEN` | erro | Query define `from`, `to` ou timeframe |
| `DQL_UNSTABLE_DIMENSION` | erro | Identidade contém dimensão volátil |
| `SCHEDULE_MISSING_DATA_CONFLICT` | erro | Horário conflita com alerta de ausência |
| `REMOTE_NAME_CONFLICT` | erro | Objeto homônimo não pertence à migração |
| `NO_RECENT_DATA` | aviso | Prévia não encontrou amostras recentes |

## 8. Questões que a amostra real precisa responder

- Qual é o delimitador e encoding usado no export do AppDynamics?
- Uma linha representa uma health rule completa ou uma condição da regra?
- Como o CSV identifica o serviço: tier, nome, ID, tag ou expressão?
- A métrica Dynatrace já vem informada ou será necessária uma tabela de
  equivalência AppDynamics → Dynatrace?
- O horário controla a detecção ou apenas o envio da notificação?
- Uma regra pode ter mais de uma janela/dia e mais de uma condição?
- O CSV já contém alerting profile, severidade e atraso de notificação?
