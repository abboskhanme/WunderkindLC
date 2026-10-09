import { useEffect, useMemo, useState } from 'react'
import type { StudentLedger } from '@/types'
import { editStudentCharge, getStudentLedger, type EditChargeResult } from '@/api/services/students'
import { Modal } from '@/components/ui/Modal'
import { Button } from '@/components/ui/Button'
import { Input, Select } from '@/components/ui/Input'
import { MoneyInput } from '@/components/ui/NumberInput'
import { Loader } from '@/components/ui/Loader'
import { formatMonth } from '@/config/constants'
import { apiErrorMessage, cn, formatMoney } from '@/lib/utils'

interface Props {
  studentId: string
  studentName: string
  onClose: () => void
  /** Saqlangach — profil balansi/tarixi qayta yuklanadi. */
  onSaved: () => void
}

/** Tanlov kaliti — oy + guruh (ko'p guruhli o'quvchida har guruh ulushi alohida). */
const keyOf = (month: string, groupId?: string | null) => `${month}|${groupId ?? ''}`

/**
 * «BALANSNI TUZATISH» (FAQAT superadmin) — mijoz talabi (2026-10-09): bitta guruhda har xil narxda
 * o'qiydiganlar (masalan koreys tili: Topik 1 va Grammatika) hisobini to'g'rilash.
 *
 * ⚠️ Balans TO'G'RIDAN-TO'G'RI yozilmaydi: u oylik hisoblar va to'lovlardan hisoblanadi (per-guruh balans,
 * maosh foizi, qarzdorlar ro'yxati — hammasi shu hisob qatorlariga tayanadi). Shuning uchun tuzatish
 * tanlangan OYNING HISOBINI o'zgartiradi (`PUT /students/{id}/charges/{month}`) — balans shundan o'zi
 * chiqadi va hech qayerda "ikki xil raqam" paydo bo'lmaydi. Yangi balans saqlashdan OLDIN serverdan
 * hisoblab ko'rsatiladi (`preview`) — chegirma yangi summadan qayta hisoblanadi, uni klientda takrorlamaymiz.
 * Tuzatilgan oy qulflanadi: avtomatik qayta hisob unga boshqa tegmaydi. KEYINGI oylar avvalgidek guruh
 * narxida yoziladi.
 */
export function BalanceAdjustModal({ studentId, studentName, onClose, onSaved }: Props) {
  const [ledger, setLedger] = useState<StudentLedger | null>(null)
  const [loadError, setLoadError] = useState('')
  const [selected, setSelected] = useState('')
  const [amount, setAmount] = useState<number | null>(null)
  const [note, setNote] = useState('')
  const [preview, setPreview] = useState<EditChargeResult | null>(null)
  const [previewFor, setPreviewFor] = useState('')
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState('')

  useEffect(() => {
    let alive = true
    getStudentLedger(studentId)
      .then((l) => {
        if (alive) setLedger(l)
      })
      .catch((e) => {
        if (alive) setLoadError(apiErrorMessage(e, "Hisoblarni yuklab bo'lmadi"))
      })
    return () => {
      alive = false
    }
  }, [studentId])

  /** Hisob qatorlari — eng yangi oy tepada; har oy ichida guruh ulushlari alohida. */
  const rows = useMemo(() => {
    if (!ledger) return []
    return [...ledger.months]
      .sort((a, b) => b.month.localeCompare(a.month))
      .flatMap((m) =>
        m.courses.map((c) => ({
          key: keyOf(m.month, c.groupId),
          month: m.month,
          groupId: c.groupId ?? null,
          label: `${formatMonth(m.month)} — ${c.groupName || c.courseName || 'guruhsiz'}`,
          fee: c.fee,
        })),
      )
  }, [ledger])

  const row = rows.find((r) => r.key === selected) ?? rows[0]
  const current = `${row?.key}|${amount ?? ''}`
  const ready = !!preview && previewFor === current

  const run = async (save: boolean) => {
    if (!row || busy) return
    if (amount === null) {
      setError('Yangi summani kiriting')
      return
    }
    setBusy(true)
    setError('')
    try {
      const r = await editStudentCharge(studentId, row.month, amount, row.groupId ?? undefined, {
        note: note.trim(),
        preview: !save,
      })
      if (save) {
        onSaved()
        return
      }
      setPreview(r ?? null)
      setPreviewFor(current)
    } catch (e) {
      setError(apiErrorMessage(e, save ? "Saqlab bo'lmadi" : "Hisoblab bo'lmadi"))
    } finally {
      setBusy(false)
    }
  }

  return (
    <Modal
      open
      size="lg"
      onClose={() => !busy && onClose()}
      title={`Balansni tuzatish — ${studentName}`}
      footer={
        <>
          <Button variant="secondary" onClick={onClose} disabled={busy}>
            Bekor qilish
          </Button>
          {ready ? (
            <Button onClick={() => run(true)} disabled={busy}>
              {busy ? 'Saqlanmoqda...' : 'Saqlash'}
            </Button>
          ) : (
            <Button onClick={() => run(false)} disabled={busy || !row}>
              {busy ? 'Hisoblanmoqda...' : 'Hisoblash'}
            </Button>
          )}
        </>
      }
    >
      {loadError ? (
        <p className="rounded-lg bg-red-50 px-3 py-2 text-sm text-red-700">{loadError}</p>
      ) : !ledger ? (
        <Loader label="Yuklanmoqda..." />
      ) : rows.length === 0 ? (
        <p className="text-sm text-slate-600">
          Bu o'quvchida hali oylik hisob yo'q — tuzatadigan narsa yo'q. Qo'shilgan oy hisoblanmagan bo'lsa,
          guruhdagi «Aktivlashtirish sanasini o'zgartirish» dan foydalaning.
        </p>
      ) : (
        <div className="space-y-3 text-sm">
          <p className="text-slate-600">
            Joriy balans: <b>{formatMoney(ledger.balance)}</b>. Balans oylik hisoblar va to'lovlardan
            hisoblanadi — shuning uchun tanlangan oyning <b>hisob summasi</b> o'zgartiriladi (masalan guruh
            narxi 225 000, o'quvchi esa boshqa tarifda o'qiydi). Keyingi oylar avvalgidek guruh narxida yoziladi.
          </p>
          <div className="grid grid-cols-1 gap-3 md:grid-cols-2">
            <Select
              label="Oy va guruh"
              value={row?.key ?? ''}
              onChange={(e) => {
                setSelected(e.target.value)
                setAmount(null)
                setPreview(null)
              }}
            >
              {rows.map((r) => (
                <option key={r.key} value={r.key}>
                  {r.label} · {formatMoney(r.fee)}
                </option>
              ))}
            </Select>
            <MoneyInput
              label="Shu oy uchun hisob (yangi summa)"
              value={amount}
              placeholder={row ? String(row.fee) : ''}
              onChange={setAmount}
            />
          </div>
          <Input
            label="Izoh (tarixga yoziladi)"
            value={note}
            maxLength={300}
            placeholder="Masalan: Topik 1 tarifi"
            onChange={(e) => setNote(e.target.value)}
          />
          {error && <p className="rounded-lg bg-red-50 px-3 py-2 text-red-700">{error}</p>}
          {ready && preview && (
            <div className="rounded-lg border border-slate-200 bg-slate-50 p-3">
              <p>
                Hisob: {formatMoney(preview.oldAmount)} → <b>{formatMoney(preview.newAmount)}</b>
                {preview.oldDiscount !== preview.newDiscount && (
                  <> · chegirma {formatMoney(preview.oldDiscount)} → {formatMoney(preview.newDiscount)}</>
                )}
              </p>
              <p className="mt-1">
                Balans: {formatMoney(preview.balanceBefore)} →{' '}
                <b className={cn(preview.balanceAfter < 0 ? 'text-red-600' : 'text-emerald-700')}>
                  {formatMoney(preview.balanceAfter)}
                </b>
              </p>
            </div>
          )}
        </div>
      )}
    </Modal>
  )
}
