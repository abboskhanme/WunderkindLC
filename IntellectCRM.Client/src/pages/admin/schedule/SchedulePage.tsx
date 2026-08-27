import { Fragment, useEffect, useMemo, useState } from 'react'
import { Link } from 'react-router-dom'
import {
  AlertTriangle,
  CalendarDays,
  CheckCircle2,
  Clock,
  DoorOpen,
  Flame,
  Lightbulb,
  Users,
} from 'lucide-react'
import type {
  ScheduleBoard,
  ScheduleGap,
  ScheduleLesson,
  ScheduleOwner,
} from '@/api/services/schedule'
import { getScheduleBoard, getScheduleGaps } from '@/api/services/schedule'
import { Badge } from '@/components/ui/Badge'
import { Card } from '@/components/ui/Card'
import { Loader } from '@/components/ui/Loader'
import { PageHeader } from '@/components/ui/PageHeader'
import { apiErrorMessage, cn } from '@/lib/utils'

const control =
  'rounded-lg border border-slate-200 bg-white px-3 py-2 text-sm font-medium text-slate-700 outline-none transition-colors focus:border-brand-400 focus:ring-2 focus:ring-brand-100'

/** Ustunlar HAR DOIM ettita — dars yo'q kun ham o'z o'rnida turadi (hafta shakli buzilmasin). */
const DAYS = [
  { idx: 0, short: 'Du', full: 'Dushanba' },
  { idx: 1, short: 'Se', full: 'Seshanba' },
  { idx: 2, short: 'Ch', full: 'Chorshanba' },
  { idx: 3, short: 'Pa', full: 'Payshanba' },
  { idx: 4, short: 'Ju', full: 'Juma' },
  { idx: 5, short: 'Sh', full: 'Shanba' },
  { idx: 6, short: 'Ya', full: 'Yakshanba' },
]

/** Shu daqiqadan boshlab oraliq "yo'qotilgan vaqt" hisoblanadi — undan kichigi tanaffus. */
const GAP_MIN_MINUTES = 60

type ScopeMode = 'room' | 'teacher'
type TabKey = 'board' | 'gaps' | 'peak'

/**
 * "HH:mm" → kun boshidan hisoblangan daqiqa.
 * Matnni to'g'ridan-to'g'ri solishtirib bo'lmaydi ("9:00" > "10:00" chiqardi), buzuq qiymat
 * uchun `NaN` qaytadi — u har qanday solishtiruvda `false` beradi, ya'ni oraliq CHIZILMAYDI.
 */
function toMinutes(hhmm: string): number {
  const m = /^(\d{1,2}):(\d{2})$/.exec(hhmm ?? '')
  if (!m) return Number.NaN
  return Number(m[1]) * 60 + Number(m[2])
}

/** Daqiqani odam o'qiydigan ko'rinishga o'giradi: "45 daqiqa", "2 soat", "1 soat 30 daqiqa". */
function formatDuration(minutes: number): string {
  if (!Number.isFinite(minutes) || minutes <= 0) return '0 daqiqa'
  if (minutes < 60) return `${minutes} daqiqa`
  const h = Math.floor(minutes / 60)
  const m = minutes % 60
  return m === 0 ? `${h} soat` : `${h} soat ${m} daqiqa`
}

/**
 * Tig'izlik katagining rangi — quyuqroq bo'lgani sari ko'proq dars ketyapti.
 * Nisbat (max'ga bo'lingan) ishlatiladi, chunki markaz kattaligiga qarab mutlaq sonlar
 * juda har xil bo'ladi: 8 dars kichik markazda "cho'qqi", kattasida "tinch payt".
 */
function peakCellCls(count: number, max: number): string {
  if (count <= 0) return 'bg-slate-50 text-slate-300'
  const ratio = max > 0 ? count / max : 0
  if (ratio >= 0.8) return 'bg-brand-600 text-white'
  if (ratio >= 0.6) return 'bg-brand-500 text-white'
  if (ratio >= 0.4) return 'bg-brand-300 text-brand-900'
  if (ratio >= 0.2) return 'bg-brand-100 text-brand-800'
  return 'bg-brand-50 text-brand-700'
}

export function SchedulePage() {
  const [tab, setTab] = useState<TabKey>('board')

  const [board, setBoard] = useState<ScheduleBoard | null>(null)
  const [boardError, setBoardError] = useState<string | null>(null)

  useEffect(() => {
    let alive = true
    getScheduleBoard()
      .then((d) => alive && setBoard(d))
      .catch((e) => alive && setBoardError(apiErrorMessage(e, 'Jadvalni yuklab bo\'lmadi')))
    return () => {
      alive = false
    }
  }, [])

  const boardLoading = board === null && boardError === null

  return (
    <div>
      <PageHeader
        title="Dars jadvali"
        sub="Haftalik jadval, bo'sh oraliqlar va tavsiyalar"
      />

      {board != null && board.skippedGroups > 0 && (
        <div className="mb-4 flex items-start gap-2 rounded-xl border border-amber-200 bg-amber-50 px-4 py-3 text-sm text-amber-700">
          <AlertTriangle className="mt-0.5 h-4 w-4 shrink-0" />
          <span>
            {board.skippedGroups} ta guruh jadvalda ko'rinmayapti — dars kunlari yoki vaqti
            to'ldirilmagan (yoki buzuq).
          </span>
        </div>
      )}

      <div className="tabs mb-4" role="tablist">
        <button
          type="button"
          role="tab"
          aria-selected={tab === 'board'}
          className={cn('tab', tab === 'board' && 'active')}
          onClick={() => setTab('board')}
        >
          Jadval
        </button>
        <button
          type="button"
          role="tab"
          aria-selected={tab === 'gaps'}
          className={cn('tab', tab === 'gaps' && 'active')}
          onClick={() => setTab('gaps')}
        >
          Bo'sh oraliqlar
        </button>
        <button
          type="button"
          role="tab"
          aria-selected={tab === 'peak'}
          className={cn('tab', tab === 'peak' && 'active')}
          onClick={() => setTab('peak')}
        >
          Tig'izlik
        </button>
      </div>

      {boardError != null && tab !== 'gaps' && (
        <Card className="text-center text-sm text-red-600">{boardError}</Card>
      )}
      {boardLoading && tab !== 'gaps' && <Loader label="Jadval yuklanmoqda..." />}

      {tab === 'board' && board != null && <BoardTab board={board} />}
      {tab === 'gaps' && <GapsTab board={board} />}
      {tab === 'peak' && board != null && <PeakTab board={board} />}
    </div>
  )
}

/* ---------------- Tab 1 — Jadval ---------------- */

function BoardTab({ board }: { board: ScheduleBoard }) {
  const [mode, setMode] = useState<ScopeMode>('room')
  const [ownerId, setOwnerId] = useState('all')

  const owners: ScheduleOwner[] = mode === 'room' ? board.rooms : board.teachers

  /** Rejim almashganda eski egani saqlab qolish mumkin emas — id'lar boshqa ro'yxatdan. */
  const switchMode = (next: ScopeMode) => {
    setMode(next)
    setOwnerId('all')
  }

  const showGaps = ownerId !== 'all'

  /** Kun bo'yicha ajratilgan va vaqt bo'yicha tartiblangan darslar. */
  const byDay = useMemo(() => {
    const map = new Map<number, ScheduleLesson[]>()
    for (const l of board.lessons) {
      if (ownerId !== 'all') {
        const id = mode === 'room' ? l.roomId : l.teacherId
        if (id !== ownerId) continue
      }
      const list = map.get(l.day)
      if (list) list.push(l)
      else map.set(l.day, [l])
    }
    for (const list of map.values()) {
      list.sort((a, b) => toMinutes(a.start) - toMinutes(b.start))
    }
    return map
  }, [board.lessons, mode, ownerId])

  return (
    <div className="space-y-4">
      <div className="flex flex-wrap items-center gap-2">
        <div className="tabs" role="tablist">
          <button
            type="button"
            role="tab"
            aria-selected={mode === 'room'}
            className={cn('tab', mode === 'room' && 'active')}
            onClick={() => switchMode('room')}
          >
            <DoorOpen className="mr-1 inline h-3.5 w-3.5" /> Xonalar bo'yicha
          </button>
          <button
            type="button"
            role="tab"
            aria-selected={mode === 'teacher'}
            className={cn('tab', mode === 'teacher' && 'active')}
            onClick={() => switchMode('teacher')}
          >
            <Users className="mr-1 inline h-3.5 w-3.5" /> O'qituvchilar bo'yicha
          </button>
        </div>

        <select
          value={ownerId}
          onChange={(e) => setOwnerId(e.target.value)}
          className={control}
          aria-label={mode === 'room' ? 'Xona' : "O'qituvchi"}
        >
          <option value="all">Hammasi</option>
          {owners.map((o) => (
            <option key={o.id} value={o.id}>
              {o.name} ({o.groupCount} guruh)
            </option>
          ))}
        </select>
      </div>

      {!showGaps && (
        <p className="text-xs text-slate-400">
          «Hammasi» tanlanganda bo'sh oraliqlar ko'rsatilmaydi — bir ustunda har xil{' '}
          {mode === 'room' ? 'xonalarning' : "o'qituvchilarning"} darslari aralashadi va «teshik»
          tushunchasi ma'nosini yo'qotadi. Aniq {mode === 'room' ? 'xonani' : "o'qituvchini"} tanlang.
        </p>
      )}

      <Card tight className="overflow-x-auto">
        <div className="grid min-w-[980px] grid-cols-7 gap-3 p-4">
          {DAYS.map((d) => {
            const items = byDay.get(d.idx) ?? []
            return (
              <div key={d.idx} className="min-w-0">
                <div className="mb-2 rounded-lg bg-slate-50 px-2 py-1.5 text-center text-xs font-bold text-slate-500">
                  {d.short}
                  <span className="ml-1 font-medium text-slate-300">{items.length}</span>
                </div>

                {items.length === 0 ? (
                  <p className="py-6 text-center text-[11px] text-slate-300">Dars yo'q</p>
                ) : (
                  <div className="space-y-2">
                    {items.map((l, i) => {
                      const prev = i > 0 ? items[i - 1] : null
                      const gap = prev ? toMinutes(l.start) - toMinutes(prev.end) : Number.NaN
                      const showGapLine = showGaps && gap >= GAP_MIN_MINUTES
                      return (
                        <Fragment key={`${l.groupId}-${l.day}-${l.start}`}>
                          {showGapLine && prev != null && (
                            <div className="rounded-lg border border-dashed border-amber-300 bg-amber-50/60 px-2 py-1.5 text-center text-[11px] font-semibold text-amber-600">
                              <Clock className="mr-1 inline h-3 w-3" />
                              Bo'sh: {prev.end} – {l.start} · {formatDuration(gap)}
                            </div>
                          )}
                          <Link
                            to={`/admin/classes/${l.groupId}`}
                            className="block rounded-lg border border-slate-200 bg-white px-2.5 py-2 transition-colors hover:border-brand-300 hover:bg-brand-50/40"
                          >
                            <div className="truncate text-xs font-bold text-slate-800">
                              {l.groupName}
                            </div>
                            <div className="mt-0.5 text-[11px] font-semibold text-brand-600">
                              {l.start}–{l.end}
                            </div>
                            {l.courseName && (
                              <div className="truncate text-[11px] text-slate-500">{l.courseName}</div>
                            )}
                            <div className="truncate text-[11px] text-slate-400">
                              {mode === 'room' ? l.teacherName : l.roomName}
                            </div>
                          </Link>
                        </Fragment>
                      )
                    })}
                  </div>
                )}
              </div>
            )
          })}
        </div>
      </Card>
    </div>
  )
}

/* ---------------- Tab 2 — Bo'sh oraliqlar ---------------- */

interface GapState {
  key: string
  items: ScheduleGap[]
  error: string | null
}

function GapsTab({ board }: { board: ScheduleBoard | null }) {
  const [scope, setScope] = useState<ScopeMode>('room')
  const [minMinutes, setMinMinutes] = useState(60)
  const [ownerId, setOwnerId] = useState('')

  const owners: ScheduleOwner[] = scope === 'room' ? (board?.rooms ?? []) : (board?.teachers ?? [])

  const switchScope = (next: ScopeMode) => {
    setScope(next)
    setOwnerId('')
  }

  const key = `${scope}|${ownerId}|${minMinutes}`
  const [state, setState] = useState<GapState>({ key: '', items: [], error: null })

  useEffect(() => {
    let alive = true
    getScheduleGaps(scope, ownerId || undefined, minMinutes)
      .then((d) => alive && setState({ key, items: d, error: null }))
      .catch(
        (e) =>
          alive &&
          setState({ key, items: [], error: apiErrorMessage(e, 'Bo\'sh oraliqlarni yuklab bo\'lmadi') }),
      )
    return () => {
      alive = false
    }
  }, [key, scope, ownerId, minMinutes])

  /**
   * Yuklanish holati ALOHIDA state emas, KALITLAR farqidan chiqariladi — aks holda uni
   * effekt ichida `setLoading(true)` bilan yoqish kerak bo'lardi (eslint
   * `react-hooks/set-state-in-effect` taqiqlaydi).
   */
  const loading = state.key !== key

  return (
    <div className="space-y-4">
      <div className="flex flex-wrap items-center gap-2">
        <div className="tabs" role="tablist">
          <button
            type="button"
            role="tab"
            aria-selected={scope === 'room'}
            className={cn('tab', scope === 'room' && 'active')}
            onClick={() => switchScope('room')}
          >
            <DoorOpen className="mr-1 inline h-3.5 w-3.5" /> Xonalar
          </button>
          <button
            type="button"
            role="tab"
            aria-selected={scope === 'teacher'}
            className={cn('tab', scope === 'teacher' && 'active')}
            onClick={() => switchScope('teacher')}
          >
            <Users className="mr-1 inline h-3.5 w-3.5" /> O'qituvchilar
          </button>
        </div>

        <select
          value={minMinutes}
          onChange={(e) => setMinMinutes(Number(e.target.value))}
          className={control}
          aria-label="Eng kam davomiylik"
        >
          <option value={60}>60 daqiqadan uzun</option>
          <option value={90}>90 daqiqadan uzun</option>
          <option value={120}>120 daqiqadan uzun</option>
        </select>

        <select
          value={ownerId}
          onChange={(e) => setOwnerId(e.target.value)}
          className={control}
          aria-label={scope === 'room' ? 'Xona' : "O'qituvchi"}
        >
          <option value="">{scope === 'room' ? 'Barcha xonalar' : "Barcha o'qituvchilar"}</option>
          {owners.map((o) => (
            <option key={o.id} value={o.id}>
              {o.name} ({o.groupCount} guruh)
            </option>
          ))}
        </select>
      </div>

      {loading && <Loader label="Hisoblanmoqda..." />}

      {!loading && state.error != null && (
        <Card className="text-center text-sm text-red-600">{state.error}</Card>
      )}

      {!loading && state.error == null && state.items.length === 0 && (
        <Card className="flex items-center justify-center gap-2 py-10 text-sm font-semibold text-emerald-600">
          <CheckCircle2 className="h-5 w-5" />
          Bo'sh oraliq topilmadi — jadval zich.
        </Card>
      )}

      {!loading &&
        state.error == null &&
        state.items.map((g) => (
          <Card key={`${g.scope}-${g.ownerId}-${g.day}-${g.start}`}>
            <div className="flex flex-wrap items-center justify-between gap-2">
              <h3 className="text-sm font-bold text-slate-800">
                {g.ownerName} · {g.dayLabel} · {g.start}–{g.end}
              </h3>
              <div className="flex items-center gap-2">
                <Badge tone="amber">{formatDuration(g.minutes)}</Badge>
                {g.peakScore > 0 ? (
                  <Badge tone="violet">
                    <Flame className="h-3 w-3" /> Tig'iz vaqt · {g.peakScore} dars ketyapti
                  </Badge>
                ) : (
                  <Badge tone="default">Bo'sh payt</Badge>
                )}
              </div>
            </div>

            <div className="mt-2 flex flex-wrap items-center gap-2 text-xs text-slate-500">
              <span className="font-medium text-slate-600">{g.beforeGroup || '—'}</span>
              <span className="text-slate-300">→</span>
              <span className="rounded-md border border-dashed border-amber-300 bg-amber-50 px-2 py-0.5 font-semibold text-amber-600">
                BO'SH
              </span>
              <span className="text-slate-300">→</span>
              <span className="font-medium text-slate-600">{g.afterGroup || '—'}</span>
            </div>

            <div className="mt-3 border-t border-slate-100 pt-3">
              <div className="mb-2 flex items-center gap-1.5 text-xs font-bold text-slate-500">
                <Lightbulb className="h-3.5 w-3.5 text-brand-600" /> Tavsiyalar
              </div>
              {g.suggestions.length === 0 ? (
                <p className="text-xs text-slate-400">
                  Bu paytda bo'sh {g.scope === 'room' ? "o'qituvchi" : 'xona'} topilmadi.
                </p>
              ) : (
                <ul className="space-y-1.5">
                  {g.suggestions.map((s) => (
                    <li
                      key={`${s.kind}-${s.id}`}
                      className="flex items-start justify-between gap-3 rounded-lg bg-slate-50 px-3 py-2"
                    >
                      <div className="min-w-0">
                        <div className="truncate text-sm font-semibold text-slate-800">{s.name}</div>
                        {s.note && <div className="text-[11px] text-slate-400">{s.note}</div>}
                      </div>
                      <span className="shrink-0 text-xs font-semibold text-slate-500">
                        {Math.round(s.weeklyMinutes / 60)} soat
                      </span>
                    </li>
                  ))}
                </ul>
              )}
            </div>
          </Card>
        ))}
    </div>
  )
}

/* ---------------- Tab 3 — Tig'izlik ---------------- */

function PeakTab({ board }: { board: ScheduleBoard }) {
  /** Faqat ma'lumot BOR yarim soatlar qatorga chiqadi — bo'sh tun soatlari jadvalni cho'zardi. */
  const buckets = useMemo(() => {
    const set = new Set<string>()
    for (const p of board.peak) set.add(p.bucket)
    return [...set].sort((a, b) => toMinutes(a) - toMinutes(b))
  }, [board.peak])

  const counts = useMemo(() => {
    const map = new Map<string, number>()
    for (const p of board.peak) map.set(`${p.bucket}|${p.day}`, p.count)
    return map
  }, [board.peak])

  const max = useMemo(
    () => board.peak.reduce((acc, p) => (p.count > acc ? p.count : acc), 0),
    [board.peak],
  )

  const top5 = useMemo(
    () => [...board.peak].filter((p) => p.count > 0).sort((a, b) => b.count - a.count).slice(0, 5),
    [board.peak],
  )

  if (buckets.length === 0) {
    return (
      <Card className="py-10 text-center text-sm text-slate-400">
        Tig'izlikni hisoblash uchun jadvalda dars yo'q.
      </Card>
    )
  }

  return (
    <div className="space-y-4">
      <p className="text-sm text-slate-500">
        Eng tig'iz vaqtlar — o'quvchi aynan o'shanda kela oladi. Yangi guruh jadvalini avval shu
        kataklarga qo'ying.
      </p>

      <Card tight className="overflow-x-auto">
        <table className="min-w-[720px] w-full border-separate border-spacing-1 p-4">
          <thead>
            <tr>
              <th className="w-16 text-left text-[11px] font-bold text-slate-400">
                <CalendarDays className="inline h-3.5 w-3.5" />
              </th>
              {DAYS.map((d) => (
                <th key={d.idx} className="text-center text-[11px] font-bold text-slate-500">
                  {d.short}
                </th>
              ))}
            </tr>
          </thead>
          <tbody>
            {buckets.map((b) => (
              <tr key={b}>
                <td className="whitespace-nowrap pr-2 text-[11px] font-semibold text-slate-400">
                  {b}
                </td>
                {DAYS.map((d) => {
                  const c = counts.get(`${b}|${d.idx}`) ?? 0
                  return (
                    <td key={d.idx} className="p-0">
                      <div
                        title={`${d.full} ${b} — ${c} dars`}
                        className={cn(
                          'rounded-md py-1.5 text-center text-xs font-bold',
                          peakCellCls(c, max),
                        )}
                      >
                        {c}
                      </div>
                    </td>
                  )
                })}
              </tr>
            ))}
          </tbody>
        </table>
      </Card>

      <Card title="Eng tig'iz 5 katak" sub="Yangi guruhni shu vaqtlarga qo'yish eng foydali">
        {top5.length === 0 ? (
          <p className="text-sm text-slate-400">Ma'lumot yo'q.</p>
        ) : (
          <ul className="space-y-1.5">
            {top5.map((p) => (
              <li
                key={`${p.day}-${p.bucket}`}
                className="flex items-center justify-between gap-3 rounded-lg bg-slate-50 px-3 py-2"
              >
                <span className="text-sm font-semibold text-slate-700">
                  {DAYS[p.day]?.full ?? `${p.day}-kun`} · {p.bucket}
                </span>
                <Badge tone="violet">
                  <Flame className="h-3 w-3" /> {p.count} dars
                </Badge>
              </li>
            ))}
          </ul>
        )}
      </Card>
    </div>
  )
}
