import { useEffect, useState } from 'react'
import { Link } from 'react-router-dom'
import { IconCashRegister } from '@tabler/icons-react'
import { getCashboxes, type Cashbox } from '@/api/services/cashboxes'
import { apiErrorMessage, balanceTextCls, cn, formatMoney } from '@/lib/utils'

/**
 * «KASSALAR BALANSI» — SUPERADMIN monitoringi (yig'ilish: "SuperAdmin balansi monitoringi").
 * Bosh sahifada ixcham qator: har FAOL kassaning qoldig'i + jami (arxivdagi kassalardagi pul ham
 * jamiga kiradi — u jismonan bor). Batafsil — Moliya → «Kassalar» (`CashboxesPanel`), shu yerga havola.
 *
 * Chaqiruvchi faqat superadmin uchun chizadi (`AdminDashboard`); server ham `finance.main`
 * (ReadRequiresPerm) bilan darvozalaydi. Bu bosh sahifaning ASOSIY ma'lumoti emas — xato bo'lsa
 * qatorning o'zida qisqa matn chiqadi, sahifa buzilmaydi.
 */
export function CashboxBalanceStrip() {
  const [boxes, setBoxes] = useState<Cashbox[] | null>(null)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    let alive = true
    getCashboxes()
      .then((b) => alive && setBoxes(b))
      .catch((e) => alive && setError(apiErrorMessage(e, "Kassalar qoldig'ini yuklab bo'lmadi")))
    return () => {
      alive = false
    }
  }, [])

  if (error) return <p className="text-[12px] text-red-600">{error}</p>
  if (!boxes || boxes.length === 0) return null

  const active = boxes.filter((b) => !b.isArchived)
  const total = boxes.reduce((a, b) => a + b.balance, 0)
  const archivedTotal = boxes.filter((b) => b.isArchived).reduce((a, b) => a + b.balance, 0)

  return (
    <div className="flex flex-wrap items-stretch gap-2.5 rounded-xl border border-[#DBE0E6] bg-white px-3 py-2 shadow-[0_1px_2px_rgba(0,0,0,0.05)]">
      <Link to="/admin/finance?tab=cashiers" className="flex min-w-0 items-center gap-2 pr-2 hover:opacity-80">
        <span className="flex h-9 w-9 shrink-0 items-center justify-center rounded-lg bg-[#019EF7] text-white">
          <IconCashRegister size={22} stroke={2} />
        </span>
        <span className="flex flex-col">
          <span className="text-[12px] text-black">Kassalar balansi</span>
          <span className={cn('text-[16px] font-semibold leading-tight', balanceTextCls(total))}>{formatMoney(total)}</span>
        </span>
      </Link>
      {active.map((b) => (
        <div key={b.id} className="flex min-w-0 flex-col justify-center border-l border-[#eef0f3] pl-2.5">
          <span className="max-w-[160px] truncate text-[12px] text-[#6B7280]" title={b.responsibleName ?? undefined}>
            {b.name}
          </span>
          <span className={cn('font-mono text-[13px] font-semibold', balanceTextCls(b.balance))}>{formatMoney(b.balance)}</span>
        </div>
      ))}
      {archivedTotal !== 0 && (
        <div className="flex flex-col justify-center border-l border-[#eef0f3] pl-2.5">
          <span className="text-[12px] text-[#6B7280]">Arxivdagi kassalar</span>
          <span className={cn('font-mono text-[13px] font-semibold', balanceTextCls(archivedTotal))}>
            {formatMoney(archivedTotal)}
          </span>
        </div>
      )}
    </div>
  )
}
