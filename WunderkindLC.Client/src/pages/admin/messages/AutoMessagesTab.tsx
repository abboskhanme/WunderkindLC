import { useEffect, useMemo, useState, type MouseEvent as ReactMouseEvent } from 'react'
import { AlertTriangle, Bell, CheckCircle2, MessageCircle, Paperclip, Plus, Sparkles, Trash2, Zap } from 'lucide-react'
import {
  getAutoMessageTriggers,
  getAutoMessageRules,
  createAutoMessageRule,
  updateAutoMessageRule,
  deleteAutoMessageRule,
  seedStandardRules,
  getMessageTokens,
  audienceLabel,
  sendScopeOptions,
  scheduleTypeOptions,
  type AutoMessageTrigger,
  type AutoMessageRule,
  type AutoMessageRuleInput,
  type MessageToken,
} from '@/api/services/autoMessages'
import { Card } from '@/components/ui/Card'
import { Button } from '@/components/ui/Button'
import { Badge } from '@/components/ui/Badge'
import { Loader } from '@/components/ui/Loader'
import { Select, Input } from '@/components/ui/Input'
import { NumberInput } from '@/components/ui/NumberInput'
import { Modal } from '@/components/ui/Modal'
import { cn, apiErrorMessage } from '@/lib/utils'
import { MessageEditor } from '@/components/messaging/MessageEditor'
import { TelegramPreview } from '@/components/messaging/TelegramPreview'
import { AttachmentPicker, type PickedAttachment } from '@/components/messaging/AttachmentPicker'
import { useMessagePreview } from '@/components/messaging/useTelegramDraft'

/** Bo'limlar tartibi (trigger.category bo'yicha guruhlash). */
const CATEGORY_ORDER = ["O'quv jarayoni", 'Moliya', 'Boshqa'] as const

/** Backend `category` bermasa — client-side fallback xarita. */
const CATEGORY_FALLBACK: Record<string, string> = {
  student_added: "O'quv jarayoni",
  attendance_absent: "O'quv jarayoni",
  attendance_present: "O'quv jarayoni",
  lesson_attendance: "O'quv jarayoni",
  birthday: "O'quv jarayoni",
  grade_entered: "O'quv jarayoni",
  achievement: "O'quv jarayoni",
  test_score: "O'quv jarayoni",
  rating_top: "O'quv jarayoni",
  rating_bottom: "O'quv jarayoni",
  payment_received: 'Moliya',
  monthly_charge: 'Moliya',
  payment_debt: 'Moliya',
}

function triggerCategory(t: AutoMessageTrigger): string {
  const c = t.category || CATEGORY_FALLBACK[t.key] || 'Boshqa'
  return (CATEGORY_ORDER as readonly string[]).includes(c) ? c : 'Boshqa'
}

/** Ro'yxatdagi jadval izohi: "Har oyning 10-kuni, 09:00" / "Har kuni 09:00". */
function scheduleSummary(t: AutoMessageTrigger | undefined, r: AutoMessageRule): string {
  if (!t) return ''
  if (t.scheduleMode === 'monthly_day_time') return `Har oyning ${r.scheduleDayOfMonth}-kuni, ${r.scheduleTime}`
  if (t.scheduleMode === 'daily_time') return `Har kuni ${r.scheduleTime}`
  return ''
}

/**
 * «Avto xabarlar» (CRM upgrade P2.8): hodisa yuz berganda (yoki jadval bo'yicha) o'zi ketadigan xabarlar.
 * Har yoqilgan qoida kabinetga bildirishnoma + Telegram (OUTBOX — sanoq, hisobot, «Qabul qildim»)
 * yuboradi; `enabled` yolg'iz hal qiladi (Telegram kaliti yo'q — 2026-10-12). Qoidada formatlash,
 * preview, bitta fayl, «Qabul qildim» tugmasi va jadval (oyning kuni + soat / soat).
 * Qo'lda yuboriladigan matnlar — «Shablonlar» tabida.
 */
export function AutoMessagesTab({
  canCreate = true,
  canEdit = true,
  canDelete = true,
}: {
  canCreate?: boolean
  canEdit?: boolean
  canDelete?: boolean
} = {}) {
  const [triggers, setTriggers] = useState<AutoMessageTrigger[]>([])
  const [rules, setRules] = useState<AutoMessageRule[]>([])
  const [allTokens, setAllTokens] = useState<MessageToken[]>([])
  const [loading, setLoading] = useState(true)
  const [failed, setFailed] = useState(false)
  const [err, setErr] = useState('')
  const [seeding, setSeeding] = useState(false)
  const [seedMsg, setSeedMsg] = useState('')
  /** Modal: 'new' — yaratish; qoida — tahrirlash; null — yopiq. */
  const [modal, setModal] = useState<'new' | AutoMessageRule | null>(null)

  // error-visibility.md §4 — fon O'QISH: yozuv (saqlash/o'chirish) allaqachon muvaffaqiyatli bo'lgan va o'z xatosini
  // ko'rsatgan; ro'yxatni qayta o'qish yiqilsa eski ro'yxat qoladi (asosiy yuklash xatosi — `failed` bilan ko'rinadi).
  const reload = () => getAutoMessageRules().then(setRules).catch(() => undefined)

  useEffect(() => {
    Promise.all([getAutoMessageTriggers(), getAutoMessageRules(), getMessageTokens()])
      .then(([t, r, tk]) => {
        setTriggers(t)
        setRules(r)
        setAllTokens(tk)
      })
      .catch(() => setFailed(true))
      .finally(() => setLoading(false))
  }, [])

  const byCategory = useMemo(() => {
    const m = new Map<string, AutoMessageRule[]>()
    for (const r of rules) {
      const t = triggers.find((x) => x.key === r.trigger)
      const c = t ? triggerCategory(t) : 'Boshqa'
      m.set(c, [...(m.get(c) ?? []), r])
    }
    return m
  }, [rules, triggers])

  const doSeed = async () => {
    setSeeding(true)
    setSeedMsg('')
    setErr('')
    try {
      const { created } = await seedStandardRules()
      await reload()
      setSeedMsg(
        created > 0
          ? `${created} ta tayyor avto xabar qo'shildi. Kerak bo'lsa tahrirlang.`
          : 'Barcha standart avto xabarlar allaqachon mavjud.',
      )
    } catch (e) {
      setErr(apiErrorMessage(e, "Namunaviy xabarlarni qo'shib bo'lmadi"))
    } finally {
      setSeeding(false)
    }
  }

  const toggleEnabled = async (rule: AutoMessageRule) => {
    try {
      await updateAutoMessageRule(rule.id, { ...toInput(rule), enabled: !rule.enabled })
      await reload()
    } catch (e) {
      setErr(apiErrorMessage(e, "O'zgartirib bo'lmadi"))
    }
  }

  const remove = async (rule: AutoMessageRule, e: ReactMouseEvent) => {
    e.stopPropagation()
    if (!window.confirm(`"${rule.name || 'Xabar'}" o'chirilsinmi?`)) return
    try {
      await deleteAutoMessageRule(rule.id)
      await reload()
    } catch (e2) {
      setErr(apiErrorMessage(e2, "O'chirib bo'lmadi"))
    }
  }

  if (loading) return <Loader label="Yuklanmoqda..." />

  if (failed || triggers.length === 0) {
    return (
      <Card>
        <div className="flex items-start gap-2 text-sm text-slate-500">
          <AlertTriangle className="mt-0.5 h-4 w-4 shrink-0 text-amber-500" />
          <p>Avto xabar hodisalari topilmadi. Keyinroq qayta urinib ko'ring.</p>
        </div>
      </Card>
    )
  }

  return (
    <div className="space-y-4">
      {err && (
        <div className="flex items-start gap-2 rounded-xl border border-red-200 bg-red-50 px-4 py-3 text-sm text-red-800">
          <AlertTriangle className="mt-0.5 h-4 w-4 shrink-0" />
          <p className="flex-1">{err}</p>
          <button type="button" onClick={() => setErr('')} className="text-red-400 hover:text-red-700" aria-label="Yopish">
            ×
          </button>
        </div>
      )}
      {seedMsg && (
        <div className="flex items-start gap-2 rounded-xl border border-emerald-200 bg-emerald-50 px-4 py-3 text-sm text-emerald-800">
          <Sparkles className="mt-0.5 h-4 w-4 shrink-0" />
          <p className="flex-1">{seedMsg}</p>
          <button type="button" onClick={() => setSeedMsg('')} className="text-emerald-400 hover:text-emerald-700" aria-label="Yopish">
            ×
          </button>
        </div>
      )}

      <div className="flex flex-wrap items-center justify-between gap-3">
        <div>
          <h3 className="text-sm font-bold text-slate-700">Avto xabarlar ({rules.length})</h3>
          <p className="text-xs text-slate-500">
            Hodisa yuz berganda (to'lov, davomat, tug'ilgan kun ...) o'zi ketadi — kabinetga va Telegram'ga.
          </p>
        </div>
        <div className="flex items-center gap-2">
          {canCreate && (
            <Button variant="secondary" onClick={doSeed} disabled={seeding}>
              <Sparkles className="h-4 w-4" /> {seeding ? "Qo'shilmoqda..." : 'Namunaviy xabarlar'}
            </Button>
          )}
          {canCreate && (
            <Button onClick={() => setModal('new')}>
              <Plus className="h-4 w-4" /> Yangi avto xabar
            </Button>
          )}
        </div>
      </div>

      {rules.length === 0 ? (
        <Card>
          <div className="flex flex-col items-center gap-3 py-10 text-center">
            <div className="rounded-full bg-brand-50 p-3">
              <Zap className="h-6 w-6 text-brand-500" />
            </div>
            <h4 className="font-semibold text-slate-700">Hali avto xabar yo'q</h4>
            <p className="text-sm text-slate-500">«Yangi avto xabar» bilan boshlang yoki tayyor to'plamni qo'shing.</p>
          </div>
        </Card>
      ) : (
        CATEGORY_ORDER.filter((c) => (byCategory.get(c) ?? []).length > 0).map((cat) => (
          <div key={cat} className="space-y-2">
            <p className="text-xs font-semibold uppercase tracking-wide text-slate-400">{cat}</p>
            {(byCategory.get(cat) ?? []).map((rule) => {
              const t = triggers.find((x) => x.key === rule.trigger)
              const sched = scheduleSummary(t, rule)
              return (
                <button
                  key={rule.id}
                  type="button"
                  disabled={!canEdit}
                  onClick={() => canEdit && setModal(rule)}
                  className={cn(
                    'flex w-full items-center gap-3 rounded-xl border p-3 text-left transition-colors',
                    canEdit && 'hover:border-brand-300 hover:bg-brand-50/40',
                    !canEdit && 'cursor-default',
                    rule.enabled ? 'border-slate-200 bg-white' : 'border-slate-100 bg-slate-50',
                  )}
                >
                  <span
                    role="switch"
                    aria-checked={rule.enabled}
                    onClick={(e) => {
                      e.stopPropagation()
                      if (canEdit) toggleEnabled(rule)
                    }}
                    className={cn(
                      'relative h-5 w-9 shrink-0 rounded-full transition-colors',
                      canEdit ? 'cursor-pointer' : 'cursor-default opacity-60',
                      rule.enabled ? 'bg-emerald-500' : 'bg-slate-300',
                    )}
                    title={rule.enabled ? 'Yoqilgan' : "O'chirilgan"}
                  >
                    <span
                      className={cn(
                        'absolute top-0.5 h-4 w-4 rounded-full bg-white shadow transition-transform',
                        rule.enabled ? 'translate-x-4' : 'translate-x-0.5',
                      )}
                    />
                  </span>
                  <div className="min-w-0 flex-1">
                    <div className="flex flex-wrap items-center gap-1.5">
                      <span className={cn('truncate font-semibold', rule.enabled ? 'text-slate-800' : 'text-slate-400')}>
                        {rule.name || t?.label || rule.trigger}
                      </span>
                      <Badge tone="blue">
                        <span className="inline-flex items-center gap-1">
                          <MessageCircle className="h-3 w-3" /> Telegram
                        </span>
                      </Badge>
                      {rule.ackButton && (
                        <Badge tone="green">
                          <span className="inline-flex items-center gap-1">
                            <CheckCircle2 className="h-3 w-3" /> Qabul qildim
                          </span>
                        </Badge>
                      )}
                      {rule.attachmentId && (
                        <Badge tone="default">
                          <span className="inline-flex items-center gap-1">
                            <Paperclip className="h-3 w-3" /> Fayl
                          </span>
                        </Badge>
                      )}
                    </div>
                    <p className="mt-0.5 truncate text-xs text-slate-400">
                      {t?.label ?? rule.trigger}
                      {(t?.audiences.length ?? 0) > 0 && ` · ${audienceLabel(rule.audience)}`}
                      {sched && ` · ${sched}`}
                    </p>
                  </div>
                  {canDelete && (
                    <span
                      onClick={(e) => remove(rule, e)}
                      className="shrink-0 rounded-lg p-1.5 text-slate-400 transition-colors hover:bg-red-50 hover:text-red-600"
                      title="O'chirish"
                    >
                      <Trash2 className="h-4 w-4" />
                    </span>
                  )}
                </button>
              )
            })}
          </div>
        ))
      )}

      {modal && (
        <RuleFormModal
          key={modal === 'new' ? 'new' : modal.id}
          triggers={triggers}
          allTokens={allTokens}
          rule={modal === 'new' ? null : modal}
          onClose={() => setModal(null)}
          onSaved={async () => {
            await reload()
            setModal(null)
          }}
        />
      )}
    </div>
  )
}

/** Qoidadan saqlash so'rovi (o'qishdagi qo'shimcha maydonlarsiz). */
function toInput(r: AutoMessageRule): AutoMessageRuleInput {
  return {
    trigger: r.trigger,
    name: r.name,
    enabled: r.enabled,
    sendTelegram: true,
    audience: r.audience,
    template: r.template,
    offsetMinutes: r.offsetMinutes,
    sendScope: r.sendScope,
    scheduleType: r.scheduleType,
    scheduleTime: r.scheduleTime,
    scheduleDayOfMonth: r.scheduleDayOfMonth,
    ackButton: r.ackButton ?? false,
    attachmentId: r.attachmentId ?? null,
  }
}

/** Yaratish/tahrirlash oynasi: bo'lim → hodisa → kimga → jadval → matn (formatlash + preview) → fayl → tugma. */
function RuleFormModal({
  triggers,
  allTokens,
  rule,
  onClose,
  onSaved,
}: {
  triggers: AutoMessageTrigger[]
  allTokens: MessageToken[]
  rule: AutoMessageRule | null
  onClose: () => void
  onSaved: () => Promise<void> | void
}) {
  const editing = !!rule
  const triggersByCategory = useMemo(() => {
    const m = new Map<string, AutoMessageTrigger[]>()
    for (const t of triggers) m.set(triggerCategory(t), [...(m.get(triggerCategory(t)) ?? []), t])
    return m
  }, [triggers])

  const initialTrigger = (rule ? triggers.find((t) => t.key === rule.trigger) : undefined) ?? triggers[0]

  const [category, setCategory] = useState(initialTrigger ? triggerCategory(initialTrigger) : CATEGORY_ORDER[0])
  const [triggerKey, setTriggerKey] = useState(initialTrigger?.key ?? '')
  const [name, setName] = useState(rule?.name ?? initialTrigger?.label ?? '')
  const [body, setBody] = useState(rule?.template ?? initialTrigger?.defaultTemplate ?? '')
  const [audience, setAudience] = useState(rule?.audience ?? initialTrigger?.defaultAudience ?? 'parents')
  const [sendScope, setSendScope] = useState(rule?.sendScope || sendScopeOptions[0].value)
  const [offsetMinutes, setOffsetMinutes] = useState<number | null>(rule?.offsetMinutes != null ? Math.abs(rule.offsetMinutes) : null)
  const [offsetBefore, setOffsetBefore] = useState<boolean>((rule?.offsetMinutes ?? 0) < 0)
  const [scheduleType, setScheduleType] = useState(rule?.scheduleType || 'daily')
  const [scheduleTime, setScheduleTime] = useState(rule?.scheduleTime || initialTrigger?.defaultTime || '09:00')
  const [scheduleDay, setScheduleDay] = useState<number | null>(rule?.scheduleDayOfMonth ?? initialTrigger?.defaultDay ?? 1)
  const [ack, setAck] = useState<boolean>(rule ? !!rule.ackButton : !!initialTrigger?.defaultAck)
  const [attachment, setAttachment] = useState<PickedAttachment | null>(rule?.attachment ?? null)
  const [saving, setSaving] = useState(false)
  const [error, setError] = useState('')

  const trigger = triggers.find((t) => t.key === triggerKey)
  const mode = trigger?.scheduleMode ?? 'none'
  const { preview, loading } = useMessagePreview({
    template: body,
    trigger: triggerKey,
    attachmentId: attachment?.id ?? null,
    enabled: !!trigger && trigger.tokens.length > 0,
  })
  const templateErrors = preview?.errors ?? []

  const editorTokens = useMemo(() => {
    const groups = ['student', 'common', 'event']
    const seen = new Set<string>()
    const list = allTokens
      .filter((t) => groups.includes(t.group) && !seen.has(t.token) && seen.add(t.token))
      .map((t) => ({ token: t.token, label: t.label }))
    return list.length > 0 ? list : (trigger?.tokens ?? []).map((tk) => ({ token: tk, label: tk }))
  }, [allTokens, trigger])

  const applyDefaults = (t: AutoMessageTrigger | undefined) => {
    if (!t) return
    setName(t.label)
    setAudience(t.defaultAudience)
    setBody(t.defaultTemplate ?? '')
    setSendScope(sendScopeOptions[0].value)
    setScheduleType('daily')
    setScheduleTime(t.defaultTime ?? '09:00')
    setScheduleDay(t.defaultDay ?? 1)
    setAck(!!t.defaultAck)
  }

  const save = async () => {
    if (!trigger) {
      setError('Hodisa tanlang')
      return
    }
    if (!name.trim()) {
      setError('Nom kerak')
      return
    }
    if (trigger.tokens.length > 0 && !trigger.templateOptional && !body.trim()) {
      setError('Xabar matni kerak')
      return
    }
    if (mode === 'monthly_day_time' && (scheduleDay == null || scheduleDay < 1 || scheduleDay > 31)) {
      setError("Oyning kuni 1–31 oralig'ida bo'lsin")
      return
    }
    if (trigger.supportsSchedule && scheduleType === 'monthly' && (scheduleDay == null || scheduleDay < 1)) {
      setError('Oy kunini kiriting (1-31)')
      return
    }
    setSaving(true)
    setError('')
    try {
      const scheduled = mode === 'monthly_day_time' || mode === 'daily_time' || trigger.supportsSchedule || trigger.supportsSendScope
      const input: AutoMessageRuleInput = {
        trigger: trigger.key,
        name: name.trim(),
        enabled: rule?.enabled ?? true,
        sendTelegram: true,
        audience,
        template: body,
        offsetMinutes: (offsetBefore ? -1 : 1) * (offsetMinutes ?? 0),
        sendScope: trigger.supportsSendScope ? sendScope : '',
        scheduleType: mode === 'monthly_day_time' ? 'monthly' : trigger.supportsSchedule ? scheduleType : 'daily',
        scheduleTime: scheduled ? scheduleTime : '09:00',
        scheduleDayOfMonth: scheduleDay ?? 1,
        ackButton: ack,
        attachmentId: attachment?.id ?? null,
      }
      if (rule) await updateAutoMessageRule(rule.id, input)
      else await createAutoMessageRule(input)
      await onSaved()
    } catch (e) {
      setError(apiErrorMessage(e, "Saqlab bo'lmadi"))
    } finally {
      setSaving(false)
    }
  }

  return (
    <Modal
      open
      onClose={onClose}
      size="xl"
      title={editing ? 'Avto xabarni tahrirlash' : 'Yangi avto xabar'}
      footer={
        <>
          <Button variant="secondary" onClick={onClose} disabled={saving}>
            Bekor qilish
          </Button>
          <Button onClick={save} disabled={saving || templateErrors.length > 0}>
            {saving ? 'Saqlanmoqda...' : editing ? 'Saqlash' : 'Yaratish'}
          </Button>
        </>
      }
    >
      <div className="grid grid-cols-1 gap-5 lg:grid-cols-[1fr_320px]">
        <div className="space-y-4">
          <div className="grid grid-cols-1 gap-3 sm:grid-cols-2">
            <Select
              label="Bo'lim"
              value={category}
              disabled={editing}
              onChange={(e) => {
                setCategory(e.target.value)
                const first = (triggersByCategory.get(e.target.value) ?? [])[0]
                if (first) {
                  setTriggerKey(first.key)
                  applyDefaults(first)
                }
              }}
            >
              {CATEGORY_ORDER.filter((c) => (triggersByCategory.get(c) ?? []).length > 0).map((c) => (
                <option key={c} value={c}>
                  {c}
                </option>
              ))}
            </Select>
            <Select
              label="Hodisa"
              value={triggerKey}
              disabled={editing}
              onChange={(e) => {
                setTriggerKey(e.target.value)
                applyDefaults(triggers.find((t) => t.key === e.target.value))
              }}
            >
              {(triggersByCategory.get(category) ?? []).map((t) => (
                <option key={t.key} value={t.key}>
                  {t.label}
                </option>
              ))}
            </Select>
          </div>
          {trigger && <p className="-mt-2 text-xs text-slate-500">{trigger.description}</p>}

          <Input label="Nom" value={name} onChange={(e) => setName(e.target.value)} placeholder="Xabar nomi" />

          {trigger && trigger.audiences.length > 1 && (
            <Select label="Kimga" value={audience} onChange={(e) => setAudience(e.target.value)}>
              {trigger.audiences.map((a) => (
                <option key={a} value={a}>
                  {audienceLabel(a)}
                </option>
              ))}
            </Select>
          )}

          <div className="flex flex-wrap gap-2 text-xs">
            <span className="inline-flex items-center gap-1.5 rounded-lg border border-slate-200 bg-slate-50 px-2.5 py-1.5 font-medium text-slate-500">
              <Bell className="h-3.5 w-3.5" /> Kabinet (bildirishnoma) — doim
            </span>
            <span className="inline-flex items-center gap-1.5 rounded-lg border border-sky-200 bg-sky-50 px-2.5 py-1.5 font-medium text-sky-700">
              <MessageCircle className="h-3.5 w-3.5" /> Telegram — faqat botga ulanganlar
            </span>
          </div>

          {/* Jadval — rejimga qarab */}
          {mode === 'monthly_day_time' && (
            <div className="grid grid-cols-2 gap-2">
              <div>
                <NumberInput
                  label="Oyning qaysi kuni"
                  placeholder="1"
                  value={scheduleDay}
                  onChange={(v) => setScheduleDay(v === null ? null : Math.min(Math.max(v, 0), 31))}
                />
                <p className="mt-1 text-xs text-slate-400">31 — qisqa oylarda oyning oxirgi kuni</p>
              </div>
              <Input label="Soat" type="time" value={scheduleTime} onChange={(e) => setScheduleTime(e.target.value)} />
            </div>
          )}
          {mode === 'daily_time' && (
            <Input label="Soat" type="time" value={scheduleTime} onChange={(e) => setScheduleTime(e.target.value)} />
          )}
          {trigger?.supportsSendScope && (
            <div className="grid grid-cols-2 gap-2">
              <Select label="Qamrov" value={sendScope} onChange={(e) => setSendScope(e.target.value)}>
                {sendScopeOptions.map((o) => (
                  <option key={o.value} value={o.value}>
                    {o.label}
                  </option>
                ))}
              </Select>
              {sendScope === 'lesson_start' ? (
                <div className="grid grid-cols-2 gap-2">
                  <Select label="Qachon" value={offsetBefore ? 'before' : 'after'} onChange={(e) => setOffsetBefore(e.target.value === 'before')}>
                    <option value="before">Darsdan oldin</option>
                    <option value="after">Dars boshlangach</option>
                  </Select>
                  <NumberInput label="Daqiqa" placeholder={offsetBefore ? '90' : '0'} value={offsetMinutes} onChange={setOffsetMinutes} />
                </div>
              ) : (
                <Input label="Yuborish vaqti" type="time" value={scheduleTime} onChange={(e) => setScheduleTime(e.target.value)} />
              )}
            </div>
          )}
          {trigger?.supportsSchedule && (
            <div className="grid grid-cols-3 gap-2">
              <Select label="Reja" value={scheduleType} onChange={(e) => setScheduleType(e.target.value)}>
                {scheduleTypeOptions.map((o) => (
                  <option key={o.value} value={o.value}>
                    {o.label}
                  </option>
                ))}
              </Select>
              <Input label="Vaqt" type="time" value={scheduleTime} onChange={(e) => setScheduleTime(e.target.value)} />
              {scheduleType === 'monthly' && (
                <NumberInput
                  label="Oy kuni (1-31)"
                  placeholder="1"
                  value={scheduleDay}
                  onChange={(v) => setScheduleDay(v === null ? null : Math.min(v, 31))}
                />
              )}
            </div>
          )}

          {trigger && trigger.tokens.length === 0 ? (
            <p className="rounded-lg bg-slate-50 px-3 py-2 text-xs text-slate-500">
              Bu hodisa matni tizim tomonidan avtomatik tuziladi — alohida matn shart emas.
            </p>
          ) : (
            <div>
              <MessageEditor
                formatting
                label="Xabar matni"
                value={body}
                onChange={setBody}
                tokens={editorTokens}
                rows={5}
                placeholder={trigger?.templateOptional ? "Matn (ixtiyoriy) — o'zgaruvchilar bilan" : "Xabar matni — o'zgaruvchilar bilan"}
                error={templateErrors.length > 0 ? templateErrors.join('; ') : null}
              />
              {trigger?.templateOptional && (
                <p className="mt-1.5 text-xs text-slate-400">
                  Matn <b>ixtiyoriy</b>: bo'sh qoldirsangiz — tizim matnni o'zi tuzadi (har kurs uchun alohida).
                </p>
              )}
            </div>
          )}

          <AttachmentPicker label="Rasm/fayl" value={attachment} onChange={setAttachment} />

          <label className="inline-flex cursor-pointer items-center gap-2 text-sm text-slate-700">
            <input type="checkbox" checked={ack} onChange={(e) => setAck(e.target.checked)} className="h-4 w-4 accent-brand-600" />
            "Qabul qildim" tugmasi
          </label>

          {error && <p className="text-sm font-medium text-red-600">{error}</p>}
        </div>

        <div>
          <p className="mb-1.5 text-sm font-medium text-slate-600">Telegram'da ko'rinishi</p>
          <TelegramPreview
            html={preview?.html ?? ''}
            mode={preview?.mode ?? 'text'}
            loading={loading}
            ackButton={ack}
            attachment={attachment}
            emptyHint={trigger?.templateOptional ? 'Tizim matni yuboriladi' : undefined}
          />
          {preview?.sample && (
            <p className="mt-1.5 text-xs text-slate-400">Namuna: {preview.sample.fullName}</p>
          )}
        </div>
      </div>
    </Modal>
  )
}
