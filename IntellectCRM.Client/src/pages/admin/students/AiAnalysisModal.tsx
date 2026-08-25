import { useEffect, useMemo, useState } from 'react'
import { Sparkles, RefreshCw, FileDown, AlertCircle, Info } from 'lucide-react'
import {
  generateStudentAiAnalysis,
  type StudentAiAnalysisRecord,
} from '@/api/services/students'
import { Modal } from '@/components/ui/Modal'
import { Button } from '@/components/ui/Button'
import { AiAnalysisView } from './AiAnalysisView'
import { escapeHtml } from '@/lib/ai'

interface Props {
  open: boolean
  onClose: () => void
  studentId: string
  studentName: string
  /** Sahifadagi saqlangan tahlillar (eng yangisi birinchi). */
  records: StudentAiAnalysisRecord[]
  /** Yangi tahlil yaratilganda sahifa ro'yxatini yangilash uchun. */
  onGenerated: (rec: StudentAiAnalysisRecord) => void
}

function buildPrintHtml(rec: StudentAiAnalysisRecord, studentName: string): string {
  const r = rec.result
  const li = (arr: string[]) => arr.map((x) => `<li>${escapeHtml(x)}</li>`).join('')
  const b = r.baholar
  const row = (label: string, v: number) =>
    `<tr><td>${label}</td><td style="text-align:right;font-weight:bold">${v}</td></tr>`
  return `<!DOCTYPE html><html lang="uz"><head><meta charset="utf-8"><title>AI Tahlil — ${escapeHtml(studentName)}</title>
<style>
  body{font-family:'Times New Roman',Times,serif;color:#1e293b;margin:0;padding:40px 48px;line-height:1.6}
  .head{border-bottom:3px solid #6d28d9;padding-bottom:14px;margin-bottom:18px}
  .brand{color:#6d28d9;font-size:13px;letter-spacing:1px;text-transform:uppercase;font-weight:bold}
  h1{font-size:24px;margin:6px 0 2px}.meta{font-size:12px;color:#64748b}
  h2{font-size:17px;color:#4c1d95;margin:18px 0 6px;border-left:4px solid #a78bfa;padding-left:10px}
  table{border-collapse:collapse;width:280px;margin:6px 0}
  td{border:1px solid #e2e8f0;padding:4px 10px;font-size:14px}
  ul{margin:4px 0 10px;padding-left:22px}li{margin:3px 0}
  .big{font-size:40px;font-weight:bold;color:#6d28d9}
  .foot{margin-top:26px;border-top:1px solid #e2e8f0;padding-top:10px;font-size:11px;color:#94a3b8}
  @media print{body{padding:20px 24px}}
</style></head><body>
  <div class="head"><div class="brand">IntellectCRM · AI Tahlil</div>
    <h1>${escapeHtml(studentName)}</h1>
    <div class="meta">Sana: ${escapeHtml(rec.date)} · Model: ${escapeHtml(rec.model)} · Umumiy baho: <b>${b.umumiy}/100</b> · Trend: ${escapeHtml(r.trend)}</div>
  </div>
  <h2>Baholar</h2>
  <table>${row('Akademik', b.akademik)}${row('Davomat', b.davomat)}${row('Intizom', b.intizom)}${row('Uy vazifa', b.uyVazifa)}${row('Faollik', b.faollik)}${row('Umumiy', b.umumiy)}</table>
  ${r.umumiy ? `<h2>Umumiy holat</h2><p>${escapeHtml(r.umumiy)}</p>` : ''}
  ${r.ozgarishlar ? `<h2>Oldingi tahlilga nisbatan o'zgarishlar</h2><p>${escapeHtml(r.ozgarishlar)}</p>` : ''}
  ${r.dinamika ? `<h2>Dinamika</h2><p>${escapeHtml(r.dinamika)}</p>` : ''}
  ${r.kuchli.length ? `<h2>Kuchli tomonlari</h2><ul>${li(r.kuchli)}</ul>` : ''}
  ${r.zaif.length ? `<h2>Zaif tomonlari</h2><ul>${li(r.zaif)}</ul>` : ''}
  ${r.tavsiyalar.length ? `<h2>Tavsiyalar</h2><ul>${li(r.tavsiyalar)}</ul>` : ''}
  <div class="foot">Ushbu tahlil sun'iy intellekt (${escapeHtml(rec.model)}) tomonidan o'quvchi ma'lumotlari asosida yaratilgan. Yakuniy qarorlar pedagogik baholash bilan birga ko'rib chiqilsin.</div>
  <script>window.onload=function(){setTimeout(function(){window.print()},250)}</script>
</body></html>`
}

export function AiAnalysisModal({ open, onClose, studentId, studentName, records, onGenerated }: Props) {
  const todayTk = useMemo(
    () => new Date().toLocaleDateString('en-CA', { timeZone: 'Asia/Tashkent' }),
    [],
  )
  const latest = records[0] ?? null
  const hasToday = latest?.date === todayTk

  const [shown, setShown] = useState<StudentAiAnalysisRecord | null>(null)
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [info, setInfo] = useState<string | null>(null)

  useEffect(() => {
    if (open) {
      setShown(latest)
      setError(null)
      setInfo(null)
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [open])

  const generate = () => {
    setLoading(true)
    setError(null)
    setInfo(null)
    generateStudentAiAnalysis(studentId)
      .then((r) => {
        if (r.ok && r.record) {
          setShown(r.record)
          onGenerated(r.record)
          if (r.alreadyToday)
            setInfo("Bugun allaqachon tahlil qilingan. Keyingi tahlilni ertaga qilish mumkin.")
        } else {
          setError(r.error || "Tahlil qilib bo'lmadi.")
        }
      })
      .catch((e) => {
        setError(
          (e as { response?: { data?: { message?: string } } })?.response?.data?.message ||
            "Tahlil qilib bo'lmadi. Internet yoki API kalitini tekshiring.",
        )
      })
      .finally(() => setLoading(false))
  }

  const downloadPdf = () => {
    if (!shown) return
    const win = window.open('', '_blank', 'width=840,height=920')
    if (!win) {
      alert("Brauzer yangi oynani bloklab qo'ydi. Pop-up'ga ruxsat bering.")
      return
    }
    win.document.write(buildPrintHtml(shown, studentName))
    win.document.close()
    win.focus()
  }

  // Bugun tahlil qilingan bo'lsa qayta tahlil bloklanadi (kuniga bir marta).
  const blockedToday = hasToday && shown?.date === todayTk

  return (
    <Modal
      open={open}
      onClose={onClose}
      size="lg"
      title="AI Tahlil"
      footer={
        <>
          <Button variant="secondary" onClick={generate} disabled={loading || blockedToday}>
            <RefreshCw className={loading ? 'h-4 w-4 animate-spin' : 'h-4 w-4'} />
            {shown ? 'Yangi tahlil' : 'Tahlil qilish'}
          </Button>
          <Button onClick={downloadPdf} disabled={loading || !shown}>
            <FileDown className="h-4 w-4" /> PDF yuklab olish
          </Button>
        </>
      }
    >
      <div className="mb-3 flex items-center gap-2 text-sm text-slate-500">
        <Sparkles className="h-4 w-4 text-brand-600" />
        <span>
          <b className="text-slate-700">{studentName}</b> — barcha ma'lumotlar AI orqali tahlil qilinadi
          <span className="ml-1 text-xs text-slate-400">(kuniga bir marta)</span>
        </span>
      </div>

      {blockedToday && !info && (
        <div className="mb-3 flex items-start gap-2 rounded-lg bg-slate-50 px-3 py-2 text-xs text-slate-500">
          <Info className="mt-0.5 h-4 w-4 shrink-0" />
          <span>Bu o'quvchi bugun tahlil qilingan. Keyingi tahlil ertaga mumkin (eski tahlil saqlanib qoladi).</span>
        </div>
      )}
      {info && (
        <div className="mb-3 flex items-start gap-2 rounded-lg bg-blue-50 px-3 py-2 text-xs text-blue-700">
          <Info className="mt-0.5 h-4 w-4 shrink-0" />
          <span>{info}</span>
        </div>
      )}

      {loading && (
        <div className="flex flex-col items-center justify-center gap-3 py-14 text-slate-400">
          <RefreshCw className="h-7 w-7 animate-spin text-brand-500" />
          <p className="text-sm">AI o'quvchi ma'lumotlarini tahlil qilmoqda...</p>
          <p className="text-xs text-slate-400">Bu bir necha soniya olishi mumkin.</p>
        </div>
      )}

      {!loading && error && (
        <div className="flex items-start gap-3 rounded-xl border border-red-100 bg-red-50 px-4 py-3.5 text-sm text-red-700">
          <AlertCircle className="mt-0.5 h-5 w-5 shrink-0" />
          <div>
            <p className="font-semibold">Tahlil amalga oshmadi</p>
            <p className="mt-0.5 text-red-600">{error}</p>
          </div>
        </div>
      )}

      {!loading && !error && shown && <AiAnalysisView record={shown} />}

      {!loading && !error && !shown && (
        <div className="flex flex-col items-center justify-center gap-3 py-12 text-center text-slate-400">
          <Sparkles className="h-9 w-9 text-brand-300" />
          <p className="max-w-sm text-sm">
            Bu o'quvchi hali tahlil qilinmagan. <b className="text-slate-600">"Tahlil qilish"</b> tugmasini bosing —
            AI barcha ma'lumotlarni o'rganib, baholar, kuchli/zaif tomonlar va tavsiyalarni chiqaradi.
          </p>
        </div>
      )}
    </Modal>
  )
}
