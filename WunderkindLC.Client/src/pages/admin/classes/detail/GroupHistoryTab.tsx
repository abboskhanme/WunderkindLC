import { useEffect, useMemo, useState } from 'react'
import { getAuditLogs } from '@/api/services/audit'
import type { AuditLog } from '@/types'
import { apiErrorMessage, formatDateTime } from '@/lib/utils'
import { toGroupHistoryRow, type GroupHistoryRow } from '@/lib/groupDisplay'
import { AuditHistoryList } from '@/components/audit/AuditHistoryList'
import { DataTable } from '@/components/ui/list/DataTable'
import { TotalPill } from '@/components/ui/list/TotalPill'
import { DateRangeFilter } from '@/components/ui/list/DateRangeFilter'
import { Switch } from '@/components/ui/list/Switch'
import { TablePagination, usePagination } from '@/components/ui/TablePagination'

/** Bir so'rovda ko'pi bilan (server chegarasi — `AuditController.MaxLimit`). */
const LIMIT = 500

/**
 * Guruh sahifasi → "Guruh tarixi ma'lumotlari" (edutizim): sana oralig'i + jadval
 * (№ · O'QUVCHILAR · MODERATOR · ESKI O'QITUVCHI · O'QITUVCHI · SANA · TURI).
 *
 * Manba — mavjud "O'zgarishlar tarixi" (`GET /admin/audit?groupId=`: guruh yozuvining o'zi +
 * a'zolik hodisalari). "Batafsil" kaliti avvalgi ko'rinishni (eski → yangi qiymatlar bilan)
 * qaytaradi — hech narsa yo'qolmaydi. Ruxsat — `audit` (tab o'zi shunga qarab ko'rsatiladi).
 *
 * ⚠️ Edutizimdagi "DARS SANASI" bizda yo'q: ustun hodisa VAQTINI ko'rsatadi ("SANA").
 */
export function GroupHistoryTab({
  groupId,
  studentNames,
}: {
  groupId: string
  /** studentId → ism (guruhning barcha a'zolari — chiqqanlari ham). */
  studentNames: Map<string, string>
}) {
  const [from, setFrom] = useState('')
  const [to, setTo] = useState('')
  const [detailed, setDetailed] = useState(false)
  const [logs, setLogs] = useState<AuditLog[]>([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    if (detailed) return
    let alive = true
    // eslint-disable-next-line react-hooks/set-state-in-effect -- filtr o'zgarganda qayta yuklash (maqsadli)
    setLoading(true)
    setError(null)
    getAuditLogs({
      groupId,
      from: from || undefined,
      // `to` KUN sifatida — o'sha kunning o'zi ham kirsin (audit.md §5).
      to: to ? `${to}T23:59:59` : undefined,
      limit: LIMIT,
    })
      .then((r) => alive && setLogs(r))
      .catch((e) => alive && setError(apiErrorMessage(e, "Tarixni yuklab bo'lmadi")))
      .finally(() => alive && setLoading(false))
    return () => {
      alive = false
    }
  }, [groupId, from, to, detailed])

  const rows = useMemo(() => logs.map((l) => toGroupHistoryRow(l, studentNames)), [logs, studentNames])
  const pg = usePagination(rows)

  return (
    <div>
      <div className="mb-2.5 flex flex-wrap items-center gap-2">
        <DateRangeFilter
          from={from}
          to={to}
          onChange={(f, t) => {
            setFrom(f)
            setTo(t)
          }}
          className="w-[320px] max-w-full"
        />
        <div className="ml-auto">
          <Switch label="Batafsil (eski → yangi qiymatlar)" checked={detailed} onChange={setDetailed} />
        </div>
      </div>

      {detailed ? (
        <div className="rounded-xl border border-[#dbe0e6] bg-white p-4">
          <AuditHistoryList
            filters={{ groupId, from: from || undefined, to: to ? `${to}T23:59:59` : undefined }}
            emptyLabel="O'zgarishlar tarixi yo'q"
          />
        </div>
      ) : (
        <>
          {error && <p className="mb-2 text-[13px] font-medium text-[#d32f2f]">{error}</p>}
          <TotalPill total={rows.length} />
          <DataTable<GroupHistoryRow>
            rows={pg.paged}
            rowKey={(r) => r.id}
            loading={loading}
            numbered
            offset={(pg.page - 1) * pg.pageSize}
            columns={[
              { key: 'student', header: "O'quvchilar", render: (r) => r.student || '—' },
              { key: 'moderator', header: 'Moderator', render: (r) => r.moderator || '—' },
              { key: 'old', header: "Eski o'qituvchi", render: (r) => r.oldTeacher },
              { key: 'teacher', header: "O'qituvchi", render: (r) => r.teacher },
              {
                key: 'at',
                header: 'Sana',
                render: (r) => <span className="whitespace-nowrap">{formatDateTime(r.at)}</span>,
              },
              {
                key: 'type',
                header: 'Turi',
                render: (r) => (
                  <span className="block max-w-[420px] whitespace-normal py-1" title={r.type}>
                    {r.type}
                  </span>
                ),
              },
            ]}
            footer={<TablePagination {...pg} />}
          />
          {logs.length >= LIMIT && (
            <p className="mt-2 text-[12px] text-[#6b7280]">
              Eng yangi {LIMIT} ta yozuv ko'rsatildi — eskilarini ko'rish uchun sana oralig'ini toraytiring.
            </p>
          )}
        </>
      )}
    </div>
  )
}
