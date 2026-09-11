import { api } from '../client'
import type { Curriculum, CurriculumItem, LessonType } from '@/types'

/** O'quv dasturi (modul → mavzu → dars → topshiriq) — Kurs (Subject)dan MUSTAQIL, standalone.
 *  Bir dastur bir nechta kursga, bir kurs bir nechta dasturga biriktirilishi mumkin (ko'p-ko'pga —
 *  biriktirish `api/services/subjects.ts`da, chunki bu Kurs resursining bir qismi). */

// ---- O'quv dasturlari ro'yxati (top-level) ----

export interface CurriculumSummary {
  id: string
  name: string
  note: string
  order: number
  createdAt: string
  moduleCount: number
  topicCount: number
  itemCount: number
  readyItemCount: number
  /** Nechta kursga biriktirilgan */
  subjectCount: number
}

export async function listCurricula(): Promise<CurriculumSummary[]> {
  const { data } = await api.get<CurriculumSummary[]>('/admin/curriculum')
  return data
}
export async function createCurriculum(name: string, note = ''): Promise<{ id: string }> {
  const { data } = await api.post<{ id: string }>('/admin/curriculum', { name, note })
  return data
}
export async function updateCurriculum(id: string, name: string, note = ''): Promise<void> {
  await api.put(`/admin/curriculum/${id}`, { name, note })
}
export async function deleteCurriculum(id: string): Promise<void> {
  await api.delete(`/admin/curriculum/${id}`)
}

// ---- Topshiriq kontenti (video/matn/audio/lug'at/test) ----
export interface VocabEntry {
  term: string
  meaning: string
}
export interface CourseQuestion {
  id: string
  text: string
  options: string[]
  correctIndex: number
}
export interface CourseItemDetail {
  id: string
  lessonId: string
  text: string
  note: string
  order: number
  /** Topshiriq turi — bu yerda o'zgarmaydi (faqat ko'rsatish uchun; o'zgartirish uchun updateItem). */
  type: LessonType
  videoUrl: string
  audioUrl: string
  textContent: string
  pdfUrl: string
  pdfName: string
  meta: string
  vocab: VocabEntry[]
  questions: CourseQuestion[]
  /** Interaktiv mashq turi ("sentence-order", "reading-choice", ...) — "exercise" turidagi topshiriqda. */
  exerciseKind: string
  /** Interaktiv mashq mazmuni (JSON) — konstruktor o'qiydi/yozadi. */
  exerciseJson: string
}
/** MUHIM: `type` yo'q — bu yerda o'zgartirilmaydi (buning uchun `updateItem`).
 *  `exerciseKind`/`exerciseJson` berilmasa (undefined) — server ularga TEGMAYDI. */
export interface SaveItemContent {
  text: string
  videoUrl?: string
  audioUrl?: string
  textContent?: string
  pdfUrl?: string
  pdfName?: string
  meta?: string
  vocab?: VocabEntry[]
  questions?: CourseQuestion[]
  exerciseKind?: string
  exerciseJson?: string
}

/** Bitta topshiriqning to'liq kontentini o'qish (tahrirlovchi/ko'rish uchun). */
export async function getCourseItem(id: string): Promise<CourseItemDetail> {
  const { data } = await api.get<CourseItemDetail>(`/admin/curriculum/item/${id}`)
  return data
}
/** Topshiriq kontentini saqlash (nom + kontent + lug'at + test savollari). */
export async function saveItemContent(id: string, payload: SaveItemContent): Promise<void> {
  await api.put(`/admin/curriculum/items/${id}/content`, payload)
}

/** Bitta o'quv dasturining to'liq daraxti. */
export async function getCurriculum(curriculumId: string): Promise<Curriculum> {
  const { data } = await api.get<Curriculum>(`/admin/curriculum/${curriculumId}`)
  return data
}

/** Bitta KURSGA (Subject) biriktirilgan BARCHA dasturlar birlashtirilgan daraxt sifatida
 *  (StudentDetailPage "O'quv dasturi" ko'rinishi uchun — `curriculumId` emas, `subjectId` beriladi). */
export async function getSubjectCurriculumTree(subjectId: string): Promise<Curriculum> {
  const { data } = await api.get<Curriculum>(`/admin/curriculum/subject/${subjectId}/tree`)
  return data
}

// ---- Modul (dastur ichidagi 1-bosqich) ----
export async function createModule(curriculumId: string, name: string, note = ''): Promise<{ id: string }> {
  const { data } = await api.post<{ id: string }>(`/admin/curriculum/${curriculumId}/modules`, { name, note })
  return data
}
export async function updateModule(id: string, name: string, note = ''): Promise<void> {
  await api.put(`/admin/curriculum/modules/${id}`, { name, note })
}
export async function deleteModule(id: string): Promise<void> {
  await api.delete(`/admin/curriculum/modules/${id}`)
}

// ---- Mavzu ----
export async function createTopic(moduleId: string, title: string, note = ''): Promise<{ id: string }> {
  const { data } = await api.post<{ id: string }>(`/admin/curriculum/modules/${moduleId}/topics`, { title, note })
  return data
}
export async function updateTopic(id: string, title: string, note = ''): Promise<void> {
  await api.put(`/admin/curriculum/topics/${id}`, { title, note })
}
export async function deleteTopic(id: string): Promise<void> {
  await api.delete(`/admin/curriculum/topics/${id}`)
}

// ---- Dars ----
export async function createLesson(
  topicId: string, title: string, note = '',
): Promise<{ id: string }> {
  const { data } = await api.post<{ id: string }>(
    `/admin/curriculum/topics/${topicId}/lessons`, { title, note },
  )
  return data
}
export async function updateLesson(id: string, title: string, note = ''): Promise<void> {
  await api.put(`/admin/curriculum/lessons/${id}`, { title, note })
}
export async function deleteLesson(id: string): Promise<void> {
  await api.delete(`/admin/curriculum/lessons/${id}`)
}

// ---- Topshiriq — o'z turini tanlaydi (video|matn|audio|pdf|lug'at|test), keyin ham o'zgartiriladi ----
export async function createItem(
  lessonId: string, text: string, type: LessonType, note = '', exerciseKind = '',
): Promise<{ id: string }> {
  const { data } = await api.post<{ id: string }>(
    `/admin/curriculum/lessons/${lessonId}/items`, { text, note, type, exerciseKind },
  )
  return data
}
/** Bir nechta topshiriqni bir zumda yaratadi — barchasi BITTA turda (har bir nom alohida band bo'ladi). */
export async function createItemsBulk(
  lessonId: string, texts: string[], type: LessonType,
): Promise<CurriculumItem[]> {
  const { data } = await api.post<CurriculumItem[]>(
    `/admin/curriculum/lessons/${lessonId}/items/bulk`, { texts, type },
  )
  return data
}
/** Nom/izoh (va ixtiyoriy ravishda — `type` berilsa — turini ham) yangilaydi. */
export async function updateItem(id: string, text: string, note = '', type?: LessonType): Promise<void> {
  await api.put(`/admin/curriculum/items/${id}`, { text, note, type })
}
export async function deleteItem(id: string): Promise<void> {
  await api.delete(`/admin/curriculum/items/${id}`)
}

// ---- O'quvchi progressi (bajarilgan band id'lari) ----
export async function getProgress(subjectId: string, studentId: string): Promise<string[]> {
  const { data } = await api.get<string[]>(`/admin/curriculum/subject/${subjectId}/progress/${studentId}`)
  return data
}
export async function setProgress(studentId: string, itemId: string, done: boolean): Promise<void> {
  await api.post(`/admin/curriculum/progress`, { studentId, itemId, done })
}

// ---- O'quvchining topshiriq URINISHLARI (ilovada ishlagan natijalari) ----
// Har urinish alohida yozuv — o'quvchi bir topshiriqni bir necha marta ishlasa, tarix ko'rinadi.

/** Bitta savol/element bo'yicha o'quvchi javobi. */
export interface AttemptAnswer {
  index: number
  prompt: string
  answer: string
  expected: string
  ok: boolean
  /** Shu savolga sarflangan vaqt (soniya). */
  sec: number
}

/** Bitta urinish — sillabusdagi joyi (dastur → modul → mavzu → dars → topshiriq) bilan. */
export interface StudentAttempt {
  id: string
  itemId: string
  itemText: string
  itemType: string
  lessonTitle: string
  topicTitle: string
  moduleName: string
  curriculumName: string
  groupName: string
  /** exercise — interaktiv mashq · test — dars testi · view — ko'rildi (ballsiz). */
  section: 'exercise' | 'test' | 'view' | string
  exerciseKind: string
  attemptNo: number
  correct: number
  total: number
  scorePct: number
  durationSec: number
  finishedAt: string
  /** Nechta javob tafsiloti saqlangan (0 bo'lsa "batafsil" ochilmaydi). */
  answerCount: number
}

export interface StudentAttempts {
  /** Nechta HAR XIL topshiriq ishlangani (urinishlar emas). */
  itemCount: number
  attemptCount: number
  /** Ballanadigan urinishlar soni (view'dan tashqari). */
  gradedCount: number
  avgScorePct: number
  totalMinutes: number
  attempts: StudentAttempt[]
}

export async function getStudentAttempts(studentId: string): Promise<StudentAttempts> {
  const { data } = await api.get<StudentAttempts>(`/admin/curriculum/student/${studentId}/attempts`)
  return data
}

/** Bitta urinishning to'liq javoblari (modalda ochiladi). */
export async function getAttemptDetail(attemptId: string): Promise<{ attempt: StudentAttempt; answers: AttemptAnswer[] }> {
  const { data } = await api.get<{ attempt: StudentAttempt; answers: AttemptAnswer[] }>(`/admin/curriculum/attempt/${attemptId}`)
  return data
}

// ---- Guruh o'quv dasturi (darsda o'tilgan bandlar + tugatish prognozi) ----
// Guruh kursiga BIR NECHTA dastur biriktirilgan bo'lsa — hammasi shu bitta ro'yxatga birlashtiriladi.

export interface GroupCurriculumItem {
  id: string
  text: string
  note: string
  order: number
  covered: boolean
  coveredDate: string
}
export interface GroupCurriculumTopic {
  id: string
  title: string
  note: string
  order: number
  items: GroupCurriculumItem[]
}
export interface GroupCurriculumModule {
  id: string
  name: string
  note: string
  order: number
  topics: GroupCurriculumTopic[]
}
export interface GroupCurriculum {
  groupId: string
  courseId: string
  courseName: string
  totalItems: number
  coveredCount: number
  revisionLessons: number
  totalLessons: number
  remainingItems: number
  estLessonsLeft: number
  lessonsPerWeek: number
  /** ISO sana yoki null — taxminiy tugash sanasi */
  estFinishDate: string | null
  modules: GroupCurriculumModule[]
}

export async function getGroupCurriculum(groupId: string): Promise<GroupCurriculum> {
  const { data } = await api.get<GroupCurriculum>(`/admin/curriculum/group/${groupId}`)
  return data
}

export async function setGroupCover(groupId: string, itemId: string, covered: boolean): Promise<void> {
  await api.post(`/admin/curriculum/group/${groupId}/cover`, { itemId, covered })
}

export async function changeGroupRevision(groupId: string, delta: number): Promise<{ revisionLessons: number }> {
  const { data } = await api.post<{ ok: boolean; revisionLessons: number }>(
    `/admin/curriculum/group/${groupId}/revision`,
    { delta },
  )
  return { revisionLessons: data.revisionLessons }
}

// ---- O'quvchi darslar tarixi (o'tilgan mavzular jadvali, eng yangisi birinchi) ----

export interface CoverageLogEntry {
  date: string
  courseName: string
  groupName: string
  moduleName: string
  topicTitle: string
  itemText: string
  isRevision: boolean
}

export async function getStudentCoverageLog(studentId: string): Promise<CoverageLogEntry[]> {
  const { data } = await api.get<CoverageLogEntry[]>(`/admin/curriculum/student/${studentId}/coverage-log`)
  return data
}

// ---- Excel import (shablon + fayl) ----

/** Excel importi natijasi: yaratilgan modul/mavzu/dars soni + xato qatorlar. Topshiriqlar Excel
 *  orqali yaratilmaydi — ular import'dan keyin qo'lda (bir nechtasini birdan) qo'shiladi. */
export interface CurriculumExcelImportResult {
  modules: number
  topics: number
  lessons: number
  skipped: number
  errors: { row: number; message: string }[]
}

/** O'quv dasturini ommaviy kiritish uchun Excel shablonini yuklab oladi (.xlsx). */
export async function downloadCurriculumImportTemplate(): Promise<void> {
  const res = await api.get('/admin/curriculum/import-template', { responseType: 'blob' })
  const url = URL.createObjectURL(res.data as Blob)
  const a = document.createElement('a')
  a.href = url
  a.download = 'oquv_dasturi_shablon.xlsx'
  document.body.appendChild(a)
  a.click()
  a.remove()
  URL.revokeObjectURL(url)
}

/** To'ldirilgan Excel (.xlsx) shablonidan o'quv dasturi skeletini (Modul→Mavzu→Dars) yuklaydi.
 *  replace=true — mavjud dastur o'chirilib almashtiriladi; aks holda qo'shiladi. */
export async function importCurriculumExcel(
  curriculumId: string,
  file: File,
  replace: boolean,
): Promise<CurriculumExcelImportResult> {
  const fd = new FormData()
  fd.append('file', file)
  const { data } = await api.post<CurriculumExcelImportResult>(
    `/admin/curriculum/${curriculumId}/import-excel?replace=${replace}`,
    fd,
    { headers: { 'Content-Type': 'multipart/form-data' } },
  )
  return data
}

// ---- Modul(lar)ni boshqa dastur(lar)ga nusxalash / birlashtirish ----

// Bir (modul, dastur) juftligi natijasi. merged=true — mavjud modulga birlashtirildi;
// added* — QO'SHILGAN yangi elementlar soni (bir xillari takrorlanmagan).
export interface CopyModuleResult {
  moduleId: string
  moduleName: string
  curriculumId: string
  curriculumName: string | null
  ok: boolean
  merged: boolean
  error: string | null
  addedTopics: number
  addedLessons: number
  addedItems: number
}

export interface CopyModulesResult {
  okCount: number
  failCount: number
  mergedCount: number
  results: CopyModuleResult[]
}

// Bir nechta modulni bir nechta o'quv dasturiga birdan nusxalaydi (har modul × har dastur).
export async function copyModulesToCurricula(
  moduleIds: string[],
  targetCurriculumIds: string[],
): Promise<CopyModulesResult> {
  const { data } = await api.post<CopyModulesResult>(
    `/admin/curriculum/modules/copy-many`,
    { moduleIds, targetCurriculumIds },
  )
  return data
}
