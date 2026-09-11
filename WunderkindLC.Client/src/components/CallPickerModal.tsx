import { useEffect, useMemo, useState } from 'react'
import { Cloud, Loader2, Phone, Smartphone, User } from 'lucide-react'
import { originateCall } from '@/api/services/calls'
import { getCtiAgents, dialCtiAgent, type CtiAgent } from '@/api/services/cti'
import { Modal } from '@/components/ui/Modal'
import { Button } from '@/components/ui/Button'
import { cn } from '@/lib/utils'

/* ============================================================
   CallPickerModal — istalgan joydan (lid, o'quvchi, ...) qo'ng'iroq qilish oynasi.
   Oqim: [raqam tanlash (bir nechta bo'lsa)] → provayder tanlash
   (Bulut MoiZvonki | Local Call) → Local bo'lsa agent tanlash → natija.
   ============================================================ */

export interface CallOption {
  /** Raqam kimniki — "O'z raqami", "Otasi", "Onasi", "Ota-ona" ... */
  label: string
  number: string
}

const control =
  'w-full rounded-lg border border-slate-200 bg-white px-3 py-2 text-sm text-slate-800 outline-none transition-colors focus:border-brand-400'

export function CallPickerModal({
  open, onClose, title, numbers, studentId,
}: {
  open: boolean
  onClose: () => void
  /** Sarlavhada ko'rinadigan ism (lid/o'quvchi) */
  title?: string
  /** Mavjud raqamlar (bo'shlari oldindan filtrlangan bo'lsin). Bitta bo'lsa raqam bosqichi o'tkaziladi. */
  numbers: CallOption[]
  /** MoiZvonki tarixini o'quvchiga bog'lash uchun (ixtiyoriy) */
  studentId?: string
}) {
  const [number, setNumber] = useState<string | null>(null)
  const [provider, setProvider] = useState<'cloud' | 'local' | null>(null)
  const [agents, setAgents] = useState<CtiAgent[] | null>(null)
  const [agentId, setAgentId] = useState('')
  const [calling, setCalling] = useState(false)
  const [result, setResult] = useState<{ ok: boolean; text: string } | null>(null)

  // Oyna qayta ochilganda holatni tozalaymiz; bitta raqam bo'lsa darhol tanlangan.
  useEffect(() => {
    if (!open) return
    setNumber(numbers.length === 1 ? numbers[0].number : null)
    setProvider(null)
    setResult(null)
    setCalling(false)
  }, [open, numbers])

  // Local tanlanganda agentlarni yuklaymiz (onlayn birinchisi avto tanlanadi).
  useEffect(() => {
    if (!open || provider !== 'local' || agents !== null) return
    getCtiAgents()
      .then((list) => {
        setAgents(list)
        const online = list.find((a) => a.isOnline && a.isActive)
        setAgentId(online?.id ?? list[0]?.id ?? '')
      })
      .catch(() => setAgents([]))
  }, [open, provider, agents])

  const sortedAgents = useMemo(
    () => (agents ? [...agents].sort((a, b) => Number(b.isOnline) - Number(a.isOnline)) : []),
    [agents],
  )

  const callCloud = async () => {
    if (!number || calling) return
    setCalling(true)
    setResult(null)
    try {
      await originateCall(studentId ? { studentId, phoneNumber: number } : { phoneNumber: number })
      setResult({ ok: true, text: "Qo'ng'iroq boshlandi — operator telefoniga ulanadi (MoiZvonki)" })
    } catch (err: any) {
      setResult({ ok: false, text: err?.response?.data?.message || "Qo'ng'iroq qilib bo'lmadi (MoiZvonki sozlanmagan bo'lishi mumkin)" })
    } finally {
      setCalling(false)
    }
  }

  const callLocal = async () => {
    if (!number || !agentId || calling) return
    setCalling(true)
    setResult(null)
    try {
      const r = await dialCtiAgent(agentId, number)
      setResult(
        r.delivered
          ? { ok: true, text: "Buyruq agent telefoniga yuborildi — telefon terilmoqda" }
          : { ok: false, text: "Agent oflayn — push yuborildi, yetkazilmadi. Qayta urining" },
      )
    } catch (err: any) {
      setResult({ ok: false, text: err?.response?.data?.message || "Qo'ng'iroq qilishda xato" })
    } finally {
      setCalling(false)
    }
  }

  return (
    <Modal
      open={open}
      onClose={onClose}
      size="sm"
      title={title ? `Qo'ng'iroq qilish — ${title}` : "Qo'ng'iroq qilish"}
    >
      <div className="space-y-3">
        {/* ---- 1-bosqich: raqam tanlash (bir nechta bo'lsa) ---- */}
        {numbers.length === 0 ? (
          <p className="py-4 text-center text-sm text-slate-400">Telefon raqami kiritilmagan.</p>
        ) : number === null ? (
          <div className="space-y-2">
            <p className="text-xs font-semibold uppercase tracking-wide text-slate-400">
              Qaysi raqamga qo'ng'iroq qilamiz?
            </p>
            {numbers.map((n) => (
              <button
                key={n.label + n.number}
                type="button"
                onClick={() => setNumber(n.number)}
                className="flex w-full items-center gap-3 rounded-xl border border-slate-200 bg-white p-3 text-left transition-colors hover:border-brand-300 hover:bg-brand-50"
              >
                <div className="flex h-9 w-9 flex-shrink-0 items-center justify-center rounded-full bg-slate-100 text-slate-500">
                  <User className="h-4 w-4" />
                </div>
                <div className="min-w-0 flex-1">
                  <div className="text-sm font-semibold text-slate-700">{n.label}</div>
                  <div className="font-mono text-xs text-slate-500">{n.number}</div>
                </div>
                <Phone className="h-4 w-4 flex-shrink-0 text-slate-300" />
              </button>
            ))}
          </div>
        ) : (
          <>
            {/* Tanlangan raqam (bir nechta bo'lsa orqaga qaytish mumkin) */}
            <div className="flex items-center justify-between rounded-lg bg-slate-50 px-3 py-2">
              <span className="font-mono text-sm font-semibold text-slate-700">{number}</span>
              {numbers.length > 1 && (
                <button
                  type="button"
                  onClick={() => { setNumber(null); setProvider(null); setResult(null) }}
                  className="text-xs font-semibold text-brand-600 hover:underline"
                >
                  Boshqa raqam
                </button>
              )}
            </div>

            {/* ---- 2-bosqich: provayder tanlash ---- */}
            <p className="text-xs font-semibold uppercase tracking-wide text-slate-400">
              Qayerdan qo'ng'iroq qilamiz?
            </p>
            <div className="grid grid-cols-2 gap-2">
              <button
                type="button"
                onClick={() => { setProvider('cloud'); setResult(null) }}
                className={cn(
                  'rounded-xl border p-3 text-left transition-all',
                  provider === 'cloud'
                    ? 'border-brand-400 bg-brand-50 ring-2 ring-brand-100'
                    : 'border-slate-200 bg-white hover:border-slate-300 hover:bg-slate-50',
                )}
              >
                <Cloud className={cn('h-5 w-5', provider === 'cloud' ? 'text-brand-600' : 'text-slate-400')} />
                <div className={cn('mt-1.5 text-sm font-semibold', provider === 'cloud' ? 'text-brand-700' : 'text-slate-700')}>
                  Bulut (MoiZvonki)
                </div>
                <p className="mt-0.5 text-xs text-slate-500">Operator telefoni orqali</p>
              </button>
              <button
                type="button"
                onClick={() => { setProvider('local'); setResult(null) }}
                className={cn(
                  'rounded-xl border p-3 text-left transition-all',
                  provider === 'local'
                    ? 'border-brand-400 bg-brand-50 ring-2 ring-brand-100'
                    : 'border-slate-200 bg-white hover:border-slate-300 hover:bg-slate-50',
                )}
              >
                <Smartphone className={cn('h-5 w-5', provider === 'local' ? 'text-brand-600' : 'text-slate-400')} />
                <div className={cn('mt-1.5 text-sm font-semibold', provider === 'local' ? 'text-brand-700' : 'text-slate-700')}>
                  Local Call
                </div>
                <p className="mt-0.5 text-xs text-slate-500">Agent-telefon orqali</p>
              </button>
            </div>

            {/* ---- Local: agent tanlash ---- */}
            {provider === 'local' && (
              <div>
                <label className="mb-1 block text-sm font-medium text-slate-600">Qaysi telefon (agent)</label>
                {agents === null ? (
                  <div className="flex items-center gap-2 py-2 text-sm text-slate-400">
                    <Loader2 className="h-4 w-4 animate-spin" /> Agentlar yuklanmoqda...
                  </div>
                ) : agents.length === 0 ? (
                  <p className="text-sm text-amber-600">Agentlar yo'q — avval Local Call bo'limida agent qo'shing.</p>
                ) : (
                  <select value={agentId} onChange={(e) => setAgentId(e.target.value)} className={control}>
                    {sortedAgents.map((a) => (
                      <option key={a.id} value={a.id}>
                        {a.isOnline ? '● ' : ''}{a.displayName} {a.isOnline ? '(onlayn)' : '(oflayn)'}
                      </option>
                    ))}
                  </select>
                )}
              </div>
            )}

            {/* ---- Natija ---- */}
            {result && (
              <div className={cn(
                'rounded-lg px-3 py-2 text-sm font-medium',
                result.ok ? 'bg-emerald-50 text-emerald-700' : 'bg-amber-50 text-amber-700',
              )}>
                {result.text}
              </div>
            )}

            <div className="flex justify-end gap-2 pt-1">
              <Button variant="secondary" onClick={onClose}>Yopish</Button>
              <Button
                onClick={provider === 'local' ? callLocal : callCloud}
                disabled={!provider || calling || (provider === 'local' && !agentId)}
              >
                {calling ? <Loader2 className="h-4 w-4 animate-spin" /> : <Phone className="h-4 w-4" />}
                Qo'ng'iroq qil
              </Button>
            </div>
          </>
        )}
      </div>
    </Modal>
  )
}
