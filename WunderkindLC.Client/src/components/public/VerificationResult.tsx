import { CheckCircle2, XCircle, AlertTriangle, Award, User, BookOpen, Calendar, Clock } from 'lucide-react'

export interface CertificateVerificationDto {
  isValid: boolean
  studentName: string
  courseName: string
  issuedAt: string
  expiresAt?: string | null
  status: string
  hashMatched: boolean
  metadata?: string | null
  errorMessage?: string | null
}

interface Props {
  isValid: boolean
  data: CertificateVerificationDto | null
  error: string | null
}

function parseMetadata(raw?: string | null): Record<string, string> {
  if (!raw) return {}
  try {
    return JSON.parse(raw) as Record<string, string>
  } catch {
    return {}
  }
}

function formatDate(iso: string): string {
  if (!iso) return ''
  const m = iso.match(/^(\d{4})-(\d{2})-(\d{2})/)
  if (!m) return iso
  return `${m[3]}.${m[2]}.${m[1]}`
}

function StatusBadge({ status }: { status: string }) {
  if (status === 'active') {
    return (
      <span className="inline-flex items-center gap-1.5 rounded-full bg-emerald-100 px-3 py-1 text-xs font-semibold text-emerald-700">
        <span className="h-1.5 w-1.5 rounded-full bg-emerald-500" />
        Amal qiluvchi
      </span>
    )
  }
  if (status === 'expired') {
    return (
      <span className="inline-flex items-center gap-1.5 rounded-full bg-amber-100 px-3 py-1 text-xs font-semibold text-amber-700">
        <span className="h-1.5 w-1.5 rounded-full bg-amber-500" />
        Muddati o&apos;tgan
      </span>
    )
  }
  if (status === 'revoked') {
    return (
      <span className="inline-flex items-center gap-1.5 rounded-full bg-red-100 px-3 py-1 text-xs font-semibold text-red-700">
        <span className="h-1.5 w-1.5 rounded-full bg-red-500" />
        Bekor qilingan
      </span>
    )
  }
  return (
    <span className="inline-flex items-center gap-1.5 rounded-full bg-slate-100 px-3 py-1 text-xs font-semibold text-slate-600">
      {status}
    </span>
  )
}

export function VerificationResult({ isValid, data, error }: Props) {
  // --- Error state (network / not found) ---
  if (error || !data) {
    const msg = error || 'Sertifikat tekshirib bo\'lmadi'
    const notFound =
      msg.toLowerCase().includes('topilmadi') ||
      msg.toLowerCase().includes('not found') ||
      msg.includes('404')
    return (
      <div className="flex flex-col items-center gap-4 py-8 text-center">
        <div className="flex h-20 w-20 items-center justify-center rounded-full bg-red-50 ring-8 ring-red-50/50">
          <XCircle className="h-10 w-10 text-red-500" />
        </div>
        <div>
          <h2 className="text-xl font-bold text-slate-800">
            {notFound ? 'Sertifikat topilmadi' : 'Tekshirib bo\'lmadi'}
          </h2>
          <p className="mt-1 max-w-sm text-sm text-slate-500">
            {notFound
              ? 'Bunday identifikatorli sertifikat mavjud emas yoki o\'chirilgan.'
              : msg}
          </p>
        </div>
      </div>
    )
  }

  const meta = parseMetadata(data.metadata)
  const progressPercent = meta.progressPercent ? Number(meta.progressPercent) : null
  const averageScore = meta.averageScore ? Number(meta.averageScore) : null
  const darsCount = meta.darsCount ? Number(meta.darsCount) : null
  const completionNotes = meta.completionNotes || null
  const isExpired = data.status === 'expired'
  const isRevoked = data.status === 'revoked'
  const hashWarning = data.hashMatched === false && data.status !== 'not_found'

  // --- Invalid certificate ---
  if (!isValid) {
    let title = 'Sertifikat yaroqsiz'
    let desc = data.errorMessage || 'Sertifikat tekshiruvdan o\'tmadi.'
    let icon = <XCircle className="h-10 w-10 text-red-500" />
    let ring = 'ring-red-50/50'
    let bg = 'bg-red-50'

    if (hashWarning) {
      title = 'Sertifikat o\'zgartirilgan'
      desc = 'Sertifikat fayli asl nusxadan farq qiladi. Hujjat buzuq yoki soxta bo\'lishi mumkin.'
      icon = <AlertTriangle className="h-10 w-10 text-amber-500" />
      ring = 'ring-amber-50/50'
      bg = 'bg-amber-50'
    } else if (isExpired) {
      title = 'Muddat tugagan'
      desc = `Bu sertifikatning amal qilish muddati tugagan${data.expiresAt ? ` (${formatDate(data.expiresAt)})` : ''}.`
      icon = <AlertTriangle className="h-10 w-10 text-amber-500" />
      ring = 'ring-amber-50/50'
      bg = 'bg-amber-50'
    } else if (isRevoked) {
      title = 'Sertifikat bekor qilingan'
      desc = 'Bu sertifikat chiqarilgan tashkilot tomonidan bekor qilingan.'
    }

    return (
      <div className="flex flex-col items-center gap-4 py-8 text-center">
        <div className={`flex h-20 w-20 items-center justify-center rounded-full ${bg} ring-8 ${ring}`}>
          {icon}
        </div>
        <div>
          <h2 className="text-xl font-bold text-slate-800">{title}</h2>
          <p className="mt-1 max-w-sm text-sm text-slate-500">{desc}</p>
        </div>
        {/* Show basic info if available */}
        {data.studentName && (
          <div className="mt-2 w-full rounded-2xl border border-slate-100 bg-slate-50 p-5 text-left">
            <p className="mb-3 text-xs font-semibold uppercase tracking-wider text-slate-400">
              Sertifikat ma&apos;lumotlari
            </p>
            <DetailRow icon={<User className="h-4 w-4" />} label="O'quvchi" value={data.studentName} />
            {data.courseName && (
              <DetailRow icon={<BookOpen className="h-4 w-4" />} label="Kurs" value={data.courseName} />
            )}
            {data.issuedAt && (
              <DetailRow icon={<Calendar className="h-4 w-4" />} label="Berilgan" value={formatDate(data.issuedAt)} />
            )}
            {data.expiresAt && (
              <DetailRow
                icon={<Clock className="h-4 w-4" />}
                label="Muddati"
                value={formatDate(data.expiresAt)}
                valueClass={isExpired ? 'text-amber-600 font-semibold' : undefined}
              />
            )}
            <div className="mt-3 flex items-center gap-2">
              <StatusBadge status={data.status} />
            </div>
          </div>
        )}
      </div>
    )
  }

  // --- Valid certificate ---
  return (
    <div className="flex flex-col items-center gap-6">
      {/* Check icon */}
      <div className="flex h-20 w-20 items-center justify-center rounded-full bg-emerald-50 ring-8 ring-emerald-50/60">
        <CheckCircle2 className="h-10 w-10 text-emerald-500" />
      </div>

      {/* Title */}
      <div className="text-center">
        <h2 className="text-2xl font-bold text-slate-800">Sertifikat haqiqiy</h2>
        <p className="mt-1 text-sm text-slate-500">
          Bu sertifikat to&apos;liq tekshiruvdan o&apos;tdi va asl nusxa ekanligi tasdiqlandi.
        </p>
      </div>

      {/* Main info card */}
      <div className="w-full overflow-hidden rounded-2xl border border-emerald-100 bg-gradient-to-br from-emerald-50 to-white">
        {/* Header stripe */}
        <div className="flex items-center gap-3 bg-gradient-to-r from-emerald-600 to-emerald-500 px-5 py-4">
          <div className="flex h-10 w-10 shrink-0 items-center justify-center rounded-xl bg-white/20">
            <Award className="h-5 w-5 text-white" />
          </div>
          <div className="min-w-0">
            <p className="truncate text-base font-bold text-white">{data.courseName}</p>
            <p className="text-xs text-emerald-100">Kurs sertifikati</p>
          </div>
          <div className="ml-auto shrink-0">
            <StatusBadge status={data.status} />
          </div>
        </div>

        {/* Details */}
        <div className="divide-y divide-slate-100 px-5">
          <DetailRow
            icon={<User className="h-4 w-4" />}
            label="O'quvchi"
            value={data.studentName}
            valueClass="font-semibold text-slate-800"
          />
          <DetailRow
            icon={<BookOpen className="h-4 w-4" />}
            label="Kurs"
            value={data.courseName}
          />
          <DetailRow
            icon={<Calendar className="h-4 w-4" />}
            label="Berilgan sana"
            value={formatDate(data.issuedAt)}
          />
          {data.expiresAt && (
            <DetailRow
              icon={<Clock className="h-4 w-4" />}
              label="Amal qilish muddati"
              value={formatDate(data.expiresAt)}
            />
          )}
        </div>
      </div>

      {/* Metadata stats */}
      {(progressPercent !== null || averageScore !== null || darsCount !== null) && (
        <div className="w-full">
          <p className="mb-3 text-xs font-semibold uppercase tracking-wider text-slate-400">
            O&apos;quv natijalari
          </p>
          <div className="grid grid-cols-3 gap-3">
            {progressPercent !== null && (
              <StatCard
                label="Kurs progresi"
                value={`${Math.round(progressPercent)}%`}
                color="emerald"
              />
            )}
            {averageScore !== null && (
              <StatCard
                label="O'rtacha ball"
                value={averageScore.toFixed(1)}
                color="blue"
              />
            )}
            {darsCount !== null && (
              <StatCard
                label="O'tilgan darslar"
                value={String(darsCount)}
                color="violet"
              />
            )}
          </div>
        </div>
      )}

      {/* Completion notes */}
      {completionNotes && (
        <div className="w-full rounded-xl border border-slate-100 bg-slate-50 p-4">
          <p className="mb-1 text-xs font-semibold uppercase tracking-wider text-slate-400">
            Izoh
          </p>
          <p className="text-sm leading-relaxed text-slate-700">{completionNotes}</p>
        </div>
      )}

      {/* Hash verified badge */}
      <div className="flex items-center gap-2 rounded-full border border-emerald-200 bg-emerald-50 px-4 py-2 text-xs font-medium text-emerald-700">
        <CheckCircle2 className="h-3.5 w-3.5" />
        SHA-256 hash tasdiqlangan — hujjat o&apos;zgartirilmagan
      </div>
    </div>
  )
}

// ─── Sub-components ──────────────────────────────────────────────────────────

function DetailRow({
  icon,
  label,
  value,
  valueClass,
}: {
  icon: React.ReactNode
  label: string
  value: string
  valueClass?: string
}) {
  return (
    <div className="flex items-center gap-3 py-3">
      <span className="shrink-0 text-slate-400">{icon}</span>
      <span className="w-32 shrink-0 text-xs text-slate-500">{label}</span>
      <span className={`flex-1 text-sm text-slate-700 ${valueClass ?? ''}`}>{value}</span>
    </div>
  )
}

function StatCard({
  label,
  value,
  color,
}: {
  label: string
  value: string
  color: 'emerald' | 'blue' | 'violet'
}) {
  const colors = {
    emerald: { bg: 'bg-emerald-50', text: 'text-emerald-700', sub: 'text-emerald-500' },
    blue: { bg: 'bg-blue-50', text: 'text-blue-700', sub: 'text-blue-500' },
    violet: { bg: 'bg-violet-50', text: 'text-violet-700', sub: 'text-violet-500' },
  }
  const c = colors[color]
  return (
    <div className={`rounded-xl ${c.bg} p-3 text-center`}>
      <p className={`font-mono text-xl font-bold ${c.text}`}>{value}</p>
      <p className={`mt-0.5 text-xs ${c.sub} leading-tight`}>{label}</p>
    </div>
  )
}
