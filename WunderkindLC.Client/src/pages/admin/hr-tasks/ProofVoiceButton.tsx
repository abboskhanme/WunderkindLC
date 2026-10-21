import { useEffect, useRef, useState } from 'react'
import { Mic, Square } from 'lucide-react'
import { HR_MAX_VOICE_SECONDS } from '@/types/hrTasks'
import { Button } from '@/components/ui/Button'
import { hrVoiceDuration } from '@/components/hr-tasks/lib'
import { cn } from '@/lib/utils'
import { isMicAvailable, micErrorMessage, pickVoiceMime, voiceFile } from './voice'

/**
 * "Ovozli izoh" in the "Bajardim" window — the LMS Mini App `VoiceRecorder` (screens/hr): the
 * employee records a voice proof, it becomes one more FILE in the proof list (uploaded with the
 * rest on submit). The teacher portal opens inside Telegram, so this is the phone path; without
 * a microphone (plain HTTP, Telegram Web iframe) the button is hidden — "Fayl biriktirish" stays.
 */
export function ProofVoiceButton({
  disabled,
  onRecorded,
  onError,
  onRecordingChange,
}: {
  disabled?: boolean
  onRecorded: (file: File, seconds: number) => void
  onError: (message: string) => void
  onRecordingChange: (recording: boolean) => void
}) {
  const [recording, setRecording] = useState(false)
  const [seconds, setSeconds] = useState(0)
  const recorderRef = useRef<MediaRecorder | null>(null)
  const streamRef = useRef<MediaStream | null>(null)
  const chunksRef = useRef<Blob[]>([])
  const timerRef = useRef<number | null>(null)
  const secondsRef = useRef(0)

  // Closing the window must not leave the microphone on.
  useEffect(
    () => () => {
      if (timerRef.current !== null) window.clearInterval(timerRef.current)
      const recorder = recorderRef.current
      if (recorder && recorder.state !== 'inactive') {
        recorder.onstop = null
        recorder.stop()
      }
      streamRef.current?.getTracks().forEach((track) => track.stop())
    },
    [],
  )

  if (!isMicAvailable()) return null

  function setState(next: boolean): void {
    setRecording(next)
    onRecordingChange(next)
  }

  function stop(): void {
    if (timerRef.current !== null) {
      window.clearInterval(timerRef.current)
      timerRef.current = null
    }
    const recorder = recorderRef.current
    if (recorder && recorder.state !== 'inactive') recorder.stop()
  }

  async function start(): Promise<void> {
    let stream: MediaStream
    try {
      stream = await navigator.mediaDevices.getUserMedia({ audio: true })
    } catch (err) {
      onError(micErrorMessage(err))
      return
    }
    const mimeType = pickVoiceMime()
    let recorder: MediaRecorder
    try {
      recorder = new MediaRecorder(stream, mimeType ? { mimeType } : undefined)
    } catch {
      stream.getTracks().forEach((track) => track.stop())
      onError('Bu brauzer ovoz yozishni qo‘llab-quvvatlamaydi. Faylni biriktiring.')
      return
    }
    chunksRef.current = []
    recorder.ondataavailable = (event) => {
      if (event.data.size > 0) chunksRef.current.push(event.data)
    }
    recorder.onstop = () => {
      const blob = new Blob(chunksRef.current, { type: recorder.mimeType || mimeType || 'audio/webm' })
      chunksRef.current = []
      streamRef.current?.getTracks().forEach((track) => track.stop())
      streamRef.current = null
      setState(false)
      if (blob.size === 0) onError('Ovoz yozilmadi — mikrofon jim qoldi.')
      else onRecorded(voiceFile(blob, secondsRef.current), secondsRef.current)
    }
    recorderRef.current = recorder
    streamRef.current = stream
    secondsRef.current = 0
    setSeconds(0)
    setState(true)
    recorder.start()
    timerRef.current = window.setInterval(() => {
      secondsRef.current += 1
      setSeconds(secondsRef.current)
      if (secondsRef.current >= HR_MAX_VOICE_SECONDS) stop()
    }, 1000)
  }

  if (recording) {
    const nearLimit = seconds >= HR_MAX_VOICE_SECONDS - 30
    return (
      <div className="flex items-center gap-2">
        <Button type="button" variant="danger" className="px-3" onClick={stop}>
          <Square className="h-4 w-4" />
          To‘xtatish
        </Button>
        <span role="status" className={cn('text-sm font-medium tabular-nums', nearLimit ? 'text-red-600' : 'text-slate-700')}>
          {hrVoiceDuration(seconds) ?? '0:00'} / {hrVoiceDuration(HR_MAX_VOICE_SECONDS)}
        </span>
      </div>
    )
  }

  return (
    <Button type="button" variant="secondary" className="px-3" disabled={disabled} onClick={() => void start()}>
      <Mic className="h-4 w-4" />
      Ovozli izoh
    </Button>
  )
}
