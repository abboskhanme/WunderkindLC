import type { ReactNode } from 'react'
import { Link, useLocation } from 'react-router-dom'
import { BookOpen, Search } from 'lucide-react'
import { usePerm } from '@/lib/permissions'
import { cn } from '@/lib/utils'
import { wikiTabOf } from '../lib/wikiModel'
import '@blocknote/core/fonts/inter.css'
import '../lib/wiki.css'

/**
 * Tepadagi tablar (egasining talabi, 2026-10-25): rahbariyat/menejerda — «Eslatmalarim | Xodimlar», oddiy xodimda —
 * faqat «Eslatmalarim». «Umumiy» (SOP bo'limlari) tab sifatida OLIB TASHLANDI. Ruxsatlar — `constants.ts` → `wiki`.
 */
const TABS = [
  // «Eslatmalarim» — ruxsatsiz, har bir xodimga (egasining talabi, 2026-10-27).
  { key: 'notes', to: '/admin/bilimlar/eslatmalar', label: 'Eslatmalarim', perm: null },
  { key: 'team', to: '/admin/bilimlar/xodimlar', label: 'Xodimlar', perm: 'wiki.team' },
] as const
interface WikiShellProps {
  /** Sarlavha ostidagi qism nomi tablarsiz rejimda (o'qituvchi portali) */
  title?: string
  /** Admin paneli: Eslatmalarim / Xodimlar tablari */
  showTabs?: boolean
  /** «Xodimlar» tabi: faqat rahbariyat yoki ko'radigan xodimi bor menejer (ruxsat kaliti yetmaydi — NotesPage hal qiladi) */
  showTeamTab?: boolean
  search?: { value: string; onChange: (v: string) => void; placeholder?: string }
  /** Chap panel ichi (daraxt, natijalar) */
  sidebar: ReactNode
  /** Chap panel pasti (masalan «Yangi bo'lim») */
  sidebarFooter?: ReactNode
  /** O'ng panel; `hasPage` false bo'lsa mobil ekranda yashirinadi (daraxt ko'rinadi) */
  main: ReactNode
  hasPage: boolean
  /** Konteyner balandligi — admin layoutida yuqori panel 56px + chekinish 20px */
  heightClass?: string
}

/**
 * Bilimlar bazasi sahifalarining UMUMIY ko'rinishi: chapda panel (tablar, qidiruv, daraxt), o'ngda sahifa.
 * Mobilda bittasi ko'rinadi: sahifa ochiq bo'lsa — sahifa, aks holda — daraxt.
 */
export function WikiShell({ title, showTabs, showTeamTab, search, sidebar, sidebarFooter, main, hasPage, heightClass }: WikiShellProps) {
  const { can } = usePerm()
  const activeTab = wikiTabOf(useLocation().pathname)
  const tabs = showTabs ? TABS.filter((t) => (t.perm === null || can(t.perm, 'view')) && (t.key !== 'team' || showTeamTab)) : []

  return (
    <div className={cn('wiki-font flex gap-2.5', heightClass ?? 'h-[calc(100dvh-76px)]')}>
      <aside className={cn('w-full shrink-0 flex-col overflow-hidden rounded-xl bg-white lg:flex lg:w-[290px]', hasPage ? 'hidden' : 'flex')}>
        <div className="space-y-2 border-b border-slate-100 p-3">
          <div className="flex items-center gap-2 text-[15px] font-semibold text-slate-800">
            <BookOpen size={18} className="text-brand-600" /> {title ?? 'Bilimlar bazasi'}
          </div>
          {/* Bitta tab ham ko'rsatiladi — oddiy xodim tepada «Eslatmalarim» ni ko'rsin (egasining talabi). */}
          {tabs.length > 0 && (
            <nav className="flex rounded-lg bg-slate-100 p-0.5 text-[12.5px]">
              {tabs.map((t) => (
                <Link
                  key={t.to}
                  to={t.to}
                  className={cn(
                    'flex-1 rounded-md py-1 text-center font-medium transition-colors',
                    t.key === activeTab ? 'bg-white text-slate-900 shadow-sm' : 'text-slate-500 hover:text-slate-700',
                  )}
                >
                  {t.label}
                </Link>
              ))}
            </nav>
          )}
          {search && (
            <div className="relative">
              <Search size={14} className="pointer-events-none absolute left-2.5 top-1/2 -translate-y-1/2 text-slate-400" />
              <input
                value={search.value}
                onChange={(e) => search.onChange(e.target.value)}
                placeholder={search.placeholder ?? 'Qidirish…'}
                className="h-8 w-full rounded-md border border-slate-200 bg-slate-50 pl-8 pr-2 text-[13px] outline-none focus:border-brand-500 focus:bg-white"
              />
            </div>
          )}
        </div>
        <div className="flex-1 overflow-y-auto p-2">{sidebar}</div>
        {sidebarFooter && <div className="border-t border-slate-100 p-2">{sidebarFooter}</div>}
      </aside>

      <section className={cn('min-w-0 flex-1 flex-col overflow-hidden rounded-xl bg-white lg:flex', hasPage ? 'flex' : 'hidden')}>
        {main}
      </section>
    </div>
  )
}

/** Sahifa tanlanmaganda o'ng panel. */
export function WikiEmptyState({ title, hint }: { title: string; hint: string }) {
  return (
    <div className="flex flex-1 flex-col items-center justify-center p-8 text-center text-slate-500">
      <BookOpen size={44} className="mb-3 text-brand-200" />
      <div className="text-[16px] font-medium text-slate-700">{title}</div>
      <div className="mt-1 max-w-sm text-[13.5px]">{hint}</div>
    </div>
  )
}
