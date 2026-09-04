import { useCallback, useEffect, useState } from 'react'
import { useSearchParams } from 'react-router-dom'
import { apiErrorMessage } from '@/lib/utils'
import {
  getAssignees, getBoards,
  type WorkTaskAssignee, type WorkTaskBoard, type WorkTaskQuery,
} from '@/api/services/workTasks'

/* =====================================================================================
 *  TOPSHIRIQLAR bo'limining SOF MANTIQI (JSX yo'q)
 * =====================================================================================
 *  Doska, ro'yxat va kalendar — AYNI ma'lumotning uch ko'rinishi: muhimlik shkalasi, sana
 *  yordamchilari, filtrlar va yuklash hooklari uchtasida ham bir xil bo'lishi kerak.
 *
 *  ⚠️ Komponentlar ATAYIN alohida faylda (`shared.tsx`): bitta fayl ham komponent, ham
 *  konstanta eksport qilsa Vite'ning "fast refresh" qoidasi buziladi (eslint xatosi).
 */

// ---------- Muhimlik va takroriylik ----------

export interface PriorityMeta {
  value: number
  label: string
  /** Kartochkadagi rangli nuqta */
  dot: string
  /** Yorliq (chip) ranglari */
  chip: string
  /** Kartochkaning chap chizig'i (hex — inline style bilan beriladi) */
  accent: string
}

/** Muhimlik darajalari — YAGONA manba (server 0..3 saqlaydi). */
export const PRIORITIES: PriorityMeta[] = [
  { value: 0, label: 'Past', dot: 'bg-slate-400', chip: 'bg-slate-100 text-slate-600', accent: '#94a3b8' },
  { value: 1, label: "O'rta", dot: 'bg-sky-500', chip: 'bg-sky-50 text-sky-700', accent: '#0284c7' },
  { value: 2, label: 'Yuqori', dot: 'bg-amber-500', chip: 'bg-amber-50 text-amber-700', accent: '#f59e0b' },
  { value: 3, label: 'Shoshilinch', dot: 'bg-rose-500', chip: 'bg-rose-50 text-rose-700', accent: '#e11d48' },
]

export function priorityOf(p: number): PriorityMeta {
  return PRIORITIES[Math.min(Math.max(p, 0), PRIORITIES.length - 1)] ?? PRIORITIES[1]
}

/** Takroriylik variantlari — server kalitlari bilan bir xil. */
export const REPEATS: { value: string; label: string }[] = [
  { value: 'none', label: 'Takrorlanmaydi' },
  { value: 'daily', label: 'Har kuni' },
  { value: 'weekly', label: 'Har hafta' },
  { value: 'monthly', label: 'Har oy' },
]

export function repeatLabel(value: string): string {
  return REPEATS.find((r) => r.value === value)?.label ?? 'Takrorlanmaydi'
}

// ---------- Sana yordamchilari ----------

/**
 * BUGUN — "yyyy-MM-dd", MAHALLIY vaqt bo'yicha.
 *
 * ⚠️ `toISOString()` ATAYIN ishlatilmaydi: u UTC beradi va Toshkent (UTC+5) da soat 05:00 gacha
 * KECHAGI kunni qaytarardi — ya'ni ertalab "bugungi topshiriqlar" bo'sh ko'rinardi.
 */
export function todayIso(): string {
  return dayIso(new Date())
}

/** `Date` → "yyyy-MM-dd" (mahalliy vaqt). */
export function dayIso(d: Date): string {
  const m = String(d.getMonth() + 1).padStart(2, '0')
  const day = String(d.getDate()).padStart(2, '0')
  return `${d.getFullYear()}-${m}-${day}`
}

/** "yyyy-MM-dd" → "dd.MM" (ixcham ko'rinish). */
export function shortDay(iso: string): string {
  return iso.length >= 10 ? `${iso.slice(8, 10)}.${iso.slice(5, 7)}` : iso
}

/** Muddat yorlig'i: "Bugun" · "Ertaga" · "3 kun kechikdi" · "12.09". */
export function dueLabel(due: string | null, today: string): string {
  if (!due) return ''
  if (due === today) return 'Bugun'
  const diff = Math.round(
    (Date.parse(`${due}T00:00:00`) - Date.parse(`${today}T00:00:00`)) / 86_400_000,
  )
  if (diff === 1) return 'Ertaga'
  if (diff === -1) return 'Kecha'
  if (diff < 0) return `${-diff} kun kechikdi`
  if (diff <= 7) return `${diff} kundan keyin`
  return shortDay(due)
}

// ---------- Doskalar va mas'ullar ----------

export interface TasksMeta {
  boards: WorkTaskBoard[]
  assignees: WorkTaskAssignee[]
  loading: boolean
  error: string | null
  /** Qayta yuklashni so'raydi (effekt ichida yangi so'rov ketadi). */
  reload: () => void
}

/**
 * Doskalar va mas'ullar ro'yxati — uchala ko'rinishga ham kerak. Axios klientidagi GET-kesh
 * (25 s) tufayli tablar orasida o'tganda qayta so'rov ketmaydi.
 *
 * ⚠️ Qayta yuklash "tick" orqali: effekt ichida to'g'ridan-to'g'ri setState chaqiruvchi
 * funksiya chaqirilsa React qoidasi buziladi (kaskad render). Shu sababdan `reload()` faqat
 * hisoblagichni oshiradi, so'rovning o'zi esa effektda bajariladi.
 */
export function useTasksMeta(): TasksMeta {
  const [boards, setBoards] = useState<WorkTaskBoard[]>([])
  const [assignees, setAssignees] = useState<WorkTaskAssignee[]>([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const [tick, setTick] = useState(0)

  useEffect(() => {
    let alive = true
    Promise.all([getBoards(), getAssignees()])
      .then(([b, a]) => {
        if (!alive) return
        setBoards(b)
        setAssignees(a)
        setError(null)
      })
      .catch((err) => {
        if (alive) setError(apiErrorMessage(err, "Ma'lumotlarni yuklab bo'lmadi"))
      })
      .finally(() => {
        if (alive) setLoading(false)
      })
    return () => {
      alive = false
    }
  }, [tick])

  const reload = useCallback(() => setTick((t) => t + 1), [])
  return { boards, assignees, loading, error, reload }
}

/**
 * Tanlangan doska — MANZILDA (`?board=...`) saqlanadi: ko'rinishlar orasida o'tganda va
 * sahifa yangilanganda tanlov yo'qolmaydi, havolani ulashsa ham o'sha doska ochiladi.
 */
export function useBoardParam(boards: WorkTaskBoard[]): [string, (id: string) => void] {
  const [params, setParams] = useSearchParams()
  const raw = params.get('board') ?? ''
  const valid = boards.some((b) => b.id === raw)
  const current = valid ? raw : (boards[0]?.id ?? '')

  const set = (id: string) => {
    const next = new URLSearchParams(params)
    if (id) next.set('board', id)
    else next.delete('board')
    setParams(next, { replace: true })
  }
  return [current, set]
}

// ---------- Filtrlar ----------

export interface TaskFilters {
  assigneeId: string
  q: string
  priority: string
  /** '' | 'open' | 'done' | 'overdue' | 'today' */
  status: string
}

export const EMPTY_FILTERS: TaskFilters = { assigneeId: '', q: '', priority: '', status: '' }

/** Filtrlarni server so'rovi parametrlariga o'giradi (bo'sh qiymatlar yuborilmaydi). */
export function toQuery(boardId: string, f: TaskFilters, extra: WorkTaskQuery = {}): WorkTaskQuery {
  const q: WorkTaskQuery = { ...extra }
  if (boardId) q.boardId = boardId
  if (f.assigneeId) q.assigneeId = f.assigneeId
  if (f.q.trim()) q.q = f.q.trim()
  if (f.priority !== '') q.priority = Number(f.priority)
  if (f.status) q.status = f.status
  return q
}
