import { useEffect, useRef, useState } from 'react'
import { IconBuilding, IconCheck, IconChevronDown } from '@tabler/icons-react'
import { getBranchLinks, type BranchLink } from '@/api/services/branchLinks'
import { cn } from '@/lib/utils'

/**
 * Yuqori paneldagi markaz/filial bloki (edutizimda filial tanlash turadigan joy).
 * Superadmin va 2+ filial bo'lsa — TANLOV: boshqa filial alohida ilova (o'z bazasi, o'z login/parollari), ya'ni
 * o'tish = o'sha manzilga borish, u yerda superadmin o'z login/paroli bilan kiradi. Qolganlarga — oddiy yorliq.
 */
export function BranchSwitcher({ name, isSuper }: { name: string; isSuper: boolean }) {
  const [links, setLinks] = useState<BranchLink[]>([])
  const [open, setOpen] = useState(false)
  const ref = useRef<HTMLDivElement>(null)

  useEffect(() => {
    if (!isSuper) return
    let alive = true
    getBranchLinks()
      .then((d) => {
        if (alive) setLinks(d)
      })
      .catch(() => {
        /* ikkilamchi ma'lumot — ro'yxat kelmasa oddiy yorliq qoladi */
      })
    return () => {
      alive = false
    }
  }, [isSuper])

  useEffect(() => {
    if (!open) return
    const onClick = (e: MouseEvent) => {
      if (ref.current && !ref.current.contains(e.target as Node)) setOpen(false)
    }
    document.addEventListener('mousedown', onClick)
    return () => document.removeEventListener('mousedown', onClick)
  }, [open])

  const current = links.find((l) => l.current)
  const label = current?.name ?? name
  const box = 'hidden h-9 w-[200px] shrink-0 items-center gap-2 rounded-lg border border-[#dbe0e6] px-3 text-sm text-[#333] md:flex'

  if (!isSuper || links.length < 2) {
    return (
      <div className={box}>
        <IconBuilding className="h-5 w-5 shrink-0 text-[#667085]" />
        <span className="truncate">{label}</span>
      </div>
    )
  }

  return (
    <div className="relative hidden md:block" ref={ref}>
      <button
        type="button"
        onClick={() => setOpen((o) => !o)}
        className={cn(box, 'hover:border-brand-600/40')}
        title="Filialni almashtirish"
        aria-haspopup="menu"
        aria-expanded={open}
      >
        <IconBuilding className="h-5 w-5 shrink-0 text-[#667085]" />
        <span className="min-w-0 flex-1 truncate text-left">{label}</span>
        <IconChevronDown className="h-4 w-4 shrink-0 text-[#667085]" />
      </button>
      {open && (
        <div
          role="menu"
          className="absolute left-0 top-full z-40 mt-2 w-64 overflow-hidden rounded-xl border border-[#dbe0e6] bg-white py-1.5 shadow-lg"
        >
          <p className="px-3 pb-1.5 pt-0.5 text-[11px] text-[#667085]">
            Har filial — alohida baza. Boshqa filialda o'z login/parolingiz bilan kirasiz.
          </p>
          {links.map((l) => (
            <a
              key={l.url}
              role="menuitem"
              href={l.current ? undefined : `${l.url}/admin`}
              onClick={() => setOpen(false)}
              className={cn(
                'mx-1.5 flex items-center gap-2 rounded-lg px-2.5 py-2 text-sm font-semibold',
                l.current ? 'cursor-default text-brand-600' : 'text-[#333] hover:bg-[#e6eaea] hover:text-brand-600',
              )}
            >
              <span className="min-w-0 flex-1 truncate">{l.name}</span>
              {l.current && <IconCheck className="h-4 w-4 shrink-0" />}
            </a>
          ))}
        </div>
      )}
    </div>
  )
}
