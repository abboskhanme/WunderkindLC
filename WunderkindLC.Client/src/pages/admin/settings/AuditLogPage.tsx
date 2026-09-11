import { useCallback, useEffect, useMemo, useState } from 'react'
import { History, RotateCcw, Search } from 'lucide-react'
import { getAuditSections, type AuditSection, type AuditFilters } from '@/api/services/audit'
import { AuditHistoryList } from '@/components/audit/AuditHistoryList'
import { Card } from '@/components/ui/Card'
import { Button } from '@/components/ui/Button'
import { PageHeader } from '@/components/ui/PageHeader'
import { cn } from '@/lib/utils'

/**
 * SOZLAMALAR → O'ZGARISHLAR TARIXI — markazda kim, qachon, nimani o'zgartirgani.
 *
 * <p>Bitta joyda BARCHA bo'limlar: o'quvchilar, guruhlar, o'qituvchilar, kurslar, moliya, lidlar,
 * kitoblar, shartnomalar, vakansiyalar, xodimlar va sozlamalar. Bo'limga bo'lish SERVERDA
 * (`AuditSections`) — yozuvning texnik turi (`entityType`) tarixiy sabablarga ko'ra aldamchi,
 * shuning uchun klient uni o'zi talqin qilmaydi.</p>
 *
 * <p>Ruxsat: `audit` (Xodimlar va rollar). Admin/superadmin bu ruxsatsiz ham ko'radi.</p>
 */
const control =
  'rounded-lg border border-slate-200 bg-white px-3 py-2 text-sm text-slate-700 outline-none transition-colors focus:border-brand-400 focus:ring-2 focus:ring-brand-100'

const actionOptions = [
  { value: '', label: 'Barcha amallar' },
  { value: 'create', label: "Qo'shildi" },
  { value: 'update', label: 'Tahrirlandi' },
  { value: 'delete', label: "O'chirildi" },
]

/** Bir marta yuklanadigan yozuvlar soni. Server chegarasi — 500 (`AuditController.MaxLimit`). */
const PAGE = 100
const MAX = 500

/** "YYYY-MM-DD" — bugundan `days` kun oldin. */
function daysAgo(days: number): string {
  const d = new Date()
  d.setDate(d.getDate() - days)
  return d.toISOString().slice(0, 10)
}

export function AuditLogPage() {
  const [section, setSection] = useState('')
  const [action, setAction] = useState('')
  const [actor, setActor] = useState('')
  /** Yozilayotgan matn — so'rov faqat `q` ga ko'chgandan keyin ketadi (har harfda emas). */
  const [term, setTerm] = useState('')
  const [q, setQ] = useState('')
  const [from, setFrom] = useState(daysAgo(30))
  const [to, setTo] = useState('')
  const [limit, setLimit] = useState(PAGE)

  const [sections, setSections] = useState<AuditSection[]>([])
  const [total, setTotal] = useState(0)
  const [actors, setActors] = useState<string[]>([])
  const [loaded, setLoaded] = useState(0)

  /**
   * `to` — KUN, timestamp esa "YYYY-MM-DDTHH:mm:ss". Xom holda solishtirilsa o'sha kunning
   * o'zi tushib qolardi ("2026-08-05T14:00" > "2026-08-05"), shuning uchun kun oxirigacha cho'zamiz.
   */
  const toBound = to ? `${to}T23:59:59` : ''

  /** Bo'lim sanog'iga TA'SIR QILADIGAN filtrlar (bo'limning o'zi bundan mustasno). */
  const baseFilters = useMemo(
    () => ({ action: action || undefined, from: from || undefined, to: toBound || undefined, actor: actor || undefined, q: q || undefined }),
    [action, from, toBound, actor, q],
  )

  const baseKey = JSON.stringify(baseFilters)
  useEffect(() => {
    let active = true
    getAuditSections(baseFilters)
      .then((r) => {
        if (!active) return
        setSections(r.sections)
        setTotal(r.total)
        setActors(r.actors)
      })
      .catch(() => {
        if (!active) return
        setSections([])
        setTotal(0)
        setActors([])
      })
    return () => {
      active = false
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps -- stabil JSON kalit
  }, [baseKey])

  // Filtr o'zgarsa ro'yxat boshidan boshlansin (aks holda "Ko'proq" bosilgan chuqurlik qolib ketardi).
  useEffect(() => {
    // eslint-disable-next-line react-hooks/set-state-in-effect -- filtr almashganda tiklash (maqsadli)
    setLimit(PAGE)
  }, [baseKey, section])

  const listFilters: AuditFilters = { ...baseFilters, section: section || undefined, limit }
  const onLoaded = useCallback((n: number) => setLoaded(n), [])

  const sectionLabels = useMemo(
    () => Object.fromEntries(sections.map((s) => [s.key, s.label])),
    [sections],
  )

  const reset = () => {
    setSection('')
    setAction('')
    setActor('')
    setTerm('')
    setQ('')
    setFrom(daysAgo(30))
    setTo('')
  }

  const shownSections = sections.filter((s) => s.count > 0 || s.key === section)

  return (
    <div>
      <PageHeader title="Sozlamalar" sub="O'zgarishlar tarixi" />

      <div className="space-y-4">
        <Card
          title="Filtr"
          sub="Davr, amal va xodim bo'yicha. Sanoq chiplardagi sonlarga darhol ta'sir qiladi."
          actions={
            <Button variant="secondary" onClick={reset}>
              <RotateCcw className="h-4 w-4" /> Tozalash
            </Button>
          }
        >
          <div className="flex flex-wrap items-end gap-3">
            <label className="flex flex-col gap-1 text-xs font-medium text-slate-500">
              Sanadan
              <input type="date" value={from} onChange={(e) => setFrom(e.target.value)} className={control} />
            </label>
            <label className="flex flex-col gap-1 text-xs font-medium text-slate-500">
              Sanagacha
              <input type="date" value={to} onChange={(e) => setTo(e.target.value)} className={control} />
            </label>
            <label className="flex flex-col gap-1 text-xs font-medium text-slate-500">
              Amal
              <select value={action} onChange={(e) => setAction(e.target.value)} className={cn(control, 'min-w-[150px]')}>
                {actionOptions.map((o) => (
                  <option key={o.value} value={o.value}>
                    {o.label}
                  </option>
                ))}
              </select>
            </label>
            <label className="flex flex-col gap-1 text-xs font-medium text-slate-500">
              Xodim
              <select value={actor} onChange={(e) => setActor(e.target.value)} className={cn(control, 'min-w-[170px]')}>
                <option value="">Hammasi</option>
                {actors.map((a) => (
                  <option key={a} value={a}>
                    {a}
                  </option>
                ))}
              </select>
            </label>
            <form
              className="flex flex-1 flex-col gap-1 text-xs font-medium text-slate-500"
              onSubmit={(e) => {
                e.preventDefault()
                setQ(term.trim())
              }}
            >
              Izoh bo'yicha qidiruv
              <div className="flex gap-2">
                <input
                  value={term}
                  onChange={(e) => setTerm(e.target.value)}
                  placeholder="Masalan: muzlatildi, chegirma, ruxsat"
                  className={cn(control, 'min-w-[200px] flex-1')}
                />
                <Button type="submit" variant="secondary">
                  <Search className="h-4 w-4" /> Qidirish
                </Button>
              </div>
            </form>
          </div>
        </Card>

        {/* BO'LIMLAR — asosiy talab: o'zgarishlar bo'limlarga ajratib ko'rsatiladi. */}
        <Card title="Bo'limlar" sub="Bo'limni tanlang — pastdagi ro'yxat faqat shu bo'lim o'zgarishlarini ko'rsatadi.">
          <div className="flex flex-wrap gap-2">
            <Chip label="Hammasi" count={total} active={section === ''} onClick={() => setSection('')} />
            {shownSections.map((s) => (
              <Chip
                key={s.key}
                label={s.label}
                count={s.count}
                active={section === s.key}
                onClick={() => setSection(section === s.key ? '' : s.key)}
              />
            ))}
            {shownSections.length === 0 && (
              <span className="py-1 text-sm text-slate-400">Bu filtrlarda o'zgarish topilmadi</span>
            )}
          </div>
        </Card>

        <Card
          title={
            <span className="inline-flex items-center gap-2">
              <History className="h-4 w-4 text-slate-400" />
              {section ? (sectionLabels[section] ?? 'Tarix') : 'Barcha o\'zgarishlar'}
            </span>
          }
          sub="Eng yangisi yuqorida. Tafsilotni ochish uchun qatordagi strelkani bosing."
        >
          <AuditHistoryList
            filters={listFilters}
            sectionLabels={sectionLabels}
            onLoaded={onLoaded}
            emptyLabel="Bu filtrlarda o'zgarish topilmadi"
          />

          {/* Server bir so'rovda ko'pi bilan MAX ta qaytaradi — chegaraga yetganda ochiq aytamiz,
              aks holda "hammasi shu" degan noto'g'ri taassurot qolardi. */}
          {loaded >= limit && limit < MAX && (
            <div className="mt-3 flex justify-center">
              <Button variant="secondary" onClick={() => setLimit((l) => Math.min(l + PAGE, MAX))}>
                Ko'proq ko'rsatish
              </Button>
            </div>
          )}
          {loaded >= MAX && (
            <p className="mt-3 text-center text-xs text-slate-400">
              Bir so'rovda eng ko'pi {MAX} ta yozuv ko'rsatiladi — davrni toraytiring yoki bo'lim tanlang.
            </p>
          )}
        </Card>
      </div>
    </div>
  )
}

function Chip({
  label,
  count,
  active,
  onClick,
}: {
  label: string
  count: number
  active: boolean
  onClick: () => void
}) {
  return (
    <button
      type="button"
      onClick={onClick}
      className={cn(
        'inline-flex items-center gap-1.5 rounded-full border px-3 py-1.5 text-sm font-medium transition-colors',
        active
          ? 'border-brand-500 bg-brand-50 text-brand-700'
          : 'border-slate-200 bg-white text-slate-600 hover:border-slate-300 hover:bg-slate-50',
      )}
    >
      {label}
      <span
        className={cn(
          'rounded-full px-1.5 text-xs font-semibold',
          active ? 'bg-brand-100 text-brand-700' : 'bg-slate-100 text-slate-500',
        )}
      >
        {count}
      </span>
    </button>
  )
}
