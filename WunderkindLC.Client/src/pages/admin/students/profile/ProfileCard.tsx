import { useState } from 'react'
import {
  IconBook2,
  IconCamera,
  IconCheck,
  IconCopy,
  IconCreditCard,
  IconMessage,
  IconPercentage,
  IconPhone,
  IconCurrencyDollar,
  IconWallet,
} from '@tabler/icons-react'
import type { Student } from '@/types'
import type { StudentNotebook } from '@/api/services/studentNotebook'
import { DropdownMenu, type DropdownMenuItem } from '@/components/ui/DropdownMenu'
import { cn, formatMoney, formatPhoneShort } from '@/lib/utils'
import { amountDue, initials } from './model'

/**
 * edutizim profil kartasi (chap, ~280px): 96px dumaloq rasm + kamera belgisi, ism, telefon +
 * nusxa olish, uchta asosiy tugma (SMS · Balans/to'lov · Qo'ng'iroq) va ko'rsatkichlar ro'yxati.
 *
 * Raqamlar YANGI hisob emas — `StudentNotebook` (balans, davomat) va chegirma registri
 * (sahifa bir marta yuklaydi) dan olinadi.
 */
export function ProfileCard({
  data,
  student,
  discountLabel,
  canEditPhoto,
  menuItems,
  onPhoto,
  onSms,
  onPay,
  onCall,
}: {
  data: StudentNotebook
  /** To'liq yozuv (telefon, arxiv holati) — kelguncha `null`. */
  student: Student | null
  /** Chegirma qatori (`studentDiscountLabel` + registr sanog'i). Bo'sh — chegirma yo'q. */
  discountLabel: string
  canEditPhoto: boolean
  /** "⋮" menyusidagi qo'shimcha amallar (AI tahlil, Bog'lanish kerak, Jurnal ...). */
  menuItems: DropdownMenuItem[]
  onPhoto: () => void
  onSms: () => void
  onPay: () => void
  onCall: () => void
}) {
  const [copied, setCopied] = useState(false)
  const phone = student ? student.phone || student.parentPhone : data.parentPhone
  const due = amountDue(data.balance)

  const copyPhone = () => {
    if (!phone) return
    navigator.clipboard
      ?.writeText(phone)
      .then(() => {
        setCopied(true)
        window.setTimeout(() => setCopied(false), 1500)
      })
      .catch(() => {})
  }

  return (
    <div className="relative rounded-xl border border-[#dbe0e6] bg-white shadow-[0_1px_2px_rgba(0,0,0,0.05)]">
      {menuItems.length > 0 && (
        <div className="absolute right-2 top-2">
          <DropdownMenu items={menuItems} />
        </div>
      )}

      <div className="flex flex-col items-center px-4 pb-4 pt-6 text-center">
        {/* 96px dumaloq rasm + kamera belgisi (rasmni almashtirish — o'quvchini tahrirlash huquqi) */}
        <div className="relative">
          <button
            type="button"
            onClick={() => canEditPhoto && onPhoto()}
            disabled={!canEditPhoto}
            title={canEditPhoto ? (data.photoUrl ? 'Rasmni almashtirish' : "Rasm qo'shish") : undefined}
            className={cn(
              'flex h-24 w-24 items-center justify-center overflow-hidden rounded-full bg-[#ecf0ff] text-2xl font-semibold text-brand-600 ring-2 ring-[#dbe0e6]',
              canEditPhoto && 'cursor-pointer',
            )}
          >
            {data.photoUrl ? (
              <img src={data.photoUrl} alt={data.fullName} className="h-full w-full object-cover" />
            ) : (
              initials(data.fullName)
            )}
          </button>
          {canEditPhoto && (
            <button
              type="button"
              onClick={onPhoto}
              title="Rasmni almashtirish"
              className="absolute bottom-0.5 right-0.5 flex h-6 w-6 items-center justify-center rounded-full border border-[#dbe0e6] bg-white text-brand-600 shadow-sm hover:bg-brand-50"
            >
              <IconCamera className="h-3.5 w-3.5" />
            </button>
          )}
        </div>

        <h1 className="mt-3 text-[16px] font-semibold leading-tight text-black">{data.fullName}</h1>
        {student?.isArchived && (
          <span className="mt-1 rounded-full bg-red-50 px-2 py-0.5 text-[11px] font-semibold text-red-600">Arxivda</span>
        )}
        {phone && (
          <p className="mt-1 inline-flex items-center gap-1 text-[12px] text-[#6b7280]">
            {/^\d{2} \d{3} \d{2} \d{2}$/.test(formatPhoneShort(phone)) ? `+998 ${formatPhoneShort(phone)}` : phone}
            <button
              type="button"
              onClick={copyPhone}
              title="Nusxa olish"
              className="rounded p-0.5 text-[#6b7280] hover:bg-slate-100 hover:text-brand-600"
            >
              {copied ? <IconCheck className="h-3.5 w-3.5 text-emerald-600" /> : <IconCopy className="h-3.5 w-3.5" />}
            </button>
          </p>
        )}

        {/* Uchta asosiy amal */}
        <div className="mt-3 flex items-center gap-1">
          <CardIconButton label="SMS yuborish" onClick={onSms}>
            <IconMessage className="h-5 w-5" />
          </CardIconButton>
          <CardIconButton label="Balans · To'lov qilish" onClick={onPay}>
            <IconCurrencyDollar className="h-5 w-5" />
          </CardIconButton>
          <CardIconButton label="Qo'ng'iroq qilish" onClick={onCall}>
            <IconPhone className="h-5 w-5" />
          </CardIconButton>
        </div>
      </div>

      {/* Ko'rsatkichlar — 32px och-ko'k plitka + kulrang yorliq + qalin qiymat */}
      <div className="space-y-3 border-t border-[#eef0f2] px-4 py-4">
        {/* ⚠️ edutizimdagi «Qolgan darslar soni» bizda hisoblanmaydi (dars narxi × jadval yo'q) —
            o'rniga jurnaldan QATNASHGAN darslar (docs/ASSUMPTIONS.md). */}
        <StatRow
          icon={<IconBook2 className="h-[18px] w-[18px]" />}
          label="Qatnashgan darslar"
          value={data.conducted > 0 ? `${data.attended} / ${data.conducted}` : '—'}
        />
        <StatRow
          icon={<IconCreditCard className="h-[18px] w-[18px]" />}
          label="To'lash kerak"
          value={formatMoney(due)}
          valueCls={due > 0 ? 'text-red-600' : undefined}
        />
        <StatRow
          icon={<IconWallet className="h-[18px] w-[18px]" />}
          label="Balans"
          value={formatMoney(data.balance)}
          valueCls={data.balance < 0 ? 'text-red-600' : data.balance > 0 ? 'text-emerald-700' : undefined}
        />
        <StatRow
          icon={<IconPercentage className="h-[18px] w-[18px]" />}
          label="Chegirma"
          value={discountLabel || "Yo'q"}
        />
      </div>
    </div>
  )
}

function CardIconButton({ label, onClick, children }: { label: string; onClick: () => void; children: React.ReactNode }) {
  return (
    <button
      type="button"
      title={label}
      aria-label={label}
      onClick={onClick}
      className="inline-flex h-8 w-8 items-center justify-center rounded-lg text-brand-600 transition-colors hover:bg-brand-600/10"
    >
      {children}
    </button>
  )
}

function StatRow({
  icon,
  label,
  value,
  valueCls,
}: {
  icon: React.ReactNode
  label: string
  value: string
  valueCls?: string
}) {
  return (
    <div className="flex items-center gap-2.5">
      <span className="flex h-8 w-8 shrink-0 items-center justify-center rounded-lg border border-brand-600/20 bg-brand-600/10 text-brand-600">
        {icon}
      </span>
      <div className="min-w-0">
        <p className="text-[12px] leading-tight text-[#6b7280]">{label}</p>
        <p className={cn('break-words text-[14px] font-semibold leading-tight text-black', valueCls)}>{value}</p>
      </div>
    </div>
  )
}

