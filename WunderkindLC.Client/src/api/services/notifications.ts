import { api } from '../client'

/** Foydalanuvchi bildirishnomasi (ilova tarixi) — Topbar qo'ng'irog'i uchun. */
export interface AppNotification {
  id: string
  title: string
  body: string
  type: string
  createdAt: string
  read: boolean
  confirmed: boolean
}

export interface NotificationsResponse {
  unread: number
  items: AppNotification[]
}

/** Joriy foydalanuvchining bildirishnomalari (oxirgi 100). */
export async function getNotifications(): Promise<NotificationsResponse> {
  const { data } = await api.get<NotificationsResponse>('/admin/notifications')
  return data
}

/** Barcha o'qilmagan bildirishnomalarni o'qilgan deb belgilaydi. */
export async function markNotificationsRead(): Promise<void> {
  await api.post('/admin/notifications/read')
}
