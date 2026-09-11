import type { LeadOriginRow } from '@/api/services/leads'
import { cn, formatMoney } from '@/lib/utils'

/**
 * «LIDLAR QAYERDAN KELADI» — kanal kesimi.
 *
 * <p>Savol "qaysi kanal ko'p lid beradi" EMAS, «qaysi kanal haqiqatan SOTADI»: shuning uchun
 * har qatorda konversiya bilan yonma-yon TO'LOV ulushi ham turadi. Ko'p lid beradigan kanal
 * eng yomon sotuvchi bo'lib chiqishi mumkin — bu jadval aynan shuni ochib beradi.</p>
 *
 * <p>Magnituda UZUNLIK bilan beriladi (rang bilan emas): bu bir o'lchovli taqqoslash, chiziq
 * uzunligi eng katta kanalga nisbatan. Yonidagi foiz esa JAMIga nisbatan (ulush).</p>
 *
 * <p>YAGONA komponent: CRM statistikasi (davr bo'yicha) ham, "Formalar"/"Daraja testi"
 * sahifalaridagi «Butun CRM manzarasi» bloki ham shuni chizadi.</p>
 */
export function OriginTable({
  origins,
  /** Shu sahifa qamrab olgan kanal (`form` | `test`) — qatori ajratib ko'rsatiladi. */
  highlight,
}: {
  origins: LeadOriginRow[]
  highlight?: string
}) {
  if (origins.length === 0)
    return <p className="py-8 text-center text-sm text-slate-400">Ma'lumot yo'q.</p>

  const total = origins.reduce((a, o) => a + o.leads, 0)
  const max = Math.max(0, ...origins.map((o) => o.leads))
  const share = (n: number) => (total > 0 ? Math.round((n / total) * 100) : 0)

  return (
    <div className="overflow-x-auto">
      <table className="w-full text-left text-sm">
        <thead className="border-b border-slate-100 text-xs uppercase tracking-wide text-slate-400">
          <tr>
            <th className="py-2 pr-3 font-medium">Kanal</th>
            <th className="py-2 pr-3 font-medium">Lidlar</th>
            <th className="py-2 pr-3 text-right font-medium">Aylandi</th>
            <th className="py-2 pr-3 text-right font-medium">Konversiya</th>
            <th className="py-2 pr-3 text-right font-medium">To'ladi</th>
            <th className="py-2 pr-3 text-right font-medium">Sotuv %</th>
            <th className="py-2 text-right font-medium">Tushum</th>
          </tr>
        </thead>
        <tbody>
          {origins.map((o) => (
            <tr
              key={o.key}
              className={cn(
                'border-b border-slate-50 last:border-0',
                // Shu sahifa qamragan kanal ajratiladi: "bu sahifa mana shu qatorni tafsilotlaydi".
                o.key === highlight && 'bg-brand-50/40',
              )}
            >
              <td className="py-2 pr-3 font-medium text-slate-700">
                {o.label}
                {o.key === highlight && (
                  <span className="ml-2 text-[11px] font-normal text-brand-600">(shu sahifa)</span>
                )}
              </td>
              <td className="py-2 pr-3">
                <div className="flex items-center gap-2">
                  <div className="h-2 w-20 shrink-0 overflow-hidden rounded-full bg-slate-100">
                    <div
                      className="h-full rounded-full bg-brand-400"
                      style={{ width: `${max > 0 ? Math.round((o.leads / max) * 100) : 0}%` }}
                    />
                  </div>
                  <span className="font-mono tabular-nums text-slate-700">{o.leads}</span>
                  <span className="text-[11px] text-slate-400">{share(o.leads)}%</span>
                </div>
              </td>
              <td className="py-2 pr-3 text-right font-mono tabular-nums text-slate-600">
                {o.converted}
              </td>
              <td className="py-2 pr-3 text-right font-mono tabular-nums text-slate-500">
                {o.conversionRate}%
              </td>
              <td className="py-2 pr-3 text-right font-mono tabular-nums text-teal-700">{o.paid}</td>
              <td
                className={cn(
                  'py-2 pr-3 text-right font-mono tabular-nums font-semibold',
                  o.payRate >= 20 ? 'text-teal-700' : o.payRate > 0 ? 'text-slate-600' : 'text-slate-300',
                )}
              >
                {o.payRate}%
              </td>
              <td className="py-2 text-right font-mono tabular-nums text-teal-700">
                {o.revenue > 0 ? formatMoney(o.revenue) : <span className="text-slate-300">—</span>}
              </td>
            </tr>
          ))}
        </tbody>
      </table>
      <p className="mt-2 text-xs text-slate-400">
        «Qo'lda kiritilgan» — xodim CRM'da o'zi ochgan lid. Eski yozuvlarda kim kiritgani
        saqlanmagani uchun ular «Boshqa» ga tushadi.
      </p>
    </div>
  )
}
