import { useEffect, useMemo, useState } from 'react'
import { Link } from 'react-router-dom'
import {
  applyPaymentMonthRepairs,
  getPaymentMonthRepairs,
  type ApplyActivationChargesResult,
  type PaymentMonthRepairRow,
} from '@/api/services/billingRepair'
import { Modal } from '@/components/ui/Modal'
import { Button } from '@/components/ui/Button'
import { Loader } from '@/components/ui/Loader'
import { formatMonth } from '@/config/constants'
import { apiErrorMessage, formatMoney } from '@/lib/utils'

interface Props {
  onClose: () => void
  /** Tuzatilgach — sahifa ma'lumotlari yangilansin. */
  onApplied: () => void
}

/**
 * «TO'LOV OYINI TUZATISH» (FAQAT superadmin, 2026-10-23) — to'lov oynasi eng eski qarzdor oyni (masalan 1 darslik
 * sentabr) tanlab qo'ygan va kassir 225 000 kiritganda butun to'lov o'sha oyga yozilgan. Natijada keyingi oy
 * "to'lanmagan" ko'rinadi va o'qituvchi maoshida «Qarzdorlar to'lasa yana» shishadi, holbuki balans 0.
 *
 * ⚠️ Avval RO'YXAT (server hech narsa yozmaydi), keyin faqat BELGILANGANLAR tuzatiladi. Pul, balans va kassa
 * O'ZGARMAYDI — faqat to'lov "qaysi oy uchun" tegi (kerak bo'lsa to'lov oilaga bo'linadi). Avto-xabar yo'q.
 */
export function PaymentMonthRepairModal({ onClose, onApplied }: Props) {
  const [rows, setRows] = useState<PaymentMonthRepairRow[] | null>(null)
  const [error, setError] = useState('')
  const [selected, setSelected] = useState<Set<string>>(new Set())
  const [busy, setBusy] = useState(false)
  const [result, setResult] = useState<ApplyActivationChargesResult | null>(null)
  const [tick, setTick] = useState(0)

  useEffect(() => {
    let alive = true
    getPaymentMonthRepairs()
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
    () => (rows ?? []).filter((r) => selected.has(r.key)).reduce((sum, r) => sum + r.total, 0),
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
    if (!confirm(`${selected.size} ta qatorda jami ${formatMoney(total)} to'lov keyingi oy(lar)ga ko'chirilsinmi?`)) return
    setBusy(true)
    setError('')
    try {
      const r = await applyPaymentMonthRepairs([...selected])
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
      title="To'lov oyini tuzatish"
      footer={
        <>
          <Button variant="secondary" onClick={onClose} disabled={busy}>
            Yopish
          </Button>
          <Button onClick={apply} disabled={busy || selected.size === 0}>
            {busy ? 'Saqlanmoqda...' : `Tuzatish (${selected.size})`}
          </Button>
        </>
      }
    >
      <div className="space-y-3 text-sm">
        <p className="text-slate-600">
          Oyning qoldig'idan ko'p pul o'sha oyga yozilgan to'lovlar: ortig'i keyingi (joriy oygacha) to'lanmagan oyga
          ko'chiriladi. Pul, balans va kassa o'zgarmaydi — faqat to'lov qaysi oy uchun ekani. Shundan keyin guruhdagi
          "qarz oy" va o'qituvchi maoshidagi «Hozir chiqadigan» to'g'ri ko'rinadi.
        </p>
        {result && (
          <p className="rounded-lg bg-emerald-50 px-3 py-2 text-emerald-800">
            {result.applied} ta qatorda jami {formatMoney(result.total)} ko'chirildi
            {result.skipped > 0 && ` · ${result.skipped} tasi o'tkazib yuborildi (allaqachon tuzatilgan)`}.
            {result.errors.length > 0 && <span className="block text-red-700">{result.errors.join('; ')}</span>}
          </p>
        )}
        {error && <p className="rounded-lg bg-red-50 px-3 py-2 text-red-700">{error}</p>}
        {!rows ? (
          !error && <Loader label="Yuklanmoqda..." />
        ) : rows.length === 0 ? (
          <p className="text-slate-600">Tuzatadigan to'lov yo'q — barcha to'lovlar o'z oyida.</p>
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
                    <th className="px-2 py-2 font-medium">O'qituvchi</th>
                    <th className="px-2 py-2 font-medium">Ko'chiriladi</th>
                    <th className="px-2 py-2 text-right font-medium">Jami</th>
                  </tr>
                </thead>
                <tbody>
                  {rows.map((r) => (
                    <tr key={r.key} className="border-t border-slate-100 align-top">
                      <td className="px-2 py-1.5">
                        <input type="checkbox" checked={selected.has(r.key)} onChange={() => toggle(r.key)} />
                      </td>
                      <td className="px-2 py-1.5">
                        <Link to={`/admin/students/${r.studentId}`} className="text-brand-600 hover:underline">
                          {r.studentName}
                        </Link>
                      </td>
                      <td className="px-2 py-1.5">{r.groupName}</td>
                      <td className="px-2 py-1.5">{r.teacherName || '—'}</td>
                      <td className="px-2 py-1.5">
                        {r.moves.map((m) => (
                          <span key={`${m.fromMonth}:${m.toMonth}`} className="block">
                            {formatMonth(m.fromMonth)} → {formatMonth(m.toMonth)}: {formatMoney(m.amount)}
                          </span>
                        ))}
                        {r.notes.map((n) => (
                          <span key={n} className="block text-xs text-amber-700">
                            {n}
                          </span>
                        ))}
                      </td>
                      <td className="px-2 py-1.5 text-right font-medium">{formatMoney(r.total)}</td>
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
