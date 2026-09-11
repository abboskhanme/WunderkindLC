/**
 * GAP TUZISH tahrirlovchilari:
 *   • SentenceEditor — "so'z tartibi / audio / rasm" (gaplar ro'yxati, so'zlar chip bo'lib ko'rinadi);
 *   • SentenceChoiceEditor — "variant tanlash" (savol + variant gaplar, to'g'risi belgilanadi).
 * Maketdagi chap panel aynan ko'chirilgan.
 */
import { useState } from 'react'
import { sans } from '../catalog'
import { AddPanel, AudioPicker, ImagePicker, EmptyRow, ItemCard, RemoveBtn, ScrollList, SectionHead, softInput } from '../kit'
import { kindMedia, uid, words } from '../model'
import type { ChoiceItem, SentenceItem } from '../model'
import { CorrectDot, MiniLabel, WordChips, optInput, subInput } from './common'
import type { EditorProps } from './common'

// ============================ Gap tuzish (so'z tartibi / audio / rasm) ============================

export function SentenceEditor({ data, onChange, active, setActive, theme }: EditorProps) {
  const [draft, setDraft] = useState('')
  const items = data.sentence?.items ?? []
  const media = kindMedia(data.kind)

  const patch = (next: SentenceItem[]) => onChange({ ...data, sentence: { items: next } })
  const update = (id: string, fields: Partial<SentenceItem>) => patch(items.map((it) => (it.id === id ? { ...it, ...fields } : it)))

  const add = () => {
    const text = draft.trim()
    if (!text) return
    patch([...items, { id: uid('s'), text, translation: '' }])
    setDraft('')
    setActive(items.length)
  }

  return (
    <>
      <SectionHead title="Gaplar" count={`${items.length} ta gap`} />
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
              placeholder="Gapni yozing"
              style={softInput}
            />
            <WordChips list={words(s.text)} />
            {media !== 'none' && (
              <div style={{ display: 'flex', gap: 10, alignItems: 'flex-start' }}>
                {(media === 'image' || media === 'both') && (
                  <ImagePicker url={s.imageUrl} onChange={(url) => update(s.id, { imageUrl: url })} />
                )}
                {(media === 'audio' || media === 'both') && (
                  <AudioPicker
                    accent={theme.accent}
                    url={s.audioUrl}
                    name={s.audioName}
                    onChange={(url, name) => update(s.id, { audioUrl: url, audioName: name })}
                  />
                )}
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
        {items.length === 0 && <EmptyRow text="Hali gap qo'shilmadi" />}
      </ScrollList>

      <AddPanel
        label="Yangi gap qo'shish"
        placeholder="To'g'ri gapni yozing, Enter bosing"
        value={draft}
        onChange={setDraft}
        onAdd={add}
        hint="Enter — gapni saqlaydi va keyingi gapga o'tadi. So'zlar bo'sh joy bo'yicha ajratiladi."
      />
    </>
  )
}

// ============================ Gap tuzish · variant tanlash ============================

export function SentenceChoiceEditor({ data, onChange, active, setActive, theme }: EditorProps) {
  const [draft, setDraft] = useState('')
  const [optDrafts, setOptDrafts] = useState<Record<string, string>>({})
  const items = data.sentenceChoice?.items ?? []

  const patch = (next: ChoiceItem[]) => onChange({ ...data, sentenceChoice: { items: next } })
  const update = (id: string, fields: Partial<ChoiceItem>) => patch(items.map((it) => (it.id === id ? { ...it, ...fields } : it)))

  const add = () => {
    const prompt = draft.trim()
    if (!prompt) return
    patch([...items, { id: uid('q'), prompt, options: [], correctId: null }])
    setDraft('')
    setActive(items.length)
  }

  const addOption = (item: ChoiceItem) => {
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
        {items.map((q, i) => (
          <ItemCard
            key={q.id}
            active={i === active}
            accent={theme.accent}
            num={i + 1}
            onSelect={() => setActive(i)}
            onRemove={() => {
              patch(items.filter((x) => x.id !== q.id))
              if (active >= items.length - 1) setActive(Math.max(0, items.length - 2))
            }}
          >
            <input
              value={q.prompt}
              onChange={(e) => update(q.id, { prompt: e.target.value })}
              onClick={(e) => e.stopPropagation()}
              placeholder="Savol / ma'no"
              style={softInput}
            />

            <MiniLabel>Variant gaplar — to'g'risini belgilang</MiniLabel>
            <div style={{ display: 'flex', flexDirection: 'column', gap: 7 }} onClick={(e) => e.stopPropagation()}>
              {q.options.map((o) => (
                <div key={o.id} style={{ display: 'flex', alignItems: 'center', gap: 8 }}>
                  <CorrectDot on={q.correctId === o.id} accent={theme.accent} onClick={() => update(q.id, { correctId: o.id })} />
                  <input
                    value={o.text}
                    onChange={(e) => update(q.id, { options: q.options.map((x) => (x.id === o.id ? { ...x, text: e.target.value } : x)) })}
                    placeholder="Variant matni"
                    style={optInput}
                  />
                  <RemoveBtn
                    size={16}
                    onClick={() =>
                      update(q.id, {
                        options: q.options.filter((x) => x.id !== o.id),
                        correctId: q.correctId === o.id ? null : q.correctId,
                      })
                    }
                  />
                </div>
              ))}
              <div style={{ display: 'flex', gap: 8 }}>
                <input
                  value={optDrafts[q.id] ?? ''}
                  onChange={(e) => setOptDrafts((d) => ({ ...d, [q.id]: e.target.value }))}
                  onKeyDown={(e) => {
                    if (e.key === 'Enter') {
                      e.preventDefault()
                      addOption(q)
                    }
                  }}
                  placeholder="Yangi variant"
                  style={optInput}
                />
                <button
                  type="button"
                  onClick={() => addOption(q)}
                  style={{ ...sans, background: '#fff', border: `1px solid ${theme.accent}55`, color: theme.accent, fontWeight: 600, fontSize: 13.5, padding: '0 14px', borderRadius: 9, cursor: 'pointer' }}
                >
                  + Qo'shish
                </button>
              </div>
            </div>
          </ItemCard>
        ))}
        {items.length === 0 && <EmptyRow text="Hali savol qo'shilmadi" />}
      </ScrollList>

      <AddPanel
        label="Yangi savol qo'shish"
        placeholder="Savol yoki ma'noni yozing, Enter bosing"
        value={draft}
        onChange={setDraft}
        onAdd={add}
        hint="Har bir savolga bir nechta variant gap yozib, to'g'risini belgilang."
      />
    </>
  )
}
