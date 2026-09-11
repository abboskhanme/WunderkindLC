import { api } from '../client'

/** Call Center — telefoniya qo'ng'iroqlari (MoiZvonki): originate, jurnal, yozuvni tinglash. */

export type CallStatus =
  | 'originating'
  | 'ringing'
  | 'answered'
  | 'completed'
  | 'no_answer'
  | 'busy'
  | 'failed'

export interface CallRow {
  id: string
  studentId: string | null
  studentName: string
  phoneNumber: string
  direction: 'outbound' | 'inbound'
  status: CallStatus
  startedAt: string
  answeredAt: string | null
  endedAt: string | null
  durationSeconds: number
  operatorName: string
  hasRecording: boolean
  note: string
}

export interface CallList {
  total: number
  items: CallRow[]
}

/** To'liq qo'ng'iroq detali — transkript va AI tahlil bilan ("Yozuvlar tarixi" o'ng paneli). */
export interface CallDetail extends CallRow {
  transcript: string
  aiAnalysis: string
}

/** Raqam bo'yicha guruhlangan qator — tarixda bitta raqam = bitta qator. */
export interface CallGroup {
  phoneNumber: string
  studentId: string | null
  studentName: string
  callsCount: number
  answeredCount: number
  totalDurationSeconds: number
  lastCallAt: string
  lastStatus: CallStatus | ''
  lastDirection: 'inbound' | 'outbound' | ''
}

export interface CallGroupList {
  total: number
  items: CallGroup[]
}

/** Tarix filtrlari: sana oralig'i (yyyy-MM-dd), yo'nalish, holat. */
export interface CallFilters {
  search?: string
  dateFrom?: string
  dateTo?: string
  direction?: '' | 'inbound' | 'outbound'
  status?: '' | 'answered' | 'missed'
}

/** SignalR "callUpdated" hodisasi payload'i (LiveHub, topic "calls"). */
export interface CallUpdate {
  id: string
  status: CallStatus
  phoneNumber: string
  studentId: string | null
  answeredAt: string | null
  endedAt: string | null
  durationSeconds: number
  hasRecording: boolean
}

/** Modul sozlanganmi (banner uchun) + faol provayder ("moizvonki" | ""). */
export async function getCallsConfig(): Promise<{
  configured: boolean
  provider: string
}> {
  const { data } = await api.get('/admin/calls/config')
  return data
}

/** Chiquvchi qo'ng'iroq: studentId YOKI phoneNumber (dialpad'dan). */
export async function originateCall(req: {
  studentId?: string
  phoneNumber?: string
}): Promise<{ callId: string; status: CallStatus; phoneNumber: string; studentId: string | null }> {
  const { data } = await api.post('/admin/calls/originate', req)
  return data
}

/** Qo'ng'iroqlar ro'yxati (eng oxirgisi tepada). phone berilsa — faqat shu raqam bilan suhbatlar. */
export async function getCalls(f: CallFilters & { phone?: string } = {}, page = 1, pageSize = 50): Promise<CallList> {
  const { data } = await api.get<CallList>('/admin/calls', {
    params: {
      search: f.search || undefined,
      phone: f.phone || undefined,
      dateFrom: f.dateFrom || undefined,
      dateTo: f.dateTo || undefined,
      direction: f.direction || undefined,
      status: f.status || undefined,
      page, pageSize,
    },
  })
  return data
}

/** Raqam bo'yicha guruhlangan tarix (har raqam bitta qator). */
export async function getCallGroups(f: CallFilters = {}, page = 1, pageSize = 50): Promise<CallGroupList> {
  const { data } = await api.get<CallGroupList>('/admin/calls/by-number', {
    params: {
      search: f.search || undefined,
      dateFrom: f.dateFrom || undefined,
      dateTo: f.dateTo || undefined,
      direction: f.direction || undefined,
      status: f.status || undefined,
      page, pageSize,
    },
  })
  return data
}

/** Bitta o'quvchining qo'ng'iroqlar tarixi (detalli oynadagi tab). */
export async function getStudentCalls(studentId: string): Promise<CallRow[]> {
  const { data } = await api.get<CallRow[]>(`/admin/calls/student/${studentId}`)
  return data
}

/** Tarixni provayderdan QO'LDA sinxronlash (MoiZvonki calls.list) — eski qo'ng'iroqlar ham tortiladi. */
export async function syncCallHistory(): Promise<{ added: number; updated: number }> {
  const { data } = await api.post('/admin/calls/telephony/sync', {})
  return data
}

/** Yozuvni autentifikatsiya bilan yuklab, pleyer uchun blob URL qaytaradi (bo'shatish chaqiruvchida). */
export async function fetchRecordingUrl(callId: string): Promise<string> {
  const res = await api.get(`/admin/calls/${callId}/recording`, { responseType: 'blob' })
  return URL.createObjectURL(res.data as Blob)
}

/** Bitta qo'ng'iroqning to'liq detali (transkript, AI tahlil) — "Yozuvlar tarixi" o'ng paneli uchun. */
export async function getCallDetail(id: string): Promise<CallDetail> {
  const { data } = await api.get<CallDetail>(`/admin/calls/${id}/detail`)
  return data
}

/** Yozuvni transkriptga o'girish (AI, 1-2 daqiqa cho'zilishi mumkin). */
export async function transcribeCall(id: string): Promise<{ transcript: string }> {
  const { data } = await api.post(`/admin/calls/${id}/transcribe`, {})
  return data
}

/** Transkript asosida AI tahlil (natija, e'tiroz, keyingi qadam va h.k.). */
export async function analyzeCall(id: string): Promise<{ analysis: string }> {
  const { data } = await api.post(`/admin/calls/${id}/analyze`, {})
  return data
}
