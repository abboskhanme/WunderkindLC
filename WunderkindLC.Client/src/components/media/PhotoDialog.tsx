import { useCallback, useEffect, useRef, useState } from 'react'
import {
  Camera, Upload, RotateCcw, Trash2, Loader2, AlertTriangle, Check, FlipHorizontal,
} from 'lucide-react'
import { uploadAdminFile } from '@/api/services/students'
import { Modal } from '@/components/ui/Modal'
import { Button } from '@/components/ui/Button'
import { apiErrorMessage } from '@/lib/utils'

/**
 * PROFIL RASMI — kameradan olish yoki fayldan tanlash. O'QUVCHI va O'QITUVCHI uchun BITTA
 * komponent (`StudentPhotoDialog` — shu yerga yo'naltiruvchi yupqa o'ram).
 *
 * Har ikkalasida ham ikki joydan ochiladi:
 *  • profil sahifasidagi DUMALOQ avatar (rasm bo'lmasa — bosilsa kamera, rasm bo'lsa —
 *    "Rasmni almashtirish");
 *  • tahrirlash formasidagi rasm maydonidagi «Kameradan olish» tugmasi.
 *
 * Dialog faqat FAYLNI YUKLAYDI va URL qaytaradi (`onSaved`) — uni qayerga yozish
 * chaqiruvchining ishi: sahifada darhol `PUT /{students|teachers}/{id}/photo`, formada esa
 * forma saqlanganda umumiy payload bilan.
 *
 * KAMERA: `getUserMedia` faqat XAVFSIZ kontekstda ishlaydi (https yoki localhost). Ishlamasa
 * (ruxsat berilmagan, kamera yo'q, http domen) — sabab ko'rsatiladi va FAYLDAN tanlash yo'li
 * ochiq qoladi. Oqim (stream) dialog yopilganda ALBATTA to'xtatiladi, aks holda kamera
 * chirog'i yonib qolardi.
 */
export function PhotoDialog({
  open,
  currentUrl,
  startWithCamera = false,
  title = 'Profil rasmi',
  hint,
  onClose,
  onSaved,
}: {
  open: boolean
  currentUrl: string | null
  /** Oyna sarlavhasi — masalan "O'quvchi rasmi" / "O'qituvchi rasmi". */
  title?: string
  /** Pastdagi tushuntirish matni (rasm qayerda ko'rinishi). */
  hint?: string
  /** true — ochilganda darhol kamera yoqiladi (rasm hali yo'q holat). */
  startWithCamera?: boolean
  onClose: () => void
  /** Yangi rasm manzili; `null` — rasm o'chirildi. */
  onSaved: (url: string | null) => void | Promise<void>
}) {
  const videoRef = useRef<HTMLVideoElement | null>(null)
  const streamRef = useRef<MediaStream | null>(null)
  const fileRef = useRef<HTMLInputElement | null>(null)

  const [camOn, setCamOn] = useState(false)
  const [camError, setCamError] = useState('')
  /**
   * Tasvirni AKS ETTIRISH (ko'zgu). Old kamera odatda ko'zgudek ko'rsatiladi — odam o'ngga
   * qimirlasa tasvir ham o'ngga siljiydi, shunisi tabiiy.
   *
   * MUHIM: kadr AYNAN SHU holatda saqlanadi. Ilgari faqat KO'RINISH aks ettirilar, saqlanadigan
   * rasm esa aks ettirilmasdi — natijada olingan surat ko'rganingizga nisbatan o'ngdan chapga
   * ag'darilib ketardi. Endi "nima ko'rsang — shuni olasan".
   */
  const [mirror, setMirror] = useState(true)
  /** Olingan/tanlangan rasm — hali yuklanmagan (oldindan ko'rish). */
  const [preview, setPreview] = useState<{ url: string; file: File } | null>(null)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState('')

  /** Kamera oqimini to'xtatadi (chiroq o'chsin). */
  const stopCamera = useCallback(() => {
    streamRef.current?.getTracks().forEach((t) => t.stop())
    streamRef.current = null
    if (videoRef.current) videoRef.current.srcObject = null
    setCamOn(false)
  }, [])

  const startCamera = useCallback(async () => {
    setCamError('')
    setError('')
    if (!navigator.mediaDevices?.getUserMedia) {
      setCamError(
        "Brauzer kamerani qo'llamaydi yoki sahifa xavfsiz emas (https/localhost kerak). " +
          'Fayldan tanlashingiz mumkin.',
      )
      return
    }
    try {
      const stream = await navigator.mediaDevices.getUserMedia({
        video: { facingMode: 'user', width: { ideal: 1280 }, height: { ideal: 1280 } },
        audio: false,
      })
      streamRef.current = stream
      setCamOn(true)
      // `srcObject` video elementi DOM'da paydo bo'lgandan keyin qo'yiladi.
      requestAnimationFrame(() => {
        if (videoRef.current) {
          videoRef.current.srcObject = stream
          void videoRef.current.play().catch(() => {})
        }
      })
    } catch (e) {
      const name = (e as DOMException)?.name ?? ''
      setCamError(
        name === 'NotAllowedError'
          ? "Kameraga ruxsat berilmadi. Brauzer manzil qatoridagi kamera belgisidan ruxsat bering yoki fayldan tanlang."
          : name === 'NotFoundError'
            ? 'Kamera topilmadi. Fayldan tanlashingiz mumkin.'
            : "Kamerani ochib bo'lmadi. Fayldan tanlashingiz mumkin.",
      )
    }
  }, [])

  // Dialog ochilganda/yopilganda holatni tiklash va kamerani to'xtatish.
  useEffect(() => {
    if (!open) {
      stopCamera()
      setPreview((p) => {
        if (p) URL.revokeObjectURL(p.url)
        return null
      })
      setError('')
      setCamError('')
      return
    }
    if (startWithCamera) void startCamera()
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [open])

  // Komponent butunlay olib tashlansa ham kamera o'chsin.
  useEffect(() => () => stopCamera(), [stopCamera])

  /**
   * Videodan KVADRAT kadr oladi (avatar dumaloq — markazdan qirqiladi).
   *
   * Aks ettirish (<see cref="mirror"/>) EKRANDAGIDEK qo'llanadi: ko'zguda ko'rganingiz aynan
   * shundayligicha saqlanadi. Aks holda surat o'ngdan chapga ag'darilib chiqardi.
   */
  const shoot = () => {
    const v = videoRef.current
    if (!v || !v.videoWidth) return
    const size = Math.min(v.videoWidth, v.videoHeight)
    const canvas = document.createElement('canvas')
    canvas.width = size
    canvas.height = size
    const ctx = canvas.getContext('2d')
    if (!ctx) return
    if (mirror) {
      // Gorizontal aks ettirish — ekrandagi `scale-x-[-1]` bilan bir xil natija.
      ctx.translate(size, 0)
      ctx.scale(-1, 1)
    }
    ctx.drawImage(v, (v.videoWidth - size) / 2, (v.videoHeight - size) / 2, size, size, 0, 0, size, size)
    canvas.toBlob(
      (blob) => {
        if (!blob) return
        const file = new File([blob], `photo-${Date.now()}.jpg`, { type: 'image/jpeg' })
        setPreview((p) => {
          if (p) URL.revokeObjectURL(p.url)
          return { url: URL.createObjectURL(blob), file }
        })
        stopCamera()
      },
      'image/jpeg',
      0.9,
    )
  }

  const pickFile = (file: File) => {
    if (!file.type.startsWith('image/')) {
      setError('Faqat rasm fayli (jpg, png, webp) tanlang')
      return
    }
    setError('')
    stopCamera()
    setPreview((p) => {
      if (p) URL.revokeObjectURL(p.url)
      return { url: URL.createObjectURL(file), file }
    })
  }

  const save = async () => {
    if (!preview || busy) return
    setBusy(true)
    setError('')
    try {
      const up = await uploadAdminFile(preview.file)
      await onSaved(up.url)
      onClose()
    } catch (e) {
      setError(apiErrorMessage(e, "Rasmni yuklab bo'lmadi"))
    } finally {
      setBusy(false)
    }
  }

  const removePhoto = async () => {
    if (busy) return
    setBusy(true)
    setError('')
    try {
      await onSaved(null)
      onClose()
    } catch (e) {
      setError(apiErrorMessage(e, "O'chirib bo'lmadi"))
    } finally {
      setBusy(false)
    }
  }

  const shown = preview?.url ?? currentUrl

  return (
    <Modal
      open={open}
      onClose={() => !busy && onClose()}
      // ENG KATTA oyna — rasmni bemalol ko'rish uchun.
      size="xl"
      title={title}
      footer={
        <>
          {currentUrl && !preview && (
            <Button variant="secondary" onClick={removePhoto} disabled={busy}>
              <Trash2 className="h-4 w-4" /> O'chirish
            </Button>
          )}
          <Button variant="secondary" onClick={onClose} disabled={busy}>
            Bekor qilish
          </Button>
          <Button onClick={save} disabled={!preview || busy}>
            {busy ? <Loader2 className="h-4 w-4 animate-spin" /> : <Check className="h-4 w-4" />}
            Saqlash
          </Button>
        </>
      }
    >
      <div className="space-y-4">
        {/*
          KATTA ko'rish maydoni — kvadrat (rasm ham kvadrat qirqiladi), balandligi ekranga
          moslashadi. Ichidagi doira — AVATARDA ko'rinadigan qism:
            • kamera yoqilganda "projektor" (tashqarisi qoraytiriladi) — kadrni to'g'ri
              joylashtirish uchun;
            • tayyor rasmda esa faqat ingichka chiziq — rasm to'silmasin, bemalol ko'rinsin.
        */}
        <div className="relative mx-auto aspect-square w-full max-w-[min(68vh,660px)] overflow-hidden rounded-2xl border border-slate-200 bg-slate-100">
          {camOn ? (
            <>
              {/* Ko'rinish va SAQLANADIGAN kadr bir xil aks ettirishda — «nima ko'rsang, shuni
                  olasan». Aks ettirishni pastdagi tugma bilan o'zgartirish mumkin. */}
              <video
                ref={videoRef}
                autoPlay
                playsInline
                muted
                className={mirror ? 'h-full w-full scale-x-[-1] object-cover' : 'h-full w-full object-cover'}
              />
              <div className="pointer-events-none absolute inset-[5%] rounded-full ring-2 ring-white/80 shadow-[0_0_0_9999px_rgba(15,23,42,0.38)]" />
            </>
          ) : shown ? (
            <>
              <img src={shown} alt="" className="h-full w-full object-cover" />
              <div className="pointer-events-none absolute inset-[5%] rounded-full ring-2 ring-white/60" />
            </>
          ) : (
            <div className="flex h-full w-full flex-col items-center justify-center gap-2 text-slate-300">
              <Camera className="h-16 w-16" />
              <span className="text-sm text-slate-400">Rasm hali yuklanmagan</span>
            </div>
          )}
        </div>

        {camError && (
          <p className="mx-auto flex max-w-[520px] items-start gap-1.5 rounded-lg bg-amber-50 px-3 py-2 text-xs text-amber-800">
            <AlertTriangle className="mt-0.5 h-3.5 w-3.5 shrink-0" />
            {camError}
          </p>
        )}
        {error && (
          <p className="mx-auto max-w-[520px] rounded-lg bg-red-50 px-3 py-2 text-sm text-red-600">
            {error}
          </p>
        )}

        {/* Amallar */}
        <div className="flex flex-wrap justify-center gap-2">
          {camOn ? (
            <>
              <Button onClick={shoot}>
                <Camera className="h-4 w-4" /> Suratga olish
              </Button>
              {/* Aks ettirish — ko'rinish ham, saqlanadigan kadr ham birga o'zgaradi.
                  Kiyimdagi yozuv teskari chiqsa shu tugma bilan to'g'rilanadi. */}
              <Button variant="secondary" onClick={() => setMirror((m) => !m)}>
                <FlipHorizontal className="h-4 w-4" /> Aks ettirish
              </Button>
              <Button variant="secondary" onClick={stopCamera}>
                To'xtatish
              </Button>
            </>
          ) : (
            <>
              <Button variant="secondary" onClick={() => void startCamera()} disabled={busy}>
                <Camera className="h-4 w-4" /> {preview ? 'Qaytadan olish' : 'Kameradan olish'}
              </Button>
              <Button variant="secondary" onClick={() => fileRef.current?.click()} disabled={busy}>
                <Upload className="h-4 w-4" /> Fayldan tanlash
              </Button>
              {preview && (
                <Button
                  variant="ghost"
                  onClick={() => {
                    URL.revokeObjectURL(preview.url)
                    setPreview(null)
                  }}
                  disabled={busy}
                >
                  <RotateCcw className="h-4 w-4" /> Bekor
                </Button>
              )}
            </>
          )}
        </div>

        <input
          ref={fileRef}
          type="file"
          accept="image/*"
          className="hidden"
          onChange={(e) => {
            const f = e.target.files?.[0]
            if (f) pickFile(f)
            e.target.value = ''
          }}
        />

        <p className="text-center text-xs text-slate-400">
          {camOn
            ? "Yuzni doira ichiga joylashtiring — aynan shu qism avatarda ko'rinadi. Surat ekranda ko'rganingizdek saqlanadi."
            : (hint ?? "Doira ichidagi qism profilda dumaloq avatar bo'lib chiqadi.")}
        </p>
      </div>
    </Modal>
  )
}
