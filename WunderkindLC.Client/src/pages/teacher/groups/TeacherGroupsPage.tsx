import { useEffect, useState } from 'react'
import { Link } from 'react-router-dom'
import { ChevronRight, Users } from 'lucide-react'
import type { TeacherClass } from '@/types'
import { getMyClasses } from '@/api/services/teacher'

function groupInitials(name: string): string {
  const cleaned = name.replace(/\s+/g, '')
  return cleaned.slice(0, 3).toUpperCase() || '?'
}

export function TeacherGroupsPage() {
  const [classes, setClasses] = useState<TeacherClass[]>([])
  const [loading, setLoading] = useState(true)

  useEffect(() => {
    getMyClasses()
      .then(setClasses)
      .finally(() => setLoading(false))
  }, [])

  return (
    <div className="px-4 pb-6">
      {/* ── Header ── */}
      <div className="pt-2 pb-3">
        <p className="text-[22px] font-extrabold tracking-tight text-ink">Jurnal</p>
        <p className="text-[12px] text-mute">Guruhni tanlang — o'quvchilarga baho qo'yish</p>
      </div>

      {/* ── Ro'yxat ── */}
      {loading ? (
        <div className="space-y-2.5">
          <div className="skeleton h-[78px] rounded-[20px]" />
          <div className="skeleton h-[78px] rounded-[20px]" />
          <div className="skeleton h-[78px] rounded-[20px]" />
        </div>
      ) : classes.length === 0 ? (
        <div className="rounded-[20px] border border-line bg-white px-5 py-10 text-center text-[13px] text-mute shadow-[var(--shadow-card)]">
          Sizga biriktirilgan guruh yo'q.
        </div>
      ) : (
        <div className="space-y-2.5">
          {classes.map((c) => (
            <Link
              key={c.classId}
              to={`/teacher/groups/${c.classId}`}
              className="tap-scale flex items-center gap-3.5 rounded-[20px] border border-line bg-white p-3.5 shadow-[var(--shadow-card)]"
            >
              <div className="flex h-12 w-12 shrink-0 items-center justify-center rounded-2xl bg-gradient-to-br from-teal-500 to-teal-700 text-[14px] font-extrabold text-white">
                {groupInitials(c.className)}
              </div>
              <div className="min-w-0 flex-1">
                <p className="truncate text-[15px] font-bold text-ink">{c.className}</p>
                <p className="mt-0.5 truncate text-[12px] text-mute">
                  {c.subjects.length > 0 ? (
                    c.subjects.map((s) => s.name).join(', ')
                  ) : (
                    <span className="inline-flex items-center gap-1 text-faint">
                      <Users className="h-3.5 w-3.5" /> Fan biriktirilmagan
                    </span>
                  )}
                </p>
              </div>
              <ChevronRight className="h-5 w-5 shrink-0 text-faint" />
            </Link>
          ))}
        </div>
      )}
    </div>
  )
}
