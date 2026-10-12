import { useEffect, useMemo, useState } from 'react'
import { Link, useNavigate } from 'react-router-dom'
import {
  DndContext,
  DragOverlay,
  PointerSensor,
  useSensor,
  useSensors,
  type DragEndEvent,
  type DragStartEvent,
} from '@dnd-kit/core'
import { IconLayoutColumns, IconList, IconPlus, IconSearch } from '@tabler/icons-react'
import type { Lead, LeadSource, Stage } from '@/types'
import {
  createLead,
  getLeadCourses,
  getLeads,
  updateLeadStage,
  type LeadAnswersPayload,
} from '@/api/services/leads'
import { getLeadSources } from '@/api/services/leadSources'
import { LOST_COLUMN_ID, markLeadLost, restoreLead, type LeadLossState } from '@/api/services/leadLoss'
import {
  createStage,
  deleteStage,
  getStages,
  reorderStages,
  updateStage,
  type StagePayload,
} from '@/api/services/stages'
import { PhoneCallModal } from '@/components/PhoneCallModal'
import { DataTable, type DataColumn } from '@/components/ui/list/DataTable'
import { FilterGrid, FilterInput, FilterSelect } from '@/components/ui/list/FilterGrid'
import { FilterDateRange } from '@/components/ui/list/FilterDateRange'
import { ListToolbar } from '@/components/ui/list/ListToolbar'
import { PhoneChip } from '@/components/ui/list/PhoneChip'
import { TotalPill } from '@/components/ui/list/TotalPill'
import { ViewToggle } from '@/components/ui/list/ViewToggle'
import { TablePagination, usePagination } from '@/components/ui/TablePagination'
import { Loader } from '@/components/ui/Loader'
import { apiErrorMessage } from '@/lib/utils'
import { usePerm } from '@/lib/permissions'
import { LeadColumn } from './LeadColumn'
import { LeadCardContent } from './LeadCard'
import { LeadFormModal, type LeadFormValues } from './LeadFormModal'
import { StageFormModal } from './StageFormModal'
import { LostColumn } from './LostColumn'
import { LeadLostModal } from './LeadLostModal'
import { isLeadLost, leadCallNumbers, leadMatchesStatus, shortLeadId, tableDateTime } from './leadLabels'
import { useUrlFilters } from './useUrlFilters'

/**
 * LIDLAR → «Buyurtmalar ro'yxati» (edutizim `/orders/order-list/table` va `/kanban`).
 *
 * Standart ko'rinish — JADVAL (edutizimdagidek), `?view=kanban` — bosqichlar taxtasi. Filtrlar
 * ikkala ko'rinishga ham qo'llanadi va manzilda saqlanadi (`useUrlFilters`). Lid bosilsa — lid
 * SAHIFASI (`/admin/leads/:id`, edutizimdagi `/orders/info/:id`).
 *
 * ⚠️ Filtrlash KLIENTDA (avvalgidek): ro'yxat bitta so'rovda keladi, taxta ham shu ro'yxatdan
 * quriladi — server so'rovi har harfda yuborilmaydi.
 */

const FILTER_KEYS = [
  'q', 'from', 'to', 'day', 'stage', 'course', 'level', 'group', 'teacher', 'moderator',
  'status', 'source',
  // YO'QOTILGANLAR: `lost` — '' (hammasi) | 'hide' | 'only'; `reason` — yo'qotish sababi (matn).
  'lost', 'reason',
] as const

/** Select'dagi "bo'sh qiymatli" variant (manbasiz, mas'ulsiz, kurssiz). */
const NONE = '__none__'

type View = 'table' | 'kanban'

/** Yaratilgan kun "yyyy-MM-dd". */
const dayOf = (l: Lead) => (l.createdAt || '').slice(0, 10)

/** Takrorlanmas (id, nom) juftliklari — select variantlari uchun, nom bo'yicha tartiblangan. */
function uniquePairs(items: { id?: string | null; name?: string | null }[]): { id: string; name: string }[] {
  const m = new Map<string, string>()
  for (const it of items) if (it.id) m.set(it.id, it.name || it.id)
  return [...m.entries()].map(([id, name]) => ({ id, name })).sort((a, b) => a.name.localeCompare(b.name))
}

export function LeadsPage() {
  const { can } = usePerm()
  const navigate = useNavigate()
  const { values: f, set, clear, active: filtersActive, params } = useUrlFilters(FILTER_KEYS)
  const view: View = params.get('view') === 'kanban' ? 'kanban' : 'table'

  const [leads, setLeads] = useState<Lead[]>([])
  const [stages, setStages] = useState<Stage[]>([])
  const [loading, setLoading] = useState(true)
  /** Ro'yxat/ustunlar yuklanmasa — sababi ko'rinadi (aks holda "lid yo'q" deb o'ylanardi). */
  const [loadError, setLoadError] = useState<string | null>(null)
  const [activeId, setActiveId] = useState<string | null>(null)
  const [sources, setSources] = useState<LeadSource[]>([])
  const [courseNames, setCourseNames] = useState<string[]>([])

  // Filtr paneli: manzilda filtr bo'lsa (masalan "orqaga" qaytilganda) — ochiq holda boshlanadi.
  const [filtersOpen, setFiltersOpen] = useState(filtersActive)
  const [sortDir, setSortDir] = useState<'asc' | 'desc'>('desc')

  // modallar
  const [leadFormOpen, setLeadFormOpen] = useState(false)
  const [stageFormOpen, setStageFormOpen] = useState(false)
  const [editingStage, setEditingStage] = useState<Stage | null>(null)
  const [callLead, setCallLead] = useState<Lead | null>(null)
  /** "Yo'qotilgan" ustuniga tashlangan lid — sabab oynasi (holat jimgina o'zgarmaydi). */
  const [lostTarget, setLostTarget] = useState<Lead | null>(null)

  const sensors = useSensors(useSensor(PointerSensor, { activationConstraint: { distance: 6 } }))

  useEffect(() => {
    Promise.all([getLeads(), getStages()])
      .then(([l, s]) => {
        setLeads(l)
        setStages(s)
      })
      // Sahifaning ASOSIY ma'lumoti — xato yutilmaydi (`error-visibility.md` §4).
      .catch((err) => setLoadError(apiErrorMessage(err, "Lidlarni yuklab bo'lmadi")))
      .finally(() => setLoading(false))
    getLeadSources().then(setSources).catch(() => setSources([]))
    getLeadCourses().then(setCourseNames).catch(() => setCourseNames([]))
  }, [])

  /**
   * `?lead=<id>` — boshqa bo'limlardan (Formalar → Arizalar, Daraja testi natijalari) AYNAN shu
   * lidni ochish uchun eski havola. Endi lid o'z SAHIFASIDA ochiladi — manzil almashtiriladi
   * (`replace`: "orqaga" bosilganda yana shu yo'naltirishga tushib qolmasin).
   */
  useEffect(() => {
    const id = params.get('lead')
    if (id) navigate(`/admin/leads/${id}`, { replace: true })
  }, [params, navigate])

  const openLead = (lead: Lead) => navigate(`/admin/leads/${lead.id}`)
  const setView = (v: View) => set({ view: v === 'kanban' ? 'kanban' : '' })

  /* ---------- Karta drag-and-drop ---------- */
  const handleDragStart = (e: DragStartEvent) => setActiveId(String(e.active.id))

  /** Server qaytargan "yo'qotilgan" holatini ro'yxatdagi qatorga qo'llaydi (muvaffaqiyatdan KEYIN). */
  const applyLossState = (st: LeadLossState) =>
    setLeads((prev) =>
      prev.map((l) =>
        l.id === st.id
          ? {
              ...l,
              stage: st.stage,
              lostAt: st.lostAt,
              lostReasonId: st.lostReasonId,
              lostReason: st.lostReason,
              lostComment: st.lostComment,
              lostByName: st.lostByName,
              assigneeUserId: st.assigneeUserId,
              assigneeName: st.assigneeName,
            }
          : l,
      ),
    )

  const handleDragEnd = (e: DragEndEvent) => {
    setActiveId(null)
    const { active, over } = e
    if (!over) return
    const leadId = String(active.id)
    const newStage = String(over.id)
    const lead = leads.find((l) => l.id === leadId)
    if (!lead) return

    // "YO'QOTILGAN" ustuniga — sabab oynasi. Holat FAQAT oynada tasdiqlangach o'zgaradi.
    if (newStage === LOST_COLUMN_ID) {
      if (isLeadLost(lead)) return
      if (lead.convertedStudentId) {
        alert("O'quvchiga aylantirilgan lidni yo'qotilgan deb belgilab bo'lmaydi.")
        return
      }
      setLostTarget(lead)
      return
    }

    // Yo'qotilgan karta boshqa ustunga — TIKLASH (server bosqichni ham o'zgartiradi va tarixga yozadi).
    if (isLeadLost(lead)) {
      const title = stages.find((s) => s.id === newStage)?.title ?? ''
      if (!confirm(`"${lead.fullName}" lidini tiklab, «${title}» ustuniga o'tkazasizmi?`)) return
      restoreLead(lead.id, newStage)
        .then(applyLossState)
        .catch((err) => alert(apiErrorMessage(err, "Lidni tiklab bo'lmadi")))
      return
    }

    if (lead.stage === newStage) return
    setLeads((prev) => prev.map((l) => (l.id === leadId ? { ...l, stage: newStage } : l)))
    // Optimistik: xatoda eski ustun qaytariladi VA sabab ko'rsatiladi (`error-visibility.md` §3).
    updateLeadStage(leadId, newStage).catch((err) => {
      setLeads((prev) => prev.map((l) => (l.id === leadId ? { ...l, stage: lead.stage } : l)))
      alert(apiErrorMessage(err, "Lidni ko'chirib bo'lmadi"))
    })
  }

  /* ---------- Yangi lid ---------- */
  /**
   * ⚠️ Promise QAYTARADI va xatoni YUQORIGA (modalga) o'tkazadi: saqlash muvaffaqiyatsiz bo'lsa
   * oyna ochiq qoladi va xato ko'rinadi. Ro'yxat SERVER JAVOBIDAN KEYIN yangilanadi (optimistik
   * emas). Tahrirlash endi lid SAHIFASIDA (`LeadDetailPage`).
   */
  const handleLeadSubmit = async (
    values: LeadFormValues, answers: LeadAnswersPayload, opts?: { force?: boolean },
  ) => {
    const stageId = stages[0]?.id ?? 'new'
    // Shu raqamli lid bo'lsa server 409 qaytaradi — modal "Bu raqamli lid allaqachon bor" ni ko'rsatadi.
    const lead = await createLead(values, stageId, answers, opts?.force ?? false)
    setLeads((prev) => [lead, ...prev])
  }

  /* ---------- Ustun CRUD ---------- */
  const handleStageSubmit = (values: StagePayload) => {
    if (editingStage) {
      const id = editingStage.id
      const before = editingStage
      setStages((prev) => prev.map((s) => (s.id === id ? { ...s, ...values } : s)))
      // Ekran OPTIMISTIK yangilanadi: so'rov yiqilsa yangi nom/rang ekranda qolib, foydalanuvchi
      // saqlandi deb o'ylardi — shuning uchun eski holat qaytariladi va sabab ko'rsatiladi.
      updateStage(id, values).catch((err) => {
        setStages((prev) => prev.map((s) => (s.id === id ? before : s)))
        alert(apiErrorMessage(err, "Ustunni saqlab bo'lmadi"))
      })
    } else {
      // Xato jimgina yutilardi: ustun qo'shilmagani ham, sababi ham ko'rinmasdi.
      createStage(values)
        .then((stage) => setStages((prev) => [...prev, stage]))
        .catch((err) => alert(apiErrorMessage(err, "Ustun qo'shib bo'lmadi")))
    }
    setStageFormOpen(false)
    setEditingStage(null)
  }

  const handleStageDelete = (stage: Stage) => {
    // Yo'qotilgan lidlar SANALMAYDI: ular bu ustunda ko'rinmaydi (faqat "Yo'qotilgan" ustunida), ustun
    // o'chsa esa serverdagi yetim-tuzatish ularni birinchi bosqichga o'tkazadi — holati o'zgarmaydi.
    const count = leads.filter((l) => l.stage === stage.id && !isLeadLost(l)).length
    if (count > 0) {
      alert("Bu ustunda lidlar bor. Avval ularni boshqa ustunga ko'chiring.")
      return
    }
    if (stages.length <= 1) {
      alert("Kamida bitta ustun bo'lishi kerak.")
      return
    }
    if (!confirm(`"${stage.title}" ustunini o'chirasizmi?`)) return
    // Server rad etsa (masalan ustun band) sabab ko'rinsin — ilgari ustun shunchaki o'chmay qolardi.
    deleteStage(stage.id)
      .then(() => setStages((prev) => prev.filter((s) => s.id !== stage.id)))
      .catch((err) => alert(apiErrorMessage(err, "Ustunni o'chirib bo'lmadi")))
  }

  const handleStageMove = (id: string, dir: -1 | 1) => {
    const idx = stages.findIndex((s) => s.id === id)
    const j = idx + dir
    if (idx < 0 || j < 0 || j >= stages.length) return
    const before = stages
    const next = [...stages]
    ;[next[idx], next[j]] = [next[j], next[idx]]
    setStages(next)
    // ⚠️ So'rov `setStages` updater'i ICHIDAN CHIQARILDI: StrictMode updater'ni ikki marta
    // chaqiradi, ya'ni tartib serverga ikki marta ketardi. Xatoda eski tartib qaytariladi —
    // ekranda yolg'on natija qolmasin.
    reorderStages(next.map((s) => s.id)).catch((err) => {
      setStages(before)
      alert(apiErrorMessage(err, "Tartibni saqlab bo'lmadi"))
    })
  }

  /* ---------- Filtrlar ---------- */
  // Xavfsizlik tarmog'i: bosqichi mavjud ustunlardan biriga MOS KELMAYDIGAN lid (eski bo'sh "" yoki
  // o'chirilgan ustun — masalan daraja testidan bosqich yo'q paytda tushgan) ko'rinmay qolmasin —
  // birinchi ustunda ko'rsatamiz (sudrab to'g'ri ustunga o'tkazish mumkin).
  const stageIds = useMemo(() => new Set(stages.map((s) => s.id)), [stages])
  const stageOf = (l: Lead) => (stageIds.has(l.stage) ? l.stage : (stages[0]?.id ?? l.stage))

  // Qidiruv FAQAT lidlar ro'yxati ichida (serverga so'rov yubormaydi). Matnli maydonlar oddiy
  // "ichida bor" bo'yicha; telefonlar esa faqat RAQAMLAR bo'yicha solishtiriladi — shu sabab
  // "901234567" ham "+998 90 123 45 67" ni topadi.
  const q = f.q.trim().toLowerCase()
  const qDigits = q.replace(/\D/g, '')
  const matchesSearch = (l: Lead) => {
    if (!q) return true
    const text = [
      l.fullName, l.fatherFullName, l.motherFullName, l.source, l.interestSubject, l.note,
      l.teacherName, l.groupName, l.assigneeName,
      l.level, shortLeadId(l.id),
    ]
      .filter(Boolean)
      .join(' ')
      .toLowerCase()
    if (text.includes(q)) return true
    return (
      qDigits.length >= 3 &&
      [l.phone, l.fatherPhone, l.motherPhone].some((p) => (p || '').replace(/\D/g, '').includes(qDigits))
    )
  }

  /**
   * STATUS — «Buyurtmalar ro'yxati» JADVALIDA standart `lead` (2026-10-09): faqat haqiqiy buyurtmalar.
   * ⚠️ Ilgari jadval HAMMA lidni ko'rsatardi — sinov darsiga yozilgan lid «Buyurtmalar»da ham,
   * «Birinchi darsga yozilganlar»da ham, o'quvchiga aylantirilgani esa «Yangi o'quvchilar»da ham
   * turardi (mijoz: "oldingi bosqichdan o'chmayapti, ikkala bo'limda qolib ketyapti").
   * «Birinchi dars» a'zoligi — serverdagi `firstLessonAt` (`LeadFirstLesson.Pick`, ikkinchi ta'rif YO'Q);
   * bosh sahifadagi «Buyurtmalar» sanog'i ham AYNAN shu (`DashboardSummary.OpenLeads`).
   * Kanban — butun voronka (bosqichlar), shuning uchun u yerda standart "hammasi".
   */
  const status = f.status || (view === 'table' ? 'lead' : 'all')
  const matchesStatus = (l: Lead) => leadMatchesStatus(l, status)

  /** Status'dan TASHQARI barcha filtrlar — jadval ostidagi "yashirilganlar" sanog'i uchun ham. */
  const matchesOthers = (l: Lead) => {
    const d = dayOf(l)
    if ((f.from || f.to || f.day) && !d) return false
    if (f.from && d < f.from) return false
    if (f.to && d > f.to) return false
    if (f.day && d !== f.day) return false
    if (f.stage && stageOf(l) !== f.stage) return false
    if (f.course && (f.course === NONE ? !!l.interestSubject : l.interestSubject !== f.course)) return false
    if (f.level && (l.level ?? '') !== f.level) return false
    if (f.group && l.groupId !== f.group) return false
    if (f.teacher && l.teacherId !== f.teacher) return false
    if (f.moderator && (f.moderator === NONE ? !!l.assigneeUserId : l.assigneeUserId !== f.moderator)) return false
    if (f.source && (f.source === NONE ? !!l.source : l.source !== f.source)) return false
    // YO'QOTILGANLAR: yashirish / faqat ular; sabab tanlansa — faqat o'sha sabab bilan yo'qotilganlar.
    if (f.lost === 'hide' && isLeadLost(l)) return false
    if (f.lost === 'only' && !isLeadLost(l)) return false
    if (f.reason && (!isLeadLost(l) || (l.lostReason ?? '') !== f.reason)) return false
    return matchesSearch(l)
  }
  const matches = (l: Lead) => matchesStatus(l) && matchesOthers(l)
  const visibleLeads = leads.filter(matches)
  /** Standart (buyurtmalar) jadvalda ko'rinmay qolganlar — jimgina "yo'qolgan" bo'lib tuyulmasin. */
  const hiddenByStatus = { first: 0, students: 0 }
  if (view === 'table' && status === 'lead') {
    for (const l of leads) {
      if (!matchesOthers(l)) continue
      if (l.convertedStudentId) hiddenByStatus.students++
      else if (l.firstLessonAt) hiddenByStatus.first++
    }
  }

  // Select variantlari — ma'lumotdan (faqat haqiqatan uchraydigan qiymatlar).
  const courseOptions = useMemo(() => {
    const s = new Set(courseNames)
    for (const l of leads) if (l.interestSubject) s.add(l.interestSubject)
    return [...s].sort((a, b) => a.localeCompare(b))
  }, [leads, courseNames])
  const levelOptions = useMemo(
    () => [...new Set(leads.map((l) => l.level).filter((x): x is string => !!x))].sort(),
    [leads],
  )
  const groupOptions = useMemo(() => uniquePairs(leads.map((l) => ({ id: l.groupId, name: l.groupName }))), [leads])
  const teacherOptions = useMemo(() => uniquePairs(leads.map((l) => ({ id: l.teacherId, name: l.teacherName }))), [leads])
  const moderatorOptions = useMemo(
    () => uniquePairs(leads.map((l) => ({ id: l.assigneeUserId, name: l.assigneeName }))),
    [leads],
  )
  const sourceOptions = useMemo(() => {
    const names = sources.map((s) => s.name)
    for (const l of leads) if (l.source && !names.includes(l.source)) names.push(l.source)
    return names
  }, [sources, leads])
  // Yo'qotish sabablari — lidlardagi SNAPSHOT matnlardan (katalogdan o'chirilgan sabab ham qoladi).
  const lostReasonOptions = useMemo(
    () => [...new Set(leads.filter(isLeadLost).map((l) => l.lostReason || '').filter(Boolean))].sort(),
    [leads],
  )

  /* ---------- Jadval ---------- */
  const sorted = useMemo(
    () =>
      [...visibleLeads].sort((a, b) => {
        const c = (a.createdAt || '').localeCompare(b.createdAt || '')
        return sortDir === 'asc' ? c : -c
      }),
    [visibleLeads, sortDir],
  )
  const pg = usePagination(sorted)

  const columns: DataColumn<Lead>[] = [
    {
      key: 'id',
      header: 'ID',
      width: 80,
      render: (l) => <span className="font-mono text-[12px] text-[#555]" title={l.id}>{shortLeadId(l.id)}</span>,
    },
    {
      key: 'name',
      header: "O'quvchini ismi",
      render: (l) => (
        <span className="inline-flex max-w-[300px] items-center gap-1.5">
          <button
            type="button"
            onClick={() => openLead(l)}
            className="max-w-[220px] truncate text-left font-medium text-black hover:text-brand-600 hover:underline"
            title={l.fullName}
          >
            {l.fullName || '—'}
          </button>
          {isLeadLost(l) && (
            <span
              className="shrink-0 rounded bg-rose-50 px-1.5 py-px text-[10.5px] font-semibold text-rose-700"
              title={`Yo'qotilgan: ${l.lostReason || 'sababsiz'}${l.lostComment ? ` — ${l.lostComment}` : ''}`}
            >
              Yo'qotilgan
            </span>
          )}
        </span>
      ),
    },
    {
      key: 'phone',
      header: 'Telefon raqam',
      render: (l) => {
        const phone = l.phone || l.fatherPhone || l.motherPhone
        return <PhoneChip phone={phone} onCall={() => setCallLead(l)} />
      },
    },
    {
      key: 'createdAt',
      header: 'Yaratilgan sanasi',
      sortable: true,
      render: (l) => <span className="whitespace-nowrap">{tableDateTime(l.createdAt)}</span>,
    },
    { key: 'teacher', header: "O'qituvchi", render: (l) => <span className="whitespace-nowrap">{l.teacherName || ''}</span> },
    { key: 'course', header: 'Kurs', render: (l) => <span className="whitespace-nowrap">{l.interestSubject || ''}</span> },
    { key: 'level', header: 'Kurs darajasi', render: (l) => <span className="whitespace-nowrap">{l.level || ''}</span> },
    { key: 'moderator', header: 'Moderator', render: (l) => <span className="whitespace-nowrap">{l.assigneeName || ''}</span> },
    {
      key: 'note',
      header: 'Izoh',
      render: (l) => (
        <span className="block max-w-[260px] truncate text-[#555]" title={l.note || undefined}>
          {l.note || ''}
        </span>
      ),
    },
  ]

  /* ---------- Ko'rinish ---------- */
  // ⚠️ Memo: `PhoneCallModal` raqamlar massivi o'zgarganda holatini tozalaydi — har renderda
  // yangi massiv berilsa oyna ochiq turganda tanlov o'z-o'zidan bekor bo'lib qolardi.
  const callNumbers = useMemo(() => (callLead ? leadCallNumbers(callLead) : []), [callLead])
  const canCreate = can('leads.list', 'create')

  const viewToggle = (
    <ViewToggle<View>
      value={view}
      onChange={setView}
      options={[
        { value: 'table', label: "Jadval ko'rinishi", icon: <IconList className="h-5 w-5" /> },
        { value: 'kanban', label: "Kanban ko'rinishi", icon: <IconLayoutColumns className="h-5 w-5" /> },
      ]}
    />
  )

  return (
    <div>
      {loadError && (
        <p className="mb-3 rounded-lg border border-red-200 bg-red-50 px-3 py-2 text-sm text-red-700">{loadError}</p>
      )}
      <ListToolbar
        left={
          <>
            {viewToggle}
            {/* Kanbanda edutizimdagidek keng "Qidirish" maydoni asboblar qatorida. */}
            {view === 'kanban' && (
              <div className="relative min-w-[200px] flex-1">
                <IconSearch className="pointer-events-none absolute left-2.5 top-1/2 h-4 w-4 -translate-y-1/2 text-[#6b7280]" />
                <input
                  value={f.q}
                  onChange={(e) => set({ q: e.target.value })}
                  placeholder="Qidirish"
                  className="h-[34px] w-full rounded-lg border border-[#dbe0e6] bg-white pl-8 pr-3 text-[13px] text-black outline-none transition-colors placeholder:text-[#6b7280] focus:border-brand-600"
                />
              </div>
            )}
          </>
        }
        filtersOpen={filtersOpen}
        onToggleFilters={() => setFiltersOpen((o) => !o)}
        addLabel={canCreate ? (view === 'kanban' ? "Qo'shish" : "Buyurtma qo'shish") : undefined}
        onAdd={() => setLeadFormOpen(true)}
      />

      {filtersOpen && (
        <FilterGrid>
          {view === 'table' && (
            <FilterInput search placeholder="Qidiruv" value={f.q} onChange={(e) => set({ q: e.target.value })} />
          )}
          <FilterDateRange
            title="Yaratilgan sana (oraliq)"
            from={f.from}
            to={f.to}
            onChange={(from, to) => set({ from, to })}
          />
          <FilterInput
            type="date"
            title="Yaratilgan kun"
            aria-label="Yaratilgan kun"
            value={f.day}
            onChange={(e) => set({ day: e.target.value })}
            className={f.day ? '' : 'text-[#6b7280]'}
          />
          <FilterSelect placeholder="Holatlar" value={f.stage} onChange={(e) => set({ stage: e.target.value })}>
            {stages.map((s) => (
              <option key={s.id} value={s.id}>
                {s.title}
              </option>
            ))}
          </FilterSelect>
          <FilterSelect placeholder="Kurs" value={f.course} onChange={(e) => set({ course: e.target.value })}>
            {courseOptions.map((c) => (
              <option key={c} value={c}>
                {c}
              </option>
            ))}
            <option value={NONE}>Ko'rsatilmagan</option>
          </FilterSelect>
          <FilterSelect placeholder="Kurs darajasi" value={f.level} onChange={(e) => set({ level: e.target.value })}>
            {levelOptions.map((lv) => (
              <option key={lv} value={lv}>
                {lv}
              </option>
            ))}
          </FilterSelect>
          <FilterSelect placeholder="Guruh" value={f.group} onChange={(e) => set({ group: e.target.value })}>
            {groupOptions.map((g) => (
              <option key={g.id} value={g.id}>
                {g.name}
              </option>
            ))}
          </FilterSelect>
          <FilterSelect placeholder="O'qituvchi" value={f.teacher} onChange={(e) => set({ teacher: e.target.value })}>
            {teacherOptions.map((t) => (
              <option key={t.id} value={t.id}>
                {t.name}
              </option>
            ))}
          </FilterSelect>
          <FilterSelect placeholder="Moderator" value={f.moderator} onChange={(e) => set({ moderator: e.target.value })}>
            {moderatorOptions.map((m) => (
              <option key={m.id} value={m.id}>
                {m.name}
              </option>
            ))}
            <option value={NONE}>Biriktirilmagan</option>
          </FilterSelect>
          <FilterSelect placeholder="Status" value={f.status} onChange={(e) => set({ status: e.target.value })}>
            <option value="lead">Buyurtma (sinovga yozilmagan)</option>
            <option value="first">Birinchi darsga yozilgan</option>
            <option value="student">O'quvchiga aylantirilgan</option>
            <option value="all">Hammasi</option>
          </FilterSelect>
          <FilterSelect placeholder="Manba" value={f.source} onChange={(e) => set({ source: e.target.value })}>
            {sourceOptions.map((s) => (
              <option key={s} value={s}>
                {s}
              </option>
            ))}
            <option value={NONE}>Noma'lum</option>
          </FilterSelect>
          <FilterSelect placeholder="Yo'qotilganlar" value={f.lost} onChange={(e) => set({ lost: e.target.value })}>
            <option value="hide">Yo'qotilganlarni yashirish</option>
            <option value="only">Faqat yo'qotilganlar</option>
          </FilterSelect>
          <FilterSelect placeholder="Yo'qotish sababi" value={f.reason} onChange={(e) => set({ reason: e.target.value })}>
            {lostReasonOptions.map((r) => (
              <option key={r} value={r}>
                {r}
              </option>
            ))}
          </FilterSelect>
          {filtersActive && (
            <button
              type="button"
              onClick={clear}
              className="h-[34px] justify-self-start rounded-lg px-3 text-[13px] font-medium text-brand-600 transition-colors hover:bg-brand-600/10"
            >
              Filtrlarni tozalash
            </button>
          )}
        </FilterGrid>
      )}

      {view === 'table' ? (
        <>
          <div className="flex items-center gap-2">
            <div className="ml-auto">
              <TotalPill total={visibleLeads.length} />
            </div>
          </div>
          {(hiddenByStatus.first > 0 || hiddenByStatus.students > 0) && (
            <p className="mb-2 text-[13px] text-[#757575]">
              Bu yerda faqat buyurtmalar.
              {hiddenByStatus.first > 0 && (
                <>
                  {' '}Birinchi darsga yozilgan <b>{hiddenByStatus.first}</b> ta lid —{' '}
                  <Link to="/admin/leads/birinchi-dars" className="font-medium text-brand-600 hover:underline">
                    «Birinchi darsga yozilganlar»
                  </Link>
                  {hiddenByStatus.students > 0 ? ',' : '.'}
                </>
              )}
              {hiddenByStatus.students > 0 && (
                <> o'quvchiga aylantirilgan <b>{hiddenByStatus.students}</b> tasi — «Status» filtrida.</>
              )}{' '}
              <button
                type="button"
                onClick={() => set({ status: 'all' })}
                className="font-medium text-brand-600 hover:underline"
              >
                Hammasini ko'rsatish
              </button>
            </p>
          )}
          <DataTable
            rows={pg.paged}
            columns={columns}
            rowKey={(l) => l.id}
            loading={loading}
            numbered
            offset={(pg.page - 1) * pg.pageSize}
            sortKey="createdAt"
            sortDir={sortDir}
            onSort={() => setSortDir((d) => (d === 'asc' ? 'desc' : 'asc'))}
            footer={<TablePagination {...pg} />}
          />
        </>
      ) : loading ? (
        <Loader label="Yuklanmoqda..." />
      ) : (
        <DndContext sensors={sensors} onDragStart={handleDragStart} onDragEnd={handleDragEnd}>
          <div className="flex items-start gap-4 overflow-x-auto pb-3">
            {stages.map((stage, i) => (
              <LeadColumn
                key={stage.id}
                stage={stage}
                leads={visibleLeads
                  // Yo'qotilgan lid o'z bosqichida emas, faqat DOIMIY "Yo'qotilgan" ustunida chiziladi.
                  .filter((l) => !isLeadLost(l) && stageOf(l) === stage.id)
                  // Eng so'nggi HARAKAT tepada. `createdAt` EMAS, chunki takroriy murojaatda
                  // u ATAYIN o'zgarmaydi (first-touch: lid QACHON birinchi kelgani hisobotlar
                  // uchun saqlanadi — `.claude/rules/lead-forms.md` §4). Operatorga esa "kim
                  // YAQINDA murojaat qildi" kerak, shuning uchun takroriy kelgan lid ham
                  // yuqoriga chiqadi.
                  .sort((a, b) => {
                    const at = a.lastRepeatAt || a.createdAt
                    const bt = b.lastRepeatAt || b.createdAt
                    const ta = at ? new Date(at).getTime() : 0
                    const tb = bt ? new Date(bt).getTime() : 0
                    return tb - ta
                  })}
                isFirst={i === 0}
                isLast={i === stages.length - 1}
                onCardClick={openLead}
                onEdit={(s) => {
                  setEditingStage(s)
                  setStageFormOpen(true)
                }}
                onDelete={handleStageDelete}
                onMove={handleStageMove}
                onCall={setCallLead}
              />
            ))}

            {/* DOIMIY "Yo'qotilgan" ustuni — har doim foydalanuvchi ustunlaridan KEYIN (ular qayta
                tartiblansa ham), tahrirlanmaydi/o'chirilmaydi. Eng so'nggi yo'qotilgani tepada. */}
            <LostColumn
              leads={visibleLeads
                .filter(isLeadLost)
                .sort((a, b) => (b.lostAt || '').localeCompare(a.lostAt || ''))}
              hidden={f.lost === 'hide'}
              showReportLink={can('leads.stats', 'view')}
              onCardClick={openLead}
              onCall={setCallLead}
            />

            <button
              type="button"
              onClick={() => {
                setEditingStage(null)
                setStageFormOpen(true)
              }}
              className="mt-[46px] flex min-h-[200px] w-[350px] shrink-0 flex-col items-center justify-center gap-2 rounded-lg border-2 border-dashed border-[#dbe0e6] text-[13px] font-medium text-[#757575] transition-colors hover:border-brand-600/40 hover:bg-brand-600/5 hover:text-brand-600"
            >
              <IconPlus className="h-5 w-5" /> Ustun qo'shish
            </button>
          </div>

          <DragOverlay>{activeId ? renderOverlay(leads.find((l) => l.id === activeId)) : null}</DragOverlay>
        </DndContext>
      )}

      {/* Modallar */}
      <LeadFormModal open={leadFormOpen} onClose={() => setLeadFormOpen(false)} onSubmit={handleLeadSubmit} initial={null} />
      <StageFormModal
        open={stageFormOpen}
        onClose={() => {
          setStageFormOpen(false)
          setEditingStage(null)
        }}
        onSubmit={handleStageSubmit}
        initial={editingStage}
      />
      <PhoneCallModal
        open={!!callLead}
        onClose={() => setCallLead(null)}
        title={callLead?.fullName}
        numbers={callNumbers}
      />
      {/* Har ochilishda YANGI nusxa — oldingi lidning tanlovi/izohi qolib ketmasin. */}
      {lostTarget && (
        <LeadLostModal
          key={lostTarget.id}
          open
          leadName={lostTarget.fullName}
          onClose={() => setLostTarget(null)}
          onConfirm={async (reasonId, comment) => {
            // Xato bo'lsa oyna ochiq qoladi va sababni O'ZI ko'rsatadi (ro'yxat tegilmaydi).
            applyLossState(await markLeadLost(lostTarget.id, reasonId, comment))
            setLostTarget(null)
          }}
        />
      )}
    </div>
  )
}

function renderOverlay(lead: Lead | undefined) {
  return lead ? <LeadCardContent lead={lead} dragging /> : null
}
