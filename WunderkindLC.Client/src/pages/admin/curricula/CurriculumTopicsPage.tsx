import { useEffect, useState } from 'react'
import { useNavigate, useParams } from 'react-router-dom'
import { ArrowLeft, Plus, Trash2, Pencil, ChevronRight, ListChecks } from 'lucide-react'
import type { Curriculum, CurriculumModule, CurriculumTopic } from '@/types'
import { getCurriculum, createTopic, updateTopic, deleteTopic } from '@/api/services/curriculum'
import { Card } from '@/components/ui/Card'
import { Button } from '@/components/ui/Button'
import { Loader } from '@/components/ui/Loader'
import { PageHeader } from '@/components/ui/PageHeader'
import { apiErrorMessage, cn } from '@/lib/utils'
import { NameModal, ConfirmDeleteModal } from './shared'

type Notice = { type: 'success' | 'error'; text: string }

/** O'quv dasturi 2-bosqich: bitta modul ichidagi Mavzular ro'yxati. Mavzu ustiga bosilsa —
 *  ichiga (Darslar) kiriladi. */
export function CurriculumTopicsPage() {
  const { curriculumId = '', moduleId = '' } = useParams()
  const navigate = useNavigate()

  const [loading, setLoading] = useState(true)
  const [data, setData] = useState<Curriculum | null>(null)
  const [notice, setNotice] = useState<Notice | null>(null)

  const [addOpen, setAddOpen] = useState(false)
  const [addBusy, setAddBusy] = useState(false)
  const [renaming, setRenaming] = useState<CurriculumTopic | null>(null)
  const [renameBusy, setRenameBusy] = useState(false)
  const [deleting, setDeleting] = useState<CurriculumTopic | null>(null)
  const [deleteBusy, setDeleteBusy] = useState(false)

  const load = () => getCurriculum(curriculumId).then(setData).catch(() => setData(null)).finally(() => setLoading(false))

  useEffect(() => {
    setLoading(true)
    void load()
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [curriculumId, moduleId])

  useEffect(() => {
    if (!notice) return
    const t = setTimeout(() => setNotice(null), 4000)
    return () => clearTimeout(t)
  }, [notice])

  const module: CurriculumModule | undefined = data?.modules.find((m) => m.id === moduleId)

  const patchTopics = (fn: (topics: CurriculumTopic[]) => CurriculumTopic[]) =>
    setData((d) =>
      d
        ? { ...d, modules: d.modules.map((m) => (m.id === moduleId ? { ...m, topics: fn(m.topics) } : m)) }
        : d,
    )

  const addTopic = async (title: string) => {
    setAddBusy(true)
    try {
      const { id: tid } = await createTopic(moduleId, title)
      const topic: CurriculumTopic = { id: tid, title, note: '', order: 0, lessons: [] }
      patchTopics((ts) => [...ts, topic])
      setAddOpen(false)
    } catch (err) {
      setNotice({ type: 'error', text: apiErrorMessage(err, 'Xato yuz berdi') })
    } finally {
      setAddBusy(false)
    }
  }

  const submitRename = async (title: string) => {
    if (!renaming) return
    setRenameBusy(true)
    try {
      await updateTopic(renaming.id, title, renaming.note)
      patchTopics((ts) => ts.map((t) => (t.id === renaming.id ? { ...t, title } : t)))
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
      await deleteTopic(deleting.id)
      patchTopics((ts) => ts.filter((t) => t.id !== deleting.id))
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

  if (!module) {
    return (
      <Card className="py-12 text-center text-slate-400">
        Modul topilmadi.
        <div className="mt-3">
          <Button variant="secondary" onClick={() => navigate(`/admin/curricula/${curriculumId}`)}>
            <ArrowLeft className="h-4 w-4" /> Modullarga qaytish
          </Button>
        </div>
      </Card>
    )
  }

  return (
    <div>
      <button
        type="button"
        onClick={() => navigate(`/admin/curricula/${curriculumId}`)}
        className="mb-3 inline-flex items-center gap-1.5 text-sm font-medium text-slate-500 hover:text-slate-700"
      >
        <ArrowLeft className="h-4 w-4" /> Modullarga qaytish
      </button>

      <PageHeader
        title={module.name}
        sub={module.topics.length > 0 ? `${module.topics.length} mavzu — mavzu tanlang` : 'Mavzu tanlang'}
        actions={
          <Button onClick={() => setAddOpen(true)}>
            <Plus className="h-4 w-4" /> Mavzu
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

      {module.topics.length === 0 ? (
        <Card className="py-12 text-center text-slate-400">
          <ListChecks className="mx-auto mb-2 h-6 w-6" />
          Hali mavzu kiritilmagan. Yuqoridagi "+ Mavzu" tugmasi bilan boshlang.
        </Card>
      ) : (
        <div className="grid grid-cols-1 gap-3 sm:grid-cols-2 lg:grid-cols-3">
          {module.topics.map((topic) => {
            const items = topic.lessons.flatMap((s) => s.items)
            const ready = items.filter((it) => it.ready).length
            return (
              <button
                key={topic.id}
                type="button"
                onClick={() => navigate(`/admin/curricula/${curriculumId}/${moduleId}/${topic.id}`)}
                className="group flex items-center gap-3 rounded-xl border border-slate-200 bg-white p-4 text-left transition-all hover:border-brand-300 hover:shadow-md"
              >
                <div className="min-w-0 flex-1">
                  <p className="truncate font-semibold text-slate-800">{topic.title}</p>
                  <div className="mt-1.5 flex items-center gap-3 text-xs">
                    <span className="text-slate-500">{topic.lessons.length} dars</span>
                    <span className="font-medium text-brand-600">
                      {ready}/{items.length} topshiriq
                    </span>
                  </div>
                </div>
                <div className="flex flex-shrink-0 items-center gap-0.5">
                  <span
                    role="button"
                    onClick={(e) => {
                      e.stopPropagation()
                      setRenaming(topic)
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
                      setDeleting(topic)
                    }}
                    className="flex h-8 w-8 items-center justify-center rounded-lg text-slate-400 transition-colors hover:bg-red-50 hover:text-red-500"
                    title="O'chirish"
                  >
                    <Trash2 className="h-4 w-4" />
                  </span>
                  <ChevronRight className="h-5 w-5 shrink-0 text-slate-300 transition-transform group-hover:translate-x-0.5" />
                </div>
              </button>
            )
          })}
        </div>
      )}

      <NameModal
        open={addOpen}
        title="Yangi mavzu"
        label="Mavzu nomi"
        placeholder="Masalan: Present Simple, Kirish..."
        hint="Mavzu ichiga darslar qo'shiladi."
        busy={addBusy}
        onClose={() => setAddOpen(false)}
        onSubmit={addTopic}
      />

      <NameModal
        open={!!renaming}
        title="Mavzu nomini o'zgartirish"
        label="Mavzu nomi"
        placeholder="Mavzu nomi"
        initialValue={renaming?.title ?? ''}
        submitLabel="Saqlash"
        busy={renameBusy}
        onClose={() => setRenaming(null)}
        onSubmit={submitRename}
      />

      <ConfirmDeleteModal
        open={!!deleting}
        title="Mavzuni o'chirish"
        message={
          deleting && (
            <>
              <b>"{deleting.title}"</b> mavzusi barcha dars va topshiriqlari bilan birga
              o'chiriladi. Bu amalni qaytarib bo'lmaydi.
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
