import { useEffect, useState } from 'react'
import { useNavigate } from 'react-router-dom'
import {
  ArrowLeft,
  CalendarClock,
  Plus,
  Trash2,
  CheckCircle2,
  X,
  ChevronLeft,
  ChevronRight,
} from 'lucide-react'
import {
  getMySupportSlots,
  addSupportSlot,
  deleteSupportSlot,
  completeSupportSlot,
  type SupportSlot,
} from '@/api/services/support'
import { Loader } from '@/components/ui/Loader'
import { formatDate } from '@/lib/utils'

/**
 * O'qituvchi (support) — "Support". O'qituvchi bo'sh vaqt bloklarini qo'shadi
 * (har odamga ajratilgan davomiylik bo'yicha avtomatik slotlarga bo'linadi);
 * o'quvchilar bron qiladi; dars o'tilgach mavzu + izoh bilan yopiladi.
 */

const SLOT_MINUTE_OPTIONS = [
  { value: 30, label: '30 daqiqa' },
  { value: 45, label: '45 daqiqa' },
  { value: 60, label: '60 daqiqa' },
  { value: 90, label: '90 daqiqa' },
  { value: 0, label: 'Butun blok' },
]

type AddMode = 'today' | 'pick' | 'daily'

const MODE_TABS: { value: AddMode; label: string }[] = [
  { value: 'today', label: 'Bugun' },
  { value: 'pick', label: 'Kun tanlash' },
  { value: 'daily', label: 'Har kuni' },
]

function todayStr(): string {
  const d = new Date()
  return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`
}

function currentMonthStr(): string {
  const d = new Date()
  return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}`
}

function lastDayOfMonth(ym: string): string {
  const [y, m] = ym.split('-').map(Number)
  const last = new Date(y, m, 0)
  return `${y}-${String(m).padStart(2, '0')}-${String(last.getDate()).padStart(2, '0')}`
}

/** "yyyy-MM-dd" + delta kun → "yyyy-MM-dd" (oy/yil chegarasidan mustaqil). */
function addDays(dateStr: string, delta: number): string {
  const [y, m, d] = dateStr.split('-').map(Number)
  const dt = new Date(y, m - 1, d + delta)
  return `${dt.getFullYear()}-${String(dt.getMonth() + 1).padStart(2, '0')}-${String(dt.getDate()).padStart(2, '0')}`
}

const WEEKDAY_NAMES = ['yakshanba', 'dushanba', 'seshanba', 'chorshanba', 'payshanba', 'juma', 'shanba']

/** Kun sarlavhasi: bugun/ertaga/kecha bo'lsa so'z bilan, aks holda "kun, DD.MM" (hafta kuni bilan). */
function dayLabel(dateStr: string, today: string): string {
  if (dateStr === today) return 'Bugun'
  if (dateStr === addDays(today, 1)) return 'Ertaga'
  if (dateStr === addDays(today, -1)) return 'Kecha'
  const [y, m, d] = dateStr.split('-').map(Number)
  const weekday = WEEKDAY_NAMES[new Date(y, m - 1, d).getDay()]
  return `${formatDate(dateStr)}, ${weekday}`
}

function statusChip(status: SupportSlot['status']): { label: string; cls: string } {
  switch (status) {
    case 'done':
      return { label: "O'tildi", cls: 'bg-tealsoft text-teal-700' }
    case 'booked':
      return { label: 'Support bron', cls: 'bg-amber-100 text-amber-700' }
    default:
      return { label: "Bo'sh", cls: 'bg-slate-100 text-mute' }
  }
}

export function TeacherSupportPage() {
  const nav = useNavigate()
  const today = todayStr()

  // Oy navigatsiyasi (ma'lumot shu oy bo'yicha yuklanadi — backend faqat oy bo'yicha filtrlaydi)
  const [viewMonth, setViewMonth] = useState(currentMonthStr())
  const [loading, setLoading] = useState(true)
  const [slots, setSlots] = useState<SupportSlot[]>([])

  // Kunlik navigatsiya — "Support bronlari" ro'yxati doim TANLANGAN kun uchun (default: bugun),
  // keyin-oldingi kun tugmalari bilan almashtiriladi. Oy chegarasidan o'tsa — kerakli oy avtomatik yuklanadi.
  const [focusDay, setFocusDay] = useState(today)
  function shiftDay(delta: number) {
    const next = addDays(focusDay, delta)
    setFocusDay(next)
    const nextMonth = next.slice(0, 7)
    if (nextMonth !== viewMonth) setViewMonth(nextMonth)
  }
  function goToday() {
    setFocusDay(today)
    if (today.slice(0, 7) !== viewMonth) setViewMonth(today.slice(0, 7))
  }

  // Qo'shish forma
  const [addMode, setAddMode] = useState<AddMode>('today')
  const [pickDate, setPickDate] = useState(today)
  const [dailyStart, setDailyStart] = useState(today)
  const [dailyEnd, setDailyEnd] = useState(lastDayOfMonth(currentMonthStr()))
  const [startTime, setStartTime] = useState('')
  const [endTime, setEndTime] = useState('')
  const [slotMinutes, setSlotMinutes] = useState(30)
  const [saving, setSaving] = useState(false)
  const [message, setMessage] = useState('')

  // Yopish (complete) holati
  const [closingId, setClosingId] = useState<string | null>(null)
  const [topic, setTopic] = useState('')
  const [notes, setNotes] = useState('')
  const [closing, setClosing] = useState(false)

  async function load(month: string) {
    setLoading(true)
    try {
      const data = await getMySupportSlots(month)
      setSlots(data)
    } catch {
      setSlots([])
    } finally {
      setLoading(false)
    }
  }

  useEffect(() => {
    load(viewMonth)
  }, [viewMonth])

  // "Har kuni" rejimida oy o'zgarganda tugash sanasini oy oxiriga o'rnat
  useEffect(() => {
    if (addMode === 'daily') {
      setDailyEnd(lastDayOfMonth(viewMonth))
    }
  }, [viewMonth, addMode])

  async function handleAdd() {
    if (!startTime || !endTime) {
      alert("Vaqtni to'ldiring")
      return
    }
    if (endTime <= startTime) {
      alert("Tugash vaqti boshlanish vaqtidan keyin bo'lishi kerak")
      return
    }

    const payload: Parameters<typeof addSupportSlot>[0] = {
      date: today,
      startTime,
      endTime,
      slotMinutes,
    }

    if (addMode === 'pick') {
      if (!pickDate) { alert("Sanani tanlang"); return }
      payload.date = pickDate
    } else if (addMode === 'daily') {
      if (!dailyStart || !dailyEnd) { alert("Sanalarni to'ldiring"); return }
      if (dailyEnd < dailyStart) {
        alert("Tugash sanasi boshlanish sanasidan keyin bo'lishi kerak")
        return
      }
      payload.date = dailyStart
      payload.repeatMode = 'daily'
      payload.endDate = dailyEnd
    }

    setSaving(true)
    setMessage('')
    try {
      const res = await addSupportSlot(payload)
      setMessage(`${res.created} ta slot qo'shildi`)
      setStartTime('')
      setEndTime('')
      await load(viewMonth)
    } catch {
      alert("Qo'shishda xatolik yuz berdi")
    } finally {
      setSaving(false)
    }
  }

  async function handleDelete(id: string) {
    if (!confirm("Slotni o'chirasizmi?")) return
    try {
      await deleteSupportSlot(id)
      await load(viewMonth)
    } catch {
      alert("O'chirishda xatolik yuz berdi")
    }
  }

  function startClose(id: string) {
    setClosingId(id)
    setTopic('')
    setNotes('')
  }

  async function handleComplete() {
    if (!closingId) return
    if (!topic.trim()) { alert("Mavzuni kiriting"); return }
    setClosing(true)
    try {
      await completeSupportSlot(closingId, topic.trim(), notes.trim())
      setClosingId(null)
      await load(viewMonth)
    } catch {
      alert("Yopishda xatolik yuz berdi")
    } finally {
      setClosing(false)
    }
  }

  // Tanlangan kunning vaqtlari (soat bo'yicha) — "Support bronlari" doim shu kun uchun ko'rsatiladi.
  const daySlots = slots
    .filter((s) => s.date === focusDay)
    .sort((a, b) => (a.startTime < b.startTime ? -1 : a.startTime > b.startTime ? 1 : 0))

  return (
    <div className="px-4 pt-3 pb-6">
      {/* Sarlavha */}
      <div className="mb-4 flex items-center gap-2.5">
        <button
          type="button"
          onClick={() => nav('/teacher/profile')}
          className="tap-scale flex h-9 w-9 shrink-0 items-center justify-center rounded-xl border border-line bg-white text-mute shadow-[var(--shadow-card)]"
        >
          <ArrowLeft className="h-5 w-5" />
        </button>
        <p className="text-[17px] font-extrabold text-ink">Support</p>
      </div>

      {/* Bo'lim 1 — Bo'sh vaqt qo'shish */}
      <div className="rounded-[20px] border border-line bg-white p-4 shadow-[var(--shadow-card)]">
        <div className="mb-3 flex items-center gap-2.5">
          <div className="flex h-10 w-10 shrink-0 items-center justify-center rounded-xl bg-tealsoft text-teal-700">
            <CalendarClock className="h-5 w-5" />
          </div>
          <p className="text-[14px] font-bold text-ink">Bo'sh vaqt qo'shish</p>
        </div>

        {/* Rejim tanlash (segment) */}
        <div className="mb-3 flex gap-1 rounded-[14px] border border-line bg-slate-50 p-1">
          {MODE_TABS.map((tab) => (
            <button
              key={tab.value}
              type="button"
              onClick={() => setAddMode(tab.value)}
              className={`flex-1 rounded-[10px] py-1.5 text-[12px] font-bold transition-colors ${
                addMode === tab.value ? 'bg-teal-600 text-white shadow-sm' : 'text-mute'
              }`}
            >
              {tab.label}
            </button>
          ))}
        </div>

        <div className="space-y-3">
          {/* Bugun — sana ko'rsatiladi, o'zgartirilmaydi */}
          {addMode === 'today' && (
            <div>
              <label className="mb-1 block text-[12px] font-semibold text-faint">Sana</label>
              <div className="w-full rounded-[14px] border border-line bg-slate-50 px-3 py-2.5 text-[14px] text-mute">
                {formatDate(today)}
              </div>
            </div>
          )}

          {/* Kun tanlash — date input */}
          {addMode === 'pick' && (
            <div>
              <label className="mb-1 block text-[12px] font-semibold text-faint">Sana</label>
              <input
                type="date"
                value={pickDate}
                onChange={(e) => setPickDate(e.target.value)}
                className="w-full rounded-[14px] border border-line bg-white px-3 py-2.5 text-[14px] text-ink outline-none focus:border-teal-500"
              />
            </div>
          )}

          {/* Har kuni — boshlanish + tugash sanasi */}
          {addMode === 'daily' && (
            <div className="grid grid-cols-2 gap-3">
              <div>
                <label className="mb-1 block text-[12px] font-semibold text-faint">Boshlanish sanasi</label>
                <input
                  type="date"
                  value={dailyStart}
                  onChange={(e) => setDailyStart(e.target.value)}
                  className="w-full rounded-[14px] border border-line bg-white px-3 py-2.5 text-[14px] text-ink outline-none focus:border-teal-500"
                />
              </div>
              <div>
                <label className="mb-1 block text-[12px] font-semibold text-faint">Tugash sanasi</label>
                <input
                  type="date"
                  value={dailyEnd}
                  onChange={(e) => setDailyEnd(e.target.value)}
                  className="w-full rounded-[14px] border border-line bg-white px-3 py-2.5 text-[14px] text-ink outline-none focus:border-teal-500"
                />
              </div>
            </div>
          )}

          {/* Vaqtlar */}
          <div className="grid grid-cols-2 gap-3">
            <div>
              <label className="mb-1 block text-[12px] font-semibold text-faint">Boshlanish</label>
              <input
                type="time"
                value={startTime}
                onChange={(e) => setStartTime(e.target.value)}
                className="w-full rounded-[14px] border border-line bg-white px-3 py-2.5 text-[14px] text-ink outline-none focus:border-teal-500"
              />
            </div>
            <div>
              <label className="mb-1 block text-[12px] font-semibold text-faint">Tugash</label>
              <input
                type="time"
                value={endTime}
                onChange={(e) => setEndTime(e.target.value)}
                className="w-full rounded-[14px] border border-line bg-white px-3 py-2.5 text-[14px] text-ink outline-none focus:border-teal-500"
              />
            </div>
          </div>

          {/* Har odamga */}
          <div>
            <label className="mb-1 block text-[12px] font-semibold text-faint">Har odamga (daqiqa)</label>
            <select
              value={slotMinutes}
              onChange={(e) => setSlotMinutes(Number(e.target.value))}
              className="w-full rounded-[14px] border border-line bg-white px-3 py-2.5 text-[14px] text-ink outline-none focus:border-teal-500"
            >
              {SLOT_MINUTE_OPTIONS.map((o) => (
                <option key={o.value} value={o.value}>
                  {o.label}
                </option>
              ))}
            </select>
          </div>

          {message && (
            <p className="rounded-[12px] bg-tealsoft px-3 py-2 text-[13px] font-semibold text-teal-700">
              {message}
            </p>
          )}

          <button
            type="button"
            onClick={handleAdd}
            disabled={saving}
            className="tap-scale flex w-full items-center justify-center gap-1.5 rounded-[14px] bg-teal-600 px-4 py-3 text-[14px] font-bold text-white disabled:opacity-60"
          >
            <Plus className="h-4 w-4" />
            {saving ? "Qo'shilmoqda..." : "Qo'shish"}
          </button>
        </div>
      </div>

      {/* Bo'lim 2 — Support bronlari (kunlik navigatsiya: bugun avtomatik, keyin/oldin bilan almashtiriladi) */}
      <div className="mt-5 mb-2 flex items-center justify-between px-0.5">
        <p className="text-[13px] font-bold text-ink">Support bronlari</p>
        <div className="flex items-center gap-1">
          <button
            type="button"
            onClick={() => shiftDay(-1)}
            className="tap-scale flex h-7 w-7 items-center justify-center rounded-lg border border-line bg-white text-mute shadow-[var(--shadow-card)]"
          >
            <ChevronLeft className="h-4 w-4" />
          </button>
          <span className="min-w-[104px] text-center text-[12px] font-bold text-ink">
            {dayLabel(focusDay, today)}
          </span>
          <button
            type="button"
            onClick={() => shiftDay(1)}
            className="tap-scale flex h-7 w-7 items-center justify-center rounded-lg border border-line bg-white text-mute shadow-[var(--shadow-card)]"
          >
            <ChevronRight className="h-4 w-4" />
          </button>
        </div>
      </div>
      {focusDay !== today && (
        <div className="mb-2 flex justify-end px-0.5">
          <button
            type="button"
            onClick={goToday}
            className="tap-scale rounded-lg px-2 py-1 text-[12px] font-bold text-teal-700"
          >
            Bugunga qaytish
          </button>
        </div>
      )}

      {loading ? (
        <Loader label="Yuklanmoqda..." />
      ) : daySlots.length === 0 ? (
        <div className="flex flex-col items-center justify-center gap-3 rounded-[20px] border border-line bg-white p-8 text-center shadow-[var(--shadow-card)]">
          <div className="flex h-12 w-12 items-center justify-center rounded-2xl bg-tealsoft text-teal-700">
            <CalendarClock className="h-6 w-6" />
          </div>
          <p className="text-[14px] font-semibold text-ink">Bu kunga vaqt qo'shilmagan</p>
          <p className="text-[13px] text-mute">Yuqoridan bo'sh vaqt blokini qo'shing.</p>
        </div>
      ) : (
        <div className="divide-y divide-line rounded-[20px] border border-line bg-white shadow-[var(--shadow-card)]">
          {daySlots.map((s) => {
            const chip = statusChip(s.status)
            return (
              <div key={s.id} className="px-3.5 py-3">
                <div className="flex items-center gap-3">
                  <div className="min-w-0 flex-1">
                    <p className="font-mono text-[14px] font-bold text-ink">
                      {s.startTime}–{s.endTime}
                    </p>
                    {s.status === 'booked' && (
                      <p className="mt-0.5 text-[12px] text-mute">{s.studentName} bron qildi</p>
                    )}
                    {s.status === 'done' && s.studentName && (
                      <p className="mt-0.5 text-[12px] text-mute">{s.studentName}</p>
                    )}
                  </div>
                  <span className={`shrink-0 rounded-full px-2 py-0.5 text-[11px] font-bold ${chip.cls}`}>
                    {s.status === 'done' && <CheckCircle2 className="mr-0.5 inline h-3 w-3" />}
                    {chip.label}
                  </span>
                </div>

                {/* Done — mavzu + izoh (faqat o'qish) */}
                {s.status === 'done' && (s.topic || s.notes) && (
                  <div className="mt-2 rounded-[12px] bg-slate-50 p-2.5">
                    {s.topic && <p className="text-[13px] font-semibold text-ink">{s.topic}</p>}
                    {s.notes && <p className="mt-0.5 text-[12px] text-mute">{s.notes}</p>}
                  </div>
                )}

                {/* Open — o'chirish */}
                {s.status === 'open' && (
                  <div className="mt-2 flex justify-end">
                    <button
                      type="button"
                      onClick={() => handleDelete(s.id)}
                      className="tap-scale flex items-center gap-1 rounded-[12px] border border-line px-3 py-1.5 text-[12px] font-semibold text-rose-600"
                    >
                      <Trash2 className="h-3.5 w-3.5" />
                      O'chirish
                    </button>
                  </div>
                )}

                {/* Booked — yopish formasi yoki tugmalar */}
                {s.status === 'booked' &&
                  (closingId === s.id ? (
                    <div className="mt-2 space-y-2 rounded-[12px] bg-slate-50 p-2.5">
                      <div className="flex items-center justify-between">
                        <p className="text-[12px] font-bold text-ink">Darsni yopish</p>
                        <button
                          type="button"
                          onClick={() => setClosingId(null)}
                          className="tap-scale flex h-6 w-6 items-center justify-center rounded-lg text-mute"
                        >
                          <X className="h-4 w-4" />
                        </button>
                      </div>
                      <input
                        type="text"
                        value={topic}
                        onChange={(e) => setTopic(e.target.value)}
                        placeholder="Mavzu"
                        className="w-full rounded-[12px] border border-line bg-white px-3 py-2 text-[13px] text-ink outline-none focus:border-teal-500"
                      />
                      <textarea
                        value={notes}
                        onChange={(e) => setNotes(e.target.value)}
                        placeholder="Izoh"
                        rows={2}
                        className="w-full resize-none rounded-[12px] border border-line bg-white px-3 py-2 text-[13px] text-ink outline-none focus:border-teal-500"
                      />
                      <button
                        type="button"
                        onClick={handleComplete}
                        disabled={closing}
                        className="tap-scale flex w-full items-center justify-center gap-1.5 rounded-[12px] bg-teal-600 px-4 py-2.5 text-[13px] font-bold text-white disabled:opacity-60"
                      >
                        <CheckCircle2 className="h-4 w-4" />
                        {closing ? 'Saqlanmoqda...' : 'Yopish'}
                      </button>
                    </div>
                  ) : (
                    <div className="mt-2 flex justify-end gap-2">
                      <button
                        type="button"
                        onClick={() => handleDelete(s.id)}
                        className="tap-scale flex items-center gap-1 rounded-[12px] border border-line px-3 py-1.5 text-[12px] font-semibold text-rose-600"
                      >
                        <Trash2 className="h-3.5 w-3.5" />
                        O'chirish
                      </button>
                      <button
                        type="button"
                        onClick={() => startClose(s.id)}
                        className="tap-scale flex items-center gap-1 rounded-[12px] bg-teal-600 px-3 py-1.5 text-[12px] font-bold text-white"
                      >
                        <CheckCircle2 className="h-3.5 w-3.5" />
                        Darsni yopish
                      </button>
                    </div>
                  ))}
              </div>
            )
          })}
        </div>
      )}
    </div>
  )
}
