import { Fragment, useEffect, useRef, useState } from 'react'
import { Send, ArrowLeft, AlertCircle } from 'lucide-react'
import type { ChatMessage } from '@/types'
import { useAuth } from '@/context/auth-context'
import { useUnread } from '@/context/unread-context'
import { Card } from '@/components/ui/Card'
import { Loader } from '@/components/ui/Loader'
import { roleLabels } from '@/config/navigation'
import { cn, formatTime, apiErrorMessage } from '@/lib/utils'

/** O'zbekcha oy nomlari (loyihadagi boshqa ekranlar bilan bir xil ro'yxat). */
const UZ_MONTHS = [
  'Yanvar', 'Fevral', 'Mart', 'Aprel', 'May', 'Iyun',
  'Iyul', 'Avgust', 'Sentabr', 'Oktabr', 'Noyabr', 'Dekabr',
]

/** `Date` → mahalliy "yyyy-MM-dd" kaliti (UTC emas — `toISOString()` kunni siljitib yuboradi). */
function dayKeyOf(d: Date): string {
  const mm = String(d.getMonth() + 1).padStart(2, '0')
  const dd = String(d.getDate()).padStart(2, '0')
  return `${d.getFullYear()}-${mm}-${dd}`
}

/**
 * Xabar vaqtidan KUN kalitini ("yyyy-MM-dd") oladi — ajratgich shu kalit o'zgarganda chiqadi.
 * Nega ikki xil yo'l:
 *  - `createdAt`da vaqt mintaqasi bor bo'lsa ("...Z" yoki "...+05:00") — `new Date()` bilan
 *    MAHALLIY vaqtga o'giramiz va mahalliy getter'lardan kalit yasaymiz. Aks holda UTC sanasi
 *    ishlatilib, kechqurun (yoki erta tongda) yozilgan xabarlar qo'shni kunga tushib qolardi;
 *    kun boshi esa foydalanuvchining mahalliy yarim tuni bo'lishi kerak.
 *  - Mintaqa ko'rsatilmagan bo'lsa ("yyyy-MM-ddTHH:mm:ss" — server allaqachon Toshkent vaqtini
 *    yuboradi) satrning sana qismini TO'G'RIDAN-TO'G'RI o'qiymiz: shunda `formatTime` ko'rsatgan
 *    soat bilan bir xil kunga tegishli bo'ladi (brauzer TZ'si sanani siljitmaydi).
 */
function messageDayKey(iso: string): string {
  const zoned = /(?:Z|[+-]\d{2}:?\d{2})$/.test(iso)
  if (!zoned) {
    const m = /^(\d{4}-\d{2}-\d{2})/.exec(iso)
    if (m) return m[1]
  }
  const d = new Date(iso)
  if (Number.isNaN(d.getTime())) return iso // noma'lum format — kalit sifatida satrning o'zi
  return dayKeyOf(d)
}

/** Kun kaliti → ajratgich matni: «Bugun» / «Kecha» / «12 Iyul» / «12 Iyul, 2025». */
function dayDividerLabel(dayKey: string): string {
  const now = new Date()
  if (dayKey === dayKeyOf(now)) return 'Bugun'
  // "Kecha" ni ham mahalliy kalendar bo'yicha hisoblaymiz (setDate oy/yil chegarasini o'zi hal qiladi).
  const yesterday = new Date(now)
  yesterday.setDate(yesterday.getDate() - 1)
  if (dayKey === dayKeyOf(yesterday)) return 'Kecha'

  const m = /^(\d{4})-(\d{2})-(\d{2})$/.exec(dayKey)
  if (!m) return dayKey // format tanilmadi — xom qiymatni ko'rsatamiz
  const label = `${Number(m[3])} ${UZ_MONTHS[Number(m[2]) - 1]}`
  // Shu yil ichidagi sanada yil takrorlanmaydi, boshqa yil bo'lsa — qo'shiladi.
  return Number(m[1]) === now.getFullYear() ? label : `${label}, ${m[1]}`
}

interface Props {
  className: string
  /** Xabarlarni olish (admin yoki o'qituvchi servisidan) */
  fetchMessages: (className: string, since?: string) => Promise<ChatMessage[]>
  /** Xabar yuborish */
  sendMessage: (className: string, text: string) => Promise<ChatMessage>
  /** Panel sarlavhasi (berilmasa — "{className} — guruh chati") */
  title?: string
  /** Sarlavha ostidagi izoh (a'zolar haqida) */
  subtitle?: string
  /** To'liq ekran rejimi (mobil): kartasiz, h-full — butun maydonni egallaydi. */
  fullHeight?: boolean
  /** Berilsa — sarlavhada orqaga tugma ko'rsatiladi (mobil suhbatdan ro'yxatga qaytish). */
  onBack?: () => void
}

/**
 * Bitta guruhning chati: real-time (SignalR) xabarlar + yozish maydoni.
 * SignalR ulanishi UnreadProvider orqali global tarzda boshqariladi — alohida ulanish ochilmaydi.
 */
export function ChatPanel({ className, fetchMessages, sendMessage, title, subtitle, fullHeight, onBack }: Props) {
  const { user } = useAuth()
  const { markRead, subscribe, onReconnect } = useUnread()
  const [messages, setMessages] = useState<ChatMessage[]>([])
  const [text, setText] = useState('')
  const [loading, setLoading] = useState(true)
  const [sending, setSending] = useState(false)
  const [reconnecting, setReconnecting] = useState(false)
  const [connectionError, setConnectionError] = useState<string | null>(null)
  const bottomRef = useRef<HTMLDivElement>(null)
  const classRef = useRef(className)
  const reconnectTimeoutRef = useRef<ReturnType<typeof setTimeout> | undefined>(undefined)

  useEffect(() => {
    classRef.current = className
  }, [className])

  // Guruh o'zgarsa — xabarlarni qayta yuklaymiz.
  useEffect(() => {
    // eslint-disable-next-line react-hooks/set-state-in-effect -- guruh almashganda chatni qayta yuklash (maqsadli)
    setLoading(true)
    fetchMessages(className)
      .then(setMessages)
      .finally(() => setLoading(false))
    // eslint-disable-next-line react-hooks/exhaustive-deps -- fetchMessages barqaror deb hisoblanadi
  }, [className])

  // Kanalga obuna bo'lamiz + o'qilgan deb belgilaymiz (badgeni o'chiradi).
  useEffect(() => {
    markRead(className)
    return subscribe(className, (m) => {
      setMessages((prev) => (prev.some((x) => x.id === m.id) ? prev : [...prev, m]))
    })
  }, [className, markRead, subscribe])

  // SignalR qayta ulanganda xabarlarni re-fetch qilamiz + notification ko'rsatamiz.
  useEffect(() => {
    const handleReconnect = (status: 'reconnecting' | 'reconnected') => {
      if (status === 'reconnecting') {
        setReconnecting(true)
        setConnectionError(null)
        // 3 sekunddan ko'p bo'lsa ogohlantirish (qo'ng'iroq ko'rinadi)
        reconnectTimeoutRef.current = setTimeout(() => {
          setConnectionError('Ulanmoqda...')
        }, 3000)
      } else if (status === 'reconnected') {
        setReconnecting(false)
        setConnectionError(null)
        if (reconnectTimeoutRef.current) {
          clearTimeout(reconnectTimeoutRef.current)
        }
        // Xabarlarni qayta yuklaymiz
        fetchMessages(classRef.current)
          .then(setMessages)
          .catch((err) => {
            setConnectionError(apiErrorMessage(err, 'Xabarlar yuklab boʻlmadi (error)'))
          })
      }
    }

    return onReconnect(handleReconnect)
    // eslint-disable-next-line react-hooks/exhaustive-deps -- fetchMessages barqaror deb hisoblanadi
  }, [onReconnect])

  useEffect(() => {
    bottomRef.current?.scrollIntoView({ behavior: 'smooth' })
  }, [messages])

  // Timeout cleanup
  useEffect(() => {
    return () => {
      if (reconnectTimeoutRef.current) {
        clearTimeout(reconnectTimeoutRef.current)
      }
    }
  }, [])

  const handleSend = async (e: React.FormEvent) => {
    e.preventDefault()
    const t = text.trim()
    if (!t || sending) return
    setSending(true)
    try {
      const m = await sendMessage(className, t)
      setText('')
      setMessages((prev) => (prev.some((x) => x.id === m.id) ? prev : [...prev, m]))
    } finally {
      setSending(false)
    }
  }

  const content = (
    <>
      <div className="flex items-center gap-2.5 border-b border-slate-100 px-4 py-3">
        {onBack && (
          <button
            type="button"
            onClick={onBack}
            className="tap-scale -ml-1 flex h-9 w-9 shrink-0 items-center justify-center rounded-xl text-slate-500 hover:bg-slate-100"
            title="Orqaga"
          >
            <ArrowLeft className="h-5 w-5" />
          </button>
        )}
        <div className="min-w-0">
          <p className="truncate font-semibold text-slate-800">{title ?? `${className} — guruh chati`}</p>
          <p className="truncate text-xs text-slate-400">
            {subtitle ?? "O'quvchilar, dars beruvchi o'qituvchilar va admin"}
          </p>
        </div>
      </div>

      {/* SignalR ulanish holati — notification */}
      {(reconnecting || connectionError) && (
        <div
          className={cn(
            'flex items-center gap-2 px-4 py-2 text-sm',
            connectionError
              ? 'bg-red-50 text-red-700'
              : 'bg-amber-50 text-amber-700'
          )}
        >
          {connectionError ? (
            <>
              <AlertCircle className="h-4 w-4 flex-shrink-0" />
              <span>{connectionError}</span>
            </>
          ) : (
            <>
              <div className="h-2 w-2 animate-pulse rounded-full bg-amber-600" />
              <span>Ulanmoqda...</span>
            </>
          )}
        </div>
      )}

      <div className="flex-1 space-y-3 overflow-y-auto px-4 py-4">
        {loading ? (
          <Loader label="Yuklanmoqda..." />
        ) : messages.length === 0 ? (
          <p className="py-12 text-center text-sm text-slate-400">
            Hozircha xabar yo'q. Birinchi bo'lib yozing.
          </p>
        ) : (
          messages.map((m, i) => {
            const mine = m.senderUserId === user?.id
            // Kun ajratgichi: birinchi xabarda yoki kun avvalgi xabar kunidan farq qilganda.
            const dayKey = messageDayKey(m.createdAt)
            const showDay = i === 0 || dayKey !== messageDayKey(messages[i - 1].createdAt)
            return (
              <Fragment key={m.id}>
                {showDay && (
                  <div className="flex items-center gap-3">
                    <span className="flex-1 border-t border-line" />
                    <span className="rounded-lg bg-panel2 px-2.5 py-1 text-[11px] font-semibold text-mute">
                      {dayDividerLabel(dayKey)}
                    </span>
                    <span className="flex-1 border-t border-line" />
                  </div>
                )}
                <div className={cn('flex', mine ? 'justify-end' : 'justify-start')}>
                  <div
                    className={cn(
                      'max-w-[75%] rounded-2xl px-3 py-2 text-sm',
                      mine ? 'bg-brand-600 text-white' : 'bg-slate-100 text-slate-800',
                    )}
                  >
                    {!mine && (
                      <div className="mb-0.5 flex items-center gap-1.5">
                        <span className="text-xs font-semibold text-slate-700">{m.senderName}</span>
                        <span className="rounded bg-slate-200 px-1 text-[10px] text-slate-500">
                          {roleLabels[m.senderRole]}
                        </span>
                      </div>
                    )}
                    <p className="whitespace-pre-wrap break-words">{m.text}</p>
                    <div
                      className={cn(
                        'mt-0.5 text-right text-[10px]',
                        mine ? 'text-brand-100' : 'text-slate-400',
                      )}
                    >
                      {formatTime(m.createdAt)}
                    </div>
                  </div>
                </div>
              </Fragment>
            )
          })
        )}
        <div ref={bottomRef} />
      </div>

      <form onSubmit={handleSend} className="flex items-center gap-2 border-t border-slate-100 p-3">
        <input
          className="flex-1 rounded-lg border border-slate-200 px-3 py-2 text-sm outline-none focus:border-brand-400 focus:ring-2 focus:ring-brand-100"
          placeholder="Xabar yozing..."
          value={text}
          onChange={(e) => setText(e.target.value)}
        />
        <button
          type="submit"
          disabled={!text.trim() || sending}
          className="inline-flex h-10 w-10 items-center justify-center rounded-lg bg-brand-600 text-white transition-colors hover:bg-brand-700 disabled:opacity-50"
          title="Yuborish"
        >
          <Send className="h-4 w-4" />
        </button>
      </form>
    </>
  )

  // To'liq ekran (mobil): kartasiz, butun maydonni egallaydi — composer pastda pinlanadi.
  if (fullHeight) {
    return <div className="flex h-full flex-col bg-white">{content}</div>
  }
  return <Card className="flex h-[70vh] flex-col p-0">{content}</Card>
}
