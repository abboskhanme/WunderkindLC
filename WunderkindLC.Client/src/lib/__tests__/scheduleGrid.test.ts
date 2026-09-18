import { describe, expect, it } from 'vitest'
import type { ScheduleGridGroup } from '@/api/services/schedule'
import {
  DEFAULT_FIELD_SETTINGS,
  assignLanes,
  dateOfServerDay,
  daysLabel,
  fieldValue,
  gridColumns,
  gridFilterOf,
  gridRange,
  homeParamsToSearch,
  jsDayToServer,
  lessonColor,
  lessonsOfDay,
  naturalCompare,
  parseFieldSettings,
  parseHomeParams,
  roomKey,
  serverDayToJs,
  textOn,
  timeSlots,
  toMinutes,
  type GridFilter,
} from '../scheduleGrid'

function g(over: Partial<ScheduleGridGroup> = {}): ScheduleGridGroup {
  return {
    groupId: 'g1',
    groupName: 'Guruh 1',
    courseId: 'eng',
    courseName: 'Ingliz tili',
    teacherId: 't1',
    teacherName: 'Aliyev',
    roomId: 'r1',
    roomName: '1-xona',
    days: [0, 2, 4],
    start: '09:00',
    end: '10:00',
    minutes: 60,
    members: 5,
    capacity: 10,
    status: 'active',
    startDate: '',
    endDate: '',
    lessonsDone: 0,
    lessonsTotal: 0,
    ...over,
  }
}

const F: GridFilter = {
  day: 4,
  fromMin: 8 * 60,
  toMin: 22 * 60,
  teacherId: '',
  groupId: '',
  roomId: '',
  courseId: '',
  status: '',
}

describe('kun raqamlari', () => {
  it('JS (0=Yakshanba) ↔ server (0=Dushanba)', () => {
    expect(jsDayToServer(0)).toBe(6) // Yakshanba
    expect(jsDayToServer(1)).toBe(0) // Dushanba
    expect(jsDayToServer(5)).toBe(4) // Juma
    for (let d = 0; d < 7; d++) expect(serverDayToJs(jsDayToServer(d))).toBe(d)
  })
})

describe('daysLabel — server ScheduleGridExport.DaysLabel bilan bir xil', () => {
  it.each([
    [[0, 2, 4], 'Toq kunlar'],
    [[4, 0, 2], 'Toq kunlar'],
    [[1, 3, 5], 'Juft kunlar'],
    [[0, 1, 2, 3, 4, 5], 'Har kuni'],
    [[0, 1, 2, 3, 4, 5, 6], 'Har kuni'],
    [[0, 5, 6], 'Du, Sha, Yak'],
    [[], ''],
  ])('%j → %s', (days, label) => {
    expect(daysLabel(days)).toBe(label)
  })
})

describe('vaqt', () => {
  it('buzuq vaqt null', () => {
    expect(toMinutes('09:30')).toBe(570)
    expect(toMinutes('24:00')).toBeNull()
    expect(toMinutes('abc')).toBeNull()
    expect(toMinutes('')).toBeNull()
  })

  it('yarim soatlik qatorlar', () => {
    expect(timeSlots(7 * 60, 8 * 60)).toEqual([
      { start: 420, label: '07:00 - 07:30' },
      { start: 450, label: '07:30 - 08:00' },
    ])
  })
})

describe('lessonsOfDay', () => {
  const groups = [
    g({ groupId: 'a' }),
    g({ groupId: 'b', start: '07:00', end: '08:00' }), // 08:00 da tugaydi — oraliqqa tegmaydi
    g({ groupId: 'c', days: [3] }),
    g({ groupId: 'd', start: '07:30', end: '09:00', courseId: 'math' }),
    g({ groupId: 'e', start: '12:00', end: '13:00', status: 'blocked', roomId: '', roomName: 'Eski' }),
  ]
  it('kun va vaqt oralig\'i', () => {
    expect(lessonsOfDay(groups, F).map((x) => x.groupId)).toEqual(['d', 'a', 'e'])
  })
  it('filtrlar', () => {
    expect(lessonsOfDay(groups, { ...F, courseId: 'math' }).map((x) => x.groupId)).toEqual(['d'])
    expect(lessonsOfDay(groups, { ...F, status: 'blocked' }).map((x) => x.groupId)).toEqual(['e'])
    expect(lessonsOfDay(groups, { ...F, roomId: 'name:Eski' }).map((x) => x.groupId)).toEqual(['e'])
    expect(lessonsOfDay(groups, { ...F, groupId: 'a' }).map((x) => x.groupId)).toEqual(['a'])
  })
})

describe('gridRange', () => {
  it('oraliqdan chiqqan dars uchun kengayadi va yarim soatga yaxlitlanadi', () => {
    expect(gridRange([], F)).toEqual({ from: 480, to: 1320 })
    expect(gridRange([g({ start: '07:15', end: '09:00' }), g({ start: '21:00', end: '22:10' })], F)).toEqual({
      from: 420,
      to: 1350,
    })
  })
})

describe('gridColumns', () => {
  const rooms = [
    { id: 'r1', name: '1-xona', groupCount: 1 },
    { id: 'r2', name: '2-xona', groupCount: 0 },
  ]
  const teachers = [
    { id: 't1', name: 'Aliyev', groupCount: 1 },
    { id: 't2', name: 'Valiyev', groupCount: 0 },
  ]
  const lessons = [g(), g({ groupId: 'x', roomId: '', roomName: '', teacherId: '' })]

  it('bo\'sh xonalar ham va xonasiz dars uchun ustun', () => {
    expect(gridColumns('room', rooms, teachers, lessons, F).map((c) => c.key)).toEqual(['r1', 'r2', ''])
    expect(gridColumns('teacher', rooms, teachers, lessons, F).map((c) => c.key)).toEqual(['t1', 't2', ''])
    expect(gridColumns('room', rooms, teachers, lessons, { ...F, roomId: 'r2' }).map((c) => c.key)).toEqual(['r2'])
  })

  it('roomKey', () => {
    expect(roomKey('r1', '1-xona')).toBe('r1')
    expect(roomKey('', 'Eski')).toBe('name:Eski')
    expect(roomKey('', '')).toBe('')
  })

  it('tabiiy tartib', () => {
    const names = ['1-bino 10-xona', '1-bino 2-xona', '2-bino 1-xona', '1-bino 1-xona']
    expect([...names].sort(naturalCompare)).toEqual(['1-bino 1-xona', '1-bino 2-xona', '1-bino 10-xona', '2-bino 1-xona'])
  })
})

describe('assignLanes', () => {
  it('ustma-ust darslar yo\'laklarga bo\'linadi, alohidalari to\'liq kenglikda', () => {
    const res = assignLanes([
      { id: 'a', start: '09:00', end: '10:30' },
      { id: 'b', start: '10:00', end: '11:00' },
      { id: 'c', start: '10:30', end: '11:30' },
      { id: 'd', start: '12:00', end: '13:00' },
    ])
    const by = Object.fromEntries(res.map((r) => [r.item.id, [r.lane, r.lanes]]))
    expect(by.a).toEqual([0, 2])
    expect(by.b).toEqual([1, 2])
    expect(by.c).toEqual([0, 2]) // a tugagach bo'shagan yo'lak
    expect(by.d).toEqual([0, 1])
  })
})

describe('ranglar', () => {
  it('bir kurs — bir rang, kurssiz — guruh bo\'yicha', () => {
    expect(lessonColor({ courseId: 'eng', groupId: 'a' })).toBe(lessonColor({ courseId: 'eng', groupId: 'b' }))
    expect(lessonColor({ courseId: '', groupId: 'a' })).toMatch(/^#[0-9A-F]{6}$/i)
  })
  it('matn rangi kontrast bo\'yicha', () => {
    expect(textOn('#F4A259')).toBe('#111827') // to'q sariq — to'q matn
    expect(textOn('#C9474B')).toBe('#FFFFFF') // qizil — oq matn
  })
})

describe('blok maydonlari', () => {
  it('saqlangan sozlama: noma\'lum qiymat standartga tushadi', () => {
    expect(parseFieldSettings(null)).toEqual(DEFAULT_FIELD_SETTINGS)
    expect(parseFieldSettings('{bad json')).toEqual(DEFAULT_FIELD_SETTINGS)
    const s = parseFieldSettings(JSON.stringify({ title1: 'group', row1: 'hacker', footer: 'none' }))
    expect(s.title1).toBe('group')
    expect(s.row1).toBe(DEFAULT_FIELD_SETTINGS.row1)
    expect(s.footer).toBe('none')
  })

  it('darslar soni: jami noma\'lum bo\'lsa o\'quvchilar soniga tushadi', () => {
    expect(fieldValue(g({ lessonsDone: 7, lessonsTotal: 52 }), 'lessons')).toEqual({ text: '7/52', icon: 'book' })
    expect(fieldValue(g(), 'lessons')).toEqual({ text: '5/10', icon: 'users' })
    expect(fieldValue(g({ capacity: 0 }), 'members')).toEqual({ text: '5', icon: 'users' })
    expect(fieldValue(g(), 'room')).toEqual({ text: 'Xona: 1-xona' })
    expect(fieldValue(g({ teacherName: '' }), 'teacher')).toBeNull()
    expect(fieldValue(g(), 'none')).toBeNull()
  })

  it('tanlangan kunning joriy haftadagi sanasi', () => {
    const friday = new Date(2026, 8, 18) // 2026-09-18, Juma
    expect(dateOfServerDay(0, friday).getDate()).toBe(14) // Dushanba
    expect(dateOfServerDay(6, friday).getDate()).toBe(20) // Yakshanba
  })
})

describe('manzil holati', () => {
  it('standart qiymatlar va buzuq parametrlar', () => {
    const p = parseHomeParams(new URLSearchParams('day=9&groupBy=x&fromHour=25:00&toHour=abc'), 5)
    expect(p).toMatchObject({ day: 5, groupBy: 'room', view: 'grid', fromHour: '08:00', toHour: '22:00' })

    const q = parseHomeParams(new URLSearchParams('day=0&groupBy=teacher&view=list&fromHour=09:00&toHour=08:00&room=r1'), 5)
    expect(q).toMatchObject({ day: 0, groupBy: 'teacher', view: 'list', fromHour: '08:00', toHour: '22:00', room: 'r1' })
  })

  it('manzilga yozish — edutizim ko\'rinishida, bo\'sh filtrlarsiz', () => {
    const p = parseHomeParams(new URLSearchParams(''), 5)
    expect(homeParamsToSearch(p).toString()).toBe('fromHour=08%3A00&toHour=22%3A00&day=5&dayName=Juma&groupBy=room')
    expect(gridFilterOf(p)).toMatchObject({ day: 4, fromMin: 480, toMin: 1320 })
  })
})
