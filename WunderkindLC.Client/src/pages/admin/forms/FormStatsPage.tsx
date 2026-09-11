import { useEffect, useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { Eye, ClipboardList, UserPlus, GraduationCap, Wallet } from 'lucide-react'
import {
  getLeadFormStats, getLeadFormSubmissions,
  type LeadFormStats, type LeadFormSubmission,
} from '@/api/services/leadForms'
import { Card } from '@/components/ui/Card'
import { StatCard } from '@/components/ui/StatCard'
import { Loader } from '@/components/ui/Loader'
import { PageHeader } from '@/components/ui/PageHeader'
import { Badge } from '@/components/ui/Badge'
import { CardTabs } from '@/components/ui/CardTabs'
import { formTabs } from '@/config/sectionTabs'
import { StageBars } from '@/components/leads/StageBars'
import { CrmOverviewCard } from '@/components/leads/CrmOverviewCard'
import { DailyFlowChart } from '@/components/charts/DailyFlowChart'
import { FunnelAiPanel } from '@/components/ai/FunnelAiPanel'
import { usePerm } from '@/lib/permissions'
import { cn, formatMoney } from '@/lib/utils'
import { SubmissionsTable } from './FormEditorPage'

/**
 * FORMALAR STATISTIKASI — modulning asosiy savoli: «qaysi ijtimoiy tarmoq haqiqiy o'quvchi
 * keltiryapti?». Voronka: ochildi → ariza → lid → o'quvchi (hozir faol).
 *
 * <p>Butun hisob serverda (`LeadFormService.BuildStatsAsync`) — bu sahifa faqat chizadi.
 * Grafik va "bosqichlar" bo'lagi DARAJA TESTI statistikasi bilan umumiy komponentlarda
 * (`DailyFlowChart`, `StageBars`) — ikkala sahifa bir xil o'qilsin.</p>
 */

export function FormStatsPage() {
  const navigate = useNavigate()
  const { can } = usePerm()
  const canTests = can('schedule.levelTests', 'view')
  const [stats, setStats] = useState<LeadFormStats | null>(null)
  const [subs, setSubs] = useState<LeadFormSubmission[] | null>(null)
  const [loading, setLoading] = useState(true)

  useEffect(() => {
    Promise.all([getLeadFormStats(), getLeadFormSubmissions()])
      .then(([s, list]) => {
        setStats(s)
        setSubs(list)
      })
      .finally(() => setLoading(false))
  }, [])

  if (loading || !stats) return <Loader label="Yuklanmoqda..." />

  return (
    <div>
      <CardTabs items={formTabs(true, canTests)} className="mb-5" />

      <PageHeader
        title="Formalar statistikasi"
        sub="Ochildi → ariza → lid → o'quvchi → TO'LADI. Foizlar TAKRORSIZ lidlar bo'yicha; «Butun CRM manzarasi» esa markazning HAMMA lidini ko'rsatadi"
      />

      <div className="mb-4 grid gap-3 sm:grid-cols-2 lg:grid-cols-3 xl:grid-cols-5">
        <StatCard
          label="Ochilgan"
          value={stats.views}
          icon={Eye}
          hint={`${stats.forms} ta forma (${stats.activeForms} faol)`}
        />
        <StatCard
          label="Ariza"
          value={stats.submissions}
          icon={ClipboardList}
          iconBg="bg-sky-50"
          iconColor="text-sky-600"
          hint={stats.views > 0 ? `Ochganlarning ${Math.round((stats.submissions / stats.views) * 100)}%` : '—'}
        />
        <StatCard
          label="Yangi lid"
          value={stats.newLeads}
          icon={UserPlus}
          iconBg="bg-amber-50"
          iconColor="text-amber-600"
          hint={`${stats.submissions - stats.newLeads} ta takroriy ariza`}
        />
        <StatCard
          label="Aktiv o'quvchi"
          value={stats.activeStudents}
          icon={GraduationCap}
          iconBg="bg-emerald-50"
          iconColor="text-emerald-600"
          hint={`${stats.converted} ta o'quvchiga aylangan`}
        />
        {/* SOTUV: pul to'lagan lidlar — "o'quvchi bo'ldi" hali pul degani emas */}
        <StatCard
          label="To'lov qildi"
          value={stats.paid}
          icon={Wallet}
          iconBg="bg-teal-50"
          iconColor="text-teal-600"
          hint={stats.revenue > 0 ? `${formatMoney(stats.revenue)} so'm tushum` : "Hali to'lov yo'q"}
        />
      </div>

      {/* BUTUN CRM MANZARASI — yuqoridagi raqamlar faqat FORMALARDAN kelganlarni sanaydi.
          Markazda qo'lda kiritilgan va boshqa kanallardan kelgan lidlar ham bor, shuning uchun
          bu blok darrov KPI'dan keyin turadi: sahifadagi sonlar noto'g'ri o'qilmasin.
          AYNAN shu blok daraja testi statistikasida ham bor. */}
      <div className="mb-4">
        <CrmOverviewCard overview={stats.overview} highlight="form" />
      </div>

      {/* AI xulosasi — KPI'dan keyin, grafiklardan OLDIN: pastdagi jadvallarni o'qishdan avval
          nima muhimligini aytadi ("boshqaruvchi xulosasi"). */}
      <FunnelAiPanel kind="lead-forms" />

      <div className="space-y-4">
        <Card title="Ariza oqimi" sub="Oxirgi 30 kun — kunlik tushgan arizalar">
          <DailyFlowChart
            data={stats.daily}
            name="Ariza"
            emptyText="Oxirgi 30 kunda ariza tushmagan."
          />
        </Card>

        <Card
          title="Formalar kesimi"
          sub="Har bir forma bo'yicha voronka — havolani qayerga qo'yganingiz shu yerda ko'rinadi"
        >
          {stats.byForm.length === 0 ? (
            <p className="py-8 text-center text-sm text-slate-400">Hali forma yaratilmagan.</p>
          ) : (
            <div className="overflow-x-auto">
              <table className="w-full text-left text-sm">
                <thead className="border-b border-slate-100 text-xs uppercase tracking-wide text-slate-400">
                  <tr>
                    <th className="px-3 py-2">Forma</th>
                    <th className="px-3 py-2">Manba</th>
                    <th className="px-3 py-2 text-center">Ochildi</th>
                    <th className="px-3 py-2 text-center">Ariza</th>
                    <th className="px-3 py-2 text-center">Ariza %</th>
                    <th className="px-3 py-2 text-center">O'quvchi</th>
                    <th className="px-3 py-2 text-center">Aktiv</th>
                    <th className="px-3 py-2 text-center">To'ladi</th>
                    <th className="px-3 py-2 text-right">Tushum</th>
                    <th className="px-3 py-2 text-center">O'quvchi %</th>
                    <th className="px-3 py-2 text-center">Sotuv %</th>
                  </tr>
                </thead>
                <tbody className="divide-y divide-slate-50">
                  {stats.byForm.map((r) => (
                    <tr
                      key={r.formId}
                      onClick={() => navigate(`/admin/forms/${r.formId}`)}
                      className="cursor-pointer hover:bg-slate-50/60"
                    >
                      <td className="px-3 py-2 font-medium text-slate-700">
                        {r.title}
                        {!r.isActive && <span className="ml-2 text-[11px] font-normal text-slate-400">(o'chiq)</span>}
                      </td>
                      <td className="px-3 py-2">
                        {r.source ? <Badge tone="blue">{r.source}</Badge> : <span className="text-slate-300">—</span>}
                      </td>
                      <td className="px-3 py-2 text-center font-mono text-slate-500">{r.views}</td>
                      <td className="px-3 py-2 text-center font-mono text-slate-700">{r.submissions}</td>
                      <td className="px-3 py-2 text-center font-mono text-slate-500">{r.submitRate}%</td>
                      <td className="px-3 py-2 text-center font-mono text-slate-700">{r.converted}</td>
                      <td className="px-3 py-2 text-center font-mono text-emerald-600">{r.activeStudents}</td>
                      <td className="px-3 py-2 text-center font-mono text-teal-700">{r.paid}</td>
                      <td className="px-3 py-2 text-right font-mono text-teal-700">
                        {r.revenue > 0 ? formatMoney(r.revenue) : <span className="text-slate-300">—</span>}
                      </td>
                      <td
                        className={cn(
                          'px-3 py-2 text-center font-mono',
                          r.convertRate >= 30 ? 'text-emerald-600' : r.convertRate > 0 ? 'text-slate-600' : 'text-slate-300',
                        )}
                      >
                        {r.convertRate}%
                      </td>
                      {/* SOTUV konversiyasi — kanalning haqiqiy natijasi (pul keldimi) */}
                      <td
                        className={cn(
                          'px-3 py-2 text-center font-mono font-semibold',
                          r.payRate >= 20 ? 'text-teal-700' : r.payRate > 0 ? 'text-slate-600' : 'text-slate-300',
                        )}
                      >
                        {r.payRate}%
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          )}
        </Card>

        {/* BOSQICHLAR — "voronka qayerda tiqilib qolgan": lidlar hozir qaysi ustunda turibdi */}
        <Card
          title="Formadan kelgan lidlar qaysi bosqichda"
          sub="FAQAT formalardan kelganlarning HOZIRGI kanban ustuni — sotuv qayerda to'xtab qolgani (barcha lidlar yuqoridagi blokda)"
        >
          <StageBars items={stats.byStage} emptyText="Bosqichga tushgan lid yo'q." />
        </Card>

        <div className="grid gap-4 lg:grid-cols-2">
          <Card title="Manbalar kesimi" sub="Bir manbaga bir nechta forma bog'langan bo'lishi mumkin">
            {stats.bySource.length === 0 ? (
              <p className="py-8 text-center text-sm text-slate-400">Ma'lumot yo'q.</p>
            ) : (
              <table className="w-full text-left text-sm">
                <thead className="border-b border-slate-100 text-xs uppercase tracking-wide text-slate-400">
                  <tr>
                    <th className="px-3 py-2">Manba</th>
                    <th className="px-3 py-2 text-center">Forma</th>
                    <th className="px-3 py-2 text-center">Ariza</th>
                    <th className="px-3 py-2 text-center">Aktiv</th>
                    <th className="px-3 py-2 text-center">To'ladi</th>
                    <th className="px-3 py-2 text-right">Tushum</th>
                  </tr>
                </thead>
                <tbody className="divide-y divide-slate-50">
                  {stats.bySource.map((s) => (
                    <tr key={s.source || '—'} className="hover:bg-slate-50/60">
                      <td className="px-3 py-2">
                        {s.source ? (
                          <Badge tone="blue">{s.source}</Badge>
                        ) : (
                          <span className="text-slate-400">Manba tanlanmagan</span>
                        )}
                      </td>
                      <td className="px-3 py-2 text-center font-mono text-slate-500">{s.forms}</td>
                      <td className="px-3 py-2 text-center font-mono text-slate-700">{s.submissions}</td>
                      <td className="px-3 py-2 text-center font-mono text-emerald-600">{s.activeStudents}</td>
                      <td className="px-3 py-2 text-center font-mono text-teal-700">{s.paid}</td>
                      <td className="px-3 py-2 text-right font-mono text-teal-700">
                        {s.revenue > 0 ? formatMoney(s.revenue) : <span className="text-slate-300">—</span>}
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            )}
          </Card>

          <Card
            title="Havola belgisi (ref)"
            sub="Bitta forma havolasini bir necha joyga qo'ysangiz — oxiriga ?ref=story deb yozing"
          >
            {stats.byRef.length === 0 ? (
              <p className="py-8 text-center text-sm text-slate-400">Ma'lumot yo'q.</p>
            ) : (
              <table className="w-full text-left text-sm">
                <thead className="border-b border-slate-100 text-xs uppercase tracking-wide text-slate-400">
                  <tr>
                    <th className="px-3 py-2">Belgi</th>
                    <th className="px-3 py-2 text-center">Ariza</th>
                    <th className="px-3 py-2 text-center">O'quvchi</th>
                    <th className="px-3 py-2 text-center">To'ladi</th>
                  </tr>
                </thead>
                <tbody className="divide-y divide-slate-50">
                  {stats.byRef.map((r) => (
                    <tr key={r.ref || '—'} className="hover:bg-slate-50/60">
                      <td className="px-3 py-2">
                        {r.ref ? (
                          <span className="rounded-md bg-slate-100 px-2 py-0.5 font-mono text-[11px] text-slate-600">
                            {r.ref}
                          </span>
                        ) : (
                          <span className="text-slate-400">Belgisiz</span>
                        )}
                      </td>
                      <td className="px-3 py-2 text-center font-mono text-slate-700">{r.submissions}</td>
                      <td className="px-3 py-2 text-center font-mono text-slate-600">{r.converted}</td>
                      <td className="px-3 py-2 text-center font-mono text-teal-700">{r.paid}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            )}
          </Card>
        </div>

        {/* Oxirgi arizalar — barcha formalar bo'yicha */}
        <SubmissionsTable
          subs={subs}
          onOpenLead={(leadId) => navigate(`/admin/leads?lead=${leadId}`)}
          showForm
        />
      </div>
    </div>
  )
}
