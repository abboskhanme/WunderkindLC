import { NavLink } from 'react-router-dom'
import { cn } from '@/lib/utils'

export interface CardTabItem {
  /** Card ustidagi matn */
  label: string
  /** Manzil (marshrut) — faol holat AYNAN shu manzil bo'yicha aniqlanadi */
  to: string
  /** NavLink aniq moslik (faqat shu manzilda faol) — ichki marshrutlari bor bo'lim uchun */
  end?: boolean
  /** Ruxsat yo'q (yoki kerak emas) — card umuman chizilmaydi */
  hidden?: boolean
}

interface CardTabsProps {
  items: CardTabItem[]
  className?: string
}

/**
 * Bir bo'limning sahifalari orasida o'tish uchun CARD ko'rinishidagi tugmalar qatori.
 *
 * NEGA `NavLink`: har card ALOHIDA marshrut bo'lib qoladi — deep-link, brauzer "orqaga"
 * tugmasi va sahifani yangilash ishlayveradi (lokal `useState` tab bilan bunday bo'lmasdi).
 * Faol card manzil bo'yicha o'zi aniqlanadi.
 *
 * Tor ekranda qator gorizontal scroll bo'ladi (cardlar siqilib buzilmasin).
 *
 * ⚠️ Pastki bo'shliq ATAYIN yo'q: chaqiruvchi o'zi beradi (`className="mb-5"`) yoki ota `space-y-*`
 * dan oladi. Loyihadagi `cn()` — oddiy `join` (tailwind-merge EMAS), shuning uchun ichkarida
 * `mb-5` turganda uni tashqaridan bekor qilib bo'lmasdi.
 */
export function CardTabs({ items, className }: CardTabsProps) {
  const visible = items.filter((i) => !i.hidden)
  if (visible.length === 0) return null

  return (
    <nav
      className={cn(
        // `-mx-1 px-1` — cardlar soyasi scroll konteynerida kesilmasligi uchun
        '-mx-1 flex gap-2 overflow-x-auto px-1 pb-1',
        className,
      )}
    >
      {visible.map((i) => (
        <NavLink
          key={i.to}
          to={i.to}
          end={i.end}
          className={({ isActive }) =>
            cn(
              'shrink-0 rounded-xl border px-4 py-2.5 text-sm font-semibold shadow-[var(--shadow-1)] transition-colors',
              isActive
                ? 'border-brand-500 bg-brand-50 text-brand-700'
                : 'border-slate-200 bg-white text-slate-600 hover:border-slate-300 hover:bg-slate-50',
            )
          }
        >
          {i.label}
        </NavLink>
      ))}
    </nav>
  )
}
