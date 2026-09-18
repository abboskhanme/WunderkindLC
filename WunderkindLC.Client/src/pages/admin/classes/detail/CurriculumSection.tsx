import type { ReactNode } from 'react'
import {
  BookOpen, CheckCircle2, ListChecks, ChevronRight, ChevronDown, Plus, Minus, Repeat, CalendarClock, Flag,
} from 'lucide-react'
import type { GroupCurriculum } from '@/api/services/curriculum'
import { cn, formatDate } from '@/lib/utils'
import { Card } from '@/components/ui/Card'
import { Loader } from '@/components/ui/Loader'

// ============================ O'quv dasturi bo'limi ============================

export function CurriculumSection({
  curr, loading, expanded, onToggleTopic, onToggleCover, onChangeRevision, revSaving, nextItemId,
}: {
  curr: GroupCurriculum | null
  loading: boolean
  expanded: Set<string>
  onToggleTopic: (topicId: string) => void
  onToggleCover: (itemId: string, covered: boolean) => void
  onChangeRevision: (delta: number) => void
  revSaving: boolean
  nextItemId: string | null
}) {
  if (loading && !curr) {
    return (
      <Card>
        <Loader label="O'quv dasturi yuklanmoqda..." />
      </Card>
    )
  }
  if (!curr || curr.totalItems === 0 || curr.modules.length === 0) {
    return (
      <Card className="py-10 text-center text-sm text-slate-400">
        Bu guruh kursida o'quv dasturi yo'q.
      </Card>
    )
  }

  const pct = curr.totalItems > 0 ? Math.round((curr.coveredCount / curr.totalItems) * 100) : 0

  return (
    <Card className="p-0">
      <div className="flex flex-wrap items-center justify-between gap-3 border-b border-slate-100 px-4 py-3">
        <div className="flex items-center gap-2">
          <ListChecks className="h-5 w-5 text-brand-600" />
          <h2 className="font-semibold text-slate-800">O'quv dasturi (darsda o'tilgan)</h2>
        </div>
      </div>

      {/* PROGNOZ KARTASI */}
      <div className="border-b border-slate-100 px-4 py-4">
        {/* Progress bar */}
        <div className="mb-4">
          <div className="mb-1.5 flex items-center justify-between text-sm">
            <span className="font-medium text-slate-600">Bajarildi</span>
            <span className="font-mono font-semibold text-brand-700">
              {curr.coveredCount}/{curr.totalItems} · {pct}%
            </span>
          </div>
          <div className="h-2.5 w-full overflow-hidden rounded-full bg-slate-100">
            <div
              className="h-full rounded-full bg-brand-500 transition-all"
              style={{ width: `${pct}%` }}
            />
          </div>
        </div>

        {/* Stat plitalar */}
        <div className="grid grid-cols-2 gap-3 sm:grid-cols-4">
          <ForecastTile icon={CheckCircle2} label="O'tilgan">
            <span className="font-mono">{curr.coveredCount}</span>
            <span className="text-slate-400">/{curr.totalItems}</span>
          </ForecastTile>
          <ForecastTile icon={Repeat} label="Takrorlash darslari">
            <span className="font-mono">{curr.revisionLessons}</span>
          </ForecastTile>
          <ForecastTile icon={Flag} label="Qolgan">
            <span className="font-mono">{curr.remainingItems}</span> band
          </ForecastTile>
          <ForecastTile icon={CalendarClock} label="Tugatishga">
            <span className="text-slate-500">~</span>
            <span className="font-mono">{curr.estLessonsLeft}</span> dars
            {curr.estFinishDate && (
              <span className="mt-0.5 block text-xs font-normal text-slate-400">
                ≈ {formatDate(curr.estFinishDate)} da tugaydi
              </span>
            )}
          </ForecastTile>
        </div>

        {/* Takrorlash darsi tugmalari */}
        <div className="mt-4 flex items-center gap-2">
          <button
            type="button"
            disabled={revSaving}
            onClick={() => onChangeRevision(1)}
            className="inline-flex items-center gap-1.5 rounded-lg bg-brand-50 px-3 py-2 text-sm font-medium text-brand-700 transition-colors hover:bg-brand-100 disabled:opacity-50"
          >
            <Plus className="h-4 w-4" /> Takrorlash darsi
          </button>
          <button
            type="button"
            disabled={revSaving || curr.revisionLessons <= 0}
            onClick={() => onChangeRevision(-1)}
            title="Oxirgi takrorlash darsini olib tashlash"
            className="inline-flex items-center justify-center rounded-lg border border-slate-200 bg-white p-2 text-slate-500 transition-colors hover:bg-slate-50 disabled:opacity-40"
          >
            <Minus className="h-4 w-4" />
          </button>
          <span className="text-xs text-slate-400">
            Jami darslar: <span className="font-mono">{curr.totalLessons}</span>
            {curr.lessonsPerWeek > 0 && (
              <> · haftasiga <span className="font-mono">{curr.lessonsPerWeek}</span> dars</>
            )}
          </span>
        </div>
      </div>

      {/* DASTUR DARAXTI — modullar → mavzular (default yopiq) */}
      <div className="space-y-4 p-4">
        {curr.modules.map((module) => {
          const mItems = module.topics.flatMap((tp) => tp.items)
          const mCovered = mItems.filter((it) => it.covered).length
          const moduleOpen = expanded.has(module.id)
          return (
            <div key={module.id} className="overflow-hidden rounded-xl border border-slate-300 bg-slate-50/40">
              <button
                type="button"
                onClick={() => onToggleTopic(module.id)}
                className="flex w-full items-center gap-2 bg-slate-100/70 px-3 py-2.5 text-left transition-colors hover:bg-slate-200/60"
              >
                <span className="flex h-7 w-7 flex-shrink-0 items-center justify-center rounded-lg text-slate-500">
                  {moduleOpen ? <ChevronDown className="h-4 w-4" /> : <ChevronRight className="h-4 w-4" />}
                </span>
                <span className="min-w-0 flex-1 truncate text-sm font-bold text-slate-800">{module.name}</span>
                <span
                  className={cn(
                    'flex-shrink-0 rounded-full px-2.5 py-1 text-xs font-medium',
                    mItems.length > 0 && mCovered === mItems.length
                      ? 'bg-emerald-50 text-emerald-700'
                      : 'bg-slate-200 text-slate-600',
                  )}
                >
                  <span className="font-mono">{mCovered}/{mItems.length}</span>
                </span>
              </button>
              {moduleOpen && (
                <div className="space-y-3 p-3">
                  {module.topics.map((topic) => {
                    const tCovered = topic.items.filter((it) => it.covered).length
                    const open = expanded.has(topic.id)
                    return (
                      <div
                        key={topic.id}
                        className="overflow-hidden rounded-xl border border-slate-200 bg-white shadow-[var(--shadow-1)]"
                      >
                        {/* Mavzu sarlavhasi */}
                        <button
                          type="button"
                          onClick={() => onToggleTopic(topic.id)}
                          className="flex w-full items-center gap-2 border-b border-slate-100 bg-slate-50/60 px-3 py-2.5 text-left transition-colors hover:bg-slate-100/60"
                        >
                <span className="flex h-7 w-7 flex-shrink-0 items-center justify-center rounded-lg text-slate-400">
                  {open ? <ChevronDown className="h-4 w-4" /> : <ChevronRight className="h-4 w-4" />}
                </span>
                <span className="flex h-8 w-8 flex-shrink-0 items-center justify-center rounded-lg bg-brand-50 text-brand-600">
                  <BookOpen className="h-4 w-4" />
                </span>
                <span className="min-w-0 flex-1 truncate text-sm font-semibold text-slate-800">
                  {topic.title}
                </span>
                <span
                  className={cn(
                    'flex-shrink-0 rounded-full px-2.5 py-1 text-xs font-medium',
                    topic.items.length > 0 && tCovered === topic.items.length
                      ? 'bg-emerald-50 text-emerald-700'
                      : 'bg-slate-100 text-slate-500',
                  )}
                >
                  <span className="font-mono">{tCovered}/{topic.items.length}</span>
                </span>
              </button>

              {open && (
                <div className="p-3">
                  {topic.note && <p className="mb-2 px-1 text-xs text-slate-400">{topic.note}</p>}
                  {topic.items.length === 0 ? (
                    <p className="px-1 text-xs text-slate-400">Topshiriq yo'q.</p>
                  ) : (
                    <div className="grid grid-cols-2 gap-x-3 gap-y-1">
                      {topic.items.map((item) => {
                        const isNext = item.id === nextItemId
                        return (
                          <label
                            key={item.id}
                            className={cn(
                              'flex cursor-pointer items-start gap-2 rounded-md px-2 py-1.5 transition-colors',
                              item.covered
                                ? 'bg-emerald-50/50'
                                : isNext
                                  ? 'bg-brand-50/60 ring-1 ring-brand-200'
                                  : 'hover:bg-slate-50',
                            )}
                          >
                            <input
                              type="checkbox"
                              checked={item.covered}
                              onChange={() => onToggleCover(item.id, !item.covered)}
                              className="mt-0.5 h-4 w-4 flex-shrink-0 cursor-pointer rounded border-slate-300 text-brand-600 focus:ring-brand-400"
                            />
                            <span
                              className={cn(
                                'min-w-0 flex-1 text-sm',
                                item.covered
                                  ? 'text-slate-400 line-through'
                                  : 'text-slate-700',
                              )}
                            >
                              {item.text}
                              {isNext && (
                                <span className="ml-2 rounded bg-brand-100 px-1.5 py-0.5 text-[10px] font-medium text-brand-700 no-underline">
                                  keyingi
                                </span>
                              )}
                            </span>
                            {item.covered && item.coveredDate && (
                              <span className="ml-auto flex-shrink-0 self-center whitespace-nowrap rounded bg-slate-100 px-1.5 py-0.5 text-[11px] font-mono text-slate-400 no-underline">
                                {formatDate(item.coveredDate)}
                              </span>
                            )}
                          </label>
                        )
                      })}
                    </div>
                  )}
                </div>
              )}
            </div>
                    )
                  })}
                </div>
              )}
            </div>
          )
        })}
      </div>
    </Card>
  )
}

function ForecastTile({
  icon: Icon,
  label,
  children,
}: {
  icon: typeof BookOpen
  label: string
  children: ReactNode
}) {
  return (
    <div className="rounded-xl border border-slate-200 bg-white p-3">
      <div className="mb-1 flex items-center gap-1.5 text-[11px] font-semibold uppercase tracking-wide text-slate-400">
        <Icon className="h-3.5 w-3.5" />
        {label}
      </div>
      <div className="text-sm font-semibold text-slate-700">{children}</div>
    </div>
  )
}
