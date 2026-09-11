import { useEffect } from 'react'
import { X, ImageOff } from 'lucide-react'

interface PhotoViewerModalProps {
  open: boolean
  onClose: () => void
  /** Rasm manzili ("/uploads/..."). Bo'sh/undefined bo'lsa "rasm yuklanmagan" holati ko'rsatiladi. */
  url?: string | null
  /** Sarlavha — odatda o'quvchining F.I.SH */
  title?: string
  /** Rasm bo'lmaganda o'rniga chiqadigan harflar. Berilmasa `title`dan olinadi. */
  initials?: string
}

/** "Voxidjonov Abduxalil" -> "VA" (rasm yo'q holati uchun). */
function initialsOf(name: string): string {
  return name
    .trim()
    .split(/\s+/)
    .slice(0, 2)
    .map((w) => w[0]?.toUpperCase() ?? '')
    .join('')
}

/**
 * Rasmni to'liq ko'rish oynasi (lightbox).
 *
 * TELEFON va KOMPYUTER uchun bitta komponent, farq faqat CSS'da:
 *  - telefonda rasm ekran kengligiga moslashadi (`max-w-[92vw]`, balandligi `70vh`) — barmoq bilan
 *    yopish uchun X tugmasi kattaroq va rasmning USTIDA emas, yonida turadi;
 *  - `sm:` dan boshlab (≥640px) o'lchov kattayadi (`80vh`), kompyuterda rasm to'liq ko'rinadi.
 * Yangi tab ochilmaydi — o'qituvchi jurnaldan chiqib ketmasligi kerak.
 *
 * Yopish: X tugmasi, fon bosilishi yoki Escape.
 */
export function PhotoViewerModal({ open, onClose, url, title, initials }: PhotoViewerModalProps) {
  useEffect(() => {
    if (!open) return
    const onKey = (e: KeyboardEvent) => e.key === 'Escape' && onClose()
    window.addEventListener('keydown', onKey)
    return () => window.removeEventListener('keydown', onKey)
  }, [open, onClose])

  if (!open) return null

  return (
    <div className="fixed inset-0 z-[60] flex items-center justify-center p-4">
      <div className="absolute inset-0 bg-slate-900/70 backdrop-blur-[2px]" onClick={onClose} />
      <div className="relative z-10 flex max-h-full w-auto max-w-[92vw] flex-col items-center gap-3 sm:max-w-[70vw]">
        <div className="flex w-full items-center justify-between gap-3 text-white">
          <span className="min-w-0 truncate text-sm font-semibold sm:text-base">{title}</span>
          <button
            type="button"
            onClick={onClose}
            aria-label="Yopish"
            className="flex h-9 w-9 shrink-0 items-center justify-center rounded-full bg-white/15 text-white transition-colors hover:bg-white/25"
          >
            <X className="h-5 w-5" />
          </button>
        </div>

        {url ? (
          <img
            src={url}
            alt={title ?? ''}
            className="max-h-[70vh] w-auto max-w-full rounded-xl bg-white object-contain shadow-2xl sm:max-h-[80vh]"
          />
        ) : (
          // Rasm yuklanmagan — bo'sh oyna o'rniga aniq xabar (o'qituvchi rasm qo'sha olmaydi,
          // shuning uchun bu yerda faqat ma'lumot beriladi).
          <div className="flex w-[80vw] max-w-xs flex-col items-center gap-3 rounded-xl bg-white px-6 py-8 shadow-2xl">
            <div className="flex h-24 w-24 items-center justify-center rounded-full bg-slate-100 text-2xl font-semibold text-slate-400">
              {initials || (title ? initialsOf(title) : <ImageOff className="h-8 w-8" />)}
            </div>
            <p className="text-center text-sm text-slate-500">Rasm yuklanmagan</p>
          </div>
        )}
      </div>
    </div>
  )
}
