import { useEffect, useState } from 'react'
import { Plus, Trash2 } from 'lucide-react'
import type { Subject } from '@/types'
import type { SubjectPayload } from '@/api/services/subjects'
import { Modal } from '@/components/ui/Modal'
import { Button } from '@/components/ui/Button'
import { Input, Select } from '@/components/ui/Input'
import { MoneyInput } from '@/components/ui/NumberInput'
import { SCHOOL_GRADE_OPTIONS } from '@/lib/schoolGrades'
import {
  MAX_PRICE_BANDS,
  nextBandStart,
  toPayloadBands,
  validateBands,
  type BandDraft,
} from '@/lib/priceBands'
import { uid } from '@/lib/utils'

interface Props {
  open: boolean
  onClose: () => void
  onSubmit: (values: SubjectPayload) => void
  initial?: Subject | null
}

type BandRow = BandDraft & { key: string }

export function SubjectFormModal({ open, onClose, onSubmit, initial }: Props) {
  const [name, setName] = useState('')
  const [price, setPrice] = useState<number | null>(null)
  const [lessonPrice, setLessonPrice] = useState<number | null>(null)
  /** Sinf oraliqlari — o'quvchi oyligi uning joriy sinfiga qarab (server `TuitionPricing`). */
  const [bands, setBands] = useState<BandRow[]>([])
  const [error, setError] = useState('')

  useEffect(() => {
    // eslint-disable-next-line react-hooks/set-state-in-effect -- modal ochilganda formani initial bilan sinxronlash (maqsadli)
    if (open) {
      setName(initial?.name ?? '')
      setPrice(initial?.price ?? null)
      // 0 = "belgilanmagan" — maydon bo'sh ko'rinadi.
      setLessonPrice(initial?.lessonPrice || null)
      setBands(
        (initial?.priceBands ?? []).map((b) => ({
          key: uid(),
          gradeFrom: b.gradeFrom,
          gradeTo: b.gradeTo,
          price: b.price,
          lessonPrice: b.lessonPrice || null,
        })),
      )
      setError('')
    }
  }, [open, initial])

  const patchBand = (key: string, patch: Partial<BandDraft>) =>
    setBands((prev) => prev.map((b) => (b.key === key ? { ...b, ...patch } : b)))

  const addBand = () =>
    setBands((prev) => {
      const from = nextBandStart(prev)
      return [...prev, { key: uid(), gradeFrom: from, gradeTo: from, price: null, lessonPrice: null }]
    })

  const handleSubmit = (e: React.FormEvent) => {
    e.preventDefault()
    if (!name.trim()) return
    if (price === null) {
      setError("Oylik narxni kiriting (bepul bo'lsa 0 yozing)")
      return
    }
    // Bepul kursga oraliq qo'shilmaydi — server ham rad etadi (`TuitionPricing.Resolve` izohi).
    if (bands.length > 0 && price <= 0) {
      setError("Sinf narxlari uchun kursning asosiy oylik narxini kiriting (bepul kursga oraliq qo'shilmaydi)")
      return
    }
    const bandError = validateBands(bands)
    if (bandError) {
      setError(bandError)
      return
    }
    setError('')
    onSubmit({ name: name.trim(), price, lessonPrice: lessonPrice ?? 0, priceBands: toPayloadBands(bands) })
  }

  const hasBands = bands.length > 0

  return (
    <Modal
      open={open}
      onClose={onClose}
      title={initial ? 'Kursni tahrirlash' : 'Yangi kurs'}
      size="md"
      footer={
        <>
          <Button variant="secondary" onClick={onClose}>
            Bekor qilish
          </Button>
          <Button type="submit" form="subject-form">
            Saqlash
          </Button>
        </>
      }
    >
      <form id="subject-form" onSubmit={handleSubmit} className="space-y-4">
        <Input
          label="Kurs nomi"
          required
          placeholder="Masalan: Matematika"
          value={name}
          onChange={(e) => setName(e.target.value)}
        />
        <div>
          <MoneyInput
            label={hasBands ? "Asosiy oylik narx (so'm)" : "Oylik narx (so'm)"}
            className="font-mono"
            value={price}
            onChange={setPrice}
          />
          {hasBands && (
            <p className="mt-1 text-xs text-slate-400">
              Sinfi kiritilmagan yoki pastdagi oraliqlarga tushmagan o'quvchi shu narxni to'laydi.
            </p>
          )}
        </div>
        <div>
          <MoneyInput
            label="Bir dars narxi (so'm, yaxlit)"
            className="font-mono"
            value={lessonPrice}
            onChange={setLessonPrice}
          />
          <p className="mt-1 text-xs text-slate-400">
            Oy o'rtasida aktivlashtirilganda yoki muzlatilganda har bir dars uchun shu summa olinadi.
            Bo'sh (0) qoldirilsa, oylik ÷ jami dars hisobi ishlaydi.
          </p>
        </div>

        {/* SINFLAR VA NARXLAR — o'quvchining oyligi uning JORIY sinfiga qarab tanlanadi
            (har yili 1-sentabrda sinf o'zi oshadi). Qoida serverda: `TuitionPricing`. */}
        <div className="rounded-xl border border-slate-200 p-3">
          <div className="flex items-center justify-between gap-2">
            <div>
              <p className="text-[13px] font-semibold text-slate-800">Sinflar va narxlar</p>
              <p className="text-xs text-slate-400">
                Ixtiyoriy. Masalan: 3–7-sinf — 225 000, 8–9-sinf — 275 000. O'quvchining oyligi
                uning joriy sinfiga qarab o'zi tanlanadi. O'zgarish joriy oy hisobiga darhol qo'llanadi;
                oraliq kiritilmasa hamma asosiy narxni to'laydi.
              </p>
            </div>
            {bands.length < MAX_PRICE_BANDS && (
              <Button type="button" variant="secondary" onClick={addBand} className="shrink-0">
                <Plus className="h-4 w-4" /> Oraliq
              </Button>
            )}
          </div>

          {bands.length > 0 && (
            <div className="mt-3 space-y-2">
              {bands.map((b) => (
                <div key={b.key} className="grid grid-cols-2 gap-2 rounded-lg bg-slate-50 p-2 sm:grid-cols-[1fr_1fr_1.3fr_1.3fr_auto] sm:items-end">
                  <Select
                    label="Sinfdan"
                    value={String(b.gradeFrom)}
                    onChange={(e) => {
                      const v = Number(e.target.value)
                      patchBand(b.key, { gradeFrom: v, gradeTo: Math.max(v, b.gradeTo) })
                    }}
                  >
                    {SCHOOL_GRADE_OPTIONS.map((o) => (
                      <option key={o.value} value={o.value}>
                        {o.label}
                      </option>
                    ))}
                  </Select>
                  <Select
                    label="Sinfgacha"
                    value={String(b.gradeTo)}
                    onChange={(e) => patchBand(b.key, { gradeTo: Number(e.target.value) })}
                  >
                    {SCHOOL_GRADE_OPTIONS.filter((o) => o.value >= b.gradeFrom).map((o) => (
                      <option key={o.value} value={o.value}>
                        {o.label}
                      </option>
                    ))}
                  </Select>
                  <MoneyInput
                    label="Oylik narx"
                    className="font-mono"
                    value={b.price}
                    onChange={(v) => patchBand(b.key, { price: v })}
                  />
                  <MoneyInput
                    label="Bir dars"
                    className="font-mono"
                    value={b.lessonPrice}
                    onChange={(v) => patchBand(b.key, { lessonPrice: v })}
                  />
                  <button
                    type="button"
                    title="Oraliqni o'chirish"
                    aria-label="Oraliqni o'chirish"
                    onClick={() => setBands((prev) => prev.filter((x) => x.key !== b.key))}
                    className="col-span-2 inline-flex h-[34px] items-center justify-center rounded-lg text-slate-400 transition-colors hover:bg-red-50 hover:text-red-600 sm:col-span-1 sm:w-[34px]"
                  >
                    <Trash2 className="h-4 w-4" />
                  </button>
                </div>
              ))}
            </div>
          )}
        </div>

        {error && <p className="text-sm font-medium text-red-600">{error}</p>}
      </form>
    </Modal>
  )
}
