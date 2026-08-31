import { api } from '../client'

/** Bitta o'tish hodisasi — turniket/FaceID qurilmasidan kelgan qayd. */
export interface StudentTurnstileEvent {
  /** "HH:mm" */
  time: string
  /** Yo'nalish: kirdi / chiqdi */
  direction: 'in' | 'out'
  /** Qurilma nomi (qaysi eshikdan o'tgan) */
  deviceName: string
}

/**
 * BITTA o'quvchining turniket tarixi — o'quvchi profilidagi «Turniket» tabi uchun.
 *
 * <p>Ilgari alohida "O'quvchilar turniketi" sahifasi BARCHA o'quvchini bir kun uchun
 * ko'rsatardi; endi savol boshqacha — "SHU o'quvchi qachon kirgan/chiqqan", shuning uchun
 * kesim o'quvchi bo'yicha va kun kalendardan tanlanadi.</p>
 */
export interface StudentTurnstileHistory {
  /** Tanlangan kun "yyyy-MM-dd" */
  date: string
  /** Kalendar chizig'i ko'rsatayotgan oy "yyyy-MM" */
  month: string
  /** Turniket integratsiyasi umuman yoqilganmi (sozlamalarda) */
  enabled: boolean
  /** Oxirgi sinxronizatsiya vaqti (ISO) */
  lastSync: string
  /** Qurilmadagi raqam (employeeNo) — bo'sh bo'lsa qurilma biriktirilmagan */
  deviceUserId: string
  /** Tanlangan kundagi o'tishlar soni */
  passes: number
  /** "HH:mm" — o'sha kungi birinchi kirish */
  firstIn: string
  /** "HH:mm" — o'sha kungi oxirgi chiqish */
  lastOut: string
  /** Tanlangan kundagi hodisalar (vaqt bo'yicha) */
  events: StudentTurnstileEvent[]
  /** Shu OYda hodisa bor kunlar ("yyyy-MM-dd") — kalendarda belgilanadi */
  activeDays: string[]
}

export interface SyncResult {
  ok: boolean
  message: string
  eventsFetched: number
  updated: number
  lastSync: string
}

/** Bitta o'quvchining kirish/chiqish tarixi: tanlangan KUN + shu OYdagi faol kunlar. */
export async function getStudentTurnstileHistory(
  studentId: string,
  date: string,
  month: string,
): Promise<StudentTurnstileHistory> {
  const { data } = await api.get<StudentTurnstileHistory>(
    `/admin/students/turnstile/${studentId}`,
    { params: { date, month } },
  )
  return data
}

export async function syncStudentTurnstile(): Promise<SyncResult> {
  const { data } = await api.post<SyncResult>('/admin/students/turnstile/sync')
  return data
}

export async function setStudentDevice(studentId: string, deviceUserId: string): Promise<void> {
  await api.put('/admin/students/turnstile/device', { studentId, deviceUserId })
}
