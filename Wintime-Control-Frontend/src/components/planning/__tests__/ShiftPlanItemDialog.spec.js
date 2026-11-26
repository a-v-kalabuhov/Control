// @vitest-environment jsdom
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { flushPromises, mount } from '@vue/test-utils'
import { nextTick } from 'vue'
import ElementPlus, { ElMessage } from 'element-plus'
import { createPinia, setActivePinia } from 'pinia'
import ShiftPlanItemDialog from '../ShiftPlanItemDialog.vue'
import { shiftPlansApi } from '@/api/shiftPlans'
import { useAuthStore } from '@/stores/auth'

const { routerPush } = vi.hoisted(() => ({ routerPush: vi.fn() }))

vi.mock('vue-router', async importOriginal => ({
  ...await importOriginal(),
  useRouter: () => ({ push: routerPush })
}))

vi.mock('@/api/shiftPlans', () => ({
  shiftPlansApi: { setLead: vi.fn(), materializeShift: vi.fn() }
}))

const candidates = [
  { id: 'lead-1', fullName: 'Иванов Иван', employeeId: '001' },
  { id: 'manager-current', fullName: 'Менеджер Текущий', employeeId: '002' }
]

const day = {
  shiftDate: '2026-11-10',
  isSaved: true,
  isFactoryToday: false,
  items: []
}

function makeItem(overrides = {}) {
  return {
    id: 'item-1',
    position: 2,
    startAtUtc: '2026-11-10T17:00:00Z',
    endAtUtc: '2026-11-11T05:00:00Z',
    timeZoneId: 'Europe/Moscow',
    shiftLeadId: 'lead-1',
    shiftLeadName: 'Иванов Иван',
    shiftLeadSource: 'Explicit',
    state: 'Future',
    canEditLead: true,
    canEditOperators: true,
    operatorAssignmentCount: 0,
    ...overrides
  }
}

function mountDialog(item = makeItem(), saving = false, dayValue = day) {
  return mount(ShiftPlanItemDialog, {
    props: { modelValue: true, day: dayValue, item, candidates, saving },
    global: {
      plugins: [ElementPlus],
      stubs: {
        RouterLink: {
          name: 'RouterLink',
          props: ['to'],
          template: '<a :href="to"><slot /></a>'
        }
      }
    },
    attachTo: document.body
  })
}

function deferred() {
  let resolve
  let reject
  const promise = new Promise((res, rej) => { resolve = res; reject = rej })
  return { promise, resolve, reject }
}

function button(testId) {
  return document.body.querySelector(`[data-test="${testId}"]`)
}

describe('ShiftPlanItemDialog', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    const auth = useAuthStore()
    auth.user = { id: 'manager-current', role: 'Manager', fullName: 'Менеджер Текущий' }
    auth.userRole = 'Manager'
    vi.clearAllMocks()
    routerPush.mockResolvedValue(undefined)
    shiftPlansApi.setLead.mockResolvedValue({ data: { ...day, items: [makeItem()] } })
    shiftPlansApi.materializeShift.mockResolvedValue({ data: { ...day, items: [makeItem()] } })
    vi.spyOn(ElMessage, 'error').mockImplementation(() => {})
  })

  afterEach(() => {
    vi.restoreAllMocks()
    document.body.innerHTML = ''
  })

  it('использует серверный список кандидатов, включая текущего Manager, без добавления Admin', () => {
    const wrapper = mountDialog()

    expect(wrapper.vm.options.map(option => option.id)).toEqual(['lead-1', 'manager-current'])
    expect(wrapper.vm.options.map(option => option.fullName)).toContain('Менеджер Текущий')
    expect(wrapper.vm.options.map(option => option.fullName)).not.toContain('Admin')
  })

  it.each([
    ['Default', 'По умолчанию: Иванов Иван'],
    ['ManagerFallback', 'Первый активный менеджер: Иванов Иван']
  ])('оставляет explicit select пустым для %s и подписывает effective fallback', async (source, label) => {
    const wrapper = mountDialog(makeItem({ id: null, shiftLeadSource: source }))
    await nextTick()

    expect(wrapper.vm.selectedExplicitLeadId).toBeNull()
    expect(wrapper.findComponent('[data-test="lead-select"]').props('modelValue')).toBeNull()
    const emptyOption = wrapper.findAllComponents({ name: 'ElOption' })
      .find(option => option.props('value') === '')
    expect(emptyOption?.props('label')).toBe(label)
    expect(wrapper.vm.canSave).toBe(false)

    await wrapper.findComponent('[data-test="lead-select"]').vm.$emit('update:modelValue', '')
    expect(wrapper.vm.selectedExplicitLeadId).toBeNull()
  })

  it('показывает «Не назначен» только при отсутствии effective старшего', async () => {
    const wrapper = mountDialog(makeItem({
      id: null, shiftLeadId: null, shiftLeadName: null, shiftLeadSource: null,
      hasMissingLeadWarning: true
    }))
    await nextTick()

    const emptyOption = wrapper.findAllComponents({ name: 'ElOption' })
      .find(option => option.props('value') === '')
    expect(emptyOption?.props('label')).toBe('Не назначен')
    expect(document.body.querySelector('[data-test="missing-lead"]')).not.toBeNull()
  })

  it('не превращает effective fallback в явное назначение при save', async () => {
    const wrapper = mountDialog(makeItem({ id: null, shiftLeadSource: 'Default' }))
    await nextTick()

    await wrapper.vm.saveLead()

    expect(shiftPlansApi.setLead).not.toHaveBeenCalled()
  })

  it('разрешает очистку только Explicit, а не Default или ManagerFallback', async () => {
    const wrapper = mountDialog(makeItem({ id: null, shiftLeadSource: 'Default' }))
    await nextTick()
    expect(wrapper.vm.canClear).toBe(false)
    expect(button('clear-lead').disabled).toBe(true)

    await wrapper.setProps({ item: makeItem({ id: null, shiftLeadSource: 'ManagerFallback' }) })
    expect(wrapper.vm.canClear).toBe(false)
    expect(button('clear-lead').disabled).toBe(true)

    await wrapper.setProps({ item: makeItem() })
    expect(wrapper.vm.canClear).toBe(true)
    expect(button('clear-lead').disabled).toBe(false)
  })

  it.each(['Default', 'ManagerFallback'])('Manager назначает себя явным старшим поверх %s', async source => {
    const wrapper = mountDialog(makeItem({ id: null, shiftLeadSource: source }))
    await nextTick()

    expect(button('self-assign-lead')).not.toBeNull()
    button('self-assign-lead').click()
    await flushPromises()

    expect(shiftPlansApi.setLead).toHaveBeenCalledWith('2026-11-10', 2, 'manager-current')
  })

  it('canEditLead=false отключает выбор, назначение и очистку', async () => {
    const wrapper = mountDialog(makeItem({ canEditLead: false }))
    await nextTick()

    expect(wrapper.vm.canEdit).toBe(false)
    expect(wrapper.vm.canSave).toBe(false)
    expect(wrapper.vm.canClear).toBe(false)
    expect(button('save-lead').disabled).toBe(true)
    expect(button('clear-lead').disabled).toBe(true)
  })

  it('назначения литейщиков не блокируют переназначение и запрос очистки Explicit', async () => {
    const wrapper = mountDialog(makeItem({ operatorAssignmentCount: 2 }))
    await nextTick()

    expect(wrapper.vm.canEdit).toBe(true)
    expect(wrapper.vm.canSave).toBe(true)
    expect(wrapper.vm.canClear).toBe(true)
    expect(button('save-lead').disabled).toBe(false)
    expect(button('clear-lead').disabled).toBe(false)

    button('clear-lead').click()
    await flushPromises()
    expect(shiftPlansApi.setLead).toHaveBeenCalledWith('2026-11-10', 2, null)
  })

  it('передаёт серверу запрет очистки без fallback при литейщиках и черновике', async () => {
    shiftPlansApi.setLead.mockRejectedValue({
      response: { status: 400, data: { message: 'Нельзя снять старшего без fallback' } }
    })
    const wrapper = mountDialog(makeItem({ operatorAssignmentCount: 2, closureStatus: 'Draft' }))
    await nextTick()

    button('clear-lead').click()
    await flushPromises()

    expect(shiftPlansApi.setLead).toHaveBeenCalledWith('2026-11-10', 2, null)
    expect(ElMessage.error).toHaveBeenCalledWith('Нельзя снять старшего без fallback')
    expect(wrapper.emitted('saved')).toBeUndefined()
  })

  it('сохраняет переназначение и передаёт сохранённый день родителю', async () => {
    const savedDay = { ...day, items: [makeItem({ shiftLeadId: 'manager-current' })] }
    shiftPlansApi.setLead.mockResolvedValue({ data: savedDay })
    const wrapper = mountDialog()

    wrapper.vm.selectedExplicitLeadId = 'manager-current'
    await nextTick()
    button('save-lead').click()
    await flushPromises()

    expect(shiftPlansApi.setLead).toHaveBeenCalledWith('2026-11-10', 2, 'manager-current')
    const events = wrapper.emitted('saved')
    expect(events).toHaveLength(1)
    expect(events[0][0]).toEqual(savedDay)
    expect(events[0][1]).toEqual({
      date: '2026-11-10', position: 2, request: expect.any(Number)
    })
  })

  it('очистка передаёт null', async () => {
    const wrapper = mountDialog()
    await nextTick()

    button('clear-lead').click()
    await flushPromises()

    expect(shiftPlansApi.setLead).toHaveBeenCalledWith('2026-11-10', 2, null)
    expect(wrapper.emitted('saved')).toHaveLength(1)
  })

  it.each([
    ['Default', 'По умолчанию: Иванов Иван'],
    ['ManagerFallback', 'Первый активный менеджер: Иванов Иван']
  ])('очистка Explicit с литейщиками и черновиком возвращает %s и сохраняет данные', async (source, label) => {
    const fallbackItem = makeItem({
      shiftLeadSource: source, hasMissingLeadWarning: false,
      operatorAssignmentCount: 2, closureStatus: 'Draft'
    })
    const savedDay = { ...day, isSaved: true, items: [fallbackItem] }
    shiftPlansApi.setLead.mockResolvedValue({ data: savedDay })
    const wrapper = mountDialog(makeItem({ operatorAssignmentCount: 2, closureStatus: 'Draft' }))
    await nextTick()

    expect(wrapper.vm.canClear).toBe(true)
    button('clear-lead').click()
    await flushPromises()
    await wrapper.setProps({ day: savedDay, item: fallbackItem })

    expect(shiftPlansApi.setLead).toHaveBeenCalledWith('2026-11-10', 2, null)
    expect(wrapper.emitted('saved')).toEqual([[savedDay, {
      date: '2026-11-10', position: 2, request: expect.any(Number)
    }]])
    expect(wrapper.props('item')).toMatchObject({
      id: 'item-1', shiftLeadSource: source, operatorAssignmentCount: 2, closureStatus: 'Draft'
    })
    expect(wrapper.vm.selectedExplicitLeadId).toBeNull()
    const emptyOption = wrapper.findAllComponents({ name: 'ElOption' })
      .find(option => option.props('value') === '')
    expect(emptyOption?.props('label')).toBe(label)
  })

  it('показывает loading на время сохранения и учитывает внешний saving', async () => {
    const request = deferred()
    shiftPlansApi.setLead.mockReturnValue(request.promise)
    const wrapper = mountDialog()
    await nextTick()

    button('save-lead').click()
    await nextTick()
    expect(wrapper.vm.isSaving).toBe(true)
    expect(button('save-lead').disabled).toBe(true)
    expect(button('save-lead').classList).toContain('is-loading')
    button('save-lead').click()
    await wrapper.vm.saveLead()
    expect(shiftPlansApi.setLead).toHaveBeenCalledTimes(1)

    request.resolve({ data: day })
    await flushPromises()
    await wrapper.setProps({ saving: true })
    expect(wrapper.vm.isSaving).toBe(true)
    expect(button('save-lead').disabled).toBe(true)
  })

  it('при 400 показывает серверное сообщение без reload', async () => {
    shiftPlansApi.setLead.mockRejectedValue({
      response: { status: 400, data: { message: 'Смена уже началась' } }
    })
    const wrapper = mountDialog()
    await nextTick()

    button('save-lead').click()
    await flushPromises()

    expect(ElMessage.error).toHaveBeenCalledWith('Смена уже началась')
    expect(wrapper.emitted('reload')).toBeUndefined()
  })

  it('при 409 показывает сообщение и запрашивает полную перезагрузку', async () => {
    shiftPlansApi.setLead.mockRejectedValue({
      response: { status: 409, data: { message: 'План смены уже изменён другим пользователем' } }
    })
    const wrapper = mountDialog()
    await nextTick()

    button('save-lead').click()
    await flushPromises()

    expect(ElMessage.error).toHaveBeenCalledWith('План смены уже изменён другим пользователем')
    expect(wrapper.emitted('reload')).toEqual([[]])
  })

  it('не публикует позднее сохранение A после закрытия и сохранения B', async () => {
    const requestA = deferred()
    const requestB = deferred()
    shiftPlansApi.setLead
      .mockImplementationOnce(() => requestA.promise)
      .mockImplementationOnce(() => requestB.promise)
    const dayB = { ...day, shiftDate: '2026-11-11' }
    const itemB = makeItem({ id: 'item-b', position: 1, shiftLeadId: null, shiftLeadName: null, shiftLeadSource: null })
    const savedDayA = { ...day, items: [makeItem()] }
    const savedDayB = { ...dayB, items: [makeItem({ id: 'item-b', position: 1 })] }
    const wrapper = mountDialog()
    await nextTick()

    button('save-lead').click()
    await nextTick()
    expect(shiftPlansApi.setLead).toHaveBeenCalledWith('2026-11-10', 2, 'lead-1')

    await wrapper.setProps({ modelValue: false })
    await wrapper.setProps({ modelValue: true, day: dayB, item: itemB })
    wrapper.vm.selectedExplicitLeadId = 'manager-current'
    await nextTick()
    expect(button('save-lead').disabled).toBe(false)
    button('save-lead').click()
    await nextTick()
    expect(shiftPlansApi.setLead).toHaveBeenCalledTimes(2)
    expect(shiftPlansApi.setLead).toHaveBeenLastCalledWith('2026-11-11', 1, 'manager-current')

    requestB.resolve({ data: savedDayB })
    await flushPromises()
    requestA.resolve({ data: savedDayA })
    await flushPromises()

    expect((wrapper.emitted('saved') ?? []).map(args => args[0])).toEqual([savedDayB])
    expect(wrapper.props('day').shiftDate).toBe('2026-11-11')
    expect(wrapper.props('item').id).toBe('item-b')
  })

  it('показывает индикаторы закрытия, сирот и отсутствия старшего только по серверным флагам', async () => {
    const wrapper = mountDialog(makeItem({
      closureStatus: 'Closed', hasOrphanOutput: true, hasMissingLeadWarning: false
    }))
    await nextTick()

    expect(document.body.querySelector('[data-test="closure-status"]').textContent).toBe('Закрыта')
    expect(document.body.querySelector('[data-test="orphan-output"]').textContent)
      .toBe('Есть выпуск без литейщика')

    await wrapper.setProps({ item: makeItem({ closureStatus: 'NotClosed', hasOrphanOutput: false }) })
    expect(document.body.querySelector('[data-test="closure-status"]').textContent).toBe('Не закрыта')
    expect(document.body.querySelector('[data-test="orphan-output"]')).toBeNull()

    await wrapper.setProps({ item: makeItem({ closureStatus: 'Draft' }) })
    expect(document.body.querySelector('[data-test="closure-status"]')).toBeNull()

    await wrapper.setProps({
      item: makeItem({
        id: null, shiftLeadId: null, shiftLeadName: null, shiftLeadSource: null, state: 'Current',
        closureStatus: null, hasMissingLeadWarning: true
      })
    })
    expect(document.body.querySelector('[data-test="closure-status"]')).toBeNull()
    expect(document.body.querySelector('[data-test="missing-lead"]').textContent)
      .toBe('Не назначен старший')
  })

  it('показывает ссылку на сменный рапорт для materialized и virtual позиции со старшим', async () => {
    const wrapper = mountDialog()
    await nextTick()

    const link = document.body.querySelector('[data-test="open-closure"]')
    expect(link).not.toBeNull()
    expect(link.getAttribute('href')).toBe('/shift-lead/items/item-1/report')
    expect(link.textContent).toContain('Сменный рапорт')

    await wrapper.setProps({ item: makeItem({ id: null, shiftLeadSource: 'Default' }) })
    expect(document.body.querySelector('[data-test="open-closure"]')).not.toBeNull()
    await wrapper.setProps({ item: makeItem({ shiftLeadId: null, shiftLeadName: null, shiftLeadSource: null }) })
    expect(document.body.querySelector('[data-test="open-closure"]')).toBeNull()
  })

  it('Manager назначает себя старшим текущей virtual позиции и открывает рапорт после saved', async () => {
    const virtualItem = makeItem({
      id: null, shiftLeadId: null, shiftLeadName: null, shiftLeadSource: null, state: 'Current',
      canEditLead: true, closureStatus: null, hasMissingLeadWarning: true
    })
    const savedItem = makeItem({
      id: 'materialized-item', shiftLeadId: 'manager-current', shiftLeadName: 'Менеджер Текущий',
      state: 'Current', canEditLead: false, closureStatus: 'Draft', hasMissingLeadWarning: false
    })
    const savedDay = { ...day, isSaved: true, items: [savedItem] }
    shiftPlansApi.setLead.mockResolvedValue({ data: savedDay })
    const wrapper = mountDialog(virtualItem)
    await nextTick()

    const selfAssign = button('self-assign-lead')
    expect(selfAssign).not.toBeNull()
    expect(selfAssign.textContent).toContain('Назначить себя старшим')
    selfAssign.click()
    await flushPromises()

    expect(shiftPlansApi.setLead).toHaveBeenCalledWith('2026-11-10', 2, 'manager-current')
    expect(wrapper.emitted('saved')).toEqual([[savedDay, {
      date: '2026-11-10', position: 2, request: expect.any(Number)
    }]])

    await wrapper.setProps({ day: savedDay, item: savedItem })
    expect(document.body.querySelector('[data-test="open-closure"]')).not.toBeNull()
  })

  it('не показывает self-action Admin, при существующем старшем и при canEditLead=false', async () => {
    const virtualItem = makeItem({
      id: null, shiftLeadId: null, shiftLeadName: null, shiftLeadSource: null, state: 'Current', canEditLead: true
    })
    const wrapper = mountDialog(virtualItem)
    useAuthStore().user = { id: 'admin-1', role: 'Admin', fullName: 'Администратор' }
    useAuthStore().userRole = 'Admin'
    await nextTick()
    expect(button('self-assign-lead')).toBeNull()

    useAuthStore().user = { id: 'manager-current', role: 'Manager', fullName: 'Менеджер Текущий' }
    useAuthStore().userRole = 'Manager'
    await wrapper.setProps({ item: makeItem() })
    expect(button('self-assign-lead')).toBeNull()

    await wrapper.setProps({ item: { ...virtualItem, canEditLead: false } })
    expect(button('self-assign-lead')).toBeNull()
    expect(shiftPlansApi.setLead).not.toHaveBeenCalled()
  })

  it('открывает ТПА и литейщиков для materialized и virtual смены со старшим', async () => {
    const wrapper = mountDialog()
    await nextTick()
    const link = document.body.querySelector('[data-test="open-staffing"]')
    expect(link).not.toBeNull()
    expect(link.getAttribute('href')).toBe('/shift-lead/items/item-1')
    expect(link.textContent).toContain('Открыть ТПА и литейщиков')

    await wrapper.setProps({ item: makeItem({ id: null, shiftLeadSource: 'Default' }) })
    expect(document.body.querySelector('[data-test="open-staffing"]')).not.toBeNull()
    await wrapper.setProps({ item: makeItem({ shiftLeadId: null, shiftLeadName: null, shiftLeadSource: null }) })
    expect(document.body.querySelector('[data-test="open-staffing"]')).toBeNull()
  })

  it.each([
    ['open-staffing', '/shift-lead/items/materialized-item'],
    ['open-closure', '/shift-lead/items/materialized-item/report']
  ])('virtual %s сначала materialize, затем saved и route по returned itemId', async (testId, path) => {
    const virtualItem = makeItem({ id: null, shiftLeadSource: 'Default' })
    const savedItem = makeItem({ id: 'materialized-item', shiftLeadSource: 'Default' })
    const savedDay = { ...day, isSaved: true, items: [savedItem] }
    shiftPlansApi.materializeShift.mockResolvedValue({ data: savedDay })
    const wrapper = mountDialog(virtualItem)
    await nextTick()
    let savedEventsAtRoute = 0
    routerPush.mockImplementation(() => {
      savedEventsAtRoute = wrapper.emitted('saved')?.length ?? 0
      return Promise.resolve()
    })

    document.body.querySelector(`[data-test="${testId}"]`).click()
    await flushPromises()

    expect(shiftPlansApi.materializeShift).toHaveBeenCalledWith('2026-11-10', 2)
    expect(wrapper.emitted('saved')).toEqual([[savedDay, {
      date: '2026-11-10', position: 2, request: expect.any(Number)
    }]])
    expect(savedEventsAtRoute).toBe(1)
    expect(routerPush).toHaveBeenCalledWith(path)
    expect(shiftPlansApi.setLead).not.toHaveBeenCalled()
  })

  it('после успешного materialize сообщает об ошибке открытия, а не сохранения, при router reject', async () => {
    const savedItem = makeItem({ id: 'materialized-item', shiftLeadSource: 'Default' })
    const savedDay = { ...day, isSaved: true, items: [savedItem] }
    shiftPlansApi.materializeShift.mockResolvedValue({ data: savedDay })
    routerPush.mockRejectedValue(new Error('Lazy route failed'))
    const wrapper = mountDialog(makeItem({ id: null, shiftLeadSource: 'Default' }))
    await nextTick()

    document.body.querySelector('[data-test="open-staffing"]').click()
    await flushPromises()

    expect(shiftPlansApi.materializeShift).toHaveBeenCalledWith('2026-11-10', 2)
    expect(wrapper.emitted('saved')).toEqual([[savedDay, {
      date: '2026-11-10', position: 2, request: expect.any(Number)
    }]])
    expect(routerPush).toHaveBeenCalledWith('/shift-lead/items/materialized-item')
    expect(ElMessage.error).toHaveBeenCalledWith('Смена сохранена, но не удалось открыть её')
    expect(ElMessage.error).not.toHaveBeenCalledWith('Не удалось сохранить смену')
    expect(wrapper.emitted('reload')).toBeUndefined()
  })

  it('ошибка materialize показывает сообщение и не выполняет route', async () => {
    shiftPlansApi.materializeShift.mockRejectedValue({
      response: { status: 400, data: { message: 'Смена уже изменена' } }
    })
    const wrapper = mountDialog(makeItem({ id: null, shiftLeadSource: 'ManagerFallback' }))
    await nextTick()

    document.body.querySelector('[data-test="open-staffing"]').click()
    await flushPromises()

    expect(ElMessage.error).toHaveBeenCalledWith('Смена уже изменена')
    expect(wrapper.emitted('saved')).toBeUndefined()
    expect(routerPush).not.toHaveBeenCalled()
  })
})
