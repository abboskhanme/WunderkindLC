import { useEffect, useState } from 'react'
import { getTeacherPayments, type GroupPaymentsReport } from '@/api/services/finance'
import { GroupPaymentsModal } from '@/pages/admin/finance/GroupPaymentsModal'
import { Card } from '@/components/ui/Card'
import { Button } from '@/components/ui/Button'
import { formatMonth } from '@/config/constants'
import { currentMonth, todayIso } from '@/lib/month'
import { apiErrorMessage, formatMoney } from '@/lib/utils'

/**
 * O'qituvchi profili → «Guruhlar»: JORIY OY bo'yicha uning guruhlaridagi o'quvchilar soni, qanchasi to'lagan va qancha
 * qarz (TZ 1.5, SPEC-backlog-2026-10-17). Ma'lumot mavjud endpointdan (`finance/teacher-payments`) — Moliya → «Guruhlar
 * bo'yicha to'lov» dagi AYNAN o'sha hisob; «Ro'yxat» — o'sha oyna (kim to'ladi, kim to'lamadi).
 */
export function TeacherPaymentsSummary({ teacherId, teacherName }: { teacherId: string; teacherName: string }) {
  const month = currentMonth()
  const from = `${month}-01`
  const to = todayIso()
  const [data, setData] = useState<GroupPaymentsReport | null>(null)
  const [error, setError] = useState('')
  const [open, setOpen] = useState(false)

  useEffect(() => {
    let active = true
    getTeacherPayments(teacherId, from, to)
      .then((d) => { if (active) { setData(d); setError('') } })
      .catch((e) => { if (active) setError(apiErrorMessage(e, "To'lov holatini yuklab bo'lmadi")) })
    return () => { active = false }
  }, [teacherId, from, to])

  const debt = data ? data.rows.reduce((s, r) => s + Math.max(0, r.debt), 0) : 0
  return (
    <Card
      title={`O'quvchilar to'lovi — ${formatMonth(month)}`}
      sub="Shu o'qituvchining guruhlaridagi o'quvchilar: kim to'lagan, kim qarzdor"
      actions={data && data.studentCount > 0 ? <Button variant="secondary" onClick={() => setOpen(true)}>Ro'yxat</Button> : undefined}
    >
      {error ? (
        <p className="text-sm text-red-600">{error}</p>
      ) : !data ? (
        <p className="text-sm text-slate-400">Yuklanmoqda...</p>
      ) : (
        <div className="grid grid-cols-2 gap-3 sm:grid-cols-4">
          {[
            ["O'quvchilar", String(data.studentCount), 'text-slate-800'],
            ["To'laganlar", String(data.paidCount), 'text-sky-700'],
            ["To'lamaganlar", String(data.unpaidCount), 'text-amber-700'],
            ['Qarz', formatMoney(debt), 'text-rose-600'],
          ].map(([label, value, cls]) => (
            <div key={label} className="rounded-lg border border-slate-200 p-3">
              <div className="text-xs text-slate-500">{label}</div>
              <div className={`mt-0.5 font-semibold tabular-nums ${cls}`}>{value}</div>
            </div>
          ))}
        </div>
      )}
      <GroupPaymentsModal
        target={open ? { kind: 'teacher', id: teacherId, name: teacherName } : null}
        from={from}
        to={to}
        onClose={() => setOpen(false)}
      />
    </Card>
  )
}
