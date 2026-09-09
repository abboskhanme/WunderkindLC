import { useCallback, useEffect, useMemo, useState } from 'react'
import { AlertTriangle, Loader2, Search, ShieldOff, Smartphone, Trash2 } from 'lucide-react'
import type { FaceDevice, FaceProfile } from '@/api/services/face'
import { deleteFaceProfile, getFaceDevices, getFaceProfile, revokeFaceDevice } from '@/api/services/face'
import { searchStudents } from '@/api/services/students'
import type { StudentSearchResult } from '@/api/services/students'
import { Badge } from '@/components/ui/Badge'
import { Button } from '@/components/ui/Button'
import { Card } from '@/components/ui/Card'
import { Input } from '@/components/ui/Input'
import { Loader } from '@/components/ui/Loader'
import { Modal } from '@/components/ui/Modal'
import { TablePagination, usePagination } from '@/components/ui/TablePagination'
import { apiErrorMessage, cn, formatDateTime } from '@/lib/utils'
import { groupsText } from '@/lib/studentGroups'
import { faceSourceLabel, platformLabel } from './faceLabels'

interface Props {
  /** Bekor qilish / etalonni tozalash tugmalari ko'rinadimi (`students:edit`). */
  canEdit: boolean
}

/** Etalonni tozalash uchun tanlangan o'quvchi (qatordan yoki qidiruvdan). */
interface EtalonTarget {
  studentId: string
  studentName: string
}

/**
 * QURILMALAR — o'quvchi bir marta selfi bilan tasdiqlagan telefonlar. Shu ro'yxatdagi
 * qurilmada keyingi kirishlarda selfi SO'RALMAYDI, shuning uchun telefon yo'qolganda uni
 * BEKOR qilish kerak.
 *
 * Shu tabda ETALONNI TOZALASH ham bor: etalon o'quvchining yuz namunasi bo'lib, u eskirgan
 * (bola o'sgan) yoki noto'g'ri odamdan olingan bo'lsa tozalanadi — keyingi kirishda o'quvchi
 * qaytadan ro'yxatdan o'tadi.
 */
export function FaceDevicesTab({ canEdit }: Props) {
  const [rows, setRows] = useState<FaceDevice[]>([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState('')
  const [busy, setBusy] = useState(false)
  const [query, setQuery] = useState('')

  const [revoking, setRevoking] = useState<FaceDevice | null>(null)
  const [clearing, setClearing] = useState<EtalonTarget | null>(null)

  const load = useCallback(() => {
    setLoading(true)
    setError('')
    getFaceDevices()
      .then(setRows)
      .catch((err) => setError(apiErrorMessage(err, "Qurilmalarni yuklab bo'lmadi")))
      .finally(() => setLoading(false))
  }, [])

  useEffect(load, [load])

  const filtered = useMemo(() => {
    const term = query.trim().toLowerCase()
    if (!term) return rows
    return rows.filter(
      (d) =>
        d.studentName.toLowerCase().includes(term) ||
        (d.deviceName || '').toLowerCase().includes(term),
    )
  }, [rows, query])

  const pg = usePagination(filtered)

  const confirmRevoke = async () => {
    if (!revoking || busy) return
    setBusy(true)
    setError('')
    try {
      await revokeFaceDevice(revoking.id)
      setRevoking(null)
      load()
    } catch (err) {
      setError(apiErrorMessage(err, "Qurilmani bekor qilib bo'lmadi"))
    } finally {
      setBusy(false)
    }
  }

  return (
    <div className="space-y-4">
      {canEdit && <ClearEtalonCard onPick={setClearing} />}

      <Card
        title="Ishonchli qurilmalar"
        sub="Shu telefonlarda kirishda selfi qayta so'ralmaydi"
        actions={
          <div className="relative w-56">
            <Input
              placeholder="O'quvchi yoki qurilma..."
              value={query}
              onChange={(e) => setQuery(e.target.value)}
            />
            <Search className="pointer-events-none absolute right-3 top-2.5 h-4 w-4 text-slate-300" />
          </div>
        }
        tight
      >
        {error && (
          <div className="mx-4 mt-4 flex items-center gap-2 rounded-lg bg-red-50 px-4 py-3 text-sm text-red-700">
            <AlertTriangle className="h-4 w-4 shrink-0" /> {error}
          </div>
        )}

        {loading ? (
          <Loader label="Yuklanmoqda..." />
        ) : filtered.length === 0 ? (
          <div className="state">
            <div className="state-icon">
              <Smartphone className="h-5 w-5" />
            </div>
            <h4>Qurilma yo'q</h4>
            <p>
              Ishonchli qurilma o'quvchi ilovaga yangi telefondan kirib, selfi tekshiruvidan
              o'tganda paydo bo'ladi.
            </p>
          </div>
        ) : (
          <>
            <div className="overflow-x-auto">
              <table className="table">
                <thead>
                  <tr>
                    <th>O'quvchi</th>
                    <th>Qurilma</th>
                    <th>Platforma</th>
                    <th>Birinchi kirish</th>
                    <th>Oxirgi faollik</th>
                    <th>Holat</th>
                    <th />
                  </tr>
                </thead>
                <tbody>
                  {pg.paged.map((d) => {
                    const revoked = !!d.revokedAt
                    return (
                      <tr key={d.id} className={cn(revoked && 'opacity-50')}>
                        <td className="font-medium text-slate-800">{d.studentName}</td>
                        <td>
                          <div className="text-slate-700">{d.deviceName || 'Nomsiz qurilma'}</div>
                          <div className="break-all font-mono text-[11px] text-slate-400">
                            {d.deviceId}
                          </div>
                        </td>
                        <td className="text-slate-500">{platformLabel(d.platform)}</td>
                        <td className="whitespace-nowrap text-slate-500">
                          {formatDateTime(d.createdAt)}
                        </td>
                        <td className="whitespace-nowrap text-slate-500">
                          {formatDateTime(d.lastSeenAt)}
                        </td>
                        <td>
                          {revoked ? (
                            <div>
                              <Badge tone="red">Bekor qilingan</Badge>
                              <div className="mt-0.5 text-[11px] text-slate-400">
                                {formatDateTime(d.revokedAt)}
                              </div>
                            </div>
                          ) : (
                            <Badge tone="green">Faol</Badge>
                          )}
                        </td>
                        <td className="whitespace-nowrap text-right">
                          {canEdit && (
                            <div className="inline-flex gap-1.5">
                              {!revoked && (
                                <Button
                                  variant="secondary"
                                  onClick={() => setRevoking(d)}
                                  className="!bg-red-50 !text-red-700 hover:!bg-red-100"
                                >
                                  <ShieldOff className="h-4 w-4" /> Bekor qilish
                                </Button>
                              )}
                              {d.studentId && (
                                <Button
                                  variant="secondary"
                                  onClick={() =>
                                    setClearing({ studentId: d.studentId, studentName: d.studentName })
                                  }
                                  title="Yuz etalonini o'chirish — o'quvchi qaytadan ro'yxatdan o'tadi"
                                >
                                  <Trash2 className="h-4 w-4" /> Etalon
                                </Button>
                              )}
                            </div>
                          )}
                        </td>
                      </tr>
                    )
                  })}
                </tbody>
              </table>
            </div>
            <TablePagination {...pg} />
          </>
        )}
      </Card>

      {/* ---- Qurilmani bekor qilish ---- */}
      <Modal
        open={!!revoking}
        onClose={() => setRevoking(null)}
        size="sm"
        title="Qurilmani bekor qilish"
        footer={
          <>
            <Button variant="secondary" onClick={() => setRevoking(null)} disabled={busy}>
              Bekor qilish
            </Button>
            <Button variant="danger" onClick={confirmRevoke} disabled={busy}>
              {busy ? <Loader2 className="h-4 w-4 animate-spin" /> : <ShieldOff className="h-4 w-4" />}
              Ha, bekor qilinsin
            </Button>
          </>
        }
      >
        {revoking && (
          <p className="text-sm text-slate-600">
            <b>{revoking.studentName}</b> ning «{revoking.deviceName || 'nomsiz qurilma'}»
            qurilmasi bekor qilinadi. Shu telefondan keyingi kirishda yana <b>selfi so'raladi</b>.
            Yozuv o'chmaydi — qachon bekor qilingani tarixda qoladi.
          </p>
        )}
      </Modal>

      {/* ---- Etalonni tozalash ---- */}
      <ClearEtalonModal
        target={clearing}
        onClose={() => setClearing(null)}
        onCleared={() => {
          setClearing(null)
          load()
        }}
      />
    </div>
  )
}

/** O'quvchini qidirib etalonini tozalash (qurilmasi yo'q o'quvchi uchun ham kerak). */
function ClearEtalonCard({ onPick }: { onPick: (t: EtalonTarget) => void }) {
  const [q, setQ] = useState('')
  const [results, setResults] = useState<StudentSearchResult[]>([])
  const [searching, setSearching] = useState(false)

  useEffect(() => {
    const term = q.trim()
    if (term.length < 2) {
      setResults([])
      return
    }
    // Har harfda so'rov yubormaslik uchun kichik kechikish.
    setSearching(true)
    const t = setTimeout(() => {
      searchStudents(term, 8)
        .then(setResults)
        .catch(() => setResults([]))
        .finally(() => setSearching(false))
    }, 350)
    return () => clearTimeout(t)
  }, [q])

  return (
    <Card
      title="Etalonni tozalash"
      sub="O'quvchining yuz namunasi eskirgan yoki noto'g'ri bo'lsa — o'chiriladi"
    >
      <div className="space-y-2">
        <div className="relative max-w-md">
          <Input
            label="O'quvchini qidirish"
            placeholder="F.I.Sh. yoki telefon (kamida 2 belgi)"
            value={q}
            onChange={(e) => setQ(e.target.value)}
          />
          {searching && (
            <Loader2 className="absolute right-3 top-[34px] h-4 w-4 animate-spin text-slate-300" />
          )}
        </div>

        {results.length > 0 && (
          <div className="max-w-md divide-y divide-slate-100 rounded-lg border border-slate-200">
            {results.map((s) => (
              <button
                key={s.id}
                type="button"
                onClick={() => {
                  onPick({ studentId: s.id, studentName: s.fullName })
                  setQ('')
                  setResults([])
                }}
                className="flex w-full items-center justify-between gap-3 px-3 py-2 text-left text-sm hover:bg-slate-50"
              >
                <span className="min-w-0 truncate font-medium text-slate-700">{s.fullName}</span>
                <span className="shrink-0 text-xs text-slate-400">
                  {/* Guruh — a'zoliklardan (muzlatilgani yashiriladi), `groups[0]` EMAS. */}
                  {s.isArchived ? 'arxiv' : groupsText(s.groups)}
                </span>
              </button>
            ))}
          </div>
        )}

        <p className="text-xs text-slate-400">
          Etalon o'chirilganda ishonchli qurilmalar TEGILMAYDI — ular allaqachon tasdiqlangan
          telefonlar. Hammasini chiqarib yuborish kerak bo'lsa qurilmalarni alohida bekor qiling.
        </p>
      </div>
    </Card>
  )
}

/** Tasdiqlash oynasi: avval etalon holati yuklanadi, keyin o'chiriladi. */
function ClearEtalonModal({
  target,
  onClose,
  onCleared,
}: {
  target: EtalonTarget | null
  onClose: () => void
  onCleared: () => void
}) {
  const [profile, setProfile] = useState<FaceProfile | null>(null)
  const [loading, setLoading] = useState(false)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState('')

  useEffect(() => {
    if (!target) return
    setProfile(null)
    setError('')
    setLoading(true)
    getFaceProfile(target.studentId)
      .then(setProfile)
      .catch((err) => setError(apiErrorMessage(err, "Etalon holatini o'qib bo'lmadi")))
      .finally(() => setLoading(false))
  }, [target])

  const confirm = async () => {
    if (!target || busy) return
    setBusy(true)
    setError('')
    try {
      await deleteFaceProfile(target.studentId)
      onCleared()
    } catch (err) {
      setError(apiErrorMessage(err, "Etalonni o'chirib bo'lmadi"))
    } finally {
      setBusy(false)
    }
  }

  return (
    <Modal
      open={!!target}
      onClose={onClose}
      size="sm"
      title="Yuz etalonini tozalash"
      footer={
        <>
          <Button variant="secondary" onClick={onClose} disabled={busy}>
            Bekor qilish
          </Button>
          <Button variant="danger" onClick={confirm} disabled={busy || loading || !profile}>
            {busy ? <Loader2 className="h-4 w-4 animate-spin" /> : <Trash2 className="h-4 w-4" />}
            Ha, o'chirilsin
          </Button>
        </>
      }
    >
      {target && (
        <div className="space-y-3 text-sm">
          <div className="rounded-lg bg-slate-50 px-3 py-2 font-semibold text-slate-800">
            {target.studentName}
          </div>

          {loading ? (
            <Loader label="Tekshirilmoqda..." className="py-6" />
          ) : profile ? (
            <>
              <dl className="grid grid-cols-[110px_1fr] gap-x-3 gap-y-1 text-xs">
                <dt className="text-slate-400">Manba</dt>
                <dd className="text-slate-700">{faceSourceLabel(profile.source)}</dd>
                <dt className="text-slate-400">Model</dt>
                <dd className="text-slate-700">{profile.modelVersion || '—'}</dd>
                <dt className="text-slate-400">Yaratilgan</dt>
                <dd className="text-slate-700">{formatDateTime(profile.createdAt)}</dd>
                <dt className="text-slate-400">Yangilangan</dt>
                <dd className="text-slate-700">{formatDateTime(profile.updatedAt)}</dd>
              </dl>
              <p className="text-slate-600">
                Etalon o'chiriladi va o'quvchi keyingi kirishda <b>qaytadan ro'yxatdan o'tadi</b>
                {' '}(selfi profil rasmi bilan solishtiriladi; profil rasmi bo'lmasa urinish yana
                shu bo'limga tushadi).
              </p>
            </>
          ) : (
            <p className="text-slate-500">
              Bu o'quvchida yuz etaloni yo'q — o'chiradigan narsa yo'q.
            </p>
          )}

          {error && (
            <div className="flex items-center gap-2 rounded-lg bg-red-50 px-3 py-2 text-red-700">
              <AlertTriangle className="h-4 w-4 shrink-0" /> {error}
            </div>
          )}
        </div>
      )}
    </Modal>
  )
}
