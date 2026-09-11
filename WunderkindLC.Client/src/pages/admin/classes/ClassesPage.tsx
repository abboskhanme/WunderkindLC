import { useEffect, useMemo, useState } from 'react'
import { Link, useNavigate } from 'react-router-dom'
import { usePersistentState } from '@/hooks/usePersistentState'
import { usePerm } from '@/lib/permissions'
import {
  Plus,
  Pencil,
  Trash2,
  Users,
  Archive,
  ArchiveRestore,
  LayoutGrid,
  List,
  ArrowDown,
  CalendarDays,
  Clock,
  User,
  BookOpenCheck,
  X,
  Eye,
  Lock,
  Unlock,
} from 'lucide-react'
import type { Group, GroupFillRow, Teacher, Subject } from '@/types'
import type { ClassPayload } from '@/api/services/classes'
import { getSubjects } from '@/api/services/subjects'
import {
  getClasses,
  createClass,
  updateClass,
  deleteClass,
  getArchivedClasses,
  archiveClass,
  unarchiveClass,
  blockClass,
  unblockClass,
  getGroupFill,
} from '@/api/services/classes'
import { getClassesStats, type ClassStats, getAllGroupsGradingStats, type GradingGroupStats } from '@/api/services/classPerformance'
import { getTeachers } from '@/api/services/teachers'
import { languageLabels } from '@/config/constants'
import { formatMoney, formatDate, cn, gradeTextCls } from '@/lib/utils'
import { Card } from '@/components/ui/Card'
import { Button } from '@/components/ui/Button'
import { Badge } from '@/components/ui/Badge'
import { PageHeader } from '@/components/ui/PageHeader'
import { Loader } from '@/components/ui/Loader'
import { Modal } from '@/components/ui/Modal'
import { Textarea } from '@/components/ui/Input'
import { ReasonPromptModal } from '@/components/ui/ReasonPromptModal'
import { ClassFormModal } from './ClassFormModal'
import { ClassMembersModal } from './ClassMembersModal'
import { JournalPolicyModal } from './JournalPolicyModal'

// Avatar uchun ism harflari va barqaror rang (faqat ko'rinish uchun)
const initialsOf = (name: string) =>
  name
    .split(/[\s-]+/)
    .filter(Boolean)
    .slice(0, 3)
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

export function ClassesPage() {
  const navigate = useNavigate()
  const { can } = usePerm()
  const [classes, setClasses] = useState<Group[]>([])
  const [stats, setStats] = useState<Record<string, ClassStats>>({})
  const [gradingStats, setGradingStats] = useState<Record<string, GradingGroupStats>>({})
  const [teachers, setTeachers] = useState<Teacher[]>([])
  const [loading, setLoading] = useState(true)
  const [formOpen, setFormOpen] = useState(false)
  const [editing, setEditing] = useState<Group | null>(null)
  // Oylik to'lov o'zgarganda — yangi narxni o'quvchilarga qachondan qo'llashni so'rash uchun
  const [feePrompt, setFeePrompt] = useState<{
    id: string
    values: ClassPayload
    oldFee: number
  } | null>(null)
  // Xona band bo'lsa — chiroyli ogohlantirish oynasi (native alert o'rniga). Forma ochiq qoladi,
  // kiritilgan ma'lumot yo'qolmaydi; "Baribir saqlash" force bilan saqlaydi.
  const [roomConflict, setRoomConflict] = useState<{
    values: ClassPayload
    editId: string | null
    applyFee?: boolean
    list: string
  } | null>(null)
  const [savingConflict, setSavingConflict] = useState(false)
  /** Arxivlangan guruhlar ro'yxati + arxiv ko'rinishi yoqilganmi */
  const [archived, setArchived] = useState<Group[]>([])
  const [showArchived, setShowArchived] = usePersistentState('classes.showArchived', false)
  /** A'zolarni boshqarish modali uchun tanlangan guruh */
  const [membersOf, setMembersOf] = useState<Group | null>(null)
  const [deletingGroup, setDeletingGroup] = useState<Group | null>(null)
  /** Guruh to'ldirish ko'rinishi */
  const [fill, setFill] = useState<GroupFillRow[]>([])
  const [showFill, setShowFill] = useState(false)
  /** Saralash (reyting): tartib bo'yicha | o'rtacha baho | davomat. Baho/davomat — yuqoridan pastga. */
  const [sort, setSort] = usePersistentState<'order' | 'grade' | 'attendance'>('classes.sort', 'order')
  /** Ko'rinish: kartalar yoki jadval */
  const [view, setView] = usePersistentState<'card' | 'table'>('classes.view', 'table')
  /** O'qituvchi filteri — faqat shu o'qituvchining guruhlari ko'rsatiladi */
  const [teacherFilter, setTeacherFilter] = usePersistentState('classes.teacherFilter', 'all')
  /** Kurs filteri — faqat shu kursga biriktirilgan guruhlar */
  const [courseFilter, setCourseFilter] = usePersistentState('classes.courseFilter', 'all')
  /** Kun filteri: barcha | toq kunlar (Du/Cho/Ju) | juft kunlar (Se/Pay/Sha) */
  const [dayFilter, setDayFilter] = usePersistentState<'all' | 'odd' | 'even'>('classes.dayFilter', 'all')
  /** Kurslar ro'yxati (filtr uchun) */
  const [subjects, setSubjects] = useState<Subject[]>([])
  /** "Jurnal boshqaruvi" oynasi — jurnal tahrirlash siyosati (barcha guruhlar uchun) */
  const [policyOpen, setPolicyOpen] = useState(false)
  /** "Vaqtincha bloklash" oynasi — guruh o'qituvchida ko'rinmay qoladi (izoh ixtiyoriy) */
  const [blockTarget, setBlockTarget] = useState<Group | null>(null)
  const [blockNote, setBlockNote] = useState('')

  // Filtrlar standart holatdan farq qiladimi — "Tozalash" tugmasi shunda ko'rinadi.
  const filtersActive = teacherFilter !== 'all' || courseFilter !== 'all' || dayFilter !== 'all'
  const clearFilters = () => {
    setTeacherFilter('all')
    setCourseFilter('all')
    setDayFilter('all')
  }

  const filteredClasses = useMemo(() => {
    return classes.filter((c) => {
      if (teacherFilter !== 'all' && c.teacherId !== teacherFilter) return false
      if (courseFilter !== 'all' && c.courseId !== courseFilter) return false
      if (dayFilter !== 'all' && dayGroup(c.days) !== dayFilter) return false
      return true
    })
  }, [classes, teacherFilter, courseFilter, dayFilter])

  const sortedClasses = useMemo(() => {
    if (sort === 'order') return filteredClasses
    return [...filteredClasses].sort((a, b) => {
      const sa = stats[a.id]
      const sb = stats[b.id]
      if (sort === 'grade') return (sb?.averageGrade ?? -1) - (sa?.averageGrade ?? -1)
      return (sb?.attendance ?? -1) - (sa?.attendance ?? -1)
    })
  }, [filteredClasses, stats, sort])

  const teacherName = (id?: string) =>
    id ? (teachers.find((t) => t.id === id)?.fullName ?? '—') : '—'

  useEffect(() => {
    Promise.all([getClasses(), getClassesStats(), getArchivedClasses(), getTeachers(), getAllGroupsGradingStats(), getSubjects()])
      .then(([cl, st, ar, te, gs, su]) => {
        setClasses(cl)
        setStats(st)
        setArchived(ar)
        setTeachers(te)
        setGradingStats(gs)
        setSubjects(su)
      })
      .finally(() => setLoading(false))
  }, [])

  /** Konflikt ro'yxatini o'qiladigan satrga aylantiradi. */
  const conflictList = (raw: unknown): string =>
    (
      raw as
        | Array<{ groupName: string; sharedDays: string; existingSlot: string; reason?: string }>
        | undefined
    )
      ?.map((c) => {
        const tag = c.reason === 'teacher' ? "O'qituvchi band" : 'Xona band'
        return `${tag}: ${c.groupName} (${c.sharedDays}, ${c.existingSlot})`
      })
      .join('; ') ?? ''

  /** Guruhni saqlaydi (yaratish yoki yangilash). roomConflict qaytsa — forma yopilmaydi, chiroyli
   *  oyna chiqadi (ma'lumot saqlanadi). force=true bilan baribir saqlaydi. */
  const saveClass = (
    values: ClassPayload,
    opts: { editId: string | null; applyFee?: boolean; force?: boolean },
  ) => {
    const { editId, applyFee, force } = opts
    const req = editId
      ? updateClass(editId, values, applyFee, force)
      : createClass(values, force)
    return req
      .then((res) => {
        const any = res as unknown as Record<string, unknown>
        if (any.roomConflict) {
          // Xona band — chiroyli oyna ko'rsatamiz, forma ochiq qoladi.
          setRoomConflict({ values, editId, applyFee, list: conflictList(any.conflicts) })
          return false
        }
        if (editId) setClasses((prev) => prev.map((c) => (c.id === res.id ? res : c)))
        else setClasses((prev) => [...prev, res])
        // Muvaffaqiyatli — endi formani va konflikt oynasini yopamiz.
        setFormOpen(false)
        setEditing(null)
        setRoomConflict(null)
        return true
      })
      .catch((e: unknown) => {
        const msg = (e as { response?: { data?: { message?: string } } })?.response?.data?.message
        alert(msg ?? 'Saqlashda xatolik yuz berdi')
        return false
      })
  }

  const applyUpdate = (id: string, values: ClassPayload, applyFee?: boolean) =>
    saveClass(values, { editId: id, applyFee })

  const handleSubmit = (values: ClassPayload) => {
    if (editing) {
      // Oylik to'lov o'zgargan bo'lsa — o'quvchilarga qo'llashni so'raymiz (Ha/Yo'q)
      if (values.monthlyFee !== editing.monthlyFee) {
        setFeePrompt({ id: editing.id, values, oldFee: editing.monthlyFee })
        setFormOpen(false)
        setEditing(null)
        return
      }
      // DIQQAT: formani DARHOL yopmaymiz — konflikt bo'lsa ma'lumot yo'qolmasligi uchun.
      applyUpdate(editing.id, values)
    } else {
      saveClass(values, { editId: null })
    }
  }

  /** Xona band oynasidagi "Baribir saqlash" — force bilan saqlaydi. */
  const handleConflictForce = () => {
    if (!roomConflict) return
    setSavingConflict(true)
    saveClass(roomConflict.values, {
      editId: roomConflict.editId,
      applyFee: roomConflict.applyFee,
      force: true,
    }).finally(() => setSavingConflict(false))
  }

  const resolveFeePrompt = (applyFee: boolean) => {
    if (!feePrompt) return
    applyUpdate(feePrompt.id, feePrompt.values, applyFee)
    setFeePrompt(null)
  }

  const handleDelete = (c: Group) => setDeletingGroup(c)

  const doDeleteGroup = (reasonId: string | undefined) => {
    const c = deletingGroup
    if (!c) return
    deleteClass(c.id, reasonId)
      .then(() => {
        setClasses((prev) => prev.filter((x) => x.id !== c.id))
        setArchived((prev) => prev.filter((x) => x.id !== c.id))
        setDeletingGroup(null)
      })
      .catch((e) => alert(e?.response?.data?.message ?? "Guruhni o'chirib bo'lmadi"))
  }

  /**
   * Guruhni arxivlash. ⚠️ O'QUVCHILAR ARXIVLANMAYDI (2026-09 dan): server faol a'zoliklarni
   * MUZLATADI («Guruhni yopish» dagi bilan bir xil yadro — qisman to'lov qayta hisoblanadi,
   * keyingi oylar hisobi bekor qilinadi), sinovdagilarni esa guruhdan chiqaradi.
   * Shuning uchun matnlar aynan shuni aytadi — eski "o'quvchilar ham arxivlanadi" YOLG'ON edi.
   */
  const handleArchive = (c: Group) => {
    if (
      !confirm(
        `"${c.name}" guruhini arxivlaysizmi?\n\n` +
          `• O'quvchilar ARXIVLANMAYDI — ular o'quvchilar ro'yxatida qolaveradi.\n` +
          `• Shu guruhdagi faol a'zoliklar MUZLATILADI: oylik hisoblanmaydi va o'quvchi «Aktiv emas» bo'lib ko'rinadi.\n` +
          `• Sinovdagi a'zoliklar guruhdan chiqariladi.`,
      )
    )
      return
    archiveClass(c.id)
      .then((r) => {
        setClasses((prev) => prev.filter((x) => x.id !== c.id))
        setArchived((prev) => [{ ...c, isArchived: true }, ...prev])
        const extra = r.trialClosed > 0 ? `, ${r.trialClosed} ta sinovdagi a'zolik guruhdan chiqarildi` : ''
        alert(`"${c.name}" arxivlandi — ${r.frozenMembers} ta a'zolik muzlatildi${extra}. O'quvchilar arxivlanmadi.`)
      })
      .catch((e) => alert(e?.response?.data?.message ?? 'Arxivlashda xatolik'))
  }

  /**
   * Arxivdan chiqarish. ⚠️ A'zoliklar MUZLATILGANICHA qoladi — avtomatik aktivlashtirilmaydi
   * (aks holda oylik hisobi o'z-o'zidan qayta boshlanib ketardi). `restoredStudents` faqat ESKI
   * yozuvlar uchun: o'sha paytda guruh bilan birga arxivlangan o'quvchilar.
   */
  const handleUnarchive = (c: Group) => {
    if (
      !confirm(
        `"${c.name}" guruhini arxivdan chiqarasizmi?\n\n` +
          `A'zoliklar MUZLATILGANICHA qoladi — ular avtomatik aktivlashtirilmaydi. ` +
          `O'quvchini yana o'qishga qaytarish uchun uning a'zoligini qo'lda aktivlashtiring.`,
      )
    )
      return
    unarchiveClass(c.id)
      .then((r) => {
        setArchived((prev) => prev.filter((x) => x.id !== c.id))
        setClasses((prev) => [...prev, { ...c, isArchived: false }])
        const extra = r.restoredStudents > 0 ? ` ${r.restoredStudents} ta o'quvchi arxivdan qaytarildi.` : ''
        alert(
          `"${c.name}" arxivdan chiqarildi.${extra} A'zoliklar muzlatilganicha qoldi — kerak bo'lsa qo'lda aktivlashtiring.`,
        )
      })
      .catch((e) => alert(e?.response?.data?.message ?? 'Arxivdan chiqarishda xatolik'))
  }

  /** Vaqtincha bloklash — guruh o'qituvchida ko'rinmay qoladi (admin ro'yxatida qoladi). */
  const confirmBlock = () => {
    const c = blockTarget
    if (!c) return
    const note = blockNote.trim()
    const today = new Date().toISOString().slice(0, 10)
    blockClass(c.id, note)
      .then(() => {
        setClasses((prev) =>
          prev.map((x) =>
            x.id === c.id ? { ...x, isBlocked: true, blockedAt: today, blockNote: note } : x,
          ),
        )
        setBlockTarget(null)
        setBlockNote('')
      })
      .catch((e) => alert(e?.response?.data?.message ?? 'Bloklashda xatolik'))
  }

  const handleUnblock = (c: Group) => {
    unblockClass(c.id)
      .then(() =>
        setClasses((prev) =>
          prev.map((x) =>
            x.id === c.id ? { ...x, isBlocked: false, blockedAt: null, blockNote: '' } : x,
          ),
        ),
      )
      .catch((e) => alert(e?.response?.data?.message ?? 'Blokdan chiqarishda xatolik'))
  }

  return (
    <div>
      <PageHeader
        title={showArchived ? 'Arxivlangan guruhlar' : 'Guruhlar'}
        sub={
          showArchived
            ? `${archived.length} ta arxivlangan guruh`
            : `Jami ${classes.length} ta guruh`
        }
        actions={
          <>
            {!showArchived && (
              <Button
                variant="secondary"
                onClick={() => {
                  setShowFill((v) => {
                    const next = !v
                    if (next && fill.length === 0) getGroupFill().then(setFill).catch(() => {})
                    return next
                  })
                }}
              >
                <LayoutGrid className="h-4 w-4" /> {showFill ? 'Jadvalni yopish' : "Guruh to'ldirish"}
              </Button>
            )}
            <Button variant="secondary" onClick={() => setShowArchived((v) => !v)}>
              {showArchived ? (
                <>
                  <Users className="h-4 w-4" /> Faol guruhlar
                </>
              ) : (
                <>
                  <Archive className="h-4 w-4" /> Arxiv ({archived.length})
                </>
              )}
            </Button>
            {!showArchived && (
              <Button variant="secondary" onClick={() => setPolicyOpen(true)}>
                <BookOpenCheck className="h-4 w-4" /> Jurnal boshqaruvi
              </Button>
            )}
            {!showArchived && can('classes.list', 'create') && (
              <Button
                onClick={() => {
                  setEditing(null)
                  setFormOpen(true)
                }}
              >
                <Plus className="h-4 w-4" /> Yangi guruh
              </Button>
            )}
          </>
        }
      />

      {/* Saralash (reyting) toolbar — faqat faol guruhlar uchun */}
      {!showArchived && !loading && classes.length > 0 && (
        <div className="toolbar">
          <div className="left">
            <select
              value={teacherFilter}
              onChange={(e) => setTeacherFilter(e.target.value)}
              className="rounded-lg border border-slate-200 bg-white px-3 py-2 text-sm font-medium text-slate-700 outline-none transition-colors focus:border-brand-400 focus:ring-2 focus:ring-brand-100"
            >
              <option value="all">Barcha o'qituvchilar</option>
              {teachers
                .filter((t) => classes.some((c) => c.teacherId === t.id))
                .sort((a, b) => a.fullName.localeCompare(b.fullName))
                .map((t) => (
                  <option key={t.id} value={t.id}>
                    {t.fullName}
                  </option>
                ))}
            </select>

            {/* Kurs filtri */}
            <select
              value={courseFilter}
              onChange={(e) => setCourseFilter(e.target.value)}
              className="rounded-lg border border-slate-200 bg-white px-3 py-2 text-sm font-medium text-slate-700 outline-none transition-colors focus:border-brand-400 focus:ring-2 focus:ring-brand-100"
            >
              <option value="all">Barcha kurslar</option>
              {subjects
                .filter((s) => classes.some((c) => c.courseId === s.id))
                .sort((a, b) => a.name.localeCompare(b.name))
                .map((s) => (
                  <option key={s.id} value={s.id}>
                    {s.name}
                  </option>
                ))}
            </select>

            {/* Kun filtri: toq / juft */}
            <select
              value={dayFilter}
              onChange={(e) => setDayFilter(e.target.value as 'all' | 'odd' | 'even')}
              className="rounded-lg border border-slate-200 bg-white px-3 py-2 text-sm font-medium text-slate-700 outline-none transition-colors focus:border-brand-400 focus:ring-2 focus:ring-brand-100"
            >
              <option value="all">Barcha kunlar</option>
              <option value="odd">Toq kunlar (Du/Cho/Ju)</option>
              <option value="even">Juft kunlar (Se/Pay/Sha)</option>
            </select>

            {filtersActive && (
              <button
                type="button"
                onClick={clearFilters}
                className="inline-flex items-center gap-1.5 rounded-lg border border-slate-200 bg-white px-3 py-2 text-sm font-medium text-slate-500 transition-colors hover:bg-slate-50 hover:text-slate-700"
                title="Barcha filtrlarni tozalash"
              >
                <X className="h-4 w-4" /> Filtrni tozalash
              </button>
            )}

            <span className="text-xs font-semibold uppercase tracking-wide text-slate-400">
              Saralash:
            </span>
            <SortChip
              label="Tartib"
              active={sort === 'order'}
              onClick={() => setSort('order')}
            />
            <SortChip
              label="O'rtacha baho"
              active={sort === 'grade'}
              onClick={() => setSort('grade')}
            />
            <SortChip
              label="Davomat"
              active={sort === 'attendance'}
              onClick={() => setSort('attendance')}
            />
          </div>

          {/* Ko'rinishni tanlash: kartalar | jadval */}
          <div className="right">
            <div className="tabs" role="tablist">
              <button
                type="button"
                role="tab"
                onClick={() => setView('card')}
                className={cn('tab inline-flex items-center gap-1.5', view === 'card' && 'active')}
              >
                <LayoutGrid className="h-3.5 w-3.5" /> Kartalar
              </button>
              <button
                type="button"
                role="tab"
                onClick={() => setView('table')}
                className={cn('tab inline-flex items-center gap-1.5', view === 'table' && 'active')}
              >
                <List className="h-3.5 w-3.5" /> Jadval
              </button>
            </div>
          </div>
        </div>
      )}

      {loading ? (
        <Card>
          <Loader label="Yuklanmoqda..." />
        </Card>
      ) : showArchived ? (
        <Card tight>
          <ArchivedTable
            items={archived}
            onUnarchive={handleUnarchive}
            onDelete={handleDelete}
            canDelete={can('classes.list', 'delete')}
          />
        </Card>
      ) : classes.length === 0 ? (
        <Card>
          <div className="state">
            <h4>Guruhlar yo'q</h4>
            <p>Yangi guruh qo'shing.</p>
          </div>
        </Card>
      ) : view === 'table' ? (
        /* ---- Faol guruhlar — jadval ko'rinishi ---- */
        <Card tight>
          <div className="table-wrap">
            <table className="table">
              <thead>
                <tr>
                  <th>Guruh</th>
                  <th>Til</th>
                  <th>O'qituvchi</th>
                  <th>Kunlar</th>
                  <th>Vaqt</th>
                  <th className="num">O'quvchilar</th>
                  <th className="num">O'rtacha</th>
                  <th className="num">Davomat</th>
                  <th className="num">Baholash</th>
                  <th className="num">Oylik to'lov</th>
                  <th className="text-right">Amallar</th>
                </tr>
              </thead>
              <tbody>
                {sortedClasses.map((c) => {
                  const st = stats[c.id]
                  const gs = gradingStats[c.id]
                  return (
                    <tr
                      key={c.id}
                      className="cursor-pointer"
                      onClick={() => navigate(`/admin/classes/${c.id}`)}
                    >
                      <td>
                        <div className="cell-user">
                          <div className="avatar" style={{ background: avatarColor(c.name) }}>
                            {initialsOf(c.name)}
                          </div>
                          <div className="meta">
                            <strong className="flex items-center gap-1.5">
                              <Link
                                to={`/admin/classes/${c.id}`}
                                onClick={(e) => e.stopPropagation()}
                                className="text-inherit no-underline hover:underline"
                              >
                                {c.name}
                              </Link>
                              {c.isBlocked && (
                                <Badge tone="amber">
                                  <Lock className="h-3 w-3" /> Bloklangan
                                </Badge>
                              )}
                            </strong>
                            <span>
                              {c.isBlocked
                                ? "O'qituvchida ko'rinmaydi"
                                : c.room || '—'}
                            </span>
                          </div>
                        </div>
                      </td>
                      <td>
                        <Badge tone={c.language === 'uz' ? 'blue' : 'amber'}>
                          {languageLabels[c.language]}
                        </Badge>
                      </td>
                      <td className="text-slate-600">
                        {c.teacherId ? (
                          <Link
                            to={`/admin/teachers/${c.teacherId}`}
                            onClick={(e) => e.stopPropagation()}
                            className="text-inherit hover:text-brand-600 hover:underline"
                          >
                            {teacherName(c.teacherId)}
                          </Link>
                        ) : (
                          teacherName(c.teacherId)
                        )}
                      </td>
                      <td className="text-slate-600">{formatDays(c.days)}</td>
                      <td className="num text-slate-600">{formatTime(c.startTime, c.endTime)}</td>
                      <td className="num">{st ? st.studentsCount : '—'}</td>
                      <td className={cn('num font-semibold', st && gradeTextCls(st.averageGrade))}>
                        {st ? st.averageGrade.toFixed(1) : '—'}
                      </td>
                      <td
                        className={cn(
                          'num font-semibold',
                          st && st.attendance != null && attColor(st.attendance),
                        )}
                      >
                        {st && st.attendance != null ? `${st.attendance}%` : '—'}
                      </td>
                      <td className="num text-slate-700 font-mono">
                        {gs ? `${gs.totalGrades}` : '—'}
                      </td>
                      <td className="num font-semibold text-slate-800">
                        {formatMoney(c.monthlyFee)}
                      </td>
                      <td onClick={(e) => e.stopPropagation()}>
                        <div className="flex items-center justify-end gap-0.5">
                          <IconBtn icon={Users} title="A'zolar" onClick={() => setMembersOf(c)} />
                          {can('classes.list', 'edit') && (
                            <IconBtn
                              icon={Pencil}
                              title="Tahrirlash"
                              onClick={() => {
                                setEditing(c)
                                setFormOpen(true)
                              }}
                            />
                          )}
                          {/* Vaqtincha bloklash — guruh o'qituvchi ilovasida ko'rinmay qoladi
                              (arxivlash EMAS: o'quvchi/a'zolik/hisob tegilmaydi). */}
                          {can('classes.list', 'edit') &&
                            (c.isBlocked ? (
                              <IconBtn
                                icon={Unlock}
                                title="Blokdan chiqarish (o'qituvchida yana ko'rinadi)"
                                onClick={() => handleUnblock(c)}
                              />
                            ) : (
                              <IconBtn
                                icon={Lock}
                                title="Vaqtincha bloklash (o'qituvchida ko'rinmaydi)"
                                onClick={() => {
                                  setBlockNote('')
                                  setBlockTarget(c)
                                }}
                              />
                            ))}
                          {can('classes.list', 'delete') && (
                            <IconBtn
                              icon={Archive}
                              title="Arxivlash (a'zoliklar muzlatiladi)"
                              onClick={() => handleArchive(c)}
                            />
                          )}
                          {can('classes.list', 'delete') && (
                            <IconBtn icon={Trash2} title="O'chirish" danger onClick={() => handleDelete(c)} />
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
      ) : (
        /* ---- Faol guruhlar — kartalar (kattaroq) ---- */
        <div className="grid gap-4 [grid-template-columns:repeat(auto-fill,minmax(340px,1fr))]">
          {sortedClasses.map((c) => {
            const st = stats[c.id]
            const gs = gradingStats[c.id]
            return (
              <div
                key={c.id}
                className="entity-card cursor-pointer"
                style={{ padding: '18px 20px' }}
                onClick={() => navigate(`/admin/classes/${c.id}`)}
              >
                <div className="ec-head">
                  <div
                    className="avatar h-12 w-12 text-[15px]"
                    style={{ background: avatarColor(c.name) }}
                  >
                    {initialsOf(c.name)}
                  </div>
                  <div className="min-w-0 flex-1">
                    <div className="ec-name truncate">
                      <Link
                        to={`/admin/classes/${c.id}`}
                        onClick={(e) => e.stopPropagation()}
                        className="text-inherit no-underline hover:underline"
                      >
                        {c.name}
                      </Link>
                    </div>
                    <div className="ec-meta truncate">
                      {languageLabels[c.language]}
                      {st && st.studentsCount > 0 && (
                        <span className="ml-2 text-slate-500">
                          • {st.studentsCount} o'quvchi
                        </span>
                      )}
                    </div>
                  </div>
                  {c.isBlocked ? (
                    <Badge tone="amber">
                      <Lock className="h-3 w-3" /> Bloklangan
                    </Badge>
                  ) : (
                    <Badge tone={c.language === 'uz' ? 'blue' : 'amber'}>
                      {languageLabels[c.language]}
                    </Badge>
                  )}
                </div>

                {/* O'qituvchi · kunlar · vaqt · xona */}
                <div className="flex flex-col gap-1.5 text-[12.5px]">
                  <div className="flex items-center justify-between gap-2">
                    <span className="inline-flex items-center gap-1.5 text-slate-400">
                      <User className="h-3.5 w-3.5" /> O'qituvchi
                    </span>
                    <span className="truncate font-medium text-slate-700">
                      {c.teacherId ? (
                        <Link
                          to={`/admin/teachers/${c.teacherId}`}
                          onClick={(e) => e.stopPropagation()}
                          className="text-inherit hover:text-brand-600 hover:underline"
                        >
                          {teacherName(c.teacherId)}
                        </Link>
                      ) : (
                        teacherName(c.teacherId)
                      )}
                    </span>
                  </div>
                  <div className="flex items-center justify-between gap-2">
                    <span className="inline-flex items-center gap-1.5 text-slate-400">
                      <CalendarDays className="h-3.5 w-3.5" /> Kunlar
                    </span>
                    <span className="text-slate-600">{formatDays(c.days)}</span>
                  </div>
                  <div className="flex items-center justify-between gap-2">
                    <span className="inline-flex items-center gap-1.5 text-slate-400">
                      <Clock className="h-3.5 w-3.5" /> Vaqt
                    </span>
                    <span className="font-mono text-slate-600">
                      {formatTime(c.startTime, c.endTime)}
                    </span>
                  </div>
                </div>

                {/* Statistika bloki: o'quvchilar · o'rtacha baho · davomat · baholash */}
                <div className="ec-stats">
                  <div>
                    <div className="ec-stat-label">O'quvchilar</div>
                    <div className="ec-stat-value font-semibold">
                      {st?.studentsCount ?? '—'}
                    </div>
                  </div>
                  <div>
                    <div className="ec-stat-label">O'rtacha</div>
                    <div className={cn('ec-stat-value', st && gradeTextCls(st.averageGrade))}>
                      {st ? st.averageGrade.toFixed(1) : '—'}
                    </div>
                  </div>
                  <div>
                    <div className="ec-stat-label">Davomat</div>
                    <div
                      className={cn(
                        'ec-stat-value',
                        st && st.attendance != null && attColor(st.attendance),
                      )}
                    >
                      {st && st.attendance != null ? `${st.attendance}%` : '—'}
                    </div>
                  </div>
                  <div>
                    <div className="ec-stat-label">Baholash</div>
                    <div className="ec-stat-value font-mono font-semibold text-blue-600">
                      {gs ? `📊 ${gs.averageScore.toFixed(1)}` : '—'}
                    </div>
                  </div>
                </div>

                {/* Oylik to'lov + xona */}
                <div className="flex items-center justify-between text-[12.5px]">
                  <span className="text-slate-400">Oylik to'lov</span>
                  <span className="font-mono font-semibold text-slate-800">
                    {formatMoney(c.monthlyFee)}
                  </span>
                </div>

                <div className="ec-foot" onClick={(e) => e.stopPropagation()}>
                  <Button
                    variant="secondary"
                    className="flex-1"
                    onClick={() => setMembersOf(c)}
                  >
                    <Users className="h-4 w-4" /> A'zolar
                  </Button>
                  {can('classes.list', 'edit') && (
                    <Button
                      variant="secondary"
                      className="flex-1"
                      onClick={() => {
                        setEditing(c)
                        setFormOpen(true)
                      }}
                    >
                      <Pencil className="h-4 w-4" /> Tahrirlash
                    </Button>
                  )}
                  {can('classes.list', 'edit') &&
                    (c.isBlocked ? (
                      <Button
                        variant="secondary"
                        title="Blokdan chiqarish (o'qituvchida yana ko'rinadi)"
                        aria-label="Blokdan chiqarish"
                        onClick={() => handleUnblock(c)}
                      >
                        <Unlock className="h-4 w-4 text-amber-600" />
                      </Button>
                    ) : (
                      <Button
                        variant="secondary"
                        title="Vaqtincha bloklash (o'qituvchida ko'rinmaydi)"
                        aria-label="Vaqtincha bloklash"
                        onClick={() => {
                          setBlockNote('')
                          setBlockTarget(c)
                        }}
                      >
                        <Lock className="h-4 w-4" />
                      </Button>
                    ))}
                  {can('classes.list', 'delete') && (
                    <Button
                      variant="secondary"
                      title="Arxivlash (a'zoliklar muzlatiladi)"
                      aria-label="Arxivlash"
                      onClick={() => handleArchive(c)}
                    >
                      <Archive className="h-4 w-4" />
                    </Button>
                  )}
                  {can('classes.list', 'delete') && (
                    <Button
                      variant="secondary"
                      title="O'chirish"
                      aria-label="O'chirish"
                      onClick={() => handleDelete(c)}
                    >
                      <Trash2 className="h-4 w-4 text-red-500" />
                    </Button>
                  )}
                </div>
              </div>
            )
          })}
        </div>
      )}

      {showFill && !showArchived && (
        <Card tight className="mt-5">
          <div className="border-b border-slate-100 px-4 py-3">
            <h2 className="text-sm font-semibold text-slate-700">Guruh to'ldirish</h2>
          </div>
          <div className="overflow-x-auto">
            <table className="w-full text-left text-sm">
              <thead className="bg-slate-50 text-xs uppercase tracking-wide text-slate-400">
                <tr>
                  <th className="px-4 py-3">Guruh</th>
                  <th className="px-4 py-3">Daraja</th>
                  <th className="px-4 py-3">O'quvchilar</th>
                  <th className="px-4 py-3">Sig'im</th>
                  <th className="px-4 py-3">Bo'sh o'rin</th>
                  <th className="px-4 py-3">Holat</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-slate-100">
                {fill.map((r) => (
                  <tr key={r.groupId} className="hover:bg-slate-50/60">
                    <td className="px-4 py-3 font-medium text-slate-800">{r.name}</td>
                    <td className="px-4 py-3 text-slate-600">{r.grade}</td>
                    <td className="px-4 py-3 text-slate-600">{r.enrolled}</td>
                    <td className="px-4 py-3 text-slate-600">{r.capacity === 0 ? 'cheksiz' : r.capacity}</td>
                    <td className="px-4 py-3">
                      <span
                        className={cn(
                          'font-medium',
                          r.capacity > 0 && r.freeSeats === 0 ? 'text-red-600' : 'text-emerald-600',
                        )}
                      >
                        {r.capacity === 0 ? '—' : r.freeSeats}
                      </span>
                    </td>
                    <td className="px-4 py-3">
                      <span
                        className={cn(
                          'rounded-md px-2 py-0.5 text-xs font-medium',
                          statusBadge(r.status),
                        )}
                      >
                        {statusLabel(r.status)}
                      </span>
                    </td>
                  </tr>
                ))}
                {fill.length === 0 && (
                  <tr>
                    <td colSpan={6} className="px-4 py-12 text-center text-slate-400">
                      Ma'lumot yo'q
                    </td>
                  </tr>
                )}
              </tbody>
            </table>
          </div>
        </Card>
      )}

      <ClassFormModal
        open={formOpen}
        onClose={() => {
          setFormOpen(false)
          setEditing(null)
        }}
        onSubmit={handleSubmit}
        initial={editing}
      />

      <ClassMembersModal group={membersOf} onClose={() => setMembersOf(null)} />

      <JournalPolicyModal open={policyOpen} onClose={() => setPolicyOpen(false)} />

      {/* Vaqtincha bloklash — nima bo'lishini ANIQ yozamiz, chunki nomi "arxivlash"ga o'xshaydi. */}
      <Modal
        open={!!blockTarget}
        onClose={() => setBlockTarget(null)}
        size="sm"
        title="Guruhni vaqtincha bloklash"
        footer={
          <>
            <Button variant="secondary" onClick={() => setBlockTarget(null)}>
              Bekor qilish
            </Button>
            <Button onClick={confirmBlock}>
              <Lock className="h-4 w-4" /> Bloklash
            </Button>
          </>
        }
      >
        <div className="space-y-3 text-sm text-slate-600">
          <p>
            <span className="font-semibold text-slate-800">{blockTarget?.name}</span> guruhi
            o'qituvchi ilovasida <b>umuman ko'rinmay qoladi</b>: guruhlar ro'yxati, jurnal,
            baholash, testlar va guruh chati. O'qituvchi unga hech narsa yoza olmaydi.
          </p>
          <div className="rounded-lg bg-slate-50 px-3 py-2 text-slate-500">
            Bu <b className="text-slate-700">arxivlash emas</b>: o'quvchilar, a'zoliklar, oylik
            hisobi va hisobotlar tegilmaydi. Guruh admin panelida faol ro'yxatda qolaveradi va
            bir tugma bilan qaytariladi.
          </div>
          <Textarea
            label="Izoh (ixtiyoriy)"
            value={blockNote}
            onChange={(e) => setBlockNote(e.target.value)}
            rows={2}
            placeholder="Masalan: o'qituvchi almashguncha yopib turamiz"
          />
        </div>
      </Modal>

      <ReasonPromptModal
        open={!!deletingGroup}
        category="group_delete"
        title="Guruhni o'chirish"
        message={deletingGroup ? `"${deletingGroup.name}" guruhini o'chirasizmi?` : undefined}
        confirmLabel="O'chirish"
        tone="red"
        onConfirm={doDeleteGroup}
        onClose={() => setDeletingGroup(null)}
      />

      <Modal
        open={!!feePrompt}
        onClose={() => setFeePrompt(null)}
        title="Oylik to'lovni o'quvchilarga qo'llash"
        size="sm"
        footer={
          <>
            <Button variant="secondary" onClick={() => resolveFeePrompt(false)}>
              Yo'q — keyingi oydan
            </Button>
            <Button onClick={() => resolveFeePrompt(true)}>Ha — joriy oydan</Button>
          </>
        }
      >
        {feePrompt && (
          <div className="space-y-3 text-sm text-slate-600">
            <p>
              <span className="font-medium text-slate-800">{feePrompt.values.name}</span> guruhining
              oylik to'lovi{' '}
              <span className="font-medium">{formatMoney(feePrompt.oldFee)}</span> →{' '}
              <span className="font-medium">{formatMoney(feePrompt.values.monthlyFee)}</span> so'mga
              o'zgardi. Yangi narx shu guruhdagi o'quvchilarga qachondan qo'llansin?
            </p>
            <div className="rounded-lg bg-slate-50 px-3 py-2 text-slate-500">
              <p>
                <b className="text-slate-700">Ha</b> — joriy oy to'lovi yangi narxga o'zgaradi
                (balans farqqa moslab to'g'rilanadi).
              </p>
              <p className="mt-1">
                <b className="text-slate-700">Yo'q</b> — joriy oy eski narxda qoladi, yangi narx
                keyingi oydan hisoblanadi.
              </p>
            </div>
          </div>
        )}
      </Modal>

      {/* Xona/o'qituvchi band — chiroyli ogohlantirish oynasi (native alert o'rniga). Forma ochiq qoladi. */}
      <Modal
        open={!!roomConflict}
        onClose={() => setRoomConflict(null)}
        title="Jadvalda to'qnashuv bor"
        size="sm"
        footer={
          <>
            <Button variant="secondary" onClick={() => setRoomConflict(null)} disabled={savingConflict}>
              Orqaga
            </Button>
            <Button variant="danger" onClick={handleConflictForce} disabled={savingConflict}>
              {savingConflict ? 'Saqlanmoqda...' : 'Baribir saqlash'}
            </Button>
          </>
        }
      >
        {roomConflict && (
          <div className="space-y-3 text-sm">
            <p className="font-medium text-red-700">
              Shu vaqt oralig'ida xona yoki o'qituvchi band bo'lgan boshqa guruh bor:
            </p>
            <div className="rounded-lg bg-red-50 px-3 py-2 text-slate-700">{roomConflict.list}</div>
            <p className="text-slate-500">
              Vaqt, xona yoki o'qituvchini o'zgartirish uchun <b>Orqaga</b> bosing (ma'lumotlaringiz saqlanib qoladi),
              yoki shunday saqlash uchun <b>Baribir saqlash</b>.
            </p>
          </div>
        )}
      </Modal>
    </div>
  )
}

function attColor(a: number): string {
  if (a >= 95) return 'text-emerald-600'
  if (a >= 90) return 'text-amber-600'
  return 'text-red-600'
}

// Toq kunlar = Dushanba(0)/Chorshanba(2)/Juma(4); juft kunlar = Seshanba(1)/Payshanba(3)/Shanba(5).
// Guruh KUNLARI shu naqshga to'liq mos kelsa "odd"/"even", aralash/har kuni bo'lsa "other".
const ODD_DAYS = [0, 2, 4]
const EVEN_DAYS = [1, 3, 5]
function dayGroup(days?: number[]): 'odd' | 'even' | 'other' {
  if (!days || days.length === 0) return 'other'
  if (days.every((d) => ODD_DAYS.includes(d))) return 'odd'
  if (days.every((d) => EVEN_DAYS.includes(d))) return 'even'
  return 'other'
}

const DAY_SHORT = ['Du', 'Se', 'Cho', 'Pay', 'Ju', 'Sha', 'Yak']
function formatDays(days?: number[]): string {
  if (!days || days.length === 0) return '—'
  return [...days].sort((a, b) => a - b).map((d) => DAY_SHORT[d] ?? '?').join(', ')
}
function formatTime(start?: string, end?: string): string {
  if (start && end) return `${start}–${end}`
  return start || end || '—'
}

/** Saralash chipi (reyting uchun — bosilganda yuqoridan pastga saralaydi). */
function SortChip({ label, active, onClick }: { label: string; active: boolean; onClick: () => void }) {
  return (
    <button
      type="button"
      onClick={onClick}
      title="Reyting bo'yicha saralash"
      className={cn('filter-chip', active && 'active')}
    >
      {label}
      {active && label !== 'Tartib' && <ArrowDown className="h-3 w-3" />}
    </button>
  )
}

function statusLabel(s: GroupFillRow['status']): string {
  return s === 'full' ? "To'lgan" : s === 'archived' ? 'Arxiv' : 'Faol'
}

function statusBadge(s: GroupFillRow['status']): string {
  return s === 'full'
    ? 'bg-red-50 text-red-700'
    : s === 'archived'
      ? 'bg-slate-100 text-slate-500'
      : 'bg-emerald-50 text-emerald-700'
}

function ArchivedTable({
  items,
  onUnarchive,
  onDelete,
  canDelete,
}: {
  items: Group[]
  onUnarchive: (c: Group) => void
  onDelete: (c: Group) => void
  canDelete: boolean
}) {
  return (
    <div className="overflow-x-auto">
      <table className="w-full text-left text-sm">
        <thead className="bg-slate-50 text-xs uppercase tracking-wide text-slate-400">
          <tr>
            <th className="w-10 px-4 py-3">#</th>
            <th className="px-4 py-3">Guruh nomi</th>
            <th className="px-4 py-3">Til</th>
            <th className="px-4 py-3">Xona</th>
            <th className="px-4 py-3">Arxiv sanasi</th>
            <th className="px-4 py-3 text-right">Amallar</th>
          </tr>
        </thead>
        <tbody className="divide-y divide-slate-100">
          {items.map((c, i) => (
            <tr key={c.id} className="hover:bg-slate-50/60">
              <td className="px-4 py-3 text-slate-400">{i + 1}</td>
              {/* Arxivdagi guruh ham ochiladi — jurnal/a'zolar/tarixi faqat ko'rish uchun qoladi. */}
              <td className="px-4 py-3 font-medium text-slate-800">
                <Link
                  to={`/admin/classes/${c.id}`}
                  className="text-inherit no-underline hover:text-brand-600 hover:underline"
                >
                  {c.name}
                </Link>
              </td>
              <td className="px-4 py-3">
                <span
                  className={cn(
                    'rounded-md px-2 py-0.5 text-xs font-medium',
                    c.language === 'uz' ? 'bg-blue-50 text-blue-700' : 'bg-amber-50 text-amber-700',
                  )}
                >
                  {languageLabels[c.language]}
                </span>
              </td>
              <td className="px-4 py-3 text-slate-600">{c.room || '—'}</td>
              <td className="px-4 py-3 text-slate-500">{c.archivedAt ? formatDate(c.archivedAt) : '—'}</td>
              <td className="px-4 py-3">
                <div className="flex items-center justify-end gap-1">
                  {/* Arxivdagi guruh ma'lumotlari (jurnal, a'zolar, baholash, tarix) — faqat ko'rish. */}
                  <Link
                    to={`/admin/classes/${c.id}`}
                    className="inline-flex items-center gap-1 rounded-lg border border-slate-200 bg-slate-50 px-2.5 py-1 text-xs font-medium text-slate-600 transition-colors hover:border-brand-300 hover:bg-brand-50 hover:text-brand-700"
                  >
                    <Eye className="h-3.5 w-3.5" /> Ko'rish
                  </Link>
                  {canDelete && (
                    <IconBtn
                      icon={ArchiveRestore}
                      title="Arxivdan chiqarish (o'quvchilari bilan)"
                      onClick={() => onUnarchive(c)}
                    />
                  )}
                  {canDelete && (
                    <IconBtn icon={Trash2} title="O'chirish" danger onClick={() => onDelete(c)} />
                  )}
                </div>
              </td>
            </tr>
          ))}
          {items.length === 0 && (
            <tr>
              <td colSpan={6} className="px-4 py-12 text-center text-slate-400">
                Arxivlangan guruh yo'q
              </td>
            </tr>
          )}
        </tbody>
      </table>
    </div>
  )
}

interface IconBtnProps {
  icon: typeof Pencil
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
