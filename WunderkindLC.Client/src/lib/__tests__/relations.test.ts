import { describe, expect, it } from 'vitest'
import {
  PARENT_RELATIONS,
  STUDENT_RELATIONS,
  countLine,
  isPreset,
  matchesTelegramFilter,
  normalizeRelation,
  relationIcon,
  relationLabel,
  sortRelations,
  toggleRelation,
} from '../relations'
import { campaignKindLabel, deliveryView, formatBytes, isCampaignOpen } from '../telegramDelivery'

describe('relations — Telegram qarindoshlik', () => {
  it('yorliq va ikonka', () => {
    expect(relationLabel('father')).toBe('Otasi')
    expect(relationLabel('mother')).toBe('Onasi')
    expect(relationLabel('parent')).toBe('Ota-onasi')
    expect(relationLabel('student')).toBe("O'zi")
    expect(relationLabel('other')).toBe('Boshqa')
    expect(relationIcon('father')).toBe('👨')
    expect(relationIcon('student')).toBe('🧑')
  })

  it("bo'sh/noma'lum qarindoshlik — ota-ona (server Effective bilan bir xil)", () => {
    expect(normalizeRelation('')).toBe('parent')
    expect(normalizeRelation(undefined)).toBe('parent')
    expect(relationLabel('xyz')).toBe('Ota-onasi')
  })

  it('presetlar: «Barcha ota-onalar» o\'quvchining o\'z chatisiz', () => {
    expect(PARENT_RELATIONS).toEqual(['father', 'mother', 'parent', 'other'])
    expect(STUDENT_RELATIONS).toEqual(['student'])
    expect(isPreset(['other', 'father', 'mother', 'parent'], PARENT_RELATIONS)).toBe(true)
    expect(isPreset(['father', 'mother'], PARENT_RELATIONS)).toBe(false)
  })

  it('toggle tartibni saqlaydi, takror yo\'q', () => {
    expect(toggleRelation(['mother'], 'father')).toEqual(['father', 'mother'])
    expect(toggleRelation(['father', 'mother'], 'father')).toEqual(['mother'])
    expect(sortRelations(['other', 'father', 'father', ''])).toEqual(['father', 'parent', 'other'])
  })

  it('sanoq satri', () => {
    expect(
      countLine({
        messages: 312,
        chats: 287,
        byRelation: [
          { relation: 'student', chats: 27 },
          { relation: 'father', chats: 140 },
          { relation: 'mother', chats: 120 },
        ],
        notConnected: 58,
        blocked: 6,
      }),
    ).toBe("Yuboriladi: 312 ta xabar · 287 ta chat (otasi 140, onasi 120, o'quvchi 27) · Ulanmagan: 58 o'quvchi · Bloklagan: 6")
    expect(countLine({ messages: 0, chats: 0, byRelation: [], notConnected: 0, blocked: 0 })).toBe(
      'Yuboriladi: 0 ta xabar · 0 ta chat',
    )
    expect(countLine({ messages: 2, chats: 2, byRelation: [], notConnected: 3, blocked: 0, teachers: true })).toContain(
      "Ulanmagan: 3 o'qituvchi",
    )
  })

  it('Telegram filtri (U6b)', () => {
    expect(matchesTelegramFilter(0, 'all')).toBe(true)
    expect(matchesTelegramFilter(2, 'linked')).toBe(true)
    expect(matchesTelegramFilter(0, 'linked')).toBe(false)
    expect(matchesTelegramFilter(undefined, 'none')).toBe(true)
    expect(matchesTelegramFilter(1, 'none')).toBe(false)
    // 2026-10-15 (egasi): kabinetga kirgan ham «Ulangan»
    expect(matchesTelegramFilter(0, 'linked', true)).toBe(true)
    expect(matchesTelegramFilter(0, 'none', true)).toBe(false)
    expect(matchesTelegramFilter(2, 'linked', true)).toBe(true)
  })
})

describe('telegramDelivery — holat ko\'rinishi', () => {
  it('qabul qilingani har doim «Qabul qildi»', () => {
    expect(deliveryView('sent', '2026-10-12T10:00:00')).toBe('acked')
    expect(deliveryView('sent')).toBe('sent')
    expect(deliveryView('sending')).toBe('pending')
    expect(deliveryView('blocked')).toBe('blocked')
    expect(deliveryView('expired')).toBe('failed')
    expect(deliveryView('unknown')).toBe('failed')
    expect(deliveryView('cancelled')).toBe('cancelled')
  })

  it('kampaniya yorliqlari va polling', () => {
    expect(campaignKindLabel('archive')).toBe('Arxiv')
    expect(campaignKindLabel('debt')).toBe('Qarz eslatmasi')
    expect(campaignKindLabel('broadcast')).toBe("E'lon")
    expect(isCampaignOpen('queued')).toBe(true)
    expect(isCampaignOpen('done')).toBe(false)
    expect(formatBytes(2.5 * 1024 * 1024)).toBe('2.5 MB')
  })
})
