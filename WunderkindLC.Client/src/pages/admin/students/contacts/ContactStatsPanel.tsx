import { useEffect, useState } from 'react'
import { Link } from 'react-router-dom'
import { Search, MessageSquareText } from 'lucide-react'
import {
  getContactStats, getContactResponses,
  type ContactStats, type ContactResponseRow,
} from '@/api/services/contacts'
import { MonthDayStrip } from '@/components/ui/MonthDayStrip'
import { currentMonth, monthRange, todayIso } from '@/lib/month'
import { ContactDailyJournal } from './ContactDailyJournal'
import { ContactAiPanel } from '@/components/ai/ContactAiPanel'
import { Card } from '@/components/ui/Card'
import { Button } from '@/components/ui/Button'
import { Loader } from '@/components/ui/Loader'
import { apiErrorMessage, cn, formatDate, formatDateTime } from '@/lib/utils'

/** "YYYY-MM-DD" — bugundan `days` kun oldin. */
function daysAgo(days: number): string {
  const d = new Date()
  d.setDate(d.getDate() - days)
  return d.toISOString().slice(0, 10)
}

const ranges = [
  { label: 'Bugun', days: 0 },
  { label: '7 kun', days: 6 },
  { label: '30 kun', days: 29 },
  { label: '90 kun', days: 89 },
]

/**
 * "BOG'LANISH KERAK" HISOBOTLARI.
 *
 * <p>Barcha sonlar HODISALARDAN (`ContactAttempt`) hisoblanadi, ya'ni "kim nima qildi" bo'yicha —
 * shuning uchun bir talab bir necha marta sanalishi mumkin (har urinish alohida). Bu ataylab:
 * savol "nechta odam bilan bog'lanildi" emas, "nechta bog'lanish bo'ldi".</p>
 *
 * <p>⚠️ "Bog'lanildi" ustuni FAQAT odam bilan haqiqatan gaplashilgan urinishlarni sanaydi
 * (server: `ContactService.Reached`) — ko'tarmagan qo'ng'iroq "urinish"ga kiradi, "bog'lanildi"ga
 * emas. Aks holda hisobot haqiqiy aloqani ko'rsatmasdi.</p>
 */
export function ContactStatsPanel() {
  /** Kalendarda ko'rinayotgan oy. */
  const [month, setMonth] = useState(currentMonth())
  /** Tanlangan KUN ("" — butun oy). Sahifa ochilganda BUGUN tanlangan bo'ladi. */
  const [day, setDay] = useState(todayIso())
  /** Qo'lda kiritilgan oraliq — tanlangan bo'lsa oy/kun tanlovidan USTUN turadi. */
  const [custom, setCustom] = useState<{ from: string; to: string } | null>(null)
  /** Kalendar kataklaridagi sonlar: oyning har kunidagi bog'lanish urinishlari. */
  const [monthCounts, setMonthCounts] = useState<Record<string, number>>({})

  // Davr — yuqoridagi uch manbadan HOSILA (bitta haqiqat manbai bo'lsin).
  const range = monthRange(month)
  const from = custom ? custom.from : day || range.from
  const to = custom ? custom.to : day || range.to
  const [data, setData] = useState<ContactStats | null>(null)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState('')

  /* --- Javoblar lentasi ("javobi nima dedi" matnlarini o'qish) --- */
  const [responses, setResponses] = useState<ContactResponseRow[]>([])
  const [respLoading, setRespLoading] = useState(true)
  /** Natija bo'yicha filtr ('' — hammasi). */
  const [respResult, setRespResult] = useState('')
  const [term, setTerm] = useState('')
  const [q, setQ] = useState('')

  /**
   * Kalendar kataklaridagi sonlar BUTUN OY bo'yicha — tanlangan davrga bog'liq emas.
   * Shuning uchun alohida (yengil) so'rov: aks holda bitta kun tanlanganda kalendar
   * bo'shab qolardi.
   */
  useEffect(() => {
    let active = true
    const r = monthRange(month)
    getContactStats(r.from, r.to)
      .then((d) => {
        if (!active) return
        setMonthCounts(Object.fromEntries(d.daily.map((x) => [x.date, x.attempts])))
      })
      .catch(() => { if (active) setMonthCounts({}) })
    return () => { active = false }
  }, [month])

  /** Oy joriy oyga qaytsa BUGUN yana tanlanadi (navbat sahifasidagi qoida bilan bir xil). */
  useEffect(() => {
    // eslint-disable-next-line react-hooks/set-state-in-effect -- oy almashganda tanlovni tiklash (maqsadli)
    setCustom(null)
    setDay(month === currentMonth() ? todayIso() : '')
  }, [month])

  useEffect(() => {
    let active = true
    // eslint-disable-next-line react-hooks/set-state-in-effect -- davr o'zgarganda qayta yuklash (maqsadli)
    setLoading(true)
    getContactStats(from || undefined, to || undefined)
      .then((d) => { if (active) { setData(d); setError('') } })
      .catch((e) => { if (active) setError(apiErrorMessage(e, "Hisobotni yuklab bo'lmadi")) })
      .finally(() => { if (active) setLoading(false) })
    return () => { active = false }
  }, [from, to])

  useEffect(() => {
    let active = true
    // eslint-disable-next-line react-hooks/set-state-in-effect -- filtr o'zgarganda qayta yuklash (maqsadli)
    setRespLoading(true)
    getContactResponses({
      from: from || undefined,
      to: to || undefined,
      result: respResult || undefined,
      q: q || undefined,
      limit: 200,
    })
      .then((r) => { if (active) setResponses(r) })
      .catch(() => { if (active) setResponses([]) })
      .finally(() => { if (active) setRespLoading(false) })
    return () => { active = false }
  }, [from, to, respResult, q])

  return (
    <div className="space-y-4">
      <Card
        title="Davr"
        sub="Kunni bosing yoki «Butun oy» ni tanlang. Sanoqlar shu davrdagi amallar bo'yicha (navbat sanoqlari — joriy holat)."
      >
        <div className="space-y-3">
          <MonthDayStrip
            month={month}
            onMonthChange={setMonth}
            selected={custom ? '' : day}
            onSelect={(d) => { setCustom(null); setDay(d) }}
            counts={monthCounts}
            hint="Katakdagi son — o'sha kundagi bog'lanish urinishlari."
          />

          <div className="flex flex-wrap items-end gap-3">
            <button
              type="button"
              onClick={() => { setCustom(null); setDay('') }}
              className={cn(
                'rounded-lg border px-3 py-1.5 text-sm font-medium transition-colors',
                !custom && !day
                  ? 'border-brand-500 bg-brand-50 text-brand-700'
                  : 'border-slate-200 bg-white text-slate-600 hover:bg-slate-50',
              )}
            >
              Butun oy
            </button>
            {ranges.map((r) => (
              <button
                key={r.label}
                type="button"
                // Tez oraliqlar QO'LDA oraliq sifatida qo'yiladi — kalendar tanlovi tozalanadi.
                onClick={() => { setDay(''); setCustom({ from: daysAgo(r.days), to: todayIso() }) }}
                className={cn(
                  'rounded-lg border px-3 py-1.5 text-sm font-medium transition-colors',
                  custom?.from === daysAgo(r.days)
                    ? 'border-brand-500 bg-brand-50 text-brand-700'
                    : 'border-slate-200 bg-white text-slate-600 hover:bg-slate-50',
                )}
              >
                {r.label}
              </button>
            ))}
            <label className="flex flex-col gap-1 text-xs font-medium text-slate-500">
              Sanadan
              <input
                type="date" value={from}
                onChange={(e) => { setDay(''); setCustom({ from: e.target.value, to }) }}
                className="rounded-lg border border-slate-200 px-3 py-2 text-sm text-slate-700 outline-none focus:border-brand-400"
              />
            </label>
            <label className="flex flex-col gap-1 text-xs font-medium text-slate-500">
              Sanagacha
              <input
                type="date" value={to}
                onChange={(e) => { setDay(''); setCustom({ from, to: e.target.value }) }}
                className="rounded-lg border border-slate-200 px-3 py-2 text-sm text-slate-700 outline-none focus:border-brand-400"
              />
            </label>
          </div>
        </div>
      </Card>

      {error && <p className="rounded-lg bg-red-50 px-3 py-2 text-sm text-red-600">{error}</p>}

      {loading || !data ? (
        <Loader label="Yuklanmoqda..." />
      ) : (
        <>
          <div className="grid gap-3 sm:grid-cols-2 lg:grid-cols-4">
            <Stat label="Yangi talab" value={data.created} hint="Davrda ochilgan" />
            <Stat label="Bog'lanildi" value={data.reached} hint={`${data.attempts} urinishdan`} tone="emerald" />
            <Stat label="Hal bo'ldi" value={data.done} tone="emerald" />
            <Stat label="Bog'lanib bo'lmadi" value={data.failed} tone="rose" />
            <Stat label="Qayta qo'ng'iroqqa o'tkazildi" value={data.callback} tone="sky" />
            <Stat label="Hozir navbatda" value={data.openNow} hint="Joriy holat" />
            <Stat label="Muddati o'tgan" value={data.overdueNow} hint="Joriy holat" tone={data.overdueNow > 0 ? 'rose' : undefined} />
            <Stat
              label="Aloqa foizi"
              value={data.attempts > 0 ? `${Math.round((data.reached / data.attempts) * 100)}%` : '—'}
              hint="Gaplashilgan / urinish"
            />
            <Stat
              label="Javob yozilgan"
              value={data.withResponse ?? 0}
              hint={
                data.attempts > 0
                  ? `${Math.round(((data.withResponse ?? 0) / data.attempts) * 100)}% urinishda izoh bor`
                  : undefined
              }
            />
          </div>

          {/* AI TAHLIL — KPI kartochkalaridan keyin, jadvallardan OLDIN: u sahifaning
              "boshqaruvchi xulosasi" (voronka tahlilidagi bilan bir xil joylashuv qoidasi).
              Davr — yuqorida tanlangani; tahlil AYNAN shu davr uchun. */}
          <ContactAiPanel from={from} to={to} />

          {/* KUNLIK — "kunlik nechta odam bilan bog'lanildi" */}
          <Card title="Kunlik" sub="Har kuni nechta yangi talab ochilgan va nechta bog'lanish bo'lgan.">
            {data.daily.every((d) => d.created + d.attempts === 0) ? (
              <p className="py-6 text-center text-sm text-slate-400">Bu davrda amal bo'lmagan</p>
            ) : (
              <div className="overflow-x-auto">
                <table className="table">
                  <thead>
                    <tr>
                      <th>Kun</th>
                      <th className="num">Yangi talab</th>
                      <th className="num">Urinish</th>
                      <th className="num">Bog'lanildi</th>
                      <th className="num">Hal bo'ldi</th>
                      <th className="num">Qayta qo'ng'iroq</th>
                      <th className="num">Hal bo'lmadi</th>
                    </tr>
                  </thead>
                  <tbody>
                    {/* Bo'sh kunlar chiqarilmaydi — jadval uzayib ketmasin. */}
                    {data.daily.filter((d) => d.created + d.attempts > 0).reverse().map((d) => (
                      <tr key={d.date}>
                        <td>{formatDate(d.date)}</td>
                        <td className="num">{d.created || '—'}</td>
                        <td className="num">{d.attempts || '—'}</td>
                        <td className="num font-semibold text-emerald-600">{d.reached || '—'}</td>
                        <td className="num">{d.done || '—'}</td>
                        <td className="num">{d.callback || '—'}</td>
                        <td className="num">{d.failed || '—'}</td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            )}
          </Card>

          {/* KUNLIK JURNAL — kunning O'ZI: kimga, qachon, nima dedi (jadvaldan keyin, chunki
              jadval "nechta" ni, jurnal esa "nima bo'ldi" ni ko'rsatadi). */}
          <ContactDailyJournal from={from} to={to} />

          {/* XODIMLAR — "kim qaysi bosqichga oldi, natijasi qanday bo'ldi" */}
          <Card title="Xodimlar kesimi" sub="Kim nechta bog'lanish qildi va qaysi bosqichga o'tkazdi.">
            {data.byStaff.length === 0 ? (
              <p className="py-6 text-center text-sm text-slate-400">Ma'lumot yo'q</p>
            ) : (
              <div className="overflow-x-auto">
                <table className="table">
                  <thead>
                    <tr>
                      <th>Xodim</th>
                      <th className="num">Urinish</th>
                      <th className="num">Bog'lanildi</th>
                      <th className="num">Hal bo'ldi</th>
                      <th className="num">Qayta qo'ng'iroq</th>
                      <th className="num">Hal bo'lmadi</th>
                    </tr>
                  </thead>
                  <tbody>
                    {data.byStaff.map((s) => (
                      <tr key={s.actorName}>
                        <td className="font-medium text-slate-700">{s.actorName}</td>
                        <td className="num">{s.attempts}</td>
                        <td className="num font-semibold text-emerald-600">{s.reached}</td>
                        <td className="num">{s.done}</td>
                        <td className="num">{s.callback}</td>
                        <td className="num">{s.failed}</td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            )}
          </Card>

          <div className="grid gap-4 lg:grid-cols-2">
            <Card title="Sabablar" sub="Qaysi sabab bilan talab ochilgan va qanchasi hal bo'lgan.">
              {data.byReason.length === 0 ? (
                <p className="py-6 text-center text-sm text-slate-400">Ma'lumot yo'q</p>
              ) : (
                <table className="table">
                  <thead>
                    <tr>
                      <th>Sabab</th>
                      <th className="num">Ochilgan</th>
                      <th className="num">Hal</th>
                      <th className="num">Hal bo'lmadi</th>
                      <th className="num">Ochiq</th>
                    </tr>
                  </thead>
                  <tbody>
                    {data.byReason.map((r) => (
                      <tr key={r.reasonLabel}>
                        <td className="text-slate-700">{r.reasonLabel}</td>
                        <td className="num">{r.created}</td>
                        <td className="num text-emerald-600">{r.done}</td>
                        <td className="num text-rose-600">{r.failed}</td>
                        <td className="num">{r.open}</td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              )}
            </Card>

            <Card title="Qo'ng'iroq natijalari" sub="Ko'tarmagan/band ulushi — aloqa sifati ko'rsatkichi.">
              {data.byResult.length === 0 ? (
                <p className="py-6 text-center text-sm text-slate-400">Ma'lumot yo'q</p>
              ) : (
                <ul className="space-y-2">
                  {data.byResult.map((r) => {
                    const pct = data.attempts > 0 ? Math.round((r.count / data.attempts) * 100) : 0
                    return (
                      <li key={r.key}>
                        <div className="flex items-center justify-between text-sm">
                          <span className="text-slate-600">{r.label}</span>
                          <span className="font-semibold text-slate-700">
                            {r.count} <span className="text-xs font-normal text-slate-400">({pct}%)</span>
                          </span>
                        </div>
                        <div className="mt-1 h-1.5 w-full overflow-hidden rounded-full bg-slate-100">
                          <div className="h-full rounded-full bg-brand-500" style={{ width: `${pct}%` }} />
                        </div>
                      </li>
                    )
                  })}
                </ul>
              )}
            </Card>
          </div>

          {/* ==================== JAVOBLAR TAHLILI ====================
              Yuqoridagi jadvallar "NECHTA" ga javob beradi, bu bo'lim esa "NIMA deyilgan" ga. */}
          {(data.topWords?.length ?? 0) > 0 && (
            <Card
              title="Javoblarda eng ko'p uchragan so'zlar"
              sub="Bir javobda so'z necha marta yozilsa ham BIR marta sanaladi — savol «nechta javobda uchradi»."
            >
              <div className="flex flex-wrap gap-2">
                {data.topWords!.map((w) => {
                  const max = data.topWords![0].count || 1
                  const strength = Math.round((w.count / max) * 100)
                  return (
                    <button
                      key={w.word}
                      type="button"
                      // So'z bosilsa — o'sha so'z bo'yicha javoblar lentasi filtrlanadi
                      // ("nega bu so'z ko'p?" savoli darhol javob topsin).
                      onClick={() => { setTerm(w.word); setQ(w.word) }}
                      title={`«${w.word}» bo'yicha javoblarni ko'rish`}
                      className={cn(
                        'inline-flex items-center gap-1.5 rounded-full border px-3 py-1.5 text-sm transition-colors',
                        q === w.word
                          ? 'border-brand-500 bg-brand-50 text-brand-700'
                          : 'border-slate-200 bg-white text-slate-600 hover:border-slate-300 hover:bg-slate-50',
                      )}
                      style={q === w.word ? undefined : { opacity: 0.55 + strength / 250 }}
                    >
                      {w.word}
                      <span className="rounded-full bg-slate-100 px-1.5 text-xs font-semibold text-slate-500">
                        {w.count}
                      </span>
                    </button>
                  )
                })}
              </div>
            </Card>
          )}

          <Card
            title={
              <span className="inline-flex items-center gap-2">
                <MessageSquareText className="h-4 w-4 text-slate-400" />
                Javoblar lentasi
              </span>
            }
            sub="Har bir bog'lanishda NIMA deb yozilgani. Natija bo'yicha filtrlang yoki so'z bo'yicha qidiring."
            actions={
              <form
                className="flex gap-2"
                onSubmit={(e) => { e.preventDefault(); setQ(term.trim()) }}
              >
                <select
                  value={respResult}
                  onChange={(e) => setRespResult(e.target.value)}
                  className="rounded-lg border border-slate-200 bg-white px-3 py-2 text-sm text-slate-700 outline-none focus:border-brand-400"
                >
                  <option value="">Barcha natijalar</option>
                  {data.byResult.map((r) => (
                    <option key={r.key} value={r.key}>{r.label}</option>
                  ))}
                </select>
                <input
                  value={term}
                  onChange={(e) => setTerm(e.target.value)}
                  placeholder="Javob matnidan qidirish"
                  className="min-w-[170px] rounded-lg border border-slate-200 px-3 py-2 text-sm text-slate-700 outline-none focus:border-brand-400"
                />
                <Button type="submit" variant="secondary">
                  <Search className="h-4 w-4" />
                </Button>
              </form>
            }
          >
            {respLoading ? (
              <Loader label="Yuklanmoqda..." />
            ) : responses.length === 0 ? (
              <p className="py-8 text-center text-sm text-slate-400">
                {q || respResult
                  ? 'Bu filtrlarda javob topilmadi'
                  : "Bu davrda izoh yozilgan bog'lanish yo'q"}
              </p>
            ) : (
              <ul className="divide-y divide-slate-100">
                {responses.map((r) => (
                  <li key={r.id} className="py-2.5 first:pt-0 last:pb-0">
                    <div className="flex flex-wrap items-center gap-2">
                      <Link
                        to={`/admin/students/${r.studentId}`}
                        className="text-sm font-semibold text-slate-800 hover:text-brand-600 hover:underline"
                      >
                        {r.studentName || "Noma'lum"}
                      </Link>
                      <span className="rounded-md bg-slate-100 px-2 py-0.5 text-xs text-slate-600">
                        {r.resultLabel}
                      </span>
                      {r.nextStatusLabel && (
                        <span className="text-xs text-slate-400">→ {r.nextStatusLabel}</span>
                      )}
                      {r.reasonLabel && (
                        <span className="text-xs text-slate-400">· {r.reasonLabel}</span>
                      )}
                    </div>
                    <p className="mt-1 text-sm text-slate-700">{r.response}</p>
                    <p className="mt-0.5 text-xs text-slate-400">
                      {formatDateTime(r.createdAt)}
                      {r.actorName && ` · ${r.actorName}`}
                    </p>
                  </li>
                ))}
              </ul>
            )}
            {responses.length >= 200 && (
              <p className="mt-3 text-center text-xs text-slate-400">
                Eng so'nggi 200 ta javob ko'rsatildi — davrni toraytiring yoki qidiruvdan foydalaning.
              </p>
            )}
          </Card>
        </>
      )}
    </div>
  )
}

function Stat({
  label, value, hint, tone,
}: {
  label: string
  value: number | string
  hint?: string
  tone?: 'emerald' | 'rose' | 'sky'
}) {
  return (
    <div className="rounded-xl border border-slate-200 bg-white px-4 py-3">
      <p className="text-xs font-medium uppercase tracking-wide text-slate-400">{label}</p>
      <p
        className={cn(
          'mt-1 text-2xl font-bold',
          tone === 'emerald' ? 'text-emerald-600'
          : tone === 'rose' ? 'text-rose-600'
          : tone === 'sky' ? 'text-sky-600'
          : 'text-slate-800',
        )}
      >
        {value}
      </p>
      {hint && <p className="mt-0.5 text-xs text-slate-400">{hint}</p>}
    </div>
  )
}
