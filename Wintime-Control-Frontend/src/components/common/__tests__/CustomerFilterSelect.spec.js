// @vitest-environment jsdom
import { describe, it, expect, vi, afterEach } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import ElementPlus, { ElMessage } from 'element-plus'
import CustomerFilterSelect from '../CustomerFilterSelect.vue'

const mounted = []
function mountFilter(props = {}) {
  const wrapper = mount(CustomerFilterSelect, {
    props: { customer: null, customerEmpty: false, fetch: vi.fn().mockResolvedValue([]), ...props },
    global: { plugins: [ElementPlus] },
    attachTo: document.body
  })
  mounted.push(wrapper)
  return wrapper
}

afterEach(() => {
  vi.useRealTimers()
  vi.restoreAllMocks()
  mounted.splice(0).forEach(wrapper => wrapper.unmount())
  document.body.innerHTML = ''
})

describe('CustomerFilterSelect', () => {
  it('загружает подсказки и всегда добавляет пункт «(не указано)»', async () => {
    const fetch = vi.fn().mockResolvedValue(['ООО Ромашка'])
    const wrapper = mountFilter({ fetch })

    await wrapper.vm.loadSuggestions('рома')

    expect(fetch).toHaveBeenCalledWith('рома')
    expect(wrapper.vm.options.map(option => option.label)).toEqual([
      '(не указано)', 'ООО Ромашка'
    ])
  })

  it('при открытии загружает исходный список', async () => {
    const fetch = vi.fn().mockResolvedValue([])
    const wrapper = mountFilter({ fetch })

    await wrapper.vm.onVisibleChange(true)

    expect(fetch).toHaveBeenCalledWith('')
  })

  it('выбор названия эмитит customer и сбрасывает customerEmpty', () => {
    const wrapper = mountFilter()

    wrapper.vm.selectKey('customer:ООО Ромашка')

    expect(wrapper.emitted('update:customer').at(-1)).toEqual(['ООО Ромашка'])
    expect(wrapper.emitted('update:customerEmpty').at(-1)).toEqual([false])
  })

  it('отличает специальный пункт от заказчика с таким же названием', () => {
    const wrapper = mountFilter()

    wrapper.vm.selectKey('empty')
    expect(wrapper.emitted('update:customer').at(-1)).toEqual([null])
    expect(wrapper.emitted('update:customerEmpty').at(-1)).toEqual([true])

    wrapper.vm.selectKey('customer:(не указано)')
    expect(wrapper.emitted('update:customer').at(-1)).toEqual(['(не указано)'])
    expect(wrapper.emitted('update:customerEmpty').at(-1)).toEqual([false])
  })

  it('очистка возвращает состояние «все»', () => {
    const wrapper = mountFilter({ customer: 'ООО Ромашка' })

    wrapper.vm.selectKey('')

    expect(wrapper.emitted('update:customer').at(-1)).toEqual([null])
    expect(wrapper.emitted('update:customerEmpty').at(-1)).toEqual([false])
  })

  it('сохраняет выбранное название при смене набора подсказок', async () => {
    const fetch = vi.fn().mockResolvedValue(['ООО Другой'])
    const wrapper = mountFilter({ customer: 'ООО Ромашка', fetch })

    await wrapper.vm.loadSuggestions('друг')

    expect(wrapper.vm.options.map(option => option.label)).toContain('ООО Ромашка')
    expect(wrapper.vm.options.map(option => option.label)).toContain('ООО Другой')
  })

  it('дебаунсит поиск и не эмитит свободный текст как заказчика', async () => {
    vi.useFakeTimers()
    const fetch = vi.fn().mockResolvedValue([])
    const wrapper = mountFilter({ fetch })

    wrapper.vm.onRemoteMethod('первый')
    wrapper.vm.onRemoteMethod('последний')
    await vi.advanceTimersByTimeAsync(299)
    expect(fetch).not.toHaveBeenCalled()
    await vi.advanceTimersByTimeAsync(1)
    await flushPromises()

    expect(fetch).toHaveBeenCalledTimes(1)
    expect(fetch).toHaveBeenCalledWith('последний')
    expect(wrapper.emitted('update:customer')).toBeUndefined()
  })

  it('ошибка подсказок не сбрасывает выбранное значение', async () => {
    const error = vi.spyOn(ElMessage, 'error').mockImplementation(() => {})
    const wrapper = mountFilter({
      customer: 'ООО Ромашка', fetch: vi.fn().mockRejectedValue(new Error('сеть'))
    })

    await wrapper.vm.loadSuggestions('x')

    expect(wrapper.vm.options.map(option => option.label)).toContain('ООО Ромашка')
    expect(wrapper.emitted('update:customer')).toBeUndefined()
    expect(error).toHaveBeenCalledWith('Ошибка загрузки заказчиков')
  })
})
