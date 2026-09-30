/**
 * OpenStreetMap xarita qatlami sozlamalari — YAGONA manba (`components/map/OsmTileLayer`).
 *
 * ⚠️ Komponent va konstanta bir faylda ARALASHMAYDI (eslint `react-refresh/only-export-components`).
 */

/**
 * Subdomensiz manzil — OSM'ning hozirgi tavsiyasi (`a./b./c.` eskirgan: HTTP/2 da foydasi yo'q,
 * ustiga keshni uch baravar bo'lib yuboradi).
 */
export const OSM_TILE_URL = 'https://tile.openstreetmap.org/{z}/{x}/{y}.png'

export const OSM_ATTRIBUTION = '&copy; <a href="https://www.openstreetmap.org/copyright">OpenStreetMap</a>'

/**
 * Xarita rasmlari uchun Referer siyosati. Sahifaning umumiy `no-referrer` siyosati bu yerda
 * YARAMAYDI — OSM Referer'siz so'rovni bloklaydi. Boshqa domenga faqat ORIGIN ketadi (yo'l emas).
 */
export const OSM_REFERRER_POLICY = 'strict-origin-when-cross-origin' as const
