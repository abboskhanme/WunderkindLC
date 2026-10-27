import { lazy, Suspense, useState, type CSSProperties, type ReactNode } from 'react'
import { ArrowLeft, Lock, RefreshCw } from 'lucide-react'
import type { WikiPage } from '@/api/services/wiki'
import { Button } from '@/components/ui/Button'
import { Loader } from '@/components/ui/Loader'
import { cn } from '@/lib/utils'
import type { useWikiPage, SaveState } from '../lib/useWikiPage'
import { formatStamp, readFontSize, stepFontSize, writeFontSize } from '../lib/wikiModel'
import '../lib/wiki.css'

// Muharrir (~70 KB gzip) ALOHIDA chunk: daraxt darhol chiziladi, muharrir orqadan keladi.
const WikiEditor = lazy(() => import('./WikiEditor'))

const SAVE_LABEL: Record<SaveState, string> = {
  idle: '',
  dirty: 'Saqlanmagan o‘zgarishlar…',
  saving: 'Saqlanmoqda…',
  saved: 'Saqlandi ✓',
  error: '',
  conflict: '',
}

interface WikiPageViewProps {
  view: ReturnType<typeof useWikiPage>
  /** Sahifa faqat o'qish uchunmi (ruxsat yo'q, qulflangan yoki xodim eslatmasi) */
  readOnly: (page: WikiPage) => boolean
  /** Faqat o'qish sababi — sarlavha ostida ko'rsatiladi ("Qulflangan", "Xodim eslatmasi") */
  readOnlyNote?: (page: WikiPage) => string
  /** Yuqori qatordagi yo'l (bo'lim / otalar) */
  breadcrumb: (page: WikiPage) => ReactNode
  /** Yuqori qatordagi qo'shimcha tugmalar (masalan qulf) */
  actions?: (page: WikiPage) => ReactNode
  /** Mobil: daraxtga qaytish */
  onBack: () => void
}

/**
 * O'NG PANEL — bitta sahifa: yo'l, saqlash holati, shrift o'lchami (A− / A+), sarlavha va muharrir.
 * Umumiy bo'limlar, «Mening eslatmalarim» va xodim eslatmalari (faqat o'qish) — hammasi SHU komponent.
 * Holat va avtosaqlash — `useWikiPage` (ota komponentda, chunki daraxt ham undan foydalanadi).
 */
export function WikiPageView({ view, readOnly, readOnlyNote, breadcrumb, actions, onBack }: WikiPageViewProps) {
  const [fontSize, setFontSize] = useState(readFontSize)
  const changeFont = (dir: 1 | -1) => {
    const next = stepFontSize(fontSize, dir)
    setFontSize(next)
    writeFontSize(next)
  }

  if (view.error)
    return (
      <div className="flex flex-1 flex-col items-center justify-center gap-3 p-8 text-center">
        <div className="text-[14px] text-[#d32f2f]">{view.error}</div>
        <Button variant="secondary" onClick={onBack}>Orqaga</Button>
      </div>
    )
  const page = view.page
  if (!page) return <Loader className="h-full" />

  const ro = readOnly(page)
  const note = ro ? readOnlyNote?.(page) : ''
  const { save } = view

  return (
    <>
      {/* Yuqori qator: yo'l, saqlash holati, shrift, qo'shimcha tugmalar */}
      <div className="flex min-h-11 items-center gap-2 border-b border-slate-100 px-3 text-[12.5px] text-slate-500">
        <button type="button" onClick={onBack} className="lg:hidden" aria-label="Orqaga">
          <ArrowLeft size={16} />
        </button>
        <div className="flex min-w-0 flex-1 items-center gap-1 truncate">{breadcrumb(page)}</div>
        <span className={cn('shrink-0', save.state === 'saved' && 'text-emerald-600')}>{SAVE_LABEL[save.state]}</span>
        <div className="flex shrink-0 items-center rounded-md border border-slate-200" title="Shrift o'lchami">
          <button type="button" onClick={() => changeFont(-1)} className="h-7 w-8 text-[12px] hover:bg-slate-50" aria-label="Shriftni kichraytirish">A−</button>
          <span className="w-7 border-x border-slate-200 text-center text-[11.5px] tabular-nums">{fontSize}</span>
          <button type="button" onClick={() => changeFont(1)} className="h-7 w-8 text-[14px] hover:bg-slate-50" aria-label="Shriftni kattalashtirish">A+</button>
        </div>
        {actions?.(page)}
      </div>

      {(save.state === 'error' || save.state === 'conflict') && (
        <div className="flex items-center gap-2 border-b border-red-100 bg-red-50 px-4 py-2 text-[13px] text-[#d32f2f]">
          <span className="flex-1">{save.message}</span>
          {save.state === 'error' ? (
            <Button variant="secondary" className="h-7 px-2 text-[12.5px]" onClick={view.retry}>Qayta urinish</Button>
          ) : (
            <Button variant="secondary" className="h-7 px-2 text-[12.5px]" onClick={view.reload}>
              <RefreshCw size={13} className="mr-1 inline" /> Sahifani yangilash
            </Button>
          )}
        </div>
      )}

      <div className="flex-1 overflow-y-auto">
        <div className="wiki-doc mx-auto max-w-[820px] px-4 pb-24 pt-10 sm:px-10" style={{ '--wiki-font-size': `${fontSize}px` } as CSSProperties}>
          <input
            value={view.title}
            readOnly={ro}
            maxLength={200}
            placeholder="Nomsiz sahifa"
            onChange={(e) => view.setTitle(e.target.value)}
            className="wiki-title mb-1 w-full bg-transparent px-[54px] outline-none placeholder:text-slate-300"
          />
          <div className="mb-5 flex flex-wrap items-center gap-x-2 gap-y-1 px-[54px] text-[12px] text-slate-400">
            <span>Oxirgi o'zgarish: {page.updatedBy} · {formatStamp(page.updatedAt)}</span>
            {note && (
              <span className="inline-flex items-center gap-1 rounded-full bg-amber-50 px-2 py-0.5 text-amber-700">
                <Lock size={11} /> {note}
              </span>
            )}
          </div>
          <Suspense fallback={<Loader className="h-40" />}>
            <WikiEditor
              // Versiya kalitda: 409 dan keyin qayta yuklanganda muharrir YANGI matn bilan qayta yaratiladi
              // (avtosaqlashda versiya ATAYIN yangilanmaydi — yozayotganda muharrir qayta tug'ilmasin).
              key={`${page.id}:${page.version}`}
              initialJson={page.contentJson}
              editable={!ro}
              onChange={view.setContent}
            />
          </Suspense>
        </div>
      </div>
    </>
  )
}
