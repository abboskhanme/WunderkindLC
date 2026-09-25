/**
 * XODIM ROLLARI — klient yordamchilari (Boshqaruv → Rollar / Xodimlar).
 *
 * ⚠️ Komponent va funksiya bir faylda ARALASHMAYDI (eslint `react-refresh/only-export-components`).
 */

/** O'qituvchi — TIZIM roli (serverda `StaffRoles.TeacherRoleId`). Rol jadvalida qatori yo'q. */
export const TEACHER_ROLE_ID = 'teacher'

/**
 * Rol yaratish/tahrirlash va xodimga rol BERISH mumkinmi — serverdagi `HasFullAccess(User, "staff")`
 * bilan AYNAN bir xil: admin/superadmin yoki "Xodimlar" bo'limi TO'LIQ (yalang `staff` kaliti) berilgan
 * xodim. Rol = ruxsat, ya'ni qisman ruxsatli xodim o'z huquqini oshira olmasin.
 */
export function canManageStaffRoles(
  role: string | undefined,
  permissions: string[] | null | undefined,
): boolean {
  if (role === 'superadmin' || role === 'admin') return true
  return !!permissions?.includes('staff')
}
