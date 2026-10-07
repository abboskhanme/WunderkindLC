import { useEffect, useState } from 'react'
import { Check, Loader2, MessageSquare, Pause, Pencil, Phone, Play, Trash2, User } from 'lucide-react'
import type { EmployeeOnboarding, OnboardingTask } from '@/api/services/onboarding'
import {
  cancelOnboarding, deleteOnboarding, getOnboarding, reopenOnboarding, setOnboardingTask,
} from '@/api/services/onboarding'
import { Modal } from '@/components/ui/Modal'
import { Button } from '@/components/ui/Button'
import { Badge } from '@/components/ui/Badge'
import { Loader } from '@/components/ui/Loader'
import { apiErrorMessage, cn, formatDate, formatDateTime } from '@/lib/utils'
import { formatPhone } from '@/lib/numberFormat'
import { dueText, progressBarClass, statusLabels, statusTones, todayIso } from './onboardingModel'

interface Props {
  onboardingId: string
  canEdit: boolean
  canDelete: boolean
  onClose: () => void
  /** Moslashuv o'zgardi (qadam belgilandi, holat o'zgardi) — ro'yxat yangilanadi. */
  onChanged: (o: EmployeeOnboarding) => void
  onDeleted: (id: string) => void
  /** Sarlavhani tahrirlash oynasini ochish. */
  onEdit: (o: EmployeeOnboarding) => void
}

/**
 * MOSLASHUV TAFSILOTI — 30 kunlik cheklist: har qadam belgilanadi (kim, qachon), muddati va
 * kechikishi ko'rinadi, izoh yoziladi. Oxirgi qadam belgilansa server moslashuvni o'zi
 * "yakunlandi" qiladi. Belgilash optimistik EMAS — javob kelgach yangilanadi (xato bo'lsa
 * holat o'zgarmaydi, sabab qizil satrda).
 * Oyna har ochilishda `key` bilan qayta yaratiladi.
 */
export function OnboardingDetailModal({
  onboardingId, canEdit, canDelete, onClose, onChanged, onDeleted, onEdit,
}: Props) {
  const [item, setItem] = useState<EmployeeOnboarding | null>(null)
  const [loadError, setLoadError] = useState('')
  const [busyTask, setBusyTask] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState('')
  const [noteFor, setNoteFor] = useState<string | null>(null)
  const [noteText, setNoteText] = useState('')
  const today = todayIso()

  useEffect(() => {
    let alive = true
    getOnboarding(onboardingId)
      .then((o) => {
        if (alive) setItem(o)
      })
      .catch((err) => {
        if (alive) setLoadError(apiErrorMessage(err, "Moslashuvni yuklab bo'lmadi"))
      })
    return () => {
      alive = false
    }
  }, [onboardingId])

  const apply = (o: EmployeeOnboarding) => {
    setItem(o)
    onChanged(o)
  }

  const saveTask = async (t: OnboardingTask, done: boolean, note: string) => {
    if (!item || busyTask) return
    setBusyTask(t.id)
    setError('')
    try {
      apply(await setOnboardingTask(item.id, t.id, done, note))
      setNoteFor(null)
    } catch (err) {
      setError(apiErrorMessage(err, "Qadamni saqlab bo'lmadi"))
    } finally {
      setBusyTask(null)
    }
  }

  const run = async (fn: () => Promise<EmployeeOnboarding>, fallback: string) => {
    if (busy) return
    setBusy(true)
    setError('')
    try {
      apply(await fn())
    } catch (err) {
      setError(apiErrorMessage(err, fallback))
    } finally {
      setBusy(false)
    }
  }

  const remove = async () => {
    if (!item || busy) return
    if (!window.confirm(`${item.fullName} moslashuvi butunlay o'chirilsinmi? Odatda «To'xtatish» afzal.`)) return
    setBusy(true)
    setError('')
    try {
      await deleteOnboarding(item.id)
      onDeleted(item.id)
      onClose()
    } catch (err) {
      setError(apiErrorMessage(err, "O'chirib bo'lmadi"))
    } finally {
      setBusy(false)
    }
  }

  const tasks = item?.tasks ?? []
  const cancelled = item?.status === 'cancelled'

  return (
    <Modal
      open
      onClose={onClose}
      size="lg"
      title={item ? `Moslashuv — ${item.fullName}` : 'Moslashuv'}
      footer={
        item && (canEdit || canDelete) ? (
          <div className="flex w-full flex-wrap justify-between gap-2">
            <div>
              {canDelete && (
                <Button variant="danger" onClick={remove} disabled={busy}>
                  <Trash2 className="h-4 w-4" /> O'chirish
                </Button>
              )}
            </div>
            {canEdit && (
              <div className="flex flex-wrap gap-2">
                <Button variant="secondary" onClick={() => onEdit(item)} disabled={busy}>
                  <Pencil className="h-4 w-4" /> Tahrirlash
                </Button>
                {cancelled ? (
                  <Button onClick={() => run(() => reopenOnboarding(item.id), "Qayta ochib bo'lmadi")} disabled={busy}>
                    <Play className="h-4 w-4" /> Qayta ochish
                  </Button>
                ) : (
                  <Button
                    variant="secondary"
                    onClick={() => {
                      if (window.confirm(`${item.fullName} moslashuvi to'xtatilsinmi? (xodim ketdi / xato boshlangan)`))
                        void run(() => cancelOnboarding(item.id), "To'xtatib bo'lmadi")
                    }}
                    disabled={busy}
                  >
                    <Pause className="h-4 w-4" /> To'xtatish
                  </Button>
                )}
              </div>
            )}
          </div>
        ) : undefined
      }
    >
      {loadError ? (
        <p className="text-sm font-medium text-red-600">{loadError}</p>
      ) : !item ? (
        <Loader label="Yuklanmoqda..." />
      ) : (
        <div className="space-y-4">
          {error && <p className="text-sm font-medium text-red-600">{error}</p>}

          {/* ---------- Xodim ---------- */}
          <div className="rounded-xl border border-slate-200 p-4">
            <div className="mb-3 flex flex-wrap items-center justify-between gap-2">
              <div className="flex items-center gap-2">
                <User className="h-4 w-4 text-slate-400" />
                <span className="font-semibold text-slate-800">{item.fullName}</span>
                {item.position && <span className="text-sm text-slate-500">· {item.position}</span>}
              </div>
              <Badge tone={statusTones[item.status]}>{statusLabels[item.status]}</Badge>
            </div>
            <div className="grid gap-1.5 text-sm text-slate-500 sm:grid-cols-2">
              {item.phone && (
                <a href={`tel:${item.phone}`} className="inline-flex items-center gap-1.5 text-brand-600 hover:underline">
                  <Phone className="h-3.5 w-3.5" /> {formatPhone(item.phone)}
                </a>
              )}
              <span>
                Boshlangan: <b className="text-slate-700">{formatDate(item.startDate)}</b>
                {item.status === 'active' && item.dayNumber > 0 && <> · {item.dayNumber}-kun</>}
              </span>
              <span>Mentor: <b className="text-slate-700">{item.mentorName || '—'}</b></span>
              <span>
                Manba:{' '}
                {item.source === 'hired' && item.applicationNumber > 0
                  ? `ariza #${item.applicationNumber} (ishga qabul qilindi)`
                  : "qo'lda boshlangan"}
              </span>
            </div>
            {item.note && <p className="mt-2 whitespace-pre-line text-sm text-slate-600">{item.note}</p>}

            <div className="mt-3">
              <div className="mb-1 flex justify-between text-xs text-slate-500">
                <span>
                  {item.doneCount}/{item.total} qadam bajarildi
                  {item.overdueCount > 0 && (
                    <span className="ml-2 font-semibold text-red-600">{item.overdueCount} ta kechikkan</span>
                  )}
                </span>
                <span className="font-semibold text-slate-700">{item.progressPct}%</span>
              </div>
              <div className="h-2 overflow-hidden rounded-full bg-slate-100">
                <div
                  className={cn('h-full rounded-full', progressBarClass(item.progressPct, item.overdueCount))}
                  style={{ width: `${item.progressPct}%` }}
                />
              </div>
            </div>
          </div>

          {cancelled && (
            <p className="rounded-lg bg-slate-50 px-3 py-2 text-xs text-slate-500">
              Moslashuv to'xtatilgan — qadamlarni belgilash uchun avval «Qayta ochish».
            </p>
          )}

          {/* ---------- Cheklist ---------- */}
          {tasks.length === 0 ? (
            <p className="text-sm text-slate-500">Cheklistda qadam yo'q (katalog bo'sh bo'lgan).</p>
          ) : (
            <ol className="space-y-2">
              {tasks.map((t, i) => {
                const disabled = !canEdit || cancelled || busyTask != null
                return (
                  <li
                    key={t.id}
                    className={cn(
                      'rounded-xl border p-3',
                      t.overdue ? 'border-red-200 bg-red-50/40' : t.done ? 'border-emerald-100 bg-emerald-50/30' : 'border-slate-200',
                    )}
                  >
                    <div className="flex items-start gap-3">
                      <button
                        type="button"
                        role="checkbox"
                        aria-checked={t.done}
                        aria-label={t.title}
                        disabled={disabled}
                        onClick={() => saveTask(t, !t.done, t.note)}
                        className={cn(
                          'mt-0.5 flex h-5 w-5 flex-none items-center justify-center rounded-md border transition-colors disabled:cursor-default',
                          t.done ? 'border-emerald-500 bg-emerald-500 text-white' : 'border-slate-300 bg-white',
                          !disabled && !t.done && 'hover:border-brand-500',
                        )}
                      >
                        {busyTask === t.id ? (
                          <Loader2 className="h-3 w-3 animate-spin text-slate-400" />
                        ) : (
                          t.done && <Check className="h-3.5 w-3.5" />
                        )}
                      </button>
                      <div className="min-w-0 flex-1">
                        <div className="flex flex-wrap items-baseline justify-between gap-x-3 gap-y-0.5">
                          <p className={cn('text-sm font-semibold', t.done ? 'text-slate-500 line-through' : 'text-slate-800')}>
                            {i + 1}. {t.title}
                          </p>
                          <span
                            className={cn(
                              'text-xs',
                              t.overdue ? 'font-semibold text-red-600' : t.done ? 'text-emerald-600' : 'text-slate-400',
                            )}
                          >
                            {t.dayOffset}-kun · {formatDate(t.dueDate)}
                            {(() => {
                              const txt = dueText(t, today)
                              return txt && !t.done ? ` · ${txt}` : ''
                            })()}
                          </span>
                        </div>
                        {t.description && <p className="text-xs text-slate-500">{t.description}</p>}
                        {t.done && (
                          <p className="text-xs text-emerald-700">
                            ✓ {formatDateTime(t.doneAt)} · {t.doneBy}
                          </p>
                        )}
                        {t.note && noteFor !== t.id && (
                          <p className="mt-1 text-xs text-slate-600">📝 {t.note}</p>
                        )}
                        {noteFor === t.id ? (
                          <div className="mt-2 flex flex-wrap gap-2">
                            <input
                              value={noteText}
                              onChange={(e) => setNoteText(e.target.value)}
                              placeholder="Izoh (masalan: shartnoma №45)"
                              className="min-w-0 flex-1 rounded-lg border border-slate-200 px-2.5 py-1.5 text-xs outline-none focus:border-brand-400"
                              maxLength={1000}
                            />
                            <Button onClick={() => saveTask(t, t.done, noteText.trim())} disabled={busyTask != null}>
                              Saqlash
                            </Button>
                            <Button variant="ghost" onClick={() => setNoteFor(null)}>
                              Bekor
                            </Button>
                          </div>
                        ) : (
                          canEdit &&
                          !cancelled && (
                            <button
                              type="button"
                              className="mt-1 inline-flex items-center gap-1 text-[11px] font-semibold text-brand-600 hover:underline"
                              onClick={() => {
                                setNoteFor(t.id)
                                setNoteText(t.note)
                              }}
                            >
                              <MessageSquare className="h-3 w-3" /> {t.note ? 'Izohni tahrirlash' : 'Izoh'}
                            </button>
                          )
                        )}
                      </div>
                    </div>
                  </li>
                )
              })}
            </ol>
          )}
        </div>
      )}
    </Modal>
  )
}
