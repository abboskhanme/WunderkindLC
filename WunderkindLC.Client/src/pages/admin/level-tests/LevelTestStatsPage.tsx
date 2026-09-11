import { useEffect, useState } from 'react'
import { Link, useNavigate } from 'react-router-dom'
import { FileText, ClipboardList, UserPlus, GraduationCap, Wallet, Send } from 'lucide-react'
import { getLevelTestOverallStats, type LevelTestOverallStats } from '@/api/services/levelTests'
import { Card } from '@/components/ui/Card'
import { StatCard } from '@/components/ui/StatCard'
import { Badge } from '@/components/ui/Badge'
import { Loader } from '@/components/ui/Loader'
import { PageHeader } from '@/components/ui/PageHeader'
import { CardTabs } from '@/components/ui/CardTabs'
import { formTabs } from '@/config/sectionTabs'
import { LeadStageChip } from '@/components/leads/LeadStageChip'
import { StageBars } from '@/components/leads/StageBars'
import { CrmOverviewCard } from '@/components/leads/CrmOverviewCard'
import { DailyFlowChart } from '@/components/charts/DailyFlowChart'
import { FunnelAiPanel } from '@/components/ai/FunnelAiPanel'
import { usePerm } from '@/lib/permissions'
import { cn, formatDate, formatMoney } from '@/lib/utils'

/**
 * DARAJA TESTLARI STATISTIKASI — "Formalar" bo'limining alohida cardi (`/admin/level-tests/stats`).
 *
 * <p>Savol lid formalaridagi bilan BIR XIL: «qaysi test haqiqiy o'quvchi va PUL keltiryapti?».
 * Voronka: topshirdi → lid → o'quvchi → TO'LADI.</p>
 *
 * <p>NEGA alohida sahifa: ilgari bu raqamlarni ko'rish uchun HAR BIR testning ichiga kirish kerak
 * edi (test → "Statistika" tabi), ya'ni testlarni bir-biriga solishtirib bo'lmasdi. Endi hammasi
 * bitta ekranda, lid statistikasi bilan bir xil ko'rinishda; testning O'Z sahifasidagi statistika
 * esa joyida qoldi (bitta test tafsiloti uchun).</p>
 *
 * <p>Butun hisob serverda (`LevelTestService.BuildOverallStatsAsync`) — bu sahifa faqat chizadi.</p>
 */
export function LevelTestStatsPage() {
  const navigate = useNavigate()
  const { can } = usePerm()
  // "Formalar" bo'limining ikkinchi turi — lid formalari `leads` ruxsatida.
  const canForms = can('leads.forms', 'view')
  // «Lidni ochish» — LIDLAR sahifasiga o'tadi, ya'ni card ruxsati emas, `leads.list` kerak.
  const canOpenLead = can('leads.list', 'view')
  const [stats, setStats] = useState<LevelTestOverallStats | null>(null)
  const [loading, setLoading] = useState(true)

  useEffect(() => {
    getLevelTestOverallStats()
      .then(setStats)
      .catch(() => setStats(null))
      .finally(() => setLoading(false))
  }, [])

  // "Darajalar bo'yicha" chiziqlari eng katta darajaga NISBATAN chiziladi (StageBars bilan bir xil).
  // Sikldan TASHQARIDA — aks holda har qator uchun butun ro'yxat qaytadan skanerlanardi.
  const levelMax = Math.max(0, ...(stats?.byLevel ?? []).map((l) => l.count))

  return (
    <div>
      <CardTabs items={formTabs(canForms, true)} className="mb-5" />

      <PageHeader
        title="Test statistikasi"
        sub="Topshirdi → lid → o'quvchi → TO'LADI. Foizlar TAKRORSIZ lidlar bo'yicha; «Butun CRM manzarasi» esa markazning HAMMA lidini ko'rsatadi"
      />

      {loading ? (
        <Loader label="Yuklanmoqda..." />
      ) : !stats ? (
        <Card>
          <p className="py-10 text-center text-slate-400">Statistikani yuklab bo'lmadi.</p>
        </Card>
      ) : (
        <>
          <div className="mb-4 grid gap-3 sm:grid-cols-2 lg:grid-cols-3 xl:grid-cols-5">
            <StatCard
              label="Testlar"
              value={stats.testCount}
              icon={FileText}
              hint={`${stats.activeTests} ta faol`}
            />
            <StatCard
              label="Topshirdi"
              value={stats.submissions}
              icon={ClipboardList}
              iconBg="bg-sky-50"
              iconColor="text-sky-600"
              hint={stats.submissions > 0 ? `O'rtacha natija ${stats.avgPercent}%` : '—'}
            />
            {/*
              Lid — TAKRORSIZ: bir odam testni ikki marta topshirsa ham bitta mijoz.
              ⚠️ Izohda "N ta takroriy topshiriq" deb YOZILMAYDI: `submissions - leads` ayirmasiga
              takrorlar bilan birga lidga umuman BOG'LANMAGAN topshiriqlar ham kiradi (server
              takrorsiz lidlarni sanaganda `leadId` bo'sh qatorlarni tashlab yuboradi), ya'ni bu
              son "takroriy" degani emas. Shuning uchun izoh faqat solishtirish uchun jami
              topshiriqni eslatadi — har doim to'g'ri bo'ladi.
            */}
            <StatCard
              label="Lid"
              value={stats.leads}
              icon={UserPlus}
              iconBg="bg-amber-50"
              iconColor="text-amber-600"
              hint={
                stats.submissions > stats.leads
                  ? `${stats.submissions} ta topshiriqdan`
                  : 'Har topshiriq — alohida odam'
              }
            />
            <StatCard
              label="Aktiv o'quvchi"
              value={stats.active}
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

          {/* BUTUN CRM MANZARASI — yuqoridagi raqamlar faqat DARAJA TESTIDAN kelganlarni
              sanaydi. Lid formalari statistikasidagi bilan AYNAN bir xil blok (server tomonda
              ham hisob bitta: `LeadCrmOverview`). */}
          {stats.overview && (
            <div className="mb-4">
              <CrmOverviewCard overview={stats.overview} highlight="test" />
            </div>
          )}

          {/* AI xulosasi — KPI'dan keyin, grafiklardan OLDIN: pastdagi jadvallarni o'qishdan avval
              nima muhimligini aytadi ("boshqaruvchi xulosasi"). */}
          <FunnelAiPanel kind="level-tests" />

          <div className="space-y-4">
            <Card title="Topshiriqlar oqimi" sub="Oxirgi 30 kun — kunlik topshirganlar">
              <DailyFlowChart
                data={stats.daily}
                name="Topshirdi"
                emptyText="Oxirgi 30 kunda hech kim topshirmagan."
              />
            </Card>

            <Card
              title="Testlar kesimi"
              sub="Har bir test bo'yicha voronka — qaysi test haqiqiy o'quvchi keltiryapti"
            >
              {stats.byTest.length === 0 ? (
                <p className="py-8 text-center text-sm text-slate-400">Hali test yaratilmagan.</p>
              ) : (
                <div className="overflow-x-auto">
                  <table className="w-full text-left text-sm">
                    <thead className="border-b border-slate-100 text-xs uppercase tracking-wide text-slate-400">
                      <tr>
                        <th className="px-3 py-2">Test</th>
                        <th className="px-3 py-2 text-center">Topshirdi</th>
                        <th className="px-3 py-2 text-center">O'rtacha %</th>
                        <th className="px-3 py-2 text-center">Havola</th>
                        <th className="px-3 py-2 text-center">Lid</th>
                        <th className="px-3 py-2 text-center">O'quvchi</th>
                        <th className="px-3 py-2 text-center">Aktiv</th>
                        <th className="px-3 py-2 text-center">To'ladi</th>
                        <th className="px-3 py-2 text-right">Tushum</th>
                        <th className="px-3 py-2 text-center">O'quvchi %</th>
                        <th className="px-3 py-2 text-center">Sotuv %</th>
                      </tr>
                    </thead>
                    <tbody className="divide-y divide-slate-50">
                      {stats.byTest.map((r) => (
                        <tr
                          key={r.testId}
                          onClick={() => navigate(`/admin/level-tests/${r.testId}`)}
                          className="cursor-pointer hover:bg-slate-50/60"
                        >
                          <td className="px-3 py-2 font-medium text-slate-700">
                            {r.title}
                            {!r.isActive && (
                              <span className="ml-2 text-[11px] font-normal text-slate-400">(o'chiq)</span>
                            )}
                          </td>
                          <td className="px-3 py-2 text-center font-mono text-slate-700">{r.submissions}</td>
                          <td className="px-3 py-2 text-center font-mono text-slate-500">{r.avgPercent}%</td>
                          {/* Havola = SMS bilan yuborilgan bir martalik taklif (ishlangani / jami) */}
                          <td className="px-3 py-2 text-center font-mono text-slate-500">
                            {r.invites > 0 ? `${r.invitesUsed}/${r.invites}` : <span className="text-slate-300">—</span>}
                          </td>
                          <td className="px-3 py-2 text-center font-mono text-slate-600">{r.leads}</td>
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
                          {/* SOTUV konversiyasi — testning haqiqiy natijasi (pul keldimi) */}
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

            {/* BOSQICHLAR — "voronka qayerda tiqilib qolgan" (lid statistikasi bilan bir xil) */}
            <Card
              title="Test topshirganlar qaysi bosqichda"
              sub="FAQAT test topshirganlarning HOZIRGI kanban ustuni — sotuv qayerda to'xtab qolgani (barcha lidlar yuqoridagi blokda)"
            >
              <StageBars items={stats.byStage} emptyText="Bosqichga tushgan lid yo'q." />
            </Card>

            <div className="grid gap-4 lg:grid-cols-2">
              <Card title="Darajalar bo'yicha" sub="Topshirganlar natijasi qaysi diapazonga tushdi">
                {stats.byLevel.length === 0 ? (
                  <p className="py-8 text-center text-sm text-slate-400">Ma'lumot yo'q.</p>
                ) : (
                  <div className="space-y-2">
                    {stats.byLevel.map((l) => (
                      <div key={l.level} className="flex items-center gap-3">
                        <span className="w-24 shrink-0 truncate rounded-md bg-brand-50 px-2 py-0.5 text-xs font-semibold text-brand-700">
                          {l.level}
                        </span>
                        <div className="h-2 flex-1 overflow-hidden rounded-full bg-slate-100">
                          <div
                            className="h-full rounded-full bg-brand-400"
                            style={{ width: `${levelMax > 0 ? Math.round((l.count / levelMax) * 100) : 0}%` }}
                          />
                        </div>
                        <span className="w-10 shrink-0 text-right font-mono text-sm text-slate-700">
                          {l.count}
                        </span>
                      </div>
                    ))}
                  </div>
                )}
              </Card>

              {/* Havolalar — lidga SMS bilan yuborilgan bir martalik taklif (ommaviy slug'dan farqli) */}
              <Card title="Yuborilgan havolalar" sub="Lidga SMS bilan yuborilgan bir martalik test havolalari">
                <div className="flex items-center gap-4 py-2">
                  <div className="flex h-11 w-11 items-center justify-center rounded-xl bg-brand-50 text-brand-600">
                    <Send className="h-5 w-5" />
                  </div>
                  <div>
                    <div className="font-mono text-2xl font-semibold text-slate-800">
                      {stats.invitesUsed}
                      <span className="text-base text-slate-400"> / {stats.invites}</span>
                    </div>
                    <div className="text-xs text-slate-400">
                      {stats.invites > 0
                        ? `Yuborilganlarning ${Math.round((stats.invitesUsed / stats.invites) * 100)}% i ishlangan`
                        : 'Hali havola yuborilmagan'}
                    </div>
                  </div>
                </div>
              </Card>
            </div>

            {/* Topshirganlar — bosqich va to'lov bilan (lid statistikasidagi arizalar jadvali kabi) */}
            {/*
              Sarlavhadagi son — JAMI topshiriqlar (`rowsTotal`), jadvalda esa server ko'pi bilan
              oxirgi 500 tasini qaytaradi. Cheklov jimgina qirqilmaydi: qirqilgan bo'lsa jadval
              ostida ochiq yozuv chiqadi (loyiha qoidasi — foydalanuvchi cheklovni bilishi kerak).
            */}
            <Card
              title={`Topshirganlar (${stats.rowsTotal})`}
              sub="Qaysi testga tegishli + natija + lidning hozirgi holati"
            >
              {stats.rows.length === 0 ? (
                <p className="py-8 text-center text-sm text-slate-400">Hali hech kim topshirmagan.</p>
              ) : (
                <div className="overflow-x-auto">
                  <table className="w-full text-left text-sm">
                    <thead className="border-b border-slate-100 text-xs uppercase tracking-wide text-slate-400">
                      <tr>
                        <th className="px-3 py-2">F.I.SH</th>
                        <th className="px-3 py-2">Test</th>
                        <th className="px-3 py-2">Daraja</th>
                        <th className="px-3 py-2 text-center">Foiz</th>
                        <th className="px-3 py-2">Bosqich</th>
                        <th className="px-3 py-2 text-right">To'lov</th>
                        <th className="px-3 py-2 text-center">Holat</th>
                        <th className="px-3 py-2">Guruh</th>
                        <th className="px-3 py-2">O'qituvchi</th>
                        <th className="px-3 py-2">Sana</th>
                        <th className="px-3 py-2 text-right">Lid</th>
                      </tr>
                    </thead>
                    <tbody className="divide-y divide-slate-50">
                      {stats.rows.map((r) => (
                        <tr key={r.submissionId} className={cn('hover:bg-slate-50/60', r.isDeleted && 'bg-red-50/40')}>
                          <td className={cn('px-3 py-2 font-medium text-slate-700', r.isDeleted && 'text-red-600 line-through')}>
                            {r.fullName}
                            {r.isDeleted && (
                              <span className="ml-1.5 text-[11px] font-normal text-red-500">(lid o'chirilgan)</span>
                            )}
                            <div className="font-mono text-[11px] font-normal text-slate-400">{r.phone}</div>
                          </td>
                          <td className="px-3 py-2 text-slate-500">
                            <Link to={`/admin/level-tests/${r.testId}`} className="text-inherit hover:underline">
                              {r.testTitle || '—'}
                            </Link>
                          </td>
                          <td className="px-3 py-2">
                            {r.level ? (
                              <span className="rounded-md bg-brand-50 px-2 py-0.5 text-xs font-semibold text-brand-700">
                                {r.level}
                              </span>
                            ) : (
                              <span className="text-slate-300">—</span>
                            )}
                          </td>
                          <td className="px-3 py-2 text-center font-mono text-slate-600">{r.percent}%</td>
                          <td className="px-3 py-2">
                            <LeadStageChip title={r.stageTitle} color={r.stageColor} />
                          </td>
                          <td className="px-3 py-2 text-right">
                            {r.paid ? (
                              <span className="whitespace-nowrap font-mono text-xs font-semibold text-emerald-600">
                                {formatMoney(r.paidTotal)}
                                {r.firstPaidAt && (
                                  <span className="block font-sans text-[10px] font-normal text-slate-400">
                                    {formatDate(r.firstPaidAt)}
                                  </span>
                                )}
                              </span>
                            ) : (
                              <span className="text-slate-300">—</span>
                            )}
                          </td>
                          <td className="px-3 py-2 text-center">
                            {r.active ? (
                              <Badge tone="green">Aktiv o'quvchi</Badge>
                            ) : r.studentId ? (
                              <Badge tone="blue">O'quvchi</Badge>
                            ) : (
                              <Badge>Lid</Badge>
                            )}
                          </td>
                          <td className="px-3 py-2 text-slate-600">
                            {r.groupName || <span className="text-slate-300">—</span>}
                          </td>
                          <td className="px-3 py-2 text-slate-600">
                            {r.teacherName || <span className="text-slate-300">—</span>}
                          </td>
                          <td className="px-3 py-2 text-slate-500">{formatDate(r.createdAt)}</td>
                          <td className="px-3 py-2 text-right">
                            {/*
                              Lid o'chirilgan bo'lsa ochadigan narsa yo'q.
                              Tugma `leads.list` ruxsati bilan ham darvozalangan: bu sahifa
                              `schedule.levelTests` ruxsatida ochiladi, lidlar ro'yxati esa
                              `leads.list` da — faqat daraja testi ruxsati bor xodim tugmani bosib
                              "ruxsatingiz yo'q" sahifasiga tushardi. Ruxsat bo'lmasa "—".
                            */}
                            {r.isDeleted || !r.leadId || !canOpenLead ? (
                              <span className="text-slate-300">—</span>
                            ) : (
                              <button
                                onClick={() => navigate(`/admin/leads?lead=${r.leadId}`)}
                                title="Lidlar bo'limida shu lidni ochish"
                                className="text-xs font-medium text-brand-600 hover:text-brand-700"
                              >
                                Lidni ochish →
                              </button>
                            )}
                          </td>
                        </tr>
                      ))}
                    </tbody>
                  </table>

                  {stats.rowsTotal > stats.rows.length && (
                    <p className="px-3 pt-3 text-xs text-slate-400">
                      Jami {stats.rowsTotal} ta topshiriq — bu yerda eng yangi {stats.rows.length} tasi
                      ko'rsatilmoqda.
                    </p>
                  )}
                </div>
              )}
            </Card>
          </div>
        </>
      )}
    </div>
  )
}
