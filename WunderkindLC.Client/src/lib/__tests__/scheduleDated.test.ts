import { describe, expect, it } from 'vitest'
import type { DatedLesson, DatedRoom } from '@/api/services/schedule'
import {
  FLOOR_NONE,
  datedColumns,
  datedParamsToSearch,
  filterDatedLessons,
  floorLabel,
  groupRuns,
  isIsoDate,
  mondayOf,
  parseDatedParams,
  rangeOf,
  rangeTitle,
  serverDayOf,
  stepDate,
  toGridLesson,
  weekDates,
  weekRows,
} from '@/lib/scheduleDated'

const L = (over: Partial<DatedLesson>): DatedLesson => ({
  date: '2026-10-12', groupId: 'g1', groupName: 'Ingliz A1', courseId: 'c1', courseName: 'Ingliz tili',
  teacherId: 't1', teacherName: 'Ali', substituteTeacherId: '', substituteTeacherName: '',
  roomId: 'r1', roomName: '101', floor: 1, start: '14:00', end: '15:30', moved: false,
  members: 8, capacity: 12, attendanceTaken: false, status: 'active', ...over,
})

const rooms: DatedRoom[] = [
  { id: 'r10', name: '10-xona', floor: 2, capacity: 10 },
  { id: 'r2', name: '2-xona', floor: 2, capacity: 10 },
  { id: 'z', name: 'Zal', floor: null, capacity: 30 },
  { id: 'r1', name: '101', floor: 1, capacity: 12 },
]
const NO_FILTER = { teacher: '', group: '', room: '', course: '', floor: '', status: '' }

describe('sana yordamchilari', () => {
  it('hafta Dushanbadan Yakshanbagacha', () => {
    expect(serverDayOf('2026-10-12')).toBe(0)        // Dushanba
    expect(serverDayOf('2026-10-18')).toBe(6)        // Yakshanba
    expect(mondayOf('2026-10-18')).toBe('2026-10-12')
    expect(weekDates('2026-10-14')).toEqual([
      '2026-10-12', '2026-10-13', '2026-10-14', '2026-10-15', '2026-10-16', '2026-10-17', '2026-10-18',
    ])
    expect(rangeOf('week', '2026-10-14')).toEqual({ from: '2026-10-12', to: '2026-10-18' })
    expect(rangeOf('day', '2026-10-14')).toEqual({ from: '2026-10-14', to: '2026-10-14' })
  })

  it('‹ › — kunda 1 kun, haftada 7 kun (oy chegarasidan o\'tadi)', () => {
    expect(stepDate('day', '2026-10-31', 1)).toBe('2026-11-01')
    expect(stepDate('week', '2026-10-12', -1)).toBe('2026-10-05')
  })

  it('sarlavha va sana tekshiruvi', () => {
    expect(rangeTitle('day', '2026-10-12')).toBe('Dushanba, 12.10.2026')
    expect(rangeTitle('week', '2026-10-14')).toBe('12.10 – 18.10.2026')
    expect(isIsoDate('2026-02-30')).toBe(false)
    expect(isIsoDate('2026-02-28')).toBe(true)
  })
})

describe('manzil holati', () => {
  it("buzuq qiymat standartga tushadi, standartlar manzilga yozilmaydi", () => {
    const p = parseDatedParams(new URLSearchParams('date=buzuq&view=yil&groupBy=x&layout=list&floor=2'), '2026-10-12')
    expect(p).toMatchObject({ date: '2026-10-12', view: 'day', groupBy: 'room', layout: 'list', floor: '2' })
    expect(datedParamsToSearch(p, '2026-10-12').toString()).toBe('layout=list&floor=2')
    const w = parseDatedParams(new URLSearchParams('date=2026-10-14&view=week&groupBy=teacher&teacher=t1'), '2026-10-12')
    expect(datedParamsToSearch(w, '2026-10-12').toString()).toBe('date=2026-10-14&view=week&groupBy=teacher&teacher=t1')
  })
})

describe('filtr', () => {
  const lessons = [
    L({}),
    L({ groupId: 'g2', groupName: 'Mat', teacherId: 't9', substituteTeacherId: 't1', substituteTeacherName: 'Ali', roomId: 'z', roomName: 'Zal', floor: null }),
    L({ groupId: 'g3', groupName: 'Rus', teacherId: 't2', teacherName: 'Vali', roomId: 'r2', roomName: '2-xona', floor: 2, status: 'full' }),
  ]

  it("o'qituvchi — egasi HAM, o'rinbosari HAM bo'yicha", () => {
    expect(filterDatedLessons(lessons, { ...NO_FILTER, teacher: 't1' }).map((l) => l.groupId)).toEqual(['g1', 'g2'])
    expect(filterDatedLessons(lessons, { ...NO_FILTER, teacher: 't9' }).map((l) => l.groupId)).toEqual(['g2'])
  })

  it("qavat, xona va holat", () => {
    expect(filterDatedLessons(lessons, { ...NO_FILTER, floor: FLOOR_NONE }).map((l) => l.groupId)).toEqual(['g2'])
    expect(filterDatedLessons(lessons, { ...NO_FILTER, floor: '2' }).map((l) => l.groupId)).toEqual(['g3'])
    expect(filterDatedLessons(lessons, { ...NO_FILTER, room: 'r1' }).map((l) => l.groupId)).toEqual(['g1'])
    expect(filterDatedLessons(lessons, { ...NO_FILTER, status: 'full' }).map((l) => l.groupId)).toEqual(['g3'])
  })
})

describe("kun ustunlari — xonalar QAVAT bo'yicha", () => {
  it("qavat o'sish tartibida, qavat ichida tabiiy nom, qavatsizlar oxirida", () => {
    const cols = datedColumns('room', rooms, [], [L({})], { room: '', floor: '', teacher: '' })
    expect(cols.map((c) => c.name)).toEqual(['101', '2-xona', '10-xona', 'Zal'])
    expect(cols.map((c) => c.group)).toEqual(['1-qavat', '2-qavat', '2-qavat', "Qavat ko'rsatilmagan"])
    expect(groupRuns(cols).map((r) => [r.group, r.items.length])).toEqual([
      ['1-qavat', 1], ['2-qavat', 2], ["Qavat ko'rsatilmagan", 1],
    ])
    expect(floorLabel(-1)).toBe('-1-qavat')
  })

  it("qavat filtri ustunlarni ham toraytiradi; xonasiz dars — alohida ustun", () => {
    expect(datedColumns('room', rooms, [], [], { room: '', floor: '2', teacher: '' }).map((c) => c.key)).toEqual(['r2', 'r10'])
    const noRoom = datedColumns('room', rooms, [], [L({ roomId: '', roomName: '' })], { room: '', floor: '', teacher: '' })
    expect(noRoom.at(-1)).toEqual({ key: '', name: 'Xona belgilanmagan', group: "Qavat ko'rsatilmagan" })
  })

  it("dars to'r shakliga: dars o'tadigan o'qituvchi va belgi", () => {
    const g = toGridLesson(L({ moved: true, substituteTeacherId: 't5', substituteTeacherName: 'Sardor' }))
    expect(g.teacherId).toBe('t5')
    expect(g.days).toEqual([0])
    expect(g.minutes).toBe(90)
    expect(g.badge).toBe("ko'chirilgan · o'rinbosar: Sardor")
    expect(toGridLesson(L({ roomId: 'name:Eski', roomName: 'Eski' })).roomId).toBe('')
  })
})

describe('hafta qatorlari', () => {
  it("katakda darslar vaqt bo'yicha, darssiz qator ham qoladi", () => {
    const dates = weekDates('2026-10-12')
    const rows = weekRows('room', rooms, [], [
      L({ groupId: 'b', groupName: 'B', start: '16:00', end: '17:00' }),
      L({ groupId: 'a', groupName: 'A', start: '09:00', end: '10:00' }),
      L({ groupId: 'c', groupName: 'C', date: '2026-10-14' }),
    ], dates, { room: '', floor: '', teacher: '' })
    const r1 = rows.find((r) => r.key === 'r1')!
    expect(r1.cells['2026-10-12'].map((l) => l.groupName)).toEqual(['A', 'B'])
    expect(r1.cells['2026-10-14'].map((l) => l.groupName)).toEqual(['C'])
    expect(rows.find((r) => r.key === 'z')!.cells['2026-10-12']).toEqual([])
  })

  it("o'qituvchi rejimida dars o'tadigan odam qatorida", () => {
    const rows = weekRows('teacher', [], [{ id: 't1', name: 'Ali' }, { id: 't9', name: 'Bek' }], [
      L({ teacherId: 't9', substituteTeacherId: 't1', substituteTeacherName: 'Ali' }),
    ], ['2026-10-12'], { room: '', floor: '', teacher: '' })
    expect(rows.find((r) => r.key === 't1')!.cells['2026-10-12']).toHaveLength(1)
    expect(rows.find((r) => r.key === 't9')!.cells['2026-10-12']).toHaveLength(0)
  })
})
