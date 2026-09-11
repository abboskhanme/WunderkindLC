import { useEffect, useMemo, useState } from 'react'
import { Link } from 'react-router-dom'
import { EyeOff, MessageSquareQuote, Trash2, Filter } from 'lucide-react'
import type { TeacherReviewFeedItem } from '@/types'
import { getTeacherReviewFeed, deleteTeacherReview } from '@/api/services/teacherReviews'
import { Card } from '@/components/ui/Card'
import { Loader } from '@/components/ui/Loader'
import { apiErrorMessage, cn, formatDate } from '@/lib/utils'

/**
 * O'QITUVCHI PROFILIDAGI «Fikrlar» bo'limi — shu o'qituvchi haqida o'quvchilardan yig'ilgan
 * BARCHA fikrlar bir joyda, eng yangisi tepada. Yozuvlar vaqt o'tgani sayin yig'ilib boradi.
 *
 * YOZISH bu yerda EMAS: fikr o'quvchi profilidagi «Fikr-mulohaza» tabida yoziladi (u yerda
 * o'quvchining qaysi guruhda ekani va o'qituvchisi aniq bo'ladi). Bu yerda faqat ko'rish,
 * guruh bo'yicha filtrlash va xato yozuvni o'chirish.
 *
 * RUXSAT: faqat admin/superadmin (server ham shu rolda cheklaydi). O'QITUVCHINING O'ZIGA bu
 * ma'lumot berilmaydi — o'qituvchi portalida va Flutter ilovasida bunday ekran yo'q.
 */
export function TeacherReviewsFeed({ teacherId }: { teacherId: string }) {
  const [items, setItems] = useState<TeacherReviewFeedItem[]>([])
  const [total, setTotal] = useState(0)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState('')
  const [deletingId, setDeletingId] = useState<string | null>(null)
  /** Guruh bo'yicha filtr ('' — hammasi) */
  const [groupId, setGroupId] = useState('')

  useEffect(() => {
    let alive = true
    setLoading(true)
    getTeacherReviewFeed(teacherId)
      .then((d) => {
        if (!alive) return
        setItems(d.items)
        setTotal(d.total)
      })
      .catch((e) => alive && setError(apiErrorMessage(e, "Fikrlarni yuklab bo'lmadi")))
      .finally(() => alive && setLoading(false))
    return () => {
      alive = false
    }
  }, [teacherId])

  /** Filtr uchun guruhlar ro'yxati (fikr yozilgan guruhlar). */
  const groups = useMemo(() => {
    const map = new Map<string, string>()
    for (const r of items) if (r.groupId) map.set(r.groupId, r.groupName || r.groupId)
    return [...map.entries()].sort((a, b) => a[1].localeCompare(b[1]))
  }, [items])

  const shown = groupId ? items.filter((r) => r.groupId === groupId) : items

  const remove = async (id: string) => {
    setDeletingId(id)
    setError('')
    try {
      await deleteTeacherReview(id)
      setItems((prev) => prev.filter((r) => r.id !== id))
      setTotal((t) => Math.max(0, t - 1))
    } catch (e) {
      setError(apiErrorMessage(e, "O'chirib bo'lmadi"))
    } finally {
      setDeletingId(null)
    }
  }

  if (loading) return <Card><Loader label="Yuklanmoqda..." /></Card>

  return (
    <div className="space-y-4">
      {/* Maxfiylik ogohlantirishi — bu ekran o'qituvchiga ko'rsatilmasligi kerak. */}
      <div className="flex items-start gap-2.5 rounded-xl border border-amber-200 bg-amber-50/70 px-3.5 py-2.5">
        <EyeOff className="mt-0.5 h-4 w-4 shrink-0 text-amber-600" />
        <p className="text-xs leading-relaxed text-amber-900">
          Bu yozuvlar <b>faqat ma'muriyat uchun</b>. O'qituvchi ilovasida va o'qituvchi portalida
          ular ko'rinmaydi — o'qituvchiga faqat «AI tahlil» bo'limidagi umumlashtirilgan xulosa
          ko'rsatiladi (o'quvchi ismisiz).
        </p>
      </div>

      {error && <Card className="py-2.5 text-center text-sm text-red-500">{error}</Card>}

      {total === 0 ? (
        <Card className="py-12 text-center text-sm text-slate-400">
          Bu o'qituvchi haqida hali fikr yozilmagan.
          <span className="mt-1 block text-xs">
            Fikr o'quvchi profilidagi «Fikr-mulohaza» tabida yoziladi.
          </span>
        </Card>
      ) : (
        <>
          {/* Sarlavha + guruh filtri */}
          <div className="flex flex-wrap items-center gap-2">
            <span className="inline-flex items-center gap-1.5 text-sm font-semibold text-slate-700">
              <MessageSquareQuote className="h-4 w-4 text-brand-600" />
              {total} ta fikr
            </span>
            {groups.length > 1 && (
              <div className="ml-auto flex items-center gap-1.5">
                <Filter className="h-3.5 w-3.5 text-slate-400" />
                <select
                  value={groupId}
                  onChange={(e) => setGroupId(e.target.value)}
                  className="rounded-lg border border-slate-200 bg-white px-2.5 py-1.5 text-xs text-slate-600 outline-none focus:border-brand-400"
                >
                  <option value="">Barcha guruhlar</option>
                  {groups.map(([id, name]) => (
                    <option key={id} value={id}>
                      {name}
                    </option>
                  ))}
                </select>
              </div>
            )}
          </div>

          {/* Fikrlar — eng yangisi tepada */}
          <div className="space-y-2.5">
            {shown.map((r) => (
              <div
                key={r.id}
                className={cn(
                  'group rounded-xl border border-slate-200 bg-white p-4',
                  deletingId === r.id && 'opacity-50',
                )}
              >
                <p className="whitespace-pre-wrap text-sm leading-relaxed text-slate-700">
                  {r.text}
                </p>
                <div className="mt-2.5 flex flex-wrap items-center gap-x-2 gap-y-1 text-xs text-slate-400">
                  {r.studentId ? (
                    <Link
                      to={`/admin/students/${r.studentId}`}
                      className="font-medium text-brand-600 hover:underline"
                    >
                      {r.studentName || "O'quvchi"}
                    </Link>
                  ) : (
                    <span className="font-medium text-slate-500">{r.studentName || "O'quvchi"}</span>
                  )}
                  {r.groupName && (
                    <>
                      <span>·</span>
                      <span>{r.groupName}</span>
                    </>
                  )}
                  <span>·</span>
                  <span>{formatDate(r.createdAt.slice(0, 10))}</span>
                  {r.createdAt.length >= 16 && <span>{r.createdAt.slice(11, 16)}</span>}
                  {r.createdBy && <span>· {r.createdBy}</span>}
                  <button
                    type="button"
                    onClick={() => void remove(r.id)}
                    disabled={deletingId === r.id}
                    className="ml-auto rounded p-1 text-slate-300 opacity-0 transition-opacity hover:bg-red-50 hover:text-red-500 group-hover:opacity-100"
                    title="O'chirish"
                  >
                    <Trash2 className="h-3.5 w-3.5" />
                  </button>
                </div>
              </div>
            ))}
          </div>

          {total > items.length && (
            <p className="text-center text-xs text-slate-400">
              Oxirgi {items.length} ta ko'rsatilyapti ({total} tadan).
            </p>
          )}
        </>
      )}
    </div>
  )
}
