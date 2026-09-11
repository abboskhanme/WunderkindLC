import { useEffect, useMemo, useRef, useState } from 'react'
import { Send, AlertTriangle, Search, Check } from 'lucide-react'
import type { MessageClass } from '@/types'
import {
  getSmsStatus,
  getPushStatus,
  getTelegramStatus,
  getSmsRecipients,
  getSmsTeacherRecipients,
  getPushRecipients,
  getTelegramTeacherRegistrations,
  sendSms,
  sendPush,
  sendBroadcast,
  watchSmsProgress,
  type SmsProvider,
  type SmsRecipient,
  type SmsTeacherRecipient,
} from '@/api/services/messages'
import type { PushRecipient, TelegramTeacher } from '@/types'
import { Card } from '@/components/ui/Card'
import { Button } from '@/components/ui/Button'
import { Badge } from '@/components/ui/Badge'
import { Input, Select } from '@/components/ui/Input'
import { cn } from '@/lib/utils'
import { CHANNELS, CHANNEL_ORDER, type ChannelKey } from '@/config/channels'
import { MessageEditor, type TokenDef } from '@/components/messaging/MessageEditor'
import { SmsProviderPicker } from '@/components/messaging/SmsProviderPicker'
import { getMessageTokens } from '@/api/services/autoMessages'
import { MessageTemplateLibrary } from './MessageTemplateLibrary'
import posthog from '@/lib/posthog'

/** Kanal — bir vaqtda bir nechtasi tanlanadi. */
type Channel = ChannelKey
/** Auditoriya — bitta tanlanadi. */
type Audience = 'parents' | 'group' | 'students' | 'teachers' | 'selected'

/** Bitta kanalning holati (sozlanganmi). */
interface ChannelState {
  key: Channel
  configured: boolean
  hint: string
}

/** Auditoriya bilan kanal mosligini tekshiradi.
 * - "students" faqat SMS (o'quvchi raqamiga); Push/Telegram uchun o'quvchi kanali yo'q.
 * - qolgan auditoriyalar uchun uchala kanal ham mos. */
function channelAllowed(ch: Channel, audience: Audience): boolean {
  if (audience === 'students') return ch === 'sms'
  return true
}

/** Yagona xabar yuborish oynasi: SMS + Push + Telegram kanallarini birlashtirgan. */
export function UnifiedComposer({
  classes,
  onConfigureAuto,
  canSend = true,
}: {
  classes: MessageClass[]
  onConfigureAuto: (ruleId: string) => void
  /** Ruxsat: "Yuborish" tugmasi ko'rinsinmi (default true) */
  canSend?: boolean
}) {
  // Kanallar holati (sozlangan/yo'q)
  const [smsCfg, setSmsCfg] = useState(false)
  const [localSmsEnabled, setLocalSmsEnabled] = useState(false)
  const [smsFrom, setSmsFrom] = useState('4546')
  const [pushCfg, setPushCfg] = useState(false)
  const [tgCfg, setTgCfg] = useState(false)
  const [statusLoaded, setStatusLoaded] = useState(false)

  // Tanlangan kanallar (multi)
  const [channels, setChannels] = useState<Record<Channel, boolean>>({
    sms: false,
    push: false,
    telegram: false,
  })

  // Auditoriya
  const [audience, setAudience] = useState<Audience>('parents')
  const [groupName, setGroupName] = useState('')
  const [onlyDebtors, setOnlyDebtors] = useState(false)
  const [toParent, setToParent] = useState(true) // "Tanlab" — ota-ona yoki o'quvchi raqamiga (SMS)
  const [smsProvider, setSmsProvider] = useState<SmsProvider>('eskiz')
  const [smsAgentId, setSmsAgentId] = useState('')

  // Matn
  const [title, setTitle] = useState('') // faqat Push uchun
  const [text, setText] = useState('')
  // Tokenlar (serverdan; xato bo'lsa lokal fallback — servisda hal qilinadi)
  const [tokens, setTokens] = useState<TokenDef[]>([])

  // "Tanlab" — oluvchilar
  const [smsRecipients, setSmsRecipients] = useState<SmsRecipient[]>([])
  const [smsTeacherRecipients, setSmsTeacherRecipients] = useState<SmsTeacherRecipient[]>([])
  const [pushRecipients, setPushRecipients] = useState<PushRecipient[]>([])
  const [selStudents, setSelStudents] = useState<Set<string>>(new Set())
  const [selTeachers, setSelTeachers] = useState<Set<string>>(new Set())
  const [selPushUsers, setSelPushUsers] = useState<Set<string>>(new Set())
  const [search, setSearch] = useState('')
  const sttLoaded = useRef(false)
  const pushLoaded = useRef(false)

  // Yuborish
  const [sending, setSending] = useState(false)
  const [results, setResults] = useState<{ channel: string; ok: boolean; text: string }[]>([])
  /** Fonda ketayotgan SMS partiyasi kuzatuvini to'xtatuvchi (sahifadan chiqilganda). */
  const stopSmsWatch = useRef<(() => void) | null>(null)
  useEffect(() => () => stopSmsWatch.current?.(), [])

  useEffect(() => {
    Promise.all([getSmsStatus(), getPushStatus(), getTelegramStatus()])
      .then(([s, p, t]) => {
        setSmsCfg(s.configured)
        setLocalSmsEnabled(s.localEnabled)
        setSmsFrom(s.from)
        setPushCfg(p.configured)
        setTgCfg(t.configured)
        // Standart: birinchi sozlangan kanalni yoqamiz
        setChannels({ sms: s.configured || s.localEnabled, push: false, telegram: false })
        // Eskiz sozlanmagan, lekin Local SMS yoqilgan bo'lsa — standart provayder Local.
        if (!s.configured && s.localEnabled) setSmsProvider('local')
      })
      .finally(() => setStatusLoaded(true))
  }, [])

  // Token katalogi (lid tokenlari bu oynaga taalluqli emas)
  useEffect(() => {
    getMessageTokens()
      .then((ts) => setTokens(ts.filter((t) => t.group !== 'lead')))
      .catch(() => setTokens([]))
  }, [])

  // Kanal kartalari — yagona tartibda (config/channels.ts)
  const channelHints: Record<Channel, { configured: boolean; hint: string }> = {
    sms: {
      configured: smsCfg || localSmsEnabled,
      hint: smsCfg ? `Jo'natuvchi: ${smsFrom}` : 'Local Call agent telefonidan',
    },
    telegram: { configured: tgCfg, hint: 'Bot orqali ota-onalarga' },
    push: { configured: pushCfg, hint: 'Ilovaga bildirishnoma' },
  }
  const channelStates: ChannelState[] = CHANNEL_ORDER.map((k) => ({
    key: k,
    configured: channelHints[k].configured,
    hint: channelHints[k].hint,
  }))

  // "Tanlab" rejimida picker turi: faqat Push yoqilgan bo'lsa akkaunt-picker, aks holda o'quvchi/o'qituvchi.
  const pushOnlySelected = channels.push && !channels.sms && !channels.telegram
  const pickerMode: 'push' | 'stt' = pushOnlySelected ? 'push' : 'stt'

  // Kanal toggle. "Tanlab" auditoriyada Push va SMS/Telegram bir vaqtda ishlamaydi
  // (oluvchilar boshqacha aniqlanadi) — o'zaro istisno qilamiz.
  const toggleChannel = (ch: Channel) => {
    if (!channelStates.find((c) => c.key === ch)?.configured) return
    if (!channelAllowed(ch, audience)) return
    setChannels((prev) => {
      const next = { ...prev, [ch]: !prev[ch] }
      if (audience === 'selected' && next[ch]) {
        if (ch === 'push') {
          next.sms = false
          next.telegram = false
        } else {
          next.push = false
        }
      }
      return next
    })
  }

  // Auditoriya o'zgarsa — mos bo'lmagan kanallarni o'chiramiz.
  const changeAudience = (a: Audience) => {
    setAudience(a)
    setChannels((prev) => {
      const next = {
        sms: prev.sms && channelAllowed('sms', a),
        push: prev.push && channelAllowed('push', a),
        telegram: prev.telegram && channelAllowed('telegram', a),
      }
      // "Tanlab"da Push va SMS/Telegram birga ishlamaydi — SMS/Telegram ustuvor.
      if (a === 'selected' && next.push && (next.sms || next.telegram)) next.push = false
      return next
    })
  }

  // "Tanlab" oluvchilarini lazy yuklash.
  useEffect(() => {
    if (audience !== 'selected') return
    if (pickerMode === 'stt' && !sttLoaded.current) {
      sttLoaded.current = true
      getSmsRecipients().then(setSmsRecipients).catch(() => setSmsRecipients([]))
      getSmsTeacherRecipients().then(setSmsTeacherRecipients).catch(() => setSmsTeacherRecipients([]))
    }
    if (pickerMode === 'push' && !pushLoaded.current) {
      pushLoaded.current = true
      getPushRecipients().then(setPushRecipients).catch(() => setPushRecipients([]))
    }
  }, [audience, pickerMode])

  const anyChannel = channels.sms || channels.push || channels.telegram

  const applyTemplate = (tpl: string, name: string) => {
    setText(tpl)
    if (channels.push && !title.trim()) setTitle(name)
  }

  const toggleSet = (setter: React.Dispatch<React.SetStateAction<Set<string>>>, id: string) =>
    setter((prev) => {
      const next = new Set(prev)
      if (next.has(id)) next.delete(id)
      else next.add(id)
      return next
    })

  const filteredSms = useMemo(() => {
    const q = search.trim().toLowerCase()
    if (!q) return smsRecipients
    return smsRecipients.filter(
      (r) => r.fullName.toLowerCase().includes(q) || r.className.toLowerCase().includes(q),
    )
  }, [smsRecipients, search])

  const filteredSmsTeachers = useMemo(() => {
    const q = search.trim().toLowerCase()
    if (!q) return smsTeacherRecipients
    return smsTeacherRecipients.filter((t) => t.fullName.toLowerCase().includes(q))
  }, [smsTeacherRecipients, search])

  const filteredPush = useMemo(() => {
    const q = search.trim().toLowerCase()
    if (!q) return pushRecipients
    return pushRecipients.filter(
      (r) => r.name.toLowerCase().includes(q) || r.detail.toLowerCase().includes(q),
    )
  }, [pushRecipients, search])

  const selectedCount =
    pickerMode === 'push' ? selPushUsers.size : selStudents.size + selTeachers.size

  async function handleSend() {
    if (!anyChannel || !text.trim() || sending) return
    if (audience === 'group' && !groupName) {
      setResults([{ channel: '', ok: false, text: 'Guruh tanlang.' }])
      return
    }
    if (audience === 'selected' && selectedCount === 0) {
      setResults([{ channel: '', ok: false, text: 'Hech kim tanlanmadi.' }])
      return
    }
    setSending(true)
    setResults([])
    const out: { channel: string; ok: boolean; text: string }[] = []
    /** Fonda ketayotgan SMS partiyasi (bo'lsa) — natija chizilgandan keyin kuzatiladi. */
    let queuedSmsBatchId: string | null = null

    // --- SMS ---
    if (channels.sms) {
      try {
        const b = await sendSms({
          audience:
            audience === 'parents' || audience === 'group'
              ? 'parents'
              : audience === 'students'
                ? 'students'
                : audience === 'teachers'
                  ? 'teachers'
                  : 'selected',
          className: audience === 'group' ? groupName : undefined,
          onlyDebtors: (audience === 'parents' || audience === 'group' || audience === 'students') && onlyDebtors,
          studentIds: audience === 'selected' ? [...selStudents] : undefined,
          teacherIds: audience === 'selected' ? [...selTeachers] : undefined,
          toParent: audience === 'selected' ? toParent : undefined,
          text: text.trim(),
          provider: smsProvider,
          agentId: smsAgentId || undefined,
        })
        if (b.queued) {
          // Ko'p oluvchi — SMS'lar FONDA ketmoqda (so'rov ichida kutilsa proksi uzib qo'yardi).
          out.push({ channel: 'SMS', ok: true, text: `yuborilmoqda 0/${b.recipientCount}` })
          queuedSmsBatchId = b.id
        } else {
          out.push({ channel: 'SMS', ok: true, text: `yuborildi ${b.sentCount}/${b.recipientCount}` })
        }
      } catch (e) {
        out.push({ channel: 'SMS', ok: false, text: errMsg(e) })
      }
    }

    // --- Push ---
    if (channels.push) {
      try {
        const p = await sendPush({
          audience:
            audience === 'teachers' ? 'teachers' : audience === 'selected' ? 'selected' : 'parents',
          className: audience === 'group' ? groupName : undefined,
          userIds: audience === 'selected' ? [...selPushUsers] : undefined,
          title: title.trim(),
          body: text.trim(),
        })
        out.push({
          channel: 'Push',
          ok: true,
          text:
            p.recipientCount === 0
              ? 'qurilma topilmadi (0)'
              : `yuborildi ${p.sentCount}/${p.recipientCount}`,
        })
      } catch (e) {
        out.push({ channel: 'Push', ok: false, text: errMsg(e) })
      }
    }

    // --- Telegram ---
    if (channels.telegram) {
      try {
        let teacherIds: string[] | undefined
        let scope: 'class' | 'all' | 'selected'
        if (audience === 'group') scope = 'class'
        else if (audience === 'selected') scope = 'selected'
        else if (audience === 'teachers') {
          // "Barcha o'qituvchilar" — ro'yxatdagi barcha o'qituvchi id'lari orqali
          scope = 'selected'
          const tt: TelegramTeacher[] = await getTelegramTeacherRegistrations()
          teacherIds = tt.map((t) => t.teacherId)
        } else scope = 'all'

        const b = await sendBroadcast({
          scope,
          className: audience === 'group' ? groupName : undefined,
          onlyDebtors: (audience === 'parents' || audience === 'group') && onlyDebtors,
          studentIds: audience === 'selected' ? [...selStudents] : undefined,
          teacherIds:
            audience === 'selected' ? [...selTeachers] : audience === 'teachers' ? teacherIds : undefined,
          text: text.trim(),
        })
        out.push({
          channel: 'Telegram',
          ok: true,
          text:
            b.recipientCount === 0
              ? "ro'yxatda oluvchi topilmadi (0)"
              : `yuborildi ${b.sentCount}/${b.recipientCount}`,
        })
      } catch (e) {
        out.push({ channel: 'Telegram', ok: false, text: errMsg(e) })
      }
    }

    setResults(out)
    if (queuedSmsBatchId) {
      stopSmsWatch.current?.()
      stopSmsWatch.current = watchSmsProgress(queuedSmsBatchId, (p) =>
        setResults((prev) =>
          prev.map((r) =>
            r.channel === 'SMS'
              ? { ...r, text: p.finished ? `yuborildi ${p.sent}/${p.total}` : `yuborilmoqda ${p.done}/${p.total}` }
              : r,
          ),
        ),
      )
    }
    const successfulChannels = out.filter((result) => result.ok).map((result) => result.channel.toLowerCase())
    if (successfulChannels.length > 0) {
      posthog.capture('message_campaign_sent', {
        audience,
        channels: successfulChannels,
        only_debtors: onlyDebtors,
        recipient_selection: audience === 'selected' ? 'selected' : 'all_matching',
      })
    }
    if (out.every((r) => r.ok)) {
      setText('')
      setTitle('')
    }
    setSending(false)
  }

  if (!statusLoaded) return null

  const noneConfigured = !smsCfg && !localSmsEnabled && !pushCfg && !tgCfg
  const showDebtors = audience === 'parents' || audience === 'group' || audience === 'students'

  return (
    <div className="space-y-4">
      {noneConfigured && (
        <div className="flex items-start gap-2 rounded-xl border border-amber-200 bg-amber-50 px-4 py-3 text-sm text-amber-800">
          <AlertTriangle className="mt-0.5 h-4 w-4 shrink-0" />
          <p>
            Hech bir xabar kanali sozlanmagan. Yuborish uchun "Sozlamalar → Xabar kanallari" bo'limida
            SMS (Eskiz), Push (Firebase) yoki Telegram botni sozlang.
          </p>
        </div>
      )}

      {/* 1. Kanallar */}
      <Card title="Kanallar" sub="Bir yoki bir nechta kanal tanlang">
        <div className="grid grid-cols-1 gap-3 sm:grid-cols-3">
          {channelStates.map((c) => {
            const meta = CHANNELS[c.key]
            const active = channels[c.key]
            const allowed = channelAllowed(c.key, audience)
            const disabled = !c.configured || !allowed
            const Icon = meta.icon
            return (
              <button
                key={c.key}
                type="button"
                disabled={disabled}
                onClick={() => toggleChannel(c.key)}
                className={cn(
                  'flex items-start gap-3 rounded-xl border p-3 text-left transition-colors',
                  disabled
                    ? 'cursor-not-allowed border-slate-100 bg-slate-50 opacity-60'
                    : active
                      ? 'border-brand-400 bg-brand-50'
                      : 'border-slate-200 hover:border-slate-300 hover:bg-slate-50',
                )}
              >
                <span
                  className={cn(
                    'flex h-9 w-9 shrink-0 items-center justify-center rounded-lg',
                    active ? 'bg-brand-600 text-white' : 'bg-slate-100 text-slate-500',
                  )}
                >
                  <Icon className="h-4.5 w-4.5" />
                </span>
                <span className="min-w-0 flex-1">
                  <span className="flex items-center gap-1.5">
                    <span className="text-sm font-semibold text-slate-800">{meta.label}</span>
                    <span className="text-xs font-normal text-slate-400">· {meta.sub}</span>
                    {active && <Check className="h-3.5 w-3.5 text-brand-600" />}
                  </span>
                  <span className="mt-0.5 block truncate text-xs text-slate-400">
                    {!c.configured
                      ? 'Sozlanmagan — Sozlamalar → Xabar kanallari'
                      : !allowed
                        ? 'Bu auditoriya uchun mavjud emas'
                        : c.hint}
                  </span>
                </span>
              </button>
            )
          })}
        </div>
      </Card>

      {/* 2. Auditoriya */}
      <Card
        title="Kimga yuborish"
        actions={
          audience === 'selected' ? (
            <Badge tone="violet">
              <span className="font-mono">{selectedCount}</span> ta tanlandi
            </Badge>
          ) : undefined
        }
      >
        <div className="flex flex-wrap items-center gap-2">
          <div className="tabs">
            <AudBtn active={audience === 'parents'} onClick={() => changeAudience('parents')}>
              Barcha ota-onalar
            </AudBtn>
            <AudBtn active={audience === 'group'} onClick={() => changeAudience('group')}>
              Guruh
            </AudBtn>
            <AudBtn active={audience === 'students'} onClick={() => changeAudience('students')}>
              O'quvchilar
            </AudBtn>
            <AudBtn active={audience === 'teachers'} onClick={() => changeAudience('teachers')}>
              O'qituvchilar
            </AudBtn>
            <AudBtn active={audience === 'selected'} onClick={() => changeAudience('selected')}>
              Tanlab
            </AudBtn>
          </div>

          {audience === 'group' && (
            <Select value={groupName} onChange={(e) => setGroupName(e.target.value)} className="w-auto">
              <option value="">Guruhni tanlang</option>
              {classes.map((c) => (
                <option key={c.name} value={c.name}>
                  {c.name}
                </option>
              ))}
            </Select>
          )}

          {showDebtors && (
            <label className="inline-flex cursor-pointer items-center gap-2 rounded-lg border border-slate-200 px-3 py-1.5 text-sm text-slate-600 transition-colors hover:bg-slate-50">
              <input
                type="checkbox"
                checked={onlyDebtors}
                onChange={(e) => setOnlyDebtors(e.target.checked)}
                className="h-4 w-4 accent-brand-600"
              />
              Faqat qarzdorlar
            </label>
          )}

          {audience === 'selected' && pickerMode === 'stt' && (
            <div className="inline-flex items-center gap-2">
              <span className="text-sm text-slate-500">Raqam:</span>
              <div className="tabs">
                <button
                  type="button"
                  onClick={() => setToParent(true)}
                  className={cn('tab', toParent && 'active')}
                >
                  Ota-ona
                </button>
                <button
                  type="button"
                  onClick={() => setToParent(false)}
                  className={cn('tab', !toParent && 'active')}
                >
                  O'quvchi
                </button>
              </div>
            </div>
          )}
        </div>

        {audience === 'selected' && channels.push && pickerMode === 'push' && (
          <p className="mt-2 text-xs text-slate-400">
            Push "Tanlab" rejimida SMS/Telegram bilan birga ishlamaydi (oluvchilar boshqacha aniqlanadi).
          </p>
        )}

        {channels.sms && (
          <SmsProviderPicker
            provider={smsProvider}
            onProviderChange={setSmsProvider}
            agentId={smsAgentId}
            onAgentChange={setSmsAgentId}
            className="mt-3"
          />
        )}
      </Card>

      <MessageTemplateLibrary onPick={applyTemplate} onConfigureAuto={onConfigureAuto} currentText={text} />

      <div className="grid grid-cols-1 gap-4 lg:grid-cols-2">
        {/* 3. Matn */}
        <Card
          title="Xabar matni"
          bodyClassName="space-y-3"
          actions={
            text || title ? (
              <button
                type="button"
                onClick={() => {
                  setText('')
                  setTitle('')
                }}
                className="rounded-full border border-slate-200 px-2.5 py-1 text-xs text-slate-400 transition-colors hover:bg-slate-50"
              >
                Tozalash
              </button>
            ) : undefined
          }
        >
          {channels.push && (
            <Input
              label="Sarlavha (Push)"
              value={title}
              onChange={(e) => setTitle(e.target.value)}
              placeholder="masalan: Yig'ilish"
            />
          )}

          <MessageEditor
            value={text}
            onChange={setText}
            tokens={tokens}
            showSmsCounter={channels.sms}
            rows={5}
            placeholder="Xabar matni — o'rinbosarlar har o'quvchiga moslab to'ldiriladi"
            hint="O'rinbosarlar har o'quvchiga moslab to'ldiriladi. O'qituvchilarga faqat {fish} ishlaydi."
          />

          {results.length > 0 && (
            <div className="space-y-1 rounded-lg border border-slate-100 bg-slate-50 p-2.5">
              {results.map((r, i) => (
                <div key={i} className="flex items-center gap-2 text-sm">
                  {r.channel && (
                    <Badge tone={r.ok ? 'green' : 'red'}>{r.channel}</Badge>
                  )}
                  <span className={cn('font-medium', r.ok ? 'text-emerald-700' : 'text-red-600')}>
                    {r.text}
                  </span>
                </div>
              ))}
            </div>
          )}

          {canSend && (
            <div className="flex items-center justify-end">
              <Button onClick={handleSend} disabled={!anyChannel || !text.trim() || sending}>
                <Send className="h-4 w-4" /> {sending ? 'Yuborilmoqda...' : 'Yuborish'}
              </Button>
            </div>
          )}
        </Card>

        {/* 4. "Tanlab" — oluvchilar */}
        {audience === 'selected' ? (
          <Card title="Oluvchilarni tanlang">
            <div className="relative mb-2">
              <Search className="pointer-events-none absolute left-3 top-2.5 h-4 w-4 text-slate-400" />
              <input
                value={search}
                onChange={(e) => setSearch(e.target.value)}
                placeholder={
                  pickerMode === 'push' ? 'Ism yoki guruh...' : "O'quvchi, o'qituvchi yoki guruh..."
                }
                className="w-full rounded-lg border border-slate-200 py-2 pl-9 pr-3 text-sm outline-none transition-colors focus:border-brand-400 focus:ring-2 focus:ring-brand-100"
              />
            </div>
            <div className="max-h-[26rem] space-y-1 overflow-y-auto">
              {pickerMode === 'stt' ? (
                <>
                  {filteredSms.length === 0 && filteredSmsTeachers.length === 0 && (
                    <p className="py-6 text-center text-sm text-slate-400">Oluvchi topilmadi</p>
                  )}
                  {filteredSms.map((r) => {
                    const active = selStudents.has(r.studentId)
                    const phone = toParent ? r.parentPhone : r.studentPhone
                    const noPhone = channels.sms && !phone
                    return (
                      <PickRow
                        key={r.studentId}
                        active={active}
                        disabled={noPhone}
                        onClick={() => toggleSet(setSelStudents, r.studentId)}
                        title={r.fullName}
                        detail={`${r.className || '—'}${phone ? ` · ${phone}` : ''}`}
                        badge={noPhone ? <Badge tone="amber">raqam yo'q</Badge> : undefined}
                      />
                    )
                  })}
                  {filteredSmsTeachers.map((t) => {
                    const active = selTeachers.has(t.teacherId)
                    const noPhone = channels.sms && !t.phone
                    return (
                      <PickRow
                        key={t.teacherId}
                        active={active}
                        disabled={noPhone}
                        onClick={() => toggleSet(setSelTeachers, t.teacherId)}
                        title={t.fullName}
                        detail={t.phone ?? ''}
                        badge={
                          <>
                            <Badge tone="blue">O'qituvchi</Badge>
                            {noPhone && <Badge tone="amber">raqam yo'q</Badge>}
                          </>
                        }
                      />
                    )
                  })}
                </>
              ) : (
                <>
                  {filteredPush.length === 0 && (
                    <p className="py-6 text-center text-sm text-slate-400">Oluvchi topilmadi</p>
                  )}
                  {filteredPush.map((r) => (
                    <PickRow
                      key={r.userId}
                      active={selPushUsers.has(r.userId)}
                      onClick={() => toggleSet(setSelPushUsers, r.userId)}
                      title={r.name}
                      detail={`${r.group}${r.detail ? ` · ${r.detail}` : ''}`}
                      badge={!r.hasDevice ? <Badge tone="amber">qurilma yo'q</Badge> : undefined}
                    />
                  ))}
                </>
              )}
            </div>
            {selectedCount > 0 && (
              <button
                type="button"
                onClick={() => {
                  setSelStudents(new Set())
                  setSelTeachers(new Set())
                  setSelPushUsers(new Set())
                }}
                className="mt-2 text-xs text-slate-500 hover:text-slate-700"
              >
                Tanlovni tozalash ({selectedCount})
              </button>
            )}
          </Card>
        ) : (
          <Card title="Eslatma" bodyClassName="text-sm text-slate-500 space-y-2">
            <p>
              Tanlangan kanallar bo'yicha xabar yuboriladi. Har kanal o'z oluvchilariga (SMS —
              raqamga, Push — ilova qurilmasiga, Telegram — botga yozilganlarga) yetkaziladi.
            </p>
            <p className="text-xs text-slate-400">
              Maslahat: bir vaqtda bir nechta kanalni yoqib, bir marta yozib bir necha usulda
              yuborishingiz mumkin.
            </p>
          </Card>
        )}
      </div>

    </div>
  )
}

/** Xato xabarini axios javobidan ajratadi. */
function errMsg(e: unknown): string {
  return (
    (e as { response?: { data?: { message?: string } } })?.response?.data?.message ??
    'Yuborishda xatolik'
  )
}

function AudBtn({
  active,
  onClick,
  children,
}: {
  active: boolean
  onClick: () => void
  children: React.ReactNode
}) {
  return (
    <button type="button" onClick={onClick} className={cn('tab', active && 'active')}>
      {children}
    </button>
  )
}

/** "Tanlab" ro'yxatidagi bitta qator (checkbox + ism + izoh + badge). */
function PickRow({
  active,
  disabled,
  onClick,
  title,
  detail,
  badge,
}: {
  active: boolean
  disabled?: boolean
  onClick: () => void
  title: string
  detail: string
  badge?: React.ReactNode
}) {
  return (
    <button
      type="button"
      disabled={disabled}
      onClick={onClick}
      className={cn(
        'flex w-full items-center gap-2 rounded-lg border px-3 py-2 text-left text-sm transition-colors',
        disabled
          ? 'cursor-not-allowed border-slate-100 bg-slate-50 opacity-60'
          : active
            ? 'border-brand-300 bg-brand-50'
            : 'border-slate-100 hover:bg-slate-50',
      )}
    >
      <span
        className={cn(
          'flex h-4 w-4 shrink-0 items-center justify-center rounded border',
          active && !disabled ? 'border-brand-500 bg-brand-500 text-white' : 'border-slate-300',
        )}
      >
        {active && !disabled && <Check className="h-3 w-3" />}
      </span>
      <span className="min-w-0 flex-1 truncate">
        <span className={cn('font-medium', disabled ? 'text-slate-400' : 'text-slate-700')}>
          {title}
        </span>
        {detail && <span className="ml-1 text-xs text-slate-400">· {detail}</span>}
      </span>
      {badge}
    </button>
  )
}
