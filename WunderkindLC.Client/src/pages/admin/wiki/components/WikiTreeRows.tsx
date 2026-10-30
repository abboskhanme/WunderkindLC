import { ChevronDown, ChevronRight, FileText, Lock } from 'lucide-react'
import type { WikiTreeNode } from '@/api/services/wiki'
import { DropdownMenu, type DropdownMenuItem } from '@/components/ui/DropdownMenu'
import { cn } from '@/lib/utils'
import type { TreeItem } from '../lib/wikiModel'

interface WikiTreeRowsProps {
  items: TreeItem[]
  depth: number
  expanded: Set<string>
  activeId: string | undefined
  onOpen: (id: string) => void
  onToggle: (id: string) => void
  /** Qatordagi ⋯ menyusi (bo'sh — menyu chizilmaydi, masalan faqat o'qish rejimida) */
  menuFor?: (node: WikiTreeNode) => DropdownMenuItem[]
}

/**
 * Chap paneldagi sahifalar daraxti (rekursiv) — umumiy bo'limlar ham, eslatmalar ham shu komponent.
 * O'z qulfi bor sahifa yonida 🔒 belgisi (meros bo'yicha qulflanganlar — sahifa ichida aytiladi).
 */
export function WikiTreeRows(props: WikiTreeRowsProps) {
  const { items, depth, expanded, activeId, onOpen, onToggle, menuFor } = props

  return items.map(({ node, children }) => {
    const isOpen = expanded.has(node.id)
    const menu = menuFor?.(node) ?? []
    return (
      <div key={node.id}>
        <div
          className={cn(
            'group flex h-8 items-center gap-1 rounded-md pr-1 text-[13.5px]',
            node.id === activeId ? 'bg-brand-50 font-medium text-brand-700' : 'text-slate-700 hover:bg-slate-100',
          )}
          style={{ paddingLeft: 4 + depth * 14 }}
        >
          <button
            type="button"
            onClick={() => onToggle(node.id)}
            className={cn('flex h-5 w-5 shrink-0 items-center justify-center rounded text-slate-400 hover:bg-slate-200', !children.length && 'invisible')}
            aria-label={isOpen ? 'Yopish' : 'Ochish'}
          >
            {isOpen ? <ChevronDown size={14} /> : <ChevronRight size={14} />}
          </button>
          <button type="button" onClick={() => onOpen(node.id)} className="flex min-w-0 flex-1 items-center gap-1.5 text-left">
            {node.icon ? <span className="shrink-0">{node.icon}</span> : <FileText size={14} className="shrink-0 text-slate-400" />}
            <span className="truncate">{node.title}</span>
            {node.locked && <Lock size={12} className="shrink-0 text-amber-600" aria-label="Qulflangan" />}
          </button>
          {menu.length > 0 && (
            <div className="opacity-0 focus-within:opacity-100 group-hover:opacity-100">
              <DropdownMenu items={menu} triggerClassName="h-6 w-6" />
            </div>
          )}
        </div>
        {isOpen && children.length > 0 && <WikiTreeRows {...props} items={children} depth={depth + 1} />}
      </div>
    )
  })
}
