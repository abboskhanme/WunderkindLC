import { api } from '../client'

export interface LandingSocials {
  telegramUrl: string
  instagramUrl: string
  youtubeUrl: string
  facebookUrl: string
  centerEmail: string
  appStoreUrl: string
  playMarketUrl: string
  contactPhone: string
  centerAddress: string
  workingHours: string
}

export interface LandingTeacher {
  id: string
  fullName: string
  subject: string
  photoUrl: string
  badge: string
  shortBio: string
  fullBio: string
  order: number
  isActive: boolean
  createdAt: string
}

export interface LandingCertificate {
  id: string
  title: string
  studentName: string
  imageUrl: string
  category: string
  certType: string
  overallScore: string
  listening?: string
  reading?: string
  writing?: string
  speaking?: string
  resultNote?: string
  order: number
  isActive: boolean
  createdAt: string
}

export interface LandingTestimonial {
  id: string
  authorName: string
  authorRole: string
  avatarUrl: string
  rating: number
  comment: string
  order: number
  isActive: boolean
  createdAt: string
}

export interface LandingFaq {
  id: string
  question: string
  answer: string
  order: number
  isActive: boolean
  createdAt: string
}

export const landingCmsService = {
  // Teachers
  getTeachers: () => api.get<LandingTeacher[]>('/admin/landing/teachers'),
  createTeacher: (data: Partial<LandingTeacher>) => api.post<LandingTeacher>('/admin/landing/teachers', data),
  updateTeacher: (id: string, data: Partial<LandingTeacher>) => api.put<LandingTeacher>(`/admin/landing/teachers/${id}`, data),
  deleteTeacher: (id: string) => api.delete<{ ok: boolean }>(`/admin/landing/teachers/${id}`),

  // Certificates
  getCertificates: () => api.get<LandingCertificate[]>('/admin/landing/certificates'),
  createCertificate: (data: Partial<LandingCertificate>) => api.post<LandingCertificate>('/admin/landing/certificates', data),
  updateCertificate: (id: string, data: Partial<LandingCertificate>) => api.put<LandingCertificate>(`/admin/landing/certificates/${id}`, data),
  deleteCertificate: (id: string) => api.delete<{ ok: boolean }>(`/admin/landing/certificates/${id}`),

  // Testimonials
  getTestimonials: () => api.get<LandingTestimonial[]>('/admin/landing/testimonials'),
  createTestimonial: (data: Partial<LandingTestimonial>) => api.post<LandingTestimonial>('/admin/landing/testimonials', data),
  updateTestimonial: (id: string, data: Partial<LandingTestimonial>) => api.put<LandingTestimonial>(`/admin/landing/testimonials/${id}`, data),
  deleteTestimonial: (id: string) => api.delete<{ ok: boolean }>(`/admin/landing/testimonials/${id}`),

  // FAQs
  getFaqs: () => api.get<LandingFaq[]>('/admin/landing/faqs'),
  createFaq: (data: Partial<LandingFaq>) => api.post<LandingFaq>('/admin/landing/faqs', data),
  updateFaq: (id: string, data: Partial<LandingFaq>) => api.put<LandingFaq>(`/admin/landing/faqs/${id}`, data),
  deleteFaq: (id: string) => api.delete<{ ok: boolean }>(`/admin/landing/faqs/${id}`),

  // Location Map URL (landing "Aloqa" bo'limidagi xarita)
  getMapUrl: () => api.get<{ mapUrl: string }>('/admin/landing/map-url'),
  /**
   * ⚠️ TUR ATAYIN `string`, `string | null` EMAS.
   *
   * Serverdagi `MergeText` semantikasi: `null` = "maydon yuborilmadi, eski qiymat qolsin",
   * `""` = "admin ATAYIN tozaladi". Admin formasida esa "tegilmadi" holati YO'Q — maydon har doim
   * ekranda turadi va aynan uning qiymati saqlanadi. Shuning uchun `null` yuborish imkoniyati
   * ochiq qoldirilmadi: u faqat "saqladim, lekin hech narsa o'zgarmadi" degan jim xatoga olib
   * kelardi. Maydonni bo'shatib saqlash = xaritani o'chirish (ommaviy javobda sukut xarita).
   */
  updateMapUrl: (mapUrl: string) => api.post<{ ok: boolean; mapUrl: string }>('/admin/landing/map-url', { mapUrl }),

  // Socials & Contact Info
  getSocials: () => api.get<LandingSocials>('/admin/landing/socials'),
  /**
   * ⚠️ TUR ATAYIN `LandingSocials` (TO'LIQ), `Partial<...>` EMAS.
   *
   * Endpoint barcha maydonni bitta payload'dan yozadi. Server endi yuborilmagan (`null`) maydonni
   * ESKI qiymatida qoldiradi, lekin klient baribir to'liq obyekt yuborishi kerak: aks holda
   * "faqat telefonni tahrirladim" degan holatda qolgan maydonlar yuborilmay, forma ekranda
   * ko'rsatgan qiymat bilan bazadagi qiymat ajralib ketardi. Bo'sh satr (`""`) — ATAYIN tozalash.
   */
  updateSocials: (data: LandingSocials) => api.post<{ ok: boolean; socials: LandingSocials }>('/admin/landing/socials', data),

  /**
   * Rasm yuklash — `UploadsController` → `UploadedFileDto(Name, Url, Size, ContentType)`.
   * ⚠️ Javobda `fileName` YO'Q (ilgari shu nom yozilgan edi va u hech qachon to'lmasdi) — `name`.
   */
  uploadImage: (file: File) => {
    const formData = new FormData()
    formData.append('file', file)
    return api.post<{ name: string; url: string; size: number; contentType: string }>('/admin/uploads', formData, {
      headers: { 'Content-Type': 'multipart/form-data' },
    })
  },
}
