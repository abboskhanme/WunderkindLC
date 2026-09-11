import { useEffect, useRef, useState } from 'react'
import { CheckCircle2, Loader2, Save, Upload } from 'lucide-react'
import type { CareerAbout } from '@/api/services/career'
import { getAbout, saveAbout, uploadCareerFile } from '@/api/services/career'
import { Card } from '@/components/ui/Card'
import { Button } from '@/components/ui/Button'
import { Loader } from '@/components/ui/Loader'
import { Input, Textarea } from '@/components/ui/Input'
import { apiErrorMessage, formatDateTime } from '@/lib/utils'

interface Props {
  canEdit: boolean
}

const empty: CareerAbout = {
  title: '', tagline: '', about: '', benefits: '', logoUrl: '',
  address: '', landmark: '', mapUrl: '', workTime: '',
  phone: '', phone2: '', email: '',
  telegram: '', instagram: '', facebook: '', youtube: '', tiktok: '', website: '',
  updatedAt: '', updatedBy: '',
}

/**
 * "BIZ HAQIMIZDA" — nomzod ilovasining (Mini App) BIRINCHI ekrani.
 * Bo'sh qoldirilgan maydon ilovada UMUMAN ko'rsatilmaydi (bo'sh blok chiqmasin).
 */
export function CareerAboutTab({ canEdit }: Props) {
  const [form, setForm] = useState<CareerAbout>(empty)
  const [loading, setLoading] = useState(true)
  const [busy, setBusy] = useState(false)
  const [uploading, setUploading] = useState(false)
  const [saved, setSaved] = useState(false)
  const [error, setError] = useState('')
  const fileRef = useRef<HTMLInputElement | null>(null)

  useEffect(() => {
    getAbout()
      .then(setForm)
      .catch((err) => setError(apiErrorMessage(err, "Ma'lumotni yuklab bo'lmadi")))
      .finally(() => setLoading(false))
  }, [])

  const set = <K extends keyof CareerAbout>(key: K, value: CareerAbout[K]) => {
    setForm((prev) => ({ ...prev, [key]: value }))
    setSaved(false)
  }

  const pickLogo = async (file: File) => {
    setUploading(true)
    setError('')
    try {
      set('logoUrl', await uploadCareerFile(file))
    } catch (err) {
      setError(apiErrorMessage(err, "Rasmni yuklab bo'lmadi"))
    } finally {
      setUploading(false)
    }
  }

  const submit = async () => {
    if (busy) return
    setBusy(true)
    setError('')
    try {
      setForm(await saveAbout(form))
      setSaved(true)
    } catch (err) {
      setError(apiErrorMessage(err, "Saqlab bo'lmadi"))
    } finally {
      setBusy(false)
    }
  }

  if (loading) {
    return (
      <Card>
        <Loader label="Yuklanmoqda..." />
      </Card>
    )
  }

  return (
    <div className="space-y-4">
      <Card title="Tanishtiruv" sub="Ilovaning birinchi ekranida ko'rinadi">
        <div className="space-y-3">
          <div className="flex flex-wrap items-center gap-3">
            <div className="flex h-16 w-16 items-center justify-center overflow-hidden rounded-2xl border border-slate-200 bg-slate-50 text-2xl">
              {form.logoUrl ? (
                <img src={form.logoUrl} alt="" className="h-full w-full object-cover" />
              ) : (
                '🎓'
              )}
            </div>
            {canEdit && (
              <div className="flex flex-wrap items-center gap-2">
                <input
                  ref={fileRef}
                  type="file"
                  accept="image/*"
                  hidden
                  onChange={(e) => {
                    const f = e.target.files?.[0]
                    if (f) void pickLogo(f)
                    e.target.value = ''
                  }}
                />
                <Button variant="secondary" onClick={() => fileRef.current?.click()} disabled={uploading}>
                  {uploading ? <Loader2 className="h-4 w-4 animate-spin" /> : <Upload className="h-4 w-4" />}
                  Logotip yuklash
                </Button>
                {form.logoUrl && (
                  <Button variant="ghost" onClick={() => set('logoUrl', '')}>
                    O'chirish
                  </Button>
                )}
              </div>
            )}
          </div>

          <Input
            label="Sarlavha"
            value={form.title}
            onChange={(e) => set('title', e.target.value)}
            placeholder="Wunderkind o'quv markazi"
            disabled={!canEdit}
          />
          <Input
            label="Shior (qisqa jumla)"
            value={form.tagline}
            onChange={(e) => set('tagline', e.target.value)}
            placeholder="Bilim va kelajak bir joyda"
            disabled={!canEdit}
          />
          <Textarea
            label="Kimmiz? (asosiy matn)"
            rows={6}
            value={form.about}
            onChange={(e) => set('about', e.target.value)}
            placeholder="Markaz qachon ochilgan, nechta yo'nalish, nechta o'quvchi, jamoa haqida..."
            disabled={!canEdit}
          />
          <Textarea
            label="Nega biz bilan? (imtiyozlar)"
            rows={5}
            value={form.benefits}
            onChange={(e) => set('benefits', e.target.value)}
            placeholder={"Har qatorda bitta imtiyoz, masalan:\nRasmiy ish haqi\nBepul malaka oshirish\nDo'stona jamoa"}
            disabled={!canEdit}
          />
        </div>
      </Card>

      <Card title="Manzil" sub="Nomzod ilovada xaritada ocha oladi">
        <div className="space-y-3">
          <Input
            label="Manzil"
            value={form.address}
            onChange={(e) => set('address', e.target.value)}
            placeholder="Qo'qon shahri, Istiqlol ko'chasi 12"
            disabled={!canEdit}
          />
          <div className="grid gap-3 sm:grid-cols-2">
            <Input
              label="Mo'ljal"
              value={form.landmark}
              onChange={(e) => set('landmark', e.target.value)}
              placeholder="Markaziy bozor ro'parasida"
              disabled={!canEdit}
            />
            <Input
              label="Ish vaqti"
              value={form.workTime}
              onChange={(e) => set('workTime', e.target.value)}
              placeholder="Du–Sh, 09:00–18:00"
              disabled={!canEdit}
            />
          </div>
          <Input
            label="Xaritaga havola"
            value={form.mapUrl}
            onChange={(e) => set('mapUrl', e.target.value)}
            placeholder="https://yandex.uz/maps/..."
            disabled={!canEdit}
          />
        </div>
      </Card>

      <Card title="Aloqa">
        <div className="grid gap-3 sm:grid-cols-2">
          <Input
            label="Telefon"
            value={form.phone}
            onChange={(e) => set('phone', e.target.value)}
            placeholder="+998 90 123 45 67"
            disabled={!canEdit}
          />
          <Input
            label="Qo'shimcha telefon"
            value={form.phone2}
            onChange={(e) => set('phone2', e.target.value)}
            disabled={!canEdit}
          />
          <Input
            label="Email"
            value={form.email}
            onChange={(e) => set('email', e.target.value)}
            placeholder="hr@wunderkindedu.uz"
            disabled={!canEdit}
          />
          <Input
            label="Veb-sayt"
            value={form.website}
            onChange={(e) => set('website', e.target.value)}
            placeholder="wunderkindedu.uz"
            disabled={!canEdit}
          />
        </div>
      </Card>

      <Card title="Ijtimoiy tarmoqlar" sub="Bo'sh qoldirilgani ilovada ko'rsatilmaydi">
        <div className="grid gap-3 sm:grid-cols-2">
          <Input
            label="Telegram"
            value={form.telegram}
            onChange={(e) => set('telegram', e.target.value)}
            placeholder="t.me/wunderkind"
            disabled={!canEdit}
          />
          <Input
            label="Instagram"
            value={form.instagram}
            onChange={(e) => set('instagram', e.target.value)}
            placeholder="instagram.com/wunderkind"
            disabled={!canEdit}
          />
          <Input
            label="Facebook"
            value={form.facebook}
            onChange={(e) => set('facebook', e.target.value)}
            disabled={!canEdit}
          />
          <Input
            label="YouTube"
            value={form.youtube}
            onChange={(e) => set('youtube', e.target.value)}
            disabled={!canEdit}
          />
          <Input
            label="TikTok"
            value={form.tiktok}
            onChange={(e) => set('tiktok', e.target.value)}
            disabled={!canEdit}
          />
        </div>
      </Card>

      {error && (
        <Card className="border-red-200 bg-red-50 text-sm font-medium text-red-600">{error}</Card>
      )}

      {canEdit && (
        <div className="flex flex-wrap items-center gap-3">
          <Button onClick={submit} disabled={busy}>
            {busy ? <Loader2 className="h-4 w-4 animate-spin" /> : <Save className="h-4 w-4" />}
            Saqlash
          </Button>
          {saved && (
            <span className="inline-flex items-center gap-1.5 text-sm font-medium text-emerald-600">
              <CheckCircle2 className="h-4 w-4" /> Saqlandi
            </span>
          )}
          {form.updatedAt && (
            <span className="text-xs text-slate-400">
              Oxirgi o'zgarish: {formatDateTime(form.updatedAt)}
              {form.updatedBy && ` · ${form.updatedBy}`}
            </span>
          )}
        </div>
      )}
    </div>
  )
}
