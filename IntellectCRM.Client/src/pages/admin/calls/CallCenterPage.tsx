import { useEffect, useMemo, useRef, useState } from 'react'
import {
  Phone, PhoneCall, PhoneOff, Delete, Search, History, AlertTriangle, Play, Loader2, X, User,
  RefreshCw, PhoneIncoming, PhoneOutgoing, Captions, Sparkles,
} from 'lucide-react'
import type { HubConnection } from '@microsoft/signalr'
import type { Student } from '@/types'
import { getStudents } from '@/api/services/students'
import {
  getCallsConfig, originateCall, getCalls, getStudentCalls, fetchRecordingUrl,
  syncCallHistory, getCallDetail, transcribeCall, analyzeCall,
  type CallRow, type CallStatus, type CallUpdate, type CallFilters, type CallDetail,
} from '@/api/services/calls'
import { connectLiveTopic } from '@/api/services/live'
import { Card } from '@/components/ui/Card'
import { Button } from '@/components/ui/Button'
import { PageHeader } from '@/components/ui/PageHeader'
import { Loader } from '@/components/ui/Loader'
import { cn } from '@/lib/utils'
import { groupsText, statesToGroups } from '@/lib/studentGroups'
import { usePerm } from '@/lib/permissions'

/* ============================================================
   Call Center — chapda o'quvchilar ro'yxati ("Qo'ng'iroq" tugmasi bilan),
   o'ngda dialpad (qo'lda raqam terish) + jonli qo'ng'iroq holati (SignalR).
   Ikkinchi tab — "Yozuvlar tarixi": barcha qo'ng'iroqlar + audio pleyer.
   ============================================================ */

const control =
  'w-full rounded-lg border border-slate-200 bg-white px-3 py-2 text-sm text-slate-800 outline-none transition-colors focus:border-brand-400'

const STATUS_LABEL: Record<CallStatus, string> = {
  originating: 'Ulanmoqda...',
  ringing: 'Chalinyapti...',
  answered: 'Gaplashilyapti',
  completed: 'Yakunlandi',
  no_answer: 'Javob berilmadi',
  busy: 'Band',
  failed: 'Xato',
}

const STATUS_TONE: Record<CallStatus, string> = {
  originating: 'bg-amber-50 text-amber-700 border-amber-200',
  ringing: 'bg-amber-50 text-amber-700 border-amber-200',
  answered: 'bg-emerald-50 text-emerald-700 border-emerald-200',
  completed: 'bg-slate-100 text-slate-600 border-slate-200',
  no_answer: 'bg-red-50 text-red-700 border-red-200',
  busy: 'bg-red-50 text-red-700 border-red-200',
  failed: 'bg-red-50 text-red-700 border-red-200',
}

function fmtDuration(sec: number): string {
  const m = Math.floor(sec / 60)
  const s = sec % 60
  return `${String(m).padStart(2, '0')}:${String(s).padStart(2, '0')}`
}

function fmtDateTime(iso: string): string {
  if (!iso || iso.length < 16) return iso
  return `${iso.slice(8, 10)}.${iso.slice(5, 7)}.${iso.slice(0, 4)} ${iso.slice(11, 16)}`
}

/** Faol (tugamagan) qo'ng'iroq holati — dialpad ostidagi jonli karta. */
interface ActiveCall {
  id: string
  phoneNumber: string
  studentName: string
  status: CallStatus
  startedAtMs: number
  answeredAtMs: number | null
  durationSeconds: number
}

const TERMINAL: CallStatus[] = ['completed', 'no_answer', 'busy', 'failed']

export function CallCenterPage() {
  const { can } = usePerm()
  const canCall = can('calls.cloud', 'create')
  const [tab, setTab] = useState<'dial' | 'history'>('dial')
  const [configured, setConfigured] = useState<boolean | null>(null)
  const [provider, setProvider] = useState('')

  const [students, setStudents] = useState<Student[]>([])
  const [loading, setLoading] = useState(true)
  const [search, setSearch] = useState('')
  const [selected, setSelected] = useState<Student | null>(null)

  const [dial, setDial] = useState('')
  const [calling, setCalling] = useState(false)
  const [error, setError] = useState('')
  const [active, setActive] = useState<ActiveCall | null>(null)
  const [, setTick] = useState(0) // taymer re-render

  useEffect(() => {
    Promise.all([
      getCallsConfig().then((c) => {
        setConfigured(c.configured)
        setProvider(c.provider ?? '')
      }).catch(() => setConfigured(false)),
      getStudents().then(setStudents).catch(() => setStudents([])),
    ]).finally(() => setLoading(false))
  }, [])

  // Jonli holat — LiveHub "calls" mavzusi (callUpdated hodisasi).
  const activeRef = useRef<ActiveCall | null>(null)
  activeRef.current = active
  useEffect(() => {
    let conn: HubConnection | null = null
    let alive = true
    connectLiveTopic('calls', {
      callUpdated: (...args: unknown[]) => {
        const u = args[0] as CallUpdate
        if (!alive) return
        const cur = activeRef.current
        if (cur && u.id === cur.id) {
          setActive({
            ...cur,
            status: u.status,
            answeredAtMs: u.answeredAt ? Date.now() - u.durationSeconds * 1000 : cur.answeredAtMs,
            durationSeconds: u.durationSeconds,
          })
        }
      },
    })
      .then((c) => { if (alive) conn = c; else c.stop() })
      .catch(() => { /* hub bo'lmasa ham sahifa ishlayveradi (holat faqat jonli yangilanmaydi) */ })
    return () => {
      alive = false
      conn?.stop()
    }
  }, [])

  // Taymer (chalinish/gaplashuv vaqti) — sekundiga bir marta.
  useEffect(() => {
    if (!active || TERMINAL.includes(active.status)) return
    const t = setInterval(() => setTick((x) => x + 1), 1000)
    return () => clearInterval(t)
  }, [active])

  const filtered = useMemo(() => {
    const q = search.trim().toLowerCase()
    const digits = search.replace(/\D/g, '')
    let list = students
    if (q) {
      list = students.filter((s) => {
        if (s.fullName.toLowerCase().includes(q)) return true
        if (digits.length >= 3) {
          return [s.phone, s.parentPhone, s.fatherPhone, s.motherPhone]
            .some((p) => (p ?? '').replace(/\D/g, '').includes(digits))
        }
        return false
      })
    }
    return list.slice(0, 100)
  }, [students, search])

  const startCall = async (opts: { studentId?: string; phoneNumber?: string; name?: string }) => {
    if (calling) return
    setCalling(true)
    setError('')
    try {
      const r = await originateCall({ studentId: opts.studentId, phoneNumber: opts.phoneNumber })
      setActive({
        id: r.callId,
        phoneNumber: r.phoneNumber,
        studentName: opts.name ?? '',
        status: r.status,
        startedAtMs: Date.now(),
        answeredAtMs: null,
        durationSeconds: 0,
      })
    } catch (err: any) {
      setError(err.response?.data?.message || err.message || 'Xato yuz berdi')
    } finally {
      setCalling(false)
    }
  }

  const elapsed = active
    ? active.status === 'answered'
      ? Math.floor((Date.now() - (active.answeredAtMs ?? Date.now())) / 1000)
      : TERMINAL.includes(active.status)
        ? active.durationSeconds
        : Math.floor((Date.now() - active.startedAtMs) / 1000)
    : 0

  if (loading) {
    return (
      <Card>
        <Loader label="Yuklanmoqda..." />
      </Card>
    )
  }

  return (
    <div>
      <PageHeader
        title="Call Center"
        sub="O'quvchilarga qo'ng'iroq qilish va suhbat yozuvlarini tinglash"
        actions={
          <div className="flex gap-2">
            <Button variant={tab === 'dial' ? 'primary' : 'secondary'} onClick={() => setTab('dial')}>
              <PhoneCall className="h-4 w-4" /> Qo'ng'iroq
            </Button>
            <Button variant={tab === 'history' ? 'primary' : 'secondary'} onClick={() => setTab('history')}>
              <History className="h-4 w-4" /> Yozuvlar tarixi
            </Button>
          </div>
        }
      />

      {configured === false && (
        <div className="mb-4 flex items-start gap-2.5 rounded-xl border border-amber-200 bg-amber-50 px-4 py-3 text-sm text-amber-800">
          <AlertTriangle className="mt-0.5 h-4 w-4 flex-shrink-0" />
          <span>
            Telefoniya hali ulanmagan — qo'ng'iroqlar ishlamaydi. Server sozlamalarida{' '}
            <code className="rounded bg-amber-100 px-1">MOIZVONKI_ENABLED/DOMAIN/USERNAME/API_KEY</code>{' '}
            muhit o'zgaruvchilarini bering.
          </span>
        </div>
      )}

      {error && (
        <div className="mb-4 flex items-start justify-between gap-2.5 rounded-xl border border-red-200 bg-red-50 px-4 py-3 text-sm text-red-800">
          <span className="flex items-start gap-2.5">
            <AlertTriangle className="mt-0.5 h-4 w-4 flex-shrink-0" /> {error}
          </span>
          <button type="button" onClick={() => setError('')} className="text-red-400 hover:text-red-600">
            <X className="h-4 w-4" />
          </button>
        </div>
      )}

      {tab === 'dial' ? (
        <div className="grid grid-cols-1 gap-4 lg:grid-cols-12">
          {/* ===== CHAP: o'quvchilar ro'yxati ===== */}
          <div className="space-y-4 lg:col-span-7">
            <Card tight>
              <div className="border-b border-slate-100 p-3">
                <div className="relative">
                  <Search className="absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-slate-400" />
                  <input
                    value={search}
                    onChange={(e) => setSearch(e.target.value)}
                    placeholder="Ism yoki telefon raqami bo'yicha qidirish..."
                    className={cn(control, 'pl-9')}
                  />
                </div>
              </div>
              <div className="max-h-[480px] overflow-y-auto">
                {filtered.length === 0 ? (
                  <p className="p-6 text-center text-sm text-slate-400">O'quvchi topilmadi.</p>
                ) : (
                  filtered.map((s) => {
                    const phone = s.phone || s.parentPhone || s.fatherPhone || s.motherPhone || ''
                    return (
                      <div
                        key={s.id}
                        onClick={() => setSelected(selected?.id === s.id ? null : s)}
                        className={cn(
                          'flex cursor-pointer items-center gap-3 border-b border-slate-50 px-4 py-2.5 transition-colors hover:bg-slate-50',
                          selected?.id === s.id && 'bg-brand-50/60',
                        )}
                      >
                        <div className="flex h-9 w-9 flex-shrink-0 items-center justify-center rounded-full bg-slate-100 text-slate-500">
                          <User className="h-4 w-4" />
                        </div>
                        <div className="min-w-0 flex-1">
                          <div className="truncate text-sm font-medium text-slate-800">{s.fullName}</div>
                          <div className="truncate text-xs text-slate-400">
                            {phone || 'Telefon kiritilmagan'}
                            {/* Guruh — TIRIK a'zoliklardan (`className` faqat zaxira). */}
                            {groupsText(statesToGroups(s.groupStates), s.className)
                              ? ` · ${groupsText(statesToGroups(s.groupStates), s.className)}`
                              : ''}
                          </div>
                        </div>
                        {canCall && (
                          <Button
                            variant="secondary"
                            type="button"
                            disabled={!phone || calling}
                            onClick={(e) => {
                              e.stopPropagation()
                              startCall({ studentId: s.id, name: s.fullName })
                            }}
                            className="flex-shrink-0"
                          >
                            <Phone className="h-4 w-4" /> Qo'ng'iroq
                          </Button>
                        )}
                      </div>
                    )
                  })
                )}
              </div>
            </Card>

            {/* O'quvchi detalli oynasi (qatorga bosilganda) */}
            {selected && (
              <StudentDetail student={selected} onClose={() => setSelected(null)} />
            )}
          </div>

          {/* ===== O'NG: dialpad + jonli holat ===== */}
          <div className="space-y-4 lg:col-span-5">
            <Card title="Raqam terish">
              <div className="flex items-center gap-2">
                <input
                  value={dial}
                  onChange={(e) => setDial(e.target.value.replace(/[^\d+*#]/g, ''))}
                  placeholder="+998 XX XXX XX XX"
                  className={cn(control, 'text-center text-lg font-semibold tracking-wider')}
                />
                <button
                  type="button"
                  onClick={() => setDial((d) => d.slice(0, -1))}
                  className="flex-shrink-0 rounded-lg p-2.5 text-slate-400 transition-colors hover:bg-slate-100 hover:text-slate-700"
                  title="O'chirish"
                >
                  <Delete className="h-5 w-5" />
                </button>
              </div>

              <div className="mx-auto mt-4 grid max-w-[240px] grid-cols-3 gap-2">
                {['1', '2', '3', '4', '5', '6', '7', '8', '9', '*', '0', '#'].map((k) => (
                  <button
                    key={k}
                    type="button"
                    onClick={() => setDial((d) => d + k)}
                    className="h-14 rounded-xl border border-slate-200 bg-white text-lg font-semibold text-slate-700 transition-colors hover:border-brand-300 hover:bg-brand-50 active:bg-brand-100"
                  >
                    {k}
                  </button>
                ))}
              </div>

              <div className="mt-4 flex gap-2">
                {canCall && (
                  <Button
                    onClick={() => startCall({ phoneNumber: dial })}
                    disabled={dial.replace(/\D/g, '').length < 7 || calling}
                    className="flex-1"
                  >
                    {calling ? <Loader2 className="h-4 w-4 animate-spin" /> : <PhoneCall className="h-4 w-4" />}
                    Qo'ng'iroq
                  </Button>
                )}
                <Button variant="secondary" onClick={() => setDial('')} disabled={!dial}>
                  Tozalash
                </Button>
              </div>
            </Card>

            {/* Jonli qo'ng'iroq kartasi */}
            {active && (
              <Card tight>
                <div className="p-4">
                  <div className="flex items-center justify-between gap-2">
                    <span
                      className={cn(
                        'inline-flex items-center gap-1.5 rounded-full border px-3 py-1 text-xs font-semibold',
                        STATUS_TONE[active.status],
                      )}
                    >
                      {!TERMINAL.includes(active.status) && (
                        <span className="h-2 w-2 animate-pulse rounded-full bg-current" />
                      )}
                      {STATUS_LABEL[active.status]}
                    </span>
                    <button
                      type="button"
                      onClick={() => setActive(null)}
                      className="rounded-lg p-1.5 text-slate-400 transition-colors hover:bg-red-50 hover:text-red-600"
                      title="Yopish"
                    >
                      <PhoneOff className="h-4 w-4" />
                    </button>
                  </div>
                  <div className="mt-3 text-center">
                    <div className="text-xl font-bold tracking-wide text-slate-800">{active.phoneNumber}</div>
                    {active.studentName && (
                      <div className="mt-0.5 text-sm text-slate-500">{active.studentName}</div>
                    )}
                    <div className="mt-2 font-mono text-2xl font-semibold text-brand-600">
                      {fmtDuration(elapsed)}
                    </div>
                  </div>
                </div>
              </Card>
            )}
          </div>
        </div>
      ) : (
        <CallHistory canSync={provider === 'moizvonki'} />
      )}
    </div>
  )
}

/* ============================ O'quvchi detali + qo'ng'iroqlar tarixi ============================ */

function StudentDetail({ student, onClose }: { student: Student; onClose: () => void }) {
  const [calls, setCalls] = useState<CallRow[] | null>(null)

  useEffect(() => {
    setCalls(null)
    getStudentCalls(student.id).then(setCalls).catch(() => setCalls([]))
  }, [student.id])

  return (
    <Card
      title={student.fullName}
      actions={
        <button type="button" onClick={onClose} className="rounded-lg p-1.5 text-slate-400 hover:bg-slate-100 hover:text-slate-700">
          <X className="h-4 w-4" />
        </button>
      }
    >
      <div className="grid grid-cols-1 gap-2 text-sm sm:grid-cols-2">
        <p><span className="text-slate-400">Telefon:</span> <span className="font-medium">{student.phone || '—'}</span></p>
        <p><span className="text-slate-400">Ota-ona:</span> <span className="font-medium">{student.parentPhone || '—'}</span></p>
        <p><span className="text-slate-400">Guruh:</span> <span className="font-medium">{student.className || '—'}</span></p>
        <p><span className="text-slate-400">Balans:</span> <span className={cn('font-medium', (student.balance ?? 0) < 0 ? 'text-red-600' : 'text-emerald-600')}>{(student.balance ?? 0).toLocaleString()} so'm</span></p>
      </div>

      <h4 className="mb-2 mt-4 text-xs font-bold uppercase tracking-wide text-slate-400">Qo'ng'iroqlar tarixi</h4>
      {calls === null ? (
        <Loader label="Yuklanmoqda..." />
      ) : calls.length === 0 ? (
        <p className="text-sm text-slate-400">Hali qo'ng'iroq qilinmagan.</p>
      ) : (
        <div className="space-y-1.5">
          {calls.map((c) => (
            <CallRowLine key={c.id} call={c} showStudent={false} />
          ))}
        </div>
      )}
    </Card>
  )
}

/* ============================ Yozuvlar tarixi (ikki ustunli: ro'yxat + detal) ============================ */

function CallHistory({ canSync }: { canSync: boolean }) {
  const [filters, setFilters] = useState<CallFilters>({
    search: '', dateFrom: '', dateTo: '', direction: '', status: '',
  })
  const [rows, setRows] = useState<CallRow[] | null>(null)
  const [total, setTotal] = useState(0)
  const [reloadKey, setReloadKey] = useState(0)
  const [syncing, setSyncing] = useState(false)
  const [syncMsg, setSyncMsg] = useState('')
  const [selectedId, setSelectedId] = useState<string | null>(null)

  const setF = <K extends keyof CallFilters>(k: K, v: CallFilters[K]) =>
    setFilters((f) => ({ ...f, [k]: v }))

  useEffect(() => {
    const t = setTimeout(() => {
      getCalls(filters, 1, 100).then((r) => {
        setRows(r.items)
        setTotal(r.total)
      }).catch(() => setRows([]))
    }, 300)
    return () => clearTimeout(t)
  }, [filters, reloadKey])

  const doSync = async () => {
    if (syncing) return
    setSyncing(true)
    setSyncMsg('')
    try {
      const r = await syncCallHistory()
      setSyncMsg(`${r.added} yangi, ${r.updated} yangilandi`)
      setReloadKey((k) => k + 1)
    } catch (err: any) {
      setSyncMsg(err.response?.data?.message || 'Sinxronlashda xato')
    } finally {
      setSyncing(false)
    }
  }

  return (
    <div className="grid grid-cols-1 gap-4 lg:grid-cols-12">
      {/* ===== CHAP: filtr + qo'ng'iroqlar ro'yxati ===== */}
      <div className="lg:col-span-5">
        <Card tight>
          {/* Filtrlar paneli */}
          <div className="space-y-2 border-b border-slate-100 p-3">
            <div className="flex items-center gap-3">
              <div className="relative flex-1">
                <Search className="absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-slate-400" />
                <input
                  value={filters.search}
                  onChange={(e) => setF('search', e.target.value)}
                  placeholder="Ism yoki raqam bo'yicha qidirish..."
                  className={cn(control, 'pl-9')}
                />
              </div>
              {canSync && (
                <Button variant="secondary" onClick={doSync} disabled={syncing} className="flex-shrink-0">
                  {syncing ? <Loader2 className="h-4 w-4 animate-spin" /> : <RefreshCw className="h-4 w-4" />}
                  Yangilash
                </Button>
              )}
            </div>
            {syncMsg && <p className="text-xs font-medium text-slate-400">{syncMsg}</p>}
            <div className="flex flex-wrap items-end gap-2">
              <div>
                <label className="mb-0.5 block text-[11px] font-semibold text-slate-400">Sanadan</label>
                <input type="date" value={filters.dateFrom} onChange={(e) => setF('dateFrom', e.target.value)} className={cn(control, 'w-auto py-1.5')} />
              </div>
              <div>
                <label className="mb-0.5 block text-[11px] font-semibold text-slate-400">Sanagacha</label>
                <input type="date" value={filters.dateTo} onChange={(e) => setF('dateTo', e.target.value)} className={cn(control, 'w-auto py-1.5')} />
              </div>
              <div>
                <label className="mb-0.5 block text-[11px] font-semibold text-slate-400">Yo'nalish</label>
                <select value={filters.direction} onChange={(e) => setF('direction', e.target.value as CallFilters['direction'])} className={cn(control, 'w-auto py-1.5')}>
                  <option value="">Barchasi</option>
                  <option value="outbound">Chiquvchi</option>
                  <option value="inbound">Kiruvchi</option>
                </select>
              </div>
              <div>
                <label className="mb-0.5 block text-[11px] font-semibold text-slate-400">Holat</label>
                <select value={filters.status} onChange={(e) => setF('status', e.target.value as CallFilters['status'])} className={cn(control, 'w-auto py-1.5')}>
                  <option value="">Barchasi</option>
                  <option value="answered">Javob berilgan</option>
                  <option value="missed">Javobsiz</option>
                </select>
              </div>
              {(filters.dateFrom || filters.dateTo || filters.direction || filters.status) && (
                <button
                  type="button"
                  onClick={() => setFilters((f) => ({ ...f, dateFrom: '', dateTo: '', direction: '', status: '' }))}
                  className="rounded-lg px-2 py-1.5 text-xs font-semibold text-slate-400 transition-colors hover:bg-slate-100 hover:text-slate-600"
                >
                  <X className="mr-1 inline h-3.5 w-3.5" />Filtrlarni tozalash
                </button>
              )}
            </div>
            <p className="text-xs font-medium text-slate-400">{total} ta qo'ng'iroq</p>
          </div>

          {/* Tekis ro'yxat */}
          <div className="max-h-[560px] overflow-y-auto">
            {rows === null ? (
              <Loader label="Yuklanmoqda..." />
            ) : rows.length === 0 ? (
              <p className="p-6 text-center text-sm text-slate-400">Qo'ng'iroqlar yo'q.</p>
            ) : (
              rows.map((c) => (
                <div
                  key={c.id}
                  onClick={() => setSelectedId(c.id)}
                  className={cn(
                    'flex cursor-pointer items-start gap-3 border-b border-slate-50 px-4 py-2.5 transition-colors hover:bg-slate-50',
                    selectedId === c.id && 'border-brand-300 bg-brand-50/60',
                  )}
                >
                  {c.direction === 'inbound'
                    ? <PhoneIncoming className="mt-0.5 h-4 w-4 flex-shrink-0 text-sky-500" />
                    : <PhoneOutgoing className="mt-0.5 h-4 w-4 flex-shrink-0 text-emerald-500" />}
                  <div className="min-w-0 flex-1">
                    <div className="truncate text-sm font-semibold text-slate-800">
                      {c.phoneNumber}
                      {c.studentName && <span className="ml-2 font-normal text-slate-400">{c.studentName}</span>}
                    </div>
                    <div className="mt-0.5 flex flex-wrap items-center gap-x-2 gap-y-0.5 text-xs text-slate-400">
                      <span>{fmtDateTime(c.startedAt)}</span>
                      {c.durationSeconds > 0 && <span className="font-mono">{fmtDuration(c.durationSeconds)}</span>}
                      <span className={cn('inline-flex items-center rounded-full border px-2 py-0.5 text-[11px] font-semibold', STATUS_TONE[c.status])}>
                        {STATUS_LABEL[c.status]}
                      </span>
                    </div>
                  </div>
                </div>
              ))
            )}
          </div>
        </Card>
      </div>

      {/* ===== O'NG: tanlangan qo'ng'iroq detali ===== */}
      <div className="lg:col-span-7">
        {selectedId ? (
          <CallDetailPanel callId={selectedId} />
        ) : (
          <Card>
            <p className="py-10 text-center text-sm text-slate-400">
              Chapdan qo'ng'iroqni tanlang — audio, transkript va AI tahlil shu yerda ochiladi.
            </p>
          </Card>
        )}
      </div>
    </div>
  )
}

/* ============================ O'ng panel: tanlangan qo'ng'iroq detali ============================ */

const DIRECTION_LABEL: Record<'inbound' | 'outbound', string> = {
  inbound: 'Kiruvchi',
  outbound: 'Chiquvchi',
}

function CallDetailPanel({ callId }: { callId: string }) {
  const [detail, setDetail] = useState<CallDetail | null>(null)
  const [loading, setLoading] = useState(true)
  const [transcribing, setTranscribing] = useState(false)
  const [analyzing, setAnalyzing] = useState(false)
  const [error, setError] = useState('')

  useEffect(() => {
    setDetail(null)
    setLoading(true)
    setError('')
    getCallDetail(callId)
      .then(setDetail)
      .catch((err: any) => setError(err.response?.data?.message || 'Yuklashda xato'))
      .finally(() => setLoading(false))
  }, [callId])

  const doTranscribe = async () => {
    if (transcribing || !detail) return
    setTranscribing(true)
    setError('')
    try {
      const r = await transcribeCall(detail.id)
      setDetail({ ...detail, transcript: r.transcript })
    } catch (err: any) {
      setError(err.response?.data?.message || 'Transkript qilishda xato')
    } finally {
      setTranscribing(false)
    }
  }

  const doAnalyze = async () => {
    if (analyzing || !detail) return
    setAnalyzing(true)
    setError('')
    try {
      const r = await analyzeCall(detail.id)
      setDetail({ ...detail, aiAnalysis: r.analysis })
    } catch (err: any) {
      setError(err.response?.data?.message || 'AI tahlilda xato')
    } finally {
      setAnalyzing(false)
    }
  }

  if (loading) {
    return (
      <Card>
        <Loader label="Yuklanmoqda..." />
      </Card>
    )
  }

  if (!detail) {
    return (
      <Card>
        <p className="py-10 text-center text-sm text-red-500">{error || 'Qo\'ng\'iroq topilmadi.'}</p>
      </Card>
    )
  }

  return (
    <div className="space-y-4">
      {/* Sarlavha */}
      <Card>
        <div className="flex flex-wrap items-start justify-between gap-3">
          <div>
            <div className="text-lg font-bold text-slate-800">
              {detail.phoneNumber}
              {detail.studentName && <span className="ml-2 text-base font-medium text-slate-400">{detail.studentName}</span>}
            </div>
            <div className="mt-1.5 flex flex-wrap items-center gap-x-3 gap-y-1 text-xs text-slate-500">
              <span>{fmtDateTime(detail.startedAt)}</span>
              {detail.durationSeconds > 0 && <span className="font-mono">{fmtDuration(detail.durationSeconds)}</span>}
              {detail.operatorName && <span>Operator: {detail.operatorName}</span>}
              <span className="inline-flex items-center gap-1">
                {detail.direction === 'inbound'
                  ? <PhoneIncoming className="h-3.5 w-3.5 text-sky-500" />
                  : <PhoneOutgoing className="h-3.5 w-3.5 text-emerald-500" />}
                {DIRECTION_LABEL[detail.direction]}
              </span>
            </div>
          </div>
          <span className={cn('inline-flex flex-shrink-0 items-center rounded-full border px-3 py-1 text-xs font-semibold', STATUS_TONE[detail.status])}>
            {STATUS_LABEL[detail.status]}
          </span>
        </div>

        {detail.hasRecording && (
          <div className="mt-3">
            <RecordingPlayer callId={detail.id} />
          </div>
        )}

        {error && (
          <div className="mt-3 flex items-start gap-2 rounded-lg border border-red-200 bg-red-50 px-3 py-2 text-xs text-red-700">
            <AlertTriangle className="mt-0.5 h-3.5 w-3.5 flex-shrink-0" /> {error}
          </div>
        )}

        <div className="mt-4 flex flex-wrap gap-2">
          <Button variant="secondary" onClick={doTranscribe} disabled={transcribing}>
            {transcribing ? <Loader2 className="h-4 w-4 animate-spin" /> : <Captions className="h-4 w-4" />}
            {transcribing ? 'Transkript qilinmoqda...' : 'Transkriptga o\'girish'}
          </Button>
          <Button
            variant="secondary"
            onClick={doAnalyze}
            disabled={analyzing || !detail.transcript}
            title={!detail.transcript ? 'Avval transkript qiling' : undefined}
          >
            {analyzing ? <Loader2 className="h-4 w-4 animate-spin" /> : <Sparkles className="h-4 w-4" />}
            {analyzing ? 'Tahlil qilinmoqda...' : 'AI tahlil'}
          </Button>
        </div>
      </Card>

      {/* Transkript */}
      <Card title="Transkript (so'zma-so'z)">
        {detail.transcript ? (
          <p className="whitespace-pre-wrap text-sm text-slate-700">{detail.transcript}</p>
        ) : (
          <p className="text-sm text-slate-400">Hali transkript qilinmagan — yuqoridagi tugmani bosing.</p>
        )}
      </Card>

      {/* AI tahlil */}
      <Card title="AI tahlil">
        {detail.aiAnalysis ? (
          <p className="whitespace-pre-wrap text-sm text-slate-700">{detail.aiAnalysis}</p>
        ) : (
          <p className="text-sm text-slate-400">Hali AI tahlil qilinmagan.</p>
        )}
      </Card>
    </div>
  )
}

/* ============================ Bitta qo'ng'iroq qatori + pleyer (o'quvchi detali tabida) ============================ */

function CallRowLine({ call, showStudent }: { call: CallRow; showStudent: boolean }) {
  return (
    <div className="rounded-xl border border-slate-100 bg-white px-3 py-2">
      <div className="flex flex-wrap items-center gap-x-3 gap-y-1">
        {call.direction === 'inbound'
          ? <PhoneIncoming className="h-3.5 w-3.5 flex-shrink-0 text-sky-500" />
          : <PhoneOutgoing className="h-3.5 w-3.5 flex-shrink-0 text-emerald-500" />}
        <span
          className={cn(
            'inline-flex items-center rounded-full border px-2 py-0.5 text-[11px] font-semibold',
            STATUS_TONE[call.status],
          )}
        >
          {STATUS_LABEL[call.status]}
        </span>
        {showStudent && (
          <span className="text-sm font-medium text-slate-800">
            {call.studentName || 'Nomaʼlum raqam'}
          </span>
        )}
        <span className="text-sm text-slate-600">{call.phoneNumber}</span>
        <span className="text-xs text-slate-400">{fmtDateTime(call.startedAt)}</span>
        {call.durationSeconds > 0 && (
          <span className="font-mono text-xs text-slate-500">{fmtDuration(call.durationSeconds)}</span>
        )}
        {call.operatorName && (
          <span className="text-xs text-slate-400">op: {call.operatorName}</span>
        )}
        <span className="ml-auto">
          {call.hasRecording && <RecordingPlayer callId={call.id} />}
        </span>
      </div>
    </div>
  )
}

/** Yozuvni bosilganda (auth bilan) yuklab, audio pleyerda ochadi.
 * Xato ikki xil ko'rsatiladi: yuklab bo'lmadi (server xabari) yoki format o'ynamadi. */
function RecordingPlayer({ callId }: { callId: string }) {
  const [url, setUrl] = useState<string | null>(null)
  const [loading, setLoading] = useState(false)
  const [failMsg, setFailMsg] = useState('')

  useEffect(() => () => { if (url) URL.revokeObjectURL(url) }, [url])

  if (url)
    return (
      <audio
        src={url}
        controls
        autoPlay
        className="h-8 max-w-[240px]"
        onError={() => {
          URL.revokeObjectURL(url)
          setUrl(null)
          setFailMsg("O'ynatib bo'lmadi (format)")
        }}
      />
    )
  return (
    <button
      type="button"
      disabled={loading || !!failMsg}
      onClick={async () => {
        setLoading(true)
        try {
          setUrl(await fetchRecordingUrl(callId))
        } catch (err: any) {
          // Blob so'rovda server xabari ham blob bo'lib keladi — matnini o'qiymiz.
          let msg = 'Yozuv topilmadi'
          try {
            const data = err.response?.data
            if (data instanceof Blob) {
              const parsed = JSON.parse(await data.text())
              if (parsed?.message) msg = parsed.message
            }
          } catch { /* standart xabar qoladi */ }
          setFailMsg(msg)
        } finally {
          setLoading(false)
        }
      }}
      className={cn(
        'inline-flex items-center gap-1.5 rounded-lg px-2.5 py-1 text-xs font-semibold transition-colors',
        failMsg
          ? 'cursor-default text-slate-300'
          : 'text-brand-600 hover:bg-brand-50 hover:text-brand-700',
      )}
      title={failMsg || 'Tinglash'}
    >
      {loading ? <Loader2 className="h-3.5 w-3.5 animate-spin" /> : <Play className="h-3.5 w-3.5" />}
      {failMsg || 'Tinglash'}
    </button>
  )
}
