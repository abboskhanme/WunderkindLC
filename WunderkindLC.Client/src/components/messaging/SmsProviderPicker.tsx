import { useEffect, useState } from 'react'
import { getCtiAgents, type CtiAgent } from '@/api/services/cti'
import { getSmsStatus, type SmsProvider } from '@/api/services/messages'
import { cn } from '@/lib/utils'

const select =
  'w-full rounded-lg border border-slate-200 bg-white px-3 py-2 text-sm text-slate-800 outline-none transition-colors focus:border-brand-400'

interface Props {
  provider: SmsProvider
  onProviderChange: (p: SmsProvider) => void
  agentId: string
  onAgentChange: (id: string) => void
  className?: string
  /** false — agent tanlovi ko'rsatilmaydi (avtomatik/fon xabarlar uchun — har doim standart agent
   * ishlatiladi, tanlaydigan admin yo'q). Default true. */
  allowAgentOverride?: boolean
}

/**
 * SMS qaysi orqali yuborilishini tanlash: Eskiz (standart) | Local (CTI agent telefonining SIM-kartasi).
 * Local SMS tizimda umuman yoqilmagan bo'lsa (Sozlamalar → Xabar kanallari → SMS) hech narsa
 * ko'rsatmaydi — faqat Eskiz ishlaydi (mavjud xulq o'zgarmaydi).
 */
export function SmsProviderPicker({
  provider, onProviderChange, agentId, onAgentChange, className, allowAgentOverride = true,
}: Props) {
  const [localEnabled, setLocalEnabled] = useState(false)
  const [defaultAgentId, setDefaultAgentId] = useState('')
  const [agents, setAgents] = useState<CtiAgent[]>([])
  const [loaded, setLoaded] = useState(false)

  useEffect(() => {
    Promise.all([getSmsStatus(), getCtiAgents()])
      .then(([status, ag]) => {
        setLocalEnabled(status.localEnabled)
        setDefaultAgentId(status.localDefaultAgentId ?? '')
        setAgents(ag)
      })
      .catch(() => {})
      .finally(() => setLoaded(true))
  }, [])

  if (!loaded || !localEnabled) return null

  return (
    <div className={cn('space-y-1.5', className)}>
      <div className="text-sm font-medium text-slate-600">Qayerdan yuboriladi</div>
      <div className="tabs inline-flex">
        <button
          type="button"
          onClick={() => onProviderChange('eskiz')}
          className={cn('tab', provider === 'eskiz' && 'active')}
        >
          SMS (Eskiz)
        </button>
        <button
          type="button"
          onClick={() => onProviderChange('local')}
          className={cn('tab', provider === 'local' && 'active')}
        >
          Local (agent telefonidan)
        </button>
      </div>
      {provider === 'local' && !allowAgentOverride && (
        <p className="text-xs text-slate-400">
          Standart agent orqali yuboriladi (Sozlamalar → Xabar kanallari → SMS).
        </p>
      )}
      {provider === 'local' && allowAgentOverride && (
        <select value={agentId} onChange={(e) => onAgentChange(e.target.value)} className={select}>
          <option value="">
            {defaultAgentId ? '— standart agent —' : '— agent tanlanmagan —'}
          </option>
          {agents.map((a) => (
            <option key={a.id} value={a.id}>
              {a.isOnline ? '● ' : ''}
              {a.displayName} {a.isOnline ? '(onlayn)' : '(oflayn)'}
            </option>
          ))}
        </select>
      )}
    </div>
  )
}
