# Создание задания из карточки заказа — план реализации

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Добавить в карточку заказа кнопку «Создать задание», которая открывает форму задания с жёстко привязанным заказом и списком пресс-форм, ограниченным типом изделия заказа.

**Architecture:** Изменения только во фронтенде. `TaskFormModal.vue` получает опциональный проп `lockedOrder`; когда он задан, поле «Заказ» заблокировано и заполнено, список ПФ фильтруется клиентски по `productTypeId`, а автозагрузка заказов по типу ПФ отключается. `OrderDetailModal.vue` монтирует эту форму и передаёт в неё текущий заказ. Бэкенд не трогаем — `POST /tasks` уже принимает `OrderId` и валидирует привязку.

**Tech Stack:** Vue 3 (script setup), Element Plus 2, Vitest + @vue/test-utils (jsdom).

**Спека:** `docs/superpowers/specs/2026-07-27-create-task-from-order-design.md`

## Global Constraints

- Рабочая директория для всех команд — `Wintime-Control-Frontend`.
- Тесты: `npm test` (это `vitest run`). Одиночный файл — `npm test -- src/путь/файл.spec.js`.
- Ветка уже создана: `feature/pzp-05-create-task-from-order`. Прямой push в `master` запрещён — только PR.
- Поведение формы задания **без** пропа `lockedOrder` меняться не должно: существующие тесты в `src/views/tasks/__tests__/TaskFormModal.spec.js` обязаны остаться зелёными без правок.
- Клиентский фильтр ПФ — это UX, а не контроль доступа. Правило привязки enforced на сервере в `Wintime.Control.Core/Policies/OrderTaskBinding.cs`. Не дублировать эту валидацию на фронте.
- Тексты интерфейса — на русском, как в остальном коде.

---

### Task 1: `TaskFormModal` — режим заблокированного заказа

**Files:**
- Modify: `Wintime-Control-Frontend/src/views/tasks/TaskFormModal.vue`
- Test: `Wintime-Control-Frontend/src/views/tasks/__tests__/TaskFormModal.spec.js`

**Interfaces:**
- Consumes: ничего от других задач.
- Produces: проп `lockedOrder` компонента `TaskFormModal` — объект вида
  `{ id: string, number: string, productTypeId: string, productTypeArticle: string }`
  или `null` (значение по умолчанию). Task 2 передаёт его из карточки заказа.
  Также появляется computed `availableMolds` — отфильтрованный список ПФ.

- [ ] **Step 1: Написать падающие тесты**

Дописать в конец `src/views/tasks/__tests__/TaskFormModal.spec.js` (существующие моки и
хелпер `mountModal` в начале файла переиспользуются как есть):

```js
describe('TaskFormModal — заблокированный заказ (lockedOrder)', () => {
  const lockedOrder = {
    id: 'order-9',
    number: 'ORD-9',
    productTypeId: 'pt-X',
    productTypeArticle: 'ART-X'
  }

  beforeEach(() => vi.clearAllMocks())

  it('подставляет заказ в форму и не запрашивает список заказов', async () => {
    const wrapper = mountModal({ lockedOrder })
    await flushPromises()

    expect(wrapper.vm.form.orderId).toBe('order-9')
    expect(ordersApi.getList).not.toHaveBeenCalled()
  })

  it('блокирует поле «Заказ» и показывает в нём номер заказа', async () => {
    const wrapper = mountModal({ lockedOrder })
    await flushPromises()

    const orderInput = wrapper.findAll('input')
      .find(i => i.attributes('placeholder') === 'Выберите заказ')
    expect(orderInput).toBeTruthy()
    expect(orderInput.element.disabled).toBe(true)
    expect(orderInput.element.value).toContain('ORD-9')
  })

  it('оставляет в списке ПФ только пресс-формы с типом изделия заказа', async () => {
    const wrapper = mountModal({ lockedOrder })
    await flushPromises()

    // мок moldsApi отдаёт mold-1 (pt-X) и mold-2 (pt-Y)
    expect(wrapper.vm.availableMolds.map(m => m.id)).toEqual(['mold-1'])
  })

  it('при выборе ПФ не сбрасывает orderId и не грузит заказы', async () => {
    const wrapper = mountModal({ lockedOrder })
    await flushPromises()

    wrapper.vm.form.moldId = 'mold-1'
    await flushPromises()

    expect(wrapper.vm.form.orderId).toBe('order-9')
    expect(ordersApi.getList).not.toHaveBeenCalled()
  })

  it('без пропа список ПФ остаётся полным', async () => {
    const wrapper = mountModal()
    await wrapper.vm.loadMolds()
    await flushPromises()

    expect(wrapper.vm.availableMolds.map(m => m.id)).toEqual(['mold-1', 'mold-2'])
  })

  it('создаёт задание с orderId заказа', async () => {
    const wrapper = mountModal({ lockedOrder })
    await flushPromises()

    wrapper.vm.form.immId = 'imm-1'
    wrapper.vm.form.moldId = 'mold-1'
    wrapper.vm.form.planQuantity = 10
    await flushPromises()

    await wrapper.vm.handleSubmit()
    await flushPromises()

    expect(tasksApi.create).toHaveBeenCalledWith(
      expect.objectContaining({ orderId: 'order-9', moldId: 'mold-1' })
    )
  })
})
```

- [ ] **Step 2: Запустить тесты и убедиться, что новые падают**

Run: `npm test -- src/views/tasks/__tests__/TaskFormModal.spec.js`
Expected: 6 новых тестов FAIL (`form.orderId` = `null`, `wrapper.vm.availableMolds` — `undefined`,
поле «Заказ» не disabled). Существующие 5 тестов — PASS.

- [ ] **Step 3: Добавить проп `lockedOrder`**

В `src/views/tasks/TaskFormModal.vue` в `defineProps` (сейчас строки 165–174) добавить третий проп:

```js
const props = defineProps({
  modelValue: {
    type: Boolean,
    default: false
  },
  task: {
    type: Object,
    default: null
  },
  // Заказ, из карточки которого открыли форму. Задан — заказ менять нельзя,
  // а список ПФ ограничен его типом изделия (см. спеку 2026-07-27).
  lockedOrder: {
    type: Object,
    default: null
  }
})
```

- [ ] **Step 4: Добавить computed `availableMolds` и применить его в шаблоне**

Сразу после `const selectedProductTypeId = computed(...)` (сейчас строка 216) добавить:

```js
const availableMolds = computed(() =>
  props.lockedOrder
    ? molds.value.filter(m => m.productTypeId === props.lockedOrder.productTypeId)
    : molds.value
)
```

В шаблоне заменить источник опций пресс-форм (сейчас строка 44):

```diff
           <el-option
-            v-for="mold in molds"
+            v-for="mold in availableMolds"
             :key="mold.id"
```

- [ ] **Step 5: Заблокировать поле «Заказ»**

В шаблоне у селекта заказа (сейчас строка 65) заменить условие `disabled`:

```diff
           :clearable="false"
-          :disabled="!selectedProductTypeId"
+          :disabled="!!lockedOrder || !selectedProductTypeId"
```

- [ ] **Step 6: Не грузить заказы по типу ПФ в заблокированном режиме**

Заменить watch на `selectedProductTypeId` (сейчас строки 249–251):

```js
watch(selectedProductTypeId, (productTypeId) => {
  // При заблокированном заказе список заказов не нужен, а вызов затёр бы form.orderId.
  if (props.lockedOrder) return
  loadOrdersForProductType(productTypeId)
})
```

- [ ] **Step 7: Подставлять заказ при открытии формы**

Добавить `applyLockedOrder` и его watch **сразу после** функции `loadMolds`
(сейчас она заканчивается на строке 261) — не выше, иначе `loadMolds` окажется
в temporal dead zone при немедленном срабатывании watch:

```js
// Единственная опция селекта — сам заказ: отдельный запрос не нужен,
// карточка заказа уже передала всё для отображения.
const applyLockedOrder = () => {
  if (!props.lockedOrder) return
  orderOptions.value = [props.lockedOrder]
  form.orderId = props.lockedOrder.id
  // Список ПФ фильтруется по типу изделия заказа, поэтому нужен сразу,
  // не дожидаясь @focus на селекте.
  loadMolds()
}

watch(() => props.lockedOrder, applyLockedOrder, { immediate: true })
```

И в существующий watch на `props.modelValue` (сейчас строки 298–302) добавить вызов:

```js
watch(() => props.modelValue, (isVisible) => {
  if (isVisible && props.task) {
    populateForm(props.task)
  }
  if (isVisible) {
    applyLockedOrder()
  }
})
```

- [ ] **Step 8: Сохранять заказ при сбросе формы**

Заменить `resetForm` (сейчас строки 218–230):

```js
const resetForm = () => {
  Object.assign(form, {
    immId: '',
    moldId: '',
    personnelId: '',
    planQuantity: 1000,
    plannedDate: null,
    note: '',
    orderId: props.lockedOrder?.id ?? null
  })
  orderOptions.value = props.lockedOrder ? [props.lockedOrder] : []
  initialOrderId.value = null
}
```

- [ ] **Step 9: Запустить тесты**

Run: `npm test -- src/views/tasks/__tests__/TaskFormModal.spec.js`
Expected: PASS, все 11 тестов (5 существующих + 6 новых).

- [ ] **Step 10: Коммит**

```bash
git add Wintime-Control-Frontend/src/views/tasks/TaskFormModal.vue \
        Wintime-Control-Frontend/src/views/tasks/__tests__/TaskFormModal.spec.js
git commit -m "feat(orders): режим lockedOrder в форме задания

Заказ подставляется и блокируется, список ПФ ограничен его типом изделия.
Без пропа поведение формы не меняется."
```

---

### Task 2: Кнопка «Создать задание» в карточке заказа

**Files:**
- Modify: `Wintime-Control-Frontend/src/components/orders/OrderDetailModal.vue`
- Test: `Wintime-Control-Frontend/src/components/orders/__tests__/OrderDetailModal.spec.js`

**Interfaces:**
- Consumes: проп `lockedOrder` компонента `TaskFormModal` из Task 1 —
  `{ id, number, productTypeId, productTypeArticle }`; событие `success` этой формы.
- Produces: ничего для последующих задач.

- [ ] **Step 1: Написать падающие тесты**

В `src/components/orders/__tests__/OrderDetailModal.spec.js` добавить импорт формы
после существующего импорта компонента (строка 5):

```js
import TaskFormModal from '@/views/tasks/TaskFormModal.vue'
```

Заменить оба существующих блока `global` на общий хелпер и дописать тесты.
Итоговый файл ниже строки 36 (`describe('OrderDetailModal', ...`) должен выглядеть так:

```js
function mountModal() {
  return mount(OrderDetailModal, {
    props: { modelValue: true, orderId: '1' },
    global: {
      plugins: [ElementPlus],
      stubs: { 'el-progress': true, TaskFormModal: true }
    }
  })
}

function findButton(wrapper, text) {
  return wrapper.findAll('button').find(b => b.text() === text)
}

describe('OrderDetailModal', () => {
  beforeEach(() => vi.clearAllMocks())

  it('показывает разбивку прогресса и список заданий', async () => {
    const wrapper = mountModal()
    await flushPromises()
    expect(wrapper.text()).toContain('ТПА-1')
    expect(wrapper.text()).toContain('100') // годных
  })

  it('отвязывает задание по кнопке «Отвязать»', async () => {
    const wrapper = mountModal()
    await flushPromises()

    const detachButton = findButton(wrapper, 'Отвязать')
    expect(detachButton).toBeTruthy()
    await detachButton.trigger('click')
    await flushPromises()

    expect(ordersApi.detachTask).toHaveBeenCalledWith('1', 't1')
    expect(wrapper.emitted('updated')).toBeTruthy()
  })

  it('на активном заказе кнопки создания и привязки доступны', async () => {
    const wrapper = mountModal()
    await flushPromises()

    expect(findButton(wrapper, 'Создать задание').element.disabled).toBe(false)
    expect(findButton(wrapper, 'Привязать существующее').element.disabled).toBe(false)
  })

  it.each(['Completed', 'Cancelled'])(
    'на заказе в статусе %s обе кнопки заблокированы',
    async (status) => {
      ordersApi.getById.mockResolvedValueOnce({ data: {
        id: '1', number: 'ORD-1', quantity: 100, producedQuantity: 100,
        defectQuantity: 0, goodQuantity: 100, progressPercent: 100, status,
        productTypeId: 'pt-1', productTypeArticle: 'A',
        dueDate: '2026-08-01T00:00:00Z', tasks: []
      } })

      const wrapper = mountModal()
      await flushPromises()

      expect(findButton(wrapper, 'Создать задание').element.disabled).toBe(true)
      expect(findButton(wrapper, 'Привязать существующее').element.disabled).toBe(true)
    }
  )

  it('по кнопке «Создать задание» открывает форму с заказом текущей карточки', async () => {
    const wrapper = mountModal()
    await flushPromises()

    await findButton(wrapper, 'Создать задание').trigger('click')
    await flushPromises()

    const form = wrapper.findComponent(TaskFormModal)
    expect(form.props('modelValue')).toBe(true)
    expect(form.props('lockedOrder')).toEqual({
      id: '1',
      number: 'ORD-1',
      productTypeId: 'pt-1',
      productTypeArticle: 'A'
    })
  })

  it('после создания задания перечитывает заказ и эмитит updated', async () => {
    const wrapper = mountModal()
    await flushPromises()
    expect(ordersApi.getById).toHaveBeenCalledTimes(1)

    wrapper.findComponent(TaskFormModal).vm.$emit('success')
    await flushPromises()

    expect(ordersApi.getById).toHaveBeenCalledTimes(2)
    expect(wrapper.emitted('updated')).toBeTruthy()
  })
})
```

- [ ] **Step 2: Запустить тесты и убедиться, что новые падают**

Run: `npm test -- src/components/orders/__tests__/OrderDetailModal.spec.js`
Expected: 4 новых теста FAIL (кнопка «Создать задание» не найдена — `undefined`,
`findComponent` не находит `TaskFormModal`). Два существующих — PASS.

- [ ] **Step 3: Заменить шапку блока «Задания» в шаблоне**

В `src/components/orders/OrderDetailModal.vue` заменить блок (сейчас строки 23–26):

```vue
      <div class="flex justify-between items-center mb-2">
        <span class="font-semibold">Задания</span>
        <div class="flex gap-2">
          <!-- el-tooltip не получает события от disabled-кнопки — нужна обёртка -->
          <el-tooltip
            :disabled="isOrderActive"
            content="Задание можно создать только для активного заказа"
          >
            <span>
              <el-button
                size="small"
                type="primary"
                :disabled="!isOrderActive"
                @click="openCreateTask"
              >
                Создать задание
              </el-button>
            </span>
          </el-tooltip>
          <el-tooltip
            :disabled="isOrderActive"
            content="Привязать задание можно только к активному заказу"
          >
            <span>
              <el-button size="small" :disabled="!isOrderActive" @click="openAttachDialog">
                Привязать существующее
              </el-button>
            </span>
          </el-tooltip>
        </div>
      </div>
```

Обратите внимание: `type="primary"` переезжает с «Привязать существующее» на
«Создать задание» — основное действие теперь создание.

- [ ] **Step 4: Смонтировать форму задания**

В том же файле, сразу перед закрывающим `</el-dialog>` внешнего диалога
(сейчас строка 64, после вложенного диалога привязки), добавить:

```vue
    <TaskFormModal
      v-if="lockedOrder"
      v-model="taskFormVisible"
      :locked-order="lockedOrder"
      append-to-body
      @success="onTaskCreated"
    />
```

- [ ] **Step 5: Добавить логику в script**

Импорт — после существующих импортов компонентов (сейчас строка 75, рядом с `apiErrorMessage`):

```js
import TaskFormModal from '@/views/tasks/TaskFormModal.vue'
```

Состояние — после `const selectedTaskId = ref(null)` (сейчас строка 95):

```js
const taskFormVisible = ref(false)
```

Computed — после `progressPercentage` (сейчас строка 105):

```js
const isOrderActive = computed(() => order.value?.status === 'Active')

// Всё, что форме задания нужно знать о заказе: отдельный запрос ей не потребуется.
const lockedOrder = computed(() =>
  order.value
    ? {
        id: order.value.id,
        number: order.value.number,
        productTypeId: order.value.productTypeId,
        productTypeArticle: order.value.productTypeArticle
      }
    : null
)
```

Обработчики — в конец блока `<script setup>`, после `attach()`:

```js
function openCreateTask() {
  taskFormVisible.value = true
}

async function onTaskCreated() {
  await loadOrder()
  emit('updated')
}
```

- [ ] **Step 6: Запустить тесты**

Run: `npm test -- src/components/orders/__tests__/OrderDetailModal.spec.js`
Expected: PASS, все 7 тестов (2 существующих + 4 новых, из них один `it.each` даёт два прогона).

- [ ] **Step 7: Прогнать весь фронтовый набор**

Run: `npm test`
Expected: PASS, падений нет. Особое внимание — `TaskFormModal.spec.js` и `OrdersView.spec.js`
(регресс формы задания в обычном режиме).

- [ ] **Step 8: Коммит**

```bash
git add Wintime-Control-Frontend/src/components/orders/OrderDetailModal.vue \
        Wintime-Control-Frontend/src/components/orders/__tests__/OrderDetailModal.spec.js
git commit -m "feat(orders): кнопка «Создать задание» в карточке заказа

Открывает форму задания с подставленным и заблокированным заказом.
Действия недоступны на неактивном заказе — привязка к нему запрещена
политикой OrderTaskBinding."
```

---

## Ручная проверка после Task 2

Запустить `npm run dev` (порт 3000) и API, затем:

1. Заказы → «Открыть» на активном заказе → «Создать задание».
2. Убедиться: поле «Заказ» серое, в нём номер этого заказа; в списке пресс-форм —
   только ПФ типа изделия заказа.
3. Выбрать ТПА, ПФ, план, сохранить → задание появилось в таблице карточки,
   прогресс в списке заказов пересчитан.
4. Завершить или отменить заказ, открыть карточку → обе кнопки серые,
   при наведении — подсказка.
5. Задания → «Выдать задание» → поле «Заказ» работает как раньше:
   до выбора ПФ заблокировано, после — предлагает активные заказы этого типа.
