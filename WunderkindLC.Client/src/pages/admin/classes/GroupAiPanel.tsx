import { useEffect, useMemo, useState } from 'react'
import {
  AlertCircle,
  AlertTriangle,
  BookOpenCheck,
  CalendarCheck,
  CalendarX2,
  CheckCircle2,
  ClipboardList,
  FileDown,
  GitCompare,
  History,
  Info,
  Lightbulb,
  RefreshCw,
  Snowflake,
  Sparkles,
  UserMinus,
  Users,
  Wallet,
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
import type { GroupAiMetrics, GroupAiRecord } from '@/types'
import { getGroupAiAnalyses, getGroupAiSnapshot, runGroupAiAnalysis } from '@/api/services/classes'
import { Card } from '@/components/ui/Card'
import { Button } from '@/components/ui/Button'
import { Loader } from '@/components/ui/Loader'
import { StatCard } from '@/components/ui/StatCard'
import {
  AiErrorBox, AiRadar, CardList, MiniStat, PctRow, RankedBars, ScoreGrid, ScoreRing, TextBlock,
} from '@/components/ai/AiParts'
import { escapeHtml, openPrintWindow, printCss, trendInfo } from '@/lib/ai'
import { monthShortNames } from '@/config/constants'
import { apiErrorMessage, cn, formatDate, formatMoney } from '@/lib/utils'

/** "2026-07" → "Iyl" (diagramma o'qi uchun qisqa yorliq). */
const shortMonth = (ym: string) => monthShortNames[Number(ym.slice(5, 7)) - 1] ?? ym

const dimLabels: { key: keyof GroupAiRecord['ai']['baholar']; label: string }[] = [
  { key: 'davomat', label: 'Davomat' },
  { key: 'barqarorlik', label: 'Barqarorlik' },
  { key: 'ozlashtirish', label: "O'zlashtirish" },
  { key: 'tolov', label: "To'lov" },
  { key: 'jurnal', label: 'Jurnal' },
]

const statusLabel: Record<string, string> = { active: 'Aktiv', frozen: 'Muzlatilgan', trial: 'Sinov' }

function buildPrintHtml(rec: GroupAiRecord): string {
  const r = rec.ai
  const m = rec.metrics
  const li = (arr: string[]) => arr.map((x) => `<li>${escapeHtml(x)}</li>`).join('')
  const b = r.baholar
  const row = (label: string, v: string | number) =>
    `<tr><td>${label}</td><td style="text-align:right;font-weight:bold">${v}</td></tr>`
  return `<!DOCTYPE html><html lang="uz"><head><meta charset="utf-8"><title>AI tahlil — ${escapeHtml(m.groupName)}</title>
<style>${printCss}</style></head><body>
  <div class="head"><div class="brand">WunderkindLC · Guruh AI tahlili</div>
    <h1>${escapeHtml(m.groupName)}</h1>
    <div class="meta">${escapeHtml(m.courseName)} · O'qituvchi: ${escapeHtml(m.teacherName || '—')} · Sana: ${escapeHtml(rec.date)} · Model: ${escapeHtml(rec.model)} · Umumiy baho: <b>${b.umumiy}/100</b> · Trend: ${escapeHtml(r.trend)}</div>
  </div>
  <h2>Baholar</h2>
  <table>${row('Davomat', b.davomat)}${row('Barqarorlik', b.barqarorlik)}${row("O'zlashtirish", b.ozlashtirish)}${row("To'lov", b.tolov)}${row('Jurnal', b.jurnal)}${row('Umumiy', b.umumiy)}</table>
  <h2>Asosiy raqamlar</h2>
  <table>${row('Hozir faol', m.activeStudents)}${row('Muzlatilgan', m.frozenStudents)}${row('Ketgan', m.leftStudents)}${row('Saqlash %', m.retentionPct + '%')}${row('Davomat %', m.attendancePct + '%')}${row('Belgilanmagan darslar', m.missedLessons)}${row("Jurnal to'ldirilishi %", m.journalDonePct + '%')}${row("O'rtacha baho (shu oy)", m.avgGradeThisMonth)}${m.financeIncluded ? row("To'lov yig'ilishi %", m.collectionPct + '%') : ''}</table>
  ${r.umumiy ? `<h2>Umumiy holat</h2><p>${escapeHtml(r.umumiy)}</p>` : ''}
  ${r.ozgarishlar ? `<h2>Oldingi tahlilga nisbatan o'zgarishlar</h2><p>${escapeHtml(r.ozgarishlar)}</p>` : ''}
  ${r.davomat ? `<h2>Davomat</h2><p>${escapeHtml(r.davomat)}</p>` : ''}
  ${r.oqim ? `<h2>Muzlatish va ketish</h2><p>${escapeHtml(r.oqim)}</p>` : ''}
  ${r.ozlashtirish ? `<h2>O'zlashtirish</h2><p>${escapeHtml(r.ozlashtirish)}</p>` : ''}
  ${r.imtihonlar ? `<h2>Imtihonlar</h2><p>${escapeHtml(r.imtihonlar)}</p>` : ''}
  ${r.tolovlar ? `<h2>To'lovlar</h2><p>${escapeHtml(r.tolovlar)}</p>` : ''}
  ${r.jurnal ? `<h2>Jurnal intizomi</h2><p>${escapeHtml(r.jurnal)}</p>` : ''}
  ${r.kuchli.length ? `<h2>Kuchli tomonlari</h2><ul>${li(r.kuchli)}</ul>` : ''}
  ${r.zaif.length ? `<h2>Zaif tomonlari</h2><ul>${li(r.zaif)}</ul>` : ''}
  ${r.xavflar.length ? `<h2>Xavflar</h2><ul>${li(r.xavflar)}</ul>` : ''}
  ${r.tavsiyalar.length ? `<h2>Tavsiyalar</h2><ul>${li(r.tavsiyalar)}</ul>` : ''}
  <div class="foot">Ushbu tahlil sun'iy intellekt (${escapeHtml(rec.model)}) tomonidan guruh ma'lumotlari asosida yaratilgan. Yakuniy qarorlar jonli kuzatuv bilan birga ko'rib chiqilsin.</div>
  <script>window.onload=function(){setTimeout(function(){window.print()},250)}</script>
</body></html>`
}

/**
 * Guruh sahifasidagi "AI tahlil" tabi.
 *
 * Ikki qatlam: (1) DETERMINISTIK ko'rsatkichlar — a'zolik oqimi (kelgan/muzlatilgan/ketgan) va
 * ketish sabablari, davomat, jurnal intizomi, o'zlashtirish, imtihonlar, to'lovlar, dastur
 * qamrovi, o'quvchilar kesimi — AI ishlatilmasa ham ko'rinadi; (2) AI NARRATIVI — Gemini shu
 * raqamlarga tayanib yozgan TANQIDIY xulosa, xavflar va tavsiyalar (kuniga bir marta, tarix bilan).
 */
export function GroupAiPanel({ groupId }: { groupId: string }) {
  const [metrics, setMetrics] = useState<GroupAiMetrics | null>(null)
  const [records, setRecords] = useState<GroupAiRecord[]>([])
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
    // Boshqa guruhga o'tilganda eski raqamlar ko'rinib qolmasin (maqsadli, bir marta).
    // eslint-disable-next-line react-hooks/set-state-in-effect
    setLoading(true)
    Promise.all([getGroupAiSnapshot(groupId), getGroupAiAnalyses(groupId)])
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
  }, [groupId])

  const shown = records.find((r) => r.id === selectedId) ?? records[0] ?? null
  const blockedToday = records.some((r) => r.date === todayTk)

  const generate = () => {
    setRunning(true)
    setRunError(null)
    setInfo(null)
    runGroupAiAnalysis(groupId)
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

  if (loading) return <Card><Loader label="Yuklanmoqda..." /></Card>
  if (loadError || !metrics)
    return <Card className="py-10 text-center text-sm text-red-500">{loadError || "Ma'lumot yo'q"}</Card>

  const m = metrics
  const flowData = m.flowByMonth.map((p) => ({
    name: shortMonth(p.month),
    Kelgan: p.came,
    Muzlatilgan: p.frozen,
    Ketgan: p.left,
  }))
  const attendanceData = m.monthStats
    .filter((p) => p.conducted > 0)
    .map((p) => ({ name: shortMonth(p.month), 'Davomat %': p.attendancePct }))
  const gradeData = m.monthStats
    .filter((p) => p.grades > 0)
    .map((p) => ({ name: shortMonth(p.month), "O'rtacha baho": p.avgGrade }))
  const journalData = m.monthStats.map((p) => ({
    name: shortMonth(p.month),
    "To'ldirilgan %": p.planned > 0 ? Math.round((p.conducted / p.planned) * 100) : 0,
    Belgilanmagan: p.missed,
  }))
  const payData = m.monthStats.map((p) => ({
    name: shortMonth(p.month),
    Hisoblangan: p.billed,
    "Yig'ilgan": p.collected,
  }))
  const curriculumPct = m.curriculumTotal > 0
    ? Math.round((m.curriculumCovered / m.curriculumTotal) * 100)
    : 0

  return (
    <div className="space-y-4">
      {/* ---------- Sarlavha + amallar ---------- */}
      <Card
        title={
          <span className="inline-flex items-center gap-2">
            <Sparkles className="h-4 w-4 text-brand-600" /> AI tahlil
          </span>
        }
        sub="Davomat, muzlatish/ketish, o'zlashtirish, imtihon va to'lovlar — oxirgi 12 oy, tanqidiy tahlil"
        actions={
          <div className="flex flex-wrap items-center gap-2">
            {records.length > 1 && (
              <select
                className="rounded-lg border border-slate-200 bg-white px-2.5 py-1.5 text-xs text-slate-600 outline-none focus:border-brand-400"
                value={shown?.id ?? ''}
                onChange={(e) => setSelectedId(e.target.value)}
              >
                {records.map((r) => (
                  <option key={r.id} value={r.id}>
                    {formatDate(r.date)} · {r.overallScore}/100
                  </option>
                ))}
              </select>
            )}
            <Button variant="secondary" onClick={generate} disabled={running || blockedToday}>
              <RefreshCw className={running ? 'h-4 w-4 animate-spin' : 'h-4 w-4'} />
              {records.length ? 'Yangi tahlil' : 'Tahlil qilish'}
            </Button>
            <Button onClick={() => shown && openPrintWindow(buildPrintHtml(shown))} disabled={!shown}>
              <FileDown className="h-4 w-4" /> PDF
            </Button>
          </div>
        }
      >
        <div className="mb-3 flex flex-wrap items-center gap-x-4 gap-y-1 text-xs text-slate-500">
          <span><b className="text-slate-700">{m.groupName}</b>{m.courseName ? ` · ${m.courseName}` : ''}</span>
          {m.teacherName && <span>O'qituvchi: {m.teacherName}</span>}
          {m.days && <span>{m.days}{m.time ? ` · ${m.time}` : ''}</span>}
          {m.startDate && <span>Boshlangan: {formatDate(m.startDate)}</span>}
          {m.capacity > 0 && <span>Sig'im: {m.activeStudents + m.trialStudents}/{m.capacity} ({m.fillPct}%)</span>}
          {m.isArchived && <span className="rounded bg-slate-100 px-1.5 py-0.5 text-[10px] text-slate-500">arxiv</span>}
        </div>
        {blockedToday && !info && (
          <div className="flex items-start gap-2 rounded-lg bg-slate-50 px-3 py-2 text-xs text-slate-500">
            <Info className="mt-0.5 h-4 w-4 shrink-0" />
            <span>Bu guruh bugun tahlil qilingan. Keyingi tahlil ertaga mumkin (eski tahlillar saqlanib qoladi).</span>
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
            <p className="text-sm">AI guruh ma'lumotlarini tahlil qilmoqda...</p>
          </div>
        )}
      </Card>

      {/* ---------- 1. ASOSIY KO'RSATKICHLAR ---------- */}
      <div className="grid grid-cols-2 gap-3 sm:grid-cols-4">
        <StatCard
          label="Hozir faol" value={m.activeStudents} icon={Users}
          iconBg="bg-emerald-50" iconColor="text-emerald-600"
          hint={`${m.trialStudents} sinov · ${m.frozenStudents} muzlatilgan`}
        />
        <StatCard
          label="Ketgan" value={m.leftStudents} icon={UserMinus}
          iconBg="bg-red-50" iconColor="text-red-500"
          hint={`Jami kelgan: ${m.cameTotal} · saqlash ${m.retentionPct}%`}
        />
        <StatCard
          label="Davomat" value={`${m.attendancePct}%`} icon={CalendarCheck}
          iconBg="bg-blue-50" iconColor="text-blue-600"
          hint={`${m.absenceCount} qoldirish · ${m.lateCount} kechikish`}
        />
        {m.financeIncluded ? (
          <StatCard
            label="To'lov yig'ilishi" value={`${m.collectionPct}%`} icon={Wallet}
            iconBg="bg-amber-50" iconColor="text-amber-600"
            hint={`${m.unpaidCount} to'lamagan · qarz ${formatMoney(m.debt)}`}
          />
        ) : (
          <StatCard
            label="Jurnal to'ldirilishi" value={`${m.journalDonePct}%`} icon={BookOpenCheck}
            iconBg="bg-amber-50" iconColor="text-amber-600"
            hint={`${m.missedLessons} dars belgilanmagan`}
          />
        )}
      </div>

      {/* ---------- 2. OQIM: kelish / muzlatish / ketish ---------- */}
      <div className="grid gap-4 lg:grid-cols-2">
        <Card title="A'zolik oqimi (oyma-oy)" sub="Kim qo'shildi, kim muzlatildi, kim ketdi">
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

        <Card title="Ketish / muzlatish sabablari" sub="Amal bajarilganda ko'rsatilgan sabablar">
          <RankedBars
            items={m.departureReasons.slice(0, 8)}
            empty="Oxirgi 12 oyda sabab qayd etilmagan."
          />
          <div className="mt-3 grid grid-cols-3 gap-2">
            <MiniStat label="Muzlatilgan" value={m.frozenStudents} />
            <MiniStat label="Ketgan" value={m.leftStudents} />
            <MiniStat label="Yo'qotish" value={`${m.lossPct}%`} />
          </div>
        </Card>
      </div>

      {/* ---------- 3. DAVOMAT ---------- */}
      <div className="grid gap-4 lg:grid-cols-2">
        <Card title="Davomat dinamikasi" sub="Oyma-oy o'rtacha davomat foizi">
          {attendanceData.length === 0 ? (
            <div className="py-12 text-center text-sm text-slate-400">O'tilgan dars yo'q.</div>
          ) : (
            <div className="h-56 w-full">
              <ResponsiveContainer width="100%" height="100%">
                <LineChart data={attendanceData} margin={{ top: 6, right: 6, left: -22, bottom: 0 }}>
                  <CartesianGrid strokeDasharray="3 3" stroke="#f1f5f9" vertical={false} />
                  <XAxis dataKey="name" tick={{ fontSize: 11, fill: '#94a3b8' }} axisLine={false} tickLine={false} />
                  <YAxis domain={[0, 100]} tick={{ fontSize: 11, fill: '#cbd5e1' }} axisLine={false} tickLine={false} />
                  <Tooltip contentStyle={{ fontSize: 12, borderRadius: 10, border: '1px solid #e2e8f0' }} />
                  <Line type="monotone" dataKey="Davomat %" stroke="#2563eb" strokeWidth={2.5} dot={{ r: 3 }} />
                </LineChart>
              </ResponsiveContainer>
            </div>
          )}
        </Card>

        <Card title="Qoldirish sabablari" sub="Jurnalda belgilangan davomat sabablari">
          <RankedBars
            items={m.absenceReasons.slice(0, 8)}
            empty="Davomat sababi belgilanmagan."
            barClass="bg-amber-400"
          />
        </Card>
      </div>

      {/* ---------- 4. JURNAL INTIZOMI ---------- */}
      <div className="grid gap-4 lg:grid-cols-2">
        <Card title="Jurnal to'ldirilishi" sub="Reja / o'tilgan / belgilanmagan darslar">
          <div className="mb-3 grid grid-cols-3 gap-2">
            <MiniStat label="Rejada" value={m.plannedLessons} />
            <MiniStat label="O'tilgan" value={m.conductedLessons} />
            <MiniStat label="Belgilanmagan" value={m.missedLessons} hint="muhlati o'tgan" />
          </div>
          <div className="h-52 w-full">
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
          {m.recentMissedDates.length > 0 && (
            <div className="mt-3 rounded-lg border border-red-100 bg-red-50/50 p-3">
              <p className="mb-1 flex items-center gap-1.5 text-xs font-semibold text-red-700">
                <CalendarX2 className="h-3.5 w-3.5" /> Belgilanmagan oxirgi darslar
              </p>
              <p className="text-xs leading-relaxed text-slate-600">{m.recentMissedDates.join(' · ')}</p>
            </div>
          )}
        </Card>

        <Card title="Dars yuritish sifati" sub="O'tilgan darslarning nechasida to'ldirilgan">
          <div className="divide-y divide-slate-50">
            <PctRow label="Mavzu yozilgan" value={m.topicPct} />
            <PctRow label="Uy vazifa berilgan" value={m.homeworkPct} />
            <PctRow label="Davomat olingan" value={m.attendanceTakenPct} />
            <PctRow label="Dastur qamrovi" value={curriculumPct} hint={`${m.curriculumCovered}/${m.curriculumTotal} band`} />
          </div>
          <div className="mt-3 grid grid-cols-2 gap-2">
            <MiniStat label="Qo'yilgan baholar" value={m.gradesCount} />
            <MiniStat
              label="Dastur tugashi"
              value={m.curriculumFinishDate ? formatDate(m.curriculumFinishDate) : '—'}
              hint={m.curriculumRemaining > 0 ? `${m.curriculumRemaining} band qoldi` : undefined}
            />
          </div>
        </Card>
      </div>

      {/* ---------- 5. O'ZLASHTIRISH ---------- */}
      <div className="grid gap-4 lg:grid-cols-2">
        <Card title="O'zlashtirish dinamikasi" sub="Oyma-oy o'rtacha baho">
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

        <Card title="Uy vazifa, xulq va ball">
          <div className="grid grid-cols-2 gap-3">
            <MiniStat label="O'rtacha ball" value={m.avgBall || '—'} />
            <MiniStat
              label="Baho (shu oy)" value={m.avgGradeThisMonth || '—'}
              hint={`o'tgan oy: ${m.avgGradePrevMonth || '—'}`}
            />
            <MiniStat label="Uy vazifa qildi" value={m.homeworkDone} hint={`qilmadi: ${m.homeworkMissed}`} />
            <MiniStat label="Xulq: yaxshi" value={m.behaviorGood} hint={`yomon: ${m.behaviorBad}`} />
          </div>
        </Card>
      </div>

      {/* ---------- 6. IMTIHONLAR ---------- */}
      <Card
        title="Imtihonlar / testlar"
        sub={m.testCount > 0 ? `${m.testCount} ta test · o'rtacha ${m.testAvgPct}%` : undefined}
        tight
      >
        {m.tests.length === 0 ? (
          <div className="px-4 py-10 text-center text-sm text-slate-400">
            Oxirgi 12 oyda test o'tkazilmagan.
          </div>
        ) : (
          <div className="overflow-x-auto">
            <table className="w-full text-left text-sm">
              <thead>
                <tr className="border-b border-slate-100 bg-slate-50/60 text-xs uppercase tracking-wide text-slate-400">
                  <th className="px-4 py-3 font-medium">Test</th>
                  <th className="px-4 py-3 font-medium">Sana</th>
                  <th className="px-4 py-3 font-medium">Turi</th>
                  <th className="px-4 py-3 text-right font-medium">Maks</th>
                  <th className="px-4 py-3 text-right font-medium">Baholangan</th>
                  <th className="px-4 py-3 text-right font-medium">O'rtacha</th>
                </tr>
              </thead>
              <tbody>
                {m.tests.map((t) => (
                  <tr key={t.id} className="border-b border-slate-50 last:border-0 hover:bg-slate-50/40">
                    <td className="px-4 py-2.5 font-medium text-slate-700">{t.name}</td>
                    <td className="px-4 py-2.5 text-slate-500">{formatDate(t.date)}</td>
                    <td className="px-4 py-2.5">
                      <span className={cn(
                        'rounded px-1.5 py-0.5 text-[10px] font-semibold',
                        t.mode === 'online' ? 'bg-violet-50 text-violet-600' : 'bg-slate-100 text-slate-500',
                      )}>
                        {t.mode === 'online' ? 'ONLAYN' : 'OFLAYN'}
                      </span>
                    </td>
                    <td className="px-4 py-2.5 text-right font-mono text-slate-600">{t.maxScore}</td>
                    <td className={cn(
                      'px-4 py-2.5 text-right font-mono',
                      t.scored < t.studentCount ? 'text-amber-600' : 'text-slate-600',
                    )}>
                      {t.scored}/{t.studentCount}
                    </td>
                    <td className={cn(
                      'px-4 py-2.5 text-right font-mono font-semibold',
                      t.avgPct >= 80 ? 'text-emerald-700' : t.avgPct >= 50 ? 'text-amber-600' : 'text-red-500',
                    )}>
                      {t.scored > 0 ? `${t.avgPct}%` : '—'}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </Card>

      {/* ---------- 7. TO'LOVLAR (faqat moliya ruxsati bo'lsa) ---------- */}
      {m.financeIncluded && (
        <div className="grid gap-4 lg:grid-cols-2">
          <Card title="To'lov holati" sub="Davr bo'yicha hisoblangan va yig'ilgan">
            <div className="grid grid-cols-2 gap-3">
              <MiniStat label="Hisoblangan" value={formatMoney(m.billed)} />
              <MiniStat label="Yig'ilgan" value={formatMoney(m.collected)} hint={`${m.collectionPct}%`} />
              <MiniStat label="Qarzdorlik" value={formatMoney(m.debt)} />
              <MiniStat label="To'lagan / to'lamagan" value={`${m.paidCount} / ${m.unpaidCount}`} />
            </div>
            <div className="mt-3">
              <PctRow label="Yig'ilish darajasi" value={m.collectionPct} />
            </div>
          </Card>

          <Card title="Oyma-oy to'lov" sub="Hisoblangan va yig'ilgan summa">
            <div className="h-56 w-full">
              <ResponsiveContainer width="100%" height="100%">
                <BarChart data={payData} margin={{ top: 6, right: 6, left: -12, bottom: 0 }}>
                  <CartesianGrid strokeDasharray="3 3" stroke="#f1f5f9" vertical={false} />
                  <XAxis dataKey="name" tick={{ fontSize: 11, fill: '#94a3b8' }} axisLine={false} tickLine={false} />
                  <YAxis
                    tick={{ fontSize: 10, fill: '#cbd5e1' }} axisLine={false} tickLine={false}
                    tickFormatter={(v: number) => (v >= 1_000_000 ? `${Math.round(v / 1_000_000)}mln` : `${Math.round(v / 1000)}k`)}
                  />
                  <Tooltip
                    contentStyle={{ fontSize: 12, borderRadius: 10, border: '1px solid #e2e8f0' }}
                    formatter={(v) => formatMoney(Number(v))}
                  />
                  <Bar dataKey="Hisoblangan" fill="#cbd5e1" radius={[4, 4, 0, 0]} />
                  <Bar dataKey="Yig'ilgan" fill="#10b981" radius={[4, 4, 0, 0]} />
                </BarChart>
              </ResponsiveContainer>
            </div>
          </Card>
        </div>
      )}

      {/* ---------- 8. O'QUVCHILAR KESIMI ---------- */}
      {m.students.length > 0 && (
        <Card title="O'quvchilar kesimi" sub="Ball bo'yicha saralangan" tight>
          <div className="overflow-x-auto">
            <table className="w-full text-left text-sm">
              <thead>
                <tr className="border-b border-slate-100 bg-slate-50/60 text-xs uppercase tracking-wide text-slate-400">
                  <th className="px-4 py-3 font-medium">O'quvchi</th>
                  <th className="px-4 py-3 font-medium">Holat</th>
                  <th className="px-4 py-3 text-right font-medium">Ball</th>
                  <th className="px-4 py-3 text-right font-medium">O'rt. baho</th>
                  <th className="px-4 py-3 text-right font-medium">Davomat</th>
                  <th className="px-4 py-3 text-right font-medium">Qoldirgan</th>
                  {m.financeIncluded && <th className="px-4 py-3 text-right font-medium">Qarz</th>}
                </tr>
              </thead>
              <tbody>
                {m.students.map((s) => (
                  <tr key={s.studentId} className="border-b border-slate-50 last:border-0 hover:bg-slate-50/40">
                    <td className="px-4 py-2.5 font-medium text-slate-700">{s.fullName}</td>
                    <td className="px-4 py-2.5">
                      <span className={cn(
                        'inline-flex items-center gap-1 rounded px-1.5 py-0.5 text-[10px] font-medium',
                        s.status === 'active' ? 'bg-emerald-50 text-emerald-700'
                          : s.status === 'frozen' ? 'bg-sky-50 text-sky-700' : 'bg-amber-50 text-amber-700',
                      )}>
                        {s.status === 'frozen' && <Snowflake className="h-3 w-3" />}
                        {statusLabel[s.status] ?? s.status}
                      </span>
                    </td>
                    <td className="px-4 py-2.5 text-right font-mono font-semibold text-slate-700">{s.ball}</td>
                    <td className="px-4 py-2.5 text-right font-mono text-slate-600">{s.avgGrade || '—'}</td>
                    <td className="px-4 py-2.5 text-right">
                      {s.attendancePct == null ? (
                        <span className="text-slate-300">—</span>
                      ) : (
                        <span className={cn(
                          'font-mono font-semibold',
                          s.attendancePct >= 90 ? 'text-emerald-700'
                            : s.attendancePct >= 75 ? 'text-amber-600' : 'text-red-500',
                        )}>
                          {s.attendancePct}%
                        </span>
                      )}
                    </td>
                    <td className="px-4 py-2.5 text-right font-mono text-slate-500">{s.absent || '—'}</td>
                    {m.financeIncluded && (
                      <td className={cn(
                        'px-4 py-2.5 text-right font-mono',
                        s.debt > 0 ? 'text-red-500' : 'text-slate-300',
                      )}>
                        {s.debt > 0 ? formatMoney(s.debt) : '—'}
                      </td>
                    )}
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </Card>
      )}

      {/* ---------- 9. AI NARRATIVI ---------- */}
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
              <TextBlock title="Davomat" text={shown.ai.davomat} />
              <TextBlock title="Muzlatish va ketish" text={shown.ai.oqim} />
              <TextBlock title="O'zlashtirish" text={shown.ai.ozlashtirish} />
              <TextBlock title="Imtihonlar" text={shown.ai.imtihonlar} />
              <TextBlock title="To'lovlar" text={shown.ai.tolovlar} />
              <TextBlock title="Jurnal intizomi" text={shown.ai.jurnal} />
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
              <ClipboardList className="h-9 w-9 text-brand-300" />
              <p className="max-w-md text-sm">
                Bu guruh hali AI orqali tahlil qilinmagan. <b className="text-slate-600">"Tahlil qilish"</b>{' '}
                tugmasini bosing — AI yuqoridagi barcha raqamlarni (davomat, ketish, imtihon, to'lov,
                jurnal) o'rganib, tanqidiy xulosa, xavflar va tavsiyalarni chiqaradi.
              </p>
            </div>
          </Card>
        )
      )}
    </div>
  )
}
