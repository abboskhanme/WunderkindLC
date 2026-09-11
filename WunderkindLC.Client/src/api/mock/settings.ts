import type { SchoolSettings } from '@/types'

export const settingsMock: SchoolSettings = {
  quarters: [
    { quarter: 1, startDate: '2025-09-02', endDate: '2025-10-26', gradesOpen: true },
    { quarter: 2, startDate: '2025-11-03', endDate: '2025-12-28', gradesOpen: true },
    { quarter: 3, startDate: '2026-01-12', endDate: '2026-03-22', gradesOpen: true },
    { quarter: 4, startDate: '2026-04-01', endDate: '2026-05-25', gradesOpen: false },
  ],
  absenceReasons: [
    { id: 'ar1', name: 'Kasal', short: 'K', isLate: false },
    { id: 'ar2', name: 'Sababli', short: 'S', isLate: false },
    { id: 'ar3', name: 'Sababsiz', short: 'SS', isLate: false },
    { id: 'ar4', name: 'Kech keldi', short: 'Kch', isLate: true },
  ],
}
