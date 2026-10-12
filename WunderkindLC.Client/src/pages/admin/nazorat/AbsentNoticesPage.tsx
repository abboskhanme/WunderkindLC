import { useEffect, useMemo, useState } from 'react'
import { Link } from 'react-router-dom'
import { BellOff, ChevronLeft, ChevronRight, RefreshCw } from 'lucide-react'
import {
  actOnAbsentNotice,
  bulkAbsentNotices,
  getAbsentNotices,
  type AbsentNoticeItem,
  type AbsentNoticeLesson,
  type AbsentNoticeList,
  type NoticeAction,
  type NoticeCountKey,
} from '@/api/services/attendanceNotices'
import { Badge } from '@/components/ui/Badge'
import { Button } from '@/components/ui/Button'
import { Card } from '@/components/ui/Card'
import { Loader } from '@/components/ui/Loader'
import { PageHeader } from '@/components/ui/PageHeader'
import {
  NOTICE_ACTION_LABELS,
  NOTICE_CHIPS,
  NOTICE_POLL_MS,
  NOTICE_STATUS_LABELS,
  NOTICE_STATUS_TONES,
  cancelReasonLabel,
  canApplyNotice,
  hhmm,
  isOpenNotice,
} from '@/lib/absentNotices'
import { localIsoDate, shiftLocalDay } from '@/lib/localDate'
import { usePerm } from '@/lib/permissions'
import { apiErrorMessage, cn } from '@/lib/utils'

const ACTIONS: NoticeAction[] = ['hold', 'approve', 'cancel']

const dateFromUrl = (): string => {
  const d = new URLSearchParams(window.location.search).get('date') ?? ''
  return /^\d{4}-\d{2}-\d{2}$/.test(d) ? d : localIsoDate(new Date())
}

/**
 * Nazorat → «Davomat xabarlari» (CRM Phase 3, U3b — SPEC P3.3). «Kelmadi» xabari ota-onaga dars TUGAGACH +
 * kechikish bilan ketadi; undan oldin menejer uni to'xtatadi, tasdiqlaydi yoki bekor qiladi. Davomat kech olingan
 * darsning «kelmadi» xabarlari tasdiq kutadi (hech kim tasdiqlamasa — yuborilmaydi, muddati o'tadi).
 * Sana manzildan (`?date=`, menejerga Telegram havolasi shu bilan keladi), bugun ko'rilsa har 30 s yangilanadi.
 */
export function AbsentNoticesPage() {
  const { can } = usePerm()
  const canEdit = can('students.notices', 'edit')
  const todayStr = localIsoDate(new Date())
  const [date, setDate] = useState(dateFromUrl)
  const [status, setStatus] = useState<NoticeCountKey | ''>('')
  const [data, setData] = useState<AbsentNoticeList | null>(null)
  const [error, setError] = useState('')
  const [tick, setTick] = useState(0)
  const [selected, setSelected] = useState<Set<string>>(new Set())
  const [note, setNote] = useState('')
  const [busy, setBusy] = useState(false)
  const [bulkError, setBulkError] = useState('')

  const key = JSON.stringify({ date, status, tick })
  useEffect(() => {
    const k = JSON.parse(key) as { date: string; status: NoticeCountKey | '' }
    let alive = true
    getAbsentNotices({ date: k.date, status: k.status })
      .then((d) => {
        if (!alive) return
        setData(d)
        setError('')
        // Tanlovdan endi ochiq bo'lmagan (yuborilgan/yopilgan) yoki ro'yxatdan chiqqanlari tushadi.
        const open = new Set(d.lessons.flatMap((l) => l.items).filter((i) => isOpenNotice(i.status)).map((i) => i.id))
        setSelected((prev) => new Set([...prev].filter((id) => open.has(id))))
      })
      // Sahifaning ASOSIY ma'lumoti — xato ko'rsatiladi (`error-visibility.md` §4).
      .catch((e) => alive && setError(apiErrorMessage(e, "Davomat xabarlarini yuklab bo'lmadi")))
    return () => {
      alive = false
    }
  }, [key])

  // Bugun — har 30 soniyada (xabarlar vaqti kelib ketadi, holatlar o'zgaradi).
  const isToday = date === todayStr
  useEffect(() => {
    if (!isToday) return
    const id = window.setInterval(() => setTick((t) => t + 1), NOTICE_POLL_MS)
    return () => window.clearInterval(id)
  }, [isToday])

  const changeDate = (next: string) => {
    if (!next || next > todayStr) return
    setDate(next)
    setSelected(new Set())
    setData(null)
  }

  const counts = data?.counts
  const totalAll = counts ? Object.values(counts).reduce((a, b) => a + b, 0) : 0
  const selectedItems = useMemo(
    () => (data?.lessons ?? []).flatMap((l) => l.items).filter((i) => selected.has(i.id)),
    [data, selected],
  )

  const toggle = (id: string) =>
    setSelected((prev) => {
      const next = new Set(prev)
      if (next.has(id)) next.delete(id)
      else next.add(id)
      return next
    })

  const toggleLesson = (lesson: AbsentNoticeLesson) => {
    const open = lesson.items.filter((i) => isOpenNotice(i.status)).map((i) => i.id)
    const all = open.length > 0 && open.every((id) => selected.has(id))
    setSelected((prev) => {
      const next = new Set(prev)
      for (const id of open) {
        if (all) next.delete(id)
        else next.add(id)
      }
      return next
    })
  }

  /** Qatordagi amal — xato `alert` bilan (ro'yxat qatori, `error-visibility.md` §2). */
  const runOne = async (item: AbsentNoticeItem, action: NoticeAction) => {
    if (action === 'cancel' && !window.confirm(`${item.studentName}: xabar BEKOR qilinsinmi? U hech qachon yuborilmaydi.`)) return
    setBusy(true)
    try {
      await actOnAbsentNotice(item.id, action)
      setTick((t) => t + 1)
    } catch (e) {
      alert(apiErrorMessage(e, "Amalni bajarib bo'lmadi"))
      setTick((t) => t + 1)
    } finally {
      setBusy(false)
    }
  }

  /** Ommaviy amal — xato panelda qoladi, izoh va tanlov saqlanadi. */
  const runBulk = async (action: NoticeAction) => {
    if (selectedItems.length === 0) return
    if (action === 'cancel' && !window.confirm(`${selectedItems.length} ta xabar BEKOR qilinsinmi?`)) return
    setBusy(true)
    setBulkError('')
    try {
      const r = await bulkAbsentNotices(selectedItems.map((i) => i.id), action, note.trim())
      setSelected(new Set())
      setNote('')
      if (r.skipped > 0) setBulkError(`${r.changed} ta o'zgardi, ${r.skipped} tasi o'tkazib yuborildi (allaqachon yuborilgan yoki yopilgan).`)
      setTick((t) => t + 1)
    } catch (e) {
      setBulkError(apiErrorMessage(e, "Amalni bajarib bo'lmadi"))
    } finally {
      setBusy(false)
    }
  }

  return (
    <div className="space-y-3">
      <PageHeader
        title="Davomat xabarlari"
        sub="«Kelmadi» xabari ota-onaga dars tugagach ketadi — undan oldin to'xtatish, tasdiqlash yoki bekor qilish mumkin. Davomat kech olingan darsning xabarlari tasdiq kutadi."
        actions={
          <div className="flex flex-wrap items-center gap-1.5">
            <button
              type="button"
              onClick={() => changeDate(shiftLocalDay(date, -1))}
              className="grid h-8 w-8 place-items-center rounded-lg border border-slate-200 bg-white text-slate-500 hover:bg-slate-50"
              aria-label="Oldingi kun"
            >
              <ChevronLeft className="h-4 w-4" />
            </button>
            <input
              type="date"
              value={date}
              max={todayStr}
              onChange={(e) => changeDate(e.target.value)}
              className="h-8 rounded-lg border border-slate-200 bg-white px-2 text-xs font-semibold text-slate-700 outline-none focus:border-blue-300"
              aria-label="Sana tanlash"
            />
            <button
              type="button"
              onClick={() => changeDate(shiftLocalDay(date, 1))}
              disabled={isToday}
              className="grid h-8 w-8 place-items-center rounded-lg border border-slate-200 bg-white text-slate-500 hover:bg-slate-50 disabled:cursor-not-allowed disabled:opacity-40"
              aria-label="Keyingi kun"
            >
              <ChevronRight className="h-4 w-4" />
            </button>
            {!isToday && (
              <Button variant="secondary" onClick={() => changeDate(todayStr)}>
                Bugun
              </Button>
            )}
            <Button variant="ghost" onClick={() => setTick((t) => t + 1)} aria-label="Yangilash">
              <RefreshCw className="h-3.5 w-3.5" /> Yangilash
            </Button>
          </div>
        }
      />

      <div className="flex flex-wrap gap-1.5">
        <Chip active={status === ''} onClick={() => setStatus('')} label="Hammasi" count={counts ? totalAll : null} />
        {NOTICE_CHIPS.map((c) => (
          <Chip
            key={c.key}
            active={status === c.key}
            onClick={() => setStatus(status === c.key ? '' : c.key)}
            label={c.label}
            count={counts ? counts[c.key] : null}
            warn={c.key === 'awaiting' && (counts?.awaiting ?? 0) > 0}
          />
        ))}
      </div>

      {canEdit && selectedItems.length > 0 && (
        <div className="sticky top-0 z-10 rounded-xl border border-brand-200 bg-brand-50/90 p-2.5 backdrop-blur">
          <div className="flex flex-wrap items-center gap-2">
            <span className="text-[13px] font-semibold text-brand-700">{selectedItems.length} ta tanlandi</span>
            <input
              value={note}
              onChange={(e) => setNote(e.target.value)}
              maxLength={300}
              placeholder="Izoh (ixtiyoriy)"
              className="h-8 min-w-[180px] flex-1 rounded-lg border border-slate-200 bg-white px-2 text-[13px] outline-none focus:border-brand-400"
            />
            {ACTIONS.map((a) => (
              <Button
                key={a}
                variant={a === 'cancel' ? 'danger' : a === 'approve' ? 'primary' : 'secondary'}
                disabled={busy || !selectedItems.some((i) => canApplyNotice(i.status, a))}
                onClick={() => runBulk(a)}
              >
                {NOTICE_ACTION_LABELS[a]}
              </Button>
            ))}
            <Button variant="ghost" disabled={busy} onClick={() => setSelected(new Set())}>
              Tanlovni tozalash
            </Button>
          </div>
          {bulkError && <p className="mt-1.5 text-[12px] font-medium text-red-600">{bulkError}</p>}
        </div>
      )}
      {canEdit && selectedItems.length === 0 && bulkError && (
        <p className="text-[12px] font-medium text-amber-700">{bulkError}</p>
      )}

      {error && <p className="rounded-lg bg-red-50 px-3 py-2 text-sm font-medium text-red-600">{error}</p>}

      {data === null && !error ? (
        <Loader label="Yuklanmoqda..." />
      ) : (data?.lessons ?? []).length === 0 ? (
        <Card>
          <div className="py-8 text-center">
            <BellOff className="mx-auto mb-2 h-6 w-6 text-slate-300" />
            <p className="text-sm text-slate-500">
              {status ? "Bu holatda xabar yo'q" : "Bu kunda davomat xabarlari yo'q"}
            </p>
            <p className="mt-1 text-[12px] text-slate-400">
              Xabar faqat «Davomat» avto-xabari yoqilgan bo'lsa va bugungi dars uchun yaratiladi.
            </p>
          </div>
        </Card>
      ) : (
        <div className="space-y-3">
          {data!.lessons.map((lesson) => (
            <LessonCard
              key={lesson.groupId}
              lesson={lesson}
              canEdit={canEdit}
              busy={busy}
              selected={selected}
              onToggle={toggle}
              onToggleLesson={() => toggleLesson(lesson)}
              onAction={runOne}
            />
          ))}
        </div>
      )}
    </div>
  )
}

function Chip({
  active,
  onClick,
  label,
  count,
  warn,
}: {
  active: boolean
  onClick: () => void
  label: string
  count: number | null
  warn?: boolean
}) {
  return (
    <button
      type="button"
      onClick={onClick}
      className={cn(
        'inline-flex items-center gap-1.5 rounded-full border px-3 py-1 text-[12px] font-semibold transition-colors',
        active
          ? 'border-brand-600 bg-brand-600 text-white'
          : warn
            ? 'border-amber-300 bg-amber-50 text-amber-700 hover:bg-amber-100'
            : 'border-slate-200 bg-white text-slate-600 hover:bg-slate-50',
      )}
    >
      {label}
      <span className={cn('rounded-full px-1.5 text-[11px]', active ? 'bg-white/25' : 'bg-slate-100 text-slate-600')}>
        {count ?? '…'}
      </span>
    </button>
  )
}

function LessonCard({
  lesson,
  canEdit,
  busy,
  selected,
  onToggle,
  onToggleLesson,
  onAction,
}: {
  lesson: AbsentNoticeLesson
  canEdit: boolean
  busy: boolean
  selected: Set<string>
  onToggle: (id: string) => void
  onToggleLesson: () => void
  onAction: (item: AbsentNoticeItem, action: NoticeAction) => void
}) {
  const open = lesson.items.filter((i) => isOpenNotice(i.status))
  const allSelected = open.length > 0 && open.every((i) => selected.has(i.id))
  const time = lesson.lessonStart ? `${lesson.lessonStart}${lesson.lessonEnd ? `–${lesson.lessonEnd}` : ''}` : 'vaqti noma\'lum'
  const taken = hhmm(lesson.attendanceTakenAt)

  return (
    <Card
      tight
      title={
        <span className="flex flex-wrap items-center gap-2">
          <Link to={`/admin/classes/${lesson.groupId}`} className="hover:underline">
            {lesson.groupName || 'Guruh'}
          </Link>
          {taken ? (
            <Badge tone={lesson.takenLate ? 'amber' : 'green'}>
              {lesson.takenLate ? `Davomat kech: ${taken}` : `Davomat: ${taken}`}
            </Badge>
          ) : (
            <Badge>Davomat olinmagan</Badge>
          )}
        </span>
      }
      sub={[lesson.courseName, lesson.teacherName, time].filter(Boolean).join(' · ')}
      actions={
        canEdit && open.length > 0 ? (
          <label className="flex cursor-pointer items-center gap-1.5 text-[12px] font-medium text-slate-600">
            <input type="checkbox" checked={allSelected} onChange={onToggleLesson} className="h-4 w-4 accent-brand-600" />
            Barchasini tanlash
          </label>
        ) : undefined
      }
    >
      <ul className="divide-y divide-slate-100">
        {lesson.items.map((item) => (
          <NoticeRow
            key={item.id}
            item={item}
            canEdit={canEdit}
            busy={busy}
            checked={selected.has(item.id)}
            onToggle={() => onToggle(item.id)}
            onAction={(a) => onAction(item, a)}
          />
        ))}
      </ul>
    </Card>
  )
}

function NoticeRow({
  item,
  canEdit,
  busy,
  checked,
  onToggle,
  onAction,
}: {
  item: AbsentNoticeItem
  canEdit: boolean
  busy: boolean
  checked: boolean
  onToggle: () => void
  onAction: (a: NoticeAction) => void
}) {
  const open = isOpenNotice(item.status)
  const when =
    item.status === 'done'
      ? 'Yuborildi'
      : open && item.status !== 'held' && item.status !== 'awaiting'
        ? `Yuboriladi: ${hhmm(item.sendAfter)}`
        : open
          ? `Vaqti: ${hhmm(item.sendAfter)}`
          : ''
  const d = item.delivery
  return (
    <li className="flex flex-wrap items-center gap-x-3 gap-y-1.5 px-4 py-2.5">
      {canEdit && (
        <input
          type="checkbox"
          checked={checked}
          disabled={!open}
          onChange={onToggle}
          className="h-4 w-4 accent-brand-600 disabled:opacity-30"
          aria-label={`${item.studentName} — tanlash`}
        />
      )}
      <div className="min-w-[160px] flex-1">
        <Link to={`/admin/students/${item.studentId}`} className="text-[13px] font-semibold text-slate-800 hover:underline">
          {item.studentName || "O'quvchi"}
        </Link>
        <p className="text-[12px] text-slate-500">
          {item.kind === 'present' ? (
            <span className="font-medium text-emerald-700">«Keldi» xabari</span>
          ) : (
            <>
              Kelmadi{item.reasonName ? ` — ${item.reasonName}` : ''}
              {item.isExcused && <span className="text-slate-400"> (sababli)</span>}
            </>
          )}
          {item.decidedByName && (
            <span className="text-slate-400">
              {' '}· {item.decidedByName} {hhmm(item.decidedAt)}
            </span>
          )}
          {item.note && <span className="text-slate-400"> · «{item.note}»</span>}
          {item.status === 'cancelled' && cancelReasonLabel(item.cancelReason) && (
            <span className="text-slate-400"> · {cancelReasonLabel(item.cancelReason)}</span>
          )}
        </p>
      </div>
      <div className="flex flex-col items-end gap-0.5 text-right">
        <Badge tone={NOTICE_STATUS_TONES[item.status]}>{NOTICE_STATUS_LABELS[item.status]}</Badge>
        {when && <span className="text-[11px] text-slate-500">{when}</span>}
        {d && (
          <span className="text-[11px] text-slate-400">
            Telegram: {d.sent} yetdi
            {d.failed > 0 && `, ${d.failed} xato`}
            {d.blocked > 0 && `, ${d.blocked} bloklagan`}
            {d.acked > 0 && `, ${d.acked} qabul qildi`}
          </span>
        )}
      </div>
      {canEdit && open && (
        <div className="flex flex-wrap gap-1">
          {ACTIONS.filter((a) => canApplyNotice(item.status, a)).map((a) => (
            <Button
              key={a}
              variant={a === 'cancel' ? 'ghost' : a === 'approve' ? 'primary' : 'secondary'}
              className={cn(a === 'cancel' && 'text-red-600 hover:bg-red-50')}
              disabled={busy}
              onClick={() => onAction(a)}
            >
              {NOTICE_ACTION_LABELS[a]}
            </Button>
          ))}
        </div>
      )}
    </li>
  )
}
