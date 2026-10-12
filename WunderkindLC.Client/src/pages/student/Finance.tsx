import { useMemo, useState } from 'react'
import { Banknote, ChevronRight, CircleCheck, CreditCard, Landmark, Percent, Receipt, Wallet } from 'lucide-react'
import { getStudentFinance, type MonthLedger, type StudentPayment } from '@/api/services/studentPortal'
import { fmtDate, fmtMoney, fmtMonth } from '@/pages/student/lib'
import { balanceState, monthStatus, paymentView, toneColors } from '@/pages/student/format'
import { useApi, useToast } from '@/pages/student/hooks'
import { EmptyState, ErrorState, IconBox, PageHeader, SectionTitle, Skeleton, SkeletonList, StatusChip, Toast } from '@/pages/student/ui'

/* ============================================================
   TO'LOVLAR — balance status (debt vs paid, crystal clear), totals, month-by-month ledger
   (newest first) and the payment history as clean lines: "O'qish to'lovi · Sentabr 2026",
   course + method, amount. The server's automatic note ("O'quvchi to'lovi (2026-09) […] — Name")
   is never shown raw. There is NO payment gateway yet — the pay button opens the provider sheet
   that says the provider is coming soon (unchanged behaviour).
   ============================================================ */

const loadFinance = () => getStudentFinance()

const METHOD_ICON = { cash: Banknote, card: CreditCard, bank: Landmark } as const

export function StudentFinanceScreen() {
  const { data, error, loading, reload } = useApi(loadFinance)
  const [sheet, setSheet] = useState(false)
  const [toast, setToast] = useToast()

  const months = useMemo(() => [...(data?.months ?? [])].sort((a, b) => b.month.localeCompare(a.month)), [data])
  const payments = useMemo(() => [...(data?.payments ?? [])].sort((a, b) => (b.date || '').localeCompare(a.date || '')), [data])

  return (
    <div className="screen">
      <PageHeader title="To'lovlar" />
      <div className="scroll">
        {error && !data ? (
          <ErrorState message={error} onRetry={reload} />
        ) : loading && !data ? (
          <div className="st-page">
            <div className="card">
              <Skeleton h={14} w="40%" />
              <Skeleton h={30} w="65%" style={{ marginTop: 10 }} />
              <Skeleton h={12} w="50%" style={{ marginTop: 10 }} />
              <Skeleton h={48} w="100%" r={14} style={{ marginTop: 16 }} />
            </div>
            <div style={{ height: 22 }} />
            <SkeletonList rows={3} />
          </div>
        ) : data ? (
          <div className="st-page">
            <BalanceCard balance={data.balance} monthlyFee={data.monthlyFee} onPay={() => setSheet(true)} />

            {/* Totals */}
            <div className={data.totalDiscount > 0 ? 'st-kpis' : 'st-kpis two'} style={{ marginTop: 12 }}>
              <Total label="Hisoblangan" value={data.totalCharged} />
              <Total label="To'langan" value={data.totalPaid} color="var(--green)" />
              {data.totalDiscount > 0 && <Total label="Chegirma" value={data.totalDiscount} color="var(--violet)" minus />}
            </div>

            {/* Month by month */}
            <SectionTitle title="Oylar bo'yicha" sub="Har oy uchun hisoblangan va to'langan summa" />
            {months.length ? (
              <div className="card flush">
                {months.map((m, i) => (
                  <MonthRow key={m.month + '_' + i} m={m} />
                ))}
              </div>
            ) : (
              <div className="card flush">
                <EmptyState compact icon={Receipt} title="Hali hisob yo'q" sub="Oylik to'lov hisoblangach shu yerda ko'rinadi." />
              </div>
            )}

            {/* Payment history */}
            <SectionTitle title="To'lovlar tarixi" />
            {payments.length ? (
              <div className="card flush">
                {payments.map((p, i) => (
                  <PaymentRow key={p.date + '_' + i} p={p} />
                ))}
              </div>
            ) : (
              <div className="card flush">
                <EmptyState compact icon={Wallet} title="To'lovlar yo'q" sub="Hozircha to'lov qayd etilmagan." />
              </div>
            )}
          </div>
        ) : null}
      </div>

      {sheet && (
        <PaymentSheet
          onClose={() => setSheet(false)}
          onMethod={(name) => {
            setSheet(false)
            setToast(`${name} orqali to'lov tez orada qo'shiladi`)
          }}
        />
      )}
      <Toast text={toast} />
    </div>
  )
}

function BalanceCard({ balance, monthlyFee, onPay }: { balance: number; monthlyFee: number; onPay: () => void }) {
  const st = balanceState(balance)
  const c = toneColors(st.tone)
  const debt = balance < 0
  return (
    <div className="card" style={{ background: c.bg, borderColor: 'transparent', boxShadow: 'none', padding: 18 }}>
      <div className="row gap10" style={{ color: c.fg }}>
        {debt ? <Wallet size={20} /> : <CircleCheck size={20} />}
        <span style={{ fontSize: 15, fontWeight: 700 }}>{st.label}</span>
      </div>
      <div className="st-num" style={{ fontSize: 34, lineHeight: 1.15, marginTop: 8, color: debt ? c.fg : 'var(--text)', overflowWrap: 'anywhere' }}>
        {st.amount > 0 ? fmtMoney(st.amount) : '0'}
        <span style={{ fontSize: 18, fontWeight: 700, marginLeft: 6 }}>so'm</span>
      </div>
      {!debt && balance === 0 && <div style={{ fontSize: 14, color: 'var(--muted)', marginTop: 2 }}>Barcha hisoblangan oylar to'langan.</div>}
      {monthlyFee > 0 && (
        <div style={{ fontSize: 14, color: 'var(--muted)', marginTop: 4 }}>
          Oylik to'lov: <b style={{ color: 'var(--text)' }}>{fmtMoney(monthlyFee)} so'm</b>
        </div>
      )}
      {debt && (
        <button className="btn btn-primary btn-lg press" onClick={onPay} style={{ marginTop: 16 }}>
          <Wallet size={19} />
          To'lash
        </button>
      )}
    </div>
  )
}

function Total({ label, value, color, minus }: { label: string; value: number; color?: string; minus?: boolean }) {
  return (
    <div className="st-kpi">
      <div className="st-kpi-l" style={{ marginTop: 0 }}>{label}</div>
      <div className="st-kpi-v" style={{ fontSize: 18, marginTop: 4, color: color ?? 'var(--text)' }}>
        {minus ? '−' : ''}
        {fmtMoney(value)}
      </div>
      <div className="st-kpi-s">so'm</div>
    </div>
  )
}

function MonthRow({ m }: { m: MonthLedger }) {
  const st = monthStatus(m)
  const c = toneColors(st.tone)
  const due = Math.max(0, m.charged - m.discount)
  const pct = due > 0 ? Math.min(100, Math.round((m.paid / due) * 100)) : 100
  const courses = (m.courses || []).map((x) => (x.courseName || x.groupName || '').trim()).filter(Boolean)
  return (
    <div className="st-li" style={{ alignItems: 'flex-start', flexDirection: 'column', gap: 8 }}>
      <div className="row gap10" style={{ width: '100%', alignItems: 'flex-start' }}>
        <div className="st-li-main">
          <div className="st-li-title">{fmtMonth(m.month)}</div>
          {courses.length > 0 && <div className="st-li-sub">{[...new Set(courses)].join(' · ')}</div>}
        </div>
        <StatusChip tone={st.tone}>{st.label}</StatusChip>
      </div>
      <div style={{ width: '100%' }}>
        <div className="progress thin">
          <div style={{ width: `${pct}%`, background: st.tone === 'ok' ? 'var(--greenBar)' : st.tone === 'warn' ? 'var(--amberBar)' : c.fg }} />
        </div>
        <div className="row sp" style={{ marginTop: 6, fontSize: 13, color: 'var(--muted)' }}>
          <span>
            To'langan <b className="st-num" style={{ color: 'var(--text)', fontWeight: 700 }}>{fmtMoney(m.paid)}</b>
          </span>
          <span>
            Oylik <b className="st-num" style={{ color: 'var(--text)', fontWeight: 700 }}>{fmtMoney(due)}</b>
          </span>
        </div>
        {m.discount > 0 && (
          <div style={{ fontSize: 12.5, fontWeight: 700, color: 'var(--violet)', marginTop: 4 }} className="row gap6">
            <Percent size={13} /> Chegirma: −{fmtMoney(m.discount)} so'm
          </div>
        )}
      </div>
    </div>
  )
}

function PaymentRow({ p }: { p: StudentPayment }) {
  const v = paymentView(p)
  const Ic = METHOD_ICON[(p.method || '') as keyof typeof METHOD_ICON] ?? Receipt
  return (
    <div className="st-li" style={{ alignItems: 'flex-start' }}>
      <IconBox icon={Ic} tone="ok" />
      <div className="st-li-main">
        <div className="st-li-title">{v.title}</div>
        {v.sub && <div className="st-li-sub">{v.sub}</div>}
        {v.note && (
          <div className="st-li-sub" style={{ fontStyle: 'italic' }}>
            {v.note}
          </div>
        )}
      </div>
      <div className="st-li-end">
        <div className="st-num" style={{ fontSize: 16, color: 'var(--green)' }}>+{fmtMoney(p.amount)}</div>
        <div className="faint" style={{ fontSize: 12.5, marginTop: 2 }}>{fmtDate(p.date)}</div>
      </div>
    </div>
  )
}

function PaymentSheet({ onClose, onMethod }: { onClose: () => void; onMethod: (name: string) => void }) {
  const methods: [string, string][] = [
    ['Click', '#0080ff'],
    ['Payme', '#00b4b0'],
    ['Uzum', '#7c3aed'],
  ]
  return (
    <div className="scrim" onClick={(e) => e.target === e.currentTarget && onClose()}>
      <div className="sheet" role="dialog" aria-label="To'lov usuli">
        <div className="grab" />
        <div style={{ fontSize: 19, fontWeight: 800, letterSpacing: '-0.3px' }}>To'lov usuli</div>
        <div className="muted" style={{ fontSize: 14, marginTop: 4, marginBottom: 14 }}>
          Onlayn to'lov tez orada qo'shiladi. Hozircha to'lov markazda qabul qilinadi.
        </div>
        <div className="card flush">
          {methods.map(([name, color]) => (
            <button key={name} type="button" className="st-li press" onClick={() => onMethod(name)}>
              <div className="st-ic" style={{ background: color, color: '#fff', fontSize: 18, fontWeight: 800 }}>
                {name[0]}
              </div>
              <span className="st-li-main" style={{ fontSize: 15.5, fontWeight: 700 }}>
                {name} orqali to'lash
              </span>
              <ChevronRight size={20} color="var(--faint)" />
            </button>
          ))}
        </div>
      </div>
    </div>
  )
}
