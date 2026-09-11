import { api } from '../client'

/**
 * TEST SERTIFIKATLARI — Word (.docx) andozalari va test bo'yicha beriladigan sertifikatlar.
 *
 * Andozalarni FAQAT admin boshqaradi ("O'quv bo'limi → Testlar natijalari → Sertifikat shablonlari"),
 * o'qituvchi esa ularni test yaratishda tanlaydi va sertifikat yaratadi. Shuning uchun quyida
 * admin (`/admin/test-results/...`) va o'qituvchi (`/teacher/test-results/...`) variantlari alohida.
 */

/** `ready` — PDF tayyor; `docx` — serverda LibreOffice yo'q, faqat Word fayl saqlangan. */
export type TestCertificateStatus = 'ready' | 'docx'

/** Andozada yozilishi mumkin bo'lgan bitta `@`-o'zgaruvchi. */
export interface CertificateToken {
  token: string
  label: string
  example: string
}

/** O'quvchi surati andozaga QANDAY qo'yilishi — bu matn belgisi emas, Word'dagi rasm o'rni. */
export interface CertificatePhotoHelp {
  title: string
  steps: string[]
  note: string
}

export interface CertificateTokensResponse {
  tokens: CertificateToken[]
  photoHelp: CertificatePhotoHelp
  /** false — serverda LibreOffice yo'q, sertifikatlar .docx bo'lib chiqadi */
  pdfAvailable: boolean
}

export interface TestCertificateTemplate {
  id: string
  name: string
  /** Yuklangan .docx manzili ("/uploads/xxx.docx") */
  fileUrl: string
  /** Faylning asl nomi */
  fileName: string
  /** Standart — test formasida shablon tanlanmasa shu ishlatiladi */
  isDefault: boolean
  isActive: boolean
  createdAt: string
  createdBy: string
}

export interface TestCertificateTemplatePayload {
  name: string
  /** Tahrirlashda bo'sh qoldirilsa fayl o'zgarmaydi */
  fileUrl?: string
  fileName?: string
  isDefault?: boolean
  isActive?: boolean
}

export interface TestCertificate {
  id: string
  testResultId: string
  studentId: string
  studentName: string
  /** "SRT-2026-0042" */
  number: string
  templateName: string
  docxUrl: string
  /** Bo'sh bo'lsa PDF yaratilmagan (status='docx') */
  pdfUrl: string
  status: TestCertificateStatus
  score: number
  maxScore: number
  percent: number
  issuedAt: string
}

/**
 * SERTIFIKAT YARATISH — fon ishining holati.
 *
 * Generatsiya so'rov ichida emas, FONDA bajariladi va 5 tadan bo'lib chiziladi (LibreOffice'ni
 * har fayl uchun qayta ochish qimmat, hammasini birdan chizish esa 1 GB serverda xotirani
 * to'ldirardi). Shuning uchun UI bu holatni so'rab turadi: `done/total` — progress,
 * `items` — SHU DAQIQADA tayyor bo'lganlar (ish tugashini kutmasdan yuklab olinadi).
 */
export interface CertificateJob {
  /** true — hali yaratilmoqda (UI so'rashda davom etadi) */
  running: boolean
  /** Jami nechta sertifikat chiqishi kutilmoqda */
  total: number
  /** Shu daqiqada nechtasi tayyor */
  done: number
  /** false — serverda LibreOffice yo'q, faqat .docx yaratiladi */
  pdfAvailable: boolean
  /** Fon ishida xato bo'lgan bo'lsa (UI qizil xabar ko'rsatadi) */
  error?: string | null
  /** LibreOffice yo'qligi haqidagi ogohlantirish */
  warning?: string | null
  /** Tayyor sertifikatlar — holat bilan BITTA so'rovda keladi */
  items: TestCertificate[]
}

// ---------------------------------------------------------------- Admin: andozalar

/** Andozada ishlatiladigan o'zgaruvchilar ro'yxati (yagona manba — server). */
export async function getCertificateTokens(): Promise<CertificateTokensResponse> {
  const { data } = await api.get<CertificateTokensResponse>(
    '/admin/test-results/certificate-tokens',
  )
  return data
}

export async function getCertificateTemplates(activeOnly = false): Promise<TestCertificateTemplate[]> {
  const { data } = await api.get<TestCertificateTemplate[]>(
    '/admin/test-results/certificate-templates',
    { params: { activeOnly } },
  )
  return data
}

export async function createCertificateTemplate(
  payload: TestCertificateTemplatePayload,
): Promise<TestCertificateTemplate> {
  const { data } = await api.post<TestCertificateTemplate>(
    '/admin/test-results/certificate-templates',
    payload,
  )
  return data
}

export async function updateCertificateTemplate(
  id: string,
  payload: TestCertificateTemplatePayload,
): Promise<TestCertificateTemplate> {
  const { data } = await api.put<TestCertificateTemplate>(
    `/admin/test-results/certificate-templates/${id}`,
    payload,
  )
  return data
}

export async function deleteCertificateTemplate(id: string): Promise<void> {
  await api.delete(`/admin/test-results/certificate-templates/${id}`)
}

// ---------------------------------------------------------------- Admin: sertifikatlar

/** Sertifikat yaratishni BOSHLASH (ball kiritilgan har o'quvchiga bittadan). So'rov darhol qaytadi —
 *  keyin `getCertificateJob` bilan holat so'raladi. Qayta chaqirilsa mavjudlari YANGILANADI. */
export async function startTestCertificates(testId: string): Promise<CertificateJob> {
  const { data } = await api.post<CertificateJob>(`/admin/test-results/${testId}/certificates`)
  return data
}

export async function getTestCertificates(testId: string): Promise<TestCertificate[]> {
  const { data } = await api.get<TestCertificate[]>(`/admin/test-results/${testId}/certificates`)
  return data
}

// ---------------------------------------------------------------- O'qituvchi

/** Tanlash uchun FAOL andozalar (o'qituvchi yarata/o'chira olmaydi — faqat tanlaydi). */
export async function getTeacherCertificateTemplates(): Promise<{
  templates: TestCertificateTemplate[]
  pdfAvailable: boolean
}> {
  const { data } = await api.get<{
    templates: TestCertificateTemplate[]
    pdfAvailable: boolean
  }>('/teacher/test-results/certificate-templates')
  return data
}

export async function startTeacherTestCertificates(testId: string): Promise<CertificateJob> {
  const { data } = await api.post<CertificateJob>(`/teacher/test-results/${testId}/certificates`)
  return data
}

// ---------------------------------------------------------------- Holat (ikkala rol uchun)

/** Generatsiya holati + shu daqiqada tayyor sertifikatlar. UI shuni bir necha soniyada bir so'raydi. */
export async function getCertificateJob(testId: string, teacher = false): Promise<CertificateJob> {
  const base = teacher ? '/teacher/test-results' : '/admin/test-results'
  const { data } = await api.get<CertificateJob>(`${base}/${testId}/certificates/status`)
  return data
}

// ---------------------------------------------------------------- Yuklab olish

/** Blob'ni brauzerda saqlash (server `Content-Disposition` da to'g'ri nom beradi). */
async function download(url: string, fallbackName: string) {
  const res = await api.get(url, { responseType: 'blob' })
  const cd = String(res.headers['content-disposition'] ?? '')
  // filename*=UTF-8''... yoki filename="..." — ikkalasini ham qo'llab-quvvatlaymiz.
  const star = /filename\*=UTF-8''([^;]+)/i.exec(cd)?.[1]
  const plain = /filename="?([^";]+)"?/i.exec(cd)?.[1]
  const name = star ? decodeURIComponent(star) : plain || fallbackName

  const href = URL.createObjectURL(res.data as Blob)
  const a = document.createElement('a')
  a.href = href
  a.download = name
  document.body.appendChild(a)
  a.click()
  a.remove()
  URL.revokeObjectURL(href)
}

/** Bitta sertifikat (standart — PDF; `format='docx'` bilan Word). */
export function downloadCertificate(certificateId: string, teacher = false, format?: 'docx') {
  const base = teacher ? '/teacher/test-results' : '/admin/test-results'
  const q = format ? `?format=${format}` : ''
  return download(`${base}/certificates/${certificateId}/download${q}`, 'sertifikat.pdf')
}

/** Test bo'yicha BARCHA sertifikatlar — bitta ZIP. */
export function downloadAllCertificates(testId: string, teacher = false) {
  const base = teacher ? '/teacher/test-results' : '/admin/test-results'
  return download(`${base}/${testId}/certificates/download`, 'sertifikatlar.zip')
}
