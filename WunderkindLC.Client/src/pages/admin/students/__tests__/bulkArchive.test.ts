import { describe, it, expect } from 'vitest'
import { bulkArchiveMessage, splitBulkArchive } from '../bulkArchive'

const t = (id: string) => ({ id, fullName: `O'quvchi ${id}` })
const reasonOf = (e: unknown) => (e instanceof Error ? e.message : 'xato')

describe('splitBulkArchive — ommaviy arxivlash QISMAN yiqilsa', () => {
  it("muvaffaqiyatlilar alohida, rad etilganlar sababi bilan (tartib saqlanadi)", () => {
    const out = splitBulkArchive(
      [t('a'), t('b'), t('c')],
      [
        { status: 'fulfilled', value: undefined },
        { status: 'rejected', reason: new Error("O'quvchi allaqachon arxivda") },
        { status: 'fulfilled', value: undefined },
      ],
      reasonOf,
    )
    expect(out.okIds).toEqual(['a', 'c'])
    expect(out.failed).toEqual([{ id: 'b', fullName: "O'quvchi b", reason: "O'quvchi allaqachon arxivda" }])
  })

  it("natija yetishmasa (kutilmagan) — o'quvchi muvaffaqiyatli deb HISOBLANMAYDI", () => {
    const out = splitBulkArchive([t('a')], [], reasonOf)
    expect(out.okIds).toEqual([])
    expect(out.failed).toHaveLength(1)
  })
})

describe('bulkArchiveMessage', () => {
  it("hammasi o'tsa — xabar yo'q", () => {
    expect(bulkArchiveMessage({ okIds: ['a'], failed: [] })).toBe('')
  })

  it('kim va NEGA arxivlanmagani yoziladi, 10 tadan keyingisi qisqartiriladi', () => {
    const failed = Array.from({ length: 12 }, (_, i) => ({ id: `${i}`, fullName: `F${i}`, reason: 'sabab' }))
    const msg = bulkArchiveMessage({ okIds: ['x', 'y'], failed })
    expect(msg).toContain("2 ta o'quvchi arxivlandi, 12 tasi arxivlanmadi")
    expect(msg).toContain('• F0: sabab')
    expect(msg).not.toContain('F11')
    expect(msg).toContain('… yana 2 ta')
  })
})
