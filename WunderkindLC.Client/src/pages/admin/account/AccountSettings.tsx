import { useState } from 'react'
import type { FormEvent } from 'react'
import axios from 'axios'
import { Check } from 'lucide-react'
import { useAuth } from '@/context/auth-context'
import { updateAccount } from '@/api/services/auth'
import { Card } from '@/components/ui/Card'
import { Button } from '@/components/ui/Button'
import { Input } from '@/components/ui/Input'
import { PhoneInput } from '@/components/ui/PhoneInput'

function errorMessage(err: unknown): string {
  if (axios.isAxiosError(err)) {
    return (err.response?.data as { message?: string })?.message ?? 'Saqlashda xatolik yuz berdi'
  }
  if (err instanceof Error) return err.message
  return 'Saqlashda xatolik yuz berdi'
}

/** Administrator o'z login (email) va parolini o'zgartiradigan sozlama bo'limi. */
export function AccountSettings() {
  const { user, updateUser } = useAuth()
  const [login, setLogin] = useState(user?.email ?? '')
  const [phone, setPhone] = useState(user?.phone ?? '')
  const [currentPassword, setCurrentPassword] = useState('')
  const [newPassword, setNewPassword] = useState('')
  const [confirmPassword, setConfirmPassword] = useState('')
  const [status, setStatus] = useState<'idle' | 'saving' | 'saved'>('idle')
  const [error, setError] = useState('')

  const onSubmit = async (e: FormEvent) => {
    e.preventDefault()
    setError('')

    if (!login.trim()) {
      setError("Login bo'sh bo'lmasligi kerak")
      return
    }
    if (!currentPassword) {
      setError('Joriy parolni kiriting')
      return
    }
    if (newPassword) {
      if (newPassword.length < 4) {
        setError("Yangi parol kamida 4 belgidan iborat bo'lsin")
        return
      }
      if (newPassword !== confirmPassword) {
        setError('Yangi parollar bir-biriga mos kelmadi')
        return
      }
    }

    setStatus('saving')
    try {
      const updated = await updateAccount({
        email: login.trim(),
        currentPassword,
        newPassword: newPassword || undefined,
        phone: phone.trim(),
      })
      updateUser(updated)
      setCurrentPassword('')
      setNewPassword('')
      setConfirmPassword('')
      setStatus('saved')
      setTimeout(() => setStatus('idle'), 2000)
    } catch (err) {
      setStatus('idle')
      setError(errorMessage(err))
    }
  }

  return (
    <Card
      title="Administrator akkaunti"
      sub="Tizimga kirish login va parolingizni shu yerdan o'zgartirasiz. Login o'zgarsa, keyingi safar yangi login bilan kirasiz (qaytadan kirish shart emas)."
    >
      <form onSubmit={onSubmit} className="max-w-md space-y-4">
        <Input
          label="Login"
          type="text"
          autoComplete="username"
          placeholder="Login"
          value={login}
          onChange={(e) => setLogin(e.target.value)}
          required
        />
        <PhoneInput
          label="Telefon (Telegram bot — yangi lid xabarnomasi)"
          value={phone}
          onChange={setPhone}
        />
        <p className="-mt-2 text-xs text-slate-400">
          Shu raqamni Telegram botga yuborib ro'yxatdan o'tsangiz, yangi lid tushganda botdan xabar olasiz.
        </p>
        <Input
          label="Joriy parol"
          type="password"
          autoComplete="current-password"
          placeholder="parol"
          value={currentPassword}
          onChange={(e) => setCurrentPassword(e.target.value)}
          required
        />

        <div className="border-t border-slate-100 pt-4">
          <p className="mb-3 text-sm font-medium text-slate-600">Parolni o'zgartirish (ixtiyoriy)</p>
          <div className="space-y-4">
            <Input
              label="Yangi parol"
              type="password"
              autoComplete="new-password"
              placeholder="parol"
              value={newPassword}
              onChange={(e) => setNewPassword(e.target.value)}
            />
            <Input
              label="Yangi parolni tasdiqlang"
              type="password"
              autoComplete="new-password"
              placeholder="parol"
              value={confirmPassword}
              onChange={(e) => setConfirmPassword(e.target.value)}
            />
          </div>
        </div>

        {error && <p className="rounded-lg bg-red-50 px-3 py-2 text-sm text-red-600">{error}</p>}

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
