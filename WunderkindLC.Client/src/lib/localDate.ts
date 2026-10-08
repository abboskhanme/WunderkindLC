/**
 * MAHALLIY sana yordamchilari (markaz — UTC+5). ⚠️ `Date.toISOString()` UTC beradi: mahalliy 00:00–05:00
 * orasida u KECHAGI kunni qaytaradi — kassirning kunlik hisobi, "bugun" va "shu oy" bir kun/bir oy surilardi.
 * Biznes sanasi uchun HAR DOIM shu funksiyalar (yoki `lib/month.ts` → `todayIso`) ishlatiladi.
 */

/** Sana → "yyyy-MM-dd" (mahalliy yil/oy/kun qismlaridan). */
export function localIsoDate(d: Date): string {
  const p = (n: number) => String(n).padStart(2, '0')
  return `${d.getFullYear()}-${p(d.getMonth() + 1)}-${p(d.getDate())}`
}

/** "yyyy-MM-dd" ni `delta` kunga suradi (oy/yil chegarasi va kabisa yili to'g'ri). */
export function shiftLocalDay(day: string, delta: number): string {
  const d = new Date(`${day}T00:00:00`)
  d.setDate(d.getDate() + delta)
  return localIsoDate(d)
}
