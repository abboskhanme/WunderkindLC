import { useEffect, useMemo, useState, type MouseEvent as ReactMouseEvent } from 'react'
import { Plus, Trash2, AlertTriangle, Sparkles, Hand, Zap } from 'lucide-react'
import {
  getAutoMessageTriggers,
  getAutoMessageRules,
  createAutoMessageRule,
  updateAutoMessageRule,
  deleteAutoMessageRule,
  seedStandardSms,
  getMessageTokens,
  audienceLabel,
  sendScopeOptions,
  scheduleTypeOptions,
  type AutoMessageTrigger,
  type AutoMessageRule,
  type AutoMessageRuleInput,
  type MessageToken,
} from '@/api/services/autoMessages'
import {
  getSmsTemplates,
  createSmsTemplate,
  updateSmsTemplate,
  deleteSmsTemplate,
  type SmsTemplate,
} from '@/api/services/messages'
import { Card } from '@/components/ui/Card'
import { Button } from '@/components/ui/Button'
import { Badge } from '@/components/ui/Badge'
import { Loader } from '@/components/ui/Loader'
import { Select, Input } from '@/components/ui/Input'
import { Modal } from '@/components/ui/Modal'
import { cn, apiErrorMessage } from '@/lib/utils'
import { CHANNEL_LIST, CHANNELS, type ChannelKey } from '@/config/channels'
import { MessageEditor } from '@/components/messaging/MessageEditor'
import { SmsProviderPicker } from '@/components/messaging/SmsProviderPicker'

/** Bo'limlar tartibi (trigger.category bo'yicha guruhlash). */
const CATEGORY_ORDER = ['Lidlar', "O'quv jarayoni", 'Moliya', 'Boshqa'] as const

/** Backend `category` bermasa — client-side fallback xarita. */
const CATEGORY_FALLBACK: Record<string, string> = {
  lead_new: 'Lidlar',
  trial_reminder: 'Lidlar',
  test_link: 'Lidlar',
  test_result: 'Lidlar',
  student_added: "O'quv jarayoni",
  attendance_absent: "O'quv jarayoni",
  lesson_attendance: "O'quv jarayoni",
  birthday: "O'quv jarayoni",
  grade_entered: "O'quv jarayoni",
  payment_received: 'Moliya',
  monthly_charge: 'Moliya',
  payment_debt: 'Moliya',
}

function triggerCategory(t: AutoMessageTrigger): string {
  const c = t.category || CATEGORY_FALLBACK[t.key] || 'Boshqa'
  return (CATEGORY_ORDER as readonly string[]).includes(c) ? c : 'Boshqa'
}

/** Qoidada YOQILGAN barcha kanallar (yagona tartibda) — badge uchun. */
function ruleChannels(r: Pick<AutoMessageRule, 'sendSms' | 'sendTelegram' | 'sendPush'>): ChannelKey[] {
  const out: ChannelKey[] = []
  for (const c of CHANNEL_LIST) {
    if (c.key === 'sms' && r.sendSms) out.push('sms')
    if (c.key === 'telegram' && r.sendTelegram) out.push('telegram')
    if (c.key === 'push' && r.sendPush) out.push('push')
  }
  return out
}

/** Trigger uchun mavjud kanallar (yagona tartibda). */
function availableChannels(t: AutoMessageTrigger | undefined): ChannelKey[] {
  if (!t) return ['sms']
  return CHANNEL_LIST.filter((c) => t.channels[c.key]).map((c) => c.key)
}

/** Yagona ro'yxat elementi — qo'lda matn (SmsTemplate) yoki avtomatik qoida (AutoMessageRule). */
type Item = { kind: 'manual'; tpl: SmsTemplate } | { kind: 'auto'; rule: AutoMessageRule }

/**
 * "Xabar yaratish" — YAGONA joy. Har xabar QO'LDA (tanlab yuboriladigan tayyor matn) yoki AVTOMATIK
 * (hodisa yuz berganda o'zi ketadigan) bo'lishi mumkin (yaratishda tanlanadi). Avtomatikda bir nechta
 * kanal (SMS/Telegram/Push) tanlash mumkin — tanlanganlarning hammasidan ketadi. Element ustiga bosilsa
 * tahrirlash ochiladi.
 */
export function AutoMessagesTab({
  highlightRuleId,
  canCreate = true,
  canEdit = true,
  canDelete = true,
}: {
  highlightRuleId?: string | null
  /** Ruxsat: "Yangi xabar" yaratish ko'rinsinmi (default true) */
  canCreate?: boolean
  /** Ruxsat: elementni bosib tahrirlash mumkinmi (default true) */
  canEdit?: boolean
  /** Ruxsat: o'chirish tugmasi ko'rinsinmi (default true) */
  canDelete?: boolean
} = {}) {
  const [triggers, setTriggers] = useState<AutoMessageTrigger[]>([])
  const [autoRules, setAutoRules] = useState<AutoMessageRule[]>([])
  const [manualTpls, setManualTpls] = useState<SmsTemplate[]>([])
  const [allTokens, setAllTokens] = useState<MessageToken[]>([])
  const [loading, setLoading] = useState(true)
  const [failed, setFailed] = useState(false)
  const [err, setErr] = useState('')
  const [seeding, setSeeding] = useState(false)
  const [seedMsg, setSeedMsg] = useState('')
  /** Modal: 'new' — yaratish; Item — tahrirlash; null — yopiq. */
  const [modal, setModal] = useState<'new' | Item | null>(null)

  const reload = () =>
    Promise.all([getAutoMessageRules().catch(() => []), getSmsTemplates().catch(() => [])]).then(
      ([r, m]) => {
        setAutoRules(r)
        setManualTpls(m)
      },
    )

  useEffect(() => {
    Promise.all([
      getAutoMessageTriggers(),
      getAutoMessageRules(),
      getSmsTemplates(),
      getMessageTokens(),
    ])
      .then(([t, r, m, tk]) => {
        setTriggers(t)
        setAutoRules(r)
        setManualTpls(m)
        setAllTokens(tk)
      })
      .catch(() => setFailed(true))
      .finally(() => setLoading(false))
  }, [])

  // "Xabar yuborish"dan "Sozlash" bosilganda shu avto qoidani tahrirlash uchun ochamiz.
  useEffect(() => {
    if (!highlightRuleId || autoRules.length === 0) return
    const r = autoRules.find((x) => x.id === highlightRuleId)
    if (r) setModal({ kind: 'auto', rule: r })
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [autoRules, highlightRuleId])

  const items: Item[] = useMemo(
    () => [
      ...manualTpls.map((tpl) => ({ kind: 'manual' as const, tpl })),
      ...autoRules.map((rule) => ({ kind: 'auto' as const, rule })),
    ],
    [manualTpls, autoRules],
  )

  const doSeed = async () => {
    setSeeding(true)
    setSeedMsg('')
    setErr('')
    try {
      const { created } = await seedStandardSms()
      await reload()
      setSeedMsg(
        created > 0
          ? `${created} ta tayyor SMS xabar qo'shildi. Kerak bo'lsa tahrirlang.`
          : 'Barcha standart SMS xabarlar allaqachon mavjud.',
      )
    } catch (e) {
      setErr(apiErrorMessage(e, "Namunaviy xabarlarni qo'shib bo'lmadi"))
    } finally {
      setSeeding(false)
    }
  }

  const toggleEnabled = async (rule: AutoMessageRule) => {
    try {
      const { id: _id, createdAt: _c, ...rest } = rule
      await updateAutoMessageRule(rule.id, { ...rest, enabled: !rule.enabled })
      await reload()
    } catch (e) {
      setErr(apiErrorMessage(e, "O'zgartirib bo'lmadi"))
    }
  }

  const removeItem = async (item: Item, e: ReactMouseEvent) => {
    e.stopPropagation()
    const name = item.kind === 'manual' ? item.tpl.name : item.rule.name
    if (!window.confirm(`"${name || 'Xabar'}" o'chirilsinmi?`)) return
    try {
      if (item.kind === 'manual') await deleteSmsTemplate(item.tpl.id)
      else await deleteAutoMessageRule(item.rule.id)
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
          <h3 className="text-sm font-bold text-slate-700">Yaratilgan xabarlar ({items.length})</h3>
          <p className="text-xs text-slate-500">
            Har xabar <b>qo'lda</b> (tanlab yuboriladigan matn) yoki <b>avtomatik</b> (hodisada o'zi ketadigan)
            bo'lishi mumkin. Element ustiga bosib tahrirlang.
          </p>
        </div>
        <div className="flex items-center gap-2">
          {canCreate && (
            <Button variant="secondary" onClick={doSeed} disabled={seeding}>
              <Sparkles className="h-4 w-4" /> {seeding ? 'Qo\'shilmoqda...' : 'Namunaviy SMS'}
            </Button>
          )}
          {canCreate && (
            <Button onClick={() => setModal('new')}>
              <Plus className="h-4 w-4" /> Yangi xabar
            </Button>
          )}
        </div>
      </div>

      {items.length === 0 ? (
        <Card>
          <div className="flex flex-col items-center gap-3 py-10 text-center">
            <div className="rounded-full bg-brand-50 p-3">
              <Plus className="h-6 w-6 text-brand-500" />
            </div>
            <div>
              <h4 className="font-semibold text-slate-700">Hali xabar yaratilmagan</h4>
              <p className="mt-1 text-sm text-slate-500">
                "Yangi xabar" bilan boshlang yoki tayyor SMS to'plamini qo'shing.
              </p>
            </div>
            <div className="flex gap-2">
              {canCreate && (
                <Button variant="secondary" onClick={doSeed} disabled={seeding}>
                  <Sparkles className="h-4 w-4" /> Namunaviy SMS qo'shish
                </Button>
              )}
              {canCreate && (
                <Button onClick={() => setModal('new')}>
                  <Plus className="h-4 w-4" /> Yangi xabar
                </Button>
              )}
            </div>
          </div>
        </Card>
      ) : (
        <div className="space-y-2">
          {items.map((item) => {
            const key = item.kind === 'manual' ? `m-${item.tpl.id}` : `a-${item.rule.id}`
            const name = item.kind === 'manual' ? item.tpl.name : item.rule.name
            const text = item.kind === 'manual' ? item.tpl.text : item.rule.template
            const enabled = item.kind === 'auto' ? item.rule.enabled : true
            return (
              <button
                key={key}
                type="button"
                disabled={!canEdit}
                onClick={() => canEdit && setModal(item)}
                className={cn(
                  'flex w-full items-center gap-3 rounded-xl border p-3 text-left transition-colors',
                  canEdit && 'hover:border-brand-300 hover:bg-brand-50/40',
                  !canEdit && 'cursor-default',
                  enabled ? 'border-slate-200 bg-white' : 'border-slate-100 bg-slate-50',
                )}
              >
                {item.kind === 'auto' ? (
                  <span
                    role="switch"
                    aria-checked={item.rule.enabled}
                    onClick={(e) => {
                      e.stopPropagation()
                      if (canEdit) toggleEnabled(item.rule)
                    }}
                    className={cn(
                      'relative h-5 w-9 shrink-0 rounded-full transition-colors',
                      canEdit ? 'cursor-pointer' : 'cursor-default opacity-60',
                      item.rule.enabled ? 'bg-emerald-500' : 'bg-slate-300',
                    )}
                    title={item.rule.enabled ? 'Yoqilgan' : "O'chirilgan"}
                  >
                    <span
                      className={cn(
                        'absolute top-0.5 h-4 w-4 rounded-full bg-white shadow transition-transform',
                        item.rule.enabled ? 'translate-x-4' : 'translate-x-0.5',
                      )}
                    />
                  </span>
                ) : (
                  <span className="flex h-5 w-9 shrink-0 items-center justify-center" title="Qo'lda">
                    <Hand className="h-4 w-4 text-slate-400" />
                  </span>
                )}

                <div className="min-w-0 flex-1">
                  <div className="flex flex-wrap items-center gap-1.5">
                    <span className={cn('truncate font-semibold', enabled ? 'text-slate-800' : 'text-slate-400')}>
                      {name || (item.kind === 'auto' ? item.rule.trigger : 'Nomsiz')}
                    </span>
                    {item.kind === 'auto' ? (
                      <>
                        <Badge tone="violet">
                          <span className="inline-flex items-center gap-1">
                            <Zap className="h-3 w-3" /> Avto
                          </span>
                        </Badge>
                        {ruleChannels(item.rule).map((ck) => {
                          const c = CHANNELS[ck]
                          const Icon = c.icon
                          return (
                            <Badge key={ck} tone={c.tone}>
                              <span className="inline-flex items-center gap-1">
                                <Icon className="h-3 w-3" /> {c.label}
                              </span>
                            </Badge>
                          )
                        })}
                      </>
                    ) : (
                      <Badge tone="default">Qo'lda</Badge>
                    )}
                  </div>
                  <p className="mt-0.5 truncate text-xs text-slate-400">
                    {item.kind === 'auto'
                      ? `${triggers.find((t) => t.key === item.rule.trigger)?.label ?? item.rule.trigger}${
                          (triggers.find((t) => t.key === item.rule.trigger)?.audiences.length ?? 0) > 0
                            ? ` · ${audienceLabel(item.rule.audience)}`
                            : ''
                        }`
                      : text || 'Matn yo\'q'}
                  </p>
                </div>

                {canDelete && (
                  <span
                    onClick={(e) => removeItem(item, e)}
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
      )}

      {modal && (
        <MessageFormModal
          triggers={triggers}
          allTokens={allTokens}
          item={modal === 'new' ? null : modal}
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

/** Yaratish/tahrirlash modali — "Avtomatik yuborish" tanlanadi; avtomatikda bo'lim/hodisa/kimga/kanallar/matn. */
function MessageFormModal({
  triggers,
  allTokens,
  item,
  onClose,
  onSaved,
}: {
  triggers: AutoMessageTrigger[]
  allTokens: MessageToken[]
  item: Item | null
  onClose: () => void
  onSaved: () => Promise<void> | void
}) {
  const editing = !!item
  const editRule = item?.kind === 'auto' ? item.rule : null
  const editTpl = item?.kind === 'manual' ? item.tpl : null

  const triggersByCategory = useMemo(() => {
    const m = new Map<string, AutoMessageTrigger[]>()
    for (const t of triggers) {
      const arr = m.get(triggerCategory(t)) ?? []
      arr.push(t)
      m.set(triggerCategory(t), arr)
    }
    return m
  }, [triggers])

  const firstTrigger = triggers[0]
  const initialTrigger = editRule ? triggers.find((t) => t.key === editRule.trigger) ?? firstTrigger : firstTrigger

  // Rejim: item bo'lsa uning turi; yangi bo'lsa AVTOMATIK (default).
  const [auto, setAuto] = useState(item ? item.kind === 'auto' : true)
  // Umumiy maydonlar
  const [name, setName] = useState(editRule?.name ?? editTpl?.name ?? initialTrigger?.label ?? '')
  const [body, setBody] = useState(editRule?.template ?? editTpl?.text ?? initialTrigger?.defaultTemplate ?? '')
  // Avtomatik maydonlar
  const [category, setCategory] = useState(initialTrigger ? triggerCategory(initialTrigger) : CATEGORY_ORDER[0])
  const [triggerKey, setTriggerKey] = useState(initialTrigger?.key ?? '')
  const [audience, setAudience] = useState(editRule?.audience ?? initialTrigger?.defaultAudience ?? 'parents')
  const [channels, setChannels] = useState<Record<ChannelKey, boolean>>(
    editRule
      ? { sms: editRule.sendSms, telegram: editRule.sendTelegram, push: editRule.sendPush }
      : { sms: true, telegram: false, push: false },
  )
  const [smsProvider, setSmsProvider] = useState<'eskiz' | 'local'>(editRule?.smsProvider === 'local' ? 'local' : 'eskiz')
  const [sendScope, setSendScope] = useState(editRule?.sendScope || sendScopeOptions[0].value)
  const [offsetMinutes, setOffsetMinutes] = useState(editRule?.offsetMinutes ?? 0)
  const [scheduleType, setScheduleType] = useState(editRule?.scheduleType || 'daily')
  const [scheduleTime, setScheduleTime] = useState(editRule?.scheduleTime || '09:00')
  const [scheduleDayOfMonth, setScheduleDayOfMonth] = useState(editRule?.scheduleDayOfMonth ?? 1)
  const [saving, setSaving] = useState(false)
  const [error, setError] = useState('')

  const trigger = triggers.find((t) => t.key === triggerKey)
  const chans = availableChannels(trigger)

  // Matn ostidagi o'zgaruvchilar: avtomatikda hodisa konteksti; qo'ldada o'quvchi+umumiy+hodisa.
  const editorTokens = useMemo(() => {
    const isLead = auto && !!trigger && trigger.audiences.length === 0
    const groups = isLead ? ['lead', 'common', 'event'] : ['student', 'common', 'event']
    const seen = new Set<string>()
    const list = allTokens
      .filter((t) => groups.includes(t.group) && !seen.has(t.token) && seen.add(t.token))
      .map((t) => ({ token: t.token, label: t.label }))
    if (list.length > 0) return list
    return (trigger?.tokens ?? []).map((tk) => ({ token: tk, label: tk }))
  }, [allTokens, trigger, auto])

  const applyTriggerDefaults = (t: AutoMessageTrigger | undefined) => {
    if (!t) return
    setName(t.label)
    setAudience(t.defaultAudience)
    setBody(t.defaultTemplate ?? '')
    const avail = availableChannels(t)
    setChannels({ sms: avail.includes('sms'), telegram: false, push: !avail.includes('sms') && avail.includes('push') })
    setSendScope(sendScopeOptions[0].value)
    setScheduleType('daily')
    setScheduleTime('09:00')
    setScheduleDayOfMonth(1)
  }

  const onCategoryChange = (cat: string) => {
    setCategory(cat)
    const first = (triggersByCategory.get(cat) ?? [])[0]
    if (first) {
      setTriggerKey(first.key)
      applyTriggerDefaults(first)
    }
  }

  const onTriggerChange = (key: string) => {
    setTriggerKey(key)
    applyTriggerDefaults(triggers.find((t) => t.key === key))
  }

  const toggleChannel = (key: ChannelKey) => setChannels((c) => ({ ...c, [key]: !c[key] }))

  const save = async () => {
    if (!name.trim()) {
      setError('Nom kerak')
      return
    }
    setSaving(true)
    setError('')
    try {
      if (auto) {
        if (!trigger) {
          setError('Hodisa tanlang')
          setSaving(false)
          return
        }
        const picked = chans.filter((c) => channels[c])
        if (picked.length === 0) {
          setError('Kamida bitta kanal tanlang')
          setSaving(false)
          return
        }
        if (trigger.tokens.length > 0 && !trigger.templateOptional && !body.trim()) {
          setError('Xabar matni kerak')
          setSaving(false)
          return
        }
        const input: AutoMessageRuleInput = {
          trigger: trigger.key,
          name: name.trim(),
          enabled: editRule?.enabled ?? true,
          sendSms: picked.includes('sms'),
          sendTelegram: picked.includes('telegram'),
          sendPush: picked.includes('push'),
          audience,
          template: body,
          offsetMinutes,
          sendScope: trigger.supportsSendScope ? sendScope : '',
          scheduleType: trigger.supportsSchedule ? scheduleType : '',
          // Vaqt kerak: reja (custom_schedule) YOKI qamrov "not_filled"/"all" (davomat eslatmasi kunlik vaqti).
          scheduleTime: trigger.supportsSchedule || trigger.supportsSendScope ? scheduleTime : '',
          scheduleDayOfMonth,
          smsProvider,
        }
        if (editRule) await updateAutoMessageRule(editRule.id, input)
        else await createAutoMessageRule(input)
      } else {
        // Qo'lda matn (SmsTemplate)
        if (!body.trim()) {
          setError('Xabar matni kerak')
          setSaving(false)
          return
        }
        if (editTpl) await updateSmsTemplate(editTpl.id, { name: name.trim(), text: body.trim() })
        else await createSmsTemplate({ name: name.trim(), text: body.trim() })
      }
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
      title={editing ? 'Xabarni tahrirlash' : 'Yangi xabar'}
      footer={
        <>
          <Button variant="secondary" onClick={onClose} disabled={saving}>
            Bekor qilish
          </Button>
          <Button onClick={save} disabled={saving}>
            {saving ? 'Saqlanmoqda...' : editing ? 'Saqlash' : 'Yaratish'}
          </Button>
        </>
      }
    >
      <div className="space-y-4">
        {/* Rejim: Avtomatik yuborish */}
        <div
          className={cn(
            'flex items-center justify-between rounded-lg border px-3 py-2.5',
            auto ? 'border-violet-200 bg-violet-50' : 'border-slate-200 bg-slate-50',
          )}
        >
          <div>
            <p className="text-sm font-semibold text-slate-700">Avtomatik yuborish</p>
            <p className="text-xs text-slate-500">
              {auto
                ? 'Hodisa yuz berganda (masalan to\'lov kelganda) o\'zi yuboriladi.'
                : 'Faqat tayyor matn — SMS yuborish oynalarida tanlab yuborasiz.'}
            </p>
          </div>
          <button
            type="button"
            role="switch"
            aria-checked={auto}
            disabled={editing}
            onClick={() => setAuto((a) => !a)}
            className={cn(
              'relative h-6 w-11 shrink-0 rounded-full transition-colors disabled:opacity-50',
              auto ? 'bg-violet-500' : 'bg-slate-300',
            )}
            title={editing ? "Yaratilgandan keyin rejim o'zgartirilmaydi" : ''}
          >
            <span
              className={cn(
                'absolute top-0.5 h-5 w-5 rounded-full bg-white shadow transition-transform',
                auto ? 'translate-x-5' : 'translate-x-0.5',
              )}
            />
          </button>
        </div>

        {auto && (
          <>
            <div className="grid grid-cols-1 gap-3 sm:grid-cols-2">
              <Select label="Bo'lim" value={category} onChange={(e) => onCategoryChange(e.target.value)}>
                {CATEGORY_ORDER.filter((c) => (triggersByCategory.get(c) ?? []).length > 0).map((c) => (
                  <option key={c} value={c}>
                    {c}
                  </option>
                ))}
              </Select>
              <Select label="Hodisa" value={triggerKey} onChange={(e) => onTriggerChange(e.target.value)}>
                {(triggersByCategory.get(category) ?? []).map((t) => (
                  <option key={t.key} value={t.key}>
                    {t.label}
                  </option>
                ))}
              </Select>
            </div>
            {trigger && <p className="-mt-1 text-xs text-slate-500">{trigger.description}</p>}
          </>
        )}

        {/* Nom */}
        <Input label="Nom" value={name} onChange={(e) => setName(e.target.value)} placeholder="Xabar nomi" />

        {auto && trigger && trigger.audiences.length > 0 && (
          <Select label="Kimga" value={audience} onChange={(e) => setAudience(e.target.value)}>
            {trigger.audiences.map((a) => (
              <option key={a} value={a}>
                {audienceLabel(a)}
              </option>
            ))}
          </Select>
        )}

        {/* Kanallar — bir nechta tanlash mumkin (avtomatikda) */}
        {auto && (
          <div>
            <p className="mb-1.5 text-sm font-medium text-slate-600">Kanallar (bir nechta tanlash mumkin)</p>
            <div className="flex flex-wrap gap-2">
              {chans.map((key) => {
                const c = CHANNELS[key]
                const Icon = c.icon
                const on = channels[key]
                return (
                  <button
                    key={key}
                    type="button"
                    onClick={() => toggleChannel(key)}
                    className={cn(
                      'inline-flex items-center gap-1.5 rounded-lg border px-3 py-2 text-sm font-medium transition-colors',
                      on ? 'border-brand-400 bg-brand-50 text-brand-700' : 'border-slate-200 text-slate-600 hover:bg-slate-50',
                    )}
                  >
                    <span
                      className={cn(
                        'flex h-4 w-4 items-center justify-center rounded border',
                        on ? 'border-brand-500 bg-brand-500 text-white' : 'border-slate-300',
                      )}
                    >
                      {on && '✓'}
                    </span>
                    <Icon className="h-4 w-4" /> {c.label}
                  </button>
                )
              })}
            </div>
            {chans.length === 1 && chans[0] === 'sms' && (
              <p className="mt-1 text-xs text-slate-400">Bu hodisada faqat SMS mavjud (lidda ilova/telegram yo'q).</p>
            )}
          </div>
        )}

        {auto && channels.sms && chans.includes('sms') && (
          <SmsProviderPicker
            provider={smsProvider}
            onProviderChange={setSmsProvider}
            agentId=""
            onAgentChange={() => {}}
            allowAgentOverride={false}
          />
        )}

        {auto && trigger?.supportsSendScope && (
          <div className="grid grid-cols-2 gap-2">
            <Select label="Qamrov" value={sendScope} onChange={(e) => setSendScope(e.target.value)}>
              {sendScopeOptions.map((o) => (
                <option key={o.value} value={o.value}>
                  {o.label}
                </option>
              ))}
            </Select>
            {sendScope === 'lesson_start' ? (
              <Input
                label="Surilish (daq)"
                type="number"
                value={offsetMinutes}
                onChange={(e) => setOffsetMinutes(Number(e.target.value) || 0)}
              />
            ) : (
              <Input
                label="Yuborish vaqti"
                type="time"
                value={scheduleTime}
                onChange={(e) => setScheduleTime(e.target.value)}
              />
            )}
          </div>
        )}

        {auto && trigger?.supportsSchedule && (
          <div className="grid grid-cols-2 gap-2">
            <Select label="Reja" value={scheduleType} onChange={(e) => setScheduleType(e.target.value)}>
              {scheduleTypeOptions.map((o) => (
                <option key={o.value} value={o.value}>
                  {o.label}
                </option>
              ))}
            </Select>
            <Input label="Vaqt" type="time" value={scheduleTime} onChange={(e) => setScheduleTime(e.target.value)} />
            {scheduleType === 'monthly' && (
              <Input
                label="Oy kuni (1-28)"
                type="number"
                min={1}
                max={28}
                value={scheduleDayOfMonth}
                onChange={(e) => setScheduleDayOfMonth(Number(e.target.value) || 1)}
              />
            )}
          </div>
        )}

        {/* Matn + o'zgaruvchilar */}
        <div>
          <p className="mb-1.5 text-sm font-medium text-slate-600">Xabar matni</p>
          {auto && trigger && trigger.tokens.length === 0 ? (
            <p className="rounded-lg bg-slate-50 px-3 py-2 text-xs text-slate-500">
              Bu hodisa matni tizim tomonidan avtomatik tuziladi — alohida matn shart emas.
            </p>
          ) : (
            <>
              <MessageEditor
                value={body}
                onChange={setBody}
                tokens={editorTokens}
                showSmsCounter={auto ? channels.sms : true}
                rows={4}
                placeholder={
                  auto && trigger?.templateOptional
                    ? "Matn (ixtiyoriy) — o'zgaruvchilar bilan"
                    : "Xabar matni — o'zgaruvchilar bilan"
                }
              />
              {auto && trigger?.templateOptional && (
                <p className="mt-1.5 text-xs text-slate-400">
                  Matn <b>ixtiyoriy</b>: bo'sh qoldirsangiz — tizim batafsil qarz ro'yxatini o'zi tuzadi.
                </p>
              )}
            </>
          )}
        </div>

        {error && <p className="text-sm font-medium text-red-600">{error}</p>}
      </div>
    </Modal>
  )
}
