/**
 * PostHog LAZY fasadi.
 *
 * ⚠️ `posthog-js` ATAYIN statik import qilinmaydi — SDK og'ir va boshlang'ich (eager)
 * chunk'ka kirmasligi kerak. `main.tsx` birinchi chizishdan keyin (idle paytda)
 * `loadPosthog()` ni chaqiradi; qolgan barcha kod esa quyidagi yengil fasad orqali
 * ishlaydi. SDK hali yuklanmagan payt kelgan `capture`/`identify`/`reset` chaqiriqlari
 * NAVBATGA olinadi va yuklangach ayni tartibda qayta ijro etiladi — hodisa yo'qolmaydi.
 */
import type posthogJs from 'posthog-js'

/** Haqiqiy posthog-js instansiyasining turi (faqat tip — bundle'ga kirmaydi). */
type PostHogInstance = typeof posthogJs

const projectToken = import.meta.env.VITE_POSTHOG_KEY
const host = import.meta.env.VITE_POSTHOG_HOST

let real: PostHogInstance | null = null
let loadPromise: Promise<PostHogInstance | null> | null = null
/** true — env sozlanmagan, SDK hech qachon yuklanmaydi (chaqiriqlar no-op). */
let disabled = false
/** SDK yuklanguncha kelgan chaqiriqlar navbati (yuklangach FIFO tartibda ijro etiladi). */
const queue: Array<(ph: PostHogInstance) => void> = []

/**
 * SDK'ni dinamik yuklab, init qiladi va navbatni bo'shatadi. Idempotent — necha marta
 * chaqirilsa ham yuklash BIR marta bo'ladi. Sozlanmagan bo'lsa (env yo'q) null qaytaradi
 * va navbat tozalanadi (cheksiz o'sib ketmasin).
 */
export function loadPosthog(): Promise<PostHogInstance | null> {
  if (!loadPromise) {
    loadPromise = (async () => {
      if (projectToken && host) {
        const { default: posthog } = await import('posthog-js')

        posthog.init(projectToken, {
          api_host: host,
          defaults: '2026-05-30',

          // ── Avtomatik kuzatuv ──────────────────────────────────────────
          // Barcha kliklar, forma o'zgarishlari va sahifa almashinuvi
          // hech qanday qo'shimcha kod yozmasdan yoziladi.
          autocapture: true,

          // ── Sahifa ko'rishlar ──────────────────────────────────────────
          // Qaysi sahifa qancha marta ochilgan — avtomatik.
          capture_pageview: true,

          // ── Sessiya yozuvi ─────────────────────────────────────────────
          // Har bir foydalanuvchi sessiyasini PostHog dashboardida
          // "video" kabi ko'rish mumkin: qayerga bosdi, nima qildi.
          session_recording: {
            maskAllInputs: false,       // parol maydonlari avtomatik berkitiladi
            maskInputOptions: {
              password: true,           // parollar HECH QACHON yozilmaydi
              email: false,
            },
          },

          // ── Xato kuzatuvi ─────────────────────────────────────────────
          // So'ralmagan JS xatolari va Promise rejection'lari avtomatik.
          capture_exceptions: {
            capture_unhandled_errors: true,
            capture_unhandled_rejections: true,
            capture_console_errors: false,
          },
        })

        real = posthog
        // Yuklanguncha to'plangan chaqiriqlarni ayni tartibda ijro etamiz.
        for (const fn of queue.splice(0)) fn(posthog)
        return posthog
      }

      // Sozlanmagan — navbatni bo'shatamiz, bundan keyingi chaqiriqlar no-op.
      disabled = true
      queue.length = 0

      if (import.meta.env.DEV) {
        const missingVariable = projectToken ? 'VITE_POSTHOG_HOST' : 'VITE_POSTHOG_KEY'

        throw new Error(
          `${missingVariable} variable required by PostHog is missing or un-configured, this causes events to be silently missed. This error stops appearing once ${missingVariable} is configured`,
        )
      }

      return null
    })()
  }
  return loadPromise
}

/** Yuklangan bo'lsa darhol, bo'lmasa navbatga qo'yib bajaradi (sozlanmagan — no-op). */
function run(fn: (ph: PostHogInstance) => void): void {
  if (real) fn(real)
  else if (!disabled) queue.push(fn)
}

/**
 * Ilova kodi ishlatadigan KICHIK sirt — hammasi async-xavfsiz: SDK yuklanmagan bo'lsa
 * navbatga tushadi, sozlanmagan bo'lsa jim no-op. Sinxron o'quvchilar
 * (`get_distinct_id`) yuklanmaguncha `undefined` qaytaradi — chaqiruvchilar
 * `?.()` bilan ishlaydi.
 */
const posthog = {
  capture(...args: Parameters<PostHogInstance['capture']>): void {
    run((ph) => { ph.capture(...args) })
  },
  identify(...args: Parameters<PostHogInstance['identify']>): void {
    run((ph) => { ph.identify(...args) })
  },
  reset(...args: Parameters<PostHogInstance['reset']>): void {
    run((ph) => { ph.reset(...args) })
  },
  get_distinct_id(): string | undefined {
    return real?.get_distinct_id()
  },
  get_session_id(): string | undefined {
    return real?.get_session_id()
  },
}

export default posthog
