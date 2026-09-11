import { api } from '../client'

/**
 * Ommaviy (autentifikatsiyasiz) LID FORMASI. `api` instance tokensiz ham ishlaydi — endpointlar
 * [AllowAnonymous]. Ariza yuborilganda CRM'da lid yaratiladi (manba = formaning manbasi).
 */

export interface PublicLeadFormField {
  id: string
  label: string
  kind: string
  options: string[]
  placeholder: string
  required: boolean
}

/** "Rahmat" ekranidagi ikonka: kind ∈ instagram | telegram | facebook | youtube | website. */
export interface PublicSocialLink {
  kind: string
  url: string
}

export interface PublicLeadForm {
  title: string
  intro: string
  buttonText: string
  courseName: string
  askAge: boolean
  askCourse: boolean
  askParentPhone: boolean
  /** Kurs variantlari — formaning O'ZIDA yozilgan (markaz kurslari katalogidan EMAS). */
  courses: string[]
  fields: PublicLeadFormField[]
  socials: PublicSocialLink[]
}

/** Slug bo'yicha faol formani oladi. 404 — topilmadi/faol emas. */
export async function getPublicLeadForm(slug: string): Promise<PublicLeadForm> {
  const { data } = await api.get<PublicLeadForm>(`/public/form/${slug}`)
  return data
}

/** Arizani yuboradi → rahmat matni qaytadi. */
export async function submitPublicLeadForm(
  slug: string,
  body: {
    fullName: string
    phone: string
    parentPhone?: string
    age: number
    course?: string
    answers: Record<string, string[]>
    ref?: string
  },
): Promise<{ message: string }> {
  const { data } = await api.post<{ message: string }>(`/public/form/${slug}/submit`, body)
  return data
}
