/** edutizim: jadval tepasida o'ngda "Umumiy soni: N" (oq, ramkali, radius 8, 13px / 500). */
export function TotalPill({ total, label = 'Umumiy soni:' }: { total: number; label?: string }) {
  return (
    <div className="mb-2 flex justify-end">
      <div className="rounded-lg border border-[#dbe0e6] bg-white px-3 py-1.5 text-[13px] font-medium text-[#333] shadow-[0_1px_2px_rgba(0,0,0,0.04)]">
        <span className="text-[12px] font-normal text-black/60">{label}</span>{' '}
        <b className="font-bold">{total}</b>
      </div>
    </div>
  )
}
