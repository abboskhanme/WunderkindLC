import { useEffect, useState } from 'react'
import type { Subject } from '@/types'
import type { SubjectPayload } from '@/api/services/subjects'
import { Modal } from '@/components/ui/Modal'
import { Button } from '@/components/ui/Button'
import { Input } from '@/components/ui/Input'

interface Props {
  open: boolean
  onClose: () => void
  onSubmit: (values: SubjectPayload) => void
  initial?: Subject | null
}

export function SubjectFormModal({ open, onClose, onSubmit, initial }: Props) {
  const [name, setName] = useState('')
  const [price, setPrice] = useState(0)
  const [lessonPrice, setLessonPrice] = useState(0)

  useEffect(() => {
    // eslint-disable-next-line react-hooks/set-state-in-effect -- modal ochilganda formani initial bilan sinxronlash (maqsadli)
    if (open) {
      setName(initial?.name ?? '')
      setPrice(initial?.price ?? 0)
      setLessonPrice(initial?.lessonPrice ?? 0)
    }
  }, [open, initial])

  const handleSubmit = (e: React.FormEvent) => {
    e.preventDefault()
    if (!name.trim()) return
    onSubmit({ name: name.trim(), price, lessonPrice })
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
        <Input
          label="Oylik narx (so'm)"
          type="number"
          min={0}
          step="any"
          className="font-mono"
          value={price}
          onChange={(e) => setPrice(Number(e.target.value))}
        />
        <div>
          <Input
            label="Bir dars narxi (so'm, yaxlit)"
            type="number"
            min={0}
            step="any"
            className="font-mono"
            value={lessonPrice}
            onChange={(e) => setLessonPrice(Number(e.target.value))}
          />
          <p className="mt-1 text-xs text-slate-400">
            Oy o'rtasida aktivlashtirilganda 12 tadan kam dars qolsa — har bir dars uchun shu summa
            olinadi. Bo'sh (0) qoldirilsa, eski (oylik ÷ jami dars) hisob ishlaydi.
          </p>
        </div>
      </form>
    </Modal>
  )
}
