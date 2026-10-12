import { useEffect, useRef, useState } from 'react'
import { FileText, Film, Image as ImageIcon, Paperclip, X } from 'lucide-react'
import {
  fetchAttachmentObjectUrl,
  uploadMessageAttachment,
  type MessageAttachment,
} from '@/api/services/messaging'
import { formatBytes } from '@/lib/telegramDelivery'
import { apiErrorMessage, cn } from '@/lib/utils'
import { useBlobUrl } from './useBlobUrl'

/** Tanlangan fayl + (rasm bo'lsa) eskiz URL — preview'ga ham shu uzatiladi. */
export type PickedAttachment = MessageAttachment & { thumbUrl?: string | null }

const ALL_ACCEPT = '.jpg,.jpeg,.png,.mp4,.pdf,.docx,.xlsx,.pptx,.doc,.xls,.ppt'
const PHOTO_ACCEPT = '.jpg,.jpeg,.png'

/**
 * BITTA biriktirma (CRM upgrade P2.8): «Rasm / video / fayl biriktirish» — yuklash jarayoni (%),
 * rasm eskizi yoki nom + hajm, «Olib tashlash». Server tur, hajm va fayl mazmunini (magic bytes)
 * tekshiradi; rad etilsa SABAB shu yerning o'zida ko'rinadi (`error-visibility.md`).
 */
export function AttachmentPicker({
  value,
  onChange,
  photoOnly = false,
  label,
  disabled,
  upload = uploadMessageAttachment,
  fetchThumb = fetchAttachmentObjectUrl,
}: {
  value: PickedAttachment | null
  onChange: (next: PickedAttachment | null) => void
  /** Faqat rasm (yutuq rasmi). */
  photoOnly?: boolean
  label?: string
  disabled?: boolean
  /** Yuklash endpointi — standart `messaging/attachments` (`messages.broadcast`); yutuq rasmi o'z
   *  endpointidan (`achievements/photo`, `students.list`). */
  upload?: (file: File, onProgress?: (pct: number) => void) => Promise<MessageAttachment>
  /** Saqlangan rasm eskizini oluvchi (blob URL). */
  fetchThumb?: (id: string) => Promise<string>
}) {
  const inputRef = useRef<HTMLInputElement>(null)
  const [progress, setProgress] = useState<number | null>(null)
  const [error, setError] = useState('')
  // Lokal eskizlar (yangi yuklangan rasm) — komponent yopilganda bo'shatiladi.
  const localUrls = useRef<string[]>([])
  useEffect(() => {
    const urls = localUrls.current // o'sha massiv — ichiga keyin qo'shilganlar ham bo'shatiladi
    return () => urls.forEach((u) => URL.revokeObjectURL(u))
  }, [])

  // Oldin saqlangan (masalan avto qoida) rasm — eskizi serverdan blob qilib olinadi.
  const remoteThumb = useBlobUrl(
    value && value.kind === 'photo' && !value.thumbUrl ? value.id : null,
    fetchThumb,
  )
  const thumb = value?.thumbUrl ?? remoteThumb

  const pick = async (file: File | undefined) => {
    if (!file) return
    setError('')
    setProgress(0)
    try {
      const att = await upload(file, setProgress)
      let thumbUrl: string | null = null
      if (att.kind === 'photo') {
        thumbUrl = URL.createObjectURL(file)
        localUrls.current.push(thumbUrl)
      }
      onChange({ ...att, thumbUrl })
    } catch (e) {
      setError(apiErrorMessage(e, "Faylni yuklab bo'lmadi"))
    } finally {
      setProgress(null)
      if (inputRef.current) inputRef.current.value = ''
    }
  }

  const Icon = value?.kind === 'video' ? Film : value?.kind === 'photo' ? ImageIcon : FileText

  return (
    <div>
      {label && <p className="mb-1.5 text-sm font-medium text-slate-600">{label}</p>}
      <input
        ref={inputRef}
        type="file"
        accept={photoOnly ? PHOTO_ACCEPT : ALL_ACCEPT}
        className="hidden"
        onChange={(e) => pick(e.target.files?.[0])}
      />
      {value ? (
        <div className="flex items-center gap-3 rounded-xl border border-slate-200 bg-slate-50 p-2">
          {value.kind === 'photo' && thumb ? (
            <img src={thumb} alt={value.fileName} className="h-12 w-12 shrink-0 rounded-lg object-cover" />
          ) : (
            <span className="flex h-12 w-12 shrink-0 items-center justify-center rounded-lg bg-white text-slate-500">
              <Icon className="h-5 w-5" />
            </span>
          )}
          <span className="min-w-0 flex-1">
            <span className="block truncate text-sm font-medium text-slate-700">{value.fileName}</span>
            {value.sizeBytes > 0 && <span className="block text-xs text-slate-400">{formatBytes(value.sizeBytes)}</span>}
          </span>
          <button
            type="button"
            disabled={disabled}
            onClick={() => onChange(null)}
            className="inline-flex shrink-0 items-center gap-1 rounded-lg border border-slate-200 bg-white px-2.5 py-1.5 text-xs font-medium text-slate-600 transition-colors hover:border-red-200 hover:bg-red-50 hover:text-red-600 disabled:opacity-50"
          >
            <X className="h-3.5 w-3.5" /> Olib tashlash
          </button>
        </div>
      ) : (
        <button
          type="button"
          disabled={disabled || progress !== null}
          onClick={() => inputRef.current?.click()}
          className={cn(
            'inline-flex items-center gap-1.5 rounded-lg border border-dashed border-slate-300 px-3 py-2 text-sm font-medium text-slate-600 transition-colors hover:border-brand-400 hover:bg-brand-50 hover:text-brand-700 disabled:opacity-60',
          )}
        >
          <Paperclip className="h-4 w-4" />
          {progress !== null
            ? `Yuklanmoqda... ${progress}%`
            : photoOnly
              ? 'Rasm biriktirish'
              : 'Rasm / video / fayl biriktirish'}
        </button>
      )}
      {progress !== null && (
        <div className="mt-1.5 h-1.5 w-full overflow-hidden rounded-full bg-slate-100">
          <div className="h-full rounded-full bg-brand-500 transition-all" style={{ width: `${progress}%` }} />
        </div>
      )}
      {error && <p className="mt-1 text-sm font-medium text-red-600">{error}</p>}
      {!value && !error && (
        <p className="mt-1 text-xs text-slate-400">
          {photoOnly ? 'jpg yoki png, 10 MB gacha' : 'Rasm (jpg, png — 10 MB), video (mp4) yoki hujjat (pdf, docx, xlsx, pptx — 50 MB gacha)'}
        </p>
      )}
    </div>
  )
}
