import { readdirSync, readFileSync, statSync } from 'node:fs'
import { join } from 'node:path'
import { fileURLToPath } from 'node:url'
import { describe, expect, it } from 'vitest'
import { OSM_ATTRIBUTION, OSM_REFERRER_POLICY, OSM_TILE_URL } from '../osmTiles'

const srcDir = fileURLToPath(new URL('../../', import.meta.url))

function sourceFiles(dir: string): string[] {
  return readdirSync(dir).flatMap((name) => {
    const p = join(dir, name)
    if (statSync(p).isDirectory()) return name === '__tests__' ? [] : sourceFiles(p)
    return /\.(ts|tsx)$/.test(name) ? [p] : []
  })
}

/**
 * Prod'da xarita o'rniga "403 Access blocked" chiqqan (2026-09-29): server `no-referrer` qo'yadi,
 * OSM esa Referer'siz so'rovni bloklaydi. Tuzatish `OsmTileLayer` da — shu testlar uni qulflaydi.
 */
describe('OSM xarita qatlami', () => {
  it("Referer'ni O'CHIRMAYDI (aks holda OSM xaritani bloklaydi)", () => {
    expect(OSM_REFERRER_POLICY).not.toBe('no-referrer')
    expect(OSM_REFERRER_POLICY).not.toBe('same-origin')
    // Boshqa domenga to'liq yo'l ham KETMASIN — faqat origin.
    expect(OSM_REFERRER_POLICY).not.toBe('unsafe-url')
    expect(OSM_REFERRER_POLICY).not.toBe('no-referrer-when-downgrade')
  })

  it('subdomensiz manzil va atribusiya (tile usage policy talabi)', () => {
    expect(OSM_TILE_URL).toBe('https://tile.openstreetmap.org/{z}/{x}/{y}.png')
    expect(OSM_ATTRIBUTION).toContain('openstreetmap.org/copyright')
  })

  it("xarita qatlami FAQAT OsmTileLayer orqali — sahifada yalang <TileLayer> yo'q", () => {
    // Yalang TileLayer yana qo'shilsa u referrerPolicy'siz bo'ladi va prod'da jimgina bloklanadi
    // (lokalda dev serverda CSP/Referrer-Policy boshqacha bo'lgani uchun sezilmaydi).
    const offenders = sourceFiles(srcDir)
      .filter((f) => !f.endsWith(join('components', 'map', 'OsmTileLayer.tsx')))
      .filter((f) => /<TileLayer\b|tile\.openstreetmap\.org/.test(readFileSync(f, 'utf8')))
      .filter((f) => !f.endsWith(join('lib', 'osmTiles.ts')))
    expect(offenders).toEqual([])
  })
})
