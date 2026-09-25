import { useEffect, useRef, useState } from 'react'
import { Plus, Pencil, Trash2, ShieldCheck, GraduationCap, KeyRound } from 'lucide-react'
import type { StaffRoleTemplate } from '@/types'
import { getStaffRoleTemplates, createStaffRole, updateStaffRole, deleteStaffRole } from '@/api/services/staff'
import { permPagesOf } from '@/config/constants'
import { useAuth } from '@/context/auth-context'
import { toggleSectionRow, togglePageRow } from '@/lib/permissions'
import { canManageStaffRoles } from '@/lib/staffRoles'
import { toast } from '@/lib/toast'
import { apiErrorMessage, cn } from '@/lib/utils'
import { PermMatrix } from '@/components/staff/PermMatrix'
import { PermChips } from '@/components/staff/PermChips'
import { Card } from '@/components/ui/Card'
import { Button } from '@/components/ui/Button'
import { Badge } from '@/components/ui/Badge'
import { PageHeader } from '@/components/ui/PageHeader'
import { Loader } from '@/components/ui/Loader'
import { Modal } from '@/components/ui/Modal'
import { Input, Textarea } from '@/components/ui/Input'

interface Draft {
  id: string | null
  name: string
  description: string
  perms: Set<string>
}

/**
 * BOSHQARUV → ROLLAR. Faqat ROLLAR ro'yxati: har rolning nomi, tavsifi, ruxsatlari va nechta xodimi.
 * Xodimlarning o'zi "Xodimlar" sahifasida (rol tanlab qo'shiladi).
 *
 * ⚠️ Rolning ruxsatlari o'zgarsa — shu roldagi BARCHA xodimga darhol tarqaladi (jonli bog'lanish,
 * `StaffRoles.SyncMembersAsync`). Oynada shu ochiq aytiladi: "bitta rolni tahrirladim" aslida
 * bir necha odamning huquqini o'zgartiradi.
 */
export function RolesPage() {
  const { user } = useAuth()
  const canEdit = canManageStaffRoles(user?.role, user?.permissions)

  const [roles, setRoles] = useState<StaffRoleTemplate[]>([])
  const [loading, setLoading] = useState(true)
  const [loadError, setLoadError] = useState<string | null>(null)
  const [draft, setDraft] = useState<Draft | null>(null)
  const [saving, setSaving] = useState(false)
  const [formError, setFormError] = useState<string | null>(null)
  /** SINXRON qulf — "Saqlash" tez bosilsa ham rol BITTA yaratiladi. */
  const inFlightRef = useRef(false)
  const [tick, setTick] = useState(0)

  useEffect(() => {
    let alive = true
    getStaffRoleTemplates()
      .then((list) => {
        if (!alive) return
        setRoles(list)
        setLoadError(null)
      })
      .catch((e) => alive && setLoadError(apiErrorMessage(e, "Rollarni yuklab bo'lmadi")))
      .finally(() => alive && setLoading(false))
    return () => {
      alive = false
    }
  }, [tick])

  const openCreate = () => {
    setFormError(null)
    setDraft({ id: null, name: '', description: '', perms: new Set() })
  }
  const openEdit = (r: StaffRoleTemplate) => {
    setFormError(null)
    setDraft({ id: r.id, name: r.name, description: r.description, perms: new Set(r.defaultPermissions) })
  }

  const save = async (e: React.FormEvent) => {
    e.preventDefault()
    if (!draft || inFlightRef.current) return
    if (!draft.name.trim()) {
      setFormError('Rol nomini kiriting')
      return
    }
    inFlightRef.current = true
    setSaving(true)
    setFormError(null)
    try {
      const payload = { name: draft.name.trim(), description: draft.description.trim(), permissions: [...draft.perms] }
      if (draft.id) {
        const r = await updateStaffRole(draft.id, payload)
        toast.success('Rol saqlandi', r.memberCount ? `${r.memberCount} ta xodimga tarqaldi` : undefined)
      } else {
        await createStaffRole(payload)
        toast.success("Rol qo'shildi", payload.name)
      }
      setDraft(null)
      setTick((t) => t + 1)
    } catch (err) {
      setFormError(apiErrorMessage(err, "Rolni saqlab bo'lmadi"))
    } finally {
      inFlightRef.current = false
      setSaving(false)
    }
  }

  const remove = async (r: StaffRoleTemplate) => {
    if (!confirm(`«${r.name}» rolini o'chirasizmi?`)) return
    try {
      await deleteStaffRole(r.id)
      toast.success("Rol o'chirildi", r.name)
      setTick((t) => t + 1)
    } catch (err) {
      alert(apiErrorMessage(err, "Rolni o'chirib bo'lmadi"))
    }
  }

  const editing = draft?.id ? roles.find((r) => r.id === draft.id) : undefined

  return (
    <div>
      <PageHeader
        title="Rollar"
        sub="Har rolga qaysi bo'limlar ochiqligi shu yerda belgilanadi. Xodim «Xodimlar» sahifasida rol tanlab qo'shiladi va ruxsatlarini roldan oladi."
        actions={
          canEdit && (
            <Button onClick={openCreate}>
              <Plus className="h-4 w-4" /> Rol qo'shish
            </Button>
          )
        }
      />

      {loadError && (
        <div className="mb-4 rounded-lg border border-red-200 bg-red-50 px-3 py-2 text-sm text-red-700">{loadError}</div>
      )}

      {loading ? (
        <Card>
          <Loader label="Yuklanmoqda..." />
        </Card>
      ) : (
        <div className="space-y-3">
          {/* TIZIM rollari — tahrirlanmaydi, lekin "qaysi rollar bor" savoliga to'liq javob bo'lsin. */}
          <SystemRow
            icon={ShieldCheck}
            name="Superadmin / Admin"
            text="To'liq huquqli akkauntlar — barcha bo'limlar ochiq, bo'lim ruxsatlari tekshirilmaydi. Xodimlar sahifasida superadmin tayinlaydi."
          />
          <SystemRow
            icon={GraduationCap}
            name="O'qituvchi"
            text="O'qituvchi portaliga kiradi (jurnal, jadval, xabarlar, maosh). Qo'shishda oylik maosh foizi belgilanadi; portal ruxsatlari o'qituvchi kartasida."
          />

          {roles.length === 0 && !loadError ? (
            <Card>
              <div className="state">
                <div className="state-icon">
                  <KeyRound className="h-6 w-6" />
                </div>
                <h4>Hali rol yo'q</h4>
                <p>«Rol qo'shish» tugmasi orqali qo'shing (masalan Kassir, Administrator).</p>
              </div>
            </Card>
          ) : (
            roles.map((r) => (
              <Card key={r.id}>
                <div className="flex flex-wrap items-start justify-between gap-2">
                  <div className="min-w-0">
                    <p className="flex items-center gap-2 font-semibold text-slate-800">
                      {r.name}
                      <Badge tone={r.memberCount ? 'blue' : 'default'}>{r.memberCount ?? 0} ta xodim</Badge>
                    </p>
                    {r.description && <p className="mt-0.5 text-sm text-slate-500">{r.description}</p>}
                  </div>
                  {canEdit && (
                    <div className="flex items-center gap-0.5">
                      <IconBtn title="Tahrirlash" onClick={() => openEdit(r)}>
                        <Pencil className="h-4 w-4" />
                      </IconBtn>
                      <IconBtn title="O'chirish" danger onClick={() => void remove(r)}>
                        <Trash2 className="h-4 w-4" />
                      </IconBtn>
                    </div>
                  )}
                </div>
                <div className="mt-3">
                  <PermChips permissions={r.defaultPermissions} />
                </div>
              </Card>
            ))
          )}
        </div>
      )}

      <Modal
        open={!!draft}
        onClose={() => setDraft(null)}
        size="lg"
        title={draft?.id ? 'Rolni tahrirlash' : "Yangi rol"}
        footer={
          <>
            <Button variant="secondary" onClick={() => setDraft(null)}>
              Bekor qilish
            </Button>
            <Button type="submit" form="role-form" disabled={saving}>
              {saving ? 'Saqlanmoqda...' : 'Saqlash'}
            </Button>
          </>
        }
      >
        {draft && (
          <form id="role-form" onSubmit={(e) => void save(e)} className="space-y-4">
            {formError && (
              <div className="rounded-lg border border-red-200 bg-red-50 px-3 py-2 text-sm text-red-700">{formError}</div>
            )}
            <Input
              label="Rol nomi"
              required
              value={draft.name}
              onChange={(e) => setDraft((d) => d && { ...d, name: e.target.value })}
              placeholder="Masalan: Kassir"
            />
            <Textarea
              label="Tavsif"
              rows={2}
              value={draft.description}
              onChange={(e) => setDraft((d) => d && { ...d, description: e.target.value })}
              placeholder="Bu rol nima ish qiladi (ixtiyoriy)"
            />
            <div>
              <span className="mb-1 block text-sm font-medium text-slate-600">Ruxsatlar</span>
              <p className="mb-2 text-xs text-slate-400">
                Har bo'lim uchun: <b>Ko'rish</b>, <b>Qo'shish</b>, <b>Tahrir</b>, <b>O'chirish</b>. Ko'rishsiz bo'lim
                yashiriladi; yozish uchun ko'rish avtomatik yoqiladi.
              </p>
              <PermMatrix
                perms={draft.perms}
                onToggleSection={(section, action) =>
                  setDraft((d) => d && { ...d, perms: toggleSectionRow(d.perms, section, action, permPagesOf(section)) })
                }
                onTogglePage={(section, page, action) =>
                  setDraft(
                    (d) => d && { ...d, perms: togglePageRow(d.perms, section, page, action, permPagesOf(section)) },
                  )
                }
              />
            </div>
            {!!editing?.memberCount && (
              <p className="rounded-lg bg-amber-50 px-3 py-2 text-xs text-amber-700">
                ⚠️ Bu rolda {editing.memberCount} ta xodim bor — ruxsatlar saqlanishi bilan ularning HAMMASIGA
                tarqaladi.
              </p>
            )}
          </form>
        )}
      </Modal>
    </div>
  )
}

function SystemRow({ icon: Icon, name, text }: { icon: typeof ShieldCheck; name: string; text: string }) {
  return (
    <Card>
      <div className="flex items-start gap-3">
        <div className="flex h-9 w-9 shrink-0 items-center justify-center rounded-lg bg-slate-100 text-slate-500">
          <Icon className="h-5 w-5" />
        </div>
        <div className="min-w-0">
          <p className="flex items-center gap-2 font-semibold text-slate-800">
            {name}
            <Badge>Tizim roli</Badge>
          </p>
          <p className="mt-0.5 text-sm text-slate-500">{text}</p>
        </div>
      </div>
    </Card>
  )
}

function IconBtn({
  title,
  onClick,
  danger,
  children,
}: {
  title: string
  onClick: () => void
  danger?: boolean
  children: React.ReactNode
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
      {children}
    </button>
  )
}
