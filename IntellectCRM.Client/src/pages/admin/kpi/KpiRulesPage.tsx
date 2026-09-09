import { useCallback, useEffect, useState, type ReactNode } from 'react'
import { CircleAlert, History, Plus, Save, Sparkles, Trash2, Wallet } from 'lucide-react'
import { Card } from '@/components/ui/Card'
import { Badge } from '@/components/ui/Badge'
import { Button } from '@/components/ui/Button'
import { Modal } from '@/components/ui/Modal'
import { Input, Select, Textarea } from '@/components/ui/Input'
import { Loader } from '@/components/ui/Loader'
import { usePerm } from '@/lib/permissions'
import { formatMonth } from '@/config/constants'
import { currentMonth } from '@/lib/month'
import { cn, apiErrorMessage, formatDateTime, formatMoney } from '@/lib/utils'
import {
  deleteKpiProfile,
  getChecklistTemplates,
  getKpiProfileCandidates,
  getKpiRuleHistory,
  saveKpiProfile,
  saveKpiRules,
  saveKpiSalary,
  seedChecklistTemplates,
  seedKpiRules,
  type KpiChecklistTemplateDto,
  type KpiProfileDto,
  type KpiRuleSetDto,
  type KpiRuleSetJson,
  type KpiStaffDto,
  type KpiTier,
} from '@/api/services/kpi'
import {
  BONUS_CONFLICT_NOTE,
  KPI_ROLES,
  effectiveMonthOptions,
  emptyRules,
  formatCoef,
  fromPercentInput,
  isEffectiveMonthAllowed,
  nextMonth,
  roleLabel,
  toPercentInput,
  useKpiStaff,
  visibleRuleGroups,
  visibleTierTables,
  type KpiNumericRuleKey,
  type KpiRuleField,
} from './model'
import { EmptyState, ErrorBanner, KpiShell } from './shared'

type Section = 'staff' | 'rules' | 'checklist'

/**
 * «QOIDALAR» — bo'limning SOZLAMA sahifasi.
 *
 * <p>Uch bo'lim: <b>Xodimlar</b> (rol biriktirish + oklad va uning tarixi), <b>Qoidalar</b>
 * (rol bo'yicha konstantalar va pog'ona jadvallari + versiyalar tarixi) va <b>Cheklist
 * shablonlari</b>.</p>
 *
 * <p>⚠️ HAMMA SUMMA VERSIYALANADI: o'zgartirish "kuchga kirish oyi"ni so'raydi, standart
 * tanlov — KEYINGI oy, joriy oy mumkin, O'TGAN oy TAQIQLANGAN. Sabab: yopilgan oy o'z
 * versiyasi bilan muzlatilgan, orqadan qayta yozish tasdiqlangan natijani jimgina boshqa
 * qilib qo'yardi (`.claude/rules/kpi.md` §5).</p>
 *
 * <p>⚠️ Eski versiyalar HECH QACHON o'chirilmaydi va tahrirlanmaydi — ular tarix.</p>
 */
export function KpiRulesPage() {
  const { can } = usePerm()
  const canEdit = can('kpi.rules', 'edit')
  const canCreate = can('kpi.rules', 'create')
  const canDelete = can('kpi.rules', 'delete')

  const [section, setSection] = useState<Section>('staff')
  const { profiles, loading, error, reload } = useKpiStaff()
  const [seeding, setSeeding] = useState(false)

  const seed = async () => {
    if (
      !confirm(
        'Excel konstantalari (uchala rol uchun qoidalar) va kunlik cheklist shablonlari ' +
          "yuklansinmi?\n\nAmal IDEMPOTENT: mavjud qoidalar TEGILMAYDI, faqat yo'q bo'lgani " +
          "qo'shiladi.",
      )
    )
      return
    setSeeding(true)
    try {
      const r = await seedKpiRules()
      const t = await seedChecklistTemplates()
      alert(
        `Yuklandi: ${r.rules} ta qoidalar to'plami, ${t.templates} ta cheklist shabloni ` +
          `(${t.items} band).`,
      )
      reload()
    } catch (err) {
      alert(apiErrorMessage(err, 'Seed qilib bo‘lmadi'))
    } finally {
      setSeeding(false)
    }
  }

  return (
    <KpiShell
      sub="Rol biriktirish, oklad, qoidalar va cheklist shablonlari"
      actions={
        canCreate && (
          <Button variant="secondary" onClick={seed} disabled={seeding}>
            <Sparkles className="h-4 w-4" />
            {seeding ? 'Yuklanmoqda...' : 'Excel qiymatlarini yuklash'}
          </Button>
        )
      }
    >
      {error && <ErrorBanner text={error} onRetry={reload} />}

      <div className="mb-5 inline-flex overflow-hidden rounded-lg border border-slate-200">
        <SectionButton active={section === 'staff'} onClick={() => setSection('staff')}>
          Xodimlar va oklad
        </SectionButton>
        <SectionButton active={section === 'rules'} onClick={() => setSection('rules')}>
          Qoidalar
        </SectionButton>
        <SectionButton active={section === 'checklist'} onClick={() => setSection('checklist')}>
          Cheklist shablonlari
        </SectionButton>
      </div>

      {loading && profiles.length === 0 ? (
        <Loader label="Yuklanmoqda..." />
      ) : section === 'staff' ? (
        <StaffSection
          profiles={profiles}
          canEdit={canEdit}
          canCreate={canCreate}
          canDelete={canDelete}
          onChanged={reload}
        />
      ) : section === 'rules' ? (
        <RulesSection canEdit={canEdit} />
      ) : (
        <ChecklistSection />
      )}
    </KpiShell>
  )
}

function SectionButton({
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
        'px-4 py-2 text-[13px] font-semibold transition-colors',
        active ? 'bg-brand-50 text-brand-700' : 'bg-white text-slate-500 hover:bg-slate-50',
      )}
    >
      {children}
    </button>
  )
}

/* =====================================================================================
 *  1. XODIMLAR va OKLAD
 * ===================================================================================== */

function StaffSection({
  profiles,
  canEdit,
  canCreate,
  canDelete,
  onChanged,
}: {
  profiles: KpiProfileDto[]
  canEdit: boolean
  canCreate: boolean
  canDelete: boolean
  onChanged: () => void
}) {
  const [profileModal, setProfileModal] = useState<{ open: boolean; p: KpiProfileDto | null }>({
    open: false,
    p: null,
  })
  const [salaryModal, setSalaryModal] = useState<KpiProfileDto | null>(null)
  const [historyOf, setHistoryOf] = useState<string | null>(null)

  const remove = async (p: KpiProfileDto) => {
    if (
      !confirm(
        `${p.userName} ning KPI profili o'chirilsinmi? Oklad tarixi ham yo'qoladi va bu ` +
          "xodim uchun oylik hisoblanmay qoladi.",
      )
    )
      return
    try {
      await deleteKpiProfile(p.id)
      onChanged()
    } catch (err) {
      alert(apiErrorMessage(err, 'Profilni o‘chirib bo‘lmadi'))
    }
  }

  return (
    <div className="space-y-5">
      <Card
        tight
        title="KPI xodimlari"
        sub="Har xodimga ROL va OKLAD biriktiriladi; oklad oy bilan versiyalanadi"
        actions={
          canCreate && (
            <Button onClick={() => setProfileModal({ open: true, p: null })}>
              <Plus className="h-4 w-4" />
              Xodim qo'shish
            </Button>
          )
        }
      >
        {profiles.length === 0 ? (
          <EmptyState
            title="Hali birorta xodimga KPI roli biriktirilmagan"
            hint="Avval yuqoridagi «Excel qiymatlarini yuklash» tugmasi bilan qoidalarni yuklang, keyin xodim qo'shing."
          />
        ) : (
          <div className="divide-y divide-slate-100">
            {profiles.map((p) => (
              <div key={p.id} className="px-[18px] py-3.5">
                <div className="flex flex-wrap items-start justify-between gap-3">
                  <div className="min-w-0">
                    <div className="flex flex-wrap items-center gap-2">
                      <span className="text-sm font-bold text-slate-800">{p.userName}</span>
                      <Badge tone="violet">{p.roleLabel || roleLabel(p.roleCode)}</Badge>
                      {!p.isActive && <Badge tone="default">faol emas</Badge>}
                      {p.guaranteeUntilMonth && (
                        <Badge tone="blue">
                          kafolat {formatMonth(p.guaranteeUntilMonth)} gacha
                        </Badge>
                      )}
                    </div>
                    <p className="mt-1 text-[11.5px] text-slate-400">
                      {p.position || 'Lavozim ko‘rsatilmagan'} · boshlanish{' '}
                      {formatMonth(p.startMonth)}
                      {p.note ? ` · ${p.note}` : ''}
                    </p>
                  </div>

                  <div className="flex shrink-0 items-center gap-2">
                    <div className="text-right">
                      <p className="text-[11px] font-semibold uppercase tracking-wide text-slate-400">
                        Joriy oklad
                      </p>
                      <p className="font-mono text-[16px] font-semibold text-slate-800">
                        {formatMoney(p.currentSalary)}
                      </p>
                    </div>
                    {canEdit && (
                      <>
                        <Button variant="secondary" onClick={() => setSalaryModal(p)}>
                          <Wallet className="h-4 w-4" />
                          Oklad
                        </Button>
                        <Button
                          variant="ghost"
                          onClick={() => setProfileModal({ open: true, p })}
                        >
                          Tahrir
                        </Button>
                      </>
                    )}
                    <Button
                      variant="ghost"
                      onClick={() => setHistoryOf(historyOf === p.id ? null : p.id)}
                    >
                      <History className="h-4 w-4" />
                      Tarix
                    </Button>
                    {canDelete && (
                      <button
                        type="button"
                        onClick={() => remove(p)}
                        title="Profilni o'chirish"
                        className="flex h-8 w-8 items-center justify-center rounded-lg border border-slate-200 text-slate-400 transition-colors hover:bg-red-50 hover:text-red-600"
                      >
                        <Trash2 className="h-4 w-4" />
                      </button>
                    )}
                  </div>
                </div>

                {historyOf === p.id && <SalaryHistory salaries={p.salaries} />}
              </div>
            ))}
          </div>
        )}
      </Card>

      {profileModal.open && (
        <ProfileModal
          key={profileModal.p?.id ?? 'new'}
          profile={profileModal.p}
          onClose={() => setProfileModal({ open: false, p: null })}
          onSaved={() => {
            setProfileModal({ open: false, p: null })
            onChanged()
          }}
        />
      )}

      {salaryModal && (
        <SalaryModal
          key={`sal-${salaryModal.id}`}
          profile={salaryModal}
          onClose={() => setSalaryModal(null)}
          onSaved={() => {
            setSalaryModal(null)
            onChanged()
          }}
        />
      )}
    </div>
  )
}

/** Oklad TARIXI — eng yangisi tepada. Hech narsa o'chirilmaydi. */
function SalaryHistory({ salaries }: { salaries: KpiProfileDto['salaries'] }) {
  if (salaries.length === 0)
    return (
      <p className="mt-3 rounded-lg bg-slate-50 px-3 py-2.5 text-[12px] text-slate-400">
        Oklad tarixi bo'sh — «Oklad» tugmasi bilan birinchi versiyani kiriting.
      </p>
    )
  const sorted = [...salaries].sort((a, b) => b.effectiveFrom.localeCompare(a.effectiveFrom))
  const today = currentMonth()
  return (
    <div className="mt-3 overflow-hidden rounded-lg border border-slate-200">
      <table className="table">
        <thead>
          <tr>
            <th>Kuchga kirgan oy</th>
            <th className="num">Oklad</th>
            <th>Izoh</th>
            <th>Kim, qachon</th>
          </tr>
        </thead>
        <tbody>
          {sorted.map((s) => {
            const future = s.effectiveFrom > today
            return (
              <tr key={s.id}>
                <td className="font-semibold text-slate-700">
                  {formatMonth(s.effectiveFrom)}
                  {future && (
                    <Badge tone="blue" className="ml-2">
                      kelajakda
                    </Badge>
                  )}
                </td>
                <td className="num">{formatMoney(s.baseSalary)}</td>
                <td className="text-[12px] text-slate-500">{s.note || '—'}</td>
                <td className="text-[11.5px] text-slate-400">
                  {s.createdBy || '—'} · {formatDateTime(s.createdAt)}
                </td>
              </tr>
            )
          })}
        </tbody>
      </table>
    </div>
  )
}

/** Rol biriktirish / tahrirlash oynasi. */
function ProfileModal({
  profile,
  onClose,
  onSaved,
}: {
  profile: KpiProfileDto | null
  onClose: () => void
  onSaved: () => void
}) {
  const editing = !!profile
  const [candidates, setCandidates] = useState<KpiStaffDto[]>([])
  const [candidatesError, setCandidatesError] = useState<string | null>(null)
  const [userId, setUserId] = useState(profile?.userId ?? '')
  const [roleCode, setRoleCode] = useState(profile?.roleCode || KPI_ROLES[0].code)
  const [startMonth, setStartMonth] = useState(profile?.startMonth || currentMonth())
  const [guarantee, setGuarantee] = useState(profile?.guaranteeUntilMonth ?? '')
  const [isActive, setIsActive] = useState(profile?.isActive ?? true)
  const [note, setNote] = useState(profile?.note ?? '')
  const [baseSalary, setBaseSalary] = useState('')
  const [saving, setSaving] = useState(false)

  // Nomzodlar — profil BERILISHI mumkin bo'lgan xodimlar (profili borlar ham keladi,
  // ular `roleCode` bilan belgilangan). ⚠️ So'rov effektda, setState javob kelgach.
  useEffect(() => {
    if (editing) return
    let alive = true
    getKpiProfileCandidates()
      .then((list) => {
        if (alive) setCandidates(list)
      })
      .catch((err) => {
        // Ro'yxat kelmasa oyna ISHLASHDA davom etadi (pastda id qo'lda kiritiladi) —
        // aks holda bitta so'rov tufayli xodim qo'shib bo'lmay qolardi.
        if (alive) setCandidatesError(apiErrorMessage(err, "Xodimlar ro'yxati yuklanmadi"))
      })
    return () => {
      alive = false
    }
  }, [editing])

  const save = async () => {
    if (!userId.trim()) return alert('Xodimni tanlang.')
    if (!editing && !baseSalary.trim()) return alert('Birinchi okladni kiriting.')
    setSaving(true)
    try {
      await saveKpiProfile({
        id: profile?.id ?? null,
        userId: userId.trim(),
        roleCode,
        startMonth,
        guaranteeUntilMonth: guarantee || null,
        isActive,
        note: note.trim() || null,
        baseSalary: baseSalary.trim() ? Number(baseSalary) : null,
        salaryEffectiveFrom: baseSalary.trim() ? startMonth : null,
      })
      onSaved()
    } catch (err) {
      alert(apiErrorMessage(err, 'Profilni saqlab bo‘lmadi'))
    } finally {
      setSaving(false)
    }
  }

  const role = KPI_ROLES.find((r) => r.code === roleCode)

  return (
    <Modal
      open
      onClose={onClose}
      title={editing ? 'KPI profilini tahrirlash' : 'Xodimga KPI roli biriktirish'}
      footer={
        <>
          <Button variant="secondary" onClick={onClose}>
            Bekor qilish
          </Button>
          <Button onClick={save} disabled={saving}>
            {saving ? 'Saqlanmoqda...' : 'Saqlash'}
          </Button>
        </>
      }
    >
      <div className="space-y-4">
        {editing ? (
          <Input label="Xodim" value={profile.userName} disabled />
        ) : candidatesError ? (
          <>
            <ErrorBanner text={candidatesError} />
            <Input
              label="Xodim ID"
              required
              value={userId}
              placeholder="«Xodimlar va rollar» bo'limidagi foydalanuvchi id'si"
              onChange={(e) => setUserId(e.target.value)}
            />
          </>
        ) : (
          <Select
            label="Xodim"
            required
            value={userId}
            onChange={(e) => setUserId(e.target.value)}
          >
            <option value="">— tanlang —</option>
            {candidates.map((c) => (
              <option key={c.userId} value={c.userId}>
                {c.userName}
                {c.roleCode ? ` — ${c.roleLabel || roleLabel(c.roleCode)} (profili bor)` : ''}
              </option>
            ))}
          </Select>
        )}
        {!editing && !candidatesError && candidates.length === 0 && (
          <p className="-mt-2 text-[11.5px] text-slate-400">
            Nomzod topilmadi — «Xodimlar va rollar» bo'limida xodim yarating.
          </p>
        )}

        <Select label="KPI roli" required value={roleCode} onChange={(e) => setRoleCode(e.target.value)}>
          {KPI_ROLES.map((r) => (
            <option key={r.code} value={r.code}>
              {r.label}
            </option>
          ))}
        </Select>
        {role && <p className="-mt-2 text-[11.5px] text-slate-400">{role.hint}</p>}

        <div className="grid gap-4 sm:grid-cols-2">
          <Select
            label="Boshlanish oyi"
            required
            value={startMonth}
            onChange={(e) => setStartMonth(e.target.value)}
          >
            {effectiveMonthOptions(24, startMonth < currentMonth() ? startMonth : currentMonth()).map(
              (m) => (
                <option key={m} value={m}>
                  {formatMonth(m)}
                </option>
              ),
            )}
          </Select>

          <Select
            label="Kafolat qaysi oygacha"
            value={guarantee}
            onChange={(e) => setGuarantee(e.target.value)}
          >
            <option value="">Kafolat yo'q</option>
            {effectiveMonthOptions(24).map((m) => (
              <option key={m} value={m}>
                {formatMonth(m)}
              </option>
            ))}
          </Select>
        </div>
        <p className="-mt-2 text-[11.5px] text-slate-400">
          Kafolat muddatida oylik kafolat summasidan past tushmaydi (summa «Qoidalar»
          bo'limida).
        </p>

        {!editing && (
          <Input
            label="Birinchi oklad (so'm)"
            required
            type="number"
            value={baseSalary}
            onChange={(e) => setBaseSalary(e.target.value)}
            placeholder="Masalan 2500000"
          />
        )}

        <label className="flex items-center gap-2 text-sm text-slate-700">
          <input
            type="checkbox"
            checked={isActive}
            onChange={(e) => setIsActive(e.target.checked)}
            className="h-4 w-4 rounded border-slate-300"
          />
          Profil faol (faol bo'lmagan xodim oylik hisobiga kirmaydi)
        </label>

        <Textarea label="Izoh" rows={2} value={note} onChange={(e) => setNote(e.target.value)} />
      </div>
    </Modal>
  )
}

/**
 * YANGI OKLAD versiyasi.
 *
 * ⚠️ Kuchga kirish oyi: standart — KEYINGI oy, joriy oy mumkin, O'TGAN oy YO'Q
 * (ro'yxatda umuman ko'rinmaydi, ya'ni foydalanuvchi server rad etadigan amalni
 * boshlay ham olmaydi).
 */
function SalaryModal({
  profile,
  onClose,
  onSaved,
}: {
  profile: KpiProfileDto
  onClose: () => void
  onSaved: () => void
}) {
  const [effectiveFrom, setEffectiveFrom] = useState(nextMonth(currentMonth()))
  const [amount, setAmount] = useState(String(profile.currentSalary || ''))
  const [note, setNote] = useState('')
  const [saving, setSaving] = useState(false)

  const save = async () => {
    const value = Number(amount)
    if (!Number.isFinite(value) || value <= 0) return alert("Oklad summasini to'g'ri kiriting.")
    if (!isEffectiveMonthAllowed(effectiveFrom))
      return alert("O'tgan oyni tanlab bo'lmaydi — yopilgan oylar qayta yozilmaydi.")
    setSaving(true)
    try {
      await saveKpiSalary(profile.userId, {
        effectiveFrom,
        baseSalary: value,
        note: note.trim() || null,
      })
      onSaved()
    } catch (err) {
      alert(apiErrorMessage(err, 'Okladni saqlab bo‘lmadi'))
    } finally {
      setSaving(false)
    }
  }

  const isCurrent = effectiveFrom === currentMonth()

  return (
    <Modal
      open
      onClose={onClose}
      title={`${profile.userName} — yangi oklad`}
      footer={
        <>
          <Button variant="secondary" onClick={onClose}>
            Bekor qilish
          </Button>
          <Button onClick={save} disabled={saving}>
            <Save className="h-4 w-4" />
            {saving ? 'Saqlanmoqda...' : 'Yangi versiya sifatida saqlash'}
          </Button>
        </>
      }
    >
      <div className="space-y-4">
        <div className="rounded-lg border border-slate-200 bg-slate-50 px-3.5 py-2.5 text-[12.5px] text-slate-600">
          Joriy oklad: <b>{formatMoney(profile.currentSalary)}</b>. Yangi qiymat eskisini
          O'CHIRMAYDI — u tanlangan oydan boshlab kuchga kiradi, oldingi oylar esa eski oklad
          bilan qolaveradi.
        </div>

        <Input
          label="Yangi oklad (so'm)"
          required
          type="number"
          value={amount}
          onChange={(e) => setAmount(e.target.value)}
        />

        <Select
          label="Qaysi oydan kuchga kiradi"
          required
          value={effectiveFrom}
          onChange={(e) => setEffectiveFrom(e.target.value)}
        >
          {effectiveMonthOptions(13).map((m) => (
            <option key={m} value={m}>
              {formatMonth(m)}
              {m === currentMonth() ? ' (joriy oy)' : ''}
              {m === nextMonth(currentMonth()) ? ' (keyingi oy)' : ''}
            </option>
          ))}
        </Select>

        {isCurrent && (
          <div className="flex items-start gap-2 rounded-lg border border-amber-200 bg-amber-50 px-3.5 py-2.5 text-[12.5px] text-amber-800">
            <CircleAlert className="mt-0.5 h-4 w-4 shrink-0" />
            <span>
              Joriy oy tanlandi — <b>{formatMonth(currentMonth())}</b> hisobi darhol yangi
              oklad bilan qayta hisoblanadi (agar bu oy hali tasdiqlanmagan bo'lsa).
            </span>
          </div>
        )}

        <Textarea
          label="Izoh (nima uchun o'zgardi)"
          rows={2}
          value={note}
          onChange={(e) => setNote(e.target.value)}
          placeholder="Masalan: sinov muddati tugadi"
        />
      </div>
    </Modal>
  )
}

/* =====================================================================================
 *  2. QOIDALAR
 * ===================================================================================== */

function RulesSection({ canEdit }: { canEdit: boolean }) {
  const [roleCode, setRoleCode] = useState(KPI_ROLES[0].code)
  const [history, setHistory] = useState<KpiRuleSetDto[]>([])
  const [draft, setDraft] = useState<KpiRuleSetJson>(emptyRules())
  const [effectiveFrom, setEffectiveFrom] = useState(nextMonth(currentMonth()))
  const [note, setNote] = useState('')
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const [saving, setSaving] = useState(false)
  const [tick, setTick] = useState(0)

  useEffect(() => {
    let alive = true
    getKpiRuleHistory(roleCode)
      .then((list) => {
        if (!alive) return
        const sorted = [...list].sort((a, b) => b.effectiveFrom.localeCompare(a.effectiveFrom))
        setHistory(sorted)
        // Tahrir formasi ENG SO'NGGI versiyadan boshlanadi — yangi versiya odatda
        // amaldagisining ustiga bir-ikki qiymat o'zgartirib yasaladi.
        setDraft(sorted[0]?.rules ?? emptyRules())
        setError(null)
        setLoading(false)
      })
      .catch((err) => {
        if (!alive) return
        setError(apiErrorMessage(err, 'Qoidalar tarixini yuklab bo‘lmadi'))
        setLoading(false)
      })
    return () => {
      alive = false
    }
  }, [roleCode, tick])

  const reload = useCallback(() => setTick((t) => t + 1), [])

  const save = async () => {
    if (!isEffectiveMonthAllowed(effectiveFrom))
      return alert("O'tgan oyni tanlab bo'lmaydi — yopilgan oylar qayta yozilmaydi.")
    if (
      !confirm(
        `${roleLabel(roleCode)} uchun YANGI versiya yaratilsinmi? U ` +
          `${formatMonth(effectiveFrom)} dan kuchga kiradi. Eski versiya tarixda qoladi va ` +
          "tasdiqlangan oylar o'zgarmaydi.",
      )
    )
      return
    setSaving(true)
    try {
      await saveKpiRules({ roleCode, effectiveFrom, note: note.trim() || null, rules: draft })
      setNote('')
      reload()
    } catch (err) {
      alert(apiErrorMessage(err, 'Qoidalarni saqlab bo‘lmadi'))
    } finally {
      setSaving(false)
    }
  }

  const setField = (key: KpiNumericRuleKey, value: number | null) =>
    setDraft((d) => ({ ...d, [key]: value }))

  const setTiers = (key: 'efficiency' | 'conversion' | 'retention' | 'extension', tiers: KpiTier[]) =>
    setDraft((d) => ({ ...d, [key]: tiers }))

  if (loading) return <Loader label="Yuklanmoqda..." />

  return (
    <div className="space-y-5">
      {error && <ErrorBanner text={error} onRetry={reload} />}

      <Card>
        <div className="flex flex-wrap items-end gap-3">
          <Select
            label="Rol"
            value={roleCode}
            onChange={(e) => setRoleCode(e.target.value)}
            className="w-auto"
          >
            {KPI_ROLES.map((r) => (
              <option key={r.code} value={r.code}>
                {r.label}
              </option>
            ))}
          </Select>
          <div className="text-[12px] text-slate-400">
            {history.length === 0 ? (
              <span className="font-semibold text-amber-600">
                Bu rol uchun hali versiya yo'q — yuqoridagi «Excel qiymatlarini yuklash» tugmasi
                boshlang'ich to'plamni qo'shadi.
              </span>
            ) : (
              <>
                Amaldagi versiya: <b>{formatMonth(history[0].effectiveFrom)}</b> dan ·{' '}
                {history.length} ta versiya
              </>
            )}
          </div>
        </div>
      </Card>

      {/* ---------- §9: hal qilinmagan ziddiyat ---------- */}
      {roleCode === 'retention_admin' && (
        <div className="rounded-xl border border-amber-300 bg-amber-50 px-4 py-3.5">
          <div className="flex items-start gap-2">
            <CircleAlert className="mt-0.5 h-4 w-4 shrink-0 text-amber-600" />
            <div>
              <p className="text-sm font-bold text-amber-900">{BONUS_CONFLICT_NOTE.title}</p>
              <p className="mt-1 text-[12.5px] leading-relaxed text-amber-900">
                {BONUS_CONFLICT_NOTE.body}
              </p>
              <p className="mt-1.5 text-[12.5px] font-semibold leading-relaxed text-amber-900">
                {BONUS_CONFLICT_NOTE.action}
              </p>
            </div>
          </div>
        </div>
      )}

      {/* ---------- Konstantalar ---------- */}
      {visibleRuleGroups(roleCode).map((g) => (
        <Card key={g.key} title={g.label} sub={g.sub}>
          <div className="grid gap-4 sm:grid-cols-2 xl:grid-cols-3">
            {g.fields.map((f) => (
              <RuleFieldInput
                key={f.key}
                field={f}
                value={draft[f.key]}
                disabled={!canEdit}
                onChange={(v) => setField(f.key, v)}
              />
            ))}
          </div>
        </Card>
      ))}

      {/* ---------- Pog'ona jadvallari ---------- */}
      {visibleTierTables(roleCode).map((t) => (
        <TierTableEditor
          key={t.key}
          label={t.label}
          sub={t.sub}
          tiers={draft[t.key]}
          disabled={!canEdit}
          onChange={(tiers) => setTiers(t.key, tiers)}
        />
      ))}

      {/* ---------- Saqlash ---------- */}
      {canEdit && (
        <Card title="Yangi versiya sifatida saqlash" sub="Eski versiya tahrirlanmaydi — u tarix">
          <div className="flex flex-wrap items-end gap-3">
            <Select
              label="Qaysi oydan kuchga kiradi"
              value={effectiveFrom}
              onChange={(e) => setEffectiveFrom(e.target.value)}
              className="w-auto"
            >
              {effectiveMonthOptions(13).map((m) => (
                <option key={m} value={m}>
                  {formatMonth(m)}
                  {m === currentMonth() ? ' (joriy oy)' : ''}
                  {m === nextMonth(currentMonth()) ? ' (keyingi oy)' : ''}
                </option>
              ))}
            </Select>
            <Input
              label="Izoh"
              value={note}
              onChange={(e) => setNote(e.target.value)}
              placeholder="Nima uchun o'zgartirildi"
              className="min-w-[16rem]"
            />
            <Button onClick={save} disabled={saving}>
              <Save className="h-4 w-4" />
              {saving ? 'Saqlanmoqda...' : 'Saqlash'}
            </Button>
          </div>
          <p className="mt-3 text-[11.5px] text-slate-400">
            O'tgan oy ro'yxatda YO'Q: yopilgan oy o'z versiyasi bilan muzlatilgan va orqadan
            qayta yozilmaydi.
          </p>
        </Card>
      )}

      {/* ---------- Versiyalar tarixi ---------- */}
      <Card
        tight
        title="Versiyalar tarixi"
        sub="Faqat o'qish uchun — kim, qachon va qaysi oydan boshlab o'zgartirgan"
      >
        {history.length === 0 ? (
          <EmptyState title="Hali versiya yo'q" />
        ) : (
          <div className="table-wrap">
            <table className="table">
              <thead>
                <tr>
                  <th>Kuchga kirgan oy</th>
                  <th>Izoh</th>
                  <th>Kim</th>
                  <th>Qachon</th>
                  <th className="num">Oklad kafolati</th>
                  <th className="num">Tiket jarimasi</th>
                </tr>
              </thead>
              <tbody>
                {history.map((h, idx) => (
                  <tr key={h.id}>
                    <td className="font-semibold text-slate-700">
                      {formatMonth(h.effectiveFrom)}
                      {idx === 0 && (
                        <Badge tone="green" className="ml-2">
                          amaldagi
                        </Badge>
                      )}
                    </td>
                    <td className="text-[12px] text-slate-500">{h.note || '—'}</td>
                    <td className="text-[12px] text-slate-500">{h.createdBy || '—'}</td>
                    <td className="text-[11.5px] text-slate-400">{formatDateTime(h.createdAt)}</td>
                    <td className="num">{formatMoney(h.rules.guarantee)}</td>
                    <td className="num">{formatMoney(h.rules.ticketFine)}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </Card>
    </div>
  )
}

/** Bitta konstanta maydoni. Foizli maydonlar EKRANDA foizda, bazada ulush (0..1) bo'lib qoladi. */
function RuleFieldInput({
  field,
  value,
  disabled,
  onChange,
}: {
  field: KpiRuleField
  value: number | null
  disabled: boolean
  onChange: (value: number | null) => void
}) {
  const shown =
    value === null || value === undefined
      ? ''
      : field.kind === 'percent'
        ? String(toPercentInput(value))
        : String(value)

  return (
    <div>
      <Input
        label={field.kind === 'percent' ? `${field.label} (%)` : field.label}
        type="number"
        value={shown}
        disabled={disabled}
        placeholder={field.nullable ? "bo'sh = chegara yo'q" : undefined}
        onChange={(e) => {
          const raw = e.target.value
          if (raw === '') return onChange(field.nullable ? null : 0)
          const n = Number(raw)
          if (!Number.isFinite(n)) return
          onChange(field.kind === 'percent' ? fromPercentInput(n) : n)
        }}
      />
      {field.hint && <p className="mt-1 text-[11px] text-slate-400">{field.hint}</p>}
      {field.kind === 'money' && value != null && value > 0 && (
        <p className="mt-1 font-mono text-[11px] text-slate-400">{formatMoney(value)}</p>
      )}
    </div>
  )
}

/**
 * Pog'ona jadvali muharriri.
 *
 * ⚠️ Qatorlar `from` bo'yicha O'SISH tartibida bo'lishi kerak (server AYNAN shunday
 * tanlaydi: `from <= x` bo'lgan ENG OXIRGI qator). Saqlashda tartiblab yuboriladi —
 * qo'lda aralashtirib yuborilgan jadval jimgina boshqa koeffitsient bermasin.
 */
function TierTableEditor({
  label,
  sub,
  tiers,
  disabled,
  onChange,
}: {
  label: string
  sub: string
  tiers: KpiTier[]
  disabled: boolean
  onChange: (tiers: KpiTier[]) => void
}) {
  const patch = (i: number, p: Partial<KpiTier>) => {
    const next = tiers.map((t, idx) => (idx === i ? { ...t, ...p } : t))
    onChange(next)
  }
  const add = () =>
    onChange(
      [...tiers, { from: 0, to: 1, coef: 1, note: '' }].sort((a, b) => a.from - b.from),
    )
  const remove = (i: number) => onChange(tiers.filter((_, idx) => idx !== i))
  const sort = () => onChange([...tiers].sort((a, b) => a.from - b.from))

  return (
    <Card
      tight
      title={label}
      sub={sub}
      actions={
        !disabled && (
          <>
            <Button variant="ghost" onClick={sort}>
              Tartiblash
            </Button>
            <Button variant="secondary" onClick={add}>
              <Plus className="h-4 w-4" />
              Qator
            </Button>
          </>
        )
      }
    >
      {tiers.length === 0 ? (
        <EmptyState
          title="Jadval bo'sh"
          hint="Pog'ona bo'lmasa koeffitsient har doim ×1 bo'ladi, ya'ni natija baholanmaydi."
        />
      ) : (
        <div className="table-wrap">
          <table className="table">
            <thead>
              <tr>
                <th className="num">Dan (%)</th>
                <th className="num">Gacha (%)</th>
                <th className="num">Koeffitsient</th>
                <th>Izoh — xodim SHU matnni ko'radi</th>
                {!disabled && <th />}
              </tr>
            </thead>
            <tbody>
              {tiers.map((t, i) => (
                <tr key={i}>
                  <td className="num">
                    <NumCell
                      value={toPercentInput(t.from)}
                      disabled={disabled}
                      onChange={(v) => patch(i, { from: fromPercentInput(v) })}
                    />
                  </td>
                  <td className="num">
                    <NumCell
                      value={toPercentInput(t.to)}
                      disabled={disabled}
                      onChange={(v) => patch(i, { to: fromPercentInput(v) })}
                    />
                  </td>
                  <td className="num">
                    <div className="flex items-center justify-end gap-2">
                      <NumCell
                        value={t.coef}
                        step={0.05}
                        disabled={disabled}
                        onChange={(v) => patch(i, { coef: v })}
                      />
                      <Badge tone="default">{formatCoef(t.coef)}</Badge>
                    </div>
                  </td>
                  <td>
                    <Input
                      value={t.note ?? ''}
                      disabled={disabled}
                      placeholder="NORMA. Bonus to'liq."
                      onChange={(e) => patch(i, { note: e.target.value })}
                    />
                  </td>
                  {!disabled && (
                    <td>
                      <button
                        type="button"
                        onClick={() => remove(i)}
                        title="Qatorni o'chirish"
                        className="flex h-8 w-8 items-center justify-center rounded-lg border border-slate-200 text-slate-400 transition-colors hover:bg-red-50 hover:text-red-600"
                      >
                        <Trash2 className="h-4 w-4" />
                      </button>
                    </td>
                  )}
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </Card>
  )
}

function NumCell({
  value,
  onChange,
  disabled,
  step,
}: {
  value: number
  onChange: (value: number) => void
  disabled: boolean
  step?: number
}) {
  return (
    <input
      type="number"
      step={step ?? 'any'}
      value={String(value)}
      disabled={disabled}
      onChange={(e) => {
        const n = Number(e.target.value)
        if (Number.isFinite(n)) onChange(n)
      }}
      className="w-24 rounded-lg border border-slate-200 px-2 py-1.5 text-right text-sm text-slate-800 outline-none transition-colors focus:border-brand-400 focus:ring-2 focus:ring-brand-100 disabled:bg-slate-50"
    />
  )
}

/* =====================================================================================
 *  3. CHEKLIST SHABLONLARI
 * ===================================================================================== */

/**
 * Cheklist shablonlari — FAQAT KO'RISH.
 *
 * <p>⚠️ Band matni serverdagi `PUT checklist/items/{id}` bilan tahrirlanadi, lekin bu
 * versiyada bandlar shu yerda faqat KO'RSATILADI: shablon Excel'dan aynan seed qilinadi va
 * uni erkin tahrirlash "qaysi band qaysi mezonga bog'langan" bog'lamini jimgina uzib
 * qo'yardi. Kerak bo'lganda tahrir ALOHIDA ish sifatida qo'shiladi.</p>
 */
function ChecklistSection() {
  const [templates, setTemplates] = useState<KpiChecklistTemplateDto[]>([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const [tick, setTick] = useState(0)

  useEffect(() => {
    let alive = true
    getChecklistTemplates()
      .then((list) => {
        if (!alive) return
        setTemplates(list)
        setError(null)
        setLoading(false)
      })
      .catch((err) => {
        if (!alive) return
        setError(apiErrorMessage(err, 'Shablonlarni yuklab bo‘lmadi'))
        setLoading(false)
      })
    return () => {
      alive = false
    }
  }, [tick])

  const reload = useCallback(() => setTick((t) => t + 1), [])

  if (loading) return <Loader label="Yuklanmoqda..." />

  return (
    <div className="space-y-5">
      {error && <ErrorBanner text={error} onRetry={reload} />}

      {templates.length === 0 ? (
        <Card>
          <EmptyState
            title="Cheklist shabloni yo'q"
            hint="Yuqoridagi «Excel qiymatlarini yuklash» tugmasi ikkala rolning kunlik cheklistini qo'shadi."
          />
        </Card>
      ) : (
        templates.map((t) => <TemplateCard key={t.id} template={t} />)
      )}
    </div>
  )
}

function TemplateCard({ template }: { template: KpiChecklistTemplateDto }) {
  // Bandlar VAQT BLOKLARIGA guruhlanadi — «Bugun» sahifasidagi ko'rinish bilan bir xil.
  const blocks: { name: string; items: KpiChecklistTemplateDto['items'] }[] = []
  for (const item of [...template.items].sort((a, b) => a.order - b.order || a.no - b.no)) {
    const last = blocks[blocks.length - 1]
    if (last && last.name === item.timeBlock) last.items.push(item)
    else blocks.push({ name: item.timeBlock, items: [item] })
  }
  const autoCount = template.items.filter((i) => i.autoCheckKey).length

  return (
    <Card
      tight
      title={`${template.roleLabel || roleLabel(template.roleCode)} — ${template.name}`}
      sub={`${template.items.length} band · ${blocks.length} vaqt bloki · ${autoCount} tasi avtomatlashtirishga tayyor`}
      actions={template.isActive ? <Badge tone="green">faol</Badge> : <Badge>faol emas</Badge>}
    >
      <div className="divide-y divide-slate-100">
        {blocks.map((b) => (
          <div key={b.name} className="px-[18px] py-3">
            <p className="mb-2 text-[11px] font-bold uppercase tracking-wide text-slate-400">
              {b.name}
            </p>
            <div className="space-y-1.5">
              {b.items.map((i) => (
                <div key={i.id} className="flex items-start gap-2.5">
                  <span className="mt-0.5 w-6 shrink-0 text-right font-mono text-[11.5px] text-slate-300">
                    {i.no}
                  </span>
                  <div className="min-w-0 flex-1">
                    <p className="text-[13px] text-slate-700">{i.text}</p>
                    <div className="mt-0.5 flex flex-wrap items-center gap-1.5">
                      {i.norm && (
                        <span className="text-[11px] text-slate-400">Norma: {i.norm}</span>
                      )}
                      {i.kpiTag && <Badge tone="violet">{i.kpiTag}</Badge>}
                      {i.criterionNo != null && <Badge tone="blue">{i.criterionNo}-mezon</Badge>}
                      {i.autoCheckKey && (
                        <Badge tone="teal">avtomatik</Badge>
                      )}
                    </div>
                  </div>
                </div>
              ))}
            </div>
          </div>
        ))}
      </div>
      <p className="border-t border-slate-100 px-[18px] py-3 text-[11.5px] text-slate-400">
        «Avtomatik» belgisi — band uchun tizim tekshiruvi TAYYORLANGAN. Qo'llanmagan kalitlar
        jimgina e'tiborsiz qoldiriladi: bunday band «Bugun» sahifasida oddiy QO'LDA belgilanadi.
      </p>
    </Card>
  )
}
