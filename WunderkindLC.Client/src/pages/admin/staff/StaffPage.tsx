import { useEffect, useState } from 'react'
import { Plus, Eye, Pencil, Trash2, Users, ShieldCheck, ShieldOff } from 'lucide-react'
import type { Staff, Credentials, StaffRoleTemplate } from '@/types'
import {
  getStaff,
  getStaffRoleTemplates,
  createStaff,
  updateStaff,
  deleteStaff,
  getStaffCredentials,
  resetStaffPassword,
  setStaffPermissions,
  setStaffRole,
  type CreateStaffWithTemplatePayload,
} from '@/api/services/staff'
import { adminPermissions, permPagesOf } from '@/config/constants'
import { useAuth } from '@/context/auth-context'
import {
  toggleSectionRow,
  togglePageRow,
  sectionActions,
  sectionRowActions,
  type PermAction,
} from '@/lib/permissions'
import { PermMatrix } from '@/components/staff/PermMatrix'
import { apiErrorMessage, cn, randomPassword } from '@/lib/utils'
import { Card } from '@/components/ui/Card'
import { Button } from '@/components/ui/Button'
import { Badge } from '@/components/ui/Badge'
import { PageHeader } from '@/components/ui/PageHeader'
import { Loader } from '@/components/ui/Loader'
import { Modal } from '@/components/ui/Modal'
import { Input } from '@/components/ui/Input'
import { PhoneInput } from '@/components/ui/PhoneInput'
import { CredentialsBox } from '@/components/ui/CredentialsBox'
import { ReasonPromptModal } from '@/components/ui/ReasonPromptModal'

const POSITIONS = ['Kassir', 'Administrator', "Direktor o'rinbosari", 'Qorovul', 'Hisobchi']

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

/** Akkaunt roli yorlig'i — ro'yxatda chip sifatida ko'rinadi. */
const roleLabel = (role?: string) =>
  role === 'superadmin' ? 'Superadmin' : role === 'admin' ? 'Admin' : 'Xodim'

export function StaffPage() {
  const { user } = useAuth()
  // Rollar (ruxsatlar)ni faqat tizim egasi (superadmin) o'zgartira oladi — backend ham shuni talab qiladi.
  const canManageRoles = user?.role === 'superadmin'

  const [staff, setStaff] = useState<Staff[]>([])
  const [templates, setTemplates] = useState<StaffRoleTemplate[]>([])
  const [loading, setLoading] = useState(true)
  const [formOpen, setFormOpen] = useState(false)
  const [editing, setEditing] = useState<Staff | null>(null)
  const [form, setForm] = useState<CreateStaffWithTemplatePayload>({ fullName: '', position: '' })
  // Rol shabloni (ixtiyoriy — matritsani oldindan to'ldiradi) + yangi xodim ruxsat matritsasi.
  const [selectedTemplate, setSelectedTemplate] = useState<string | null>(null)
  const [createPerms, setCreatePerms] = useState<Set<string>>(new Set())
  const [saving, setSaving] = useState(false)

  // Har bir xodim uchun tahrirlanayotgan ruxsatlar (id → kalitlar to'plami)
  const [draft, setDraft] = useState<Record<string, Set<string>>>({})
  const [savingPermsId, setSavingPermsId] = useState<string | null>(null)

  // Login/parol oynasi
  const [credOf, setCredOf] = useState<Staff | null>(null)
  const [creds, setCreds] = useState<Credentials | null>(null)
  const [credLoading, setCredLoading] = useState(false)
  const [deleting, setDeleting] = useState<Staff | null>(null)
  /** Rol almashtirilayotgan akkaunt id'si (tugma bloklanadi) */
  const [roleBusyId, setRoleBusyId] = useState<string | null>(null)

  const syncDraft = (list: Staff[]) =>
    setDraft(Object.fromEntries(list.map((s) => [s.id, new Set(s.permissions)])))

  useEffect(() => {
    getStaff()
      .then((list) => {
        setStaff(list)
        syncDraft(list)
      })
      .finally(() => setLoading(false))
    // Shablonlar — ixtiyoriy; yuklanmasa xodim ro'yxati buzilmaydi.
    getStaffRoleTemplates()
      .then(setTemplates)
      .catch(() => setTemplates([]))
  }, [])

  const openCreate = () => {
    setEditing(null)
    setForm({ fullName: '', position: '', phone: '' })
    setSelectedTemplate(null)
    setCreatePerms(new Set())
    setFormOpen(true)
  }

  // Rol shabloni tanlanganda — matritsani shablonning default ruxsatlaridan to'ldiramiz (keyin qo'lda o'zgartirish mumkin).
  const applyTemplate = (code: string | null) => {
    setSelectedTemplate(code)
    if (code) {
      const t = templates.find((x) => x.code === code)
      if (t) setCreatePerms(new Set(t.defaultPermissions))
    }
  }
  const openEdit = (s: Staff) => {
    setEditing(s)
    setForm({ fullName: s.fullName, position: s.position, phone: s.phone ?? '' })
    setFormOpen(true)
  }

  const showCredentials = (s: Staff) => {
    setCredOf(s)
    setCreds(null)
    setCredLoading(true)
    getStaffCredentials(s.id)
      .then(setCreds)
      .finally(() => setCredLoading(false))
  }

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault()
    if (!form.fullName.trim()) return
    setSaving(true)
    try {
      if (editing) {
        const u = await updateStaff(editing.id, form)
        setStaff((p) => p.map((x) => (x.id === u.id ? u : x)))
        setFormOpen(false)
      } else {
        // Ruxsatlar to'g'ridan-to'g'ri matritsadan (shablon faqat oldindan to'ldirish uchun edi).
        const payload: CreateStaffWithTemplatePayload = {
          ...form,
          extraPermissions: createPerms.size > 0 ? [...createPerms] : undefined,
        }
        const created = await createStaff(payload)
        setStaff((p) => [created, ...p])
        setDraft((d) => ({ ...d, [created.id]: new Set(created.permissions) }))
        setFormOpen(false)
        showCredentials(created)
      }
    } catch (err: any) {
      alert(err?.response?.data?.message ?? "Saqlab bo'lmadi")
    } finally {
      setSaving(false)
    }
  }

  const handleDelete = (s: Staff) => setDeleting(s)

  const doDelete = (reasonId?: string) => {
    const s = deleting
    if (!s) return
    deleteStaff(s.id, reasonId)
      .then(() => {
        setStaff((p) => p.filter((x) => x.id !== s.id))
        setDraft((d) => {
          const { [s.id]: _, ...rest } = d
          return rest
        })
        setDeleting(null)
      })
      .catch((e) => alert(e?.response?.data?.message ?? "O'chirib bo'lmadi"))
  }

  /** BO'LIM qatori — bo'limning barcha sahifalari uchun. */
  const toggleSection = (staffId: string, section: string, action: PermAction) =>
    setDraft((d) => ({
      ...d,
      [staffId]: toggleSectionRow(
        d[staffId] ?? new Set<string>(),
        section,
        action,
        permPagesOf(section),
      ),
    }))

  /** SAHIFA qatori — faqat shu sahifa (bo'lim tokeni kerak bo'lsa sahifalarga yoyiladi). */
  const togglePage = (staffId: string, section: string, page: string, action: PermAction) =>
    setDraft((d) => ({
      ...d,
      [staffId]: togglePageRow(
        d[staffId] ?? new Set<string>(),
        section,
        page,
        action,
        permPagesOf(section),
      ),
    }))

  const dirty = (s: Staff) => {
    const cur = draft[s.id] ?? new Set()
    return cur.size !== s.permissions.length || s.permissions.some((p) => !cur.has(p))
  }

  /**
   * "Superadmin qilish" / "Superadminlikni olib tashlash". Superadminda bo'lim ruxsatlari umuman
   * tekshirilmaydi — shu sabab qaytarib bo'lmaydigan amal emasligi ta'kidlanib, tasdiq so'raladi.
   * Olib tashlanganda akkaunt xodimga (staff) qaytadi va eski ruxsat matritsasi yana kuchga kiradi.
   */
  const toggleSuperAdmin = async (s: Staff) => {
    const makeSuper = s.role !== 'superadmin'
    const msg = makeSuper
      ? `"${s.fullName}" TO'LIQ superadmin bo'ladi: barcha bo'limlar, moliyani tahrirlash, o'chirish va sozlamalar ochiladi. Davom etamizmi?`
      : `"${s.fullName}" superadminlikdan olinadi va oddiy xodimga qaytadi (faqat belgilangan bo'limlar). Davom etamizmi?`
    if (!confirm(msg)) return
    setRoleBusyId(s.id)
    try {
      const u = await setStaffRole(s.id, makeSuper ? 'superadmin' : 'staff')
      setStaff((p) => p.map((x) => (x.id === u.id ? u : x)))
      setDraft((d) => ({ ...d, [u.id]: new Set(u.permissions) }))
    } catch (e) {
      alert(apiErrorMessage(e, "Rolni o'zgartirib bo'lmadi"))
    } finally {
      setRoleBusyId(null)
    }
  }

  const savePerms = (s: Staff) => {
    const perms = [...(draft[s.id] ?? new Set())]
    setSavingPermsId(s.id)
    setStaffPermissions(s.id, perms)
      .then((u) => setStaff((p) => p.map((x) => (x.id === u.id ? u : x))))
      .catch((e) => alert(e?.response?.data?.message ?? "Ruxsatlarni saqlab bo'lmadi"))
      .finally(() => setSavingPermsId(null))
  }

  return (
    <div>
      <PageHeader
        title="Xodimlar va rollar"
        sub={
          <>
            O'qituvchi bo'lmagan ishchilar (kassir, administrator, ...)
            {canManageRoles
              ? " — har biriga kerakli bo'limlarni (rollarni) shu yerda belgilang. Kerak bo'lsa akkauntni TO'LIQ superadmin qilib ham qo'yish mumkin."
              : '.'}
          </>
        }
        actions={
          <Button onClick={openCreate}>
            <Plus className="h-4 w-4" /> Yangi xodim
          </Button>
        }
      />

      {loading ? (
        <Card>
          <Loader label="Yuklanmoqda..." />
        </Card>
      ) : staff.length === 0 ? (
        <Card>
          <div className="state">
            <div className="state-icon">
              <Users className="h-6 w-6" />
            </div>
            <h4>Hali xodim qo'shilmagan</h4>
            <p>"Yangi xodim" tugmasi orqali qo'shing.</p>
          </div>
        </Card>
      ) : (
        <div className="space-y-4">
          {staff.map((s) => {
            const cur = draft[s.id] ?? new Set<string>()
            // Superadmin/admin akkauntida bo'lim ruxsatlari o'rinsiz (baribir tekshirilmaydi),
            // ism/parol/o'chirish esa ATAYIN yopiq — huquq oshirishning oldini oladi (backend ham shunday).
            const isSuper = s.role === 'superadmin'
            const isStaffRow = (s.role ?? 'staff') === 'staff'
            const isMe = user?.id === s.id
            return (
              <Card key={s.id}>
                <div className="mb-3 flex flex-wrap items-start justify-between gap-2">
                  <div className="cell-user">
                    <div className="avatar h-10 w-10 text-sm" style={{ background: avatarColor(s.fullName) }}>
                      {initialsOf(s.fullName)}
                    </div>
                    <div className="meta">
                      <strong className="flex items-center gap-2 text-slate-800">
                        {s.fullName}
                        {!isStaffRow && (
                          <Badge tone={isSuper ? 'amber' : 'violet'}>{roleLabel(s.role)}</Badge>
                        )}
                      </strong>
                      <span className="text-slate-400">
                        {s.position || roleLabel(s.role)} · <code className="font-mono">{s.login}</code>
                      </span>
                    </div>
                  </div>
                  <div className="flex items-center gap-1.5">
                    {canManageRoles && !isMe && (
                      <Button
                        variant="secondary"
                        onClick={() => toggleSuperAdmin(s)}
                        disabled={roleBusyId === s.id}
                      >
                        {isSuper ? <ShieldOff className="h-4 w-4" /> : <ShieldCheck className="h-4 w-4" />}
                        {roleBusyId === s.id
                          ? 'Saqlanmoqda...'
                          : isSuper
                            ? 'Superadminlikni olib tashlash'
                            : 'Superadmin qilish'}
                      </Button>
                    )}
                    {isStaffRow && (
                      <div className="flex items-center gap-0.5">
                        <IconBtn icon={Eye} title="Login/parol" onClick={() => showCredentials(s)} />
                        <IconBtn icon={Pencil} title="Tahrirlash" onClick={() => openEdit(s)} />
                        <IconBtn icon={Trash2} title="O'chirish" danger onClick={() => handleDelete(s)} />
                      </div>
                    )}
                  </div>
                </div>

                {!isStaffRow ? (
                  <p className="rounded-lg bg-slate-50 px-3 py-2 text-xs text-slate-500">
                    {isSuper
                      ? "To'liq huquqli akkaunt — barcha bo'limlar ochiq, bo'lim ruxsatlari tekshirilmaydi."
                      : "Administrator akkaunti — bo'limlar ochiq (superadminning ayrim imtiyozlaridan tashqari)."}
                    {isMe && ' Bu — sizning akkauntingiz.'}
                  </p>
                ) : canManageRoles ? (
                  <>
                    <p className="mb-2 text-xs text-slate-400">
                      Har bo'lim uchun ruxsat: <b>Ko'rish</b> (ochadi), <b>Qo'shish</b>, <b>Tahrir</b>,
                      <b> O'chirish</b>. Ko'rishsiz bo'lim yashiriladi; yozish uchun ko'rish avtomatik yoqiladi.
                    </p>
                    <PermMatrix
                      perms={cur}
                      onToggleSection={(section, action) => toggleSection(s.id, section, action)}
                      onTogglePage={(section, page, action) => togglePage(s.id, section, page, action)}
                    />
                    <div className="mt-3 flex justify-end">
                      <Button
                        onClick={() => savePerms(s)}
                        disabled={!dirty(s) || savingPermsId === s.id}
                      >
                        {savingPermsId === s.id ? 'Saqlanmoqda...' : 'Ruxsatlarni saqlash'}
                      </Button>
                    </div>
                  </>
                ) : (
                  <div className="flex flex-wrap gap-1.5">
                    {s.permissions.length === 0 ? (
                      <span className="text-xs text-slate-400">Ruxsatlar belgilanmagan</span>
                    ) : (
                      // Bo'lim ochiq bo'lsa BITTA chip (sahifalari uning ichida), aks holda
                      // ochiq sahifalar alohida chip bo'lib chiqadi — "nima berilgan" aniq ko'rinsin.
                      adminPermissions.flatMap((p) => {
                        const own = new Set(s.permissions)
                        const secActs = sectionRowActions(own, p.key, permPagesOf(p.key))
                        if (secActs.size > 0)
                          return [
                            <Badge key={p.key} tone="violet">
                              {p.label}
                              {secActs.size !== 4 && (
                                <span className="ml-1 opacity-70">({secActs.size} amal)</span>
                              )}
                            </Badge>,
                          ]
                        return (p.pages ?? [])
                          .filter((pg) => sectionActions(own, pg.key).size > 0)
                          .map((pg) => {
                            const acts = sectionActions(own, pg.key)
                            return (
                              <Badge key={pg.key} tone="violet">
                                {p.label} → {pg.label}
                                {acts.size !== 4 && (
                                  <span className="ml-1 opacity-70">({acts.size} amal)</span>
                                )}
                              </Badge>
                            )
                          })
                      })
                    )}
                  </div>
                )}
              </Card>
            )
          })}
        </div>
      )}

      {/* Yaratish / tahrirlash */}
      <Modal
        open={formOpen}
        onClose={() => setFormOpen(false)}
        title={editing ? 'Xodimni tahrirlash' : 'Yangi xodim'}
        footer={
          <>
            <Button variant="secondary" onClick={() => setFormOpen(false)}>
              Bekor qilish
            </Button>
            <Button type="submit" form="staff-form" disabled={saving}>
              {saving ? 'Saqlanmoqda...' : 'Saqlash'}
            </Button>
          </>
        }
      >
        <form id="staff-form" onSubmit={handleSubmit} className="space-y-4">
          <Input
            label="F.I.SH"
            required
            value={form.fullName}
            onChange={(e) => setForm((f) => ({ ...f, fullName: e.target.value }))}
          />
          <div>
            <label className="mb-1 block text-sm font-medium text-slate-600">Lavozim</label>
            <input
              list="staff-positions"
              value={form.position}
              onChange={(e) => setForm((f) => ({ ...f, position: e.target.value }))}
              placeholder="Masalan: Kassir"
              className="w-full rounded-lg border border-slate-200 px-3 py-2 text-sm text-slate-800 outline-none focus:border-brand-400 focus:ring-2 focus:ring-brand-100"
            />
            <datalist id="staff-positions">
              {POSITIONS.map((p) => (
                <option key={p} value={p} />
              ))}
            </datalist>
          </div>
          <PhoneInput
            label="Telefon"
            value={form.phone ?? ''}
            onChange={(phone) => setForm((f) => ({ ...f, phone }))}
          />
          {editing && (
            <div>
              <label className="mb-1 block text-sm font-medium text-slate-600">Parolni almashtirish</label>
              <div className="flex items-start gap-2">
                <input
                  type="text"
                  autoComplete="new-password"
                  placeholder="Bo'sh qoldirilsa — parol o'zgarmaydi"
                  value={form.newPassword ?? ''}
                  onChange={(e) => setForm((f) => ({ ...f, newPassword: e.target.value }))}
                  className="w-full rounded-lg border border-slate-200 px-3 py-2 text-sm text-slate-800 outline-none focus:border-brand-400 focus:ring-2 focus:ring-brand-100"
                />
                <Button
                  type="button"
                  variant="secondary"
                  onClick={() => setForm((f) => ({ ...f, newPassword: randomPassword() }))}
                >
                  Generatsiya
                </Button>
              </div>
            </div>
          )}
          {!editing && canManageRoles && (
            <div className="space-y-4">
              {/* Rol shabloni — tanlansa matritsani oldindan to'ldiradi (keyin qo'lda o'zgartirish mumkin). */}
              {templates.length > 0 && (
                <div>
                  <label className="mb-1 block text-sm font-medium text-slate-600">
                    Rol shabloni (ixtiyoriy)
                  </label>
                  <div className="space-y-2">
                    <select
                      value={selectedTemplate ?? ''}
                      onChange={(e) => applyTemplate(e.target.value || null)}
                      className="w-full rounded-lg border border-slate-200 px-3 py-2 text-sm text-slate-800 outline-none focus:border-brand-400 focus:ring-2 focus:ring-brand-100"
                    >
                      <option value="">— Tanlang yoki qo'lda belgilang —</option>
                      {templates.map((t) => (
                        <option key={t.id} value={t.code}>
                          {t.name}
                        </option>
                      ))}
                    </select>
                    {selectedTemplate && (
                      <p className="text-xs text-slate-500">
                        {templates.find((t) => t.code === selectedTemplate)?.description}
                      </p>
                    )}
                  </div>
                </div>
              )}

              {/* Ruxsatlar matritsasi — har bo'lim uchun amallar */}
              <div>
                <label className="mb-1 block text-sm font-medium text-slate-600">
                  Ruxsatlar (har bo'lim uchun amallar)
                </label>
                <PermMatrix
                  perms={createPerms}
                  onToggleSection={(section, action) =>
                    setCreatePerms((s) => toggleSectionRow(s, section, action, permPagesOf(section)))
                  }
                  onTogglePage={(section, page, action) =>
                    setCreatePerms((s) =>
                      togglePageRow(s, section, page, action, permPagesOf(section)),
                    )
                  }
                />
              </div>
            </div>
          )}
          {!editing && (
            <p className="text-xs text-slate-400">
              Saqlangach tizimga kirish uchun login va parol avtomatik yaratiladi va ko'rsatiladi.
              {canManageRoles
                ? " Ruxsatlarni keyinroq ham har bir xodim kartasidan o'zgartirishingiz mumkin."
                : " Ruxsatlarni (bo'limlarni) tizim egasi belgilaydi."}
            </p>
          )}
        </form>
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
            credOf
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
  icon: Icon,
  title,
  onClick,
  danger,
}: {
  icon: typeof Eye
  title: string
  onClick: () => void
  danger?: boolean
}) {
  return (
    <button
      type="button"
      title={title}
      onClick={onClick}
      className={cn(
        'rounded-lg p-1.5 transition-colors',
        danger
          ? 'text-slate-400 hover:bg-red-50 hover:text-red-600'
          : 'text-slate-400 hover:bg-slate-100 hover:text-slate-700',
      )}
    >
      <Icon className="h-4 w-4" />
    </button>
  )
}
