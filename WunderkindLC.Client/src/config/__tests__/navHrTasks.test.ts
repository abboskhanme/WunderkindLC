import { readFileSync } from 'node:fs'
import { fileURLToPath } from 'node:url'
import { describe, expect, it } from 'vitest'
import { activeNavTo, navByRole } from '../navigation'
import { TEACHER_NAV, navMatches } from '@/lib/teacherPortal'

/**
 * HR TOPSHIRIQLARI menyusi (docs/SPEC-hr-tasks.md) — LMS'dagi «HR → TOPSHIRIQLAR» bilan AYNAN bir xil bandlar.
 * LC'da «HR» bo'limi yo'q, shuning uchun o'z yuqori guruhi «Boshqaruv» dan keyin; har xodimning o'z
 * topshiriqlari — alohida yuqori band (admin/xodim), o'qituvchi portalida va kassa portalida ham.
 */
const admin = navByRole.admin
const app = readFileSync(fileURLToPath(new URL('../../App.tsx', import.meta.url)), 'utf8')

describe('HR topshiriqlari — admin menyusi', () => {
  it("«Topshiriqlar» guruhi «Boshqaruv» dan KEYIN turadi", () => {
    const labels = admin.map((i) => i.label)
    expect(labels.indexOf('Topshiriqlar')).toBe(labels.indexOf('Boshqaruv') + 1)
  })

  it('bandlar LMS tartibida, hammasi `hrTasks` kaliti bilan', () => {
    const group = admin.find((i) => i.label === 'Topshiriqlar')!
    expect(group.to).toBe('#topshiriqlar')
    expect(group.children!.map((c) => [c.label, c.to, c.perm])).toEqual([
      ['Boshqaruv paneli', '/admin/hr/topshiriqlar', 'hrTasks'],
      ['Topshiriqlar', '/admin/hr/topshiriqlar/royxat', 'hrTasks'],
      ['Takroriy topshiriqlar', '/admin/hr/topshiriqlar/takroriy', 'hrTasks'],
      ['Xodimlar', '/admin/hr/topshiriqlar/xodimlar', 'hrTasks'],
      ['Hisobotlar', '/admin/hr/topshiriqlar/hisobotlar', 'hrTasks'],
      ['Sozlamalar', '/admin/hr/topshiriqlar/sozlamalar', 'hrTasks'],
    ])
    // Boshqaruv paneli — faqat aynan o'z manzilida faol (ichki sahifalarda emas).
    expect(group.children![0].end).toBe(true)
  })

  it('«Mening topshiriqlarim» — kalitsiz yuqori band, staff/admin/superadmin uchun', () => {
    const mine = admin.find((i) => i.label === 'Mening topshiriqlarim')!
    expect(mine).toMatchObject({ to: '/admin/hr/mening-topshiriqlarim' })
    expect(mine.perm).toBeUndefined()
    expect(mine.children).toBeUndefined()
    expect([...(mine.roles ?? [])].sort()).toEqual(['admin', 'staff', 'superadmin'])
  })

  it('HR sahifalarida «Topshiriqlar» guruhi faol, o\'z topshiriqlarida — o\'z bandi', () => {
    for (const p of [
      '/admin/hr/topshiriqlar',
      '/admin/hr/topshiriqlar/royxat',
      '/admin/hr/topshiriqlar/royxat/yangi',
      '/admin/hr/topshiriqlar/xodimlar',
      '/admin/hr/topshiriqlar/sozlamalar',
    ])
      expect(activeNavTo(admin, p), p).toBe('#topshiriqlar')
    expect(activeNavTo(admin, '/admin/hr/mening-topshiriqlarim')).toBe('/admin/hr/mening-topshiriqlarim')
  })

  it('har bir panel sahifasi App.tsx da `RequirePerm perm="hrTasks"` bilan', () => {
    for (const path of ['hr/topshiriqlar', 'hr/topshiriqlar/royxat', 'hr/topshiriqlar/royxat/yangi', 'hr/topshiriqlar/takroriy',
      'hr/topshiriqlar/xodimlar', 'hr/topshiriqlar/hisobotlar', 'hr/topshiriqlar/sozlamalar'])
      expect(app).toMatch(new RegExp(`path="${path}" element={<RequirePerm perm="hrTasks">`))
    // O'z topshiriqlari — kalitsiz (HR qatori yo'q xodimga server 403 `hr_not_employee` beradi).
    expect(app).toContain('path="hr/mening-topshiriqlarim" element={<HrMyTasksPage />}')
  })
})

describe('HR topshiriqlari — o\'qituvchi va kassa portallari', () => {
  it('o\'qituvchi portali menyusida «Mening topshiriqlarim» (ruxsatsiz, Support emas)', () => {
    const item = TEACHER_NAV.find((i) => i.to === '/teacher/topshiriqlar')!
    expect(item).toMatchObject({ label: 'Mening topshiriqlarim' })
    expect(item.perm).toBeUndefined()
    expect(item.supportOnly).toBeUndefined()
    expect(navMatches(item, '/teacher/topshiriqlar')).toBe(true)
    expect(navByRole.teacher.some((i) => i.to === '/teacher/topshiriqlar')).toBe(true)
  })

  it('o\'qituvchi va kassa marshrutlari kun ko\'rinishidan boshlanadi (Telegram ichida)', () => {
    expect(app.match(/path="topshiriqlar" element={<HrMyTasksPage defaultTab="day" \/>}/g)?.length).toBe(2)
  })
})
