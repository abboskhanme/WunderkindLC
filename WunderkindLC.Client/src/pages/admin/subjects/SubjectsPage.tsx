import { useEffect, useState } from 'react'
import { Plus, Pencil, Trash2, BookOpen, ListChecks, AlertTriangle, Loader2, Check } from 'lucide-react'
import type { Subject } from '@/types'
import type { SubjectPayload, SubjectCurriculumLink } from '@/api/services/subjects'
import {
  getSubjects,
  createSubject,
  updateSubject,
  deleteSubject,
  getSubjectCurricula,
  attachCurriculumToSubject,
  detachCurriculumFromSubject,
} from '@/api/services/subjects'
import { listCurricula } from '@/api/services/curriculum'
import type { CurriculumSummary } from '@/api/services/curriculum'
import { Card } from '@/components/ui/Card'
import { Button } from '@/components/ui/Button'
import { Loader } from '@/components/ui/Loader'
import { Modal } from '@/components/ui/Modal'
import { PageHeader } from '@/components/ui/PageHeader'
import { apiErrorMessage, cn, formatMoney } from '@/lib/utils'
import { usePerm } from '@/lib/permissions'
import { SubjectFormModal } from './SubjectFormModal'

export function SubjectsPage() {
  const { can } = usePerm()
  const [subjects, setSubjects] = useState<Subject[]>([])
  const [loading, setLoading] = useState(true)
  const [formOpen, setFormOpen] = useState(false)
  const [editing, setEditing] = useState<Subject | null>(null)
  // Kursga biriktirilgan o'quv dasturlarini boshqarish modali
  const [curriculaFor, setCurriculaFor] = useState<Subject | null>(null)
  // Narx o'zgarganda — yangi narxni bog'langan guruh o'quvchilariga qachondan qo'llashni so'rash uchun
  const [feePrompt, setFeePrompt] = useState<{
    id: string
    values: SubjectPayload
    oldPrice: number
  } | null>(null)
  // Kursni o'chirishni tasdiqlash (brauzer confirm o'rniga)
  const [deleting, setDeleting] = useState<Subject | null>(null)
  const [deleteBusy, setDeleteBusy] = useState(false)

  useEffect(() => {
    getSubjects()
      .then(setSubjects)
      .finally(() => setLoading(false))
  }, [])

  const applyUpdate = (id: string, values: SubjectPayload, applyFee?: boolean) =>
    updateSubject(id, values, applyFee).then((u) =>
      setSubjects((prev) => prev.map((s) => (s.id === u.id ? u : s))),
    )

  const handleSubmit = (values: SubjectPayload) => {
    if (editing) {
      // Narx o'zgargan bo'lsa — bog'langan guruhlar o'quvchilariga qo'llashni so'raymiz (Ha/Yo'q)
      if (values.price !== editing.price) {
        setFeePrompt({ id: editing.id, values, oldPrice: editing.price })
        setFormOpen(false)
        setEditing(null)
        return
      }
      applyUpdate(editing.id, values)
    } else {
      createSubject(values).then((c) => setSubjects((prev) => [...prev, c]))
    }
    setFormOpen(false)
    setEditing(null)
  }

  const resolveFeePrompt = (applyFee: boolean) => {
    if (!feePrompt) return
    applyUpdate(feePrompt.id, feePrompt.values, applyFee)
    setFeePrompt(null)
  }

  const handleDelete = (s: Subject) => setDeleting(s)

  const confirmDelete = async () => {
    if (!deleting || deleteBusy) return
    setDeleteBusy(true)
    try {
      await deleteSubject(deleting.id)
      setSubjects((prev) => prev.filter((x) => x.id !== deleting.id))
      setDeleting(null)
    } finally {
      setDeleteBusy(false)
    }
  }

  return (
    <div>
      <PageHeader
        title="Kurslar"
        sub={`Jami ${subjects.length} ta kurs`}
        actions={
          can('schedule.courses', 'create') ? (
            <Button
              onClick={() => {
                setEditing(null)
                setFormOpen(true)
              }}
            >
              <Plus className="h-4 w-4" /> Yangi kurs
            </Button>
          ) : undefined
        }
      />

      {loading ? (
        <Card>
          <Loader label="Yuklanmoqda..." />
        </Card>
      ) : subjects.length === 0 ? (
        <Card>
          <div className="state">
            <div className="state-icon">
              <BookOpen className="h-5 w-5" />
            </div>
            <h4>Kurslar yo'q</h4>
            <p>"Yangi kurs" tugmasi orqali birinchi kursni qo'shing.</p>
          </div>
        </Card>
      ) : (
        <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-3 xl:grid-cols-4">
          {subjects.map((s) => (
            <div
              key={s.id}
              className="group flex flex-col rounded-2xl border border-slate-200 bg-white p-4 shadow-[var(--shadow-1)] transition-shadow hover:shadow-[var(--shadow-pop)]"
            >
              {/* Kurs nomi */}
              <div className="flex items-start gap-3">
                <div className="flex h-11 w-11 flex-shrink-0 items-center justify-center rounded-xl bg-brand-50 text-brand-600">
                  <BookOpen className="h-[22px] w-[22px]" />
                </div>
                <div className="min-w-0 flex-1">
                  <p className="truncate text-base font-bold tracking-tight text-slate-800">
                    {s.name}
                  </p>
                  <p className="mt-0.5 text-sm text-slate-500">
                    <span className="font-mono font-medium text-slate-700">{formatMoney(s.price)}</span>{' '}
                    so'm / oy
                  </p>
                  {(s.lessonPrice ?? 0) > 0 && (
                    <p className="mt-0.5 text-xs text-slate-400">
                      <span className="font-mono">{formatMoney(s.lessonPrice ?? 0)}</span> so'm / dars
                    </p>
                  )}
                </div>
              </div>

              {/* Amallar */}
              <div className="mt-4 flex items-center gap-2 border-t border-slate-100 pt-3">
                <button
                  type="button"
                  onClick={() => setCurriculaFor(s)}
                  className="inline-flex flex-1 items-center justify-center gap-1.5 rounded-lg bg-brand-50 px-3 py-2 text-sm font-medium text-brand-700 transition-colors hover:bg-brand-100"
                >
                  <ListChecks className="h-4 w-4" /> O'quv dasturlari
                </button>
                {can('schedule.courses', 'edit') && (
                  <IconBtn
                    icon={Pencil}
                    title="Tahrirlash"
                    onClick={() => {
                      setEditing(s)
                      setFormOpen(true)
                    }}
                  />
                )}
                {can('schedule.courses', 'delete') && (
                  <IconBtn icon={Trash2} title="O'chirish" danger onClick={() => handleDelete(s)} />
                )}
              </div>
            </div>
          ))}
        </div>
      )}

      <SubjectFormModal
        open={formOpen}
        onClose={() => {
          setFormOpen(false)
          setEditing(null)
        }}
        onSubmit={handleSubmit}
        initial={editing}
      />

      <Modal
        open={!!feePrompt}
        onClose={() => setFeePrompt(null)}
        title="Kurs narxini o'quvchilarga qo'llash"
        size="sm"
        footer={
          <>
            <Button variant="secondary" onClick={() => resolveFeePrompt(false)}>
              Yo'q — keyingi oydan
            </Button>
            <Button onClick={() => resolveFeePrompt(true)}>Ha — joriy oydan</Button>
          </>
        }
      >
        {feePrompt && (
          <div className="space-y-3 text-sm text-slate-600">
            <p>
              <span className="font-medium text-slate-800">{feePrompt.values.name}</span> kursining
              oylik narxi{' '}
              <span className="font-medium">{formatMoney(feePrompt.oldPrice)}</span> →{' '}
              <span className="font-medium">{formatMoney(feePrompt.values.price)}</span> so'mga
              o'zgardi. Yangi narx shu kursga bog'langan guruhlardagi o'quvchilarga qachondan
              qo'llansin?
            </p>
            <div className="rounded-lg bg-slate-50 px-3 py-2 text-slate-500">
              <p>
                <b className="text-slate-700">Ha</b> — joriy oy to'lovi yangi narxga o'zgaradi
                (balans farqqa moslab to'g'rilanadi; qo'lda tahrirlangan oyliklar tegilmaydi).
              </p>
              <p className="mt-1">
                <b className="text-slate-700">Yo'q</b> — joriy oy eski narxda qoladi, yangi narx
                keyingi oydan hisoblanadi.
              </p>
            </div>
          </div>
        )}
      </Modal>

      {/* Kursni o'chirishni tasdiqlash */}
      <Modal
        open={!!deleting}
        onClose={() => setDeleting(null)}
        size="sm"
        title="Kursni o'chirish"
        footer={
          <>
            <Button variant="secondary" onClick={() => setDeleting(null)} disabled={deleteBusy}>
              Bekor qilish
            </Button>
            <Button variant="danger" onClick={confirmDelete} disabled={deleteBusy}>
              {deleteBusy ? <Loader2 className="h-4 w-4 animate-spin" /> : <Trash2 className="h-4 w-4" />}
              O'chirish
            </Button>
          </>
        }
      >
        <div className="flex items-start gap-3">
          <div className="flex h-10 w-10 flex-shrink-0 items-center justify-center rounded-full bg-red-50 text-red-600">
            <AlertTriangle className="h-5 w-5" />
          </div>
          <p className="text-sm leading-relaxed text-slate-600">
            <b>"{deleting?.name}"</b> kursi o'chiriladi. Unga biriktirilgan o'quv dasturlari
            (mustaqil bo'lgani uchun) o'chirilmaydi — faqat bog'lanish uziladi. Bu amalni qaytarib
            bo'lmaydi.
          </p>
        </div>
      </Modal>

      {/* Kursga biriktirilgan o'quv dasturlarini boshqarish */}
      <SubjectCurriculaModal subject={curriculaFor} onClose={() => setCurriculaFor(null)} />
    </div>
  )
}

// ============================ Kursga o'quv dasturlarini biriktirish modali ============================

interface SubjectCurriculaModalProps {
  subject: Subject | null
  onClose: () => void
}

/** Bitta kursga bir nechta o'quv dasturi biriktirilishi mumkin (ko'p-ko'pga — bitta dastur ham
 *  bir nechta kursga biriktirilgan bo'lishi mumkin). Ro'yxatdagi har bir dastur bosilganda
 *  biriktiriladi/uziladi (optimistic). */
function SubjectCurriculaModal({ subject, onClose }: SubjectCurriculaModalProps) {
  const [loading, setLoading] = useState(true)
  const [all, setAll] = useState<CurriculumSummary[]>([])
  const [attached, setAttached] = useState<SubjectCurriculumLink[]>([])
  const [busyId, setBusyId] = useState<string | null>(null)
  const [error, setError] = useState('')

  useEffect(() => {
    if (!subject) return
    setLoading(true)
    setError('')
    Promise.all([listCurricula(), getSubjectCurricula(subject.id)])
      .then(([allC, att]) => {
        setAll(allC)
        setAttached(att)
      })
      .catch((err) => setError(apiErrorMessage(err, 'Yuklab bo\'lmadi')))
      .finally(() => setLoading(false))
  }, [subject])

  const toggle = async (curriculumId: string, isAttached: boolean) => {
    if (!subject || busyId) return
    setBusyId(curriculumId)
    setError('')
    try {
      if (isAttached) {
        await detachCurriculumFromSubject(subject.id, curriculumId)
        setAttached((prev) => prev.filter((a) => a.curriculumId !== curriculumId))
      } else {
        await attachCurriculumToSubject(subject.id, curriculumId)
        const c = all.find((x) => x.id === curriculumId)
        setAttached((prev) => [...prev, { curriculumId, name: c?.name ?? '', order: prev.length }])
      }
    } catch (err) {
      setError(apiErrorMessage(err, 'Xato yuz berdi'))
    } finally {
      setBusyId(null)
    }
  }

  const attachedIds = new Set(attached.map((a) => a.curriculumId))

  return (
    <Modal
      open={!!subject}
      onClose={onClose}
      size="sm"
      title={`"${subject?.name}" — o'quv dasturlari`}
      footer={
        <Button variant="secondary" onClick={onClose}>
          Yopish
        </Button>
      }
    >
      {loading ? (
        <Loader label="Yuklanmoqda..." />
      ) : (
        <div className="space-y-3">
          <p className="text-xs leading-relaxed text-slate-400">
            Belgilangan dasturlardagi barcha topshiriqlar shu kursga biriktirilgan guruhlarda
            ko'rinadi (bir nechtasi belgilansa — hammasi ketma-ket birlashtiriladi).
          </p>
          {error && <p className="text-sm text-red-500">{error}</p>}
          {all.length === 0 ? (
            <p className="text-sm text-slate-400">
              Hali o'quv dasturi yo'q — avval "O'quv bo'limi → O'quv dasturi" sahifasidan yarating.
            </p>
          ) : (
            <div className="max-h-72 space-y-1.5 overflow-y-auto">
              {all.map((c) => {
                const isAttached = attachedIds.has(c.id)
                return (
                  <button
                    key={c.id}
                    type="button"
                    disabled={busyId === c.id}
                    onClick={() => toggle(c.id, isAttached)}
                    className={cn(
                      'flex w-full items-center gap-3 rounded-lg border px-3 py-2 text-left text-sm transition-colors disabled:opacity-60',
                      isAttached
                        ? 'border-brand-400 bg-brand-50 text-brand-700'
                        : 'border-slate-200 text-slate-700 hover:border-slate-300 hover:bg-slate-50',
                    )}
                  >
                    <span
                      className={cn(
                        'flex h-5 w-5 flex-shrink-0 items-center justify-center rounded-md border-2 transition-colors',
                        isAttached ? 'border-brand-500 bg-brand-500 text-white' : 'border-slate-300',
                      )}
                    >
                      {busyId === c.id ? (
                        <Loader2 className="h-3 w-3 animate-spin" />
                      ) : (
                        isAttached && <Check className="h-3 w-3" />
                      )}
                    </span>
                    <span className="min-w-0 flex-1 truncate font-medium">{c.name}</span>
                    <span className="flex-shrink-0 text-xs text-slate-400">{c.itemCount} topshiriq</span>
                  </button>
                )
              })}
            </div>
          )}
        </div>
      )}
    </Modal>
  )
}

interface IconBtnProps {
  icon: typeof Pencil
  title: string
  onClick: () => void
  danger?: boolean
}

function IconBtn({ icon: Icon, title, onClick, danger }: IconBtnProps) {
  return (
    <button
      type="button"
      title={title}
      onClick={onClick}
      className={cn(
        'rounded-lg p-1.5 transition-colors',
        danger
          ? 'text-slate-400 hover:bg-red-50 hover:text-red-600'
          : 'text-slate-400 hover:bg-slate-100 hover:text-slate-700',
      )}
    >
      <Icon className="h-4 w-4" />
    </button>
  )
}
