/**
 * Bitta forma ochilishi uchun SO'ROV KALITI. Server shu kalit bilan kelgan takroriy so'rovni
 * yangi yozuv sifatida emas, avvalgisi sifatida qaytaradi (to'lov ikki marta o'tmasin).
 */
export function newRequestId(): string {
  if (typeof crypto !== 'undefined' && typeof crypto.randomUUID === 'function') return crypto.randomUUID()
  return `${Date.now().toString(36)}-${Math.random().toString(36).slice(2, 12)}`
}
