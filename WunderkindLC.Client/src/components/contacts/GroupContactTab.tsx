import { useEffect, useMemo, useState } from 'react'
import { PhoneCall, CheckCircle2, Search } from 'lucide-react'
import type { ContactBulkResult } from '@/api/services/contacts'
import { Button } from '@/components/ui/Button'
import { Loader } from '@/components/ui/Loader'
import { apiErrorMessage, cn } from '@/lib/utils'

/** Tabdagi o'quvchi (jurnal ro'yxatidan keladi). */
export interface ContactTabStudent {
  id: string
  fullName: string
  /** active | trial | frozen — qatorda belgi bo'lib chiqadi. */
  status?: string
}

const statusBadge: Record<string, { label: string; cls: string }> = {
  active: { label: 'Aktiv', cls: 'bg-emerald-100 text-emerald-700' },
  trial: { label: 'Sinov', cls: 'bg-amber-100 text-amber-700' },
  frozen: { label: 'Muzlatilgan', cls: 'bg-slate-200 text-slate-600' },
}

/**
 * GURUH JURNALIDAGI "ALOQA" TABI — o'quvchini "Bog'lanish kerak" navbatiga yuborish.
 *
 * <p>Ikkala tomonda ham ishlatiladi: admin guruh sahifasi va O'QITUVCHI ilovasi. API farqi
 * `loadReasons`/`onSend` orqali tashqaridan beriladi, ko'rinish va qoidalar esa BITTA joyda.</p>
 *
 * <p>SANA SO'RALMAYDI — talab darhol navbatga tushadi (bugungi ish). Sabab va izoh
 * tanlanganlarning HAMMASIGA bir xil qo'yiladi; bitta o'quvchini qatordagi tugma bilan
 * ham yuborsa bo'ladi.</p>
 *
 * <p>⚠️ MUZLATILGAN o'quvchilar ro'yxatda KO'RINMAYDI. Jurnal ro'yxati ularni ham qaytaradi
 * (alohida blokda), lekin ular darsga qatnamayapti — navbatga yuborish uchun nishon emas.
 * Filtr shu yerda (komponent ichida): chaqiruv joyi ikkita (admin va o'qituvchi jurnali),
 * ikkalasida ham qoida bir xil bo'lishi kerak va unutilib qolmasligi shart.
 * SINOVDAGILAR QOLADI — aynan ular bilan bog'lanish ko'p kerak bo'ladi.</p>
 */
export function GroupContactTab({
  students,
  loading,
  loadReasons,
  onSend,
}: {
  students: ContactTabStudent[]
  loading?: boolean
  loadReasons: () => Promise<{ id: string; label: string }[]>
  onSend: (studentIds: string[], reasonId: string, note: string) => Promise<ContactBulkResult>
}) {
  const [reasons, setReasons] = useState<{ id: string; label: string }[]>([])
  const [reasonId, setReasonId] = useState('')
  const [note, setNote] = useState('')
  const [selected, setSelected] = useState<Set<string>>(new Set())
  const [term, setTerm] = useState('')
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState('')
  const [result, setResult] = useState<ContactBulkResult | null>(null)

  useEffect(() => {
    loadReasons().then(setReasons).catch(() => setReasons([]))
    // eslint-disable-next-line react-hooks/exhaustive-deps -- bir marta yuklanadi
  }, [])

  const filtered = useMemo(() => {
    // Muzlatilganlar chiqarib tashlanadi (yuqoridagi izohga qarang).
    const list = students.filter((s) => s.status !== 'frozen')
    const q = term.trim().toLowerCase()
    return q ? list.filter((s) => s.fullName.toLowerCase().includes(q)) : list
  }, [students, term])

  /**
   * SABAB va IZOH — MAJBURIY. Ilgari qatordagi telefon tugmasi ular bo'sh bo'lsa ham
   * yuborardi va navbatga "sababsiz, izohsiz" talab tushardi: operator nima uchun
   * qo'ng'iroq qilayotganini bilmasdi.
   */
  const ready = reasonId !== '' && note.trim().length > 0

  const allSelected = filtered.length > 0 && filtered.every((s) => selected.has(s.id))

  const toggle = (id: string) =>
    setSelected((prev) => {
      const next = new Set(prev)
      if (next.has(id)) next.delete(id)
      else next.add(id)
      return next
    })

  const toggleAll = () =>
    setSelected((prev) => {
      const next = new Set(prev)
      if (allSelected) filtered.forEach((s) => next.delete(s.id))
      else filtered.forEach((s) => next.add(s.id))
      return next
    })

  const send = async (ids: string[]) => {
    if (busy || ids.length === 0) return
    // Tugmalar allaqachon o'chirilgan, lekin qo'shimcha to'siq — qoida bitta joyda tursin.
    if (!ready) {
      setError(reasonId === '' ? 'Sababni tanlang' : 'Izohni yozing')
      return
    }
    setBusy(true)
    setError('')
    setResult(null)
    try {
      const r = await onSend(ids, reasonId, note.trim())
      setResult(r)
      // Yuborilganlar tanlovdan chiqadi — ikki marta bosib yuborilmasin.
      setSelected((prev) => {
        const next = new Set(prev)
        ids.forEach((id) => next.delete(id))
        return next
      })
    } catch (e) {
      setError(apiErrorMessage(e, "Navbatga yuborib bo'lmadi"))
    } finally {
      setBusy(false)
    }
  }

  if (loading) return <Loader label="Yuklanmoqda..." />

  return (
    <div className="space-y-4">
      {/* Sabab + izoh — tanlanganlarning HAMMASIGA bir xil qo'yiladi. */}
      <div className="grid gap-3 sm:grid-cols-2">
        <div>
          <label className="mb-1 block text-sm font-medium text-slate-600">
            Sabab <span className="text-red-500">*</span>
          </label>
          <select
            value={reasonId}
            onChange={(e) => setReasonId(e.target.value)}
            className="w-full rounded-lg border border-slate-200 bg-white px-3 py-2 text-sm text-slate-700 outline-none focus:border-brand-400"
          >
            <option value="">— Tanlanmagan —</option>
            {reasons.map((r) => (
              <option key={r.id} value={r.id}>
                {r.label}
              </option>
            ))}
          </select>
          {reasons.length === 0 && (
            <p className="mt-1 text-xs text-amber-700">
              Sabablar ro'yxati bo'sh — Sozlamalar → Sabablar da "Bog'lanish kerak" kategoriyasiga
              sabab qo'shilishi kerak.
            </p>
          )}
        </div>
        <div>
          <label className="mb-1 block text-sm font-medium text-slate-600">
            Izoh <span className="text-red-500">*</span>
          </label>
          <input
            value={note}
            onChange={(e) => setNote(e.target.value)}
            maxLength={2000}
            placeholder="Masalan: darsga 3 marta kelmadi"
            className="w-full rounded-lg border border-slate-200 px-3 py-2 text-sm text-slate-700 outline-none focus:border-brand-400"
          />
        </div>
      </div>

      <p className="rounded-lg bg-sky-50 px-3 py-2 text-xs leading-relaxed text-sky-800">
        Tanlangan o'quvchilar <strong>bugungi sana bilan</strong> "Bog'lanish kerak" navbatiga
        tushadi — sana tanlash shart emas. Navbatda kim yuborgani ko'rinib turadi.
        Muzlatilgan o'quvchilar bu ro'yxatda ko'rinmaydi.
        <strong className="ml-1">Sabab va izoh majburiy</strong> — operator nima uchun
        qo'ng'iroq qilayotganini bilishi kerak.
      </p>

      {/* Amal paneli */}
      <div className="flex flex-wrap items-center gap-2">
        <label className="inline-flex cursor-pointer items-center gap-2 text-sm text-slate-600">
          <input
            type="checkbox"
            checked={allSelected}
            onChange={toggleAll}
            className="h-4 w-4 rounded border-slate-300 accent-brand-600"
          />
          Hammasini tanlash
        </label>
        <Button
          onClick={() => send([...selected])}
          disabled={busy || selected.size === 0 || !ready}
          title={!ready ? "Avval sabab va izohni to'ldiring" : undefined}
        >
          <PhoneCall className="h-4 w-4" />
          {busy ? 'Yuborilmoqda...' : `Navbatga yuborish (${selected.size})`}
        </Button>
        {!ready && (
          <span className="text-xs text-amber-700">Sabab va izoh to'ldirilishi kerak</span>
        )}
        <div className="relative ml-auto">
          <Search className="pointer-events-none absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-slate-400" />
          <input
            value={term}
            onChange={(e) => setTerm(e.target.value)}
            placeholder="O'quvchi qidirish"
            className="min-w-[180px] rounded-lg border border-slate-200 py-2 pl-9 pr-3 text-sm outline-none focus:border-brand-400"
          />
        </div>
      </div>

      {error && <p className="rounded-lg bg-red-50 px-3 py-2 text-sm text-red-600">{error}</p>}

      {result && (
        <div className="space-y-1 rounded-lg bg-emerald-50 px-3 py-2.5 text-sm text-emerald-800">
          <p className="flex items-start gap-2">
            <CheckCircle2 className="mt-0.5 h-4 w-4 shrink-0" />
            <span>
              <strong>{result.created} ta</strong> o'quvchi navbatga yuborildi.
            </span>
          </p>
          {result.skipped > 0 && (
            <p className="text-amber-800">
              {result.skipped} tasida allaqachon ochiq talab bor — qayta yuborilmadi
              {result.skippedNames.length > 0 && `: ${result.skippedNames.join(', ')}`}
              {result.skipped > result.skippedNames.length && ' va boshqalar'}
            </p>
          )}
          {result.notFound > 0 && (
            <p className="text-slate-600">{result.notFound} ta o'quvchi topilmadi.</p>
          )}
        </div>
      )}

      {/* O'quvchilar ro'yxati */}
      {filtered.length === 0 ? (
        <p className="py-8 text-center text-sm text-slate-400">
          {term ? 'Hech kim topilmadi' : "Guruhda o'quvchi yo'q"}
        </p>
      ) : (
        <ul className="divide-y divide-slate-100 rounded-lg border border-slate-100">
          {filtered.map((s) => {
            const b = s.status ? statusBadge[s.status] : undefined
            return (
              <li key={s.id} className="flex items-center gap-3 px-3 py-2.5">
                <input
                  type="checkbox"
                  checked={selected.has(s.id)}
                  onChange={() => toggle(s.id)}
                  className="h-4 w-4 shrink-0 rounded border-slate-300 accent-brand-600"
                />
                <span className="min-w-0 flex-1 truncate text-sm font-medium text-slate-700">
                  {s.fullName}
                </span>
                {b && (
                  <span className={cn('shrink-0 rounded px-1.5 py-0.5 text-[11px] font-semibold', b.cls)}>
                    {b.label}
                  </span>
                )}
                {/* BITTA o'quvchini darhol yuborish — tanlab o'tirmasdan. */}
                <Button
                  variant="ghost"
                  disabled={busy || !ready}
                  onClick={() => send([s.id])}
                  title={
                    ready
                      ? "Shu o'quvchini navbatga yuborish"
                      : "Avval sabab va izohni to'ldiring"
                  }
                >
                  <PhoneCall className="h-4 w-4" />
                </Button>
              </li>
            )
          })}
        </ul>
      )}
    </div>
  )
}
