import { describe, it, expect } from 'vitest'
import {
  viewPrefsKey, readViewPrefs, writeViewPrefs, resolveSignalOrder,
} from '@/utils/monitoringViewPrefs'

function memoryStorage(initial = {}) {
  const data = { ...initial }
  return {
    data,
    getItem: (k) => (k in data ? data[k] : null),
    setItem: (k, v) => { data[k] = String(v) },
  }
}
const throwingStorage = {
  getItem: () => { throw new Error('SecurityError') },
  setItem: () => { throw new Error('QuotaExceededError') },
}
const KEY = 'wtctrl.monitoring.view.v1.u1.t1'

describe('viewPrefsKey', () => {
  it('собирает ключ из пользователя и шаблона', () => {
    expect(viewPrefsKey('u1', 't1')).toBe(KEY)
  })

  it('null без пользователя или шаблона', () => {
    expect(viewPrefsKey(null, 't1')).toBeNull()
    expect(viewPrefsKey('u1', undefined)).toBeNull()
    expect(viewPrefsKey('', 't1')).toBeNull()
  })
})

describe('readViewPrefs', () => {
  it('читает сохранённый массив строк', () => {
    const s = memoryStorage({ [KEY]: '["B","A"]' })
    expect(readViewPrefs(s, KEY)).toEqual(['B', 'A'])
  })

  it('null, если ключа нет', () => {
    expect(readViewPrefs(memoryStorage(), KEY)).toBeNull()
  })

  it('null на мусоре: не JSON, не массив, не строки', () => {
    expect(readViewPrefs(memoryStorage({ [KEY]: '{oops' }), KEY)).toBeNull()
    expect(readViewPrefs(memoryStorage({ [KEY]: '{"a":1}' }), KEY)).toBeNull()
    expect(readViewPrefs(memoryStorage({ [KEY]: '["A",1]' }), KEY)).toBeNull()
  })

  it('null без хранилища или ключа и когда getItem бросает', () => {
    expect(readViewPrefs(null, KEY)).toBeNull()
    expect(readViewPrefs(memoryStorage({ [KEY]: '["A"]' }), null)).toBeNull()
    expect(readViewPrefs(throwingStorage, KEY)).toBeNull()
  })
})

describe('writeViewPrefs', () => {
  it('пишет JSON-массив', () => {
    const s = memoryStorage()
    writeViewPrefs(s, KEY, ['C', 'A'])
    expect(s.data[KEY]).toBe('["C","A"]')
  })

  it('ничего не делает без хранилища или ключа и не бросает, когда setItem бросает', () => {
    const s = memoryStorage()
    writeViewPrefs(s, null, ['A'])
    expect(s.data).toEqual({})
    expect(() => writeViewPrefs(null, KEY, ['A'])).not.toThrow()
    expect(() => writeViewPrefs(throwingStorage, KEY, ['A'])).not.toThrow()
  })
})

describe('resolveSignalOrder', () => {
  const available = ['A', 'B', 'C', 'D']

  it('сохранённый порядок', () => {
    expect(resolveSignalOrder(available, ['C', 'A'])).toEqual(['C', 'A'])
  })

  it('отбрасывает отсутствующие у ТПА датчики и дубли', () => {
    expect(resolveSignalOrder(available, ['X', 'B', 'B', 'D'])).toEqual(['B', 'D'])
  })

  it('первый датчик, если сохранения нет или пересечение пустое', () => {
    expect(resolveSignalOrder(available, null)).toEqual(['A'])
    expect(resolveSignalOrder(available, [])).toEqual(['A'])
    expect(resolveSignalOrder(available, ['X'])).toEqual(['A'])
  })

  it('пусто, если у ТПА нет датчиков', () => {
    expect(resolveSignalOrder([], ['A'])).toEqual([])
  })
})
