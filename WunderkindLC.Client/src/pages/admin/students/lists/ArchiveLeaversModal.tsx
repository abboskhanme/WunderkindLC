import { useEffect, useMemo, useRef, useState } from 'react'
import { archiveLeavers, getArchiveLeavers, type ArchiveLeaver, type ArchiveLeaversResult } from '@/api/services/students'
import { Modal } from '@/components/ui/Modal'
import { Button } from '@/components/ui/Button'
import { Loader } from '@/components/ui/Loader'
import { apiErrorMessage, balanceTextCls, cn, formatDate, formatMoney } from '@/lib/utils'

/** Oyna faqat OCHIQ bo'lganda chiziladi (`{open && <ArchiveLeaversModal …/>}`) — har ochilishda toza holat. */
interface Props {
  onClose: () => void
  /** Kamida bitta o'quvchi arxivlangach — ro'yxatni yangilash uchun. */
  onDone: () => void
}

/**
 * «GURUHSIZ QOLGANLARNI ARXIVLASH» — guruhdan chiqarilgan, hech bir guruhda qolmagan, lekin arxivga
 * o'tkazilmagan o'quvchilar (server `LeaverArchive`). Mijoz: "58 ta o'quvchini chiqardim, arxiv 0".
 * Har qatorda oxirgi chiqish sababi va izohi; arxiv sababi = o'sha sabab. Tanlov standart HAMMASI,
 * server nomzodlikni qayta tekshiradi (shu orada guruhga qaytgani tegilmaydi — natijada aytiladi).
 */
export function ArchiveLeaversModal({ onClose, onDone }: Props) {
  const [rows, setRows] = useState<ArchiveLeaver[] | null>(null)
  const [selected, setSelected] = useState<Set<string>>(new Set())
  const [error, setError] = useState<string | null>(null)
  const [result, setResult] = useState<ArchiveLeaversResult | null>(null)
  const [submitting, setSubmitting] = useState(false)
  /** SINXRON qulf — `submitting` keyingi renderda yangilanadi, tez ikkinchi bosish o'tib ketmasin. */
  const inFlightRef = useRef(false)

  useEffect(() => {
    let alive = true
    getArchiveLeavers()
      .then((list) => {
        if (!alive) return
        setRows(list)
        setSelected(new Set(list.map((r) => r.studentId)))
      })
      .catch((err) => alive && setError(apiErrorMessage(err, "Ro'yxatni yuklab bo'lmadi")))
    return () => {
      alive = false
    }
  }, [])

  const all = useMemo(() => rows ?? [], [rows])
  const allChecked = all.length > 0 && all.every((r) => selected.has(r.studentId))
  const debtors = useMemo(() => all.filter((r) => selected.has(r.studentId) && r.balance < 0).length, [all, selected])

  const toggle = (id: string) =>
    setSelected((prev) => {
      const next = new Set(prev)
      if (next.has(id)) next.delete(id)
      else next.add(id)
      return next
    })

  const submit = async () => {
    if (inFlightRef.current || selected.size === 0) return
    inFlightRef.current = true
    setSubmitting(true)
    setError(null)
    try {
      const res = await archiveLeavers([...selected])
      setResult(res)
      if (res.archived > 0) onDone()
    } catch (err) {
      setError(apiErrorMessage(err, "Arxivga o'tkazib bo'lmadi"))
    } finally {
      inFlightRef.current = false
      setSubmitting(false)
    }
  }

  return (
    <Modal
      open
      onClose={onClose}
      title="Guruhsiz qolganlarni arxivlash"
      size="xl"
      footer={
        result ? (
          <Button onClick={onClose}>Yopish</Button>
        ) : (
          <>
            <Button variant="secondary" onClick={onClose}>
              Bekor
            </Button>
            <Button onClick={submit} disabled={submitting || selected.size === 0}>
              {submitting ? 'Saqlanmoqda…' : `Arxivga o'tkazish (${selected.size})`}
            </Button>
          </>
        )
      }
    >
      {result ? (
        <div className="space-y-2 text-sm text-slate-700">
          <p>
            <span className="font-semibold">{result.archived} ta</span> o'quvchi arxivga o'tkazildi.
          </p>
          {result.skipped > 0 && (
            <p className="text-amber-700">
              {result.skipped} tasi o'tkazilmadi — shu orada guruhga qo'shilgan yoki arxivlangan
              {result.skippedNames.length > 0 && `: ${result.skippedNames.join(', ')}`}.
            </p>
          )}
          {result.errors.length > 0 && (
            <div className="rounded-lg bg-red-50 p-2 text-red-700">
              {result.errors.map((e) => (
                <p key={e}>{e}</p>
              ))}
            </div>
          )}
        </div>
      ) : rows === null && !error ? (
        <Loader className="min-h-[160px]" />
      ) : (
        <div className="space-y-3">
          <p className="text-sm text-slate-600">
            Guruhdan chiqarilgan va hech bir guruhda qolmagan, lekin arxivga o'tkazilmagan o'quvchilar. Arxiv
            sababi — chiqarishdagi sabab. Arxivda o'quvchi faol ro'yxatdan chiqadi va kabinetga kirishi
            yopiladi; qarzi saqlanadi (Moliya → O'quvchilar qarzi). Kerak bo'lsa arxivdan qaytariladi.
          </p>
          {error && <p className="text-sm text-red-600">{error}</p>}
          {all.length === 0 ? (
            <p className="py-6 text-center text-sm text-slate-500">Guruhsiz qolgan o'quvchi yo'q.</p>
          ) : (
            <>
              <div className="flex flex-wrap items-center justify-between gap-2 text-[13px]">
                <label className="flex cursor-pointer items-center gap-2 text-slate-700">
                  <input
                    type="checkbox"
                    className="h-4 w-4 rounded border-slate-300"
                    checked={allChecked}
                    onChange={() =>
                      setSelected(allChecked ? new Set() : new Set(all.map((r) => r.studentId)))
                    }
                  />
                  Hammasini belgilash ({all.length})
                </label>
                {debtors > 0 && (
                  <span className="text-amber-700">Tanlanganlardan {debtors} tasida qarz bor</span>
                )}
              </div>
              <div className="max-h-[55vh] overflow-auto rounded-lg border border-slate-200">
                <table className="w-full text-left text-[13px]">
                  <thead className="sticky top-0 bg-slate-50 text-slate-500">
                    <tr>
                      <th className="w-8 px-2 py-2" />
                      <th className="px-2 py-2 font-medium">O'quvchi</th>
                      <th className="px-2 py-2 font-medium">Chiqqan guruhi</th>
                      <th className="px-2 py-2 font-medium">Sana</th>
                      <th className="px-2 py-2 font-medium">Sabab</th>
                      <th className="px-2 py-2 font-medium">Izoh</th>
                      <th className="px-2 py-2 text-right font-medium">Balans</th>
                    </tr>
                  </thead>
                  <tbody>
                    {all.map((r) => (
                      <tr
                        key={r.studentId}
                        onClick={() => toggle(r.studentId)}
                        className={cn(
                          'cursor-pointer border-t border-slate-100',
                          selected.has(r.studentId) ? 'bg-white' : 'bg-slate-50 text-slate-400',
                        )}
                      >
                        <td className="px-2 py-2">
                          <input
                            type="checkbox"
                            className="h-4 w-4 rounded border-slate-300"
                            checked={selected.has(r.studentId)}
                            onChange={() => toggle(r.studentId)}
                            onClick={(e) => e.stopPropagation()}
                          />
                        </td>
                        <td className="px-2 py-2 font-medium">{r.fullName}</td>
                        <td className="px-2 py-2">{r.groupName || '—'}</td>
                        <td className="whitespace-nowrap px-2 py-2">{formatDate(r.leftAt)}</td>
                        <td className="px-2 py-2">{r.reason || <span className="text-slate-400">—</span>}</td>
                        <td className="max-w-[260px] px-2 py-2">
                          {r.note ? (
                            <span className="line-clamp-2" title={r.note}>
                              {r.note}
                            </span>
                          ) : (
                            <span className="text-slate-400">—</span>
                          )}
                        </td>
                        <td className={cn('whitespace-nowrap px-2 py-2 text-right', balanceTextCls(r.balance))}>
                          {formatMoney(r.balance)}
                        </td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            </>
          )}
        </div>
      )}
    </Modal>
  )
}
