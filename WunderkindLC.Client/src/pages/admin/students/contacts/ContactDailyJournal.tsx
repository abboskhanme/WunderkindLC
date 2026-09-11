import { useEffect, useState } from 'react'
import { Link } from 'react-router-dom'
import { CalendarDays, ChevronDown, ChevronRight, Phone } from 'lucide-react'
import { getContactJournal, type ContactJournalDay } from '@/api/services/contacts'
import { Card } from '@/components/ui/Card'
import { Loader } from '@/components/ui/Loader'
import { apiErrorMessage, cn, formatDate } from '@/lib/utils'

/**
 * KUNLIK JURNAL — "kimga qo'ng'iroq qilindi, QACHON, NIMA dedi va qaysi SABAB bilan",
 * HAR KUN ALOHIDA.
 *
 * <p>Hisobotdagi jadvallar "nechta" ga javob beradi, jurnal esa kunning O'ZINI ko'rsatadi:
 * rahbar bir kunni ochib, o'sha kuni nima bo'lganini boshdan-oxir o'qiy oladi.</p>
 *
 * <p>⚠️ Kunlar YANGISIDAN eskisiga, kun ICHIDA esa ertalabdan kechgacha (server shunday
 * qaytaradi) — jurnal xronologik o'qilsin. BIRINCHI kun ochiq holda keladi: davr sifatida
 * "bugun" tanlanganda operator hech narsa bosmasdan bugungi ishni ko'radi.</p>
 */

/** Hodisa turining rangi — jurnalda qator nima ekani bir qarashda ko'rinsin. */
const typeTone: Record<string, string> = {
  contact: 'bg-brand-50 text-brand-700',
  created: 'bg-amber-50 text-amber-700',
  note: 'bg-slate-100 text-slate-600',
  reopen: 'bg-sky-50 text-sky-700',
}

/** Natija rangi: gaplashildi — yashil, ko'tarmadi/band — qizg'ish. */
const resultTone: Record<string, string> = {
  answered: 'bg-emerald-50 text-emerald-700',
  other: 'bg-emerald-50 text-emerald-700',
  no_answer: 'bg-rose-50 text-rose-600',
  busy: 'bg-amber-50 text-amber-700',
  wrong_number: 'bg-rose-50 text-rose-600',
}

export function ContactDailyJournal({ from, to }: { from: string; to: string }) {
  const [days, setDays] = useState<ContactJournalDay[]>([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState('')
  /** Yopilgan kunlar (standart holat — OCHIQ; birinchi kun ayniqsa muhim). */
  const [collapsed, setCollapsed] = useState<Record<string, boolean>>({})
  /** Faqat qo'ng'iroqlar (bog'lanish urinishlari) yoki barcha hodisalar. */
  const [onlyCalls, setOnlyCalls] = useState(true)

  useEffect(() => {
    let alive = true
    // eslint-disable-next-line react-hooks/set-state-in-effect -- davr/filtr o'zgarganda qayta yuklash (maqsadli)
    setLoading(true)
    getContactJournal({
      from: from || undefined,
      to: to || undefined,
      type: onlyCalls ? 'contact' : undefined,
      limit: 500,
    })
      .then((d) => {
        if (!alive) return
        setDays(d)
        setError('')
      })
      .catch((e) => alive && setError(apiErrorMessage(e, "Jurnalni yuklab bo'lmadi")))
      .finally(() => alive && setLoading(false))
    return () => {
      alive = false
    }
  }, [from, to, onlyCalls])

  const totalItems = days.reduce((sum, d) => sum + d.items.length, 0)

  return (
    <Card
      title={
        <span className="inline-flex items-center gap-2">
          <CalendarDays className="h-4 w-4 text-slate-400" /> Kunlik jurnal
        </span>
      }
      sub="Har kun alohida: kimga qo'ng'iroq qilindi, qachon, nima dedi va qaysi sabab bilan."
      actions={
        <div className="flex items-center gap-1 rounded-lg border border-slate-200 p-0.5">
          <button
            type="button"
            onClick={() => setOnlyCalls(true)}
            className={cn(
              'rounded-md px-2.5 py-1 text-xs font-medium transition-colors',
              onlyCalls ? 'bg-brand-50 text-brand-700' : 'text-slate-500 hover:bg-slate-50',
            )}
          >
            Qo'ng'iroqlar
          </button>
          <button
            type="button"
            onClick={() => setOnlyCalls(false)}
            className={cn(
              'rounded-md px-2.5 py-1 text-xs font-medium transition-colors',
              !onlyCalls ? 'bg-brand-50 text-brand-700' : 'text-slate-500 hover:bg-slate-50',
            )}
            title="Talab ochilishi, izohlar va qayta ochishlar ham ko'rinadi"
          >
            Barcha amallar
          </button>
        </div>
      }
    >
      {loading ? (
        <Loader label="Yuklanmoqda..." />
      ) : error ? (
        <p className="rounded-lg bg-red-50 px-3 py-2 text-sm text-red-600">{error}</p>
      ) : days.length === 0 ? (
        <p className="py-8 text-center text-sm text-slate-400">
          Bu davrda {onlyCalls ? "bog'lanish bo'lmagan" : 'amal bo‘lmagan'}
        </p>
      ) : (
        <div className="space-y-3">
          {days.map((d) => {
            const isOpen = !collapsed[d.date]
            return (
              <div key={d.date} className="rounded-xl border border-slate-100">
                <button
                  type="button"
                  onClick={() => setCollapsed((prev) => ({ ...prev, [d.date]: !!isOpen }))}
                  className="flex w-full flex-wrap items-center gap-3 px-4 py-3 text-left transition-colors hover:bg-slate-50"
                >
                  {isOpen ? (
                    <ChevronDown className="h-4 w-4 shrink-0 text-slate-400" />
                  ) : (
                    <ChevronRight className="h-4 w-4 shrink-0 text-slate-400" />
                  )}
                  <span className="text-sm font-bold text-slate-800">{formatDate(d.date)}</span>
                  <span className="text-xs text-slate-400">{d.items.length} ta yozuv</span>
                  <span className="ml-auto flex flex-wrap items-center gap-2 text-xs">
                    {d.created > 0 && (
                      <span className="rounded-md bg-amber-50 px-2 py-0.5 text-amber-700">
                        yangi talab: {d.created}
                      </span>
                    )}
                    {d.attempts > 0 && (
                      <span className="rounded-md bg-slate-100 px-2 py-0.5 text-slate-600">
                        urinish: {d.attempts}
                      </span>
                    )}
                    {d.reached > 0 && (
                      <span className="rounded-md bg-emerald-50 px-2 py-0.5 font-semibold text-emerald-700">
                        bog'lanildi: {d.reached}
                      </span>
                    )}
                    {d.done > 0 && (
                      <span className="rounded-md bg-emerald-50 px-2 py-0.5 text-emerald-700">
                        hal bo'ldi: {d.done}
                      </span>
                    )}
                    {d.callback > 0 && (
                      <span className="rounded-md bg-sky-50 px-2 py-0.5 text-sky-700">
                        qayta qo'ng'iroq: {d.callback}
                      </span>
                    )}
                    {d.failed > 0 && (
                      <span className="rounded-md bg-rose-50 px-2 py-0.5 text-rose-600">
                        bo'lmadi: {d.failed}
                      </span>
                    )}
                  </span>
                </button>

                {isOpen && (
                  <ul className="divide-y divide-slate-100 border-t border-slate-100">
                    {d.items.map((it) => (
                      <li key={it.id} className="flex gap-3 px-4 py-2.5">
                        {/* Soat — jurnalning chap ustuni: "qachon qilingan" birinchi savol. */}
                        <span className="w-12 shrink-0 pt-0.5 font-mono text-sm font-semibold text-slate-500">
                          {it.time || '—'}
                        </span>
                        <div className="min-w-0 flex-1">
                          <div className="flex flex-wrap items-center gap-2">
                            <Link
                              to={`/admin/students/${it.studentId}`}
                              className="text-sm font-semibold text-slate-800 hover:text-brand-600 hover:underline"
                            >
                              {it.studentName || "Noma'lum"}
                            </Link>
                            {it.type !== 'contact' && (
                              <span
                                className={cn(
                                  'rounded-md px-2 py-0.5 text-xs',
                                  typeTone[it.type] ?? 'bg-slate-100 text-slate-600',
                                )}
                              >
                                {it.typeLabel}
                              </span>
                            )}
                            {it.resultLabel && (
                              <span
                                className={cn(
                                  'rounded-md px-2 py-0.5 text-xs',
                                  resultTone[it.result] ?? 'bg-slate-100 text-slate-600',
                                )}
                              >
                                {it.resultLabel}
                              </span>
                            )}
                            {it.nextStatusLabel && it.type === 'contact' && (
                              <span className="text-xs text-slate-400">
                                → {it.nextStatusLabel}
                                {it.dueDate ? ` (${formatDate(it.dueDate)})` : ''}
                              </span>
                            )}
                            {it.reasonLabel && (
                              <span className="rounded-md bg-slate-50 px-2 py-0.5 text-xs text-slate-500">
                                sabab: {it.reasonLabel}
                              </span>
                            )}
                          </div>

                          {it.response ? (
                            <p className="mt-1 whitespace-pre-wrap break-words text-sm text-slate-700">
                              {it.response}
                            </p>
                          ) : (
                            it.type === 'contact' && (
                              <p className="mt-1 text-sm italic text-slate-300">javob yozilmagan</p>
                            )
                          )}

                          <div className="mt-0.5 flex flex-wrap items-center gap-2 text-xs text-slate-400">
                            {it.actorName && <span>{it.actorName}</span>}
                            {it.phones.map((p) => (
                              <a
                                key={p}
                                href={`tel:${p}`}
                                onClick={(e) => e.stopPropagation()}
                                className="inline-flex items-center gap-1 text-slate-400 hover:text-brand-600"
                              >
                                <Phone className="h-3 w-3" /> {p}
                              </a>
                            ))}
                          </div>
                        </div>
                      </li>
                    ))}
                  </ul>
                )}
              </div>
            )
          })}

          {totalItems >= 500 && (
            <p className="text-center text-xs text-slate-400">
              Eng so'nggi 500 ta yozuv ko'rsatildi — davrni toraytiring (masalan kalendardan bitta
              kunni tanlang).
            </p>
          )}
        </div>
      )}
    </Card>
  )
}
