import { useEffect, useRef, useState } from 'react'
import type { ReactNode } from 'react'
import { Wallet, AlertTriangle } from 'lucide-react'
import type { MonthStatus, Student, StudentGroupMembership } from '@/types'
import { getStudentLedger, getGroupLedger, receiptDuplicateOf, type DuplicateReceipt } from '@/api/services/students'
import { getStudentGroups } from '@/api/services/classes'
import { Modal } from '@/components/ui/Modal'
import { Button } from '@/components/ui/Button'
import {
  RightDrawer,
  DrawerActions,
  DrawerChoice,
  DrawerField,
  DrawerInput,
  DrawerSelect,
  DrawerTextarea,
} from '@/components/ui/RightDrawer'
import { Loader } from '@/components/ui/Loader'
import { formatMoney, formatDate, formatDateTime, apiErrorMessage, cn } from '@/lib/utils'
import { formatMonth, monthStatusLabels, paymentMethods, paymentMethodLabel } from '@/config/constants'
import posthog from '@/lib/posthog'

interface Props {
  student: Student | null
  /**
   * Oyna ochiqmi. Berilmasa — `student` tanlangan bo'lsa ochiq (eski xatti-harakat). Kassalar
   * sahifasi uni o'quvchi HALI tanlanmagan holda ochadi (tepada `headerSlot` — o'quvchi tanlash).
   */
  open?: boolean
  /** Sarlavha (standart "To'lov kiritish"; Kassalar — "Kirim"). */
  title?: string
  /** Forma TEPASIDAGI qo'shimcha blok (masalan o'quvchi tanlash) — ixtiyoriy. */
  headerSlot?: ReactNode
  /**
   * Ko'rinish: `drawer` (standart, edutizim — o'ngdan chiqadigan panel) yoki `modal` (markazdagi
   * oyna). Telefondagi kassa portali (`/kassa`) o'zgarmasligi uchun `modal` ni tanlaydi.
   */
  presentation?: 'drawer' | 'modal'
  onClose: () => void
  onSubmit: (
    amount: number,
    month: string,
    groupId?: string,
    comment?: string,
    method?: string,
    date?: string,
    /** Naqd — qog'oz kvitansiya raqami ("KV..."); karta — to'lov vaqti "HH:mm" va karta
     *  raqamining oxirgi 4 raqami.
     *  `forceReceipt` — kvitansiya band bo'lsa ham saqlash ("Baribir saqlash"). */
    extra?: { receiptNo?: string; paidTime?: string; cardLast4?: string; forceReceipt?: boolean },
  ) => void | Promise<void>
}

/** Oy qatori (guruh yoki aggregate hisobdan normallashtirilgan) */
type Row = { month: string; remaining: number; status: MonthStatus }

/** "YYYY-MM" joriy oy */
const currentMonth = () => new Date().toISOString().slice(0, 7)
/** "YYYY-MM-DD" bugungi sana */
const today = () => new Date().toISOString().slice(0, 10)
/** "HH:mm" hozirgi vaqt (karta to'lovi vaqti uchun standart qiymat) */
const nowTime = () => {
  const d = new Date()
  return `${String(d.getHours()).padStart(2, '0')}:${String(d.getMinutes()).padStart(2, '0')}`
}
/** Kvitansiya seriyasi — qog'oz blankada bosilgan (backend ham shu bilan saqlaydi). */
const RECEIPT_SERIES = 'KV'

/** Oylar ro'yxatidan standart tanlov. Avval JORIY OYGACHA (o'tgan/joriy) eng eski QARZDOR oy tanlanadi —
 *  ya'ni qarz bo'lsa u to'lanadi. KELAJAK (avans) oy HECH QACHON avtomatik tanlanmaydi (foydalanuvchi o'zi
 *  tanlashi mumkin) — aks holda qarz bo'lsa ham to'lov kelasi oyga yozilib qolardi. Qarz bo'lmasa — joriy oy. */
const pickDefault = (rows: Row[]): { month: string; amount: number } => {
  const cur = currentMonth()
  // Joriy oygacha bo'lgan eng eski qarzdor oy (o'tgan qarzni birinchi to'laymiz).
  const due = rows.find((r) => r.remaining > 0 && r.month <= cur)
  if (due) return { month: due.month, amount: due.remaining }
  // Qarz yo'q — joriy oyni tanlaymiz (ro'yxatda bo'lsa), aks holda oxirgi mavjud oy. Kelajak oy summasi
  // avtomatik to'ldirilmaydi (0) — cheksiz avans yozilib qolmasligi uchun.
  const curRow = rows.find((r) => r.month === cur)
  const target = curRow ?? rows[rows.length - 1]
  return { month: target?.month ?? cur, amount: curRow ? curRow.remaining : 0 }
}

/** Guruh tanlovidagi izoh: muzlatilgan / guruhdan chiqarilgan a'zolik ekani ko'rinib tursin. */
const membershipNote = (g: StudentGroupMembership): string =>
  g.status === 'frozen'
    ? ` — muzlatilgan${g.frozenAt ? ` ${g.frozenAt}` : ''}`
    : !g.isActive
      ? ' — chiqarilgan'
      : ''

export function PaymentModal({
  student,
  open,
  title = "To'lov kiritish",
  headerSlot,
  presentation = 'drawer',
  onClose,
  onSubmit,
}: Props) {
  const [amount, setAmount] = useState<number>(0)
  const [month, setMonth] = useState<string>(currentMonth())
  const [rows, setRows] = useState<Row[]>([])
  const [groups, setGroups] = useState<StudentGroupMembership[]>([])
  const [groupId, setGroupId] = useState<string>('')
  const [comment, setComment] = useState("")
  const [method, setMethod] = useState<string>('cash')
  /** NAQD to'lov — qog'oz kvitansiya raqami (seriya "KV" alohida ko'rsatiladi, bu yerda faqat raqam). */
  const [receiptNo, setReceiptNo] = useState('')
  /** KARTA to'lovi — pul haqiqatan o'tkazilgan vaqt ("HH:mm"), bank ilovasidagi vaqt bilan solishtirish uchun. */
  const [paidTime, setPaidTime] = useState(nowTime())
  /** KARTA to'lovi — karta raqamining OXIRGI 4 RAQAMI (bank ko'chirmasi bilan solishtirish uchun).
   *  To'liq karta raqami saqlanmaydi — server ham faqat oxirgi 4 raqamni oladi. */
  const [cardLast4, setCardLast4] = useState('')
  /** To'lov haqiqatan sodir bo'lgan sana — bugun to'lagan, lekin tizimga keyinroq kiritilayotgan
   * to'lov uchun eski sana tanlash imkoni. */
  const [paidDate, setPaidDate] = useState<string>(today())
  const [submitting, setSubmitting] = useState(false)
  /** Kvitansiya raqami BAND — server 409 qaytardi; shu to'lov ma'lumoti kartochka bo'lib chiqadi. */
  const [duplicate, setDuplicate] = useState<DuplicateReceipt | null>(null)
  const [error, setError] = useState<string | null>(null)

  const [loading, setLoading] = useState(false) // boshlang'ich (guruhlar) yuklash
  const [loadingMonths, setLoadingMonths] = useState(false) // tanlangan guruh oylari
  /**
   * Guruhlar ro'yxati YUKLANMADI (tarmoq/server xatosi). ⚠️ Bo'sh ro'yxatdan FARQ QILADI:
   * bo'sh ro'yxat — "o'quvchi haqiqatan guruhsiz" (eski `className` bo'yicha jami hisob),
   * xato esa — "bilmaymiz". Ikkalasi bir xil bo'lsa, to'lov `groupId` siz yozilib PULNI
   * NOTO'G'RI guruhga (yoki umuman guruhsiz hisobga) tushirib yuborardi.
   */
  const [groupsError, setGroupsError] = useState<string | null>(null)
  /** Oylik hisob (ledger) yuklanmadi — oy ro'yxatiga ISHONIB bo'lmaydi, saqlash bloklanadi. */
  const [monthsError, setMonthsError] = useState<string | null>(null)
  /**
   * SO'ROV RAQAMI — kechikib kelgan javob YANGISINI bosib ketmasin.
   * ⚠️ Kassir A o'quvchisini yopib B ni ochsa, A ning guruh hisobiga ketgan so'rov keyinroq
   * qaytib B ning oylar ro'yxatini ALMASHTIRIB yuborardi (server mavjud bo'lmagan a'zolikka
   * ham 200 + bo'sh `months` qaytaradi): B ning uch oylik qarzi ko'rinmay, kassir summani
   * NOTO'G'RI oyga qo'lda yozardi.
   */
  const reqRef = useRef(0)
  const studentId = student?.id ?? ''

  // O'quvchi ALMASHGANDA yoki modal YOPILGANDA — barcha holat tozalanadi, so'ng guruhlar
  // yuklanadi. ⚠️ Tozalash `student == null` da ham bajariladi: aks holda keyingi ochilishda
  // ESKI o'quvchining guruhi/oyi bir commit'ga qolib, o'sha guruh bo'yicha so'rov ketardi.
  useEffect(() => {
    const req = ++reqRef.current
    // eslint-disable-next-line react-hooks/set-state-in-effect -- modal ochilganda/yopilganda holatni tozalash va yuklash (maqsadli)
    setLoading(!!studentId)
    setLoadingMonths(false)
    setRows([])
    setGroups([])
    setGroupId('')
    setComment("")
    setMethod('cash')
    setAmount(0)
    setMonth(currentMonth())
    setPaidDate(today())
    setReceiptNo('')
    setPaidTime(nowTime())
    setCardLast4('')
    setDuplicate(null)
    setError(null)
    setGroupsError(null)
    setMonthsError(null)
    if (!studentId) return
    getStudentGroups(studentId)
      .then(async (allGroups) => {
        // To'lov qilish mumkin bo'lgan a'zoliklar: SINOVDAN boshqa hammasi — MUZLATILGAN va guruhi
        // YOPILGAN (arxivdagi) a'zoliklar ham. Ular bo'yicha qarz muzlatish sanasigacha hisoblangan
        // bo'lishi mumkin — kassir keyin ham to'lovni qabul qila olishi kerak.
        const billable = allGroups.filter((g) => g.status !== 'trial')
        if (reqRef.current !== req) return
        setGroups(billable)
        if (billable.length === 0) {
          // Guruhsiz (eski ClassName) o'quvchi — aggregate hisob.
          const ledger = await getStudentLedger(studentId)
          if (reqRef.current !== req) return
          const r: Row[] = ledger.months.map((m) => ({
            month: m.month,
            remaining: m.remaining,
            status: m.status,
          }))
          setRows(r)
          const d = pickDefault(r)
          setMonth(d.month)
          setAmount(d.amount)
        } else {
          // JORIY (faol, muzlatilmagan) a'zolik bitta bo'lsa — avtomatik tanlanadi. Eski/muzlatilgan
          // guruhlar ro'yxatda qoladi (tanlash mumkin), lekin standart tanlovni buzmaydi.
          const current = billable.filter((g) => g.isActive && g.status !== 'frozen')
          if (current.length === 1) setGroupId(current[0].groupId)
          else if (billable.length === 1) setGroupId(billable[0].groupId)
          // Aks holda — foydalanuvchi tanlaguncha kutamiz.
        }
      })
      .catch((err) => {
        // ⚠️ JIMGINA bo'sh ro'yxatga TUSHIRILMAYDI — pul noto'g'ri hisobga yozilardi.
        if (reqRef.current !== req) return
        setGroupsError(apiErrorMessage(err, "Guruhlar ro'yxatini yuklab bo'lmadi"))
      })
      .finally(() => {
        if (reqRef.current === req) setLoading(false)
      })
  }, [studentId])

  // Guruh tanlanganda (yoki avtomatik bitta guruh) — shu guruh oylik hisobini yukla.
  useEffect(() => {
    if (!studentId || !groupId) return
    const req = ++reqRef.current
    // eslint-disable-next-line react-hooks/set-state-in-effect -- guruh tanlanganda oylarni yuklash (maqsadli)
    setLoadingMonths(true)
    setRows([])
    setMonthsError(null)
    getGroupLedger(studentId, groupId)
      .then((ledger) => {
        // Eski (boshqa o'quvchi/guruh) javobi kelsa — TASHLANADI.
        if (reqRef.current !== req) return
        const r: Row[] = ledger.months.map((m) => ({
          month: m.month,
          remaining: m.remaining,
          status: m.status,
        }))
        setRows(r)
        const d = pickDefault(r)
        setMonth(d.month)
        setAmount(d.amount)
      })
      .catch((err) => {
        if (reqRef.current !== req) return
        setMonthsError(apiErrorMessage(err, "Oylik hisobni yuklab bo'lmadi"))
      })
      .finally(() => {
        if (reqRef.current === req) setLoadingMonths(false)
      })
  }, [studentId, groupId])

  // Bir nechta guruh bo'lsa — guruh tanlanishi SHART.
  const needGroup = groups.length > 1
  /**
   * Oylarni ko'rsatish: guruh tanlangan bo'lsa, YOKI o'quvchi HAQIQATAN guruhsiz bo'lsa
   * (aggregate hisob). ⚠️ Guruhlar so'rovi YIQILGANDA ko'rsatilmaydi: u holda "guruhsiz"
   * ekaniga ishonch yo'q va to'lov `groupId` siz ketib qolardi.
   */
  const showMonths = (groups.length === 0 && !groupsError) || !!groupId
  /** Ma'lumot ishonchsiz — saqlash BLOKLANADI (pul noto'g'ri hisobga tushmasin). */
  const blocked = !!groupsError || !!monthsError

  const handleMonthChange = (value: string) => {
    setMonth(value)
    const r = rows.find((x) => x.month === value)
    setAmount(r && r.remaining > 0 ? r.remaining : 0)
  }

  /** To'lovni saqlash. `force=true` — kvitansiya band bo'lsa ham ("Baribir saqlash"). */
  const save = async (force: boolean) => {
    // Ikki marta bosishdan himoya (dublikat to'lov yaratilmasin).
    // ⚠️ `blocked` — guruhlar yoki oylik hisob YUKLANMAGAN: qaysi guruh/oy ekani noaniq.
    if (submitting || blocked || amount <= 0 || !month || (needGroup && !groupId)) return
    setSubmitting(true)
    setError(null)
    try {
      await onSubmit(amount, month, groupId || undefined, comment.trim() || undefined, method, paidDate || undefined, {
        // Kvitansiya faqat NAQD to'lovda, vaqt faqat KARTA to'lovida yuboriladi.
        receiptNo: method === 'cash' && receiptNo.trim() ? RECEIPT_SERIES + receiptNo.trim() : undefined,
        paidTime: method === 'card' && paidTime ? paidTime : undefined,
        cardLast4: method === 'card' && cardLast4 ? cardLast4 : undefined,
        forceReceipt: force,
      })
      setDuplicate(null)
      posthog.capture('student_payment_recorded', {
        payment_method: method,
        has_group: Boolean(groupId),
        is_for_future_month: month > currentMonth(),
      })
    } catch (err) {
      // Kvitansiya raqami allaqachon ishlatilgan — modal yopilmaydi, kassir qaror qabul qiladi.
      const dup = receiptDuplicateOf(err)
      if (dup) setDuplicate(dup)
      else setError(apiErrorMessage(err, "To'lovni saqlab bo'lmadi"))
    } finally {
      setSubmitting(false)
    }
  }

  const handleSubmit = (e: React.FormEvent) => {
    e.preventDefault()
    void save(false)
  }

  const selected = rows.find((r) => r.month === month)
  const newBalance = student ? student.balance + amount : 0
  const monthOptions = rows.length > 0 ? rows.map((r) => r.month) : [currentMonth()]
  const isOpen = open ?? !!student
  const saveDisabled =
    !student || amount <= 0 || !month || (needGroup && !groupId) || loading || loadingMonths || submitting || blocked

  const primaryAction = duplicate ? (
    // Kvitansiya band — kassir ataylab davom etishi mumkin (haqiqatan takroriy blank bo'lsa).
    <Button variant="danger" disabled={submitting} onClick={() => void save(true)}>
      <Wallet className="h-4 w-4" /> {submitting ? 'Saqlanmoqda...' : 'Baribir saqlash'}
    </Button>
  ) : (
    <Button type="submit" form="payment-form" disabled={saveDisabled}>
      <Wallet className="h-4 w-4" /> {submitting ? 'Saqlanmoqda...' : 'Saqlash'}
    </Button>
  )

  const body = (
    <div className="space-y-4">
      {headerSlot}
      {student &&
        (loading ? (
          <Loader label="Yuklanmoqda..." />
        ) : (
          <form id="payment-form" onSubmit={handleSubmit} className="space-y-4">
            {/* KVITANSIYA BAND — shu raqam bilan qaysi to'lov allaqachon kiritilgani.
                Kassir yo raqamni to'g'rilaydi, yo "Baribir saqlash"ni bosadi. */}
            {duplicate && (
              <div className="rounded-lg border border-amber-300 bg-amber-50 p-3">
                <div className="flex items-start gap-2">
                  <AlertTriangle className="mt-0.5 h-4 w-4 shrink-0 text-amber-600" />
                  <div className="min-w-0 flex-1">
                    <p className="text-sm font-semibold text-amber-800">
                      {duplicate.receiptNo} raqami allaqachon kiritilgan
                    </p>
                    <dl className="mt-2 space-y-1 text-[13px]">
                      <div className="flex gap-2">
                        <dt className="w-28 shrink-0 text-amber-700/70">O'quvchi</dt>
                        <dd className="font-semibold text-slate-800">{duplicate.studentName || '—'}</dd>
                      </div>
                      <div className="flex gap-2">
                        <dt className="w-28 shrink-0 text-amber-700/70">Guruh</dt>
                        <dd className="text-slate-700">
                          {duplicate.groupName || '—'}
                          {duplicate.courseName ? ` — ${duplicate.courseName}` : ''}
                        </dd>
                      </div>
                      <div className="flex gap-2">
                        <dt className="w-28 shrink-0 text-amber-700/70">O'qituvchi</dt>
                        <dd className="text-slate-700">{duplicate.teacherName || '—'}</dd>
                      </div>
                      <div className="flex gap-2">
                        <dt className="w-28 shrink-0 text-amber-700/70">Summa</dt>
                        <dd className="font-mono font-semibold text-emerald-700">
                          {formatMoney(duplicate.amount)}
                          {duplicate.month ? ` · ${formatMonth(duplicate.month)} uchun` : ''}
                        </dd>
                      </div>
                      <div className="flex gap-2">
                        <dt className="w-28 shrink-0 text-amber-700/70">To'lov sanasi</dt>
                        <dd className="text-slate-700">
                          {formatDate(duplicate.date)}
                          {duplicate.method ? ` · ${paymentMethodLabel(duplicate.method)}` : ''}
                        </dd>
                      </div>
                      <div className="flex gap-2">
                        <dt className="w-28 shrink-0 text-amber-700/70">Kiritilgan</dt>
                        <dd className="text-slate-700">
                          {formatDateTime(duplicate.createdAt)}
                          {duplicate.createdBy ? ` · ${duplicate.createdBy}` : ''}
                        </dd>
                      </div>
                    </dl>
                    <p className="mt-2 text-xs text-amber-700">
                      Raqamni tekshirib to'g'rilang. Haqiqatan takroriy blank bo'lsa — "Baribir saqlash".
                    </p>
                  </div>
                </div>
              </div>
            )}

            {error && (
              <div className="rounded-lg border border-red-200 bg-red-50 px-3 py-2 text-sm text-red-700">{error}</div>
            )}

            {/* ⚠️ GURUHLAR YUKLANMADI — "guruhsiz o'quvchi" bilan ARALASHTIRMASLIK uchun ochiq
                yoziladi va saqlash o'chiriladi: aks holda to'lov guruhsiz hisobga tushib,
                pul noto'g'ri joyga yozilardi. */}
            {groupsError && (
              <div className="rounded-lg border border-red-200 bg-red-50 px-3 py-2 text-sm text-red-700">
                <p className="font-semibold">Guruhlar ro'yxati yuklanmadi</p>
                <p className="mt-0.5 text-red-600">{groupsError}</p>
                <p className="mt-1 text-xs text-red-500">
                  To'lov qaysi guruhga tushishi noma'lum — oynani yopib qayta oching.
                </p>
              </div>
            )}

            {monthsError && (
              <div className="rounded-lg border border-red-200 bg-red-50 px-3 py-2 text-sm text-red-700">
                <p className="font-semibold">Oylik hisob yuklanmadi</p>
                <p className="mt-0.5 text-red-600">{monthsError}</p>
                <p className="mt-1 text-xs text-red-500">
                  Qaysi oyda qancha qarz borligi noma'lum — guruhni qayta tanlang yoki oynani
                  yopib qayta oching.
                </p>
              </div>
            )}

            <div className="rounded-lg border border-[#dbe0e6] bg-[#f0f2f2] px-3 py-2 text-sm">
              <p className="font-semibold text-black">{student.fullName}</p>
              <p className="mt-0.5 text-[#6b7280]">
                Joriy balans:{' '}
                <span className={cn('font-mono font-semibold', student.balance < 0 ? 'text-red-600' : 'text-emerald-600')}>
                  {formatMoney(student.balance)}
                </span>
              </p>
            </div>

            {/* Qaysi guruh uchun to'lov — o'quvchi bir nechta guruhda o'qisa tanlanadi */}
            {groups.length > 0 &&
              (groups.length === 1 ? (
                <DrawerField label="Qaysi guruh uchun" group>
                  <div className="flex min-h-9 items-center rounded-lg border border-[#dbe0e6] bg-[#f0f2f2] px-3 py-1.5 text-[14px] text-black">
                    {groups[0].groupName}
                    {groups[0].courseName ? ` — ${groups[0].courseName}` : ''}
                    {membershipNote(groups[0])}
                  </div>
                </DrawerField>
              ) : (
                <DrawerField label="Qaysi guruh uchun" required={needGroup}>
                  <DrawerSelect value={groupId} onChange={(e) => setGroupId(e.target.value)}>
                    <option value="">— Guruhni tanlang —</option>
                    {groups.map((g) => (
                      <option key={g.groupId} value={g.groupId}>
                        {g.groupName}
                        {g.courseName ? ` — ${g.courseName}` : ''}
                        {` (${formatMoney(g.monthlyFee)})`}
                        {membershipNote(g)}
                      </option>
                    ))}
                  </DrawerSelect>
                </DrawerField>
              ))}

            {/* Oy + summa — faqat guruh tanlangach (yoki guruhsiz aggregate) ko'rinadi */}
            {groupsError ? (
              /* Sabab yuqorida qizil kartochkada aytilgan — bu yerda "guruhni tanlang" deyish
                 CHALG'ITARDI (tanlash uchun ro'yxatning o'zi yo'q). */
              null
            ) : !showMonths ? (
              <p className="rounded-lg bg-amber-50 px-3 py-2 text-xs text-amber-700">
                O'quvchi bir nechta guruhda o'qiydi — avval to'lov qaysi guruh uchun ekanini tanlang.
              </p>
            ) : loadingMonths ? (
              <Loader label="Oylar yuklanmoqda..." />
            ) : (
              <>
                <DrawerField
                  label="Qaysi oy uchun"
                  hint={
                    selected && month > currentMonth() ? (
                      <span className="text-amber-600">Kelajak oy — to'lov avans sifatida hisobga olinadi.</span>
                    ) : selected && selected.remaining <= 0 ? (
                      <span className="text-amber-600">
                        Bu oy allaqachon to'langan — to'lov avans sifatida hisobga olinadi.
                      </span>
                    ) : undefined
                  }
                >
                  <DrawerSelect value={month} onChange={(e) => handleMonthChange(e.target.value)}>
                    {monthOptions.map((mo) => {
                      const r = rows.find((x) => x.month === mo)
                      const future = mo > currentMonth()
                      const suffix = r
                        ? future
                          ? ' — kelajak oy (avans)'
                          : r.remaining > 0
                            ? ` — ${monthStatusLabels[r.status]} (qoldiq ${formatMoney(r.remaining)})`
                            : ` — ${monthStatusLabels[r.status]}`
                        : ''
                      return (
                        <option key={mo} value={mo}>
                          {formatMonth(mo)}
                          {suffix}
                        </option>
                      )
                    })}
                  </DrawerSelect>
                </DrawerField>

                <DrawerField
                  label="Qiymat (so'm)"
                  hint={
                    amount > 0 ? (
                      <>
                        To'lovdan keyingi balans:{' '}
                        <span className={cn('font-mono font-semibold', newBalance < 0 ? 'text-red-600' : 'text-emerald-600')}>
                          {formatMoney(newBalance)}
                        </span>
                      </>
                    ) : undefined
                  }
                >
                  <DrawerInput
                    type="number"
                    min={0}
                    step="any"
                    autoFocus
                    value={amount}
                    onChange={(e) => setAmount(Number(e.target.value))}
                  />
                </DrawerField>

                <DrawerField label="To'lov turi" group>
                  <DrawerChoice options={paymentMethods} value={method} onChange={setMethod} />
                </DrawerField>

                {/* NAQD — qog'oz kvitansiya raqami: seriya "KV" (o'zgarmas) + raqam. */}
                {method === 'cash' && (
                  <DrawerField
                    label="Kvitansiya raqami"
                    hint="Qog'oz kvitansiyadagi raqam — Moliya bo'limida ko'rinadi va qidiriladi (ixtiyoriy)."
                  >
                    <div className="flex items-stretch">
                      <span className="flex select-none items-center rounded-l-lg border border-r-0 border-[#dbe0e6] bg-[#e6eaea] px-3 text-sm font-semibold tracking-wide text-[#6b7280]">
                        {RECEIPT_SERIES}
                      </span>
                      <input
                        type="text"
                        inputMode="numeric"
                        value={receiptNo}
                        onChange={(e) => {
                          setReceiptNo(e.target.value.replace(/\s+/g, ''))
                          // Raqam o'zgardi — eski "band" ogohlantirishi endi tegishli emas.
                          setDuplicate(null)
                        }}
                        placeholder="000123"
                        maxLength={20}
                        className="h-9 w-full rounded-r-lg border border-[#dbe0e6] bg-[#f0f2f2] px-3 font-mono text-[14px] text-black outline-none placeholder:text-[#9ca3af] focus:border-brand-600 focus:bg-white focus:ring-1 focus:ring-brand-600"
                      />
                    </div>
                  </DrawerField>
                )}

                {/* KARTA — avval karta raqamining oxirgi 4 raqami, so'ng pul o'tkazilgan vaqt
                    (ikkalasi ham bank ko'chirmasi bilan solishtirish uchun). */}
                {method === 'card' && (
                  <>
                    <DrawerField
                      label="Karta raqami (oxirgi 4 raqam)"
                      hint={'Faqat oxirgi 4 raqam saqlanadi. Moliya → To\'lovlar jadvalida "Kvitansiya" ustunida ko\'rinadi (ixtiyoriy).'}
                    >
                      <div className="flex items-stretch">
                        <span className="flex select-none items-center rounded-l-lg border border-r-0 border-[#dbe0e6] bg-[#e6eaea] px-3 font-mono text-sm tracking-widest text-[#6b7280]">
                          ••••
                        </span>
                        <input
                          type="text"
                          inputMode="numeric"
                          value={cardLast4}
                          // Faqat raqam; kassir to'liq raqam kiritsa ham OXIRGI 4 tasi qoladi
                          // (to'liq karta raqami saqlanmaydi).
                          onChange={(e) => setCardLast4(e.target.value.replace(/\D/g, '').slice(-4))}
                          placeholder="1234"
                          maxLength={4}
                          className="h-9 w-full rounded-r-lg border border-[#dbe0e6] bg-[#f0f2f2] px-3 font-mono text-[14px] tracking-widest text-black outline-none placeholder:text-[#9ca3af] focus:border-brand-600 focus:bg-white focus:ring-1 focus:ring-brand-600"
                        />
                      </div>
                    </DrawerField>

                    <DrawerField
                      label="To'lov vaqti"
                      hint="Karta orqali pul o'tkazilgan vaqt (bank cheki bilan solishtirish uchun)."
                    >
                      <DrawerInput type="time" value={paidTime} onChange={(e) => setPaidTime(e.target.value)} />
                    </DrawerField>
                  </>
                )}

                <DrawerField
                  label="Sanani tanlang"
                  hint="Masalan mijoz bugun to'lagan, lekin tizimga ertaga kiritilayotgan bo'lsa — shu yerda haqiqiy to'lov sanasini tanlang."
                >
                  <DrawerInput type="date" max={today()} value={paidDate} onChange={(e) => setPaidDate(e.target.value)} />
                </DrawerField>

                <DrawerField label="Izoh">
                  <DrawerTextarea
                    rows={2}
                    value={comment}
                    onChange={(e) => setComment(e.target.value)}
                    placeholder="To'lov haqida izoh (ixtiyoriy)..."
                  />
                </DrawerField>
              </>
            )}
          </form>
        ))}
    </div>
  )

  // Telefondagi kassa portali — avvalgi markaziy oyna (portal o'zgarmaydi).
  if (presentation === 'modal') {
    return (
      <Modal
        open={isOpen}
        onClose={onClose}
        size="sm"
        title={title}
        footer={
          <>
            <Button variant="secondary" onClick={onClose}>
              Bekor qilish
            </Button>
            {primaryAction}
          </>
        }
      >
        {body}
      </Modal>
    )
  }

  return (
    <RightDrawer
      open={isOpen}
      onClose={onClose}
      title={title}
      footer={<DrawerActions onBack={onClose}>{primaryAction}</DrawerActions>}
    >
      {body}
    </RightDrawer>
  )
}
