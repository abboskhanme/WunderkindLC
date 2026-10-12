import { useEffect, useMemo, useState } from 'react'
import { Bell, Send } from 'lucide-react'
import type { Group, Subject, Teacher } from '@/types'
import { getClasses } from '@/api/services/classes'
import { getSubjects } from '@/api/services/subjects'
import { getTeachers } from '@/api/services/teachers'
import {
  createCampaign,
  getDebtors,
  getMessagingTemplates,
  type AudienceSpec,
  type Debtor,
  type MessagingTemplate,
} from '@/api/services/messaging'
import { Card } from '@/components/ui/Card'
import { Button } from '@/components/ui/Button'
import { Loader } from '@/components/ui/Loader'
import { Select } from '@/components/ui/Input'
import { MoneyInput } from '@/components/ui/NumberInput'
import { RightDrawer } from '@/components/ui/RightDrawer'
import { MultiPick } from '@/components/messaging/MultiPick'
import { RelationChips } from '@/components/messaging/RelationChips'
import { TelegramLinkCell } from '@/components/messaging/TelegramLinkCell'
import { TelegramPreview } from '@/components/messaging/TelegramPreview'
import { useAudienceCount, useMessagePreview } from '@/components/messaging/useTelegramDraft'
import { DELIVERY_META, deliveryView } from '@/lib/telegramDelivery'
import { PARENT_RELATIONS, countLine, type Relation } from '@/lib/relations'
import { apiErrorMessage, cn, formatDateTime, formatMoney } from '@/lib/utils'

/**
 * «Qarzdorlar» (CRM upgrade P2.8): avtomatik eslatma bilan BIR XIL ro'yxat (arxivsiz, balansi manfiy,
 * guruhi yopilmagan), qarz HAR KURS bo'yicha (`DebtMessages`), Telegram holati va oxirgi eslatma.
 * Belgilanganlarga «Eslatma yuborish» — yon panel: tizim matni yoki shablon, «Qabul qildim», preview,
 * sanoq → `kind=debt` kampaniya (har kurs uchun alohida xabar).
 */
export function DebtorsTab({ canSend, onSent }: { canSend: boolean; onSent: (campaignId: string) => void }) {
  const [courseId, setCourseId] = useState<string[]>([])
  const [groupId, setGroupId] = useState<string[]>([])
  const [teacherId, setTeacherId] = useState<string[]>([])
  const [minDebt, setMinDebt] = useState<number | null>(null)
  const [memberStatus, setMemberStatus] = useState<'' | 'active' | 'frozen'>('')
  const [rows, setRows] = useState<Debtor[] | null>(null)
  const [error, setError] = useState('')
  const [selected, setSelected] = useState<Set<string>>(new Set())
  const [panelOpen, setPanelOpen] = useState(false)
  const [tick, setTick] = useState(0)

  const [groups, setGroups] = useState<Group[]>([])
  const [courses, setCourses] = useState<Subject[]>([])
  const [teachers, setTeachers] = useState<Teacher[]>([])

  useEffect(() => {
    getClasses().then((g) => setGroups(g.filter((x) => !x.isArchived))).catch(() => setGroups([]))
    getSubjects().then(setCourses).catch(() => setCourses([]))
    getTeachers().then((t) => setTeachers(t.filter((x) => !x.isArchived))).catch(() => setTeachers([]))
  }, [])

  const filterKey = JSON.stringify({ courseId, groupId, teacherId, minDebt, memberStatus, tick })
  useEffect(() => {
    const f = JSON.parse(filterKey) as { courseId: string[]; groupId: string[]; teacherId: string[]; minDebt: number | null; memberStatus: '' | 'active' | 'frozen' }
    let alive = true
    const t = window.setTimeout(() => {
      getDebtors({
        courseId: f.courseId[0],
        groupId: f.groupId[0],
        teacherId: f.teacherId[0],
        minDebt: f.minDebt ?? undefined,
        memberStatus: f.memberStatus,
      })
        .then((d) => {
          if (!alive) return
          setRows(d)
          setError('')
        })
        // Sahifaning ASOSIY ma'lumoti — sabab ko'rsatiladi.
        .catch((e) => alive && setError(apiErrorMessage(e, "Qarzdorlarni yuklab bo'lmadi")))
    }, 300)
    return () => {
      alive = false
      window.clearTimeout(t)
    }
  }, [filterKey])

  const list = useMemo(() => rows ?? [], [rows])
  const allChecked = list.length > 0 && list.every((r) => selected.has(r.studentId))
  const total = list.reduce((s, r) => s + r.totalDebt, 0)

  const toggle = (id: string) =>
    setSelected((prev) => {
      const next = new Set(prev)
      if (next.has(id)) next.delete(id)
      else next.add(id)
      return next
    })

  return (
    <div className="space-y-4">
      <Card>
        <div className="grid grid-cols-1 gap-3 sm:grid-cols-5">
          <MultiPick label="Kurs" single options={courses.map((c) => ({ id: c.id, label: c.name }))} value={courseId} onChange={setCourseId} />
          <MultiPick label="Guruh" single options={groups.map((g) => ({ id: g.id, label: g.name }))} value={groupId} onChange={setGroupId} />
          <MultiPick label="O'qituvchi" single options={teachers.map((t) => ({ id: t.id, label: t.fullName }))} value={teacherId} onChange={setTeacherId} />
          <MoneyInput label="Qarz kamida" placeholder="0" value={minDebt} onChange={setMinDebt} />
          <Select label="Holat" value={memberStatus} onChange={(e) => setMemberStatus(e.target.value as '' | 'active' | 'frozen')}>
            <option value="">Hammasi</option>
            <option value="active">Aktiv</option>
            <option value="frozen">Muzlatilgan</option>
          </Select>
        </div>
      </Card>

      {error && <p className="text-sm font-medium text-red-600">{error}</p>}

      <Card tight>
        <div className="flex flex-wrap items-center justify-between gap-3 border-b border-slate-100 px-4 py-3">
          <p className="text-sm text-slate-600">
            {list.length} ta qarzdor · jami <b className="text-red-600">{formatMoney(total)}</b>
          </p>
          {canSend && (
            <Button onClick={() => setPanelOpen(true)} disabled={selected.size === 0}>
              <Bell className="h-4 w-4" /> Eslatma yuborish{selected.size > 0 ? ` (${selected.size})` : ''}
            </Button>
          )}
        </div>
        {rows === null && !error ? (
          <Loader label="Yuklanmoqda..." />
        ) : (
          <div className="overflow-x-auto">
            <table className="w-full text-sm">
              <thead className="bg-slate-50 text-left text-xs text-slate-500">
                <tr>
                  <th className="w-10 px-3 py-2.5">
                    <input
                      type="checkbox"
                      checked={allChecked}
                      onChange={(e) => setSelected(e.target.checked ? new Set(list.map((r) => r.studentId)) : new Set())}
                      className="h-4 w-4 accent-brand-600"
                    />
                  </th>
                  <th className="px-3 py-2.5">F.I.Sh</th>
                  <th className="px-3 py-2.5">Kurslar bo'yicha qarz</th>
                  <th className="px-3 py-2.5 text-right">Jami qarz</th>
                  <th className="px-3 py-2.5">Telegram</th>
                  <th className="px-3 py-2.5">Oxirgi eslatma</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-slate-100">
                {list.length === 0 && (
                  <tr>
                    <td colSpan={6} className="px-4 py-10 text-center text-slate-400">
                      Qarzdor topilmadi
                    </td>
                  </tr>
                )}
                {list.map((r) => {
                  const last = r.lastReminder
                  const lv = last ? DELIVERY_META[deliveryView(last.status, last.ackAt)] : null
                  return (
                    <tr key={r.studentId} className={cn(selected.has(r.studentId) && 'bg-brand-50/40')}>
                      <td className="px-3 py-2.5">
                        <input
                          type="checkbox"
                          checked={selected.has(r.studentId)}
                          onChange={() => toggle(r.studentId)}
                          className="h-4 w-4 accent-brand-600"
                        />
                      </td>
                      <td className="px-3 py-2.5 font-medium text-slate-700">{r.fullName}</td>
                      <td className="px-3 py-2.5">
                        <div className="flex flex-wrap gap-1">
                          {r.items.map((it, i) => (
                            <span key={`${it.groupId}-${i}`} className="rounded-md bg-red-50 px-1.5 py-0.5 text-xs text-red-700">
                              {(it.courseName || it.groupName || 'Umumiy')} — {formatMoney(it.debt)}
                            </span>
                          ))}
                        </div>
                      </td>
                      <td className="px-3 py-2.5 text-right font-mono font-semibold text-red-600">{formatMoney(r.totalDebt)}</td>
                      <td className="px-3 py-2.5">
                        <TelegramLinkCell linked={r.telegram.chats} relations={r.telegram.relations} />
                      </td>
                      <td className="px-3 py-2.5 text-xs">
                        {last && lv ? (
                          <span className="inline-flex items-center gap-1.5">
                            <span className="text-slate-500">{formatDateTime(last.at)}</span>
                            <span className={cn('rounded-md px-1.5 py-0.5 font-medium', lv.cls)}>
                              {lv.icon} {lv.label}
                            </span>
                          </span>
                        ) : (
                          <span className="text-slate-400">—</span>
                        )}
                      </td>
                    </tr>
                  )
                })}
              </tbody>
            </table>
          </div>
        )}
      </Card>

      {panelOpen && (
        <DebtReminderPanel
          studentIds={[...selected]}
          onClose={() => setPanelOpen(false)}
          onSent={(id) => {
            setPanelOpen(false)
            setSelected(new Set())
            setTick((t) => t + 1)
            onSent(id)
          }}
        />
      )}
    </div>
  )
}

/** Yon panel: tizim matni yoki shablon, «Qabul qildim», preview, sanoq → `kind=debt`. */
function DebtReminderPanel({
  studentIds,
  onClose,
  onSent,
}: {
  studentIds: string[]
  onClose: () => void
  onSent: (campaignId: string) => void
}) {
  const [templates, setTemplates] = useState<MessagingTemplate[]>([])
  const [templateId, setTemplateId] = useState('')
  const [ack, setAck] = useState(true)
  const [relations, setRelations] = useState<Relation[]>([...PARENT_RELATIONS])
  const [sending, setSending] = useState(false)
  const [error, setError] = useState('')

  useEffect(() => {
    getMessagingTemplates().then(setTemplates).catch(() => setTemplates([]))
  }, [])

  const template = templates.find((t) => t.id === templateId)?.text ?? ''
  const audience: AudienceSpec = useMemo(() => ({ kind: 'selected', studentIds, relations }), [studentIds, relations])
  const { preview, loading } = useMessagePreview({ template, audience, studentId: studentIds[0], kind: 'debt' })
  const { count, error: countError, loading: countLoading } = useAudienceCount({ audience, template, kind: 'debt' })
  const errors = preview?.errors ?? []

  const send = async () => {
    setSending(true)
    setError('')
    try {
      const res = await createCampaign({ kind: 'debt', audience, template, attachmentId: null, ackRequired: ack })
      onSent(res.id)
    } catch (e) {
      setError(apiErrorMessage(e, "Eslatmani yuborib bo'lmadi"))
    } finally {
      setSending(false)
    }
  }

  return (
    <RightDrawer
      open
      onClose={onClose}
      title="Qarz eslatmasi"
      width={520}
      footer={
        <div className="flex w-full items-center justify-end gap-2">
          <Button variant="secondary" onClick={onClose} disabled={sending}>
            Bekor qilish
          </Button>
          <Button onClick={send} disabled={sending || errors.length > 0 || (count?.messages ?? 0) === 0 || countLoading}>
            <Send className="h-4 w-4" /> {sending ? 'Yuborilmoqda...' : 'Yuborish'}
          </Button>
        </div>
      }
    >
      <div className="space-y-4">
        <p className="text-sm text-slate-600">
          {studentIds.length} ta qarzdor · har kurs bo'yicha alohida xabar
        </p>
        <Select label="Matn" value={templateId} onChange={(e) => setTemplateId(e.target.value)}>
          <option value="">Tizim matni</option>
          {templates.map((t) => (
            <option key={t.id} value={t.id}>
              {t.name}
            </option>
          ))}
        </Select>
        <div>
          <p className="mb-1.5 text-xs font-medium text-slate-500">Kimning Telegramiga</p>
          <RelationChips value={relations} onChange={setRelations} />
        </div>
        <label className="inline-flex cursor-pointer items-center gap-2 text-sm text-slate-700">
          <input type="checkbox" checked={ack} onChange={(e) => setAck(e.target.checked)} className="h-4 w-4 accent-brand-600" />
          "Qabul qildim" tugmasi
        </label>
        <TelegramPreview html={preview?.html ?? ''} mode={preview?.mode ?? 'text'} loading={loading} ackButton={ack} />
        {errors.length > 0 && <p className="text-sm font-medium text-red-600">{errors.join('; ')}</p>}
        <p className={cn('text-sm', countError ? 'font-medium text-red-600' : 'text-slate-700')}>
          {count ? countLine(count) : countError || 'Sanalmoqda...'}
        </p>
        {error && <p className="text-sm font-medium text-red-600">{error}</p>}
      </div>
    </RightDrawer>
  )
}
