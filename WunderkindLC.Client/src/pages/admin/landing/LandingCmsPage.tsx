import React, { useState, useEffect } from 'react'
import { landingCmsService } from '@/api/services/landingCms'
import { useAuth } from '@/context/auth-context'
import { can } from '@/lib/permissions'
import type {
  LandingTeacher,
  LandingCertificate,
  LandingTestimonial,
  LandingFaq,
  LandingSocials,
} from '@/api/services/landingCms'
import {
  UserCheck,
  Award,
  MessageSquare,
  HelpCircle,
  Plus,
  Edit2,
  Trash2,
  Star,
  Sparkles,
  Upload,
  MapPin,
  Share2,
  Phone,
  Clock,
  Send,
  Camera,
  Video,
  Globe,
  Mail,
  Smartphone,
  Download,
  Map,
  ExternalLink,
  AlertTriangle,
} from 'lucide-react'

/**
 * CSP `frame-src` OQ RO'YXATI — manba: `WunderkindLC.Server/Program.cs` (`frame-src` qatori,
 * ~833-835). Prodda faqat shu domenlardagi `<iframe>` ochiladi ('self' dan tashqari), boshqasi
 * (masalan 2GIS) brauzer tomonidan JIMGINA bloklanadi: sayt ochiladi, xarita joyi bo'sh qoladi va
 * admin sababini bilmaydi. Shuning uchun ogohlantirish AYNAN kiritish paytida ko'rsatiladi.
 *
 * ⚠️ Ro'yxat Program.cs dan KO'CHIRILGAN — u yerda kengaytirilsa, bu yerga ham qo'shilsin.
 * Bu faqat OGOHLANTIRISH manbai: saqlash TAQIQLANMAYDI (CSP kelajakda kengayishi mumkin).
 */
const MAP_ALLOWED_HOSTS = ['yandex.uz', 'yandex.ru', 'google.com', 'openstreetmap.org']

/**
 * Kiritilgan matndan xarita MANZILINI ajratadi — AYNAN `wwwroot/landing.js` dagi `renderMap`
 * kabi: avval `<iframe ... src="...">`, aks holda `http(s)://` bilan boshlangan yalang URL.
 * Hech biri mos kelmasa `''` qaytadi. Sabab: admin ekranda saytdagi bilan BIR XIL natijani
 * ko'rsin — "kiritdim, lekin saytda chiqmadi" holati saqlashdan OLDIN ma'lum bo'lsin.
 */
const extractMapUrl = (raw: string): string => {
  const v = (raw || '').trim()
  if (!v) return ''
  const iframeMatch = v.match(/<iframe[^>]*src=["']([^"']+)["']/i)
  if (iframeMatch && iframeMatch[1]) return iframeMatch[1].replace(/&amp;/gi, '&')
  if (/^https?:\/\//i.test(v)) return v.split(/\s+/)[0].replace(/&amp;/gi, '&')
  return ''
}

/** Manzil CSP oq ro'yxatidagi domenga (yoki uning subdomeniga) tegishlimi. */
const isMapHostAllowed = (url: string): boolean => {
  try {
    const host = new URL(url).hostname.toLowerCase()
    return MAP_ALLOWED_HOSTS.some((h) => host === h || host.endsWith('.' + h))
  } catch {
    return false
  }
}

/**
 * Manzil ALLAQACHON "embed/widget" shaklidami.
 *
 * ⚠️ Oldindan ko'rish FAQAT shundagina chiziladi. Sabab: oddiy xarita havolasini sayt o'zi
 * `normalizeMapUrl` bilan widget shakliga keltiradi, bu yerda esa xom havola iframe'da
 * ochilmaydi (provayder o'zi taqiqlaydi) — admin ISHLAYDIGAN havolani "buzuq" deb o'ylab,
 * tuzatishga urinardi. `normalizeMapUrl` mantig'i bu yerda ATAYIN takrorlanmadi: ikki joyda
 * turgan bir xil qoida vaqt o'tib ajralib ketardi (drift).
 */
const isMapEmbedUrl = (url: string): boolean => {
  const u = url.toLowerCase()
  return (
    u.includes('/map-widget/') ||
    u.includes('output=embed') ||
    u.includes('/maps/embed') ||
    u.includes('export/embed')
  )
}

export const LandingCmsPage: React.FC = () => {
  const [activeTab, setActiveTab] = useState<'teachers' | 'certificates' | 'testimonials' | 'faqs' | 'socials'>('teachers')
  const [loading, setLoading] = useState(false)
  const [uploading, setUploading] = useState(false)

  // ⚠️ BO'SH BOSHLANADI — qattiq kodlangan "namuna" qiymatlar bilan EMAS.
  // Sabab: saqlash TO'LIQ obyektni yuboradi. Ilgari bu yerda haqiqiy havolalar/telefon turardi va
  // `getSocials()` xato bersa (tarmoq uzilishi, 403) admin ekranda O'SHA soxta qiymatlarni ko'rib
  // "Saqlash" bossa, ular bazadagi haqiqiy ma'lumot ustidan yozilardi. Sukut qiymatlar SERVERda,
  // faqat ommaviy o'qishda qo'yiladi (`GetPublicLandingData`).
  const emptySocials: LandingSocials = {
    telegramUrl: '',
    instagramUrl: '',
    youtubeUrl: '',
    facebookUrl: '',
    centerEmail: '',
    appStoreUrl: '',
    playMarketUrl: '',
    contactPhone: '',
    centerAddress: '',
    workingHours: '',
  }
  const [socials, setSocials] = useState<LandingSocials>(emptySocials)
  /** Server javobi KELDIMI — kelmaguncha saqlash yopiq (bo'sh forma bazani tozalab yubormasin). */
  const [socialsLoaded, setSocialsLoaded] = useState(false)
  const [socialsSaving, setSocialsSaving] = useState(false)

  // XARITA — aloqa ma'lumotining bir qismi, lekin ENDPOINTI AYRI (`/landing/map-url`).
  // Boshlang'ich qiymat BO'SH: `socials` dagi bilan bir xil tuzoq — soxta boshlang'ich qiymat
  // bilan ochilgan forma "Saqlash" bosilganda bazadagi haqiqiy manzil ustidan yozib yuborardi.
  const [mapUrl, setMapUrl] = useState('')
  /** Server javobi KELDIMI — kelmaguncha saqlash YOPIQ (bo'sh forma xaritani o'chirib yubormasin). */
  const [mapUrlLoaded, setMapUrlLoaded] = useState(false)
  const [mapUrlSaving, setMapUrlSaving] = useState(false)

  // Ruxsat darvozasi — server `[AdminPerm("settings")]` bilan AYNAN bir xil kalit.
  // Sahifaning O'ZI `RequirePerm perm="settings"` bilan ochiladi, lekin u faqat KO'RISHni
  // tekshiradi: `settings:view` bo'lgan xodim formani to'ldirib, oxirida sababsiz 403 olardi.
  const { user } = useAuth()
  const canCreate = can(user?.permissions, 'settings', 'create')
  const canEdit = can(user?.permissions, 'settings', 'edit')
  const canDelete = can(user?.permissions, 'settings', 'delete')

  // Data states
  const [teachers, setTeachers] = useState<LandingTeacher[]>([])
  const [certificates, setCertificates] = useState<LandingCertificate[]>([])
  const [testimonials, setTestimonials] = useState<LandingTestimonial[]>([])
  const [faqs, setFaqs] = useState<LandingFaq[]>([])

  // Modal states
  const [modalType, setModalType] = useState<'teacher' | 'certificate' | 'testimonial' | 'faq' | null>(null)
  const [editingItem, setEditingItem] = useState<any>(null)

  // Form fields
  const [formData, setFormData] = useState<any>({})

  const handleFileUpload = async (file: File, fieldName: string) => {
    if (!file) return
    setUploading(true)
    try {
      const res = await landingCmsService.uploadImage(file)
      if (res.data?.url) {
        setFormData((prev: any) => ({ ...prev, [fieldName]: res.data.url }))
      }
    } catch (err) {
      alert("Rasm yuklashda xatolik yuz berdi! Qayta urinib ko'ring.")
    } finally {
      setUploading(false)
    }
  }

  useEffect(() => {
    loadData()
  }, [activeTab])

  const loadData = async () => {
    setLoading(true)
    try {
      if (activeTab === 'teachers') {
        const res = await landingCmsService.getTeachers()
        setTeachers(res.data || [])
      } else if (activeTab === 'certificates') {
        const res = await landingCmsService.getCertificates()
        setCertificates(res.data || [])
      } else if (activeTab === 'testimonials') {
        const res = await landingCmsService.getTestimonials()
        setTestimonials(res.data || [])
      } else if (activeTab === 'faqs') {
        const res = await landingCmsService.getFaqs()
        setFaqs(res.data || [])
      } else if (activeTab === 'socials') {
        // Ikkala ma'lumot ham shu tabda ko'rinadi, lekin ENDPOINTLARI ayri. `allSettled` ATAYIN:
        // biri xato bersa (tarmoq, 403) ikkinchisi baribir yuklansin — aks holda bitta xato
        // ikkala formani ham "yuklanmadi" holatida qoldirib, saqlashni yopib qo'yardi.
        const [socialsRes, mapRes] = await Promise.allSettled([
          landingCmsService.getSocials(),
          landingCmsService.getMapUrl(),
        ])
        if (socialsRes.status === 'fulfilled' && socialsRes.value.data) {
          setSocials({ ...emptySocials, ...socialsRes.value.data })
          setSocialsLoaded(true)
        }
        if (mapRes.status === 'fulfilled' && mapRes.value.data) {
          // Bo'sh satr ham TO'LIQ HAQIQIY javob (xarita hali kiritilmagan) — shuning uchun
          // qiymatning o'ziga emas, javob KELGANIGA qaraymiz.
          setMapUrl(mapRes.value.data.mapUrl || '')
          setMapUrlLoaded(true)
        }
      }
    } catch (err) {
      console.error('Landing CMS data load error:', err)
    } finally {
      setLoading(false)
    }
  }

  // --- Modal Open Helpers ---
  const openTeacherModal = (item?: LandingTeacher) => {
    setEditingItem(item || null)
    setFormData(
      item || {
        fullName: '',
        subject: '',
        photoUrl: '',
        badge: '',
        shortBio: '',
        fullBio: '',
        order: teachers.length + 1,
        isActive: true,
      }
    )
    setModalType('teacher')
  }

  const openCertificateModal = (item?: LandingCertificate) => {
    setEditingItem(item || null)
    setFormData(
      item || {
        title: '',
        studentName: '',
        imageUrl: '',
        category: 'Xalqaro',
        certType: 'IELTS',
        overallScore: '',
        listening: '',
        reading: '',
        writing: '',
        speaking: '',
        resultNote: '',
        order: certificates.length + 1,
        isActive: true,
      }
    )
    setModalType('certificate')
  }

  const openTestimonialModal = (item?: LandingTestimonial) => {
    setEditingItem(item || null)
    setFormData(
      item || {
        authorName: '',
        authorRole: "Ota-ona",
        avatarUrl: '',
        rating: 5,
        comment: '',
        order: testimonials.length + 1,
        isActive: true,
      }
    )
    setModalType('testimonial')
  }

  const openFaqModal = (item?: LandingFaq) => {
    setEditingItem(item || null)
    setFormData(
      item || {
        question: '',
        answer: '',
        order: faqs.length + 1,
        isActive: true,
      }
    )
    setModalType('faq')
  }

  const closeModal = () => {
    setModalType(null)
    setEditingItem(null)
    setFormData({})
  }

  // --- Submit & Delete Handlers ---
  const handleSave = async (e: React.FormEvent) => {
    e.preventDefault()
    try {
      if (modalType === 'teacher') {
        if (editingItem) {
          await landingCmsService.updateTeacher(editingItem.id, formData)
        } else {
          await landingCmsService.createTeacher(formData)
        }
      } else if (modalType === 'certificate') {
        if (editingItem) {
          await landingCmsService.updateCertificate(editingItem.id, formData)
        } else {
          await landingCmsService.createCertificate(formData)
        }
      } else if (modalType === 'testimonial') {
        if (editingItem) {
          await landingCmsService.updateTestimonial(editingItem.id, formData)
        } else {
          await landingCmsService.createTestimonial(formData)
        }
      } else if (modalType === 'faq') {
        if (editingItem) {
          await landingCmsService.updateFaq(editingItem.id, formData)
        } else {
          await landingCmsService.createFaq(formData)
        }
      }
      closeModal()
      loadData()
    } catch (err) {
      alert("Xatolik yuz berdi! Qayta urinib ko'ring.")
    }
  }

  const handleDelete = async (id: string) => {
    if (!confirm("Haqiqatan ham o'chirmoqchimisiz?")) return
    try {
      if (activeTab === 'teachers') await landingCmsService.deleteTeacher(id)
      if (activeTab === 'certificates') await landingCmsService.deleteCertificate(id)
      if (activeTab === 'testimonials') await landingCmsService.deleteTestimonial(id)
      if (activeTab === 'faqs') await landingCmsService.deleteFaq(id)
      loadData()
    } catch (err) {
      alert("O'chirishda xatolik yuz berdi!")
    }
  }

  // XARITA maydonining joriy qiymati tahlili — ogohlantirish SAQLASHDAN OLDIN ko'rinsin
  // (prodda CSP bloklagan xarita hech qanday xato bermaydi, shunchaki bo'sh joy qoladi).
  const mapTrimmed = mapUrl.trim()
  const mapExtractedUrl = extractMapUrl(mapUrl)
  const mapHostAllowed = mapExtractedUrl ? isMapHostAllowed(mapExtractedUrl) : false
  const mapCanPreview = mapHostAllowed && isMapEmbedUrl(mapExtractedUrl)

  return (
    <div className="p-6 max-w-7xl mx-auto space-y-6">
      {/* Header Banner - Matching Clean CRM Theme */}
      <div className="rounded-2xl bg-white dark:bg-gray-900 border border-gray-200 dark:border-gray-800 p-6 shadow-sm">
        <div className="flex flex-col md:flex-row justify-between items-start md:items-center gap-4">
          <div>
            <div className="inline-flex items-center gap-2 px-3 py-1 rounded-full bg-indigo-50 dark:bg-indigo-950/50 border border-indigo-200 dark:border-indigo-800/60 text-indigo-700 dark:text-indigo-300 text-xs font-semibold mb-3">
              <Sparkles className="w-3.5 h-3.5 text-amber-500" />
              <span>Landing CMS Hub</span>
            </div>
            <h1 className="text-2xl md:text-3xl font-extrabold tracking-tight text-gray-900 dark:text-white">Landing Sahifasi Boshqaruvi</h1>
            <p className="text-sm text-gray-500 dark:text-gray-400 max-w-2xl mt-1">
              Asosiy sayt (Landing page) dagi O'qituvchilar, Sertifikatlar, Ota-onalar fikrlari va FAQ bo'limlarini dinamik boshqarish.
            </p>
          </div>

          <div className="grid grid-cols-2 sm:grid-cols-4 gap-3 w-full md:w-auto">
            <div className="bg-gray-50 dark:bg-gray-800/60 border border-gray-200/80 dark:border-gray-700/60 rounded-xl px-4 py-2.5 text-center">
              <span className="block text-xl font-bold text-gray-900 dark:text-white">{teachers.length}</span>
              <span className="text-[11px] text-gray-500 dark:text-gray-400 uppercase font-semibold">Ustozlar</span>
            </div>
            <div className="bg-gray-50 dark:bg-gray-800/60 border border-gray-200/80 dark:border-gray-700/60 rounded-xl px-4 py-2.5 text-center">
              <span className="block text-xl font-bold text-gray-900 dark:text-white">{certificates.length}</span>
              <span className="text-[11px] text-gray-500 dark:text-gray-400 uppercase font-semibold">Natijalar</span>
            </div>
            <div className="bg-gray-50 dark:bg-gray-800/60 border border-gray-200/80 dark:border-gray-700/60 rounded-xl px-4 py-2.5 text-center">
              <span className="block text-xl font-bold text-gray-900 dark:text-white">{testimonials.length}</span>
              <span className="text-[11px] text-gray-500 dark:text-gray-400 uppercase font-semibold">Fikrlar</span>
            </div>
            <div className="bg-gray-50 dark:bg-gray-800/60 border border-gray-200/80 dark:border-gray-700/60 rounded-xl px-4 py-2.5 text-center">
              <span className="block text-xl font-bold text-gray-900 dark:text-white">{faqs.length}</span>
              <span className="text-[11px] text-gray-500 dark:text-gray-400 uppercase font-semibold">FAQ</span>
            </div>
          </div>
        </div>
      </div>

      {/* Tabs */}
      <div className="flex flex-wrap gap-2 border-b border-gray-200 dark:border-gray-800">
        <button
          onClick={() => setActiveTab('teachers')}
          className={`flex items-center gap-2 px-5 py-3 font-semibold text-sm border-b-2 transition-all ${
            activeTab === 'teachers'
              ? 'border-indigo-600 text-indigo-600 dark:text-indigo-400 bg-indigo-50/50 dark:bg-indigo-950/30 rounded-t-xl'
              : 'border-transparent text-gray-500 hover:text-gray-700 dark:text-gray-400'
          }`}
        >
          <UserCheck className="w-4 h-4 text-indigo-500" />
          <span>O'qituvchilar</span>
          <span className="ml-1 px-2 py-0.5 text-xs rounded-full bg-indigo-100 dark:bg-indigo-900/60 text-indigo-700 dark:text-indigo-300 font-bold">
            {teachers.length}
          </span>
        </button>

        <button
          onClick={() => setActiveTab('certificates')}
          className={`flex items-center gap-2 px-5 py-3 font-semibold text-sm border-b-2 transition-all ${
            activeTab === 'certificates'
              ? 'border-indigo-600 text-indigo-600 dark:text-indigo-400 bg-indigo-50/50 dark:bg-indigo-950/30 rounded-t-xl'
              : 'border-transparent text-gray-500 hover:text-gray-700 dark:text-gray-400'
          }`}
        >
          <Award className="w-4 h-4 text-amber-500" />
          <span>Sertifikatlar & Natijalar</span>
          <span className="ml-1 px-2 py-0.5 text-xs rounded-full bg-amber-100 dark:bg-amber-900/60 text-amber-700 dark:text-amber-300 font-bold">
            {certificates.length}
          </span>
        </button>

        <button
          onClick={() => setActiveTab('testimonials')}
          className={`flex items-center gap-2 px-5 py-3 font-semibold text-sm border-b-2 transition-all ${
            activeTab === 'testimonials'
              ? 'border-indigo-600 text-indigo-600 dark:text-indigo-400 bg-indigo-50/50 dark:bg-indigo-950/30 rounded-t-xl'
              : 'border-transparent text-gray-500 hover:text-gray-700 dark:text-gray-400'
          }`}
        >
          <MessageSquare className="w-4 h-4 text-emerald-500" />
          <span>Ota-onalar & O'quvchilar Fikrlari</span>
          <span className="ml-1 px-2 py-0.5 text-xs rounded-full bg-emerald-100 dark:bg-emerald-900/60 text-emerald-700 dark:text-emerald-300 font-bold">
            {testimonials.length}
          </span>
        </button>

        <button
          onClick={() => setActiveTab('faqs')}
          className={`flex items-center gap-2 px-5 py-3 font-semibold text-sm border-b-2 transition-all ${
            activeTab === 'faqs'
              ? 'border-indigo-600 text-indigo-600 dark:text-indigo-400 bg-indigo-50/50 dark:bg-indigo-950/30 rounded-t-xl'
              : 'border-transparent text-gray-500 hover:text-gray-700 dark:text-gray-400'
          }`}
        >
          <HelpCircle className="w-4 h-4 text-purple-500" />
          <span>FAQ (Savollar)</span>
          <span className="ml-1 px-2 py-0.5 text-xs rounded-full bg-purple-100 dark:bg-purple-900/60 text-purple-700 dark:text-purple-300 font-bold">
            {faqs.length}
          </span>
        </button>
        <button
          onClick={() => setActiveTab('socials')}
          className={`flex items-center gap-2 px-5 py-3 font-semibold text-sm border-b-2 transition-all ${
            activeTab === 'socials'
              ? 'border-indigo-600 text-indigo-600 dark:text-indigo-400 bg-indigo-50/50 dark:bg-indigo-950/30 rounded-t-xl'
              : 'border-transparent text-gray-500 hover:text-gray-700 dark:text-gray-400'
          }`}
        >
          <Share2 className="w-4 h-4 text-cyan-500" />
          <span>📱 Ijtimoiy Tarmoqlar & Aloqa</span>
        </button>
      </div>

      {/* Content Area */}
      <div className="bg-white dark:bg-gray-900 rounded-2xl border border-gray-200 dark:border-gray-800 p-6 shadow-sm">
        {/* TAB 1: TEACHERS */}
        {activeTab === 'teachers' && (
          <div>
            <div className="flex justify-between items-center mb-6">
              <h2 className="text-lg font-semibold text-gray-900 dark:text-white">O'qituvchilar Ro'yxati</h2>
              <button
                onClick={() => openTeacherModal()}
                disabled={!canCreate}
                title={canCreate ? '' : "Qo'shish uchun ruxsatingiz yo'q"}
                className="flex items-center gap-2 px-4 py-2 bg-indigo-600 hover:bg-indigo-700 text-white font-medium rounded-lg text-sm transition-colors"
              >
                <Plus className="w-4 h-4" />
                Yangi Ustoz Qo'shish
              </button>
            </div>

            {loading ? (
              <div className="py-8 text-center text-gray-500">Yuklanmoqda...</div>
            ) : teachers.length === 0 ? (
              <div className="py-8 text-center text-gray-500">Hozircha o'qituvchilar kiritilmagan.</div>
            ) : (
              <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-3 gap-6">
                {teachers.map((t) => (
                  <div
                    key={t.id}
                    className="border border-gray-200 dark:border-gray-800 rounded-xl p-5 bg-gray-50 dark:bg-gray-800/50 flex flex-col justify-between"
                  >
                    <div>
                      <div className="flex items-center gap-4 mb-4">
                        <img
                          src={t.photoUrl || '/placeholder.png'}
                          alt={t.fullName}
                          className="w-16 h-16 rounded-full object-cover border-2 border-indigo-500/20"
                        />
                        <div>
                          <h3 className="font-bold text-gray-900 dark:text-white">{t.fullName}</h3>
                          <p className="text-xs text-indigo-600 dark:text-indigo-400 font-medium">{t.subject}</p>
                          {t.badge && (
                            <span className="inline-block mt-1 px-2 py-0.5 text-[10px] font-semibold bg-amber-100 dark:bg-amber-900/40 text-amber-700 dark:text-amber-300 rounded">
                              {t.badge}
                            </span>
                          )}
                        </div>
                      </div>
                      <p className="text-xs text-gray-600 dark:text-gray-400 line-clamp-2 mb-4">{t.shortBio}</p>
                    </div>

                    <div className="flex items-center justify-between pt-3 border-t border-gray-200 dark:border-gray-700/50">
                      <span className={`text-[10px] font-semibold px-2 py-0.5 rounded ${t.isActive ? 'bg-emerald-100 dark:bg-emerald-900/40 text-emerald-700 dark:text-emerald-300' : 'bg-gray-100 dark:bg-gray-800 text-gray-500'}`}>
                        {t.isActive ? 'Faol' : 'Nofaol'}
                      </span>
                      <div className="flex items-center gap-1">
                        <button
                          onClick={() => openTeacherModal(t)}
                          disabled={!canEdit}
                          title={canEdit ? '' : "Tahrirlash uchun ruxsatingiz yo'q"}
                          className="p-1.5 text-gray-600 hover:text-indigo-600 dark:text-gray-400 dark:hover:text-indigo-400"
                        >
                          <Edit2 className="w-4 h-4" />
                        </button>
                        <button
                          onClick={() => handleDelete(t.id)}
                          disabled={!canDelete}
                          title={canDelete ? '' : "O'chirish uchun ruxsatingiz yo'q"}
                          className="p-1.5 text-gray-600 hover:text-red-600 dark:text-gray-400 dark:hover:text-red-400"
                        >
                          <Trash2 className="w-4 h-4" />
                        </button>
                      </div>
                    </div>
                  </div>
                ))}
              </div>
            )}
          </div>
        )}

        {/* TAB 2: CERTIFICATES */}
        {activeTab === 'certificates' && (
          <div>
            <div className="flex justify-between items-center mb-6">
              <h2 className="text-lg font-semibold text-gray-900 dark:text-white">Sertifikatlar Ro'yxati</h2>
              <button
                onClick={() => openCertificateModal()}
                disabled={!canCreate}
                title={canCreate ? '' : "Qo'shish uchun ruxsatingiz yo'q"}
                className="flex items-center gap-2 px-4 py-2 bg-indigo-600 hover:bg-indigo-700 text-white font-medium rounded-lg text-sm transition-colors"
              >
                <Plus className="w-4 h-4" />
                Yangi Sertifikat Qo'shish
              </button>
            </div>

            {loading ? (
              <div className="py-8 text-center text-gray-500">Yuklanmoqda...</div>
            ) : certificates.length === 0 ? (
              <div className="py-8 text-center text-gray-500">Hozircha sertifikatlar kiritilmagan.</div>
            ) : (
              <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-3 gap-6">
                {certificates.map((c) => (
                  <div
                    key={c.id}
                    className="border border-gray-200 dark:border-gray-800 rounded-xl p-5 bg-gray-50 dark:bg-gray-800/50 flex flex-col justify-between"
                  >
                    <div>
                      <div className="relative h-44 rounded-lg overflow-hidden mb-4 bg-gray-200 dark:bg-gray-900">
                        <img src={c.imageUrl} alt={c.title} className="w-full h-full object-cover" />
                        <span className="absolute top-2 left-2 px-2 py-0.5 text-[10px] font-bold bg-black/60 backdrop-blur text-white rounded">
                          {c.certType} - {c.overallScore}
                        </span>
                      </div>
                      <h3 className="font-bold text-gray-900 dark:text-white">{c.studentName}</h3>
                      <p className="text-xs text-gray-500 dark:text-gray-400">{c.title}</p>
                    </div>

                    <div className="flex items-center justify-between pt-3 border-t border-gray-200 dark:border-gray-700/50 mt-4">
                      <span className={`text-[10px] font-semibold px-2 py-0.5 rounded ${c.isActive ? 'bg-emerald-100 dark:bg-emerald-900/40 text-emerald-700 dark:text-emerald-300' : 'bg-gray-100 dark:bg-gray-800 text-gray-500'}`}>
                        {c.isActive ? 'Faol' : 'Nofaol'}
                      </span>
                      <div className="flex items-center gap-1">
                        <button
                          onClick={() => openCertificateModal(c)}
                          disabled={!canEdit}
                          title={canEdit ? '' : "Tahrirlash uchun ruxsatingiz yo'q"}
                          className="p-1.5 text-gray-600 hover:text-indigo-600 dark:text-gray-400 dark:hover:text-indigo-400"
                        >
                          <Edit2 className="w-4 h-4" />
                        </button>
                        <button
                          onClick={() => handleDelete(c.id)}
                          disabled={!canDelete}
                          title={canDelete ? '' : "O'chirish uchun ruxsatingiz yo'q"}
                          className="p-1.5 text-gray-600 hover:text-red-600 dark:text-gray-400 dark:hover:text-red-400"
                        >
                          <Trash2 className="w-4 h-4" />
                        </button>
                      </div>
                    </div>
                  </div>
                ))}
              </div>
            )}
          </div>
        )}

        {/* TAB 3: TESTIMONIALS */}
        {activeTab === 'testimonials' && (
          <div>
            <div className="flex justify-between items-center mb-6">
              <h2 className="text-lg font-semibold text-gray-900 dark:text-white">Fikrlar Ro'yxati</h2>
              <button
                onClick={() => openTestimonialModal()}
                disabled={!canCreate}
                title={canCreate ? '' : "Qo'shish uchun ruxsatingiz yo'q"}
                className="flex items-center gap-2 px-4 py-2 bg-indigo-600 hover:bg-indigo-700 text-white font-medium rounded-lg text-sm transition-colors"
              >
                <Plus className="w-4 h-4" />
                Yangi Fikr Qo'shish
              </button>
            </div>

            {loading ? (
              <div className="py-8 text-center text-gray-500">Yuklanmoqda...</div>
            ) : testimonials.length === 0 ? (
              <div className="py-8 text-center text-gray-500">Hozircha fikrlar kiritilmagan.</div>
            ) : (
              <div className="grid grid-cols-1 md:grid-cols-2 gap-6">
                {testimonials.map((t) => (
                  <div
                    key={t.id}
                    className="border border-gray-200 dark:border-gray-800 rounded-xl p-5 bg-gray-50 dark:bg-gray-800/50 flex flex-col justify-between"
                  >
                    <div>
                      <div className="flex items-center gap-3 mb-3">
                        <img
                          src={t.avatarUrl || '/placeholder.png'}
                          alt={t.authorName}
                          className="w-12 h-12 rounded-full object-cover border border-gray-200 dark:border-gray-700"
                        />
                        <div>
                          <h3 className="font-bold text-gray-900 dark:text-white text-sm">{t.authorName}</h3>
                          <p className="text-xs text-gray-500 dark:text-gray-400">{t.authorRole}</p>
                          <div className="flex items-center text-amber-400 mt-0.5">
                            {[...Array(t.rating || 5)].map((_, i) => (
                              <Star key={i} className="w-3 h-3 fill-current" />
                            ))}
                          </div>
                        </div>
                      </div>
                      <p className="text-xs text-gray-600 dark:text-gray-300 italic">"{t.comment}"</p>
                    </div>

                    <div className="flex items-center justify-between pt-3 border-t border-gray-200 dark:border-gray-700/50 mt-4">
                      <span className={`text-[10px] font-semibold px-2 py-0.5 rounded ${t.isActive ? 'bg-emerald-100 dark:bg-emerald-900/40 text-emerald-700 dark:text-emerald-300' : 'bg-gray-100 dark:bg-gray-800 text-gray-500'}`}>
                        {t.isActive ? 'Faol' : 'Nofaol'}
                      </span>
                      <div className="flex items-center gap-1">
                        <button
                          onClick={() => openTestimonialModal(t)}
                          disabled={!canEdit}
                          title={canEdit ? '' : "Tahrirlash uchun ruxsatingiz yo'q"}
                          className="p-1.5 text-gray-600 hover:text-indigo-600 dark:text-gray-400 dark:hover:text-indigo-400"
                        >
                          <Edit2 className="w-4 h-4" />
                        </button>
                        <button
                          onClick={() => handleDelete(t.id)}
                          disabled={!canDelete}
                          title={canDelete ? '' : "O'chirish uchun ruxsatingiz yo'q"}
                          className="p-1.5 text-gray-600 hover:text-red-600 dark:text-gray-400 dark:hover:text-red-400"
                        >
                          <Trash2 className="w-4 h-4" />
                        </button>
                      </div>
                    </div>
                  </div>
                ))}
              </div>
            )}
          </div>
        )}

        {/* TAB 4: FAQS */}
        {activeTab === 'faqs' && (
          <div>
            <div className="flex justify-between items-center mb-6">
              <h2 className="text-lg font-semibold text-gray-900 dark:text-white">Savol-Javoblar (FAQ)</h2>
              <button
                onClick={() => openFaqModal()}
                disabled={!canCreate}
                title={canCreate ? '' : "Qo'shish uchun ruxsatingiz yo'q"}
                className="flex items-center gap-2 px-4 py-2 bg-indigo-600 hover:bg-indigo-700 text-white font-medium rounded-lg text-sm transition-colors"
              >
                <Plus className="w-4 h-4" />
                Yangi Savol Qo'shish
              </button>
            </div>

            {loading ? (
              <div className="py-8 text-center text-gray-500">Yuklanmoqda...</div>
            ) : faqs.length === 0 ? (
              <div className="py-8 text-center text-gray-500">Hozircha savollar kiritilmagan.</div>
            ) : (
              <div className="space-y-4">
                {faqs.map((f) => (
                  <div
                    key={f.id}
                    className="border border-gray-200 dark:border-gray-800 rounded-xl p-4 bg-gray-50 dark:bg-gray-800/50 flex justify-between items-start"
                  >
                    <div className="space-y-1 pr-4">
                      <h4 className="font-bold text-gray-900 dark:text-white text-base">❓ {f.question}</h4>
                      <p className="text-sm text-gray-600 dark:text-gray-300">💡 {f.answer}</p>
                    </div>
                    <div className="flex items-center gap-2 flex-shrink-0">
                      <button onClick={() => openFaqModal(f)} disabled={!canEdit} title={canEdit ? '' : "Tahrirlash uchun ruxsatingiz yo'q"} className="p-1.5 text-blue-600 hover:bg-blue-50 rounded-md">
                        <Edit2 className="w-4 h-4" />
                      </button>
                      <button onClick={() => handleDelete(f.id)} disabled={!canDelete} title={canDelete ? '' : "O'chirish uchun ruxsatingiz yo'q"} className="p-1.5 text-red-600 hover:bg-red-50 rounded-md">
                        <Trash2 className="w-4 h-4" />
                      </button>
                    </div>
                  </div>
                ))}
              </div>
            )}
          </div>
        )}

        {/* TAB 5: SOCIALS & CONTACT */}
        {activeTab === 'socials' && (
          <div className="space-y-6">
            <div>
              <h2 className="text-lg font-semibold text-gray-900 dark:text-white">📱 Ijtimoiy Tarmoqlar & Aloqa Ma'lumotlari</h2>
              <p className="text-sm text-gray-500 dark:text-gray-400">
                Landing sahifasining Footer va Aloqa bo'limlaridagi ijtimoiy tarmoq havolalari, telefon, manzil hamda ish vaqtini boshqarish.
              </p>
            </div>

            <div className="grid grid-cols-1 md:grid-cols-2 gap-6 max-w-4xl">
              {/* Telegram */}
              <div className="space-y-1.5">
                <label className="flex items-center gap-2 text-sm font-semibold text-gray-700 dark:text-gray-300">
                  <Send className="w-4 h-4 text-sky-500" />
                  <span>Telegram Havolasi:</span>
                </label>
                <input
                  type="url"
                  value={socials.telegramUrl}
                  onChange={(e) => setSocials({ ...socials, telegramUrl: e.target.value })}
                  placeholder="https://t.me/kanal_nomi"
                  className="w-full px-4 py-2.5 rounded-lg border border-gray-300 dark:border-gray-700 bg-white dark:bg-gray-800 text-gray-900 dark:text-white text-sm focus:ring-2 focus:ring-indigo-500"
                />
              </div>

              {/* Instagram */}
              <div className="space-y-1.5">
                <label className="flex items-center gap-2 text-sm font-semibold text-gray-700 dark:text-gray-300">
                  <Camera className="w-4 h-4 text-pink-500" />
                  <span>Instagram Havolasi:</span>
                </label>
                <input
                  type="url"
                  value={socials.instagramUrl}
                  onChange={(e) => setSocials({ ...socials, instagramUrl: e.target.value })}
                  placeholder="https://instagram.com/akkaunt_nomi"
                  className="w-full px-4 py-2.5 rounded-lg border border-gray-300 dark:border-gray-700 bg-white dark:bg-gray-800 text-gray-900 dark:text-white text-sm focus:ring-2 focus:ring-indigo-500"
                />
              </div>

              {/* YouTube */}
              <div className="space-y-1.5">
                <label className="flex items-center gap-2 text-sm font-semibold text-gray-700 dark:text-gray-300">
                  <Video className="w-4 h-4 text-red-500" />
                  <span>YouTube Havolasi:</span>
                </label>
                <input
                  type="url"
                  value={socials.youtubeUrl}
                  onChange={(e) => setSocials({ ...socials, youtubeUrl: e.target.value })}
                  placeholder="https://youtube.com/@kanal_nomi"
                  className="w-full px-4 py-2.5 rounded-lg border border-gray-300 dark:border-gray-700 bg-white dark:bg-gray-800 text-gray-900 dark:text-white text-sm focus:ring-2 focus:ring-indigo-500"
                />
              </div>

              {/* Facebook */}
              <div className="space-y-1.5">
                <label className="flex items-center gap-2 text-sm font-semibold text-gray-700 dark:text-gray-300">
                  <Globe className="w-4 h-4 text-blue-600" />
                  <span>Facebook Havolasi:</span>
                </label>
                <input
                  type="url"
                  value={socials.facebookUrl}
                  onChange={(e) => setSocials({ ...socials, facebookUrl: e.target.value })}
                  placeholder="https://facebook.com/sahifa_nomi"
                  className="w-full px-4 py-2.5 rounded-lg border border-gray-300 dark:border-gray-700 bg-white dark:bg-gray-800 text-gray-900 dark:text-white text-sm focus:ring-2 focus:ring-indigo-500"
                />
              </div>

              {/* Email */}
              <div className="space-y-1.5">
                <label className="flex items-center gap-2 text-sm font-semibold text-gray-700 dark:text-gray-300">
                  <Mail className="w-4 h-4 text-indigo-500" />
                  <span>Email Manzili:</span>
                </label>
                <input
                  type="email"
                  value={socials.centerEmail}
                  onChange={(e) => setSocials({ ...socials, centerEmail: e.target.value })}
                  placeholder="info@wunderkindedu.uz"
                  className="w-full px-4 py-2.5 rounded-lg border border-gray-300 dark:border-gray-700 bg-white dark:bg-gray-800 text-gray-900 dark:text-white text-sm focus:ring-2 focus:ring-indigo-500"
                />
              </div>

              {/* Contact Phone */}
              <div className="space-y-1.5">
                <label className="flex items-center gap-2 text-sm font-semibold text-gray-700 dark:text-gray-300">
                  <Phone className="w-4 h-4 text-emerald-500" />
                  <span>Aloqa Telefon Raqami:</span>
                </label>
                <input
                  type="text"
                  value={socials.contactPhone}
                  onChange={(e) => setSocials({ ...socials, contactPhone: e.target.value })}
                  placeholder="+998 (90) 000-00-00"
                  className="w-full px-4 py-2.5 rounded-lg border border-gray-300 dark:border-gray-700 bg-white dark:bg-gray-800 text-gray-900 dark:text-white text-sm focus:ring-2 focus:ring-indigo-500"
                />
              </div>

              {/* Working Hours */}
              <div className="space-y-1.5 md:col-span-2">
                <label className="flex items-center gap-2 text-sm font-semibold text-gray-700 dark:text-gray-300">
                  <Clock className="w-4 h-4 text-amber-500" />
                  <span>Ish Vaqti:</span>
                </label>
                <input
                  type="text"
                  value={socials.workingHours}
                  onChange={(e) => setSocials({ ...socials, workingHours: e.target.value })}
                  placeholder="Dushanba — Shanba: 09:00 – 17:00"
                  className="w-full px-4 py-2.5 rounded-lg border border-gray-300 dark:border-gray-700 bg-white dark:bg-gray-800 text-gray-900 dark:text-white text-sm focus:ring-2 focus:ring-indigo-500"
                />
              </div>

              {/* Center Address - Full Width */}
              <div className="space-y-1.5 md:col-span-2">
                <label className="flex items-center gap-2 text-sm font-semibold text-gray-700 dark:text-gray-300">
                  <MapPin className="w-4 h-4 text-rose-500" />
                  <span>Markaz Manzili:</span>
                </label>
                <input
                  type="text"
                  value={socials.centerAddress}
                  onChange={(e) => setSocials({ ...socials, centerAddress: e.target.value })}
                  placeholder="Viloyat, shahar, ko'cha, uy"
                  className="w-full px-4 py-2.5 rounded-lg border border-gray-300 dark:border-gray-700 bg-white dark:bg-gray-800 text-gray-900 dark:text-white text-sm focus:ring-2 focus:ring-indigo-500"
                />
              </div>

              {/* APP DOWNLOAD LINKS SECTION */}
              <div className="md:col-span-2 pt-4 border-t border-gray-200 dark:border-gray-800">
                <h3 className="text-sm font-bold text-gray-900 dark:text-white flex items-center gap-2 mb-3">
                  <Smartphone className="w-4 h-4 text-indigo-500" />
                  <span>Mobil Ilova Yuklash Havolalari (App Store & Play Market)</span>
                </h3>
                <div className="grid grid-cols-1 md:grid-cols-2 gap-4">
                  {/* App Store URL */}
                  <div className="space-y-1.5">
                    <label className="flex items-center gap-2 text-xs font-semibold text-gray-700 dark:text-gray-300">
                      <Download className="w-3.5 h-3.5 text-blue-500" />
                      <span>App Store Havolasi (iOS):</span>
                    </label>
                    <input
                      type="url"
                      value={socials.appStoreUrl}
                      onChange={(e) => setSocials({ ...socials, appStoreUrl: e.target.value })}
                      placeholder="https://apps.apple.com/app/id..."
                      className="w-full px-4 py-2.5 rounded-lg border border-gray-300 dark:border-gray-700 bg-white dark:bg-gray-800 text-gray-900 dark:text-white text-sm focus:ring-2 focus:ring-indigo-500"
                    />
                  </div>

                  {/* Play Market URL */}
                  <div className="space-y-1.5">
                    <label className="flex items-center gap-2 text-xs font-semibold text-gray-700 dark:text-gray-300">
                      <Download className="w-3.5 h-3.5 text-emerald-500" />
                      <span>Google Play Market Havolasi (Android):</span>
                    </label>
                    <input
                      type="url"
                      value={socials.playMarketUrl}
                      onChange={(e) => setSocials({ ...socials, playMarketUrl: e.target.value })}
                      placeholder="https://play.google.com/store/apps/details?id=..."
                      className="w-full px-4 py-2.5 rounded-lg border border-gray-300 dark:border-gray-700 bg-white dark:bg-gray-800 text-gray-900 dark:text-white text-sm focus:ring-2 focus:ring-indigo-500"
                    />
                  </div>
                </div>
              </div>
            </div>

            <div className="pt-4 border-t border-gray-200 dark:border-gray-800 flex items-center gap-3">
              <button
                type="button"
                disabled={socialsSaving || !socialsLoaded || !canEdit}
                onClick={async () => {
                  setSocialsSaving(true)
                  try {
                    await landingCmsService.updateSocials(socials)
                    alert("Ijtimoiy tarmoqlar va aloqa ma'lumotlari saqlandi!")
                  } catch (err: any) {
                    const msg = err?.response?.data?.message || err?.message || "Xatolik yuz berdi!"
                    alert("Xatolik: " + msg)
                  } finally {
                    setSocialsSaving(false)
                  }
                }}
                className="px-6 py-2.5 bg-indigo-600 hover:bg-indigo-700 text-white font-semibold rounded-xl text-sm transition-all shadow-md shadow-indigo-600/20 disabled:opacity-50"
              >
                {socialsSaving ? 'Saqlanmoqda...' : "Ma'lumotlarni Saqlash"}
              </button>
              {!socialsLoaded && (
                <span className="text-xs text-amber-600 dark:text-amber-400">
                  Ma'lumot hali yuklanmadi — saqlash yopiq (bo'sh forma bazani tozalab yubormasin).
                </span>
              )}
              {socialsLoaded && !canEdit && (
                <span className="text-xs text-gray-500">Tahrirlash uchun ruxsatingiz yo'q.</span>
              )}
            </div>

            {/* ---------- XARITA ---------- */}
            {/* Joylashuvi: aloqa ma'lumotining bir qismi (manzil/telefon bilan yonma-yon), lekin
                ALOHIDA karta va ALOHIDA "Saqlash" tugmasi — chunki endpointi ham ayri
                (`/landing/map-url`). Bitta tugmaga qo'shib yuborilsa, ikkita so'rovdan biri
                xato bersa "saqlandi" degan yolg'on xabar chiqib ketardi. */}
            <div className="pt-6 border-t border-gray-200 dark:border-gray-800 space-y-4 max-w-4xl">
              <div>
                <h3 className="flex items-center gap-2 text-base font-bold text-gray-900 dark:text-white">
                  <Map className="w-4 h-4 text-rose-500" />
                  <span>Xarita (Aloqa bo'limi)</span>
                </h3>
                <p className="text-sm text-gray-500 dark:text-gray-400 mt-1">
                  Landing sahifasining "Aloqa" bo'limida ko'rsatiladigan xarita. To'liq{' '}
                  <code className="px-1 py-0.5 rounded bg-gray-100 dark:bg-gray-800 text-[11px]">&lt;iframe ... &gt;</code>{' '}
                  kodini ham, yalang havolani ham qo'yish mumkin — sayt manzilni o'zi ajratib oladi va
                  Yandex/Google havolasini widget (embed) shakliga keltiradi.
                </p>
              </div>

              <div className="space-y-1.5">
                <label className="flex items-center gap-2 text-sm font-semibold text-gray-700 dark:text-gray-300">
                  <MapPin className="w-4 h-4 text-rose-500" />
                  <span>Xarita kodi yoki havolasi:</span>
                </label>
                <textarea
                  rows={4}
                  value={mapUrl}
                  onChange={(e) => setMapUrl(e.target.value)}
                  disabled={!mapUrlLoaded}
                  placeholder={'<iframe src="https://yandex.uz/map-widget/v1/org/239829322269" ...></iframe>\nyoki: https://yandex.uz/maps/?ll=70.93,40.54&z=16'}
                  className="w-full px-4 py-2.5 rounded-lg border border-gray-300 dark:border-gray-700 bg-white dark:bg-gray-800 text-gray-900 dark:text-white text-xs font-mono focus:ring-2 focus:ring-indigo-500 disabled:opacity-60"
                />
                <p className="text-xs text-gray-500 dark:text-gray-400">
                  Ishlaydigan provayderlar: <b>Yandex Maps</b> (yandex.uz / yandex.ru),{' '}
                  <b>Google Maps</b> (google.com / maps.google.com), <b>OpenStreetMap</b>{' '}
                  (openstreetmap.org). Boshqa provayder (masalan 2GIS) saytning xavfsizlik
                  siyosati (CSP) tomonidan bloklanadi va xarita o'rni bo'sh qoladi.
                </p>
                <p className="text-xs text-gray-500 dark:text-gray-400">
                  Maydon <b>bo'sh qoldirilsa</b> — saytda standart xarita (OpenStreetMap) ko'rsatiladi.
                  Ya'ni maydonni tozalab saqlash ham haqiqiy amal, u kiritilgan manzilni o'chiradi.
                </p>
              </div>

              {/* Ogohlantirishlar — saqlashni TAQIQLAMAYDI (CSP kelajakda kengayishi mumkin),
                  lekin admin nima bo'lishini oldindan bilib tursin. */}
              {mapTrimmed && !mapExtractedUrl && (
                <div className="flex items-start gap-2 rounded-lg border border-amber-300 dark:border-amber-800/60 bg-amber-50 dark:bg-amber-950/30 px-3 py-2 text-xs text-amber-800 dark:text-amber-300">
                  <AlertTriangle className="w-4 h-4 shrink-0 mt-0.5" />
                  <span>
                    Kiritilgan matndan manzil ajratib olinmadi. <code>&lt;iframe src="..."&gt;</code>{' '}
                    kodini yoki <code>https://</code> bilan boshlanadigan havolani qo'ying.
                  </span>
                </div>
              )}
              {mapExtractedUrl && !mapHostAllowed && (
                <div className="flex items-start gap-2 rounded-lg border border-rose-300 dark:border-rose-800/60 bg-rose-50 dark:bg-rose-950/30 px-3 py-2 text-xs text-rose-800 dark:text-rose-300">
                  <AlertTriangle className="w-4 h-4 shrink-0 mt-0.5" />
                  <span>
                    Bu manzil ruxsat etilgan provayderlar ro'yxatida yo'q — saytda xarita{' '}
                    <b>jimgina bloklanadi</b> (brauzer CSP). Saqlash mumkin, lekin Yandex, Google yoki
                    OpenStreetMap havolasini ishlatish tavsiya etiladi.
                  </span>
                </div>
              )}

              <div className="flex flex-wrap items-center gap-3">
                <button
                  type="button"
                  disabled={mapUrlSaving || !mapUrlLoaded || !canEdit}
                  onClick={async () => {
                    setMapUrlSaving(true)
                    try {
                      // Bo'sh satr ATAYIN yuboriladi: serverda `""` = tozalash, `null` = tegilmadi.
                      const res = await landingCmsService.updateMapUrl(mapTrimmed)
                      // Server qiymatni Trim qilib qaytaradi — ekranda AYNAN saqlangani tursin.
                      setMapUrl(res.data?.mapUrl ?? mapTrimmed)
                      alert(
                        mapTrimmed
                          ? 'Xarita manzili saqlandi!'
                          : "Xarita tozalandi — saytda standart xarita ko'rsatiladi.",
                      )
                    } catch (err) {
                      // `any` ATAYIN emas (eslint `no-explicit-any`): xato shakli shu yerda
                      // tor tur bilan tavsiflanadi — xulq socials'dagi bilan bir xil qoladi.
                      const e = err as { response?: { data?: { message?: string } }; message?: string }
                      const msg = e?.response?.data?.message || e?.message || 'Xatolik yuz berdi!'
                      alert('Xatolik: ' + msg)
                    } finally {
                      setMapUrlSaving(false)
                    }
                  }}
                  className="px-6 py-2.5 bg-indigo-600 hover:bg-indigo-700 text-white font-semibold rounded-xl text-sm transition-all shadow-md shadow-indigo-600/20 disabled:opacity-50"
                >
                  {mapUrlSaving ? 'Saqlanmoqda...' : 'Xaritani Saqlash'}
                </button>
                {!mapUrlLoaded && (
                  <span className="text-xs text-amber-600 dark:text-amber-400">
                    Ma'lumot hali yuklanmadi — saqlash yopiq (bo'sh forma xaritani o'chirib yubormasin).
                  </span>
                )}
                {mapUrlLoaded && !canEdit && (
                  <span className="text-xs text-gray-500">Tahrirlash uchun ruxsatingiz yo'q.</span>
                )}
                <a
                  href="/#contact"
                  target="_blank"
                  rel="noopener noreferrer"
                  className="inline-flex items-center gap-1.5 text-xs font-semibold text-indigo-600 dark:text-indigo-400 hover:underline"
                >
                  <ExternalLink className="w-3.5 h-3.5" />
                  <span>Saytda tekshirish</span>
                </a>
              </div>

              {/* Oldindan ko'rish — FAQAT ruxsat etilgan domen VA allaqachon embed/widget shaklidagi
                  manzil uchun. Xom havola sayt tomonida widget shakliga keltiriladi, bu yerda esa
                  bo'sh oyna chiqib, ishlaydigan havolani "buzuq" deb ko'rsatib qo'yardi. */}
              {mapExtractedUrl && mapCanPreview && (
                <div className="space-y-1.5">
                  <span className="text-xs font-semibold text-gray-700 dark:text-gray-300">Oldindan ko'rish:</span>
                  <iframe
                    key={mapExtractedUrl}
                    src={mapExtractedUrl}
                    title="Xarita oldindan ko'rish"
                    className="w-full h-64 rounded-xl border border-gray-200 dark:border-gray-800"
                    loading="lazy"
                  />
                </div>
              )}
              {mapExtractedUrl && mapHostAllowed && !mapCanPreview && (
                <p className="text-xs text-gray-500 dark:text-gray-400">
                  Bu havola saytda avtomatik widget (embed) shakliga keltiriladi, shuning uchun bu
                  yerda oldindan ko'rsatib bo'lmaydi — natijani "Saytda tekshirish" havolasi orqali
                  ko'ring.
                </p>
              )}
            </div>
          </div>
        )}
      </div>

      {/* MODAL */}
      {modalType && (
        <div className="fixed inset-0 z-50 bg-black/60 backdrop-blur-sm flex items-center justify-center p-4">
          <div className="bg-white dark:bg-gray-900 rounded-2xl max-w-xl w-full p-6 shadow-xl border border-gray-200 dark:border-gray-800 max-h-[90vh] overflow-y-auto">
            <h3 className="text-xl font-bold text-gray-900 dark:text-white mb-4">
              {editingItem ? "Ma'lumotni Tahrirlash" : "Yangi Ma'lumot Qo'shish"}
            </h3>

            <form onSubmit={handleSave} className="space-y-4">
              {/* TEACHER FORM */}
              {modalType === 'teacher' && (
                <>
                  <div>
                    <label className="block text-xs font-semibold text-gray-700 dark:text-gray-300 mb-1">F.I.SH (Ism-Familiya)</label>
                    <input
                      type="text"
                      required
                      value={formData.fullName || ''}
                      onChange={(e) => setFormData({ ...formData, fullName: e.target.value })}
                      className="w-full px-3 py-2 border rounded-lg dark:bg-gray-800 dark:border-gray-700 dark:text-white"
                      placeholder="Muhabbatxon Ubaydullayeva"
                    />
                  </div>

                  <div>
                    <label className="block text-xs font-semibold text-gray-700 dark:text-gray-300 mb-1">Qaysi fandanligi / Lavozimi</label>
                    <input
                      type="text"
                      required
                      value={formData.subject || ''}
                      onChange={(e) => setFormData({ ...formData, subject: e.target.value })}
                      className="w-full px-3 py-2 border rounded-lg dark:bg-gray-800 dark:border-gray-700 dark:text-white"
                      placeholder="Bosh Ingliz tili va IELTS Ustozisi"
                    />
                  </div>

                  <div>
                    <label className="block text-xs font-semibold text-gray-700 dark:text-gray-300 mb-1">O'qituvchi Rasmi (Kompyuterdan yuklash)</label>
                    <div className="flex items-center gap-3">
                      {formData.photoUrl && (
                        <img src={formData.photoUrl} alt="Preview" className="w-12 h-12 rounded-lg object-cover border border-blue-500 flex-shrink-0" />
                      )}
                      <label className="flex items-center gap-2 px-3 py-2 bg-blue-50 hover:bg-blue-100 dark:bg-blue-950/40 dark:hover:bg-blue-900/60 border border-blue-200 dark:border-blue-800 rounded-lg text-xs font-semibold text-blue-600 dark:text-blue-400 cursor-pointer transition-colors flex-shrink-0">
                        <Upload className="w-4 h-4" />
                        <span>{uploading ? "Yuklanmoqda..." : "Fayl Yuklash"}</span>
                        <input
                          type="file"
                          accept="image/*"
                          disabled={uploading}
                          className="hidden"
                          onChange={(e) => {
                            if (e.target.files && e.target.files[0]) {
                              handleFileUpload(e.target.files[0], 'photoUrl')
                            }
                          }}
                        />
                      </label>
                      <input
                        type="text"
                        value={formData.photoUrl || ''}
                        onChange={(e) => setFormData({ ...formData, photoUrl: e.target.value })}
                        className="flex-1 px-3 py-2 border rounded-lg text-xs dark:bg-gray-800 dark:border-gray-700 dark:text-white"
                        placeholder="yoki rasm URL manzili..."
                      />
                    </div>
                  </div>

                  <div>
                    <label className="block text-xs font-semibold text-gray-700 dark:text-gray-300 mb-1">Nishon / Badge</label>
                    <input
                      type="text"
                      value={formData.badge || ''}
                      onChange={(e) => setFormData({ ...formData, badge: e.target.value })}
                      className="w-full px-3 py-2 border rounded-lg dark:bg-gray-800 dark:border-gray-700 dark:text-white"
                      placeholder="IELTS 8.5+"
                    />
                  </div>

                  <div>
                    <label className="block text-xs font-semibold text-gray-700 dark:text-gray-300 mb-1">Qisqa izoh (Karta ustida)</label>
                    <input
                      type="text"
                      value={formData.shortBio || ''}
                      onChange={(e) => setFormData({ ...formData, shortBio: e.target.value })}
                      className="w-full px-3 py-2 border rounded-lg dark:bg-gray-800 dark:border-gray-700 dark:text-white"
                      placeholder="CELTA sertifikasi sohibasi..."
                    />
                  </div>

                  <div>
                    <label className="block text-xs font-semibold text-gray-700 dark:text-gray-300 mb-1">To'liq tarjimai hol va yutuqlari (Modal oyna uchun)</label>
                    <textarea
                      rows={4}
                      value={formData.fullBio || ''}
                      onChange={(e) => setFormData({ ...formData, fullBio: e.target.value })}
                      className="w-full px-3 py-2 border rounded-lg dark:bg-gray-800 dark:border-gray-700 dark:text-white"
                      placeholder="Ustoz haqida to'liq ma'lumotlar, tajribasi, sertifikatlari..."
                    />
                  </div>
                </>
              )}

              {/* CERTIFICATE FORM */}
              {modalType === 'certificate' && (
                <>
                  <div className="grid grid-cols-2 gap-4">
                    <div>
                      <label className="block text-xs font-semibold text-gray-700 dark:text-gray-300 mb-1">Kategoriya</label>
                      <select
                        value={formData.category || 'Xalqaro'}
                        onChange={(e) => setFormData({ ...formData, category: e.target.value })}
                        className="w-full px-3 py-2 border rounded-lg dark:bg-gray-800 dark:border-gray-700 dark:text-white text-xs font-medium"
                      >
                        <option value="Xalqaro">Xalqaro Sertifikatlar (IELTS, CEFR, SAT)</option>
                        <option value="Milliy">Milliy Sertifikatlar (DTM, A/A+)</option>
                        <option value="Oliygoh">Oliygohga kirishlar (universitet, grant)</option>
                      </select>
                    </div>
                    <div>
                      <label className="block text-xs font-semibold text-gray-700 dark:text-gray-300 mb-1">Sertifikat Turi</label>
                      <select
                        value={formData.certType || 'IELTS'}
                        onChange={(e) => setFormData({ ...formData, certType: e.target.value })}
                        className="w-full px-3 py-2 border rounded-lg dark:bg-gray-800 dark:border-gray-700 dark:text-white text-xs font-medium"
                      >
                        <option value="IELTS">IELTS</option>
                        <option value="Multilevel">Multilevel (CEFR)</option>
                        <option value="Milliy">Milliy Sertifikat (A/A+)</option>
                        <option value="SAT">SAT</option>
                        <option value="Oliygoh">Oliygoh (universitetga kirish)</option>
                        <option value="Boshqa">Boshqa Yo'nalish</option>
                      </select>
                    </div>
                  </div>

                  <div className="grid grid-cols-2 gap-4">
                    <div>
                      <label className="block text-xs font-semibold text-gray-700 dark:text-gray-300 mb-1">Sertifikat Sarlavhasi</label>
                      <input
                        type="text"
                        required
                        value={formData.title || ''}
                        onChange={(e) => setFormData({ ...formData, title: e.target.value })}
                        className="w-full px-3 py-2 border rounded-lg dark:bg-gray-800 dark:border-gray-700 dark:text-white text-xs"
                        placeholder="Masalan: IELTS 8.5 Natija"
                      />
                    </div>
                    <div>
                      <label className="block text-xs font-semibold text-gray-700 dark:text-gray-300 mb-1">O'quvchi Ismi</label>
                      <input
                        type="text"
                        required
                        value={formData.studentName || ''}
                        onChange={(e) => setFormData({ ...formData, studentName: e.target.value })}
                        className="w-full px-3 py-2 border rounded-lg dark:bg-gray-800 dark:border-gray-700 dark:text-white text-xs"
                        placeholder="Jasurbek Karimov"
                      />
                    </div>
                  </div>

                  {formData.category !== 'Oliygoh' &&
                   formData.certType !== 'Oliygoh' &&
                   (formData.certType === 'IELTS' || formData.certType === 'Multilevel' || formData.category === 'Xalqaro') ? (
                    <div className="p-3 bg-amber-50 dark:bg-amber-950/30 border border-amber-200 dark:border-amber-900 rounded-xl space-y-2">
                      <div className="flex items-center justify-between">
                        <span className="text-xs font-bold text-amber-800 dark:text-amber-300">4 ta Section Ballari (IELTS / CEFR)</span>
                      </div>
                      <div className="grid grid-cols-5 gap-2">
                        <div>
                          <label className="block text-[11px] font-bold text-gray-700 dark:text-gray-300 mb-1">Overall</label>
                          <input
                            type="text"
                            value={formData.overallScore || ''}
                            onChange={(e) => setFormData({ ...formData, overallScore: e.target.value })}
                            className="w-full px-2 py-1.5 border rounded-lg text-xs font-bold dark:bg-gray-800 dark:border-gray-700 dark:text-white"
                            placeholder="8.5"
                          />
                        </div>
                        <div>
                          <label className="block text-[11px] text-gray-600 dark:text-gray-400 mb-1">Listening</label>
                          <input
                            type="text"
                            value={formData.listening || ''}
                            onChange={(e) => setFormData({ ...formData, listening: e.target.value })}
                            className="w-full px-2 py-1.5 border rounded-lg text-xs dark:bg-gray-800 dark:border-gray-700 dark:text-white"
                            placeholder="9.0"
                          />
                        </div>
                        <div>
                          <label className="block text-[11px] text-gray-600 dark:text-gray-400 mb-1">Reading</label>
                          <input
                            type="text"
                            value={formData.reading || ''}
                            onChange={(e) => setFormData({ ...formData, reading: e.target.value })}
                            className="w-full px-2 py-1.5 border rounded-lg text-xs dark:bg-gray-800 dark:border-gray-700 dark:text-white"
                            placeholder="8.5"
                          />
                        </div>
                        <div>
                          <label className="block text-[11px] text-gray-600 dark:text-gray-400 mb-1">Writing</label>
                          <input
                            type="text"
                            value={formData.writing || ''}
                            onChange={(e) => setFormData({ ...formData, writing: e.target.value })}
                            className="w-full px-2 py-1.5 border rounded-lg text-xs dark:bg-gray-800 dark:border-gray-700 dark:text-white"
                            placeholder="7.5"
                          />
                        </div>
                        <div>
                          <label className="block text-[11px] text-gray-600 dark:text-gray-400 mb-1">Speaking</label>
                          <input
                            type="text"
                            value={formData.speaking || ''}
                            onChange={(e) => setFormData({ ...formData, speaking: e.target.value })}
                            className="w-full px-2 py-1.5 border rounded-lg text-xs dark:bg-gray-800 dark:border-gray-700 dark:text-white"
                            placeholder="8.0"
                          />
                        </div>
                      </div>
                    </div>
                  ) : (
                    <div>
                      <label className="block text-xs font-semibold text-gray-700 dark:text-gray-300 mb-1">
                        {formData.category === 'Oliygoh' || formData.certType === 'Oliygoh'
                          ? 'Oliygoh va natija (matn)'
                          : 'Natija Balli / Daraja (String / Matn)'}
                      </label>
                      <input
                        type="text"
                        value={formData.overallScore || ''}
                        onChange={(e) => setFormData({ ...formData, overallScore: e.target.value })}
                        className="w-full px-3 py-2 border rounded-lg dark:bg-gray-800 dark:border-gray-700 dark:text-white text-xs font-semibold"
                        placeholder={
                          formData.category === 'Oliygoh' || formData.certType === 'Oliygoh'
                            ? "Masalan: TATU — grant, Inha University — 100% stipendiya"
                            : "Masalan: Milliy Sertifikat A+, 1500+ SAT, 96.4 ball yoki DTM 100%"
                        }
                      />
                    </div>
                  )}

                  <div>
                    <label className="block text-xs font-semibold text-gray-700 dark:text-gray-300 mb-1">Qo'shimcha Izoh (Keshbek / Izoh)</label>
                    <input
                      type="text"
                      value={formData.resultNote || ''}
                      onChange={(e) => setFormData({ ...formData, resultNote: e.target.value })}
                      className="w-full px-3 py-2 border rounded-lg dark:bg-gray-800 dark:border-gray-700 dark:text-white text-xs"
                      placeholder="DTM 100% natija, Keshbek berilgan..."
                    />
                  </div>

                  <div>
                    <label className="block text-xs font-semibold text-gray-700 dark:text-gray-300 mb-1">Sertifikat Rasmi (Kompyuterdan yuklash)</label>
                    <div className="flex items-center gap-3">
                      {formData.imageUrl && (
                        <img src={formData.imageUrl} alt="Preview" className="w-12 h-12 rounded-lg object-cover border border-amber-500 flex-shrink-0" />
                      )}
                      <label className="flex items-center gap-2 px-3 py-2 bg-amber-50 hover:bg-amber-100 dark:bg-amber-950/40 dark:hover:bg-amber-900/60 border border-amber-200 dark:border-amber-800 rounded-lg text-xs font-semibold text-amber-600 dark:text-amber-400 cursor-pointer transition-colors flex-shrink-0">
                        <Upload className="w-4 h-4" />
                        <span>{uploading ? "Yuklanmoqda..." : "Sertifikat Yuklash"}</span>
                        <input
                          type="file"
                          accept="image/*"
                          disabled={uploading}
                          className="hidden"
                          onChange={(e) => {
                            if (e.target.files && e.target.files[0]) {
                              handleFileUpload(e.target.files[0], 'imageUrl')
                            }
                          }}
                        />
                      </label>
                      <input
                        type="text"
                        value={formData.imageUrl || ''}
                        onChange={(e) => setFormData({ ...formData, imageUrl: e.target.value })}
                        className="flex-1 px-3 py-2 border rounded-lg text-xs dark:bg-gray-800 dark:border-gray-700 dark:text-white"
                        placeholder="yoki rasm URL manzili..."
                      />
                    </div>
                  </div>
                </>
              )}

              {/* TESTIMONIAL FORM */}
              {modalType === 'testimonial' && (
                <>
                  <div>
                    <label className="block text-xs font-semibold text-gray-700 dark:text-gray-300 mb-1">Muallif Ismi</label>
                    <input
                      type="text"
                      required
                      value={formData.authorName || ''}
                      onChange={(e) => setFormData({ ...formData, authorName: e.target.value })}
                      className="w-full px-3 py-2 border rounded-lg dark:bg-gray-800 dark:border-gray-700 dark:text-white"
                      placeholder="Dilfuza Rahimova"
                    />
                  </div>
                  <div>
                    <label className="block text-xs font-semibold text-gray-700 dark:text-gray-300 mb-1">Maqomi (Ota-ona / Bitiruvchi)</label>
                    <input
                      type="text"
                      value={formData.authorRole || ''}
                      onChange={(e) => setFormData({ ...formData, authorRole: e.target.value })}
                      className="w-full px-3 py-2 border rounded-lg dark:bg-gray-800 dark:border-gray-700 dark:text-white"
                      placeholder="Ota-ona"
                    />
                  </div>
                  <div>
                    <label className="block text-xs font-semibold text-gray-700 dark:text-gray-300 mb-1">Fikr / Izoh</label>
                    <textarea
                      rows={3}
                      required
                      value={formData.comment || ''}
                      onChange={(e) => setFormData({ ...formData, comment: e.target.value })}
                      className="w-full px-3 py-2 border rounded-lg dark:bg-gray-800 dark:border-gray-700 dark:text-white"
                      placeholder="Markazdagi dars berish sifatidan juda mamnunmiz..."
                    />
                  </div>
                </>
              )}

              {/* FAQ FORM */}
              {modalType === 'faq' && (
                <>
                  <div>
                    <label className="block text-xs font-semibold text-gray-700 dark:text-gray-300 mb-1">Savol</label>
                    <input
                      type="text"
                      required
                      value={formData.question || ''}
                      onChange={(e) => setFormData({ ...formData, question: e.target.value })}
                      className="w-full px-3 py-2 border rounded-lg dark:bg-gray-800 dark:border-gray-700 dark:text-white"
                      placeholder="Sinov darsi bepulmi?"
                    />
                  </div>
                  <div>
                    <label className="block text-xs font-semibold text-gray-700 dark:text-gray-300 mb-1">Javob</label>
                    <textarea
                      rows={3}
                      required
                      value={formData.answer || ''}
                      onChange={(e) => setFormData({ ...formData, answer: e.target.value })}
                      className="w-full px-3 py-2 border rounded-lg dark:bg-gray-800 dark:border-gray-700 dark:text-white"
                      placeholder="Ha, birinchi dars mutlaqo bepul..."
                    />
                  </div>
                </>
              )}

              {/* COMMON FIELDS */}
              <div className="grid grid-cols-2 gap-4">
                <div>
                  <label className="block text-xs font-semibold text-gray-700 dark:text-gray-300 mb-1">Tartib (Order)</label>
                  <input
                    type="number"
                    value={formData.order || 1}
                    onChange={(e) => setFormData({ ...formData, order: parseInt(e.target.value) || 1 })}
                    className="w-full px-3 py-2 border rounded-lg dark:bg-gray-800 dark:border-gray-700 dark:text-white"
                  />
                </div>
                <div className="flex items-center pt-6">
                  <label className="flex items-center gap-2 cursor-pointer text-sm text-gray-700 dark:text-gray-300">
                    <input
                      type="checkbox"
                      checked={formData.isActive ?? true}
                      onChange={(e) => setFormData({ ...formData, isActive: e.target.checked })}
                      className="w-4 h-4 text-blue-600 rounded"
                    />
                    Saytda ko'rinsin (Faol)
                  </label>
                </div>
              </div>

              {/* ACTIONS */}
              <div className="flex justify-end gap-3 pt-4 border-t border-gray-200 dark:border-gray-800">
                <button
                  type="button"
                  onClick={closeModal}
                  className="px-4 py-2 text-sm text-gray-600 dark:text-gray-400 hover:bg-gray-100 dark:hover:bg-gray-800 rounded-lg"
                >
                  Bekor qilish
                </button>
                <button
                  type="submit"
                  disabled={editingItem ? !canEdit : !canCreate}
                  className="px-5 py-2 text-sm bg-blue-600 hover:bg-blue-700 text-white font-medium rounded-lg disabled:opacity-50"
                >
                  Saqlash
                </button>
              </div>
            </form>
          </div>
        </div>
      )}
    </div>
  )
}
