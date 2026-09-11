import { useEffect, useState } from 'react'
import { ChevronDown, Clock } from 'lucide-react'
import type { AuditAction, AuditLog } from '@/types'
import { getAuditLogs, type AuditFilters } from '@/api/services/audit'
import { Loader } from '@/components/ui/Loader'
import { formatDateTime, formatMoney, cn } from '@/lib/utils'

interface Props {
  filters: AuditFilters
  /** Bo'sh bo'lganda ko'rsatiladigan matn */
  emptyLabel?: string
  /**
   * Har qatorda BO'LIM yorlig'ini ko'rsatish (umumiy tarix sahifasi uchun). O'quvchi/guruh
   * sahifasidagi tarixda kerak emas — u yerda bo'lim baribir ma'lum.
   */
  sectionLabels?: Record<string, string>
  /** Yuklab bo'lingach chaqiriladi — sahifa "Ko'proq" tugmasini shu songa qarab ko'rsatadi. */
  onLoaded?: (count: number) => void
}

const actionConfig: Record<AuditAction, { label: string; cls: string }> = {
  create: { label: "Qo'shildi", cls: 'bg-emerald-50 text-emerald-700' },
  update: { label: 'Tahrirlandi', cls: 'bg-amber-50 text-amber-700' },
  delete: { label: "O'chirildi", cls: 'bg-red-50 text-red-700' },
}

const moneyKeys = new Set(['amount', 'salary', 'monthlyFee', 'discountAmount'])
/** Ha/Yo'q ko'rinishida chiziladigan bayroqlar. */
const boolKeys = new Set(['isSupport'])
const fieldLabels: Record<string, string> = {
  amount: 'Summa',
  date: 'Sana',
  category: 'Toifa',
  direction: "Yo'nalish",
  note: 'Izoh',
  month: 'Oy',
  salary: 'Oylik',
  monthlyFee: "Oylik to'lov",
  name: 'Guruh',
  discountPct: 'Chegirma foizi',
  discountAmount: 'Chegirma summasi',
  discountNote: 'Chegirma izohi',
  fullName: 'F.I.SH',
  phone: 'Telefon',
  birthDate: "Tug'ilgan sana",
  gender: 'Jins',
  address: 'Manzil',
  parentFullName: "Ota-ona F.I.SH",
  parentPhone: "Ota-ona telefoni",
  fatherFullName: "Otasi F.I.SH",
  fatherPhone: "Otasi telefoni",
  motherFullName: "Onasi F.I.SH",
  motherPhone: "Onasi telefoni",
  className: 'Guruh',
  enrollmentDate: 'Qabul sanasi',
  grade: 'Daraja',
  language: 'Til',
  room: 'Xona',
  status: 'Holat',
  startTime: 'Boshlanish vaqti',
  endTime: 'Tugash vaqti',
  // O'qituvchi snapshot'i (AuditService.TeacherSnapshot) maydonlari.
  salaryMode: 'Maosh rejimi',
  salaryPercent: 'Maosh foizi',
  bonusPct: 'Ustama',
  salaryStartDate: 'Maosh boshlanish sanasi',
  salaryStartMonth: 'Maosh boshlanish oyi',
  homeroomClass: 'Biriktirilgan guruh',
  isSupport: "Support o'qituvchi",
}
/** Foiz qiymatlari uchun (raqamga "%" qo'shadi). */
const pctKeys = new Set(['discountPct', 'salaryPercent', 'bonusPct'])
const hiddenKeys = new Set(['studentId', 'teacherId'])

function parse(json?: string): Record<string, unknown> | null {
  if (!json) return null
  try {
    return JSON.parse(json) as Record<string, unknown>
  } catch {
    return null
  }
}

function fmtValue(key: string, value: unknown): string {
  if (value === null || value === undefined || value === '') return '—'
  if (moneyKeys.has(key) && typeof value === 'number') return formatMoney(value)
  if (pctKeys.has(key) && typeof value === 'number') return `${value}%`
  if (boolKeys.has(key)) return value ? 'Ha' : "Yo'q"
  if (key === 'salaryMode') return value === 'percent' ? 'Foizli' : "Qat'iy"
  if (key === 'direction') return value === 'income' ? 'Kirim' : 'Chiqim'
  if (key === 'gender') return value === 'female' ? 'Ayol' : 'Erkak'
  if (key === 'language') return value === 'ru' ? 'Rus' : "O'zbek"
  if (key === 'status') {
    const map: Record<string, string> = { active: 'Aktiv', frozen: 'Muzlatilgan', full: "To'lgan", archived: 'Arxiv', trial: 'Sinov' }
    return map[String(value)] ?? String(value)
  }
  return String(value)
}

/** before/after snapshotlarini o'qiladigan tafsilotga aylantiradi */
function SnapshotDetail({ before, after }: { before?: string; after?: string }) {
  const b = parse(before)
  const a = parse(after)
  const keys = [...new Set([...Object.keys(b ?? {}), ...Object.keys(a ?? {})])].filter(
    (k) => !hiddenKeys.has(k) && (k in fieldLabels),
  )
  if (keys.length === 0) return null

  return (
    <dl className="mt-2 space-y-1 rounded-lg bg-slate-50 px-3 py-2 text-xs">
      {keys.map((k) => {
        const ov = b?.[k]
        const nv = a?.[k]
        const changed = b && a && JSON.stringify(ov) !== JSON.stringify(nv)
        return (
          <div key={k} className="flex justify-between gap-3">
            <dt className="text-slate-400">{fieldLabels[k]}</dt>
            <dd className="text-right text-slate-600">
              {b && a ? (
                changed ? (
                  <>
                    <span className="text-slate-400 line-through">{fmtValue(k, ov)}</span>
                    {' → '}
                    <span className="font-medium text-slate-700">{fmtValue(k, nv)}</span>
                  </>
                ) : (
                  fmtValue(k, nv)
                )
              ) : (
                fmtValue(k, a ? nv : ov)
              )}
            </dd>
          </div>
        )
      })}
    </dl>
  )
}

export function AuditHistoryList({
  filters,
  emptyLabel = "O'zgarishlar tarixi yo'q",
  sectionLabels,
  onLoaded,
}: Props) {
  const [logs, setLogs] = useState<AuditLog[]>([])
  const [loading, setLoading] = useState(true)
  const [openId, setOpenId] = useState<string | null>(null)

  const key = JSON.stringify(filters)
  useEffect(() => {
    let active = true
    // eslint-disable-next-line react-hooks/set-state-in-effect -- filtr o'zgarganda qayta yuklash (maqsadli)
    setLoading(true)
    getAuditLogs(filters)
      .then((data) => {
        if (!active) return
        setLogs(data)
        onLoaded?.(data.length)
      })
      .finally(() => {
        if (active) setLoading(false)
      })
    return () => {
      active = false
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps -- filtrlarni stabil JSON kalit orqali kuzatamiz
  }, [key])

  if (loading) return <Loader label="Yuklanmoqda..." />
  if (logs.length === 0)
    return (
      <div className="flex items-center gap-2 py-6 text-sm text-slate-400">
        <Clock className="h-4 w-4" /> {emptyLabel}
      </div>
    )

  return (
    <ul className="space-y-2">
      {logs.map((log) => {
        const cfg = actionConfig[log.action] ?? { label: log.action, cls: 'bg-slate-100 text-slate-600' }
        const hasDetail = !!(log.before || log.after)
        const open = openId === log.id
        return (
          <li key={log.id} className="rounded-lg border border-slate-100 px-3 py-2">
            <div className="flex items-start justify-between gap-3">
              <div className="min-w-0">
                <div className="flex flex-wrap items-center gap-2">
                  <span className={cn('rounded-md px-2 py-0.5 text-xs font-medium', cfg.cls)}>
                    {cfg.label}
                  </span>
                  {sectionLabels && (
                    <span className="rounded-md bg-slate-100 px-2 py-0.5 text-xs font-medium text-slate-500">
                      {sectionLabels[log.section ?? 'other'] ?? log.entityType}
                    </span>
                  )}
                  <span className="text-sm text-slate-700">{log.summary}</span>
                </div>
                <p className="mt-0.5 text-xs text-slate-400">
                  {formatDateTime(log.timestamp)} · {log.actorName || 'Tizim'}
                </p>
              </div>
              {hasDetail && (
                <button
                  type="button"
                  onClick={() => setOpenId(open ? null : log.id)}
                  className="shrink-0 rounded-md p-1 text-slate-400 transition-colors hover:bg-slate-100 hover:text-slate-600"
                  title="Tafsilot"
                >
                  <ChevronDown className={cn('h-4 w-4 transition-transform', open && 'rotate-180')} />
                </button>
              )}
            </div>
            {open && hasDetail && <SnapshotDetail before={log.before} after={log.after} />}
          </li>
        )
      })}
    </ul>
  )
}
