/**
 * OMMAVIY ARXIVLASH natijasi — sof funksiyalar (komponent fayliga qo'shilmaydi: eslint
 * `react-refresh/only-export-components`).
 *
 * ⚠️ Ilgari `Promise.all` ishlatilardi: bitta o'quvchi rad etilsa (masalan allaqachon arxivda) butun
 * amal "xato" bo'lib ko'rinar, muvaffaqiyatli arxivlanganlar esa ro'yxatda FAOL bo'lib qolardi —
 * qayta urinishda ular "allaqachon arxivda" deb yiqilardi. Endi har so'rov alohida hisoblanadi.
 */

export interface BulkArchiveTarget {
  id: string
  fullName: string
}

export interface BulkArchiveOutcome {
  /** Arxivlangan o'quvchilar id'lari. */
  okIds: string[]
  /** Rad etilganlar — kim va nega (server sababi). */
  failed: { id: string; fullName: string; reason: string }[]
}

/** `Promise.allSettled` natijasini (targets bilan bir tartibda) ajratadi. */
export function splitBulkArchive(
  targets: BulkArchiveTarget[],
  results: PromiseSettledResult<unknown>[],
  reasonOf: (err: unknown) => string,
): BulkArchiveOutcome {
  const okIds: string[] = []
  const failed: BulkArchiveOutcome['failed'] = []
  targets.forEach((t, i) => {
    const r = results[i]
    if (r && r.status === 'fulfilled') okIds.push(t.id)
    else failed.push({ id: t.id, fullName: t.fullName, reason: reasonOf(r?.status === 'rejected' ? r.reason : undefined) })
  })
  return { okIds, failed }
}

/** Foydalanuvchiga xabar: nechtasi arxivlandi, kim va NEGA arxivlanmadi. Hammasi o'tgan bo'lsa — ''. */
export function bulkArchiveMessage(o: BulkArchiveOutcome): string {
  if (o.failed.length === 0) return ''
  const lines = o.failed.slice(0, 10).map((f) => `• ${f.fullName}: ${f.reason}`)
  if (o.failed.length > 10) lines.push(`… yana ${o.failed.length - 10} ta`)
  return (
    `${o.okIds.length} ta o'quvchi arxivlandi, ${o.failed.length} tasi arxivlanmadi:\n` + lines.join('\n')
  )
}
