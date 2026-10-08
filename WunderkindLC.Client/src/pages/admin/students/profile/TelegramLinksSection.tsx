import { useEffect, useState } from 'react'
import { Send, Unlink } from 'lucide-react'
import {
  getStudentTelegramLinks,
  unlinkStudentTelegram,
  type StudentTelegramLink,
} from '@/api/services/students'
import { Card } from '@/components/ui/Card'
import { apiErrorMessage, cn, formatDateTime } from '@/lib/utils'
import { formatPhone } from '@/lib/numberFormat'
import { Empty } from './shared'

/**
 * «Telegram ulanishlari» — o'quvchiga bog'langan bot chatlari (qarz/davomat xabarlari va kirish kodi
 * shu chatlarga ketadi). Server qoidasi — `TelegramLinks`: raqam tahrirlansa ESKI raqamli ulanish o'zi
 * uziladi; bu yerda esa qo'lda «Uzish» (masalan telefon begona odamga o'tgan).
 *
 * Raqami o'quvchining joriy raqamlariga mos KELMAYDIGAN ulanish sariq belgi bilan ko'rsatiladi.
 * O'qish — fon so'rovi: ruxsat yo'q (403) yoki xato bo'lsa bo'lim chizilmaydi (profilning asosiy
 * ma'lumoti emas). Uzish xatosi — `alert` (ro'yxat qatoridagi amal, `error-visibility.md`).
 */
export function TelegramLinksSection({ studentId, canUnlink }: { studentId: string; canUnlink: boolean }) {
  const [links, setLinks] = useState<StudentTelegramLink[] | null>(null)
  const [busyId, setBusyId] = useState<string | null>(null)
  const [tick, setTick] = useState(0)

  useEffect(() => {
    let alive = true
    getStudentTelegramLinks(studentId)
      .then((rows) => {
        if (alive) setLinks(rows)
      })
      // Ikkilamchi (fon) o'qish — ruxsat yo'q xodimda bo'lim shunchaki ko'rinmaydi.
      .catch(() => {
        if (alive) setLinks(null)
      })
    return () => {
      alive = false
    }
  }, [studentId, tick])

  const unlink = async (link: StudentTelegramLink) => {
    const who = link.parentName || formatPhone(link.phone) || 'bu chat'
    if (!confirm(`"${who}" Telegram ulanishi uzilsinmi? Bu chat endi o'quvchi bo'yicha xabar va kirish kodini olmaydi.`)) return
    setBusyId(link.id)
    try {
      await unlinkStudentTelegram(studentId, link.id)
      setTick((t) => t + 1)
    } catch (err) {
      alert(apiErrorMessage(err, "Ulanishni uzib bo'lmadi"))
    } finally {
      setBusyId(null)
    }
  }

  if (links === null) return null

  return (
    <Card>
      <div className="mb-4 flex flex-wrap items-center gap-x-2 gap-y-3">
        <Send className="h-5 w-5 text-brand-600" />
        <h2 className="font-semibold text-slate-800">Telegram ulanishlari</h2>
      </div>
      {links.length === 0 ? (
        <Empty>Botga hech kim ulanmagan.</Empty>
      ) : (
        <ul className="divide-y divide-slate-100">
          {links.map((l) => (
            <li key={l.id} className="flex flex-wrap items-center gap-x-3 gap-y-1 py-2.5 first:pt-0 last:pb-0">
              <div className="min-w-0 flex-1">
                <div className="flex flex-wrap items-center gap-2">
                  <span className="text-sm font-medium text-slate-700">{l.parentName || 'Ismsiz'}</span>
                  {l.phone && <span className="font-mono text-xs text-slate-500">{formatPhone(l.phone)}</span>}
                  {!l.phoneMatches && (
                    <span
                      className={cn('rounded-md bg-amber-100 px-2 py-0.5 text-xs font-medium text-amber-700')}
                      title="Bu raqam o'quvchining joriy raqamlaridan hech biriga mos emas"
                    >
                      Raqam mos emas
                    </span>
                  )}
                </div>
                <p className="mt-0.5 text-xs text-slate-400">Ulangan: {formatDateTime(l.createdAt)}</p>
              </div>
              {canUnlink && (
                <button
                  type="button"
                  onClick={() => unlink(l)}
                  disabled={busyId === l.id}
                  className="inline-flex items-center gap-1.5 rounded-lg border border-slate-200 px-2.5 py-1.5 text-xs font-semibold text-slate-600 transition-colors hover:border-red-200 hover:bg-red-50 hover:text-red-600 disabled:cursor-not-allowed disabled:opacity-50"
                >
                  <Unlink className="h-3.5 w-3.5" /> Uzish
                </button>
              )}
            </li>
          ))}
        </ul>
      )}
    </Card>
  )
}
