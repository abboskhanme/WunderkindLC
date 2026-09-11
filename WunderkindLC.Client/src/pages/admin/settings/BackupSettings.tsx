import { useEffect, useState } from 'react'
import type { FormEvent } from 'react'
import { Check, CheckCircle2, XCircle, Database, Send, Clock, AlertTriangle } from 'lucide-react'
import {
  getTelegramSettings,
  getTelegramBackupConfig,
  saveTelegramBackupConfig,
  testTelegramBackup,
  runTelegramBackup,
  type TelegramBackupConfig,
} from '@/api/services/settings'
import { Card } from '@/components/ui/Card'
import { Button } from '@/components/ui/Button'
import { Loader } from '@/components/ui/Loader'

/** Axios xato javobidan backend xabarini ajratib oladi ({ message } yoki { error }). */
function backendMessage(e: unknown): string {
  const data = (e as { response?: { data?: { message?: string; error?: string } } })?.response?.data
  return data?.message || data?.error || ''
}

function fmtDateTime(iso?: string): string {
  if (!iso) return ''
  const d = new Date(iso)
  const pad = (n: number) => String(n).padStart(2, '0')
  return `${pad(d.getDate())}.${pad(d.getMonth() + 1)}.${d.getFullYear()} ${pad(d.getHours())}:${pad(d.getMinutes())}`
}

/**
 * "Zaxira nusxa" sozlamasi — Telegram orqali kunlik DB backup.
 * (Ilgari Telegram bot sozlamasi ichida edi — alohida bo'limga ajratildi.)
 */
export function BackupSettings() {
  const [cfg, setCfg] = useState<TelegramBackupConfig>({
    adminChatId: '',
    scheduleHour: 21,
    scheduleMinute: 0,
    enabled: false,
  })
  const [loading, setLoading] = useState(true)
  const [tgConfigured, setTgConfigured] = useState(true)
  const [status, setStatus] = useState<'idle' | 'saving' | 'saved'>('idle')
  const [testing, setTesting] = useState(false)
  const [running, setRunning] = useState(false)
  const [testResult, setTestResult] = useState<{ success: boolean; message: string } | null>(null)
  const [chatIdError, setChatIdError] = useState('')

  useEffect(() => {
    Promise.all([
      getTelegramBackupConfig().then(setCfg),
      getTelegramSettings()
        .then((t) => setTgConfigured(t.configured))
        .catch(() => setTgConfigured(false)),
    ]).finally(() => setLoading(false))
  }, [])

  const validateChatId = (val: string) => {
    const v = (val ?? '').trim()
    if (!v) return ''
    if (!/^-?\d{9,}$/.test(v)) return "Chat ID faqat raqamdan iborat bo'lishi va kamida 9 ta raqam bo'lishi kerak"
    return ''
  }

  const onChatIdChange = (val: string) => {
    setCfg((p) => ({ ...p, adminChatId: val }))
    setChatIdError(validateChatId(val))
    setTestResult(null)
  }

  const onSave = async (e: FormEvent) => {
    e.preventDefault()
    const err = validateChatId(cfg.adminChatId)
    if (err) { setChatIdError(err); return }
    setStatus('saving')
    setTestResult(null)
    try {
      const updated = await saveTelegramBackupConfig(cfg)
      setCfg({ ...updated, adminChatId: updated.adminChatId ?? '' })
      setStatus('saved')
      setTimeout(() => setStatus('idle'), 2000)
    } catch (e) {
      setStatus('idle')
      setTestResult({ success: false, message: backendMessage(e) || 'Saqlashda xatolik' })
    }
  }

  const onTest = async () => {
    const err = validateChatId(cfg.adminChatId)
    if (err) { setChatIdError(err); return }
    if (!(cfg.adminChatId ?? '').trim()) { setChatIdError("Chat ID kiriting"); return }
    setTesting(true)
    setTestResult(null)
    try {
      const res = await testTelegramBackup()
      setTestResult(res)
    } catch (e) {
      setTestResult({ success: false, message: backendMessage(e) || "So'rov yuborishda xatolik" })
    } finally {
      setTesting(false)
    }
  }

  // Backupni HOZIR yuborish (markaz ma'lumotlari JSON qilib Telegram orqali adminga).
  const onRunBackup = async () => {
    const err = validateChatId(cfg.adminChatId)
    if (err) { setChatIdError(err); return }
    if (!(cfg.adminChatId ?? '').trim()) { setChatIdError("Chat ID kiriting"); return }
    setRunning(true)
    setTestResult(null)
    try {
      const res = await runTelegramBackup()
      setTestResult(res)
    } catch (e) {
      setTestResult({ success: false, message: backendMessage(e) || "Backup yuborishda xatolik" })
    } finally {
      setRunning(false)
    }
  }

  if (loading) return <Loader label="Yuklanmoqda..." />

  const canSave = !chatIdError && status !== 'saving'

  return (
    <Card
      title={
        <span className="flex items-center gap-2">
          <Database className="h-4 w-4 text-brand-600" /> Zaxira nusxa (Telegram backup)
        </span>
      }
    >
      <p className="mb-4 text-sm text-slate-400">
        Har kuni belgilangan vaqtda ma'lumotlar bazasi zaxirasi Telegram orqali admin chatiga yuboriladi.
        Chat ID'ni bilish uchun Telegramda{' '}
        <span className="font-medium text-slate-500">@userinfobot</span>'ga /start yuboring.
      </p>

      {!tgConfigured && (
        <div className="mb-4 flex items-start gap-2 rounded-xl border border-amber-200 bg-amber-50 px-3 py-2 text-sm text-amber-800">
          <AlertTriangle className="mt-0.5 h-4 w-4 shrink-0" />
          <p>
            Telegram bot sozlanmagan — backup yuborish uchun avval "Sozlamalar → Xabar kanallari →
            Telegram" bo'limida bot tokenini kiriting.
          </p>
        </div>
      )}

      <form onSubmit={onSave} className="max-w-2xl space-y-4">
        {/* Enabled toggle */}
        <div className="flex items-center justify-between rounded-lg border border-slate-200 px-4 py-3">
          <div>
            <p className="text-sm font-medium text-slate-700">Backup yuborish</p>
            <p className="text-xs text-slate-400">Yoqilsa — har kuni belgilangan soatda yuboriladi</p>
          </div>
          <button
            type="button"
            role="switch"
            aria-checked={cfg.enabled}
            onClick={() => setCfg((p) => ({ ...p, enabled: !p.enabled }))}
            className={`relative inline-flex h-6 w-11 shrink-0 cursor-pointer items-center rounded-full transition-colors focus-visible:outline-none ${
              cfg.enabled ? 'bg-brand-600' : 'bg-slate-200'
            }`}
          >
            <span
              className={`pointer-events-none inline-block h-4 w-4 rounded-full bg-white shadow-sm ring-0 transition-transform ${
                cfg.enabled ? 'translate-x-6' : 'translate-x-1'
              }`}
            />
          </button>
        </div>

        {/* Admin Chat ID */}
        <div className="space-y-1">
          <label className="block text-sm font-medium text-slate-700">
            Admin Chat ID
          </label>
          <input
            type="text"
            placeholder="123456789"
            value={cfg.adminChatId}
            onChange={(e) => onChatIdChange(e.target.value)}
            className={`block w-full rounded-lg border px-3 py-2 text-sm transition-colors focus:outline-none focus:ring-2 focus:ring-brand-500/30 ${
              chatIdError
                ? 'border-red-400 bg-red-50 focus:ring-red-300/30'
                : 'border-slate-200 bg-white focus:border-brand-500'
            }`}
          />
          {chatIdError ? (
            <p className="text-xs text-red-600">{chatIdError}</p>
          ) : (
            <p className="text-xs text-slate-400">
              @userinfobot Telegram'da /start yuboring — chat ID'ngizni ko'rsatadi
            </p>
          )}
        </div>

        {/* Schedule hour + minute */}
        <div className="space-y-1">
          <label className="block text-sm font-medium text-slate-700">
            <span className="inline-flex items-center gap-1.5">
              <Clock className="h-3.5 w-3.5 text-slate-400" /> Kunlik backup vaqti (soat:daqiqa)
            </span>
          </label>
          <div className="flex items-center gap-2">
            <input
              type="number"
              min={0}
              max={23}
              value={cfg.scheduleHour}
              onChange={(e) => {
                const h = Math.max(0, Math.min(23, Number(e.target.value) || 0))
                setCfg((p) => ({ ...p, scheduleHour: h }))
              }}
              className="w-20 rounded-lg border border-slate-200 bg-white px-3 py-2 text-sm focus:border-brand-500 focus:outline-none focus:ring-2 focus:ring-brand-500/30"
            />
            <span className="text-lg font-semibold text-slate-400">:</span>
            <input
              type="number"
              min={0}
              max={59}
              value={cfg.scheduleMinute}
              onChange={(e) => {
                const mm = Math.max(0, Math.min(59, Number(e.target.value) || 0))
                setCfg((p) => ({ ...p, scheduleMinute: mm }))
              }}
              className="w-20 rounded-lg border border-slate-200 bg-white px-3 py-2 text-sm focus:border-brand-500 focus:outline-none focus:ring-2 focus:ring-brand-500/30"
            />
            <span className="text-sm text-slate-500">
              {String(cfg.scheduleHour).padStart(2, '0')}:{String(cfg.scheduleMinute).padStart(2, '0')} (UTC)
            </span>
          </div>
          <p className="text-xs text-slate-400">
            Soat 0-23, daqiqa 0-59 (UTC). Toshkent = UTC+5 — masalan 02:30 Toshkent uchun 21:30.
          </p>
        </div>

        {/* Last sent info */}
        {cfg.lastSentAt && (
          <div className="flex items-center gap-2 rounded-lg bg-emerald-50 px-3 py-2 text-sm text-emerald-700">
            <CheckCircle2 className="h-4 w-4 shrink-0" />
            <span>Oxirgi backup yuborildi: {fmtDateTime(cfg.lastSentAt)}</span>
          </div>
        )}

        {/* Test result */}
        {testResult && (
          <div
            className={`flex items-start gap-2 rounded-lg px-3 py-2 text-sm ${
              testResult.success
                ? 'bg-emerald-50 text-emerald-700'
                : 'bg-red-50 text-red-700'
            }`}
          >
            {testResult.success ? (
              <CheckCircle2 className="mt-0.5 h-4 w-4 shrink-0" />
            ) : (
              <XCircle className="mt-0.5 h-4 w-4 shrink-0" />
            )}
            <span>{testResult.message}</span>
          </div>
        )}

        {/* Actions */}
        <div className="flex flex-wrap items-center gap-3">
          <Button type="submit" disabled={!canSave}>
            {status === 'saving' ? 'Saqlanmoqda...' : 'Saqlash'}
          </Button>
          <Button
            type="button"
            variant="secondary"
            disabled={testing || running || !(cfg.adminChatId ?? '').trim()}
            onClick={onTest}
          >
            <Send className="h-4 w-4" />
            {testing ? 'Yuborilmoqda...' : 'Test xabar'}
          </Button>
          <Button
            type="button"
            variant="secondary"
            disabled={running || testing || !(cfg.adminChatId ?? '').trim()}
            onClick={onRunBackup}
            title="Markaz ma'lumotlarini JSON qilib hozir Telegram'ga yuboradi"
          >
            <Database className="h-4 w-4" />
            {running ? 'Yuborilmoqda...' : 'Backupni hozir yuborish'}
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
