import type { InputHTMLAttributes, ReactNode, SelectHTMLAttributes } from 'react'
import { Link, useLocation } from 'react-router-dom'
import { IconArrowLeft, IconChevronDown } from '@tabler/icons-react'
import { cn } from '@/lib/utils'
import { readBackState } from '@/lib/nav'

/**
 * O'QUVCHI PROFILI — umumiy KO'RINISH bo'laklari (edutizim tokenlari: karta r12 `#DBE0E6`,
 * tab "pill" 32px r8 `#F0F2F2`, bo'lim sarlavhasi chapda rangli chiziq + sanoq belgisi).
 * Sof mantiq `model.ts` da — bu faylda faqat komponentlar.
 */

/** edutizim bo'lim sarlavhasi: chapda 3px rangli chiziq, nom, yonida och-binafsha sanoq belgisi. */
export function SectionBar({
  title,
  count,
  tone = 'purple',
  action,
}: {
  title: ReactNode
  count?: number
  tone?: 'purple' | 'primary'
  action?: ReactNode
}) {
  return (
    <div className="mb-3 flex flex-wrap items-center gap-2">
      <span className={cn('h-5 w-[3px] rounded-full', tone === 'purple' ? 'bg-[#9747ff]' : 'bg-brand-600')} />
      <h3 className="text-[16px] font-semibold text-black">{title}</h3>
      {count != null && (
        <span className="inline-flex min-w-6 items-center justify-center rounded-full bg-[#efe7ff] px-2 py-0.5 text-[12px] font-semibold text-[#7a3ff0]">
          {count}
        </span>
      )}
      {action && <div className="ml-auto flex flex-wrap items-center gap-2">{action}</div>}
    </div>
  )
}

/** Tab ichidagi oq karta (r12) — ixtiyoriy sarlavha bilan. */
export function ProfileSection({
  title,
  count,
  tone,
  action,
  children,
  className,
}: {
  title?: ReactNode
  count?: number
  tone?: 'purple' | 'primary'
  action?: ReactNode
  children: ReactNode
  className?: string
}) {
  return (
    <section
      className={cn(
        'rounded-xl border border-[#dbe0e6] bg-white p-4 shadow-[0_1px_2px_rgba(0,0,0,0.05)]',
        className,
      )}
    >
      {title != null && <SectionBar title={title} count={count} tone={tone ?? 'primary'} action={action} />}
      {children}
    </section>
  )
}

export const Empty = ({ children }: { children: ReactNode }) => (
  <p className="py-8 text-center text-sm text-slate-400">{children}</p>
)

/**
 * edutizim TAB "pill"lari: 32px, radius 8, kulrang `#F0F2F2` fon, tanlangani — asosiy rang +
 * oq matn. Bir necha qatorga o'raladi (tablar ko'p).
 */
export function TabPills<K extends string>({
  tabs,
  active,
  onChange,
}: {
  tabs: { key: K; label: string }[]
  active: K
  onChange: (key: K) => void
}) {
  return (
    <div
      role="tablist"
      className="flex flex-wrap gap-1.5 rounded-xl border border-[#dbe0e6] bg-white p-2 shadow-[0_1px_2px_rgba(0,0,0,0.05)]"
    >
      {tabs.map((t) => (
        <button
          key={t.key}
          type="button"
          role="tab"
          aria-selected={t.key === active}
          onClick={() => onChange(t.key)}
          className={cn(
            'inline-flex h-8 items-center whitespace-nowrap rounded-lg px-3 text-[13px] font-medium transition-colors',
            t.key === active ? 'bg-brand-600 text-white' : 'bg-[#f0f2f2] text-[#333] hover:bg-[#e6eaea]',
          )}
        >
          {t.label}
        </button>
      ))}
    </div>
  )
}

/**
 * "Orqaga" havolasi — profil QAYERDAN ochilganiga qarab. Guruh sahifasidan kirilgan bo'lsa
 * (`Link state` da `BackState` keladi) o'sha GURUHGA qaytaradi, aks holda o'quvchilar ro'yxatiga.
 */
export function BackLink() {
  const { backTo, backLabel } = readBackState(useLocation().state, {
    backTo: '/admin/students',
    backLabel: "O'quvchilar ro'yxati",
  })
  return (
    <Link
      to={backTo}
      className="inline-flex items-center gap-1.5 text-[13px] font-medium text-[#6b7280] hover:text-brand-600"
    >
      <IconArrowLeft className="h-4 w-4" /> {backLabel}
    </Link>
  )
}

/* ─────────── edutizim FORMA maydonlari (to'ldirilgan kulrang, 36px) ─────────── */

const fieldCls =
  'h-9 w-full rounded-lg border border-[#dbe0e6] bg-[#f0f2f2] px-3 text-[14px] text-black outline-none transition-colors placeholder:text-[#9e9e9e] focus:border-brand-600 focus:bg-white disabled:cursor-not-allowed disabled:text-black/70'

/** Yorliq TEPADA (15px / 500), maydon ostida. */
export function FormField({ label, required, children }: { label: string; required?: boolean; children: ReactNode }) {
  return (
    <label className="block min-w-0">
      <span className="mb-1.5 block text-[15px] font-medium text-black">
        {label}
        {required && <span className="text-[#e34a29]">*</span>}
      </span>
      {children}
    </label>
  )
}

export function TextField({ className, ...rest }: InputHTMLAttributes<HTMLInputElement>) {
  return <input className={cn(fieldCls, className)} {...rest} />
}

export function SelectField({ className, children, ...rest }: SelectHTMLAttributes<HTMLSelectElement>) {
  return (
    <div className="relative">
      <select className={cn(fieldCls, 'appearance-none pr-8', className)} {...rest}>
        {children}
      </select>
      <IconChevronDown className="pointer-events-none absolute right-2.5 top-1/2 h-4 w-4 -translate-y-1/2 text-[#6b7280]" />
    </div>
  )
}

/**
 * Mavjud `PhoneInput` (maska + 998 qoidasi) edutizim to'ldirilgan ko'rinishida: ichki `<input>`
 * avlod selektori bilan qayta bo'yaladi — maska mantig'i nusxalanmaydi.
 */
export function FilledPhoneWrap({ children }: { children: ReactNode }) {
  return (
    <div className="[&_input]:h-9 [&_input]:border-[#dbe0e6] [&_input]:bg-[#f0f2f2] [&_input]:py-0 [&_input]:text-[14px] [&_input]:text-black [&_input:disabled]:cursor-not-allowed [&_input:focus]:bg-white">
      {children}
    </div>
  )
}

/** edutizim forma tugmalari: to'la ko'k (Saqlash), ramkali (Orqaga), qizil `#E34A29` (O'chirish). */
export function FormButton({
  tone = 'primary',
  className,
  ...rest
}: React.ButtonHTMLAttributes<HTMLButtonElement> & { tone?: 'primary' | 'outline' | 'danger' }) {
  return (
    <button
      type="button"
      className={cn(
        'inline-flex min-h-[36px] items-center justify-center gap-1.5 rounded-lg px-4 text-[13px] font-medium transition-colors disabled:cursor-not-allowed disabled:opacity-60',
        tone === 'primary' && 'bg-brand-600 text-white hover:bg-brand-700',
        tone === 'outline' && 'border border-brand-600/50 bg-white text-brand-600 hover:bg-brand-50',
        tone === 'danger' && 'bg-[#e34a29] text-white hover:bg-[#cc3f20]',
        className,
      )}
      {...rest}
    />
  )
}
