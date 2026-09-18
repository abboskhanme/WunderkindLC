import type { Student } from '@/types'
import { checkStudentPhones, type PhoneMatch, type StudentPayload } from '@/api/services/students'

/**
 * O'quvchi FORMASINING sof mantiqi — ikki joyda ishlatiladi:
 * `StudentFormModal` (ro'yxatdan qo'shish/tahrirlash oynasi) va profilning «Tahrirlash» /
 * «Parol o'rnatish» tablari. Nusxa YO'Q: payload shakli va telefon dublikati qoidasi bitta joyda.
 */

/** "Familiya Ism Sharifi" stringidan parts. Eski yozuvlarni tahrirda taqsimlaymiz. */
export function splitFullName(full: string): { last: string; first: string; middle: string } {
  const parts = (full ?? '').trim().split(/\s+/).filter(Boolean)
  return {
    last: parts[0] ?? '',
    first: parts[1] ?? '',
    middle: parts.slice(2).join(' '),
  }
}

export function joinName(last?: string, first?: string, middle?: string): string {
  return [last, first, middle]
    .map((p) => (p ?? '').trim())
    .filter((p) => p !== '')
    .join(' ')
}

/**
 * Mavjud o'quvchidan TAHRIR payloadi. `PUT /students/{id}` to'liq yozuvni kutadi, shuning uchun
 * faqat parol yoki bitta maydon o'zgarsa ham QOLGAN maydonlar shu yerdan olinadi (bo'shab
 * qolmasin). Ism qismlari saqlanmagan eski yozuvlarda `fullName` dan taqsimlanadi.
 */
export function payloadFromStudent(initial: Student): StudentPayload {
  const sParts = initial.lastName || initial.firstName || initial.middleName
    ? { last: initial.lastName ?? '', first: initial.firstName ?? '', middle: initial.middleName ?? '' }
    : splitFullName(initial.fullName)
  return {
    fullName: initial.fullName,
    lastName: sParts.last,
    firstName: sParts.first,
    middleName: sParts.middle,
    birthDate: initial.birthDate,
    birthCertificateUrl: initial.birthCertificateUrl ?? null,
    address: initial.address,
    gender: initial.gender,
    phone: initial.phone ?? '',
    fatherFullName: initial.fatherFullName ?? '',
    fatherPhone: initial.fatherPhone ?? '',
    motherFullName: initial.motherFullName ?? '',
    motherPhone: initial.motherPhone ?? '',
    className: initial.className,
    districtId: initial.districtId ?? '',
    schoolId: initial.schoolId ?? '',
    enrollmentDate: initial.enrollmentDate,
    discountPct: initial.discountPct,
    discountAmount: initial.discountAmount,
    discountNote: initial.discountNote,
    discountStartMonth: initial.discountStartMonth ?? '',
    discountEndMonth: initial.discountEndMonth ?? '',
    discountGroupId: initial.discountGroupId ?? '',
  }
}

/**
 * Kiritilgan raqamlar (o'quvchi/ota/ona) boshqa o'quvchida (ARXIVDAGILAR ham) bormi.
 * Tahrirlashda o'quvchining O'ZI dublikat sifatida chiqmasligi kafolatlanadi (backend
 * `excludeId` dan tashqari mijoz tarafida ham filtrlaymiz).
 */
export async function findPhoneDupes(form: StudentPayload, excludeId?: string): Promise<PhoneMatch[]> {
  const found = await checkStudentPhones({
    phone: form.phone ?? undefined,
    fatherPhone: form.fatherPhone ?? undefined,
    motherPhone: form.motherPhone ?? undefined,
    excludeId,
  })
  return excludeId ? found.filter((m) => m.studentId !== excludeId) : found
}
