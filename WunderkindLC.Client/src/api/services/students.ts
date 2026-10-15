import type { Credentials, MonthStatus, Student, StudentBall, StudentLedger } from '@/types'
import { delay, uid } from '@/lib/utils'
import { localIsoDate } from '@/lib/localDate'
import { api, USE_MOCK } from '../client'
import { studentsMock } from '../mock/students'
import { classesMock } from '../mock/classes'
import { financeMock } from '../mock/finance'

/** Serverga yuklangan fayl haqida (admin uploads javobi). */
export interface UploadedFile {
  name: string
  url: string
  size: number
  contentType: string
}

/**
 * Barcha o'quvchilarni login/parol bilan Excel (.xlsx) ga yuklab oladi. Eksport — `HasFullAccess("students.list")`,
 * «Parol» ustuni esa FAQAT superadmin uchun (M2, 2026-10-12 — ommaviy parol dumpi; server hard rol bilan tekshiradi
 * va auditga yozadi). Parol — seyfdagi JORIY parol (CRM Phase 6); noma'lumi «(saqlanmagan)».
 */
export async function downloadStudentCredentials(): Promise<void> {
  if (USE_MOCK) {
    alert('Eksport faqat real serverda ishlaydi (VITE_USE_MOCK=false).')
    return
  }
  const res = await api.get('/admin/students/export', { responseType: 'blob' })
  const url = URL.createObjectURL(res.data as Blob)
  const a = document.createElement('a')
  a.href = url
  const cd = (res.headers['content-disposition'] as string | undefined) ?? ''
  const m = cd.match(/filename="?([^"]+)"?/)
  a.download = m?.[1] ?? `oquvchilar_${localIsoDate(new Date())}.xlsx`
  document.body.appendChild(a)
  a.click()
  a.remove()
  URL.revokeObjectURL(url)
}

/** TANLANGAN o'quvchilarning to'liq ma'lumotlarini Excel (.xlsx) ga yuklab oladi
 *  (profil + guruhlar + balans + login; «Parol» ustuni — FAQAT superadmin, M2: ommaviy eksport). */
export async function downloadSelectedStudents(studentIds: string[]): Promise<void> {
  if (USE_MOCK) {
    alert('Eksport faqat real serverda ishlaydi (VITE_USE_MOCK=false).')
    return
  }
  const res = await api.post('/admin/students/export-selected', { studentIds }, { responseType: 'blob' })
  const url = URL.createObjectURL(res.data as Blob)
  const a = document.createElement('a')
  a.href = url
  const cd = (res.headers['content-disposition'] as string | undefined) ?? ''
  const m = cd.match(/filename="?([^"]+)"?/)
  a.download = m?.[1] ?? `oquvchilar_tanlangan_${localIsoDate(new Date())}.xlsx`
  document.body.appendChild(a)
  a.click()
  a.remove()
  URL.revokeObjectURL(url)
}

/** Excel'dan ommaviy import natijasi (bitta xato qator). */
export interface StudentImportRowError {
  row: number
  message: string
}

/** Excel'dan ommaviy import yakuniy hisoboti. */
export interface StudentImportResult {
  created: number
  failed: number
  skipped: number
  errors: StudentImportRowError[]
  /** Bazada (yoki faylda yuqoriroqda) BOR bo'lib chiqqan qatorlar — yangi o'quvchi yaratilmadi. */
  existing?: number
  /** Ulardan mavjud o'quvchi yangi guruhga qo'shilgani. */
  addedToGroups?: number
  /** Xato emas, ko'rib chiqish uchun: mavjud o'quvchi, ismdosh, guruhdan chiqqan. */
  warnings?: StudentImportRowError[]
  /** «active» bilan qo'shilganlarga qo'shilgan OY uchun yozilgan qisman hisob — jami so'm va nechta a'zolik. */
  joinMonthCharged?: number
  joinMonthChargedCount?: number
}

/** O'quvchilarni ommaviy kiritish uchun bo'sh Excel shablonini yuklab oladi (.xlsx). */
export async function downloadStudentImportTemplate(): Promise<void> {
  if (USE_MOCK) {
    alert('Shablon faqat real serverda ishlaydi (VITE_USE_MOCK=false).')
    return
  }
  const res = await api.get('/admin/students/import-template', { responseType: 'blob' })
  const url = URL.createObjectURL(res.data as Blob)
  const a = document.createElement('a')
  a.href = url
  a.download = 'oquvchilar_shablon.xlsx'
  document.body.appendChild(a)
  a.click()
  a.remove()
  URL.revokeObjectURL(url)
}

/** To'ldirilgan Excel (.xlsx) shablonini yuklab, o'quvchilarni ommaviy yaratadi. */
export async function importStudents(file: File): Promise<StudentImportResult> {
  if (USE_MOCK) {
    await delay(300)
    return { created: 0, failed: 0, skipped: 0, errors: [] }
  }
  const fd = new FormData()
  fd.append('file', file)
  const { data } = await api.post<StudentImportResult>('/admin/students/import', fd, {
    headers: { 'Content-Type': 'multipart/form-data' },
  })
  return data
}

/** Faylni serverga yuklash (rasm/PDF, ~20 MB). URL qaytaradi — uni keyin entity'da saqlash mumkin. */
export async function uploadAdminFile(file: File): Promise<UploadedFile> {
  if (USE_MOCK) {
    await delay(200)
    return { name: file.name, url: `/uploads/mock-${Date.now()}-${file.name}`, size: file.size, contentType: file.type }
  }
  const fd = new FormData()
  fd.append('file', file)
  const { data } = await api.post<UploadedFile>('/admin/uploads', fd, {
    headers: { 'Content-Type': 'multipart/form-data' },
  })
  return data
}

/** Forma maydonlari (balans bu yerda emas — u to'lov orqali o'zgaradi).
 *  newPassword — ixtiyoriy: tahrirda kiritilsa o'quvchi akkaunti paroli almashtiriladi. */
export type StudentPayload = Omit<Student, 'id' | 'balance' | 'parentFullName' | 'parentPhone'> & {
  newPassword?: string
  /** Backend father/mother'dan o'zi hosil qiladi — forma yubormaydi (ixtiyoriy). */
  parentFullName?: string
  parentPhone?: string
  /** Maktab sinfi: `null`/yo'q = tegilmaydi, `0` = tozalash, 1..11 yoki 12 (bitirgan) = o'rnatish. */
  schoolGrade?: number | null
}

export async function getStudents(state?: 'trial' | 'active'): Promise<Student[]> {
  if (USE_MOCK) {
    await delay()
    return studentsMock
  }
  // `state` — edutizimdagi "Yangi o'quvchilar" (sinov) va "Aktiv o'quvchilar" ro'yxatlari.
  // Qoida SERVERDA (`StudentListView.MatchesState`) — bosh sahifa kartochkalari bilan bitta ta'rif.
  const { data } = await api.get<Student[]>('/admin/students', { params: state ? { state } : undefined })
  return data
}

/** "Joriy oyda obunasi tugaydiganlar" — puli joriy oy darslarini qoplamaydigan o'quvchilar. */
export async function getSubscriptionRisk(): Promise<SubscriptionRiskReport> {
  if (USE_MOCK) {
    await delay()
    return { month: '', totalLessons: 0, totalBalance: 0, totalExpected: 0, items: [] }
  }
  const { data } = await api.get<SubscriptionRiskReport>('/admin/students/subscription-risk')
  return data
}

export interface SubscriptionRiskRow {
  studentId: string
  fullName: string
  phone: string
  parentPhone: string
  /** JAMI DARSLAR NARXI — joriy oy o'quv to'lovi (chegirma ayrilgan). */
  lessonsTotal: number
  balance: number
  /** KUTILAYOTGAN BALANS — oyning butun hisobi yozilgandan keyin. Manfiy = qoplamaydi. */
  expected: number
  memberState: string
}

export interface SubscriptionRiskReport {
  month: string
  totalLessons: number
  totalBalance: number
  totalExpected: number
  items: SubscriptionRiskRow[]
}

/** Bitta o'quvchi (profil sahifasida tahrirlash uchun to'liq obyekt). */
export async function getStudent(id: string): Promise<Student> {
  if (USE_MOCK) {
    await delay()
    const found = studentsMock.find((s) => s.id === id)
    if (!found) throw new Error("O'quvchi topilmadi")
    return found
  }
  const { data } = await api.get<Student>(`/admin/students/${id}`)
  return data
}

/** Qidiruv natijasidagi bitta a'zolik: guruh nomi + holati ('active' | 'trial' | 'frozen'). */
export interface StudentSearchGroup {
  name: string
  status: string
}

/**
 * Global qidiruv natijasi — ATAYIN yengil (to'liq `Student` emas): backend
 * `StudentSearchResultDto` bilan bir xil shakl. Hujjat manzillari bu tipga umuman kirmaydi.
 */
export interface StudentSearchResult {
  id: string
  fullName: string
  /** O'quvchining o'z raqami (bo'sh bo'lishi mumkin). */
  phone: string
  /** Ota-onaning birinchi mavjud raqami (asosiy → ota → ona). */
  parentPhone: string
  isArchived: boolean
  /** 'active' | 'trial' | 'frozen' | '' — badge uchun. */
  memberState: string
  groups: StudentSearchGroup[]
}

/**
 * Global qidiruv (Ctrl+K / topbar): FISH (so'z tartibi muhim emas, o'quvchi + ota-ona ismlari)
 * yoki telefon (o'z/ota/ona/ota-ona) bo'yicha; ARXIVLANGANLAR ham qaytadi. Filtrlash endi
 * SERVERDA (`GET /admin/students/search`) — ilgari butun ro'yxat tortilib brauzerda
 * filtrlanardi. `signal` — debounce bekor bo'lganda so'rovni haqiqatan uzish uchun
 * (axios so'rovni `CanceledError` bilan rad etadi — chaqiruvchi jim yutadi).
 */
export async function searchStudents(
  q: string,
  limit = 12,
  signal?: AbortSignal,
): Promise<StudentSearchResult[]> {
  const term = q.trim()
  if (!term) return []
  if (USE_MOCK) {
    await delay(100)
    // Serverdagi qoidaning yengil nusxasi: so'zlarga ajratib (tartibi muhim emas) ism bo'yicha,
    // kamida 3 raqam bo'lsa telefon bo'yicha. Apostrof turlari birxillashtiriladi.
    const norm = (v: string) =>
      v.toLowerCase().replace(/[ʻʼ‘’`´]/g, "'")
    const words = norm(term).split(/\s+/).filter(Boolean)
    const digits = term.replace(/\D/g, '')
    return studentsMock
      .filter((s) => {
        const haystack = norm(
          [s.fullName, s.parentFullName, s.fatherFullName, s.motherFullName]
            .filter(Boolean)
            .join(' '),
        )
        if (words.length > 0 && words.every((w) => haystack.includes(w))) return true
        if (digits.length >= 3) {
          const phones = [s.phone, s.fatherPhone, s.motherPhone, s.parentPhone]
          if (phones.some((p) => p && p.replace(/\D/g, '').includes(digits))) return true
        }
        return false
      })
      .slice(0, limit)
      .map((s) => ({
        id: s.id,
        fullName: s.fullName,
        phone: s.phone ?? '',
        parentPhone: s.parentPhone || s.fatherPhone || s.motherPhone || '',
        isArchived: !!s.isArchived,
        memberState: s.memberState ?? '',
        groups: (s.groupStates ?? []).map((g) => ({ name: g.name, status: g.status })),
      }))
  }
  const { data } = await api.get<StudentSearchResult[]>('/admin/students/search', {
    params: { q: term, limit, includeArchived: true },
    signal,
  })
  return data
}

/**
 * Bosh sahifa kartochkasining RO'YXATI: shu oyda yangi/aktiv o'quvchidan ketganlar yoki birinchi
 * to'lovni qilganlar. Tanlov SERVERDA — kartochka sonini hisoblaydigan funksiyaning o'zidan
 * (`DashboardSummary.MovementAsync`), ya'ni qatorlar soni kartochkadagi son bilan teng.
 * Arxivlangan o'quvchilar ham keladi. `month` bo'sh — joriy oy.
 */
export type MonthMovementKind = 'leftNew' | 'leftActive' | 'firstPayment'

export async function getMonthMovement(kind: MonthMovementKind, month?: string): Promise<Student[]> {
  if (USE_MOCK) {
    await delay()
    return []
  }
  const { data } = await api.get<Student[]>('/admin/students/month-movement', {
    params: month ? { kind, month } : { kind },
  })
  return data
}

/** Faqat arxivlangan o'quvchilar ro'yxati (alohida ko'rish uchun). */
export async function getArchivedStudents(): Promise<Student[]> {
  if (USE_MOCK) {
    await delay()
    return []
  }
  const { data } = await api.get<Student[]>('/admin/students/archived')
  return data
}

/** Arxivlash natijasi — o'quvchi qaysi guruhlardan chiqarildi (arxiv = ketdi). */
export interface ArchiveStudentResult {
  closedMemberships: number
  /** Keyingi oylar hisobi bekor qilinib balansga qaytgan summa. */
  restored: number
  groups: string[]
}

/**
 * O'quvchini arxivga ko'chirish (sabab yoki reasonId bilan). Login bloklanadi va o'quvchi BARCHA
 * guruhlaridan «Guruhdan chiqarish» bilan bir xil chiqariladi (server: `StudentArchiving`).
 * `note` — ixtiyoriy erkin izoh, o'quvchi profilidagi izohlarga yoziladi (server `LeaveNotes`).
 */
export async function archiveStudent(
  id: string,
  reason?: string,
  reasonId?: string,
  note?: string,
): Promise<ArchiveStudentResult> {
  if (USE_MOCK) {
    await delay(150)
    return { closedMemberships: 0, restored: 0, groups: [] }
  }
  const { data } = await api.post<ArchiveStudentResult>(`/admin/students/${id}/archive`, { reason, reasonId, note })
  return data ?? { closedMemberships: 0, restored: 0, groups: [] }
}

/**
 * Arxivdan qaytarish. Ixtiyoriy yangi parol bilan (parol bo'sh = login bloklangicha qoladi).
 * ⚠️ Guruhlar TIKLANMAYDI — `message` foydalanuvchiga aytiladi ("guruhga qo'shing").
 */
export async function restoreStudent(id: string, newPassword?: string): Promise<{ message?: string }> {
  if (USE_MOCK) {
    await delay(150)
    return {}
  }
  const { data } = await api.post<{ message?: string }>(`/admin/students/${id}/restore`, { newPassword: newPassword ?? null })
  return data ?? {}
}

/* ---------- Telegram ulanishlari (ota-ona boti) ---------- */

/** O'quvchiga bog'langan Telegram chat (bot xabarlari va kirish kodi shu chatga ketadi). */
export interface StudentTelegramLink {
  id: string
  /** Telegram'dagi ism (kontakt ulashgan odam). */
  parentName: string
  phone: string
  createdAt: string
  /** `false` — raqami o'quvchining joriy raqamlaridan hech biriga mos emas (eskirgan ulanish). */
  phoneMatches: boolean
  /** Kim ulangan: father · mother · parent · student · other (CRM upgrade P2.4). */
  relation?: string
  /** Chat botni bloklagan — xabar bormaydi (foydalanuvchi botga qayta yozsa o'zi tiklanadi). */
  blocked?: boolean
  blockedAt?: string | null
}

/** Ulanish qarindoshligini o'zgartirish (`students.list:edit`). */
export async function setStudentTelegramRelation(
  id: string, linkId: string, relation: string,
): Promise<StudentTelegramLink> {
  const { data } = await api.put<StudentTelegramLink>(`/admin/students/${id}/telegram-links/${linkId}`, { relation })
  return data
}

/** O'quvchining «Telegram xabarlari» (oxirgilari birinchi). */
export interface StudentTelegramDelivery {
  id: string
  createdAt: string
  sentAt: string | null
  trigger: string
  /** Xabar turi yorlig'i (masalan «To'lov eslatmasi», «E'lon»). */
  label: string
  relation: string
  status: string
  ackAt: string | null
  textPlain: string
}

export async function getStudentTelegramDeliveries(id: string, limit = 50): Promise<StudentTelegramDelivery[]> {
  if (USE_MOCK) return []
  const { data } = await api.get<StudentTelegramDelivery[]>(`/admin/students/${id}/telegram-deliveries`, {
    params: { limit },
    cache: false,
  })
  return data
}

export async function getStudentTelegramLinks(id: string): Promise<StudentTelegramLink[]> {
  if (USE_MOCK) return []
  const { data } = await api.get<StudentTelegramLink[]>(`/admin/students/${id}/telegram-links`)
  return data
}

/** Ulanishni uzish — shu chat endi bu o'quvchi bo'yicha xabar va kirish kodi olmaydi. */
export async function unlinkStudentTelegram(id: string, linkId: string): Promise<void> {
  await api.delete(`/admin/students/${id}/telegram-links/${linkId}`)
}

export async function createStudent(payload: StudentPayload): Promise<Student> {
  if (USE_MOCK) {
    await delay(300)
    // Kelgan oyidan joriy oygacha har oy uchun qarz: balans = -fee * oylar soni
    const fee = classesMock.find((c) => c.name === payload.className)?.monthlyFee ?? 0
    const cur = localIsoDate(new Date()).slice(0, 7)
    const enr = (payload.enrollmentDate || cur).slice(0, 7)
    let months = 0
    if (enr <= cur) {
      const [ey, em] = enr.split('-').map(Number)
      const [cy, cm] = cur.split('-').map(Number)
      months = (cy - ey) * 12 + (cm - em) + 1
    }
    return {
      ...payload,
      parentFullName: payload.parentFullName ?? payload.fatherFullName ?? payload.motherFullName ?? '',
      parentPhone: payload.parentPhone ?? payload.fatherPhone ?? payload.motherPhone ?? '',
      id: uid(),
      balance: -fee * months,
    }
  }
  const { data } = await api.post<Student>('/admin/students', payload)
  return data
}

/** Telefon dublikati — mos kelgan mavjud o'quvchi (arxivdagilar ham). */
export interface PhoneMatch {
  /** Kiritilgan (mos kelgan) raqam */
  phone: string
  studentId: string
  fullName: string
  className: string
  isArchived: boolean
  /** Mavjud yozuvda qaysi raqam mos keldi: O'quvchi / Ota / Ona / Ota-ona */
  role: string
}

/**
 * Kiritilgan raqamlar (o'quvchi o'zi / ota / ona) allaqachon biror o'quvchida (ARXIVDAGILAR ham)
 * bormi — tekshiradi. `excludeId` — tahrirdagi o'quvchining o'zi. Bo'sh massiv = dublikat yo'q.
 */
export async function checkStudentPhones(req: {
  phone?: string
  fatherPhone?: string
  motherPhone?: string
  parentPhone?: string
  excludeId?: string
}): Promise<PhoneMatch[]> {
  if (USE_MOCK) {
    await delay(150)
    return []
  }
  const { data } = await api.post<PhoneMatch[]>('/admin/students/check-phones', req)
  return data
}

/** Update o'quvchini tahrirlash.
 *  `applyDiscount=true` — chegirma o'zgargan bo'lsa, joriy oy hisobi yangi summaga
 *  to'g'rilanadi (balans deltaga moslab tuziladi). false (default) — joriy oy eski summada
 *  qoladi, yangi chegirma keyingi accrual'dan amal qiladi. */
export async function updateStudent(
  id: string,
  payload: StudentPayload,
  applyDiscount?: boolean,
): Promise<void> {
  if (USE_MOCK) {
    await delay(300)
    return
  }
  await api.put(`/admin/students/${id}`, payload, {
    params: applyDiscount ? { applyDiscount: true } : undefined,
  })
}

/**
 * O'QUVCHI RASMINI o'rnatish/o'chirish (`photoUrl = null` → o'chirish).
 *
 * Nega alohida: rasm o'quvchi sahifasidagi DUMALOQ avatarni bosib ham yuklanadi — u yerda
 * to'liq forma yo'q, to'liq `PUT` yuborilsa boshqa maydonlar bo'shab qolishi mumkin edi.
 * Serverda ma'lumot `Student.BirthCertificateUrl` ustunida (nomi eski, tizim uni RASM deb
 * ishlatadi — o'quvchi ilovasida `photoUrl` bo'lib chiqadi).
 */
export async function updateStudentPhoto(id: string, photoUrl: string | null): Promise<void> {
  if (USE_MOCK) {
    await delay(200)
    return
  }
  await api.put(`/admin/students/${id}/photo`, { photoUrl })
}

export async function deleteStudent(id: string, reasonId?: string): Promise<void> {
  if (USE_MOCK) {
    await delay(200)
    return
  }
  await api.delete(`/admin/students/${id}`, { params: reasonId ? { reasonId } : undefined })
}

/** «Butunlay o'chirish» oynasi uchun: nimalar o'chadi (`detach` — saqlanadi, faqat ajratiladi). */
export interface StudentPurgeItem {
  key: string
  label: string
  count: number
  detach: boolean
}

export interface StudentPurgePreview {
  studentId: string
  fullName: string
  /** `false` — tirik pul amali bor, sababi `blockReason` da. */
  canPurge: boolean
  blockReason: string | null
  liveTransactions: number
  liveTransactionsTotal: number
  items: StudentPurgeItem[]
}

/** Ruxsat — `students.list:delete` (boshqalarga 403). */
export async function getStudentPurgePreview(id: string): Promise<StudentPurgePreview> {
  const { data } = await api.get<StudentPurgePreview>(`/admin/students/${id}/purge-preview`)
  return data
}

/** O'quvchini va unga tegishli HAMMA narsani o'chiradi — arxivga ham tushmaydi, qaytarib bo'lmaydi. */
export async function purgeStudent(id: string): Promise<void> {
  await api.delete(`/admin/students/${id}/purge`)
}

/** O'quvchining tizim akkaunti (login/parol) */
export async function getStudentCredentials(id: string): Promise<Credentials> {
  if (USE_MOCK) {
    await delay(200)
    return { login: 'aliyevvali', password: 'demo23', role: 'student' }
  }
  const { data } = await api.get<Credentials>(`/admin/students/${id}/credentials`)
  return data
}

/** O'quvchiga yangi tasodifiy parol generatsiya qiladi — parol bir marta qaytadi. */
export async function resetStudentPassword(id: string): Promise<Credentials> {
  if (USE_MOCK) {
    await delay(200)
    return { login: 'aliyevvali', password: 'yangi' + Math.random().toString(36).slice(2, 8), role: 'student' }
  }
  const { data } = await api.post<Credentials>(`/admin/students/${id}/reset-password`)
  return data
}

/** O'quvchining login orqali tizimga kirishini cheklash/ochish (admin qo'lda). */
export async function setStudentLoginBlock(id: string, blocked: boolean): Promise<void> {
  if (USE_MOCK) {
    await delay(150)
    return
  }
  await api.put(`/admin/students/${id}/login-block`, { blocked })
}

/** Bir nechta o'quvchining login orqali tizimga kirishini birdaniga cheklash/ochish (jadvalda ko'p tanlash). */
export async function setStudentLoginBlockBulk(
  ids: string[],
  blocked: boolean,
): Promise<{ changed: number }> {
  if (USE_MOCK) {
    await delay(150)
    return { changed: ids.length }
  }
  const { data } = await api.put<{ changed: number }>('/admin/students/login-block-bulk', {
    studentIds: ids,
    blocked,
  })
  return data
}

/** Bitta guruh bo'yicha o'quvchining oylik hisobi (to'lov oynasi uchun) — aggregate emas. */
export async function getGroupLedger(
  studentId: string,
  groupId: string,
): Promise<import('@/types').GroupLedger> {
  const { data } = await api.get<import('@/types').GroupLedger>(
    `/admin/students/${studentId}/group-ledger`,
    { params: { groupId } },
  )
  return data
}

/** O'quvchiga to'lov kiritish — balansga qo'shiladi.
 *  `month` ("YYYY-MM") berilsa, to'lov shu oy uchun hisoblanadi.
 *  `groupId` berilsa, to'lov shu guruh uchun hisoblanadi (o'qituvchi foizli maoshi shunga tayanadi).
 *  `date` ("YYYY-MM-DD") — to'lov haqiqatan sodir bo'lgan sana (ixtiyoriy, bo'sh bo'lsa bugun —
 *  masalan bugun to'lagan, lekin tizimga ertaga kiritilayotgan to'lov uchun eski sana tanlanadi). */
export async function addPayment(
  id: string,
  amount: number,
  month?: string,
  groupId?: string,
  comment?: string,
  method?: string,
  date?: string,
  /** Naqd to'lovda — qog'oz kvitansiya raqami ("KV..."); kartada — to'lov vaqti "HH:mm" va
   *  karta raqamining oxirgi 4 raqami.
   *  `forceReceipt` — kvitansiya raqami band bo'lsa ham saqlash ("Baribir saqlash"). */
  extra?: { receiptNo?: string; paidTime?: string; cardLast4?: string; forceReceipt?: boolean; requestId?: string; cashboxId?: string },
): Promise<string | null> {
  if (USE_MOCK) {
    await delay(250)
    return null
  }
  const { data } = await api.post<{ id: string }>(`/admin/students/${id}/payments`, {
    amount,
    month,
    groupId,
    comment,
    method,
    date,
    receiptNo: extra?.receiptNo,
    paidTime: extra?.paidTime,
    cardLast4: extra?.cardLast4,
    forceReceipt: extra?.forceReceipt ?? false,
    // Oyna ochilishining kaliti — qayta bosilgan/yuborilgan so'rov IKKINCHI to'lov yozmaydi.
    requestId: extra?.requestId,
    // Qaysi kassa (bo'sh — server kassirning o'z kassasini / «Asosiy kassa»ni tanlaydi).
    cashboxId: extra?.cashboxId,
  })
  return data?.id ?? null
}

/**
 * ALLAQACHON kiritilgan kvitansiya raqami haqidagi ma'lumot — server 409 Conflict bilan qaytaradi.
 * Kassir ekranida kartochka bo'lib chiqadi: kim to'lagan, qaysi guruh/o'qituvchi, qancha, qachon.
 */
export interface DuplicateReceipt {
  receiptNo: string
  transactionId: string
  studentId: string | null
  studentName: string
  groupName: string
  courseName: string
  teacherName: string
  amount: number
  date: string
  month: string
  method: string
  /** To'lovni kiritgan xodim (kassir/admin). */
  createdBy: string
  createdAt: string
}

/** Xatolik kvitansiya dublikati (409) bo'lsa — uning ma'lumotini qaytaradi, aks holda null. */
export function receiptDuplicateOf(err: unknown): DuplicateReceipt | null {
  const res = (err as { response?: { status?: number; data?: { duplicate?: DuplicateReceipt } } })?.response
  if (res?.status !== 409) return null
  return res.data?.duplicate ?? null
}

const LEDGER_MONTHS = ['2026-01', '2026-02', '2026-03', '2026-04', '2026-05']

/** O'quvchi to'lov tarixi: oylar bo'yicha hisoblangan/to'langan holat */
export async function getStudentLedger(id: string): Promise<StudentLedger> {
  if (USE_MOCK) {
    await delay()
    const student = studentsMock.find((s) => s.id === id)
    if (!student) throw new Error('O\'quvchi topilmadi')
    const rawFee = classesMock.find((c) => c.name === student.className)?.monthlyFee ?? 0
    const monthDiscount = Math.max(
      0,
      Math.min(rawFee, (rawFee * student.discountPct) / 100 + student.discountAmount),
    )
    const fee = Math.max(0, rawFee - monthDiscount)
    const totalCharged = rawFee * LEDGER_MONTHS.length
    const totalDiscount = monthDiscount * LEDGER_MONTHS.length
    let pool = Math.max(0, fee * LEDGER_MONTHS.length + student.balance)
    const totalPaid = pool
    const months = LEDGER_MONTHS.map((month) => {
      const paid = Math.min(pool, fee)
      pool -= paid
      const remaining = fee - paid
      const status: MonthStatus = remaining === 0 ? 'paid' : paid > 0 ? 'partial' : 'unpaid'
      return { month, charged: rawFee, discount: monthDiscount, paid, remaining, status, courses: [] }
    })
    const payments = financeMock
      .filter((t) => t.studentId === id && t.category === 'tuition')
      .map((t) => ({ date: t.date, amount: t.amount, note: t.note }))
      .sort((a, b) => (a.date < b.date ? 1 : -1))
    return {
      student,
      balance: student.balance,
      monthlyFee: fee,
      totalCharged,
      totalDiscount,
      totalPaid,
      months,
      payments,
    }
  }
  const { data } = await api.get<StudentLedger>(`/admin/students/${id}/ledger`)
  return data
}

/** Qo'lda oylik tahriri natijasi (ko'rish rejimida ham): eski/yangi hisob, balans oldin/keyin. */
export interface EditChargeResult {
  saved: boolean
  oldAmount: number
  newAmount: number
  oldDiscount: number
  newDiscount: number
  balanceBefore: number
  balanceAfter: number
}

/** FAQAT super admin (server ham tekshiradi): shu oyning hisoblangan summasini qo'lda tahrirlaydi
 *  («Balansni tuzatish»). `groupId` berilsa — shu guruh hisobi; null/bo'sh — guruhsiz (ClassName) hisobi.
 *  `opts.preview` — natijani ko'rsatadi, SAQLAMAYDI; `opts.note` — tarixga yoziladigan izoh. */
export async function editStudentCharge(
  id: string,
  month: string,
  amount: number,
  groupId?: string,
  opts?: { note?: string; preview?: boolean },
): Promise<EditChargeResult | undefined> {
  if (USE_MOCK) {
    await delay(150)
    return undefined
  }
  // Olib tashla :1/:0 agar bor bo'lsa (backend Month faqat YYYY-MM formatda kutadi)
  const cleanMonth = month.split(':')[0]
  const { data } = await api.put<EditChargeResult>(
    `/admin/students/${id}/charges/${cleanMonth}`,
    { amount, note: opts?.note || undefined, preview: opts?.preview ?? false },
    { params: groupId ? { groupId } : undefined },
  )
  return data
}

/* ---------- Tugatgan kurslar + sertifikatlar ---------- */

/** Admin: o'quvchining tugatgan kursi + sertifikati. */
export interface StudentCompletedCourse {
  certificateId: string
  courseId: string
  courseName: string
  issuedAt: string
  expiresAt: string
  status: string
  fileName: string
  downloadUrl: string
  downloadCount: number
  groupName: string
}

/** Support o'qituvchidan kelgan feedback (o'tilgan support darsi: mavzu + izoh). */
export interface StudentSupportFeedback {
  date: string
  startTime: string
  endTime: string
  teacherName: string
  topic: string
  notes: string
}

/** AI tahlilidagi sohaviy baholar (0-100) — radar/diagramma uchun. */
export interface AiRatings {
  akademik: number
  davomat: number
  intizom: number
  uyVazifa: number
  faollik: number
  umumiy: number
}
/** AI tahlilining strukturali natijasi (matn bo'limlari + diagramma sonlari). */
export interface StudentAiAnalysisResult {
  umumiy: string
  kuchli: string[]
  zaif: string[]
  dinamika: string
  ozgarishlar: string
  tavsiyalar: string[]
  baholar: AiRatings
  /** "yaxshilanmoqda" | "barqaror" | "yomonlashmoqda" */
  trend: string
}
/** Saqlangan bitta AI tahlil yozuvi (tarix elementi). */
export interface StudentAiAnalysisRecord {
  id: string
  /** "yyyy-MM-dd" */
  date: string
  createdAt: string
  model: string
  overallScore: number
  result: StudentAiAnalysisResult
}
/** AI tahlil yaratish javobi. */
export interface StudentAiAnalysisResponse {
  ok: boolean
  /** true bo'lsa bugun allaqachon tahlil qilingan (yangi Gemini chaqirig'i bo'lmadi). */
  alreadyToday: boolean
  record: StudentAiAnalysisRecord | null
  error: string | null
}

/** O'quvchining saqlangan AI tahlillari tarixi (eng yangisi birinchi). */
export async function getStudentAiAnalyses(studentId: string): Promise<StudentAiAnalysisRecord[]> {
  if (USE_MOCK) {
    await delay()
    return []
  }
  const { data } = await api.get<StudentAiAnalysisRecord[]>(`/admin/students/${studentId}/ai-analyses`)
  return data
}

/**
 * O'quvchining BARCHA ma'lumotlarini Gemini orqali tahlil qiladi (kuniga bir marta).
 * Bugun qilingan bo'lsa mavjud yozuv qaytadi (alreadyToday=true), yangi chaqiruv bo'lmaydi.
 * Sozlamalar → AI Tahlil (Gemini) bo'limida API kaliti kiritilgan bo'lishi kerak.
 */
export async function generateStudentAiAnalysis(studentId: string): Promise<StudentAiAnalysisResponse> {
  const { data } = await api.post<StudentAiAnalysisResponse>(`/admin/students/${studentId}/ai-analysis`)
  return data
}

/** O'quvchiga support o'qituvchilar bergan feedback (o'tilgan support darslari). */
export async function getStudentSupportFeedback(
  studentId: string,
): Promise<StudentSupportFeedback[]> {
  if (USE_MOCK) {
    await delay()
    return []
  }
  const { data } = await api.get<StudentSupportFeedback[]>(
    `/admin/students/${studentId}/support-feedback`,
  )
  return data
}

/** O'quvchining tugatgan kurslari + sertifikatlari ro'yxati. */
export async function getStudentCertificates(studentId: string): Promise<StudentCompletedCourse[]> {
  if (USE_MOCK) {
    await delay()
    return []
  }
  const { data } = await api.get<StudentCompletedCourse[]>(
    `/admin/students/${studentId}/certificates`,
  )
  return data
}

/** Admin: o'quvchiga qo'lda sertifikat yaratish (kurs bo'yicha). */
export async function generateStudentCertificate(
  studentId: string,
  courseId: string,
  notes?: string,
): Promise<StudentCompletedCourse> {
  const { data } = await api.post<StudentCompletedCourse>(
    `/admin/students/${studentId}/certificates/generate`,
    { courseId, notes },
  )
  return data
}

/** Sertifikat faylini yuklab olish (admin). Auth header avtomatik qo'shiladi. */
export async function downloadStudentCertificate(
  studentId: string,
  certificateId: string,
  fileName: string,
): Promise<void> {
  if (USE_MOCK) {
    alert('Sertifikat faqat real serverda yuklanadi.')
    return
  }
  const res = await api.get(
    `/admin/students/${studentId}/certificates/${certificateId}/download`,
    { responseType: 'blob' },
  )
  const url = URL.createObjectURL(res.data as Blob)
  const a = document.createElement('a')
  a.href = url
  a.download = fileName || `sertifikat_${certificateId}.html`
  document.body.appendChild(a)
  a.click()
  a.remove()
  URL.revokeObjectURL(url)
}

/* ---------- Ball (reyting bali) ---------- */

/**
 * Markaz bo'yicha o'quvchilar bali: jurnal baholari yig'indisi + bajarilgan baholash mezonlari.
 * "O'quvchilar" ro'yxatidagi "Ball" ustuni va ball bo'yicha saralash uchun (serverda keshlangan).
 */
export async function getStudentBalls(): Promise<StudentBall[]> {
  if (USE_MOCK) {
    await delay()
    return []
  }
  const { data } = await api.get<StudentBall[]>('/admin/students/balls')
  return data
}

/* ---------- Izohlar (o'quvchi profilidagi erkin eslatmalar) ---------- */

/** O'quvchi profilidagi izoh — kim va qachon yozgani bilan (tarix, ustiga yozilmaydi). */
export interface StudentNote {
  id: string
  text: string
  authorName: string
  createdAt: string
  /** Joriy foydalanuvchi o'chira oladimi (o'z izohi yoki superadmin) */
  canDelete: boolean
  /** Joriy foydalanuvchi tahrirlay oladimi (o'chirish bilan bir xil qoida) */
  canEdit?: boolean
  /** Tahrirlangan bo'lsa — oxirgi tahrir vaqti (ISO); tahrirlanmagan bo'lsa null. */
  editedAt?: string | null
}

/** O'quvchining izohlari — yangisi tepada. */
export async function getStudentNotes(studentId: string): Promise<StudentNote[]> {
  if (USE_MOCK) {
    await delay()
    return []
  }
  const { data } = await api.get<StudentNote[]>(`/admin/students/${studentId}/notes`)
  return data
}

/** Yangi izoh qo'shish — javobda yaratilgan izoh qaytadi (ro'yxat boshiga qo'shish uchun). */
export async function addStudentNote(studentId: string, text: string): Promise<StudentNote> {
  const { data } = await api.post<StudentNote>(`/admin/students/${studentId}/notes`, { text })
  return data
}

/**
 * Izoh matnini tahrirlash — faqat muallifi yoki superadmin (server ham tekshiradi).
 * Muallif va yozilgan vaqt o'zgarmaydi, javobda `editedAt` to'ladi ("tahrirlangan" belgisi).
 */
export async function updateStudentNote(noteId: string, text: string): Promise<StudentNote> {
  const { data } = await api.put<StudentNote>(`/admin/students/notes/${noteId}`, { text })
  return data
}

/** Izohni o'chirish — faqat muallifi yoki superadmin (server ham tekshiradi). */
export async function deleteStudentNote(noteId: string): Promise<void> {
  await api.delete(`/admin/students/notes/${noteId}`)
}

/* ---------- "Izohlarga javoblar" — izoh yozilgan o'quvchilar bir ro'yxatda ---------- */

/** Bitta o'quvchi — unga yozilgan izohlarning jamlanmasi. */
export interface StudentNoteOverviewRow {
  studentId: string
  fullName: string
  /** Faol guruhlari (muzlatilganlarsiz — o'quvchilar ro'yxatidagi qoida bilan bir xil). */
  groups: string[]
  phone: string
  parentPhone: string
  isArchived: boolean
  /** Izohlar soni (davr tanlangan bo'lsa — o'sha davrdagilar). */
  noteCount: number
  /** Birinchi izoh vaqti (ISO). */
  firstNoteAt: string
  /** Oxirgi izoh vaqti (ISO) — ro'yxat shu bo'yicha saralangan. */
  lastNoteAt: string
  lastNoteText: string
  lastAuthorName: string
  /** Izoh yozgan xodimlar (takrorsiz). */
  authors: string[]
}

/**
 * IZOH YOZILGAN o'quvchilar ro'yxati — eng yangi izoh tepada.
 *
 * `q` — o'quvchi ISMI yoki izoh MATNI ichidan qidiradi; `from`/`to` — izoh yozilgan sana
 * (server `to` ni kun oxirigacha cho'zadi).
 */
export async function getStudentNotesOverview(params: {
  q?: string
  from?: string
  to?: string
  limit?: number
} = {}): Promise<StudentNoteOverviewRow[]> {
  if (USE_MOCK) {
    await delay()
    return []
  }
  const { data } = await api.get<StudentNoteOverviewRow[]>('/admin/students/notes/overview', { params })
  return data
}

/**
 * Oylik kalendar kataklaridagi sonlar: shu oyning qaysi kunida nechta izoh yozilgan.
 *
 * ⚠️ Tanlangan davrga BOG'LIQ EMAS (alohida yengil so'rov) — bitta kun tanlanganda ham
 * kalendar butun oyni ko'rsatib tursin.
 */
export async function getStudentNoteDays(month?: string): Promise<{ date: string; count: number }[]> {
  if (USE_MOCK) {
    await delay()
    return []
  }
  const { data } = await api.get<{ date: string; count: number }[]>('/admin/students/notes/days', {
    params: { month },
  })
  return data
}

/* ---------- «Chegirma» — kelmagan darslar (to'lov oynasida; server `LessonDiscountService`) ---------- */

/** Qaysi yo'l: admin paneli (`students.list`) yoki kassa portali (`kassa`) — ruxsat shunga qarab. */
/** Qaysi yo'l bilan (ruxsat shunga qarab): o'quvchi profili (`students.list`), Kassa portali (`kassa`), Moliya → «Yangi
 * moliyaviy amal» → o'quvchi to'lovi (`finance.main`). Mantiq serverda bitta (`LessonDiscountHttp`). */
export type LessonDiscountChannel = 'admin' | 'kassa' | 'finance'

export interface LessonDiscountQuote {
  groupId: string
  month: string
  lessons: number
  /** Shu oydagi jami darslar (guruh kunlari bo'yicha). */
  lessonsInMonth: number
  lessonPrice: number
  /** Oyning to'lanmagan qoldig'i — ayriladigan summa undan oshmaydi. */
  remaining: number
  amount: number
}

export interface LessonDiscount {
  id: string
  groupId: string
  groupName: string
  month: string
  lessons: number
  lessonPrice: number
  amount: number
  reason: string
  createdAt: string
  createdByName: string
  cancelled: boolean
  cancelledAt: string | null
  cancelledByName: string | null
}

const lessonDiscountBase = (channel: LessonDiscountChannel, studentId: string) =>
  channel === 'kassa'
    ? `/admin/kassa/students/${studentId}/lesson-discounts`
    : channel === 'finance'
      ? `/admin/finance/students/${studentId}/lesson-discounts`
      : `/admin/students/${studentId}/lesson-discounts`

export async function getLessonDiscounts(channel: LessonDiscountChannel, studentId: string): Promise<LessonDiscount[]> {
  if (USE_MOCK) return []
  const { data } = await api.get<LessonDiscount[]>(lessonDiscountBase(channel, studentId))
  return data
}

export async function quoteLessonDiscount(
  channel: LessonDiscountChannel, studentId: string, groupId: string, month: string, lessons: number,
): Promise<LessonDiscountQuote> {
  const { data } = await api.get<LessonDiscountQuote>(`${lessonDiscountBase(channel, studentId)}/quote`, {
    params: { groupId, month, lessons },
  })
  return data
}

export async function applyLessonDiscount(
  channel: LessonDiscountChannel, studentId: string,
  payload: { groupId: string; month: string; lessons: number; reason: string },
): Promise<LessonDiscount> {
  const { data } = await api.post<LessonDiscount>(lessonDiscountBase(channel, studentId), payload)
  return data
}

export async function cancelLessonDiscount(
  channel: LessonDiscountChannel, studentId: string, discountId: string,
): Promise<LessonDiscount> {
  const { data } = await api.post<LessonDiscount>(`${lessonDiscountBase(channel, studentId)}/${discountId}/cancel`)
  return data
}
