import { useEffect, useState } from 'react'

/**
 * Avtorizatsiyalangan endpointdan rasmni BLOB URL qilib oladi (yopiq `uploads/messaging` fayllari
 * statik yo'ldan berilmaydi — `uploads-security.md`). `key` o'zgarsa qayta yuklanadi; eski URL
 * va komponent yopilganda URL bo'shatiladi (`revokeObjectURL`). Xato — `null` (fon o'qish: rasm
 * o'rniga ikonka chiqadi).
 */
export function useBlobUrl(key: string | null, loader: (key: string) => Promise<string>): string | null {
  const [state, setState] = useState<{ key: string; url: string } | null>(null)

  useEffect(() => {
    if (!key) return
    let alive = true
    let created: string | null = null
    loader(key)
      .then((url) => {
        created = url
        if (alive) setState({ key, url })
        else URL.revokeObjectURL(url)
      })
      .catch(() => {
        /* fon o'qish — rasm o'rniga ikonka */
      })
    return () => {
      alive = false
      if (created) URL.revokeObjectURL(created)
    }
    // `loader` — modul darajasidagi funksiya; har renderda yangi bo'lsa ham qayta yuklamaymiz.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [key])

  return state && state.key === key ? state.url : null
}
