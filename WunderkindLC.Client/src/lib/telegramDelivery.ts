/**
 * TELEGRAM XABAR HOLATI — outbox qatori (`MessageDelivery`) foydalanuvchiga qanday ko'rinadi
 * (CRM upgrade P2.8): ✓ «Yuborildi», ✓✓ «Qabul qildi», ⛔ «Bloklagan», ✗ «Xato», ⏳ «Navbatda».
 * Server holatlari: pending · sending · sent · failed · blocked · unknown · expired · cancelled.
 */

export type DeliveryView = 'acked' | 'sent' | 'blocked' | 'failed' | 'pending' | 'cancelled'

/** «Qabul qildi» — bitta holat: tugma bosilgan (botlar o'qilganini bilmaydi). */
export function deliveryView(status: string, ackAt?: string | null): DeliveryView {
  if (ackAt) return 'acked'
  switch (status) {
    case 'sent':
      return 'sent'
    case 'blocked':
      return 'blocked'
    case 'pending':
    case 'sending':
      return 'pending'
    case 'cancelled':
      return 'cancelled'
    default:
      return 'failed' // failed · unknown · expired
  }
}

export const DELIVERY_META: Record<DeliveryView, { icon: string; label: string; cls: string }> = {
  acked: { icon: '✓✓', label: 'Qabul qildi', cls: 'text-emerald-700 bg-emerald-50' },
  sent: { icon: '✓', label: 'Yuborildi', cls: 'text-sky-700 bg-sky-50' },
  blocked: { icon: '⛔', label: 'Bloklagan', cls: 'text-red-700 bg-red-50' },
  failed: { icon: '✗', label: 'Xato', cls: 'text-red-700 bg-red-50' },
  pending: { icon: '⏳', label: 'Navbatda', cls: 'text-amber-700 bg-amber-50' },
  cancelled: { icon: '—', label: 'Bekor qilindi', cls: 'text-slate-500 bg-slate-100' },
}

/** Hisobot status filtri chiplari (server `status` parametri bilan bir xil kalitlar). */
export const REPORT_FILTERS: { key: '' | 'sent' | 'acked' | 'failed' | 'blocked' | 'pending' | 'cancelled'; label: string }[] = [
  { key: '', label: 'Hammasi' },
  { key: 'sent', label: 'Yuborildi' },
  { key: 'acked', label: 'Qabul qildi' },
  { key: 'pending', label: 'Navbatda' },
  { key: 'blocked', label: 'Bloklagan' },
  { key: 'failed', label: 'Xato' },
  { key: 'cancelled', label: 'Bekor qilindi' },
]

/** Kampaniya turi yorlig'i. */
export function campaignKindLabel(kind: string): string {
  if (kind === 'archive') return 'Arxiv'
  if (kind === 'debt') return 'Qarz eslatmasi'
  return "E'lon"
}

/** Kampaniya holati yorlig'i (`queued` · `sending` · `done` · `cancelled`). */
export function campaignStatusLabel(status: string): string {
  switch (status) {
    case 'queued':
      return 'Navbatda'
    case 'sending':
      return 'Yuborilmoqda'
    case 'cancelled':
      return 'Bekor qilindi'
    default:
      return 'Tugadi'
  }
}

/** Hisobot yangilanib turishi kerakmi (3 s polling): kampaniya hali tugamagan. */
export const isCampaignOpen = (status: string): boolean => status === 'queued' || status === 'sending'

/** Fayl hajmi: "2.4 MB" / "830 KB". */
export function formatBytes(n: number): string {
  if (n >= 1024 * 1024) return `${(n / (1024 * 1024)).toFixed(1)} MB`
  if (n >= 1024) return `${Math.round(n / 1024)} KB`
  return `${n} B`
}
