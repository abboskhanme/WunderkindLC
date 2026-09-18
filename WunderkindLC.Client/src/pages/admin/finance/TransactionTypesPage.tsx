import { useState } from 'react'
import { IconArrowDownLeft, IconArrowUpRight } from '@tabler/icons-react'
import { expenseCategories, incomeCategories } from '@/config/constants'
import { cn } from '@/lib/utils'

/**
 * Moliya → «Tranzaksiya turi» (edutizim `/finance/payment-type`): kirim va chiqim turlari
 * ma'lumotnomasi.
 *
 * ⚠️ TURLAR TAHRIRLANMAYDI (edutizimdan farq — `docs/ASSUMPTIONS.md`): bizda tur kodi mantiqqa
 * bog'langan — `tuition` o'quv to'lovi (maosh foizi, oylik hisobi, kassir hisoboti shunga
 * qaraydi), `refund` vozvrat, `salary` maosh. Ularni foydalanuvchi o'zgartira olsa, hisob-kitob
 * jimgina buzilardi (`.claude/rules/billing.md`). Shuning uchun sahifa turlarni KO'RSATADI:
 * qaysi tur bor va u qaysi yo'nalishda ishlatiladi.
 */
export function TransactionTypesPage() {
  const [side, setSide] = useState<'income' | 'expense' | 'all'>('all')

  const rows = [
    ...(side === 'expense' ? [] : incomeCategories.map((c) => ({ ...c, direction: 'income' as const }))),
    ...(side === 'income' ? [] : expenseCategories.map((c) => ({ ...c, direction: 'expense' as const }))),
  ]

  return (
    <div className="rounded-xl border border-[#dbe0e6] bg-white p-4 shadow-[0_1px_2px_rgba(0,0,0,0.05)]">
      <div className="mb-3 flex flex-wrap items-center justify-between gap-2">
        <h1 className="text-[16px] font-semibold text-black">Tranzaksiya turi</h1>
        <div className="flex items-center gap-2">
          {([
            { key: 'all', label: 'Hammasi', cls: 'bg-[#f0f2f2] text-[#333]' },
            { key: 'income', label: 'Kirim', cls: 'bg-[#e8f8ee] text-[#2e7d32]' },
            { key: 'expense', label: 'Chiqim', cls: 'bg-[#fdeceb] text-[#de4141]' },
          ] as const).map((c) => (
            <button
              key={c.key}
              type="button"
              onClick={() => setSide(c.key)}
              className={cn(
                'rounded-full px-3 py-1 text-[12px] font-semibold transition-opacity',
                c.cls,
                side !== c.key && 'opacity-60 hover:opacity-100',
              )}
            >
              {c.label}
            </button>
          ))}
        </div>
      </div>

      <ul className="space-y-1.5">
        {rows.map((r) => (
          <li
            key={`${r.direction}-${r.value}`}
            className="flex items-center justify-between rounded-lg border border-[#dbe0e6] px-3 py-2.5"
          >
            <span className="flex items-center gap-2 text-[13px] font-medium text-[#333]">
              {r.direction === 'income' ? (
                <IconArrowDownLeft className="h-4 w-4 text-[#2e7d32]" />
              ) : (
                <IconArrowUpRight className="h-4 w-4 text-[#de4141]" />
              )}
              {r.label}
            </span>
            <span
              className={cn(
                'rounded-full px-2 py-0.5 text-[11px] font-semibold',
                r.direction === 'income' ? 'bg-[#e8f8ee] text-[#2e7d32]' : 'bg-[#fdeceb] text-[#de4141]',
              )}
            >
              {r.direction === 'income' ? 'Kirim' : 'Chiqim'}
            </span>
          </li>
        ))}
      </ul>

      <p className="mt-3 text-[12px] text-[#6b7280]">
        Turlar tizimga bog'langan (o'quv to'lovi, vozvrat, maosh) — shuning uchun ular
        o'zgartirilmaydi. Yangi tur kerak bo'lsa, ishlab chiquvchiga murojaat qiling.
      </p>
    </div>
  )
}
