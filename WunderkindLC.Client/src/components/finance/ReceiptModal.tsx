import { useEffect, useState } from 'react'
import { Printer } from 'lucide-react'
import { Modal } from '@/components/ui/Modal'
import { Button } from '@/components/ui/Button'
import { Loader } from '@/components/ui/Loader'
import { getReceipt } from '@/api/services/finance'
import { getTrialReceipt } from '@/api/services/leads'
import { receiptHtml, receiptCss, printReceipt, type ReceiptData } from '@/lib/receipt'
import { parseCheckSettings, resolveCheckSettings, type CheckSettings } from '@/config/checkSettings'

interface Props {
  /** To'lov cheki uchun tranzaksiya id'si (moliya) */
  txId?: string | null
  /** Sinov darsi cheki uchun trial id'si (lid bo'limi, to'lovsiz) */
  trialId?: string | null
  /** Ochilganda avtomatik print dialogini ochish (to'lov/yozuv kiritilgandan keyin) */
  autoPrint?: boolean
  onClose: () => void
}

/** To'lov / sinov darsi cheki (termal kvitansiya) — ko'rib chiqish + bosib chiqarish. */
export function ReceiptModal({ txId, trialId, autoPrint, onClose }: Props) {
  const [data, setData] = useState<ReceiptData | null>(null)
  const [settings, setSettings] = useState<CheckSettings | null>(null)
  const [loading, setLoading] = useState(false)
  const open = !!txId || !!trialId

  useEffect(() => {
    if (!txId && !trialId) {
      // eslint-disable-next-line react-hooks/set-state-in-effect -- modal yopilganda tozalash
      setData(null)
      setSettings(null)
      return
    }
    // eslint-disable-next-line react-hooks/set-state-in-effect -- modal ochilganda yuklash
    setLoading(true)
    const fetch = txId ? getReceipt(txId) : getTrialReceipt(trialId!)
    fetch
      .then((r) => {
        const { settingsJson, ...rest } = r
        setData(rest)
        // Sinov cheki (trialId) bo'lsa — sinov maydonlari/footeri qo'llanadi.
        const s = resolveCheckSettings(parseCheckSettings(settingsJson), !!trialId)
        setSettings(s)
        if (autoPrint) printReceipt(rest, s)
      })
      .finally(() => setLoading(false))
  }, [txId, trialId, autoPrint])

  const print = () => {
    if (data && settings) printReceipt(data, settings)
  }

  return (
    <Modal
      open={open}
      onClose={onClose}
      size="sm"
      title={trialId ? 'Sinov darsi cheki' : "To'lov cheki"}
      footer={
        <>
          <Button variant="secondary" onClick={onClose}>
            Yopish
          </Button>
          <Button onClick={print} disabled={!data}>
            <Printer className="h-4 w-4" /> Chop etish
          </Button>
        </>
      }
    >
      {loading || !data || !settings ? (
        <Loader label="Yuklanmoqda..." />
      ) : (
        <div className="flex justify-center rounded-lg bg-slate-100 py-4">
          <style>{receiptCss}</style>
          <div
            className="receipt shadow-md"
            dangerouslySetInnerHTML={{ __html: receiptHtml(data, settings) }}
          />
        </div>
      )}
    </Modal>
  )
}
