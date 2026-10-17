import { useEffect, useState } from 'react'
import { Check, RefreshCw, ShieldCheck, Undo2, X } from 'lucide-react'
import {
  approveExpense,
  cancelExpense,
  getExpenseApprovalSettings,
  getExpenseRequests,
  rejectExpense,
  saveExpenseApprovalThreshold,
  type ExpenseApprovalSettings,
  type ExpenseRequest,
  type ExpenseRequestStatus,
} from '@/api/services/financeOps'
import { Card } from '@/components/ui/Card'
import { Button } from '@/components/ui/Button'
import { Badge } from '@/components/ui/Badge'
import { Loader } from '@/components/ui/Loader'
import { Modal } from '@/components/ui/Modal'
import { MoneyInput } from '@/components/ui/NumberInput'
import { financeCategoryLabel } from '@/config/constants'
import { apiErrorMessage, cn, formatMoney } from '@/lib/utils'
import { toast } from '@/lib/toast'
import { cashboxOptionLabel, useCashboxOptions } from '@/hooks/useCashboxOptions'

const STATUS: Array<[ExpenseRequestStatus | '', string]> = [
  ['pending', 'Kutilmoqda'],
  ['approved', 'Tasdiqlangan'],
  ['rejected', 'Rad etilgan'],
  ['', 'Hammasi'],
]

const STATUS_BADGE: Record<ExpenseRequestStatus, { tone: 'amber' | 'green' | 'red' | 'default'; label: string }> = {
  pending: { tone: 'amber', label: 'Kutilmoqda' },
  approved: { tone: 'green', label: 'Tasdiqlangan' },
  rejected: { tone: 'red', label: 'Rad etilgan' },
  cancelled: { tone: 'default', label: 'Qaytarib olingan' },
}

/** "2026-10-17T09:30:00" → "17.10 09:30"; "2026-10-17" → "17.10.2026". */
const when = (iso: string | null) =>
  !iso ? '—' : iso.length > 10 ? `${iso.slice(8, 10)}.${iso.slice(5, 7)} ${iso.slice(11, 16)}` : `${iso.slice(8, 10)}.${iso.slice(5, 7)}.${iso.slice(0, 4)}`

/**
 * Moliya → «Chiqimlar tasdig'i» (SPEC-backlog-2026-10-17 F3 K2; egasining qarori: faqat katta summalar). Chegaradan katta
 * chiqim kiritilganda kassadan darhol chiqmaydi — shu ro'yxatga tushadi. Superadmin yoki «Chiqimlarni tasdiqlash»
 * ruxsatli mas'ul tasdiqlaydi (kiritgan odamning o'zi emas) — shundagina tranzaksiya yoziladi. Chegarani faqat
 * superadmin o'zgartiradi.
 */
export function ExpenseApprovalsTab() {
  const [status, setStatus] = useState<ExpenseRequestStatus | ''>('pending')
  const [rows, setRows] = useState<ExpenseRequest[] | null>(null)
  const [settings, setSettings] = useState<ExpenseApprovalSettings | null>(null)
  const [error, setError] = useState('')
  const [loading, setLoading] = useState(true)
  const [tick, setTick] = useState(0)
  const [busy, setBusy] = useState<string | null>(null)
  const [rejecting, setRejecting] = useState<ExpenseRequest | null>(null)
  const [approving, setApproving] = useState<ExpenseRequest | null>(null)
  const [threshold, setThreshold] = useState<number | null>(null)
  const [thresholdBusy, setThresholdBusy] = useState(false)
  const [thresholdError, setThresholdError] = useState('')

  useEffect(() => {
    let active = true
    // eslint-disable-next-line react-hooks/set-state-in-effect -- holat/yangilash o'zgarganda qayta yuklash (maqsadli)
    setLoading(true)
    Promise.all([getExpenseRequests(status || undefined), getExpenseApprovalSettings()])
      .then(([r, s]) => {
        if (!active) return
        setRows(r)
        setSettings(s)
        setThreshold((t) => t ?? s.threshold)
        setError('')
      })
      .catch((e) => { if (active) setError(apiErrorMessage(e, "Ro'yxatni yuklab bo'lmadi")) })
      .finally(() => { if (active) setLoading(false) })
    return () => { active = false }
  }, [status, tick])

  const run = async (id: string, fn: () => Promise<unknown>, ok: string, fail: string) => {
    if (busy) return
    setBusy(id)
    try {
      await fn()
      toast.success(ok)
      setTick((n) => n + 1)
    } catch (e) {
      alert(apiErrorMessage(e, fail))
    } finally {
      setBusy(null)
    }
  }

  const saveThreshold = async () => {
    setThresholdBusy(true)
    setThresholdError('')
    try {
      const s = await saveExpenseApprovalThreshold(threshold ?? 0)
      setSettings(s)
      setThreshold(s.threshold)
      toast.success('Chegara saqlandi', s.threshold > 0 ? formatMoney(s.threshold) : "Tasdiq o'chirildi")
    } catch (e) {
      setThresholdError(apiErrorMessage(e, "Chegarani saqlab bo'lmadi"))
    } finally {
      setThresholdBusy(false)
    }
  }

  return (
    <div className="space-y-4">
      <Card
        title={<span className="flex items-center gap-2"><ShieldCheck className="h-4 w-4 text-brand-600" /> Chiqimlar tasdig'i</span>}
        sub={settings
          ? settings.threshold > 0
            ? `${formatMoney(settings.threshold)} dan katta chiqim (maosh ham) mas'ul tasdiqlagach kassadan chiqadi. Kiritgan odam o'zi tasdiqlay olmaydi.`
            : "Tasdiq o'chiq — barcha chiqimlar darhol kassadan chiqadi. Yoqish uchun chegara kiriting."
          : undefined}
      >
        {settings?.canEditThreshold ? (
          <div className="flex flex-wrap items-end gap-2">
            <div className="w-56">
              <MoneyInput label="Tasdiq chegarasi (so'm, 0 — o'chiq)" value={threshold} onChange={setThreshold} />
            </div>
            <Button onClick={() => void saveThreshold()} disabled={thresholdBusy}>
              {thresholdBusy ? 'Saqlanmoqda...' : 'Saqlash'}
            </Button>
            {thresholdError && <p className="text-sm text-red-600">{thresholdError}</p>}
          </div>
        ) : (
          <p className="text-sm text-slate-500">
            {settings && !settings.canApprove
              ? "Sizda tasdiqlash ruxsati yo'q — so'rovlaringiz holatini shu yerda kuzatasiz."
              : 'Chegarani faqat superadmin o\'zgartiradi.'}
          </p>
        )}
      </Card>

      <div className="toolbar flex-wrap">
        <div className="flex gap-1">
          {STATUS.map(([k, label]) => (
            <button key={k || 'all'} type="button" onClick={() => setStatus(k)}
              className={cn('rounded-lg border px-3 py-1.5 text-sm font-medium transition-colors',
                status === k ? 'border-brand-500 bg-brand-50 text-brand-700' : 'border-slate-200 bg-white text-slate-600 hover:bg-slate-50')}>
              {label}
            </button>
          ))}
        </div>
        <Button variant="secondary" onClick={() => setTick((n) => n + 1)} disabled={loading}>
          <RefreshCw className={cn('h-4 w-4', loading && 'animate-spin')} /> Yangilash
        </Button>
      </div>

      {error && <p className="rounded-lg bg-red-50 px-3 py-2 text-sm text-red-600">{error}</p>}

      {!rows ? (error ? null : <Loader />) : (
        <Card tight>
          {rows.length === 0 ? (
            <p className="py-10 text-center text-sm text-slate-400">{status === 'pending' ? "Tasdiq kutayotgan chiqim yo'q" : "Ro'yxat bo'sh"}</p>
          ) : (
            <div className={cn('table-wrap', loading && 'opacity-60')}>
              <table className="table">
                <thead>
                  <tr>
                    <th>Kiritildi</th>
                    <th>Chiqim</th>
                    <th className="num">Summa</th>
                    <th>Holat</th>
                    <th className="text-right">Amal</th>
                  </tr>
                </thead>
                <tbody>
                  {rows.map((r) => (
                    <tr key={r.id}>
                      <td className="whitespace-nowrap">
                        <div className="text-slate-700">{when(r.createdAt)}</div>
                        <div className="text-xs text-slate-400">{r.createdBy ?? '—'}</div>
                      </td>
                      <td>
                        <div className="font-medium text-slate-700">
                          {r.typeName ?? financeCategoryLabel(r.category, 'expense')}
                          {r.salaryPayKind && <span className="ml-1 text-xs text-slate-400">({r.salaryPayKind === 'advance' ? 'avans' : 'yakuniy'})</span>}
                        </div>
                        <div className="text-xs text-slate-500">
                          {[r.teacherName ?? r.staffName, r.month, r.cashboxName, r.note].filter(Boolean).join(' · ') || '—'}
                        </div>
                      </td>
                      <td className="num font-mono font-semibold tabular-nums text-red-600">{formatMoney(r.amount)}</td>
                      <td>
                        <Badge tone={STATUS_BADGE[r.status].tone}>{STATUS_BADGE[r.status].label}</Badge>
                        {r.decidedBy && r.status !== 'pending' && (
                          <div className="mt-0.5 text-xs text-slate-400">{r.decidedBy} · {when(r.decidedAt)}</div>
                        )}
                        {r.decisionNote && <div className="text-xs text-slate-500">{r.decisionNote}</div>}
                      </td>
                      <td className="text-right">
                        <div className="inline-flex gap-1">
                          {r.canDecide && (
                            <>
                              <Button className="!px-2.5 !py-1 text-xs" disabled={busy === r.id} onClick={() => setApproving(r)}>
                                <Check className="h-4 w-4" /> Tasdiqlash
                              </Button>
                              <Button className="!px-2.5 !py-1 text-xs" variant="secondary" disabled={busy === r.id} onClick={() => setRejecting(r)}>
                                <X className="h-4 w-4" /> Rad etish
                              </Button>
                            </>
                          )}
                          {r.isMine && r.status === 'pending' && (
                            <Button className="!px-2.5 !py-1 text-xs" variant="secondary" disabled={busy === r.id}
                              onClick={() => void run(r.id, () => cancelExpense(r.id), 'Qaytarib olindi', "Qaytarib olib bo'lmadi")}>
                              <Undo2 className="h-4 w-4" /> Qaytarib olish
                            </Button>
                          )}
                        </div>
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          )}
        </Card>
      )}

      {approving && (
        <ApproveModal
          request={approving}
          onClose={() => setApproving(null)}
          onDone={() => { setApproving(null); setTick((n) => n + 1) }}
        />
      )}

      {rejecting && (
        <RejectModal
          request={rejecting}
          onClose={() => setRejecting(null)}
          onDone={() => { setRejecting(null); setTick((n) => n + 1) }}
        />
      )}
    </div>
  )
}

/**
 * Tasdiqlash — qaysi KASSADAN chiqishini ko'rsatadi va tanlashga imkon beradi: so'rovdagi kassa arxivlangan bo'lsa
 * server 400 qaytaradi, kassa tanlovisiz esa so'rovni faqat rad etish mumkin bo'lardi. Pul TASDIQ kuni chiqadi.
 */
function ApproveModal({ request, onClose, onDone }: { request: ExpenseRequest; onClose: () => void; onDone: () => void }) {
  const options = useCashboxOptions()
  const [picked, setPicked] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState('')
  const requestBoxActive = !!options?.items.some((o) => o.id === request.cashboxId)
  // Tanlanmagan bo'lsa: so'rovdagi kassa (faol bo'lsa), aks holda tasdiqlovchining standart kassasi.
  const cashboxId = picked ?? (requestBoxActive ? request.cashboxId ?? '' : options?.defaultId ?? options?.items[0]?.id ?? '')
  const submit = async () => {
    if (busy) return
    setBusy(true)
    setError('')
    try {
      await approveExpense(request.id, undefined, cashboxId || undefined)
      toast.success('Tasdiqlandi — kassadan chiqdi')
      onDone()
    } catch (e) {
      setError(apiErrorMessage(e, "Tasdiqlab bo'lmadi"))
    } finally {
      setBusy(false)
    }
  }
  return (
    <Modal
      open
      onClose={onClose}
      size="sm"
      title="Chiqimni tasdiqlash"
      footer={
        <>
          <Button variant="secondary" onClick={onClose} disabled={busy}>Bekor</Button>
          {/* Kassalar ro'yxati yuklanmasa ham tasdiqlash mumkin — so'rovdagi kassa ishlatiladi, server sababni aytadi. */}
          <Button onClick={() => void submit()} disabled={busy}>{busy ? 'Saqlanmoqda...' : 'Tasdiqlash'}</Button>
        </>
      }
    >
      <p className="mb-3 text-sm text-slate-600">
        <span className="font-semibold text-red-600">{formatMoney(request.amount)}</span> — {financeCategoryLabel(request.category)}
        {request.teacherName || request.staffName ? ` · ${request.teacherName ?? request.staffName}` : ''} · kiritdi: {request.createdBy}
      </p>
      <label className="mb-1 block text-xs font-semibold text-slate-500">Qaysi kassadan</label>
      {!options && <p className="mb-1 text-xs text-slate-400">Kassalar ro'yxati yuklanmadi — so'rovdagi kassa ishlatiladi.</p>}
      <select
        value={cashboxId}
        onChange={(e) => setPicked(e.target.value)}
        disabled={!options}
        className="h-10 w-full rounded-lg border border-slate-200 bg-white px-2 text-sm text-slate-700"
      >
        {(options?.items ?? []).map((o) => (
          <option key={o.id} value={o.id}>{cashboxOptionLabel(o)}</option>
        ))}
      </select>
      {request.cashboxName && !requestBoxActive && options && (
        <p className="mt-1 text-xs text-amber-600">So'rovdagi kassa («{request.cashboxName}») arxivlangan — boshqa kassani tanlang.</p>
      )}
      <p className="mt-2 text-xs text-slate-400">Pul tasdiqlangan kuni kassadan chiqadi (so'rov sanasi izohda qoladi).</p>
      {error && <p className="mt-2 text-sm text-red-600">{error}</p>}
    </Modal>
  )
}

/** Rad etish — sabab MAJBURIY; xato oynaning o'zida (forma yopilmaydi). */
function RejectModal({ request, onClose, onDone }: { request: ExpenseRequest; onClose: () => void; onDone: () => void }) {
  const [reason, setReason] = useState('')
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState('')
  const submit = async () => {
    if (!reason.trim() || busy) return
    setBusy(true)
    setError('')
    try {
      await rejectExpense(request.id, reason.trim())
      toast.success('Rad etildi')
      onDone()
    } catch (e) {
      setError(apiErrorMessage(e, "Rad etib bo'lmadi"))
    } finally {
      setBusy(false)
    }
  }
  return (
    <Modal
      open
      onClose={onClose}
      size="sm"
      title="Chiqimni rad etish"
      footer={
        <>
          <Button variant="secondary" onClick={onClose} disabled={busy}>Bekor</Button>
          <Button onClick={() => void submit()} disabled={busy || !reason.trim()}>{busy ? 'Saqlanmoqda...' : 'Rad etish'}</Button>
        </>
      }
    >
      <p className="mb-2 text-sm text-slate-600">{formatMoney(request.amount)} — {request.createdBy}</p>
      <textarea
        value={reason}
        onChange={(e) => setReason(e.target.value)}
        maxLength={300}
        rows={3}
        placeholder="Sabab (majburiy)"
        className="w-full rounded-lg border border-slate-200 px-3 py-2 text-sm outline-none focus:border-brand-400"
      />
      {error && <p className="mt-2 text-sm text-red-600">{error}</p>}
    </Modal>
  )
}
