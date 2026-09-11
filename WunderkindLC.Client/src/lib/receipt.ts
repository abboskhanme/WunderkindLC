/**
 * To'lov cheki (termal kvitansiya) — HTML chizish + bosib chiqarish.
 * Bir xil HTML ham jonli ko'rib chiqish (preview), ham print uchun ishlatiladi —
 * shuning uchun preview va chop etilgan chek doim mos keladi.
 *
 * Format: 80mm termal chek (58mm da ham normal — kenglik foizli/auto).
 */
import { paymentMethodLabel } from '@/config/constants'
import type { CheckSettings } from '@/config/checkSettings'

/** Chek ma'lumotlari (backend /admin/finance/receipt/{id} bilan mos). */
export interface ReceiptData {
  receiptNo: string
  dateTime: string
  studentName: string
  teacherName: string
  responsibleName: string
  groupName: string
  method: string
  comment?: string | null
  /** To'lov summasi. null/undefined = to'lovsiz chek (lid sinov varaqasi) — "Jami" ko'rsatilmaydi. */
  total?: number | null
  centerName: string
  centerPhone: string
  centerAddress: string
  logoUrl: string
  /** Sarlavha ostidagi kichik izoh, masalan "Sinov darsiga yozildi". */
  subtitle?: string | null
  /** QOG'OZ kvitansiya raqami ("KV...") — naqd to'lovda kiritilgan bo'lsa chekda ham chiqadi. */
  kvNo?: string | null
}

/** Pul: "200,000 so'm" (chekda — minglik ajratkichi vergul, misoldagidek). */
function money(v: number): string {
  return Math.round(v).toLocaleString('en-US') + " so'm"
}

function esc(s: string): string {
  return (s ?? '').replace(/[&<>"']/g, (c) =>
    ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' })[c] as string,
  )
}

/** Bitta "Nom: Qiymat" qatori (qiymat bo'sh bo'lsa — qator chiqmaydi). */
function row(label: string, value: string): string {
  if (!value) return ''
  return `<div class="r"><span class="k">${esc(label)}</span><span class="v">${esc(value)}</span></div>`
}

/** Chek ICHKI HTML'i (inline stil — iframe/preview uchun mustaqil). */
export function receiptHtml(d: ReceiptData, s: CheckSettings): string {
  const f = s.fields
  const header = `
    <div class="hdr">
      ${s.showLogo && d.logoUrl ? `<img class="logo" src="${esc(d.logoUrl)}" alt="" />` : ''}
      ${s.showName && d.centerName ? `<div class="name">${esc(d.centerName)}</div>` : ''}
      ${d.subtitle ? `<div class="sub">${esc(d.subtitle)}</div>` : ''}
    </div>`

  const rows = [
    f.receiptNo ? row('Id', d.receiptNo) : '',
    f.datetime ? row('Vaqt', d.dateTime) : '',
    f.student ? row("O'quvchi", d.studentName) : '',
    f.teacher ? row("O'qituvchi", d.teacherName) : '',
    f.responsible ? row("Mas'ul", d.responsibleName) : '',
    f.group ? row('Guruh', d.groupName) : '',
    f.method ? row("To'lov turi", paymentMethodLabel(d.method)) : '',
    // Qog'oz kvitansiya raqami — kiritilgan bo'lsa har doim chiqadi (sozlamada alohida bayroq yo'q).
    row('Kvitansiya', d.kvNo ?? ''),
    f.comment ? row('Izoh', d.comment ?? '') : '',
  ].join('')

  const qrSrc = s.showQr
    ? `https://api.qrserver.com/v1/create-qr-code/?size=120x120&data=${encodeURIComponent(s.qrText || d.centerPhone || d.centerName)}`
    : ''

  const contact = s.showContact
    ? `<div class="contact">${[d.centerPhone, d.centerAddress].filter(Boolean).map(esc).join('<br/>')}</div>`
    : ''

  const totalBlock =
    d.total != null
      ? `<div class="total"><span>Jami</span><span>${esc(money(d.total))}</span></div>
         <div class="sep"></div>`
      : ''

  return `
    ${header}
    <div class="sep"></div>
    <div class="rows">${rows}</div>
    <div class="sep"></div>
    ${totalBlock}
    ${qrSrc ? `<div class="qr"><img src="${qrSrc}" alt="QR" /></div>` : ''}
    ${contact}
    ${s.footerText ? `<div class="footer">${esc(s.footerText)}</div>` : ''}
  `
}

/** Chek uchun termal CSS (preview konteyneri va print iframe — ikkalasida ham). */
export const receiptCss = `
  .receipt { width: 280px; margin: 0 auto; background: #fff; color: #000;
    font-family: 'Courier New', ui-monospace, monospace; font-size: 12px; line-height: 1.45;
    padding: 10px 12px; }
  .receipt .hdr { text-align: center; }
  .receipt .logo { max-width: 96px; max-height: 96px; object-fit: contain; margin: 0 auto 4px; display: block; }
  .receipt .name { font-size: 16px; font-weight: 700; letter-spacing: .5px; }
  .receipt .sub { font-size: 11px; font-weight: 600; margin-top: 2px; }
  .receipt .sep { border-top: 1px dashed #000; margin: 7px 0; }
  .receipt .r { display: flex; justify-content: space-between; gap: 8px; margin: 2px 0; }
  .receipt .r .k { color: #000; white-space: nowrap; }
  .receipt .r .v { text-align: right; font-weight: 600; word-break: break-word; }
  .receipt .total { display: flex; justify-content: space-between; font-size: 14px; font-weight: 700; }
  .receipt .qr { text-align: center; margin: 6px 0; }
  .receipt .qr img { width: 110px; height: 110px; }
  .receipt .contact { text-align: center; font-size: 11px; margin-top: 4px; }
  .receipt .footer { text-align: center; margin-top: 6px; font-weight: 600; }
`

/** Chekni bosib chiqaradi (yashirin iframe — ilova CSS'iga ta'sir qilmaydi, popup bloklamaydi). */
export function printReceipt(d: ReceiptData, s: CheckSettings): void {
  const html = `<!doctype html><html><head><meta charset="utf-8" />
    <style>
      @page { size: 80mm auto; margin: 0; }
      html, body { margin: 0; padding: 0; background: #fff; }
      ${receiptCss}
      .receipt { width: 100%; box-sizing: border-box; }
    </style></head>
    <body><div class="receipt">${receiptHtml(d, s)}</div></body></html>`

  const iframe = document.createElement('iframe')
  iframe.style.position = 'fixed'
  iframe.style.right = '0'
  iframe.style.bottom = '0'
  iframe.style.width = '0'
  iframe.style.height = '0'
  iframe.style.border = '0'
  document.body.appendChild(iframe)

  const doc = iframe.contentWindow?.document
  if (!doc) {
    document.body.removeChild(iframe)
    return
  }
  doc.open()
  doc.write(html)
  doc.close()

  const win = iframe.contentWindow!
  const cleanup = () => {
    // Print dialog yopilgach iframe'ni olib tashlaymiz.
    setTimeout(() => {
      if (iframe.parentNode) iframe.parentNode.removeChild(iframe)
    }, 500)
  }
  // Rasm/QR yuklanishini biroz kutib, so'ng print.
  setTimeout(() => {
    win.focus()
    win.print()
    cleanup()
  }, 300)
}
