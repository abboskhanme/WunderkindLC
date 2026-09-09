import { useEffect, useState } from 'react'
import type { Credentials, Student, StudentGroupMembership } from '@/types'
import { Modal } from '@/components/ui/Modal'
import { Button } from '@/components/ui/Button'
import { Badge } from '@/components/ui/Badge'
import type { BadgeTone } from '@/components/ui/Badge'
import { CredentialsBox } from '@/components/ui/CredentialsBox'
import { getStudentCredentials, resetStudentPassword } from '@/api/services/students'
import { getStudentGroups } from '@/api/services/classes'
import { genderLabels } from '@/config/constants'
import { apiErrorMessage, formatDate, formatMoney, cn } from '@/lib/utils'
import { groupsText } from '@/lib/studentGroups'
import { studentDiscountLabel } from './discountLabel'

interface Props {
  student: Student | null
  onClose: () => void
}

function Row({ label, value, mono }: { label: string; value: string; mono?: boolean }) {
  return (
    <div className="flex justify-between gap-4 border-b border-slate-100 py-2.5 last:border-0">
      <span className="text-sm text-slate-400">{label}</span>
      <span className={cn('text-right text-sm font-medium text-slate-800', mono && 'font-mono')}>
        {value}
      </span>
    </div>
  )
}

/**
 * Kirish ma'lumotlari (login/parol) holati — UCHTA ALOHIDA holat.
 *
 * ⚠️ `null` ni "yuklanmoqda" deb qabul qilib bo'lmaydi: `GET {id}/credentials` faqat TO'LIQ
 * huquqli foydalanuvchiga ochiq (`HasFullAccess`), xodimga esa **403** qaytaradi. Ilgari 403
 * ham `null` bo'lardi va `CredentialsBox` MANGU "Yuklanmoqda..." ko'rsatardi — xodim kutib
 * o'tirar, so'ng "Yangi parol yaratish"ni bosardi, u ham jimgina yiqilardi va xodim parol
 * BERILDI deb o'ylardi.
 */
type CredState =
  | { kind: 'loading' }
  | { kind: 'ok'; value: Credentials }
  | { kind: 'forbidden' }
  | { kind: 'error'; message: string }

export function StudentViewModal({ student, onClose }: Props) {
  return (
    <Modal
      open={!!student}
      onClose={onClose}
      title="O'quvchi ma'lumotlari"
      footer={
        <Button variant="secondary" onClick={onClose}>
          Yopish
        </Button>
      }
    >
      {/* ⚠️ `key` — o'quvchi almashganda ichki holat (kirish ma'lumotlari, guruhlar) BUTUNLAY
          yangidan boshlanadi va oyna yopilganda tozalanadi. Effekt ichida qo'lda "reset"
          qilinsa, eski o'quvchining javobi yangisining ustiga tushib qolishi mumkin edi. */}
      {student && <StudentViewBody key={student.id} student={student} />}
    </Modal>
  )
}

function StudentViewBody({ student }: { student: Student }) {
  const [cred, setCred] = useState<CredState>({ kind: 'loading' })
  const [groups, setGroups] = useState<StudentGroupMembership[]>([])
  /** Parol yangilashdagi xato — JIM qolmasin (xodim "parol berildi" deb o'ylamasin). */
  const [resetError, setResetError] = useState('')

  useEffect(() => {
    let active = true
    getStudentCredentials(student.id)
      .then((c) => active && setCred({ kind: 'ok', value: c }))
      .catch((e) => {
        if (!active) return
        // 403 — RUXSAT yo'q (nosozlik EMAS): xodimga boshqacha matn ko'rsatiladi.
        const status = (e as { response?: { status?: number } })?.response?.status
        setCred(
          status === 403
            ? { kind: 'forbidden' }
            : { kind: 'error', message: apiErrorMessage(e, "Kirish ma'lumotlarini yuklab bo'lmadi") },
        )
      })
    getStudentGroups(student.id)
      .then((g) => active && setGroups(g))
      .catch(() => active && setGroups([]))
    return () => {
      active = false
    }
  }, [student.id])

  /**
   * TIRIK a'zoliklar (`isActive`) — ular ichida MUZLATILGAN ham bo'ladi: `isActive` "guruhda
   * a'zo" degani, "aktiv o'qiyapti" degani EMAS (muzlatish `isActive` ni o'zgartirmaydi).
   * Shuning uchun har badge o'z HOLATI bilan chiziladi (`.claude/rules/year-freeze.md` §5).
   */
  const liveGroups = groups.filter((g) => g.isActive)
  /** "Guruh" qatoridagi matn — a'zoliklardan; eski `className` faqat zaxira. */
  const groupsLabel = groupsText(
    liveGroups.map((g) => ({ name: g.groupName, status: g.status })),
    student.className,
    '—',
  )
  /** A'zolik holati belgisi — ro'yxatdagi rang/yorliqlar bilan bir xil. */
  const stateBadge = (g: StudentGroupMembership): { label: string; tone: BadgeTone } =>
    g.status === 'active' ? { label: 'aktiv', tone: 'green' }
    : g.status === 'trial' ? { label: 'sinov', tone: 'violet' }
    : g.status === 'frozen' && g.yearFreeze ? { label: 'aktiv muzlatilgan', tone: 'indigo' }
    : g.status === 'frozen' ? { label: 'muzlatilgan', tone: 'blue' }
    : { label: g.status || 'a\'zo', tone: 'default' }

  return (
    <div className="space-y-0">
          <Row label="F.I.SH" value={student.fullName} />
          <Row label="Tug'ilgan kun" value={formatDate(student.birthDate)} mono />
          <Row label="Jinsi" value={genderLabels[student.gender]} />
          <Row label="Manzil" value={student.address} />
          {/* ⚠️ Ilgari bu yerda `student.className` (BIRINCHI qo'shilgan guruh) turardi va pastdagi
              ro'yxat bilan ZID chiqardi: o'quvchi eski guruhida muzlatilib yangisida o'qiyotgan
              bo'lsa, "Guruh" da eskisi, "Guruhlar" da esa ikkalasi ko'rinardi. */}
          <Row label="Guruh" value={groupsLabel} />
          {liveGroups.length > 0 && (
            <div className="border-b border-slate-100 py-2.5">
              <span className="mb-1.5 block text-sm text-slate-400">Guruhlar</span>
              <div className="flex flex-wrap gap-1.5">
                {liveGroups.map((g) => {
                  const b = stateBadge(g)
                  return (
                    <Badge key={g.id} tone={b.tone}>
                      {g.groupName} · {b.label}
                      <span className="font-mono text-brand-400">{formatDate(g.joinedAt)}</span>
                    </Badge>
                  )
                })}
              </div>
            </div>
          )}
          <Row label="Ota-onasi" value={student.parentFullName} />
          <Row label="Ota-onasi raqami" value={student.parentPhone} mono />
          <Row label="Balans" value={formatMoney(student.balance)} mono />
          {/* Chegirma HAR FAN uchun alohida bo'lishi mumkin — matn `discountCount` ni ham hisobga oladi. */}
          {!!studentDiscountLabel(student) && (
            <Row label="Chegirma" value={studentDiscountLabel(student)} />
          )}
          {/* RUXSAT YO'Q — "yuklanmoqda" bo'lib turmaydi va parol tugmasi ham ko'rsatilmaydi
              (u baribir 403 bilan yiqilardi). */}
          {cred.kind === 'forbidden' ? (
            <div className="mt-4 rounded-xl border border-slate-200 bg-slate-50 p-4 text-sm text-slate-500">
              <p className="font-semibold text-slate-600">Tizimga kirish ma'lumotlari</p>
              <p className="mt-0.5">
                Sizda bu ma'lumotni ko'rish huquqi yo'q — login/parolni to'liq huquqli admin
                ko'rsata oladi.
              </p>
            </div>
          ) : cred.kind === 'error' ? (
            <div className="mt-4 rounded-xl border border-red-100 bg-red-50 p-4 text-sm text-red-700">
              <p className="font-semibold">Kirish ma'lumotlari yuklanmadi</p>
              <p className="mt-0.5 text-red-600">{cred.message}</p>
            </div>
          ) : (
            <>
              <CredentialsBox
                credentials={cred.kind === 'ok' ? cred.value : null}
                loading={cred.kind === 'loading'}
                onReset={async () => {
                  setResetError('')
                  try {
                    const c = await resetStudentPassword(student.id)
                    setCred({ kind: 'ok', value: c })
                  } catch (e) {
                    // ⚠️ JIM YIQILMAYDI: xodim "parol yaratildi" deb o'ylab ketardi.
                    setResetError(apiErrorMessage(e, "Yangi parol yaratib bo'lmadi"))
                  }
                }}
              />
              {resetError && (
                <p className="mt-2 rounded-lg border border-red-200 bg-red-50 px-3 py-2 text-sm text-red-700">
                  {resetError}
                </p>
              )}
            </>
          )}
    </div>
  )
}
