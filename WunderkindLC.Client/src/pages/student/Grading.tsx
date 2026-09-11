import { useNavigate } from 'react-router-dom'
import { Icon } from '@/pages/student/lib'
import { GradingPanel } from '@/pages/student/GradingPanel'

/* ============================================================
   O'quvchi portali — BAHOLASH statistikasi (oylik + har darslik).
   Har faol guruh bo'yicha: mezonlarda oylik xulosa + har darsdagi belgilar.
   (Mazmun umumiy GradingPanel'da — Statistika bo'limi ham shuni ishlatadi.)
   ============================================================ */

export function StudentGradingScreen() {
  const nav = useNavigate()
  return (
    <div className="screen">
      <div className="hd">
        <div className="row gap10" style={{ minHeight: 38 }}>
          <button className="iconbtn press" onClick={() => nav(-1)}>
            <Icon name="chevL" size={22} />
          </button>
          <div className="hd-sm" style={{ flex: 1 }}>Baholash</div>
        </div>
      </div>

      <div className="scroll" style={{ paddingBottom: 28 }}>
        <div className="pad">
          <GradingPanel />
        </div>
      </div>
    </div>
  )
}
