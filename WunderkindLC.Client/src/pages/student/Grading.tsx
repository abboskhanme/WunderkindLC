import { GradingPanel } from '@/pages/student/GradingPanel'
import { PageHeader } from '@/pages/student/ui'

/* ============================================================
   BAHOLASH MEZONLARI — points per criterion (monthly + per lesson) for every active group.
   The content is the shared GradingPanel (Statistika shows the same block).
   ============================================================ */

export function StudentGradingScreen() {
  return (
    <div className="screen">
      <PageHeader title="Baholash mezonlari" back="/student/statistics" />
      <div className="scroll">
        <div className="st-page" style={{ paddingTop: 8 }}>
          <GradingPanel />
        </div>
      </div>
    </div>
  )
}
