import { describe, expect, it } from 'vitest'
import type { HrEmployeeNode, HrEmployeeTree, HrTaskTemplate } from '@/types/hrTasks'
import { HR_MAX_BATCH_EMPLOYEES, HR_MAX_BATCH_TOTAL } from '@/types/hrTasks'
import {
  hrAmount,
  hrAttachmentFileError,
  hrFlattenTree,
  hrFormatDate,
  hrFormatDateTime,
  hrFormatRange,
  hrIsOverdue,
  hrMediaSrc,
  hrPresetRange,
  hrProgressPercent,
  hrProgressText,
  hrProofFileError,
  hrRecurrenceLabel,
  hrTashkentParts,
  hrToIso,
  hrToday,
  hrVoiceDuration,
} from '@/components/hr-tasks/lib'
import { localDigits, phoneHref } from '@/components/hr-tasks/phone'
import {
  hrAllTreePaths,
  hrBuildTreeRows,
  hrDefaultExpandedPaths,
  hrSameManagerIds,
  hrToggleTreePath,
} from '../employeeTree'
import {
  hrBatchLimitError,
  hrBatchSummary,
  hrEmptyTaskRow,
  hrHasShortMonthDay,
  hrTaskRowErrors,
  hrToBatchItem,
  hrToTemplateBatchItem,
} from '../taskBatchForm'
import { emptyTemplateForm, templateErrors, toTemplateFormValues, toTemplatePayload } from '../templateForm'
import { dayHeadline, daySummary, sortDayTasks, stripDays, weekShiftFor } from '../myDay'
import { voiceFile } from '../voice'

/** HR topshiriqlari — LMS'dan ko'chirilgan sof yordamchilar (docs/SPEC-hr-tasks.md). */

describe('lib — yorliqlar, KPI, holat', () => {
  it('takroriylik matni', () => {
    expect(hrRecurrenceLabel({ recurrence: 'daily', weekdays: [1, 2, 3, 4, 5, 6, 7], daysOfMonth: [] })).toBe('Har kuni')
    expect(hrRecurrenceLabel({ recurrence: 'daily', weekdays: [1, 3], daysOfMonth: [] })).toBe('Har kuni: dushanba, chorshanba')
    expect(hrRecurrenceLabel({ recurrence: 'weekly', weekdays: [5], daysOfMonth: [] })).toBe('Har hafta: juma')
    expect(hrRecurrenceLabel({ recurrence: 'monthly', weekdays: [], daysOfMonth: [14] })).toBe('Oyning 14-sanasi')
    expect(hrRecurrenceLabel({ recurrence: 'monthly', weekdays: [], daysOfMonth: [14, 28] })).toBe('Oyning 14, 28-sanalari')
  })

  it('numeric(12,2) satr ham, son ham o\'qiladi; buzuq — 0', () => {
    expect(hrAmount('147.00')).toBe(147)
    expect(hrAmount(12.5)).toBe(12.5)
    expect(hrAmount('abc')).toBe(0)
    expect(hrAmount(null)).toBe(0)
  })

  it('KPI foizi 100 da qisiladi, `check` uchun 0 / null', () => {
    expect(hrProgressPercent({ kind: 'numeric', targetAmount: '200', currentAmount: '147' })).toBe(73.5)
    expect(hrProgressPercent({ kind: 'numeric', targetAmount: 10, currentAmount: 25 })).toBe(100)
    expect(hrProgressPercent({ kind: 'check', targetAmount: null, currentAmount: null })).toBe(0)
    expect(hrProgressText({ kind: 'numeric', targetAmount: 200, currentAmount: 147, unit: 'lead' })).toBe('147/200 lead')
    expect(hrProgressText({ kind: 'check', targetAmount: null, currentAmount: null })).toBeNull()
  })

  it('muddati o\'tgan — faqat ochiq/qaytarilgan', () => {
    const now = new Date('2026-10-21T10:00:00Z')
    expect(hrIsOverdue({ dueAt: '2026-10-21T09:00:00Z', status: 'open' }, now)).toBe(true)
    expect(hrIsOverdue({ dueAt: '2026-10-21T09:00:00Z', status: 'returned' }, now)).toBe(true)
    expect(hrIsOverdue({ dueAt: '2026-10-21T09:00:00Z', status: 'done' }, now)).toBe(false)
    expect(hrIsOverdue({ dueAt: '2026-10-21T11:00:00Z', status: 'open' }, now)).toBe(false)
    expect(hrIsOverdue({ dueAt: null, status: 'open' }, now)).toBe(false)
  })
})

describe('lib — Toshkent vaqti', () => {
  it('bugun brauzer mintaqasidan qat\'i nazar Toshkent bo\'yicha', () => {
    // 2026-10-21 20:30 UTC = 22-oktabr 01:30 Toshkent.
    expect(hrToday(new Date('2026-10-21T20:30:00Z'))).toBe('2026-10-22')
  })

  it('sana + vaqt → ISO (+05:00) va qaytadan', () => {
    expect(hrToIso('2026-10-21', '18:00')).toBe('2026-10-21T18:00:00+05:00')
    expect(hrToIso('2026-10-21', '25:00')).toBeNull()
    expect(hrToIso('21.10.2026', '18:00')).toBeNull()
    expect(hrTashkentParts('2026-10-21T13:00:00Z')).toEqual({ date: '2026-10-21', time: '18:00' })
    expect(hrFormatDateTime('2026-10-21T13:00:00Z')).toBe('21.10.2026 18:00')
    expect(hrFormatDate('2026-10-21')).toBe('21.10.2026')
    expect(hrFormatRange('2026-10-01', '2026-10-01')).toBe('01.10.2026')
  })

  it('davr tugmalari bugundan ORQAGA', () => {
    expect(hrPresetRange('today', '2026-10-21')).toEqual({ dateFrom: '2026-10-21', dateTo: '2026-10-21' })
    expect(hrPresetRange('7d', '2026-10-21')).toEqual({ dateFrom: '2026-10-15', dateTo: '2026-10-21' })
    // 2026-10-21 — chorshanba → hafta dushanbadan.
    expect(hrPresetRange('week', '2026-10-21')).toEqual({ dateFrom: '2026-10-19', dateTo: '2026-10-21' })
    expect(hrPresetRange('month', '2026-10-21')).toEqual({ dateFrom: '2026-10-01', dateTo: '2026-10-21' })
    expect(hrPresetRange('prev_month', '2026-03-10')).toEqual({ dateFrom: '2026-02-01', dateTo: '2026-02-28' })
  })
})

describe('lib — fayllar', () => {
  it('HTML/SVG/skript isbot ham, biriktirma ham bo\'lmaydi; hajm chegarasi', () => {
    expect(hrProofFileError({ size: 100, type: 'image/svg+xml' })).toMatch(/HTML, SVG/)
    expect(hrProofFileError({ size: 100, type: 'text/html; charset=utf-8' })).toMatch(/HTML, SVG/)
    expect(hrProofFileError({ size: 11 * 1024 * 1024, type: 'image/jpeg' })).toMatch(/juda katta/)
    expect(hrProofFileError({ size: 100, type: 'image/jpeg' })).toBeNull()
    expect(hrAttachmentFileError({ size: 100, type: 'application/javascript' })).toMatch(/biriktirib/)
  })

  it('fayl manzili /uploads ostiga, tashqi havola o\'zgarmaydi', () => {
    expect(hrMediaSrc('hr/proofs/2026/10/a.jpg')).toBe('/uploads/hr/proofs/2026/10/a.jpg')
    expect(hrMediaSrc('/uploads/hr/a.jpg')).toBe('/uploads/hr/a.jpg')
    expect(hrMediaSrc('https://example.com/x')).toBe('https://example.com/x')
    expect(hrMediaSrc(null)).toBeNull()
  })

  it('ovoz davomiyligi', () => {
    expect(hrVoiceDuration(83)).toBe('1:23')
    expect(hrVoiceDuration(3725)).toBe('1:02:05')
    expect(hrVoiceDuration(-1)).toBeNull()
  })

  it('yozib olingan ovoz nomli faylga aylanadi', () => {
    const file = voiceFile(new Blob(['x'], { type: 'audio/webm;codecs=opus' }), 65)
    expect(file.name).toBe('ovoz-1-05.webm')
    expect(file.type).toBe('audio/webm;codecs=opus')
    expect(voiceFile(new Blob(['x'], { type: 'audio/mp4' }), 3).name).toBe('ovoz-0-03.m4a')
  })

  it('telefon: istalgan yozuvdan 9 ta mahalliy raqam', () => {
    expect(localDigits('998901234567')).toBe('901234567')
    expect(localDigits('+998 90 123 45 67')).toBe('901234567')
    expect(localDigits('90 123 45 67')).toBe('901234567')
    expect(phoneHref('998901234567')).toBe('tel:+998901234567')
  })
})

describe('Topshiriq berish — qatorlar va chegaralar', () => {
  it('bir martalik qator: sarlavha, muddat, KPI maqsadi', () => {
    const row = hrEmptyTaskRow('2026-10-21')
    expect(hrTaskRowErrors(row, false).title).toBeDefined()
    const ok = { ...row, title: 'Hisobot tayyorlash' }
    expect(hrTaskRowErrors(ok, false)).toEqual({})
    expect(hrTaskRowErrors({ ...ok, kind: 'numeric', targetAmount: '0' }, false).targetAmount).toBeDefined()
    expect(hrTaskRowErrors({ ...ok, kind: 'numeric', targetAmount: '12,5' }, false)).toEqual({})
    expect(hrTaskRowErrors({ ...ok, dueTime: '' }, false).due).toBeDefined()
  })

  it('takroriy qator: hafta kunlari, oy sanalari, KPI birligi majburiy', () => {
    const row = { ...hrEmptyTaskRow('2026-10-21'), title: 'Kunlik hisobot' }
    expect(hrTaskRowErrors({ ...row, weekdays: [] }, true).weekdays).toBeDefined()
    const monthly = { ...row, recurrence: 'monthly' as const, dayOfMonth1: '30', dayOfMonth2: '30' }
    expect(hrTaskRowErrors(monthly, true).dayOfMonth2).toBe('Ikkala sana bir xil bo‘lmasin')
    expect(hrHasShortMonthDay(monthly)).toBe(true)
    expect(hrTaskRowErrors({ ...row, kind: 'numeric', targetAmount: '200', unit: '' }, true).unit).toBeDefined()
  })

  it('chegaralar yuborishdan OLDIN', () => {
    expect(hrBatchLimitError(0, 1)).toBe('Kamida bitta xodimni tanlang')
    expect(hrBatchLimitError(HR_MAX_BATCH_EMPLOYEES + 1, 1)).toMatch(/xodim tanlanadi/)
    expect(hrBatchLimitError(50, 11)).toMatch(new RegExp(`chegara ${HR_MAX_BATCH_TOTAL}`))
    expect(hrBatchLimitError(3, 2)).toBeNull()
    expect(hrBatchSummary(6, 3)).toBe('6 xodim × 3 topshiriq = 18 ta yaratiladi')
  })

  it('so\'rov tanasi: muddat Toshkent ISO, takroriyga ovoz ketmaydi', () => {
    const row = { ...hrEmptyTaskRow('2026-10-21'), title: ' Hisobot ', kind: 'numeric' as const, targetAmount: '12,5', unit: 'dona' }
    expect(hrToBatchItem({ ...row, attachment: { id: 'a1' } as never })).toMatchObject({
      title: 'Hisobot',
      dueAt: '2026-10-21T18:00:00+05:00',
      targetAmount: 12.5,
      unit: 'dona',
      attachmentId: 'a1',
    })
    const weekly = hrToTemplateBatchItem({ ...row, recurrence: 'weekly', weekday: '5' })
    expect(weekly).toMatchObject({ recurrence: 'weekly', weekdays: [5], daysOfMonth: [], dueTime: '18:00' })
    expect('attachmentId' in weekly).toBe(false)
  })
})

describe('Takroriy topshiriq formasi', () => {
  it('maqsad (xodim/lavozim) va tahrirlash qiymatlari', () => {
    const empty = { ...emptyTemplateForm(), title: 'Kunlik hisobot' }
    expect(templateErrors(empty).targetEmployeeId).toBe('Xodimni tanlang')
    expect(templateErrors({ ...empty, targetType: 'position' }).targetPositionId).toBe('Lavozimni tanlang')
    const values = toTemplateFormValues({
      id: 't1', title: 'Hisobot', description: null, targetType: 'position', targetEmployeeId: null, targetPositionId: 'p1',
      recurrence: 'weekly', weekdays: [3], daysOfMonth: [], dueTime: '18:00:00', kind: 'numeric', targetAmount: '200.00',
      unit: 'lead', proofRequired: true, overduePolicy: 'keep_open', isActive: true, createdBy: null, createdAt: null, updatedAt: null,
    } as HrTaskTemplate)
    expect(values).toMatchObject({ weekday: '3', dueTime: '18:00', targetAmount: '200.00' })
    expect(toTemplatePayload(values)).toMatchObject({ targetEmployeeId: null, targetPositionId: 'p1', weekdays: [3], targetAmount: 200 })
  })
})

function node(id: string, children: HrEmployeeNode[] = []): HrEmployeeNode {
  return { id, fullName: id, children } as unknown as HrEmployeeNode
}

describe('Xodimlar daraxti', () => {
  // A → B → A (halqa) va C ikki rahbarda (A va B).
  const c = node('C')
  const tree: HrEmployeeTree = { roots: [node('A', [node('B', [node('A'), c]), c])], unassigned: [node('D')] }

  it('halqa xavfsiz, ikki rahbarli xodim ikkala joyda (yo\'l bo\'yicha kalit)', () => {
    const all = hrAllTreePaths(tree)
    expect([...all].sort()).toEqual(['A', 'A/B'])
    const { roots, unassigned } = hrBuildTreeRows(tree, all)
    expect(roots.map((r) => r.path)).toEqual(['A', 'A/B', 'A/B/C', 'A/C'])
    expect(unassigned.map((r) => r.path)).toEqual(['D'])
    expect(hrFlattenTree(tree).map((n) => n.id)).toEqual(['A', 'B', 'C', 'D'])
  })

  it('sukut — faqat birinchi daraja ochiq; ochish/yopish', () => {
    const expanded = hrDefaultExpandedPaths(tree)
    expect(hrBuildTreeRows(tree, expanded).roots.map((r) => r.path)).toEqual(['A', 'A/B', 'A/C'])
    expect(hrToggleTreePath(expanded, 'A').has('A')).toBe(false)
    expect(hrSameManagerIds(['a', 'b'], ['b', 'a'])).toBe(true)
    expect(hrSameManagerIds(['a'], ['a', 'b'])).toBe(false)
  })
})

describe('Kun ko\'rinishi (Mini App)', () => {
  it('lenta bugundan bir kun oldinga cho\'ziladi, haftama-hafta suriladi', () => {
    expect(stripDays('2026-10-21', 0)).toEqual([
      '2026-10-16', '2026-10-17', '2026-10-18', '2026-10-19', '2026-10-20', '2026-10-21', '2026-10-22',
    ])
    expect(stripDays('2026-10-21', -1)[0]).toBe('2026-10-09')
    expect(weekShiftFor('2026-10-21', '2026-10-21')).toBe(0)
    expect(weekShiftFor('2026-10-23', '2026-10-21')).toBe(1)
    expect(weekShiftFor('2026-10-10', '2026-10-21')).toBe(-1)
  })

  it('sarlavha va yakun satri', () => {
    expect(dayHeadline('2026-10-21', '2026-10-21')).toBe('Bugun')
    expect(dayHeadline('2026-10-20', '2026-10-21')).toBe('Kecha')
    expect(dayHeadline('2026-10-22', '2026-10-21')).toBe('Ertaga')
    expect(dayHeadline('2026-09-01', '2026-10-21')).toBe('1-sentabr, seshanba')
    const items = [
      { status: 'done', isOverdue: false },
      { status: 'open', isOverdue: false },
      { status: 'cancelled', isOverdue: null },
    ]
    expect(daySummary(items, '2026-10-21', '2026-10-21')).toBe('Bugun: 2 dan 1 tasi bajarildi')
    expect(daySummary(items, '2026-10-25', '2026-10-21')).toBe('25-oktabr: 2 ta topshiriq rejalashtirilgan')
  })

  it('muddati o\'tgan ochiqlar boshida, qolgani server tartibida', () => {
    const list = [
      { id: 1, status: 'done', isOverdue: true },
      { id: 2, status: 'open', isOverdue: false },
      { id: 3, status: 'returned', isOverdue: true },
      { id: 4, status: 'open', isOverdue: true },
    ]
    expect(sortDayTasks(list).map((t) => t.id)).toEqual([3, 4, 1, 2])
  })
})
