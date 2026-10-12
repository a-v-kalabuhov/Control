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
    const wrapper = mountModal({ id: 't1', status: 'Draft' })
    await flushPromises()
    const cells = wrapper.findAll('.el-descriptions__cell')
    const idx = cells.findIndex(c => c.text() === 'Рабочий режим')
    expect(cells[idx + 1].text()).toBe('—')
  })

  it('workMode: Auto → «Автомат»', async () => {
    const wrapper = mountModal({ id: 't1', status: 'Draft', workMode: 'Auto' })
    await flushPromises()
    const cells = wrapper.findAll('.el-descriptions__cell')
    const idx = cells.findIndex(c => c.text() === 'Рабочий режим')
    expect(cells[idx + 1].text()).toBe('Автомат')
  })

  it('workMode: SemiAuto → «Полуавтомат»', async () => {
    const wrapper = mountModal({ id: 't1', status: 'Draft', workMode: 'SemiAuto' })
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
      id: 't1', status: 'Draft',
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
