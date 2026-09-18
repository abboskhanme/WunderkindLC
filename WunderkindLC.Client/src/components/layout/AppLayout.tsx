import { Suspense, useEffect, useState } from 'react'
import { Outlet } from 'react-router-dom'
import { UnreadProvider } from '@/context/unread-context'
import { Loader } from '@/components/ui/Loader'
import { Sidebar } from './Sidebar'
import { Topbar } from './Topbar'
import { CommandPalette } from './CommandPalette'

/** Desktopdagi yig'ilgan holat brauzerda eslab qolinadi (har sahifada qayta yig'ilmasin). */
const COLLAPSE_KEY = 'sidebar:collapsed'

export function AppLayout() {
  // MOBIL drawer: desktopda ochiq, mobil ekranda yopiq holatda boshlanadi.
  const [open, setOpen] = useState(
    () => typeof window !== 'undefined' && window.innerWidth >= 1024,
  )
  // DESKTOP: yon menyu yig'ilganmi. Mobil drawer'dan ALOHIDA holat — ikkalasi bir o'zgaruvchida
  // bo'lsa, oynani kichraytirib-kattalashtirganda holat chalkashib ketardi.
  const [collapsed, setCollapsed] = useState(() => {
    try {
      return typeof window !== 'undefined' && localStorage.getItem(COLLAPSE_KEY) === '1'
    } catch {
      return false
    }
  })

  const closeOnMobile = () => {
    if (window.innerWidth < 1024) setOpen(false)
  }

  /** Desktop: yon menyuni yig'ish/ochish (tugma yon menyuning o'zida, edutizimdagidek). */
  const toggleCollapse = () => {
    setCollapsed((c) => {
      const next = !c
      try {
        localStorage.setItem(COLLAPSE_KEY, next ? '1' : '0')
      } catch {
        /* brauzer xotirasi yopiq — holat faqat shu sessiyada */
      }
      return next
    })
  }

  // Breakpoint (lg=1024px) KESIB O'TILGANDA holatni moslaymiz: desktopga o'tilsa drawer ochiq
  // (desktopda sidebar baribir statik ko'rinadi), mobilga o'tilsa yopiq. matchMedia faqat chegara
  // o'zgarganda ishlaydi (har resize/scroll'da emas) — mobil drawer holatini buzmaydi.
  useEffect(() => {
    const mq = window.matchMedia('(min-width: 1024px)')
    const onChange = (e: MediaQueryListEvent) => setOpen(e.matches)
    mq.addEventListener('change', onChange)
    return () => mq.removeEventListener('change', onChange)
  }, [])

  return (
    <UnreadProvider>
      <CommandPalette />
      {/* edutizim tartibi: yuqori panel BUTUN eni bo'ylab, uning ostida yon menyu + kontent */}
      <div className="flex h-screen flex-col overflow-hidden">
        <Topbar onMenuClick={() => setOpen((o) => !o)} collapsed={collapsed} />

        <div className="relative flex flex-1 overflow-hidden">
          {/* Mobil uchun fon (orqa qoplama) */}
          {open && (
            <div
              onClick={() => setOpen(false)}
              className="fixed inset-0 z-30 bg-slate-900/40 lg:hidden"
            />
          )}

          <Sidebar
            open={open}
            collapsed={collapsed}
            onToggleCollapse={toggleCollapse}
            onNavigate={closeOnMobile}
          />

          <main className="flex-1 overflow-y-auto bg-[var(--bg)] px-3 py-2.5 sm:px-[15px]">
            {/* Suspense LAYOUT ICHIDA — lazy sahifa chunk'i yuklanayotganda faqat kontent
                maydoni almashadi; Sidebar/Topbar joyida qoladi (qayta mount bo'lmaydi,
                SignalR/unread ulanishlari uzilmaydi). */}
            <Suspense fallback={<Loader className="h-full min-h-[240px]" />}>
              <Outlet />
            </Suspense>
          </main>
        </div>
      </div>
    </UnreadProvider>
  )
}
