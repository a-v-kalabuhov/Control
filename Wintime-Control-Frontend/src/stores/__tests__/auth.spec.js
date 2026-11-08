// @vitest-environment jsdom
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { createPinia, setActivePinia } from 'pinia'
import { useAuthStore } from '../auth'
import { useShiftStaffingStore } from '../shiftStaffing'
import { authApi } from '@/api/auth'

vi.mock('@/api/auth', () => ({
  authApi: {
    login: vi.fn(),
    logout: vi.fn(),
    getCurrentUser: vi.fn(),
    refreshToken: vi.fn()
  }
}))

vi.mock('@/api/shiftStaffing', () => ({
  shiftStaffingApi: {
    nearest: vi.fn(),
    history: vi.fn(),
    item: vi.fn(),
    operators: vi.fn(),
    assign: vi.fn(),
    unassign: vi.fn()
  }
}))

vi.mock('@/router', () => ({ default: { push: vi.fn() } }))

function seedStaffing(ownerUserId) {
  const staffing = useShiftStaffingStore()
  staffing.ownerUserId = ownerUserId
  staffing.nearest = { itemId: 'item-a' }
  staffing.nearestContextItemId = 'item-a'
  staffing.currentItem = { shift: { itemId: 'item-a', canEditOperators: true } }
  staffing.history = [{ itemId: 'history-a' }]
  staffing.operators = [{ id: 'operator-a' }]
  return staffing
}

describe('auth store — изоляция auth-scoped данных', () => {
  beforeEach(() => {
    localStorage.clear()
    setActivePinia(createPinia())
    vi.clearAllMocks()
  })

  it('каждый успешный login очищает staffing, включая повторный вход того же пользователя', async () => {
    const staffing = seedStaffing('lead-a')
    const auth = useAuthStore()
    authApi.login.mockResolvedValue({
      data: {
        accessToken: 'new-token',
        refreshToken: 'new-refresh',
        user: { id: 'lead-a', role: 'ShiftLead' }
      }
    })

    const result = await auth.login({ username: 'lead-a', password: 'secret' })

    expect(result.success).toBe(true)
    expect(staffing.ownerUserId).toBe('lead-a')
    expect(staffing.nearest).toBeNull()
    expect(staffing.currentItem).toBeNull()
    expect(staffing.history).toEqual([])
    expect(staffing.operators).toEqual([])
  })

  it('fetchCurrentUser при смене authenticated user ID очищает staffing прежнего аккаунта', async () => {
    const staffing = seedStaffing('lead-a')
    const auth = useAuthStore()
    auth.user = { id: 'lead-a', role: 'ShiftLead' }
    authApi.getCurrentUser.mockResolvedValue({
      data: { id: 'lead-b', role: 'ShiftLead' }
    })

    const result = await auth.fetchCurrentUser()

    expect(result.success).toBe(true)
    expect(auth.user.id).toBe('lead-b')
    expect(staffing.ownerUserId).toBe('lead-b')
    expect(staffing.nearest).toBeNull()
    expect(staffing.currentItem).toBeNull()
    expect(staffing.history).toEqual([])
    expect(staffing.operators).toEqual([])
  })

  it('refreshTokens того же пользователя не сбрасывает его актуальный staffing', async () => {
    const staffing = seedStaffing('lead-a')
    const auth = useAuthStore()
    auth.accessToken = 'old-token'
    auth.refreshToken = 'old-refresh'
    authApi.refreshToken.mockResolvedValue({
      data: {
        accessToken: 'new-token',
        refreshToken: 'new-refresh',
        user: { id: 'lead-a', role: 'ShiftLead' }
      }
    })

    const result = await auth.refreshTokens()

    expect(result.success).toBe(true)
    expect(staffing.currentItem.shift.itemId).toBe('item-a')
    expect(staffing.ownerUserId).toBe('lead-a')
  })
})
