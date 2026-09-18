import { useEffect, useRef, useState } from 'react'
import { AlertCircle, Check, Pencil, RefreshCw, Users, Wallet, X } from 'lucide-react'
import type { MonthLedger, MonthStatus, Student, StudentLedger } from '@/types'
import { getStudentLedger, editStudentCharge, getStudent, addPayment } from '@/api/services/students'
import { useAuth } from '@/context/auth-context'
import { usePerm } from '@/lib/permissions'
import { Button } from '@/components/ui/Button'
import { Loader } from '@/components/ui/Loader'
import { AuditHistoryList } from '@/components/audit/AuditHistoryList'
import { PaymentModal } from './PaymentModal'
import { ReceiptModal } from '@/components/finance/ReceiptModal'
import { formatDate, formatMoney, cn, apiErrorMessage } from '@/lib/utils'
import { groupsText, statesToGroups } from '@/lib/studentGroups'
import { formatMonth, monthStatusLabels, paymentMethodLabel } from '@/config/constants'
import { DataTable } from '@/components/ui/list/DataTable'
import { TotalPill } from '@/components/ui/list/TotalPill'
import { joinDateTimeBar } from './profile/model'

interface Props {
  studentId: string
  /** To'lov shu panel ichidan kiritilgach — chaqiruvchi o'z holatini (balans/ro'yxat) yangilashi uchun. */
  onPaid?: () => void
  /**
   * Oylik hisob QO'LDA tahrirlangach chaqiriladi.
   *
   * ⚠️ `onPaid` DAN ALOHIDA prop kerak edi: u faqat TO'LOVDA chaqiriladi, tahrirda esa boshqa
   * narsa eskiradi. Server yangi summadan **chegirmani QAYTA HISOBLAYDI**
   * (`.claude/rules/discounts.md` §8.5), ya'ni tahrirdan keyin «Chegirma» tabidagi jadval
   * eskirib qoladi — u sahifada bir marta yuklanadi va o'zi qayta so'ramaydi.
   */
  onChargeEdited?: () => void
  /**
   * `profile` — o'quvchi profilidagi «Tranzaksiyalar tarixi» tabi: edutizim (DataGrid) jadvallari,
   * o'zgarishlar tarixi (audit) CHIZILMAYDI — u profilning «Harakatlar tarixi» tabida.
   * Standart (`panel`) — avvalgi ko'rinish (`PaymentHistoryModal`).
   */
  variant?: 'panel' | 'profile'
  /**
   * O'zgarsa hisob QAYTA so'raladi. Profil tablari yashirin holda TIRIK turadi (qayta ochilganda
   * so'ralmaydi), shuning uchun panel TASHQARIDAN kiritilgan to'lov/chegirmani shu orqali biladi.
   */
  refreshKey?: number
}

const statusStyles: Record<MonthStatus, string> = {
  paid: 'bg-emerald-50 text-emerald-700',
  partial: 'bg-amber-50 text-amber-700',
  unpaid: 'bg-red-50 text-red-700',
}

/** O'quvchining to'lov tarixi — oylar bo'yicha holat, kassa yozuvlari, o'zgarishlar tarixi.
 *  `PaymentHistoryModal` (modal ichida) va `StudentDetailPage`ning "To'lov tarixi" tabida (inline) ishlatiladi. */
export function PaymentHistoryPanel({ studentId, onPaid, onChargeEdited, variant = 'panel', refreshKey = 0 }: Props) {
  const { user } = useAuth()
  // O'zgarishlar tarixi — alohida `audit` ruxsati (admin/superadmin uchun har doim true).
  const canSeeAudit = usePerm().can('audit', 'view')
  const isSuper = user?.role === 'superadmin'
  const [ledger, setLedger] = useState<StudentLedger | null>(null)
  const [loading, setLoading] = useState(false)
  /**
   * YUKLASH XATOSI — ALOHIDA holat. ⚠️ Ilgari `.catch` umuman yo'q edi va shart
   * `loading || !ledger` bo'lgani uchun har qanday nosozlik MANGU "Yuklanmoqda..." bo'lib
   * qolardi: foydalanuvchi na sababni ko'rar, na qayta urina olardi.
   */
  const [error, setError] = useState('')
  /** "Qayta urinish" — hisobni qaytadan so'rash uchun hisoblagich. */
  const [tick, setTick] = useState(0)
  /** Kechikib kelgan javob yangisini bosib ketmasin (o'quvchi almashganda). */
  const reqRef = useRef(0)
  /** Hisoblangan summani qo'lda tahrirlash (faqat super admin) — har (oy, guruh) bo'yicha alohida.
   *  Kalit: `${month}|${groupId ?? ''}` — ko'p guruhli o'quvchida har guruh ulushi alohida tahrirlanadi. */
  const [editKey, setEditKey] = useState<string | null>(null)
  const [editVal, setEditVal] = useState('')
  const [saving, setSaving] = useState(false)
  /** "To'lov qilish" bosilganda — PaymentModal uchun to'liq Student kerak. */
  const [payTarget, setPayTarget] = useState<Student | null>(null)
  /** To'lov cheki — to'lov kiritilgach shu tranzaksiya cheki ochiladi. */
  const [receiptTx, setReceiptTx] = useState<string | null>(null)
  const [receiptAuto, setReceiptAuto] = useState(false)

  const keyOf = (month: string, groupId?: string | null) => `${month}|${groupId ?? ''}`

  useEffect(() => {
    if (!studentId) return
    const req = ++reqRef.current
    // eslint-disable-next-line react-hooks/set-state-in-effect -- o'quvchi almashganda tarixni yuklash (maqsadli)
    setLoading(true)
    setLedger(null)
    setError('')
    setEditKey(null)
    getStudentLedger(studentId)
      .then((l) => {
        if (reqRef.current === req) setLedger(l)
      })
      .catch((e) => {
        if (reqRef.current === req) setError(apiErrorMessage(e, "To'lov tarixini yuklab bo'lmadi"))
      })
      .finally(() => {
        if (reqRef.current === req) setLoading(false)
      })
  }, [studentId, tick, refreshKey])

  const saveEdit = async (month: string, groupId?: string | null) => {
    if (!studentId) return
    const amount = Number(editVal)
    if (!Number.isFinite(amount) || amount < 0) {
      alert("Summa noto'g'ri")
      return
    }
    setSaving(true)
    try {
      // Guruhsiz (ClassName) hisob — groupId=undefined (backend null = ClassName);
      // guruhli ulush — shu guruh hisobi alohida tahrirlanadi.
      await editStudentCharge(studentId, month, amount, groupId ?? undefined)
      const fresh = await getStudentLedger(studentId)
      setLedger(fresh)
      setEditKey(null)
      // Chegirma YANGI summadan qayta hisoblandi — chaqiruvchi o'zidagi chegirma ko'rinishini
      // yangilaydi (bu panel faqat O'Z ledger'ini biladi).
      onChargeEdited?.()
    } catch (err) {
      alert(apiErrorMessage(err, "Tahrirlab bo'lmadi"))
    } finally {
      setSaving(false)
    }
  }

  const openPay = () => {
    if (!studentId) return
    getStudent(studentId)
      .then(setPayTarget)
      .catch(() => alert("O'quvchi ma'lumotini yuklab bo'lmadi"))
  }

  const handlePayment = async (
    amount: number,
    month: string,
    groupId?: string,
    comment?: string,
    method?: string,
    date?: string,
    extra?: { receiptNo?: string; paidTime?: string; cardLast4?: string; forceReceipt?: boolean },
  ) => {
    if (!studentId) return
    // Xato QAYTA OTILADI — PaymentModal o'zi ko'rsatadi (kvitansiya band bo'lsa kartochka
    // + "Baribir saqlash"). Shu sabab bu yerda catch/alert yo'q.
    const txId = await addPayment(studentId, amount, month, groupId, comment, method, date, extra)
    // ⚠️ TO'LOV SAQLANDI — avval modal YOPILADI, keyin ro'yxat yangilanadi.
    // Ilgari yangilash `await` bilan shu yerda turardi va u yiqilsa xato PaymentModal ning
    // catch'iga borib "To'lovni saqlab bo'lmadi" deb ko'rinardi — kassir to'lovni QAYTA
    // kiritib, DUBLIKAT tranzaksiya yasardi. Yangilash xatosi endi to'lovga TA'SIR QILMAYDI.
    setPayTarget(null)
    onPaid?.()
    try {
      const fresh = await getStudentLedger(studentId)
      setLedger(fresh)
    } catch (e) {
      setError(apiErrorMessage(e, "To'lov saqlandi, lekin ro'yxatni yangilab bo'lmadi"))
    }
    // CHEK: to'lov saqlangach kvitansiya ochiladi (Kassa bo'limidagi bilan bir xil).
    if (txId) {
      setReceiptAuto(true)
      setReceiptTx(txId)
    }
  }

  /**
   * Oy ichidagi GURUH ulushlari (+ super admin uchun qo'lda tahrir). Ikkala ko'rinish (`panel` va
   * `profile`) ham AYNAN shuni chizadi — tahrir oqimi bitta joyda.
   */
  const renderCourses = (m: MonthLedger) =>
    m.courses.length > 0 && (
      <div className="mt-1 space-y-1">
        {m.courses.map((co, i) => {
          const k = keyOf(m.month, co.groupId)
          const editing = isSuper && editKey === k
          return (
            <div
              key={i}
              className="flex items-center gap-1.5 text-xs font-normal text-slate-400"
            >
              <span className="h-1.5 w-1.5 shrink-0 rounded-full bg-brand-300" />
              {/* GURUH nomi asosiy, yonida — kursi (bir xil bo'lsa takrorlanmaydi). */}
              <span className="truncate text-slate-500">
                {co.groupName || co.courseName}
                {co.groupName && co.courseName && co.courseName !== co.groupName
                  ? ` — ${co.courseName}`
                  : ''}
              </span>
              <span className="text-slate-300">·</span>
              {editing ? (
                <span className="inline-flex items-center gap-1">
                  <input
                    type="number"
                    autoFocus
                    value={editVal}
                    disabled={saving}
                    onChange={(e) => setEditVal(e.target.value)}
                    onKeyDown={(e) => {
                      if (e.key === 'Enter') saveEdit(m.month, co.groupId)
                      if (e.key === 'Escape') setEditKey(null)
                    }}
                    className="w-24 rounded-md border border-slate-200 px-2 py-0.5 text-right font-mono text-xs outline-none focus:border-brand-400 disabled:opacity-50"
                  />
                  <button
                    type="button"
                    title="Saqlash"
                    disabled={saving}
                    onClick={() => saveEdit(m.month, co.groupId)}
                    className="rounded p-0.5 text-emerald-600 hover:bg-emerald-50 disabled:opacity-50"
                  >
                    <Check className="h-3.5 w-3.5" />
                  </button>
                  <button
                    type="button"
                    title="Bekor"
                    disabled={saving}
                    onClick={() => setEditKey(null)}
                    className="rounded p-0.5 text-slate-400 hover:bg-slate-100 disabled:opacity-50"
                  >
                    <X className="h-3.5 w-3.5" />
                  </button>
                </span>
              ) : (
                <>
                  <span className="font-mono text-slate-500">{formatMoney(co.fee)}</span>
                  {isSuper && (
                    <button
                      type="button"
                      title="Bu guruh hisobini tahrirlash"
                      onClick={() => {
                        setEditKey(k)
                        setEditVal(String(co.fee))
                      }}
                      className="rounded p-0.5 text-slate-300 transition-colors hover:bg-slate-100 hover:text-brand-600"
                    >
                      <Pencil className="h-3 w-3" />
                    </button>
                  )}
                </>
              )}
            </div>
          )
        })}
      </div>
    )

  // UCHTA ALOHIDA holat: yuklanmoqda · xato (sabab + qayta urinish) · ma'lumot yo'q.
  if (loading) return <Loader label="Yuklanmoqda..." />

  if (error && !ledger)
    return (
      <div className="flex flex-col items-center gap-3 rounded-xl border border-red-100 bg-red-50 px-4 py-8 text-center">
        <AlertCircle className="h-6 w-6 text-red-500" />
        <p className="text-sm font-semibold text-red-700">To'lov tarixi yuklanmadi</p>
        <p className="max-w-md text-sm text-red-600">{error}</p>
        <Button variant="secondary" onClick={() => setTick((t) => t + 1)}>
          <RefreshCw className="h-4 w-4" /> Qayta urinish
        </Button>
      </div>
    )

  if (!ledger)
    return <p className="py-8 text-center text-sm text-slate-400">To'lov ma'lumoti topilmadi</p>

  /** Ma'lumot BOR, lekin oxirgi yangilash yiqildi — ikkala ko'rinishda ham bir xil ogohlantirish. */
  const staleBanner = error && (
    <div className="flex items-center gap-2 rounded-lg border border-amber-200 bg-amber-50 px-3 py-2 text-sm text-amber-700">
      <AlertCircle className="h-4 w-4 shrink-0" />
      <span className="flex-1">{error}</span>
      <button
        type="button"
        onClick={() => setTick((t) => t + 1)}
        className="shrink-0 rounded-md px-2 py-1 text-xs font-semibold text-amber-800 hover:bg-amber-100"
      >
        Qayta urinish
      </button>
    </div>
  )

  /** Balans rangi/belgisi — sarlavhada ham, profil jadvali tepasida ham bir xil. */
  const balanceCls =
    ledger.balance < 0 ? 'text-red-600' : ledger.balance > 0 ? 'text-emerald-600' : 'text-slate-600'
  const balanceText = ledger.balance > 0 ? `+${formatMoney(ledger.balance)}` : formatMoney(ledger.balance)

  // PROFIL ko'rinishi — edutizim «Tranzaksiyalar tarixi»: avval to'lovlar (SANA "dd.mm.yyyy | HH:mm"),
  // keyin oylar bo'yicha hisob. Raqamlar AYNAN o'sha `ledger` dan — faqat chizilishi boshqa.
  const profileBody = variant === 'profile' && (
    <div className="space-y-4">
      {staleBanner}

      <div className="flex flex-wrap items-center justify-between gap-2">
        <p className="text-[13px] text-[#333]">
          <span className="text-black/60">Joriy balans:</span>{' '}
          <b className={cn('font-bold', balanceCls)}>{balanceText}</b>
          <span className="mx-2 text-[#dbe0e6]">|</span>
          <span className="text-black/60">Jami oylik:</span>{' '}
          <b className="font-bold">{formatMoney(ledger.monthlyFee)}</b>
        </p>
        <div className="flex items-center gap-2">
          <TotalPill total={ledger.payments.length} />
          <Button onClick={openPay} className="mb-2">
            <Wallet className="h-4 w-4" /> To'lov qilish
          </Button>
        </div>
      </div>

      <DataTable
        rows={ledger.payments.map((p, i) => ({ ...p, _key: `${p.date}-${i}` }))}
        rowKey={(p) => p._key}
        numbered
        columns={[
          { key: 'date', header: 'Sana', width: 150, render: (p) => joinDateTimeBar(p.date, p.paidTime) },
          {
            key: 'amount',
            header: 'Miqdori',
            align: 'right',
            render: (p) => <span className="font-semibold text-emerald-600">+{formatMoney(p.amount)}</span>,
          },
          { key: 'month', header: 'Oy', render: (p) => (p.month ? formatMonth(p.month) : '—') },
          {
            key: 'group',
            header: 'Guruh',
            render: (p) =>
              p.groupName
                ? `${p.groupName}${p.courseName && p.courseName !== p.groupName ? ` — ${p.courseName}` : ''}`
                : '—',
          },
          { key: 'method', header: "To'lov turi", render: (p) => (p.method ? paymentMethodLabel(p.method) : '—') },
          {
            key: 'ref',
            header: 'Kvitansiya / karta',
            render: (p) =>
              p.receiptNo ? (
                <span className="font-mono">{p.receiptNo}</span>
              ) : p.cardLast4 ? (
                <span className="font-mono">•••• {p.cardLast4}</span>
              ) : (
                '—'
              ),
          },
          { key: 'comment', header: 'Izoh', render: (p) => p.comment || '—' },
        ]}
      />

      <h3 className="flex items-center gap-2 pt-2 text-[15px] font-semibold text-black">
        <span className="h-5 w-[3px] rounded-full bg-brand-600" /> Oylar bo'yicha
      </h3>
      <DataTable
        rows={ledger.months}
        rowKey={(m) => m.month}
        columns={[
          {
            key: 'month',
            header: 'Oy',
            render: (m) => (
              <div className="py-2">
                {formatMonth(m.month)}
                {renderCourses(m)}
              </div>
            ),
          },
          { key: 'charged', header: 'Hisoblangan', align: 'right', render: (m) => formatMoney(m.charged) },
          {
            key: 'discount',
            header: 'Chegirma',
            align: 'right',
            render: (m) =>
              m.discount > 0 ? <span className="text-amber-600">−{formatMoney(m.discount)}</span> : '—',
          },
          {
            key: 'paid',
            header: "To'langan",
            align: 'right',
            render: (m) => <span className="text-emerald-600">{formatMoney(m.paid)}</span>,
          },
          {
            key: 'remaining',
            header: 'Qoldiq',
            align: 'right',
            render: (m) => (
              <span className={m.remaining > 0 ? 'text-red-600' : 'text-slate-400'}>{formatMoney(m.remaining)}</span>
            ),
          },
          {
            key: 'status',
            header: 'Holat',
            align: 'center',
            render: (m) => (
              <span className={cn('rounded-md px-2 py-0.5 text-xs font-medium', statusStyles[m.status])}>
                {monthStatusLabels[m.status]}
              </span>
            ),
          },
        ]}
        footer={
          <div className="flex flex-wrap justify-end gap-x-6 gap-y-1 border-t border-[#e0e0e0] px-2.5 py-3 text-[13px] font-semibold">
            <span>
              Jami hisoblangan: <span className="font-bold">{formatMoney(ledger.totalCharged)}</span>
            </span>
            <span className="text-amber-700">
              Chegirma: {ledger.totalDiscount > 0 ? `−${formatMoney(ledger.totalDiscount)}` : '—'}
            </span>
            <span className="text-emerald-700">To'langan: {formatMoney(ledger.totalPaid)}</span>
          </div>
        }
      />
    </div>
  )

  return (
    <>
      {profileBody || (
      <div className="space-y-5">
        {/* Ma'lumot BOR, lekin oxirgi yangilash yiqildi — jimgina eskirib qolmasin. */}
        {staleBanner}

        {/* Sarlavha */}
        <div className="flex flex-wrap items-center justify-between gap-3 rounded-xl bg-slate-50 px-4 py-3">
          <div>
            <p className="font-semibold text-slate-800">{ledger.student.fullName}</p>
            {/* ⚠️ GURUH NOMI a'zoliklardan (`groupStates`), eski `className` faqat ZAXIRA —
                o'quvchi bir NECHTA guruhda bo'lishi mumkin (`lib/studentGroups.ts`).
                «Oylik» ham BARCHA faol guruhlar yig'indisi (server shunday hisoblaydi), shuning
                uchun yorliq "jami oylik" — aks holda sarlavha pastdagi «Hisoblangan» ustuni
                bilan ZID bo'lib ko'rinardi (bitta guruh narxi kabi o'qilardi). */}
            <p className="text-sm text-slate-500">
              {groupsText(statesToGroups(ledger.student.groupStates), ledger.student.className, '—')} · jami oylik{' '}
              <span className="font-mono">{formatMoney(ledger.monthlyFee)}</span>
            </p>
          </div>
          <div className="flex items-center gap-4">
            <div className="text-right">
              <p className="text-xs text-slate-400">Joriy balans</p>
              <p className={cn('font-mono text-lg font-semibold', balanceCls)}>{balanceText}</p>
            </div>
            <Button onClick={openPay}>
              <Wallet className="h-4 w-4" /> To'lov qilish
            </Button>
          </div>
        </div>

        {/* Oylar bo'yicha holat */}
        <div>
          <p className="mb-2 text-sm font-medium text-slate-600">Oylar bo'yicha</p>
          <div className="overflow-hidden rounded-xl border border-slate-200">
            <table className="w-full text-left text-sm">
              <thead className="bg-slate-50 text-xs uppercase tracking-wide text-slate-400">
                <tr>
                  <th className="px-4 py-2.5">Oy</th>
                  <th className="px-4 py-2.5 text-right">Hisoblangan</th>
                  <th className="px-4 py-2.5 text-right">Chegirma</th>
                  <th className="px-4 py-2.5 text-right">To'langan</th>
                  <th className="px-4 py-2.5 text-right">Qoldiq</th>
                  <th className="px-4 py-2.5 text-center">Holat</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-slate-100">
                {ledger.months.map((m) => (
                  <tr key={m.month} className="hover:bg-slate-50/60">
                    <td className="px-4 py-2.5 align-top font-medium text-slate-700">
                      {formatMonth(m.month)}
                      {renderCourses(m)}
                    </td>
                    <td className="px-4 py-2.5 text-right align-top font-mono text-slate-600">
                      {formatMoney(m.charged)}
                      {isSuper && m.courses.length > 1 && (
                        <p className="mt-0.5 text-[10px] font-normal text-slate-300">
                          har guruh alohida ↙
                        </p>
                      )}
                    </td>
                    <td
                      className={cn(
                        'px-4 py-2.5 text-right font-mono',
                        m.discount > 0 ? 'text-amber-600 font-medium' : 'text-slate-300',
                      )}
                    >
                      {m.discount > 0 ? `−${formatMoney(m.discount)}` : '—'}
                    </td>
                    <td className="px-4 py-2.5 text-right font-mono text-emerald-600 font-medium">
                      {formatMoney(m.paid)}
                    </td>
                    <td
                      className={cn(
                        'px-4 py-2.5 text-right font-mono',
                        m.remaining > 0 ? 'text-red-600' : 'text-slate-400',
                      )}
                    >
                      {formatMoney(m.remaining)}
                    </td>
                    <td className="px-4 py-2.5 text-center">
                      <span
                        className={cn(
                          'rounded-md px-2 py-0.5 text-xs font-medium',
                          statusStyles[m.status],
                        )}
                      >
                        {monthStatusLabels[m.status]}
                      </span>
                    </td>
                  </tr>
                ))}
              </tbody>
              <tfoot className="border-t border-slate-200 bg-slate-50 text-sm font-semibold text-slate-700">
                <tr>
                  <td className="px-4 py-2.5">Jami</td>
                  <td className="px-4 py-2.5 text-right font-mono">{formatMoney(ledger.totalCharged)}</td>
                  <td className="px-4 py-2.5 text-right font-mono text-amber-700">
                    {ledger.totalDiscount > 0 ? `−${formatMoney(ledger.totalDiscount)}` : '—'}
                  </td>
                  <td className="px-4 py-2.5 text-right font-mono text-emerald-700">
                    {formatMoney(ledger.totalPaid)}
                  </td>
                  <td className="px-4 py-2.5 text-right" colSpan={2} />
                </tr>
              </tfoot>
            </table>
          </div>
        </div>

        {/* To'lovlar tarixi (kassa yozuvlari) */}
        <div>
          <p className="mb-2 text-sm font-medium text-slate-600">Amalga oshirilgan to'lovlar</p>
          {ledger.payments.length === 0 ? (
            <p className="text-sm text-slate-400">To'lov yozuvlari yo'q</p>
          ) : (
            <ul className="space-y-1.5">
              {ledger.payments.map((p, i) => (
                <li
                  key={i}
                  className="rounded-lg border border-slate-100 px-3 py-2 text-sm"
                >
                  <div className="flex items-center justify-between">
                    <span className="flex flex-wrap items-center gap-2 text-slate-500">
                      <span className="font-mono">
                        {formatDate(p.date)}
                        {/* KARTA to'lovida pul o'tkazilgan aniq VAQT ham ko'rsatiladi (bank
                            ilovasidagi vaqt bilan solishtirish uchun kiritilgan). */}
                        {p.paidTime && <span className="text-slate-400"> · {p.paidTime}</span>}
                      </span>
                      {p.month && (
                        <span className="rounded-md bg-slate-100 px-1.5 py-0.5 text-xs font-medium text-slate-500">
                          {formatMonth(p.month)} uchun
                        </span>
                      )}
                      {p.method && (
                        <span className="rounded-md bg-blue-50 px-1.5 py-0.5 text-xs font-medium text-blue-600">
                          {paymentMethodLabel(p.method)}
                        </span>
                      )}
                      {/* KARTA — qaysi kartaga tushgani (oxirgi 4 raqam; to'liq raqam saqlanmaydi) */}
                      {p.cardLast4 && (
                        <span className="rounded-md bg-indigo-50 px-1.5 py-0.5 font-mono text-xs font-medium text-indigo-600">
                          •••• {p.cardLast4}
                        </span>
                      )}
                      {/* NAQD — qog'oz kvitansiya (KV) raqami */}
                      {p.receiptNo && (
                        <span className="rounded-md bg-amber-50 px-1.5 py-0.5 font-mono text-xs font-medium text-amber-700">
                          {p.receiptNo}
                        </span>
                      )}
                    </span>
                    <span className="shrink-0 font-mono font-medium text-emerald-600">
                      +{formatMoney(p.amount)}
                    </span>
                  </div>
                  {/* Usul bo'yicha to'liq tafsilot — bir qarashda "qachon, qaysi karta / qaysi
                      kvitansiya" savoliga javob. Ma'lumot kiritilmagan bo'lsa qator umuman chiqmaydi. */}
                  {(p.cardLast4 || p.paidTime || p.receiptNo) && (
                    <p className="mt-0.5 text-xs text-slate-400">
                      {p.method === 'cash'
                        ? `Naqd · ${formatDate(p.date)}${p.receiptNo ? ` · kvitansiya ${p.receiptNo}` : ''}`
                        : `${paymentMethodLabel(p.method ?? 'card')} · ${formatDate(p.date)}${
                            p.paidTime ? ` ${p.paidTime}` : ''
                          }${p.cardLast4 ? ` · karta •••• ${p.cardLast4}` : ''}`}
                    </p>
                  )}
                  {/* To'lov qaysi GURUHGA tushgani — yonida shu guruhning kursi ("Guruh — Kurs"). */}
                  {p.groupName && (
                    <p className="mt-1 flex items-center gap-1.5 text-xs text-slate-500">
                      <span className="inline-flex items-center gap-1 rounded-md bg-brand-50 px-1.5 py-0.5 font-medium text-brand-700">
                        <Users className="h-3 w-3" /> {p.groupName}
                        {p.courseName && p.courseName !== p.groupName ? ` — ${p.courseName}` : ''}
                      </span>
                    </p>
                  )}
                  {p.comment && <p className="mt-0.5 text-xs text-slate-500">{p.comment}</p>}
                </li>
              ))}
            </ul>
          )}
        </div>

        {/* O'zgarishlar tarixi (audit) — `audit` ruxsati bilan */}
        {canSeeAudit && (
          <div>
            <p className="mb-2 text-sm font-medium text-slate-600">O'zgarishlar tarixi</p>
            <AuditHistoryList
              filters={{ studentId: ledger.student.id }}
              emptyLabel="To'lovlar bo'yicha o'zgarishlar yo'q"
            />
          </div>
        )}
      </div>
      )}

      <PaymentModal student={payTarget} onClose={() => setPayTarget(null)} onSubmit={handlePayment} />

      {/* TO'LOV CHEKI — to'lov kiritilgach avtomatik ochiladi va bosib chiqarish dialogini
          chaqiradi (Kassa/Moliya bo'limlaridagi bilan bir xil xatti-harakat). */}
      <ReceiptModal
        txId={receiptTx}
        autoPrint={receiptAuto}
        onClose={() => {
          setReceiptTx(null)
          setReceiptAuto(false)
        }}
      />

    </>
  )
}
