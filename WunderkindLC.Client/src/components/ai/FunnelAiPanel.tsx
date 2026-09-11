import { useEffect, useMemo, useState } from 'react'
import {
  AlertCircle,
  AlertTriangle,
  CheckCircle2,
  FileDown,
  GitCompare,
  History,
  Info,
  Lightbulb,
  RefreshCw,
  Sparkles,
} from 'lucide-react'
import {
  getFunnelAiAnalyses,
  runFunnelAiAnalysis,
  type FunnelAiKind,
  type FunnelAiRecord,
  type FunnelAiScores,
} from '@/api/services/funnelAi'
import { Card } from '@/components/ui/Card'
import { Button } from '@/components/ui/Button'
import {
  AiErrorBox, AiRadar, CardList, RankedBars, ScoreGrid, ScoreRing, TextBlock,
} from '@/components/ai/AiParts'
import { escapeHtml, openPrintWindow, printCss, scoreColor, trendInfo } from '@/lib/ai'
import { usePerm } from '@/lib/permissions'
import { apiErrorMessage, formatDate, formatMoney } from '@/lib/utils'

/**
 * VORONKA AI TAHLILI — "Formalar" bo'limining IKKALA statistika sahifasi uchun yagona panel:
 * lid formalari (`kind="lead-forms"`) va daraja testlari (`kind="level-tests"`).
 *
 * <p>Ikki nusxa ATAYIN yozilmagan: savol ham, voronka ham (ochildi → ariza → lid → o'quvchi →
 * to'ladi), AI shartnomasi ham bir xil — farq faqat YORLIQLARDA, ular esa bitta xaritada
 * (`texts`). Umumiy qismlar `components/ai/AiParts` va `lib/ai` dan olinadi (nusxa yo'q).</p>
 *
 * <p>Panel sahifada KPI kartochkalaridan keyin, birinchi grafikdan oldin turadi — AI xulosasi
 * sahifaning "boshqaruvchi xulosasi": pastdagi jadvallarni o'qishdan oldin nima muhimligini
 * aytadi.</p>
 */

/** Sohaviy baholar — radar va ball kartochkalari uchun yagona tartib. */
const dimLabels: { key: keyof FunnelAiScores; label: string }[] = [
  { key: 'hajm', label: 'Hajm' },
  { key: 'konversiya', label: 'Konversiya' },
  { key: 'sotuv', label: 'Sotuv' },
  { key: 'barqarorlik', label: 'Barqarorlik' },
]

/** Turga bog'liq BARCHA matnlar — komponent bo'ylab `if (kind === ...)` sochilmasin. */
interface KindText {
  /** PDF sarlavhasidagi brend qatori. */
  brand: string
  /** Panel sarlavhasi ostidagi izoh. */
  sub: string
  /** Voronkaning birinchi qadami: "Ochilgan" ↔ "Yuborilgan havolalar". */
  views: string
  /** Ikkinchi qadam: "Ariza" ↔ "Topshirdi". */
  submissions: string
  /** Manba birligi: "Formalar" ↔ "Testlar". */
  sources: string
  /** Reyting kartochkasi sarlavhasi. */
  channels: string
  channelsSub: string
  channelsEmpty: string
  /** AI narrativining kanal bo'limi sarlavhasi. */
  narrativeChannels: string
  /** Tahlil hali yo'q bo'lgandagi tushuntirish. */
  intro: string
  /** Tahlil ketayotgandagi matn. */
  running: string
  /** Bugun allaqachon tahlil qilingani haqidagi eslatma. */
  blocked: string
  /**
   * Bo'lim ruxsati. Tahlil YARATISH shu bo'limning "create" amaliga bog'langan — serverda ham
   * shunday (`AdminPermAttribute` yozish amalini `PermissionRules.CanWrite` bilan tekshiradi).
   * ⚠️ Tugma shu bilan darvozalanadi: aks holda faqat KO'RISH ruxsati bor xodim tugmani bosib
   * 403 olardi (va Gemini chaqiruvi — pul — baribir urinilardi).
   */
  perm: string
}

const texts: Record<FunnelAiKind, KindText> = {
  'lead-forms': {
    brand: 'WunderkindLC · Lid formalari AI tahlili',
    sub: "Kanallar, voronka va sotuv bo'yicha AI xulosasi — kuniga bir marta",
    views: 'Ochilgan',
    submissions: 'Ariza',
    sources: 'Formalar',
    channels: 'Eng samarali kanallar',
    channelsSub: "Qaysi forma haqiqiy natija berdi — AI shu tartibga tayanib yozadi",
    channelsEmpty: "Hali forma bo'yicha ma'lumot yo'q.",
    narrativeChannels: 'Kanallar va formalar',
    intro: "Formalar voronkasi hali AI orqali tahlil qilinmagan. Tugmani bosing — AI qaysi ijtimoiy tarmoq haqiqiy o'quvchi va PUL keltirayotganini, voronka qayerda uzilayotganini va nimani tuzatish kerakligini yozib beradi.",
    running: "AI kanallar voronkasini tahlil qilmoqda...",
    blocked: "Bugun allaqachon tahlil qilingan. Keyingi tahlil ertaga mumkin (eski tahlillar saqlanib qoladi).",
    perm: 'leads',
  },
  'level-tests': {
    brand: 'WunderkindLC · Daraja testlari AI tahlili',
    sub: "Testlar, voronka va sotuv bo'yicha AI xulosasi — kuniga bir marta",
    views: 'Yuborilgan havolalar',
    submissions: 'Topshirdi',
    sources: 'Testlar',
    channels: 'Eng samarali testlar',
    channelsSub: "Qaysi test haqiqiy natija berdi — AI shu tartibga tayanib yozadi",
    channelsEmpty: "Hali test bo'yicha ma'lumot yo'q.",
    narrativeChannels: 'Testlar kesimi',
    intro: "Daraja testlari voronkasi hali AI orqali tahlil qilinmagan. Tugmani bosing — AI qaysi test haqiqiy o'quvchi va PUL keltirayotganini, voronka qayerda uzilayotganini va nimani tuzatish kerakligini yozib beradi.",
    running: "AI testlar voronkasini tahlil qilmoqda...",
    blocked: "Bugun allaqachon tahlil qilingan. Keyingi tahlil ertaga mumkin (eski tahlillar saqlanib qoladi).",
    perm: 'schedule',
  },
}

/** Chop etish (PDF) uchun HTML — `lib/ai` dagi umumiy uslub bilan. */
function buildPrintHtml(rec: FunnelAiRecord, t: KindText): string {
  const r = rec.ai
  const m = rec.metrics
  const b = r.baholar
  const li = (arr: string[]) => arr.map((x) => `<li>${escapeHtml(x)}</li>`).join('')
  const row = (label: string, v: string | number) =>
    `<tr><td>${escapeHtml(label)}</td><td style="text-align:right;font-weight:bold">${v}</td></tr>`
  return `<!DOCTYPE html><html lang="uz"><head><meta charset="utf-8"><title>${escapeHtml(t.brand)} — ${escapeHtml(rec.date)}</title>
<style>${printCss}</style></head><body>
  <div class="head"><div class="brand">${escapeHtml(t.brand)}</div>
    <h1>Voronka tahlili</h1>
    <div class="meta">Sana: ${escapeHtml(rec.date)} · Model: ${escapeHtml(rec.model)} · Umumiy baho: <b>${b.umumiy}/100</b> · Trend: ${escapeHtml(r.trend)}</div>
  </div>
  <h2>Baholar</h2>
  <table>${row('Hajm', b.hajm)}${row('Konversiya', b.konversiya)}${row('Sotuv', b.sotuv)}${row('Barqarorlik', b.barqarorlik)}${row('Umumiy', b.umumiy)}</table>
  <h2>Asosiy raqamlar</h2>
  <table>${row(t.sources, `${m.sources} (${m.activeSources} faol)`)}${row(t.views, m.views)}${row(t.submissions, m.submissions)}${row('Lid (takrorsiz)', m.leads)}${row("O'quvchiga aylandi", m.converted)}${row("Aktiv o'quvchi", m.activeStudents)}${row("To'lov qildi", m.paid)}${row('Tushum', formatMoney(m.revenue))}${row(`${t.submissions} %`, m.submitRate + '%')}${row("O'quvchi %", m.convertRate + '%')}${row('Sotuv %', m.payRate + '%')}</table>
  ${r.umumiy ? `<h2>Umumiy holat</h2><p>${escapeHtml(r.umumiy)}</p>` : ''}
  ${r.ozgarishlar ? `<h2>Oldingi tahlilga nisbatan o'zgarishlar</h2><p>${escapeHtml(r.ozgarishlar)}</p>` : ''}
  ${r.kanallar ? `<h2>${escapeHtml(t.narrativeChannels)}</h2><p>${escapeHtml(r.kanallar)}</p>` : ''}
  ${r.voronka ? `<h2>Voronka</h2><p>${escapeHtml(r.voronka)}</p>` : ''}
  ${r.sifat ? `<h2>Lid sifati</h2><p>${escapeHtml(r.sifat)}</p>` : ''}
  ${r.pul ? `<h2>Pul</h2><p>${escapeHtml(r.pul)}</p>` : ''}
  ${m.channels.length ? `<h2>${escapeHtml(t.channels)}</h2><ul>${li(m.channels.map((c) => `${c.name}${c.source ? ` (${c.source})` : ''} — ${t.submissions.toLowerCase()}: ${c.submissions}, lid: ${c.leads}, to'ladi: ${c.paid} (${c.payRate}%), tushum: ${formatMoney(c.revenue)}`))}</ul>` : ''}
  ${m.stages.length ? `<h2>Lidlar qaysi bosqichda</h2><ul>${li(m.stages.map((s) => `${s.stage} — ${s.leads}`))}</ul>` : ''}
  ${r.kuchli.length ? `<h2>Kuchli tomonlari</h2><ul>${li(r.kuchli)}</ul>` : ''}
  ${r.zaif.length ? `<h2>Zaif tomonlari</h2><ul>${li(r.zaif)}</ul>` : ''}
  ${r.xavflar.length ? `<h2>Xavflar</h2><ul>${li(r.xavflar)}</ul>` : ''}
  ${r.tavsiyalar.length ? `<h2>Tavsiyalar</h2><ul>${li(r.tavsiyalar)}</ul>` : ''}
  <div class="foot">Ushbu tahlil sun'iy intellekt (${escapeHtml(rec.model)}) tomonidan ${escapeHtml(rec.date)} holatidagi raqamlar asosida yaratilgan. Yakuniy qarorlar jonli kuzatuv bilan birga ko'rib chiqilsin.</div>
  <script>window.onload=function(){setTimeout(function(){window.print()},250)}</script>
</body></html>`
}

export function FunnelAiPanel({ kind }: { kind: FunnelAiKind }) {
  const t = texts[kind]
  const { can } = usePerm()
  // Yaratish — bo'limning "create" amali (serverdagi qoida bilan bir xil). Ko'rish ruxsati bor,
  // yozish ruxsati yo'q xodim tahlillarni O'QIYDI, lekin yangisini boshlay olmaydi.
  const canRun = can(t.perm, 'create')

  const [records, setRecords] = useState<FunnelAiRecord[]>([])
  const [loading, setLoading] = useState(true)
  const [loadError, setLoadError] = useState<string | null>(null)

  const [selectedId, setSelectedId] = useState<string | null>(null)
  const [showHistory, setShowHistory] = useState(false)
  const [running, setRunning] = useState(false)
  const [runError, setRunError] = useState<string | null>(null)
  const [info, setInfo] = useState<string | null>(null)

  // "Bugun" — markaz vaqti bo'yicha (server ham shu kunni hisoblaydi).
  const todayTk = useMemo(
    () => new Date().toLocaleDateString('en-CA', { timeZone: 'Asia/Tashkent' }),
    [],
  )

  useEffect(() => {
    let alive = true
    // Boshqa turga o'tilganda eski tahlil ko'rinib qolmasin (maqsadli, bir marta).
    // eslint-disable-next-line react-hooks/set-state-in-effect
    setLoading(true)
    getFunnelAiAnalyses(kind)
      .then((recs) => {
        if (!alive) return
        setRecords(recs)
        setSelectedId(recs[0]?.id ?? null)
      })
      .catch((e) => alive && setLoadError(apiErrorMessage(e, "Tahlillarni yuklab bo'lmadi")))
      .finally(() => alive && setLoading(false))
    return () => {
      alive = false
    }
  }, [kind])

  const shown = records.find((r) => r.id === selectedId) ?? records[0] ?? null
  const blockedToday = records.some((r) => r.date === todayTk)

  const generate = () => {
    setRunning(true)
    setRunError(null)
    setInfo(null)
    runFunnelAiAnalysis(kind)
      .then((r) => {
        if (r.ok && r.record) {
          const rec = r.record
          setRecords((prev) => [rec, ...prev.filter((x) => x.id !== rec.id)])
          setSelectedId(rec.id)
          // ⚠️ Bu XATO EMAS: server bugungi tayyor tahlilni qaytardi (Gemini chaqirilmadi).
          if (r.alreadyToday) setInfo("Bugungi tahlil allaqachon tayyor — o'sha ko'rsatildi. Keyingi tahlil ertaga.")
        } else {
          setRunError(r.error || "Tahlil qilib bo'lmadi.")
        }
      })
      .catch((e) => setRunError(apiErrorMessage(e, "Tahlil qilib bo'lmadi. Internet yoki API kalitini tekshiring.")))
      .finally(() => setRunning(false))
  }

  // Reyting: to'lov bo'yicha, hali to'lov bo'lmasa — hajm bo'yicha (bo'sh diagramma ma'nosiz).
  const channels = shown?.metrics.channels ?? []
  const anyPaid = channels.some((c) => c.paid > 0)
  const barItems = [...channels]
    .sort((a, b) => (anyPaid ? b.paid - a.paid : b.submissions - a.submissions))
    .slice(0, 6)
    .map((c) => ({
      label: c.source ? `${c.name} · ${c.source}` : c.name,
      value: anyPaid ? c.paid : c.submissions,
    }))

  return (
    <Card
      className="mb-4"
      title={
        <span className="inline-flex items-center gap-2">
          <Sparkles className="h-4 w-4 text-brand-600" /> AI tahlil
        </span>
      }
      sub={
        shown ? (
          <span className="inline-flex items-center gap-1.5">
            <History className="h-3.5 w-3.5" /> {formatDate(shown.date)} holatidagi raqamlar ·{' '}
            <span className="font-mono">{shown.model}</span>
          </span>
        ) : (
          t.sub
        )
      }
      actions={
        <div className="flex flex-wrap items-center gap-2">
          {records.length > 1 && (
            <Button variant="ghost" onClick={() => setShowHistory((v) => !v)}>
              <History className="h-4 w-4" /> Tarix · {records.length}
            </Button>
          )}
          {records.length > 0 && canRun && (
            <Button variant="secondary" onClick={generate} disabled={running || blockedToday}>
              <RefreshCw className={running ? 'h-4 w-4 animate-spin' : 'h-4 w-4'} />
              Yangi tahlil
            </Button>
          )}
          {shown && (
            <Button onClick={() => openPrintWindow(buildPrintHtml(shown, t))}>
              <FileDown className="h-4 w-4" /> PDF
            </Button>
          )}
        </div>
      }
    >
      {loading ? (
        <p className="py-6 text-center text-sm text-slate-400">Tahlillar yuklanmoqda...</p>
      ) : (
        <div className="space-y-4">
          {loadError && <AiErrorBox message={loadError} />}
          {blockedToday && !info && (
            <div className="flex items-start gap-2 rounded-lg bg-slate-50 px-3 py-2 text-xs text-slate-500">
              <Info className="mt-0.5 h-4 w-4 shrink-0" /> <span>{t.blocked}</span>
            </div>
          )}
          {info && (
            <div className="flex items-start gap-2 rounded-lg bg-blue-50 px-3 py-2 text-xs text-blue-700">
              <Info className="mt-0.5 h-4 w-4 shrink-0" /> <span>{info}</span>
            </div>
          )}
          {runError && <AiErrorBox message={runError} />}

          {running && (
            <div className="flex flex-col items-center justify-center gap-2 py-8 text-slate-400">
              <RefreshCw className="h-7 w-7 animate-spin text-brand-500" />
              <p className="text-sm">{t.running}</p>
            </div>
          )}

          {/* ---------- TARIX: sana + umumiy ball, eng yangisi tepada ---------- */}
          {showHistory && records.length > 0 && (
            <div className="max-h-64 space-y-1.5 overflow-y-auto rounded-xl border border-slate-100 p-2">
              {records.map((r) => {
                const active = r.id === shown?.id
                return (
                  <button
                    key={r.id}
                    type="button"
                    onClick={() => setSelectedId(r.id)}
                    className={`flex w-full items-center gap-3 rounded-lg border px-3 py-2 text-left transition-colors ${
                      active
                        ? 'border-brand-300 bg-brand-50/60'
                        : 'border-slate-100 hover:border-slate-200 hover:bg-slate-50'
                    }`}
                  >
                    <span
                      className="flex h-9 w-9 shrink-0 items-center justify-center rounded-lg bg-slate-50 text-xs font-bold"
                      style={{ color: scoreColor(r.overallScore) }}
                    >
                      {r.overallScore}
                    </span>
                    <span className="min-w-0 flex-1">
                      <span className="block text-sm font-semibold text-slate-800">{formatDate(r.date)}</span>
                      <span className="block truncate text-xs text-slate-400">
                        {r.createdAt.length >= 16 ? `${r.createdAt.slice(11, 16)} · ` : ''}
                        {r.ai.umumiy || r.model}
                      </span>
                    </span>
                  </button>
                )
              })}
            </div>
          )}

          {/* ---------- AI NARRATIVI ---------- */}
          {shown ? (
            <div className="space-y-5">
              <div className="grid items-center gap-4 rounded-2xl border border-slate-100 bg-slate-50/60 p-4 sm:grid-cols-2">
                <div className="flex items-center gap-4">
                  <ScoreRing value={shown.ai.baholar.umumiy} />
                  <div className="space-y-2">
                    <p className="text-sm font-medium text-slate-500">Umumiy baho</p>
                    {(() => {
                      const tr = trendInfo(shown.ai.trend)
                      return (
                        <span className={`inline-flex items-center gap-1.5 rounded-full px-3 py-1 text-sm font-semibold ${tr.cls}`}>
                          <tr.Icon className="h-4 w-4" /> {tr.label}
                        </span>
                      )
                    })()}
                  </div>
                </div>
                <AiRadar data={dimLabels.map((d) => ({ subject: d.label, value: shown.ai.baholar[d.key] ?? 0 }))} />
              </div>

              <ScoreGrid items={dimLabels.map((d) => ({ label: d.label, value: shown.ai.baholar[d.key] ?? 0 }))} />

              <TextBlock title="Umumiy holat" text={shown.ai.umumiy} />

              {shown.ai.ozgarishlar && (
                <div className="rounded-xl border border-brand-100 bg-brand-50/60 p-4">
                  <p className="mb-1.5 flex items-center gap-1.5 text-sm font-semibold text-brand-800">
                    <GitCompare className="h-4 w-4" /> Oldingi tahlilga nisbatan o'zgarishlar
                  </p>
                  <p className="text-sm leading-relaxed text-slate-700">{shown.ai.ozgarishlar}</p>
                </div>
              )}

              <div className="grid gap-4 md:grid-cols-2">
                <TextBlock title={t.narrativeChannels} text={shown.ai.kanallar} />
                <TextBlock title="Voronka" text={shown.ai.voronka} />
                <TextBlock title="Lid sifati" text={shown.ai.sifat} />
                <TextBlock title="Pul" text={shown.ai.pul} />
              </div>

              {/* Kanal/test reytingi — AI xulosasini RAQAM bilan tekshirish uchun */}
              {barItems.length > 0 && (
                <div className="rounded-xl border border-slate-100 p-4">
                  <p className="text-sm font-semibold text-slate-800">{t.channels}</p>
                  <p className="mb-3 text-xs text-slate-400">
                    {anyPaid ? t.channelsSub : "Hali to'lov yo'q — hajm bo'yicha saralandi"}
                  </p>
                  <RankedBars items={barItems} empty={t.channelsEmpty} barClass="bg-teal-400" />
                </div>
              )}

              <div className="grid gap-4 md:grid-cols-2">
                {shown.ai.kuchli.length > 0 && (
                  <CardList title="Kuchli tomonlari" Icon={CheckCircle2} tone="green" items={shown.ai.kuchli} />
                )}
                {shown.ai.zaif.length > 0 && (
                  <CardList title="Zaif tomonlari" Icon={AlertTriangle} tone="amber" items={shown.ai.zaif} />
                )}
              </div>

              {shown.ai.xavflar.length > 0 && (
                <CardList title="Xavflar" Icon={AlertCircle} tone="red" items={shown.ai.xavflar} />
              )}
              {shown.ai.tavsiyalar.length > 0 && (
                <CardList title="Tavsiyalar" Icon={Lightbulb} tone="blue" items={shown.ai.tavsiyalar} />
              )}
            </div>
          ) : (
            !running && (
              <div className="flex flex-col items-center justify-center gap-3 py-8 text-center">
                <Sparkles className="h-9 w-9 text-brand-300" />
                <p className="max-w-xl text-sm text-slate-500">{t.intro}</p>
                {canRun ? (
                  <Button onClick={generate} disabled={running || blockedToday}>
                    <Sparkles className="h-4 w-4" /> AI tahlil yaratish
                  </Button>
                ) : (
                  // Ko'rish ruxsati bor, yozish yo'q: tugma o'rniga sabab (bosilsa 403 bo'lardi).
                  <p className="text-xs text-slate-400">
                    Tahlil yaratish uchun bu bo'limda yozish ruxsati kerak.
                  </p>
                )}
              </div>
            )
          )}
        </div>
      )}
    </Card>
  )
}
