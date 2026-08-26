import { Suspense } from 'react'
import { NavLink, Outlet } from 'react-router-dom'
import { Wallet, ReceiptText, LogOut } from 'lucide-react'
import { useAuth } from '@/context/auth-context'
import { Loader } from '@/components/ui/Loader'
import { cn } from '@/lib/utils'

const TABS = [
  { to: '/kassa', label: "To'lov", icon: Wallet, end: true },
  { to: '/kassa/payments', label: "To'lovlarim", icon: ReceiptText },
]

/**
 * KASSA portali — TELEFON uchun qobiq (kassirning yagona ish o'rni). Admin paneli (yon menyu,
 * bosh sahifa, boshqa bo'limlar) YO'Q: kassir faqat to'lov qabul qiladi va o'z to'lovlarini ko'radi.
 * Pastda 2 tab: "To'lov" (o'quvchini topib to'lov kiritish) va "To'lovlarim" (o'zi kiritganlari).
 *
 * Admin/superadmin ham bu manzilga kira oladi (masalan telefonidan); ular uchun bundan tashqari
 * admin panelidagi "Kassa" bo'limi ham bor.
 */
export function KassaMobileLayout() {
  const { user, logout } = useAuth()

  return (
    <div className="flex h-[100dvh] flex-col overflow-hidden bg-slate-50">
      {/* Yuqori panel — kim kirgani va chiqish */}
      <header className="flex shrink-0 items-center gap-3 border-b border-slate-200 bg-white px-4 py-3">
        <div className="flex h-9 w-9 shrink-0 items-center justify-center rounded-xl bg-brand-50 text-brand-600">
          <Wallet className="h-5 w-5" />
        </div>
        <div className="min-w-0 flex-1">
          <p className="truncate text-[13px] font-bold text-slate-800">{user?.fullName || 'Kassa'}</p>
          <p className="text-[11px] text-slate-400">Kassa</p>
        </div>
        <button
          type="button"
          onClick={logout}
          aria-label="Chiqish"
          className="flex h-9 w-9 items-center justify-center rounded-lg border border-slate-200 text-slate-400 transition-colors hover:bg-slate-50 hover:text-slate-700"
        >
          <LogOut className="h-4 w-4" />
        </button>
      </header>

      {/* Kontent — har bir ekran o'z sarlavha/paddingini beradi */}
      <main className="flex-1 overflow-y-auto">
        <div className="mx-auto w-full max-w-3xl px-3 py-3">
          {/* Suspense LAYOUT ICHIDA — sahifa chunk'i yuklanayotganda qobiq (header/tab) qoladi. */}
          <Suspense fallback={<Loader className="min-h-[240px]" />}>
            <Outlet />
          </Suspense>
        </div>
      </main>

      {/* Pastki navigatsiya — 2 tab (telefonda barmoq bilan qulay) */}
      <nav className="shrink-0 border-t border-slate-200 bg-white pb-[env(safe-area-inset-bottom)]">
        <div className="mx-auto flex h-[60px] max-w-3xl">
          {TABS.map((tab) => {
            const Icon = tab.icon
            return (
              <NavLink
                key={tab.to}
                to={tab.to}
                end={tab.end}
                className="flex flex-1 flex-col items-center justify-center gap-0.5"
              >
                {({ isActive }) => (
                  <>
                    <span
                      className={cn(
                        'flex h-7 w-16 items-center justify-center rounded-xl transition-colors',
                        isActive ? 'bg-brand-50' : 'bg-transparent',
                      )}
                    >
                      <Icon
                        className={cn('h-[22px] w-[22px]', isActive ? 'text-brand-600' : 'text-slate-400')}
                        strokeWidth={isActive ? 2.4 : 2}
                      />
                    </span>
                    <span
                      className={cn(
                        'text-[10.5px] tracking-tight',
                        isActive ? 'font-bold text-brand-600' : 'font-medium text-slate-400',
                      )}
                    >
                      {tab.label}
                    </span>
                  </>
                )}
              </NavLink>
            )
          })}
        </div>
      </nav>
    </div>
  )
}
