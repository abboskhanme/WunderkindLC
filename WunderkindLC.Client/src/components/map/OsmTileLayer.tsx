import { TileLayer } from 'react-leaflet'
import { OSM_ATTRIBUTION, OSM_REFERRER_POLICY, OSM_TILE_URL } from '@/lib/osmTiles'

/**
 * OpenStreetMap xarita qatlami — loyihadagi BARCHA Leaflet xaritalari shuni ishlatadi.
 *
 * ⚠️ `referrerPolicy` SHART: server hamma sahifaga `Referrer-Policy: no-referrer` qo'yadi
 * (`Program.cs`, `/uploads` manzillari sizib chiqmasin), OSM esa `Referer`siz so'rovga
 * xarita o'rniga "403 Access blocked" rasmini qaytaradi (tile usage policy). Rasm darajasidagi
 * atribut hujjat siyosatidan USTUN turadi — ya'ni faqat xarita so'rovlariga Referer qaytadi va
 * u ham faqat ORIGIN (`https://lc.wunderkindedu.uz/`), sahifa yo'li emas.
 *
 * Atribusiya ham policy talabi — olib tashlamang.
 */
export function OsmTileLayer() {
  return (
    <TileLayer
      url={OSM_TILE_URL}
      attribution={OSM_ATTRIBUTION}
      referrerPolicy={OSM_REFERRER_POLICY}
      maxZoom={19}
    />
  )
}
