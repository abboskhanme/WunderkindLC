/**
 * MASHQ PLEYERLARI — "foydalanuvchi ko'rinishi" (maketdagi o'ng paneldagi jonli ekran).
 *
 * BIR XIL komponentlar ikki joyda ishlatiladi:
 *   1) konstruktorda — o'qituvchi kiritayotgan mashqni darhol sinab ko'rishi uchun (preview);
 *   2) o'quvchi portalida — mashqni haqiqatan yechish uchun (solve).
 * Farqi faqat `mode`: preview'da element chapdagi ro'yxatdan tanlanadi, solve'da esa ketma-ket
 * o'tiladi ("Keyingi") va oxirida natija qaytariladi.
 */
import { useEffect, useMemo, useRef, useState } from 'react'
import type { CSSProperties, MutableRefObject, ReactNode } from 'react'
// Konstruktor CSS'i (`.dc-*`) shu yerda ham — pleyer o'quvchi portalida mustaqil ishlatiladi
import '@/styles/exercise.css'
import { display, sans, kindTheme } from './catalog'
import type { Theme } from './catalog'
import { PlayButton, ResultBar } from './kit'
import {
  answerMatches, blankGlyph, colLetter, exerciseCount, fillMode, kindFamily, kindMedia,
  parsePickText, splitBlanks, words,
} from './model'
import type { ExerciseData, ExerciseKind } from './model'

// ============================ Umumiy ============================

/** Bitta element bo'yicha o'quvchi javobi — mashq yakunlanganda serverga yuboriladi va
 *  `CourseItemAttempt.AnswersJson` da saqlanadi (o'qituvchi "qayerda xato qildi" ni ko'radi). */
export interface ExerciseAnswerLog {
  /** Element tartib raqami (0-asosli). */
  index: number
  /** Savol / topshiriq matni. */
  prompt: string
  /** O'quvchi bergan javob. */
  answer: string
  /** To'g'ri javob. */
  expected: string
  ok: boolean
  /** Shu elementga sarflangan vaqt (soniya). */
  sec: number
}

export interface PlayerProps {
  data: ExerciseData
  /** Joriy element (0-asosli). Konstruktorda chapdagi tanlov bilan boshqariladi. */
  index: number
  onIndex?: (i: number) => void
  /** preview — konstruktor; solve — o'quvchi (ketma-ket o'tish + yakuniy natija). */
  mode?: 'preview' | 'solve'
  onFinish?: (correct: number, total: number, answers: ExerciseAnswerLog[]) => void
}

/** Mashqni ishlash jarayoni — element almashganda YO'QOLMASLIGI uchun ref'da (dispatcher'da).
 *  Javoblar Map'da INDEKS bo'yicha saqlanadi: o'quvchi ↺ bosib qayta tekshirsa, eski yozuv
 *  almashadi (ilgari hisoblagich ikki marta oshib ketardi). */
interface RunState {
  answers: Map<number, ExerciseAnswerLog>
  /** Joriy element boshlangan vaqt (ms) — sarflangan soniyani hisoblash uchun. */
  itemStart: number
}

/** Ichki pleyerlar props'i. Pleyerning o'zi har element uchun `key` bilan QAYTA YARATILADI —
 *  javob holati (tanlov, terilgan so'zlar) shu tarzda tozalanadi (effekt bilan emas: effekt
 *  ortiqcha render va bir lahzalik eski holatni ko'rsatishga olib kelardi). */
interface InnerProps extends PlayerProps {
  runRef: MutableRefObject<RunState>
}

/** Ref'dagi javoblardan yakuniy ro'yxat (indeks bo'yicha tartiblangan) va to'g'rilar soni. */
function collect(run: RunState): { answers: ExerciseAnswerLog[]; correct: number } {
  const answers = [...run.answers.values()].sort((a, b) => a.index - b.index)
  return { answers, correct: answers.filter((a) => a.ok).length }
}

/** BIR elementli mashqlar (writing / speaking / matching) uchun yagona javob yozuvi. */
function singleLog(
  run: RunState, ok: boolean, prompt: string, answer: string, expected: string,
): ExerciseAnswerLog[] {
  return [{ index: 0, prompt, answer, expected, ok, sec: Math.max(0, Math.round((Date.now() - run.itemStart) / 1000)) }]
}

/** Barqaror (id bo'yicha) aralashtirish — har renderda tartib o'zgarib ketmasligi uchun. */
function shuffled<T>(list: T[], seed: string): T[] {
  let h = 0
  for (let i = 0; i < seed.length; i++) h = (h * 31 + seed.charCodeAt(i)) | 0
  const out = [...list]
  for (let i = out.length - 1; i > 0; i--) {
    h = (h * 1103515245 + 12345) & 0x7fffffff
    const j = h % (i + 1)
    ;[out[i], out[j]] = [out[j], out[i]]
  }
  return out
}

/** Telefon ichidagi yuqori qism (progress + bo'lim nomi). */
function Head({ theme, progress, label, caption }: { theme: Theme; progress: number; label: string; caption: string }) {
  return (
    <div style={{ padding: '16px 18px 12px', borderBottom: `1px solid ${theme.line}` }}>
      <div style={{ display: 'flex', alignItems: 'center', gap: 10, marginBottom: 12 }}>
        <div style={{ width: 28, height: 28, borderRadius: 8, background: theme.head, display: 'flex', alignItems: 'center', justifyContent: 'center' }}>
          <svg width="15" height="15" viewBox="0 0 24 24" fill="none" stroke={theme.accent} strokeWidth={2.4} strokeLinecap="round">
            <path d="M15 18l-6-6 6-6" />
          </svg>
        </div>
        <div style={{ flex: 1, height: 8, borderRadius: 4, background: '#f7f5f1', overflow: 'hidden' }}>
          <div style={{ width: `${Math.max(0, Math.min(100, progress))}%`, height: '100%', background: theme.accent, borderRadius: 4, transition: 'width .2s ease' }} />
        </div>
        <span style={{ fontSize: 12, fontWeight: 600, color: '#9aa0aa' }}>{label}</span>
      </div>
      <div style={{ fontSize: 12, fontWeight: 600, letterSpacing: '.03em', textTransform: 'uppercase', color: theme.caption }}>{caption}</div>
    </div>
  )
}

/** Tarjima / izoh qatori. */
function Hint({ theme, text }: { theme: Theme; text?: string }) {
  if (!text) return null
  return (
    <div style={{ display: 'flex', alignItems: 'center', gap: 9, background: theme.soft, borderRadius: 12, padding: '11px 13px' }}>
      <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke={theme.caption} strokeWidth={2} strokeLinecap="round">
        <circle cx="12" cy="12" r="9" />
        <path d="M12 8h.01M11 12h1v4h1" />
      </svg>
      <span style={{ fontSize: 14, color: '#4a4d56', fontStyle: 'italic' }}>{text}</span>
    </div>
  )
}

function Media({ theme, kind, imageUrl, audioUrl }: { theme: Theme; kind: ExerciseKind; imageUrl?: string; audioUrl?: string }) {
  const media = kindMedia(kind)
  if (media === 'none') return null

  const picture = (
    <div style={{ width: '100%', aspectRatio: '16 / 10', borderRadius: 14, overflow: 'hidden', border: `1px solid ${theme.line}`, background: theme.soft, display: 'flex', alignItems: 'center', justifyContent: 'center' }}>
      {imageUrl ? (
        <img src={imageUrl} alt="" style={{ width: '100%', height: '100%', objectFit: 'cover' }} />
      ) : (
        <span style={{ fontSize: 13, color: theme.caption }}>Rasm yuklanmagan</span>
      )}
    </div>
  )
  const player = <PlayButton accent={theme.accent} tint={theme.soft} url={audioUrl} />

  if (media === 'image') return picture
  if (media === 'audio') return player
  // 'both' — avval rasm, ostida audio (masalan "Bo'sh joy · audio + rasm").
  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: 12 }}>
      {picture}
      {player}
    </div>
  )
}

/** Preview/solve pastidagi tugmalar qatori. */
function Actions({
  theme, checked, onCheck, onReset, onNext, mode, last, disabled,
}: {
  theme: Theme
  checked: boolean
  onCheck: () => void
  onReset: () => void
  onNext?: () => void
  mode: 'preview' | 'solve'
  last: boolean
  disabled?: boolean
}) {
  const showNext = mode === 'solve' && checked
  return (
    <div style={{ display: 'flex', gap: 10, marginTop: 'auto' }}>
      <button
        type="button"
        onClick={onReset}
        title="Qayta boshlash"
        style={{ ...sans, flex: 'none', background: theme.line, border: 'none', color: '#4a4d56', fontWeight: 600, fontSize: 15, padding: '14px 16px', borderRadius: 13, cursor: 'pointer' }}
      >
        ↺
      </button>
      <button
        type="button"
        onClick={showNext ? onNext : onCheck}
        disabled={disabled && !showNext}
        style={{
          ...sans, flex: 1, background: disabled && !showNext ? '#9aa0aa' : theme.accent, border: 'none', color: '#fff',
          fontWeight: 700, fontSize: 15, padding: '14px 16px', borderRadius: 13, cursor: disabled && !showNext ? 'default' : 'pointer',
        }}
      >
        {showNext ? (last ? 'Yakunlash' : 'Keyingi') : 'Tekshirish'}
      </button>
    </div>
  )
}

const bodyStyle: CSSProperties = { padding: '18px 18px 20px', display: 'flex', flexDirection: 'column', gap: 16, flex: 1 }

/** Bo'sh mashq holati. */
function EmptyState({ theme, text }: { theme: Theme; text: string }) {
  return (
    <div style={{ ...bodyStyle, alignItems: 'center', justifyContent: 'center', textAlign: 'center' }}>
      <span style={{ fontSize: 14, color: theme.caption }}>{text}</span>
    </div>
  )
}

/** Ko'p elementli mashqlarda umumiy holat (joriy element, natija, keyingiga o'tish).
 *  `finish(ok, detail)` — natijani BELGILAYDI va o'quvchi javobini ref'ga yozadi (tarix uchun). */
function useRunner(total: number, props: InnerProps) {
  const { index, onIndex, mode = 'preview', onFinish, runRef } = props
  const [checked, setChecked] = useState<boolean | null>(null)

  const finish = (ok: boolean, detail?: { prompt?: string; answer?: string; expected?: string }) => {
    setChecked(ok)
    const run = runRef.current
    run.answers.set(index, {
      index,
      prompt: detail?.prompt ?? '',
      answer: detail?.answer ?? '',
      expected: detail?.expected ?? '',
      ok,
      sec: Math.max(0, Math.round((Date.now() - run.itemStart) / 1000)),
    })
    run.itemStart = Date.now()
  }

  const next = () => {
    if (index + 1 >= total) {
      const { answers, correct } = collect(runRef.current)
      onFinish?.(correct, total, answers)
      if (mode === 'solve') return
    }
    runRef.current.itemStart = Date.now()
    onIndex?.(Math.min(total - 1, index + 1))
  }

  return { checked, setChecked, finish, next, last: index + 1 >= total, mode }
}

// ============================ 1. Gap tuzish (so'z tartibi) ============================

function SentencePlayer(props: InnerProps) {
  const { data, index } = props
  const theme = kindTheme(data.kind)
  const items = data.sentence?.items ?? []
  const item = items[index]
  const runner = useRunner(items.length, props)
  const [placed, setPlaced] = useState<number[]>([])

  const bank = useMemo(() => (item ? shuffled(words(item.text).map((w, i) => ({ i, w })), item.id) : []), [item])

  if (!item) return <EmptyState theme={theme} text="Hali gap qo'shilmadi" />

  const correct = words(item.text)
  const check = () => {
    const answer = placed.map((i) => correct[i])
    runner.finish(answer.length === correct.length && answer.every((w, i) => w === correct[i]), {
      prompt: item.translation || 'Gapni tuzing',
      answer: answer.join(' '),
      expected: item.text,
    })
  }
  const reset = () => {
    setPlaced([])
    runner.setChecked(null)
  }

  return (
    <>
      <Head theme={theme} progress={((index + 1) / items.length) * 100} label={`${index + 1}/${items.length}`} caption="Gapni tuzing" />
      <div style={bodyStyle}>
        <Media theme={theme} kind={data.kind} imageUrl={item.imageUrl} audioUrl={item.audioUrl} />
        <Hint theme={theme} text={item.translation} />

        <div style={{ minHeight: 88, borderBottom: `2px solid ${theme.line}`, paddingBottom: 14, display: 'flex', flexWrap: 'wrap', gap: 9, alignContent: 'flex-start' }}>
          {placed.map((wi, pos) => (
            <button
              key={`${wi}-${pos}`}
              type="button"
              className="dc-tile dc-pop"
              onClick={() => setPlaced((p) => p.filter((_, i) => i !== pos))}
              style={{ background: theme.accent, border: 'none', color: '#fff', borderRadius: 11, padding: '11px 15px', ...sans, fontSize: 16, fontWeight: 600, cursor: 'pointer', boxShadow: `0 4px 10px -3px ${theme.accent}99` }}
            >
              {correct[wi]}
            </button>
          ))}
          {placed.length === 0 && <span style={{ fontSize: 14, color: '#cfd3ff', padding: '8px 2px' }}>So'zlarni bu yerga terib chiqing…</span>}
        </div>

        <div style={{ display: 'flex', flexWrap: 'wrap', gap: 9, flex: 1, alignContent: 'flex-start' }}>
          {bank
            .filter((b) => !placed.includes(b.i))
            .map((b) => (
              <button
                key={b.i}
                type="button"
                className="dc-tile"
                onClick={() => setPlaced((p) => [...p, b.i])}
                style={{ background: '#fff', border: '1.5px solid #e3e4e8', color: '#4a4d56', borderRadius: 11, padding: '11px 15px', ...sans, fontSize: 16, fontWeight: 600, cursor: 'pointer', boxShadow: '0 2px 5px -2px rgba(40,30,80,.15)' }}
              >
                {b.w}
              </button>
            ))}
          {placed.length === bank.length && bank.length > 0 && (
            <span style={{ fontSize: 14, color: '#cfd3ff', padding: '8px 2px' }}>Barcha so'zlar ishlatildi</span>
          )}
        </div>

        {runner.checked !== null && <ResultBar ok={runner.checked} text={runner.checked ? "To'g'ri! 🎉" : `To'g'ri javob: ${item.text}`} />}
        <Actions theme={theme} checked={runner.checked !== null} onCheck={check} onReset={reset} onNext={runner.next} mode={runner.mode} last={runner.last} disabled={placed.length === 0} />
      </div>
    </>
  )
}

// ============================ 2. Gap tuzish · variant tanlash ============================

function SentenceChoicePlayer(props: InnerProps) {
  const { data, index } = props
  const theme = kindTheme(data.kind)
  const items = data.sentenceChoice?.items ?? []
  const item = items[index]
  const runner = useRunner(items.length, props)
  const [picked, setPicked] = useState<string | null>(null)
  const options = useMemo(() => (item ? shuffled(item.options, item.id) : []), [item])

  if (!item) return <EmptyState theme={theme} text="Hali savol qo'shilmadi" />

  return (
    <>
      <Head theme={theme} progress={((index + 1) / items.length) * 100} label={`${index + 1}/${items.length}`} caption="To'g'ri gapni tanlang" />
      <div style={bodyStyle}>
        <div style={{ background: theme.soft, borderRadius: 12, padding: '12px 14px' }}>
          <div style={{ fontSize: 11, fontWeight: 700, letterSpacing: '.05em', textTransform: 'uppercase', color: theme.caption, marginBottom: 4 }}>Savol</div>
          <div style={{ fontSize: 17, fontWeight: 600, color: '#181a22' }}>{item.prompt}</div>
        </div>

        <div style={{ display: 'flex', flexDirection: 'column', gap: 10 }}>
          {options.map((o) => {
            const on = picked === o.id
            const isRight = runner.checked !== null && o.id === item.correctId
            const isWrong = runner.checked === false && on
            return (
              <button
                key={o.id}
                type="button"
                className="dc-opt"
                onClick={() => {
                  setPicked(o.id)
                  runner.setChecked(null)
                }}
                style={{
                  display: 'flex', alignItems: 'center', gap: 10, textAlign: 'left', ...sans, fontSize: 15,
                  fontWeight: on || isRight ? 600 : 500, borderRadius: 13, padding: '13px 14px', cursor: 'pointer',
                  background: isRight ? '#e7f6ee' : isWrong ? '#fdecec' : on ? theme.soft : '#fff',
                  border: `1.5px solid ${isRight ? '#169f65' : isWrong ? '#de3b3d' : on ? theme.accent : '#e3e4e8'}`,
                  color: '#4a4d56',
                }}
              >
                <span style={{ width: 14, height: 14, borderRadius: '50%', flex: 'none', border: on ? `4px solid ${theme.accent}` : '1.5px solid #cfd3ff' }} />
                {o.text}
              </button>
            )
          })}
        </div>

        {runner.checked !== null && <ResultBar ok={runner.checked} text={runner.checked ? "To'g'ri! 🎉" : "Xato — to'g'ri javob belgilandi"} />}
        <Actions
          theme={theme}
          checked={runner.checked !== null}
          onCheck={() =>
            runner.finish(picked !== null && picked === item.correctId, {
              prompt: item.prompt,
              answer: item.options.find((o) => o.id === picked)?.text ?? '',
              expected: item.options.find((o) => o.id === item.correctId)?.text ?? '',
            })
          }
          onReset={() => {
            setPicked(null)
            runner.setChecked(null)
          }}
          onNext={runner.next}
          mode={runner.mode}
          last={runner.last}
          disabled={!picked}
        />
      </div>
    </>
  )
}

// ============================ 3. Bo'sh joyni to'ldirish ============================

function FillPlayer(props: InnerProps) {
  const { data, index } = props
  const theme = kindTheme(data.kind)
  const items = data.fill?.items ?? []
  const blank = data.fill?.blank ?? 'line'
  const item = items[index]
  const runner = useRunner(items.length, props)
  const [picked, setPicked] = useState<string | null>(null)
  const [typed, setTyped] = useState('')
  const options = useMemo(() => (item ? shuffled(item.options, item.id) : []), [item])

  if (!item) return <EmptyState theme={theme} text="Hali savol qo'shilmadi" />

  const mode = fillMode(data.kind)
  const parts = splitBlanks(item.text)
  const filledText = mode === 'choose' ? item.options.find((o) => o.id === picked)?.text ?? '' : typed

  const check = () => {
    const detail = {
      prompt: item.text,
      answer: mode === 'choose' ? item.options.find((o) => o.id === picked)?.text ?? '' : typed,
      expected: mode === 'choose' ? item.options.find((o) => o.id === item.correctId)?.text ?? '' : item.answer,
    }
    if (mode === 'choose') runner.finish(picked !== null && picked === item.correctId, detail)
    else runner.finish(answerMatches(item.answer, typed), detail)
  }

  return (
    <>
      <Head theme={theme} progress={((index + 1) / items.length) * 100} label={`${index + 1}/${items.length}`} caption="Bo'sh joyni to'ldiring" />
      <div style={{ ...bodyStyle, padding: '20px 18px' }}>
        <Media theme={theme} kind={data.kind} imageUrl={item.imageUrl} audioUrl={item.audioUrl} />
        <Hint theme={theme} text={item.translation} />

        <div style={{ fontSize: 19, lineHeight: 1.9, fontWeight: 500, color: '#181a22' }}>
          {parts.map((p, i) => (
            <span key={i}>
              {p}
              {i < parts.length - 1 && (
                <span
                  style={{
                    display: 'inline-block', minWidth: 74, textAlign: 'center', margin: '0 4px', fontWeight: 700,
                    color: filledText ? theme.accent : '#cfd3ff',
                    borderBottom: `2px solid ${theme.accent}`,
                  }}
                >
                  {filledText || blankGlyph(blank)}
                </span>
              )}
            </span>
          ))}
        </div>

        {mode === 'choose' ? (
          <div style={{ display: 'flex', flexDirection: 'column', gap: 10, marginTop: 4 }}>
            {options.map((o) => {
              const on = picked === o.id
              const isRight = runner.checked !== null && o.id === item.correctId
              const isWrong = runner.checked === false && on
              return (
                <button
                  key={o.id}
                  type="button"
                  className="dc-opt"
                  onClick={() => {
                    setPicked(o.id)
                    runner.setChecked(null)
                  }}
                  style={{
                    display: 'flex', alignItems: 'center', gap: 10, textAlign: 'left', ...sans, fontSize: 15, fontWeight: 600,
                    borderRadius: 13, padding: '13px 14px', cursor: 'pointer', color: '#181a22',
                    background: isRight ? '#e7f6ee' : isWrong ? '#fdecec' : on ? theme.soft : '#fff',
                    border: `1.5px solid ${isRight ? '#169f65' : isWrong ? '#de3b3d' : on ? theme.accent : '#e3e4e8'}`,
                  }}
                >
                  <span style={{ width: 13, height: 13, borderRadius: '50%', flex: 'none', border: on ? `4px solid ${theme.accent}` : '1.5px solid #cfd3ff' }} />
                  {o.text}
                </button>
              )
            })}
          </div>
        ) : (
          <input
            value={typed}
            onChange={(e) => {
              setTyped(e.target.value)
              runner.setChecked(null)
            }}
            onKeyDown={(e) => {
              if (e.key === 'Enter') check()
            }}
            placeholder="Javobni yozing…"
            style={{ ...sans, width: '100%', fontSize: 17, fontWeight: 600, color: '#181a22', background: theme.phone, border: `1.6px solid ${theme.phoneBorder}`, borderRadius: 13, padding: '14px 16px', outline: 'none' }}
          />
        )}

        {runner.checked !== null && (
          <ResultBar
            ok={runner.checked}
            text={runner.checked ? "To'g'ri! 🎉" : `To'g'ri javob: ${mode === 'choose' ? item.options.find((o) => o.id === item.correctId)?.text ?? '—' : item.answer}`}
          />
        )}
        <Actions theme={theme} checked={runner.checked !== null} onCheck={check} onReset={() => { setPicked(null); setTyped(''); runner.setChecked(null) }} onNext={runner.next} mode={runner.mode} last={runner.last} disabled={mode === 'choose' ? !picked : !typed.trim()} />
      </div>
    </>
  )
}

// ============================ 4. So'z tanlash (gap ichida) ============================

function WordPickPlayer(props: InnerProps) {
  const { data, index } = props
  const theme = kindTheme(data.kind)
  const items = data.wordpick?.items ?? []
  const item = items[index]
  const runner = useRunner(items.length, props)
  const [sel, setSel] = useState<Record<number, number>>({})

  if (!item) return <EmptyState theme={theme} text="Hali gap qo'shilmadi" />

  const tokens = parsePickText(item.text)
  const groups = tokens.filter((t) => t.kind === 'group')

  const check = () => {
    const ok = groups.every((g) => {
      const chosen = sel[g.groupIndex ?? 0]
      return chosen !== undefined && g.options?.[chosen]?.correct
    })
    runner.finish(groups.length > 0 && ok, {
      // Gapdagi variantlar `(bir/*ikki)` shaklida yozilgan — o'qish uchun `[…]` bilan almashtiramiz.
      prompt: tokens.map((t) => (t.kind === 'text' ? t.text : '[…]')).join(''),
      answer: groups.map((g) => g.options?.[sel[g.groupIndex ?? 0]]?.text ?? '—').join(', '),
      expected: groups.map((g) => g.options?.find((o) => o.correct)?.text ?? '—').join(', '),
    })
  }

  return (
    <>
      <Head theme={theme} progress={((index + 1) / items.length) * 100} label={`${index + 1}/${items.length}`} caption="To'g'ri so'zni tanlang" />
      <div style={{ ...bodyStyle, padding: '20px 18px' }}>
        <Media theme={theme} kind={data.kind} imageUrl={item.imageUrl} audioUrl={item.audioUrl} />
        <Hint theme={theme} text={item.translation} />

        <div style={{ fontSize: 18, lineHeight: 2.5, color: '#181a22' }}>
          {tokens.map((t, i) =>
            t.kind === 'text' ? (
              <span key={i}>{t.text}</span>
            ) : (
              <span key={i} style={{ display: 'inline-flex', gap: 5, verticalAlign: 'middle', margin: '0 4px' }}>
                {t.options?.map((o, oi) => {
                  const gi = t.groupIndex ?? 0
                  const on = sel[gi] === oi
                  const reveal = runner.checked !== null
                  const good = reveal && o.correct
                  const bad = reveal && on && !o.correct
                  return (
                    <button
                      key={oi}
                      type="button"
                      className="dc-tile"
                      onClick={() => {
                        setSel((s) => ({ ...s, [gi]: oi }))
                        runner.setChecked(null)
                      }}
                      style={{
                        ...sans, fontSize: 15, fontWeight: 700, borderRadius: 9, padding: '5px 12px', cursor: 'pointer',
                        background: good ? '#169f65' : bad ? '#de3b3d' : on ? theme.accent : '#fff',
                        border: `1.5px solid ${good ? '#169f65' : bad ? '#de3b3d' : on ? theme.accent : theme.phoneBorder}`,
                        color: good || bad || on ? '#fff' : '#4a4d56',
                      }}
                    >
                      {o.text}
                    </button>
                  )
                })}
              </span>
            ),
          )}
        </div>

        {runner.checked !== null && <ResultBar ok={runner.checked} text={runner.checked ? "To'g'ri! 🎉" : "Xato — to'g'ri so'zlar yashil bilan belgilandi"} />}
        <Actions theme={theme} checked={runner.checked !== null} onCheck={check} onReset={() => { setSel({}); runner.setChecked(null) }} onNext={runner.next} mode={runner.mode} last={runner.last} disabled={Object.keys(sel).length === 0} />
      </div>
    </>
  )
}

// ============================ 5. So'z topish (bo'sh joylarga so'z terish) ============================

function WordFindPlayer(props: InnerProps) {
  const { data, index } = props
  const theme = kindTheme(data.kind)
  const items = data.wordfind?.items ?? []
  const blank = data.wordfind?.blank ?? 'line'
  const item = items[index]
  const runner = useRunner(items.length, props)
  const [placed, setPlaced] = useState<(string | null)[]>(() =>
    item ? new Array(Math.max(1, splitBlanks(item.text).length - 1)).fill(null) : [],
  )

  const pool = useMemo(
    () => (item ? shuffled([...item.answers, ...item.distractors].map((w, i) => ({ id: `${i}-${w}`, w })), item.id) : []),
    [item],
  )

  if (!item) return <EmptyState theme={theme} text="Hali savol qo'shilmadi" />

  const parts = splitBlanks(item.text)
  const usedIds = placed.filter(Boolean) as string[]

  const place = (id: string) => {
    const slot = placed.findIndex((p) => p === null)
    if (slot === -1) return
    setPlaced((p) => p.map((v, i) => (i === slot ? id : v)))
    runner.setChecked(null)
  }
  const clearSlot = (i: number) => {
    setPlaced((p) => p.map((v, j) => (j === i ? null : v)))
    runner.setChecked(null)
  }
  const textOf = (id: string | null) => pool.find((p) => p.id === id)?.w ?? ''
  const check = () =>
    runner.finish(placed.every((id, i) => textOf(id).toLowerCase() === (item.answers[i] ?? '').toLowerCase()), {
      prompt: item.text,
      answer: placed.map((id) => textOf(id) || '—').join(', '),
      expected: item.answers.join(', '),
    })

  return (
    <>
      <Head theme={theme} progress={((index + 1) / items.length) * 100} label={`${index + 1}/${items.length}`} caption="Gapga mos so'zlarni toping" />
      <div style={{ ...bodyStyle, padding: '20px 18px' }}>
        <Media theme={theme} kind={data.kind} imageUrl={item.imageUrl} audioUrl={item.audioUrl} />
        <Hint theme={theme} text={item.translation} />

        <div style={{ fontSize: 18, lineHeight: 2.2, color: '#181a22' }}>
          {parts.map((p, i) => (
            <span key={i}>
              {p}
              {i < parts.length - 1 && (
                <button
                  type="button"
                  onClick={() => clearSlot(i)}
                  style={{
                    display: 'inline-block', minWidth: 70, margin: '0 4px', padding: '3px 10px', borderRadius: 8, cursor: 'pointer',
                    ...sans, fontSize: 16, fontWeight: 700,
                    background: placed[i] ? theme.accent : theme.head,
                    border: `1.4px dashed ${theme.accent}`,
                    color: placed[i] ? '#fff' : 'transparent',
                  }}
                >
                  {placed[i] ? textOf(placed[i]) : blankGlyph(blank)}
                </button>
              )}
            </span>
          ))}
        </div>

        <div style={{ display: 'flex', flexWrap: 'wrap', gap: 8 }}>
          {pool
            .filter((p) => !usedIds.includes(p.id))
            .map((p) => (
              <button
                key={p.id}
                type="button"
                className="dc-tile"
                onClick={() => place(p.id)}
                style={{ ...sans, fontSize: 15, fontWeight: 600, color: '#4a4d56', background: '#fff', border: `1.2px solid ${theme.phoneBorder}`, borderRadius: 9, padding: '9px 13px', cursor: 'pointer' }}
              >
                {p.w}
              </button>
            ))}
          {usedIds.length === pool.length && pool.length > 0 && (
            <span style={{ fontSize: 14, color: '#cfd3ff', padding: '8px 2px' }}>Barcha so'zlar ishlatildi</span>
          )}
        </div>

        {runner.checked !== null && <ResultBar ok={runner.checked} text={runner.checked ? "To'g'ri! 🎉" : `To'g'ri javob: ${item.answers.join(', ')}`} />}
        <Actions theme={theme} checked={runner.checked !== null} onCheck={check} onReset={() => { setPlaced(placed.map(() => null)); runner.setChecked(null) }} onNext={runner.next} mode={runner.mode} last={runner.last} disabled={placed.every((p) => p === null)} />
      </div>
    </>
  )
}

// ============================ 6. Reading ============================

function ReadingPlayer(props: InnerProps) {
  const { data, index } = props
  const theme = kindTheme(data.kind)
  const passage = data.reading?.passage ?? ''
  const items = data.reading?.items ?? []
  const item = items[index]
  const runner = useRunner(items.length, props)
  const [picked, setPicked] = useState<string | null>(null)
  const [typed, setTyped] = useState('')

  const isWrite = data.kind === 'reading-fill' || data.kind === 'reading-short'

  /** Joriy savol bo'yicha javob tafsiloti (tarixga yoziladi). */
  const detail = () => ({
    prompt: item?.q ?? '',
    answer: isWrite ? typed : item?.options.find((o) => o.id === picked)?.text ?? '',
    expected: isWrite ? item?.answer ?? '' : item?.options.find((o) => o.id === item.correctId)?.text ?? '',
  })

  return (
    <>
      <Head theme={theme} progress={items.length ? ((index + 1) / items.length) * 100 : 0} label={items.length ? `${index + 1}/${items.length}` : '0/0'} caption="Matnni o'qing" />
      <div style={{ ...bodyStyle, padding: '18px 18px 20px' }}>
        <div style={{ background: theme.soft, borderRadius: 14, padding: '13px 14px', maxHeight: 190, overflowY: 'auto' }}>
          <p style={{ margin: 0, fontSize: 14.5, lineHeight: 1.7, color: '#4a4d56', whiteSpace: 'pre-wrap' }}>
            {passage || 'Matn shu yerda ko\'rinadi…'}
          </p>
        </div>

        {!item ? (
          <span style={{ fontSize: 14, color: theme.caption, textAlign: 'center', padding: '20px 0' }}>Hali savol qo'shilmadi</span>
        ) : (
          <>
            <div style={{ fontSize: 16, fontWeight: 600, color: '#181a22' }}>{item.q}</div>

            {isWrite ? (
              <input
                value={typed}
                onChange={(e) => {
                  setTyped(e.target.value)
                  runner.setChecked(null)
                }}
                onKeyDown={(e) => {
                  if (e.key === 'Enter') runner.finish(answerMatches(item.answer, typed), detail())
                }}
                placeholder="Javobni yozing…"
                style={{ ...sans, width: '100%', fontSize: 16, fontWeight: 600, color: '#181a22', background: theme.phone, border: `1.6px solid ${theme.phoneBorder}`, borderRadius: 13, padding: '13px 15px', outline: 'none' }}
              />
            ) : (
              <div style={{ display: 'flex', flexDirection: 'column', gap: 9 }}>
                {item.options.map((o) => {
                  const on = picked === o.id
                  const isRight = runner.checked !== null && o.id === item.correctId
                  const isWrong = runner.checked === false && on
                  return (
                    <button
                      key={o.id}
                      type="button"
                      className="dc-opt"
                      onClick={() => {
                        setPicked(o.id)
                        runner.setChecked(null)
                      }}
                      style={{
                        display: 'flex', alignItems: 'center', gap: 9, textAlign: 'left', ...sans, fontSize: 14.5, fontWeight: on || isRight ? 600 : 500,
                        borderRadius: 12, padding: '11px 13px', cursor: 'pointer', color: '#4a4d56',
                        background: isRight ? '#e7f6ee' : isWrong ? '#fdecec' : on ? theme.soft : '#fff',
                        border: `1.4px solid ${isRight ? '#169f65' : isWrong ? '#de3b3d' : on ? theme.accent : '#e3e4e8'}`,
                      }}
                    >
                      <span style={{ width: 12, height: 12, borderRadius: '50%', flex: 'none', border: on ? `3.5px solid ${theme.accent}` : '1.5px solid #cfd3ff' }} />
                      {o.text}
                    </button>
                  )
                })}
              </div>
            )}

            {runner.checked !== null && (
              <ResultBar ok={runner.checked} text={runner.checked ? "To'g'ri! 🎉" : `To'g'ri javob: ${isWrite ? item.answer : item.options.find((o) => o.id === item.correctId)?.text ?? '—'}`} />
            )}
            <Actions
              theme={theme}
              checked={runner.checked !== null}
              onCheck={() => runner.finish(isWrite ? answerMatches(item.answer, typed) : picked !== null && picked === item.correctId, detail())}
              onReset={() => {
                setPicked(null)
                setTyped('')
                runner.setChecked(null)
              }}
              onNext={runner.next}
              mode={runner.mode}
              last={runner.last}
              disabled={isWrite ? !typed.trim() : !picked}
            />
          </>
        )}
      </div>
    </>
  )
}

// ============================ 7. Test ============================

function TestPlayer(props: InnerProps) {
  const { data, index } = props
  const theme = kindTheme(data.kind)
  const items = data.test?.items ?? []
  const item = items[index]
  const runner = useRunner(items.length, props)
  const [picked, setPicked] = useState<string | null>(null)

  if (!item) return <EmptyState theme={theme} text="Hali savol qo'shilmadi" />

  const imageOptions = data.kind === 'test-imageopts'

  return (
    <>
      <Head theme={theme} progress={((index + 1) / items.length) * 100} label={`${index + 1}/${items.length}`} caption="To'g'ri javobni tanlang" />
      <div style={{ ...bodyStyle, padding: '18px 18px 20px' }}>
        {data.kind === 'test-image' && (
          <div style={{ width: '100%', aspectRatio: '16 / 10', borderRadius: 14, overflow: 'hidden', border: `1px solid ${theme.line}`, background: theme.soft, display: 'flex', alignItems: 'center', justifyContent: 'center' }}>
            {item.imageUrl ? <img src={item.imageUrl} alt="" style={{ width: '100%', height: '100%', objectFit: 'cover' }} /> : <span style={{ fontSize: 13, color: theme.caption }}>Rasm yuklanmagan</span>}
          </div>
        )}
        {data.kind === 'test-audio' && <PlayButton accent={theme.accent} tint={theme.soft} url={item.audioUrl} />}

        <div style={{ fontSize: 16.5, fontWeight: 600, color: '#181a22' }}>{item.q}</div>

        {imageOptions ? (
          <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: 10 }}>
            {item.options.map((o) => {
              const on = picked === o.id
              const isRight = runner.checked !== null && o.id === item.correctId
              const isWrong = runner.checked === false && on
              return (
                <button
                  key={o.id}
                  type="button"
                  className="dc-opt"
                  onClick={() => {
                    setPicked(o.id)
                    runner.setChecked(null)
                  }}
                  style={{
                    height: 96, borderRadius: 12, cursor: 'pointer', overflow: 'hidden', padding: 0, background: '#fbfaf7',
                    border: `2px solid ${isRight ? '#169f65' : isWrong ? '#de3b3d' : on ? theme.accent : '#e3e4e8'}`,
                    display: 'flex', alignItems: 'center', justifyContent: 'center', position: 'relative',
                  }}
                >
                  {o.imageUrl ? (
                    <img src={o.imageUrl} alt="" style={{ width: '100%', height: '100%', objectFit: 'cover' }} />
                  ) : (
                    <span style={{ fontSize: 12, color: '#cfd3ff' }}>{o.text || 'Rasm'}</span>
                  )}
                  {o.imageUrl && o.text && (
                    <span style={{ position: 'absolute', left: 0, right: 0, bottom: 0, background: 'rgba(24,26,34,.62)', color: '#fff', fontSize: 11.5, fontWeight: 600, padding: '3px 6px' }}>{o.text}</span>
                  )}
                </button>
              )
            })}
          </div>
        ) : (
          <div style={{ display: 'flex', flexDirection: 'column', gap: 9 }}>
            {item.options.map((o) => {
              const on = picked === o.id
              const isRight = runner.checked !== null && o.id === item.correctId
              const isWrong = runner.checked === false && on
              return (
                <button
                  key={o.id}
                  type="button"
                  className="dc-opt"
                  onClick={() => {
                    setPicked(o.id)
                    runner.setChecked(null)
                  }}
                  style={{
                    display: 'flex', alignItems: 'center', gap: 9, textAlign: 'left', ...sans, fontSize: 14.5, fontWeight: on || isRight ? 600 : 500,
                    borderRadius: 12, padding: '11px 13px', cursor: 'pointer', color: '#181a22',
                    background: isRight ? '#e7f6ee' : isWrong ? '#fdecec' : on ? theme.soft : '#fff',
                    border: `1.4px solid ${isRight ? '#169f65' : isWrong ? '#de3b3d' : on ? theme.accent : '#e3e4e8'}`,
                  }}
                >
                  <span style={{ width: 12, height: 12, borderRadius: '50%', flex: 'none', border: on ? `3.5px solid ${theme.accent}` : '1.5px solid #cfd3ff' }} />
                  {o.text}
                </button>
              )
            })}
          </div>
        )}

        {runner.checked !== null && (
          <ResultBar ok={runner.checked} text={runner.checked ? "To'g'ri! 🎉" : item.explain ? item.explain : "Xato — to'g'ri javob belgilandi"} />
        )}
        <Actions
          theme={theme}
          checked={runner.checked !== null}
          onCheck={() =>
            runner.finish(picked !== null && picked === item.correctId, {
              prompt: item.q,
              answer: item.options.find((o) => o.id === picked)?.text ?? '',
              expected: item.options.find((o) => o.id === item.correctId)?.text ?? '',
            })
          }
          onReset={() => {
            setPicked(null)
            runner.setChecked(null)
          }}
          onNext={runner.next}
          mode={runner.mode}
          last={runner.last}
          disabled={!picked}
        />
      </div>
    </>
  )
}

// ============================ 8. Writing ============================

function WritingPlayer({ data, mode = 'preview', onFinish, runRef }: InnerProps) {
  const theme = kindTheme(data.kind)
  const w = data.writing
  const [answer, setAnswer] = useState('')
  const [submitted, setSubmitted] = useState(false)
  const count = answer.trim() ? answer.trim().split(/\s+/).length : 0
  const enough = count >= (w?.minWords ?? 0)

  if (!w) return <EmptyState theme={theme} text="Mashq sozlanmagan" />

  return (
    <>
      <Head theme={theme} progress={Math.min(100, w.minWords ? (count / w.minWords) * 100 : 0)} label={`${count} so'z`} caption={`Writing · ${w.minutes} daq`} />
      <div style={{ ...bodyStyle, padding: '18px 18px 20px' }}>
        <div style={{ background: theme.soft, borderRadius: 12, padding: '11px 13px', display: 'flex', flexDirection: 'column', gap: 4 }}>
          <span style={{ fontSize: 10, fontWeight: 700, letterSpacing: '.05em', textTransform: 'uppercase', color: theme.caption }}>Mavzu</span>
          <span style={{ fontSize: 15.5, fontWeight: 700, color: '#181a22' }}>{w.topic || 'Mavzu kiritilmagan'}</span>
        </div>
        {w.prompt && <p style={{ margin: 0, fontSize: 14, lineHeight: 1.6, color: '#4a4d56' }}>{w.prompt}</p>}
        {w.hints.length > 0 && (
          <div style={{ display: 'flex', flexWrap: 'wrap', gap: 6 }}>
            {w.hints.map((h, i) => (
              <span key={i} style={{ fontSize: 12.5, fontWeight: 600, color: theme.accent, background: theme.head, borderRadius: 20, padding: '5px 11px' }}>
                {h}
              </span>
            ))}
          </div>
        )}
        <textarea
          value={answer}
          onChange={(e) => {
            setAnswer(e.target.value)
            setSubmitted(false)
          }}
          placeholder="Matnni shu yerga yozing…"
          rows={8}
          style={{ ...sans, width: '100%', flex: 1, fontSize: 15, lineHeight: 1.6, color: '#181a22', background: theme.phone, border: `1.4px solid ${theme.phoneBorder}`, borderRadius: 13, padding: '12px 14px', outline: 'none', resize: 'none' }}
        />
        <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', fontSize: 12.5, color: enough ? '#0f7a4c' : '#777a82' }}>
          <span>
            {count} / {w.minWords} so'z
          </span>
          {submitted && <span style={{ fontWeight: 700, color: '#0f7a4c' }}>Topshirildi ✓</span>}
        </div>
        <button
          type="button"
          onClick={() => {
            setSubmitted(true)
            // O'quvchi yozgan MATNNING O'ZI tarixga tushadi — o'qituvchi keyin o'qib baholaydi.
            if (mode === 'solve') {
              onFinish?.(enough ? 1 : 0, 1, singleLog(
                runRef.current, enough, w.topic || 'Writing', answer.trim(),
                `Kamida ${w.minWords} so'z (yozildi: ${count})`,
              ))
            }
          }}
          disabled={!answer.trim()}
          style={{ ...sans, background: answer.trim() ? theme.accent : '#9aa0aa', border: 'none', color: '#fff', fontWeight: 700, fontSize: 15, padding: '14px 16px', borderRadius: 13, cursor: answer.trim() ? 'pointer' : 'default', marginTop: 'auto' }}
        >
          {submitted ? 'Qayta topshirish' : 'Topshirish'}
        </button>
      </div>
    </>
  )
}

// ============================ 9. Speaking ============================

function SpeakingPlayer({ data, mode = 'preview', onFinish, runRef }: InnerProps) {
  const theme = kindTheme(data.kind)
  const s = data.speaking
  const [phase, setPhase] = useState<'idle' | 'prep' | 'rec' | 'done'>('idle')
  const [left, setLeft] = useState(0)
  const timer = useRef<number | null>(null)
  /** Gapirish boshlangan vaqt — necha soniya gapirgani tarixga yoziladi. */
  const spokeFrom = useRef(0)

  useEffect(() => {
    return () => {
      if (timer.current) window.clearInterval(timer.current)
    }
  }, [])

  if (!s) return <EmptyState theme={theme} text="Mashq sozlanmagan" />

  const run = (seconds: number, onEnd: () => void) => {
    setLeft(seconds)
    if (timer.current) window.clearInterval(timer.current)
    timer.current = window.setInterval(() => {
      setLeft((v) => {
        if (v <= 1) {
          if (timer.current) window.clearInterval(timer.current)
          onEnd()
          return 0
        }
        return v - 1
      })
    }, 1000)
  }

  /** Yakunlash — javob (necha soniya gapirgani) tarixga yoziladi. Speaking hozircha
   *  avto-baholanmaydi: topshirilgani "to'g'ri" deb hisoblanadi, ballni o'qituvchi qo'yadi. */
  const done = () => {
    setPhase('done')
    if (mode !== 'solve') return
    const spoke = spokeFrom.current ? Math.round((Date.now() - spokeFrom.current) / 1000) : 0
    onFinish?.(1, 1, singleLog(
      runRef.current, true, s.topic || 'Speaking',
      `Ovozli javob · ${spoke} soniya gapirdi`,
      `${s.speakSec} soniya`,
    ))
  }

  const startRec = () => {
    setPhase('rec')
    spokeFrom.current = Date.now()
    run(s.speakSec, done)
  }

  const start = () => {
    if (s.prepSec > 0) {
      setPhase('prep')
      run(s.prepSec, startRec)
    } else {
      startRec()
    }
  }

  const stop = () => {
    if (timer.current) window.clearInterval(timer.current)
    done()
  }

  const reset = () => {
    if (timer.current) window.clearInterval(timer.current)
    setPhase('idle')
    setLeft(0)
    spokeFrom.current = 0
  }

  const mmss = (v: number) => `${String(Math.floor(v / 60)).padStart(2, '0')}:${String(v % 60).padStart(2, '0')}`

  return (
    <>
      <Head
        theme={theme}
        progress={phase === 'rec' ? ((s.speakSec - left) / Math.max(1, s.speakSec)) * 100 : phase === 'done' ? 100 : 0}
        label={phase === 'idle' ? mmss(s.speakSec) : mmss(left)}
        caption={phase === 'prep' ? 'Tayyorlaning' : phase === 'rec' ? 'Gapiring' : 'Speaking'}
      />
      <div style={{ ...bodyStyle, padding: '18px 18px 20px' }}>
        <div style={{ background: theme.soft, borderRadius: 12, padding: '11px 13px', display: 'flex', flexDirection: 'column', gap: 4 }}>
          <span style={{ fontSize: 10, fontWeight: 700, letterSpacing: '.05em', textTransform: 'uppercase', color: theme.caption }}>Mavzu</span>
          <span style={{ fontSize: 15.5, fontWeight: 700, color: '#181a22' }}>{s.topic || 'Mavzu kiritilmagan'}</span>
        </div>
        {s.prompt && <p style={{ margin: 0, fontSize: 14, lineHeight: 1.6, color: '#4a4d56' }}>{s.prompt}</p>}
        {s.hints.length > 0 && (
          <div style={{ display: 'flex', flexDirection: 'column', gap: 6 }}>
            {s.hints.map((h, i) => (
              <span key={i} style={{ fontSize: 13.5, color: '#4a4d56', background: theme.head, borderRadius: 10, padding: '8px 11px' }}>
                • {h}
              </span>
            ))}
          </div>
        )}

        <div style={{ display: 'flex', flexDirection: 'column', alignItems: 'center', gap: 12, marginTop: 'auto' }}>
          <button
            type="button"
            onClick={phase === 'rec' ? stop : phase === 'done' ? reset : start}
            className={phase === 'rec' ? 'dc-pulse' : undefined}
            style={{
              width: 78, height: 78, borderRadius: '50%', border: 'none', cursor: 'pointer',
              background: phase === 'rec' ? '#de3b3d' : theme.accent,
              display: 'flex', alignItems: 'center', justifyContent: 'center', boxShadow: `0 12px 26px -10px ${theme.accent}`,
            }}
          >
            {phase === 'rec' ? (
              <svg width="26" height="26" viewBox="0 0 24 24" fill="#fff">
                <rect x="6" y="6" width="12" height="12" rx="2" />
              </svg>
            ) : (
              <svg width="30" height="30" viewBox="0 0 24 24" fill="none" stroke="#fff" strokeWidth={2} strokeLinecap="round" strokeLinejoin="round">
                <rect x="9" y="3" width="6" height="11" rx="3" />
                <path d="M5 11a7 7 0 0014 0" />
                <path d="M12 18v3" />
              </svg>
            )}
          </button>
          <span style={{ fontSize: 13.5, fontWeight: 600, color: theme.accent }}>
            {phase === 'idle' && 'Boshlash uchun bosing'}
            {phase === 'prep' && `Tayyorlanish: ${mmss(left)}`}
            {phase === 'rec' && `Yozilmoqda: ${mmss(left)}`}
            {phase === 'done' && 'Javob topshirildi ✓'}
          </span>
        </div>
      </div>
    </>
  )
}

// ============================ 10. Moslashtirish ============================

function MatchingPlayer({ data, mode = 'preview', onFinish, runRef }: InnerProps) {
  const theme = kindTheme(data.kind)
  const m = data.matching
  const [answers, setAnswers] = useState<Record<string, number>>({})
  const [checked, setChecked] = useState<boolean | null>(null)

  if (!m) return <EmptyState theme={theme} text="Mashq sozlanmagan" />
  if (m.rows.length === 0) return <EmptyState theme={theme} text="Hali element qo'shilmadi" />

  const cols = Array.from({ length: m.colCount }, (_, i) => i)
  const done = Object.keys(answers).length
  const rows = m.rows
  const startNum = m.startNum
  const check = () => {
    const ok = rows.every((r) => answers[r.id] === r.key)
    setChecked(ok)
    if (mode !== 'solve') return
    // Javob "1→A, 2→C" ko'rinishida yoziladi — o'qituvchi qaysi qator xato bo'lganini ko'radi.
    const pairs = (pick: (r: (typeof rows)[number]) => number | undefined) =>
      rows.map((r, ri) => {
        const c = pick(r)
        return `${startNum + ri}→${c === undefined ? '—' : colLetter(c)}`
      }).join(', ')
    onFinish?.(ok ? 1 : 0, 1, singleLog(
      runRef.current, ok, m.statement || 'Moslashtiring',
      pairs((r) => answers[r.id]), pairs((r) => r.key),
    ))
  }

  return (
    <>
      <Head theme={theme} progress={(done / m.rows.length) * 100} label={`${done}/${m.rows.length}`} caption="Moslashtiring" />
      <div style={{ ...bodyStyle, padding: '18px 18px 20px' }}>
        {m.statement && <div style={{ fontSize: 14.5, fontWeight: 600, color: '#181a22' }}>{m.statement}</div>}
        {data.kind === 'matching-reading' && (
          <div style={{ background: theme.soft, borderRadius: 12, padding: '12px 13px', maxHeight: 150, overflowY: 'auto' }}>
            <p style={{ margin: 0, fontSize: 14, lineHeight: 1.7, color: '#4a4d56', whiteSpace: 'pre-wrap' }}>{m.passage || "Matn shu yerda ko'rinadi…"}</p>
          </div>
        )}
        {data.kind === 'matching-audio' && <PlayButton accent={theme.accent} tint={theme.soft} url={m.audioUrl} />}

        {/* Harflar ma'nosi */}
        <div style={{ display: 'flex', flexDirection: 'column', gap: 6 }}>
          {cols.map((c) => (
            <div key={c} style={{ display: 'flex', alignItems: 'center', gap: 9, fontSize: 13.5, color: '#4a4d56' }}>
              <span style={{ flex: 'none', width: 22, height: 22, borderRadius: 6, background: theme.head, color: theme.accent, fontWeight: 700, fontSize: 12, display: 'flex', alignItems: 'center', justifyContent: 'center', ...display }}>
                {colLetter(c)}
              </span>
              {m.colLabels[c] || <span style={{ color: '#777a82' }}>—</span>}
            </div>
          ))}
        </div>

        {/* Javob jadvali */}
        <div style={{ overflowX: 'auto' }}>
          <table style={{ borderCollapse: 'collapse', width: '100%', border: `1px solid ${theme.line}` }}>
            <tbody>
              <tr style={{ background: theme.soft }}>
                <td style={{ padding: '6px 8px' }} />
                {cols.map((c) => (
                  <td key={c} style={{ padding: '6px 0', textAlign: 'center', fontWeight: 700, fontSize: 12, color: theme.accent, borderLeft: `1px solid ${theme.line}`, ...display }}>
                    {colLetter(c)}
                  </td>
                ))}
              </tr>
              {m.rows.map((r, ri) => (
                <tr key={r.id}>
                  <td style={{ padding: '6px 8px', fontSize: 13, fontWeight: 700, color: '#181a22', borderTop: `1px solid ${theme.line}`, whiteSpace: 'nowrap' }}>
                    {m.startNum + ri}. {r.text}
                  </td>
                  {cols.map((c) => {
                    const on = answers[r.id] === c
                    const reveal = checked !== null
                    const good = reveal && r.key === c
                    const bad = reveal && on && r.key !== c
                    return (
                      <td key={c} style={{ padding: 3, borderLeft: `1px solid ${theme.line}`, borderTop: `1px solid ${theme.line}` }}>
                        <button
                          type="button"
                          className="dc-cell"
                          onClick={() => {
                            setAnswers((a) => ({ ...a, [r.id]: c }))
                            setChecked(null)
                          }}
                          style={{
                            width: '100%', height: 26, borderRadius: 5, cursor: 'pointer', border: 'none',
                            background: good ? '#169f65' : bad ? '#de3b3d' : on ? theme.accent : 'transparent',
                            color: '#fff', fontWeight: 700, fontSize: 12,
                          }}
                        >
                          {on || good ? '✓' : ''}
                        </button>
                      </td>
                    )
                  })}
                </tr>
              ))}
            </tbody>
          </table>
        </div>

        {checked !== null && <ResultBar ok={checked} text={checked ? "To'g'ri! 🎉" : "Xato — to'g'ri kataklar yashil"} />}
        <div style={{ display: 'flex', gap: 10, marginTop: 'auto' }}>
          <button
            type="button"
            onClick={() => {
              setAnswers({})
              setChecked(null)
            }}
            style={{ ...sans, flex: 'none', background: theme.line, border: 'none', color: '#4a4d56', fontWeight: 600, fontSize: 15, padding: '14px 16px', borderRadius: 13, cursor: 'pointer' }}
          >
            ↺
          </button>
          <button
            type="button"
            onClick={check}
            disabled={done === 0}
            style={{ ...sans, flex: 1, background: done === 0 ? '#9aa0aa' : theme.accent, border: 'none', color: '#fff', fontWeight: 700, fontSize: 15, padding: '14px 16px', borderRadius: 13, cursor: done === 0 ? 'default' : 'pointer' }}
          >
            Tekshirish
          </button>
        </div>
      </div>
    </>
  )
}

// ============================ Dispatcher ============================

/** Turga mos pleyer. Element almashganda `key` orqali QAYTA YARATILADI — javob holati
 *  (tanlangan variant, terilgan so'zlar) o'z-o'zidan tozalanadi; yig'ilgan javoblar esa
 *  `runRef` da (bu komponentda) saqlanib qoladi. */
export function ExercisePlayer(props: PlayerProps) {
  const runRef = useRef<RunState>({ answers: new Map(), itemStart: Date.now() })
  return <PlayerBody key={`${props.data.kind}:${props.index}`} {...props} runRef={runRef} />
}

function PlayerBody(props: InnerProps) {
  switch (kindFamily(props.data.kind)) {
    case 'sentence':
      return <SentencePlayer {...props} />
    case 'sentence-choice':
      return <SentenceChoicePlayer {...props} />
    case 'fill':
      return <FillPlayer {...props} />
    case 'wordpick':
      return <WordPickPlayer {...props} />
    case 'wordfind':
      return <WordFindPlayer {...props} />
    case 'reading':
      return <ReadingPlayer {...props} />
    case 'test':
      return <TestPlayer {...props} />
    case 'writing':
      return <WritingPlayer {...props} />
    case 'speaking':
      return <SpeakingPlayer {...props} />
    default:
      return <MatchingPlayer {...props} />
  }
}

/** O'quvchi uchun to'liq mashq (barcha elementlar ketma-ket) — portal sahifasi ishlatadi.
 *  `onDone` yakunda natija VA har element bo'yicha javoblarni beradi — portal shu ma'lumotni
 *  serverga yuboradi (urinish tarixi). */
export function ExerciseRunner({ data, onDone, frame = true }: { data: ExerciseData; onDone?: (correct: number, total: number, answers: ExerciseAnswerLog[]) => void; frame?: boolean }) {
  const theme = kindTheme(data.kind)
  const [index, setIndex] = useState(0)
  const total = Math.max(1, exerciseCount(data))
  const inner: ReactNode = (
    <ExercisePlayer data={data} index={Math.min(index, total - 1)} onIndex={setIndex} mode="solve" onFinish={(c, t, a) => onDone?.(c, t, a)} />
  )
  if (!frame) return <div className="dc-root" style={{ display: 'flex', flexDirection: 'column', minHeight: 480, background: '#fff', borderRadius: 18, overflow: 'hidden', border: `1px solid ${theme.phoneBorder}` }}>{inner}</div>
  return (
    <div className="dc-root" style={{ background: theme.phone, borderRadius: 26, padding: 12, border: `1px solid ${theme.phoneBorder}` }}>
      <div style={{ background: '#fff', borderRadius: 18, overflow: 'hidden', display: 'flex', flexDirection: 'column', minHeight: 480 }}>{inner}</div>
    </div>
  )
}
