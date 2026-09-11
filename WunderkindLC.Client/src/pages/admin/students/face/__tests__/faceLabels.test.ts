// `src/pages/admin/students/face/faceLabels.ts` — "Yuz bilan kirish" bo'limining sof funksiyalari.
//
// Eng muhim ikkitasi:
//  • `parseQuality` — telefondan kelgan JSON MATNI (ishonchsiz manba) o'qiladi; u hech qachon
//    istisno tashlamasligi kerak, aks holda butun sahifa yiqilardi;
//  • `scorePercent` — kosinus → foiz; "solishtirilmagan" (null) va "0%" ni ARALASHTIRMASLIK muhim.

import { describe, expect, it } from 'vitest'
import {
  faceSourceLabel,
  parseQuality,
  platformLabel,
  qualityMetrics,
  scorePassed,
  scorePercent,
  statusLabel,
  statusTone,
} from '@/pages/admin/students/face/faceLabels'

describe('scorePercent', () => {
  it('kosinusni foizga aylantiradi', () => {
    expect(scorePercent(0.83)).toBe('83%')
    expect(scorePercent(1)).toBe('100%')
    expect(scorePercent(0)).toBe('0%')
  })

  it('yaxlitlaydi', () => {
    expect(scorePercent(0.8349)).toBe('83%')
    expect(scorePercent(0.835)).toBe('84%')
  })

  it("hisoblanmagan ball (null/undefined) — 0% EMAS, chiziqcha", () => {
    expect(scorePercent(null)).toBe('—')
    expect(scorePercent(undefined)).toBe('—')
  })

  it('yaroqsiz son ham chiziqcha', () => {
    expect(scorePercent(Number.NaN)).toBe('—')
    expect(scorePercent(Number.POSITIVE_INFINITY)).toBe('—')
  })

  it('oraliqdan chiqqan qiymatlar qisiladi (manfiy kosinus → 0%)', () => {
    expect(scorePercent(-0.4)).toBe('0%')
    expect(scorePercent(1.7)).toBe('100%')
  })
})

describe('scorePassed', () => {
  it("chegara bilan solishtiradi (teng ham o'tadi)", () => {
    expect(scorePassed(0.6, 0.6)).toBe(true)
    expect(scorePassed(0.59, 0.6)).toBe(false)
  })

  it('ball yo\'q bo\'lsa hech qachon o\'tmaydi', () => {
    expect(scorePassed(null, 0.6)).toBe(false)
    expect(scorePassed(undefined, 0)).toBe(false)
  })
})

describe('statusLabel / statusTone', () => {
  it("uchta holat o'zbekcha", () => {
    expect(statusLabel('approved')).toBe('Tasdiqlangan')
    expect(statusLabel('rejected')).toBe('Rad etilgan')
    expect(statusLabel('pending')).toBe('Kutilmoqda')
  })

  it("noma'lum holat yo'qolib ketmaydi — kaliti ko'rinadi", () => {
    expect(statusLabel('weird')).toBe('weird')
    expect(statusLabel('')).toBe('—')
    expect(statusTone('weird')).toBe('default')
  })

  it('ranglar', () => {
    expect(statusTone('approved')).toBe('green')
    expect(statusTone('rejected')).toBe('red')
    expect(statusTone('pending')).toBe('amber')
  })
})

describe('platformLabel', () => {
  it('tanish platformalar chiroyli yoziladi', () => {
    expect(platformLabel('android')).toBe('Android')
    expect(platformLabel('IOS')).toBe('iOS')
    expect(platformLabel('web')).toBe('Brauzer')
  })

  it("noma'lum qiymat o'zgarishsiz, bo'shi — chiziqcha", () => {
    expect(platformLabel('HarmonyOS')).toBe('HarmonyOS')
    expect(platformLabel('   ')).toBe('—')
    expect(platformLabel('')).toBe('—')
  })
})

describe('parseQuality', () => {
  const full = '{"faces":1,"sharpness":0.8,"brightness":0.45,"faceRatio":0.32,"yaw":-4,"roll":2,"eyesOpen":true}'

  it("to'liq JSON o'qiladi", () => {
    expect(parseQuality(full)).toEqual({
      faces: 1,
      sharpness: 0.8,
      brightness: 0.45,
      faceRatio: 0.32,
      yaw: -4,
      roll: 2,
      eyesOpen: true,
    })
  })

  it('bo\'sh/yo\'q qiymat — null', () => {
    expect(parseQuality('')).toBeNull()
    expect(parseQuality('   ')).toBeNull()
    expect(parseQuality(null)).toBeNull()
    expect(parseQuality(undefined)).toBeNull()
  })

  it('BUZUQ JSON istisno tashlamaydi', () => {
    expect(() => parseQuality('{faces:1')).not.toThrow()
    expect(parseQuality('{faces:1')).toBeNull()
    expect(parseQuality('<html>')).toBeNull()
  })

  it('obyekt bo\'lmagan JSON ham null (massiv, son, null)', () => {
    expect(parseQuality('[1,2]')).toBeNull()
    expect(parseQuality('5')).toBeNull()
    expect(parseQuality('null')).toBeNull()
    expect(parseQuality('"matn"')).toBeNull()
  })

  it("tanish maydon umuman bo'lmasa — null (bo'sh chiplar chizilmasin)", () => {
    expect(parseQuality('{"boshqa":1}')).toBeNull()
    expect(parseQuality('{}')).toBeNull()
  })

  it("qisman ma'lumot: yo'q maydon undefined bo'lib qoladi (0 EMAS)", () => {
    const q = parseQuality('{"sharpness":0.5}')
    expect(q?.sharpness).toBe(0.5)
    expect(q?.brightness).toBeUndefined()
    expect(q?.faces).toBeUndefined()
  })

  it("raqamli MATN ham qabul qilinadi (klient string yuborishi mumkin)", () => {
    expect(parseQuality('{"yaw":"-12.5"}')?.yaw).toBe(-12.5)
  })

  it('son bo\'lmagan qiymat e\'tiborsiz qoldiriladi', () => {
    expect(parseQuality('{"sharpness":"juda-yaxshi","faces":2}')).toEqual({ faces: 2 })
  })

  it('NaN/Infinity JSON\'da bo\'lmaydi, lekin matn ko\'rinishida kelsa ham tushmaydi', () => {
    expect(parseQuality('{"yaw":"NaN","roll":3}')).toEqual({ roll: 3 })
  })

  it('eyesOpen: boolean ham, son ham', () => {
    expect(parseQuality('{"eyesOpen":false}')?.eyesOpen).toBe(false)
    expect(parseQuality('{"eyesOpen":0}')?.eyesOpen).toBe(false)
    expect(parseQuality('{"eyesOpen":1}')?.eyesOpen).toBe(true)
  })
})

describe('qualityMetrics', () => {
  it('null — bo\'sh ro\'yxat (UI "ma\'lumot yo\'q" ko\'rsatadi)', () => {
    expect(qualityMetrics(null)).toEqual([])
  })

  it("faqat MAVJUD maydonlar qaytadi", () => {
    const rows = qualityMetrics(parseQuality('{"sharpness":0.5}'))
    expect(rows).toHaveLength(1)
    expect(rows[0]).toMatchObject({ key: 'sharpness', label: 'Tiniqlik', value: '50%', ok: true })
  })

  it('yaxshi kadrda hammasi ok', () => {
    const rows = qualityMetrics(
      parseQuality('{"faces":1,"sharpness":0.8,"brightness":0.45,"faceRatio":0.32,"yaw":-4,"roll":2,"eyesOpen":true}'),
    )
    expect(rows).toHaveLength(7)
    expect(rows.every((r) => r.ok)).toBe(true)
    expect(rows.map((r) => r.value)).toEqual(['1 ta', '80%', '45%', '32%', '-4°', '2°', 'ochiq'])
  })

  it('chegaradan chiqqanlar belgilanadi', () => {
    const bad = qualityMetrics(
      parseQuality('{"faces":2,"sharpness":0.05,"brightness":0.95,"faceRatio":0.02,"yaw":40,"roll":-33,"eyesOpen":false}'),
    )
    expect(bad.every((r) => !r.ok)).toBe(true)
  })

  it("qorong'i kadr — yorug'lik ok emas, qolgani ok", () => {
    const rows = qualityMetrics(parseQuality('{"brightness":0.1,"sharpness":0.9}'))
    expect(rows.find((r) => r.key === 'brightness')?.ok).toBe(false)
    expect(rows.find((r) => r.key === 'sharpness')?.ok).toBe(true)
  })

  it('burilish MODUL bo\'yicha tekshiriladi (chapga ham, o\'ngga ham)', () => {
    expect(qualityMetrics(parseQuality('{"yaw":-26}'))[0].ok).toBe(false)
    expect(qualityMetrics(parseQuality('{"yaw":-25}'))[0].ok).toBe(true)
  })
})

describe('faceSourceLabel', () => {
  it('etalon manbai', () => {
    expect(faceSourceLabel('photo')).toBe('Profil rasmidan')
    expect(faceSourceLabel('admin')).toBe('Administrator tasdiqlagan selfidan')
    expect(faceSourceLabel('')).toBe('—')
  })
})
