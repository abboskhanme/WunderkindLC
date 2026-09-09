import { useCallback, useEffect, useState } from 'react'
import { Link } from 'react-router-dom'
import { Check, Download, Lock, LockOpen, TriangleAlert } from 'lucide-react'
import { Card } from '@/components/ui/Card'
import { Badge } from '@/components/ui/Badge'
import { Button } from '@/components/ui/Button'
import { Loader } from '@/components/ui/Loader'
import { usePerm } from '@/lib/permissions'
import { formatMonth } from '@/config/constants'
import { cn, apiErrorMessage, formatMoney } from '@/lib/utils'
import {
  confirmKpiMonth,
  exportKpiClose,
  getKpiClose,
  reopenKpiMonth,
  type KpiCloseDto,
  type KpiMonthDto,
  type KpiUnitEconomicsDto,
} from '@/api/services/kpi'
import { formatPercent, isPastMonth, roleLabel, useKpiParams } from './model'
import { EmptyState, ErrorBanner, KpiShell, MonthPicker } from './shared'

/**
 * «YOPISH» — oyni yakunlash sahifasi.
 *
 * <p>Har xodim bo'yicha hisoblangan oylik, tasdiqlash/qayta ochish tugmalari, birlik
 * iqtisodiyotining uch sog'lomlik indikatori (3% / 5% / 10%) va Excel eksporti.</p>
 *
 * <p>⚠️ TASDIQLASH — MUZLATISH: undan keyin qoida yoki oklad o'zgarsa ham o'sha oyning
 * summasi o'zgarmaydi (`KpiMonthResult` `RuleSetId` bilan saqlanadi). Shuning uchun
 * tugma har doim tasdiq so'raydi.</p>
 *
 * <p>⚠️ YUQORI CHEGARADAN OSHGAN qator ALOHIDA, ikkinchi tasdiq talab qiladi: summa
 * kesilmaydi (`.claude/rules/kpi.md` §3), ya'ni chegaradan oshgan oylik faqat rahbarning
 * ONGLI qarori bilan muzlatilishi kerak.</p>
 */
export function KpiClosePage() {
  const { can } = usePerm()
  const canClose = can('kpi.close', 'edit')

  const { month, setMonth } = useKpiParams()
  const [data, setData] = useState<KpiCloseDto | null>(null)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState<string | null>(null)
  const [exporting, setExporting] = useState(false)
  const [tick, setTick] = useState(0)

  useEffect(() => {
    let alive = true
    getKpiClose(month)
      .then((d) => {
        if (!alive) return
        setData(d)
        setError(null)
        setLoading(false)
      })
      .catch((err) => {
        if (!alive) return
        setError(apiErrorMessage(err, 'Oy natijalarini yuklab bo‘lmadi'))
        setLoading(false)
      })
    return () => {
      alive = false
    }
  }, [month, tick])

  const reload = useCallback(() => setTick((t) => t + 1), [])

  const confirmRow = async (row: KpiMonthDto) => {
    if (
      !confirm(
        `${row.userName} — ${formatMonth(row.month)}: ${formatMoney(row.salary)}\n\n` +
          'Natija MUZLATILADI: shundan keyin qoida yoki oklad o‘zgarsa ham bu oy summasi ' +
          'o‘zgarmaydi. Tasdiqlansinmi?',
      )
    )
      return

    // ⚠️ IKKINCHI tasdiq — faqat chegaradan oshgan qator uchun. Summa kesilmagani sababli
    // bu qaror ONGLI bo'lishi kerak; bitta "OK" bilan o'tib ketmasin.
    if (
      row.capExceeded &&
      !confirm(
        `DIQQAT: bu oylik yuqori chegaradan${row.cap != null ? ` (${formatMoney(row.cap)})` : ''} ` +
          `OSHIB ketgan — ${formatMoney(row.salary)}. Summa avtomatik KESILMAYDI.\n\n` +
          'Chegaradan oshgan summani shu holicha tasdiqlaysizmi?',
      )
    )
      return

    setBusy(row.userId)
    try {
      // Chegaradan oshgani yuqorida ONGLI tasdiqlandi — server ham shu bayroqni talab qiladi.
      await confirmKpiMonth(row.userId, month, row.capExceeded)
      reload()
    } catch (err) {
      alert(apiErrorMessage(err, 'Oyni yopib bo‘lmadi'))
    } finally {
      setBusy(null)
    }
  }

  const reopenRow = async (row: KpiMonthDto) => {
    if (
      !confirm(
        `${row.userName} — ${formatMonth(row.month)} qayta ochilsinmi? Muzlatilgan natija ` +
          'bekor qilinadi va summa hozirgi ma’lumot bo‘yicha QAYTA hisoblanadi.',
      )
    )
      return
    setBusy(row.userId)
    try {
      await reopenKpiMonth(row.userId, month)
      reload()
    } catch (err) {
      alert(apiErrorMessage(err, 'Qayta ochib bo‘lmadi'))
    } finally {
      setBusy(null)
    }
  }

  const exportExcel = async () => {
    setExporting(true)
    try {
      await exportKpiClose(month)
    } catch (err) {
      alert(apiErrorMessage(err, 'Excel faylni yuklab bo‘lmadi'))
    } finally {
      setExporting(false)
    }
  }

  const rows = data?.rows ?? []
  const total = rows.reduce((s, r) => s + r.salary, 0)
  const confirmedCount = rows.filter((r) => r.status === 'confirmed').length
  const notFinished = !isPastMonth(month)

  return (
    <KpiShell
      sub="Oylik natijani tasdiqlash va muzlatish"
      actions={
        <>
          <MonthPicker month={month} onChange={setMonth} />
          <Button variant="secondary" onClick={exportExcel} disabled={exporting || rows.length === 0}>
            <Download className="h-4 w-4" />
            {exporting ? 'Tayyorlanmoqda...' : 'Excel'}
          </Button>
        </>
      }
    >
      {error && <ErrorBanner text={error} onRetry={reload} />}

      {notFinished && (
        <div className="mb-4 flex items-start gap-2 rounded-xl border border-amber-200 bg-amber-50 px-4 py-3 text-[12.5px] text-amber-800">
          <TriangleAlert className="mt-0.5 h-4 w-4 shrink-0" />
          <span>
            <b>{formatMonth(month)} hali tugamagan.</b> Raqamlar oy oxirigacha o'zgaradi —
            tasdiqlash odatda oy yopilgandan keyin qilinadi. Hozir tasdiqlasangiz, natija
            SHU KUNGI ma'lumot bilan muzlatiladi.
          </span>
        </div>
      )}

      {loading && !data ? (
        <Loader label="Hisoblanmoqda..." />
      ) : (
        <div className="space-y-5">
          {/* ---------- Xodimlar ---------- */}
          <Card
            tight
            title={`${formatMonth(month)} — xodimlar`}
            sub={
              rows.length === 0
                ? undefined
                : `${confirmedCount} / ${rows.length} tasdiqlangan · jami ${formatMoney(total)}`
            }
          >
            {rows.length === 0 ? (
              <EmptyState
                title="Bu oyda KPI xodimi yo'q"
                hint="«Qoidalar» sahifasida xodimlarga rol va oklad biriktiring."
              />
            ) : (
              <div className="divide-y divide-slate-100">
                {rows.map((r) => (
                  <CloseRow
                    key={r.userId}
                    row={r}
                    canClose={canClose}
                    busy={busy === r.userId}
                    onConfirm={() => confirmRow(r)}
                    onReopen={() => reopenRow(r)}
                  />
                ))}
              </div>
            )}
          </Card>

          {/* ---------- Birlik iqtisodiyoti ---------- */}
          {data?.unit ? (
            <UnitEconomics unit={data.unit} />
          ) : (
            <Card title="Birlik iqtisodiyoti" sub="Bonuslar markaz marjasiga sig'yaptimi">
              <EmptyState
                title="Indikatorlar hisoblanmadi"
                hint="Kurs narxi va o'qituvchi ulushi «Qoidalar» sahifasidagi birlik iqtisodiyoti maydonlaridan olinadi."
              />
            </Card>
          )}
        </div>
      )}
    </KpiShell>
  )
}

/** Bitta xodimning yopish qatori. */
function CloseRow({
  row,
  canClose,
  busy,
  onConfirm,
  onReopen,
}: {
  row: KpiMonthDto
  canClose: boolean
  busy: boolean
  onConfirm: () => void
  onReopen: () => void
}) {
  const confirmed = row.status === 'confirmed'
  return (
    <div
      className={cn(
        'flex flex-wrap items-center justify-between gap-3 px-[18px] py-3.5',
        row.capExceeded && !confirmed && 'bg-amber-50/50',
      )}
    >
      <div className="min-w-0 flex-1">
        <div className="flex flex-wrap items-center gap-2">
          <Link
            to={`/admin/boshqaruv/kpi/oy?user=${row.userId}&month=${row.month}`}
            className="text-sm font-bold text-slate-800 hover:text-brand-600 hover:underline"
          >
            {row.userName}
          </Link>
          <Badge tone="violet">{row.roleLabel || roleLabel(row.roleCode)}</Badge>
          {confirmed ? (
            <Badge tone="green">
              <Lock className="h-3 w-3" />
              Muzlatilgan
            </Badge>
          ) : (
            <Badge tone="default">Qoralama</Badge>
          )}
          {row.guaranteeApplied && <Badge tone="blue">kafolat</Badge>}
          {row.capExceeded && (
            <Badge tone="amber">
              <TriangleAlert className="h-3 w-3" />
              chegaradan oshdi
            </Badge>
          )}
          {row.snapshotMissing && <Badge tone="amber">snapshot yo'q</Badge>}
        </div>
        <p className="mt-1 text-[11.5px] text-slate-400">
          Oklad {formatMoney(row.baseSalary)} · bonus {formatMoney(row.bonusTotal)} · jarima{' '}
          {row.fineTotal > 0 ? `−${formatMoney(row.fineTotal)}` : '0'}
          {confirmed && row.confirmedBy && ` · tasdiqladi: ${row.confirmedBy}`}
        </p>
        {row.capExceeded && !confirmed && (
          <p className="mt-1 text-[11.5px] font-semibold text-amber-700">
            Summa avtomatik KESILMAYDI — tasdiqlashda alohida ogohlantirish chiqadi.
          </p>
        )}
      </div>

      <div className="flex shrink-0 items-center gap-3">
        <span className="font-mono text-[19px] font-semibold text-slate-900">
          {formatMoney(row.salary)}
        </span>
        {canClose &&
          (confirmed ? (
            <Button variant="secondary" onClick={onReopen} disabled={busy}>
              <LockOpen className="h-4 w-4" />
              Qayta ochish
            </Button>
          ) : (
            <Button onClick={onConfirm} disabled={busy}>
              <Check className="h-4 w-4" />
              {busy ? 'Saqlanmoqda...' : 'Tasdiqlash'}
            </Button>
          ))}
      </div>
    </div>
  )
}

/**
 * BIRLIK IQTISODIYOTI — uchta sog'lomlik indikatori.
 *
 * <p>Savol: <b>"bonuslar markazning marjasiga SIG'YAPTIMI"</b>. Chegara qoidalarda
 * belgilangan (3% Bonus A, 5% Bonus B, 10% umumiy oyliklar). Bu tekshiruv oyni
 * yopishdan OLDIN turadi: modul xodimni rag'batlantiradi, lekin markazni zarar
 * qilishga olib bormasligi kerak.</p>
 */
function UnitEconomics({ unit }: { unit: KpiUnitEconomicsDto }) {
  return (
    <Card
      title="Birlik iqtisodiyoti"
      sub={`Kurs narxi ${formatMoney(unit.coursePrice)} · o'qituvchi ulushi ${formatPercent(unit.teacherShare, 0)} · bir o'quvchidan marja ${formatMoney(unit.marginPerStudent)}`}
    >
      <div className="grid gap-3 lg:grid-cols-3">
        <HealthCard
          title="Bonus A — faol o'quvchi"
          value={unit.bonusAShare}
          max={unit.bonusAMax}
          ok={unit.bonusAOk}
          detail={`Birlik ${formatMoney(unit.bonusAUnit)} · ${unit.activeStudents} faol o'quvchi`}
          basis={`Markaz marjasi: ${formatMoney(unit.centerMargin)}`}
        />
        <HealthCard
          title="Bonus B — uzaytirish"
          value={unit.bonusBShare}
          max={unit.bonusBMax}
          ok={unit.bonusBOk}
          detail={`Birlik ${formatMoney(unit.bonusBUnit)}`}
          basis={`Uzaytirish marjasi: ${formatMoney(unit.extensionMargin)}`}
        />
        <HealthCard
          title="Umumiy oyliklar"
          value={unit.salaryShare}
          max={unit.salaryMax}
          ok={unit.salaryOk}
          detail={`Jami: ${formatMoney(unit.totalSalaries)}`}
          basis={`Markaz marjasi: ${formatMoney(unit.centerMargin)}`}
        />
      </div>

      <p className="mt-4 text-[11.5px] text-slate-400">
        Chegaralar «Qoidalar» sahifasidagi birlik iqtisodiyoti maydonlaridan olinadi va
        keyingi oydan kuchga kiradigan yangi versiya bilan o'zgartiriladi. Indikator qizil
        bo'lsa — bonus birligi yoki oklad qayta ko'rib chiqilishi kerak.
      </p>
    </Card>
  )
}

function HealthCard({
  title,
  value,
  max,
  ok,
  detail,
  basis,
}: {
  title: string
  value: number
  max: number
  ok: boolean
  detail: string
  basis: string
}) {
  const pct = max <= 0 ? 0 : Math.min(100, (value / max) * 100)
  return (
    <div
      className={cn(
        'rounded-xl border p-4',
        ok ? 'border-emerald-200 bg-emerald-50/50' : 'border-red-200 bg-red-50/50',
      )}
    >
      <div className="flex items-start justify-between gap-2">
        <span className="text-xs font-semibold uppercase tracking-wide text-slate-500">
          {title}
        </span>
        <Badge tone={ok ? 'green' : 'red'}>{ok ? 'normada' : 'oshib ketdi'}</Badge>
      </div>
      <div className="mt-2 flex items-baseline gap-2">
        <span
          className={cn(
            'font-mono text-[26px] font-semibold leading-none',
            ok ? 'text-emerald-700' : 'text-red-700',
          )}
        >
          {formatPercent(value)}
        </span>
        <span className="font-mono text-[13px] text-slate-400">
          / {formatPercent(max, 0)} chegara
        </span>
      </div>
      <div className="mt-2.5 h-1.5 overflow-hidden rounded-full bg-white">
        <div
          className={cn('h-full rounded-full', ok ? 'bg-emerald-500' : 'bg-red-500')}
          style={{ width: `${pct}%` }}
        />
      </div>
      <p className="mt-2 text-[11.5px] text-slate-500">{detail}</p>
      <p className="text-[11.5px] text-slate-400">{basis}</p>
    </div>
  )
}
