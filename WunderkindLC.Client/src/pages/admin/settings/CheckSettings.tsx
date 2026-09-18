import { useEffect, useState } from 'react'
import { Check, Receipt as ReceiptIcon } from 'lucide-react'
import {
  getCheckSettings,
  saveCheckSettings,
  getSchoolInfo,
  type SchoolInfo,
} from '@/api/services/settings'
import { Card } from '@/components/ui/Card'
import { Button } from '@/components/ui/Button'
import { Input } from '@/components/ui/Input'
import { Loader } from '@/components/ui/Loader'
import { cn } from '@/lib/utils'
import {
  parseCheckSettings,
  resolveCheckSettings,
  checkFieldLabels,
  checkTrialFieldKeys,
  type CheckSettings as CheckSettingsModel,
  type CheckFieldFlags,
} from '@/config/checkSettings'
import { receiptHtml, receiptCss, type ReceiptData } from '@/lib/receipt'
import { useAuth } from '@/context/auth-context'

type Tab = 'payment' | 'trial'

/** Namuna — to'lov cheki (sizning misolingiz). */
const samplePayment = {
  receiptNo: '693096843',
  dateTime: '2026-06-30 15:00',
  studentName: "Azamjon Qo'chqorov",
  teacherName: 'Odilov Azizbek',
  groupName: 'A100',
  method: 'cash',
  comment: "Oylik to'lov",
  total: 200000,
}

/** Namuna — sinov darsi cheki (to'lovsiz). */
const sampleTrial = {
  receiptNo: '481209337',
  dateTime: '2026-07-02 11:20',
  studentName: "Azamjon Qo'chqorov",
  teacherName: 'Odilov Azizbek',
  groupName: 'A100',
  method: '',
  comment: '',
  total: null as number | null,
  subtitle: 'Sinov darsiga yozildi',
}

/** Yoqish/o'chirish qatori. */
function Toggle({
  checked,
  onChange,
  label,
}: {
  checked: boolean
  onChange: (v: boolean) => void
  label: string
}) {
  return (
    <label className="flex cursor-pointer items-center justify-between gap-3 rounded-lg border border-slate-200 px-3 py-2 text-sm hover:bg-slate-50">
      <span className="text-slate-700">{label}</span>
      <input
        type="checkbox"
        checked={checked}
        onChange={(e) => onChange(e.target.checked)}
        className="h-4 w-4 accent-brand-600"
      />
    </label>
  )
}

/**
 * To'lov cheki (kvitansiya) sozlamalari — IKKI tur: "To'lov cheki" (moliya) va "Sinov darsi cheki" (lid).
 * Har biriga alohida maydon tanlovi va footer; sarlavha (logotip/nom) + aloqa/QR ikkalasiga umumiy.
 * O'ng tomonda tanlangan tur uchun jonli namuna. Saqlanadi: CenterMeta.CheckSettings (JSON).
 */
export function CheckSettings() {
  const { user } = useAuth()
  const [s, setS] = useState<CheckSettingsModel | null>(null)
  const [school, setSchool] = useState<SchoolInfo | null>(null)
  const [loading, setLoading] = useState(true)
  const [status, setStatus] = useState<'idle' | 'saving' | 'saved'>('idle')
  const [tab, setTab] = useState<Tab>('payment')

  useEffect(() => {
    Promise.all([getCheckSettings(), getSchoolInfo()])
      .then(([json, info]) => {
        setS(parseCheckSettings(json))
        setSchool(info)
      })
      .finally(() => setLoading(false))
  }, [])

  const save = async () => {
    if (!s) return
    setStatus('saving')
    try {
      await saveCheckSettings(JSON.stringify(s))
      setStatus('saved')
      setTimeout(() => setStatus('idle'), 2000)
    } catch {
      setStatus('idle')
    }
  }

  if (loading || !s) return <Loader label="Yuklanmoqda..." />

  const isTrial = tab === 'trial'
  // Aktiv tur maydonlari va footeri (to'lov yoki sinov).
  const activeFields = isTrial ? s.trial.fields : s.fields
  const activeFooter = isTrial ? s.trial.footerText : s.footerText
  const fieldList = isTrial
    ? checkFieldLabels.filter((f) => checkTrialFieldKeys.includes(f.key))
    : checkFieldLabels

  const setField = (key: keyof CheckFieldFlags, v: boolean) => {
    if (isTrial) setS({ ...s, trial: { ...s.trial, fields: { ...s.trial.fields, [key]: v } } })
    else setS({ ...s, fields: { ...s.fields, [key]: v } })
  }
  const setFooter = (v: string) => {
    if (isTrial) setS({ ...s, trial: { ...s.trial, footerText: v } })
    else setS({ ...s, footerText: v })
  }

  const center = {
    centerName: school?.name || 'Wunderkind',
    centerPhone: school?.phone || '+998 90 123 45 67',
    centerAddress: school?.address || 'Toshkent sh.',
    logoUrl: school?.logoUrl || '',
  }
  // "Mas'ul" — chekda to'lovni KIRITGAN xodim ismi chiqadi, shuning uchun namunada ham
  // joriy foydalanuvchi ko'rsatiladi (ilgari bu yerda o'ylab topilgan ism turardi).
  const responsibleName = user?.fullName || "Mas'ul xodim"
  const preview: ReceiptData = isTrial
    ? { ...sampleTrial, ...center, responsibleName }
    : { ...samplePayment, ...center, responsibleName }

  return (
    <Card
      title={
        <span className="flex items-center gap-2">
          <ReceiptIcon className="h-4 w-4 text-brand-600" /> To'lov cheki (kvitansiya)
        </span>
      }
    >
      <p className="mb-4 text-sm text-slate-400">
        <b>Termal chek</b> (58/80mm) ko'rinishini sozlang. Ikki tur bor:{' '}
        <b>To'lov cheki</b> (moliyada to'lov kiritilganda) va <b>Sinov darsi cheki</b> (lid sinov darsiga
        yozilganda — to'lovsiz). O'ngdagi namuna real vaqtda yangilanadi.
      </p>

      {/* Tur tanlash (tab) */}
      <div className="mb-5 inline-flex rounded-lg border border-slate-200 p-0.5">
        {(
          [
            { k: 'payment', label: "To'lov cheki" },
            { k: 'trial', label: 'Sinov darsi cheki' },
          ] as { k: Tab; label: string }[]
        ).map((t) => (
          <button
            key={t.k}
            type="button"
            onClick={() => setTab(t.k)}
            className={cn(
              'rounded-md px-3.5 py-1.5 text-sm font-medium transition-colors',
              tab === t.k ? 'bg-brand-600 text-white' : 'text-slate-600 hover:bg-slate-100',
            )}
          >
            {t.label}
          </button>
        ))}
      </div>

      <div className="grid gap-6 lg:grid-cols-[1fr_320px]">
        {/* Chap: sozlamalar */}
        <div className="space-y-5">
          <div>
            <h4 className="mb-2 text-xs font-bold uppercase tracking-wide text-slate-400">
              Sarlavha (ikkala chekka umumiy)
            </h4>
            <div className="space-y-2">
              <Toggle checked={s.showLogo} onChange={(v) => setS({ ...s, showLogo: v })} label="Markaz logotipi" />
              <Toggle checked={s.showName} onChange={(v) => setS({ ...s, showName: v })} label="Markaz nomi" />
            </div>
          </div>

          <div>
            <h4 className="mb-2 text-xs font-bold uppercase tracking-wide text-slate-400">
              Maydonlar — {isTrial ? 'Sinov darsi cheki' : "To'lov cheki"}
            </h4>
            <div className="grid gap-2 sm:grid-cols-2">
              {fieldList.map((f) => (
                <Toggle
                  key={f.key}
                  checked={activeFields[f.key]}
                  onChange={(v) => setField(f.key, v)}
                  label={f.label}
                />
              ))}
            </div>
            {!isTrial && (
              <p className="mt-2 text-xs text-slate-400">"Jami" (to'lov summasi) doim ko'rinadi.</p>
            )}
            {isTrial && (
              <p className="mt-2 text-xs text-slate-400">
                Sinov cheki to'lovsiz — "To'lov turi", "Izoh", "Jami" bo'lmaydi.
              </p>
            )}
          </div>

          <div>
            <h4 className="mb-2 text-xs font-bold uppercase tracking-wide text-slate-400">Past qismi</h4>
            <div className="space-y-3">
              <div>
                <label className="mb-1 block text-sm font-medium text-slate-700">
                  Pastki izoh — {isTrial ? 'Sinov darsi cheki' : "To'lov cheki"}
                </label>
                <Input
                  value={activeFooter}
                  onChange={(e) => setFooter(e.target.value)}
                  placeholder={isTrial ? 'Sinov darsiga xush kelibsiz!' : 'Tashrifingiz uchun rahmat!'}
                />
              </div>
              <Toggle
                checked={s.showContact}
                onChange={(v) => setS({ ...s, showContact: v })}
                label="Aloqa (markaz telefoni + manzili) — umumiy"
              />
              <Toggle
                checked={s.showQr}
                onChange={(v) => setS({ ...s, showQr: v })}
                label="QR kod — umumiy"
              />
              {s.showQr && (
                <div>
                  <label className="mb-1 block text-sm font-medium text-slate-700">QR matni / havola</label>
                  <Input
                    value={s.qrText}
                    onChange={(e) => setS({ ...s, qrText: e.target.value })}
                    placeholder="Bo'sh = markaz telefoni"
                  />
                </div>
              )}
            </div>
          </div>

          <div className="flex items-center gap-3">
            <Button onClick={save} disabled={status === 'saving'}>
              {status === 'saving' ? 'Saqlanmoqda...' : 'Saqlash'}
            </Button>
            {status === 'saved' && (
              <span className="inline-flex items-center gap-1 text-sm font-medium text-emerald-600">
                <Check className="h-4 w-4" /> Saqlandi
              </span>
            )}
          </div>
        </div>

        {/* O'ng: jonli namuna */}
        <div>
          <h4 className="mb-2 text-xs font-bold uppercase tracking-wide text-slate-400">
            Namuna — {isTrial ? 'Sinov darsi cheki' : "To'lov cheki"}
          </h4>
          <div className="flex justify-center rounded-xl bg-slate-100 py-5">
            <style>{receiptCss}</style>
            <div
              className="receipt shadow-md"
              dangerouslySetInnerHTML={{
                __html: receiptHtml(preview, resolveCheckSettings(s, isTrial)),
              }}
            />
          </div>
        </div>
      </div>
    </Card>
  )
}
