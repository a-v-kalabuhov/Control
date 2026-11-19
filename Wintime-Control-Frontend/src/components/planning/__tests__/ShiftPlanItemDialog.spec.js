// @vitest-environment jsdom
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { flushPromises, mount } from '@vue/test-utils'
import { nextTick } from 'vue'
import ElementPlus, { ElMessage } from 'element-plus'
import { createPinia, setActivePinia } from 'pinia'
import ShiftPlanItemDialog from '../ShiftPlanItemDialog.vue'
import { shiftPlansApi } from '@/api/shiftPlans'
import { useAuthStore } from '@/stores/auth'

vi.mock('@/api/shiftPlans', () => ({
  shiftPlansApi: { setLead: vi.fn() }
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
    shiftPlansApi.setLead.mockResolvedValue({ data: { ...day, items: [makeItem()] } })
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

  it('canEditLead=false отключает выбор, назначение и очистку', async () => {
    const wrapper = mountDialog(makeItem({ canEditLead: false }))
    await nextTick()

    expect(wrapper.vm.canEdit).toBe(false)
    expect(wrapper.vm.canSave).toBe(false)
    expect(wrapper.vm.canClear).toBe(false)
    expect(button('save-lead').disabled).toBe(true)
    expect(button('clear-lead').disabled).toBe(true)
  })

  it('назначения литейщиков запрещают только очистку, но не переназначение', async () => {
    const wrapper = mountDialog(makeItem({ operatorAssignmentCount: 2 }))
    await nextTick()

    expect(wrapper.vm.canEdit).toBe(true)
    expect(wrapper.vm.canSave).toBe(true)
    expect(wrapper.vm.canClear).toBe(false)
    expect(button('save-lead').disabled).toBe(false)
    expect(button('clear-lead').disabled).toBe(true)
  })

  it('сохраняет переназначение и передаёт сохранённый день родителю', async () => {
    const savedDay = { ...day, items: [makeItem({ shiftLeadId: 'manager-current' })] }
    shiftPlansApi.setLead.mockResolvedValue({ data: savedDay })
    const wrapper = mountDialog()

    wrapper.vm.selectedLeadId = 'manager-current'
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
    const itemB = makeItem({ id: 'item-b', position: 1, shiftLeadId: null, shiftLeadName: null })
    const savedDayA = { ...day, items: [makeItem()] }
    const savedDayB = { ...dayB, items: [makeItem({ id: 'item-b', position: 1 })] }
    const wrapper = mountDialog()
    await nextTick()

    button('save-lead').click()
    await nextTick()
    expect(shiftPlansApi.setLead).toHaveBeenCalledWith('2026-11-10', 2, 'lead-1')

    await wrapper.setProps({ modelValue: false })
    await wrapper.setProps({ modelValue: true, day: dayB, item: itemB })
    wrapper.vm.selectedLeadId = 'manager-current'
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
        id: null, shiftLeadId: null, shiftLeadName: null, state: 'Current',
        closureStatus: null, hasMissingLeadWarning: true
      })
    })
    expect(document.body.querySelector('[data-test="closure-status"]')).toBeNull()
    expect(document.body.querySelector('[data-test="missing-lead"]').textContent)
      .toBe('Не назначен старший')
  })

  it('показывает ссылку на сменный рапорт только для materialized позиции со старшим', async () => {
    const wrapper = mountDialog()
    await nextTick()

    const link = document.body.querySelector('[data-test="open-closure"]')
    expect(link).not.toBeNull()
    expect(link.getAttribute('href')).toBe('/shift-lead/items/item-1/report')
    expect(link.textContent).toContain('Сменный рапорт')

    await wrapper.setProps({ item: makeItem({ id: null }) })
    expect(document.body.querySelector('[data-test="open-closure"]')).toBeNull()
    await wrapper.setProps({ item: makeItem({ shiftLeadId: null, shiftLeadName: null }) })
    expect(document.body.querySelector('[data-test="open-closure"]')).toBeNull()
  })

  it('Manager назначает себя старшим текущей virtual позиции и открывает рапорт после saved', async () => {
    const virtualItem = makeItem({
      id: null, shiftLeadId: null, shiftLeadName: null, state: 'Current',
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
      id: null, shiftLeadId: null, shiftLeadName: null, state: 'Current', canEditLead: true
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

  it('открывает ТПА и литейщиков только для сохранённой смены со старшим', async () => {
    const wrapper = mountDialog()
    await nextTick()
    const link = document.body.querySelector('[data-test="open-staffing"]')
    expect(link).not.toBeNull()
    expect(link.getAttribute('href')).toBe('/shift-lead/items/item-1')
    expect(link.textContent).toContain('Открыть ТПА и литейщиков')

    await wrapper.setProps({ item: makeItem({ id: null }) })
    expect(document.body.querySelector('[data-test="open-staffing"]')).toBeNull()
    await wrapper.setProps({ item: makeItem({ shiftLeadId: null, shiftLeadName: null }) })
    expect(document.body.querySelector('[data-test="open-staffing"]')).toBeNull()
  })
})
