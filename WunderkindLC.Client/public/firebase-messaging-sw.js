/* Firebase Cloud Messaging — web/PWA push service worker.
 * Web app config query string orqali keladi (webpush.ts uni ro'yxatdan o'tkazganda qo'yadi):
 *   /firebase-messaging-sw.js?config=<encodeURIComponent(JSON)>
 * Konfig OMMAVIY (apiKey/messagingSenderId ochiq kalitlar) — maxfiy emas.
 *
 * DIQQAT (dubl bildirishnoma): server `notification` payload yuboradi (native uchun). Web'da
 * `firebase.messaging()` ishga tushirilsa, background'da bunday xabar SDK tomonidan AVTOMATIK
 * ko'rsatiladi. Shu sabab bu yerda onBackgroundMessage'da showNotification CHAQIRILMAYDI —
 * aks holda ikkita bir xil bildirishnoma chiqadi.
 */
/* global importScripts, firebase, self, clients */

const SDK_VERSION = '10.14.1'
importScripts(`https://www.gstatic.com/firebasejs/${SDK_VERSION}/firebase-app-compat.js`)
importScripts(`https://www.gstatic.com/firebasejs/${SDK_VERSION}/firebase-messaging-compat.js`)

try {
  const params = new URLSearchParams(self.location.search)
  const raw = params.get('config')
  const config = raw ? JSON.parse(decodeURIComponent(raw)) : null

  if (config && config.apiKey) {
    firebase.initializeApp(config)
    // messaging()'ni ishga tushirish yetarli — `notification` payload'li xabar background'da
    // avtomatik ko'rsatiladi. (Data-only xabar bo'lsa handler qo'shish mumkin edi, lekin server
    // doim notification yuboradi, shuning uchun bu yerda hech narsa ko'rsatmaymiz — dubl bo'lmasin.)
    firebase.messaging()
  }
} catch (e) {
  // Konfig noto'g'ri yoki SDK yuklanmadi — SW baribir o'rnatiladi, shunchaki push kelmaydi.
}

// Darhol faollashsin (kutmasin) — o'rnatilgan PWA tez tayyor bo'ladi.
self.addEventListener('install', () => self.skipWaiting())
self.addEventListener('activate', (event) => event.waitUntil(self.clients.claim()))

// Minimal fetch handler — PWA "o'rnatiladigan" (installable) bo'lishi mezoni. Keshlash yo'q,
// so'rovni to'g'ridan-to'g'ri tarmoqqa o'tkazamiz (offline-first kerak bo'lsa keyin qo'shiladi).
self.addEventListener('fetch', () => {
  // no-op passthrough: brauzer standart tarmoq javobidan foydalanadi.
})

// Bildirishnoma bosilganda — ochiq ilova oynasini fokuslash yoki yangisini ochish.
self.addEventListener('notificationclick', (event) => {
  event.notification.close()
  event.waitUntil(
    clients.matchAll({ type: 'window', includeUncontrolled: true }).then((list) => {
      for (const client of list) {
        if ('focus' in client) return client.focus()
      }
      if (clients.openWindow) return clients.openWindow('/')
    }),
  )
})
