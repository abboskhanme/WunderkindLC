import { useCallback, useEffect, useMemo, useState } from 'react'
import {
  PhoneCall, Search, AlertTriangle, History, CalendarDays, X, Columns3,
  LayoutGrid, RefreshCw, Sun, Flame, CalendarClock, Filter,
} from 'lucide-react'
import {
  getContactMeta, getContactRequests, getContactRequest, reopenContactRequest,
  deleteContactRequest, addContactNote,
  type ContactMeta, type ContactRequestItem, type ContactDue,
} from '@/api/services/contacts'
import { moveContactStage } from '@/api/services/contacts'
import {
  getContactStages, createContactStage, updateContactStage, deleteContactStage,
  reorderContactStages, type ContactStage, type ContactStagePayload,
} from '@/api/services/contactStages'
import { getActionReasons } from '@/api/services/actionReasons'
import type { ActionReason } from '@/types'
import { ContactAttemptModal } from './ContactAttemptModal'
import { ContactBoard, type DropIntent } from './ContactBoard'
import { ContactCardContent, type ContactCardActions } from './ContactCard'
import { ContactStageFormModal } from './ContactStageFormModal'
import { MonthDayStrip } from '@/components/ui/MonthDayStrip'
import { currentMonth, todayIso } from '@/lib/month'
import {
  bucketOf, isTodo, stageKeyOf, stageColumns, DUE_COLUMNS, STATUS_COLUMNS,
  type BoardColumn,
} from '@/lib/contactDue'
import { ContactStatsPanel } from './ContactStatsPanel'
import { Card } from '@/components/ui/Card'
import { Button } from '@/components/ui/Button'
import { Modal } from '@/components/ui/Modal'
import { Loader } from '@/components/ui/Loader'
import { PageHeader } from '@/components/ui/PageHeader'
import { usePerm } from '@/lib/permissions'
import { apiErrorMessage, cn, formatDate, formatDateTime } from '@/lib/utils'
import { tabFromUrl } from '@/lib/tabParam'

type Tab = 'navbat' | 'hisobot'
const CONTACT_TABS = ['navbat', 'hisobot'] as const

type GroupBy = 'due' | 'status'
type View = 'board' | 'list'

/** Serverning bir so'rovdagi chegarasi (`ContactsController`: `Math.Clamp(limit, 1, 500)`). */
const MAX_ROWS = 500

/** Ustunlarni toraytiradigan "fokus" tugmalari — sahifadagi eng katta raqamlar. */
const FOCUS_TILES: {
  key: ContactDue
  label: string
  hint: string
  icon: typeof Flame
  ring: string
  text: string
  iconCls: string
}[] = [
  { key: 'todo', label: 'Bugun qilish kerak', hint: "Muddati o'tgan + bugungi + sanasiz", icon: Flame, ring: 'border-amber-300 bg-amber-50', text: 'text-amber-600', iconCls: 'bg-amber-100 text-amber-600' },
  { key: 'overdue', label: "Muddati o'tgan", hint: 'Kechikkan — birinchi shular', icon: AlertTriangle, ring: 'border-rose-300 bg-rose-50', text: 'text-rose-600', iconCls: 'bg-rose-100 text-rose-600' },
  { key: 'today', label: 'Bugun', hint: 'Aynan bugunga rejalashtirilgan', icon: Sun, ring: 'border-sky-300 bg-sky-50', text: 'text-sky-600', iconCls: 'bg-sky-100 text-sky-600' },
  { key: 'tomorrow', label: 'Ertaga', hint: "Ertangi qayta qo'ng'iroqlar", icon: CalendarClock, ring: 'border-slate-300 bg-slate-50', text: 'text-slate-700', iconCls: 'bg-slate-100 text-slate-500' },
]

/** "2026-08-05" → "05.08, Chor". */
const weekdays = ['Yak', 'Du', 'Se', 'Chor', 'Pay', 'Ju', 'Sha']
function dayLabel(iso: string): string {
  const d = new Date(`${iso}T00:00:00`)
  if (Number.isNaN(d.getTime())) return iso
  return `${iso.slice(8, 10)}.${iso.slice(5, 7)}, ${weekdays[d.getDay()]}`
}

/** Ro'yxat ko'rinishidagi tartib — SHOSHILINCHLIK bo'yicha (taxtadagi ustunlar tartibi). */
const URGENCY: Record<string, number> = {
  overdue: 0, today: 1, nodate: 2, tomorrow: 3, week: 4, later: 5, '': 6,
}

function readPref<T extends string>(key: string, allowed: readonly T[], fallback: T): T {
  if (typeof window === 'undefined') return fallback
  const raw = window.localStorage.getItem(key)
  return raw && (allowed as readonly string[]).includes(raw) ? (raw as T) : fallback
}

/**
 * BOG'LANISH KERAK — o'quvchi bilan bog'lanish NAVBATI (kanban taxtasi) va hisobotlari.
 *
 * <p>Taxta ikki xil guruhlanadi: <b>Muddat</b> (standart — "qachon qo'ng'iroq kerak", operatorning
 * asosiy savoli, `.claude/rules/contacts.md` §3.6) va <b>Bosqich</b> ("talab qayerda turibdi").
 * Ranglar HAR IKKI rejimda ham shoshilinchlikni bildiradi, ya'ni kechikkan karta bosqich
 * rejimida ham qizil bo'lib ko'zga tashlanadi.</p>
 *
 * <p>⚠️ <b>Navbat BIR so'rovda olinadi</b> (`limit=500`) va ustunlarga KLIENTDA bo'linadi
 * (`lib/contactDue.ts`) — shuning uchun qidiruv, sabab va "yuborgan" filtrlari serverga
 * so'rov yubormaydi va bir zumda ishlaydi. Serverga faqat QAMROV (ochiqlar/hammasi) va
 * kalendar oyi uzatiladi.</p>
 *
 * <p>Ruxsat: `contacts` (O'quvchilar bo'limidan alohida).</p>
 */
export function ContactQueuePage() {
  const { can } = usePerm()
  const canWrite = can('contacts', 'edit')
  const canDelete = can('contacts', 'delete')

  // `?tab=hisobot` — "Hisobotlar" bo'limidan to'g'ridan-to'g'ri bog'lanish hisobotiga.
  const [tab, setTab] = useState<Tab>(() => tabFromUrl(CONTACT_TABS, 'navbat'))
  const [meta, setMeta] = useState<ContactMeta>({ statuses: [], results: [], counts: [], overdue: 0 })
  const [items, setItems] = useState<ContactRequestItem[]>([])
  const [reasons, setReasons] = useState<ActionReason[]>([])
  /** Taxta ustunlari (foydalanuvchi boshqaradi) — "Bosqich" rejimida ishlatiladi. */
  const [stages, setStages] = useState<ContactStage[]>([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState('')

  /** Bugungi kun — sahifa ochilganda bir marta (barcha muddat hisoblari shundan). */
  const today = useMemo(() => todayIso(), [])

  /* ---------- Ko'rinish (brauzerda eslab qolinadi) ---------- */
  const [groupBy, setGroupBy] = useState<GroupBy>(() => readPref('contacts:groupBy', ['due', 'status'] as const, 'due'))
  const [view, setView] = useState<View>(() => readPref('contacts:view', ['board', 'list'] as const, 'board'))

  /* ---------- Filtrlar ---------- */
  /**
   * YAGONA server filtri: ochiqlar (new+callback) yoki hammasi.
   *
   * ⚠️ Boshlang'ich qiymat GURUHLASHDAN kelib chiqadi: "Bosqich" rejimida yakuniy ("Hal bo'ldi",
   * "Bog'lanib bo'lmadi") ustunlari ham bor — ochiqlar bilan ular DOIM bo'sh turardi, ya'ni
   * saqlangan tanlov bilan sahifa qayta ochilganda taxta yarim ishlagandek ko'rinardi.
   */
  const [scope, setScope] = useState<'open' | 'all'>(() => (groupBy === 'status' ? 'all' : 'open'))
  const [search, setSearch] = useState('')
  /** Sabab: 'all' | '__none__' (sababsiz) | reasonId. */
  const [reason, setReason] = useState('all')
  /** Talabni ochgan xodim/o'qituvchi: 'all' | ism. */
  const [author, setAuthor] = useState('all')
  /** Katta raqamlardan tanlangan fokus — taxtani shu guruhga toraytiradi. */
  const [focus, setFocus] = useState<ContactDue | ''>('')
  /** Kalendardan tanlangan ANIQ kun. */
  const [dueDate, setDueDate] = useState('')
  const [month, setMonth] = useState(currentMonth())
  const [calOpen, setCalOpen] = useState(false)

  const pickGroupBy = (g: GroupBy) => {
    setGroupBy(g)
    window.localStorage.setItem('contacts:groupBy', g)
    // Bosqich rejimida yakuniy ustunlar ham ko'rinsin — aks holda ikkitasi doim bo'sh turardi.
    if (g === 'status') setScope('all')
  }
  const pickView = (v: View) => {
    setView(v)
    window.localStorage.setItem('contacts:view', v)
  }

  /* ---------- Modallar ---------- */
  const [attemptFor, setAttemptFor] = useState<ContactRequestItem | null>(null)
  /** Karta sudrab tashlanganda oldindan tanlanadigan keyingi qadam. */
  const [preset, setPreset] = useState<{ nextStatus?: string; dueDate?: string; stageId?: string }>({})
  /* Ustun (kanban) oynasi */
  const [stageFormOpen, setStageFormOpen] = useState(false)
  const [editingStage, setEditingStage] = useState<ContactStage | null>(null)
  const [stageBusy, setStageBusy] = useState(false)
  const [detail, setDetail] = useState<ContactRequestItem | null>(null)
  const [noteFor, setNoteFor] = useState<ContactRequestItem | null>(null)
  const [noteText, setNoteText] = useState('')

  const load = useCallback(async () => {
    setLoading(true)
    try {
      const [m, list, st] = await Promise.all([
        getContactMeta(month),
        // Butun navbat BIR so'rovda — qolgan filtrlar klientda (yuqoridagi izoh).
        getContactRequests({ status: scope === 'all' ? 'all' : undefined, limit: MAX_ROWS }),
        // Ustunlar server bermasa (eski backend) taxta ZAXIRA ro'yxat bilan ishlayveradi.
        getContactStages().catch(() => [] as ContactStage[]),
      ])
      setMeta(m)
      setItems(list)
      setStages(st)
      setError('')
    } catch (e) {
      setError(apiErrorMessage(e, "Navbatni yuklab bo'lmadi"))
    } finally {
      setLoading(false)
    }
  }, [scope, month])

  useEffect(() => {
    // eslint-disable-next-line react-hooks/set-state-in-effect -- qamrov/oy o'zgarganda qayta yuklash (maqsadli)
    void load()
  }, [load])

  /** Sabablar katalogi — "Sabab" filtri uchun (bir marta). */
  useEffect(() => {
    getActionReasons()
      .then((all) => setReasons(all.filter((r) => r.category === 'contact')))
      .catch(() => setReasons([]))
  }, [])

  const countOf = useCallback(
    (key: string) => meta.counts.find((c) => c.key === key)?.count ?? 0,
    [meta.counts],
  )
  const openCount = useMemo(() => countOf('new') + countOf('callback'), [countOf])

  /** "Yuborgan" ro'yxati — kelgan ma'lumotdan (alohida so'rov kerak emas). */
  const authors = useMemo(
    () => [...new Set(items.map((r) => r.createdBy).filter(Boolean))].sort((a, b) => a.localeCompare(b)),
    [items],
  )

  /* ---------- KLIENT filtri ---------- */
  const visible = useMemo(() => {
    const q = search.trim().toLowerCase()
    const qDigits = q.replace(/\D/g, '')
    return items.filter((r) => {
      if (reason === '__none__' ? !!r.reasonId : reason !== 'all' && r.reasonId !== reason) return false
      if (author !== 'all' && r.createdBy !== author) return false
      // Aniq kun — server semantikasi bilan bir xil: faqat o'sha kunga rejalashtirilganlar.
      if (dueDate && !(r.status === 'callback' && r.dueDate === dueDate)) return false
      if (focus) {
        const b = bucketOf(r.status, r.dueDate, today)
        if (focus === 'todo' ? !isTodo(b) : b !== focus) return false
      }
      if (!q) return true
      // Qidiruv serverdagidan KENG: server faqat ism va sababdan qidiradi, bu yerda izoh,
      // oxirgi javob, yuborgan xodim va telefon raqamlari ham qamraladi.
      const text = [r.studentName, r.reasonLabel, r.note, r.lastResponse, r.createdBy, r.lastActorName]
        .filter(Boolean).join(' ').toLowerCase()
      if (text.includes(q)) return true
      return qDigits.length >= 3 && r.phones.some((p) => p.replace(/\D/g, '').includes(qDigits))
    })
  }, [items, search, reason, author, dueDate, focus, today])

  /** Ro'yxat ko'rinishi — shoshilinchlik bo'yicha saralangan. */
  const sorted = useMemo(
    () =>
      [...visible].sort((a, b) => {
        const ra = URGENCY[bucketOf(a.status, a.dueDate, today)] ?? 9
        const rb = URGENCY[bucketOf(b.status, b.dueDate, today)] ?? 9
        if (ra !== rb) return ra - rb
        if (a.dueDate !== b.dueDate) return (a.dueDate || '9999').localeCompare(b.dueDate || '9999')
        return b.createdAt.localeCompare(a.createdAt)
      }),
    [visible, today],
  )

  /** Fokus tanlanganda MUDDAT ustunlari ham toraytiriladi (bo'sh ustunlar chalg'itmasin). */
  const columns = useMemo(() => {
    if (groupBy === 'status') {
      // Ustunlar SERVERDAN; javob bo'sh bo'lsa (eski backend/xato) zaxira ro'yxat.
      return stages.length > 0 ? stageColumns(stages) : STATUS_COLUMNS
    }
    if (!focus) return DUE_COLUMNS
    return DUE_COLUMNS.filter((c) => (focus === 'todo' ? isTodo(c.key as never) : c.key === focus))
  }, [groupBy, focus, stages])

  /**
   * Taxtada HAQIQATAN chiziladiganlar.
   *
   * ⚠️ "Muddat" rejimida yakuniy (done/failed) talabning ustuni YO'Q (`bucketOf` bo'sh qaytaradi),
   * "Hammasi" qamrovida esa ular ro'yxatga tushadi. Ularni shu yerda chiqarib tashlamasak,
   * pastdagi sanoq chizilganidan KO'P ko'rsatib, "talab yo'qolgan"dek tuyulardi.
   */
  const boardItems = useMemo(() => {
    const keys = new Set(columns.map((c) => c.key))
    return visible.filter((r) =>
      keys.has(groupBy === 'status' ? stageKeyOf(r, keys) : bucketOf(r.status, r.dueDate, today)))
  }, [visible, columns, groupBy, today])

  /** Ekranda ko'rinadiganlar — bo'sh holat va sanoq AYNAN shundan hisoblanadi. */
  const shown = view === 'board' ? boardItems : sorted

  const filterCount =
    (search.trim() ? 1 : 0) + (reason !== 'all' ? 1 : 0) + (author !== 'all' ? 1 : 0) +
    (focus ? 1 : 0) + (dueDate ? 1 : 0)

  const clearFilters = () => {
    setSearch('')
    setReason('all')
    setAuthor('all')
    setFocus('')
    setDueDate('')
  }

  /* ---------- Amallar ---------- */
  const afterChange = async (updated?: ContactRequestItem) => {
    if (updated && detail?.id === updated.id) setDetail(await getContactRequest(updated.id))
    await load()
  }

  const openAttempt = (r: ContactRequestItem) => {
    setPreset({})
    setAttemptFor(r)
  }

  /**
   * Karta boshqa ustunga SUDRAB tashlandi.
   *
   * <p>Ikki xil holat bor va farqi PRINSIPIAL:</p>
   * <ul>
   *   <li><b>Bazaviy bosqich BIR XIL</b> — bu bog'lanish emas, taxta ichidagi siljish.
   *       Darhol saqlanadi (server uni tarixga izoh sifatida yozadi), oyna so'ralmaydi.</li>
   *   <li><b>Bosqich O'ZGARADI</b> — "Bog'lanildi" oynasi keyingi qadam va ustun oldindan
   *       tanlangan holda ochiladi: har o'tish natija va javob bilan yozilishi SHART.</li>
   * </ul>
   */
  const handleDrop = async (intent: DropIntent) => {
    if (!intent.sameStatus) {
      setPreset({ nextStatus: intent.nextStatus, dueDate: intent.dueDate, stageId: intent.stageId })
      setAttemptFor(intent.request)
      return
    }
    if (!intent.stageId) return
    const id = intent.request.id
    const before = intent.request.stageId ?? ''
    // Optimistik: karta darhol yangi ustunda ko'rinadi, xato bo'lsa joyiga qaytadi.
    setItems((prev) => prev.map((x) => (x.id === id ? { ...x, stageId: intent.stageId } : x)))
    try {
      await moveContactStage(id, intent.stageId)
    } catch (e) {
      setItems((prev) => prev.map((x) => (x.id === id ? { ...x, stageId: before } : x)))
      setError(apiErrorMessage(e, "Ustunni o'zgartirib bo'lmadi"))
    }
  }

  /* ---------- Ustun (kanban) CRUD ---------- */

  const submitStage = async (values: ContactStagePayload) => {
    if (stageBusy) return
    setStageBusy(true)
    try {
      if (editingStage) await updateContactStage(editingStage.id, values)
      else await createContactStage(values)
      setStageFormOpen(false)
      setEditingStage(null)
      setStages(await getContactStages())
      setError('')
    } catch (e) {
      setError(apiErrorMessage(e, "Ustunni saqlab bo'lmadi"))
    } finally {
      setStageBusy(false)
    }
  }

  const removeStage = async (col: BoardColumn) => {
    const stage = col.stage
    if (!stage) return
    // Server ham tekshiradi, lekin sababni DARHOL aytamiz — bosib ko'rib xato olishdan yaxshi.
    if (stage.isSystem) {
      alert("Tizim ustunini o'chirib bo'lmaydi — u shu bosqichdagi kartalar uchun doimiy joy.")
      return
    }
    if (stage.count > 0) {
      alert(`Bu ustunda ${stage.count} ta talab bor. Avval ularni boshqa ustunga ko'chiring.`)
      return
    }
    if (!confirm(`"${stage.title}" ustunini o'chirasizmi?`)) return
    try {
      await deleteContactStage(stage.id)
      setStages(await getContactStages())
    } catch (e) {
      setError(apiErrorMessage(e, "Ustunni o'chirib bo'lmadi"))
    }
  }

  const moveStageColumn = (col: BoardColumn, dir: -1 | 1) => {
    setStages((prev) => {
      const idx = prev.findIndex((s) => s.id === col.key)
      const j = idx + dir
      if (idx < 0 || j < 0 || j >= prev.length) return prev
      const next = [...prev]
      ;[next[idx], next[j]] = [next[j], next[idx]]
      reorderContactStages(next.map((s) => s.id)).catch((e) =>
        setError(apiErrorMessage(e, "Tartibni saqlab bo'lmadi")))
      return next
    })
  }

  const openDetail = async (r: ContactRequestItem) => {
    try {
      setDetail(await getContactRequest(r.id))
    } catch (e) {
      setError(apiErrorMessage(e, "Tarixni yuklab bo'lmadi"))
    }
  }

  const actions: ContactCardActions = {
    canWrite,
    canDelete,
    onAttempt: openAttempt,
    onDetail: (r) => void openDetail(r),
    onNote: (r) => { setNoteFor(r); setNoteText('') },
    onReopen: async (r) => {
      try {
        await reopenContactRequest(r.id)
        await afterChange()
      } catch (e) {
        setError(apiErrorMessage(e, "Qayta ochib bo'lmadi"))
      }
    },
    onDelete: async (r) => {
      // Tasdiq oddiy `confirm` bilan (loyihadagi boshqa joylar kabi).
      if (!confirm(`"${r.studentName}" talabini o'chirasizmi?`)) return
      try {
        await deleteContactRequest(r.id)
        await afterChange()
      } catch (e) {
        setError(apiErrorMessage(e, "O'chirib bo'lmadi"))
      }
    },
  }

  return (
    <div>
      <PageHeader
        title="Bog'lanish kerak"
        sub="Kim bilan bog'lanish kerak, nima deyildi va keyingi qadam — kartani sudrang yoki ustiga bosing"
        actions={
          <Button variant="secondary" onClick={() => void load()} disabled={loading}>
            <RefreshCw className={cn('h-4 w-4', loading && 'animate-spin')} /> Yangilash
          </Button>
        }
      />

      <div className="mb-4 flex gap-1 border-b border-slate-200">
        <button type="button" className={cn('tab', tab === 'navbat' && 'active')} onClick={() => setTab('navbat')}>
          <PhoneCall className="mr-1 inline h-3.5 w-3.5" /> Navbat
          {openCount > 0 && <span className="ml-1.5 text-xs text-slate-400">({openCount})</span>}
        </button>
        <button type="button" className={cn('tab', tab === 'hisobot' && 'active')} onClick={() => setTab('hisobot')}>
          <History className="mr-1 inline h-3.5 w-3.5" /> Hisobot
        </button>
      </div>

      {error && <p className="mb-3 rounded-lg bg-red-50 px-3 py-2 text-sm text-red-600">{error}</p>}

      {tab === 'hisobot' ? (
        <ContactStatsPanel />
      ) : (
        <div className="space-y-4">
          {/* ====== BUGUNGI ISH — sahifadagi eng katta raqamlar, ayni paytda fokus tugmalari ====== */}
          {meta.due && (
            <div className="grid gap-3 sm:grid-cols-2 lg:grid-cols-4">
              {FOCUS_TILES.map((t) => {
                const Icon = t.icon
                const value = meta.due![t.key as keyof typeof meta.due] as number
                const active = focus === t.key
                return (
                  <button
                    key={t.key}
                    type="button"
                    title={t.hint}
                    onClick={() => setFocus(active ? '' : t.key)}
                    className={cn(
                      'flex items-center gap-3 rounded-xl border p-4 text-left shadow-[var(--shadow-1)] transition-all',
                      active
                        ? 'border-brand-500 bg-brand-50 ring-1 ring-brand-200'
                        : value > 0
                          ? `${t.ring} hover:shadow-[var(--shadow-2)]`
                          : 'border-slate-200 bg-white hover:border-slate-300',
                    )}
                  >
                    <span className={cn('grid h-9 w-9 shrink-0 place-items-center rounded-lg', value > 0 ? t.iconCls : 'bg-slate-100 text-slate-400')}>
                      <Icon className="h-4.5 w-4.5" />
                    </span>
                    <span className="min-w-0">
                      <span className={cn('block font-mono text-[26px] font-bold leading-none', value === 0 ? 'text-slate-300' : t.text)}>
                        {value}
                      </span>
                      <span className="mt-1 block truncate text-xs font-semibold text-slate-500">{t.label}</span>
                    </span>
                  </button>
                )
              })}
            </div>
          )}

          {/* ====== ASBOBLAR PANELI — bitta qatorda, hamma filtr shu yerda ====== */}
          <div className="sticky top-0 z-20 -mx-1 rounded-xl border border-slate-200 bg-white/95 px-3 py-2.5 shadow-[var(--shadow-1)] backdrop-blur">
            <div className="flex flex-wrap items-center gap-2">
              {/* Qidiruv — JONLI (Enter bosish shart emas) */}
              <div className="flex items-center gap-2 rounded-lg border border-slate-200 px-2.5 py-1.5 focus-within:border-brand-400">
                <Search className="h-4 w-4 shrink-0 text-slate-400" />
                <input
                  value={search}
                  onChange={(e) => setSearch(e.target.value)}
                  placeholder="Ism, telefon, sabab, javob..."
                  className="w-52 border-0 bg-transparent text-sm text-slate-700 outline-none placeholder:text-slate-400"
                />
                {search && (
                  <button type="button" title="Tozalash" onClick={() => setSearch('')} className="text-slate-400 hover:text-slate-600">
                    <X className="h-3.5 w-3.5" />
                  </button>
                )}
              </div>

              <select
                value={reason}
                onChange={(e) => setReason(e.target.value)}
                title="Sabab bo'yicha"
                className="rounded-lg border border-slate-200 bg-white px-2.5 py-2 text-sm text-slate-700 outline-none focus:border-brand-400"
              >
                <option value="all">Barcha sabablar</option>
                <option value="__none__">— sababsiz —</option>
                {reasons.map((r) => (
                  <option key={r.id} value={r.id}>{r.label}</option>
                ))}
              </select>

              {authors.length > 1 && (
                <select
                  value={author}
                  onChange={(e) => setAuthor(e.target.value)}
                  title="Talabni kim yuborgan"
                  className="rounded-lg border border-slate-200 bg-white px-2.5 py-2 text-sm text-slate-700 outline-none focus:border-brand-400"
                >
                  <option value="all">Kim yuborgan: hammasi</option>
                  {authors.map((a) => (
                    <option key={a} value={a}>{a}</option>
                  ))}
                </select>
              )}

              <Seg
                value={scope}
                onChange={(v) => setScope(v)}
                options={[
                  { key: 'open', label: `Ochiqlar${openCount ? ` (${openCount})` : ''}` },
                  { key: 'all', label: 'Hammasi' },
                ]}
              />

              <span className="mx-0.5 hidden h-6 w-px bg-slate-200 sm:block" />

              <Seg
                value={groupBy}
                onChange={pickGroupBy}
                title="Ustunlar nima bo'yicha bo'linadi"
                options={[
                  { key: 'due', label: 'Muddat', icon: CalendarClock },
                  { key: 'status', label: 'Bosqich', icon: Columns3 },
                ]}
              />

              <Seg
                value={view}
                onChange={pickView}
                options={[
                  { key: 'board', label: 'Taxta', icon: Columns3 },
                  { key: 'list', label: "Ro'yxat", icon: LayoutGrid },
                ]}
              />

              <button
                type="button"
                onClick={() => setCalOpen((v) => !v)}
                title="Oylik kunlik reja"
                className={cn(
                  'inline-flex items-center gap-1.5 rounded-lg border px-2.5 py-2 text-[13px] font-semibold transition-colors',
                  calOpen || dueDate
                    ? 'border-brand-500 bg-brand-50 text-brand-700'
                    : 'border-slate-200 bg-white text-slate-600 hover:bg-slate-50',
                )}
              >
                <CalendarDays className="h-4 w-4" />
                {dueDate ? dayLabel(dueDate) : 'Kalendar'}
              </button>

              {filterCount > 0 && (
                <button
                  type="button"
                  onClick={clearFilters}
                  className="ml-auto inline-flex items-center gap-1.5 rounded-lg border border-rose-200 bg-rose-50 px-2.5 py-2 text-[13px] font-semibold text-rose-600 transition-colors hover:bg-rose-100"
                >
                  <Filter className="h-3.5 w-3.5" /> Filtrni tozalash ({filterCount})
                </button>
              )}
            </div>
          </div>

          {/* OYLIK KALENDAR — yig'iladi, chunki taxtaning O'ZI "qachon" savoliga javob beradi */}
          {calOpen && (
            <Card
              title={
                <span className="inline-flex items-center gap-2">
                  <CalendarDays className="h-4 w-4 text-slate-400" /> Kunlik reja
                </span>
              }
              sub="Kunni bosib o'sha kunga rejalashtirilganlarni ko'ring. Oyni strelkalar bilan almashtiring."
              actions={
                dueDate && (
                  <Button variant="ghost" onClick={() => setDueDate('')}>
                    <X className="h-3.5 w-3.5" /> Kun filtrini olib tashlash
                  </Button>
                )
              }
            >
              <MonthDayStrip
                month={month}
                onMonthChange={(m) => {
                  setMonth(m)
                  // Boshqa oyga o'tilganda o'tgan oyning tanlangan kuni filtrda qolib ketmasin.
                  if (dueDate && !dueDate.startsWith(m)) setDueDate('')
                }}
                selected={dueDate}
                onSelect={(d) => setDueDate(d === dueDate ? '' : d)}
                counts={Object.fromEntries((meta.days ?? []).map((d) => [d.date, d.count]))}
                // BUGUNGI katak "bugun qilish kerak" ni ko'rsatadi: muddati o'tgan va sanasizlar
                // ham kiradi — operator kechagi ishni ko'rmay qolmasin.
                todayCount={meta.due?.todo}
                hint="Bugungi katak muddati o'tgan va sana belgilanmagan talablarni ham qamraydi."
              />
            </Card>
          )}

          {/* ====== NAVBAT ====== */}
          {loading ? (
            <Loader label="Yuklanmoqda..." />
          ) : shown.length === 0 ? (
            <Card>
              <div className="py-12 text-center">
                <span className="mx-auto mb-3 grid h-12 w-12 place-items-center rounded-full bg-emerald-50 text-emerald-500">
                  <PhoneCall className="h-6 w-6" />
                </span>
                <p className="text-sm font-semibold text-slate-600">
                  {filterCount > 0 ? 'Bu filtrda hech narsa topilmadi' : "Navbat bo'sh"}
                </p>
                <p className="mt-1 text-xs text-slate-400">
                  {filterCount > 0
                    ? "Filtrni tozalab ko'ring."
                    : "Bog'lanish kerak bo'lgan o'quvchi yo'q — hammasi bilan bog'lanilgan."}
                </p>
                {filterCount > 0 && (
                  <Button variant="secondary" className="mt-3" onClick={clearFilters}>
                    Filtrni tozalash
                  </Button>
                )}
              </div>
            </Card>
          ) : view === 'board' ? (
            <ContactBoard
              columns={columns}
              items={boardItems}
              groupBy={groupBy}
              today={today}
              actions={actions}
              onCardClick={(r) => void openDetail(r)}
              onDrop={(i) => void handleDrop(i)}
              // Ustunlarni boshqarish FAQAT "Bosqich" rejimida: muddat ustunlari sana bo'yicha
              // HISOBLANADI, ular ma'lumot emas — tahrirlash mumkin bo'lgan narsa emas.
              onAddColumn={groupBy === 'status' && canWrite ? () => {
                setEditingStage(null)
                setStageFormOpen(true)
              } : undefined}
              columnAdmin={groupBy === 'status' && canWrite ? {
                onEdit: (col) => {
                  if (!col.stage) return
                  setEditingStage(col.stage)
                  setStageFormOpen(true)
                },
                onDelete: (col) => void removeStage(col),
                onMove: moveStageColumn,
              } : undefined}
            />
          ) : (
            <div className="grid gap-3 sm:grid-cols-2 xl:grid-cols-3">
              {sorted.map((r) => (
                <div key={r.id} onClick={() => void openDetail(r)}>
                  <ContactCardContent r={r} today={today} actions={actions} />
                </div>
              ))}
            </div>
          )}

          {/* Chegaraga yetildi — jimgina qirqib qo'ymaymiz */}
          {!loading && items.length >= MAX_ROWS && (
            <p className="text-center text-xs text-slate-400">
              Eng so'nggi {MAX_ROWS} ta talab ko'rsatildi — qolganini ko'rish uchun filtrdan foydalaning.
            </p>
          )}

          {!loading && shown.length > 0 && (
            <p className="text-center text-xs text-slate-400">
              {shown.length} ta talab
              {shown.length !== items.length && ` (jami ${items.length} tadan)`}
            </p>
          )}
        </div>
      )}

      <ContactAttemptModal
        open={!!attemptFor}
        request={attemptFor}
        meta={meta}
        presetNextStatus={preset.nextStatus}
        presetDueDate={preset.dueDate}
        presetStageId={preset.stageId}
        onClose={() => setAttemptFor(null)}
        onSaved={(u) => void afterChange(u)}
      />

      <ContactStageFormModal
        open={stageFormOpen}
        initial={editingStage}
        meta={meta}
        busy={stageBusy}
        onClose={() => {
          setStageFormOpen(false)
          setEditingStage(null)
        }}
        onSubmit={(v) => void submitStage(v)}
      />

      {/* TARIX — "kim qaysi bosqichga oldi, natijasi qanday bo'ldi" */}
      <Modal open={!!detail} onClose={() => setDetail(null)} size="md" title="Bog'lanish tarixi">
        {detail && (
          <div className="space-y-3">
            <div className="rounded-lg bg-slate-50 px-3 py-2 text-sm">
              <p className="font-semibold text-slate-700">{detail.studentName}</p>
              <p className="mt-0.5 text-slate-500">
                {detail.reasonLabel || '— sababsiz —'} · {detail.statusLabel}
              </p>
              <p className="mt-0.5 text-xs text-slate-400">
                Ochgan: {detail.createdBy} · {formatDateTime(detail.createdAt)}
              </p>
              {canWrite && (detail.status === 'new' || detail.status === 'callback') && (
                <Button
                  className="mt-2"
                  onClick={() => {
                    const r = detail
                    setDetail(null)
                    openAttempt(r)
                  }}
                >
                  <PhoneCall className="h-4 w-4" /> Bog'lanildi
                </Button>
              )}
            </div>
            <ul className="space-y-2">
              {(detail.history ?? []).map((h) => (
                <li key={h.id} className="rounded-lg border border-slate-100 px-3 py-2">
                  <div className="flex flex-wrap items-center gap-2 text-sm">
                    <span className="font-medium text-slate-700">{eventTitle(h.type)}</span>
                    {h.resultLabel && (
                      <span className="rounded-md bg-slate-100 px-2 py-0.5 text-xs text-slate-600">{h.resultLabel}</span>
                    )}
                    {h.nextStatusLabel && (
                      <span className="text-xs text-slate-400">
                        → {h.nextStatusLabel}
                        {h.dueDate && ` (${formatDate(h.dueDate)})`}
                      </span>
                    )}
                  </div>
                  {h.response && <p className="mt-1 text-sm text-slate-600">{h.response}</p>}
                  <p className="mt-0.5 text-xs text-slate-400">
                    {formatDateTime(h.createdAt)} · {h.actorName || 'Tizim'}
                  </p>
                </li>
              ))}
              {(detail.history ?? []).length === 0 && (
                <li className="py-4 text-center text-sm text-slate-400">Hodisa yo'q</li>
              )}
            </ul>
          </div>
        )}
      </Modal>

      {/* Izoh qo'shish (bosqich o'zgarmaydi) */}
      <Modal
        open={!!noteFor}
        onClose={() => setNoteFor(null)}
        size="sm"
        title="Izoh qo'shish"
        footer={
          <>
            <Button variant="secondary" onClick={() => setNoteFor(null)}>Bekor</Button>
            <Button
              disabled={!noteText.trim()}
              onClick={async () => {
                if (!noteFor) return
                try {
                  await addContactNote(noteFor.id, noteText.trim())
                  setNoteFor(null)
                  await afterChange()
                } catch (e) {
                  setError(apiErrorMessage(e, "Izohni saqlab bo'lmadi"))
                }
              }}
            >
              Saqlash
            </Button>
          </>
        }
      >
        <textarea
          value={noteText}
          onChange={(e) => setNoteText(e.target.value)}
          rows={4}
          maxLength={2000}
          placeholder="Masalan: ota-onasi o'zi kelib ketdi"
          className="w-full rounded-lg border border-slate-200 px-3 py-2 text-sm text-slate-700 outline-none focus:border-brand-400"
        />
        <p className="mt-1 text-xs text-slate-400">Bosqich o'zgarmaydi — faqat tarixga yoziladi.</p>
      </Modal>
    </div>
  )
}

function eventTitle(type: string): string {
  switch (type) {
    case 'created': return 'Talab ochildi'
    case 'contact': return "Bog'lanildi"
    case 'note': return 'Izoh'
    case 'reopen': return 'Qayta ochildi'
    default: return type
  }
}

/** Segmentli tanlov (qamrov · guruhlash · ko'rinish) — asboblar panelini bir qatorda ushlaydi. */
function Seg<T extends string>({
  value, onChange, options, title,
}: {
  value: T
  onChange: (v: T) => void
  options: { key: T; label: string; icon?: typeof Flame }[]
  title?: string
}) {
  return (
    <div title={title} className="inline-flex items-center gap-0.5 rounded-lg border border-slate-200 bg-slate-50 p-0.5">
      {options.map((o) => {
        const Icon = o.icon
        return (
          <button
            key={o.key}
            type="button"
            onClick={() => onChange(o.key)}
            className={cn(
              'inline-flex items-center gap-1.5 rounded-md px-2.5 py-1.5 text-[13px] font-semibold transition-colors',
              value === o.key ? 'bg-white text-brand-700 shadow-[var(--shadow-1)]' : 'text-slate-500 hover:text-slate-700',
            )}
          >
            {Icon && <Icon className="h-3.5 w-3.5" />}
            {o.label}
          </button>
        )
      })}
    </div>
  )
}
