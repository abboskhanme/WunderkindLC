import { useState } from 'react'
import { FileText, History, MessageSquare, Send, Wallet, Zap } from 'lucide-react'
import { PageHeader } from '@/components/ui/PageHeader'
import { cn } from '@/lib/utils'
import { tabFromUrl } from '@/lib/tabParam'
import { usePerm } from '@/lib/permissions'
import { TelegramComposer } from './TelegramComposer'
import { DebtorsTab } from './DebtorsTab'
import { AutoMessagesTab } from './AutoMessagesTab'
import { TemplatesTab } from './TemplatesTab'
import { HistoryTab } from './HistoryTab'

const TABS = ['yuborish', 'qarzdorlar', 'avto', 'shablonlar', 'tarix'] as const
type Tab = (typeof TABS)[number]

/**
 * «Telegram xabarlar» (CRM upgrade P2.8) — tablar `?tab=` bilan (`lib/tabParam`):
 * «Xabar yuborish» (default) · «Qarzdorlar» · «Avto xabarlar» · «Shablonlar» · «Tarix».
 * Hammasi Telegram OUTBOX orqali (sanoq, oluvchi bo'yicha hisobot, «Qabul qildim»).
 */
export function MessagesPage() {
  const { can } = usePerm()
  const [tab, setTab] = useState<Tab>(() => tabFromUrl(TABS, 'yuborish'))
  // `?campaign=` yoki hozirgina yuborilgan kampaniya — «Tarix»da hisoboti ochiq turadi.
  const [campaignId, setCampaignId] = useState<string | null>(() =>
    typeof window === 'undefined' ? null : new URLSearchParams(window.location.search).get('campaign'),
  )
  const canSend = can('messages.broadcast', 'create')

  const openReport = (id: string) => {
    setCampaignId(id)
    setTab('tarix')
  }

  return (
    <div>
      <PageHeader
        title="Telegram xabarlar"
        sub="Ota-onalar va o'quvchilarga Telegram bot orqali — sanoq, hisobot va «Qabul qildim»"
        actions={
          <div className="tabs flex-wrap">
            <TabButton active={tab === 'yuborish'} onClick={() => setTab('yuborish')} icon={Send}>
              Xabar yuborish
            </TabButton>
            <TabButton active={tab === 'qarzdorlar'} onClick={() => setTab('qarzdorlar')} icon={Wallet}>
              Qarzdorlar
            </TabButton>
            <TabButton active={tab === 'avto'} onClick={() => setTab('avto')} icon={Zap}>
              Avto xabarlar
            </TabButton>
            <TabButton active={tab === 'shablonlar'} onClick={() => setTab('shablonlar')} icon={FileText}>
              Shablonlar
            </TabButton>
            <TabButton active={tab === 'tarix'} onClick={() => setTab('tarix')} icon={History}>
              Tarix
            </TabButton>
          </div>
        }
      />

      {tab === 'yuborish' ? (
        <TelegramComposer canSend={canSend} onSent={openReport} />
      ) : tab === 'qarzdorlar' ? (
        <DebtorsTab canSend={canSend} onSent={openReport} />
      ) : tab === 'avto' ? (
        <AutoMessagesTab
          canCreate={can('messages.broadcast', 'create')}
          canEdit={can('messages.broadcast', 'edit')}
          canDelete={can('messages.broadcast', 'delete')}
        />
      ) : tab === 'shablonlar' ? (
        <TemplatesTab
          canCreate={can('messages.broadcast', 'create')}
          canEdit={can('messages.broadcast', 'edit')}
          canDelete={can('messages.broadcast', 'delete')}
        />
      ) : (
        <HistoryTab
          key={campaignId ?? 'none'}
          openCampaignId={campaignId}
          onCampaignClosed={() => setCampaignId(null)}
          canCancel={canSend}
        />
      )}
    </div>
  )
}

interface TabButtonProps {
  active: boolean
  onClick: () => void
  icon: typeof MessageSquare
  children: React.ReactNode
}

function TabButton({ active, onClick, icon: Icon, children }: TabButtonProps) {
  return (
    <button type="button" onClick={onClick} className={cn('tab inline-flex items-center gap-1.5', active && 'active')}>
      <Icon className="h-4 w-4" />
      {children}
    </button>
  )
}
