// @vitest-environment jsdom
import { describe, expect, it } from 'vitest'
import router from '../index'

describe('маршрут справочника типов изделий', () => {
  it('использует заголовок «Типы изделий»', () => {
    const route = router.resolve('/dictionary/product-types')

    expect(route.name).toBe('DictionaryProductTypes')
    expect(route.meta.title).toBe('Типы изделий')
  })
})
