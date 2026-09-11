/**
 * INTERAKTIV MASHQ (topshiriq konstruktori) — ma'lumot modeli.
 *
 * O'quv dasturining OXIRGI bosqichida (Dastur → Modul → Mavzu → Dars → Topshiriq) topshiriq turi
 * "Mashq" tanlansa, shu konstruktor ochiladi: avval TUR tanlanadi (8 kategoriya, 25 tur), so'ng
 * o'sha turning tahrirlovchisi + jonli "foydalanuvchi ko'rinishi" ko'rsatiladi.
 *
 * Saqlash: `CourseItem.ExerciseKind` (tur) + `CourseItem.ExerciseJson` (shu fayldagi `ExerciseData`
 * JSON'i). Bir turdan boshqasiga o'tilganda mazmun ham almashadi (har turning o'z shakli bor).
 */

// ============================ Turlar ============================

export type ExerciseKind =
  // Gap tuzish (Make sentence)
  | 'sentence-order'
  | 'sentence-audio'
  | 'sentence-image'
  | 'sentence-choice'
  // Bo'sh joyni to'ldirish
  | 'fill-choose'
  | 'fill-write'
  | 'fill-audio'
  | 'fill-image'
  | 'fill-media'
  // So'z tanlash
  | 'wordpick-plain'
  | 'wordpick-image'
  | 'wordpick-audio'
  // So'z topish
  | 'wordfind-plain'
  | 'wordfind-image'
  | 'wordfind-audio'
  // Reading
  | 'reading-choice'
  | 'reading-fill'
  | 'reading-short'
  // Test
  | 'test-image'
  | 'test-imageopts'
  | 'test-audio'
  // Writing & Speaking
  | 'writing'
  | 'speaking'
  // Moslashtirish
  | 'matching-plain'
  | 'matching-reading'
  | 'matching-audio'

/** Mashqning media ko'rinishi — turdan kelib chiqadi: audio, rasm yoki IKKALASI ("both"). */
export type MediaMode = 'none' | 'audio' | 'image' | 'both'

export interface Option {
  id: string
  text: string
  /** "Rasmli variantlar" testi uchun — variantning rasmi. */
  imageUrl?: string
}

/** Gap tuzish (so'z tartibi / audio / rasm) — bitta gap. */
export interface SentenceItem {
  id: string
  text: string
  translation?: string
  audioUrl?: string
  audioName?: string
  imageUrl?: string
}

/** Gap tuzish · variant tanlash — savol + variant gaplar. */
export interface ChoiceItem {
  id: string
  prompt: string
  options: Option[]
  correctId: string | null
}

/** Bo'sh joyni to'ldirish — matnda `___` bo'sh joylari. */
export interface FillItem {
  id: string
  text: string
  translation?: string
  /** "Variant tanlash" rejimi uchun. */
  options: Option[]
  correctId: string | null
  /** "So'z yozish" rejimi uchun — to'g'ri javob(lar), "/" bilan ajratiladi. */
  answer: string
  audioUrl?: string
  audioName?: string
  imageUrl?: string
}

/** So'z tanlash — gap ichida `(bir/*ikki)` ko'rinishidagi variantlar. */
export interface WordPickItem {
  id: string
  text: string
  translation?: string
  audioUrl?: string
  audioName?: string
  imageUrl?: string
}

/** So'z topish — `___` bo'sh joylari + to'g'ri javoblar + chalg'ituvchi so'zlar. */
export interface WordFindItem {
  id: string
  text: string
  translation?: string
  answers: string[]
  distractors: string[]
  audioUrl?: string
  audioName?: string
  imageUrl?: string
}

/** Reading savoli (variant / to'g'ri-xato / bo'sh joy / qisqa javob). */
export interface ReadingItem {
  id: string
  q: string
  options: Option[]
  correctId: string | null
  /** Bo'sh joy / qisqa javob uchun — to'g'ri javob(lar), "/" bilan ajratiladi. */
  answer: string
}

/** Test savoli (rasmli / rasmli variantlar / audio). */
export interface TestItem {
  id: string
  q: string
  explain?: string
  options: Option[]
  correctId: string | null
  imageUrl?: string
  audioUrl?: string
  audioName?: string
}

/** Moslashtirish qatori (chap ustun) — `key` to'g'ri ustun indeksi. */
export interface MatchRow {
  id: string
  text: string
  key: number
}

// ---- Mashq mazmuni (turga qarab) ----

export interface SentenceData {
  items: SentenceItem[]
}
export interface SentenceChoiceData {
  items: ChoiceItem[]
}
export interface FillData {
  blank: 'line' | 'dots'
  items: FillItem[]
}
export interface WordPickData {
  items: WordPickItem[]
}
export interface WordFindData {
  blank: 'line' | 'dots'
  items: WordFindItem[]
}
export interface ReadingData {
  passage: string
  items: ReadingItem[]
}
export interface TestData {
  items: TestItem[]
}
export interface WritingData {
  topic: string
  prompt: string
  minWords: number
  minutes: number
  hints: string[]
}
export interface SpeakingData {
  topic: string
  prompt: string
  prepSec: number
  speakSec: number
  hints: string[]
}
export interface MatchingData {
  statement: string
  passage: string
  audioUrl?: string
  audioName?: string
  startNum: number
  colCount: number
  /** Ustun (A, B, C...) ma'nolari — indeks bo'yicha. */
  colLabels: Record<number, string>
  rows: MatchRow[]
}

/** Bitta topshiriqda saqlanadigan to'liq mashq. `kind` — tur, qolgani turga mos bo'lim.
 *  Til tanlash YO'Q — mashq bitta tilda yoziladi (markaz o'z tilida). */
export interface ExerciseData {
  kind: ExerciseKind
  sentence?: SentenceData
  sentenceChoice?: SentenceChoiceData
  fill?: FillData
  wordpick?: WordPickData
  wordfind?: WordFindData
  reading?: ReadingData
  test?: TestData
  writing?: WritingData
  speaking?: SpeakingData
  matching?: MatchingData
}

// ============================ Yordamchilar ============================

let seq = 0
/** Barqaror mahalliy id (JSON ichida saqlanadi). */
export function uid(prefix = 'x'): string {
  seq += 1
  return `${prefix}${Date.now().toString(36)}${seq.toString(36)}`
}

/** Turning qaysi guruhga tegishli ekani (tahrirlovchini tanlash uchun). */
export function kindFamily(kind: ExerciseKind) {
  if (kind === 'sentence-choice') return 'sentence-choice' as const
  if (kind.startsWith('sentence')) return 'sentence' as const
  if (kind.startsWith('fill')) return 'fill' as const
  if (kind.startsWith('wordpick')) return 'wordpick' as const
  if (kind.startsWith('wordfind')) return 'wordfind' as const
  if (kind.startsWith('reading')) return 'reading' as const
  if (kind.startsWith('test')) return 'test' as const
  if (kind === 'writing') return 'writing' as const
  if (kind === 'speaking') return 'speaking' as const
  return 'matching' as const
}

/** Turdagi media rejimi: audio, rasm yoki ikkalasi. */
export function kindMedia(kind: ExerciseKind): MediaMode {
  if (kind.endsWith('-media')) return 'both'
  if (kind.endsWith('-audio') || kind === 'sentence-audio') return 'audio'
  if (kind.endsWith('-image') || kind === 'sentence-image') return 'image'
  return 'none'
}

/** Bo'sh joyni to'ldirishda javob rejimi: variant tanlash yoki so'z yozish. */
export function fillMode(kind: ExerciseKind): 'choose' | 'write' {
  return kind === 'fill-choose' ? 'choose' : 'write'
}

/** Bo'sh joy belgisi ("___" yoki "···") — matnda ko'rsatish uchun. */
export function blankGlyph(blank: 'line' | 'dots'): string {
  return blank === 'dots' ? '···' : '___'
}

/** Gapni so'zlarga ajratadi (bo'sh joy bo'yicha). */
export function words(text: string): string[] {
  return text.trim().split(/\s+/).filter(Boolean)
}

/** Matnni `___` bo'yicha bo'laklarga ajratadi: ["Men har kuni ", " ichaman"] — bo'shliqlar soni = length−1. */
export function splitBlanks(text: string): string[] {
  return text.split('___')
}

/** Matndagi bo'sh joylar soni. */
export function blankCount(text: string): number {
  return Math.max(0, splitBlanks(text).length - 1)
}

/** "/" bilan ajratilgan to'g'ri javoblardan biriga mos keladimi (registr/bo'shliqqa e'tiborsiz). */
export function answerMatches(answer: string, typed: string): boolean {
  const t = typed.trim().toLowerCase()
  if (!t) return false
  return answer
    .split('/')
    .map((a) => a.trim().toLowerCase())
    .filter(Boolean)
    .includes(t)
}

/** So'z tanlash gapi: "Men (bir/*ikki) olma yedim" → matn bo'laklari va variant guruhlari. */
export interface PickToken {
  kind: 'text' | 'group'
  text: string
  /** group uchun — variantlar; `correct` — yulduzcha bilan belgilangani. */
  options?: { text: string; correct: boolean }[]
  groupIndex?: number
}

export function parsePickText(text: string): PickToken[] {
  const out: PickToken[] = []
  const re = /\(([^)]*)\)/g
  let last = 0
  let gi = 0
  let m: RegExpExecArray | null
  while ((m = re.exec(text)) !== null) {
    if (m.index > last) out.push({ kind: 'text', text: text.slice(last, m.index) })
    const options = m[1]
      .split('/')
      .map((raw) => raw.trim())
      .filter(Boolean)
      .map((raw) => ({ text: raw.replace(/^\*/, '').trim(), correct: raw.startsWith('*') }))
    out.push({ kind: 'group', text: m[0], options, groupIndex: gi++ })
    last = m.index + m[0].length
  }
  if (last < text.length) out.push({ kind: 'text', text: text.slice(last) })
  return out
}

/** So'z tanlash gapida nechta variant guruhi bor. */
export function pickGroupCount(text: string): number {
  return parsePickText(text).filter((t) => t.kind === 'group').length
}

/** Ustun harfi: 0 → A, 1 → B ... */
export function colLetter(i: number): string {
  return String.fromCharCode(65 + i)
}

// ============================ Bo'sh (default) mazmun ============================

/** Tur tanlanganda ochiladigan boshlang'ich mazmun — namuna yozuvsiz, faqat tuzilma. */
export function emptyExercise(kind: ExerciseKind): ExerciseData {
  const base: ExerciseData = { kind }
  switch (kindFamily(kind)) {
    case 'sentence':
      return { ...base, sentence: { items: [] } }
    case 'sentence-choice':
      return { ...base, sentenceChoice: { items: [] } }
    case 'fill':
      return { ...base, fill: { blank: 'line', items: [] } }
    case 'wordpick':
      return { ...base, wordpick: { items: [] } }
    case 'wordfind':
      return { ...base, wordfind: { blank: 'line', items: [] } }
    case 'reading':
      return { ...base, reading: { passage: '', items: [] } }
    case 'test':
      return { ...base, test: { items: [] } }
    case 'writing':
      return { ...base, writing: { topic: '', prompt: '', minWords: 60, minutes: 15, hints: [] } }
    case 'speaking':
      return { ...base, speaking: { topic: '', prompt: '', prepSec: 30, speakSec: 90, hints: [] } }
    default:
      return {
        ...base,
        matching: {
          statement: "Quyidagilarni moslang. To'g'ri harfni tanlang.",
          passage: '',
          startNum: 1,
          colCount: 4,
          colLabels: {},
          rows: [],
        },
      }
  }
}

/** Saqlangan JSON'ni o'qish — buzilgan/eski bo'lsa bo'sh mazmun qaytadi (hech qachon crash emas). */
export function parseExercise(kind: string, json: string): ExerciseData | null {
  if (!kind) return null
  const k = kind as ExerciseKind
  if (!json) return emptyExercise(k)
  try {
    const parsed = JSON.parse(json) as ExerciseData
    if (!parsed || typeof parsed !== 'object') return emptyExercise(k)
    // Tur o'zgargan bo'lsa (masalan boshqa turga almashtirilgan) — bo'sh mazmun.
    if (parsed.kind !== k) return emptyExercise(k)
    return { ...emptyExercise(k), ...parsed }
  } catch {
    return emptyExercise(k)
  }
}

/** Mashqda kamida bitta element bormi (saqlash tugmasi va "tayyor" belgisi uchun). */
export function exerciseCount(data: ExerciseData): number {
  switch (kindFamily(data.kind)) {
    case 'sentence':
      return data.sentence?.items.length ?? 0
    case 'sentence-choice':
      return data.sentenceChoice?.items.length ?? 0
    case 'fill':
      return data.fill?.items.length ?? 0
    case 'wordpick':
      return data.wordpick?.items.length ?? 0
    case 'wordfind':
      return data.wordfind?.items.length ?? 0
    case 'reading':
      return data.reading?.items.length ?? 0
    case 'test':
      return data.test?.items.length ?? 0
    case 'writing':
      return data.writing?.topic.trim() ? 1 : 0
    case 'speaking':
      return data.speaking?.topic.trim() ? 1 : 0
    default:
      return data.matching?.rows.length ?? 0
  }
}
