// @vitest-environment jsdom
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import ShiftLeadLayout from '../ShiftLeadLayout.vue'
import { useAuthStore } from '@/stores/auth'

const stubs = {
  'router-view': { template: '<div data-test="content"><slot /></div>' },
  'router-link': {
    props: ['to'],
    template: '<a :href="to"><slot /></a>'
  },
  'el-button': {
    emits: ['click'],
    template: '<button @click="$emit(\'click\')"><slot /></button>'
  }
}

describe('ShiftLeadLayout', () => {
  let wrapper

  beforeEach(() => {
    setActivePinia(createPinia())
  })

  afterEach(() => {
    wrapper?.unmount()
    wrapper = null
    vi.restoreAllMocks()
  })

  function mountLayout(role) {
    const auth = useAuthStore()
    auth.isAuthenticated = true
    auth.user = { role, fullName: 'Тестовый Пользователь' }
    wrapper = mount(ShiftLeadLayout, { global: { stubs } })
    return auth
  }

  it('показывает старшему только Моя смена и История', () => {
    mountLayout('ShiftLead')

    expect(wrapper.text()).toContain('Старший смены')
    const links = wrapper.findAll('a')
    expect(links.map(link => link.attributes('href'))).toEqual([
      '/shift-lead',
      '/shift-lead/history'
    ])
    expect(wrapper.text()).toContain('Моя смена')
    expect(wrapper.text()).toContain('История')
    expect(wrapper.text()).not.toContain('Назад к плану смен')
    expect(wrapper.find('[data-test="content"]').exists()).toBe(true)
  })

  it.each(['Manager', 'Admin'])('показывает роли %s только возврат к плану смен', role => {
    mountLayout(role)

    const links = wrapper.findAll('a')
    expect(links).toHaveLength(1)
    expect(links[0].attributes('href')).toBe('/planning/shift-plan')
    expect(wrapper.text()).toContain('Назад к плану смен')
    expect(wrapper.text()).not.toContain('Моя смена')
    expect(wrapper.text()).not.toContain('История')
  })

  it('выходит через auth store', async () => {
    const auth = mountLayout('ShiftLead')
    const logout = vi.spyOn(auth, 'logout').mockResolvedValue(undefined)

    await wrapper.find('[data-test="logout"]').trigger('click')

    expect(logout).toHaveBeenCalledTimes(1)
  })
})
