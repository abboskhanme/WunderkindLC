import { useEffect, useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { FileText } from 'lucide-react'
import type { ContractDoc } from '@/types'
import { getStudentContracts } from '@/api/services/studentPortal'
import { Icon, fmtDate } from '@/pages/student/lib'

/* ============================================================
   O'quvchi portali — Shartnoma ekrani.
   Tuzilgan shartnomalar ro'yxati; kartochka bosilsa PDF yangi tabda ochiladi.
   Dizayn: Certificates.tsx bilan bir xil (.student-app shell).
   ============================================================ */

const ACCENT = '#2563EB'
const ACCENT_SOFT = '#DBEAFE'

/** Ochish uchun havola: yuklangan PDF, bo'lmasa auth'li stream. */
function docUrl(doc: ContractDoc): string {
  return doc.pdfUrl || `/api/student/contracts/${doc.id}/pdf`
}

function ContractCard({ doc }: { doc: ContractDoc }) {
  const open = () => window.open(docUrl(doc), '_blank', 'noopener')

  return (
    <button
      className="card press"
      onClick={open}
      style={{
        width: '100%',
        textAlign: 'left',
        borderRadius: 18,
        padding: '16px 16px 14px',
        marginBottom: 12,
        borderLeft: `4px solid ${ACCENT}`,
      }}
    >
      {/* Sarlavha qatori */}
      <div className="row gap12" style={{ alignItems: 'flex-start', marginBottom: 10 }}>
        <div
          style={{
            width: 44,
            height: 44,
            borderRadius: 12,
            background: ACCENT_SOFT,
            display: 'flex',
            alignItems: 'center',
            justifyContent: 'center',
            flex: 'none',
          }}
        >
          <FileText size={24} color={ACCENT} strokeWidth={2} />
        </div>
        <div style={{ flex: 1, minWidth: 0 }}>
          <div
            style={{
              fontSize: 15,
              fontWeight: 800,
              letterSpacing: '-.2px',
              lineHeight: 1.25,
              marginBottom: 3,
            }}
          >
            {doc.title || `Shartnoma № ${doc.number}`}
          </div>
          <div className="muted" style={{ fontSize: 12.5 }}>
            {doc.templateName || 'Shartnoma'}
          </div>
        </div>
      </div>

      {/* Raqam va sana */}
      <div className="row gap12" style={{ marginBottom: 12 }}>
        <div style={{ flex: 1 }}>
          <div className="faint" style={{ fontSize: 11, fontWeight: 700, marginBottom: 2 }}>
            SHARTNOMA RAQAMI
          </div>
          <div style={{ fontSize: 13.5, fontWeight: 700 }}>№ {doc.number}</div>
        </div>
        <div style={{ flex: 1 }}>
          <div className="faint" style={{ fontSize: 11, fontWeight: 700, marginBottom: 2 }}>
            TUZILGAN SANA
          </div>
          <div style={{ fontSize: 13.5, fontWeight: 700 }}>{fmtDate(doc.date)}</div>
        </div>
      </div>

      {/* Ochish */}
      <div
        className="btn btn-primary"
        style={{
          display: 'flex',
          alignItems: 'center',
          justifyContent: 'center',
          gap: 6,
          fontSize: 13.5,
          fontWeight: 700,
          borderRadius: 12,
          padding: '10px 14px',
        }}
      >
        <Icon name="file" size={16} />
        <span>PDF'ni ochish</span>
      </div>
    </button>
  )
}

export function StudentContractsScreen() {
  const navigate = useNavigate()
  const [docs, setDocs] = useState<ContractDoc[]>([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    let alive = true
    getStudentContracts()
      .then((d) => alive && setDocs(d))
      .catch((e) => alive && setError(e?.message || String(e)))
      .finally(() => alive && setLoading(false))
    return () => {
      alive = false
    }
  }, [])

  const head = (
    <div className="hd">
      <div className="row gap10" style={{ minHeight: 38 }}>
        <button className="iconbtn press" onClick={() => navigate(-1)}>
          <Icon name="chevL" size={22} />
        </button>
        <div
          className="hd-sm"
          style={{ flex: 1, overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }}
        >
          Shartnoma
        </div>
      </div>
    </div>
  )

  if (loading) {
    return (
      <div className="screen">
        {head}
        <div className="center" style={{ minHeight: '60dvh' }}>
          <div className="spin" />
        </div>
      </div>
    )
  }

  if (error) {
    return (
      <div className="screen">
        {head}
        <div className="center" style={{ minHeight: '60dvh', flexDirection: 'column', gap: 12 }}>
          <Icon name="alert" size={36} color="var(--red)" />
          <div style={{ fontSize: 14, fontWeight: 700, color: 'var(--red)' }}>Yuklab bo'lmadi</div>
          <div className="muted" style={{ fontSize: 13, textAlign: 'center' }}>
            {error}
          </div>
        </div>
      </div>
    )
  }

  return (
    <div className="screen">
      {head}
      <div className="scroll" style={{ paddingBottom: 24 }}>
        <div className="pad">
          {docs.length === 0 ? (
            /* Bo'sh holat */
            <div
              style={{
                display: 'flex',
                flexDirection: 'column',
                alignItems: 'center',
                justifyContent: 'center',
                minHeight: '55dvh',
                gap: 14,
                textAlign: 'center',
              }}
            >
              <div
                style={{
                  width: 72,
                  height: 72,
                  borderRadius: 20,
                  background: ACCENT_SOFT,
                  display: 'flex',
                  alignItems: 'center',
                  justifyContent: 'center',
                }}
              >
                <FileText size={36} color={ACCENT} strokeWidth={2} />
              </div>
              <div style={{ fontSize: 18, fontWeight: 800, letterSpacing: '-.2px' }}>
                Shartnoma hali tuzilmagan
              </div>
              <div className="muted" style={{ fontSize: 14, maxWidth: 260 }}>
                Markaz bilan shartnoma tuzilgach, uning elektron nusxasi shu yerda ko'rinadi.
              </div>
            </div>
          ) : (
            <>
              {/* Umumiy hisobot */}
              <div
                style={{
                  borderRadius: 16,
                  padding: '14px 16px',
                  background: 'linear-gradient(135deg,#2563EB,#1D4ED8)',
                  color: '#fff',
                  marginBottom: 16,
                  display: 'flex',
                  alignItems: 'center',
                  gap: 14,
                }}
              >
                <FileText size={32} color="#fff" strokeWidth={2} />
                <div>
                  <div style={{ fontSize: 22, fontWeight: 800, letterSpacing: '-.3px' }}>
                    {docs.length}
                  </div>
                  <div style={{ fontSize: 13, opacity: 0.9 }}>ta shartnoma</div>
                </div>
              </div>

              {/* Shartnomalar ro'yxati */}
              {docs.map((d) => (
                <ContractCard key={d.id} doc={d} />
              ))}
            </>
          )}
        </div>
      </div>
    </div>
  )
}
