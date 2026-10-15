// @vitest-environment jsdom
import { describe, it, expect } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import ElementPlus from 'element-plus'
import dayjs from 'dayjs'
import TaskDetailModal from '../TaskDetailModal.vue'

function mountModal(task) {
  setActivePinia(createPinia())
  return mount(TaskDetailModal, {
    props: { modelValue: true, task },
    global: { plugins: [ElementPlus] },
    attachTo: document.body
  })
}

describe('TaskDetailModal — рабочий режим', () => {
  // Находка 4: до загрузки задания (task === null) строка «Рабочий режим»
  // должна показывать прочерк, как соседние строки, а не «Автомат» по умолчанию.
  it('без задания показывает прочерк, а не «Автомат»', async () => {
    const wrapper = mountModal(null)
    await flushPromises()
    expect(wrapper.text()).toContain('—')
    expect(wrapper.text()).not.toContain('Автомат')
  })

  it('workMode отсутствует в загруженном задании → прочерк', async () => {
    const wrapper = mountModal({ id: 't1', status: 'Planned' })
    await flushPromises()
    const cells = wrapper.findAll('.el-descriptions__cell')
    const idx = cells.findIndex(c => c.text() === 'Рабочий режим')
    expect(cells[idx + 1].text()).toBe('—')
  })

  it('workMode: Auto → «Автомат»', async () => {
    const wrapper = mountModal({ id: 't1', status: 'Planned', workMode: 'Auto' })
    await flushPromises()
    const cells = wrapper.findAll('.el-descriptions__cell')
    const idx = cells.findIndex(c => c.text() === 'Рабочий режим')
    expect(cells[idx + 1].text()).toBe('Автомат')
  })

  it('workMode: SemiAuto → «Полуавтомат»', async () => {
    const wrapper = mountModal({ id: 't1', status: 'Planned', workMode: 'SemiAuto' })
    await flushPromises()
    const cells = wrapper.findAll('.el-descriptions__cell')
    const idx = cells.findIndex(c => c.text() === 'Рабочий режим')
    expect(cells[idx + 1].text()).toBe('Полуавтомат')
  })
})

describe('TaskDetailModal — план задания (PZP-14)', () => {
  const cellText = (wrapper, label) => {
    const cells = wrapper.findAll('.el-descriptions__cell')
    const idx = cells.findIndex(c => c.text() === label)
    return cells[idx + 1].text()
  }

  it('показывает норматив, выпуск, буфер с расшифровкой и плановое окончание', async () => {
    const wrapper = mountModal({
      id: 't1', status: 'Issued',
      plannedSetupMinutes: 30, releaseBufferFixedMinutes: 10, releaseBufferPercent: 10,
      plannedReleaseSeconds: 1500, plannedReleaseBufferSeconds: 750,
      plannedEndAt: '2026-10-15T06:07:30Z'
    })
    await flushPromises()

    expect(cellText(wrapper, 'Норматив наладки')).toBe('30 мин')
    expect(cellText(wrapper, 'Выпуск (расчёт)')).toBe('25 мин')
    expect(cellText(wrapper, 'Буфер выпуска')).toBe('13 мин (10 мин + 10 %)')
    expect(cellText(wrapper, 'Плановое окончание'))
      .toBe(dayjs('2026-10-15T06:07:30Z').format('DD.MM.YYYY HH:mm'))
  })

  it('без расчёта выпуска показывает прочерки, норматив — всегда', async () => {
    const wrapper = mountModal({
      id: 't1', status: 'Planned',
      plannedSetupMinutes: 45, releaseBufferFixedMinutes: 10, releaseBufferPercent: 10,
      plannedReleaseSeconds: null, plannedReleaseBufferSeconds: null, plannedEndAt: null
    })
    await flushPromises()

    expect(cellText(wrapper, 'Норматив наладки')).toBe('45 мин')
    expect(cellText(wrapper, 'Выпуск (расчёт)')).toBe('—')
    expect(cellText(wrapper, 'Буфер выпуска')).toBe('—')
    expect(cellText(wrapper, 'Плановое окончание')).toBe('—')
  })
})

describe('TaskDetailModal — вкладки', () => {
  it('по умолчанию «Данные задания»; «Результаты выполнения» монтируют вкладку итогов с id задания', async () => {
    const wrapper = mount(TaskDetailModal, {
      props: { modelValue: true, task: { id: 't1', status: 'InProgress' } },
      global: {
        plugins: [ElementPlus, createPinia()],
        stubs: { TaskResultsTab: { name: 'TaskResultsTab', props: ['taskId'], template: '<div class="results-stub" />' } }
      },
      attachTo: document.body
    })
    await flushPromises()

    const tabs = wrapper.findAll('.el-tabs__item').map(t => t.text())
    expect(tabs).toEqual(['Данные задания', 'Результаты выполнения'])
    expect(wrapper.findComponent({ name: 'TaskResultsTab' }).exists()).toBe(false)

    await wrapper.findAll('.el-tabs__item')[1].trigger('click')
    await flushPromises()

    const tab = wrapper.findComponent({ name: 'TaskResultsTab' })
    expect(tab.exists()).toBe(true)
    expect(tab.props('taskId')).toBe('t1')

    await wrapper.setProps({ task: { id: 't2', status: 'Completed' } })
    await flushPromises()
    expect(wrapper.findComponent({ name: 'TaskResultsTab' }).exists()).toBe(false)   // сброс на «Данные задания»
  })

  it('закрытие снаружи (modelValue=false) сбрасывает вкладку на «Данные задания»', async () => {
    const task = { id: 't1', status: 'InProgress' }
    const wrapper = mount(TaskDetailModal, {
      props: { modelValue: true, task },
      global: {
        plugins: [ElementPlus, createPinia()],
        stubs: { TaskResultsTab: { name: 'TaskResultsTab', props: ['taskId'], template: '<div class="results-stub" />' } }
      },
      attachTo: document.body
    })
    await flushPromises()
    await wrapper.findAll('.el-tabs__item')[1].trigger('click')
    await flushPromises()
    expect(wrapper.findComponent({ name: 'TaskResultsTab' }).exists()).toBe(true)

    await wrapper.setProps({ modelValue: false })
    await flushPromises()
    await wrapper.setProps({ modelValue: true })
    await flushPromises()

    expect(wrapper.findComponent({ name: 'TaskResultsTab' }).exists()).toBe(false)
  })
})

describe('TaskDetailModal — только чтение', () => {
  it('readonly: нет кнопок действий и поля причины закрытия, вкладки на месте', async () => {
    setActivePinia(createPinia())
    const { useAuthStore } = await import('@/stores/auth')
    useAuthStore().userRole = 'Manager'   // у менеджера без readonly были бы «Редактировать», «Закрыть задание» и поле причины
    const wrapper = mount(TaskDetailModal, {
      props: { modelValue: true, task: { id: 't1', status: 'InProgress', planQuantity: 10, actualQuantity: 1 }, readonly: true },
      global: { plugins: [ElementPlus], stubs: { TaskResultsTab: true } },
      attachTo: document.body
    })
    await flushPromises()

    const buttons = wrapper.findAll('button').map(b => b.text())
    for (const label of ['Редактировать', 'Выдать', 'Завершить', 'Закрыть задание']) {
      expect(buttons).not.toContain(label)
    }
    expect(buttons).toContain('Назад')
    expect(wrapper.find('textarea').exists()).toBe(false)
    expect(wrapper.findAll('.el-tabs__item').map(t => t.text())).toEqual(['Данные задания', 'Результаты выполнения'])
    wrapper.unmount()
  })
})
