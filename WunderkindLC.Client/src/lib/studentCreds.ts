/**
 * O'QUVCHI LOGIN/PAROLI — klient yordamchilari (CRM Phase 6, `.claude/rules/auth-sessions.md` §6).
 *
 * ⚠️ Komponent va funksiya bir faylda ARALASHMAYDI (eslint `react-refresh/only-export-components`).
 */

/**
 * Login/parolni ko'rish mumkinmi — serverdagi `AdminPermAttribute.HasFullAccess(User, "students.list")` ning
 * AYNAN nusxasi: admin/superadmin yoki YALANG `students` / `students.list` kaliti (barcha amallar) berilgan xodim.
 * `students.list:edit` kabi qisman kalit YETMAYDI. O'qituvchi hech qachon ko'rmaydi (SPEC §10 #5).
 */
export function canStudentCreds(role: string | undefined, permissions: string[] | null | undefined): boolean {
  if (role === 'superadmin' || role === 'admin') return true
  if (role !== 'staff') return false
  return !!permissions?.some((p) => p === 'students.list' || p === 'students')
}

/** Parol qayerdan kelgani — profil kartasidagi kichik izoh uchun. */
export function passwordSourceLabel(source: string | null | undefined): string {
  switch (source) {
    case 'create':
      return 'akkaunt yaratilganda berilgan'
    case 'reset':
      return 'qayta yaratilgan'
    case 'admin_set':
      return "admin o'rnatgan"
    case 'self_change':
      return "o'quvchi o'zi almashtirgan"
    case 'backfill':
    case 'initial':
      return 'dastlabki parol'
    default:
      return ''
  }
}

/** Parolni yashirish uchun — uzunligi bilan bir xil nuqtalar (kamida 6). */
export function maskPassword(password: string): string {
  return '•'.repeat(Math.max(6, password.length))
}
