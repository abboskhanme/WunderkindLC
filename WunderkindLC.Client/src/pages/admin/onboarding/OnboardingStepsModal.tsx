import { useEffect, useState } from 'react'
import { ArrowDown, ArrowUp, Loader2, Pencil, Plus, Save, Trash2, X } from 'lucide-react'
import type { OnboardingStep, OnboardingStepPayload } from '@/api/services/onboarding'
import {
  createOnboardingStep, deleteOnboardingStep, getOnboardingSteps, reorderOnboardingSteps, updateOnboardingStep,
} from '@/api/services/onboarding'
import { Modal } from '@/components/ui/Modal'
import { Button } from '@/components/ui/Button'
import { Input } from '@/components/ui/Input'
import { NumberInput } from '@/components/ui/NumberInput'
import { Loader } from '@/components/ui/Loader'
import { apiErrorMessage, cn } from '@/lib/utils'

interface Props {
  canCreate: boolean
  canEdit: boolean
  canDelete: boolean
  onClose: () => void
}

interface Draft {
  /** null — yangi qadam */
  id: string | null
  title: string
  description: string
  dayOffset: number | null
  isActive: boolean
}

const PERIOD_DAYS = 30

/**
 * QADAMLAR KATALOGI — moslashuv cheklistining andozasi (standart 11 qadam).
 * ⚠️ O'zgarishlar FAQAT YANGI moslashuvlarga ta'sir qiladi: boshlangan moslashuv qadamlari
 * boshlanish paytida NUSXA qilingan (serverda) — buni oyna tepasida ochiq yozamiz.
 * Har yozuv amali xatosi qizil satrda (`run` yordamchisi — BoardSettingsModal naqshi).
 * Oyna har ochilishda `key` bilan qayta yaratiladi.
 */
export function OnboardingStepsModal({ canCreate, canEdit, canDelete, onClose }: Props) {
  const [steps, setSteps] = useState<OnboardingStep[] | null>(null)
  const [draft, setDraft] = useState<Draft | null>(null)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState('')

  useEffect(() => {
    let alive = true
    getOnboardingSteps()
      .then((list) => {
        if (alive) setSteps(list)
      })
      .catch((err) => {
        if (alive) setError(apiErrorMessage(err, "Qadamlarni yuklab bo'lmadi"))
      })
    return () => {
      alive = false
    }
  }, [])

  /** Yozuv amali: band holat + xato matni (forma yopilmaydi, kiritilgan matn qoladi). */
  const run = async (fn: () => Promise<void>, fallback: string): Promise<boolean> => {
    if (busy) return false
    setBusy(true)
    setError('')
    try {
      await fn()
      return true
    } catch (err) {
      setError(apiErrorMessage(err, fallback))
      return false
    } finally {
      setBusy(false)
    }
  }

  const saveDraft = async () => {
    if (!draft) return
    const title = draft.title.trim()
    if (title.length < 2) {
      setError('Qadam nomini kiriting')
      return
    }
    const day = draft.dayOffset ?? 0
    if (day < 0 || day > PERIOD_DAYS) {
      setError(`Muddat 0 dan ${PERIOD_DAYS} gacha kun bo'lishi kerak`)
      return
    }
    const payload: OnboardingStepPayload = {
      title, description: draft.description.trim(), dayOffset: day, isActive: draft.isActive,
    }
    const ok = await run(async () => {
      if (draft.id) {
        const saved = await updateOnboardingStep(draft.id, payload)
        setSteps((prev) => (prev ?? []).map((s) => (s.id === saved.id ? saved : s)))
      } else {
        const saved = await createOnboardingStep(payload)
        setSteps((prev) => [...(prev ?? []), saved])
      }
    }, "Qadamni saqlab bo'lmadi")
    if (ok) setDraft(null)
  }

  const remove = (s: OnboardingStep) => {
    if (!window.confirm(`«${s.title}» katalogdan o'chirilsinmi? Boshlangan moslashuvlarga ta'sir qilmaydi.`)) return
    void run(async () => {
      await deleteOnboardingStep(s.id)
      setSteps((prev) => (prev ?? []).filter((x) => x.id !== s.id))
    }, "Qadamni o'chirib bo'lmadi")
  }

  const move = (index: number, dir: -1 | 1) => {
    if (!steps) return
    const j = index + dir
    if (j < 0 || j >= steps.length) return
    const next = [...steps]
    ;[next[index], next[j]] = [next[j], next[index]]
    // Optimistik EMAS: tartib server javobidan olinadi (xato bo'lsa ro'yxat o'zgarmaydi).
    void run(async () => {
      setSteps(await reorderOnboardingSteps(next.map((s) => s.id)))
    }, "Tartibni saqlab bo'lmadi")
  }

  return (
    <Modal open onClose={onClose} size="lg" title="Moslashuv qadamlari (katalog)">
      <div className="space-y-3">
        <p className="rounded-lg bg-amber-50 px-3 py-2 text-xs text-amber-700">
          Bu — yangi xodim cheklistining ANDOZASI. O'zgarishlar faqat bundan keyin boshlanadigan
          moslashuvlarga ta'sir qiladi; boshlanganlarining qadamlari o'zgarmaydi. Muddat —
          boshlanishdan necha kun ichida bajarilishi kerak (0–{PERIOD_DAYS}).
        </p>
        {error && <p className="text-sm font-medium text-red-600">{error}</p>}

        {steps == null ? (
          !error && <Loader label="Yuklanmoqda..." />
        ) : (
          <ol className="space-y-2">
            {steps.map((s, i) =>
              draft?.id === s.id ? (
                <li key={s.id}>
                  <StepEditor draft={draft} busy={busy} onChange={setDraft} onSave={saveDraft} onCancel={() => setDraft(null)} />
                </li>
              ) : (
                <li
                  key={s.id}
                  className={cn('flex items-start gap-3 rounded-xl border border-slate-200 p-3', !s.isActive && 'opacity-60')}
                >
                  <span className="mt-0.5 w-6 flex-none text-right text-xs font-semibold text-slate-400">{i + 1}.</span>
                  <div className="min-w-0 flex-1">
                    <p className="text-sm font-semibold text-slate-800">
                      {s.title}
                      {!s.isActive && <span className="ml-2 text-xs font-normal text-slate-400">(o'chirilgan)</span>}
                    </p>
                    {s.description && <p className="text-xs text-slate-500">{s.description}</p>}
                    <p className="text-xs text-slate-400">{s.dayOffset}-kungacha</p>
                  </div>
                  {canEdit && (
                    <div className="flex flex-none items-center gap-1">
                      <IconBtn title="Yuqoriga" disabled={busy || i === 0} onClick={() => move(i, -1)}>
                        <ArrowUp className="h-3.5 w-3.5" />
                      </IconBtn>
                      <IconBtn title="Pastga" disabled={busy || i === steps.length - 1} onClick={() => move(i, 1)}>
                        <ArrowDown className="h-3.5 w-3.5" />
                      </IconBtn>
                      <IconBtn
                        title="Tahrirlash"
                        disabled={busy}
                        onClick={() =>
                          setDraft({ id: s.id, title: s.title, description: s.description, dayOffset: s.dayOffset, isActive: s.isActive })
                        }
                      >
                        <Pencil className="h-3.5 w-3.5" />
                      </IconBtn>
                      {canDelete && (
                        <IconBtn title="O'chirish" disabled={busy} onClick={() => remove(s)}>
                          <Trash2 className="h-3.5 w-3.5 text-red-500" />
                        </IconBtn>
                      )}
                    </div>
                  )}
                </li>
              ),
            )}
          </ol>
        )}

        {draft && draft.id == null ? (
          <StepEditor draft={draft} busy={busy} onChange={setDraft} onSave={saveDraft} onCancel={() => setDraft(null)} />
        ) : (
          canCreate &&
          steps != null && (
            <Button
              variant="secondary"
              onClick={() => setDraft({ id: null, title: '', description: '', dayOffset: null, isActive: true })}
              disabled={busy}
            >
              <Plus className="h-4 w-4" /> Qadam qo'shish
            </Button>
          )
        )}
      </div>
    </Modal>
  )
}

function IconBtn({
  title, disabled, onClick, children,
}: { title: string; disabled?: boolean; onClick: () => void; children: React.ReactNode }) {
  return (
    <button
      type="button"
      title={title}
      aria-label={title}
      disabled={disabled}
      onClick={onClick}
      className="rounded-md p-1.5 text-slate-500 hover:bg-slate-100 disabled:cursor-not-allowed disabled:opacity-40"
    >
      {children}
    </button>
  )
}

interface EditorProps {
  draft: Draft
  busy: boolean
  onChange: (d: Draft) => void
  onSave: () => void
  onCancel: () => void
}

function StepEditor({ draft, busy, onChange, onSave, onCancel }: EditorProps) {
  return (
    <div className="space-y-2 rounded-xl border border-brand-200 bg-brand-50/30 p-3">
      <Input
        label="Qadam nomi"
        required
        value={draft.title}
        onChange={(e) => onChange({ ...draft, title: e.target.value })}
        placeholder="Masalan: Mehnat shartnomasini imzolash"
      />
      <Input
        label="Izoh (ixtiyoriy)"
        value={draft.description}
        onChange={(e) => onChange({ ...draft, description: e.target.value })}
      />
      <div className="grid items-end gap-2 sm:grid-cols-2">
        <NumberInput
          label={`Muddat (kun, 0–${PERIOD_DAYS})`}
          value={draft.dayOffset}
          onChange={(v) => onChange({ ...draft, dayOffset: v })}
          placeholder="0"
        />
        <label className="flex items-center gap-2 pb-2 text-sm text-slate-600">
          <input
            type="checkbox"
            checked={draft.isActive}
            onChange={(e) => onChange({ ...draft, isActive: e.target.checked })}
          />
          Yangi moslashuvlarga qo'shilsin
        </label>
      </div>
      <div className="flex gap-2">
        <Button onClick={onSave} disabled={busy}>
          {busy ? <Loader2 className="h-4 w-4 animate-spin" /> : <Save className="h-4 w-4" />} Saqlash
        </Button>
        <Button variant="ghost" onClick={onCancel} disabled={busy}>
          <X className="h-4 w-4" /> Bekor
        </Button>
      </div>
    </div>
  )
}
