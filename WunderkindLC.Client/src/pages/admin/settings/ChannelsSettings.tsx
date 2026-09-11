import { useState } from 'react'
import { cn } from '@/lib/utils'
import { CHANNEL_LIST, type ChannelKey } from '@/config/channels'
import { EskizSettings } from './EskizSettings'
import { LocalSmsSettings } from './LocalSmsSettings'
import { TelegramSettings } from './TelegramSettings'
import { FirebaseSettings } from './FirebaseSettings'

type ChannelTab = 'sms' | 'telegram' | 'firebase'

/** Yagona kanal kaliti → sozlamalar ichki tab qiymati (push → firebase). */
const tabByChannel: Record<ChannelKey, ChannelTab> = {
  sms: 'sms',
  telegram: 'telegram',
  push: 'firebase',
}

/**
 * "Xabar kanallari" — SMS (Eskiz), Telegram (bot) va Push (Firebase) sozlamalari bitta joyda,
 * ichki tab orqali almashtiriladi. Tartib/yorliqlar `config/channels.ts` bilan yagona.
 */
export function ChannelsSettings({ initialTab = 'sms' }: { initialTab?: ChannelTab }) {
  const [tab, setTab] = useState<ChannelTab>(initialTab)

  return (
    <div className="space-y-4">
      <div className="tabs" role="tablist">
        {CHANNEL_LIST.map((c) => {
          const value = tabByChannel[c.key]
          return (
            <button
              key={c.key}
              type="button"
              role="tab"
              onClick={() => setTab(value)}
              className={cn('tab', tab === value && 'active')}
            >
              {c.label} ({c.sub})
            </button>
          )
        })}
      </div>

      {tab === 'sms' && (
        <div className="space-y-4">
          <EskizSettings />
          <LocalSmsSettings />
        </div>
      )}
      {tab === 'telegram' && <TelegramSettings />}
      {tab === 'firebase' && <FirebaseSettings />}
    </div>
  )
}
