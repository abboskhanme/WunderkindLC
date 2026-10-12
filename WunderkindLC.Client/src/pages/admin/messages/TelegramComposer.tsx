import { useEffect, useMemo, useState } from 'react'
import { AlertTriangle, Check, ChevronDown, Search, Send } from 'lucide-react'
import type { Group, Subject, Teacher } from '@/types'
import { getClasses } from '@/api/services/classes'
import { getSubjects } from '@/api/services/subjects'
import { getTeachers } from '@/api/services/teachers'
import {
  getMessageRecipients,
  getMessageTeacherRecipients,
  getPickableTemplates,
  getTelegramStatus,
  type MessageRecipient,
  type MessageTeacherRecipient,
  type PickableTemplate,
} from '@/api/services/messages'
import { getMessageTokens } from '@/api/services/autoMessages'
import {
  createCampaign,
  type AudienceSpec,
  type CampaignKind,
  type MemberStatus,
} from '@/api/services/messaging'
import { Card } from '@/components/ui/Card'
import { Button } from '@/components/ui/Button'
import { Badge } from '@/components/ui/Badge'
import { Modal } from '@/components/ui/Modal'
import { Input } from '@/components/ui/Input'
import { MessageEditor, type TokenDef } from '@/components/messaging/MessageEditor'
import { TelegramPreview } from '@/components/messaging/TelegramPreview'
import { AttachmentPicker, type PickedAttachment } from '@/components/messaging/AttachmentPicker'
import { RelationChips } from '@/components/messaging/RelationChips'
import { MultiPick } from '@/components/messaging/MultiPick'
import { useAudienceCount, useMessagePreview } from '@/components/messaging/useTelegramDraft'
import { PARENT_RELATIONS, countLine, relationLabel, type Relation } from '@/lib/relations'
import { apiErrorMessage, cn } from '@/lib/utils'

type AudTab = 'active' | 'archive' | 'selected' | 'teachers'

const AUD_TABS: { key: AudTab; label: string }[] = [
  { key: 'active', label: "Faol o'quvchilar" },
  { key: 'archive', label: 'Arxiv' },
  { key: 'selected', label: 'Tanlangan' },
  { key: 'teachers', label: "O'qituvchilar" },
]

const STATUS_CHIPS: { key: MemberStatus; label: string }[] = [
  { key: 'active', label: 'Aktiv' },
  { key: 'trial', label: 'Sinov' },
  { key: 'frozen', label: 'Muzlatilgan' },
]

/**
 * «Xabar yuborish» — TELEGRAM KOMPOZITORI (CRM upgrade P2.8). Kimga → matn (formatlash + tokenlar +
 * shablon) → bitta fayl → «Qabul qildim» tugmasi → o'ng ustunda server preview'i, pastda yuborishdan
 * OLDINGI sanoq. Yuborish — tasdiqlash oynasi → `POST messaging/campaigns` → «Tarix»da hisobot ochiladi.
 * Faol va ARXIV o'quvchilar hech qachon aralashmaydi (server ham rad etadi).
 */
export function TelegramComposer({
  canSend,
  onSent,
}: {
  canSend: boolean
  /** Kampaniya yaratildi — «Tarix»ga o'tib hisobotni ochish. */
  onSent: (campaignId: string) => void
}) {
  const [aud, setAud] = useState<AudTab>('active')
  // Faol
  const [groupIds, setGroupIds] = useState<string[]>([])
  const [courseIds, setCourseIds] = useState<string[]>([])
  const [teacherId, setTeacherId] = useState<string[]>([])
  const [statuses, setStatuses] = useState<MemberStatus[]>(['active', 'trial'])
  const [onlyDebtors, setOnlyDebtors] = useState(false)
  // Arxiv
  const [archivedFrom, setArchivedFrom] = useState('')
  const [archivedTo, setArchivedTo] = useState('')
  const [archiveReason, setArchiveReason] = useState('')
  const [archiveCourses, setArchiveCourses] = useState<string[]>([])
  // Tanlangan / O'qituvchilar
  const [selStudents, setSelStudents] = useState<string[]>([])
  const [selTeachers, setSelTeachers] = useState<string[]>([])
  const [search, setSearch] = useState('')
  // Kimning Telegramiga
  const [relations, setRelations] = useState<Relation[]>([...PARENT_RELATIONS])
  // Xabar
  const [text, setText] = useState('')
  const [attachment, setAttachment] = useState<PickedAttachment | null>(null)
  const [ack, setAck] = useState(true)
  const [sampleId, setSampleId] = useState('')
  const [sampleOpen, setSampleOpen] = useState(false)
  const [sampleQ, setSampleQ] = useState('')
  // Ma'lumotnomalar
  const [groups, setGroups] = useState<Group[]>([])
  const [courses, setCourses] = useState<Subject[]>([])
  const [teachers, setTeachers] = useState<Teacher[]>([])
  const [recipients, setRecipients] = useState<MessageRecipient[]>([])
  const [teacherRecipients, setTeacherRecipients] = useState<MessageTeacherRecipient[]>([])
  const [tokens, setTokens] = useState<TokenDef[]>([])
  const [templates, setTemplates] = useState<PickableTemplate[]>([])
  const [botOk, setBotOk] = useState(true)
  // Yuborish
  const [confirmOpen, setConfirmOpen] = useState(false)
  const [sending, setSending] = useState(false)
  const [sendError, setSendError] = useState('')

  useEffect(() => {
    // Fon o'qishlar — filtrlar/ro'yxatlar uchun; xato bo'lsa ro'yxat bo'sh (yuborishda server sababni aytadi).
    getClasses().then((g) => setGroups(g.filter((x) => !x.isArchived))).catch(() => setGroups([]))
    getSubjects().then(setCourses).catch(() => setCourses([]))
    getTeachers().then((t) => setTeachers(t.filter((x) => !x.isArchived))).catch(() => setTeachers([]))
    getMessageRecipients().then(setRecipients).catch(() => setRecipients([]))
    getMessageTeacherRecipients().then(setTeacherRecipients).catch(() => setTeacherRecipients([]))
    getMessageTokens().then((ts) => setTokens(ts.filter((t) => t.group !== 'lead'))).catch(() => setTokens([]))
    getPickableTemplates().then(setTemplates).catch(() => setTemplates([]))
    getTelegramStatus().then((s) => setBotOk(s.configured)).catch(() => setBotOk(true))
  }, [])

  const audience: AudienceSpec | null = useMemo(() => {
    switch (aud) {
      case 'active':
        return {
          kind: 'active',
          groupIds: groupIds.length ? groupIds : undefined,
          courseIds: courseIds.length ? courseIds : undefined,
          teacherId: teacherId[0],
          memberStatuses: statuses,
          onlyDebtors,
          relations,
        }
      case 'archive':
        return {
          kind: 'archive',
          courseIds: archiveCourses.length ? archiveCourses : undefined,
          archivedFrom: archivedFrom || undefined,
          archivedTo: archivedTo || undefined,
          archiveReason: archiveReason.trim() || undefined,
          relations,
        }
      case 'selected':
        return selStudents.length ? { kind: 'selected', studentIds: selStudents, relations } : null
      default:
        return { kind: 'teachers', teacherIds: selTeachers.length ? selTeachers : undefined }
    }
  }, [aud, groupIds, courseIds, teacherId, statuses, onlyDebtors, archiveCourses, archivedFrom, archivedTo, archiveReason, selStudents, selTeachers, relations])

  const kind: CampaignKind = aud === 'archive' ? 'archive' : 'broadcast'
  const { preview, loading: previewLoading } = useMessagePreview({
    template: text,
    audience,
    studentId: sampleId || undefined,
    attachmentId: attachment?.id,
    kind,
  })
  const { count, loading: countLoading, error: countError } = useAudienceCount({ audience, template: text, kind })

  const errors = preview?.errors ?? []
  const empty = !text.trim() && !attachment
  const canSubmit = canSend && !!audience && !empty && errors.length === 0 && (count?.messages ?? 0) > 0 && !countLoading

  const filteredStudents = useMemo(() => {
    const q = search.trim().toLowerCase()
    return q ? recipients.filter((r) => r.fullName.toLowerCase().includes(q) || r.className.toLowerCase().includes(q)) : recipients
  }, [recipients, search])
  const sampleList = useMemo(() => {
    const q = sampleQ.trim().toLowerCase()
    const base = aud === 'selected' ? recipients.filter((r) => selStudents.includes(r.studentId)) : recipients
    return (q ? base.filter((r) => r.fullName.toLowerCase().includes(q)) : base).slice(0, 50)
  }, [recipients, sampleQ, aud, selStudents])

  const toggleIn = <T,>(list: T[], v: T): T[] => (list.includes(v) ? list.filter((x) => x !== v) : [...list, v])

  const send = async () => {
    if (!audience || sending) return
    setSending(true)
    setSendError('')
    try {
      const res = await createCampaign({
        kind,
        audience,
        template: text,
        attachmentId: attachment?.id ?? null,
        ackRequired: ack,
      })
      setConfirmOpen(false)
      setText('')
      setAttachment(null)
      onSent(res.id)
    } catch (e) {
      // Oyna OCHIQ qoladi — sabab shu yerda, matn saqlanadi (`error-visibility.md`).
      setSendError(apiErrorMessage(e, "Xabarni yuborib bo'lmadi"))
    } finally {
      setSending(false)
    }
  }

  const countText = count
    ? countLine({ ...count, teachers: aud === 'teachers' })
    : countError || (countLoading ? 'Sanalmoqda...' : aud === 'selected' ? "O'quvchilarni tanlang" : '')

  return (
    <div className="space-y-4">
      {!botOk && (
        <div className="flex items-start gap-2 rounded-xl border border-amber-200 bg-amber-50 px-4 py-3 text-sm text-amber-800">
          <AlertTriangle className="mt-0.5 h-4 w-4 shrink-0" />
          <p>Telegram bot sozlanmagan — xabar yuborilmaydi. «Sozlamalar → Xabar kanallari → Telegram»ni tekshiring.</p>
        </div>
      )}

      {/* 1. Kimlarga */}
      <Card title="Kimlarga" sub="Faol o'quvchilar va arxiv HECH QACHON aralashmaydi">
        <div className="space-y-3">
          <div className="tabs">
            {AUD_TABS.map((t) => (
              <button
                key={t.key}
                type="button"
                className={cn('tab', aud === t.key && 'active')}
                onClick={() => {
                  setAud(t.key)
                  setSampleId('')
                }}
              >
                {t.label}
              </button>
            ))}
          </div>

          {aud === 'active' && (
            <div className="space-y-3">
              <div className="grid grid-cols-1 gap-3 sm:grid-cols-3">
                <MultiPick
                  label="Guruh"
                  options={groups.map((g) => ({ id: g.id, label: g.name }))}
                  value={groupIds}
                  onChange={setGroupIds}
                />
                <MultiPick
                  label="Kurs"
                  options={courses.map((c) => ({ id: c.id, label: c.name }))}
                  value={courseIds}
                  onChange={setCourseIds}
                />
                <MultiPick
                  label="O'qituvchi"
                  single
                  options={teachers.map((t) => ({ id: t.id, label: t.fullName }))}
                  value={teacherId}
                  onChange={setTeacherId}
                />
              </div>
              <div className="flex flex-wrap items-center gap-1.5">
                <span className="mr-1 text-xs text-slate-500">Holat</span>
                {STATUS_CHIPS.map((s) => {
                  const on = statuses.includes(s.key)
                  return (
                    <button
                      key={s.key}
                      type="button"
                      aria-pressed={on}
                      onClick={() => {
                        const next = toggleIn(statuses, s.key)
                        if (next.length > 0) setStatuses(next)
                      }}
                      className={cn(
                        'inline-flex items-center gap-1 rounded-full border px-2.5 py-1 text-xs font-medium',
                        on ? 'border-brand-400 bg-brand-50 text-brand-700' : 'border-slate-200 text-slate-500 hover:bg-slate-50',
                      )}
                    >
                      {on && <Check className="h-3 w-3" />} {s.label}
                    </button>
                  )
                })}
                <label className="ml-2 inline-flex cursor-pointer items-center gap-2 rounded-lg border border-slate-200 px-3 py-1 text-xs text-slate-600 hover:bg-slate-50">
                  <input
                    type="checkbox"
                    checked={onlyDebtors}
                    onChange={(e) => setOnlyDebtors(e.target.checked)}
                    className="h-3.5 w-3.5 accent-brand-600"
                  />
                  Faqat qarzdorlar
                </label>
              </div>
            </div>
          )}

          {aud === 'archive' && (
            <div className="grid grid-cols-1 gap-3 sm:grid-cols-4">
              <Input label="Arxivlangan sana (dan)" type="date" value={archivedFrom} onChange={(e) => setArchivedFrom(e.target.value)} />
              <Input label="(gacha)" type="date" value={archivedTo} onChange={(e) => setArchivedTo(e.target.value)} />
              <MultiPick
                label="Kurs"
                options={courses.map((c) => ({ id: c.id, label: c.name }))}
                value={archiveCourses}
                onChange={setArchiveCourses}
              />
              <Input
                label="Sabab"
                placeholder="Sabab bo'yicha qidirish"
                value={archiveReason}
                onChange={(e) => setArchiveReason(e.target.value)}
              />
            </div>
          )}

          {aud === 'selected' && (
            <div>
              <div className="mb-2 flex items-center justify-between gap-2">
                <div className="relative flex-1">
                  <Search className="pointer-events-none absolute left-3 top-2.5 h-4 w-4 text-slate-400" />
                  <input
                    value={search}
                    onChange={(e) => setSearch(e.target.value)}
                    placeholder="O'quvchi yoki guruh..."
                    className="w-full rounded-lg border border-slate-200 py-2 pl-9 pr-3 text-sm outline-none focus:border-brand-400 focus:ring-2 focus:ring-brand-100"
                  />
                </div>
                <Badge tone="violet">
                  <span className="font-mono">{selStudents.length}</span> ta tanlandi
                </Badge>
              </div>
              <div className="max-h-72 space-y-1 overflow-y-auto">
                {filteredStudents.length === 0 && <p className="py-6 text-center text-sm text-slate-400">O'quvchi topilmadi</p>}
                {filteredStudents.map((r) => (
                  <PickRow
                    key={r.studentId}
                    active={selStudents.includes(r.studentId)}
                    onClick={() => setSelStudents((s) => toggleIn(s, r.studentId))}
                    title={r.fullName}
                    detail={r.className || '—'}
                    badge={r.telegramChats === 0 ? <Badge tone="amber">Telegram ulanmagan</Badge> : undefined}
                  />
                ))}
              </div>
              {selStudents.length > 0 && (
                <button type="button" onClick={() => setSelStudents([])} className="mt-2 text-xs text-slate-500 hover:text-slate-700">
                  Tanlovni tozalash ({selStudents.length})
                </button>
              )}
            </div>
          )}

          {aud === 'teachers' && (
            <MultiPick
              label="O'qituvchilar"
              placeholder="Barcha o'qituvchilar"
              options={teacherRecipients.map((t) => ({
                id: t.teacherId,
                label: t.fullName,
                hint: t.telegramChats === 0 ? 'ulanmagan' : undefined,
              }))}
              value={selTeachers}
              onChange={setSelTeachers}
            />
          )}

          {aud !== 'teachers' && (
            <div className="border-t border-slate-100 pt-3">
              <p className="mb-1.5 text-xs font-medium text-slate-500">Kimning Telegramiga</p>
              <RelationChips value={relations} onChange={setRelations} />
            </div>
          )}
        </div>
      </Card>

      <div className="grid grid-cols-1 gap-4 lg:grid-cols-2">
        {/* 2–4. Matn, fayl, tugma */}
        <Card title="Xabar" bodyClassName="space-y-4">
          <MessageEditor
            formatting
            value={text}
            onChange={setText}
            tokens={tokens}
            templates={templates.map((t) => ({ name: t.name, text: t.text }))}
            rows={7}
            label="Matn"
            placeholder="Xabar matni — o'zgaruvchilar har o'quvchiga moslab to'ldiriladi"
            hint={aud === 'teachers' ? "O'qituvchilarga faqat {fish} va umumiy o'zgaruvchilar ishlaydi." : 'Shablondan: yuqoridagi chiplar.'}
            error={errors.length > 0 ? errors.join('; ') : null}
          />
          <AttachmentPicker value={attachment} onChange={setAttachment} />
          {preview?.mode === 'separate' && (
            <p className="-mt-2 text-xs font-medium text-amber-700">
              Matn izoh uchun uzun — fayl va matn ikki alohida xabar bo'lib ketadi
            </p>
          )}
          <label className="inline-flex cursor-pointer items-center gap-2 text-sm text-slate-700">
            <input type="checkbox" checked={ack} onChange={(e) => setAck(e.target.checked)} className="h-4 w-4 accent-brand-600" />
            "Qabul qildim" tugmasi
          </label>
        </Card>

        {/* 5. Ko'rinish */}
        <Card
          title="Telegram'da ko'rinishi"
          actions={
            aud !== 'teachers' ? (
              <div className="relative">
                <button
                  type="button"
                  onClick={() => setSampleOpen((v) => !v)}
                  className="inline-flex items-center gap-1 rounded-lg border border-slate-200 px-2.5 py-1 text-xs text-slate-600 hover:bg-slate-50"
                >
                  Namuna: {preview?.sample?.fullName ?? '—'}
                  {preview?.sample?.relation ? ` (${relationLabel(preview.sample.relation)})` : ''}
                  <ChevronDown className="h-3.5 w-3.5" />
                </button>
                {sampleOpen && (
                  <>
                    <div className="fixed inset-0 z-30" onClick={() => setSampleOpen(false)} />
                    <div className="absolute right-0 z-40 mt-1 w-64 rounded-xl border border-slate-200 bg-white p-2 shadow-lg">
                      <input
                        autoFocus
                        value={sampleQ}
                        onChange={(e) => setSampleQ(e.target.value)}
                        placeholder="O'quvchi..."
                        className="mb-1.5 w-full rounded-lg border border-slate-200 px-2 py-1.5 text-sm outline-none focus:border-brand-400"
                      />
                      <div className="max-h-56 overflow-y-auto">
                        <button
                          type="button"
                          onClick={() => {
                            setSampleId('')
                            setSampleOpen(false)
                          }}
                          className="block w-full rounded-lg px-2 py-1.5 text-left text-sm text-slate-500 hover:bg-slate-50"
                        >
                          Avtomatik (birinchi oluvchi)
                        </button>
                        {sampleList.map((r) => (
                          <button
                            key={r.studentId}
                            type="button"
                            onClick={() => {
                              setSampleId(r.studentId)
                              setSampleOpen(false)
                            }}
                            className="block w-full truncate rounded-lg px-2 py-1.5 text-left text-sm text-slate-700 hover:bg-slate-50"
                          >
                            {r.fullName}
                          </button>
                        ))}
                      </div>
                    </div>
                  </>
                )}
              </div>
            ) : undefined
          }
        >
          <TelegramPreview
            html={preview?.html ?? ''}
            mode={preview?.mode ?? 'text'}
            loading={previewLoading}
            ackButton={ack}
            attachment={attachment}
          />
          {(preview?.warnings ?? []).filter((w) => !w.startsWith('Matn izoh')).map((w) => (
            <p key={w} className="mt-2 text-xs text-amber-700">
              {w}
            </p>
          ))}
        </Card>
      </div>

      {/* 6. Sanoq + yuborish */}
      <Card>
        <div className="flex flex-wrap items-center justify-between gap-3">
          <p className={cn('text-sm', countError ? 'font-medium text-red-600' : 'text-slate-700')}>{countText}</p>
          {canSend && (
            <Button onClick={() => setConfirmOpen(true)} disabled={!canSubmit || !botOk}>
              <Send className="h-4 w-4" /> Yuborish
            </Button>
          )}
        </div>
      </Card>

      <Modal
        open={confirmOpen}
        onClose={() => !sending && setConfirmOpen(false)}
        title="Xabarni yuborish"
        size="sm"
        footer={
          <>
            <Button variant="secondary" onClick={() => setConfirmOpen(false)} disabled={sending}>
              Bekor qilish
            </Button>
            <Button onClick={send} disabled={sending}>
              <Send className="h-4 w-4" /> {sending ? 'Yuborilmoqda...' : 'Tasdiqlash va yuborish'}
            </Button>
          </>
        }
      >
        <div className="space-y-2 text-sm text-slate-700">
          <p>{count ? countLine({ ...count, teachers: aud === 'teachers' }) : ''}</p>
          {attachment && <p className="text-xs text-slate-500">Fayl: {attachment.fileName}</p>}
          {ack && <p className="text-xs text-slate-500">«✅ Qabul qildim» tugmasi bilan</p>}
          {aud === 'archive' && <p className="text-xs font-medium text-amber-700">Faqat ARXIVDAGI o'quvchilarga.</p>}
          {sendError && <p className="text-sm font-medium text-red-600">{sendError}</p>}
        </div>
      </Modal>
    </div>
  )
}

/** "Tanlangan" ro'yxatidagi qator. */
function PickRow({
  active,
  onClick,
  title,
  detail,
  badge,
}: {
  active: boolean
  onClick: () => void
  title: string
  detail: string
  badge?: React.ReactNode
}) {
  return (
    <button
      type="button"
      onClick={onClick}
      className={cn(
        'flex w-full items-center gap-2 rounded-lg border px-3 py-2 text-left text-sm transition-colors',
        active ? 'border-brand-300 bg-brand-50' : 'border-slate-100 hover:bg-slate-50',
      )}
    >
      <span
        className={cn(
          'flex h-4 w-4 shrink-0 items-center justify-center rounded border',
          active ? 'border-brand-500 bg-brand-500 text-white' : 'border-slate-300',
        )}
      >
        {active && <Check className="h-3 w-3" />}
      </span>
      <span className="min-w-0 flex-1 truncate">
        <span className="font-medium text-slate-700">{title}</span>
        {detail && <span className="ml-1 text-xs text-slate-400">· {detail}</span>}
      </span>
      {badge}
    </button>
  )
}
