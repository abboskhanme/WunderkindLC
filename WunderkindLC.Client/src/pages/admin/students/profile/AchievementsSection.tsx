import { useEffect, useState } from 'react'
import { CalendarDays, Image as ImageIcon, Pencil, Plus, Send, Trash2, Trophy, X } from 'lucide-react'
import {
  ACHIEVEMENT_KINDS,
  congratulateAchievement,
  createAchievement,
  deleteAchievement,
  fetchAchievementPhotoUrl,
  getAchievements,
  updateAchievement,
  uploadAchievementPhoto,
  type Achievement,
  type AchievementKind,
} from '@/api/services/achievements'
import { AttachmentPicker, type PickedAttachment } from '@/components/messaging/AttachmentPicker'
import { useBlobUrl } from '@/components/messaging/useBlobUrl'
import { Button } from '@/components/ui/Button'
import { Card } from '@/components/ui/Card'
import { Input, Select, Textarea } from '@/components/ui/Input'
import { Modal } from '@/components/ui/Modal'
import { localIsoDate } from '@/lib/localDate'
import { toast } from '@/lib/toast'
import { apiErrorMessage, formatDate, formatDateTime } from '@/lib/utils'
import { Empty } from './shared'

/** Rasm kaliti: `{id}#{versiya}` — tahrirdan keyin rasm qayta olinadi (id o'zgarmaydi). */
const loadPhoto = (key: string) => fetchAchievementPhotoUrl(key.split('#')[0])

/** Saqlangan yutuq rasmi eskizi yutuq endpointidan keladi (pastda) — biriktirma endpointi `messages.broadcast`
 *  ruxsatini talab qiladi, shuning uchun picker o'zi so'ramaydi. */
const noRemoteThumb = () => Promise.reject(new Error('achievement thumbnail comes from the achievement endpoint'))

/**
 * «Yutuqlar» (CRM upgrade U2c) — o'quvchining IELTS / CEFR / sertifikat / olimpiada natijalari.
 * Qo'shishda ota-onaga Telegram tabrik («Yutuq» avto-xabari) yuboriladi; tabrik yuborilmasa yutuq
 * baribir saqlanadi va SABAB (`warning`) shu yerda ko'rinadi.
 *
 * Ro'yxat — tabning ASOSIY ma'lumoti: o'qish xatosi ko'rsatiladi. Qator amallari (o'chirish, tabrik)
 * xatosi — `alert`, forma xatosi — oyna ichida, oyna yopilmaydi (`error-visibility.md`).
 */
export function AchievementsSection({
  studentId,
  canCreate,
  canEdit,
  canDelete,
}: {
  studentId: string
  canCreate: boolean
  canEdit: boolean
  canDelete: boolean
}) {
  const [rows, setRows] = useState<Achievement[] | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [tick, setTick] = useState(0)
  const [busyId, setBusyId] = useState<string | null>(null)
  const [warning, setWarning] = useState<string | null>(null)
  // `null` — yopiq; `'new'` — yangi; aks holda tahrirlanayotgan yutuq.
  const [form, setForm] = useState<Achievement | 'new' | null>(null)

  useEffect(() => {
    let alive = true
    getAchievements(studentId)
      .then((r) => {
        if (!alive) return
        setRows(r)
        setError(null)
      })
      .catch((e) => {
        if (alive) setError(apiErrorMessage(e, "Yutuqlarni yuklab bo'lmadi"))
      })
    return () => {
      alive = false
    }
  }, [studentId, tick])

  const remove = async (a: Achievement) => {
    if (!confirm(`"${a.kindLabel}: ${a.title}" yutug'i o'chirilsinmi?`)) return
    setBusyId(a.id)
    try {
      await deleteAchievement(a.id)
      setTick((t) => t + 1)
    } catch (e) {
      alert(apiErrorMessage(e, "Yutuqni o'chirib bo'lmadi"))
    } finally {
      setBusyId(null)
    }
  }

  const congratulate = async (a: Achievement) => {
    const again = a.congratulatedAt ? ' (tabrik avval ham yuborilgan)' : ''
    if (!confirm(`Ota-onaga "${a.title}" uchun Telegram tabrik yuborilsinmi?${again}`)) return
    setBusyId(a.id)
    try {
      const { messages } = await congratulateAchievement(a.id)
      if (messages > 0) toast.success('Tabrik navbatga qo\'yildi', `${messages} ta xabar`)
      else setWarning("Tabrik navbatga qo'yilmadi: ota-ona Telegram botga ulanmagan")
      setTick((t) => t + 1)
    } catch (e) {
      alert(apiErrorMessage(e, "Tabrikni yuborib bo'lmadi"))
    } finally {
      setBusyId(null)
    }
  }

  return (
    <Card>
      <div className="mb-4 flex flex-wrap items-center gap-x-2 gap-y-3">
        <Trophy className="h-5 w-5 text-amber-500" />
        <h2 className="font-semibold text-slate-800">Yutuqlar</h2>
        {rows && rows.length > 0 && <span className="text-xs text-slate-400">{rows.length} ta</span>}
        {canCreate && (
          <Button className="ml-auto" onClick={() => setForm('new')}>
            <Plus className="mr-1 inline h-4 w-4" /> Yutuq qo'shish
          </Button>
        )}
      </div>

      {warning && (
        <div className="mb-3 flex items-start gap-2 rounded-lg border border-amber-200 bg-amber-50 px-3 py-2 text-sm text-amber-800">
          <span className="flex-1">{warning}</span>
          <button type="button" aria-label="Yopish" onClick={() => setWarning(null)} className="text-amber-700 hover:text-amber-900">
            <X className="h-4 w-4" />
          </button>
        </div>
      )}

      {error ? (
        <p className="text-sm text-red-600">{error}</p>
      ) : rows === null ? (
        <p className="py-6 text-center text-sm text-slate-400">Yuklanmoqda...</p>
      ) : rows.length === 0 ? (
        <Empty>Hali yutuq qo'shilmagan.</Empty>
      ) : (
        <div className="grid gap-3 sm:grid-cols-2 lg:grid-cols-3">
          {rows.map((a) => (
            <AchievementCard
              key={a.id}
              a={a}
              version={tick}
              busy={busyId === a.id}
              onEdit={canEdit ? () => setForm(a) : undefined}
              onDelete={canDelete ? () => remove(a) : undefined}
              onCongratulate={canCreate ? () => congratulate(a) : undefined}
            />
          ))}
        </div>
      )}

      {form && (
        <AchievementFormModal
          key={form === 'new' ? 'new' : form.id}
          studentId={studentId}
          initial={form === 'new' ? null : form}
          onClose={() => setForm(null)}
          onSaved={(saved, created) => {
            setForm(null)
            setTick((t) => t + 1)
            if (saved.warning) setWarning(saved.warning)
            else if (created && (saved.messages ?? 0) > 0)
              toast.success('Yutuq saqlandi', `Tabrik: ${saved.messages} ta xabar navbatga qo'yildi`)
            else toast.success('Saqlandi')
          }}
        />
      )}
    </Card>
  )
}

function AchievementCard({
  a,
  version,
  busy,
  onEdit,
  onDelete,
  onCongratulate,
}: {
  a: Achievement
  version: number
  busy: boolean
  onEdit?: () => void
  onDelete?: () => void
  onCongratulate?: () => void
}) {
  const photo = useBlobUrl(a.hasPhoto ? `${a.id}#${version}` : null, loadPhoto)
  return (
    <div className="flex flex-col gap-2 rounded-xl border border-slate-200 bg-gradient-to-br from-amber-50/60 to-slate-50 p-3">
      <div className="flex items-start gap-3">
        {a.hasPhoto ? (
          photo ? (
            <a href={photo} target="_blank" rel="noreferrer" className="shrink-0" title="Rasmni ochish">
              <img src={photo} alt={a.title} className="h-16 w-16 rounded-lg object-cover" />
            </a>
          ) : (
            <span className="flex h-16 w-16 shrink-0 items-center justify-center rounded-lg bg-white text-slate-400">
              <ImageIcon className="h-5 w-5" />
            </span>
          )
        ) : (
          <span className="flex h-16 w-16 shrink-0 items-center justify-center rounded-lg bg-white text-amber-500">
            <Trophy className="h-6 w-6" />
          </span>
        )}
        <div className="min-w-0 flex-1">
          <span className="inline-block rounded-md bg-amber-100 px-1.5 py-0.5 text-[11px] font-semibold text-amber-800">
            {a.kindLabel}
          </span>
          <p className="mt-1 break-words font-semibold text-slate-800">{a.title}</p>
          {a.result && <p className="text-sm font-medium text-brand-700">Natija: {a.result}</p>}
        </div>
      </div>
      <p className="flex items-center gap-1.5 text-xs text-slate-500">
        <CalendarDays className="h-3.5 w-3.5 text-slate-400" /> {formatDate(a.date)}
      </p>
      {a.note && <p className="whitespace-pre-line break-words text-xs text-slate-500">{a.note}</p>}
      <p className="text-xs text-slate-400">
        {a.congratulatedAt ? `Tabrik yuborilgan: ${formatDateTime(a.congratulatedAt)}` : 'Tabrik yuborilmagan'}
      </p>
      {(onEdit || onDelete || onCongratulate) && (
        <div className="mt-auto flex flex-wrap gap-1.5 pt-1">
          {onCongratulate && (
            <button
              type="button"
              disabled={busy}
              onClick={onCongratulate}
              className="inline-flex items-center gap-1 rounded-lg border border-brand-200 bg-white px-2.5 py-1.5 text-xs font-semibold text-brand-700 transition-colors hover:bg-brand-50 disabled:opacity-50"
            >
              <Send className="h-3.5 w-3.5" /> Tabrik yuborish
            </button>
          )}
          {onEdit && (
            <button
              type="button"
              disabled={busy}
              onClick={onEdit}
              className="inline-flex items-center gap-1 rounded-lg border border-slate-200 bg-white px-2.5 py-1.5 text-xs font-semibold text-slate-600 transition-colors hover:bg-slate-50 disabled:opacity-50"
            >
              <Pencil className="h-3.5 w-3.5" /> Tahrirlash
            </button>
          )}
          {onDelete && (
            <button
              type="button"
              disabled={busy}
              onClick={onDelete}
              className="inline-flex items-center gap-1 rounded-lg border border-slate-200 bg-white px-2.5 py-1.5 text-xs font-semibold text-slate-600 transition-colors hover:border-red-200 hover:bg-red-50 hover:text-red-600 disabled:opacity-50"
            >
              <Trash2 className="h-3.5 w-3.5" /> O'chirish
            </button>
          )}
        </div>
      )}
    </div>
  )
}

function AchievementFormModal({
  studentId,
  initial,
  onClose,
  onSaved,
}: {
  studentId: string
  initial: Achievement | null
  onClose: () => void
  onSaved: (saved: Achievement, created: boolean) => void
}) {
  const today = localIsoDate(new Date())
  const [kind, setKind] = useState<AchievementKind>(initial?.kind ?? 'ielts')
  const [title, setTitle] = useState(initial?.title ?? '')
  const [result, setResult] = useState(initial?.result ?? '')
  const [date, setDate] = useState(initial?.date || today)
  const [note, setNote] = useState(initial?.note ?? '')
  // Tahrirda joriy rasm biriktirmasi saqlanadi (PUT da `attachmentId` bo'sh kelsa server rasmni olib tashlaydi).
  const [photo, setPhoto] = useState<PickedAttachment | null>(
    initial?.hasPhoto && initial.attachmentId
      ? { id: initial.attachmentId, kind: 'photo', fileName: 'Joriy rasm', sizeBytes: 0, contentType: 'image/jpeg' }
      : null,
  )
  const [congratulate, setCongratulate] = useState(true)
  const [saving, setSaving] = useState(false)
  const [error, setError] = useState<string | null>(null)

  // Joriy rasm eskizi — yutuq endpointidan (biriktirma endpointi boshqa ruxsat talab qiladi).
  const currentThumb = useBlobUrl(initial?.hasPhoto ? initial.id : null, fetchAchievementPhotoUrl)
  const pickerValue =
    photo && initial?.attachmentId === photo.id && !photo.thumbUrl && currentThumb
      ? { ...photo, thumbUrl: currentThumb }
      : photo
  // Eski backend biriktirma id'sini bermasa — saqlash rasmni olib tashlaydi: buni OCHIQ aytamiz.
  const photoUnknown = !!initial?.hasPhoto && !initial.attachmentId && !photo

  const save = async () => {
    if (saving) return
    if (!title.trim()) {
      setError('Yutuq nomini kiriting')
      return
    }
    if (!date) {
      setError('Sanani tanlang')
      return
    }
    setSaving(true)
    setError(null)
    const req = {
      studentId,
      kind,
      title: title.trim(),
      result: result.trim(),
      date,
      note: note.trim(),
      attachmentId: photo?.id ?? null,
    }
    try {
      const saved = initial
        ? await updateAchievement(initial.id, req)
        : await createAchievement({ ...req, congratulate })
      onSaved(saved, !initial)
    } catch (e) {
      setError(apiErrorMessage(e, "Yutuqni saqlab bo'lmadi"))
    } finally {
      setSaving(false)
    }
  }

  return (
    <Modal
      open
      onClose={() => !saving && onClose()}
      title={initial ? 'Yutuqni tahrirlash' : "Yutuq qo'shish"}
      footer={
        <>
          <Button variant="secondary" onClick={onClose} disabled={saving}>
            Bekor qilish
          </Button>
          <Button onClick={save} disabled={saving}>
            {saving ? 'Saqlanmoqda...' : 'Saqlash'}
          </Button>
        </>
      }
    >
      <form
        className="space-y-3"
        onSubmit={(e) => {
          e.preventDefault()
          void save()
        }}
      >
        <div className="grid gap-3 sm:grid-cols-2">
          <Select label="Turi" required value={kind} onChange={(e) => setKind(e.target.value as AchievementKind)}>
            {ACHIEVEMENT_KINDS.map((k) => (
              <option key={k.key} value={k.key}>
                {k.label}
              </option>
            ))}
          </Select>
          <Input
            label="Sana"
            required
            type="date"
            max={today}
            value={date}
            onChange={(e) => setDate(e.target.value)}
          />
        </div>
        <Input
          label="Nomi"
          required
          maxLength={200}
          placeholder="masalan: IELTS Academic"
          value={title}
          onChange={(e) => setTitle(e.target.value)}
        />
        <Input
          label="Natija"
          maxLength={50}
          placeholder="masalan: 7.5 yoki B2"
          value={result}
          onChange={(e) => setResult(e.target.value)}
        />
        <Textarea label="Izoh" rows={3} maxLength={1000} value={note} onChange={(e) => setNote(e.target.value)} />
        <AttachmentPicker
          value={pickerValue}
          onChange={setPhoto}
          photoOnly
          label="Rasm"
          disabled={saving}
          upload={uploadAchievementPhoto}
          fetchThumb={noRemoteThumb}
        />
        {photoUnknown && (
          <p className="text-xs font-medium text-amber-700">
            Joriy rasm bu oynada ko'rsatilmadi — saqlasangiz rasm olib tashlanadi. Rasmni qayta biriktiring.
          </p>
        )}
        {!initial && (
          <label className="flex cursor-pointer items-center gap-2 text-sm text-slate-700">
            <input
              type="checkbox"
              checked={congratulate}
              onChange={(e) => setCongratulate(e.target.checked)}
              className="h-4 w-4 accent-brand-600"
            />
            Ota-onaga tabrik yuborish
          </label>
        )}
        {error && <p className="text-sm font-medium text-red-600">{error}</p>}
        <button type="submit" hidden />
      </form>
    </Modal>
  )
}
