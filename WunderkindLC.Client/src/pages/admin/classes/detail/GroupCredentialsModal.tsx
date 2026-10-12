import { useEffect, useState } from 'react'
import { IconCheck, IconCopy, IconDownload, IconEye, IconEyeOff, IconKey, IconRefresh } from '@tabler/icons-react'
import {
  downloadGroupCredentials,
  ensureGroupCredentials,
  getGroupCredentials,
  type GroupCredentials,
  type GroupCredentialRow,
} from '@/api/services/groupCredentials'
import { Button } from '@/components/ui/Button'
import { Modal } from '@/components/ui/Modal'
import { membershipBadge } from '@/lib/groupDisplay'
import { maskPassword } from '@/lib/studentCreds'
import { apiErrorMessage, cn, copyText } from '@/lib/utils'

/**
 * Guruh → «O'quvchilar» → «Login/parollar» tugmasi + oynasi (CRM Phase 6, `auth-sessions.md` §6).
 * Chaqiruvchi faqat `canStudentCreds` o'tganda chizadi (server ham `HasFullAccess` bilan 403 beradi).
 */
export function GroupCredentialsButton({ groupId, isSuperAdmin }: { groupId: string; isSuperAdmin: boolean }) {
  // Har ochilishda yangi `key` — oyna ichidagi holat (ro'yxat, xato) qaytadan boshlanadi.
  const [openKey, setOpenKey] = useState(0)
  const [open, setOpen] = useState(false)
  return (
    <>
      <Button
        variant="secondary"
        onClick={() => {
          setOpenKey((k) => k + 1)
          setOpen(true)
        }}
      >
        <IconKey className="h-4 w-4" /> Login/parollar
      </Button>
      <Modal open={open} onClose={() => setOpen(false)} title="Login/parollar" size="xl">
        {open && <GroupCredentialsBody key={openKey} groupId={groupId} isSuperAdmin={isSuperAdmin} />}
      </Modal>
    </>
  )
}

type Load = { kind: 'loading' } | { kind: 'ok'; data: GroupCredentials } | { kind: 'error'; message: string }

function GroupCredentialsBody({ groupId, isSuperAdmin }: { groupId: string; isSuperAdmin: boolean }) {
  const [load, setLoad] = useState<Load>({ kind: 'loading' })
  const [show, setShow] = useState(false)
  const [busy, setBusy] = useState<'ensure' | 'export' | null>(null)
  const [error, setError] = useState('')
  const [notice, setNotice] = useState('')

  useEffect(() => {
    let alive = true
    getGroupCredentials(groupId)
      .then((data) => {
        if (alive) setLoad({ kind: 'ok', data })
      })
      .catch((e) => {
        if (alive) setLoad({ kind: 'error', message: apiErrorMessage(e, "Login/parollarni yuklab bo'lmadi") })
      })
    return () => {
      alive = false
    }
  }, [groupId])

  if (load.kind === 'loading') return <p className="py-6 text-center text-sm text-slate-400">Yuklanmoqda...</p>
  if (load.kind === 'error') return <p className="py-6 text-center text-sm text-red-600">{load.message}</p>

  const { data } = load
  const missing = data.rows.filter((r) => !r.hasAccount || !r.known).length

  const ensure = async () => {
    if (busy || missing === 0 || !data.vaultConfigured) return
    if (!confirm(`${missing} ta o'quvchiga yangi parol yaratilsinmi? Ularning eski parollari ishlamay qoladi.`)) return
    setBusy('ensure')
    setError('')
    setNotice('')
    try {
      const res = await ensureGroupCredentials(groupId)
      setLoad({ kind: 'ok', data: res })
      setShow(true)
      setNotice(`${res.created} ta yangi akkaunt, ${res.regenerated} ta yangi parol yaratildi`)
    } catch (e) {
      setError(apiErrorMessage(e, "Yangi parollarni yaratib bo'lmadi"))
    } finally {
      setBusy(null)
    }
  }

  const exportXlsx = async () => {
    if (busy) return
    setBusy('export')
    setError('')
    try {
      await downloadGroupCredentials(groupId)
    } catch (e) {
      setError(apiErrorMessage(e, "Excel faylni yuklab bo'lmadi"))
    } finally {
      setBusy(null)
    }
  }

  return (
    <div className="space-y-3">
      {!data.vaultConfigured && (
        <p className="rounded-lg bg-amber-50 px-3 py-2 text-xs text-amber-800">
          {isSuperAdmin
            ? "Parollarni saqlash o'chiq: .env da CREDENTIAL_VAULT_KEY yo'q"
            : "Parollarni saqlash o'chiq — o'quvchi kirgandan keyin parol ko'rinmaydi"}
        </p>
      )}

      <div className="flex flex-wrap items-center gap-2">
        <Button variant="secondary" disabled={!!busy} onClick={exportXlsx}>
          <IconDownload className="h-4 w-4" /> {busy === 'export' ? 'Yuklanmoqda...' : 'Excel yuklab olish'}
        </Button>
        {/* Seyf o'chiq bo'lsa yangi parol SAQLANMAYDI (server 400 beradi) — tugma ko'rsatilmaydi. */}
        {missing > 0 && data.vaultConfigured && (
          <Button disabled={!!busy} onClick={ensure}>
            <IconRefresh className={cn('h-4 w-4', busy === 'ensure' && 'animate-spin')} />
            Paroli noma'lumlarga yangi parol yaratish ({missing})
          </Button>
        )}
        <Button variant="ghost" className="ml-auto" onClick={() => setShow((v) => !v)}>
          {show ? <IconEyeOff className="h-4 w-4" /> : <IconEye className="h-4 w-4" />}
          {show ? 'Parollarni yashirish' : "Parollarni ko'rsatish"}
        </Button>
      </div>

      {error && <p className="rounded-lg border border-red-200 bg-red-50 px-3 py-2 text-sm text-red-700">{error}</p>}
      {notice && <p className="rounded-lg bg-emerald-50 px-3 py-2 text-sm text-emerald-700">{notice}</p>}

      {data.rows.length === 0 ? (
        <p className="py-6 text-center text-sm text-slate-400">Guruhda o'quvchi yo'q</p>
      ) : (
        <div className="max-h-[60vh] overflow-auto rounded-lg border border-slate-200">
          <table className="w-full text-sm">
            <thead className="sticky top-0 bg-slate-50 text-left text-xs text-slate-500">
              <tr>
                <th className="px-3 py-2">№</th>
                <th className="px-3 py-2">F.I.SH.</th>
                <th className="px-3 py-2">Login</th>
                <th className="px-3 py-2">Parol</th>
                <th className="px-3 py-2">Holat</th>
              </tr>
            </thead>
            <tbody className="divide-y divide-slate-100">
              {data.rows.map((r, i) => (
                <Row key={r.studentId} n={i + 1} row={r} show={show} />
              ))}
            </tbody>
          </table>
        </div>
      )}
    </div>
  )
}

function Row({ n, row, show }: { n: number; row: GroupCredentialRow; show: boolean }) {
  const badge = membershipBadge(row.status, row.yearFreeze, true)
  return (
    <tr>
      <td className="px-3 py-2 text-slate-400">{n}</td>
      <td className="px-3 py-2 font-medium text-slate-800">{row.fullName}</td>
      <td className="px-3 py-2">
        {row.hasAccount ? <CopyCell value={row.login} display={row.login} /> : <span className="text-xs text-slate-400">akkaunt yo'q</span>}
      </td>
      <td className="px-3 py-2">
        {row.hasAccount && row.known && row.password ? (
          <CopyCell value={row.password} display={show ? row.password : maskPassword(row.password)} />
        ) : row.hasAccount ? (
          <span className="text-xs text-slate-400">saqlanmagan</span>
        ) : (
          '—'
        )}
      </td>
      <td className="px-3 py-2">
        <span className={cn('whitespace-nowrap rounded-full px-2 py-0.5 text-[11px] font-semibold', badge.cls)}>{badge.label}</span>
      </td>
    </tr>
  )
}

function CopyCell({ value, display }: { value: string; display: string }) {
  const [copied, setCopied] = useState(false)
  const onCopy = async () => {
    if (await copyText(value)) {
      setCopied(true)
      setTimeout(() => setCopied(false), 1500)
    }
  }
  return (
    <span className="inline-flex items-center gap-1">
      <code className="text-[13px] text-slate-800">{display}</code>
      <button
        type="button"
        onClick={onCopy}
        title="Nusxalash"
        className="rounded p-0.5 text-slate-400 transition-colors hover:bg-slate-100 hover:text-slate-700"
      >
        {copied ? <IconCheck className="h-3.5 w-3.5 text-emerald-600" /> : <IconCopy className="h-3.5 w-3.5" />}
      </button>
    </span>
  )
}
