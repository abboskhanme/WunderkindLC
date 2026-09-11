import { Modal } from '@/components/ui/Modal'
import { PaymentHistoryPanel } from './PaymentHistoryPanel'

interface Props {
  studentId: string | null
  onClose: () => void
  /** To'lov shu modal ichidan kiritilgach — chaqiruvchi o'z holatini (balans/ro'yxat) yangilashi uchun. */
  onPaid?: () => void
}

export function PaymentHistoryModal({ studentId, onClose, onPaid }: Props) {
  return (
    <Modal open={!!studentId} onClose={onClose} size="lg" title="To'lov tarixi">
      {studentId && <PaymentHistoryPanel studentId={studentId} onPaid={onPaid} />}
    </Modal>
  )
}
