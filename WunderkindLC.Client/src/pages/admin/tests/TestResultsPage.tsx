import { useEffect, useMemo, useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { ClipboardList, Users, Search, ChevronRight, ArrowLeft, GraduationCap, Award } from 'lucide-react'
import type { TestGroupOverview } from '@/types'
import { getTestGroups } from '@/api/services/testResults'
import { Card } from '@/components/ui/Card'
import { Button } from '@/components/ui/Button'
import { Loader } from '@/components/ui/Loader'
import { PageHeader } from '@/components/ui/PageHeader'
import { apiErrorMessage } from '@/lib/utils'

interface TeacherSummary {
  teacherId: string
  teacherName: string
  groupCount: number
  testCount: number
}

/**
 * "O'quv bo'limi" → Testlar natijalari — ikki bosqichli tanlov:
 * 1) O'qituvchi tanlanadi (kartalar);
 * 2) Shu o'qituvchining guruhlari ko'rsatiladi. Guruhga kirilsa — shu guruhning testlar ro'yxati.
 */
export function TestResultsPage() {
  const navigate = useNavigate()
  const [groups, setGroups] = useState<TestGroupOverview[]>([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState('')
  const [q, setQ] = useState('')
  const [selectedTeacherId, setSelectedTeacherId] = useState<string | null>(null)

  useEffect(() => {
    getTestGroups()
      .then(setGroups)
      .catch((e) => setError(apiErrorMessage(e, 'Guruhlarni yuklab bo\'lmadi')))
      .finally(() => setLoading(false))
  }, [])

  const teachers = useMemo(() => {
    const byId = new Map<string, TeacherSummary>()
    for (const g of groups) {
      if (!g.teacherId) continue
      const cur = byId.get(g.teacherId)
      if (cur) {
        cur.groupCount += 1
        cur.testCount += g.testCount
      } else {
        byId.set(g.teacherId, {
          teacherId: g.teacherId,
          teacherName: g.teacherName || "Noma'lum",
          groupCount: 1,
          testCount: g.testCount,
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
                    <ClipboardList className="h-3.5 w-3.5" /> {g.testCount} ta test
                  </span>
                </div>
              </div>
              <ChevronRight className="h-5 w-5 shrink-0 text-slate-300 transition-transform group-hover:translate-x-0.5" />
            </button>
          ))}
        </div>
      )}
    </div>
  )
}
