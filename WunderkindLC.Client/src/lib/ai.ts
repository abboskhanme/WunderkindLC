import { Minus, TrendingDown, TrendingUp } from 'lucide-react'

/** AI tahlil bo'limlarining umumiy yordamchilari (komponent emas — rang/format/chop etish). */

/** 0-100 ball rangi (yashil → ko'k → sariq → qizil). */
export function scoreColor(v: number): string {
  if (v >= 80) return '#16a34a'
  if (v >= 60) return '#2563eb'
  if (v >= 40) return '#f59e0b'
  return '#dc2626'
}

/** AI "trend" matnidan yorliq + rang + ikon. */
export function trendInfo(trend: string): { label: string; cls: string; Icon: typeof TrendingUp } {
  const t = (trend || '').toLowerCase()
  if (t.includes('yaxshi'))
    return { label: 'Yaxshilanmoqda', cls: 'bg-emerald-50 text-emerald-700', Icon: TrendingUp }
  if (t.includes('yomon'))
    return { label: 'Yomonlashmoqda', cls: 'bg-red-50 text-red-700', Icon: TrendingDown }
  return { label: 'Barqaror', cls: 'bg-slate-100 text-slate-600', Icon: Minus }
}

/** PDF (chop etish) oynasi uchun HTML'ga xavfsiz matn (barcha 5 belgi: & < > " '). */
export function escapeHtml(s: string): string {
  return (s ?? '').replace(
    /[&<>"']/g,
    (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' })[c] as string,
  )
}

/** Tayyor HTML'ni yangi oynada ochib chop etish (PDF sifatida saqlash uchun). */
export function openPrintWindow(html: string): void {
  const win = window.open('', '_blank', 'width=840,height=920')
  if (!win) {
    alert("Brauzer yangi oynani bloklab qo'ydi. Pop-up'ga ruxsat bering.")
    return
  }
  win.document.write(html)
  win.document.close()
  win.focus()
}

/** AI hisobotlari uchun umumiy chop etish uslubi (o'quvchi/o'qituvchi/guruh — bir xil). */
export const printCss = `
  body{font-family:'Times New Roman',Times,serif;color:#1e293b;margin:0;padding:40px 48px;line-height:1.6}
  .head{border-bottom:3px solid #6d28d9;padding-bottom:14px;margin-bottom:18px}
  .brand{color:#6d28d9;font-size:13px;letter-spacing:1px;text-transform:uppercase;font-weight:bold}
  h1{font-size:24px;margin:6px 0 2px}.meta{font-size:12px;color:#64748b}
  h2{font-size:17px;color:#4c1d95;margin:18px 0 6px;border-left:4px solid #a78bfa;padding-left:10px}
  table{border-collapse:collapse;width:340px;margin:6px 0}
  td{border:1px solid #e2e8f0;padding:4px 10px;font-size:14px}
  ul{margin:4px 0 10px;padding-left:22px}li{margin:3px 0}
  .foot{margin-top:26px;border-top:1px solid #e2e8f0;padding-top:10px;font-size:11px;color:#94a3b8}
  @media print{body{padding:20px 24px}}
`
