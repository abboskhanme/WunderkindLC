import { useEffect, useMemo, useRef, useState } from 'react'
import { getTeachers } from '@/api/services/teachers'
import {
  createTeacherFine,
  getTeacherFines,
  restoreTeacherFine,
  waiveTeacherFine,
  type TeacherFine,
} from '@/api/services/teacherFines'
import { Button } from '@/components/ui/Button'
import { Input, Select } from '@/components/ui/Input'
import { Loader } from '@/components/ui/Loader'
import { Modal } from '@/components/ui/Modal'
import { MoneyInput } from '@/components/ui/NumberInput'
import { DataTable } from '@/components/ui/list/DataTable'
import { ListToolbar } from '@/components/ui/list/ListToolbar'
import { TotalPill } from '@/components/ui/list/TotalPill'
import { TablePagination, usePagination } from '@/components/ui/TablePagination'
import { formatMonth } from '@/config/constants'
import { currentMonth, shiftMonth } from '@/lib/month'
import { usePerm } from '@/lib/permissions'
import { apiErrorMessage, cn, formatDate, formatMoney } from '@/lib/utils'
import type { Teacher } from '@/types'

/** Jarima qo'yiladigan oylar: joriy + oldingi 5 ta (kelajak oyga jarima qo'yilmaydi — server ham rad etadi). */
const fineMonths = () => Array.from({ length: 6 }, (_, i) => shiftMonth(currentMonth(), -i))

/**
 * Moliya → «Jarima» → «Jarimalar» — o'qituvchiga QO'LDA jarima (2026-10-13, owner: "maoshdan ushlansin").
 * Jarima tanlangan oy MAOSHIDAN ushlanadi (o'qituvchi profili → Maosh, o'qituvchi kabineti — sababi bilan) va
 * KASSAGA TEGMAYDI. O'chirilmaydi — bekor qilinadi (sabab bilan) va qayta tiklanadi. Server — `TeacherFinesController`.
 */
export function TeacherFinesTab() {
  const { can } = usePerm()
  const canWrite = can('finance.main', 'create')
  const [rows, setRows] = useState<TeacherFine[] | null>(null)
  const [loadError, setLoadError] = useState<string | null>(null)
  const [tick, setTick] = useState(0)
  const [month, setMonth] = useState('')
  const [q, setQ] = useState('')
  const [adding, setAdding] = useState(false)
  const [waiving, setWaiving] = useState<TeacherFine | null>(null)
  const [rowError, setRowError] = useState<string | null>(null)

  useEffect(() => {
    let alive = true
    getTeacherFines(month ? { month } : {})
      .then((r) => alive && setRows(r))
      .catch((err: unknown) => alive && setLoadError(apiErrorMessage(err, "Jarimalarni yuklab bo'lmadi")))
    return () => {
      alive = false
    }
  }, [month, tick])

  const filtered = useMemo(() => {
    const needle = q.trim().toLowerCase()
    return (rows ?? []).filter(
      (f) => !needle || f.teacherName.toLowerCase().includes(needle) || f.reason.toLowerCase().includes(needle),
    )
  }, [rows, q])
  const activeSum = filtered.filter((f) => f.status === 'active').reduce((s, f) => s + f.amount, 0)
  const pg = usePagination(filtered)

  const restore = async (f: TeacherFine) => {
    setRowError(null)
    try {
      await restoreTeacherFine(f.id)
      setTick((t) => t + 1)
    } catch (err: unknown) {
      setRowError(apiErrorMessage(err, "Jarimani tiklab bo'lmadi"))
    }
  }

  if (loadError) return <p className="text-[13px] text-red-600">{loadError}</p>
  if (!rows) return <Loader className="min-h-[240px]" />

  return (
    <div>
      <p className="mb-2.5 rounded-lg bg-sky-50 px-3 py-2 text-[13px] text-sky-800">
        Jarima tanlangan oy <b>maoshidan ushlanadi</b> — kassadan pul chiqmaydi. O'qituvchi uni maosh sahifasida sababi
        bilan ko'radi.
      </p>
      <ListToolbar
        left={
          <div className="flex flex-wrap items-center gap-2">
            <select
              value={month}
              onChange={(e) => setMonth(e.target.value)}
              className="h-[34px] rounded-lg border border-black/25 bg-white px-2 text-[13px] outline-none focus:border-brand-600"
              aria-label="Oy"
            >
              <option value="">Barcha oylar</option>
              {fineMonths().map((m) => (
                <option key={m} value={m}>
                  {formatMonth(m)}
                </option>
              ))}
            </select>
            <input
              value={q}
              onChange={(e) => setQ(e.target.value)}
              placeholder="O'qituvchi yoki sabab"
              className="h-[34px] w-[220px] rounded-lg border border-black/25 bg-white px-3 text-[13px] outline-none focus:border-brand-600"
            />
          </div>
        }
        addLabel={canWrite ? "Jarima qo'shish" : undefined}
        onAdd={canWrite ? () => setAdding(true) : undefined}
      />

      {rowError && <p className="mb-2 rounded-lg bg-red-50 px-3 py-2 text-[13px] text-red-700">{rowError}</p>}

      <div className="mb-2 flex items-center justify-end gap-3 text-[13px]">
        <span className="text-[#6b7280]">
          Amaldagi jarimalar: <b className="font-mono text-red-600">−{formatMoney(activeSum)}</b>
        </span>
        <TotalPill total={filtered.length} />
      </div>

      <DataTable
        rows={pg.paged}
        rowKey={(f) => f.id}
        numbered
        offset={(pg.page - 1) * pg.pageSize}
        emptyHint="Jarima yo'q."
        rowClassName={(f) => (f.status === 'waived' ? 'opacity-60' : undefined)}
        columns={[
          { key: 'teacher', header: "O'qituvchi", render: (f) => f.teacherName || '—' },
          { key: 'month', header: 'Maosh oyi', render: (f) => <span className="whitespace-nowrap">{formatMonth(f.month)}</span> },
          {
            key: 'amount',
            header: 'Miqdori',
            align: 'right',
            render: (f) => (
              <span className={cn('font-mono', f.status === 'waived' ? 'line-through' : 'text-red-600')}>
                −{formatMoney(f.amount)}
              </span>
            ),
          },
          { key: 'reason', header: 'Sabab', render: (f) => <span className="block max-w-[320px]">{f.reason}</span> },
          {
            key: 'status',
            header: 'Holat',
            render: (f) =>
              f.status === 'active' ? (
                <span className="text-emerald-700">Amalda</span>
              ) : (
                <span className="text-[#6b7280]" title={f.waiveReason ?? undefined}>
                  Bekor qilingan{f.waivedByName ? ` — ${f.waivedByName}` : ''}
                </span>
              ),
          },
          {
            key: 'by',
            header: 'Kiritgan',
            render: (f) => (
              <span className="whitespace-nowrap text-[#555]">
                {f.createdByName || '—'} · {formatDate(f.createdAt)}
              </span>
            ),
          },
          ...(canWrite
            ? [
                {
                  key: 'act',
                  header: '',
                  render: (f: TeacherFine) =>
                    f.status === 'active' ? (
                      <button type="button" onClick={() => setWaiving(f)} className="text-[12px] font-semibold text-red-600 hover:underline">
                        Bekor qilish
                      </button>
                    ) : (
                      <button type="button" onClick={() => void restore(f)} className="text-[12px] font-semibold text-brand-600 hover:underline">
                        Tiklash
                      </button>
                    ),
                },
              ]
            : []),
        ]}
        footer={<TablePagination {...pg} />}
      />

      {adding && (
        <AddFineModal
          onClose={() => setAdding(false)}
          onSaved={() => {
            setAdding(false)
            setTick((t) => t + 1)
          }}
        />
      )}
      {waiving && (
        <WaiveFineModal
          fine={waiving}
          onClose={() => setWaiving(null)}
          onSaved={() => {
            setWaiving(null)
            setTick((t) => t + 1)
          }}
        />
      )}
    </div>
  )
}

function AddFineModal({ onClose, onSaved }: { onClose: () => void; onSaved: () => void }) {
  const [teachers, setTeachers] = useState<Teacher[]>([])
  const [teacherId, setTeacherId] = useState('')
  const [month, setMonth] = useState(currentMonth())
  const [amount, setAmount] = useState<number | null>(null)
  const [reason, setReason] = useState('')
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const inFlight = useRef(false)

  useEffect(() => {
    let alive = true
    getTeachers()
      .then((ts) => alive && setTeachers(ts.filter((t) => !t.isArchived).sort((a, b) => a.fullName.localeCompare(b.fullName))))
      .catch((err: unknown) => alive && setError(apiErrorMessage(err, "O'qituvchilar ro'yxatini yuklab bo'lmadi")))
    return () => {
      alive = false
    }
  }, [])

  const canSave = !!teacherId && !!month && (amount ?? 0) > 0 && reason.trim().length > 0 && !busy

  const save = async () => {
    if (!canSave || inFlight.current) return
    inFlight.current = true
    setBusy(true)
    setError(null)
    try {
      await createTeacherFine({ teacherId, month, amount: amount ?? 0, reason: reason.trim() })
      onSaved()
    } catch (err: unknown) {
      setError(apiErrorMessage(err, "Jarimani saqlab bo'lmadi"))
    } finally {
      inFlight.current = false
      setBusy(false)
    }
  }

  return (
    <Modal
      open
      onClose={onClose}
      title="Jarima qo'shish"
      footer={
        <>
          <Button variant="secondary" onClick={onClose}>
            Bekor qilish
          </Button>
          <Button disabled={!canSave} onClick={() => void save()}>
            {busy ? 'Saqlanmoqda…' : 'Saqlash'}
          </Button>
        </>
      }
    >
      <div className="space-y-3">
        <Select label="O'qituvchi" required value={teacherId} onChange={(e) => setTeacherId(e.target.value)}>
          <option value="">— o'qituvchini tanlang —</option>
          {teachers.map((t) => (
            <option key={t.id} value={t.id}>
              {t.fullName}
            </option>
          ))}
        </Select>
        <Select label="Qaysi oy maoshidan" required value={month} onChange={(e) => setMonth(e.target.value)}>
          {fineMonths().map((m) => (
            <option key={m} value={m}>
              {formatMonth(m)}
            </option>
          ))}
        </Select>
        <label className="block">
          <span className="mb-1 block text-[13px] font-semibold text-[#333]">
            Summa <span className="text-red-500">*</span>
          </span>
          <MoneyInput
            as="input"
            value={amount}
            onChange={setAmount}
            className="w-full min-h-[34px] rounded-lg border border-black/25 bg-white px-3 py-1.5 text-[13px] outline-none focus:border-brand-600"
          />
        </label>
        <Input label="Sabab" required value={reason} maxLength={500} onChange={(e) => setReason(e.target.value)} placeholder="Masalan: darsga kechikdi" />
        <p className="text-[12px] text-[#6b7280]">Jarima shu oy maoshidan ushlanadi, kassadan pul chiqmaydi.</p>
        {error && <p className="rounded-lg bg-red-50 px-3 py-2 text-[13px] text-red-700">{error}</p>}
      </div>
    </Modal>
  )
}

function WaiveFineModal({ fine, onClose, onSaved }: { fine: TeacherFine; onClose: () => void; onSaved: () => void }) {
  const [reason, setReason] = useState('')
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const inFlight = useRef(false)

  const save = async () => {
    if (!reason.trim() || inFlight.current) return
    inFlight.current = true
    setBusy(true)
    setError(null)
    try {
      await waiveTeacherFine(fine.id, reason.trim())
      onSaved()
    } catch (err: unknown) {
      setError(apiErrorMessage(err, "Jarimani bekor qilib bo'lmadi"))
    } finally {
      inFlight.current = false
      setBusy(false)
    }
  }

  return (
    <Modal
      open
      onClose={onClose}
      size="sm"
      title="Jarimani bekor qilish"
      footer={
        <>
          <Button variant="secondary" onClick={onClose}>
            Orqaga
          </Button>
          <Button variant="danger" disabled={!reason.trim() || busy} onClick={() => void save()}>
            {busy ? 'Saqlanmoqda…' : 'Bekor qilish'}
          </Button>
        </>
      }
    >
      <div className="space-y-3">
        <p className="text-[13px] text-[#333]">
          {fine.teacherName} — <b>−{formatMoney(fine.amount)}</b> ({formatMonth(fine.month)}): {fine.reason}
        </p>
        <Input label="Bekor qilish sababi" required value={reason} maxLength={300} onChange={(e) => setReason(e.target.value)} />
        <p className="text-[12px] text-[#6b7280]">Summa maoshga qaytadi. Yozuv o'chmaydi — kerak bo'lsa qayta tiklanadi.</p>
        {error && <p className="rounded-lg bg-red-50 px-3 py-2 text-[13px] text-red-700">{error}</p>}
      </div>
    </Modal>
  )
}
