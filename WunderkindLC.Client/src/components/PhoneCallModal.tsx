import { Phone, User } from 'lucide-react'
import { Modal } from '@/components/ui/Modal'
import { Button } from '@/components/ui/Button'
import { formatPhone } from '@/lib/numberFormat'

/* ============================================================
   PhoneCallModal — istalgan joydan (lid, o'quvchi, ...) qo'ng'iroq qilish oynasi.
   Bir nechta raqam bo'lsa (o'zi, otasi, onasi ...) qaysi biriga ekanini tanlatadi; har raqam —
   oddiy `tel:` havola (telefonda terish oynasi, kompyuterda tizim ilovasi ochiladi).
   ⚠️ Markazning o'z telefoniyasi (Local Call / CTI agent-telefon) 2026-10 da olib tashlandi —
   "agent orqali terish" bosqichi yo'q.
   ============================================================ */

export interface CallOption {
  /** Raqam kimniki — "O'z raqami", "Otasi", "Onasi", "Ota-ona" ... */
  label: string
  number: string
}

/** `tel:` havolasi uchun raqam: faqat raqamlar va `+`. */
const telHref = (number: string) => `tel:${number.replace(/[^\d+]/g, '')}`

export function PhoneCallModal({
  open, onClose, title, numbers,
}: {
  open: boolean
  onClose: () => void
  /** Sarlavhada ko'rinadigan ism (lid/o'quvchi) */
  title?: string
  /** Mavjud raqamlar (bo'shlari oldindan filtrlangan bo'lsin). */
  numbers: CallOption[]
}) {
  return (
    <Modal
      open={open}
      onClose={onClose}
      size="sm"
      title={title ? `Qo'ng'iroq qilish — ${title}` : "Qo'ng'iroq qilish"}
    >
      <div className="space-y-3">
        {numbers.length === 0 ? (
          <p className="py-4 text-center text-sm text-slate-400">Telefon raqami kiritilmagan.</p>
        ) : (
          <div className="space-y-2">
            <p className="text-xs font-semibold uppercase tracking-wide text-slate-400">
              Qaysi raqamga qo'ng'iroq qilamiz?
            </p>
            {numbers.map((n) => (
              <a
                key={n.label + n.number}
                href={telHref(n.number)}
                className="flex w-full items-center gap-3 rounded-xl border border-slate-200 bg-white p-3 text-left transition-colors hover:border-brand-300 hover:bg-brand-50"
              >
                <div className="flex h-9 w-9 flex-shrink-0 items-center justify-center rounded-full bg-slate-100 text-slate-500">
                  <User className="h-4 w-4" />
                </div>
                <div className="min-w-0 flex-1">
                  <div className="text-sm font-semibold text-slate-700">{n.label}</div>
                  <div className="font-mono text-xs text-slate-500">{formatPhone(n.number)}</div>
                </div>
                <Phone className="h-4 w-4 flex-shrink-0 text-brand-500" />
              </a>
            ))}
          </div>
        )}
        <div className="flex justify-end pt-1">
          <Button variant="secondary" onClick={onClose}>Yopish</Button>
        </div>
      </div>
    </Modal>
  )
}
