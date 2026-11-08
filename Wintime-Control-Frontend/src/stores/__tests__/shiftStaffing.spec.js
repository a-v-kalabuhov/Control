// @vitest-environment jsdom
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { createPinia, setActivePinia } from 'pinia'
import { ElMessage } from 'element-plus'
import { useShiftStaffingStore } from '../shiftStaffing'
import { shiftStaffingApi } from '@/api/shiftStaffing'

vi.mock('@/api/shiftStaffing', () => ({
  shiftStaffingApi: {
    nearest: vi.fn(),
    history: vi.fn(),
    item: vi.fn(),
    task: vi.fn(),
    operators: vi.fn(),
    assign: vi.fn(),
    unassign: vi.fn()
  }
}))

function summary(itemId = 'item-1', overrides = {}) {
  return {
    itemId,
    shiftDate: '2026-11-08',
    position: 1,
    startAtUtc: '2026-11-08T08:00:00Z',
    endAtUtc: '2026-11-08T20:00:00Z',
    state: 'Current',
    canEditOperators: true,
    ...overrides
  }
}

function detail(itemId = 'item-1', overrides = {}) {
  return {
    shift: summary(itemId),
    shiftLeadId: 'lead-1',
    shiftLeadName: 'Старший Смены',
    imms: [],
    ...overrides
  }
}

function deferred() {
  let resolve
  let reject
  const promise = new Promise((res, rej) => {
    resolve = res
    reject = rej
  })
  return { promise, resolve, reject }
}

describe('shiftStaffing store', () => {
  let errorMessage

  beforeEach(() => {
    setActivePinia(createPinia())
    vi.clearAllMocks()
    errorMessage = vi.spyOn(ElMessage, 'error').mockImplementation(() => {})
  })

  afterEach(() => {
    vi.restoreAllMocks()
  })

  it('loadNearest сохраняет summary и загружает detail', async () => {
    const nearest = summary()
    const item = detail()
    shiftStaffingApi.nearest.mockResolvedValue({ data: { item: nearest } })
    shiftStaffingApi.item.mockResolvedValue({ data: item })
    const store = useShiftStaffingStore()

    await store.loadNearest()

    expect(store.nearest).toEqual(nearest)
    expect(store.currentItem).toEqual(item)
    expect(shiftStaffingApi.item).toHaveBeenCalledWith('item-1')
  })

  it('loadNearest без nearest перезагружает прежний item и применяет read-only флаг сервера', async () => {
    const store = useShiftStaffingStore()
    store.nearest = summary()
    store.currentItem = detail()
    const ended = detail('item-1', {
      shift: summary('item-1', { state: 'Historical', canEditOperators: false })
    })
    shiftStaffingApi.nearest.mockResolvedValue({ data: { item: null } })
    shiftStaffingApi.item.mockResolvedValue({ data: ended })

    await store.loadNearest()

    expect(store.nearest).toBeNull()
    expect(shiftStaffingApi.item).toHaveBeenCalledWith('item-1')
    expect(store.currentItem).toEqual(ended)
    expect(store.canEditOperators).toBe(false)
  })

  it('loadNearest без nearest и прежнего item оставляет empty state', async () => {
    shiftStaffingApi.nearest.mockResolvedValue({ data: { item: null } })
    const store = useShiftStaffingStore()

    await store.loadNearest()

    expect(store.nearest).toBeNull()
    expect(store.currentItem).toBeNull()
    expect(shiftStaffingApi.item).not.toHaveBeenCalled()
  })

  it('устаревший loadItem не перезаписывает более новый item', async () => {
    const oldRequest = deferred()
    const newRequest = deferred()
    shiftStaffingApi.item
      .mockReturnValueOnce(oldRequest.promise)
      .mockReturnValueOnce(newRequest.promise)
    const store = useShiftStaffingStore()

    const oldLoad = store.loadItem('old-item')
    const newLoad = store.loadItem('new-item')
    newRequest.resolve({ data: detail('new-item') })
    await newLoad
    oldRequest.resolve({ data: detail('old-item') })
    await oldLoad

    expect(store.currentItem.shift.itemId).toBe('new-item')
    expect(store.loading).toBe(false)
    expect(store.refreshing).toBe(false)
  })

  it('фоновый loadItem выставляет refreshing без первичного spinner', async () => {
    const request = deferred()
    shiftStaffingApi.item.mockReturnValue(request.promise)
    const store = useShiftStaffingStore()
    store.currentItem = detail()

    const load = store.loadItem('item-1')

    expect(store.refreshing).toBe(true)
    expect(store.loading).toBe(false)
    request.resolve({ data: detail() })
    await load
    expect(store.refreshing).toBe(false)
  })

  it('первичный loadItem показывает loading', async () => {
    const request = deferred()
    shiftStaffingApi.item.mockReturnValue(request.promise)
    const store = useShiftStaffingStore()

    const load = store.loadItem('item-1')

    expect(store.loading).toBe(true)
    expect(store.refreshing).toBe(false)
    request.resolve({ data: detail() })
    await load
    expect(store.loading).toBe(false)
  })

  it('loadHistory и loadOperators сохраняют ответы API', async () => {
    const history = [summary('history-1', { state: 'Historical', canEditOperators: false })]
    const operators = [{ id: 'operator-1', fullName: 'Литейщик Без Логина' }]
    shiftStaffingApi.history.mockResolvedValue({ data: history })
    shiftStaffingApi.operators.mockResolvedValue({ data: operators })
    const store = useShiftStaffingStore()

    await store.loadHistory()
    await store.loadOperators()

    expect(store.history).toEqual(history)
    expect(store.operators).toEqual(operators)
  })

  it('assign отправляет только item, ТПА и Operator и перезагружает item', async () => {
    const store = useShiftStaffingStore()
    store.currentItem = detail()
    shiftStaffingApi.assign.mockResolvedValue({ data: { id: 'assignment-1' } })
    shiftStaffingApi.item.mockResolvedValue({ data: detail() })

    const result = await store.assign('item-1', 'imm-1', 'operator-1')

    expect(result.success).toBe(true)
    expect(shiftStaffingApi.assign).toHaveBeenCalledWith('item-1', 'imm-1', 'operator-1')
    expect(shiftStaffingApi.assign.mock.calls[0]).toHaveLength(3)
    expect(shiftStaffingApi.item).toHaveBeenCalledWith('item-1')
  })

  it('unassign отправляет только assignmentId и перезагружает текущий item', async () => {
    const store = useShiftStaffingStore()
    store.currentItem = detail()
    shiftStaffingApi.unassign.mockResolvedValue({ data: { id: 'assignment-1' } })
    shiftStaffingApi.item.mockResolvedValue({ data: detail() })

    const result = await store.unassign('assignment-1')

    expect(result.success).toBe(true)
    expect(shiftStaffingApi.unassign).toHaveBeenCalledWith('assignment-1')
    expect(shiftStaffingApi.unassign.mock.calls[0]).toHaveLength(1)
    expect(shiftStaffingApi.item).toHaveBeenCalledWith('item-1')
  })

  it('400 показывает доменное сообщение и не перезагружает item', async () => {
    const store = useShiftStaffingStore()
    store.currentItem = detail()
    shiftStaffingApi.assign.mockRejectedValue({
      response: { status: 400, data: { message: 'На ТПА уже назначены два литейщика' } }
    })

    const result = await store.assign('item-1', 'imm-1', 'operator-1')

    expect(result).toEqual({ success: false, message: 'На ТПА уже назначены два литейщика' })
    expect(errorMessage).toHaveBeenCalledWith('На ТПА уже назначены два литейщика')
    expect(shiftStaffingApi.item).not.toHaveBeenCalled()
  })

  it('403 сразу запрещает редактирование и перезагружает item', async () => {
    const reload = deferred()
    const store = useShiftStaffingStore()
    store.currentItem = detail()
    shiftStaffingApi.assign.mockRejectedValue({
      response: { status: 403, data: { message: 'Эта смена больше не доступна для редактирования' } }
    })
    shiftStaffingApi.item.mockReturnValue(reload.promise)

    const mutation = store.assign('item-1', 'imm-1', 'operator-1')
    await vi.waitFor(() => expect(store.canEditOperators).toBe(false))
    expect(shiftStaffingApi.item).toHaveBeenCalledWith('item-1')

    reload.resolve({
      data: detail('item-1', { shift: summary('item-1', { canEditOperators: false }) })
    })
    const result = await mutation
    expect(result.success).toBe(false)
    expect(store.canEditOperators).toBe(false)
  })

  it.each(['assign', 'unassign'])('409 при %s показывает сообщение и обязательно перезагружает item', async action => {
    const store = useShiftStaffingStore()
    store.currentItem = detail()
    shiftStaffingApi[action].mockRejectedValue({
      response: { status: 409, data: { message: 'Состав литейщиков уже изменился' } }
    })
    shiftStaffingApi.item.mockResolvedValue({ data: detail() })

    const result = action === 'assign'
      ? await store.assign('item-1', 'imm-1', 'operator-1')
      : await store.unassign('assignment-1')

    expect(result.success).toBe(false)
    expect(errorMessage).toHaveBeenCalledWith('Состав литейщиков уже изменился')
    expect(shiftStaffingApi.item).toHaveBeenCalledWith('item-1')
  })

  it('pending mutation блокирует повторный submit', async () => {
    const request = deferred()
    const store = useShiftStaffingStore()
    store.currentItem = detail()
    shiftStaffingApi.assign.mockReturnValue(request.promise)
    shiftStaffingApi.item.mockResolvedValue({ data: detail() })

    const first = store.assign('item-1', 'imm-1', 'operator-1')
    const second = await store.assign('item-1', 'imm-1', 'operator-1')

    expect(second).toEqual({ success: false, pending: true })
    expect(shiftStaffingApi.assign).toHaveBeenCalledTimes(1)
    expect(store.saving).toBe(true)
    request.resolve({ data: { id: 'assignment-1' } })
    await first
    expect(store.saving).toBe(false)
    expect(shiftStaffingApi.item).toHaveBeenCalledTimes(1)
  })
})
