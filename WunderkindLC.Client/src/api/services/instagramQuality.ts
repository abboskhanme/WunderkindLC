import { api } from '../client'

/**
 * MARKETING → JAVOB SIFATI JURNALI — admin API klienti
 * (`GET /api/admin/instagram/quality`, sinf darajasidagi `marketing` ruxsati).
 *
 * Modul bitta savolga javob beradi: <b>operator AI javobini qayerda va qanchalik tuzatadi</b>.
 * Aynan shu tuzatish promptni yaxshilashning eng ishonchli manbai — sonlar ("nechta javob
 * ketdi") mavjud analitikada bor, bu yerda esa MAZMUN.
 *
 * 🔴 MAXFIYLIK: javobda mijozning HECH QANDAY belgisi yo'q — na ismi, na Instagram ID'si,
 * na telefoni, na MIJOZ YOZGAN MATN, na `conversationId`. Faqat bizning ikki chiquvchi
 * matnimiz (AI taklifi va operator yuborgani), niyat, kanal va XODIM ismi. Bu ataylab
 * shunday: "kim bilan yozishilgani" savolining joyi — Inbox. Shu sababdan bu yerda
 * mijozga oid maydon SO'RALMAYDI ham, tiplarga qo'shilmaydi ham.
 */

/** Bo'sh qiymatlarni tashlab, faqat to'ldirilgan filtrlarni yuboradi (`instagram.ts` bilan bir xil). */
function clean(params: object): Record<string, unknown> {
  return Object.fromEntries(
    Object.entries(params).filter(([, v]) => v !== undefined && v !== null && v !== '' && v !== false),
  )
}

/** Bitta juftlik: AI nima taklif qilgan va operator nima yuborgan. */
export interface IgQualityPair {
  id: string
  /** `comment` | `dm` | `private_reply`. */
  channel: string
  /** AI aniqlagan niyat (`price_question`, `complaint`, …). */
  intent: string
  /** AI taklif qilgan matn. */
  aiText: string
  /** Operator haqiqatda yuborgan matn. */
  sentText: string
  /** 0..100 — 100 = matnlar aynan bir xil. */
  similarity: number
  /** Operator matnni o'zgartirganmi. */
  wasEdited: boolean
  /** Javobni yozgan XODIM ismi (mijoz emas). */
  actorName: string
  createdAt: string
}

/** Niyat kesimi. `avgSimilarity` FAQAT tahrirlanganlar bo'yicha. */
export interface IgQualityIntent {
  intent: string
  total: number
  edited: number
  avgSimilarity: number
}

/**
 * Hisobot.
 *
 * ⚠️ FILTRLARNING QAMROVI serverda ataylab har xil:
 * davr va kanal — hammasiga; niyat — jamlanma va lentaga (kesimga EMAS, chunki kesim
 * ayni paytda tanlagich); "faqat tahrirlanganlar" — faqat lentaga (aks holda "tahrir
 * ulushi" doim 100% bo'lardi).
 */
export interface IgQuality {
  from: string
  to: string
  /** Davrdagi barcha juftliklar (taklif AYNAN qabul qilingani ham kiradi). */
  total: number
  edited: number
  /** Taklif AYNAN yuborilgan holatlar. */
  kept: number
  editedPercent: number
  /** O'rtacha o'xshashlik — FAQAT tahrirlanganlar bo'yicha. */
  avgSimilarity: number
  byIntent: IgQualityIntent[]
  items: IgQualityPair[]
  /** Lenta filtrlariga mos kelgan JAMI juftliklar (`items` — ulardan `limit` tasi). */
  itemsTotal: number
  /** `true` — davrda qatorlar server chegarasidan (2000) oshgan, ekranda ochiq yoziladi. */
  truncated: boolean
}

export async function getIgQuality(params: {
  from?: string
  to?: string
  /** Niyat kaliti; bo'sh — hammasi. */
  intent?: string
  /** Kanal kaliti; bo'sh — hammasi. Noma'lum qiymat serverda jim tashlanadi. */
  channel?: string
  /** Lentada faqat tahrirlangan juftliklarni ko'rsatish. */
  onlyEdited?: boolean
  /** Lenta uzunligi (server chegarasi — 200). */
  limit?: number
}): Promise<IgQuality> {
  const { data } = await api.get<IgQuality>('/admin/instagram/quality', { params: clean(params) })
  return data
}
