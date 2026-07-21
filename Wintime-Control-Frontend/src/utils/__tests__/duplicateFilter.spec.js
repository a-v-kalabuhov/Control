import { describe, it, expect } from 'vitest'
import { createDuplicateFilter } from '../duplicateFilter'

describe('createDuplicateFilter', () => {
  it('пропускает первое появление ключа', () => {
    const accept = createDuplicateFilter(15000)
    expect(accept('A')).toBe(true)
  })

  it('подавляет тот же ключ внутри окна', () => {
    let t = 1000
    const accept = createDuplicateFilter(15000, () => t)
    expect(accept('A')).toBe(true)
    t += 5000
    expect(accept('A')).toBe(false)
    t += 9999 // суммарно 14999 мс < 15000
    expect(accept('A')).toBe(false)
  })

  it('снова пропускает ключ после окна', () => {
    let t = 1000
    const accept = createDuplicateFilter(15000, () => t)
    expect(accept('A')).toBe(true)
    t += 15000 // ровно окно — уже не «внутри»
    expect(accept('A')).toBe(true)
  })

  it('разные ключи независимы', () => {
    let t = 1000
    const accept = createDuplicateFilter(15000, () => t)
    expect(accept('A')).toBe(true)
    expect(accept('B')).toBe(true)
    expect(accept('A')).toBe(false) // A ещё в окне
    expect(accept('B')).toBe(false)
  })
})
