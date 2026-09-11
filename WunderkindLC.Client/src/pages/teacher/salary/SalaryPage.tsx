import { useEffect, useState } from 'react'
import { useNavigate } from 'react-router-dom'
import {
  AlertTriangle, ArrowLeft, Award, CalendarClock, ChevronDown, Hourglass, Wallet,
} from 'lucide-react'
import { getTeacherSalary, getMyRetentionBonuses } from '@/api/services/teacher'
import type { TeacherRetentionSummary } from '@/api/services/retentionBonus'
import type { SalaryLedger } from '@/types'
import { formatMoney } from '@/lib/utils'
import { Loader } from '@/components/ui/Loader'

/**
 * O'qituvchi — Maosh. Joriy oy (hisoblandi/berildi/qoldi) + jami ko'rsatkichlar +
 * oylar ro'yxati (eng yangi tepada). Maosh rejimi: qat'iy yoki yig'ilgan to'lov foizi.
 */
const MONTH_NAMES = [
  'Yanvar', 'Fevral', 'Mart', 'Aprel', 'May', 'Iyun',
  'Iyul', 'Avgust', 'Sentabr', 'Oktabr', 'Noyabr', 'Dekabr',
]

function monthLabel(m: string): string {
  // "YYYY-MM" → "Iyun 2026" (xato bo'lsa xom qaytadi)
  const parts = m.split('-')
  const y = parts[0]
  const mi = Number(parts[1]) - 1
  if (mi >= 0 && mi < 12 && y) return `${MONTH_NAMES[mi]} ${y}`
  return m
}

/**
 * Foizli maosh ulushi yorlig'i. Har guruh alohida foizga sozlangan bo'lishi mumkin
 * (bir guruhi 40%, keyingisi 60%) — bunday holda bitta raqam yozib bo'lmaydi.
 */
function percentLabel(l: SalaryLedger): string {
  const groups = (l.groups ?? []).filter((g) => g.mode === 'percent')
  const rates = [...new Set(groups.map((g) => g.percent))]
  if (rates.length === 1) return `${rates[0]}%`
  if (rates.length > 1) return 'guruh foizlari'
  return `${l.salaryPercent ?? 0}%`
}

function statusChip(status: string): { label: string; cls: string } {
  switch (status) {
    case 'paid':
      return { label: "To'langan", cls: 'bg-tealsoft text-teal-700' }
    case 'partial':
      return { label: 'Qisman', cls: 'bg-amber-100 text-amber-700' }
    default:
      return { label: "To'lanmagan", cls: 'bg-rose-100 text-rose-600' }
  }
}

export function TeacherSalaryPage() {
  const nav = useNavigate()
  const [ledger, setLedger] = useState<SalaryLedger | null>(null)
  /** Ushlab turish bonuslari — maoshdan ALOHIDA (Expected/Remaining ga qo'shilmagan). */
  const [bonuses, setBonuses] = useState<TeacherRetentionSummary | null>(null)
  const [loading, setLoading] = useState(true)
  /** Ushlanma sababi ochilgan oy ("YYYY-MM") */
  const [expanded, setExpanded] = useState<string | null>(null)

  useEffect(() => {
    let alive = true
    getTeacherSalary()
      .then((d) => {
        if (alive) setLedger(d)
      })
      .catch(() => {
        if (alive) setLedger(null)
      })
      .finally(() => {
        if (alive) setLoading(false)
      })
    getMyRetentionBonuses()
      .then((d) => {
        if (alive) setBonuses(d)
      })
      .catch(() => {
        if (alive) setBonuses(null)
      })
    return () => {
      alive = false
    }
  }, [])

  // Joriy oy "YYYY-MM"
  const now = new Date()
  const curKey = `${now.getFullYear()}-${String(now.getMonth() + 1).padStart(2, '0')}`

  // Maoshda foizli ulush bormi (o'qituvchi darajasida yoki biror guruhda) — yig'ilgan
  // to'lov bazasi faqat shunda ma'noga ega.
  const isPercent =
    ledger?.salaryMode === 'percent' || (ledger?.groups ?? []).some((g) => g.mode === 'percent')

  return (
    <div className="px-4 pt-3 pb-6">
      {/* Sarlavha */}
      <div className="mb-4 flex items-center gap-2.5">
        <button
          type="button"
          onClick={() => nav(-1)}
          className="tap-scale flex h-9 w-9 shrink-0 items-center justify-center rounded-xl border border-line bg-white text-mute shadow-[var(--shadow-card)]"
        >
          <ArrowLeft className="h-5 w-5" />
        </button>
        <p className="text-[17px] font-extrabold text-ink">Maosh</p>
      </div>

      {loading ? (
        <Loader label="Yuklanmoqda..." />
      ) : !ledger || ledger.months.length === 0 ? (
        <div className="flex flex-col items-center justify-center gap-3 rounded-[20px] border border-line bg-white p-8 text-center shadow-[var(--shadow-card)]">
          <div className="flex h-12 w-12 items-center justify-center rounded-2xl bg-tealsoft text-teal-700">
            <Wallet className="h-6 w-6" />
          </div>
          <p className="text-[14px] font-semibold text-ink">Maosh ma'lumoti yo'q</p>
          <p className="text-[13px] text-mute">Hozircha hisoblangan oylik mavjud emas.</p>
        </div>
      ) : (
        <>
          {/* Umumiy karta */}
          {(() => {
            const cur = ledger.months.find((m) => m.month === curKey)
            const expected = cur ? cur.expected : ledger.totalExpected
            const paid = cur ? cur.paid : ledger.totalPaid
            const remaining = cur ? cur.remaining : ledger.remaining
            const modeSub =
              ledger.salaryMode === 'percent'
                ? `Yig'ilgan to'lovga asoslangan (${ledger.salaryPercent}%)`
                : "Qat'iy oylik"
            return (
              <div className="rounded-[20px] border border-line bg-white p-4 shadow-[var(--shadow-card)]">
                <div className="mb-3 flex items-center gap-2.5">
                  <div className="flex h-10 w-10 shrink-0 items-center justify-center rounded-xl bg-tealsoft text-teal-700">
                    <Wallet className="h-5 w-5" />
                  </div>
                  <div className="min-w-0">
                    <p className="text-[14px] font-bold text-ink">
                      {cur ? monthLabel(curKey) : 'Jami'}
                    </p>
                    <p className="truncate text-[12px] text-mute">{modeSub}</p>
                  </div>
                </div>

                <div className="grid grid-cols-3 gap-2">
                  <div className="rounded-[14px] border border-line bg-white p-2.5 text-center">
                    <p className="text-[11px] font-semibold text-faint">Hisoblandi</p>
                    <p className="mt-0.5 text-[14px] font-extrabold text-ink font-mono">
                      {formatMoney(expected)}
                    </p>
                  </div>
                  <div className="rounded-[14px] border border-line bg-white p-2.5 text-center">
                    <p className="text-[11px] font-semibold text-faint">Berildi</p>
                    <p className="mt-0.5 text-[14px] font-extrabold text-teal-700 font-mono">
                      {formatMoney(paid)}
                    </p>
                  </div>
                  <div className="rounded-[14px] border border-line bg-white p-2.5 text-center">
                    <p className="text-[11px] font-semibold text-faint">Qoldi</p>
                    <p className="mt-0.5 text-[14px] font-extrabold text-ink font-mono">
                      {formatMoney(remaining)}
                    </p>
                  </div>
                </div>

                {/* FOIZLI maoshda: hisob qayerdan chiqqani — yig'ilgan × foiz. */}
                {isPercent && cur && (cur.collected ?? 0) > 0 && (
                  <p className="mt-3 rounded-[14px] bg-slate-50 px-3.5 py-2.5 text-[12px] leading-relaxed text-mute">
                    Yig'ilgan:{' '}
                    <span className="font-mono font-bold text-ink">{formatMoney(cur.collected ?? 0)}</span>
                    {' × '}
                    <span className="font-bold text-ink">{percentLabel(ledger)}</span>
                    {' = '}
                    <span className="font-mono font-bold text-ink">
                      {formatMoney(cur.baseExpected ?? cur.expected)}
                    </span>
                  </p>
                )}

                {/* Jami (joriy oy ko'rsatilganda alohida) */}
                {cur && (
                  <div className="mt-3 flex items-center justify-between rounded-[14px] bg-tealsoft px-3.5 py-2.5">
                    <p className="text-[12px] font-semibold text-teal-700">Jami qoldiq</p>
                    <p className="text-[14px] font-extrabold text-teal-700 font-mono">
                      {formatMoney(ledger.remaining)}
                    </p>
                  </div>
                )}
              </div>
            )
          })()}

          {/* QAYSI OYGA — eng ko'p savol tug'diradigan joy. To'lov qaysi oy UCHUN qilingan
              bo'lsa, o'sha oyga kiradi: 3-avgustda iyul uchun to'lansa — iyul maoshiga. */}
          {isPercent && (
            <div className="mt-3 flex items-start gap-2.5 rounded-[16px] border border-line bg-white px-3.5 py-2.5 shadow-[var(--shadow-card)]">
              <CalendarClock className="mt-0.5 h-4 w-4 shrink-0 text-teal-600" />
              <p className="text-[12px] leading-relaxed text-mute">
                To'lov <span className="font-bold text-ink">qaysi oy uchun</span> qilingan bo'lsa,
                o'sha oy maoshiga kiradi — to'langan kun emas. Masalan 3-avgustda{' '}
                <span className="font-bold text-ink">iyul</span> uchun to'langan pul{' '}
                <span className="font-bold text-ink">iyul</span> maoshida ko'rinadi.
              </p>
            </div>
          )}

          {/* Jurnal ushlanmasi haqida eslatma */}
          {ledger.journalLinked && (
            <div className="mt-3 flex items-start gap-2.5 rounded-[16px] border border-amber-200 bg-amber-50 px-3.5 py-2.5">
              <AlertTriangle className="mt-0.5 h-4 w-4 shrink-0 text-amber-600" />
              <p className="text-[12px] leading-relaxed text-amber-800">
                Maosh jurnal bo'yicha hisoblanadi: jurnalda "o'tildi" deb belgilanmagan dars
                o'tilmagan hisoblanib, oylikdan ushlanadi. Tafsiloti uchun oyni bosing.
              </p>
            </div>
          )}

          {/* Oylar ro'yxati */}
          <p className="px-0.5 pb-2 pt-5 text-[13px] font-bold text-ink">Oylar</p>
          <div className="divide-y divide-line rounded-[20px] border border-line bg-white shadow-[var(--shadow-card)]">
            {[...ledger.months].reverse().map((m) => {
              const chip = statusChip(m.status)
              const missed = m.missedLessons ?? 0
              const deduction = m.deduction ?? 0
              const open = expanded === m.month
              const canOpen = deduction > 0
              return (
                <div key={m.month}>
                  <div
                    className="flex items-center gap-3 px-3.5 py-3"
                    onClick={() => canOpen && setExpanded(open ? null : m.month)}
                  >
                    <div className="min-w-0 flex-1">
                      <p className="flex items-center gap-1 text-[14px] font-bold text-ink">
                        {monthLabel(m.month)}
                        {canOpen && (
                          <ChevronDown
                            className={`h-3.5 w-3.5 text-faint transition-transform ${open ? 'rotate-180' : ''}`}
                          />
                        )}
                      </p>
                      <p className="text-[12px] text-mute">
                        Hisoblandi:{' '}
                        <span className="font-mono text-ink">{formatMoney(m.expected)}</span>
                        {' · '}Berildi:{' '}
                        <span className="font-mono text-teal-700">{formatMoney(m.paid)}</span>
                      </p>
                      {/* Foizli maoshda: shu OY UCHUN yig'ilgan to'lov — hisob shundan chiqadi. */}
                      {isPercent && (m.collected ?? 0) > 0 && (
                        <p className="text-[11px] text-faint">
                          Shu oy uchun yig'ilgan:{' '}
                          <span className="font-mono text-mute">{formatMoney(m.collected ?? 0)}</span>
                        </p>
                      )}
                      {/* Pul hali to'liq kelmagan bo'lsa (masalan guruh yopilgan oy) — "hammasi
                          to'lansa" qancha bo'lishi. Aks holda o'sha oy 0 bo'lib ko'rinardi. */}
                      {(m.potentialExpected ?? 0) > m.expected && (
                        <p className="text-[11px] text-amber-700">
                          Hisob bo'yicha:{' '}
                          <span className="font-mono font-semibold">
                            {formatMoney(m.potentialExpected ?? 0)}
                          </span>
                          <span className="ml-1 text-faint">(to'lovlar kelgani sari qo'shiladi)</span>
                        </p>
                      )}
                      {(m.substituteFee ?? 0) > 0 && (
                        <p className="mt-0.5 text-[12px] font-semibold text-emerald-600">
                          O'rinbosarlik haqi: <span className="font-mono">+{formatMoney(m.substituteFee ?? 0)}</span>
                        </p>
                      )}
                      {(m.substituteDeduction ?? 0) > 0 && (
                        <p className="mt-0.5 text-[12px] font-semibold text-amber-700">
                          O'rinbosar ushlanmasi: <span className="font-mono">−{formatMoney(m.substituteDeduction ?? 0)}</span>
                        </p>
                      )}
                      {/* SOF NATIJA — o'qituvchi ikki qatorni O'ZI ayirmasin. Ikkalasi ham
                          bo'lgan oyda "qo'shildimi yoki ushlandimi" savoli shu yerda yopiladi. */}
                      {(m.substituteFee ?? 0) > 0 && (m.substituteDeduction ?? 0) > 0 && (
                        <p className="mt-0.5 text-[12px] font-semibold text-slate-700">
                          O'rinbosarlik bo'yicha sof natija:{' '}
                          <span
                            className={`font-mono ${
                              (m.substituteFee ?? 0) - (m.substituteDeduction ?? 0) >= 0
                                ? 'text-emerald-600'
                                : 'text-amber-700'
                            }`}
                          >
                            {(m.substituteFee ?? 0) - (m.substituteDeduction ?? 0) >= 0 ? '+' : '−'}
                            {formatMoney(
                              Math.abs((m.substituteFee ?? 0) - (m.substituteDeduction ?? 0)),
                            )}
                          </span>
                        </p>
                      )}
                      {deduction > 0 && (
                        <p className="mt-0.5 text-[12px] font-semibold text-rose-600">
                          Ushlandi: <span className="font-mono">−{formatMoney(deduction)}</span>
                          <span className="ml-1 font-normal text-faint">
                            ({missed} ta dars belgilanmagan)
                          </span>
                        </p>
                      )}
                    </div>
                    <div className="shrink-0 text-right">
                      <span
                        className={`inline-block rounded-full px-2 py-0.5 text-[11px] font-bold ${chip.cls}`}
                      >
                        {chip.label}
                      </span>
                      <p className="mt-1 text-[12px] font-semibold text-faint">
                        Qoldi: <span className="font-mono text-ink">{formatMoney(m.remaining)}</span>
                      </p>
                    </div>
                  </div>

                  {/* Ushlanma sababi: qaysi guruhda qaysi darslar belgilanmagan */}
                  {open && (
                    <div className="space-y-2 border-t border-line bg-slate-50/70 px-3.5 py-3">
                      <p className="text-[11px] font-bold text-faint">
                        Belgilanmagan darslar — hisoblangan: {formatMoney(m.baseExpected ?? 0)}
                      </p>
                      {(m.lessons ?? [])
                        .filter((l) => l.missed > 0)
                        .map((l) => (
                          <div
                            key={l.groupId}
                            className="rounded-[14px] border border-line bg-white px-3 py-2"
                          >
                            <div className="flex items-center justify-between gap-2">
                              <span className="text-[13px] font-bold text-ink">{l.groupName}</span>
                              <span className="font-mono text-[12px] font-bold text-rose-600">
                                −{formatMoney(l.deduction)}
                              </span>
                            </div>
                            <p className="mt-0.5 text-[11px] text-mute">
                              {l.conducted}/{l.planned} dars belgilangan
                            </p>
                            <div className="mt-1.5 flex flex-wrap gap-1">
                              {l.missedDates.map((d) => (
                                <span
                                  key={d}
                                  className="rounded-md bg-rose-50 px-1.5 py-0.5 font-mono text-[11px] text-rose-700"
                                >
                                  {d.slice(5)}
                                </span>
                              ))}
                            </div>
                          </div>
                        ))}
                    </div>
                  )}
                </div>
              )
            })}
          </div>
        </>
      )}

      {/* Ushlab turish bonuslari — maosh raqamlaridan ALOHIDA bo'lim (Hisoblandi/Qoldi ga
          qo'shilmagan): bonus qayd, pul odatdagi maosh to'lovi orqali beriladi.
          Ikki qism: YO'LDAGILAR (oylari to'planayotgan o'quvchi × fan sikllari) va BERILGANLAR. */}
      {!loading && bonuses && (bonuses.items.length > 0 || (bonuses.inProgress ?? []).length > 0) && (
        <>
          <p className="px-0.5 pb-2 pt-5 text-[13px] font-bold text-ink">Ushlab turish bonusi</p>

          {/* Yo'ldagilar — hali bonus berilmagan, oylari to'planayotgan sikllar */}
          {(bonuses.inProgress ?? []).length > 0 && (
            <div className="mb-3 rounded-[20px] border border-line bg-white shadow-[var(--shadow-card)]">
              <div className="flex items-center gap-2.5 px-3.5 py-3">
                <div className="flex h-10 w-10 shrink-0 items-center justify-center rounded-xl bg-tealsoft text-teal-700">
                  <Hourglass className="h-5 w-5" />
                </div>
                <div className="min-w-0">
                  <p className="text-[14px] font-bold text-ink">Yo'ldagilar</p>
                  <p className="text-[12px] text-mute">
                    {(bonuses.inProgress ?? []).length} ta o'quvchi · fan bo'yicha alohida
                  </p>
                </div>
              </div>
              <p className="px-3.5 pb-2.5 text-[11px] leading-relaxed text-faint">
                «Menda» — bonusdan sizga qancha <span className="font-bold">ulush</span> tegishi,
                «necha oy dars berdim» EMAS: faqat sanoqqa kirgan (to'langan) oylar hisoblanadi.
              </p>
              <div className="divide-y divide-line border-t border-line">
                {(bonuses.inProgress ?? []).map((p) => {
                  const pct =
                    p.required > 0 ? Math.min(100, Math.round((p.counted / p.required) * 100)) : 0
                  return (
                    <div
                      key={`${p.studentId}:${p.courseId}`}
                      className={p.alreadyAwarded ? 'px-3.5 py-2.5 opacity-50' : 'px-3.5 py-2.5'}
                    >
                      <div className="flex items-center justify-between gap-2">
                        <span className="truncate text-[13px] font-bold text-ink">
                          {p.studentName}
                        </span>
                        <span className="shrink-0 font-mono text-[12px] font-bold text-teal-700">
                          {p.counted}/{p.required}
                        </span>
                      </div>
                      <p className="mt-0.5 truncate text-[11px] text-mute">
                        {p.courseName || '—'}
                        {p.groupNames ? ` · ${p.groupNames}` : ''}
                      </p>
                      <div className="mt-1.5 h-1.5 w-full overflow-hidden rounded-full bg-slate-100">
                        <div
                          className="h-full rounded-full bg-teal-600"
                          style={{ width: `${pct}%` }}
                        />
                      </div>
                      <p className="mt-1 text-[11px] text-faint">
                        Menda: <span className="font-mono text-ink">{p.myMonths}</span> oy
                        {p.alreadyAwarded
                          ? ' · bu o’quvchi orqali bonus olingan, bu sikldan tegmaydi'
                          : p.statusNote
                            ? ` · ${p.statusNote}`
                            : ''}
                      </p>
                    </div>
                  )
                })}
              </div>
            </div>
          )}

          {/* Berilgan bonuslar */}
          {bonuses.items.length > 0 && (
            <div className="rounded-[20px] border border-line bg-white shadow-[var(--shadow-card)]">
              <div className="flex items-center gap-2.5 px-3.5 py-3">
                <div className="flex h-10 w-10 shrink-0 items-center justify-center rounded-xl bg-tealsoft text-teal-700">
                  <Award className="h-5 w-5" />
                </div>
                <div className="min-w-0">
                  <p className="font-mono text-[15px] font-extrabold text-teal-700">
                    {formatMoney(bonuses.total)}
                  </p>
                  <p className="text-[12px] text-mute">
                    {bonuses.count} ta bonus · maoshga qo'shilmagan
                  </p>
                </div>
              </div>
              <div className="divide-y divide-line border-t border-line">
                {bonuses.items.map((b) => (
                  <div
                    key={b.awardId}
                    className={b.status === 'cancelled' ? 'px-3.5 py-2.5 opacity-50' : 'px-3.5 py-2.5'}
                  >
                    <div className="flex items-center justify-between gap-2">
                      <span className="truncate text-[13px] font-bold text-ink">{b.studentName}</span>
                      <span
                        className={
                          b.status === 'cancelled'
                            ? 'shrink-0 font-mono text-[12px] font-bold text-mute line-through'
                            : 'shrink-0 font-mono text-[12px] font-bold text-teal-700'
                        }
                      >
                        {formatMoney(b.amount)}
                      </span>
                    </div>
                    <p className="mt-0.5 text-[11px] text-mute">
                      {b.courseName ? `${b.courseName} · ` : ''}
                      {b.periodFrom} … {b.periodTo} · {b.months} oy
                      {b.status === 'cancelled' ? ' · bekor qilingan' : ''}
                    </p>
                  </div>
                ))}
              </div>
            </div>
          )}
        </>
      )}
    </div>
  )
}
