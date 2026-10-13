// @vitest-environment jsdom
import { act, useEffect, useState } from 'react'
import { createRoot, type Root } from 'react-dom/client'
import { afterEach, beforeEach, describe, expect, it } from 'vitest'
import { DateInput } from '../DateInput'

/**
 * Foydalanuvchi shikoyati (2026-10-13): sana maydonida OY birinchi ("10/15/2026" — inglizcha brauzer). Endi maydon
 * HAR DOIM "15.10.2026", qiymat esa avvalgidek ISO — `onChange={(e) => set(e.target.value)}` o'zgarishsiz.
 */

;(globalThis as { IS_REACT_ACT_ENVIRONMENT?: boolean }).IS_REACT_ACT_ENVIRONMENT = true

let host: HTMLDivElement
let root: Root
let last = ''
let events = 0
let setOuter: (v: string) => void = () => {}

function Harness({ initial, min, max }: { initial: string; min?: string; max?: string }) {
  const [v, setV] = useState(initial)
  useEffect(() => {
    setOuter = setV
    last = v
  }, [v])
  return (
    <DateInput
      name="d"
      value={v}
      min={min}
      max={max}
      className="h-9 w-40 rounded-lg px-3"
      onChange={(e) => {
        events++
        setV(e.target.value)
      }}
    />
  )
}

const visible = () => host.querySelector('input[type="text"]') as HTMLInputElement
const native = () => host.querySelector('input[type="date"]') as HTMLInputElement

function typeInto(el: HTMLInputElement, value: string) {
  const setter = Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value')!.set!
  act(() => {
    setter.call(el, value)
    el.dispatchEvent(new Event('input', { bubbles: true }))
  })
}

beforeEach(() => {
  host = document.createElement('div')
  document.body.appendChild(host)
  root = createRoot(host)
  events = 0
})
afterEach(() => {
  act(() => root.unmount())
  host.remove()
})

describe('DateInput', () => {
  it('ISO qiymatni kun.oy.yil ko\'rsatadi; kenglik klassi o\'ramda', () => {
    act(() => root.render(<Harness initial="2026-10-15" />))
    expect(visible().value).toBe('15.10.2026')
    expect(native().value).toBe('2026-10-15')
    expect(native().name).toBe('d')
    expect((visible().parentElement as HTMLElement).className).toContain('w-40')
    expect(visible().className).not.toContain('w-40')
  })

  it('yozilgan TO\'LIQ sana ISO bo\'lib onChange ga ketadi; chala matn yuborilmaydi', () => {
    act(() => root.render(<Harness initial="" />))
    typeInto(visible(), '0101')
    expect(visible().value).toBe('01.01')
    expect(events).toBe(0)
    typeInto(visible(), '01012027')
    expect(visible().value).toBe('01.01.2027')
    expect(last).toBe('2027-01-01')
    expect(events).toBe(1)
  })

  it('noto\'g\'ri sana yuborilmaydi va maydondan chiqqanda eski qiymat qaytadi', () => {
    act(() => root.render(<Harness initial="2026-10-15" />))
    typeInto(visible(), '31022026')
    expect(events).toBe(0)
    expect(visible().getAttribute('aria-invalid')).toBe('true')
    act(() => {
      visible().dispatchEvent(new FocusEvent('focusout', { bubbles: true }))
    })
    expect(visible().value).toBe('15.10.2026')
    expect(last).toBe('2026-10-15')
  })

  it('max dan keyingi sana qabul qilinmaydi', () => {
    act(() => root.render(<Harness initial="" max="2026-10-13" />))
    typeInto(visible(), '14102026')
    expect(events).toBe(0)
    typeInto(visible(), '13102026')
    expect(last).toBe('2026-10-13')
  })

  it('tozalash bo\'sh qiymat yuboradi', () => {
    act(() => root.render(<Harness initial="2026-10-15" />))
    typeInto(visible(), '')
    expect(last).toBe('')
    expect(events).toBe(1)
  })

  it('kalendardan tanlash (native) va tashqaridan o\'zgarish matnni yangilaydi', () => {
    act(() => root.render(<Harness initial="2026-10-15" />))
    typeInto(native(), '2026-11-02')
    expect(last).toBe('2026-11-02')
    expect(visible().value).toBe('02.11.2026')
    act(() => setOuter('2025-01-31'))
    expect(visible().value).toBe('31.01.2025')
  })
})
