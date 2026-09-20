import { useEffect, useMemo, useState } from 'react'
import { CalendarClock, CheckCircle2, GraduationCap, Receipt, Send } from 'lucide-react'
import type { Group, Lead, LevelTestListItem, Teacher, TrialLesson } from '@/types'
import { getPickableTemplates, sendLeadSms, type PickableTemplate } from '@/api/services/messages'
import { getMessageTokens } from '@/api/services/autoMessages'
import { getLevelTests, sendLeadTest } from '@/api/services/levelTests'
import { convertLead, scheduleTrial } from '@/api/services/leads'
import { MessageEditor, type TokenDef } from '@/components/messaging/MessageEditor'
import { Button } from '@/components/ui/Button'
import { Input, Select } from '@/components/ui/Input'
import { apiErrorMessage, formatDate, formatDateTime } from '@/lib/utils'
import {
  WD_SHORT,
  isoDate,
  nextLessonDates,
  toMonFirst,
  trialResultColors,
  trialResultLabels,
} from './leadLabels'

/*
 * LID AMALLARI — ilgari hammasi bitta `LeadDetailModal` ichida edi. Endi lid SAHIFASI
 * (`LeadDetailPage`, edutizimdagi `/orders/info/:id`) ularni "Ko'proq amal" menyusi va o'ng
 * paneldagi tablar orqali ochadi. Mantiq O'ZGARMAGAN — faqat alohida komponentlarga ajratildi.
 */

function errMessage(err: unknown, fallback: string): string {
  return (err as { response?: { data?: { message?: string } } })?.response?.data?.message ?? fallback
}

const leadPhoneOf = (lead: Lead) => lead.phone || lead.fatherPhone || lead.motherPhone || ''

/* ------------------------------------------------------------------ SMS */

export function LeadSmsPanel({ lead, onSent }: { lead: Lead; onSent: () => void }) {
  const [templates, setTemplates] = useState<PickableTemplate[]>([])
  const [tokens, setTokens] = useState<TokenDef[]>([])
  const [text, setText] = useState('')
  const [sending, setSending] = useState(false)
  const [result, setResult] = useState<string | null>(null)
  const phone = leadPhoneOf(lead)

  useEffect(() => {
    getPickableTemplates('lead').then(setTemplates).catch(() => setTemplates([]))
    // Lid uchun faqat lid + umumiy guruh tokenlari mos keladi
    getMessageTokens()
      .then((ts) => setTokens(ts.filter((t) => t.group === 'lead' || t.group === 'common')))
      .catch(() => setTokens([]))
  }, [])

  const send = async () => {
    if (!text.trim() || sending) return
    setSending(true)
    setResult(null)
    try {
      // Kanal (Eskiz/Local) tanlanmaydi — server Sozlamalardagi qiymatdan o'zi oladi.
      const b = await sendLeadSms(lead.id, text.trim())
      setResult(b.sentCount > 0 ? 'SMS yuborildi ✓' : 'Yuborildi (holat kutilmoqda)')
      setText('')
      onSent()
    } catch (err: unknown) {
      setResult(errMessage(err, 'Yuborishda xatolik'))
    } finally {
      setSending(false)
    }
  }

  if (!phone) return <p className="text-sm text-slate-400">Lidda telefon raqami yo'q.</p>
  return (
    <div className="space-y-3">
      <p className="text-xs text-slate-400">
        Raqam: <span className="font-mono text-slate-600">{phone}</span>
      </p>
      <MessageEditor
        value={text}
        onChange={setText}
        tokens={tokens}
        templates={templates.map((t) => ({ name: t.name, text: t.text }))}
        showSmsCounter
        rows={3}
        placeholder="SMS matni (shablon tanlang yoki yozing)..."
      />
      <div className="flex items-center justify-between gap-3">
        {result && (
          <p className={`text-sm font-medium ${result.includes('✓') ? 'text-emerald-700' : 'text-amber-700'}`}>
            {result}
          </p>
        )}
        <Button className="ml-auto" onClick={send} disabled={!text.trim() || sending}>
          <Send className="h-4 w-4" /> {sending ? 'Yuborilmoqda...' : 'SMS yuborish'}
        </Button>
      </div>
    </div>
  )
}

/* ------------------------------------------------------------------ Daraja testi */

export function LeadLevelTestPanel({ lead, onSent }: { lead: Lead; onSent: () => void }) {
  const [tests, setTests] = useState<LevelTestListItem[]>([])
  const [testId, setTestId] = useState('')
  const [sending, setSending] = useState(false)
  const [result, setResult] = useState<string | null>(null)
  const phone = leadPhoneOf(lead)

  useEffect(() => {
    getLevelTests().then((ts) => setTests(ts.filter((t) => t.isActive))).catch(() => setTests([]))
  }, [])

  const send = async () => {
    if (!testId || sending) return
    setSending(true)
    setResult(null)
    try {
      const r = await sendLeadTest(lead.id, testId)
      setResult(r.ok ? 'Test havolasi SMS yuborildi ✓' : `Yuborilmadi: ${r.status}`)
      onSent()
    } catch (err: unknown) {
      setResult(errMessage(err, 'Yuborishda xatolik'))
    } finally {
      setSending(false)
    }
  }

  if (!phone) return <p className="text-sm text-slate-400">Lidda telefon raqami yo'q.</p>
  if (tests.length === 0) return <p className="text-sm text-slate-400">Faol daraja testi yo'q.</p>
  return (
    <div className="space-y-3">
      <p className="text-xs text-slate-400">
        Tanlangan test uchun <b>bir martalik havola</b> SMS qilib yuboriladi. Lid ma'lumotini qayta
        kiritmaydi; natija shu lidga bog'lanadi. (SMS andoza: "daraja testi havolasi" — {'{link}'} tokeni)
      </p>
      <div className="flex items-center gap-2">
        <Select value={testId} onChange={(e) => setTestId(e.target.value)} className="flex-1">
          <option value="">— Testni tanlang —</option>
          {tests.map((t) => (
            <option key={t.id} value={t.id}>
              {t.title}
            </option>
          ))}
        </Select>
        <Button onClick={send} disabled={!testId || sending}>
          <Send className="h-4 w-4" /> {sending ? 'Yuborilmoqda...' : 'Yuborish'}
        </Button>
      </div>
      {result && (
        <p className={`text-sm font-medium ${result.includes('✓') ? 'text-emerald-700' : 'text-amber-700'}`}>
          {result}
        </p>
      )}
    </div>
  )
}

/* ------------------------------------------------------------------ O'quvchiga aylantirish */

export function LeadConvertPanel({
  lead,
  groups,
  teacherOptions,
  onConverted,
}: {
  lead: Lead
  groups: Group[]
  teacherOptions: Teacher[]
  onConverted: (studentId: string) => void
}) {
  const [teacherId, setTeacherId] = useState('')
  const [groupId, setGroupId] = useState('')
  const [date, setDate] = useState('')
  const [converting, setConverting] = useState(false)
  const teacherGroups = useMemo(() => groups.filter((g) => g.teacherId === teacherId), [groups, teacherId])

  const convert = async () => {
    setConverting(true)
    try {
      const { studentId } = await convertLead(lead.id, {
        enrollmentDate: date || undefined,
        groupId: groupId || undefined,
      })
      onConverted(studentId)
    } catch (err: unknown) {
      alert(errMessage(err, 'Aylantirishda xatolik yuz berdi'))
    } finally {
      setConverting(false)
    }
  }

  if (lead.convertedStudentId)
    return (
      <div className="inline-flex items-center gap-2 rounded-lg bg-emerald-50 px-3 py-2 text-sm font-medium text-emerald-700">
        <CheckCircle2 className="h-4 w-4" /> Aylantirilgan
      </div>
    )
  return (
    <div className="space-y-3">
      <div className="grid grid-cols-1 gap-3 sm:grid-cols-3">
        <Select
          label="O'qituvchi (ixtiyoriy)"
          value={teacherId}
          onChange={(e) => {
            setTeacherId(e.target.value)
            setGroupId('')
          }}
        >
          <option value="">— biriktirmaslik —</option>
          {teacherOptions.map((t) => (
            <option key={t.id} value={t.id}>
              {t.fullName}
            </option>
          ))}
        </Select>
        <Select label="Guruh" value={groupId} disabled={!teacherId} onChange={(e) => setGroupId(e.target.value)}>
          <option value="">{teacherId ? '— guruh —' : "— avval o'qituvchi —"}</option>
          {teacherGroups.map((g) => (
            <option key={g.id} value={g.id}>
              {g.name}
            </option>
          ))}
        </Select>
        <Input
          label="Qabul sanasi (ixtiyoriy)"
          type="date"
          value={date}
          onChange={(e) => setDate(e.target.value)}
        />
      </div>
      <Button onClick={convert} disabled={converting}>
        <GraduationCap className="h-4 w-4" />
        {converting ? 'Aylantirilmoqda...' : "O'quvchiga aylantirish"}
      </Button>
    </div>
  )
}

/* ------------------------------------------------------------------ Sinov darsi: belgilash */

export function LeadTrialForm({
  leadId,
  groups,
  teacherOptions,
  onScheduled,
}: {
  leadId: string
  groups: Group[]
  teacherOptions: Teacher[]
  /** Saqlangandan keyin — `trialId` bilan (chek avtomatik ochilishi uchun). */
  onScheduled: (trialId: string | null) => void
}) {
  const [teacherId, setTeacherId] = useState('')
  const [groupId, setGroupId] = useState('')
  const [at, setAt] = useState('')
  const [saving, setSaving] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const teacherGroups = useMemo(() => groups.filter((g) => g.teacherId === teacherId), [groups, teacherId])

  // Tanlangan guruh + uning dars kunlari/vaqti + keyingi dars sanalari.
  const group = groups.find((g) => g.id === groupId) || null
  const hasSchedule = !!(group?.days?.length && group?.startTime)
  const dates = hasSchedule ? nextLessonDates(group!.days!, 8) : []

  const submit = async (e: React.FormEvent) => {
    e.preventDefault()
    if (!groupId || !at) return
    setSaving(true)
    setError(null)
    try {
      const tid = await scheduleTrial(leadId, groupId, at)
      setGroupId('')
      setAt('')
      onScheduled(tid)
    } catch (err) {
      // ⚠️ Ilgari `catch` YO'Q edi: so'rov yiqilsa (403, 400, tarmoq) oyna JIMGINA ochiq qolar,
      // foydalanuvchi esa "sinov darsiga yozib bo'lmayapti" deb ko'rardi — sababi faqat
      // DevTools → Network da ko'rinardi.
      setError(apiErrorMessage(err, "Sinov darsini belgilab bo'lmadi"))
    } finally {
      setSaving(false)
    }
  }

  return (
    <form onSubmit={submit} className="space-y-3">
      <div className="grid grid-cols-1 gap-3 sm:grid-cols-2">
        <Select
          label="O'qituvchi"
          value={teacherId}
          onChange={(e) => {
            // O'qituvchi o'zgardi — guruh va tanlangan kun bekor qilinadi.
            setTeacherId(e.target.value)
            setGroupId('')
            setAt('')
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
          value={groupId}
          disabled={!teacherId}
          onChange={(e) => {
            setGroupId(e.target.value)
            setAt('') // guruh o'zgardi — tanlangan kun bekor qilinadi
          }}
        >
          <option value="">{teacherId ? '— guruh tanlang —' : "— avval o'qituvchi —"}</option>
          {teacherGroups.map((g) => (
            <option key={g.id} value={g.id}>
              {g.name}
            </option>
          ))}
        </Select>
      </div>

      {/* Guruh jadvali: dars kunlari + vaqti */}
      {group && (
        <div className="rounded-lg bg-violet-50/60 px-3 py-2 text-sm text-slate-600">
          {hasSchedule ? (
            <>
              <span className="font-medium text-slate-700">Dars kunlari:</span>{' '}
              {group
                .days!.slice()
                .sort((a, b) => a - b)
                .map((d) => WD_SHORT[d])
                .join(', ')}
              {group.startTime && (
                <>
                  {' '}
                  • {group.startTime}
                  {group.endTime ? `–${group.endTime}` : ''}
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
      {hasSchedule ? (
        <div>
          <label className="mb-1 block text-sm font-medium text-slate-600">Keyingi dars uchun kun</label>
          <div className="flex flex-wrap gap-2">
            {dates.map((d) => {
              const value = `${isoDate(d)}T${group!.startTime}`
              const active = at === value
              return (
                <button
                  key={value}
                  type="button"
                  onClick={() => setAt(value)}
                  className={`rounded-lg border px-3 py-1.5 text-xs font-medium transition-colors ${
                    active
                      ? 'border-violet-500 bg-violet-600 text-white'
                      : 'border-slate-200 text-slate-600 hover:bg-slate-50'
                  }`}
                >
                  {WD_SHORT[toMonFirst(d.getDay())]}, {String(d.getDate()).padStart(2, '0')}.
                  {String(d.getMonth() + 1).padStart(2, '0')} • {group!.startTime}
                </button>
              )
            })}
          </div>
        </div>
      ) : (
        <Input label="Sana va vaqt" type="datetime-local" value={at} onChange={(e) => setAt(e.target.value)} />
      )}

      {error && (
        <p className="rounded-lg border border-red-200 bg-red-50 px-3 py-2 text-[13px] text-red-700">
          {error}
        </p>
      )}

      <Button type="submit" disabled={saving || !groupId || !at}>
        <CalendarClock className="h-4 w-4" />
        {saving ? 'Saqlanmoqda...' : 'Sinov darsi belgilash'}
      </Button>
    </form>
  )
}

/* ------------------------------------------------------------------ Sinov darsi: ro'yxat */

export function LeadTrialList({
  trials,
  onResult,
  onReceipt,
}: {
  trials: TrialLesson[]
  onResult: (trialId: string, result: TrialLesson['result']) => void
  onReceipt: (trialId: string) => void
}) {
  if (trials.length === 0) return <p className="text-sm text-slate-400">Sinov darslari belgilanmagan.</p>
  return (
    <ul className="space-y-2">
      {trials.map((t) => (
        <li
          key={t.id}
          className="flex flex-wrap items-center justify-between gap-2 rounded-lg border border-slate-100 px-3 py-2"
        >
          <div className="min-w-0">
            <p className="text-sm font-medium text-slate-800">{t.groupName}</p>
            <p className="font-mono text-xs text-slate-400">{formatDateTime(t.scheduledAt)}</p>
            {t.attendedAt && <p className="text-xs text-sky-600">Sinovga keldi: {formatDate(t.attendedAt)}</p>}
          </div>
          <div className="flex flex-wrap items-center gap-2">
            <span className={`rounded-full px-2.5 py-0.5 text-xs font-medium ${trialResultColors[t.result]}`}>
              {trialResultLabels[t.result]}
            </span>
            <button
              type="button"
              title="Chek (sinov darsi varaqasi)"
              onClick={() => onReceipt(t.id)}
              className="rounded-lg p-1.5 text-slate-400 transition-colors hover:bg-violet-50 hover:text-violet-600"
            >
              <Receipt className="h-4 w-4" />
            </button>
            {/* 1-QADAM — TASHRIF: "Keldi/Kelmadi" faqat natija hali belgilanmaganda.
                Kelgan sana shu tugmadan yoziladi va KPI konversiyasi shundan hisoblanadi. */}
            {t.result === 'pending' && (
              <>
                <Button variant="secondary" className="px-2.5 py-1 text-xs" onClick={() => onResult(t.id, 'came')}>
                  Keldi
                </Button>
                <Button variant="secondary" className="px-2.5 py-1 text-xs" onClick={() => onResult(t.id, 'no_show')}>
                  Kelmadi
                </Button>
              </>
            )}
            {/* 2-QADAM — NATIJA: "Qoldi/Ketdi" avvalgidek ishlaydi. Sinovga kelgani
                belgilangandan keyin ham ko'rinadi (tashrifdan keyingi qaror). */}
            {(t.result === 'pending' || t.result === 'came') && (
              <>
                <Button variant="secondary" className="px-2.5 py-1 text-xs" onClick={() => onResult(t.id, 'stayed')}>
                  Qoldi
                </Button>
                <Button variant="secondary" className="px-2.5 py-1 text-xs" onClick={() => onResult(t.id, 'left')}>
                  Ketdi
                </Button>
              </>
            )}
          </div>
        </li>
      ))}
    </ul>
  )
}
