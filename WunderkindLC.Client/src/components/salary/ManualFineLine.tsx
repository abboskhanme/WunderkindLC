import { formatMoney } from '@/lib/utils'

/**
 * «Jarima: −X so'm» — o'qituvchiga QO'LDA qo'yilgan jarimalar (Moliya → Jarima, 2026-10-13), shu oy maoshidan
 * ayrilgan. Sabablari ham ko'rinadi (o'qituvchi profili, o'qituvchi kabineti, `TeacherSalaryDetailModal`).
 */
export function ManualFineLine({
  amount,
  items,
  className,
}: {
  amount: number
  items?: { amount: number; reason: string }[] | null
  className?: string
}) {
  if (amount <= 0) return null
  return (
    <div className={className}>
      <div className="rounded-lg bg-red-50 px-3 py-2.5 text-xs text-red-800">
        <p className="font-semibold">Jarima: −{formatMoney(amount)}</p>
        {items && items.length > 0 && (
          <ul className="mt-1 space-y-0.5">
            {items.map((f, i) => (
              <li key={i} className="flex justify-between gap-3">
                <span className="min-w-0">{f.reason}</span>
                <span className="shrink-0 font-mono">−{formatMoney(f.amount)}</span>
              </li>
            ))}
          </ul>
        )}
      </div>
    </div>
  )
}
