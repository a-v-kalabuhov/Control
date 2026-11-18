// @vitest-environment jsdom
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { flushPromises, mount } from '@vue/test-utils'
import ElementPlus, { ElMessage } from 'element-plus'
import { createPinia, setActivePinia } from 'pinia'
import { nextTick } from 'vue'
import dayjs from 'dayjs'
import OperatorIntervalDialog from '../OperatorIntervalDialog.vue'
import { useShiftStaffingStore } from '@/stores/shiftStaffing'
import { shiftStaffingApi } from '@/api/shiftStaffing'

vi.mock('@/api/shiftStaffing', () => ({
  shiftStaffingApi: {
    nearest: vi.fn(), history: vi.fn(), item: vi.fn(), task: vi.fn(), operators: vi.fn(),
    assignOperator: vi.fn(), unassignOperator: vi.fn(), updateOperatorInterval: vi.fn()
  }
}))

const shift = {
  itemId: 'item-1', shiftDate: '2026-11-18', position: 1,
  startAtUtc: '2026-11-18T05:00:00Z', endAtUtc: '2026-11-18T17:00:00Z',
  state: 'Current', canEditOperators: true
}
const assignment = {
  id: 'assignment-1', immId: 'imm-1', operatorId: 'op-1', operatorName: 'Иванов', slot: 1,
  assignedAt: '2026-11-18T06:00:00Z', unassignedAt: null
}

async function mountDialog(props = {}) {
  const wrapper = mount(OperatorIntervalDialog, {
    props: {
      modelValue: true,
      mode: 'create',
      itemId: 'item-1',
      immId: 'imm-1',
      operators: [{ id: 'op-1', fullName: 'Иванов', employeeId: '001' }],
      canEdit: true,
      ...props
    },
    global: { plugins: [ElementPlus] }
  })
  await nextTick()
  return wrapper
}

async function chooseOperator(wrapper, id = 'op-1') {
  await wrapper.findComponent('[data-test="operator-select"]').vm.$emit('update:modelValue', id)
}

async function submit(wrapper) {
  await wrapper.get('[data-test="submit-interval"]').trigger('click')
  await flushPromises()
}

describe('OperatorIntervalDialog', () => {
  let wrapper

  beforeEach(() => {
    setActivePinia(createPinia())
    vi.clearAllMocks()
    const store = useShiftStaffingStore()
    store.activateItem('item-1')
    store.currentItem = { shift, imms: [] }
    shiftStaffingApi.item.mockResolvedValue({ data: { shift, imms: [] } })
    shiftStaffingApi.assignOperator.mockResolvedValue({ data: assignment })
    shiftStaffingApi.unassignOperator.mockResolvedValue({ data: assignment })
    shiftStaffingApi.updateOperatorInterval.mockResolvedValue({ data: assignment })
    vi.spyOn(ElMessage, 'error').mockImplementation(() => {})
  })

  afterEach(() => {
    wrapper?.unmount()
    wrapper = null
    vi.restoreAllMocks()
    document.body.innerHTML = ''
  })

  it('create с пустыми границами использует server defaults и перезагружает item/report', async () => {
    wrapper = await mountDialog()
    await chooseOperator(wrapper)
    await submit(wrapper)

    expect(shiftStaffingApi.assignOperator).toHaveBeenCalledWith('item-1', {
      immId: 'imm-1', operatorId: 'op-1', assignedAt: null, unassignedAt: null
    })
    expect(shiftStaffingApi.item).toHaveBeenCalledWith('item-1')
    expect(wrapper.emitted('saved')).toHaveLength(1)
    expect(wrapper.emitted('reload')).toHaveLength(1)
  })

  it('create для сироты предзаполняет границы в browser timezone и отправляет ISO UTC', async () => {
    const initialInterval = {
      assignedAt: '2026-11-18T06:15:00Z',
      unassignedAt: '2026-11-18T08:45:00Z'
    }
    wrapper = await mountDialog({ initialInterval })

    expect(wrapper.get('[data-test="assigned-at"]').element.value)
      .toBe(dayjs(initialInterval.assignedAt).format('YYYY-MM-DDTHH:mm'))
    expect(wrapper.get('[data-test="unassigned-at"]').element.value)
      .toBe(dayjs(initialInterval.unassignedAt).format('YYYY-MM-DDTHH:mm'))
    await chooseOperator(wrapper)
    await submit(wrapper)

    expect(shiftStaffingApi.assignOperator).toHaveBeenCalledWith('item-1', {
      immId: 'imm-1', operatorId: 'op-1',
      assignedAt: dayjs(initialInterval.assignedAt).toISOString(),
      unassignedAt: dayjs(initialInterval.unassignedAt).toISOString()
    })
  })

  it('unassign по умолчанию отправляет строго {}', async () => {
    wrapper = await mountDialog({ mode: 'unassign', assignment })
    await submit(wrapper)

    expect(shiftStaffingApi.unassignOperator).toHaveBeenCalledWith('assignment-1', {})
  })

  it('unassign с выбранным фактическим окончанием отправляет ISO UTC', async () => {
    wrapper = await mountDialog({ mode: 'unassign', assignment })
    const local = dayjs('2026-11-18T10:20:00Z').format('YYYY-MM-DDTHH:mm')
    await wrapper.get('[data-test="unassigned-at"]').setValue(local)
    await submit(wrapper)

    expect(shiftStaffingApi.unassignOperator).toHaveBeenCalledWith('assignment-1', {
      unassignedAt: dayjs(local).toISOString()
    })
  })

  it('edit отправляет PUT с выбранными фактическими границами', async () => {
    wrapper = await mountDialog({ mode: 'edit', assignment })
    const assignedAt = dayjs('2026-11-18T06:30:00Z').format('YYYY-MM-DDTHH:mm')
    const unassignedAt = dayjs('2026-11-18T09:30:00Z').format('YYYY-MM-DDTHH:mm')
    await wrapper.get('[data-test="assigned-at"]').setValue(assignedAt)
    await wrapper.get('[data-test="unassigned-at"]').setValue(unassignedAt)
    await submit(wrapper)

    expect(shiftStaffingApi.updateOperatorInterval).toHaveBeenCalledWith('assignment-1', {
      assignedAt: dayjs(assignedAt).toISOString(),
      unassignedAt: dayjs(unassignedAt).toISOString()
    })
    expect(wrapper.emitted('saved')).toHaveLength(1)
    expect(wrapper.emitted('reload')).toHaveLength(1)
  })

  it('409 показывает сообщение и запрашивает перезагрузку item/report', async () => {
    shiftStaffingApi.assignOperator.mockRejectedValue({
      response: { status: 409, data: { message: 'Занятость и интервалы уже изменились' } }
    })
    wrapper = await mountDialog()
    await chooseOperator(wrapper)
    await submit(wrapper)

    expect(ElMessage.error).toHaveBeenCalledWith('Занятость и интервалы уже изменились')
    expect(shiftStaffingApi.item).toHaveBeenCalledWith('item-1')
    expect(wrapper.emitted('reload')).toHaveLength(1)
    expect(wrapper.emitted('saved')).toBeUndefined()
  })

  it('canEdit=false блокирует mutation controls', async () => {
    wrapper = await mountDialog({ canEdit: false })
    await chooseOperator(wrapper)

    expect(wrapper.get('[data-test="submit-interval"]').attributes('disabled')).toBeDefined()
    await submit(wrapper)
    expect(shiftStaffingApi.assignOperator).not.toHaveBeenCalled()
  })
})
