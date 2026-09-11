/**
 * CRM statistikasi grafiklari uchun YAGONA palitra va umumiy "chrome" (o'q, to'r, tooltip).
 *
 * ⚠️ Rangni bu yerdan tashqarida yozma va yangi tus O'YLAB TOPMA. Bu qiymatlar
 * dataviz validatoridan (`validate_palette.js`) o'tgan — o'zgartirilsa qayta tekshirish shart.
 */

/**
 * KATEGORIAL (identity) palitra — seriyalarga QAT'IY shu tartibda beriladi, aylantirilmaydi.
 * Oq fon (`#ffffff`) uchun 5 ta tekshiruvdan ham ogohlantirishsiz o'tgan:
 * yorug'lik diapazoni, xroma, CVD ajralishi (eng yomon juft ΔE 8.8 deutan),
 * oddiy ko'rish uchun ajralish (ΔE 20.1) va fon bilan kontrast (hammasi ≥ 3:1).
 */
export const CATEGORICAL = ['#6366f1', '#16a34a', '#ec4899', '#d97706', '#0284c7'] as const

/**
 * «Boshqa» uchun neytral kulrang. Bu KATEGORIAL slot EMAS — identity tashimaydi,
 * shuning uchun xroma qoidasi unga tegishli emas. Oq fonda kontrasti 4.76:1.
 * 6-chi va undan keyingi kesmalar shu rangda bitta «Boshqa» ga yig'iladi.
 */
export const OTHER_GRAY = '#64748b'

/** Nechta kesma o'z rangini oladi — qolgani «Boshqa» ga yig'iladi. */
export const MAX_SLICES = CATEGORICAL.length

/**
 * Kategorial rangni indeks bo'yicha beradi. Indeks palitradan chiqib ketsa — «Boshqa» kulrangi.
 * Rang OBYEKT ketma-ketligiga bog'lanadi, uning REYTINGIGA emas.
 */
export function categoricalColor(index: number): string {
  return CATEGORICAL[index] ?? OTHER_GRAY
}

/**
 * ORDINAL (bosqich tartibi) ramp — bitta tus (indigo), ochiqdan quyuqqa.
 * Voronka bosqichlari va bosqich taqsimoti kategorial EMAS: tartibini almashtirsang
 * ma'nosi o'zgaradi, shuning uchun bitta tusning pog'onalari ishlatiladi.
 *
 * Har bir qator `validate_palette.js --ordinal --mode light --surface "#ffffff"` dan
 * to'liq o'tgan: yorug'lik monoton, qo'shni pog'onalar ΔL ≥ 0.06, eng ochiq pog'ona
 * fonda ≥ 2:1 (#818cf8 — 2.98:1), tus tarqalishi 2°.
 * 6 tadan ortiq pog'ona shu diapazonga sig'maydi (ΔL qoidasi buziladi), shuning uchun
 * bosqich 6 tadan ko'p bo'lsa shu 6 pog'ona bir tekis "cho'ziladi" — qo'shni bosqichlar
 * bir xil pog'onani ulashishi mumkin, identity esa o'q yorlig'idan o'qiladi.
 */
const STAGE_RAMPS: readonly string[][] = [
  ['#6366f1'],
  ['#818cf8', '#312e81'],
  ['#818cf8', '#575cbb', '#312e81'],
  ['#818cf8', '#656ccf', '#4a4ca7', '#312e81'],
  ['#818cf8', '#6c74d9', '#575cbb', '#43459e', '#312e81'],
  ['#818cf8', '#7078df', '#5f65c7', '#4f53af', '#404098', '#312e81'],
]

/** `count` ta bosqich uchun ochiqdan quyuqqa ramp (birinchi bosqich — eng ochiq). */
export function stageRamp(count: number): string[] {
  if (count <= 0) return []
  const exact = STAGE_RAMPS[count - 1]
  if (exact) return [...exact]
  // 6 tadan ko'p: eng uzun validatsiyadan o'tgan rampni bir tekis namuna qilib olamiz.
  const base = STAGE_RAMPS[STAGE_RAMPS.length - 1]
  return Array.from(
    { length: count },
    (_, i) => base[Math.round((i * (base.length - 1)) / (count - 1))],
  )
}

/* ---------- Umumiy grafik "chrome" — bosiq (recessive) o'q va to'r ---------- */

/** O'q yorliqlari — matn rangida, seriya rangida EMAS. */
export const axisTick = { fontSize: 12, fill: '#94a3b8' }

/** To'r/o'q chizig'i — fondan bir pog'ona farq qiladigan yupqa SOLID chiziq (punktir emas). */
export const gridStroke = '#eef0f4'

export const tooltipStyle = { borderRadius: 12, border: '1px solid #e2e8f0', fontSize: 13 }

/** Bar ustidagi kursor (hover) — juda yengil soya. */
export const barCursor = { fill: 'rgba(0,0,0,0.03)' }

/* ---------- Formatlash ---------- */

/**
 * Bosqichda o'rtacha turish vaqti. `null` — TARIX YETARLI EMAS, bu "0 soat" DEGANI EMAS.
 * Chaqiruvchi `null` holatini alohida matn bilan ko'rsatishi shart.
 */
export function formatDwell(avgHours: number | null): string | null {
  if (avgHours == null) return null
  if (avgHours < 1) return `${Math.round(avgHours * 60)} daqiqa`
  if (avgHours < 48) return `${avgHours.toFixed(1)} soat`
  return `${(avgHours / 24).toFixed(1)} kun`
}

/* ---------- SEKVENSIAL (magnituda) — matritsa kataklari uchun ---------- */

/**
 * «Kim qaysi bosqichgacha olib bordi» matritsasidagi katak foni: BITTA tus (kategorial
 * palitraning 1-sloti, indigo) ning ochiqdan quyuqqa pog'onasi. Magnituda — rang bilan,
 * chunki bu ikki o'lchovli jadval (menejer × bosqich) va uzunlik uchun joy yo'q.
 *
 * ⚠️ Shaffoflik 0.55 dan OSHMAYDI. Sabab hisoblab chiqilgan: eng quyuq katak oq fonda
 * `#a9abf7` bo'ladi va uning ustidagi matn (`#1e293b`, slate-800) kontrasti **6.9:1** —
 * ya'ni AA dan yuqori. Undan quyuqroq qilinsa matn oqartirilishi kerak bo'lardi va bir
 * jadvalda ikki xil matn rangi paydo bo'lardi.
 *
 * ⚠️ 0 — mutlaqo rangsiz (fon qoladi): "hech narsa yo'q" ni eng och tus bilan ko'rsatish
 * uni "ozgina bor" dan ajratib bo'lmaydigan qilib qo'yardi.
 */
export function matrixTint(value: number, max: number): string | undefined {
  if (value <= 0 || max <= 0) return undefined
  // Kvadrat ildiz — kichik qiymatlar ham ko'rinadigan bo'lsin (chiziqli shkalada bitta
  // yirik katak qolganlarini butunlay oqartirib yuborardi).
  const alpha = 0.08 + 0.47 * Math.sqrt(Math.min(1, value / max))
  return `rgba(99, 102, 241, ${alpha.toFixed(3)})`
}
