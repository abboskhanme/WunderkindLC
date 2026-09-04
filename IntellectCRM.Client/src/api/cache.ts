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
 *  - INVALIDATSIYA: MIQYOSLI (scoped) — pastdagi "INVALIDATSIYA MIQYOSI" blokiga qarang;
 *  - KESHLANMAYDI: `/auth/` yo'llari (login/refresh/logout — sessiya holati), blob/fayl
 *    javoblari, `X-No-Cache: 1` sarlavhali yoki `cache: false` konfigli so'rovlar;
 *  - faqat 2xx keshlanadi (xato javob keshda qolmaydi); abort bo'lgan so'rov reject bo'lgani
 *    uchun keshga o'z-o'zidan yozilmaydi;
 *  - `signal` (AbortController) bilan kelgan so'rov DEDUP'ga QO'SHILMAYDI: promise ulashilsa,
 *    bittasining aborti boshqasining so'rovini ham uzib qo'yardi (yoki abort e'tiborsiz
 *    qolardi). Keshdan O'QIYDI va muvaffaqiyatda YOZADI — bu xavfsiz.
 *  - hajm chegarasi {@link MAX_ENTRIES} (200): oshsa eng eski yozuv chiqariladi.
 */

/**
 * ─────────────────────────  INVALIDATSIYA MIQYOSI (scoped)  ─────────────────────────
 *
 * ILGARI: har qanday muvaffaqiyatli POST/PUT/PATCH/DELETE BUTUN keshni tozalardi ("qo'pol,
 * lekin xavfsiz"). Amalda bu keshni AYNAN eng kerak paytda o'ldirardi: kassir bitta to'lov
 * kiritsa — o'quvchilar ro'yxati, guruhlar, jadval, hammasi tushib ketardi va keyingi har
 * bosishda ~350-400 ms (Fransiya↔O'zbekiston) qaytadan to'lanardi. Faol ishlayotgan
 * foydalanuvchi keshdan deyarli hech qachon foyda ko'rmasdi.
 *
 * ENDI: mutatsiya URL'idan RESURS MIQYOSI hisoblanadi va faqat o'sha miqyosdagi GET'lar
 * tashlanadi. Miqyos — URL'ning birinchi ikki bo'lagi (`/admin/students`, `/admin/finance`,
 * `/teacher/groups` ...), ya'ni `POST /admin/students/{id}/arxiv` → `/admin/students`.
 *
 * ⚠️ ASOSIY XAVF — "MENING RESURSIM EMAS" o'zgarishlar. Bitta amal ko'pincha BOSHQA bo'lim
 * javobini ham o'zgartiradi: to'lov `/admin/finance` ga tushadi, lekin o'quvchining BALANSI
 * `/admin/students` ro'yxatida ko'rinadi. Shuning uchun quyida OSHKORA kesishma jadvali
 * ({@link CROSS_INVALIDATION}) bor va u ATAYIN saxiy: kassirga eskirgan balans ko'rsatish —
 * qo'shimcha bitta so'rovdan ANCHA yomon xato. Shubha bo'lsa — KO'PROQ tozalanadi.
 *
 * ⚠️ STANDART — TO'LIQ TOZALASH. Jadval "ruxsat etilganlar ro'yxati" (whitelist): miqyosi
 * unda YO'Q bo'lgan har qanday mutatsiya eski yo'ldan boradi — {@link clearApiCache} BUTUN
 * keshni tozalaydi (auth, sozlamalar, ruxsatlar, xodimlar, filiallar va hali o'rganilmagan
 * yangi endpointlar). Bu "escape hatch" ATAYIN saqlangan: yangi endpoint eng yomon holatda
 * kesh FOYDASINI yo'qotadi, lekin hech qachon eski ma'lumot KO'RSATMAYDI.
 *
 * YANGI ENDPOINT QO'SHGANDA:
 *  1. Hech narsa qilmasangiz ham xavfsiz (butun kesh tozalanadi) — shoshilmang.
 *  2. Miqyoslamoqchi bo'lsangiz avval savol bering: *"bu amaldan keyin QAYSI boshqa
 *     bo'limning javobi o'zgaradi?"* — balans, davomat foizi, maosh, jadval, portal.
 *  3. Javobni jadvalga SABAB bilan yozing. Ortiqcha prefiks — bitta qo'shimcha so'rov;
 *     yetishmagan prefiks — ekranda ESKI PUL. Ikkinchisi ancha qimmat.
 */

/**
 * URL'ning birinchi bo'lagi ROL/NAMESPACE bo'lgan oilalar: bularda miqyos IKKI bo'lakdan
 * olinadi (`/admin/students`), aks holda butun `/admin` bitta miqyos bo'lib qolardi va
 * hech narsa yutmasdik. Boshqa (`/dashboard` kabi) yo'llarda BITTA bo'lak — u yerda ikkinchi
 * bo'lak odatda resurs emas, amal/id bo'ladi.
 */
const NAMESPACES = new Set(['admin', 'teacher', 'student', 'parent', 'public', 'portal'])

/**
 * HAR QANDAY mutatsiyada tozalanadigan miqyoslar — bu javoblar deyarli har amaldan keyin
 * o'zgaradi, ya'ni ularni saqlab qolishning ma'nosi yo'q:
 *  • `/admin/audit` — o'zgarishlar tarixiga deyarli har amal qator yozadi (`audit.md` §1);
 *  • `/admin/dashboard` — jamlanma sonlar (to'lov, davomat, lid, guruh — hammasi tegadi).
 */
const ALWAYS_INVALIDATE = ['/admin/audit', '/admin/dashboard']

/**
 * MIQYOSLAB BO'LMAYDIGAN miqyoslar — BUTUN kesh tozalanadi, jadvalda tursa ham.
 * Sabab: bular "men kimman va nimani ko'ra olaman" ni o'zgartiradi, ya'ni ta'siri butun
 * ilovaga yoyiladi: ruxsat olib qo'yilsa `ReadRequiresPerm` endpointlari 403 qaytara
 * boshlaydi (`permissions.md`), filial almashsa har ro'yxat boshqa filialniki bo'ladi,
 * markaz sozlamasi esa yorliq/menyu/modul yoqilishiga tegadi.
 *
 * ⚠️ Bu ro'yxat ATAYIN "qulf" sifatida turadi: kimdir keyinchalik jadvalga `/admin/settings`
 * qo'shib "optimallashtirmoqchi" bo'lsa ham, bu yerdagi tekshiruv oldin ishlaydi.
 */
const FULL_CLEAR_SCOPES = new Set([
  '/auth',            // login/otp-login/refresh/account — boshqa foydalanuvchi bo'lishi mumkin
  '/admin/staff',     // xodim, ROL va RUXSATLAR — javoblarning tarkibi o'zgaradi
  '/admin/settings',  // markaz sozlamalari, modul yoqish/o'chirish, logo, SMS/telegram
  '/admin/branches',  // filial — barcha ro'yxatlar boshqa filialniki bo'ladi
  '/admin/locations',
])

/**
 * ───────────────  KESISHMA JADVALI (WHITELIST)  ───────────────
 *
 * Kalit — mutatsiya MIQYOSI, qiymat — o'sha mutatsiyadan keyin YANA tozalanishi kerak
 * bo'lgan yo'l prefikslari (o'z miqyosi va {@link ALWAYS_INVALIDATE} avtomatik qo'shiladi).
 *
 * ⚠️ Bu ATAYIN "ruxsat etilganlar ro'yxati": jadvalda YO'Q miqyos → BUTUN kesh tozalanadi
 * (eski xatti-harakat). Ya'ni ertaga qo'shiladigan, hali o'rganilmagan endpoint jimgina
 * eskirgan ma'lumot ko'rsatmaydi — u shunchaki keshdan foyda ko'rmaydi. "Qora ro'yxat"
 * (hammasi miqyosli, ba'zilari istisno) teskari xatoga olib borardi: bog'liqlik
 * unutilsa — kassir ekranida ESKI BALANS.
 *
 * ⚠️ Qiymatlar CANONIK miqyos bo'lishi shart emas: taqqoslash bo'lak-prefiks bo'yicha
 * ({@link keyInScope}), shuning uchun `/student` butun o'quvchi portalini, `/teacher/journal`
 * esa faqat o'sha oilani tozalaydi.
 *
 * Har qator SABAB bilan — sababsiz qator keyin "kerakmasmi?" deb olib tashlanadi va
 * bo'shliq jimgina qaytadi.
 */
const CROSS_INVALIDATION: Record<string, string[]> = {
  /* ─────────── PUL ─────────── */
  // Tranzaksiya/to'lov/qaytarish/hisoblash: o'quvchi BALANSI ro'yxatlarda va guruh
  // a'zolari qatorida chiqadi, kitob qarzi va retention-bonus ham shu pul oqimidan.
  '/admin/finance': ['/admin/students', '/admin/kassa', '/admin/classes', '/admin/teachers',
    '/admin/books', '/admin/retention-bonus', '/student/finance'],
  // Kassa — o'sha pul, boshqa eshik (`POST /admin/kassa/students/{id}/payments`).
  '/admin/kassa': ['/admin/finance', '/admin/students', '/admin/classes', '/admin/books',
    '/admin/retention-bonus', '/student/finance'],
  // Kitob sotuvi tasdiqlansa moliya tranzaksiyasi va o'quvchi qarzi o'zgaradi.
  '/admin/books': ['/admin/finance', '/admin/kassa', '/admin/students'],
  // Bonus o'qituvchi maoshidan to'lanadi.
  '/admin/retention-bonus': ['/admin/finance', '/admin/teachers', '/admin/students',
    '/teacher/retention-bonus'],

  /* ─────────── O'QUVCHI · GURUH · A'ZOLIK ─────────── */
  // O'quvchi tahriri/arxivi/to'lovi/oylik tuzatishi: guruh ro'yxatidagi qatori, moliya
  // yozuvi, navbatdagi snapshot, portal ko'rinishi (login blok, sertifikat) — hammasi.
  '/admin/students': ['/admin/classes', '/admin/finance', '/admin/kassa', '/admin/journal',
    '/admin/student-attendance', '/admin/contacts', '/admin/archive', '/admin/grading',
    '/admin/course-analytics', '/admin/test-results', '/admin/parents', '/student'],
  // A'zolik amallari (qo'shish/chiqarish/muzlatish/ko'chirish/yopish) IKKI tomonni ham
  // o'zgartiradi va `billing.md` bo'yicha oylik hisobni qayta yozadi; jadval, xona bandligi
  // va o'qituvchi maoshi ham guruhdan kelib chiqadi.
  '/admin/classes': ['/admin/students', '/admin/finance', '/admin/kassa', '/admin/journal',
    '/admin/student-attendance', '/admin/grading', '/admin/course-analytics', '/admin/schedule',
    '/admin/rooms', '/admin/teachers', '/admin/archive', '/admin/curriculum',
    '/admin/retention-bonus', '/student', '/teacher'],
  // Umumiy arxiv uchta ro'yxatga qaytaradi.
  '/admin/archive': ['/admin/students', '/admin/teachers', '/admin/classes'],
  '/admin/parents': ['/admin/students'],

  /* ─────────── DAVOMAT · BAHO · DASTUR ─────────── */
  // Jurnal AYNI BIR do'konga admin ham, o'qituvchi ham yozadi; davomat foizi o'quvchida,
  // o'tilgan dars esa o'qituvchi MAOSHIDA (`billing.md`) aks etadi.
  '/admin/journal': ['/admin/students', '/admin/classes', '/admin/student-attendance',
    '/admin/teachers', '/admin/teacher-reports', '/admin/finance', '/admin/course-analytics',
    '/student', '/teacher/journal'],
  '/teacher/journal': ['/admin/journal', '/admin/students', '/admin/classes',
    '/admin/student-attendance', '/admin/teachers', '/admin/finance', '/admin/teacher-reports',
    '/student'],
  // Turniket va yuz bilan kirish — DAVOMAT manbalari.
  '/admin/face': ['/admin/student-attendance', '/admin/journal', '/admin/students'],
  // Baho o'quvchi bali/reytingi va guruh ko'rsatkichiga tushadi.
  '/admin/grading': ['/admin/students', '/admin/classes', '/admin/course-analytics',
    '/student', '/teacher/grading'],
  '/teacher/grading': ['/admin/grading', '/admin/students', '/admin/classes', '/student'],
  // O'quv dasturi progressi UCH xil prefiksdan yoziladi, o'qiladi esa bitta joydan.
  '/admin/curriculum': ['/admin/subjects', '/admin/classes', '/admin/course-analytics',
    '/student', '/teacher/curriculum'],
  '/teacher/curriculum': ['/admin/curriculum', '/admin/classes', '/admin/course-analytics',
    '/student'],
  '/student/curriculum': ['/admin/curriculum', '/admin/students', '/admin/classes',
    '/admin/course-analytics'],
  '/admin/subjects': ['/admin/classes', '/admin/curriculum', '/admin/course-analytics'],
  // Test natijasi/sertifikati: admin va o'qituvchi bitta do'konga yozadi (`testCertificates.ts`).
  '/admin/test-results': ['/admin/students', '/admin/classes', '/student', '/teacher/test-results'],
  '/teacher/test-results': ['/admin/test-results', '/admin/students', '/admin/classes', '/student'],
  '/admin/level-tests': ['/admin/leads', '/admin/lead-forms', '/admin/test-results'],

  /* ─────────── O'QITUVCHI ─────────── */
  // O'qituvchi kartochkasi guruhda, jadvalda va maosh hisobida ko'rinadi
  // (`PUT {id}/group-salaries` — maosh stavkasi "teachers" yo'lida, ta'siri moliyada).
  '/admin/teachers': ['/admin/classes', '/admin/schedule', '/admin/finance', '/admin/journal',
    '/admin/teacher-reports', '/admin/archive', '/teacher'],
  '/admin/teacher-attendance': ['/admin/teachers', '/admin/finance', '/teacher/salary'],
  '/admin/teacher-reviews': ['/admin/teachers', '/admin/students'],
  // O'rinbosar — KIM dars o'tadi va KIMGA haq to'lanadi (`substitute-teachers.md`).
  '/admin/substitute-teachers': ['/admin/journal', '/admin/schedule', '/admin/teachers',
    '/admin/finance', '/admin/classes', '/teacher'],
  '/admin/rooms': ['/admin/classes', '/admin/schedule'],

  /* ─────────── LID · MARKETING ─────────── */
  // `POST /admin/leads/{id}/convert` lidni O'QUVCHIGA aylantiradi (tuman/maktabni ham ko'chiradi).
  '/admin/leads': ['/admin/students', '/admin/classes', '/admin/lead-forms', '/admin/lead-sources',
    '/admin/lead-stages', '/admin/level-tests', '/admin/districts', '/admin/schools'],
  // Bosqich/manba nomlari lid qatorlariga denormalizatsiya qilingan.
  '/admin/lead-stages': ['/admin/leads', '/admin/lead-forms'],
  '/admin/lead-sources': ['/admin/leads', '/admin/lead-forms'],
  '/admin/lead-forms': ['/admin/leads', '/admin/lead-sources', '/admin/level-tests'],
  // Instagram suhbatidan LID yaratiladi (`conversations/{id}/create-lead`, ads/leads retry).
  '/admin/instagram': ['/admin/leads', '/admin/lead-sources', '/admin/lead-forms'],
  // Ommaviy forma/test topshirilishi — admin tomonda lid/ariza bo'lib chiqadi.
  '/public/form': ['/admin/leads', '/admin/lead-forms', '/admin/level-tests'],
  '/public/test': ['/admin/leads', '/admin/lead-forms', '/admin/level-tests', '/admin/test-results'],

  /* ─────────── ALOQA · XABAR ─────────── */
  // Talab o'quvchi profilining "Aloqa" tabida va xodim vazifalarida ko'rinadi (`contacts.md`).
  '/admin/contacts': ['/admin/students', '/admin/staff-tasks'],
  // O'qituvchi jurnaldan navbatga yuboradi — natija FAQAT admin modulida ko'rinadi (§3.7).
  '/teacher/groups': ['/admin/contacts', '/admin/students', '/admin/classes', '/admin/staff-tasks'],
  // Qo'ng'iroqlar: `/cti` — o'sha yozuvlarga IKKINCHI eshik.
  '/admin/calls': ['/admin/students', '/admin/contacts'],
  '/cti': ['/admin/calls', '/admin/students', '/admin/contacts'],
  // Xabar/SMS/push yuborilishi qabul qiluvchining portal ro'yxatlarida ko'rinadi.
  '/admin/messages': ['/admin/students', '/admin/notifications', '/student', '/teacher'],
  '/admin/reminders': ['/admin/messages'],
  '/admin/auto-messages': ['/admin/messages'],
  '/teacher/chat': ['/admin/messages'],
  '/student/chat': ['/admin/messages'],
  '/teacher/notifications': ['/admin/messages'],
  '/student/notifications': ['/admin/messages'],
  // Faqat "o'qildi" belgisi — boshqa hech narsaga tegmaydi (Topbar qo'ng'irog'i).
  '/admin/notifications': [],

  /* ─────────── QO'LLAB-QUVVATLASH · FIKR · AI ─────────── */
  '/admin/support': ['/teacher/support', '/student/support'],
  '/teacher/support': ['/admin/support', '/student/support'],
  '/student/support': ['/admin/support', '/teacher/support'],
  '/admin/feedback': ['/student/feedback', '/teacher/feedback'],
  '/teacher/feedback': ['/admin/feedback'],
  '/student/feedback': ['/admin/feedback'],
  // O'quvchi topshirig'i admin navbatida; admin ruxsati esa portal holatida.
  '/student/ai-check': ['/admin/ai-check'],
  '/admin/ai-check': ['/student/ai-check', '/admin/students'],
  // Markaz AI tahlili bir necha bo'lim `ai-analyses` javoblariga yoziladi.
  '/admin/ai-analysis': ['/admin/students', '/admin/teachers', '/admin/classes',
    '/admin/contacts', '/admin/lead-forms', '/admin/level-tests'],

  /* ─────────── HUJJAT · MA'LUMOTNOMA · SAYT ─────────── */
  '/admin/contracts': ['/admin/students', '/student/contracts'],
  '/admin/districts': ['/admin/schools', '/admin/students', '/admin/leads'],
  '/admin/schools': ['/admin/districts', '/admin/students', '/admin/leads'],
  '/admin/cameras': [],
  // Sabablar katalogi o'chirish/muzlatish/chiqarish oqimlarining ochiladigan ro'yxati.
  '/admin/action-reasons': ['/admin/classes', '/admin/students', '/admin/finance',
    '/admin/journal', '/admin/contacts'],
  // Landing/vakansiya CMS — OMMAVIY sahifa va brend nomi (`/school`, `/public/brand`).
  '/admin/landing': ['/public', '/school'],
  '/admin/career': ['/public'],
  '/admin/staff-tasks': [],
  // Topshiriqlar (Kanban) — doska/ustun/topshiriq/izoh hammasi shu miqyosda; boshqa bo'lim
  // ro'yxatlariga ta'sir qilmaydi, shuning uchun bog'liq miqyos yo'q.
  '/admin/work-tasks': [],
  // Sof fayl yuklash — hech qanday ro'yxatni o'zgartirmaydi (manzil javobda qaytadi).
  '/admin/uploads': [],
}

/**
 * URL → miqyos. Query, host va oxirgi `/` tashlanadi.
 * `null` — miqyosni ishonchli aniqlab bo'lmadi (bo'sh yoki absolut URL): chaqiruvchi
 * BUTUN keshni tozalaydi (xavfsiz tomon).
 */
export function scopeOf(url: string | undefined): string | null {
  if (!url) return null
  // Absolut URL (boshqa host/baseURL bilan aralashish) — miqyos solishtirish kalitlari
  // nisbiy yo'llardan quriladi, ya'ni ishonchli taqqoslab bo'lmaydi.
  if (url.includes('://')) return null
  const path = url.split('?')[0].split('#')[0]
  const parts = path.split('/').filter(Boolean)
  if (parts.length === 0) return null
  const depth = NAMESPACES.has(parts[0].toLowerCase()) ? 2 : 1
  if (parts.length < depth) return null
  return '/' + parts.slice(0, depth).join('/')
}

/**
 * Kesh kaliti shu miqyosga tegishlimi. Kalit — `url?params`, shuning uchun avval yo'l
 * ajratiladi. Taqqoslash BO'LAK darajasida: `/admin/student` miqyosi `/admin/students` ni
 * TUTMAYDI (oddiy `startsWith` shu xatoni qilardi).
 */
export function keyInScope(key: string, scope: string): boolean {
  const path = key.split('?')[0]
  const normalized = path.startsWith('/') ? path : '/' + path
  return normalized === scope || normalized.startsWith(scope + '/')
}

/**
 * Mutatsiya URL'i uchun tozalanadigan miqyoslar ro'yxati.
 * `null` — miqyoslab bo'lmaydi, BUTUN kesh tozalanadi.
 */
export function invalidationScopesFor(url: string | undefined): string[] | null {
  const scope = scopeOf(url)
  if (!scope) return null                        // miqyos aniqlanmadi → to'liq tozalash
  if (FULL_CLEAR_SCOPES.has(scope)) return null  // global ta'sirli → to'liq tozalash
  const cross = CROSS_INVALIDATION[scope]
  if (!cross) return null                        // jadvalda YO'Q → to'liq tozalash (whitelist)
  return [...new Set<string>([scope, ...cross, ...ALWAYS_INVALIDATE])]
}

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

// INVALIDATSIYA SHTAMPLARI. Ilgari bitta global "avlod" hisoblagichi bor edi: har mutatsiya
// uni oshirar va undan OLDIN boshlangan GET javobi keshga yozilmasdi (aks holda tugab qolgan
// eski so'rov keshni yana eski ma'lumot bilan to'ldirardi). Invalidatsiya MIQYOSLI bo'lgach
// bu hisoblagich ham miqyosli bo'lishi kerak: `/admin/finance` ga qilingan POST
// `/admin/rooms` ga ketayotgan GET'ni bekorga bekor qilmasin.
//
// `seq` — umumiy o'suvchi hisoblagich; `scopeStamps` — miqyos → oxirgi invalidatsiya raqami;
// `globalStamp` — oxirgi TO'LIQ tozalash raqami. GET boshlanishida o'z kaliti uchun shtamp
// olinadi, yozishdan oldin qayta hisoblanadi: farq bo'lsa — javob eskirgan, yozilmaydi.
let seq = 0
let globalStamp = 0
const scopeStamps = new Map<string, number>()

// Shtamplar xaritasi cheksiz o'smasin (miqyoslar soni cheklangan bo'lsa ham — himoya).
const MAX_SCOPE_STAMPS = 100

function setScopeStamp(scope: string, stamp: number): void {
  scopeStamps.delete(scope) // insertion-order yangilansin (eng eskisi birinchi chiqadi)
  scopeStamps.set(scope, stamp)
  while (scopeStamps.size > MAX_SCOPE_STAMPS) {
    const oldest = scopeStamps.keys().next().value
    if (oldest === undefined) break
    const dropped = scopeStamps.get(oldest) ?? 0
    scopeStamps.delete(oldest)
    // Tashlangan shtamp GLOBAL'ga ko'tariladi: shu miqyos "unutilgani" tufayli eskirgan
    // javob keshga tushib qolmasin (ortiqcha ehtiyot — faqat uchayotgan so'rovlarga tegadi).
    if (dropped > globalStamp) globalStamp = dropped
  }
}

/** Shu kalit uchun oxirgi invalidatsiya raqami (global + moslashadigan miqyoslar ichidan eng kattasi). */
function stampFor(key: string): number {
  let max = globalStamp
  for (const [scope, stamp] of scopeStamps) {
    if (stamp > max && keyInScope(key, scope)) max = stamp
  }
  return max
}

/** Berilgan miqyoslardagi yozuvlarni (va ulashilayotgan so'rovlarni) tashlaydi. */
function invalidateScopes(scopes: string[]): void {
  for (const scope of scopes) {
    seq++
    setScopeStamp(scope, seq)
    for (const key of [...store.keys()]) if (keyInScope(key, scope)) store.delete(key)
    // In-flight: mutatsiyadan OLDIN boshlangan so'rovni yangi chaqiruvchiga ulashib bo'lmaydi
    // (javobi eskirgan bo'lishi mumkin) — dedup xaritasidan chiqariladi, so'rovning o'zi esa
    // uni boshlagan chaqiruvchiga odatdagidek yetib boradi.
    for (const key of [...inflight.keys()]) if (keyInScope(key, scope)) inflight.delete(key)
  }
}

/**
 * BUTUN keshni tozalaydi — "escape hatch". Miqyoslab bo'lmaydigan har narsa uchun:
 * auth, sozlamalar, ruxsat/rol, filial, tanib bo'lmagan URL (qarang {@link FULL_CLEAR_SCOPES}).
 * Testlar ham shuni chaqiradi.
 */
export function clearApiCache(): void {
  seq++
  globalStamp = seq
  scopeStamps.clear()
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

  // Non-GET: tarmoqqa uzatamiz; MUVAFFAQIYATDA o'zgargan RESURS miqyosi (va kesishma
  // jadvalidagi qo'shnilari) tozalanadi — reject'da emas, xato hech narsani o'zgartirmagan.
  // Miqyosni aniqlab bo'lmasa yoki u "global ta'sirli" bo'lsa — butun kesh tozalanadi.
  // HEAD/OPTIONS ma'lumot o'zgartirmaydi, hech narsa tozalamaydi.
  if (method !== 'get') {
    const response = await base(config)
    if (method === 'post' || method === 'put' || method === 'patch' || method === 'delete') {
      const scopes = invalidationScopesFor(config.url)
      if (scopes) invalidateScopes(scopes)
      else clearApiCache()
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

  // 3) Tarmoq. Shtamp yozish paytida qayta tekshiriladi: so'rov ketayotganda AYNAN SHU
  // kalitga tegadigan invalidatsiya bo'lgan bo'lsa (kimdir POST qildi) — bu javob endi eski,
  // keshga tushmaydi. Boshqa miqyosdagi mutatsiya esa bu so'rovga tegmaydi.
  const startStamp = stampFor(key)
  const exec = base(config).then((response) => {
    if (stampFor(key) === startStamp && isCacheableResponse(response)) {
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
