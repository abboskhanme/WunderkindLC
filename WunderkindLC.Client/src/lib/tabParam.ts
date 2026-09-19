/**
 * `?tab=` manzil parametridan boshlang'ich tabni o'qish.
 *
 * NEGA KERAK: "Hisobotlar" bo'limi hisobot SAHIFAGA emas, sahifaning ICHIDAGI hisobot
 * TABIGA havola beradi (masalan Moliya → "Bonus", Kitoblar → "Analitika"). Parametrsiz
 * havola foydalanuvchini sahifaning birinchi (operativ) tabiga tashlab ketardi va u
 * hisobotni qo'lda qidirishga majbur bo'lardi.
 *
 * ⚠️ Odatda FAQAT boshlang'ich qiymat: `useState` initializer'ida bir marta o'qiladi. Keyin
 * foydalanuvchi tab almashtirsa manzil o'zgarmaydi va bu ATAYIN — har tab bosilganda
 * tarixga yozuv qo'shilsa "orqaga" tugmasi sahifadan chiqmay, tablar orasida aylanardi.
 *
 * ⚠️ ISTISNO — `search` berilgan holat: sahifada tab qatori BO'LMASA va ko'rinish faqat
 * menyudan tanlansa (Moliya), tab manzilga ERGASHISHI shart. U holda chaqiruvchi
 * `useLocation().search` ni uzatadi va qiymat har navigatsiyada QAYTA hisoblanadi — aks
 * holda menyudagi ikkinchi bandni bosganda sahifa o'zgarmay qolardi (komponent qayta
 * yaratilmaydi, faqat manzil o'zgaradi).
 *
 * Noma'lum qiymat JIM e'tiborsiz qoldiriladi (`fallback`) — qo'lda yozilgan yoki eskirgan
 * havola sahifani buzmasin.
 */
export function tabFromUrl<T extends string>(
  allowed: readonly T[],
  fallback: T,
  search?: string,
): T {
  const query = search ?? (typeof window === 'undefined' ? '' : window.location.search)
  const raw = new URLSearchParams(query).get('tab')
  return raw && (allowed as readonly string[]).includes(raw) ? (raw as T) : fallback
}
