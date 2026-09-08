import { useMemo, useState } from 'react'
import {
  BadgePercent, Ban, CalendarRange, Pencil, Plus, Wallet, History as HistoryIcon,
} from 'lucide-react'
import {
  cancelStudentDiscount,
  createStudentDiscount,
  updateStudentDiscount,
  type StudentDiscountItem,
  type StudentDiscountPayload,
  type StudentDiscountsResponse,
} from '@/api/services/discounts'
import type { StudentGroupMembership } from '@/types'
import { Card } from '@/components/ui/Card'
import { Badge, type BadgeTone } from '@/components/ui/Badge'
import { Button } from '@/components/ui/Button'
import { Loader } from '@/components/ui/Loader'
import { Modal } from '@/components/ui/Modal'
import { Input, Select, Textarea } from '@/components/ui/Input'
import { StatCard } from '@/components/ui/StatCard'
import { apiErrorMessage, cn, formatDateTime, formatMoney } from '@/lib/utils'

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
  /** Tahrirlash/bekor qilish tugmalari — `students.list` "edit" ruxsati. */
  canEdit: boolean
  /** O'quvchining a'zoliklari — modaldagi guruh tanlash uchun (YANGI so'rov qilinmaydi). */
  groups: StudentGroupMembership[]
  /**
   * Amaldan keyin server TO'LIQ registrni qaytaradi — sahifa uni holatga yozadi VA o'quvchi
   * ma'lumotini qayta yuklaydi (chegirma balansga tegadi, aks holda eski balans qolib ketardi).
   */
  onChanged: (res: StudentDiscountsResponse) => void
}

/**
 * CHEGIRMA — o'quvchi olgan barcha chegirmalar registri, joriy holat va oylar bo'yicha
 * HAQIQATAN qo'llangan summa.
 *
 * <p>Tahrirlash/bekor qilish FAQAT `status === 'active'` qatorda: tarixiy qator (almashtirilgan
 * yoki bekor qilingan) o'zgartirilmaydi — server ham 400 qaytaradi.</p>
 */
export function DiscountSection({
  studentId, data, loading, error, canEdit, groups, onChanged,
}: Props) {
  /** Yaratish/tahrirlash oynasi: `null` — yopiq, `'new'` — yangi, obyekt — tahrirlash. */
  const [formFor, setFormFor] = useState<StudentDiscountItem | 'new' | null>(null)
  /** Bekor qilish oynasi (tasdiqlashsiz bajarilmaydi). */
  const [cancelFor, setCancelFor] = useState<StudentDiscountItem | null>(null)

  const items = data?.items ?? []
  const applied = data?.applied ?? []
  const current = data?.current ?? null

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
      {/* ── 1. JORIY HOLAT ─────────────────────────────────────────────────── */}
      <Section
        title="Chegirma"
        icon={BadgePercent}
        action={
          canEdit && !current ? (
            <Button onClick={() => setFormFor('new')}>
              <Plus className="h-4 w-4" /> Chegirma berish
            </Button>
          ) : undefined
        }
      >
        <div className="grid gap-4 sm:grid-cols-3">
          <StatCard
            label="Hozirgi chegirma"
            value={current ? sizeLabel(current.pct, current.amount) : "Yo'q"}
            hint={current ? periodLabel(current.startMonth, current.endMonth) : 'Chegirma berilmagan'}
            icon={BadgePercent}
            iconBg={current ? 'bg-emerald-50' : 'bg-slate-100'}
            iconColor={current ? 'text-emerald-600' : 'text-slate-400'}
          />
          <StatCard
            label="Joriy oyda"
            value={formatMoney(data?.currentMonthDiscount ?? 0)}
            hint="Shu oy hisobidan ayriladigan summa"
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

        {current ? (
          <div className="mt-4 rounded-xl border border-emerald-100 bg-emerald-50/50 p-4">
            <div className="flex flex-wrap items-center gap-2">
              <Badge tone="green" dot>{current.statusLabel || 'Amalda'}</Badge>
              {!current.inForce && (
                <Badge tone="amber">Davri boshlanmagan yoki tugagan</Badge>
              )}
              <span className="font-mono text-lg font-semibold text-slate-800">
                {sizeLabel(current.pct, current.amount)}
              </span>
              {canEdit && (
                <div className="ml-auto flex gap-2">
                  <Button variant="secondary" onClick={() => setFormFor(current)}>
                    <Pencil className="h-3.5 w-3.5" /> Tahrirlash
                  </Button>
                  <Button variant="danger" onClick={() => setCancelFor(current)}>
                    <Ban className="h-3.5 w-3.5" /> Bekor qilish
                  </Button>
                </div>
              )}
            </div>
            <dl className="mt-3 grid gap-x-6 gap-y-1.5 text-sm sm:grid-cols-2">
              <Row label="Davr" value={periodLabel(current.startMonth, current.endMonth)} />
              <Row label="Guruh" value={groupLabel(current.groupName)} />
              <Row label="Sabab" value={current.reason || '—'} />
              <Row
                label="Kim berdi"
                value={`${current.createdBy || '—'} · ${formatDateTime(current.createdAt)}`}
              />
            </dl>
          </div>
        ) : (
          <Empty>
            Hozircha chegirma yo'q.
            {canEdit
              ? " «Chegirma berish» tugmasi bilan yangi chegirma qo'shishingiz mumkin."
              : ''}
          </Empty>
        )}
      </Section>

      {/* ── 2. TARIX (registr) ─────────────────────────────────────────────── */}
      <Section title="Chegirmalar tarixi" icon={HistoryIcon}>
        {items.length === 0 ? (
          <Empty>Chegirma yozuvlari yo'q</Empty>
        ) : (
          <ul className="space-y-3">
            {items.map((it) => (
              <li
                key={it.id}
                className={cn(
                  'rounded-xl border p-3.5',
                  it.status === 'active'
                    ? 'border-emerald-200 bg-emerald-50/40'
                    : 'border-slate-200 bg-slate-50/60',
                )}
              >
                <div className="flex flex-wrap items-center gap-2">
                  <Badge tone={statusTone[it.status]} dot={it.status === 'active'}>
                    {it.statusLabel || it.status}
                  </Badge>
                  <span className="font-mono text-sm font-semibold text-slate-800">
                    {sizeLabel(it.pct, it.amount)}
                  </span>
                  <span className="text-xs text-slate-400">·</span>
                  <span className="text-sm text-slate-500">{periodLabel(it.startMonth, it.endMonth)}</span>
                  {/* Tarixiy qator O'ZGARTIRILMAYDI — tugmalar faqat amaldagi yozuvda. */}
                  {canEdit && it.status === 'active' && (
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
        )}
      </Section>

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
                  <td className="px-3 py-2 font-semibold text-slate-700" colSpan={4}>
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
          groups={groups}
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

function DiscountFormModal({
  studentId,
  initial,
  groups,
  onClose,
  onDone,
}: {
  studentId: string
  /** `null` — yangi chegirma. */
  initial: StudentDiscountItem | null
  groups: StudentGroupMembership[]
  onClose: () => void
  onDone: (res: StudentDiscountsResponse) => void
}) {
  // Boshlang'ich qiymatlar `useState` initializer'ida — oyna `key` bilan qaytadan yaratiladi.
  const [pct, setPct] = useState(initial ? String(initial.pct) : '')
  const [amount, setAmount] = useState(initial ? String(initial.amount) : '')
  const [startMonth, setStartMonth] = useState(initial?.startMonth ?? '')
  const [endMonth, setEndMonth] = useState(initial?.endMonth ?? '')
  const [groupId, setGroupId] = useState(initial?.groupId ?? '')
  const [reason, setReason] = useState(initial?.reason ?? '')
  /** Joriy oy hisobiga DARHOL qo'llansinmi (default — HA). */
  const [applyNow, setApplyNow] = useState(true)
  const [saving, setSaving] = useState(false)
  const [err, setErr] = useState('')

  /**
   * Guruh variantlari — FAOL a'zoliklar. Tahrirlanayotgan chegirmaning guruhi ro'yxatda
   * bo'lmasa (a'zolik yopilgan) ham qo'shiladi: aks holda saqlashda guruh JIMGINA
   * "barcha guruhlar" ga aylanib qolardi.
   */
  const groupOptions = useMemo(() => {
    const map = new Map<string, string>()
    groups.filter((g) => g.isActive).forEach((g) => map.set(g.groupId, g.groupName))
    if (initial?.groupId && !map.has(initial.groupId)) {
      map.set(initial.groupId, initial.groupName || 'Guruh')
    }
    return [...map.entries()].map(([id, name]) => ({ id, name }))
  }, [groups, initial])

  const pctNum = Number(pct) || 0
  const amountNum = Number(amount) || 0
  const pctInvalid = pctNum < 0 || pctNum > 100
  // Nolga tushirish uchun ALOHIDA amal bor («Bekor qilish»), shuning uchun "0 + 0" saqlanmaydi.
  const bothZero = pctNum === 0 && amountNum === 0
  const canSave = !pctInvalid && !bothZero && amountNum >= 0 && !saving

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
      groupId: groupId || null,
    }
    try {
      const res = initial
        ? await updateStudentDiscount(studentId, initial.id, payload, applyNow)
        : await createStudentDiscount(studentId, payload, applyNow)
      onDone(res)
    } catch (e) {
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

        <Select label="Guruh" value={groupId} onChange={(e) => setGroupId(e.target.value)}>
          <option value="">Barcha guruhlar</option>
          {groupOptions.map((g) => (
            <option key={g.id} value={g.id}>{g.name}</option>
          ))}
        </Select>

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
          <span className="font-semibold text-slate-800">
            {sizeLabel(item.pct, item.amount)}
          </span>{' '}
          chegirmasi bekor qilinadi ({groupLabel(item.groupName)},{' '}
          {periodLabel(item.startMonth, item.endMonth)}). Yozuv o'chmaydi — tarixda
          «bekor qilingan» bo'lib qoladi.
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
