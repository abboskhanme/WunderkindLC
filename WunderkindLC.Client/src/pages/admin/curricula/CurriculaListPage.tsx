import { useEffect, useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { Plus, Pencil, Trash2, ListChecks, ChevronRight, Layers } from 'lucide-react'
import type { CurriculumSummary } from '@/api/services/curriculum'
import { listCurricula, createCurriculum, updateCurriculum, deleteCurriculum } from '@/api/services/curriculum'
import { Card } from '@/components/ui/Card'
import { Button } from '@/components/ui/Button'
import { Loader } from '@/components/ui/Loader'
import { PageHeader } from '@/components/ui/PageHeader'
import { apiErrorMessage, cn } from '@/lib/utils'
import { NameModal, ConfirmDeleteModal } from './shared'

type Notice = { type: 'success' | 'error'; text: string }

/** O'quv dasturlari — top-level ro'yxat. Kurslardan MUSTAQIL: bir dastur bir nechta kursga
 *  biriktirilishi mumkin (biriktirish "Kurslar" sahifasida). Dastur ustiga bosilsa ichiga
 *  (Modullar) kiriladi. */
export function CurriculaListPage() {
  const navigate = useNavigate()
  const [loading, setLoading] = useState(true)
  const [curricula, setCurricula] = useState<CurriculumSummary[]>([])
  const [notice, setNotice] = useState<Notice | null>(null)

  const [addOpen, setAddOpen] = useState(false)
  const [addBusy, setAddBusy] = useState(false)
  const [renaming, setRenaming] = useState<CurriculumSummary | null>(null)
  const [renameBusy, setRenameBusy] = useState(false)
  const [deleting, setDeleting] = useState<CurriculumSummary | null>(null)
  const [deleteBusy, setDeleteBusy] = useState(false)

  const load = () => listCurricula().then(setCurricula).catch(() => setCurricula([])).finally(() => setLoading(false))

  useEffect(() => {
    void load()
  }, [])

  useEffect(() => {
    if (!notice) return
    const t = setTimeout(() => setNotice(null), 4000)
    return () => clearTimeout(t)
  }, [notice])

  const addCurriculum = async (name: string) => {
    setAddBusy(true)
    try {
      const { id } = await createCurriculum(name)
      setCurricula((prev) => [
        ...prev,
        { id, name, note: '', order: prev.length, createdAt: '', moduleCount: 0, topicCount: 0, itemCount: 0, readyItemCount: 0, subjectCount: 0 },
      ])
      setAddOpen(false)
    } catch (err) {
      setNotice({ type: 'error', text: apiErrorMessage(err, 'Xato yuz berdi') })
    } finally {
      setAddBusy(false)
    }
  }

  const submitRename = async (name: string) => {
    if (!renaming) return
    setRenameBusy(true)
    try {
      await updateCurriculum(renaming.id, name, renaming.note)
      setCurricula((prev) => prev.map((c) => (c.id === renaming.id ? { ...c, name } : c)))
      setRenaming(null)
    } catch (err) {
      setNotice({ type: 'error', text: apiErrorMessage(err, 'Xato yuz berdi') })
    } finally {
      setRenameBusy(false)
    }
  }

  const confirmDelete = async () => {
    if (!deleting) return
    setDeleteBusy(true)
    try {
      await deleteCurriculum(deleting.id)
      setCurricula((prev) => prev.filter((c) => c.id !== deleting.id))
      setDeleting(null)
    } catch (err) {
      setNotice({ type: 'error', text: apiErrorMessage(err, "O'chirib bo'lmadi") })
    } finally {
      setDeleteBusy(false)
    }
  }

  if (loading) {
    return (
      <Card>
        <Loader label="Yuklanmoqda..." />
      </Card>
    )
  }

  return (
    <div>
      <PageHeader
        title="O'quv dasturlari"
        sub="Har bir dastur bir nechta kursga biriktirilishi mumkin (Kurslar sahifasidan)"
        actions={
          <Button onClick={() => setAddOpen(true)}>
            <Plus className="h-4 w-4" /> Yangi dastur
          </Button>
        }
      />

      {notice && (
        <div
          className={cn(
            'mb-3 rounded-xl border px-4 py-3 text-sm font-medium',
            notice.type === 'success'
              ? 'border-emerald-200 bg-emerald-50 text-emerald-800'
              : 'border-red-200 bg-red-50 text-red-800',
          )}
        >
          {notice.text}
        </div>
      )}

      {curricula.length === 0 ? (
        <Card className="py-12 text-center text-slate-400">
          <ListChecks className="mx-auto mb-2 h-6 w-6" />
          Hali o'quv dasturi yaratilmagan. "Yangi dastur" tugmasi bilan boshlang.
        </Card>
      ) : (
        <div className="grid grid-cols-1 gap-3 sm:grid-cols-2 lg:grid-cols-3">
          {curricula.map((c) => (
            <button
              key={c.id}
              type="button"
              onClick={() => navigate(`/admin/curricula/${c.id}`)}
              className="group flex items-center gap-3 rounded-xl border border-slate-200 bg-white p-4 text-left transition-all hover:border-brand-300 hover:shadow-md"
            >
              <div className="flex h-11 w-11 flex-shrink-0 items-center justify-center rounded-xl bg-brand-50 text-brand-600">
                <Layers className="h-5 w-5" />
              </div>
              <div className="min-w-0 flex-1">
                <p className="truncate font-semibold text-slate-800">{c.name}</p>
                <div className="mt-1.5 flex flex-wrap items-center gap-x-3 gap-y-0.5 text-xs">
                  <span className="text-slate-500">{c.moduleCount} modul</span>
                  <span className="font-medium text-brand-600">
                    {c.readyItemCount}/{c.itemCount} topshiriq
                  </span>
                  <span className="text-slate-400">
                    {c.subjectCount > 0 ? `${c.subjectCount} kursda` : 'kursga biriktirilmagan'}
                  </span>
                </div>
              </div>
              <div className="flex flex-shrink-0 items-center gap-0.5">
                <span
                  role="button"
                  onClick={(e) => {
                    e.stopPropagation()
                    setRenaming(c)
                  }}
                  className="flex h-8 w-8 items-center justify-center rounded-lg text-slate-400 transition-colors hover:bg-slate-50 hover:text-slate-600"
                  title="Nomini o'zgartirish"
                >
                  <Pencil className="h-4 w-4" />
                </span>
                <span
                  role="button"
                  onClick={(e) => {
                    e.stopPropagation()
                    setDeleting(c)
                  }}
                  className="flex h-8 w-8 items-center justify-center rounded-lg text-slate-400 transition-colors hover:bg-red-50 hover:text-red-500"
                  title="O'chirish"
                >
                  <Trash2 className="h-4 w-4" />
                </span>
                <ChevronRight className="h-5 w-5 shrink-0 text-slate-300 transition-transform group-hover:translate-x-0.5" />
              </div>
            </button>
          ))}
        </div>
      )}

      <NameModal
        open={addOpen}
        title="Yangi o'quv dasturi"
        label="Dastur nomi"
        placeholder="Masalan: Umumiy ingliz tili, IELTS dasturi..."
        hint="Ichiga bo'lim, mavzu, sub-mavzu va topshiriqlar qo'shiladi. Keyin bu dasturni istalgan kursga biriktirasiz."
        busy={addBusy}
        onClose={() => setAddOpen(false)}
        onSubmit={addCurriculum}
      />

      <NameModal
        open={!!renaming}
        title="Dastur nomini o'zgartirish"
        label="Dastur nomi"
        placeholder="Dastur nomi"
        initialValue={renaming?.name ?? ''}
        submitLabel="Saqlash"
        busy={renameBusy}
        onClose={() => setRenaming(null)}
        onSubmit={submitRename}
      />

      <ConfirmDeleteModal
        open={!!deleting}
        title="Dasturni o'chirish"
        message={
          deleting && (
            <>
              <b>"{deleting.name}"</b> dasturi barcha modul/mavzu/dars/topshiriqlari bilan birga
              o'chiriladi{deleting.subjectCount > 0 ? <> va <b>{deleting.subjectCount} ta kursdan</b> uziladi</> : null}.
              Bu amalni qaytarib bo'lmaydi.
            </>
          )
        }
        busy={deleteBusy}
        onClose={() => setDeleting(null)}
        onConfirm={confirmDelete}
      />
    </div>
  )
}
