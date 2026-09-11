// `src/lib/permissions.ts` — xodim ruxsatlari. Bu yerdagi xato TO'G'RIDAN-TO'G'RI xavfsizlik
// muammosi (xodim ko'rmasligi kerak bo'lgan bo'limni ko'rishi yoki o'chira olishi), shuning uchun
// "ruxsat BERILMASLIGI kerak" holatlari ham alohida tekshiriladi.
//
// Server tomondagi juftligi: `AdminPermAttribute` — qoidalar bir xil bo'lishi shart.

import { describe, expect, it } from 'vitest'
import {
  can,
  canAny,
  pageRowActions,
  sectionRowActions,
  toggleSectionRow,
  togglePageRow,
  sectionActions,
  superOrGranted,
  toggleSectionAction,
  writeSection,
  type PermAction,
} from '@/lib/permissions'

/** `Set<PermAction>` ni barqaror tartibda solishtirish uchun. */
const sorted = (s: Set<string>): string[] => [...s].sort()

describe('can — ruxsat berish', () => {
  it('permissions null/undefined bo\'lsa (admin/superadmin) hamma narsaga ruxsat', () => {
    expect(can(null, 'students', 'delete')).toBe(true)
    expect(can(undefined, 'finance', 'edit')).toBe(true)
  })

  it('yalang "section" tokeni bo\'limdagi BARCHA amallarni ochadi', () => {
    const perms = ['students']
    expect(can(perms, 'students', 'view')).toBe(true)
    expect(can(perms, 'students', 'create')).toBe(true)
    expect(can(perms, 'students', 'edit')).toBe(true)
    expect(can(perms, 'students', 'delete')).toBe(true)
  })

  it('"section:action" faqat aynan shu amalni ochadi', () => {
    const perms = ['students:edit']
    expect(can(perms, 'students', 'edit')).toBe(true)
    expect(can(perms, 'students', 'create')).toBe(false)
    expect(can(perms, 'students', 'delete')).toBe(false)
  })

  it('biror yozish ruxsati bo\'lsa "view" avtomatik ochiladi', () => {
    expect(can(['students:edit'], 'students', 'view')).toBe(true)
    expect(can(['students:delete'], 'students', 'view')).toBe(true)
    expect(can(['students:create'], 'students', 'view')).toBe(true)
  })
})

describe('can — ruxsat BERMASLIGI kerak bo\'lgan holatlar', () => {
  it('faqat ko\'rish ruxsati o\'chirishga yo\'l bermaydi', () => {
    const perms = ['students:view']
    expect(can(perms, 'students', 'view')).toBe(true)
    expect(can(perms, 'students', 'delete')).toBe(false)
    expect(can(perms, 'students', 'edit')).toBe(false)
    expect(can(perms, 'students', 'create')).toBe(false)
  })

  it('bir bo\'lim ruxsati boshqa bo\'limga SIZIB O\'TMAYDI', () => {
    const perms = ['students', 'leads:edit']
    expect(can(perms, 'finance', 'view')).toBe(false)
    expect(can(perms, 'finance', 'edit')).toBe(false)
    expect(can(perms, 'leads', 'delete')).toBe(false)
  })

  it('bo\'sh massiv — hech narsaga ruxsat yo\'q (null bilan ADASHTIRILMAYDI)', () => {
    expect(can([], 'students', 'view')).toBe(false)
    expect(can([], 'students', 'delete')).toBe(false)
  })

  it('o\'xshash nomli bo\'lim prefiksi ruxsat bermaydi ("students" ≠ "studentsArchive")', () => {
    expect(can(['students'], 'studentsArchive', 'view')).toBe(false)
    expect(can(['students:view'], 'studentsArchive', 'view')).toBe(false)
  })
})

describe('superOrGranted — superadmin yoki ANIQ berilgan ruxsat', () => {
  it('superadmin har doim true', () => {
    expect(superOrGranted('superadmin', null, 'retentionBonus')).toBe(true)
    expect(superOrGranted('superadmin', [], 'retentionBonus')).toBe(true)
  })

  it('oddiy admin (permissions null) ATAYIN false — can() dan asosiy farqi', () => {
    expect(superOrGranted('admin', null, 'retentionBonus')).toBe(false)
    expect(superOrGranted('admin', undefined, 'retentionBonus')).toBe(false)
    // Taqqoslash uchun: `can()` aynan shu kirishda true qaytaradi.
    expect(can(null, 'retentionBonus', 'view')).toBe(true)
  })

  it('ruxsati aniq berilgan xodim true (yalang ham, amalli token ham)', () => {
    expect(superOrGranted('staff', ['retentionBonus'], 'retentionBonus')).toBe(true)
    expect(superOrGranted('staff', ['retentionBonus:view'], 'retentionBonus')).toBe(true)
  })

  it('ruxsati yo\'q xodim va noma\'lum rol false', () => {
    expect(superOrGranted('staff', ['students'], 'retentionBonus')).toBe(false)
    expect(superOrGranted('staff', [], 'retentionBonus')).toBe(false)
    expect(superOrGranted(undefined, ['students'], 'retentionBonus')).toBe(false)
  })
})

describe('sectionActions — tokenlardan amallar to\'plami', () => {
  it('yalang "section" → to\'rtala amal', () => {
    expect(sorted(sectionActions(new Set(['students']), 'students'))).toEqual([
      'create',
      'delete',
      'edit',
      'view',
    ])
  })

  it('yozish amali tanlansa "view" ham qo\'shiladi', () => {
    expect(sorted(sectionActions(new Set(['students:edit']), 'students'))).toEqual(['edit', 'view'])
  })

  it('faqat "view" tokeni — faqat view', () => {
    expect(sorted(sectionActions(new Set(['students:view']), 'students'))).toEqual(['view'])
  })

  it('boshqa bo\'lim tokenlari hisobga olinmaydi', () => {
    expect(sectionActions(new Set(['leads', 'finance:edit']), 'students').size).toBe(0)
  })
})

describe('writeSection — amallar to\'plamini tokenlarga qaytarish', () => {
  it('to\'rtala amal tanlansa IXCHAM yalang token yoziladi', () => {
    const acts = new Set<PermAction>(['view', 'create', 'edit', 'delete'])
    expect(sorted(writeSection(new Set(), 'students', acts))).toEqual(['students'])
  })

  it('qisman tanlovda "section:action" tokenlari yoziladi', () => {
    const acts = new Set<PermAction>(['view', 'edit'])
    expect(sorted(writeSection(new Set(), 'students', acts))).toEqual([
      'students:edit',
      'students:view',
    ])
  })

  it('bo\'sh to\'plam shu bo\'limning eski tokenlarini TOZALAYDI', () => {
    const before = new Set(['students', 'students:edit', 'leads:view'])
    expect(sorted(writeSection(before, 'students', new Set()))).toEqual(['leads:view'])
  })

  it('boshqa bo\'lim tokenlariga tegmaydi va kirish to\'plamini o\'zgartirmaydi', () => {
    const before = new Set(['leads', 'finance:view'])
    const after = writeSection(before, 'students', new Set<PermAction>(['view']))
    expect(sorted(after)).toEqual(['finance:view', 'leads', 'students:view'])
    expect(sorted(before)).toEqual(['finance:view', 'leads']) // mutatsiya yo'q
  })
})

describe('toggleSectionAction — matritsadagi katakni bosish', () => {
  it('yozish amali yoqilsa "view" ham avtomatik yoqiladi', () => {
    expect(sorted(toggleSectionAction(new Set(), 'students', 'edit'))).toEqual([
      'students:edit',
      'students:view',
    ])
  })

  it('"view" o\'chirilsa bo\'limning BARCHA amallari o\'chadi (ko\'rmasdan yozib bo\'lmaydi)', () => {
    const before = new Set(['students:view', 'students:edit', 'students:delete'])
    expect(sorted(toggleSectionAction(before, 'students', 'view'))).toEqual([])
  })

  it('yoqilgan amalni qayta bosish uni o\'chiradi', () => {
    const before = new Set(['students:view', 'students:edit'])
    expect(sorted(toggleSectionAction(before, 'students', 'edit'))).toEqual(['students:view'])
  })

  it('yalang bo\'limdan bitta amalni yechish qolganini ochiq tokenlarga yoyadi', () => {
    const after = toggleSectionAction(new Set(['students']), 'students', 'delete')
    expect(sorted(after)).toEqual(['students:create', 'students:edit', 'students:view'])
    expect(can([...after], 'students', 'delete')).toBe(false)
  })

  it('oxirgi yetishmayotgan amal qo\'shilsa yana yalang tokenga ixchamlanadi', () => {
    const before = new Set(['students:view', 'students:create', 'students:edit'])
    expect(sorted(toggleSectionAction(before, 'students', 'delete'))).toEqual(['students'])
  })
})

// ---------------------------------------------------------------- SAHIFA (page) ruxsatlari

describe('can — sahifa (page) kalitlari', () => {
  it("BO'LIM ruxsati uning HAR BIR sahifasini ochadi (eski xodimlar buzilmasin)", () => {
    const perms = ['students']
    expect(can(perms, 'students.turnstile', 'view')).toBe(true)
    expect(can(perms, 'students.turnstile', 'delete')).toBe(true)
    expect(can(['students:edit'], 'students.face', 'edit')).toBe(true)
    expect(can(['students:edit'], 'students.face', 'delete')).toBe(false)
  })

  it("SAHIFA ruxsati bo'limni faqat KO'RISHga ochadi, yozishga EMAS", () => {
    const perms = ['students.turnstile']
    // Menyudagi "O'quvchilar" guruhi ko'rinishi kerak.
    expect(can(perms, 'students', 'view')).toBe(true)
    // Lekin bo'lim darajasida yozish — YO'Q (turniket operatori o'quvchi yaratmasin).
    expect(can(perms, 'students', 'create')).toBe(false)
    expect(can(perms, 'students.list', 'create')).toBe(false)
    // Qo'shni sahifaga ham o'tmaydi.
    expect(can(perms, 'students.face', 'view')).toBe(false)
  })

  it('sahifa o‘z amallarini oladi', () => {
    expect(can(['students.turnstile:edit'], 'students.turnstile', 'edit')).toBe(true)
    expect(can(['students.turnstile:edit'], 'students.turnstile', 'view')).toBe(true)
    expect(can(['students.turnstile:edit'], 'students.turnstile', 'delete')).toBe(false)
  })

  it("BOSHQA bo'lim sahifasi adashtirmaydi", () => {
    expect(can(['teachers.list'], 'students.list', 'view')).toBe(false)
    expect(can(['students-arxiv.turnstile'], 'students', 'view')).toBe(false)
  })

  it('canAny — kalitlardan birortasi yetadi', () => {
    expect(canAny(['students.list:view'], ['students.list', 'students.notes'], 'create')).toBe(false)
    expect(canAny(['students.list'], ['students.list', 'students.notes'], 'create')).toBe(true)
    expect(canAny(['students.notes'], ['students.list', 'students.notes'], 'view')).toBe(true)
    expect(canAny(['finance'], ['finance.bonus', 'audit'], 'edit')).toBe(true)
  })
})

describe('matritsa — bo\'lim va sahifa qatorlari', () => {
  const PAGES = ['students.list', 'students.turnstile', 'students.face']

  it("bo'lim qatori yoqilsa sahifa qatorlari ham yoqilgan bo'lib ko'rinadi", () => {
    const perms = new Set(['students'])
    expect([...sectionRowActions(perms, 'students', PAGES)].sort()).toEqual([
      'create',
      'delete',
      'edit',
      'view',
    ])
    expect([...pageRowActions(perms, 'students', 'students.turnstile')].sort()).toEqual([
      'create',
      'delete',
      'edit',
      'view',
    ])
  })

  it("bo'lim ochiq bo'lsa ham bitta sahifani OLIB TASHLASH mumkin (avtomatik yoyish)", () => {
    let perms = new Set(['students'])
    perms = togglePageRow(perms, 'students', 'students.turnstile', 'view', PAGES)
    // Bo'lim tokeni sahifalarga yoyildi, turniketniki olib tashlandi.
    expect(perms.has('students')).toBe(false)
    expect(perms.has('students.list')).toBe(true)
    expect(perms.has('students.face')).toBe(true)
    expect([...perms].some((p) => p.startsWith('students.turnstile'))).toBe(false)
    // Natija amalda ham to'g'ri:
    const arr = [...perms]
    expect(can(arr, 'students.list', 'edit')).toBe(true)
    expect(can(arr, 'students.turnstile', 'view')).toBe(false)
  })

  it("hamma sahifa qayta yoqilsa — bitta bo'lim tokeniga IXCHAMLANADI", () => {
    let perms = new Set(['students.list', 'students.face'])
    perms = togglePageRow(perms, 'students', 'students.turnstile', 'view', PAGES)
    // "view" bosildi, lekin qolganlari to'liq — shuning uchun to'plamlar teng emas, ixchamlanmaydi.
    expect(perms.has('students')).toBe(false)
    // Endi turniketni ham to'liq qilamiz:
    for (const a of ['create', 'edit', 'delete'] as PermAction[])
      perms = togglePageRow(perms, 'students', 'students.turnstile', a, PAGES)
    expect(perms.has('students')).toBe(true)
    expect([...perms].filter((p) => p.startsWith('students.'))).toEqual([])
  })

  it("bo'lim qatoridagi ko'rish o'chirilsa — sahifa tokenlari ham tozalanadi", () => {
    let perms = new Set(['students.turnstile:edit', 'students.face'])
    // Avval bo'limni to'liq yoqamiz, keyin ko'rishni o'chiramiz.
    perms = toggleSectionRow(perms, 'students', 'view', PAGES)
    expect(perms.has('students:view')).toBe(true)
    perms = toggleSectionRow(perms, 'students', 'view', PAGES)
    expect([...perms]).toEqual([])
  })
})
