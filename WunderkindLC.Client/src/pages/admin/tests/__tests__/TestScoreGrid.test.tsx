// @vitest-environment jsdom
import { act } from 'react'
import { createRoot, type Root } from 'react-dom/client'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import type { TestResultDetail, TestScoreBatchResult, TestScoreBatchRow } from '@/types'
import { TestScoreGrid } from '../TestScoreGrid'

/**
 * CRM U4a — qabul mezoni #1 (docs/SPEC-crm-upgrade.md P4.9): 30 savolli testga Excel'dan 18 qatorli ustun qo'yilsa
 * 18 qator to'ladi; 31 — qizil va yuborilmaydi; Enter/↑/↓ qatorlar orasida yuradi; avtosaqlash — bitta batch.
 */
;(globalThis as { IS_REACT_ACT_ENVIRONMENT?: boolean }).IS_REACT_ACT_ENVIRONMENT = true

let host: HTMLDivElement
let root: Root

const names = Array.from({ length: 18 }, (_, i) => `O'quvchi ${String(i + 1).padStart(2, '0')}`)
const detail: TestResultDetail = {
  id: 't1', groupId: 'g1', groupName: 'A1', name: 'Unit 5', date: '2026-10-12', maxScore: 30,
  createdAt: '', createdBy: '', certificateEnabled: false, certificateTemplateId: '',
  kind: 'interim', questionCount: 30, resultsSentAt: null,
  rows: names.map((fullName, i) => ({ studentId: `s${i + 1}`, fullName, score: null, rank: 0, member: true })),
}

const inputs = () => Array.from(host.querySelectorAll<HTMLInputElement>('input[data-grid-row]'))

function paste(el: HTMLInputElement, text: string) {
  const evt = new Event('paste', { bubbles: true, cancelable: true })
  Object.defineProperty(evt, 'clipboardData', { value: { getData: () => text } })
  el.dispatchEvent(evt)
}

beforeEach(() => {
  vi.useFakeTimers()
  host = document.createElement('div')
  document.body.appendChild(host)
  root = createRoot(host)
})

afterEach(() => {
  act(() => root.unmount())
  host.remove()
  vi.useRealTimers()
})

describe('TestScoreGrid', () => {
  it("18 qatorli Excel ustuni 18 qatorni to'ldiradi, 31 qizil va saqlanmaydi, bitta batch", async () => {
    const sent: TestScoreBatchRow[][] = []
    const save = async (rows: TestScoreBatchRow[]): Promise<TestScoreBatchResult> => {
      sent.push(rows)
      return { detail, errors: [] }
    }
    act(() => root.render(<TestScoreGrid detail={detail} editable save={save} onDetail={() => {}} />))

    const column = Array.from({ length: 18 }, (_, i) => (i === 4 ? '31' : `${10 + i}/30`)).join('\n') + '\n'
    act(() => paste(inputs()[0], column))

    const vals = inputs().map((i) => i.value)
    expect(vals).toHaveLength(18)
    expect(vals[0]).toBe('10')
    expect(vals[17]).toBe('27')
    expect(inputs()[4].getAttribute('aria-invalid')).toBe('true')
    // Foiz jonli: 10/30 = 33.3%
    expect(host.textContent).toContain('33.3%')

    await act(async () => {
      vi.advanceTimersByTime(800)
    })
    expect(sent).toHaveLength(1)
    expect(sent[0]).toHaveLength(17) // 31 yuborilmaydi
    expect(sent[0].some((r) => r.solved === 31)).toBe(false)
  })

  it('Enter/↓ keyingi qatorga, Shift+Enter/↑ oldingisiga o\'tadi', () => {
    act(() =>
      root.render(
        <TestScoreGrid detail={detail} editable save={async () => ({ detail, errors: [] })} onDetail={() => {}} />,
      ),
    )
    const [first, second] = inputs()
    act(() => first.focus())
    act(() => {
      first.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter', bubbles: true }))
    })
    expect(document.activeElement).toBe(second)
    act(() => {
      second.dispatchEvent(new KeyboardEvent('keydown', { key: 'ArrowUp', bubbles: true }))
    })
    expect(document.activeElement).toBe(first)
    act(() => {
      first.dispatchEvent(new KeyboardEvent('keydown', { key: 'ArrowDown', bubbles: true }))
    })
    expect(document.activeElement).toBe(second)
    act(() => {
      second.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter', shiftKey: true, bubbles: true }))
    })
    expect(document.activeElement).toBe(first)
  })
})
