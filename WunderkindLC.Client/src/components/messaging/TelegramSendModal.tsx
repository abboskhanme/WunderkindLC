import { useEffect, useMemo, useState } from 'react'
import { Link } from 'react-router-dom'
import { AlertTriangle, Check, ChevronDown, Search, Send } from 'lucide-react'
import { getPickableTemplates, getTelegramStatus, type PickableTemplate } from '@/api/services/messages'
import { getMessageTokens } from '@/api/services/autoMessages'
import { createCampaign, type AudienceSpec, type CampaignCreated } from '@/api/services/messaging'
import { Modal } from '@/components/ui/Modal'
import { Button } from '@/components/ui/Button'
import { apiErrorMessage, cn, formatMoney } from '@/lib/utils'
import { PARENT_RELATIONS, countLine, type Relation } from '@/lib/relations'
import { MessageEditor, type TokenDef } from './MessageEditor'
import { TelegramPreview } from './TelegramPreview'
import { AttachmentPicker, type PickedAttachment } from './AttachmentPicker'
import { RelationChips } from './RelationChips'
import { useAudienceCount, useMessagePreview } from './useTelegramDraft'

/** Oluvchi — modal faqat shu maydonlarni ishlatadi (Student ham, guruh a'zosi ham mos keladi). */
export interface TelegramRecipient {
  id: string
  fullName: string
  /** A'zolik holati: 'active' | 'trial' | 'frozen' (berilmasa «Holat» filtri yo'q). */
  status?: string
  /** Student obyekti uzatilganda holat shu maydonda keladi. */
  memberState?: string
  /** Balans: manfiy = qarz (guruhdan kelsa — SHU GURUH balansi). Berilmasa «To'lov» filtri yo'q. */
  balance?: number
}

type DebtFilter = 'all' | 'debt' | 'nodebt'

const STATUS_META: { key: string; label: string; chip: string; badge: string }[] = [
  { key: 'active', label: 'Aktiv', chip: 'border-emerald-300 bg-emerald-50 text-emerald-700', badge: 'bg-emerald-50 text-emerald-700' },
  { key: 'trial', label: 'Sinov', chip: 'border-amber-300 bg-amber-50 text-amber-700', badge: 'bg-amber-50 text-amber-700' },
  { key: 'frozen', label: 'Muzlatilgan', chip: 'border-sky-300 bg-sky-50 text-sky-700', badge: 'bg-sky-50 text-sky-700' },
  // «Aktiv muzlatilgan» — `memberState` "yearFrozen" (kichik harfda) — alohida kalit, aks holda
  // bunday oluvchi hech bir chipga tushmay, filtrdan jimgina yo'qolardi.
  { key: 'yearfrozen', label: 'Aktiv muzlatilgan', chip: 'border-indigo-300 bg-indigo-50 text-indigo-700', badge: 'bg-indigo-50 text-indigo-700' },
]

const stateOf = (r: TelegramRecipient) => (r.status ?? r.memberState ?? '').toLowerCase()
const isDebtor = (r: TelegramRecipient) => (r.balance ?? 0) < 0

/**
 * Tanlangan o'quvchi(lar)ga TELEGRAM xabar — ixcham kompozitor (CRM upgrade P2.8): oluvchilar
 * filtri (holat / to'lov / qo'lda belgilash) + kimning Telegramiga + formatlash + bitta fayl +
 * «Qabul qildim» + server preview + yuborishdan OLDINGI sanoq. Yuborish — OUTBOX kampaniyasi
 * (`kind` — `broadcast` yoki ARXIV ro'yxatidan `archive`; faol va arxiv aralashmaydi).
 * O'quvchi profili, o'quvchilar ro'yxati, guruh sahifasi va davomat sahifasidan ochiladi.
 */
export function TelegramSendModal({
  open,
  onClose,
  recipients,
  kind = 'broadcast',
}: {
  open: boolean
  onClose: () => void
  recipients: TelegramRecipient[]
  kind?: 'broadcast' | 'archive'
}) {
  if (!open) return null
  return <SendModalBody onClose={onClose} recipients={recipients} kind={kind} />
}

function SendModalBody({
  onClose,
  recipients,
  kind,
}: {
  onClose: () => void
  recipients: TelegramRecipient[]
  kind: 'broadcast' | 'archive'
}) {
  const [text, setText] = useState('')
  const [attachment, setAttachment] = useState<PickedAttachment | null>(null)
  const [ack, setAck] = useState(true)
  const [relations, setRelations] = useState<Relation[]>([...PARENT_RELATIONS])
  const [statusSel, setStatusSel] = useState<Set<string>>(new Set())
  const [debtSel, setDebtSel] = useState<DebtFilter>('all')
  const [excluded, setExcluded] = useState<Set<string>>(new Set())
  const [listOpen, setListOpen] = useState(false)
  const [search, setSearch] = useState('')
  const [templates, setTemplates] = useState<PickableTemplate[]>([])
  const [tokens, setTokens] = useState<TokenDef[]>([])
  const [configured, setConfigured] = useState(true)
  const [sending, setSending] = useState(false)
  const [error, setError] = useState('')
  const [done, setDone] = useState<CampaignCreated | null>(null)

  useEffect(() => {
    // Fon o'qishlar: holat olinmasa ogohlantirish chiqmaydi — yuborishda server sababni aytadi.
    getTelegramStatus().then((s) => setConfigured(s.configured)).catch(() => setConfigured(true))
    getPickableTemplates().then(setTemplates).catch(() => setTemplates([]))
    getMessageTokens().then((ts) => setTokens(ts.filter((t) => t.group !== 'lead'))).catch(() => setTokens([]))
  }, [])

  const multi = recipients.length > 1
  const availableStatuses = useMemo(() => STATUS_META.filter((s) => recipients.some((r) => stateOf(r) === s.key)), [recipients])
  const hasBalance = useMemo(() => recipients.some((r) => typeof r.balance === 'number'), [recipients])
  const debtorCount = useMemo(() => recipients.filter(isDebtor).length, [recipients])

  const filtered = useMemo(
    () =>
      recipients.filter((r) => {
        if (statusSel.size > 0 && !statusSel.has(stateOf(r))) return false
        if (debtSel === 'debt' && !isDebtor(r)) return false
        if (debtSel === 'nodebt' && isDebtor(r)) return false
        return true
      }),
    [recipients, statusSel, debtSel],
  )
  const chosen = useMemo(() => filtered.filter((r) => !excluded.has(r.id)), [filtered, excluded])
  const visibleRows = useMemo(() => {
    const q = search.trim().toLowerCase()
    return q ? filtered.filter((r) => r.fullName.toLowerCase().includes(q)) : filtered
  }, [filtered, search])

  const studentIds = useMemo(() => chosen.map((r) => r.id).slice(0, 2000), [chosen])
  const audience: AudienceSpec | null = studentIds.length > 0 ? { kind: 'selected', studentIds, relations } : null
  const { preview, loading: previewLoading } = useMessagePreview({
    template: text,
    studentId: studentIds[0],
    audience,
    attachmentId: attachment?.id,
    kind,
  })
  const { count, loading: countLoading, error: countError } = useAudienceCount({ audience, template: text, kind })
  const errors = preview?.errors ?? []
  const canSend = !!audience && (text.trim() !== '' || !!attachment) && errors.length === 0 && (count?.messages ?? 0) > 0 && !countLoading

  const toggleSet = (setter: React.Dispatch<React.SetStateAction<Set<string>>>, key: string) =>
    setter((prev) => {
      const next = new Set(prev)
      if (next.has(key)) next.delete(key)
      else next.add(key)
      return next
    })

  const setAllVisible = (checked: boolean) =>
    setExcluded((prev) => {
      const next = new Set(prev)
      for (const r of visibleRows) {
        if (checked) next.delete(r.id)
        else next.add(r.id)
      }
      return next
    })

  const send = async () => {
    if (!audience || sending) return
    if (count && !confirm(`${countLine(count)}\n\nYuborilsinmi?`)) return
    setSending(true)
    setError('')
    try {
      const res = await createCampaign({ kind, audience, template: text, attachmentId: attachment?.id ?? null, ackRequired: ack })
      setDone(res)
      setText('')
      setAttachment(null)
    } catch (e) {
      // Oyna OCHIQ qoladi, matn saqlanadi — sabab shu yerda (`error-visibility.md`).
      setError(apiErrorMessage(e, "Xabarni yuborib bo'lmadi"))
    } finally {
      setSending(false)
    }
  }

  return (
    <Modal
      open
      onClose={onClose}
      title={kind === 'archive' ? 'Telegram xabar (arxiv)' : 'Telegram xabar'}
      size="xl"
      footer={
        <>
          <Button variant="secondary" onClick={onClose}>
            Yopish
          </Button>
          <Button onClick={send} disabled={!canSend || sending}>
            <Send className="h-4 w-4" /> {sending ? 'Yuborilmoqda...' : 'Yuborish'}
          </Button>
        </>
      }
    >
      <div className="grid grid-cols-1 gap-5 lg:grid-cols-[1fr_300px]">
        <div className="space-y-4">
          {!configured && (
            <div className="flex items-start gap-2 rounded-xl border border-amber-200 bg-amber-50 px-3 py-2 text-sm text-amber-800">
              <AlertTriangle className="mt-0.5 h-4 w-4 shrink-0" />
              <p>Telegram bot sozlanmagan — xabar yuborilmaydi.</p>
            </div>
          )}

          <p className="rounded-lg bg-slate-50 px-3 py-2 text-sm text-slate-600">
            {!multi ? (
              recipients.length === 1 ? (
                <>
                  <b>{recipients[0].fullName}</b> — botga ulangan chatlariga yuboriladi.
                </>
              ) : (
                <span className="text-slate-400">Oluvchi tanlanmagan</span>
              )
            ) : (
              <>Tanlangan o'quvchilarning botga ulangan Telegram chatlariga yuboriladi.</>
            )}
          </p>

          {multi && (
            <div className="space-y-3 rounded-xl border border-slate-200 p-3">
              <div className="flex items-center justify-between">
                <span className="text-sm font-medium text-slate-600">
                  Oluvchilar <span className="text-slate-400">({recipients.length} ta)</span>
                </span>
              </div>
              {availableStatuses.length > 0 && (
                <div className="flex flex-wrap items-center gap-1.5">
                  <span className="w-12 shrink-0 text-xs text-slate-400">Holat</span>
                  <FilterChip active={statusSel.size === 0} onClick={() => setStatusSel(new Set())}>
                    Hammasi
                  </FilterChip>
                  {availableStatuses.map((s) => (
                    <FilterChip key={s.key} active={statusSel.has(s.key)} activeCls={s.chip} onClick={() => toggleSet(setStatusSel, s.key)}>
                      {s.label} · {recipients.filter((r) => stateOf(r) === s.key).length}
                    </FilterChip>
                  ))}
                </div>
              )}
              {hasBalance && (
                <div className="flex flex-wrap items-center gap-1.5">
                  <span className="w-12 shrink-0 text-xs text-slate-400">To'lov</span>
                  <FilterChip active={debtSel === 'all'} onClick={() => setDebtSel('all')}>
                    Hammasi
                  </FilterChip>
                  <FilterChip active={debtSel === 'debt'} activeCls="border-red-300 bg-red-50 text-red-700" onClick={() => setDebtSel('debt')}>
                    Faqat qarzdorlar · {debtorCount}
                  </FilterChip>
                  <FilterChip active={debtSel === 'nodebt'} activeCls="border-emerald-300 bg-emerald-50 text-emerald-700" onClick={() => setDebtSel('nodebt')}>
                    Qarzi yo'q · {recipients.length - debtorCount}
                  </FilterChip>
                </div>
              )}
              <div className="flex flex-wrap items-center justify-between gap-2 border-t border-slate-100 pt-2.5">
                <p className="text-sm text-slate-600">
                  Tanlangan: <b className={chosen.length === 0 ? 'text-red-500' : 'text-emerald-600'}>{chosen.length} ta</b> o'quvchi
                </p>
                <button
                  type="button"
                  onClick={() => setListOpen((v) => !v)}
                  className="inline-flex items-center gap-1 rounded-lg border border-slate-200 px-2.5 py-1 text-xs font-medium text-slate-600 hover:bg-slate-50"
                >
                  Ro'yxat
                  <ChevronDown className={cn('h-3.5 w-3.5 transition-transform', listOpen && 'rotate-180')} />
                </button>
              </div>
              {listOpen && (
                <div className="space-y-2">
                  <div className="flex items-center gap-2">
                    <div className="relative flex-1">
                      <Search className="pointer-events-none absolute left-3 top-2.5 h-4 w-4 text-slate-400" />
                      <input
                        value={search}
                        onChange={(e) => setSearch(e.target.value)}
                        placeholder="Ism bo'yicha qidirish..."
                        className="w-full rounded-lg border border-slate-200 py-2 pl-9 pr-3 text-sm outline-none focus:border-brand-400"
                      />
                    </div>
                    <button type="button" onClick={() => setAllVisible(true)} className="rounded-lg border border-slate-200 px-2.5 py-2 text-xs text-slate-600 hover:bg-slate-50">
                      Hammasi
                    </button>
                    <button type="button" onClick={() => setAllVisible(false)} className="rounded-lg border border-slate-200 px-2.5 py-2 text-xs text-slate-600 hover:bg-slate-50">
                      Hech kim
                    </button>
                  </div>
                  <div className="max-h-56 space-y-1 overflow-y-auto">
                    {visibleRows.length === 0 && <p className="py-6 text-center text-sm text-slate-400">Filtrga mos oluvchi yo'q</p>}
                    {visibleRows.map((r) => {
                      const active = !excluded.has(r.id)
                      const st = STATUS_META.find((s) => s.key === stateOf(r))
                      return (
                        <button
                          key={r.id}
                          type="button"
                          onClick={() => toggleSet(setExcluded, r.id)}
                          className={cn(
                            'flex w-full items-center gap-2 rounded-lg border px-3 py-2 text-left text-sm',
                            active ? 'border-brand-300 bg-brand-50' : 'border-slate-100 hover:bg-slate-50',
                          )}
                        >
                          <span className={cn('flex h-4 w-4 shrink-0 items-center justify-center rounded border', active ? 'border-brand-500 bg-brand-500 text-white' : 'border-slate-300')}>
                            {active && <Check className="h-3 w-3" />}
                          </span>
                          <span className="min-w-0 flex-1 truncate font-medium text-slate-700">{r.fullName}</span>
                          {isDebtor(r) && (
                            <span className="shrink-0 rounded-md bg-red-50 px-1.5 py-0.5 text-[11px] font-semibold text-red-600">
                              {formatMoney(Math.abs(r.balance ?? 0))}
                            </span>
                          )}
                          {st && <span className={cn('shrink-0 rounded-md px-1.5 py-0.5 text-[11px] font-medium', st.badge)}>{st.label}</span>}
                        </button>
                      )
                    })}
                  </div>
                </div>
              )}
            </div>
          )}

          <div>
            <p className="mb-1.5 text-xs font-medium text-slate-500">Kimning Telegramiga</p>
            <RelationChips value={relations} onChange={setRelations} />
          </div>

          <MessageEditor
            formatting
            label="Xabar matni"
            value={text}
            onChange={setText}
            tokens={tokens}
            templates={templates.map((t) => ({ name: t.name, text: t.text }))}
            rows={5}
            placeholder="Hurmatli ota-ona, ..."
            hint="O'zgaruvchilar har o'quvchiga moslab to'ldiriladi."
            error={errors.length > 0 ? errors.join('; ') : null}
          />
          <AttachmentPicker value={attachment} onChange={setAttachment} />
          {preview?.mode === 'separate' && (
            <p className="-mt-2 text-xs font-medium text-amber-700">Matn izoh uchun uzun — fayl va matn ikki alohida xabar bo'lib ketadi</p>
          )}
          <label className="inline-flex cursor-pointer items-center gap-2 text-sm text-slate-700">
            <input type="checkbox" checked={ack} onChange={(e) => setAck(e.target.checked)} className="h-4 w-4 accent-brand-600" />
            "Qabul qildim" tugmasi
          </label>

          <p className={cn('text-sm', countError ? 'font-medium text-red-600' : 'text-slate-700')}>
            {count ? countLine(count) : countError || (audience ? 'Sanalmoqda...' : "O'quvchi tanlanmagan")}
          </p>
          {error && <p className="text-sm font-medium text-red-600">{error}</p>}
          {done && (
            <p className="flex flex-wrap items-center gap-1.5 text-sm font-medium text-emerald-700">
              <Check className="h-4 w-4" /> Navbatga qo'yildi: {done.messages} ta xabar ({done.chats} ta chat).
              <Link to={`/admin/messages?tab=tarix&campaign=${done.id}`} className="text-brand-600 underline">
                Hisobot
              </Link>
            </p>
          )}
        </div>

        <div>
          <p className="mb-1.5 text-sm font-medium text-slate-600">Telegram'da ko'rinishi</p>
          <TelegramPreview html={preview?.html ?? ''} mode={preview?.mode ?? 'text'} loading={previewLoading} ackButton={ack} attachment={attachment} />
          {preview?.sample && <p className="mt-1.5 text-xs text-slate-400">Namuna: {preview.sample.fullName}</p>}
        </div>
      </div>
    </Modal>
  )
}

function FilterChip({
  active,
  activeCls = 'border-brand-400 bg-brand-50 text-brand-700',
  onClick,
  children,
}: {
  active: boolean
  activeCls?: string
  onClick: () => void
  children: React.ReactNode
}) {
  return (
    <button
      type="button"
      onClick={onClick}
      className={cn('rounded-full border px-2.5 py-1 text-xs font-medium transition-colors', active ? activeCls : 'border-slate-200 text-slate-500 hover:bg-slate-50')}
    >
      {children}
    </button>
  )
}
