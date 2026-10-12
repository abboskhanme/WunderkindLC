import { useMemo, useState } from 'react'
import {
  getAttendanceFines,
  restoreAttendanceFine,
  waiveAttendanceFine,
  waiveAttendanceFinesDay,
  type AttendanceFine,
} from '@/api/services/attendanceFines'
import { DataTable } from '@/components/ui/list/DataTable'
import { TablePagination, usePagination } from '@/components/ui/TablePagination'
import { Modal } from '@/components/ui/Modal'
import { Button } from '@/components/ui/Button'
import { useAsync } from '@/hooks/useAsync'
import { usePerm } from '@/lib/permissions'
import { currentMonth, shiftMonth } from '@/lib/month'
import { apiErrorMessage, cn, formatMoney } from '@/lib/utils'
import { localIsoDate } from '@/lib/localDate'

type StatusFilter = '' | 'active' | 'waived'

const STATUS_CHIPS: { key: StatusFilter; label: string }[] = [
  { key: '', label: 'Hammasi' },
  { key: 'active', label: 'Faol' },
  { key: 'waived', label: 'Bekor qilingan' },
]

const dmy = (d: string) => (d && d.length >= 10 ? `${d.slice(8, 10)}.${d.slice(5, 7)}.${d.slice(0, 4)}` : d)
const hm = (iso: string | null) => (iso && iso.length >= 16 ? iso.slice(11, 16) : '')

/**
 * Moliya → Jarima → «Davomat jarimalari» (CRM Phase 3, U3c): davomat o'z vaqtida olinmagan darslar uchun
 * o'qituvchiga yozilgan jarimalar (fon xizmati yozadi). Bekor qilish — sabab bilan; bayram kuni — «Kunni bekor
 * qilish». Faol jarima o'qituvchi maoshidan ushlanadi (kassaga tegmaydi).
 */
export function AttendanceFinesTab() {
  const { can } = usePerm()
  const canEdit = can('finance.main', 'edit')
  const [month, setMonth] = useState(currentMonth())
  const [status, setStatus] = useState<StatusFilter>('')
  const [teacherId, setTeacherId] = useState('')
  const [tick, setTick] = useState(0)
  const { data, loading, error } = useAsync(() => getAttendanceFines({ month, status }), [month, status, tick])

  const [waiving, setWaiving] = useState<AttendanceFine | null>(null)
  const [dayOpen, setDayOpen] = useState(false)

  const teachers = useMemo(() => {
    const map = new Map<string, string>()
    for (const r of data?.rows ?? []) map.set(r.teacherId, r.teacherName)
    return [...map.entries()].sort((a, b) => a[1].localeCompare(b[1], 'uz'))
  }, [data])

  const rows = useMemo(
    () => (data?.rows ?? []).filter((r) => !teacherId || r.teacherId === teacherId),
    [data, teacherId],
  )
  const pg = usePagination(rows)

  const restore = async (f: AttendanceFine) => {
    if (!confirm(`${f.teacherName} — ${dmy(f.lessonDate)} jarimasi qayta tiklansinmi? U yana maoshdan ushlanadi.`)) return
    try {
      await restoreAttendanceFine(f.id)
      setTick((t) => t + 1)
    } catch (err) {
      alert(apiErrorMessage(err, "Jarimani tiklab bo'lmadi"))
    }
  }

  const selectCls =
    'h-9 rounded-lg border border-black/25 bg-white px-3 text-[13px] text-black outline-none hover:border-black/60 focus:border-brand-600'

  return (
    <div>
      <div className="mb-3 flex flex-wrap items-center gap-2">
        <div className="flex items-center gap-1">
          <button type="button" className={selectCls} onClick={() => setMonth((m) => shiftMonth(m, -1))} aria-label="Oldingi oy">
            ‹
          </button>
          <input
            type="month"
            value={month}
            onChange={(e) => e.target.value && setMonth(e.target.value)}
            className={cn(selectCls, 'w-40')}
            aria-label="Oy"
          />
          <button type="button" className={selectCls} onClick={() => setMonth((m) => shiftMonth(m, 1))} aria-label="Keyingi oy">
            ›
          </button>
        </div>
        <select value={teacherId} onChange={(e) => setTeacherId(e.target.value)} className={selectCls} aria-label="O'qituvchi">
          <option value="">Barcha o'qituvchilar</option>
          {teachers.map(([id, name]) => (
            <option key={id} value={id}>
              {name}
            </option>
          ))}
        </select>
        <div className="flex rounded-lg border border-[#dbe0e6] bg-white p-1">
          {STATUS_CHIPS.map((c) => (
            <button
              key={c.key}
              type="button"
              onClick={() => setStatus(c.key)}
              className={cn(
                'h-7 rounded-md px-3 text-[13px] font-medium transition-colors',
                status === c.key ? 'bg-brand-600 text-white' : 'text-[#333] hover:bg-[#f0f2f2]',
              )}
            >
              {c.label}
            </button>
          ))}
        </div>
        <div className="ml-auto flex items-center gap-2">
          {data && (
            <span className="rounded-lg bg-red-50 px-3 py-1.5 text-[13px] font-semibold text-red-700">
              Faol: {data.countActive} ta · {formatMoney(data.totalActive)}
            </span>
          )}
          {canEdit && (
            <Button variant="secondary" onClick={() => setDayOpen(true)}>
              Kunni bekor qilish
            </Button>
          )}
        </div>
      </div>

      {error && <p className="mb-2 text-[13px] text-red-600">{error}</p>}

      <DataTable
        rows={pg.paged}
        rowKey={(f) => f.id}
        loading={loading && !data}
        numbered
        offset={(pg.page - 1) * pg.pageSize}
        rowClassName={(f) => (f.status === 'waived' ? 'opacity-60' : undefined)}
        emptyHint="Davomat o'z vaqtida olinmagan dars bo'lsa jarima shu yerda paydo bo'ladi (Sozlamalar → O'quv → «Davomat intizomi»)."
        columns={[
          { key: 'teacher', header: "O'qituvchi", render: (f) => f.teacherName || '—' },
          { key: 'group', header: 'Guruh', render: (f) => f.groupName || '—' },
          { key: 'date', header: 'Dars', render: (f) => <span className="whitespace-nowrap">{dmy(f.lessonDate)} · {f.lessonStart}</span> },
          { key: 'deadline', header: 'Muddat', render: (f) => hm(f.deadline) },
          {
            key: 'taken',
            header: 'Davomat olindi',
            render: (f) => (f.attendanceTakenAt ? <span className="text-amber-700">{hm(f.attendanceTakenAt)}</span> : <span className="text-[#9ca3af]">olinmagan</span>),
          },
          { key: 'amount', header: 'Summa', align: 'right', render: (f) => formatMoney(f.amount) },
          {
            key: 'status',
            header: 'Holat',
            render: (f) =>
              f.status === 'active' ? (
                <span className="rounded-full bg-red-50 px-2 py-0.5 text-[12px] font-semibold text-red-700">Faol</span>
              ) : (
                <span
                  className="rounded-full bg-slate-100 px-2 py-0.5 text-[12px] font-semibold text-slate-600"
                  title={`${f.waivedByName ?? ''} — ${f.waiveReason ?? ''}`}
                >
                  Bekor qilingan
                </span>
              ),
          },
          {
            key: 'reason',
            header: 'Sabab',
            render: (f) => (f.status === 'waived' ? <span className="text-[12px] text-slate-600">{f.waiveReason}</span> : ''),
          },
          {
            key: 'actions',
            header: '',
            align: 'right',
            render: (f) =>
              !canEdit ? null : f.status === 'active' ? (
                <button type="button" className="text-[13px] font-medium text-red-600 hover:underline" onClick={() => setWaiving(f)}>
                  Bekor qilish
                </button>
              ) : (
                <button type="button" className="text-[13px] font-medium text-brand-600 hover:underline" onClick={() => restore(f)}>
                  Tiklash
                </button>
              ),
          },
        ]}
        footer={<TablePagination {...pg} />}
      />

      {waiving && (
        <WaiveModal
          title="Jarimani bekor qilish"
          message={`${waiving.teacherName}, ${waiving.groupName} — ${dmy(waiving.lessonDate)} (${formatMoney(waiving.amount)})`}
          onClose={() => setWaiving(null)}
          onConfirm={async (reason) => {
            await waiveAttendanceFine(waiving.id, reason)
            setWaiving(null)
            setTick((t) => t + 1)
          }}
        />
      )}
      {dayOpen && (
        <WaiveModal
          title="Kunni bekor qilish"
          message="Tanlangan kunning barcha faol davomat jarimalari bekor qilinadi (masalan bayram kuni)."
          withDate
          onClose={() => setDayOpen(false)}
          onConfirm={async (reason, date) => {
            const r = await waiveAttendanceFinesDay(date!, reason)
            setDayOpen(false)
            setTick((t) => t + 1)
            alert(`${r.waived} ta jarima bekor qilindi`)
          }}
        />
      )}
    </div>
  )
}

/** Sabab (va ixtiyoriy sana) so'raydigan oyna — xato oynada ko'rinadi, kiritilgan matn saqlanadi. */
function WaiveModal({
  title,
  message,
  withDate,
  onClose,
  onConfirm,
}: {
  title: string
  message: string
  withDate?: boolean
  onClose: () => void
  onConfirm: (reason: string, date?: string) => Promise<void>
}) {
  const [reason, setReason] = useState('')
  const [date, setDate] = useState(localIsoDate(new Date()))
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const submit = async () => {
    if (!reason.trim()) {
      setError('Sababni yozing')
      return
    }
    setBusy(true)
    setError(null)
    try {
      await onConfirm(reason.trim(), withDate ? date : undefined)
    } catch (err) {
      setError(apiErrorMessage(err, "Bekor qilib bo'lmadi"))
    } finally {
      setBusy(false)
    }
  }

  return (
    <Modal
      open
      onClose={onClose}
      title={title}
      footer={
        <>
          <Button variant="secondary" onClick={onClose} disabled={busy}>
            Yopish
          </Button>
          <Button onClick={submit} disabled={busy}>
            {busy ? 'Saqlanmoqda...' : 'Bekor qilish'}
          </Button>
        </>
      }
    >
      <div className="space-y-3">
        <p className="text-sm text-slate-600">{message}</p>
        {withDate && (
          <label className="block text-sm">
            <span className="mb-1 block text-slate-600">Sana</span>
            <input
              type="date"
              value={date}
              onChange={(e) => setDate(e.target.value)}
              className="h-9 w-48 rounded-lg border border-slate-200 px-3 text-sm outline-none focus:border-brand-400"
            />
          </label>
        )}
        <label className="block text-sm">
          <span className="mb-1 block text-slate-600">Sabab</span>
          <textarea
            value={reason}
            onChange={(e) => setReason(e.target.value)}
            maxLength={300}
            rows={3}
            className="w-full rounded-lg border border-slate-200 px-3 py-2 text-sm outline-none focus:border-brand-400"
            placeholder="Masalan: bayram kuni, dars o'tilmagan"
          />
        </label>
        {error && <p className="text-sm text-red-600">{error}</p>}
      </div>
    </Modal>
  )
}
