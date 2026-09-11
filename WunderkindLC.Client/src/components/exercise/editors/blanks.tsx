/**
 * BO'SH JOY / SO'Z tahrirlovchilari:
 *   • FillEditor     — "Bo'sh joyni to'ldirish" (variant tanlash yoki so'z yozish);
 *   • WordPickEditor — "So'z tanlash" (gap ichida (bir/*ikki) ko'rinishidagi variantlar);
 *   • WordFindEditor — "So'z topish" (bo'sh joylarga so'zlar terib chiqiladi + chalg'ituvchilar).
 */
import { useState } from 'react'
import { sans } from '../catalog'
import { AddPanel, AudioPicker, ImagePicker, EmptyRow, ItemCard, RemoveBtn, ScrollList, SectionHead, softInput } from '../kit'
import { blankCount, fillMode, kindMedia, parsePickText, uid } from '../model'
import type { FillItem, WordFindItem, WordPickItem } from '../model'
import { CorrectDot, MiniLabel, optInput, subInput } from './common'
import type { EditorProps } from './common'

/** Kartadagi media qatori (rasm yoki audio) — uch tahrirlovchida ham bir xil. */
function MediaRow({
  kind, accent, imageUrl, audioUrl, audioName, onImage, onAudio,
}: {
  kind: string
  accent: string
  imageUrl?: string
  audioUrl?: string
  audioName?: string
  onImage: (url: string) => void
  onAudio: (url: string, name: string) => void
}) {
  const media = kindMedia(kind as never)
  if (media === 'none') return null
  // 'both' — rasm va audio YONMA-YON (bo'sh joy · audio + rasm turi).
  return (
    <div style={{ display: 'flex', gap: 10, alignItems: 'flex-start' }}>
      {(media === 'image' || media === 'both') && <ImagePicker url={imageUrl} onChange={onImage} />}
      {(media === 'audio' || media === 'both') && (
        <AudioPicker accent={accent} url={audioUrl} name={audioName} onChange={onAudio} />
      )}
    </div>
  )
}

// ============================ Bo'sh joyni to'ldirish ============================

export function FillEditor({ data, onChange, active, setActive, theme }: EditorProps) {
  const [draft, setDraft] = useState('')
  const [optDrafts, setOptDrafts] = useState<Record<string, string>>({})
  const fill = data.fill ?? { blank: 'line' as const, items: [] }
  const items = fill.items
  const mode = fillMode(data.kind)

  const patch = (next: FillItem[]) => onChange({ ...data, fill: { ...fill, items: next } })
  const update = (id: string, fields: Partial<FillItem>) => patch(items.map((it) => (it.id === id ? { ...it, ...fields } : it)))

  const add = () => {
    const text = draft.trim()
    if (!text) return
    patch([...items, { id: uid('f'), text, translation: '', options: [], correctId: null, answer: '' }])
    setDraft('')
    setActive(items.length)
  }

  const addOption = (item: FillItem) => {
    const text = (optDrafts[item.id] ?? '').trim()
    if (!text) return
    const opt = { id: uid('o'), text }
    update(item.id, { options: [...item.options, opt], correctId: item.correctId ?? opt.id })
    setOptDrafts((d) => ({ ...d, [item.id]: '' }))
  }

  return (
    <>
      <SectionHead title="Savollar" count={`${items.length} ta savol`} />
      <ScrollList>
        {items.map((s, i) => (
          <ItemCard
            key={s.id}
            active={i === active}
            accent={theme.accent}
            num={i + 1}
            onSelect={() => setActive(i)}
            onRemove={() => {
              patch(items.filter((x) => x.id !== s.id))
              if (active >= items.length - 1) setActive(Math.max(0, items.length - 2))
            }}
          >
            <input
              value={s.text}
              onChange={(e) => update(s.id, { text: e.target.value })}
              onClick={(e) => e.stopPropagation()}
              placeholder="Bo'sh joy uchun ___ qo'shing"
              style={softInput}
            />
            {blankCount(s.text) === 0 && <MiniLabel>Gapda ___ yo'q — bo'sh joy ko'rinmaydi</MiniLabel>}

            <MediaRow
              kind={data.kind}
              accent={theme.accent}
              imageUrl={s.imageUrl}
              audioUrl={s.audioUrl}
              audioName={s.audioName}
              onImage={(url) => update(s.id, { imageUrl: url })}
              onAudio={(url, name) => update(s.id, { audioUrl: url, audioName: name })}
            />

            {mode === 'choose' ? (
              <div style={{ display: 'flex', flexDirection: 'column', gap: 7 }} onClick={(e) => e.stopPropagation()}>
                <MiniLabel>Variantlar — to'g'risini belgilang</MiniLabel>
                {s.options.map((o) => (
                  <div key={o.id} style={{ display: 'flex', alignItems: 'center', gap: 8 }}>
                    <CorrectDot on={s.correctId === o.id} accent={theme.accent} onClick={() => update(s.id, { correctId: o.id })} />
                    <input
                      value={o.text}
                      onChange={(e) => update(s.id, { options: s.options.map((x) => (x.id === o.id ? { ...x, text: e.target.value } : x)) })}
                      placeholder="Variant"
                      style={optInput}
                    />
                    <RemoveBtn size={16} onClick={() => update(s.id, { options: s.options.filter((x) => x.id !== o.id), correctId: s.correctId === o.id ? null : s.correctId })} />
                  </div>
                ))}
                <div style={{ display: 'flex', gap: 8 }}>
                  <input
                    value={optDrafts[s.id] ?? ''}
                    onChange={(e) => setOptDrafts((d) => ({ ...d, [s.id]: e.target.value }))}
                    onKeyDown={(e) => {
                      if (e.key === 'Enter') {
                        e.preventDefault()
                        addOption(s)
                      }
                    }}
                    placeholder="Yangi variant"
                    style={optInput}
                  />
                  <button
                    type="button"
                    onClick={() => addOption(s)}
                    style={{ ...sans, background: '#fff', border: `1px solid ${theme.accent}55`, color: theme.accent, fontWeight: 600, fontSize: 13.5, padding: '0 14px', borderRadius: 9, cursor: 'pointer' }}
                  >
                    + Qo'shish
                  </button>
                </div>
              </div>
            ) : (
              <div style={{ display: 'flex', flexDirection: 'column', gap: 6 }} onClick={(e) => e.stopPropagation()}>
                <MiniLabel>To'g'ri javob</MiniLabel>
                <input value={s.answer} onChange={(e) => update(s.id, { answer: e.target.value })} placeholder="masalan: yugurishni" style={optInput} />
                <span style={{ fontSize: 11.5, color: '#777a82' }}>Bir nechta to'g'ri variant bo'lsa "/" bilan ajrating: yugurishni/chopishni</span>
              </div>
            )}

            <input
              value={s.translation ?? ''}
              onChange={(e) => update(s.id, { translation: e.target.value })}
              onClick={(e) => e.stopPropagation()}
              placeholder="Tarjima / izoh (ixtiyoriy)"
              style={subInput}
            />
          </ItemCard>
        ))}
        {items.length === 0 && <EmptyRow text="Hali savol qo'shilmadi" />}
      </ScrollList>

      <AddPanel
        label="Yangi savol qo'shish"
        placeholder="Masalan: Men har kuni ertalab ___ yaxshi ko'raman"
        value={draft}
        onChange={setDraft}
        onAdd={add}
        hint="Bo'sh joy uchun ___ (uchta pastki chiziq) qo'ying. Enter — savolni saqlaydi."
      />
    </>
  )
}

// ============================ So'z tanlash ============================

export function WordPickEditor({ data, onChange, active, setActive, theme }: EditorProps) {
  const [draft, setDraft] = useState('')
  const items = data.wordpick?.items ?? []

  const patch = (next: WordPickItem[]) => onChange({ ...data, wordpick: { items: next } })
  const update = (id: string, fields: Partial<WordPickItem>) => patch(items.map((it) => (it.id === id ? { ...it, ...fields } : it)))

  const add = () => {
    const text = draft.trim()
    if (!text) return
    patch([...items, { id: uid('w'), text, translation: '' }])
    setDraft('')
    setActive(items.length)
  }

  return (
    <>
      <SectionHead title="Gaplar" count={`${items.length} ta gap`} />
      <div style={{ fontSize: 12.5, color: '#777a82', background: '#f7f5f1', border: '1px solid #e3e4e8', borderRadius: 10, padding: '9px 12px' }}>
        Variantlarni qavs ichida <b>/</b> bilan yozing, to'g'risiga <b>*</b> qo'ying. Masalan: Men (bir/*ikki) olma yedim
      </div>

      <ScrollList>
        {items.map((s, i) => {
          const tokens = parsePickText(s.text)
          return (
            <ItemCard
              key={s.id}
              active={i === active}
              accent={theme.accent}
              num={i + 1}
              onSelect={() => setActive(i)}
              onRemove={() => {
                patch(items.filter((x) => x.id !== s.id))
                if (active >= items.length - 1) setActive(Math.max(0, items.length - 2))
              }}
            >
              <input
                value={s.text}
                onChange={(e) => update(s.id, { text: e.target.value })}
                onClick={(e) => e.stopPropagation()}
                placeholder="Qavs ichida variant qo'shing: (bir/*ikki)"
                style={softInput}
              />
              <div style={{ display: 'flex', flexWrap: 'wrap', gap: 6, alignItems: 'center' }}>
                {tokens.map((t, ti) =>
                  t.kind === 'text' ? (
                    <span key={ti} style={{ fontSize: 13, color: '#777a82' }}>
                      {t.text.trim()}
                    </span>
                  ) : (
                    <span key={ti} style={{ display: 'inline-flex', gap: 4 }}>
                      {t.options?.map((o, oi) => (
                        <span
                          key={oi}
                          style={{
                            fontSize: 12.5, fontWeight: 700, borderRadius: 7, padding: '3px 9px',
                            background: o.correct ? theme.accent : '#fff',
                            border: `1.3px solid ${o.correct ? theme.accent : theme.phoneBorder}`,
                            color: o.correct ? '#fff' : '#4a4d56',
                          }}
                        >
                          {o.text}
                        </span>
                      ))}
                    </span>
                  ),
                )}
              </div>

              <MediaRow
                kind={data.kind}
                accent={theme.accent}
                imageUrl={s.imageUrl}
                audioUrl={s.audioUrl}
                audioName={s.audioName}
                onImage={(url) => update(s.id, { imageUrl: url })}
                onAudio={(url, name) => update(s.id, { audioUrl: url, audioName: name })}
              />

              <input
                value={s.translation ?? ''}
                onChange={(e) => update(s.id, { translation: e.target.value })}
                onClick={(e) => e.stopPropagation()}
                placeholder="Tarjima / izoh (ixtiyoriy)"
                style={subInput}
              />
            </ItemCard>
          )
        })}
        {items.length === 0 && <EmptyRow text="Hali gap qo'shilmadi" />}
      </ScrollList>

      <AddPanel
        label="Yangi gap qo'shish"
        placeholder="Men (bir/*ikki) olma yedim"
        value={draft}
        onChange={setDraft}
        onAdd={add}
        hint="Bir gapda bir nechta qavs bo'lishi mumkin — har biri alohida tanlov bo'ladi."
      />
    </>
  )
}

// ============================ So'z topish ============================

export function WordFindEditor({ data, onChange, active, setActive, theme }: EditorProps) {
  const [draft, setDraft] = useState('')
  const [distDrafts, setDistDrafts] = useState<Record<string, string>>({})
  const wf = data.wordfind ?? { blank: 'line' as const, items: [] }
  const items = wf.items

  const patch = (next: WordFindItem[]) => onChange({ ...data, wordfind: { ...wf, items: next } })
  const update = (id: string, fields: Partial<WordFindItem>) => patch(items.map((it) => (it.id === id ? { ...it, ...fields } : it)))

  const add = () => {
    const text = draft.trim()
    if (!text) return
    patch([...items, { id: uid('wf'), text, translation: '', answers: [], distractors: [] }])
    setDraft('')
    setActive(items.length)
  }

  const addDistractor = (item: WordFindItem) => {
    const text = (distDrafts[item.id] ?? '').trim()
    if (!text) return
    update(item.id, { distractors: [...item.distractors, text] })
    setDistDrafts((d) => ({ ...d, [item.id]: '' }))
  }

  return (
    <>
      <SectionHead title="Savollar" count={`${items.length} ta savol`} />
      <div style={{ fontSize: 12.5, color: '#777a82', background: '#f7f5f1', border: '1px solid #e3e4e8', borderRadius: 10, padding: '9px 12px' }}>
        Bir nechta so'z tushadigan gap uchun bir nechta <b>___</b> qo'ying. Har bir bo'sh joyga tartib bo'yicha to'g'ri javob yoziladi.
      </div>

      <ScrollList>
        {items.map((s, i) => {
          const n = blankCount(s.text)
          return (
            <ItemCard
              key={s.id}
              active={i === active}
              accent={theme.accent}
              num={i + 1}
              onSelect={() => setActive(i)}
              onRemove={() => {
                patch(items.filter((x) => x.id !== s.id))
                if (active >= items.length - 1) setActive(Math.max(0, items.length - 2))
              }}
            >
              <input
                value={s.text}
                onChange={(e) => update(s.id, { text: e.target.value })}
                onClick={(e) => e.stopPropagation()}
                placeholder="Bo'sh joy uchun ___ qo'shing (bir nechta bo'lishi mumkin)"
                style={softInput}
              />

              <MediaRow
                kind={data.kind}
                accent={theme.accent}
                imageUrl={s.imageUrl}
                audioUrl={s.audioUrl}
                audioName={s.audioName}
                onImage={(url) => update(s.id, { imageUrl: url })}
                onAudio={(url, name) => update(s.id, { audioUrl: url, audioName: name })}
              />

              <div style={{ display: 'flex', flexDirection: 'column', gap: 7 }} onClick={(e) => e.stopPropagation()}>
                <MiniLabel>To'g'ri javoblar — bo'sh joy tartibida</MiniLabel>
                {n === 0 && <span style={{ fontSize: 11.5, color: '#777a82' }}>Gapda ___ yo'q</span>}
                {Array.from({ length: n }, (_, bi) => (
                  <div key={bi} style={{ display: 'flex', alignItems: 'center', gap: 8 }}>
                    <span style={{ flex: 'none', width: 20, height: 20, borderRadius: 6, background: theme.head, color: theme.accent, fontSize: 11.5, fontWeight: 700, display: 'flex', alignItems: 'center', justifyContent: 'center' }}>
                      {bi + 1}
                    </span>
                    <input
                      value={s.answers[bi] ?? ''}
                      onChange={(e) => {
                        const next = [...s.answers]
                        next[bi] = e.target.value
                        update(s.id, { answers: next })
                      }}
                      placeholder={`${bi + 1}-bo'sh joy javobi`}
                      style={optInput}
                    />
                  </div>
                ))}
              </div>

              <div style={{ display: 'flex', flexDirection: 'column', gap: 7 }} onClick={(e) => e.stopPropagation()}>
                <MiniLabel>Chalg'ituvchi so'zlar — ixtiyoriy</MiniLabel>
                <div style={{ display: 'flex', flexWrap: 'wrap', gap: 6 }}>
                  {s.distractors.map((d, di) => (
                    <span key={di} style={{ display: 'inline-flex', alignItems: 'center', gap: 6, fontSize: 12.5, fontWeight: 600, color: '#4a4d56', background: '#fff', border: `1px solid ${theme.phoneBorder}`, borderRadius: 8, padding: '5px 9px' }}>
                      {d}
                      <button
                        type="button"
                        onClick={() => update(s.id, { distractors: s.distractors.filter((_, j) => j !== di) })}
                        style={{ border: 'none', background: 'transparent', color: '#9aa0aa', cursor: 'pointer', fontSize: 14, lineHeight: 1, padding: 0 }}
                      >
                        ×
                      </button>
                    </span>
                  ))}
                </div>
                <div style={{ display: 'flex', gap: 8 }}>
                  <input
                    value={distDrafts[s.id] ?? ''}
                    onChange={(e) => setDistDrafts((d) => ({ ...d, [s.id]: e.target.value }))}
                    onKeyDown={(e) => {
                      if (e.key === 'Enter') {
                        e.preventDefault()
                        addDistractor(s)
                      }
                    }}
                    placeholder="Chalg'ituvchi so'z"
                    style={optInput}
                  />
                  <button
                    type="button"
                    onClick={() => addDistractor(s)}
                    style={{ ...sans, background: '#fff', border: `1px solid ${theme.accent}55`, color: theme.accent, fontWeight: 600, fontSize: 13.5, padding: '0 14px', borderRadius: 9, cursor: 'pointer' }}
                  >
                    + Qo'shish
                  </button>
                </div>
              </div>

              <input
                value={s.translation ?? ''}
                onChange={(e) => update(s.id, { translation: e.target.value })}
                onClick={(e) => e.stopPropagation()}
                placeholder="Tarjima / izoh (ixtiyoriy)"
                style={subInput}
              />
            </ItemCard>
          )
        })}
        {items.length === 0 && <EmptyRow text="Hali savol qo'shilmadi" />}
      </ScrollList>

      <AddPanel
        label="Yangi savol qo'shish"
        placeholder="Men har kuni ___ va ___ ichaman"
        value={draft}
        onChange={setDraft}
        onAdd={add}
        hint="Har ___ uchun javob maydoni ochiladi. Chalg'ituvchi so'zlar javoblar bilan aralashtiriladi."
      />
    </>
  )
}
