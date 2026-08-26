import { useEffect, useMemo, useRef, useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { Search, User } from 'lucide-react'
import type { LucideIcon } from 'lucide-react'
import type { Role } from '@/types'
import { useAuth } from '@/context/auth-context'
import { navByRole } from '@/config/navigation'
import { teacherTabs, roomTabs, formTabs } from '@/config/sectionTabs'
import type { CardTabItem } from '@/components/ui/CardTabs'
import { searchStudents } from '@/api/services/students'
import { studentStateBadge } from '@/config/constants'
import { can as hasPerm } from '@/lib/permissions'
import { cn } from '@/lib/utils'

interface Cmd {
  label: string
  /** Ota guruh nomi (masalan "Boshqaruv") — bor bo'lsa ko'rsatamiz */
  group?: string
  to: string
  icon: LucideIcon
}

/** Qidiruv natijasidagi o'quvchi (arxivdagilar ham — `archived` bilan belgilanadi). */
interface StudentHit {
  id: string
  fullName: string
  /** Ko'rsatish uchun raqam (o'z, bo'lmasa ota-ona) */
  phone?: string
  className?: string
  archived: boolean
  /** A'zolik holati: 'active' | 'trial' | 'frozen' | '' — badge uchun */
  memberState?: string
}

/**
 * Ctrl/⌘+K buyruq paneli — bo'limlar bo'ylab tez o'tish + o'quvchini FISH yoki TELEFON
 * (o'z/ota/ona/ota-ona) bo'yicha global qidirish. Arxivlanganlar ham chiqadi; har natija yonida
 * holat belgisi: "arxiv" | "muzlatilgan" | "sinov" (aktiv — belgisiz). Tanlansa
 * o'quvchi detal sahifasiga (`/admin/students/:id`) o'tadi.
 * Bo'lim ro'yxati Sidebar bilan bir xil mantiqda (rol + ruxsat) filtrlanadi.
 * Boshqa joydan ochish uchun: `window.dispatchEvent(new Event('cmdk:open'))`.
 */
export function CommandPalette() {
  const { user } = useAuth()
  const navigate = useNavigate()
  const [open, setOpen] = useState(false)
  const [query, setQuery] = useState('')
  const [active, setActive] = useState(0)
  const [students, setStudents] = useState<StudentHit[]>([])
  const [searching, setSearching] = useState(false)
  const inputRef = useRef<HTMLInputElement>(null)
  const listRef = useRef<HTMLDivElement>(null)

  // O'quvchi qidiruvi faqat admin (students ruxsati borlar) uchun.
  const canSearchStudents = !!user && (!user.permissions || user.permissions.includes('students'))

  // Ochish/yopish: Ctrl/⌘+K yoki tashqi 'cmdk:open' hodisasi
  useEffect(() => {
    const onKey = (e: KeyboardEvent) => {
      if ((e.ctrlKey || e.metaKey) && e.key.toLowerCase() === 'k') {
        e.preventDefault()
        setOpen((o) => !o)
      }
    }
    const onOpen = () => setOpen(true)
    document.addEventListener('keydown', onKey)
    window.addEventListener('cmdk:open', onOpen)
    return () => {
      document.removeEventListener('keydown', onKey)
      window.removeEventListener('cmdk:open', onOpen)
    }
  }, [])

  // Ochilganda qidiruvni tozalab, inputni fokuslaymiz
  useEffect(() => {
    if (!open) return
    setQuery('')
    setActive(0)
    setStudents([])
    const t = setTimeout(() => inputRef.current?.focus(), 0)
    return () => clearTimeout(t)
  }, [open])

  // Ko'rinadigan bo'limlar ro'yxati (Sidebar'dagi canSee bilan bir xil)
  const commands = useMemo<Cmd[]>(() => {
    if (!user) return []
    const role = user.role as Role
    // ⚠️ Sidebar bilan AYNAN bir xil qoida (`lib/permissions.can`): "leads:view" kabi AMAL
    // tokeni ham bo'limni ochadi va `permAny` (bir necha ruxsatdan biri yetarli — masalan
    // "Formalar": lid formalari `leads`, daraja testi `schedule`) hisobga olinadi.
    // Ilgari bu yerda yalang `permissions.includes(perm)` turardi: `permAny` umuman
    // tekshirilmasdi (ya'ni "Formalar" HECH QANDAY ruxsati yo'q xodimga ham chiqardi), granular
    // ruxsatli xodim esa menyuda ko'rinadigan bo'limni Ctrl+K dan topa olmasdi.
    const perms = user.permissions
    const canSee = (x: { roles?: Role[]; perm?: string; permAny?: string[] }) =>
      (!x.roles || x.roles.includes(role)) &&
      (!x.perm || hasPerm(perms, x.perm, 'view')) &&
      (!x.permAny || x.permAny.some((p) => hasPerm(perms, p, 'view')))
    const out: Cmd[] = []
    for (const item of navByRole[role]) {
      if (!canSee(item)) continue
      if (item.children) {
        for (const c of item.children) {
          if (canSee(c)) out.push({ label: c.label, group: item.label, to: c.to, icon: item.icon })
        }
      } else {
        out.push({ label: item.label, to: item.to, icon: item.icon })
      }
    }

    // BO'LIM ICHIDAGI CARD SAHIFALARI. Ular menyuda alohida band emas — bo'lim ichida
    // `CardTabs` orqali ochiladi. Lekin Ctrl+K dan topilishi SHART: aks holda menyuni
    // birlashtirish qidiruvni yo'qotib qo'yardi ("O'qituvchilar davomati" ni yozib topib
    // bo'lmasdi). Manba `sectionTabs` — cardlar bilan bir xil ro'yxat, ikki joyda takrorlanmaydi.
    const seen = new Set(out.map((c) => c.to))
    const addTabs = (tabs: CardTabItem[], group: string, icon: LucideIcon) => {
      for (const t of tabs) {
        // `hidden` — ruxsati yo'q (masalan «Hisoboti»); `seen` — menyuda allaqachon bor sahifa.
        if (t.hidden || seen.has(t.to)) continue
        seen.add(t.to)
        out.push({ label: `${group} — ${t.label}`, group, to: t.to, icon })
      }
    }
    // Faqat bo'lim boshi shu foydalanuvchiga KO'RINSA qo'shamiz — rol/ruxsat qoidasi
    // menyudan meros bo'lib qoladi, alohida tekshiruv yozilmaydi.
    // Bo'lim boshi (yoki uni o'z ichiga olgan guruh) — ikonkani o'shandan olamiz.
    const ownerOf = (to: string) =>
      navByRole[role].find((i) => i.to === to || i.children?.some((c) => c.to === to))

    const teachers = ownerOf('/admin/teachers')
    if (teachers && seen.has('/admin/teachers'))
      addTabs(teacherTabs((perm) => canSee({ perm })), "O'qituvchilar", teachers.icon)

    const rooms = ownerOf('/admin/rooms')
    if (rooms && seen.has('/admin/rooms')) addTabs(roomTabs, 'Xonalar', rooms.icon)

    // ⚠️ «Daraja testi» ilgari menyuda ALOHIDA band edi va Ctrl+K dan topilardi; endi u
    // "Formalar" bo'limi ichidagi card — shu sabab shu yerda qo'shiladi, aks holda "daraja"
    // deb qidirganda hech narsa topilmasdi.
    const forms = ownerOf('/admin/forms')
    if (forms && seen.has('/admin/forms'))
      addTabs(
        formTabs(canSee({ perm: 'leads.forms' }), canSee({ perm: 'schedule.levelTests' })),
        'Formalar',
        forms.icon,
      )

    return out
  }, [user])

  const sectionResults = useMemo(() => {
    const q = query.trim().toLowerCase()
    if (!q) return commands
    return commands.filter(
      (c) => c.label.toLowerCase().includes(q) || (c.group?.toLowerCase().includes(q) ?? false),
    )
  }, [commands, query])

  // O'quvchilarni FISH/telefon bo'yicha qidirish (debounce). Arxivdagilar ham qaytadi.
  // Qidiruv endi SERVERDA — cleanup'da so'rov `AbortController` bilan haqiqatan uziladi;
  // abort xatosi (`CanceledError`) `cancelled` bayrog'i orqali jim yutiladi.
  useEffect(() => {
    if (!open || !canSearchStudents) {
      setStudents([])
      return
    }
    const q = query.trim()
    if (q.length < 2) {
      setStudents([])
      setSearching(false)
      return
    }
    setSearching(true)
    let cancelled = false
    const controller = new AbortController()
    const t = setTimeout(async () => {
      try {
        const list = await searchStudents(q, 12, controller.signal)
        if (cancelled) return
        setStudents(
          list.map((s) => ({
            id: s.id,
            fullName: s.fullName,
            phone: s.phone || s.parentPhone || undefined,
            className: s.groups[0]?.name || undefined,
            archived: s.isArchived,
            memberState: s.memberState,
          })),
        )
      } catch {
        if (!cancelled) setStudents([])
      } finally {
        if (!cancelled) setSearching(false)
      }
    }, 250)
    return () => {
      cancelled = true
      controller.abort()
      clearTimeout(t)
    }
  }, [query, open, canSearchStudents])

  // Birlashtirilgan tekis ro'yxat: avval bo'limlar, keyin o'quvchilar (klaviatura navigatsiyasi uchun)
  const total = sectionResults.length + students.length

  // Qidiruv o'zgarsa tanlovni boshiga qaytaramiz
  useEffect(() => setActive(0), [query, students.length])

  // Tanlangan element ko'rinishda turishi uchun unga aylantiramiz
  useEffect(() => {
    listRef.current
      ?.querySelector<HTMLElement>(`[data-idx="${active}"]`)
      ?.scrollIntoView({ block: 'nearest' })
  }, [active, total])

  if (!open) return null

  const goSection = (c: Cmd) => {
    setOpen(false)
    navigate(c.to)
  }

  const goStudent = (s: StudentHit) => {
    setOpen(false)
    navigate(`/admin/students/${s.id}`)
  }

  const goActive = () => {
    if (active < sectionResults.length) {
      const c = sectionResults[active]
      if (c) goSection(c)
    } else {
      const s = students[active - sectionResults.length]
      if (s) goStudent(s)
    }
  }

  const onKeyDown = (e: React.KeyboardEvent) => {
    if (e.key === 'Escape') {
      setOpen(false)
    } else if (e.key === 'ArrowDown') {
      e.preventDefault()
      setActive((a) => Math.min(a + 1, total - 1))
    } else if (e.key === 'ArrowUp') {
      e.preventDefault()
      setActive((a) => Math.max(a - 1, 0))
    } else if (e.key === 'Enter') {
      e.preventDefault()
      goActive()
    }
  }

  const hasQuery = query.trim().length > 0

  return (
    <div
      className="fixed inset-0 z-50 flex items-start justify-center bg-slate-900/40 px-4 pt-[15vh]"
      onClick={() => setOpen(false)}
    >
      <div
        className="w-full max-w-lg overflow-hidden rounded-2xl bg-white shadow-2xl"
        onClick={(e) => e.stopPropagation()}
        onKeyDown={onKeyDown}
      >
        <div className="flex items-center gap-3 border-b border-slate-100 px-4">
          <Search className="h-5 w-5 shrink-0 text-slate-400" />
          <input
            ref={inputRef}
            value={query}
            onChange={(e) => setQuery(e.target.value)}
            placeholder={
              canSearchStudents ? "O'quvchi (FISH/telefon) yoki bo'lim qidirish..." : "Bo'lim qidirish..."
            }
            className="flex-1 bg-transparent py-4 text-sm text-slate-800 outline-none placeholder:text-slate-400"
          />
          <kbd className="rounded border border-slate-200 px-1.5 py-0.5 text-[10px] text-slate-400">
            ESC
          </kbd>
        </div>

        <div ref={listRef} className="max-h-96 overflow-y-auto p-2">
          {total === 0 ? (
            <p className="py-10 text-center text-sm text-slate-400">
              {searching ? 'Qidirilmoqda...' : 'Hech narsa topilmadi'}
            </p>
          ) : (
            <>
              {sectionResults.length > 0 && (
                <>
                  <p className="px-3 pb-1 pt-2 text-[11px] font-semibold uppercase tracking-wide text-slate-400">
                    Bo'limlar
                  </p>
                  {sectionResults.map((c, i) => (
                    <button
                      key={c.to + c.label}
                      type="button"
                      data-idx={i}
                      onMouseEnter={() => setActive(i)}
                      onClick={() => goSection(c)}
                      className={cn(
                        'flex w-full items-center gap-3 rounded-lg px-3 py-2.5 text-left text-sm transition-colors',
                        i === active ? 'bg-brand-50 text-brand-700' : 'text-slate-700 hover:bg-slate-50',
                      )}
                    >
                      <c.icon className="h-4 w-4 shrink-0 text-slate-400" />
                      <span className="flex-1 truncate">
                        {c.group && <span className="text-slate-400">{c.group} · </span>}
                        {c.label}
                      </span>
                    </button>
                  ))}
                </>
              )}

              {canSearchStudents && hasQuery && (students.length > 0 || searching) && (
                <>
                  <p className="px-3 pb-1 pt-3 text-[11px] font-semibold uppercase tracking-wide text-slate-400">
                    O'quvchilar
                  </p>
                  {searching && students.length === 0 ? (
                    <p className="px-3 py-2 text-sm text-slate-400">Qidirilmoqda...</p>
                  ) : (
                    students.map((s, j) => {
                      const idx = sectionResults.length + j
                      const badge = studentStateBadge(s.memberState, s.archived)
                      return (
                        <button
                          key={s.id}
                          type="button"
                          data-idx={idx}
                          onMouseEnter={() => setActive(idx)}
                          onClick={() => goStudent(s)}
                          className={cn(
                            'flex w-full items-center gap-3 rounded-lg px-3 py-2.5 text-left text-sm transition-colors',
                            idx === active ? 'bg-brand-50 text-brand-700' : 'text-slate-700 hover:bg-slate-50',
                          )}
                        >
                          <User className="h-4 w-4 shrink-0 text-slate-400" />
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
                            {(s.phone || s.className) && (
                              <span className="truncate text-xs text-slate-400">
                                {s.className && <span>{s.className}</span>}
                                {s.className && s.phone && <span> · </span>}
                                {s.phone && <span className="font-mono">{s.phone}</span>}
                              </span>
                            )}
                          </span>
                        </button>
                      )
                    })
                  )}
                </>
              )}
            </>
          )}
        </div>

        <div className="flex items-center gap-4 border-t border-slate-100 px-4 py-2 text-[11px] text-slate-400">
          <span>↑↓ tanlash</span>
          <span>↵ ochish</span>
          <span>esc yopish</span>
        </div>
      </div>
    </div>
  )
}
