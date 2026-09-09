import type { DiscountScopeOption } from '@/api/services/discounts'

/**
 * CHEGIRMA QAMROVI — sof qoidalar (`__tests__/discountScope.test.ts`).
 *
 * Komponentdan ATAYIN ajratilgan: bitta fayl ham komponent, ham konstanta eksport qilsa
 * Vite'ning fast-refresh qoidasi buziladi (`react-refresh/only-export-components`).
 *
 * Batafsil: `.claude/rules/discounts.md` §5.
 */

/**
 * `null`/`''` guruh id — «Barcha guruhlar» qamrovi. Select `value` si va xarita kaliti
 * sifatida BIR XIL ko'rinishga keltiriladi (aks holda `null` va `''` ikki xil kalit bo'lardi).
 */
export const scopeKey = (groupId: string | null | undefined) => groupId ?? ''

/**
 * «Hali tanlanmagan» qamrov. ⚠️ `''` DAN FARQ QILADI — `''` bu «Barcha guruhlar» degan
 * HAQIQIY tanlov. Ikkalasi bir xil bo'lsa, hech narsa tanlamagan admin bilmasdan barcha
 * fanlarga chegirma berib yuborardi.
 */
export const SCOPE_NONE = '__none__'

/**
 * YANGI chegirma oynasidagi STANDART tanlov (`.claude/rules/discounts.md` §5):
 *
 * | Bo'sh FANlar | Standart tanlov |
 * |---|---|
 * | bitta | o'sha fan |
 * | bir nechta | HECH NARSA (`SCOPE_NONE`) — admin o'zi tanlaydi |
 * | umuman yo'q (o'quvchi guruhsiz) | «Barcha guruhlar» |
 * | hammasida chegirma bor | HECH NARSA (`SCOPE_NONE`) |
 *
 * ⚠️ «Barcha guruhlar» HECH QACHON o'z-o'zidan tanlanmaydi (o'quvchining fani bo'lsa):
 * xatolarning zarari TENG EMAS. Blanket chegirma tanlab qo'yilsa, o'quvchi KEYIN yangi
 * guruhga qo'shilganda chegirma o'sha fanga ham JIMGINA tushadi va buni hech kim sezmaydi
 * (pul yo'qoladi). Aniq fan tanlangan bo'lsa, yangi fanga chegirma tushmaydi — admin buni
 * KO'RADI va kerak bo'lsa qo'shadi.
 *
 * ⚠️ Ilgari bu yerda `freeScopes[0]?.groupId` turardi va `scopes[0]` HAR DOIM «Barcha
 * guruhlar» bo'lgani uchun, hamma fani band o'quvchida oyna aynan BLANKET qamrov bilan
 * ochilardi — hech bir ogohlantirish chiqmay, «Saqlash» ochiq qolardi.
 */
export function defaultScopeKey(scopes: DiscountScopeOption[]): string {
  const free = scopes.filter((s) => !s.hasActive)
  const freeGroups = free.filter((s) => s.groupId)
  if (freeGroups.length === 1) return scopeKey(freeGroups[0].groupId)
  if (freeGroups.length > 1) return SCOPE_NONE
  // Bo'sh FAN yo'q. O'quvchining fani UMUMAN bo'lmasa (guruhsiz) — «Barcha guruhlar»,
  // lekin faqat u BO'SH bo'lsa. Aks holda tanlov MAJBURIY.
  const hasAnyGroupScope = scopes.some((s) => s.groupId)
  if (!hasAnyGroupScope && free.some((s) => !s.groupId)) return ''
  return SCOPE_NONE
}

/**
 * Davr NOTO'G'RI: boshlanish oyi tugash oyidan KEYIN.
 *
 * ⚠️ Bunday qator `active` bo'lib saqlanadi, lekin `TuitionService.DiscountActiveForMonth`
 * uni HECH QACHON qo'llamaydi — natijada ekranda «Davri boshlanmagan yoki tugagan» turadi
 * va klassik «chegirma berdim, oylik o'zgarmadi» shikoyati chiqadi. Shuning uchun saqlashdan
 * OLDIN bloklanadi.
 */
export function periodInvalid(startMonth: string, endMonth: string): boolean {
  return !!startMonth && !!endMonth && startMonth > endMonth
}

/**
 * «Oylar bo'yicha qo'llangan chegirma» jadvalidagi GURUH ustuni.
 *
 * ⚠️ Server bo'sh `groupName` ni IKKI xil holatda qaytaradi va ular BOSHQA-BOSHQA narsa:
 *   • `groupId == null` — hisob qatori umuman guruhga bog'lanmagan (eski/`className` hisobi),
 *     ya'ni chegirma «Barcha guruhlar» ma'nosida;
 *   • `groupId` bor, lekin guruh topilmadi — guruh O'CHIRILGAN (tarixiy qator).
 *
 * Ikkalasi ham «Barcha guruhlar» deb yozilsa, HAR FANGA berilgan tarixiy chegirma butun
 * o'quvchiga berilgandek o'qilardi.
 */
export function appliedGroupLabel(groupId: string | null | undefined, groupName: string): string {
  if (groupName) return groupName
  return groupId ? "Guruh o'chirilgan" : 'Barcha guruhlar'
}
