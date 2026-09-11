import { useCallback, useEffect, useMemo, useState } from 'react'
import { AlertTriangle, Check, ImageOff, Loader2, ScanFace, Search, X } from 'lucide-react'
import type { FaceCheck, FaceCheckStatus } from '@/api/services/face'
import {
  approveFaceCheck, FACE_MAX_LIMIT, getFaceChecks, rejectFaceCheck,
} from '@/api/services/face'
import { Badge } from '@/components/ui/Badge'
import { Button } from '@/components/ui/Button'
import { Card } from '@/components/ui/Card'
import { Input, Textarea } from '@/components/ui/Input'
import { Loader } from '@/components/ui/Loader'
import { Modal } from '@/components/ui/Modal'
import { TablePagination, usePagination } from '@/components/ui/TablePagination'
import { apiErrorMessage, cn, formatDateTime } from '@/lib/utils'
import {
  FACE_APPROVE_HINT,
  parseQuality,
  platformLabel,
  qualityMetrics,
  scorePercent,
  statusLabel,
  statusTone,
  type FaceQualityMetric,
} from './faceLabels'
import { SelfieThumb } from './SelfieThumb'
import { useFaceImage } from './useFaceImage'

interface Props {
  /** Tasdiqlash/rad etish tugmalari ko'rinadimi (`students:edit`). */
  canDecide: boolean
  /** Qaror qabul qilingach — tab yorlig'idagi kutilayotganlar sonini yangilash. */
  onDecided: () => void
}

const statusTabs: { value: FaceCheckStatus | ''; label: string }[] = [
  { value: 'pending', label: 'Kutilmoqda' },
  { value: 'approved', label: 'Tasdiqlangan' },
  { value: 'rejected', label: 'Rad etilgan' },
  { value: '', label: 'Hammasi' },
]

/** Qaror oynasi: tasdiqlash yoki rad etish. */
type Decision = { check: FaceCheck; action: 'approve' | 'reject' }

/**
 * URINISHLAR — o'quvchi ilovasidan kelgan selfi tekshiruvlari jurnali.
 *
 * Odatda qaror AVTOMATIK chiqadi (selfi etalon yoki profil rasmi bilan solishtiriladi), lekin
 * o'quvchining na etaloni, na profil rasmi bo'lmasa urinish `pending` bo'lib shu yerga tushadi —
 * admin selfi kimga tegishli ekanini ko'zi bilan ko'rib tasdiqlaydi.
 */
export function FaceChecksTab({ canDecide, onDecided }: Props) {
  const [rows, setRows] = useState<FaceCheck[]>([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState('')
  const [busyId, setBusyId] = useState<string | null>(null)

  const [status, setStatus] = useState<FaceCheckStatus | ''>('pending')
  const [from, setFrom] = useState('')
  const [to, setTo] = useState('')
  /** O'quvchi bo'yicha qidiruv — ro'yxat ustida, MIJOZ tomonida (API'da matn filtri yo'q). */
  const [query, setQuery] = useState('')

  const [detail, setDetail] = useState<FaceCheck | null>(null)
  const [decision, setDecision] = useState<Decision | null>(null)
  const [note, setNote] = useState('')

  const load = useCallback(() => {
    setLoading(true)
    setError('')
    // Server default 100 ta beradi — sahifalash mijozda bo'lgani uchun to'liq chegarani
    // (500) so'raymiz, aks holda "Hammasi" filtri jimgina kesilib qolardi.
    getFaceChecks({ status, from, to, limit: FACE_MAX_LIMIT })
      .then(setRows)
      .catch((err) => setError(apiErrorMessage(err, "Urinishlarni yuklab bo'lmadi")))
      .finally(() => setLoading(false))
  }, [status, from, to])

  useEffect(load, [load])

  const filtered = useMemo(() => {
    const term = query.trim().toLowerCase()
    if (!term) return rows
    return rows.filter((r) => r.studentName.toLowerCase().includes(term))
  }, [rows, query])

  const pg = usePagination(filtered)

  const confirmDecision = async () => {
    if (!decision || busyId) return
    setBusyId(decision.check.id)
    setError('')
    try {
      const text = note.trim()
      if (decision.action === 'approve') await approveFaceCheck(decision.check.id, text || undefined)
      else await rejectFaceCheck(decision.check.id, text || undefined)
      setDecision(null)
      setNote('')
      setDetail(null)
      load()
      onDecided()
    } catch (err) {
      setError(apiErrorMessage(err, "Amalni bajarib bo'lmadi"))
    } finally {
      setBusyId(null)
    }
  }

  const openDecision = (check: FaceCheck, action: Decision['action']) => {
    setDecision({ check, action })
    setNote('')
  }

  return (
    <div className="space-y-4">
      {/* ---- Filtrlar ---- */}
      <Card tight>
        <div className="flex flex-wrap items-end gap-3 p-4">
          <div className="tabs">
            {statusTabs.map((s) => (
              <button
                key={s.value || 'all'}
                type="button"
                onClick={() => setStatus(s.value)}
                className={cn('tab', status === s.value && 'active')}
              >
                {s.label}
              </button>
            ))}
          </div>

          <Input
            label="Sanadan"
            type="date"
            className="w-auto"
            value={from}
            onChange={(e) => setFrom(e.target.value)}
          />
          <Input
            label="Sanagacha"
            type="date"
            className="w-auto"
            value={to}
            onChange={(e) => setTo(e.target.value)}
          />
          <div className="relative">
            <Input
              label="O'quvchi"
              placeholder="F.I.Sh. bo'yicha..."
              value={query}
              onChange={(e) => setQuery(e.target.value)}
            />
            <Search className="pointer-events-none absolute right-3 top-[34px] h-4 w-4 text-slate-300" />
          </div>
        </div>
      </Card>

      {error && (
        <div className="flex items-center gap-2 rounded-lg bg-red-50 px-4 py-3 text-sm text-red-700">
          <AlertTriangle className="h-4 w-4 shrink-0" /> {error}
        </div>
      )}

      {/* Kutilayotganlar bo'lsa — qaror nimani anglatishini ochiq yozamiz. */}
      {filtered.some((r) => r.status === 'pending') && (
        <div className="flex items-start gap-2 rounded-lg bg-amber-50 px-4 py-3 text-sm text-amber-800">
          <AlertTriangle className="mt-0.5 h-4 w-4 shrink-0" />
          <span>
            <b>Kutilayotgan urinish</b> — o'quvchining na yuz etaloni, na profil rasmi bo'lgani
            uchun tizim o'zi qaror qila olmadi. {FACE_APPROVE_HINT} Shuning uchun selfi AYNAN shu
            o'quvchiniki ekaniga ishonch hosil qiling.
          </span>
        </div>
      )}

      {/* ---- Ro'yxat ---- */}
      {loading ? (
        <Card>
          <Loader label="Yuklanmoqda..." />
        </Card>
      ) : filtered.length === 0 ? (
        <Card>
          <div className="state">
            <div className="state-icon">
              <ScanFace className="h-5 w-5" />
            </div>
            <h4>Urinish yo'q</h4>
            <p>
              Bu filtr bo'yicha kirish urinishi topilmadi. Urinish o'quvchi ilovaga YANGI
              qurilmadan kirganda paydo bo'ladi.
            </p>
          </div>
        </Card>
      ) : (
        <Card tight>
          <div className="overflow-x-auto">
            <table className="table">
              <thead>
                <tr>
                  <th>Sana</th>
                  <th>O'quvchi</th>
                  <th>Selfi</th>
                  <th className="text-right">Ball</th>
                  <th>Holat</th>
                  <th>Sabab</th>
                  <th>Sifat</th>
                  <th>Qurilma</th>
                  <th>IP</th>
                  <th />
                </tr>
              </thead>
              <tbody>
                {pg.paged.map((c) => (
                  <tr key={c.id}>
                    <td className="whitespace-nowrap text-slate-500">{formatDateTime(c.createdAt)}</td>
                    <td className="font-medium text-slate-800">{c.studentName}</td>
                    <td>
                      <SelfieThumb
                        url={c.imageUrl}
                        alt={`${c.studentName} — selfi`}
                        onClick={() => setDetail(c)}
                      />
                    </td>
                    <td className="text-right font-mono text-slate-700">{scorePercent(c.score)}</td>
                    <td>
                      <Badge tone={statusTone(c.status)}>{statusLabel(c.status)}</Badge>
                    </td>
                    <td className="max-w-[200px] text-xs text-slate-500">{c.reason || '—'}</td>
                    <td>
                      <QualityChips metrics={qualityMetrics(parseQuality(c.quality))} compact />
                    </td>
                    <td className="text-xs text-slate-500">
                      <div className="font-medium text-slate-700">{c.deviceName || 'Nomsiz qurilma'}</div>
                      <div>
                        {platformLabel(c.platform)}
                        {c.appVersion && <span className="ml-1 text-slate-400">v{c.appVersion}</span>}
                      </div>
                    </td>
                    <td className="whitespace-nowrap font-mono text-xs text-slate-400">{c.ip || '—'}</td>
                    <td className="whitespace-nowrap text-right">
                      {canDecide && c.status === 'pending' && c.canApprove && (
                        <div className="inline-flex max-w-[230px] flex-col items-end gap-1">
                          <div className="inline-flex gap-1.5">
                            <Button
                              variant="secondary"
                              disabled={busyId === c.id}
                              onClick={() => openDecision(c, 'approve')}
                              className="!bg-emerald-50 !text-emerald-700 hover:!bg-emerald-100"
                            >
                              <Check className="h-4 w-4" /> Tasdiqlash
                            </Button>
                            <Button
                              variant="secondary"
                              disabled={busyId === c.id}
                              onClick={() => openDecision(c, 'reject')}
                              className="!bg-red-50 !text-red-700 hover:!bg-red-100"
                            >
                              <X className="h-4 w-4" /> Rad etish
                            </Button>
                          </div>
                          <span className="text-right text-[11px] leading-tight text-slate-400">
                            {FACE_APPROVE_HINT}
                          </span>
                        </div>
                      )}
                      {c.status === 'pending' && !c.canApprove && (
                        <span
                          className="text-[11px] text-slate-400"
                          title="Urinishning yuz vektori saqlanmagan — undan etalon yasab bo'lmaydi"
                        >
                          Tasdiqlab bo'lmaydi
                        </span>
                      )}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
          <TablePagination {...pg} />
        </Card>
      )}

      {/* ---- Tafsilot (katta selfi + sifat) ---- */}
      <Modal
        open={!!detail}
        onClose={() => setDetail(null)}
        size="lg"
        title={detail ? `${detail.studentName} — kirish urinishi` : ''}
        footer={
          <>
            {detail && canDecide && detail.status === 'pending' && detail.canApprove && (
              <>
                <Button
                  variant="secondary"
                  onClick={() => openDecision(detail, 'reject')}
                  className="!bg-red-50 !text-red-700 hover:!bg-red-100"
                >
                  <X className="h-4 w-4" /> Rad etish
                </Button>
                <Button onClick={() => openDecision(detail, 'approve')}>
                  <Check className="h-4 w-4" /> Tasdiqlash
                </Button>
              </>
            )}
            <Button variant="secondary" onClick={() => setDetail(null)}>
              Yopish
            </Button>
          </>
        }
      >
        {detail && <CheckDetail check={detail} />}
      </Modal>

      {/* ---- Qaror (tasdiqlash / rad etish) ---- */}
      <Modal
        open={!!decision}
        onClose={() => setDecision(null)}
        size="sm"
        title={
          decision?.action === 'approve'
            ? 'Urinishni tasdiqlash'
            : 'Urinishni rad etish'
        }
        footer={
          <>
            <Button variant="secondary" onClick={() => setDecision(null)} disabled={!!busyId}>
              Bekor qilish
            </Button>
            <Button
              variant={decision?.action === 'approve' ? 'primary' : 'danger'}
              onClick={confirmDecision}
              disabled={!!busyId}
            >
              {busyId ? (
                <Loader2 className="h-4 w-4 animate-spin" />
              ) : decision?.action === 'approve' ? (
                <Check className="h-4 w-4" />
              ) : (
                <X className="h-4 w-4" />
              )}
              {decision?.action === 'approve' ? 'Tasdiqlash' : 'Rad etish'}
            </Button>
          </>
        }
      >
        {decision && (
          <div className="space-y-3">
            <div className="flex items-center gap-3 rounded-lg bg-slate-50 px-3 py-2.5">
              <SelfieThumb url={decision.check.imageUrl} alt={decision.check.studentName} />
              <div className="min-w-0 text-sm">
                <div className="font-semibold text-slate-800">{decision.check.studentName}</div>
                <div className="text-xs text-slate-500">
                  {formatDateTime(decision.check.createdAt)} ·{' '}
                  {decision.check.deviceName || 'Nomsiz qurilma'}
                </div>
              </div>
            </div>

            {decision.action === 'approve' ? (
              <p className="text-sm text-slate-600">
                {FACE_APPROVE_HINT} O'quvchi shu qurilmadan kira oladi. Selfi boshqa odamniki
                bo'lsa — <b>rad eting</b>.
              </p>
            ) : (
              <p className="text-sm text-slate-600">
                O'quvchi bu qurilmadan kira olmaydi va qaytadan urinishi kerak bo'ladi. Izoh
                yozilmasa "Administrator rad etdi" deb saqlanadi.
              </p>
            )}

            {/* ⚠️ IZOH FAQAT RAD ETISHDA so'raladi: server uni `Reason` bo'lib saqlaydi va u
                ro'yxatda ko'rinadi. Tasdiqlashda esa endpoint izohni QABUL QILMAYDI (audit
                yozuvi tayyor matn bilan ketadi) — yozilgan izoh jimgina yo'qolib, foydalanuvchi
                "saqlandi" deb o'ylab qolardi. */}
            {decision.action === 'reject' && (
              <Textarea
                label="Sabab (ixtiyoriy)"
                rows={2}
                placeholder="Masalan: rasmda boshqa odam"
                value={note}
                onChange={(e) => setNote(e.target.value)}
              />
            )}
          </div>
        )}
      </Modal>
    </div>
  )
}

/** Sifat ko'rsatkichlari — chiplar ko'rinishida (xom JSON hech qachon ekranga chiqmaydi). */
function QualityChips({ metrics, compact }: { metrics: FaceQualityMetric[]; compact?: boolean }) {
  if (metrics.length === 0) {
    return <span className="text-[11px] text-slate-300">ma'lumot yo'q</span>
  }
  // Jadvalda faqat "yomon" ko'rsatkichlar, ular bo'lmasa dastlabki uchtasi — ustun kengayib
  // ketmasin (to'liq ro'yxat tafsilot oynasida).
  const bad = metrics.filter((m) => !m.ok)
  const shown = compact ? (bad.length > 0 ? bad : metrics.slice(0, 3)) : metrics
  const hidden = metrics.length - shown.length

  return (
    <div className={cn('flex flex-wrap gap-1', compact && 'max-w-[190px]')}>
      {shown.map((m) => (
        <span
          key={m.key}
          className={cn(
            'rounded px-1.5 py-0.5 text-[11px] font-medium',
            m.ok ? 'bg-slate-100 text-slate-600' : 'bg-red-50 text-red-700',
          )}
          title={m.ok ? undefined : 'Chegaradan chiqqan'}
        >
          {m.label} {m.value}
        </span>
      ))}
      {compact && hidden > 0 && <span className="text-[11px] text-slate-400">+{hidden}</span>}
    </div>
  )
}

/** Tafsilot oynasining ichi: katta selfi + urinish ma'lumotlari. */
function CheckDetail({ check }: { check: FaceCheck }) {
  // Rasm JWT bilan olinadi — `<img src>` to'g'ridan-to'g'ri ishlamaydi (`useFaceImage`).
  const { src, failed, loading } = useFaceImage(check.imageUrl)
  const metrics = qualityMetrics(parseQuality(check.quality))

  return (
    <div className="grid gap-4 sm:grid-cols-[minmax(0,260px)_1fr]">
      <div>
        {loading ? (
          <div className="aspect-square w-full animate-pulse rounded-xl bg-slate-100" />
        ) : src && !failed ? (
          <img
            src={src}
            alt={`${check.studentName} — selfi`}
            className="w-full rounded-xl border border-slate-200 object-cover"
          />
        ) : (
          <div className="flex aspect-square w-full flex-col items-center justify-center gap-2 rounded-xl border border-dashed border-slate-200 bg-slate-50 text-slate-400">
            <ImageOff className="h-7 w-7" />
            <span className="px-4 text-center text-xs">
              {check.imageUrl
                ? "Rasmni ko'rsatib bo'lmadi — fayl o'chirilgan bo'lishi mumkin"
                : 'Selfi saqlanmagan'}
            </span>
          </div>
        )}
      </div>

      <div className="space-y-3 text-sm">
        <div className="flex flex-wrap items-center gap-2">
          <Badge tone={statusTone(check.status)}>{statusLabel(check.status)}</Badge>
          <span className="font-mono text-slate-500">Ball: {scorePercent(check.score)}</span>
        </div>
        {check.reason && <p className="text-slate-600">{check.reason}</p>}

        <dl className="grid grid-cols-[110px_1fr] gap-x-3 gap-y-1.5 text-xs">
          <Row label="Vaqt">{formatDateTime(check.createdAt)}</Row>
          <Row label="Qurilma">{check.deviceName || 'Nomsiz qurilma'}</Row>
          <Row label="Platforma">{platformLabel(check.platform)}</Row>
          <Row label="Ilova">{check.appVersion || '—'}</Row>
          <Row label="Qurilma ID">
            <span className="break-all font-mono">{check.deviceId || '—'}</span>
          </Row>
          <Row label="IP">
            <span className="font-mono">{check.ip || '—'}</span>
          </Row>
          <Row label="Model">{check.modelVersion || '—'}</Row>
        </dl>

        <div>
          <div className="mb-1 text-xs font-semibold text-slate-500">Kadr sifati</div>
          <QualityChips metrics={metrics} />
        </div>
      </div>
    </div>
  )
}

function Row({ label, children }: { label: string; children: React.ReactNode }) {
  return (
    <>
      <dt className="text-slate-400">{label}</dt>
      <dd className="text-slate-700">{children}</dd>
    </>
  )
}
