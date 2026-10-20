# «Нет связи» доминирует — план реализации

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Сырой `Offline` всегда даёт эффективное состояние «Нет связи» и никогда не открывает авто-простой.

**Architecture:** Две чистые функции в `Wintime.Control.Core/Policies`: `ImmEffectiveStatus.Resolve`
(матрица эффективного состояния — общая для плитки, хронологии, отчётов, итогов задания) и
`DowntimeDecision.Evaluate` (решение воркера авто-простоев). Схема БД, API и фронт не меняются.

**Tech Stack:** .NET 9, xUnit + FluentAssertions.

**Spec:** `docs/superpowers/specs/2026-10-20-offline-dominates-design.md`

## Global Constraints

- Сырой `Offline` → `EffectiveStatus.Offline` при любом задании (`Setup`/`InProgress`/нет), любом открытом простое (авто или ручном) и любом `thresholdPassed`.
- Авто-простой по `Offline` не открывается; открытый авто-простой при `Offline` не закрывается.
- Существующие данные (`Events`) не трогаем — миграций нет.
- Остальные строки матрицы и правила воркера — без изменений.
- Ветка `fix/offline-dominates` (уже создана от `master`, спека закоммичена); `VERSION` 1.18.1 → 1.18.2.
- Тесты запускать из корня репо `E:\Projects\Control\Sources\Control`.
- Каждое сообщение коммита заканчивать строкой `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>` (через пустую строку).

---

### Task 1: `Offline` доминирует в матрице эффективного состояния

**Files:**
- Modify: `Wintime.Control.Core/Policies/ImmEffectiveStatus.cs`
- Test: `Wintime.Control.Tests.Unit/Policies/ImmEffectiveStatusTests.cs`
- Test: `Wintime.Control.Tests.Unit/Policies/EffectiveStatusTimelineTests.cs`

**Interfaces:**
- Consumes: —
- Produces: `ImmEffectiveStatus.Resolve(string rawMode, ActiveTaskStatus task, bool hasOpenDowntime, bool thresholdPassed) : string` — сигнатура не меняется, меняется только поведение для `Offline`.

- [ ] **Step 1: Обновить матрицу в `ImmEffectiveStatusTests`**

В `Resolve_MatchesMatrix` заменить три строки со старым поведением и добавить новые комбинации.
Было (строки 17, 22, 23):

```csharp
    [InlineData("Offline", ActiveTaskStatus.Setup,      false, true,  EffectiveStatus.Setup)]
```
```csharp
    [InlineData("Offline", ActiveTaskStatus.InProgress, false, true,  EffectiveStatus.Downtime)]    // offline дольше порога
    [InlineData("Offline", ActiveTaskStatus.InProgress, true,  false, EffectiveStatus.Downtime)]    // открыт простой
```

Стало:

```csharp
    [InlineData("Offline", ActiveTaskStatus.Setup,      false, true,  EffectiveStatus.Offline)]     // нет связи важнее наладки (ADR-0021)
```
```csharp
    [InlineData("Offline", ActiveTaskStatus.InProgress, false, true,  EffectiveStatus.Offline)]     // нет связи важнее порога (ADR-0021)
    [InlineData("Offline", ActiveTaskStatus.InProgress, true,  false, EffectiveStatus.Offline)]     // нет связи важнее простоя (ADR-0021)
```

И добавить в конец класса отдельный тест на все комбинации:

```csharp
    // ADR-0021: сырой Offline → «Нет связи» при любом задании, простое и пороге.
    [Theory]
    [InlineData(ActiveTaskStatus.Setup)]
    [InlineData(ActiveTaskStatus.InProgress)]
    [InlineData(ActiveTaskStatus.None)]
    public void Resolve_Offline_AlwaysOffline(ActiveTaskStatus task)
    {
        foreach (var hasOpenDowntime in new[] { false, true })
        foreach (var thresholdPassed in new[] { false, true })
            ImmEffectiveStatus.Resolve(ImmStatus.Offline, task, hasOpenDowntime, thresholdPassed)
                .Should().Be(EffectiveStatus.Offline,
                    $"task={task}, hasOpenDowntime={hasOpenDowntime}, thresholdPassed={thresholdPassed}");
    }
```

- [ ] **Step 2: Добавить тесты таймлайна в `EffectiveStatusTimelineTests`**

Перед закрывающей `}` класса (после `Build_GapAsNoData_ExplicitOfflineStaysOffline`):

```csharp
    [Fact]
    public void Build_OfflineInsideOpenDowntime_IsOffline()
    {
        // ПЗП 19→20.10: ТПА стоял (простой открыт), потом пропала связь. Простой в журнале
        // продолжается, но период без связи — «Нет связи», а не «Простой» (ADR-0021).
        var raw = new[]
        {
            new RawSegment(ImmStatus.Idle,    M(0),  M(20)),
            new RawSegment(ImmStatus.Offline, M(20), M(50)),
            new RawSegment(ImmStatus.Auto,    M(50), M(60)),
        };
        var tasks = new[] { new TaskInterval(ActiveTaskStatus.InProgress, M(0), M(60)) };
        var downtimes = new[] { new Interval(M(0), M(50)) };

        var result = EffectiveStatusTimeline.Build(raw, tasks, downtimes, M(0), M(60));

        result.Should().HaveCount(3);
        result[0].Should().BeEquivalentTo(new EffectiveSegment(EffectiveStatus.Downtime,   M(0),  M(20)));
        result[1].Should().BeEquivalentTo(new EffectiveSegment(EffectiveStatus.Offline,    M(20), M(50)));
        result[2].Should().BeEquivalentTo(new EffectiveSegment(EffectiveStatus.Production, M(50), M(60)));
    }

    [Fact]
    public void Build_OfflineDuringSetup_IsOffline()
    {
        var raw = new[]
        {
            new RawSegment(ImmStatus.Manual,  M(0),  M(30)),
            new RawSegment(ImmStatus.Offline, M(30), M(60)),
        };
        var tasks = new[] { new TaskInterval(ActiveTaskStatus.Setup, M(0), M(60)) };

        var result = EffectiveStatusTimeline.Build(raw, tasks, System.Array.Empty<Interval>(), M(0), M(60));

        result.Should().HaveCount(2);
        result[0].Should().BeEquivalentTo(new EffectiveSegment(EffectiveStatus.Setup,   M(0),  M(30)));
        result[1].Should().BeEquivalentTo(new EffectiveSegment(EffectiveStatus.Offline, M(30), M(60)));
    }
```

- [ ] **Step 3: Запустить тесты — должны упасть**

Run: `dotnet test Wintime.Control.Tests.Unit --filter "FullyQualifiedName~ImmEffectiveStatusTests|FullyQualifiedName~EffectiveStatusTimelineTests"`
Expected: FAIL — `Resolve_MatchesMatrix` (3 кейса: ожидается `Offline`, получено `Setup`/`Downtime`),
`Resolve_Offline_AlwaysOffline` (Setup, InProgress), `Build_OfflineInsideOpenDowntime_IsOffline`,
`Build_OfflineDuringSetup_IsOffline`.

- [ ] **Step 4: Реализация в `ImmEffectiveStatus.Resolve`**

Заменить тело метода `Resolve` (файл `Wintime.Control.Core/Policies/ImmEffectiveStatus.cs`) целиком:

```csharp
    public static string Resolve(string rawMode, ActiveTaskStatus task,
                                 bool hasOpenDowntime, bool thresholdPassed)
    {
        var mode = ImmMode.Normalize(rawMode);

        // Нет данных — нет ни «Работы», ни «Простоя», ни «Наладки» (ADR-0021, заменяет ADR-0019):
        // «Нет связи» доминирует над заданием, открытым простоем и порогом.
        if (mode == OfflineMode)
            return EffectiveStatus.Offline;

        var isAuto = mode == ImmMode.Auto;

        if (task == ActiveTaskStatus.Setup)
            return EffectiveStatus.Setup;

        if (task == ActiveTaskStatus.InProgress)
        {
            if (isAuto) return EffectiveStatus.Production;
            if (hasOpenDowntime || thresholdPassed) return EffectiveStatus.Downtime;
            return EffectiveStatus.Production; // до порога — «Stopped» растворён в Работу
        }

        // task == None
        return isAuto ? EffectiveStatus.Unplanned : EffectiveStatus.NoTask;
    }
```

- [ ] **Step 5: Запустить тесты — должны пройти**

Run: `dotnet test Wintime.Control.Tests.Unit --filter "FullyQualifiedName~ImmEffectiveStatusTests|FullyQualifiedName~EffectiveStatusTimelineTests"`
Expected: PASS, 0 failed.

- [ ] **Step 6: Commit**

```bash
git add Wintime.Control.Core/Policies/ImmEffectiveStatus.cs Wintime.Control.Tests.Unit/Policies/ImmEffectiveStatusTests.cs Wintime.Control.Tests.Unit/Policies/EffectiveStatusTimelineTests.cs
git commit -m "fix(status): Offline dominates setup and downtime in effective status"
```

---

### Task 2: Авто-простой не открывается по `Offline`

**Files:**
- Modify: `Wintime.Control.Core/Policies/DowntimeDecision.cs`
- Test: `Wintime.Control.Tests.Unit/Policies/DowntimeDecisionTests.cs`

**Interfaces:**
- Consumes: —
- Produces: `DowntimeDecision.Evaluate(...)` — сигнатура не меняется.

- [ ] **Step 1: Заменить тест старого поведения и добавить новые**

В `DowntimeDecisionTests` заменить тест `Offline_InProgress_PastThreshold_Opens` целиком на:

```csharp
    [Fact]
    public void Offline_InProgress_PastThreshold_DoesNotOpen()
    {
        // Обрыв связи — не простой: мы не знаем, что с ТПА (ADR-0021).
        var since = Now.AddHours(-23);
        var r = DowntimeDecision.Evaluate(
            ImmStatus.Offline, since, Now, ActiveTaskStatus.InProgress, Started.AddDays(-1),
            false, false, 120);

        r.Action.Should().Be(DowntimeAction.None);
    }

    [Fact]
    public void Offline_WithOpenAutoDowntime_KeepsItOpen()
    {
        // ТПА стоял, простой открыт, потом пропала связь: остановка была реальной,
        // обрыв её не отменяет — простой закрывается только возвратом в Auto (ADR-0021).
        var since = Now.AddSeconds(-600);
        var r = DowntimeDecision.Evaluate(
            ImmStatus.Offline, since, Now, ActiveTaskStatus.InProgress, Started,
            hasOpenAutoDowntime: true, hasOpenManualDowntime: false, thresholdSeconds: 120);

        r.Action.Should().Be(DowntimeAction.None);
    }

    [Fact]
    public void AfterReconnect_IdlePastThreshold_OpensAtIdleSince()
    {
        // Связь вернулась в Idle: порог считается от момента нового статуса, а не от обрыва.
        var idleSince = Now.AddSeconds(-200);
        var r = DowntimeDecision.Evaluate(
            ImmStatus.Idle, idleSince, Now, ActiveTaskStatus.InProgress, Started,
            false, false, 120);

        r.Action.Should().Be(DowntimeAction.Open);
        r.At.Should().Be(idleSince);
    }
```

- [ ] **Step 2: Запустить тесты — должен упасть один**

Run: `dotnet test Wintime.Control.Tests.Unit --filter "FullyQualifiedName~DowntimeDecisionTests"`
Expected: FAIL — `Offline_InProgress_PastThreshold_DoesNotOpen` (ожидается `None`, получено `Open`).
`Offline_WithOpenAutoDowntime_KeepsItOpen` и `AfterReconnect_IdlePastThreshold_OpensAtIdleSince`
проходят уже сейчас — они закрепляют неизменное поведение.

- [ ] **Step 3: Реализация в `DowntimeDecision.Evaluate`**

В `Wintime.Control.Core/Policies/DowntimeDecision.cs` после строки
`bool productionActive = task == ActiveTaskStatus.InProgress;` добавить:

```csharp
        bool isOffline = rawStatus == ImmStatus.Offline;
```

Комментарий и условие открытия заменить:

```csharp
        // Открытие: производство идёт, статус не-Auto дольше порога, и НЕТ открытого простоя
        // ЛЮБОГО вида (ни авто, ни ручного).
        if (productionActive && !isAuto && !hasOpenAutoDowntime && !hasOpenManualDowntime)
```

на:

```csharp
        // Открытие: производство идёт, статус не-Auto и не-Offline дольше порога, и НЕТ открытого
        // простоя ЛЮБОГО вида (ни авто, ни ручного). Обрыв связи — не простой (ADR-0021).
        if (productionActive && !isAuto && !isOffline && !hasOpenAutoDowntime && !hasOpenManualDowntime)
```

И обновить `<summary>` класса:

```csharp
/// <summary>
/// Решение об автоматическом простое для одного ТПА на момент опроса воркера.
/// Открывает простой, если задание InProgress и сырой статус не-Auto и не-Offline дольше порога
/// (обрыв связи — не простой, ADR-0021); закрывает открытый авто-простой при возврате в Auto
/// или выходе задания из InProgress (при Offline открытый простой не закрывается).
/// </summary>
```

- [ ] **Step 4: Запустить тесты — должны пройти**

Run: `dotnet test Wintime.Control.Tests.Unit --filter "FullyQualifiedName~DowntimeDecisionTests"`
Expected: PASS, 0 failed.

- [ ] **Step 5: Commit**

```bash
git add Wintime.Control.Core/Policies/DowntimeDecision.cs Wintime.Control.Tests.Unit/Policies/DowntimeDecisionTests.cs
git commit -m "fix(downtime): do not open auto downtime on Offline"
```

---

### Task 3: ADR-0021, CLAUDE.md, версия, полный прогон

**Files:**
- Create: `docs/adr/0021-offline-dominates.md`
- Modify: `docs/adr/0019-offline-with-task-is-offline.md:3`
- Modify: `CLAUDE.md:134-135`
- Modify: `VERSION`

**Interfaces:**
- Consumes: поведение из Task 1 и Task 2.
- Produces: —

- [ ] **Step 1: Создать `docs/adr/0021-offline-dominates.md`**

```markdown
# ADR-0021: «Нет связи» доминирует над всеми эффективными состояниями

- **Статус:** Accepted
- **Дата:** 2026-10-20
- **Supersedes:** [ADR-0019](0019-offline-with-task-is-offline.md)
- **Спека:** `docs/superpowers/specs/2026-10-20-offline-dominates-design.md`

## Контекст

ADR-0019 убрал подмену «нет данных» на «Работу», но оставил две другие: при задании `InProgress`
через порог (900 с) `DowntimeDetectionWorker` открывал авто-простой по сырому `Offline`, и плитка
становилась «Простоем»; при задании `Setup` `Offline` показывался «Наладкой».

Пилот ПЗП, 19→20.10.2026: пропала Ethernet-связь шлюз USR ↔ коннектор. Control записал `Offline` через
35 с, через порог открыл авто-простой, и 23 часа плитка ТПА-4 была красной «Простой». Никто не понял,
что пропали данные; связь восстановили только после ручного перезапуска коннектора. Цель статуса
«Нет связи» — чтобы пользователь заметил проблему и занялся ею.

## Решение

1. **Матрица (`ImmEffectiveStatus.Resolve`).** Сырой `Offline` проверяется первым и всегда даёт
   `Offline` — при любом задании (`Setup`/`InProgress`/нет), любом открытом простое (авто или ручном)
   и любом пороге. Остальная матрица без изменений. Действует везде (плитка, хронология, отчёты,
   итоги задания) и ретроспективно.
2. **Авто-простой (`DowntimeDecision.Evaluate`).** По `Offline` не открывается. Открытый до обрыва
   простой обрывом не закрывается — закрывается возвратом в `Auto` или выходом задания из `InProgress`.
3. **Данные.** Авто-простои, открытые раньше по `Offline`, остаются в журнале; на эффективное состояние
   они больше не влияют.

## Отвергнутые альтернативы

- **Порог «долгого» обрыва.** Любой порог прячет сигнал «нет данных» именно тогда, когда он нужен.
- **Ручной простой с причиной важнее `Offline`.** О связи система знает точно; причина видна в журнале.
- **Закрывать простой при обрыве.** Моргание связи резало бы остановку на две, а после восстановления
  новый простой открылся бы только через порог — 15 минут «Работы» на плитке.
- **Удалить миграцией старые авто-простои по `Offline`.** Пилот, таких событий немного — наладчик
  разберёт вручную.

## Последствия

- Обрыв связи виден на плитке как «Нет связи» всё время, пока он длится.
- В отчётах прошлые периоды «Простой»/«Наладка» без связи стали «Нет связи»; это время исключается из
  знаменателя эффективности.
- Журнал и Парето простоев больше не получают простоев без причины «по обрыву связи».
```

- [ ] **Step 2: Пометить ADR-0019 заменённым**

В `docs/adr/0019-offline-with-task-is-offline.md` строку 3:

```markdown
- **Статус:** Accepted
```

заменить на:

```markdown
- **Статус:** Superseded by [ADR-0021](0021-offline-dominates.md)
```

- [ ] **Step 3: Обновить CLAUDE.md**

В разделе «Статусы ТПА» заменить:

```markdown
Сырой `Offline` при задании `InProgress` без простоя — `Offline`, **не** `Production`: нет данных —
нет «работы» (иначе сбой сервера превращает период без связи в выработку). См. ADR-0019.
```

на:

```markdown
Сырой `Offline` → **всегда** `Offline` («Нет связи»): доминирует над наладкой, простоем (авто и ручным)
и порогом — нет данных, значит нет ни «работы», ни «простоя». Авто-простой по `Offline` не открывается;
открытый до обрыва — не закрывается. См. ADR-0021.
```

- [ ] **Step 4: Поднять версию**

Содержимое файла `VERSION`: `1.18.1` → `1.18.2`.

- [ ] **Step 5: Полный прогон тестов**

Run: `dotnet test Wintime.Control.Tests.Unit`
Expected: PASS, 0 failed.

Run: `dotnet test Wintime.Control.Tests.Integration`
Expected: PASS, 0 failed. (Интеграционные тесты отчёта и итогов задания не содержат `Offline`,
пересекающегося с простоем или наладкой, — правки в них не нужны. Если тест падает из-за Docker/БД
окружения, а не из-за утверждений, — сообщить, не править.)

- [ ] **Step 6: Commit**

```bash
git add docs/adr/0021-offline-dominates.md docs/adr/0019-offline-with-task-is-offline.md CLAUDE.md VERSION
git commit -m "docs(adr): ADR-0021 Offline dominates, supersedes ADR-0019; bump 1.18.2"
```

---

## После плана

PR `fix/offline-dominates` → `master` через `gh pr create` (push: `git push -u origin fix/offline-dominates`).
Не добавлять в коммиты `deploy/pzp/package/control.dump` (неотслеживаемый дамп БД заказчика).
