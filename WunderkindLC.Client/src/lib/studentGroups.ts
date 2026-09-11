/**
 * O'QUVCHINING GURUHLARI — KO'RSATISH qoidasi (sof funksiyalar, `studentGroups.test.ts`).
 *
 * ⚠️ **INVARIANT:** o'quvchi bir vaqtda bir NECHTA guruhda bo'lishi mumkin va uning umumiy holati
 * BARCHA a'zoliklar ustidan jamlama (`active > trial > yearFrozen > frozen > ""` — serverdagi
 * `MembershipLifecycle.MemberState`). Shuning uchun hech bir ekranda guruh nomi BITTA a'zolikdan
 * (`groups[0]`, `.find(...)`) yoki eski `student.className` yorlig'idan OLINMASIN.
 *
 * `className` — BIRINCHI qo'shilgan guruhning nomi. U keyin YANGILANMAYDI (server:
 * `ClassesController.AddMember` uni faqat BO'SH bo'lganda yozadi), ya'ni eski guruhida
 * muzlatilib yangisida o'qiyotgan o'quvchi hamma joyda ESKI guruhi bilan ko'rinardi. Bu yerda u
 * faqat ZAXIRA: a'zolik ma'lumoti umuman kelmagan bo'lsa.
 */

/** Bitta a'zolik — nomi va holati (server DTO'larida shu ikki maydon har doim bor). */
export interface GroupLike {
  name: string
  /** 'active' | 'trial' | 'frozen' | 'completed' | '' */
  status?: string
}

/**
 * KO'RSATILADIGAN guruh nomlari: MUZLATILGAN a'zoliklar chiqarib tashlanadi (o'quvchi eski
 * guruhida muzlatilib yangisida o'qiyotgan bo'lsa faqat YANGISI ko'rinsin), lekin HAMMA a'zoligi
 * muzlatilgan bo'lsa — o'shalar qoladi (aks holda o'quvchi "guruhsiz" bo'lib ko'rinardi).
 * Takrorsiz, alifbo tartibida.
 */
export function displayGroupNames(groups: GroupLike[] | undefined | null): string[] {
  const list = (groups ?? []).filter((g) => !!g?.name)
  const open = list.filter((g) => g.status !== 'frozen' && g.status !== 'completed')
  const use = open.length > 0 ? open : list
  return [...new Set(use.map((g) => g.name))].sort((a, b) => a.localeCompare(b))
}

/**
 * Guruhlar matni (", " bilan). A'zolik ro'yxati bo'sh bo'lsa — `fallback` (odatda eski
 * `student.className`), u ham bo'sh bo'lsa `empty` ('' yoki '—').
 */
export function groupsText(
  groups: GroupLike[] | undefined | null,
  fallback?: string | null,
  empty = '',
): string {
  const names = displayGroupNames(groups)
  if (names.length > 0) return names.join(', ')
  return fallback && fallback.length > 0 ? fallback : empty
}

/**
 * BITTA guruh ko'rsatilishi shart bo'lgan joylar uchun "asosiy guruh": avval faol, keyin sinov,
 * keyin muzlatilgan (serverdagi `MembershipLifecycle.PrimaryMembership` bilan bir xil tartib).
 * Hech qachon "birinchi qo'shilgani".
 */
export function primaryGroupName(
  groups: GroupLike[] | undefined | null,
  fallback?: string | null,
): string {
  const rank = (s?: string) => (s === 'trial' ? 1 : s === 'frozen' ? 2 : s === 'completed' ? 3 : 0)
  const sorted = [...(groups ?? [])].filter((g) => !!g?.name).sort((a, b) => rank(a.status) - rank(b.status))
  return sorted[0]?.name ?? (fallback ?? '')
}

/** `Student.groupStates` ni `GroupLike` ga keltiradi (nomi/holati bir xil maydonlarda). */
export function statesToGroups(
  states: { name: string; status: string }[] | undefined | null,
): GroupLike[] {
  return (states ?? []).map((g) => ({ name: g.name, status: g.status }))
}
