// @vitest-environment jsdom
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { flushPromises, mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import ElementPlus from 'element-plus'
import PersonnelDictionary from '../PersonnelDictionary.vue'
import { personnelApi } from '@/api/personnel'

vi.mock('@/api/personnel', () => ({
  personnelApi: {
    getList: vi.fn().mockResolvedValue({ data: [] }),
    create: vi.fn().mockResolvedValue({ data: {} }),
    update: vi.fn().mockResolvedValue({ data: {} })
  }
}))

function mountDictionary(people = []) {
  setActivePinia(createPinia())
  personnelApi.getList.mockResolvedValue({ data: people })
  return mount(PersonnelDictionary, {
    global: { plugins: [ElementPlus], stubs: { Plus: true } }
  })
}

function fillRequiredIdentity(wrapper) {
  Object.assign(wrapper.vm.form, {
    employeeId: 'EMP-30',
    fullName: 'Литейщик Тестовый',
    role: 'Operator'
  })
}

describe('PersonnelDictionary (PZP-30)', () => {
  beforeEach(() => vi.clearAllMocks())

  it('при создании требует явного выбора роли и предлагает роль литейщика', async () => {
    const wrapper = mountDictionary()
    await flushPromises()

    wrapper.vm.showCreateModal()
    await flushPromises()

    expect(wrapper.vm.form.role).toBe('')
    expect(wrapper.findAllComponents({ name: 'ElOption' })
      .some(option => option.props('label') === 'Литейщик')).toBe(true)
  })

  it('создаёт литейщика без логина и пароля', async () => {
    const wrapper = mountDictionary()
    await flushPromises()
    wrapper.vm.showCreateModal()
    await flushPromises()
    fillRequiredIdentity(wrapper)
    Object.assign(wrapper.vm.form, { login: '', password: '' })

    await wrapper.vm.savePersonnel()
    await flushPromises()

    expect(personnelApi.create).toHaveBeenCalledWith(expect.objectContaining({
      role: 'Operator',
      login: '',
      password: ''
    }))
  })

  it('предлагает роль старшего смены при создании сотрудника', async () => {
    const wrapper = mountDictionary()
    await flushPromises()
    wrapper.vm.showCreateModal()
    await flushPromises()

    expect(wrapper.findAllComponents({ name: 'ElOption' })
      .some(option => option.props('value') === 'ShiftLead' && option.props('label') === 'Старший смены')).toBe(true)
  })

  it('создаёт старшего смены без логина и пароля', async () => {
    const wrapper = mountDictionary()
    await flushPromises()
    wrapper.vm.showCreateModal()
    await flushPromises()
    fillRequiredIdentity(wrapper)
    Object.assign(wrapper.vm.form, { role: 'ShiftLead', login: '', password: '' })

    await wrapper.vm.savePersonnel()
    await flushPromises()

    expect(personnelApi.create).toHaveBeenCalledWith(expect.objectContaining({
      role: 'ShiftLead',
      login: '',
      password: ''
    }))
  })

  it('не создаёт учётную запись с логином, но без пароля', async () => {
    const wrapper = mountDictionary()
    await flushPromises()
    wrapper.vm.showCreateModal()
    await flushPromises()
    fillRequiredIdentity(wrapper)
    Object.assign(wrapper.vm.form, { login: 'operator1', password: '' })

    await wrapper.vm.savePersonnel()
    await flushPromises()

    expect(personnelApi.create).not.toHaveBeenCalled()
  })

  it('при редактировании позволяет очистить логин и отправляет это в API', async () => {
    const person = {
      id: 'user-1', employeeId: 'EMP-1', fullName: 'Сотрудник', role: 'Operator',
      login: 'operator1', isActive: true, qualification: ''
    }
    const wrapper = mountDictionary([person])
    await flushPromises()
    wrapper.vm.editPersonnel(person)
    await flushPromises()
    Object.assign(wrapper.vm.form, { login: '', password: '' })

    await wrapper.vm.savePersonnel()
    await flushPromises()

    expect(personnelApi.update).toHaveBeenCalledWith('user-1', expect.objectContaining({ login: '' }))
  })

  it('показывает в списке наличие входа в систему', async () => {
    const wrapper = mountDictionary([
      { id: '1', fullName: 'С логином', login: 'user1', role: 'Manager', isActive: true },
      { id: '2', fullName: 'Без логина', login: null, role: 'Operator', isActive: true }
    ])
    await flushPromises()

    expect(wrapper.text()).toContain('Вход в систему')
    expect(wrapper.text()).toContain('Есть')
    expect(wrapper.text()).toContain('Нет')
  })
})
