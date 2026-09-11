import {
  Bar, BarChart, CartesianGrid, ResponsiveContainer, Tooltip, XAxis, YAxis,
} from 'recharts'
import type { DayCount } from '@/types'

/**
 * KUNLIK OQIM grafigi — "oxirgi 30 kunda nechta keldi". Lid formalari statistikasi ham, daraja
 * testi statistikasi ham SHU komponentdan foydalanadi: ikkalasi bir xil ma'lumot shaklini
 * (`DayCount`) qaytaradi va ikkala sahifada grafik bir xil o'qilishi kerak.
 *
 * <p>Bo'sh kunlar serverdan ham keladi (grafik uzilib qolmasin), shu sabab bu yerda faqat
 * chiziladi. Hech qanday kunda yozuv bo'lmasa — grafik o'rniga matn (bo'm-bo'sh o'qlar
 * foydalanuvchiga hech narsa aytmaydi).</p>
 */

/** Yakka seriya — legend kerak emas; rang loyihaning tekshirilgan palitrasidan (sky-600). */
const BAR_COLOR = '#0284c7'
const axisTick = { fontSize: 12, fill: '#94a3b8' }
const tooltipStyle = { borderRadius: 12, border: '1px solid #e2e8f0', fontSize: 13 }

/** "2026-08-06" → "06.08" (o'q uchun ixcham). */
export function shortDay(iso: string) {
  return iso.length >= 10 ? `${iso.slice(8, 10)}.${iso.slice(5, 7)}` : iso
}

export function DailyFlowChart({
  data, name, emptyText, height = 220,
}: {
  data: DayCount[]
  /** Ustun nomi (tooltipda ko'rinadi) — masalan "Ariza" yoki "Topshirdi". */
  name: string
  emptyText: string
  height?: number
}) {
  if (!data.some((d) => d.count > 0))
    return <p className="py-10 text-center text-sm text-slate-400">{emptyText}</p>

  // ⚠️ Ma'lumot kaliti TAYIN (`value`), ko'rsatiladigan nom esa `<Bar name=...>` orqali beriladi.
  // Ilgari `name` propi kalit sifatida ham ishlatilardi (`{ [name]: d.count }`) — chaqiruvchi
  // `name="name"` bersa, u X o'qining kaliti (`dataKey="name"`) bilan to'qnashib, grafik buzilardi.
  // Recharts'da `name` faqat tooltip/legend uchun, `dataKey` esa ma'lumot manzili — ularni
  // ajratib qo'yish chaqiruvchining matniga bog'liqlikni butunlay yo'q qiladi.
  const rows = data.map((d) => ({ name: shortDay(d.date), value: d.count }))
  return (
    <ResponsiveContainer width="100%" height={height}>
      <BarChart data={rows} margin={{ top: 10, right: 10, left: 0, bottom: 0 }}>
        <CartesianGrid strokeDasharray="3 3" vertical={false} stroke="#eef0f4" />
        <XAxis dataKey="name" tickLine={false} axisLine={false} tick={axisTick} interval="preserveStartEnd" />
        <YAxis tickLine={false} axisLine={false} tick={axisTick} allowDecimals={false} width={36} />
        <Tooltip contentStyle={tooltipStyle} />
        <Bar dataKey="value" name={name} fill={BAR_COLOR} radius={[4, 4, 0, 0]} maxBarSize={22} />
      </BarChart>
    </ResponsiveContainer>
  )
}
