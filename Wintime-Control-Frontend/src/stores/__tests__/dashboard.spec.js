import { describe, it, expect, beforeEach, vi } from 'vitest'
import { setActivePinia, createPinia } from 'pinia'

// Стор тянет за собой apiClient → authStore → router → createWebHistory(),
// которому нужен window. Тесты ниже работают только с локальным состоянием
// стора (без сетевых action'ов), поэтому api-модули мокаем.
vi.mock('@/api/dashboard', () => ({ dashboardApi: {} }))
vi.mock('@/api/imm', () => ({ immApi: {} }))
vi.mock('@/api/shifts', () => ({ shiftsApi: {} }))

const { useDashboardStore } = await import('@/stores/dashboard')

function seed(store) {
  store.imms = [
    { id: '1', name: 'A', status: 'Production' },
    { id: '2', name: 'B', status: 'Production' },
    { id: '3', name: 'C', status: 'Setup' },
    { id: '4', name: 'D', status: 'Downtime' },
    { id: '5', name: 'E', status: 'NoTask' },
    { id: '6', name: 'F', status: 'Unplanned' },
    { id: '7', name: 'G', status: 'Offline' },
  ]
}

describe('dashboard store — эффективные состояния', () => {
  beforeEach(() => setActivePinia(createPinia()))

  it('группирует ТПА по эффективным состояниям', () => {
    const store = useDashboardStore()
    seed(store)
    expect(store.workingImms).toHaveLength(2)
    expect(store.setupImms).toHaveLength(1)
    expect(store.downtimeImms).toHaveLength(1)
    expect(store.noTaskImms).toHaveLength(1)
    expect(store.unplannedImms).toHaveLength(1)
    expect(store.offlineImms).toHaveLength(1)
  })

  it('overallEfficiency = (Production + Setup) / всего', () => {
    const store = useDashboardStore()
    seed(store)
    // (2 + 1) / 7 = 42.857 → 43
    expect(store.overallEfficiency).toBe(43)
  })

  it('filteredImms фильтрует по эффективному статусу', () => {
    const store = useDashboardStore()
    seed(store)
    store.setFilter('status', 'Downtime')
    expect(store.filteredImms).toHaveLength(1)
    expect(store.filteredImms[0].id).toBe('4')
  })
})
