import { Award, Download, Share2, Trophy } from 'lucide-react'
import { getStudentCertificates, type StudentCertificateDto } from '@/api/services/studentPortal'
import { fetchMyAchievementPhotoUrl, getMyAchievements, type Achievement } from '@/api/services/achievements'
import { useBlobUrl } from '@/components/messaging/useBlobUrl'
import { fmtDate } from '@/pages/student/lib'
import { useApi, useToast } from '@/pages/student/hooks'
import { EmptyState, ErrorState, IconBox, PageHeader, SectionTitle, SkeletonList, StatusChip, Toast } from '@/pages/student/ui'

/* ============================================================
   Profil → SERTIFIKATLAR VA YUTUQLAR — achievements (IELTS / CEFR / olympiad …, photo from the
   authorised endpoint) and course certificates (download + share the verify link).
   ============================================================ */

const loadCerts = () => getStudentCertificates()
const loadAchievements = () => getMyAchievements()

function certStatus(c: StudentCertificateDto) {
  if (c.status === 'active') return { label: 'Amal qiladi', tone: 'ok' as const }
  if (c.status === 'expired') return { label: "Muddati o'tgan", tone: 'warn' as const }
  return { label: 'Bekor qilingan', tone: 'bad' as const }
}

function CertCard({ cert, onShare }: { cert: StudentCertificateDto; onShare: (id: string) => void }) {
  const st = certStatus(cert)
  return (
    <div className="card" style={{ padding: 14 }}>
      <div className="row gap12" style={{ alignItems: 'flex-start' }}>
        <IconBox icon={Award} color="#d97706" />
        <div style={{ flex: 1, minWidth: 0 }}>
          <div className="st-wrap" style={{ fontSize: 15.5, fontWeight: 700, lineHeight: 1.3 }}>{cert.courseName}</div>
          <div className="muted" style={{ fontSize: 13, marginTop: 2 }}>
            Berilgan: {fmtDate(cert.issuedAt)}
            {cert.expiresAt ? ` · Amal qiladi: ${fmtDate(cert.expiresAt)} gacha` : ''}
            {cert.downloadCount > 0 ? ` · ${cert.downloadCount} marta yuklangan` : ''}
          </div>
        </div>
        <StatusChip tone={st.tone}>{st.label}</StatusChip>
      </div>
      <div className="row gap8" style={{ marginTop: 12 }}>
        <a href={`/api/student/certificates/${cert.id}/download`} download className="btn btn-primary btn-sm press" style={{ flex: 1 }}>
          <Download size={16} />
          Yuklab olish
        </a>
        <button type="button" className="btn btn-ghost btn-sm press" onClick={() => onShare(cert.id)} style={{ flex: 1 }}>
          <Share2 size={16} />
          Ulashish
        </button>
      </div>
    </div>
  )
}

/** Achievement card — the photo comes from the closed endpoint as a blob. */
function AchievementCard({ a }: { a: Achievement }) {
  const photo = useBlobUrl(a.hasPhoto ? a.id : null, fetchMyAchievementPhotoUrl)
  return (
    <div className="st-li" style={{ alignItems: 'center' }}>
      {photo ? (
        <img src={photo} alt={a.title} style={{ width: 56, height: 56, borderRadius: 14, objectFit: 'cover', flex: 'none' }} />
      ) : (
        <IconBox icon={Trophy} color="#d97706" size={56} />
      )}
      <div className="st-li-main">
        <div className="muted" style={{ fontSize: 12, fontWeight: 700, letterSpacing: '0.3px', textTransform: 'uppercase' }}>{a.kindLabel}</div>
        <div className="st-li-title">{a.title}</div>
        <div className="row gap8" style={{ marginTop: 2, fontSize: 13, flexWrap: 'wrap' }}>
          {a.result && <span className="st-num" style={{ color: 'var(--amber)', fontWeight: 800 }}>{a.result}</span>}
          {a.date && <span className="muted">{fmtDate(a.date)}</span>}
        </div>
      </div>
    </div>
  )
}

export function CertificatesPage() {
  const certs = useApi(loadCerts)
  // Achievements are a secondary (background) read: on failure the block is simply not shown.
  const achievements = useApi(loadAchievements)
  const [toast, setToast] = useToast()

  function handleShare(id: string) {
    const url = `${window.location.origin}/verify/certificate/${id}`
    if (navigator.clipboard) {
      // Browser API — allowed to fail quietly (error-visibility.md §4); fallback shows the link.
      navigator.clipboard.writeText(url).then(
        () => setToast('Havola nusxalandi'),
        () => setToast('Havola: ' + url),
      )
    } else {
      setToast('Havola: ' + url)
    }
  }

  const ach = achievements.data ?? []
  const list = certs.data ?? []
  const activeCount = list.filter((c) => c.status === 'active').length

  return (
    <div className="screen">
      <PageHeader title="Sertifikatlar va yutuqlar" back="/student/profile" />
      <div className="scroll">
        <div className="st-page" style={{ paddingTop: 8 }}>
          {ach.length > 0 && (
            <>
              <SectionTitle title="Yutuqlar" sub={`${ach.length} ta`} style={{ marginTop: 4 }} />
              <div className="card flush" style={{ marginBottom: 6 }}>
                {ach.map((a) => (
                  <AchievementCard key={a.id} a={a} />
                ))}
              </div>
            </>
          )}

          <SectionTitle
            title="Sertifikatlar"
            sub={list.length ? `${list.length} ta · ${activeCount} tasi amal qiladi` : undefined}
            style={ach.length ? undefined : { marginTop: 4 }}
          />
          {certs.error && !certs.data ? (
            <ErrorState message={certs.error} onRetry={certs.reload} />
          ) : certs.loading && !certs.data ? (
            <SkeletonList rows={2} />
          ) : list.length === 0 ? (
            <div className="card flush">
              <EmptyState icon={Award} tone="warn" title="Hali sertifikat yo'q" sub="Kurs muvaffaqiyatli tugatilgach, sertifikat shu yerda paydo bo'ladi." />
            </div>
          ) : (
            <div className="col" style={{ gap: 10 }}>
              {list.map((c) => (
                <CertCard key={c.id} cert={c} onShare={handleShare} />
              ))}
            </div>
          )}
        </div>
      </div>
      <Toast text={toast} />
    </div>
  )
}
