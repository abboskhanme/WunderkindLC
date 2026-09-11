import type { LeadFunnelStage, LeadManagerRow } from '@/api/services/leads'
import { Card } from '@/components/ui/Card'
import { matrixTint } from './palette'
import { cn } from '@/lib/utils'

/**
 * «KIM QAYSI BOSQICHGACHA OLIB BORDI» — menejer × bosqich matritsasi.
 *
 * <p>Sotuv bo'limining asosiy savoli: xodim lidni qayerga qadar sura oladi. Bitta menejer
 * "Yangi" dan "Aloqada" ga o'tkazishda zo'r bo'lib, "Shartnoma" ga umuman olib bora olmasligi
 * mumkin — bu jadval aynan shuni ochib beradi.</p>
 *
 * <p><b>Nega HEATMAP:</b> bu ikki o'lchovli kesim (menejer × bosqich) — uzunlik uchun joy yo'q,
 * shuning uchun magnituda BITTA tusning pog'onasi bilan beriladi (`matrixTint`). Raqamning O'ZI
 * har katakda yozilgan, ya'ni rang faqat "ko'z bilan tez topish" uchun — hech qanday qiymat
 * faqat rangda qolmaydi.</p>
 *
 * <p>⚠️ <b>Bu VORONKA EMAS:</b> qatordagi sonlar o'ngga qarab kamayib borishi SHART emas —
 * menejer lidni o'rtadagi bosqichdan olib, keyingisiga surgan bo'lishi mumkin. Voronka lidning
 * yo'lini, bu jadval esa XODIMNING ishini ko'rsatadi.</p>
 *
 * <p>⚠️ Bir lidni ikki menejer surgan bo'lsa, HAR BIRI o'zi ko'chirgan bosqich uchun sanaladi —
 * bu takror emas ("kim nima qildi"). Shu sababli ustun yig'indisi voronkadagi son bilan mos
 * kelmasligi mumkin va bu yerda jami qator ATAYIN chizilmaydi.</p>
 */
export function ManagerStageMatrix({
  managers,
  funnel,
}: {
  managers: LeadManagerRow[]
  funnel: LeadFunnelStage[]
}) {
  // Ustunlar — voronka bosqichlari (tartibi server tomonda `Order` bo'yicha berilgan).
  const stages = funnel.map((f) => ({ id: f.stageId, title: f.title }))
  const rows = managers.filter((m) => m.stages.some((s) => s.reached > 0))

  // Rang shkalasi BUTUN jadval bo'yicha bitta — aks holda har qator o'z maksimumiga bo'yalib,
  // kam ishlagan menejer ham "quyuq" ko'rinardi (taqqoslash buzilardi).
  const max = Math.max(0, ...rows.flatMap((m) => m.stages.map((s) => s.reached)))

  return (
    <Card
      title="Kim qaysi bosqichgacha olib bordi"
      sub="Menejer shu bosqichga olib kelgan takrorsiz lidlar soni — sotuv bo'limining KPI asosi"
    >
      {rows.length === 0 || stages.length === 0 ? (
        <div className="py-10 text-center">
          <p className="text-sm text-slate-500">Bosqichlar bo'yicha ma'lumot yo'q</p>
          <p className="mx-auto mt-1 max-w-md text-xs text-slate-400">
            Bu xato emas: tanlangan davrda lid bosqichini ko'chirgan xodim qayd etilmagan.
            Davrni kengaytirib ko'ring.
          </p>
        </div>
      ) : (
        <>
          <div className="overflow-x-auto">
            <table className="w-full text-left text-sm">
              <thead className="border-b border-slate-100 text-xs uppercase tracking-wide text-slate-400">
                <tr>
                  <th className="sticky left-0 bg-white py-2 pr-3 font-medium">Menejer</th>
                  {stages.map((s) => (
                    <th key={s.id} className="px-2 py-2 text-center font-medium">
                      {s.title}
                    </th>
                  ))}
                  <th className="py-2 pl-3 text-right font-medium">Aylantirdi</th>
                </tr>
              </thead>
              <tbody>
                {rows.map((m) => {
                  const byStage = new Map(m.stages.map((s) => [s.stageId, s.reached]))
                  return (
                    <tr key={m.userId} className="border-b border-slate-50 last:border-0">
                      <td className="sticky left-0 bg-white py-1.5 pr-3 font-medium text-slate-700">
                        {m.name || m.userId}
                      </td>
                      {stages.map((s) => {
                        const v = byStage.get(s.id) ?? 0
                        return (
                          <td key={s.id} className="px-1 py-1 text-center">
                            <div
                              className={cn(
                                'rounded-md py-1.5 font-mono tabular-nums',
                                v > 0 ? 'text-slate-800' : 'text-slate-300',
                              )}
                              style={{ backgroundColor: matrixTint(v, max) }}
                              title={`${m.name}: ${s.title} — ${v} ta lid`}
                            >
                              {v > 0 ? v : '—'}
                            </div>
                          </td>
                        )
                      })}
                      <td className="py-1.5 pl-3 text-right font-mono tabular-nums font-semibold text-emerald-600">
                        {m.won.toLocaleString()}
                      </td>
                    </tr>
                  )
                })}
              </tbody>
            </table>
          </div>
          <p className="mt-3 text-xs text-slate-400">
            Katak rangi — shu jadvaldagi eng katta songa nisbatan. Bir lidni ikki xodim surgan
            bo'lsa, har biri O'ZI ko'chirgan bosqich uchun sanaladi, shuning uchun ustun
            yig'indisi voronkadagi son bilan mos kelmasligi mumkin.
          </p>
        </>
      )}
    </Card>
  )
}
