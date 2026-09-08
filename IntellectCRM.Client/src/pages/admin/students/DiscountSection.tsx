import { useMemo, useState } from 'react'
import {
  BadgePercent, Ban, BookOpen, CalendarRange, ChevronDown, ChevronRight, Pencil, Plus, Wallet,
  History as HistoryIcon,
} from 'lucide-react'
import {
  cancelStudentDiscount,
  createStudentDiscount,
  updateStudentDiscount,
  type DiscountScopeOption,
  type StudentDiscountItem,
  type StudentDiscountPayload,
  type StudentDiscountsResponse,
} from '@/api/services/discounts'
import { Card } from '@/components/ui/Card'
import { Badge, type BadgeTone } from '@/components/ui/Badge'
import { Button } from '@/components/ui/Button'
import { Loader } from '@/components/ui/Loader'
import { Modal } from '@/components/ui/Modal'
import { Input, Select, Textarea } from '@/components/ui/Input'
import { StatCard } from '@/components/ui/StatCard'
import { apiErrorMessage, formatDateTime, formatMoney } from '@/lib/utils'

/* ─────────────────────────── kichik yordamchilar ─────────────────────────── */

const uzMonths = [
  'Yanvar', 'Fevral', 'Mart', 'Aprel', 'May', 'Iyun',
  'Iyul', 'Avgust', 'Sentabr', 'Oktabr', 'Noyabr', 'Dekabr',
]

/** "yyyy-MM" → "Sentabr 2026". Bo'sh/buzuq qiymat o'zgarishsiz qaytadi. */
const monthLabel = (m: string) =>
  m && m.length >= 7 ? `${uzMonths[Number(m.slice(5, 7)) - 1] ?? m} ${m.slice(0, 4)}` : m

/** Davr matni: ikkalasi bo'sh bo'lsa — «cheklovsiz». */
function periodLabel(startMonth: string, endMonth: string): string {
  if (!startMonth && !endMonth) return 'Cheklovsiz'
  if (startMonth && !endMonth) return `${monthLabel(startMonth)} dan boshlab`
  if (!startMonth && endMonth) return `${monthLabel(endMonth)} gacha`
  if (startMonth === endMonth) return monthLabel(startMonth)
  return `${monthLabel(startMonth)} — ${monthLabel(endMonth)}`
}

/** Chegirma hajmi: "20% + 50 000 so'm" ko'rinishida (ikkalasi 0 bo'lsa — «—»). */
function sizeLabel(pct: number, amount: number): string {
  const parts = [pct > 0 ? `${pct}%` : null, amount > 0 ? formatMoney(amount) : null].filter(Boolean)
  return parts.length > 0 ? parts.join(' + ') : '—'
}

const statusTone: Record<StudentDiscountItem['status'], BadgeTone> = {
  active: 'green',
  replaced: 'default',
  cancelled: 'red',
}

/** Guruh nomi bo'sh bo'lsa — chegirma BARCHA guruh hisoblariga tegishli. */
const groupLabel = (name: string) => (name ? name : 'Barcha guruhlar')

/**
 * Chegirma QAYSI FANGA berilganini bir qatorda ko'rsatish. Fan nomi bo'sh — chegirma
 * guruhga biriktirilmagan, ya'ni «Barcha guruhlar».
 */
const scopeTitle = (courseName: string) => (courseName ? courseName : 'Barcha guruhlar')

/**
 * `null`/`''` guruh id — «Barcha guruhlar» qamrovi. Select `value` si va xarita kaliti
 * sifatida BIR XIL ko'rinishga keltiriladi (aks holda `null` va `''` ikki xil kalit bo'lardi).
 */
const scopeKey = (groupId: string | null | undefined) => groupId ?? ''

/**
 * Chegirmadan keyingi oylik: AVVAL foiz, KEYIN summa; 0 dan past tushmaydi.
 * ⚠️ Serverdagi `TuitionService.DiscountForMonth` bilan bir xil tartib — teskarisi
 * boshqa raqam berardi.
 */
function previewFee(fee: number, pct: number, amount: number): { final: number; off: number } {
  if (fee <= 0) return { final: 0, off: 0 }
  const afterPct = fee - (fee * pct) / 100
  const final = Math.max(0, afterPct - amount)
  return { final: Math.round(final), off: Math.round(fee - final) }
}

/** Bo'lim sarlavhasi — o'quvchi profilidagi qolgan tablar bilan AYNAN bir xil ko'rinish. */
function Section({
  title,
  icon: Icon,
  children,
  action,
}: {
  title: string
  icon: typeof BadgePercent
  children: React.ReactNode
  action?: React.ReactNode
}) {
  return (
    <Card>
      <div className="mb-4 flex items-center gap-2">
        <Icon className="h-5 w-5 text-brand-600" />
        <h2 className="font-semibold text-slate-800">{title}</h2>
        {action && <div className="ml-auto">{action}</div>}
      </div>
      {children}
    </Card>
  )
}

const Empty = ({ children }: { children: React.ReactNode }) => (
  <p className="py-8 text-center text-sm text-slate-400">{children}</p>
)

/* ─────────────────────────── ASOSIY BO'LIM ─────────────────────────── */

interface Props {
  studentId: string
  data: StudentDiscountsResponse | null
  loading: boolean
  error: string
  /** Tahrirlash/bekor qilish/berish tugmalari — `students.list` "edit" ruxsati. */
  canEdit: boolean
  /**
   * Amaldan keyin server TO'LIQ registrni qaytaradi — sahifa uni holatga yozadi VA o'quvchi
   * ma'lumotini qayta yuklaydi (chegirma balansga tegadi, aks holda eski balans qolib ketardi).
   */
  onChanged: (res: StudentDiscountsResponse) => void
}

/**
 * CHEGIRMA — o'quvchining HAR FANI (guruhi) uchun ALOHIDA chegirma: berish, tahrirlash,
 * bekor qilish, tarix va oylar bo'yicha HAQIQATAN qo'llangan summa.
 *
 * <p>⚠️ Bir o'quvchida bir NECHTA amaldagi chegirma bo'lishi mumkin — har QAMROV (fan/guruh
 * yoki «Barcha guruhlar») uchun bittadan. Shuning uchun «Chegirma berish» tugmasi HAR DOIM
 * ko'rinadi: ikkinchi fanga chegirma berish yo'li shu.</p>
 *
 * <p>Tahrirlash/bekor qilish FAQAT `status === 'active'` qatorda: tarixiy qator (almashtirilgan
 * yoki bekor qilingan) o'zgartirilmaydi — server ham 400 qaytaradi.</p>
 */
export function DiscountSection({
  studentId, data, loading, error, canEdit, onChanged,
}: Props) {
  /** Yaratish/tahrirlash oynasi: `null` — yopiq, `'new'` — yangi, obyekt — tahrirlash. */
  const [formFor, setFormFor] = useState<StudentDiscountItem | 'new' | null>(null)
  /** Bekor qilish oynasi (tasdiqlashsiz bajarilmaydi). */
  const [cancelFor, setCancelFor] = useState<StudentDiscountItem | null>(null)
  /** Tarix ATAYIN yig'ilgan — kundalik savol "hozir nima amalda", tarix esa kamdan-kam kerak. */
  const [historyOpen, setHistoryOpen] = useState(false)

  const items = data?.items ?? []
  const active = data?.active ?? []
  const scopes = data?.scopes ?? []
  const applied = data?.applied ?? []

  /** Tarix = amalda BO'LMAGAN qatorlar (almashtirilgan/bekor qilingan). */
  const history = items.filter((it) => it.status !== 'active')

  /** Yangi chegirma berish mumkin bo'lgan qamrov qolganmi (hammasida bo'lsa — faqat tahrirlash). */
  const freeScopes = scopes.filter((s) => !s.hasActive)

  if (loading)
    return (
      <Section title="Chegirma" icon={BadgePercent}>
        <Loader label="Yuklanmoqda..." />
      </Section>
    )

  if (error)
    return (
      <Section title="Chegirma" icon={BadgePercent}>
        <p className="py-8 text-center text-sm text-red-600">{error}</p>
      </Section>
    )

  return (
    <div className="space-y-6">
      {/* ── 1. HOZIRGI CHEGIRMALAR (har fan uchun alohida) ─────────────────── */}
      <Section
        title="Chegirma"
        icon={BadgePercent}
        action={
          canEdit ? (
            /* ⚠️ HAR DOIM ko'rinadi — o'quvchi 2-3 fanga qatnashi mumkin, ya'ni chegirma bori
               ikkinchisiga chegirma berishga TO'SIQ emas. */
            <Button onClick={() => setFormFor('new')}>
              <Plus className="h-4 w-4" /> Chegirma berish
            </Button>
          ) : undefined
        }
      >
        <div className="grid gap-4 sm:grid-cols-3">
          <StatCard
            label="Amaldagi chegirmalar"
            value={active.length > 0 ? `${active.length} ta` : "Yo'q"}
            hint={
              active.length > 0
                ? active.map((a) => scopeTitle(a.courseName)).join(', ')
                : 'Hech bir fanga chegirma berilmagan'
            }
            icon={BookOpen}
            iconBg={active.length > 0 ? 'bg-emerald-50' : 'bg-slate-100'}
            iconColor={active.length > 0 ? 'text-emerald-600' : 'text-slate-400'}
          />
          <StatCard
            label="Joriy oyda"
            value={formatMoney(data?.currentMonthDiscount ?? 0)}
            hint="Shu oy hisobidan ayriladigan JAMI summa (barcha fanlar)"
            icon={CalendarRange}
            iconBg="bg-brand-50"
            iconColor="text-brand-600"
          />
          <StatCard
            label="Jami berilgan chegirma"
            value={formatMoney(data?.totalDiscount ?? 0)}
            hint="Oylik hisoblarda HAQIQATAN qo'llangani"
            icon={Wallet}
            iconBg="bg-indigo-50"
            iconColor="text-indigo-600"
          />
        </div>

        {active.length === 0 ? (
          <Empty>
            Hozircha chegirma yo'q.
            {canEdit
              ? " «Chegirma berish» tugmasi bilan istalgan fanga chegirma qo'shishingiz mumkin."
              : ''}
          </Empty>
        ) : (
          <ul className="mt-4 space-y-3">
            {active.map((it) => (
              <li
                key={it.id}
                className="rounded-xl border border-emerald-100 bg-emerald-50/50 p-4"
              >
                <div className="flex flex-wrap items-center gap-2">
                  <BookOpen className="h-4 w-4 shrink-0 text-emerald-600" />
                  <span className="font-semibold text-slate-800">{scopeTitle(it.courseName)}</span>
                  <Badge tone="green" dot>{it.statusLabel || 'Amalda'}</Badge>
                  {!it.inForce && <Badge tone="amber">Davri boshlanmagan yoki tugagan</Badge>}
                  <span className="font-mono text-lg font-semibold text-slate-800">
                    {sizeLabel(it.pct, it.amount)}
                  </span>
                  {canEdit && (
                    <div className="ml-auto flex gap-2">
                      <button
                        type="button"
                        className="btn btn-secondary btn-sm"
                        onClick={() => setFormFor(it)}
                      >
                        <Pencil className="h-3.5 w-3.5" /> Tahrirlash
                      </button>
                      <button
                        type="button"
                        className="btn btn-danger btn-sm"
                        onClick={() => setCancelFor(it)}
                      >
                        <Ban className="h-3.5 w-3.5" /> Bekor qilish
                      </button>
                    </div>
                  )}
                </div>
                <dl className="mt-3 grid gap-x-6 gap-y-1.5 text-sm sm:grid-cols-2">
                  <Row label="Guruh" value={groupLabel(it.groupName)} />
                  <Row label="O'qituvchi" value={it.teacherName || '—'} />
                  <Row label="Davr" value={periodLabel(it.startMonth, it.endMonth)} />
                  <Row label="Sabab" value={it.reason || '—'} />
                  <Row
                    label="Kim berdi"
                    value={`${it.createdBy || '—'} · ${formatDateTime(it.createdAt)}`}
                  />
                </dl>
              </li>
            ))}
          </ul>
        )}

        {canEdit && scopes.length > 0 && freeScopes.length === 0 && (
          <p className="mt-3 rounded-lg bg-slate-50 px-3 py-2 text-xs text-slate-500">
            Barcha fanlarda allaqachon chegirma bor — yangisini berish o'rniga mavjudini
            tahrirlang.
          </p>
        )}
      </Section>

      {/* ── 2. TARIX (yig'iladigan) ─────────────────────────────────────────── */}
      <Card>
        <button
          type="button"
          className="flex w-full items-center gap-2 text-left"
          onClick={() => setHistoryOpen((v) => !v)}
        >
          <HistoryIcon className="h-5 w-5 text-brand-600" />
          <h2 className="font-semibold text-slate-800">Chegirmalar tarixi</h2>
          <span className="text-sm text-slate-400">({history.length})</span>
          {historyOpen ? (
            <ChevronDown className="ml-auto h-4 w-4 text-slate-400" />
          ) : (
            <ChevronRight className="ml-auto h-4 w-4 text-slate-400" />
          )}
        </button>

        {historyOpen && (
          history.length === 0 ? (
            <Empty>Almashtirilgan yoki bekor qilingan chegirma yo'q</Empty>
          ) : (
            <ul className="mt-4 space-y-3">
              {history.map((it) => (
                <li key={it.id} className="rounded-xl border border-slate-200 bg-slate-50/60 p-3.5">
                  <div className="flex flex-wrap items-center gap-2">
                    <span className="font-semibold text-slate-700">{scopeTitle(it.courseName)}</span>
                    <Badge tone={statusTone[it.status]}>{it.statusLabel || it.status}</Badge>
                    <span className="font-mono text-sm font-semibold text-slate-800">
                      {sizeLabel(it.pct, it.amount)}
                    </span>
                    <span className="text-xs text-slate-400">·</span>
                    <span className="text-sm text-slate-500">
                      {periodLabel(it.startMonth, it.endMonth)}
                    </span>
                  </div>
                  <dl className="mt-2.5 grid gap-x-6 gap-y-1 text-sm sm:grid-cols-2">
                    <Row label="Guruh" value={groupLabel(it.groupName)} />
                    {it.teacherName && <Row label="O'qituvchi" value={it.teacherName} />}
                    <Row label="Sabab" value={it.reason || '—'} />
                    <Row
                      label="Kim berdi"
                      value={`${it.createdBy || '—'} · ${formatDateTime(it.createdAt)}`}
                    />
                    {it.endedAt && (
                      <Row
                        label={it.status === 'cancelled' ? 'Kim bekor qildi' : 'Kim yopdi'}
                        value={`${it.endedBy || '—'} · ${formatDateTime(it.endedAt)}`}
                      />
                    )}
                    {it.cancelReason && <Row label="Bekor sababi" value={it.cancelReason} />}
                  </dl>
                </li>
              ))}
            </ul>
          )
        )}
      </Card>

      {/* ── 3. OYLAR BO'YICHA HAQIQATAN QO'LLANGANI ────────────────────────── */}
      <Section title="Oylar bo'yicha qo'llangan chegirma" icon={CalendarRange}>
        {applied.length === 0 ? (
          <Empty>Hali birorta oy hisobiga chegirma qo'llanmagan</Empty>
        ) : (
          <div className="overflow-x-auto">
            <table className="w-full text-left text-sm">
              <thead className="bg-slate-50 text-xs uppercase tracking-wide text-slate-400">
                <tr>
                  <th className="px-3 py-2">Oy</th>
                  <th className="px-3 py-2">Fan</th>
                  <th className="px-3 py-2">Guruh</th>
                  <th className="px-3 py-2">O'qituvchi</th>
                  <th className="px-3 py-2 text-right">Hisoblangan</th>
                  <th className="px-3 py-2 text-right">Chegirma</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-slate-100">
                {applied.map((m, i) => (
                  <tr key={`${m.month}:${m.groupId ?? ''}:${i}`} className="hover:bg-slate-50/60">
                    <td className="px-3 py-2 font-medium text-slate-700">{monthLabel(m.month)}</td>
                    <td className="px-3 py-2 text-slate-500">{m.courseName || '—'}</td>
                    <td className="px-3 py-2 text-slate-500">{groupLabel(m.groupName)}</td>
                    <td className="px-3 py-2 text-slate-500">{m.teacherName || '—'}</td>
                    <td className="px-3 py-2 text-right font-mono text-slate-600">
                      {formatMoney(m.charged)}
                    </td>
                    <td className="px-3 py-2 text-right font-mono font-semibold text-emerald-600">
                      {formatMoney(m.discount)}
                    </td>
                  </tr>
                ))}
              </tbody>
              <tfoot>
                <tr className="border-t border-slate-200">
                  <td className="px-3 py-2 font-semibold text-slate-700" colSpan={5}>
                    Jami
                  </td>
                  <td className="px-3 py-2 text-right font-mono font-semibold text-emerald-700">
                    {formatMoney(data?.totalDiscount ?? 0)}
                  </td>
                </tr>
              </tfoot>
            </table>
          </div>
        )}
      </Section>

      {formFor && (
        <DiscountFormModal
          key={formFor === 'new' ? 'new' : formFor.id}
          studentId={studentId}
          initial={formFor === 'new' ? null : formFor}
          scopes={scopes}
          onClose={() => setFormFor(null)}
          onDone={(res) => {
            setFormFor(null)
            onChanged(res)
          }}
        />
      )}

      {cancelFor && (
        <CancelDiscountModal
          key={cancelFor.id}
          studentId={studentId}
          item={cancelFor}
          onClose={() => setCancelFor(null)}
          onDone={(res) => {
            setCancelFor(null)
            onChanged(res)
          }}
        />
      )}
    </div>
  )
}

/** Nom → qiymat qatori (kartochka ichidagi qisqa ma'lumot ro'yxati uchun). */
function Row({ label, value }: { label: string; value: string }) {
  return (
    <div className="flex gap-2">
      <dt className="shrink-0 text-slate-400">{label}:</dt>
      <dd className="min-w-0 font-medium text-slate-700">{value}</dd>
    </div>
  )
}

/* ─────────────────────────── YARATISH / TAHRIRLASH ─────────────────────────── */

/**
 * Chegirma berish/tahrirlash oynasi.
 *
 * ⚠️ TAHRIRLASHDA QAMROV O'ZGARTIRILMAYDI — qamrovni almashtirish "chegirmani boshqa fanga
 * ko'chirish" degani bo'lardi va tarix bilan pul hisobi bir-biridan ayrilib ketardi. Kerak
 * bo'lsa: mavjudini bekor qilib, yangi fanga yangisini berish.
 */
function DiscountFormModal({
  studentId,
  initial,
  scopes,
  onClose,
  onDone,
}: {
  studentId: string
  /** `null` — yangi chegirma. */
  initial: StudentDiscountItem | null
  scopes: DiscountScopeOption[]
  onClose: () => void
  onDone: (res: StudentDiscountsResponse) => void
}) {
  /** Bo'sh qamrovlar — YANGI chegirma faqat shulardan biriga beriladi (server ham 409 qaytaradi). */
  const freeScopes = useMemo(() => scopes.filter((s) => !s.hasActive), [scopes])

  // Boshlang'ich qiymatlar `useState` initializer'ida — oyna `key` bilan qaytadan yaratiladi.
  const [pct, setPct] = useState(initial ? String(initial.pct) : '')
  const [amount, setAmount] = useState(initial ? String(initial.amount) : '')
  const [startMonth, setStartMonth] = useState(initial?.startMonth ?? '')
  const [endMonth, setEndMonth] = useState(initial?.endMonth ?? '')
  /** Tanlangan qamrov kaliti ('' — «Barcha guruhlar»). Tahrirlashda O'ZGARMAYDI. */
  const [groupKey, setGroupKey] = useState(() =>
    initial ? scopeKey(initial.groupId) : scopeKey(freeScopes[0]?.groupId),
  )
  const [reason, setReason] = useState(initial?.reason ?? '')
  /** Joriy oy hisobiga DARHOL qo'llansinmi (default — HA). */
  const [applyNow, setApplyNow] = useState(true)
  const [saving, setSaving] = useState(false)
  const [err, setErr] = useState('')

  /** Tanlangan qamrov (oylik to'lovni va nomni ko'rsatish uchun). */
  const scope = scopes.find((s) => scopeKey(s.groupId) === groupKey) ?? null

  const pctNum = Number(pct) || 0
  const amountNum = Number(amount) || 0
  const pctInvalid = pctNum < 0 || pctNum > 100
  // Nolga tushirish uchun ALOHIDA amal bor («Bekor qilish»), shuning uchun "0 + 0" saqlanmaydi.
  const bothZero = pctNum === 0 && amountNum === 0
  /** Yangi chegirmada band qamrov tanlab bo'lmaydi (variantning o'zi ham `disabled`). */
  const scopeTaken = !initial && !!scope?.hasActive
  const noFreeScope = !initial && freeScopes.length === 0
  const canSave = !pctInvalid && !bothZero && amountNum >= 0 && !scopeTaken && !noFreeScope && !saving

  /** Oylik to'lov — tahrirlashda qamrov `scopes` da bo'lmasligi mumkin (a'zolik yopilgan). */
  const fee = scope?.monthlyFee ?? 0
  const preview = previewFee(fee, pctNum, amountNum)

  const submit = async () => {
    if (!canSave) return
    setSaving(true)
    setErr('')
    const payload: StudentDiscountPayload = {
      pct: pctNum,
      amount: amountNum,
      startMonth,
      endMonth,
      reason: reason.trim(),
      // Tahrirlashda qamrov O'ZGARMAYDI — mavjud qiymat qaytariladi.
      groupId: (initial ? scopeKey(initial.groupId) : groupKey) || null,
    }
    try {
      const res = initial
        ? await updateStudentDiscount(studentId, initial.id, payload, applyNow)
        : await createStudentDiscount(studentId, payload, applyNow)
      onDone(res)
    } catch (e) {
      // 409 — bu qamrovda allaqachon chegirma bor. Server matni o'zi tushunarli.
      setErr(apiErrorMessage(e, "Chegirmani saqlab bo'lmadi"))
    } finally {
      setSaving(false)
    }
  }

  return (
    <Modal
      open
      onClose={onClose}
      title={initial ? 'Chegirmani tahrirlash' : 'Chegirma berish'}
      footer={
        <>
          <Button variant="secondary" onClick={onClose} disabled={saving}>
            Bekor qilish
          </Button>
          <Button onClick={submit} disabled={!canSave}>
            {saving ? 'Saqlanmoqda...' : 'Saqlash'}
          </Button>
        </>
      }
    >
      <div className="space-y-4">
        {/* ── QAMROV (fan) ── */}
        {initial ? (
          <div className="rounded-lg border border-slate-200 bg-slate-50/60 p-3">
            <p className="text-xs font-semibold uppercase tracking-wide text-slate-400">Fan</p>
            <p className="mt-0.5 font-semibold text-slate-800">
              {scopeTitle(initial.courseName)}
            </p>
            <p className="text-sm text-slate-500">{groupLabel(initial.groupName)}</p>
            <p className="mt-1.5 text-xs text-slate-400">
              Tahrirlashda fan o'zgartirilmaydi. Boshqa fanga chegirma kerak bo'lsa — shu
              chegirmani bekor qilib, o'sha fanga yangisini bering.
            </p>
          </div>
        ) : (
          <div>
            <Select
              label="Qaysi fanga (guruhga)"
              value={groupKey}
              onChange={(e) => setGroupKey(e.target.value)}
            >
              {scopes.length === 0 && <option value="">Barcha guruhlar</option>}
              {scopes.map((s) => (
                <option
                  key={scopeKey(s.groupId)}
                  value={scopeKey(s.groupId)}
                  disabled={s.hasActive}
                >
                  {scopeTitle(s.courseName)}
                  {s.groupId ? ` — ${s.groupName}` : ''}
                  {s.teacherName ? ` (${s.teacherName})` : ''}
                  {s.hasActive ? ' — chegirma bor' : ''}
                </option>
              ))}
            </Select>
            <p className="mt-1 text-xs text-slate-400">
              Har fanga ALOHIDA chegirma beriladi. Chegirmasi bor fan ro'yxatda o'chiq turadi —
              bu fanda allaqachon chegirma bor, uni tahrirlang.
            </p>
            {noFreeScope && (
              <p className="mt-1.5 text-sm text-amber-600">
                Barcha fanlarda allaqachon chegirma bor — yangisini berib bo'lmaydi. Mavjudini
                tahrirlang yoki bekor qiling.
              </p>
            )}
            {scopeTaken && (
              <p className="mt-1.5 text-sm text-amber-600">
                Bu fanda allaqachon chegirma bor — uni tahrirlang.
              </p>
            )}
          </div>
        )}

        <div className="grid gap-4 sm:grid-cols-2">
          <Input
            label="Foiz (%)"
            type="number"
            min={0}
            max={100}
            value={pct}
            onChange={(e) => setPct(e.target.value)}
            placeholder="0"
          />
          <Input
            label="Summa (so'm)"
            type="number"
            min={0}
            value={amount}
            onChange={(e) => setAmount(e.target.value)}
            placeholder="0"
          />
        </div>
        <p className="text-xs text-slate-400">
          Avval foiz olib tashlanadi, keyin summa ayriladi. Ikkalasini birga ishlatish mumkin.
        </p>

        {/* Natijani OLDINDAN ko'rsatish — "20% qancha bo'ladi" savoli kalkulyatorsiz yopilsin. */}
        {fee > 0 && !bothZero && !pctInvalid && (
          <div className="rounded-lg border border-brand-100 bg-brand-50/60 p-3 text-sm">
            <span className="font-mono text-slate-500 line-through">{formatMoney(fee)}</span>
            <span className="mx-2 text-slate-400">→</span>
            <span className="font-mono text-base font-semibold text-slate-800">
              {formatMoney(preview.final)}
            </span>
            <span className="ml-2 font-medium text-emerald-600">
              (chegirma {formatMoney(preview.off)})
            </span>
            <span className="mt-0.5 block text-xs text-slate-400">
              Bir oylik hisob — {scope ? scopeTitle(scope.courseName) : 'tanlangan fan'}.
            </span>
          </div>
        )}

        <div className="grid gap-4 sm:grid-cols-2">
          <Input
            label="Boshlanish oyi"
            type="month"
            value={startMonth}
            onChange={(e) => setStartMonth(e.target.value)}
          />
          <Input
            label="Tugash oyi"
            type="month"
            value={endMonth}
            onChange={(e) => setEndMonth(e.target.value)}
          />
        </div>
        <p className="text-xs text-slate-400">
          Bo'sh qoldirilsa — chegirma cheklovsiz (muddatsiz) amal qiladi.
        </p>

        <Textarea
          label="Sabab"
          rows={3}
          value={reason}
          onChange={(e) => setReason(e.target.value)}
          placeholder="Masalan: aka-uka chegirmasi, ijtimoiy holat..."
        />

        <label className="flex cursor-pointer items-start gap-2.5 rounded-lg border border-slate-200 bg-slate-50/60 p-3">
          <input
            type="checkbox"
            className="mt-0.5 h-4 w-4 accent-brand-600"
            checked={applyNow}
            onChange={(e) => setApplyNow(e.target.checked)}
          />
          <span className="text-sm text-slate-700">
            Joriy oy hisobiga darhol qo'llansin
            <span className="mt-0.5 block text-xs font-normal text-slate-400">
              O'chirilgan bo'lsa chegirma faqat KEYINGI oydan amal qiladi — joriy oy hisobi
              o'zgarishsiz qoladi.
            </span>
          </span>
        </label>

        {pctInvalid && <p className="text-sm text-red-600">Foiz 0 dan 100 gacha bo'lishi kerak.</p>}
        {bothZero && (
          <p className="text-sm text-amber-600">
            Foiz ham, summa ham 0 — saqlash mumkin emas. Chegirmani olib tashlash uchun
            «Bekor qilish» dan foydalaning.
          </p>
        )}
        {err && <p className="text-sm text-red-600">{err}</p>}
      </div>
    </Modal>
  )
}

/* ─────────────────────────── BEKOR QILISH ─────────────────────────── */

function CancelDiscountModal({
  studentId,
  item,
  onClose,
  onDone,
}: {
  studentId: string
  item: StudentDiscountItem
  onClose: () => void
  onDone: (res: StudentDiscountsResponse) => void
}) {
  const [reason, setReason] = useState('')
  /** Bekor qilish ham joriy oyga tegadimi (yaratish/tahrirlashdagi bilan bir xil ma'no). */
  const [applyNow, setApplyNow] = useState(true)
  const [saving, setSaving] = useState(false)
  const [err, setErr] = useState('')

  const submit = async () => {
    setSaving(true)
    setErr('')
    try {
      const res = await cancelStudentDiscount(studentId, item.id, reason.trim(), applyNow)
      onDone(res)
    } catch (e) {
      setErr(apiErrorMessage(e, "Chegirmani bekor qilib bo'lmadi"))
    } finally {
      setSaving(false)
    }
  }

  return (
    <Modal
      open
      onClose={onClose}
      title="Chegirmani bekor qilish"
      size="sm"
      footer={
        <>
          <Button variant="secondary" onClick={onClose} disabled={saving}>
            Yo'q
          </Button>
          <Button variant="danger" onClick={submit} disabled={saving}>
            {saving ? 'Bajarilmoqda...' : 'Ha, bekor qilinsin'}
          </Button>
        </>
      }
    >
      <div className="space-y-4">
        <p className="text-sm text-slate-600">
          <span className="font-semibold text-slate-800">{scopeTitle(item.courseName)}</span> fani
          bo'yicha{' '}
          <span className="font-semibold text-slate-800">
            {sizeLabel(item.pct, item.amount)}
          </span>{' '}
          chegirmasi bekor qilinadi ({groupLabel(item.groupName)},{' '}
          {periodLabel(item.startMonth, item.endMonth)}). Yozuv o'chmaydi — tarixda
          «bekor qilingan» bo'lib qoladi.
        </p>
        <p className="text-xs text-slate-400">
          O'quvchining BOSHQA fanlaridagi chegirmalari tegilmaydi.
        </p>

        <Textarea
          label="Nega bekor qilinmoqda?"
          rows={3}
          value={reason}
          onChange={(e) => setReason(e.target.value)}
          placeholder="Ixtiyoriy — tarixda ko'rinadi"
        />

        <label className="flex cursor-pointer items-start gap-2.5 rounded-lg border border-slate-200 bg-slate-50/60 p-3">
          <input
            type="checkbox"
            className="mt-0.5 h-4 w-4 accent-brand-600"
            checked={applyNow}
            onChange={(e) => setApplyNow(e.target.checked)}
          />
          <span className="text-sm text-slate-700">
            Joriy oy hisobiga darhol qo'llansin
            <span className="mt-0.5 block text-xs font-normal text-slate-400">
              O'chirilgan bo'lsa joriy oy chegirmali bo'lib qoladi, bekor qilish keyingi oydan
              kuchga kiradi.
            </span>
          </span>
        </label>

        {err && <p className="text-sm text-red-600">{err}</p>}
      </div>
    </Modal>
  )
}
