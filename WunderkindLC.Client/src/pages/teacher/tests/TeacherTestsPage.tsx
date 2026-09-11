import { useEffect, useState } from 'react'
import { GraduationCap } from 'lucide-react'
import type { TeacherClass } from '@/types'
import { getMyClasses } from '@/api/services/teacher'
import { cn } from '@/lib/utils'
import { Loader } from '@/components/ui/Loader'
import { TeacherGroupTestsPanel } from './TeacherGroupTestsPanel'

function initialsOf(name: string): string {
  return name
    .split(' ')
    .map((w) => w[0])
    .filter(Boolean)
    .slice(0, 2)
    .join('')
    .toUpperCase()
}

/**
 * O'qituvchi — Testlar. Guruhni tanlaydi, keyin `TeacherGroupTestsPanel` ochiladi (testlar ro'yxati,
 * onlayn/oflayn test yaratish, ball qo'yish). AYNAN shu panel guruh (jurnal) sahifasidagi
 * "Imtihonlar" tabida ham ishlatiladi — funksiya ikkala joyda bir xil.
 */
export function TeacherTestsPage() {
  const [classes, setClasses] = useState<TeacherClass[]>([])
  const [classesLoading, setClassesLoading] = useState(true)
  const [selectedClass, setSelectedClass] = useState<TeacherClass | null>(null)

  useEffect(() => {
    getMyClasses()
      .then(setClasses)
      .catch(() => {})
      .finally(() => setClassesLoading(false))
  }, [])

  // ---------------- 2. Tanlangan guruh testlari ----------------
  if (selectedClass) {
    return (
      <div className="px-4 pt-3 pb-6">
        <TeacherGroupTestsPanel
          key={selectedClass.classId}
          groupId={selectedClass.classId}
          title={selectedClass.className}
          subtitle="Test natijalari"
          onBack={() => setSelectedClass(null)}
        />
      </div>
    )
  }

  // ---------------- 1. Guruhlar ro'yxati ----------------
  return (
    <div className="px-4 pt-3 pb-6">
      <p className="mb-3 text-[17px] font-extrabold text-ink">Test natijalari</p>

      {classesLoading ? (
        <div className="rounded-[20px] border border-line bg-white p-6 shadow-[var(--shadow-card)]">
          <Loader label="Yuklanmoqda..." />
        </div>
      ) : classes.length === 0 ? (
        <div className="rounded-[20px] border border-line bg-white px-5 py-8 text-center text-[13px] text-faint shadow-[var(--shadow-card)]">
          Sizga biriktirilgan guruh yo'q.
        </div>
      ) : (
        <div className="overflow-hidden rounded-[20px] border border-line bg-white shadow-[var(--shadow-card)]">
          {classes.map((c, i) => (
            <button
              key={c.classId}
              type="button"
              onClick={() => setSelectedClass(c)}
              className={cn(
                'tap-scale flex w-full items-center gap-3 px-4 py-3.5 text-left',
                i < classes.length - 1 && 'border-b border-line',
              )}
            >
              <div className="flex h-10 w-10 shrink-0 items-center justify-center rounded-[12px] bg-tealsoft text-[15px] font-extrabold text-teal-700">
                {initialsOf(c.className)}
              </div>
              <div className="min-w-0 flex-1">
                <p className="truncate text-[14px] font-bold text-ink">{c.className}</p>
                {c.subjects.length > 0 && (
                  <p className="truncate text-[11px] text-mute">
                    {c.subjects.map((s) => s.name).join(', ')}
                  </p>
                )}
              </div>
              <GraduationCap className="h-4 w-4 shrink-0 text-faint" />
            </button>
          ))}
        </div>
      )}
    </div>
  )
}
