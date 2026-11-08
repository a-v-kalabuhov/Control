// @vitest-environment jsdom
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { flushPromises, mount } from '@vue/test-utils'
import { nextTick } from 'vue'
import ElementPlus, { ElMessage } from 'element-plus'
import ShiftPlanItemDialog from '../ShiftPlanItemDialog.vue'
import { shiftPlansApi } from '@/api/shiftPlans'

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

function mountDialog(item = makeItem(), saving = false) {
  return mount(ShiftPlanItemDialog, {
    props: { modelValue: true, day, item, candidates, saving },
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
    expect(wrapper.emitted('saved')).toEqual([[savedDay]])
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
