import { useEffect, useState } from 'react'
import { Briefcase, MessageSquare, ChevronRight } from 'lucide-react'
import { getTeacherChatClasses, getTeacherChat, sendTeacherChat } from '@/api/services/teacher'
import { STAFF_CHANNEL, STAFF_CHANNEL_LABEL } from '@/config/constants'
import { useUnread } from '@/context/unread-context'
import { Loader } from '@/components/ui/Loader'
import { cn } from '@/lib/utils'
import { ChatPanel } from '@/components/chat/ChatPanel'

/**
 * O'qituvchi xabarlari — MOBIL oqim: kanal ro'yxati → bosilsa to'liq-ekran suhbat
 * (orqaga tugma bilan). Admin 2-ustun grid'i mobilda yaroqsiz edi.
 */
export function TeacherMessagesPage() {
  const { unreadChannels } = useUnread()
  const [classes, setClasses] = useState<string[]>([])
  const [loading, setLoading] = useState(true)
  // null = ro'yxat ko'rinishi; aks holda — tanlangan kanal suhbati
  const [selected, setSelected] = useState<string | null>(null)

  useEffect(() => {
    getTeacherChatClasses()
      .then((cs) => setClasses(cs.filter((c) => c !== STAFF_CHANNEL)))
      .catch(() => {})
      .finally(() => setLoading(false))
  }, [])

  // ---- Suhbat ko'rinishi (TO'LIQ EKRAN — marginlarsiz) ----
  if (selected) {
    const isStaff = selected === STAFF_CHANNEL
    return (
      <div className="h-full">
        <ChatPanel
          key={selected}
          className={selected}
          fetchMessages={getTeacherChat}
          sendMessage={sendTeacherChat}
          title={isStaff ? STAFF_CHANNEL_LABEL : `${selected} — guruh chati`}
          subtitle={isStaff ? "Barcha o'qituvchilar va adminlar" : 'Guruh chati'}
          fullHeight
          onBack={() => setSelected(null)}
        />
      </div>
    )
  }

  // ---- Kanallar ro'yxati ----
  return (
    <div className="min-h-full bg-paper px-4 pb-6 pt-3">
      {/* Ekran sarlavhasi (shell global headeri olib tashlangan) */}
      <div className="mb-4">
        <p className="text-[22px] font-extrabold tracking-tight text-ink">Xabarlar</p>
        <p className="text-[12px] text-mute">Guruh va xodimlar chati</p>
      </div>

      {loading ? (
        <Loader label="Yuklanmoqda..." />
      ) : (
        <div className="space-y-2">
          {/* Xodimlar kanali — har bir o'qituvchiga ochiq */}
          <ChannelRow
            icon={Briefcase}
            name={STAFF_CHANNEL_LABEL}
            hint="Barcha o'qituvchilar va adminlar"
            unread={unreadChannels.has(STAFF_CHANNEL)}
            staff
            onClick={() => setSelected(STAFF_CHANNEL)}
          />

          {classes.map((c) => (
            <ChannelRow
              key={c}
              icon={MessageSquare}
              name={c}
              hint="Guruh chati"
              unread={unreadChannels.has(c)}
              onClick={() => setSelected(c)}
            />
          ))}

          {classes.length === 0 && (
            <p className="rounded-[18px] border border-dashed border-line px-4 py-8 text-center text-[13px] text-faint">
              Sizga biriktirilgan guruh yo'q.
            </p>
          )}
        </div>
      )}
    </div>
  )
}

function ChannelRow({
  icon: Icon,
  name,
  hint,
  unread,
  staff,
  onClick,
}: {
  icon: typeof MessageSquare
  name: string
  hint: string
  unread: boolean
  staff?: boolean
  onClick: () => void
}) {
  return (
    <button
      type="button"
      onClick={onClick}
      className="tap-scale flex w-full items-center gap-3 rounded-[18px] border border-line bg-white p-3.5 text-left shadow-[var(--shadow-card)]"
    >
      <div
        className={cn(
          'flex h-12 w-12 shrink-0 items-center justify-center rounded-2xl text-white',
          staff
            ? 'bg-gradient-to-br from-violet-500 to-fuchsia-600'
            : 'bg-gradient-to-br from-teal-500 to-teal-700',
        )}
      >
        <Icon className="h-5 w-5" />
      </div>
      <div className="min-w-0 flex-1">
        <p className="truncate font-bold tracking-tight text-ink">{name}</p>
        <p className="truncate text-[12px] text-faint">{hint}</p>
      </div>
      {unread && <span className="h-2.5 w-2.5 shrink-0 rounded-full bg-red-500" />}
      <ChevronRight className="h-5 w-5 shrink-0 text-faint" />
    </button>
  )
}
