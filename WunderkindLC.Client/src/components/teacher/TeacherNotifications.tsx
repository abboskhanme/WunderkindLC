import { useEffect, useRef, useState } from 'react'
import { Bell, CheckCircle2, X } from 'lucide-react'
import {
  confirmTeacherNotification,
  getTeacherNotifications,
  markTeacherNotificationsRead,
  type NotificationsResponse,
} from '@/api/services/teacher'
import { apiErrorMessage, cn, formatDateTime } from '@/lib/utils'

/**
 * O'qituvchi bildirishnomalari — yuqori paneldagi qo'ng'iroqcha.
 * Telefonda pastdan chiqadigan varaq, kompyuterda qo'ng'iroq ostida ochiladigan panel.
 * Ochilganda hammasi "o'qildi" bo'ladi (ikkilamchi amal — xatosi jim, error-visibility.md §4);
 * "Tasdiqlash" esa asosiy amal: xato bo'lsa panelda ko'rsatiladi va belgi qaytariladi.
 */
export function TeacherNotifications() {
  const [data, setData] = useState<NotificationsResponse | null>(null)
  const [open, setOpen] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const ref = useRef<HTMLDivElement>(null)

  useEffect(() => {
    // Fon o'qish — xato bo'lsa qo'ng'iroqcha shunchaki bo'sh qoladi.
    getTeacherNotifications().then(setData).catch(() => {})
  }, [])

  useEffect(() => {
    if (!open) return
    const onDown = (e: MouseEvent) => {
      if (window.innerWidth >= 640 && ref.current && !ref.current.contains(e.target as Node)) setOpen(false)
    }
    const onKey = (e: KeyboardEvent) => e.key === 'Escape' && setOpen(false)
    document.addEventListener('mousedown', onDown)
    document.addEventListener('keydown', onKey)
    return () => {
      document.removeEventListener('mousedown', onDown)
      document.removeEventListener('keydown', onKey)
    }
  }, [open])

  const toggle = () => {
    const next = !open
    setOpen(next)
    setError(null)
    if (next && data && data.unread > 0) {
      markTeacherNotificationsRead().catch(() => {})
      setData((p) => (p ? { unread: 0, items: p.items.map((i) => ({ ...i, read: true })) } : p))
    }
  }

  const confirm = async (id: string) => {
    setError(null)
    setData((p) => (p ? { ...p, items: p.items.map((i) => (i.id === id ? { ...i, confirmed: true } : i)) } : p))
    try {
      await confirmTeacherNotification(id)
    } catch (err) {
      setData((p) => (p ? { ...p, items: p.items.map((i) => (i.id === id ? { ...i, confirmed: false } : i)) } : p))
      setError(apiErrorMessage(err, "Tasdiqlab bo'lmadi"))
    }
  }

  const unread = data?.unread ?? 0
  const items = data?.items ?? []

  const list = (
    <>
      {error && <p className="mb-2 rounded-lg bg-red-50 px-3 py-2 text-[13px] text-red-600">{error}</p>}
      {items.length === 0 ? (
        <div className="py-10 text-center text-[13px] text-mute">Bildirishnoma yo'q. Yangi e'lonlar shu yerda ko'rinadi.</div>
      ) : (
        <div className="space-y-2">
          {items.map((n) => (
            <div key={n.id} className="flex gap-3 rounded-lg border border-line bg-panel2 p-3">
              <div className="grid h-9 w-9 shrink-0 place-items-center rounded-lg bg-brand-50 text-brand-600">
                <Bell className="h-4 w-4" />
              </div>
              <div className="min-w-0 flex-1">
                <p className="text-[14px] font-bold text-ink">{n.title}</p>
                {n.body && <p className="mt-0.5 whitespace-pre-line text-[13px] text-mute">{n.body}</p>}
                <p className="mt-1 text-[11px] text-faint">{formatDateTime(n.createdAt)}</p>
                {n.type === 'announcement' &&
                  (n.confirmed ? (
                    <p className="mt-2 inline-flex items-center gap-1 text-[12px] font-bold text-emerald-600">
                      <CheckCircle2 className="h-3.5 w-3.5" /> Tasdiqlandi
                    </p>
                  ) : (
                    <button
                      type="button"
                      onClick={() => confirm(n.id)}
                      className="mt-2 rounded-lg bg-brand-600 px-4 py-1.5 text-[13px] font-semibold text-white hover:bg-brand-700"
                    >
                      Tasdiqlash
                    </button>
                  ))}
              </div>
            </div>
          ))}
        </div>
      )}
    </>
  )

  return (
    <div className="relative" ref={ref}>
      <button
        type="button"
        onClick={toggle}
        className="relative flex h-9 w-9 items-center justify-center rounded-lg text-brand-600 transition-colors hover:bg-brand-50"
        aria-label="Bildirishnomalar"
        title="Bildirishnomalar"
      >
        <Bell className="h-5 w-5" />
        {unread > 0 && (
          <span className="absolute -right-0.5 -top-0.5 flex h-4 min-w-4 items-center justify-center rounded-full bg-[#e53e3e] px-1 text-[10px] font-bold text-white ring-2 ring-white">
            {unread > 9 ? '9+' : unread}
          </span>
        )}
      </button>

      {open && (
        <>
          {/* Telefon: pastdan varaq */}
          <div className="fixed inset-0 z-[60] flex items-end bg-black/40 sm:hidden" onClick={() => setOpen(false)}>
            <div
              className="max-h-[80dvh] w-full overflow-y-auto rounded-t-2xl bg-white p-4 pb-[calc(1.5rem+env(safe-area-inset-bottom))]"
              onClick={(e) => e.stopPropagation()}
            >
              <div className="mx-auto mb-3 h-1.5 w-10 rounded-full bg-line" />
              <div className="mb-3 flex items-center justify-between">
                <p className="text-[17px] font-bold text-ink">Bildirishnomalar</p>
                <button
                  type="button"
                  onClick={() => setOpen(false)}
                  className="grid h-9 w-9 place-items-center rounded-lg bg-panel3 text-ink"
                  aria-label="Yopish"
                >
                  <X className="h-4 w-4" />
                </button>
              </div>
              {list}
            </div>
          </div>
          {/* Kompyuter: qo'ng'iroq ostida panel */}
          <div
            className={cn(
              'absolute right-0 top-full z-50 mt-2 hidden max-h-[70vh] w-[380px] overflow-y-auto rounded-xl border border-line bg-white p-3 shadow-[0_12px_32px_rgba(16,24,40,0.12)] sm:block',
            )}
          >
            <p className="mb-2 px-1 text-[15px] font-bold text-ink">Bildirishnomalar</p>
            {list}
          </div>
        </>
      )}
    </div>
  )
}
