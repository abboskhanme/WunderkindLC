import { useEffect, useState } from 'react'
import type { FormEvent } from 'react'
import { Check, CheckCircle2, XCircle, Info } from 'lucide-react'
import {
  getTurnstileSettings,
  saveTurnstileSettings,
  type TurnstileConfig,
  type TeacherDeviceMap,
} from '@/api/services/settings'
import { Card } from '@/components/ui/Card'
import { Badge } from '@/components/ui/Badge'
import { Button } from '@/components/ui/Button'
import { Input, Select, Time24Input } from '@/components/ui/Input'
import { Loader } from '@/components/ui/Loader'
import { EnvSecretField } from '@/components/settings/EnvSecretField'

const control =
  'rounded-lg border border-slate-200 bg-white px-3 py-2 text-sm text-slate-700 outline-none transition-colors focus:border-brand-400 focus:ring-2 focus:ring-brand-100'

/**
 * Turniket / FaceID integratsiya sozlamasi. Qurilma (Hikvision/ZKTeco) o'tish hodisalaridan
 * Qurilma LOGIN/PAROLI serverdagi `.env` faylida (TURNSTILE_USERNAME / TURNSTILE_PASSWORD) —
 * bazada saqlanmaydi va UI'dan kiritilmaydi.
 * o'qituvchilar davomati AVTOMATIK yuklanadi (natija — "O'qituvchilar → Davomat" dashboardida).
 */
export function TurnstileSettings() {
  const [cfg, setCfg] = useState<TurnstileConfig | null>(null)
  const [loading, setLoading] = useState(true)
  const [status, setStatus] = useState<'idle' | 'saving' | 'saved'>('idle')

  useEffect(() => {
    getTurnstileSettings()
      .then(setCfg)
      .finally(() => setLoading(false))
  }, [])

  const set = <K extends keyof TurnstileConfig>(key: K, value: TurnstileConfig[K]) =>
    setCfg((prev) => (prev ? { ...prev, [key]: value } : prev))

  const setTeacherDevice = (teacherId: string, deviceUserId: string) =>
    setCfg((prev) =>
      prev
        ? { ...prev, teachers: prev.teachers.map((t) => (t.teacherId === teacherId ? { ...t, deviceUserId } : t)) }
        : prev,
    )

  const onSubmit = async (e: FormEvent) => {
    e.preventDefault()
    if (!cfg) return
    setStatus('saving')
    try {
      const saved = await saveTurnstileSettings({
        enabled: cfg.enabled,
        vendor: cfg.vendor,
        host: (cfg.host ?? '').trim(),
        port: cfg.port,
        workStartTime: cfg.workStartTime,
        lateGraceMinutes: cfg.lateGraceMinutes,
        teachers: cfg.teachers,
      })
      setCfg(saved)
      setStatus('saved')
      setTimeout(() => setStatus('idle'), 2000)
    } catch {
      setStatus('idle')
    }
  }

  if (loading || !cfg) return <Loader label="Yuklanmoqda..." />

  const mapped = cfg.teachers.filter((t: TeacherDeviceMap) => (t.deviceUserId ?? '').trim()).length

  return (
    <form onSubmit={onSubmit} className="space-y-6">
      <Card
        title={
          <span className="flex items-center gap-2">
            Turniket integratsiya
            {cfg.enabled ? (
              <Badge tone="green">
                <CheckCircle2 className="h-3.5 w-3.5" /> Yoqilgan
              </Badge>
            ) : (
              <Badge tone="default">
                <XCircle className="h-3.5 w-3.5" /> O'chiq
              </Badge>
            )}
          </span>
        }
      >
        <p className="mb-4 text-sm text-slate-400">
          Turniket/FaceID qurilmasidan o'qituvchilar davomati avtomatik yuklanadi. Natija{' '}
          <span className="font-medium text-slate-500">O'qituvchilar → Davomat</span> bo'limida (dashboard)
          ko'rinadi — kim keldi, soat nechada, kechikdimi.
        </p>

        <label className="mb-4 inline-flex cursor-pointer items-center gap-2 text-sm font-medium text-slate-700">
          <input
            type="checkbox"
            checked={cfg.enabled}
            onChange={(e) => set('enabled', e.target.checked)}
            className="h-4 w-4 rounded border-slate-300 accent-brand-600"
          />
          Integratsiyani yoqish
        </label>

        <div className="grid max-w-2xl grid-cols-1 gap-4 sm:grid-cols-2">
          <Select
            label="Qurilma turi"
            value={cfg.vendor}
            onChange={(e) => set('vendor', e.target.value)}
          >
            <option value="hikvision">Hikvision (ISAPI)</option>
            <option value="zkteco">ZKTeco</option>
          </Select>
          <Input
            label="Qurilma manzili (IP / host)"
            placeholder="192.168.1.64"
            value={cfg.host}
            onChange={(e) => set('host', e.target.value)}
            autoComplete="off"
          />
          <Input
            label="Port"
            type="number"
            placeholder="80"
            value={String(cfg.port)}
            onChange={(e) => set('port', Number(e.target.value) || 0)}
            autoComplete="off"
          />
          <Input
            label="Login (.env: TURNSTILE_USERNAME)"
            value={cfg.username || '—'}
            readOnly
            disabled
            autoComplete="off"
          />
        </div>

        <div className="mt-4 max-w-2xl">
          <EnvSecretField
            label="Qurilma login/paroli"
            secret={cfg.credentials}
            sample="<qurilma paroli>"
            hint="Login uchun TURNSTILE_USERNAME, parol uchun TURNSTILE_PASSWORD qatorlari."
          />
        </div>
      </Card>

      <Card title="Kechikish qoidasi">
        <p className="mb-4 text-sm text-slate-400">
          Kelgan vaqt — <b>ish boshlanish vaqti</b> va o'sha kungi <b>birinchi dars</b>dan (qaysi biri erta
          bo'lsa) + grace dan keyin bo'lsa "kechikdi" deb belgilanadi. Umuman kelmasa — "kelmadi".
        </p>
        <div className="flex flex-wrap items-end gap-4">
          <label className="flex flex-col gap-1 text-sm">
            <span className="font-medium text-slate-600">Ish boshlanishi</span>
            <Time24Input value={cfg.workStartTime} onChange={(v) => set('workStartTime', v)} />
          </label>
          <label className="flex flex-col gap-1 text-sm">
            <span className="font-medium text-slate-600">Grace (daqiqa)</span>
            <input
              type="number"
              min={0}
              value={cfg.lateGraceMinutes}
              onChange={(e) => set('lateGraceMinutes', Number(e.target.value) || 0)}
              className={`${control} w-28`}
            />
          </label>
        </div>
      </Card>

      <Card
        title="O'qituvchi qurilma ID lari"
        actions={
          <span className="font-mono text-xs text-slate-400">
            Moslashtirilgan: {mapped} / {cfg.teachers.length}
          </span>
        }
      >
        <p className="mb-3 flex items-start gap-1.5 text-sm text-slate-400">
          <Info className="mt-0.5 h-4 w-4 shrink-0" />
          Qurilmadagi xodim ID'sini (personId / employeeNo) shu yerga kiriting — davomat hodisalari shu ID
          orqali o'qituvchiga bog'lanadi.
        </p>
        <div className="max-h-80 space-y-1.5 overflow-y-auto">
          {cfg.teachers.map((t) => (
            <div key={t.teacherId} className="flex items-center gap-3">
              <span className="flex-1 truncate text-sm text-slate-700">{t.fullName}</span>
              <input
                value={t.deviceUserId}
                onChange={(e) => setTeacherDevice(t.teacherId, e.target.value)}
                placeholder="Qurilma ID"
                className={`${control} w-40`}
              />
            </div>
          ))}
          {cfg.teachers.length === 0 && <p className="text-sm text-slate-400">O'qituvchi yo'q</p>}
        </div>
      </Card>

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
  )
}
