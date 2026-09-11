import { formatMoney } from '@/lib/utils'

/**
 * O'quvchining chegirmasi — RO'YXAT/KARTOCHKA uchun bitta qatorlik matn.
 *
 * ⚠️ O'quvchida bir NECHTA amaldagi chegirma bo'lishi mumkin (har fan uchun alohida —
 * `.claude/rules/discounts.md`), `Student.discount*` maydonlari esa faqat BITTASINI
 * ko'rsatadi. Shuning uchun `discountCount` bo'yicha «+N ta fan» qo'shimchasi chiqadi:
 * aks holda ro'yxatda 3 ta chegirmali o'quvchi 1 ta chegirmali bo'lib ko'rinardi.
 *
 * Qaytadi:
 * - `"20% — Aka-uka chegirmasi"` — bitta chegirma;
 * - `"20% — Aka-uka (+2 ta fan)"` — yana IKKI fanda chegirma bor;
 * - `"3 ta fan bo'yicha chegirma"` — eski maydonlar bo'sh, lekin sanoq bor;
 * - `''` — chegirma yo'q (chaqiruvchi o'zi «Yo'q» yozadi yoki qatorni chizmaydi).
 */
export function studentDiscountLabel(s: {
  discountPct?: number
  discountAmount?: number
  discountNote?: string
  discountCount?: number
}): string {
  const pct = s.discountPct ?? 0
  const amount = s.discountAmount ?? 0
  const count = s.discountCount ?? 0

  if (pct > 0 || amount > 0) {
    const base =
      [pct > 0 ? `${pct}%` : null, amount > 0 ? formatMoney(amount) : null]
        .filter(Boolean)
        .join(' + ') + (s.discountNote ? ` — ${s.discountNote}` : '')
    // N — QOLGAN fanlar soni (bittasi matnning o'zida allaqachon ko'rsatilgan).
    return count > 1 ? `${base} (+${count - 1} ta fan)` : base
  }

  if (count > 0) return `${count} ta fan bo'yicha chegirma`
  return ''
}
