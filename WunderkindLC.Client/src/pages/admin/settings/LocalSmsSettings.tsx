import { useEffect, useState } from 'react'
import { Check, CheckCircle2, MessageSquareText, XCircle } from 'lucide-react'
import { getLocalSmsSettings, saveLocalSmsSettings } from '@/api/services/settings'
import { getCtiAgents, type CtiAgent } from '@/api/services/cti'
import { Card } from '@/components/ui/Card'
import { Badge } from '@/components/ui/Badge'
import { Button } from '@/components/ui/Button'
import { Select } from '@/components/ui/Input'
import { Loader } from '@/components/ui/Loader'

/**
 * Local SMS — Eskiz'ga muqobil kanal: SMS Local Call CTI agent-telefonining SIM-kartasidan
 * yuboriladi. Bu yerda yoqish/o'chirish va STANDART agentni tanlash mumkin — u har qanday
 * "Local" tanlangan SMS yuborishda (aniq agent ko'rsatilmasa) va avtomatik xabarlarda ishlatiladi.
 */
export function LocalSmsSettings() {
  const [enabled, setEnabled] = useState(false)
  const [defaultAgentId, setDefaultAgentId] = useState('')
  const [delaySeconds, setDelaySeconds] = useState(0)
  const [agents, setAgents] = useState<CtiAgent[]>([])
  const [loading, setLoading] = useState(true)
  const [status, setStatus] = useState<'idle' | 'saving' | 'saved'>('idle')
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    Promise.all([getLocalSmsSettings(), getCtiAgents()])
      .then(([cfg, ag]) => {
        setEnabled(cfg.enabled)
        setDefaultAgentId(cfg.defaultAgentId ?? '')
        setDelaySeconds(cfg.delaySeconds)
        setAgents(ag)
      })
      .finally(() => setLoading(false))
  }, [])

  const onSubmit = async (e: React.FormEvent) => {
    e.preventDefault()
    setStatus('saving')
    setError(null)
    try {
      const saved = await saveLocalSmsSettings({ enabled, defaultAgentId: defaultAgentId || null, delaySeconds })
      setEnabled(saved.enabled)
      setDefaultAgentId(saved.defaultAgentId ?? '')
      setDelaySeconds(saved.delaySeconds)
      setStatus('saved')
      setTimeout(() => setStatus('idle'), 2000)
    } catch (e: unknown) {
      setStatus('idle')
      setError(
        (e as { response?: { data?: { message?: string } } })?.response?.data?.message || "Saqlab bo'lmadi.",
      )
    }
  }

  if (loading) return <Loader label="Yuklanmoqda..." />

  return (
    <Card
      title={
        <span className="flex flex-wrap items-center gap-2">
          <MessageSquareText className="h-4 w-4 text-brand-600" /> Local SMS
          {enabled ? (
            <Badge tone="green">
              <CheckCircle2 className="h-3.5 w-3.5" /> Yoqilgan
            </Badge>
          ) : (
            <Badge tone="default">
              <XCircle className="h-3.5 w-3.5" /> O'chirilgan
            </Badge>
          )}
        </span>
      }
    >
      <p className="mb-4 text-sm text-slate-400">
        Yoqilsa, SMS yuborish oynalarida (Xabar yuborish, Lidga SMS, Avto xabarlar) "Eskiz" bilan bir
        qatorda <b>Local</b> tanlovi paydo bo'ladi — SMS Eskiz shlyuzi o'rniga tanlangan <b>Local Call</b>{' '}
        agent telefonining SIM-kartasidan jo'natiladi. Standart agent — aniq agent ko'rsatilmagan
        yuborishlarda (va barcha avtomatik xabarlarda) ishlatiladi.
      </p>

      <form onSubmit={onSubmit} className="space-y-4">
        <label className="flex items-center gap-2.5">
          <input
            type="checkbox"
            checked={enabled}
            onChange={(e) => setEnabled(e.target.checked)}
            className="h-4 w-4 accent-brand-600"
          />
          <span className="text-sm font-medium text-slate-700">Local SMS'ni yoqish</span>
        </label>

        <div>
          <label className="mb-1 block text-sm font-medium text-slate-700">Standart agent</label>
          {agents.length === 0 ? (
            <p className="text-xs text-slate-400">
              Agentlar yo'q — avval "Call Center → Local Call → Agentlar"da agent qo'shing.
            </p>
          ) : (
            <Select value={defaultAgentId} onChange={(e) => setDefaultAgentId(e.target.value)} className="max-w-sm">
              <option value="">— tanlanmagan —</option>
              {agents.map((a) => (
                <option key={a.id} value={a.id}>
                  {a.isOnline ? '● ' : ''}
                  {a.displayName} {a.isOnline ? '(onlayn)' : '(oflayn)'}
                </option>
              ))}
            </Select>
          )}
        </div>

        <div>
          <label className="mb-1 block text-sm font-medium text-slate-700">
            Ikkita SMS orasidagi kutish (soniya)
          </label>
          <input
            type="number"
            min={0}
            max={300}
            value={delaySeconds}
            onChange={(e) => setDelaySeconds(Math.max(0, Math.min(300, Number(e.target.value) || 0)))}
            className="max-w-[140px] rounded-lg border border-slate-200 px-3 py-2 text-sm text-slate-700 outline-none focus:border-brand-400"
          />
          <p className="mt-1 text-xs text-slate-400">
            Massaviy Local SMS yuborilganda (Xabar yuborish, Lidlarga ommaviy SMS, avto-xabarlar)
            har bir SMS orasida shuncha soniya kutiladi — agent telefoni/operator haddan tashqari
            yuklanmasligi uchun. 0 = kutishsiz.
          </p>
        </div>

        {error && <p className="text-sm font-medium text-red-600">{error}</p>}
        <div className="flex items-center gap-3">
          <Button type="submit" disabled={status === 'saving'}>
            {status === 'saving' ? 'Saqlanmoqda...' : 'Saqlash'}
          </Button>
          {status === 'saved' && (
            <span className="inline-flex items-center gap-1 text-sm font-medium text-emerald-600">
              <Check className="h-4 w-4" /> Saqlandi
            </span>
          )}
        </div>
      </form>
    </Card>
  )
}
