import { useEffect, useMemo, useState } from 'react'
import { useNavigate } from 'react-router-dom'
import {
  DndContext,
  DragOverlay,
  PointerSensor,
  useSensor,
  useSensors,
  type DragEndEvent,
  type DragStartEvent,
} from '@dnd-kit/core'
import { IconLayoutColumns, IconList, IconMessage, IconPlus, IconSearch } from '@tabler/icons-react'
import type { District, Lead, LeadSource, Stage } from '@/types'
import {
  createLead,
  getLeadCourses,
  getLeads,
  updateLeadStage,
  type LeadAnswersPayload,
} from '@/api/services/leads'
import { getLeadSources } from '@/api/services/leadSources'
import { getDistricts } from '@/api/services/districts'
import {
  createStage,
  deleteStage,
  getStages,
  reorderStages,
  updateStage,
  type StagePayload,
} from '@/api/services/stages'
import { CallPickerModal } from '@/components/CallPickerModal'
import { DataTable, type DataColumn } from '@/components/ui/list/DataTable'
import { FilterGrid, FilterInput, FilterSelect } from '@/components/ui/list/FilterGrid'
import { FilterDateRange } from '@/components/ui/list/FilterDateRange'
import { ListToolbar } from '@/components/ui/list/ListToolbar'
import { PhoneChip } from '@/components/ui/list/PhoneChip'
import { TintedIconButton } from '@/components/ui/list/TintedIconButton'
import { TotalPill } from '@/components/ui/list/TotalPill'
import { ViewToggle } from '@/components/ui/list/ViewToggle'
import { TablePagination, usePagination } from '@/components/ui/TablePagination'
import { Loader } from '@/components/ui/Loader'
import { apiErrorMessage } from '@/lib/utils'
import { usePerm } from '@/lib/permissions'
import { LeadColumn } from './LeadColumn'
import { LeadCardContent } from './LeadCard'
import { LeadFormModal, type LeadFormValues } from './LeadFormModal'
import { LeadBulkSmsModal } from './LeadBulkSmsModal'
import { StageFormModal } from './StageFormModal'
import { leadCallNumbers, shortLeadId, tableDateTime } from './leadLabels'
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
  'status', 'source', 'district', 'school',
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
  const [activeId, setActiveId] = useState<string | null>(null)
  const [sources, setSources] = useState<LeadSource[]>([])
  const [districts, setDistricts] = useState<District[]>([])
  const [courseNames, setCourseNames] = useState<string[]>([])

  // Filtr paneli: manzilda filtr bo'lsa (masalan "orqaga" qaytilganda) — ochiq holda boshlanadi.
  const [filtersOpen, setFiltersOpen] = useState(filtersActive)
  // "⋮" — qatorlarni tanlash rejimi (tanlanganlarga ommaviy SMS).
  const [selectMode, setSelectMode] = useState(false)
  const [selected, setSelected] = useState<Set<string>>(new Set())
  const [sortDir, setSortDir] = useState<'asc' | 'desc'>('desc')

  // modallar
  const [leadFormOpen, setLeadFormOpen] = useState(false)
  const [stageFormOpen, setStageFormOpen] = useState(false)
  const [editingStage, setEditingStage] = useState<Stage | null>(null)
  const [callLead, setCallLead] = useState<Lead | null>(null)
  const [bulkSmsLeads, setBulkSmsLeads] = useState<Lead[] | null>(null)

  const sensors = useSensors(useSensor(PointerSensor, { activationConstraint: { distance: 6 } }))

  useEffect(() => {
    Promise.all([getLeads(), getStages()])
      .then(([l, s]) => {
        setLeads(l)
        setStages(s)
      })
      .finally(() => setLoading(false))
    getLeadSources().then(setSources).catch(() => setSources([]))
    getDistricts().then(setDistricts).catch(() => setDistricts([]))
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
  const setView = (v: View) => {
    // Tanlash rejimi faqat jadvalda — kanbanga o'tganda tanlov yashirin qolib ketmasin.
    setSelectMode(false)
    setSelected(new Set())
    set({ view: v === 'kanban' ? 'kanban' : '' })
  }

  /* ---------- Karta drag-and-drop ---------- */
  const handleDragStart = (e: DragStartEvent) => setActiveId(String(e.active.id))

  const handleDragEnd = (e: DragEndEvent) => {
    setActiveId(null)
    const { active, over } = e
    if (!over) return
    const leadId = String(active.id)
    const newStage = String(over.id)
    const lead = leads.find((l) => l.id === leadId)
    if (!lead || lead.stage === newStage) return

    setLeads((prev) => prev.map((l) => (l.id === leadId ? { ...l, stage: newStage } : l)))
    updateLeadStage(leadId, newStage).catch(() => {
      setLeads((prev) => prev.map((l) => (l.id === leadId ? { ...l, stage: lead.stage } : l)))
    })
  }

  /* ---------- Yangi lid ---------- */
  /**
   * ⚠️ Promise QAYTARADI va xatoni YUQORIGA (modalga) o'tkazadi: saqlash muvaffaqiyatsiz bo'lsa
   * oyna ochiq qoladi va xato ko'rinadi. Ro'yxat SERVER JAVOBIDAN KEYIN yangilanadi (optimistik
   * emas). Tahrirlash endi lid SAHIFASIDA (`LeadDetailPage`).
   */
  const handleLeadSubmit = async (values: LeadFormValues, answers: LeadAnswersPayload) => {
    const stageId = stages[0]?.id ?? 'new'
    const lead = await createLead(values, stageId, answers)
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
    const count = leads.filter((l) => l.stage === stage.id).length
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

  // Maktab id → nomi (qidiruv uchun) — tumanlar ma'lumotnomasidan.
  const schoolNames = useMemo(() => {
    const m = new Map<string, string>()
    for (const d of districts) for (const s of d.schools) m.set(s.id, s.name)
    return m
  }, [districts])

  // Qidiruv FAQAT lidlar ro'yxati ichida (serverga so'rov yubormaydi). Matnli maydonlar oddiy
  // "ichida bor" bo'yicha; telefonlar esa faqat RAQAMLAR bo'yicha solishtiriladi — shu sabab
  // "901234567" ham "+998 90 123 45 67" ni topadi.
  const q = f.q.trim().toLowerCase()
  const qDigits = q.replace(/\D/g, '')
  const matchesSearch = (l: Lead) => {
    if (!q) return true
    const text = [
      l.fullName, l.fatherFullName, l.motherFullName, l.source, l.interestSubject, l.note,
      l.schoolId ? schoolNames.get(l.schoolId) : '', l.teacherName, l.groupName, l.assigneeName,
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

  const matches = (l: Lead) => {
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
    if (f.status === 'lead' && l.convertedStudentId) return false
    if (f.status === 'student' && !l.convertedStudentId) return false
    if (f.source && (f.source === NONE ? !!l.source : l.source !== f.source)) return false
    if (f.district && l.districtId !== f.district) return false
    if (f.school && l.schoolId !== f.school) return false
    return matchesSearch(l)
  }
  const visibleLeads = leads.filter(matches)

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

  const toggleSelected = (id: string) =>
    setSelected((prev) => {
      const next = new Set(prev)
      if (next.has(id)) next.delete(id)
      else next.add(id)
      return next
    })
  const allOnPageSelected = pg.paged.length > 0 && pg.paged.every((l) => selected.has(l.id))
  const togglePage = () =>
    setSelected((prev) => {
      const next = new Set(prev)
      for (const l of pg.paged) {
        if (allOnPageSelected) next.delete(l.id)
        else next.add(l.id)
      }
      return next
    })

  const columns: DataColumn<Lead>[] = [
    ...(selectMode
      ? [
          {
            key: '__sel',
            width: 44,
            header: (
              <input
                type="checkbox"
                aria-label="Sahifadagilarni tanlash"
                checked={allOnPageSelected}
                onChange={togglePage}
                className="h-4 w-4 accent-brand-600"
              />
            ),
            render: (l: Lead) => (
              <input
                type="checkbox"
                aria-label={`${l.fullName} — tanlash`}
                checked={selected.has(l.id)}
                onChange={() => toggleSelected(l.id)}
                className="h-4 w-4 accent-brand-600"
              />
            ),
          } satisfies DataColumn<Lead>,
        ]
      : []),
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
        <button
          type="button"
          onClick={() => openLead(l)}
          className="max-w-[220px] truncate text-left font-medium text-black hover:text-brand-600 hover:underline"
          title={l.fullName}
        >
          {l.fullName || '—'}
        </button>
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
  // ⚠️ Memo: `CallPickerModal` raqamlar massivi o'zgarganda holatini tozalaydi — har renderda
  // yangi massiv berilsa oyna ochiq turganda tanlov o'z-o'zidan bekor bo'lib qolardi.
  const callNumbers = useMemo(() => (callLead ? leadCallNumbers(callLead) : []), [callLead])
  const canCreate = can('leads.list', 'create')
  const openBulkSms = () => {
    if (selectMode && selected.size > 0) setBulkSmsLeads(leads.filter((l) => selected.has(l.id)))
    else setBulkSmsLeads(leads.filter((l) => !l.convertedStudentId))
  }

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
        extra={
          <TintedIconButton
            label={selectMode && selected.size > 0 ? `Tanlanganlarga SMS (${selected.size})` : 'Lidlarga SMS yuborish'}
            onClick={openBulkSms}
          >
            <IconMessage className="h-5 w-5" />
          </TintedIconButton>
        }
        onMore={
          view === 'table'
            ? () => {
                setSelectMode((m) => !m)
                setSelected(new Set())
              }
            : undefined
        }
        moreActive={selectMode}
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
            <option value="lead">Lid (aylantirilmagan)</option>
            <option value="student">O'quvchiga aylantirilgan</option>
          </FilterSelect>
          <FilterSelect placeholder="Manba" value={f.source} onChange={(e) => set({ source: e.target.value })}>
            {sourceOptions.map((s) => (
              <option key={s} value={s}>
                {s}
              </option>
            ))}
            <option value={NONE}>Noma'lum</option>
          </FilterSelect>
          {districts.length > 0 && (
            <>
              <FilterSelect
                placeholder="Tuman"
                value={f.district}
                // Tuman o'zgarsa, oldingi maktab tanlovi tozalanadi (boshqa tumanga tegishli edi).
                onChange={(e) => set({ district: e.target.value, school: '' })}
              >
                {districts.map((d) => (
                  <option key={d.id} value={d.id}>
                    {d.name}
                  </option>
                ))}
              </FilterSelect>
              <FilterSelect
                placeholder="Maktab"
                value={f.school}
                disabled={!f.district}
                onChange={(e) => set({ school: e.target.value })}
              >
                {(districts.find((d) => d.id === f.district)?.schools ?? []).map((s) => (
                  <option key={s.id} value={s.id}>
                    {s.name}
                  </option>
                ))}
              </FilterSelect>
            </>
          )}
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
            {selectMode && (
              <p className="mb-2 mr-auto text-[13px] text-[#333]">
                Tanlandi: <b>{selected.size}</b>
                {selected.size > 0 && (
                  <>
                    {' · '}
                    <button type="button" onClick={openBulkSms} className="font-medium text-brand-600 hover:underline">
                      SMS yuborish
                    </button>
                    {' · '}
                    <button
                      type="button"
                      onClick={() => setSelected(new Set())}
                      className="font-medium text-[#757575] hover:underline"
                    >
                      Tozalash
                    </button>
                  </>
                )}
              </p>
            )}
            <div className="ml-auto">
              <TotalPill total={visibleLeads.length} />
            </div>
          </div>
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
                  .filter((l) => stageOf(l) === stage.id)
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
      <LeadBulkSmsModal
        open={!!bulkSmsLeads}
        onClose={() => setBulkSmsLeads(null)}
        leads={bulkSmsLeads ?? []}
        stages={stages}
      />
      <CallPickerModal
        open={!!callLead}
        onClose={() => setCallLead(null)}
        title={callLead?.fullName}
        numbers={callNumbers}
      />
    </div>
  )
}

function renderOverlay(lead: Lead | undefined) {
  return lead ? <LeadCardContent lead={lead} dragging /> : null
}
