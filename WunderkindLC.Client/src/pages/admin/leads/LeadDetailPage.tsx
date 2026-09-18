import { useEffect, useMemo, useRef, useState, type ReactNode } from 'react'
import { Link, useNavigate, useParams } from 'react-router-dom'
import {
  CalendarClock,
  GraduationCap,
  MessageSquare,
  Phone,
  Trash2,
  ClipboardCheck,
} from 'lucide-react'
import {
  IconArrowLeft,
  IconChevronDown,
  IconClipboardText,
  IconPlus,
  IconSend,
  IconUsers,
} from '@tabler/icons-react'
import type { Lead, LeadEventType, Stage, TrialLesson } from '@/types'
import {
  addLeadEvent,
  deleteLead,
  getLeads,
  setTrialResult,
  updateLead,
  updateLeadStage,
} from '@/api/services/leads'
import { getStages } from '@/api/services/stages'
import { CallPickerModal } from '@/components/CallPickerModal'
import { ReceiptModal } from '@/components/finance/ReceiptModal'
import { DropdownMenu, type DropdownMenuItem } from '@/components/ui/DropdownMenu'
import { Input, Select, Textarea } from '@/components/ui/Input'
import { PhoneInput } from '@/components/ui/PhoneInput'
import { Modal } from '@/components/ui/Modal'
import { Button } from '@/components/ui/Button'
import { Loader } from '@/components/ui/Loader'
import { ReasonPromptModal } from '@/components/ui/ReasonPromptModal'
import { genderOptions } from '@/config/constants'
import { usePerm } from '@/lib/permissions'
import { cn, formatDate, formatDateTime } from '@/lib/utils'
import { LeadCustomAnswer } from './LeadCustomAnswer'
import {
  LeadConvertPanel,
  LeadLevelTestPanel,
  LeadSmsPanel,
  LeadTrialForm,
  LeadTrialList,
} from './LeadPanels'
import { eventTypeColors, eventTypeLabels, leadCallNumbers } from './leadLabels'
import { useLeadActivity, useLeadGroupOptions } from './useLeadDetail'
import { leadErrorText, useLeadEntryForm } from './useLeadEntryForm'

/**
 * LID SAHIFASI (edutizim `/orders/info/:id`): chapda ~385px karta — bosqich tanlovi + "Ko'proq
 * amal" menyusi, "Asosiy" | "Sozlamalar" tablari, "Buyurtma ma'lumotlari" va "O'quvchi
 * ma'lumotlari" (yorliq: qiymat qatorlari) va "Saqlash"; o'ngda — faoliyat lentasi (sana
 * "tabletkalari" bilan) va pastda "Eslatma".
 *
 * ⚠️ Yangi mantiq YO'Q — ilgari `LeadDetailModal` da bo'lgan hamma amal shu yerda:
 * forma holati va «Lid kiritish formasi» qoidalari — `useLeadEntryForm` («Yangi lid» oynasi bilan
 * BITTA hook), SMS/daraja testi/aylantirish/sinov — `LeadPanels`, tarix — `useLeadActivity`.
 */

type ActionModal = 'sms' | 'test' | 'convert' | 'trial' | null

/** "Yorliq: qiymat" qatori (edutizimdagi inline qator). */
function FieldRow({ label, required, children }: { label: string; required?: boolean; children: ReactNode }) {
  return (
    <div className="grid grid-cols-[118px_minmax(0,1fr)] items-center gap-2 py-1">
      <span className="text-[13px] font-semibold leading-tight text-[#333]">
        {label}
        {required && <span className="text-red-500">*</span>}:
      </span>
      <div className="min-w-0">{children}</div>
    </div>
  )
}

/** Tahrirlanmaydigan qiymat — bo'sh bo'lsa edutizimdagidek "...". */
function ReadValue({ children }: { children?: ReactNode }) {
  const empty = children == null || children === '' || children === false
  return <span className={cn('block truncate text-[13px]', empty ? 'text-[#9e9e9e]' : 'text-black')}>{empty ? '...' : children}</span>
}

/** Bo'lim sarlavhasi: och-ko'k ikonka kvadrati + nom. */
function SectionTitle({ icon, children }: { icon: ReactNode; children: ReactNode }) {
  return (
    <div className="mb-1.5 mt-1 flex items-center gap-2">
      <span className="inline-flex h-7 w-7 items-center justify-center rounded-lg border border-brand-600/20 bg-brand-600/10 text-brand-600">
        {icon}
      </span>
      <h3 className="text-[14px] font-bold text-black">{children}</h3>
    </div>
  )
}

/** Tab "tabletka"si (tanlangani — ko'k). */
function PillTab({ active, onClick, children }: { active: boolean; onClick: () => void; children: ReactNode }) {
  return (
    <button
      type="button"
      onClick={onClick}
      className={cn(
        'h-[30px] rounded-lg px-4 text-[12.5px] font-semibold transition-colors',
        active ? 'bg-brand-600 text-white' : 'text-[#333] hover:bg-black/[0.05]',
      )}
    >
      {children}
    </button>
  )
}

export function LeadDetailPage() {
  const { id = '' } = useParams()
  const navigate = useNavigate()
  const { can } = usePerm()
  const canEdit = can('leads.list', 'edit')
  const canDelete = can('leads.list', 'delete')

  const [lead, setLead] = useState<Lead | null>(null)
  // Forma MANBASI alohida: bosqich/aylantirish kabi o'zgarishlarda `lead` yangilanadi, lekin
  // chap paneldagi SAQLANMAGAN tahrirlar tozalanib ketmasligi kerak — forma faqat yuklanganda va
  // saqlangandan keyin qayta to'ldiriladi.
  const [formLead, setFormLead] = useState<Lead | null>(null)
  const [stages, setStages] = useState<Stage[]>([])
  const [loading, setLoading] = useState(true)

  const [leftTab, setLeftTab] = useState<'main' | 'settings'>('main')
  const [rightTab, setRightTab] = useState<'activity' | 'trials'>('activity')
  const [modal, setModal] = useState<ActionModal>(null)
  const [callOpen, setCallOpen] = useState(false)
  const [deleting, setDeleting] = useState(false)
  const [receiptTrial, setReceiptTrial] = useState<string | null>(null)
  const [receiptAuto, setReceiptAuto] = useState(false)
  const [noteText, setNoteText] = useState('')
  const [savingNote, setSavingNote] = useState(false)
  const [savedAt, setSavedAt] = useState<number | null>(null)

  useEffect(() => {
    let cancelled = false
    // Alohida "bitta lid" endpointi YO'Q — ro'yxat so'rovi (25 s keshlangan, ro'yxatdan kelinganda
    // qayta tortilmaydi) ichidan olinadi. Mas'ul ismi, o'qituvchi, daraja ham shu javobda.
    Promise.all([getLeads(), getStages()])
      .then(([all, st]) => {
        if (cancelled) return
        const found = all.find((l) => l.id === id) ?? null
        setLead(found)
        setFormLead(found)
        setStages(st)
      })
      .catch(() => {
        if (!cancelled) setLead(null)
      })
      .finally(() => {
        if (!cancelled) setLoading(false)
      })
    return () => {
      cancelled = true
    }
  }, [id])

  const entry = useLeadEntryForm(!!formLead, formLead)
  const { form, update, setForm, show, req } = entry
  const activity = useLeadActivity(lead?.id ?? null)
  const { groups, teacherOptions } = useLeadGroupOptions(!!lead)

  // Tarix — chat kabi: eskisi tepada, yangisi pastda (pastdagi "Eslatma" maydoni yonida).
  const timeline = useMemo(() => {
    const asc = [...activity.events].sort((a, b) => (a.createdAt || '').localeCompare(b.createdAt || ''))
    const days: { day: string; items: typeof asc }[] = []
    for (const ev of asc) {
      const day = formatDate(ev.createdAt)
      const last = days[days.length - 1]
      if (last && last.day === day) last.items.push(ev)
      else days.push({ day, items: [ev] })
    }
    return days
  }, [activity.events])
  const timelineRef = useRef<HTMLDivElement>(null)
  useEffect(() => {
    const el = timelineRef.current
    if (el) el.scrollTop = el.scrollHeight
  }, [timeline, rightTab])

  const callNumbers = useMemo(() => (lead ? leadCallNumbers(lead) : []), [lead])
  const schoolOptions = entry.districts.find((d) => d.id === form.districtId)?.schools ?? []

  if (loading) return <Loader label="Yuklanmoqda..." />
  if (!lead)
    return (
      <div className="rounded-xl border border-[#dbe0e6] bg-white p-8 text-center">
        <p className="text-sm font-semibold text-[#333]">Lid topilmadi</p>
        <p className="mt-1 text-xs text-[#757575]">U o'chirilgan yoki o'quvchiga aylantirilgan bo'lishi mumkin.</p>
        <Link to="/admin/leads" className="mt-4 inline-block text-sm font-medium text-brand-600 hover:underline">
          Buyurtmalar ro'yxatiga qaytish
        </Link>
      </div>
    )

  /* ---------- Amallar ---------- */
  const stageIds = new Set(stages.map((s) => s.id))
  const currentStage = stageIds.has(lead.stage) ? lead.stage : ''
  const stageIndex = stages.findIndex((s) => s.id === currentStage)

  const changeStage = (stage: string) => {
    if (!stage || stage === lead.stage) return
    const prev = lead.stage
    setLead({ ...lead, stage })
    updateLeadStage(lead.id, stage)
      .then(() => activity.refresh())
      .catch(() => setLead((l) => (l ? { ...l, stage: prev } : l)))
  }

  const save = async () => {
    if (entry.saving) return
    const problem = entry.validate()
    if (problem) {
      entry.setError(problem)
      return
    }
    entry.setError(null)
    entry.setSaving(true)
    try {
      await updateLead(lead.id, form, entry.answers)
      const next = { ...lead, ...form }
      setLead(next)
      setFormLead(next)
      setSavedAt(Date.now())
    } catch (err) {
      // Xato ko'rinadi, kiritilgan qiymatlar joyida qoladi (`lead-entry-form.md` §8).
      entry.setError(leadErrorText(err))
    } finally {
      entry.setSaving(false)
    }
  }

  const addNote = async () => {
    if (!noteText.trim() || savingNote) return
    setSavingNote(true)
    try {
      await addLeadEvent(lead.id, 'note' as LeadEventType, noteText.trim())
      setNoteText('')
      activity.refresh()
    } finally {
      setSavingNote(false)
    }
  }

  const doDelete = (reasonId: string | undefined) => {
    deleteLead(lead.id, reasonId).then(() => navigate('/admin/leads', { replace: true }))
  }

  const handleTrialResult = async (trialId: string, result: TrialLesson['result']) => {
    await setTrialResult(trialId, result)
    activity.refresh()
  }

  const menu: DropdownMenuItem[] = [
    { label: 'Sinov darsiga yozish', icon: CalendarClock, onClick: () => setModal('trial') },
    ...(lead.convertedStudentId
      ? []
      : [{ label: "O'quvchiga aylantirish", icon: GraduationCap, onClick: () => setModal('convert') }]),
    { label: 'SMS yuborish', icon: MessageSquare, onClick: () => setModal('sms') },
    { label: 'Daraja testi yuborish', icon: ClipboardCheck, onClick: () => setModal('test') },
    ...(callNumbers.length > 0 ? [{ label: "Qo'ng'iroq qilish", icon: Phone, onClick: () => setCallOpen(true) }] : []),
    ...(canDelete ? [{ label: "Lidni o'chirish", icon: Trash2, danger: true, onClick: () => setDeleting(true) }] : []),
  ]

  const modalTitle: Record<Exclude<ActionModal, null>, string> = {
    sms: 'SMS yuborish',
    test: 'Daraja testi yuborish',
    convert: "O'quvchiga aylantirish",
    trial: 'Sinov darsiga yozish',
  }

  /* ---------- Ko'rinish ---------- */
  return (
    <div className="flex flex-col gap-2.5 lg:h-[calc(100vh-76px)] lg:flex-row">
      {/* ===== CHAP PANEL ===== */}
      <aside className="flex min-h-0 w-full shrink-0 flex-col rounded-xl border border-[#dbe0e6] bg-white shadow-[0_1px_2px_rgba(0,0,0,0.05)] lg:w-[385px]">
        <div className="border-b border-[#eeeeee] px-3 pb-2 pt-3">
          <div className="flex items-center gap-1">
            <button
              type="button"
              title="Buyurtmalar ro'yxati"
              // Ro'yxatdan kelingan bo'lsa — orqaga (filtrlar manzilda saqlangan); to'g'ridan-to'g'ri
              // havola bilan ochilgan bo'lsa — ro'yxatga (aks holda ilovadan chiqib ketardi).
              onClick={() =>
                ((window.history.state as { idx?: number } | null)?.idx ?? 0) > 0
                  ? navigate(-1)
                  : navigate('/admin/leads')
              }
              className="rounded-lg p-1 text-[#757575] transition-colors hover:bg-black/[0.05] hover:text-black"
            >
              <IconArrowLeft className="h-4 w-4" />
            </button>
            <div className="relative min-w-0 flex-1">
              <select
                value={currentStage}
                disabled={!canEdit}
                onChange={(e) => changeStage(e.target.value)}
                aria-label="Bosqich"
                className="h-9 w-full cursor-pointer appearance-none truncate rounded-lg bg-transparent pl-2 pr-8 text-[15px] font-bold text-black outline-none transition-colors hover:bg-black/[0.03] disabled:cursor-default"
              >
                {!currentStage && <option value="">Bosqichni tanlang</option>}
                {stages.map((s) => (
                  <option key={s.id} value={s.id}>
                    {s.title}
                  </option>
                ))}
              </select>
              <IconChevronDown className="pointer-events-none absolute right-2 top-1/2 h-4 w-4 -translate-y-1/2 text-black" />
            </div>
            <DropdownMenu
              items={menu}
              triggerClassName="h-8 w-8 rounded-lg border-0 text-brand-600 hover:bg-brand-600/10"
            />
          </div>
          {/* Bosqich progressi — voronkada qayerda turgani (edutizimdagi ingichka chiziq). */}
          <div className="mx-2 mt-1 h-1 overflow-hidden rounded-full bg-[#e0e0e0]" title="Bosqich">
            <div
              className="h-full rounded-full bg-brand-600 transition-all"
              style={{ width: stages.length ? `${((stageIndex + 1) / stages.length) * 100}%` : '0%' }}
            />
          </div>
          <div className="mt-2 flex items-center gap-1 rounded-lg bg-[#F0F2F2] p-1">
            <PillTab active={leftTab === 'main'} onClick={() => setLeftTab('main')}>
              Asosiy
            </PillTab>
            <PillTab active={leftTab === 'settings'} onClick={() => setLeftTab('settings')}>
              Sozlamalar
            </PillTab>
          </div>
        </div>

        <div className="min-h-0 flex-1 overflow-y-auto px-3 py-2">
          {leftTab === 'main' ? (
            <fieldset disabled={!canEdit} className="min-w-0">
              {entry.error && (
                <p className="mb-2 rounded-lg border border-red-200 bg-red-50 px-3 py-2 text-[13px] text-red-700">
                  {entry.error}
                </p>
              )}

              <SectionTitle icon={<IconClipboardText className="h-4 w-4" />}>Buyurtma ma'lumotlari</SectionTitle>
              <FieldRow label="Mas'ul shaxs">
                <ReadValue>{lead.assigneeName}</ReadValue>
              </FieldRow>
              {show('interestSubject') && (
                <FieldRow label="Kurs" required={req('interestSubject')}>
                  <Select value={form.interestSubject ?? ''} onChange={(e) => update('interestSubject', e.target.value)}>
                    <option value="">...</option>
                    {form.interestSubject && !entry.courseOptions.includes(form.interestSubject) && (
                      <option value={form.interestSubject}>{form.interestSubject}</option>
                    )}
                    {entry.courseOptions.map((c) => (
                      <option key={c} value={c}>
                        {c}
                      </option>
                    ))}
                  </Select>
                </FieldRow>
              )}
              <FieldRow label="O'qituvchi">
                <ReadValue>{lead.teacherName}</ReadValue>
              </FieldRow>
              <FieldRow label="Guruh">
                <ReadValue>{lead.groupName}</ReadValue>
              </FieldRow>
              <FieldRow label="Kurs darajasi">
                <ReadValue>{lead.level}</ReadValue>
              </FieldRow>
              {lead.firstLessonAt && (
                <FieldRow label="Birinchi dars">
                  <ReadValue>{formatDateTime(lead.firstLessonAt)}</ReadValue>
                </FieldRow>
              )}
              {show('source') && (
                <FieldRow label="Manba" required={req('source')}>
                  <Select value={form.source ?? ''} onChange={(e) => update('source', e.target.value)}>
                    <option value="">...</option>
                    {form.source && !entry.sourceOptions.includes(form.source) && (
                      <option value={form.source}>{form.source}</option>
                    )}
                    {entry.sourceOptions.map((s) => (
                      <option key={s} value={s}>
                        {s}
                      </option>
                    ))}
                  </Select>
                </FieldRow>
              )}
              {/* QO'SHIMCHA SAVOLLAR — «Lid kiritish formasi»da markaz qo'shgani (edutizimdagi "WET:"). */}
              {entry.fields.map((fl) =>
                fl.kind === 'radio' || fl.kind === 'checkbox' || fl.kind === 'textarea' ? (
                  <div key={fl.id} className="py-1">
                    <LeadCustomAnswer
                      field={fl}
                      value={entry.answers[fl.id] ?? []}
                      onSet={(vals) => entry.setAnswer(fl.id, vals)}
                      onToggle={(val) => entry.toggleAnswer(fl.id, val)}
                    />
                  </div>
                ) : (
                  <FieldRow key={fl.id} label={fl.label} required={fl.required}>
                    <LeadCustomAnswer
                      inline
                      field={fl}
                      value={entry.answers[fl.id] ?? []}
                      onSet={(vals) => entry.setAnswer(fl.id, vals)}
                      onToggle={(val) => entry.toggleAnswer(fl.id, val)}
                    />
                  </FieldRow>
                ),
              )}
              {show('note') && (
                <FieldRow label="Izoh" required={req('note')}>
                  <Textarea rows={2} value={form.note ?? ''} onChange={(e) => update('note', e.target.value)} />
                </FieldRow>
              )}
              <FieldRow label="Yaratilgan">
                <ReadValue>{lead.createdAt ? formatDateTime(lead.createdAt) : ''}</ReadValue>
              </FieldRow>
              {/* TAKRORIY MUROJAAT — forma/daraja testi orqali yana yozilgan (bosqichi o'zgarmaydi,
                  shuning uchun bu yerda ochiq yozib qo'yiladi; tafsiloti tarixda). */}
              {!!lead.repeatCount && lead.repeatCount > 0 && (
                <FieldRow label="Takroriy murojaat">
                  <ReadValue>
                    {`${lead.repeatCount} marta` + (lead.lastRepeatAt ? ` · oxirgisi ${formatDate(lead.lastRepeatAt)}` : '')}
                  </ReadValue>
                </FieldRow>
              )}
              {lead.convertedStudentId && (
                <FieldRow label="Holat">
                  <span className="inline-flex flex-wrap items-center gap-1.5 text-[13px]">
                    <span className="rounded bg-emerald-50 px-1.5 py-0.5 font-semibold text-emerald-700">O'quvchi</span>
                    {lead.firstLessonAttendance === 'attended' && <span className="text-emerald-700">✓ Keldi</span>}
                    {lead.firstLessonAttendance === 'absent' && <span className="text-rose-700">✗ Kelmadi</span>}
                    {can('students.list', 'view') && (
                      <Link to={`/admin/students/${lead.convertedStudentId}`} className="text-brand-600 hover:underline">
                        Profil
                      </Link>
                    )}
                  </span>
                </FieldRow>
              )}

              <div className="my-2 border-t border-[#eeeeee]" />

              <SectionTitle icon={<IconUsers className="h-4 w-4" />}>O'quvchi ma'lumotlari</SectionTitle>
              {/* F.I.SH — sozlamada yashirib ham, ixtiyoriy qilib ham bo'lmaydi (`locked`). */}
              <FieldRow label="F.I.SH" required>
                <Input value={form.fullName} onChange={(e) => update('fullName', e.target.value)} />
              </FieldRow>
              {show('gender') && (
                <FieldRow label="Jinsi" required={req('gender')}>
                  <Select value={form.gender} onChange={(e) => update('gender', e.target.value as typeof form.gender)}>
                    {genderOptions.map((g) => (
                      <option key={g.value} value={g.value}>
                        {g.label}
                      </option>
                    ))}
                  </Select>
                </FieldRow>
              )}
              {show('birthDate') && (
                <FieldRow label="Tug'ilgan kun" required={req('birthDate')}>
                  <Input type="date" value={form.birthDate} onChange={(e) => update('birthDate', e.target.value)} />
                </FieldRow>
              )}
              {show('phone') && (
                <FieldRow label="Telefon" required={req('phone')}>
                  <PhoneInput value={form.phone} onChange={(phone) => update('phone', phone)} />
                </FieldRow>
              )}
              {show('fatherFullName') && (
                <FieldRow label="Otasi" required={req('fatherFullName')}>
                  <Input value={form.fatherFullName} onChange={(e) => update('fatherFullName', e.target.value)} />
                </FieldRow>
              )}
              {show('fatherPhone') && (
                <FieldRow label="Otasi raqami" required={req('fatherPhone')}>
                  <PhoneInput value={form.fatherPhone} onChange={(phone) => update('fatherPhone', phone)} />
                </FieldRow>
              )}
              {show('motherFullName') && (
                <FieldRow label="Onasi" required={req('motherFullName')}>
                  <Input value={form.motherFullName} onChange={(e) => update('motherFullName', e.target.value)} />
                </FieldRow>
              )}
              {show('motherPhone') && (
                <FieldRow label="Onasi raqami" required={req('motherPhone')}>
                  <PhoneInput value={form.motherPhone} onChange={(phone) => update('motherPhone', phone)} />
                </FieldRow>
              )}
              {show('districtId') && (
                <FieldRow label="Tuman" required={req('districtId')}>
                  <Select
                    value={form.districtId ?? ''}
                    // Tuman o'zgarsa, oldingi maktab tanlovi tozalanadi.
                    onChange={(e) => setForm((fv) => ({ ...fv, districtId: e.target.value, schoolId: '' }))}
                  >
                    <option value="">...</option>
                    {entry.districts.map((d) => (
                      <option key={d.id} value={d.id}>
                        {d.name}
                      </option>
                    ))}
                  </Select>
                </FieldRow>
              )}
              {show('schoolId') && (
                <FieldRow label="Maktab" required={req('schoolId')}>
                  <Select
                    value={form.schoolId ?? ''}
                    disabled={!form.districtId}
                    onChange={(e) => update('schoolId', e.target.value)}
                  >
                    <option value="">{form.districtId ? '...' : '— avval tuman —'}</option>
                    {schoolOptions.map((s) => (
                      <option key={s.id} value={s.id}>
                        {s.name}
                      </option>
                    ))}
                  </Select>
                </FieldRow>
              )}
              {!canEdit && (
                <p className="mt-2 text-[12px] text-[#757575]">
                  Tahrirlash uchun ruxsat yo'q — ma'lumotlar faqat ko'rish uchun.
                </p>
              )}
            </fieldset>
          ) : (
            <div className="space-y-3 py-1">
              <SectionTitle icon={<IconClipboardText className="h-4 w-4" />}>Buyurtma maydonlari</SectionTitle>
              {entry.fields.length === 0 ? (
                <p className="text-[13px] text-[#757575]">Qo'shimcha maydon qo'shilmagan.</p>
              ) : (
                <ul className="space-y-1">
                  {entry.fields.map((fl) => (
                    <li
                      key={fl.id}
                      className="flex items-center justify-between rounded-lg border border-[#eeeeee] px-3 py-2 text-[13px]"
                    >
                      <span className="truncate font-medium text-black">{fl.label}</span>
                      {fl.required && <span className="text-[11px] font-semibold text-red-500">majburiy</span>}
                    </li>
                  ))}
                </ul>
              )}
              <p className="text-[12px] text-[#757575]">
                Qaysi maydon so'ralishi va majburiyligi butun markaz uchun bitta — «Lid kiritish formasi»da
                sozlanadi.
              </p>
              {can('leads.forms', 'view') && (
                <Link to="/admin/forms/lid-kiritish">
                  <Button variant="secondary">
                    <IconPlus className="h-4 w-4" /> Maydon qo'shish
                  </Button>
                </Link>
              )}
            </div>
          )}
        </div>

        {leftTab === 'main' && canEdit && (
          <div className="flex items-center gap-2 border-t border-[#eeeeee] px-3 py-2">
            <Button onClick={save} disabled={entry.saving}>
              {entry.saving ? 'Saqlanmoqda…' : 'Saqlash'}
            </Button>
            {savedAt && !entry.saving && !entry.error && <span className="text-[12px] text-emerald-700">Saqlandi ✓</span>}
          </div>
        )}
      </aside>

      {/* ===== O'NG PANEL ===== */}
      <section className="flex min-h-[420px] min-w-0 flex-1 flex-col rounded-xl border border-[#dbe0e6] bg-white shadow-[0_1px_2px_rgba(0,0,0,0.05)]">
        <div className="flex items-center gap-1 border-b border-[#eeeeee] p-2">
          <PillTab active={rightTab === 'activity'} onClick={() => setRightTab('activity')}>
            Umumiy
          </PillTab>
          <PillTab active={rightTab === 'trials'} onClick={() => setRightTab('trials')}>
            Sinov darslari{activity.trials.length > 0 ? ` (${activity.trials.length})` : ''}
          </PillTab>
          <span className="ml-auto truncate pr-2 text-[13px] font-semibold text-black" title={lead.fullName}>
            {lead.fullName}
          </span>
        </div>

        {rightTab === 'activity' ? (
          <>
            <div ref={timelineRef} className="min-h-0 flex-1 overflow-y-auto px-4 py-3">
              {timeline.length === 0 ? (
                <p className="py-10 text-center text-[13px] text-[#9e9e9e]">Hozircha tarix yo'q.</p>
              ) : (
                timeline.map((g) => (
                  <div key={g.day} className="mb-3">
                    <div className="my-3 flex items-center gap-3">
                      <span className="h-px flex-1 bg-[#e0e0e0]" />
                      <span className="rounded-full border border-[#dbe0e6] bg-white px-4 py-0.5 text-[12px] text-[#757575]">
                        {g.day}
                      </span>
                      <span className="h-px flex-1 bg-[#e0e0e0]" />
                    </div>
                    <ol className="space-y-1.5">
                      {g.items.map((ev) => (
                        <li key={ev.id} className="flex items-start gap-2 text-[12.5px] leading-snug">
                          <span
                            className={cn(
                              'mt-px shrink-0 rounded px-1.5 py-px text-[10.5px] font-semibold',
                              eventTypeColors[ev.type],
                            )}
                          >
                            {eventTypeLabels[ev.type]}
                          </span>
                          <p className={cn('min-w-0', ev.type === 'note' ? 'text-black' : 'text-[#555]')}>
                            <span className="text-[#757575]">
                              {formatDateTime(ev.createdAt)}
                              {ev.actorName ? ` - ${ev.actorName}` : ''}:
                            </span>{' '}
                            {ev.text}
                          </p>
                        </li>
                      ))}
                    </ol>
                  </div>
                ))
              )}
            </div>
            {/* "Eslatma" — lid tarixiga izoh (edutizimdagi pastki kulrang panel). */}
            <div className="flex items-center gap-2 rounded-b-xl border-t border-[#eeeeee] bg-[#F5F6F6] px-3 py-2">
              <span className="shrink-0 text-[13px] font-medium text-brand-600 underline">Eslatma:</span>
              <input
                value={noteText}
                onChange={(e) => setNoteText(e.target.value)}
                onKeyDown={(e) => {
                  if (e.key === 'Enter') {
                    e.preventDefault()
                    void addNote()
                  }
                }}
                placeholder="Izoh yozing..."
                className="h-8 min-w-0 flex-1 rounded-lg border border-[#dbe0e6] bg-white px-3 text-[13px] outline-none focus:border-brand-600"
              />
              <Button onClick={addNote} disabled={savingNote || !noteText.trim()} title="Qo'shish">
                <IconSend className="h-4 w-4" />
              </Button>
            </div>
          </>
        ) : (
          <div className="min-h-0 flex-1 space-y-4 overflow-y-auto p-4">
            <LeadTrialList
              trials={activity.trials}
              onResult={handleTrialResult}
              onReceipt={(tid) => {
                setReceiptAuto(false)
                setReceiptTrial(tid)
              }}
            />
            <Button variant="secondary" onClick={() => setModal('trial')}>
              <CalendarClock className="h-4 w-4" /> Sinov darsiga yozish
            </Button>
          </div>
        )}
      </section>

      {/* ===== Amal oynalari ===== */}
      <Modal open={!!modal} onClose={() => setModal(null)} title={modal ? modalTitle[modal] : ''} size="lg">
        {modal === 'sms' && <LeadSmsPanel lead={lead} onSent={activity.refresh} />}
        {modal === 'test' && <LeadLevelTestPanel lead={lead} onSent={activity.refresh} />}
        {modal === 'convert' && (
          <LeadConvertPanel
            lead={lead}
            groups={groups}
            teacherOptions={teacherOptions}
            onConverted={(studentId) => {
              setLead({ ...lead, convertedStudentId: studentId })
              activity.refresh()
            }}
          />
        )}
        {modal === 'trial' && (
          <LeadTrialForm
            leadId={lead.id}
            groups={groups}
            teacherOptions={teacherOptions}
            onScheduled={(tid) => {
              setModal(null)
              setRightTab('trials')
              activity.refresh()
              // Sinov darsiga yozildi — chekni avtomatik ochib, print dialogini chiqaramiz.
              if (tid) {
                setReceiptAuto(true)
                setReceiptTrial(tid)
              }
            }}
          />
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
      <CallPickerModal open={callOpen} onClose={() => setCallOpen(false)} title={lead.fullName} numbers={callNumbers} />
      <ReasonPromptModal
        open={deleting}
        category="lead_delete"
        title="Lidni o'chirish"
        message={`"${lead.fullName}" lidini o'chirasizmi?`}
        confirmLabel="O'chirish"
        tone="red"
        onConfirm={doDelete}
        onClose={() => setDeleting(false)}
      />
    </div>
  )
}
