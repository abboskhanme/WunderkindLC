import type { ReactNode } from 'react'
import { IconArrowDown, IconArrowUp, IconInbox } from '@tabler/icons-react'
import { cn } from '@/lib/utils'

export interface DataColumn<T> {
  key: string
  /** Sarlavha — KATTA HARFLARGA CSS o'zi aylantiradi, bu yerda oddiy yoziladi. */
  header: ReactNode
  render: (row: T, index: number) => ReactNode
  /** Ustun kengligi (px yoki CSS qiymat). Berilmasa kontentga qarab. */
  width?: number | string
  align?: 'left' | 'center' | 'right'
  /** Saralanadigan ustun — bosilganda `onSort(key)` chaqiriladi. */
  sortable?: boolean
}

export interface DataTableProps<T> {
  rows: T[]
  columns: DataColumn<T>[]
  rowKey: (row: T) => string
  loading?: boolean
  /** Qator bosilganda (ixtiyoriy). edutizimda odatda faqat ISM havola bo'ladi. */
  onRowClick?: (row: T) => void
  /** Qatorga qo'shimcha class — masalan o'tib ketgan sana uchun och-qizil (`#FFCACA`). */
  rowClassName?: (row: T) => string | undefined
  /** Birinchi ustun — tartib raqami (№). Sahifalashda `offset` beriladi. */
  numbered?: boolean
  offset?: number
  sortKey?: string
  sortDir?: 'asc' | 'desc'
  onSort?: (key: string) => void
  /** Jadval ostidagi panel (odatda `<TablePagination />`). */
  footer?: ReactNode
  className?: string
}

/**
 * edutizimdagi MUI X DataGrid ko'rinishidagi jadval: oq karta (yuqori burchaklari 12px),
 * sarlavha 56px / 12px / 600 / KATTA HARFLAR, qatorlar 52px / 13px / 500, gorizontal aylantirish,
 * yopishqoq sarlavha. Yuklanayotganda kulrang "skelet" chiziqlar, bo'sh bo'lsa
 * "Ma'lumotlar topilmadi".
 */
export function DataTable<T>({
  rows,
  columns,
  rowKey,
  loading,
  onRowClick,
  rowClassName,
  numbered,
  offset = 0,
  sortKey,
  sortDir,
  onSort,
  footer,
  className,
}: DataTableProps<T>) {
  const cols: DataColumn<T>[] = numbered
    ? [{ key: '__n', header: '№', width: 56, render: (_r, i) => offset + i + 1 }, ...columns]
    : columns

  return (
    <div className={cn('overflow-hidden rounded-t-xl border border-[#dbe0e6] bg-white', className)}>
      <div className="max-h-[calc(100vh-220px)] overflow-auto">
        <table className="w-full border-collapse text-[13px] font-medium text-black">
          <thead className="sticky top-0 z-10 bg-white">
            <tr>
              {cols.map((c) => (
                <th
                  key={c.key}
                  style={{ width: c.width, minWidth: c.width }}
                  className={cn(
                    'h-14 whitespace-nowrap border-b border-[#e0e0e0] px-2.5 text-[12px] font-semibold uppercase text-black',
                    c.align === 'right' ? 'text-right' : c.align === 'center' ? 'text-center' : 'text-left',
                  )}
                >
                  {c.sortable && onSort ? (
                    <button
                      type="button"
                      onClick={() => onSort(c.key)}
                      className="group inline-flex items-center gap-1 uppercase"
                      title="Saralash"
                    >
                      {c.header}
                      {sortKey === c.key ? (
                        sortDir === 'asc' ? (
                          <IconArrowUp className="h-4 w-4 text-[#757575]" />
                        ) : (
                          <IconArrowDown className="h-4 w-4 text-[#757575]" />
                        )
                      ) : (
                        <IconArrowDown className="h-4 w-4 text-[#757575] opacity-0 group-hover:opacity-50" />
                      )}
                    </button>
                  ) : (
                    c.header
                  )}
                </th>
              ))}
            </tr>
          </thead>
          <tbody>
            {loading
              ? Array.from({ length: 8 }, (_, i) => (
                  <tr key={`sk-${i}`}>
                    {cols.map((c) => (
                      <td key={c.key} className="h-[52px] border-b border-[#e0e0e0] px-2.5">
                        <div className="h-2.5 w-3/4 animate-pulse rounded-full bg-[#ebebeb]" />
                      </td>
                    ))}
                  </tr>
                ))
              : rows.map((r, i) => (
                  <tr
                    key={rowKey(r)}
                    onClick={onRowClick ? () => onRowClick(r) : undefined}
                    className={cn(
                      'transition-colors hover:bg-black/[0.04]',
                      onRowClick && 'cursor-pointer',
                      rowClassName?.(r),
                    )}
                  >
                    {cols.map((c) => (
                      <td
                        key={c.key}
                        className={cn(
                          'h-[52px] border-b border-[#e0e0e0] px-2.5 align-middle',
                          c.align === 'right' ? 'text-right' : c.align === 'center' ? 'text-center' : 'text-left',
                        )}
                      >
                        {c.render(r, i)}
                      </td>
                    ))}
                  </tr>
                ))}
          </tbody>
        </table>

        {!loading && rows.length === 0 && (
          <div className="flex flex-col items-center justify-center gap-1 py-14 text-center">
            <IconInbox className="h-10 w-10 text-[#bdbdbd]" stroke={1.5} />
            <p className="text-sm font-semibold text-[#333]">Ma'lumotlar topilmadi</p>
            <p className="text-xs text-[#9e9e9e]">Ma'lumotlar topilmadi. Filterni o'zgartirib ko'ring.</p>
          </div>
        )}
      </div>
      {footer}
    </div>
  )
}
