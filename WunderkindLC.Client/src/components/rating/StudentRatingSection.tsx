import { useEffect, useState } from 'react'
import { getStudentRatingBlock, type StudentRatingBlock as BlockData } from '@/api/services/rating'
import { defaultRatingPeriod, type RatingPeriodValue } from '@/lib/ratingPeriods'
import { apiErrorMessage } from '@/lib/utils'
import { StudentRatingBlock } from './StudentRatingBlock'

/** Davr kaliti — javob qaysi tanlov uchun kelganini bilish (eski javob yangisini bosmasin). */
const keyOf = (p: RatingPeriodValue) => `${p.kind}:${p.key}`

/**
 * Admin o'quvchi profilidagi «Reyting» tabi — `GET /admin/rating/students/{id}`.
 * Davr almashganda eski ma'lumot xira holda qoladi (sakramaydi), xato qizil satrda ko'rinadi.
 */
export function StudentRatingSection({ studentId }: { studentId: string }) {
  const [period, setPeriod] = useState<RatingPeriodValue>(() => defaultRatingPeriod(new Date()))
  const [res, setRes] = useState<{ key: string; data: BlockData | null; error: string | null } | null>(null)
  const key = `${studentId}|${keyOf(period)}`

  useEffect(() => {
    let alive = true
    const k = `${studentId}|${period.kind}:${period.key}`
    getStudentRatingBlock(studentId, { period: period.kind, key: period.key })
      .then((data) => alive && setRes({ key: k, data, error: null }))
      // Sahifa bo'limining ASOSIY ma'lumoti — xato ko'rsatiladi (error-visibility.md §4).
      .catch((e) => alive && setRes((prev) => ({ key: k, data: prev?.data ?? null, error: apiErrorMessage(e, "Reytingni yuklab bo'lmadi") })))
    return () => {
      alive = false
    }
  }, [studentId, period.kind, period.key])

  return (
    // Tashqi karta YO'Q: plitkalar, grafiklar va jadval o'zlari karta (karta ichida karta bo'lmasin).
    <StudentRatingBlock
      data={res?.data?.studentId === studentId ? res.data : null}
      loading={res?.key !== key}
      error={res?.key === key ? res.error : null}
      period={period}
      onPeriodChange={setPeriod}
    />
  )
}
