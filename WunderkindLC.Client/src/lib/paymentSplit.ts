/**
 * TO'LOVNI OYLARGA TAQSIMLASH — serverdagi `PaymentMonthSplit.Allocate` ning AYNAN nusxasi (faqat KO'RSATISH:
 * to'lov oynasida "Taqsimlanadi: …" qatori). Haqiqiy taqsimotni SERVER qiladi (`PaymentIntake`), shuning uchun
 * qoida o'zgarsa ikkalasi BIRGA o'zgaradi — `lib/__tests__/paymentSplit.test.ts` serverdagi
 * `PaymentMonthSplitTests` holatlarini takrorlaydi.
 *
 * Qoida: tanlangan oydan boshlab har oy (per-guruh hisob qatori) o'z QOLDIG'ICHA oladi, oxirgi qatordan ortgani
 * o'sha oxirgi qatorda qoladi (avans). Summa tanlangan oy qoldig'iga sig'sa, tanlangan oy ro'yxatda bo'lmasa yoki
 * uning qoldig'i 0 bo'lsa (to'langan oy ATAYIN tanlangan) — bitta qism, tanlangan oyda.
 */
export interface SplitRow {
  month: string
  remaining: number
}

export interface SplitPart {
  month: string
  amount: number
}

export function splitPayment(rows: SplitRow[], selected: string, amount: number): SplitPart[] {
  const single = [{ month: selected, amount }]
  const start = rows.findIndex((r) => r.month === selected)
  if (start < 0 || rows[start].remaining <= 0 || amount <= rows[start].remaining) return single

  const parts: SplitPart[] = []
  let left = amount
  for (let i = start; i < rows.length && left > 0; i++) {
    // Oxirgi qator qolgan hammasini oladi (avans).
    const take = i === rows.length - 1 ? left : Math.min(Math.max(0, rows[i].remaining), left)
    if (take <= 0) continue
    parts.push({ month: rows[i].month, amount: take })
    left -= take
  }
  return parts.length === 0 ? single : parts
}
