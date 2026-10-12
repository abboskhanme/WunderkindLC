// @vitest-environment jsdom
import { act } from 'react'
import { createRoot, type Root } from 'react-dom/client'
import { afterEach, beforeEach, describe, expect, it } from 'vitest'
import type { StudentRatingBlock as BlockData } from '@/api/services/rating'
import { StudentRatingBlock } from '../StudentRatingBlock'

/** Reyting bloki — server javobini to'g'ri chizadi (plitkalar, testlar, davomat, bo'sh holatlar). */

;(globalThis as { IS_REACT_ACT_ENVIRONMENT?: boolean }).IS_REACT_ACT_ENVIRONMENT = true
// recharts ResponsiveContainer jsdom'da ResizeObserver so'raydi.
;(globalThis as { ResizeObserver?: unknown }).ResizeObserver ??= class {
  observe() {}
  unobserve() {}
  disconnect() {}
}

let host: HTMLDivElement
let root: Root

beforeEach(() => {
  host = document.createElement('div')
  document.body.appendChild(host)
  root = createRoot(host)
})
afterEach(() => {
  act(() => root.unmount())
  host.remove()
})

const block: BlockData = {
  studentId: 's1',
  period: { kind: 'month', key: '2026-10', label: 'Oktabr 2026', from: '2026-10-01', to: '2026-10-12' },
  centre: { rank: 12, total: 430, score: 84.6, prevRank: 15 },
  groups: [
    { groupId: 'g1', groupName: 'English A1', courseName: 'Ingliz tili', rank: 3, total: 18, score: 84.6, testAvg: 88, testCount: 2, ball: 30, ballNorm: 79.5 },
  ],
  components: { testAvg: 88, testCount: 2, ball: 30, testWeight: 60 },
  history: [
    { month: '2026-09', centreRank: 15, centreTotal: 420, score: 80, testAvg: 82 },
    { month: '2026-10', centreRank: 12, centreTotal: 430, score: 84.6, testAvg: 88 },
  ],
  tests: [
    { testId: 't1', name: 'Unit 5', kind: 'interim', date: '2026-10-12', groupName: 'English A1', solved: 25, questions: 30, percent: 83.3, rankInTest: 3, takers: 18 },
  ],
  attendance: { held: 50, attended: 46, percent: 92 },
}

const render = (data: BlockData | null) =>
  act(() =>
    root.render(
      <StudentRatingBlock
        data={data}
        loading={false}
        error={null}
        period={{ kind: 'month', key: '2026-10' }}
        onPeriodChange={() => {}}
      />,
    ),
  )

describe('StudentRatingBlock', () => {
  it("plitkalar, o'rin o'zgarishi, testlar va davomat", () => {
    render(block)
    const text = host.textContent ?? ''
    expect(text).toContain("12-o'rin")
    expect(text).toContain('/ 430')
    expect(text).toContain('▲3')                    // 15 → 12: ko'tarildi
    expect(text).toContain("3-o'rin")
    expect(text).toContain('84.6')
    expect(text).toContain('Testlarim')
    expect(text).toContain('Unit 5')
    expect(text).toContain('Oraliq')
    expect(text).toContain('25/30')
    expect(text).toContain('83.3%')
    expect(text).toContain('3/18')
    expect(text).toContain('92%')
    expect(text).toContain('(46/50)')
    // Reyting bali izohi — tooltip matnida formula.
    expect(host.querySelector('[title^="Testlar 88% × 60% + Ball 79.5 × 40%"]')).not.toBeNull()
  })

  it("bo'sh davr: o'rinsiz, testsiz, darssiz — yiqilmaydi", () => {
    render({
      ...block,
      centre: { rank: null, total: 430, score: null, prevRank: null },
      groups: [],
      history: block.history.map((h) => ({ ...h, centreRank: null, score: null })),
      tests: [],
      attendance: { held: 0, attended: 0, percent: null },
    })
    const text = host.textContent ?? ''
    expect(text).toContain("Bu davrda o'rin yo'q")
    expect(text).toContain("Bu davrda test yo'q")
    expect(text).toContain("o'tilgan dars yo'q")
    expect(text).toContain("12 oylik tarix hali yo'q")
  })
})
