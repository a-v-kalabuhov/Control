// @vitest-environment jsdom
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { flushPromises, mount } from '@vue/test-utils'
import ElementPlus from 'element-plus'
import dayjs from 'dayjs'

vi.mock('@/api/telemetryAdmin', () => ({
  telemetryAdminApi: {
    getStatistics: vi.fn(), previewCleanup: vi.fn(), cleanup: vi.fn()
  }
}))
vi.mock('element-plus', async (importOriginal) => ({
  ...(await importOriginal()),
  ElMessage: { success: vi.fn(), error: vi.fn(), warning: vi.fn() }
}))

import { ElMessage } from 'element-plus'
import { telemetryAdminApi } from '@/api/telemetryAdmin'
import TelemetryAdminView from '../TelemetryAdminView.vue'

const period = (startDate, endDate, count, isCurrent = false) => ({ startDate, endDate, count, isCurrent })
const sample = () => ({
  totalCount: 123, tableSizeBytes: 1536, indexesSizeBytes: 1024 ** 2,
  currentMonth: period('2026-11-01', '2026-11-30', 20, true),
  recentWeeks: [
    period('2026-11-02', '2026-11-08', 1), period('2026-11-09', '2026-11-15', 2),
    period('2026-11-16', '2026-11-22', 3), period('2026-11-23', '2026-11-29', 4, true)
  ],
  previousMonths: [
    period('2026-08-01', '2026-08-31', 5), period('2026-09-01', '2026-09-30', 6),
    period('2026-10-01', '2026-10-31', 7)
  ]
})

function deferred() {
  let resolve
  let reject
  const promise = new Promise((res, rej) => { resolve = res; reject = rej })
  return { promise, resolve, reject }
}

describe('TelemetryAdminView', () => {
  let wrapper

  beforeEach(() => {
    vi.clearAllMocks()
    telemetryAdminApi.getStatistics.mockResolvedValue({ data: sample() })
    telemetryAdminApi.previewCleanup.mockResolvedValue({
      data: { date: '2026-11-27', cutoffUtc: '2026-11-26T21:00:00Z', count: 12 }
    })
    telemetryAdminApi.cleanup.mockResolvedValue({
      data: { cutoffUtc: '2026-11-26T21:00:00Z', deletedCount: 12 }
    })
  })

  afterEach(() => {
    wrapper?.unmount()
    document.body.innerHTML = ''
  })

  async function mountView() {
    wrapper = mount(TelemetryAdminView, { global: { plugins: [ElementPlus] }, attachTo: document.body })
    await flushPromises()
    return wrapper
  }

  async function openPreview(count = 12) {
    telemetryAdminApi.previewCleanup.mockResolvedValue({ data: { count } })
    wrapper.vm.openCleanup()
    wrapper.vm.cleanupDate = '2026-11-27'
    await wrapper.vm.runPreview()
    await flushPromises()
  }

  it('автоматически загружает и показывает все группы показателей', async () => {
    await mountView()
    const zone = Intl.DateTimeFormat().resolvedOptions().timeZone

    expect(telemetryAdminApi.getStatistics).toHaveBeenCalledWith(zone)
    expect(wrapper.get('[data-test="total-count"]').text()).toBe('123')
    expect(wrapper.get('[data-test="table-size"]').text()).toBe('1,5 КБ')
    expect(wrapper.get('[data-test="indexes-size"]').text()).toBe('1 МБ')
    expect(wrapper.findAll('[data-test="current-month"]')).toHaveLength(1)
    expect(wrapper.findAll('[data-test="recent-week"]')).toHaveLength(4)
    expect(wrapper.findAll('[data-test="previous-month"]')).toHaveLength(3)
    expect(wrapper.text()).toContain('текущая')
    expect(wrapper.text()).toContain('текущий')
  })

  it('кнопка обновления повторно загружает, а ошибка сохраняет последние значения', async () => {
    await mountView()
    telemetryAdminApi.getStatistics.mockRejectedValueOnce({ response: { data: { message: 'Обновление недоступно' } } })

    await wrapper.vm.loadStatistics()

    expect(telemetryAdminApi.getStatistics).toHaveBeenCalledTimes(2)
    expect(wrapper.get('[data-test="total-count"]').text()).toBe('123')
    expect(wrapper.vm.statisticsError).toBe('Обновление недоступно')
    expect(ElMessage.error).toHaveBeenCalledWith('Обновление недоступно')
  })

  it('при ошибке начальной загрузки панель остаётся пустой', async () => {
    telemetryAdminApi.getStatistics.mockReset()
    telemetryAdminApi.getStatistics.mockRejectedValueOnce(new Error('network'))

    await mountView()

    expect(wrapper.vm.statistics).toBeNull()
    expect(wrapper.find('[data-test="statistics-panel"]').exists()).toBe(false)
    expect(wrapper.vm.statisticsError).toBe('Не удалось загрузить статистику телеметрии')
  })

  it('preview не вызывает удаление, показывает точный текст, отмена безопасна', async () => {
    await mountView()
    await openPreview(12)

    expect(telemetryAdminApi.previewCleanup).toHaveBeenCalledWith(
      '2026-11-27', Intl.DateTimeFormat().resolvedOptions().timeZone)
    expect(telemetryAdminApi.cleanup).not.toHaveBeenCalled()
    expect(wrapper.vm.confirmationVisible).toBe(true)
    expect(document.body.textContent).toContain('12 записей будет удалено. Подтвердите удаление')

    wrapper.vm.cancelConfirmation()
    expect(wrapper.vm.confirmationVisible).toBe(false)
    expect(telemetryAdminApi.cleanup).not.toHaveBeenCalled()
  })

  it('нулевой preview открывает 0, но не разрешает подтверждение', async () => {
    await mountView()
    await openPreview(0)

    expect(wrapper.vm.confirmationVisible).toBe(true)
    expect(wrapper.vm.canConfirmCleanup).toBe(false)
    await wrapper.vm.confirmCleanup()
    expect(telemetryAdminApi.cleanup).not.toHaveBeenCalled()
  })

  it('запрещает будущую дату до API', async () => {
    await mountView()
    wrapper.vm.openCleanup()
    wrapper.vm.cleanupDate = dayjs().add(1, 'day').format('YYYY-MM-DD')

    await wrapper.vm.runPreview()

    expect(telemetryAdminApi.previewCleanup).not.toHaveBeenCalled()
    expect(wrapper.vm.confirmationVisible).toBe(false)
    expect(ElMessage.warning).toHaveBeenCalledWith('Дата очистки не может быть в будущем')
  })

  it('ошибка preview не открывает диалог и сохраняет дату', async () => {
    await mountView()
    telemetryAdminApi.previewCleanup.mockRejectedValueOnce({ response: { data: { message: 'Счётчик недоступен' } } })
    wrapper.vm.openCleanup()
    wrapper.vm.cleanupDate = '2026-11-27'

    await wrapper.vm.runPreview()

    expect(wrapper.vm.confirmationVisible).toBe(false)
    expect(wrapper.vm.cleanupDialogVisible).toBe(true)
    expect(wrapper.vm.cleanupDate).toBe('2026-11-27')
    expect(ElMessage.error).toHaveBeenCalledWith('Счётчик недоступен')
  })

  it('блокирует управление во время DELETE и затем показывает фактический результат и refresh', async () => {
    await mountView()
    await openPreview(1)
    const request = deferred()
    telemetryAdminApi.cleanup.mockReturnValueOnce(request.promise)

    const pending = wrapper.vm.confirmCleanup()
    expect(wrapper.vm.deleting).toBe(true)
    expect(wrapper.vm.controlsLocked).toBe(true)

    request.resolve({ data: { deletedCount: 2 } })
    await pending
    await flushPromises()

    expect(ElMessage.success).toHaveBeenCalledWith('Удалено записей: 2')
    expect(wrapper.vm.deleting).toBe(false)
    expect(wrapper.vm.confirmationVisible).toBe(false)
    expect(wrapper.vm.cleanupDialogVisible).toBe(false)
    expect(telemetryAdminApi.getStatistics).toHaveBeenCalledTimes(2)
  })

  it('ошибка cleanup сохраняет форму и дату и разблокирует управление', async () => {
    await mountView()
    await openPreview()
    telemetryAdminApi.cleanup.mockRejectedValueOnce({ response: { status: 500, data: { message: 'Ошибка БД' } } })

    await wrapper.vm.confirmCleanup()

    expect(wrapper.vm.cleanupDialogVisible).toBe(true)
    expect(wrapper.vm.confirmationVisible).toBe(true)
    expect(wrapper.vm.cleanupDate).toBe('2026-11-27')
    expect(wrapper.vm.deleting).toBe(false)
    expect(ElMessage.error).toHaveBeenCalledWith('Ошибка БД')
    expect(telemetryAdminApi.getStatistics).toHaveBeenCalledTimes(1)
  })

  it('504 показывает отдельный текст таймаута', async () => {
    await mountView()
    await openPreview()
    telemetryAdminApi.cleanup.mockRejectedValueOnce({ response: { status: 504 } })

    await wrapper.vm.confirmCleanup()

    expect(ElMessage.error).toHaveBeenCalledWith('Очистка превысила допустимое время. Повторите операцию или обратитесь к администратору БД')
  })

  it('успех DELETE не заменяется ошибкой последующего refresh', async () => {
    await mountView()
    await openPreview(1)
    telemetryAdminApi.cleanup.mockResolvedValueOnce({ data: { deletedCount: 3 } })
    telemetryAdminApi.getStatistics.mockRejectedValueOnce({ response: { data: { message: 'Статистика не обновилась' } } })

    await wrapper.vm.confirmCleanup()

    expect(ElMessage.success).toHaveBeenCalledWith('Удалено записей: 3')
    expect(ElMessage.error).toHaveBeenCalledWith('Статистика не обновилась')
    expect(ElMessage.error).not.toHaveBeenCalledWith('Ошибка очистки телеметрии')
    expect(wrapper.vm.cleanupDialogVisible).toBe(false)
  })
})
