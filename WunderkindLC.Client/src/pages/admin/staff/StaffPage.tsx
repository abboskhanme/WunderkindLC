import { useEffect, useMemo, useRef, useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { Plus, Eye, Pencil, Trash2, Users, ShieldCheck, ShieldOff, Search, ExternalLink } from 'lucide-react'
import type { Staff, Credentials, StaffRoleTemplate, Subject, Teacher } from '@/types'
import {
  getStaff,
  getStaffRoleTemplates,
  createStaff,
  updateStaff,
  deleteStaff,
  getStaffCredentials,
  resetStaffPassword,
  setStaffRole,
  type CreateStaffWithTemplatePayload,
} from '@/api/services/staff'
import { getTeachers, createTeacher, getTeacherCredentials, type TeacherPayload } from '@/api/services/teachers'
import { getSubjects } from '@/api/services/subjects'
import { teacherTabs } from '@/config/sectionTabs'
import { useAuth } from '@/context/auth-context'
import { usePerm } from '@/lib/permissions'
import { canManageStaffRoles, TEACHER_ROLE_ID } from '@/lib/staffRoles'
import { toast } from '@/lib/toast'
import { apiErrorMessage, cn, randomPassword } from '@/lib/utils'
import { PermChips } from '@/components/staff/PermChips'
import { Card } from '@/components/ui/Card'
import { CardTabs } from '@/components/ui/CardTabs'
import { Button } from '@/components/ui/Button'
import { Badge } from '@/components/ui/Badge'
import { PageHeader } from '@/components/ui/PageHeader'
import { Loader } from '@/components/ui/Loader'
import { Modal } from '@/components/ui/Modal'
import { Input, Select } from '@/components/ui/Input'
import { PhoneInput } from '@/components/ui/PhoneInput'
import { CredentialsBox } from '@/components/ui/CredentialsBox'
import { ReasonPromptModal } from '@/components/ui/ReasonPromptModal'
import { TeacherFormModal } from '@/pages/admin/teachers/TeacherFormModal'

// Avatar uchun ism harflari (faqat ko'rinish uchun)
const initialsOf = (name: string) =>
  name
    .split(' ')
    .filter(Boolean)
    .slice(0, 2)
    .map((s) => s[0]?.toUpperCase())
    .join('')

const AVATAR_COLORS = ['#7c3aed', '#0ea5e9', '#10b981', '#f59e0b', '#ef4444', '#6366f1', '#ec4899', '#14b8a6']
const avatarColor = (name: string) => {
  let h = 0
  for (let i = 0; i < name.length; i++) h = (h * 31 + name.charCodeAt(i)) >>> 0
  return AVATAR_COLORS[h % AVATAR_COLORS.length]
}

/** Ro'yxat qatori — o'qituvchi ham, panel xodimi ham BITTA ko'rinishda. */
type Row =
  | { kind: 'teacher'; id: string; fullName: string; phone: string; teacher: Teacher }
  | { kind: 'staff'; id: string; fullName: string; phone: string; staff: Staff }

/** Filtr kaliti: 'all' | 'teacher' | 'admin' | 'individual' | <rol id>. */
type Filter = string

/**
 * BOSHQARUV → XODIMLAR. Markazning BARCHA xodimlari (o'qituvchilar + panel akkauntlari) bitta
 * ro'yxatda va ROL orqali qo'shiladi:
 *   • rol "O'qituvchi" → o'qituvchi formasi (oylik maosh FOIZI bilan);
 *   • boshqa rol (Boshqaruv → Rollar) → xodim akkaunti, ruxsatlari ROLDAN (jonli bog'lanish).
 *
 * Ruxsatlarning o'zi (qaysi rol nimani ko'radi) — «Rollar» sahifasida. Bu yerda per-xodim ruxsat
 * matritsasi ATAYIN yo'q: xodimning huquqi uning roli bilan belgilanadi.
 */
export function StaffPage() {
  const { user } = useAuth()
  const { can } = usePerm()
  const navigate = useNavigate()
  const isSuperAdmin = user?.role === 'superadmin'
  const canRoles = canManageStaffRoles(user?.role, user?.permissions)
  const canSeeStaff = can('staff', 'view')
  const canSeeTeachers = can('teachers.list', 'view')
  const canAddTeacher = can('teachers.list', 'create')
  const canAddStaff = can('staff', 'create') && canRoles
  // Login/parol — serverdagi `HasFullAccess` bilan bir xil (aks holda "saqlandi" dan keyin 403).
  const canTeacherCreds =
    user?.role === 'superadmin' || user?.role === 'admin' || !!user?.permissions?.includes('teachers.list')

  const [staff, setStaff] = useState<Staff[]>([])
  const [teachers, setTeachers] = useState<Teacher[]>([])
  const [roles, setRoles] = useState<StaffRoleTemplate[]>([])
  const [subjects, setSubjects] = useState<Subject[]>([])
  const [loading, setLoading] = useState(true)
  const [loadError, setLoadError] = useState<string | null>(null)
  const [tick, setTick] = useState(0)

  const [filter, setFilter] = useState<Filter>('all')
  const [query, setQuery] = useState('')

  // Qo'shish: avval ROL tanlanadi.
  const [addOpen, setAddOpen] = useState(false)
  const [addRole, setAddRole] = useState('')
  const [teacherFormOpen, setTeacherFormOpen] = useState(false)

  // Xodim (panel akkaunti) formasi — yaratish ham, tahrirlash ham.
  const [staffForm, setStaffForm] = useState<(CreateStaffWithTemplatePayload & { id?: string }) | null>(null)
  const [saving, setSaving] = useState(false)
  const [formError, setFormError] = useState<string | null>(null)
  const inFlightRef = useRef(false)

  // Login/parol oynasi
  const [credOf, setCredOf] = useState<Row | null>(null)
  const [creds, setCreds] = useState<Credentials | null>(null)
  const [credLoading, setCredLoading] = useState(false)
  const [deleting, setDeleting] = useState<Staff | null>(null)
  const [roleBusyId, setRoleBusyId] = useState<string | null>(null)

  useEffect(() => {
    let alive = true
    Promise.all([
      canSeeStaff ? getStaff() : Promise.resolve<Staff[]>([]),
      canSeeTeachers ? getTeachers() : Promise.resolve<Teacher[]>([]),
      canSeeStaff ? getStaffRoleTemplates() : Promise.resolve<StaffRoleTemplate[]>([]),
    ])
      .then(([s, t, r]) => {
        if (!alive) return
        setStaff(s)
        setTeachers(t)
        setRoles(r)
        setLoadError(null)
      })
      .catch((e) => alive && setLoadError(apiErrorMessage(e, "Xodimlar ro'yxatini yuklab bo'lmadi")))
      .finally(() => alive && setLoading(false))
    return () => {
      alive = false
    }
  }, [tick, canSeeStaff, canSeeTeachers])

  const rows = useMemo<Row[]>(() => {
    const list: Row[] = [
      ...teachers.map((t) => ({ kind: 'teacher' as const, id: t.id, fullName: t.fullName, phone: t.phone ?? '', teacher: t })),
      ...staff.map((s) => ({ kind: 'staff' as const, id: s.id, fullName: s.fullName, phone: s.phone ?? '', staff: s })),
    ]
    return list.sort((a, b) => a.fullName.localeCompare(b.fullName))
  }, [teachers, staff])

  const filterOf = (r: Row): string =>
    r.kind === 'teacher'
      ? TEACHER_ROLE_ID
      : (r.staff.role ?? 'staff') !== 'staff'
        ? 'admin'
        : r.staff.roleTemplateId || 'individual'

  const visible = useMemo(() => {
    const q = query.trim().toLowerCase()
    const digits = q.replace(/\D/g, '')
    return rows.filter((r) => {
      if (filter !== 'all' && filterOf(r) !== filter) return false
      if (!q) return true
      return (
        r.fullName.toLowerCase().includes(q) ||
        (digits.length >= 3 && r.phone.replace(/\D/g, '').includes(digits))
      )
    })
  }, [rows, filter, query])

  const counts = useMemo(() => {
    const m: Record<string, number> = {}
    rows.forEach((r) => (m[filterOf(r)] = (m[filterOf(r)] ?? 0) + 1))
    return m
  }, [rows])

  const filterChips: { key: string; label: string }[] = [
    { key: 'all', label: 'Hammasi' },
    ...(canSeeTeachers ? [{ key: TEACHER_ROLE_ID, label: "O'qituvchilar" }] : []),
    ...roles.map((r) => ({ key: r.id, label: r.name })),
    ...(counts.individual ? [{ key: 'individual', label: 'Rolsiz' }] : []),
    ...(counts.admin ? [{ key: 'admin', label: 'Admin / Superadmin' }] : []),
  ]

  // ---------- Qo'shish ----------

  const openAdd = () => {
    setAddRole(canAddTeacher ? TEACHER_ROLE_ID : (roles[0]?.id ?? ''))
    setAddOpen(true)
    // Fanlar — ikkilamchi ma'lumot (faqat fan tanlash chiplari): yuklanmasa forma baribir ishlaydi,
    // ichida "Avval fan qo'shing" yoziladi (`error-visibility.md` §4).
    if (canAddTeacher && subjects.length === 0) getSubjects().then(setSubjects).catch(() => setSubjects([]))
  }

  const continueAdd = () => {
    if (!addRole) return
    setAddOpen(false)
    if (addRole === TEACHER_ROLE_ID) {
      setTeacherFormOpen(true)
      return
    }
    setFormError(null)
    setStaffForm({ fullName: '', position: '', phone: '', roleTemplateId: addRole })
  }

  const openCredentials = (r: Row) => {
    setCredOf(r)
    setCreds(null)
    setCredLoading(true)
    ;(r.kind === 'teacher' ? getTeacherCredentials(r.id) : getStaffCredentials(r.id))
      .then(setCreds)
      .catch((e) => {
        setCredOf(null)
        alert(apiErrorMessage(e, "Login/parolni olib bo'lmadi"))
      })
      .finally(() => setCredLoading(false))
  }

  const submitTeacher = async (values: TeacherPayload) => {
    try {
      const t = await createTeacher(values)
      toast.success("O'qituvchi qo'shildi", `${t.fullName} — ${values.salaryPercent ?? 0}%`)
      setTeacherFormOpen(false)
      setTick((x) => x + 1)
      if (canTeacherCreds)
        openCredentials({ kind: 'teacher', id: t.id, fullName: t.fullName, phone: t.phone ?? '', teacher: t })
    } catch (e) {
      // Forma OCHIQ qoladi — kiritilgan ma'lumot yo'qolmasin.
      alert(apiErrorMessage(e, "O'qituvchini qo'shib bo'lmadi"))
    }
  }

  const submitStaff = async (e: React.FormEvent) => {
    e.preventDefault()
    if (!staffForm || inFlightRef.current) return
    if (!staffForm.fullName.trim()) {
      setFormError('F.I.SH kiriting')
      return
    }
    inFlightRef.current = true
    setSaving(true)
    setFormError(null)
    try {
      const { id, ...payload } = staffForm
      if (id) {
        await updateStaff(id, payload)
        toast.success('Xodim saqlandi', payload.fullName)
        setStaffForm(null)
        setTick((x) => x + 1)
      } else {
        const created = await createStaff(payload)
        toast.success("Xodim qo'shildi", created.roleName ? `${created.fullName} — ${created.roleName}` : created.fullName)
        setStaffForm(null)
        setTick((x) => x + 1)
        if (canRoles)
          openCredentials({ kind: 'staff', id: created.id, fullName: created.fullName, phone: created.phone ?? '', staff: created })
      }
    } catch (err) {
      setFormError(apiErrorMessage(err, "Saqlab bo'lmadi"))
    } finally {
      inFlightRef.current = false
      setSaving(false)
    }
  }

  const openEditStaff = (s: Staff) => {
    setFormError(null)
    setStaffForm({
      id: s.id,
      fullName: s.fullName,
      position: s.position,
      phone: s.phone ?? '',
      roleTemplateId: s.roleTemplateId ?? '',
    })
  }

  const doDelete = (reasonId?: string) => {
    const s = deleting
    if (!s) return
    return deleteStaff(s.id, reasonId)
      .then(() => {
        toast.success("Xodim o'chirildi", s.fullName)
        setDeleting(null)
        setTick((x) => x + 1)
      })
      .catch((e) => alert(apiErrorMessage(e, "O'chirib bo'lmadi")))
  }

  /**
   * "Superadmin qilish" / "Superadminlikni olib tashlash" — faqat superadmin. Superadminda bo'lim
   * ruxsatlari umuman tekshirilmaydi, shuning uchun tasdiq so'raladi. Olib tashlanganda akkaunt
   * xodimga qaytadi va roli (yoki individual ruxsatlari) yana kuchga kiradi.
   */
  const toggleSuperAdmin = async (s: Staff) => {
    const makeSuper = s.role !== 'superadmin'
    const msg = makeSuper
      ? `"${s.fullName}" TO'LIQ superadmin bo'ladi: barcha bo'limlar, moliyani tahrirlash, o'chirish va sozlamalar ochiladi. Davom etamizmi?`
      : `"${s.fullName}" superadminlikdan olinadi va oddiy xodimga qaytadi (roli bo'yicha). Davom etamizmi?`
    if (!confirm(msg)) return
    setRoleBusyId(s.id)
    try {
      await setStaffRole(s.id, makeSuper ? 'superadmin' : 'staff')
      toast.success(makeSuper ? 'Superadmin qilindi' : 'Superadminlik olib tashlandi', s.fullName)
      setTick((x) => x + 1)
    } catch (e) {
      alert(apiErrorMessage(e, "Rolni o'zgartirib bo'lmadi"))
    } finally {
      setRoleBusyId(null)
    }
  }

  const roleBadge = (r: Row) => {
    if (r.kind === 'teacher') {
      const t = r.teacher
      return (
        <div className="flex flex-wrap items-center gap-1.5">
          <Badge tone="teal">O'qituvchi</Badge>
          {t.salaryMode === 'percent' ? (
            <span className="font-mono text-xs text-slate-500">{t.salaryPercent ?? 0}%</span>
          ) : (
            <span className="text-xs text-slate-400">guruh bo'yicha</span>
          )}
        </div>
      )
    }
    const s = r.staff
    if (s.role === 'superadmin') return <Badge tone="amber">Superadmin</Badge>
    if (s.role === 'admin') return <Badge tone="violet">Admin</Badge>
    return s.roleName ? <Badge tone="blue">{s.roleName}</Badge> : <Badge>Rolsiz (individual)</Badge>
  }

  const editingStaff = staffForm?.id ? staff.find((x) => x.id === staffForm.id) : undefined
  const addChoices = [
    ...(canAddTeacher ? [{ id: TEACHER_ROLE_ID, name: "O'qituvchi" }] : []),
    ...(canAddStaff ? roles.map((r) => ({ id: r.id, name: r.name })) : []),
  ]

  return (
    <div>
      <PageHeader
        title="Xodimlar"
        sub="Markazning barcha xodimlari. Yangi xodim ROL tanlab qo'shiladi; o'qituvchi uchun oylik maosh foizi belgilanadi. Rollar va ularning ruxsatlari — «Rollar» sahifasida."
        actions={
          addChoices.length > 0 && (
            <Button onClick={openAdd}>
              <Plus className="h-4 w-4" /> Xodim qo'shish
            </Button>
          )
        }
      />
      <CardTabs items={teacherTabs((p) => can(p, 'view'))} className="mb-5" />

      {loadError && (
        <div className="mb-4 rounded-lg border border-red-200 bg-red-50 px-3 py-2 text-sm text-red-700">{loadError}</div>
      )}

      <div className="mb-3 flex flex-wrap items-center gap-2">
        <div className="relative w-full sm:w-72">
          <Search className="pointer-events-none absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-slate-400" />
          <input
            value={query}
            onChange={(e) => setQuery(e.target.value)}
            placeholder="Ism yoki telefon..."
            className="w-full rounded-lg border border-slate-200 py-2 pl-9 pr-3 text-sm outline-none focus:border-brand-400"
          />
        </div>
        <div className="flex flex-wrap gap-1.5">
          {filterChips.map((c) => (
            <button
              key={c.key}
              type="button"
              onClick={() => setFilter(c.key)}
              className={cn(
                'rounded-full border px-3 py-1 text-xs font-medium transition-colors',
                filter === c.key
                  ? 'border-brand-500 bg-brand-50 text-brand-700'
                  : 'border-slate-200 text-slate-600 hover:bg-slate-50',
              )}
            >
              {c.label}
              <span className="ml-1 opacity-60">{c.key === 'all' ? rows.length : (counts[c.key] ?? 0)}</span>
            </button>
          ))}
        </div>
      </div>

      {loading ? (
        <Card>
          <Loader label="Yuklanmoqda..." />
        </Card>
      ) : visible.length === 0 ? (
        <Card>
          <div className="state">
            <div className="state-icon">
              <Users className="h-6 w-6" />
            </div>
            <h4>{rows.length === 0 ? "Hali xodim qo'shilmagan" : 'Hech kim topilmadi'}</h4>
            <p>{rows.length === 0 ? "«Xodim qo'shish» tugmasi orqali qo'shing." : "Filtr yoki qidiruvni o'zgartiring."}</p>
          </div>
        </Card>
      ) : (
        <Card className="overflow-hidden p-0">
          <div className="overflow-x-auto">
            <table className="w-full text-sm">
              <thead>
                <tr className="border-b border-slate-100 text-left text-xs uppercase text-slate-400">
                  <th className="px-4 py-2.5 font-medium">Xodim</th>
                  <th className="px-4 py-2.5 font-medium">Rol</th>
                  <th className="px-4 py-2.5 font-medium">Telefon</th>
                  <th className="px-4 py-2.5 text-right font-medium">Amallar</th>
                </tr>
              </thead>
              <tbody>
                {visible.map((r) => {
                  const isStaffRow = r.kind === 'staff' && (r.staff.role ?? 'staff') === 'staff'
                  const isMe = r.kind === 'staff' && user?.id === r.id
                  const isSuper = r.kind === 'staff' && r.staff.role === 'superadmin'
                  return (
                    <tr key={`${r.kind}:${r.id}`} className="border-b border-slate-50 last:border-0 hover:bg-slate-50/60">
                      <td className="px-4 py-2.5">
                        <div className="cell-user">
                          <div className="avatar h-8 w-8 text-xs" style={{ background: avatarColor(r.fullName) }}>
                            {initialsOf(r.fullName)}
                          </div>
                          <div className="meta">
                            <strong className="text-slate-800">{r.fullName}</strong>
                            {r.kind === 'staff' && r.staff.position && (
                              <span className="text-slate-400">{r.staff.position}</span>
                            )}
                          </div>
                        </div>
                      </td>
                      <td className="px-4 py-2.5">{roleBadge(r)}</td>
                      <td className="px-4 py-2.5 font-mono text-xs text-slate-500">{r.phone || '—'}</td>
                      <td className="px-4 py-2.5">
                        <div className="flex items-center justify-end gap-0.5">
                          {r.kind === 'teacher' ? (
                            <IconBtn title="O'qituvchi kartasi (maosh, guruhlar)" onClick={() => navigate(`/admin/teachers/${r.id}`)}>
                              <ExternalLink className="h-4 w-4" />
                            </IconBtn>
                          ) : (
                            <>
                              {isSuperAdmin && !isMe && (
                                <IconBtn
                                  title={isSuper ? 'Superadminlikni olib tashlash' : 'Superadmin qilish'}
                                  disabled={roleBusyId === r.id}
                                  onClick={() => void toggleSuperAdmin(r.staff)}
                                >
                                  {isSuper ? <ShieldOff className="h-4 w-4" /> : <ShieldCheck className="h-4 w-4" />}
                                </IconBtn>
                              )}
                              {/* Login/parol superadmin qilingan akkauntda ham (faqat superadminga) — aks
                                  holda rol berilishi bilan akkauntni boshqarib bo'lmay qolardi. */}
                              {canRoles && (isStaffRow || (isSuperAdmin && !isMe)) && (
                                <IconBtn title="Login/parol" onClick={() => openCredentials(r)}>
                                  <Eye className="h-4 w-4" />
                                </IconBtn>
                              )}
                              {isStaffRow && can('staff', 'edit') && (
                                <IconBtn title="Tahrirlash" onClick={() => openEditStaff(r.staff)}>
                                  <Pencil className="h-4 w-4" />
                                </IconBtn>
                              )}
                              {isStaffRow && can('staff', 'delete') && (
                                <IconBtn title="O'chirish" danger onClick={() => setDeleting(r.staff)}>
                                  <Trash2 className="h-4 w-4" />
                                </IconBtn>
                              )}
                            </>
                          )}
                        </div>
                      </td>
                    </tr>
                  )
                })}
              </tbody>
            </table>
          </div>
        </Card>
      )}

      {/* 1-qadam: ROL tanlash */}
      <Modal
        open={addOpen}
        onClose={() => setAddOpen(false)}
        size="sm"
        title="Xodim qo'shish"
        footer={
          <>
            <Button variant="secondary" onClick={() => setAddOpen(false)}>
              Bekor qilish
            </Button>
            <Button onClick={continueAdd} disabled={!addRole}>
              Davom etish
            </Button>
          </>
        }
      >
        <div className="space-y-3">
          <Select label="Rol" value={addRole} onChange={(e) => setAddRole(e.target.value)}>
            {addChoices.map((c) => (
              <option key={c.id} value={c.id}>
                {c.name}
              </option>
            ))}
          </Select>
          {addRole === TEACHER_ROLE_ID ? (
            <p className="text-xs text-slate-500">
              O'qituvchi portaliga kiradi. Keyingi oynada <b>oylik maosh foizi</b> so'raladi.
            </p>
          ) : (
            (() => {
              const r = roles.find((x) => x.id === addRole)
              return r ? (
                <div>
                  <p className="mb-1.5 text-xs text-slate-500">Bu rolning ruxsatlari:</p>
                  <PermChips permissions={r.defaultPermissions} />
                </div>
              ) : null
            })()
          )}
          {canAddTeacher && !canAddStaff && (
            <p className="text-xs text-slate-400">Boshqa rollarda xodim qo'shish uchun «Xodimlar» bo'limiga to'liq ruxsat kerak.</p>
          )}
        </div>
      </Modal>

      <TeacherFormModal
        open={teacherFormOpen}
        onClose={() => setTeacherFormOpen(false)}
        onSubmit={submitTeacher}
        initial={null}
        subjects={subjects}
      />

      {/* Panel xodimi — yaratish/tahrirlash */}
      <Modal
        open={!!staffForm}
        onClose={() => setStaffForm(null)}
        title={staffForm?.id ? 'Xodimni tahrirlash' : "Yangi xodim"}
        footer={
          <>
            <Button variant="secondary" onClick={() => setStaffForm(null)}>
              Bekor qilish
            </Button>
            <Button type="submit" form="staff-form" disabled={saving}>
              {saving ? 'Saqlanmoqda...' : 'Saqlash'}
            </Button>
          </>
        }
      >
        {staffForm && (
          <form id="staff-form" onSubmit={(e) => void submitStaff(e)} className="space-y-4">
            {formError && (
              <div className="rounded-lg border border-red-200 bg-red-50 px-3 py-2 text-sm text-red-700">{formError}</div>
            )}
            <Input
              label="F.I.SH"
              required
              value={staffForm.fullName}
              onChange={(e) => setStaffForm((f) => f && { ...f, fullName: e.target.value })}
            />
            <PhoneInput
              label="Telefon"
              value={staffForm.phone ?? ''}
              onChange={(phone) => setStaffForm((f) => f && { ...f, phone })}
            />
            {canRoles && (
              <Select
                label="Rol"
                value={staffForm.roleTemplateId ?? ''}
                onChange={(e) => setStaffForm((f) => f && { ...f, roleTemplateId: e.target.value })}
              >
                {/* Rolsiz variant FAQAT hozir rolsiz bo'lgan (eski) xodimga — yangi xodim doim rol bilan. */}
                {staffForm.id && !editingStaff?.roleTemplateId && (
                  <option value="">— Rolsiz (hozirgi individual ruxsatlar) —</option>
                )}
                {roles.map((r) => (
                  <option key={r.id} value={r.id}>
                    {r.name}
                  </option>
                ))}
              </Select>
            )}
            <Input
              label="Lavozim (ixtiyoriy)"
              value={staffForm.position}
              onChange={(e) => setStaffForm((f) => f && { ...f, position: e.target.value })}
              placeholder="Bo'sh qoldirilsa — rol nomi"
            />
            {/* Parol almashtirish = akkauntga kirish — server `HasFullAccess("staff")` talab qiladi. */}
            {staffForm.id && canRoles && (
              <div>
                <label className="mb-1 block text-sm font-medium text-slate-600">Parolni almashtirish</label>
                <div className="flex items-start gap-2">
                  <input
                    type="text"
                    autoComplete="new-password"
                    placeholder="Bo'sh qoldirilsa — parol o'zgarmaydi"
                    value={staffForm.newPassword ?? ''}
                    onChange={(e) => setStaffForm((f) => f && { ...f, newPassword: e.target.value })}
                    className="w-full rounded-lg border border-slate-200 px-3 py-2 text-sm text-slate-800 outline-none focus:border-brand-400 focus:ring-2 focus:ring-brand-100"
                  />
                  <Button
                    type="button"
                    variant="secondary"
                    onClick={() => setStaffForm((f) => f && { ...f, newPassword: randomPassword() })}
                  >
                    Generatsiya
                  </Button>
                </div>
              </div>
            )}
            {editingStaff && !editingStaff.roleTemplateId && (
              <div>
                <p className="mb-1.5 text-xs text-slate-500">Hozirgi individual ruxsatlari:</p>
                <PermChips permissions={editingStaff.permissions} />
              </div>
            )}
            {!staffForm.id && (
              <p className="text-xs text-slate-400">
                Saqlangach tizimga kirish uchun login va parol avtomatik yaratiladi va ko'rsatiladi.
              </p>
            )}
          </form>
        )}
      </Modal>

      {/* Login/parol */}
      <Modal
        open={!!credOf}
        onClose={() => setCredOf(null)}
        title={credOf ? `${credOf.fullName} — akkaunt` : 'Akkaunt'}
        footer={
          <Button variant="secondary" onClick={() => setCredOf(null)}>
            Yopish
          </Button>
        }
      >
        <CredentialsBox
          credentials={creds}
          loading={credLoading}
          onReset={
            credOf?.kind === 'staff'
              ? async () => {
                  const c = await resetStaffPassword(credOf.id)
                  setCreds(c)
                }
              : undefined
          }
        />
      </Modal>

      <ReasonPromptModal
        open={!!deleting}
        category="staff_delete"
        title="Xodimni o'chirish"
        message={deleting ? `"${deleting.fullName}" xodimni o'chirasizmi? Akkaunti ham o'chadi.` : undefined}
        confirmLabel="O'chirish"
        tone="red"
        onConfirm={doDelete}
        onClose={() => setDeleting(null)}
      />
    </div>
  )
}

function IconBtn({
  title,
  onClick,
  danger,
  disabled,
  children,
}: {
  title: string
  onClick: () => void
  danger?: boolean
  disabled?: boolean
  children: React.ReactNode
}) {
  return (
    <button
      type="button"
      title={title}
      onClick={onClick}
      disabled={disabled}
      className={cn(
        'rounded-lg p-1.5 transition-colors disabled:opacity-40',
        danger
          ? 'text-slate-400 hover:bg-red-50 hover:text-red-600'
          : 'text-slate-400 hover:bg-slate-100 hover:text-slate-700',
      )}
    >
      {children}
    </button>
  )
}
