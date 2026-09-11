import { useEffect, useMemo, useState } from 'react'
import {
  AlertCircle,
  AlertTriangle,
  Award,
  BookOpenCheck,
  CalendarX2,
  CheckCircle2,
  ClipboardList,
  FileDown,
  GitCompare,
  History,
  Info,
  Lightbulb,
  MessageSquareQuote,
  MessageSquareWarning,
  RefreshCw,
  Sparkles,
  UserMinus,
  UserPlus,
  Users,
} from 'lucide-react'
import {
  Bar,
  BarChart,
  CartesianGrid,
  Line,
  LineChart,
  ResponsiveContainer,
  Tooltip,
  XAxis,
  YAxis,
} from 'recharts'
import type { TeacherAiMetrics, TeacherAiRecord } from '@/types'
import {
  getTeacherAiAnalyses,
  getTeacherAiSnapshot,
  runTeacherAiAnalysis,
} from '@/api/services/teachers'
import { Card } from '@/components/ui/Card'
import { Button } from '@/components/ui/Button'
import { Loader } from '@/components/ui/Loader'
import { StatCard } from '@/components/ui/StatCard'
import {
  AiErrorBox, AiRadar, CardList, MiniStat, PctRow, RankedBars, ScoreGrid, ScoreRing, TextBlock,
} from '@/components/ai/AiParts'
import { escapeHtml, openPrintWindow, printCss, scoreColor, trendInfo } from '@/lib/ai'
import { monthShortNames } from '@/config/constants'
import { apiErrorMessage, cn, formatDate } from '@/lib/utils'

/** "2026-07" → "Iyl" (diagramma o'qi uchun qisqa yorliq). */
const shortMonth = (ym: string) => monthShortNames[Number(ym.slice(5, 7)) - 1] ?? ym

const dimLabels: { key: keyof TeacherAiRecord['ai']['baholar']; label: string }[] = [
  { key: 'jurnal', label: 'Jurnal' },
  { key: 'saqlash', label: "O'quvchi saqlash" },
  { key: 'baholash', label: 'Baholash' },
  { key: 'rivojlanish', label: 'Rivojlanish' },
  { key: 'faollik', label: 'Faollik' },
]

function buildPrintHtml(rec: TeacherAiRecord, teacherName: string): string {
  const r = rec.ai
  const m = rec.metrics
  const li = (arr: string[]) => arr.map((x) => `<li>${escapeHtml(x)}</li>`).join('')
  const b = r.baholar
  const row = (label: string, v: string | number) =>
    `<tr><td>${label}</td><td style="text-align:right;font-weight:bold">${v}</td></tr>`
  return `<!DOCTYPE html><html lang="uz"><head><meta charset="utf-8"><title>AI tahlil — ${escapeHtml(teacherName)}</title>
<style>${printCss}</style></head><body>
  <div class="head"><div class="brand">WunderkindLC · O'qituvchi AI tahlili</div>
    <h1>${escapeHtml(teacherName)}</h1>
    <div class="meta">Sana: ${escapeHtml(rec.date)} · Model: ${escapeHtml(rec.model)} · Umumiy baho: <b>${b.umumiy}/100</b> · Trend: ${escapeHtml(r.trend)}</div>
  </div>
  <h2>Baholar</h2>
  <table>${row('Jurnal', b.jurnal)}${row("O'quvchi saqlash", b.saqlash)}${row('Baholash', b.baholash)}${row('Rivojlanish', b.rivojlanish)}${row('Faollik', b.faollik)}${row('Umumiy', b.umumiy)}</table>
  <h2>Asosiy raqamlar</h2>
  <table>${row('Kelgan o\'quvchilar', m.cameTotal)}${row('Hozir faol', m.activeStudents)}${row('Ketgan', m.leftStudents)}${row('Saqlash %', m.retentionPct + '%')}${row('Rejadagi darslar', m.plannedLessons)}${row('Belgilanmagan darslar', m.missedLessons)}${row('Jurnal to\'ldirilishi %', m.journalDonePct + '%')}${row('O\'rtacha baho (shu oy)', m.avgGradeThisMonth)}</table>
  ${r.umumiy ? `<h2>Umumiy holat</h2><p>${escapeHtml(r.umumiy)}</p>` : ''}
  ${r.oquvchilarFikri ? `<h2>O'quvchilar fikri asosida</h2><p>${escapeHtml(r.oquvchilarFikri)}</p>` : ''}
  ${r.ozgarishlar ? `<h2>Oldingi tahlilga nisbatan o'zgarishlar</h2><p>${escapeHtml(r.ozgarishlar)}</p>` : ''}
  ${r.oquvchiOqimi ? `<h2>O'quvchi oqimi</h2><p>${escapeHtml(r.oquvchiOqimi)}</p>` : ''}
  ${r.ketishSabablari ? `<h2>Ketish sabablari</h2><p>${escapeHtml(r.ketishSabablari)}</p>` : ''}
  ${r.jurnal ? `<h2>Jurnal intizomi</h2><p>${escapeHtml(r.jurnal)}</p>` : ''}
  ${r.rivojlanish ? `<h2>Rivojlanish</h2><p>${escapeHtml(r.rivojlanish)}</p>` : ''}
  ${r.kuchli.length ? `<h2>Kuchli tomonlari</h2><ul>${li(r.kuchli)}</ul>` : ''}
  ${r.zaif.length ? `<h2>Zaif tomonlari</h2><ul>${li(r.zaif)}</ul>` : ''}
  ${r.xavflar.length ? `<h2>Xavflar</h2><ul>${li(r.xavflar)}</ul>` : ''}
  ${r.tavsiyalar.length ? `<h2>Tavsiyalar</h2><ul>${li(r.tavsiyalar)}</ul>` : ''}
  <div class="foot">Ushbu tahlil sun'iy intellekt (${escapeHtml(rec.model)}) tomonidan o'qituvchi ma'lumotlari asosida yaratilgan. Yakuniy qarorlar jonli kuzatuv bilan birga ko'rib chiqilsin.</div>
  <script>window.onload=function(){setTimeout(function(){window.print()},250)}</script>
</body></html>`
}

/**
 * O'qituvchi profilidagi "AI tahlil" tabi.
 *
 * Ikki qatlam: (1) DETERMINISTIK ko'rsatkichlar — o'quvchi oqimi (kim kelyapti/ketyapti), ketish
 * sabablari, jurnalni o'z vaqtida to'ldirish, baholar dinamikasi, testlar, davomat —
 * AI ishlatilmasa ham ko'rinadi; (2) AI NARRATIVI — Gemini shu raqamlarga tayanib yozgan xulosa,
 * kuchli/zaif tomonlar, xavflar va tavsiyalar (kuniga bir marta, tarixi saqlanadi).
 */
export function TeacherAiPanel({ teacherId, teacherName }: { teacherId: string; teacherName: string }) {
  const [metrics, setMetrics] = useState<TeacherAiMetrics | null>(null)
  const [records, setRecords] = useState<TeacherAiRecord[]>([])
  const [loading, setLoading] = useState(true)
  const [loadError, setLoadError] = useState<string | null>(null)

  const [selectedId, setSelectedId] = useState<string | null>(null)
  const [running, setRunning] = useState(false)
  const [runError, setRunError] = useState<string | null>(null)
  const [info, setInfo] = useState<string | null>(null)

  const todayTk = useMemo(
    () => new Date().toLocaleDateString('en-CA', { timeZone: 'Asia/Tashkent' }),
    [],
  )

  useEffect(() => {
    let alive = true
    // Boshqa o'qituvchiga o'tilganda eski raqamlar ko'rinib qolmasin (maqsadli, bir marta).
    // eslint-disable-next-line react-hooks/set-state-in-effect
    setLoading(true)
    Promise.all([getTeacherAiSnapshot(teacherId), getTeacherAiAnalyses(teacherId)])
      .then(([m, recs]) => {
        if (!alive) return
        setMetrics(m)
        setRecords(recs)
        setSelectedId(recs[0]?.id ?? null)
      })
      .catch((e) => alive && setLoadError(apiErrorMessage(e, "Ma'lumotni yuklab bo'lmadi")))
      .finally(() => alive && setLoading(false))
    return () => {
      alive = false
    }
  }, [teacherId])

  const shown = records.find((r) => r.id === selectedId) ?? records[0] ?? null
  const blockedToday = records.some((r) => r.date === todayTk)

  const generate = () => {
    setRunning(true)
    setRunError(null)
    setInfo(null)
    runTeacherAiAnalysis(teacherId)
      .then((r) => {
        if (r.ok && r.record) {
          setRecords((prev) => [r.record!, ...prev.filter((x) => x.id !== r.record!.id)])
          setSelectedId(r.record.id)
          if (r.alreadyToday) setInfo('Bugun allaqachon tahlil qilingan. Keyingi tahlilni ertaga qilish mumkin.')
        } else {
          setRunError(r.error || "Tahlil qilib bo'lmadi.")
        }
      })
      .catch((e) => setRunError(apiErrorMessage(e, "Tahlil qilib bo'lmadi. Internet yoki API kalitini tekshiring.")))
      .finally(() => setRunning(false))
  }

  const downloadPdf = () => {
    if (shown) openPrintWindow(buildPrintHtml(shown, teacherName))
  }

  if (loading) return <Card><Loader label="Yuklanmoqda..." /></Card>
  if (loadError || !metrics)
    return <Card className="py-10 text-center text-sm text-red-500">{loadError || "Ma'lumot yo'q"}</Card>

  const flowData = metrics.flowByMonth.map((p) => ({
    name: shortMonth(p.month),
    Kelgan: p.came,
    Muzlatilgan: p.frozen,
    Ketgan: p.left,
  }))
  const journalData = metrics.journalByMonth.map((p) => ({
    name: shortMonth(p.month),
    "To'ldirilgan %": p.planned > 0 ? Math.round((p.conducted / p.planned) * 100) : 0,
    Belgilanmagan: p.missed,
  }))
  const gradeData = metrics.journalByMonth
    .filter((p) => p.grades > 0)
    .map((p) => ({ name: shortMonth(p.month), "O'rtacha baho": p.avgGrade }))

  return (
    <div className="space-y-4">
      {/* ---------- Sarlavha + amallar ---------- */}
      <Card
        title={
          <span className="inline-flex items-center gap-2">
            <Sparkles className="h-4 w-4 text-brand-600" /> AI tahlil
          </span>
        }
        sub="O'quvchi oqimi, ketish sabablari, jurnal intizomi va rivojlanish — oxirgi 12 oy"
        actions={
          <div className="flex flex-wrap items-center gap-2">
            <Button variant="secondary" onClick={generate} disabled={running || blockedToday}>
              <RefreshCw className={running ? 'h-4 w-4 animate-spin' : 'h-4 w-4'} />
              {records.length ? 'Yangi tahlil' : 'Tahlil qilish'}
            </Button>
            <Button onClick={downloadPdf} disabled={!shown}>
              <FileDown className="h-4 w-4" /> PDF
            </Button>
          </div>
        }
      >
        {blockedToday && !info && (
          <div className="flex items-start gap-2 rounded-lg bg-slate-50 px-3 py-2 text-xs text-slate-500">
            <Info className="mt-0.5 h-4 w-4 shrink-0" />
            <span>Bu o'qituvchi bugun tahlil qilingan. Keyingi tahlil ertaga mumkin (eski tahlillar saqlanib qoladi).</span>
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
            <p className="text-sm">AI o'qituvchi ma'lumotlarini tahlil qilmoqda...</p>
          </div>
        )}
      </Card>

      {/* ---------- TAHLILLAR (tarix) ----------
          Tahlillar vaqt o'tgani sayin YIG'ILIB boradi — qaysi sanada nima yozilgani ko'rinib
          tursin. Eng YANGISI tepada (server shu tartibda qaytaradi), qatorni bosib o'shanisi
          ochiladi. Ilgari bu oddiy <select> edi va tarix ko'rinmasdi. */}
      {records.length > 0 && (
        <Card
          title={
            <span className="inline-flex items-center gap-2">
              <History className="h-4 w-4 text-brand-600" /> Tahlillar
            </span>
          }
          sub={`${records.length} ta tahlil · eng yangisi tepada`}
        >
          <div className="max-h-72 space-y-1.5 overflow-y-auto">
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
                      {formatDate(r.date)}
                    </span>
                    <span className="block truncate text-xs text-slate-400">
                      {r.createdAt.length >= 16 ? `${r.createdAt.slice(11, 16)} · ` : ''}
                      {r.ai.umumiy || r.model}
                    </span>
                  </span>
                  {r.ai.oquvchilarFikri && (
                    <MessageSquareQuote
                      className="h-3.5 w-3.5 shrink-0 text-violet-500"
                      aria-label="O'quvchilar fikri hisobga olingan"
                    />
                  )}
                </button>
              )
            })}
          </div>
        </Card>
      )}

      {/* ---------- 1. O'QUVCHI OQIMI ---------- */}
      <div className="grid grid-cols-2 gap-3 sm:grid-cols-4">
        <StatCard label="Kelgan (jami)" value={metrics.cameTotal} icon={UserPlus} />
        <StatCard
          label="Hozir faol" value={metrics.activeStudents} icon={Users}
          iconBg="bg-emerald-50" iconColor="text-emerald-600"
          hint={`${metrics.trialStudents} sinov · ${metrics.frozenStudents} muzlatilgan`}
        />
        <StatCard
          label="Ketgan" value={metrics.leftStudents} icon={UserMinus}
          iconBg="bg-red-50" iconColor="text-red-500"
          hint={`Yo'qotish: ${metrics.lossPct}%`}
        />
        <StatCard
          label="Saqlash (retention)" value={`${metrics.retentionPct}%`} icon={Award}
          iconBg="bg-amber-50" iconColor="text-amber-600"
          hint={`${metrics.activeGroupCount} faol guruh`}
        />
      </div>

      <div className="grid gap-4 lg:grid-cols-2">
        <Card title="O'quvchi oqimi (oyma-oy)" sub="Kim kelyapti, kim ketyapti">
          <div className="h-60 w-full">
            <ResponsiveContainer width="100%" height="100%">
              <BarChart data={flowData} margin={{ top: 6, right: 6, left: -22, bottom: 0 }}>
                <CartesianGrid strokeDasharray="3 3" stroke="#f1f5f9" vertical={false} />
                <XAxis dataKey="name" tick={{ fontSize: 11, fill: '#94a3b8' }} axisLine={false} tickLine={false} />
                <YAxis tick={{ fontSize: 11, fill: '#cbd5e1' }} axisLine={false} tickLine={false} allowDecimals={false} />
                <Tooltip contentStyle={{ fontSize: 12, borderRadius: 10, border: '1px solid #e2e8f0' }} />
                <Bar dataKey="Kelgan" fill="#10b981" radius={[4, 4, 0, 0]} />
                <Bar dataKey="Muzlatilgan" fill="#f59e0b" radius={[4, 4, 0, 0]} />
                <Bar dataKey="Ketgan" fill="#ef4444" radius={[4, 4, 0, 0]} />
              </BarChart>
            </ResponsiveContainer>
          </div>
        </Card>

        <Card title="Ketish sabablari" sub="Guruhdan chiqarish/muzlatishda ko'rsatilgan sabablar">
          <RankedBars
            items={metrics.departureReasons.slice(0, 8)}
            empty="Oxirgi 12 oyda ketish sababi qayd etilmagan."
          />
        </Card>
      </div>

      {/* ---------- 2. JURNAL INTIZOMI ---------- */}
      <div className="grid grid-cols-2 gap-3 sm:grid-cols-4">
        <StatCard label="Rejadagi darslar" value={metrics.plannedLessons} icon={ClipboardList} />
        <StatCard
          label="Jurnal to'ldirilgan" value={`${metrics.journalDonePct}%`} icon={BookOpenCheck}
          iconBg="bg-emerald-50" iconColor="text-emerald-600"
          hint={`${metrics.conductedLessons} ta dars belgilangan`}
        />
        <StatCard
          label="O'z vaqtida to'ldirilmagan" value={metrics.missedLessons} icon={CalendarX2}
          iconBg="bg-red-50" iconColor="text-red-500"
          hint="Muhlati o'tgan, belgilanmagan darslar"
        />
        <StatCard
          label="Qo'yilgan baholar" value={metrics.gradesCount} icon={Award}
          iconBg="bg-blue-50" iconColor="text-blue-600"
          hint={`O'rtacha: ${metrics.avgGradeThisMonth || '—'} (shu oy)`}
        />
      </div>

      <div className="grid gap-4 lg:grid-cols-2">
        <Card title="Jurnalni o'z vaqtida to'ldirish" sub="Oyma-oy: to'ldirilgan % va belgilanmagan darslar">
          <div className="h-56 w-full">
            <ResponsiveContainer width="100%" height="100%">
              <BarChart data={journalData} margin={{ top: 6, right: 6, left: -22, bottom: 0 }}>
                <CartesianGrid strokeDasharray="3 3" stroke="#f1f5f9" vertical={false} />
                <XAxis dataKey="name" tick={{ fontSize: 11, fill: '#94a3b8' }} axisLine={false} tickLine={false} />
                <YAxis tick={{ fontSize: 11, fill: '#cbd5e1' }} axisLine={false} tickLine={false} allowDecimals={false} />
                <Tooltip contentStyle={{ fontSize: 12, borderRadius: 10, border: '1px solid #e2e8f0' }} />
                <Bar dataKey="To'ldirilgan %" fill="#6d5ef8" radius={[4, 4, 0, 0]} />
                <Bar dataKey="Belgilanmagan" fill="#ef4444" radius={[4, 4, 0, 0]} />
              </BarChart>
            </ResponsiveContainer>
          </div>
        </Card>

        <Card title="Dars yuritish sifati" sub="O'tilgan darslarning nechasida to'ldirilgan">
          <div className="divide-y divide-slate-50">
            <PctRow label="Mavzu yozilgan" value={metrics.topicPct} />
            <PctRow label="Uy vazifa berilgan" value={metrics.homeworkPct} />
            <PctRow label="Davomat olingan" value={metrics.attendanceTakenPct} />
            <PctRow label="O'quvchilar davomati" value={Math.round(metrics.studentAttendancePct)} hint="Guruhlaridagi o'quvchilar" />
          </div>
          {metrics.recentMissedDates.length > 0 && (
            <div className="mt-3 rounded-lg border border-red-100 bg-red-50/50 p-3">
              <p className="mb-1 flex items-center gap-1.5 text-xs font-semibold text-red-700">
                <CalendarX2 className="h-3.5 w-3.5" /> Belgilanmagan oxirgi darslar
              </p>
              <p className="text-xs leading-relaxed text-slate-600">
                {metrics.recentMissedDates.join(' · ')}
              </p>
            </div>
          )}
        </Card>
      </div>

      {/* ---------- 3. RIVOJLANISH ---------- */}
      <div className="grid gap-4 lg:grid-cols-2">
        <Card title="O'zlashtirish dinamikasi" sub="Guruhlaridagi o'rtacha baho (oyma-oy)">
          {gradeData.length === 0 ? (
            <div className="py-12 text-center text-sm text-slate-400">Baho kiritilmagan.</div>
          ) : (
            <div className="h-56 w-full">
              <ResponsiveContainer width="100%" height="100%">
                <LineChart data={gradeData} margin={{ top: 6, right: 6, left: -22, bottom: 0 }}>
                  <CartesianGrid strokeDasharray="3 3" stroke="#f1f5f9" vertical={false} />
                  <XAxis dataKey="name" tick={{ fontSize: 11, fill: '#94a3b8' }} axisLine={false} tickLine={false} />
                  <YAxis domain={[0, 5]} tick={{ fontSize: 11, fill: '#cbd5e1' }} axisLine={false} tickLine={false} />
                  <Tooltip contentStyle={{ fontSize: 12, borderRadius: 10, border: '1px solid #e2e8f0' }} />
                  <Line type="monotone" dataKey="O'rtacha baho" stroke="#6d5ef8" strokeWidth={2.5} dot={{ r: 3 }} />
                </LineChart>
              </ResponsiveContainer>
            </div>
          )}
        </Card>

        <Card title="Natijaviy ko'rsatkichlar">
          <div className="grid grid-cols-2 gap-3">
            <MiniStat label="O'rtacha ball" value={metrics.avgBall || '—'} />
            <MiniStat label="Testlar" value={metrics.testCount} hint={metrics.testAvgPct ? `o'rtacha ${metrics.testAvgPct}%` : undefined} />
            <MiniStat label="Baho (shu oy)" value={metrics.avgGradeThisMonth || '—'} hint={`o'tgan oy: ${metrics.avgGradePrevMonth || '—'}`} />
            <MiniStat label="O'qituvchi davomati" value={`${metrics.teacherPresentDays} kun`} hint={`${metrics.teacherLateDays} kechikish · ${metrics.teacherAbsentDays} kelmagan`} />
          </div>
          {(metrics.complaintCount > 0 || metrics.suggestionCount > 0) && (
            <div className="mt-3 flex items-center gap-2 rounded-lg bg-slate-50 px-3 py-2 text-xs text-slate-600">
              <MessageSquareWarning className="h-4 w-4 shrink-0 text-amber-500" />
              Ota-onalardan: <b className="text-red-600">{metrics.complaintCount}</b> shikoyat ·{' '}
              <b className="text-slate-700">{metrics.suggestionCount}</b> taklif
            </div>
          )}
        </Card>
      </div>

      {/* ---------- 4. GURUHLAR KESIMI ---------- */}
      {metrics.groups.length > 0 && (
        <Card title="Guruhlar kesimi" tight className="overflow-hidden">
          <div className="overflow-x-auto">
            <table className="w-full text-left text-sm">
              <thead>
                <tr className="border-b border-slate-100 bg-slate-50/60 text-xs uppercase tracking-wide text-slate-400">
                  <th className="px-4 py-3 font-medium">Guruh</th>
                  <th className="px-4 py-3 font-medium">Kurs</th>
                  <th className="px-4 py-3 text-right font-medium">Faol</th>
                  <th className="px-4 py-3 text-right font-medium">Sinov</th>
                  <th className="px-4 py-3 text-right font-medium">Muzlat</th>
                  <th className="px-4 py-3 text-right font-medium">Ketgan</th>
                  <th className="px-4 py-3 text-right font-medium">Dars (o'tilgan/reja)</th>
                  <th className="px-4 py-3 text-right font-medium">Belgilanmagan</th>
                  <th className="px-4 py-3 text-right font-medium">O'rt. baho</th>
                </tr>
              </thead>
              <tbody>
                {metrics.groups.map((g) => (
                  <tr key={g.groupId} className="border-b border-slate-50 last:border-0 hover:bg-slate-50/40">
                    <td className="px-4 py-2.5 font-medium text-slate-700">
                      {g.name}
                      {g.isArchived && (
                        <span className="ml-1.5 rounded bg-slate-100 px-1.5 py-0.5 text-[10px] text-slate-500">arxiv</span>
                      )}
                    </td>
                    <td className="px-4 py-2.5 text-slate-500">{g.courseName || '—'}</td>
                    <td className="px-4 py-2.5 text-right font-mono text-emerald-700">{g.active}</td>
                    <td className="px-4 py-2.5 text-right font-mono text-slate-500">{g.trial}</td>
                    <td className="px-4 py-2.5 text-right font-mono text-amber-600">{g.frozen}</td>
                    <td className="px-4 py-2.5 text-right font-mono text-red-500">{g.left}</td>
                    <td className="px-4 py-2.5 text-right font-mono text-slate-600">
                      {g.conducted}/{g.planned}
                    </td>
                    <td className={cn('px-4 py-2.5 text-right font-mono', g.missed > 0 ? 'text-red-500' : 'text-slate-300')}>
                      {g.missed}
                    </td>
                    <td className="px-4 py-2.5 text-right font-mono text-slate-600">{g.avgGrade || '—'}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </Card>
      )}

      {/* ---------- 5. AI NARRATIVI ---------- */}
      {shown ? (
        <Card
          title={
            <span className="inline-flex items-center gap-2">
              <Sparkles className="h-4 w-4 text-brand-600" /> AI xulosasi
            </span>
          }
          sub={
            <span className="inline-flex items-center gap-1.5">
              <History className="h-3.5 w-3.5" /> {formatDate(shown.date)} · <span className="font-mono">{shown.model}</span>
            </span>
          }
        >
          <div className="space-y-5">
            {/* Ball + radar */}
            <div className="grid items-center gap-4 rounded-2xl border border-slate-100 bg-slate-50/60 p-4 sm:grid-cols-2">
              <div className="flex items-center gap-4">
                <ScoreRing value={shown.ai.baholar.umumiy} />
                <div className="space-y-2">
                  <p className="text-sm font-medium text-slate-500">Umumiy baho</p>
                  {(() => {
                    const t = trendInfo(shown.ai.trend)
                    return (
                      <span className={`inline-flex items-center gap-1.5 rounded-full px-3 py-1 text-sm font-semibold ${t.cls}`}>
                        <t.Icon className="h-4 w-4" /> {t.label}
                      </span>
                    )
                  })()}
                </div>
              </div>
              <AiRadar data={dimLabels.map((d) => ({ subject: d.label, value: shown.ai.baholar[d.key] ?? 0 }))} />
            </div>

            {/* Sohaviy ballar */}
            <ScoreGrid items={dimLabels.map((d) => ({ label: d.label, value: shown.ai.baholar[d.key] ?? 0 }))} />

            <TextBlock title="Umumiy holat" text={shown.ai.umumiy} />

            {/* O'QUVCHILAR FIKRI — o'quvchi profilida yozib borilgan matnli mulohazalardan AI
                ajratgan naqshlar. XOM matn va o'quvchi ismi bu yerda HECH QACHON chiqmaydi. */}
            {shown.ai.oquvchilarFikri && (
              <div className="rounded-xl border border-violet-100 bg-violet-50/60 p-4">
                <p className="mb-1.5 flex items-center gap-1.5 text-sm font-semibold text-violet-800">
                  <MessageSquareQuote className="h-4 w-4" /> O'quvchilar fikri asosida
                </p>
                <p className="text-sm leading-relaxed text-slate-700">{shown.ai.oquvchilarFikri}</p>
                {(shown.metrics.studentReviewCount ?? 0) > 0 && (
                  <p className="mt-2 text-[11px] text-violet-700/70">
                    {shown.metrics.studentReviewCount} ta yozib olingan fikr asosida · xom matnlar
                    ko'rsatilmaydi
                  </p>
                )}
              </div>
            )}

            {shown.ai.ozgarishlar && (
              <div className="rounded-xl border border-brand-100 bg-brand-50/60 p-4">
                <p className="mb-1.5 flex items-center gap-1.5 text-sm font-semibold text-brand-800">
                  <GitCompare className="h-4 w-4" /> Oldingi tahlilga nisbatan o'zgarishlar
                </p>
                <p className="text-sm leading-relaxed text-slate-700">{shown.ai.ozgarishlar}</p>
              </div>
            )}

            <div className="grid gap-4 md:grid-cols-2">
              <TextBlock title="O'quvchi oqimi" text={shown.ai.oquvchiOqimi} />
              <TextBlock title="Ketish sabablari" text={shown.ai.ketishSabablari} />
              <TextBlock title="Jurnal intizomi" text={shown.ai.jurnal} />
              <TextBlock title="Rivojlanish" text={shown.ai.rivojlanish} />
            </div>

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
        </Card>
      ) : (
        !running && (
          <Card>
            <div className="flex flex-col items-center justify-center gap-3 py-10 text-center text-slate-400">
              <Sparkles className="h-9 w-9 text-brand-300" />
              <p className="max-w-md text-sm">
                Bu o'qituvchi hali AI orqali tahlil qilinmagan. <b className="text-slate-600">"Tahlil qilish"</b>{' '}
                tugmasini bosing — AI yuqoridagi barcha raqamlarni o'rganib, xulosa, kuchli/zaif tomonlar,
                xavflar va tavsiyalarni chiqaradi.
              </p>
            </div>
          </Card>
        )
      )}
    </div>
  )
}
