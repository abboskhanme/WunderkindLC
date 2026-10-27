import type { WikiSearchHit } from '@/api/services/wiki'
import { cn } from '@/lib/utils'

interface WikiSearchResultsProps {
  hits: WikiSearchHit[]
  activeId: string | undefined
  onOpen: (id: string) => void
  error?: string
}

/** Qidiruv natijalari (chap panelda daraxt o'rniga): sarlavha, bo'lim va topilgan joy parchasi. */
export function WikiSearchResults({ hits, activeId, onOpen, error }: WikiSearchResultsProps) {
  if (error) return <div className="rounded-md bg-red-50 p-2 text-[13px] text-[#d32f2f]">{error}</div>
  return (
    <div className="space-y-1">
      <div className="px-1 pb-1 text-[12px] text-slate-500">{hits.length ? `${hits.length} ta natija` : 'Hech narsa topilmadi'}</div>
      {hits.map((h) => (
        <button
          key={h.pageId}
          type="button"
          onClick={() => onOpen(h.pageId)}
          className={cn('block w-full rounded-md px-2 py-1.5 text-left hover:bg-slate-100', h.pageId === activeId && 'bg-brand-50')}
        >
          <div className="truncate text-[13.5px] font-medium text-slate-800">{h.icon} {h.title}</div>
          {h.snippet && <div className="line-clamp-2 text-[12px] text-slate-500">{h.snippet}</div>}
        </button>
      ))}
    </div>
  )
}
