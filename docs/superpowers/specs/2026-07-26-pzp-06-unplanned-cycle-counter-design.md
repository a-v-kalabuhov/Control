# PZP-06 — счётчик циклов на карточке ТПА в режиме «Работа без задания»

> Дата: 2026-07-26 · Клиент: ПЗП · Сложность: малая · Ветка: `feature/pzp-06-unplanned-cycle-counter`

## Проблема

На дашборде карточка ТПА показывает «Циклов: 0», когда станок работает **без задания**
(эффективный статус `Unplanned`). Станок реально гонит сироты-циклы (`ImmCycle.TaskId == null`),
но счётчик пуст → пользователь думает, что оборудование стоит. Психологический дискомфорт и
потеря сигнала «разберись, станок работает без задания».

Причина в коде: `ImmController.GetImmList` заполняет `ImmDto.CycleCount` только для ТПА с
активным заданием — группировка циклов по `TaskId == CurrentTaskId`
([ImmController.cs:98-120]). Без задания `CurrentTaskId == null` → блок не срабатывает, счётчик 0.

Фронтовая карточка (`ImmCard.vue:55`) рендерит `imm.cycleCount || 0` **безусловно** — она не
завязана на наличие задания. Значит достаточно, чтобы бэкенд заполнил поле.

## Акторы и сценарии (use cases)

- **UC-1 (Начальник цеха / наладчик смотрит дашборд).** ТПА работает без задания, идёт открытый
  эпизод `UnplannedRun`. На карточке ТПА в поле «Циклов» видно число сирот-циклов текущего эпизода
  (> 0) → понятно, что станок работает, просто задание не принято.
- **UC-2 (Задание принято / эпизод закрыт).** Как только наладчик принимает задание, эпизод
  `UnplannedRun` закрывается (`ClosedAt`), а новые циклы идут уже с `TaskId` активного задания →
  карточка показывает счётчик по заданию (существующее поведение). Сиротский счётчик больше не
  подмешивается.
- **UC-3 (Станок с заданием).** Поведение не меняется — счётчик по `CurrentTaskId`, как сейчас.

## Окно подсчёта (снятый открытый вопрос ТЗ)

Берём **текущий открытый эпизод `UnplannedRun`** (`ClosedAt == null`) — сущность и граница
введены в PZP-04 (ADR-0008). Счётчик = сироты-циклы окна эпизода: `TaskId == null && EndTime >=
run.StartTime`. Это тот же derive-on-read-субстрат, что `UnplannedRunController.ComputeAggregatesAsync`,
и ровно те циклы, которые PZP-04 потом переприсвоит заданию — счётчик работает как «живой превью».

Отвергнутые альтернативы:
- **«с начала смены/суток»** — нет привязки к эпизоду, подмешает чужие прогоны в пределах суток;
- **«все сироты ТПА за всё время»** — кумулятив без границы, бессмысленно большое число.

## Изменение (только бэкенд)

Один файл — `ImmController.GetImmList` ([ImmController.cs]). После существующего блока
task-based статистики добавляется блок для ТПА без активного задания:

1. Собрать `Id` ТПА, у которых `CurrentTaskId == null`.
2. Одним запросом найти их **открытые** эпизоды (`ClosedAt == null`), взять `StartTime`
   (на случай аномалии из нескольких открытых — брать `Max(StartTime)`, т.е. самый свежий).
3. Одним групповым запросом посчитать сироты-циклы этих ТПА (`TaskId == null`,
   `EndTime >= StartTime` соответствующего эпизода) и проставить `dto.CycleCount`.

Итого +2 запроса на весь список (не N+1). `AvgCycleTime` не трогаем — карточка его не биндит
(поле «Время цикла» берёт `currentCycleTime`, которого в списке нет; вне скоупа). Фронт без изменений.

Псевдокод вставки:

```csharp
var noTaskImmIds = imms.Where(i => !i.CurrentTaskId.HasValue).Select(i => i.Id).ToList();
if (noTaskImmIds.Count > 0)
{
    var openRuns = await _context.UnplannedRuns
        .Where(r => r.ClosedAt == null && noTaskImmIds.Contains(r.ImmId))
        .GroupBy(r => r.ImmId)
        .Select(g => new { ImmId = g.Key, StartTime = g.Max(r => r.StartTime) })
        .ToListAsync();

    if (openRuns.Count > 0)
    {
        var runImmIds = openRuns.Select(r => r.ImmId).ToList();
        var minStart  = openRuns.Min(r => r.StartTime);
        var orphanCycles = await _context.ImmCycles
            .Where(c => c.TaskId == null && runImmIds.Contains(c.ImmId) && c.EndTime >= minStart)
            .Select(c => new { c.ImmId, c.EndTime })
            .ToListAsync();

        foreach (var run in openRuns)
        {
            var count = orphanCycles.Count(c => c.ImmId == run.ImmId && c.EndTime >= run.StartTime);
            if (count > 0)
                imms.First(d => d.Id == run.ImmId).CycleCount = count;
        }
    }
}
```

## Тесты

Интеграционные на `GET /api/imm` (по образцу существующих в `Wintime.Control.Tests.Integration`):

1. ТПА без задания + открытый `UnplannedRun` + N сирот-циклов (`EndTime >= StartTime`) →
   `CycleCount == N`.
2. Эпизод закрыт (`ClosedAt` задан) → `CycleCount == 0` (сироты не подмешиваются в карточку).
3. Циклы принадлежат заданию (не сироты) → не задваиваются со счётчиком по заданию.
4. (граница) сирота-цикл с `EndTime < run.StartTime` не считается.

## Вне скоупа (YAGNI)

- Темп/прогноз окончания для беззадачных ТПА (нужен `TaskStartedAt`/план).
- Изменение `GetImmById` / `GetImmStatus` (это карточка **списка** дашборда, деталь не трогаем).
- `AvgCycleTime` для сирот (карточка поле не показывает).

## Связи

- **PZP-04 / ADR-0008** — источник сущности `UnplannedRun` и окна деривации; счётчик = превью
  переприсвоения.
- **BL-17** (превентивное уведомление «работа без задания») — тот же сигнал, другой канал.

[ImmController.cs]: ../../../Wintime.Control.API/Controllers/ImmController.cs
[ImmController.cs:98-120]: ../../../Wintime.Control.API/Controllers/ImmController.cs
[ImmCard.vue:55]: ../../../Wintime-Control-Frontend/src/components/dashboard/ImmCard.vue
