/* PWA service worker — faqat "o'rnatiladigan" (installable) bo'lish uchun.
 *
 * Push bildirishnomalar (Firebase Cloud Messaging) 2026-10 da olib tashlandi: markazda
 * o'rnatiladigan mobil ilova ishlatilmaydi, xabarlar faqat Telegram orqali boradi.
 * ⚠️ Fayl nomi ATAYIN o'zgarmadi — allaqachon o'rnatilgan PWA'lar shu manzilni ro'yxatdan
 * o'tkazgan; brauzer yangilangan worker'ni o'zi yuklab oladi (Firebase importlarisiz).
 */
/* global self */

// Darhol faollashsin (kutmasin) — o'rnatilgan PWA tez tayyor bo'ladi.
self.addEventListener('install', () => self.skipWaiting())
self.addEventListener('activate', (event) => event.waitUntil(self.clients.claim()))

// Minimal fetch handler — PWA "o'rnatiladigan" (installable) bo'lishi mezoni. Keshlash yo'q,
// so'rovni to'g'ridan-to'g'ri tarmoqqa o'tkazamiz.
self.addEventListener('fetch', () => {
  // no-op passthrough: brauzer standart tarmoq javobidan foydalanadi.
})
