import { Fragment, useState } from 'react'
import { ChevronDown, ChevronRight } from 'lucide-react'
import { adminPermissions } from '@/config/constants'
import {
  PERM_ACTIONS,
  pageRowActions,
  sectionActions,
  sectionRowActions,
  type PermAction,
} from '@/lib/permissions'
import { cn } from '@/lib/utils'

interface Props {
  /** Joriy ruxsat tokenlari to'plami (yalang "bolim", "bolim:amal" yoki "bolim.sahifa[:amal]"). */
  perms: Set<string>
  /** BO'LIM qatoridagi katak bosildi — chaqiruvchi `toggleSectionRow` ni qo'llaydi. */
  onToggleSection: (section: string, action: PermAction) => void
  /** SAHIFA qatoridagi katak bosildi — chaqiruvchi `togglePageRow` ni qo'llaydi. */
  onTogglePage: (section: string, page: string, action: PermAction) => void
}

/**
 * Rol berish matritsasi: har bo'lim (qator) × 4 amal (Ko'rish/Qo'shish/Tahrir/O'chirish).
 * Bo'lim ichida SAHIFALAR bo'lsa — ochib, har birini alohida berish mumkin.
 *
 * ⚠️ BO'LIM qatori — "hammasi": yoqilsa uning barcha sahifalari ochiladi (sahifa katakchalari
 * ham yoqilgan bo'lib ko'rinadi). Bitta sahifani olib tashlash uchun o'sha sahifa katagini
 * bosish yetadi — bo'lim tokeni avtomatik sahifalarga YOYILADI (`togglePageRow`), ya'ni
 * "bo'lim ochiq, bittasidan tashqari" holatini yasash uchun avval bo'limni o'chirish shart emas.
 */
export function PermMatrix({ perms, onToggleSection, onTogglePage }: Props) {
  // Sahifa darajasida ruxsat berilgan bo'lim OCHIQ holda chiziladi — aks holda "nega ko'rsatkich
  // boshqacha" savoli tug'ilardi (ruxsat bor, lekin ko'rinmaydi).
  const [open, setOpen] = useState<Set<string>>(
    () =>
      new Set(
        adminPermissions
          .filter((s) => s.pages?.some((p) => sectionActions(perms, p.key).size > 0))
          .map((s) => s.key),
      ),
  )

  const toggleOpen = (key: string) =>
    setOpen((prev) => {
      const next = new Set(prev)
      if (!next.delete(key)) next.add(key)
      return next
    })

  return (
    <div className="overflow-x-auto rounded-xl border border-slate-200">
      <table className="w-full text-sm">
        <thead>
          <tr className="bg-slate-50 text-xs uppercase tracking-wide text-slate-400">
            <th className="px-3 py-2 text-left font-semibold">Bo'lim / sahifa</th>
            {PERM_ACTIONS.map((a) => (
              <th key={a.key} className="px-2 py-2 text-center font-semibold">
                {a.label}
              </th>
            ))}
          </tr>
        </thead>
        <tbody className="divide-y divide-slate-100">
          {adminPermissions.map((s) => {
            const pages = s.pages ?? []
            const pageKeys = pages.map((p) => p.key)
            const acts = sectionRowActions(perms, s.key, pageKeys)
            const expanded = open.has(s.key)
            return (
              <Fragment key={s.key}>
                <tr className="hover:bg-slate-50/60">
                  <td className="px-3 py-1.5 font-medium text-slate-700">
                    {pages.length > 0 ? (
                      <button
                        type="button"
                        onClick={() => toggleOpen(s.key)}
                        className="flex items-center gap-1 text-left hover:text-brand-600"
                      >
                        {expanded ? (
                          <ChevronDown className="h-4 w-4 shrink-0 text-slate-400" />
                        ) : (
                          <ChevronRight className="h-4 w-4 shrink-0 text-slate-400" />
                        )}
                        {s.label}
                        <span className="ml-1 text-xs font-normal text-slate-400">
                          ({pages.length} sahifa)
                        </span>
                      </button>
                    ) : (
                      <span className="pl-5">{s.label}</span>
                    )}
                  </td>
                  {PERM_ACTIONS.map((a) => (
                    <td key={a.key} className="px-2 py-1.5 text-center">
                      <Box
                        checked={acts.has(a.key)}
                        onChange={() => onToggleSection(s.key, a.key)}
                        title={
                          pages.length > 0
                            ? "Bo'limning barcha sahifalari uchun"
                            : undefined
                        }
                      />
                    </td>
                  ))}
                </tr>
                {expanded &&
                  pages.map((p) => {
                    const pActs = pageRowActions(perms, s.key, p.key)
                    return (
                      <tr key={p.key} className="bg-slate-50/40 hover:bg-slate-50">
                        <td className="py-1 pl-10 pr-3 text-[13px] text-slate-500">{p.label}</td>
                        {PERM_ACTIONS.map((a) => (
                          <td key={a.key} className="px-2 py-1 text-center">
                            <Box
                              checked={pActs.has(a.key)}
                              onChange={() => onTogglePage(s.key, p.key, a.key)}
                              // Bo'limdan meros bo'lsa — sal xiraroq: "bu katak bo'lim qatoridan keldi".
                              dim={acts.has(a.key)}
                            />
                          </td>
                        ))}
                      </tr>
                    )
                  })}
              </Fragment>
            )
          })}
        </tbody>
      </table>
    </div>
  )
}

function Box({
  checked,
  onChange,
  dim,
  title,
}: {
  checked: boolean
  onChange: () => void
  dim?: boolean
  title?: string
}) {
  return (
    <label className="inline-flex cursor-pointer items-center justify-center" title={title}>
      <input
        type="checkbox"
        checked={checked}
        onChange={onChange}
        className={cn(
          'h-4 w-4 cursor-pointer rounded border-slate-300 text-brand-600 focus:ring-brand-400 focus:ring-offset-0',
          dim && 'opacity-60',
        )}
      />
    </label>
  )
}
