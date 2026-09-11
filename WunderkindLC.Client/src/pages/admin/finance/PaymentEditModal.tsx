import { useEffect, useState } from 'react'
import type { FormEvent } from 'react'
import { AlertTriangle } from 'lucide-react'
import type { FinanceTransaction, MonthLedger, StudentGroupMembership } from '@/types'
import { updatePayment, type PaymentEditPayload } from '@/api/services/finance'
import { getStudentLedger, receiptDuplicateOf, type DuplicateReceipt } from '@/api/services/students'
import { getStudentGroups } from '@/api/services/classes'
import { Modal } from '@/components/ui/Modal'
import { Button } from '@/components/ui/Button'
import { Input, Select, Textarea } from '@/components/ui/Input'
import { Loader } from '@/components/ui/Loader'
import { formatMonth, monthStatusLabels, paymentMethods } from '@/config/constants'
import { apiErrorMessage, formatDate, formatMoney, cn } from '@/lib/utils'

interface Props {
  /** Tahrirlanadigan o'quvchi to'lovi (income + tuition) */
  payment: FinanceTransaction | null
  onClose: () => void
  onSaved: () => void
}

const today = () => new Date().toISOString().slice(0, 10)

/**
 * "To'lovlar" bo'limidagi kiritilgan to'lovni tahrirlash — FAQAT superadmin.
 * O'quvchi almashtirilmaydi (buning uchun to'lov o'chirilib qaytadan kiritiladi).
 * Saqlanganda server: balansni summa farqiga moslaydi, yangi (guruh, oy) hisobini ochadi, audit yozadi.
 */
export function PaymentEditModal({ payment, onClose, onSaved }: Props) {
  const [form, setForm] = useState<PaymentEditPayload | null>(null)
  const [months, setMonths] = useState<MonthLedger[]>([])
  const [groups, setGroups] = useState<StudentGroupMembership[]>([])
  const [loading, setLoading] = useState(false)
  const [saving, setSaving] = useState(false)
  const [error, setError] = useState<string | null>(null)
  /** Kvitansiya raqami BOSHQA to'lovda band — server 409 bilan qaytargan yozuv. */
  const [duplicate, setDuplicate] = useState<DuplicateReceipt | null>(null)

  useEffect(() => {
    if (!payment) return
    // eslint-disable-next-line react-hooks/set-state-in-effect -- modal ochilganda formani to'lov bilan sinxronlash (maqsadli)
    setError(null)
    setLoading(true)
    setForm({
      date: payment.date,
      amount: payment.amount,
      month: payment.month ?? payment.date.slice(0, 7),
      groupId: payment.groupId ?? '',
      method: payment.method ?? 'cash',
      comment: payment.comment ?? '',
      receiptNo: payment.receiptNo ?? '',
      paidTime: payment.paidTime ?? '',
      cardLast4: payment.cardLast4 ?? '',
    })
    const sid = payment.studentId
    if (!sid) {
      setLoading(false)
      return
    }
    Promise.all([
      getStudentLedger(sid).then((l) => l.months).catch(() => [] as MonthLedger[]),
      getStudentGroups(sid).catch(() => [] as StudentGroupMembership[]),
    ])
      .then(([ms, gs]) => {
        setMonths(ms)
        setGroups(gs)
      })
      .finally(() => setLoading(false))
  }, [payment])

  const set = <K extends keyof PaymentEditPayload>(key: K, value: PaymentEditPayload[K]) =>
    setForm((f) => (f ? { ...f, [key]: value } : f))

  /** Saqlash. `force=true` — kvitansiya raqami boshqa to'lovda band bo'lsa ham ("Baribir saqlash"). */
  const save = async (force: boolean) => {
    if (!payment || !form || form.amount <= 0 || !form.date || !form.month) return
    setSaving(true)
    setError(null)
    try {
      await updatePayment(payment.id, {
        ...form,
        groupId: form.groupId || undefined,
        comment: form.comment?.trim() || undefined,
        // Kvitansiya faqat naqdda, vaqt faqat kartada saqlanadi (usul o'zgarsa eskisi tozalanadi).
        receiptNo: (form.method ?? 'cash') === 'cash' ? form.receiptNo?.trim() || '' : '',
        paidTime: (form.method ?? 'cash') === 'card' ? form.paidTime || '' : '',
        cardLast4: (form.method ?? 'cash') === 'card' ? form.cardLast4 || '' : '',
        forceReceipt: force,
      })
      setDuplicate(null)
      onSaved()
      onClose()
    } catch (err) {
      // Kvitansiya raqami boshqa to'lovda band — qaysi to'lov ekani kartochka bo'lib chiqadi.
      const dup = receiptDuplicateOf(err)
      if (dup) setDuplicate(dup)
      else setError(apiErrorMessage(err, "To'lovni saqlab bo'lmadi"))
    } finally {
      setSaving(false)
    }
  }

  const submit = (e: FormEvent) => {
    e.preventDefault()
    void save(false)
  }

  // Oy tanlovi: o'quvchi hisob oylari + joriy to'lov oyi (ro'yxatda bo'lmasa ham yo'qolmasin).
  const monthOptions = (() => {
    const list = months.map((m) => m.month)
    if (form?.month && !list.includes(form.month)) list.push(form.month)
    return list.sort()
  })()

  const diff = payment && form ? form.amount - payment.amount : 0

  return (
    <Modal
      open={!!payment}
      onClose={onClose}
      title="To'lovni tahrirlash"
      footer={
        <>
          <Button variant="secondary" onClick={onClose} disabled={saving}>
            Bekor qilish
          </Button>
          {duplicate ? (
            <Button variant="danger" disabled={saving} onClick={() => void save(true)}>
              {saving ? 'Saqlanmoqda...' : 'Baribir saqlash'}
            </Button>
          ) : (
            <Button
              type="submit"
              form="payment-edit-form"
              disabled={saving || loading || !form || form.amount <= 0 || !form.month}
            >
              {saving ? 'Saqlanmoqda...' : 'Saqlash'}
            </Button>
          )}
        </>
      }
    >
      {loading || !form || !payment ? (
        <Loader label="Yuklanmoqda..." />
      ) : (
        <form id="payment-edit-form" onSubmit={submit} className="space-y-4">
          {/* Kvitansiya raqami BOSHQA to'lovda band — qaysi to'lov ekani ko'rsatiladi. */}
          {duplicate && (
            <div className="rounded-lg border border-amber-300 bg-amber-50 px-3 py-2.5 text-[13px]">
              <p className="font-semibold text-amber-800">
                {duplicate.receiptNo} raqami boshqa to'lovda band
              </p>
              <p className="mt-1 text-slate-700">
                {duplicate.studentName || '—'}
                {duplicate.groupName ? ` · ${duplicate.groupName}` : ''}
                {duplicate.teacherName ? ` · ${duplicate.teacherName}` : ''}
              </p>
              <p className="mt-0.5 text-slate-700">
                <span className="font-mono font-semibold text-emerald-700">{formatMoney(duplicate.amount)}</span>
                {` · ${formatDate(duplicate.date)}`}
                {duplicate.createdBy ? ` · kiritgan: ${duplicate.createdBy}` : ''}
              </p>
            </div>
          )}
          {/* O'quvchi — o'zgartirilmaydi */}
          <div className="rounded-lg bg-slate-50 px-3 py-2.5">
            <p className="text-xs text-slate-400">O'quvchi</p>
            <p className="font-semibold text-slate-800">{payment.studentName ?? '—'}</p>
            <p className="mt-0.5 text-xs text-slate-400">
              O'quvchini almashtirish uchun to'lovni o'chirib, qaytadan kiriting.
            </p>
          </div>

          <div className="grid grid-cols-2 gap-4">
            <Input
              label="Summa (so'm)"
              type="number"
              min={0}
              step="any"
              required
              value={form.amount}
              onChange={(e) => set('amount', Number(e.target.value))}
            />
            <Input
              label="Sana"
              type="date"
              required
              max={today()}
              value={form.date}
              onChange={(e) => set('date', e.target.value)}
            />
          </div>

          <div className="grid grid-cols-2 gap-4">
            <div>
              <label className="mb-1 block text-sm font-medium text-slate-600">Qaysi oy uchun</label>
              <select
                value={form.month}
                onChange={(e) => set('month', e.target.value)}
                className="w-full rounded-lg border border-slate-200 bg-white px-3 py-2 text-sm text-slate-700 outline-none focus:border-brand-400"
              >
                {monthOptions.map((mo) => {
                  const m = months.find((x) => x.month === mo)
                  const suffix = m
                    ? m.remaining > 0
                      ? ` — ${monthStatusLabels[m.status]} (qoldiq ${formatMoney(m.remaining)})`
                      : ` — ${monthStatusLabels[m.status]}`
                    : ''
                  return (
                    <option key={mo} value={mo}>
                      {formatMonth(mo)}
                      {suffix}
                    </option>
                  )
                })}
              </select>
            </div>
            <Select
              label="Guruh"
              value={form.groupId ?? ''}
              onChange={(e) => set('groupId', e.target.value)}
            >
              <option value="">— guruhsiz —</option>
              {groups.map((g) => (
                <option key={g.groupId} value={g.groupId}>
                  {g.groupName}
                </option>
              ))}
            </Select>
          </div>

          <div>
            <label className="mb-1 block text-sm font-medium text-slate-600">To'lov usuli</label>
            <div className="grid grid-cols-3 gap-2">
              {paymentMethods.map((m) => (
                <button
                  key={m.value}
                  type="button"
                  onClick={() => set('method', m.value)}
                  className={cn(
                    'rounded-lg border px-3 py-2 text-sm font-medium transition-colors',
                    (form.method ?? 'cash') === m.value
                      ? 'border-brand-400 bg-brand-50 text-brand-700'
                      : 'border-slate-200 text-slate-600 hover:bg-slate-50',
                  )}
                >
                  {m.label}
                </button>
              ))}
            </div>

            {/* Naqd — qog'oz kvitansiya raqami; karta — pul o'tkazilgan vaqt (to'lov oynasidagi kabi). */}
            {(form.method ?? 'cash') === 'cash' && (
              <div className="mt-3">
                <label className="mb-1 block text-sm font-medium text-slate-600">Kvitansiya raqami</label>
                <div className="flex items-stretch">
                  <span className="flex select-none items-center rounded-l-lg border border-r-0 border-slate-200 bg-slate-50 px-3 text-sm font-semibold tracking-wide text-slate-500">
                    KV
                  </span>
                  <input
                    type="text"
                    inputMode="numeric"
                    value={(form.receiptNo ?? '').replace(/^KV/i, '')}
                    onChange={(e) => set('receiptNo', e.target.value.replace(/\s+/g, ''))}
                    placeholder="000123"
                    maxLength={20}
                    className="w-full rounded-r-lg border border-slate-200 bg-white px-3 py-2 font-mono text-sm text-slate-700 outline-none focus:border-brand-400"
                  />
                </div>
              </div>
            )}
            {(form.method ?? 'cash') === 'card' && (
              <>
                <div className="mt-3">
                  <label className="mb-1 block text-sm font-medium text-slate-600">
                    Karta raqami (oxirgi 4 raqam)
                  </label>
                  <div className="flex items-stretch">
                    <span className="flex select-none items-center rounded-l-lg border border-r-0 border-slate-200 bg-slate-50 px-3 font-mono text-sm tracking-widest text-slate-400">
                      ••••
                    </span>
                    <input
                      type="text"
                      inputMode="numeric"
                      value={form.cardLast4 ?? ''}
                      // Faqat raqam va faqat OXIRGI 4 tasi (to'liq karta raqami saqlanmaydi).
                      onChange={(e) => set('cardLast4', e.target.value.replace(/\D/g, '').slice(-4))}
                      placeholder="1234"
                      maxLength={4}
                      className="w-full rounded-r-lg border border-slate-200 bg-white px-3 py-2 font-mono text-sm tracking-widest text-slate-700 outline-none focus:border-brand-400"
                    />
                  </div>
                </div>
                <div className="mt-3">
                  <Input
                    label="To'lov vaqti"
                    type="time"
                    value={form.paidTime ?? ''}
                    onChange={(e) => set('paidTime', e.target.value)}
                  />
                </div>
              </>
            )}
          </div>

          <Textarea
            label="Izoh (kassir)"
            rows={2}
            value={form.comment ?? ''}
            onChange={(e) => set('comment', e.target.value)}
          />

          {/* Summa o'zgarsa — balansga qanday ta'sir qilishini oldindan ko'rsatamiz */}
          {diff !== 0 && (
            <div className="flex items-start gap-2.5 rounded-lg bg-amber-50 px-3 py-2.5 text-xs text-amber-800">
              <AlertTriangle className="mt-0.5 h-4 w-4 shrink-0" />
              <p>
                O'quvchi balansi{' '}
                <b className="font-mono">
                  {diff > 0 ? `+${formatMoney(diff)}` : `−${formatMoney(-diff)}`}
                </b>{' '}
                ga o'zgaradi. Qarz/avans, guruh tushumi va o'qituvchining foizli maoshi shu zahoti qayta
                hisoblanadi.
              </p>
            </div>
          )}

          {error && <p className="text-sm font-medium text-red-600">{error}</p>}
        </form>
      )}
    </Modal>
  )
}
