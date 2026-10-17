import { describe, expect, it } from 'vitest'
import { conversionPct, deltaPct } from '../executive'

describe('executive', () => {
  it("o'tgan oyga nisbatan o'zgarish", () => {
    expect(deltaPct(112, 100)).toEqual({ value: '+12%', dir: 'up' })
    expect(deltaPct(95, 100)).toEqual({ value: '-5%', dir: 'down' })
    expect(deltaPct(100, 0)).toBeUndefined()
  })

  it('lid konversiyasi', () => {
    expect(conversionPct(40, 10)).toBe(25)
    expect(conversionPct(0, 0)).toBeNull()
  })
})
