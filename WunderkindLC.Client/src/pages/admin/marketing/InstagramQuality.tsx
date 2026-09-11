import { useCallback, useEffect, useState } from 'react'
import { apiErrorMessage } from '@/lib/utils'
import {
  getIgQuality, type IgQuality, type IgQualityIntent, type IgQualityPair,
} from '@/api/services/instagramQuality'
import { Icon, MarketingPage, MkCard, MkEmpty, MkError, MkLoading, MkStat } from './mk'

/** Yakka seriya rangi — `course-analytics.md`: yashil/qizil juftlik ISHLATILMAYDI. */
const C_BAR = '#0284c7'

/** Niyat (intent) kalitlarining o'zbekcha yorliqlari — analitika sahifasi bilan bir xil. */
const INTENT_LABEL: Record<string, string> = {
  greeting: 'Salomlashish',
  price_question: 'Narx savoli',
  product_question: 'Xizmat/kurs savoli',
  buying_intent: 'Sotib olish niyati',
  complaint: 'Shikoyat',
  spam: 'Spam',
  other: 'Boshqa',
}

/** Kanal kalitlari. */
const CHANNEL_LABEL: Record<string, string> = {
  comment: 'Izoh',
  dm: 'Shaxsiy xabar',
  private_reply: 'Izohga shaxsiy javob',
}

/** Lentada matn shuncha belgidan uzun bo'lsa qisqartiriladi (bosilganda to'liq ochiladi). */
const SNIP = 260

const today = () => new Date().toISOString().slice(0, 10)

/** Bugundan N kun oldin ("yyyy-MM-dd"). */
function daysAgo(n: number): string {
  const d = new Date()
  d.setDate(d.getDate() - n)
  return d.toISOString().slice(0, 10)
}

/**
 * Tuzatish DARAJASI — o'xshashlik foizidan.
 *
 * ⚠️ RANG YAGONA KANAL EMAS: har darajaning YORLIG'I va IKONKASI bor. Rang ko'rmaydigan
 * (yoki chop etilgan hisobotni o'qiyotgan) odam ham "bu javob butunlay qayta yozilgan"ligini
 * bilishi kerak.
 */
function editLevel(p: IgQualityPair): { label: string; icon: string; color: string; bg: string } {
  if (!p.wasEdited) {
    return { label: 'Aynan qabul qilingan', icon: 'check', color: 'var(--success)', bg: 'var(--success-soft)' }
  }
  if (p.similarity < 40) {
    return { label: 'Butunlay qayta yozilgan', icon: 'warn', color: 'var(--danger)', bg: 'var(--danger-soft)' }
  }
  if (p.similarity < 80) {
    return { label: "Ko'p tuzatilgan", icon: 'edit', color: 'var(--warning)', bg: 'var(--warning-soft)' }
  }
  return { label: 'Kichik tuzatish', icon: 'edit', color: 'var(--primary)', bg: 'var(--primary-soft)' }
}

/**
 * JAVOB SIFATI JURNALI — «AI shunday dedi → operator shunday yozdi».
 *
 * <b>Nima uchun kerak:</b> promptni va bilim bazasini yaxshirishning eng ishonchli manbai —
 * odamning AI javobiga kiritgan TUZATISHI. "Nechta javob ketdi" degan sonlar analitikada bor,
 * bu ekranda esa MAZMUN: qaysi niyatda AI ko'proq yanglishadi va matn qanday o'zgartiriladi.
 * Shu sababdan sahifaning asosiy qiymati — <b>niyat kesimi</b>: u aynan promptning qaysi
 * qismi zaif ekanini ko'rsatadi.
 *
 * 🔴 <b>MAXFIYLIK:</b> ekranda mijozning HECH QANDAY belgisi yo'q — na ismi, na Instagram
 * ID'si, na telefoni, na mijoz yozgan matn. Backend ularni ataylab qaytarmaydi va UI ham
 * so'ramaydi. Bu ICHKI SIFAT ma'lumoti; "kim bilan yozishilgani" savolining joyi — Inbox.
 *
 * ⚠️ Filtrlarning QAMROVI serverda ataylab har xil (`InstagramController.Quality.cs`):
 * davr va kanal — hammasiga, niyat — jamlanma va lentaga (kesimga emas, chunki kesim ayni
 * paytda tanlagich), "faqat tahrirlanganlar" — faqat lentaga. Shuning uchun uning tugmasi
 * ham filtrlar kartasida emas, LENTA sarlavhasida turadi.
 *
 * ⚠️ KO'RINISH: sahifa to'liq kenglikda ochiladi — niyat jadvali gorizontal skroll
 * (`mk-scroll-x`) ichida, ya'ni sahifa tanasi hech qachon yon tomonga siljimaydi.
 */
export function InstagramQuality() {
  const [data, setData] = useState<IgQuality | null>(null)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState('')

  const [from, setFrom] = useState(daysAgo(29))
  const [to, setTo] = useState(today())
  const [intent, setIntent] = useState('')
  const [channel, setChannel] = useState('')
  const [onlyEdited, setOnlyEdited] = useState(false)
  const [limit, setLimit] = useState(50)

  const load = useCallback(() => {
    setLoading(true)
    setError('')
    getIgQuality({ from, to, intent, channel, onlyEdited, limit })
      .then(setData)
      .catch((e) => setError(apiErrorMessage(e, "Javob sifati jurnalini yuklab bo'lmadi")))
      .finally(() => setLoading(false))
  }, [from, to, intent, channel, onlyEdited, limit])

  useEffect(load, [load])

  /** Filtr o'zgarsa lenta uzunligi boshiga qaytadi — aks holda tanlov 200 talik ro'yxatga tushardi. */
  const patch = (fn: () => void) => { fn(); setLimit(50) }

  const setRange = (days: number) => patch(() => { setFrom(daysAgo(days - 1)); setTo(today()) })

  return (
    <MarketingPage
      title="Javob sifati"
      sub="«AI shunday dedi → operator shunday yozdi» — promptni qayerda tuzatish kerakligi"
      actions={<button className="btn btn-ghost btn-sm" onClick={load}><Icon name="refresh" /> Yangilash</button>}
    >
      <div className="fade-up">
        {/* ── Filtrlar (davr · kanal · niyat) ─────────────────────────────── */}
        <MkCard>
          <div style={{ display: 'flex', gap: 14, alignItems: 'flex-end', flexWrap: 'wrap' }}>
            <div style={{ minWidth: 150 }}>
              <label className="field-label">Boshlanishi</label>
              <input className="input" type="date" value={from} onChange={(e) => patch(() => setFrom(e.target.value))} />
            </div>
            <div style={{ minWidth: 150 }}>
              <label className="field-label">Tugashi</label>
              <input className="input" type="date" value={to} onChange={(e) => patch(() => setTo(e.target.value))} />
            </div>
            <div className="seg">
              {([[7, '7 kun'], [30, '30 kun'], [90, '90 kun']] as const).map(([d, l]) => (
                <button
                  key={d}
                  className={from === daysAgo(d - 1) && to === today() ? 'active' : ''}
                  onClick={() => setRange(d)}
                >
                  {l}
                </button>
              ))}
            </div>
            <div style={{ minWidth: 190 }}>
              <label className="field-label">Niyat</label>
              <select className="input" value={intent} onChange={(e) => patch(() => setIntent(e.target.value))}>
                <option value="">Barcha niyatlar</option>
                {Object.entries(INTENT_LABEL).map(([k, l]) => <option key={k} value={k}>{l}</option>)}
              </select>
            </div>
            <div style={{ minWidth: 190 }}>
              <label className="field-label">Kanal</label>
              <select className="input" value={channel} onChange={(e) => patch(() => setChannel(e.target.value))}>
                <option value="">Barcha kanallar</option>
                {Object.entries(CHANNEL_LABEL).map(([k, l]) => <option key={k} value={k}>{l}</option>)}
              </select>
            </div>
          </div>
        </MkCard>

        {loading && <MkLoading />}
        {!loading && error && <MkError text={error} onRetry={load} />}

        {!loading && !error && data && (
          <>
            {data.truncated && (
              <div className="mk-alert">
                <Icon name="warn" style={{ width: 18, height: 18, flexShrink: 0 }} />
                <div>
                  <div className="mk-alert-title">Davr juda katta</div>
                  Davr ichida javoblar server chegarasidan (2000) oshdi — jamlanma ham, lenta ham
                  faqat ENG YANGI 2000 tasidan hisoblandi. Aniq manzara uchun davrni qisqartiring.
                </div>
              </div>
            )}

            {/* ⚠️ Niyat tanlangan bo'lsa JAMLANMA ham o'sha niyat bo'yicha — buni ochiq
                yozamiz, aks holda "sonlar nega kamaydi" savoli javobsiz qolardi. */}
            {intent && (
              <div
                style={{ display: 'flex', alignItems: 'center', gap: 10, marginBottom: 14, flexWrap: 'wrap' }}
              >
                <span className="badge badge-ai">
                  <Icon name="filter" style={{ width: 12, height: 12 }} />
                  Niyat: {INTENT_LABEL[intent] ?? intent}
                </span>
                <span className="field-hint" style={{ marginTop: 0 }}>
                  Jamlanma va ro'yxat shu niyat bo'yicha. Niyat kesimi esa butun davrni
                  ko'rsatishda davom etadi — u ayni paytda tanlagich.
                </span>
                <button className="btn btn-ghost btn-sm" onClick={() => patch(() => setIntent(''))}>
                  <Icon name="close" /> Filtrni olib tashlash
                </button>
              </div>
            )}

            {/* ── KPI ──────────────────────────────────────────────────────── */}
            <div className="mk-kpi" style={{ marginBottom: 22 }}>
              <MkStat
                value={data.total.toLocaleString()}
                label="Jami taklif"
                tone="primary"
                icon="ai"
                hint="AI matn yozgan javoblar"
              />
              <MkStat
                value={data.edited.toLocaleString()}
                label="Tahrirlangan"
                tone="warning"
                icon="edit"
                hint={`${data.kept.toLocaleString()} tasi aynan qabul qilingan`}
              />
              <MkStat
                value={`${data.editedPercent}%`}
                label="Tahrir ulushi"
                icon="gauge"
                hint="Qanchasiga odam qo'l urdi"
              />
              <MkStat
                value={data.edited > 0 ? `${data.avgSimilarity}%` : '—'}
                label="O'rtacha o'xshashlik"
                icon="sliders"
                hint="Faqat TAHRIRLANGANLAR bo'yicha — 100% ga yaqin bo'lsa tuzatish kichik"
              />
            </div>

            {/* ── Niyat kesimi — ekranning asosiy qiymati ──────────────────── */}
            <IntentTable
              rows={data.byIntent}
              selected={intent}
              onSelect={(k) => patch(() => setIntent(k === intent ? '' : k))}
            />

            {/* ── Lenta ───────────────────────────────────────────────────── */}
            <MkCard
              title="AI taklifi → operator javobi"
              sub="Eng yangisi tepada"
              actions={(
                <div className="seg">
                  {([[false, 'Hammasi'], [true, 'Faqat tahrirlanganlar']] as const).map(([v, l]) => (
                    <button key={String(v)} className={onlyEdited === v ? 'active' : ''} onClick={() => patch(() => setOnlyEdited(v))}>
                      {l}
                    </button>
                  ))}
                </div>
              )}
            >
              <div className="field-hint" style={{ marginTop: 0, marginBottom: 12 }}>
                Bu tugma FAQAT ro'yxatga ta'sir qiladi: yuqoridagi jamlanma ham, niyat kesimi ham
                undan o'zgarmaydi — aks holda «tahrir ulushi» doim 100% bo'lib qolardi.
              </div>

              {data.items.length === 0
                ? (
                  <MkEmpty
                    text="Hozircha ma'lumot yo'q"
                    hint="Operator AI javobini tahrirlaganda shu yerda ko'rinadi. Davr yoki filtrlarni kengaytirib ham ko'ring."
                  />
                )
                : (
                  <>
                    {data.items.map((p) => <PairRow key={p.id} pair={p} />)}

                    <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'center', gap: 12, marginTop: 14 }}>
                      <span className="feed-time">
                        {data.itemsTotal.toLocaleString()} tadan {data.items.length.toLocaleString()} tasi ko'rsatilyapti
                      </span>
                      {data.items.length < data.itemsTotal && limit < 200 && (
                        <button className="btn btn-outline btn-sm" onClick={() => setLimit(limit >= 100 ? 200 : 100)}>
                          <Icon name="chevDown" /> Ko'proq ko'rsatish
                        </button>
                      )}
                    </div>
                  </>
                )}
            </MkCard>
          </>
        )}
      </div>
    </MarketingPage>
  )
}

/**
 * NIYAT KESIMI — "AI qaysi mavzuda ko'proq yanglishadi".
 *
 * Server tartibi (eng ko'p TAHRIRLANGAN tepada) SAQLANADI — bu yerda qayta saralanmaydi,
 * aks holda hisobotning savoli ("qayerda yanglishadi") "qaysi niyat ko'p uchraydi"ga
 * almashib qolardi.
 *
 * Qator bosilsa lenta o'sha niyat bo'yicha filtrlanadi (qayta bosilsa — tozalanadi).
 */
function IntentTable({
  rows, selected, onSelect,
}: {
  rows: IgQualityIntent[]
  selected: string
  onSelect: (key: string) => void
}) {
  return (
    <MkCard
      title="Niyat bo'yicha"
      sub="AI eng ko'p tuzatiladigan mavzu tepada — promptning aynan shu qismi zaif. Qator bosilsa lenta o'sha niyat bo'yicha filtrlanadi."
    >
      {rows.length === 0
        ? <MkEmpty text="Ma'lumot yo'q" />
        : (
          <div className="mk-scroll-x">
            <table className="mk-table" style={{ minWidth: 720 }}>
              <thead>
                <tr>
                  <th>Niyat</th>
                  <th className="mk-num">Jami</th>
                  <th className="mk-num">Tahrirlangan</th>
                  <th style={{ minWidth: 160 }}>Tahrir ulushi</th>
                  <th className="mk-num">O'rtacha o'xshashlik</th>
                </tr>
              </thead>
              <tbody>
                {rows.map((r, i) => {
                  const pct = r.total > 0 ? Math.round((r.edited / r.total) * 100) : 0
                  const on = selected === r.intent
                  return (
                    <tr
                      key={r.intent}
                      tabIndex={0}
                      onClick={() => onSelect(r.intent)}
                      onKeyDown={(e) => { if (e.key === 'Enter' || e.key === ' ') { e.preventDefault(); onSelect(r.intent) } }}
                      style={{ cursor: 'pointer', background: on ? 'var(--primary-soft)' : undefined }}
                    >
                      <td>
                        <span style={{ fontWeight: 700 }}>{INTENT_LABEL[r.intent] ?? r.intent}</span>
                        {i === 0 && r.edited > 0 && (
                          <span className="badge badge-warning" style={{ marginLeft: 8 }}>
                            Eng ko'p tuzatiladi
                          </span>
                        )}
                        {on && <span className="badge badge-ai" style={{ marginLeft: 8 }}>Filtrda</span>}
                      </td>
                      <td className="mk-num">{r.total.toLocaleString()}</td>
                      <td className="mk-num">{r.edited.toLocaleString()}</td>
                      <td>
                        <div style={{ display: 'flex', alignItems: 'center', gap: 10 }}>
                          <div className="progress-track">
                            <div className="progress-fill" style={{ width: `${pct}%`, background: C_BAR }} />
                          </div>
                          <span className="feed-time" style={{ minWidth: 34, textAlign: 'right' }}>{pct}%</span>
                        </div>
                      </td>
                      {/* ⚠️ O'rtacha o'xshashlik faqat tahrirlanganlar bo'yicha — tahrir bo'lmasa "—". */}
                      <td className="mk-num">{r.edited > 0 ? `${r.avgSimilarity}%` : '—'}</td>
                    </tr>
                  )
                })}
              </tbody>
            </table>
          </div>
        )}
    </MkCard>
  )
}

/**
 * Bitta juftlik: chapda AI taklifi, o'ngda operator yuborgani.
 *
 * 🔴 `mk-cols2` bu yerda ATAYIN ISHLATILMAYDI. U `minmax(430px, 1fr)` bo'lgani uchun
 * ikkita ustun sig'ishi uchun ~876px kerak — ya'ni noutbukdagi odatiy 1152px va undan tor
 * ekranlarda taklif bilan javob USTMA-UST tushib qolardi. Sahifaning butun ma'nosi esa
 * ikkovini YONMA-YON solishtirish; ustma-ust tushgan ikki matnni taqqoslab bo'lmaydi.
 *
 * Shuning uchun grid AYNAN shu yerda, inline yoziladi: `minmax(300px, 1fr)` bilan ustunlar
 * ~616px dan keng joyda yonma-yon turadi, telefonda esa tabiiy ravishda bittaga tushadi.
 */
function PairRow({ pair }: { pair: IgQualityPair }) {
  const [open, setOpen] = useState(false)
  const lvl = editLevel(pair)
  const long = pair.aiText.length > SNIP || pair.sentText.length > SNIP
  const cut = (t: string) => (open || t.length <= SNIP ? t : `${t.slice(0, SNIP)}…`)

  return (
    <div className="feed-item" style={{ flexDirection: 'column', gap: 8, alignItems: 'stretch' }}>
      <div style={{ display: 'flex', alignItems: 'center', gap: 8, flexWrap: 'wrap' }}>
        {/* Daraja — RANG + YORLIQ + IKONKA (rang yagona kanal emas). */}
        <span className="badge" style={{ background: lvl.bg, color: lvl.color }}>
          <Icon name={lvl.icon} style={{ width: 12, height: 12 }} />
          {lvl.label}
        </span>
        <span className="match-pill">{pair.similarity}% o'xshash</span>
        <span className="match-pill">{CHANNEL_LABEL[pair.channel] ?? pair.channel}</span>
        <span className="match-pill">{INTENT_LABEL[pair.intent] ?? pair.intent}</span>
        <span style={{ flex: 1 }} />
        <span className="feed-time">
          {pair.actorName || 'Xodim'} · {(pair.createdAt || '').replace('T', ' ').slice(0, 16)}
        </span>
      </div>

      {/* ⚠️ Kenglik chegarasi 300px — `mk-cols2` (430px) bu yerda juda keng edi. */}
      <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(300px, 1fr))', gap: 16 }}>
        <TextBox label="AI taklif qilgan" text={cut(pair.aiText)} muted />
        <TextBox label="Operator yuborgan" text={cut(pair.sentText)} />
      </div>

      {long && (
        <button className="link-btn" onClick={() => setOpen(!open)} style={{ alignSelf: 'flex-start' }}>
          <Icon name={open ? 'chevUp' : 'chevDown'} style={{ width: 14, height: 14 }} />
          {open ? "Qisqartirish" : "To'liq matn"}
        </button>
      )}
    </div>
  )
}

/** Juftlikning bitta tomoni. `muted` — AI matni (u taklif, yakuniy javob emas). */
function TextBox({ label, text, muted }: { label: string; text: string; muted?: boolean }) {
  return (
    <div
      className="flow-step"
      style={muted ? { background: 'var(--surface-2)' } : { background: 'var(--surface)' }}
    >
      <div className="flow-step-label">
        <Icon name={muted ? 'ai' : 'user'} style={{ width: 13, height: 13 }} />
        {label}
      </div>
      <div className="feed-text" style={{ whiteSpace: 'pre-wrap', wordBreak: 'break-word' }}>
        {text || <span className="feed-time">— matn yo'q —</span>}
      </div>
    </div>
  )
}
