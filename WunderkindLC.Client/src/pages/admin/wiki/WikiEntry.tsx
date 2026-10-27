import { Navigate, useParams } from 'react-router-dom'

/**
 * `/admin/bilimlar` — KIRISH NUQTASI: har doim «Eslatmalarim» (u har bir xodimga ruxsatsiz ochiq — egasining talabi,
 * 2026-10-27). «Xodimlar» tabi u yerdan, faqat rahbariyat/menejerga.
 */
export function WikiEntry() {
  return <Navigate to="/admin/bilimlar/eslatmalar" replace />
}

/** Oraliq manzillar (`/admin/bilimlar/eslatmalar/xodim/...`) — «Xodimlar» tabiga. */
export function LegacyTeamNotesRedirect() {
  const { userId, pageId } = useParams<{ userId?: string; pageId?: string }>()
  const to = userId ? `/admin/bilimlar/xodimlar/${userId}${pageId ? `/${pageId}` : ''}` : '/admin/bilimlar/xodimlar'
  return <Navigate to={to} replace />
}
