import { useEffect, useState } from 'react'
import { Link } from 'react-router-dom'
import { Headset, ChevronRight, Phone } from 'lucide-react'
import { getSupportTeachers, type SupportTeacher } from '@/api/services/support'
import { Card } from '@/components/ui/Card'
import { Loader } from '@/components/ui/Loader'
import { PageHeader } from '@/components/ui/PageHeader'

/** Ism bosh harflari (rasm yo'q bo'lsa avatar). */
function initials(name: string) {
  return name
    .trim()
    .split(/\s+/)
    .slice(0, 2)
    .map((p) => p[0]?.toUpperCase() ?? '')
    .join('')
}

export function SupportPage() {
  const [teachers, setTeachers] = useState<SupportTeacher[]>([])
  const [loading, setLoading] = useState(true)

  useEffect(() => {
    getSupportTeachers()
      .then(setTeachers)
      .finally(() => setLoading(false))
  }, [])

  if (loading) return <Loader label="Yuklanmoqda..." />

  return (
    <div>
      <PageHeader
        title="Support"
        sub="Support o'qituvchilar — bo'sh vaqt va bron darslari"
      />

      {teachers.length === 0 ? (
        <Card>
          <div className="flex flex-col items-center gap-3 py-12 text-center">
            <div className="flex h-14 w-14 items-center justify-center rounded-2xl bg-brand-50 text-brand-600">
              <Headset className="h-7 w-7" />
            </div>
            <p className="max-w-md text-sm text-slate-500">
              Support o'qituvchi yo'q. O'qituvchilar bo'limida o'qituvchini "Support" deb belgilang.
            </p>
          </div>
        </Card>
      ) : (
        <div className="grid gap-4 sm:grid-cols-2 xl:grid-cols-3">
          {teachers.map((t) => (
            <Link
              key={t.id}
              to={`/admin/support/${t.id}`}
              className="group text-inherit no-underline"
            >
              <Card className="flex h-full flex-col transition-shadow hover:shadow-md">
                <div className="flex items-center gap-3">
                  {t.photoUrl ? (
                    <img
                      src={t.photoUrl}
                      alt={t.fullName}
                      className="h-12 w-12 shrink-0 rounded-full object-cover"
                    />
                  ) : (
                    <div className="flex h-12 w-12 shrink-0 items-center justify-center rounded-full bg-brand-50 text-sm font-bold text-brand-600">
                      {initials(t.fullName)}
                    </div>
                  )}
                  <div className="min-w-0 flex-1">
                    <h3 className="truncate text-base font-bold text-slate-800 group-hover:underline">
                      {t.fullName}
                    </h3>
                    <p className="mt-0.5 inline-flex items-center gap-1.5 truncate text-xs text-slate-400">
                      <Phone className="h-3.5 w-3.5" />
                      <span className="font-mono">{t.phone || '—'}</span>
                    </p>
                  </div>
                  <ChevronRight className="h-5 w-5 shrink-0 text-slate-300 transition-colors group-hover:text-brand-500" />
                </div>

                <div className="mt-4 grid grid-cols-3 gap-2 border-t border-slate-100 pt-3 text-center">
                  <div>
                    <div className="font-mono text-lg font-bold text-slate-700">{t.openCount}</div>
                    <div className="text-[11px] text-slate-400">Bo'sh</div>
                  </div>
                  <div>
                    <div className="font-mono text-lg font-bold text-amber-600">{t.bookedCount}</div>
                    <div className="text-[11px] text-slate-400">Bron</div>
                  </div>
                  <div>
                    <div className="font-mono text-lg font-bold text-emerald-600">{t.doneCount}</div>
                    <div className="text-[11px] text-slate-400">O'tilgan</div>
                  </div>
                </div>
              </Card>
            </Link>
          ))}
        </div>
      )}
    </div>
  )
}
