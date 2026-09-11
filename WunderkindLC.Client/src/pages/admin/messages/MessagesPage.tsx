import { useEffect, useState } from 'react'
import { MessageSquare, Send, Zap, History } from 'lucide-react'
import type { MessageClass } from '@/types'
import { getMessageClasses } from '@/api/services/messages'
import { PageHeader } from '@/components/ui/PageHeader'
import { Loader } from '@/components/ui/Loader'
import { cn } from '@/lib/utils'
import { UnifiedComposer } from './UnifiedComposer'
import { AutoMessagesTab } from './AutoMessagesTab'
import { HistoryTab } from './HistoryTab'
import { usePerm } from '@/lib/permissions'
type Tab = 'send' | 'auto' | 'history'

export function MessagesPage() {
  const { can } = usePerm()
  const [classes, setClasses] = useState<MessageClass[]>([])
  const [loading, setLoading] = useState(true)
  const [tab, setTab] = useState<Tab>('send')
  const [highlightRule, setHighlightRule] = useState<string | null>(null)

  useEffect(() => {
    getMessageClasses()
      .then(setClasses)
      .finally(() => setLoading(false))
  }, [])

  return (
    <div>
      <PageHeader
        title="Xabarlar"
        sub="Xabar yuborish, avto xabarlar va tarix (guruh chati — «Chats» bo'limida)"
        actions={
          <div className="tabs">
            <TabButton active={tab === 'send'} onClick={() => setTab('send')} icon={Send}>
              Xabar yuborish
            </TabButton>
            <TabButton active={tab === 'auto'} onClick={() => setTab('auto')} icon={Zap}>
              Xabar yaratish
            </TabButton>
            <TabButton active={tab === 'history'} onClick={() => setTab('history')} icon={History}>
              Tarix
            </TabButton>
          </div>
        }
      />

      {loading ? (
        <Loader label="Yuklanmoqda..." />
      ) : tab === 'send' ? (
        <UnifiedComposer
          classes={classes}
          canSend={can('messages.broadcast', 'create')}
          onConfigureAuto={(id: string) => {
            setHighlightRule(id)
            setTab('auto')
          }}
        />
      ) : tab === 'auto' ? (
        <AutoMessagesTab
          highlightRuleId={highlightRule}
          canCreate={can('messages.broadcast', 'create')}
          canEdit={can('messages.broadcast', 'edit')}
          canDelete={can('messages.broadcast', 'delete')}
        />
      ) : (
        <HistoryTab />
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
