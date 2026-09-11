import { useEffect, useMemo, useState } from 'react'
import { Link } from 'react-router-dom'
import { Sparkles, Check, Search, Lock, LockOpen, Loader2, AlertCircle } from 'lucide-react'
import type { AiCheckOverviewRow } from '@/types'
import {
  getAiCheckOverview,
  getAiCheckSettings,
  saveAiCheckSettings,
  saveAiAccess,
  setAiCheckEnabled,
} from '@/api/services/aiCheck'
import { Card } from '@/components/ui/Card'
import { Button } from '@/components/ui/Button'
import { Loader } from '@/components/ui/Loader'
import { PageHeader } from '@/components/ui/PageHeader'
import { apiErrorMessage, cn } from '@/lib/utils'

const control =
  'rounded-lg border border-slate-200 px-2 py-1.5 text-sm text-slate-800 outline-none transition-colors focus:border-brand-400'

export function AiCheckPage() {
  const [loading, setLoading] = useState(true)
  const [rows, setRows] = useState<AiCheckOverviewRow[]>([])
  const [defaultLimit, setDefaultLimit] = useState(3)
  const [savingDefault, setSavingDefault] = useState<'idle' | 'saving' | 'saved'>('idle')
  const [query, setQuery] = useState('')
  // Bo'lim o'quvchi ilovasida ochiqmi (default: yopiq).
  const [enabled, setEnabled] = useState(false)
  const [togglingEnabled, setTogglingEnabled] = useState(false)
  const [enabledError, setEnabledError] = useState('')

  // FISH yoki guruh bo'yicha qidiruv (registrsiz).
  const filtered = useMemo(() => {
    const q = query.trim().toLowerCase()
    if (!q) return rows
    return rows.filter(
      (r) => r.fullName.toLowerCase().includes(q) || (r.className || '').toLowerCase().includes(q),
    )
  }, [rows, query])

  useEffect(() => {
    Promise.all([getAiCheckOverview(), getAiCheckSettings()])
      .then(([o, s]) => {
        setRows(o)
        setDefaultLimit(s.defaultDailyLimit)
        setEnabled(!!s.enabled)
      })
      .finally(() => setLoading(false))
  }, [])

  // Bo'limni o'quvchi ilovasida ochish/yopish (kunlik limitdan ALOHIDA endpoint).
  const toggleEnabled = async () => {
    const next = !enabled
    setTogglingEnabled(true)
    setEnabledError('')
    try {
      await setAiCheckEnabled(next)
      setEnabled(next)
    } catch (e) {
      setEnabledError(apiErrorMessage(e, "Holatni o'zgartirib bo'lmadi"))
    } finally {
      setTogglingEnabled(false)
    }
  }

  const saveDefault = async () => {
    setSavingDefault('saving')
    await saveAiCheckSettings(defaultLimit)
    setSavingDefault('saved')
    setTimeout(() => setSavingDefault('idle'), 1500)
  }

  const patchRow = (id: string, patch: Partial<AiCheckOverviewRow>) =>
    setRows((prev) => prev.map((r) => (r.studentId === id ? { ...r, ...patch } : r)))

  const persist = async (r: AiCheckOverviewRow) => {
    try {
      await saveAiAccess(r.studentId, r.effectiveLimit, r.premium, r.blocked)
    } catch (e) {
      alert(e instanceof Error ? e.message : 'Saqlab boʻlmadi')
    }
  }

  if (loading) return <Loader label="Yuklanmoqda..." />

  return (
    <div>
      <PageHeader
        title="AI tekshiruv (Speaking & Writing)"
        sub="Kim necha marta foydalanayotgani, kunlik limit, premium va cheklash boshqaruvi"
      />

      {/* Bo'limni o'quvchi ilovasida ochish/yopish — AI kalitlaridan ALOHIDA bayroq. */}
      <Card
        title="O'quvchi ilovasidagi holat"
        sub="Bo'lim o'quvchilarga ko'rinadimi va tekshiruv yuborsa bo'ladimi"
        className="mb-4"
        actions={
          <div className="flex items-center gap-2">
            <span
              className={cn(
                'inline-flex items-center gap-1.5 rounded-full px-2.5 py-1 text-xs font-semibold',
                enabled ? 'bg-emerald-50 text-emerald-700' : 'bg-amber-50 text-amber-700',
              )}
            >
              {enabled ? <LockOpen className="h-3.5 w-3.5" /> : <Lock className="h-3.5 w-3.5" />}
              {enabled ? 'Ilovada ochiq' : 'Ilovada YOPIQ'}
            </span>
            <Button
              variant={enabled ? 'secondary' : 'primary'}
              onClick={toggleEnabled}
              disabled={togglingEnabled}
            >
              {togglingEnabled ? (
                <Loader2 className="h-4 w-4 animate-spin" />
              ) : enabled ? (
                <Lock className="h-4 w-4" />
              ) : (
                <LockOpen className="h-4 w-4" />
              )}
              {togglingEnabled
                ? 'Saqlanmoqda...'
                : enabled
                  ? 'Ilovada yopish'
                  : 'Ilovada ochish'}
            </Button>
          </div>
        }
      >
        {enabledError && (
          <p className="mb-2 flex items-start gap-1.5 text-xs font-medium text-red-600">
            <AlertCircle className="mt-0.5 h-3.5 w-3.5 shrink-0" />
            {enabledError}
          </p>
        )}
        <p className="text-xs text-slate-400">
          {enabled
            ? "Bo'lim ochiq: o'quvchilar ilovada AI tekshiruvga (Speaking & Writing) ish yuborishlari mumkin — kunlik limit va bloklash qoidalari amal qiladi."
            : "Bo'lim yopiq: o'quvchi ilovasida \"Bu bo'lim hali markaz tomonidan ochilmagan\" xabari chiqadi va tekshiruv yuborib bo'lmaydi. Eski natijalar saqlanib qoladi — yo'qolmaydi."}
        </p>
        <p className="mt-1.5 text-xs text-slate-400">
          Bu bayroq AI kalitlaridan ALOHIDA: Gemini/Azure kalitlari kiritilgan bo'lsa ham, bayroq
          yopiq turganda o'quvchi ilovasida bo'lim ishlamaydi.
        </p>
      </Card>

      <Card
        title="Standart kunlik limit"
        sub="Premium yoki shaxsiy limiti yo'q o'quvchilar uchun (kuniga necha marta)"
        actions={
          <div className="flex items-center gap-2">
            <input
              type="number"
              min={0}
              value={defaultLimit}
              onChange={(e) => setDefaultLimit(Math.max(0, Number(e.target.value) || 0))}
              className={control + ' w-20'}
            />
            <Button onClick={saveDefault} disabled={savingDefault === 'saving'}>
              <Check className="h-4 w-4" />
              {savingDefault === 'saving' ? 'Saqlanmoqda...' : savingDefault === 'saved' ? 'Saqlandi' : 'Saqlash'}
            </Button>
          </div>
        }
      >
        <p className="text-xs text-slate-400">
          AI tekshiruv ishlashi uchun Sozlamalar → AI Tahlil (Gemini) kaliti, Speaking uchun esa
          Sozlamalar → Speaking (Azure) kiritilgan bo'lishi kerak.
        </p>
      </Card>

      <Card
        title="O'quvchilar"
        sub={`Jami ${rows.length} ta o'quvchi${query ? ` — topildi: ${filtered.length}` : ''}`}
        className="mt-4"
        actions={
          <div className="relative">
            <Search className="pointer-events-none absolute left-2.5 top-1/2 h-4 w-4 -translate-y-1/2 text-slate-400" />
            <input
              value={query}
              onChange={(e) => setQuery(e.target.value)}
              placeholder="FISH yoki guruh bo'yicha qidirish..."
              className={control + ' w-64 pl-8'}
            />
          </div>
        }
      >
        {rows.length === 0 ? (
          <p className="py-6 text-center text-sm text-slate-400">
            <Sparkles className="mx-auto mb-2 h-6 w-6 text-slate-300" />
            O'quvchilar yo'q.
          </p>
        ) : filtered.length === 0 ? (
          <p className="py-6 text-center text-sm text-slate-400">
            "{query}" bo'yicha o'quvchi topilmadi.
          </p>
        ) : (
          <div className="overflow-x-auto">
            <table className="table w-full">
              <thead>
                <tr>
                  <th className="text-left">O'quvchi (FISH)</th>
                  <th className="text-left">Guruh</th>
                  <th className="text-center">Writing</th>
                  <th className="text-center">Speaking</th>
                  <th className="text-center">Bugun</th>
                  <th className="text-center">Kunlik limit</th>
                  <th className="text-center">Premium</th>
                  <th className="text-center">Bloklash</th>
                  <th></th>
                </tr>
              </thead>
              <tbody>
                {filtered.map((r) => (
                  <tr key={r.studentId}>
                    <td className="font-medium text-slate-800">{r.fullName}</td>
                    <td className="text-slate-500">{r.className || '—'}</td>
                    <td className="text-center font-mono">{r.writingCount}</td>
                    <td className="text-center font-mono">{r.speakingCount}</td>
                    <td className="text-center font-mono">{r.todayUsed}</td>
                    <td className="text-center">
                      <input
                        type="number"
                        min={0}
                        value={r.effectiveLimit}
                        disabled={r.premium}
                        onChange={(e) => patchRow(r.studentId, { effectiveLimit: Math.max(0, Number(e.target.value) || 0) })}
                        onBlur={() => persist(r)}
                        className={control + ' w-16 text-center disabled:opacity-40'}
                      />
                    </td>
                    <td className="text-center">
                      <input
                        type="checkbox"
                        checked={r.premium}
                        onChange={(e) => {
                          const next = { ...r, premium: e.target.checked }
                          patchRow(r.studentId, { premium: e.target.checked })
                          persist(next)
                        }}
                        className="h-4 w-4 rounded border-slate-300 accent-brand-600"
                      />
                    </td>
                    <td className="text-center">
                      <input
                        type="checkbox"
                        checked={r.blocked}
                        onChange={(e) => {
                          const next = { ...r, blocked: e.target.checked }
                          patchRow(r.studentId, { blocked: e.target.checked })
                          persist(next)
                        }}
                        className="h-4 w-4 rounded border-slate-300 accent-red-600"
                      />
                    </td>
                    <td className="text-right">
                      <Link to={`/admin/ai-check/${r.studentId}`} className="text-sm font-medium text-brand-600 hover:text-brand-700">
                        Tarix →
                      </Link>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </Card>
    </div>
  )
}
