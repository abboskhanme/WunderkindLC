import { useEffect, useState } from 'react'
import { MessagesSquare } from 'lucide-react'
import { getStudentTelegramDeliveries, type StudentTelegramDelivery } from '@/api/services/students'
import { Card } from '@/components/ui/Card'
import { DELIVERY_META, deliveryView } from '@/lib/telegramDelivery'
import { relationIcon, relationLabel } from '@/lib/relations'
import { cn, formatDateTime, formatTime } from '@/lib/utils'
import { Empty } from './shared'

/** Xabar matni ro'yxatda qisqa ko'rinadi — to'liq matn bosilganda ochiladi. */
const PREVIEW_CHARS = 160

/**
 * «Telegram xabarlari» (CRM upgrade P2.7) — o'quvchi bo'yicha ketgan oxirgi 50 ta Telegram xabar:
 * turi, kimga (qarindoshlik), holati (✓ Yuborildi · ✓✓ Qabul qildi · ⛔ Bloklagan · ✗ Xato) va
 * «Qabul qildi» vaqti.
 *
 * O'qish — fon so'rovi: ruxsat yo'q (403) yoki xato bo'lsa bo'lim chizilmaydi (profilning asosiy
 * ma'lumoti emas, `error-visibility.md` §4).
 */
export function TelegramDeliveriesSection({ studentId }: { studentId: string }) {
  const [rows, setRows] = useState<StudentTelegramDelivery[] | null>(null)
  const [openId, setOpenId] = useState<string | null>(null)

  useEffect(() => {
    let alive = true
    getStudentTelegramDeliveries(studentId, 50)
      .then((r) => {
        if (alive) setRows(r)
      })
      .catch(() => {
        if (alive) setRows(null)
      })
    return () => {
      alive = false
    }
  }, [studentId])

  if (rows === null) return null

  return (
    <Card>
      <div className="mb-4 flex flex-wrap items-center gap-x-2 gap-y-3">
        <MessagesSquare className="h-5 w-5 text-brand-600" />
        <h2 className="font-semibold text-slate-800">Telegram xabarlari</h2>
        {rows.length > 0 && <span className="text-xs text-slate-400">oxirgi {rows.length} ta</span>}
      </div>
      {rows.length === 0 ? (
        <Empty>Bu o'quvchi bo'yicha Telegram xabar yuborilmagan.</Empty>
      ) : (
        <ul className="divide-y divide-slate-100">
          {rows.map((d) => {
            const view = deliveryView(d.status, d.ackAt)
            const meta = DELIVERY_META[view]
            const open = openId === d.id
            const long = d.textPlain.length > PREVIEW_CHARS
            return (
              <li key={d.id} className="py-2.5 first:pt-0 last:pb-0">
                <div className="flex flex-wrap items-center gap-x-2 gap-y-1">
                  <span className="text-sm font-medium text-slate-700">{d.label || d.trigger || 'Xabar'}</span>
                  <span className="text-xs text-slate-500" title="Kimga">
                    {relationIcon(d.relation)} {relationLabel(d.relation)}
                  </span>
                  <span className={cn('rounded-md px-2 py-0.5 text-xs font-semibold', meta.cls)}>
                    {meta.icon} {meta.label}
                    {view === 'acked' && d.ackAt ? ` ${formatTime(d.ackAt)}` : ''}
                  </span>
                  <span className="ml-auto text-xs text-slate-400">{formatDateTime(d.sentAt ?? d.createdAt)}</span>
                </div>
                {d.textPlain && (
                  <p className="mt-1 whitespace-pre-line break-words text-xs text-slate-500">
                    {open || !long ? d.textPlain : `${d.textPlain.slice(0, PREVIEW_CHARS)}…`}
                    {long && (
                      <button
                        type="button"
                        onClick={() => setOpenId(open ? null : d.id)}
                        className="ml-1 font-semibold text-brand-600 hover:underline"
                      >
                        {open ? 'Yopish' : "To'liq"}
                      </button>
                    )}
                  </p>
                )}
              </li>
            )
          })}
        </ul>
      )}
    </Card>
  )
}
