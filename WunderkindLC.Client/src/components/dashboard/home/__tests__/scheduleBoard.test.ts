// @vitest-environment jsdom
/**
 * Bosh sahifa "Dars jadvali" kartasi — chizish tutuni (smoke): to'r/ro'yxat × xona/o'qituvchi
 * rejimlari yiqilmasdan chiziladi, tooltip, sozlamalar drawer'i va katta holat ishlaydi.
 * `localStorage` ATAYIN "bloklangan" (inkognito/xatcho'p) — sahifa baribir ishlashi shart.
 */
import { act, createElement as h } from 'react'
import { createRoot } from 'react-dom/client'
import { MemoryRouter } from 'react-router-dom'
import { expect, it, vi } from 'vitest'
import { ScheduleBoard } from '@/components/dashboard/home/ScheduleBoard'
import { HomeFilterRow } from '@/components/dashboard/home/HomeFilterRow'
import { parseHomeParams, gridFilterOf } from '@/lib/scheduleGrid'
import type { ScheduleGrid } from '@/api/services/schedule'

;(globalThis as unknown as { IS_REACT_ACT_ENVIRONMENT: boolean }).IS_REACT_ACT_ENVIRONMENT = true

const grid: ScheduleGrid = {
  groups: [
    { groupId: 'g1', groupName: 'Nazokatxon teacher | Beginner', courseId: 'eng', courseName: 'Ingliz tili', teacherId: 't1', teacherName: 'Nazokatxon Sobirova', roomId: 'r1', roomName: '1-bino 5', days: [0, 2, 4], start: '08:00', end: '10:00', minutes: 120, members: 7, capacity: 12, status: 'active', startDate: '2026-09-01', endDate: '2026-12-31', lessonsDone: 7, lessonsTotal: 52 },
    { groupId: 'g2', groupName: 'Overlap', courseId: 'math', courseName: 'Matematika', teacherId: 't2', teacherName: 'Sevinchxon', roomId: 'r1', roomName: '1-bino 5', days: [4], start: '09:00', end: '11:00', minutes: 120, members: 3, capacity: 0, status: 'full', startDate: '', endDate: '', lessonsDone: 0, lessonsTotal: 0 },
    { groupId: 'g3', groupName: 'Early', courseId: 'math', courseName: 'Matematika', teacherId: 't2', teacherName: 'Sevinchxon', roomId: '', roomName: '', days: [4], start: '07:00', end: '09:00', minutes: 120, members: 3, capacity: 0, status: 'active', startDate: '', endDate: '', lessonsDone: 0, lessonsTotal: 0 },
  ],
  rooms: [{ id: 'r1', name: '1-bino 5', groupCount: 2 }, { id: 'r2', name: '1-bino 6', groupCount: 0 }],
  teachers: [{ id: 't1', name: 'Nazokatxon Sobirova', groupCount: 1 }, { id: 't2', name: 'Sevinchxon', groupCount: 2 }],
  skippedGroups: 2,
}

it("to'r, ro'yxat, o'qituvchi rejimi va filtr qatori yiqilmasdan chiziladi", async () => {
  vi.stubGlobal('localStorage', { getItem: () => { throw new Error('blocked') }, setItem: () => { throw new Error('blocked') } })
  const el = document.createElement('div')
  document.body.appendChild(el)
  const root = createRoot(el)
  const params = parseHomeParams(new URLSearchParams('day=5'), 5)
  const noop = () => {}
  for (const [groupBy, view] of [['room', 'grid'], ['room', 'list'], ['teacher', 'grid'], ['teacher', 'list']] as const) {
    await act(async () => {
      root.render(
        h(MemoryRouter, null,
          h(HomeFilterRow, { grid, params, onChange: noop }),
          h(ScheduleBoard, { grid, loading: false, error: null, jsDay: 5, onDay: noop, groupBy, onGroupBy: noop, view, onView: noop, filter: gridFilterOf(params), canOpenGroup: true }),
        ),
      )
    })
    const text = el.textContent ?? ''
    expect(text).toContain('07:00 - 07:30')
    expect(text).toContain('Ingliz tili')
    expect(text).toContain('Toq kunlar')
    expect(text).toContain('7/52')
    expect(text).toContain("2 ta guruhning")
    if (groupBy === 'room') expect(text).toContain('Xona belgilanmagan')
  }
  // Tooltip — blok ustiga kelganda
  const link = el.querySelector('a[href="/admin/classes/g1"]')!
  await act(async () => { link.dispatchEvent(new MouseEvent('mouseover', { bubbles: true })) })
  expect(document.body.textContent).toContain('Boshlangan vaqti')
  // Sozlamalar drawer'i
  const gear = el.querySelector('button[title="Jadval ko\'rinishi sozlamalari"]') as HTMLButtonElement
  await act(async () => { gear.click() })
  expect(document.body.textContent).toContain('Har bir qator uchun maydon tanlang')
  const save = [...document.querySelectorAll('button')].find((b) => b.textContent === 'Saqlash')!
  await act(async () => { save.click() })
  expect(document.body.textContent).not.toContain('Har bir qator uchun maydon tanlang')
  // Katta holat
  const max = el.querySelector('button[title="Katta xolatda ko\'rish"]') as HTMLButtonElement
  await act(async () => { max.click() })
  expect(el.querySelector('section')!.className).toContain('fixed')
  await act(async () => root.unmount())
  vi.unstubAllGlobals()
})
