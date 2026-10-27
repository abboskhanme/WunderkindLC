import type { ContactDue, ContactRequestItem } from '@/api/services/contacts'
import type { ContactStage } from '@/api/services/contactStages'
import type { StageColor } from '@/types'
import { stageColorKeys } from '@/config/stageColors'

/* ======================================================================================
 *  "BOG'LANISH KERAK" — MUDDAT GURUHLARI (kanban ustunlari)
 * ====================================================================================== */

/**
 * Ochiq talab qaysi MUDDAT guruhiga tushadi ("" — talab navbatda emas: done/failed).
 *
 * ⚠️ Bu serverdagi `ContactService.BucketOf` ning AYNAN nusxasi (`ContactAttemptModal` dagi
 * `nextSteps` `CanTransitionTo` ni takrorlagani kabi). Nusxa ATAYIN: kanban taxtasi BITTA
 * so'rovda kelgan ro'yxatni ustunlarga bo'ladi, server esa bir so'rovda faqat BITTA guruhni
 * qaytara oladi (`?due=`) — olti ustun uchun olti so'rov ortiqcha yuk bo'lardi.
 *
 * ⚠️ Serverdagi qoida o'zgarsa SHU YERNI ham o'zgartiring — `contactDue.test.ts` ikkisining
 * bir xilligini qulflaydi.
 *
 * @param today "yyyy-MM-dd" — sof funksiya bo'lishi uchun UZATILADI (ichkarida `new Date()` yo'q).
 */
export function bucketOf(status: string, dueDate: string, today: string): ContactDue | '' {
  // "Bog'lanish kerak" — sana yo'q, ya'ni "hoziroq navbatda turibdi".
  if (status === 'new') return 'nodate'
  if (status !== 'callback') return ''

  const due = (dueDate ?? '').trim()
  // Sanasiz "qayta qo'ng'iroq" bo'lmasligi kerak (server sanani talab qiladi), lekin eski yoki
  // qo'lda tuzatilgan yozuv shunday bo'lsa u YO'QOLIB ketmasin — sanasizlarga qo'shamiz.
  if (due.length === 0) return 'nodate'

  // "yyyy-MM-dd" leksikografik tartibi = xronologik tartib (server: `string.CompareOrdinal`).
  if (due < today) return 'overdue'
  if (due === today) return 'today'

  const days = dayDiff(due, today)
  // Buzuq sana ("2026-13-99") — `later` ga tushadi va navbatda baribir ko'rinadi.
  if (days == null) return 'later'
  return days === 1 ? 'tomorrow' : days <= 7 ? 'week' : 'later'
}

/** Ikki "yyyy-MM-dd" orasidagi kun farqi (`a - b`); biror sana buzuq bo'lsa `null`. */
function dayDiff(a: string, b: string): number | null {
  const ta = Date.parse(`${a}T00:00:00Z`)
  const tb = Date.parse(`${b}T00:00:00Z`)
  if (Number.isNaN(ta) || Number.isNaN(tb)) return null
  // Sana ISO bo'lsa ham "2026-02-31" kabi qatorni brauzer boshqa kunga SURIB yuboradi —
  // shuning uchun qaytib kelgan sana asl matnga mos kelishini tekshiramiz.
  if (new Date(ta).toISOString().slice(0, 10) !== a) return null
  if (new Date(tb).toISOString().slice(0, 10) !== b) return null
  return Math.round((ta - tb) / 86_400_000)
}

/** Shu guruh "BUGUN QILISH KERAK" ga kiradimi (server: `ContactService.IsTodo`). */
export function isTodo(bucket: ContactDue | ''): boolean {
  return bucket === 'overdue' || bucket === 'today' || bucket === 'nodate'
}

/* ======================================================================================
 *  USTUNLAR KATALOGI — taxta shu ro'yxatdan quriladi
 * ====================================================================================== */

export interface BoardColumn {
  /** Ustun kaliti — muddat guruhi yoki USTUN id'si (`ContactStage.id`). */
  key: string
  label: string
  hint: string
  color: StageColor
  /**
   * Karta shu ustunga SUDRALGANDA qaysi keyingi qadam taklif qilinadi (`null` — ustun
   * qabul qilmaydi). Taxta hech qachon holatni JIMGINA o'zgartirmaydi: sudrash faqat
   * "Bog'lanildi" oynasini shu qadam bilan OLDINDAN to'ldirib ochadi (§ ContactBoard).
   */
  drop: { nextStatus: 'callback' | 'done' | 'failed'; offsetDays?: number } | null
  /** Serverdagi ustun — FAQAT "Bosqich" rejimida bo'ladi (tahrirlash shu bilan ochiladi). */
  stage?: ContactStage
}

/**
 * MUDDAT bo'yicha ustunlar — operatorning asosiy savoli bosqich emas, VAQT
 * (`.claude/rules/contacts.md` §3.6). Birinchi UCHTASI = "bugun qilish kerak".
 */
export const DUE_COLUMNS: BoardColumn[] = [
  {
    key: 'overdue',
    label: "Muddati o'tgan",
    hint: 'Qayta qo\'ng\'iroq sanasi bugundan oldin — birinchi shular',
    color: 'rose',
    drop: { nextStatus: 'callback', offsetDays: 0 },
  },
  {
    key: 'today',
    label: 'Bugun',
    hint: 'Aynan bugunga rejalashtirilgan qayta qo\'ng\'iroqlar',
    color: 'amber',
    drop: { nextStatus: 'callback', offsetDays: 0 },
  },
  {
    key: 'nodate',
    label: 'Sanasiz',
    hint: 'Yangi talablar — sana belgilanmagan, hoziroq navbatda',
    // Bu ustun "Bog'lanish kerak" (new) holati: unga QAYTARIB bo'lmaydi
    // (server `CanTransitionTo` da `new` ATAYIN yo'q), shuning uchun `drop: null`.
    color: 'violet',
    drop: null,
  },
  {
    key: 'tomorrow',
    label: 'Ertaga',
    hint: 'Ertangi qayta qo\'ng\'iroqlar',
    color: 'blue',
    drop: { nextStatus: 'callback', offsetDays: 1 },
  },
  {
    key: 'week',
    label: 'Shu hafta',
    hint: 'Ertadan keyingi 6 kun (bugundan +2..+7)',
    color: 'cyan',
    drop: { nextStatus: 'callback', offsetDays: 3 },
  },
  {
    key: 'later',
    label: 'Keyinroq',
    hint: '7 kundan keyingi qayta qo\'ng\'iroqlar',
    color: 'slate',
    drop: { nextStatus: 'callback', offsetDays: 14 },
  },
]

/**
 * BOSQICH ustunlarining ZAXIRA ro'yxati — server ustunlarni bermasa (eski backend yoki
 * so'rov xatosi) taxta baribir ishlashi kerak. Odatda ustunlar SERVERDAN keladi va
 * `stageColumns()` bilan quriladi.
 *
 * ⚠️ "Bog'lanish kerak" ustuni karta QABUL QILMAYDI: bog'langandan keyin boshiga qaytarish
 * navbatni cheksiz aylantirardi (server `ContactService.CanTransitionTo` da `new` yo'q).
 */
export const STATUS_COLUMNS: BoardColumn[] = [
  {
    key: 'new',
    label: "Bog'lanish kerak",
    hint: 'Hali bir marta ham bog\'lanilmagan talablar',
    color: 'amber',
    drop: null,
  },
  {
    key: 'callback',
    label: "Qayta qo'ng'iroq",
    hint: 'Bog\'lanilgan, lekin yana qo\'ng\'iroq qilish kerak',
    color: 'blue',
    drop: { nextStatus: 'callback', offsetDays: 1 },
  },
  {
    key: 'done',
    label: "Hal bo'ldi",
    hint: 'Masala yopildi',
    color: 'emerald',
    drop: { nextStatus: 'done' },
  },
  {
    key: 'failed',
    label: "Bog'lanib bo'lmadi",
    hint: 'Natijasiz yopildi',
    color: 'rose',
    drop: { nextStatus: 'failed' },
  },
]

/* ======================================================================================
 *  SERVERDAN KELGAN USTUNLAR ("Bosqich" rejimi)
 * ====================================================================================== */

/**
 * Foydalanuvchi ustunlaridan taxta ustunlarini quradi.
 *
 * ⚠️ `drop` ustunning BAZAVIY HOLATIDAN kelib chiqadi: `new` ga o'tish serverda taqiqlangan,
 * shuning uchun `new` ga bog'langan ustun BOSHQA holatdagi kartani qabul qilmaydi. O'SHA
 * holatdagi kartani esa qabul qiladi (bu holat o'zgarishi emas, oddiy ustun ko'chirish) —
 * bu qaror `ContactBoard` da, sudralayotgan kartaga qarab beriladi.
 */
export function stageColumns(stages: ContactStage[]): BoardColumn[] {
  return stages.map((s) => ({
    key: s.id,
    label: s.title,
    hint: `Bazaviy bosqich: ${s.baseStatusLabel}`,
    color: (stageColorKeys as string[]).includes(s.color) ? s.color : 'slate',
    drop:
      s.baseStatus === 'new'
        ? null
        : {
            nextStatus: s.baseStatus as 'callback' | 'done' | 'failed',
            offsetDays: s.baseStatus === 'callback' ? 1 : undefined,
          },
    stage: s,
  }))
}

/**
 * Talab QAYSI ustunda turibdi ("Bosqich" rejimi) — serverdagi `ContactService.StageHomeOf` ning AYNAN nusxasi.
 *
 * ⚠️ Xavfsizlik tarmog'i: `stageId` bo'sh yoki o'chirilgan ustunga ishora qilsa, talab O'Z HOLATINING tizim
 * ustuniga tushadi (tizim ustunining id'si holat kalitining o'zi); u ham o'chirilgan (yashirilgan) bo'lsa — shu
 * holatga bog'langan BIRINCHI ustunga. Ochiq holatning oxirgi ustunini server o'chirtirmaydi, ya'ni ochiq karta
 * HECH QACHON taxtadan yo'qolmaydi. `''` — ustun yo'q (yakuniy holat, taxtada faqat ochiqlar).
 */
export function stageKeyOf(r: ContactRequestItem, columns: BoardColumn[]): string {
  if (r.stageId && columns.some((c) => c.key === r.stageId)) return r.stageId
  if (columns.some((c) => c.key === r.status)) return r.status
  return columns.find((c) => c.stage?.baseStatus === r.status)?.key ?? ''
}

/* ======================================================================================
 *  TALAB TURI — rang va filtr (2026-10-27, egasining talabi)
 * ====================================================================================== */

export type ContactKind = 'debt' | 'absence' | 'other'

/**
 * Kartadagi tur belgisi: yorliq, CHAP CHIZIQ rangi (`accent`) va belgi. ⚠️ Shoshilinchlik (muddati o'tgan — qizil FON va
 * muddat belgisi) bundan alohida qoladi: tur — "kim", shoshilinchlik — "qachon" (contacts.md §3.65).
 * `accent` `null` — tur rangi yo'q, chiziq shoshilinchlik rangida qoladi.
 */
export const CONTACT_KINDS: Record<ContactKind, { label: string; short: string; accent: string | null; badge: string }> = {
  debt: { label: 'Qarzdorlar', short: 'Qarzdor', accent: '#f59e0b', badge: 'bg-amber-100 text-amber-800' },
  absence: { label: 'Darsga kelmaganlar', short: 'Darsga kelmadi', accent: '#8b5cf6', badge: 'bg-violet-100 text-violet-800' },
  other: { label: 'Boshqa', short: '', accent: null, badge: '' },
}

/**
 * Talab qaysi turga kiradi: sabab id'si serverdagi avtomatik sabab bilan mos (meta `kinds`) — yoki, sabab katalogdan
 * o'chirilgan eski kartada, sabab MATNI mos. Qo'lda ochilgan boshqa sababli talab — `other`.
 */
export function requestKind(
  r: Pick<ContactRequestItem, 'reasonId' | 'reasonLabel'>,
  kinds: { kind: 'debt' | 'absence'; reasonId: string; label: string }[] | undefined,
): ContactKind {
  for (const k of kinds ?? []) {
    if ((k.reasonId && r.reasonId === k.reasonId) || (!r.reasonId && r.reasonLabel === k.label)) return k.kind
  }
  return 'other'
}
