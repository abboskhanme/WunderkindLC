import { useEffect, useState } from 'react'
import { Link, useNavigate } from 'react-router-dom'
import { usePersistentState } from '@/hooks/usePersistentState'
import {
  Plus,
  Search,
  Eye,
  Pencil,
  Trash2,
  Archive,
  RotateCcw,
  Download,
  Users,
  GraduationCap,
  BookOpen,
  ArrowUpRight,
  X,
  Lock,
  Unlock,
} from 'lucide-react'
import type { Gender, Group, Subject, Teacher } from '@/types'
import type { TeacherPayload } from '@/api/services/teachers'
import {
  getTeachers,
  getArchivedTeachers,
  createTeacher,
  updateTeacher,
  deleteTeacher,
  archiveTeacher,
  restoreTeacher,
  blockTeacher,
  unblockTeacher,
  downloadTeacherCredentials,
} from '@/api/services/teachers'
import { useAuth } from '@/context/auth-context'
import { usePerm } from '@/lib/permissions'
import { getSubjects } from '@/api/services/subjects'
import { getClasses } from '@/api/services/classes'
import { genderLabels } from '@/config/constants'
import { formatDate, cn } from '@/lib/utils'
import { Card } from '@/components/ui/Card'
import { Button } from '@/components/ui/Button'
import { Badge } from '@/components/ui/Badge'
import { PageHeader } from '@/components/ui/PageHeader'
import { CardTabs } from '@/components/ui/CardTabs'
import { teacherTabs } from '@/config/sectionTabs'
import { StatCard } from '@/components/ui/StatCard'
import { Loader } from '@/components/ui/Loader'
import { Modal } from '@/components/ui/Modal'
import { Textarea } from '@/components/ui/Input'
import { TablePagination, usePagination } from '@/components/ui/TablePagination'
import { TeacherFormModal } from './TeacherFormModal'
import { TeacherViewModal } from './TeacherViewModal'
import { ReasonPromptModal } from '@/components/ui/ReasonPromptModal'

/** Ro'yxat holati filtri — avvalgi "Faol | Arxiv" tablari o'rniga (endi "Hammasi" ham bor). */
type StatusFilter = 'active' | 'archived' | 'all'

const STATUS_CHIPS: { key: StatusFilter; label: string }[] = [
  { key: 'active', label: 'Faol' },
  { key: 'archived', label: 'Arxivlangan' },
  { key: 'all', label: 'Hammasi' },
]

const control =
  'rounded-lg border border-slate-200 bg-white px-3 py-2 text-sm text-slate-700 outline-none transition-colors focus:border-brand-400 focus:ring-2 focus:ring-brand-100'

// Avatar uchun ism harflari va barqaror rang (faqat ko'rinish uchun)
const initialsOf = (name: string) =>
  name
    .split(' ')
    .filter(Boolean)
    .slice(0, 2)
    .map((s) => s[0]?.toUpperCase())
    .join('')

const AVATAR_COLORS = [
  '#7c3aed',
  '#0ea5e9',
  '#10b981',
  '#f59e0b',
  '#ef4444',
  '#6366f1',
  '#ec4899',
  '#14b8a6',
]
const avatarColor = (name: string) => {
  let h = 0
  for (let i = 0; i < name.length; i++) h = (h * 31 + name.charCodeAt(i)) >>> 0
  return AVATAR_COLORS[h % AVATAR_COLORS.length]
}

export function TeachersPage() {
  const { user } = useAuth()
  const { can } = usePerm()
  const navigate = useNavigate()
  const [teachers, setTeachers] = useState<Teacher[]>([])
  const [archived, setArchived] = useState<Teacher[]>([])
  const [subjects, setSubjects] = useState<Subject[]>([])
  const [classes, setClasses] = useState<Group[]>([])
  const [loading, setLoading] = useState(true)

  const [status, setStatus] = usePersistentState<StatusFilter>('teachers.status', 'active')
  const [search, setSearch] = usePersistentState('teachers.search', '')
  const [subjectFilter, setSubjectFilter] = usePersistentState('teachers.subjectFilter', 'all')
  const [genderFilter, setGenderFilter] = usePersistentState<'all' | Gender>('teachers.genderFilter', 'all')
  const [formOpen, setFormOpen] = useState(false)
  const [editing, setEditing] = useState<Teacher | null>(null)
  const [viewing, setViewing] = useState<Teacher | null>(null)

  // Arxivga ko'chirish tasdiq oynasi
  const [archiveTarget, setArchiveTarget] = useState<Teacher | null>(null)
  const [reason, setReason] = useState('')
  const [deleting, setDeleting] = useState<Teacher | null>(null)
  // "Vaqtincha aktiv emas" oynasi — o'qituvchi tizimga kira olmaydi (arxivlash EMAS)
  const [blockTarget, setBlockTarget] = useState<Teacher | null>(null)
  const [blockNote, setBlockNote] = useState('')

  useEffect(() => {
    Promise.all([getTeachers(), getArchivedTeachers(), getSubjects(), getClasses()])
      .then(([t, a, s, c]) => {
        setTeachers(t)
        setArchived(a)
        setSubjects(s)
        setClasses(c)
      })
      .finally(() => setLoading(false))
  }, [])

  // O'qituvchi o'tadigan guruhlar (guruhga o'qituvchi guruh formasida biriktiriladi — Group.teacherId).
  const teacherGroups = (tid: string) => classes.filter((c) => c.teacherId === tid && !c.isArchived)

  const source =
    status === 'archived' ? archived : status === 'all' ? [...teachers, ...archived] : teachers

  const filtered = source.filter((t) => {
    const q = search.trim().toLowerCase()
    const matchSearch =
      !q || t.fullName.toLowerCase().includes(q) || (t.phone ?? '').toLowerCase().includes(q)
    const matchSubject = subjectFilter === 'all' || t.subjectIds.includes(subjectFilter)
    const matchGender = genderFilter === 'all' || t.gender === genderFilter
    return matchSearch && matchSubject && matchGender
  })

  const pg = usePagination(filtered)
  const { setPage } = pg
  // Filtr o'zgarganda — birinchi sahifaga (ro'yxat uzunligi tasodifan bir xil qolsa ham).
  useEffect(() => {
    setPage(1)
  }, [search, subjectFilter, genderFilter, status, setPage])

  // Filtrlar standart holatdan farq qiladimi — "Tozalash" tugmasi shunda ko'rinadi.
  const filtersActive =
    search !== '' || genderFilter !== 'all' || subjectFilter !== 'all' || status !== 'active'
  const clearFilters = () => {
    setSearch('')
    setGenderFilter('all')
    setSubjectFilter('all')
    setStatus('active')
  }

  // KPI: faol guruhlarda biriktirilgan o'qituvchilar soni
  const activeGroups = classes.filter((c) => !c.isArchived)
  const assignedTeachers = new Set(activeGroups.map((c) => c.teacherId).filter(Boolean)).size

  const handleSubmit = (values: TeacherPayload) => {
    if (editing) {
      updateTeacher(editing.id, values).then((u) =>
        setTeachers((prev) => prev.map((t) => (t.id === u.id ? u : t))),
      )
    } else {
      createTeacher(values).then((c) => {
        setTeachers((prev) => [c, ...prev])
        setViewing(c)
      })
    }
    setFormOpen(false)
    setEditing(null)
  }

  const confirmArchive = () => {
    if (!archiveTarget) return
    const t = archiveTarget
    const today = new Date().toISOString().slice(0, 10)
    archiveTeacher(t.id, reason.trim()).then(() => {
      setTeachers((prev) => prev.filter((x) => x.id !== t.id))
      setArchived((prev) => [
        { ...t, isArchived: true, archivedAt: today, archiveReason: reason.trim() },
        ...prev,
      ])
    })
    setArchiveTarget(null)
    setReason('')
  }

  const handleRestore = (t: Teacher) => {
    if (!confirm(`"${t.fullName}" o'qituvchini arxivdan qaytarasizmi?`)) return
    restoreTeacher(t.id).then(() => {
      setArchived((prev) => prev.filter((x) => x.id !== t.id))
      setTeachers((prev) =>
        [{ ...t, isArchived: false, archivedAt: null, archiveReason: null }, ...prev].sort((a, b) =>
          a.fullName.localeCompare(b.fullName),
        ),
      )
    })
  }

  /** Vaqtincha faolsizlantirish — login yopiladi, paroli va butun tarixi joyida qoladi. */
  const confirmBlock = () => {
    const t = blockTarget
    if (!t) return
    const note = blockNote.trim()
    const today = new Date().toISOString().slice(0, 10)
    blockTeacher(t.id, note)
      .then(() => {
        setTeachers((prev) =>
          prev.map((x) =>
            x.id === t.id ? { ...x, isBlocked: true, blockedAt: today, blockNote: note } : x,
          ),
        )
        setBlockTarget(null)
        setBlockNote('')
      })
      .catch((e) => alert(e?.response?.data?.message ?? 'Amalni bajarib bo‘lmadi'))
  }

  const handleUnblock = (t: Teacher) => {
    unblockTeacher(t.id)
      .then(() =>
        setTeachers((prev) =>
          prev.map((x) =>
            x.id === t.id ? { ...x, isBlocked: false, blockedAt: null, blockNote: '' } : x,
          ),
        ),
      )
      .catch((e) => alert(e?.response?.data?.message ?? 'Amalni bajarib bo‘lmadi'))
  }

  const handleDelete = (t: Teacher) => setDeleting(t)

  const doDelete = (reasonId?: string) => {
    const t = deleting
    if (!t) return
    deleteTeacher(t.id, reasonId)
      .then(() => {
        setArchived((prev) => prev.filter((x) => x.id !== t.id))
        setDeleting(null)
      })
      .catch((e) => alert(e?.response?.data?.message ?? "O'chirib bo'lmadi"))
  }

  // Arxiv ustunlari faqat arxiv ko'rinayotganda kerak
  const showArchiveCols = status !== 'active'

  return (
    <div>
      <CardTabs items={teacherTabs((p) => can(p, 'view'))} className="mb-5" />
      <PageHeader
        title="O'qituvchilar"
        sub={`Faol ${teachers.length} ta · Arxivda ${archived.length} ta`}
        actions={
          <>
            {/* Faqat superadmin: o'qituvchilarni login/parol bilan Excel'ga yuklab olish.
                Parol faqat o'qituvchi hali kirmagan bo'lsa ko'rinadi. */}
            {user?.role === 'superadmin' && (
              <Button variant="secondary" onClick={() => downloadTeacherCredentials()}>
                <Download className="h-4 w-4" /> Login/parollar
              </Button>
            )}
            {can('teachers.list', 'create') && (
              <Button
                onClick={() => {
                  setEditing(null)
                  setFormOpen(true)
                }}
              >
                <Plus className="h-4 w-4" /> Yangi qo'shish
              </Button>
            )}
          </>
        }
      />

      {/* KPI kartochkalar */}
      <div className="mb-5 grid grid-cols-1 gap-3 sm:grid-cols-2 lg:grid-cols-3">
        <StatCard label="Faol o'qituvchilar" value={teachers.length} icon={Users} />
        <StatCard
          label="Guruhga biriktirilgan"
          value={assignedTeachers}
          icon={GraduationCap}
          iconBg="bg-sky-50"
          iconColor="text-sky-600"
        />
        <StatCard
          label="Fanlar soni"
          value={subjects.length}
          icon={BookOpen}
          iconBg="bg-emerald-50"
          iconColor="text-emerald-600"
        />
      </div>

      {/* ---- Filtrlar (jadval ustida, bitta qatorda) ---- */}
      <Card className="mb-4 p-4">
        <div className="flex flex-wrap items-end gap-3">
          <div className="min-w-[240px] flex-1">
            <span className="mb-1 block text-sm font-medium text-slate-600">Qidiruv</span>
            <div className="relative">
              <Search className="pointer-events-none absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-slate-400" />
              <input
                className={cn(control, 'w-full pl-9')}
                value={search}
                onChange={(e) => setSearch(e.target.value)}
                placeholder="F.I.SH yoki telefon bo'yicha qidirish..."
              />
            </div>
          </div>

          <div className="w-[190px]">
            <span className="mb-1 block text-sm font-medium text-slate-600">Fan</span>
            <select
              className={cn(control, 'w-full')}
              value={subjectFilter}
              onChange={(e) => setSubjectFilter(e.target.value)}
            >
              <option value="all">Barcha fanlar</option>
              {subjects.map((s) => (
                <option key={s.id} value={s.id}>
                  {s.name}
                </option>
              ))}
            </select>
          </div>

          <div className="w-[150px]">
            <span className="mb-1 block text-sm font-medium text-slate-600">Jinsi</span>
            <select
              className={cn(control, 'w-full')}
              value={genderFilter}
              onChange={(e) => setGenderFilter(e.target.value as 'all' | Gender)}
            >
              <option value="all">Barchasi</option>
              <option value="male">{genderLabels.male}</option>
              <option value="female">{genderLabels.female}</option>
            </select>
          </div>

          <div>
            <span className="mb-1 block text-sm font-medium text-slate-600">Holat</span>
            <div className="flex items-center gap-1.5">
              {STATUS_CHIPS.map((c) => (
                <button
                  key={c.key}
                  type="button"
                  onClick={() => setStatus(c.key)}
                  className={cn(
                    'rounded-lg border px-3 py-2 text-[13px] font-semibold transition-colors',
                    status === c.key
                      ? 'border-transparent bg-brand-50 text-brand-700'
                      : 'border-slate-200 bg-white text-slate-600 hover:bg-slate-50',
                  )}
                >
                  {c.label}
                  {c.key === 'active' && ` (${teachers.length})`}
                  {c.key === 'archived' && ` (${archived.length})`}
                </button>
              ))}
            </div>
          </div>

          {filtersActive && (
            <button
              type="button"
              onClick={clearFilters}
              className="inline-flex items-center gap-1.5 rounded-lg border border-slate-200 bg-white px-3 py-2 text-sm font-medium text-slate-500 transition-colors hover:bg-slate-50 hover:text-slate-700"
              title="Barcha filtrlarni tozalash"
            >
              <X className="h-4 w-4" /> Tozalash
            </button>
          )}
        </div>
      </Card>

      {loading ? (
        <Card>
          <Loader label="Yuklanmoqda..." />
        </Card>
      ) : filtered.length === 0 ? (
        <Card>
          <div className="state">
            <h4>
              {status === 'archived' ? "Arxivda o'qituvchi yo'q" : "O'qituvchi topilmadi"}
            </h4>
            <p>
              {filtersActive
                ? 'Filtrga mos o‘qituvchi yo‘q — filtrlarni o‘zgartirib ko‘ring.'
                : "Hozircha ro'yxat bo'sh. «Yangi qo'shish» tugmasi orqali o'qituvchi qo'shing."}
            </p>
          </div>
        </Card>
      ) : (
        /* ---- O'qituvchilar jadvali ---- */
        <Card tight>
          <div className="table-wrap">
            <table className="table min-w-[1080px]">
              <thead>
                <tr>
                  <th className="w-12">№</th>
                  <th>F.I.SH</th>
                  <th>Telefon</th>
                  {/* Fanlar / Guruhlar / Maosh rejimi ustunlari ATAYIN yo'q — ro'yxat qisqa va
                      o'qishga qulay bo'lsin (bu ma'lumotlar o'qituvchi profilida ko'rinadi). */}
                  <th>Holat</th>
                  {showArchiveCols && <th>Arxiv sababi</th>}
                  <th className="num">Amallar</th>
                </tr>
              </thead>
              <tbody>
                {pg.paged.map((t, i) => {
                  const isArchived = !!t.isArchived
                  return (
                    // BUTUN QATOR bosilsa profilga o'tiladi. Amallar katagi esa bosilishni
                    // to'xtatadi (stopPropagation) — u yerda tugmalar o'z ishini bajaradi.
                    <tr
                      key={t.id}
                      className="cursor-pointer"
                      onClick={() => navigate(`/admin/teachers/${t.id}`)}
                      title="Profilga o'tish"
                    >
                      <td className="text-slate-400">{pg.rangeFrom + i}</td>

                      {/* F.I.SH — bosilsa profilga */}
                      <td>
                        <Link
                          to={`/admin/teachers/${t.id}`}
                          className="cell-user group"
                          title="Profilga o'tish"
                        >
                          {t.photoUrl ? (
                            <img src={t.photoUrl} alt="" className="avatar object-cover" />
                          ) : (
                            <div className="avatar" style={{ background: avatarColor(t.fullName) }}>
                              {initialsOf(t.fullName)}
                            </div>
                          )}
                          <div className="meta">
                            <strong className="text-slate-800 group-hover:text-brand-600">
                              {t.fullName}
                            </strong>
                            <span>
                              {genderLabels[t.gender]}
                              {t.birthDate ? ` · ${formatDate(t.birthDate)}` : ''}
                            </span>
                          </div>
                        </Link>
                      </td>

                      <td className="font-mono text-[12.5px] text-slate-600">
                        {t.phone || <span className="text-slate-300">—</span>}
                      </td>

                      {/* Fanlar / Guruhlar / Maosh rejimi — bu yerda KO'RSATILMAYDI (sarlavhadagi
                          izohga qarang). Fan bo'yicha FILTR saqlangan: u ro'yxatni toraytiradi,
                          lekin o'qituvchining fanlarini jadvalda chiqarmaydi. */}

                      {/* Holat */}
                      <td>
                        {isArchived ? (
                          <div>
                            <Badge tone="red">Arxivlangan</Badge>
                            {t.archivedAt && (
                              <div className="mt-0.5 font-mono text-[11px] text-slate-400">
                                {formatDate(t.archivedAt)}
                              </div>
                            )}
                          </div>
                        ) : t.isBlocked ? (
                          /* Vaqtincha aktiv emas — arxivdan FARQLI: paroli va tarixi joyida,
                             ro'yxatdan yo'qolmaydi, faqat tizimga kira olmaydi. */
                          <div title={t.blockNote || undefined}>
                            <Badge tone="amber">
                              <Lock className="h-3 w-3" /> Vaqtincha aktiv emas
                            </Badge>
                            {t.blockedAt && (
                              <div className="mt-0.5 font-mono text-[11px] text-slate-400">
                                {formatDate(t.blockedAt)}
                              </div>
                            )}
                          </div>
                        ) : (
                          <Badge tone="green" dot>
                            Faol
                          </Badge>
                        )}
                      </td>

                      {showArchiveCols && (
                        <td
                          className="max-w-[220px] truncate text-slate-500"
                          title={t.archiveReason ?? ''}
                        >
                          {t.archiveReason || <span className="text-slate-300">—</span>}
                        </td>
                      )}

                      {/* Amallar */}
                      {/* Amallar — qatorning profilga o'tishi bu yerda TO'XTAYDI. */}
                      <td className="num" onClick={(e) => e.stopPropagation()}>
                        <div className="flex items-center justify-end gap-0.5">
                          <Link
                            to={`/admin/teachers/${t.id}`}
                            title="Batafsil (profil)"
                            className="rounded-lg p-1.5 text-slate-400 transition-colors hover:bg-brand-50 hover:text-brand-600"
                          >
                            <ArrowUpRight className="h-4 w-4" />
                          </Link>
                          <IconBtn icon={Eye} title="Ko'rish" onClick={() => setViewing(t)} />
                          {!isArchived && can('teachers.list', 'edit') && (
                            <IconBtn
                              icon={Pencil}
                              title="Tahrirlash"
                              onClick={() => {
                                setEditing(t)
                                setFormOpen(true)
                              }}
                            />
                          )}
                          {/* Vaqtincha aktiv emas — tizimga kira olmaydi, lekin arxivlanmaydi
                              (paroli, guruhlari va tarixi joyida qoladi). */}
                          {!isArchived && can('teachers.list', 'edit') &&
                            (t.isBlocked ? (
                              <IconBtn
                                icon={Unlock}
                                title="Qayta faollashtirish (tizimga kira oladi)"
                                onClick={() => handleUnblock(t)}
                              />
                            ) : (
                              <IconBtn
                                icon={Lock}
                                title="Vaqtincha aktiv emas (tizimga kira olmaydi)"
                                onClick={() => {
                                  setBlockNote('')
                                  setBlockTarget(t)
                                }}
                              />
                            ))}
                          {!isArchived && can('teachers.list', 'delete') && (
                            <IconBtn
                              icon={Archive}
                              title="Arxivga ko'chirish"
                              onClick={() => {
                                setReason('')
                                setArchiveTarget(t)
                              }}
                            />
                          )}
                          {isArchived && can('teachers.list', 'edit') && (
                            <IconBtn
                              icon={RotateCcw}
                              title="Arxivdan qaytarish"
                              onClick={() => handleRestore(t)}
                            />
                          )}
                          {isArchived && can('teachers.list', 'delete') && (
                            <IconBtn
                              icon={Trash2}
                              title="Butunlay o'chirish"
                              danger
                              onClick={() => handleDelete(t)}
                            />
                          )}
                        </div>
                      </td>
                    </tr>
                  )
                })}
              </tbody>
            </table>
          </div>
          <TablePagination {...pg} />
        </Card>
      )}

      <TeacherFormModal
        open={formOpen}
        onClose={() => {
          setFormOpen(false)
          setEditing(null)
        }}
        onSubmit={handleSubmit}
        initial={editing}
        subjects={subjects}
      />
      <TeacherViewModal
        teacher={viewing}
        subjects={subjects}
        groups={viewing ? teacherGroups(viewing.id) : []}
        onClose={() => setViewing(null)}
      />

      <ReasonPromptModal
        open={!!deleting}
        category="teacher_delete"
        title="O'qituvchini o'chirish"
        message={deleting ? `"${deleting.fullName}" o'qituvchini BUTUNLAY o'chirasizmi? Bu amalni ortga qaytarib bo'lmaydi.` : undefined}
        confirmLabel="O'chirish"
        tone="red"
        onConfirm={doDelete}
        onClose={() => setDeleting(null)}
      />

      {/* Vaqtincha aktiv emas — arxivlash bilan chalkashmasligi uchun farqi ochiq yozilgan. */}
      <Modal
        open={!!blockTarget}
        onClose={() => setBlockTarget(null)}
        size="md"
        title="Vaqtincha aktiv emas deb belgilash"
        footer={
          <>
            <Button variant="secondary" onClick={() => setBlockTarget(null)}>
              Bekor qilish
            </Button>
            <Button onClick={confirmBlock}>
              <Lock className="h-4 w-4" /> Belgilash
            </Button>
          </>
        }
      >
        <div className="space-y-3">
          <p className="text-sm text-slate-600">
            <span className="font-semibold text-slate-800">{blockTarget?.fullName}</span> tizimga{' '}
            <b>kira olmaydi</b> — hozir kirib turgan bo'lsa ham darrov chiqib ketadi (ilova va web
            panel ikkalasi ham).
          </p>
          <div className="rounded-lg bg-slate-50 px-3 py-2 text-sm text-slate-500">
            Bu <b className="text-slate-700">arxivlash emas</b>: paroli o'chirilmaydi, guruhlari,
            maoshi va jurnali joyida qoladi, ro'yxatdan ham yo'qolmaydi. Qaytarish — bitta tugma
            (arxivdan qaytarishda esa yangi parol kerak bo'ladi).
          </div>
          <Textarea
            label="Izoh (ixtiyoriy)"
            value={blockNote}
            onChange={(e) => setBlockNote(e.target.value)}
            rows={2}
            placeholder="Masalan: ta'tilda, 20-avgustgacha"
          />
        </div>
      </Modal>

      {/* Arxivga ko'chirish tasdiqi */}
      <Modal
        open={!!archiveTarget}
        onClose={() => setArchiveTarget(null)}
        size="md"
        title="Arxivga ko'chirish"
        footer={
          <>
            <Button variant="secondary" onClick={() => setArchiveTarget(null)}>
              Bekor qilish
            </Button>
            <Button variant="danger" onClick={confirmArchive}>
              <Archive className="h-4 w-4" /> Arxivga ko'chirish
            </Button>
          </>
        }
      >
        <div className="space-y-3">
          <p className="text-sm text-slate-600">
            <span className="font-semibold text-slate-800">{archiveTarget?.fullName}</span> arxivga
            ko'chiriladi: faol ro'yxatdan yashiriladi va tizimga kirishi bloklanadi. Jurnal va
            hisobot ma'lumotlari saqlanib qoladi.
          </p>
          <Textarea
            label="Sabab (ixtiyoriy)"
            value={reason}
            onChange={(e) => setReason(e.target.value)}
            rows={3}
            placeholder="Masalan: ishdan bo'shadi"
          />
        </div>
      </Modal>
    </div>
  )
}


interface IconBtnProps {
  icon: typeof Eye
  title: string
  onClick: () => void
  danger?: boolean
}

function IconBtn({ icon: Icon, title, onClick, danger }: IconBtnProps) {
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
