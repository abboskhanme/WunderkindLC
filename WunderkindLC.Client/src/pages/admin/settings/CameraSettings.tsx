import { useEffect, useState } from 'react'
import type { FormEvent } from 'react'
import { Check, CheckCircle2, XCircle, Info, HardDrive, AlertTriangle, Server, KeyRound } from 'lucide-react'
import { Link } from 'react-router-dom'
import { getCameraSettings, saveCameraSettings, type CameraConfig } from '@/api/services/settings'
import { Card } from '@/components/ui/Card'
import { Input } from '@/components/ui/Input'
import { Badge } from '@/components/ui/Badge'
import { Button } from '@/components/ui/Button'
import { Loader } from '@/components/ui/Loader'
import { apiErrorMessage, cn } from '@/lib/utils'

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
  /** Saqlash xatosi — ilgari `try` umuman yo'q edi: so'rov yiqilsa holat abadiy 'saving' da
   *  qolib, tugma o'chiq bo'lib qolardi (sahifani yangilashdan boshqa chora yo'q edi). */
  const [error, setError] = useState('')

  useEffect(() => {
    getCameraSettings().then(setCfg).finally(() => setLoading(false))
  }, [])

  const onSubmit = async (e: FormEvent) => {
    e.preventDefault()
    if (!cfg) return
    setStatus('saving')
    setError('')
    try {
      const saved = await saveCameraSettings({
        enabled: cfg.enabled, recordEnabled: cfg.recordEnabled,
        nvrEnabled: cfg.nvrEnabled, nvrHost: cfg.nvrHost,
        nvrRtspPort: cfg.nvrRtspPort, nvrIsapiPort: cfg.nvrIsapiPort, nvrVendor: cfg.nvrVendor,
      })
      setCfg(saved)
      setStatus('saved')
      setTimeout(() => setStatus('idle'), 2000)
    } catch (err) {
      setError(apiErrorMessage(err, "Saqlab bo'lmadi"))
      setStatus('idle')
    }
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

      {/* NVR — yozuv kartochkasidan OLDIN: bu AFZAL yo'l (bizda disk sarflanmaydi). */}
      <Card
        title={
          <span className="flex items-center gap-2">
            <Server className="h-4 w-4 text-slate-400" /> NVR arxivi
            {cfg.nvrEnabled && cfg.nvrHost && cfg.nvrCredentialsSet ? (
              <Badge tone="green"><CheckCircle2 className="h-3.5 w-3.5" /> Ulangan</Badge>
            ) : (
              <Badge tone="default"><XCircle className="h-3.5 w-3.5" /> Ulanmagan</Badge>
            )}
          </span>
        }
      >
        <p className="mb-4 text-sm text-slate-400">
          NVR (videoregistrator) allaqachon 24/7 <b>o'z disklariga</b> yozib turibdi. Bu yoqilsa
          biz hech narsa yozmaymiz — orqaga qaytarish va klip <b>NVR'dan</b> olinadi. Serverda
          disk sarflanmaydi, markazning interneti esa faqat kimdir arxiv so'raganda band bo'ladi.
        </p>

        <label className="mb-4 inline-flex cursor-pointer items-center gap-2 text-sm font-medium text-slate-700">
          <input type="checkbox" checked={cfg.nvrEnabled}
            onChange={(e) => setCfg({ ...cfg, nvrEnabled: e.target.checked })}
            className="h-4 w-4 rounded border-slate-300 accent-brand-600" />
          Arxivni NVR'dan olish
        </label>

        {cfg.nvrEnabled && (
          <>
            <div className="grid grid-cols-1 gap-4 sm:grid-cols-2">
              <Input label="NVR manzili (IP/host)" placeholder="192.168.1.50" value={cfg.nvrHost}
                onChange={(e) => setCfg({ ...cfg, nvrHost: e.target.value })} autoComplete="off" />
              <label className="flex flex-col gap-1 text-sm">
                <span className="font-medium text-slate-600">Rusum</span>
                <select value={cfg.nvrVendor} onChange={(e) => setCfg({ ...cfg, nvrVendor: e.target.value })}
                  className="rounded-lg border border-slate-200 bg-white px-3 py-2 text-sm text-slate-700 outline-none focus:border-brand-400">
                  <option value="hikvision">Hikvision</option>
                </select>
              </label>
              <Input label="RTSP porti" type="number" value={String(cfg.nvrRtspPort)}
                onChange={(e) => setCfg({ ...cfg, nvrRtspPort: Number(e.target.value) || 554 })} />
              <Input label="ISAPI (HTTP) porti" type="number" value={String(cfg.nvrIsapiPort)}
                onChange={(e) => setCfg({ ...cfg, nvrIsapiPort: Number(e.target.value) || 80 })} />
            </div>

            <div className={cn('mt-4 rounded-lg border px-3 py-2.5 text-xs',
              cfg.nvrCredentialsSet
                ? 'border-emerald-200 bg-emerald-50 text-emerald-900'
                : 'border-amber-200 bg-amber-50 text-amber-900')}>
              <div className="mb-1 flex items-center gap-1.5 font-medium">
                <KeyRound className="h-3.5 w-3.5" />
                {cfg.nvrCredentialsSet ? 'Login/parol berilgan' : 'Login/parol berilmagan'}
              </div>
              NVR login/paroli <b>bazada saqlanmaydi</b> — u faqat serverdagi <code>.env</code> faylida
              (turniketdagi bilan bir xil qoida): <code>NVR_USERNAME</code> va <code>NVR_PASSWORD</code>.
              {!cfg.nvrCredentialsSet && ' Ularsiz arxiv olinmaydi.'}
            </div>

            <div className="mt-4 rounded-lg bg-slate-50 px-3 py-2.5 text-xs text-slate-600">
              <div className="mb-1 font-medium text-slate-500">Keyingi qadam:</div>
              Har kameraning <b>NVR kanali</b>ni ko'rsating (Kameralar → kamera → Tahrirlash).
              Kanali 0 bo'lgan kamera NVR'da yo'q deb hisoblanadi. Kanal berilgach o'sha kamera
              kartasida <b>«Yozuvni tekshirish»</b> tugmasi bilan aloqani sinab ko'rish mumkin.
              {cfg.nvrCameraCount > 0 && (
                <div className="mt-1">Hozir <b>{cfg.nvrCameraCount}</b> ta kameraga kanal berilgan.</div>
              )}
            </div>
          </>
        )}
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
            <li><b>NVR kanali berilgan kamera bu yerda YOZILMAYDI</b> — arxiv NVR'da, ikkinchi
                nusxa saqlashning ma'nosi yo'q.</li>
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
        {error && <span className="text-sm font-medium text-red-600">{error}</span>}
      </div>
    </form>
  )
}
