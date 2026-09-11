import { useCallback, useEffect, useMemo, useState } from 'react'
import {
  Bar, BarChart, CartesianGrid, Legend, Line, LineChart, ResponsiveContainer, Tooltip, XAxis, YAxis,
} from 'recharts'
import { apiErrorMessage } from '@/lib/utils'
import { getIgAnalytics, type IgAnalytics, type IgBreakdown } from '@/api/services/instagram'
import { Icon, MarketingPage, MkCard, MkEmpty, MkError, MkLoading, MkStat } from './mk'

/**
 * GRAFIK RANGLARI — TEKSHIRILGAN (`.claude/rules/course-analytics.md`).
 * Yashil/qizil juftlik ATAYIN olinmadi: deuteranopiyada ular deyarli ajralmaydi.
 */
const C_IN = '#0284c7'   // kelgan hodisalar
const C_OUT = '#e11d48'  // yuborilgan javoblar
const C_SINGLE = '#6366f1' // yakka seriya (lidlar)

const today = () => new Date().toISOString().slice(0, 10)

/** Bugundan N kun oldin ("yyyy-MM-dd"). */
function daysAgo(n: number): string {
  const d = new Date()
  d.setDate(d.getDate() - n)
  return d.toISOString().slice(0, 10)
}

/** "2026-08-12" → "12.08" (grafik o'qi uchun qisqa yorliq). */
const shortDay = (d: string) => (d?.length >= 10 ? `${d.slice(8, 10)}.${d.slice(5, 7)}` : d)

/** Niyat (intent) kalitlarining o'zbekcha yorliqlari. */
const INTENT_LABEL: Record<string, string> = {
  greeting: 'Salomlashish',
  price_question: 'Narx savoli',
  product_question: 'Xizmat/kurs savoli',
  buying_intent: 'Sotib olish niyati',
  complaint: 'Shikoyat',
  spam: 'Spam',
  other: 'Boshqa',
}

/** Til/yozuv kalitlari. */
const LANG_LABEL: Record<string, string> = {
  'uz-Latn': "O'zbek (lotin)",
  'uz-Cyrl': "O'zbek (kirill)",
  ru: 'Rus',
  en: 'Ingliz',
}

/** Kanal kalitlari. */
const CHANNEL_LABEL: Record<string, string> = {
  comment: 'Izoh',
  dm: 'Shaxsiy xabar',
  private_reply: 'Izohga shaxsiy javob',
}

/**
 * ANALITIKA — davr bo'yicha Instagram agentining ishi.
 *
 * Kunlik grafikda IKKI o'lchov (kelgan hodisa · yuborilgan javob) bitta y-o'qda ko'rsatiladi —
 * ikkalasi ham "dona", ya'ni taqqoslash halol. Lidlar ALOHIDA grafikda: turli miqyosdagi
 * o'lchovni ikki y-o'q bilan bitta grafikka tiqish ATAYIN qilinmagan (chalg'itadi).
 *
 * ⚠️ KO'RINISH: sahifa to'liq kenglikda ochiladi. Kesim kartochkalari `grid-cards` grid'ida —
 * qat'iy `1fr 1fr 1fr` ATAYIN olib tashlandi: u tor ekranda uchta kartani siqib, keng
 * ekranda esa ularni cho'zib yuborardi.
 */
export function InstagramAnalytics() {
  const [from, setFrom] = useState(() => daysAgo(29))
  const [to, setTo] = useState(today)
  const [data, setData] = useState<IgAnalytics | null>(null)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState('')

  const load = useCallback(() => {
    setLoading(true)
    setError('')
    getIgAnalytics(from, to)
      .then(setData)
      .catch((e) => setError(apiErrorMessage(e, "Analitikani yuklab bo'lmadi")))
      .finally(() => setLoading(false))
  }, [from, to])

  useEffect(load, [load])

  const chart = useMemo(
    () => (data?.daily ?? []).map((d) => ({
      name: shortDay(d.date),
      Kelgan: d.events,
      Javoblar: d.replies,
      Lidlar: d.leads,
      Qaynoq: d.hot,
    })),
    [data],
  )

  const quick = (days: number) => { setFrom(daysAgo(days - 1)); setTo(today()) }

  return (
    <MarketingPage
      title="Analitika"
      sub="Instagram agentining davr bo'yicha natijalari"
      actions={<button className="btn btn-ghost btn-sm" onClick={load}><Icon name="refresh" /> Yangilash</button>}
    >
      <div className="fade-up">
        {/* Davr tanlash */}
        <MkCard>
          <div style={{ display: 'flex', gap: 14, alignItems: 'flex-end', flexWrap: 'wrap' }}>
            <div style={{ minWidth: 150 }}>
              <label className="field-label">Boshlanishi</label>
              <input className="input" type="date" value={from} onChange={(e) => setFrom(e.target.value)} />
            </div>
            <div style={{ minWidth: 150 }}>
              <label className="field-label">Tugashi</label>
              <input className="input" type="date" value={to} onChange={(e) => setTo(e.target.value)} />
            </div>
            <div className="seg">
              <button onClick={() => quick(7)}>7 kun</button>
              <button onClick={() => quick(30)}>30 kun</button>
              <button onClick={() => quick(90)}>90 kun</button>
            </div>
          </div>
        </MkCard>

        {loading && <MkLoading />}
        {!loading && error && <MkError text={error} onRetry={load} />}

        {!loading && !error && data && (
          <>
            {/* Jamlanma */}
            <div className="mk-kpi" style={{ marginBottom: 22 }}>
              {([
                { label: 'Kelgan hodisalar', value: data.totals.events, tone: 'primary', icon: 'inbox' },
                { label: 'Yuborilgan javoblar', value: data.totals.replies, tone: 'muted', icon: 'send' },
                { label: 'Yaratilgan lidlar', value: data.totals.leads, tone: 'success', icon: 'user' },
                { label: 'Qaynoq lidlar', value: data.totals.hot, tone: 'warning', icon: 'fire' },
              ] as const).map((s) => (
                <MkStat key={s.label} label={s.label} value={s.value.toLocaleString()} tone={s.tone} icon={s.icon} />
              ))}
            </div>

            {/* Kunlik grafik: hodisa vs javob (bitta y-o'q, ikkalasi ham "dona") */}
            <MkCard title="Kunlik oqim" sub="Kelgan hodisalar va yuborilgan javoblar">
              {chart.length === 0
                ? <MkEmpty text="Bu davrda ma'lumot yo'q" />
                : (
                  <div style={{ width: '100%', height: 260 }}>
                    <ResponsiveContainer>
                      <BarChart data={chart} margin={{ top: 8, right: 8, left: -20, bottom: 0 }}>
                        <CartesianGrid strokeDasharray="3 3" vertical={false} />
                        <XAxis dataKey="name" tick={{ fontSize: 11 }} />
                        <YAxis tick={{ fontSize: 11 }} allowDecimals={false} />
                        <Tooltip />
                        <Legend />
                        <Bar dataKey="Kelgan" fill={C_IN} radius={[4, 4, 0, 0]} />
                        <Bar dataKey="Javoblar" fill={C_OUT} radius={[4, 4, 0, 0]} />
                      </BarChart>
                    </ResponsiveContainer>
                  </div>
                )}
            </MkCard>

            {/* Lidlar — ALOHIDA grafik (ikki y-o'q ishlatilmaydi) */}
            <MkCard title="Kunlik lidlar" sub="Instagram orqali voronkaga tushgan mijozlar">
              {chart.length === 0
                ? <MkEmpty text="Bu davrda lid yo'q" />
                : (
                  <div style={{ width: '100%', height: 220 }}>
                    <ResponsiveContainer>
                      <LineChart data={chart} margin={{ top: 8, right: 8, left: -20, bottom: 0 }}>
                        <CartesianGrid strokeDasharray="3 3" vertical={false} />
                        <XAxis dataKey="name" tick={{ fontSize: 11 }} />
                        <YAxis tick={{ fontSize: 11 }} allowDecimals={false} />
                        <Tooltip />
                        <Line type="monotone" dataKey="Lidlar" stroke={C_SINGLE} strokeWidth={2} dot={false} />
                      </LineChart>
                    </ResponsiveContainer>
                  </div>
                )}
            </MkCard>

            {/* Kesimlar — `grid-cards`: keng ekranda yonma-yon, tor ekranda o'zi ustma-ust */}
            <div className="grid-cards" style={{ marginBottom: 18 }}>
              <Breakdown title="Niyat bo'yicha" rows={data.byIntent} labels={INTENT_LABEL} />
              <Breakdown title="Til bo'yicha" rows={data.byLanguage} labels={LANG_LABEL} />
              <Breakdown title="Kanal bo'yicha" rows={data.byChannel} labels={CHANNEL_LABEL} />
            </div>

            {/* Top qoidalar */}
            <MkCard title="Eng ko'p ishlagan qoidalar" sub="AI'gacha javob bergan kalit so'z qoidalari">
              {data.topRules.length === 0
                ? <MkEmpty text="Qoida ishlamagan" hint="Kalit so'z qoidalari qo'shilsa javob tezroq va arzonroq bo'ladi." />
                : data.topRules.map((r, i) => (
                  <div className="feed-item" key={r.id} style={{ alignItems: 'center' }}>
                    <div className="rule-num" style={{ background: 'var(--primary-soft)', color: 'var(--primary)' }}>{i + 1}</div>
                    <div className="feed-body"><div style={{ fontWeight: 700, fontSize: 13.5 }}>{r.title}</div></div>
                    <div className="mk-num">{r.count.toLocaleString()}</div>
                  </div>
                ))}
            </MkCard>
          </>
        )}
      </div>
    </MarketingPage>
  )
}

/** Kesim kartochkasi: kalit → soni + ulush chizig'i. */
function Breakdown({
  title, rows, labels,
}: {
  title: string
  rows: IgBreakdown[]
  labels: Record<string, string>
}) {
  const total = rows.reduce((s, r) => s + r.count, 0)
  return (
    <MkCard title={title}>
      {rows.length === 0
        ? <MkEmpty text="Ma'lumot yo'q" />
        : rows.map((r) => {
          const pct = total > 0 ? Math.round((r.count / total) * 100) : 0
          return (
            <div className="metric-row" key={r.key} style={{ gap: 10 }}>
              <div style={{ flex: 1, minWidth: 0 }}>
                <div style={{ fontSize: 13, fontWeight: 600 }}>{labels[r.key] ?? r.key ?? '—'}</div>
                <div className="progress-track" style={{ marginTop: 6 }}>
                  <div className="progress-fill" style={{ width: `${pct}%`, background: C_IN }} />
                </div>
              </div>
              <div style={{ textAlign: 'right', minWidth: 62 }}>
                <div className="mk-num">{r.count.toLocaleString()}</div>
                <div className="feed-time">{pct}%</div>
              </div>
            </div>
          )
        })}
    </MkCard>
  )
}
