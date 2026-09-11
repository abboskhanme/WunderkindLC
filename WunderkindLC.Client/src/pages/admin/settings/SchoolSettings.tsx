import { useEffect, useRef, useState } from 'react'
import type { FormEvent } from 'react'
import { Check } from 'lucide-react'
import {
  getSchoolInfo,
  saveSchoolInfo,
  uploadLogo,
  deleteLogo,
  type SchoolInfo,
} from '@/api/services/settings'
import { Card } from '@/components/ui/Card'
import { Button } from '@/components/ui/Button'
import { Input } from '@/components/ui/Input'
import { Loader } from '@/components/ui/Loader'

const empty: SchoolInfo = {
  name: '',
  director: '',
  phone: '',
  email: '',
  address: '',
  region: '',
  district: '',
  logoUrl: '',
}

/** Markazga oid umumiy ma'lumotlar (nomi, direktor, manzil va h.k.) kiritiladigan sozlama. */
export function SchoolSettings() {
  const [form, setForm] = useState<SchoolInfo>(empty)
  const [loading, setLoading] = useState(true)
  const [status, setStatus] = useState<'idle' | 'saving' | 'saved'>('idle')
  const [logoBusy, setLogoBusy] = useState(false)
  const fileRef = useRef<HTMLInputElement>(null)

  useEffect(() => {
    getSchoolInfo()
      .then(setForm)
      .finally(() => setLoading(false))
  }, [])

  const update = <K extends keyof SchoolInfo>(key: K, value: SchoolInfo[K]) =>
    setForm((f) => ({ ...f, [key]: value }))

  const onSubmit = async (e: FormEvent) => {
    e.preventDefault()
    setStatus('saving')
    try {
      await saveSchoolInfo({ ...form, name: (form.name ?? '').trim() })
      // Yon menyudagi markaz nomini darrov yangilash uchun.
      window.dispatchEvent(new Event('school:updated'))
      setStatus('saved')
      setTimeout(() => setStatus('idle'), 2000)
    } catch {
      setStatus('idle')
    }
  }

  const onPickLogo = async (e: React.ChangeEvent<HTMLInputElement>) => {
    const file = e.target.files?.[0]
    e.target.value = '' // bir xil faylni qayta tanlash imkonini beradi
    if (!file) return
    setLogoBusy(true)
    try {
      const updated = await uploadLogo(file)
      setForm((f) => ({ ...f, logoUrl: updated.logoUrl }))
      window.dispatchEvent(new Event('school:updated'))
    } finally {
      setLogoBusy(false)
    }
  }

  const onRemoveLogo = async () => {
    setLogoBusy(true)
    try {
      const updated = await deleteLogo()
      setForm((f) => ({ ...f, logoUrl: updated.logoUrl }))
      window.dispatchEvent(new Event('school:updated'))
    } finally {
      setLogoBusy(false)
    }
  }

  if (loading) return <Loader label="Yuklanmoqda..." />

  return (
    <Card
      title="Markaz ma'lumotlari"
      sub="Markaz nomi va umumiy ma'lumotlar — hisobotlar va hujjatlarda ishlatiladi."
    >
      {/* Logo */}
      <div className="mb-6 max-w-2xl">
        <p className="mb-2 text-sm font-medium text-slate-700">Logo</p>
        <div className="flex items-center gap-4">
          <div className="flex h-16 w-16 shrink-0 items-center justify-center overflow-hidden rounded-xl border border-slate-200 bg-slate-50">
            {form.logoUrl ? (
              <img src={form.logoUrl} alt="Logo" className="h-full w-full object-contain" />
            ) : (
              <span className="text-[11px] text-slate-400">Logo yo'q</span>
            )}
          </div>
          <div className="flex items-center gap-2">
            <input
              ref={fileRef}
              type="file"
              accept="image/*"
              className="hidden"
              onChange={onPickLogo}
            />
            <Button
              type="button"
              variant="secondary"
              disabled={logoBusy}
              onClick={() => fileRef.current?.click()}
            >
              {logoBusy ? 'Yuklanmoqda...' : 'Logo yuklash'}
            </Button>
            {form.logoUrl && (
              <Button type="button" variant="ghost" disabled={logoBusy} onClick={onRemoveLogo}>
                O'chirish
              </Button>
            )}
          </div>
        </div>
      </div>

      <form onSubmit={onSubmit} className="max-w-2xl space-y-4">
        <Input
          label="Markaz nomi"
          placeholder="Masalan: 1-sonli umumiy o'rta ta'lim markazi"
          value={form.name}
          onChange={(e) => update('name', e.target.value)}
          required
        />
        <Input
          label="Direktor (F.I.SH)"
          value={form.director}
          onChange={(e) => update('director', e.target.value)}
        />
        <div className="grid grid-cols-1 gap-4 sm:grid-cols-2">
          <Input label="Telefon" value={form.phone} onChange={(e) => update('phone', e.target.value)} />
          <Input
            label="Email"
            type="email"
            value={form.email}
            onChange={(e) => update('email', e.target.value)}
          />
        </div>
        <Input label="Manzil" value={form.address} onChange={(e) => update('address', e.target.value)} />
        <div className="grid grid-cols-1 gap-4 sm:grid-cols-2">
          <Input label="Viloyat" value={form.region} onChange={(e) => update('region', e.target.value)} />
          <Input label="Tuman" value={form.district} onChange={(e) => update('district', e.target.value)} />
        </div>

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
