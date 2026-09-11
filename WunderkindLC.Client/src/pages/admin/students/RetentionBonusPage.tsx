import { useEffect, useMemo, useState } from 'react'
import { Link } from 'react-router-dom'
import {
  AlertTriangle,
  Award,
  CheckCircle2,
  Download,
  Hourglass,
  RefreshCw,
  RotateCcw,
  Search,
  Settings2,
  Undo2,
  Wallet,
} from 'lucide-react'
import {
  getRetentionReport,
  exportRetentionReport,
  restartRetentionCycle,
  cancelRetentionBonus,
  type RetentionReport,
  type RetentionRow,
  type RetentionState,
  type RetentionStatus,
} from '@/api/services/retentionBonus'
import { apiErrorMessage, cn, formatMoney } from '@/lib/utils'
import { usePerm } from '@/lib/permissions'
import { Card } from '@/components/ui/Card'
import { Badge, type BadgeTone } from '@/components/ui/Badge'
import { Button } from '@/components/ui/Button'
import { Input } from '@/components/ui/Input'
import { Loader } from '@/components/ui/Loader'
import { Modal } from '@/components/ui/Modal'
import { PageHeader } from '@/components/ui/PageHeader'
import { StatCard } from '@/components/ui/StatCard'
import { TablePagination, usePagination } from '@/components/ui/TablePagination'
import { GiveRetentionBonusModal } from './GiveRetentionBonusModal'
import { RetentionSettingsModal } from './RetentionSettingsModal'

type Chip = 'all' | 'progress' | 'ready' | 'broken' | 'blocked' | 'given'

const CHIPS: { key: Chip; label: string }[] = [
  { key: 'all', label: 'Hammasi' },
  { key: 'ready', label: 'Tayyor' },
  { key: 'progress', label: "Yo'lda" },
  { key: 'broken', label: 'Uzilgan' },
  { key: 'blocked', label: 'Bonus berilgan' },
  { key: 'given', label: 'Berilgan' },
]

/** Oy katagining belgisi va tushuntirishi (jadval ustidagi izohda ham shu ishlatiladi). */
const STATE_UI: Record<RetentionState, { icon: string; title: string; cls: string }> = {
  paid: { icon: '✅', title: "To'liq — o'qidi va to'ladi", cls: 'bg-emerald-50 text-emerald-700' },
  debt: { icon: '⏳', title: "Qarzdor — o'qidi, lekin to'lov hali yo'q (sikl uzilmaydi)", cls: 'bg-amber-50 text-amber-700' },
  nocharge: { icon: '📄', title: 'Hisob yozilmagan — sanoqqa kirmaydi (sikl uzilmaydi)', cls: 'bg-violet-50 text-violet-700' },
  frozen: { icon: '❄️', title: "Muzlatilgan — sanoq to'xtaydi, oyna cho'ziladi", cls: 'bg-sky-50 text-sky-700' },
  gone: { icon: '🚪', title: "A'zolik yo'q — sanoq to'xtaydi", cls: 'bg-slate-100 text-slate-500' },
}

const STATUS_UI: Record<RetentionStatus, { label: string; tone: BadgeTone }> = {
  ready: { label: 'Tayyor', tone: 'green' },
  progress: { label: "Yo'lda", tone: 'default' },
  broken: { label: 'Uzildi', tone: 'red' },
  notstarted: { label: 'Boshlanmagan', tone: 'amber' },
  blocked: { label: 'Bonus berilgan', tone: 'amber' },
}

const control =
  'rounded-lg border border-slate-200 bg-white px-3 py-2 text-sm text-slate-700 outline-none transition-colors focus:border-brand-400 focus:ring-2 focus:ring-brand-100'

/** Qatorda ko'rsatiladigan oxirgi N oy — jadval juda uzun bo'lib ketmasin. */
const MAX_CELLS = 14

/** Qator kaliti — sikl har FAN uchun alohida yuritiladi. */
const rowKey = (r: RetentionRow) => `${r.studentId}:${r.courseId}`

export function RetentionBonusPage() {
  const { can } = usePerm()
  // Bonus — MOLIYA bo'limining sahifasi (pul beradigan amal), menyudagi joyi esa
  // o'quvchilar bilan. `finance` (butun bo'lim) ruxsati ham merosi bilan shu yerga yetadi.
  const canEdit = can('finance.bonus', 'edit')

  const [report, setReport] = useState<RetentionReport | null>(null)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState('')
  const [chip, setChip] = useState<Chip>('all')
  const [search, setSearch] = useState('')
  const [giveFor, setGiveFor] = useState<RetentionRow | null>(null)
  const [restartFor, setRestartFor] = useState<RetentionRow | null>(null)
  const [cancelFor, setCancelFor] = useState<{ awardId: string; label: string } | null>(null)
  const [settingsOpen, setSettingsOpen] = useState(false)

  const load = () => {
    setLoading(true)
    setError('')
    getRetentionReport()
      .then(setReport)
      .catch((err) => setError(apiErrorMessage(err, "Bonus hisobotini yuklab bo'lmadi")))
      .finally(() => setLoading(false))
  }

  useEffect(load, [])

  const rows = report?.rows ?? []

  const filtered = useMemo(() => {
    const q = search.trim().toLowerCase()
    return rows.filter((r) => {
      if (chip === 'given' && r.awards.filter((a) => a.status === 'given').length === 0) return false
      if (chip !== 'all' && chip !== 'given' && r.status !== chip) return false
      if (
        q &&
        !r.fullName.toLowerCase().includes(q) &&
        !r.groups.some((g) => g.name.toLowerCase().includes(q)) &&
        !r.teachers.some((t) => t.name.toLowerCase().includes(q)) &&
        !r.courseName.toLowerCase().includes(q)
      )
        return false
      return true
    })
  }, [rows, chip, search])

  // Sahifalash — jadval uzun bo'lgani uchun (har o'quvchi × fan alohida qator).
  // Filtr/qidiruv o'zgarsa hook o'zi 1-sahifaga qaytaradi.
  const pg = usePagination(filtered)

  // Sanoq FAN kesimida: bitta o'quvchi ikki fanga qatnasa ikki marta hisoblanadi.
  const totals = useMemo(() => {
    const given = rows.flatMap((r) => r.awards).filter((a) => a.status === 'given')
    return {
      ready: rows.filter((r) => r.status === 'ready').length,
      progress: rows.filter((r) => r.status === 'progress').length,
      givenCount: given.length,
      givenSum: given.reduce((s, a) => s + a.totalAmount, 0),
    }
  }, [rows])

  return (
    <div className="space-y-5">
      <PageHeader
        title="Bonus hisoboti"
        sub={
          report
            ? `Har FAN alohida sanaladi: o'quvchi ${report.settings.monthsRequired} oy uzluksiz o'qib to'lasa — uni o'qitgan o'qituvchilarga bonus. Tanaffusga ruxsat: ${report.settings.maxGapMonths} oy.`
            : "O'quvchini ushlab turgan o'qituvchilarni rag'batlantirish."
        }
        actions={
          <>
            <Button variant="ghost" onClick={load} disabled={loading}>
              <RefreshCw className={cn('h-4 w-4', loading && 'animate-spin')} />
              Yangilash
            </Button>
            <Button variant="ghost" onClick={() => void exportRetentionReport()}>
              <Download className="h-4 w-4" />
              Excel
            </Button>
            {canEdit && (
              <Button variant="ghost" onClick={() => setSettingsOpen(true)}>
                <Settings2 className="h-4 w-4" />
                Sozlamalar
              </Button>
            )}
          </>
        }
      />

      {error && (
        <div className="rounded-lg border border-rose-200 bg-rose-50 px-4 py-3 text-sm text-rose-700">
          {error}
        </div>
      )}

      <div className="grid grid-cols-2 gap-3 lg:grid-cols-4">
        <StatCard label="Bonusga tayyor (fan)" value={totals.ready} icon={Award} />
        <StatCard label="Yo'ldagi sikl (fan)" value={totals.progress} icon={Hourglass} />
        <StatCard label="Berilgan bonuslar" value={totals.givenCount} icon={CheckCircle2} />
        <StatCard label="Berilgan summa" value={formatMoney(totals.givenSum)} icon={Wallet} />
      </div>

      <Card className="p-4">
        <div className="flex flex-wrap items-center gap-2">
          {CHIPS.map((c) => (
            <button
              key={c.key}
              type="button"
              onClick={() => setChip(c.key)}
              className={cn(
                'rounded-full px-3 py-1.5 text-sm font-medium transition-colors',
                chip === c.key
                  ? 'bg-brand-500 text-white'
                  : 'bg-slate-100 text-slate-600 hover:bg-slate-200',
              )}
            >
              {c.label}
            </button>
          ))}
          <div className="relative ml-auto">
            <Search className="pointer-events-none absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-slate-400" />
            <input
              className={cn(control, 'pl-9')}
              placeholder="F.I.Sh, fan, guruh yoki o'qituvchi..."
              value={search}
              onChange={(e) => setSearch(e.target.value)}
            />
          </div>
        </div>

        <p className="mt-3 text-xs text-slate-400">
          {(Object.keys(STATE_UI) as RetentionState[]).map((s) => (
            <span key={s} className="mr-4 inline-block">
              {STATE_UI[s].icon} {STATE_UI[s].title}
            </span>
          ))}
        </p>
        <p className="mt-1 text-xs text-slate-400">
          Sikl har fan uchun alohida: bir o'quvchi ikki fanga qatnasa — ikkita qator. Bir
          o'qituvchi bitta o'quvchi orqali <b>umr bo'yi bir marta</b> bonus oladi.
        </p>
      </Card>

      {loading ? (
        <Loader />
      ) : filtered.length === 0 ? (
        <Card className="p-10 text-center text-sm text-slate-400">
          {rows.length === 0 ? (
            <>
              Hali birorta o'quvchida bonus ptichkasi yoqilmagan. O'quvchi formasidagi
              «Ushlab turish bonusi» bo'limidan yoqing.
            </>
          ) : (
            'Filtrga mos o’quvchi topilmadi.'
          )}
        </Card>
      ) : (
        <Card className="overflow-x-auto">
          <table className="w-full min-w-[1000px] text-sm">
            <thead className="border-b border-slate-100 text-left text-xs uppercase tracking-wide text-slate-400">
              <tr>
                <th className="px-4 py-3 w-12">№</th>
                <th className="px-4 py-3">F.I.Sh</th>
                <th className="px-4 py-3">Guruh</th>
                <th className="px-4 py-3">O'qituvchi</th>
                <th className="px-4 py-3">Oylar</th>
                <th className="px-4 py-3">Sanoq</th>
                <th className="px-4 py-3">Holat</th>
                <th className="px-4 py-3" />
              </tr>
            </thead>
            <tbody className="divide-y divide-slate-50">
              {pg.paged.map((r, i) => {
                const cells = r.months.slice(-MAX_CELLS)
                const hidden = r.months.length - cells.length
                const givenAwards = r.awards.filter((a) => a.status === 'given')
                return (
                  <tr key={rowKey(r)} className="align-top hover:bg-slate-50/60">
                    {/* Raqam UMUMIY ro'yxat bo'yicha (2-sahifada 21, 22… davom etadi). */}
                    <td className="px-4 py-3 text-slate-400">{pg.rangeFrom + i}</td>
                    <td className="px-4 py-3">
                      <Link
                        to={`/admin/students/${r.studentId}`}
                        className="font-medium text-slate-800 hover:text-brand-600 hover:underline"
                      >
                        {r.fullName}
                      </Link>
                      {r.isArchived && (
                        <Badge tone="red" className="ml-2">
                          arxivda
                        </Badge>
                      )}
                      {/* Fan ustuni alohida emas — qator (o'quvchi × fan) bo'lgani uchun fan nomi
                          shu yerda, sikl ma'lumoti bilan birga ko'rsatiladi. */}
                      <div className="text-xs text-slate-400">
                        {r.courseName || '—'}
                        {r.startMonth ? ` · ${r.cycleNo}-sikl · ${r.startMonth} dan` : ''}
                      </div>
                    </td>
                    <td className="px-4 py-3">
                      {r.groups.length === 0 ? (
                        <span className="text-slate-400">—</span>
                      ) : (
                        r.groups.map((g, gi) => (
                          <span key={g.id}>
                            {gi > 0 && ', '}
                            <Link
                              to={`/admin/classes/${g.id}`}
                              className="text-slate-600 hover:text-brand-600 hover:underline"
                            >
                              {g.name}
                            </Link>
                          </span>
                        ))
                      )}
                    </td>
                    <td className="px-4 py-3">
                      {r.teachers.length === 0 ? (
                        <span className="text-slate-400">—</span>
                      ) : (
                        r.teachers.map((t, ti) => (
                          <span key={t.id}>
                            {ti > 0 && ', '}
                            <Link
                              to={`/admin/teachers/${t.id}`}
                              className="text-slate-600 hover:text-brand-600 hover:underline"
                            >
                              {t.name}
                            </Link>
                          </span>
                        ))
                      )}
                    </td>
                    <td className="px-4 py-3">
                      {cells.length === 0 ? (
                        <span className="text-xs text-slate-400">—</span>
                      ) : (
                        <div className="flex flex-wrap items-start gap-1">
                          {hidden > 0 && (
                            <span className="self-center text-xs text-slate-400">+{hidden}…</span>
                          )}
                          {cells.map((m) => (
                            <div
                              key={m.month}
                              title={`${m.month} — ${STATE_UI[m.state].title}${
                                m.teacherName ? `\nO'qituvchi: ${m.teacherName}` : ''
                              }${
                                m.state === 'debt'
                                  ? `\nHisoblangan: ${formatMoney(m.charged)} · To'langan: ${formatMoney(m.paid)}`
                                  : ''
                              }`}
                              className={cn(
                                'w-14 rounded-md px-1 py-1 text-center',
                                STATE_UI[m.state].cls,
                              )}
                            >
                              <div className="text-sm leading-none">{STATE_UI[m.state].icon}</div>
                              <div className="mt-0.5 text-[10px] leading-tight opacity-70">
                                {m.month.slice(5)}
                              </div>
                              <div className="truncate text-[10px] leading-tight opacity-60">
                                {m.teacherName ? m.teacherName.split(' ')[0] : ''}
                              </div>
                            </div>
                          ))}
                        </div>
                      )}
                    </td>
                    <td className="px-4 py-3 whitespace-nowrap font-medium text-slate-700">
                      {r.counted}/{r.required}
                    </td>
                    <td className="px-4 py-3">
                      <Badge tone={STATUS_UI[r.status].tone}>{STATUS_UI[r.status].label}</Badge>
                      <div className="mt-1 max-w-[220px] text-xs text-slate-400">{r.statusNote}</div>
                      {givenAwards.map((a) => (
                        <div key={a.id} className="mt-1 text-xs text-emerald-700">
                          {a.cycleNo}-sikl: {formatMoney(a.totalAmount)}
                          {canEdit && (
                            <button
                              type="button"
                              className="ml-1 text-slate-400 hover:text-rose-600"
                              title="Bonusni bekor qilish"
                              onClick={() =>
                                setCancelFor({
                                  awardId: a.id,
                                  label: `${r.fullName} · ${r.courseName || a.courseName}`,
                                })
                              }
                            >
                              <Undo2 className="inline h-3 w-3" />
                            </button>
                          )}
                        </div>
                      ))}
                    </td>
                    <td className="px-4 py-3 text-right">
                      {canEdit && r.status === 'ready' && (
                        <Button className="whitespace-nowrap" onClick={() => setGiveFor(r)}>
                          <Award className="h-4 w-4" />
                          Bonus berish
                        </Button>
                      )}
                      {canEdit && (r.status === 'broken' || r.status === 'notstarted') && (
                        <Button
                          variant="ghost"
                          className="whitespace-nowrap"
                          onClick={() => setRestartFor(r)}
                        >
                          <RotateCcw className="h-4 w-4" />
                          {r.status === 'broken' ? 'Qayta boshlash' : 'Oyni belgilash'}
                        </Button>
                      )}
                    </td>
                  </tr>
                )
              })}
            </tbody>
          </table>
          <TablePagination {...pg} />
        </Card>
      )}

      {giveFor && report && (
        <GiveRetentionBonusModal
          row={giveFor}
          defaultAmount={report.settings.defaultAmount}
          onClose={() => setGiveFor(null)}
          onSaved={() => {
            setGiveFor(null)
            load()
          }}
        />
      )}

      {restartFor && (
        <RestartCycleModal
          row={restartFor}
          onClose={() => setRestartFor(null)}
          onSaved={() => {
            setRestartFor(null)
            load()
          }}
        />
      )}

      {cancelFor && (
        <CancelAwardModal
          awardId={cancelFor.awardId}
          label={cancelFor.label}
          onClose={() => setCancelFor(null)}
          onSaved={() => {
            setCancelFor(null)
            load()
          }}
        />
      )}

      {settingsOpen && (
        <RetentionSettingsModal
          onClose={() => setSettingsOpen(false)}
          onSaved={() => {
            setSettingsOpen(false)
            load()
          }}
        />
      )}
    </div>
  )
}

/** Siklni yangi oydan qayta boshlash — FAQAT tanlangan fan uchun. */
function RestartCycleModal({
  row,
  onClose,
  onSaved,
}: {
  row: RetentionRow
  onClose: () => void
  onSaved: () => void
}) {
  const [month, setMonth] = useState(row.startMonth || new Date().toISOString().slice(0, 7))
  const [saving, setSaving] = useState(false)
  const [error, setError] = useState('')

  const submit = async () => {
    if (!month) return
    setSaving(true)
    setError('')
    try {
      await restartRetentionCycle(row.studentId, row.courseId, month)
      onSaved()
    } catch (err) {
      setError(apiErrorMessage(err, "Qayta boshlab bo'lmadi"))
    } finally {
      setSaving(false)
    }
  }

  return (
    <Modal
      open
      onClose={onClose}
      title="Sanoqni qayta boshlash"
      size="sm"
      footer={
        <>
          <Button variant="secondary" onClick={onClose} disabled={saving}>
            Bekor
          </Button>
          <Button onClick={() => void submit()} disabled={saving || !month}>
            {saving ? 'Saqlanmoqda...' : 'Saqlash'}
          </Button>
        </>
      }
    >
      <div className="space-y-3">
        <div className="rounded-lg bg-slate-50 px-4 py-3 text-sm">
          <div className="font-semibold text-slate-800">{row.fullName}</div>
          <div className="text-slate-500">
            Fan: {row.courseName || '—'} · {row.cycleNo}-sikl
          </div>
        </div>

        <Input
          label="Sanoq qaysi oydan boshlansin"
          type="month"
          value={month}
          onChange={(e) => setMonth(e.target.value)}
        />
        <p className="text-xs text-slate-400">
          Faqat shu <b>fan</b> bo'yicha sanoq yangidan boshlanadi — o'quvchining boshqa fanlaridagi
          sikllarga ta'sir qilmaydi.
        </p>

        {error && (
          <div className="rounded-lg border border-rose-200 bg-rose-50 px-3 py-2 text-sm text-rose-700">
            {error}
          </div>
        )}
      </div>
    </Modal>
  )
}

/** Berilgan bonusni bekor qilish — sabab + qaytmas oqibat haqida ogohlantirish. */
function CancelAwardModal({
  awardId,
  label,
  onClose,
  onSaved,
}: {
  awardId: string
  label: string
  onClose: () => void
  onSaved: () => void
}) {
  const [reason, setReason] = useState('')
  const [saving, setSaving] = useState(false)
  const [error, setError] = useState('')

  const submit = async () => {
    setSaving(true)
    setError('')
    try {
      await cancelRetentionBonus(awardId, reason.trim() || undefined)
      onSaved()
    } catch (err) {
      setError(apiErrorMessage(err, "Bekor qilib bo'lmadi"))
    } finally {
      setSaving(false)
    }
  }

  return (
    <Modal
      open
      onClose={onClose}
      title="Bonusni bekor qilish"
      size="sm"
      footer={
        <>
          <Button variant="secondary" onClick={onClose} disabled={saving}>
            Yopish
          </Button>
          <Button variant="danger" onClick={() => void submit()} disabled={saving}>
            {saving ? 'Bekor qilinmoqda...' : 'Bekor qilish'}
          </Button>
        </>
      }
    >
      <div className="space-y-3">
        <div className="rounded-lg bg-slate-50 px-4 py-3 text-sm font-semibold text-slate-800">
          {label}
        </div>

        <div className="flex gap-2 rounded-lg bg-amber-50 px-3 py-2 text-xs text-amber-800">
          <AlertTriangle className="mt-0.5 h-4 w-4 shrink-0" />
          <span>
            Bekor qilingandan keyin bu o'quvchi orqali o'sha o'qituvchi(lar)ga <b>QAYTA bonus
            berib bo'lmaydi</b>. Sanoq ham qaytarilmaydi — bekor qilish faqat summani hisobdan
            chiqaradi.
          </span>
        </div>

        <Input
          label="Bekor qilish sababi (ixtiyoriy)"
          value={reason}
          onChange={(e) => setReason(e.target.value)}
          placeholder="masalan: xato kiritilgan"
        />

        {error && (
          <div className="rounded-lg border border-rose-200 bg-rose-50 px-3 py-2 text-sm text-rose-700">
            {error}
          </div>
        )}
      </div>
    </Modal>
  )
}
