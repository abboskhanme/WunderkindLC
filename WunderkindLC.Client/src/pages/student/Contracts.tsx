import { useMemo } from 'react'
import { ExternalLink, FileText } from 'lucide-react'
import type { ContractDoc } from '@/types'
import { getStudentContracts } from '@/api/services/studentPortal'
import { fmtDate } from '@/pages/student/lib'
import { useApi } from '@/pages/student/hooks'
import { EmptyState, ErrorState, IconBox, PageHeader, SkeletonList } from '@/pages/student/ui'

/* ============================================================
   Profil → SHARTNOMA — contracts made for this family; a row opens the PDF in a new tab
   (uploaded PDF, otherwise the authorised stream).
   ============================================================ */

const loadContracts = () => getStudentContracts()

/** Link to open: the uploaded PDF, otherwise the authorised stream. */
function docUrl(doc: ContractDoc): string {
  return doc.pdfUrl || `/api/student/contracts/${doc.id}/pdf`
}

export function StudentContractsScreen() {
  const { data, error, loading, reload } = useApi(loadContracts)
  const docs = useMemo(() => [...(data ?? [])].sort((a, b) => (b.date || '').localeCompare(a.date || '')), [data])

  return (
    <div className="screen">
      <PageHeader title="Shartnoma" back="/student/profile" />
      <div className="scroll">
        <div className="st-page" style={{ paddingTop: 8 }}>
          {error && !data ? (
            <ErrorState message={error} onRetry={reload} />
          ) : loading && !data ? (
            <SkeletonList rows={2} />
          ) : docs.length === 0 ? (
            <div className="card flush">
              <EmptyState icon={FileText} tone="info" title="Shartnoma hali tuzilmagan" sub="Markaz bilan shartnoma tuzilgach, uning elektron nusxasi shu yerda ko'rinadi." />
            </div>
          ) : (
            <div className="card flush">
              {docs.map((doc) => (
                <button key={doc.id} type="button" className="st-li press" onClick={() => window.open(docUrl(doc), '_blank', 'noopener')}>
                  <IconBox icon={FileText} tone="info" />
                  <div className="st-li-main">
                    <div className="st-li-title">{doc.title || `Shartnoma № ${doc.number}`}</div>
                    <div className="st-li-sub">
                      № {doc.number} · {fmtDate(doc.date)}
                      {doc.templateName ? ` · ${doc.templateName}` : ''}
                    </div>
                  </div>
                  <div className="st-li-end row gap6" style={{ color: 'var(--accent)', fontSize: 14, fontWeight: 700 }}>
                    PDF
                    <ExternalLink size={16} />
                  </div>
                </button>
              ))}
            </div>
          )}
        </div>
      </div>
    </div>
  )
}
