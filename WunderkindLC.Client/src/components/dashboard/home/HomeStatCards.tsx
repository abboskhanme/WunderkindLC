import { useNavigate } from 'react-router-dom'
import type { DashboardSummary } from '@/api/services/dashboard'
import { usePerm } from '@/lib/permissions'
import { cn } from '@/lib/utils'
import { HOME_CARDS } from '@/lib/homeCards'

/**
 * Bosh sahifaning 12 ta kartochkasi (ro'yxat, ranglar va ruxsatlar — `lib/homeCards.ts`).
 * Foydalanuvchi ko'ra olmaydigan kartochka chizilmaydi; bittasi ham qolmasa — blok umuman yo'q.
 */
export function HomeStatCards({ data, loading }: { data: DashboardSummary | null; loading: boolean }) {
  const { can } = usePerm()
  const navigate = useNavigate()
  const visible = HOME_CARDS.filter((c) => can(c.perm, 'view'))
  if (visible.length === 0) return null

  return (
    <div className="grid grid-cols-2 gap-2.5 md:grid-cols-3 xl:grid-cols-6">
      {visible.map((c) => {
        const Icon = c.icon
        const clickable = can(c.linkPerm, 'view')
        const value = data ? data[c.key] : null
        return (
          <button
            key={c.key}
            type="button"
            disabled={!clickable}
            onClick={() => clickable && navigate(c.to)}
            className={cn(
              'flex h-[76px] min-w-0 items-center gap-2 rounded-xl border border-[#DBE0E6] bg-white px-2 pb-1 pt-2 text-left shadow-[0_1px_2px_rgba(0,0,0,0.05)] transition-shadow',
              clickable ? 'cursor-pointer hover:shadow-md' : 'cursor-default',
            )}
          >
            <span
              className="flex h-10 w-10 shrink-0 items-center justify-center rounded-lg text-white"
              style={{ backgroundColor: c.color }}
            >
              <Icon size={28} stroke={2} />
            </span>
            <span className="flex min-w-0 flex-col">
              <span className="line-clamp-2 text-[12px] font-normal leading-[1.25] text-black">{c.label}</span>
              <span className="text-[18px] font-semibold leading-tight text-black">
                {value !== null ? value.toLocaleString('ru-RU') : loading ? '…' : '—'}
              </span>
            </span>
          </button>
        )
      })}
    </div>
  )
}
