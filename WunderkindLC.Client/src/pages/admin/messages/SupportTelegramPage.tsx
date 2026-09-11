import { PageHeader } from '@/components/ui/PageHeader'
import { SupportPanel } from './SupportPanel'

/**
 * "Support Telegram" — alohida sahifa (Xabarlar bo'limidan keyin nav'da).
 * Botda /start bosgan foydalanuvchilar bilan ikki tomonlama yozishma.
 */
export function SupportTelegramPage() {
  return (
    <div>
      <PageHeader title="Support Telegram" sub="Telegram bot foydalanuvchilari bilan yozishma" />
      <SupportPanel />
    </div>
  )
}
