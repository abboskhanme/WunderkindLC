import { useEffect, useState } from 'react'
import {
  GraduationCap, BookOpen, Wallet, LogOut, MessageSquare, ChevronRight,
  Lock, Moon, Bell, LifeBuoy,
} from 'lucide-react'
import { Link } from 'react-router-dom'
import type { SalaryLedger, TeacherClass } from '@/types'
import { getMyClasses, getTeacherSalary, getTeacherProfile } from '@/api/services/teacher'
import { getTeacherTheme, setTeacherTheme } from '@/components/layout/TeacherMobileLayout'
import { useAuth } from '@/context/auth-context'
import { Loader } from '@/components/ui/Loader'
import { formatMoney, cn } from '@/lib/utils'
import { usePerm } from '@/lib/permissions'

/** `perm` berilgan bo'lsa — o'qituvchida shu bo'lim ruxsati bo'lmasa qator KO'RSATILMAYDI
 *  (aks holda bosilganda "ruxsatingiz yo'q" chiqadi). */
type MenuItem = { to: string; label: string; sub: string; icon: typeof BookOpen; color: string; perm?: string }

// Support o'qituvchi uchun qo'shimcha bo'lim (faqat IsSupport bo'lsa ko'rsatiladi).
const SUPPORT_MENU: MenuItem = {
  to: '/teacher/support', label: 'Support', sub: "Bo'sh vaqt va bron darslari", icon: LifeBuoy, color: '#0d9488',
}

const MENU: MenuItem[] = [
  { to: '/teacher/salary', label: 'Maosh', sub: 'Oylik hisob va tarix', icon: Wallet, color: '#7c3aed' },
  { to: '/teacher/feedback', label: 'Taklif va shikoyat', sub: 'Adminga xabar yuborish', icon: MessageSquare, color: '#0d9488' },
  { to: '/teacher/account', label: 'Parolni almashtirish', sub: 'Hisob xavfsizligi', icon: Lock, color: '#64748b' },
]

function initialsOf(name: string): string {
  return name
    .split(' ')
    .map((w) => w[0])
    .filter(Boolean)
    .slice(0, 2)
    .join('')
    .toUpperCase()
}

/** O'qituvchi profili — mobil ekran (teal): header karta, ma'lumot qatorlari, guruhlar, chiqish. */
export function TeacherProfilePage() {
  const { user, logout } = useAuth()
  const { can } = usePerm()
  const [classes, setClasses] = useState<TeacherClass[]>([])
  const [salary, setSalary] = useState<SalaryLedger | null>(null)
  const [loading, setLoading] = useState(true)
  const [isSupport, setIsSupport] = useState(false)
  const [dark, setDark] = useState<boolean>(() => getTeacherTheme() === 'dark')
  const [push, setPush] = useState<boolean>(() => localStorage.getItem('teacher_push') !== 'off')

  const toggleDark = () => {
    const next = !dark
    setDark(next)
    setTeacherTheme(next ? 'dark' : 'light')
  }
  const togglePush = () => {
    const next = !push
    setPush(next)
    localStorage.setItem('teacher_push', next ? 'on' : 'off')
  }

  useEffect(() => {
    Promise.all([
      getMyClasses().catch(() => [] as TeacherClass[]),
      getTeacherSalary().catch(() => null),
      getTeacherProfile().catch(() => null),
    ])
      .then(([cl, sal, prof]) => {
        setClasses(cl)
        setSalary(sal)
        setIsSupport(!!prof?.isSupport)
      })
      .finally(() => setLoading(false))
  }, [])

  // Support o'qituvchiga "Support" bo'limi menyu boshida qo'shiladi; `perm` li qatorlar esa
  // faqat shu bo'lim ruxsati bor o'qituvchiga ko'rinadi.
  const menu = (isSupport ? [SUPPORT_MENU, ...MENU] : MENU).filter((i) => !i.perm || can(i.perm, 'view'))

  const subjectCount = classes.reduce((acc, c) => acc + c.subjects.length, 0)

  // Joriy oy maoshi (mavjud bo'lsa) — FAQAT hisoblangan summa ko'rsatiladi (foiz emas)
  const ym = (() => {
    const d = new Date()
    return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}`
  })()
  const currentMonth = salary?.months?.find((m) => m.month === ym) ?? null

  return (
    <div className="px-4 pt-3 pb-6">
      {/* Sarlavha */}
      <p className="mb-3 text-[17px] font-extrabold text-ink">Profil</p>

      {/* Header karta — teal cover + avatar + ma'lumot qatorlari */}
      <div className="overflow-hidden rounded-[20px] border border-line bg-white shadow-[var(--shadow-card)]">
        <div className="h-[90px] bg-gradient-to-br from-teal-500 to-teal-700" />
        <div className="-mt-10 flex flex-col items-center pb-4">
          <div
            className="flex h-20 w-20 shrink-0 items-center justify-center rounded-full bg-gradient-to-br from-teal-500 to-teal-700 text-[30px] font-bold text-white"
            style={{ boxShadow: '0 0 0 2px var(--color-paper), 0 0 0 5px rgba(13,148,136,.5)' }}
          >
            {initialsOf(user?.fullName || 'O')}
          </div>
          <p className="mt-3 text-[18px] font-extrabold text-ink">
            {user?.fullName || "O'qituvchi"}
          </p>
          {user?.email && <p className="text-[12px] text-mute">{user.email}</p>}
          <span className="mt-3 inline-flex items-center gap-1 rounded-lg bg-tealsoft px-2.5 py-1 text-[12px] font-semibold text-teal-700">
            <GraduationCap className="h-3.5 w-3.5" />
            O'qituvchi
          </span>

          {/* Ma'lumot qatorlari */}
          <div className="mt-4 w-full divide-y divide-line px-5">
            <InfoRow
              icon={GraduationCap}
              label="Guruhlar"
              value={classes.length > 0 ? `${classes.length} ta guruh` : '—'}
            />
            <InfoRow
              icon={BookOpen}
              label="Fanlar"
              value={subjectCount > 0 ? String(subjectCount) : '—'}
            />
            <InfoRow
              icon={Wallet}
              label="Maosh"
              value={formatMoney(currentMonth?.expected ?? 0)}
              mono
            />
          </div>
        </div>
      </div>

      {/* Dars beradigan guruhlar */}
      <div className="mt-4">
        <p className="pb-2 pl-1 text-[13px] font-bold tracking-tight text-ink">
          Dars beradigan guruhlar
        </p>
        {loading ? (
          <div className="rounded-[20px] border border-line bg-white p-5 shadow-[var(--shadow-card)]">
            <Loader label="Yuklanmoqda..." />
          </div>
        ) : classes.length === 0 ? (
          <div className="rounded-[20px] border border-line bg-white px-5 py-8 text-center text-[13px] text-faint shadow-[var(--shadow-card)]">
            Sizga biriktirilgan guruh/fan yo'q.
          </div>
        ) : (
          <div className="overflow-hidden rounded-[20px] border border-line bg-white shadow-[var(--shadow-card)]">
            {classes.map((c, i) => (
              <Link
                key={c.classId}
                to={`/teacher/groups/${c.classId}`}
                className={
                  'tap-scale flex items-center gap-3 px-4 py-3.5 text-left' +
                  (i < classes.length - 1 ? ' border-b border-line' : '')
                }
              >
                <div className="flex h-10 w-10 shrink-0 items-center justify-center rounded-[12px] bg-tealsoft text-[15px] font-extrabold text-teal-700">
                  {initialsOf(c.className)}
                </div>
                <div className="min-w-0 flex-1">
                  <p className="truncate text-[14px] font-bold text-ink">{c.className}</p>
                  {c.subjects.length > 0 && (
                    <p className="truncate text-[11px] text-mute">
                      {c.subjects.map((s) => s.name).join(', ')}
                    </p>
                  )}
                </div>
              </Link>
            ))}
          </div>
        )}
      </div>

      {/* Bo'limlar menyusi */}
      <div className="mt-4 overflow-hidden rounded-[20px] border border-line bg-white shadow-[var(--shadow-card)]">
        {menu.map((m, i) => {
          const Icon = m.icon
          return (
            <Link
              key={m.to}
              to={m.to}
              className={cn(
                'tap-scale flex items-center gap-3 px-4 py-3.5 text-left',
                i < menu.length - 1 && 'border-b border-line',
              )}
            >
              <div
                className="flex h-10 w-10 shrink-0 items-center justify-center rounded-[12px]"
                style={{ background: m.color + '22', color: m.color }}
              >
                <Icon className="h-[18px] w-[18px]" />
              </div>
              <div className="min-w-0 flex-1">
                <p className="text-[14px] font-bold text-ink">{m.label}</p>
                <p className="text-[11px] text-mute">{m.sub}</p>
              </div>
              <ChevronRight className="h-5 w-5 shrink-0 text-faint" />
            </Link>
          )
        })}
      </div>

      {/* Sozlamalar */}
      <p className="mb-2 mt-5 pl-1 text-[13px] font-bold tracking-tight text-ink">Sozlamalar</p>
      <div className="overflow-hidden rounded-[20px] border border-line bg-white shadow-[var(--shadow-card)]">
        <ToggleRow
          icon={Moon}
          color="#7c3aed"
          label="Tungi rejim"
          sub={dark ? 'Yoqilgan' : "O'chirilgan"}
          on={dark}
          onToggle={toggleDark}
          border
        />
        <ToggleRow
          icon={Bell}
          color="#0d9488"
          label="Bildirishnoma"
          sub={push ? 'Yoqilgan' : "O'chirilgan"}
          on={push}
          onToggle={togglePush}
        />
      </div>

      {/* Chiqish */}
      <div className="mt-4">
        <button
          type="button"
          onClick={logout}
          className="inline-flex h-12 w-full items-center justify-center gap-2 rounded-xl border border-line bg-transparent text-[15px] font-semibold text-red-500"
        >
          <LogOut className="h-[18px] w-[18px]" />
          Chiqish
        </button>
      </div>
    </div>
  )
}

function InfoRow({
  icon: Icon,
  label,
  value,
  mono,
}: {
  icon: typeof GraduationCap
  label: string
  value: string
  mono?: boolean
}) {
  return (
    <div className="flex items-start gap-3 py-2.5">
      <span className="mt-0.5 text-mute">
        <Icon className="h-[18px] w-[18px]" />
      </span>
      <div className="min-w-0">
        <p className="text-[11px] text-mute">{label}</p>
        <p className={'text-[14px] font-semibold text-ink' + (mono ? ' font-mono' : '')}>
          {value}
        </p>
      </div>
    </div>
  )
}

function ToggleRow({
  icon: Icon,
  color,
  label,
  sub,
  on,
  onToggle,
  border,
}: {
  icon: typeof GraduationCap
  color: string
  label: string
  sub: string
  on: boolean
  onToggle: () => void
  border?: boolean
}) {
  return (
    <button
      type="button"
      onClick={onToggle}
      className={cn('flex w-full items-center gap-3 px-4 py-3.5 text-left', border && 'border-b border-line')}
    >
      <div
        className="flex h-10 w-10 shrink-0 items-center justify-center rounded-[12px]"
        style={{ background: color + '22', color }}
      >
        <Icon className="h-[18px] w-[18px]" />
      </div>
      <div className="min-w-0 flex-1">
        <p className="text-[14px] font-bold text-ink">{label}</p>
        <p className="text-[11px] text-mute">{sub}</p>
      </div>
      {/* Toggle switch */}
      <span
        className={cn(
          'relative h-6 w-11 shrink-0 rounded-full transition-colors',
          on ? 'bg-teal-600' : 'bg-slate-200',
        )}
      >
        <span
          className={cn(
            'absolute top-0.5 h-5 w-5 rounded-full bg-white shadow transition-transform',
            on ? 'translate-x-[22px]' : 'translate-x-0.5',
          )}
        />
      </span>
    </button>
  )
}
