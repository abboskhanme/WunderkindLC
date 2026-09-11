import { useCallback, useEffect, useState } from 'react'
import type { LucideIcon } from 'lucide-react'
import { ShoppingCart, Package, BarChart3, CreditCard, Wallet, HandCoins } from 'lucide-react'
import type { BookBadges } from '@/api/services/books'
import { getBookBadges } from '@/api/services/books'
import { PageHeader } from '@/components/ui/PageHeader'
import { cn } from '@/lib/utils'
import { usePerm } from '@/lib/permissions'
import { BookOrdersTab } from './BookOrdersTab'
import { BookCardPaymentsTab } from './BookCardPaymentsTab'
import { BookInventoryTab } from './BookInventoryTab'
import { BookCreditsTab } from './BookCreditsTab'
import { BookAnalyticsTab } from './BookAnalyticsTab'
import { BookSettingsTab } from './BookSettingsTab'
import { tabFromUrl } from '@/lib/tabParam'

type Tab = 'orders' | 'card' | 'credits' | 'inventory' | 'analytics' | 'settings'
const BOOK_TABS = ['orders', 'card', 'credits', 'inventory', 'analytics', 'settings'] as const

const NO_BADGES: BookBadges = { count: 0, credits: 0, overdue: 0 }

/**
 * KITOBLAR SOTUVI — bitta sahifa, 6 tab:
 *  • Buyurtmalar — botdan tushgan so'rovlarni tasdiqlash/rad etish (chek rasmi bilan);
 *  • Karta to'lovlari — kartaga o'tkazma qilganlar: chek rasmi ro'yxatda ko'rinadi,
 *    tepada shu kartaga hisoblangan jami summa;
 *  • Nasiya — kitob berilgan, pul hali olinmagan sotuvlar: qarzdorlar va "To'landi" tasdiqlash;
 *  • Ombor — kitob yaratish/tahrirlash, narx, qoldiq kirim + kirim tarixi;
 *  • Analitika — sotuv (naqd/karta/nasiya), kunlik grafik, har kuni sotilgan kitoblar, qoldiq;
 *  • Sozlamalar — botda ko'rinadigan karta rekvizitlari.
 */
export function BookSalesPage() {
  const { can } = usePerm()
  // `?tab=analytics` — "Hisobotlar" bo'limidan to'g'ridan-to'g'ri sotuv analitikasiga.
  const [tab, setTab] = useState<Tab>(() => tabFromUrl(BOOK_TABS, 'orders'))
  const [badges, setBadges] = useState<BookBadges>(NO_BADGES)

  const refreshPending = useCallback(() => {
    getBookBadges()
      .then(setBadges)
      .catch(() => setBadges(NO_BADGES))
  }, [])

  useEffect(refreshPending, [refreshPending])

  return (
    <div>
      <PageHeader
        title="Kitoblar sotuvi"
        sub="Botdan tushgan buyurtmalar, ombor qoldig'i va sotuv hisobotlari"
        actions={
          <div className="tabs">
            <TabButton active={tab === 'orders'} onClick={() => setTab('orders')} icon={ShoppingCart}>
              <span className="inline-flex items-center gap-1.5">
                Buyurtmalar
                {badges.count > 0 && (
                  <span className="rounded-full bg-red-500 px-1.5 py-px text-[11px] font-bold text-white">
                    {badges.count}
                  </span>
                )}
              </span>
            </TabButton>
            <TabButton active={tab === 'card'} onClick={() => setTab('card')} icon={Wallet}>
              Karta to'lovlari
            </TabButton>
            <TabButton active={tab === 'credits'} onClick={() => setTab('credits')} icon={HandCoins}>
              <span className="inline-flex items-center gap-1.5">
                Nasiya
                {badges.credits > 0 && (
                  <span
                    className={cn(
                      'rounded-full px-1.5 py-px text-[11px] font-bold text-white',
                      // Muddati o'tgan qarz bo'lsa — qizil (darhol ko'zga tashlansin).
                      badges.overdue > 0 ? 'bg-red-500' : 'bg-orange-500',
                    )}
                  >
                    {badges.credits}
                  </span>
                )}
              </span>
            </TabButton>
            <TabButton active={tab === 'inventory'} onClick={() => setTab('inventory')} icon={Package}>
              Ombor
            </TabButton>
            <TabButton active={tab === 'analytics'} onClick={() => setTab('analytics')} icon={BarChart3}>
              Analitika
            </TabButton>
            <TabButton active={tab === 'settings'} onClick={() => setTab('settings')} icon={CreditCard}>
              Sozlamalar
            </TabButton>
          </div>
        }
      />

      {tab === 'orders' ? (
        <BookOrdersTab
          canDecide={can('books', 'edit')}
          canSell={can('books', 'create')}
          onDecided={refreshPending}
        />
      ) : tab === 'card' ? (
        <BookCardPaymentsTab canDecide={can('books', 'edit')} onDecided={refreshPending} />
      ) : tab === 'credits' ? (
        <BookCreditsTab canDecide={can('books', 'edit')} onPaid={refreshPending} />
      ) : tab === 'inventory' ? (
        <BookInventoryTab
          canCreate={can('books', 'create')}
          canEdit={can('books', 'edit')}
          canDelete={can('books', 'delete')}
        />
      ) : tab === 'analytics' ? (
        <BookAnalyticsTab canReturn={can('books', 'edit')} onReturned={refreshPending} />
      ) : (
        <BookSettingsTab canEdit={can('books', 'edit')} />
      )}
    </div>
  )
}

interface TabButtonProps {
  active: boolean
  onClick: () => void
  icon: LucideIcon
  children: React.ReactNode
}

function TabButton({ active, onClick, icon: Icon, children }: TabButtonProps) {
  return (
    <button
      type="button"
      onClick={onClick}
      className={cn('tab inline-flex items-center gap-1.5', active && 'active')}
    >
      <Icon className="h-4 w-4" />
      {children}
    </button>
  )
}
