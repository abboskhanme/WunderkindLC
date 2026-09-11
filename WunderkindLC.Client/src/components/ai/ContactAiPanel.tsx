import { useEffect, useState } from 'react'
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
  getContactAiAnalyses,
  runContactAiAnalysis,
  type ContactAiRecord,
  type ContactAiScores,
} from '@/api/services/contacts'
import { Card } from '@/components/ui/Card'
import { Button } from '@/components/ui/Button'
import {
  AiErrorBox, AiRadar, CardList, RankedBars, ScoreGrid, ScoreRing, TextBlock,
} from '@/components/ai/AiParts'
import { escapeHtml, openPrintWindow, printCss, scoreColor, trendInfo } from '@/lib/ai'
import { usePerm } from '@/lib/permissions'
import { apiErrorMessage, formatDate } from '@/lib/utils'

/**
 * "BOG'LANISH KERAK" HISOBOTINING AI TAHLILI — yozilgan SABABLAR, "javobi nima dedi" matnlari va
 * qo'ng'iroq natijalari bo'yicha xulosa.
 *
 * <p>⚠️ Boshqa AI panellardan FARQI — u DAVR bilan ishlaydi: hisobotda tanlangan kun/oy/oraliq
 * uchun tahlil qilinadi va TARIX ham AYNAN o'sha davr bo'yicha ko'rsatiladi (boshqa davr tahlili
 * ko'rinib qolsa, ekrandagi raqamlar boshqa davrniki bo'lardi). Davr o'zgarganda panel qaytadan
 * yuklanadi.</p>
 *
 * <p>Umumiy qismlar `components/ai/AiParts` va `lib/ai` dan olinadi (nusxa yo'q) —
 * `.claude/rules/ai-analysis.md`.</p>
 */

/** Sohaviy baholar — radar va ball kartochkalari uchun yagona tartib. */
const dimLabels: { key: keyof ContactAiScores; label: string }[] = [
  { key: 'qamrov', label: 'Qamrov' },
  { key: 'aloqa', label: 'Aloqa' },
  { key: 'natija', label: 'Natija' },
  { key: 'sifat', label: 'Yozuv sifati' },
]

/** Chop etish (PDF) uchun HTML — `lib/ai` dagi umumiy uslub bilan. */
function buildPrintHtml(rec: ContactAiRecord): string {
  const r = rec.ai
  const m = rec.metrics
  const b = r.baholar
  const li = (arr: string[]) => arr.map((x) => `<li>${escapeHtml(x)}</li>`).join('')
  const row = (label: string, v: string | number) =>
    `<tr><td>${escapeHtml(label)}</td><td style="text-align:right;font-weight:bold">${v}</td></tr>`
  const pct = m.attempts > 0 ? Math.round((m.reached / m.attempts) * 100) : 0
  return `<!DOCTYPE html><html lang="uz"><head><meta charset="utf-8"><title>Bog'lanish kerak — AI tahlil (${escapeHtml(rec.from)} — ${escapeHtml(rec.to)})</title>
<style>${printCss}</style></head><body>
  <div class="head"><div class="brand">WunderkindLC · Bog'lanish kerak — AI tahlil</div>
    <h1>Bog'lanish navbati tahlili</h1>
    <div class="meta">Davr: ${escapeHtml(rec.from)} — ${escapeHtml(rec.to)} · Yaratilgan: ${escapeHtml(rec.date)} · Model: ${escapeHtml(rec.model)} · Umumiy baho: <b>${b.umumiy}/100</b> · Trend: ${escapeHtml(r.trend)}</div>
  </div>
  <h2>Baholar</h2>
  <table>${row('Qamrov', b.qamrov)}${row('Aloqa', b.aloqa)}${row('Natija', b.natija)}${row('Yozuv sifati', b.sifat)}${row('Umumiy', b.umumiy)}</table>
  <h2>Asosiy raqamlar</h2>
  <table>${row('Yangi talab', m.created)}${row('Urinish', m.attempts)}${row("Bog'lanildi", m.reached)}${row('Aloqa foizi', pct + '%')}${row("Hal bo'ldi", m.done)}${row("Qayta qo'ng'iroq", m.callback)}${row("Bog'lanib bo'lmadi", m.failed)}${row('Hozir navbatda', m.openNow)}${row("Muddati o'tgan", m.overdueNow)}${row('Javob yozilgan', m.withResponse ?? 0)}</table>
  ${r.umumiy ? `<h2>Umumiy holat</h2><p>${escapeHtml(r.umumiy)}</p>` : ''}
  ${r.ozgarishlar ? `<h2>Oldingi tahlilga nisbatan o'zgarishlar</h2><p>${escapeHtml(r.ozgarishlar)}</p>` : ''}
  ${r.sabablar ? `<h2>Sabablar</h2><p>${escapeHtml(r.sabablar)}</p>` : ''}
  ${r.javoblar ? `<h2>Javoblarda nima deyilyapti</h2><p>${escapeHtml(r.javoblar)}</p>` : ''}
  ${r.sifat ? `<h2>Aloqa sifati</h2><p>${escapeHtml(r.sifat)}</p>` : ''}
  ${r.xodimlar ? `<h2>Xodimlar</h2><p>${escapeHtml(r.xodimlar)}</p>` : ''}
  ${m.byReason.length ? `<h2>Sabablar kesimi</h2><ul>${li(m.byReason.map((x) => `${x.reasonLabel} — ochilgan: ${x.created}, hal: ${x.done}, ochiq: ${x.open}`))}</ul>` : ''}
  ${m.byStaff.length ? `<h2>Xodimlar kesimi</h2><ul>${li(m.byStaff.map((x) => `${x.actorName} — urinish: ${x.attempts}, bog'lanildi: ${x.reached}, hal: ${x.done}`))}</ul>` : ''}
  ${r.kuchli.length ? `<h2>Kuchli tomonlari</h2><ul>${li(r.kuchli)}</ul>` : ''}
  ${r.zaif.length ? `<h2>Zaif tomonlari</h2><ul>${li(r.zaif)}</ul>` : ''}
  ${r.xavflar.length ? `<h2>Xavflar</h2><ul>${li(r.xavflar)}</ul>` : ''}
  ${r.tavsiyalar.length ? `<h2>Tavsiyalar</h2><ul>${li(r.tavsiyalar)}</ul>` : ''}
  <div class="foot">Ushbu tahlil sun'iy intellekt (${escapeHtml(rec.model)}) tomonidan ${escapeHtml(rec.from)} — ${escapeHtml(rec.to)} davridagi raqamlar va yozilgan javoblar asosida yaratilgan. Yakuniy qarorlar jonli kuzatuv bilan birga ko'rib chiqilsin.</div>
  <script>window.onload=function(){setTimeout(function(){window.print()},250)}</script>
</body></html>`
}

export function ContactAiPanel({ from, to }: { from: string; to: string }) {
  const { can } = usePerm()
  // Yaratish — `contacts` bo'limining "qo'shish" amali (serverdagi qoida bilan bir xil):
  // faqat ko'rish ruxsati bor xodim tahlilni O'QIYDI, lekin pulli chaqiruvni boshlay olmaydi.
  const canRun = can('contacts', 'create')

  const [records, setRecords] = useState<ContactAiRecord[]>([])
  const [loading, setLoading] = useState(true)
  const [loadError, setLoadError] = useState<string | null>(null)

  const [selectedId, setSelectedId] = useState<string | null>(null)
  const [showHistory, setShowHistory] = useState(false)
  const [running, setRunning] = useState(false)
  const [runError, setRunError] = useState<string | null>(null)
  const [info, setInfo] = useState<string | null>(null)

  useEffect(() => {
    let alive = true
    // Davr o'zgarganda eski davr tahlili ko'rinib qolmasin (maqsadli).
    // eslint-disable-next-line react-hooks/set-state-in-effect
    setLoading(true)
    setRunError(null)
    setInfo(null)
    getContactAiAnalyses(from, to)
      .then((recs) => {
        if (!alive) return
        setRecords(recs)
        setSelectedId(recs[0]?.id ?? null)
        setLoadError(null)
      })
      .catch((e) => alive && setLoadError(apiErrorMessage(e, "Tahlillarni yuklab bo'lmadi")))
      .finally(() => alive && setLoading(false))
    return () => {
      alive = false
    }
  }, [from, to])

  const shown = records.find((r) => r.id === selectedId) ?? records[0] ?? null

  // "Bugun" — markaz vaqti bo'yicha (server ham shu kunni hisoblaydi).
  const todayTk = new Date().toLocaleDateString('en-CA', { timeZone: 'Asia/Tashkent' })
  /** Shu DAVR uchun bugun tahlil qilinganmi (cheklov davr bo'yicha — sana bo'yicha emas). */
  const blockedToday = records.some((r) => r.date === todayTk)

  const generate = () => {
    setRunning(true)
    setRunError(null)
    setInfo(null)
    runContactAiAnalysis(from, to)
      .then((r) => {
        if (r.ok && r.record) {
          const rec = r.record
          setRecords((prev) => [rec, ...prev.filter((x) => x.id !== rec.id)])
          setSelectedId(rec.id)
          // ⚠️ Bu XATO EMAS: server shu davr uchun tayyor tahlilni qaytardi (Gemini chaqirilmadi).
          if (r.alreadyToday)
            setInfo("Bu davr uchun bugungi tahlil allaqachon tayyor — o'sha ko'rsatildi.")
        } else {
          setRunError(r.error || "Tahlil qilib bo'lmadi.")
        }
      })
      .catch((e) =>
        setRunError(apiErrorMessage(e, "Tahlil qilib bo'lmadi. Internet yoki API kalitini tekshiring.")),
      )
      .finally(() => setRunning(false))
  }

  // Sabablar reytingi — AI xulosasini RAQAM bilan tekshirish uchun.
  const reasonBars = [...(shown?.metrics.byReason ?? [])]
    .sort((a, b) => b.created - a.created)
    .slice(0, 6)
    .map((r) => ({ label: r.reasonLabel, value: r.created }))

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
            <History className="h-3.5 w-3.5" /> {formatDate(shown.from)} — {formatDate(shown.to)} davri ·{' '}
            <span className="font-mono">{shown.model}</span>
          </span>
        ) : (
          "Sabablar, javob matnlari va natijalar bo'yicha AI xulosasi — tanlangan davr uchun"
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
            <Button onClick={() => openPrintWindow(buildPrintHtml(shown))}>
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
              <Info className="mt-0.5 h-4 w-4 shrink-0" />
              <span>
                Bu davr bugun allaqachon tahlil qilingan. Boshqa davrni tanlasangiz — yangi tahlil
                qilish mumkin (eski tahlillar saqlanib qoladi).
              </span>
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
              <p className="text-sm">AI javoblar va sabablarni o'qimoqda...</p>
            </div>
          )}

          {/* ---------- TARIX: shu davrning tahlillari, eng yangisi tepada ---------- */}
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
                      <span className="block text-sm font-semibold text-slate-800">
                        {formatDate(r.date)} kuni yaratilgan
                      </span>
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
                        <span
                          className={`inline-flex items-center gap-1.5 rounded-full px-3 py-1 text-sm font-semibold ${tr.cls}`}
                        >
                          <tr.Icon className="h-4 w-4" /> {tr.label}
                        </span>
                      )
                    })()}
                  </div>
                </div>
                <AiRadar
                  data={dimLabels.map((d) => ({ subject: d.label, value: shown.ai.baholar[d.key] ?? 0 }))}
                />
              </div>

              <ScoreGrid
                items={dimLabels.map((d) => ({ label: d.label, value: shown.ai.baholar[d.key] ?? 0 }))}
              />

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
                <TextBlock title="Sabablar" text={shown.ai.sabablar} />
                <TextBlock title="Javoblarda nima deyilyapti" text={shown.ai.javoblar} />
                <TextBlock title="Aloqa sifati" text={shown.ai.sifat} />
                <TextBlock title="Xodimlar" text={shown.ai.xodimlar} />
              </div>

              {reasonBars.length > 0 && (
                <div className="rounded-xl border border-slate-100 p-4">
                  <p className="text-sm font-semibold text-slate-800">Eng ko'p uchragan sabablar</p>
                  <p className="mb-3 text-xs text-slate-400">
                    Davrda ochilgan talablar soni bo'yicha — AI shu tartibga tayanib yozadi
                  </p>
                  <RankedBars items={reasonBars} empty="Sabab bo'yicha ma'lumot yo'q." barClass="bg-teal-400" />
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
                <p className="max-w-xl text-sm text-slate-500">
                  Bu davr hali AI orqali tahlil qilinmagan. Tugmani bosing — AI yozilgan sabablarni,
                  «javobi nima dedi» matnlarini va qo'ng'iroq natijalarini o'qib, odamlar aynan nima
                  deyayotganini, navbat qanday ishlanayotganini va nimani tuzatish kerakligini yozib
                  beradi.
                </p>
                {canRun ? (
                  <Button onClick={generate} disabled={running}>
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
