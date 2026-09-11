import { useEffect, useState, useMemo } from 'react'
import { Link } from 'react-router-dom'
import {
  Plus,
  Search,
  Users,
  Calendar,
  UserCheck,
  XCircle,
  CheckCircle2,
  UserX,
  ArrowRight,
  AlertTriangle,
  Clock,
} from 'lucide-react'
import type {
  Group,
  Teacher,
  SubstituteTeacherAssignment,
  SubstitutePreview,
  GroupLessonDate,
} from '@/types'
import {
  getSubstituteAssignments,
  createSubstituteAssignment,
  cancelSubstituteAssignment,
  getGroupLessonDates,
  getSubstitutePreview,
} from '@/api/services/substituteTeachers'
import { getTeachers } from '@/api/services/teachers'
import { getClasses } from '@/api/services/classes'
import { usePerm } from '@/lib/permissions'
import { cn, formatMoney, apiErrorMessage } from '@/lib/utils'
import { currentMonth, todayIso } from '@/lib/month'
import { Card } from '@/components/ui/Card'
import { Button } from '@/components/ui/Button'
import { Badge } from '@/components/ui/Badge'
import { PageHeader } from '@/components/ui/PageHeader'
import { CardTabs } from '@/components/ui/CardTabs'
import { teacherTabs } from '@/config/sectionTabs'
import { StatCard } from '@/components/ui/StatCard'
import { Loader } from '@/components/ui/Loader'
import { Modal } from '@/components/ui/Modal'
import { Input, Textarea } from '@/components/ui/Input'
import { TablePagination, usePagination } from '@/components/ui/TablePagination'

const control =
  'rounded-lg border border-slate-200 bg-white px-3 py-2 text-sm text-slate-700 outline-none transition-colors focus:border-brand-400 focus:ring-2 focus:ring-brand-100'

/**
 * Tayinlovning OXIRGI dars kuni.
 *
 * ⚠️ `isActive` faqat "bekor qilinmagan" degani, "hozir amalda" degani EMAS — o'tgan yilgi
 * tugagan tayinlov ham `isActive=true` bo'lib qolaveradi. Shuning uchun "faol" savoli SANA
 * bo'yicha hal qilinadi.
 */
function lastDateOf(a: SubstituteTeacherAssignment): string {
  if (a.dates && a.dates.length > 0) {
    return a.dates.reduce((max, d) => (d > max ? d : max), a.dates[0])
  }
  return a.endDate || a.date
}

/** Qatordagi holat: bekor qilingan | muddati o'tgan | faol. */
type RowStatus = 'cancelled' | 'expired' | 'active'

function rowStatusOf(a: SubstituteTeacherAssignment, today: string): RowStatus {
  if (!a.isActive) return 'cancelled'
  return lastDateOf(a) < today ? 'expired' : 'active'
}

export function SubstituteTeachersPage() {
  const { can } = usePerm()
  // Ruxsat DARVOZASI: ilgari tugmalar hammaga ko'rinardi va xodim formani to'liq to'ldirib
  // bo'lgach oxirida bo'sh 403 olardi. Endi TeachersPage bilan bir xil kalitlar.
  const canCreate = can('teachers.substitutions', 'create')
  const canEdit = can('teachers.substitutions', 'edit')

  // Bugungi sana — bir marta hisoblanadi. `toISOString()` UTC beradi (Toshkent UTC+5, ya'ni
  // 1-sentabr ertalab "avgust" chiqib qolardi), shuning uchun `lib/month.ts` yagona manba.
  const today = useMemo(() => todayIso(), [])

  const [assignments, setAssignments] = useState<SubstituteTeacherAssignment[]>([])
  const [totalCount, setTotalCount] = useState(0)
  const [teachers, setTeachers] = useState<Teacher[]>([])
  const [classes, setClasses] = useState<Group[]>([])
  const [loading, setLoading] = useState(true)
  /** Ro'yxat yuklanmadi — BO'SH HOLAT o'rniga shu ko'rsatiladi (aks holda 500/403 da
   *  "hali birorta ham biriktirilmagan" degan YOLG'ON xabar chiqardi). */
  const [loadError, setLoadError] = useState('')
  const [refsError, setRefsError] = useState('')
  /** Yaratish/bekor qilishdan keyin ro'yxatni qayta yuklash uchun. */
  const [reloadTick, setReloadTick] = useState(0)

  // Filters
  const [search, setSearch] = useState('')
  const [groupIdFilter, setGroupIdFilter] = useState('')
  const [teacherIdFilter, setTeacherIdFilter] = useState('')
  const [statusFilter, setStatusFilter] = useState<'active' | 'cancelled' | 'all'>('all')

  // Modal
  const [isModalOpen, setIsModalOpen] = useState(false)
  const [submitting, setSubmitting] = useState(false)
  const [errorMsg, setErrorMsg] = useState('')

  // Form State
  const [formGroupId, setFormGroupId] = useState('')
  const [formSubstituteTeacherId, setFormSubstituteTeacherId] = useState('')
  const [formMonth, setFormMonth] = useState(() => currentMonth())
  const [lessonDates, setLessonDates] = useState<GroupLessonDate[]>([])
  const [selectedDates, setSelectedDates] = useState<string[]>([])
  const [loadingLessonDates, setLoadingLessonDates] = useState(false)
  const [formReason, setFormReason] = useState('')

  // Jonli hisob-kitob (server) — formula KLIENTDA takrorlanmaydi
  const [preview, setPreview] = useState<SubstitutePreview | null>(null)
  const [previewLoading, setPreviewLoading] = useState(false)
  const [previewError, setPreviewError] = useState('')

  // Cancel Modal state
  const [cancelTargetId, setCancelTargetId] = useState<string | null>(null)
  const [cancelling, setCancelling] = useState(false)
  const [cancelError, setCancelError] = useState('')

  // ── Ma'lumotnomalar (o'qituvchilar + guruhlar) — bir marta yuklanadi ───────────────────
  useEffect(() => {
    let active = true
    Promise.allSettled([getTeachers(), getClasses()]).then(([teachRes, clsRes]) => {
      if (!active) return
      if (teachRes.status === 'fulfilled') setTeachers(teachRes.value)
      if (clsRes.status === 'fulfilled') setClasses(clsRes.value)
      const failed = teachRes.status === 'rejected' || clsRes.status === 'rejected'
      setRefsError(
        failed
          ? "O'qituvchilar/guruhlar ro'yxati to'liq yuklanmadi — tanlov ro'yxatlari kam ko'rinishi mumkin."
          : '',
      )
    })
    return () => {
      active = false
    }
  }, [])

  // ── Tayinlovlar ro'yxati — filtrlar SERVERDA qo'llanadi ───────────────────────────────
  // Poyga himoyasi: filtr tez almashsa eski javob yangisini bosib ketmasin.
  useEffect(() => {
    let active = true
    // eslint-disable-next-line react-hooks/set-state-in-effect -- filtr o'zgarganda yuklanish holatini yoqish (maqsadli)
    setLoading(true)
    getSubstituteAssignments({
      groupId: groupIdFilter || undefined,
      teacherId: teacherIdFilter || undefined,
      isActive: statusFilter === 'all' ? undefined : statusFilter === 'active',
    })
      .then((res) => {
        if (!active) return
        setAssignments(res.items)
        setTotalCount(res.total)
        setLoadError('')
      })
      .catch((err: unknown) => {
        if (!active) return
        setAssignments([])
        setTotalCount(0)
        setLoadError(
          apiErrorMessage(err, "Ma'lumot yuklanmadi — ruxsat yoki tarmoq muammosi bo'lishi mumkin."),
        )
      })
      .finally(() => {
        if (active) setLoading(false)
      })
    return () => {
      active = false
    }
  }, [groupIdFilter, teacherIdFilter, statusFilter, reloadTick])

  // ── Modal: guruh/oy tanlanganda dars kunlari ──────────────────────────────────────────
  useEffect(() => {
    if (!formGroupId || !formMonth) {
      // eslint-disable-next-line react-hooks/set-state-in-effect -- guruh/oy tanlanmagan bo'lsa sanalarni tozalash (maqsadli)
      setLessonDates([])
      setSelectedDates([])
      return
    }
    let active = true
    setLoadingLessonDates(true)
    getGroupLessonDates(formGroupId, formMonth)
      .then((res) => {
        if (active) {
          setLessonDates(res)
          // ⚠️ ATAYIN BO'SH: ilgari oyning BARCHA darslari avtomatik belgilanardi — bitta
          // bosish bilan butun oy o'rinbosarga yozilib, asosiy o'qituvchi oylikdan ayrilardi
          // (o'tgan kunlar ham). Endi admin kunlarni O'ZI tanlaydi.
          setSelectedDates([])
        }
      })
      .catch(() => {
        if (active) {
          setLessonDates([])
          setSelectedDates([])
        }
      })
      .finally(() => {
        if (active) setLoadingLessonDates(false)
      })
    return () => {
      active = false
    }
  }, [formGroupId, formMonth])

  // ── Modal: JONLI hisob-kitob (debounce 350ms) ─────────────────────────────────────────
  // Summani KLIENT hisoblamaydi — chip bosilgan sari serverdan so'raladi.
  useEffect(() => {
    if (!formGroupId || !formSubstituteTeacherId || selectedDates.length === 0) {
      // eslint-disable-next-line react-hooks/set-state-in-effect -- shartlar buzilganda hisobni tozalash (maqsadli)
      setPreview(null)
      setPreviewError('')
      setPreviewLoading(false)
      return
    }
    let active = true
    setPreviewLoading(true)
    const timer = setTimeout(() => {
      getSubstitutePreview({
        groupId: formGroupId,
        substituteTeacherId: formSubstituteTeacherId,
        dates: selectedDates,
      })
        .then((res) => {
          if (!active) return
          setPreview(res)
          setPreviewError('')
        })
        .catch((err: unknown) => {
          if (!active) return
          // Eski (noto'g'ri) raqamni ushlab turmaymiz — noaniq summa ko'rsatgandan
          // ko'ra hech narsa ko'rsatmagan xavfsizroq.
          setPreview(null)
          setPreviewError(apiErrorMessage(err, "Hisob-kitobni yuklab bo'lmadi"))
        })
        .finally(() => {
          if (active) setPreviewLoading(false)
        })
    }, 350)
    return () => {
      active = false
      clearTimeout(timer)
    }
  }, [formGroupId, formSubstituteTeacherId, selectedDates])

  const toggleDate = (dateStr: string) => {
    setSelectedDates((prev) =>
      prev.includes(dateStr) ? prev.filter((d) => d !== dateStr) : [...prev, dateStr],
    )
  }

  const selectAllDates = () => {
    setSelectedDates(lessonDates.map((d) => d.date))
  }

  const clearAllDates = () => {
    setSelectedDates([])
  }

  // Auto-derived original teacher for selected group in modal
  const selectedGroup = useMemo(
    () => classes.find((c) => c.id === formGroupId),
    [classes, formGroupId],
  )
  const originalTeacher = useMemo(
    () => teachers.find((t) => t.id === selectedGroup?.teacherId),
    [teachers, selectedGroup],
  )

  // Qidiruv KLIENTDA qoladi (server qidiruv parametrini qabul qilmaydi). Guruh/o'qituvchi/holat
  // filtrlari serverda qo'llanadi — bu yerdagi takror tekshiruv zaxira (eski server bilan ham
  // ro'yxat to'g'ri ko'rinsin).
  const filteredAssignments = useMemo(() => {
    return assignments.filter((a) => {
      if (statusFilter === 'active' && !a.isActive) return false
      if (statusFilter === 'cancelled' && a.isActive) return false
      if (groupIdFilter && a.groupId !== groupIdFilter) return false
      if (
        teacherIdFilter &&
        a.substituteTeacherId !== teacherIdFilter &&
        a.originalTeacherId !== teacherIdFilter
      )
        return false

      if (search.trim()) {
        const q = search.toLowerCase()
        const matchGroup = (a.groupName || '').toLowerCase().includes(q)
        const matchOriginal = (a.originalTeacherName || '').toLowerCase().includes(q)
        const matchSubstitute = (a.substituteTeacherName || '').toLowerCase().includes(q)
        const matchReason = (a.reason || '').toLowerCase().includes(q)
        if (!matchGroup && !matchOriginal && !matchSubstitute && !matchReason) return false
      }
      return true
    })
  }, [assignments, statusFilter, groupIdFilter, teacherIdFilter, search])

  const pagination = usePagination(filteredAssignments)

  const stats = useMemo(() => {
    // "Ayni vaqtda faol" = bekor qilinmagan VA oxirgi dars kuni hali o'tmagan.
    const live = assignments.filter((a) => rowStatusOf(a, today) === 'active')
    const expiredCount = assignments.filter((a) => rowStatusOf(a, today) === 'expired').length
    return {
      activeCount: live.length,
      expiredCount,
      uniqueSubstituteCount: new Set(live.map((a) => a.substituteTeacherId)).size,
    }
  }, [assignments, today])

  const handleOpenModal = () => {
    // ⚠️ Bu yerda `await loadData()` YO'Q: ilgari modal ochilishi butun sahifani
    // "yuklanmoqda" holatiga tushirardi. Ma'lumotnomalar sahifa ochilganda yuklangan.
    setFormGroupId('')
    setFormSubstituteTeacherId('')
    setFormMonth(currentMonth())
    setLessonDates([])
    setSelectedDates([])
    setFormReason('')
    setPreview(null)
    setPreviewError('')
    setErrorMsg('')
    setIsModalOpen(true)
  }

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault()
    if (!formGroupId) {
      setErrorMsg('Guruhni tanlang')
      return
    }
    if (!formSubstituteTeacherId) {
      setErrorMsg("O'rinbosar o'qituvchini tanlang")
      return
    }
    if (selectedDates.length === 0) {
      setErrorMsg('Kamida bitta dars kunini tanlang')
      return
    }

    setSubmitting(true)
    setErrorMsg('')
    try {
      await createSubstituteAssignment({
        groupId: formGroupId,
        substituteTeacherId: formSubstituteTeacherId,
        dates: selectedDates,
        reason: formReason,
      })
      setIsModalOpen(false)
      setReloadTick((t) => t + 1)
    } catch (err: unknown) {
      setErrorMsg(apiErrorMessage(err, 'Tayinlashda xatolik yuz berdi'))
    } finally {
      setSubmitting(false)
    }
  }

  const handleCancelAssignment = async () => {
    if (!cancelTargetId) return
    setCancelling(true)
    setCancelError('')
    try {
      await cancelSubstituteAssignment(cancelTargetId)
      setCancelTargetId(null)
      setReloadTick((t) => t + 1)
    } catch (err: unknown) {
      // Ilgari xato faqat `console.error` ga tushardi va modal jimgina ochiq qolardi.
      setCancelError(apiErrorMessage(err, 'Bekor qilishda xatolik yuz berdi'))
    } finally {
      setCancelling(false)
    }
  }

  return (
    <div className="space-y-6">
      <PageHeader
        title="O'qituvchilar"
        sub="Markaz o'qituvchilari va vaqtincha o'rinbosar biriktiruvlarini boshqarish"
        actions={
          canCreate ? (
            <Button onClick={handleOpenModal}>
              <Plus className="mr-2 h-4 w-4" /> O'rinbosar biriktirish
            </Button>
          ) : undefined
        }
      />

      <CardTabs items={teacherTabs((p) => can(p, 'view'))} />

      {refsError && (
        <Card className="border-amber-200 bg-amber-50 p-3 text-sm font-medium text-amber-700">
          {refsError}
        </Card>
      )}

      {/* Stats Cards */}
      <div className="grid grid-cols-1 gap-4 sm:grid-cols-3">
        <StatCard
          label="Ayni vaqtda faol"
          value={stats.activeCount}
          hint={
            stats.expiredCount > 0
              ? `Bugungi sanaga amal qiladi · muddati o'tgani ${stats.expiredCount} ta`
              : 'Bugungi sanaga amal qiladigan tayinlovlar'
          }
          icon={UserCheck}
          iconBg="bg-brand-50"
          iconColor="text-brand-600"
        />
        <StatCard
          label="Barcha biriktiruvlar"
          value={totalCount}
          hint="Joriy filtr bo'yicha jami (serverdagi son)"
          icon={Calendar}
          iconBg="bg-indigo-50"
          iconColor="text-indigo-600"
        />
        <StatCard
          label="O'rinbosar o'qituvchilar"
          value={stats.uniqueSubstituteCount}
          hint="Ayni vaqtda almashtirayotgan o'qituvchilar"
          icon={Users}
          iconBg="bg-emerald-50"
          iconColor="text-emerald-600"
        />
      </div>

      {/* Filter and Search Bar */}
      <Card className="p-4">
        <div className="flex flex-wrap items-center gap-3">
          <div className="relative min-w-[200px] flex-1">
            <label htmlFor="sub-search" className="sr-only">
              Guruh, o'qituvchi yoki sabab bo'yicha qidiruv
            </label>
            <Search className="absolute left-3 top-2.5 h-4 w-4 text-slate-400" />
            <input
              id="sub-search"
              type="text"
              placeholder="Guruh, o'qituvchi yoki sabab bo'yicha qidiruv..."
              value={search}
              onChange={(e) => setSearch(e.target.value)}
              className={cn(control, 'w-full pl-9')}
            />
          </div>

          <select
            aria-label="Guruh bo'yicha filtr"
            value={groupIdFilter}
            onChange={(e) => setGroupIdFilter(e.target.value)}
            className={control}
          >
            <option value="">Barcha guruhlar</option>
            {classes.map((c) => (
              <option key={c.id} value={c.id}>
                {c.name}
              </option>
            ))}
          </select>

          <select
            aria-label="O'qituvchi bo'yicha filtr"
            value={teacherIdFilter}
            onChange={(e) => setTeacherIdFilter(e.target.value)}
            className={control}
          >
            <option value="">Barcha o'qituvchilar</option>
            {teachers.map((t) => (
              <option key={t.id} value={t.id}>
                {t.fullName}
              </option>
            ))}
          </select>

          {/* Birinchi variant boshlang'ich qiymat bilan MOS bo'lishi shart — aks holda
              select'da "Faol" ko'rinib, ro'yxatda hammasi chiqardi. */}
          <select
            aria-label="Holat bo'yicha filtr"
            value={statusFilter}
            onChange={(e) => setStatusFilter(e.target.value as typeof statusFilter)}
            className={control}
          >
            <option value="all">Hammasi</option>
            <option value="active">Faol biriktiruvlar</option>
            <option value="cancelled">Bekor qilinganlar</option>
          </select>
        </div>
      </Card>

      {/* Assignments Table */}
      <Card className="overflow-hidden">
        {loading ? (
          <div className="flex h-48 items-center justify-center">
            <Loader />
          </div>
        ) : loadError ? (
          /* ⚠️ XATO ≠ BO'SH: aks holda admin "hech kim biriktirilmagan" deb o'ylab, mavjud
             tayinlovni QAYTA yaratib yuborardi. */
          <div className="p-12 text-center">
            <AlertTriangle className="mx-auto mb-2 h-12 w-12 text-red-300" />
            <p className="font-medium text-slate-700">Ma'lumot yuklanmadi</p>
            <p className="mt-1 text-sm text-red-600">{loadError}</p>
            <Button
              variant="secondary"
              className="mt-3"
              onClick={() => setReloadTick((t) => t + 1)}
            >
              Qayta urinish
            </Button>
          </div>
        ) : filteredAssignments.length === 0 ? (
          <div className="p-12 text-center text-slate-500">
            <UserX className="mx-auto mb-2 h-12 w-12 text-slate-300" />
            <p className="font-medium text-slate-700">O'rinbosarlik tayinlovlari topilmadi</p>
            <p className="mt-1 text-sm text-slate-500">
              {search || groupIdFilter || teacherIdFilter || statusFilter !== 'all'
                ? "Filtr bo'yicha natija chiqmadi"
                : "Hali birorta ham o'rinbosar o'qituvchi biriktirilmagan"}
            </p>
          </div>
        ) : (
          <div>
            <div className="overflow-x-auto">
              <table className="w-full text-left text-sm text-slate-600">
                <thead className="border-b border-slate-200 bg-slate-50 text-xs font-semibold uppercase text-slate-500">
                  <tr>
                    <th className="px-4 py-3">Guruh</th>
                    <th className="px-4 py-3">Asosiy o'qituvchi</th>
                    <th className="px-4 py-3">O'rinbosar o'qituvchi</th>
                    <th className="px-4 py-3">Sana</th>
                    <th className="px-4 py-3 text-center">Darslar</th>
                    <th className="px-4 py-3">Hisoblangan haq</th>
                    <th className="px-4 py-3">Sababi</th>
                    <th className="px-4 py-3">Biriktirgan admin</th>
                    <th className="px-4 py-3">Holati</th>
                    <th className="px-4 py-3 text-right">Amallar</th>
                  </tr>
                </thead>
                <tbody className="divide-y divide-slate-100">
                  {pagination.paged.map((item) => {
                    const status = rowStatusOf(item, today)
                    return (
                      <tr key={item.id} className="transition-colors hover:bg-slate-50/50">
                        <td className="px-4 py-3 font-semibold text-slate-800">
                          <Link
                            to={`/admin/classes/${item.groupId}`}
                            className="hover:text-brand-600 hover:underline"
                          >
                            {item.groupName}
                          </Link>
                        </td>
                        <td className="px-4 py-3">
                          <Link
                            to={`/admin/teachers/${item.originalTeacherId}`}
                            className="font-medium text-slate-700 hover:text-brand-600 hover:underline"
                          >
                            {item.originalTeacherName}
                          </Link>
                        </td>
                        <td className="px-4 py-3">
                          <Link
                            to={`/admin/teachers/${item.substituteTeacherId}`}
                            className="flex items-center gap-1.5 font-semibold text-brand-700 hover:underline"
                          >
                            <ArrowRight className="h-3.5 w-3.5 text-brand-500" />
                            <span>{item.substituteTeacherName}</span>
                          </Link>
                        </td>
                        <td
                          className="max-w-[180px] truncate px-4 py-3 font-mono text-xs"
                          title={item.dates?.join(', ') || item.date}
                        >
                          {item.dates && item.dates.length > 0 ? (
                            <span>{item.dates.join(', ')}</span>
                          ) : item.endDate ? (
                            <span>
                              {item.date} ~ {item.endDate}
                            </span>
                          ) : (
                            <span>{item.date}</span>
                          )}
                        </td>
                        {/* `??` — server 0 qaytarsa "1 ta dars" deb yolg'on yozilmasin */}
                        <td className="px-4 py-3 text-center font-semibold text-slate-700">
                          {item.lessonCount ?? 1} ta dars
                        </td>
                        <td className="px-4 py-3 font-semibold text-emerald-600">
                          {formatMoney(item.estimatedSalary ?? 0)}
                        </td>
                        <td
                          className="max-w-[200px] truncate px-4 py-3 text-slate-600"
                          title={item.reason}
                        >
                          {item.reason || '-'}
                        </td>
                        <td className="px-4 py-3 text-xs text-slate-500">{item.createdBy}</td>
                        <td className="px-4 py-3">
                          {status === 'cancelled' ? (
                            <Badge tone="default">
                              <XCircle className="h-3 w-3 text-slate-400" /> Bekor qilingan
                            </Badge>
                          ) : status === 'expired' ? (
                            <Badge tone="amber">
                              <Clock className="h-3 w-3" /> Muddati o'tgan
                            </Badge>
                          ) : (
                            <Badge tone="green">
                              <CheckCircle2 className="h-3 w-3" /> Faol
                            </Badge>
                          )}
                        </td>
                        <td className="px-4 py-3 text-right">
                          {item.isActive && canEdit && (
                            <Button
                              variant="secondary"
                              onClick={() => {
                                setCancelError('')
                                setCancelTargetId(item.id)
                              }}
                              className="px-2.5 py-1 text-xs text-red-600 hover:bg-red-50 hover:text-red-700"
                            >
                              Bekor qilish
                            </Button>
                          )}
                        </td>
                      </tr>
                    )
                  })}
                </tbody>
              </table>
            </div>

            {/* CHEKLOV YASHIRILMAYDI: server ko'pi bilan 500 ta yozuv qaytaradi. */}
            {totalCount > assignments.length && (
              <p className="border-t border-slate-100 px-4 pt-3 text-xs text-amber-700">
                Jami {totalCount} ta biriktiruv bor, bu yerda eng yangi {assignments.length} tasi
                ko'rsatilyapti. Qolganini ko'rish uchun guruh/o'qituvchi/holat filtrlaridan
                foydalaning.
              </p>
            )}

            <div className="border-t border-slate-100 p-4">
              <TablePagination {...pagination} />
            </div>
          </div>
        )}
      </Card>

      {/* Create Modal */}
      <Modal
        open={isModalOpen}
        onClose={() => setIsModalOpen(false)}
        title="O'rinbosar o'qituvchi biriktirish"
      >
        <form onSubmit={handleSubmit} className="space-y-4">
          {errorMsg && (
            <div className="rounded-lg bg-red-50 p-3 text-sm font-medium text-red-600">
              {errorMsg}
            </div>
          )}

          <label className="block">
            <span className="mb-1 block text-xs font-medium text-slate-700">
              Guruhni tanlang <span className="text-red-500">*</span>
            </span>
            <select
              value={formGroupId}
              onChange={(e) => setFormGroupId(e.target.value)}
              className={cn(control, 'w-full')}
              required
            >
              <option value="">-- Guruhni tanlang --</option>
              {classes.map((c) => (
                <option key={c.id} value={c.id}>
                  {c.name} ({c.grade ? `${c.grade}-sinf` : 'Sinf belgilanmagan'})
                </option>
              ))}
            </select>
          </label>

          {selectedGroup && (
            <div className="rounded-lg border border-slate-200 bg-slate-50 p-3">
              <span className="block text-xs text-slate-500">Asosiy (doimiy) o'qituvchisi:</span>
              <span className="text-sm font-semibold text-slate-800">
                {originalTeacher ? originalTeacher.fullName : "Noma'lum o'qituvchi"}
              </span>
            </div>
          )}

          <label className="block">
            <span className="mb-1 block text-xs font-medium text-slate-700">
              O'rinbosar (vaqtincha) o'qituvchini tanlang <span className="text-red-500">*</span>
            </span>
            <select
              value={formSubstituteTeacherId}
              onChange={(e) => setFormSubstituteTeacherId(e.target.value)}
              className={cn(control, 'w-full')}
              required
            >
              <option value="">-- O'rinbosar o'qituvchini tanlang --</option>
              {teachers
                .filter(
                  (t) => !selectedGroup || !selectedGroup.teacherId || t.id !== selectedGroup.teacherId,
                )
                .map((t) => (
                  <option key={t.id} value={t.id}>
                    {t.fullName} ({t.phone || "Tel ko'rsatilmagan"})
                  </option>
                ))}
            </select>
          </label>

          <Input
            label="Oyni tanlang"
            required
            type="month"
            value={formMonth}
            onChange={(e) => setFormMonth(e.target.value)}
          />

          <div>
            <div className="mb-1.5 flex items-center justify-between">
              <span className="block text-xs font-medium text-slate-700">
                O'rinbosar dars o'tadigan sanalar <span className="text-red-500">*</span>
              </span>
              {lessonDates.length > 0 && (
                <div className="flex items-center gap-2 text-xs">
                  <button
                    type="button"
                    onClick={selectAllDates}
                    className="font-medium text-brand-600 hover:underline"
                  >
                    Barchasini tanlash
                  </button>
                  <span className="text-slate-300">|</span>
                  <button
                    type="button"
                    onClick={clearAllDates}
                    className="font-medium text-slate-500 hover:underline"
                  >
                    Tozalash
                  </button>
                </div>
              )}
            </div>

            {loadingLessonDates ? (
              <div className="rounded-lg bg-slate-50 p-4 text-center text-xs text-slate-500">
                Dars sanalari yuklanmoqda...
              </div>
            ) : !formGroupId ? (
              <div className="rounded-lg border border-dashed border-slate-200 bg-slate-50 p-3 text-center text-xs text-slate-400">
                Dars sanalarini ko'rish uchun avval guruhni tanlang
              </div>
            ) : lessonDates.length === 0 ? (
              <div className="rounded-lg border border-amber-200 bg-amber-50 p-3 text-center text-xs text-amber-600">
                Ushbu oyda guruh uchun rejalashtirilgan dars kunlari topilmadi
              </div>
            ) : (
              <>
                <div className="flex max-h-40 flex-wrap gap-1.5 overflow-y-auto rounded-lg border border-slate-200 bg-slate-50 p-3">
                  {lessonDates.map((d) => {
                    const isSelected = selectedDates.includes(d.date)
                    const isPast = d.date < today
                    return (
                      <button
                        key={d.date}
                        type="button"
                        aria-pressed={isSelected}
                        title={isPast ? `${d.date} — o'tgan kun` : d.date}
                        onClick={() => toggleDate(d.date)}
                        className={cn(
                          'rounded-md border px-2.5 py-1.5 text-xs font-semibold transition-all',
                          isSelected
                            ? 'border-emerald-600 bg-emerald-600 text-white shadow-sm'
                            : isPast
                              ? // O'tgan kunlar ATAYIN xira va uzuq chegarali — ular ham
                                // tanlanadi (kechikkan rasmiylashtirish bo'ladi), lekin
                                // adashib bosib qo'yilmasin.
                                'border-dashed border-slate-300 bg-white text-slate-400 hover:bg-slate-100'
                              : 'border-slate-200 bg-white text-slate-700 hover:border-slate-300 hover:bg-slate-100',
                        )}
                      >
                        {d.dayName}
                      </button>
                    )
                  })}
                </div>
                <p className="mt-1 text-[11px] text-slate-400">
                  Standart bo'yicha hech qaysi kun tanlanmagan — kerakli kunlarni o'zingiz
                  belgilang. Xira (uzuq chegarali) kunlar — allaqachon o'tib ketgan sanalar.
                </p>
              </>
            )}
          </div>

          {/* ── SERVER HISOBI ────────────────────────────────────────────────────────────
              ⚠️ Summalar KLIENTDA hisoblanmaydi. Ilgari bu yerda mustaqil formula turardi
              (o'quvchilar soni "10", foiz "50" deb taxmin qilinardi) va natijada modal,
              jadval hamda maosh varaqasi UCH XIL raqam ko'rsatardi. */}
          {formGroupId && selectedDates.length > 0 && (
            <div className="space-y-1 rounded-lg border border-emerald-200 bg-emerald-50/70 p-3 text-xs">
              {!formSubstituteTeacherId ? (
                <p className="text-slate-500">
                  Hisob-kitobni ko'rish uchun o'rinbosar o'qituvchini tanlang
                </p>
              ) : previewLoading ? (
                <div className="space-y-1.5" aria-busy="true">
                  <p className="text-slate-500">Hisoblanmoqda...</p>
                  <div className="h-3 w-2/3 animate-pulse rounded bg-emerald-200/60" />
                  <div className="h-3 w-1/2 animate-pulse rounded bg-emerald-200/60" />
                  <div className="h-3 w-3/4 animate-pulse rounded bg-emerald-200/60" />
                </div>
              ) : previewError ? (
                <p className="font-medium text-red-600">{previewError}</p>
              ) : preview ? (
                <>
                  <div className="flex justify-between text-slate-700">
                    <span>1 ta dars narxi:</span>
                    <span className="font-semibold">{formatMoney(preview.perLessonFee)}</span>
                  </div>
                  <div className="flex justify-between text-slate-700">
                    <span>Tanlangan darslar soni:</span>
                    <span className="font-semibold text-emerald-700">
                      {preview.lessonCount} ta dars
                      {preview.monthLessons > 0 && (
                        <span className="ml-1 font-normal text-slate-400">
                          / oyda {preview.monthLessons} ta
                        </span>
                      )}
                    </span>
                  </div>
                  <div className="flex justify-between border-t border-emerald-200 pt-1 font-semibold text-emerald-800">
                    <span>O'rinbosar o'qituvchiga to'lanadi:</span>
                    <span>{formatMoney(preview.estimatedSalary)}</span>
                  </div>
                  <div className="flex justify-between font-medium text-slate-600">
                    <span>Asosiy o'qituvchidan ushlanadi:</span>
                    <span className="text-red-600">
                      −{formatMoney(preview.estimatedDeduction)}
                    </span>
                  </div>
                  {preview.warning && (
                    <p className="flex items-start gap-1 pt-1 font-medium text-amber-700">
                      <AlertTriangle className="mt-0.5 h-3.5 w-3.5 shrink-0" />
                      <span>{preview.warning}</span>
                    </p>
                  )}
                </>
              ) : null}
            </div>
          )}

          <Textarea
            label="Almashtirish sababi"
            placeholder="Masalan: Asosiy o'qituvchi betobligi yoki xizmat safari munosabati bilan..."
            value={formReason}
            onChange={(e) => setFormReason(e.target.value)}
            rows={3}
          />

          <div className="flex justify-end gap-2 pt-2">
            <Button
              type="button"
              variant="secondary"
              onClick={() => setIsModalOpen(false)}
              disabled={submitting}
            >
              Bekor qilish
            </Button>
            <Button type="submit" disabled={submitting}>
              {submitting ? 'Saqlanmoqda...' : 'Biriktirish'}
            </Button>
          </div>
        </form>
      </Modal>

      {/* Cancel Confirmation Modal */}
      <Modal
        open={!!cancelTargetId}
        onClose={() => setCancelTargetId(null)}
        title="Biriktiruvni bekor qilish"
      >
        <div className="space-y-4">
          {cancelError && (
            <div className="rounded-lg bg-red-50 p-3 text-sm font-medium text-red-600">
              {cancelError}
            </div>
          )}
          <p className="text-sm text-slate-600">
            Ushbu o'rinbosar o'qituvchi biriktiruvini bekor qilmoqchimisiz? Bekor qilingandan so'ng
            o'rinbosar o'qituvchi ushbu guruh dars jurnali va darslariga kira olmaydi.
          </p>
          <div className="flex justify-end gap-2 pt-2">
            <Button variant="secondary" onClick={() => setCancelTargetId(null)} disabled={cancelling}>
              Orqaga
            </Button>
            <Button variant="danger" onClick={handleCancelAssignment} disabled={cancelling}>
              {cancelling ? 'Bekor qilinmoqda...' : 'Ha, bekor qilinsin'}
            </Button>
          </div>
        </div>
      </Modal>
    </div>
  )
}
