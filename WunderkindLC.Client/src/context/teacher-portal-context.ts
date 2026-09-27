import { createContext, useContext } from 'react'

/**
 * O'qituvchi portali qobig'i (`TeacherMobileLayout`) bir marta yuklaydigan umumiy ma'lumot —
 * sahifalar uni qayta so'ramasin (markaz nomi/logosi, Telegram kanali, Support bayrog'i).
 */
export interface TeacherPortalInfo {
  schoolName: string
  logoUrl: string
  telegramChannel: string
  /** Support o'qituvchimi — "Support" bo'limi menyuda ko'rinadimi */
  isSupport: boolean
}

export const TeacherPortalContext = createContext<TeacherPortalInfo>({
  schoolName: '',
  logoUrl: '',
  telegramChannel: '',
  isSupport: false,
})

export function useTeacherPortal(): TeacherPortalInfo {
  return useContext(TeacherPortalContext)
}
