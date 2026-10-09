import { useEffect, useMemo, useState } from 'react'
import { Link } from 'react-router-dom'
import {
  applyActivationCharges,
  getMissingActivationCharges,
  type ApplyActivationChargesResult,
  type MissingActivationCharge,
} from '@/api/services/billingRepair'
import { Modal } from '@/components/ui/Modal'
import { Button } from '@/components/ui/Button'
import { Loader } from '@/components/ui/Loader'
import { formatMonth } from '@/config/constants'
import { apiErrorMessage, formatDate, formatMoney } from '@/lib/utils'

interface Props {
  onClose: () => void
  /** Hisob yozilgach — sahifa ma'lumotlari yangilansin. */
  onApplied: () => void
}

/**
 * «QO'SHILGAN OY HISOBINI TUZATISH» (FAQAT superadmin) — aktiv qilib qo'shilganda (Excel importi)
 * qo'shilgan oyi hisoblanmay, balansi 0 bo'lib qolgan o'quvchilar (mijoz, 2026-10-09).
 *
 * ⚠️ Avval RO'YXAT (server hech narsa yozmaydi), keyin faqat BELGILANGANLAR tuzatiladi. Summa —
 * «Aktivlashtirish» oynasidagi AYNAN o'sha qisman hisob (sanadan qolgan darslar, chegirma bilan).
 * Ota-onalarga avto-xabar ketmaydi. Qamrov — joriy va o'tgan oy (eski tarixga orqaga qarz yozilmaydi).
 */
export function ActivationChargeRepairModal({ onClose, onApplied }: Props) {
  const [rows, setRows] = useState<MissingActivationCharge[] | null>(null)
  const [error, setError] = useState('')
  const [selected, setSelected] = useState<Set<string>>(new Set())
  const [busy, setBusy] = useState(false)
  const [result, setResult] = useState<ApplyActivationChargesResult | null>(null)
  const [tick, setTick] = useState(0)

  useEffect(() => {
    let alive = true
    getMissingActivationCharges()
      .then((r) => {
        if (!alive) return
        setRows(r)
        // ⚠️ Hech narsa oldindan BELGILANMAYDI — har qator ongli tanlanadi (yoki «Hammasini belgilash»).
        setSelected(new Set())
      })
      .catch((e) => {
        if (alive) setError(apiErrorMessage(e, "Ro'yxatni yuklab bo'lmadi"))
      })
    return () => {
      alive = false
    }
  }, [tick])

  const total = useMemo(
    () => (rows ?? []).filter((r) => selected.has(r.key)).reduce((sum, r) => sum + r.amount, 0),
    [rows, selected],
  )

  const toggle = (key: string) =>
    setSelected((prev) => {
      const next = new Set(prev)
      if (next.has(key)) next.delete(key)
      else next.add(key)
      return next
    })

  const apply = async () => {
    if (busy || selected.size === 0) return
    if (!confirm(`${selected.size} ta o'quvchiga jami ${formatMoney(total)} qarz yozilsinmi?`)) return
    setBusy(true)
    setError('')
    try {
      const r = await applyActivationCharges([...selected])
      setResult(r)
      onApplied()
      setRows(null)
      setTick((t) => t + 1)
    } catch (e) {
      setError(apiErrorMessage(e, "Tuzatib bo'lmadi"))
    } finally {
      setBusy(false)
    }
  }

  return (
    <Modal
      open
      size="xl"
      onClose={() => !busy && onClose()}
      title="Qo'shilgan oy hisobini tuzatish"
      footer={
        <>
          <Button variant="secondary" onClick={onClose} disabled={busy}>
            Yopish
          </Button>
          <Button onClick={apply} disabled={busy || selected.size === 0}>
            {busy ? 'Yozilmoqda...' : `Tuzatish (${selected.size})`}
          </Button>
        </>
      }
    >
      <div className="space-y-3 text-sm">
        <p className="text-slate-600">
          Aktiv qilib qo'shilganda qo'shilgan oyi hisoblanmay qolgan o'quvchilar (joriy va o'tgan oy). Har biriga
          «Aktivlashtirish» oynasidagidek qisman hisob yoziladi: aktivlashtirilgan sanadan oy oxirigacha qolgan
          darslar, chegirma bilan. Ota-onalarga xabar ketmaydi.
        </p>
        {result && (
          <p className="rounded-lg bg-emerald-50 px-3 py-2 text-emerald-800">
            {result.applied} ta o'quvchiga jami {formatMoney(result.total)} yozildi
            {result.skipped > 0 && ` · ${result.skipped} tasi o'tkazib yuborildi (allaqachon hisoblangan)`}.
            {result.errors.length > 0 && <span className="block text-red-700">{result.errors.join('; ')}</span>}
          </p>
        )}
        {error && <p className="rounded-lg bg-red-50 px-3 py-2 text-red-700">{error}</p>}
        {!rows ? (
          !error && <Loader label="Yuklanmoqda..." />
        ) : rows.length === 0 ? (
          <p className="text-slate-600">Tuzatadigan o'quvchi yo'q — barcha aktiv a'zoliklarning qo'shilgan oyi hisoblangan.</p>
        ) : (
          <>
            <div className="flex items-center justify-between">
              <label className="inline-flex items-center gap-2">
                <input
                  type="checkbox"
                  checked={selected.size === rows.length}
                  onChange={(e) => setSelected(e.target.checked ? new Set(rows.map((r) => r.key)) : new Set())}
                />
                Hammasini belgilash
              </label>
              <span>
                Belgilangan: <b>{selected.size}</b> · jami <b>{formatMoney(total)}</b>
              </span>
            </div>
            <div className="max-h-[50vh] overflow-auto rounded-lg border border-slate-200">
              <table className="w-full text-left">
                <thead className="sticky top-0 bg-slate-50 text-xs text-slate-500">
                  <tr>
                    <th className="w-8 px-2 py-2" />
                    <th className="px-2 py-2 font-medium">O'quvchi</th>
                    <th className="px-2 py-2 font-medium">Guruh</th>
                    <th className="px-2 py-2 font-medium">Aktiv sanasi</th>
                    <th className="px-2 py-2 font-medium">Oy</th>
                    <th className="px-2 py-2 text-right font-medium">Yoziladi</th>
                    <th className="px-2 py-2 text-right font-medium">Hozirgi balans</th>
                  </tr>
                </thead>
                <tbody>
                  {rows.map((r) => (
                    <tr key={r.key} className="border-t border-slate-100">
                      <td className="px-2 py-1.5">
                        <input type="checkbox" checked={selected.has(r.key)} onChange={() => toggle(r.key)} />
                      </td>
                      <td className="px-2 py-1.5">
                        <Link to={`/admin/students/${r.studentId}`} className="text-brand-600 hover:underline">
                          {r.studentName}
                        </Link>
                      </td>
                      <td className="px-2 py-1.5">{r.groupName}</td>
                      <td className="px-2 py-1.5">{formatDate(r.activatedAt)}</td>
                      <td className="px-2 py-1.5">{formatMonth(r.month)}</td>
                      <td className="px-2 py-1.5 text-right font-medium text-red-600">−{formatMoney(r.amount)}</td>
                      <td className="px-2 py-1.5 text-right">{formatMoney(r.balance)}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          </>
        )}
      </div>
    </Modal>
  )
}
