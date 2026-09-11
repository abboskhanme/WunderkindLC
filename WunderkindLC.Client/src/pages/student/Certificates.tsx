import { useEffect, useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { Award } from 'lucide-react'
import { getStudentCertificates, type StudentCertificateDto } from '@/api/services/studentPortal'
import { Icon, fmtDate } from '@/pages/student/lib'

/* ============================================================
   O'quvchi portali — Sertifikatlar ekrani.
   Kurs sertifikatlari: yuklab olish + ulashish (verify havolasi).
   Dizayn: student.html blue tema, .student-app shell.
   ============================================================ */

function CertCard({
  cert,
  onCopy,
}: {
  cert: StudentCertificateDto
  onCopy: (id: string) => void
}) {
  const isActive = cert.status === 'active'
  const isExpired = cert.status === 'expired'

  const statusLabel = isActive ? 'Amal qiluvchi' : isExpired ? 'Muddati o\'tgan' : 'Bekor qilingan'
  const statusColor = isActive ? 'var(--green)' : isExpired ? 'var(--amber, #D97706)' : 'var(--red)'
  const statusBg = isActive ? 'var(--greenSoft)' : isExpired ? '#FEF3C7' : '#FEE2E2'

  return (
    <div
      className="card"
      style={{
        borderRadius: 18,
        padding: '16px 16px 14px',
        marginBottom: 12,
        borderLeft: `4px solid ${isActive ? '#D97706' : 'var(--border)'}`,
      }}
    >
      {/* Sarlavha qatori */}
      <div className="row gap12" style={{ alignItems: 'flex-start', marginBottom: 10 }}>
        <div
          style={{
            width: 44,
            height: 44,
            borderRadius: 12,
            background: '#FEF3C7',
            display: 'flex',
            alignItems: 'center',
            justifyContent: 'center',
            flex: 'none',
          }}
        >
          <Award size={24} color="#D97706" strokeWidth={2} />
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
            {cert.courseName}
          </div>
          <div className="muted" style={{ fontSize: 12.5 }}>
            {cert.fileName}
          </div>
        </div>
        {/* Status badge */}
        <div
          style={{
            fontSize: 11.5,
            fontWeight: 700,
            color: statusColor,
            background: statusBg,
            borderRadius: 8,
            padding: '3px 8px',
            whiteSpace: 'nowrap',
            flex: 'none',
          }}
        >
          {isActive && '✓ '}{statusLabel}
        </div>
      </div>

      {/* Sanalar */}
      <div className="row gap12" style={{ marginBottom: 12 }}>
        <div style={{ flex: 1 }}>
          <div className="faint" style={{ fontSize: 11, fontWeight: 700, marginBottom: 2 }}>
            BERILGAN SANA
          </div>
          <div style={{ fontSize: 13.5, fontWeight: 700 }}>{fmtDate(cert.issuedAt)}</div>
        </div>
        {cert.expiresAt && (
          <div style={{ flex: 1 }}>
            <div className="faint" style={{ fontSize: 11, fontWeight: 700, marginBottom: 2 }}>
              AMAL QILISH MUDDATI
            </div>
            <div style={{ fontSize: 13.5, fontWeight: 700, color: isExpired ? 'var(--red)' : undefined }}>
              {fmtDate(cert.expiresAt)}
            </div>
          </div>
        )}
        {cert.downloadCount > 0 && (
          <div>
            <div className="faint" style={{ fontSize: 11, fontWeight: 700, marginBottom: 2 }}>
              YUKLANDI
            </div>
            <div style={{ fontSize: 13.5, fontWeight: 700 }}>{cert.downloadCount} marta</div>
          </div>
        )}
      </div>

      {/* Tugmalar */}
      <div className="row gap8">
        <a
          href={`/api/student/certificates/${cert.id}/download`}
          download
          className="btn btn-primary press"
          style={{
            flex: 1,
            display: 'flex',
            alignItems: 'center',
            justifyContent: 'center',
            gap: 6,
            textDecoration: 'none',
            fontSize: 13.5,
            fontWeight: 700,
            borderRadius: 12,
            padding: '10px 14px',
          }}
        >
          <Icon name="download" size={16} />
          <span>Yuklab olish</span>
        </a>
        <button
          className="btn btn-ghost press"
          onClick={() => onCopy(cert.id)}
          style={{
            display: 'flex',
            alignItems: 'center',
            justifyContent: 'center',
            gap: 6,
            fontSize: 13.5,
            fontWeight: 700,
            borderRadius: 12,
            padding: '10px 14px',
            border: '1.5px solid var(--border)',
          }}
        >
          <Icon name="upload" size={16} />
          <span>Ulashish</span>
        </button>
      </div>
    </div>
  )
}

export function CertificatesPage() {
  const navigate = useNavigate()
  const [certs, setCerts] = useState<StudentCertificateDto[]>([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const [toast, setToast] = useState<string | null>(null)

  useEffect(() => {
    let alive = true
    setLoading(true)
    getStudentCertificates()
      .then((d) => alive && setCerts(d))
      .catch((e) => alive && setError(e?.message || String(e)))
      .finally(() => alive && setLoading(false))
    return () => {
      alive = false
    }
  }, [])

  useEffect(() => {
    if (!toast) return
    const t = setTimeout(() => setToast(null), 2500)
    return () => clearTimeout(t)
  }, [toast])

  function handleCopy(id: string) {
    const url = `${window.location.origin}/verify/certificate/${id}`
    if (navigator.clipboard) {
      navigator.clipboard.writeText(url).then(() => setToast('Havola nusxalandi'))
    } else {
      setToast('Havola: ' + url)
    }
  }

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
          Sertifikatlar
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
          {certs.length === 0 ? (
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
                  background: '#FEF3C7',
                  display: 'flex',
                  alignItems: 'center',
                  justifyContent: 'center',
                }}
              >
                <Award size={36} color="#D97706" strokeWidth={2} />
              </div>
              <div style={{ fontSize: 18, fontWeight: 800, letterSpacing: '-.2px' }}>
                Hali sertifikat yo'q
              </div>
              <div className="muted" style={{ fontSize: 14, maxWidth: 260 }}>
                Kursni muvaffaqiyatli tugatganingizdan so'ng sertifikatlar bu yerda ko'rinadi.
              </div>
            </div>
          ) : (
            <>
              {/* Umumiy hisobot */}
              <div
                style={{
                  borderRadius: 16,
                  padding: '14px 16px',
                  background: 'linear-gradient(135deg,#D97706,#B45309)',
                  color: '#fff',
                  marginBottom: 16,
                  display: 'flex',
                  alignItems: 'center',
                  gap: 14,
                }}
              >
                <Award size={32} color="#fff" strokeWidth={2} />
                <div>
                  <div style={{ fontSize: 22, fontWeight: 800, letterSpacing: '-.3px' }}>
                    {certs.length}
                  </div>
                  <div style={{ fontSize: 13, opacity: 0.9 }}>
                    ta sertifikat ({certs.filter((c) => c.status === 'active').length} ta amal qiluvchi)
                  </div>
                </div>
              </div>

              {/* Sertifikatlar ro'yxati */}
              {certs.map((c) => (
                <CertCard key={c.id} cert={c} onCopy={handleCopy} />
              ))}
            </>
          )}
        </div>
      </div>

      {/* Toast bildirishnoma */}
      {toast && (
        <div
          style={{
            position: 'fixed',
            bottom: 90,
            left: '50%',
            transform: 'translateX(-50%)',
            background: 'rgba(15,23,42,0.92)',
            color: '#fff',
            borderRadius: 12,
            padding: '10px 18px',
            fontSize: 13.5,
            fontWeight: 700,
            whiteSpace: 'nowrap',
            zIndex: 9999,
            pointerEvents: 'none',
          }}
        >
          {toast}
        </div>
      )}
    </div>
  )
}
