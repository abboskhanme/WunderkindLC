import { useEffect, useMemo, useState } from 'react'
import {
  IconChevronLeft,
  IconChevronRight,
  IconChevronsLeft,
  IconChevronsRight,
  IconMenu2,
} from '@tabler/icons-react'
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
/** Standart hajm — edutizimdagidek 50 qator. */
export const DEFAULT_PAGE_SIZE = 50

export function usePagination<T>(items: T[], initialSize: number = DEFAULT_PAGE_SIZE): Pagination<T> {
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
 * Sahifalash paneli (jadval ostida) — edutizim ko'rinishida: pastki O'NG burchakda "≡ 50 qator"
 * tanlovi va ramkali tugmalar (birinchi · oldingi · raqamlar · keyingi · oxirgi).
 * `usePagination` qaytargan holat to'g'ridan-to'g'ri beriladi: `<TablePagination {...pg} />`.
 * Ro'yxat bo'm-bo'sh bo'lsa umuman chizilmaydi.
 */
export function TablePagination<T>({
  page, setPage, pageSize, setPageSize, totalPages, total, rangeFrom, rangeTo,
}: Pagination<T>) {
  if (total === 0) return null

  return (
    <div className="pagination flex-wrap gap-3">
      <span className="mr-auto text-[12px] text-[#6b7280]">
        {rangeFrom}–{rangeTo} / {total}
      </span>

      {/* "≡ 50 qator" — ramkali tugma ko'rinishidagi tanlov */}
      <label className="relative inline-flex h-8 items-center gap-1.5 rounded-lg border border-black/25 bg-white pl-2.5 pr-3 text-[13px] font-medium text-[#333]">
        <IconMenu2 className="h-4 w-4" />
        <span>{pageSize} qator</span>
        <select
          value={pageSize}
          onChange={(e) => setPageSize(Number(e.target.value))}
          className="absolute inset-0 cursor-pointer opacity-0"
          aria-label="Qatorlar soni"
        >
          {PAGE_SIZES.map((n) => (
            <option key={n} value={n}>
              {n} qator
            </option>
          ))}
        </select>
      </label>

      <div className="pages items-center">
        <button type="button" className="pg-btn" disabled={page <= 1} onClick={() => setPage(1)} title="Birinchi sahifa">
          <IconChevronsLeft className="h-4 w-4" />
        </button>
        <button type="button" className="pg-btn" disabled={page <= 1} onClick={() => setPage(page - 1)} title="Oldingi sahifa">
          <IconChevronLeft className="h-4 w-4" />
        </button>
        {pageWindow(page, totalPages).map((p, i) =>
          p === '…' ? (
            <span key={`gap-${i}`} className="px-1 text-slate-400">
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
        <button type="button" className="pg-btn" disabled={page >= totalPages} onClick={() => setPage(page + 1)} title="Keyingi sahifa">
          <IconChevronRight className="h-4 w-4" />
        </button>
        <button type="button" className="pg-btn" disabled={page >= totalPages} onClick={() => setPage(totalPages)} title="Oxirgi sahifa">
          <IconChevronsRight className="h-4 w-4" />
        </button>
      </div>
    </div>
  )
}
