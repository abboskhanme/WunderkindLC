import { useEffect, useMemo, useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { IconMessage } from '@tabler/icons-react'
import type { Lead, Stage } from '@/types'
import { getFirstLessonLeads, type FirstLessonFilter, type FirstLessonLead } from '@/api/services/leads'
import { getStages } from '@/api/services/stages'
import { CallPickerModal } from '@/components/CallPickerModal'
import { DataTable, type DataColumn } from '@/components/ui/list/DataTable'
import { FilterGrid, FilterInput, FilterSelect } from '@/components/ui/list/FilterGrid'
import { FilterDateRange } from '@/components/ui/list/FilterDateRange'
import { PhoneChip } from '@/components/ui/list/PhoneChip'
import { TotalPill } from '@/components/ui/list/TotalPill'
import { TablePagination, usePagination } from '@/components/ui/TablePagination'
import { LeadBulkSmsModal } from './LeadBulkSmsModal'
import { WD_FULL, leadCallNumbers, shortLeadId, tableDateTime } from './leadLabels'
import { useUrlFilters } from './useUrlFilters'

/**
 * LIDLAR → «Birinchi darsga yozilganlar» (edutizim `/orders/come-orders`).
 *
 * Ochiq lid + natijasi hali belgilanmagan sinov darsi. Tanlov va filtrlar SERVERDA
 * (`GET /admin/leads/first-lesson`, `LeadFirstLesson`) — bosh sahifadagi "Birinchi darsga
 * keladiganlar" kartochkasi bilan BITTA ta'rif. Farqi: sanasi o'tib ketgan (lekin "keldi/kelmadi"
 * belgilanmagan) sinov ham ro'yxatda qoladi — qator QIZIL (`#FFCACA`), menejer qaytib ko'rsin.
 *
 * Sarlavha ham, "qo'shish" tugmasi ham YO'Q (edutizimdagidek); filtrlar doim ochiq.
 */

const FILTER_KEYS = [
  'date', 'from', 'to', 'course', 'level', 'color', 'weekday', 'parity', 'assignee', 'teacher', 'q',
] as const

const NONE = '__none__'

function uniquePairs(items: { id?: string | null; name?: string | null }[]) {
  const m = new Map<string, string>()
  for (const it of items) if (it.id) m.set(it.id, it.name || it.id)
  return [...m.entries()].map(([id, name]) => ({ id, name })).sort((a, b) => a.name.localeCompare(b.name))
}

/** Ommaviy SMS oynasi `Lead` kutadi — qatordan kerakli maydonlar bilan quriladi. */
function asLead(r: FirstLessonLead): Lead {
  return {
    id: r.id, fullName: r.fullName, gender: 'male', birthDate: '', phone: r.phone,
    fatherFullName: '', fatherPhone: r.fatherPhone, motherFullName: '', motherPhone: r.motherPhone,
    stage: r.stage, note: r.note ?? undefined,
  }
}

export function FirstLessonLeadsPage() {
  const navigate = useNavigate()
  const { values: f, set } = useUrlFilters(FILTER_KEYS)

  const [rows, setRows] = useState<FirstLessonLead[]>([])
  // Select variantlari FILTRSIZ ro'yxatdan — aks holda bitta filtr tanlanganda qolganlarining
  // variantlari (va tanlangan qiymatning o'zi) yo'qolib qolardi.
  const [all, setAll] = useState<FirstLessonLead[]>([])
  const [stages, setStages] = useState<Stage[]>([])
  const [loading, setLoading] = useState(true)
  const [selected, setSelected] = useState<Set<string>>(new Set())
  const [callRow, setCallRow] = useState<FirstLessonLead | null>(null)
  const [smsLeads, setSmsLeads] = useState<Lead[] | null>(null)

  // Qidiruv matni kechiktirib yuboriladi (har harfda so'rov ketmasin).
  const [q, setQ] = useState(f.q)
  useEffect(() => {
    const t = setTimeout(() => {
      if (q !== f.q) set({ q })
    }, 300)
    return () => clearTimeout(t)
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [q])

  useEffect(() => {
    getFirstLessonLeads().then(setAll).catch(() => setAll([]))
    getStages().then(setStages).catch(() => setStages([]))
  }, [])

  const filterKey = FILTER_KEYS.map((k) => f[k]).join('|')
  useEffect(() => {
    let cancelled = false
    const filter: FirstLessonFilter = { ...f }
    getFirstLessonLeads(filter)
      .then((r) => {
        if (!cancelled) setRows(r)
      })
      .catch(() => {
        if (!cancelled) setRows([])
      })
      .finally(() => {
        if (!cancelled) setLoading(false)
      })
    return () => {
      cancelled = true
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [filterKey])

  const courseOptions = useMemo(() => [...new Set(all.map((r) => r.course).filter(Boolean))].sort(), [all])
  const levelOptions = useMemo(() => [...new Set(all.map((r) => r.level).filter(Boolean))].sort(), [all])
  const teacherOptions = useMemo(() => uniquePairs(all.map((r) => ({ id: r.teacherId, name: r.teacherName }))), [all])
  const assigneeOptions = useMemo(
    () => uniquePairs(all.map((r) => ({ id: r.assigneeUserId, name: r.assigneeName }))),
    [all],
  )

  const pg = usePagination(rows)
  const allOnPage = pg.paged.length > 0 && pg.paged.every((r) => selected.has(r.id))
  const toggle = (id: string) =>
    setSelected((prev) => {
      const next = new Set(prev)
      if (next.has(id)) next.delete(id)
      else next.add(id)
      return next
    })
  const togglePage = () =>
    setSelected((prev) => {
      const next = new Set(prev)
      for (const r of pg.paged) {
        if (allOnPage) next.delete(r.id)
        else next.add(r.id)
      }
      return next
    })

  const columns: DataColumn<FirstLessonLead>[] = [
    {
      key: '__sel',
      width: 44,
      header: (
        <input
          type="checkbox"
          aria-label="Sahifadagilarni tanlash"
          checked={allOnPage}
          onChange={togglePage}
          className="h-4 w-4 accent-brand-600"
        />
      ),
      render: (r) => (
        <input
          type="checkbox"
          aria-label={`${r.fullName} — tanlash`}
          checked={selected.has(r.id)}
          onChange={() => toggle(r.id)}
          className="h-4 w-4 accent-brand-600"
        />
      ),
    },
    {
      key: 'id',
      header: 'ID',
      width: 80,
      render: (r) => <span className="font-mono text-[12px] text-[#555]" title={r.id}>{shortLeadId(r.id)}</span>,
    },
    {
      key: 'name',
      header: "O'quvchini ismi",
      render: (r) => (
        <button
          type="button"
          onClick={() => navigate(`/admin/leads/${r.id}`)}
          className="max-w-[220px] truncate text-left font-medium text-black hover:text-brand-600 hover:underline"
          title={r.fullName}
        >
          {r.fullName || '—'}
        </button>
      ),
    },
    {
      key: 'phone',
      header: 'Telefon raqam',
      render: (r) => <PhoneChip phone={r.phone || r.fatherPhone || r.motherPhone} onCall={() => setCallRow(r)} />,
    },
    { key: 'createdAt', header: 'Yaratilgan sanasi', render: (r) => <span className="whitespace-nowrap">{tableDateTime(r.createdAt)}</span> },
    {
      key: 'firstLessonAt',
      header: 'Birinchi darsga kelish sanasi',
      render: (r) => (
        <span className="whitespace-nowrap" title={r.groupName ? `Guruh: ${r.groupName}` : undefined}>
          {tableDateTime(r.firstLessonAt)}
        </span>
      ),
    },
    { key: 'teacher', header: "O'qituvchi", render: (r) => <span className="whitespace-nowrap">{r.teacherName}</span> },
    { key: 'course', header: 'Kurs', render: (r) => <span className="whitespace-nowrap">{r.course}</span> },
    { key: 'level', header: 'Kurs darajasi', render: (r) => <span className="whitespace-nowrap">{r.level}</span> },
    { key: 'moderator', header: 'Moderator', render: (r) => <span className="whitespace-nowrap">{r.assigneeName ?? ''}</span> },
    {
      key: 'note',
      header: 'Izoh',
      render: (r) => (
        <span className="block max-w-[260px] truncate text-[#555]" title={r.note || undefined}>
          {r.note || ''}
        </span>
      ),
    },
  ]

  const callNumbers = useMemo(() => (callRow ? leadCallNumbers(callRow) : []), [callRow])

  return (
    <div>
      <FilterGrid>
        <FilterInput
          type="date"
          title="Sanani tanlang (birinchi dars kuni)"
          aria-label="Sanani tanlang"
          value={f.date}
          onChange={(e) => set({ date: e.target.value })}
          className={f.date ? '' : 'text-[#6b7280]'}
        />
        <FilterDateRange
          title="Oraliqni tanlang (birinchi dars sanasi)"
          from={f.from}
          to={f.to}
          onChange={(from, to) => set({ from, to })}
        />
        <FilterSelect placeholder="Kurs" value={f.course} onChange={(e) => set({ course: e.target.value })}>
          {courseOptions.map((c) => (
            <option key={c} value={c}>
              {c}
            </option>
          ))}
        </FilterSelect>
        <FilterSelect placeholder="Daraja" value={f.level} onChange={(e) => set({ level: e.target.value })}>
          {levelOptions.map((lv) => (
            <option key={lv} value={lv}>
              {lv}
            </option>
          ))}
        </FilterSelect>
        <FilterSelect placeholder="Ranglar bo'yicha" value={f.color} onChange={(e) => set({ color: e.target.value })}>
          <option value="past">Qizil — sanasi o'tib ketgan</option>
          <option value="upcoming">Oq — kutilmoqda</option>
        </FilterSelect>
        <FilterSelect placeholder="Kun" value={f.weekday} onChange={(e) => set({ weekday: e.target.value })}>
          {WD_FULL.map((d, i) => (
            <option key={d} value={String(i)}>
              {d}
            </option>
          ))}
        </FilterSelect>
        <FilterSelect placeholder="Toq/Juft kunlar" value={f.parity} onChange={(e) => set({ parity: e.target.value })}>
          <option value="odd">Toq kunlar (Du, Chor, Ju)</option>
          <option value="even">Juft kunlar (Se, Pay, Sha)</option>
        </FilterSelect>
        <FilterSelect placeholder="Moderator" value={f.assignee} onChange={(e) => set({ assignee: e.target.value })}>
          {assigneeOptions.map((m) => (
            <option key={m.id} value={m.id}>
              {m.name}
            </option>
          ))}
          <option value={NONE}>Biriktirilmagan</option>
        </FilterSelect>
        <FilterSelect placeholder="O'qituvchi" value={f.teacher} onChange={(e) => set({ teacher: e.target.value })}>
          {teacherOptions.map((t) => (
            <option key={t.id} value={t.id}>
              {t.name}
            </option>
          ))}
        </FilterSelect>
        <FilterInput search placeholder="Qidirish" value={q} onChange={(e) => setQ(e.target.value)} />
      </FilterGrid>

      <div className="flex items-center gap-2">
        {selected.size > 0 && (
          <p className="mb-2 mr-auto text-[13px] text-[#333]">
            Tanlandi: <b>{selected.size}</b>
            {' · '}
            <button
              type="button"
              onClick={() => setSmsLeads(rows.filter((r) => selected.has(r.id)).map(asLead))}
              className="inline-flex items-center gap-1 font-medium text-brand-600 hover:underline"
            >
              <IconMessage className="h-4 w-4" /> SMS yuborish
            </button>
            {' · '}
            <button type="button" onClick={() => setSelected(new Set())} className="font-medium text-[#757575] hover:underline">
              Tozalash
            </button>
          </p>
        )}
        <div className="ml-auto">
          <TotalPill total={rows.length} />
        </div>
      </div>

      <DataTable
        rows={pg.paged}
        columns={columns}
        rowKey={(r) => r.id}
        loading={loading}
        numbered
        offset={(pg.page - 1) * pg.pageSize}
        // edutizim: birinchi dars sanasi o'tib ketgan qator — och-qizil (`custom-table-row-past`).
        rowClassName={(r) => (r.isPast ? 'bg-[#FFCACA] hover:bg-[#F8B9B9]!' : undefined)}
        // ⚠️ Bu sahifada "qo'shish" tugmasi ATAYIN yo'q (edutizimdagidek) — shuning uchun
        // bo'sh ro'yxatda qayerdan yozilishini AYTIB qo'yamiz, aks holda "olib o'tib
        // bo'lmayapti" degan tushunmovchilik chiqadi.
        emptyHint="Lid sahifasidagi «⋮» menyudan «Sinov darsiga yozish» orqali qo'shiladi."
        footer={<TablePagination {...pg} />}
      />

      <CallPickerModal
        open={!!callRow}
        onClose={() => setCallRow(null)}
        title={callRow?.fullName}
        numbers={callNumbers}
      />
      <LeadBulkSmsModal open={!!smsLeads} onClose={() => setSmsLeads(null)} leads={smsLeads ?? []} stages={stages} />
    </div>
  )
}
