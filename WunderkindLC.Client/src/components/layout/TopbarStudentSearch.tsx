import { useEffect, useRef, useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { Search, User, X } from 'lucide-react'
import { useAuth } from '@/context/auth-context'
import { searchStudents } from '@/api/services/students'
import { studentStateBadge } from '@/config/constants'
import { can } from '@/lib/permissions'
import { cn } from '@/lib/utils'

/** Qidiruv natijasidagi o'quvchi (arxivdagilar ham). */
interface Hit {
  id: string
  fullName: string
  phone?: string
  archived: boolean
  /** A'zolik holati: 'active' | 'trial' | 'frozen' | '' — badge uchun */
  memberState?: string
  /** BARCHA a'zoliklar — dropdownda tagma-tag, har biri o'z holati bilan. */
  groups: { name: string; status: string }[]
}

/** A'zolik holati yorlig'i va rangi (dropdowndagi guruh qatorlari uchun). */
const memberBadge: Record<string, { label: string; cls: string }> = {
  active: { label: 'Aktiv', cls: 'bg-emerald-100 text-emerald-700' },
  trial: { label: 'Sinov', cls: 'bg-amber-100 text-amber-700' },
  frozen: { label: 'Muzlatilgan', cls: 'bg-slate-200 text-slate-600' },
}

/**
 * Topbar'da DOIM ko'rinib turadigan inline o'quvchi qidiruvi (barcha sahifalarda).
 * FISH yoki telefon (o'z/ota/ona) bo'yicha qidiradi, natijalar dropdown'da chiqadi,
 * tanlansa o'quvchi detal sahifasiga (`/admin/students/:id`) o'tadi. Har natija yonida holat
 * belgisi: "arxiv" | "muzlatilgan" | "sinov" (aktiv — belgisiz). Faqat `students` ruxsati borlarga.
 */
export function TopbarStudentSearch() {
  const { user } = useAuth()
  const navigate = useNavigate()
  const [query, setQuery] = useState('')
  const [hits, setHits] = useState<Hit[]>([])
  const [open, setOpen] = useState(false)
  const [searching, setSearching] = useState(false)
  /** Oxirgi so'rov XATO bilan tugadi (tarmoq/server) — "topilmadi" bilan adashtirmaymiz. */
  const [failed, setFailed] = useState(false)
  const [active, setActive] = useState(0)
  const boxRef = useRef<HTMLDivElement>(null)
  const inputRef = useRef<HTMLInputElement>(null)

  // ⚠️ `can(...)` — yalang `includes('students')` EMAS: xodim ruxsati granular bo'lishi mumkin
  // (`students:view`, `students.list`, `students.list:edit` ...). Eski tekshiruv faqat yalang
  // `"students"` tokenini tanir edi — qisman ruxsatli xodim uchun qidiruv maydoni UMUMAN
  // chizilmasdi ("qidiruv tizimi yo'q"). Endpoint kaliti bilan bir xil: `students.list`.
  const canSearch = !!user && can(user.permissions, 'students.list', 'view')

  // Tashqariga bosilganda dropdown'ni yopamiz
  useEffect(() => {
    const onClick = (e: MouseEvent) => {
      if (boxRef.current && !boxRef.current.contains(e.target as Node)) setOpen(false)
    }
    document.addEventListener('mousedown', onClick)
    return () => document.removeEventListener('mousedown', onClick)
  }, [])

  // FISH/telefon bo'yicha qidirish (debounce 250ms). Qidiruv endi SERVERDA — cleanup'da
  // so'rov `AbortController` bilan haqiqatan uziladi (tez terilganda eski so'rovlar tarmoqda
  // qolib ketmasin); abort xatosi (`CanceledError`) `cancelled` bayrog'i orqali jim yutiladi.
  useEffect(() => {
    if (!canSearch) return
    const q = query.trim()
    if (q.length < 2) {
      setHits([])
      setSearching(false)
      return
    }
    setSearching(true)
    setFailed(false)
    let cancelled = false
    const controller = new AbortController()
    const t = setTimeout(async () => {
      try {
        const list = await searchStudents(q, 12, controller.signal)
        if (cancelled) return
        setHits(
          list.map((s) => ({
            id: s.id,
            fullName: s.fullName,
            phone: s.phone || s.parentPhone || undefined,
            archived: s.isArchived,
            memberState: s.memberState,
            // BARCHA a'zoliklar (muzlatilganlari ham) — bu yerda maqsad "qayerda va qanday
            // holatda" ni ko'rsatish, ro'yxat ustunidan farqli o'laroq.
            groups: s.groups,
          })),
        )
        setActive(0)
      } catch {
        // Abort (`cancelled=true`) jim yutiladi; QOLGANI haqiqiy xato — foydalanuvchiga
        // "topilmadi" o'rniga xato ekani ko'rsatiladi (aks holda tarmoq/server nosozligi
        // "o'quvchi yo'q" bo'lib ko'rinardi).
        if (!cancelled) {
          setHits([])
          setFailed(true)
        }
      } finally {
        if (!cancelled) setSearching(false)
      }
    }, 250)
    return () => {
      cancelled = true
      controller.abort()
      clearTimeout(t)
    }
  }, [query, canSearch])

  if (!canSearch) return null

  const go = (s: Hit) => {
    setOpen(false)
    setQuery('')
    setHits([])
    navigate(`/admin/students/${s.id}`)
  }

  const onKeyDown = (e: React.KeyboardEvent) => {
    if (e.key === 'Escape') {
      setOpen(false)
      inputRef.current?.blur()
    } else if (e.key === 'ArrowDown') {
      e.preventDefault()
      setActive((a) => Math.min(a + 1, hits.length - 1))
    } else if (e.key === 'ArrowUp') {
      e.preventDefault()
      setActive((a) => Math.max(a - 1, 0))
    } else if (e.key === 'Enter') {
      e.preventDefault()
      const s = hits[active]
      if (s) go(s)
    }
  }

  const showDropdown = open && query.trim().length >= 2

  return (
    // Maydon edutizimdagidek IXCHAM (340px), natijalar ro'yxati esa KENG (600px): natijada har
    // o'quvchining BARCHA guruhlari tagma-tag chiqadi, tor oynada nomlar qirqilib ketardi.
    <div ref={boxRef} className="relative w-full max-w-[340px]">
      <div className="flex h-9 items-center gap-2 rounded-lg border border-[#dbe0e6] bg-white px-3 transition-colors focus-within:border-brand-400">
        <Search className="h-[18px] w-[18px] shrink-0 text-[#98a2b3]" />
        <input
          ref={inputRef}
          value={query}
          onChange={(e) => {
            setQuery(e.target.value)
            setOpen(true)
          }}
          onFocus={() => setOpen(true)}
          onKeyDown={onKeyDown}
          placeholder="Qidirish..."
          title="Ism, familiya yoki telefon bo'yicha qidirish"
          className="w-full bg-transparent text-sm text-[#333] outline-none placeholder:text-[#98a2b3]"
        />
        {query && (
          <button
            type="button"
            onClick={() => {
              setQuery('')
              setHits([])
              inputRef.current?.focus()
            }}
            className="shrink-0 text-slate-300 transition-colors hover:text-slate-500"
            title="Tozalash"
          >
            <X className="h-4 w-4" />
          </button>
        )}
        {!query && (
          <kbd className="shrink-0 rounded border border-[#dbe0e6] px-1.5 text-[10px] leading-4 text-[#98a2b3]">
            ⌘K
          </kbd>
        )}
      </div>

      {showDropdown && (
        <div className="absolute left-0 top-full z-40 mt-2 max-h-96 w-[600px] max-w-[calc(100vw-32px)] overflow-y-auto rounded-xl border border-slate-200 bg-white p-2 shadow-lg">
          {hits.length === 0 ? (
            <p className="py-6 text-center text-sm text-slate-400">
              {searching
                ? 'Qidirilmoqda...'
                : failed
                  ? "Qidiruv xatosi — internet yoki serverni tekshiring"
                  : 'Hech narsa topilmadi'}
            </p>
          ) : (
            hits.map((s, i) => {
              const badge = studentStateBadge(s.memberState, s.archived)
              return (
              <button
                key={s.id}
                type="button"
                onMouseEnter={() => setActive(i)}
                onClick={() => go(s)}
                className={cn(
                  'flex w-full items-start gap-3 rounded-lg px-3 py-2.5 text-left text-sm transition-colors',
                  i === active ? 'bg-brand-50 text-brand-700' : 'text-slate-700 hover:bg-slate-50',
                )}
              >
                <User className="mt-0.5 h-4 w-4 shrink-0 text-slate-400" />
                <span className="flex min-w-0 flex-1 flex-col">
                  <span className="flex items-center gap-2">
                    <span className="truncate">{s.fullName}</span>
                    {badge && (
                      <span
                        className={cn(
                          'shrink-0 rounded-full px-2 py-0.5 text-[10px] font-semibold uppercase tracking-wide',
                          badge.className,
                        )}
                      >
                        {badge.label}
                      </span>
                    )}
                  </span>
                  {s.phone && (
                    <span className="truncate font-mono text-xs text-slate-400">{s.phone}</span>
                  )}
                  {/* BARCHA guruhlar TAGMA-TAG, har biri o'z holati bilan: o'quvchi eski
                      guruhida muzlatilib yangisida aktiv bo'lsa — ikkalasi ham ko'rinadi,
                      lekin qaysi biri qaysi holatda ekani ADASHTIRMAYDI. */}
                  {s.groups.length > 0 && (
                    <span className="mt-1 flex flex-col gap-0.5">
                      {s.groups.map((g) => {
                        const b = memberBadge[g.status]
                        return (
                          <span key={`${g.name}-${g.status}`} className="flex items-center gap-1.5 text-xs">
                            <span className="truncate text-slate-500">{g.name}</span>
                            {b && (
                              <span
                                className={cn(
                                  'shrink-0 rounded px-1.5 py-0.5 text-[10px] font-semibold',
                                  b.cls,
                                )}
                              >
                                {b.label}
                              </span>
                            )}
                          </span>
                        )
                      })}
                    </span>
                  )}
                </span>
              </button>
              )
            })
          )}
        </div>
      )}
    </div>
  )
}
