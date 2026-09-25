import { adminPermissions, permPagesOf } from '@/config/constants'
import { sectionActions, sectionRowActions } from '@/lib/permissions'
import { Badge } from '@/components/ui/Badge'

/**
 * Ruxsatlar XULOSASI (chiplar): bo'lim to'liq ochiq bo'lsa BITTA chip (sahifalari uning ichida),
 * aks holda ochiq sahifalar alohida chip bo'lib chiqadi — "nima berilgan" aniq ko'rinsin.
 */
export function PermChips({ permissions, empty = 'Ruxsatlar belgilanmagan' }: { permissions: string[]; empty?: string }) {
  if (permissions.length === 0) return <span className="text-xs text-slate-400">{empty}</span>
  const own = new Set(permissions)
  return (
    <div className="flex flex-wrap gap-1.5">
      {adminPermissions.flatMap((p) => {
        const secActs = sectionRowActions(own, p.key, permPagesOf(p.key))
        if (secActs.size > 0)
          return [
            <Badge key={p.key} tone="violet">
              {p.label}
              {secActs.size !== 4 && <span className="ml-1 opacity-70">({secActs.size} amal)</span>}
            </Badge>,
          ]
        return (p.pages ?? [])
          .filter((pg) => sectionActions(own, pg.key).size > 0)
          .map((pg) => {
            const acts = sectionActions(own, pg.key)
            return (
              <Badge key={pg.key} tone="violet">
                {p.label} → {pg.label}
                {acts.size !== 4 && <span className="ml-1 opacity-70">({acts.size} amal)</span>}
              </Badge>
            )
          })
      })}
    </div>
  )
}
