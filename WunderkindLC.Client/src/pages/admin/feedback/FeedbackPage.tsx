import { useEffect, useState } from 'react'
import { MessageSquareWarning, Lightbulb, CheckCircle2, GraduationCap, Users, Inbox } from 'lucide-react'
import type { Feedback } from '@/types'
import { getFeedback, resolveFeedback } from '@/api/services/feedback'
import { formatDate, cn } from '@/lib/utils'
import { Card } from '@/components/ui/Card'
import { Button } from '@/components/ui/Button'
import { Badge } from '@/components/ui/Badge'
import { PageHeader } from '@/components/ui/PageHeader'
import { Loader } from '@/components/ui/Loader'
import { usePerm } from '@/lib/permissions'

type TypeFilter = '' | 'suggestion' | 'complaint'
type StatusFilter = '' | 'new' | 'resolved'

export function FeedbackPage() {
  const { can } = usePerm()
  const [items, setItems] = useState<Feedback[]>([])
  const [loading, setLoading] = useState(true)
  const [type, setType] = useState<TypeFilter>('')
  const [status, setStatus] = useState<StatusFilter>('')

  useEffect(() => {
    // eslint-disable-next-line react-hooks/set-state-in-effect -- filtr o'zgarganda qayta yuklash (maqsadli)
    setLoading(true)
    getFeedback(type, status)
      .then(setItems)
      .finally(() => setLoading(false))
  }, [type, status])

  const resolve = (id: string) => {
    resolveFeedback(id).then(() =>
      setItems((p) => p.map((f) => (f.id === id ? { ...f, status: 'resolved' } : f))),
    )
  }

  return (
    <div>
      <PageHeader
        title="Taklif va shikoyatlar"
        sub="Ota-onalar ilova orqali yuborgan murojaatlar"
      />

      <div className="toolbar">
        <div className="left">
          <button
            type="button"
            className={cn('filter-chip', type === '' && 'active')}
            onClick={() => setType('')}
          >
            Hammasi
          </button>
          <button
            type="button"
            className={cn('filter-chip', type === 'suggestion' && 'active')}
            onClick={() => setType('suggestion')}
          >
            Takliflar
          </button>
          <button
            type="button"
            className={cn('filter-chip', type === 'complaint' && 'active')}
            onClick={() => setType('complaint')}
          >
            Shikoyatlar
          </button>
        </div>
        <div className="right">
          <button
            type="button"
            className={cn('filter-chip', status === '' && 'active')}
            onClick={() => setStatus('')}
          >
            Barcha holat
          </button>
          <button
            type="button"
            className={cn('filter-chip', status === 'new' && 'active')}
            onClick={() => setStatus('new')}
          >
            Yangi
          </button>
          <button
            type="button"
            className={cn('filter-chip', status === 'resolved' && 'active')}
            onClick={() => setStatus('resolved')}
          >
            Hal qilingan
          </button>
        </div>
      </div>

      {loading ? (
        <Card>
          <Loader label="Yuklanmoqda..." />
        </Card>
      ) : items.length === 0 ? (
        <Card>
          <div className="state">
            <div className="state-icon">
              <Inbox className="h-6 w-6" />
            </div>
            <h4>Murojaatlar yo'q</h4>
            <p>Tanlangan filtrlar bo'yicha murojaat topilmadi.</p>
          </div>
        </Card>
      ) : (
        <div className="space-y-3">
          {items.map((f) => (
            <Card key={f.id} className="flex flex-col gap-2">
              <div className="flex flex-wrap items-center justify-between gap-2">
                <div className="flex flex-wrap items-center gap-1.5">
                  {f.type === 'complaint' ? (
                    <Badge tone="red">
                      <MessageSquareWarning className="h-3.5 w-3.5" /> Shikoyat
                    </Badge>
                  ) : (
                    <Badge tone="amber">
                      <Lightbulb className="h-3.5 w-3.5" /> Taklif
                    </Badge>
                  )}
                  {f.senderRole === 'teacher' ? (
                    <Badge tone="violet">
                      <GraduationCap className="h-3.5 w-3.5" /> O'qituvchi
                    </Badge>
                  ) : (
                    <Badge tone="blue">
                      <Users className="h-3.5 w-3.5" /> Ota-ona
                    </Badge>
                  )}
                  {f.status === 'resolved' ? (
                    <Badge tone="green">
                      <CheckCircle2 className="h-3.5 w-3.5" /> Hal qilingan
                    </Badge>
                  ) : (
                    <Badge tone="default">Yangi</Badge>
                  )}
                </div>
                {f.status === 'new' && can('feedback', 'edit') && (
                  <Button variant="secondary" onClick={() => resolve(f.id)}>
                    <CheckCircle2 className="h-4 w-4" /> Hal qilindi
                  </Button>
                )}
              </div>
              <p className="whitespace-pre-wrap break-words text-sm text-slate-700">{f.text}</p>
              {f.imageUrl && (
                <a href={f.imageUrl} target="_blank" rel="noreferrer" className="block w-fit">
                  <img
                    src={f.imageUrl}
                    alt="Biriktirilgan rasm"
                    className="max-h-44 rounded-lg border border-slate-200 object-cover"
                  />
                </a>
              )}
              <div className="text-xs text-slate-400">
                {f.senderName || f.parentName || f.studentName || "Noma'lum"}
                {f.senderRole === 'parent' && f.studentName ? ` · farzandi: ${f.studentName}` : ''}
                {f.senderRole === 'parent' && f.className ? ` · ${f.className}` : ''}
                {' · '}
                <span className="font-mono">{formatDate(f.createdAt)}</span>
              </div>
            </Card>
          ))}
        </div>
      )}
    </div>
  )
}
