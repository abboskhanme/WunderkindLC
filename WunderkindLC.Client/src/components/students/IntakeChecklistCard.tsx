import { useEffect, useState, type ReactNode } from 'react'
import { Link } from 'react-router-dom'
import { CheckCircle2, ChevronDown, Circle, ClipboardCheck, UserCheck } from 'lucide-react'
import {
  getStudentIntake,
  type IntakeItem,
  type StudentIntakeDetail,
} from '@/api/services/studentIntake'
import { usePerm } from '@/lib/permissions'
import { itemState, progressTone } from '@/lib/intake'
import { apiErrorMessage, cn, formatDateTime } from '@/lib/utils'
import { IntakeMarkModal } from './IntakeMarkModal'
import { IntakeContractModal } from './IntakeContractModal'

const actionCls = 'text-[12px] font-semibold text-brand-600 hover:underline'

const barCls: Record<ReturnType<typeof progressTone>, string> = {
  complete: 'bg-emerald-500',
  partial: 'bg-amber-400',
  low: 'bg-rose-400',
}

/** Band belgisi: avtomatik — yashil, qo'lda — indigo (farqi ko'rinsin), kutilmoqda — kulrang doira. */
function StateIcon({ item }: { item: IntakeItem }) {
  const s = itemState(item)
  if (s === 'auto') return <CheckCircle2 className="mt-0.5 h-4 w-4 shrink-0 text-emerald-600" aria-label="Bajarilgan" />
  if (s === 'manual') return <UserCheck className="mt-0.5 h-4 w-4 shrink-0 text-indigo-600" aria-label="Qo'lda belgilangan" />
  return <Circle className="mt-0.5 h-4 w-4 shrink-0 text-slate-300" aria-label="Bajarilmagan" />
}

/**
 * O'QUVCHI PROFILI → «Qabul cheklisti» (chap ustun, profil kartasi ostida): 5 ta band, progress va
 * har band uchun "qayerda tuzatiladi" tugmasi (tahrirlash formasi, guruhga qo'shish, shartnoma,
 * bot havolasi, to'lov oynasi). Cheklist TO'LIQ bo'lsa — bitta qatorga YIG'ILADI (bosilsa ochiladi).
 *
 * <p>Holat SERVERDA hisoblanadi (`GET /admin/student-intake/{id}`) — bu yerda faqat ko'rsatiladi.
 * `refreshKey` o'zgarsa (tahrir, to'lov, guruh) qayta so'raladi.</p>
 *
 * <p>Chaqiruvchi `key={studentId}` beradi — boshqa o'quvchiga o'tilganda yig'ish holati tozalanadi.</p>
 */
export function IntakeChecklistCard({
  studentId,
  refreshKey,
  onEditProfile,
  onAddGroup,
  onPay,
}: {
  studentId: string
  /** O'zgarsa cheklist qayta yuklanadi (profil tahriri, to'lov, a'zolik). */
  refreshKey: string | number
  /** «To'ldirish» — tahrirlash formasi (ruxsat bo'lmasa berilmaydi). */
  onEditProfile?: () => void
  /** «Guruhga qo'shish» oynasi. */
  onAddGroup?: () => void
  /** «To'lov qilish» oynasi. */
  onPay?: () => void
}) {
  const { can } = usePerm()
  const canMark = can('students.list', 'edit')
  const canContract = can('contracts', 'create')
  const canLeads = can('leads.list', 'view')

  const [detail, setDetail] = useState<StudentIntakeDetail | null>(null)
  const [error, setError] = useState('')
  /** Shartnoma tuzilgach qayta so'rash uchun (refreshKey tashqaridan, bu — ichki). */
  const [tick, setTick] = useState(0)
  /** null — standart (to'liq bo'lsa yig'iq, aks holda ochiq); foydalanuvchi bosgach — uning tanlovi. */
  const [toggled, setToggled] = useState<boolean | null>(null)
  const [markItem, setMarkItem] = useState<IntakeItem | null>(null)
  const [contractOpen, setContractOpen] = useState(false)
  const [copied, setCopied] = useState(false)

  useEffect(() => {
    let active = true
    getStudentIntake(studentId)
      .then((d) => {
        if (!active) return
        setDetail(d)
        setError('')
      })
      // Sahifaning ASOSIY ma'lumoti emas, lekin xato jim yutilmaydi — karta ichida ko'rinadi.
      .catch((e) => active && setError(apiErrorMessage(e, "Qabul cheklistini yuklab bo'lmadi")))
    return () => {
      active = false
    }
  }, [studentId, refreshKey, tick])

  if (!detail) {
    return error ? (
      <div className="rounded-xl border border-[#dbe0e6] bg-white px-4 py-3 text-[12px] text-red-600 shadow-[0_1px_2px_rgba(0,0,0,0.05)]">
        {error}
      </div>
    ) : null
  }

  const c = detail.checklist
  const expanded = toggled ?? !c.complete
  const tone = progressTone(c.done, c.total)
  const botUrl = detail.telegramBot ? `https://t.me/${detail.telegramBot}` : ''

  const copyBot = () => {
    if (!botUrl) return
    navigator.clipboard
      ?.writeText(botUrl)
      .then(() => {
        setCopied(true)
        window.setTimeout(() => setCopied(false), 1500)
      })
      // Brauzer ruxsat bermasa — havola matni baribir ko'rinib turibdi (error-visibility.md §4).
      .catch(() => {})
  }

  const actionsOf = (item: IntakeItem) => {
    const out: ReactNode[] = []
    if (!item.done) {
      if (item.key === 'profile') {
        const needsGroup = item.missing.some((m) => m.key === 'group')
        const needsFields = item.missing.some((m) => m.key !== 'group')
        if (needsFields && onEditProfile)
          out.push(<button key="edit" type="button" className={actionCls} onClick={onEditProfile}>To'ldirish</button>)
        if (needsGroup && onAddGroup)
          out.push(<button key="group" type="button" className={actionCls} onClick={onAddGroup}>Guruhga qo'shish</button>)
      }
      if (item.key === 'levelTest' && c.leadId && canLeads)
        out.push(
          <Link key="lead" to={`/admin/leads/${c.leadId}`} className={actionCls} title="Lid kartasidan daraja testi havolasini yuboring">
            Lid kartasi
          </Link>,
        )
      if (item.key === 'contract' && canContract)
        out.push(<button key="contract" type="button" className={actionCls} onClick={() => setContractOpen(true)}>Shartnoma tuzish</button>)
      if (item.key === 'telegram' && botUrl)
        out.push(
          <button key="bot" type="button" className={actionCls} onClick={copyBot} title={botUrl}>
            {copied ? 'Nusxalandi' : 'Bot havolasi'}
          </button>,
        )
      if (item.key === 'payment' && onPay)
        out.push(<button key="pay" type="button" className={actionCls} onClick={onPay}>To'lov qilish</button>)
    }
    if (canMark && (item.mark || !item.done))
      out.push(
        <button key="mark" type="button" className={cn(actionCls, 'text-slate-500')} onClick={() => setMarkItem(item)}>
          {item.mark ? "Belgini o'zgartirish" : "Qo'lda belgilash"}
        </button>,
      )
    return out
  }

  return (
    <div className="rounded-xl border border-[#dbe0e6] bg-white shadow-[0_1px_2px_rgba(0,0,0,0.05)]">
      <button
        type="button"
        onClick={() => setToggled(!expanded)}
        className="flex w-full items-center gap-2 px-4 py-3 text-left"
        aria-expanded={expanded}
      >
        <ClipboardCheck className={cn('h-5 w-5 shrink-0', c.complete ? 'text-emerald-600' : 'text-brand-600')} />
        <span className="flex-1 font-semibold text-slate-800">Qabul cheklisti</span>
        <span
          className={cn(
            'rounded-full px-2 py-0.5 text-[12px] font-semibold',
            c.complete ? 'bg-emerald-50 text-emerald-700' : 'bg-amber-50 text-amber-800',
          )}
        >
          {c.complete ? "To'liq" : `${c.done}/${c.total}`}
        </span>
        <ChevronDown className={cn('h-4 w-4 text-slate-400 transition-transform', expanded && 'rotate-180')} />
      </button>

      {expanded && (
        <div className="border-t border-slate-100 px-4 pb-3 pt-2">
          <div className="mb-1 h-1.5 overflow-hidden rounded-full bg-slate-100">
            <div className={cn('h-full rounded-full', barCls[tone])} style={{ width: `${(c.done / Math.max(1, c.total)) * 100}%` }} />
          </div>
          {error && <p className="mt-1 text-[12px] text-red-600">{error}</p>}
          <ul className="divide-y divide-slate-100">
            {c.items.map((item) => {
              const actions = actionsOf(item)
              return (
                <li key={item.key} className="flex gap-2 py-2">
                  <StateIcon item={item} />
                  <div className="min-w-0 flex-1">
                    <p className={cn('text-[13px] font-medium', item.done ? 'text-slate-800' : 'text-slate-600')}>{item.label}</p>
                    <p className="text-[12px] text-slate-500">{item.detail}</p>
                    {item.mark && (
                      <p className="mt-0.5 text-[12px] text-indigo-700">
                        Qo'lda: {item.mark.markedBy || '—'} · {formatDateTime(item.mark.markedAt)}
                        {item.mark.note && <span className="block text-slate-500">«{item.mark.note}»</span>}
                      </p>
                    )}
                    {item.key === 'telegram' && !item.done && botUrl && (
                      <p className="mt-0.5 text-[11px] text-slate-400">
                        Ota-ona @{detail.telegramBot} botiga kirib telefon raqamini ulashadi.
                      </p>
                    )}
                    {actions.length > 0 && <div className="mt-1 flex flex-wrap gap-x-3 gap-y-1">{actions}</div>}
                  </div>
                </li>
              )
            })}
          </ul>
        </div>
      )}

      {markItem && (
        <IntakeMarkModal
          key={markItem.key}
          studentId={studentId}
          studentName={c.fullName}
          item={markItem}
          onClose={() => setMarkItem(null)}
          onSaved={setDetail}
        />
      )}
      {contractOpen && (
        <IntakeContractModal
          studentId={studentId}
          studentName={c.fullName}
          onClose={() => setContractOpen(false)}
          onDone={() => setTick((t) => t + 1)}
        />
      )}
    </div>
  )
}
