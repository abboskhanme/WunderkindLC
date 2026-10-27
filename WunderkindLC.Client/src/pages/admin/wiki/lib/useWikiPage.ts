import { useCallback, useEffect, useRef, useState } from 'react'
import type { WikiPage, WikiPageSource } from '@/api/services/wiki'
import { apiErrorMessage } from '@/lib/utils'
import { AUTOSAVE_MS, isConflictError } from './wikiModel'

/** Ochiq sahifaning saqlanmagan holati. Har sahifa uchun ALOHIDA obyekt: sahifa almashganda
 *  eskisining kechikkan saqlashi yangisining matnini/versiyasini aralashtirib yubormasin. */
interface Session {
  id: string
  title: string
  icon: string
  content: string
  version: number
  dirty: boolean
  saving: boolean
  pending: boolean
  /** 409 — boshqa odam saqlagan, yoki sahifa o'chirilmoqda: avtosaqlash TO'XTAYDI. */
  stopped: boolean
}

export type SaveState = 'idle' | 'dirty' | 'saving' | 'saved' | 'error' | 'conflict'

/**
 * Bitta sahifani ochish va AVTOSAQLASH (`.claude/rules/wiki.md` §4):
 * - yozish to'xtagandan `AUTOSAVE_MS` keyin saqlanadi; sahifa almashganda/yopilganda kutib turgan o'zgarish
 *   DARHOL saqlanadi; brauzer yopilayotganda saqlanmagan bo'lsa — ogohlantirish;
 * - 409 (boshqa odam saqlagan) → avtosaqlash to'xtaydi, `reload()` yangi versiyani yuklaydi;
 * - `source.save` bo'lmasa — faqat o'qish (xodim eslatmasi).
 *
 * `onSaved` — daraxtdagi sarlavhani yangilash uchun (qayta so'rovsiz).
 */
export function useWikiPage(
  source: WikiPageSource,
  pageId: string | undefined,
  onSaved?: (page: WikiPage) => void,
  /** JONLI KUZATISH: shu ms da qayta so'raydi va versiya o'zgargan bo'lsa matnni yangilaydi — faqat
   *  saqlanmagan o'zgarish YO'Q paytda (rahbar xodim yozayotganini ko'radi). 0/yo'q — o'chiq. */
  liveMs?: number,
) {
  const [page, setPage] = useState<WikiPage | null>(null)
  const [pageError, setPageError] = useState<{ id: string; message: string } | null>(null)
  const [nonce, setNonce] = useState(0)
  const [title, setTitleState] = useState('')
  const [save, setSave] = useState<{ state: SaveState; message?: string }>({ state: 'idle' })
  const sessionRef = useRef<Session | null>(null)
  const timerRef = useRef<number | undefined>(undefined)
  // Eng so'nggi qiymatlar ref'da — effekt/callback'lar har renderda qayta yaratilmasin.
  const sourceRef = useRef(source)
  const onSavedRef = useRef(onSaved)
  useEffect(() => {
    sourceRef.current = source
    onSavedRef.current = onSaved
  })

  const flush = useCallback(async function run(s: Session): Promise<void> {
    window.clearTimeout(timerRef.current)
    const saveFn = sourceRef.current.save
    if (!saveFn || !s.dirty || s.stopped) return
    if (s.saving) {
      s.pending = true
      return
    }
    const isCurrent = () => sessionRef.current === s
    s.saving = true
    s.dirty = false
    if (isCurrent()) setSave({ state: 'saving' })
    try {
      const saved = await saveFn(s.id, { title: s.title, icon: s.icon, contentJson: s.content, baseVersion: s.version })
      s.version = saved.version
      onSavedRef.current?.(saved)
      if (isCurrent()) {
        // Versiya ATAYIN yangilanmaydi: muharrir `key` i unga bog'langan — yozayotganda qayta yaratilmasin.
        setPage((p) => (p && p.id === saved.id ? { ...p, updatedAt: saved.updatedAt, updatedBy: saved.updatedBy } : p))
        setSave({ state: s.dirty ? 'dirty' : 'saved' })
      }
    } catch (err) {
      s.dirty = true
      if (isConflictError(err)) {
        s.stopped = true
        if (isCurrent()) setSave({ state: 'conflict', message: apiErrorMessage(err, "Sahifani boshqa odam o'zgartirdi") })
      } else if (isCurrent()) {
        setSave({ state: 'error', message: apiErrorMessage(err, "Saqlab bo'lmadi") })
      }
    } finally {
      s.saving = false
      if (s.pending) {
        s.pending = false
        void run(s)
      }
    }
  }, [])

  const schedule = useCallback(() => {
    const s = sessionRef.current
    if (!s || s.stopped) return
    s.dirty = true
    setSave((cur) => (cur.state === 'saving' ? cur : { state: 'dirty' }))
    window.clearTimeout(timerRef.current)
    timerRef.current = window.setTimeout(() => void flush(s), AUTOSAVE_MS)
  }, [flush])

  // Sahifani yuklash. Almashganda/yopilganda kutib turgan o'zgarish DARHOL saqlanadi.
  useEffect(() => {
    if (!pageId) return
    let alive = true
    sourceRef.current
      .get(pageId)
      .then((p) => {
        if (!alive) return
        sessionRef.current = {
          id: p.id, title: p.title, icon: p.icon, content: p.contentJson, version: p.version,
          dirty: false, saving: false, pending: false, stopped: false,
        }
        setPage(p)
        setTitleState(p.title)
        setSave({ state: 'idle' })
        setPageError(null)
      })
      .catch((err) => {
        if (alive) setPageError({ id: pageId, message: apiErrorMessage(err, "Sahifani ochib bo'lmadi") })
      })
    return () => {
      alive = false
      const s = sessionRef.current
      if (s && s.id === pageId) {
        void flush(s)
        sessionRef.current = null
      }
    }
  }, [pageId, nonce, flush])

  // JONLI KUZATISH. Fon o'qishi — xato jim (keyingi urinishda qayta so'raladi, error-visibility.md §4).
  useEffect(() => {
    if (!pageId || !liveMs) return
    let alive = true
    const timer = window.setInterval(() => {
      const s = sessionRef.current
      if (!s || s.id !== pageId || s.dirty || s.saving) return
      sourceRef.current
        .get(pageId)
        .then((p) => {
          const cur = sessionRef.current
          if (!alive || cur !== s || cur.dirty || cur.saving) return
          if (p.version === cur.version) {
            // Matn o'sha — faqat qulf holati o'zgargan bo'lishi mumkin.
            setPage((prev) =>
              prev && prev.id === p.id && (prev.locked !== p.locked || prev.effectiveLocked !== p.effectiveLocked)
                ? { ...prev, locked: p.locked, effectiveLocked: p.effectiveLocked }
                : prev,
            )
            return
          }
          cur.version = p.version
          cur.title = p.title
          cur.icon = p.icon
          cur.content = p.contentJson
          // Versiya o'zgardi — muharrir `key` i ham o'zgaradi va yangi matn bilan qayta chiziladi.
          setPage(p)
          setTitleState(p.title)
        })
        .catch(() => {})
    }, liveMs)
    return () => {
      alive = false
      window.clearInterval(timer)
    }
  }, [pageId, liveMs])

  // Brauzer yopilayotganda saqlanmagan o'zgarish bo'lsa — ogohlantirish.
  useEffect(() => {
    const onBeforeUnload = (e: BeforeUnloadEvent) => {
      const s = sessionRef.current
      if (s && (s.dirty || s.saving) && !s.stopped) {
        void flush(s)
        e.preventDefault()
      }
    }
    window.addEventListener('beforeunload', onBeforeUnload)
    return () => window.removeEventListener('beforeunload', onBeforeUnload)
  }, [flush])

  return {
    /** Ochiq sahifa (boshqasi yuklanayotganda — null) */
    page: pageId && page?.id === pageId ? page : null,
    error: pageId && pageError?.id === pageId ? pageError.message : '',
    title,
    save,
    setTitle: (v: string) => {
      setTitleState(v)
      if (sessionRef.current) sessionRef.current.title = v
      schedule()
    },
    setContent: (json: string) => {
      if (sessionRef.current) sessionRef.current.content = json
      schedule()
    },
    /** Xatodan keyin qayta urinish */
    retry: () => {
      if (sessionRef.current) void flush(sessionRef.current)
    },
    /** Serverdan qayta yuklash (409 dan keyin) — muharrir yangi versiya bilan qayta yaratiladi */
    reload: () => setNonce((n) => n + 1),
    /** Sahifa o'chirilmoqda — avtosaqlash o'chgan sahifaga yozmasin (`resume` — o'chirish muvaffaqiyatsiz bo'lsa) */
    stop: () => {
      if (sessionRef.current) sessionRef.current.stopped = true
    },
    resume: () => {
      if (sessionRef.current) sessionRef.current.stopped = false
    },
    /** Qulf holati o'zgarganda sahifa obyektini yangilash (qayta yuklamasdan) */
    patchPage: (patch: Partial<WikiPage>) => setPage((p) => (p ? { ...p, ...patch } : p)),
  }
}
