import { useEffect, useRef, useState } from 'react'
import { useNavigate, useParams } from 'react-router-dom'
import { ArrowLeft, Plus, Trash2, Pencil, ChevronRight, ListChecks, Check, Loader2, X } from 'lucide-react'
import type { Curriculum, CurriculumTopic, CurriculumLesson, CurriculumItem, LessonType } from '@/types'
import { getCurriculum, createItem, createItemsBulk, updateItem, deleteItem } from '@/api/services/curriculum'
import { ExercisePicker } from '@/components/exercise/ExercisePicker'
import { kindInfo, kindTitle } from '@/components/exercise/catalog'
import type { ExerciseKind } from '@/components/exercise/model'
import { Card } from '@/components/ui/Card'
import { Button } from '@/components/ui/Button'
import { Modal } from '@/components/ui/Modal'
import { Loader } from '@/components/ui/Loader'
import { PageHeader } from '@/components/ui/PageHeader'
import { apiErrorMessage, cn, formatDate } from '@/lib/utils'
import { control, LESSON_TYPES, typeMeta, ConfirmDeleteModal, NameModal } from './shared'

type Notice = { type: 'success' | 'error'; text: string }

/** O'quv dasturi 4-bosqich: bitta dars ichidagi Topshiriqlar — JADVAL ko'rinishida
 *  (No, Nomi, Turi, Yaratilgan sana). "+ Topshiriq" bosilsa RO'YXAT modali ochiladi: turini bir
 *  marta tanlaysiz, bir nechta nom kiritasiz (masalan 10ta), BIR marta "N ta yaratish" bosasiz —
 *  hammasi bir zumda, bitta amal bilan yaratiladi. Mavjud topshiriqning nomi VA turi qalam tugmasi
 *  orqali keyin ham o'zgartiriladi. */
export function CurriculumItemsPage() {
  const { curriculumId = '', moduleId = '', topicId = '', lessonId = '' } = useParams()
  const navigate = useNavigate()

  const [loading, setLoading] = useState(true)
  const [data, setData] = useState<Curriculum | null>(null)
  const [notice, setNotice] = useState<Notice | null>(null)

  /** "+ Topshiriq" — avval TUR tanlanadi (dizayn ekrani), keyin nom(lar) so'raladi. */
  const [picking, setPicking] = useState(false)
  /** "Boshqa" tabidan tanlangan eski tur — bir nechta nom bilan birdan yaratiladi. */
  const [bulkType, setBulkType] = useState<LessonType | null>(null)
  /** Mashq turi tanlangan — bitta nom so'raladi va konstruktor ochiladi. */
  const [newKind, setNewKind] = useState<ExerciseKind | null>(null)
  const [bulkBusy, setBulkBusy] = useState(false)

  const [editing, setEditing] = useState<CurriculumItem | null>(null)
  const [editBusy, setEditBusy] = useState(false)
  const [deleting, setDeleting] = useState<CurriculumItem | null>(null)
  const [deleteBusy, setDeleteBusy] = useState(false)

  /** silent=true — fon (background)da yangilaydi, butun sahifa "Yuklanmoqda..." holatiga o'tmaydi
   *  (tezkor qo'shish input fokusini yo'qotmasligi uchun). */
  const load = (silent = false) => {
    if (!silent) setLoading(true)
    return getCurriculum(curriculumId)
      .then(setData)
      .catch(() => setData(null))
      .finally(() => {
        if (!silent) setLoading(false)
      })
  }

  useEffect(() => {
    void load()
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [curriculumId, moduleId, topicId, lessonId])

  useEffect(() => {
    if (!notice) return
    const t = setTimeout(() => setNotice(null), 4000)
    return () => clearTimeout(t)
  }, [notice])

  const module = data?.modules.find((m) => m.id === moduleId)
  const topic: CurriculumTopic | undefined = module?.topics.find((t) => t.id === topicId)
  const lesson: CurriculumLesson | undefined = topic?.lessons.find((l) => l.id === lessonId)

  const patchItems = (fn: (items: CurriculumItem[]) => CurriculumItem[]) =>
    setData((d) =>
      d
        ? {
            ...d,
            modules: d.modules.map((m) =>
              m.id === moduleId
                ? {
                    ...m,
                    topics: m.topics.map((t) =>
                      t.id === topicId
                        ? { ...t, lessons: t.lessons.map((l) => (l.id === lessonId ? { ...l, items: fn(l.items) } : l)) }
                        : t,
                    ),
                  }
                : m,
            ),
          }
        : d,
    )

  /** "Boshqa" (eski) tur — bir nechta nom bilan birdan yaratish. */
  const bulkAdd = async (names: string[], type: LessonType) => {
    setBulkBusy(true)
    try {
      const created = await createItemsBulk(lessonId, names, type)
      await load(true)
      setBulkType(null)
      setPicking(false)
      setNotice({ type: 'success', text: `${created.length} ta topshiriq qo'shildi` })
    } catch (err) {
      setNotice({ type: 'error', text: apiErrorMessage(err, 'Xato yuz berdi') })
    } finally {
      setBulkBusy(false)
    }
  }

  /** Mashq turi tanlangan — topshiriq yaratiladi va DARHOL konstruktor ochiladi. */
  const addExercise = async (name: string) => {
    if (!newKind) return
    setBulkBusy(true)
    try {
      const { id } = await createItem(lessonId, name, 'exercise', '', newKind)
      setNewKind(null)
      setPicking(false)
      navigate(`/admin/curricula/${curriculumId}/${moduleId}/${topicId}/${lessonId}/${id}`)
    } catch (err) {
      setNotice({ type: 'error', text: apiErrorMessage(err, 'Xato yuz berdi') })
    } finally {
      setBulkBusy(false)
    }
  }

  const submitEdit = async (name: string, type: LessonType) => {
    if (!editing) return
    setEditBusy(true)
    try {
      await updateItem(editing.id, name, editing.note, type)
      patchItems((items) => items.map((it) => (it.id === editing.id ? { ...it, text: name, type } : it)))
      setEditing(null)
    } catch (err) {
      setNotice({ type: 'error', text: apiErrorMessage(err, 'Xato yuz berdi') })
    } finally {
      setEditBusy(false)
    }
  }

  const confirmDelete = async () => {
    if (!deleting) return
    setDeleteBusy(true)
    try {
      await deleteItem(deleting.id)
      patchItems((items) => items.filter((it) => it.id !== deleting.id))
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

  if (!module || !topic || !lesson) {
    return (
      <Card className="py-12 text-center text-slate-400">
        Dars topilmadi.
        <div className="mt-3">
          <Button variant="secondary" onClick={() => navigate(`/admin/curricula/${curriculumId}`)}>
            <ArrowLeft className="h-4 w-4" /> Modullarga qaytish
          </Button>
        </div>
      </Card>
    )
  }

  const ready = lesson.items.filter((it) => it.ready).length

  return (
    <div>
      <button
        type="button"
        onClick={() => navigate(`/admin/curricula/${curriculumId}/${moduleId}/${topicId}`)}
        className="mb-3 inline-flex items-center gap-1.5 text-sm font-medium text-slate-500 hover:text-slate-700"
      >
        <ArrowLeft className="h-4 w-4" /> {topic.title} darslariga qaytish
      </button>

      <PageHeader
        title={lesson.title}
        sub={
          lesson.items.length > 0
            ? `${lesson.items.length} ta topshiriq yaratilgan · ${ready} tasi tayyor`
            : "Hali topshiriq yo'q"
        }
        actions={
          <Button onClick={() => setPicking(true)}>
            <Plus className="h-4 w-4" /> Topshiriq
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

      {lesson.items.length === 0 ? (
        <Card className="py-12 text-center text-slate-400">
          <ListChecks className="mx-auto mb-2 h-6 w-6" />
          Hali topshiriq yaratilmagan. "+ Topshiriq" tugmasi bilan bir nechtasini birdan yarating.
        </Card>
      ) : (
        <Card tight className="overflow-hidden">
          <div className="overflow-x-auto">
            <table className="w-full text-sm">
              <thead>
                <tr className="border-b border-slate-100 bg-slate-50/70 text-left text-xs font-semibold uppercase tracking-wide text-slate-400">
                  <th className="w-12 px-4 py-3">No</th>
                  <th className="px-2 py-3">Nomi</th>
                  <th className="w-32 px-2 py-3">Topshiriqlar soni</th>
                  <th className="w-44 px-2 py-3">Topshiriq turi</th>
                  <th className="w-32 px-2 py-3">Yaratilgan sana</th>
                  <th className="w-28 px-4 py-3" />
                </tr>
              </thead>
              <tbody>
                {lesson.items.map((item, idx) => {
                  const meta = typeMeta(item.type)
                  const Icon = meta.icon
                  return (
                    <tr
                      key={item.id}
                      onClick={() => navigate(`/admin/curricula/${curriculumId}/${moduleId}/${topicId}/${lessonId}/${item.id}`)}
                      className="group cursor-pointer border-b border-slate-100 transition-colors last:border-0 hover:bg-slate-50/70"
                    >
                      <td className="px-4 py-4 text-slate-400">{idx + 1}</td>
                      <td className="px-2 py-4">
                        <div className="flex items-center gap-2.5">
                          {item.ready ? (
                            <span className="flex h-5 w-5 flex-shrink-0 items-center justify-center rounded-full bg-emerald-100 text-emerald-600">
                              <Check className="h-3.5 w-3.5" />
                            </span>
                          ) : (
                            <span className="h-5 w-5 flex-shrink-0 rounded-full border-2 border-slate-200" />
                          )}
                          <span className="text-[14.5px] font-semibold leading-6 text-slate-800">{item.text}</span>
                        </div>
                      </td>
                      <td className="px-2 py-4">
                        {item.count > 0 ? (
                          <span className="inline-flex items-center rounded-lg bg-slate-100 px-2 py-1 text-xs font-semibold text-slate-600">
                            {item.count} ta
                          </span>
                        ) : (
                          <span className="text-xs text-slate-300">—</span>
                        )}
                      </td>
                      <td className="px-2 py-4">
                        <span className="inline-flex items-center gap-1.5 rounded-full bg-brand-50 px-2.5 py-1 text-xs font-semibold text-brand-600">
                          {/* Mashqda aniq turi ko'rinadi ("Gap tuzish · so'z tartibi") — meta serverdan keladi. */}
                          <Icon className="h-3.5 w-3.5" /> {item.type === 'exercise' && item.meta ? item.meta : meta.label}
                        </span>
                      </td>
                      <td className="px-2 py-4 text-xs text-slate-400">
                        {item.createdAt ? formatDate(item.createdAt) : '—'}
                      </td>
                      <td className="px-4 py-4">
                        <div className="flex items-center justify-end gap-1 opacity-0 transition-opacity group-hover:opacity-100">
                          <button
                            type="button"
                            onClick={(e) => {
                              e.stopPropagation()
                              setEditing(item)
                            }}
                            className="flex h-8 w-8 items-center justify-center rounded-lg text-slate-400 transition-colors hover:bg-slate-100 hover:text-slate-600"
                            title="Nomi/turini tahrirlash"
                          >
                            <Pencil className="h-4 w-4" />
                          </button>
                          <button
                            type="button"
                            onClick={(e) => {
                              e.stopPropagation()
                              setDeleting(item)
                            }}
                            className="flex h-8 w-8 items-center justify-center rounded-lg text-slate-400 transition-colors hover:bg-red-50 hover:text-red-500"
                            title="O'chirish"
                          >
                            <Trash2 className="h-4 w-4" />
                          </button>
                          <ChevronRight className="h-4 w-4 text-slate-300" />
                        </div>
                      </td>
                    </tr>
                  )
                })}
              </tbody>
            </table>
          </div>
        </Card>
      )}

      {/* "+ Topshiriq" — TUR TANLASH oynasi (katta modal karta): 8 mashq kategoriyasi va
          oxirida "Boshqa" tabi (eski turlar: video/matn/audio/PDF/lug'at/test). */}
      {picking && (
        <ExercisePicker
          subtitle={lesson.title}
          onPick={(k) => setNewKind(k)}
          onPickLegacy={(t) => setBulkType(t)}
          onClose={() => setPicking(false)}
        />
      )}

      {/* Mashq turi tanlandi — bitta nom so'raladi, so'ng konstruktor ochiladi */}
      <NameModal
        open={!!newKind}
        title="Yangi mashq"
        label="Topshiriq nomi"
        placeholder="Masalan: 1-mashq. Gap tuzish"
        initialValue={newKind ? kindTitle(newKind) : ''}
        hint={newKind ? `Tur: ${kindInfo(newKind)?.type.name ?? ''} — yaratilgach konstruktor ochiladi.` : undefined}
        submitLabel="Yaratish"
        busy={bulkBusy}
        onClose={() => setNewKind(null)}
        onSubmit={addExercise}
      />

      {/* "Boshqa" turi tanlandi — eski oqim: bir nechta nom, birdan yaratish */}
      <BulkAddModal
        open={!!bulkType}
        type={bulkType ?? 'video'}
        busy={bulkBusy}
        onClose={() => setBulkType(null)}
        onSubmit={bulkAdd}
      />

      <ItemEditModal
        open={!!editing}
        initialName={editing?.text ?? ''}
        initialType={editing?.type ?? 'text'}
        busy={editBusy}
        onClose={() => setEditing(null)}
        onSubmit={submitEdit}
      />

      <ConfirmDeleteModal
        open={!!deleting}
        title="Topshiriqni o'chirish"
        message={
          deleting && (
            <>
              <b>"{deleting.text}"</b> topshirig'i o'chiriladi. Bu amalni qaytarib bo'lmaydi.
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

// ============================ Bir nechta topshiriq yaratish modali (ro'yxat) ============================

interface BulkAddModalProps {
  open: boolean
  /** Tur "Boshqa" tabida ALLAQACHON tanlangan — modalda faqat nomlar kiritiladi. */
  type: LessonType
  busy: boolean
  onClose: () => void
  onSubmit: (names: string[], type: LessonType) => void
}

const INITIAL_ROWS = 3

/** Tur tanlash ekranida turini tanlaysiz, bu yerda bir nechta nom kiritasiz (qatorlar), BIR marta
 *  "N ta yaratish" bosasiz — hammasi bitta amal bilan yaratiladi. Har qatorda Enter — oxirgisida
 *  yangi qator qo'shadi, boshqasida keyingi qatorga o'tadi (jadval kabi tez to'ldirish uchun). */
function BulkAddModal({ open, type, busy, onClose, onSubmit }: BulkAddModalProps) {
  const [names, setNames] = useState<string[]>(() => Array(INITIAL_ROWS).fill(''))
  const inputsRef = useRef<(HTMLInputElement | null)[]>([])
  const prevLenRef = useRef(names.length)

  useEffect(() => {
    if (open) {
      setNames(Array(INITIAL_ROWS).fill(''))
      prevLenRef.current = INITIAL_ROWS
    }
  }, [open])

  useEffect(() => {
    if (names.length > prevLenRef.current) inputsRef.current[names.length - 1]?.focus()
    prevLenRef.current = names.length
  }, [names.length])

  const setName = (i: number, v: string) => setNames((prev) => prev.map((n, idx) => (idx === i ? v : n)))
  const addRow = () => setNames((prev) => [...prev, ''])
  const removeRow = (i: number) => setNames((prev) => (prev.length > 1 ? prev.filter((_, idx) => idx !== i) : prev))

  const onKeyDown = (i: number, e: React.KeyboardEvent<HTMLInputElement>) => {
    if (e.key !== 'Enter') return
    e.preventDefault()
    if (i === names.length - 1) addRow()
    else inputsRef.current[i + 1]?.focus()
  }

  const validNames = names.map((n) => n.trim()).filter(Boolean)

  const submit = (e: React.FormEvent) => {
    e.preventDefault()
    if (validNames.length === 0 || busy) return
    onSubmit(validNames, type)
  }

  return (
    <Modal
      open={open}
      onClose={onClose}
      size="sm"
      title="Yangi topshiriqlar"
      footer={
        <>
          <Button variant="secondary" onClick={onClose} disabled={busy}>
            Bekor qilish
          </Button>
          <Button type="submit" form="bulk-item-form" disabled={busy || validNames.length === 0}>
            {busy ? <Loader2 className="h-4 w-4 animate-spin" /> : <Plus className="h-4 w-4" />}
            {validNames.length > 0 ? `${validNames.length} ta yaratish` : 'Yaratish'}
          </Button>
        </>
      }
    >
      <form id="bulk-item-form" onSubmit={submit} className="space-y-4">
        <div>
          <label className="mb-1.5 block text-xs font-semibold text-slate-500">
            Turi <span className="font-normal text-slate-400">— barchasi shu turda yaratiladi</span>
          </label>
          <span className="inline-flex items-center gap-1.5 rounded-lg border border-brand-400 bg-brand-50 px-3 py-2 text-[13px] font-semibold text-brand-700">
            {(() => {
              const TIcon = typeMeta(type).icon
              return <TIcon className="h-4 w-4" />
            })()}
            {typeMeta(type).label}
          </span>
        </div>

        <div>
          <label className="mb-1.5 block text-xs font-semibold text-slate-500">Topshiriq nomlari</label>
          <div className="max-h-64 space-y-2 overflow-y-auto pr-1">
            {names.map((name, i) => (
              <div key={i} className="flex items-center gap-2">
                <span className="w-4 flex-shrink-0 text-xs font-medium text-slate-400">{i + 1}.</span>
                <input
                  ref={(el) => {
                    inputsRef.current[i] = el
                  }}
                  autoFocus={i === 0}
                  value={name}
                  onChange={(e) => setName(i, e.target.value)}
                  onKeyDown={(e) => onKeyDown(i, e)}
                  placeholder={`${i + 1}-topshiriq nomi...`}
                  className={control}
                />
                <button
                  type="button"
                  onClick={() => removeRow(i)}
                  disabled={names.length <= 1}
                  className="flex-shrink-0 rounded-lg p-2 text-slate-300 transition-colors hover:bg-red-50 hover:text-red-600 disabled:opacity-30 disabled:hover:bg-transparent disabled:hover:text-slate-300"
                  title="Qatorni olib tashlash"
                >
                  <X className="h-4 w-4" />
                </button>
              </div>
            ))}
          </div>
          <button
            type="button"
            onClick={addRow}
            className="mt-2 inline-flex items-center gap-1.5 rounded-lg px-2 py-1 text-xs font-semibold text-brand-600 transition-colors hover:text-brand-700"
          >
            <Plus className="h-3.5 w-3.5" /> yana nom qo'shish
          </button>
        </div>

        <p className="text-xs leading-relaxed text-slate-400">
          Har qatorda Enter bossangiz — oxirgi qatorda yangisi qo'shiladi, boshqasida keyingi
          qatorga o'tasiz. Bo'sh qoldirilgan qatorlar e'tiborga olinmaydi.
        </p>
      </form>
    </Modal>
  )
}

// ============================ Topshiriqni tahrirlash modali (nom + tur) ============================

interface ItemEditModalProps {
  open: boolean
  initialName: string
  initialType: LessonType
  busy: boolean
  onClose: () => void
  onSubmit: (name: string, type: LessonType) => void
}

function ItemEditModal({ open, initialName, initialType, busy, onClose, onSubmit }: ItemEditModalProps) {
  const [name, setName] = useState(initialName)
  const [type, setType] = useState<LessonType>(initialType)
  useEffect(() => {
    if (open) {
      setName(initialName)
      setType(initialType)
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [open])

  const submit = (e: React.FormEvent) => {
    e.preventDefault()
    const v = name.trim()
    if (!v || busy) return
    onSubmit(v, type)
  }

  return (
    <Modal
      open={open}
      onClose={onClose}
      size="sm"
      title="Topshiriqni tahrirlash"
      footer={
        <>
          <Button variant="secondary" onClick={onClose} disabled={busy}>
            Bekor qilish
          </Button>
          <Button type="submit" form="curriculum-item-edit-form" disabled={busy || !name.trim()}>
            {busy ? <Loader2 className="h-4 w-4 animate-spin" /> : <Check className="h-4 w-4" />}
            Saqlash
          </Button>
        </>
      }
    >
      <form id="curriculum-item-edit-form" onSubmit={submit} className="space-y-4">
        <div>
          <label className="mb-1.5 block text-xs font-semibold text-slate-500">Topshiriq nomi</label>
          <input
            autoFocus
            value={name}
            onChange={(e) => setName(e.target.value)}
            placeholder="Masalan: 1-topshiriq. Tanishuv..."
            className={control}
          />
        </div>
        <div>
          <label className="mb-1.5 block text-xs font-semibold text-slate-500">Turi</label>
          <div className="flex flex-wrap gap-2">
            {LESSON_TYPES.map((t) => {
              const TIcon = t.icon
              const on = type === t.type
              return (
                <button
                  key={t.type}
                  type="button"
                  onClick={() => setType(t.type)}
                  className={cn(
                    'inline-flex items-center gap-1.5 rounded-lg border px-3 py-2 text-[13px] font-semibold transition-colors',
                    on
                      ? 'border-brand-400 bg-brand-50 text-brand-700'
                      : 'border-slate-200 bg-white text-slate-600 hover:border-slate-300 hover:bg-slate-50',
                  )}
                >
                  <TIcon className="h-4 w-4" /> {t.label}
                </button>
              )
            })}
          </div>
        </div>
      </form>
    </Modal>
  )
}
