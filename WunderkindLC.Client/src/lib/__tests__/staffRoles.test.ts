import { describe, expect, it } from 'vitest'
import { canManageStaffRoles } from '../staffRoles'

// Serverdagi `AdminPermAttribute.HasFullAccess(User, "staff")` bilan AYNAN bir xil bo'lishi shart:
// UI tugmani ko'rsatib, server 403 qaytarsa — "tugma ishlamayapti" bo'lib ko'rinardi.
describe('canManageStaffRoles — rol yaratish/berish kimga ochiq', () => {
  it.each([
    ['superadmin', null, true],
    ['admin', null, true],
    ['staff', ['staff'], true],
    ['staff', ['staff:view'], false],
    ['staff', ['staff:create', 'staff:edit'], false],
    ['staff', ['finance'], false],
    ['staff', null, false],
    ['teacher', ['staff'], true],
  ] as const)('%s + %j → %s', (role, perms, expected) => {
    expect(canManageStaffRoles(role, perms ? [...perms] : null)).toBe(expected)
  })
})
