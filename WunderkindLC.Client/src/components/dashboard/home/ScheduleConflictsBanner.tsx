import { useState } from 'react'
import { Link } from 'react-router-dom'
import { IconAlertTriangle, IconChevronDown } from '@tabler/icons-react'
import { getScheduleConflicts } from '@/api/services/schedule'
import { useAsync } from '@/hooks/useAsync'
import { WEEKDAY_SHORT } from '@/lib/scheduleDated'
import { cn } from '@/lib/utils'

/**
 * «Jadvalda {n} ta to'qnashuv bor» — bazada ALLAQACHON to'qnashib turgan guruhlar (ikki faol guruh bir
 * xonada yoki bir o'qituvchida bir vaqtda). Yangi to'qnashuvni server endi saqlamaydi (409), lekin eskisi
 * qolgan bo'lishi mumkin — ro'yxat shu yerda, guruh sahifasiga havola bilan.
 * O'qish xatosi JIM: bu ikkilamchi ogohlantirish, jadvalning o'zi ishlayveradi (error-visibility.md §4).
 */
export function ScheduleConflictsBanner({ canOpenGroup }: { canOpenGroup: boolean }) {
  const { data } = useAsync(() => getScheduleConflicts(), [])
  const [open, setOpen] = useState(false)
  if (!data || data.length === 0) return null

  const name = (g: { id: string; name: string }) =>
    canOpenGroup ? (
      <Link to={`/admin/classes/${g.id}`} className="font-semibold underline-offset-2 hover:underline">
        {g.name}
      </Link>
    ) : (
      <span className="font-semibold">{g.name}</span>
    )

  return (
    <div className="rounded-xl border border-amber-200 bg-amber-50 px-3 py-2 text-[13px] text-amber-800">
      <button type="button" onClick={() => setOpen((v) => !v)} className="flex w-full items-center gap-2 text-left">
        <IconAlertTriangle size={18} stroke={2} className="shrink-0" />
        <span className="flex-1 font-semibold">Jadvalda {data.length} ta to'qnashuv bor</span>
        <IconChevronDown size={16} stroke={2} className={cn('shrink-0 transition-transform', open && 'rotate-180')} />
      </button>
      {open && (
        <ul className="mt-2 space-y-1 pl-6">
          {data.map((c, i) => (
            <li key={`${c.groupA.id}:${c.groupB.id}:${i}`}>
              {name(c.groupA)} ↔ {name(c.groupB)} — {c.kind === 'room' ? 'xona' : "o'qituvchi"},{' '}
              {c.days.map((d) => WEEKDAY_SHORT[d]).join(', ')} {c.slot}
            </li>
          ))}
        </ul>
      )}
    </div>
  )
}
