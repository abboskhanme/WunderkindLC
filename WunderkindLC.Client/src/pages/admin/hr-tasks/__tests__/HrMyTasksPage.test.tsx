// @vitest-environment jsdom
import { act } from 'react'
import { createRoot, type Root } from 'react-dom/client'
import { MemoryRouter } from 'react-router-dom'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import type { HrMyTask } from '@/api/services/hrTasks'

/**
 * «Mening topshiriqlarim» (docs/SPEC-hr-tasks.md) — LMS sahifasi + Mini App'ning kun ko'rinishi. API soxta:
 * sahifa server javobini to'g'ri chizadimi, HR qatori yo'q xodimga tushuntirish chiqadimi.
 */
const api = vi.hoisted(() => ({
  getMe: vi.fn(),
  getMySummary: vi.fn(),
  getMyTasks: vi.fn(),
  getMyDayTasks: vi.fn(),
}))

vi.mock('@/api/services/hrTasks', async (importOriginal) => {
  const real = await importOriginal<typeof import('@/api/services/hrTasks')>()
  return { ...real, ...api }
})

import { HrMyTasksPage } from '../HrMyTasksPage'

;(globalThis as { IS_REACT_ACT_ENVIRONMENT?: boolean }).IS_REACT_ACT_ENVIRONMENT = true

let host: HTMLDivElement
let root: Root

beforeEach(() => {
  host = document.createElement('div')
  document.body.appendChild(host)
  root = createRoot(host)
  for (const fn of Object.values(api)) fn.mockReset()
  api.getMySummary.mockResolvedValue({ periodStart: '2026-10-01', periodEnd: '2026-10-31', total: 0, done: 0, missed: 0, late: 0, open: 0, percent: 0, kpi: [] })
})
afterEach(() => {
  act(() => root.unmount())
  host.remove()
})

function task(patch: Partial<HrMyTask>): HrMyTask {
  return {
    id: 't1', title: 'Hisobot', description: null, kind: 'check', status: 'open', source: 'director',
    dueAt: '2026-10-21T13:00:00Z', isOverdue: false, isLate: false, targetAmount: null, currentAmount: null, unit: null,
    employeeComment: null, directorComment: null, completedAt: null, acceptedAt: null, needsAcceptance: false,
    voiceUrl: null, attachments: [], proofs: [], ...patch,
  }
}

async function render(defaultTab?: 'open' | 'day') {
  await act(async () => {
    root.render(
      <MemoryRouter>
        <HrMyTasksPage defaultTab={defaultTab} />
      </MemoryRouter>,
    )
  })
  // useLoad: me → (list | day) — two rounds of promises.
  await act(async () => {
    await Promise.resolve()
  })
}

describe('HrMyTasksPage', () => {
  it('kun ko\'rinishi: server "bugun"i, muddati o\'tgan ochiq tepada, tugmalar', async () => {
    api.getMe.mockResolvedValue({ employeeId: 'e1', fullName: 'Ali Valiyev', positionName: 'Kassir', timezone: 'Asia/Tashkent', today: '2026-10-21' })
    api.getMyDayTasks.mockResolvedValue({
      date: '2026-10-21',
      items: [
        task({ id: 'a', title: 'Kassani topshirish', status: 'done', completedAt: '2026-10-21T10:00:00Z' }),
        task({ id: 'b', title: 'Qarzdorlarga qo‘ng‘iroq', isOverdue: true, needsAcceptance: true }),
      ],
    })
    await render('day')

    expect(api.getMyDayTasks).toHaveBeenCalledWith('2026-10-21')
    expect(api.getMyTasks).not.toHaveBeenCalled()
    const text = host.textContent ?? ''
    expect(text).toContain('Bugun')
    expect(text).toContain('Bugun: 2 dan 1 tasi bajarildi')
    const titles = [...host.querySelectorAll('li p.font-medium')].map((p) => p.textContent)
    expect(titles[0]).toBe('Qarzdorlarga qo‘ng‘iroq')
    expect(text).toContain('Bajardim')
    expect(text).toContain('Tasdiqlash')
    // Bugun bajarilgan — qayta ochsa bo'ladi.
    expect(text).toContain('Qayta ochish')
  })

  it('ro\'yxat ko\'rinishi (admin paneli) — sukut «Bajarish kerak»', async () => {
    api.getMe.mockResolvedValue({ employeeId: 'e1', fullName: 'Ali', positionName: null, timezone: 'Asia/Tashkent', today: '2026-10-21' })
    api.getMyTasks.mockResolvedValue({ items: [task({ title: 'Oylik hisobot' })], nextCursor: null, hasMore: false, total: 1 })
    await render()

    expect(api.getMyTasks).toHaveBeenCalledWith(expect.objectContaining({ status: ['open', 'returned'], sort: 'due_at' }))
    expect(api.getMyDayTasks).not.toHaveBeenCalled()
    expect(host.textContent).toContain('Oylik hisobot')
    expect(host.textContent).toContain('Jami 1 ta topshiriq')
  })

  it('HR qatori yo\'q foydalanuvchi — xato emas, tushuntirish', async () => {
    api.getMe.mockRejectedValue({ response: { status: 403, data: { code: 'hr_not_employee', message: 'Siz ro‘yxatda yo‘qsiz' } } })
    await render()

    expect(host.textContent).toContain('Siz HR xodimlari ro‘yxatida yo‘qsiz')
    expect(api.getMyTasks).not.toHaveBeenCalled()
  })
})
