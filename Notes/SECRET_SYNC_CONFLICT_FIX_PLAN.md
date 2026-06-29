# Secret Sync Conflict Fix Plan

Этот документ фиксирует принятое решение и план реализации для крипто-конфликтов
при WebDAV-синхронизации секретных закладок.

Документ является рабочим заданием для агента. Если во время реализации
обнаружится, что принятое решение невозможно выполнить без опасных побочных
эффектов, нужно остановиться, описать проблему и согласовать новое решение с
пользователем.

## Принятое решение

Для текущей версии проекта крипто-конфликты не должны накапливаться как
долгоживущие `conflict`/`pending` состояния.

Правило:

> Если крипто-конфликт нельзя безопасно разрешить автоматически без запроса
> дополнительных данных от пользователя, WebDAV считается источником правды.

Пользователь выбирает только одно из двух:

1. подтвердить удаление локальных конфликтующих секретных данных и продолжить
   синхронизацию;
2. отказаться, тогда синхронизация прерывается без применения локальных
   изменений.

Не делаем:

- не спрашиваем старые/новые мастер-пароли для merge;
- не пытаемся автоматически переносить секреты между secret generation;
- не держим крипто-конфликты в состоянии "потом разберемся";
- не выгружаем локальные секреты, если remote reset/profile уже сделал их
  generation конфликтной;
- не трогаем обычные закладки и обычные иконки.

Первый простой вариант может удалять все локальное секретное целиком. Это
грубее, чем удаление только затронутой generation, но надежнее и понятнее для
пользователя на текущем этапе.

## Термины

- **Secret generation** - идентификатор поколения секретного хранилища.
- **Crypto profile** - запись с wrapped DEK и данными проверки мастер-пароля.
- **Password change** - изменение crypto profile той же generation.
- **Password reset** - удаление секретного хранилища generation и создание
  `secret_reset_event`.
- **Server truth** - состояние WebDAV-репозитория, которое приложение принимает
  как главное после подтверждения пользователя.

## Какие конфликты закрываем

### 1. Remote reset против локального состояния

Сценарии:

- B сбросил мастер-пароль и выгрузил `secret_reset_event`.
- A до синхронизации сменил пароль, создал секретные закладки или просто имеет
  локальные секреты той же generation.
- A запускает sync и получает remote reset event.

Ожидаемое поведение:

- sync не применяет reset молча, если локально есть секретные данные;
- показывается диалог подтверждения;
- при отказе sync прекращается без изменений;
- при подтверждении локальные секреты удаляются, remote reset считается
  принятым, sync продолжается;
- A не должен выгрузить секреты generation, которую remote уже reset-нул.

### 2. Remote crypto profile против локального dirty crypto profile

Сценарии:

- A и B независимо сменили мастер-пароль одной generation;
- один клиент выгрузил crypto profile первым;
- второй клиент запускает sync с локальным dirty crypto profile.

Ожидаемое поведение:

- sync видит, что remote profile уже изменен;
- показывается диалог подтверждения;
- при отказе sync прекращается без изменений;
- при подтверждении локальные конфликтующие секретные данные удаляются,
  серверный crypto profile принимается;
- локальный dirty crypto profile не перезаписывает remote profile.

### 3. Remote crypto profile другой generation

Сценарии:

- A и B независимо сбросили старое хранилище и создали разные новые generation;
- один клиент выгрузил свою новую generation первым;
- второй клиент запускает sync с локальной другой generation.

Ожидаемое поведение:

- sync не пытается автоматически объединить две generation;
- показывается диалог подтверждения;
- при отказе sync прекращается без изменений;
- при подтверждении локальные секреты удаляются, серверная generation
  принимается.

### 4. Два reset-а одной старой generation без новых локальных секретов

Если локальный reset и remote reset относятся к одной generation, и локально нет
секретных данных, которые нужно сохранять, это можно считать идемпотентным
случаем:

- пользовательский диалог не нужен;
- локальный reset intent можно считать удовлетворенным remote reset event;
- sync metadata локального reset event можно привести к clean.

Если есть новые локальные секреты после reset-а, но remote уже представляет
другую truth, применяем общий "server truth after confirmation" flow.

## UX

### Диалог для remote reset

Текст до действия:

> На другом устройстве был сброшен мастер-пароль для секретных закладок.
>
> Чтобы продолжить синхронизацию, Страничник должен удалить секретные закладки
> на этом компьютере, которые относятся к старому мастер-паролю. Обычные
> закладки не будут затронуты.
>
> Если отменить, синхронизация сейчас не будет выполнена.

Кнопки:

- `Удалить секретные закладки и продолжить`
- `Отмена`

Текст после подтверждения:

> Секретные закладки старого хранилища на этом компьютере удалены.
>
> Страничник принял сброс мастер-пароля с WebDAV и завершил синхронизацию.
> Обычные закладки не были затронуты.

### Диалог для remote password change / crypto profile conflict

Текст до действия:

> Мастер-пароль был изменен на другом устройстве раньше, чем изменения с этого
> компьютера попали на WebDAV.
>
> Секретные закладки, созданные или измененные здесь после локальной смены
> пароля, нельзя безопасно объединить с серверной версией. Чтобы продолжить
> синхронизацию, их нужно удалить с этого компьютера. Обычные закладки не будут
> затронуты.
>
> Если отменить, синхронизация сейчас не будет выполнена.

Кнопки:

- `Удалить локальные секретные изменения и продолжить`
- `Отмена`

Текст после подтверждения:

> Локальные секретные изменения, которые нельзя было синхронизировать, удалены.
>
> Страничник принял серверную версию секретного хранилища. Обычные закладки не
> были затронуты.

### Отказ пользователя

Если пользователь отказался:

> Синхронизация не выполнена.
>
> Локальные секретные закладки оставлены без изменений. Чтобы синхронизировать
> это устройство, нужно будет снова запустить синхронизацию и подтвердить
> удаление локальных конфликтующих секретных данных.

## Архитектурный подход

### Новый тип результата sync

Нужен промежуточный результат pull до применения destructive crypto-конфликта:

```csharp
enum SyncUserDecisionKind
{
    RemoteSecretReset,
    RemoteSecretPasswordChange
}
```

Варианты реализации:

- `SyncRunSummary` получает blocking reason `NeedsSecretConflictConfirmation`;
- или `SyncApplicationService.SyncNowAsync` возвращает специальный результат;
- или вводится отдельный exception/result type, который UI ловит и превращает в
  диалог.

Предпочтение: не использовать exception для нормального пользовательского
выбора. Лучше явный result.

### Где остановить sync

Останавливать нужно после чтения remote objects и построения pull plan, но до
`ISyncLocalStore.ApplyPullPlan(...)`.

Причина:

- к этому моменту уже понятно, есть ли remote reset/crypto conflict;
- локальная база еще не изменена;
- можно безопасно показать диалог и либо abort, либо выполнить destructive
  локальную подготовку и затем продолжить sync.

### Что считать требующим подтверждения

Подтверждение нужно, если remote состояние конфликтует с локальными секретными
данными и auto-resolve невозможен.

Сигналы:

1. `SyncPullPlan.ApplyBatch.SecretResetEvents` содержит reset generation, а
   локально есть secret items, secret icon assets, active crypto profile или
   dirty reset/profile state, которые будут затронуты.
2. Remote crypto profile той же generation конфликтует с локальным dirty crypto
   profile.
3. Remote crypto profile другой generation должен заменить локальную active
   generation, а локально есть secret data.
4. Pull planner уже обнаружил crypto-profile conflict.
5. Локальный dirty reset event был создан поверх старой версии crypto profile,
   а на WebDAV для той же generation уже лежит другой crypto profile. Для этого
   локальный reset хранит baseline `content_hash`/ETag старого crypto profile
   на момент сброса; несовпадение baseline с remote profile означает, что reset
   нельзя выгружать молча.

Не требовать подтверждение:

- remote reset generation уже clean локально;
- remote reset относится к generation, для которой локально нет secret data;
- локальный reset относится к generation, чей remote crypto profile все еще
  совпадает с baseline reset-а;
- свежая пустая локальная база просто скачивает remote crypto profile;
- remote objects совпадают по content hash.

### Как принять server truth

Первый простой вариант:

1. Удалить все локальные секретные bookmarks.
2. Удалить все локальные secret icon assets.
3. Удалить локальный active crypto profile.
4. Очистить deferred secret items и pending secret icon refs.
5. Очистить runtime secret session/cache.
6. Перезапустить sync с тем же remote состоянием или продолжить pull с уже
   прочитанным plan, если код позволяет сделать это безопасно.

Практически проще и надежнее:

1. Первый sync run возвращает `NeedsSecretConflictConfirmation`.
2. UI показывает диалог.
3. Если пользователь подтвердил, UI вызывает storage/service метод
   `AcceptRemoteSecretTruthAndClearLocalSecrets()`.
4. Затем UI запускает sync заново.

Так проще избежать полупримененного состояния и не нужно держать remote snapshot
между UI-await.

### Storage API

Добавить service/store метод, название можно уточнить:

```csharp
void ClearAllLocalSecretsForRemoteTruth(DateTimeOffset changedAtUtc);
```

Он должен:

- физически удалить secret bookmark rows;
- удалить папки, которые содержали только secret bookmark content, если текущая
  reset-логика уже умеет это делать безопасно;
- удалить secret icon assets;
- удалить active crypto profile;
- удалить local secret reset events или привести их к состоянию, которое не
  будет конфликтовать с remote truth;
- удалить deferred secret items;
- удалить pending secret icon refs;
- не трогать normal bookmarks, folders, regular icon assets и WebDAV settings.

Если проще переиспользовать существующую reset-логику, можно создать внутренний
variant reset-а без создания нового local reset event. Важно: при принятии
server truth нельзя создавать новый local reset event другой generation и потом
пытаться выгрузить его.

### Runtime state

После удаления локальных секретов:

- скрыть секреты;
- очистить runtime data key;
- очистить decrypted secret icon cache;
- обновить secret session configuration;
- перезагрузить дерево и search index.

## План реализации

### Шаг 1. Обновить модели результата sync

Добавить явный результат, который позволяет UI понять:

- sync остановлен из-за remote secret reset;
- sync остановлен из-за remote secret password change / crypto profile conflict;
- sync можно продолжать только после подтверждения удаления локальных секретов.

Файлы-кандидаты:

- `Sync/SyncRunSummary.cs`
- `Sync/SyncApplicationService.cs`
- `Sync/Pull/SyncPullService.cs`
- `Sync/Pull/SyncPullPlan.cs`

Не логировать generation id.

### Шаг 2. Расширить `SyncPullPlanner`

Planner должен вычислять crypto conflict confirmation request.

Добавить в `SyncPullPlan` поле вроде:

```csharp
SyncSecretConflictConfirmation? SecretConflictConfirmation
```

Где `SyncSecretConflictConfirmation` содержит только тип причины, без
пользовательских данных:

```csharp
enum SyncSecretConflictConfirmationReason
{
    RemoteSecretReset,
    RemoteSecretPasswordChange
}
```

Правила:

- remote reset + local affected secrets => `RemoteSecretReset`;
- dirty local crypto profile + changed remote profile => `RemoteSecretPasswordChange`;
- remote profile another generation + local secret data => `RemoteSecretPasswordChange`;
- если local affected secrets отсутствуют, confirmation не нужен.

### Шаг 3. Не применять pull plan при pending confirmation

`SyncPullService` должен:

1. построить plan;
2. если plan требует confirmation:
   - не вызывать `ApplyPullPlan`;
   - вернуть summary/result с blocking reason;
   - не запускать push;
   - залогировать non-sensitive причину.

Это исправит текущий баг, где reset начал применяться, затем apply упал на
`MatchedDirtyObjects`.

### Шаг 4. Добавить UI flow подтверждения

В `MainWindow.axaml.cs` / sync orchestration:

1. пользователь запускает sync;
2. sync возвращает `NeedsSecretConflictConfirmation`;
3. показать подходящий `ConfirmDialog` с текстом из этого документа;
4. если отказ:
   - показать информационный диалог "Синхронизация не выполнена";
   - оставить базу без изменений;
   - выставить sync status failure/cancelled;
5. если согласие:
   - вызвать clear-local-secrets service/store method;
   - показать post-action dialog;
   - запустить sync снова.

Важно: повторный sync должен проходить без повторного диалога, если локальные
секреты уже очищены.

### Шаг 5. Реализовать очистку локальных секретов

Добавить storage/service метод.

Файлы-кандидаты:

- `Storage/Sqlite/SqliteSecretResetStore.cs`
- `Storage/Sqlite/SqliteBookmarkTreeStore.cs`
- `Storage/Sqlite/SqliteSyncMetadataStore.cs`
- `Security/SecretSessionService.cs`
- `IconProcessing/BookmarkIconImageCache.cs` или существующий secret icon cache.

Первый вариант может быть грубым:

- удалить все local secret bookmark rows;
- удалить all secret icon assets;
- удалить active crypto profile;
- удалить deferred secret items;
- удалить pending secret icon refs;
- удалить local reset events, если они мешают принять remote truth;
- сохранить обычные данные.

### Шаг 6. Обновить push planner

Push должен дополнительно страховать:

- если локально есть reset event for generation, не пушить secret items/icons
  этой generation;
- если crypto profile generation конфликтует с remote truth или отсутствует,
  не пушить secret items;
- если sync summary указывает на unresolved secret confirmation, push вообще не
  запускается.

### Шаг 7. Тесты

Добавить/обновить тесты:

- `SyncPullPlannerTests`:
  - remote reset + local secret data => confirmation `RemoteSecretReset`;
  - remote reset + no local secret data => no confirmation;
  - dirty local crypto profile + changed remote profile =>
    confirmation `RemoteSecretPasswordChange`;
  - remote profile another generation + local secret data =>
    confirmation `RemoteSecretPasswordChange`;
  - remote reset and matched dirty same generation must not produce apply +
    matched metadata refresh combination that crashes.
- `SyncPullServiceTests`:
  - confirmation plan does not call local apply;
  - summary blocks push.
- `Sqlite...` tests:
  - clear local secrets removes secret bookmarks/icons/profile/deferred rows;
  - clear local secrets keeps normal bookmarks/folders/icons/settings.
- UI-level or service-level tests where possible:
  - refusal leaves storage unchanged;
  - confirmation clears local secrets and second sync can proceed.

### Шаг 8. Ручные проверки

Попросить пользователя проверить:

1. Reset на B, смена пароля + secret bookmark на A, первым sync делает B, потом
   A.
   - A показывает reset dialog;
   - отказ оставляет локальные секреты и sync не выполнен;
   - подтверждение удаляет локальные секреты;
   - повторный/продолженный sync принимает server truth.
2. Смена пароля на A и B одной generation.
   - второй клиент показывает password-change dialog;
   - отказ не меняет локальную базу;
   - подтверждение удаляет локальные конфликтующие секреты и принимает server
     truth.
3. Два reset-а одной generation без новых секретов.
   - второй sync проходит без лишнего диалога или с корректной идемпотентной
     обработкой.
4. Обычные закладки и обычные иконки не удаляются.
5. Секреты скрываются/lock state очищается после подтверждения.

### Шаг 9. Документация

После реализации обновить:

- `Notes/WEBDAV_SYNC_ARCHITECTURE.md`;
- `Notes/ENCRYPTION_ARCHITECTURE.md`;
- `Notes/PROJECT_STATE.md`;
- `Personal/SECRET_SYNC_CONFLICT_MANUAL_TESTS.md`, если ручной checklist
  расходится с новой логикой.

## Логирование

Логировать:

- secret conflict confirmation requested;
- confirmation reason kind;
- user accepted/rejected;
- local secret cleanup started/finished;
- повторный sync started after accepted server truth.

Не логировать:

- generation ids;
- bookmark/folder titles;
- bookmark URLs;
- encrypted payloads;
- source hashes;
- WebDAV URLs/usernames/passwords.

## Открытые вопросы

Открытые вопросы не блокируют первый грубый вариант:

- Нужно ли в будущем сохранять локальные конфликтующие секреты как export/recovery
  package перед удалением?
- Нужно ли удалять только affected generation вместо all local secrets?
- Нужен ли отдельный экран истории crypto-conflict decisions?

На текущем этапе ответ: не реализуем, чтобы не усложнять.
