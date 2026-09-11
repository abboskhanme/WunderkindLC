import type { LucideIcon } from 'lucide-react'
import { Smartphone, MessageCircle, Bell } from 'lucide-react'
import type { BadgeTone } from '@/components/ui/Badge'

/**
 * Xabar kanallari — YAGONA tartib va yorliqlar manbai.
 * Composer kartalari, Tarix badge'lari va Sozlamalar ichki tablari shu tartib/yorliqqa amal qiladi.
 */
export type ChannelKey = 'sms' | 'telegram' | 'push'

export interface ChannelMeta {
  key: ChannelKey
  /** Asosiy yorliq (masalan "SMS") */
  label: string
  /** Qo'shimcha izoh (masalan "Eskiz") */
  sub: string
  icon: LucideIcon
  /** Badge rangi (Tarix va boshqa ro'yxatlarda) */
  tone: BadgeTone
}

/** Kanallar yagona tartibda: SMS → Telegram → Push. */
export const CHANNEL_ORDER: ChannelKey[] = ['sms', 'telegram', 'push']

export const CHANNELS: Record<ChannelKey, ChannelMeta> = {
  sms: { key: 'sms', label: 'SMS', sub: 'Eskiz', icon: Smartphone, tone: 'green' },
  telegram: { key: 'telegram', label: 'Telegram', sub: 'Bot orqali', icon: MessageCircle, tone: 'blue' },
  push: { key: 'push', label: 'Push', sub: 'Mobil ilova', icon: Bell, tone: 'violet' },
}

/** Tartiblangan ro'yxat ko'rinishida (map qilish qulay bo'lishi uchun). */
export const CHANNEL_LIST: ChannelMeta[] = CHANNEL_ORDER.map((k) => CHANNELS[k])
