import { useEffect, useMemo, useRef, useState } from 'react'
import { Send, AlertTriangle, Search, Check, MessageCircle } from 'lucide-react'
import type { MessageClass } from '@/types'
import {
  getTelegramStatus,
  getMessageRecipients,
  getMessageTeacherRecipients,
  getTelegramTeacherRegistrations,
  sendBroadcast,
  type MessageRecipient,
  type MessageTeacherRecipient,
} from '@/api/services/messages'
import type { TelegramTeacher } from '@/types'
import { Card } from '@/components/ui/Card'
import { Button } from '@/components/ui/Button'
import { Badge } from '@/components/ui/Badge'
import { Select } from '@/components/ui/Input'
import { cn } from '@/lib/utils'
import { MessageEditor, type TokenDef } from '@/components/messaging/MessageEditor'
import { getMessageTokens } from '@/api/services/autoMessages'
import { MessageTemplateLibrary } from './MessageTemplateLibrary'
import posthog from '@/lib/posthog'

/** Auditoriya — bitta tanlanadi. */
type Audience = 'parents' | 'group' | 'teachers' | 'selected'

/**
 * Xabar yuborish oynasi — markazning YAGONA tashqi kanali: Telegram bot.
 * SMS kanali va push 2026-10 da olib tashlandi. Xabar faqat botga ulangan
 * oilalar (va o'qituvchilar) ga yetadi — "Tanlab" ro'yxatida ulanmaganlar belgilanadi.
 */
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
  const [tgCfg, setTgCfg] = useState(false)
  const [statusLoaded, setStatusLoaded] = useState(false)

  // Auditoriya
  const [audience, setAudience] = useState<Audience>('parents')
  const [groupName, setGroupName] = useState('')
  const [onlyDebtors, setOnlyDebtors] = useState(false)

  // Matn
  const [text, setText] = useState('')
  // Tokenlar (serverdan; xato bo'lsa lokal fallback — servisda hal qilinadi)
  const [tokens, setTokens] = useState<TokenDef[]>([])

  // "Tanlab" — oluvchilar
  const [recipients, setRecipients] = useState<MessageRecipient[]>([])
  const [teacherRecipients, setTeacherRecipients] = useState<MessageTeacherRecipient[]>([])
  const [selStudents, setSelStudents] = useState<Set<string>>(new Set())
  const [selTeachers, setSelTeachers] = useState<Set<string>>(new Set())
  const [search, setSearch] = useState('')
  const pickerLoaded = useRef(false)

  // Yuborish
  const [sending, setSending] = useState(false)
  const [result, setResult] = useState<{ ok: boolean; text: string } | null>(null)

  useEffect(() => {
    getTelegramStatus()
      .then((t) => setTgCfg(t.configured))
      // Fon o'qish: holat olinmasa ogohlantirish banneri chiqadi (bot sozlanmagan deb ko'rsatiladi).
      .catch(() => setTgCfg(false))
      .finally(() => setStatusLoaded(true))
  }, [])

  // Token katalogi (lid tokenlari bu oynaga taalluqli emas)
  useEffect(() => {
    getMessageTokens()
      .then((ts) => setTokens(ts.filter((t) => t.group !== 'lead')))
      .catch(() => setTokens([]))
  }, [])

  // "Tanlab" oluvchilarini lazy yuklash.
  useEffect(() => {
    if (audience !== 'selected' || pickerLoaded.current) return
    pickerLoaded.current = true
    getMessageRecipients().then(setRecipients).catch(() => setRecipients([]))
    getMessageTeacherRecipients().then(setTeacherRecipients).catch(() => setTeacherRecipients([]))
  }, [audience])

  const applyTemplate = (tpl: string) => setText(tpl)

  const toggleSet = (setter: React.Dispatch<React.SetStateAction<Set<string>>>, id: string) =>
    setter((prev) => {
      const next = new Set(prev)
      if (next.has(id)) next.delete(id)
      else next.add(id)
      return next
    })

  const filteredStudents = useMemo(() => {
    const q = search.trim().toLowerCase()
    if (!q) return recipients
    return recipients.filter(
      (r) => r.fullName.toLowerCase().includes(q) || r.className.toLowerCase().includes(q),
    )
  }, [recipients, search])

  const filteredTeachers = useMemo(() => {
    const q = search.trim().toLowerCase()
    if (!q) return teacherRecipients
    return teacherRecipients.filter((t) => t.fullName.toLowerCase().includes(q))
  }, [teacherRecipients, search])

  const selectedCount = selStudents.size + selTeachers.size

  async function handleSend() {
    if (!text.trim() || sending) return
    if (audience === 'group' && !groupName) {
      setResult({ ok: false, text: 'Guruh tanlang.' })
      return
    }
    if (audience === 'selected' && selectedCount === 0) {
      setResult({ ok: false, text: 'Hech kim tanlanmadi.' })
      return
    }
    setSending(true)
    setResult(null)
    try {
      let teacherIds: string[] | undefined
      let scope: 'class' | 'all' | 'selected'
      if (audience === 'group') scope = 'class'
      else if (audience === 'selected') scope = 'selected'
      else if (audience === 'teachers') {
        // "Barcha o'qituvchilar" — botga ulangan barcha o'qituvchi id'lari orqali.
        scope = 'selected'
        const tt: TelegramTeacher[] = await getTelegramTeacherRegistrations()
        teacherIds = tt.map((t) => t.teacherId)
        if (teacherIds.length === 0) {
          setResult({ ok: false, text: "Botga ulangan o'qituvchi yo'q (0)" })
          return
        }
      } else scope = 'all'

      const b = await sendBroadcast({
        scope,
        className: audience === 'group' ? groupName : undefined,
        onlyDebtors: (audience === 'parents' || audience === 'group') && onlyDebtors,
        studentIds: audience === 'selected' ? [...selStudents] : undefined,
        teacherIds: audience === 'selected' ? [...selTeachers] : audience === 'teachers' ? teacherIds : undefined,
        text: text.trim(),
      })
      setResult({
        ok: true,
        text:
          b.recipientCount === 0
            ? "botga ulangan oluvchi topilmadi (0)"
            : `yuborildi ${b.sentCount}/${b.recipientCount}`,
      })
      posthog.capture('message_campaign_sent', {
        audience,
        channels: ['telegram'],
        only_debtors: onlyDebtors,
        recipient_selection: audience === 'selected' ? 'selected' : 'all_matching',
      })
      setText('')
    } catch (e) {
      setResult({ ok: false, text: errMsg(e) })
    } finally {
      setSending(false)
    }
  }

  if (!statusLoaded) return null

  const showDebtors = audience === 'parents' || audience === 'group'

  return (
    <div className="space-y-4">
      {!tgCfg && (
        <div className="flex items-start gap-2 rounded-xl border border-amber-200 bg-amber-50 px-4 py-3 text-sm text-amber-800">
          <AlertTriangle className="mt-0.5 h-4 w-4 shrink-0" />
          <p>
            Telegram bot sozlanmagan. Xabar yuborish uchun "Sozlamalar → Sotuv va marketing → Xabar kanallari"
            bo'limida botni sozlang.
          </p>
        </div>
      )}

      {/* 1. Auditoriya */}
      <Card
        title="Kimga yuborish"
        sub="Xabar Telegram bot orqali — faqat botga ulangan oilalar va o'qituvchilarga boradi"
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
            <AudBtn active={audience === 'parents'} onClick={() => setAudience('parents')}>
              Barcha ota-onalar
            </AudBtn>
            <AudBtn active={audience === 'group'} onClick={() => setAudience('group')}>
              Guruh
            </AudBtn>
            <AudBtn active={audience === 'teachers'} onClick={() => setAudience('teachers')}>
              O'qituvchilar
            </AudBtn>
            <AudBtn active={audience === 'selected'} onClick={() => setAudience('selected')}>
              Tanlab
            </AudBtn>
          </div>

          {audience === 'group' && (
            <Select value={groupName} onChange={(e) => setGroupName(e.target.value)} className="w-auto">
              <option value="">Guruhni tanlang</option>
              {classes.map((c) => (
                <option key={c.name} value={c.name}>
                  {c.name} ({c.parentCount} ta Telegram)
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
        </div>
      </Card>

      <MessageTemplateLibrary onPick={applyTemplate} onConfigureAuto={onConfigureAuto} currentText={text} />

      <div className="grid grid-cols-1 gap-4 lg:grid-cols-2">
        {/* 2. Matn */}
        <Card
          title="Xabar matni"
          bodyClassName="space-y-3"
          actions={
            text ? (
              <button
                type="button"
                onClick={() => setText('')}
                className="rounded-full border border-slate-200 px-2.5 py-1 text-xs text-slate-400 transition-colors hover:bg-slate-50"
              >
                Tozalash
              </button>
            ) : undefined
          }
        >
          <MessageEditor
            value={text}
            onChange={setText}
            tokens={tokens}
            rows={5}
            placeholder="Xabar matni — o'rinbosarlar har o'quvchiga moslab to'ldiriladi"
            hint="O'rinbosarlar har o'quvchiga moslab to'ldiriladi. O'qituvchilarga faqat {fish} ishlaydi."
          />

          {result && (
            <div className="flex items-center gap-2 rounded-lg border border-slate-100 bg-slate-50 p-2.5 text-sm">
              <Badge tone={result.ok ? 'green' : 'red'}>Telegram</Badge>
              <span className={cn('font-medium', result.ok ? 'text-emerald-700' : 'text-red-600')}>
                {result.text}
              </span>
            </div>
          )}

          {canSend && (
            <div className="flex items-center justify-end">
              <Button onClick={handleSend} disabled={!tgCfg || !text.trim() || sending}>
                <Send className="h-4 w-4" /> {sending ? 'Yuborilmoqda...' : 'Yuborish'}
              </Button>
            </div>
          )}
        </Card>

        {/* 3. "Tanlab" — oluvchilar */}
        {audience === 'selected' ? (
          <Card title="Oluvchilarni tanlang">
            <div className="relative mb-2">
              <Search className="pointer-events-none absolute left-3 top-2.5 h-4 w-4 text-slate-400" />
              <input
                value={search}
                onChange={(e) => setSearch(e.target.value)}
                placeholder="O'quvchi, o'qituvchi yoki guruh..."
                className="w-full rounded-lg border border-slate-200 py-2 pl-9 pr-3 text-sm outline-none transition-colors focus:border-brand-400 focus:ring-2 focus:ring-brand-100"
              />
            </div>
            <div className="max-h-[26rem] space-y-1 overflow-y-auto">
              {filteredStudents.length === 0 && filteredTeachers.length === 0 && (
                <p className="py-6 text-center text-sm text-slate-400">Oluvchi topilmadi</p>
              )}
              {filteredStudents.map((r) => (
                <PickRow
                  key={r.studentId}
                  active={selStudents.has(r.studentId)}
                  onClick={() => toggleSet(setSelStudents, r.studentId)}
                  title={r.fullName}
                  detail={r.className || '—'}
                  badge={r.telegramChats === 0 ? <Badge tone="amber">Telegram ulanmagan</Badge> : undefined}
                />
              ))}
              {filteredTeachers.map((t) => (
                <PickRow
                  key={t.teacherId}
                  active={selTeachers.has(t.teacherId)}
                  onClick={() => toggleSet(setSelTeachers, t.teacherId)}
                  title={t.fullName}
                  detail=""
                  badge={
                    <>
                      <Badge tone="blue">O'qituvchi</Badge>
                      {t.telegramChats === 0 && <Badge tone="amber">Telegram ulanmagan</Badge>}
                    </>
                  }
                />
              ))}
            </div>
            {selectedCount > 0 && (
              <button
                type="button"
                onClick={() => {
                  setSelStudents(new Set())
                  setSelTeachers(new Set())
                }}
                className="mt-2 text-xs text-slate-500 hover:text-slate-700"
              >
                Tanlovni tozalash ({selectedCount})
              </button>
            )}
          </Card>
        ) : (
          <Card title="Eslatma" bodyClassName="text-sm text-slate-500 space-y-2">
            <p className="flex items-start gap-2">
              <MessageCircle className="mt-0.5 h-4 w-4 shrink-0 text-sky-500" />
              Xabar Telegram bot orqali yuboriladi — faqat botga ulangan (telefon raqamini yuborgan) oilalar
              va o'qituvchilar oladi.
            </p>
            <p className="text-xs text-slate-400">
              Avtomatik xabarlar (to'lov, davomat, tug'ilgan kun ...) «Xabar yaratish» bo'limida sozlanadi va
              kabinetdagi bildirishnomalarga ham yoziladi.
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
