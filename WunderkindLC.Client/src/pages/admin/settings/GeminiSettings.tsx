import { useEffect, useState } from 'react'
import { CheckCircle2, Sparkles, XCircle } from 'lucide-react'
import { getGeminiSettings, type GeminiConfig } from '@/api/services/settings'
import { Card } from '@/components/ui/Card'
import { Badge } from '@/components/ui/Badge'
import { Input } from '@/components/ui/Input'
import { Loader } from '@/components/ui/Loader'
import { EnvSecretField } from '@/components/settings/EnvSecretField'

/**
 * AI Tahlil (Google Gemini) sozlamasi.
 * API kaliti UI'dan KIRITILMAYDI — u serverdagi `.env` faylida (GEMINI_API_KEY), bazada
 * saqlanmaydi. Bu sahifa faqat holatni va qanday sozlashni ko'rsatadi.
 * Model ham env'dan (GEMINI_MODEL, default gemini-3.1-flash-lite).
 */
export function GeminiSettings() {
  const [cfg, setCfg] = useState<GeminiConfig | null>(null)
  const [loading, setLoading] = useState(true)

  useEffect(() => {
    getGeminiSettings()
      .then(setCfg)
      .finally(() => setLoading(false))
  }, [])

  if (loading) return <Loader label="Yuklanmoqda..." />

  const configured = !!cfg?.configured

  return (
    <Card
      title={
        <span className="flex flex-wrap items-center gap-2">
          <Sparkles className="h-4 w-4 text-brand-600" /> AI Tahlil (Gemini)
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
        Har o'quvchi profilida <b>"AI Tahlil"</b> tugmasi bo'ladi: bosilganda o'quvchining barcha
        ma'lumotlari (baholar, davomat, uy vazifa, baholash, balans) <b>Google Gemini</b>{' '}
        orqali tahlil qilinib, o'zbek tilida xulosa va tavsiyalar beriladi. Kalit berilmasa AI tahlil
        ishlamaydi.
      </p>

      <div className="max-w-xl space-y-4">
        <EnvSecretField
          label="Gemini API kaliti"
          secret={cfg?.key}
          sample="AIzaSy..."
          hint={
            <>
              Kalit <b>Google AI Studio → Get API key</b> (aistudio.google.com/app/apikey) dan olinadi.
            </>
          }
        />
        <div>
          <label className="mb-1 block text-sm font-medium text-slate-700">Model</label>
          <p className="mb-2 text-xs text-slate-400">
            Server <code className="rounded bg-slate-100 px-1">GEMINI_MODEL</code> env o'zgaruvchisidan olinadi.
          </p>
          <Input value={cfg?.model ?? ''} readOnly disabled className="max-w-[280px] font-mono text-xs" />
        </div>
      </div>
    </Card>
  )
}
