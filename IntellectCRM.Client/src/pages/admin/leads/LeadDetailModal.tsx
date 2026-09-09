import { useEffect, useMemo, useState } from 'react'
import {
  Pencil,
  Trash2,
  Send,
  CheckCircle2,
  CalendarClock,
  History,
  GraduationCap,
  MessageSquare,
  Receipt,
  Phone,
} from 'lucide-react'
import { ReceiptModal } from '@/components/finance/ReceiptModal'
import { CallPickerModal, type CallOption } from '@/components/CallPickerModal'
import { getPickableTemplates, sendLeadSms, type PickableTemplate } from '@/api/services/messages'
import { getMessageTokens } from '@/api/services/autoMessages'
import { MessageEditor, type TokenDef } from '@/components/messaging/MessageEditor'
import { getLevelTests, sendLeadTest } from '@/api/services/levelTests'
import type { LevelTestListItem } from '@/types'
import type { Lead, LeadEvent, LeadEventType, TrialLesson, Group, Teacher } from '@/types'
import { Modal } from '@/components/ui/Modal'
import { Button } from '@/components/ui/Button'
import { Input, Select } from '@/components/ui/Input'
import { genderLabels } from '@/config/constants'
import { formatDate, formatDateTime } from '@/lib/utils'
import {
  getLeadEvents,
  addLeadEvent,
  getLeadTrials,
  scheduleTrial,
  setTrialResult,
  convertLead,
} from '@/api/services/leads'
import { getLeadAnswers, type LeadAnswer } from '@/api/services/leadEntryForm'
import { getClasses } from '@/api/services/classes'
import { getTeachers } from '@/api/services/teachers'

interface Props {
  lead: Lead | null
  /** Lid o'qiydigan tashqi maktab NOMI (id → nom LeadsPage'da yechiladi) */
  schoolName?: string
  onClose: () => void
  onEdit: (lead: Lead) => void
  onDelete: (lead: Lead) => void
  /** Lid aylantirilgandan keyin ro'yxatni yangilash uchun */
  onConverted?: (leadId: string, studentId: string) => void
  /** Ruxsat: tahrirlash tugmasi ko'rinsinmi (default true) */
  canEdit?: boolean
  /** Ruxsat: o'chirish tugmasi ko'rinsinmi (default true) */
  canDelete?: boolean
}

function Row({ label, value, mono }: { label: string; value: string; mono?: boolean }) {
  return (
    <div className="flex justify-between gap-4 border-b border-slate-100 py-2.5 last:border-0">
      <span className="text-sm text-slate-400">{label}</span>
      <span
        className={`text-right text-sm font-medium text-slate-800 ${mono ? 'font-mono' : ''}`}
      >
        {value}
      </span>
    </div>
  )
}

/** Ism-sharifdan bosh harflar (avatar uchun) */
function leadInitials(name: string): string {
  const parts = name.trim().split(/\s+/).filter(Boolean)
  if (parts.length === 0) return '?'
  if (parts.length === 1) return parts[0].slice(0, 2).toUpperCase()
  return (parts[0][0] + parts[1][0]).toUpperCase()
}

const eventTypeLabels: Record<LeadEventType, string> = {
  note: 'Izoh',
  stage: 'Bosqich',
  call: "Qo'ng'iroq",
  trial: 'Sinov darsi',
  convert: 'Aylantirildi',
  created: 'Yaratildi',
}

const eventTypeColors: Record<LeadEventType, string> = {
  note: 'bg-slate-100 text-slate-600',
  stage: 'bg-blue-50 text-blue-600',
  call: 'bg-amber-50 text-amber-600',
  trial: 'bg-violet-50 text-violet-600',
  convert: 'bg-emerald-50 text-emerald-600',
  created: 'bg-slate-100 text-slate-500',
}

/**
 * ⚠️ IKKI BOSHQA savol: «Keldi/Kelmadi» — lid sinovga TASHRIF BUYURDIMI; «Qoldi/Ketdi» —
 * tashrifdan keyin markazda QOLDIMI. Ilgari faqat ikkinchisi bor edi, ya'ni "keldi, lekin
 * ketdi" bilan "umuman kelmadi" bir xil ko'rinardi — holbuki kiruvchi adminning asosiy
 * ko'rsatkichi aynan "lid → sinovga KELISH" konversiyasi.
 */
const trialResultLabels: Record<TrialLesson['result'], string> = {
  pending: 'Kutilmoqda',
  came: 'Keldi',
  no_show: 'Kelmadi',
  stayed: 'Qoldi',
  left: 'Ketdi',
}

const trialResultColors: Record<TrialLesson['result'], string> = {
  pending: 'bg-amber-50 text-amber-600',
  came: 'bg-sky-50 text-sky-600',
  no_show: 'bg-slate-100 text-slate-500',
  stayed: 'bg-emerald-50 text-emerald-600',
  left: 'bg-rose-50 text-rose-600',
}

/** Hafta kunlari qisqartmasi — 0=Dushanba .. 6=Yakshanba (backend Group.days bilan mos). */
const WD_SHORT = ['Du', 'Se', 'Chor', 'Pay', 'Jum', 'Shan', 'Yak']

/** JS getDay() (0=Yakshanba) → bizning indeks (0=Dushanba). */
const toMonFirst = (jsDay: number) => (jsDay + 6) % 7

/** ISO sana "YYYY-MM-DD" (mahalliy, TZ siljishisiz). */
const isoDate = (d: Date) =>
  `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`

/** Bugundan boshlab guruh kunlariga (days, 0=Du..6=Yak) mos keyingi `count` dars sanasi. */
function nextLessonDates(days: number[], count: number): Date[] {
  const out: Date[] = []
  const base = new Date()
  base.setHours(0, 0, 0, 0)
  for (let i = 0; i < 90 && out.length < count; i++) {
    const d = new Date(base)
    d.setDate(base.getDate() + i)
    if (days.includes(toMonFirst(d.getDay()))) out.push(d)
  }
  return out
}

export function LeadDetailModal({
  lead,
  schoolName,
  onClose,
  onEdit,
  onDelete,
  onConverted,
  canEdit = true,
  canDelete = true,
}: Props) {
  const leadId = lead?.id ?? null

  const [events, setEvents] = useState<LeadEvent[]>([])
  const [trials, setTrials] = useState<TrialLesson[]>([])
  const [groups, setGroups] = useState<Group[]>([])
  const [teachers, setTeachers] = useState<Teacher[]>([])

  const [noteText, setNoteText] = useState('')
  const [savingNote, setSavingNote] = useState(false)

  // Lidga SMS yuborish
  const [smsTemplates, setSmsTemplates] = useState<PickableTemplate[]>([])
  const [smsTokens, setSmsTokens] = useState<TokenDef[]>([])
  const [smsText, setSmsText] = useState('')
  const [smsSending, setSmsSending] = useState(false)
  const [smsResult, setSmsResult] = useState<string | null>(null)

  // Lidga daraja testi havolasini yuborish (bir martalik)
  const [levelTests, setLevelTests] = useState<LevelTestListItem[]>([])
  const [sendTestId, setSendTestId] = useState('')
  const [sendingTest, setSendingTest] = useState(false)
  const [sendTestResult, setSendTestResult] = useState<string | null>(null)

  const [trialTeacherId, setTrialTeacherId] = useState('')
  const [trialGroupId, setTrialGroupId] = useState('')
  const [trialAt, setTrialAt] = useState('')
  // Sinov darsi cheki (to'lovsiz ro'yxat varaqasi) — qaysi trial cheki ochiq + avto-print.
  const [receiptTrial, setReceiptTrial] = useState<string | null>(null)
  const [receiptAuto, setReceiptAuto] = useState(false)
  const [savingTrial, setSavingTrial] = useState(false)

  const [convertTeacherId, setConvertTeacherId] = useState('')
  const [convertGroupId, setConvertGroupId] = useState('')
  const [convertDate, setConvertDate] = useState('')
  const [converting, setConverting] = useState(false)
  // Modalni yopmasdan aylantirish holatini kuzatish uchun lokal nusxa
  const [convertedStudentId, setConvertedStudentId] = useState<string | null>(null)

  // Qo'ng'iroq qilish oynasi
  const [callOpen, setCallOpen] = useState(false)

  // «Lid kiritish formasi» QO'SHIMCHA savollariga berilgan javoblar (savol matni + javob(lar)).
  const [answers, setAnswers] = useState<LeadAnswer[]>([])

  const refreshTimeline = (id: string) => {
    getLeadEvents(id).then(setEvents).catch(() => setEvents([]))
    getLeadTrials(id).then(setTrials).catch(() => setTrials([]))
  }

  useEffect(() => {
    if (!leadId) return
    setNoteText('')
    setTrialTeacherId('')
    setTrialGroupId('')
    setTrialAt('')
    setConvertTeacherId('')
    setConvertGroupId('')
    setConvertDate('')
    setConvertedStudentId(lead?.convertedStudentId ?? null)
    setSmsText('')
    setSmsResult(null)
    setSendTestId('')
    setSendTestResult(null)
    refreshTimeline(leadId)
    // Qo'shimcha savollar javobi: sozlama bo'sh bo'lsa ham so'rov yengil (bo'sh ro'yxat qaytadi),
    // xato bo'lsa bo'lim shunchaki chizilmaydi — lid oynasining qolgani ishlayveradi.
    // ⚠️ Avval TOZALANADI: aks holda boshqa lid ochilganda eskisining javoblari qisqa vaqt
    // ko'rinib turardi (bir lidning ma'lumoti boshqasinikidek o'qilardi).
    setAnswers([])
    getLeadAnswers(leadId).then(setAnswers).catch(() => setAnswers([]))
    getPickableTemplates('lead').then(setSmsTemplates).catch(() => setSmsTemplates([]))
    // Lid uchun faqat lid + umumiy guruh tokenlari mos keladi
    getMessageTokens()
      .then((ts) => setSmsTokens(ts.filter((t) => t.group === 'lead' || t.group === 'common')))
      .catch(() => setSmsTokens([]))
    getLevelTests().then((ts) => setLevelTests(ts.filter((t) => t.isActive))).catch(() => setLevelTests([]))
    getClasses()
      .then((gs) => setGroups(gs.filter((g) => !g.isArchived)))
      .catch(() => setGroups([]))
    getTeachers().then(setTeachers).catch(() => setTeachers([]))
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [leadId])

  // Guruh biriktirish — avval o'qituvchi, keyin uning guruhi (kaskad). Faqat guruhi bor o'qituvchilar.
  const teacherOptions = useMemo(
    () =>
      teachers
        .filter((t) => groups.some((g) => g.teacherId === t.id))
        .sort((a, b) => a.fullName.localeCompare(b.fullName)),
    [teachers, groups],
  )
  const convertGroups = useMemo(
    () => groups.filter((g) => g.teacherId === convertTeacherId),
    [groups, convertTeacherId],
  )
  const trialGroups = useMemo(
    () => groups.filter((g) => g.teacherId === trialTeacherId),
    [groups, trialTeacherId],
  )

  const handleAddNote = async () => {
    if (!leadId || !noteText.trim()) return
    setSavingNote(true)
    try {
      await addLeadEvent(leadId, 'note' as LeadEventType, noteText.trim())
      setNoteText('')
      const fresh = await getLeadEvents(leadId)
      setEvents(fresh)
    } finally {
      setSavingNote(false)
    }
  }

  const leadPhone = lead?.phone || lead?.fatherPhone || lead?.motherPhone || ''
  const callNumbers: CallOption[] = lead
    ? ([
        { label: "O'z raqami", number: lead.phone },
        { label: 'Otasi', number: lead.fatherPhone },
        { label: 'Onasi', number: lead.motherPhone },
      ].filter((n) => n.number) as CallOption[])
    : []
  const handleSendSms = async () => {
    if (!leadId || !smsText.trim() || smsSending) return
    setSmsSending(true)
    setSmsResult(null)
    try {
      // Kanal (Eskiz/Local) tanlanmaydi — server Sozlamalardagi qiymatdan o'zi oladi.
      const b = await sendLeadSms(leadId, smsText.trim())
      setSmsResult(b.sentCount > 0 ? 'SMS yuborildi ✓' : 'Yuborildi (holat kutilmoqda)')
      setSmsText('')
      refreshTimeline(leadId)
    } catch (err: unknown) {
      setSmsResult(
        (err as { response?: { data?: { message?: string } } })?.response?.data?.message ??
          'Yuborishda xatolik',
      )
    } finally {
      setSmsSending(false)
    }
  }

  const handleSendTest = async () => {
    if (!leadId || !sendTestId || sendingTest) return
    setSendingTest(true)
    setSendTestResult(null)
    try {
      const r = await sendLeadTest(leadId, sendTestId)
      setSendTestResult(r.ok ? 'Test havolasi SMS yuborildi ✓' : `Yuborilmadi: ${r.status}`)
      refreshTimeline(leadId)
    } catch (err: unknown) {
      setSendTestResult(
        (err as { response?: { data?: { message?: string } } })?.response?.data?.message ??
          'Yuborishda xatolik',
      )
    } finally {
      setSendingTest(false)
    }
  }

  const handleScheduleTrial = async (e: React.FormEvent) => {
    e.preventDefault()
    if (!leadId || !trialGroupId || !trialAt) return
    setSavingTrial(true)
    try {
      const tid = await scheduleTrial(leadId, trialGroupId, trialAt)
      setTrialGroupId('')
      setTrialAt('')
      refreshTimeline(leadId)
      // Sinov darsiga yozildi — chekni avtomatik ochib, print dialogini chiqaramiz.
      if (tid) {
        setReceiptAuto(true)
        setReceiptTrial(tid)
      }
    } finally {
      setSavingTrial(false)
    }
  }

  /** Mavjud sinov darsi uchun chekni qayta ochish (avto-print yo'q). */
  const openTrialReceipt = (trialId: string) => {
    setReceiptAuto(false)
    setReceiptTrial(trialId)
  }

  // Sinov formasi uchun: tanlangan guruh + uning dars kunlari/vaqti + keyingi dars sanalari.
  const selectedTrialGroup = groups.find((g) => g.id === trialGroupId) || null
  const trialHasSchedule = !!(selectedTrialGroup?.days?.length && selectedTrialGroup?.startTime)
  const trialDates = trialHasSchedule ? nextLessonDates(selectedTrialGroup!.days!, 8) : []

  const handleTrialResult = async (trialId: string, result: TrialLesson['result']) => {
    if (!leadId) return
    await setTrialResult(trialId, result)
    // Ro'yxat ham yangilanadi: natija va "kelgan sana" darhol ko'rinsin.
    getLeadTrials(leadId).then(setTrials).catch(() => {})
    refreshTimeline(leadId)
  }

  const handleConvert = async () => {
    if (!leadId) return
    setConverting(true)
    try {
      const { studentId } = await convertLead(leadId, {
        enrollmentDate: convertDate || undefined,
        groupId: convertGroupId || undefined,
      })
      setConvertedStudentId(studentId)
      onConverted?.(leadId, studentId)
      if (leadId) refreshTimeline(leadId)
    } catch (err: unknown) {
      const message =
        (err as { response?: { data?: { message?: string } } })?.response?.data?.message ??
        'Aylantirishda xatolik yuz berdi'
      alert(message)
    } finally {
      setConverting(false)
    }
  }

  return (
    <>
    <Modal
      open={!!lead}
      onClose={onClose}
      title="Lid ma'lumotlari"
      size="lg"
      footer={
        lead && (
          <>
            {canDelete && (
              <Button variant="danger" onClick={() => onDelete(lead)} className="mr-auto">
                <Trash2 className="h-4 w-4" /> O'chirish
              </Button>
            )}
            <Button variant="secondary" onClick={onClose}>
              Yopish
            </Button>
            {canEdit && (
              <Button onClick={() => onEdit(lead)}>
                <Pencil className="h-4 w-4" /> Tahrirlash
              </Button>
            )}
          </>
        )
      }
    >
      {lead && (
        <div className="space-y-6">
          {/* Sarlavha — avatar + ism */}
          <div className="flex items-center gap-3">
            <span className="avatar h-12 w-12 bg-brand-500 text-base">
              {leadInitials(lead.fullName)}
            </span>
            <div className="min-w-0">
              <p className="truncate text-lg font-bold tracking-tight text-slate-800">
                {lead.fullName}
              </p>
              <p className="text-sm text-slate-400">
                {genderLabels[lead.gender]}
                {lead.interestSubject ? ` · ${lead.interestSubject}` : ''}
              </p>
            </div>
          </div>

          {/* Asosiy ma'lumotlar */}
          <div>
            <Row label="Jinsi" value={genderLabels[lead.gender]} />
            <Row label="Tug'ilgan kun" value={formatDate(lead.birthDate)} mono />
            <Row label="O'z raqami" value={lead.phone || '—'} mono />
            <Row label="Otasi" value={lead.fatherFullName || '—'} />
            <Row label="Otasi raqami" value={lead.fatherPhone || '—'} mono />
            <Row label="Onasi" value={lead.motherFullName || '—'} />
            <Row label="Onasi raqami" value={lead.motherPhone || '—'} mono />
            {callNumbers.length > 0 && (
              <div className="flex justify-end border-b border-slate-100 py-2.5 last:border-0">
                <Button variant="secondary" className="px-3 py-1.5 text-sm" onClick={() => setCallOpen(true)}>
                  <Phone className="h-4 w-4" /> Qo'ng'iroq qilish
                </Button>
              </div>
            )}
            <Row label="Manba" value={lead.source || '—'} />
            <Row label="Qiziqqan fani" value={lead.interestSubject || '—'} />
            <Row label="Maktab" value={schoolName || '—'} />
            {lead.createdAt && <Row label="Yaratilgan" value={formatDate(lead.createdAt)} mono />}
            {/* TAKRORIY MUROJAAT — forma/daraja testi orqali yana yozilgan (bosqichi o'zgarmaydi,
                shuning uchun bu yerda ochiq yozib qo'yiladi; tafsiloti tarixda). */}
            {!!lead.repeatCount && lead.repeatCount > 0 && (
              <Row
                label="Takroriy murojaat"
                value={
                  `${lead.repeatCount} marta` +
                  (lead.lastRepeatAt ? ` · oxirgisi ${formatDate(lead.lastRepeatAt)}` : '')
                }
              />
            )}
            {lead.convertedStudentId && lead.firstLessonAttendance && (
              <div className="border-b border-slate-100 py-2.5">
                <div className="flex justify-between gap-4">
                  <span className="text-sm text-slate-400">Birinchi dars davomat</span>
                  <span className="text-right">
                    {lead.firstLessonAttendance === 'attended' && (
                      <span className="inline-flex items-center gap-1.5 rounded-full bg-emerald-50 px-3 py-1.5 text-sm font-medium text-emerald-700">
                        ✓ Keldi
                      </span>
                    )}
                    {lead.firstLessonAttendance === 'absent' && (
                      <span className="inline-flex items-center gap-1.5 rounded-full bg-rose-50 px-3 py-1.5 text-sm font-medium text-rose-700">
                        ✗ Kelmadi
                      </span>
                    )}
                    {lead.firstLessonAttendance === 'no-lesson' && (
                      <span className="inline-flex items-center gap-1.5 rounded-full bg-slate-50 px-3 py-1.5 text-sm font-medium text-slate-600">
                        — Dars yo&apos;q
                      </span>
                    )}
                  </span>
                </div>
              </div>
            )}
            {lead.note && (
              <div className="pt-3">
                <p className="mb-1 text-sm text-slate-400">Izoh</p>
                <p className="rounded-lg bg-slate-50 px-3 py-2 text-sm text-slate-700">{lead.note}</p>
              </div>
            )}
          </div>

          {/* QO'SHIMCHA MA'LUMOT — «Lid kiritish formasi»dagi markaz qo'shgan savollar javobi.
              Javob yo'q bo'lsa bo'lim UMUMAN chizilmaydi: sozlamada savol qo'shmagan markaz
              lid oynasida bo'm-bo'sh sarlavha ko'rmasin. */}
          {answers.length > 0 && (
            <section className="rounded-xl border border-slate-100 p-4">
              <h4 className="mb-3 font-semibold text-slate-800">Qo&apos;shimcha ma&apos;lumot</h4>
              <div className="space-y-3">
                {answers.map((a, i) => (
                  <div key={`${a.question}-${i}`}>
                    <p className="text-xs text-slate-400">{a.question}</p>
                    <p className="text-sm text-slate-800">{a.answers.join(', ') || '—'}</p>
                  </div>
                ))}
              </div>
            </section>
          )}

          {/* SMS yuborish */}
          <section className="rounded-xl border border-slate-100 p-4">
            <h4 className="mb-3 flex items-center gap-2 font-semibold text-slate-800">
              <MessageSquare className="h-4 w-4 text-brand-600" /> SMS yuborish
            </h4>
            {!leadPhone ? (
              <p className="text-sm text-slate-400">Lidda telefon raqami yo'q.</p>
            ) : (
              <div className="space-y-3">
                <p className="text-xs text-slate-400">
                  Raqam: <span className="font-mono text-slate-600">{leadPhone}</span>
                </p>
                <MessageEditor
                  value={smsText}
                  onChange={setSmsText}
                  tokens={smsTokens}
                  templates={smsTemplates.map((t) => ({ name: t.name, text: t.text }))}
                  showSmsCounter
                  rows={3}
                  placeholder="SMS matni (shablon tanlang yoki yozing)..."
                />
                <div className="flex items-center justify-between gap-3">
                  {smsResult && (
                    <p className={`text-sm font-medium ${smsResult.includes('✓') ? 'text-emerald-700' : 'text-amber-700'}`}>
                      {smsResult}
                    </p>
                  )}
                  <Button className="ml-auto" onClick={handleSendSms} disabled={!smsText.trim() || smsSending}>
                    <Send className="h-4 w-4" /> {smsSending ? 'Yuborilmoqda...' : 'SMS yuborish'}
                  </Button>
                </div>
              </div>
            )}
          </section>

          {/* Daraja testi havolasini yuborish (bir martalik) */}
          <section className="rounded-xl border border-slate-100 p-4">
            <h4 className="mb-3 flex items-center gap-2 font-semibold text-slate-800">
              <GraduationCap className="h-4 w-4 text-brand-600" /> Daraja testi yuborish
            </h4>
            {!leadPhone ? (
              <p className="text-sm text-slate-400">Lidda telefon raqami yo'q.</p>
            ) : levelTests.length === 0 ? (
              <p className="text-sm text-slate-400">Faol daraja testi yo'q.</p>
            ) : (
              <div className="space-y-3">
                <p className="text-xs text-slate-400">
                  Tanlangan test uchun <b>bir martalik havola</b> SMS qilib yuboriladi. Lid ma'lumotini qayta
                  kiritmaydi; natija shu lidga bog'lanadi. (SMS andoza: "daraja testi havolasi" — {'{link}'} tokeni)
                </p>
                <div className="flex items-center gap-2">
                  <Select value={sendTestId} onChange={(e) => setSendTestId(e.target.value)} className="flex-1">
                    <option value="">— Testni tanlang —</option>
                    {levelTests.map((t) => (
                      <option key={t.id} value={t.id}>{t.title}</option>
                    ))}
                  </Select>
                  <Button onClick={handleSendTest} disabled={!sendTestId || sendingTest}>
                    <Send className="h-4 w-4" /> {sendingTest ? 'Yuborilmoqda...' : 'Yuborish'}
                  </Button>
                </div>
                {sendTestResult && (
                  <p className={`text-sm font-medium ${sendTestResult.includes('✓') ? 'text-emerald-700' : 'text-amber-700'}`}>
                    {sendTestResult}
                  </p>
                )}
              </div>
            )}
          </section>

          {/* O'quvchiga aylantirish */}
          <section className="rounded-xl border border-slate-100 p-4">
            <h4 className="mb-3 flex items-center gap-2 font-semibold text-slate-800">
              <GraduationCap className="h-4 w-4 text-emerald-600" /> O'quvchiga aylantirish
            </h4>
            {convertedStudentId ? (
              <div className="inline-flex items-center gap-2 rounded-lg bg-emerald-50 px-3 py-2 text-sm font-medium text-emerald-700">
                <CheckCircle2 className="h-4 w-4" /> Aylantirilgan
              </div>
            ) : (
              <div className="space-y-3">
                <div className="grid grid-cols-1 gap-3 sm:grid-cols-3">
                  <Select
                    label="O'qituvchi (ixtiyoriy)"
                    value={convertTeacherId}
                    onChange={(e) => {
                      setConvertTeacherId(e.target.value)
                      setConvertGroupId('')
                    }}
                  >
                    <option value="">— biriktirmaslik —</option>
                    {teacherOptions.map((t) => (
                      <option key={t.id} value={t.id}>
                        {t.fullName}
                      </option>
                    ))}
                  </Select>
                  <Select
                    label="Guruh"
                    value={convertGroupId}
                    disabled={!convertTeacherId}
                    onChange={(e) => setConvertGroupId(e.target.value)}
                  >
                    <option value="">{convertTeacherId ? '— guruh —' : "— avval o'qituvchi —"}</option>
                    {convertGroups.map((g) => (
                      <option key={g.id} value={g.id}>
                        {g.name}
                      </option>
                    ))}
                  </Select>
                  <Input
                    label="Qabul sanasi (ixtiyoriy)"
                    type="date"
                    value={convertDate}
                    onChange={(e) => setConvertDate(e.target.value)}
                  />
                </div>
                <Button onClick={handleConvert} disabled={converting}>
                  <GraduationCap className="h-4 w-4" />
                  {converting ? 'Aylantirilmoqda...' : "O'quvchiga aylantirish"}
                </Button>
              </div>
            )}
          </section>

          {/* Sinov darslari */}
          <section className="rounded-xl border border-slate-100 p-4">
            <h4 className="mb-3 flex items-center gap-2 font-semibold text-slate-800">
              <CalendarClock className="h-4 w-4 text-violet-600" /> Sinov darslari
            </h4>

            {trials.length === 0 ? (
              <p className="mb-3 text-sm text-slate-400">Sinov darslari belgilanmagan.</p>
            ) : (
              <ul className="mb-3 space-y-2">
                {trials.map((t) => (
                  <li
                    key={t.id}
                    className="flex flex-wrap items-center justify-between gap-2 rounded-lg border border-slate-100 px-3 py-2"
                  >
                    <div className="min-w-0">
                      <p className="text-sm font-medium text-slate-800">{t.groupName}</p>
                      <p className="font-mono text-xs text-slate-400">{formatDateTime(t.scheduledAt)}</p>
                      {t.attendedAt && (
                        <p className="text-xs text-sky-600">Sinovga keldi: {formatDate(t.attendedAt)}</p>
                      )}
                    </div>
                    <div className="flex items-center gap-2">
                      <span
                        className={`rounded-full px-2.5 py-0.5 text-xs font-medium ${trialResultColors[t.result]}`}
                      >
                        {trialResultLabels[t.result]}
                      </span>
                      <button
                        type="button"
                        title="Chek (sinov darsi varaqasi)"
                        onClick={() => openTrialReceipt(t.id)}
                        className="rounded-lg p-1.5 text-slate-400 transition-colors hover:bg-violet-50 hover:text-violet-600"
                      >
                        <Receipt className="h-4 w-4" />
                      </button>
                      {/* 1-QADAM — TASHRIF: "Keldi/Kelmadi" faqat natija hali belgilanmaganda.
                          Kelgan sana shu tugmadan yoziladi va KPI konversiyasi shundan hisoblanadi. */}
                      {t.result === 'pending' && (
                        <>
                          <Button
                            variant="secondary"
                            className="px-2.5 py-1 text-xs"
                            onClick={() => handleTrialResult(t.id, 'came')}
                          >
                            Keldi
                          </Button>
                          <Button
                            variant="secondary"
                            className="px-2.5 py-1 text-xs"
                            onClick={() => handleTrialResult(t.id, 'no_show')}
                          >
                            Kelmadi
                          </Button>
                        </>
                      )}
                      {/* 2-QADAM — NATIJA: "Qoldi/Ketdi" avvalgidek ishlaydi. Sinovga kelgani
                          belgilangandan keyin ham ko'rinadi (tashrifdan keyingi qaror). */}
                      {(t.result === 'pending' || t.result === 'came') && (
                        <>
                          <Button
                            variant="secondary"
                            className="px-2.5 py-1 text-xs"
                            onClick={() => handleTrialResult(t.id, 'stayed')}
                          >
                            Qoldi
                          </Button>
                          <Button
                            variant="secondary"
                            className="px-2.5 py-1 text-xs"
                            onClick={() => handleTrialResult(t.id, 'left')}
                          >
                            Ketdi
                          </Button>
                        </>
                      )}
                    </div>
                  </li>
                ))}
              </ul>
            )}

            <form onSubmit={handleScheduleTrial} className="space-y-3">
              <div className="grid grid-cols-1 gap-3 sm:grid-cols-2">
                <Select
                  label="O'qituvchi"
                  value={trialTeacherId}
                  onChange={(e) => {
                    // O'qituvchi o'zgardi — guruh va tanlangan kun bekor qilinadi.
                    setTrialTeacherId(e.target.value)
                    setTrialGroupId('')
                    setTrialAt('')
                  }}
                >
                  <option value="">— o'qituvchi tanlang —</option>
                  {teacherOptions.map((t) => (
                    <option key={t.id} value={t.id}>
                      {t.fullName}
                    </option>
                  ))}
                </Select>
                <Select
                  label="Guruh"
                  value={trialGroupId}
                  disabled={!trialTeacherId}
                  onChange={(e) => {
                    setTrialGroupId(e.target.value)
                    setTrialAt('') // guruh o'zgardi — tanlangan kun bekor qilinadi
                  }}
                >
                  <option value="">{trialTeacherId ? '— guruh tanlang —' : "— avval o'qituvchi —"}</option>
                  {trialGroups.map((g) => (
                    <option key={g.id} value={g.id}>
                      {g.name}
                    </option>
                  ))}
                </Select>
              </div>

              {/* Guruh jadvali: dars kunlari + vaqti */}
              {selectedTrialGroup && (
                <div className="rounded-lg bg-violet-50/60 px-3 py-2 text-sm text-slate-600">
                  {trialHasSchedule ? (
                    <>
                      <span className="font-medium text-slate-700">Dars kunlari:</span>{' '}
                      {selectedTrialGroup
                        .days!.slice()
                        .sort((a, b) => a - b)
                        .map((d) => WD_SHORT[d])
                        .join(', ')}
                      {selectedTrialGroup.startTime && (
                        <>
                          {' '}
                          • {selectedTrialGroup.startTime}
                          {selectedTrialGroup.endTime ? `–${selectedTrialGroup.endTime}` : ''}
                        </>
                      )}
                    </>
                  ) : (
                    <span className="text-amber-600">
                      Bu guruhda dars kunlari/vaqti belgilanmagan — sanani qo'lda tanlang.
                    </span>
                  )}
                </div>
              )}

              {/* Keyingi dars uchun kun tanlash (guruh jadvalidan) */}
              {trialHasSchedule ? (
                <div>
                  <label className="mb-1 block text-sm font-medium text-slate-600">
                    Keyingi dars uchun kun
                  </label>
                  <div className="flex flex-wrap gap-2">
                    {trialDates.map((d) => {
                      const at = `${isoDate(d)}T${selectedTrialGroup!.startTime}`
                      const active = trialAt === at
                      return (
                        <button
                          key={at}
                          type="button"
                          onClick={() => setTrialAt(at)}
                          className={`rounded-lg border px-3 py-1.5 text-xs font-medium transition-colors ${
                            active
                              ? 'border-violet-500 bg-violet-600 text-white'
                              : 'border-slate-200 text-slate-600 hover:bg-slate-50'
                          }`}
                        >
                          {WD_SHORT[toMonFirst(d.getDay())]},{' '}
                          {String(d.getDate()).padStart(2, '0')}.
                          {String(d.getMonth() + 1).padStart(2, '0')} • {selectedTrialGroup!.startTime}
                        </button>
                      )
                    })}
                  </div>
                </div>
              ) : (
                <Input
                  label="Sana va vaqt"
                  type="datetime-local"
                  value={trialAt}
                  onChange={(e) => setTrialAt(e.target.value)}
                />
              )}

              <Button type="submit" disabled={savingTrial || !trialGroupId || !trialAt}>
                <CalendarClock className="h-4 w-4" />
                {savingTrial ? 'Saqlanmoqda...' : 'Sinov darsi belgilash'}
              </Button>
            </form>
          </section>

          {/* Tarix (timeline) */}
          <section className="rounded-xl border border-slate-100 p-4">
            <h4 className="mb-3 flex items-center gap-2 font-semibold text-slate-800">
              <History className="h-4 w-4 text-blue-600" /> Tarix
            </h4>

            <div className="mb-4 flex items-end gap-2">
              <div className="flex-1">
                <Input
                  label="Izoh qo'shish"
                  placeholder="Izoh yozing..."
                  value={noteText}
                  onChange={(e) => setNoteText(e.target.value)}
                  onKeyDown={(e) => {
                    if (e.key === 'Enter') {
                      e.preventDefault()
                      handleAddNote()
                    }
                  }}
                />
              </div>
              <Button onClick={handleAddNote} disabled={savingNote || !noteText.trim()}>
                <Send className="h-4 w-4" />
              </Button>
            </div>

            {events.length === 0 ? (
              <p className="text-sm text-slate-400">Hozircha tarix yo'q.</p>
            ) : (
              <ol className="space-y-3">
                {events.map((ev) => (
                  <li key={ev.id} className="flex gap-3">
                    <div className="flex flex-col items-center">
                      <span
                        className={`rounded-full px-2 py-0.5 text-[11px] font-medium ${eventTypeColors[ev.type]}`}
                      >
                        {eventTypeLabels[ev.type]}
                      </span>
                    </div>
                    <div className="min-w-0 flex-1">
                      <p className="text-sm text-slate-700">{ev.text}</p>
                      <p className="text-xs text-slate-400">
                        {ev.actorName ? `${ev.actorName} • ` : ''}
                        <span className="font-mono">{formatDateTime(ev.createdAt)}</span>
                      </p>
                    </div>
                  </li>
                ))}
              </ol>
            )}
          </section>
        </div>
      )}
    </Modal>

    <ReceiptModal
      trialId={receiptTrial}
      autoPrint={receiptAuto}
      onClose={() => {
        setReceiptTrial(null)
        setReceiptAuto(false)
      }}
    />

    <CallPickerModal
      open={callOpen}
      onClose={() => setCallOpen(false)}
      title={lead?.fullName}
      numbers={callNumbers}
    />
    </>
  )
}
