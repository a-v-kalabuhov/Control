// @vitest-environment jsdom
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { createPinia, setActivePinia } from 'pinia'
import { ElMessage } from 'element-plus'
import { useShiftStaffingStore } from '../shiftStaffing'
import { useAuthStore } from '../auth'
import { shiftStaffingApi } from '@/api/shiftStaffing'
import { authApi } from '@/api/auth'

vi.mock('@/api/auth', () => ({
  authApi: {
    login: vi.fn(),
    logout: vi.fn(),
    getCurrentUser: vi.fn(),
    refreshToken: vi.fn()
  }
}))

vi.mock('@/router', () => ({ default: { push: vi.fn() } }))

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
    localStorage.clear()
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

  it('foreground navigation к B очищает A и после ошибки не показывает stale editable detail', async () => {
    const request = deferred()
    const store = useShiftStaffingStore()
    store.nearest = summary('item-a')
    store.currentItem = detail('item-a')
    shiftStaffingApi.item.mockReturnValue(request.promise)

    const load = store.loadItem('item-b')

    expect(store.currentItem).toBeNull()
    expect(store.nearest).toBeNull()
    expect(store.loading).toBe(true)
    expect(store.refreshing).toBe(false)
    request.reject({ response: { status: 500, data: { message: 'Смена недоступна' } } })
    const result = await load

    expect(result.success).toBe(false)
    expect(store.currentItem).toBeNull()
    expect(store.nearest).toBeNull()
    expect(store.canEditOperators).toBe(false)
    expect(store.loading).toBe(false)
  })

  it('loadNearest атомарно заменяет summary и detail только после успешного ответа item', async () => {
    const request = deferred()
    const store = useShiftStaffingStore()
    store.nearest = summary('item-a')
    store.currentItem = detail('item-a')
    shiftStaffingApi.nearest.mockResolvedValue({ data: { item: summary('item-b') } })
    shiftStaffingApi.item.mockReturnValue(request.promise)

    const load = store.loadNearest()
    await vi.waitFor(() => expect(shiftStaffingApi.item).toHaveBeenCalledWith('item-b'))

    expect(store.nearest.itemId).toBe('item-a')
    expect(store.currentItem.shift.itemId).toBe('item-a')
    request.resolve({ data: detail('item-b') })
    await load

    expect(store.nearest.itemId).toBe('item-b')
    expect(store.currentItem.shift.itemId).toBe('item-b')
  })

  it('loadNearest при ошибке detail B сохраняет согласованную пару A/A', async () => {
    const store = useShiftStaffingStore()
    store.nearest = summary('item-a')
    store.currentItem = detail('item-a')
    shiftStaffingApi.nearest.mockResolvedValue({ data: { item: summary('item-b') } })
    shiftStaffingApi.item.mockRejectedValue({
      response: { status: 500, data: { message: 'Detail B недоступен' } }
    })

    const result = await store.loadNearest()

    expect(result.success).toBe(false)
    expect(store.nearest.itemId).toBe('item-a')
    expect(store.currentItem.shift.itemId).toBe('item-a')
  })

  it('loadNearest без next при ошибке завершившегося reload очищает editable stale detail', async () => {
    const store = useShiftStaffingStore()
    store.nearest = summary('item-a')
    store.currentItem = detail('item-a')
    shiftStaffingApi.nearest.mockResolvedValue({ data: { item: null } })
    shiftStaffingApi.item.mockRejectedValue({
      response: { status: 500, data: { message: 'Завершившаяся смена недоступна' } }
    })

    const result = await store.loadNearest()

    expect(result.success).toBe(false)
    expect(store.nearest).toBeNull()
    expect(store.currentItem).toBeNull()
    expect(store.canEditOperators).toBe(false)
  })

  it('mutation A после navigation к B не перезагружает A и не перезаписывает B', async () => {
    const mutationRequest = deferred()
    const store = useShiftStaffingStore()
    store.currentItem = detail('item-a')
    shiftStaffingApi.assign.mockReturnValue(mutationRequest.promise)
    shiftStaffingApi.item.mockResolvedValue({ data: detail('item-b') })

    const mutation = store.assign('item-a', 'imm-a', 'operator-1')
    await store.loadItem('item-b')
    expect(store.saving).toBe(false)

    mutationRequest.resolve({ data: { id: 'assignment-a' } })
    const result = await mutation

    expect(result.stale).toBe(true)
    expect(store.currentItem.shift.itemId).toBe('item-b')
    expect(shiftStaffingApi.item).toHaveBeenCalledTimes(1)
    expect(shiftStaffingApi.item).toHaveBeenCalledWith('item-b')
  })

  it('navigation во время success reload mutation A возвращает stale и сохраняет B', async () => {
    const reloadA = deferred()
    const store = useShiftStaffingStore()
    store.currentItem = detail('item-a')
    shiftStaffingApi.assign.mockResolvedValue({ data: { id: 'assignment-a' } })
    shiftStaffingApi.item
      .mockReturnValueOnce(reloadA.promise)
      .mockResolvedValueOnce({ data: detail('item-b') })

    const mutation = store.assign('item-a', 'imm-a', 'operator-1')
    await vi.waitFor(() => expect(shiftStaffingApi.item).toHaveBeenCalledWith('item-a'))
    await store.loadItem('item-b')
    reloadA.resolve({ data: detail('item-a') })
    const result = await mutation

    expect(result.stale).toBe(true)
    expect(store.currentItem.shift.itemId).toBe('item-b')
    expect(shiftStaffingApi.item).toHaveBeenCalledTimes(2)
  })

  it('поздний 403 mutation A не запрещает редактирование B и не показывает чужую ошибку', async () => {
    const mutationRequest = deferred()
    const lateReload = deferred()
    const store = useShiftStaffingStore()
    store.currentItem = detail('item-a')
    shiftStaffingApi.assign.mockReturnValue(mutationRequest.promise)
    shiftStaffingApi.item
      .mockResolvedValueOnce({ data: detail('item-b') })
      .mockReturnValueOnce(lateReload.promise)

    const mutation = store.assign('item-a', 'imm-a', 'operator-1')
    await store.loadItem('item-b')
    mutationRequest.reject({
      response: { status: 403, data: { message: 'Доступ к A запрещён' } }
    })
    await Promise.resolve()
    await Promise.resolve()

    expect(store.currentItem.shift.itemId).toBe('item-b')
    expect(store.canEditOperators).toBe(true)
    expect(shiftStaffingApi.item).toHaveBeenCalledTimes(1)
    expect(errorMessage).not.toHaveBeenCalled()
    const result = await mutation
    expect(result.stale).toBe(true)
  })

  it('history отбрасывает stale response и не сбрасывает spinner item load', async () => {
    const oldHistory = deferred()
    const newHistory = deferred()
    const itemRequest = deferred()
    shiftStaffingApi.history
      .mockReturnValueOnce(oldHistory.promise)
      .mockReturnValueOnce(newHistory.promise)
    shiftStaffingApi.item.mockReturnValue(itemRequest.promise)
    const store = useShiftStaffingStore()

    const itemLoad = store.loadItem('item-1')
    const oldLoad = store.loadHistory()
    const newLoad = store.loadHistory()
    newHistory.resolve({ data: [summary('new-history')] })
    await newLoad

    expect(store.history.map(item => item.itemId)).toEqual(['new-history'])
    expect(store.historyLoading).toBe(false)
    expect(store.loading).toBe(true)

    oldHistory.resolve({ data: [summary('old-history')] })
    await oldLoad
    expect(store.history.map(item => item.itemId)).toEqual(['new-history'])
    expect(store.historyLoading).toBe(false)
    expect(store.loading).toBe(true)

    itemRequest.resolve({ data: detail('item-1') })
    await itemLoad
    expect(store.loading).toBe(false)
  })

  it('activateItem при forced navigation отменяет in-flight nearest B и изолирует item A', async () => {
    const nearestRequest = deferred()
    shiftStaffingApi.nearest.mockReturnValue(nearestRequest.promise)
    const store = useShiftStaffingStore()
    const pendingNearest = store.loadNearest()

    store.activateItem('item-a')
    shiftStaffingApi.item.mockResolvedValue({ data: detail('item-a') })
    await store.loadItem('item-a')
    nearestRequest.resolve({ data: { item: summary('item-b') } })
    const nearestResult = await pendingNearest

    expect(nearestResult.stale).toBe(true)
    expect(store.currentItem.shift.itemId).toBe('item-a')
    expect(store.activeItemId).toBe('item-a')
    expect(shiftStaffingApi.item).toHaveBeenCalledWith('item-a')
    expect(shiftStaffingApi.item).not.toHaveBeenCalledWith('item-b')
  })

  it.each(['assign', 'unassign'])('%s не отправляется при рассинхроне activeItemId/currentItem', async action => {
    const store = useShiftStaffingStore()
    store.activateItem('item-a')
    store.currentItem = detail('item-b')
    shiftStaffingApi.assign.mockResolvedValue({ data: {} })
    shiftStaffingApi.unassign.mockResolvedValue({ data: {} })

    const result = action === 'assign'
      ? await store.assign('item-a', 'imm-a', 'operator-1')
      : await store.unassign('assignment-b')

    expect(result.stale).toBe(true)
    expect(shiftStaffingApi[action]).not.toHaveBeenCalled()
    expect(store.activeItemId).toBe('item-a')
    expect(store.currentItem.shift.itemId).toBe('item-b')
  })

  it('activateNearest отбрасывает unrelated history и nearest=null не перезагружает её', async () => {
    const store = useShiftStaffingStore()
    store.nearest = null
    store.currentItem = detail('history-item', {
      shift: summary('history-item', { state: 'Historical', canEditOperators: false })
    })
    shiftStaffingApi.nearest.mockResolvedValue({ data: { item: null } })

    store.activateNearest()
    await store.loadNearest()

    expect(store.currentItem).toBeNull()
    expect(store.nearest).toBeNull()
    expect(shiftStaffingApi.item).not.toHaveBeenCalled()
  })

  it.each([403, 404])('фоновый loadItem %s очищает item/nearest и отменяет pending mutation', async status => {
    const mutationRequest = deferred()
    const store = useShiftStaffingStore()
    store.ownerUserId = 'lead-a'
    store.nearest = summary('item-1')
    store.nearestContextItemId = 'item-1'
    store.currentItem = detail('item-1')
    store.activateItem('item-1')
    shiftStaffingApi.assign.mockReturnValue(mutationRequest.promise)
    shiftStaffingApi.item.mockRejectedValue({
      response: { status, data: { message: 'Смена больше недоступна' } }
    })
    const mutation = store.assign('item-1', 'imm-1', 'operator-1')
    await vi.waitFor(() => expect(shiftStaffingApi.assign).toHaveBeenCalledTimes(1))
    const mutationGeneration = store.mutationSeq

    const result = await store.loadItem('item-1')

    expect(result.success).toBe(false)
    expect(store.currentItem).toBeNull()
    expect(store.nearest).toBeNull()
    expect(store.nearestContextItemId).toBeNull()
    expect(store.canEditOperators).toBe(false)
    expect(store.mutationSeq).toBeGreaterThan(mutationGeneration)
    expect(store.saving).toBe(false)
    expect(store.refreshing).toBe(false)

    mutationRequest.resolve({ data: { id: 'assignment-late' } })
    expect((await mutation).stale).toBe(true)
    expect(shiftStaffingApi.item).toHaveBeenCalledTimes(1)
  })

  it.each([403, 404])('фоновый loadNearest при %s detail не оставляет прежнюю защищённую пару', async status => {
    const store = useShiftStaffingStore()
    store.nearest = summary('item-a')
    store.currentItem = detail('item-a')
    store.nearestContextItemId = 'item-a'
    shiftStaffingApi.nearest.mockResolvedValue({ data: { item: summary('item-b') } })
    shiftStaffingApi.item.mockRejectedValue({
      response: { status, data: { message: 'Доступ к смене отозван' } }
    })

    const result = await store.loadNearest()

    expect(result.success).toBe(false)
    expect(store.nearest).toBeNull()
    expect(store.currentItem).toBeNull()
    expect(store.nearestContextItemId).toBeNull()
    expect(store.canEditOperators).toBe(false)
    expect(store.refreshing).toBe(false)
  })

  it('фоновый loadItem при обычной ошибке 500 сохраняет прежний read model', async () => {
    const store = useShiftStaffingStore()
    store.nearest = summary('item-a')
    store.currentItem = detail('item-a')
    store.nearestContextItemId = 'item-a'
    store.activateItem('item-a')
    shiftStaffingApi.item.mockRejectedValue({
      response: { status: 500, data: { message: 'Временная ошибка сервера' } }
    })

    const result = await store.loadItem('item-a')

    expect(result.success).toBe(false)
    expect(store.currentItem.shift.itemId).toBe('item-a')
    expect(store.nearest.itemId).toBe('item-a')
    expect(store.canEditOperators).toBe(true)
    expect(store.refreshing).toBe(false)
  })

  it('A logout → B login не показывает B кеш A даже после ошибки первого запроса', async () => {
    const store = useShiftStaffingStore()
    const auth = useAuthStore()
    store.ownerUserId = 'lead-a'
    store.nearest = summary('item-a')
    store.nearestContextItemId = 'item-a'
    store.currentItem = detail('item-a', {
      imms: [{ immId: 'imm-a', name: 'ТПА A' }]
    })
    store.history = [summary('history-a', { state: 'Historical', canEditOperators: false })]
    store.operators = [{ id: 'operator-a', fullName: 'Литейщик A' }]
    auth.user = { id: 'lead-a', role: 'ShiftLead' }
    auth.userRole = 'ShiftLead'
    auth.isAuthenticated = true
    authApi.logout.mockResolvedValue({})
    authApi.login.mockResolvedValue({
      data: {
        accessToken: 'token-b',
        refreshToken: 'refresh-b',
        user: { id: 'lead-b', role: 'ShiftLead' }
      }
    })

    await auth.logout()

    expect(store.currentItem).toBeNull()
    expect(store.nearest).toBeNull()
    expect(store.history).toEqual([])
    expect(store.operators).toEqual([])

    await auth.login({ username: 'lead-b', password: 'secret' })
    shiftStaffingApi.nearest.mockRejectedValue({
      response: { status: 500, data: { message: 'Сеть временно недоступна' } }
    })
    const result = await store.loadNearest()

    expect(result.success).toBe(false)
    expect(store.ownerUserId).toBe('lead-b')
    expect(store.currentItem).toBeNull()
    expect(store.nearest).toBeNull()
    expect(store.nearestContextItemId).toBeNull()
    expect(store.history).toEqual([])
    expect(store.operators).toEqual([])
    expect(store.canEditOperators).toBe(false)
  })

  it('смена аккаунта отбрасывает поздний ответ operators прежнего пользователя', async () => {
    const request = deferred()
    const store = useShiftStaffingStore()
    const auth = useAuthStore()
    store.ownerUserId = 'lead-a'
    shiftStaffingApi.operators.mockReturnValue(request.promise)
    const pending = store.loadOperators()
    authApi.login.mockResolvedValue({
      data: {
        accessToken: 'token-b',
        refreshToken: 'refresh-b',
        user: { id: 'lead-b', role: 'ShiftLead' }
      }
    })

    await auth.login({ username: 'lead-b', password: 'secret' })
    request.resolve({ data: [{ id: 'operator-a', fullName: 'Литейщик A' }] })
    const result = await pending

    expect(result.stale).toBe(true)
    expect(store.ownerUserId).toBe('lead-b')
    expect(store.operators).toEqual([])
  })
})
