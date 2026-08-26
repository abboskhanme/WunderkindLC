import axios, {
  type AxiosAdapter,
  type AxiosInstance,
  type AxiosResponse,
  type InternalAxiosRequestConfig,
} from 'axios'

/**
 * SHAFFOF GET-KESH — axios ADAPTER darajasida.
 *
 * Nega adapter (interceptor emas)? Request-interceptor'dan keshlangan javob qaytarishning toza
 * yo'li yo'q (interceptor faqat config qaytaradi, javobni qaytarish uchun reject-hack kerak
 * bo'lardi). Adapter esa aynan "config → javob" nuqtasi: keshda bo'lsa tarmoqqa umuman
 * chiqmaymiz, bo'lmasa asl adapterga uzatamiz. Response-interceptorlar (401→refresh) esa
 * odatdagidek ishlayveradi — keshlangan javob 2xx bo'lgani uchun ularga tegmaydi.
 *
 * Qoidalar:
 *  - faqat GET, TTL {@link TTL_MS} (25 s) — sahifalar orasida yurishda ro'yxatlar qayta tortilmaydi;
 *  - IN-FLIGHT DEDUP: bir xil GET parallel kelsa bitta tarmoq so'rovi ulashiladi;
 *  - INVALIDATSIYA: HAR QANDAY muvaffaqiyatli non-GET (POST/PUT/PATCH/DELETE) BUTUN keshni
 *    tozalaydi. Qo'pol, lekin xavfsiz: tahrirdan keyin hech qayerda eski ma'lumot qolmaydi.
 *    Nozik kalit-darajali invalidatsiya ATAYIN yo'q — xato qilish oson, foyda kam (TTL 25 s).
 *  - KESHLANMAYDI: `/auth/` yo'llari (login/refresh/logout — sessiya holati), blob/fayl
 *    javoblari, `X-No-Cache: 1` sarlavhali yoki `cache: false` konfigli so'rovlar;
 *  - faqat 2xx keshlanadi (xato javob keshda qolmaydi); abort bo'lgan so'rov reject bo'lgani
 *    uchun keshga o'z-o'zidan yozilmaydi;
 *  - `signal` (AbortController) bilan kelgan so'rov DEDUP'ga QO'SHILMAYDI: promise ulashilsa,
 *    bittasining aborti boshqasining so'rovini ham uzib qo'yardi (yoki abort e'tiborsiz
 *    qolardi). Keshdan O'QIYDI va muvaffaqiyatda YOZADI — bu xavfsiz.
 *  - hajm chegarasi {@link MAX_ENTRIES} (200): oshsa eng eski yozuv chiqariladi.
 */

/** Yozuv qancha yashaydi. Qisqa ATAYIN: fokus/visibility'da tozalash shart emas, o'zi eskiradi. */
export const TTL_MS = 25_000

/** Keshdagi maksimal yozuvlar soni — xotira cheksiz o'smasin. */
export const MAX_ENTRIES = 200

/** So'rov darajasida keshni o'chirish sarlavhasi: `api.get(url, { headers: { 'X-No-Cache': '1' } })`. */
export const NO_CACHE_HEADER = 'X-No-Cache'

// Axios konfigiga ixtiyoriy `cache: false` maydonini qo'shamiz — sarlavhaga alternativa
// (sarlavha serverga ketmaydi, biz uni jo'natishdan oldin O'CHIRAMIZ, lekin config-maydon
// usuli CORS/typo xavfisiz). Hozircha hech qaysi chaqiruvda ishlatilmaydi — faqat mexanizm.
declare module 'axios' {
  export interface AxiosRequestConfig {
    /** `false` — shu so'rov kesh qatlamini chetlab o'tadi (o'qish ham, yozish ham yo'q). */
    cache?: boolean
  }
}

interface CacheEntry {
  expiresAt: number
  response: AxiosResponse
}

// Insertion-order Map: eviction "eng eskisi chiqadi" uchun birinchi kalit o'chiriladi.
const store = new Map<string, CacheEntry>()

// Bir xil kalitli parallel GET'lar ulashadigan promise'lar.
const inflight = new Map<string, Promise<AxiosResponse>>()

// AVLOD hisoblagichi: non-GET muvaffaqiyatida oshadi. Undan OLDIN boshlangan GET javobi
// keshga YOZILMAYDI — aks holda invalidatsiyadan keyin tugagan eski so'rov keshni yana
// eski ma'lumot bilan to'ldirib qo'yardi.
let generation = 0

/** Butun keshni tozalaydi (non-GET muvaffaqiyati; testlar ham chaqiradi). */
export function clearApiCache(): void {
  generation++
  store.clear()
  inflight.clear()
}

/** Faqat testlar uchun: keshdagi yozuvlar soni. */
export function apiCacheSize(): number {
  return store.size
}

/**
 * Params'ni BARQAROR satrga aylantiradi: kalitlar tartibi farq qilmasin
 * (`{a:1,b:2}` va `{b:2,a:1}` — bitta kesh yozuvi).
 */
export function stableStringify(value: unknown): string {
  if (value === undefined) return ''
  if (value === null || typeof value !== 'object') return JSON.stringify(value)
  if (value instanceof URLSearchParams) return value.toString()
  if (Array.isArray(value)) return `[${value.map(stableStringify).join(',')}]`
  const obj = value as Record<string, unknown>
  const parts = Object.keys(obj)
    .sort()
    .filter((k) => obj[k] !== undefined)
    .map((k) => `${JSON.stringify(k)}:${stableStringify(obj[k])}`)
  return `{${parts.join(',')}}`
}

/** Kesh kaliti: url + barqaror params. baseURL bitta instansiyada o'zgarmas — kalitga kirmaydi. */
export function cacheKeyOf(config: InternalAxiosRequestConfig): string {
  return `${config.url ?? ''}?${stableStringify(config.params)}`
}

/** `X-No-Cache` sarlavhasi bormi — tekshiradi va serverga KETMASLIGI uchun o'chiradi. */
function consumeNoCacheHeader(config: InternalAxiosRequestConfig): boolean {
  const headers = config.headers
  if (!headers) return false
  const value = headers.get?.(NO_CACHE_HEADER) ?? headers[NO_CACHE_HEADER]
  if (value === undefined || value === null || value === false) return false
  headers.delete?.(NO_CACHE_HEADER)
  return true
}

/** Bu GET so'rovni keshlash mumkinmi? (metod tekshiruvi chaqiruvchida) */
function isCacheableGet(config: InternalAxiosRequestConfig, noCacheHeader: boolean): boolean {
  if (noCacheHeader || config.cache === false) return false
  // Auth yo'llari: sessiya holatiga bog'liq, keshlash xavfli (masalan /auth/me eski qolardi).
  if ((config.url ?? '').includes('/auth/')) return false
  // Fayl/blob javoblari kesh uchun emas (katta hajm, bir martalik yuklab olish).
  const rt = config.responseType
  if (rt && rt !== 'json' && rt !== 'text') return false
  return true
}

/** Faqat 2xx keshlanadi (axios default validateStatus'da adapter baribir faqat 2xx resolve qiladi). */
function isCacheableResponse(response: AxiosResponse): boolean {
  return response.status >= 200 && response.status < 300
}

/**
 * Keshlangan javobning NUSXASINI qaytaradi: `data` chuqur klonlanadi, `config` — joriy
 * so'rovniki. Aks holda ikki sahifa BITTA obyektni ulashib, biri mutatsiya qilsa
 * ikkinchisida (va keshda) ham o'zgarib qolardi.
 */
function toFreshResponse(cached: AxiosResponse, config: InternalAxiosRequestConfig): AxiosResponse {
  return { ...cached, config, data: cloneData(cached.data) }
}

function cloneData<T>(data: T): T {
  if (data === null || typeof data !== 'object') return data
  try {
    return structuredClone(data)
  } catch {
    // structuredClone yo'q/klonlab bo'lmaydigan qiymat — asl obyekt qaytadi (keshsiz holatdagidek).
    return data
  }
}

function writeCache(key: string, response: AxiosResponse): void {
  // Chegara: eng eski yozuv(lar) chiqadi (Map insertion-order).
  while (store.size >= MAX_ENTRIES) {
    const oldest = store.keys().next().value
    if (oldest === undefined) break
    store.delete(oldest)
  }
  store.set(key, { expiresAt: Date.now() + TTL_MS, response })
}

/**
 * Kesh adapterini instansiyaga o'rnatadi — `client.ts` dan BIR marta chaqiriladi.
 * Asl adapterni (xhr/fetch) o'rab oladi; request-interceptorlar (CSRF) adapterdan OLDIN,
 * response-interceptorlar (401→refresh) KEYIN ishlaydi — ularga tegilmaydi.
 * Refresh'dan keyin qayta yuborilgan so'rov ham shu adapterdan o'tadi va odatdagidek keshlanadi.
 */
export function installApiCache(instance: AxiosInstance): void {
  const base: AxiosAdapter = axios.getAdapter(instance.defaults.adapter ?? axios.defaults.adapter)
  instance.defaults.adapter = (config) => cachedAdapter(config, base)
}

async function cachedAdapter(
  config: InternalAxiosRequestConfig,
  base: AxiosAdapter,
): Promise<AxiosResponse> {
  const noCacheHeader = consumeNoCacheHeader(config)
  const method = (config.method ?? 'get').toLowerCase()

  // Non-GET: tarmoqqa uzatamiz; MUVAFFAQIYATDA butun kesh tozalanadi (reject'da emas —
  // xato hech narsani o'zgartirmagan). HEAD/OPTIONS ma'lumot o'zgartirmaydi, tozalamaydi.
  if (method !== 'get') {
    const response = await base(config)
    if (method === 'post' || method === 'put' || method === 'patch' || method === 'delete') {
      clearApiCache()
    }
    return response
  }

  if (!isCacheableGet(config, noCacheHeader)) return base(config)

  const key = cacheKeyOf(config)

  // 1) Muddati o'tmagan yozuv — tarmoqsiz qaytadi.
  const hit = store.get(key)
  if (hit) {
    if (hit.expiresAt > Date.now()) return toFreshResponse(hit.response, config)
    store.delete(key)
  }

  // 2) In-flight dedup — signal'siz so'rovlar bitta promise'ni ulashadi.
  const shareable = !config.signal
  if (shareable) {
    const pending = inflight.get(key)
    if (pending) return pending.then((response) => toFreshResponse(response, config))
  }

  // 3) Tarmoq. Avlod yozish paytida tekshiriladi: so'rov ketayotganda invalidatsiya
  // bo'lgan bo'lsa (kimdir POST qildi) — bu javob endi eski, keshga tushmaydi.
  const startGeneration = generation
  const exec = base(config).then((response) => {
    if (generation === startGeneration && isCacheableResponse(response)) {
      writeCache(key, response)
    }
    return response
  })

  if (shareable) {
    inflight.set(key, exec)
    exec
      .catch(() => {}) // xato chaqiruvchiga `exec` orqali yetadi; bu zanjir faqat tozalash uchun
      .finally(() => {
        if (inflight.get(key) === exec) inflight.delete(key)
      })
  }

  // Chaqiruvchiga ham NUSXA beriladi — keshdagi asl obyekt mutatsiyadan himoyalanadi.
  return exec.then((response) => toFreshResponse(response, config))
}
