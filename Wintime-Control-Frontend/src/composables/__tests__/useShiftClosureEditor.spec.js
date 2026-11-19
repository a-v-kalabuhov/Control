import { beforeEach, describe, expect, it, vi } from 'vitest'
import { ElMessage } from 'element-plus'
import { useShiftClosureEditor } from '../useShiftClosureEditor'
import * as api from '@/api/shiftClosures'

vi.mock('@/api/shiftClosures', () => ({
  getByItem: vi.fn(),
  saveDraft: vi.fn(),
  close: vi.fn()
}))

const manualFields = [
  'actualPartWeightGrams', 'manualFullCycleSeconds', 'actualPackagingUnit',
  'packagingPlaces', 'manualGoodQuantity', 'manualDefectQuantity'
]

function entry(overrides = {}) {
  return {
    taskId: 'task-1', automaticOutputQuantity: 2,
    actualPartWeightGrams: 4, manualFullCycleSeconds: 50, actualPackagingUnit: 20,
    packagingPlaces: 1.25, manualGoodQuantity: 8, manualDefectQuantity: 1,
    ...overrides
  }
}

function report(overrides = {}) {
  return {
    id: 'closure-1', shiftPlanItemId: 'item-1', state: 'Draft',
    startAtUtc: '2026-11-18T05:00:00Z', endAtUtc: '2026-11-18T17:00:00Z',
    entries: [entry()], ...overrides
  }
}

function response(value) {
  return { data: value }
}

function deferred() {
  let resolve
  const promise = new Promise(res => { resolve = res })
  return { promise, resolve }
}

function createEditor() {
  return useShiftClosureEditor('item-1')
}

describe('useShiftClosureEditor', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    api.getByItem.mockResolvedValue(response(report()))
    api.saveDraft.mockImplementation(async (_itemId, payload) => response(report({
      entries: [entry({ ...payload.entries[0] })]
    })))
    api.close.mockResolvedValue(response(report({ state: 'Closed' })))
    vi.spyOn(ElMessage, 'error').mockImplementation(() => {})
  })

  it('хранит ручной ввод отдельно и пропускает фоновую загрузку при dirty', async () => {
    const editor = createEditor()
    await editor.load()

    editor.updateManual('task-1', 'actualPartWeightGrams', 4.5)

    expect(editor.report.value.entries[0].actualPartWeightGrams).toBe(4.5)
    expect(editor.isDirty.value).toBe(true)
    await editor.load()
    expect(api.getByItem).toHaveBeenCalledTimes(1)
    expect(editor.report.value.entries[0].actualPartWeightGrams).toBe(4.5)
  })

  it('preserveDirty обновляет automatic и не перезаписывает dirty manual по TaskId', async () => {
    const editor = createEditor()
    await editor.load()
    editor.updateManual('task-1', 'manualGoodQuantity', 9)
    api.getByItem.mockResolvedValueOnce(response(report({
      entries: [entry({ automaticOutputQuantity: 7, manualGoodQuantity: 8 })]
    })))

    await editor.load({ preserveDirty: true })

    expect(editor.report.value.entries[0].automaticOutputQuantity).toBe(7)
    expect(editor.report.value.entries[0].manualGoodQuantity).toBe(9)
    expect(editor.isDirty.value).toBe(true)
  })

  it('preserveDirty принимает новые server manual для неизменённой строки', async () => {
    const editor = createEditor()
    await editor.load()
    editor.updateManual('task-1', 'manualGoodQuantity', 9)
    api.getByItem.mockResolvedValueOnce(response(report({
      entries: [entry({ actualPartWeightGrams: 6 })]
    })))

    await editor.load({ preserveDirty: true })

    expect(editor.report.value.entries[0].actualPartWeightGrams).toBe(6)
  })

  it('save отправляет шесть manual fields без computed и перечитывает automatic', async () => {
    const editor = createEditor()
    await editor.load()
    editor.updateManual('task-1', 'manualGoodQuantity', 10)
    api.saveDraft.mockResolvedValueOnce(response(report({
      entries: [entry({ manualGoodQuantity: 10 })]
    })))
    api.getByItem.mockResolvedValueOnce(response(report({
      entries: [entry({ manualGoodQuantity: 10, automaticOutputQuantity: 12 })]
    })))

    const result = await editor.save()

    expect(result).toBe(true)
    expect(api.saveDraft).toHaveBeenCalledWith('item-1', [{
      shiftTaskId: 'task-1',
      actualPartWeightGrams: 4,
      manualFullCycleSeconds: 50,
      actualPackagingUnit: 20,
      packagingPlaces: 1.25,
      manualGoodQuantity: 10,
      manualDefectQuantity: 1
    }])
    expect(api.getByItem).toHaveBeenCalledTimes(2)
    expect(editor.report.value.entries[0].automaticOutputQuantity).toBe(12)
    expect(editor.isDirty.value).toBe(false)
  })

  it('field errors сохраняют ввод и сопоставляются taskId+field', async () => {
    const editor = createEditor()
    await editor.load()
    editor.updateManual('task-1', 'actualPartWeightGrams', null)
    api.saveDraft.mockRejectedValueOnce({ response: { status: 400, data: {
      code: 'required', errors: [{ taskId: 'task-1', field: 'actualPartWeightGrams', code: 'required' }]
    } } })

    const result = await editor.save()

    expect(result).toBe(false)
    expect(editor.fieldErrors.value['task-1:actualPartWeightGrams']).toContain('обязатель')
    expect(editor.report.value.entries[0].actualPartWeightGrams).toBeNull()
    expect(editor.isDirty.value).toBe(true)
    expect(api.getByItem).toHaveBeenCalledTimes(1)
  })

  it('после 409 показывает сообщение и перезагружает состояние без потери ввода', async () => {
    const editor = createEditor()
    await editor.load()
    editor.updateManual('task-1', 'manualGoodQuantity', 11)
    api.saveDraft.mockRejectedValueOnce({ response: { status: 409, data: { message: 'Рапорт изменён' } } })
    api.getByItem.mockResolvedValueOnce(response(report({
      entries: [entry({ automaticOutputQuantity: 5 })]
    })))

    const result = await editor.save()

    expect(result).toBe(false)
    expect(ElMessage.error).toHaveBeenCalledWith('Рапорт изменён')
    expect(api.getByItem).toHaveBeenCalledTimes(2)
    expect(editor.report.value.entries[0].automaticOutputQuantity).toBe(5)
    expect(editor.report.value.entries[0].manualGoodQuantity).toBe(11)
  })

  it('earlyCloseConfirmationRequired возвращается вызывающему без повторного запроса', async () => {
    const editor = createEditor()
    await editor.load()
    api.close.mockRejectedValueOnce({ response: { status: 400, data: {
      code: 'earlyCloseConfirmationRequired', message: 'Нужно подтверждение'
    } } })

    const result = await editor.close({ confirmEarlyClose: false })

    expect(result).toEqual({ earlyCloseConfirmationRequired: true })
    expect(api.close).toHaveBeenCalledTimes(1)
    expect(api.close).toHaveBeenCalledWith('item-1', false)
  })

  it('dirty close сначала сохраняет и перечитывает, затем закрывает', async () => {
    const editor = createEditor()
    await editor.load()
    editor.updateManual('task-1', 'manualGoodQuantity', 10)
    api.saveDraft.mockResolvedValueOnce(response(report({ entries: [entry({ manualGoodQuantity: 10 })] })))
    api.getByItem.mockResolvedValueOnce(response(report({ entries: [entry({ manualGoodQuantity: 10 })] })))

    const result = await editor.close({ confirmEarlyClose: true })

    expect(result).toEqual({ success: true })
    expect(api.saveDraft).toHaveBeenCalledTimes(1)
    expect(api.getByItem).toHaveBeenCalledTimes(2)
    expect(api.close).toHaveBeenCalledWith('item-1', true)
    expect(api.saveDraft.mock.invocationCallOrder[0]).toBeLessThan(api.getByItem.mock.invocationCallOrder[1])
    expect(api.getByItem.mock.invocationCallOrder[1]).toBeLessThan(api.close.mock.invocationCallOrder[0])
  })

  it('ошибка сохранения отменяет close и сохраняет ручной ввод', async () => {
    const editor = createEditor()
    await editor.load()
    editor.updateManual('task-1', 'actualPartWeightGrams', null)
    api.saveDraft.mockRejectedValueOnce({ response: { status: 400, data: {
      errors: [{ taskId: 'task-1', field: 'actualPartWeightGrams', code: 'required' }]
    } } })

    const result = await editor.close({ confirmEarlyClose: true })

    expect(result).toEqual({ success: false })
    expect(api.close).not.toHaveBeenCalled()
    expect(editor.report.value.entries[0].actualPartWeightGrams).toBeNull()
  })

  it('закрытый рапорт не сохраняет и не закрывает повторно', async () => {
    api.getByItem.mockResolvedValueOnce(response(report({ state: 'Closed' })))
    const editor = createEditor()
    await editor.load()

    expect(await editor.save()).toBe(false)
    expect(await editor.close({ confirmEarlyClose: true })).toEqual({ success: false })
    expect(api.saveDraft).not.toHaveBeenCalled()
    expect(api.close).not.toHaveBeenCalled()
  })

  it('revert всех manual fields к server values очищает dirty', async () => {
    const editor = createEditor()
    await editor.load()
    editor.updateManual('task-1', 'manualGoodQuantity', 9)
    editor.updateManual('task-1', 'manualGoodQuantity', 8)

    expect(editor.isDirty.value).toBe(false)
    expect(editor.report.value.entries[0].manualGoodQuantity).toBe(8)
  })

  it('manual payload всегда содержит все шесть ключей', async () => {
    const editor = createEditor()
    await editor.load()
    editor.updateManual('task-1', 'manualGoodQuantity', 9)
    await editor.save()
    for (const field of manualFields) {
      expect(api.saveDraft.mock.calls[0][1][0]).toHaveProperty(field)
    }
  })

  it('сохраняет правки, сделанные во время in-flight load без preserveDirty', async () => {
    const editor = createEditor()
    await editor.load()
    const request = deferred()
    api.getByItem.mockReturnValueOnce(request.promise)

    const loading = editor.load()
    editor.updateManual('task-1', 'manualGoodQuantity', 12)
    request.resolve(response(report({ entries: [entry({ automaticOutputQuantity: 6 })] })))
    await loading

    expect(editor.report.value.entries[0].automaticOutputQuantity).toBe(6)
    expect(editor.report.value.entries[0].manualGoodQuantity).toBe(12)
    expect(editor.isDirty.value).toBe(true)
  })

  it('старый deferred GET не перезаписывает успешно Closed рапорт', async () => {
    const editor = createEditor()
    await editor.load()
    const staleGet = deferred()
    api.getByItem.mockReturnValueOnce(staleGet.promise)
    const loading = editor.load({ preserveDirty: true })
    api.close.mockResolvedValueOnce(response(report({ state: 'Closed' })))

    const closeResult = await editor.close({ confirmEarlyClose: true })
    staleGet.resolve(response(report({ state: 'Draft' })))
    await loading

    expect(closeResult).toEqual({ success: true })
    expect(editor.report.value.state).toBe('Closed')
    expect(editor.loading.value).toBe(false)
  })

  it('revert к A во время in-flight save A→B сохраняет новейший intent и не даёт close заморозить B', async () => {
    const editor = createEditor()
    await editor.load()
    editor.updateManual('task-1', 'manualGoodQuantity', 9)
    const put = deferred()
    api.saveDraft.mockReturnValueOnce(put.promise)

    const saving = editor.save()
    editor.updateManual('task-1', 'manualGoodQuantity', 8)
    put.resolve(response(report({ entries: [entry({ manualGoodQuantity: 9 })] })))
    api.getByItem.mockResolvedValueOnce(response(report({ entries: [entry({ manualGoodQuantity: 9 })] })))
    await saving

    expect(editor.report.value.entries[0].manualGoodQuantity).toBe(8)
    expect(editor.isDirty.value).toBe(true)

    api.saveDraft.mockImplementationOnce(async (_itemId, entries) => response(report({
      entries: [entry({ ...entries[0] })]
    })))
    api.getByItem.mockResolvedValueOnce(response(report({ entries: [entry({ manualGoodQuantity: 8 })] })))
    api.close.mockResolvedValueOnce(response(report({
      state: 'Closed', entries: [entry({ manualGoodQuantity: 8 })]
    })))
    const closeResult = await editor.close({ confirmEarlyClose: true })

    expect(closeResult).toEqual({ success: true })
    expect(api.saveDraft).toHaveBeenCalledTimes(2)
    expect(api.saveDraft.mock.calls[1][1][0].manualGoodQuantity).toBe(8)
    expect(editor.report.value.entries[0].manualGoodQuantity).toBe(8)
  })

  it('400 message=shiftClosed при cross-tab close перезагружает Closed и отзывает редактирование', async () => {
    const editor = createEditor()
    await editor.load()
    editor.updateManual('task-1', 'manualGoodQuantity', 11)
    api.saveDraft.mockRejectedValueOnce({ response: { status: 400, data: { message: 'shiftClosed' } } })
    api.getByItem.mockResolvedValueOnce(response(report({ state: 'Closed' })))

    const result = await editor.save()

    expect(result).toBe(false)
    expect(api.getByItem).toHaveBeenCalledTimes(2)
    expect(editor.report.value.state).toBe('Closed')
    expect(editor.isDirty.value).toBe(false)
  })

  it('при ошибке close показывает сообщение о закрытии, а не о сохранении', async () => {
    const editor = createEditor()
    await editor.load()
    api.close.mockRejectedValueOnce({ response: { status: 500 } })

    const result = await editor.close({ confirmEarlyClose: true })

    expect(result).toEqual({ success: false })
    expect(ElMessage.error).toHaveBeenCalledWith('Не удалось закрыть сменный рапорт')
  })
})
