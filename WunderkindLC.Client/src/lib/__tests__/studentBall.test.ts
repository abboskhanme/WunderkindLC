import { describe, it, expect } from 'vitest'
import { ballGrade } from '../studentBall'

describe('ballGrade — bir check ballda BIR MARTA sanaladi (server StudentBallService bilan bir xil)', () => {
  it("qo'lda qo'yilgan baho to'liq qo'shiladi", () => {
    expect(ballGrade({ grade: 5 })).toBe(5)
    expect(ballGrade({ grade: 4, gradeFromCriteria: false })).toBe(4)
  })

  it('mezondan avtomatik yozilgan baho QO\'SHILMAYDI (checklar mezon qismida sanaladi)', () => {
    expect(ballGrade({ grade: 3, gradeFromCriteria: true })).toBe(0)
  })

  it("yozuv yo'q yoki baho bo'sh — 0", () => {
    expect(ballGrade(undefined)).toBe(0)
    expect(ballGrade({ grade: null })).toBe(0)
    expect(ballGrade({})).toBe(0)
  })

  it('jami ball = qo\'lda baholar + checklar (ikki marta emas)', () => {
    const entries = [{ grade: 5 }, { grade: 2, gradeFromCriteria: true }, { grade: 4 }]
    const doneChecks = 2 // o'sha "2" bahoni bergan checklar
    const ball = entries.reduce((s, e) => s + ballGrade(e), 0) + doneChecks
    expect(ball).toBe(11)
  })
})
