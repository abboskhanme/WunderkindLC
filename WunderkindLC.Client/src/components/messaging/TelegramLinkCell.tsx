import { CheckCircle2, Circle } from 'lucide-react'
import { relationLabel, sortRelations } from '@/lib/relations'
import { cn } from '@/lib/utils'

/**
 * «Telegram» ustuni: ota-ona (yoki o'quvchi) botga ULANGANMI — yashil «Ulangan» / sariq «Ulanmagan»
 * (2026-10-15, WunderkindLMS dagidek; ilgari qarindoshlik ikonkalari + son chiqardi va qarindoshligi
 * noma'lum ulanishlar «❔ 1» bo'lib ko'rinardi). Kim ulangani va nechta chat — tooltip'da. Bloklaganlar
 * kulrang sanoq. Raqam/ism YO'Q — o'qituvchi ko'rinishida ham xavfsiz.
 */
export function TelegramLinkCell({
  linked,
  relations,
  blocked,
  className,
}: {
  linked?: number | null
  relations?: readonly string[] | null
  blocked?: number | null
  className?: string
}) {
  const n = linked ?? 0
  const who = sortRelations(relations ?? []).map(relationLabel).join(', ')
  return (
    <span className={cn('inline-flex flex-wrap items-center gap-1 whitespace-nowrap', className)}>
      {n === 0 ? (
        <span className="inline-flex items-center gap-1 rounded-md bg-amber-50 px-2 py-0.5 text-xs font-medium text-amber-700">
          <Circle className="h-3.5 w-3.5" /> Ulanmagan
        </span>
      ) : (
        <span
          className="inline-flex items-center gap-1 rounded-md bg-emerald-50 px-2 py-0.5 text-xs font-medium text-emerald-700"
          title={`Telegram botga ulangan: ${n} ta chat${who ? ` (${who})` : ''}`}
        >
          <CheckCircle2 className="h-3.5 w-3.5" /> Ulangan
        </span>
      )}
      {(blocked ?? 0) > 0 && (
        <span className="rounded-md bg-slate-100 px-1.5 py-0.5 text-[11px] text-slate-500" title="Botni bloklagan chatlar">
          {blocked} bloklagan
        </span>
      )}
    </span>
  )
}
