// @vitest-environment jsdom
import { act, useEffect, useState } from 'react'
import { createRoot, type Root } from 'react-dom/client'
import { afterEach, beforeEach, describe, expect, it } from 'vitest'
import { MoneyInput, NumberInput } from '../NumberInput'

/**
 * Asosiy qulf — foydalanuvchi shikoyati: "summa maydonida 0 turadi, uni o'chirib bo'lmaydi".
 * `type="number"` + `Number(e.target.value)` bo'shatilgan maydonni 0 qilardi.
 */

;(globalThis as { IS_REACT_ACT_ENVIRONMENT?: boolean }).IS_REACT_ACT_ENVIRONMENT = true

let host: HTMLDivElement
let root: Root
let last: number | null | undefined
let setOuter: (v: number | null) => void

function Harness({ initial, mode = 'money' }: { initial: number | null; mode?: 'money' | 'number' | 'kpi' }) {
  const [v, setV] = useState<number | null>(initial)
  // Test tashqarisidan kuzatish uchun — render'da emas, effektda (react-hooks/globals).
  useEffect(() => {
    setOuter = setV
    last = v
  }, [v])
  if (mode === 'money') return <MoneyInput value={v} onChange={setV} as="input" />
  if (mode === 'kpi') return <NumberInput value={v} onChange={setV} as="input" fractional allowNegative />
  return <NumberInput value={v} onChange={setV} as="input" />
}

const input = () => host.querySelector('input') as HTMLInputElement

/** Brauzerdagi yozishga o'xshatib qiymat beradi (React `input` hodisasini tinglaydi). */
function type(value: string) {
  const setter = Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value')!.set!
  act(() => {
    setter.call(input(), value)
    input().dispatchEvent(new Event('input', { bubbles: true }))
  })
}

beforeEach(() => {
  host = document.createElement('div')
  document.body.appendChild(host)
  root = createRoot(host)
})
afterEach(() => {
  act(() => root.unmount())
  host.remove()
})

describe('MoneyInput', () => {
  it("0 ni o'chirib bo'sh qoldirish mumkin — qiymat null, maydon bo'sh", () => {
    act(() => root.render(<Harness initial={0} />))
    expect(input().value).toBe('0')
    type('')
    expect(input().value).toBe('')
    expect(last).toBeNull()
  })

  it('yozilayotganda 3 xonadan ajratadi, son to\'g\'ri chiqadi', () => {
    act(() => root.render(<Harness initial={null} />))
    expect(input().value).toBe('')
    type('1250000')
    expect(input().value).toBe('1 250 000')
    expect(last).toBe(1250000)
  })

  it("0 turganida yozilsa '05' emas, '5'", () => {
    act(() => root.render(<Harness initial={0} />))
    type('05')
    expect(input().value).toBe('5')
    expect(last).toBe(5)
  })

  it('tashqaridan qiymat o\'zgarsa (forma tozalandi) maydon yangilanadi', () => {
    act(() => root.render(<Harness initial={100} />))
    act(() => setOuter(750000))
    expect(input().value).toBe('750 000')
    act(() => setOuter(null))
    expect(input().value).toBe('')
  })

  it('telefonda raqamli klaviatura ochiladi', () => {
    act(() => root.render(<Harness initial={null} />))
    expect(input().getAttribute('inputmode')).toBe('numeric')
    expect(input().type).toBe('text')
  })
})

describe('faqat butun son (foydalanuvchi qarori)', () => {
  it("vergul/nuqta yozib bo'lmaydi", () => {
    act(() => root.render(<Harness initial={null} mode="number" />))
    type('12,')
    expect(input().value).toBe('12')
    expect(input().getAttribute('inputmode')).toBe('numeric')
  })

  it("serverdan kasrli summa kelsa yaxlitlanadi va formaga ham BUTUN son qaytadi", () => {
    act(() => root.render(<Harness initial={125000.6} />))
    expect(input().value).toBe('125 001')
    expect(last).toBe(125001)
  })
})

describe('KPI istisnosi (fractional)', () => {
  it("koeffitsiyent -0,2 o'zgarmaydi va kasr yoziladi", () => {
    act(() => root.render(<Harness initial={-0.2} mode="kpi" />))
    expect(input().value).toBe('-0,2')
    expect(last).toBe(-0.2)
    type('1,2')
    expect(input().value).toBe('1,2')
    expect(last).toBe(1.2)
  })
})
