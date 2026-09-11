/**
 * MASHQ TURLARI KATALOGI — "Topshiriq yaratish" ekranidagi kategoriyalar, turlar va ularning
 * mini "foydalanuvchi ko'rinishi" previewlari. Dizayn maketidagi tuzilma va ranglar aynan
 * saqlangan (kartalar, badge'lar, ikonlar).
 */
import type { CSSProperties, ReactNode } from 'react'
import type { LessonType } from '@/types'
import type { ExerciseKind } from './model'

// ============================ Dizayn tokenlari ============================

export const UI = {
  /** Ish maydoni foni — CRM'ning yumshoq yuzasi (bg-2). */
  page: '#f7f5f1',
  /** Sarlavha paneli — OQ (CRM modallari kabi); ilgari deyarli qora edi. */
  bar: '#ffffff',
  barMuted: '#777a82',
  /** YAGONA aksent — CRM brand-600 (violet). Kategoriyalar uchun alohida rang YO'Q. */
  accent: '#5d53cb',
  accentSoft: '#eef0ff',
  ink: '#181a22',
  muted: '#777a82',
  line: '#e3e4e8',
  panel: '#fbfaf7',
  ok: '#169f65',
  danger: '#de3b3d',
} as const

/** Sarlavhalar — CRM global shrifti (Pliant), faqat harflar orasi zichroq. */
export const display: CSSProperties = { fontFamily: 'var(--font-sans)', letterSpacing: '-0.01em' }
export const sans: CSSProperties = { fontFamily: 'var(--font-sans)' }

/** Bitta mashq turining rang sxemasi — maketdagi qiymatlar (aksent, telefon ramkasi, yumshoq fon). */
export interface Theme {
  accent: string
  /** Telefon ramkasi foni va chegarasi. */
  phone: string
  phoneBorder: string
  /** Sarlavhadagi ikon kvadrati foni. */
  head: string
  /** "Gapni tuzing" kabi kichik sarlavha rangi. */
  caption: string
  /** Izoh/tarjima blokining foni. */
  soft: string
  /** Ichki ajratuvchi chiziq. */
  line: string
}

/** YAGONA rang sxemasi — CRM dizayn tokenlari (brand-600 + neytral yuzalar). Ilgari har
 *  kategoriyaning o'z rangi bor edi (binafsha/ko'k/yashil/sariq...) va juda rang-barang chiqardi;
 *  endi barchasi bitta CRM sxemasida. Struktura saqlangan — kerak bo'lsa alohida turga rang
 *  berish oson. */
const CRM_THEME: Theme = {
  accent: '#5d53cb',
  phone: '#fbfaf7',
  phoneBorder: '#e3e4e8',
  head: '#eef0ff',
  caption: '#8582f0',
  soft: '#f5f4fc',
  line: '#eceef2',
}

export const THEMES: Record<string, Theme> = {
  sentence: CRM_THEME,
  'sentence-choice': CRM_THEME,
  fill: CRM_THEME,
  wordpick: CRM_THEME,
  wordfind: CRM_THEME,
  reading: CRM_THEME,
  test: CRM_THEME,
  writing: CRM_THEME,
  speaking: CRM_THEME,
  matching: CRM_THEME,
}

/** Har kategoriyaning o'z aksent rangi — maketlardagi ranglar. */
export const ACCENTS: Record<string, string> = Object.fromEntries(
  Object.entries(THEMES).map(([k, v]) => [k, v.accent]),
)

// ============================ Ikonlar (maketdagi svg path'lar) ============================

const ICON_PATHS: Record<string, ReactNode> = {
  blocks: (
    <>
      <rect x="2" y="6" width="6" height="6" rx="1.5" />
      <rect x="10" y="6" width="5" height="6" rx="1.5" />
      <rect x="17" y="6" width="5" height="6" rx="1.5" />
      <path d="M4 17h16" />
    </>
  ),
  play: (
    <>
      <path d="M11 5L6 9H2v6h4l5 4V5z" />
      <path d="M15.5 8.5a5 5 0 010 7" />
      <path d="M18.5 5.5a9 9 0 010 13" />
    </>
  ),
  image: (
    <>
      <rect x="3" y="4" width="18" height="16" rx="2" />
      <circle cx="8.5" cy="9" r="1.5" />
      <path d="M21 15l-5-5-8 8" />
    </>
  ),
  list: (
    <>
      <path d="M9 6h11M9 12h11M9 18h11" />
      <circle cx="4" cy="6" r="1.4" />
      <circle cx="4" cy="12" r="1.4" />
      <circle cx="4" cy="18" r="1.4" />
    </>
  ),
  edit: (
    <>
      <path d="M12 20h9" />
      <path d="M16.5 3.5a2.1 2.1 0 013 3L7 19l-4 1 1-4z" />
    </>
  ),
  wave: <path d="M4 12h4l2 5 4-10 2 5h4" />,
  search: (
    <>
      <circle cx="11" cy="11" r="7" />
      <path d="M21 21l-4.3-4.3" />
    </>
  ),
  link: (
    <>
      <path d="M7 8H4a2 2 0 00-2 2v0a2 2 0 002 2h3" />
      <path d="M17 12h3a2 2 0 012 2v0a2 2 0 01-2 2h-3" />
      <path d="M8 10h8" />
    </>
  ),
  check: (
    <>
      <circle cx="12" cy="12" r="9" />
      <path d="M8 12l2.5 2.5L16 9" />
    </>
  ),
  book: (
    <>
      <path d="M4 5a2 2 0 012-2h6v18H6a2 2 0 01-2-2z" />
      <path d="M20 5a2 2 0 00-2-2h-6v18h6a2 2 0 002-2z" />
    </>
  ),
  mic: (
    <>
      <rect x="9" y="3" width="6" height="11" rx="3" />
      <path d="M5 11a7 7 0 0014 0" />
      <path d="M12 18v3" />
    </>
  ),
  grid: (
    <>
      <rect x="3" y="3" width="7.5" height="7.5" rx="1.5" />
      <rect x="13.5" y="3" width="7.5" height="7.5" rx="1.5" />
      <rect x="3" y="13.5" width="7.5" height="7.5" rx="1.5" />
      <rect x="13.5" y="13.5" width="7.5" height="7.5" rx="1.5" />
    </>
  ),
  // "Boshqa" (eski) topshiriq turlari uchun ikonlar.
  video: (
    <>
      <rect x="2" y="5" width="14" height="14" rx="2.5" />
      <path d="M16 10l6-3v10l-6-3z" />
    </>
  ),
  file: (
    <>
      <path d="M14 3H7a2 2 0 00-2 2v14a2 2 0 002 2h10a2 2 0 002-2V8z" />
      <path d="M14 3v5h5" />
    </>
  ),
  note: <path d="M4 6h16M4 12h16M4 18h10" />,
}

export function Icon({ name, size = 20, color = '#fff', width = 2 }: { name: string; size?: number; color?: string; width?: number }) {
  return (
    <svg
      width={size}
      height={size}
      viewBox="0 0 24 24"
      fill="none"
      stroke={color}
      strokeWidth={width}
      strokeLinecap="round"
      strokeLinejoin="round"
    >
      {ICON_PATHS[name] ?? ICON_PATHS.list}
    </svg>
  )
}

// ============================ Katalog ============================

export type PreviewKind =
  | 'order' | 'audio' | 'image' | 'choice' | 'fill' | 'inline' | 'pool'
  | 'match' | 'reading' | 'writing' | 'speaking' | 'testimg' | 'testimgopts' | 'audioimage'

export interface ExerciseType {
  kind: ExerciseKind
  name: string
  desc: string
  preview: PreviewKind
  icon: string
}

export interface ExerciseCategory {
  id: string
  label: string
  title: string
  desc: string
  types: ExerciseType[]
}

export const CATEGORIES: ExerciseCategory[] = [
  {
    id: 'sentence',
    label: 'Make sentence',
    title: 'Gap tuzish turini tanlang',
    desc: "\"Make sentence\" bir nechta turga bo'linadi. Kerakli turini tanlab, gaplarni kiritishga o'tasiz.",
    types: [
      { kind: 'sentence-order', preview: 'order', name: "So'z tartibi", desc: "Aralashtirilgan so'zlardan to'g'ri gapni yig'ish.", icon: 'blocks' },
      { kind: 'sentence-audio', preview: 'audio', name: "Audio bo'yicha", desc: "Audioni tinglab, so'zlardan gap yig'ish.", icon: 'play' },
      { kind: 'sentence-image', preview: 'image', name: "Rasm bo'yicha", desc: "Rasmga qarab, so'zlardan gap yig'ish.", icon: 'image' },
      { kind: 'sentence-choice', preview: 'choice', name: 'Variant tanlash', desc: "Bir nechta gapdan to'g'risini tanlash.", icon: 'list' },
    ],
  },
  {
    id: 'fill',
    label: "Bo'sh joyni to'ldirish",
    title: "Bo'sh joyni to'ldirish turini tanlang",
    desc: "Gapdagi bo'sh joyni (___ yoki ···) to'ldirish. Besh xil turda mavjud.",
    types: [
      { kind: 'fill-choose', preview: 'choice', name: 'Variant tanlash', desc: "Bo'sh joyga variantlardan to'g'ri so'zni tanlash.", icon: 'list' },
      { kind: 'fill-write', preview: 'fill', name: "So'z yozish", desc: "Bo'sh joyga to'g'ri so'zni yozib qo'yish.", icon: 'edit' },
      { kind: 'fill-audio', preview: 'audio', name: "Audio bo'yicha", desc: "Audioni tinglab, bo'sh joyni to'ldirish.", icon: 'play' },
      { kind: 'fill-image', preview: 'image', name: "Rasm bo'yicha", desc: "Rasmga qarab, bo'sh joyni to'ldirish.", icon: 'image' },
      { kind: 'fill-media', preview: 'audioimage', name: 'Audio + rasm', desc: "Ham audio, ham rasm beriladi — ikkalasidan foydalanib bo'sh joyni to'ldirish.", icon: 'grid' },
    ],
  },
  {
    id: 'wordpick',
    label: "So'z tanlash",
    title: "So'z tanlash turini tanlang",
    desc: "Gap ichidagi variantlardan (bir/*ikki) to'g'ri so'zni tanlash.",
    types: [
      { kind: 'wordpick-plain', preview: 'inline', name: 'Oddiy gap', desc: "Gap ichidan to'g'ri so'zni tanlash.", icon: 'wave' },
      { kind: 'wordpick-image', preview: 'image', name: "Rasm bo'yicha", desc: "Rasmga qarab, so'zni tanlash.", icon: 'image' },
      { kind: 'wordpick-audio', preview: 'audio', name: "Audio bo'yicha", desc: "Audioni tinglab, so'zni tanlash.", icon: 'play' },
    ],
  },
  {
    id: 'wordfind',
    label: "So'z topish",
    title: "So'z topish turini tanlang",
    desc: "Gap beriladi, bir nechta so'zdan gapga mos tushadiganini topish.",
    types: [
      { kind: 'wordfind-plain', preview: 'pool', name: 'Oddiy gap', desc: "So'zlardan gapga mos tushadiganini topish.", icon: 'search' },
      { kind: 'wordfind-image', preview: 'image', name: "Rasm bo'yicha", desc: "Rasmga qarab, mos so'zni topish.", icon: 'image' },
      { kind: 'wordfind-audio', preview: 'audio', name: "Audio bo'yicha", desc: "Audioni tinglab, mos so'zni topish.", icon: 'play' },
    ],
  },
  {
    id: 'reading',
    label: 'Reading',
    title: 'Reading turini tanlang',
    desc: "Matn beriladi, foydalanuvchi o'qib savollarga javob beradi. Uch xil turda mavjud.",
    types: [
      { kind: 'reading-choice', preview: 'reading', name: 'Variant tanlash', desc: "Matn bo'yicha variantlardan to'g'risini tanlash.", icon: 'list' },
      { kind: 'reading-fill', preview: 'reading', name: "Bo'sh joyni to'ldirish", desc: "Matn asosida bo'sh joyni to'ldirish.", icon: 'edit' },
      { kind: 'reading-short', preview: 'reading', name: 'Qisqa javob', desc: "Matn bo'yicha savolga qisqa javob yozish.", icon: 'book' },
    ],
  },
  {
    id: 'test',
    label: 'Test',
    title: 'Test turini tanlang',
    desc: 'Savol va variantlar kiritiladi. Rasmli yoki oddiy ko\'rinishda.',
    types: [
      { kind: 'test-image', preview: 'testimg', name: 'Rasmli test', desc: "Savolga rasm qo'yiladi, variantlardan to'g'risi tanlanadi.", icon: 'image' },
      { kind: 'test-imageopts', preview: 'testimgopts', name: 'Rasmli variantlar', desc: 'Variantlar rasm ko\'rinishida beriladi.', icon: 'grid' },
      { kind: 'test-audio', preview: 'audio', name: "Audio bo'yicha", desc: "Audioni tinglab, variantlardan to'g'risi tanlanadi.", icon: 'play' },
    ],
  },
  {
    id: 'prod',
    label: 'Writing & Speaking',
    title: 'Writing & Speaking turini tanlang',
    desc: 'Mavzu beriladi, foydalanuvchi matn yozadi yoki gapirib javob beradi.',
    types: [
      { kind: 'writing', preview: 'writing', name: 'Writing', desc: "Mavzu bo'yicha matn yozish.", icon: 'edit' },
      { kind: 'speaking', preview: 'speaking', name: 'Speaking', desc: "Mavzu bo'yicha gapirib javob berish.", icon: 'mic' },
    ],
  },
  {
    id: 'match',
    label: 'Moslashtirish',
    title: 'Moslashtirish',
    desc: "Chap va o'ng ustundagi juftliklarni bog'lash (Matching question).",
    types: [
      { kind: 'matching-plain', preview: 'match', name: 'Moslashtirish', desc: "So'z va tarjimalarni juftlab bog'lash.", icon: 'link' },
      { kind: 'matching-reading', preview: 'match', name: 'Reading', desc: 'Matn beriladi, moslarini topib bog\'lash.', icon: 'book' },
      { kind: 'matching-audio', preview: 'match', name: "Audio bo'yicha", desc: "Audioni tinglab, moslarini topib bog'lash.", icon: 'play' },
    ],
  },
]

// ---- "Boshqa" kategoriyasi: ESKI (oddiy kontent) topshiriq turlari ----

/** Eski topshiriq turi — mashq emas, oddiy kontent bandi (video/matn/audio/PDF/lug'at/test). */
export interface LegacyType {
  lesson: LessonType
  name: string
  desc: string
  icon: string
}

/** Tur tanlash ekranidagi OXIRGI tab — ilgari "+ Topshiriq" modalida bo'lgan turlar shu yerda. */
export const OTHER_CATEGORY = {
  id: 'other',
  label: 'Boshqa',
  title: 'Boshqa topshiriq turlari',
  desc: "Interaktiv mashq emas, oddiy kontent bandlari: video, matn, audio, PDF, lug'at yoki oddiy test. Bir nechta nom kiritib, birdan bir nechtasini yaratish mumkin.",
  types: [
    { lesson: 'video', name: 'Video', desc: 'YouTube havolasi yoki yuklangan video dars.', icon: 'video' },
    { lesson: 'text', name: 'Matn', desc: "O'qish uchun matnli dars mazmuni.", icon: 'note' },
    { lesson: 'audio', name: 'Audio', desc: 'Tinglash uchun audio (MP3) dars.', icon: 'play' },
    { lesson: 'pdf', name: 'PDF', desc: "Yuklangan PDF hujjat (darslik, tarqatma).", icon: 'file' },
    { lesson: 'vocab', name: "Lug'at", desc: "So'z + tarjima juftliklari ro'yxati.", icon: 'book' },
    { lesson: 'test', name: 'Test', desc: 'Oddiy savol-variant testi (avtomatik tekshiriladi).', icon: 'check' },
  ] as LegacyType[],
}

const BY_KIND = new Map<ExerciseKind, { cat: ExerciseCategory; type: ExerciseType }>()
for (const cat of CATEGORIES) for (const type of cat.types) BY_KIND.set(type.kind, { cat, type })

/** Tur bo'yicha katalog yozuvi (nomi, tavsifi, kategoriyasi). */
export function kindInfo(kind: ExerciseKind) {
  return BY_KIND.get(kind)
}

/** "Gap tuzish · So'z tartibi" ko'rinishidagi to'liq nom. */
export function kindTitle(kind: ExerciseKind): string {
  const info = BY_KIND.get(kind)
  if (!info) return 'Mashq'
  return `${info.cat.label} · ${info.type.name}`
}

/** Turning rang sxemasi (oila bo'yicha — model.kindFamily bilan bir xil guruhlash). */
export function kindTheme(kind: ExerciseKind): Theme {
  if (kind === 'sentence-choice') return THEMES['sentence-choice']
  if (kind === 'writing') return THEMES.writing
  if (kind === 'speaking') return THEMES.speaking
  const family = kind.split('-')[0]
  return THEMES[family] ?? THEMES.sentence
}

/** Kategoriya (yoki tur) aksent rangi. */
export function kindAccent(kind: ExerciseKind): string {
  return kindTheme(kind).accent
}

// ============================ Mini previewlar (karta ichidagi) ============================

const chip = (text: string, i: number) => (
  <span
    key={i}
    style={{ fontSize: 11.5, fontWeight: 600, color: '#4a4d56', background: '#fff', border: '1.2px solid #e3e4e8', borderRadius: 6, padding: '4px 8px' }}
  >
    {text}
  </span>
)

const wave = (heights: number[], colors: string[]) => (
  <span style={{ display: 'flex', alignItems: 'flex-end', gap: 3, height: 22 }}>
    {heights.map((h, i) => (
      <span key={i} style={{ width: 3, height: h, background: colors[i % colors.length], borderRadius: 2 }} />
    ))}
  </span>
)

const imgIcon = (stroke: string, size = 24) => (
  <svg width={size} height={size} viewBox="0 0 24 24" fill="none" stroke={stroke} strokeWidth={1.8} strokeLinecap="round" strokeLinejoin="round">
    <rect x="3" y="4" width="18" height="16" rx="2" />
    <circle cx="8.5" cy="9" r="1.5" />
    <path d="M21 16l-5-5-9 9" />
  </svg>
)

/** Karta ichidagi "Foydalanuvchi ko'rinishi" mini maketi — maketdagi har bir variant. */
export function MiniPreview({ preview, kind }: { preview: PreviewKind; kind: ExerciseKind }) {
  switch (preview) {
    case 'order':
      return (
        <div style={{ padding: '12px 13px', display: 'flex', flexDirection: 'column', gap: 9 }}>
          <div style={{ fontSize: 11, fontStyle: 'italic', color: '#777a82' }}>"I go running every morning"</div>
          <div style={{ display: 'flex', gap: 5, borderBottom: '1.5px dashed #e3e4e8', paddingBottom: 9, flexWrap: 'wrap' }}>
            {['Men', 'har'].map((t, i) => (
              <span key={i} style={{ fontSize: 11.5, fontWeight: 600, color: '#fff', background: '#5d53cb', borderRadius: 6, padding: '4px 8px' }}>
                {t}
              </span>
            ))}
          </div>
          <div style={{ display: 'flex', gap: 5, flexWrap: 'wrap' }}>{['yugurishni', 'kuni', 'yaxshi', "ko'raman"].map(chip)}</div>
        </div>
      )
    case 'audio':
      return (
        <div style={{ padding: '12px 13px', display: 'flex', flexDirection: 'column', gap: 10 }}>
          <div style={{ display: 'flex', alignItems: 'center', gap: 9 }}>
            <span style={{ flex: 'none', width: 30, height: 30, borderRadius: '50%', background: '#5d53cb', display: 'flex', alignItems: 'center', justifyContent: 'center' }}>
              <svg width="13" height="13" viewBox="0 0 24 24" fill="#fff"><path d="M8 5v14l11-7z" /></svg>
            </span>
            {wave([8, 16, 22, 13, 18, 9, 15, 7], ['#cfd3ff', '#8582f0', '#5d53cb', '#8582f0', '#8582f0', '#cfd3ff', '#8582f0', '#cfd3ff'])}
          </div>
          <div style={{ display: 'flex', gap: 5, flexWrap: 'wrap', borderTop: '1.5px dashed #e3e4e8', paddingTop: 9 }}>
            {['kuni', 'har', 'Men', 'yaxshi'].map(chip)}
          </div>
        </div>
      )
    case 'image':
      return (
        <div style={{ padding: '12px 13px', display: 'flex', flexDirection: 'column', gap: 10 }}>
          <div style={{ height: 52, borderRadius: 9, background: 'linear-gradient(135deg,#eef0ff,#eef0ff)', display: 'flex', alignItems: 'center', justifyContent: 'center', border: '1px solid #eceef2' }}>
            {imgIcon('#cfd3ff')}
          </div>
          <div style={{ display: 'flex', gap: 5, flexWrap: 'wrap' }}>{['bola', 'olma', 'yemoqda'].map(chip)}</div>
        </div>
      )
    case 'choice':
      return (
        <div style={{ padding: '12px 13px', display: 'flex', flexDirection: 'column', gap: 8 }}>
          <div style={{ fontSize: 11, fontWeight: 600, color: '#4a4d56' }}>To'g'ri tarjimani tanlang:</div>
          <div style={{ display: 'flex', flexDirection: 'column', gap: 6 }}>
            {[
              { t: 'He runs slowly', on: false },
              { t: 'I run every morning', on: true },
              { t: 'She likes reading', on: false },
            ].map((o, i) => (
              <span
                key={i}
                style={{
                  display: 'flex', alignItems: 'center', gap: 8, fontSize: 11.5, borderRadius: 8, padding: '6px 9px',
                  fontWeight: o.on ? 600 : 400,
                  color: o.on ? '#5d53cb' : '#4a4d56',
                  background: o.on ? '#eef0ff' : '#fff',
                  border: o.on ? '1.4px solid #5d53cb' : '1.2px solid #e3e4e8',
                }}
              >
                <span style={{ width: 11, height: 11, borderRadius: '50%', border: o.on ? '3.5px solid #5d53cb' : '1.5px solid #cfd3ff', display: 'inline-block' }} />
                {o.t}
              </span>
            ))}
          </div>
        </div>
      )
    case 'fill':
      return (
        <div style={{ padding: '14px 13px', display: 'flex', flexDirection: 'column', gap: 11 }}>
          <div style={{ fontSize: 12, lineHeight: 1.6, color: '#4a4d56' }}>
            Bu <span style={{ borderBottom: '2px solid #5d53cb', padding: '0 14px', color: 'transparent' }}>x</span> juda qiziqarli
          </div>
          <div style={{ display: 'flex', alignItems: 'center', gap: 6, background: '#fbfaf7', border: '1.2px solid #e3e4e8', borderRadius: 8, padding: '7px 9px' }}>
            <span style={{ fontSize: 11.5, color: '#cfd3ff' }}>javobni yozing…</span>
            <span style={{ marginLeft: 'auto', width: 1.5, height: 13, background: '#5d53cb', display: 'inline-block' }} />
          </div>
        </div>
      )
    case 'inline':
      return (
        <div style={{ padding: '16px 13px', display: 'flex', flexDirection: 'column', gap: 8 }}>
          <div style={{ fontSize: 13, lineHeight: 2, color: '#4a4d56' }}>
            Men
            <span style={{ display: 'inline-flex', gap: 4, verticalAlign: 'middle', margin: '0 3px' }}>
              <span style={{ fontSize: 12, fontWeight: 700, borderRadius: 7, padding: '3px 9px', background: '#fff', border: '1.4px solid #e3e4e8', color: '#4a4d56' }}>bir</span>
              <span style={{ fontSize: 12, fontWeight: 700, borderRadius: 7, padding: '3px 9px', background: '#5d53cb', border: '1.4px solid #5d53cb', color: '#fff' }}>ikki</span>
            </span>
            olma yedim
          </div>
        </div>
      )
    case 'pool':
      return (
        <div style={{ padding: '14px 13px', display: 'flex', flexDirection: 'column', gap: 11 }}>
          <div style={{ fontSize: 12, lineHeight: 1.6, color: '#4a4d56' }}>
            Men har kuni{' '}
            <span style={{ display: 'inline-block', minWidth: 44, borderRadius: 6, background: '#eef0ff', border: '1.4px dashed #cfd3ff', verticalAlign: 'middle', height: 15 }} /> yaxshi
            ko'raman
          </div>
          <div style={{ display: 'flex', gap: 5, flexWrap: 'wrap' }}>
            {['yugurishni', 'uxlashni', 'kitobni'].map((t, i) => (
              <span key={i} style={{ fontSize: 11.5, fontWeight: 600, color: '#4a4d56', background: '#fff', border: '1.2px solid #cfd3ff', borderRadius: 6, padding: '4px 8px' }}>
                {t}
              </span>
            ))}
          </div>
        </div>
      )
    case 'audioimage':
      return (
        <div style={{ padding: '12px 13px', display: 'flex', flexDirection: 'column', gap: 9 }}>
          <div style={{ height: 44, borderRadius: 9, background: 'linear-gradient(135deg,#eef0ff,#fbfaf7)', display: 'flex', alignItems: 'center', justifyContent: 'center', border: '1px solid #e3e4e8' }}>
            {imgIcon('#8582f0', 20)}
          </div>
          <div style={{ display: 'flex', alignItems: 'center', gap: 8 }}>
            <span style={{ flex: 'none', width: 24, height: 24, borderRadius: '50%', background: '#5d53cb', display: 'flex', alignItems: 'center', justifyContent: 'center' }}>
              <svg width="11" height="11" viewBox="0 0 24 24" fill="#fff"><path d="M8 5v14l11-7z" /></svg>
            </span>
            <span style={{ display: 'flex', alignItems: 'flex-end', gap: 2.5, height: 16 }}>
              {[6, 12, 16, 9, 13, 7].map((h, i) => (
                <span key={i} style={{ width: 2.5, height: h, background: i % 2 === 0 ? '#cfd3ff' : '#5d53cb', borderRadius: 2 }} />
              ))}
            </span>
          </div>
          <div style={{ fontSize: 11.5, lineHeight: 1.5, color: '#4a4d56' }}>
            Bu <span style={{ borderBottom: '2px solid #5d53cb', padding: '0 12px', color: 'transparent' }}>x</span> juda qiziqarli
          </div>
        </div>
      )
    case 'testimg':
      return (
        <div style={{ padding: '12px 13px', display: 'flex', flexDirection: 'column', gap: 9 }}>
          <div style={{ height: 56, borderRadius: 9, background: 'linear-gradient(135deg,#eef0ff,#eef0ff)', display: 'flex', alignItems: 'center', justifyContent: 'center', border: '1px solid #e3e4e8' }}>
            {imgIcon('#8582f0')}
          </div>
          <div style={{ display: 'flex', flexDirection: 'column', gap: 5 }}>
            <span style={{ display: 'flex', alignItems: 'center', gap: 7, fontSize: 11, fontWeight: 600, color: '#5d53cb', background: '#eef0ff', border: '1.3px solid #5d53cb', borderRadius: 7, padding: '5px 8px' }}>
              <span style={{ width: 9, height: 9, borderRadius: '50%', border: '3px solid #5d53cb', display: 'inline-block' }} />
              Olma
            </span>
            <span style={{ display: 'flex', alignItems: 'center', gap: 7, fontSize: 11, color: '#4a4d56', background: '#fff', border: '1.2px solid #e3e4e8', borderRadius: 7, padding: '5px 8px' }}>
              <span style={{ width: 9, height: 9, borderRadius: '50%', border: '1.5px solid #cfd3ff', display: 'inline-block' }} />
              Banan
            </span>
          </div>
        </div>
      )
    case 'testimgopts':
      return (
        <div style={{ padding: '12px 13px', display: 'flex', flexDirection: 'column', gap: 9 }}>
          <div style={{ fontSize: 11, fontWeight: 600, color: '#181a22' }}>Qaysi rasmda olma bor?</div>
          <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: 6 }}>
            {[true, false, false, false].map((on, i) => (
              <span
                key={i}
                style={{
                  height: 38, borderRadius: 8, display: 'flex', alignItems: 'center', justifyContent: 'center',
                  background: on ? '#eef0ff' : '#fbfaf7',
                  border: on ? '2px solid #5d53cb' : '1.5px solid #e3e4e8',
                }}
              >
                <svg width="15" height="15" viewBox="0 0 24 24" fill="none" stroke={on ? '#5d53cb' : '#cfd3ff'} strokeWidth={1.9} strokeLinecap="round">
                  <rect x="3" y="4" width="18" height="16" rx="2" />
                  <path d="M21 16l-5-5-9 9" />
                </svg>
              </span>
            ))}
          </div>
        </div>
      )
    case 'writing':
      return (
        <div style={{ padding: '12px 13px', display: 'flex', flexDirection: 'column', gap: 9 }}>
          <div style={{ background: '#eef0ff', borderRadius: 8, padding: '8px 9px', display: 'flex', flexDirection: 'column', gap: 3 }}>
            <span style={{ fontSize: 9, fontWeight: 700, letterSpacing: '.05em', textTransform: 'uppercase', color: '#8582f0' }}>Mavzu</span>
            <span style={{ fontSize: 11.5, fontWeight: 700, color: '#181a22' }}>Mening yozgi ta'tilim</span>
          </div>
          <div style={{ background: '#fbfaf7', border: '1.3px solid #e3e4e8', borderRadius: 8, padding: '9px 10px', display: 'flex', flexDirection: 'column', gap: 4, minHeight: 44 }}>
            {[90, 75, 45].map((w, i) => (
              <span key={i} style={{ height: 5, width: `${w}%`, borderRadius: 3, background: '#e3e4e8', display: 'inline-block' }} />
            ))}
          </div>
        </div>
      )
    case 'speaking':
      return (
        <div style={{ padding: '12px 13px', display: 'flex', flexDirection: 'column', gap: 11 }}>
          <div style={{ background: '#eef0ff', borderRadius: 8, padding: '8px 9px', display: 'flex', flexDirection: 'column', gap: 3 }}>
            <span style={{ fontSize: 9, fontWeight: 700, letterSpacing: '.05em', textTransform: 'uppercase', color: '#8582f0' }}>Mavzu</span>
            <span style={{ fontSize: 11.5, fontWeight: 700, color: '#181a22' }}>Sevimli kitobingiz haqida</span>
          </div>
          <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'center', gap: 9 }}>
            <span style={{ width: 34, height: 34, borderRadius: '50%', background: '#5d53cb', display: 'flex', alignItems: 'center', justifyContent: 'center', flex: 'none' }}>
              <Icon name="mic" size={16} />
            </span>
            {wave([8, 16, 22, 13, 18, 9], ['#cfd3ff', '#8582f0', '#5d53cb', '#8582f0', '#8582f0', '#cfd3ff'])}
          </div>
        </div>
      )
    case 'reading':
      return (
        <div style={{ padding: '12px 13px', display: 'flex', flexDirection: 'column', gap: 9 }}>
          <div style={{ background: '#eef0ff', borderRadius: 8, padding: '8px 9px', display: 'flex', flexDirection: 'column', gap: 4 }}>
            {[100, 85, 60].map((w, i) => (
              <span key={i} style={{ height: 6, width: `${w}%`, borderRadius: 3, background: '#cfd3ff', display: 'inline-block' }} />
            ))}
          </div>
          <div style={{ fontSize: 11, fontWeight: 600, color: '#4a4d56' }}>
            {kind === 'reading-fill' ? 'Ali maktabga ___ boradi' : 'How does Ali go to school?'}
          </div>
          {kind === 'reading-choice' ? (
            <div style={{ display: 'flex', flexDirection: 'column', gap: 5 }}>
              <span style={{ display: 'flex', alignItems: 'center', gap: 7, fontSize: 11, fontWeight: 600, color: '#5d53cb', background: '#eef0ff', border: '1.3px solid #5d53cb', borderRadius: 7, padding: '5px 8px' }}>
                <span style={{ width: 9, height: 9, borderRadius: '50%', border: '3px solid #5d53cb', display: 'inline-block' }} />
                By bus
              </span>
              <span style={{ display: 'flex', alignItems: 'center', gap: 7, fontSize: 11, color: '#4a4d56', background: '#fff', border: '1.2px solid #e3e4e8', borderRadius: 7, padding: '5px 8px' }}>
                <span style={{ width: 9, height: 9, borderRadius: '50%', border: '1.5px solid #cfd3ff', display: 'inline-block' }} />
                By car
              </span>
            </div>
          ) : (
            /* Bo'sh joy / qisqa javob — variant emas, javob maydoni */
            <div style={{ display: 'flex', alignItems: 'center', gap: 6, background: '#fbfaf7', border: '1.2px solid #cfd3ff', borderRadius: 8, padding: '7px 9px' }}>
              <span style={{ fontSize: 11.5, color: '#9aa0aa' }}>javobni yozing…</span>
              <span style={{ marginLeft: 'auto', width: 1.5, height: 13, background: '#5d53cb', display: 'inline-block' }} />
            </div>
          )}
        </div>
      )
    case 'match':
    default: {
      const cell = { padding: 2, borderLeft: '1px solid #eceef2', borderTop: '1px solid #eceef2' } as CSSProperties
      const tick = (
        <span style={{ display: 'flex', alignItems: 'center', justifyContent: 'center', height: 16, borderRadius: 3, background: '#5d53cb', color: '#fff', fontSize: 9, fontWeight: 700 }}>✓</span>
      )
      return (
        <div style={{ padding: '11px 12px', display: 'flex', flexDirection: 'column', gap: 7 }}>
          {kind === 'matching-reading' && (
            <div style={{ background: '#eef0ff', borderRadius: 7, padding: '6px 8px', display: 'flex', flexDirection: 'column', gap: 3 }}>
              {[100, 72].map((w, i) => (
                <span key={i} style={{ height: 4, width: `${w}%`, borderRadius: 2, background: '#cfd3ff', display: 'inline-block' }} />
              ))}
            </div>
          )}
          {kind === 'matching-audio' && (
            <div style={{ display: 'flex', alignItems: 'center', gap: 7, background: '#eef0ff', borderRadius: 7, padding: '6px 8px' }}>
              <span style={{ flex: 'none', width: 18, height: 18, borderRadius: '50%', background: '#5d53cb', display: 'flex', alignItems: 'center', justifyContent: 'center' }}>
                <svg width="9" height="9" viewBox="0 0 24 24" fill="#fff"><path d="M8 5v14l11-7z" /></svg>
              </span>
              <span style={{ display: 'flex', alignItems: 'flex-end', gap: 2, height: 12 }}>
                {[5, 10, 12, 7].map((h, i) => (
                  <span key={i} style={{ width: 2, height: h, background: ['#cfd3ff', '#8582f0', '#5d53cb', '#8582f0'][i], borderRadius: 1 }} />
                ))}
              </span>
            </div>
          )}
          <table style={{ borderCollapse: 'collapse', width: '100%', border: '1px solid #eceef2', borderRadius: 6 }}>
            <tbody>
              <tr style={{ background: '#eef0ff' }}>
                <td style={{ padding: '4px 6px' }} />
                {['A', 'B', 'C'].map((l) => (
                  <td key={l} style={{ padding: '4px 0', textAlign: 'center', fontWeight: 700, fontSize: 9, color: '#777a82', borderLeft: '1px solid #eceef2', ...display }}>
                    {l}
                  </td>
                ))}
              </tr>
              {[
                { label: '1. apple', at: 0 },
                { label: '2. book', at: 1 },
              ].map((r) => (
                <tr key={r.label}>
                  <td style={{ padding: '3px 6px', fontSize: 9.5, fontWeight: 700, color: '#181a22', borderTop: '1px solid #eceef2', whiteSpace: 'nowrap' }}>{r.label}</td>
                  {[0, 1, 2].map((c) => (
                    <td key={c} style={cell}>
                      {r.at === c ? tick : <span style={{ display: 'block', height: 16 }} />}
                    </td>
                  ))}
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )
    }
  }
}

/** "Boshqa" kategoriyasidagi eski turlar uchun mini preview (hub kartalari ichida). */
export function LegacyPreview({ type }: { type: LessonType }) {
  const line = (w: number, i: number, color = '#e3e4e8') => (
    <span key={i} style={{ height: 6, width: `${w}%`, borderRadius: 3, background: color, display: 'inline-block' }} />
  )
  switch (type) {
    case 'video':
      return (
        <div style={{ padding: '12px 13px', display: 'flex', flexDirection: 'column', gap: 9 }}>
          <div style={{ height: 62, borderRadius: 9, background: 'linear-gradient(135deg,#181a22,#181a22)', display: 'flex', alignItems: 'center', justifyContent: 'center' }}>
            <span style={{ width: 30, height: 30, borderRadius: '50%', background: 'rgba(255,255,255,.92)', display: 'flex', alignItems: 'center', justifyContent: 'center' }}>
              <svg width="13" height="13" viewBox="0 0 24 24" fill="#181a22"><path d="M8 5v14l11-7z" /></svg>
            </span>
          </div>
          <div style={{ display: 'flex', alignItems: 'center', gap: 7 }}>
            <span style={{ flex: 1, height: 4, borderRadius: 3, background: '#e3e4e8', overflow: 'hidden' }}>
              <span style={{ display: 'block', width: '38%', height: '100%', background: '#5d53cb' }} />
            </span>
            <span style={{ fontSize: 9.5, fontWeight: 700, color: '#8582f0' }}>04:12</span>
          </div>
        </div>
      )
    case 'text':
      return (
        <div style={{ padding: '14px 13px', display: 'flex', flexDirection: 'column', gap: 7 }}>
          <span style={{ fontSize: 11, fontWeight: 700, color: '#4a4d56' }}>Dars matni</span>
          {[100, 92, 80, 96, 55].map((w, i) => line(w, i))}
        </div>
      )
    case 'audio':
      return (
        <div style={{ padding: '16px 13px', display: 'flex', alignItems: 'center', gap: 10 }}>
          <span style={{ flex: 'none', width: 32, height: 32, borderRadius: '50%', background: '#5d53cb', display: 'flex', alignItems: 'center', justifyContent: 'center' }}>
            <svg width="14" height="14" viewBox="0 0 24 24" fill="#fff"><path d="M8 5v14l11-7z" /></svg>
          </span>
          <span style={{ display: 'flex', alignItems: 'flex-end', gap: 3, height: 26 }}>
            {[10, 18, 26, 14, 22, 9, 17, 24, 12, 8].map((h, i) => (
              <span key={i} style={{ width: 3, height: h, background: i % 3 === 0 ? '#5d53cb' : '#cfd3ff', borderRadius: 2 }} />
            ))}
          </span>
        </div>
      )
    case 'pdf':
      return (
        <div style={{ padding: '12px 13px', display: 'flex', gap: 10, alignItems: 'flex-start' }}>
          <span style={{ flex: 'none', width: 40, height: 50, borderRadius: 7, background: '#fff', border: '1.4px solid #e3e4e8', display: 'flex', alignItems: 'flex-end', justifyContent: 'center', padding: 4 }}>
            <span style={{ fontSize: 8.5, fontWeight: 800, color: '#de3b3d', letterSpacing: '.04em' }}>PDF</span>
          </span>
          <span style={{ flex: 1, display: 'flex', flexDirection: 'column', gap: 6, paddingTop: 4 }}>
            {[95, 78, 60].map((w, i) => line(w, i))}
          </span>
        </div>
      )
    case 'vocab':
      return (
        <div style={{ padding: '12px 13px', display: 'flex', flexDirection: 'column', gap: 7 }}>
          {[
            ['hello', 'salom'],
            ['book', 'kitob'],
            ['water', 'suv'],
          ].map(([a, b], i) => (
            <span key={i} style={{ display: 'flex', alignItems: 'center', gap: 7, fontSize: 11, background: '#fbfaf7', border: '1px solid #eceef2', borderRadius: 7, padding: '5px 8px' }}>
              <span style={{ fontWeight: 700, color: '#4a4d56' }}>{a}</span>
              <span style={{ color: '#cfd3ff' }}>→</span>
              <span style={{ color: '#4a4d56' }}>{b}</span>
            </span>
          ))}
        </div>
      )
    default:
      return (
        <div style={{ padding: '12px 13px', display: 'flex', flexDirection: 'column', gap: 8 }}>
          <div style={{ fontSize: 11, fontWeight: 600, color: '#4a4d56' }}>2 + 2 = ?</div>
          <div style={{ display: 'flex', flexDirection: 'column', gap: 6 }}>
            {[
              { t: '3', on: false },
              { t: '4', on: true },
              { t: '5', on: false },
            ].map((o, i) => (
              <span
                key={i}
                style={{
                  display: 'flex', alignItems: 'center', gap: 8, fontSize: 11.5, borderRadius: 8, padding: '6px 9px',
                  fontWeight: o.on ? 600 : 400,
                  color: o.on ? '#0f7a4c' : '#4a4d56',
                  background: o.on ? '#e7f6ee' : '#fff',
                  border: o.on ? '1.4px solid #169f65' : '1.2px solid #e3e4e8',
                }}
              >
                <span style={{ width: 11, height: 11, borderRadius: '50%', border: o.on ? '3.5px solid #169f65' : '1.5px solid #cfd3ff', display: 'inline-block' }} />
                {o.t}
              </span>
            ))}
          </div>
        </div>
      )
  }
}
