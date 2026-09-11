import { useEffect, useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { getStudentFinance, type StudentFinance, type MonthLedger } from '@/api/services/studentPortal'
import { Icon, fmtMoney, fmtDate, fmtMonth } from '@/pages/student/lib'

/* ============================================================
   O'quvchi portali — To'lovlar ekrani.
   Balans hero (qarz=qizil / balans=yashil), jami to'langan/hisoblangan,
   chegirma, oylar bo'yicha hisob, to'lovlar tarixi.
   To'lov gateway YO'Q — tugma faqat "tez orada" sheet ko'rsatadi.
   ============================================================ */

const statusOf = (s: string) =>
  s === 'paid'
    ? { label: 'To‘langan', color: 'var(--green)', icon: 'checkCircle' }
    : s === 'partial'
      ? { label: 'Qisman', color: 'var(--amber)', icon: 'clock' }
      : { label: 'To‘lanmagan', color: 'var(--red)', icon: 'alert' }

export function StudentFinanceScreen() {
  const navigate = useNavigate()
  const [data, setData] = useState<StudentFinance | null>(null)
  const [err, setErr] = useState<string | null>(null)
  const [sheet, setSheet] = useState(false)
  const [toast, setToast] = useState<string | null>(null)

  useEffect(() => {
    let on = true
    getStudentFinance()
      .then((d) => on && setData(d))
      .catch((e) => on && setErr(e?.message || String(e)))
    return () => {
      on = false
    }
  }, [])

  useEffect(() => {
    if (!toast) return
    const t = setTimeout(() => setToast(null), 2500)
    return () => clearTimeout(t)
  }, [toast])

  const head = (
    <div className="hd">
      <div className="row gap10" style={{ minHeight: 38 }}>
        <button className="iconbtn press" onClick={() => navigate(-1)}>
          <Icon name="chevL" size={22} />
        </button>
        <div className="hd-sm" style={{ flex: 1, overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }}>
          To‘lovlar
        </div>
      </div>
    </div>
  )

  if (err) {
    return (
      <div className="screen">
        {head}
        <div className="center">
          <Empty title="Yuklab bo'lmadi" sub={err} ic="alert" />
        </div>
      </div>
    )
  }
  if (!data) {
    return (
      <div className="screen">
        {head}
        <div className="center">
          <div className="spin" />
        </div>
      </div>
    )
  }

  const debt = data.balance < 0
  const months = data.months || []
  const payments = data.payments || []

  const total = (label: string, value: string, color: string) => (
    <div className="card" style={{ flex: 1, padding: 14, borderRadius: 16 }}>
      <div className="muted" style={{ fontSize: 12, fontWeight: 700 }}>
        {label}
      </div>
      <div style={{ fontSize: 18, fontWeight: 800, color, whiteSpace: 'nowrap', overflow: 'hidden', textOverflow: 'ellipsis', marginTop: 3 }}>
        {value}
      </div>
    </div>
  )

  return (
    <div className="screen">
      {head}
      <div className="scroll" style={{ paddingBottom: 24 }}>
        <div className="pad">
          {/* Balans hero */}
          <div
            style={{
              borderRadius: 22,
              padding: 20,
              color: '#fff',
              boxShadow: `0 14px 34px rgba(${debt ? '239,68,68' : '22,163,74'},.3)`,
              background: `linear-gradient(135deg,${debt ? '#EF4444,#B91C1C' : '#16A34A,#15803D'})`,
            }}
          >
            <div style={{ fontSize: 13, fontWeight: 700, opacity: 0.85 }}>{debt ? 'Joriy qarz' : 'Balans'}</div>
            <div style={{ fontSize: 34, fontWeight: 800, letterSpacing: '-.5px', marginTop: 4 }}>
              {fmtMoney(Math.abs(data.balance))}
              <span style={{ fontSize: 18 }}> so‘m</span>
            </div>
            <div style={{ fontSize: 13, fontWeight: 600, opacity: 0.85, marginTop: 2 }}>
              Oylik to‘lov: {fmtMoney(data.monthlyFee)} so‘m
            </div>
          </div>

          {debt && (
            <>
              <div style={{ height: 16 }} />
              <button className="btn btn-primary btn-lg press" onClick={() => setSheet(true)}>
                <Icon name="wallet" size={18} />
                <span>To‘lovni amalga oshirish</span>
              </button>
            </>
          )}

          <div style={{ height: 16 }} />
          <div className="row gap10">
            {total('Jami to‘langan', fmtMoney(data.totalPaid), 'var(--green)')}
            {total('Jami hisoblangan', fmtMoney(data.totalCharged), 'var(--text)')}
          </div>

          {data.totalDiscount > 0 && (
            <>
              <div style={{ height: 10 }} />
              <div className="card row gap12" style={{ borderRadius: 16 }}>
                <div
                  style={{
                    width: 40,
                    height: 40,
                    borderRadius: 11,
                    background: 'color-mix(in srgb,var(--violet) 13%,transparent)',
                    display: 'flex',
                    alignItems: 'center',
                    justifyContent: 'center',
                    flex: 'none',
                  }}
                >
                  <Icon name="award" size={20} color="var(--violet)" />
                </div>
                <div style={{ flex: 1 }}>
                  <div className="muted" style={{ fontSize: 12, fontWeight: 700 }}>
                    Chegirma
                  </div>
                  <div className="faint" style={{ fontSize: 12.5 }}>
                    Jami olingan chegirma
                  </div>
                </div>
                <div style={{ fontSize: 16, fontWeight: 800, color: 'var(--violet)' }}>−{fmtMoney(data.totalDiscount)} so‘m</div>
              </div>
            </>
          )}

          {/* Oylar bo'yicha */}
          <div style={{ height: 16 }} />
          <div className="sh">
            <div className="sh-title">Oylar bo‘yicha</div>
          </div>
          <div className="card" style={{ padding: 4 }}>
            {months.length ? (
              months.map((m, i) => <MonthRow key={m.month + '_' + i} m={m} border={i < months.length - 1} />)
            ) : (
              <Empty title="Ma'lumot yo'q" ic="wallet" />
            )}
          </div>

          {/* To'lovlar tarixi */}
          <div style={{ height: 16 }} />
          <div className="sh">
            <div className="sh-title">To‘lovlar tarixi</div>
          </div>
          {payments.length ? (
            <div className="card" style={{ padding: 4 }}>
              {payments.map((p, i) => (
                <div
                  key={p.date + '_' + i}
                  className="row gap12"
                  style={{ padding: '12px 11px', borderBottom: i < payments.length - 1 ? '1px solid var(--border)' : undefined }}
                >
                  <div
                    style={{
                      width: 38,
                      height: 38,
                      borderRadius: 11,
                      background: 'var(--greenSoft)',
                      display: 'flex',
                      alignItems: 'center',
                      justifyContent: 'center',
                      flex: 'none',
                    }}
                  >
                    <Icon name="download" size={18} color="var(--green)" />
                  </div>
                  <div style={{ flex: 1, minWidth: 0 }}>
                    <div style={{ fontSize: 14.5, fontWeight: 700 }}>{fmtDate(p.date)}</div>
                    {(p.note || p.comment) && (
                      <div className="muted" style={{ fontSize: 12 }}>
                        {p.note || p.comment}
                      </div>
                    )}
                  </div>
                  <div style={{ fontSize: 15, fontWeight: 800, color: 'var(--green)' }}>+{fmtMoney(p.amount)}</div>
                </div>
              ))}
            </div>
          ) : (
            <div className="card">
              <Empty title="To'lovlar yo'q" sub="Hozircha to'lov qayd etilmagan." ic="wallet" />
            </div>
          )}
        </div>
      </div>

      {sheet && (
        <PaymentSheet
          onClose={() => setSheet(false)}
          onMethod={(name) => {
            setSheet(false)
            setToast(`${name} orqali to‘lov tez orada qo‘shiladi`)
          }}
        />
      )}
      {toast && <div className="toast">{toast}</div>}
    </div>
  )
}

function MonthRow({ m, border }: { m: MonthLedger; border: boolean }) {
  const sm = statusOf(m.status)
  return (
    <div className="row gap12" style={{ padding: '12px 11px', borderBottom: border ? '1px solid var(--border)' : undefined }}>
      <div
        style={{
          width: 40,
          height: 40,
          borderRadius: 11,
          background: `color-mix(in srgb,${sm.color} 13%,transparent)`,
          display: 'flex',
          alignItems: 'center',
          justifyContent: 'center',
          flex: 'none',
        }}
      >
        <Icon name={sm.icon} size={20} color={sm.color} />
      </div>
      <div style={{ flex: 1, minWidth: 0 }}>
        <div style={{ fontSize: 14.5, fontWeight: 700 }}>{fmtMonth(m.month)}</div>
        <div className="muted" style={{ fontSize: 12 }}>
          {fmtMoney(m.paid)} / {fmtMoney(m.charged)} so‘m
        </div>
        {(m.courses || []).length > 0 && (
          <div className="faint" style={{ fontSize: 11.5 }}>
            {m.courses.map((c) => `${c.courseName} · ${fmtMoney(c.fee)}`).join(' , ')}
          </div>
        )}
        {m.discount > 0 && (
          <div style={{ fontSize: 11.5, fontWeight: 700, color: 'var(--violet)' }}>Chegirma: −{fmtMoney(m.discount)} so‘m</div>
        )}
      </div>
      <span
        className="chip"
        style={{ color: sm.color, background: `color-mix(in srgb,${sm.color} 12%,transparent)`, fontSize: 11, flex: 'none' }}
      >
        {sm.label}
      </span>
    </div>
  )
}

function PaymentSheet({ onClose, onMethod }: { onClose: () => void; onMethod: (name: string) => void }) {
  const methods: [string, string][] = [
    ['Click', '#3B82F6'],
    ['Payme', '#00CDB6'],
    ['Uzum', '#7C3AED'],
  ]
  return (
    <div className="scrim" onClick={(e) => e.target === e.currentTarget && onClose()}>
      <div className="sheet">
        <div className="grab" />
        <div style={{ fontSize: 19, fontWeight: 800, letterSpacing: '-.3px', marginBottom: 14 }}>To‘lov usuli</div>
        {methods.map((m) => (
          <button
            key={m[0]}
            className="card press row gap12"
            onClick={() => onMethod(m[0])}
            style={{ width: '100%', borderRadius: 15, padding: 14, marginBottom: 10, textAlign: 'left' }}
          >
            <div
              style={{
                width: 42,
                height: 42,
                borderRadius: 12,
                background: m[1],
                color: '#fff',
                display: 'flex',
                alignItems: 'center',
                justifyContent: 'center',
                fontSize: 18,
                fontWeight: 800,
                flex: 'none',
              }}
            >
              {m[0][0]}
            </div>
            <div style={{ flex: 1, fontSize: 15.5, fontWeight: 700 }}>{m[0]} orqali to‘lash</div>
            <Icon name="chevR" size={24} color="var(--faint)" />
          </button>
        ))}
      </div>
    </div>
  )
}

function Empty({ title, sub, ic = 'sparkle' }: { title: string; sub?: string; ic?: string }) {
  return (
    <div className="empty">
      <div className="empty-ic">
        <Icon name={ic} size={30} />
      </div>
      <div style={{ fontSize: 16, fontWeight: 800 }}>{title}</div>
      {sub && (
        <div className="muted" style={{ fontSize: 13.5, marginTop: 6, lineHeight: 1.5 }}>
          {sub}
        </div>
      )}
    </div>
  )
}
