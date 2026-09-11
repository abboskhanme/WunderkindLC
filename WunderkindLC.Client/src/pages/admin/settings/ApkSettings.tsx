import { useEffect, useRef, useState } from 'react'
import type { ReactNode } from 'react'
import { Smartphone, Upload, Trash2, GraduationCap } from 'lucide-react'
import {
  getAppApkSettings,
  uploadAppApk,
  deleteAppApk,
  type AppApkConfig,
} from '@/api/services/settings'
import { Card } from '@/components/ui/Card'
import { Button } from '@/components/ui/Button'
import { Loader } from '@/components/ui/Loader'

function fmtSize(bytes: number): string {
  if (!bytes) return ''
  const mb = bytes / (1024 * 1024)
  return mb >= 1 ? `${mb.toFixed(1)} MB` : `${Math.max(1, Math.round(bytes / 1024))} KB`
}

/**
 * "Mobil ilova (APK)" sozlamasi — ilova fayllarini yuklash; bot ro'yxatdan o'tgan
 * o'quvchi/o'qituvchiga avtomatik yuboradi. (Ilgari Telegram bot sozlamasi ichida edi.)
 */
export function ApkSettings() {
  const [cfg, setCfg] = useState<AppApkConfig | null>(null)
  const [loading, setLoading] = useState(true)
  const [busy, setBusy] = useState<'student' | 'teacher' | null>(null)
  const [err, setErr] = useState('')

  useEffect(() => {
    getAppApkSettings()
      .then(setCfg)
      .finally(() => setLoading(false))
  }, [])

  const onUpload = async (role: 'student' | 'teacher', file: File) => {
    setErr('')
    if (!file.name.toLowerCase().endsWith('.apk')) {
      setErr('Faqat .apk fayl yuklang')
      return
    }
    setBusy(role)
    try {
      setCfg(await uploadAppApk(role, file))
    } catch (e) {
      const msg = (e as { response?: { data?: { message?: string } } })?.response?.data?.message
      setErr(msg || 'Yuklashda xatolik (APK 50 MB dan oshmasin)')
    } finally {
      setBusy(null)
    }
  }

  const onDelete = async (role: 'student' | 'teacher') => {
    setBusy(role)
    try {
      setCfg(await deleteAppApk(role))
    } finally {
      setBusy(null)
    }
  }

  if (loading) return <Loader label="Yuklanmoqda..." />

  return (
    <Card
      title={
        <span className="flex items-center gap-2">
          <Smartphone className="h-4 w-4 text-brand-600" /> Mobil ilova (APK)
        </span>
      }
    >
      <p className="mb-4 text-sm text-slate-400">
        Bu yerga ilova (APK) faylini yuklang. Foydalanuvchi botda telefon raqamini yuborib,
        kanalga obuna bo'lganidan so'ng — o'quvchi yoki o'qituvchi ilovasini bot avtomatik yuboradi.
        Bittagina ilova bo'lsa, faqat bittasini yuklang (ikkala rol uchun ishlatiladi). Telegram chegarasi: 50 MB.
      </p>

      {err && <p className="mb-3 text-sm text-red-600">{err}</p>}

      <div className="grid gap-3 sm:grid-cols-2">
        <ApkSlot
          role="student"
          label="O'quvchi ilovasi"
          icon={<Smartphone className="h-4 w-4" />}
          name={cfg?.studentApkName ?? ''}
          size={cfg?.studentApkSize ?? 0}
          busy={busy === 'student'}
          onUpload={(f) => onUpload('student', f)}
          onDelete={() => onDelete('student')}
        />
        <ApkSlot
          role="teacher"
          label="O'qituvchi ilovasi"
          icon={<GraduationCap className="h-4 w-4" />}
          name={cfg?.teacherApkName ?? ''}
          size={cfg?.teacherApkSize ?? 0}
          busy={busy === 'teacher'}
          onUpload={(f) => onUpload('teacher', f)}
          onDelete={() => onDelete('teacher')}
        />
      </div>
    </Card>
  )
}

function ApkSlot({
  label,
  icon,
  name,
  size,
  busy,
  onUpload,
  onDelete,
}: {
  role: 'student' | 'teacher'
  label: string
  icon: ReactNode
  name: string
  size: number
  busy: boolean
  onUpload: (file: File) => void
  onDelete: () => void
}) {
  const inputRef = useRef<HTMLInputElement>(null)
  return (
    <div className="rounded-xl border border-slate-200 p-4">
      <div className="mb-3 flex items-center gap-2 font-semibold text-slate-700">
        {icon} {label}
      </div>
      {name ? (
        <div className="flex items-center justify-between gap-2 rounded-lg bg-emerald-50 px-3 py-2">
          <div className="min-w-0">
            <p className="truncate text-sm font-medium text-emerald-800">{name}</p>
            <p className="font-mono text-xs text-emerald-600">{fmtSize(size)}</p>
          </div>
          <button
            type="button"
            title="O'chirish"
            disabled={busy}
            onClick={onDelete}
            className="shrink-0 rounded-lg p-1.5 text-slate-400 transition-colors hover:bg-red-50 hover:text-red-600 disabled:opacity-50"
          >
            <Trash2 className="h-4 w-4" />
          </button>
        </div>
      ) : (
        <p className="mb-3 text-xs text-slate-400">Hali yuklanmagan</p>
      )}

      <input
        ref={inputRef}
        type="file"
        accept=".apk"
        className="hidden"
        onChange={(e) => {
          const f = e.target.files?.[0]
          if (f) onUpload(f)
          e.target.value = ''
        }}
      />
      <Button
        type="button"
        variant="secondary"
        className="mt-3 w-full"
        disabled={busy}
        onClick={() => inputRef.current?.click()}
      >
        <Upload className="h-4 w-4" /> {busy ? 'Yuklanmoqda…' : name ? 'Almashtirish' : 'APK yuklash'}
      </Button>
    </div>
  )
}
