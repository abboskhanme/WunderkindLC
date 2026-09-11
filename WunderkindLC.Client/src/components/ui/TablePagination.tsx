import { useEffect, useMemo, useState } from 'react'
import { ChevronLeft, ChevronRight } from 'lucide-react'
import { cn } from '@/lib/utils'

/**
 * JADVAL SAHIFALASH — ro'yxat uzun bo'lganda brauzerni cho'ktirmaslik uchun (moliya amallari,
 * to'lovlar, vozvratlar ...). Ma'lumot MIJOZ tomonida bo'lakka bo'linadi: filtr/qidiruv allaqachon
 * shu yerda ishlaydi, shuning uchun server so'rovi kerak emas.
 */

/** Sahifa hajmi variantlari — foydalanuvchi tanlaydi. */
export const PAGE_SIZES = [20, 30, 50, 100] as const

export interface Pagination<T> {
  /** Joriy sahifadagi elementlar. */
  paged: T[]
  page: number
  setPage: (p: number) => void
  pageSize: number
  setPageSize: (n: number) => void
  totalPages: number
  total: number
  /** Ko'rinayotgan diapazon (1-asosli, "21–40 / 137" uchun). */
  rangeFrom: number
  rangeTo: number
}

/**
 * Ro'yxatni sahifalarga bo'ladi. Filtr o'zgarib ro'yxat qisqarsa yoki sahifa hajmi almashsa —
 * birinchi sahifaga qaytadi (bo'sh sahifada "hech narsa yo'q" ko'rinib qolmasin).
 */
export function usePagination<T>(items: T[], initialSize: number = PAGE_SIZES[0]): Pagination<T> {
  const [page, setPage] = useState(1)
  const [pageSize, setPageSize] = useState<number>(initialSize)

  const totalPages = Math.max(1, Math.ceil(items.length / pageSize))
  const current = Math.min(page, totalPages)

  // Ro'yxat (filtr/qidiruv) yoki sahifa hajmi o'zgarsa — boshiga.
  useEffect(() => {
    setPage(1)
  }, [items.length, pageSize])

  const paged = useMemo(
    () => items.slice((current - 1) * pageSize, current * pageSize),
    [items, current, pageSize],
  )

  return {
    paged,
    page: current,
    setPage,
    pageSize,
    setPageSize,
    totalPages,
    total: items.length,
    rangeFrom: items.length === 0 ? 0 : (current - 1) * pageSize + 1,
    rangeTo: Math.min(items.length, current * pageSize),
  }
}

/** Ko'rsatiladigan sahifa raqamlari — ko'p sahifada oynali ("1 … 4 5 6 … 20"). */
function pageWindow(page: number, totalPages: number): (number | '…')[] {
  if (totalPages <= 7) return Array.from({ length: totalPages }, (_, i) => i + 1)
  const out: (number | '…')[] = [1]
  const from = Math.max(2, page - 1)
  const to = Math.min(totalPages - 1, page + 1)
  if (from > 2) out.push('…')
  for (let i = from; i <= to; i++) out.push(i)
  if (to < totalPages - 1) out.push('…')
  out.push(totalPages)
  return out
}

/**
 * Sahifalash paneli (jadval ostida). `usePagination` qaytargan holatni to'g'ridan-to'g'ri beriladi:
 * `<TablePagination {...pg} />`. Bitta sahifa bo'lsa ham hajm tanlovi ko'rinadi (foydalanuvchi
 * ro'yxatni kengaytira olsin), lekin ro'yxat bo'm-bo'sh bo'lsa umuman chizilmaydi.
 */
export function TablePagination<T>({
  page, setPage, pageSize, setPageSize, totalPages, total, rangeFrom, rangeTo,
}: Pagination<T>) {
  if (total === 0) return null

  return (
    <div className="pagination flex-wrap gap-3">
      <div className="flex items-center gap-2">
        <span>Sahifada:</span>
        <select
          value={pageSize}
          onChange={(e) => setPageSize(Number(e.target.value))}
          className="rounded-lg border border-slate-200 bg-white px-2 py-1 text-xs font-semibold text-slate-600 outline-none focus:border-brand-400"
        >
          {PAGE_SIZES.map((n) => (
            <option key={n} value={n}>
              {n} ta
            </option>
          ))}
        </select>
        <span className="font-mono text-slate-400">
          {rangeFrom}–{rangeTo} / {total}
        </span>
      </div>

      <div className="pages items-center">
        <button
          type="button"
          className="pg-btn"
          disabled={page <= 1}
          onClick={() => setPage(page - 1)}
          title="Oldingi sahifa"
        >
          <ChevronLeft className="h-3.5 w-3.5" />
        </button>
        {pageWindow(page, totalPages).map((p, i) =>
          p === '…' ? (
            <span key={`gap-${i}`} className="px-1 text-slate-300">
              …
            </span>
          ) : (
            <button
              key={p}
              type="button"
              className={cn('pg-btn', p === page && 'active')}
              onClick={() => setPage(p)}
            >
              {p}
            </button>
          ),
        )}
        <button
          type="button"
          className="pg-btn"
          disabled={page >= totalPages}
          onClick={() => setPage(page + 1)}
          title="Keyingi sahifa"
        >
          <ChevronRight className="h-3.5 w-3.5" />
        </button>
      </div>
    </div>
  )
}
