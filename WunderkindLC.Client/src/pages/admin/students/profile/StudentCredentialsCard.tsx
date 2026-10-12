import { useEffect, useState, type ReactNode } from 'react'
import { Check, Copy, Eye, EyeOff, KeyRound, RefreshCw, TriangleAlert } from 'lucide-react'
import type { Credentials } from '@/types'
import { getStudentCredentials, resetStudentPassword } from '@/api/services/students'
import { Card } from '@/components/ui/Card'
import { apiErrorMessage, copyText, formatDateTime } from '@/lib/utils'
import { maskPassword, passwordSourceLabel } from '@/lib/studentCreds'

type State =
  | { kind: 'loading' }
  | { kind: 'ok'; value: Credentials }
  | { kind: 'error'; message: string }

/**
 * «Kirish ma'lumotlari» — o'quvchining logini va JORIY paroli (CRM Phase 6, `auth-sessions.md` §6).
 *
 * Parol seyfdan keladi: o'quvchi kirgandan keyin ham, kabinetda o'zi almashtirgandan keyin ham ko'rinadi.
 * Eski akkaunt (seyfdan oldin kirib bo'lgan) — «Parol saqlanmagan», «Yangi parol» bilan yaratiladi.
 *
 * ⚠️ Faqat `canStudentCreds` o'tganda chiziladi (chaqiruvchi tekshiradi) — server ham AYNAN shu
 * `HasFullAccess` bilan 403 beradi. O'qituvchiga hech qachon ko'rsatilmaydi.
 */
export function StudentCredentialsCard({ studentId, isSuperAdmin }: { studentId: string; isSuperAdmin: boolean }) {
  const [state, setState] = useState<State>({ kind: 'loading' })
  const [show, setShow] = useState(false)
  const [busy, setBusy] = useState(false)
  const [resetError, setResetError] = useState('')

  useEffect(() => {
    let alive = true
    getStudentCredentials(studentId)
      .then((c) => {
        if (alive) setState({ kind: 'ok', value: c })
      })
      .catch((e) => {
        if (alive) setState({ kind: 'error', message: apiErrorMessage(e, "Kirish ma'lumotlarini yuklab bo'lmadi") })
      })
    return () => {
      alive = false
    }
  }, [studentId])

  const reset = async () => {
    if (busy) return
    if (!confirm("Yangi parol yaratilsinmi? Eski parol ishlamay qoladi va o'quvchi barcha qurilmalardan chiqariladi.")) return
    setBusy(true)
    setResetError('')
    try {
      const c = await resetStudentPassword(studentId)
      setState({ kind: 'ok', value: c })
      setShow(true)
    } catch (e) {
      // JIM yiqilmaydi — xodim "parol yaratildi" deb o'ylab ketmasin (error-visibility.md).
      setResetError(apiErrorMessage(e, "Yangi parol yaratib bo'lmadi"))
    } finally {
      setBusy(false)
    }
  }

  const c = state.kind === 'ok' ? state.value : null
  const known = !!c && c.known !== false && !!c.password
  const source = c ? passwordSourceLabel(c.source) : ''

  return (
    <Card>
      <div className="mb-2 flex items-center gap-2">
        <KeyRound className="h-5 w-5 text-brand-600" />
        <h2 className="font-semibold text-slate-800">Kirish ma'lumotlari</h2>
      </div>

      {state.kind === 'loading' && <p className="text-sm text-slate-400">Yuklanmoqda...</p>}
      {state.kind === 'error' && <p className="text-sm text-red-600">{state.message}</p>}

      {c && (
        <div className="space-y-2">
          {c.vaultConfigured === false && (
            <p className="flex items-start gap-1.5 rounded-lg bg-amber-50 px-3 py-2 text-xs text-amber-800">
              <TriangleAlert className="mt-0.5 h-3.5 w-3.5 shrink-0" />
              {isSuperAdmin
                ? "Parollarni saqlash o'chiq: .env da CREDENTIAL_VAULT_KEY yo'q"
                : "Parollarni saqlash o'chiq — o'quvchi kirgandan keyin parol ko'rinmaydi"}
            </p>
          )}
          <CopyRow label="Login" value={c.login} display={c.login || '—'} />
          {known ? (
            <CopyRow
              label="Parol"
              value={c.password}
              display={show ? c.password : maskPassword(c.password)}
              extra={
                <button
                  type="button"
                  onClick={() => setShow((v) => !v)}
                  title={show ? 'Yashirish' : "Ko'rsatish"}
                  className="rounded p-1 text-slate-400 transition-colors hover:bg-slate-100 hover:text-slate-700"
                >
                  {show ? <EyeOff className="h-4 w-4" /> : <Eye className="h-4 w-4" />}
                </button>
              }
            />
          ) : (
            <p className="rounded-lg bg-slate-50 px-3 py-2 text-xs text-slate-500">
              Parol saqlanmagan — «Yangi parol» bilan yarating
            </p>
          )}
          {known && (source || c.updatedAt) && (
            <p className="px-1 text-[11px] text-slate-400">
              {source}
              {source && c.updatedAt ? ' · ' : ''}
              {c.updatedAt ? formatDateTime(c.updatedAt) : ''}
            </p>
          )}
          <button
            type="button"
            onClick={reset}
            disabled={busy}
            className="inline-flex items-center gap-2 rounded-lg border border-brand-200 bg-white px-3 py-1.5 text-sm font-medium text-brand-700 transition-colors hover:bg-brand-50 disabled:opacity-60"
          >
            <RefreshCw className={`h-4 w-4 ${busy ? 'animate-spin' : ''}`} />
            {busy ? 'Yaratilmoqda...' : 'Yangi parol'}
          </button>
          {resetError && <p className="px-1 text-xs font-medium text-red-600">{resetError}</p>}
        </div>
      )}
    </Card>
  )
}

function CopyRow({
  label,
  value,
  display,
  extra,
}: {
  label: string
  value: string
  display: string
  extra?: ReactNode
}) {
  const [copied, setCopied] = useState(false)
  const onCopy = async () => {
    if (await copyText(value)) {
      setCopied(true)
      setTimeout(() => setCopied(false), 1500)
    }
  }
  return (
    <div className="flex items-center justify-between gap-3 rounded-lg bg-slate-50 px-3 py-2">
      <span className="text-xs uppercase tracking-wide text-slate-400">{label}</span>
      <div className="flex min-w-0 items-center gap-1">
        <code className="truncate text-sm font-medium text-slate-800">{display}</code>
        {extra}
        {value && (
          <button
            type="button"
            onClick={onCopy}
            title="Nusxalash"
            className="rounded p-1 text-slate-400 transition-colors hover:bg-slate-100 hover:text-slate-700"
          >
            {copied ? <Check className="h-4 w-4 text-emerald-600" /> : <Copy className="h-4 w-4" />}
          </button>
        )}
      </div>
    </div>
  )
}
