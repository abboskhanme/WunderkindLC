import { useEffect, useState } from 'react'
import { AlertTriangle, Check, Loader2, Save, ShieldCheck } from 'lucide-react'
import type { FaceSettings } from '@/api/services/face'
import { getFaceSettings, saveFaceSettings } from '@/api/services/face'
import { Button } from '@/components/ui/Button'
import { Card } from '@/components/ui/Card'
import { Input } from '@/components/ui/Input'
import { Loader } from '@/components/ui/Loader'
import { apiErrorMessage, cn } from '@/lib/utils'
import { FACE_PRIVACY_NOTE } from './faceLabels'

interface Props {
  canEdit: boolean
}

/** Tavsiya etilgan chegara — juda past qo'yilsa begona odam kirib ketishi mumkin. */
const RECOMMENDED_THRESHOLD = 0.6
/** Server qabul qiladigan oraliq (`AdminFaceController` uni shu chegaralarga qisadi). */
const MIN_THRESHOLD = 0.05
const MAX_THRESHOLD = 0.99
/**
 * Saqlanadigan urinishlarning ENG KAMI — `FaceLoginService.MaxAttemptsPerHour` bilan bir xil.
 *
 * ⚠️ Soatlik urinishlar chegarasi AYNAN shu jurnal qatorlaridan sanaladi; undan kam saqlansa
 * hisob hech qachon chegaraga yetmaydi va brute-force himoyasi JIMGINA o'chib qoladi. Server ham
 * shu qiymatgacha qisadi (`AdminFaceController.PutSettings`) — bu yerdagi `min` faqat foydalanuvchi
 * pastroq son yozib, keyin "nega o'zgarmadi" deb hayron bo'lmasligi uchun.
 */
const MIN_KEEP_CHECKS = 5

/**
 * SOZLAMALAR — modulni yoqish, o'xshashlik chegarasi, model versiyasi va saqlanadigan
 * selfilar soni. Qiymatlar `CenterMeta` da (maxfiy emas — kalit/parol emas).
 */
export function FaceSettingsTab({ canEdit }: Props) {
  const [settings, setSettings] = useState<FaceSettings | null>(null)
  const [loading, setLoading] = useState(true)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState('')
  const [saved, setSaved] = useState(false)

  useEffect(() => {
    getFaceSettings()
      .then(setSettings)
      .catch((err) => setError(apiErrorMessage(err, "Sozlamalarni yuklab bo'lmadi")))
      .finally(() => setLoading(false))
  }, [])

  const patch = (p: Partial<FaceSettings>) => {
    setSettings((s) => (s ? { ...s, ...p } : s))
    setSaved(false)
  }

  const submit = async () => {
    if (!settings || busy) return
    setBusy(true)
    setError('')
    try {
      setSettings(await saveFaceSettings(settings))
      setSaved(true)
    } catch (err) {
      setError(apiErrorMessage(err, "Saqlab bo'lmadi"))
    } finally {
      setBusy(false)
    }
  }

  if (loading || !settings) {
    return (
      <Card>{error ? <p className="text-sm text-red-700">{error}</p> : <Loader label="Yuklanmoqda..." />}</Card>
    )
  }

  return (
    <div className="grid gap-4 lg:grid-cols-[1fr_340px]">
      <Card
        title="Yuz bilan kirish sozlamalari"
        sub="O'quvchi ilovasiga YANGI qurilmadan kirishda selfi so'raladi"
        actions={
          canEdit ? (
            <Button onClick={submit} disabled={busy}>
              {busy ? <Loader2 className="h-4 w-4 animate-spin" /> : <Save className="h-4 w-4" />}
              Saqlash
            </Button>
          ) : undefined
        }
      >
        <div className="space-y-5">
          {/* ---- Yoqish ---- */}
          <label className="flex cursor-pointer items-start gap-2.5 rounded-lg bg-slate-50 px-3 py-2.5">
            <input
              type="checkbox"
              className="mt-0.5"
              disabled={!canEdit}
              checked={settings.enabled}
              onChange={(e) => patch({ enabled: e.target.checked })}
            />
            <span className="text-sm text-slate-700">
              <b>Yuz bilan kirish yoqilgan</b>
              <span className="block text-xs text-slate-400">
                O'chirilsa BUTUN modul ishlamaydi: o'quvchi istalgan yangi qurilmadan faqat login
                va parol bilan, <b>selfisiz</b> kiraveradi. Mavjud etalonlar va ishonchli
                qurilmalar saqlanadi — qayta yoqilganda ular ishlatiladi.
              </span>
            </span>
          </label>

          {!settings.enabled && (
            <div className="flex items-start gap-2 rounded-lg bg-amber-50 px-3 py-2.5 text-sm text-amber-800">
              <AlertTriangle className="mt-0.5 h-4 w-4 shrink-0" />
              <span>
                Hozir modul <b>o'chiq</b>. Yangi urinishlar umuman yozilmaydi — «Urinishlar» tabi
                bo'sh turadi.
              </span>
            </div>
          )}

          {/* ---- Chegara ---- */}
          <div>
            <div className="mb-1 flex items-center justify-between">
              <span className="text-sm font-semibold text-slate-700">O'xshashlik chegarasi</span>
              <span className="font-mono text-sm font-bold text-slate-800">
                {settings.threshold.toFixed(2)}{' '}
                <span className="text-xs font-normal text-slate-400">
                  ({Math.round(settings.threshold * 100)}%)
                </span>
              </span>
            </div>
            <input
              type="range"
              min={MIN_THRESHOLD}
              max={MAX_THRESHOLD}
              step={0.01}
              disabled={!canEdit}
              value={settings.threshold}
              onChange={(e) => patch({ threshold: Number(e.target.value) })}
              className="w-full accent-brand-500"
            />
            <div className="mt-1 flex justify-between text-[11px] text-slate-400">
              <span>{MIN_THRESHOLD.toFixed(2)} — deyarli hammani o'tkazadi</span>
              <span>{MAX_THRESHOLD.toFixed(2)} — deyarli hech kimni o'tkazmaydi</span>
            </div>
            <p className="mt-2 text-xs text-slate-500">
              Selfi bilan etalon orasidagi o'xshashlik shu qiymatdan yuqori bo'lsa — kirishga
              ruxsat beriladi. <b>Past</b> qiymat ko'proq o'tkazadi (begona odam kirib qolish
              xavfi ortadi), <b>yuqori</b> qiymat ko'proq rad etadi (o'z o'quvchisi ham kira
              olmay qolishi mumkin). Tavsiya: <b>{RECOMMENDED_THRESHOLD.toFixed(2)}</b>.
            </p>
            {Math.abs(settings.threshold - RECOMMENDED_THRESHOLD) > 0.15 && (
              <button
                type="button"
                disabled={!canEdit}
                onClick={() => patch({ threshold: RECOMMENDED_THRESHOLD })}
                className="mt-1.5 rounded-full border border-slate-200 px-2.5 py-1 text-xs text-slate-600 hover:border-brand-300 hover:bg-brand-50"
              >
                Tavsiya etilgan {RECOMMENDED_THRESHOLD.toFixed(2)} ga qaytarish
              </button>
            )}
          </div>

          {/* ---- Model versiyasi ---- */}
          <div>
            <Input
              label="Model versiyasi"
              placeholder="masalan: arcface-v1"
              disabled={!canEdit}
              value={settings.modelVersion}
              onChange={(e) => patch({ modelVersion: e.target.value })}
            />
            <div className="mt-1.5 flex items-start gap-2 rounded-lg bg-red-50 px-3 py-2 text-xs text-red-700">
              <AlertTriangle className="mt-0.5 h-3.5 w-3.5 shrink-0" />
              <span>
                <b>Diqqat:</b> bu qiymat MOBIL ILOVADAGI model versiyasi bilan aynan mos bo'lishi
                SHART. Mos kelmasa turli modellarning vektorlari solishtirilardi (natija
                ma'nosiz), shuning uchun server har bir urinishni «Ilovani yangilang» deb rad
                etadi — ya'ni <b>hech kim kira olmaydi</b>. Modelni ilova bilan birga
                yangilang.
              </span>
            </div>
          </div>

          {/* ---- Saqlanadigan selfilar ---- */}
          <div>
            <Input
              label="Saqlanadigan selfilar (o'quvchi boshiga)"
              type="number"
              min={MIN_KEEP_CHECKS}
              max={100}
              className="w-40"
              disabled={!canEdit}
              value={settings.keepChecks}
              onChange={(e) => patch({ keepChecks: Number(e.target.value) })}
            />
            <p className="mt-1 text-xs text-slate-500">
              Har bir o'quvchidan faqat shuncha oxirgi urinish (yozuv + selfi FAYLI) saqlanadi,
              eskilari avtomatik o'chiriladi. Qiymat {MIN_KEEP_CHECKS} dan 100 gacha. Kichik son —
              kamroq biometrik ma'lumot saqlanadi; katta son — tekshirish uchun ko'proq tarix
              qoladi.
            </p>
            <p className="mt-1 text-xs text-slate-500">
              Eng kamida {MIN_KEEP_CHECKS} ta: soatlik urinishlar chegarasi aynan shu yozuvlardan
              hisoblanadi, undan kam saqlansa chegara ishlamay qoladi. Server ham shu qiymatdan
              pastini qabul qilmaydi.
            </p>
          </div>

          {error && (
            <div className="flex items-center gap-2 rounded-lg bg-red-50 px-3 py-2.5 text-sm text-red-700">
              <AlertTriangle className="h-4 w-4 shrink-0" /> {error}
            </div>
          )}
          {saved && (
            <div className="flex items-center gap-2 rounded-lg bg-emerald-50 px-3 py-2.5 text-sm text-emerald-700">
              <Check className="h-4 w-4 shrink-0" /> Saqlandi
            </div>
          )}
          {!canEdit && (
            <p className="text-xs text-slate-400">
              Sozlamalarni o'zgartirish uchun «O'quvchilar» bo'limida tahrirlash ruxsati kerak.
            </p>
          )}
        </div>
      </Card>

      {/* ---- Yon panel: qanday ishlaydi + maxfiylik ---- */}
      <div className="space-y-4">
        <Card title="Qanday ishlaydi">
          <ol className="space-y-2 text-xs text-slate-600">
            <Step n={1}>
              O'quvchi ilovaga <b>yangi qurilmadan</b> kiradi — login va paroldan keyin selfi
              so'raladi.
            </Step>
            <Step n={2}>
              Yuz vektori <b>telefonda</b> hisoblanadi (serverda model ishlamaydi), serverga
              faqat sonlar yuboriladi.
            </Step>
            <Step n={3}>
              <b>Etalon bor</b> bo'lsa selfi u bilan solishtiriladi; <b>etalon yo'q</b> bo'lsa
              o'quvchining profil rasmi bilan. Shu sabab parolni o'g'irlagan begona odam o'z
              yuzini etalon qilib qo'ya olmaydi.
            </Step>
            <Step n={4}>
              Profil rasmi ham bo'lmasa — urinish <b>kutilmoqda</b> bo'lib «Urinishlar» tabiga
              tushadi va admin qo'lda tasdiqlaydi.
            </Step>
            <Step n={5}>
              Tasdiqlangan qurilma «Qurilmalar» ro'yxatiga tushadi — undan keyingi kirishlarda
              selfi so'ralmaydi.
            </Step>
          </ol>
        </Card>

        <Card
          title={
            <span className="inline-flex items-center gap-1.5">
              <ShieldCheck className="h-4 w-4 text-brand-500" /> Maxfiylik
            </span>
          }
          className={cn('bg-brand-50/40')}
        >
          <p className="text-xs leading-relaxed text-slate-600">{FACE_PRIVACY_NOTE}</p>
          <p className="mt-2 text-xs leading-relaxed text-slate-600">
            Selfi manzillari o'zgarishlar tarixiga (auditga) yozilmaydi va bo'limni faqat
            «O'quvchilar» ruxsati bor xodim ko'ra oladi.
          </p>
        </Card>
      </div>
    </div>
  )
}

function Step({ n, children }: { n: number; children: React.ReactNode }) {
  return (
    <li className="flex gap-2">
      <span className="flex h-5 w-5 shrink-0 items-center justify-center rounded-full bg-brand-50 text-[11px] font-bold text-brand-700">
        {n}
      </span>
      <span>{children}</span>
    </li>
  )
}
