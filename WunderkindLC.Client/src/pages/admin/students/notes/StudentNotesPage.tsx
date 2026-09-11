import { useEffect, useState } from 'react'
import { Link } from 'react-router-dom'
import { MessageSquareText, Search, StickyNote, User } from 'lucide-react'
import {
  getStudentNotesOverview,
  getStudentNoteDays,
  type StudentNoteOverviewRow,
} from '@/api/services/students'
import { StudentNotesThread } from '@/components/students/StudentNotesThread'
import { MonthDayStrip } from '@/components/ui/MonthDayStrip'
import { currentMonth, monthRange } from '@/lib/month'
import { Card } from '@/components/ui/Card'
import { Button } from '@/components/ui/Button'
import { Modal } from '@/components/ui/Modal'
import { Loader } from '@/components/ui/Loader'
import { PageHeader } from '@/components/ui/PageHeader'
import { apiErrorMessage, cn, formatDate, formatDateTime } from '@/lib/utils'
import { usePerm } from '@/lib/permissions'

/**
 * Tanlangan DAVR — bir vaqtda faqat BITTASI bo'ladi.
 *
 * ⚠️ Ataylab "union": ilgari (kun + oraliq + tez tugmalar) bir-biriga qarama-qarshi holatlar
 * paydo bo'lishi mumkin edi. Endi holat bitta: hammasi | oy | aniq KUN.
 */
type Period =
  | { kind: 'all' }
  | { kind: 'month' }
  | { kind: 'day'; date: string }

/**
 * "IZOHLARGA JAVOBLAR" — o'quvchi profillariga yozilgan izohlar BIR RO'YXATDA.
 *
 * <p>Izoh profil ichida yoziladi, ya'ni "kimda izoh bor" degan savolga javob berish uchun har bir
 * profilni ochib chiqish kerak edi. Bu sahifa aynan shu savolga javob beradi: kimga izoh yozilgan,
 * NECHTA, oxirgisi QACHON va NIMA deb yozilgan.</p>
 *
 * <p>Qatorni bosish — o'quvchining butun izoh tarixi va o'sha yerdan QO'SHIMCHA izoh yozish
 * (`StudentNotesThread` — profildagi bilan AYNAN bir xil komponent).</p>
 *
 * <p>Ruxsat: `students.notes` (marshrutda `RequirePerm`, serverda esa
 * `[AdminPerm("students", ReadRequiresPerm = true)]` — butun markazning izohlari bir joyda
 * bo'lgani uchun o'qish ham darvozalangan).</p>
 */
export function StudentNotesPage() {
  const { can } = usePerm()
  const canOpenProfile = can('students.list', 'view')
  const [rows, setRows] = useState<StudentNoteOverviewRow[]>([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState('')

  const [term, setTerm] = useState('')
  const [q, setQ] = useState('')

  /** Kalendarda ko'rinayotgan oy ("yyyy-MM"). */
  const [month, setMonth] = useState(currentMonth())
  /**
   * Davr. Standart — HAMMASI: sahifaning asosiy savoli "kimda umuman izoh bor", shuning uchun
   * ochilganda ro'yxat to'liq turadi (bugun izoh yozilmagan bo'lsa bo'sh ekran chiqmasin).
   * Aniq kun kerak bo'lsa — kalendardan bosiladi.
   */
  const [period, setPeriod] = useState<Period>({ kind: 'all' })
  /** Kalendar kataklaridagi sonlar: oyning har kunida nechta izoh yozilgan. */
  const [monthCounts, setMonthCounts] = useState<Record<string, number>>({})

  // Davr — tanlovdan HOSILA (bitta haqiqat manbai).
  const range = monthRange(month)
  const from = period.kind === 'all' ? '' : period.kind === 'month' ? range.from : period.date
  const to = period.kind === 'all' ? '' : period.kind === 'month' ? range.to : period.date

  /** Ochilgan o'quvchi (izohlar oynasi). */
  const [open, setOpen] = useState<StudentNoteOverviewRow | null>(null)

  useEffect(() => {
    let alive = true
    // eslint-disable-next-line react-hooks/set-state-in-effect -- filtr o'zgarganda qayta yuklash (maqsadli)
    setLoading(true)
    getStudentNotesOverview({ q: q || undefined, from: from || undefined, to: to || undefined })
      .then((r) => {
        if (!alive) return
        setRows(r)
        setError('')
      })
      .catch((e) => alive && setError(apiErrorMessage(e, "Ro'yxatni yuklab bo'lmadi")))
      .finally(() => alive && setLoading(false))
    return () => {
      alive = false
    }
  }, [q, from, to])

  /** Kalendar sonlari — BUTUN OY bo'yicha (tanlangan davrga bog'liq emas). */
  useEffect(() => {
    let alive = true
    getStudentNoteDays(month)
      .then((d) => {
        if (!alive) return
        setMonthCounts(Object.fromEntries(d.map((x) => [x.date, x.count])))
      })
      .catch(() => alive && setMonthCounts({}))
    return () => {
      alive = false
    }
  }, [month])

  /**
   * Oy almashtirilganda tanlangan KUN saqlanib qolmaydi — u boshqa oyga tegishli edi va
   * kalendarda ko'rinmagan holda ro'yxatni jimgina filtrlab turardi.
   */
  const changeMonth = (m: string) => {
    setMonth(m)
    setPeriod((p) => (p.kind === 'day' ? { kind: 'month' } : p))
  }

  const totalNotes = rows.reduce((sum, r) => sum + r.noteCount, 0)

  /**
   * Oynada izoh qo'shilsa/o'chirilsa ro'yxatdagi son ham yangilanadi — sahifani qayta
   * yuklamasdan (aks holda "3 ta izoh" deb turgan qator eskirib qolardi).
   */
  const applyCount = (studentId: string, count: number) => {
    setRows((prev) =>
      prev
        .map((r) => (r.studentId === studentId ? { ...r, noteCount: count } : r))
        // Izohi qolmagan o'quvchi ro'yxatda turmasin (server ham uni qaytarmasdi).
        .filter((r) => r.noteCount > 0),
    )
    setOpen((prev) => (prev && prev.studentId === studentId ? { ...prev, noteCount: count } : prev))
  }

  return (
    <div>
      <PageHeader
        title="Izohlarga javoblar"
        sub="O'quvchi profillariga yozilgan izohlar bir joyda — kimga, nechta va nima deb yozilgan"
      />

      <Card
        className="mb-4"
        title="Sana"
        sub="Kalendardan ANIQ KUNNI bosing — o'sha kuni yozilgan izohlar chiqadi. Qidiruv esa ism bo'yicha ham, izoh matni bo'yicha ham ishlaydi."
      >
        <div className="space-y-3">
          <MonthDayStrip
            month={month}
            onMonthChange={changeMonth}
            selected={period.kind === 'day' ? period.date : ''}
            onSelect={(d) => setPeriod(d ? { kind: 'day', date: d } : { kind: 'month' })}
            counts={monthCounts}
            hint="Katakdagi son — o'sha kuni yozilgan izohlar soni. Tanlangan kunni qayta bossangiz butun oyga qaytadi."
          />

          <div className="flex flex-wrap items-center gap-2">
            <button
              type="button"
              onClick={() => setPeriod({ kind: 'all' })}
              className={cn(
                'rounded-lg border px-3 py-1.5 text-sm font-medium transition-colors',
                period.kind === 'all'
                  ? 'border-brand-500 bg-brand-50 text-brand-700'
                  : 'border-slate-200 bg-white text-slate-600 hover:bg-slate-50',
              )}
            >
              Hamma vaqt
            </button>
            <button
              type="button"
              onClick={() => setPeriod({ kind: 'month' })}
              className={cn(
                'rounded-lg border px-3 py-1.5 text-sm font-medium transition-colors',
                period.kind === 'month'
                  ? 'border-brand-500 bg-brand-50 text-brand-700'
                  : 'border-slate-200 bg-white text-slate-600 hover:bg-slate-50',
              )}
            >
              Butun oy
            </button>

            <form
              className="ml-auto flex gap-2"
              onSubmit={(e) => {
                e.preventDefault()
                setQ(term.trim())
              }}
            >
              <input
                value={term}
                onChange={(e) => setTerm(e.target.value)}
                placeholder="Ism yoki izoh matni"
                className="min-w-[220px] rounded-lg border border-slate-200 px-3 py-2 text-sm text-slate-700 outline-none focus:border-brand-400"
              />
              <Button type="submit" variant="secondary">
                <Search className="h-4 w-4" />
              </Button>
            </form>
          </div>
        </div>
      </Card>

      {error && <p className="mb-3 rounded-lg bg-red-50 px-3 py-2 text-sm text-red-600">{error}</p>}

      <Card
        tight
        title={
          <span className="inline-flex items-center gap-2">
            <StickyNote className="h-4 w-4 text-slate-400" /> Izoh yozilgan o'quvchilar
            {/* Tanlangan davr SARLAVHADA ham turadi — ro'yxat nega qisqarganini izohlaydi. */}
            {period.kind !== 'all' && (
              <span className="rounded-md bg-brand-50 px-2 py-0.5 text-xs font-medium text-brand-700">
                {period.kind === 'day' ? formatDate(period.date) : `${formatDate(range.from)} — ${formatDate(range.to)}`}
              </span>
            )}
          </span>
        }
        sub={
          loading
            ? undefined
            : `${rows.length} ta o'quvchi · ${totalNotes} ta izoh — eng yangi izoh tepada`
        }
      >
        {loading ? (
          <div className="p-6">
            <Loader label="Yuklanmoqda..." />
          </div>
        ) : rows.length === 0 ? (
          <p className="py-10 text-center text-sm text-slate-400">
            {period.kind === 'day'
              ? `${formatDate(period.date)} kuni izoh yozilmagan`
              : q || period.kind === 'month'
                ? 'Bu filtrlarda izoh topilmadi'
                : 'Hali hech kimga izoh yozilmagan'}
          </p>
        ) : (
          <div className="overflow-x-auto">
            <table className="table">
              <thead>
                <tr>
                  <th>F.I.Sh</th>
                  <th>Guruhi</th>
                  <th className="num">Izohlar</th>
                  <th>Oxirgi izoh</th>
                  <th>Nima deb yozilgan</th>
                  <th>Kim yozgan</th>
                </tr>
              </thead>
              <tbody>
                {rows.map((r) => (
                  <tr
                    key={r.studentId}
                    onClick={() => setOpen(r)}
                    className="cursor-pointer transition-colors hover:bg-slate-50"
                  >
                    <td>
                      <span className="font-semibold text-slate-800">{r.fullName}</span>
                      {r.isArchived && (
                        <span className="ml-2 rounded-md bg-slate-100 px-1.5 py-0.5 text-[11px] text-slate-500">
                          arxivda
                        </span>
                      )}
                    </td>
                    <td className="text-sm text-slate-600">
                      {r.groups.length > 0 ? r.groups.join(', ') : <span className="text-slate-300">—</span>}
                    </td>
                    <td className="num font-semibold text-slate-700">{r.noteCount}</td>
                    <td className="whitespace-nowrap text-sm text-slate-500">
                      {formatDateTime(r.lastNoteAt)}
                    </td>
                    <td className="max-w-[380px] truncate text-sm text-slate-700" title={r.lastNoteText}>
                      {r.lastNoteText}
                    </td>
                    <td className="text-sm text-slate-500">{r.lastAuthorName || '—'}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </Card>

      {rows.length >= 200 && !loading && (
        <p className="mt-3 text-center text-xs text-slate-400">
          Eng so'nggi 200 ta o'quvchi ko'rsatildi — davrni toraytiring yoki qidiruvdan foydalaning.
        </p>
      )}

      {/* ---------- Bitta o'quvchining butun izoh tarixi + qo'shimcha izoh ---------- */}
      <Modal
        open={!!open}
        onClose={() => setOpen(null)}
        size="lg"
        title={open ? `${open.fullName} — izohlar` : ''}
      >
        {open && (
          <div className="space-y-4">
            <div className="flex flex-wrap items-center gap-3 rounded-xl border border-slate-100 bg-slate-50/60 px-4 py-3 text-sm">
              <span className="inline-flex items-center gap-1.5 text-slate-600">
                <MessageSquareText className="h-4 w-4 text-slate-400" />
                <b className="text-slate-800">{open.noteCount}</b> ta izoh
              </span>
              {open.groups.length > 0 && (
                <span className="text-slate-500">· {open.groups.join(', ')}</span>
              )}
              {open.authors.length > 0 && (
                <span className="text-slate-500">· Yozganlar: {open.authors.join(', ')}</span>
              )}
              {/* Profil — ALOHIDA sahifa (`students.list`). Faqat shu ro'yxatga ruxsati bor
                  xodimga havola KO'RSATILMAYDI: bosib "ruxsatingiz yo'q" ga tushib qolmasin. */}
              {canOpenProfile && (
                <Link
                  to={`/admin/students/${open.studentId}`}
                  className="ml-auto inline-flex items-center gap-1.5 text-sm font-medium text-brand-600 hover:underline"
                >
                  <User className="h-4 w-4" /> Profilga o'tish
                </Link>
              )}
            </div>

            {/* Profildagi bilan AYNAN bir xil komponent — qoida ikki joyda ayri ketmaydi. */}
            <StudentNotesThread
              studentId={open.studentId}
              onChanged={(count) => applyCount(open.studentId, count)}
            />
          </div>
        )}
      </Modal>
    </div>
  )
}
