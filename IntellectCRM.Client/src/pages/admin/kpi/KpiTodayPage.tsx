import { useCallback, useEffect, useState } from 'react'
import { Link } from 'react-router-dom'
import { ExternalLink, Lock, TrendingUp } from 'lucide-react'
import { Card } from '@/components/ui/Card'
import { Badge } from '@/components/ui/Badge'
import { Input } from '@/components/ui/Input'
import { Loader } from '@/components/ui/Loader'
import { usePerm } from '@/lib/permissions'
import { cn, apiErrorMessage, formatMoney } from '@/lib/utils'
import { todayIso } from '@/lib/month'
import {
  getKpiToday,
  saveChecklistCheck,
  type KpiChecklistItemDto,
  type KpiNormDto,
  type KpiTodayDto,
} from '@/api/services/kpi'
import {
  CHECK_STATES,
  blockProgress,
  checkStateLabel,
  formatNorm,
  isAutoChecked,
  normStatus,
  roleLabel,
  useKpiParams,
  useKpiStaff,
} from './model'
import { EmptyState, ErrorBanner, KpiShell, StaffPicker } from './shared'

/**
 * «BUGUN» — xodimning KUNLIK sahifasi: reja/fakt normalari, vaqt bloklariga bo'lingan
 * cheklist, signal chiplari va (chiquvchi admin uchun) erta ogohlantirish ro'yxatlari.
 *
 * <p>Savol bitta: <b>"bugun nima qilishim kerak va qanchasini qildim"</b>. Oylik hisob
 * bu yerda YO'Q (u «Oy» sahifasida) — faqat "shu tezlikda davom etsa" prognozi bor.</p>
 *
 * <p>⚠️ Cheklist belgisi — modulning yozadigan beshta narsasidan biri. Qolgani (lid, to'lov,
 * davomat) o'z bo'limlarida qoladi va bu sahifada faqat O'QILADI.</p>
 */
export function KpiTodayPage() {
  const { can } = usePerm()
  const canEdit = can('kpi.today', 'edit')
  const { userId, date, setUserId, setDate } = useKpiParams()
  const { profiles, error: staffError } = useKpiStaff()

  const [data, setData] = useState<KpiTodayDto | null>(null)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const [saving, setSaving] = useState<string | null>(null)
  const [tick, setTick] = useState(0)

  // ⚠️ So'rov AYNAN effektda, setState esa faqat javob kelgach — effekt tanasida
  // to'g'ridan-to'g'ri setState kaskad render beradi (`react-hooks/set-state-in-effect`).
  useEffect(() => {
    let alive = true
    getKpiToday(userId, date)
      .then((d) => {
        if (!alive) return
        setData(d)
        setError(null)
        setLoading(false)
      })
      .catch((err) => {
        if (!alive) return
        setError(apiErrorMessage(err, "Kunlik ma'lumotni yuklab bo'lmadi"))
        setLoading(false)
      })
    return () => {
      alive = false
    }
  }, [userId, date, tick])

  const reload = useCallback(() => setTick((t) => t + 1), [])

  /**
   * Cheklist bandini belgilash. Server javobi kelgach RO'YXAT QAYTA YUKLANADI — belgini
   * faqat mahalliy holatda o'zgartirsak, tizim tomonidan hisoblanadigan bandlar va prognoz
   * eskirib qolardi.
   */
  const check = async (item: KpiChecklistItemDto, state: string) => {
    if (!canEdit || !data) return
    if (isAutoChecked(item.source, item.autoCheckKey)) return
    setSaving(item.itemId)
    try {
      await saveChecklistCheck({
        userId: data.userId,
        date: data.date,
        itemId: item.itemId,
        state,
        note: item.note,
      })
      reload()
    } catch (err) {
      alert(apiErrorMessage(err, 'Belgini saqlab bo‘lmadi'))
    } finally {
      setSaving(null)
    }
  }

  const isRetention = data?.roleCode === 'retention_admin'

  return (
    <KpiShell
      sub={
        data
          ? `${data.userName} — ${roleLabel(data.roleCode)}`
          : 'Kunlik norma, cheklist va signal'
      }
      actions={
        <>
          <StaffPicker profiles={profiles} value={userId} onChange={setUserId} />
          <Input
            type="date"
            value={date}
            max={todayIso()}
            onChange={(e) => setDate(e.target.value || todayIso())}
            className="w-auto"
          />
        </>
      }
    >
      {staffError && <ErrorBanner text={staffError} />}
      {error && <ErrorBanner text={error} onRetry={reload} />}

      {loading && !data ? (
        <Loader label="Yuklanmoqda..." />
      ) : !data ? (
        !error && (
          <Card>
            <EmptyState
              title="Bu xodimning KPI profili yo'q"
              hint="«Qoidalar» sahifasida xodimga rol va oklad biriktiring — shundan keyin kunlik norma va cheklist paydo bo'ladi."
            />
          </Card>
        )
      ) : (
        <div className="space-y-5">
          {/* ---------- Kunlik normalar: reja vs fakt ---------- */}
          <Card
            tight
            title="Kunlik norma"
            sub="Reja — oylik plandan ish kunlariga bo'lingan; fakt — bugungi haqiqiy raqam"
          >
            {data.norms.length === 0 ? (
              <EmptyState
                title="Bu rol uchun kunlik norma belgilanmagan"
                hint="Norma «Qoidalar» sahifasidagi oylik plan va ish kunlari sonidan hisoblanadi."
              />
            ) : (
              <div className="grid gap-3 p-[18px] sm:grid-cols-2 xl:grid-cols-3">
                {data.norms.map((n) => (
                  <NormCard key={n.key} norm={n} />
                ))}
              </div>
            )}
          </Card>

          {/* ---------- Prognoz ---------- */}
          {data.forecast && (
            <Card>
              <div className="flex flex-wrap items-center justify-between gap-3">
                <div className="flex items-start gap-3">
                  <div className="flex h-9 w-9 shrink-0 items-center justify-center rounded-lg bg-sky-50 text-sky-600">
                    <TrendingUp className="h-[18px] w-[18px]" />
                  </div>
                  <div className="min-w-0">
                    <p className="text-sm font-bold text-slate-800">
                      Shu tezlikda davom etsa — oy oxirida
                    </p>
                    <p className="mt-0.5 text-[12px] text-slate-400">{data.forecast.note}</p>
                  </div>
                </div>
                <div className="text-right">
                  <p className="font-mono text-[22px] font-semibold leading-none text-slate-900">
                    {formatMoney(data.forecast.salary)}
                  </p>
                  <p className="mt-1 text-[11.5px] text-slate-400">
                    oy {Math.round(data.forecast.progress * 100)}% o'tdi
                  </p>
                </div>
              </div>
            </Card>
          )}

          {/* ---------- Signal chiplari ---------- */}
          {data.signals.length > 0 && (
            <Card
              title="Signal"
              sub="Darhol e'tibor talab qiladigan raqamlar — bosilsa manba sahifasi ochiladi"
            >
              <div className="flex flex-wrap gap-2">
                {data.signals.map((s) => {
                  const chip = (
                    <span
                      className={cn(
                        'inline-flex items-center gap-2 rounded-lg border px-3 py-2 text-[12.5px] font-semibold transition-colors',
                        s.tone === 'bad'
                          ? 'border-red-200 bg-red-50 text-red-700'
                          : s.tone === 'warn'
                            ? 'border-amber-200 bg-amber-50 text-amber-700'
                            : 'border-slate-200 bg-white text-slate-600',
                        s.link && 'hover:border-slate-300',
                      )}
                    >
                      {s.label}
                      <span className="font-mono text-[13px]">{s.count}</span>
                      {s.link && <ExternalLink className="h-3.5 w-3.5 opacity-60" />}
                    </span>
                  )
                  return s.link ? (
                    <Link key={s.key} to={s.link}>
                      {chip}
                    </Link>
                  ) : (
                    <span key={s.key}>{chip}</span>
                  )
                })}
              </div>
            </Card>
          )}

          {/* ---------- Cheklist ---------- */}
          {data.blocks.length === 0 ? (
            <Card>
              <EmptyState
                title="Cheklist shabloni yo'q"
                hint="«Qoidalar» sahifasidagi «Shablonlarni yuklash» tugmasi ikkala rolning Excel cheklistini qo'shadi."
              />
            </Card>
          ) : (
            <div className="space-y-4">
              {data.blocks.map((block) => {
                const p = blockProgress(block.items.map((i) => i.state))
                return (
                  <Card
                    key={block.timeBlock}
                    tight
                    title={block.timeBlock}
                    sub={
                      p.total === 0
                        ? 'Hali belgilanmagan'
                        : `${p.done} / ${p.total} bajarildi (${p.pct}%)`
                    }
                  >
                    <div className="divide-y divide-slate-100">
                      {block.items.map((item) => (
                        <ChecklistRow
                          key={item.itemId}
                          item={item}
                          canEdit={canEdit}
                          saving={saving === item.itemId}
                          onCheck={(state) => check(item, state)}
                        />
                      ))}
                    </div>
                  </Card>
                )
              })}
            </div>
          )}

          {/* ---------- Erta ogohlantirish ro'yxatlari (chiquvchi admin) ---------- */}
          {data.lists.length > 0 && (
            <div className="space-y-4">
              <h2 className="text-sm font-bold uppercase tracking-wide text-slate-400">
                {isRetention ? 'Erta ogohlantirish' : "E'tibor talab qiladi"}
              </h2>
              {data.lists.map((list) => (
                <Card
                  key={list.key}
                  tight
                  title={list.label}
                  sub={`${list.rows.length} ta`}
                  actions={
                    list.link && (
                      <Link
                        to={list.link}
                        className="inline-flex items-center gap-1 text-[12.5px] font-semibold text-brand-600 hover:underline"
                      >
                        To'liq ro'yxat
                        <ExternalLink className="h-3.5 w-3.5" />
                      </Link>
                    )
                  }
                >
                  {list.rows.length === 0 ? (
                    <EmptyState title="Bo'sh — bugun bu bo'yicha ish yo'q" />
                  ) : (
                    <div className="divide-y divide-slate-100">
                      {list.rows.map((row) => {
                        const body = (
                          <div className="flex items-center justify-between gap-3 px-[18px] py-2.5">
                            <div className="min-w-0">
                              <p className="truncate text-sm font-semibold text-slate-800">
                                {row.title}
                              </p>
                              <p className="truncate text-[11.5px] text-slate-400">{row.sub}</p>
                            </div>
                            {row.link && (
                              <ExternalLink className="h-3.5 w-3.5 shrink-0 text-slate-300" />
                            )}
                          </div>
                        )
                        return row.link ? (
                          <Link
                            key={row.id}
                            to={row.link}
                            className="block transition-colors hover:bg-slate-50"
                          >
                            {body}
                          </Link>
                        ) : (
                          <div key={row.id}>{body}</div>
                        )
                      })}
                    </div>
                  )}
                </Card>
              ))}
            </div>
          )}
        </div>
      )}
    </KpiShell>
  )
}

/**
 * Bitta kunlik norma kartochkasi: REJA va FAKT yonma-yon, ustida ochiq "bajarildi /
 * bajarilmadi" belgisi.
 *
 * ⚠️ Reja endi HAQIQIY SON (`KpiNormDto.plan`) — matndan ajratib olinmaydi. Hisob esa
 * baribir serverda: bu yerda faqat ikki son solishtiriladi.
 *
 * ⚠️ `inverse` — "KAMROQ yaxshiroq" ko'rsatkichi (norma odatda 0). Chiziq bunday katakda
 * TESKARI o'qiladi: u chegaraning BAND qilingan ulushini ko'rsatadi, ya'ni to'lgan chiziq
 * YOMON. Shuning uchun rang har ikki rejimda `ok` bo'yicha beriladi va tagida shu izoh
 * yozib qo'yiladi — aks holda "reja 0, bajarildi" katagi bo'sh chiziq bilan chalg'itardi.
 */
function NormCard({ norm }: { norm: KpiNormDto }) {
  const st = normStatus(norm.fact, norm.plan, norm.inverse)
  const card = (
    <div
      className={cn(
        'h-full rounded-xl border bg-white p-3.5 transition-colors',
        st.ok ? 'border-slate-200' : 'border-amber-200',
        norm.link && 'hover:border-slate-300',
      )}
    >
      <div className="flex items-start justify-between gap-2">
        <span className="text-xs font-semibold uppercase tracking-wide text-slate-400">
          {norm.label}
        </span>
        <Badge tone={st.ok ? 'green' : 'red'}>{st.ok ? 'bajarildi' : st.delta}</Badge>
      </div>

      <div className="mt-2 flex items-baseline gap-2">
        <span className="font-mono text-[24px] font-semibold leading-none text-slate-800">
          {formatNorm(norm.fact)}
        </span>
        <span className="font-mono text-[13px] text-slate-400">
          / {formatNorm(norm.plan)} {norm.inverse ? 'chegara' : 'reja'}
          {norm.unit ? ` ${norm.unit}` : ''}
        </span>
      </div>

      <div className="mt-2 h-1.5 overflow-hidden rounded-full bg-slate-100">
        <div
          className={cn('h-full rounded-full', st.ok ? 'bg-emerald-500' : 'bg-amber-500')}
          style={{ width: `${Math.min(100, Math.max(0, st.pct))}%` }}
        />
      </div>

      <p className="mt-1.5 text-[11.5px] text-slate-400">
        {norm.note ||
          (norm.inverse
            ? `Kamroq — yaxshiroq (chegara ${formatNorm(norm.plan)})`
            : `Kunlik reja: ${formatNorm(norm.plan)}${norm.unit ? ` ${norm.unit}` : ''}`)}
      </p>
    </div>
  )
  return norm.link ? (
    <Link to={norm.link} className="block">
      {card}
    </Link>
  ) : (
    card
  )
}

/**
 * Cheklistning bitta qatori: № · matn · norma · KPI teg / audit mezoni · uch holatli belgi.
 *
 * ⚠️ TIZIM belgilagan band (`source == "auto"` va `autoCheckKey` bor) — FAQAT O'QISH uchun:
 * uni qo'lda o'zgartirish "tizim nima ko'rgani" bilan "xodim nima yozgani" ni aralashtirib
 * yuborardi va hisobot ishonchini yo'qotardi. Bunday band ochiq belgilanadi ("tizim belgiladi").
 */
function ChecklistRow({
  item,
  canEdit,
  saving,
  onCheck,
}: {
  item: KpiChecklistItemDto
  canEdit: boolean
  saving: boolean
  onCheck: (state: string) => void
}) {
  const auto = isAutoChecked(item.source, item.autoCheckKey)
  const locked = auto || !canEdit

  return (
    <div
      className={cn(
        'flex items-start gap-3 px-[18px] py-3',
        item.state === 'failed' && 'bg-red-50/40',
        auto && 'bg-slate-50/60',
      )}
    >
      <span className="mt-0.5 w-6 shrink-0 text-right font-mono text-[12px] font-semibold text-slate-300">
        {item.no}
      </span>

      <div className="min-w-0 flex-1">
        <p className="text-sm font-medium text-slate-800">{item.text}</p>
        <div className="mt-1 flex flex-wrap items-center gap-1.5">
          {item.norm && (
            <span className="text-[11px] font-semibold text-slate-400">Norma: {item.norm}</span>
          )}
          {item.kpiTag && <Badge tone="violet">{item.kpiTag}</Badge>}
          {item.criterionNo != null && (
            <Badge tone="blue">{item.criterionNo}-mezon</Badge>
          )}
          {auto && (
            <Badge tone="teal">
              <Lock className="h-3 w-3" />
              tizim belgiladi
            </Badge>
          )}
          {item.note && (
            <span className="text-[11px] italic text-slate-400">{item.note}</span>
          )}
        </div>
      </div>

      <div className="flex shrink-0 items-center gap-1">
        {CHECK_STATES.map((s) => {
          const active = item.state === s.value
          return (
            <button
              key={s.value}
              type="button"
              disabled={locked || saving}
              title={
                auto
                  ? `Tizim belgiladi (${checkStateLabel(item.state)}) — qo'lda o'zgartirilmaydi`
                  : s.label
              }
              onClick={() => onCheck(s.value)}
              className={cn(
                'flex h-8 w-8 items-center justify-center rounded-lg border text-[15px] font-bold transition-colors',
                active
                  ? s.value === 'done'
                    ? 'border-emerald-500 bg-emerald-50 text-emerald-600'
                    : s.value === 'failed'
                      ? 'border-red-400 bg-red-50 text-red-600'
                      : 'border-slate-300 bg-slate-100 text-slate-500'
                  : 'border-slate-200 bg-white text-slate-300 hover:border-slate-300 hover:text-slate-500',
                locked && 'cursor-not-allowed opacity-60 hover:border-slate-200 hover:text-slate-300',
              )}
            >
              {s.mark}
            </button>
          )
        })}
      </div>
    </div>
  )
}
