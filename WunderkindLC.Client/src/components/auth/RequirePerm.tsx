import type { ReactNode } from 'react'
import { useAuth } from '@/context/auth-context'
import { can } from '@/lib/permissions'
import { Card } from '@/components/ui/Card'

/**
 * Bo'lim sahifasi uchun ruxsat darvozasi. Foydalanuvchida shu bo'limni KO'RISH ruxsati bo'lmasa —
 * "ruxsat yo'q" ko'rsatadi. permissions umuman bo'lmasa (admin) — har doim ochiq. "Ko'rish" — bare
 * "section" yoki biror "section:action" (create/edit/delete ruxsati ham ko'rishga yetadi).
 */
export function RequirePerm({ perm, children }: { perm: string; children: ReactNode }) {
  const { user } = useAuth()
  const ok = can(user?.permissions, perm, 'view')
  if (!ok) {
    return (
      <Card>
        <p className="py-12 text-center text-slate-400">Bu bo'limga ruxsatingiz yo'q.</p>
      </Card>
    )
  }
  return <>{children}</>
}
