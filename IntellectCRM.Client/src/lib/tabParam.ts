/**
 * `?tab=` manzil parametridan boshlang'ich tabni o'qish.
 *
 * NEGA KERAK: "Hisobotlar" bo'limi hisobot SAHIFAGA emas, sahifaning ICHIDAGI hisobot
 * TABIGA havola beradi (masalan Moliya → "Bonus", Kitoblar → "Analitika"). Parametrsiz
 * havola foydalanuvchini sahifaning birinchi (operativ) tabiga tashlab ketardi va u
 * hisobotni qo'lda qidirishga majbur bo'lardi.
 *
 * ⚠️ FAQAT boshlang'ich qiymat: `useState` initializer'ida bir marta o'qiladi. Keyin
 * foydalanuvchi tab almashtirsa manzil o'zgarmaydi va bu ATAYIN — har tab bosilganda
 * tarixga yozuv qo'shilsa "orqaga" tugmasi sahifadan chiqmay, tablar orasida aylanardi.
 *
 * Noma'lum qiymat JIM e'tiborsiz qoldiriladi (`fallback`) — qo'lda yozilgan yoki eskirgan
 * havola sahifani buzmasin.
 */
export function tabFromUrl<T extends string>(allowed: readonly T[], fallback: T): T {
  if (typeof window === 'undefined') return fallback
  const raw = new URLSearchParams(window.location.search).get('tab')
  return raw && (allowed as readonly string[]).includes(raw) ? (raw as T) : fallback
}
