# Sync Interruption Hardening Plan

Этот документ описывает план стабилизации WebDAV-синхронизации на случай
обрывов сети, закрытия приложения, падения процесса и частично примененных
состояний.

План является рабочим заданием для агента, а не неизменяемым контрактом.
Если во время реализации обнаружится более простой или надежный вариант,
архитектурное решение можно пересмотреть и обязательно зафиксировать в
документации.

## Цель

Сделать WebDAV sync устойчивее к обрывам, закрытию приложения и частичным
состояниям:

- после неидеального sync UI не должен показывать устаревшее дерево;
- локальные пользовательские операции не должны пересекаться с sync;
- pull должен применяться атомарнее;
- пропавшие remote-файлы не должны приводить к тихой потере данных;
- типовые interruption-сценарии должны быть покрыты тестами.

## Текущее состояние реализации

На текущем этапе реализованы основные защитные меры из этого плана:

- UI перезагружает дерево/поиск после любого завершенного sync summary,
  потому что pull мог успеть применить валидные изменения даже при итоговом
  проблемном результате.
- `SyncOperationGate` используется вокруг редакторов и коротких локальных
  write-операций, чтобы ручной sync не пересекался с add/edit/delete/move,
  DnD и настройками master password.
- Pull-план умеет обнаруживать clean local objects, которые ранее были
  синхронизированы, но теперь отсутствуют на remote, и помечает их dirty для
  повторной выгрузки.
- Применение pull-плана централизовано в `ISyncLocalStore.ApplyPullPlan(...)`.
  SQLite-реализация использует один общий `SqliteConnection` и
  `SqliteTransaction` для remote apply, matched dirty cleanup, conflict
  marking, quarantine updates, missing remote marking, pending icon refs,
  deferred secret items, reset events, icon assets, crypto profiles и items.
- Create-only upload использует временный объект в `.tmp/` и WebDAV `MOVE` в
  финальный путь. Если провайдер не поддерживает `MOVE`, транспорт
  откатывается к прямому create-only `PUT`.
- Update upload с ожидаемым ETag пока остается прямым `PUT` с условием,
  потому что перенос ETag-условий на WebDAV `MOVE` destination
  провайдеро-зависим и может быть менее надежным.
- UI-boundary sync code ловит ожидаемые local/storage-level ошибки и показывает
  обычную ошибку синхронизации, не превращая их в успешный summary.

Дополнительно добавлен rollback-тест: если применение remote batch падает в
середине, уже примененная часть batch-а не остается в SQLite.

## 1. Обновлять UI после любого sync, который мог изменить SQLite

### Проблема

Сейчас после `SyncNowAsync` вызывается
`RefreshSecretSessionConfigurationFromStorage()`, но если `summary.Succeeded ==
false`, метод выходит до `ReloadVisibleTreeAndSearch()`.

При этом pull уже мог применить часть валидных remote-изменений в SQLite, даже
если итоговый summary неуспешный из-за конфликтов, quarantined remote objects
или других проблем.

### Решение

В `MainWindow.ExecuteManualSyncAsync` после получения `summary` всегда вызывать:

```csharp
viewModel.RefreshSecretSessionConfigurationFromStorage();
viewModel.ReloadVisibleTreeAndSearch();
```

`LastSuccessfulSyncAtUtc` обновлять только если sync полностью успешный.

Если sync завершился с проблемами, вернуть ошибку для UI, но дерево уже должно
отражать текущее состояние локальной SQLite-базы.

### Статус

Реализовано.

### Проверка

- Положить на remote один валидный новый объект и один битый объект.
- Запустить sync.
- Sync должен вернуть проблемный результат.
- Валидный объект должен появиться в UI без перезапуска приложения.

## 2. Реально использовать `SyncOperationGate`

### Проблема

`SyncOperationGate` есть, но почти не используется вокруг UI write-операций.
Теоретически sync может пересечься с add/edit/delete/move или открытым
редактором.

Это может привести к состояниям, где sync snapshot снят до пользовательского
изменения, а SQLite меняется уже во время pull или push.

### Решение

В `MainWindow` обернуть editor dialogs в:

```csharp
_syncOperationGate.EnterEditorSession()
```

Пока открыто окно создания или редактирования закладки/папки, sync должен
возвращать `LocalOperationActive`.

Фактические локальные записи обернуть в:

```csharp
_syncOperationGate.EnterLocalWriteOperation()
```

Обернуть следующие операции:

- create bookmark;
- edit bookmark;
- delete bookmark;
- create folder;
- edit folder;
- delete folder;
- DnD/move;
- reset master password;
- изменение secret-state, если оно пишет storage.

Не держать `EnterLocalWriteOperation()` вокруг сетевых операций вроде metadata
fetch/favicon fetch. Только вокруг короткой локальной записи.

### Принятое решение

- `EnterEditorSession()` блокирует sync на весь срок жизни окна редактора.
- `EnterLocalWriteOperation()` блокирует sync только на короткую запись.

### Статус

Реализовано для editor dialogs, bookmark/folder create/edit/delete, DnD/move и
secret/master-password storage writes.

### Проверка

- Открыть диалог создания закладки.
- Нажать `Cmd+S` / `Ctrl+S`.
- Sync не должен стартовать.
- Результат должен быть `LocalOperationActive` или понятный UI-статус.
- Закрыть диалог и повторить shortcut: sync должен стартовать.

## 3. Сделать pull-apply атомарнее

### Проблема

`SqliteSyncLocalStore.ApplyRemoteChanges` применяет reset events, icons,
profiles и items отдельными операциями.

Если приложение упадет посередине, SQLite может остаться в частично примененном
состоянии.

### Решение

Ввести storage-level boundary для применения pull-плана.

Предпочтительный вариант:

```csharp
void ApplyPullPlan(SyncPullPlan plan, DateTimeOffset syncedAtUtc);
```

Или расширить существующий `ApplyRemoteChanges`, чтобы он принимал не только
`ApplyBatch`, но и:

- matched dirty objects;
- conflicts;
- quarantined objects;
- known quarantined objects;
- resolved quarantines.

Внутри SQLite применить это в одной транзакции настолько, насколько это
разумно возможно.

Если сразу трудно протащить одну транзакцию через все текущие store-классы,
сделать промежуточный шаг:

- сгруппировать `ApplyRemoteChanges + MarkUploaded + MarkConflict +
  quarantine updates` в одном методе `SqliteSyncLocalStore`;
- затем постепенно перенести внутренние операции на shared
  `SqliteConnection`/`SqliteTransaction`.

### Принятое решение

Цель не в том, чтобы откатить весь sync run. Цель в том, чтобы результат pull
либо применялся целиком, либо не применялся.

Push оставляем поштучным, потому что WebDAV не дает общей транзакции.

### Статус

Реализовано.

`ISyncLocalStore.ApplyPullPlan(...)` введен и используется как единая точка
применения pull-плана. Внутри него сгруппированы remote apply, matched dirty
cleanup, conflict marking, quarantine updates и missing remote marking.

SQLite implementation протаскивает shared `SqliteConnection`/`SqliteTransaction`
через задействованные store-операции, включая sync metadata, pending refs,
deferred secret items, quarantined remote objects, reset events, icon assets,
secret icon assets, crypto profiles и items.

### Проверка

- Simulated failure в середине apply не оставляет половину batch-а.
- Remote reset + related secret objects применяется атомарно.
- Quarantine updates не расходятся с применением pull batch.

## 4. Обрабатывать clean local object, который исчез на remote

### Проблема

Если локальный объект находится в состоянии clean, но соответствующий remote
JSON-файл исчез, push не обязан заново его загрузить.

Это может привести к тихой потере данных для нового устройства.

### Решение

Во время pull иметь remote inventory по всем категориям.

После чтения remote объектов сравнить:

- local clean objects with `remote_etag` / `last_synced_at_utc`;
- remote paths/ids, которые реально присутствуют на WebDAV.

Если local clean object отсутствует на remote:

- не удалять локально;
- пометить объект dirty, чтобы следующий push восстановил remote-файл;
- очистить старый `remote_etag`, потому что remote-файл уже отсутствует и
  следующий push должен идти как create-only upload, а не как update по
  устаревшему ETag.

### Исключения

- Если есть remote tombstone/reset event, применять обычную sync-логику.
- Если object локально deleted/tombstone и remote отсутствует, можно считать
  это нормальным состоянием или оставить clean, в зависимости от текущей
  tombstone policy.

### Иконки и зависимости

Для assets:

- если clean icon asset отсутствует, но используется item-ом, пометить asset
  dirty;
- item при необходимости должен быть догружен после asset.

Для secret crypto profile / secret icon:

- использовать аналогичное правило, если объект нужен живым secret item-ам.

Push planner не должен повторно загружать clean already-synced зависимости
только потому, что их использует dirty item. Это относится к parent folders,
regular icon assets, secret icon assets и crypto profiles. Если зависимость
действительно пропала на remote, pull сначала помечает ее dirty, и только после
этого push восстанавливает ее обычным правилом dirty-object upload.

Это особенно важно для WebDAV-провайдеров без стабильных ETag: clean synced
объект с `remote_etag = NULL` все равно считается представленным на remote,
если у него есть `last_synced_at_utc`.

### Статус

Реализовано в `SyncPullPlanner` и `SqliteSyncLocalStore.ApplyPullPlan(...)`.

Правило применяется только к объектам, которые уже были подтверждены remote
ранее (`last_synced_at_utc != NULL`). Локальные объекты, которые никогда не
были синхронизированы, не считаются "пропавшими на remote"; они попадают в
push через обычное правило first upload.

При переводе такого объекта обратно в dirty состояние SQLite-слой очищает
`remote_etag`, но сохраняет `last_synced_at_utc` и `content_hash`. Это
позволяет push восстановить файл через create-only upload.

Если remote-файл есть, но он invalid/quarantined, объект не считается
missing. Это не дает локальной копии затереть удаленный проблемный файл до
явного решения пользователя.

### Проверка

- Загрузить все на WebDAV.
- Удалить один clean `items/*.json` на сервере.
- Запустить sync.
- Sync должен заново выгрузить этот item, а не молча оставить remote неполным.

## 5. Улучшить upload atomicity через temp + MOVE

### Проблема

Сейчас PUT идет сразу в финальный `*.json`.

Если WebDAV-провайдер оставит частичный файл при обрыве, следующий pull увидит
битый JSON или content hash mismatch и отправит файл в quarantine.

### Решение

Добавить в transport метод вида:

```csharp
PutJsonAtomicallyAsync(finalPath, bytes, expectedEtag, createOnly)
```

Алгоритм:

1. PUT во временный путь, например `.tmp/<guid>.json`.
2. WebDAV `MOVE` во final path.
3. Для create/update использовать условия осторожно:
   - create-only: финальный объект не должен существовать;
   - update: должен совпасть expected ETag.
4. Если MOVE не поддерживается провайдером, fallback на текущий PUT.

Этот этап делать после пунктов 1-4, потому что WebDAV MOVE у разных
провайдеров может вести себя по-разному.

### Проверка

- Transport tests на MOVE success.
- Fallback path.
- Конфликт при существующем final object.
- Cleanup временного файла при ошибке, если возможно.

### Статус

Реализовано для create-only upload.

Remote layout теперь содержит служебный каталог `.tmp/`. Новый объект сначала
загружается туда, затем переносится в финальный путь через `MOVE` с
`Overwrite: F`. Если `MOVE` возвращает "не поддерживается", используется
fallback на прямой create-only `PUT`. При ошибках транспорт пытается удалить
временный объект.

Update upload с expected ETag пока остается прямым `PUT` с условием. Не
переносить его на temp+MOVE без отдельного проектирования, потому что не все
WebDAV-провайдеры одинаково надежно поддерживают precondition-семантику для
destination path при `MOVE`.

## 6. Улучшить обработку unexpected exceptions

### Проблема

UI ловит `HttpRequestException` и `TaskCanceledException`, но
SQLite/JSON/unexpected ошибки могут всплыть слишком грубо.

### Решение

В `ExecuteManualSyncAsync` добавить обработку ожидаемых storage-level ошибок
там, где это не скрывает баги.

Логировать без пользовательских данных.

UI должен показывать нейтральное сообщение:

```text
Не удалось выполнить синхронизацию.
```

### Осторожность

Не ловить бездумно `Exception` глубоко внутри sync engine.

Лучше ловить на UI boundary, логировать тип ошибки без payload/details и не
превращать ошибку в success summary.

### Статус

Реализовано на UI boundary для ожидаемых local/storage-level ошибок.

## 7. Добавить interruption-like тесты

Минимальный набор тестов:

- pull read падает до apply: local store не меняется;
- apply падает в середине: после атомаризации local store не частично изменен;
- push падает после successful remote PUT, но до local `MarkUploaded`:
  следующий pull marks dirty object clean по matching content hash;
- push падает после upload parent, до child: следующий push догружает child;
- clean local object missing remotely становится dirty и восстанавливается;
- sync while editor open returns/postpones with local operation active.

### Статус

Добавлены тесты для:

- missing remote clean-object planning;
- игнорирования never-synced clean objects в missing-remote логике;
- игнорирования invalid/quarantined remote object как "missing";
- service-level применения missing-remote dirty mark;
- create-only temp PUT + MOVE;
- MOVE fallback;
- cleanup temp object при MOVE conflict.

Тест на single-transaction atomic pull apply добавлен: batch с валидным первым
remote object и ошибочным вторым remote object откатывается целиком.

## Рекомендуемый порядок работ

1. UI reload after non-successful sync.
2. Реальное применение `SyncOperationGate`.
3. Тесты на текущие interruption-сценарии, чтобы зафиксировать ожидания.
4. Atomic pull apply.
5. Missing remote clean-object detection.
6. Temp + MOVE upload.
7. Финальный review документации:
   - `Notes/WEBDAV_SYNC_ARCHITECTURE.md`;
   - `Notes/PROJECT_STATE.md`.

## Приоритеты

Самый быстрый и полезный первый фикс:

- пункт 1.

Самый важный архитектурный фикс:

- пункт 3.

Самый вероятный источник странных багов при реальном использовании:

- пункт 2.

## Завершенный hardening-этап

После ревью текущего этапа дополнительно закрыты точечные риски:

- reset generation больше не планирует восстановление missing remote для
  старых secret item-ов той же generation;
- после каждого pull apply выполняется reconciliation уже существующих pending
  icon refs и deferred secret items;
- invalid remote objects получают raw content hash для стабильного known
  quarantine поведения даже без ETag;
- production WebDAV transport чистит stale `.tmp/` файлы с консервативным
  retention-порогом;
- удаление quarantined remote file репортит финальное состояние в sync activity.

Последний закрытый шаг: полная SQLite-атомаризация `ApplyPullPlan(...)`.

Реализовано:

1. Выделены transaction-aware внутренние методы для apply items/assets/profiles,
   sync metadata, conflict/quarantine updates, pending refs, deferred secret
   items и missing-remote dirty marks.
2. Один `SqliteConnection` и `SqliteTransaction` передается через весь pull
   apply.
3. Добавлен тест, который искусственно роняет apply в середине и проверяет,
   что локальная база не осталась в частично примененном состоянии.
