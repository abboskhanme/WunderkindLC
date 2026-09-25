import { useEffect, useMemo, useRef, useState } from 'react'
import type { FormEvent } from 'react'
import type {
  FinanceDirection,
  FinanceTransaction,
  MonthLedger,
  MonthSalary,
  Group,
  Student,
  Teacher,
} from '@/types'
import type { FinanceTransactionPayload } from '@/api/services/finance'
import { getTeachers, getSalaryMonth } from '@/api/services/teachers'
import { getClasses } from '@/api/services/classes'
import { getStudents, getStudentLedger } from '@/api/services/students'
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
import { categoriesByDirection, financeDirectionLabels, formatMonth, monthStatusLabels, paymentMethods } from '@/config/constants'
import { getTransactionTypes, type TransactionType } from '@/api/services/finance'
import { balanceTextCls, formatMoney, cn, apiErrorMessage } from '@/lib/utils'
import { newRequestId } from '@/lib/requestId'

interface Props {
  open: boolean
  onClose: () => void
  /** Saqlash. Promise qaytarsa — tugaguncha "Saqlash" bloklanadi, xato esa forma ichida chiqadi. */
  onSubmit: (values: FinanceTransactionPayload) => void | Promise<void>
  initial?: FinanceTransaction | null
  /**
   * YANGI amal uchun oldindan tanlangan qiymatlar (Kassalar → "Chiqim", Oylik chiqarish → maosh).
   * `direction` berilsa "Yo'nalish" tanlovi YASHIRILADI (panel faqat shu yo'nalish uchun),
   * toifa esa edutizimdagidek "Tranzaksiya" deb nomlanadi. Tahrirda (`initial`) e'tiborsiz.
   */
  preset?: { direction: FinanceDirection; category?: string; teacherId?: string; month?: string }
  /** Panel sarlavhasi (standart — "Yangi moliyaviy amal" / "Amalni tahrirlash"). */
  title?: string
}

const today = () => new Date().toISOString().slice(0, 10)

/** Shu yo'nalish+toifa "oylik maosh chiqimi"mi? */
const isSalaryCat = (direction: FinanceDirection, category: string) =>
  direction === 'expense' && category === 'salary'

const emptyFor = (direction: FinanceDirection): FinanceTransactionPayload => ({
  date: today(),
  direction,
  category: categoriesByDirection[direction][0].value,
  amount: 0,
  note: '',
  method: direction === 'income' ? 'cash' : undefined,
})

export function TransactionFormModal({ open, onClose, onSubmit, initial, preset, title }: Props) {
  const [form, setForm] = useState<FinanceTransactionPayload>(emptyFor('income'))
  const [teachers, setTeachers] = useState<Teacher[]>([])
  const [monthInfo, setMonthInfo] = useState<MonthSalary | null>(null)
  // O'quvchi to'lovi uchun: guruh/o'quvchi tanlash + tanlangan o'quvchining oylar holati
  const [classes, setClasses] = useState<Group[]>([])
  const [students, setStudents] = useState<Student[]>([])
  const [classId, setClassId] = useState('')
  // Tuition to'lovi: avval o'qituvchi, keyin uning guruhi (kaskad)
  const [tuitionTeacherId, setTuitionTeacherId] = useState('')
  const [ledgerMonths, setLedgerMonths] = useState<MonthLedger[]>([])
  // Tanlangan o'quvchining BALANSI (manfiy = qarz, musbat = haqdorlik). null = hali yuklanmagan.
  const [studentBalance, setStudentBalance] = useState<number | null>(null)
  // O'quvchini QIDIRISH — kaskad (o'qituvchi → guruh) o'rniga to'g'ridan-to'g'ri ism/telefon bo'yicha.
  const [studentQuery, setStudentQuery] = useState('')
  // Maosh izohi avtomatik to'ldiriladi — foydalanuvchi qo'lda yozganini bosib ketmaslik uchun
  // oxirgi avto izohni eslab turamiz.
  const autoNoteRef = useRef('')
  // ⚠️ E'LON QILISH JOYI MUHIM: bu holat quyidagi `[open]` effektidan OLDIN turishi shart,
  // aks holda modal yopilganda tozalab bo'lmaydi (JS'da `useState` hoisting qilinmaydi).
  const [salaryType, setSalaryType] = useState<'all' | 'main' | 'substitute'>('all')
  // Markazning tranzaksiya turlari (Moliya → «Tranzaksiya turi»). Bir marta yuklanadi.
  const [txTypes, setTxTypes] = useState<TransactionType[]>([])
  const [submitting, setSubmitting] = useState(false)
  const [error, setError] = useState<string | null>(null)
  /**
   * SINXRON qulf: "Saqlash" (yoki Enter) bir necha marta tez bosilsa ham so'rov BITTA ketadi.
   * ⚠️ Ilgari himoya umuman yo'q edi — har bosish alohida to'lov yozardi.
   */
  const inFlightRef = useRef(false)
  /** Forma ochilishining so'rov kaliti — server takroriy so'rovni avvalgisi sifatida qaytaradi. */
  const requestIdRef = useRef(newRequestId())

  const typeOptions = txTypes.filter((t) => t.direction === form.direction)
  /** Tanlanmagan holatda ko'rsatiladigan tur — joriy toifaga mos birinchisi. */
  const fallbackType =
    typeOptions.find((t) => t.baseCategory === form.category) ?? typeOptions[0]

  const isSalaryExpense = isSalaryCat(form.direction, form.category)
  const isTuitionIncome = form.direction === 'income' && form.category === 'tuition'
  const showTuition = isTuitionIncome && !initial
  // "Qaysi oy uchun" — maoshda foydalanuvchi TANLAYDI (pul berilgan sanadan mustaqil).
  // Tanlanmagan bo'lsa — zaxira sifatida sana oyi ishlatiladi (eski xatti-harakat).
  const month = form.month || form.date?.slice(0, 7)

  /**
   * Tanlangan GURUHDAGI o'quvchilar — TIRIK a'zolik bo'yicha (`classId` — guruh NOMI).
   *
   * ⚠️ Ilgari `s.className === classId` edi, ya'ni eski "asosiy guruh" yorlig'i. U BIRINCHI
   * qo'shilgan guruhda qotib qoladi: o'quvchi yangi guruhga o'tgan bo'lsa, kassir o'sha guruhni
   * tanlaganda ro'yxatda umuman ko'rinmasdi (eski guruhida esa ko'rinib turardi).
   * MUZLATILGAN a'zolik ham qoladi — u guruhga to'lov qilinishi mumkin (qarzini yopish).
   */
  const studentsInClass = useMemo(
    () =>
      classId
        ? students.filter(
            (s) =>
              !s.isArchived &&
              ((s.groupStates?.length ?? 0) > 0
                ? s.groupStates!.some((g) => g.name === classId)
                : s.className === classId),
          )
        : [],
    [students, classId],
  )
  // Faqat guruhi bor o'qituvchilar tanlov ro'yxatida; guruhlar tanlangan o'qituvchiniki.
  const tuitionTeacherOptions = useMemo(
    () =>
      teachers
        .filter((t) => classes.some((c) => c.teacherId === t.id))
        .sort((a, b) => a.fullName.localeCompare(b.fullName)),
    [teachers, classes],
  )
  const tuitionGroups = useMemo(
    () => classes.filter((c) => c.teacherId === tuitionTeacherId),
    [classes, tuitionTeacherId],
  )

  /**
   * Qidiruv natijalari — BARCHA arxivlanmagan o'quvchilar bo'yicha (guruhdan qat'i nazar).
   * Ism ham, telefon ham qidiriladi: raqamlar solishtirilganda format ("+998 90 ...") tashlab
   * yuboriladi, ya'ni "901234567" ham topadi.
   */
  const studentMatches = useMemo(() => {
    const q = studentQuery.trim().toLowerCase()
    if (q.length < 2) return []
    const digits = q.replace(/\D/g, '')
    return students
      .filter((s) => !s.isArchived)
      .filter((s) => {
        if (s.fullName?.toLowerCase().includes(q)) return true
        if (digits.length < 4) return false
        const phones = [s.phone, s.parentPhone].filter(Boolean).join(' ').replace(/\D/g, '')
        return phones.includes(digits)
      })
      .slice(0, 8)
  }, [students, studentQuery])

  /**
   * Qidiruvdan o'quvchi tanlash: guruh va o'qituvchi ham AVTOMATIK to'ldiriladi (o'quvchining
   * faol guruhidan), ya'ni kaskadni qo'lda bosib chiqish shart emas. Guruhi topilmasa
   * to'lov baribir yoziladi — guruh tegi bo'sh qoladi (eski/arxiv guruh holati).
   */
  const pickStudent = (s: Student) => {
    const name = s.groupStates?.[0]?.name ?? s.className ?? ''
    const cls = classes.find((c) => c.name === name)
    setTuitionTeacherId(cls?.teacherId ?? '')
    setClassId(cls?.name ?? '')
    setStudentQuery('')
    onStudentChange(s.id)
  }

  // O'quvchi tanlanganda — uning oylar holatini yuklab, eng eski qarzdor oyni standart qilamiz.
  const onStudentChange = (id: string) => {
    setForm((f) => ({ ...f, studentId: id || undefined, month: undefined }))
    setLedgerMonths([])
    setStudentBalance(null)
    if (!id) return
    getStudentLedger(id).then((l) => {
      setLedgerMonths(l.months)
      setStudentBalance(l.balance)
      const due = l.months.find((m) => m.remaining > 0)
      const target = due ?? l.months[l.months.length - 1]
      setForm((f) => ({
        ...f,
        month: target?.month ?? f.date?.slice(0, 7),
        amount: due ? due.remaining : 0,
      }))
    })
  }

  const onMonthChange = (mo: string) => {
    const ml = ledgerMonths.find((x) => x.month === mo)
    setForm((f) => ({ ...f, month: mo, amount: ml && ml.remaining > 0 ? ml.remaining : f.amount }))
  }

  useEffect(() => {
    if (!open) return
    requestIdRef.current = newRequestId()
    inFlightRef.current = false
    // eslint-disable-next-line react-hooks/set-state-in-effect -- modal ochilganda formani initial bilan sinxronlash (maqsadli)
    setSubmitting(false)
    setError(null)
    setForm(
      initial
        ? {
            date: initial.date,
            direction: initial.direction,
            category: initial.category,
            amount: initial.amount,
            note: initial.note ?? '',
            studentId: initial.studentId,
            teacherId: initial.teacherId,
            // Tahrirda "qaysi oy uchun" saqlanib qolsin (ilgari yo'qolib ketardi).
            // Eski maosh yozuvlarida month bo'sh — orqaga moslik uchun sanadan olinadi.
            month:
              initial.month ??
              (isSalaryCat(initial.direction, initial.category)
                ? initial.date?.slice(0, 7)
                : undefined),
            method: initial.method,
          }
        : preset
          ? {
              ...emptyFor(preset.direction),
              ...(preset.category ? { category: preset.category } : {}),
              teacherId: preset.teacherId,
              // Maosh chiqimida "qaysi oy uchun" bo'sh qolmasin (standart — sana oyi).
              month:
                preset.month ??
                (isSalaryCat(preset.direction, preset.category ?? categoriesByDirection[preset.direction][0].value)
                  ? today().slice(0, 7)
                  : undefined),
            }
          : emptyFor('income'),
    )
    // `preset` — ochilish paytidagi qiymat; har renderda yangi obyekt bo'lgani uchun kuzatilmaydi.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [open, initial])

  // O'qituvchilar (maosh) + guruh/o'quvchilar (o'quvchi to'lovi) ro'yxatlarini API'dan olamiz
  useEffect(() => {
    if (!open) return
    getTeachers().then(setTeachers)
    getClasses().then(setClasses)
    getStudents().then(setStudents)
    // eslint-disable-next-line react-hooks/set-state-in-effect -- modal ochilganda guruh/oylar tanlovini tozalash
    setTuitionTeacherId('')
    setClassId('')
    setLedgerMonths([])
    setStudentBalance(null)
    setStudentQuery('')
    // ⚠️ Maosh TURI ham tozalanadi: ilgari u yopilganda qolib ketardi va keyingi
    // o'qituvchida "O'rinbosarlik haqi" tanlovi kuchda bo'lardi. Uning `substituteFee`si 0
    // bo'lsa "Maosh turi" select'i UMUMAN ko'rinmaydi, ya'ni summa jimgina 0 ga tushar va
    // kassir buni tuzatadigan boshqaruvni ekranda topa olmasdi.
    setSalaryType('all')
    autoNoteRef.current = ''
  }, [open])

  /**
   * O'rinbosarlik haqi uchun taklif qilinadigan summa.
   *
   * ⚠️ QOLDIQ bilan CHEKLANADI: oylik allaqachon to'liq to'langan bo'lsa (`remaining <= 0`)
   * o'rinbosarlik haqini yana to'liq taklif qilish o'qituvchiga ORTIQCHA pul berilishiga olib
   * kelardi. `FinanceTransaction` da "bu to'lov o'rinbosarlik uchun" degan maydon yo'q, ya'ni
   * ledger buni keyin qaytarib ajrata olmaydi — shuning uchun chegara SHU YERDA qo'yiladi.
   */
  const substituteAmount = (remaining: number, subFee: number) =>
    Math.min(subFee, Math.max(0, remaining))

  const handleSalaryTypeChange = (type: 'all' | 'main' | 'substitute') => {
    setSalaryType(type)
    if (!monthInfo) return
    const subFee = monthInfo.substituteFee ?? 0
    const teacherName = teachers.find((t) => t.id === form.teacherId)?.fullName ?? ''
    const typeLabel = type === 'substitute' ? "O'rinbosarlik haqi" : type === 'main' ? 'Asosiy maosh' : 'Umumiy maosh'
    const note = `Oylik maosh (${typeLabel}) — ${teacherName} (${formatMonth(monthInfo.month)})`

    let amt = Math.max(0, monthInfo.remaining)
    if (type === 'substitute') amt = substituteAmount(monthInfo.remaining, subFee)
    else if (type === 'main') amt = Math.max(0, monthInfo.remaining - subFee)

    setForm((f) => ({ ...f, amount: amt, note }))
    autoNoteRef.current = note
  }

  // Oylik maosh + o'qituvchi tanlanganda: shu oy uchun belgilangan/berilgan/qoldiq
  useEffect(() => {
    if (!open || !isSalaryExpense || !form.teacherId || !month) {
      // eslint-disable-next-line react-hooks/set-state-in-effect -- shartlar buzilganda panelni tozalash (maqsadli)
      setMonthInfo(null)
      return
    }
    let active = true
    getSalaryMonth(form.teacherId, month).then((m) => {
      if (!active) return
      setMonthInfo(m)
      // Yangi amalda qoldiqni avtomatik summaga qo'yamiz
      if (!initial && m) {
        const teacherName = teachers.find((t) => t.id === form.teacherId)?.fullName ?? ''
        const typeLabel = salaryType === 'substitute' ? "O'rinbosarlik haqi" : salaryType === 'main' ? 'Asosiy maosh' : 'Umumiy maosh'
        const autoNote = `Oylik maosh (${typeLabel}) — ${teacherName} (${formatMonth(month)})`
        let amt = Math.max(0, m.remaining)
        if (salaryType === 'substitute') amt = substituteAmount(m.remaining, m.substituteFee ?? 0)
        else if (salaryType === 'main') amt = Math.max(0, m.remaining - (m.substituteFee ?? 0))

        setForm((f) => ({
          ...f,
          amount: amt,
          // Izoh bo'sh yoki oldingi AVTO izoh bo'lsa — tanlangan oyga qarab yangilanadi
          note: !f.note?.trim() || f.note === autoNoteRef.current ? autoNote : f.note,
        }))
        autoNoteRef.current = autoNote
      }
    })
    return () => {
      active = false
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps -- teachers nomini faqat prefill uchun ishlatamiz
  }, [open, isSalaryExpense, form.teacherId, month, initial, salaryType])

  // ⚠️ setState EFFEKT ICHIDA to'g'ridan-to'g'ri chaqirilmaydi: so'rov effektda bajariladi,
  // holat esa javob kelgach yangilanadi (`react-hooks/set-state-in-effect`).
  useEffect(() => {
    if (!open) return
    let active = true
    getTransactionTypes()
      .then((list) => {
        if (active) setTxTypes(list)
      })
      .catch(() => {
        // Katalog yuklanmasa forma eski toifalar bilan ishlayveradi (pastdagi fallback).
      })
    return () => {
      active = false
    }
  }, [open])

  const update = <K extends keyof FinanceTransactionPayload>(
    key: K,
    value: FinanceTransactionPayload[K],
  ) => setForm((f) => ({ ...f, [key]: value }))

  // Yo'nalish o'zgarsa, toifani shu yo'nalishning birinchi qiymatiga moslaymiz
  const changeDirection = (direction: FinanceDirection) => {
    setForm((f) => {
      const category = categoriesByDirection[direction][0].value
      return {
        ...f,
        direction,
        category,
        studentId: undefined,
        // "Qaysi oy uchun": maosh chiqimida bo'sh qolmasin (standart — sana oyi)
        month: isSalaryCat(direction, category) ? f.date?.slice(0, 7) : undefined,
        teacherId: undefined,
        method: direction === 'income' ? (f.method ?? 'cash') : undefined,
      }
    })
    setTuitionTeacherId('')
    setClassId('')
    setLedgerMonths([])
    setStudentBalance(null)
    setStudentQuery('')
    autoNoteRef.current = ''
  }

  /**
   * Markaz turini tanlash: `typeId` saqlanadi (jadvalda nomi chiqishi uchun), `category` esa
   * turning tizim toifasidan olinadi — qolgan mantiq (maosh oyi, o'quvchi tanlash) avvalgidek
   * AYNAN toifa bo'yicha ishlaydi.
   */
  const changeType = (typeId: string) => {
    const t = typeOptions.find((x) => x.id === typeId)
    if (!t) return
    setForm((f) => ({ ...f, typeId }))
    changeCategory(t.baseCategory)
  }

  const changeCategory = (category: string) => {
    setForm((f) => ({
      ...f,
      category,
      ...(category !== 'tuition'
        ? {
            studentId: undefined,
            // Maoshga o'tilsa — oy standart (sana oyi) bilan to'ldiriladi, aks holda tozalanadi
            month: isSalaryCat(f.direction, category) ? f.month || f.date?.slice(0, 7) : undefined,
          }
        : {}),
    }))
    if (category !== 'tuition') {
      setTuitionTeacherId('')
      setClassId('')
      setLedgerMonths([])
      setStudentBalance(null)
      setStudentQuery('')
    }
    autoNoteRef.current = ''
  }

  const handleSubmit = async (e: FormEvent) => {
    e.preventDefault()
    if (inFlightRef.current) return
    if (form.amount <= 0 || !form.date) return
    if (isSalaryExpense && !form.teacherId) return
    if (showTuition && !form.studentId) return
    inFlightRef.current = true
    setSubmitting(true)
    setError(null)
    try {
      await onSubmit({
      ...form,
      requestId: requestIdRef.current,
      // Tur EKRANDA ko'ringani bilan yoziladi: foydalanuvchi tegmagan bo'lsa ham zaxira tur
      // saqlanadi, aks holda jadvalda nom bo'sh chiqardi (`fallbackType` izohiga qarang).
      typeId: form.typeId ?? fallbackType?.id,
      note: form.note?.trim() || undefined,
      // teacherId faqat oylik maosh chiqimida; studentId faqat o'quvchi to'lovida saqlanadi
      teacherId: isSalaryExpense ? form.teacherId : undefined,
      studentId: isTuitionIncome ? form.studentId : undefined,
      // month = "QAYSI OY UCHUN" (sana emas!): o'quvchi to'lovida ham, maoshda ham saqlanadi —
      // maosh shu maydon bo'yicha oyga bog'lanadi (SalaryLedger.BuildAsync)
      // (maoshda bo'sh qolsa sana oyi zaxira; o'quvchi to'lovi mantig'i o'zgarmagan)
      month: isSalaryExpense ? month : isTuitionIncome ? form.month : undefined,
      method: form.direction === 'income' ? (form.method || 'cash') : undefined,
      })
      // Muvaffaqiyat — keyingi saqlash (forma ochiq qolsa ham) YANGI amal.
      requestIdRef.current = newRequestId()
    } catch (err) {
      // ⚠️ Forma YOPILMAYDI, kiritilgan qiymatlar qoladi — kalit ham o'sha: qayta urinish
      // allaqachon o'tgan (lekin javobi kelmagan) to'lovni IKKINCHI marta yozmaydi.
      setError(apiErrorMessage(err, "Saqlab bo'lmadi"))
    } finally {
      inFlightRef.current = false
      setSubmitting(false)
    }
  }

  const saveDisabled =
    submitting ||
    form.amount <= 0 || (isSalaryExpense && !form.teacherId) || (showTuition && !form.studentId)
  // Yo'nalish oldindan berilgan (Kassalar → Chiqim) — panel faqat shu yo'nalish uchun.
  const lockedDirection = !initial && !!preset

  return (
    <RightDrawer
      open={open}
      onClose={onClose}
      title={title ?? (initial ? 'Amalni tahrirlash' : 'Yangi moliyaviy amal')}
      footer={
        <DrawerActions onBack={onClose}>
          <Button type="submit" form="finance-form" disabled={saveDisabled}>
            {submitting ? 'Saqlanmoqda...' : 'Saqlash'}
          </Button>
        </DrawerActions>
      }
    >
      <form id="finance-form" onSubmit={(e) => void handleSubmit(e)} className="space-y-4">
        {error && (
          <div className="rounded-lg border border-red-200 bg-red-50 px-3 py-2 text-sm text-red-700">{error}</div>
        )}
        {!lockedDirection && (
          <DrawerField label="Yo'nalish">
            <DrawerSelect
              value={form.direction}
              onChange={(e) => changeDirection(e.target.value as FinanceDirection)}
            >
              <option value="income">{financeDirectionLabels.income}</option>
              <option value="expense">{financeDirectionLabels.expense}</option>
            </DrawerSelect>
          </DrawerField>
        )}
        <DrawerField label={lockedDirection ? 'Tranzaksiya' : 'Turi'}>
          {/*
            Markazning O'Z turlari («Arenda», «Kanstovar») — Moliya → «Tranzaksiya turi»
            katalogidan. Tanlanganda toifa (`category`) turning `baseCategory` sidan olinadi,
            ya'ni hisob-kitob avvalgidek tizim kodi bo'yicha ishlaydi.
            ⚠️ Katalog bo'sh bo'lsa (eski baza) avvalgi toifalar ro'yxati ko'rsatiladi —
            forma hech qachon bo'sh select bilan qolmaydi.
            ⚠️ Tanlov ham bo'sh qolmaydi: `typeId` hali yo'q bo'lsa joriy TOIFAGA mos birinchi
            tur ko'rsatiladi (`fallbackType`) va saqlashda AYNAN o'sha yoziladi — forma
            "Turini tanlang" da qotib turmasin.
          */}
          {typeOptions.length > 0 ? (
            <DrawerSelect
              value={form.typeId ?? fallbackType?.id ?? ''}
              onChange={(e) => changeType(e.target.value)}
            >
              {!form.typeId && !fallbackType && <option value="">Turini tanlang</option>}
              {typeOptions.map((t) => (
                <option key={t.id} value={t.id}>
                  {t.name}
                </option>
              ))}
            </DrawerSelect>
          ) : (
            <DrawerSelect value={form.category} onChange={(e) => changeCategory(e.target.value)}>
              {categoriesByDirection[form.direction].map((c) => (
                <option key={c.value} value={c.value}>
                  {c.label}
                </option>
              ))}
            </DrawerSelect>
          )}
        </DrawerField>

        {/* Oylik maosh: o'qituvchi tanlash + shu oy holati */}
        {isSalaryExpense && (
          <>
            <DrawerField label="O'qituvchini tanlang" required>
              <DrawerSelect
                value={form.teacherId ?? ''}
                onChange={(e) => update('teacherId', e.target.value || undefined)}
              >
                <option value="">— tanlang —</option>
                {teachers.map((t) => (
                  <option key={t.id} value={t.id}>
                    {t.fullName}
                  </option>
                ))}
              </DrawerSelect>
            </DrawerField>

            {/* QAYSI OY UCHUN — pul berilgan sanadan MUSTAQIL (iyul maoshi avgustda berilishi mumkin) */}
            <DrawerField
              label="Oyni tanlang"
              required
              hint={
                <>
                  Maosh qaysi oyga tegishli. Pastdagi <b>Sana</b> — pul berilgan kun; masalan iyul
                  maoshini 5-avgustda berish mumkin.
                </>
              }
            >
              <DrawerInput
                type="month"
                value={form.month ?? ''}
                onChange={(e) => update('month', e.target.value || undefined)}
              />
            </DrawerField>

            {form.teacherId && monthInfo && (monthInfo.substituteFee ?? 0) > 0 && (
              <DrawerField
                label="Maosh turi"
                hint={
                  // Chegara JIMGINA qo'llanmaydi — kassir summa nega kamayganini bilsin.
                  salaryType === 'substitute' &&
                  (monthInfo.substituteFee ?? 0) > Math.max(0, monthInfo.remaining) ? (
                    <span className="font-medium text-amber-600">
                      O'rinbosarlik haqi {formatMoney(monthInfo.substituteFee ?? 0)}, lekin bu oyda
                      qoldiq {formatMoney(Math.max(0, monthInfo.remaining))} — summa qoldiq bilan
                      cheklandi (ortiqcha to'lov bo'lmasin).
                    </span>
                  ) : undefined
                }
              >
                <DrawerSelect
                  value={salaryType}
                  onChange={(e) => handleSalaryTypeChange(e.target.value as 'all' | 'main' | 'substitute')}
                >
                  <option value="all">Umumiy (Asosiy + O'rinbosarlik)</option>
                  <option value="main">Asosiy maosh</option>
                  <option value="substitute">
                    O'rinbosarlik haqi (+{formatMoney(monthInfo.substituteFee ?? 0)})
                  </option>
                </DrawerSelect>
              </DrawerField>
            )}

            {form.teacherId && monthInfo && (
              <div className="grid grid-cols-3 gap-2 rounded-lg border border-[#dbe0e6] bg-[#f0f2f2] p-2 text-center text-sm">
                <InfoCell label={`${formatMonth(monthInfo.month)} belgilangan`} value={formatMoney(monthInfo.expected)} />
                <InfoCell
                  label="Berilgan"
                  value={formatMoney(monthInfo.paid)}
                  valueClass="text-emerald-600"
                />
                <InfoCell
                  label="Qoldiq"
                  value={
                    monthInfo.remaining < 0
                      ? `+${formatMoney(-monthInfo.remaining)}`
                      : formatMoney(monthInfo.remaining)
                  }
                  valueClass={monthInfo.remaining > 0 ? 'text-red-600' : 'text-slate-500'}
                />
              </div>
            )}
            {form.teacherId && monthInfo && monthInfo.remaining <= 0 && (
              <p className="text-xs text-amber-600">
                Bu oy uchun maosh to'liq berilgan — qo'shimcha summa ortiqcha hisoblanadi.
              </p>
            )}
          </>
        )}

        {/* O'quvchi to'lovi: QIDIRUV (tez yo'l) yoki o'qituvchi → guruh → o'quvchi kaskadi */}
        {showTuition && (
          <>
            <DrawerField label="O'quvchini qidirish">
              <DrawerInput
                type="search"
                placeholder="Ism yoki telefon..."
                value={studentQuery}
                onChange={(e) => setStudentQuery(e.target.value)}
              />
              {/*
                Natijalar — maydon ostida. Bosilganda o'quvchi, guruhi va o'qituvchisi BIRGA
                to'ldiriladi (`pickStudent`), ya'ni kaskadni qo'lda bosib chiqish shart emas.
              */}
              {studentMatches.length > 0 && (
                <ul className="mt-1 max-h-48 overflow-y-auto rounded-lg border border-[#dbe0e6] bg-white">
                  {studentMatches.map((s) => (
                    <li key={s.id}>
                      <button
                        type="button"
                        onClick={() => pickStudent(s)}
                        className="flex w-full items-center justify-between gap-2 px-3 py-2 text-left text-[13px] hover:bg-[#f0f2f2]"
                      >
                        <span className="min-w-0">
                          <span className="block truncate font-medium text-[#333]">{s.fullName}</span>
                          <span className="block truncate text-[12px] text-[#6b7280]">
                            {s.groupStates?.[0]?.name || s.className || "— guruhsiz —"}
                          </span>
                        </span>
                        <span className={cn('whitespace-nowrap text-[12px] font-semibold', balanceTextCls(s.balance))}>
                          {formatMoney(s.balance)}
                        </span>
                      </button>
                    </li>
                  ))}
                </ul>
              )}
              {studentQuery.trim().length >= 2 && studentMatches.length === 0 && (
                <p className="mt-1 text-[12px] text-[#6b7280]">Topilmadi.</p>
              )}
            </DrawerField>
            <DrawerField label="O'qituvchini tanlang">
              <DrawerSelect
                value={tuitionTeacherId}
                onChange={(e) => {
                  // O'qituvchi o'zgarsa — guruh va o'quvchi tanlovi tozalanadi.
                  setTuitionTeacherId(e.target.value)
                  setClassId('')
                  onStudentChange('')
                }}
              >
                <option value="">— o'qituvchi —</option>
                {tuitionTeacherOptions.map((t) => (
                  <option key={t.id} value={t.id}>
                    {t.fullName}
                  </option>
                ))}
              </DrawerSelect>
            </DrawerField>
            <DrawerField label="Guruh">
              <DrawerSelect
                value={classId}
                disabled={!tuitionTeacherId}
                onChange={(e) => {
                  setClassId(e.target.value)
                  onStudentChange('')
                }}
              >
                <option value="">{tuitionTeacherId ? '— guruh —' : "— avval o'qituvchi —"}</option>
                {tuitionGroups.map((c) => (
                  <option key={c.id} value={c.name}>
                    {c.name}
                  </option>
                ))}
              </DrawerSelect>
            </DrawerField>
            <DrawerField label="O'quvchini tanlang" required>
              <DrawerSelect
                value={form.studentId ?? ''}
                onChange={(e) => onStudentChange(e.target.value)}
                disabled={!classId}
              >
                <option value="">{classId ? "— o'quvchi —" : 'avval guruh'}</option>
                {studentsInClass.map((s) => (
                  <option key={s.id} value={s.id}>
                    {s.fullName}
                  </option>
                ))}
              </DrawerSelect>
            </DrawerField>

            {/*
              BALANS — kassir to'lovni kiritishdan OLDIN "qarzi bormi yoki haqdormi" ni ko'rishi
              kerak. Manfiy = qarz (qizil), musbat = haqdorlik/avans (yashil).
              Ranglar yagona joydan — `lib/utils.ts` (jurnal va guruh ro'yxati bilan bir xil).
            */}
            {form.studentId && studentBalance !== null && (
              <DrawerField label="Balansi">
                <p className={cn('text-[15px] font-semibold', balanceTextCls(studentBalance))}>
                  {formatMoney(studentBalance)}
                  <span className="ml-2 text-[12px] font-normal text-[#6b7280]">
                    {studentBalance < 0 ? 'qarz' : studentBalance > 0 ? 'haqdor (avans)' : "qarzi yo'q"}
                  </span>
                </p>
              </DrawerField>
            )}

            {form.studentId && (
              <DrawerField label="Qaysi oy uchun">
                <DrawerSelect value={form.month ?? ''} onChange={(e) => onMonthChange(e.target.value)}>
                  {(ledgerMonths.length ? ledgerMonths.map((m) => m.month) : [form.date?.slice(0, 7) ?? '']).map(
                    (mo) => {
                      const m = ledgerMonths.find((x) => x.month === mo)
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
                    },
                  )}
                </DrawerSelect>
              </DrawerField>
            )}
          </>
        )}

        <DrawerField label="Qiymat (so'm)" required>
          <DrawerInput
            type="number"
            min={0}
            step="any"
            required
            value={form.amount}
            onChange={(e) => update('amount', Number(e.target.value))}
          />
        </DrawerField>
        {form.direction === 'income' && (
          <DrawerField label="To'lov turi" group>
            <DrawerChoice
              options={paymentMethods}
              value={form.method ?? 'cash'}
              onChange={(v) => update('method', v)}
            />
          </DrawerField>
        )}
        <DrawerField
          label={isSalaryExpense ? 'Sanani tanlang (berilgan kun)' : 'Sanani tanlang'}
          required
          hint={isSalaryExpense ? 'Pul haqiqatda berilgan kun.' : undefined}
        >
          <DrawerInput
            type="date"
            required
            value={form.date}
            onChange={(e) => update('date', e.target.value)}
          />
        </DrawerField>

        <DrawerField label="Izoh">
          <DrawerTextarea rows={2} value={form.note} onChange={(e) => update('note', e.target.value)} />
        </DrawerField>
      </form>
    </RightDrawer>
  )
}

function InfoCell({
  label,
  value,
  valueClass = 'text-slate-700',
}: {
  label: string
  value: string
  valueClass?: string
}) {
  return (
    <div className="rounded-lg bg-white px-2 py-1.5">
      <p className="text-xs text-slate-400">{label}</p>
      <p className={cn('mt-0.5 font-mono font-semibold', valueClass)}>{value}</p>
    </div>
  )
}
