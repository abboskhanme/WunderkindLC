/** Mashq tahrirlovchilari uchun umumiy tiplar va kichik yordamchilar. */
import type { CSSProperties, ReactNode } from 'react'
import type { Theme } from '../catalog'
import { sans } from '../catalog'
import type { ExerciseData } from '../model'

export interface EditorProps {
  data: ExerciseData
  onChange: (next: ExerciseData) => void
  /** Chapdagi ro'yxatda tanlangan element (o'ng paneldagi preview shuni ko'rsatadi). */
  active: number
  setActive: (i: number) => void
  theme: Theme
}

/** Kichik yorliq (maketdagi "Variantlar — to'g'risini belgilang" kabi). */
export function MiniLabel({ children }: { children: ReactNode }) {
  return <span style={{ fontSize: 12, fontWeight: 600, color: '#777a82' }}>{children}</span>
}

/** Kartadagi ikkilamchi (tarjima/izoh) input. */
export const subInput: CSSProperties = {
  ...sans,
  width: '100%',
  fontSize: 13.5,
  fontStyle: 'italic',
  color: '#4a4d56',
  background: 'transparent',
  border: 'none',
  padding: 0,
  outline: 'none',
}

/** Variant qatoridagi input. */
export const optInput: CSSProperties = {
  ...sans,
  flex: 1,
  fontSize: 14,
  color: '#4a4d56',
  background: '#fff',
  border: '1px solid #e3e4e8',
  borderRadius: 9,
  padding: '8px 11px',
  outline: 'none',
}

/** "To'g'ri javob" radiosi. */
export function CorrectDot({ on, accent, onClick }: { on: boolean; accent: string; onClick: () => void }) {
  return (
    <button
      type="button"
      onClick={(e) => {
        e.stopPropagation()
        onClick()
      }}
      title="To'g'ri javob"
      style={{
        flex: 'none', width: 20, height: 20, borderRadius: '50%', cursor: 'pointer', padding: 0,
        border: on ? `6px solid ${accent}` : '1.6px solid #cfd3ff',
        background: '#fff',
      }}
    />
  )
}

/** So'zlarni chip ko'rinishida ko'rsatish (gap tuzishda). */
export function WordChips({ list }: { list: string[] }) {
  return (
    <div style={{ display: 'flex', flexWrap: 'wrap', gap: 6 }}>
      {list.map((w, i) => (
        <span key={i} style={{ fontSize: 13, fontWeight: 500, color: '#4a4d56', background: '#f7f5f1', border: '1px solid #e3e4e8', borderRadius: 7, padding: '4px 8px' }}>
          {w}
        </span>
      ))}
    </div>
  )
}

/** Yordamchi so'z/g'oyalar ro'yxati (writing/speaking). */
export function HintList({
  hints, draft, onDraft, onAdd, onRemove, accent,
}: {
  hints: string[]
  draft: string
  onDraft: (v: string) => void
  onAdd: () => void
  onRemove: (i: number) => void
  accent: string
}) {
  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: 8 }}>
      <div style={{ display: 'flex', flexWrap: 'wrap', gap: 6 }}>
        {hints.map((h, i) => (
          <span key={i} style={{ display: 'inline-flex', alignItems: 'center', gap: 6, fontSize: 13, fontWeight: 600, color: accent, background: '#fff', border: `1px solid ${accent}33`, borderRadius: 20, padding: '6px 11px' }}>
            {h}
            <button type="button" onClick={() => onRemove(i)} style={{ border: 'none', background: 'transparent', color: '#9aa0aa', cursor: 'pointer', fontSize: 14, lineHeight: 1, padding: 0 }}>
              ×
            </button>
          </span>
        ))}
      </div>
      <div style={{ display: 'flex', gap: 8 }}>
        <input
          value={draft}
          onChange={(e) => onDraft(e.target.value)}
          onKeyDown={(e) => {
            if (e.key === 'Enter') {
              e.preventDefault()
              onAdd()
            }
          }}
          placeholder="Yangi yordamchi so'z / savol"
          style={{ ...optInput }}
        />
        <button type="button" onClick={onAdd} style={{ ...sans, background: '#fff', border: `1px solid ${accent}55`, color: accent, fontWeight: 600, fontSize: 13.5, padding: '0 16px', borderRadius: 9, cursor: 'pointer' }}>
          + Qo'shish
        </button>
      </div>
    </div>
  )
}

/** Raqamli maydon (so'z soni, daqiqa, soniya). */
export function NumberField({ label, value, onChange, min = 0, max = 999, suffix }: { label: string; value: number; onChange: (v: number) => void; min?: number; max?: number; suffix?: string }) {
  return (
    <label style={{ display: 'flex', flexDirection: 'column', gap: 6, flex: 1, minWidth: 120 }}>
      <MiniLabel>{label}</MiniLabel>
      <div style={{ display: 'flex', alignItems: 'center', gap: 8 }}>
        <input
          type="number"
          min={min}
          max={max}
          value={value}
          onChange={(e) => onChange(Math.max(min, Math.min(max, Number(e.target.value) || 0)))}
          style={{ ...optInput, maxWidth: 110 }}
        />
        {suffix && <span style={{ fontSize: 13, color: '#777a82' }}>{suffix}</span>}
      </div>
    </label>
  )
}
