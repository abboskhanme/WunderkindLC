import type { ReactNode } from 'react'
import { Link } from 'react-router-dom'
import { ChevronLeft, ChevronRight, ExternalLink, TriangleAlert } from 'lucide-react'
import { usePerm } from '@/lib/permissions'
import { kpiTabs } from '@/config/sectionTabs'
import { formatMonth } from '@/config/constants'
import { shiftMonth } from '@/lib/month'
import { CardTabs } from '@/components/ui/CardTabs'
import { PageHeader } from '@/components/ui/PageHeader'
import { Badge } from '@/components/ui/Badge'
import { Select } from '@/components/ui/Input'
import { cn, formatMoney } from '@/lib/utils'
import type { KpiCoefDto, KpiInputDto, KpiMoneyLineDto, KpiProfileDto } from '@/api/services/kpi'
import { coefTone, formatCoef, formatPercent, roleLabel } from './model'

/* =====================================================================================
 *  KPI bo'limining UMUMIY KO'RINISH bo'laklari
 * =====================================================================================
 *  Sarlavha, sahifalar qatori, xodim/oy tanlagichlari va hisob qatorlari beshala
 *  sahifada AYNAN bir xil ko'rinishi kerak. Mantiq (yorliq, format, pog'ona) `model.ts` da.
 */

/**
 * Bo'limning UMUMIY qobig'i: sarlavha + sahifalar qatori (`CardTabs`). Har sahifa faqat
 * o'z mazmunini yozadi.
 */
export function KpiShell({
  title = 'KPI',
  sub,
  actions,
  children,
}: {
  title?: ReactNode
  sub?: ReactNode
  actions?: ReactNode
  children: ReactNode
}) {
  const { can } = usePerm()
  return (
    <div>
      <PageHeader title={title} sub={sub} actions={actions} />
      <CardTabs items={kpiTabs((perm) => can(perm, 'view'))} className="mb-5" />
      {children}
    </div>
  )
}

/**
 * XODIM tanlagich — KPI profili bor xodimlar.
 *
 * ⚠️ Bo'sh qiymat ("Men") ATAYIN qoldirilgan: server `userId` bo'sh bo'lsa joriy
 * foydalanuvchini oladi, ya'ni har KPI xodimi o'z raqamini bo'lim ruxsatisiz ko'radi.
 */
export function StaffPicker({
  profiles,
  value,
  onChange,
  label,
  includeAll,
  className,
}: {
  profiles: KpiProfileDto[]
  value: string
  onChange: (userId: string) => void
  label?: string
  /** "Barcha xodimlar" varianti (tiketlar filtri uchun). */
  includeAll?: boolean
  className?: string
}) {
  return (
    <Select
      label={label}
      value={value}
      onChange={(e) => onChange(e.target.value)}
      className={cn('w-auto', className)}
    >
      {includeAll ? <option value="">Barcha xodimlar</option> : <option value="">Men</option>}
      {profiles.map((p) => (
        <option key={p.userId} value={p.userId}>
          {p.userName} — {p.roleLabel || roleLabel(p.roleCode)}
        </option>
      ))}
    </Select>
  )
}

/**
 * OY tanlagich: `‹ May 2026 ›`. Strelkalar bilan qo'shni oyga o'tiladi.
 *
 * ⚠️ Kelajakka cheklov ATAYIN yo'q: rejalashtirish uchun keyingi oyni ochish kerak
 * bo'ladi (masalan oklad versiyasi "kelasi oydan"), va bo'sh oy baribir bo'sh ko'rinadi.
 */
export function MonthPicker({
  month,
  onChange,
  label,
}: {
  month: string
  onChange: (month: string) => void
  label?: string
}) {
  return (
    <div className="inline-flex flex-col">
      {label && <span className="mb-1 text-sm font-medium text-slate-600">{label}</span>}
      <div className="inline-flex items-center gap-1 rounded-lg border border-slate-200 bg-white px-1 py-1">
        <button
          type="button"
          aria-label="Oldingi oy"
          onClick={() => onChange(shiftMonth(month, -1))}
          className="flex h-7 w-7 items-center justify-center rounded-md text-slate-400 transition-colors hover:bg-slate-100 hover:text-slate-600"
        >
          <ChevronLeft className="h-4 w-4" />
        </button>
        <span className="min-w-[6.5rem] text-center text-[13px] font-semibold text-slate-700">
          {formatMonth(month)}
        </span>
        <button
          type="button"
          aria-label="Keyingi oy"
          onClick={() => onChange(shiftMonth(month, 1))}
          className="flex h-7 w-7 items-center justify-center rounded-md text-slate-400 transition-colors hover:bg-slate-100 hover:text-slate-600"
        >
          <ChevronRight className="h-4 w-4" />
        </button>
      </div>
    </div>
  )
}

/**
 * KIRISH raqamining bitta qatori: qiymat + "qayerdan olindi" izohi va MAVJUD sahifaga havola.
 *
 * ⚠️ Havola shu sababdan MAJBURIY emas, lekin bo'lsa doim ko'rsatiladi: KPI hech narsa
 * yaratmaydi, ya'ni raqamga e'tiroz bo'lsa javob HAR DOIM boshqa bo'limda ("lidlar
 * noto'g'ri" → Lidlar sahifasi). Havolasiz raqam "qayerdan chiqdi" savolini javobsiz
 * qoldirardi.
 *
 * ⚠️ `estimated` — oy boshi snapshoti yo'q, qiymat jonli hisobdan TIKLANGAN. Bu jimgina
 * o'tkazilmaydi: oy boshidagi holat orqaga qarab aniq tiklanmaydi, ya'ni raqam TAXMINIY.
 */
export function InputRow({ input }: { input: KpiInputDto }) {
  return (
    <div className="flex items-start justify-between gap-4 px-[18px] py-3">
      <div className="min-w-0">
        <div className="flex flex-wrap items-center gap-2">
          <span className="text-sm font-semibold text-slate-800">{input.label}</span>
          {input.auto && (
            <Badge tone="blue">avtomatik</Badge>
          )}
          {input.estimated && (
            <Badge tone="amber">
              <TriangleAlert className="h-3 w-3" />
              taxminiy
            </Badge>
          )}
        </div>
        <p className="mt-0.5 text-[11.5px] text-slate-400">{input.source}</p>
        {input.estimated && (
          <p className="mt-0.5 text-[11.5px] font-medium text-amber-600">
            Oy boshi snapshoti yo'q — qiymat hozirgi ma'lumotdan tiklandi.
          </p>
        )}
      </div>
      <div className="flex shrink-0 items-center gap-2">
        <span className="font-mono text-sm font-semibold text-slate-800">
          {formatValue(input.value)}
        </span>
        {input.link && (
          <Link
            to={input.link}
            title="Manba sahifasini ochish"
            className="flex h-7 w-7 items-center justify-center rounded-md border border-slate-200 text-slate-400 transition-colors hover:bg-slate-50 hover:text-slate-600"
          >
            <ExternalLink className="h-3.5 w-3.5" />
          </Link>
        )}
      </div>
    </div>
  )
}

/** Butun son butun, kasr — bitta xonagacha (konversiya 0.3286 → "0,33"). */
function formatValue(value: number): string {
  if (!Number.isFinite(value)) return '—'
  if (Number.isInteger(value)) return new Intl.NumberFormat('ru-RU').format(value)
  return String(Number(value.toFixed(2))).replace('.', ',')
}

/**
 * KOEFFITSIENT qatori: xom qiymat → qaysi pog'onaga tushdi → koeffitsient → pog'ona IZOHI.
 *
 * ⚠️ Izoh ("NORMA. Bonus to'liq.") ko'rsatilishi SHART: koeffitsientning o'zi (`×0,8`)
 * "nega bunday" savoliga javob bermaydi va xodim raqamni jazo deb qabul qilardi.
 */
export function CoefRow({ coef }: { coef: KpiCoefDto }) {
  return (
    <div className="flex items-start justify-between gap-4 px-[18px] py-3">
      <div className="min-w-0">
        <p className="text-sm font-semibold text-slate-800">{coef.label}</p>
        <p className="mt-0.5 text-[11.5px] text-slate-400">{coef.note || 'Pog‘ona izohi yo‘q'}</p>
      </div>
      <div className="flex shrink-0 items-center gap-3">
        <span className="font-mono text-[12.5px] text-slate-500">{formatPercent(coef.value)}</span>
        <Badge tone={coefTone(coef.coef)}>{formatCoef(coef.coef)}</Badge>
      </div>
    </div>
  )
}

/**
 * PUL qatori (oklad · bonus A/B/C · jarima · jami).
 *
 * `strong` — yakuniy qator (kattaroq shrift), `negative` — jarima (qizil, minus bilan).
 */
export function MoneyRow({
  line,
  strong,
  negative,
}: {
  line: KpiMoneyLineDto
  strong?: boolean
  negative?: boolean
}) {
  return (
    <div
      className={cn(
        'flex items-start justify-between gap-4 px-[18px] py-3',
        strong && 'bg-slate-50',
      )}
    >
      <div className="min-w-0">
        <p
          className={cn(
            'font-semibold text-slate-800',
            strong ? 'text-[15px]' : 'text-sm',
          )}
        >
          {line.label}
        </p>
        {line.note && <p className="mt-0.5 text-[11.5px] text-slate-400">{line.note}</p>}
      </div>
      <span
        className={cn(
          'shrink-0 font-mono font-semibold tabular-nums',
          strong ? 'text-[17px] text-slate-900' : 'text-sm',
          negative ? 'text-red-600' : !strong && 'text-slate-700',
        )}
      >
        {negative && line.amount > 0 ? '−' : ''}
        {formatMoney(Math.abs(line.amount))}
      </span>
    </div>
  )
}

/**
 * Yuklashda yiqilganda ko'rsatiladigan INLINE banner.
 *
 * ⚠️ Yuklash xatosi `alert()` bilan berilmaydi: u bir marta chiqib yo'qoladi va sahifa
 * bo'sh qolgani "ma'lumot yo'q" bo'lib ko'rinardi. Amal xatosi esa aksincha — `alert`.
 */
export function ErrorBanner({ text, onRetry }: { text: string; onRetry?: () => void }) {
  return (
    <div className="mb-4 flex flex-wrap items-center justify-between gap-3 rounded-xl border border-red-200 bg-red-50 px-4 py-3">
      <div className="flex items-start gap-2 text-sm text-red-700">
        <TriangleAlert className="mt-0.5 h-4 w-4 shrink-0" />
        <span>{text}</span>
      </div>
      {onRetry && (
        <button
          type="button"
          onClick={onRetry}
          className="rounded-lg border border-red-300 bg-white px-3 py-1.5 text-[13px] font-semibold text-red-700 transition-colors hover:bg-red-100"
        >
          Qayta urinish
        </button>
      )}
    </div>
  )
}

/** Bo'sh ro'yxat holati — nima yo'qligi va NIMA QILISH kerakligi bilan. */
export function EmptyState({ title, hint }: { title: string; hint?: ReactNode }) {
  return (
    <div className="flex flex-col items-center gap-2 py-12 text-center">
      <p className="text-sm font-semibold text-slate-500">{title}</p>
      {hint && <p className="max-w-md text-[12.5px] text-slate-400">{hint}</p>}
    </div>
  )
}
