// @vitest-environment jsdom
import { act } from 'react'
import { createRoot, type Root } from 'react-dom/client'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import { afterEach, beforeEach, describe, expect, it } from 'vitest'
import { StudentMobileLayout } from '../StudentMobileLayout'

/** Cabinet shell: bottom navigation (active tab per screen group) and the Telegram Mini App wiring. */

;(globalThis as { IS_REACT_ACT_ENVIRONMENT?: boolean }).IS_REACT_ACT_ENVIRONMENT = true

let host: HTMLDivElement
let root: Root

beforeEach(() => {
  host = document.createElement('div')
  document.body.appendChild(host)
  root = createRoot(host)
  localStorage.clear()
})
afterEach(() => {
  act(() => root.unmount())
  host.remove()
  delete window.Telegram
})

const render = (path: string) =>
  act(() =>
    root.render(
      <MemoryRouter key={path} initialEntries={[path]}>
        <Routes>
          <Route path="/student" element={<StudentMobileLayout />}>
            <Route path="*" element={<div className="screen">screen</div>} />
            <Route index element={<div className="screen">home</div>} />
          </Route>
        </Routes>
      </MemoryRouter>,
    ),
  )

const activeTab = () => host.querySelector('.st-nav [aria-current="page"]')?.textContent

describe('StudentMobileLayout', () => {
  it("5 ta tab; bo'lim ekranlari o'z tabini faol qiladi", () => {
    render('/student/attendance')
    const labels = [...host.querySelectorAll('.st-nav a')].map((a) => a.textContent)
    expect(labels).toEqual(['Bosh sahifa', 'Darslar', 'Natijalar', "To'lovlar", 'Profil'])
    expect(activeTab()).toBe('Darslar')
  })

  it('natijalar va profil guruhlari', () => {
    render('/student/tests')
    expect(activeTab()).toBe('Natijalar')
    render('/student/certificates')
    expect(activeTab()).toBe('Profil')
  })

  it("qo'lda tanlangan tungi rejim saqlanadi (oddiy brauzer)", () => {
    localStorage.setItem('student_theme', 'dark')
    render('/student')
    expect(host.querySelector('.student-app')?.getAttribute('data-theme')).toBe('dark')
  })

  it("Telegram ichida: mavzu Telegram'dan, ichki sahifada native BackButton", () => {
    const calls: string[] = []
    window.Telegram = {
      WebApp: {
        ready() {},
        expand() {},
        initData: 'query_id=x',
        colorScheme: 'dark',
        themeParams: { bg_color: '#17212b', text_color: '#f5f5f5', hint_color: '#708499', secondary_bg_color: '#0e1621' },
        isVersionAtLeast: () => true,
        setHeaderColor: (c) => calls.push('header:' + c),
        setBackgroundColor: (c) => calls.push('bg:' + c),
        BackButton: {
          isVisible: false,
          show: () => calls.push('back:show'),
          hide: () => calls.push('back:hide'),
          onClick: () => {},
          offClick: () => {},
        },
      },
    }
    render('/student/settings')
    const app = host.querySelector('.student-app') as HTMLElement
    expect(app.getAttribute('data-theme')).toBe('dark')
    expect(app.style.getPropertyValue('--surface')).toBe('#17212b')
    expect(calls).toContain('back:show')
    expect(calls).toContain('header:#0e1621')
    render('/student/finance')
    expect(calls[calls.length - 1]).toBe('back:hide')
  })
})
