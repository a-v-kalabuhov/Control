// @vitest-environment jsdom
import { describe, it, expect, vi, afterEach } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import ElementPlus from 'element-plus'
import SuggestInput from '../SuggestInput.vue'

const mounted = []
function mountInput(props) {
  const w = mount(SuggestInput, {
    props: { modelValue: '', ...props },
    global: { plugins: [ElementPlus] },
    attachTo: document.body
  })
  mounted.push(w)
  return w
}

afterEach(() => {
  mounted.splice(0).forEach(w => w.unmount())
  document.body.innerHTML = ''
})

describe('SuggestInput', () => {
  it('запрашивает подсказки через fetch и показывает подписи', async () => {
    const fetch = vi.fn().mockResolvedValue([{ name: 'Чёрный', code: 'RAL 9005' }])
    const w = mountInput({ fetch, itemLabel: c => `${c.name} · ${c.code}`, itemValue: c => c.name })

    const cb = vi.fn()
    await w.vm.querySearch('чёр', cb)
    await flushPromises()

    expect(fetch).toHaveBeenCalledWith('чёр')
    expect(cb).toHaveBeenCalledWith([{ value: 'Чёрный', label: 'Чёрный · RAL 9005', item: { name: 'Чёрный', code: 'RAL 9005' } }])
  })

  it('строки работают без itemLabel/itemValue', async () => {
    const w = mountInput({ fetch: vi.fn().mockResolvedValue(['Сибур']) })
    const cb = vi.fn()
    await w.vm.querySearch('', cb)
    expect(cb).toHaveBeenCalledWith([{ value: 'Сибур', label: 'Сибур', item: 'Сибур' }])
  })

  it('ошибка fetch даёт пустой список', async () => {
    const w = mountInput({ fetch: vi.fn().mockRejectedValue(new Error('сеть')) })
    const cb = vi.fn()
    await w.vm.querySearch('x', cb)
    expect(cb).toHaveBeenCalledWith([])
  })

  it('свободный ввод обновляет v-model', async () => {
    const w = mountInput({ fetch: vi.fn().mockResolvedValue([]) })
    await w.find('input').setValue('Новый завод')
    expect(w.emitted('update:modelValue').at(-1)).toEqual(['Новый завод'])
  })

  it('выбор пункта пишет value в v-model и отдаёт исходный элемент в select', async () => {
    const item = { name: 'Белый', code: 'RAL 9010' }
    const w = mountInput({ fetch: vi.fn(), itemValue: c => c.name })
    w.vm.onSelect({ value: 'Белый', label: 'Белый · RAL 9010', item })
    expect(w.emitted('update:modelValue').at(-1)).toEqual(['Белый'])
    expect(w.emitted('select').at(-1)).toEqual([item])
  })
})
