import { useEffect, useState } from 'react'
import { getCashboxOptions, type CashboxOptions } from '@/api/services/cashboxes'

/**
 * To'lov/kirim/chiqim oynalaridagi «Kassa» tanlovi uchun faol kassalar va foydalanuvchining
 * standart kassasi. Yuklanguncha `null`.
 *
 * Tanlov faqat 2+ faol kassa bo'lganda ko'rsatiladi (`showCashboxPicker`) — bitta kassada maydon
 * ortiqcha, server baribir o'sha kassani tanlaydi.
 */
export function useCashboxOptions(enabled = true): CashboxOptions | null {
  const [options, setOptions] = useState<CashboxOptions | null>(null)
  useEffect(() => {
    if (!enabled) return
    let alive = true
    getCashboxOptions()
      .then((o) => alive && setOptions(o))
      .catch(() => {
        /* ikkilamchi o'qish: tanlov ko'rinmaydi, server kassani o'zi tanlaydi (kassirning o'z kassasi) */
      })
    return () => {
      alive = false
    }
  }, [enabled])
  return options
}

/** «Kassa» maydonini ko'rsatish kerakmi — faqat bir nechta faol kassa bo'lsa. */
export const showCashboxPicker = (o: CashboxOptions | null): o is CashboxOptions => !!o && o.items.length >= 2

/** Tanlovdagi yorliq: «Kassa nomi — mas'ul». */
export const cashboxOptionLabel = (o: { name: string; responsibleName: string | null }) =>
  o.responsibleName ? `${o.name} — ${o.responsibleName}` : o.name
