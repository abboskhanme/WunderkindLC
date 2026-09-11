import { useMemo, useState } from 'react'
import { AlertTriangle } from 'lucide-react'
import {
  giveRetentionBonus,
  splitByMonths,
  type RetentionRow,
  type RetentionShare,
} from '@/api/services/retentionBonus'
import { apiErrorMessage, formatMoney } from '@/lib/utils'
import { Modal } from '@/components/ui/Modal'
import { Badge } from '@/components/ui/Badge'
import { Button } from '@/components/ui/Button'
import { Input } from '@/components/ui/Input'

interface Props {
  row: RetentionRow
  /** CenterMeta dagi standart summa — modal shu bilan ochiladi. */
  defaultAmount: number
  onClose: () => void
  onSaved: () => void
}

/**
 * BONUS BERISH modali — HAR FAN uchun alohida (so'rovga `courseId` ketadi).
 *
 * Taqsimot serverda o'qigan OYLAR nisbatida hisoblangan holda keladi; admin jami summani ham,
 * har bir ulushni ham o'zgartira oladi. Summa o'zgarsa ulushlar oylar nisbatida QAYTA bo'linadi
 * (server bilan bir xil qoida). Allaqachon shu o'quvchi orqali bonus olgan o'qituvchi taqsimotga
 * KIRMAYDI (`alreadyAwarded`): ulushi 0, inputi o'chirilgan. Saqlashdan oldin bloklanmaganlar
 * yig'indisi jami summaga TENG bo'lishi shart — bu server tomonda ham qayta tekshiriladi.
 */
export function GiveRetentionBonusModal({ row, defaultAmount, onClose, onSaved }: Props) {
  const initialTotal = defaultAmount > 0 ? defaultAmount : row.shares.reduce((s, x) => s + x.amount, 0)

  const [total, setTotal] = useState<number>(initialTotal)
  const [shares, setShares] = useState<RetentionShare[]>(() =>
    splitByMonths(row.shares, initialTotal),
  )
  const [note, setNote] = useState('')
  const [saving, setSaving] = useState(false)
  const [error, setError] = useState('')

  /** Bonus tegishi mumkin bo'lgan o'qituvchilar — bloklanganlar hisobga kirmaydi. */
  const openShares = useMemo(() => shares.filter((s) => !s.alreadyAwarded), [shares])
  const allBlocked = shares.length > 0 && openShares.length === 0
  const sum = useMemo(() => openShares.reduce((s, x) => s + x.amount, 0), [openShares])
  const balanced = sum === total
  const period = `${row.startMonth} … ${row.months.at(-1)?.month ?? ''}`

  /** Jami summa o'zgarganda — ulushlarni oylar nisbatida qayta bo'lish. */
  const changeTotal = (value: number) => {
    const next = Math.max(0, Math.round(value) || 0)
    setTotal(next)
    setShares((prev) => splitByMonths(prev, next))
  }

  const changeShare = (teacherId: string, value: number) =>
    setShares((prev) =>
      prev.map((s) =>
        s.teacherId === teacherId && !s.alreadyAwarded
          ? { ...s, amount: Math.max(0, Math.round(value) || 0) }
          : s,
      ),
    )

  const submit = async () => {
    // `saving` tekshiruvi — tugma `disabled` bo'lishidan tashqari IKKINCHI to'siq: tez ikki marta
    // bosilganda ikkita `POST /awards` ketib qolmasin (server ham qulf bilan himoyalangan).
    if (saving || !balanced || allBlocked) return
    setSaving(true)
    setError('')
    try {
      await giveRetentionBonus({
        studentId: row.studentId,
        courseId: row.courseId,
        totalAmount: total,
        shares: openShares.map((s) => ({
          teacherId: s.teacherId,
          amount: s.amount,
          months: s.months,
        })),
        note: note.trim() || undefined,
      })
      onSaved()
    } catch (err) {
      setError(apiErrorMessage(err, "Bonusni saqlab bo'lmadi"))
    } finally {
      setSaving(false)
    }
  }

  return (
    <Modal
      open
      onClose={onClose}
      title="Bonus berish"
      footer={
        <>
          <Button variant="secondary" onClick={onClose} disabled={saving}>
            Bekor
          </Button>
          <Button
            onClick={() => void submit()}
            disabled={saving || allBlocked || !balanced || total <= 0}
            title={
              allBlocked
                ? "Barcha o'qituvchilar bu o'quvchi orqali allaqachon bonus olgan"
                : undefined
            }
          >
            {saving ? 'Saqlanmoqda...' : 'Bonusni berish'}
          </Button>
        </>
      }
    >
      <div className="space-y-4">
        <div className="rounded-lg bg-slate-50 px-4 py-3 text-sm">
          <div className="font-semibold text-slate-800">
            {row.fullName}
            {row.courseName && <span className="text-slate-500"> · {row.courseName}</span>}
          </div>
          <div className="text-slate-500">
            Davr: {period} · {row.counted}/{row.required} oy · {row.cycleNo}-sikl
          </div>
        </div>

        <Input
          label="Bonus summasi (so'm)"
          type="number"
          min={0}
          step={1000}
          value={total}
          onChange={(e) => changeTotal(Number(e.target.value))}
          disabled={allBlocked}
        />

        <div>
          <div className="mb-2 text-sm font-medium text-slate-700">
            Taqsimot — o'qigan oylar nisbatida
          </div>
          <div className="space-y-2">
            {shares.map((s) => (
              <div key={s.teacherId} className="flex items-center gap-3">
                <div className="min-w-0 flex-1">
                  <div className="truncate text-sm text-slate-700">
                    {s.teacherName}
                    {s.alreadyAwarded && (
                      <Badge tone="amber" className="ml-2">
                        allaqachon bonus olgan
                      </Badge>
                    )}
                  </div>
                  <div className="text-xs text-slate-400">
                    {s.months} oy
                    {s.alreadyAwarded && ' · ulushi 0, vazni qolganlarga taqsimlandi'}
                  </div>
                </div>
                <input
                  type="number"
                  min={0}
                  step={1000}
                  disabled={s.alreadyAwarded}
                  className="w-36 rounded-lg border border-slate-200 px-3 py-2 text-right text-sm outline-none focus:border-brand-400 disabled:cursor-not-allowed disabled:bg-slate-50 disabled:text-slate-400"
                  value={s.amount}
                  onChange={(e) => changeShare(s.teacherId, Number(e.target.value))}
                />
              </div>
            ))}
            {shares.length === 0 && (
              <p className="text-sm text-slate-400">
                Taqsimot hisoblanmadi — guruhlarga o'qituvchi biriktirilmagan bo'lishi mumkin.
              </p>
            )}
          </div>

          <div className="mt-2 flex items-center justify-between border-t border-slate-100 pt-2 text-sm">
            <span className="text-slate-500">Yig'indi</span>
            <span className={balanced ? 'font-semibold text-emerald-600' : 'font-semibold text-rose-600'}>
              {formatMoney(sum)} {balanced ? '✓' : `≠ ${formatMoney(total)}`}
            </span>
          </div>
        </div>

        {allBlocked && (
          <div className="flex gap-2 rounded-lg border border-amber-200 bg-amber-50 px-3 py-2 text-xs text-amber-800">
            <AlertTriangle className="mt-0.5 h-4 w-4 shrink-0" />
            <span>
              Bonus berib bo'lmaydi: <b>barcha o'qituvchilar</b> bu o'quvchi orqali allaqachon
              bonus olishgan. Qoida — bir o'qituvchi bitta o'quvchi uchun umr bo'yi bir marta
              bonus oladi.
            </span>
          </div>
        )}

        <Input
          label="Izoh (ixtiyoriy)"
          value={note}
          onChange={(e) => setNote(e.target.value)}
          placeholder="masalan: yillik rag'batlantirish"
        />

        <div className="flex gap-2 rounded-lg bg-amber-50 px-3 py-2 text-xs text-amber-800">
          <AlertTriangle className="mt-0.5 h-4 w-4 shrink-0" />
          <span>
            Bu amal <b>pul chiqarmaydi</b> — faqat hisoblab qo'yiladi. Bonus o'qituvchi profilidagi
            «Bonus» bo'limida ko'rinadi; pul odatdagi maosh to'lovi orqali beriladi. Berilgach
            o'quvchining shu <b>fan</b> bo'yicha sanog'i keyingi sikldan boshlanadi.
          </span>
        </div>

        {error && (
          <div className="rounded-lg border border-rose-200 bg-rose-50 px-3 py-2 text-sm text-rose-700">
            {error}
          </div>
        )}
      </div>
    </Modal>
  )
}
