import { useEffect, useState } from 'react'
import { Link } from 'react-router-dom'
import { ArrowUpRight } from 'lucide-react'
import type { Credentials, Group, Subject, Teacher } from '@/types'
import { Modal } from '@/components/ui/Modal'
import { Button } from '@/components/ui/Button'
import { CredentialsBox } from '@/components/ui/CredentialsBox'
import { getTeacherCredentials, resetTeacherPassword } from '@/api/services/teachers'
import { genderLabels, formatMonth } from '@/config/constants'
import { formatDate, formatMoney, cn } from '@/lib/utils'

interface Props {
  teacher: Teacher | null
  subjects: Subject[]
  /** O'qituvchi o'tadigan guruhlar (Group.teacherId bo'yicha) */
  groups?: Group[]
  onClose: () => void
}

function Row({ label, value, mono }: { label: string; value: string; mono?: boolean }) {
  return (
    <div className="flex justify-between gap-4 border-b border-slate-100 py-2.5 last:border-0">
      <span className="text-sm text-slate-400">{label}</span>
      <span
        className={cn(
          'text-right text-sm font-medium text-slate-800',
          mono && 'font-mono',
        )}
      >
        {value}
      </span>
    </div>
  )
}

export function TeacherViewModal({ teacher, subjects, groups = [], onClose }: Props) {
  const [credentials, setCredentials] = useState<Credentials | null>(null)

  useEffect(() => {
    if (!teacher) {
      setCredentials(null)
      return
    }
    let active = true
    setCredentials(null)
    getTeacherCredentials(teacher.id)
      .then((c) => active && setCredentials(c))
      .catch(() => active && setCredentials(null))
    return () => {
      active = false
    }
  }, [teacher])

  const subjectNames = teacher
    ? teacher.subjectIds
        .map((id) => subjects.find((s) => s.id === id)?.name)
        .filter(Boolean)
        .join(', ')
    : ''

  return (
    <Modal
      open={!!teacher}
      onClose={onClose}
      title="O'qituvchi ma'lumotlari"
      footer={
        <Button variant="secondary" onClick={onClose}>
          Yopish
        </Button>
      }
    >
      {teacher && (
        <div>
          {/* Sarlavha — avatar + ism */}
          <div className="mb-4 flex items-center gap-3">
            {teacher.photoUrl ? (
              <img
                src={teacher.photoUrl}
                alt=""
                className="h-12 w-12 rounded-full object-cover"
              />
            ) : (
              <div className="flex h-12 w-12 items-center justify-center rounded-full bg-brand-50 text-base font-semibold text-brand-600">
                {teacher.fullName
                  .split(' ')
                  .filter(Boolean)
                  .slice(0, 2)
                  .map((s) => s[0]?.toUpperCase())
                  .join('')}
              </div>
            )}
            <div>
              <div className="font-semibold text-slate-800">{teacher.fullName}</div>
              <div className="text-xs text-slate-400">{genderLabels[teacher.gender]}</div>
            </div>
          </div>
          <Row label="Tug'ilgan kun" value={formatDate(teacher.birthDate)} />
          <Row label="Manzil" value={teacher.address || '—'} />
          <div className="flex flex-wrap items-start justify-between gap-2 border-b border-slate-50 py-2 last:border-0">
            <span className="text-sm text-slate-400">Guruhlari</span>
            <div className="flex max-w-[70%] flex-wrap justify-end gap-1.5">
              {groups.length > 0 ? (
                groups.map((g) => (
                  <Link
                    key={g.id}
                    to={`/admin/classes/${g.id}`}
                    onClick={onClose}
                    className="inline-flex items-center gap-1 rounded-full border border-slate-200 bg-slate-50 px-2.5 py-0.5 text-xs font-medium text-slate-600 transition-colors hover:border-brand-300 hover:bg-brand-50 hover:text-brand-700"
                    title={`${g.name} guruhiga o'tish`}
                  >
                    {g.name}
                    <ArrowUpRight className="h-3 w-3" />
                  </Link>
                ))
              ) : (
                <span className="text-sm text-slate-700">—</span>
              )}
            </div>
          </div>
          <Row
            label="Maosh turi"
            mono
            value={
              teacher.salaryMode === 'percent'
                ? `Foiz — guruh to'lovining ${teacher.salaryPercent ?? 0}%i`
                : `Qat'iy summa — ${formatMoney(teacher.salary)}`
            }
          />
          <Row
            label="Maosh hisoblanadi"
            value={
              teacher.salaryStartDate
                ? `${formatDate(teacher.salaryStartDate)} dan`
                : teacher.salaryStartMonth
                  ? `${formatMonth(teacher.salaryStartMonth)} dan`
                  : 'o\'quv yili boshidan'
            }
          />
          <Row label="Fanlar" value={subjectNames || '—'} />
          <CredentialsBox
            credentials={credentials}
            onReset={async () => {
              const c = await resetTeacherPassword(teacher.id)
              setCredentials(c)
            }}
          />
        </div>
      )}
    </Modal>
  )
}
