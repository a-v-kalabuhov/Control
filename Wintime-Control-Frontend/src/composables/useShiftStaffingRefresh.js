import { getCurrentScope, onScopeDispose } from 'vue'

export const SHIFT_STAFFING_REFRESH_MS = 60_000
const MAX_TIMEOUT_MS = 2_147_483_647

export function useShiftStaffingRefresh({ getEndAtUtc, refresh }) {
  let stopped = false
  let running = false
  let pollTimer = null
  let endTimer = null

  async function runRefresh() {
    if (stopped || running) return
    running = true
    try {
      await refresh()
    } catch {
      // Store показывает ошибку; таймер не должен создавать неперехваченный rejection.
    } finally {
      running = false
      if (!stopped) scheduleEnd(true)
    }
  }

  function scheduleEnd(afterRefresh = false) {
    clearTimeout(endTimer)
    endTimer = null
    const end = getEndAtUtc()
    if (!end || stopped) return

    const remaining = Date.parse(end) - Date.now() + 50
    if (!Number.isFinite(remaining)) return
    let delay = Math.max(0, Math.min(remaining, MAX_TIMEOUT_MS))
    // После любого refresh истёкший nearest ждёт период: иначе ошибка reload
    // или неизменный expired EndAtUtc порождают цикл запросов и сообщений.
    if (afterRefresh && remaining <= 50) delay = SHIFT_STAFFING_REFRESH_MS
    endTimer = setTimeout(runRefresh, delay)
  }

  pollTimer = setInterval(runRefresh, SHIFT_STAFFING_REFRESH_MS)
  scheduleEnd()

  function stop() {
    stopped = true
    clearInterval(pollTimer)
    clearTimeout(endTimer)
    pollTimer = null
    endTimer = null
  }

  if (getCurrentScope()) onScopeDispose(stop)
  return { stop, refresh: runRefresh }
}
