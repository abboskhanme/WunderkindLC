import { useEffect, useMemo, useState } from 'react'
import { Link, useNavigate } from 'react-router-dom'
import {
  IconArchive,
  IconArchiveOff,
  IconBook2,
  IconEye,
  IconFileExport,
  IconFilterOff,
  IconLayoutGrid,
  IconLock,
  IconLockOpen,
  IconPencil,
  IconPlus,
  IconTrash,
  IconUsers,
} from '@tabler/icons-react'
import { Lock } from 'lucide-react'
import { usePersistentState } from '@/hooks/usePersistentState'
import { usePerm } from '@/lib/permissions'
import type { Group, GroupFillRow, Room, Teacher, Subject } from '@/types'
import type { ClassPayload } from '@/api/services/classes'
import { getSubjects } from '@/api/services/subjects'
import { getRooms } from '@/api/services/rooms'
import { getTodayLessons } from '@/api/services/dashboard'
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
import { formatMoney, formatDate, cn, gradeTextCls, exportToCsv } from '@/lib/utils'
import {
  WEEKDAYS,
  dayKind,
  formatGroupDays,
  formatGroupPeriod,
  formatLessonTime,
  lessonCovers,
  notAttendedGroupIds,
} from '@/lib/groupDisplay'
import { Button } from '@/components/ui/Button'
import { Modal } from '@/components/ui/Modal'
import { Textarea } from '@/components/ui/Input'
import { ReasonPromptModal } from '@/components/ui/ReasonPromptModal'
import { DataTable, type DataColumn } from '@/components/ui/list/DataTable'
import { ListToolbar } from '@/components/ui/list/ListToolbar'
import { FilterGrid, FilterInput, FilterSelect } from '@/components/ui/list/FilterGrid'
import { TotalPill } from '@/components/ui/list/TotalPill'
import { MoreMenu } from '@/components/ui/list/MoreMenu'
import { RowActionBar } from '@/components/ui/list/RowActionBar'
import { TablePagination, usePagination } from '@/components/ui/TablePagination'
import { ClassFormModal } from './ClassFormModal'
import { ClassMembersModal } from './ClassMembersModal'
import { JournalPolicyModal } from './JournalPolicyModal'

type SortKey = '' | 'name' | 'students' | 'avg' | 'att'

/**
 * Guruh → Guruh (edutizim `/group/groups`): sahifa SARLAVHASIZ — birinchi qator asboblar
 * (chapda "Qo'shish", o'ngda filtr voronkasi va "⋮"), ostida doim ochiq filtr to'ri, kulrang
 * jamlanma qatori va DataGrid ko'rinishidagi jadval.
 *
 * ⚠️ Bu RE-LAYOUT: barcha amallar (yaratish/tahrirlash + xona konflikti + narx so'rovi, a'zolar
 * oynasi, vaqtincha bloklash, arxivlash/arxivdan chiqarish, o'chirish, jurnal boshqaruvi, guruh
 * to'ldirish, reyting bo'yicha saralash) avvalgi endpointlar orqali ishlaydi.
 */
export function ClassesPage() {
  const navigate = useNavigate()
  const { can } = usePerm()
  const [classes, setClasses] = useState<Group[]>([])
  const [stats, setStats] = useState<Record<string, ClassStats>>({})
  const [gradingStats, setGradingStats] = useState<Record<string, GradingGroupStats>>({})
  const [teachers, setTeachers] = useState<Teacher[]>([])
  const [rooms, setRooms] = useState<Room[]>([])
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
  /** Arxivlangan guruhlar ro'yxati (holat filtri "Arxiv" bo'lganda ko'rsatiladi) */
  const [archived, setArchived] = useState<Group[]>([])
  /** A'zolarni boshqarish modali uchun tanlangan guruh */
  const [membersOf, setMembersOf] = useState<Group | null>(null)
  const [deletingGroup, setDeletingGroup] = useState<Group | null>(null)
  /** Guruh to'ldirish (sig'im/bo'sh o'rin + muzlatilganlar soni) — jamlanma qatori ham shundan. */
  const [fill, setFill] = useState<GroupFillRow[]>([])
  const [showFill, setShowFill] = useState(false)
  /** "Jurnal boshqaruvi" oynasi — jurnal tahrirlash siyosati (barcha guruhlar uchun) */
  const [policyOpen, setPolicyOpen] = useState(false)
  /** "Vaqtincha bloklash" oynasi — guruh o'qituvchida ko'rinmay qoladi (izoh ixtiyoriy) */
  const [blockTarget, setBlockTarget] = useState<Group | null>(null)
  const [blockNote, setBlockNote] = useState('')
  const [subjects, setSubjects] = useState<Subject[]>([])
  /** Bugun darsi bor, davomati olinmagan guruhlar — sariq qator (edutizim `#FFFF04`). */
  const [notAttended, setNotAttended] = useState<Set<string>>(new Set())

  // ---- Filtrlar (edutizimdagidek DOIM ochiq; tanlov sessiyada saqlanadi) ----
  const [filtersOpen, setFiltersOpen] = usePersistentState('classes.filtersOpen', true)
  const [q, setQ] = usePersistentState('classes.f.q', '')
  const [state, setState] = usePersistentState<'active' | 'archived'>('classes.f.state', 'active')
  const [teacherFilter, setTeacherFilter] = usePersistentState('classes.f.teacher', '')
  const [courseFilter, setCourseFilter] = usePersistentState('classes.f.course', '')
  const [levelFilter, setLevelFilter] = usePersistentState('classes.f.level', '')
  const [roomFilter, setRoomFilter] = usePersistentState('classes.f.room', '')
  const [weekdayFilter, setWeekdayFilter] = usePersistentState('classes.f.weekday', '')
  const [dayFilter, setDayFilter] = usePersistentState<'' | 'odd' | 'even'>('classes.f.dayKind', '')
  const [timeFilter, setTimeFilter] = usePersistentState('classes.f.time', '')
  /** Reyting bo'yicha saralash (ilgari "Saralash: Tartib / O'rtacha baho / Davomat" chiplari). */
  const [sortKey, setSortKey] = usePersistentState<SortKey>('classes.sortKey', '')
  const [sortDir, setSortDir] = usePersistentState<'asc' | 'desc'>('classes.sortDir', 'desc')

  const showArchived = state === 'archived'

  useEffect(() => {
    Promise.all([
      getClasses(),
      getClassesStats(),
      getArchivedClasses(),
      getTeachers(),
      getAllGroupsGradingStats(),
      getSubjects(),
    ])
      .then(([cl, st, ar, te, gs, su]) => {
        setClasses(cl)
        setStats(st)
        setArchived(ar)
        setTeachers(te)
        setGradingStats(gs)
        setSubjects(su)
      })
      .finally(() => setLoading(false))
    // Ikkinchi darajali ma'lumotlar — xato bo'lsa ro'yxat baribir ishlaydi.
    getGroupFill().then(setFill).catch(() => {})
    getRooms().then(setRooms).catch(() => {})
    getTodayLessons()
      .then((t) => setNotAttended(notAttendedGroupIds(t.lessons)))
      .catch(() => {})
  }, [])

  const teacherById = useMemo(() => new Map(teachers.map((t) => [t.id, t])), [teachers])
  const subjectById = useMemo(() => new Map(subjects.map((s) => [s.id, s])), [subjects])
  const fillById = useMemo(() => new Map(fill.map((f) => [f.groupId, f])), [fill])
  const teacherName = (id?: string) => (id ? (teacherById.get(id)?.fullName ?? '—') : '—')
  const courseName = (id?: string) => (id ? (subjectById.get(id)?.name ?? '') : '')

  const source = showArchived ? archived : classes
  const hasLevels = useMemo(() => classes.some((c) => c.grade > 0), [classes])

  const filtered = useMemo(() => {
    const s = q.trim().toLowerCase()
    return source.filter((c) => {
      if (teacherFilter && c.teacherId !== teacherFilter) return false
      if (courseFilter && c.courseId !== courseFilter) return false
      if (levelFilter && String(c.grade) !== levelFilter) return false
      if (roomFilter && (c.roomId || `name:${c.room ?? ''}`) !== roomFilter) return false
      if (weekdayFilter && !(c.days ?? []).includes(Number(weekdayFilter))) return false
      if (dayFilter && dayKind(c.days) !== dayFilter) return false
      if (timeFilter && !lessonCovers(c.startTime, c.endTime, timeFilter)) return false
      if (s) {
        const hay = `${c.name} ${teacherName(c.teacherId)} ${courseName(c.courseId)} ${c.room ?? ''}`.toLowerCase()
        if (!hay.includes(s)) return false
      }
      return true
    })
    // eslint-disable-next-line react-hooks/exhaustive-deps -- nom yordamchilari xaritalardan
  }, [source, q, teacherFilter, courseFilter, levelFilter, roomFilter, weekdayFilter, dayFilter, timeFilter, teacherById, subjectById])

  const sorted = useMemo(() => {
    if (!sortKey) return filtered
    const dir = sortDir === 'asc' ? 1 : -1
    const val = (c: Group): number | string => {
      const st = stats[c.id]
      switch (sortKey) {
        case 'name':
          return c.name.toLowerCase()
        case 'students':
          return st?.studentsCount ?? -1
        case 'avg':
          return st?.averageGrade ?? -1
        case 'att':
          return st?.attendance ?? -1
      }
    }
    return [...filtered].sort((a, b) => {
      const va = val(a)
      const vb = val(b)
      if (typeof va === 'string' && typeof vb === 'string') return va.localeCompare(vb, 'uz') * dir
      return ((va as number) - (vb as number)) * dir
    })
  }, [filtered, sortKey, sortDir, stats])

  const pg = usePagination(sorted)

  const onSort = (key: string) => {
    const k = key as SortKey
    if (sortKey === k) {
      // Uchinchi bosishda saralash bekor — server tartibi (daraja, nom) qaytadi.
      if (sortDir === 'desc') setSortDir('asc')
      else {
        setSortKey('')
        setSortDir('desc')
      }
    } else {
      setSortKey(k)
      setSortDir(k === 'name' ? 'asc' : 'desc')
    }
  }

  // Jamlanma: O'QUVCHILAR ustuni yig'indisi va ulardan muzlatilganlar (faqat faol guruhlarda).
  const totals = useMemo(() => {
    let students = 0
    let frozen = 0
    for (const c of filtered) {
      students += stats[c.id]?.studentsCount ?? 0
      frozen += fillById.get(c.id)?.frozen ?? 0
    }
    return { students, frozen }
  }, [filtered, stats, fillById])

  const filtersActive =
    !!q || !!teacherFilter || !!courseFilter || !!levelFilter || !!roomFilter || !!weekdayFilter || !!dayFilter || !!timeFilter
  const clearFilters = () => {
    setQ('')
    setTeacherFilter('')
    setCourseFilter('')
    setLevelFilter('')
    setRoomFilter('')
    setWeekdayFilter('')
    setDayFilter('')
    setTimeFilter('')
  }

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

  /** Joriy (filtrlangan) ro'yxatni CSV'ga — ustunlar jadvaldagidek. */
  const exportList = () => {
    const today = new Date().toISOString().slice(0, 10)
    exportToCsv(
      `guruhlar-${showArchived ? 'arxiv-' : ''}${today}.csv`,
      ['№', 'Guruh nomi', 'Kurs', 'Darajasi', 'Kun', 'Dars vaqti', 'Guruh vaqti', "O'quvchilar", "O'qituvchi", 'Xona'],
      sorted.map((c, i) => [
        String(i + 1),
        c.name,
        courseName(c.courseId),
        c.grade > 0 ? String(c.grade) : '',
        formatGroupDays(c.days),
        formatLessonTime(c.startTime, c.endTime),
        formatGroupPeriod(c.startDate, c.endDate),
        String(stats[c.id]?.studentsCount ?? ''),
        teacherName(c.teacherId),
        c.room ?? '',
      ]),
    )
  }

  const canCreate = can('classes.list', 'create')
  const canEdit = can('classes.list', 'edit')
  const canDelete = can('classes.list', 'delete')

  const columns: DataColumn<Group>[] = [
    {
      key: 'name',
      header: 'Guruh nomi',
      sortable: true,
      width: 240,
      render: (c) => (
        <span className="inline-flex max-w-[260px] items-center gap-1.5">
          <Link
            to={`/admin/classes/${c.id}`}
            className="truncate text-black no-underline hover:text-brand-600 hover:underline"
            title={c.name}
          >
            {c.name}
          </Link>
          {c.isBlocked && (
            <span
              className="inline-flex shrink-0 items-center gap-0.5 rounded-full bg-amber-100 px-1.5 py-0.5 text-[10px] font-semibold text-amber-700"
              title={c.blockNote || "Vaqtincha bloklangan — o'qituvchida ko'rinmaydi"}
            >
              <Lock className="h-3 w-3" /> Bloklangan
            </span>
          )}
        </span>
      ),
    },
    { key: 'course', header: 'Kurs', render: (c) => courseName(c.courseId) || '—' },
    { key: 'level', header: 'Darajasi', render: (c) => (c.grade > 0 ? c.grade : '') },
    { key: 'days', header: 'Kun', render: (c) => <span className="whitespace-nowrap">{formatGroupDays(c.days)}</span> },
    {
      key: 'time',
      header: 'Dars vaqti',
      render: (c) => <span className="whitespace-nowrap">{formatLessonTime(c.startTime, c.endTime)}</span>,
    },
    {
      key: 'period',
      header: 'Guruh vaqti',
      render: (c) => {
        const p = formatGroupPeriod(c.startDate, c.endDate)
        return p ? (
          <span className="whitespace-nowrap rounded-full bg-[#ebebeb] px-2.5 py-1 text-[12px] font-medium">{p}</span>
        ) : (
          '—'
        )
      },
    },
    {
      key: 'students',
      header: "O'quvchilar",
      sortable: true,
      render: (c) => stats[c.id]?.studentsCount ?? '—',
    },
    {
      key: 'teacher',
      header: "O'qituvchi",
      render: (c) =>
        c.teacherId ? (
          <Link
            to={`/admin/teachers/${c.teacherId}`}
            className="whitespace-nowrap text-black no-underline hover:text-brand-600 hover:underline"
          >
            {teacherName(c.teacherId)}
          </Link>
        ) : (
          '—'
        ),
    },
    { key: 'room', header: 'Xona', render: (c) => <span className="whitespace-nowrap">{c.room || '—'}</span> },
    // ---- Bizning qo'shimcha ustunlar (edutizimda yo'q) — edutizim ustunlaridan KEYIN ----
    ...(showArchived
      ? [
          {
            key: 'archivedAt',
            header: 'Arxiv sanasi',
            render: (c: Group) => (c.archivedAt ? formatDate(c.archivedAt) : '—'),
          },
        ]
      : [
          {
            key: 'avg',
            header: "O'rtacha",
            sortable: true,
            align: 'right' as const,
            render: (c: Group) => {
              const st = stats[c.id]
              return <span className={cn('font-semibold', st && gradeTextCls(st.averageGrade))}>{st ? st.averageGrade.toFixed(1) : '—'}</span>
            },
          },
          {
            key: 'att',
            header: 'Davomat',
            sortable: true,
            align: 'right' as const,
            render: (c: Group) => {
              const st = stats[c.id]
              return st && st.attendance != null ? (
                <span className={cn('font-semibold', attColor(st.attendance))}>{st.attendance}%</span>
              ) : (
                '—'
              )
            },
          },
          {
            key: 'grading',
            header: 'Baholash',
            align: 'right' as const,
            render: (c: Group) => gradingStats[c.id]?.totalGrades ?? '—',
          },
          {
            key: 'fee',
            header: "Oylik to'lov",
            align: 'right' as const,
            render: (c: Group) => <span className="whitespace-nowrap">{formatMoney(c.monthlyFee)}</span>,
          },
        ]),
    {
      key: 'actions',
      header: '',
      width: 48,
      align: 'center',
      render: (c) => (
        <RowActionBar
          actions={
            showArchived
              ? [
                  { label: "Ko'rish", icon: IconEye, onClick: () => navigate(`/admin/classes/${c.id}`) },
                  {
                    label: 'Arxivdan chiqarish',
                    icon: IconArchiveOff,
                    hidden: !canDelete,
                    onClick: () => handleUnarchive(c),
                  },
                  { label: "O'chirish", icon: IconTrash, danger: true, hidden: !canDelete, onClick: () => setDeletingGroup(c) },
                ]
              : [
                  { label: "A'zolar", icon: IconUsers, onClick: () => setMembersOf(c) },
                  {
                    label: 'Tahrirlash',
                    icon: IconPencil,
                    hidden: !canEdit,
                    onClick: () => {
                      setEditing(c)
                      setFormOpen(true)
                    },
                  },
                  // Vaqtincha bloklash — guruh o'qituvchi ilovasida ko'rinmay qoladi
                  // (arxivlash EMAS: o'quvchi/a'zolik/hisob tegilmaydi).
                  c.isBlocked
                    ? {
                        label: "Blokdan chiqarish (o'qituvchida yana ko'rinadi)",
                        icon: IconLockOpen,
                        hidden: !canEdit,
                        onClick: () => handleUnblock(c),
                      }
                    : {
                        label: "Vaqtincha bloklash (o'qituvchida ko'rinmaydi)",
                        icon: IconLock,
                        hidden: !canEdit,
                        onClick: () => {
                          setBlockNote('')
                          setBlockTarget(c)
                        },
                      },
                  {
                    label: "Arxivlash (a'zoliklar muzlatiladi)",
                    icon: IconArchive,
                    hidden: !canDelete,
                    onClick: () => handleArchive(c),
                  },
                  { label: "O'chirish", icon: IconTrash, danger: true, hidden: !canDelete, onClick: () => setDeletingGroup(c) },
                ]
          }
        />
      ),
    },
  ]

  const teacherOptions = useMemo(
    () =>
      teachers
        .filter((t) => source.some((c) => c.teacherId === t.id))
        .sort((a, b) => a.fullName.localeCompare(b.fullName)),
    [teachers, source],
  )
  const courseOptions = useMemo(
    () =>
      subjects
        .filter((s) => source.some((c) => c.courseId === s.id))
        .sort((a, b) => a.name.localeCompare(b.name)),
    [subjects, source],
  )
  const levelOptions = useMemo(
    () => [...new Set(source.map((c) => c.grade).filter((g) => g > 0))].sort((a, b) => a - b),
    [source],
  )
  /** Xona: FK bo'yicha (roomId), eski matnli `room` — nomi bo'yicha zaxira kalit. */
  const roomOptions = useMemo(() => {
    const map = new Map<string, string>()
    for (const r of rooms) if (source.some((c) => c.roomId === r.id)) map.set(r.id, r.name)
    for (const c of source) if (!c.roomId && c.room) map.set(`name:${c.room}`, c.room)
    return [...map.entries()].sort((a, b) => a[1].localeCompare(b[1], 'uz'))
  }, [rooms, source])

  return (
    <div>
      <ListToolbar
        left={
          !showArchived && canCreate ? (
            <Button
              onClick={() => {
                setEditing(null)
                setFormOpen(true)
              }}
            >
              <IconPlus className="h-5 w-5" /> Qo'shish
            </Button>
          ) : null
        }
        filtersOpen={filtersOpen}
        onToggleFilters={() => setFiltersOpen((v) => !v)}
        extra={
          <MoreMenu
            items={[
              {
                label: showFill ? "Guruh to'ldirishni yopish" : "Guruh to'ldirish",
                icon: IconLayoutGrid,
                hidden: showArchived,
                onClick: () => setShowFill((v) => !v),
              },
              { label: 'Jurnal boshqaruvi', icon: IconBook2, onClick: () => setPolicyOpen(true) },
              { label: 'Export (CSV)', icon: IconFileExport, onClick: exportList },
              ...(filtersActive ? [{ label: 'Filtrni tozalash', icon: IconFilterOff, onClick: clearFilters }] : []),
            ]}
          />
        }
      />

      {filtersOpen && (
        <FilterGrid>
          <FilterInput search placeholder="Qidiruv" value={q} onChange={(e) => setQ(e.target.value)} />
          <FilterSelect
            placeholder="Holati"
            value={state}
            onChange={(e) => setState((e.target.value || 'active') as 'active' | 'archived')}
          >
            <option value="active">Aktiv</option>
            <option value="archived">Arxiv ({archived.length})</option>
          </FilterSelect>
          <FilterSelect placeholder="O'qituvchi" value={teacherFilter} onChange={(e) => setTeacherFilter(e.target.value)}>
            {teacherOptions.map((t) => (
              <option key={t.id} value={t.id}>
                {t.fullName}
              </option>
            ))}
          </FilterSelect>
          <FilterSelect placeholder="Kurs" value={courseFilter} onChange={(e) => setCourseFilter(e.target.value)}>
            {courseOptions.map((s) => (
              <option key={s.id} value={s.id}>
                {s.name}
              </option>
            ))}
          </FilterSelect>
          {hasLevels && (
            <FilterSelect placeholder="Daraja" value={levelFilter} onChange={(e) => setLevelFilter(e.target.value)}>
              {levelOptions.map((g) => (
                <option key={g} value={String(g)}>
                  {g}
                </option>
              ))}
            </FilterSelect>
          )}
          <FilterSelect placeholder="Xona" value={roomFilter} onChange={(e) => setRoomFilter(e.target.value)}>
            {roomOptions.map(([k, name]) => (
              <option key={k} value={k}>
                {name}
              </option>
            ))}
          </FilterSelect>
          <FilterSelect placeholder="Kun" value={weekdayFilter} onChange={(e) => setWeekdayFilter(e.target.value)}>
            {WEEKDAYS.map((d, i) => (
              <option key={d} value={String(i)}>
                {d}
              </option>
            ))}
          </FilterSelect>
          <FilterSelect
            placeholder="Juft/toq kunlar"
            value={dayFilter}
            onChange={(e) => setDayFilter(e.target.value as '' | 'odd' | 'even')}
          >
            <option value="odd">Toq kunlar</option>
            <option value="even">Juft kunlar</option>
          </FilterSelect>
          <FilterInput
            type="time"
            aria-label="Dars vaqti"
            title="Dars vaqti — shu paytda darsi bo'lgan guruhlar"
            value={timeFilter}
            onChange={(e) => setTimeFilter(e.target.value)}
          />
        </FilterGrid>
      )}

      <div className="mb-2 flex flex-wrap items-end justify-between gap-2">
        {!showArchived ? (
          <p className="text-[13px] text-[#6b7280]">
            Jami o'quvchilar soni: <b className="font-bold text-[#333]">{totals.students}</b>
            <span className="ml-3">
              Muzlatilgan o'quvchilar soni : <b className="font-bold text-[#333]">{totals.frozen}</b>
            </span>
          </p>
        ) : (
          <p className="text-[13px] text-[#6b7280]">Arxivlangan guruhlar — faqat ko'rish, arxivdan chiqarish va o'chirish.</p>
        )}
        <TotalPill total={filtered.length} />
      </div>

      <DataTable
        rows={pg.paged}
        columns={columns}
        rowKey={(c) => c.id}
        loading={loading}
        numbered
        offset={(pg.page - 1) * pg.pageSize}
        sortKey={sortKey || undefined}
        sortDir={sortDir}
        onSort={onSort}
        // Bugun darsi bor, davomati olinmagan guruh — edutizimdagidek sariq (hover ham o'zgartirmaydi).
        rowClassName={(c) => (!showArchived && notAttended.has(c.id) ? '!bg-[#ffff04]' : undefined)}
        footer={<TablePagination {...pg} />}
      />
      {!showArchived && notAttended.size > 0 && (
        <p className="mt-2 flex items-center gap-1.5 text-[12px] text-[#6b7280]">
          <span className="inline-block h-3 w-3 rounded-sm border border-black/10 bg-[#ffff04]" />
          Bugun darsi bor, lekin davomat hali qilinmagan guruhlar
        </p>
      )}

      {showFill && !showArchived && (
        <div className="mt-5">
          <h2 className="mb-2 text-[16px] font-semibold text-black">Guruh to'ldirish</h2>
          <DataTable
            rows={fill}
            rowKey={(r) => r.groupId}
            numbered
            columns={[
              { key: 'name', header: 'Guruh', render: (r) => r.name },
              { key: 'grade', header: 'Daraja', render: (r) => (r.grade > 0 ? r.grade : '') },
              { key: 'enrolled', header: "O'quvchilar", render: (r) => r.enrolled },
              { key: 'frozen', header: 'Muzlatilgan', render: (r) => r.frozen ?? 0 },
              { key: 'cap', header: "Sig'im", render: (r) => (r.capacity === 0 ? 'cheksiz' : r.capacity) },
              {
                key: 'free',
                header: "Bo'sh o'rin",
                render: (r) => (
                  <span
                    className={cn(
                      'font-semibold',
                      r.capacity > 0 && r.freeSeats === 0 ? 'text-red-600' : 'text-emerald-600',
                    )}
                  >
                    {r.capacity === 0 ? '—' : r.freeSeats}
                  </span>
                ),
              },
              {
                key: 'status',
                header: 'Holat',
                render: (r) => (
                  <span className={cn('rounded-md px-2 py-0.5 text-xs font-medium', fillStatusBadge(r.status))}>
                    {fillStatusLabel(r.status)}
                  </span>
                ),
              },
            ]}
          />
        </div>
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

function fillStatusLabel(s: GroupFillRow['status']): string {
  return s === 'full' ? "To'lgan" : s === 'archived' ? 'Arxiv' : 'Faol'
}

function fillStatusBadge(s: GroupFillRow['status']): string {
  return s === 'full'
    ? 'bg-red-50 text-red-700'
    : s === 'archived'
      ? 'bg-slate-100 text-slate-500'
      : 'bg-emerald-50 text-emerald-700'
}
