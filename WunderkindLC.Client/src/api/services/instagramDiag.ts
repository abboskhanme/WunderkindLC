import { api } from '../client'

/**
 * MARKETING → SOZLAMALAR: **«Meta bilan aloqani tekshirish»** klienti.
 *
 * Sozlamalar saqlangandan keyin admin "ishladimi yoki yo'qmi" ni faqat bir necha KUN
 * KUTIB bilardi (lid kelmasa, post yiqilsa, statistika bo'sh chiqsa). Bu endpoint har
 * yoqilgan modul uchun Meta'ga bitta ENG YENGIL o'qish so'rovini yuboradi va natijani
 * "nima bo'ldi + nima qilish kerak" ko'rinishida qaytaradi.
 *
 * ⚠️ **Natija SAQLANMAYDI** — har bosishda yangisi (holat har daqiqada o'zgarishi
 * mumkin: token muddati tugaydi, ruxsat olib qo'yiladi).
 *
 * 🔴 **CAPI ATAYIN SINALMAYDI** — qarang: `IgDiagItem.checked`.
 *
 * 🔴 **MAXFIYLIK:** javobda token, secret yoki Dataset ID QIYMATI yo'q.
 */

/** Modul kaliti — backenddagi `IgDiagnostics` konstantalari bilan bir xil. */
export type IgDiagKey = 'account' | 'adLeads' | 'adsStats' | 'content' | 'capi'

/**
 * Bitta modulning natijasi.
 *
 * ⚠️ **Uchta bayroq — uchta boshqa savol**, ularni bitta "status"ga qisqartirmang:
 * `enabled` — modul yoqilganmi · `checked` — Meta'ga so'rov KETDIMI · `ok` — natija yaxshimi.
 */
export interface IgDiagItem {
  key: IgDiagKey
  /** Ekrandagi nom (o'zbekcha, serverdan keladi). */
  label: string
  /** Modul bayrog'i yoqilganmi. `false` — tekshirilmaydi. */
  enabled: boolean
  /**
   * Meta'ga HAQIQATAN so'rov ketdimi.
   *
   * 🔴 CAPI'da bu **doim `false`**: uni sinash Meta'ga HODISA yuborishni talab qiladi,
   * hodisa esa Events Manager statistikasiga tushib qoladi va uni qaytarib bo'lmaydi.
   * Shuning uchun `checked === false` bo'lgan qatorga **hech qachon yashil belgi
   * qo'yilmaydi** — sinalmagan modulni "ishlayapti" deb ko'rsatish eng yomon variant.
   */
  checked: boolean
  /**
   * Natija yaxshimi.
   *
   * ⚠️ `checked === false` bo'lganda bu "sozlama to'liqmi" degani, "ishlayapti" degani EMAS.
   */
  ok: boolean
  /** Nima bo'lgani — tayyor o'zbekcha jumla. */
  message: string
  /** NIMA QILISH kerak. Hammasi joyida bo'lsa — bo'sh satr. */
  hint: string
}

/** Butun tekshiruv natijasi. */
export interface IgDiagResult {
  /** Tekshiruv vaqti (markaz mintaqasi, ISO). */
  checkedAt: string
  total: number
  /** Tekshirilgan VA muvaffaqiyatli. */
  okCount: number
  /** Tekshirilgan VA nosoz. */
  failCount: number
  /** Umuman tekshirilmagan: o'chirilgan · sozlanmagan · CAPI. */
  skippedCount: number
  items: IgDiagItem[]
}

/**
 * Barcha yoqilgan modullar bo'yicha aloqani tekshiradi.
 *
 * ⚠️ **POST, GET emas** — amal tashqi so'rov yuboradi (Meta rate-limitini yeydi), shuning
 * uchun serverda `marketing.settings` ruxsati talab qilinadi.
 *
 * ⚠️ Bir necha soniya davom etishi mumkin (har modul uchun 20 soniyagacha) — tugma shu
 * vaqtda `disabled` bo'lishi kerak.
 */
export async function checkMetaConnection(): Promise<IgDiagResult> {
  const { data } = await api.post<IgDiagResult>('/admin/instagram/diagnostics/check')
  return data
}
