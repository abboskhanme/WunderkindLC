import { useCallback, useEffect, useMemo, useState } from 'react'
import type { Group, LeadEvent, Teacher, TrialLesson } from '@/types'
import { getLeadEvents, getLeadTrials } from '@/api/services/leads'
import { getClasses } from '@/api/services/classes'
import { getTeachers } from '@/api/services/teachers'

/**
 * Lid TARIXI (hodisalar) va SINOV DARSLARI — lid sahifasining o'ng paneli va amal oynalari
 * AYNAN shundan foydalanadi. `refresh` har amaldan (izoh, SMS, sinov, natija, aylantirish)
 * keyin chaqiriladi — server tarixga yozgan hodisa darhol ko'rinsin.
 */
export function useLeadActivity(leadId: string | null) {
  const [events, setEvents] = useState<LeadEvent[]>([])
  const [trials, setTrials] = useState<TrialLesson[]>([])

  const refresh = useCallback(() => {
    if (!leadId) return
    getLeadEvents(leadId).then(setEvents).catch(() => setEvents([]))
    getLeadTrials(leadId).then(setTrials).catch(() => setTrials([]))
  }, [leadId])

  useEffect(() => {
    refresh()
  }, [refresh])

  return { events, trials, refresh }
}

/**
 * Sinov darsi va aylantirish formalarining GURUH tanlovi: avval o'qituvchi, keyin uning guruhi
 * (kaskad). Faqat arxivlanmagan guruhlar va guruhi BOR o'qituvchilar.
 */
export function useLeadGroupOptions(active: boolean) {
  const [groups, setGroups] = useState<Group[]>([])
  const [teachers, setTeachers] = useState<Teacher[]>([])

  useEffect(() => {
    if (!active) return
    getClasses()
      .then((gs) => setGroups(gs.filter((g) => !g.isArchived)))
      .catch(() => setGroups([]))
    getTeachers().then(setTeachers).catch(() => setTeachers([]))
  }, [active])

  const teacherOptions = useMemo(
    () =>
      teachers
        .filter((t) => groups.some((g) => g.teacherId === t.id))
        .sort((a, b) => a.fullName.localeCompare(b.fullName)),
    [teachers, groups],
  )

  return { groups, teacherOptions }
}
