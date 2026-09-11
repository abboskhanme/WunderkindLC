import { useEffect, useState } from 'react'
import { useParams, Link } from 'react-router-dom'
import { ArrowLeft } from 'lucide-react'
import type { AiCheck, AiCheckListItem } from '@/types'
import { getAiCheckStudentHistory, getAiCheckAdminItem } from '@/api/services/aiCheck'
import { Card } from '@/components/ui/Card'
import { Loader } from '@/components/ui/Loader'
import { PageHeader } from '@/components/ui/PageHeader'
import { fmtDate } from '@/pages/student/lib'
import { AiCheckResultView } from '@/pages/student/AiCheckResultView'
import { cn } from '@/lib/utils'

export function AiCheckStudentPage() {
  const { studentId = '' } = useParams()
  const [loading, setLoading] = useState(true)
  const [history, setHistory] = useState<AiCheckListItem[]>([])
  const [selected, setSelected] = useState<AiCheck | null>(null)

  useEffect(() => {
    getAiCheckStudentHistory(studentId)
      .then(async (h) => {
        setHistory(h)
        if (h.length > 0) setSelected(await getAiCheckAdminItem(h[0].id))
      })
      .finally(() => setLoading(false))
  }, [studentId])

  const open = async (id: string) => {
    setSelected(await getAiCheckAdminItem(id))
  }

  if (loading) return <Loader label="Yuklanmoqda..." />

  return (
    <div>
      <Link to="/admin/ai-check" className="mb-3 inline-flex items-center gap-1 text-sm font-medium text-brand-600 hover:text-brand-700">
        <ArrowLeft className="h-4 w-4" /> AI tekshiruv
      </Link>
      <PageHeader title="O'quvchi AI tekshiruv tarixi" sub="O'quvchi yozgan/gapirgan va AI tahlili (o'quvchidagidek ko'rinish)" />

      {history.length === 0 ? (
        <Card title="Tarix">
          <p className="py-4 text-sm text-slate-400">Bu o'quvchida AI tekshiruv yo'q.</p>
        </Card>
      ) : (
        <div className="grid gap-4 lg:grid-cols-[280px_1fr]">
          {/* Tarix ro'yxati */}
          <Card title="Tarix" sub={`${history.length} ta`}>
            <div className="space-y-1.5">
              {history.map((h) => (
                <button
                  key={h.id}
                  onClick={() => open(h.id)}
                  className={cn(
                    'flex w-full items-center justify-between rounded-lg border px-3 py-2 text-left transition-colors',
                    selected?.id === h.id ? 'border-brand-300 bg-brand-50' : 'border-slate-200 hover:bg-slate-50',
                  )}
                >
                  <div className="min-w-0">
                    <div className="text-sm font-medium text-slate-800">
                      {h.type === 'speaking' ? '🎤 Speaking' : '✍️ Writing'}
                    </div>
                    <div className="truncate text-xs text-slate-400">{h.prompt || fmtDate(h.createdAt, true)}</div>
                  </div>
                  <span className="font-mono text-sm font-bold text-slate-700">{Math.round(h.score)}</span>
                </button>
              ))}
            </div>
          </Card>

          {/* Tanlangan natija — o'quvchi ko'rinishi (.student-app scope) */}
          <div>
            {selected ? (
              <div className="student-app" data-theme="light" style={{ background: 'transparent' }}>
                <AiCheckResultView rec={selected} />
              </div>
            ) : (
              <Card title="Natija"><p className="py-4 text-sm text-slate-400">Chapdan tanlang.</p></Card>
            )}
          </div>
        </div>
      )}
    </div>
  )
}
