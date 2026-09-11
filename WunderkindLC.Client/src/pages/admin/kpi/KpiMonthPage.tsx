import { useCallback, useEffect, useState, type ReactNode } from 'react'
import { Link } from 'react-router-dom'
import { Lock, ShieldCheck, TriangleAlert } from 'lucide-react'
import { Card } from '@/components/ui/Card'
import { Badge } from '@/components/ui/Badge'
import { Loader } from '@/components/ui/Loader'
import { formatMonth } from '@/config/constants'
import { cn, apiErrorMessage, formatMoney } from '@/lib/utils'
import { getKpiMonth, getKpiMonthAll, type KpiMonthDto } from '@/api/services/kpi'
import {
  formatPercent,
  isIntakeRole,
  roleLabel,
  useKpiParams,
  useKpiStaff,
} from './model'
import {
  CoefRow,
  EmptyState,
  ErrorBanner,
  InputRow,
  KpiShell,
  MoneyRow,
  MonthPicker,
  StaffPicker,
} from './shared'

/**
 * «OY» — Excel kalkulyatorining SAHIFA ko'rinishi, bitta xodim bo'yicha.
 *
 * <p>Beshta bo'lim, Excel varag'idagi tartibda: <b>1)</b> kirish raqamlari "qayerdan"
 * izohi bilan, <b>2)</b> konversiyalar, <b>3)</b> koeffitsientlar (qaysi pog'onaga tushdi
 * va NEGA), <b>4)</b> pul qatorlari va yakuniy oylik, <b>5)</b> reja bilan solishtirish
 * (faqat kiruvchi admin).</p>
 *
 * <p>⚠️ Hisob KLIENTDA TAKRORLANMAYDI — hamma raqam serverdan tayyor keladi
 * (`KpiCalculator`). Bu sahifa faqat KO'RSATADI: formulani ikki joyda yozsak, biri
 * o'zgarganda ikkinchisi jimgina boshqa summa chiqarardi.</p>
 */
export function KpiMonthPage() {
  const { userId, month, setUserId, setMonth } = useKpiParams()
  const { profiles, error: staffError } = useKpiStaff()
  const [view, setView] = useState<'one' | 'all'>('one')

  const [data, setData] = useState<KpiMonthDto | null>(null)
  const [rows, setRows] = useState<KpiMonthDto[]>([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const [tick, setTick] = useState(0)

  // ⚠️ Ikki so'rov ATAYIN alohida shoxda: `cond ? a() : b()` ikki xil `Promise` turining
  // birlashmasini beradi va `.then` ichida qaytgan qiymat turi yo'qolardi.
  useEffect(() => {
    let alive = true
    const fail = (err: unknown) => {
      if (!alive) return
      setError(apiErrorMessage(err, 'Oylik hisobni yuklab bo‘lmadi'))
      setLoading(false)
    }
    if (view === 'all') {
      getKpiMonthAll(month)
        .then((list) => {
          if (!alive) return
          setRows(list)
          setError(null)
          setLoading(false)
        })
        .catch(fail)
    } else {
      getKpiMonth(userId, month)
        .then((one) => {
          if (!alive) return
          setData(one)
          setError(null)
          setLoading(false)
        })
        .catch(fail)
    }
    return () => {
      alive = false
    }
  }, [userId, month, view, tick])

  const reload = useCallback(() => setTick((t) => t + 1), [])

  return (
    <KpiShell
      sub="Excel kalkulyatori: kirish raqamlari → koeffitsientlar → oylik"
      actions={
        <>
          <div className="inline-flex overflow-hidden rounded-lg border border-slate-200">
            <ViewButton active={view === 'one'} onClick={() => setView('one')}>
              Bitta xodim
            </ViewButton>
            <ViewButton active={view === 'all'} onClick={() => setView('all')}>
              Barchasi
            </ViewButton>
          </div>
          {view === 'one' && (
            <StaffPicker profiles={profiles} value={userId} onChange={setUserId} />
          )}
          <MonthPicker month={month} onChange={setMonth} />
        </>
      }
    >
      {staffError && <ErrorBanner text={staffError} />}
      {error && <ErrorBanner text={error} onRetry={reload} />}

      {loading && !data && rows.length === 0 ? (
        <Loader label="Hisoblanmoqda..." />
      ) : view === 'all' ? (
        <AllStaffTable rows={rows} month={month} />
      ) : !data ? (
        !error && (
          <Card>
            <EmptyState
              title="Bu xodimning KPI profili yo'q"
              hint="«Qoidalar» sahifasida xodimga rol va oklad biriktiring."
            />
          </Card>
        )
      ) : (
        <MonthDetail data={data} />
      )}
    </KpiShell>
  )
}

function ViewButton({
  active,
  onClick,
  children,
}: {
  active: boolean
  onClick: () => void
  children: ReactNode
}) {
  return (
    <button
      type="button"
      onClick={onClick}
      className={cn(
        'px-3 py-2 text-[13px] font-semibold transition-colors',
        active ? 'bg-brand-50 text-brand-700' : 'bg-white text-slate-500 hover:bg-slate-50',
      )}
    >
      {children}
    </button>
  )
}

/** Bitta xodimning to'liq hisobi — beshta bo'lim. */
function MonthDetail({ data }: { data: KpiMonthDto }) {
  const intake = isIntakeRole(data.roleCode)
  const confirmed = data.status === 'confirmed'

  // ⚠️ 2 va 3-bo'lim ikki ALOHIDA ro'yxatdan chiziladi: `conversions` — "nima bo'ldi"
  // (pulga tegmaydigan ko'rsatkich ham bor, u `coef: 1` bilan keladi), `coefs` — "bu qanday
  // baholandi" (FAQAT pulni ko'paytiradigan qatorlar). Kalit nomiga qarab ajratish
  // (`key.startsWith('conv')`) ATAYIN qilinmagan: server kalitini o'zgartirsa sahifa
  // JIMGINA yarim bo'sh qolardi.
  const conversions = data.conversions ?? []
  const coefs = data.coefs

  return (
    <div className="space-y-5">
      {/* ---------- Sarlavha qatori ---------- */}
      <Card>
        <div className="flex flex-wrap items-center justify-between gap-4">
          <div className="min-w-0">
            <div className="flex flex-wrap items-center gap-2">
              <h2 className="text-lg font-bold tracking-tight text-slate-800">{data.userName}</h2>
              <Badge tone="violet">{data.roleLabel || roleLabel(data.roleCode)}</Badge>
              {confirmed ? (
                <Badge tone="green">
                  <Lock className="h-3 w-3" />
                  Tasdiqlangan
                </Badge>
              ) : (
                <Badge tone="default">Qoralama</Badge>
              )}
            </div>
            <p className="mt-1 text-sm text-slate-400">
              {formatMonth(data.month)}
              {confirmed && data.confirmedBy && (
                <> · tasdiqladi: {data.confirmedBy}{data.confirmedAt ? ` (${data.confirmedAt.slice(0, 10)})` : ''}</>
              )}
            </p>
          </div>
          <div className="text-right">
            <p className="text-xs font-semibold uppercase tracking-wide text-slate-400">
              Oylik
            </p>
            <p className="font-mono text-[30px] font-semibold leading-none text-slate-900">
              {formatMoney(data.salary)}
            </p>
          </div>
        </div>
      </Card>

      {/* ---------- Ogohlantirishlar ---------- */}
      {data.rulesMissing && (
        <ErrorBanner text="Bu oy uchun qoidalar to‘plami topilmadi — hisob standart qiymatlar bilan qilindi. «Qoidalar» sahifasida rol uchun versiya yarating." />
      )}
      {data.warning && (
        <div className="flex items-start gap-2 rounded-xl border border-amber-200 bg-amber-50 px-4 py-3 text-sm text-amber-800">
          <TriangleAlert className="mt-0.5 h-4 w-4 shrink-0" />
          <span>{data.warning}</span>
        </div>
      )}
      {data.snapshotMissing && (
        <div className="flex items-start gap-2 rounded-xl border border-amber-200 bg-amber-50 px-4 py-3 text-sm text-amber-800">
          <TriangleAlert className="mt-0.5 h-4 w-4 shrink-0" />
          <span>
            <b>Oy boshi snapshoti yo'q.</b> Oy boshidagi holat (faol o'quvchilar soni, qarz
            qoldig'i) orqaga qarab aniq tiklanmaydi — pastdagi «taxminiy» belgili raqamlar
            hozirgi ma'lumotdan hisoblandi va haqiqiy oy boshidagidan farq qilishi mumkin.
          </span>
        </div>
      )}

      {/* ---------- 1. Kirish raqamlari ---------- */}
      <Card
        tight
        title="1. Kirish raqamlari"
        sub="Har bir raqam qayerdan olingani va manba sahifasiga havola"
      >
        {data.inputs.length === 0 ? (
          <EmptyState title="Kirish raqamlari yo'q" />
        ) : (
          <div className="divide-y divide-slate-100">
            {data.inputs.map((i) => (
              <InputRow key={i.key} input={i} />
            ))}
          </div>
        )}
      </Card>

      {/* ---------- 2. Konversiyalar ---------- */}
      {conversions.length > 0 && (
        <Card
          tight
          title="2. Konversiyalar"
          sub="Oqimning har bosqichida nechta odam qoldi"
        >
          <div className="divide-y divide-slate-100">
            {conversions.map((c) => (
              <div
                key={c.key}
                className="flex items-start justify-between gap-4 px-[18px] py-3"
              >
                <div className="min-w-0">
                  <p className="text-sm font-semibold text-slate-800">{c.label}</p>
                  {c.note && <p className="mt-0.5 text-[11.5px] text-slate-400">{c.note}</p>}
                </div>
                <span className="shrink-0 font-mono text-[15px] font-semibold text-slate-800">
                  {formatPercent(c.value)}
                </span>
              </div>
            ))}
          </div>
        </Card>
      )}

      {/* ---------- 3. Koeffitsientlar ---------- */}
      <Card
        tight
        title="3. Koeffitsientlar"
        sub="Pulni KO'PAYTIRADIGAN qatorlar: qaysi pog'onaga tushdi va o'sha pog'onaning izohi"
      >
        {coefs.length === 0 ? (
          <EmptyState title="Koeffitsient hisoblanmadi" />
        ) : (
          <div className="divide-y divide-slate-100">
            {coefs.map((c) => (
              <CoefRow key={c.key} coef={c} />
            ))}
          </div>
        )}
      </Card>

      {/* ---------- 4. Pul ---------- */}
      <Card tight title="4. Hisob" sub="Oklad · bonuslar · jarimalar · yakuniy oylik">
        <div className="divide-y divide-slate-100">
          {data.lines.map((l) => (
            <MoneyRow
              key={l.key}
              line={l}
              negative={l.key.startsWith('fine') || l.amount < 0}
            />
          ))}
          <MoneyRow
            line={{
              key: '__total',
              label: 'YAKUNIY OYLIK',
              amount: data.salary,
              note:
                data.guaranteeApplied
                  ? 'Kafolat qo‘llandi — hisoblangan summa kafolatdan past edi'
                  : null,
            }}
            strong
          />
        </div>

        <div className="flex flex-wrap gap-2 border-t border-slate-100 px-[18px] py-3">
          {data.guaranteeApplied && (
            <Badge tone="blue">
              <ShieldCheck className="h-3 w-3" />
              Kafolat qo'llandi: hisoblangan {formatMoney(data.computed)}
            </Badge>
          )}
          {data.capExceeded && (
            <Badge tone="amber">
              <TriangleAlert className="h-3 w-3" />
              Yuqori chegaradan oshdi{data.cap != null ? ` (${formatMoney(data.cap)})` : ''} —
              summa KESILMADI, «Yopish» sahifasida alohida tasdiqlanadi
            </Badge>
          )}
          {!data.guaranteeApplied && !data.capExceeded && (
            <span className="text-[12px] text-slate-400">
              Kafolat ham, yuqori chegara ham qo'llanmadi.
            </span>
          )}
        </div>
      </Card>

      {/* ---------- 5. Reja (faqat kiruvchi) ---------- */}
      {intake && data.plan && <PlanCard plan={data.plan} />}
    </div>
  )
}

/**
 * 5-QADAM: reja bilan solishtirish.
 *
 * ⚠️ LID KAFOLATI: lid soni chegaradan (`leadFloor`) past bo'lsa reja PASAYADI — admin
 * kelmagan lidga javob bermaydi. Bu ochiq yozilishi shart, aks holda "reja bajarilmadi"
 * degan xulosa noto'g'ri chiqardi.
 */
function PlanCard({ plan }: { plan: NonNullable<KpiMonthDto['plan']> }) {
  const ok = plan.planDone >= 1
  return (
    <Card
      title="5. Reja bilan solishtirish"
      sub="Oylik shartnoma rejasi va undan kelib chiqadigan kunlik normalar"
    >
      <div className="mb-4 flex flex-wrap items-center gap-3">
        <div>
          <p className="text-xs font-semibold uppercase tracking-wide text-slate-400">Reja</p>
          <p className="font-mono text-[22px] font-semibold leading-none text-slate-800">
            {plan.planContracts}
          </p>
        </div>
        <div className="h-8 w-px bg-slate-200" />
        <div>
          <p className="text-xs font-semibold uppercase tracking-wide text-slate-400">
            Bajarilishi
          </p>
          <p
            className={cn(
              'font-mono text-[22px] font-semibold leading-none',
              ok ? 'text-emerald-600' : 'text-amber-600',
            )}
          >
            {formatPercent(plan.planDone, 1)}
          </p>
        </div>
      </div>

      {plan.leadFloorApplied ? (
        <div className="mb-4 rounded-lg border border-sky-200 bg-sky-50 px-3.5 py-2.5 text-[12.5px] text-sky-800">
          <b>Lid kafolati ishladi.</b> Oyda kelgan lid soni kafolat chegarasidan (
          {plan.leadFloor} ta) kam bo'ldi, shuning uchun reja to'liq plan emas, HAQIQIY lid
          oqimidan hisoblandi: admin kelmagan lidga javob bermaydi.
        </div>
      ) : (
        <div className="mb-4 rounded-lg border border-slate-200 bg-slate-50 px-3.5 py-2.5 text-[12.5px] text-slate-500">
          Lid oqimi kafolat chegarasidan ({plan.leadFloor} ta) yuqori — to'liq oylik reja
          qo'llandi.
        </div>
      )}

      <div className="grid gap-3 sm:grid-cols-2 xl:grid-cols-4">
        <PlanCell label="Kuniga lid" value={plan.dailyLeads} />
        <PlanCell label="Kuniga urinish" value={plan.dailyTouches} />
        <PlanCell label="Kuniga sinov darsi" value={plan.dailyTrials} />
        <PlanCell label="Kuniga shartnoma" value={plan.dailyContracts} />
      </div>
    </Card>
  )
}

function PlanCell({ label, value }: { label: string; value: number }) {
  return (
    <div className="rounded-xl border border-slate-200 bg-white p-3.5">
      <p className="text-xs font-semibold uppercase tracking-wide text-slate-400">{label}</p>
      <p className="mt-1.5 font-mono text-[20px] font-semibold leading-none text-slate-800">
        {String(Number(value.toFixed(2))).replace('.', ',')}
      </p>
    </div>
  )
}

/** Barcha KPI xodimlari bo'yicha IXCHAM jadval — "kim qancha oladi" bir qarashda. */
function AllStaffTable({ rows, month }: { rows: KpiMonthDto[]; month: string }) {
  if (rows.length === 0)
    return (
      <Card>
        <EmptyState
          title={`${formatMonth(month)} uchun KPI xodimi topilmadi`}
          hint="«Qoidalar» sahifasida xodimlarga rol va oklad biriktiring."
        />
      </Card>
    )

  const total = rows.reduce((s, r) => s + r.salary, 0)

  return (
    <Card
      tight
      title={`${formatMonth(month)} — barcha xodimlar`}
      sub={`${rows.length} ta xodim · jami ${formatMoney(total)}`}
    >
      <div className="table-wrap">
        <table className="table">
          <thead>
            <tr>
              <th>Xodim</th>
              <th>Rol</th>
              <th className="num">Oklad</th>
              <th className="num">Bonus</th>
              <th className="num">Jarima</th>
              <th className="num">Oylik</th>
              <th>Holat</th>
            </tr>
          </thead>
          <tbody>
            {rows.map((r) => (
              <tr key={r.userId}>
                <td>
                  <Link
                    to={`/admin/boshqaruv/kpi/oy?user=${r.userId}&month=${r.month}`}
                    className="font-semibold text-slate-800 hover:text-brand-600 hover:underline"
                  >
                    {r.userName}
                  </Link>
                </td>
                <td className="text-[12.5px] text-slate-500">
                  {r.roleLabel || roleLabel(r.roleCode)}
                </td>
                <td className="num">{formatMoney(r.baseSalary)}</td>
                <td className="num text-emerald-600">{formatMoney(r.bonusTotal)}</td>
                <td className={cn('num', r.fineTotal > 0 && 'font-bold text-red-600')}>
                  {r.fineTotal > 0 ? `−${formatMoney(r.fineTotal)}` : '—'}
                </td>
                <td className="num font-bold text-slate-900">{formatMoney(r.salary)}</td>
                <td>
                  <div className="flex flex-wrap gap-1">
                    {r.status === 'confirmed' ? (
                      <Badge tone="green">Tasdiqlangan</Badge>
                    ) : (
                      <Badge tone="default">Qoralama</Badge>
                    )}
                    {r.guaranteeApplied && <Badge tone="blue">kafolat</Badge>}
                    {r.capExceeded && <Badge tone="amber">chegaradan oshdi</Badge>}
                    {r.snapshotMissing && <Badge tone="amber">taxminiy</Badge>}
                  </div>
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
      <p className="border-t border-slate-100 px-[18px] py-3 text-[11.5px] text-slate-400">
        Kirish raqamlari, koeffitsientlar va pog'ona izohlarini ko'rish uchun xodim ismini bosing.
      </p>
    </Card>
  )
}
