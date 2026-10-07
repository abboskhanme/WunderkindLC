import { useEffect, useRef, useState } from 'react'
import type { FormEvent } from 'react'
import { Archive, ArrowLeftRight, Crown, Pencil, Plus, RotateCcw, Wallet } from 'lucide-react'
import {
  archiveCashbox,
  createCashbox,
  getCashboxes,
  restoreCashbox,
  updateCashbox,
  type Cashbox,
} from '@/api/services/cashboxes'
import { getStaff } from '@/api/services/staff'
import type { Staff } from '@/types'
import { Card } from '@/components/ui/Card'
import { Button } from '@/components/ui/Button'
import { RightDrawer, DrawerActions, DrawerField, DrawerInput, DrawerSelect } from '@/components/ui/RightDrawer'
import { apiErrorMessage, balanceTextCls, cn, formatMoney } from '@/lib/utils'
import { toast } from '@/lib/toast'
import { CashboxTransferForm, CashboxTransfersCard } from './CashboxTransfers'

/**
 * Moliya → «Kassalar»: kassa KARTALARI (qoldiq + davr ichidagi kirim/chiqim), `docs/SPEC-kassalar.md`.
 * Yaratish/tahrirlash/arxivlash va KASSADAN KASSAGA KO'CHIRISH — FAQAT superadmin (`isSuperAdmin`;
 * server ham 403 qaytaradi).
 *
 * Qoldiq — BUTUN tarix bo'yicha (kassada hozir qancha pul), kirim/chiqim esa sahifadagi davr bo'yicha.
 * Ko'chirishlar kirim/chiqimga QO'SHILMAYDI — kartada alohida qator (`billing.md`).
 */
export function CashboxesPanel({ from, to, isSuperAdmin }: { from: string; to: string; isSuperAdmin: boolean }) {
  const [boxes, setBoxes] = useState<Cashbox[]>([])
  const [loadError, setLoadError] = useState<string | null>(null)
  const [tick, setTick] = useState(0)
  const [showArchived, setShowArchived] = useState(false)
  const [confirmArchive, setConfirmArchive] = useState<string | null>(null)
  /** Ochiq forma: `null` — yopiq, `'new'` — yangi kassa, aks holda tahrirlanayotgan kassa. */
  const [editing, setEditing] = useState<Cashbox | 'new' | null>(null)
  /** Ko'chirish oynasi: `null` — yopiq; `fromId` — oldindan tanlangan "qayerdan" (kartadagi tugma). */
  const [transfer, setTransfer] = useState<{ fromId: string | null } | null>(null)
  const inFlight = useRef(false)

  useEffect(() => {
    let alive = true
    getCashboxes(from, to)
      .then((b) => {
        if (!alive) return
        setBoxes(b)
        setLoadError(null)
      })
      .catch((e) => alive && setLoadError(apiErrorMessage(e, "Kassalarni yuklab bo'lmadi")))
    return () => {
      alive = false
    }
  }, [from, to, tick])

  const active = boxes.filter((b) => !b.isArchived)
  const archived = boxes.filter((b) => b.isArchived)
  // Jami — ARXIVDAGILAR bilan: arxivlangan kassadagi pul jismonan bor (uni «Ko'chirish» bilan faol
  // kassaga olish mumkin) — tashlab yuborsak arxivlash bilan pul jamidan yo'qolgandek ko'rinardi.
  const total = boxes.reduce((a, b) => a + b.balance, 0)
  const archivedTotal = archived.reduce((a, b) => a + b.balance, 0)

  // Ro'yxat qatoridagi amal — xato `alert` bilan (`error-visibility.md` §2).
  const runRowAction = async (fn: () => Promise<void>, done: string, fallback: string) => {
    if (inFlight.current) return
    inFlight.current = true
    try {
      await fn()
      toast.success(done)
      setConfirmArchive(null)
      setTick((x) => x + 1)
    } catch (e) {
      alert(apiErrorMessage(e, fallback))
    } finally {
      inFlight.current = false
    }
  }

  return (
    <>
      <Card
        tight
        title="Kassalar"
        sub={
          `Qoldiq — kassada hozir turgan pul (butun tarix). Kirim/chiqim — tanlangan davr. Jami: ${formatMoney(total)}` +
          (archivedTotal !== 0 ? ` (shundan arxivdagi kassalarda ${formatMoney(archivedTotal)})` : '')
        }
        actions={
          isSuperAdmin ? (
            <>
              {boxes.length >= 2 && (
                <Button variant="secondary" onClick={() => setTransfer({ fromId: null })}>
                  <ArrowLeftRight className="h-4 w-4" /> Kassadan kassaga
                </Button>
              )}
              <Button onClick={() => setEditing('new')}>
                <Plus className="h-4 w-4" /> Yangi kassa qo'shish
              </Button>
            </>
          ) : undefined
        }
      >
        <div className="space-y-3 p-4">
          {loadError && <p className="text-sm text-red-600">{loadError}</p>}
          {!loadError && active.length === 0 && (
            <p className="py-6 text-center text-sm text-slate-400">Kassa yo'q.</p>
          )}
          <div className="grid gap-3 sm:grid-cols-2 xl:grid-cols-3">
            {active.map((b) => (
              <div key={b.id} className="rounded-xl border border-slate-200 bg-white p-4">
                <div className="flex items-start justify-between gap-2">
                  <div className="min-w-0">
                    <p className="flex items-center gap-1.5 truncate font-semibold text-slate-800">
                      <Wallet className="h-4 w-4 shrink-0 text-brand-600" />
                      <span className="truncate">{b.name}</span>
                      {b.isMain && (
                        <span className="shrink-0 rounded bg-brand-50 px-1.5 py-0.5 text-[10px] font-semibold text-brand-700">
                          ASOSIY
                        </span>
                      )}
                    </p>
                    <p className="mt-0.5 truncate text-xs text-slate-500">
                      {b.responsibleName ? (
                        <span className="inline-flex items-center gap-1">
                          <Crown className="h-3 w-3" /> {b.responsibleName}
                        </span>
                      ) : (
                        "Mas'ul biriktirilmagan"
                      )}
                    </p>
                  </div>
                  {isSuperAdmin && (
                    <div className="flex shrink-0 gap-1">
                      {active.length >= 2 && (
                        <button type="button" aria-label="Boshqa kassaga ko'chirish" title="Boshqa kassaga ko'chirish"
                          onClick={() => setTransfer({ fromId: b.id })}
                          className="rounded-md p-1.5 text-slate-400 hover:bg-slate-100 hover:text-brand-600">
                          <ArrowLeftRight className="h-4 w-4" />
                        </button>
                      )}
                      <button type="button" aria-label="Tahrirlash" onClick={() => setEditing(b)}
                        className="rounded-md p-1.5 text-slate-400 hover:bg-slate-100 hover:text-slate-700">
                        <Pencil className="h-4 w-4" />
                      </button>
                      {!b.isMain && (
                        <button type="button" aria-label="Arxivlash" onClick={() => setConfirmArchive(b.id)}
                          className="rounded-md p-1.5 text-slate-400 hover:bg-slate-100 hover:text-red-600">
                          <Archive className="h-4 w-4" />
                        </button>
                      )}
                    </div>
                  )}
                </div>
                <p className={cn('mt-3 font-mono text-xl font-semibold', balanceTextCls(b.balance))}>{formatMoney(b.balance)}</p>
                <p className="mt-1 text-xs text-slate-500">
                  Davrda: <span className="font-mono text-emerald-600">+{formatMoney(b.periodIncome)}</span>
                  {' · '}
                  <span className="font-mono text-red-600">−{formatMoney(b.periodExpense)}</span>
                </p>
                {/* Ko'chirishlar kirim/chiqim EMAS — alohida qator, faqat bo'lsa. */}
                {(b.periodTransferIn > 0 || b.periodTransferOut > 0) && (
                  <p className="mt-0.5 text-xs text-slate-500">
                    Ko'chirish: <span className="font-mono text-sky-700">+{formatMoney(b.periodTransferIn)}</span>
                    {' · '}
                    <span className="font-mono text-slate-600">−{formatMoney(b.periodTransferOut)}</span>
                  </p>
                )}
                {confirmArchive === b.id && (
                  <div className="mt-3 rounded-lg bg-red-50 p-2 text-xs text-red-700">
                    Arxivlansa yangi pul qabul qilmaydi (qoldig'i va tarixi saqlanadi).
                    {b.balance !== 0 && (
                      <b className="mt-1 block">
                        Kassada {formatMoney(b.balance)} qoldiq bor — u arxivdagi kassada qoladi. Avval uni
                        «Kassadan kassaga» bilan boshqa kassaga o'tkazish mumkin.
                      </b>
                    )}
                    <div className="mt-2 flex gap-2">
                      <button type="button"
                        onClick={() => void runRowAction(() => archiveCashbox(b.id), 'Kassa arxivlandi', "Arxivlab bo'lmadi")}
                        className="rounded-md bg-red-600 px-2 py-1 font-semibold text-white">
                        Ha, arxivlash
                      </button>
                      <button type="button" onClick={() => setConfirmArchive(null)}
                        className="rounded-md px-2 py-1 font-semibold text-red-700 hover:bg-red-100">
                        Yo'q
                      </button>
                    </div>
                  </div>
                )}
              </div>
            ))}
          </div>

          {archived.length > 0 && (
            <div>
              <button type="button" onClick={() => setShowArchived((v) => !v)}
                className="text-xs font-semibold text-slate-500 hover:underline">
                {showArchived ? 'Arxivni yashirish' : `Arxivlangan kassalar (${archived.length})`}
              </button>
              {showArchived && (
                <ul className="mt-2 divide-y divide-slate-100 rounded-lg border border-slate-200">
                  {archived.map((b) => (
                    <li key={b.id} className="flex items-center justify-between gap-2 px-3 py-2 text-sm">
                      <span className="min-w-0 truncate text-slate-600">
                        {b.name}
                        <span className={cn('ml-2 font-mono text-xs', balanceTextCls(b.balance))}>{formatMoney(b.balance)}</span>
                      </span>
                      {isSuperAdmin && (
                        <span className="flex shrink-0 items-center gap-1">
                          {/* Arxivdagi qoldiqni faol kassaga olish — arxivlangan kassa pul QABUL qilmaydi, beradi. */}
                          {b.balance > 0 && active.length > 0 && (
                            <button type="button" onClick={() => setTransfer({ fromId: b.id })}
                              className="inline-flex items-center gap-1 rounded-md px-2 py-1 text-xs font-semibold text-brand-600 hover:bg-brand-50">
                              <ArrowLeftRight className="h-3.5 w-3.5" /> Ko'chirish
                            </button>
                          )}
                          <button type="button"
                            onClick={() => void runRowAction(() => restoreCashbox(b.id), 'Kassa tiklandi', "Tiklab bo'lmadi")}
                            className="inline-flex items-center gap-1 rounded-md px-2 py-1 text-xs font-semibold text-brand-600 hover:bg-brand-50">
                            <RotateCcw className="h-3.5 w-3.5" /> Tiklash
                          </button>
                        </span>
                      )}
                    </li>
                  ))}
                </ul>
              )}
            </div>
          )}
        </div>
      </Card>

      <CashboxTransfersCard from={from} to={to} tick={tick} isSuperAdmin={isSuperAdmin}
        onChanged={() => setTick((x) => x + 1)} />

      {isSuperAdmin && transfer !== null && (
        <CashboxTransferForm
          key={transfer.fromId ?? 'any'}
          boxes={boxes}
          initialFromId={transfer.fromId}
          onClose={() => setTransfer(null)}
          onSaved={() => {
            setTransfer(null)
            setTick((x) => x + 1)
          }}
        />
      )}

      {isSuperAdmin && editing !== null && (
        <CashboxForm
          key={editing === 'new' ? 'new' : editing.id}
          box={editing === 'new' ? null : editing}
          onClose={() => setEditing(null)}
          onSaved={() => {
            setEditing(null)
            setTick((x) => x + 1)
          }}
        />
      )}
    </>
  )
}

/** Kassa yaratish/tahrirlash (superadmin). Xato forma ichida chiqadi, kiritilgan qiymat qoladi. */
function CashboxForm({ box, onClose, onSaved }: { box: Cashbox | null; onClose: () => void; onSaved: () => void }) {
  const [name, setName] = useState(box?.name ?? '')
  const [responsibleId, setResponsibleId] = useState(box?.responsibleUserId ?? '')
  const [staff, setStaff] = useState<Staff[]>([])
  const [staffError, setStaffError] = useState<string | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [saving, setSaving] = useState(false)
  const inFlight = useRef(false)

  useEffect(() => {
    let alive = true
    getStaff()
      .then((s) => alive && setStaff(s.filter((x) => !x.role || ['staff', 'admin', 'superadmin'].includes(x.role))))
      .catch((e) => alive && setStaffError(apiErrorMessage(e, "Xodimlar ro'yxatini yuklab bo'lmadi")))
    return () => {
      alive = false
    }
  }, [])

  const submit = async (e: FormEvent) => {
    e.preventDefault()
    if (inFlight.current || !name.trim()) return
    inFlight.current = true
    setSaving(true)
    setError(null)
    try {
      const payload = { name: name.trim(), responsibleUserId: responsibleId || null }
      if (box) await updateCashbox(box.id, payload)
      else await createCashbox(payload)
      toast.success(box ? 'Kassa saqlandi' : 'Kassa yaratildi', payload.name)
      onSaved()
    } catch (err) {
      setError(apiErrorMessage(err, "Kassani saqlab bo'lmadi"))
    } finally {
      inFlight.current = false
      setSaving(false)
    }
  }

  // Mas'ul ro'yxatda bo'lmasa ham (akkaunt o'chgan) tanlovda o'z nomi bilan tursin.
  const missingResponsible =
    box?.responsibleUserId && !staff.some((s) => s.id === box.responsibleUserId) ? box : null

  return (
    <RightDrawer
      open
      onClose={onClose}
      title={box ? 'Kassani tahrirlash' : 'Yangi kassa'}
      footer={
        <DrawerActions onBack={onClose}>
          <Button type="submit" form="cashbox-form" disabled={saving || !name.trim()}>
            {saving ? 'Saqlanmoqda...' : 'Saqlash'}
          </Button>
        </DrawerActions>
      }
    >
      <form id="cashbox-form" onSubmit={(e) => void submit(e)} className="space-y-4">
        {error && <div className="rounded-lg border border-red-200 bg-red-50 px-3 py-2 text-sm text-red-700">{error}</div>}
        <DrawerField label="Kassa nomi" required>
          <DrawerInput value={name} maxLength={100} autoFocus placeholder="Masalan: Naqd kassa"
            onChange={(e) => setName(e.target.value)} />
        </DrawerField>
        <DrawerField label="Mas'ul xodim" hint="U kiritgan to'lovlar avtomatik shu kassaga tushadi (to'lov oynasida almashtirsa bo'ladi).">
          <DrawerSelect value={responsibleId} onChange={(e) => setResponsibleId(e.target.value)}>
            <option value="">— mas'ulsiz —</option>
            {missingResponsible && (
              <option value={missingResponsible.responsibleUserId ?? ''}>{missingResponsible.responsibleName}</option>
            )}
            {staff.map((s) => (
              <option key={s.id} value={s.id}>
                {s.fullName}
                {s.position ? ` — ${s.position}` : ''}
              </option>
            ))}
          </DrawerSelect>
          {staffError && <p className="mt-1 text-xs text-red-600">{staffError}</p>}
        </DrawerField>
      </form>
    </RightDrawer>
  )
}
