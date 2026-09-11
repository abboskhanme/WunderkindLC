import { Users, UserCheck, Wallet } from 'lucide-react'
import type { CrmOverview } from '@/api/services/leads'
import { Card } from '@/components/ui/Card'
import { StageBars } from './StageBars'
import { OriginTable } from './OriginTable'
import { cn, formatMoney } from '@/lib/utils'

/**
 * «BUTUN CRM MANZARASI» — bu sahifadagi raqamlar markazning QANCHA qismini qamraydi.
 *
 * <p><b>Nima uchun bor:</b> "Formalar" bo'limidagi ikkala statistika ham (lid formalari va
 * daraja testi) faqat O'Z kanalidan kelgan lidlarni sanaydi. Markazda esa qo'lda kiritilgan,
 * Instagramdan va boshqa yo'llardan kelgan lidlar ham bor. Shu kontekstsiz sahifadagi "jami"
 * raqami "markazning hamma lidi" deb o'qilib, noto'g'ri xulosaga olib kelardi.</p>
 *
 * <p>Ikkala sahifa AYNAN shu komponentni chizadi va server tomonda ham hisob bitta
 * (`LeadCrmOverview`) — ya'ni "qo'lda kiritilgan" yoki "to'ladi" so'zi ikki sahifada ikki xil
 * ma'no anglatmaydi.</p>
 *
 * <p>⚠️ Bu blok DAVRGA BOG'LIQ EMAS — joriy holat (kanban ustunlari kabi). Sahifaning qolgan
 * qismidagi davr filtri unga ta'sir qilmaydi va bu <c>sub</c> matnida ochiq yozilgan.</p>
 */
export function CrmOverviewCard({
  overview,
  /** Shu sahifa qamrab olgan kanal kaliti (`form` | `test`) — jadvalda ajratib ko'rsatiladi. */
  highlight,
}: {
  overview: CrmOverview
  highlight?: string
}) {
  const { leads, converted, paid, revenue, origins, byStage } = overview
  const pct = (n: number) => (leads > 0 ? Math.round((n / leads) * 100) : 0)

  return (
    <Card
      title="Butun CRM manzarasi"
      sub="Markazdagi BARCHA lidlar — qo'lda kiritilgani ham. Joriy holat (davr filtriga bog'liq emas)"
    >
      {leads === 0 ? (
        <p className="py-8 text-center text-sm text-slate-400">CRM'da hali lid yo'q.</p>
      ) : (
        <div className="space-y-5">
          {/* Uchta asosiy son — grafik emas, chunki bu bitta sarlavha qiymat (hero number). */}
          <div className="grid gap-3 sm:grid-cols-3">
            <Mini icon={Users} label="Jami lid" value={leads.toLocaleString()} tone="slate" />
            <Mini
              icon={UserCheck}
              label="O'quvchiga aylandi"
              value={converted.toLocaleString()}
              hint={`${pct(converted)}% konversiya`}
              tone="emerald"
            />
            <Mini
              icon={Wallet}
              label="To'lov qildi"
              value={paid.toLocaleString()}
              hint={
                revenue > 0
                  ? `${pct(paid)}% · ${formatMoney(revenue)} so'm`
                  : `${pct(paid)}% sotuv konversiyasi`
              }
              tone="teal"
            />
          </div>

          {/* KANALLAR — "qaysi yo'l bilan kelmoqda va qaysi biri haqiqatan SOTADI". */}
          <div>
            <h3 className="mb-2 text-sm font-semibold text-slate-700">Lidlar qayerdan keladi</h3>
            <OriginTable origins={origins} highlight={highlight} />
          </div>

          {/* BOSQICHLAR — "voronka qayerda tiqilib qolgan" (BARCHA lidlar bo'yicha). */}
          <div>
            <h3 className="mb-2 text-sm font-semibold text-slate-700">
              Barcha lidlar qaysi bosqichda
            </h3>
            <StageBars items={byStage} emptyText="Bosqichga tushgan lid yo'q." />
          </div>
        </div>
      )}
    </Card>
  )
}

/** Kichik ko'rsatkich — `StatCard` dan yengilroq (card ichida turadi). */
function Mini({
  icon: Icon,
  label,
  value,
  hint,
  tone,
}: {
  icon: typeof Users
  label: string
  value: string
  hint?: string
  tone: 'slate' | 'emerald' | 'teal'
}) {
  const tones = {
    slate: 'bg-slate-50 text-slate-500',
    emerald: 'bg-emerald-50 text-emerald-600',
    teal: 'bg-teal-50 text-teal-600',
  }
  return (
    <div className="flex items-center gap-3 rounded-xl border border-slate-100 bg-slate-50/40 px-3 py-2.5">
      <div className={cn('flex h-9 w-9 items-center justify-center rounded-lg', tones[tone])}>
        <Icon className="h-[18px] w-[18px]" />
      </div>
      <div className="min-w-0">
        <p className="text-[11px] font-semibold uppercase tracking-wide text-slate-400">{label}</p>
        <p className="font-mono text-lg font-semibold leading-tight text-slate-800">{value}</p>
        {hint && <p className="truncate text-[11px] text-slate-400">{hint}</p>}
      </div>
    </div>
  )
}
