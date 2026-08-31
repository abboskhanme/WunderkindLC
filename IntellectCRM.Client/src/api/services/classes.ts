import type {
  Group,
  GroupAiMetrics,
  GroupAiRecord,
  GroupAiResponse,
  GroupFillRow,
  GroupMember,
  StudentGroupMembership,
} from '@/types'
import { delay, uid } from '@/lib/utils'
import { api, USE_MOCK } from '../client'
import { classesMock } from '../mock/classes'

export type ClassPayload = Omit<Group, 'id'>

/**
 * Guruhlar ro'yxati. Standart holatda faqat FAOL guruhlar; `includeArchived=true` bo'lsa
 * tugatilgan (arxivlangan) guruhlar ham qo'shiladi — o'qituvchi profilidagi "Tugatilgan guruhlar",
 * o'quvchilar ro'yxatidagi guruh filtri va arxiv guruh sahifasini ochish uchun.
 */
export async function getClasses(includeArchived = false, teacherId?: string): Promise<Group[]> {
  if (USE_MOCK) {
    await delay()
    return teacherId ? classesMock.filter((c) => c.teacherId === teacherId) : classesMock
  }
  // ⚠️ `teacherId` berilsa filtr SERVERDA bajariladi. Ilgari o'qituvchi sahifasi markazning
  // BARCHA guruhini tortib, brauzerda filtrlardi — bu tarmoqdan keraksiz ma'lumot o'tkazardi.
  const params: Record<string, unknown> = {}
  if (includeArchived) params.includeArchived = true
  if (teacherId) params.teacherId = teacherId
  const { data } = await api.get<Group[]>('/admin/classes', {
    params: Object.keys(params).length > 0 ? params : undefined,
  })
  return data
}

/**
 * BITTA guruh id bo'yicha — `getClasses()` bilan AYNAN bir xil shakl (`Group`).
 *
 * ⚠️ Bitta guruh kerak bo'lganda SHUNI ishlating, `getClasses()` ni EMAS: ilgari guruh
 * sahifasi bitta guruhni topish uchun markazning BARCHA guruhlarini tortardi. Server
 * Fransiyada, foydalanuvchi O'zbekistonda — har ortiqcha bayt va har ortiqcha so'rov
 * ~350-400 ms tarmoq vaqtiga tushadi.
 *
 * Arxivlangan guruh ham qaytadi (tugatilgan guruh sahifasi ochilishi kerak).
 * Topilmasa server 404 beradi — chaqiruvchi `catch` bilan ishlaydi.
 */
export async function getClass(id: string): Promise<Group> {
  if (USE_MOCK) {
    await delay()
    const found = classesMock.find((c) => c.id === id)
    if (!found) throw new Error('Guruh topilmadi')
    return found
  }
  const { data } = await api.get<Group>(`/admin/classes/${id}`)
  return data
}

/** Guruhning FAOL a'zosi — SMS modali uchun (telefonlar + a'zolik holati + SHU GURUH balansi). */
export interface GroupSmsRecipient {
  studentId: string
  fullName: string
  phone: string
  parentPhone: string
  fatherPhone: string
  motherPhone: string
  status: string
  balance: number
}

/**
 * Guruh a'zolarining SMS uchun kerakli ma'lumoti.
 *
 * ⚠️ Ilgari SMS modali `getStudents()` bilan markazning BARCHA o'quvchisini (to'liq entity —
 * passport, manzil, chegirma va h.k.) tortib olib, brauzerda ~15 a'zoni filtrlardi. Endi
 * server faqat shu guruhning faol a'zolarini va faqat kerakli maydonlarni qaytaradi.
 * Balans — a'zolar ro'yxatidagi bilan bir xil manbadan (SHU GURUH bo'yicha).
 */
export async function getGroupSmsRecipients(id: string): Promise<GroupSmsRecipient[]> {
  if (USE_MOCK) {
    await delay()
    return []
  }
  const { data } = await api.get<GroupSmsRecipient[]>(`/admin/classes/${id}/sms-recipients`)
  return data
}

export async function createClass(payload: ClassPayload, force?: boolean): Promise<Group> {
  if (USE_MOCK) {
    await delay(200)
    return { ...payload, id: uid() }
  }
  const { data } = await api.post<Group>('/admin/classes', payload, {
    params: force ? { force: true } : undefined,
  })
  return data
}

/**
 * Guruhni yangilash. Oylik to'lov o'zgargan bo'lsa, `applyFee` orqali yangi narx joriy oy
 * o'quvchilariga qo'llanishini boshqaramiz: true = joriy oydan, false = keyingi oydan.
 */
export async function updateClass(
  id: string,
  payload: ClassPayload,
  applyFee?: boolean,
  force?: boolean,
): Promise<Group> {
  if (USE_MOCK) {
    await delay(200)
    return { ...payload, id }
  }
  const params: Record<string, unknown> = {}
  if (applyFee !== undefined) params.applyFee = applyFee
  if (force) params.force = true
  const { data } = await api.put<Group>(`/admin/classes/${id}`, payload, {
    params: Object.keys(params).length ? params : undefined,
  })
  return data
}

export async function deleteClass(id: string, reasonId?: string): Promise<void> {
  if (USE_MOCK) {
    await delay(200)
    return
  }
  await api.delete(`/admin/classes/${id}`, { params: reasonId ? { reasonId } : undefined })
}

/** Arxivlangan guruhlar ro'yxati. */
export async function getArchivedClasses(): Promise<Group[]> {
  if (USE_MOCK) {
    await delay()
    return []
  }
  const { data } = await api.get<Group[]>('/admin/classes/archived')
  return data
}

/** Guruhni arxivlash — o'quvchilari ham arxivlanadi. */
export async function archiveClass(id: string): Promise<{ archivedStudents: number }> {
  const { data } = await api.post<{ archivedStudents: number }>(`/admin/classes/${id}/archive`)
  return data
}

/** Guruhni arxivdan chiqarish — guruh bilan arxivlangan o'quvchilar ham qaytariladi. */
export async function unarchiveClass(id: string): Promise<{ restoredStudents: number }> {
  const { data } = await api.post<{ restoredStudents: number }>(`/admin/classes/${id}/unarchive`)
  return data
}

/**
 * Guruhni VAQTINCHA BLOKLASH — o'qituvchi ilovasida umuman ko'rinmay qoladi (ro'yxat, jurnal,
 * baholash, testlar, guruh chati). Arxivlash EMAS: o'quvchilar, a'zoliklar va oylik hisobi
 * tegilmaydi, guruh admin panelida faol ro'yxatda qolaveradi.
 *
 * PUT (POST emas) — server tomonda `classes:edit` ruxsatiga tushsin (UI darvozasi bilan bir xil).
 */
export async function blockClass(id: string, note?: string): Promise<void> {
  await api.put(`/admin/classes/${id}/block`, { note: note ?? '' })
}

/** Guruhni blokdan chiqarish — o'qituvchida yana odatdagidek ko'rinadi. */
export async function unblockClass(id: string): Promise<void> {
  await api.put(`/admin/classes/${id}/unblock`)
}

/* ---------- Guruh a'zoligi (many-to-many) ---------- */

/** Guruh a'zolari ro'yxati. */
export async function getGroupMembers(id: string): Promise<GroupMember[]> {
  if (USE_MOCK) {
    await delay()
    return []
  }
  const { data } = await api.get<GroupMember[]>(`/admin/classes/${id}/members`)
  return data
}

/**
 * Guruhga o'quvchi qo'shish. To'lgan/allaqachon a'zo bo'lsa server 409/400 qaytaradi.
 * Arxivdagi o'quvchi qo'shilsa — server uni ARXIVDAN CHIQARADI va `restored: true` qaytaradi.
 */
export async function addGroupMember(
  id: string,
  studentId: string,
  joinedAt?: string,
): Promise<{ ok: boolean; restored?: boolean }> {
  if (USE_MOCK) {
    await delay(150)
    return { ok: true }
  }
  const { data } = await api.post<{ ok: boolean; restored?: boolean }>(`/admin/classes/${id}/members`, {
    studentId,
    joinedAt,
  })
  return data
}

/** Guruhdan o'quvchini chiqarish (left deb belgilanadi). Sabab (ixtiyoriy) auditga yoziladi. */
export async function removeGroupMember(id: string, studentId: string, reasonId?: string): Promise<void> {
  if (USE_MOCK) {
    await delay(150)
    return
  }
  await api.delete(`/admin/classes/${id}/members/${studentId}`, {
    params: reasonId ? { reasonId } : undefined,
  })
}

/**
 * A'zolikni AKTIVLASHTIRISH (sinov → faol). Birinchi (qisman) oy to'lovi shu sanadan avtomatik hisoblanadi.
 *
 * `retentionBonus` — ushlab turish bonusi shu guruh FANI bo'yicha hisoblansinmi:
 * - `true` — hisoblanadi, sanoq AKTIVLASHTIRILGAN OYdan boshlanadi;
 * - `false` — bu fan bonus hisobotida ko'rinmaydi;
 * - berilmasa (`undefined`) — tegilmaydi (eski xatti-harakat saqlanadi).
 */
export async function activateMember(
  id: string,
  studentId: string,
  date: string,
  retentionBonus?: boolean,
): Promise<void> {
  if (USE_MOCK) {
    await delay(150)
    return
  }
  const body: Record<string, unknown> = { date }
  if (retentionBonus !== undefined) body.retentionBonus = retentionBonus
  await api.post(`/admin/classes/${id}/members/${studentId}/activate`, body)
}

/** A'zolikni MUZLATISH — kiritilgan sanadan boshlab oylik to'lov hisoblanmaydi. Sabab (ixtiyoriy). */
export async function freezeMember(id: string, studentId: string, date: string, reasonId?: string): Promise<void> {
  if (USE_MOCK) {
    await delay(150)
    return
  }
  await api.post(`/admin/classes/${id}/members/${studentId}/freeze`, { date, reasonId })
}

/**
 * O'quvchini boshqa guruhga o'tkazish: joriy guruh a'zoligi `freezeDate`dan muzlatiladi,
 * maqsad guruh (`toGroupId`) `activateDate`dan aktivlashtiriladi (ikkalasi ham qisman oy
 * to'lovi bilan — oddiy muzlatish/aktivlashtirish bilan bir xil hisob-kitob).
 */
export async function transferMember(
  fromGroupId: string,
  studentId: string,
  toGroupId: string,
  freezeDate: string,
  activateDate: string,
): Promise<void> {
  if (USE_MOCK) {
    await delay(150)
    return
  }
  await api.post(`/admin/classes/${fromGroupId}/members/${studentId}/transfer`, {
    toGroupId,
    freezeDate,
    activateDate,
  })
}

/**
 * OMMAVIY (bir paytda ko'p o'quvchi) muzlatish/aktivlashtirish natijasi.
 * Amal BITTA xato tufayli TO'XTAMAYDI — shuning uchun javob "nima bo'ldi" ni to'liq ochib beradi.
 */
export interface BulkMembershipResult {
  /** So'ralgan o'quvchilar soni. */
  requested: number
  /** Mos kelgan a'zoliklar (bitta o'quvchi bir necha guruhda bo'lishi mumkin). */
  memberships: number
  /** Haqiqatan o'zgargan a'zoliklar. */
  changed: number
  /** Haqiqatan o'zgargan o'quvchilar. */
  students: number
  /** Allaqachon kerakli holatda bo'lgani uchun o'tkazib yuborilgani. */
  skipped: number
  failed: number
  /** Umuman faol a'zoligi topilmagan o'quvchilar. */
  noMembership: number
  /** Muzlatishda balansga qaytarilgan umumiy summa. */
  restored: number
  movedAdvance: number
  catchUpMonths: number
  errors: string[]
}

const EMPTY_BULK: BulkMembershipResult = {
  requested: 0, memberships: 0, changed: 0, students: 0, skipped: 0, failed: 0,
  noMembership: 0, restored: 0, movedAdvance: 0, catchUpMonths: 0, errors: [],
}

/**
 * Tanlangan o'quvchilarni BIR PAYTDA muzlatish.
 *
 * `groupId` berilsa — faqat SHU guruhdagi a'zoliklar (guruh sahifasi);
 * `null` bo'lsa — o'quvchilarning BARCHA guruhlardagi faol a'zoliklari (o'quvchilar ro'yxati).
 * Hisob-kitob yakka "Muzlatish" bilan AYNAN bir xil (server bitta manbadan ishlaydi).
 */
export async function bulkFreezeMembers(
  groupId: string | null,
  studentIds: string[],
  date: string,
  reasonId?: string,
): Promise<BulkMembershipResult> {
  if (USE_MOCK) {
    await delay(200)
    return { ...EMPTY_BULK, requested: studentIds.length, memberships: studentIds.length, changed: studentIds.length, students: studentIds.length }
  }
  const url = groupId
    ? `/admin/classes/${groupId}/members/bulk-freeze`
    : '/admin/classes/members/bulk-freeze'
  const { data } = await api.post<BulkMembershipResult>(url, { studentIds, date, reasonId })
  return data
}

/**
 * Tanlangan o'quvchilarni BIR PAYTDA aktivlashtirish (sinov/muzlatilgan → faol).
 * Har biriga qisman oy to'lovi yakka aktivlashtirishdagidek hisoblanadi.
 */
export async function bulkActivateMembers(
  groupId: string | null,
  studentIds: string[],
  date: string,
  retentionBonus?: boolean,
): Promise<BulkMembershipResult> {
  if (USE_MOCK) {
    await delay(200)
    return { ...EMPTY_BULK, requested: studentIds.length, memberships: studentIds.length, changed: studentIds.length, students: studentIds.length }
  }
  const url = groupId
    ? `/admin/classes/${groupId}/members/bulk-activate`
    : '/admin/classes/members/bulk-activate'
  const body: Record<string, unknown> = { studentIds, date }
  if (retentionBonus !== undefined) body.retentionBonus = retentionBonus
  const { data } = await api.post<BulkMembershipResult>(url, body)
  return data
}

/**
 * Ommaviy amal natijasini foydalanuvchiga ko'rsatiladigan QISQA matnga aylantiradi.
 * "Jimgina tushib qolgan o'quvchi bo'lmasin" — o'tkazib yuborilgani ham, xatosi ham yoziladi.
 */
export function bulkMembershipSummary(r: BulkMembershipResult, action: 'freeze' | 'activate'): string {
  const verb = action === 'freeze' ? 'muzlatildi' : 'aktivlashtirildi'
  const parts = [`${r.changed} ta a'zolik ${verb}` + (r.students && r.students !== r.changed ? ` (${r.students} ta o'quvchi)` : '')]
  if (r.skipped > 0) parts.push(`${r.skipped} tasi allaqachon shu holatda edi`)
  if (r.noMembership > 0) parts.push(`${r.noMembership} tasida mos a'zolik topilmadi`)
  if (r.failed > 0) parts.push(`${r.failed} tasida xato`)
  let text = parts.join(', ') + '.'
  if (r.errors.length > 0) text += `\n\n${r.errors.join('\n')}`
  return text
}

/** A'zolikni SINOVGA qaytarish (active/frozen → trial). Sabab (ixtiyoriy). */
export async function returnMemberToTrial(id: string, studentId: string, reasonId?: string): Promise<void> {
  if (USE_MOCK) {
    await delay(150)
    return
  }
  await api.post(`/admin/classes/${id}/members/${studentId}/return-trial`, { reasonId })
}

/** O'quvchining barcha guruh a'zoliklari. */
export async function getStudentGroups(studentId: string): Promise<StudentGroupMembership[]> {
  if (USE_MOCK) {
    await delay()
    return []
  }
  const { data } = await api.get<StudentGroupMembership[]>(
    `/admin/classes/student/${studentId}/groups`,
  )
  return data
}

/** Guruhlar to'ldirilishi (sig'im / a'zolar / bo'sh o'rin). */
export async function getGroupFill(): Promise<GroupFillRow[]> {
  if (USE_MOCK) {
    await delay()
    return []
  }
  const { data } = await api.get<GroupFillRow[]>('/admin/classes/fill')
  return data
}

export interface CloseGroupResult {
  ok: boolean
  groupId: string
  groupName: string
  /** Muzlatish sanasi — qarzdorlik shu sanagacha hisoblangan. */
  freezeDate: string
  frozenCount: number
  alreadyFrozen: number
  /** Sinovdagi (hisobsiz) a'zoliklar — ular guruhdan chiqarildi. */
  trialClosed: number
  /** Muzlatishdan keyingi oylar uchun bekor qilingan (balansga qaytarilgan) summa. */
  restoredCharges: number
}

/**
 * Guruhni YOPISH: barcha faol a'zolar berilgan sanadan muzlatiladi (qarzdorlik shu sanagacha
 * hisoblanadi) va guruh arxivga olinadi. Sertifikat berilmaydi, yangi guruh ochilmaydi.
 */
export async function closeClass(
  id: string,
  opts: { date: string; reasonId?: string },
): Promise<CloseGroupResult> {
  if (USE_MOCK) {
    await delay(300)
    return {
      ok: true, groupId: id, groupName: '', freezeDate: opts.date,
      frozenCount: 0, alreadyFrozen: 0, trialClosed: 0, restoredCharges: 0,
    }
  }
  const { data } = await api.post<CloseGroupResult>(`/admin/classes/${id}/close`, {
    date: opts.date,
    reasonId: opts.reasonId ?? null,
  })
  return data
}

export interface CompleteAndTransferResult {
  ok: boolean
  archivedGroupId: string
  newGroupId: string
  certificatesGenerated: number
  enrolledInNew: number
  targetCourseName?: string
  /** Eski guruh yopilgan (a'zoliklar muzlatilgan) sana. */
  closeDate: string
  /** Yangi guruhda aktivlashtirish sanasi ('' — aktivlashtirilmagan, sinovda qoldi). */
  activateDate: string
  /** Eski guruhga qisman oylik hisobi yozilgan a'zolar soni. */
  chargedOldGroup: number
  /** Yopishdan keyingi oylar uchun bekor qilingan (balansga qaytarilgan) summa. */
  restoredCharges: number
  /** Yangi guruhda darhol aktivlashtirilgan a'zolar soni. */
  activatedInNew: number
  /** Eski guruhdan yangi guruhga ko'chirilgan avans summasi. */
  movedAdvance: number
  /** Yangi guruhga KO'CHIRILMAGAN a'zolar soni (eski guruhda sinovda yoki muzlatilgan edi). */
  skippedNotActive: number
}

/** Guruhni yakunlash (Hybrid): eski guruh arxivlanadi, maqsad kurs bilan yangi guruh ochiladi, sertifikat beriladi. */
export async function completeAndTransferClass(
  id: string,
  opts?: {
    /** Yangi guruhga avtomatik qo'shish — FAQAT eski guruhda AKTIV bo'lgan a'zolar ko'chiriladi
     *  (sinovdagi va muzlatilganlar ko'chirilmaydi). */
    autoEnrollNewGroup?: boolean
    newGroupName?: string
    completionNotes?: string
    /** Yangi guruh kursi. Bo'sh bo'lsa — eski kurs qayta ishlatiladi. */
    targetCourseId?: string
    /** ESKI guruh yopiladigan sana — hisob AYNAN shu sanagacha eski guruhga yoziladi. */
    closeDate?: string
    /** YANGI guruhda aktivlashtirish sanasi (bo'sh — closeDate). */
    activateDate?: string
    /** Yangi guruhda darhol aktivlashtirish (false — "sinov" bo'lib qoladi, to'lov hisoblanmaydi). */
    activateInNewGroup?: boolean
  },
): Promise<CompleteAndTransferResult> {
  if (USE_MOCK) {
    await delay(300)
    return {
      ok: true,
      archivedGroupId: id,
      newGroupId: 'mock-new-group',
      certificatesGenerated: 5,
      enrolledInNew: 5,
      targetCourseName: 'Elementary',
      closeDate: opts?.closeDate ?? '',
      activateDate: opts?.activateDate ?? '',
      chargedOldGroup: 5,
      restoredCharges: 0,
      activatedInNew: 5,
      movedAdvance: 0,
      skippedNotActive: 0,
    }
  }
  const { data } = await api.post<CompleteAndTransferResult>(
    `/admin/classes/${id}/complete-and-transfer`,
    {
      autoEnrollNewGroup: opts?.autoEnrollNewGroup ?? true,
      newGroupName: opts?.newGroupName ?? null,
      completionNotes: opts?.completionNotes ?? null,
      targetCourseId: opts?.targetCourseId ?? null,
      closeDate: opts?.closeDate ?? null,
      activateDate: opts?.activateDate ?? null,
      activateInNewGroup: opts?.activateInNewGroup ?? true,
    },
  )
  return data
}

/* ---------- AI tahlil (guruh sahifasi) ---------- */

/**
 * Guruhning DETERMINISTIK ko'rsatkichlari (AI'siz ham ko'rinadi): a'zolik oqimi va ketish
 * sabablari, davomat, jurnal intizomi, o'zlashtirish, imtihonlar, to'lovlar (moliya ruxsati
 * bo'lsa), dastur qamrovi, o'quvchilar kesimi — oxirgi 12 oy.
 */
export async function getGroupAiSnapshot(id: string): Promise<GroupAiMetrics> {
  const { data } = await api.get<GroupAiMetrics>(`/admin/classes/${id}/ai-snapshot`)
  return data
}

/** Guruhning saqlangan AI tahlillari (eng yangisi birinchi). */
export async function getGroupAiAnalyses(id: string): Promise<GroupAiRecord[]> {
  const { data } = await api.get<GroupAiRecord[]>(`/admin/classes/${id}/ai-analyses`)
  return data
}

/** Yangi AI tahlil yaratish (kuniga bir marta — bugungi bo'lsa mavjudi qaytadi). */
export async function runGroupAiAnalysis(id: string): Promise<GroupAiResponse> {
  const { data } = await api.post<GroupAiResponse>(`/admin/classes/${id}/ai-analysis`)
  return data
}
