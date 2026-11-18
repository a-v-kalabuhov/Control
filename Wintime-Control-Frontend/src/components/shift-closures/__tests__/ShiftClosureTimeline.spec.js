// @vitest-environment jsdom
import { afterEach, describe, expect, it } from 'vitest'
import { mount } from '@vue/test-utils'
import ElementPlus from 'element-plus'
import dayjs from 'dayjs'
import ShiftClosureTimeline from '../ShiftClosureTimeline.vue'

function interval(overrides = {}) {
  return {
    startAtUtc: '2026-11-18T05:00:00Z',
    endAtUtc: '2026-11-18T07:00:00Z',
    operators: [],
    automaticOutputQuantity: 0,
    hasOrphanOutput: false,
    taskOutputs: [],
    ...overrides
  }
}

function timeline(overrides = {}) {
  return {
    immId: 'imm-1',
    immName: 'ТПА-1',
    intervals: [interval()],
    ...overrides
  }
}

function mountTimeline(value = timeline(), props = {}) {
  return mount(ShiftClosureTimeline, {
    props: { timeline: value, canEdit: true, ...props },
    global: { plugins: [ElementPlus] }
  })
}

describe('ShiftClosureTimeline', () => {
  let wrapper

  afterEach(() => {
    wrapper?.unmount()
    wrapper = null
    document.body.innerHTML = ''
  })

  it('выводит каждый server merged interval с границами, Operators и automatic quantity без Slot', () => {
    const first = interval({
      operators: [
        { operatorId: 'op-1', fullName: 'Иванов', employeeId: '001', slot: 2 },
        { operatorId: 'op-2', fullName: 'Петров', employeeId: null, slot: 1 }
      ],
      automaticOutputQuantity: 12
    })
    const second = interval({
      startAtUtc: '2026-11-18T07:00:00Z',
      endAtUtc: '2026-11-18T09:30:00Z',
      operators: [{ operatorId: 'op-3', fullName: 'Сидоров', employeeId: '003', slot: 1 }],
      automaticOutputQuantity: 7
    })
    wrapper = mountTimeline(timeline({ intervals: [first, second] }))

    const rows = wrapper.findAll('[data-test^="timeline-interval-"]')
    expect(rows).toHaveLength(2)
    expect(rows[0].text()).toContain(dayjs(first.startAtUtc).format('DD.MM.YYYY HH:mm'))
    expect(rows[0].text()).toContain(dayjs(first.endAtUtc).format('DD.MM.YYYY HH:mm'))
    expect(rows[0].text()).toContain('Иванов')
    expect(rows[0].text()).toContain('001')
    expect(rows[0].text()).toContain('Петров')
    expect(rows[0].text()).toContain('12')
    expect(rows[1].text()).toContain('Сидоров')
    expect(rows[1].text()).toContain('7')
    expect(wrapper.text()).not.toContain('место')
    expect(wrapper.text()).not.toContain('Slot')
  })

  it('нулевой выпуск без литейщиков не является ошибкой и не предлагает назначение', () => {
    wrapper = mountTimeline()
    const row = wrapper.get('[data-test="timeline-interval-0"]')

    expect(row.attributes('data-orphan')).toBe('false')
    expect(row.classes()).not.toContain('border-red-300')
    expect(wrapper.text()).toContain('Литейщики не назначены')
    expect(wrapper.find('[data-test="assign-orphan-0"]').exists()).toBe(false)
  })

  it('positive orphan подсвечен и Назначить передаёт immId и точные границы интервала', async () => {
    const orphan = interval({ automaticOutputQuantity: 5, hasOrphanOutput: true })
    wrapper = mountTimeline(timeline({ intervals: [orphan] }))
    const row = wrapper.get('[data-test="timeline-interval-0"]')

    expect(row.attributes('data-orphan')).toBe('true')
    expect(row.classes()).toContain('border-red-300')
    await wrapper.get('[data-test="assign-orphan-0"]').trigger('click')

    expect(wrapper.emitted('assign')).toEqual([[{
      immId: 'imm-1',
      assignedAt: orphan.startAtUtc,
      unassignedAt: orphan.endAtUtc
    }]])
  })

  it('Closed скрывает mutation, но оставляет саму шкалу', () => {
    wrapper = mountTimeline(timeline({
      intervals: [interval({ automaticOutputQuantity: 5, hasOrphanOutput: true })]
    }), { canEdit: false })

    expect(wrapper.find('[data-test="timeline-interval-0"]').exists()).toBe(true)
    expect(wrapper.find('[data-test="assign-orphan-0"]').exists()).toBe(false)
  })
})
