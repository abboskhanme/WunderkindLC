import { relationIcon, relationLabel, sortRelations } from '@/lib/relations'
import { cn } from '@/lib/utils'

/**
 * «Telegram» ustuni (CRM upgrade U6b): ulangan chatlarning qarindoshlik ikonkalari (👨 👩 👪 🧑 ❔)
 * + soni; ulanmagan bo'lsa qizil «Ulanmagan»; bloklaganlar kulrang sanoq. Raqam/ism YO'Q — o'qituvchi
 * ko'rinishida ham xavfsiz.
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
  const rels = sortRelations(relations ?? [])
  return (
    <span className={cn('inline-flex flex-wrap items-center gap-1 whitespace-nowrap', className)}>
      {n === 0 ? (
        <span className="rounded-md bg-red-50 px-1.5 py-0.5 text-[11px] font-semibold text-red-600">Ulanmagan</span>
      ) : (
        <span className="inline-flex items-center gap-0.5" title={rels.map(relationLabel).join(', ')}>
          {rels.map((r) => (
            <span key={r} aria-label={relationLabel(r)}>
              {relationIcon(r)}
            </span>
          ))}
          <span className="ml-0.5 text-[12px] font-semibold text-slate-600">{n}</span>
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
