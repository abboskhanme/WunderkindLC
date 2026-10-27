import { useEffect, useState } from 'react'
import type { WikiSearchHit } from '@/api/services/wiki'
import { apiErrorMessage } from '@/lib/utils'

/**
 * Chap paneldagi qidiruv: 300 ms kechikish bilan, kamida 2 belgi. `hits === null` — qidiruv yo'q,
 * daraxt ko'rsatiladi. Xato — natijalar o'rnida matn bilan (jim yutilmaydi).
 */
export function useWikiSearch(searchFn: (q: string) => Promise<WikiSearchHit[]>) {
  const [q, setQ] = useState('')
  const [result, setResult] = useState<{ q: string; hits: WikiSearchHit[]; error: string } | null>(null)

  useEffect(() => {
    const term = q.trim()
    if (term.length < 2) return
    let alive = true
    const timer = window.setTimeout(() => {
      searchFn(term)
        .then((hits) => {
          if (alive) setResult({ q: term, hits, error: '' })
        })
        .catch((err) => {
          if (alive) setResult({ q: term, hits: [], error: apiErrorMessage(err, "Qidirib bo'lmadi") })
        })
    }, 300)
    return () => {
      alive = false
      window.clearTimeout(timer)
    }
  }, [q, searchFn])

  const active = q.trim().length >= 2
  return {
    q,
    setQ,
    /** null — qidiruv yo'q (daraxtni ko'rsating); natija hali kelmagan bo'lsa — bo'sh ro'yxat */
    hits: active ? (result?.q === q.trim() ? result.hits : []) : null,
    error: active && result?.q === q.trim() ? result.error : '',
  }
}
