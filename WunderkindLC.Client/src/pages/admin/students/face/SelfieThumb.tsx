import { ImageOff } from 'lucide-react'
import { cn } from '@/lib/utils'
import { useFaceImage } from './useFaceImage'

interface Props {
  url: string
  alt: string
  /** Bosilganda — tafsilot oynasi. Berilmasa rasm oddiy ko'rinish bo'ladi. */
  onClick?: () => void
  className?: string
}

/**
 * SELFI kichik ko'rinishi.
 *
 * Rasm `<img src={url}>` bilan OLINMAYDI — biometrik surat statik yo'ldan berilmaydi va
 * JWT talab qiladigan endpointdan keladi (`useFaceImage` izohiga qarang). Fayl eskirib
 * o'chirilgan bo'lishi mumkin (maxfiylik uchun eski selfilar avtomatik o'chiriladi) —
 * o'shanda **buzuq rasm ikonkasi** o'rniga tushunarli holat ko'rsatiladi.
 */
export function SelfieThumb({ url, alt, onClick, className }: Props) {
  const { src, failed } = useFaceImage(url)

  if (!url || failed) {
    return (
      <div
        title={!url ? 'Selfi saqlanmagan' : "Rasmni ko'rsatib bo'lmadi (o'chirilgan bo'lishi mumkin)"}
        className={cn(
          'flex h-12 w-12 flex-col items-center justify-center gap-0.5 rounded-lg border border-dashed border-slate-200 bg-slate-50 text-slate-400',
          className,
        )}
      >
        <ImageOff className="h-4 w-4" />
        <span className="text-[9px] leading-none">yo'q</span>
      </div>
    )
  }

  return (
    <button
      type="button"
      onClick={onClick}
      disabled={!onClick}
      className={cn(
        'block h-12 w-12 overflow-hidden rounded-lg border border-slate-200 bg-slate-50',
        onClick && 'cursor-zoom-in transition hover:border-brand-400',
        className,
      )}
      title={onClick ? "Kattalashtirish uchun bosing" : undefined}
    >
      {src ? (
        <img src={src} alt={alt} className="h-full w-full object-cover" />
      ) : (
        // Yuklanmoqda — bo'sh kulrang katak (buzuq rasm ikonkasi chaqnab ketmasin).
        <span className="block h-full w-full animate-pulse bg-slate-100" />
      )}
    </button>
  )
}
