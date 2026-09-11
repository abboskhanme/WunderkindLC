import { useEffect, useState } from 'react'
import { Save, Loader2, CreditCard, AlertTriangle, Check } from 'lucide-react'
import type { BookSettings } from '@/api/services/books'
import { getBookSettings, saveBookSettings } from '@/api/services/books'
import { Card } from '@/components/ui/Card'
import { Button } from '@/components/ui/Button'
import { Loader } from '@/components/ui/Loader'
import { Input, Textarea } from '@/components/ui/Input'
import { apiErrorMessage } from '@/lib/utils'

interface Props {
  canEdit: boolean
}

/**
 * SOZLAMALAR — botda ko'rinadigan TO'LOV REKVIZITLARI (karta raqami va egasi) hamda
 * «📚 Kitob sotib olish» tugmasini yoqish/o'chirish. Karta raqami mijozga baribir ko'rsatiladi
 * (maxfiy emas), shuning uchun bu qiymatlar bazada saqlanadi — .env kaliti EMAS.
 *
 * Karta raqami bo'sh bo'lsa bot faqat «💵 Naqd pulda» variantini ko'rsatadi.
 */
export function BookSettingsTab({ canEdit }: Props) {
  const [settings, setSettings] = useState<BookSettings | null>(null)
  const [loading, setLoading] = useState(true)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState('')
  const [saved, setSaved] = useState(false)

  useEffect(() => {
    getBookSettings()
      .then(setSettings)
      .catch((err) => setError(apiErrorMessage(err, "Sozlamalarni yuklab bo'lmadi")))
      .finally(() => setLoading(false))
  }, [])

  const patch = (p: Partial<BookSettings>) => {
    setSettings((s) => (s ? { ...s, ...p } : s))
    setSaved(false)
  }

  const submit = async () => {
    if (!settings || busy) return
    setBusy(true)
    setError('')
    try {
      setSettings(await saveBookSettings(settings))
      setSaved(true)
    } catch (err) {
      setError(apiErrorMessage(err, "Saqlab bo'lmadi"))
    } finally {
      setBusy(false)
    }
  }

  if (loading || !settings) {
    return (
      <Card>
        <Loader label="Yuklanmoqda..." />
      </Card>
    )
  }

  return (
    <div className="grid gap-4 lg:grid-cols-[1fr_340px]">
      <Card
        title="To'lov rekvizitlari"
        sub="Mijoz botda «Karta orqali» tanlaganda shu ma'lumotlar ko'rsatiladi"
        actions={
          canEdit ? (
            <Button onClick={submit} disabled={busy}>
              {busy ? <Loader2 className="h-4 w-4 animate-spin" /> : <Save className="h-4 w-4" />}
              Saqlash
            </Button>
          ) : undefined
        }
      >
        <div className="space-y-4">
          <label className="flex cursor-pointer items-start gap-2.5 rounded-lg bg-slate-50 px-3 py-2.5">
            <input
              type="checkbox"
              className="mt-0.5"
              disabled={!canEdit}
              checked={settings.bookSalesEnabled}
              onChange={(e) => patch({ bookSalesEnabled: e.target.checked })}
            />
            <span className="text-sm text-slate-700">
              <b>Botda kitob sotuvi yoqilgan</b>
              <span className="block text-xs text-slate-400">
                O'chirilsa botdagi «📚 Kitob sotib olish» tugmasi ishlamaydi (mavjud buyurtmalar
                saqlanadi).
              </span>
            </span>
          </label>

          <Input
            label="Karta raqami"
            placeholder="8600 1234 5678 9012"
            disabled={!canEdit}
            value={settings.bookCardNumber}
            onChange={(e) => patch({ bookCardNumber: e.target.value })}
          />
          <Input
            label="Karta egasi (F.I.Sh.)"
            placeholder="ABDULLAYEV ABDULLA"
            disabled={!canEdit}
            value={settings.bookCardHolder}
            onChange={(e) => patch({ bookCardHolder: e.target.value })}
          />
          <Textarea
            label="Qo'shimcha izoh (ixtiyoriy)"
            rows={2}
            placeholder="Masalan: To'lovdan keyin chek rasmini yuborishni unutmang"
            disabled={!canEdit}
            value={settings.bookPaymentNote}
            onChange={(e) => patch({ bookPaymentNote: e.target.value })}
          />

          {!settings.bookCardNumber.trim() && (
            <div className="flex items-start gap-2 rounded-lg bg-amber-50 px-3 py-2.5 text-sm text-amber-800">
              <AlertTriangle className="mt-0.5 h-4 w-4 shrink-0" />
              <span>
                Karta raqami kiritilmagan — bot faqat <b>«💵 Naqd pulda»</b> variantini ko'rsatadi.
              </span>
            </div>
          )}

          {error && (
            <div className="flex items-center gap-2 rounded-lg bg-red-50 px-3 py-2.5 text-sm text-red-700">
              <AlertTriangle className="h-4 w-4 shrink-0" /> {error}
            </div>
          )}
          {saved && (
            <div className="flex items-center gap-2 rounded-lg bg-emerald-50 px-3 py-2.5 text-sm text-emerald-700">
              <Check className="h-4 w-4 shrink-0" /> Saqlandi.
            </div>
          )}
        </div>
      </Card>

      {/* Botda qanday ko'rinadi — jonli namuna */}
      <Card title="Botda qanday ko'rinadi" sub="Mijoz «Karta orqali» tanlaganda">
        <div className="space-y-2 rounded-xl bg-slate-50 p-3 text-sm leading-relaxed text-slate-700">
          <p className="font-semibold">💳 Karta orqali to'lov</p>
          <p className="text-slate-500">
            📕 Kitob nomi — 1 dona
            <br />
            💰 To'lash kerak: <b className="text-slate-800">50 000 so'm</b>
          </p>
          <div className="rounded-lg bg-white px-3 py-2">
            <p className="text-xs text-slate-400">💳 Karta raqami</p>
            <p className="font-mono text-[15px] font-semibold text-slate-800">
              {settings.bookCardNumber.trim() || '— kiritilmagan —'}
            </p>
            {settings.bookCardHolder.trim() && (
              <p className="mt-1 text-xs text-slate-500">
                👤 Karta egasi: <b className="text-slate-700">{settings.bookCardHolder}</b>
              </p>
            )}
          </div>
          {settings.bookPaymentNote.trim() && (
            <p className="text-xs text-slate-500">ℹ️ {settings.bookPaymentNote}</p>
          )}
          <p className="text-xs text-slate-500">
            🧾 To'lovni amalga oshirib, <b>chek rasmini (skrinshot) yoki PDF faylini</b> shu yerga
            yuboring — administrator tekshirib tasdiqlaydi.
          </p>
        </div>

        <div className="mt-3 flex items-start gap-2 text-xs leading-relaxed text-slate-400">
          <CreditCard className="mt-0.5 h-3.5 w-3.5 shrink-0" />
          <span>
            Avtomatik to'lov tizimlari (Click/Payme) ishlatilmaydi — to'lov naqd yoki karta raqamiga
            o'tkazma (P2P) shaklida qabul qilinadi va admin qo'lda tasdiqlaydi.
          </span>
        </div>
      </Card>
    </div>
  )
}
