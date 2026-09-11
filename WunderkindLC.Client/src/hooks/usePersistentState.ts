import { useCallback, useState } from 'react'
import type { Dispatch, SetStateAction } from 'react'

/**
 * `useState` kabi ishlaydi, lekin qiymatni `sessionStorage`da saqlaydi. Shu tufayli ro'yxat
 * sahifasidan chiqib (masalan o'quvchi/guruh kartasiga o'tib) qaytib kelinganda tanlangan filtr
 * yoki qidiruv holati YO'QOLMAYDI — sahifa qayta mount bo'lganda saqlangan qiymat tiklanadi.
 *
 * Kalit sahifaga xos bo'lishi kerak (masalan `"students.classFilter"`), aks holda turli sahifalar
 * bir-birining holatini ustiga yozadi. Sessiya (tab) yopilganda holat o'z-o'zidan tozalanadi.
 */
export function usePersistentState<T>(key: string, initial: T): [T, Dispatch<SetStateAction<T>>] {
  const [state, setState] = useState<T>(() => {
    try {
      const raw = sessionStorage.getItem(key)
      return raw !== null ? (JSON.parse(raw) as T) : initial
    } catch {
      return initial
    }
  })

  const setPersistent = useCallback<Dispatch<SetStateAction<T>>>(
    (value) => {
      setState((prev) => {
        const next = typeof value === 'function' ? (value as (p: T) => T)(prev) : value
        try {
          sessionStorage.setItem(key, JSON.stringify(next))
        } catch {
          /* kvota to'lgan yoki inkognito — saqlamasdan davom etamiz */
        }
        return next
      })
    },
    [key],
  )

  return [state, setPersistent]
}
