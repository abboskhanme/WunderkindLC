import { useEffect, useState } from 'react'
import { Link } from 'react-router-dom'
import { Download } from 'lucide-react'
import type { ContractTemplate } from '@/types'
import { downloadContract, getTemplates } from '@/api/services/contracts'
import { Modal } from '@/components/ui/Modal'
import { Button } from '@/components/ui/Button'
import { apiErrorMessage, cn } from '@/lib/utils'

/**
 * `downloadContract` javobi `blob` — server xatosi (`{ message }`) ham Blob bo'lib keladi va
 * `apiErrorMessage` uni o'qiy olmaydi. Shu sabab matn shu yerda ochib olinadi.
 */
async function blobErrorMessage(err: unknown, fallback: string): Promise<string> {
  const data = (err as { response?: { data?: unknown } })?.response?.data
  if (data instanceof Blob) {
    try {
      const parsed = JSON.parse(await data.text()) as { message?: unknown }
      if (typeof parsed.message === 'string' && parsed.message) return parsed.message
    } catch {
      /* JSON emas — umumiy matnga tushamiz */
    }
  }
  return apiErrorMessage(err, fallback)
}

/**
 * QABUL CHEKLISTI → «Shartnoma tuzish»: ota-ona andozasini tanlab, SHU o'quvchi uchun to'ldirilgan
 * Word (.docx) faylni yuklab olish. «Shartnomalar» sahifasidagi «Shartnoma tuzish» bilan AYNAN
 * bitta endpoint (`POST /admin/contracts/build`) — raqam berish va tarixga yozish o'sha yerda.
 * Telegram orqali yuborish va PDF biriktirish — to'liq «Shartnomalar» sahifasida.
 */
export function IntakeContractModal({
  studentId,
  studentName,
  onClose,
  onDone,
}: {
  studentId: string
  studentName: string
  onClose: () => void
  /** Fayl yuklab olingach — cheklist qayta so'raladi (shartnoma endi BOR). */
  onDone: () => void
}) {
  const [templates, setTemplates] = useState<ContractTemplate[]>([])
  const [selected, setSelected] = useState('')
  const [loading, setLoading] = useState(true)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState('')

  useEffect(() => {
    let active = true
    getTemplates('parent')
      .then((list) => {
        if (!active) return
        setTemplates(list)
        setSelected(list[0]?.id ?? '')
      })
      .catch((e) => active && setError(apiErrorMessage(e, "Andozalarni yuklab bo'lmadi")))
      .finally(() => active && setLoading(false))
    return () => {
      active = false
    }
  }, [])

  const build = async () => {
    if (!selected || busy) return
    setBusy(true)
    setError('')
    try {
      await downloadContract('parent', selected, studentId)
      onDone()
      onClose()
    } catch (e) {
      setError(await blobErrorMessage(e, "Shartnomani tuzib bo'lmadi"))
    } finally {
      setBusy(false)
    }
  }

  return (
    <Modal
      open
      onClose={() => !busy && onClose()}
      size="sm"
      title="Shartnoma tuzish"
      footer={
        <>
          <Button variant="secondary" onClick={onClose} disabled={busy}>
            Bekor qilish
          </Button>
          <Button onClick={build} disabled={busy || !selected}>
            <Download className="h-4 w-4" /> {busy ? 'Tayyorlanmoqda...' : 'Word (.docx) yuklab olish'}
          </Button>
        </>
      }
    >
      <div className="space-y-3 text-sm">
        <p className="font-semibold text-slate-700">{studentName}</p>
        {loading ? (
          <p className="text-slate-400">Andozalar yuklanmoqda...</p>
        ) : templates.length === 0 ? (
          <p className="rounded-lg bg-amber-50 px-3 py-2 text-amber-800">
            Ota-ona uchun shartnoma andozasi yo'q —{' '}
            <Link to="/admin/contracts" className="font-semibold underline">
              «Shartnomalar»
            </Link>{' '}
            sahifasida andoza qo'shing.
          </p>
        ) : (
          <div className="space-y-1.5">
            {templates.map((t) => (
              <label
                key={t.id}
                className={cn(
                  'flex cursor-pointer items-center gap-2 rounded-lg border px-3 py-2',
                  selected === t.id ? 'border-brand-300 bg-brand-50' : 'border-slate-100 hover:bg-slate-50',
                )}
              >
                <input
                  type="radio"
                  name="intake-contract-tpl"
                  checked={selected === t.id}
                  onChange={() => setSelected(t.id)}
                  className="h-4 w-4 accent-brand-600"
                />
                <span className="truncate text-slate-700">{t.name || t.fileName || 'Andoza'}</span>
              </label>
            ))}
          </div>
        )}
        <p className="text-xs text-slate-500">
          Shartnoma raqami beriladi va tarixga yoziladi. Yakunlangan PDF nusxani «Shartnomalar → Tuzilgan
          shartnomalar» bo'limidan biriktiring.
        </p>
        {error && <p className="text-sm text-red-600">{error}</p>}
      </div>
    </Modal>
  )
}
