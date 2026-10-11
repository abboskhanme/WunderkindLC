import { describe, expect, it } from 'vitest'
import { liveMcpConnections, mcpConnectionState, mcpTime } from '@/lib/mcp'

describe('mcpTime — server UTC vaqti markaz vaqtida (UTC+5)', () => {
  it('Z belgisiz qiymat UTC deb o\'qiladi', () => {
    expect(mcpTime('2026-10-11T07:30:00')).toBe('11.10.2026 12:30')
  })
  it('Z belgili qiymat ham', () => {
    expect(mcpTime('2026-10-11T23:10:00Z')).toBe('12.10.2026 04:10')
  })
  it("buzuq qiymat o'zgarishsiz qaytadi", () => {
    expect(mcpTime('ertaga')).toBe('ertaga')
  })
})

describe('mcpConnectionState', () => {
  it('bekor qilingan — faol bayroqdan ustun', () => {
    expect(mcpConnectionState({ active: true, revokedAt: '2026-10-11T07:30:00' })).toBe('revoked')
  })
  it('faol', () => {
    expect(mcpConnectionState({ active: true, revokedAt: null })).toBe('active')
  })
  it("faol emas va bekor qilinmagan — muddati o'tgan", () => {
    expect(mcpConnectionState({ active: false, revokedAt: null })).toBe('expired')
  })
})

describe('liveMcpConnections', () => {
  it('faqat bekor qilinmaganlarni sanaydi', () => {
    expect(liveMcpConnections([{ revokedAt: null }, { revokedAt: 'x' }, { revokedAt: null }])).toBe(2)
  })
})
