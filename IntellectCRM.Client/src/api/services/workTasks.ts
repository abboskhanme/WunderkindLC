import { api } from '../client'
import type { StageColor } from '@/types'

/* =====================================================================================
 *  TOPSHIRIQLAR (Kanban) — "Topshiriqlar" bo'limi
 * =====================================================================================
 *  Doska/ustun/muddat/mas'ul bilan ishlaydigan topshiriqlar. Eski "Adminga topshiriq"
 *  (kunlik cheklist) moduli olib tashlangan — takroriylik shu modulning O'ZIDA (`repeat`).
 */

export interface WorkTaskColumn {
  id: string
  boardId: string
  title: string
  color: StageColor
  order: number
  /** Bu ustundagi topshiriq BAJARILGAN hisoblanadi. */
  isDone: boolean
  /** Tizim ustuni — o'chirib bo'lmaydi. */
  isSystem: boolean
  taskCount: number
}

export interface WorkTaskBoard {
  id: string
  title: string
  color: StageColor
  order: number
  isArchived: boolean
  columns: WorkTaskColumn[]
  taskCount: number
}

export interface WorkTaskAssignee {
  userId: string
  fullName: string
  role: string
  position: string
  avatarUrl: string | null
  hasTelegram: boolean
  openCount: number
  overdueCount: number
}

export interface WorkTask {
  id: string
  boardId: string
  columnId: string
  title: string
  description: string
  assigneeId: string
  assigneeName: string
  assigneeAvatarUrl: string | null
  createdById: string
  createdByName: string
  /** 0 past · 1 o'rta · 2 yuqori · 3 shoshilinch */
  priority: number
  dueDate: string | null
  dueTime: string | null
  order: number
  tags: string[]
  isDone: boolean
  isOverdue: boolean
  stepsTotal: number
  stepsDone: number
  commentCount: number
  createdAt: string
  updatedAt: string
  completedAt: string | null
  repeat: string
  isArchived: boolean
}

export interface WorkTaskStep {
  id: string
  title: string
  done: boolean
  doneAt: string | null
  order: number
}

export interface WorkTaskComment {
  id: string
  authorId: string
  authorName: string
  text: string
  createdAt: string
}

export interface WorkTaskEvent {
  id: string
  actorId: string
  actorName: string
  kind: string
  text: string
  createdAt: string
}

export interface WorkTaskDetail {
  task: WorkTask
  steps: WorkTaskStep[]
  comments: WorkTaskComment[]
  events: WorkTaskEvent[]
}

export interface WorkTaskInput {
  boardId: string
  columnId?: string | null
  title: string
  description?: string
  assigneeId?: string
  priority: number
  dueDate?: string | null
  dueTime?: string | null
  tags?: string[]
  repeat?: string
}

export interface WorkTaskStatRow {
  userId: string
  fullName: string
  position: string
  total: number
  done: number
  open: number
  overdue: number
  dueToday: number
  doneOnTime: number
  doneLate: number
  rate: number
}

export interface WorkTaskTrendPoint {
  date: string
  created: number
  done: number
}

export interface WorkTaskDashboard {
  total: number
  done: number
  open: number
  overdue: number
  dueToday: number
  rate: number
  rows: WorkTaskStatRow[]
  trend: WorkTaskTrendPoint[]
  attention: WorkTask[]
}

export interface WorkTaskSettings {
  enabled: boolean
  hour: number
  minute: number
}

/** Ro'yxat filtrlari — doska, ro'yxat va kalendar AYNAN shu endpointdan oziqlanadi. */
export interface WorkTaskQuery {
  boardId?: string
  assigneeId?: string
  q?: string
  /** open | done | overdue | today */
  status?: string
  priority?: number
  from?: string
  to?: string
  archived?: boolean
}

// ---------- Doskalar va ustunlar ----------

export async function getBoards(includeArchived = false): Promise<WorkTaskBoard[]> {
  const { data } = await api.get<WorkTaskBoard[]>('/admin/work-tasks/boards', {
    params: { includeArchived },
  })
  return data
}

export async function createBoard(title: string, color: StageColor): Promise<WorkTaskBoard> {
  const { data } = await api.post<WorkTaskBoard>('/admin/work-tasks/boards', { title, color })
  return data
}

export async function updateBoard(id: string, title: string, color: StageColor): Promise<void> {
  await api.put(`/admin/work-tasks/boards/${id}`, { title, color })
}

/** Standart holatda ARXIVLAYDI (topshiriqlar saqlanadi); `hard` — butunlay o'chiradi. */
export async function deleteBoard(id: string, hard = false): Promise<void> {
  await api.delete(`/admin/work-tasks/boards/${id}`, { params: { hard } })
}

export async function createColumn(
  boardId: string,
  title: string,
  color: StageColor,
  isDone: boolean,
): Promise<WorkTaskColumn> {
  const { data } = await api.post<WorkTaskColumn>('/admin/work-tasks/columns', {
    boardId, title, color, isDone,
  })
  return data
}

export async function updateColumn(
  id: string,
  boardId: string,
  title: string,
  color: StageColor,
  isDone: boolean,
): Promise<void> {
  await api.put(`/admin/work-tasks/columns/${id}`, { boardId, title, color, isDone })
}

export async function moveColumn(id: string, dir: -1 | 1): Promise<void> {
  await api.put(`/admin/work-tasks/columns/${id}/move`, null, { params: { dir } })
}

export async function deleteColumn(id: string): Promise<void> {
  await api.delete(`/admin/work-tasks/columns/${id}`)
}

// ---------- Topshiriqlar ----------

export async function getTasks(query: WorkTaskQuery = {}): Promise<WorkTask[]> {
  const { data } = await api.get<WorkTask[]>('/admin/work-tasks', { params: query })
  return data
}

export async function getTask(id: string): Promise<WorkTaskDetail> {
  const { data } = await api.get<WorkTaskDetail>(`/admin/work-tasks/${id}`)
  return data
}

export async function createTask(input: WorkTaskInput): Promise<WorkTask> {
  const { data } = await api.post<WorkTask>('/admin/work-tasks', input)
  return data
}

export async function updateTask(id: string, input: WorkTaskInput): Promise<WorkTask> {
  const { data } = await api.put<WorkTask>(`/admin/work-tasks/${id}`, input)
  return data
}

/** Sudrab ko'chirish — maqsad ustun va ustun ichidagi o'rin. */
export async function moveTask(id: string, columnId: string, order: number): Promise<WorkTask> {
  const { data } = await api.put<WorkTask>(`/admin/work-tasks/${id}/move`, { columnId, order })
  return data
}

export async function archiveTask(id: string, value = true): Promise<void> {
  await api.put(`/admin/work-tasks/${id}/archive`, null, { params: { value } })
}

export async function deleteTask(id: string): Promise<void> {
  await api.delete(`/admin/work-tasks/${id}`)
}

// ---------- Qadamlar va izohlar ----------

export async function addStep(taskId: string, title: string): Promise<WorkTaskStep> {
  const { data } = await api.post<WorkTaskStep>(`/admin/work-tasks/${taskId}/steps`, { title })
  return data
}

export async function updateStep(
  stepId: string,
  patch: { title?: string; done?: boolean },
): Promise<void> {
  await api.put(`/admin/work-tasks/steps/${stepId}`, patch)
}

export async function deleteStep(stepId: string): Promise<void> {
  await api.delete(`/admin/work-tasks/steps/${stepId}`)
}

export async function addComment(taskId: string, text: string): Promise<WorkTaskComment> {
  const { data } = await api.post<WorkTaskComment>(`/admin/work-tasks/${taskId}/comments`, { text })
  return data
}

export async function deleteComment(commentId: string): Promise<void> {
  await api.delete(`/admin/work-tasks/comments/${commentId}`)
}

// ---------- Nazorat va sozlamalar ----------

export async function getAssignees(): Promise<WorkTaskAssignee[]> {
  const { data } = await api.get<WorkTaskAssignee[]>('/admin/work-tasks/assignees')
  return data
}

export async function getDashboard(
  params: { boardId?: string; from?: string; to?: string } = {},
): Promise<WorkTaskDashboard> {
  const { data } = await api.get<WorkTaskDashboard>('/admin/work-tasks/dashboard', { params })
  return data
}

export async function getWorkTaskSettings(): Promise<WorkTaskSettings> {
  const { data } = await api.get<WorkTaskSettings>('/admin/work-tasks/settings')
  return data
}

export async function setWorkTaskSettings(s: WorkTaskSettings): Promise<void> {
  await api.put('/admin/work-tasks/settings', s)
}
