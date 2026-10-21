// @vitest-environment jsdom
import { describe, it, expect, beforeEach } from 'vitest'
import { mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import ElementPlus from 'element-plus'
import PlanConflictsBadge from '../PlanConflictsBadge.vue'
import { usePlanConflictsStore } from '@/stores/planConflicts'

const mountBadge = () => mount(PlanConflictsBadge, { global: { plugins: [ElementPlus] } })

describe('PlanConflictsBadge', () => {
  beforeEach(() => setActivePinia(createPinia()))

  it('скрыт, когда проблемных заданий нет', () => {
    expect(mountBadge().find('[data-test="plan-conflicts-badge"]').exists()).toBe(false)
  })

  it('красный при конфликте плана', async () => {
    usePlanConflictsStore().data = { taskCount: 3, planCount: 1, deviationCount: 2, items: [] }
    const w = mountBadge()
    const badge = w.find('[data-test="plan-conflicts-badge"]')
    expect(badge.text()).toContain('3')
    expect(badge.find('.el-badge__content--danger').exists()).toBe(true)
  })

  it('оранжевый, если только отклонения', () => {
    usePlanConflictsStore().data = { taskCount: 2, planCount: 0, deviationCount: 1, items: [] }
    expect(mountBadge().find('.el-badge__content--warning').exists()).toBe(true)
  })
})
