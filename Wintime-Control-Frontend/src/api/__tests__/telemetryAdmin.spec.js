import { beforeEach, describe, expect, it, vi } from 'vitest'

vi.mock('@/api/client', () => ({
  default: { get: vi.fn(), delete: vi.fn() }
}))

import apiClient from '@/api/client'
import { telemetryAdminApi } from '@/api/telemetryAdmin'

describe('telemetryAdminApi', () => {
  beforeEach(() => vi.clearAllMocks())

  it('передаёт IANA-пояс в statistics', () => {
    telemetryAdminApi.getStatistics('Europe/Moscow')
    expect(apiClient.get).toHaveBeenCalledWith('/admin/telemetry/statistics', {
      params: { timeZone: 'Europe/Moscow' }
    })
  })

  it('передаёт локальную дату и пояс в preview', () => {
    telemetryAdminApi.previewCleanup('2026-11-27', 'Europe/Moscow')
    expect(apiClient.get).toHaveBeenCalledWith('/admin/telemetry/cleanup/preview', {
      params: { date: '2026-11-27', timeZone: 'Europe/Moscow' }
    })
  })

  it('ждёт DELETE дольше серверного 30-минутного лимита', () => {
    telemetryAdminApi.cleanup('2026-11-27', 'Europe/Moscow')
    expect(apiClient.delete).toHaveBeenCalledWith('/admin/telemetry/cleanup', {
      params: { date: '2026-11-27', timeZone: 'Europe/Moscow' },
      timeout: 31 * 60 * 1000
    })
  })
})
