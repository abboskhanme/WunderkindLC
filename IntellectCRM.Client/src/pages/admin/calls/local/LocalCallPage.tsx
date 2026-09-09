import { useEffect, useMemo, useRef, useState } from 'react'
import {
  History, Users, Search, X, AlertTriangle, PhoneIncoming, PhoneOutgoing, PhoneMissed,
  Phone, PhoneCall, Delete, User, Loader2, ChevronLeft, ChevronRight, Plus, Pencil, CircleDot,
  MessageSquare, Captions, Sparkles,
} from 'lucide-react'
import type { Student, Staff } from '@/types'
import { getStudents } from '@/api/services/students'
import { getStaff } from '@/api/services/staff'
import { useAuth } from '@/context/auth-context'
import {
  getCtiAgents, createCtiAgent, updateCtiAgent, dialCtiAgent, sendCtiSms,
  getCtiCalls, getCtiCallsGrouped, getCtiCallDetail, fetchCtiCallAudioUrl, updateCtiCallNote,
  transcribeCtiCall, analyzeCtiCall, getCtiSmsForNumber,
  type CtiAgent, type CtiCall, type CtiCallDetail, type CtiCallFilters, type CtiNumberGroup,
  type CtiSmsHistoryItem,
} from '@/api/services/cti'
import { Card } from '@/components/ui/Card'
import { Button } from '@/components/ui/Button'
import { PageHeader } from '@/components/ui/PageHeader'
import { Loader } from '@/components/ui/Loader'
import { Modal } from '@/components/ui/Modal'
import { Input, Textarea, Select } from '@/components/ui/Input'
import { cn, formatDateTime } from '@/lib/utils'
import { groupsText, statesToGroups } from '@/lib/studentGroups'

/* ============================================================
   Local Call — lokal CTI moduli: Android agent-telefonlar qo'ng'iroqlar
   tarixi/audiosini serverga yuboradi. Bu yerda operator tarixni ko'radi,
   audioni eshitadi, agentlar holatini kuzatadi va click-to-call qiladi.
   ============================================================ */

const control =
  'w-full rounded-lg border border-slate-200 bg-white px-3 py-2 text-sm text-slate-800 outline-none transition-colors focus:border-brand-400'

const DIRECTION_LABEL: Record<CtiCall['direction'], string> = {
  incoming: 'Kiruvchi',
  outgoing: 'Chiquvchi',
  missed: "O'tkazib yuborilgan",
}

const DIRECTION_TONE: Record<CtiCall['direction'], string> = {
  incoming: 'bg-emerald-50 text-emerald-700 border-emerald-200',
  outgoing: 'bg-sky-50 text-sky-700 border-sky-200',
  missed: 'bg-red-50 text-red-700 border-red-200',
}

function fmtDuration(sec: number): string {
  const m = Math.floor(sec / 60)
  const s = sec % 60
  return `${String(m).padStart(2, '0')}:${String(s).padStart(2, '0')}`
}

function DirectionIcon({ direction, className }: { direction: CtiCall['direction']; className?: string }) {
  if (direction === 'incoming') return <PhoneIncoming className={cn('text-emerald-500', className)} />
  if (direction === 'outgoing') return <PhoneOutgoing className={cn('text-sky-500', className)} />
  return <PhoneMissed className={cn('text-red-500', className)} />
}

export function LocalCallPage() {
  const { user } = useAuth()
  const isSuperAdmin = user?.role === 'superadmin'
  const [tab, setTab] = useState<'dial' | 'history' | 'agents'>('dial')

  const [agents, setAgents] = useState<CtiAgent[]>([])
  const [agentsLoaded, setAgentsLoaded] = useState(false)
  const [error, setError] = useState('')

  const reloadAgents = () => getCtiAgents().then(setAgents).catch(() => setAgents([]))

  useEffect(() => {
    reloadAgents().finally(() => setAgentsLoaded(true))
    const t = setInterval(reloadAgents, 10000)
    return () => clearInterval(t)
  }, [])

  return (
    <div>
      <PageHeader
        title="Local Call"
        sub="O'quvchilarga lokal agent-telefonlar orqali qo'ng'iroq qilish va suhbat yozuvlarini tinglash"
        actions={
          <div className="flex gap-2">
            <Button variant={tab === 'dial' ? 'primary' : 'secondary'} onClick={() => setTab('dial')}>
              <PhoneCall className="h-4 w-4" /> Qo'ng'iroq qilish
            </Button>
            <Button variant={tab === 'history' ? 'primary' : 'secondary'} onClick={() => setTab('history')}>
              <History className="h-4 w-4" /> Yozuvlar tarixi
            </Button>
            <Button variant={tab === 'agents' ? 'primary' : 'secondary'} onClick={() => setTab('agents')}>
              <Users className="h-4 w-4" /> Agentlar
            </Button>
          </div>
        }
      />

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

      {!agentsLoaded ? (
        <Card>
          <Loader label="Yuklanmoqda..." />
        </Card>
      ) : tab === 'dial' ? (
        <DialTab agents={agents} onError={setError} />
      ) : tab === 'history' ? (
        <HistoryTab agents={agents} onError={setError} />
      ) : (
        <AgentsTab agents={agents} onReload={reloadAgents} onError={setError} isSuperAdmin={isSuperAdmin} />
      )}
    </div>
  )
}

/* ============================ Tab: Qo'ng'iroq qilish (dial) ============================ */

function DialTab({ agents, onError }: { agents: CtiAgent[]; onError: (msg: string) => void }) {
  const [students, setStudents] = useState<Student[]>([])
  const [loadingStudents, setLoadingStudents] = useState(true)
  const [search, setSearch] = useState('')

  const [agentId, setAgentId] = useState('')
  const [dial, setDial] = useState('')
  const [calling, setCalling] = useState(false)
  const [result, setResult] = useState<{ delivered: boolean } | null>(null)
  const resultTimer = useRef<ReturnType<typeof setTimeout> | null>(null)
  // SMS yuborish oynasi — bosilgan raqam shu yerda saqlanadi (student qatori yoki dialpad'dan).
  const [smsFor, setSmsFor] = useState<string | null>(null)

  useEffect(() => {
    getStudents().then(setStudents).catch(() => setStudents([])).finally(() => setLoadingStudents(false))
  }, [])

  // Agentlar birinchi yuklanganda (yoki tanlangan agent ro'yxatdan tushib qolganda) — onlayn birinchisini tanlaymiz.
  useEffect(() => {
    if (agentId && agents.some((a) => a.id === agentId)) return
    const firstOnline = agents.find((a) => a.isOnline && a.isActive)
    setAgentId(firstOnline?.id ?? agents[0]?.id ?? '')
  }, [agents, agentId])

  const sortedAgents = useMemo(
    () => [...agents].sort((a, b) => Number(b.isOnline) - Number(a.isOnline)),
    [agents],
  )

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

  useEffect(() => () => { if (resultTimer.current) clearTimeout(resultTimer.current) }, [])

  const showResult = (delivered: boolean) => {
    setResult({ delivered })
    if (resultTimer.current) clearTimeout(resultTimer.current)
    resultTimer.current = setTimeout(() => setResult(null), 6000)
  }

  const startCall = async (number: string) => {
    if (calling || !number) return
    if (!agentId) {
      onError('Avval telefon (agent) tanlang')
      return
    }
    setCalling(true)
    setResult(null)
    try {
      const r = await dialCtiAgent(agentId, number)
      showResult(r.delivered)
    } catch (err: any) {
      onError(err.response?.data?.message || err.message || 'Xato yuz berdi')
    } finally {
      setCalling(false)
    }
  }

  if (loadingStudents) {
    return (
      <Card>
        <Loader label="Yuklanmoqda..." />
      </Card>
    )
  }

  return (
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
                    className="flex items-center gap-3 border-b border-slate-50 px-4 py-2.5 transition-colors hover:bg-slate-50"
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
                    <Button
                      variant="secondary"
                      type="button"
                      disabled={!phone || calling}
                      onClick={() => startCall(phone)}
                      className="flex-shrink-0"
                    >
                      <Phone className="h-4 w-4" /> Qo'ng'iroq
                    </Button>
                    <Button
                      variant="secondary"
                      type="button"
                      disabled={!phone}
                      onClick={() => setSmsFor(phone)}
                      className="flex-shrink-0"
                      title="SMS yuborish"
                    >
                      <MessageSquare className="h-4 w-4" />
                    </Button>
                  </div>
                )
              })
            )}
          </div>
        </Card>
      </div>

      {/* ===== O'NG: agent tanlash + dialpad ===== */}
      <div className="space-y-4 lg:col-span-5">
        <Card title="Raqam terish">
          <div className="mb-3">
            <label className="mb-1 block text-sm font-medium text-slate-600">Qaysi telefon orqali</label>
            <select value={agentId} onChange={(e) => setAgentId(e.target.value)} className={control}>
              {sortedAgents.length === 0 && <option value="">Agentlar yo'q</option>}
              {sortedAgents.map((a) => (
                <option key={a.id} value={a.id}>
                  {a.isOnline ? '● ' : ''}{a.displayName} {a.isOnline ? '(onlayn)' : '(oflayn)'}
                </option>
              ))}
            </select>
          </div>

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
            <Button
              onClick={() => startCall(dial)}
              disabled={dial.replace(/\D/g, '').length < 7 || calling || !agentId}
              className="flex-1"
            >
              {calling ? <Loader2 className="h-4 w-4 animate-spin" /> : <PhoneCall className="h-4 w-4" />}
              Qo'ng'iroq
            </Button>
            <Button
              variant="secondary"
              onClick={() => setSmsFor(dial)}
              disabled={dial.replace(/\D/g, '').length < 7}
            >
              <MessageSquare className="h-4 w-4" /> SMS
            </Button>
            <Button variant="secondary" onClick={() => setDial('')} disabled={!dial}>
              Tozalash
            </Button>
          </div>
        </Card>

        {/* Qo'ng'iroq natijasi (SignalR yo'q — faqat dial buyrug'i yetkazilgan/yetkazilmagani) */}
        {result && (
          <Card tight>
            <div
              className={cn(
                'flex items-start gap-2.5 p-4 text-sm font-medium',
                result.delivered ? 'text-emerald-700' : 'text-amber-700',
              )}
            >
              {result.delivered
                ? "Buyruq agent telefoniga yuborildi — telefon terilmoqda"
                : 'Agent oflayn — push yuborildi, telefon uyg\'onganda yetkazilmaydi/qayta urining'}
            </div>
          </Card>
        )}
      </div>

      {smsFor && (
        <SmsModal
          number={smsFor}
          agents={agents}
          onClose={() => setSmsFor(null)}
          onError={onError}
        />
      )}
    </div>
  )
}

/* ============================ Tab 1: Qo'ng'iroqlar tarixi ============================ */

function HistoryTab({ agents, onError }: { agents: CtiAgent[]; onError: (msg: string) => void }) {
  const [filters, setFilters] = useState<CtiCallFilters>({
    agentId: '', direction: '', dateFrom: '', dateTo: '', search: '',
  })
  // Raqam bo'yicha GURUHLANGAN qatorlar — bir raqamga 10 marta qo'ng'iroq = BITTA qator (soni bilan).
  const [rows, setRows] = useState<CtiNumberGroup[] | null>(null)
  const [total, setTotal] = useState(0)
  const [page, setPage] = useState(1)
  const pageSize = 20
  /** Tanlangan raqam — o'ng panelda shu raqamning BARCHA qo'ng'iroqlari ochiladi */
  const [selectedNumber, setSelectedNumber] = useState<string | null>(null)
  const [dialFor, setDialFor] = useState<string | null>(null)
  const [smsFor, setSmsFor] = useState<string | null>(null)

  const setF = <K extends keyof CtiCallFilters>(k: K, v: CtiCallFilters[K]) => {
    setFilters((f) => ({ ...f, [k]: v }))
    setPage(1)
  }

  useEffect(() => {
    const load = () =>
      getCtiCallsGrouped(filters, page, pageSize)
        .then((r) => { setRows(r.items); setTotal(r.total) })
        .catch(() => setRows((prev) => prev ?? []))
    // Filtr yozilayotganda ortiqcha so'rov bo'lmasin — 400ms debounce.
    const t = setTimeout(load, 400)
    // Ro'yxat JONLI: har 15 soniyada jimgina yangilanadi — oxirgi qo'ng'iroq qilgan
    // raqam avtomatik TEPAGA chiqadi (server LastAt bo'yicha kamayish tartibida beradi).
    const live = setInterval(load, 15000)
    return () => { clearTimeout(t); clearInterval(live) }
  }, [filters, page])

  const totalPages = Math.max(1, Math.ceil(total / pageSize))

  return (
    <div className="grid grid-cols-1 gap-4 lg:grid-cols-12">
      <div className="lg:col-span-7">
        <Card tight>
          {/* Filtrlar */}
          <div className="space-y-2 border-b border-slate-100 p-3">
            <div className="relative">
              <Search className="absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-slate-400" />
              <input
                value={filters.search}
                onChange={(e) => setF('search', e.target.value)}
                placeholder="Ism yoki raqam bo'yicha qidirish..."
                className={cn(control, 'pl-9')}
              />
            </div>
            <div className="flex flex-wrap items-end gap-2">
              <div>
                <label className="mb-0.5 block text-[11px] font-semibold text-slate-400">Agent</label>
                <select value={filters.agentId} onChange={(e) => setF('agentId', e.target.value)} className={cn(control, 'w-auto py-1.5')}>
                  <option value="">Barchasi</option>
                  {agents.map((a) => (
                    <option key={a.id} value={a.id}>{a.displayName}</option>
                  ))}
                </select>
              </div>
              <div>
                <label className="mb-0.5 block text-[11px] font-semibold text-slate-400">Yo'nalish</label>
                <select value={filters.direction} onChange={(e) => setF('direction', e.target.value as CtiCallFilters['direction'])} className={cn(control, 'w-auto py-1.5')}>
                  <option value="">Barchasi</option>
                  <option value="incoming">Kiruvchi</option>
                  <option value="outgoing">Chiquvchi</option>
                  <option value="missed">O'tkazib yuborilgan</option>
                </select>
              </div>
              <div>
                <label className="mb-0.5 block text-[11px] font-semibold text-slate-400">Sanadan</label>
                <input type="date" value={filters.dateFrom} onChange={(e) => setF('dateFrom', e.target.value)} className={cn(control, 'w-auto py-1.5')} />
              </div>
              <div>
                <label className="mb-0.5 block text-[11px] font-semibold text-slate-400">Sanagacha</label>
                <input type="date" value={filters.dateTo} onChange={(e) => setF('dateTo', e.target.value)} className={cn(control, 'w-auto py-1.5')} />
              </div>
              {(filters.agentId || filters.direction || filters.dateFrom || filters.dateTo) && (
                <button
                  type="button"
                  onClick={() => setFilters((f) => ({ ...f, agentId: '', direction: '', dateFrom: '', dateTo: '' }))}
                  className="rounded-lg px-2 py-1.5 text-xs font-semibold text-slate-400 transition-colors hover:bg-slate-100 hover:text-slate-600"
                >
                  <X className="mr-1 inline h-3.5 w-3.5" />Filtrlarni tozalash
                </button>
              )}
            </div>
            <p className="text-xs font-medium text-slate-400">{total} ta raqam</p>
          </div>

          {/* Ro'yxat — har RAQAM bitta qator (nechta qo'ng'iroq bo'lgani soni bilan) */}
          <div className="max-h-[560px] overflow-y-auto">
            {rows === null ? (
              <Loader label="Yuklanmoqda..." />
            ) : rows.length === 0 ? (
              <p className="p-6 text-center text-sm text-slate-400">Qo'ng'iroqlar yo'q.</p>
            ) : (
              rows.map((g) => (
                <div
                  key={g.remoteNumber}
                  onClick={() => setSelectedNumber(g.remoteNumber)}
                  className={cn(
                    'flex cursor-pointer items-start gap-3 border-b border-slate-50 px-4 py-2.5 transition-colors hover:bg-slate-50',
                    selectedNumber === g.remoteNumber && 'bg-brand-50/60',
                  )}
                >
                  <DirectionIcon direction={g.lastDirection} className="mt-0.5 h-4 w-4 flex-shrink-0" />
                  <div className="min-w-0 flex-1">
                    <div className="flex items-center gap-2">
                      <span className="truncate text-sm font-semibold text-slate-800">{g.remoteNumber}</span>
                      {(g.studentName || g.contactName) && (
                        <span className="truncate text-sm text-slate-400">{g.studentName || g.contactName}</span>
                      )}
                      <span className="inline-flex flex-shrink-0 items-center rounded-full bg-brand-100 px-2 py-0.5 text-[11px] font-bold text-brand-700">
                        {g.callCount} ta
                      </span>
                      {g.missedCount > 0 && (
                        <span className="inline-flex flex-shrink-0 items-center gap-1 rounded-full bg-red-50 px-2 py-0.5 text-[11px] font-semibold text-red-600">
                          <PhoneMissed className="h-3 w-3" /> {g.missedCount}
                        </span>
                      )}
                    </div>
                    <div className="mt-0.5 flex flex-wrap items-center gap-x-2 gap-y-0.5 text-xs text-slate-400">
                      <span>Oxirgisi: {formatDateTime(g.lastCallAt)}</span>
                      {g.lastAgentName && <span>{g.lastAgentName}</span>}
                      {g.lastDurationSec > 0 && <span className="font-mono">{fmtDuration(g.lastDurationSec)}</span>}
                      {g.hasAudio && <span className="text-brand-500">audio</span>}
                      <span className={cn('inline-flex items-center rounded-full border px-2 py-0.5 text-[11px] font-semibold', DIRECTION_TONE[g.lastDirection])}>
                        {DIRECTION_LABEL[g.lastDirection]}
                      </span>
                    </div>
                  </div>
                  <Button
                    variant="secondary"
                    type="button"
                    onClick={(e) => { e.stopPropagation(); setDialFor(g.remoteNumber) }}
                    className="flex-shrink-0"
                  >
                    <Phone className="h-4 w-4" /> Qo'ng'iroq qil
                  </Button>
                  <Button
                    variant="secondary"
                    type="button"
                    onClick={(e) => { e.stopPropagation(); setSmsFor(g.remoteNumber) }}
                    className="flex-shrink-0"
                    title="SMS yuborish"
                  >
                    <MessageSquare className="h-4 w-4" />
                  </Button>
                </div>
              ))
            )}
          </div>

          {/* Sahifalash */}
          {total > pageSize && (
            <div className="flex items-center justify-between border-t border-slate-100 px-4 py-2.5 text-xs">
              <span className="font-medium text-slate-400">
                {(page - 1) * pageSize + 1}–{Math.min(page * pageSize, total)} / {total}
              </span>
              <div className="flex items-center gap-1">
                <button
                  type="button"
                  disabled={page <= 1}
                  onClick={() => setPage((p) => Math.max(1, p - 1))}
                  className="rounded-lg border border-slate-200 p-1.5 text-slate-500 transition-colors hover:bg-slate-50 disabled:cursor-not-allowed disabled:opacity-40"
                  title="Oldingi sahifa"
                >
                  <ChevronLeft className="h-4 w-4" />
                </button>
                <span className="min-w-[64px] text-center font-medium text-slate-600">{page} / {totalPages}</span>
                <button
                  type="button"
                  disabled={page >= totalPages}
                  onClick={() => setPage((p) => Math.min(totalPages, p + 1))}
                  className="rounded-lg border border-slate-200 p-1.5 text-slate-500 transition-colors hover:bg-slate-50 disabled:cursor-not-allowed disabled:opacity-40"
                  title="Keyingi sahifa"
                >
                  <ChevronRight className="h-4 w-4" />
                </button>
              </div>
            </div>
          )}
        </Card>
      </div>

      <div className="lg:col-span-5">
        {selectedNumber ? (
          <NumberHistoryPanel number={selectedNumber} />
        ) : (
          <Card>
            <p className="py-10 text-center text-sm text-slate-400">
              Chapdan raqamni tanlang — shu raqamning barcha qo'ng'iroqlari shu yerda ochiladi.
            </p>
          </Card>
        )}
      </div>

      {dialFor && (
        <DialModal
          number={dialFor}
          agents={agents}
          onClose={() => setDialFor(null)}
          onError={onError}
        />
      )}

      {smsFor && (
        <SmsModal
          number={smsFor}
          agents={agents}
          onClose={() => setSmsFor(null)}
          onError={onError}
        />
      )}
    </div>
  )
}

/* ============================ O'ng panel: raqamning qo'ng'iroqlar tarixi ============================ */

/** Tanlangan raqamning BARCHA qo'ng'iroqlari (filtrsiz, to'liq tarix). Bitta qo'ng'iroq
 *  bosilganda uning to'liq detali (audio, hodisalar, izoh) ochiladi; "Orqaga" — ro'yxatga qaytadi. */
/** SMS holatini rangga aylantiradi (Eskiz granular holatlari + Local "yuborildi"/"yetkazilmadi"). */
function smsStatusTone(status: string): { label: string; className: string } {
  const s = (status || '').toUpperCase()
  if (s === 'DELIVRD' || s === 'DELIVERED' || s === 'YUBORILDI')
    return { label: 'Yetkazildi', className: 'border-emerald-200 bg-emerald-50 text-emerald-700' }
  if (s === 'WAITING' || s === 'NEW' || s === 'ACCEPTED' || s === 'STORED')
    return { label: 'Kutilmoqda', className: 'border-amber-200 bg-amber-50 text-amber-700' }
  return { label: 'Yetkazilmadi', className: 'border-red-200 bg-red-50 text-red-700' }
}

/** Bitta raqamning BIRLASHGAN vaqt chizig'i — qo'ng'iroqlar + SMS'lar (vaqt bo'yicha kamayish tartibida). */
type TimelineEntry =
  | { kind: 'call'; at: string; call: CtiCall }
  | { kind: 'sms'; at: string; sms: CtiSmsHistoryItem }

function NumberHistoryPanel({ number }: { number: string }) {
  const [calls, setCalls] = useState<CtiCall[] | null>(null)
  const [smsList, setSmsList] = useState<CtiSmsHistoryItem[] | null>(null)
  const [selectedId, setSelectedId] = useState<string | null>(null)

  useEffect(() => {
    setCalls(null)
    setSmsList(null)
    setSelectedId(null)
    const load = () => {
      getCtiCalls({}, 1, 200, number)
        .then((r) => setCalls(r.items))
        .catch(() => setCalls((prev) => prev ?? []))
      getCtiSmsForNumber(number)
        .then(setSmsList)
        .catch(() => setSmsList((prev) => prev ?? []))
    }
    load()
    // Jonli: ochiq raqamga yangi qo'ng'iroq/SMS kelsa ro'yxat 15 soniyada o'zi yangilanadi.
    const live = setInterval(load, 15000)
    return () => clearInterval(live)
  }, [number])

  const timeline = useMemo<TimelineEntry[]>(() => {
    const items: TimelineEntry[] = [
      ...(calls ?? []).map((c): TimelineEntry => ({ kind: 'call', at: c.startedAt, call: c })),
      ...(smsList ?? []).map((s): TimelineEntry => ({ kind: 'sms', at: s.createdAt, sms: s })),
    ]
    return items.sort((a, b) => (a.at < b.at ? 1 : -1))
  }, [calls, smsList])

  const loading = calls === null || smsList === null

  if (selectedId) {
    return (
      <div className="space-y-3">
        <button
          type="button"
          onClick={() => setSelectedId(null)}
          className="inline-flex items-center gap-1 rounded-lg px-2 py-1.5 text-sm font-semibold text-slate-500 transition-colors hover:bg-slate-100 hover:text-slate-700"
        >
          <ChevronLeft className="h-4 w-4" /> {number} — barcha tarix
        </button>
        <CallDetailPanel callId={selectedId} />
      </div>
    )
  }

  return (
    <Card tight>
      <div className="border-b border-slate-100 px-4 py-3">
        <div className="text-base font-bold text-slate-800">{number}</div>
        <div className="mt-0.5 text-xs text-slate-400">
          {loading
            ? 'Yuklanmoqda...'
            : `${calls!.length} ta qo'ng'iroq${calls!.length >= 200 ? ' (oxirgi 200 tasi)' : ''} · ${smsList!.length} ta SMS`}
          {calls?.[0] && (calls[0].studentName || calls[0].contactName) && (
            <span className="ml-2 font-medium text-slate-500">{calls[0].studentName || calls[0].contactName}</span>
          )}
        </div>
      </div>
      <div className="max-h-[560px] overflow-y-auto">
        {loading ? (
          <Loader label="Yuklanmoqda..." />
        ) : timeline.length === 0 ? (
          <p className="p-6 text-center text-sm text-slate-400">Tarix yo'q.</p>
        ) : (
          timeline.map((it) =>
            it.kind === 'call' ? (
              <div
                key={`call-${it.call.id}`}
                onClick={() => setSelectedId(it.call.id)}
                className="flex cursor-pointer items-center gap-3 border-b border-slate-50 px-4 py-2.5 transition-colors hover:bg-slate-50"
              >
                <DirectionIcon direction={it.call.direction} className="h-4 w-4 flex-shrink-0" />
                <div className="min-w-0 flex-1">
                  <div className="text-sm font-medium text-slate-700">{formatDateTime(it.call.startedAt)}</div>
                  <div className="mt-0.5 flex flex-wrap items-center gap-x-2 text-xs text-slate-400">
                    <span>{it.call.agentName}</span>
                    {it.call.durationSec > 0 ? (
                      <span className="font-mono">{fmtDuration(it.call.durationSec)}</span>
                    ) : (
                      <span>javobsiz</span>
                    )}
                    {it.call.hasAudio && <span className="text-brand-500">audio</span>}
                    {it.call.note && <span className="italic">izoh bor</span>}
                  </div>
                </div>
                <span className={cn('inline-flex flex-shrink-0 items-center rounded-full border px-2 py-0.5 text-[11px] font-semibold', DIRECTION_TONE[it.call.direction])}>
                  {DIRECTION_LABEL[it.call.direction]}
                </span>
                <ChevronRight className="h-4 w-4 flex-shrink-0 text-slate-300" />
              </div>
            ) : (
              <div key={`sms-${it.sms.id}`} className="flex items-start gap-3 border-b border-slate-50 px-4 py-2.5">
                <MessageSquare className="mt-0.5 h-4 w-4 flex-shrink-0 text-sky-500" />
                <div className="min-w-0 flex-1">
                  <div className="text-sm font-medium text-slate-700">{formatDateTime(it.sms.createdAt)}</div>
                  <p className="mt-0.5 whitespace-pre-wrap break-words text-xs text-slate-500">{it.sms.message}</p>
                </div>
                <div className="flex flex-shrink-0 flex-col items-end gap-1">
                  <span className={cn('inline-flex items-center rounded-full border px-2 py-0.5 text-[11px] font-semibold', smsStatusTone(it.sms.status).className)}>
                    {smsStatusTone(it.sms.status).label}
                  </span>
                  <span className="text-[11px] font-medium text-slate-400">
                    {it.sms.provider === 'local' ? 'Local' : 'Eskiz'}
                  </span>
                </div>
              </div>
            ),
          )
        )}
      </div>
    </Card>
  )
}

/* ============================ O'ng panel: qo'ng'iroq detali ============================ */

function CallDetailPanel({ callId }: { callId: string }) {
  const [detail, setDetail] = useState<CtiCallDetail | null>(null)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState('')
  const [note, setNote] = useState('')
  const [saving, setSaving] = useState(false)
  const [transcribing, setTranscribing] = useState(false)
  const [analyzing, setAnalyzing] = useState(false)

  useEffect(() => {
    setDetail(null)
    setLoading(true)
    setError('')
    getCtiCallDetail(callId)
      .then((d) => { setDetail(d); setNote(d.note) })
      .catch((err: any) => setError(err.response?.data?.message || 'Yuklashda xato'))
      .finally(() => setLoading(false))
  }, [callId])

  const saveNote = async () => {
    if (!detail || saving) return
    setSaving(true)
    setError('')
    try {
      await updateCtiCallNote(detail.id, note)
    } catch (err: any) {
      setError(err.response?.data?.message || 'Izohni saqlashda xato')
    } finally {
      setSaving(false)
    }
  }

  const doTranscribe = async () => {
    if (transcribing || !detail) return
    setTranscribing(true)
    setError('')
    try {
      const r = await transcribeCtiCall(detail.id)
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
      const r = await analyzeCtiCall(detail.id)
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
      <Card>
        <div className="flex flex-wrap items-start justify-between gap-3">
          <div>
            <div className="text-lg font-bold text-slate-800">
              {detail.remoteNumber}
              {(detail.studentName || detail.contactName) && (
                <span className="ml-2 text-base font-medium text-slate-400">
                  {detail.studentName || detail.contactName}
                </span>
              )}
            </div>
            <div className="mt-1.5 flex flex-wrap items-center gap-x-3 gap-y-1 text-xs text-slate-500">
              <span>{formatDateTime(detail.startedAt)}</span>
              {detail.durationSec > 0 && <span className="font-mono">{fmtDuration(detail.durationSec)}</span>}
              <span>Agent: {detail.agentName}</span>
              <span className="inline-flex items-center gap-1">
                <DirectionIcon direction={detail.direction} className="h-3.5 w-3.5" />
                {DIRECTION_LABEL[detail.direction]}
              </span>
            </div>
          </div>
          <span className={cn('inline-flex flex-shrink-0 items-center rounded-full border px-3 py-1 text-xs font-semibold', DIRECTION_TONE[detail.direction])}>
            {DIRECTION_LABEL[detail.direction]}
          </span>
        </div>

        {detail.hasAudio && (
          <div className="mt-3">
            <AudioPlayer callId={detail.id} />
          </div>
        )}

        {/* Hodisalar vaqt chizig'i */}
        <div className="mt-4">
          <h4 className="mb-2 text-xs font-bold uppercase tracking-wide text-slate-400">Hodisalar</h4>
          {detail.events.length === 0 ? (
            <p className="text-sm text-slate-400">Hodisalar yo'q.</p>
          ) : (
            <div className="space-y-1.5">
              {detail.events.map((ev, i) => (
                <div key={i} className="flex items-center gap-2 text-sm">
                  <CircleDot className="h-3.5 w-3.5 flex-shrink-0 text-brand-400" />
                  <span className="font-medium text-slate-700">{EVENT_LABEL[ev.type] ?? ev.type}</span>
                  <span className="text-xs text-slate-400">{formatDateTime(ev.at)}</span>
                </div>
              ))}
            </div>
          )}
        </div>

        {error && (
          <div className="mt-3 flex items-start gap-2 rounded-lg border border-red-200 bg-red-50 px-3 py-2 text-xs text-red-700">
            <AlertTriangle className="mt-0.5 h-3.5 w-3.5 flex-shrink-0" /> {error}
          </div>
        )}

        {/* Transkript + AI tahlil tugmalari (audio bo'lsa) */}
        {detail.hasAudio && (
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
        )}
      </Card>

      {/* Transkript (so'zlovchilar ajratilgan) */}
      {detail.hasAudio && (
        <Card title="Transkript (so'zlovchilar ajratilgan)">
          {detail.transcript ? (
            <p className="whitespace-pre-wrap text-sm text-slate-700">{detail.transcript}</p>
          ) : (
            <p className="text-sm text-slate-400">Hali transkript qilinmagan — yuqoridagi tugmani bosing.</p>
          )}
        </Card>
      )}

      {/* AI tahlil */}
      {detail.hasAudio && (
        <Card title="AI tahlil (Gemini)">
          {detail.aiAnalysis ? (
            <p className="whitespace-pre-wrap text-sm text-slate-700">{detail.aiAnalysis}</p>
          ) : (
            <p className="text-sm text-slate-400">Hali AI tahlil qilinmagan.</p>
          )}
        </Card>
      )}

      <Card title="Izoh">
        <textarea
          value={note}
          onChange={(e) => setNote(e.target.value)}
          rows={3}
          placeholder="Qo'ng'iroq bo'yicha izoh..."
          className={cn(control, 'resize-none')}
        />
        <div className="mt-2 flex justify-end">
          <Button onClick={saveNote} disabled={saving}>
            {saving ? <Loader2 className="h-4 w-4 animate-spin" /> : null}
            Saqlash
          </Button>
        </div>
      </Card>
    </div>
  )
}

const EVENT_LABEL: Record<string, string> = {
  ringing: 'Chalinmoqda',
  answered: 'Javob berildi',
  ended: 'Tugadi',
}

/** Yozuvni bosilganda (auth bilan) yuklab, audio pleyerda ochadi. */
function AudioPlayer({ callId }: { callId: string }) {
  const [url, setUrl] = useState<string | null>(null)
  const [loading, setLoading] = useState(false)
  const [failMsg, setFailMsg] = useState('')
  const urlRef = useRef<string | null>(null)
  urlRef.current = url

  useEffect(() => () => { if (urlRef.current) URL.revokeObjectURL(urlRef.current) }, [])

  useEffect(() => {
    setUrl(null)
    setFailMsg('')
    setLoading(true)
    fetchCtiCallAudioUrl(callId)
      .then(setUrl)
      .catch(() => setFailMsg('Yozuv topilmadi'))
      .finally(() => setLoading(false))
  }, [callId])

  if (loading) return <Loader label="Yuklanmoqda..." className="py-3" />
  if (failMsg) return <p className="text-sm text-red-500">{failMsg}</p>
  if (!url) return null
  return (
    <audio
      src={url}
      controls
      className="h-9 w-full"
      onError={() => setFailMsg("O'ynatib bo'lmadi (format)")}
    />
  )
}

/* ============================ Dial modal (raqamga qo'ng'iroq qilish) ============================ */

function DialModal({
  number, agents, onClose, onError,
}: {
  number: string
  agents: CtiAgent[]
  onClose: () => void
  onError: (msg: string) => void
}) {
  const online = agents.filter((a) => a.isOnline && a.isActive)
  const [agentId, setAgentId] = useState(online[0]?.id ?? '')
  const [calling, setCalling] = useState(false)
  const [result, setResult] = useState('')

  const doDial = async () => {
    if (!agentId || calling) return
    setCalling(true)
    setResult('')
    try {
      const r = await dialCtiAgent(agentId, number)
      setResult(r.delivered ? "Buyruq agentga yetkazildi" : 'Agent oflayn — push yuborildi, yetkazilmadi')
    } catch (err: any) {
      onError(err.response?.data?.message || 'Qo\'ng\'iroq qilishda xato')
      onClose()
    } finally {
      setCalling(false)
    }
  }

  return (
    <Modal open onClose={onClose} size="sm" title={`Qo'ng'iroq qil: ${number}`}>
      <div className="space-y-3">
        {agents.length === 0 ? (
          <p className="text-sm text-slate-400">Agentlar yo'q.</p>
        ) : (
          <div>
            <label className="mb-1 block text-sm font-medium text-slate-600">Agent tanlang</label>
            <select value={agentId} onChange={(e) => setAgentId(e.target.value)} className={control}>
              <option value="">— tanlang —</option>
              {agents.map((a) => (
                <option key={a.id} value={a.id} disabled={!a.isOnline || !a.isActive}>
                  {a.displayName} {a.isOnline ? '(onlayn)' : '(oflayn)'}
                </option>
              ))}
            </select>
            {online.length === 0 && (
              <p className="mt-1 text-xs text-amber-600">Hech qaysi agent onlayn emas — push yuboriladi, lekin yetkazilmasligi mumkin.</p>
            )}
          </div>
        )}

        {result && (
          <div className={cn('rounded-lg px-3 py-2 text-sm', result.includes('yetkazilmadi') ? 'bg-amber-50 text-amber-700' : 'bg-emerald-50 text-emerald-700')}>
            {result}
          </div>
        )}

        <div className="flex justify-end gap-2">
          <Button variant="secondary" onClick={onClose}>Yopish</Button>
          <Button onClick={doDial} disabled={!agentId || calling}>
            {calling ? <Loader2 className="h-4 w-4 animate-spin" /> : <Phone className="h-4 w-4" />}
            Qo'ng'iroq qil
          </Button>
        </div>
      </div>
    </Modal>
  )
}

/* ============================ SMS modal (raqamga ixtiyoriy matn yuborish) ============================ */

function SmsModal({
  number, agents, onClose, onError,
}: {
  number: string
  agents: CtiAgent[]
  onClose: () => void
  onError: (msg: string) => void
}) {
  const online = agents.filter((a) => a.isOnline && a.isActive)
  const [agentId, setAgentId] = useState(online[0]?.id ?? '')
  const [text, setText] = useState('')
  const [sending, setSending] = useState(false)
  const [result, setResult] = useState('')

  const doSend = async () => {
    if (!agentId || !text.trim() || sending) return
    setSending(true)
    setResult('')
    try {
      const r = await sendCtiSms(agentId, number, text.trim())
      setResult(r.delivered ? 'SMS agentga yetkazildi — telefon yubormoqda' : 'Agent oflayn — push yuborildi, yetkazilmadi')
    } catch (err: any) {
      onError(err.response?.data?.message || 'SMS yuborishda xato')
      onClose()
    } finally {
      setSending(false)
    }
  }

  return (
    <Modal open onClose={onClose} size="sm" title={`SMS yuborish: ${number}`}>
      <div className="space-y-3">
        {agents.length === 0 ? (
          <p className="text-sm text-slate-400">Agentlar yo'q.</p>
        ) : (
          <div>
            <label className="mb-1 block text-sm font-medium text-slate-600">Qaysi telefondan</label>
            <select value={agentId} onChange={(e) => setAgentId(e.target.value)} className={control}>
              <option value="">— tanlang —</option>
              {agents.map((a) => (
                <option key={a.id} value={a.id} disabled={!a.isOnline || !a.isActive}>
                  {a.displayName} {a.isOnline ? '(onlayn)' : '(oflayn)'}
                </option>
              ))}
            </select>
            {online.length === 0 && (
              <p className="mt-1 text-xs text-amber-600">Hech qaysi agent onlayn emas — push yuboriladi, lekin yetkazilmasligi mumkin.</p>
            )}
          </div>
        )}

        <div>
          <label className="mb-1 block text-sm font-medium text-slate-600">Matn</label>
          <Textarea
            rows={4}
            value={text}
            onChange={(e) => setText(e.target.value)}
            placeholder="SMS matnini yozing..."
            maxLength={1000}
          />
          <p className="mt-1 text-right text-xs text-slate-400">{text.length}/1000</p>
        </div>

        {result && (
          <div className={cn('rounded-lg px-3 py-2 text-sm', result.includes('yetkazilmadi') ? 'bg-amber-50 text-amber-700' : 'bg-emerald-50 text-emerald-700')}>
            {result}
          </div>
        )}

        <div className="flex justify-end gap-2">
          <Button variant="secondary" onClick={onClose}>Yopish</Button>
          <Button onClick={doSend} disabled={!agentId || !text.trim() || sending}>
            {sending ? <Loader2 className="h-4 w-4 animate-spin" /> : <MessageSquare className="h-4 w-4" />}
            Yuborish
          </Button>
        </div>
      </div>
    </Modal>
  )
}

/* ============================ Tab 2: Agentlar ============================ */

function AgentsTab({
  agents, onReload, onError, isSuperAdmin,
}: {
  agents: CtiAgent[]
  onReload: () => void
  onError: (msg: string) => void
  isSuperAdmin: boolean
}) {
  const [formOpen, setFormOpen] = useState(false)
  const [editing, setEditing] = useState<CtiAgent | null>(null)
  const [dialFor, setDialFor] = useState<CtiAgent | null>(null)
  const [dialNumber, setDialNumber] = useState('')

  return (
    <div>
      <div className="mb-3 flex justify-end">
        <Button onClick={() => { setEditing(null); setFormOpen(true) }}>
          <Plus className="h-4 w-4" /> Agent qo'shish
        </Button>
      </div>

      {agents.length === 0 ? (
        <Card>
          <p className="py-10 text-center text-sm text-slate-400">Hali agent qo'shilmagan.</p>
        </Card>
      ) : (
        <div className="grid grid-cols-1 gap-3 sm:grid-cols-2 lg:grid-cols-3">
          {agents.map((a) => (
            <Card key={a.id} tight>
              <div className="p-4">
                <div className="flex items-start justify-between gap-2">
                  <div className="min-w-0">
                    <div className="flex items-center gap-1.5">
                      <span className={cn('h-2 w-2 flex-shrink-0 rounded-full', a.isOnline ? 'bg-emerald-500' : 'bg-slate-300')} />
                      <span className="truncate text-sm font-bold text-slate-800">{a.displayName}</span>
                    </div>
                    <p className="mt-0.5 truncate text-xs text-slate-400">{a.login}</p>
                  </div>
                  <span className={cn(
                    'flex-shrink-0 rounded-full border px-2 py-0.5 text-[11px] font-semibold',
                    a.isActive ? 'border-emerald-200 bg-emerald-50 text-emerald-700' : 'border-slate-200 bg-slate-100 text-slate-500',
                  )}>
                    {a.isActive ? 'Faol' : 'Nofaol'}
                  </span>
                </div>

                <div className="mt-2.5 space-y-1 text-xs text-slate-500">
                  <p>Oxirgi ko'rinish: {a.lastSeenAt ? formatDateTime(a.lastSeenAt) : '—'}</p>
                  <p>FCM: {a.hasFcmToken ? 'bor' : "yo'q"}</p>
                  {isSuperAdmin && (
                    <p>Xodim: {a.staffUserName || <span className="text-amber-600">biriktirilmagan</span>}</p>
                  )}
                </div>

                <div className="mt-3 flex gap-2">
                  <Button variant="secondary" onClick={() => { setEditing(a); setFormOpen(true) }} className="flex-1">
                    <Pencil className="h-3.5 w-3.5" /> Tahrirlash
                  </Button>
                  <Button variant="secondary" onClick={() => { setDialFor(a); setDialNumber('') }} className="flex-1">
                    <Phone className="h-3.5 w-3.5" /> Qo'ng'iroq
                  </Button>
                </div>
              </div>
            </Card>
          ))}
        </div>
      )}

      {formOpen && (
        <AgentFormModal
          editing={editing}
          onClose={() => setFormOpen(false)}
          onSaved={() => { setFormOpen(false); onReload() }}
          onError={onError}
          isSuperAdmin={isSuperAdmin}
        />
      )}

      {dialFor && (
        <Modal open onClose={() => setDialFor(null)} size="sm" title={`Qo'ng'iroq qil: ${dialFor.displayName}`}>
          <div className="space-y-3">
            <Input
              label="Telefon raqami"
              value={dialNumber}
              onChange={(e) => setDialNumber(e.target.value.replace(/[^\d+]/g, ''))}
              placeholder="+998 XX XXX XX XX"
            />
            <div className="flex justify-end gap-2">
              <Button variant="secondary" onClick={() => setDialFor(null)}>Yopish</Button>
              <Button
                disabled={dialNumber.replace(/\D/g, '').length < 7}
                onClick={async () => {
                  try {
                    const r = await dialCtiAgent(dialFor.id, dialNumber)
                    onError(r.delivered ? '' : 'Agent oflayn — push yuborildi, yetkazilmadi')
                  } catch (err: any) {
                    onError(err.response?.data?.message || 'Qo\'ng\'iroq qilishda xato')
                  } finally {
                    setDialFor(null)
                  }
                }}
              >
                <Phone className="h-4 w-4" /> Qo'ng'iroq qil
              </Button>
            </div>
          </div>
        </Modal>
      )}
    </div>
  )
}

/* ============================ Agent qo'shish/tahrirlash modal ============================ */

function AgentFormModal({
  editing, onClose, onSaved, onError, isSuperAdmin,
}: {
  editing: CtiAgent | null
  onClose: () => void
  onSaved: () => void
  onError: (msg: string) => void
  isSuperAdmin: boolean
}) {
  const [login, setLogin] = useState(editing?.login ?? '')
  const [displayName, setDisplayName] = useState(editing?.displayName ?? '')
  const [password, setPassword] = useState('')
  const [isActive, setIsActive] = useState(editing?.isActive ?? true)
  const [staffUserId, setStaffUserId] = useState(editing?.staffUserId ?? '')
  const [staffList, setStaffList] = useState<Staff[]>([])
  const [saving, setSaving] = useState(false)

  // Xodimga biriktirish dropdown'i faqat SuperAdmin uchun (boshqalar o'ziga avtomatik biriktiriladi).
  useEffect(() => {
    if (!isSuperAdmin) return
    getStaff().then(setStaffList).catch(() => setStaffList([]))
  }, [isSuperAdmin])

  const canSave = displayName.trim() && (editing || (login.trim() && password.trim()))

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault()
    if (!canSave || saving) return
    setSaving(true)
    try {
      if (editing) {
        await updateCtiAgent(editing.id, {
          displayName: displayName.trim(), isActive, password,
          staffUserId: isSuperAdmin ? (staffUserId || null) : undefined,
        })
      } else {
        await createCtiAgent({
          login: login.trim(), password, displayName: displayName.trim(),
          staffUserId: isSuperAdmin ? (staffUserId || null) : undefined,
        })
      }
      onSaved()
    } catch (err: any) {
      onError(err.response?.data?.message || 'Saqlashda xato')
    } finally {
      setSaving(false)
    }
  }

  return (
    <Modal
      open
      onClose={onClose}
      size="sm"
      title={editing ? 'Agentni tahrirlash' : "Yangi agent qo'shish"}
      footer={
        <>
          <Button variant="secondary" onClick={onClose}>Bekor qilish</Button>
          <Button type="submit" form="cti-agent-form" disabled={!canSave || saving}>
            {saving ? 'Saqlanmoqda...' : 'Saqlash'}
          </Button>
        </>
      }
    >
      <form id="cti-agent-form" onSubmit={handleSubmit} className="space-y-3">
        {!editing && (
          <Input
            label="Login"
            required
            value={login}
            onChange={(e) => setLogin(e.target.value)}
            placeholder="agent1"
          />
        )}
        <Input
          label="Ism (ko'rinadigan)"
          required
          value={displayName}
          onChange={(e) => setDisplayName(e.target.value)}
          placeholder="Masalan: Dilnoza (call center)"
        />
        <Input
          label={editing ? "Yangi parol (bo'sh — o'zgarmaydi)" : 'Parol'}
          required={!editing}
          type="text"
          value={password}
          onChange={(e) => setPassword(e.target.value)}
          placeholder={editing ? "o'zgartirmaslik uchun bo'sh qoldiring" : ''}
        />
        {editing && (
          <label className="flex items-center gap-2 text-sm font-medium text-slate-600">
            <input type="checkbox" checked={isActive} onChange={(e) => setIsActive(e.target.checked)} className="accent-brand-600" />
            Faol
          </label>
        )}
        {isSuperAdmin ? (
          <Select
            label="Xodimga biriktirish"
            value={staffUserId}
            onChange={(e) => setStaffUserId(e.target.value)}
          >
            <option value="">Biriktirilmagan (faqat SuperAdmin ko'radi)</option>
            {staffList.map((s) => (
              <option key={s.id} value={s.id}>{s.fullName}</option>
            ))}
          </Select>
        ) : !editing ? (
          <p className="text-xs text-slate-400">
            Bu agent avtomatik ravishda sizga biriktiriladi — qo'ng'iroqlari faqat sizga (va SuperAdmin'ga) ko'rinadi.
          </p>
        ) : null}
      </form>
    </Modal>
  )
}
