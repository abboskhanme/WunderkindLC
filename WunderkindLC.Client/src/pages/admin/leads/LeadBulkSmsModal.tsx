import { useEffect, useMemo, useRef, useState } from 'react'
import { Send, AlertTriangle, Check } from 'lucide-react'
import type { Lead, Stage } from '@/types'
import {
  getSmsStatus,
  getPickableTemplates,
  sendLeadSmsBulk,
  watchSmsProgress,
  type PickableTemplate,
  type LeadBulkSmsResult,
} from '@/api/services/messages'
import { getMessageTokens } from '@/api/services/autoMessages'
import { Modal } from '@/components/ui/Modal'
import { Button } from '@/components/ui/Button'
import { MessageEditor, type TokenDef } from '@/components/messaging/MessageEditor'
import { cn } from '@/lib/utils'

interface Props {
  open: boolean
  onClose: () => void
  leads: Lead[]
  stages: Stage[]
}

/**
 * Lidlarga OMMAVIY SMS: bosqich(lar)ni tanlab (yoki "Barchasi") barcha mos lidlarga
 * bitta matn yuboriladi. Tokenlar har lidga moslab to'ldiriladi (backend).
 */
export function LeadBulkSmsModal({ open, onClose, leads, stages }: Props) {
  const [selStages, setSelStages] = useState<Set<string>>(new Set())
  const [allStages, setAllStages] = useState(true)
  const [text, setText] = useState('')
  const [configured, setConfigured] = useState(true)
  // Kanal (Eskiz/Local) tanlanmaydi — server Sozlamalardan oladi; bu faqat ogohlantirish uchun.
  const [localSms, setLocalSms] = useState(false)
  const [templates, setTemplates] = useState<PickableTemplate[]>([])
  const [tokens, setTokens] = useState<TokenDef[]>([])
  const [sending, setSending] = useState(false)
  const [result, setResult] = useState<LeadBulkSmsResult | null>(null)
  const [error, setError] = useState('')
  /** Fonda ketayotgan partiyani kuzatishni to'xtatuvchi. */
  const stopWatchRef = useRef<(() => void) | null>(null)
  useEffect(() => {
    if (open) return
    stopWatchRef.current?.()
    stopWatchRef.current = null
  }, [open])
  useEffect(() => () => stopWatchRef.current?.(), [])

  useEffect(() => {
    if (!open) return
    // eslint-disable-next-line react-hooks/set-state-in-effect -- modal ochilganda holatni tozalaymiz (maqsadli)
    setSelStages(new Set())
    setAllStages(true)
    setText('')
    setResult(null)
    setError('')
    setSending(false)
    getSmsStatus()
      .then((s) => {
        setConfigured(s.configured)
        setLocalSms(s.localEnabled)
      })
      .catch(() => setConfigured(false))
    getPickableTemplates('lead').then(setTemplates).catch(() => setTemplates([]))
    getMessageTokens()
      .then((ts) => setTokens(ts.filter((t) => t.group === 'lead' || t.group === 'common')))
      .catch(() => setTokens([]))
  }, [open])

  /** Har bosqichdagi lidlar soni. */
  const countByStage = useMemo(() => {
    const m = new Map<string, number>()
    for (const l of leads) m.set(l.stage, (m.get(l.stage) ?? 0) + 1)
    return m
  }, [leads])

  /** Tanlangan bosqich(lar)dagi lidlar. */
  const targets = useMemo(
    () => (allStages ? leads : leads.filter((l) => selStages.has(l.stage))),
    [leads, allStages, selStages],
  )
  const withPhone = useMemo(
    () => targets.filter((l) => l.phone || l.fatherPhone || l.motherPhone).length,
    [targets],
  )

  const toggleStage = (id: string) => {
    setAllStages(false)
    setSelStages((prev) => {
      const next = new Set(prev)
      if (next.has(id)) next.delete(id)
      else next.add(id)
      return next
    })
    setResult(null)
  }

  const pickAll = () => {
    setAllStages(true)
    setSelStages(new Set())
    setResult(null)
  }

  const handleSend = async () => {
    if (!text.trim() || sending || targets.length === 0) return
    setSending(true)
    setResult(null)
    setError('')
    try {
      const r = await sendLeadSmsBulk(targets.map((l) => l.id), text.trim())
      setResult(r)
      // Ko'p lid — SMS'lar FONDA ketmoqda. Jonli holatni kuzatamiz (oyna yopilsa ham yuborish davom etadi).
      if (r.queued && r.batchId) {
        stopWatchRef.current?.()
        stopWatchRef.current = watchSmsProgress(r.batchId, (p) =>
          setResult((prev) =>
            prev ? { ...prev, sent: p.sent, queued: !p.finished, queuedCount: p.total - p.done } : prev,
          ),
        )
      }
    } catch (e) {
      setError(
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
      title="Lidlarga SMS yuborish"
      size="md"
      footer={
        <>
          <Button variant="secondary" onClick={onClose}>
            Yopish
          </Button>
          <Button onClick={handleSend} disabled={!text.trim() || sending || targets.length === 0}>
            <Send className="h-4 w-4" />{' '}
            {sending ? 'Yuborilmoqda...' : `Yuborish (${targets.length} lid)`}
          </Button>
        </>
      }
    >
      <div className="space-y-4">
        {/* Local yoqilgan bo'lsa kanal — Local (Eskiz kerak emas), ogohlantirish chiqmaydi. */}
        {!configured && !localSms && (
          <div className="flex items-start gap-2 rounded-xl border border-amber-200 bg-amber-50 px-3 py-2 text-sm text-amber-800">
            <AlertTriangle className="mt-0.5 h-4 w-4 shrink-0" />
            <p>SMS (Eskiz) sozlanmagan. "Sozlamalar → Xabar kanallari"da login/parol kiriting.</p>
          </div>
        )}

        {/* Bosqichlar */}
        <div>
          <div className="mb-1.5 text-sm font-medium text-slate-600">Qaysi bosqich(lar)ga</div>
          <div className="flex flex-wrap gap-1.5">
            <button
              type="button"
              onClick={pickAll}
              className={cn(
                'inline-flex items-center gap-1 rounded-full border px-3 py-1.5 text-xs font-medium transition-colors',
                allStages
                  ? 'border-brand-400 bg-brand-50 text-brand-700'
                  : 'border-slate-200 text-slate-600 hover:bg-slate-50',
              )}
            >
              {allStages && <Check className="h-3 w-3" />} Barchasi ({leads.length})
            </button>
            {stages.map((s) => {
              const on = !allStages && selStages.has(s.id)
              return (
                <button
                  key={s.id}
                  type="button"
                  onClick={() => toggleStage(s.id)}
                  className={cn(
                    'inline-flex items-center gap-1 rounded-full border px-3 py-1.5 text-xs font-medium transition-colors',
                    on
                      ? 'border-brand-400 bg-brand-50 text-brand-700'
                      : 'border-slate-200 text-slate-600 hover:bg-slate-50',
                  )}
                >
                  {on && <Check className="h-3 w-3" />} {s.title} ({countByStage.get(s.id) ?? 0})
                </button>
              )
            })}
          </div>
          <p className="mt-1.5 rounded-lg bg-slate-50 px-3 py-2 text-sm text-slate-600">
            Tanlandi: <b>{targets.length} lid</b> · raqami borlar:{' '}
            <b className={withPhone === 0 ? 'text-red-500' : 'text-emerald-600'}>{withPhone}</b>
          </p>
        </div>

        {/* Matn */}
        <MessageEditor
          label="Xabar matni"
          value={text}
          onChange={setText}
          tokens={tokens}
          templates={templates.map((t) => ({ name: t.name, text: t.text }))}
          showSmsCounter
          rows={4}
          placeholder="SMS matni — tokenlar har lidga moslab to'ldiriladi"
        />

        {error && <p className="text-sm font-medium text-red-600">{error}</p>}
        {result && (
          <div className="space-y-1 rounded-lg border border-slate-100 bg-slate-50 px-3 py-2 text-sm">
            <div className="flex flex-wrap items-center gap-3">
              <span className="inline-flex items-center gap-1 font-medium text-emerald-700">
                <Check className="h-4 w-4" /> Yuborildi: {result.sent}
              </span>
              {!result.queued && <span className="font-medium text-red-600">Xato: {result.failed}</span>}
              <span className="font-medium text-amber-600">Telefonsiz: {result.noPhone}</span>
            </div>
            {result.queued && (
              // Ko'p lid — fonda yuborilmoqda: oynani yopsa ham to'xtamaydi.
              <p className="text-slate-600">
                Navbatda: <b>{result.queuedCount}</b> — yuborish fonda davom etmoqda, oynani yopsangiz
                ham to'xtamaydi (natija «Xabarlar → Tarix»da).
              </p>
            )}
          </div>
        )}
      </div>
    </Modal>
  )
}
