import { useEffect, useState } from 'react'
import { History, MessageCircle } from 'lucide-react'
import type { Broadcast } from '@/types'
import { getBroadcasts } from '@/api/services/messages'
import { Card } from '@/components/ui/Card'
import { Badge } from '@/components/ui/Badge'
import { Loader } from '@/components/ui/Loader'
import { apiErrorMessage, formatDate } from '@/lib/utils'

/**
 * Yuborilgan xabarlar tarixi — Telegram e'lonlari (vaqt bo'yicha kamayish tartibida).
 * SMS va push kanallari 2026-10 da olib tashlandi; ularning eski tarixi bazada qoladi, lekin
 * bu yerda ko'rsatilmaydi.
 */
export function HistoryTab() {
  const [items, setItems] = useState<Broadcast[]>([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState('')

  useEffect(() => {
    getBroadcasts()
      .then((list) => setItems([...list].sort((a, b) => (a.createdAt < b.createdAt ? 1 : -1))))
      // Sahifaning ASOSIY ma'lumoti — xato bo'lsa sababi ko'rsatiladi (`error-visibility.md` §4).
      .catch((e) => setError(apiErrorMessage(e, "Tarixni yuklab bo'lmadi")))
      .finally(() => setLoading(false))
  }, [])

  if (loading) return <Loader label="Yuklanmoqda..." />

  return (
    <Card title="Yuborilgan xabarlar" sub="Telegram bot orqali yuborilgan e'lonlar">
      {error && <p className="mb-3 text-sm font-medium text-red-600">{error}</p>}
      {items.length === 0 ? (
        <div className="py-10 text-center">
          <History className="mx-auto mb-2 h-6 w-6 text-slate-300" />
          <p className="text-sm text-slate-400">Hali xabar yuborilmagan</p>
        </div>
      ) : (
        <div className="space-y-2.5">
          {items.map((it) => (
            <div key={it.id} className="rounded-lg border border-slate-100 p-3">
              <div className="mb-1 flex flex-wrap items-center gap-2">
                <Badge tone="blue">
                  <MessageCircle className="h-3 w-3" /> Telegram
                </Badge>
                {it.className && <Badge>{it.className}</Badge>}
                <span className="ml-auto font-mono text-xs text-slate-400">{formatDate(it.createdAt)}</span>
              </div>
              <p className="whitespace-pre-wrap break-words text-sm text-slate-700">{it.text}</p>
              <div className="mt-1.5 flex items-center justify-between text-xs text-slate-400">
                <span className="font-mono">
                  {it.sentCount}/{it.recipientCount} yuborildi
                </span>
                {it.senderName && <span>{it.senderName}</span>}
              </div>
            </div>
          ))}
        </div>
      )}
    </Card>
  )
}
