import { useEffect, useMemo, useState } from 'react'
import { ClipboardCheck, Plus, Pencil, Trash2 } from 'lucide-react'
import type { Group, Teacher } from '@/types'
import {
  getCriteria,
  createCriterion,
  updateCriterion,
  deleteCriterion,
  getGroupCriteria,
  setGroupCriteria,
  type GradingCriterion,
} from '@/api/services/grading'
import { getClasses } from '@/api/services/classes'
import { getTeachers } from '@/api/services/teachers'
import { Card } from '@/components/ui/Card'
import { Button } from '@/components/ui/Button'
import { Loader } from '@/components/ui/Loader'
import { Modal } from '@/components/ui/Modal'
import { Input, Select, Textarea } from '@/components/ui/Input'
import { PageHeader } from '@/components/ui/PageHeader'
import { cn, apiErrorMessage } from '@/lib/utils'
import { usePerm } from '@/lib/permissions'

/** Ism-sharifdan bosh harflar (avatar uchun). */
function initials(name: string): string {
  const parts = name.trim().split(/\s+/).filter(Boolean)
  if (parts.length === 0) return '?'
  if (parts.length === 1) return parts[0].slice(0, 2).toUpperCase()
  return (parts[0][0] + parts[1][0]).toUpperCase()
}

export function GradingCriteriaPage() {
  const { can } = usePerm()
  const [criteria, setCriteria] = useState<GradingCriterion[]>([])
  const [groups, setGroups] = useState<Group[]>([])
  const [teachers, setTeachers] = useState<Teacher[]>([])
  const [loading, setLoading] = useState(true)

  // Mezon yaratish/tahrirlash modali
  const [formOpen, setFormOpen] = useState(false)
  const [editing, setEditing] = useState<GradingCriterion | null>(null)
  const [fName, setFName] = useState('')
  const [fDesc, setFDesc] = useState('')
  const [fTeacherId, setFTeacherId] = useState('')
  const [saving, setSaving] = useState(false)

  // Guruhga biriktirish — avval o'qituvchi, keyin uning guruhi tanlanadi (kaskad)
  const [teacherId, setTeacherId] = useState('')
  const [groupId, setGroupId] = useState('')
  const [assigned, setAssigned] = useState<string[]>([])
  const [assignBusy, setAssignBusy] = useState(false)
  const [assignSaved, setAssignSaved] = useState(false)

  useEffect(() => {
    Promise.all([getCriteria(), getClasses(), getTeachers()])
      .then(([c, g, t]) => {
        setCriteria(c)
        setGroups(g)
        setTeachers(t)
        // Birinchi guruhi bor o'qituvchini va uning birinchi guruhini tanlab qo'yamiz.
        const firstTeacher = t.find((tt) => g.some((gg) => gg.teacherId === tt.id))
        if (firstTeacher) {
          setTeacherId(firstTeacher.id)
          const fg = g.find((gg) => gg.teacherId === firstTeacher.id)
          if (fg) setGroupId(fg.id)
        }
      })
      .finally(() => setLoading(false))
  }, [])

  // Faqat guruhi bor o'qituvchilar ro'yxatda ko'rinadi.
  const teacherOptions = useMemo(
    () =>
      teachers
        .filter((t) => groups.some((g) => g.teacherId === t.id))
        .sort((a, b) => a.fullName.localeCompare(b.fullName)),
    [teachers, groups],
  )
  const groupsForTeacher = useMemo(
    () => groups.filter((g) => g.teacherId === teacherId),
    [groups, teacherId],
  )

  // Modal uchun — barcha o'qituvchilar (mezon egasi ixtiyoriy tanlanadi), alifbo tartibida.
  const allTeacherOptions = useMemo(
    () => [...teachers].sort((a, b) => a.fullName.localeCompare(b.fullName)),
    [teachers],
  )

  // "Mezonlar" ro'yxati o'qituvchi bo'yicha guruhlanadi (chiroyli ko'rinish): har o'qituvchi
  // ostida o'ziga tegishli mezonlar; egasiz mezonlar — "Umumiy".
  const groupedCriteria = useMemo(() => {
    const byTeacher = new Map<string, GradingCriterion[]>()
    for (const c of criteria) {
      const key = c.teacherId || ''
      if (!byTeacher.has(key)) byTeacher.set(key, [])
      byTeacher.get(key)!.push(c)
    }
    const nameOf = (id: string) => teachers.find((t) => t.id === id)?.fullName || "Noma'lum o'qituvchi"
    const out: { key: string; label: string; items: GradingCriterion[] }[] = []
    ;[...byTeacher.keys()]
      .filter((k) => k !== '')
      .sort((a, b) => nameOf(a).localeCompare(nameOf(b)))
      .forEach((k) => out.push({ key: k, label: nameOf(k), items: byTeacher.get(k)! }))
    if (byTeacher.has('')) out.push({ key: '', label: 'Umumiy', items: byTeacher.get('')! })
    return out
  }, [criteria, teachers])

  // Biriktirish ro'yxatida — tanlangan guruh o'qituvchisining mezonlari + umumiy (egasiz) mezonlar.
  // Allaqachon biriktirilgan mezon boshqa o'qituvchiniki bo'lsa ham ko'rinadi (belgini olib tashlash mumkin).
  const assignableCriteria = useMemo(
    () => criteria.filter((c) => !c.teacherId || c.teacherId === teacherId || assigned.includes(c.id)),
    [criteria, teacherId, assigned],
  )

  // Tanlangan guruh o'zgarganda — biriktirilgan mezonlarni yuklash
  useEffect(() => {
    if (!groupId) {
      setAssigned([])
      return
    }
    let active = true
    getGroupCriteria(groupId).then((ids) => {
      if (active) setAssigned(ids)
    })
    return () => {
      active = false
    }
  }, [groupId])

  const openCreate = () => {
    setEditing(null)
    setFName('')
    setFDesc('')
    // Biriktirish tomonida tanlangan o'qituvchini standart qilib qo'yamiz (qulaylik uchun).
    setFTeacherId(teacherId || '')
    setFormOpen(true)
  }

  const openEdit = (c: GradingCriterion) => {
    setEditing(c)
    setFName(c.name)
    setFDesc(c.description)
    setFTeacherId(c.teacherId || '')
    setFormOpen(true)
  }

  const handleSave = async () => {
    if (!fName.trim() || !fTeacherId || saving) return
    setSaving(true)
    try {
      if (editing) {
        await updateCriterion(editing.id, fName.trim(), fDesc.trim(), fTeacherId)
      } else {
        await createCriterion(fName.trim(), fDesc.trim(), fTeacherId)
      }
      const fresh = await getCriteria()
      setCriteria(fresh)
      setFormOpen(false)
      setEditing(null)
    } catch (err) {
      alert(apiErrorMessage(err, "Saqlab bo'lmadi"))
    } finally {
      setSaving(false)
    }
  }

  const handleDelete = async (c: GradingCriterion) => {
    if (!confirm("Mezonni o'chirasizmi? Bog'langan baholar ham o'chadi.")) return
    try {
      await deleteCriterion(c.id)
      setCriteria((prev) => prev.filter((x) => x.id !== c.id))
      setAssigned((prev) => prev.filter((id) => id !== c.id))
    } catch (err) {
      alert(apiErrorMessage(err, "O'chirib bo'lmadi"))
    }
  }

  const toggleAssign = async (criterionId: string, checked: boolean) => {
    if (!groupId) return
    const next = checked
      ? [...assigned, criterionId]
      : assigned.filter((id) => id !== criterionId)
    setAssigned(next)
    setAssignBusy(true)
    setAssignSaved(false)
    try {
      await setGroupCriteria(groupId, next)
      setAssignSaved(true)
      setTimeout(() => setAssignSaved(false), 1600)
    } catch (err) {
      // Xato bo'lsa — eski holatga qaytaramiz
      alert(apiErrorMessage(err, "Saqlab bo'lmadi"))
      try {
        const ids = await getGroupCriteria(groupId)
        setAssigned(ids)
      } catch {
        /* jim */
      }
    } finally {
      setAssignBusy(false)
    }
  }

  if (loading) return <Loader label="Yuklanmoqda..." />

  return (
    <div>
      <PageHeader
        title="Baholash mezonlari"
        sub="Mezon yarating va guruhlarga biriktiring — har guruhga boshqa-boshqa mezonlar"
      />

      <div className="grid gap-4 lg:grid-cols-2">
        {/* 1. Mezonlar puli */}
        <Card
          title="Mezonlar"
          actions={
            can('schedule.grading', 'create') ? (
              <Button onClick={openCreate}>
                <Plus className="h-4 w-4" /> Mezon
              </Button>
            ) : undefined
          }
        >
          {criteria.length === 0 ? (
            <div className="flex flex-col items-center gap-3 py-10 text-center">
              <div className="flex h-12 w-12 items-center justify-center rounded-2xl bg-brand-50 text-brand-600">
                <ClipboardCheck className="h-6 w-6" />
              </div>
              <p className="text-sm text-slate-500">
                Hali mezon yo'q. "Mezon" tugmasi orqali birinchisini qo'shing.
              </p>
            </div>
          ) : (
            <div className="space-y-5">
              {groupedCriteria.map((grp) => (
                <div key={grp.key || '__umumiy'}>
                  {/* O'qituvchi sarlavhasi (avatar + ism + mezonlar soni) */}
                  <div className="mb-2 flex items-center gap-2">
                    <div
                      className={cn(
                        'flex h-7 w-7 shrink-0 items-center justify-center rounded-full text-[11px] font-bold',
                        grp.key
                          ? 'bg-brand-100 text-brand-700'
                          : 'bg-slate-100 text-slate-500',
                      )}
                    >
                      {grp.key ? initials(grp.label) : '•'}
                    </div>
                    <p className="min-w-0 flex-1 truncate text-sm font-semibold text-slate-700">
                      {grp.label}
                    </p>
                    <span className="shrink-0 rounded-full bg-slate-100 px-2 py-0.5 text-xs font-medium text-slate-500">
                      {grp.items.length}
                    </span>
                  </div>
                  <div className="divide-y divide-slate-100 rounded-xl border border-slate-100">
                    {grp.items.map((c) => (
                      <div key={c.id} className="flex items-start gap-3 px-3 py-2.5">
                        <div className="min-w-0 flex-1">
                          <p className="truncate font-semibold text-slate-800">{c.name}</p>
                          {c.description && (
                            <p className="mt-0.5 text-xs text-slate-400">{c.description}</p>
                          )}
                        </div>
                        <div className="flex shrink-0 items-center gap-1">
                          {can('schedule.grading', 'edit') && (
                            <button
                              type="button"
                              title="Tahrirlash"
                              onClick={() => openEdit(c)}
                              className="rounded-lg p-1.5 text-slate-400 transition-colors hover:bg-slate-100 hover:text-slate-700"
                            >
                              <Pencil className="h-4 w-4" />
                            </button>
                          )}
                          {can('schedule.grading', 'delete') && (
                            <button
                              type="button"
                              title="O'chirish"
                              onClick={() => handleDelete(c)}
                              className="rounded-lg p-1.5 text-slate-400 transition-colors hover:bg-red-50 hover:text-red-600"
                            >
                              <Trash2 className="h-4 w-4" />
                            </button>
                          )}
                        </div>
                      </div>
                    ))}
                  </div>
                </div>
              ))}
            </div>
          )}
        </Card>

        {/* 2. Guruhga biriktirish */}
        <Card
          title="Guruhga biriktirish"
          sub={assignBusy ? 'Saqlanmoqda...' : assignSaved ? 'Saqlandi' : undefined}
        >
          {teacherOptions.length === 0 ? (
            <p className="py-6 text-center text-sm text-slate-400">
              Guruhi bor o'qituvchi yo'q.
            </p>
          ) : (
            <div className="space-y-4">
              <Select
                label="O'qituvchi"
                value={teacherId}
                onChange={(e) => {
                  // O'qituvchi o'zgarsa — uning birinchi guruhiga o'tamiz.
                  const tid = e.target.value
                  setTeacherId(tid)
                  const fg = groups.find((g) => g.teacherId === tid)
                  setGroupId(fg ? fg.id : '')
                }}
              >
                {teacherOptions.map((t) => (
                  <option key={t.id} value={t.id}>
                    {t.fullName}
                  </option>
                ))}
              </Select>
              <Select
                label="Guruh"
                value={groupId}
                disabled={!teacherId}
                onChange={(e) => setGroupId(e.target.value)}
              >
                {groupsForTeacher.length === 0 && (
                  <option value="">— guruh yo'q —</option>
                )}
                {groupsForTeacher.map((g) => (
                  <option key={g.id} value={g.id}>
                    {g.name}
                  </option>
                ))}
              </Select>

              {criteria.length === 0 ? (
                <p className="rounded-lg bg-slate-50 px-3 py-3 text-center text-sm text-slate-400">
                  Avval mezon yarating
                </p>
              ) : assignableCriteria.length === 0 ? (
                <p className="rounded-lg bg-slate-50 px-3 py-3 text-center text-sm text-slate-400">
                  Bu o'qituvchi uchun mezon yo'q — yuqoridagi "Mezon" tugmasi orqali qo'shing.
                </p>
              ) : (
                <div className="divide-y divide-slate-100">
                  {assignableCriteria.map((c) => {
                    const checked = assigned.includes(c.id)
                    return (
                      <label
                        key={c.id}
                        className={cn(
                          'flex cursor-pointer items-center gap-3 py-2.5 first:pt-0 last:pb-0',
                          assignBusy && 'pointer-events-none opacity-60',
                        )}
                      >
                        <input
                          type="checkbox"
                          checked={checked}
                          disabled={assignBusy}
                          onChange={(e) => toggleAssign(c.id, e.target.checked)}
                          className="h-4 w-4 shrink-0 rounded border-slate-300 text-brand-600 focus:ring-brand-200"
                        />
                        <span className="min-w-0 flex-1">
                          <span className="block truncate text-sm font-medium text-slate-700">
                            {c.name}
                          </span>
                          {c.description && (
                            <span className="block truncate text-xs text-slate-400">
                              {c.description}
                            </span>
                          )}
                        </span>
                      </label>
                    )
                  })}
                </div>
              )}
            </div>
          )}
        </Card>
      </div>

      {/* Mezon yaratish/tahrirlash */}
      <Modal
        open={formOpen}
        onClose={() => {
          setFormOpen(false)
          setEditing(null)
        }}
        title={editing ? 'Mezonni tahrirlash' : 'Yangi mezon'}
        size="sm"
        footer={
          <>
            <Button
              variant="secondary"
              onClick={() => {
                setFormOpen(false)
                setEditing(null)
              }}
            >
              Bekor
            </Button>
            <Button onClick={handleSave} disabled={saving || !fName.trim() || !fTeacherId}>
              {editing ? 'Saqlash' : 'Qo\'shish'}
            </Button>
          </>
        }
      >
        <div className="space-y-3">
          <Select
            label="O'qituvchi"
            required
            value={fTeacherId}
            onChange={(e) => setFTeacherId(e.target.value)}
          >
            <option value="">— o'qituvchini tanlang —</option>
            {allTeacherOptions.map((t) => (
              <option key={t.id} value={t.id}>
                {t.fullName}
              </option>
            ))}
          </Select>
          <p className="-mt-1 text-xs text-slate-400">
            Bu mezon faqat shu o'qituvchiga tegishli bo'ladi — uning guruhlariga biriktiriladi.
          </p>
          <Input
            label="Nom"
            required
            placeholder="Masalan: Faollik"
            value={fName}
            onChange={(e) => setFName(e.target.value)}
            autoFocus
          />
          <Textarea
            label="Izoh"
            rows={3}
            placeholder="Ixtiyoriy izoh (masalan: uy vazifasini bajardimi)"
            value={fDesc}
            onChange={(e) => setFDesc(e.target.value)}
          />
        </div>
      </Modal>
    </div>
  )
}
