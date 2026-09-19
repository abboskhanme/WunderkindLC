import { useState } from 'react'
import { IconPencil, IconPlus, IconTrash } from '@tabler/icons-react'
import {
  createTransactionType,
  deleteTransactionType,
  getTransactionTypes,
  updateTransactionType,
  type TransactionType,
} from '@/api/services/finance'
import { Button } from '@/components/ui/Button'
import { Input, Select } from '@/components/ui/Input'
import { Loader } from '@/components/ui/Loader'
import { Modal } from '@/components/ui/Modal'
import { TintedIconButton } from '@/components/ui/list/TintedIconButton'
import { useAsync } from '@/hooks/useAsync'
import { expenseCategories, incomeCategories } from '@/config/constants'
import { usePerm } from '@/lib/permissions'
import { apiErrorMessage, cn } from '@/lib/utils'

/**
 * Moliya → «Tranzaksiya turi» (edutizim `/finance/payment-type`): markaz o'z turlarini
 * qo'shadi, tahrirlaydi va o'chiradi. To'rtta bo'lim — Kirim · Chiqim · Vaucher · Jarima.
 *
 * ⚠️ **TUR HISOB-KITOBNI O'ZGARTIRMAYDI.** Erkin nom («Arenda», «Kanstovar») mavjud TIZIM
 * toifasiga bog'lanadi (`baseCategory`: `rent`, `supplies` ...) va butun moliya mantig'i —
 * maosh foizi, P&L, «Jarima» sahifasi — avvalgidek o'sha kodga qaraydi. Shuning uchun formada
 * «Hisobda nima sifatida yuradi» maydoni bor va u MAJBURIY.
 *
 * ⚠️ Yo'nalish (kirim/chiqim) alohida SO'RALMAYDI — u bo'limdan kelib chiqadi (vaucher = kirim,
 * jarima = chiqim). Aks holda «chiqim bo'limidagi kirim turi» kabi zid yozuv paydo bo'lardi.
 */

const KINDS = [
  { key: 'kirim', label: 'Kirim', cls: 'bg-[#e8f8ee] text-[#2e7d32]' },
  { key: 'chiqim', label: 'Chiqim', cls: 'bg-[#fdeceb] text-[#de4141]' },
  { key: 'vaucher', label: 'Vaucher', cls: 'bg-[#eef2ff] text-brand-600' },
  { key: 'jarima', label: 'Jarima', cls: 'bg-[#fff4e5] text-[#b26a00]' },
] as const

/** Bo'lim → pul yo'nalishi. Serverdagi `TransactionTypeCatalog.DirectionOf` bilan BIR XIL. */
const directionOf = (kind: string) => (kind === 'chiqim' || kind === 'jarima' ? 'expense' : 'income')

const baseOptions = (kind: string) =>
  directionOf(kind) === 'expense' ? expenseCategories : incomeCategories

export function TransactionTypesPage() {
  const { can } = usePerm()
  const canEdit = can('finance.main', 'create')
  const [tick, setTick] = useState(0)
  const { data, loading, error } = useAsync(getTransactionTypes, [tick])
  const [kind, setKind] = useState<string>('kirim')
  const [editing, setEditing] = useState<TransactionType | null>(null)
  const [open, setOpen] = useState(false)
  const [formKind, setFormKind] = useState('kirim')
  const [saving, setSaving] = useState(false)
  const [formError, setFormError] = useState<string | null>(null)
  const [rowError, setRowError] = useState<string | null>(null)

  const rows = (data ?? []).filter((t) => t.kind === kind)
  const countOf = (k: string) => (data ?? []).filter((t) => t.kind === k).length

  const openNew = () => {
    setEditing(null)
    setFormKind(kind)
    setFormError(null)
    setOpen(true)
  }

  const openEdit = (row: TransactionType) => {
    setEditing(row)
    setFormKind(row.kind)
    setFormError(null)
    setOpen(true)
  }

  const submit = async (e: React.FormEvent<HTMLFormElement>) => {
    e.preventDefault()
    const f = new FormData(e.currentTarget)
    const payload = {
      name: String(f.get('name') ?? '').trim(),
      kind: String(f.get('kind') ?? 'kirim'),
      baseCategory: String(f.get('baseCategory') ?? 'other'),
    }
    setSaving(true)
    setFormError(null)
    try {
      if (editing) await updateTransactionType(editing.id, payload)
      else await createTransactionType(payload)
      setOpen(false)
      setKind(payload.kind)
      setTick((t) => t + 1)
    } catch (err) {
      setFormError(apiErrorMessage(err, "Saqlab bo'lmadi"))
    } finally {
      setSaving(false)
    }
  }

  const remove = async (row: TransactionType) => {
    setRowError(null)
    try {
      await deleteTransactionType(row.id)
      setTick((t) => t + 1)
    } catch (err) {
      // Ishlatilgan turni server rad etadi (nechta amalda ekani bilan) — sabab ekranda qolsin.
      setRowError(apiErrorMessage(err, "O'chirib bo'lmadi"))
    }
  }

  if (loading && !data) return <Loader className="min-h-[240px]" />
  if (error) return <p className="text-[13px] text-red-600">{error}</p>

  return (
    <div className="rounded-xl border border-[#dbe0e6] bg-white p-4 shadow-[0_1px_2px_rgba(0,0,0,0.05)]">
      <div className="mb-3 flex flex-wrap items-center justify-between gap-2">
        <h1 className="text-[16px] font-semibold text-black">Tranzaksiya turi</h1>
        {canEdit && (
          <Button onClick={openNew}>
            <IconPlus className="mr-1 h-4 w-4" /> Tranzaksiya turini qo'shish
          </Button>
        )}
      </div>

      <div className="mb-4 flex flex-wrap items-center gap-2">
        {KINDS.map((k) => (
          <button
            key={k.key}
            type="button"
            onClick={() => setKind(k.key)}
            className={cn(
              'rounded-lg px-3 py-1.5 text-[13px] font-semibold transition-opacity',
              k.cls,
              kind !== k.key && 'opacity-55 hover:opacity-100',
            )}
          >
            {k.label} <span className="opacity-70">{countOf(k.key)}</span>
          </button>
        ))}
      </div>

      {rowError && <p className="mb-2 text-[13px] text-red-600">{rowError}</p>}

      {rows.length === 0 ? (
        <p className="py-8 text-center text-[13px] text-[#6b7280]">Bu bo'limda tur yo'q.</p>
      ) : (
        <ul className="divide-y divide-[#dbe0e6] rounded-lg border border-[#dbe0e6]">
          {rows.map((r) => (
            <li key={r.id} className="flex items-center gap-3 px-3 py-2.5">
              <span className="flex-1 text-[13px] font-medium text-[#333]">{r.name}</span>
              <span className="hidden text-[12px] text-[#6b7280] sm:block">
                {directionOf(r.kind) === 'income' ? 'Kirim' : 'Chiqim'} ·{' '}
                {baseOptions(r.kind).find((c) => c.value === r.baseCategory)?.label ?? r.baseCategory}
              </span>
              {canEdit && (
                <span className="flex gap-1.5">
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
                </span>
              )}
            </li>
          ))}
        </ul>
      )}

      <p className="mt-3 text-[12px] text-[#6b7280]">
        Tur nomini o'zingiz yozasiz, «Hisobda nima sifatida yuradi» esa uni tizim toifasiga
        bog'laydi — maosh, jarima va hisobotlar o'shanga qarab hisoblanadi.
      </p>

      {open && (
        <Modal
          open={open}
          onClose={() => setOpen(false)}
          title={editing ? 'Turni tahrirlash' : "Tranzaksiya turini qo'shish"}
        >
          <form onSubmit={submit} className="space-y-3">
            <Input name="name" label="Nomi" required defaultValue={editing?.name ?? ''} autoFocus />
            <Select
              name="kind"
              label="Bo'lim"
              value={formKind}
              onChange={(e) => setFormKind(e.target.value)}
            >
              {KINDS.map((k) => (
                <option key={k.key} value={k.key}>
                  {k.label}
                </option>
              ))}
            </Select>
            <Select
              name="baseCategory"
              label="Hisobda nima sifatida yuradi"
              // ⚠️ `key` — bo'lim almashganda ro'yxat ham, TANLANGAN qiymat ham yangilanishi kerak
              // (kirim toifasi chiqim ro'yxatida qolib ketmasin).
              key={formKind}
              defaultValue={
                editing && editing.kind === formKind
                  ? editing.baseCategory
                  : formKind === 'jarima'
                    ? 'penalty'
                    : baseOptions(formKind)[0].value
              }
            >
              {baseOptions(formKind).map((c) => (
                <option key={c.value} value={c.value}>
                  {c.label}
                </option>
              ))}
            </Select>
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
