/**
 * Microphone helpers shared by the two voice recorders: the manager's voice note on a new task
 * (`VoiceRecorder`) and the employee's voice proof in "Bajardim" (`ProofVoiceButton`).
 *
 * The microphone needs a secure context (HTTPS/localhost); otherwise the file path remains.
 * MIME order: `audio/mp4` (Safari) → `audio/webm;codecs=opus` → browser default.
 */
const VOICE_MIME_CANDIDATES = ['audio/mp4', 'audio/webm;codecs=opus', 'audio/webm'] as const

export const NO_MIC_HINT = 'Mikrofon faqat HTTPS yoki localhost sahifada ishlaydi. Ovozni fayl qilib yuklang.'

export function pickVoiceMime(): string | undefined {
  if (typeof MediaRecorder === 'undefined') return undefined
  return VOICE_MIME_CANDIDATES.find((type) => MediaRecorder.isTypeSupported(type))
}

export function isMicAvailable(): boolean {
  return typeof MediaRecorder !== 'undefined' && typeof navigator !== 'undefined' && Boolean(navigator.mediaDevices?.getUserMedia)
}

export function micErrorMessage(error: unknown): string {
  const name = typeof error === 'object' && error !== null ? (error as { name?: string }).name : ''
  if (name === 'NotAllowedError' || name === 'SecurityError') {
    return 'Mikrofonga ruxsat berilmadi. Manzil qatoridagi qulf belgisini bosib ruxsat bering.'
  }
  if (name === 'NotFoundError' || name === 'DevicesNotFoundError') return 'Mikrofon topilmadi — qurilma ulanganini tekshiring.'
  if (name === 'NotReadableError') return 'Mikrofonni boshqa dastur band qilgan. Uni yopib qayta urining.'
  return 'Mikrofonni ochib bo‘lmadi. Ovozni fayl qilib yuklang.'
}

const EXTENSION: Record<string, string> = {
  'audio/webm': 'webm',
  'audio/ogg': 'ogg',
  'audio/mp4': 'm4a',
  'audio/aac': 'm4a',
  'audio/mpeg': 'mp3',
}

/** A recorded `Blob` → a named `File` the proof upload accepts (`ovoz-1-05.webm`). */
export function voiceFile(blob: Blob, seconds: number): File {
  const type = blob.type.split(';')[0]?.trim().toLowerCase() ?? ''
  const total = Math.max(0, Math.round(seconds))
  const name = `ovoz-${Math.floor(total / 60)}-${String(total % 60).padStart(2, '0')}.${EXTENSION[type] ?? 'webm'}`
  return new File([blob], name, { type: blob.type || 'audio/webm' })
}
