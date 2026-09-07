import { useEffect, useState } from 'react'
import type { FormEvent } from 'react'
import { Check, CheckCircle2, XCircle, Info, HardDrive, AlertTriangle } from 'lucide-react'
import { Link } from 'react-router-dom'
import { getCameraSettings, saveCameraSettings, type CameraConfig } from '@/api/services/settings'
import { Card } from '@/components/ui/Card'
import { Badge } from '@/components/ui/Badge'
import { Button } from '@/components/ui/Button'
import { Loader } from '@/components/ui/Loader'

/**
 * Kamera integratsiya sozlamasi. IP kameralar RTSP oqimi media-shlyuz (MediaMTX) orqali brauzerda
 * jonli (HLS) ko'rsatiladi. Kameralar ro'yxati va kuzatuv — "Boshqaruv → Kameralar" bo'limida.
 *
 * ⚠️ Bu sahifada IKKI kalit bor va ular ATAYIN ajratilgan:
 *   1) "Kamera kuzatuvi" — modulning o'zi (jonli ko'rish);
 *   2) "24/7 yozib borish" — diskka yozish. Default O'CHIQ, chunki narxi jonli kuzatuvnikidan
 *      butunlay boshqa: 1080p kamera ~20-43 GB/KUN disk + markazning kanalidan uzluksiz trafik.
 *      O'chiq bo'lsa kameralar baribir jonli ko'rinaveradi. Batafsil: `.claude/rules/cameras.md`.
 */
export function CameraSettings() {
  const [cfg, setCfg] = useState<CameraConfig | null>(null)
  const [loading, setLoading] = useState(true)
  const [status, setStatus] = useState<'idle' | 'saving' | 'saved'>('idle')

  useEffect(() => {
    getCameraSettings().then(setCfg).finally(() => setLoading(false))
  }, [])

  const onSubmit = async (e: FormEvent) => {
    e.preventDefault()
    if (!cfg) return
    setStatus('saving')
    const saved = await saveCameraSettings({ enabled: cfg.enabled, recordEnabled: cfg.recordEnabled })
    setCfg(saved)
    setStatus('saved')
    setTimeout(() => setStatus('idle'), 2000)
  }

  if (loading || !cfg) return <Loader label="Yuklanmoqda..." />

  return (
    <form onSubmit={onSubmit} className="space-y-6">
      <Card
        title={
          <span className="flex items-center gap-2">
            Kamera integratsiya
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
          IP kameralar (RTSP) media-shlyuz orqali brauzerda ko'rinadi. Kameralarni qo'shish va kuzatish —{' '}
          <Link to="/admin/boshqaruv/cameras" className="font-medium text-brand-600 hover:underline">Boshqaruv → Kameralar</Link>.
        </p>

        <label className="mb-4 inline-flex cursor-pointer items-center gap-2 text-sm font-medium text-slate-700">
          <input type="checkbox" checked={cfg.enabled} onChange={(e) => setCfg({ ...cfg, enabled: e.target.checked })}
            className="h-4 w-4 rounded border-slate-300 accent-brand-600" />
          Kamera kuzatuvini yoqish
        </label>

        <div className="rounded-lg bg-slate-50 px-3 py-2.5 text-xs text-slate-600">
          <div className="mb-1 font-medium text-slate-500">Qanday ishlaydi:</div>
          <ul className="list-inside list-disc space-y-1">
            <li>Har kameraga uning <b>RTSP manzili</b>ni (login/parol bilan) kiriting.</li>
            <li>Media-shlyuz (MediaMTX) RTSP'ni brauzer ko'radigan <b>HLS</b> ga o'giradi.</li>
            <li>Jonli kuzatuv — Kameralar bo'limida, hech qanday qo'shimcha sozlamasiz.</li>
            <li>Yozuvni orqaga qaytarish va <b>qirqib yuklab olish</b> faqat quyidagi «24/7 yozib
                borish» yoqilgan bo'lsa ishlaydi.</li>
          </ul>
        </div>

        <p className="mt-4 flex items-center gap-1.5 text-sm text-slate-400">
          <Info className="h-4 w-4" /> Hozircha {cfg.cameraCount} ta kamera qo'shilgan.
        </p>
      </Card>

      {/* Yozuv — ALOHIDA kalit: jonli kuzatuvdan narxi ham, mas'uliyati ham boshqa. */}
      <Card
        title={
          <span className="flex items-center gap-2">
            <HardDrive className="h-4 w-4 text-slate-400" /> 24/7 yozib borish
            {cfg.recordEnabled ? (
              <Badge tone="amber">
                <CheckCircle2 className="h-3.5 w-3.5" /> Yozilmoqda
              </Badge>
            ) : (
              <Badge tone="default">
                <XCircle className="h-3.5 w-3.5" /> Yozilmayapti
              </Badge>
            )}
          </span>
        }
      >
        <p className="mb-4 text-sm text-slate-400">
          Kameralarni <b>diskka uzluksiz yozib borish</b>. Yozuv bo'lsagina keyin orqaga qaytarish
          va bo'lakni qirqib yuklab olish mumkin. <b>Jonli kuzatuvga aloqasi yo'q</b> — bu kalit
          o'chiq bo'lsa ham kameralar jonli ko'rinaveradi.
        </p>

        <label className="mb-4 inline-flex cursor-pointer items-center gap-2 text-sm font-medium text-slate-700">
          <input type="checkbox" checked={cfg.recordEnabled}
            onChange={(e) => setCfg({ ...cfg, recordEnabled: e.target.checked })}
            className="h-4 w-4 rounded border-slate-300 accent-brand-600" />
          Yozib borishni yoqish
        </label>

        <div className="rounded-lg border border-amber-200 bg-amber-50 px-3 py-2.5 text-xs text-amber-900">
          <div className="mb-1 flex items-center gap-1.5 font-medium">
            <AlertTriangle className="h-3.5 w-3.5" /> Yoqishdan oldin bilib qo'ying
          </div>
          <ul className="list-inside list-disc space-y-1">
            <li>Bitta 1080p kamera taxminan <b>20–43 GB/kun</b> joy oladi (oqim sifatiga qarab).</li>
            <li>8 kamera × 7 kun ≈ <b>1.2 TB</b>. Muddat har kamera kartasida sozlanadi.</li>
            <li>Yozuv yoqilganda shlyuz kameraga <b>24/7 ulanib turadi</b> — markazning internet
                kanali hech kim qaramaganda ham band bo'ladi. O'chiq bo'lsa faqat kimdir
                qaraganda ulanadi.</li>
            <li>Yozuv <b>faqat yoqilgandan keyingi</b> vaqt uchun bo'ladi — orqaga ishlamaydi.</li>
          </ul>
        </div>

        {cfg.recordEnabled && (
          <p className="mt-4 flex items-center gap-1.5 text-sm text-slate-400">
            <Info className="h-4 w-4" /> {cfg.cameraCount} tadan <b>{cfg.recordingCameraCount}</b> ta
            kamera yozib borilmoqda (har kamerani alohida ham o'chirish mumkin —{' '}
            <Link to="/admin/boshqaruv/cameras" className="font-medium text-brand-600 hover:underline">Kameralar</Link>).
          </p>
        )}
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
