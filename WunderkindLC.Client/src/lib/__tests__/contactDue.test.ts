import { describe, expect, it } from 'vitest'
import {
  bucketOf, isTodo, requestKind, stageColumns, stageKeyOf, DUE_COLUMNS, STATUS_COLUMNS,
} from '../contactDue'
import type { ContactStage } from '@/api/services/contactStages'
import type { ContactRequestItem } from '@/api/services/contacts'

/**
 * Bu testlar klientdagi `bucketOf` serverdagi `ContactService.BucketOf` bilan bir xil qolishini
 * qulflaydi (`WunderkindLC.Tests/ContactServiceTests.cs` dagi holatlar bilan bir xil).
 */
const TODAY = '2026-09-02'

describe('bucketOf — muddat guruhlari', () => {
  it("yakuniy talab navbatda YO'Q", () => {
    expect(bucketOf('done', '', TODAY)).toBe('')
    expect(bucketOf('failed', '2026-09-10', TODAY)).toBe('')
  })

  it("«Bog'lanish kerak» — har doim sanasiz", () => {
    expect(bucketOf('new', '', TODAY)).toBe('nodate')
    // Sana bo'lsa ham: `new` da sana ma'noga ega emas.
    expect(bucketOf('new', '2026-12-01', TODAY)).toBe('nodate')
  })

  it("sanasiz qayta qo'ng'iroq YO'QOLMAYDI — sanasizlarga tushadi", () => {
    expect(bucketOf('callback', '', TODAY)).toBe('nodate')
    expect(bucketOf('callback', '   ', TODAY)).toBe('nodate')
  })

  it('kecha va undan oldingilar — muddati o‘tgan', () => {
    expect(bucketOf('callback', '2026-09-01', TODAY)).toBe('overdue')
    expect(bucketOf('callback', '2025-01-01', TODAY)).toBe('overdue')
  })

  it('bugun / ertaga', () => {
    expect(bucketOf('callback', TODAY, TODAY)).toBe('today')
    expect(bucketOf('callback', '2026-09-03', TODAY)).toBe('tomorrow')
  })

  it('shu hafta = +2..+7 kun, undan keyingisi — keyinroq', () => {
    expect(bucketOf('callback', '2026-09-04', TODAY)).toBe('week') // +2
    expect(bucketOf('callback', '2026-09-09', TODAY)).toBe('week') // +7
    expect(bucketOf('callback', '2026-09-10', TODAY)).toBe('later') // +8
  })

  it('buzuq sana JIMGINA yo‘qolmaydi — navbatda ko‘rinaveradi', () => {
    // Kelajakka o'xshab turgan buzuq sana parse qilinmaydi → «keyinroq».
    expect(bucketOf('callback', '2026-13-99', TODAY)).toBe('later')
    // ⚠️ Leksikografik solishtiruv parse'dan OLDIN bo'ladi (server ham `CompareOrdinal` bilan
    // shunday qiladi), shuning uchun bugundan "kichik" buzuq sana muddati o'tganlarga tushadi —
    // ikkala holatda ham talab navbatdan YO'QOLMAYDI, muhimi shu.
    expect(bucketOf('callback', '2026-02-31', TODAY)).toBe('overdue')
  })
})

describe('isTodo — «bugun qilish kerak»', () => {
  it("kechikkanlar ham, sanasizlar ham KIRADI", () => {
    expect(isTodo('overdue')).toBe(true)
    expect(isTodo('today')).toBe(true)
    expect(isTodo('nodate')).toBe(true)
  })

  it('kelajakdagilar kirmaydi', () => {
    expect(isTodo('tomorrow')).toBe(false)
    expect(isTodo('week')).toBe(false)
    expect(isTodo('later')).toBe(false)
    expect(isTodo('')).toBe(false)
  })
})

describe('taxta ustunlari', () => {
  it("MUDDAT ustunlari `bucketOf` qaytaradigan BARCHA guruhlarni qamraydi", () => {
    const keys = DUE_COLUMNS.map((c) => c.key).sort()
    expect(keys).toEqual(['later', 'nodate', 'overdue', 'today', 'tomorrow', 'week'])
  })

  it("birinchi UCH ustun = «bugun qilish kerak» (tartib operator ustuvorligi bo'yicha)", () => {
    expect(DUE_COLUMNS.slice(0, 3).map((c) => c.key)).toEqual(['overdue', 'today', 'nodate'])
    expect(DUE_COLUMNS.slice(0, 3).every((c) => isTodo(c.key as never))).toBe(true)
  })

  it("«Bog'lanish kerak» ustuni karta QABUL QILMAYDI (server `CanTransitionTo` da `new` yo'q)", () => {
    expect(STATUS_COLUMNS.find((c) => c.key === 'new')?.drop).toBeNull()
    expect(DUE_COLUMNS.find((c) => c.key === 'nodate')?.drop).toBeNull()
  })

  it('qabul qiladigan ustunlar faqat ruxsat etilgan bosqichlarni taklif qiladi', () => {
    const allowed = ['callback', 'done', 'failed']
    for (const c of [...DUE_COLUMNS, ...STATUS_COLUMNS]) {
      if (c.drop) expect(allowed).toContain(c.drop.nextStatus)
    }
  })
})

/* ====================================================================================== */

const stage = (p: Partial<ContactStage>): ContactStage => ({
  id: 'x', title: 'X', color: 'blue', order: 0,
  baseStatus: 'callback', baseStatusLabel: "Qayta qo'ng'iroq", isSystem: false, count: 0, ...p,
})

const req = (p: Partial<ContactRequestItem>): ContactRequestItem => ({
  id: 'r', studentId: 's', studentName: 'A', reasonId: '', reasonLabel: '', note: '',
  status: 'callback', statusLabel: '', dueDate: '', overdue: false, attemptCount: 0,
  lastResponse: '', lastActorName: '', lastActionAt: '', createdAt: '', createdBy: '',
  closedAt: '', closedBy: '', phones: [], ...p,
})

describe('stageColumns — serverdan kelgan ustunlar', () => {
  it('rang va yorliq ustundan olinadi', () => {
    const [c] = stageColumns([stage({ id: 's1', title: 'SMS yuborildi', color: 'cyan' })])
    expect(c.key).toBe('s1')
    expect(c.label).toBe('SMS yuborildi')
    expect(c.color).toBe('cyan')
    expect(c.stage?.id).toBe('s1')
  })

  it("noma'lum rang sahifani buzmaydi — slate ga tushadi", () => {
    const [c] = stageColumns([stage({ color: 'neon-pushti' as never })])
    expect(c.color).toBe('slate')
  })

  it("«Bog'lanish kerak»ga bog'langan ustun boshqa bosqichdagi kartani QABUL QILMAYDI", () => {
    // Serverda `new` ga qaytish taqiqlangan (`CanTransitionTo`) — taxta ham taklif qilmasin.
    const [c] = stageColumns([stage({ id: 'new', baseStatus: 'new' })])
    expect(c.drop).toBeNull()
  })

  it('qolgan ustunlar O‘Z bazaviy bosqichini taklif qiladi', () => {
    const cols = stageColumns([
      stage({ id: 'a', baseStatus: 'callback' }),
      stage({ id: 'b', baseStatus: 'done' }),
      stage({ id: 'c', baseStatus: 'failed' }),
    ])
    expect(cols.map((c) => c.drop?.nextStatus)).toEqual(['callback', 'done', 'failed'])
    // Qayta qo'ng'iroqqa sana kerak — taxta ertangi kunni taklif qiladi.
    expect(cols[0].drop?.offsetDays).toBe(1)
    expect(cols[1].drop?.offsetDays).toBeUndefined()
  })
})

describe('stageKeyOf — karta qaysi ustunda (server StageHomeOf nusxasi)', () => {
  const st = (id: string, baseStatus: string) =>
    ({ id, title: id, color: 'slate', order: 0, baseStatus, baseStatusLabel: '', isSystem: id === baseStatus, count: 0 }) as ContactStage
  const all = stageColumns([st('new', 'new'), st('custom', 'callback'), st('callback', 'callback'), st('done', 'done'), st('failed', 'failed')])

  it('ustuni belgilangan karta o‘sha ustunda', () => {
    expect(stageKeyOf(req({ stageId: 'custom' }), all)).toBe('custom')
  })

  it('ustunsiz karta O‘Z HOLATINING tizim ustuniga tushadi', () => {
    expect(stageKeyOf(req({ status: 'done' }), all)).toBe('done')
    expect(stageKeyOf(req({ stageId: '' }), all)).toBe('callback')
  })

  it("O'CHIRILGAN ustunga ishora qiladigan karta YO'QOLMAYDI", () => {
    // Xavfsizlik tarmog'i: noma'lum ustun → o'z holatining tizim ustuni.
    expect(stageKeyOf(req({ status: 'failed', stageId: 'ochirilgan' }), all)).toBe('failed')
  })

  it("tizim ustuni o'chirilgan (yashirilgan) bo'lsa — shu holatning BIRINCHI ustuniga", () => {
    const noCallback = stageColumns([st('new', 'new'), st('custom', 'callback')])
    expect(stageKeyOf(req({ status: 'callback', stageId: '' }), noCallback)).toBe('custom')
    // yakuniy holatning ustuni yo'q — taxtada yo'q (faqat ochiqlar)
    expect(stageKeyOf(req({ status: 'done' }), noCallback)).toBe('')
  })

  it('zaxira (STATUS_COLUMNS) ro‘yxatida ham ishlaydi', () => {
    expect(stageKeyOf(req({ status: 'new' }), STATUS_COLUMNS)).toBe('new')
  })
})

describe('requestKind — qarzdor / darsga kelmagan (rang va filtr)', () => {
  const kinds = [
    { kind: 'debt' as const, reasonId: 'r-debt', label: "To'lov qarzdorligi" },
    { kind: 'absence' as const, reasonId: 'r-abs', label: 'Ketma-ket darsga kelmadi' },
  ]

  it('sabab id bo‘yicha', () => {
    expect(requestKind({ reasonId: 'r-debt', reasonLabel: 'qayta nomlangan' }, kinds)).toBe('debt')
    expect(requestKind({ reasonId: 'r-abs', reasonLabel: '' }, kinds)).toBe('absence')
    expect(requestKind({ reasonId: 'boshqa', reasonLabel: "To'lov qarzdorligi" }, kinds)).toBe('other')
  })

  it('sabab katalogdan o‘chirilgan eski karta — matni bo‘yicha', () => {
    expect(requestKind({ reasonId: '', reasonLabel: "To'lov qarzdorligi" }, kinds)).toBe('debt')
    expect(requestKind({ reasonId: '', reasonLabel: 'Boshqa sabab' }, kinds)).toBe('other')
    expect(requestKind({ reasonId: 'r-debt', reasonLabel: '' }, undefined)).toBe('other')
  })
})
