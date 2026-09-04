# Banco local SQLite

O banco padrão fica em:

```text
%LOCALAPPDATA%\A2DAlertMigrator\data\a2d-alert-migrator.db
```

Em **Configurações > Banco de dados local (SQLite)** é possível alterar a pasta,
habilitar ou desabilitar WAL, definir o timeout de bloqueio, verificar a
integridade, abrir a pasta e exportar uma cópia.

WAL é o padrão recomendado para concorrência local. Com WAL ativo podem existir
arquivos temporários `-wal` e `-shm`; por isso, não copie somente o `.db` durante
uma gravação. Use **Exportar cópia**, que utiliza o backup consistente do SQLite.

Não execute o banco ativo diretamente em Google Drive, OneDrive ou pasta de
rede. Exporte o arquivo, envie a cópia e importe-o somente quando não estiver em
uso. Alterar o caminho nas configurações cria ou abre outro banco; o arquivo
anterior não é apagado nem movido automaticamente.

O esquema é versionado e registra o histórico das importações e dos sincronismos
com timestamps UTC, status e contagens. Os inventários de perfis e detectores de
anomalia preservam o JSON original retornado pela API para auditoria e marcam
como ausente o objeto que deixou de existir em um inventário administrativo
completo. Credenciais nunca são copiadas para o SQLite.

Os Davis Events são identificados por tenant e `event.id`. Novas consultas
atualizam estado, transição e término sem apagar eventos históricos que ficaram
fora do período selecionado. Cada execução registra período, limite e se o
resultado atingiu o teto de 5.000 registros.

Os problemas seguem a mesma identidade por tenant e `event.id`. Coleções de
entidades, serviços e eventos correlacionados são gravadas como JSON UTF-8 no
SQLite, enquanto os campos principais permanecem indexados para consulta local.
