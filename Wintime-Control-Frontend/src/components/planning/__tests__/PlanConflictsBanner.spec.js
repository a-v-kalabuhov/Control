// @vitest-environment jsdom
import { describe, it, expect, beforeEach } from 'vitest'
import { mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import ElementPlus from 'element-plus'
import PlanConflictsBanner from '../PlanConflictsBanner.vue'
import { usePlanConflictsStore } from '@/stores/planConflicts'

const ref = (id) => ({ id, immName: `ТПА-${id}`, moldName: `ПФ-${id}`, productArticle: `A${id}.0`, plannedStartAt: '2026-10-21T07:00:00Z' })
const mountBanner = () => mount(PlanConflictsBanner, { global: { plugins: [ElementPlus] } })

describe('PlanConflictsBanner', () => {
  beforeEach(() => setActivePinia(createPinia()))

  it('нет проблем — баннера нет', () => {
    expect(mountBanner().find('[data-test="plan-conflicts-banner"]').exists()).toBe(false)
  })

  it('счётчики, раскрытие списка, клик по строке — select с заданием', async () => {
    usePlanConflictsStore().data = {
      taskCount: 3, planCount: 1, deviationCount: 1,
      items: [
        { kind: 'Plan', resource: 'Imm', task: ref('1'), other: ref('2'), overlapSeconds: 3000 },
        { kind: 'Deviation', resource: 'Mold', task: ref('3'), other: ref('1'), overlapSeconds: 1500 }
      ]
    }
    const w = mountBanner()
    expect(w.text()).toContain('3 задания требуют корректировки: 1 конфликт плана, 1 конфликт отклонения')

    await w.find('[data-test="plan-conflicts-toggle"]').trigger('click')
    const rows = w.findAll('[data-test="plan-conflict-row"]')
    expect(rows).toHaveLength(2)
    expect(rows[0].text()).toContain('A1.0 · ПФ-1 · ТПА-1')

    await rows[1].trigger('click')
    expect(w.emitted('select')[0][0].id).toBe('3')
  })
})
