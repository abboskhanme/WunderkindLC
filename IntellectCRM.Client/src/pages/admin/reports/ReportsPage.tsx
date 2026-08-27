import { useMemo, useState } from 'react'
import { Link } from 'react-router-dom'
import { ChevronRight, Search, X } from 'lucide-react'
import { PageHeader } from '@/components/ui/PageHeader'
import { Card } from '@/components/ui/Card'
import { usePerm } from '@/lib/permissions'
import { visibleReportGroups, type ReportGroup } from '@/config/reports'
import { cn } from '@/lib/utils'

/**
 * HISOBOTLAR — markazning barcha analitika/hisobot sahifalari BIR joyda.
 *
 * ⚠️ Ro'yxat shu yerda EMAS, `config/reports.ts` da: AYNAN o'sha katalogdan yon menyudagi
 * "Hisobotlar" guruhi ham quriladi. Ikki joyda yozilsa ular vaqt o'tib ayrilib ketardi
 * (menyuda bor, hub'da yo'q — foydalanuvchi uchun "hisobot yo'qolib qolgan" ko'rinardi).
 *
 * Marshrutlar O'ZGARMAGAN — bu sahifa mavjud hisobotlarga YO'L ko'rsatadi, ya'ni eski
 * havolalar, xatcho'plar va boshqa sahifalardagi ichki linklar ishlayveradi.
 */
export function ReportsPage() {
  const { can } = usePerm()
  const [query, setQuery] = useState('')

  // Ruxsati bor hisobotlargina — bo'sh qolgan guruh umuman chizilmaydi (Sidebar qoidasi bilan bir xil).
  const groups = useMemo<ReportGroup[]>(
    () => visibleReportGroups((perm) => can(perm, 'view')),
    [can],
  )

  const q = query.trim().toLowerCase()
  const shown = useMemo(() => {
    if (!q) return groups
    return groups
      .map((g) => ({
        ...g,
        items: g.items.filter(
          (i) => i.label.toLowerCase().includes(q) || i.description.toLowerCase().includes(q),
        ),
      }))
      .filter((g) => g.items.length > 0)
  }, [groups, q])

  const total = groups.reduce((n, g) => n + g.items.length, 0)

  return (
    <div>
      <PageHeader
        title="Hisobotlar"
        sub={`Markazning barcha analitika va hisobotlari bir joyda — ${total} ta`}
        actions={
          <div className="relative">
            <Search className="pointer-events-none absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-slate-400" />
            <input
              value={query}
              onChange={(e) => setQuery(e.target.value)}
              placeholder="Hisobot qidirish..."
              className="w-64 rounded-lg border border-slate-200 bg-white py-2 pl-9 pr-8 text-sm text-slate-700 outline-none transition-colors focus:border-brand-400"
            />
            {query && (
              <button
                type="button"
                onClick={() => setQuery('')}
                title="Tozalash"
                className="absolute right-2 top-1/2 -translate-y-1/2 text-slate-400 transition-colors hover:text-slate-600"
              >
                <X className="h-4 w-4" />
              </button>
            )}
          </div>
        }
      />

      {shown.length === 0 ? (
        <Card>
          <p className="py-10 text-center text-sm text-slate-400">
            {total === 0
              ? "Sizga ochiq hisobot yo'q."
              : `«${query}» bo'yicha hisobot topilmadi.`}
          </p>
        </Card>
      ) : (
        <div className="space-y-5">
          {shown.map((g) => {
            const Icon = g.icon
            return (
              <Card key={g.key} tight>
                <div className="flex items-center gap-2 border-b border-slate-100 px-4 py-3">
                  <Icon className="h-4 w-4 text-brand-600" />
                  <h2 className="text-sm font-semibold text-slate-800">{g.label}</h2>
                  <span className="text-xs text-slate-400">{g.items.length}</span>
                </div>
                <div className="grid gap-px bg-slate-100 sm:grid-cols-2 xl:grid-cols-3">
                  {g.items.map((i) => (
                    <Link
                      key={i.to}
                      to={i.to}
                      className={cn(
                        'group flex items-start gap-3 bg-white px-4 py-3 transition-colors hover:bg-brand-50/60',
                      )}
                    >
                      <div className="min-w-0 flex-1">
                        <p className="truncate text-sm font-semibold text-slate-800 group-hover:text-brand-700">
                          {i.label}
                        </p>
                        {/* Izoh — "bu hisobot qaysi savolga javob beradi". Nom o'zi ko'pincha
                            yetarli emas ("Analitika" — nimaning analitikasi?). */}
                        <p className="mt-0.5 text-xs leading-relaxed text-slate-400">{i.description}</p>
                      </div>
                      <ChevronRight className="mt-0.5 h-4 w-4 shrink-0 text-slate-300 transition-colors group-hover:text-brand-500" />
                    </Link>
                  ))}
                </div>
              </Card>
            )
          })}
        </div>
      )}
    </div>
  )
}
