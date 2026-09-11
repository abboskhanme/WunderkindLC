import { useEffect, useState } from 'react'
import type { FormEvent } from 'react'
import { AlertTriangle, Check, CheckCircle2, XCircle } from 'lucide-react'
import {
  getTelegramSettings,
  saveTelegramSettings,
  type TelegramConfig,
} from '@/api/services/settings'
import { Card } from '@/components/ui/Card'
import { Badge } from '@/components/ui/Badge'
import { Button } from '@/components/ui/Button'
import { Input, Select } from '@/components/ui/Input'
import { Loader } from '@/components/ui/Loader'
import { EnvSecretField } from '@/components/settings/EnvSecretField'
import type { EnvSecret } from '@/api/services/settings'

/**
 * Telegram bot sozlamasi (ota-onalarga e'lon yuborish uchun).
 * TOKEN bu yerda KIRITILMAYDI — u serverdagi `.env` faylida (TELEGRAM_BOT_TOKEN), bazada
 * saqlanmaydi. Bu sahifada faqat maxfiy bo'lmagan qismlar: bot nomi/username, kanal, telefon
 * moslash. Zaxira nusxa va APK yuklash alohida bo'limlarda.
 */
export function TelegramSettings() {
  const [tokenSecret, setTokenSecret] = useState<EnvSecret | null>(null)
  const [username, setUsername] = useState('')
  const [name, setName] = useState('')
  const [channel, setChannel] = useState('')
  const [phoneMatchField, setPhoneMatchField] = useState<'parent' | 'student'>('parent')
  const [configured, setConfigured] = useState(false)
  const [loading, setLoading] = useState(true)
  const [status, setStatus] = useState<'idle' | 'saving' | 'saved'>('idle')
  // Majburiy obuna tekshiruvi haqiqatan ishlayaptimi (serverdan diagnostika)
  const [channelStatus, setChannelStatus] = useState('')
  const [channelMessage, setChannelMessage] = useState('')

  useEffect(() => {
    getTelegramSettings()
      .then((c: TelegramConfig) => {
        setTokenSecret(c.token ?? null)
        setUsername(c.botUsername ?? '')
        setName(c.botName ?? '')
        setChannel(c.channel ?? '')
        setPhoneMatchField(c.phoneMatchField === 'student' ? 'student' : 'parent')
        setConfigured(c.configured)
        setChannelStatus(c.channelStatus ?? '')
        setChannelMessage(c.channelMessage ?? '')
      })
      .catch(() => setLoading(false))
      .finally(() => setLoading(false))
  }, [])

  const onSubmit = async (e: FormEvent) => {
    e.preventDefault()
    setStatus('saving')
    try {
      const saved = await saveTelegramSettings({
        botUsername: (username ?? '').trim(),
        botName: (name ?? '').trim(),
        channel: (channel ?? '').trim(),
        phoneMatchField,
      })
      setConfigured(saved.configured)
      setTokenSecret(saved.token ?? null)
      setStatus('saved')
      setTimeout(() => setStatus('idle'), 2000)
    } catch (err) {
      console.error('Telegram sozlamalarini saqlashda xato:', err)
      setStatus('idle')
    }
  }

  if (loading) return <Loader label="Yuklanmoqda..." />

  return (
    <Card
      title={
        <span className="flex items-center gap-2">
          Telegram bot
          {configured ? (
            <Badge tone="green">
              <CheckCircle2 className="h-3.5 w-3.5" /> Sozlangan
            </Badge>
          ) : (
            <Badge tone="default">
              <XCircle className="h-3.5 w-3.5" /> Sozlanmagan
            </Badge>
          )}
        </span>
      }
    >
      <p className="mb-4 text-sm text-slate-400">
        Bot orqali guruh ota-onalariga e'lon yuboriladi. Tokenni Telegramdagi{' '}
        <span className="font-medium text-slate-500">@BotFather</span> dan oling
        (/newbot → token) va serverdagi <code className="rounded bg-slate-100 px-1">.env</code> ga
        joylang — bot qayta ishga tushganda avtomatik ulanadi.
      </p>

      <form onSubmit={onSubmit} className="max-w-2xl space-y-4">
        <EnvSecretField
          label="Bot tokeni"
          secret={tokenSecret}
          sample="123456789:AAH..."
          hint="Token @BotFather dan olinadi (/newbot yoki /token)."
        />
        <Input
          label="Bot nomi (ko'rsatish uchun)"
          placeholder="WunderkindLC Bot"
          value={name}
          onChange={(e) => setName(e.target.value)}
          autoComplete="off"
        />
        <Input
          label="Bot foydalanuvchi nomi (@username)"
          placeholder="MarkazBot"
          value={username}
          onChange={(e) => setUsername(e.target.value)}
          autoComplete="off"
        />

        <div className="rounded-lg bg-slate-50 px-3 py-2 text-xs text-slate-500">
          Ota-ona (yoki o'quvchi) botni ochib (masalan {username ? `@${username}` : '@BotUsername'}) "Start"
          bossin va telefon raqamini ulashsin — raqami pastdagi sozlamaga qarab solishtiriladi.
        </div>

        <Select
          label="Kontakt ulashilganda qaysi raqam bo'yicha tekshirilsin"
          value={phoneMatchField}
          onChange={(e) => setPhoneMatchField(e.target.value as 'parent' | 'student')}
        >
          <option value="parent">Ota-ona raqami</option>
          <option value="student">O'quvchining o'zi raqami</option>
        </Select>
        <p className="-mt-2 text-xs text-slate-400">
          "Ota-ona raqami" — botga ota-ona o'z raqamini yuborsa moslashadi (standart). "O'quvchining
          o'zi raqami" — bot orqali o'quvchi o'zi ro'yxatdan o'tsa, uning shaxsiy telefon raqami
          bilan solishtiriladi.
        </p>

        <div className="border-t border-slate-100 pt-4">
          <Input
            label="Telegram kanal (o'quvchi/o'qituvchi ilovasida ko'rinadi)"
            placeholder="@wunderkindedu yoki https://t.me/wunderkindedu"
            value={channel}
            onChange={(e) => setChannel(e.target.value)}
            autoComplete="off"
          />
          <p className="mt-1 text-xs text-slate-400">
            To'ldirilsa — o'quvchi va o'qituvchi ilovasida "Telegram kanalga o'tish" tugmasi chiqadi,
            hamda botda MAJBURIY OBUNA yoqiladi (kod olish va onlayn testni ishlash uchun).
          </p>

          {/* Majburiy obuna DIAGNOSTIKASI — Telegram getChatMember faqat bot kanalda ADMIN
              bo'lsagina ishlaydi. Aks holda bot tekshira olmaydi va hammani o'tkazib yuboradi. */}
          {channelMessage && (
            <div
              className={
                channelStatus === 'ok'
                  ? 'mt-2 flex items-start gap-2 rounded-lg bg-emerald-50 px-3 py-2 text-xs text-emerald-700'
                  : channelStatus === 'not-set'
                    ? 'mt-2 flex items-start gap-2 rounded-lg bg-slate-50 px-3 py-2 text-xs text-slate-500'
                    : 'mt-2 flex items-start gap-2 rounded-lg bg-amber-50 px-3 py-2 text-xs text-amber-700'
              }
            >
              {channelStatus === 'ok' ? (
                <CheckCircle2 className="mt-px h-4 w-4 shrink-0" />
              ) : (
                <AlertTriangle className="mt-px h-4 w-4 shrink-0" />
              )}
              <span>
                <b>Majburiy obuna:</b> {channelMessage}
                {channelStatus !== 'ok' && channelStatus !== 'not-set' && (
                  <>
                    {' '}
                    <span className="text-amber-600">
                      (Kanalga kiring → Administratorlar → botni qo'shing.)
                    </span>
                  </>
                )}
              </span>
            </div>
          )}
        </div>

        <div className="flex items-center gap-3">
          <Button type="submit" disabled={status === 'saving'}>
            {status === 'saving' ? 'Saqlanmoqda...' : 'Saqlash'}
          </Button>
          {status === 'saved' && (
            <span className="inline-flex items-center gap-1 text-sm font-medium text-emerald-600">
              <Check className="h-4 w-4" /> Saqlandi
            </span>
          )}
        </div>
      </form>
    </Card>
  )
}
