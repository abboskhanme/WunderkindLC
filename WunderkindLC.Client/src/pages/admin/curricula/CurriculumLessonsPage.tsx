import { useEffect, useState } from 'react'
import { useNavigate, useParams } from 'react-router-dom'
import { ArrowLeft, Plus, Trash2, Pencil, ChevronRight, ListChecks } from 'lucide-react'
import type { Curriculum, CurriculumTopic, CurriculumLesson } from '@/types'
import { getCurriculum, createLesson, updateLesson, deleteLesson } from '@/api/services/curriculum'
import { Card } from '@/components/ui/Card'
import { Button } from '@/components/ui/Button'
import { Loader } from '@/components/ui/Loader'
import { PageHeader } from '@/components/ui/PageHeader'
import { apiErrorMessage, cn } from '@/lib/utils'
import { NameModal, ConfirmDeleteModal } from './shared'

type Notice = { type: 'success' | 'error'; text: string }

/** O'quv dasturi 3-bosqich: bitta mavzu ichidagi Darslar ro'yxati. Dars ustiga bosilsa —
 *  ichiga (Topshiriqlar) kiriladi. */
export function CurriculumLessonsPage() {
  const { curriculumId = '', moduleId = '', topicId = '' } = useParams()
  const navigate = useNavigate()

  const [loading, setLoading] = useState(true)
  const [data, setData] = useState<Curriculum | null>(null)
  const [notice, setNotice] = useState<Notice | null>(null)

  const [addOpen, setAddOpen] = useState(false)
  const [addBusy, setAddBusy] = useState(false)
  const [renaming, setRenaming] = useState<CurriculumLesson | null>(null)
  const [renameBusy, setRenameBusy] = useState(false)
  const [deleting, setDeleting] = useState<CurriculumLesson | null>(null)
  const [deleteBusy, setDeleteBusy] = useState(false)

  const load = () => getCurriculum(curriculumId).then(setData).catch(() => setData(null)).finally(() => setLoading(false))

  useEffect(() => {
    setLoading(true)
    void load()
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [curriculumId, moduleId, topicId])

  useEffect(() => {
    if (!notice) return
    const t = setTimeout(() => setNotice(null), 4000)
    return () => clearTimeout(t)
  }, [notice])

  const module = data?.modules.find((m) => m.id === moduleId)
  const topic: CurriculumTopic | undefined = module?.topics.find((t) => t.id === topicId)

  const patchLessons = (fn: (lessons: CurriculumLesson[]) => CurriculumLesson[]) =>
    setData((d) =>
      d
        ? {
            ...d,
            modules: d.modules.map((m) =>
              m.id === moduleId
                ? { ...m, topics: m.topics.map((t) => (t.id === topicId ? { ...t, lessons: fn(t.lessons) } : t)) }
                : m,
            ),
          }
        : d,
    )

  const addLesson = async (title: string) => {
    setAddBusy(true)
    try {
      const { id: lid } = await createLesson(topicId, title)
      const lesson: CurriculumLesson = { id: lid, title, note: '', order: 0, items: [] }
      patchLessons((ls) => [...ls, lesson])
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
      await updateLesson(renaming.id, title, renaming.note)
      patchLessons((ls) => ls.map((l) => (l.id === renaming.id ? { ...l, title } : l)))
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
      await deleteLesson(deleting.id)
      patchLessons((ls) => ls.filter((l) => l.id !== deleting.id))
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

  if (!module || !topic) {
    return (
      <Card className="py-12 text-center text-slate-400">
        Mavzu topilmadi.
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
        onClick={() => navigate(`/admin/curricula/${curriculumId}/${moduleId}`)}
        className="mb-3 inline-flex items-center gap-1.5 text-sm font-medium text-slate-500 hover:text-slate-700"
      >
        <ArrowLeft className="h-4 w-4" /> {module.name} mavzulariga qaytish
      </button>

      <PageHeader
        title={topic.title}
        sub={topic.lessons.length > 0 ? `${topic.lessons.length} dars — dars tanlang` : 'Dars tanlang'}
        actions={
          <Button onClick={() => setAddOpen(true)}>
            <Plus className="h-4 w-4" /> Dars
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

      {topic.lessons.length === 0 ? (
        <Card className="py-12 text-center text-slate-400">
          <ListChecks className="mx-auto mb-2 h-6 w-6" />
          Hali dars kiritilmagan. Yuqoridagi "+ Dars" tugmasi bilan boshlang.
        </Card>
      ) : (
        <div className="grid grid-cols-1 gap-3 sm:grid-cols-2 lg:grid-cols-3">
          {topic.lessons.map((lesson) => {
            const ready = lesson.items.filter((it) => it.ready).length
            return (
              <button
                key={lesson.id}
                type="button"
                onClick={() => navigate(`/admin/curricula/${curriculumId}/${moduleId}/${topicId}/${lesson.id}`)}
                className="group flex items-center gap-3 rounded-xl border border-slate-200 bg-white p-4 text-left transition-all hover:border-brand-300 hover:shadow-md"
              >
                <div className="min-w-0 flex-1">
                  <p className="truncate font-semibold text-slate-800">{lesson.title}</p>
                  <div className="mt-1.5 flex items-center gap-3 text-xs">
                    <span className="font-medium text-brand-600">
                      {ready}/{lesson.items.length} topshiriq
                    </span>
                  </div>
                </div>
                <div className="flex flex-shrink-0 items-center gap-0.5">
                  <span
                    role="button"
                    onClick={(e) => {
                      e.stopPropagation()
                      setRenaming(lesson)
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
                      setDeleting(lesson)
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
        title="Yangi dars"
        label="Dars nomi"
        placeholder="Masalan: 1-dars. Tanishuv..."
        hint="Dars ichiga topshiriqlar (video/matn/audio/pdf/lug'at/test) qo'shiladi."
        busy={addBusy}
        onClose={() => setAddOpen(false)}
        onSubmit={addLesson}
      />

      <NameModal
        open={!!renaming}
        title="Dars nomini o'zgartirish"
        label="Dars nomi"
        placeholder="Dars nomi"
        initialValue={renaming?.title ?? ''}
        submitLabel="Saqlash"
        busy={renameBusy}
        onClose={() => setRenaming(null)}
        onSubmit={submitRename}
      />

      <ConfirmDeleteModal
        open={!!deleting}
        title="Darsni o'chirish"
        message={
          deleting && (
            <>
              <b>"{deleting.title}"</b> darsi barcha topshiriqlari bilan birga o'chiriladi. Bu
              amalni qaytarib bo'lmaydi.
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
