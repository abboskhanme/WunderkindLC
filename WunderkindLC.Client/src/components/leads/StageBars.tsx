import type { LeadStageCount } from '@/types'
import { LeadStageChip } from './LeadStageChip'

/**
 * «LIDLAR QAYSI BOSQICHDA» — kelgan lidlar HOZIR kanbanning qaysi ustunida turibdi.
 * Savol: "voronka qayerda tiqilib qolgan".
 *
 * <p>Lid formalari statistikasi ham, daraja testi statistikasi ham SHU komponentni chizadi —
 * ikkalasida bir xil ma'lumot (`LeadStageCount`) va bir xil ko'rinish bo'lishi kerak.</p>
 *
 * <p>Chiziq uzunligi ENG KATTA ustunga nisbatan (nisbiy taqqoslash uchun), yonidagi foiz esa
 * BOSQICHDAGI jami lidlardan. Bosqichi yo'q (yoki ustuni o'chirilgan) lid serverda ro'yxatga
 * umuman kirmaydi — kanbanda ham ko'rinmaydi, sun'iy "Noma'lum bosqich" yasalmaydi.</p>
 */
export function StageBars({ items, emptyText }: { items: LeadStageCount[]; emptyText: string }) {
  if (items.length === 0)
    return <p className="py-8 text-center text-sm text-slate-400">{emptyText}</p>

  const max = Math.max(0, ...items.map((s) => s.leads))
  const total = items.reduce((a, s) => a + s.leads, 0)

  return (
    <div className="space-y-2">
      {items.map((st) => (
        // ⚠️ Kalit — `(nom, rang)` juftligidan: server bosqichlarni AYNAN shu juftlik bo'yicha
        // guruhlaydi va `LeadStage.Title` da unikal cheklov YO'Q. Ya'ni kanbanda bir xil nomli,
        // har xil rangli ikkita ustun bo'lsa, yolg'iz `st.stage` takror `key` berardi (React
        // ogohlantirishi + qatorlar noto'g'ri qayta chizilishi).
        <div key={`${st.stage}|${st.color}`} className="flex items-center gap-3">
          <div className="w-40 shrink-0 sm:w-52">
            <LeadStageChip title={st.stage} color={st.color} />
          </div>
          <div className="h-2 flex-1 overflow-hidden rounded-full bg-slate-100">
            <div
              className="h-full rounded-full bg-brand-400 transition-all"
              style={{ width: `${max > 0 ? Math.round((st.leads / max) * 100) : 0}%` }}
            />
          </div>
          <span className="w-16 shrink-0 text-right font-mono text-sm text-slate-700">
            {st.leads}
            {total > 0 && (
              <span className="ml-1 text-[11px] text-slate-400">
                {Math.round((st.leads / total) * 100)}%
              </span>
            )}
          </span>
        </div>
      ))}
    </div>
  )
}
