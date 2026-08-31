import { useEffect, useMemo, useRef, useState } from 'react'
import { Send, AlertTriangle, Check, ChevronDown, Search } from 'lucide-react'
import {
  getSmsStatus, getPickableTemplates, sendSms, watchSmsProgress,
  type SmsProvider, type PickableTemplate,
} from '@/api/services/messages'
import { getMessageTokens } from '@/api/services/autoMessages'
import { Modal } from '@/components/ui/Modal'
import { Button } from '@/components/ui/Button'
import { MessageEditor, type TokenDef } from '@/components/messaging/MessageEditor'
import { SmsProviderPicker } from '@/components/messaging/SmsProviderPicker'
import { cn, formatMoney } from '@/lib/utils'

/** SMS oluvchi — SmsModal faqat shu maydonlarni ishlatadi (Student ham mos keladi). */
export interface SmsRecipient {
  id: string
  fullName: string
  phone?: string | null
  parentPhone?: string | null
  fatherPhone?: string | null
  motherPhone?: string | null
  /** A'zolik holati: 'active' (aktiv) | 'trial' (sinov) | 'frozen' (muzlatilgan).
   *  Berilmasa — "Holat" filtri ko'rinmaydi. */
  status?: string
  /** Student obyekti to'g'ridan-to'g'ri uzatilganda holat shu maydonda keladi (StudentsPage). */
  memberState?: string
  /** Balans: manfiy = qarz. Guruh a'zosidan kelsa — SHU GURUH balansi (umumiy emas).
   *  Berilmasa — "To'lov" filtri ko'rinmaydi. */
  balance?: number
}

interface Props {
  open: boolean
  onClose: () => void
  /** Bitta o'quvchi ham bo'lishi mumkin (masalan o'quvchi sahifasidan) — UI mos ko'rinadi. */
  recipients: SmsRecipient[]
}

/** To'lov holati bo'yicha filtr. */
type DebtFilter = 'all' | 'debt' | 'nodebt'

const STATUS_META: { key: string; label: string; chip: string; badge: string }[] = [
  { key: 'active', label: 'Aktiv', chip: 'border-emerald-300 bg-emerald-50 text-emerald-700', badge: 'bg-emerald-50 text-emerald-700' },
  { key: 'trial', label: 'Sinov', chip: 'border-amber-300 bg-amber-50 text-amber-700', badge: 'bg-amber-50 text-amber-700' },
  { key: 'frozen', label: 'Muzlatilgan', chip: 'border-sky-300 bg-sky-50 text-sky-700', badge: 'bg-sky-50 text-sky-700' },
  // «Aktiv muzlatilgan» — `memberState` "yearFrozen" (kichik harfda), ya'ni ALOHIDA kalit.
  // Bu yerda bo'lmasa bunday oluvchi hech bir chipga tushmay, holat filtridan JIMGINA
  // yo'qolib qolardi (guruh a'zosida esa `status` baribir "frozen" bo'lib qolaveradi).
  { key: 'yearfrozen', label: 'Aktiv muzlatilgan', chip: 'border-indigo-300 bg-indigo-50 text-indigo-700', badge: 'bg-indigo-50 text-indigo-700' },
]

/** A'zolik holati — `status` yoki (Student uzatilganda) `memberState`dan. */
function stateOf(r: SmsRecipient): string {
  return (r.status ?? r.memberState ?? '').toLowerCase()
}

/** Tanlangan rejimga mos raqam (ota-ona raqamida ota/ona zaxirasi — server bilan bir xil tartib). */
function phoneOf(r: SmsRecipient, toParent: boolean): string | null | undefined {
  return toParent ? r.parentPhone || r.fatherPhone || r.motherPhone : r.phone
}

function isDebtor(r: SmsRecipient): boolean {
  return (r.balance ?? 0) < 0
}

/**
 * O'quvchilar ro'yxatidan tanlangan(lar)ga SMS yuborish (Eskiz). Shablon tanlab, ota-ona yoki
 * o'quvchi raqamiga jo'natiladi; matn har o'quvchiga moslab to'ldiriladi ({fish} {sinf} {qarzdorlik}...).
 *
 * Ko'p oluvchi bo'lsa (masalan guruhga yuborish) — HOLAT (aktiv/sinov/muzlatilgan) va TO'LOV
 * (faqat qarzdorlar / qarzi yo'q) filtrlari, ustiga har bir oluvchini qo'lda belgilash/olib tashlash.
 */
export function SmsModal({ open, onClose, recipients }: Props) {
  const [toParent, setToParent] = useState(true)
  const [message, setMessage] = useState('')
  const [provider, setProvider] = useState<SmsProvider>('eskiz')
  const [agentId, setAgentId] = useState('')
  const [configured, setConfigured] = useState(true)
  const [templates, setTemplates] = useState<PickableTemplate[]>([])
  const [tokens, setTokens] = useState<TokenDef[]>([])
  const [sending, setSending] = useState(false)
  const [result, setResult] = useState<string | null>(null)
  /** Fonda ketayotgan partiyani kuzatishni to'xtatuvchi (oyna yopilganda chaqiriladi). */
  const stopWatchRef = useRef<(() => void) | null>(null)

  // ---- Oluvchilar filtri ----
  /** Tanlangan holatlar; bo'sh = hammasi. */
  const [statusSel, setStatusSel] = useState<Set<string>>(new Set())
  const [debtSel, setDebtSel] = useState<DebtFilter>('all')
  /** Qo'lda olib tashlangan oluvchilar (id). */
  const [excluded, setExcluded] = useState<Set<string>>(new Set())
  const [listOpen, setListOpen] = useState(false)
  const [search, setSearch] = useState('')

  useEffect(() => {
    if (!open) return
    // eslint-disable-next-line react-hooks/set-state-in-effect -- modal ochilganda holatni tozalaymiz (maqsadli)
    setMessage('')
    setResult(null)
    setToParent(true)
    setProvider('eskiz')
    setAgentId('')
    setSending(false)
    setStatusSel(new Set())
    setDebtSel('all')
    setExcluded(new Set())
    setListOpen(false)
    setSearch('')
    getSmsStatus().then((s) => setConfigured(s.configured))
    getPickableTemplates('student').then(setTemplates).catch(() => setTemplates([]))
    getMessageTokens()
      .then((ts) => setTokens(ts.filter((t) => t.group !== 'lead')))
      .catch(() => setTokens([]))
  }, [open])

  // Oyna yopilganda (yoki komponent yo'q qilinganda) fon partiyasi kuzatuvini to'xtatamiz.
  useEffect(() => {
    if (open) return
    stopWatchRef.current?.()
    stopWatchRef.current = null
  }, [open])
  useEffect(() => () => stopWatchRef.current?.(), [])

  const multi = recipients.length > 1

  /** Ro'yxatda uchraydigan holatlar (bo'sh bo'lsa "Holat" filtri ko'rsatilmaydi). */
  const availableStatuses = useMemo(
    () => STATUS_META.filter((s) => recipients.some((r) => stateOf(r) === s.key)),
    [recipients],
  )
  const hasBalance = useMemo(() => recipients.some((r) => typeof r.balance === 'number'), [recipients])
  const debtorCount = useMemo(() => recipients.filter(isDebtor).length, [recipients])

  /** Filtrlardan o'tganlar. */
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

  /** Filtrdan o'tgan va qo'lda olib tashlanmaganlar. */
  const chosen = useMemo(() => filtered.filter((r) => !excluded.has(r.id)), [filtered, excluded])
  /** Shulardan raqami borlari — aslida SMS shularga ketadi. */
  const sendable = useMemo(() => chosen.filter((r) => phoneOf(r, toParent)), [chosen, toParent])
  const noPhoneCount = chosen.length - sendable.length

  const visibleRows = useMemo(() => {
    const q = search.trim().toLowerCase()
    return q ? filtered.filter((r) => r.fullName.toLowerCase().includes(q)) : filtered
  }, [filtered, search])

  const filtersDirty = statusSel.size > 0 || debtSel !== 'all' || excluded.size > 0

  const toggleStatus = (key: string) =>
    setStatusSel((prev) => {
      const next = new Set(prev)
      if (next.has(key)) next.delete(key)
      else next.add(key)
      return next
    })

  const toggleRow = (id: string) =>
    setExcluded((prev) => {
      const next = new Set(prev)
      if (next.has(id)) next.delete(id)
      else next.add(id)
      return next
    })

  /** Ko'rinib turgan (qidiruvdan o'tgan) qatorlarni hammasini belgilash / bekor qilish. */
  const setAllVisible = (checked: boolean) =>
    setExcluded((prev) => {
      const next = new Set(prev)
      for (const r of visibleRows) {
        if (checked) next.delete(r.id)
        else next.add(r.id)
      }
      return next
    })

  const resetFilters = () => {
    setStatusSel(new Set())
    setDebtSel('all')
    setExcluded(new Set())
    setSearch('')
  }

  const handleSend = async () => {
    if (!message.trim() || sending || sendable.length === 0) return
    setSending(true)
    setResult(null)
    try {
      const b = await sendSms({
        audience: 'selected',
        studentIds: sendable.map((s) => s.id),
        // Qarzdorlar filtri MIJOZ tomonida qo'llanadi (guruhda — shu guruh balansi bo'yicha,
        // serverdagi OnlyDebtors esa o'quvchining UMUMIY balansiga qarardi).
        onlyDebtors: false,
        toParent,
        text: message.trim(),
        provider,
        agentId: agentId || undefined,
      })
      if (b.recipientCount === 0) {
        setResult('Raqamli oluvchi topilmadi — hech kimga yuborilmadi.')
      } else if (b.queued) {
        // Ko'p oluvchi: SMS'lar FONDA ketmoqda (so'rov ichida kutilsa proksi ulanishni uzardi).
        // Oynani yopsa ham yuborish davom etadi — natija "Xabarlar → Tarix"da.
        setResult(`Yuborilmoqda: 0/${b.recipientCount} — oynani yopsangiz ham davom etadi.`)
        stopWatchRef.current?.()
        stopWatchRef.current = watchSmsProgress(b.id, (p) =>
          setResult(
            p.finished
              ? `SMS yuborildi: ${p.sent}/${p.total} raqamga.`
              : `Yuborilmoqda: ${p.done}/${p.total} — oynani yopsangiz ham davom etadi.`,
          ),
        )
      } else {
        setResult(`SMS yuborildi: ${b.sentCount}/${b.recipientCount} raqamga.`)
      }
    } catch (e) {
      setResult(
        (e as { response?: { data?: { message?: string } } })?.response?.data?.message ??
          'Yuborishda xatolik',
      )
    } finally {
      setSending(false)
    }
  }

  return (
    <Modal
      open={open}
      onClose={onClose}
      title="SMS yuborish"
      size={multi ? 'lg' : 'md'}
      footer={
        <>
          <Button variant="secondary" onClick={onClose}>
            Yopish
          </Button>
          <Button onClick={handleSend} disabled={!message.trim() || sending || sendable.length === 0}>
            <Send className="h-4 w-4" />{' '}
            {sending ? 'Yuborilmoqda...' : multi ? `Yuborish (${sendable.length})` : 'Yuborish'}
          </Button>
        </>
      }
    >
      <div className="space-y-4">
        {!configured && provider === 'eskiz' && (
          <div className="flex items-start gap-2 rounded-xl border border-amber-200 bg-amber-50 px-3 py-2 text-sm text-amber-800">
            <AlertTriangle className="mt-0.5 h-4 w-4 shrink-0" />
            <p>SMS (Eskiz) sozlanmagan. "Sozlamalar → Xabar kanallari"da login/parol kiriting.</p>
          </div>
        )}

        {/* Kimga: ota-ona yoki o'quvchi raqami */}
        <div>
          <div className="mb-1.5 text-sm font-medium text-slate-600">Kimga</div>
          <div className="tabs inline-flex">
            <button
              type="button"
              onClick={() => setToParent(true)}
              className={cn('tab', toParent && 'active')}
            >
              Ota-ona raqami
            </button>
            <button
              type="button"
              onClick={() => setToParent(false)}
              className={cn('tab', !toParent && 'active')}
            >
              O'quvchi raqami
            </button>
          </div>
          {!multi && (
            <p className="mt-1.5 rounded-lg bg-slate-50 px-3 py-2 text-sm text-slate-600">
              {recipients.length === 1 ? (
                <>
                  <b>{recipients[0].fullName}</b>
                  {' — '}
                  {phoneOf(recipients[0], toParent) || <span className="text-red-500">raqam yo'q</span>}
                </>
              ) : (
                <span className="text-slate-400">Oluvchi tanlanmagan</span>
              )}
            </p>
          )}
        </div>

        {/* Oluvchilar filtri — faqat ko'p oluvchi bo'lganda (guruhga / tanlanganlarga yuborish) */}
        {multi && (
          <div className="space-y-3 rounded-xl border border-slate-200 p-3">
            <div className="flex items-center justify-between">
              <span className="text-sm font-medium text-slate-600">
                Oluvchilar <span className="text-slate-400">({recipients.length} ta)</span>
              </span>
              {filtersDirty && (
                <button
                  type="button"
                  onClick={resetFilters}
                  className="rounded-full border border-slate-200 px-2.5 py-1 text-xs text-slate-400 transition-colors hover:bg-slate-50"
                >
                  Filtrni tozalash
                </button>
              )}
            </div>

            {availableStatuses.length > 0 && (
              <div className="flex flex-wrap items-center gap-1.5">
                <span className="w-12 shrink-0 text-xs text-slate-400">Holat</span>
                <FilterChip active={statusSel.size === 0} onClick={() => setStatusSel(new Set())}>
                  Hammasi
                </FilterChip>
                {availableStatuses.map((s) => (
                  <FilterChip
                    key={s.key}
                    active={statusSel.has(s.key)}
                    activeCls={s.chip}
                    onClick={() => toggleStatus(s.key)}
                  >
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
                <FilterChip
                  active={debtSel === 'debt'}
                  activeCls="border-red-300 bg-red-50 text-red-700"
                  onClick={() => setDebtSel('debt')}
                >
                  Faqat qarzdorlar · {debtorCount}
                </FilterChip>
                <FilterChip
                  active={debtSel === 'nodebt'}
                  activeCls="border-emerald-300 bg-emerald-50 text-emerald-700"
                  onClick={() => setDebtSel('nodebt')}
                >
                  Qarzi yo'q · {recipients.length - debtorCount}
                </FilterChip>
              </div>
            )}

            <div className="flex flex-wrap items-center justify-between gap-2 border-t border-slate-100 pt-2.5">
              <p className="text-sm text-slate-600">
                Yuboriladi:{' '}
                <b className={sendable.length === 0 ? 'text-red-500' : 'text-emerald-600'}>
                  {sendable.length} ta
                </b>{' '}
                raqamga
                {noPhoneCount > 0 && (
                  <span className="text-amber-600"> · {noPhoneCount} tasida raqam yo'q</span>
                )}
              </p>
              <button
                type="button"
                onClick={() => setListOpen((v) => !v)}
                className="inline-flex items-center gap-1 rounded-lg border border-slate-200 px-2.5 py-1 text-xs font-medium text-slate-600 transition-colors hover:bg-slate-50"
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
                      className="w-full rounded-lg border border-slate-200 py-2 pl-9 pr-3 text-sm outline-none transition-colors focus:border-brand-400 focus:ring-2 focus:ring-brand-100"
                    />
                  </div>
                  <button
                    type="button"
                    onClick={() => setAllVisible(true)}
                    className="shrink-0 rounded-lg border border-slate-200 px-2.5 py-2 text-xs text-slate-600 transition-colors hover:bg-slate-50"
                  >
                    Hammasi
                  </button>
                  <button
                    type="button"
                    onClick={() => setAllVisible(false)}
                    className="shrink-0 rounded-lg border border-slate-200 px-2.5 py-2 text-xs text-slate-600 transition-colors hover:bg-slate-50"
                  >
                    Hech kim
                  </button>
                </div>
                <div className="max-h-64 space-y-1 overflow-y-auto">
                  {visibleRows.length === 0 && (
                    <p className="py-6 text-center text-sm text-slate-400">Filtrga mos oluvchi yo'q</p>
                  )}
                  {visibleRows.map((r) => {
                    const phone = phoneOf(r, toParent)
                    const active = !excluded.has(r.id)
                    const st = STATUS_META.find((s) => s.key === stateOf(r))
                    return (
                      <button
                        key={r.id}
                        type="button"
                        onClick={() => toggleRow(r.id)}
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
                          <span className="font-medium text-slate-700">{r.fullName}</span>
                          <span className={cn('ml-1 text-xs', phone ? 'text-slate-400' : 'text-amber-600')}>
                            · {phone || "raqam yo'q"}
                          </span>
                        </span>
                        {isDebtor(r) && (
                          <span className="shrink-0 rounded-md bg-red-50 px-1.5 py-0.5 text-[11px] font-semibold text-red-600">
                            {formatMoney(Math.abs(r.balance ?? 0))}
                          </span>
                        )}
                        {st && (
                          <span
                            className={cn(
                              'shrink-0 rounded-md px-1.5 py-0.5 text-[11px] font-medium',
                              st.badge,
                            )}
                          >
                            {st.label}
                          </span>
                        )}
                      </button>
                    )
                  })}
                </div>
              </div>
            )}
          </div>
        )}

        <SmsProviderPicker
          provider={provider}
          onProviderChange={setProvider}
          agentId={agentId}
          onAgentChange={setAgentId}
        />

        {/* Matn (shablon chiplari + tokenlar + SMS hisoblagich — yagona MessageEditor) */}
        <MessageEditor
          label="Xabar matni"
          value={message}
          onChange={setMessage}
          tokens={tokens}
          templates={templates.map((t) => ({ name: t.name, text: t.text }))}
          showSmsCounter
          rows={5}
          placeholder="Hurmatli ota-ona, ..."
          hint="O'rinbosarlar har o'quvchiga moslab to'ldiriladi."
        />

        {result && (
          <p
            className={cn(
              'flex items-center gap-1.5 text-sm font-medium',
              result.startsWith('SMS yuborildi') ? 'text-emerald-700' : 'text-amber-700',
            )}
          >
            {result.startsWith('SMS yuborildi') && <Check className="h-4 w-4" />}
            {result}
          </p>
        )}
      </div>
    </Modal>
  )
}

/** Filtr chipi — tanlanganda rangli, aks holda oq. */
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
      className={cn(
        'rounded-full border px-2.5 py-1 text-xs font-medium transition-colors',
        active ? activeCls : 'border-slate-200 text-slate-500 hover:bg-slate-50',
      )}
    >
      {children}
    </button>
  )
}
