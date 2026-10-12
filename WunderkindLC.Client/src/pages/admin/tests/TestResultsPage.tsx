import { useEffect, useMemo, useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { ClipboardList, Users, Search, ChevronRight, ArrowLeft, GraduationCap, Award, Send } from 'lucide-react'
import type { TestGroupOverview, TestOverviewRow } from '@/types'
import { getTestGroups, getTestsOverview } from '@/api/services/testResults'
import { Card } from '@/components/ui/Card'
import { Button } from '@/components/ui/Button'
import { Loader } from '@/components/ui/Loader'
import { PageHeader } from '@/components/ui/PageHeader'
import { apiErrorMessage, cn, formatDate } from '@/lib/utils'
import { currentMonth } from '@/lib/month'
import { fmtScore, testKindLabel } from '@/lib/ratingPeriods'
import { formatSentAt } from '@/lib/testScoreGrid'
import { TestMonthFilter } from './TestMonthFilter'

interface TeacherSummary {
  teacherId: string
  teacherName: string
  groupCount: number
  testCount: number
}

type View = 'groups' | 'tests'

/**
 * "O'quv bo'limi" → Testlar natijalari. Ikki ko'rinish (CRM U4a):
 * • «Guruhlar» — ikki bosqichli tanlov: o'qituvchi → uning guruhlari (tanlangan OYdagi testlar soni va o'rtacha
 *   foiz bilan); guruhga kirilsa — testlar ro'yxati;
 * • «Barcha testlar» — markaz kesimi: oydagi barcha guruhlar testlari (tur filtri bilan).
 * Oy filtri (standart — joriy oy, «Hammasi» — barcha oylar) ikkalasiga ham tegishli.
 */
export function TestResultsPage() {
  const navigate = useNavigate()
  const thisMonth = currentMonth()
  const [month, setMonth] = useState(thisMonth)
  const [kind, setKind] = useState('')
  const [view, setView] = useState<View>('groups')
  const [res, setRes] = useState<{ month: string; groups: TestGroupOverview[]; error: string } | null>(null)
  const [q, setQ] = useState('')
  const [selectedTeacherId, setSelectedTeacherId] = useState<string | null>(null)

  useEffect(() => {
    let active = true
    getTestGroups(month)
      .then((groups) => active && setRes({ month, groups, error: '' }))
      .catch((e) => active && setRes({ month, groups: [], error: apiErrorMessage(e, 'Guruhlarni yuklab bo\'lmadi') }))
    return () => {
      active = false
    }
  }, [month])

  const groups = useMemo(() => res?.groups ?? [], [res])
  const loading = !res
  const error = res?.error ?? ''
  /** Tanlangan oy kesimidagi testlar soni (oysiz — butun vaqt). */
  const testsOf = (g: TestGroupOverview) => g.monthTestCount ?? g.testCount

  const teachers = useMemo(() => {
    const byId = new Map<string, TeacherSummary>()
    for (const g of groups) {
      if (!g.teacherId) continue
      const cur = byId.get(g.teacherId)
      const n = g.monthTestCount ?? g.testCount
      if (cur) {
        cur.groupCount += 1
        cur.testCount += n
      } else {
        byId.set(g.teacherId, {
          teacherId: g.teacherId,
          teacherName: g.teacherName || "Noma'lum",
          groupCount: 1,
          testCount: n,
        })
      }
    }
    return [...byId.values()].sort((a, b) => a.teacherName.localeCompare(b.teacherName, 'uz'))
  }, [groups])

  const selectedTeacher = teachers.find((t) => t.teacherId === selectedTeacherId) ?? null

  const filteredTeachers = useMemo(() => {
    const s = q.trim().toLowerCase()
    if (!s) return teachers
    return teachers.filter((t) => t.teacherName.toLowerCase().includes(s))
  }, [teachers, q])

  const teacherGroups = useMemo(() => {
    if (!selectedTeacherId) return []
    const s = q.trim().toLowerCase()
    return groups
      .filter((g) => g.teacherId === selectedTeacherId)
      .filter((g) => !s || g.name.toLowerCase().includes(s) || g.courseName.toLowerCase().includes(s))
  }, [groups, selectedTeacherId, q])

  const selectTeacher = (teacherId: string) => {
    setSelectedTeacherId(teacherId)
    setQ('')
  }

  const backToTeachers = () => {
    setSelectedTeacherId(null)
    setQ('')
  }

  return (
    <div>
      <PageHeader
        title="Testlar natijalari"
        sub={
          selectedTeacher
            ? `${selectedTeacher.teacherName} — guruh tanlang`
            : "O'qituvchi tanlang — keyin uning guruhlarini ko'rasiz"
        }
        actions={
          <Button variant="secondary" onClick={() => navigate('/admin/test-results/certificate-templates')}>
            <Award className="h-4 w-4" /> Sertifikat shablonlari
          </Button>
        }
      />

      <div className="mb-4 flex flex-wrap items-center gap-3">
        <div className="flex gap-2" role="tablist">
          <button type="button" className={cn('tab', view === 'groups' && 'active')} onClick={() => setView('groups')}>
            Guruhlar
          </button>
          <button type="button" className={cn('tab', view === 'tests' && 'active')} onClick={() => setView('tests')}>
            Barcha testlar
          </button>
        </div>
        <TestMonthFilter
          month={month}
          kind={kind}
          onMonth={setMonth}
          onKind={setKind}
          currentMonth={thisMonth}
          showKind={view === 'tests'}
        />
      </div>

      {view === 'tests' ? (
        <OverviewTable
          month={month}
          kind={kind}
          onOpen={(row) => navigate(`/admin/test-results/${row.groupId}/tests/${row.testId}`)}
        />
      ) : (
      <>
      {selectedTeacher && (
        <button
          type="button"
          onClick={backToTeachers}
          className="mb-4 inline-flex items-center gap-1.5 text-sm font-medium text-slate-500 transition-colors hover:text-brand-600"
        >
          <ArrowLeft className="h-4 w-4" /> O'qituvchilarga qaytish
        </button>
      )}

      <div className="mb-4 relative max-w-sm">
        <Search className="pointer-events-none absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-slate-400" />
        <input
          value={q}
          onChange={(e) => setQ(e.target.value)}
          placeholder={selectedTeacher ? 'Guruh yoki kurs qidirish...' : "O'qituvchi qidirish..."}
          className="w-full rounded-lg border border-slate-200 py-2 pl-9 pr-3 text-sm text-slate-800 outline-none transition-colors focus:border-brand-400"
        />
      </div>

      {loading ? (
        <Loader label="Yuklanmoqda..." />
      ) : error ? (
        <Card className="py-10 text-center text-red-500">{error}</Card>
      ) : !selectedTeacher ? (
        filteredTeachers.length === 0 ? (
          <Card className="py-12 text-center text-slate-400">
            {teachers.length === 0 ? "O'qituvchilar topilmadi" : 'Qidiruvga mos o\'qituvchi yo\'q'}
          </Card>
        ) : (
          <div className="grid grid-cols-1 gap-3 sm:grid-cols-2 lg:grid-cols-3">
            {filteredTeachers.map((t) => (
              <button
                key={t.teacherId}
                type="button"
                onClick={() => selectTeacher(t.teacherId)}
                className="group flex items-center gap-3 rounded-xl border border-slate-200 bg-white p-4 text-left transition-all hover:border-brand-300 hover:shadow-md"
              >
                <div className="flex h-11 w-11 shrink-0 items-center justify-center rounded-xl bg-brand-50 text-brand-600">
                  <GraduationCap className="h-5 w-5" />
                </div>
                <div className="min-w-0 flex-1">
                  <p className="truncate font-semibold text-slate-800">{t.teacherName}</p>
                  <div className="mt-1.5 flex items-center gap-3 text-xs">
                    <span className="inline-flex items-center gap-1 text-slate-500">
                      <Users className="h-3.5 w-3.5" /> {t.groupCount} guruh
                    </span>
                    <span className="inline-flex items-center gap-1 font-medium text-brand-600">
                      <ClipboardList className="h-3.5 w-3.5" /> {t.testCount} ta test
                    </span>
                  </div>
                </div>
                <ChevronRight className="h-5 w-5 shrink-0 text-slate-300 transition-transform group-hover:translate-x-0.5" />
              </button>
            ))}
          </div>
        )
      ) : teacherGroups.length === 0 ? (
        <Card className="py-12 text-center text-slate-400">
          {q.trim() ? 'Qidiruvga mos guruh yo\'q' : "Bu o'qituvchining guruhi yo'q"}
        </Card>
      ) : (
        <div className="grid grid-cols-1 gap-3 sm:grid-cols-2 lg:grid-cols-3">
          {teacherGroups.map((g) => (
            <button
              key={g.groupId}
              type="button"
              onClick={() => navigate(`/admin/test-results/${g.groupId}`)}
              className="group flex items-center gap-3 rounded-xl border border-slate-200 bg-white p-4 text-left transition-all hover:border-brand-300 hover:shadow-md"
            >
              <div className="flex h-11 w-11 shrink-0 items-center justify-center rounded-xl bg-brand-50 text-brand-600">
                <ClipboardList className="h-5 w-5" />
              </div>
              <div className="min-w-0 flex-1">
                <p className="truncate font-semibold text-slate-800">{g.name}</p>
                <p className="truncate text-xs text-slate-400">{g.courseName || '—'}</p>
                <div className="mt-1.5 flex items-center gap-3 text-xs">
                  <span className="inline-flex items-center gap-1 text-slate-500">
                    <Users className="h-3.5 w-3.5" /> {g.studentCount}
                  </span>
                  <span className="inline-flex items-center gap-1 font-medium text-brand-600">
                    <ClipboardList className="h-3.5 w-3.5" /> {testsOf(g)} ta test
                  </span>
                  {g.monthAvgPercent != null && (
                    <span className="font-medium text-slate-600">o'rtacha {fmtScore(g.monthAvgPercent)}%</span>
                  )}
                </div>
              </div>
              <ChevronRight className="h-5 w-5 shrink-0 text-slate-300 transition-transform group-hover:translate-x-0.5" />
            </button>
          ))}
        </div>
      )}
      </>
      )}
    </div>
  )
}

/** «Barcha testlar» — tanlangan oydagi barcha guruhlar testlari (markaz kesimi). */
function OverviewTable({
  month,
  kind,
  onOpen,
}: {
  month: string
  kind: string
  onOpen: (row: TestOverviewRow) => void
}) {
  const key = `${month}|${kind}`
  const [res, setRes] = useState<{ key: string; rows: TestOverviewRow[]; error: string } | null>(null)

  useEffect(() => {
    let active = true
    const k = `${month}|${kind}`
    getTestsOverview({ month, kind })
      .then((rows) => active && setRes({ key: k, rows, error: '' }))
      .catch((e) => active && setRes({ key: k, rows: [], error: apiErrorMessage(e, "Testlarni yuklab bo'lmadi") }))
    return () => {
      active = false
    }
  }, [month, kind])

  if (!res) return <Loader label="Yuklanmoqda..." />
  if (res.error) return <Card className="py-10 text-center text-red-500">{res.error}</Card>
  if (res.rows.length === 0) return <Card className="py-12 text-center text-slate-400">Bu davrda test yo'q.</Card>

  return (
    <Card tight className={cn('overflow-x-auto transition-opacity', res.key !== key && 'opacity-60')}>
      <table className="w-full min-w-[720px] text-left text-sm">
        <thead>
          <tr className="border-b border-slate-100 bg-slate-50/60 text-xs uppercase tracking-wide text-slate-400">
            <th className="px-4 py-3 font-medium">Sana</th>
            <th className="px-3 py-3 font-medium">Test</th>
            <th className="px-3 py-3 font-medium">Guruh</th>
            <th className="px-3 py-3 font-medium">O'qituvchi</th>
            <th className="px-3 py-3 text-right font-medium">Kiritilgan</th>
            <th className="px-3 py-3 text-right font-medium">O'rtacha</th>
            <th className="px-4 py-3 font-medium">Yuborilgan</th>
          </tr>
        </thead>
        <tbody>
          {res.rows.map((r) => (
            <tr
              key={r.testId}
              onClick={() => onOpen(r)}
              className="cursor-pointer border-b border-slate-50 last:border-0 hover:bg-slate-50/60"
            >
              <td className="whitespace-nowrap px-4 py-2.5 text-slate-500">{formatDate(r.date)}</td>
              <td className="px-3 py-2.5">
                <span className="font-medium text-slate-800">{r.name}</span>
                {testKindLabel(r.kind) && (
                  <span className="ml-1.5 rounded-md bg-indigo-50 px-1.5 py-0.5 text-[10px] font-semibold text-indigo-700">
                    {testKindLabel(r.kind)}
                  </span>
                )}
                <span className="ml-1.5 text-xs text-slate-400">
                  {r.questionCount > 0 ? `${r.questionCount} ta savol` : `maks ${r.maxScore}`}
                </span>
              </td>
              <td className="px-3 py-2.5 text-slate-600">{r.groupName}</td>
              <td className="px-3 py-2.5 text-slate-600">{r.teacherName || '—'}</td>
              <td className="whitespace-nowrap px-3 py-2.5 text-right text-slate-600">
                {r.scored}/{r.members}
              </td>
              <td className="whitespace-nowrap px-3 py-2.5 text-right font-semibold text-slate-700">
                {r.avgPercent == null ? '—' : `${fmtScore(r.avgPercent)}%`}
              </td>
              <td className="whitespace-nowrap px-4 py-2.5 text-xs text-emerald-700">
                {r.resultsSentAt ? (
                  <span className="inline-flex items-center gap-1">
                    <Send className="h-3 w-3" /> {formatSentAt(r.resultsSentAt)}
                  </span>
                ) : (
                  <span className="text-slate-300">—</span>
                )}
              </td>
            </tr>
          ))}
        </tbody>
      </table>
    </Card>
  )
}
