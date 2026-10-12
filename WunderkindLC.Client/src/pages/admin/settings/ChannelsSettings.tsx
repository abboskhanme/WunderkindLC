import { TelegramSettings } from './TelegramSettings'

/**
 * "Xabar kanallari" — markazning YAGONA tashqi xabar kanali: Telegram bot.
 * SMS kanali va push (Firebase) 2026-10 da olib tashlandi (markazda o'rnatiladigan
 * mobil ilova ishlatilmaydi, xabarlar faqat Telegram orqali boradi). Marshrut va ruxsat kaliti
 * (`settings.channels`) o'zgarmagan — eski xatcho'plar shu sahifaga tushadi.
 */
export function ChannelsSettings() {
  return <TelegramSettings />
}
