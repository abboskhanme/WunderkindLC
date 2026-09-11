import { useEffect, useState } from 'react'
import { Navigate, useParams, useSearchParams } from 'react-router-dom'
import { Check, Plus, Trash2 } from 'lucide-react'
import type { AbsenceReason } from '@/types'
import {
  getSettings,
  saveAbsenceReasons,
} from '@/api/services/settings'
import { cn, uid } from '@/lib/utils'
import { Card } from '@/components/ui/Card'
import { Button } from '@/components/ui/Button'
import { Loader } from '@/components/ui/Loader'
import { PageHeader } from '@/components/ui/PageHeader'
import { SchoolSettings } from './SchoolSettings'
import { ChannelsSettings } from './ChannelsSettings'
import { BackupSettings } from './BackupSettings'
import { ApkSettings } from './ApkSettings'
import { AzureSpeechSettings } from './AzureSpeechSettings'
import { GeminiSettings } from './GeminiSettings'
import { TurnstileSettings } from './TurnstileSettings'
import { CameraSettings } from './CameraSettings'
import { CheckSettings } from './CheckSettings'
import { PosthogSettings } from './PosthogSettings'

type Status = 'idle' | 'saving' | 'saved'

const control =
  'rounded-lg border border-slate-200 bg-white px-3 py-2 text-sm text-slate-700 outline-none transition-colors focus:border-brand-400 focus:ring-2 focus:ring-brand-100'

const sectionTitles: Record<string, string> = {
  reasons: 'Davomat sabablari',
  school: "Markaz ma'lumotlari",
  channels: 'Xabar kanallari',
  backup: 'Zaxira nusxa',
  apk: 'Mobil ilova (APK)',
  'azure-speech': 'Speaking (Azure)',
  gemini: 'AI Tahlil (Gemini)',
  check: "To'lov cheki",
  turnstile: 'Turniket integratsiya',
  cameras: 'Kamera integratsiya',
  posthog: 'PostHog Analitika',
}

/** Eski alohida sozlama sahifalari — endi "Xabar kanallari" ichidagi tab. */
const legacyChannelTab: Record<string, 'sms' | 'telegram' | 'firebase'> = {
  eskiz: 'sms',
  telegram: 'telegram',
  firebase: 'firebase',
}

function channelTabFromQuery(tab: string | null): 'sms' | 'telegram' | 'firebase' {
  return tab === 'telegram' || tab === 'firebase' ? tab : 'sms'
}

export function SettingsPage() {
  const { section = 'school' } = useParams()
  const [searchParams] = useSearchParams()
  const [reasons, setReasons] = useState<AbsenceReason[]>([])
  const [loading, setLoading] = useState(true)
  const [rStatus, setRStatus] = useState<Status>('idle')

  useEffect(() => {
    getSettings()
      .then((s) => {
        setReasons(s.absenceReasons)
      })
      .finally(() => setLoading(false))
  }, [])

  const updateReason = (i: number, field: 'name' | 'short', value: string) =>
    setReasons((prev) => prev.map((r, idx) => (idx === i ? { ...r, [field]: value } : r)))

  const toggleReasonLate = (i: number) =>
    setReasons((prev) => prev.map((r, idx) => (idx === i ? { ...r, isLate: !r.isLate } : r)))

  const addReason = () =>
    setReasons((prev) => [...prev, { id: uid(), name: '', short: '', isLate: false }])

  const removeReason = (i: number) =>
    setReasons((prev) => prev.filter((_, idx) => idx !== i))

  const onSaveReasons = async () => {
    setRStatus('saving')
    try {
      await saveAbsenceReasons(reasons.filter((r) => (r.name ?? '').trim()))
      setRStatus('saved')
      setTimeout(() => setRStatus('idle'), 2000)
    } catch {
      setRStatus('idle')
    }
  }

  if (section === 'reminders') return <Navigate to="/admin/messages" replace />
  if (section in legacyChannelTab) {
    return <Navigate to={`/admin/settings/channels?tab=${legacyChannelTab[section]}`} replace />
  }

  return (
    <div>
      <PageHeader title="Sozlamalar" sub={sectionTitles[section] ?? 'Sozlamalar'} />

      {loading ? (
        <Loader label="Yuklanmoqda..." />
      ) : (
        <div className={cn('space-y-6', section !== 'check' && 'max-w-3xl')}>
          {/* Davomat sabablari */}
          {section === 'reasons' && (
          <Card
            title="Davomat sabablari"
            sub="Davomatda ishlatiladigan sabablar ro'yxati."
            actions={<SaveButton status={rStatus} onClick={onSaveReasons} />}
          >
            <div className="space-y-2">
              {reasons.map((r, i) => (
                <div key={r.id} className="flex flex-wrap items-center gap-2">
                  <input
                    value={r.name}
                    onChange={(e) => updateReason(i, 'name', e.target.value)}
                    placeholder="Sabab nomi (masalan: Kasal)"
                    className={cn(control, 'flex-1 min-w-[180px]')}
                  />
                  <input
                    value={r.short}
                    onChange={(e) => updateReason(i, 'short', e.target.value)}
                    placeholder="Belgi"
                    maxLength={3}
                    className={cn(control, 'w-20 text-center')}
                  />
                  <label
                    className="inline-flex cursor-pointer items-center gap-1.5 whitespace-nowrap text-sm text-slate-600"
                    title="Kech keldi — yo'qlik emas, baho qo'ysa bo'ladi"
                  >
                    <input
                      type="checkbox"
                      checked={r.isLate}
                      onChange={() => toggleReasonLate(i)}
                      className="h-4 w-4 rounded border-slate-300 accent-brand-600"
                    />
                    Kech qolish
                  </label>
                  <button
                    type="button"
                    onClick={() => removeReason(i)}
                    title="O'chirish"
                    className="rounded-lg p-2 text-slate-400 transition-colors hover:bg-red-50 hover:text-red-600"
                  >
                    <Trash2 className="h-4 w-4" />
                  </button>
                </div>
              ))}
              <button
                type="button"
                onClick={addReason}
                className="inline-flex items-center gap-1 text-sm font-medium text-brand-600 hover:text-brand-700"
              >
                <Plus className="h-4 w-4" /> Sabab qo'shish
              </button>
            </div>
          </Card>
          )}

          {/* Markaz ma'lumotlari */}
          {section === 'school' && <SchoolSettings />}

          {/* Xabar kanallari: SMS (Eskiz) / Telegram bot / Push (Firebase) */}
          {section === 'channels' && (
            <ChannelsSettings initialTab={channelTabFromQuery(searchParams.get('tab'))} />
          )}

          {/* Zaxira nusxa (Telegram backup) */}
          {section === 'backup' && <BackupSettings />}

          {/* Mobil ilova (APK) */}
          {section === 'apk' && <ApkSettings />}

          {/* Speaking (Azure) */}
          {section === 'azure-speech' && <AzureSpeechSettings />}

          {/* AI Tahlil (Gemini) */}
          {section === 'gemini' && <GeminiSettings />}

          {/* Turniket / FaceID integratsiya */}
          {section === 'turnstile' && <TurnstileSettings />}

          {/* Kamera (videokuzatuv) integratsiya */}
          {section === 'cameras' && <CameraSettings />}

          {section === 'check' && <CheckSettings />}

          {/* PostHog analytics */}
          {section === 'posthog' && <PosthogSettings />}
        </div>
      )}
    </div>
  )
}

function SaveButton({ status, onClick }: { status: Status; onClick: () => void }) {
  if (status === 'saved') {
    return (
      <span className="inline-flex items-center gap-1 text-sm font-medium text-emerald-600">
        <Check className="h-4 w-4" /> Saqlandi
      </span>
    )
  }
  return (
    <Button onClick={onClick} disabled={status === 'saving'}>
      {status === 'saving' ? 'Saqlanmoqda...' : 'Saqlash'}
    </Button>
  )
}
