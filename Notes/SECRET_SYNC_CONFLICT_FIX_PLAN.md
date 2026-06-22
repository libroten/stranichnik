# Secret Sync Conflict Fix Plan

Этот документ описывает план исправления конфликтных сценариев вокруг
секретного мастер-пароля, secret reset и WebDAV-синхронизации.

Документ является рабочим заданием для агента. Если во время реализации
обнаружится более простое или безопасное решение, нужно обновить этот документ
и архитектурные заметки перед продолжением.

## Какую проблему решаем

В текущей модели есть несколько связанных рисков:

1. Две независимые смены мастер-пароля одной secret generation должны
   конфликтовать консервативно и не перетирать remote profile молча.
2. Два reset-а одной и той же secret generation должны быть идемпотентными:
   второй reset не должен создавать вечный конфликт, если remote reset event
   уже существует.
3. Reset одной generation должен всегда побеждать старые secret items,
   старые secret icons и старый crypto profile этой generation.
4. Две разные новые generation после независимых reset-ов не должны молча
   перетирать друг друга.
5. Secret bookmark не должен выводить свою sync generation только из текущего
   active crypto profile. Generation должна быть частью persisted item state.

Самый опасный текущий сценарий:

1. Клиент A и клиент B синхронизированы на generation `G0`.
2. A делает reset `G0`, задает новый пароль и создает secret bookmark в новой
   generation `GA`.
3. B независимо делает reset `G0`, задает новый пароль и создает secret
   bookmark в новой generation `GB`.
4. A синхронизируется первым.
5. B синхронизируется вторым.

Текущий риск: remote crypto profile `GA` может быть применен на B как active
profile и заменить локальный active profile `GB`. Так как локальные secret
items сейчас завязаны на `crypto_profile_id`, а не на собственную generation,
следующий push может ошибочно экспортировать локальные secret items B как будто
они относятся к generation `GA`, либо оставить локальные данные в состоянии,
которое трудно расшифровать.

## Почему это возникает

Смена мастер-пароля устроена через DEK/KEK:

- secret bookmark payloads шифруются DEK;
- мастер-пароль производит KEK;
- KEK только оборачивает DEK в crypto profile;
- смена мастер-пароля не меняет DEK и не переписывает secret bookmark payloads.

Это хорошо для обычной смены пароля, но создает тонкость для sync:

- `crypto_profiles.id` локально один и тот же (`ActiveProfileId`);
- remote identity crypto profile равен `secret_generation_id`;
- secret bookmark row хранит `crypto_profile_id`, но не должна полагаться
  только на текущий active profile для определения sync generation;
- после reset-а новый active profile снова получает тот же локальный id, но
  должен представлять уже другую `secret_generation_id`.

Из-за этого локальный `crypto_profile_id` недостаточен для безопасной
синхронизации secret item-ов между разными generation.

## Целевое поведение

### Обычная конкурентная смена мастер-пароля одной generation

Если два клиента независимо сменили пароль для одной и той же generation:

- первый успешно синхронизированный crypto profile становится remote-версией;
- второй клиент не должен молча перезаписывать remote profile;
- второй клиент должен получить конфликт/проблему по crypto profile;
- secret items той же generation не должны теряться;
- secret items, созданные после локальной смены пароля, можно сохранить, потому
  что DEK не менялся.

### Двойной reset одной generation

Если два клиента независимо сбросили одну и ту же generation:

- первый sync создает remote reset event;
- второй sync должен считать existing remote reset event достаточным
  подтверждением того же destructive intent;
- второй reset не должен приводить к постоянному конфликту;
- старые secret items этой generation не должны воскресать.

### Reset против смены пароля

Если один клиент сбросил generation, а второй только сменил пароль той же
generation:

- reset event побеждает;
- old-generation secret items, secret icons и crypto profile должны быть
  удалены/подавлены;
- secret items, созданные после простой смены пароля, но до получения reset-а,
  все еще относятся к старой generation и должны быть удалены после применения
  reset-а.

### Две разные новые generation

Если два клиента независимо сделали reset, задали новые пароли и создали новые
secret items:

- приложение не должно автоматически объединять эти две generation;
- remote profile другой generation не должен молча заменять локальный active
  profile, если у локального клиента есть unsynced secret data своей generation;
- sync должен остановиться на безопасном конфликте/отложенной проблеме;
- локальные secret items должны оставаться расшифровываемыми на своем клиенте;
- приложение не должно выгружать local secret items под чужой generation.

## Ключевые инварианты

1. У каждого secret bookmark должен быть persisted `secret_generation_id`.
2. `secret_generation_id` secret bookmark-а не должен вычисляться на push через
   текущий active profile, если он уже сохранен на самом item-е.
3. Secret item можно выгружать только если:
   - его generation не сброшена reset event-ом;
   - есть local crypto profile той же generation;
   - item payload был зашифрован ключом этой generation.
4. Remote crypto profile другой generation нельзя автоматически применять,
   если локально уже есть active profile другой generation и unsynced secret
   data.
5. Reset event одной generation является идемпотентным sync intent.
6. Reset event применяется до crypto profiles, secret icons и secret items той
   же generation.

## План реализации

### Шаг 1. Зафиксировать тестами текущее и целевое поведение

Добавить unit/integration tests до изменения логики, чтобы явно описать
ожидания.

Минимальные тесты:

- `SyncPullPlanner`:
  - dirty local crypto profile same generation + changed remote profile =>
    conflict;
  - local dirty reset event same generation + remote reset event exists =>
    semantic reset match, not conflict;
  - remote reset event for generation `G` suppresses remote profile/items/icons
    generation `G`;
  - remote crypto profile generation `GA` is not applied over local active
    dirty profile generation `GB`;
  - remote crypto profile generation `GA` may be applied if local generation
    `GB` is reset by a reset event in the same pull plan.
- `SqliteSyncLocalStore`:
  - applying remote reset purges secret items by item generation;
  - applying remote crypto profile with another generation does not overwrite a
    local active unsynced generation;
  - secret item push DTO uses item generation, not whatever active profile
    happens to be loaded after pull.
- End-to-end sync-style tests with in-memory WebDAV:
  - two password changes same generation;
  - two resets same generation;
  - reset first, password-change second;
  - password-change first, reset second;
  - double reset with two new generations.

If an existing test fails after the model change, first decide whether the test
encoded an outdated assumption or whether real behavior broke.

### Шаг 2. Добавить generation в модель secret item-а

Проверить фактическую SQLite-схему. Если `items.secret_generation_id` еще нет,
добавить новую миграцию. Если колонка уже есть в локальной экспериментальной
ветке, убедиться, что она используется во всех слоях.

Целевая форма:

```text
items.secret_generation_id TEXT NULL
```

Правила:

- `NULL` для обычных bookmark/folder rows;
- non-`NULL` только для secret bookmark rows;
- при создании secret bookmark значение берется из active crypto profile;
- при переводе normal -> secret значение ставится из active crypto profile;
- при переводе secret -> normal значение очищается;
- при применении remote secret item значение берется из
  `SyncItemDto.CryptoProfileSecretGenerationId`;
- при master-password change значение у items не меняется;
- при reset generation `G` удаляются secret items where
  `items.secret_generation_id = G`.

Файлы/места:

- `Storage/BookmarkItemRecord.cs` - добавить `SecretGenerationId`.
- `Storage/InMemoryBookmarkTreeStore.cs` - хранить и валидировать generation.
- `Storage/Sqlite/SqliteDatabaseMigrator.cs` - добавить migration при
  необходимости.
- `Storage/Sqlite/SqliteBookmarkTreeStore.cs` - read/write/upsert item generation.
- `Storage/Sqlite/SqliteBookmarkItemMapper.cs` - маппинг и проверки.
- `Security/SecretBookmarkProjectionService.cs` - сохранять generation при
  projection, если record меняется.
- `ViewModels/MainWindowViewModel.cs` и связанные code-behind paths - при
  создании/редактировании secret item передавать generation из active profile.
- `Notes/DATA_SCHEMA.md` - обновить схему.

### Шаг 3. Перестать вычислять secret item generation через active profile на push

Сейчас `SyncRemoteDtoMapper.ToSecretItemDto(...)` получает generation через
map `cryptoProfileId -> secretGenerationId`.

Нужно:

- брать generation из `BookmarkItemRecord.SecretGenerationId`;
- использовать crypto profile только как dependency validation:
  - profile generation должен совпадать с item generation;
  - если profile отсутствует, item нельзя выгружать;
  - если profile generation отличается от item generation, item нельзя
    выгружать, нужно пометить sync problem/blocked dependency.

Файлы/места:

- `Sync/Push/SyncRemoteDtoMapper.cs`
- `Sync/Push/SyncPushPlanner.cs`
- `Sync/Push/SyncPushService.cs`
- `Sync/Local/SyncItemSnapshotRecord.cs`, если потребуется расширить snapshot.

Алгоритм push для secret item:

1. Если item не secret - обычный путь.
2. Если item secret и `SecretGenerationId` пустой - не push, пометить локальную
   sync-проблему или conflict.
3. Если generation сброшена reset event-ом - не push.
4. Если crypto profile той же generation отсутствует - не push item, увеличить
   pending crypto profile count.
5. Если crypto profile есть и generation совпадает - создать DTO с
   `CryptoProfileSecretGenerationId = item.SecretGenerationId`.

### Шаг 4. Сделать reset event идемпотентным

Проблема: два локальных reset-а одной generation создают разные payload details
(`resetAtUtc`, `resetDeviceId`, etc.), но их смысл одинаковый: generation
сброшена.

Нужно добавить отдельную semantic-match ветку для reset events.

Целевое поведение:

- если remote reset event generation `G` существует;
- и local reset event generation `G` тоже существует;
- то local reset intent считается satisfied независимо от различий в
  `resetAtUtc` и `resetDeviceId`;
- если local reset event был dirty/never uploaded, его можно пометить clean с
  remote ETag/last synced timestamp;
- push не должен пытаться create-only upload второго reset file и получать
  вечный conflict.

Возможная реализация:

1. Расширить `SyncPullPlan` новым списком:

```csharp
IReadOnlyList<SyncPullSatisfiedResetEvent> SatisfiedResetEvents
```

2. В `SyncPullPlanner.PlanResetEvents(...)`:
   - если local reset event for generation exists and remote exists:
     - не добавлять remote reset в apply batch;
     - если local reset не clean или не имеет remote metadata, добавить в
       `SatisfiedResetEvents`.
3. В `ISyncLocalStore.ApplyPullPlan(...)`:
   - обработать `SatisfiedResetEvents`;
   - обновить sync metadata локального reset event на clean;
   - не менять destructive state, потому что reset уже локально применен.

Файлы/места:

- `Sync/Pull/SyncPullPlan.cs`
- `Sync/Pull/SyncPullPlanner.cs`
- `Storage/ISyncLocalStore.cs`
- `Storage/Sqlite/SqliteSyncLocalStore.cs`
- `Storage/Sqlite/SqliteSecretResetStore.cs`
- `Tests/SyncPullPlannerTests.cs`
- `Tests/SqliteSyncLocalStoreTests.cs`

### Шаг 5. Защитить apply remote crypto profile другой generation

Проблема: remote crypto profile identity = `secret_generation_id`, а local
SQLite profile row имеет постоянный `id = ActiveProfileId`. Если применить
remote profile другой generation через upsert по local id, можно заменить
локальную active generation.

Нужно сделать explicit policy.

Предлагаемая политика v1:

- Если локального active profile нет: remote profile можно применить.
- Если локальный active profile той же generation: использовать обычную
  dirty/clean/conflict логику.
- Если локальный active profile другой generation:
  - если локальная generation сбрасывается reset event-ом в этом же pull plan,
    remote profile можно применить после reset-а;
  - иначе remote profile нельзя применять автоматически.

Как хранить "нельзя применить":

Предпочтительный вариант: добавить dedicated deferred table, а не использовать
remote quarantine, потому что remote file валидный, просто небезопасный для
текущего локального состояния.

Возможная таблица:

```sql
sync_deferred_crypto_profiles
  secret_generation_id TEXT PRIMARY KEY
  remote_etag TEXT NULL
  content_hash TEXT NOT NULL
  canonical_json BLOB NOT NULL
  created_at_utc TEXT NOT NULL
  last_attempt_at_utc TEXT NULL
  attempt_count INTEGER NOT NULL DEFAULT 0
  last_error_code TEXT NULL
```

Минимальный вариант, если отдельная таблица слишком велика для первого шага:

- добавить в pull summary `PendingCryptoProfileCount`;
- не применять remote profile;
- хранить deferred remote object по аналогии с deferred secret items.

Не рекомендуется использовать `quarantined_remote_objects` для этого сценария:
remote object валидный, и кнопка "Удалить remote файл" может подтолкнуть
пользователя к опасному действию.

Файлы/места:

- `Sync/Pull/SyncPullPlanner.cs`
- `Sync/Pull/SyncPullPlan.cs`
- `Storage/Sqlite/SqliteSyncMetadataStore.cs`
- `Storage/Sqlite/SqliteSyncLocalStore.cs`
- `Sync/SyncRunSummary.cs`
- `Views/SettingsDialog.axaml.cs`, если нужно показать счетчик/сообщение.

### Шаг 6. Reset должен удалять secret data по item generation

После добавления item generation reset purge должен опираться на нее.

Правила:

- `DeleteSecretBookmarksForGeneration(G)` удаляет rows where
  `is_secret = 1 AND secret_generation_id = G`;
- secret-only folders удаляются после удаления secret items generation `G`;
- secret icon assets уже имеют `secret_generation_id` и удаляются по нему;
- active crypto profile удаляется только если его generation равна `G`;
- deferred secret items generation `G` удаляются;
- pending secret icon refs generation `G` очищаются, если они больше не могут
  быть применены.

Файлы/места:

- `Storage/Sqlite/SqliteSecretResetStore.cs`
- `Storage/InMemorySecretResetStore.cs`
- `Storage/Sqlite/SqliteSyncLocalStore.cs`
- `Tests/SecretMasterPasswordResetServiceTests.cs`
- `Tests/SqliteSecretResetStoreTests.cs`
- `Tests/SqliteSyncLocalStoreTests.cs`

### Шаг 7. Pull remote secret item должен использовать generation из DTO

В `SqliteSyncLocalStore.ApplyOrDeferRemoteSecretItem(...)` сейчас remote secret
item применяется только если active profile generation совпадает с
`CryptoProfileSecretGenerationId`.

Сохранить это правило, но дополнительно:

- при apply записывать `items.secret_generation_id` из DTO;
- если profile отсутствует, defer item с generation;
- если reset event для generation уже есть, удалить/defer-clean old item и не
  применять;
- если active local profile другой generation существует, но remote item
  относится к отсутствующей generation, не заменять profile неявно.

### Шаг 8. Улучшить push dependency blocking

Если crypto profile upload конфликтует или remote profile другой generation
отложен:

- все secret items этой generation должны быть skipped в этом run;
- skip должен быть диагностируемым через non-sensitive log:

```text
Sync push object skipped. Kind=Item; Reason=blocked-crypto-profile.
```

Не логировать item id, title, URL, generation id.

### Шаг 9. Обновить документацию

Обновить:

- `Notes/ARCHITECTURE.md`
- `Notes/DATA_SCHEMA.md`
- `Notes/ENCRYPTION_ARCHITECTURE.md`
- `Notes/SECRET_RESET_ARCHITECTURE.md`
- `Notes/WEBDAV_SYNC_ARCHITECTURE.md`
- `Notes/PROJECT_STATE.md`
- `Personal/SECRET_SYNC_CONFLICT_MANUAL_TESTS.md`, если фактическое целевое
  поведение изменится.

Документы должны явно фиксировать:

- secret item generation is persisted;
- reset events are idempotent by generation;
- reset wins over password change for the same generation;
- remote crypto profile of another active generation is deferred/blocked, not
  silently applied;
- advanced user-facing resolution for two independent new generations remains
  future work unless implemented.

## Риски и компромиссы

- Полноценное разрешение конфликта "две разные новые generation" требует UI
  выбора: оставить локальную generation, принять remote generation, экспортировать
  локальные secret items как копии, или сделать manual merge. Этот план только
  предотвращает тихую порчу данных.
- Добавление `items.secret_generation_id` требует аккуратной миграции старых
  баз. Для существующих secret items значение можно заполнить через связанный
  `crypto_profile_id`.
- Если в старой базе есть поврежденные secret items без profile, migration не
  должна падать без понятной причины. Лучше помечать такие rows как sync
  conflict/problem и не выгружать их.
- Нельзя логировать generation ids, reset ids, encrypted payloads, bookmark
  titles, bookmark URLs или local paths.

## Рекомендуемый порядок работы

1. Добавить unit tests на planner для reset idempotency и crypto profile
   generation mismatch.
2. Добавить `SecretGenerationId` в `BookmarkItemRecord` и storage mapping.
3. Обновить SQLite migration/read/write/apply paths.
4. Перевести push DTO mapping на item generation.
5. Добавить reset idempotency в pull plan/apply.
6. Добавить deferred/blocked handling для remote crypto profile другой
   generation.
7. Обновить reset purge по item generation.
8. Добавить end-to-end sync tests с in-memory WebDAV.
9. Обновить документацию.
10. Попросить пользователя прогнать:

```bash
dotnet build
dotnet test Tests/Stranichnik.Tests.csproj
dotnet format --verify-no-changes
```

11. Попросить пользователя вручную пройти ключевые сценарии из
    `Personal/SECRET_SYNC_CONFLICT_MANUAL_TESTS.md`.
