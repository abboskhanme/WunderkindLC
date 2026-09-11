/**
 * Sahifalararo "Orqaga" konteksti.
 *
 * Muammo: o'quvchi profili (`/admin/students/:id`) bir necha joydan ochiladi — o'quvchilar
 * ro'yxati, GURUH sahifasi, o'qituvchi profili, moliya... Lekin sahifadagi "Orqaga" havolasi
 * qat'iy `/admin/students` ga olib borardi, ya'ni guruh ichidan kirgan odam ro'yxatga tushib
 * qolardi. Yechim: havola bosilganda `Link state` ichida QAYERDAN kelinganini uzatamiz
 * (`react-router` buni brauzer history'siga yozadi — sahifa yangilansa ham saqlanadi),
 * qabul qiluvchi sahifa esa shuni o'qib "Orqaga" manzilini moslaydi. State bo'lmasa
 * (to'g'ridan-to'g'ri URL, yorliq) — odatdagi zaxira manzil ishlaydi.
 */
export interface BackState {
  /** Qaytish manzili, masalan `/admin/classes/{id}`. */
  backTo: string
  /** Havolada ko'rinadigan matn, masalan guruh nomi. */
  backLabel: string
}

/** `<Link state={...}>` uchun qulay quruvchi. */
export function backState(to: string, label: string): BackState {
  return { backTo: to, backLabel: label }
}

/** `useLocation().state` dan `BackState`ni xavfsiz o'qiydi; bo'lmasa `fallback` qaytaradi. */
export function readBackState(state: unknown, fallback: BackState): BackState {
  const s = state as Partial<BackState> | null | undefined
  if (s && typeof s.backTo === 'string' && s.backTo && typeof s.backLabel === 'string' && s.backLabel) {
    return { backTo: s.backTo, backLabel: s.backLabel }
  }
  return fallback
}
