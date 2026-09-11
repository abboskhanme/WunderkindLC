import { useEffect, useState } from 'react'
import { Check, CheckCircle2, XCircle } from 'lucide-react'
import { getFirebaseSettings, saveFirebaseSettings, type FirebaseConfig } from '@/api/services/settings'
import { Card } from '@/components/ui/Card'
import { Badge } from '@/components/ui/Badge'
import { Button } from '@/components/ui/Button'
import { Input, Textarea } from '@/components/ui/Input'
import { Loader } from '@/components/ui/Loader'
import { EnvSecretField } from '@/components/settings/EnvSecretField'
import type { EnvSecret } from '@/api/services/settings'

/**
 * Firebase push sozlamasi — ikki qatlam:
 *  1) Service Account JSON — server FCM'ga push YUBORISHI uchun (MAXFIY: faqat serverdagi
 *     `.env` da — FCM_SERVICE_ACCOUNT_JSON; bazada saqlanmaydi va UI'dan kiritilmaydi).
 *  2) Web app config + VAPID kaliti — brauzer/PWA token OLISHI uchun (ommaviy).
 * Native (Flutter) ilova tokenni o'zi oladi; web/PWA esa web config bilan Firebase JS SDK orqali.
 */
export function FirebaseSettings() {
  const [serviceAccount, setServiceAccount] = useState<EnvSecret | null>(null)
  const [webJson, setWebJson] = useState('')
  const [vapid, setVapid] = useState('')
  const [configured, setConfigured] = useState(false)
  const [webConfigured, setWebConfigured] = useState(false)
  const [loading, setLoading] = useState(true)
  const [status, setStatus] = useState<'idle' | 'saving' | 'saved'>('idle')
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    getFirebaseSettings()
      .then((c: FirebaseConfig) => {
        setServiceAccount(c.serviceAccount ?? null)
        setWebJson(c.webConfigJson)
        setVapid(c.vapidKey)
        setConfigured(c.configured)
        setWebConfigured(c.webConfigured)
      })
      .finally(() => setLoading(false))
  }, [])

  const onSubmit = async (e: React.FormEvent) => {
    e.preventDefault()
    setStatus('saving')
    setError(null)
    try {
      const saved = await saveFirebaseSettings({
        webConfigJson: (webJson ?? '').trim(),
        vapidKey: (vapid ?? '').trim(),
      })
      setServiceAccount(saved.serviceAccount ?? null)
      setWebJson(saved.webConfigJson)
      setVapid(saved.vapidKey)
      setConfigured(saved.configured)
      setWebConfigured(saved.webConfigured)
      setStatus('saved')
      setTimeout(() => setStatus('idle'), 2000)
    } catch (e: unknown) {
      setStatus('idle')
      const msg =
        (e as { response?: { data?: { message?: string } } })?.response?.data?.message ||
        "Saqlab bo'lmadi — kiritilgan ma'lumotlarni tekshiring."
      setError(msg)
    }
  }

  if (loading) return <Loader label="Yuklanmoqda..." />

  return (
    <Card
      title={
        <span className="flex flex-wrap items-center gap-2">
          Push (Firebase)
          {configured ? (
            <Badge tone="green">
              <CheckCircle2 className="h-3.5 w-3.5" /> Ilova (native)
            </Badge>
          ) : (
            <Badge tone="default">
              <XCircle className="h-3.5 w-3.5" /> Ilova sozlanmagan
            </Badge>
          )}
          {webConfigured ? (
            <Badge tone="green">
              <CheckCircle2 className="h-3.5 w-3.5" /> Web/PWA
            </Badge>
          ) : (
            <Badge tone="default">
              <XCircle className="h-3.5 w-3.5" /> Web/PWA sozlanmagan
            </Badge>
          )}
        </span>
      }
    >
      <p className="mb-4 text-sm text-slate-400">
        Bildirishnoma (push) ikki yo'l bilan yetadi: <b>Ilova</b> (Flutter APK — token native olinadi) va{' '}
        <b>Web/PWA</b> (brauzer/telefon ekraniga o'rnatilgan sayt — token quyidagi web config bilan olinadi).
        Ikkalasi ham <b>bitta Firebase loyihadan</b> bo'lishi shart. Serverdan push yuborish uchun esa{' '}
        <b>Service Account JSON</b> zarur (u ikkala yo'lga ham xizmat qiladi).
      </p>

      <form onSubmit={onSubmit} className="max-w-2xl space-y-6">
        {/* 1) Server → push yuboradi (maxfiy kalit — .env) */}
        <EnvSecretField
          label="Service account (JSON) — server push yuborishi uchun"
          secret={serviceAccount}
          sample='{"type":"service_account","project_id":"...","private_key":"..."}'
          hint={
            <>
              Firebase Console → Project Settings → Service accounts → "Generate new private key" →
              yuklab olingan JSON faylni BIR QATORGA aylantirib .env ga qo'ying.
            </>
          }
        />

        <div className="border-t border-slate-100 pt-5">
          <p className="mb-3 text-sm font-semibold text-slate-700">Web / PWA push (brauzer tokeni uchun)</p>

          {/* 2) Web app config */}
          <div className="mb-4">
            <label className="mb-1 block text-sm font-medium text-slate-700">Web app config (JSON)</label>
            <p className="mb-2 text-xs text-slate-400">
              Firebase Console → Project Settings → General → "Your apps" → Web app (<code>&lt;/&gt;</code>) →
              SDK setup and configuration → <b>Config</b>. <code>firebaseConfig</code> obyektini (apiKey, projectId,
              messagingSenderId, appId...) JSON ko'rinishida qo'ying. Ommaviy — maxfiy emas.
            </p>
            <Textarea
              value={webJson}
              onChange={(e) => setWebJson(e.target.value)}
              placeholder='{ "apiKey": "...", "authDomain": "...", "projectId": "...", "messagingSenderId": "...", "appId": "..." }'
              spellCheck={false}
              className="h-36 font-mono text-xs"
            />
          </div>

          {/* 3) VAPID key */}
          <div>
            <label className="mb-1 block text-sm font-medium text-slate-700">Web Push sertifikati (VAPID key)</label>
            <p className="mb-2 text-xs text-slate-400">
              Firebase Console → Project Settings → Cloud Messaging → "Web configuration" → Web Push certificates →
              Key pair (ochiq kalit). Web/PWA push shu kalitsiz ishlamaydi.
            </p>
            <Input
              value={vapid}
              onChange={(e) => setVapid(e.target.value)}
              placeholder="B*** (Web Push certificate ochiq kaliti)"
              spellCheck={false}
              className="font-mono text-xs"
            />
          </div>
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
