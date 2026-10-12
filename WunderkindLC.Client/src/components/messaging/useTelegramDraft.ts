import { useEffect, useState } from 'react'
import {
  countAudience,
  previewMessage,
  type AudienceCount,
  type AudienceSpec,
  type CampaignKind,
  type PreviewResult,
} from '@/api/services/messaging'
import { apiErrorMessage } from '@/lib/utils'

const DEBOUNCE_MS = 400

/**
 * Server PREVIEW (debounce 400 ms, SPEC P2.8). Natija — server renderi; shablon xatolari 400 emas,
 * `errors` ichida keladi. Eski so'rov yangisi kelganda bekor qilinadi (AbortController).
 */
export function useMessagePreview(input: {
  template: string
  audience?: AudienceSpec | null
  studentId?: string
  attachmentId?: string | null
  trigger?: string
  kind?: CampaignKind
  enabled?: boolean
}): { preview: PreviewResult | null; loading: boolean } {
  const [preview, setPreview] = useState<PreviewResult | null>(null)
  const [loading, setLoading] = useState(false)
  const key = JSON.stringify(input)

  useEffect(() => {
    const req = JSON.parse(key) as typeof input
    if (req.enabled === false) return
    const ctrl = new AbortController()
    const t = window.setTimeout(() => {
      setLoading(true)
      previewMessage(
        {
          template: req.template,
          audience: req.audience ?? undefined,
          studentId: req.studentId,
          attachmentId: req.attachmentId ?? undefined,
          trigger: req.trigger,
          kind: req.kind,
        },
        ctrl.signal,
      )
        .then((p) => setPreview(p))
        // Fon o'qish (preview): tarmoq xatosi — eski ko'rinish qoladi; bekor qilingani — jim.
        .catch(() => {})
        .finally(() => {
          if (!ctrl.signal.aborted) setLoading(false)
        })
    }, DEBOUNCE_MS)
    return () => {
      window.clearTimeout(t)
      ctrl.abort()
    }
  }, [key])

  return { preview, loading }
}

/**
 * Yuborishdan OLDINGI sanoq (debounce 400 ms) — server `campaigns` bilan AYNAN bir xil rejalashtiruvchi,
 * ya'ni ko'rsatilgan son = yaratiladigan xabarlar. Noto'g'ri tanlov (masalan guruh tanlanmagan) — `error`.
 */
export function useAudienceCount(input: {
  audience: AudienceSpec | null
  template: string
  kind?: CampaignKind
  enabled?: boolean
}): { count: AudienceCount | null; loading: boolean; error: string } {
  const [count, setCount] = useState<AudienceCount | null>(null)
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState('')
  const key = JSON.stringify(input)

  useEffect(() => {
    const req = JSON.parse(key) as typeof input
    if (req.enabled === false || !req.audience) return
    const ctrl = new AbortController()
    const t = window.setTimeout(() => {
      setLoading(true)
      countAudience(req.audience!, req.template, req.kind, ctrl.signal)
        .then((c) => {
          setCount(c)
          setError('')
        })
        .catch((e) => {
          if (ctrl.signal.aborted) return
          setCount(null)
          setError(apiErrorMessage(e, "Oluvchilarni sanab bo'lmadi"))
        })
        .finally(() => {
          if (!ctrl.signal.aborted) setLoading(false)
        })
    }, DEBOUNCE_MS)
    return () => {
      window.clearTimeout(t)
      ctrl.abort()
    }
  }, [key])

  return { count, loading, error }
}
