import { useEffect, useState } from 'react'
import { useNavigate, useParams } from 'react-router-dom'
import { ArrowLeft } from 'lucide-react'
import type { TestGroupOverview } from '@/types'
import { getTestGroups } from '@/api/services/testResults'
import { Card } from '@/components/ui/Card'
import { PageHeader } from '@/components/ui/PageHeader'
import { apiErrorMessage } from '@/lib/utils'
import { GroupTestsPanel } from './GroupTestsPanel'

/**
 * Bitta guruhning testlari — sarlavha (guruh ma'lumoti) + testlar paneli.
 * Panel guruh (jurnal) sahifasidagi "Imtihonlar" tabi bilan AYNAN bir xil komponent
 * (`GroupTestsPanel`) — onlayn/oflayn test yaratish ikkala joyda ham bor.
 */
export function TestGroupPage() {
  const { groupId = '' } = useParams()
  const navigate = useNavigate()
  const [group, setGroup] = useState<TestGroupOverview | null>(null)
  const [error, setError] = useState('')

  useEffect(() => {
    getTestGroups()
      .then((groups) => setGroup(groups.find((g) => g.groupId === groupId) ?? null))
      .catch((e) => setError(apiErrorMessage(e, "Yuklab bo'lmadi")))
  }, [groupId])

  return (
    <div>
      <button
        type="button"
        onClick={() => navigate('/admin/test-results')}
        className="mb-3 inline-flex items-center gap-1.5 text-sm font-medium text-slate-500 hover:text-slate-700"
      >
        <ArrowLeft className="h-4 w-4" /> Barcha guruhlar
      </button>

      <PageHeader
        title={group?.name || 'Guruh testlari'}
        sub={
          group
            ? [group.courseName, group.teacherName, `${group.studentCount} o'quvchi`]
                .filter(Boolean)
                .join(' · ')
            : undefined
        }
      />

      {error && <Card className="mb-3 py-3 text-center text-sm text-red-500">{error}</Card>}

      <GroupTestsPanel
        groupId={groupId}
        onOpenTest={(testId) => navigate(`/admin/test-results/${groupId}/tests/${testId}`)}
      />
    </div>
  )
}
