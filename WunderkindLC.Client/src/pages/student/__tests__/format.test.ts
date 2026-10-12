import { describe, expect, it } from 'vitest'
import type { StudentGroupItem } from '@/api/services/studentPortal'
import {
  balanceState,
  dayLabel,
  greeting,
  isAutoNote,
  monthStatus,
  nextLesson,
  paymentView,
  percentTone,
  som,
  todayLine,
} from '../format'

/** Student cabinet presentation helpers — the numbers/labels a parent reads first. */

const group = (over: Partial<StudentGroupItem> = {}): StudentGroupItem => ({
  groupId: 'g1',
  name: 'Diyorbek teacher | Foundation (oxiri)',
  courseName: 'Ingliz tili | Foundation',
  teacherName: 'Diyorbek Zokirov',
  days: [0, 2, 4], // Du, Ch, Ju
  startTime: '13:30',
  endTime: '15:30',
  room: '2-bino 8-xona',
  state: 'active',
  status: 'active',
  isActive: true,
  groupArchived: false,
  joinedAt: '2026-09-30',
  leftAt: '',
  ...over,
})

// 2026-10-12 is a Monday.
const at = (hhmm: string, date = '2026-10-12') => new Date(`${date}T${hhmm}:00`)

describe('nextLesson', () => {
  it("bugungi dars boshlanmagan bo'lsa — bugun", () => {
    const n = nextLesson([group()], at('09:00'))
    expect(n).toMatchObject({ dayOffset: 0, day: 0, status: 'today' })
  })

  it('dars ketayotgan paytda — "hozir"', () => {
    expect(nextLesson([group()], at('14:00'))?.status).toBe('now')
  })

  it('bugungi dars tugagan — keyingi dars kuni (Chorshanba)', () => {
    const n = nextLesson([group()], at('16:00'))
    expect(n).toMatchObject({ dayOffset: 2, day: 2, status: 'later' })
    expect(dayLabel(n!.dayOffset, n!.day)).toBe('Chorshanba')
  })

  it("muzlatilgan/tugagan guruh va vaqtsiz guruh jadvalga kirmaydi", () => {
    expect(nextLesson([group({ state: 'frozen' })], at('09:00'))).toBeNull()
    expect(nextLesson([group({ state: 'finished' })], at('09:00'))).toBeNull()
    expect(nextLesson([group({ startTime: '' })], at('09:00'))).toBeNull()
  })

  it('bir kunda ikki dars — eng erta boshlanadigani', () => {
    const late = group({ groupId: 'late', startTime: '17:00', endTime: '18:00' })
    const early = group({ groupId: 'early', startTime: '10:00', endTime: '11:00' })
    expect(nextLesson([late, early], at('08:00'))?.group.groupId).toBe('early')
  })

  it('ertangi dars — "Ertaga"', () => {
    const n = nextLesson([group({ days: [1] })], at('20:00'))
    expect(n?.dayOffset).toBe(1)
    expect(dayLabel(1, 1)).toBe('Ertaga')
  })
})

describe('paymentView', () => {
  it("server izohi xom ko'rinmaydi — turi, oyi, kursi va usuli", () => {
    const v = paymentView({
      date: '2026-10-12',
      amount: 195000,
      note: "O'quvchi to'lovi (2026-09) [Diyorbek teacher | Foundation (oxiri)] — Aliyev Umidjon",
      month: '2026-09',
      comment: null,
      method: 'cash',
      groupName: 'Diyorbek teacher | Foundation (oxiri)',
      courseName: 'Ingliz tili | Foundation',
    })
    expect(v.title).toBe("O'qish to'lovi")
    expect(v.sub).toBe('Sentabr 2026 uchun · Ingliz tili | Foundation · Naqd')
    expect(v.note).toBe('')
  })

  it("kassir izohi (comment) ko'rsatiladi; avtomatik bo'lmagan izoh ham", () => {
    expect(paymentView({ date: '2026-10-01', amount: 1, comment: 'Chegirma bilan' }).note).toBe('Chegirma bilan')
    expect(paymentView({ date: '2026-10-01', amount: 1, note: 'Naqd, onasi olib keldi' }).note).toBe('Naqd, onasi olib keldi')
  })

  it("oyi yo'q / buzuq eski yozuv — faqat tur", () => {
    expect(paymentView({ date: '2026-10-01', amount: 1, month: '2026-9' }).sub).toBe('')
  })

  it('avtomatik izohni turli apostroflar bilan ham taniydi', () => {
    expect(isAutoNote("O'quvchi to'lovi (2026-09) — X")).toBe(true)
    expect(isAutoNote('Oʻquvchi toʻlovi (2026-09)')).toBe(true)
    expect(isAutoNote('Boshqa izoh')).toBe(false)
  })
})

describe('balance & month status', () => {
  it('qarz / avans / nol', () => {
    expect(balanceState(-168000)).toEqual({ tone: 'bad', label: "To'lanishi kerak", amount: 168000 })
    expect(balanceState(50000)).toMatchObject({ tone: 'ok', amount: 50000 })
    expect(balanceState(0)).toMatchObject({ tone: 'ok', label: "Qarz yo'q" })
  })

  it("oy holati: to'langan / qisman (qoldiq bilan) / to'lanmagan", () => {
    expect(monthStatus({ status: 'paid', remaining: 0 })).toEqual({ label: "To'langan", tone: 'ok' })
    expect(monthStatus({ status: 'partial', remaining: 168000 })).toEqual({ label: '168 000 qoldi', tone: 'warn' })
    expect(monthStatus({ status: 'unpaid', remaining: 335000 }).tone).toBe('bad')
  })

  it("so'm — bo'shliq bilan guruhlangan", () => {
    expect(som(1250000)).toBe("1 250 000 so'm")
  })
})

describe('labels', () => {
  it('sana qatori va salom (ota-ona ham, o\'quvchi ham uchun neytral)', () => {
    expect(todayLine(at('09:00'))).toBe('12-oktabr, Dushanba')
    expect(greeting(at('08:00'))).toBe('Xayrli tong')
    expect(greeting(at('13:00'))).toBe('Xayrli kun')
    expect(greeting(at('20:00'))).toBe('Xayrli kech')
  })

  it('foiz rangi chegaralari', () => {
    expect(percentTone(null)).toBe('neutral')
    expect(percentTone(90)).toBe('ok')
    expect(percentTone(70)).toBe('info')
    expect(percentTone(45)).toBe('warn')
    expect(percentTone(10)).toBe('bad')
  })
})
