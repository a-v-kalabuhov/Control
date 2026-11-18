// @vitest-environment jsdom
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { flushPromises, mount } from '@vue/test-utils'
import ElementPlus, { ElMessage } from 'element-plus'
import ShiftClosurePdfActions from '../ShiftClosurePdfActions.vue'
import * as api from '@/api/shiftClosures'

vi.mock('@/api/shiftClosures', () => ({
  getBlankPdf: vi.fn(),
  getClosedPdf: vi.fn()
}))

function mountActions(state = 'Draft') {
  return mount(ShiftClosurePdfActions, {
    props: {
      itemId: 'item-1', closureId: state === 'Closed' ? 'closure-1' : null,
      state, shiftDate: '2026-11-18'
    },
    global: { plugins: [ElementPlus] }
  })
}

describe('ShiftClosurePdfActions', () => {
  let click

  beforeEach(() => {
    vi.clearAllMocks()
    Object.defineProperty(URL, 'createObjectURL', { configurable: true, value: vi.fn(() => 'blob:closure') })
    Object.defineProperty(URL, 'revokeObjectURL', { configurable: true, value: vi.fn() })
    click = vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(() => {})
    vi.spyOn(ElMessage, 'error').mockImplementation(() => {})
    api.getBlankPdf.mockResolvedValue({ data: new Blob(['blank'], { type: 'application/pdf' }) })
    api.getClosedPdf.mockResolvedValue({ data: new Blob(['closed'], { type: 'application/pdf' }) })
  })

  afterEach(() => {
    vi.restoreAllMocks()
    delete URL.createObjectURL
    delete URL.revokeObjectURL
    document.body.innerHTML = ''
  })

  it('до закрытия предлагает только бланк и скачивает blob как .pdf с освобождением URL', async () => {
    const wrapper = mountActions('Draft')
    expect(wrapper.find('[data-test="print-closed"]').exists()).toBe(false)

    await wrapper.get('[data-test="print-blank"]').trigger('click')
    await flushPromises()

    expect(api.getBlankPdf).toHaveBeenCalledWith('item-1')
    expect(api.getClosedPdf).not.toHaveBeenCalled()
    expect(URL.createObjectURL).toHaveBeenCalledWith(expect.any(Blob))
    expect(click).toHaveBeenCalledTimes(1)
    expect(click.mock.instances[0].download).toMatch(/^shift-closure-blank-2026-11-18.*\.pdf$/)
    expect(URL.revokeObjectURL).toHaveBeenCalledWith('blob:closure')
  })

  it('после закрытия предлагает только закрытый PDF', async () => {
    const wrapper = mountActions('Closed')
    expect(wrapper.find('[data-test="print-blank"]').exists()).toBe(false)

    await wrapper.get('[data-test="print-closed"]').trigger('click')
    await flushPromises()

    expect(api.getClosedPdf).toHaveBeenCalledWith('closure-1')
    expect(api.getBlankPdf).not.toHaveBeenCalled()
    expect(click.mock.instances[0].download).toMatch(/^shift-closure-2026-11-18.*\.pdf$/)
    expect(URL.revokeObjectURL).toHaveBeenCalledWith('blob:closure')
  })

  it('при ошибке API показывает notification и не создаёт пустой файл', async () => {
    api.getBlankPdf.mockRejectedValueOnce({ response: { status: 403, data: { message: 'Недоступно' } } })
    const wrapper = mountActions('Draft')

    await wrapper.get('[data-test="print-blank"]').trigger('click')
    await flushPromises()

    expect(ElMessage.error).toHaveBeenCalled()
    expect(URL.createObjectURL).not.toHaveBeenCalled()
    expect(click).not.toHaveBeenCalled()
    expect(URL.revokeObjectURL).not.toHaveBeenCalled()
  })
})
