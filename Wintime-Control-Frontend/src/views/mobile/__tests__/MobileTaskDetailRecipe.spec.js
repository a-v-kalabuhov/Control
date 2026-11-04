// @vitest-environment jsdom
import { describe, it, expect, vi } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import ElementPlus from 'element-plus'
import { createPinia, setActivePinia } from 'pinia'
import MobileTaskDetailView from '../MobileTaskDetailView.vue'

// Вью берёт useMobileStore/useAuthStore на setup; сетевые вызовы — только по кнопкам.
vi.mock('@/api/mobile', () => ({ mobileApi: {} }))

function mountView(task) {
  setActivePinia(createPinia())
  return mount(MobileTaskDetailView, {
    props: { modelValue: true, task },
    global: { plugins: [ElementPlus] },
    attachTo: document.body
  })
}

const recipe = [
  { id: 'ri-1', materialId: 'pp-1', materialType: 'Plastic', materialArticle: 'PP-1', materialName: 'Полипропилен',
    originalMaterialId: 'pp-1', originalMaterialArticle: 'PP-1', originalMaterialName: 'Полипропилен', percent: 100 }
]

describe('MobileTaskDetailView — рецепт (BL-36)', () => {
  it('показывает строку на ингредиент', async () => {
    const wrapper = mountView({ id: 't1', status: 'Planned', recipeIngredients: recipe })
    await flushPromises()

    expect(wrapper.text()).toContain('Рецепт')
    expect(wrapper.text()).toContain('PP-1 — 100 %')
  })

  it('пустой рецепт — прочерк', async () => {
    const wrapper = mountView({ id: 't1', status: 'Planned', recipeIngredients: [] })
    await flushPromises()

    expect(wrapper.text()).toContain('Рецепт')
  })

  it('сумма пластиков ≠ 100 % — красный текст', async () => {
    const wrapper = mountView({ id: 't1', status: 'Planned', recipeIngredients: [{ ...recipe[0], percent: 98 }] })
    await flushPromises()

    expect(wrapper.vm.recipeSumOk).toBe(false)
  })
})
