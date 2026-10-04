import { describe, it, expect, vi } from 'vitest'
import axios from 'axios'

vi.mock('@/api/client', () => ({
  default: { get: vi.fn(() => Promise.resolve({ data: {} })), post: vi.fn() }
}))

const { default: apiClient } = await import('@/api/client')
const { reportsApi } = await import('@/api/reports')

describe('reportsApi.getEquipment', () => {
  it('сериализует immIds без индексов: immIds=a&immIds=b', () => {
    reportsApi.getEquipment({ dateFrom: '2026-10-01', immIds: ['a', 'b'], archive: 'exclude' })
    const [url, config] = apiClient.get.mock.calls[0]
    const uri = axios.getUri({ url, params: config.params, paramsSerializer: config.paramsSerializer })
    expect(uri).toBe('/reports/equipment?dateFrom=2026-10-01&immIds=a&immIds=b&archive=exclude')
  })
})
