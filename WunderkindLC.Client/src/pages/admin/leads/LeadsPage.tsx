import { useEffect, useState } from 'react'
import { useSearchParams } from 'react-router-dom'
import {
  DndContext,
  DragOverlay,
  PointerSensor,
  useSensor,
  useSensors,
  type DragEndEvent,
  type DragStartEvent,
} from '@dnd-kit/core'
import { Plus, MessageSquare, Search, X } from 'lucide-react'
import type { Lead, Stage, LeadSource, District } from '@/types'
import {
  getLeads,
  createLead,
  updateLead,
  updateLeadStage,
  deleteLead,
  type LeadAnswersPayload,
} from '@/api/services/leads'
import { getLeadSources } from '@/api/services/leadSources'
import { getDistricts } from '@/api/services/districts'
import {
  getStages,
  createStage,
  updateStage,
  deleteStage,
  reorderStages,
  type StagePayload,
} from '@/api/services/stages'
import { Button } from '@/components/ui/Button'
import { PageHeader } from '@/components/ui/PageHeader'
import { Loader } from '@/components/ui/Loader'
import { CallPickerModal, type CallOption } from '@/components/CallPickerModal'
import { LeadColumn } from './LeadColumn'
import { LeadCardContent } from './LeadCard'
import { ReasonPromptModal } from '@/components/ui/ReasonPromptModal'
import { LeadFormModal, type LeadFormValues } from './LeadFormModal'
import { LeadDetailModal } from './LeadDetailModal'
import { LeadBulkSmsModal } from './LeadBulkSmsModal'
import { StageFormModal } from './StageFormModal'
import { usePerm } from '@/lib/permissions'

export function LeadsPage() {
  const { can } = usePerm()
  // `?lead=<id>` — boshqa bo'limdan (masalan "Formalar → Arizalar") AYNAN shu lidni ochish uchun.
  const [searchParams, setSearchParams] = useSearchParams()
  const [leads, setLeads] = useState<Lead[]>([])
  const [stages, setStages] = useState<Stage[]>([])
  const [loading, setLoading] = useState(true)
  const [activeId, setActiveId] = useState<string | null>(null)

  // modallar
  const [detailLead, setDetailLead] = useState<Lead | null>(null)
  const [deletingLead, setDeletingLead] = useState<Lead | null>(null)
  const [leadFormOpen, setLeadFormOpen] = useState(false)
  const [editingLead, setEditingLead] = useState<Lead | null>(null)
  const [stageFormOpen, setStageFormOpen] = useState(false)
  const [editingStage, setEditingStage] = useState<Stage | null>(null)
  // Lid raqamiga qo'ng'iroq qilish oynasi
  const [callLead, setCallLead] = useState<Lead | null>(null)
  // Lidlarga ommaviy SMS oynasi
  const [bulkSmsOpen, setBulkSmsOpen] = useState(false)
  // Sana bo'yicha filtr (kiritilgan sana): bitta kun (dan=gacha) yoki oraliq
  const [dateFrom, setDateFrom] = useState('')
  const [dateTo, setDateTo] = useState('')
  // Manba bo'yicha filtr: 'all' | '__none__' (manbasiz) | manba nomi
  const [sourceFilter, setSourceFilter] = useState('all')
  const [sources, setSources] = useState<LeadSource[]>([])
  // Qidiruv — FAQAT lidlar ichida (ism, telefon, ota-ona, manba, fan, maktab, izoh)
  const [search, setSearch] = useState('')
  // Tashqi maktab bo'yicha filtr (o'quvchilar sahifasidagi bilan bir xil: tuman → maktab)
  const [districtFilter, setDistrictFilter] = useState('all')
  const [schoolFilter, setSchoolFilter] = useState('all')
  const [districts, setDistricts] = useState<District[]>([])

  const sensors = useSensors(useSensor(PointerSensor, { activationConstraint: { distance: 6 } }))

  useEffect(() => {
    Promise.all([getLeads(), getStages()])
      .then(([l, s]) => {
        setLeads(l)
        setStages(s)
      })
      .finally(() => setLoading(false))
    getLeadSources()
      .then(setSources)
      .catch(() => setSources([]))
    getDistricts()
      .then(setDistricts)
      .catch(() => setDistricts([]))
  }, [])

  /**
   * `?lead=<id>` bilan kelingan bo'lsa — o'sha lidning oynasi ochiladi va parametr manzildan
   * OLIB TASHLANADI: aks holda oynani yopib, sahifani yangilaganda u qaytadan ochilaverardi.
   * Lid topilmasa (o'chirilgan) hech narsa qilinmaydi — ro'yxat odatdagidek ko'rinadi.
   */
  useEffect(() => {
    const id = searchParams.get('lead')
    if (!id || leads.length === 0) return
    const lead = leads.find((l) => l.id === id)
    if (lead) setDetailLead(lead)
    const next = new URLSearchParams(searchParams)
    next.delete('lead')
    setSearchParams(next, { replace: true })
  }, [leads, searchParams, setSearchParams])

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

  /* ---------- Lid CRUD ---------- */
  /**
   * ⚠️ Promise QAYTARADI va xatoni YUQORIGA (modalga) o'tkazadi: oynani yopish endi modalning
   * o'zida (`onClose` ni u chaqiradi), ya'ni saqlash muvaffaqiyatsiz bo'lsa oyna ochiq qoladi va
   * xato ko'rinadi.
   *
   * ⚠️ Ro'yxat SERVER JAVOBIDAN KEYIN yangilanadi (optimistik emas): server rad etgan tahrir
   * ilgari ekranda o'zgargan bo'lib qolib ketardi — sahifa yangilanmaguncha yolg'on ko'rinardi.
   */
  const handleLeadSubmit = async (values: LeadFormValues, answers: LeadAnswersPayload) => {
    if (editingLead) {
      const id = editingLead.id
      await updateLead(id, values, answers)
      setLeads((prev) => prev.map((l) => (l.id === id ? { ...l, ...values } : l)))
    } else {
      const stageId = stages[0]?.id ?? 'new'
      const lead = await createLead(values, stageId, answers)
      setLeads((prev) => [lead, ...prev])
    }
  }

  const handleLeadEdit = (lead: Lead) => {
    setDetailLead(null)
    setEditingLead(lead)
    setLeadFormOpen(true)
  }

  const handleLeadDelete = (lead: Lead) => setDeletingLead(lead)

  const doDeleteLead = (reasonId: string | undefined) => {
    const lead = deletingLead
    if (!lead) return
    deleteLead(lead.id, reasonId).then(() => {
      setLeads((prev) => prev.filter((l) => l.id !== lead.id))
      setDetailLead(null)
      setDeletingLead(null)
    })
  }

  /* ---------- Ustun CRUD ---------- */
  const handleStageSubmit = (values: StagePayload) => {
    if (editingStage) {
      const id = editingStage.id
      setStages((prev) => prev.map((s) => (s.id === id ? { ...s, ...values } : s)))
      updateStage(id, values)
    } else {
      createStage(values).then((stage) => setStages((prev) => [...prev, stage]))
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
      alert('Kamida bitta ustun bo\'lishi kerak.')
      return
    }
    if (!confirm(`"${stage.title}" ustunini o'chirasizmi?`)) return
    deleteStage(stage.id).then(() => setStages((prev) => prev.filter((s) => s.id !== stage.id)))
  }

  const handleStageMove = (id: string, dir: -1 | 1) => {
    setStages((prev) => {
      const idx = prev.findIndex((s) => s.id === id)
      const j = idx + dir
      if (idx < 0 || j < 0 || j >= prev.length) return prev
      const next = [...prev]
      ;[next[idx], next[j]] = [next[j], next[idx]]
      reorderStages(next.map((s) => s.id))
      return next
    })
  }

  const activeLead = activeId ? leads.find((l) => l.id === activeId) : null
  const convertedCount = leads.filter((l) => l.convertedStudentId).length
  // Sana filtri: lid kiritilgan sana (createdAt) tanlangan oraliqqa kirsa ko'rinadi.
  const dateActive = !!(dateFrom || dateTo)
  const inDateRange = (l: Lead) => {
    if (!dateActive) return true
    const d = (l.createdAt || '').slice(0, 10)
    if (!d) return false
    if (dateFrom && d < dateFrom) return false
    if (dateTo && d > dateTo) return false
    return true
  }
  const inSourceFilter = (l: Lead) => {
    if (sourceFilter === 'all') return true
    if (sourceFilter === '__none__') return !l.source
    return l.source === sourceFilter
  }
  // Maktab id → nomi (qidiruv va ko'rsatish uchun) — tumanlar ma'lumotnomasidan.
  const schoolNames = new Map<string, string>()
  for (const d of districts) for (const s of d.schools) schoolNames.set(s.id, s.name)
  const inSchoolFilter = (l: Lead) => {
    if (districtFilter !== 'all' && l.districtId !== districtFilter) return false
    if (schoolFilter !== 'all' && l.schoolId !== schoolFilter) return false
    return true
  }
  // Qidiruv FAQAT lidlar ro'yxati ichida ishlaydi (serverga so'rov yubormaydi).
  // Matnli maydonlar oddiy "ichida bor" bo'yicha; telefonlar esa faqat RAQAMLAR bo'yicha
  // solishtiriladi — shu sabab "901234567" ham "+998 90 123 45 67" ni topadi.
  const q = search.trim().toLowerCase()
  const qDigits = q.replace(/\D/g, '')
  const matchesSearch = (l: Lead) => {
    if (!q) return true
    const text = [
      l.fullName,
      l.fatherFullName,
      l.motherFullName,
      l.source,
      l.interestSubject,
      l.note,
      l.schoolId ? schoolNames.get(l.schoolId) : '',
    ]
      .filter(Boolean)
      .join(' ')
      .toLowerCase()
    if (text.includes(q)) return true
    return (
      qDigits.length >= 3 &&
      [l.phone, l.fatherPhone, l.motherPhone].some((p) =>
        (p || '').replace(/\D/g, '').includes(qDigits),
      )
    )
  }
  const visibleLeads = leads.filter(
    (l) => inDateRange(l) && inSourceFilter(l) && inSchoolFilter(l) && matchesSearch(l),
  )
  const filtersActive =
    dateActive || sourceFilter !== 'all' || districtFilter !== 'all' || schoolFilter !== 'all' || !!q
  // Xavfsizlik tarmog'i: bosqichi mavjud ustunlardan biriga MOS KELMAYDIGAN lid (eski bo'sh "" yoki
  // o'chirilgan ustun — masalan daraja testidan bosqich yo'q paytda tushgan) ko'rinmay qolmasin —
  // birinchi ustunda ko'rsatamiz (sudrab to'g'ri ustunga o'tkazish mumkin).
  const stageIds = new Set(stages.map((s) => s.id))

  return (
    <div>
      <PageHeader
        title="Lidlar"
        sub={
          <>
            {leads.length} ta lid · {convertedCount} aylantirilgan — kartani sudrang yoki ustiga bosing
          </>
        }
        actions={
          <div className="flex items-center gap-2">
            <Button variant="secondary" onClick={() => setBulkSmsOpen(true)}>
              <MessageSquare className="h-4 w-4" /> SMS yuborish
            </Button>
            {can('leads.list', 'create') && (
              <Button
                onClick={() => {
                  setEditingLead(null)
                  setLeadFormOpen(true)
                }}
              >
                <Plus className="h-4 w-4" /> Yangi lid
              </Button>
            )}
          </div>
        }
      />

      {/* Qidiruv — faqat lidlar ichida (ism, telefon, ota-ona, manba, maktab, izoh) */}
      <div className="mb-3 flex flex-wrap items-center gap-2">
        <div className="flex items-center gap-2 rounded-lg border border-slate-200 bg-white px-3 py-2 transition-colors focus-within:border-brand-400">
          <Search className="h-4 w-4 shrink-0 text-slate-400" />
          <input
            value={search}
            onChange={(e) => setSearch(e.target.value)}
            placeholder="Lidlar ichidan qidirish: ism, telefon, maktab..."
            className="w-64 border-0 bg-transparent text-sm text-slate-700 outline-none placeholder:text-slate-400"
          />
          {search && (
            <button
              type="button"
              onClick={() => setSearch('')}
              title="Tozalash"
              className="text-slate-400 transition-colors hover:text-slate-600"
            >
              <X className="h-3.5 w-3.5" />
            </button>
          )}
        </div>

        {/* Tuman → maktab (ma'lumotnoma bo'sh bo'lsa ko'rsatilmaydi) */}
        {districts.length > 0 && (
          <>
            <select
              value={districtFilter}
              onChange={(e) => {
                // Tuman o'zgarsa, oldingi maktab tanlovi tozalanadi (boshqa tumanga tegishli edi).
                setDistrictFilter(e.target.value)
                setSchoolFilter('all')
              }}
              className="rounded-lg border border-slate-200 bg-white px-3 py-2 text-sm text-slate-600 outline-none transition-colors focus:border-brand-400"
            >
              <option value="all">Barcha tumanlar</option>
              {districts.map((d) => (
                <option key={d.id} value={d.id}>
                  {d.name}
                </option>
              ))}
            </select>
            <select
              value={schoolFilter}
              onChange={(e) => setSchoolFilter(e.target.value)}
              disabled={districtFilter === 'all'}
              className="rounded-lg border border-slate-200 bg-white px-3 py-2 text-sm text-slate-600 outline-none transition-colors focus:border-brand-400 disabled:opacity-50"
            >
              <option value="all">
                {districtFilter === 'all' ? 'Barcha maktablar' : '— barcha maktablar —'}
              </option>
              {(districts.find((d) => d.id === districtFilter)?.schools ?? []).map((sc) => (
                <option key={sc.id} value={sc.id}>
                  {sc.name}
                </option>
              ))}
            </select>
          </>
        )}
      </div>

      {/* Sana bo'yicha filtr — bitta kun (Bugun) yoki "dan ... gacha" oraliq */}
      <div className="mb-4 flex flex-wrap items-center gap-2">
        <span className="text-sm font-medium text-slate-500">Kiritilgan sana:</span>
        <input
          type="date"
          value={dateFrom}
          max={dateTo || undefined}
          onChange={(e) => setDateFrom(e.target.value)}
          className="rounded-lg border border-slate-200 bg-white px-3 py-2 text-sm text-slate-600 outline-none transition-colors focus:border-brand-400"
        />
        <span className="text-slate-400">—</span>
        <input
          type="date"
          value={dateTo}
          min={dateFrom || undefined}
          onChange={(e) => setDateTo(e.target.value)}
          className="rounded-lg border border-slate-200 bg-white px-3 py-2 text-sm text-slate-600 outline-none transition-colors focus:border-brand-400"
        />
        <button
          type="button"
          onClick={() => {
            const t = new Date().toISOString().slice(0, 10)
            setDateFrom(t)
            setDateTo(t)
          }}
          className="rounded-lg border border-slate-200 bg-white px-3 py-2 text-sm font-medium text-slate-600 transition-colors hover:bg-slate-50"
        >
          Bugun
        </button>

        {sources.length > 0 && (
          <>
            <span className="ml-2 text-sm font-medium text-slate-500">Manba:</span>
            <select
              value={sourceFilter}
              onChange={(e) => setSourceFilter(e.target.value)}
              className="rounded-lg border border-slate-200 bg-white px-3 py-2 text-sm text-slate-600 outline-none transition-colors focus:border-brand-400"
            >
              <option value="all">Barcha manbalar</option>
              {sources.map((s) => (
                <option key={s.id} value={s.name}>
                  {s.name}
                </option>
              ))}
              <option value="__none__">Noma'lum</option>
            </select>
          </>
        )}

        {filtersActive && (
          <>
            <button
              type="button"
              onClick={() => {
                setDateFrom('')
                setDateTo('')
                setSourceFilter('all')
                setSearch('')
                setDistrictFilter('all')
                setSchoolFilter('all')
              }}
              className="rounded-lg px-3 py-2 text-sm font-medium text-slate-400 transition-colors hover:bg-slate-50 hover:text-slate-600"
            >
              Tozalash
            </button>
            <span className="text-sm text-slate-400">{visibleLeads.length} ta topildi</span>
          </>
        )}
      </div>

      {loading ? (
        <Loader label="Yuklanmoqda..." />
      ) : (
        <DndContext sensors={sensors} onDragStart={handleDragStart} onDragEnd={handleDragEnd}>
          <div className="kanban">
            {stages.map((stage, i) => (
              <LeadColumn
                key={stage.id}
                stage={stage}
                leads={visibleLeads
                  .filter((l) => l.stage === stage.id || (i === 0 && !stageIds.has(l.stage)))
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
                onCardClick={setDetailLead}
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
              onClick={() => {
                setEditingStage(null)
                setStageFormOpen(true)
              }}
              className="flex min-h-[200px] w-72 shrink-0 flex-col items-center justify-center gap-2 rounded-2xl border-2 border-dashed border-slate-200 text-sm font-medium text-slate-400 transition-colors hover:border-brand-300 hover:bg-brand-50/50 hover:text-brand-600"
            >
              <Plus className="h-5 w-5" /> Ustun qo'shish
            </button>
          </div>

          <DragOverlay>
            {activeLead ? <LeadCardContent lead={activeLead} dragging /> : null}
          </DragOverlay>
        </DndContext>
      )}

      {/* Modallar */}
      <LeadDetailModal
        lead={detailLead}
        schoolName={detailLead?.schoolId ? schoolNames.get(detailLead.schoolId) : undefined}
        canEdit={can('leads.list', 'edit')}
        canDelete={can('leads.list', 'delete')}
        onClose={() => setDetailLead(null)}
        onEdit={handleLeadEdit}
        onDelete={handleLeadDelete}
        onConverted={(leadId, studentId) => {
          setLeads((prev) =>
            prev.map((l) => (l.id === leadId ? { ...l, convertedStudentId: studentId } : l)),
          )
          setDetailLead((prev) =>
            prev && prev.id === leadId ? { ...prev, convertedStudentId: studentId } : prev,
          )
        }}
      />

      <ReasonPromptModal
        open={!!deletingLead}
        category="lead_delete"
        title="Lidni o'chirish"
        message={deletingLead ? `"${deletingLead.fullName}" lidini o'chirasizmi?` : undefined}
        confirmLabel="O'chirish"
        tone="red"
        onConfirm={doDeleteLead}
        onClose={() => setDeletingLead(null)}
      />
      <LeadFormModal
        open={leadFormOpen}
        onClose={() => {
          setLeadFormOpen(false)
          setEditingLead(null)
        }}
        onSubmit={handleLeadSubmit}
        initial={editingLead}
      />
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
        open={bulkSmsOpen}
        onClose={() => setBulkSmsOpen(false)}
        leads={leads.filter((l) => !l.convertedStudentId)}
        stages={stages}
      />
      <CallPickerModal
        open={!!callLead}
        onClose={() => setCallLead(null)}
        title={callLead?.fullName}
        numbers={
          callLead
            ? (
                [
                  { label: "O'z raqami", number: callLead.phone },
                  { label: 'Otasi', number: callLead.fatherPhone },
                  { label: 'Onasi', number: callLead.motherPhone },
                ].filter((n) => n.number) as CallOption[]
              )
            : []
        }
      />
    </div>
  )
}
