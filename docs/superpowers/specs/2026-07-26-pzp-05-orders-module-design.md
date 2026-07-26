# PZP-05 — Модуль учёта заказов

- **Дата:** 2026-07-26
- **Задача:** PZP-05 (спринт ПЗП «фундамент заказов», срок 30 июля)
- **Статус:** дизайн утверждён, готов к плану реализации
- **Prerequisite:** PZP-09 (`ProductType` + `Mold.ProductTypeId`) — выполнен (PR #89, ADR-0007)

## Проблема

В системе нет уровня **над** заданиями. Иерархия сейчас: `ShiftTask` (задание) → `ImmCycle`
(выпуск). Нет сущности «Заказ», под которую катится выпуск нескольких заданий по одной
номенклатурной позиции. Из-за этого нельзя:

- вести заказы (номер, срок, требуемое количество) и видеть их **прогресс** по факту выпуска;
- отчитываться «что выполнено / что в работе»;
- построить вертикаль **Заказ → Задание → Партия → Изделие** (фундамент под BL-27 и PZP-07).

PZP-05 вводит сущность **`Order`** и связь `Order → ShiftTask (1:N)`. Заказ = **одна**
номенклатурная позиция (`ProductType` + количество). Прогресс считается **на чтении** из
уже существующего инкрементального поля `ShiftTask.ActualQuantity`. Добавляется учёт **брака**
на задании (ручной), чтобы прогресс заказа считался по **годным** деталям.

## Решения (зафиксированы при брейнсторминге 2026-07-26)

1. **Заказ = одна номенклатурная позиция** (`ProductType` + `Quantity`), НЕ многострочный.
   Многострочный коммерческий заказ из CRM раскладывается на позиции = отдельные `Order`.
2. **Номер заказа (`Number`) — НЕ уникален.** Одна пара `(Number, OrderDate)` может повторяться:
   по строке на каждую позицию одного коммерческого заказа. Идентичность `Order` — суррогатный
   `Id` (Guid). Никаких unique-constraint на `Number`. (Отличие от `ProductType.Article`, где
   уникальность есть.)
3. **`Order → ShiftTask = 1:N`**, `ShiftTask.OrderId` — **nullable**. Большой заказ исполняется
   параллельно несколькими заданиями на разных ТПА/ПФ. Привязка задания к заказу **опциональна** —
   модуль заказов по сути отключаемый (будущий feature-flag ROS-07; см. «Границы объёма»).
4. **Прогресс — derive-on-read (Вариант A).** На `Order` прогресс НЕ хранится. Считается запросом
   `Σ` по привязанным заданиям в момент чтения. `ActualQuantity` уже live-инкрементальное
   (`TaskOutputHandler` + правки переназначения сирот PZP-04) → второй источник правды не нужен.
   Тот же паттерн, что `UnplannedRun` (PZP-04). Отвергнут Вариант B (хранимый счётчик на `Order`,
   обновляемый из Стадии-2 хендлера) — дублирование правды, риск изоляции `DbContext`, лишняя
   синхронизация при привязке/отвязке; заказов немного, `Σ` по индексу `OrderId` тривиальна.
5. **Завершение заказа — вручную менеджером, при достаточном выпуске.** Нет авто-перехода
   (устойчиво к браку/перевыпуску, не нужен хук в конвейере циклов). Завершить (`Active → Completed`)
   можно **только если выпущено достаточно годных** (`Σ(Actual − Defect) ≥ Quantity`); при недостатке
   заказ можно лишь **отменить**. Статусы: `Active` / `Completed` / `Cancelled`. Отменённый скрыт из
   отчётов «выполняемых»; заказ физически не удаляется. Гард завершения — в доменном методе
   `Order.Complete(goodQuantity)`.
6. **Учёт брака — новое поле `ShiftTask.DefectQuantity` (ручной ввод).** Брака как величины в
   системе не было (`TaskOutputHandler` считает только успешные циклы, `ImmCycle.IsSuccessful=false`
   = **авария**, не QC-брак). Оператор/наладчик указывает брак **при завершении задания**. Поле —
   зерно для полноценного контроля качества РОСОМС (ROS-05).
7. **Два уровня «выполнения» (ключевая семантика):**
   - **Выполнение ЗАДАНИЯ** = выпущено `PlanQuantity` (по `ActualQuantity`, **независимо** от брака).
     Компенсация брака — существующей функцией PZP-08 «Продолжить выпуск» (`AddPlannedQuantity`):
     наладчик проверяет выпуск на брак и добавляет в план недостающее.
   - **Выполнение ЗАКАЗА** = выпущено `Quantity` **годных** деталей:
     `Σ(ActualQuantity − DefectQuantity) ≥ Quantity`. Брак заказчику не отправляется, поэтому в
     прогресс заказа идут только годные. Прогресс **не капается** — `>100%` допустим.
8. **Жёсткая валидация изделия при привязке.** Привязать к заказу можно только задание, у которого
   `task.Mold.ProductTypeId == order.ProductTypeId` (иначе 400). ПФ без типа привязать нельзя
   (изделие не определить). Привязка возможна только к заказу в статусе `Active`.
9. **ADR — пишем** (ADR-0009): новая сущность и контракт (заказ = 1 позиция, номер неуникален,
   прогресс derive-on-read по годным, ручной брак, жёсткая валидация изделия, ручное завершение).

## Акторы и сценарии (use cases)

**Акторы:**

- **Менеджер / Админ** — ведёт заказы (CRUD), привязывает/отвязывает задания, завершает/отменяет/
  возобновляет заказы, смотрит прогресс. Единственный, кто пишет по заказам (`Admin,Manager`).
- **Наладчик / Оператор (Adjuster)** — при **завершении задания** вводит брак (`DefectQuantity`).
  Заказы не ведёт и не видит (заказы — десктоп менеджера).
- **Система** — валидации (изделие, статус заказа, диапазон брака), derive-on-read прогресса,
  корректная работа UTC (Npgsql).

**Сценарии** (основной поток → альтернативы-исключения → endpoint / тест):

| # | Сценарий | Основной поток | Альтернативы (исключения) | Endpoint / тест |
| --- | --- | --- | --- | --- |
| UC-1 | Менеджер создаёт заказ | номер + даты + изделие + кол-во → заказ `Active` | `Quantity ≤ 0` → 400; `ProductType` не существует/архивный → 400; `Number` пустой → 400 | `POST /api/orders` · тест create/400 |
| UC-2 | Менеджер редактирует заказ | правит номер/даты/кол-во/примечание → сохранено | смена `ProductTypeId` при наличии привязанных заданий → 409 | `PUT /api/orders/{id}` · тест lock-producttype |
| UC-3 | Менеджер завершает заказ вручную | выпущено годных `Σ(Actual − Defect) ≥ Quantity` → `Active → Completed` | заказ не `Active` → 400; **годных `< Quantity` → 400** (заказ можно только отменить) | `POST /api/orders/{id}/complete` · тесты guard/insufficient |
| UC-4 | Менеджер отменяет заказ | `Active → Cancelled`, скрыт из «выполняемых» | заказ не `Active` → 400 | `POST /api/orders/{id}/cancel` · тест guard |
| UC-5 | Менеджер возобновляет заказ | `Completed/Cancelled → Active` | заказ уже `Active` → 400 | `POST /api/orders/{id}/reopen` · тест guard |
| UC-6 | Менеджер привязывает задание к заказу | из формы задания (селект) ИЛИ карточки заказа → `OrderId` установлен | заказ не `Active` → 400; `Mold.ProductTypeId ≠ order.ProductTypeId` → 400; ПФ без типа → 400 | `POST /api/orders/{id}/tasks`, `POST /api/tasks/{id}/set-order` · тесты mismatch/inactive |
| UC-7 | Менеджер отвязывает задание | **только из карточки заказа** → `OrderId = null` (всегда разрешено) | из формы/карточки задания отвязка недоступна | `DELETE /api/orders/{id}/tasks/{taskId}` · тест detach |
| UC-8 | Менеджер создаёт/ведёт задание без заказа | `OrderId = null` → задание работает как сейчас | — (модуль опционален) | `POST /api/tasks` · тест nullable-order |
| UC-9 | Оператор/наладчик завершает задание с браком | вводит брак → `DefectQuantity` сохранён на задании | `Defect < 0` или `Defect > ActualQuantity` → 400 | `POST /api/tasks/{id}/complete` · тест defect-range |
| UC-10 | Менеджер смотрит список заказов | таблица: заказано / выпущено / брак / **годных** / прогресс % / статус; фильтры статус, поиск по номеру, изделие | — | `GET /api/orders` · тест list-progress/GroupBy |
| UC-11 | Менеджер открывает карточку заказа | реквизиты + привязанные задания + разбивка (заказано/выпущено/брак/годных/прогресс) | — | `GET /api/orders/{id}` · тест details |
| UC-12 | Прогресс отражает переназначение сирот | менеджер переназначил сироты-циклы (PZP-04) → `ActualQuantity` задания изменился → прогресс заказа пересчитан на чтении | — | `GET /api/orders/{id}` · тест progress-after-reassign |

Сложность фичи — в UC-6 (валидация изделия + статуса), UC-9 (диапазон брака) и UC-7/UC-10/UC-12
(прогресс по годным, derive-on-read, устойчивость к переназначению), а не в CRUD-ядре.

## Доменная модель

Новая сущность:

```csharp
namespace Wintime.Control.Core.Entities;

public class Order : BaseEntity              // Id (Guid) — суррогатный ключ / FK-цель
{
    public string Number { get; set; } = string.Empty;   // человекочитаемый, НЕ уникален (CRM/1С)
    public DateTime OrderDate { get; set; }               // дата заказа (Utc)
    public DateTime DueDate { get; set; }                 // крайний срок (Utc)
    public Guid ProductTypeId { get; set; }               // изделие заказа (обязателен)
    public ProductType ProductType { get; set; } = null!;
    public int Quantity { get; set; }                     // требуемое кол-во годных, > 0
    public OrderStatus Status { get; set; } = OrderStatus.Active;
    public string? Note { get; set; }

    public ICollection<ShiftTask> Tasks { get; set; } = new List<ShiftTask>();

    // ── Конечный автомат статуса (паттерн ADR-0002: логика в сущности, DomainException → 400) ──
    // goodQuantity = Σ(ActualQuantity − DefectQuantity) по привязанным заданиям, считает вызывающий
    // код (derive-on-read) и передаёт сюда — сущность прогресс не хранит.
    public void Complete(int goodQuantity)  // Active → Completed (ручное завершение менеджером)
    {
        EnsureStatus(OrderStatus.Active, "Заказ не активен");
        if (goodQuantity < Quantity)
            throw new DomainException("Недостаточно годных для завершения заказа");
        Status = OrderStatus.Completed;
    }
    public void Cancel()    // Active → Cancelled
    {
        EnsureStatus(OrderStatus.Active, "Заказ не активен");
        Status = OrderStatus.Cancelled;
    }
    public void Reopen()    // Completed/Cancelled → Active
    {
        if (Status == OrderStatus.Active)
            throw new DomainException("Заказ уже активен");
        Status = OrderStatus.Active;
    }
    private void EnsureStatus(OrderStatus expected, string message)
    {
        if (Status != expected) throw new DomainException(message);
    }
}
```

```csharp
public enum OrderStatus { Active, Completed, Cancelled }
```

Правки `ShiftTask`:

```csharp
public Guid? OrderId { get; set; }        // nullable — задание может быть без заказа
public Order? Order { get; set; }         // навигация
public int DefectQuantity { get; set; }   // брак (ручной ввод при завершении), default 0
```

`ShiftTask.Complete` расширяется браком (валидация диапазона в сущности):

```csharp
public void Complete(int? actualQuantity, string? completionReason, int? defectQuantity)
{
    EnsureStatus(TaskStatus.InProgress, "Задание не в работе");
    if (actualQuantity.HasValue) ActualQuantity = actualQuantity.Value;
    if (defectQuantity.HasValue)
    {
        if (defectQuantity.Value < 0 || defectQuantity.Value > ActualQuantity)
            throw new DomainException("Брак должен быть в диапазоне 0..выпущено");
        DefectQuantity = defectQuantity.Value;
    }
    if (ActualQuantity != PlanQuantity && completionReason != null)
        CloseReason = completionReason;
    Status = TaskStatus.Completed;
    CompletedAt = DateTime.UtcNow;
}
```

**Валидация привязки** — чистая доменная функция (unit-тестируема, `DomainException → 400`):

```csharp
public static class OrderTaskBinding
{
    // order и task.Mold должны быть загружены вызывающим кодом
    public static void EnsureCanBind(Order order, ShiftTask task, Guid? moldProductTypeId)
    {
        if (order.Status != OrderStatus.Active)
            throw new DomainException("Привязать задание можно только к активному заказу");
        if (moldProductTypeId == null)
            throw new DomainException("У пресс-формы задания не задан тип изделия");
        if (moldProductTypeId != order.ProductTypeId)
            throw new DomainException("Изделие задания не совпадает с изделием заказа");
    }
}
```

## EF Core / БД

`ControlDbContext`:

- `DbSet<Order> Orders`.
- Конфиг в `OnModelCreating`:
  ```csharp
  builder.Entity<Order>(e =>
  {
      e.HasKey(x => x.Id);
      e.ToTable("Orders");
      e.HasOne(x => x.ProductType)
       .WithMany()
       .HasForeignKey(x => x.ProductTypeId)
       .OnDelete(DeleteBehavior.Restrict);   // ProductType физически не удаляют (IsActive)
      // Number НЕ уникален — unique-индекс НЕ создаём (решение п.2)
      e.HasIndex(x => x.Number);              // обычный индекс под поиск
  });
  builder.Entity<ShiftTask>()
      .HasOne(t => t.Order)
      .WithMany(o => o.Tasks)
      .HasForeignKey(t => t.OrderId)
      .OnDelete(DeleteBehavior.SetNull);      // заказ не удаляют физически; защита по умолчанию
  builder.Entity<ShiftTask>().HasIndex(t => t.OrderId);   // под Σ-агрегацию прогресса
  ```
- Одна миграция `AddOrders`: таблица `Orders` + колонки `ShiftTasks.OrderId` (nullable),
  `ShiftTasks.DefectQuantity` (int, default 0) + индексы/FK.
- **UTC (CLAUDE.md):** `OrderDate`/`DueDate` — `timestamptz`; при биндинге из запроса
  `DateTime.SpecifyKind(..., DateTimeKind.Utc)` (как `PlannedDate` в `TasksController`).

## Прогресс (derive-on-read)

Единый расчёт по привязанным заданиям заказа:

- `producedQuantity = Σ task.ActualQuantity`
- `defectQuantity   = Σ task.DefectQuantity`
- `goodQuantity     = producedQuantity − defectQuantity`  (= `Σ(Actual − Defect)`)
- `progressPercent  = Quantity > 0 ? goodQuantity * 100 / Quantity : 0`  (не капается, `>100%` ок)

Одиночный заказ — один агрегирующий запрос по `OrderId == id`. Список — **одна** `GroupBy(OrderId)`
по всем `OrderId` из выборки (без N+1), результат подмешивается в DTO. Отменённый статус на расчёт
не влияет — фильтруется на уровне выборки списка/отчёта.

## Backend — API

`OrdersController` по образцу `ProductTypesController`. Роли: чтение и запись — `Admin,Manager`
(наладчик заказы не ведёт).

| Метод | Маршрут | Назначение |
| --- | --- | --- |
| GET | `/api/orders?status=&search=&productTypeId=` | список с прогрессом (фильтры: статус, поиск по `Number`, изделие) |
| GET | `/api/orders/{id:guid}` | заказ + разбивка прогресса + список привязанных заданий |
| POST | `/api/orders` | создать (валидация `Number`, `Quantity>0`, существование/активность `ProductType`) |
| PUT | `/api/orders/{id:guid}` | обновить (PATCH-семантика; смена `ProductTypeId` при наличии заданий → 409) |
| POST | `/api/orders/{id:guid}/complete` | `Active → Completed`; контроллер считает `goodQuantity` (derive-on-read) → `order.Complete(goodQuantity)`; недостаточно годных → 400 |
| POST | `/api/orders/{id:guid}/cancel` | `Active → Cancelled` |
| POST | `/api/orders/{id:guid}/reopen` | `Completed/Cancelled → Active` |
| POST | `/api/orders/{id:guid}/tasks` | привязать существующее задание `{ taskId }` (валидация `OrderTaskBinding`) |
| DELETE | `/api/orders/{id:guid}/tasks/{taskId:guid}` | **единственная точка отвязки** задания (`OrderId = null`) |

**Правки `TasksController`** (три однозначных механизма привязки, без PATCH-неоднозначности):

- **Создание:** `CreateTaskRequestDto` получает `OrderId?` (опционально при создании). Если задан —
  загрузить ПФ и заказ, вызвать `OrderTaskBinding.EnsureCanBind`, установить `OrderId`.
  `UpdateTaskRequestDto` **`OrderId` НЕ добавляем** — nullable-PATCH не различает «не передано» и
  «отвязать»; редактирование привязки идёт через `set-order` ниже.
- **Привязка/смена из формы задания:** `POST /api/tasks/{id}/set-order` (роли `Admin,Manager`),
  тело `{ orderId: Guid }` (**НЕ nullable** — отвязка из формы задания недоступна, UC-7): задание
  `Include(Mold)` + заказ → `OrderTaskBinding.EnsureCanBind` → установить `OrderId`. Отвязка — только
  из карточки заказа (`DELETE /orders/{id}/tasks/{taskId}`).
- **Завершение с браком:** `CompleteTaskRequestDto` получает `DefectQuantity?`; `CompleteTask`
  передаёт его в `task.Complete(actualQuantity, completionReason, defectQuantity)`.

DTO (папка `DTOs/Order/`): `OrderDto` (реквизиты + `productTypeArticle`/`productTypeName` +
`producedQuantity`/`defectQuantity`/`goodQuantity`/`progressPercent` + `taskCount`),
`OrderDetailsDto` (`OrderDto` + `IEnumerable<OrderTaskSummaryDto>`: taskId, immName, moldName,
planQuantity, actualQuantity, defectQuantity, status), `CreateOrderRequestDto`,
`UpdateOrderRequestDto`, `SetOrderRequestDto { Guid OrderId }` (non-null, UC-7). Маппинг вручную (как в
`ProductTypesController`/`MoldsController`, без AutoMapper).

`TaskDto` дополняется: `OrderId` (Guid?), `OrderNumber` (string?), `DefectQuantity` (int) — для
отображения привязки и брака без второго запроса.

## Frontend

- `src/api/orders.js` — list/get/create/update/complete/cancel/reopen/attachTask/detachTask
  (по образцу `src/api/molds.js`).
- `src/views/OrdersView.vue` — новая страница «Заказы» (роут + пункт меню, роль `Admin,Manager`):
  таблица (Номер / Дата / Срок / Изделие / Заказано / **Прогресс-бар годных %** / Статус); фильтры
  статус + поиск по номеру; форма создания/редактирования; кнопки Завершить / Отменить / Возобновить.
- **Карточка заказа** (модалка/страница по образцу `TaskDetailModal.vue`): реквизиты + разбивка
  (Заказано / Выпущено / Брак / **Годных** / Прогресс) + список привязанных заданий + кнопки
  «Привязать существующее» (селект заданий того же `ProductType` без заказа) и **«Отвязать»**
  (единственная точка отвязки, UC-7). Кнопка «Завершить заказ» **активна только при `Годных ≥ Заказано`**
  (UC-3); иначе доступна только «Отменить».
- `src/constants/orderStatus.js` — палитра/подписи статусов (по образцу `effectiveStatus.js`).
- Правки формы задания (`TasksView.vue` / `TaskDetailModal.vue`): опциональный `el-select`
  **«Заказ»** — активные заказы, **отфильтрованные по `ProductType` ПФ задания** (чтобы не предлагать
  несовместимое). При смене ПФ — сброс/перефильтрация селекта. **Отвязку из формы задания не
  предоставляем** (UC-7): селект привязывает/меняет заказ, но не очищает в «нет» — снять привязку
  можно только из карточки заказа.
- Правка мобильного завершения (`MobileTaskDetailView.vue`): в диалоге «Завершить задание» — поле
  **«Брак»** (число, `0..выпущено`), уходит в `DefectQuantity` через `complete`.

## Тесты

- **xUnit (unit):**
  - `Order` — конечный автомат: `Complete`/`Cancel` из не-`Active` → `DomainException`; `Reopen`
    из `Active` → `DomainException`; **`Complete(goodQuantity)` при `goodQuantity < Quantity` →
    `DomainException`; при `≥ Quantity` → `Completed`**; допустимые переходы меняют статус.
  - `OrderTaskBinding.EnsureCanBind` — не-`Active` заказ → throw; `ProductType` mismatch → throw;
    ПФ без типа → throw; совпадение + активный → ок.
  - `ShiftTask.Complete` — `DefectQuantity` вне `0..ActualQuantity` → `DomainException`; в диапазоне —
    сохранён; брак не влияет на переход `InProgress → Completed`.
- **xUnit (integration):**
  - `OrdersController`: CRUD; `Quantity ≤ 0` → 400; `ProductType` архивный/несуществующий → 400;
    смена `ProductTypeId` при наличии привязанных заданий → 409; переходы complete/cancel/reopen +
    guard'ы; **complete при недостатке годных (`Σ(Actual−Defect) < Quantity`) → 400, при достатке →
    Completed** (UC-3); список с фильтрами статус/поиск/изделие.
  - Привязка: совместимое задание → ок; `ProductType` mismatch → 400; ПФ без типа → 400; привязка к
    не-`Active` заказу → 400; отвязка (`set-order null` / DELETE) → `OrderId=null`.
  - Прогресс: `GET /api/orders/{id}` возвращает `good = Σ(Actual − Defect)` и `progressPercent`;
    список считает прогресс одной `GroupBy` (без N+1); **прогресс после переназначения сирот
    (PZP-04)** отражает новый `ActualQuantity` (UC-12).
  - Завершение задания: `complete` с `DefectQuantity` сохраняет брак; `Defect > Actual` → 400.
- **Vitest:** `OrdersView.vue` (рендер, прогресс-бар, фильтры, действия статуса); селект «Заказ» в
  форме задания фильтруется по `ProductType`; поле «Брак» в мобильном завершении блокирует
  значение вне диапазона.

## Границы объёма (YAGNI — НЕ входит в PZP-05)

- **Интеграция CRM/1С** (импорт заказов) — только ручное создание; интеграция → BL-10/ROS-11.
- **Родительская сущность «коммерческий заказ»** (группировка позиций по `Number+OrderDate` в один
  экран) — не делаем; заказы плоские, номер повторяется. Группировка-представление — позже при спросе.
- **Экспорт PDF/Excel** отчётов по заказам — только список с фильтрами/прогрессом; экспорт → follow-up.
- **Feature-flag включения/отключения модуля** (ROS-07) — не сейчас; но дизайн держит модуль
  опциональным (`OrderId` nullable, нет жёсткой зависимости заданий от заказов).
- **Авто-завершение заказа** по достижению плана — только ручное (решение п.5).
- **Кап прогресса `≤100%`** — не капаем; перевыпуск виден как `>100%`.
- **Полноценный контроль качества** (причины брака, приёмка, паспорт) → ROS-05 / BL-27;
  `DefectQuantity` — минимальное зерно.
- **Планирование/распределение заказов на ТПА** (прогноз выполнения из плана) → PZP-07.

## Связи

- **PZP-09** (`ProductType`) — заказ ссылается на изделие; валидация привязки по нему.
- **PZP-08** (`AddPlannedQuantity`) — механизм компенсации брака на уровне задания; согласован с
  двухуровневой семантикой (п.7): рост плана задания добирает годные, не раздувая заказ.
- **PZP-04** (переназначение сирот) — меняет `ActualQuantity` → прогресс заказа отражает на чтении
  (UC-12).
- **BL-27** (партии/паспорт) — вертикаль Заказ → Задание → Партия → Изделие; несколько ПФ/ТПА на
  заказ → несколько партий под заказом.
- **PZP-07** (планирование) — прогноз выполнения заказов из плана заданий (ф.6).
- **ROS-05** (контроль качества оператора) — `DefectQuantity` расширяется причинами/приёмкой.
- **ROS-07** (feature flags) — будущий переключатель модуля заказов.
- **ADR-0009** — фиксирует контракт заказов.
