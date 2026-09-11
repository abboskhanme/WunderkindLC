import { useParams } from 'react-router-dom'
import { RequirePerm } from '@/components/auth/RequirePerm'
import { settingsPagePerm } from '@/config/constants'
import { SettingsPage } from './SettingsPage'

/**
 * «Sozlamalar» sahifasining kirish nuqtasi (`/admin/settings/:section`).
 *
 * <p>Bo'lim ichida o'nga yaqin MUSTAQIL sahifa bor (markaz ma'lumotlari, zaxira nusxa, APK,
 * integratsiyalar...) va ular BITTA marshrutdan chiziladi. Shuning uchun ruxsat ham segmentga
 * qarab tanlanadi: `settings.backup`, `settings.apk`, ... (`settingsPagePerm`).</p>
 *
 * <p>⚠️ Katalogda yo'q (yangi qo'shilgan yoki noto'g'ri yozilgan) segment `settings` kalitiga
 * tushadi — sahifa JIMGINA yopilib qolmasin. Yangi sozlama sahifasi qo'shsangiz uni
 * `adminPermissions` dagi `settings.pages` ro'yxatiga ham qo'shing.</p>
 */
export function SettingsEntry() {
  const { section } = useParams<{ section: string }>()
  return (
    <RequirePerm perm={settingsPagePerm(section)}>
      <SettingsPage />
    </RequirePerm>
  )
}
