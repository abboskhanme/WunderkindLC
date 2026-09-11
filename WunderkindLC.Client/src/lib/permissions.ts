import { useAuth } from '@/context/auth-context'

/** Bo'lim ichidagi amal: ko'rish / qo'shish / tahrir / o'chirish. */
export type PermAction = 'view' | 'create' | 'edit' | 'delete'

/** Amallar ro'yxati (rol berish UI'si va tekshiruvlar uchun yagona manba). */
export const PERM_ACTIONS: { key: PermAction; label: string }[] = [
  { key: 'view', label: "Ko'rish" },
  { key: 'create', label: "Qo'shish" },
  { key: 'edit', label: 'Tahrir' },
  { key: 'delete', label: "O'chirish" },
]

/**
 * SAHIFA (page) kaliti — bo'lim ichidagi bitta sahifa: `"bolim.sahifa"` (masalan
 * `"students.turnstile"`). Nuqta ajratkich. Bo'lim kalitida nuqta bo'lmaydi.
 */
export const PAGE_SEPARATOR = '.'

/** Sahifa kalitining BO'LIMI (`"students.turnstile"` → `"students"`); bo'lim kaliti bo'lsa `null`. */
export function parentOf(key: string): string | null {
  const i = key.indexOf(PAGE_SEPARATOR)
  return i <= 0 ? null : key.slice(0, i)
}

/**
 * Xodimning `permissions` ro'yxatida `key` (bo'lim YOKI sahifa) uchun `action` amaliga ruxsati bormi?
 *
 * Token turlari:
 *  - `permissions` = undefined/null → admin/superadmin (ruxsat cheklovi yo'q) → har doim `true`.
 *  - yalang `"bolim"` → shu bo'limda TO'LIQ ruxsat (barcha sahifa, barcha amal) — eski ma'lumot ham shunday.
 *  - `"bolim:action"` → butun bo'lim, faqat shu amal.
 *  - `"bolim.sahifa"` / `"bolim.sahifa:action"` → FAQAT shu sahifa.
 *  - `view` — biror amal (create/edit/delete) ruxsati bo'lsa ham ko'ra oladi.
 *
 * ⚠️ MEROS PASTGA: bo'lim ruxsati uning HAR BIR sahifasini ochadi (shuning uchun eski xodimlar
 * ruxsati o'zgarmaydi). YUQORIGA esa faqat `view`: bitta sahifasi bor xodim bo'lim GURUHINI
 * menyuda ko'radi, lekin bo'lim darajasidagi yozish amallariga ruxsat OLMAYDI.
 * Server tomondagi juftligi: `PermissionRules.HasSection` / `CanWrite`.
 */
export function can(
  permissions: string[] | null | undefined,
  key: string,
  action: PermAction,
): boolean {
  if (!permissions) return true // admin/superadmin — cheklovsiz
  if (own(permissions, key, action)) return true
  // Pastga: sahifa so'ralgan bo'lsa — butun bo'lim ruxsati ham yetadi.
  const parent = parentOf(key)
  if (parent && own(permissions, parent, action)) return true
  // Yuqoriga — FAQAT ko'rish: bo'lim so'ralgan bo'lsa, uning istalgan sahifasi yetadi.
  if (action === 'view' && !parent)
    return permissions.some((p) => p.startsWith(`${key}${PAGE_SEPARATOR}`))
  return false
}

/** Aynan shu kalitning o'z tokeni (meroslarsiz). */
function own(permissions: string[], key: string, action: PermAction): boolean {
  if (permissions.includes(key)) return true // yalang = to'liq
  if (permissions.includes(`${key}:${action}`)) return true
  if (action === 'view') return permissions.some((p) => p.startsWith(`${key}:`))
  return false
}

/** Kalitlardan BIRORTASIGA ruxsat bormi (bitta ish ikki sahifadan bajarilsa — masalan izohlar). */
export function canAny(
  permissions: string[] | null | undefined,
  keys: string[],
  action: PermAction,
): boolean {
  if (!permissions) return true
  return keys.some((k) => can(permissions, k, action))
}

/**
 * SUPERADMIN yoki shu bo'lim ruxsati ANIQ berilgan xodim.
 *
 * `can()` dan farqi: oddiy **`admin`** roli (`permissions == null`) `false` oladi. Markaz egasi
 * o'zida qoldirmoqchi bo'lgan nozik amallar uchun — masalan a'zolikni aktivlashtirishdagi
 * «Bonus hisoblansin» ptichkasi. Xodimga ruxsat «Xodimlar va rollar» bo'limidan beriladi
 * (`adminPermissions` katalogidagi kalit orqali).
 *
 * Server tomonda AYNAN shu qoida: `AdminPermAttribute.IsSuperAdminOrGranted`.
 */
export function superOrGranted(
  role: string | undefined,
  permissions: string[] | null | undefined,
  section: string,
): boolean {
  if (role === 'superadmin') return true
  if (!permissions) return false // admin (cheklovsiz) — bu yerda ATAYIN kirmaydi
  return permissions.includes(section) || permissions.some((p) => p.startsWith(`${section}:`))
}

/** <see cref="superOrGranted"/> ning hook ko'rinishi — joriy foydalanuvchi bo'yicha. */
export function useSuperOrGranted(section: string): boolean {
  const { user } = useAuth()
  return superOrGranted(user?.role, user?.permissions, section)
}

/** Joriy foydalanuvchining ruxsatiga bog'langan tekshiruv: `can('students.list', 'edit')`. */
export function usePerm() {
  const { user } = useAuth()
  const perms = user?.permissions
  return {
    /** Bo'lim/sahifa + amalga ruxsat bormi? */
    can: (key: string, action: PermAction) => can(perms, key, action),
    /** Kalitlardan BIRORTASIGA ruxsat bormi (bir ish ikki sahifadan bajarilsa). */
    canAny: (keys: string[], action: PermAction) => canAny(perms, keys, action),
  }
}

// ---------- Rol berish (matritsa) yordamchilari ----------

const ALL_ACTIONS: PermAction[] = ['view', 'create', 'edit', 'delete']

/**
 * Ruxsat tokenlari to'plamidan bir bo'lim uchun TANLANGAN amallarni chiqaradi.
 * Yalang `"section"` → barcha amallar. Biror amal (create/edit/delete) bo'lsa `view` ham qo'shiladi.
 */
export function sectionActions(perms: Set<string>, section: string): Set<PermAction> {
  if (perms.has(section)) return new Set(ALL_ACTIONS)
  const out = new Set<PermAction>()
  for (const a of ALL_ACTIONS) if (perms.has(`${section}:${a}`)) out.add(a)
  if (out.size > 0) out.add('view')
  return out
}

/**
 * Bir bo'limning amallar to'plamini token to'plamiga qayta yozadi (mavjud shu bo'lim tokenlarini
 * almashtiradi). Barcha 4 amal tanlansa — yalang `"section"` (ixcham, backward-compat) saqlanadi.
 */
export function writeSection(perms: Set<string>, section: string, acts: Set<PermAction>): Set<string> {
  const next = new Set(perms)
  next.delete(section)
  for (const a of ALL_ACTIONS) next.delete(`${section}:${a}`)
  if (acts.size === 0) return next
  if (ALL_ACTIONS.every((a) => acts.has(a))) {
    next.add(section) // to'liq → yalang
    return next
  }
  for (const a of acts) next.add(`${section}:${a}`)
  return next
}

/**
 * Matritsada bitta katakni bosish: shu bo'lim+amalni almashtiradi.
 * Qoidalar: yozish amali (create/edit/delete) yoqilsa `view` ham yoqiladi; `view` o'chirilsa
 * shu bo'limning barcha amallari o'chadi (ko'rmasdan yozib bo'lmaydi).
 */
export function toggleSectionAction(
  perms: Set<string>,
  section: string,
  action: PermAction,
): Set<string> {
  const acts = sectionActions(perms, section)
  if (acts.has(action)) {
    acts.delete(action)
    if (action === 'view') acts.clear()
  } else {
    acts.add(action)
    if (action !== 'view') acts.add('view')
  }
  return writeSection(perms, section, acts)
}

// ---------- SAHIFA (page) darajasidagi matritsa ----------
//
// Bo'lim qatori — MASTER: yoqilsa bo'limning BARCHA sahifalari ochiladi va sahifa tokenlari
// keraksiz bo'lib qoladi (shuning uchun tozalanadi). Sahifa qatori esa aynan bitta sahifani
// beradi. Ikkalasi bir vaqtda saqlanmaydi — token ro'yxati doim eng IXCHAM ko'rinishda turadi:
//   • hamma sahifa bir xil to'plamda  →  bitta bo'lim tokeni (`"students"` / `"students:edit"`);
//   • aks holda                       →  sahifa tokenlari (`"students.turnstile:edit"`).

/**
 * Bo'lim tokenini SAHIFALARGA yoyadi: bo'limdagi amallar har bir sahifaga alohida yoziladi va
 * bo'lim tokeni o'chadi. "Bo'lim ochiq, lekin bitta sahifasi yopiq" holatini yasash uchun kerak.
 */
export function expandSection(perms: Set<string>, section: string, pages: string[]): Set<string> {
  const acts = sectionActions(perms, section)
  if (acts.size === 0 || pages.length === 0) return new Set(perms)
  let next = writeSection(perms, section, new Set())
  for (const p of pages) next = writeSection(next, p, new Set(acts))
  return next
}

/** Barcha sahifalar AYNAN bir xil to'plamda bo'lsa — bitta bo'lim tokeniga qaytaradi (ixchamlash). */
function collapsePages(perms: Set<string>, section: string, pages: string[]): Set<string> {
  if (pages.length === 0) return perms
  const first = [...pageOwnActions(perms, pages[0])].sort().join(',')
  if (first === '') return perms
  for (const p of pages) if ([...pageOwnActions(perms, p)].sort().join(',') !== first) return perms
  let next = perms
  for (const p of pages) next = writeSection(next, p, new Set())
  return writeSection(next, section, sectionActions(perms, pages[0]))
}

/** Sahifaning O'Z tokenlari (bo'limdan meros olinmagan holda). */
function pageOwnActions(perms: Set<string>, page: string): Set<PermAction> {
  return sectionActions(perms, page)
}

/**
 * Matritsadagi BO'LIM qatorining holati: bo'lim tokeni yoki (eski ma'lumotda) barcha sahifalarda
 * bir xil yoqilgan amal — ikkalasi ham "bo'lim ochiq" degani.
 */
export function sectionRowActions(
  perms: Set<string>,
  section: string,
  pages: string[] = [],
): Set<PermAction> {
  const out = sectionActions(perms, section)
  if (pages.length > 0)
    for (const a of ALL_ACTIONS)
      if (pages.every((p) => pageOwnActions(perms, p).has(a))) out.add(a)
  return out
}

/** Matritsadagi SAHIFA qatorining holati: o'z tokeni + bo'limdan meros. */
export function pageRowActions(perms: Set<string>, section: string, page: string): Set<PermAction> {
  const out = pageOwnActions(perms, page)
  for (const a of sectionActions(perms, section)) out.add(a)
  return out
}

/**
 * BO'LIM qatoridagi katakni bosish. Bo'lim — master, shuning uchun shu amal bo'yicha sahifa
 * tokenlari ham tozalanadi (yoqilganda keraksiz, o'chirilganda esa "bo'limni yopdim, lekin
 * sahifalari ochiq qoldi" degan chalkashlik bo'lmasin).
 */
export function toggleSectionRow(
  perms: Set<string>,
  section: string,
  action: PermAction,
  pages: string[] = [],
): Set<string> {
  const on = !sectionRowActions(perms, section, pages).has(action)
  const acts = sectionActions(perms, section)
  if (on) {
    acts.add(action)
    if (action !== 'view') acts.add('view') // ko'rmasdan yozib bo'lmaydi
  } else {
    acts.delete(action)
    if (action === 'view') acts.clear() // ko'rish yopilsa — bo'limda hech narsa qolmaydi
  }
  let next = writeSection(perms, section, acts)
  for (const p of pages) {
    if (!on && action === 'view') {
      next = writeSection(next, p, new Set()) // butun bo'lim yopildi — sahifalar ham
      continue
    }
    const pa = pageOwnActions(next, p)
    pa.delete(action)
    if (!pa.has('view') && !acts.has('view')) pa.clear()
    next = writeSection(next, p, pa)
  }
  return next
}

/**
 * SAHIFA qatoridagi katakni bosish.
 *
 * ⚠️ Sahifa bo'limdan MEROS olib turgan bo'lsa (bo'lim tokeni bor), avval bo'lim sahifalarga
 * YOYILADI (`expandSection`) — aks holda "bo'limni berdim, faqat bittasini olib tashlayman"
 * mumkin bo'lmasdi (bo'limni o'chirmasdan sahifani o'chirib bo'lmaydi).
 */
export function togglePageRow(
  perms: Set<string>,
  section: string,
  page: string,
  action: PermAction,
  pages: string[],
): Set<string> {
  let next = perms
  if (sectionActions(perms, section).size > 0) next = expandSection(perms, section, pages)
  next = toggleSectionAction(next, page, action)
  return collapsePages(next, section, pages)
}
