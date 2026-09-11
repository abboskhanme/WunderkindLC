import { useEffect, useState } from 'react'
import { CheckCircle2, XCircle } from 'lucide-react'
import { getAzureSpeechSettings, type AzureSpeechConfig } from '@/api/services/settings'
import { Card } from '@/components/ui/Card'
import { Badge } from '@/components/ui/Badge'
import { Input } from '@/components/ui/Input'
import { Loader } from '@/components/ui/Loader'
import { EnvSecretField } from '@/components/settings/EnvSecretField'

/**
 * Speaking (Azure Pronunciation Assessment) sozlamasi — kalit + region.
 * Ikkalasi ham serverdagi `.env` faylidan (AZURE_SPEECH_KEY / AZURE_SPEECH_REGION) o'qiladi;
 * bazada saqlanmaydi va UI'dan kiritilmaydi. Bu sahifa faqat holatni ko'rsatadi.
 */
export function AzureSpeechSettings() {
  const [cfg, setCfg] = useState<AzureSpeechConfig | null>(null)
  const [loading, setLoading] = useState(true)

  useEffect(() => {
    getAzureSpeechSettings()
      .then(setCfg)
      .finally(() => setLoading(false))
  }, [])

  if (loading) return <Loader label="Yuklanmoqda..." />

  const configured = !!cfg?.configured

  return (
    <Card
      title={
        <span className="flex flex-wrap items-center gap-2">
          Speaking (Azure)
          {configured ? (
            <Badge tone="green">
              <CheckCircle2 className="h-3.5 w-3.5" /> Sozlangan
            </Badge>
          ) : (
            <Badge tone="default">
              <XCircle className="h-3.5 w-3.5" /> Sozlanmagan
            </Badge>
          )}
        </span>
      }
    >
      <p className="mb-4 text-sm text-slate-400">
        Speaking mashqlari <b>Azure Speech — Pronunciation Assessment</b> orqali baholanadi: o'quvchi
        gapiradi, xizmat nutqni matnga o'giradi va talaffuzni (aniqlik, ravonlik, to'liqlik, ohang) baholab,
        avtomatik ball qo'yadi. Kalit/region berilmasa, speaking baholanmaydi. Kalit{' '}
        <b>Azure portal → Speech service → Keys and Endpoint</b> bo'limidan olinadi.
      </p>

      <div className="max-w-xl space-y-4">
        <EnvSecretField label="Azure Speech maxfiy kaliti" secret={cfg?.key} sample="<KEY 1>" />
        <div>
          <label className="mb-1 block text-sm font-medium text-slate-700">Region</label>
          <p className="mb-2 text-xs text-slate-400">
            Serverdagi <code className="rounded bg-slate-100 px-1">{cfg?.regionEnvKey ?? 'AZURE_SPEECH_REGION'}</code>{' '}
            (.env) qiymati. Masalan: eastus, westeurope, southeastasia.
          </p>
          <Input
            value={cfg?.region || '—'}
            readOnly
            disabled
            className="max-w-[220px] font-mono text-xs"
          />
        </div>
      </div>
    </Card>
  )
}
