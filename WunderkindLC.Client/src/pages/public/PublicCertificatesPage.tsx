import { useEffect, useState } from 'react'
import { Link } from 'react-router-dom'
import {
  Award,
  Search,
  CheckCircle2,
  X,
  Eye,
  Send,
  Sparkles,
  ArrowLeft,
  Share2,
  ChevronDown,
} from 'lucide-react'
import { api } from '@/api/client'

/**
 * `GET /api/public/landing-data` → `certificates[]` (server: `LandingCertificate` entity, camelCase).
 *
 * ⚠️ MAYDON NOMLARI SERVERDAGIDEK: `listening` / `reading` / `writing` / `speaking`.
 * Ilgari bu yerda `listeningScore` va h.k. yozilgan edi — bunday maydon javobda UMUMAN YO'Q,
 * shuning uchun ballar bo'limi hech qachon chizilmasdi (jimgina bo'sh qolardi).
 * `category` ham qo'shildi: filtr statik `sertifikatlar.js` dagi kabi shu maydonga ham qarashi kerak.
 */
export interface PublicCertItem {
  id: string
  title: string
  studentName: string
  category: string
  certType: string
  overallScore: string
  listening?: string
  reading?: string
  writing?: string
  speaking?: string
  resultNote?: string
  imageUrl: string
  order: number
}

/**
 * Manzil hash'idan kesim: `#oliygoh` → oliygohga kirishlar, `#sertifikatlar` → sertifikatlar.
 * Statik `sertifikatlar.js` dagi `applyHashFilter` bilan AYNAN bir xil qoida.
 */
function categoryFromHash(): string {
  const h = (window.location.hash || '').replace('#', '').toLowerCase()
  if (h === 'oliygoh') return 'oliygoh'
  if (h === 'sertifikatlar' || h === 'sertifikat') return 'sertifikat'
  return 'all'
}

/**
 * OLIYGOHGA KIRISH natijasimi? Yagona ta'rif — "Oliygohga kirishlar" va "Sertifikatlar"
 * kesimlari ustma-ust tushmasin (statik `sertifikatlar.js` dagi `isOliygoh` bilan bir xil).
 */
function isOliygoh(item: { category?: string; certType?: string; title?: string }): boolean {
  const cat = (item.category || '').toLowerCase()
  const type = (item.certType || '').toLowerCase()
  const title = (item.title || '').toLowerCase()
  return cat.includes('oliygoh') || type.includes('oliygoh') ||
    title.includes('oliygoh') || title.includes('universitet') || title.includes('grant')
}

export function PublicCertificatesPage() {
  const [certs, setCerts] = useState<PublicCertItem[]>([])
  const [loading, setLoading] = useState(true)
  // ⚠️ Xato holati BO'SH holatdan ATAYIN ajratilgan: so'rov yiqilganda "sertifikat topilmadi"
  // deyish foydalanuvchini chalg'itardi (markazda sertifikat yo'q deb tushunardi).
  const [loadError, setLoadError] = useState(false)
  const [search, setSearch] = useState('')
  // Navdagi "Yutuqlar → Oliygohga kirishlar / Sertifikatlar" bandlari shu sahifaga hash bilan
  // keladi. Noma'lum hash e'tiborsiz qoldiriladi — sahifa hech qachon bo'sh ochilmasin.
  const [activeCategory, setActiveCategory] = useState<string>(() => categoryFromHash())
  /** Nav menyusi ochiqmi (sensorli ekranda hover yo'q — bosish bilan ochiladi). */
  const [navOpen, setNavOpen] = useState(false)
  const [selectedImg, setSelectedImg] = useState<string | null>(null)
  const [selectedCert, setSelectedCert] = useState<PublicCertItem | null>(null)

  // ⚠️ Zaxira qiymatlar ATAYIN BO'SH — `Program.cs` dagi CenterMeta default'lari bilan bir xil
  // printsip. Ilgari bu yerda BOSHQA markazning haqiqiy telefoni, manzili va ijtimoiy tarmoqlari
  // turardi: CMS to'ldirilmagan bo'lsa (yoki so'rov kechikkanida) ular ekranda CHIQIB QOLARDI.
  const [socials, setSocials] = useState({
    telegramUrl: '',
    instagramUrl: '',
    youtubeUrl: '',
    facebookUrl: '',
    centerEmail: '',
    contactPhone: '',
    centerAddress: '',
    workingHours: '',
  })

  useEffect(() => {
    // Load public landing data
    api
      .get('/public/landing-data')
      .then((res) => {
        // Bo'sh massiv — TO'LIQ HUQUQLI javob (CMS'ga hali sertifikat kiritilmagan), shuning
        // uchun `if (res.data?.certificates)` sharti olib tashlandi: u bo'sh ro'yxatni ham,
        // maydon umuman kelmagan holatni ham bir xil "e'tiborsiz" qoldirardi.
        setCerts(Array.isArray(res.data?.certificates) ? res.data.certificates : [])
        if (res.data?.socials) {
          setSocials(res.data.socials)
        }
      })
      .catch((err) => {
        console.error('Failed to load certificates:', err)
        setLoadError(true)
      })
      .finally(() => setLoading(false))
  }, [])

  // Categories extraction
  const categories = [
    { id: 'all', label: '🏆 Barchasi' },
    { id: 'oliygoh', label: '🎓 Oliygohga kirishlar' },
    { id: 'sertifikat', label: '📜 Sertifikatlar' },
    { id: 'xalqaro', label: '🌐 Xalqaro Sertifikatlar (IELTS / Multilevel)' },
    { id: 'milliy', label: '🇺🇿 Milliy Sertifikatlar (A/A+ / SAT)' },
  ]

  /** Nav menyusidan yoki chipdan kesim tanlash — manzil ham yangilanadi (havola nusxalansa
   *  o'sha kesim ochilsin). Sahifa qayta yuklanmaydi, shuning uchun `replaceState`. */
  const pickCategory = (id: string) => {
    setActiveCategory(id)
    setNavOpen(false)
    const hash = id === 'oliygoh' ? '#oliygoh' : id === 'sertifikat' ? '#sertifikatlar' : ''
    window.history.replaceState(null, '', `/sertifikatlar${hash}`)
  }

  const filteredCerts = certs.filter((item) => {
    // ⚠️ Maydonlar `|| ''` bilan himoyalangan: CMS'da to'ldirilmagan qiymat javobda `null`
    // bo'lib kelsa `.toLowerCase()` butun sahifani yiqitardi (oq ekran).
    const q = search.toLowerCase()
    const matchesSearch =
      (item.studentName || '').toLowerCase().includes(q) ||
      (item.title || '').toLowerCase().includes(q) ||
      (item.certType || '').toLowerCase().includes(q) ||
      String(item.overallScore ?? '').toLowerCase().includes(q)

    if (!matchesSearch) return false

    if (activeCategory === 'all') return true
    const typeLower = (item.certType || '').toLowerCase()
    const titleLower = (item.title || '').toLowerCase()
    // ⚠️ `category` ham hisobga olinadi (statik `sertifikatlar.js` bilan BIR XIL qoida): CMS'da
    // toifa "Milliy" deb belgilangan, lekin turi/sarlavhasi "SAT/Milliy" so'zini o'z ichiga
    // olmagan sertifikat aks holda hech bir filtrga tushmay, ro'yxatdan yo'qolardi.
    const catLower = (item.category || '').toLowerCase()

    const oliygoh = isOliygoh(item)
    if (activeCategory === 'oliygoh') return oliygoh
    // "Sertifikatlar" = oliygohga kirishlardan BOSHQA hammasi (navdagi ikkinchi band).
    if (activeCategory === 'sertifikat') return !oliygoh
    // ⚠️ Oliygoh natijasi xalqaro/milliy kesimlariga TUSHMAYDI: turi "IELTS" bo'lib qolgan
    // bo'lsa ham u sertifikat emas, kirish natijasi.
    if (oliygoh) return false

    if (activeCategory === 'xalqaro') {
      return typeLower.includes('ielts') || typeLower.includes('multi') || typeLower.includes('cefr') || titleLower.includes('ielts') || titleLower.includes('multi') || catLower.includes('xalqaro')
    }
    if (activeCategory === 'milliy') {
      return typeLower.includes('sat') || typeLower.includes('milli') || titleLower.includes('sat') || titleLower.includes('milli') || catLower.includes('milliy')
    }

    return true
  })

  return (
    <div className="min-h-screen bg-[#090d16] text-gray-100 flex flex-col font-sans selection:bg-blue-500 selection:text-white">
      {/* HEADER */}
      <header className="sticky top-0 z-50 bg-[#090d16]/90 backdrop-blur-md border-b border-white/10">
        <div className="max-w-7xl mx-auto px-4 sm:px-6 lg:px-8 h-20 flex items-center justify-between">
          <Link to="/" className="flex items-center gap-3 group">
            <span className="font-extrabold text-xl tracking-tight text-white group-hover:text-blue-400 transition-colors">
              Wunderkind
            </span>
          </Link>

          <nav className="hidden md:flex items-center gap-8">
            <a href="/#courses" className="text-sm font-semibold text-gray-300 hover:text-white transition-colors">
              Kurslar
            </a>
            <a href="/#teachers" className="text-sm font-semibold text-gray-300 hover:text-white transition-colors">
              Ustozlar
            </a>
            {/* YUTUQLAR — ikki band: oliygohga kirishlar va sertifikatlar. Ikkalasi ham shu
                sahifaning kesimlari (statik landing/sertifikatlar sahifalaridagi nav bilan bir xil). */}
            <div className="relative group">
              <button
                type="button"
                onClick={() => setNavOpen((v) => !v)}
                aria-expanded={navOpen}
                aria-haspopup="true"
                className="inline-flex items-center gap-1 text-sm font-bold text-blue-400 border-b-2 border-blue-500 pb-1"
              >
                Yutuqlar
                <ChevronDown className="w-3.5 h-3.5" />
              </button>
              <div className={`${navOpen ? 'block' : 'hidden'} group-hover:block absolute left-1/2 -translate-x-1/2 top-full pt-3 z-50`}>
                <div className="min-w-[232px] rounded-2xl border border-white/10 bg-[#0d1524] p-2 shadow-2xl">
                  {[
                    { id: 'oliygoh', label: '🎓 Oliygohga kirishlar' },
                    { id: 'sertifikat', label: '📜 Sertifikatlar' },
                  ].map((it) => (
                    <button
                      key={it.id}
                      type="button"
                      onClick={() => pickCategory(it.id)}
                      className="block w-full rounded-xl px-3 py-2.5 text-left text-sm font-semibold text-gray-300 hover:bg-white/5 hover:text-white"
                    >
                      {it.label}
                    </button>
                  ))}
                </div>
              </div>
            </div>
            <a href="/#contact" className="text-sm font-semibold text-gray-300 hover:text-white transition-colors">
              Aloqa
            </a>
          </nav>

          <div className="flex items-center gap-3">
            <Link
              to="/"
              className="inline-flex items-center gap-2 px-4 py-2 rounded-xl bg-white/5 border border-white/10 hover:bg-white/10 text-sm font-semibold text-white transition-all"
            >
              <ArrowLeft className="w-4 h-4 text-blue-400" />
              <span>Bosh Sahifa</span>
            </Link>
          </div>
        </div>
      </header>

      <main className="flex-1">
        {/* HERO SECTION */}
        <section className="relative py-16 overflow-hidden border-b border-white/5 bg-gradient-to-b from-blue-950/20 to-transparent">
          <div className="absolute top-1/2 left-1/2 -translate-x-1/2 -translate-y-1/2 w-[600px] h-[300px] bg-blue-600/10 blur-[120px] rounded-full pointer-events-none" />
          
          <div className="max-w-7xl mx-auto px-4 sm:px-6 lg:px-8 text-center relative z-10">
            <div className="inline-flex items-center gap-2 px-4 py-1.5 rounded-full bg-blue-500/10 border border-blue-500/20 text-blue-400 text-xs font-bold uppercase tracking-wider mb-4">
              <Sparkles className="w-3.5 h-3.5" />
              <span>Faxrimiz va Natijalarimiz</span>
            </div>
            
            <h1 className="text-3xl sm:text-5xl font-black text-white tracking-tight mb-4 leading-tight">
              O'quvchilarimizning Yutuqlari Katalogi
            </h1>
            <p className="max-w-2xl mx-auto text-gray-400 text-base sm:text-lg">
              Wunderkind o'quv markazi o'quvchilarining rasmiy natijalari: IELTS, Multilevel (CEFR),
              SAT va Milliy Sertifikat — hamda oliygohga kirish (grant) yutuqlari.
            </p>

            {/* SEARCH & FILTERS */}
            <div className="mt-10 max-w-3xl mx-auto space-y-4">
              {/* Search Bar */}
              <div className="relative">
                <Search className="w-5 h-5 absolute left-4 top-1/2 -translate-y-1/2 text-gray-400" />
                <input
                  type="text"
                  value={search}
                  onChange={(e) => setSearch(e.target.value)}
                  placeholder="O'quvchi ismi yoki imtihon turi bo'yicha qidirish..."
                  className="w-full pl-12 pr-4 py-3.5 rounded-2xl bg-gray-900/80 border border-white/15 text-white placeholder-gray-500 focus:outline-none focus:ring-2 focus:ring-blue-500 text-sm sm:text-base backdrop-blur"
                />
                {search && (
                  <button
                    onClick={() => setSearch('')}
                    className="absolute right-4 top-1/2 -translate-y-1/2 text-gray-400 hover:text-white"
                  >
                    <X className="w-5 h-5" />
                  </button>
                )}
              </div>

              {/* Category Pills */}
              <div className="flex items-center justify-center gap-2 flex-wrap pt-2">
                {categories.map((cat) => (
                  <button
                    key={cat.id}
                    onClick={() => pickCategory(cat.id)}
                    className={`px-4 py-2 rounded-xl text-xs sm:text-sm font-semibold transition-all ${
                      activeCategory === cat.id
                        ? 'bg-blue-600 text-white shadow-lg shadow-blue-600/30 border border-blue-500'
                        : 'bg-gray-900/60 text-gray-400 border border-white/10 hover:border-white/20 hover:text-white'
                    }`}
                  >
                    {cat.label}
                  </button>
                ))}
              </div>
            </div>
          </div>
        </section>

        {/* CERTIFICATES GRID */}
        <section className="py-12">
          <div className="max-w-7xl mx-auto px-4 sm:px-6 lg:px-8">
            {loading ? (
              <div className="py-20 text-center text-gray-400">
                <div className="inline-block w-8 h-8 border-4 border-blue-500 border-t-transparent rounded-full animate-spin mb-4" />
                <p>Sertifikatlar yuklanmoqda...</p>
              </div>
            ) : loadError ? (
              /* XATO — bo'sh holatdan alohida: tarmoq muammosini "sertifikat yo'q" deb
                 ko'rsatish markaz haqida noto'g'ri taassurot qoldirardi. */
              <div className="py-20 text-center text-gray-400 bg-gray-900/40 rounded-3xl border border-white/5 max-w-xl mx-auto">
                <Award className="w-12 h-12 mx-auto text-gray-600 mb-3" />
                <h3 className="text-lg font-bold text-white mb-1">Sertifikatlarni yuklab bo'lmadi</h3>
                <p className="text-sm text-gray-500">Internet aloqasini tekshirib, sahifani yangilang.</p>
              </div>
            ) : certs.length === 0 ? (
              /* CMS'da umuman yozuv yo'q — bu qidiruv natijasi EMAS, shuning uchun matn ham
                 boshqacha ("qidiruvni o'zgartiring" deyish bu yerda ma'nosiz bo'lardi). */
              <div className="py-20 text-center text-gray-400 bg-gray-900/40 rounded-3xl border border-white/5 max-w-xl mx-auto">
                <Award className="w-12 h-12 mx-auto text-gray-600 mb-3" />
                <h3 className="text-lg font-bold text-white mb-1">Hozircha sertifikatlar joylanmagan</h3>
                <p className="text-sm text-gray-500">Tez orada o'quvchilarimizning natijalari shu yerda e'lon qilinadi.</p>
              </div>
            ) : filteredCerts.length === 0 ? (
              <div className="py-20 text-center text-gray-400 bg-gray-900/40 rounded-3xl border border-white/5 max-w-xl mx-auto">
                <Award className="w-12 h-12 mx-auto text-gray-600 mb-3" />
                <h3 className="text-lg font-bold text-white mb-1">Mos sertifikatlar topilmadi</h3>
                <p className="text-sm text-gray-500">Qidiruv so'rovini yoki toifani o'zgartirib ko'ring.</p>
              </div>
            ) : (
              <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-3 gap-6">
                {filteredCerts.map((cert) => (
                  <div
                    key={cert.id}
                    className="group bg-gray-900/60 border border-white/10 rounded-2xl overflow-hidden hover:border-blue-500/50 transition-all duration-300 flex flex-col justify-between hover:-translate-y-1 shadow-xl hover:shadow-blue-500/10"
                  >
                    {/* Image Preview Container */}
                    <div
                      className="relative h-64 bg-gray-950 overflow-hidden cursor-pointer"
                      onClick={() => {
                        setSelectedImg(cert.imageUrl)
                        setSelectedCert(cert)
                      }}
                    >
                      {/* ⚠️ `/placeholder.png` fayli loyihada UMUMAN yo'q edi — surati yo'q
                          sertifikatda 404 va "singan rasm" belgisi chiqardi. Boshqa o'quvchining
                          sertifikat surati esa zaxira sifatida ishlatilmaydi, shuning uchun
                          neytral belgi ko'rsatiladi. */}
                      {cert.imageUrl ? (
                        <img
                          src={cert.imageUrl}
                          alt={cert.studentName}
                          className="w-full h-full object-cover group-hover:scale-105 transition-transform duration-500"
                        />
                      ) : (
                        <div className="w-full h-full flex items-center justify-center bg-gray-900">
                          <Award className="w-12 h-12 text-gray-700" />
                        </div>
                      )}
                      <div className="absolute inset-0 bg-gradient-to-t from-gray-950 via-transparent to-transparent opacity-80" />

                      {/* Badge Top Right */}
                      {/* Tur ham, ball ham bo'lmasa nishon UMUMAN chizilmaydi — aks holda
                          kartochkada yolg'iz "—" belgisi osilib qolardi. */}
                      {(cert.certType || cert.overallScore) && (
                        <span className="absolute top-3 right-3 px-3 py-1 bg-blue-600/90 backdrop-blur text-white text-xs font-black rounded-lg shadow-lg border border-blue-400/30">
                          {[cert.certType, cert.overallScore].filter(Boolean).join(' — ')}
                        </span>
                      )}

                      {/* View Hover Overlay */}
                      <div className="absolute inset-0 bg-blue-600/20 backdrop-blur-[2px] opacity-0 group-hover:opacity-100 transition-opacity flex items-center justify-center">
                        <span className="px-4 py-2 rounded-xl bg-black/70 text-white text-xs font-bold flex items-center gap-2">
                          <Eye className="w-4 h-4 text-blue-400" /> Kattalashtirish
                        </span>
                      </div>
                    </div>

                    {/* Content Bottom */}
                    <div className="p-5 flex-1 flex flex-col justify-between">
                      <div>
                        <h3 className="text-lg font-bold text-white group-hover:text-blue-400 transition-colors">
                          {cert.studentName}
                        </h3>
                        <p className="text-xs text-gray-400 mt-1 font-medium">{cert.title}</p>

                        {/* Sub-scores grid if available */}
                        {(cert.listening || cert.reading || cert.writing || cert.speaking) && (
                          <div className="grid grid-cols-4 gap-1.5 mt-4 pt-3 border-t border-white/5 text-center">
                            {cert.listening && (
                              <div className="bg-white/5 rounded-lg p-1.5">
                                <span className="block text-[10px] text-gray-400 font-bold uppercase">L</span>
                                <span className="text-xs font-extrabold text-blue-400">{cert.listening}</span>
                              </div>
                            )}
                            {cert.reading && (
                              <div className="bg-white/5 rounded-lg p-1.5">
                                <span className="block text-[10px] text-gray-400 font-bold uppercase">R</span>
                                <span className="text-xs font-extrabold text-blue-400">{cert.reading}</span>
                              </div>
                            )}
                            {cert.writing && (
                              <div className="bg-white/5 rounded-lg p-1.5">
                                <span className="block text-[10px] text-gray-400 font-bold uppercase">W</span>
                                <span className="text-xs font-extrabold text-blue-400">{cert.writing}</span>
                              </div>
                            )}
                            {cert.speaking && (
                              <div className="bg-white/5 rounded-lg p-1.5">
                                <span className="block text-[10px] text-gray-400 font-bold uppercase">S</span>
                                <span className="text-xs font-extrabold text-blue-400">{cert.speaking}</span>
                              </div>
                            )}
                          </div>
                        )}
                      </div>

                      <div className="mt-4 pt-3 border-t border-white/5 flex items-center justify-between text-xs text-gray-500">
                        <span className="flex items-center gap-1 text-emerald-400 font-semibold">
                          <CheckCircle2 className="w-3.5 h-3.5" /> Tasdiqlangan Natija
                        </span>
                        <button
                          onClick={() => {
                            setSelectedImg(cert.imageUrl)
                            setSelectedCert(cert)
                          }}
                          className="text-blue-400 hover:text-blue-300 font-bold underline"
                        >
                          Ko'rish
                        </button>
                      </div>
                    </div>
                  </div>
                ))}
              </div>
            )}
          </div>
        </section>
      </main>

      {/* LIGHTBOX MODAL
          ⚠️ Ochilish sharti `selectedCert` (rasm EMAS): surati yo'q sertifikatda "Ko'rish"
          tugmasi ilgari jimgina hech narsa qilmasdi. */}
      {selectedCert && (
        <div
          className="fixed inset-0 z-50 bg-black/90 backdrop-blur-md flex items-center justify-center p-4"
          onClick={() => { setSelectedImg(null); setSelectedCert(null) }}
        >
          <div
            className="relative max-w-4xl w-full bg-gray-900 rounded-2xl overflow-hidden border border-white/10 shadow-2xl"
            onClick={(e) => e.stopPropagation()}
          >
            <div className="p-4 bg-gray-950 flex items-center justify-between border-b border-white/10">
              <div>
                <h4 className="font-bold text-white text-base">
                  {[selectedCert?.studentName, [selectedCert?.certType, selectedCert?.overallScore].filter(Boolean).join(' ')]
                    .filter(Boolean)
                    .join(' — ')}
                </h4>
                <p className="text-xs text-gray-400">{selectedCert?.title}</p>
              </div>
              <button
                onClick={() => { setSelectedImg(null); setSelectedCert(null) }}
                className="p-2 text-gray-400 hover:text-white rounded-lg bg-white/5 hover:bg-white/10"
              >
                <X className="w-5 h-5" />
              </button>
            </div>
            <div className="p-2 max-h-[80vh] overflow-auto flex items-center justify-center bg-black">
              {selectedImg ? (
                <img src={selectedImg} alt="Certificate" className="max-w-full max-h-[75vh] object-contain rounded-lg" />
              ) : (
                /* Surat biriktirilmagan — boshqa o'quvchining sertifikati ko'rsatilmaydi. */
                <div className="py-16 text-center text-gray-500 text-sm">Sertifikat surati biriktirilmagan.</div>
              )}
            </div>
          </div>
        </div>
      )}

      {/* FOOTER */}
      <footer className="bg-[#060911] border-t border-white/10 py-12">
        <div className="max-w-7xl mx-auto px-4 sm:px-6 lg:px-8 flex flex-col md:flex-row items-center justify-between gap-6">
          <div className="flex items-center gap-3">
            <div className="w-9 h-9 rounded-xl bg-blue-600 flex items-center justify-center text-lg text-white">
              🎓
            </div>
            <span className="font-bold text-white text-lg">Wunderkind</span>
          </div>

          <div className="flex items-center gap-4">
            <a
              href={socials.telegramUrl}
              target="_blank"
              rel="noreferrer"
              className="w-10 h-10 rounded-full bg-white/5 border border-white/10 flex items-center justify-center hover:bg-blue-600 transition-colors"
            >
              <Send className="w-4 h-4 text-white" />
            </a>
            <a
              href={socials.instagramUrl}
              target="_blank"
              rel="noreferrer"
              className="w-10 h-10 rounded-full bg-white/5 border border-white/10 flex items-center justify-center hover:bg-pink-600 transition-colors"
            >
              <Share2 className="w-4 h-4 text-white" />
            </a>
          </div>

          <p className="text-xs text-gray-500">
            © 2013-2026 Wunderkind. Barcha huquqlar himoyalangan. | <a href="/privacy" className="underline">Ommaviy offerta</a>
          </p>
        </div>
      </footer>
    </div>
  )
}
