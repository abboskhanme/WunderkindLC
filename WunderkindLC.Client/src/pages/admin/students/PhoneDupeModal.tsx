import { AlertTriangle } from 'lucide-react'
import type { PhoneMatch } from '@/api/services/students'
import { Modal } from '@/components/ui/Modal'
import { Button } from '@/components/ui/Button'

/**
 * Telefon dublikati ogohlantirishi — "Baribir saqlash" / "Bekor qilish".
 * `StudentFormModal` ham, profilning «Tahrirlash» tabi ham AYNAN shu oynani ko'rsatadi.
 */
export function PhoneDupeModal({
  dupes,
  onCancel,
  onConfirm,
}: {
  dupes: PhoneMatch[]
  onCancel: () => void
  onConfirm: () => void
}) {
  return (
    <Modal
      open={dupes.length > 0}
      onClose={onCancel}
      size="md"
      title="Bunday raqam allaqachon mavjud"
      footer={
        <>
          <Button variant="secondary" onClick={onCancel}>
            Bekor qilish
          </Button>
          <Button variant="danger" onClick={onConfirm}>
            Baribir saqlash
          </Button>
        </>
      }
    >
      <div className="space-y-3">
        <div className="flex items-start gap-2.5 rounded-lg bg-amber-50 px-3 py-2.5 text-sm text-amber-700">
          <AlertTriangle className="mt-0.5 h-4 w-4 flex-shrink-0" />
          <p>
            Kiritilgan raqam(lar) allaqachon quyidagi o'quvchi(lar)da ishlatilgan. Baribir saqlashni
            xohlaysizmi?
          </p>
        </div>
        <ul className="divide-y divide-slate-100 rounded-lg border border-slate-200">
          {dupes.map((d, i) => (
            <li key={`${d.studentId}-${i}`} className="flex items-center gap-3 px-3 py-2.5">
              <div className="min-w-0 flex-1">
                <p className="truncate text-sm font-medium text-slate-800">
                  {d.fullName}
                  {d.isArchived && (
                    <span className="ml-2 rounded bg-slate-100 px-1.5 py-0.5 text-[11px] font-semibold text-slate-500">
                      arxivda
                    </span>
                  )}
                </p>
                <p className="truncate text-xs text-slate-400">
                  {d.className || 'guruhsiz'} · {d.role} raqami
                </p>
              </div>
              <span className="font-mono text-sm text-slate-600">{d.phone}</span>
            </li>
          ))}
        </ul>
      </div>
    </Modal>
  )
}
