import { api } from '../client'

/**
 * KITOBLAR SOTUVI — "O'quv bo'limi → Kitoblar sotuvi" bo'limi API'si.
 * Buyurtmalar botdan (Telegram) tushadi; admin ularni tasdiqlaydi/rad etadi.
 * Tasdiqlanganda ombor qoldig'idan ayiriladi va sotuv analitikaga tushadi.
 */

/**
 * To'lov turi. `credit` = NASIYA: kitob berildi, pul keyin olinadi (faqat markazda qo'lda
 * sotuvda — botda yo'q). Nasiya sotuv ham odatdagidek tasdiqlanadi va qoldiqdan ayiriladi;
 * pul olinganda "Nasiya" tabidan "To'landi" bosiladi va summa tushumga qo'shiladi.
 */
export type BookPaymentMethod = 'cash' | 'card' | 'credit'
/** Nasiya qanday yopilgani (pul qaysi ko'rinishda olingani). */
export type BookSettleMethod = 'cash' | 'card'
export type BookOrderStatus = 'pending' | 'approved' | 'rejected'
/**
 * Ro'yxat filtri: holatlar + `returned` — QAYTARILGAN sotuvlar kesimi.
 * `returned` holat EMAS: qaytarish buyurtmani "approved" holida qoldiradi (qisman ham bo'ladi),
 * shuning uchun bu faqat filtr qiymati.
 */
export type BookOrderStatusFilter = BookOrderStatus | 'returned'
/** Buyurtma manbai: botdan tushgan yoki markazda qo'lda sotilgan. */
export type BookOrderSource = 'bot' | 'manual'
/** Ombor harakati turi: boshlang'ich qoldiq | kirim | sotuv | QAYTARISH | qo'lda korreksiya */
export type BookStockReason = 'initial' | 'restock' | 'sale' | 'return' | 'correction'

export interface Book {
  id: string
  title: string
  author: string
  description: string
  coverUrl: string
  price: number
  /** Joriy ombor qoldig'i (ostatka) */
  stock: number
  /** Botda ko'rinadimi */
  isActive: boolean
  /** Tasdiqlangan buyurtmalarda sotilgan dona */
  soldQty: number
  /** Tasdiqlangan sotuvlardan tushum */
  soldTotal: number
  /** Kutilayotgan buyurtmalardagi dona (rezerv emas — ogohlantirish uchun) */
  pendingQty: number
  createdAt: string
  createdBy: string
}

export interface BookPayload {
  title: string
  price: number
  author?: string
  description?: string
  coverUrl?: string
  isActive: boolean
  /** FAQAT yaratishda: boshlang'ich qoldiq (kirim tarixiga "initial" bo'lib yoziladi) */
  initialStock?: number
}

export interface BookStockMove {
  id: string
  bookId: string
  bookTitle: string
  /** Musbat = kirim, manfiy = chiqim (sotuv/korreksiya) */
  qty: number
  reason: BookStockReason
  orderId?: string | null
  note: string
  /** Shu harakatdan keyingi qoldiq */
  stockAfter: number
  createdAt: string
  createdBy: string
}

export interface BookOrder {
  id: string
  number: number
  customerName: string
  phone: string
  studentId?: string | null
  studentName?: string | null
  bookId: string
  bookTitle: string
  unitPrice: number
  qty: number
  total: number
  paymentMethod: BookPaymentMethod
  /** Karta to'lovida mijoz yuborgan chek (`/uploads/...`); naqdda bo'sh */
  receiptUrl: string
  status: BookOrderStatus
  rejectReason: string
  createdAt: string
  decidedAt?: string | null
  decidedBy: string
  /** Kitobning JORIY qoldig'i — tasdiqlash mumkinligini ko'rish uchun */
  bookStock: number
  /** Manba: 'bot' — mijoz o'zi buyurtma bergan; 'manual' — markazda qo'lda sotilgan */
  source?: BookOrderSource
  /** Karta to'lovida kartaning oxirgi 4 raqami ("1234"). Qo'lda sotuvda kiritiladi. */
  cardLast4?: string | null
  /** Karta to'lovi qilingan vaqt ("HH:mm"). Sana — `createdAt`. */
  paidTime?: string | null
  /** Pul olinganmi. Naqd/kartada tasdiqlangani = to'langani; nasiyada — "To'landi" bosilganda. */
  isPaid: boolean
  /** NASIYA: va'da qilingan to'lov sanasi ("yyyy-MM-dd"). */
  dueDate?: string | null
  /** NASIYA: muddat o'tib ketgan va hali to'lanmagan. */
  isOverdue: boolean
  /** Pul qachon olindi ("yyyy-MM-ddTHH:mm:ss") va kim qabul qildi. */
  paidAt?: string | null
  paidBy: string
  /** NASIYA qanday yopildi: naqd yoki karta. */
  settledMethod?: BookSettleMethod | null
  /** QAYTARISH: shu sotuvdan jami qaytarilgan dona (0 = qaytarilmagan). */
  returnedQty: number
  returnedAt?: string | null
  returnedBy: string
  returnReason: string
  /** Mijozga haqiqatan qaytarilgan pul (to'lanmagan nasiyada 0 — u yerda qarz kamaygan). */
  refundedAmount: number
  /** SOF summa = total − qaytarilganlar qiymati. Hisobotlarda AYNAN shu ishlatiladi. */
  netTotal: number
}

/** Qo'lda sotuv oynasidagi o'quvchi qidiruvi natijasi. */
export interface BookStudent {
  id: string
  fullName: string
  phone: string
  parentPhone: string
  className: string
  isArchived: boolean
}

/** Markazda qo'lda sotuv so'rovi (kitob → o'quvchi → soni → naqd/karta). */
export interface BookManualSalePayload {
  bookId: string
  /** Markazdagi o'quvchi — IXTIYORIY. Bo'sh = markazda o'qimaydigan xaridor. */
  studentId?: string | null
  /** O'quvchi tanlanmaganda xaridor ismi — bu ham ixtiyoriy (bo'sh = "Noma'lum"). */
  customerName?: string
  qty: number
  paymentMethod: BookPaymentMethod
  /** Karta to'lovida MAJBURIY — kartaning oxirgi 4 raqami. */
  cardLast4?: string
  /** Karta to'lovida MAJBURIY — to'lov qilingan vaqt ("HH:mm"). */
  paidTime?: string
  /** O'quvchi tanlanmaganda xaridor telefoni (nasiyada qarzdorni topish uchun). */
  customerPhone?: string
  /** NASIYADA: pul qaytarish uchun va'da qilingan sana ("yyyy-MM-dd", ixtiyoriy). */
  dueDate?: string
}

/** Nasiya to'lovini qabul qilish ("pulini oldim → Tasdiqlash"). Ombor tegilmaydi. */
export interface BookCreditPayPayload {
  method: BookSettleMethod
  /** Karta bo'lsa MAJBURIY — oxirgi 4 raqam. */
  cardLast4?: string
}

/**
 * Kunlik sotuv nuqtasi. ⚠️ Barcha sonlar SOF — qaytarilgan kitoblar AYIRILGAN va qaytarish
 * aynan SOTILGAN kunga yoziladi (aks holda "shu kuni nima sotildi" noto'g'ri chiqardi).
 */
export interface BookDaySales {
  date: string
  qty: number
  cash: number
  card: number
  /** Nasiyaga sotilgan summa (o'sha kuni) — keyin to'lansa ham shu kunda nasiya bo'lib qoladi */
  credit: number
  total: number
  /** Shu kunning sotuvlaridan keyinchalik qaytarilgani (yuqoridagi sonlar shu qadar kamaygan) */
  returnedQty: number
  returnedTotal: number
}

export interface BookSalesByBook {
  bookId: string
  bookTitle: string
  qty: number
  total: number
  stock: number
}

/** Har kuni qaysi kitob nechta sotilgani (kun × kitob kesimi). */
export interface BookDayBookSales {
  date: string
  bookId: string
  bookTitle: string
  qty: number
  total: number
  /** Shu kuni shu kitob bo'yicha nechta alohida sotuv bo'lgani */
  orders: number
  /** Shu kitobdan o'sha kungi sotuvlardan qaytarilgan dona */
  returnedQty: number
}

/**
 * Bitta sotuv — "qaysi kitob qachon (soati bilan) va kimga sotildi" lentasi uchun.
 * ⚠️ `qty`/`total` — sotuv PAYTIDAGI (xom) qiymat: lenta hodisalar tarixi, kunlik jamlanma esa
 * sof. Farqni `returnedQty` tushuntiradi.
 */
export interface BookSaleRow {
  id: string
  number: number
  soldAt: string
  bookId: string
  bookTitle: string
  qty: number
  total: number
  customerName: string
  paymentMethod: BookPaymentMethod
  isPaid: boolean
  source: BookOrderSource
  /** Shu sotuvdan keyinchalik qaytarilgan dona (0 = qaytarilmagan) */
  returnedQty: number
}

export interface BookAnalytics {
  from: string
  to: string
  ordersApproved: number
  ordersPending: number
  ordersRejected: number
  soldQty: number
  /** Davr ichidagi SOTUV summasi to'lov turi bo'yicha: naqd + karta + nasiya = revenueTotal */
  revenueCash: number
  revenueCard: number
  revenueTotal: number
  stockTotal: number
  stockInQty: number
  byDay: BookDaySales[]
  byBook: BookSalesByBook[]
  lowStock: BookSalesByBook[]
  /** Har kuni qaysi kitob sotilgani — TO'LIQ (chegarasiz) */
  byDayBook: BookDayBookSales[]
  /** Sotuvlar lentasi (soati bilan) — eng oxirgi 400 tasi */
  sales: BookSaleRow[]
  salesTruncated: boolean
  /** NASIYA: davr ichida nasiyaga sotilgani va shundan to'langani */
  creditSold: number
  creditSoldCount: number
  creditSoldPaid: number
  /** NASIYA: JORIY qarz — davrga bog'liq emas (ombor qoldig'i kabi) */
  creditOutstanding: number
  creditOutstandingCount: number
  creditOverdue: number
  creditOverdueCount: number
  /** NASIYA: davr ichida yig'ilgan pul (to'lov sanasi bo'yicha) */
  creditCollected: number
  creditCollectedCount: number
  /** QAYTARISH: davr SOTUVLARIDAN qaytarilgani (sotuv sanasi bo'yicha — sof raqamlar shu qadar kam) */
  returnedQty: number
  returnedTotal: number
  /** QAYTARISH: davr ICHIDA kassadan chiqqan pul (qaytarish sanasi bo'yicha) */
  refundedInPeriod: number
  refundedCount: number
}

/** Bitta qarzdor (nasiya bo'limida xaridor kesimi). */
export interface BookDebtor {
  key: string
  studentId?: string | null
  name: string
  phone: string
  orders: number
  total: number
  oldestDate: string
  hasOverdue: boolean
}

/**
 * NASIYA bo'limi. Jamlanma raqamlari (qarz, muddati o'tgan) — JORIY holat, davr va qidiruvdan
 * qat'i nazar; `collectedInPeriod` esa tanlangan davrda yig'ilgan pul (to'lov sanasi bo'yicha).
 */
export interface BookCredits {
  totalUnpaid: number
  countUnpaid: number
  totalOverdue: number
  countOverdue: number
  collectedInPeriod: number
  collectedCount: number
  debtors: BookDebtor[]
  orders: BookOrder[]
}

export interface BookSettings {
  bookSalesEnabled: boolean
  bookCardNumber: string
  bookCardHolder: string
  bookPaymentNote: string
}

export interface BookOrderFilters {
  status?: BookOrderStatusFilter | ''
  from?: string
  to?: string
  bookId?: string
  method?: BookPaymentMethod | ''
  q?: string
}

/**
 * Karta to'lovlari bo'limi — kartaga o'tkazma bilan to'langan buyurtmalar + jamlanma.
 * Jami summalar SERVERDA butun topilma bo'yicha hisoblanadi (`orders` ro'yxati ko'rsatish
 * uchun cheklangan bo'lishi mumkin, undan qo'shib chiqarish NOTO'G'RI bo'lardi).
 */
export interface BookCardPayments {
  /** Bo'lim bog'langan karta (Sozlamalar tabidan) — pul shu kartaga tushadi */
  cardNumber: string
  cardHolder: string
  /** Tasdiqlangan — kartaga hisoblangan pul */
  countApproved: number
  totalApproved: number
  /** Chek kelgan, admin hali tasdiqlamagan */
  countPending: number
  totalPending: number
  countRejected: number
  orders: BookOrder[]
}

/** Bo'sh qiymatlarni tashlab, faqat to'ldirilgan filtrlarni yuboradi. */
function clean(params: object): Record<string, unknown> {
  return Object.fromEntries(
    Object.entries(params).filter(([, v]) => v !== undefined && v !== null && v !== ''),
  )
}

// ---------- Ombor (kitoblar) ----------

export async function getBooks(activeOnly = false): Promise<Book[]> {
  const { data } = await api.get<Book[]>('/admin/books', { params: clean({ activeOnly }) })
  return data
}

export async function createBook(payload: BookPayload): Promise<Book> {
  const { data } = await api.post<Book>('/admin/books', payload)
  return data
}

export async function updateBook(id: string, payload: BookPayload): Promise<Book> {
  const { data } = await api.put<Book>(`/admin/books/${id}`, payload)
  return data
}

export async function deleteBook(id: string): Promise<void> {
  await api.delete(`/admin/books/${id}`)
}

/** Omborga kirim (qty > 0) yoki qo'lda ayirish (qty < 0). Har amal tarixga yoziladi. */
export async function addBookStock(id: string, qty: number, note?: string): Promise<Book> {
  const { data } = await api.post<Book>(`/admin/books/${id}/stock`, { qty, note })
  return data
}

/** Kitob muqovasini yuklaydi va `/uploads/...` URL'ini qaytaradi. */
export async function uploadBookCover(file: File): Promise<string> {
  const form = new FormData()
  form.append('file', file)
  const { data } = await api.post<{ url: string }>('/admin/books/cover', form, {
    headers: { 'Content-Type': 'multipart/form-data' },
  })
  return data.url
}

export async function getBookStockMoves(params: {
  from?: string
  to?: string
  bookId?: string
  onlyIn?: boolean
} = {}): Promise<BookStockMove[]> {
  const { data } = await api.get<BookStockMove[]>('/admin/books/stock-moves', { params: clean(params) })
  return data
}

// ---------- Buyurtmalar ----------

export async function getBookOrders(filters: BookOrderFilters = {}): Promise<BookOrder[]> {
  const { data } = await api.get<BookOrder[]>('/admin/books/orders', { params: clean(filters) })
  return data
}

/** Karta to'lovlari (chek rasmi bilan) + shu karta bo'yicha jamlanma. `method` yubormaymiz —
 *  server har doim faqat karta buyurtmalarini qaytaradi. */
export async function getBookCardPayments(
  filters: Omit<BookOrderFilters, 'method'> = {},
): Promise<BookCardPayments> {
  const { data } = await api.get<BookCardPayments>('/admin/books/card-payments', {
    params: clean(filters),
  })
  return data
}

// ---------- Qo'lda sotuv (markazda, joyida) ----------

/** O'quvchi qidiruvi (F.I.Sh yoki telefon, kamida 2 belgi). Bo'sh so'rovda server [] qaytaradi. */
export async function searchBookStudents(q: string): Promise<BookStudent[]> {
  const { data } = await api.get<BookStudent[]>('/admin/books/students', { params: { q } })
  return data
}

/** Markazda qo'lda sotish. Buyurtma DARHOL tasdiqlangan holatda yaratiladi — qoldiq shu
 *  zahoti ayiriladi va sotuv analitikaga tushadi. Qoldiq yetmasa server 400 qaytaradi. */
export async function sellBookManual(payload: BookManualSalePayload): Promise<BookOrder> {
  const { data } = await api.post<BookOrder>('/admin/books/orders/manual', payload)
  return data
}

/** Tab belgilari: kutilayotgan buyurtmalar + to'lanmagan nasiyalar (shundan muddati o'tganlari). */
export interface BookBadges {
  count: number
  credits: number
  overdue: number
}

export async function getBookBadges(): Promise<BookBadges> {
  const { data } = await api.get<BookBadges>('/admin/books/orders/pending-count')
  return data
}

/** Tasdiqlash: qoldiqdan ayiriladi + mijozga botda xabar ketadi. */
export async function approveBookOrder(id: string): Promise<BookOrder> {
  const { data } = await api.post<BookOrder>(`/admin/books/orders/${id}/approve`)
  return data
}

/** Rad etish: sabab mijozga botda yuboriladi. */
export async function rejectBookOrder(id: string, reason: string): Promise<BookOrder> {
  const { data } = await api.post<BookOrder>(`/admin/books/orders/${id}/reject`, { reason })
  return data
}

// ---------- Qaytarish (vozvrat) ----------

/** Sotilgan kitobni qaytarish: `qty` dona omborga qaytadi, `reason` — sabab (ixtiyoriy). */
export interface BookReturnPayload {
  qty: number
  reason?: string
}

/**
 * SOTILGAN KITOBNI QAYTARISH — naqd, karta va nasiya sotuvlari uchun.
 * Ombor qoldig'i oshadi va sotuv summasidan qaytarilgan qismi ayiriladi (barcha hisobotlar sof).
 * To'lanmagan nasiyada pul chiqmaydi — shunchaki QARZ kamayadi.
 * Qisman qaytarish mumkin; sotilganidan ko'p qaytarilsa server 400 qaytaradi.
 */
export async function returnBookOrder(id: string, payload: BookReturnPayload): Promise<BookOrder> {
  const { data } = await api.post<BookOrder>(`/admin/books/orders/${id}/return`, payload)
  return data
}

// ---------- Nasiya (kitob berildi, pul keyin) ----------

/** Nasiya ro'yxati. `status: 'paid'` — tanlangan davrda to'langanlari; aks holda to'lanmaganlar. */
export async function getBookCredits(
  filters: { status?: 'unpaid' | 'paid'; from?: string; to?: string; q?: string } = {},
): Promise<BookCredits> {
  const { data } = await api.get<BookCredits>('/admin/books/credits', { params: clean(filters) })
  return data
}

/** "Pulini oldim" → nasiya to'langan deb belgilanadi va summa tushumga qo'shiladi.
 *  Ombor TEGILMAYDI — kitob sotuv paytida berilgan. */
export async function payBookCredit(id: string, payload: BookCreditPayPayload): Promise<BookOrder> {
  const { data } = await api.post<BookOrder>(`/admin/books/orders/${id}/pay`, payload)
  return data
}

// ---------- Analitika ----------

export async function getBookAnalytics(from?: string, to?: string): Promise<BookAnalytics> {
  const { data } = await api.get<BookAnalytics>('/admin/books/analytics', { params: clean({ from, to }) })
  return data
}

// ---------- Sozlamalar (botdagi to'lov rekvizitlari) ----------

export async function getBookSettings(): Promise<BookSettings> {
  const { data } = await api.get<BookSettings>('/admin/books/settings')
  return data
}

export async function saveBookSettings(payload: BookSettings): Promise<BookSettings> {
  const { data } = await api.put<BookSettings>('/admin/books/settings', payload)
  return data
}

// ---------- Excel eksport ----------

async function download(url: string, params: object, fallbackName: string) {
  const res = await api.get(url, { params: clean(params), responseType: 'blob' })
  const cd = String(res.headers['content-disposition'] ?? '')
  const match = /filename\*?=(?:UTF-8'')?"?([^";]+)"?/i.exec(cd)
  const href = URL.createObjectURL(res.data as Blob)
  const a = document.createElement('a')
  a.href = href
  a.download = match?.[1] ?? fallbackName
  a.click()
  URL.revokeObjectURL(href)
}

const today = () => new Date().toISOString().slice(0, 10)

export const exportBookOrders = (filters: BookOrderFilters = {}) =>
  download('/admin/books/orders/export', filters, `kitob_sotuvlari_${today()}.xlsx`)

export const exportBookStockMoves = (
  params: { from?: string; to?: string; bookId?: string; onlyIn?: boolean } = {},
) => download('/admin/books/stock-moves/export', params, `kitob_ombor_${today()}.xlsx`)

export const exportBookAnalytics = (from?: string, to?: string) =>
  download('/admin/books/analytics/export', { from, to }, `kitob_hisobot_${today()}.xlsx`)

export const exportBookCredits = (
  filters: { status?: 'unpaid' | 'paid'; from?: string; to?: string; q?: string } = {},
) => download('/admin/books/credits/export', filters, `kitob_nasiya_${today()}.xlsx`)
