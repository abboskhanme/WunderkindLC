/**
 * MATN VA MOSLASHTIRISH tahrirlovchilari:
 *   • ReadingEditor  — o'qish matni + savollar (variant / to'g'ri-xato / bo'sh joy / qisqa javob);
 *   • TestEditor     — rasmli / rasmli variantli / audio test savollari;
 *   • WritingEditor  — mavzu bo'yicha matn yozish topshirig'i;
 *   • SpeakingEditor — mavzu bo'yicha gapirish topshirig'i;
 *   • MatchingEditor — juftlarni moslashtirish jadvali.
 */
import { useState } from 'react'
import { sans, display } from '../catalog'
import { AddPanel, AudioPicker, ImagePicker, EmptyRow, ItemCard, RemoveBtn, ScrollList, SectionHead, inputStyle, softInput } from '../kit'
import { colLetter, uid } from '../model'
import type { MatchRow, ReadingItem, TestItem } from '../model'
import { CorrectDot, HintList, MiniLabel, NumberField, optInput } from './common'
import type { EditorProps } from './common'

// ============================ Reading ============================

export function ReadingEditor({ data, onChange, active, setActive, theme }: EditorProps) {
  const [draft, setDraft] = useState('')
  const [optDrafts, setOptDrafts] = useState<Record<string, string>>({})
  const reading = data.reading ?? { passage: '', items: [] }
  const items = reading.items
  const isWrite = data.kind === 'reading-fill' || data.kind === 'reading-short'
  const isFill = data.kind === 'reading-fill'

  const patch = (next: ReadingItem[]) => onChange({ ...data, reading: { ...reading, items: next } })
  const update = (id: string, fields: Partial<ReadingItem>) => patch(items.map((it) => (it.id === id ? { ...it, ...fields } : it)))

  const add = () => {
    const q = draft.trim()
    if (!q) return
    patch([...items, { id: uid('r'), q, options: [], correctId: null, answer: '' }])
    setDraft('')
    setActive(items.length)
  }

  const addOption = (item: ReadingItem) => {
    const text = (optDrafts[item.id] ?? '').trim()
    if (!text) return
    const opt = { id: uid('o'), text }
    update(item.id, { options: [...item.options, opt], correctId: item.correctId ?? opt.id })
    setOptDrafts((d) => ({ ...d, [item.id]: '' }))
  }

  const wordCount = reading.passage.trim() ? reading.passage.trim().split(/\s+/).length : 0

  return (
    <>
      <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between' }}>
        <h2 style={{ margin: 0, fontWeight: 600, fontSize: 15, letterSpacing: '.02em', textTransform: 'uppercase', color: '#777a82', ...display }}>O'qish matni</h2>
        <span style={{ fontSize: 12.5, fontWeight: 600, color: '#777a82', background: '#f7f5f1', padding: '4px 11px', borderRadius: 20 }}>{wordCount} so'z</span>
      </div>
      <textarea
        value={reading.passage}
        onChange={(e) => onChange({ ...data, reading: { ...reading, passage: e.target.value } })}
        placeholder="Foydalanuvchi o'qiydigan matnni shu yerga kiriting…"
        rows={6}
        style={{ ...inputStyle, resize: 'vertical', lineHeight: 1.6, fontSize: 15 }}
      />

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
              value={q.q}
              onChange={(e) => update(q.id, { q: e.target.value })}
              onClick={(e) => e.stopPropagation()}
              placeholder={isFill ? "Bo'sh joyli gap: Ali maktabga ___ boradi" : 'Savol matni'}
              style={softInput}
            />

            {isWrite ? (
              <div style={{ display: 'flex', flexDirection: 'column', gap: 6 }} onClick={(e) => e.stopPropagation()}>
                <MiniLabel>To'g'ri javob</MiniLabel>
                <input value={q.answer} onChange={(e) => update(q.id, { answer: e.target.value })} placeholder="masalan: By bus" style={optInput} />
                <span style={{ fontSize: 11.5, color: '#777a82' }}>Bir nechta to'g'ri javob bo'lsa "/" bilan ajrating.</span>
              </div>
            ) : (
              <div style={{ display: 'flex', flexDirection: 'column', gap: 7 }} onClick={(e) => e.stopPropagation()}>
                <MiniLabel>Variantlar — to'g'risini belgilang</MiniLabel>
                {q.options.map((o) => (
                  <div key={o.id} style={{ display: 'flex', alignItems: 'center', gap: 8 }}>
                    <CorrectDot on={q.correctId === o.id} accent={theme.accent} onClick={() => update(q.id, { correctId: o.id })} />
                    <input
                      value={o.text}
                      onChange={(e) => update(q.id, { options: q.options.map((x) => (x.id === o.id ? { ...x, text: e.target.value } : x)) })}
                      placeholder="Variant"
                      style={optInput}
                    />
                    <RemoveBtn size={16} onClick={() => update(q.id, { options: q.options.filter((x) => x.id !== o.id), correctId: q.correctId === o.id ? null : q.correctId })} />
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
            )}
          </ItemCard>
        ))}
        {items.length === 0 && <EmptyRow text="Hali savol qo'shilmadi" />}
      </ScrollList>

      <AddPanel
        label="Yangi savol qo'shish"
        placeholder={isFill ? "Bo'sh joyli gapni yozing (___), Enter bosing" : 'Savolni yozing, Enter bosing'}
        value={draft}
        onChange={setDraft}
        onAdd={add}
      />
    </>
  )
}

// ============================ Test ============================

export function TestEditor({ data, onChange, active, setActive, theme }: EditorProps) {
  const [draft, setDraft] = useState('')
  const [optDrafts, setOptDrafts] = useState<Record<string, string>>({})
  const items = data.test?.items ?? []
  const imageOpts = data.kind === 'test-imageopts'

  const patch = (next: TestItem[]) => onChange({ ...data, test: { items: next } })
  const update = (id: string, fields: Partial<TestItem>) => patch(items.map((it) => (it.id === id ? { ...it, ...fields } : it)))

  const add = () => {
    const q = draft.trim()
    if (!q) return
    patch([...items, { id: uid('t'), q, explain: '', options: [], correctId: null }])
    setDraft('')
    setActive(items.length)
  }

  const addOption = (item: TestItem) => {
    const text = (optDrafts[item.id] ?? '').trim()
    if (!text && !imageOpts) return
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
            <input value={q.q} onChange={(e) => update(q.id, { q: e.target.value })} onClick={(e) => e.stopPropagation()} placeholder="Savol matni" style={softInput} />

            {data.kind === 'test-image' && (
              <div style={{ display: 'flex', gap: 10, alignItems: 'flex-start' }}>
                <ImagePicker url={q.imageUrl} onChange={(url) => update(q.id, { imageUrl: url })} size={92} />
                <span style={{ fontSize: 12, color: '#777a82' }}>Savol rasmi</span>
              </div>
            )}
            {data.kind === 'test-audio' && (
              <AudioPicker accent={theme.accent} url={q.audioUrl} name={q.audioName} onChange={(url, name) => update(q.id, { audioUrl: url, audioName: name })} />
            )}

            <div style={{ display: 'flex', flexDirection: 'column', gap: 8 }} onClick={(e) => e.stopPropagation()}>
              <MiniLabel>{imageOpts ? "Rasmli variantlar — to'g'risini belgilang" : "Variantlar — to'g'risini belgilang"}</MiniLabel>
              {q.options.map((o) => (
                <div key={o.id} style={{ display: 'flex', alignItems: 'center', gap: 8 }}>
                  <CorrectDot on={q.correctId === o.id} accent={theme.accent} onClick={() => update(q.id, { correctId: o.id })} />
                  {imageOpts && (
                    <ImagePicker
                      url={o.imageUrl}
                      size={46}
                      radius={9}
                      label=""
                      onChange={(url) => update(q.id, { options: q.options.map((x) => (x.id === o.id ? { ...x, imageUrl: url } : x)) })}
                    />
                  )}
                  <input
                    value={o.text}
                    onChange={(e) => update(q.id, { options: q.options.map((x) => (x.id === o.id ? { ...x, text: e.target.value } : x)) })}
                    placeholder={imageOpts ? 'Izoh (ixtiyoriy)' : 'Variant'}
                    style={optInput}
                  />
                  <RemoveBtn size={16} onClick={() => update(q.id, { options: q.options.filter((x) => x.id !== o.id), correctId: q.correctId === o.id ? null : q.correctId })} />
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
                  placeholder={imageOpts ? 'Variant izohi (yoki bo\'sh)' : 'Yangi variant'}
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

            <input
              value={q.explain ?? ''}
              onChange={(e) => update(q.id, { explain: e.target.value })}
              onClick={(e) => e.stopPropagation()}
              placeholder="Izoh — xato javobda ko'rsatiladi (ixtiyoriy)"
              style={optInput}
            />
          </ItemCard>
        ))}
        {items.length === 0 && <EmptyRow text="Hali savol qo'shilmadi" />}
      </ScrollList>

      <AddPanel label="Yangi savol qo'shish" placeholder="Savolni yozing, Enter bosing" value={draft} onChange={setDraft} onAdd={add} />
    </>
  )
}

// ============================ Writing ============================

export function WritingEditor({ data, onChange, theme }: EditorProps) {
  const [hintDraft, setHintDraft] = useState('')
  const w = data.writing ?? { topic: '', prompt: '', minWords: 60, minutes: 15, hints: [] }
  const set = (fields: Partial<typeof w>) => onChange({ ...data, writing: { ...w, ...fields } })

  return (
    <>
      <SectionHead title="Topshiriq" count={w.topic.trim() ? 'tayyor' : "to'ldirilmagan"} />

      <label style={{ display: 'flex', flexDirection: 'column', gap: 6 }}>
        <MiniLabel>Mavzu</MiniLabel>
        <input value={w.topic} onChange={(e) => set({ topic: e.target.value })} placeholder="Masalan: Mening yozgi ta'tilim" style={inputStyle} />
      </label>

      <label style={{ display: 'flex', flexDirection: 'column', gap: 6 }}>
        <MiniLabel>Topshiriq matni (ixtiyoriy)</MiniLabel>
        <textarea
          value={w.prompt}
          onChange={(e) => set({ prompt: e.target.value })}
          placeholder="Nimalar haqida yozish kerakligini tushuntiring…"
          rows={4}
          style={{ ...inputStyle, resize: 'vertical', lineHeight: 1.6, fontSize: 15 }}
        />
      </label>

      <div style={{ display: 'flex', gap: 12, flexWrap: 'wrap' }}>
        <NumberField label="Eng kam so'z" value={w.minWords} onChange={(v) => set({ minWords: v })} max={2000} suffix="so'z" />
        <NumberField label="Vaqt" value={w.minutes} onChange={(v) => set({ minutes: v })} max={300} suffix="daqiqa" />
      </div>

      <div style={{ display: 'flex', flexDirection: 'column', gap: 8 }}>
        <MiniLabel>Yordamchi so'zlar / g'oyalar</MiniLabel>
        <HintList
          hints={w.hints}
          draft={hintDraft}
          onDraft={setHintDraft}
          onAdd={() => {
            const v = hintDraft.trim()
            if (!v) return
            set({ hints: [...w.hints, v] })
            setHintDraft('')
          }}
          onRemove={(i) => set({ hints: w.hints.filter((_, j) => j !== i) })}
          accent={theme.accent}
        />
      </div>
    </>
  )
}

// ============================ Speaking ============================

export function SpeakingEditor({ data, onChange, theme }: EditorProps) {
  const [hintDraft, setHintDraft] = useState('')
  const s = data.speaking ?? { topic: '', prompt: '', prepSec: 30, speakSec: 90, hints: [] }
  const set = (fields: Partial<typeof s>) => onChange({ ...data, speaking: { ...s, ...fields } })

  return (
    <>
      <SectionHead title="Topshiriq" count={s.topic.trim() ? 'tayyor' : "to'ldirilmagan"} />

      <label style={{ display: 'flex', flexDirection: 'column', gap: 6 }}>
        <MiniLabel>Mavzu</MiniLabel>
        <input value={s.topic} onChange={(e) => set({ topic: e.target.value })} placeholder="Masalan: Sevimli kitobingiz haqida gapiring" style={inputStyle} />
      </label>

      <label style={{ display: 'flex', flexDirection: 'column', gap: 6 }}>
        <MiniLabel>Topshiriq matni (ixtiyoriy)</MiniLabel>
        <textarea
          value={s.prompt}
          onChange={(e) => set({ prompt: e.target.value })}
          placeholder="Nimalarni aytib berish kerakligini tushuntiring…"
          rows={4}
          style={{ ...inputStyle, resize: 'vertical', lineHeight: 1.6, fontSize: 15 }}
        />
      </label>

      <div style={{ display: 'flex', gap: 12, flexWrap: 'wrap' }}>
        <NumberField label="Tayyorlanish" value={s.prepSec} onChange={(v) => set({ prepSec: v })} max={600} suffix="sek" />
        <NumberField label="Gapirish" value={s.speakSec} onChange={(v) => set({ speakSec: v })} max={1800} suffix="sek" />
      </div>

      <div style={{ display: 'flex', flexDirection: 'column', gap: 8 }}>
        <MiniLabel>Yordamchi savollar / g'oyalar</MiniLabel>
        <HintList
          hints={s.hints}
          draft={hintDraft}
          onDraft={setHintDraft}
          onAdd={() => {
            const v = hintDraft.trim()
            if (!v) return
            set({ hints: [...s.hints, v] })
            setHintDraft('')
          }}
          onRemove={(i) => set({ hints: s.hints.filter((_, j) => j !== i) })}
          accent={theme.accent}
        />
      </div>
    </>
  )
}

// ============================ Moslashtirish ============================

export function MatchingEditor({ data, onChange, theme }: EditorProps) {
  const [draft, setDraft] = useState('')
  const m = data.matching ?? { statement: '', passage: '', startNum: 1, colCount: 4, colLabels: {}, rows: [] }
  const set = (fields: Partial<typeof m>) => onChange({ ...data, matching: { ...m, ...fields } })
  const cols = Array.from({ length: m.colCount }, (_, i) => i)

  const addRow = () => {
    const text = draft.trim()
    if (!text) return
    const row: MatchRow = { id: uid('m'), text, key: 0 }
    set({ rows: [...m.rows, row] })
    setDraft('')
  }

  return (
    <>
      <SectionHead title="Savollar ro'yxati" count={`${m.rows.length} ta element`} />

      <label style={{ display: 'flex', flexDirection: 'column', gap: 6 }}>
        <MiniLabel>Savol matni</MiniLabel>
        <input value={m.statement} onChange={(e) => set({ statement: e.target.value })} placeholder="Quyidagilarni moslang…" style={inputStyle} />
      </label>

      {data.kind === 'matching-reading' && (
        <label style={{ display: 'flex', flexDirection: 'column', gap: 6 }}>
          <MiniLabel>O'qish matni</MiniLabel>
          <textarea
            value={m.passage}
            onChange={(e) => set({ passage: e.target.value })}
            placeholder="Matnni kiriting…"
            rows={4}
            style={{ ...inputStyle, resize: 'vertical', lineHeight: 1.6, fontSize: 15 }}
          />
        </label>
      )}
      {data.kind === 'matching-audio' && (
        <div style={{ display: 'flex', flexDirection: 'column', gap: 6 }}>
          <MiniLabel>Audio</MiniLabel>
          <AudioPicker accent={theme.accent} url={m.audioUrl} name={m.audioName} onChange={(url, name) => set({ audioUrl: url, audioName: name })} />
        </div>
      )}

      {/* Chap ustun elementlari */}
      <ScrollList>
        {m.rows.map((r, i) => (
          <div key={r.id} style={{ display: 'flex', alignItems: 'center', gap: 10, background: '#fff', border: '1.5px solid #eceef2', borderRadius: 12, padding: '10px 12px' }}>
            <span style={{ flex: 'none', width: 24, height: 24, borderRadius: 7, background: theme.head, color: theme.accent, fontSize: 12, fontWeight: 700, display: 'flex', alignItems: 'center', justifyContent: 'center', ...display }}>
              {m.startNum + i}
            </span>
            <input
              value={r.text}
              onChange={(e) => set({ rows: m.rows.map((x) => (x.id === r.id ? { ...x, text: e.target.value } : x)) })}
              placeholder="Element matni"
              style={{ ...optInput, border: 'none', background: 'transparent', fontSize: 15, fontWeight: 500 }}
            />
            <RemoveBtn onClick={() => set({ rows: m.rows.filter((x) => x.id !== r.id) })} />
          </div>
        ))}
        {m.rows.length === 0 && <EmptyRow text="Hali element qo'shilmadi" />}
      </ScrollList>

      <AddPanel label="Yangi element" placeholder="Masalan: apple" value={draft} onChange={setDraft} onAdd={addRow} btnLabel="+ Qo'shish" />

      {/* Javob harflari */}
      <div style={{ display: 'flex', alignItems: 'center', gap: 10 }}>
        <MiniLabel>Javob harflari</MiniLabel>
        <div style={{ display: 'flex', alignItems: 'center', gap: 8, marginLeft: 'auto' }}>
          <button
            type="button"
            onClick={() => set({ colCount: Math.max(2, m.colCount - 1) })}
            style={{ ...sans, width: 30, height: 30, borderRadius: 8, border: '1px solid #e3e4e8', background: '#fff', color: '#4a4d56', fontSize: 16, cursor: 'pointer' }}
          >
            −
          </button>
          <span style={{ fontSize: 13.5, fontWeight: 700, color: theme.accent, ...display }}>
            A–{colLetter(m.colCount - 1)}
          </span>
          <button
            type="button"
            onClick={() => set({ colCount: Math.min(9, m.colCount + 1) })}
            style={{ ...sans, width: 30, height: 30, borderRadius: 8, border: '1px solid #e3e4e8', background: '#fff', color: '#4a4d56', fontSize: 16, cursor: 'pointer' }}
          >
            +
          </button>
        </div>
      </div>

      {/* Harflar ma'nosi */}
      <div style={{ display: 'flex', flexDirection: 'column', gap: 7 }}>
        <MiniLabel>Harflar ma'nosi</MiniLabel>
        {cols.map((c) => (
          <div key={c} style={{ display: 'flex', alignItems: 'center', gap: 8 }}>
            <span style={{ flex: 'none', width: 24, height: 24, borderRadius: 7, background: theme.head, color: theme.accent, fontSize: 12, fontWeight: 700, display: 'flex', alignItems: 'center', justifyContent: 'center', ...display }}>
              {colLetter(c)}
            </span>
            <input
              value={m.colLabels[c] ?? ''}
              onChange={(e) => set({ colLabels: { ...m.colLabels, [c]: e.target.value } })}
              placeholder={`${colLetter(c)} varianti`}
              style={optInput}
            />
          </div>
        ))}
      </div>

      {/* To'g'ri javoblar jadvali */}
      {m.rows.length > 0 && (
        <div style={{ display: 'flex', flexDirection: 'column', gap: 7 }}>
          <MiniLabel>To'g'ri javoblar — katakni bosing</MiniLabel>
          <div style={{ overflowX: 'auto' }}>
            <table style={{ borderCollapse: 'collapse', width: '100%', border: '1px solid #e3e4e8', background: '#fff', borderRadius: 8 }}>
              <tbody>
                <tr style={{ background: '#fbfaf7' }}>
                  <td style={{ padding: '6px 8px' }} />
                  {cols.map((c) => (
                    <td key={c} style={{ padding: '6px 0', textAlign: 'center', fontSize: 12, fontWeight: 700, color: '#777a82', borderLeft: '1px solid #e3e4e8', ...display }}>
                      {colLetter(c)}
                    </td>
                  ))}
                </tr>
                {m.rows.map((r, ri) => (
                  <tr key={r.id}>
                    <td style={{ padding: '6px 8px', fontSize: 13, fontWeight: 600, color: '#181a22', borderTop: '1px solid #e3e4e8', whiteSpace: 'nowrap' }}>
                      {m.startNum + ri}. {r.text}
                    </td>
                    {cols.map((c) => (
                      <td key={c} style={{ padding: 3, borderLeft: '1px solid #e3e4e8', borderTop: '1px solid #e3e4e8' }}>
                        <button
                          type="button"
                          className="dc-cell"
                          onClick={() => set({ rows: m.rows.map((x) => (x.id === r.id ? { ...x, key: c } : x)) })}
                          style={{
                            width: '100%', height: 26, borderRadius: 5, border: 'none', cursor: 'pointer', fontSize: 12, fontWeight: 700,
                            background: r.key === c ? theme.accent : 'transparent',
                            color: '#fff',
                          }}
                        >
                          {r.key === c ? '✓' : ''}
                        </button>
                      </td>
                    ))}
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </div>
      )}
    </>
  )
}
