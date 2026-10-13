import type { ActivationQuote } from '@/api/services/classes'
import { formatMonth } from '@/config/constants'
import { formatMoney } from '@/lib/utils'

/**
 * «Aktiv o'quvchi qilish» — birinchi oy QANDAY hisoblanganini bitta qatorda ochiq aytadi (owner: "to'lovni ayirib
 * hisoblab berish"). Raqamlar serverdan (`ActivationQuoteAsync` — aktivlashtirish yozadigan summaning O'ZI).
 */
export function activationQuoteText(q: ActivationQuote): string {
  const month = formatMonth(q.month)
  if (q.monthly <= 0) return `${month}: guruhda oylik narx yo'q — hisob yozilmaydi.`
  if (q.amount <= 0) return `${month}: shu sanadan keyin bu oyda dars yo'q — ${month} uchun hisob yozilmaydi.`
  if (q.fullMonth) return `${month}: oyning birinchi darsidan — to'liq oylik ${formatMoney(q.amount)}.`
  const how =
    q.lessonFee > 0
      ? `${q.lessonsLeft} dars × ${formatMoney(q.lessonFee)}`
      : `${formatMoney(q.monthly)} ÷ ${q.lessonsInMonth} dars × ${q.lessonsLeft}`
  return `${month}: ${q.lessonsInMonth} darsdan ${q.lessonsLeft} tasi qoladi — ${how} = ${formatMoney(q.amount)}.`
}

/** Keyingi oylar — to'liq oylik (guruh narxi). */
export function activationNextMonthsText(q: ActivationQuote): string {
  return q.monthly > 0 ? `Keyingi oylar — to'liq oylik ${formatMoney(q.monthly)}.` : ''
}
