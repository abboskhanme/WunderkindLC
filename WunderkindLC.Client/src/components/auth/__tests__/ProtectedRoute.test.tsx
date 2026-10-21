// @vitest-environment jsdom
import { act } from 'react'
import { createRoot, type Root } from 'react-dom/client'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'

;(globalThis as { IS_REACT_ACT_ENVIRONMENT?: boolean }).IS_REACT_ACT_ENVIRONMENT = true

let user: { role: string; permissions?: string[] | null } | null = null
vi.mock('@/context/auth-context', () => ({
  useAuth: () => ({ isAuthenticated: !!user, user }),
}))

import { ProtectedRoute } from '../ProtectedRoute'

let host: HTMLDivElement
let root: Root

beforeEach(() => {
  host = document.createElement('div')
  document.body.appendChild(host)
  root = createRoot(host)
  user = { role: 'staff', permissions: ['kassa'] }
})
afterEach(() => {
  act(() => root.unmount())
  host.remove()
})

function at(path: string) {
  act(() => {
    root.render(
      <MemoryRouter initialEntries={[path]}>
        <Routes>
          <Route element={<ProtectedRoute role="admin" />}>
            <Route path="/admin/*" element={<div>admin</div>} />
          </Route>
          <Route path="/kassa" element={<div>kassa-home</div>} />
          <Route path="/kassa/topshiriqlar" element={<div>kassa-tasks</div>} />
        </Routes>
      </MemoryRouter>,
    )
  })
  return host.textContent
}

describe('ProtectedRoute — faqat kassa ruxsatli xodim', () => {
  it('admin paneliga kirsa kassa portaliga qaytariladi', () => {
    expect(at('/admin/students')).toBe('kassa-home')
  })

  it('Telegram «Topshiriqlarim» havolasi kassa portalidagi topshiriqlarga olib boradi', () => {
    expect(at('/admin/hr/mening-topshiriqlarim')).toBe('kassa-tasks')
  })

  it('boshqa xodim admin panelida qoladi', () => {
    user = { role: 'staff', permissions: ['kassa', 'students'] }
    expect(at('/admin/hr/mening-topshiriqlarim')).toBe('admin')
  })
})
