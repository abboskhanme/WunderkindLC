import { useEffect, useState } from 'react'
import { FileText, Loader2, Phone, Save, Send, Trash2, User } from 'lucide-react'
import type { ApplicationStatus, CareerStage, JobApplication } from '@/api/services/career'
import {
  deleteApplication, getApplication, setApplicationNote, setApplicationStatus,
} from '@/api/services/career'
import { Modal } from '@/components/ui/Modal'
import { Button } from '@/components/ui/Button'
import { Badge } from '@/components/ui/Badge'
import { Loader } from '@/components/ui/Loader'
import { Select, Textarea } from '@/components/ui/Input'
import { apiErrorMessage, cn, formatDateTime, openTelegram } from '@/lib/utils'
import { statusIcons, statusLabels, statusOrder, statusTones } from './careerLabels'

interface Props {
  open: boolean
  applicationId: string | null
  stages: CareerStage[]
  canEdit: boolean
  canDelete: boolean
  onClose: () => void
  onChanged: (app: JobApplication) => void
  onDeleted: (id: string) => void
}

/**
 * Ariza tafsiloti — nomzod ma'lumotlari, CV, bosqichlar TARIXI va bosqichni o'zgartirish.
 *
 * DIQQAT: bosqich o'zgarganda kiritilgan izoh NOMZODGA ko'rinadi (unga karyera botida xabar
 * ketadi) — masalan suhbat vaqti yoki rad etish sababi. Faqat ichki eslatma uchun pastdagi
 * "Ichki izoh" maydoni ishlatiladi (u hech qachon yuborilmaydi).
 */
export function ApplicationDetailModal({
  open, applicationId, stages, canEdit, canDelete, onClose, onChanged, onDeleted,
}: Props) {
  const [app, setApp] = useState<JobApplication | null>(null)
  const [loading, setLoading] = useState(false)
  const [status, setStatus] = useState<ApplicationStatus>('new')
  const [note, setNote] = useState('')
  const [adminNote, setAdminNote] = useState('')
  const [busy, setBusy] = useState(false)
  const [savingNote, setSavingNote] = useState(false)
  const [error, setError] = useState('')

  useEffect(() => {
    if (!open || !applicationId) return
    // eslint-disable-next-line react-hooks/set-state-in-effect -- modal ochilganda yuklash (maqsadli)
    setLoading(true)
    setError('')
    setNote('')
    getApplication(applicationId)
      .then((a) => {
        setApp(a)
        setStatus(a.status)
        setAdminNote(a.adminNote)
      })
      .catch((err) => setError(apiErrorMessage(err, "Arizani yuklab bo'lmadi")))
      .finally(() => setLoading(false))
  }, [open, applicationId])

  /** Bosqich tanlagichi: server katalogi (bo'lsa), aks holda lokal zaxira ro'yxat. */
  const list: CareerStage[] = stages.length > 0
    ? stages
    : statusOrder.map((k, i) => ({
        key: k, label: statusLabels[k], candidateText: '', icon: statusIcons[k],
        order: i + 1, isFinal: k === 'hired' || k === 'rejected',
      }))
  const history = app?.history ?? []

  const applyStatus = async () => {
    if (!app || busy) return
    setBusy(true)
    setError('')
    try {
      const saved = await setApplicationStatus(app.id, status, note.trim())
      const fresh = await getApplication(app.id)
      setApp(fresh)
      setNote('')
      onChanged(saved)
    } catch (err) {
      setError(apiErrorMessage(err, "Bosqichni o'zgartirib bo'lmadi"))
    } finally {
      setBusy(false)
    }
  }

  const saveAdminNote = async () => {
    if (!app || savingNote) return
    setSavingNote(true)
    try {
      onChanged(await setApplicationNote(app.id, adminNote))
    } catch (err) {
      setError(apiErrorMessage(err, "Izohni saqlab bo'lmadi"))
    } finally {
      setSavingNote(false)
    }
  }

  const remove = async () => {
    if (!app) return
    if (!window.confirm(`#${app.number} — ${app.fullName} arizasi o'chirilsinmi?`)) return
    try {
      await deleteApplication(app.id)
      onDeleted(app.id)
      onClose()
    } catch (err) {
      setError(apiErrorMessage(err, "O'chirib bo'lmadi"))
    }
  }

  return (
    <Modal
      open={open}
      onClose={onClose}
      size="lg"
      title={app ? `Ariza #${app.number} — ${app.fullName}` : 'Ariza'}
      footer={
        canDelete && app ? (
          <Button variant="danger" onClick={remove}>
            <Trash2 className="h-4 w-4" /> O'chirish
          </Button>
        ) : undefined
      }
    >
      {loading || !app ? (
        <Loader label="Yuklanmoqda..." />
      ) : (
        <div className="space-y-4">
          {error && <p className="text-sm font-medium text-red-600">{error}</p>}

          {/* ---------- Nomzod ---------- */}
          <div className="rounded-xl border border-slate-200 p-4">
            <div className="mb-3 flex flex-wrap items-center justify-between gap-2">
              <span className="text-xs font-semibold uppercase tracking-wide text-slate-400">
                Nomzod
              </span>
              <Badge tone={statusTones[app.status]}>
                {statusIcons[app.status]} {statusLabels[app.status]}
              </Badge>
            </div>

            <div className="grid gap-2 text-sm sm:grid-cols-2">
              <div className="flex items-center gap-2">
                <User className="h-4 w-4 text-slate-400" />
                <span className="font-semibold text-slate-800">{app.fullName}</span>
              </div>
              <div className="flex items-center gap-2">
                <Phone className="h-4 w-4 text-slate-400" />
                <a href={`tel:${app.phone}`} className="text-brand-600 hover:underline">
                  {app.phone}
                </a>
              </div>
              <div className="text-slate-500">
                Vakansiya: <span className="font-medium text-slate-700">{app.vacancyTitle}</span>
              </div>
              <div className="text-slate-500">Yuborilgan: {formatDateTime(app.createdAt)}</div>
            </div>

            <div className="mt-3 flex flex-wrap gap-2">
              {app.tgUsername && (
                <Button variant="secondary" onClick={() => openTelegram(app.tgUsername)}>
                  <Send className="h-3.5 w-3.5" /> @{app.tgUsername}
                </Button>
              )}
              {app.cvUrl && (
                <a
                  href={app.cvUrl}
                  target="_blank"
                  rel="noreferrer"
                  className="inline-flex items-center gap-1.5 rounded-lg border border-slate-200 bg-white px-3.5 py-2 text-[13px] font-semibold text-slate-700 hover:bg-slate-50"
                >
                  <FileText className="h-3.5 w-3.5" /> CV: {app.cvName || 'yuklab olish'}
                </a>
              )}
            </div>
          </div>

          {/* ---------- Tajriba / motivatsiya ---------- */}
          {app.experience && (
            <Section title="Ish tajribasi">{app.experience}</Section>
          )}
          <Section title="Motivatsion xat">{app.motivation}</Section>

          {/* ---------- Bosqich o'zgartirish ---------- */}
          {canEdit && (
            <div className="rounded-xl border border-brand-100 bg-brand-50/40 p-4">
              <p className="mb-3 text-xs font-semibold uppercase tracking-wide text-slate-500">
                Bosqichni o'zgartirish
              </p>
              <div className="space-y-3">
                <Select
                  label="Yangi bosqich"
                  value={status}
                  onChange={(e) => setStatus(e.target.value as ApplicationStatus)}
                >
                  {list.map((s) => (
                    <option key={s.key} value={s.key}>
                      {s.icon} {s.label}
                    </option>
                  ))}
                </Select>
                <Textarea
                  label="Nomzodga izoh"
                  rows={3}
                  value={note}
                  onChange={(e) => setNote(e.target.value)}
                  placeholder="Masalan: Suhbat 12-avgust, 15:00 da markaz binosida"
                />
                <p className="text-xs text-slate-400">
                  Bu matn nomzodga botda va «Arizalarim» bo'limida ko'rinadi.
                </p>
                <Button onClick={applyStatus} disabled={busy}>
                  {busy ? <Loader2 className="h-4 w-4 animate-spin" /> : <Send className="h-4 w-4" />}
                  Saqlash va xabar berish
                </Button>
              </div>
            </div>
          )}

          {/* ---------- Bosqichlar tarixi ---------- */}
          {history.length > 0 && (
            <div className="rounded-xl border border-slate-200 p-4">
              <p className="mb-3 text-xs font-semibold uppercase tracking-wide text-slate-400">
                Bosqichlar tarixi
              </p>
              <ol className="space-y-3">
                {history.map((h, i) => (
                  <li key={`${h.createdAt}-${i}`} className="flex gap-3">
                    <span
                      className={cn(
                        'mt-0.5 flex h-6 w-6 flex-none items-center justify-center rounded-full text-[11px]',
                        i === history.length - 1
                          ? 'bg-brand-600 text-white'
                          : 'bg-slate-100 text-slate-500',
                      )}
                    >
                      {statusIcons[h.status]}
                    </span>
                    <div className="min-w-0">
                      <p className="text-sm font-semibold text-slate-700">
                        {statusLabels[h.status]}
                      </p>
                      {h.note && <p className="text-sm text-slate-500">{h.note}</p>}
                      <p className="text-xs text-slate-400">
                        {formatDateTime(h.createdAt)} · {h.createdBy}
                      </p>
                    </div>
                  </li>
                ))}
              </ol>
            </div>
          )}

          {/* ---------- Ichki izoh ---------- */}
          {canEdit && (
            <div className="rounded-xl border border-slate-200 p-4">
              <Textarea
                label="Ichki izoh (nomzodga ko'rinmaydi)"
                rows={3}
                value={adminNote}
                onChange={(e) => setAdminNote(e.target.value)}
                placeholder="Jamoa uchun eslatma"
              />
              <Button className="mt-2" variant="secondary" onClick={saveAdminNote} disabled={savingNote}>
                {savingNote ? <Loader2 className="h-4 w-4 animate-spin" /> : <Save className="h-4 w-4" />}
                Izohni saqlash
              </Button>
            </div>
          )}
        </div>
      )}
    </Modal>
  )
}

function Section({ title, children }: { title: string; children: string }) {
  return (
    <div className="rounded-xl border border-slate-200 p-4">
      <p className="mb-2 text-xs font-semibold uppercase tracking-wide text-slate-400">{title}</p>
      <p className="whitespace-pre-line text-sm leading-relaxed text-slate-700">{children}</p>
    </div>
  )
}
