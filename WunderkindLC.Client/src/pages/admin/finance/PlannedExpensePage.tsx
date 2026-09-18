import { useState } from 'react'
import { IconPencil, IconTrash } from '@tabler/icons-react'
import {
  createPlannedExpense,
  deletePlannedExpense,
  getPlannedExpenses,
  updatePlannedExpense,
  type PlannedExpense,
} from '@/api/services/finance'
import { DataTable } from '@/components/ui/list/DataTable'
import { ListToolbar } from '@/components/ui/list/ListToolbar'
import { TintedIconButton } from '@/components/ui/list/TintedIconButton'
import { TotalPill } from '@/components/ui/list/TotalPill'
import { TablePagination, usePagination } from '@/components/ui/TablePagination'
import { Loader } from '@/components/ui/Loader'
import { Modal } from '@/components/ui/Modal'
import { Button } from '@/components/ui/Button'
import { Input, Select, Textarea } from '@/components/ui/Input'
import { useAsync } from '@/hooks/useAsync'
import { expenseCategories, financeCategoryLabel } from '@/config/constants'
import { usePerm } from '@/lib/permissions'
import { apiErrorMessage, cn, formatMoney } from '@/lib/utils'

const STATUS: Record<string, { label: string; cls: string }> = {
  planned: { label: 'Rejada', cls: 'bg-[#eef2ff] text-brand-600' },
  paid: { label: "To'langan", cls: 'bg-[#e8f8ee] text-[#2e7d32]' },
  canceled: { label: 'Bekor qilindi', cls: 'bg-[#f0f2f2] text-[#6b7280]' },
}

function fmtDay(v: string): string {
  const d = (v ?? '').slice(0, 10)
  if (!d) return '—'
  const [y, m, dd] = d.split('-')
  return `${dd}.${m}.${y}`
}

/**
 * Moliya → «Rejalashtirilgan xarajatlar» (edutizim `/finance/planned-expense`): kutilayotgan
 * to'lovlar ro'yxati (ijara, oylik, soliq ...).
 *
 * ⚠️ REJA — pul HARAKATI EMAS: balans, kassa va moliya hisobotlariga KIRMAYDI. Pul chiqqanda
 * odatdagidek chiqim tranzaksiyasi kiritiladi, bu yerdagi qator esa "To'langan" deb belgilanadi.
 */
export function PlannedExpensePage() {
  const { can } = usePerm()
  const canEdit = can('finance.main', 'create')
  const [tick, setTick] = useState(0)
  const { data, loading, error } = useAsync(getPlannedExpenses, [tick])
  const [editing, setEditing] = useState<PlannedExpense | null>(null)
  const [open, setOpen] = useState(false)
  const [saving, setSaving] = useState(false)
  const [formError, setFormError] = useState<string | null>(null)

  const rows = data ?? []
  const pg = usePagination(rows)

  const openNew = () => {
    setEditing(null)
    setFormError(null)
    setOpen(true)
  }

  const openEdit = (row: PlannedExpense) => {
    setEditing(row)
    setFormError(null)
    setOpen(true)
  }

  const submit = async (e: React.FormEvent<HTMLFormElement>) => {
    e.preventDefault()
    const f = new FormData(e.currentTarget)
    const payload = {
      name: String(f.get('name') ?? '').trim(),
      amount: Number(f.get('amount') ?? 0),
      category: String(f.get('category') ?? 'other'),
      startDate: String(f.get('startDate') ?? ''),
      endDate: String(f.get('endDate') ?? ''),
      status: String(f.get('status') ?? 'planned'),
      note: String(f.get('note') ?? ''),
    }
    setSaving(true)
    setFormError(null)
    try {
      if (editing) await updatePlannedExpense(editing.id, payload)
      else await createPlannedExpense(payload)
      setOpen(false)
      setTick((t) => t + 1)
    } catch (err) {
      setFormError(apiErrorMessage(err, "Saqlab bo'lmadi"))
    } finally {
      setSaving(false)
    }
  }

  const remove = async (row: PlannedExpense) => {
    await deletePlannedExpense(row.id)
    setTick((t) => t + 1)
  }

  if (loading && !data) return <Loader className="min-h-[240px]" />
  if (error) return <p className="text-[13px] text-red-600">{error}</p>

  return (
    <div>
      <div className="mb-2.5 flex items-center justify-between">
        <h1 className="text-[18px] font-semibold text-black">Rejalashtirilgan xarajatlar</h1>
      </div>

      <ListToolbar addLabel={canEdit ? "Qo'shish" : undefined} onAdd={canEdit ? openNew : undefined} />

      <div className="mb-2 flex justify-end">
        <TotalPill total={rows.length} />
      </div>

      <DataTable
        rows={pg.paged}
        rowKey={(r) => r.id}
        numbered
        offset={(pg.page - 1) * pg.pageSize}
        columns={[
          { key: 'name', header: 'Nomi', render: (r) => <span className="font-medium">{r.name}</span> },
          { key: 'amount', header: 'Miqdori', align: 'right', render: (r) => formatMoney(r.amount) },
          { key: 'category', header: 'Turi', render: (r) => financeCategoryLabel(r.category, 'expense') },
          { key: 'start', header: 'Boshlanish sanasi', render: (r) => fmtDay(r.startDate) },
          { key: 'end', header: 'Tugash sanasi', render: (r) => fmtDay(r.endDate) },
          {
            key: 'status',
            header: 'Holati',
            render: (r) => {
              const s = STATUS[r.status] ?? STATUS.planned
              return <span className={cn('rounded-full px-2 py-0.5 text-[11px] font-semibold', s.cls)}>{s.label}</span>
            },
          },
          {
            key: 'actions',
            header: '',
            align: 'right',
            render: (r) =>
              canEdit ? (
                <div className="flex justify-end gap-1.5">
                  <TintedIconButton label="Tahrirlash" onClick={() => openEdit(r)}>
                    <IconPencil className="h-4 w-4" />
                  </TintedIconButton>
                  <TintedIconButton
                    label="O'chirish"
                    className="border-[#e34a29]/20 bg-[#e34a29]/10 text-[#e34a29] hover:bg-[#e34a29]/15"
                    onClick={() => remove(r)}
                  >
                    <IconTrash className="h-4 w-4" />
                  </TintedIconButton>
                </div>
              ) : null,
          },
        ]}
        footer={<TablePagination {...pg} />}
      />

      {open && (
        <Modal
          open={open}
          onClose={() => setOpen(false)}
          title={editing ? 'Rejani tahrirlash' : "Rejalashtirilgan xarajat qo'shish"}
        >
          <form onSubmit={submit} className="space-y-3">
            <Input name="name" label="Nomi" required defaultValue={editing?.name ?? ''} />
            <div className="grid grid-cols-2 gap-3">
              <Input name="amount" label="Miqdori" type="number" min="0" required defaultValue={editing?.amount ?? ''} />
              <Select name="category" label="Turi" defaultValue={editing?.category ?? 'other'}>
                {expenseCategories.map((c) => (
                  <option key={c.value} value={c.value}>
                    {c.label}
                  </option>
                ))}
              </Select>
            </div>
            <div className="grid grid-cols-2 gap-3">
              <Input name="startDate" label="Boshlanish sanasi" type="date" defaultValue={editing?.startDate ?? ''} />
              <Input name="endDate" label="Tugash sanasi" type="date" defaultValue={editing?.endDate ?? ''} />
            </div>
            <Select name="status" label="Holati" defaultValue={editing?.status ?? 'planned'}>
              {Object.entries(STATUS).map(([value, s]) => (
                <option key={value} value={value}>
                  {s.label}
                </option>
              ))}
            </Select>
            <Textarea name="note" label="Izoh" rows={2} defaultValue={editing?.note ?? ''} />
            {formError && <p className="text-[13px] text-red-600">{formError}</p>}
            <div className="flex justify-end gap-2 pt-1">
              <Button type="button" variant="secondary" onClick={() => setOpen(false)}>
                Orqaga
              </Button>
              <Button type="submit" disabled={saving}>
                {saving ? 'Saqlanmoqda…' : 'Saqlash'}
              </Button>
            </div>
          </form>
        </Modal>
      )}
    </div>
  )
}
