import { useRef, useState } from 'react'
import { Camera, Check, FileText, Mic, Paperclip, X } from 'lucide-react'
import { addMyProgress, completeMyTask, createMyTask, hrErrorCode, hrErrorMessage, hrErrorStatus, uploadMyProof, type HrMyTask } from '@/api/services/hrTasks'
import { HR_MAX_PROOFS, HR_PROOF_ACCEPT } from '@/types/hrTasks'
import { Button } from '@/components/ui/Button'
import { Modal } from '@/components/ui/Modal'
import { DateTimeField, Field, inputCls } from '@/components/hr-tasks/ui'
import { hrFormatBytes, hrFormatDateTime, hrProgressText, hrProofFileError, hrToIso, hrVoiceDuration } from '@/components/hr-tasks/lib'
import { cn } from '@/lib/utils'
import { ProofVoiceButton } from './ProofVoiceButton'

const footerCls = 'flex w-full flex-col-reverse gap-2 sm:flex-row sm:justify-end'

/** Uzbek text for a "Bajardim" error (the old `hrMyCompleteError`). */
function completeError(err: unknown): string {
  const code = hrErrorCode(err)
  // `/api/hr/me/*` drops the `hr_` prefix (`proof_required`), the panel keeps it.
  if (code === 'proof_required' || code === 'hr_proof_required') return 'Bu topshiriq uchun isbot talab qilinadi — fayl biriktiring yoki nima qilganingizni yozing.'
  if (code === 'validation_error' || hrErrorStatus(err) === 422) {
    return 'Izoh qabul qilinmadi — u bo‘sh yoki juda uzun. Nima qilganingizni qisqacha yozib, qayta yuboring.'
  }
  return hrErrorMessage(err)
}

/** One file in the "Bajardim" window; `proofId` is kept once uploaded so a retry never re-uploads it. */
interface PickedProof {
  key: string
  file: File
  /** Set for a recorded voice proof — shown as "Ovozli izoh · 0:42". */
  voiceSeconds: number | null
  proofId: string | null
  percent: number
}

let pickCounter = 0

/**
 * "Bajardim" — a proof FILE or a non-empty COMMENT (hr-tasks.md §5). Our JWT works on the
 * web too, so files can be uploaded from the panel (`/api/hr/me/proofs`).
 *
 * Mobile parity with the LMS Mini App `CompleteSheet` (the teacher portal and «📱 Panel» open
 * inside Telegram): "Suratga olish" (camera), "Fayl biriktirish" (several) and "Ovozli izoh"
 * (recorded voice = one more file). Proofs already on the task count toward the limit. An uploaded
 * file keeps its `proofId` — if "Bajardim" itself fails, "Yuborish" again does not re-upload it.
 */
export function CompleteTaskModal({
  task,
  onClose,
  onDone,
}: {
  task: HrMyTask
  onClose: () => void
  onDone: (message: string) => void
}) {
  const [comment, setComment] = useState('')
  const [picked, setPicked] = useState<PickedProof[]>([])
  const [submitted, setSubmitted] = useState(false)
  const [phase, setPhase] = useState<'idle' | 'uploading' | 'saving'>('idle')
  const [recording, setRecording] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const cameraRef = useRef<HTMLInputElement | null>(null)
  const fileRef = useRef<HTMLInputElement | null>(null)

  const busy = phase !== 'idle'
  const attached = task.proofs.length
  const room = Math.max(0, HR_MAX_PROOFS - attached - picked.length)
  const uploadedCount = picked.filter((p) => p.proofId !== null).length

  const commentTooLong = comment.trim().length > 2000
  const missingProof = comment.trim().length === 0 && picked.length === 0 && attached === 0
  const formError = submitted
    ? commentTooLong
      ? 'Izoh 2000 belgidan oshmasin'
      : missingProof
        ? 'Isbot kerak: fayl biriktiring yoki nima qilganingizni yozing'
        : null
    : null

  /** Checked OUTSIDE the state updater — React may run an updater twice (StrictMode). */
  function addFiles(list: FileList | File[] | null, voiceSeconds: number | null = null): void {
    if (!list || list.length === 0) return
    const accepted: PickedProof[] = []
    let problem: string | null = null
    let free = room
    for (const file of Array.from(list)) {
      if (free <= 0) {
        problem = `Ko‘pi bilan ${HR_MAX_PROOFS} ta isbot biriktiriladi`
        break
      }
      const invalid = hrProofFileError(file)
      if (invalid) {
        problem = `${file.name}: ${invalid}`
        continue
      }
      pickCounter += 1
      accepted.push({ key: `p${pickCounter}`, file, voiceSeconds, proofId: null, percent: 0 })
      free -= 1
    }
    setError(problem)
    if (accepted.length > 0) setPicked((current) => [...current, ...accepted])
  }

  function patchPicked(key: string, patch: Partial<PickedProof>): void {
    setPicked((current) => current.map((p) => (p.key === key ? { ...p, ...patch } : p)))
  }

  async function submit(): Promise<void> {
    setSubmitted(true)
    if (recording) {
      setError('Avval ovoz yozishni to‘xtating')
      return
    }
    if (commentTooLong || missingProof) return
    setError(null)
    const proofIds: string[] = []
    try {
      setPhase('uploading')
      for (const item of picked) {
        if (item.proofId !== null) {
          proofIds.push(item.proofId)
          continue
        }
        const uploaded = await uploadMyProof(task.id, {
          file: item.file,
          onProgress: (percent) => patchPicked(item.key, { percent }),
        })
        patchPicked(item.key, { proofId: uploaded.id, percent: 100 })
        proofIds.push(uploaded.id)
      }
      setPhase('saving')
      await completeMyTask(task.id, { comment: comment.trim() || null, proofIds })
      onDone('Topshiriq bajarildi deb belgilandi')
    } catch (err) {
      // The window STAYS OPEN — the comment and the (already uploaded) files are kept.
      setError(completeError(err))
    } finally {
      setPhase('idle')
    }
  }

  const buttonLabel =
    phase === 'uploading'
      ? `Yuklanmoqda… ${uploadedCount}/${picked.length}`
      : phase === 'saving'
        ? 'Yuborilmoqda…'
        : 'Bajardim'

  return (
    <Modal
      open
      onClose={busy ? () => undefined : onClose}
      title="Topshiriqni bajardim"
      footer={
        <div className={footerCls}>
          <Button variant="secondary" onClick={onClose} disabled={busy}>
            Bekor qilish
          </Button>
          <Button onClick={() => void submit()} disabled={busy || recording}>
            {buttonLabel}
          </Button>
        </div>
      }
    >
      <div className="space-y-3">
        <p className="break-words rounded-xl bg-slate-50 px-3 py-2 text-sm font-medium text-slate-800">{task.title}</p>
        <p className="text-sm text-slate-400">
          Muddat: <span className="tabular-nums">{hrFormatDateTime(task.dueAt)}</span>
        </p>
        <Field label="Nima qildingiz?" hint="Isbot har doim talab qilinadi: fayl biriktiring yoki nima qilganingizni yozing.">
          <textarea
            className={`${inputCls} resize-none`}
            rows={4}
            maxLength={2000}
            placeholder="Qisqacha yozing — fayl biriktirmasangiz shu izohning o‘zi isbot bo‘ladi"
            value={comment}
            disabled={busy}
            onChange={(e) => setComment(e.target.value)}
          />
        </Field>

        <Field label="Isbot" hint={`Rasm, hujjat, ovoz yoki video — ${HR_MAX_PROOFS} tagacha, har biri 10 MB gacha`}>
          <div className="space-y-2">
            {attached > 0 && (
              <p className="text-xs text-slate-500">Bu topshiriqda allaqachon {attached} ta isbot bor.</p>
            )}
            {picked.length > 0 && (
              <ul className="space-y-1.5">
                {picked.map((item) => {
                  const done = item.proofId !== null
                  const Icon = done ? Check : item.voiceSeconds !== null ? Mic : FileText
                  return (
                    <li key={item.key} className="rounded-lg border border-slate-200 px-3 py-2 text-sm">
                      <div className="flex items-center gap-2">
                        <Icon className={cn('h-4 w-4 shrink-0', done ? 'text-emerald-600' : 'text-slate-400')} />
                        <span className="min-w-0 flex-1 truncate text-slate-700">
                          {item.voiceSeconds !== null
                            ? `Ovozli izoh · ${hrVoiceDuration(item.voiceSeconds) ?? '—'}`
                            : item.file.name}
                        </span>
                        <span className="shrink-0 text-xs text-slate-400">{hrFormatBytes(item.file.size)}</span>
                        <button
                          type="button"
                          aria-label={`${item.file.name} — olib tashlash`}
                          disabled={busy}
                          onClick={() => setPicked((list) => list.filter((p) => p.key !== item.key))}
                          className="rounded p-1 text-slate-400 hover:bg-slate-100 hover:text-slate-700 disabled:opacity-40"
                        >
                          <X className="h-4 w-4" />
                        </button>
                      </div>
                      {(phase === 'uploading' || done) && (
                        <div className="mt-2 h-1.5 overflow-hidden rounded-full bg-slate-100">
                          <div
                            className={cn('h-full rounded-full transition-[width]', done ? 'bg-emerald-500' : 'bg-brand-500')}
                            style={{ width: `${done ? 100 : item.percent}%` }}
                          />
                        </div>
                      )}
                    </li>
                  )
                })}
              </ul>
            )}
            <div className="flex flex-wrap gap-2">
              {!recording && (
                <>
                  <Button
                    type="button"
                    variant="secondary"
                    className="px-3"
                    disabled={busy || room === 0}
                    onClick={() => cameraRef.current?.click()}
                  >
                    <Camera className="h-4 w-4" />
                    Suratga olish
                  </Button>
                  <Button
                    type="button"
                    variant="secondary"
                    className="px-3"
                    disabled={busy || room === 0}
                    onClick={() => fileRef.current?.click()}
                  >
                    <Paperclip className="h-4 w-4" />
                    Fayl biriktirish
                  </Button>
                </>
              )}
              <ProofVoiceButton
                disabled={busy || room === 0}
                onRecorded={(file, seconds) => addFiles([file], seconds)}
                onError={setError}
                onRecordingChange={setRecording}
              />
            </div>
            {/* `capture` and `multiple` on ONE input break some clients (the gallery never opens) — two inputs. */}
            <input
              ref={cameraRef}
              type="file"
              accept="image/*"
              capture="environment"
              className="hidden"
              onChange={(e) => {
                addFiles(e.target.files)
                e.target.value = ''
              }}
            />
            <input
              ref={fileRef}
              type="file"
              multiple
              className="hidden"
              accept={HR_PROOF_ACCEPT || undefined}
              onChange={(e) => {
                addFiles(e.target.files)
                e.target.value = ''
              }}
            />
          </div>
        </Field>

        {(formError || error) && (
          <p className="text-sm text-red-600" role="alert">
            {formError ?? error}
          </p>
        )}
      </div>
    </Modal>
  )
}

/** KPI step (`numeric`) — the task completes itself at the target. */
export function ProgressModal({
  task,
  onClose,
  onDone,
}: {
  task: HrMyTask
  onClose: () => void
  onDone: (message: string) => void
}) {
  const [amount, setAmount] = useState('')
  const [note, setNote] = useState('')
  const [submitted, setSubmitted] = useState(false)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const normalized = amount.trim().replace(',', '.')
  const amountError = !/^-?\d+(\.\d{1,2})?$/.test(normalized) || Number(normalized) === 0 ? 'Noldan farqli son kiriting (masalan 12 yoki 2.5)' : null

  async function submit(): Promise<void> {
    setSubmitted(true)
    if (amountError) return
    setBusy(true)
    setError(null)
    try {
      await addMyProgress(task.id, { amount: normalized, note: note.trim() || null })
      onDone('Natija qo‘shildi')
    } catch (err) {
      setError(hrErrorMessage(err))
    } finally {
      setBusy(false)
    }
  }

  return (
    <Modal
      open
      onClose={onClose}
      title="Natija qo‘shish"
      size="sm"
      footer={
        <div className={footerCls}>
          <Button variant="secondary" onClick={onClose} disabled={busy}>
            Bekor qilish
          </Button>
          <Button onClick={() => void submit()} disabled={busy}>
            {busy ? 'Saqlanmoqda…' : 'Qo‘shish'}
          </Button>
        </div>
      }
    >
      <div className="space-y-3">
        <p className="break-words text-sm font-medium text-slate-800">{task.title}</p>
        <p className="text-sm text-slate-400">Hozirgi holat: {hrProgressText(task) ?? '—'}</p>
        <Field label={`Miqdor${task.unit ? ` (${task.unit})` : ''}`} required hint="Xatoni tuzatish uchun manfiy son ham kiritish mumkin" error={submitted ? amountError : null}>
          <input className={inputCls} inputMode="decimal" autoFocus placeholder="12" value={amount} onChange={(e) => setAmount(e.target.value)} />
        </Field>
        <Field label="Izoh">
          <input className={inputCls} placeholder="Ixtiyoriy" value={note} onChange={(e) => setNote(e.target.value)} />
        </Field>
        {error && (
          <p className="text-sm text-red-600" role="alert">
            {error}
          </p>
        )}
      </div>
    </Modal>
  )
}

/** "+ O'zimga topshiriq" (`source = self`). */
export function SelfTaskModal({
  today,
  onClose,
  onDone,
}: {
  today: string
  onClose: () => void
  /** `dueAt` — the created task's due instant (the day view opens that day). */
  onDone: (message: string, dueAt: string | null) => void
}) {
  const [title, setTitle] = useState('')
  const [description, setDescription] = useState('')
  const [date, setDate] = useState(today)
  const [time, setTime] = useState('18:00')
  const [submitted, setSubmitted] = useState(false)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const titleError = title.trim().length < 3 ? 'Sarlavha kamida 3 ta belgi' : title.trim().length > 200 ? 'Sarlavha 200 belgidan oshmasin' : null
  const dueIso = hrToIso(date, time)
  const dueError = !dueIso ? 'Muddat noto‘g‘ri: sana va vaqtni tanlang' : null

  async function submit(): Promise<void> {
    setSubmitted(true)
    if (titleError || !dueIso) return
    setBusy(true)
    setError(null)
    try {
      const created = await createMyTask({ title: title.trim(), description: description.trim() || null, dueAt: dueIso })
      onDone('Topshiriq qo‘shildi', created.dueAt ?? dueIso)
    } catch (err) {
      setError(hrErrorMessage(err))
    } finally {
      setBusy(false)
    }
  }

  return (
    <Modal
      open
      onClose={onClose}
      title="O‘zimga topshiriq"
      footer={
        <div className={footerCls}>
          <Button variant="secondary" onClick={onClose} disabled={busy}>
            Bekor qilish
          </Button>
          <Button onClick={() => void submit()} disabled={busy}>
            {busy ? 'Saqlanmoqda…' : 'Qo‘shish'}
          </Button>
        </div>
      }
    >
      <div className="space-y-3">
        <Field label="Sarlavha" required error={submitted ? titleError : null}>
          <input className={inputCls} autoFocus placeholder="Masalan: Hisobotni tekshirish" value={title} onChange={(e) => setTitle(e.target.value)} />
        </Field>
        <Field label="Tavsif">
          <textarea className={`${inputCls} resize-none`} rows={2} value={description} onChange={(e) => setDescription(e.target.value)} />
        </Field>
        <DateTimeField
          label="Muddat"
          required
          hint="Toshkent vaqti"
          date={date}
          time={time}
          onDate={setDate}
          onTime={setTime}
          error={submitted ? dueError : null}
        />
        {error && (
          <p className="text-sm text-red-600" role="alert">
            {error}
          </p>
        )}
      </div>
    </Modal>
  )
}
