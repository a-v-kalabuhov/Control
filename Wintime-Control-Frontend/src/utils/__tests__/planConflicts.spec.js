import { describe, it, expect } from 'vitest'
import { conflictsOf, worstKind, otherSide, taskRefLabel, conflictLine, CONFLICT_KIND_COLORS } from '../planConflicts'

const ref = (id, extra = {}) => ({ id, immName: `ТПА-${id}`, moldName: `ПФ-${id}`, productArticle: `A${id}.0`, ...extra })
const conflict = (kind, resource, a, b, overlapSeconds = 4800) => ({ kind, resource, task: ref(a), other: ref(b), overlapSeconds })

describe('planConflicts utils', () => {
  it('conflictsOf — пары с заданием с любой стороны', () => {
    const items = [conflict('Plan', 'Imm', '1', '2'), conflict('Deviation', 'Mold', '3', '1'), conflict('Plan', 'Imm', '4', '5')]
    expect(conflictsOf(items, '1')).toHaveLength(2)
    expect(conflictsOf(items, '9')).toEqual([])
  })

  it('worstKind — конфликт плана важнее отклонения', () => {
    expect(worstKind([conflict('Deviation', 'Imm', '1', '2'), conflict('Plan', 'Imm', '1', '3')])).toBe('Plan')
    expect(worstKind([conflict('Deviation', 'Imm', '1', '2')])).toBe('Deviation')
    expect(worstKind([])).toBeNull()
  })

  it('otherSide — вторая сторона пары', () => {
    const c = conflict('Plan', 'Imm', '1', '2')
    expect(otherSide(c, '1').id).toBe('2')
    expect(otherSide(c, '2').id).toBe('1')
  })

  it('taskRefLabel — артикул · ПФ · ТПА', () => {
    expect(taskRefLabel(ref('1'))).toBe('A1.0 · ПФ-1 · ТПА-1')
  })

  it('conflictLine — вид, ресурс, другое задание, пересечение', () => {
    expect(conflictLine(conflict('Plan', 'Imm', '1', '2'), '1'))
      .toBe('Конфликт плана по ТПА с «A2.0 · ПФ-2 · ТПА-2», пересечение 1ч 20м')
    expect(conflictLine(conflict('Deviation', 'Mold', '1', '2', 1800), '2'))
      .toBe('Конфликт отклонения по ПФ «ПФ-1» с «A1.0 · ПФ-1 · ТПА-1», пересечение 30м')
  })

  it('цвета видов', () => {
    expect(CONFLICT_KIND_COLORS).toEqual({ Plan: '#f56c6c', Deviation: '#e6a23c' })
  })
})
