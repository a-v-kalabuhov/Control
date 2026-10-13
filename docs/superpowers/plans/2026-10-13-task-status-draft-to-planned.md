# TaskStatus `Draft` → `Planned` Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Переименовать значение `TaskStatus.Draft` в `TaskStatus.Planned` во всём коде (backend, тесты, фронт, документация) без изменения логики.

**Architecture:** Чисто механическое переименование. В БД статус хранится как `int`, значение `0` сохраняется — миграции нет. В API статус сериализуется строкой (`JsonStringEnumConverter`), поэтому контракт меняется: `"Draft"` → `"Planned"`; legacy-клиентов и данных нет (пилот ПЗП).

**Tech Stack:** .NET 9, xUnit, Vue 3, Vitest.

**Spec:** `docs/superpowers/specs/2026-10-13-arch-03-task-shift-decoupling-design.md` (раздел «Предшествующий PR»).

## Global Constraints

- Значение enum: `Planned = 0` (как было у `Draft`). Остальные значения `TaskStatus` не трогать.
- Подпись в UI не меняется: «Планируется».
- Не переименовывать слова «draft»/«черновик» вне смысла статуса задания (например, черновики форм).
- Принятые ADR (`docs/adr/0002`, `0014` и др.) **не править** — имя `Draft` там историческое.
- Ветка: `refactor/task-status-planned` от `master`. Прямой push в `master` запрещён — только PR.

---

### Task 1: Переименование во всём репозитории

**Files (полный список по `grep -rln Draft`, проверено 2026-10-13):**
- Modify: `Wintime.Control.Core/Enums/TaskStatus.cs`
- Modify: `Wintime.Control.Core/Enums/ActiveTaskStatus.cs` (комментарий)
- Modify: `Wintime.Control.Core/Entities/ShiftTask.cs`
- Modify: `Wintime.Control.API/Controllers/TasksController.cs`
- Modify: `Wintime.Control.Tests.Unit/Entities/ShiftTaskEditGuardsTests.cs`, `ShiftTaskPlanningNormsTests.cs`, `ShiftTaskPlanningTests.cs`, `ShiftTaskStateMachineTests.cs`
- Modify: `Wintime.Control.Tests.Integration/Molds/MoldProductTypeTests.cs`, `Tasks/MyTasksApiTests.cs`, `Tasks/TaskCycleNormsApiTests.cs`, `Tasks/TaskEditGuardsApiTests.cs`, `Tasks/TaskPersonnelApiTests.cs`, `Tasks/TaskPlannedStartApiTests.cs`, `Tasks/TasksControllerTests.cs`
- Modify: `Wintime-Control-Frontend/src/constants/taskStatus.js`, `src/stores/tasks.js`, `src/utils/plannedStart.js`, `src/components/mobile/MobileStatusBadge.vue`, `src/components/mobile/MobileTaskCard.vue`, `src/components/tasks/TaskFilters.vue`, `src/components/tasks/TaskStatusBadge.vue`, `src/views/tasks/TaskDetailModal.vue`, `src/views/tasks/TasksView.vue`
- Modify (тесты фронта): `src/stores/__tests__/tasks.spec.js`, `src/utils/__tests__/plannedStart.spec.js`, `src/views/tasks/__tests__/TaskDetailModal.spec.js`, `src/views/tasks/__tests__/TaskFormModal.spec.js`
- Modify: `CLAUDE.md`, `docs/domain/shift-task.md`

**Interfaces:**
- Produces: `TaskStatus.Planned = 0`; строка статуса в API `"Planned"`; фронт-константа `TASK_STATUS.PLANNED = 'Planned'`; геттер стора `plannedTasks` (был `draftTasks`).

- [ ] **Step 1: Создать ветку**

```bash
git checkout master && git pull && git checkout -b refactor/task-status-planned
```

- [ ] **Step 2: Enum**

`Wintime.Control.Core/Enums/TaskStatus.cs`:

```csharp
namespace Wintime.Control.Core.Enums;

public enum TaskStatus
{
    Planned = 0,
    Issued = 1,
    InProgress = 2,
    Completed = 3,
    Closed = 4,
    Setup = 5
}
```

- [ ] **Step 3: Backend и тесты .NET — заменить идентификатор**

Во всех `.cs`-файлах из списка выше заменить `TaskStatus.Draft` → `TaskStatus.Planned` и строковые литералы `"Draft"` → `"Planned"` (в интеграционных тестах статус сравнивается строкой из JSON). В комментариях и XML-doc заменить слово `Draft` → `Planned` (например, «Планируется (Draft) → Выдано (Issued)» → «Планируется (Planned) → Выдано (Issued)»). Имена тестовых методов с `Draft` переименовать (`Returns201WithDraftStatus` → `Returns201WithPlannedStatus`).

```bash
# PowerShell или Git Bash — посмотреть все вхождения перед заменой
grep -rn "Draft" --include=*.cs Wintime.Control.Core Wintime.Control.API Wintime.Control.Infrastructure Wintime.Control.Tests.Unit Wintime.Control.Tests.Integration | grep -v Migrations
```

Заменять осознанно по каждому файлу (Edit), не слепым sed: миграции (`Wintime.Control.Infrastructure/Migrations`) **не трогать**.

- [ ] **Step 4: Собрать и прогнать .NET-тесты**

Run: `dotnet build` → Expected: `Build succeeded`, 0 errors.
Run: `dotnet test Wintime.Control.Tests.Unit` → Expected: all PASS.
Run: `dotnet test Wintime.Control.Tests.Integration` → Expected: all PASS (нужен Docker/PostgreSQL, как обычно для интеграционных тестов).

Проверка, что идентификатора не осталось:

Run: `grep -rn "Draft" --include=*.cs Wintime.Control.Core Wintime.Control.API Wintime.Control.Infrastructure Wintime.Control.Tests.Unit Wintime.Control.Tests.Integration | grep -v Migrations`
Expected: пусто.

- [ ] **Step 5: Фронт — константы**

`Wintime-Control-Frontend/src/constants/taskStatus.js`:

```js
export const TASK_STATUS = {
  PLANNED: 'Planned',
  ISSUED: 'Issued',
  SETUP: 'Setup',
  IN_PROGRESS: 'InProgress',
  COMPLETED: 'Completed',
  CLOSED: 'Closed'
}

export const TASK_STATUS_LABELS = {
  [TASK_STATUS.PLANNED]: 'Планируется',
  [TASK_STATUS.ISSUED]: 'Выдано',
  [TASK_STATUS.SETUP]: 'Наладка',
  [TASK_STATUS.IN_PROGRESS]: 'В работе',
  [TASK_STATUS.COMPLETED]: 'Выполнено',
  [TASK_STATUS.CLOSED]: 'Закрыто'
}

export const TASK_STATUS_COLORS = {
  [TASK_STATUS.PLANNED]: 'gray',
  [TASK_STATUS.ISSUED]: 'blue',
  [TASK_STATUS.SETUP]: 'orange',
  [TASK_STATUS.IN_PROGRESS]: 'yellow',
  [TASK_STATUS.COMPLETED]: 'green',
  [TASK_STATUS.CLOSED]: 'gray'
}
```

- [ ] **Step 6: Фронт — остальные файлы**

- `src/stores/tasks.js:32`: `draftTasks: (state) => state.tasks.filter(t => t.status === 'Draft')` →
  `plannedTasks: (state) => state.tasks.filter(t => t.status === 'Planned')`.
- `src/views/tasks/TasksView.vue:36`: `tasksStore.draftTasks.length` → `tasksStore.plannedTasks.length`.
- В остальных `.vue`/`.js` из списка заменить строку `'Draft'` → `'Planned'`, ключ объекта `Draft:` → `Planned:`
  (`TaskStatusBadge.vue`, `MobileStatusBadge.vue`, `MobileTaskCard.vue`), `statusConfig.Draft` → `statusConfig.Planned`,
  `value="Draft"` → `value="Planned"` (`TaskFilters.vue`), `'Draft'` в `NOT_STARTED` (`plannedStart.js`),
  в массивах статусов (`TaskDetailModal.vue`, `TasksView.vue`).
- В тестах фронта — то же для фикстур (`status: 'Draft'` → `status: 'Planned'`).

Run: `grep -rn "Draft\|draftTasks\|DRAFT" Wintime-Control-Frontend/src`
Expected: пусто.

- [ ] **Step 7: Прогнать фронт-тесты и сборку**

Run: `cd Wintime-Control-Frontend && npx vitest run` → Expected: all PASS.
Run: `cd Wintime-Control-Frontend && npm run build` → Expected: сборка без ошибок.

- [ ] **Step 8: Документация**

`CLAUDE.md`, раздел «Плановое начало и выдача задания»:
- `Менять план и наладчика можно только в\n\`Draft\`/\`Issued\`.` → `… только в \`Planned\`/\`Issued\`.`
- Удалить фразу `В UI \`Draft\` подписан «Планируется».`
- В абзаце PZP-14: `\`Draft\`/\`Issued\`` → `\`Planned\`/\`Issued\``, `(\`Draft\`…\`InProgress\`)` → `(\`Planned\`…\`InProgress\`)`.

`docs/domain/shift-task.md`: строка 39 `Draft → Issued → …` → `Planned → Issued → …`; строка 45 `` `Draft → Issued` `` → `` `Planned → Issued` ``. Прочитать файл целиком и заменить прочие упоминания статуса `Draft`.

- [ ] **Step 9: Commit**

```bash
git add -A
git commit -m "refactor: rename TaskStatus.Draft to Planned

Value 0 is unchanged (no migration); API string becomes \"Planned\".

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

- [ ] **Step 10: PR**

```bash
git push -u origin refactor/task-status-planned
gh pr create --title "refactor: TaskStatus Draft → Planned" --body "$(cat <<'EOF'
Переименование статуса задания `Draft` → `Planned` (предшествующий PR к ARCH-03).

- Значение enum `0` не меняется — миграции нет.
- Контракт API: строка статуса `"Draft"` → `"Planned"` (legacy-клиентов нет).
- Подпись в UI не меняется: «Планируется».
- Принятые ADR не правились — имя `Draft` в них историческое.

🤖 Generated with [Claude Code](https://claude.com/claude-code)
EOF
)"
```
