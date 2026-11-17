// @vitest-environment jsdom
import { describe, expect, it, vi } from 'vitest'
import { flushPromises, mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import ElementPlus from 'element-plus'
import LoginView from '../auth/LoginView.vue'
import { useAppInfoStore } from '@/stores/appInfo'

vi.mock('vue-router', async importOriginal => {
  const actual = await importOriginal()
  return { ...actual, useRouter: () => ({ push: vi.fn() }) }
})

describe('LoginView', () => {
  it('не показывает демонстрационные учётные записи даже в демо-режиме', async () => {
    setActivePinia(createPinia())
    const appInfo = useAppInfoStore()
    appInfo.demoMode = true
    appInfo.loaded = true

    const wrapper = mount(LoginView, { global: { plugins: [ElementPlus] } })
    await flushPromises()

    expect(wrapper.text()).toContain('Войти')
    expect(wrapper.text()).not.toContain('Демо-учётки')
    expect(wrapper.text()).not.toContain('Admin123!')
    expect(wrapper.text()).not.toContain('Manager123!')
    expect(wrapper.text()).not.toContain('Adjuster123!')
  })
})
