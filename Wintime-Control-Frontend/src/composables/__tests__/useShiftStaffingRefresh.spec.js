import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { effectScope } from 'vue'
import {
  SHIFT_STAFFING_REFRESH_MS,
  useShiftStaffingRefresh
} from '../useShiftStaffingRefresh'

const MAX_TIMEOUT_MS = 2_147_483_647

function deferred() {
  let resolve
  let reject
  const promise = new Promise((res, rej) => {
    resolve = res
    reject = rej
  })
  return { promise, resolve, reject }
}

function createComposable(options) {
  const scope = effectScope()
  let composable
  scope.run(() => { composable = useShiftStaffingRefresh(options) })
  return { ...composable, dispose: () => scope.stop() }
}

describe('useShiftStaffingRefresh', () => {
  beforeEach(() => {
    vi.useFakeTimers()
    vi.setSystemTime(new Date('2026-11-08T15:59:00Z'))
  })

  afterEach(() => {
    vi.clearAllTimers()
    vi.useRealTimers()
    vi.restoreAllMocks()
  })

  it('обновляет каждые 60 секунд и в EndAtUtc, очищая оба таймера', async () => {
    const refresh = vi.fn().mockResolvedValue({ success: true })
    const getEndAtUtc = vi.fn(() => '2026-11-08T16:00:00Z')
    const { stop } = createComposable({ getEndAtUtc, refresh })

    await vi.advanceTimersByTimeAsync(60_000)
    expect(refresh).toHaveBeenCalledTimes(1)
    await vi.advanceTimersByTimeAsync(60_000)
    expect(refresh).toHaveBeenCalledTimes(2)
    stop()
    await vi.advanceTimersByTimeAsync(120_000)
    expect(refresh).toHaveBeenCalledTimes(2)
    expect(vi.getTimerCount()).toBe(0)
  })

  it('экспортирует период 60 секунд и при истёком EndAtUtc обновляет немедленно', async () => {
    const refresh = vi.fn().mockResolvedValue({ success: true })
    const { stop } = createComposable({
      getEndAtUtc: () => '2026-11-08T15:58:00Z',
      refresh
    })

    expect(SHIFT_STAFFING_REFRESH_MS).toBe(60_000)
    await vi.advanceTimersByTimeAsync(0)
    expect(refresh).toHaveBeenCalledTimes(1)
    stop()
  })

  it('после refresh пересоздаёт timeout по новому EndAtUtc', async () => {
    let endAtUtc = '2026-11-08T15:59:10Z'
    const refresh = vi.fn().mockImplementation(async () => {
      endAtUtc = '2026-11-08T16:00:20Z'
      return { success: true }
    })
    const { stop } = createComposable({ getEndAtUtc: () => endAtUtc, refresh })

    await vi.advanceTimersByTimeAsync(10_050)
    expect(refresh).toHaveBeenCalledTimes(1)
    await vi.advanceTimersByTimeAsync(60_000)
    expect(refresh).toHaveBeenCalledTimes(2)
    await vi.advanceTimersByTimeAsync(10_000)
    expect(refresh).toHaveBeenCalledTimes(3)
    stop()
  })

  it('ограничивает дальний timeout и после refresh снова использует capped delay', async () => {
    const timeout = vi.spyOn(globalThis, 'setTimeout')
    const refresh = vi.fn().mockResolvedValue({ success: true })
    const composable = createComposable({
      getEndAtUtc: () => new Date(Date.now() + MAX_TIMEOUT_MS + 100_000).toISOString(),
      refresh
    })

    expect(timeout.mock.calls.some(([, delay]) => delay === MAX_TIMEOUT_MS)).toBe(true)
    await composable.refresh()
    expect(timeout.mock.calls.filter(([, delay]) => delay === MAX_TIMEOUT_MS)).toHaveLength(2)
    composable.stop()
  })

  it('уничтожение scope очищает interval и timeout', () => {
    const { dispose } = createComposable({
      getEndAtUtc: () => '2026-11-08T16:00:00Z',
      refresh: vi.fn().mockResolvedValue({ success: true })
    })

    expect(vi.getTimerCount()).toBe(2)
    dispose()
    expect(vi.getTimerCount()).toBe(0)
  })

  it('не запускает overlapping refresh из ручного, periodic и end triggers', async () => {
    const request = deferred()
    let endAtUtc = '2026-11-08T15:58:00Z'
    const refresh = vi.fn().mockImplementation(() => {
      endAtUtc = null
      return request.promise
    })
    const composable = createComposable({
      getEndAtUtc: () => endAtUtc,
      refresh
    })

    await vi.advanceTimersByTimeAsync(0)
    await composable.refresh()
    await vi.advanceTimersByTimeAsync(60_000)
    expect(refresh).toHaveBeenCalledTimes(1)

    request.resolve({ success: true })
    await Promise.resolve()
    await vi.advanceTimersByTimeAsync(60_000)
    expect(refresh).toHaveBeenCalledTimes(2)
    composable.stop()
  })

  it('после неудачного refresh истёкший EndAtUtc ждёт не меньше 60 секунд', async () => {
    const refresh = vi.fn().mockResolvedValue({ success: false })
    const { stop } = createComposable({
      getEndAtUtc: () => '2026-11-08T15:58:00Z',
      refresh
    })

    await vi.advanceTimersByTimeAsync(0)
    expect(refresh).toHaveBeenCalledTimes(1)
    await vi.advanceTimersByTimeAsync(59_999)
    expect(refresh).toHaveBeenCalledTimes(1)
    await vi.advanceTimersByTimeAsync(1)
    expect(refresh).toHaveBeenCalledTimes(2)
    stop()
  })
})
