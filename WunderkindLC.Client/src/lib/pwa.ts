/* ============================================================
   PWA o'rnatiladiganligi (installability).
   Push bildirishnomalar 2026-10 da olib tashlandi (markazda o'rnatiladigan ilova ishlatilmaydi) —
   bu yerda faqat service worker ro'yxatdan o'tkaziladi: brauzer "Ilovani o'rnatish" taklifini
   ko'rsatishi uchun minimal fetch handler'li worker kerak.
   ⚠️ Worker fayli ATAYIN eski yo'lda (`/firebase-messaging-sw.js`) qoldi — allaqachon o'rnatilgan
   PWA'lar aynan shu manzilni ro'yxatdan o'tkazgan; yangi nom ularni yetim worker bilan qoldirardi.
   ============================================================ */

/** Service worker ishlay oladimi (HTTPS yoki localhost). */
function canRegisterWorker(): boolean {
  try {
    return (
      typeof window !== 'undefined' &&
      'serviceWorker' in navigator &&
      (window.isSecureContext === true || location.hostname === 'localhost')
    )
  } catch {
    return false
  }
}

/** Startup'da (login'dan OLDIN) service worker'ni ro'yxatdan o'tkazadi. Best-effort. */
export async function registerForInstall(): Promise<void> {
  if (!canRegisterWorker()) return
  try {
    await navigator.serviceWorker.register('/firebase-messaging-sw.js', { scope: '/' })
  } catch {
    /* PWA o'rnatish taklifi chiqmaydi — sayt baribir ishlaydi */
  }
}
