import { useEffect, useState } from 'react'
import { Users, Briefcase } from 'lucide-react'
import type { MessageClass } from '@/types'
import { getMessageClasses, getChat, sendChat } from '@/api/services/messages'
import { STAFF_CHANNEL, STAFF_CHANNEL_LABEL } from '@/config/constants'
import { useUnread } from '@/context/unread-context'
import { Card } from '@/components/ui/Card'
import { PageHeader } from '@/components/ui/PageHeader'
import { Loader } from '@/components/ui/Loader'
import { cn } from '@/lib/utils'
import { ChatPanel } from '@/components/chat/ChatPanel'

/**
 * Guruh chati — ilgari "Xabarlar" sahifasidagi 4-tab edi, endi "Chats" menyusidagi alohida
 * sahifa (`/admin/chats`). Kanallar ro'yxati (xodimlar kanali + guruhlar) + `ChatPanel`.
 *
 * Kanallar ro'yxatini SAHIFA O'ZI yuklaydi (`MessagesPage` ham o'zi yuklaydi) — ikkisi
 * bir-biriga bog'liq emas.
 */
export function GroupChatPage() {
  const { unreadChannels } = useUnread()
  const [classes, setClasses] = useState<MessageClass[]>([])
  const [loading, setLoading] = useState(true)
  const [selected, setSelected] = useState<string | null>(null)

  useEffect(() => {
    getMessageClasses()
      .then((cs) => {
        setClasses(cs)
        setSelected((prev) => prev ?? cs[0]?.name ?? STAFF_CHANNEL)
      })
      .finally(() => setLoading(false))
  }, [])

  return (
    <div>
      <PageHeader
        title="Guruh chati"
        sub="Xodimlar kanali va guruhlar bo'yicha jonli yozishmalar"
      />

      {loading ? (
        <Loader label="Yuklanmoqda..." />
      ) : (
        <div className="grid grid-cols-1 gap-4 lg:grid-cols-[280px_1fr]">
          <Card tight>
            <div className="max-h-[70vh] space-y-1 overflow-y-auto p-2">
              {/* Xodimlar kanali */}
              <button
                type="button"
                onClick={() => setSelected(STAFF_CHANNEL)}
                className={cn(
                  'flex w-full items-center gap-2 rounded-lg px-3 py-2 text-left text-sm transition-colors',
                  selected === STAFF_CHANNEL
                    ? 'bg-brand-50 font-semibold text-brand-700'
                    : 'text-slate-600 hover:bg-slate-50',
                )}
              >
                <Briefcase className="h-4 w-4 shrink-0 text-slate-400" />
                <span className="font-medium">{STAFF_CHANNEL_LABEL}</span>
                <span className="ml-auto flex items-center gap-1.5">
                  <span className="text-xs text-slate-400">barcha</span>
                  {unreadChannels.has(STAFF_CHANNEL) && (
                    <span className="h-2 w-2 shrink-0 rounded-full bg-red-500" />
                  )}
                </span>
              </button>
              <div className="my-1 border-t border-slate-100" />

              {classes.map((c) => (
                <button
                  key={c.name}
                  type="button"
                  onClick={() => setSelected(c.name)}
                  className={cn(
                    'flex w-full items-center justify-between gap-2 rounded-lg px-3 py-2 text-left text-sm transition-colors',
                    selected === c.name
                      ? 'bg-brand-50 font-semibold text-brand-700'
                      : 'text-slate-600 hover:bg-slate-50',
                  )}
                >
                  <span className="truncate font-medium">{c.name}</span>
                  <span className="flex items-center gap-2 text-xs text-slate-400">
                    <span className="inline-flex items-center gap-0.5" title="O'quvchilar">
                      <Users className="h-3 w-3" />
                      <span className="font-mono">{c.studentCount}</span>
                    </span>
                    {unreadChannels.has(c.name) && (
                      <span className="h-2 w-2 shrink-0 rounded-full bg-red-500" />
                    )}
                  </span>
                </button>
              ))}

              {classes.length === 0 && (
                <p className="px-3 py-6 text-center text-sm text-slate-400">
                  Guruhlar yo'q. Avval guruh qo'shing.
                </p>
              )}
            </div>
          </Card>

          <div>
            {selected === STAFF_CHANNEL ? (
              <ChatPanel
                key="staff"
                className={STAFF_CHANNEL}
                fetchMessages={getChat}
                sendMessage={sendChat}
                title={`${STAFF_CHANNEL_LABEL} — guruh chati`}
                subtitle="Barcha o'qituvchilar va adminlar"
              />
            ) : selected ? (
              <ChatPanel
                key={selected}
                className={selected}
                fetchMessages={getChat}
                sendMessage={sendChat}
              />
            ) : (
              <Card>
                <p className="py-10 text-center text-slate-400">Kanal tanlang</p>
              </Card>
            )}
          </div>
        </div>
      )}
    </div>
  )
}
