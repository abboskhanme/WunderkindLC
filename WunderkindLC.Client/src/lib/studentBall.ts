/**
 * O'quvchi BALLI — klient tomoni (guruh sahifasidagi "Reyting" tabi va jurnal saralashi).
 * Server `StudentBallService` bilan AYNAN bir xil formula:
 *   Ball = Σ(QO'LDA qo'yilgan jurnal baholari) + Σ(bajarilgan baholash mezonlari)
 *
 * ⚠️ Mezon belgilansa jurnal bahosi ham avtomatik yoziladi (checklar soni, `gradeFromCriteria`).
 * Bunday baho jurnal qismiga QO'SHILMAYDI — o'sha checklar mezonlar qismida allaqachon sanaladi.
 * Ilgari ikkalasi ham qo'shilib, mezon ishlatadigan guruhda ball IKKI barobar chiqardi.
 */

/** Ball hisobida bitta jurnal yozuvining ulushi (mezondan yozilgan baho — 0). */
export function ballGrade(e: { grade?: number | null; gradeFromCriteria?: boolean } | undefined): number {
  if (!e || e.gradeFromCriteria) return 0
  return e.grade ?? 0
}
