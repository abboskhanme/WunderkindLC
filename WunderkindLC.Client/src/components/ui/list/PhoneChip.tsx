import { formatPhoneShort } from '@/lib/utils'

const chip =
  'inline-flex items-center whitespace-nowrap rounded-full bg-[#ebebeb] px-2.5 py-0.5 text-[12px] font-medium text-black hover:bg-[#e0e0e0]'

/**
 * edutizim jadvallaridagi telefon: kulrang yumaloq "chip". Bosilsa qo'ng'iroq (`tel:`).
 * `onCall` berilsa — `tel:` o'rniga o'sha chaqiriladi (masalan markazning qo'ng'iroq oynasi:
 * MoiZvonki / Local Call, `CallPickerModal`).
 */
export function PhoneChip({ phone, onCall }: { phone?: string | null; onCall?: () => void }) {
  if (!phone) return <span className="text-[#9ca3af]">—</span>
  if (onCall)
    return (
      <button
        type="button"
        title="Qo'ng'iroq qilish"
        onClick={(e) => {
          e.stopPropagation()
          onCall()
        }}
        className={chip}
      >
        {formatPhoneShort(phone)}
      </button>
    )
  return (
    <a href={`tel:${phone.replace(/[^\d+]/g, '')}`} onClick={(e) => e.stopPropagation()} className={chip}>
      {formatPhoneShort(phone)}
    </a>
  )
}
