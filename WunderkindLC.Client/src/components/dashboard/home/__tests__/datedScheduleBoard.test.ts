// @vitest-environment jsdom
/**
 * Sanali «Dars jadvali» (CRM Phase 5) va «Vidjetlar» oynasi — chizish tutuni (smoke): Kun (to'r/ro'yxat,
 * qavat sarlavhalari) va Hafta ko'rinishlari, ko'chirilgan/o'rinbosar belgilari, ‹ › navigatsiya, vidjetlar
 * oynasida ▲/▼ va saqlash. `localStorage` ATAYIN bloklangan — sahifa baribir ishlashi shart.
 */
import { act, createElement as h } from 'react'
import { createRoot } from 'react-dom/client'
import { MemoryRouter } from 'react-router-dom'
import { expect, it, vi } from 'vitest'
import type { DatedSchedule } from '@/api/services/schedule'
import { DatedScheduleBoard } from '@/components/dashboard/home/DatedScheduleBoard'
import { DatedFilterRow } from '@/components/dashboard/home/DatedFilterRow'
import { HomeWidgetsDrawer } from '@/components/dashboard/home/HomeWidgetsDrawer'
import { defaultHomeLayout, type HomeLayout } from '@/lib/homeWidgets'
import { parseDatedParams, type DatedParams } from '@/lib/scheduleDated'

;(globalThis as unknown as { IS_REACT_ACT_ENVIRONMENT: boolean }).IS_REACT_ACT_ENVIRONMENT = true

const data: DatedSchedule = {
  from: '2026-10-12',
  to: '2026-10-18',
  rooms: [
    { id: 'r1', name: '101', floor: 1, capacity: 12 },
    { id: 'r2', name: '201', floor: 2, capacity: 12 },
  ],
  teachers: [{ id: 't1', name: 'Ali' }, { id: 't2', name: 'Vali' }],
  lessons: [
    { date: '2026-10-12', groupId: 'g1', groupName: 'Ingliz A1', courseId: 'c1', courseName: 'Ingliz tili', teacherId: 't1', teacherName: 'Ali', substituteTeacherId: '', substituteTeacherName: '', roomId: 'r1', roomName: '101', floor: 1, start: '14:00', end: '15:30', moved: true, members: 7, capacity: 12, attendanceTaken: false, status: 'active' },
    { date: '2026-10-14', groupId: 'g2', groupName: 'Mat B', courseId: 'c2', courseName: 'Matematika', teacherId: 't2', teacherName: 'Vali', substituteTeacherId: 't1', substituteTeacherName: 'Ali', roomId: 'r2', roomName: '201', floor: 2, start: '09:00', end: '10:00', moved: false, members: 5, capacity: 0, attendanceTaken: true, status: 'active' },
  ],
  skippedGroups: 1,
}

it("Kun va Hafta ko'rinishlari, qavat sarlavhalari va belgilar chiziladi", async () => {
  vi.stubGlobal('localStorage', { getItem: () => { throw new Error('blocked') }, setItem: () => { throw new Error('blocked') } })
  const el = document.createElement('div')
  document.body.appendChild(el)
  const root = createRoot(el)
  const onChange = vi.fn()
  const render = async (query: string) => {
    const params: DatedParams = parseDatedParams(new URLSearchParams(query), '2026-10-12')
    await act(async () => {
      root.render(
        h(MemoryRouter, null,
          h(DatedFilterRow, { data, params, onChange: () => {} }),
          h(DatedScheduleBoard, { data, loading: false, error: null, params, onChange, today: '2026-10-12', canOpenGroup: true }),
        ),
      )
    })
    return el.textContent ?? ''
  }

  let text = await render('date=2026-10-12')
  expect(text).toContain('Dushanba, 12.10.2026')
  expect(text).toContain('1-qavat')
  expect(text).toContain('2-qavat')
  expect(text).toContain("ko'chirilgan")
  expect(text).toContain('1 ta guruhning')
  expect(text).toContain('Qavat')

  text = await render('date=2026-10-12&layout=list')
  expect(text).toContain('1-qavat')
  expect(text).toContain('Ingliz tili')

  text = await render('date=2026-10-12&view=week')
  expect(text).toContain('12.10 – 18.10.2026')
  expect(text).toContain('14:00 Ingliz A1')
  expect(text).toContain("o'rinbosar: Ali")
  expect(el.querySelector('a[href="/admin/classes/g2"]')).not.toBeNull()

  text = await render('date=2026-10-12&view=week&groupBy=teacher&teacher=t1')
  expect(text).toContain('09:00 Mat B')   // o'rinbosar bo'lgan darsi o'z qatorida

  const next = el.querySelector('button[title="Keyingi hafta"]') as HTMLButtonElement
  await act(async () => { next.click() })
  expect(onChange).toHaveBeenCalledWith({ date: '2026-10-19' })

  await act(async () => root.unmount())
  vi.unstubAllGlobals()
})

it("Vidjetlar oynasi: ▲/▼, yashirish va saqlash", async () => {
  const el = document.createElement('div')
  document.body.appendChild(el)
  const root = createRoot(el)
  let saved: HomeLayout | null = null
  await act(async () => {
    root.render(
      h(HomeWidgetsDrawer, {
        initial: defaultHomeLayout(),
        access: { can: () => true, isSuperAdmin: true },
        onSave: async (l: HomeLayout) => { saved = l },
        onReset: async () => {},
        onClose: () => {},
      }),
    )
  })
  expect(document.body.textContent).toContain('Kartochkalar')
  expect(document.body.textContent).toContain('Mening topshiriqlarim')
  expect(document.body.textContent).not.toContain("Qizil ro'yxat")   // keyingi bosqich — hali yo'q

  const downs = [...document.querySelectorAll('button[aria-label="Pastga"]')] as HTMLButtonElement[]
  // Bloklar ro'yxatining birinchisi (Kassalar) pastga — Dars jadvali bilan o'rin almashadi.
  const firstBlockDown = downs[12]
  await act(async () => { firstBlockDown.click() })
  const save = [...document.querySelectorAll('button')].find((b) => b.textContent === 'Saqlash')!
  await act(async () => { save.click() })
  expect(saved!.blocks.slice(0, 2).map((b) => b.key)).toEqual(['schedule', 'cashboxes'])
  await act(async () => root.unmount())
})
