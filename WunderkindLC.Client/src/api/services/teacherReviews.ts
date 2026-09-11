import { api } from '../client'
import type { StudentTeacherReviewGroup, TeacherReview, TeacherReviewFeed } from '@/types'

/**
 * O'QUVCHINING O'QITUVCHI HAQIDAGI FIKRI — o'quvchi profilidagi «Fikr-mulohazalar» bo'limi.
 *
 * Kim yozadi: FAQAT admin/superadmin (o'quvchi yoki ota-ona emas). Server ham shu qoidada —
 * `TeacherReviewsController` da `[Authorize(Roles = admin,superadmin,platformowner)]`.
 *
 * MAXFIYLIK: xom matn o'qituvchi profilida yoki o'qituvchi ilovasida HECH QACHON ko'rsatilmaydi —
 * u faqat shu yerda va o'qituvchining AI tahlili uchun manba sifatida ishlatiladi.
 */

/** O'quvchining har GURUHI bo'yicha blok: o'qituvchi + u haqida yozilgan fikrlar. */
export async function getStudentTeacherReviews(
  studentId: string,
): Promise<StudentTeacherReviewGroup[]> {
  const { data } = await api.get<StudentTeacherReviewGroup[]>(
    `/admin/students/${studentId}/teacher-reviews`,
  )
  return data
}

/** Yangi fikr yozish. Fikr AYNAN guruh o'qituvchisi haqida bo'ladi (server tekshiradi). */
export async function addTeacherReview(
  studentId: string,
  payload: { teacherId: string; groupId: string; text: string },
): Promise<TeacherReview> {
  const { data } = await api.post<TeacherReview>(
    `/admin/students/${studentId}/teacher-reviews`,
    payload,
  )
  return data
}

/**
 * O'QITUVCHI profilidagi «Fikrlar» bo'limi: shu o'qituvchi haqida yozilgan BARCHA fikrlar
 * (eng yangisi tepada, o'quvchi va guruh nomi bilan).
 *
 * Bu ADMIN ko'rinishi — o'qituvchi portalida/ilovasida bunday endpoint ataylab YO'Q.
 */
export async function getTeacherReviewFeed(teacherId: string): Promise<TeacherReviewFeed> {
  const { data } = await api.get<TeacherReviewFeed>(`/admin/teachers/${teacherId}/reviews`)
  return data
}

/** Xato yozilgan fikrni o'chirish. */
export async function deleteTeacherReview(id: string): Promise<void> {
  await api.delete(`/admin/teacher-reviews/${id}`)
}
