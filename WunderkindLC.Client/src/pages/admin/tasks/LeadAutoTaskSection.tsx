import { useEffect, useState } from 'react'
import { Send } from 'lucide-react'
import {
  getAssignees, getLeadAutoTaskSettings, setLeadAutoTaskSettings,
  type WorkTaskAssignee, type WorkTaskBoard,
} from '@/api/services/workTasks'
import { Button } from '@/components/ui/Button'
import { cn, apiErrorMessage } from '@/lib/utils'

/**
 * LIDGA AVTOMATIK TOPSHIRIQ (SPEC-backlog F4 L1) — «Doska sozlamalari» oynasining bo'limi.
 *
 * <p>Lid tashqi kanaldan (forma, landing, daraja testi, Instagram, reklama) ma'lumot qoldirganda tanlangan xodimlarga
 * NAVBAT bilan «Lidga qo'ng'iroq» topshirig'i (bugungi muddat) va xabar (qo'ng'iroqcha + Telegram) ketadi. Navbat
 * tartibi — belgilash tartibi (raqam bilan ko'rsatiladi). Qo'lda kiritilgan lidga topshiriq yaratilmaydi.</p>
 */
export function LeadAutoTaskSection({ boards }: { boards: WorkTaskBoard[] }) {
  const [loaded, setLoaded] = useState(false)
  const [staff, setStaff] = useState<WorkTaskAssignee[]>([])
  const [enabled, setEnabled] = useState(false)
  const [pool, setPool] = useState<string[]>([])
  const [boardId, setBoardId] = useState('')
  const [error, setError] = useState('')
  const [saved, setSaved] = useState(false)
  const [busy, setBusy] = useState(false)

  useEffect(() => {
    let alive = true
    Promise.all([getLeadAutoTaskSettings(), getAssignees()])
      .then(([s, people]) => {
        if (!alive) return
        setEnabled(s.enabled)
        setPool(s.assigneeIds)
        setBoardId(s.boardId ?? '')
        setStaff(people)
        setLoaded(true)
      })
      .catch((err) => alive && setError(apiErrorMessage(err, "Sozlamani o'qib bo'lmadi")))
    return () => {
      alive = false
    }
  }, [])

  const toggle = (id: string) =>
    setPool((p) => (p.includes(id) ? p.filter((x) => x !== id) : [...p, id]))

  const save = async () => {
    if (busy) return
    setBusy(true)
    setError('')
    try {
      await setLeadAutoTaskSettings({ enabled, assigneeIds: pool, boardId: boardId || null })
      setSaved(true)
      setTimeout(() => setSaved(false), 1600)
    } catch (err) {
      setError(apiErrorMessage(err, "Sozlamani saqlab bo'lmadi"))
    } finally {
      setBusy(false)
    }
  }

  return (
    <section className="rounded-xl border border-slate-100 p-3">
      <h4 className="mb-2 text-sm font-bold text-slate-700">Lidga avtomatik topshiriq</h4>
      <p className="mb-3 text-xs text-slate-400">
        Lid forma, landing, daraja testi, Instagram yoki reklama orqali ma'lumot qoldirganda tanlangan xodimlarga
        NAVBAT bilan «Lidga qo'ng'iroq» topshirig'i (muddati — bugun) beriladi va xabar boradi (qo'ng'iroqcha +
        Telegram). Lidning mas'uli bo'lsa — topshiriq unga. Bir lidda bitta ochiq topshiriq; qo'lda kiritilgan lidga
        topshiriq yaratilmaydi.
      </p>

      {!loaded && !error && <p className="text-sm text-slate-400">Yuklanmoqda…</p>}

      {loaded && (
        <div className="space-y-3">
          <label className="flex cursor-pointer items-center gap-2">
            <input
              type="checkbox"
              checked={enabled}
              onChange={(e) => setEnabled(e.target.checked)}
              className="h-4 w-4 rounded border-slate-300 text-brand-600 focus:ring-brand-200"
            />
            <span className="text-sm font-medium text-slate-700">Yoqilgan</span>
          </label>

          <div>
            <div className="mb-1 text-xs font-semibold text-slate-500">
              Navbat bilan oladigan xodimlar ({pool.filter((id) => staff.some((u) => u.userId === id)).length} ta tanlangan)
            </div>
            <div className="max-h-52 divide-y divide-slate-100 overflow-y-auto rounded-xl border border-slate-100">
              {staff.map((u) => {
                const idx = pool.indexOf(u.userId)
                return (
                  <label key={u.userId} className="flex cursor-pointer items-center gap-2 px-3 py-2 hover:bg-slate-50">
                    <input
                      type="checkbox"
                      checked={idx >= 0}
                      onChange={() => toggle(u.userId)}
                      className="h-4 w-4 rounded border-slate-300 text-brand-600 focus:ring-brand-200"
                    />
                    <span
                      className={cn(
                        'inline-flex h-5 w-5 shrink-0 items-center justify-center rounded-full text-[11px] font-bold',
                        idx >= 0 ? 'bg-brand-600 text-white' : 'bg-slate-100 text-slate-300',
                      )}
                      title="Navbatdagi o'rni"
                    >
                      {idx >= 0 ? idx + 1 : '·'}
                    </span>
                    <span className="min-w-0 flex-1 truncate text-sm text-slate-700">{u.fullName}</span>
                    {u.position && <span className="hidden truncate text-xs text-slate-400 sm:inline">{u.position}</span>}
                    {!u.hasTelegram && (
                      <span className="inline-flex shrink-0 items-center gap-1 text-[11px] text-amber-600" title="Telegram botga ulanmagan — faqat qo'ng'iroqcha oladi">
                        <Send className="h-3 w-3" /> botsiz
                      </span>
                    )}
                  </label>
                )
              })}
              {staff.length === 0 && <p className="px-3 py-4 text-center text-sm text-slate-400">Xodim yo'q.</p>}
            </div>
          </div>

          <div className="flex flex-wrap items-center gap-2">
            <span className="text-sm text-slate-500">Doska:</span>
            <select
              value={boardId}
              onChange={(e) => setBoardId(e.target.value)}
              className="h-9 rounded-lg border border-slate-200 bg-white px-2 text-sm text-slate-700"
            >
              <option value="">Birinchi faol doska</option>
              {boards.map((b) => (
                <option key={b.id} value={b.id}>
                  {b.title}
                </option>
              ))}
            </select>
            <Button variant="secondary" onClick={save} disabled={busy}>
              {saved ? 'Saqlandi' : 'Saqlash'}
            </Button>
          </div>
        </div>
      )}

      {error && <p className="mt-2 text-sm text-rose-600">{error}</p>}
    </section>
  )
}
