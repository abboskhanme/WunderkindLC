import { TodayLessonsMonitor } from '@/components/dashboard/TodayLessonsMonitor'

/**
 * Nazorat → «Davomat qilinmagan guruhlar» (edutizimdagi band). Ilgari bu monitor bosh
 * sahifada turardi; bosh sahifa edutizimdagidek "Dars jadvali" bo'lgach, u shu yerga ko'chdi.
 * Ichida kun tanlash va "Davomat qilmagan / qilgan" filtri bor.
 */
export function NotAttendedGroupsPage() {
  return (
    <div className="space-y-3">
      <h1 className="text-xl font-bold text-[#333]">Davomat qilinmagan guruhlar</h1>
      <TodayLessonsMonitor />
    </div>
  )
}
