import { useSearchParams } from 'react-router-dom'

/**
 * Ro'yxat filtrlari MANZILDA (`?course=...&q=...`) — lid sahifasiga kirib "orqaga" qaytilganda
 * tanlangan filtrlar yo'qolmasin (edutizim ham filtrni URL'da saqlaydi). Yozish `replace` bilan:
 * har bosish/harf brauzer tarixiga yozilsa "orqaga" tugmasi sahifadan chiqmay qolardi.
 */
export function useUrlFilters<K extends string>(keys: readonly K[]) {
  const [sp, setSp] = useSearchParams()
  const values = Object.fromEntries(keys.map((k) => [k, sp.get(k) ?? ''])) as Record<K, string>

  /** Bir nechta parametrni birga yozadi; bo'sh qiymat — parametr olib tashlanadi. */
  const set = (patch: Record<string, string>) =>
    setSp(
      (prev) => {
        const next = new URLSearchParams(prev)
        for (const [k, v] of Object.entries(patch)) {
          if (v) next.set(k, v)
          else next.delete(k)
        }
        return next
      },
      { replace: true },
    )

  /** Faqat filtr kalitlarini tozalaydi (boshqa parametrlar — masalan `view` — qoladi). */
  const clear = () => set(Object.fromEntries(keys.map((k) => [k, ''])))

  const active = keys.some((k) => values[k] !== '')
  return { values, set, clear, active, params: sp }
}
