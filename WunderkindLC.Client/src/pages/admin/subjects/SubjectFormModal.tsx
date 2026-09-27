import { useEffect, useState } from 'react'
import type { Subject } from '@/types'
import type { SubjectPayload } from '@/api/services/subjects'
import { Modal } from '@/components/ui/Modal'
import { Button } from '@/components/ui/Button'
import { Input } from '@/components/ui/Input'
import { MoneyInput } from '@/components/ui/NumberInput'

interface Props {
  open: boolean
  onClose: () => void
  onSubmit: (values: SubjectPayload) => void
  initial?: Subject | null
}

export function SubjectFormModal({ open, onClose, onSubmit, initial }: Props) {
  const [name, setName] = useState('')
  const [price, setPrice] = useState<number | null>(null)
  const [lessonPrice, setLessonPrice] = useState<number | null>(null)
  const [error, setError] = useState('')

  useEffect(() => {
    // eslint-disable-next-line react-hooks/set-state-in-effect -- modal ochilganda formani initial bilan sinxronlash (maqsadli)
    if (open) {
      setName(initial?.name ?? '')
      setPrice(initial?.price ?? null)
      // 0 = "belgilanmagan" — maydon bo'sh ko'rinadi.
      setLessonPrice(initial?.lessonPrice || null)
      setError('')
    }
  }, [open, initial])

  const handleSubmit = (e: React.FormEvent) => {
    e.preventDefault()
    if (!name.trim()) return
    if (price === null) {
      setError("Oylik narxni kiriting (bepul bo'lsa 0 yozing)")
      return
    }
    setError('')
    onSubmit({ name: name.trim(), price, lessonPrice: lessonPrice ?? 0 })
  }

  return (
    <Modal
      open={open}
      onClose={onClose}
      title={initial ? 'Kursni tahrirlash' : 'Yangi kurs'}
      size="sm"
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
        <MoneyInput
          label="Oylik narx (so'm)"
          className="font-mono"
          value={price}
          onChange={setPrice}
        />
        <div>
          <MoneyInput
            label="Bir dars narxi (so'm, yaxlit)"
            className="font-mono"
            value={lessonPrice}
            onChange={setLessonPrice}
          />
          <p className="mt-1 text-xs text-slate-400">
            Oy o'rtasida aktivlashtirilganda 12 tadan kam dars qolsa — har bir dars uchun shu summa
            olinadi. Bo'sh (0) qoldirilsa, eski (oylik ÷ jami dars) hisob ishlaydi.
          </p>
        </div>
        {error && <p className="text-sm font-medium text-red-600">{error}</p>}
      </form>
    </Modal>
  )
}
