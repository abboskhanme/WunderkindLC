import { useEffect, useRef, useState } from 'react'
import { Send } from 'lucide-react'
import { getBotThreads, getBotMessages, replyBotThread } from '@/api/services/botSupport'
import type { BotThread, BotSupportMsg } from '@/api/services/botSupport'
import { Card } from '@/components/ui/Card'
import { Loader } from '@/components/ui/Loader'
import { cn, formatTime } from '@/lib/utils'

export function SupportPanel() {
  const [threads, setThreads] = useState<BotThread[]>([])
  const [loadingThreads, setLoadingThreads] = useState(true)
  const [search, setSearch] = useState('')
  const [selected, setSelected] = useState<BotThread | null>(null)

  const [msgs, setMsgs] = useState<BotSupportMsg[]>([])
  const [loadingMsgs, setLoadingMsgs] = useState(false)
  const [text, setText] = useState('')
  const [sending, setSending] = useState(false)

  const bottomRef = useRef<HTMLDivElement>(null)
  const containerRef = useRef<HTMLDivElement>(null)
  const selectedRef = useRef<BotThread | null>(null)
  selectedRef.current = selected
  /** Foydalanuvchi pastki qismga yaqinmi (4s polling YANGI xabar bo'lmasa ham qayta chizadi —
   *  shuning uchun faqat shu holatda avto-scroll qilinadi, aks holda tepaga o'qish uchun chiqilgan
   *  bo'lsa har 4s'da pastga tashlab yuboradi). */
  const nearBottomRef = useRef(true)
  const handleScroll = () => {
    const el = containerRef.current
    if (!el) return
    nearBottomRef.current = el.scrollHeight - el.scrollTop - el.clientHeight < 80
  }

  // Dastlabki thread yuklash
  useEffect(() => {
    getBotThreads()
      .then(setThreads)
      .finally(() => setLoadingThreads(false))
  }, [])

  // Thread ro'yxatini har 5s yangilash
  useEffect(() => {
    const id = setInterval(() => {
      getBotThreads().then(setThreads).catch(() => {})
    }, 5000)
    return () => clearInterval(id)
  }, [])

  // Tanlangan thread xabarlarini yuklash (tanlanganda va har 4s)
  useEffect(() => {
    if (!selected) return
    // Thread yangi tanlanganda har doim pastga (suhbat oxiriga) tushiladi.
    nearBottomRef.current = true
    setLoadingMsgs(true)
    getBotMessages(selected.chatId)
      .then(setMsgs)
      .finally(() => setLoadingMsgs(false))

    const id = setInterval(() => {
      if (!selectedRef.current) return
      getBotMessages(selectedRef.current.chatId)
        .then(setMsgs)
        .catch(() => {})
    }, 4000)
    return () => clearInterval(id)
  }, [selected?.chatId])

  // Yangi xabar kelganda pastga aylantirish — FAQAT foydalanuvchi allaqachon pastda bo'lsa (yoki
  // thread endi tanlangan bo'lsa). 4s polling har safar yangi msgs massivi qaytaradi (yangi xabar
  // bo'lmasa ham) — shu tekshiruv bo'lmasa tepaga o'qish uchun chiqilganda doim pastga tashlab yuborardi.
  useEffect(() => {
    if (nearBottomRef.current) bottomRef.current?.scrollIntoView({ behavior: 'smooth' })
  }, [msgs])

  const handleSend = async (e: React.FormEvent) => {
    e.preventDefault()
    if (!selected) return
    const t = text.trim()
    if (!t || sending) return
    setSending(true)
    try {
      await replyBotThread(selected.chatId, t)
      setText('')
      // O'zi yozgan javobini har doim ko'rsin — pastga tushiriladi.
      nearBottomRef.current = true
      const fresh = await getBotMessages(selected.chatId)
      setMsgs(fresh)
    } finally {
      setSending(false)
    }
  }

  const filtered = threads.filter((th) => {
    const q = search.toLowerCase()
    return (
      th.name.toLowerCase().includes(q) ||
      th.phone.toLowerCase().includes(q) ||
      th.linked.toLowerCase().includes(q)
    )
  })

  return (
    <div className="grid grid-cols-1 gap-4 lg:grid-cols-[300px_1fr]">
      {/* Chap: thread ro'yxati */}
      <Card tight>
        <div className="border-b border-slate-100 p-3">
          <input
            type="text"
            value={search}
            onChange={(e) => setSearch(e.target.value)}
            placeholder="Qidirish (ism, telefon)..."
            className="w-full rounded-lg border border-slate-200 px-3 py-2 text-sm outline-none focus:border-brand-400 focus:ring-2 focus:ring-brand-100"
          />
        </div>
        <div className="max-h-[70vh] overflow-y-auto">
          {loadingThreads ? (
            <Loader label="Yuklanmoqda..." />
          ) : filtered.length === 0 ? (
            <p className="px-4 py-8 text-center text-sm text-slate-400">
              {threads.length === 0
                ? "Hali hech kim botdan murojaat qilmagan"
                : "Hech narsa topilmadi"}
            </p>
          ) : (
            <div className="space-y-0.5 p-2">
              {filtered.map((th) => (
                <button
                  key={th.chatId}
                  type="button"
                  onClick={() => setSelected(th)}
                  className={cn(
                    'flex w-full items-start gap-2 rounded-lg px-3 py-2.5 text-left text-sm transition-colors',
                    selected?.chatId === th.chatId
                      ? 'bg-brand-50 text-brand-700'
                      : 'text-slate-600 hover:bg-slate-50',
                  )}
                >
                  <div className="min-w-0 flex-1">
                    <div className="flex items-center gap-1.5">
                      <span className="truncate font-semibold">
                        {th.name || 'Telegram user'}
                      </span>
                      {th.unread > 0 && (
                        <span className="ml-auto shrink-0 rounded-full bg-red-500 px-1.5 py-0.5 text-[10px] font-bold text-white">
                          {th.unread}
                        </span>
                      )}
                    </div>
                    <div className="truncate text-xs text-slate-400">
                      {th.phone || th.linked || th.username || ''}
                    </div>
                    {th.lastText && (
                      <div className="mt-0.5 truncate text-xs text-slate-400">
                        {th.lastText}
                      </div>
                    )}
                  </div>
                </button>
              ))}
            </div>
          )}
        </div>
      </Card>

      {/* O'ng: yozishma */}
      {!selected ? (
        <Card>
          <p className="py-12 text-center text-sm text-slate-400">Yozishmani tanlang</p>
        </Card>
      ) : (
        <Card className="flex h-[70vh] flex-col p-0">
          {/* Sarlavha */}
          <div className="flex items-center gap-2.5 border-b border-slate-100 px-4 py-3">
            <div className="min-w-0">
              <p className="truncate font-semibold text-slate-800">
                {selected.name || 'Telegram user'}
              </p>
              <p className="truncate text-xs text-slate-400">
                {[selected.phone, selected.linked].filter(Boolean).join(' · ') || selected.username || `@${selected.chatId}`}
              </p>
            </div>
          </div>

          {/* Xabarlar */}
          <div ref={containerRef} onScroll={handleScroll} className="flex-1 space-y-3 overflow-y-auto px-4 py-4">
            {loadingMsgs ? (
              <Loader label="Yuklanmoqda..." />
            ) : msgs.length === 0 ? (
              <p className="py-12 text-center text-sm text-slate-400">
                Xabarlar yo'q
              </p>
            ) : (
              msgs.map((m) => (
                <div key={m.id} className={cn('flex', m.fromUser ? 'justify-start' : 'justify-end')}>
                  <div
                    className={cn(
                      'max-w-[75%] rounded-2xl px-3 py-2 text-sm',
                      m.fromUser ? 'bg-slate-100 text-slate-800' : 'bg-brand-600 text-white',
                    )}
                  >
                    <p className="whitespace-pre-wrap break-words">{m.text}</p>
                    <div
                      className={cn(
                        'mt-0.5 text-right text-[10px]',
                        m.fromUser ? 'text-slate-400' : 'text-brand-100',
                      )}
                    >
                      {formatTime(m.createdAt)}
                      {!m.fromUser && m.adminName && (
                        <span className="ml-1 opacity-75">· {m.adminName}</span>
                      )}
                    </div>
                  </div>
                </div>
              ))
            )}
            <div ref={bottomRef} />
          </div>

          {/* Yuborish */}
          <form onSubmit={handleSend} className="flex items-center gap-2 border-t border-slate-100 p-3">
            <textarea
              rows={1}
              className="flex-1 resize-none rounded-lg border border-slate-200 px-3 py-2 text-sm outline-none focus:border-brand-400 focus:ring-2 focus:ring-brand-100"
              placeholder="Javob yozing..."
              value={text}
              onChange={(e) => setText(e.target.value)}
              onKeyDown={(e) => {
                if (e.key === 'Enter' && !e.shiftKey) {
                  e.preventDefault()
                  handleSend(e as unknown as React.FormEvent)
                }
              }}
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
        </Card>
      )}
    </div>
  )
}
