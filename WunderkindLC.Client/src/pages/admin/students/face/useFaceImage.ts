import { useEffect, useState } from 'react'
import { fetchFaceImage } from '@/api/services/face'

/**
 * Selfi rasmini avtorizatsiya bilan yuklaydi va `<img src>` ga yaroqli vaqtinchalik
 * (blob) manzil qaytaradi.
 *
 * NEGA KERAK: biometrik surat `uploads/face/` da yotadi va STATIK yo'l bilan umuman
 * berilmaydi (sertifikatlar bilan bir xil siyosat) — u faqat `/api/admin/face/...`
 * endpointidan, JWT bilan olinadi. Brauzer esa `<img>` so'roviga `Authorization`
 * sarlavhasini qo'shmaydi, ya'ni to'g'ridan-to'g'ri `src` ishlamaydi.
 *
 * Manzil komponent yopilganda yoki `url` o'zgarganda BEKOR QILINADI — aks holda
 * ro'yxatni varaqlagan sari blob'lar brauzer xotirasida to'planib qolardi.
 */
export function useFaceImage(url: string | null | undefined) {
  const [src, setSrc] = useState<string | null>(null)
  const [failed, setFailed] = useState(false)

  useEffect(() => {
    if (!url) {
      setSrc(null)
      setFailed(false)
      return
    }
    let alive = true
    let objectUrl: string | null = null
    setSrc(null)
    setFailed(false)
    fetchFaceImage(url)
      .then((u) => {
        if (!alive) {
          // Komponent allaqachon yopilgan — yaratilgan manzilni tashlab ketmaymiz.
          if (u) URL.revokeObjectURL(u)
          return
        }
        objectUrl = u
        // Bo'sh satr — mock rejim: rasm yo'q, lekin bu XATO emas.
        if (u) setSrc(u)
        else setFailed(true)
      })
      .catch(() => {
        if (alive) setFailed(true)
      })
    return () => {
      alive = false
      if (objectUrl) URL.revokeObjectURL(objectUrl)
    }
  }, [url])

  return { src, failed, loading: !!url && !src && !failed }
}
