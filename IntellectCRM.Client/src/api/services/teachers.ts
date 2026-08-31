import type {
  Credentials,
  MonthSalary,
  MonthStatus,
  SalaryLedger,
  Teacher,
  TeacherAiMetrics,
  TeacherAiRecord,
  TeacherAiResponse,
  TeacherPerformance,
  TeacherRating,
} from '@/types'
import { delay, uid } from '@/lib/utils'
import { api, USE_MOCK } from '../client'
import { teachersMock } from '../mock/teachers'
import { financeMock } from '../mock/finance'

/** newPassword — ixtiyoriy: tahrirda kiritilsa o'qituvchi akkaunti paroli almashtiriladi. */
export type TeacherPayload = Omit<Teacher, 'id'> & { newPassword?: string }

/**
 * Barcha o'qituvchilarni login/parol bilan Excel (.xlsx) ga yuklab oladi (faqat superadmin).
 * Parol faqat o'qituvchi hali kirmagan bo'lsa to'ldiriladi (kirgach bo'sh).
 */
export async function downloadTeacherCredentials(): Promise<void> {
  if (USE_MOCK) {
    alert('Eksport faqat real serverda ishlaydi (VITE_USE_MOCK=false).')
    return
  }
  const res = await api.get('/admin/teachers/export', { responseType: 'blob' })
  const url = URL.createObjectURL(res.data as Blob)
  const a = document.createElement('a')
  a.href = url
  const cd = (res.headers['content-disposition'] as string | undefined) ?? ''
  const m = cd.match(/filename="?([^"]+)"?/)
  a.download = m?.[1] ?? `oqituvchilar_${new Date().toISOString().slice(0, 10)}.xlsx`
  document.body.appendChild(a)
  a.click()
  a.remove()
  URL.revokeObjectURL(url)
}

export async function getTeachers(): Promise<Teacher[]> {
  if (USE_MOCK) {
    await delay()
    return teachersMock
  }
  const { data } = await api.get<Teacher[]>('/admin/teachers')
  return data
}

/**
 * BITTA o'qituvchi id bo'yicha — `getTeachers()` bilan AYNAN bir xil shakl (`Teacher`).
 *
 * ⚠️ Bitta o'qituvchi kerak bo'lganda SHUNI ishlating: ilgari o'qituvchi sahifasi bittasini
 * topish uchun markazning BARCHA o'qituvchilarini tortardi (prodda `Teachers` jadvali
 * 20 000 martadan ko'p sequential scan qilingan). Server Fransiyada — har ortiqcha bayt
 * ~350-400 ms tarmoq vaqtiga qo'shiladi.
 *
 * Arxivlangan o'qituvchi ham qaytadi. Topilmasa server 404 beradi.
 */
export async function getTeacher(id: string): Promise<Teacher> {
  if (USE_MOCK) {
    await delay()
    const found = teachersMock.find((t) => t.id === id)
    if (!found) throw new Error('O\'qituvchi topilmadi')
    return found
  }
  const { data } = await api.get<Teacher>(`/admin/teachers/${id}`)
  return data
}

export async function createTeacher(payload: TeacherPayload): Promise<Teacher> {
  if (USE_MOCK) {
    await delay(300)
    return { ...payload, id: uid() }
  }
  const { data } = await api.post<Teacher>('/admin/teachers', payload)
  return data
}

export async function updateTeacher(id: string, payload: TeacherPayload): Promise<Teacher> {
  if (USE_MOCK) {
    await delay(300)
    return { ...payload, id }
  }
  const { data } = await api.put<Teacher>(`/admin/teachers/${id}`, payload)
  return data
}

/**
 * O'qituvchi rasmini (profil surati) o'rnatadi yoki o'chiradi (`null`).
 *
 * <p>ALOHIDA endpoint — o'quvchidagi `updateStudentPhoto` bilan bir xil sabab: avatarni bosib
 * rasm almashtirilganda to'liq `PUT /teachers/{id}` yuborilsa maosh, toifa, fanlar va ruxsatlar
 * tasodifan bo'shab qolardi.</p>
 */
export async function updateTeacherPhoto(id: string, photoUrl: string | null): Promise<void> {
  if (USE_MOCK) {
    await delay(200)
    return
  }
  await api.put(`/admin/teachers/${id}/photo`, { photoUrl })
}

export async function deleteTeacher(id: string, reasonId?: string): Promise<void> {
  if (USE_MOCK) {
    await delay(200)
    return
  }
  await api.delete(`/admin/teachers/${id}`, { params: reasonId ? { reasonId } : undefined })
}

/** Faqat arxivlangan o'qituvchilar */
export async function getArchivedTeachers(): Promise<Teacher[]> {
  if (USE_MOCK) {
    await delay()
    return []
  }
  const { data } = await api.get<Teacher[]>('/admin/teachers/archived')
  return data
}

/** O'qituvchini arxivga ko'chirish (login bloklanadi) */
export async function archiveTeacher(id: string, reason: string): Promise<void> {
  if (USE_MOCK) {
    await delay(200)
    return
  }
  await api.post(`/admin/teachers/${id}/archive`, { reason })
}

/** Arxivdan qaytarish — ixtiyoriy yangi parol bilan */
export async function restoreTeacher(id: string, newPassword?: string): Promise<void> {
  if (USE_MOCK) {
    await delay(200)
    return
  }
  await api.post(`/admin/teachers/${id}/restore`, { newPassword })
}

/**
 * O'qituvchini VAQTINCHA AKTIV EMAS qilish — tizimga kira olmaydi (mavjud tokeni ham darrov
 * ishlamay qoladi). Arxivlash EMAS: paroli, guruhlari, maoshi va tarixi joyida qoladi,
 * qaytarish bir tugma. PUT — server `teachers:edit` ruxsatini talab qilsin (UI bilan bir xil).
 */
export async function blockTeacher(id: string, note?: string): Promise<void> {
  if (USE_MOCK) {
    await delay(200)
    return
  }
  await api.put(`/admin/teachers/${id}/block`, { note: note ?? '' })
}

/** O'qituvchini qayta faollashtirish — eski paroli bilan odatdagidek kiraveradi. */
export async function unblockTeacher(id: string): Promise<void> {
  if (USE_MOCK) {
    await delay(200)
    return
  }
  await api.put(`/admin/teachers/${id}/unblock`)
}

/** O'qituvchining tizim akkaunti (login/parol) */
export async function getTeacherCredentials(id: string): Promise<Credentials> {
  if (USE_MOCK) {
    await delay(200)
    return { login: 'umarovaziz', password: 'demo23', role: 'teacher' }
  }
  const { data } = await api.get<Credentials>(`/admin/teachers/${id}/credentials`)
  return data
}

/** O'qituvchiga yangi tasodifiy parol generatsiya qiladi — parol bir marta qaytadi. */
export async function resetTeacherPassword(id: string): Promise<Credentials> {
  if (USE_MOCK) {
    await delay(200)
    return { login: 'umarovaziz', password: 'yangi' + Math.random().toString(36).slice(2, 8), role: 'teacher' }
  }
  const { data } = await api.post<Credentials>(`/admin/teachers/${id}/reset-password`)
  return data
}

/** O'qituvchi maoshi bo'yicha batafsil hisob (davr bo'yicha): oma-oy taqsimot */
export async function getSalaryLedger(id: string, from?: string, to?: string): Promise<SalaryLedger> {
  if (USE_MOCK) {
    await delay()
    const teacher = teachersMock.find((t) => t.id === id)
    if (!teacher) throw new Error("O'qituvchi topilmadi")
    const periodFrom = (from ?? `${new Date().getFullYear()}-01-01`).slice(0, 7)
    const toM = (to ?? new Date().toISOString().slice(0, 10)).slice(0, 7)
    // Oylik o'qituvchi boshlagan oydan hisoblanadi — undan oldingi oylar uchun qarz yozilmaydi.
    const fromM =
      teacher.salaryStartMonth && teacher.salaryStartMonth > periodFrom
        ? teacher.salaryStartMonth
        : periodFrom
    const pays = financeMock.filter(
      (t) =>
        t.teacherId === id &&
        t.category === 'salary' &&
        t.date.slice(0, 7) >= fromM &&
        t.date.slice(0, 7) <= toM,
    )
    const months: MonthSalary[] = []
    let m = fromM
    while (m <= toM) {
      const paid = pays.filter((p) => p.date.slice(0, 7) === m).reduce((a, p) => a + p.amount, 0)
      const remaining = teacher.salary - paid
      const status: MonthStatus = remaining <= 0 ? 'paid' : paid > 0 ? 'partial' : 'unpaid'
      months.push({ month: m, expected: teacher.salary, paid, remaining, status })
      const [y, mm] = m.split('-').map(Number)
      m = mm === 12 ? `${y + 1}-01` : `${y}-${String(mm + 1).padStart(2, '0')}`
    }
    const totalExpected = teacher.salary * months.length
    const totalPaid = pays.reduce((a, p) => a + p.amount, 0)
    return {
      teacherId: teacher.id,
      fullName: teacher.fullName,
      salary: teacher.salary,
      totalExpected,
      totalPaid,
      remaining: totalExpected - totalPaid,
      months,
      payments: pays
        .map((t) => ({ date: t.date, amount: t.amount, note: t.note, month: t.month }))
        .sort((a, b) => (a.date < b.date ? 1 : -1)),
    }
  }
  const { data } = await api.get<SalaryLedger>(`/admin/teachers/${id}/salary-ledger`, {
    params: { from, to },
  })
  return data
}

/** Per-guruh maosh sozlamasi (bitta guruh) — yuborish uchun */
export type GroupSalaryItem = {
  groupId: string
  /** '' (umumiy) | 'percent' | 'fixed' */
  mode: string
  percent: number
  fixed: number
}

/**
 * O'qituvchi guruhlarining PER-GURUH maosh sozlamasini saqlaydi (har guruhga alohida foiz yoki qat'iy summa).
 * O'qituvchi oyligi = guruhlar ulushi yig'indisi.
 */
export async function saveGroupSalaries(teacherId: string, items: GroupSalaryItem[]): Promise<void> {
  if (USE_MOCK) {
    await delay(200)
    return
  }
  await api.put(`/admin/teachers/${teacherId}/group-salaries`, { items })
}

/** Barcha faol o'qituvchilarning talaba saqlab qolish statistikasi (lifetime, per-group) */
export async function getTeacherPerformance(): Promise<TeacherPerformance[]> {
  if (USE_MOCK) {
    await delay()
    return []
  }
  const { data } = await api.get<TeacherPerformance[]>('/admin/teachers/performance')
  return data
}

/** Bitta o'qituvchining talaba saqlab qolish statistikasi */
export async function getTeacherPerformanceSingle(id: string): Promise<TeacherPerformance> {
  if (USE_MOCK) {
    await delay()
    return {
      teacherId: id,
      teacherName: '',
      phone: '',
      totalStudents: 0,
      activeStudents: 0,
      frozenStudents: 0,
      leftStudents: 0,
      retentionPercent: 0,
      lossPercent: 0,
      effectivenessScore: 0,
      groupCount: 0,
    }
  }
  const { data } = await api.get<TeacherPerformance>(`/admin/teachers/${id}/performance`)
  return data
}

/** Bitta oy uchun maosh holati (belgilangan/berilgan/qoldiq) — to'lov oynasi uchun */
export async function getSalaryMonth(id: string, month: string): Promise<MonthSalary | null> {
  const ledger = await getSalaryLedger(id, `${month}-01`, `${month}-31`)
  return ledger.months[0] ?? null
}

/* ---------- O'quvchilar reytingi (ball bo'yicha) ---------- */

/**
 * O'qituvchi guruhlaridagi o'quvchilar reytingi: ball = jurnal baholari yig'indisi +
 * bajarilgan baholash mezonlari. Faqat SHU o'qituvchi guruhlaridagi ball hisoblanadi.
 * `month` ("yyyy-MM") berilmasa — Umumiy (barcha vaqt), ya'ni avvalgi xatti-harakat.
 */
export async function getTeacherRating(id: string, month?: string): Promise<TeacherRating> {
  if (USE_MOCK) {
    await delay()
    return {
      teacherId: id, fullName: '', groupsCount: 0, studentsCount: 0, averageBall: 0, rows: [],
      month: month ?? '', months: [],
    }
  }
  const q = month ? `?month=${encodeURIComponent(month)}` : ''
  const { data } = await api.get<TeacherRating>(`/admin/teachers/${id}/rating${q}`)
  return data
}

/* ---------- AI tahlil (o'qituvchi profili) ---------- */

/**
 * O'qituvchining DETERMINISTIK ko'rsatkichlari (AI'siz ham ko'rinadi): o'quvchi oqimi
 * (kelgan/ketgan), ketish sabablari, jurnalni o'z vaqtida to'ldirish, baholar dinamikasi,
 * testlar, davomat — oxirgi 12 oy.
 */
export async function getTeacherAiSnapshot(id: string): Promise<TeacherAiMetrics> {
  const { data } = await api.get<TeacherAiMetrics>(`/admin/teachers/${id}/ai-snapshot`)
  return data
}

/** O'qituvchining saqlangan AI tahlillari (eng yangisi birinchi). */
export async function getTeacherAiAnalyses(id: string): Promise<TeacherAiRecord[]> {
  const { data } = await api.get<TeacherAiRecord[]>(`/admin/teachers/${id}/ai-analyses`)
  return data
}

/** Yangi AI tahlil yaratish (kuniga bir marta — bugungi bo'lsa mavjudi qaytadi). */
export async function runTeacherAiAnalysis(id: string): Promise<TeacherAiResponse> {
  const { data } = await api.post<TeacherAiResponse>(`/admin/teachers/${id}/ai-analysis`)
  return data
}
