import { useEffect, useState } from 'react'
import type { FormEvent } from 'react'
import { Link, Navigate, useLocation, useNavigate } from 'react-router-dom'
import { Button } from '@/components/ui/Button'
import { Input } from '@/components/ui/Input'
import { useAuth } from '@/context/auth-context'
import { homeFor } from '@/config/navigation'
import { getPublicBrand, type PublicBrand } from '@/api/services/settings'
import { apiErrorMessage, cn } from '@/lib/utils'
import type { User } from '@/types'
import posthog from '@/lib/posthog'

interface LocationState {
  from?: string
}

type LoginMode = 'password' | 'code'

export function LoginPage() {
  const { isAuthenticated, user, login, loginWithCode } = useAuth()
  const navigate = useNavigate()
  const location = useLocation()

  const [mode, setMode] = useState<LoginMode>('password')
  const [email, setEmail] = useState('')
  const [password, setPassword] = useState('')
  const [code, setCode] = useState('')
  const [error, setError] = useState('')
  const [loading, setLoading] = useState(false)
  const [brand, setBrand] = useState<PublicBrand>({ name: '', logoUrl: '', phone: '' })

  useEffect(() => {
    getPublicBrand()
      .then(setBrand)
      .catch(() => {})
  }, [])

  // Allaqachon kirgan bo'lsa — o'z bo'limiga
  if (isAuthenticated && user) {
    return <Navigate to={homeFor(user)} replace />
  }

  const afterLogin = (u: User) => {
    const from = (location.state as LocationState | null)?.from
    navigate(from ?? homeFor(u), { replace: true })
  }

  const handleSubmit = async (e: FormEvent) => {
    e.preventDefault()
    setError('')
    setLoading(true)
    try {
      const u = await login(email, password)
      posthog.capture('user_logged_in', { login_method: 'password' })
      afterLogin(u)
    } catch (err) {
      setError(apiErrorMessage(err, "Kirishda xatolik yuz berdi"))
    } finally {
      setLoading(false)
    }
  }

  const handleCodeSubmit = async (e: FormEvent) => {
    e.preventDefault()
    setError('')
    setLoading(true)
    try {
      const u = await loginWithCode(code.trim())
      posthog.capture('user_logged_in', { login_method: 'one_time_code' })
      afterLogin(u)
    } catch (err) {
      setError(apiErrorMessage(err, 'Kod noto\'g\'ri yoki muddati o\'tgan'))
    } finally {
      setLoading(false)
    }
  }

  return (
    <div className="relative flex min-h-screen items-center justify-center overflow-hidden bg-slate-50 px-4">
      {/* Yumshoq fon nuri — premium his */}
      <div className="pointer-events-none absolute -top-40 left-1/2 h-[480px] w-[480px] -translate-x-1/2 rounded-full bg-brand-200/40 blur-3xl" />
      <div className="pointer-events-none absolute -bottom-40 right-0 h-[360px] w-[360px] rounded-full bg-brand-100/50 blur-3xl" />

      <div className="relative w-full max-w-sm rounded-2xl border border-slate-200 bg-white p-8 shadow-[var(--shadow-2,0_10px_40px_-12px_rgba(0,0,0,0.18))]">
        <div className="mb-7 flex flex-col items-center gap-4 text-center">
          {brand.logoUrl ? (
            <img
              src={brand.logoUrl}
              alt="Logo"
              className="h-14 w-14 rounded-2xl object-contain"
            />
          ) : (
            <div className="flex h-14 w-14 items-center justify-center rounded-2xl bg-brand-600 text-lg font-bold tracking-tight text-white shadow-lg">
              IC
            </div>
          )}
          <div>
            <h1 className="text-xl font-bold tracking-tight text-slate-800">
              {brand.name || 'WunderkindLC'}
            </h1>
            <p className="mt-1 text-sm text-slate-400">Tizimga kirish</p>
          </div>
        </div>

        <div className="mb-5 grid grid-cols-2 gap-1 rounded-xl bg-slate-100 p-1 text-sm font-medium">
          {(
            [
              ['password', 'Parol bilan'],
              ['code', 'Kod bilan'],
            ] as const
          ).map(([m, label]) => (
            <button
              key={m}
              type="button"
              onClick={() => {
                setMode(m)
                setError('')
              }}
              className={cn(
                'rounded-lg py-1.5 transition-colors',
                mode === m ? 'bg-white text-brand-700 shadow-sm' : 'text-slate-500 hover:text-slate-700',
              )}
            >
              {label}
            </button>
          ))}
        </div>

        {mode === 'password' ? (
          <form onSubmit={handleSubmit} className="space-y-4">
            <Input
              label="Login"
              type="text"
              autoComplete="username"
              placeholder="Login"
              value={email}
              onChange={(e) => setEmail(e.target.value)}
              required
            />
            <Input
              label="Parol"
              type="password"
              autoComplete="current-password"
              placeholder="parol"
              value={password}
              onChange={(e) => setPassword(e.target.value)}
              required
            />

            {error && (
              <p className="rounded-lg bg-red-50 px-3 py-2 text-sm text-red-600">{error}</p>
            )}

            <Button type="submit" disabled={loading} className="w-full">
              {loading ? 'Kirilmoqda…' : 'Kirish'}
            </Button>
          </form>
        ) : (
          <form onSubmit={handleCodeSubmit} className="space-y-4">
            <p className="text-sm text-slate-400">
              Telegram botda «🔑 Yangi kod olish» tugmasini bosing va olingan 8 belgili kodni kiriting.
            </p>
            <Input
              label="Kod"
              type="text"
              autoComplete="one-time-code"
              placeholder="masalan: 7K9M2XQ4"
              value={code}
              onChange={(e) => setCode(e.target.value.toUpperCase())}
              maxLength={8}
              autoFocus
              required
            />

            {error && (
              <p className="rounded-lg bg-red-50 px-3 py-2 text-sm text-red-600">{error}</p>
            )}

            <Button type="submit" disabled={loading} className="w-full">
              {loading ? 'Kirilmoqda…' : 'Kirish'}
            </Button>
          </form>
        )}

        <p className="mt-6 text-center text-xs text-slate-400">
          <Link to="/privacy" className="hover:text-brand-600 hover:underline">
            Maxfiylik siyosati
          </Link>
        </p>
      </div>
    </div>
  )
}
