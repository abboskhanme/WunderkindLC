import { useEffect, useState } from 'react'
import { CalendarCheck, CalendarClock, CalendarRange, EyeOff, Info, ShieldCheck, Wallet } from 'lucide-react'
import { getJournalPolicy, saveJournalPolicy, type JournalPolicy } from '@/api/services/journal'
import { Modal } from '@/components/ui/Modal'
import { Button } from '@/components/ui/Button'
import { Loader } from '@/components/ui/Loader'
import { cn } from '@/lib/utils'

/**
 * "Jurnal boshqaruvi" — barcha guruhlar jurnali uchun tahrirlash siyosati (Guruhlar sahifasidan
 * ochiladi). Rejim: erkin / faqat bugungi kun / oxirgi N kun; qo'shimcha: faqat "o'tildi" darsga
 * baho, cheklovni adminlarga ham qo'llash. Kelajak sanalar HAR DOIM taqiqlangan (server tomonda).
 * Alohida "To'lov nazorati" bo'limi — to'lamagan o'quvchini FAQAT o'qituvchi jurnalidan yashiradi
 * (muzlatish emas: hisob davom etadi, to'lovdan keyin qaytadi, admin hammani ko'radi).
 */
export function JournalPolicyModal({ open, onClose }: { open: boolean; onClose: () => void }) {
  const [policy, setPolicy] = useState<JournalPolicy | null>(null)
  const [saving, setSaving] = useState(false)

  useEffect(() => {
    if (!open) return
    setPolicy(null)
    getJournalPolicy()
      .then(setPolicy)
      .catch(() => {
        alert("Jurnal sozlamasini yuklab bo'lmadi")
        onClose()
      })
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [open])

  const save = async () => {
    if (!policy) return
    setSaving(true)
    try {
      await saveJournalPolicy(policy)
      onClose()
    } catch (err) {
      const message =
        (err as { response?: { data?: { message?: string } } })?.response?.data?.message ??
        "Saqlab bo'lmadi"
      alert(message)
    } finally {
      setSaving(false)
    }
  }

  const set = (patch: Partial<JournalPolicy>) =>
    setPolicy((p) => (p ? { ...p, ...patch } : p))

  return (
    <Modal
      open={open}
      onClose={onClose}
      title="Jurnal boshqaruvi"
      size="lg"
      footer={
        <>
          <Button variant="secondary" onClick={onClose} disabled={saving}>
            Bekor qilish
          </Button>
          <Button onClick={save} disabled={saving || !policy}>
            {saving ? 'Saqlanmoqda...' : 'Saqlash'}
          </Button>
        </>
      }
    >
      {!policy ? (
        <Loader label="Yuklanmoqda..." />
      ) : (
        <div className="space-y-5">
          {/* ---- Tahrirlash oynasi (rejim) ---- */}
          <div>
            <h3 className="mb-2 text-sm font-semibold text-slate-700">
              Baho/davomat kiritish oynasi
            </h3>
            <p className="mb-3 text-xs text-slate-500">
              Barcha guruhlar jurnaliga qaysi sanalar uchun kiritish mumkinligini belgilaydi.
            </p>
            <div className="grid gap-2 sm:grid-cols-3">
              <ModeCard
                icon={CalendarRange}
                title="Erkin"
                desc="Istalgan o'tgan sanaga kiritish mumkin"
                active={policy.editMode === 'free'}
                onClick={() => set({ editMode: 'free' })}
              />
              <ModeCard
                icon={CalendarCheck}
                title="Faqat bugungi kun"
                desc="Baho/davomat faqat shu kunning o'zida qo'yiladi, eski sanalar yopiq"
                active={policy.editMode === 'today'}
                onClick={() => set({ editMode: 'today' })}
              />
              <ModeCard
                icon={CalendarClock}
                title="Oxirgi N kun"
                desc="Belgilangan kun ichida to'ldirishga ruxsat (kechikkanlar uchun)"
                active={policy.editMode === 'window'}
                onClick={() => set({ editMode: 'window' })}
              />
            </div>
            {policy.editMode === 'window' && (
              <div className="mt-3 flex items-center gap-3 rounded-lg bg-slate-50 px-3 py-2.5">
                <span className="text-sm text-slate-600">Orqaga ruxsat etilgan muddat:</span>
                <input
                  type="number"
                  min={1}
                  max={90}
                  value={policy.retroDays}
                  onChange={(e) =>
                    set({ retroDays: Math.max(1, Math.min(90, Number(e.target.value) || 1)) })
                  }
                  className="w-20 rounded-lg border border-slate-200 px-2.5 py-1.5 text-center text-sm font-semibold outline-none focus:border-brand-400 focus:ring-2 focus:ring-brand-100"
                />
                <span className="text-sm text-slate-600">kun</span>
                <span className="ml-auto text-xs text-slate-400">
                  Masalan 3 — bugun va oldingi 3 kun ochiq
                </span>
              </div>
            )}
          </div>

          {/* ---- Qo'shimcha cheklovlar ---- */}
          <div className="space-y-2">
            <h3 className="text-sm font-semibold text-slate-700">Qo'shimcha</h3>

            <ToggleRow
              icon={CalendarCheck}
              title="Faqat o'tilgan darsga baho"
              desc={
                'Baho/davomat faqat "o\'tildi" deb belgilangan darsga qo\'yiladi — avval davomat qilinadi (sana ustuni bosiladi), keyin baho.'
              }
              checked={policy.conductedOnly}
              onChange={(v) => set({ conductedOnly: v })}
            />

            <ToggleRow
              icon={ShieldCheck}
              title="Adminlarga ham qo'llash"
              desc="Yoqilmasa cheklovlar faqat o'qituvchi ilovasiga tegishli — admin jurnali erkin qoladi."
              checked={policy.applyToAdmins}
              onChange={(v) => set({ applyToAdmins: v })}
            />
          </div>

          {/* ---- To'lov nazorati (o'qituvchi jurnalida yashirish) ---- */}
          <div>
            <h3 className="mb-2 text-sm font-semibold text-slate-700">To'lov nazorati</h3>
            <p className="mb-3 text-xs text-slate-500">
              To'lov qilmagan o'quvchi <b>o'qituvchi jurnalida</b> ko'rinmasin — o'qituvchi unga
              davomat/baho qo'ya olmaydi.
            </p>

            <div className="space-y-2">
              <ToggleRow
                icon={EyeOff}
                title="O'tgan oy(lar) uchun to'lamaganlar ko'rinmasin"
                desc="Oldingi oylardan qarzi bo'lgan o'quvchi o'qituvchi jurnalidan olib turiladi. Qarz yopilishi bilan qator o'z-o'zidan qaytadi."
                checked={policy.hideUnpaidPrevMonth}
                onChange={(v) => set({ hideUnpaidPrevMonth: v })}
              />

              <ToggleRow
                icon={CalendarClock}
                title="Joriy oy uchun belgilangan sanadan keyin to'lamaganlar ko'rinmasin"
                desc="Oyning belgilangan kunigacha shu oy uchun to'lov tushmasa, o'quvchi o'qituvchi jurnalidan olib turiladi."
                checked={policy.hideUnpaidAfterDay}
                onChange={(v) => set({ hideUnpaidAfterDay: v })}
              />
            </div>

            {policy.hideUnpaidAfterDay && (
              <div className="mt-3 flex flex-wrap items-center gap-3 rounded-lg bg-slate-50 px-3 py-2.5">
                <span className="text-sm text-slate-600">To'lov muddati — oyning</span>
                <input
                  type="number"
                  min={1}
                  max={28}
                  value={policy.unpaidCutoffDay}
                  onChange={(e) =>
                    set({ unpaidCutoffDay: Math.max(1, Math.min(28, Number(e.target.value) || 1)) })
                  }
                  className="w-20 rounded-lg border border-slate-200 px-2.5 py-1.5 text-center text-sm font-semibold outline-none focus:border-brand-400 focus:ring-2 focus:ring-brand-100"
                />
                <span className="text-sm text-slate-600">-kuni</span>
                <span className="ml-auto text-xs text-slate-400">
                  Eng katta qiymat — 28 (fevralda ham mavjud bo'lishi uchun)
                </span>
              </div>
            )}

            {(policy.hideUnpaidPrevMonth || policy.hideUnpaidAfterDay) && (
              <div className="mt-2 flex items-start gap-2.5 rounded-lg bg-amber-50 px-3 py-2.5 text-xs text-amber-800">
                <Info className="mt-0.5 h-4 w-4 shrink-0" />
                <div>
                  <p>
                    Bu <b>muzlatish EMAS</b>: a'zolik saqlanadi, oylik to'lov odatdagidek hisoblanib
                    boraveradi va qarz o'sadi. O'quvchi faqat <b>o'qituvchi jurnalida</b> ko'rinmaydi.
                  </p>
                  <p className="mt-1">
                    To'lov qilinishi bilan qator o'z-o'zidan <b>qaytadi</b> — hech qanday qo'shimcha
                    amal kerak emas.
                  </p>
                  <p className="mt-1">
                    <b>Admin jurnalida hamma ko'rinaveradi</b> — bunday o'quvchi ismi yonida
                    "O'qituvchida ko'rinmaydi" belgisi turadi.
                  </p>
                </div>
              </div>
            )}
          </div>

          {/* ---- Maoshni jurnalga bog'lash ---- */}
          <div>
            <h3 className="mb-2 text-sm font-semibold text-slate-700">Maosh va jurnal</h3>

            <ToggleRow
              icon={Wallet}
              title="Maosh jurnal bo'yicha hisoblansin"
              desc="Oy davomida jurnalda “o'tildi” deb belgilanmagan dars — o'tilmagan hisoblanadi va uning haqi o'qituvchi oyligidan ushlanadi. Qat'iy oylikka ham, foizli oylikka ham qo'llanadi."
              checked={policy.salaryRequireJournal}
              onChange={(v) => set({ salaryRequireJournal: v })}
            />

            {policy.salaryRequireJournal && (
              <>
                <div className="mt-3 flex flex-wrap items-center gap-3 rounded-lg bg-slate-50 px-3 py-2.5">
                  <span className="text-sm text-slate-600">Jurnalni to'ldirish muhlati:</span>
                  <input
                    type="number"
                    min={0}
                    max={30}
                    value={policy.salaryGraceDays}
                    onChange={(e) =>
                      set({ salaryGraceDays: Math.max(0, Math.min(30, Number(e.target.value) || 0)) })
                    }
                    className="w-20 rounded-lg border border-slate-200 px-2.5 py-1.5 text-center text-sm font-semibold outline-none focus:border-brand-400 focus:ring-2 focus:ring-brand-100"
                  />
                  <span className="text-sm text-slate-600">kun</span>
                  <span className="ml-auto text-xs text-slate-400">
                    Oxirgi shuncha kundagi darslar hali ushlanmaydi
                  </span>
                </div>
                <div className="mt-2 flex items-start gap-2.5 rounded-lg bg-amber-50 px-3 py-2.5 text-xs text-amber-800">
                  <Info className="mt-0.5 h-4 w-4 shrink-0" />
                  <div>
                    <p>
                      Rejadagi darslar guruh <b>dars kunlari</b>dan olinadi. Oylik = hisoblangan summa ×
                      (belgilangan darslar ÷ rejadagi darslar).
                    </p>
                    <p className="mt-1">
                      Ushlanma sababi (qaysi guruh, qaysi sanalar) <b>Moliya → O'qituvchilar</b> bo'limida
                      va o'qituvchining o'z "Maosh" sahifasida ko'rinadi.
                    </p>
                  </div>
                </div>
              </>
            )}
          </div>

          {/* ---- Eslatma ---- */}
          <div className="flex items-start gap-2.5 rounded-lg bg-blue-50 px-3 py-2.5 text-xs text-blue-700">
            <Info className="mt-0.5 h-4 w-4 shrink-0" />
            <div>
              <p>
                Kelajak sanalarga kiritish <b>har doim taqiqlangan</b> — bu sozlamaga bog'liq emas.
              </p>
              <p className="mt-1">
                Cheklov yopgan katakka urinilsa, foydalanuvchi sababini tushuntiruvchi xabar
                ko'radi (masalan: "faqat bugungi kun uchun kiritiladi").
              </p>
            </div>
          </div>
        </div>
      )}
    </Modal>
  )
}

/** Rejim kartasi — radio o'rnida bosiladigan karta. */
function ModeCard({
  icon: Icon,
  title,
  desc,
  active,
  onClick,
}: {
  icon: typeof CalendarCheck
  title: string
  desc: string
  active: boolean
  onClick: () => void
}) {
  return (
    <button
      type="button"
      onClick={onClick}
      className={cn(
        'rounded-xl border p-3 text-left transition-all',
        active
          ? 'border-brand-400 bg-brand-50 ring-2 ring-brand-100'
          : 'border-slate-200 bg-white hover:border-slate-300 hover:bg-slate-50',
      )}
    >
      <div className="flex items-center gap-2">
        <Icon className={cn('h-4 w-4', active ? 'text-brand-600' : 'text-slate-400')} />
        <span
          className={cn('text-sm font-semibold', active ? 'text-brand-700' : 'text-slate-700')}
        >
          {title}
        </span>
      </div>
      <p className="mt-1.5 text-xs leading-relaxed text-slate-500">{desc}</p>
    </button>
  )
}

/** Yoqish/o'chirish qatori (checkbox uslubidagi toggle). */
function ToggleRow({
  icon: Icon,
  title,
  desc,
  checked,
  onChange,
}: {
  icon: typeof CalendarCheck
  title: string
  desc: string
  checked: boolean
  onChange: (v: boolean) => void
}) {
  return (
    <button
      type="button"
      onClick={() => onChange(!checked)}
      className={cn(
        'flex w-full items-start gap-3 rounded-xl border p-3 text-left transition-all',
        checked
          ? 'border-brand-400 bg-brand-50'
          : 'border-slate-200 bg-white hover:border-slate-300 hover:bg-slate-50',
      )}
    >
      <Icon className={cn('mt-0.5 h-4 w-4 shrink-0', checked ? 'text-brand-600' : 'text-slate-400')} />
      <div className="min-w-0 flex-1">
        <div className={cn('text-sm font-semibold', checked ? 'text-brand-700' : 'text-slate-700')}>
          {title}
        </div>
        <p className="mt-0.5 text-xs leading-relaxed text-slate-500">{desc}</p>
      </div>
      {/* Toggle ko'rinishi */}
      <span
        className={cn(
          'relative mt-0.5 inline-flex h-5 w-9 shrink-0 items-center rounded-full transition-colors',
          checked ? 'bg-brand-500' : 'bg-slate-200',
        )}
      >
        <span
          className={cn(
            'inline-block h-3.5 w-3.5 rounded-full bg-white shadow transition-transform',
            checked ? 'translate-x-[18px]' : 'translate-x-[3px]',
          )}
        />
      </span>
    </button>
  )
}
