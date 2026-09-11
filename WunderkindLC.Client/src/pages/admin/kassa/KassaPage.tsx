import { useEffect, useMemo, useState } from 'react'
import {
  Search,
  ChevronRight,
  ChevronLeft,
  Wallet,
  Loader2,
  Archive,
  GraduationCap,
  Users,
  X,
} from 'lucide-react'
import type { Group, GroupMember, Student, Subject, Teacher } from '@/types'
import { searchKassaStudents, addKassaPayment, type KassaStudent } from '@/api/services/kassa'
import { getStudent } from '@/api/services/students'
import { getTeachers, getArchivedTeachers } from '@/api/services/teachers'
import { getClasses, getGroupMembers } from '@/api/services/classes'
import { getSubjects } from '@/api/services/subjects'
import { Badge } from '@/components/ui/Badge'
import type { BadgeTone } from '@/components/ui/Badge'
import { Button } from '@/components/ui/Button'
import { Loader } from '@/components/ui/Loader'
import { ReceiptModal } from '@/components/finance/ReceiptModal'
import { PaymentModal } from '@/pages/admin/students/PaymentModal'
import { formatMoney, apiErrorMessage, cn } from '@/lib/utils'

/** O'qituvchi qatori — arxivdagilar ham ro'yxatda (ularning qarzi ham to'lanishi mumkin). */
interface TeacherRow {
  id: string
  fullName: string
  archived: boolean
}

/** Ekran: o'qituvchilar → guruhlar → o'quvchilar (telefonda "ichiga kirish"). */
type View =
  | { kind: 'teachers' }
  | { kind: 'groups'; teacher: TeacherRow }
  | { kind: 'students'; teacher: TeacherRow; group: Group }

/** A'zolik holati yorlig'i — kassir kimga to'lov qilayotganini adashtirmasin. */
const memberStatus = (m: GroupMember): { label: string; tone: BadgeTone } | null => {
  if (!m.isActive) return { label: 'Chiqarilgan', tone: 'default' }
  if (m.status === 'trial') return { label: 'Sinov', tone: 'amber' }
  if (m.status === 'frozen') return { label: 'Muzlatilgan', tone: 'blue' }
  return null
}

/** Balans rangi: manfiy (qarz) — qizil, aks holda yashil. */
const balanceClass = (v: number) => cn('font-mono text-[13px] font-semibold', v < 0 ? 'text-red-600' : 'text-emerald-600')

/**
 * KASSA — to'lov qabul qilish ekrani (TELEFON uchun). Ikki yo'l:
 *  1) yuqoridagi qidiruv — o'quvchi F.I.Sh yoki telefon raqami bo'yicha;
 *  2) ro'yxat bo'yicha "ichiga kirish": BARCHA o'qituvchilar → uning guruhlari (ARXIV/faol emaslari
 *     ham) → guruh o'quvchilari (chiqarilgan/muzlatilgan/sinov ham) → "To'lov qilish".
 * To'lov oynasi o'quvchilar bo'limidagi bilan bir xil (`PaymentModal`), saqlangach chek ochiladi.
 */
export function KassaPage() {
  /* ---------- Qidiruv (F.I.Sh / telefon) ---------- */
  const [term, setTerm] = useState('')
  const [results, setResults] = useState<KassaStudent[]>([])
  const [searching, setSearching] = useState(false)
  const [searched, setSearched] = useState(false)
  const isSearching = term.trim().length >= 2

  useEffect(() => {
    const q = term.trim()
    if (q.length < 2) {
      // eslint-disable-next-line react-hooks/set-state-in-effect -- qidiruv bo'shatilganda tozalash
      setResults([])
      setSearched(false)
      return
    }
    // eslint-disable-next-line react-hooks/set-state-in-effect -- yozayotganda indikator
    setSearching(true)
    let alive = true
    const t = setTimeout(() => {
      searchKassaStudents(q)
        .then((r) => {
          if (!alive) return
          setResults(r)
          setSearched(true)
        })
        .catch(() => alive && setResults([]))
        .finally(() => alive && setSearching(false))
    }, 300) // har bosishda so'rov ketmasin
    return () => {
      alive = false
      clearTimeout(t)
    }
  }, [term])

  /* ---------- Ro'yxat: o'qituvchi → guruh → o'quvchi ---------- */
  const [view, setView] = useState<View>({ kind: 'teachers' })
  const [teachers, setTeachers] = useState<TeacherRow[]>([])
  const [classes, setClasses] = useState<Group[]>([])
  const [subjects, setSubjects] = useState<Subject[]>([])
  const [refsLoading, setRefsLoading] = useState(true)
  const [members, setMembers] = useState<GroupMember[]>([])
  const [membersLoading, setMembersLoading] = useState(false)

  // Ma'lumotnomalar: BARCHA o'qituvchilar (arxivdagilar ham) va BARCHA guruhlar (arxiv ham) —
  // kassir eski/yopilgan guruhga ham to'lov qabul qila olishi kerak.
  useEffect(() => {
    let alive = true
    Promise.all([
      getTeachers(),
      getArchivedTeachers().catch(() => [] as Teacher[]),
      getClasses(true),
      getSubjects(),
    ])
      .then(([active, archived, cls, subs]) => {
        if (!alive) return
        const rows: TeacherRow[] = [
          ...active.map((t) => ({ id: t.id, fullName: t.fullName, archived: false })),
          ...archived
            .filter((a) => !active.some((t) => t.id === a.id))
            .map((t) => ({ id: t.id, fullName: t.fullName, archived: true })),
        ].sort((a, b) => Number(a.archived) - Number(b.archived) || a.fullName.localeCompare(b.fullName))
        setTeachers(rows)
        setClasses(cls)
        setSubjects(subs)
      })
      .finally(() => alive && setRefsLoading(false))
    return () => {
      alive = false
    }
  }, [])

  const courseName = useMemo(() => {
    const map = new Map(subjects.map((s) => [s.id, s.name]))
    return (g: Group) => (g.courseId ? (map.get(g.courseId) ?? '') : '')
  }, [subjects])

  /** O'qituvchi bo'yicha guruhlar (arxivlangani ham — oxirida). */
  const groupsByTeacher = useMemo(() => {
    const map = new Map<string, Group[]>()
    for (const g of classes) {
      if (!g.teacherId) continue
      const list = map.get(g.teacherId)
      if (list) list.push(g)
      else map.set(g.teacherId, [g])
    }
    for (const list of map.values())
      list.sort((a, b) => Number(!!a.isArchived) - Number(!!b.isArchived) || a.name.localeCompare(b.name))
    return map
  }, [classes])

  // Guruh ochilganda a'zolarni yuklaymiz (balans — SHU GURUH bo'yicha).
  useEffect(() => {
    if (view.kind !== 'students') return
    const gid = view.group.id
    // eslint-disable-next-line react-hooks/set-state-in-effect -- guruh ochilganda yuklash (maqsadli)
    setMembersLoading(true)
    setMembers([])
    let alive = true
    getGroupMembers(gid)
      .then((m) => alive && setMembers(m))
      .catch(() => alive && setMembers([]))
      .finally(() => alive && setMembersLoading(false))
    return () => {
      alive = false
    }
  }, [view])

  /** To'lovdan keyin ro'yxatni yangilash (balans o'zgardi). */
  const reload = () => {
    if (isSearching) {
      searchKassaStudents(term.trim()).then(setResults).catch(() => {})
      return
    }
    if (view.kind === 'students') getGroupMembers(view.group.id).then(setMembers).catch(() => {})
  }

  /* ---------- To'lov ---------- */
  const [payStudent, setPayStudent] = useState<Student | null>(null)
  const [opening, setOpening] = useState<string | null>(null)
  const [receiptTx, setReceiptTx] = useState<string | null>(null)
  const [receiptAuto, setReceiptAuto] = useState(false)

  const openPayment = async (studentId: string) => {
    setOpening(studentId)
    try {
      setPayStudent(await getStudent(studentId))
    } catch (e) {
      alert(apiErrorMessage(e, "O'quvchi ma'lumotini olib bo'lmadi"))
    } finally {
      setOpening(null)
    }
  }

  // Xato YUTILMAYDI — `PaymentModal` uni o'zi ko'rsatadi (kvitansiya band bo'lsa kartochka).
  const handlePayment = async (
    amount: number,
    month: string,
    gid?: string,
    comment?: string,
    method?: string,
    date?: string,
    extra?: { receiptNo?: string; paidTime?: string; cardLast4?: string; forceReceipt?: boolean },
  ) => {
    if (!payStudent) return
    const txId = await addKassaPayment(payStudent.id, amount, month, gid, comment, method, date, extra)
    setPayStudent(null)
    reload()
    if (txId) {
      setReceiptAuto(true)
      setReceiptTx(txId)
    }
  }

  /** Qatordagi "To'lov" tugmasi. */
  const payButton = (studentId: string) => (
    <Button
      className="shrink-0 px-3 py-1.5 text-xs"
      disabled={opening === studentId}
      onClick={(e) => {
        e.stopPropagation()
        void openPayment(studentId)
      }}
    >
      {opening === studentId ? <Loader2 className="h-4 w-4 animate-spin" /> : <Wallet className="h-4 w-4" />}
      To'lov
    </Button>
  )

  const teacherGroups = view.kind === 'teachers' ? [] : (groupsByTeacher.get(view.teacher.id) ?? [])

  return (
    <div className="space-y-3">
      {/* ---------- Qidiruv (har doim tepada) ---------- */}
      <div className="relative">
        <Search className="pointer-events-none absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-slate-400" />
        <input
          value={term}
          onChange={(e) => setTerm(e.target.value)}
          placeholder="O'quvchi F.I.Sh yoki telefon..."
          className="w-full rounded-xl border border-slate-200 bg-white py-3 pl-9 pr-9 text-[15px] text-slate-700 outline-none focus:border-brand-400"
        />
        {searching ? (
          <Loader2 className="absolute right-3 top-1/2 h-4 w-4 -translate-y-1/2 animate-spin text-slate-400" />
        ) : term ? (
          <button
            type="button"
            onClick={() => setTerm('')}
            aria-label="Tozalash"
            className="absolute right-2 top-1/2 flex h-7 w-7 -translate-y-1/2 items-center justify-center rounded-lg text-slate-400 hover:bg-slate-100"
          >
            <X className="h-4 w-4" />
          </button>
        ) : null}
      </div>

      {isSearching ? (
        /* ---------- Qidiruv natijalari ---------- */
        <div className="overflow-hidden rounded-xl border border-slate-200 bg-white">
          {results.length === 0 ? (
            <p className="py-10 text-center text-sm text-slate-400">
              {searching ? 'Qidirilmoqda...' : searched ? "O'quvchi topilmadi." : 'Kamida 2 belgi kiriting.'}
            </p>
          ) : (
            results.map((s) => (
              <div key={s.id} className="flex items-center gap-3 border-b border-slate-100 px-3 py-2.5 last:border-0">
                <div className="min-w-0 flex-1">
                  <p className="truncate text-[14px] font-semibold text-slate-800">
                    {s.fullName}
                    {s.isArchived && (
                      <Badge tone="default" className="ml-2 align-middle">
                        <Archive className="h-3 w-3" /> Arxiv
                      </Badge>
                    )}
                  </p>
                  <p className="truncate text-[12px] text-slate-400">
                    {s.groups.length > 0 ? s.groups.join(', ') : 'Guruhsiz'}
                    {s.phone || s.parentPhone ? ` · ${s.phone || s.parentPhone}` : ''}
                  </p>
                  <p className={balanceClass(s.balance)}>{formatMoney(s.balance)}</p>
                </div>
                {payButton(s.id)}
              </div>
            ))
          )}
        </div>
      ) : refsLoading ? (
        <Loader label="Yuklanmoqda..." />
      ) : view.kind === 'teachers' ? (
        /* ---------- 1-ekran: BARCHA o'qituvchilar ---------- */
        <>
          <h2 className="flex items-center gap-2 px-1 text-[13px] font-semibold text-slate-500">
            <GraduationCap className="h-4 w-4" /> O'qituvchilar ({teachers.length})
          </h2>
          <div className="overflow-hidden rounded-xl border border-slate-200 bg-white">
            {teachers.length === 0 ? (
              <p className="py-10 text-center text-sm text-slate-400">O'qituvchi yo'q.</p>
            ) : (
              teachers.map((t) => {
                const gs = groupsByTeacher.get(t.id) ?? []
                return (
                  <button
                    key={t.id}
                    type="button"
                    onClick={() => setView({ kind: 'groups', teacher: t })}
                    className="flex w-full items-center gap-3 border-b border-slate-100 px-3 py-3 text-left last:border-0 active:bg-slate-50"
                  >
                    <div className="min-w-0 flex-1">
                      <p className="truncate text-[14px] font-semibold text-slate-800">
                        {t.fullName}
                        {t.archived && (
                          <Badge tone="default" className="ml-2 align-middle">
                            Arxiv
                          </Badge>
                        )}
                      </p>
                      <p className="text-[12px] text-slate-400">
                        {gs.length > 0 ? `${gs.length} guruh` : 'Guruhi yo’q'}
                      </p>
                    </div>
                    <ChevronRight className="h-5 w-5 shrink-0 text-slate-300" />
                  </button>
                )
              })
            )}
          </div>
        </>
      ) : view.kind === 'groups' ? (
        /* ---------- 2-ekran: o'qituvchining guruhlari (arxiv ham) ---------- */
        <>
          <BackBar title={view.teacher.fullName} sub="Guruhlar" onBack={() => setView({ kind: 'teachers' })} />
          <div className="overflow-hidden rounded-xl border border-slate-200 bg-white">
            {teacherGroups.length === 0 ? (
              <p className="py-10 text-center text-sm text-slate-400">Bu o'qituvchining guruhi yo'q.</p>
            ) : (
              teacherGroups.map((g) => (
                <button
                  key={g.id}
                  type="button"
                  onClick={() => setView({ kind: 'students', teacher: view.teacher, group: g })}
                  className="flex w-full items-center gap-3 border-b border-slate-100 px-3 py-3 text-left last:border-0 active:bg-slate-50"
                >
                  <div className="min-w-0 flex-1">
                    <p className="truncate text-[14px] font-semibold text-slate-800">
                      {g.name}
                      {g.isArchived && (
                        <Badge tone="default" className="ml-2 align-middle">
                          <Archive className="h-3 w-3" /> Arxiv
                        </Badge>
                      )}
                    </p>
                    <p className="truncate text-[12px] text-slate-400">
                      {[courseName(g), g.monthlyFee ? formatMoney(g.monthlyFee) : ''].filter(Boolean).join(' · ')}
                    </p>
                  </div>
                  <ChevronRight className="h-5 w-5 shrink-0 text-slate-300" />
                </button>
              ))
            )}
          </div>
        </>
      ) : (
        /* ---------- 3-ekran: guruh o'quvchilari ---------- */
        <>
          <BackBar
            title={view.group.name}
            sub={`${view.teacher.fullName}${view.group.isArchived ? ' · arxiv guruh' : ''}`}
            onBack={() => setView({ kind: 'groups', teacher: view.teacher })}
          />
          <div className="overflow-hidden rounded-xl border border-slate-200 bg-white">
            {membersLoading ? (
              <Loader label="Yuklanmoqda..." />
            ) : members.length === 0 ? (
              <p className="py-10 text-center text-sm text-slate-400">Guruhda o'quvchi yo'q.</p>
            ) : (
              <>
                <p className="flex items-center gap-2 border-b border-slate-100 px-3 py-2 text-[12px] text-slate-400">
                  <Users className="h-3.5 w-3.5" /> {members.length} o'quvchi · balans shu guruh bo'yicha
                </p>
                {members.map((m) => {
                  const st = memberStatus(m)
                  return (
                    <div
                      key={m.studentId}
                      className="flex items-center gap-3 border-b border-slate-100 px-3 py-2.5 last:border-0"
                    >
                      <div className="min-w-0 flex-1">
                        <p className="truncate text-[14px] font-semibold text-slate-800">
                          {m.fullName}
                          {st && (
                            <Badge tone={st.tone} className="ml-2 align-middle">
                              {st.label}
                            </Badge>
                          )}
                        </p>
                        <p className={balanceClass(m.balance)}>{formatMoney(m.balance)}</p>
                      </div>
                      {payButton(m.studentId)}
                    </div>
                  )
                })}
              </>
            )}
          </div>
        </>
      )}

      <PaymentModal student={payStudent} onClose={() => setPayStudent(null)} onSubmit={handlePayment} />

      <ReceiptModal
        txId={receiptTx}
        autoPrint={receiptAuto}
        onClose={() => {
          setReceiptTx(null)
          setReceiptAuto(false)
        }}
      />
    </div>
  )
}

/** Ichki ekran sarlavhasi — orqaga qaytish tugmasi bilan (telefonda asosiy navigatsiya). */
function BackBar({ title, sub, onBack }: { title: string; sub?: string; onBack: () => void }) {
  return (
    <div className="flex items-center gap-2 px-1">
      <button
        type="button"
        onClick={onBack}
        aria-label="Orqaga"
        className="flex h-9 w-9 shrink-0 items-center justify-center rounded-lg border border-slate-200 bg-white text-slate-500 active:bg-slate-100"
      >
        <ChevronLeft className="h-5 w-5" />
      </button>
      <div className="min-w-0">
        <p className="truncate text-[14px] font-bold text-slate-800">{title}</p>
        {sub && <p className="truncate text-[12px] text-slate-400">{sub}</p>}
      </div>
    </div>
  )
}
