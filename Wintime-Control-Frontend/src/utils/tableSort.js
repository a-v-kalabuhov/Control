import { computed, ref, unref, watch } from 'vue'

function readCookie(name) {
  if (typeof document === 'undefined' || !name) return null
  const match = document.cookie.match(new RegExp('(^| )' + name + '=([^;]+)'))
  return match ? decodeURIComponent(match[2]) : null
}

function writeCookie(name, value) {
  if (typeof document === 'undefined' || !name) return
  const expires = new Date(Date.now() + 365 * 864e5).toUTCString()
  document.cookie = `${name}=${encodeURIComponent(value)};expires=${expires};path=/`
}

function compareValues(a, b) {
  if (a == null && b == null) return 0
  if (a == null) return -1
  if (b == null) return 1
  if (typeof a === 'string') return a.localeCompare(String(b), 'ru')
  if (typeof b === 'string') return String(a).localeCompare(b, 'ru')
  return a < b ? -1 : a > b ? 1 : 0
}

// Поведение как в списке задач: desc → asc → без сортировки, выбор сохраняется в cookie.
export function useTableSort(cookieName) {
  const sort = ref({ field: null, dir: null })

  watch(() => unref(cookieName), (name) => {
    sort.value = { field: null, dir: null }
    const saved = readCookie(name)
    if (!saved) return
    try {
      const parsed = JSON.parse(saved)
      if (typeof parsed?.field === 'string' && ['asc', 'desc'].includes(parsed?.dir)) {
        sort.value = { field: parsed.field, dir: parsed.dir }
      }
    } catch {}
  }, { immediate: true })

  function handleSort(field) {
    if (sort.value.field !== field) {
      sort.value = { field, dir: 'desc' }
    } else if (sort.value.dir === 'desc') {
      sort.value = { field, dir: 'asc' }
    } else {
      sort.value = { field: null, dir: null }
    }
    writeCookie(unref(cookieName), JSON.stringify(sort.value))
  }

  function sortIcon(field) {
    if (sort.value.field !== field) return ''
    return sort.value.dir === 'desc' ? '▼' : '▲'
  }

  function sortedItems(items, getters = {}) {
    return computed(() => {
      const list = unref(items) ?? []
      const { field, dir } = sort.value
      if (!field || !dir) return list
      const getValue = getters[field] ?? ((row) => row[field])
      const sign = dir === 'desc' ? -1 : 1
      return [...list].sort((a, b) => compareValues(getValue(a), getValue(b)) * sign)
    })
  }

  return { sort, handleSort, sortIcon, sortedItems }
}
