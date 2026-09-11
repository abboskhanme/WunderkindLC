/**
 * TOPSHIRIQ KONSTRUKTORI — to'liq ish maydoni (o'quv dasturining OXIRGI bosqichida ochiladi).
 *
 * Ikki holat:
 *   1) tur tanlanmagan → "Topshiriq yaratish" (ExercisePicker) ekrani;
 *   2) tur tanlangan  → sarlavha + tur banneri + chapda tahrirlovchi, o'ngda jonli
 *      "foydalanuvchi ko'rinishi" (aynan shu komponent o'quvchi portalida ham ishlaydi).
 */
import { useMemo, useState } from 'react'
import type { ReactElement } from 'react'
// Konstruktor CSS'i (`.dc-*`) shu yerda — main.tsx'da EMAS: faqat mashq ekranlari bilan yuklanadi
import '@/styles/exercise.css'
import { UI, kindInfo, kindTheme, sans } from './catalog'
import { ExercisePicker } from './ExercisePicker'
import {
  BlankToggle, ConfirmExit, ConstructorHeader, EditorPane, PhoneFrame, PreviewPane, Split, Toast, TypeBanner, useToast,
} from './kit'
import { ExercisePlayer } from './players'
import { emptyExercise, exerciseCount, kindFamily, parseExercise } from './model'
import type { ExerciseData, ExerciseKind } from './model'
import { SentenceChoiceEditor, SentenceEditor } from './editors/sentence'
import { FillEditor, WordFindEditor, WordPickEditor } from './editors/blanks'
import { MatchingEditor, ReadingEditor, SpeakingEditor, TestEditor, WritingEditor } from './editors/content'
import type { EditorProps } from './editors/common'

interface Props {
  /** Topshiriq nomi — sarlavhada ko'rinadi. */
  itemName: string
  initialKind: string
  initialJson: string
  /** Saqlash — chaqiruvchi (topshiriq sahifasi) serverga yozadi. */
  onSave: (kind: ExerciseKind, json: string) => Promise<void>
  /** Konstruktordan chiqish (topshiriqlar ro'yxatiga qaytish). */
  onExit: () => void
}

const EDITORS: Record<string, (p: EditorProps) => ReactElement> = {
  sentence: SentenceEditor,
  'sentence-choice': SentenceChoiceEditor,
  fill: FillEditor,
  wordpick: WordPickEditor,
  wordfind: WordFindEditor,
  reading: ReadingEditor,
  test: TestEditor,
  writing: WritingEditor,
  speaking: SpeakingEditor,
  matching: MatchingEditor,
}

/** Chap panel pastidagi maslahat matni (maketdagi kabi). */
const PREVIEW_HINT: Record<string, string> = {
  sentence: "Chapdagi gapni tanlab, foydalanuvchidek sinab ko'ring.",
  'sentence-choice': "Chapdagi savolni tanlab, foydalanuvchidek sinab ko'ring.",
  fill: "Chapdagi savolni tanlab, foydalanuvchidek sinab ko'ring.",
  wordpick: "Chapdagi gapni tanlab, foydalanuvchidek sinab ko'ring.",
  wordfind: "Chapdagi savolni tanlab, foydalanuvchidek sinab ko'ring.",
  reading: "Chapdagi savolni tanlab, foydalanuvchidek sinab ko'ring.",
  test: "Chapdagi savolni tanlab, foydalanuvchidek sinab ko'ring.",
  writing: 'Foydalanuvchi mavzu bo\'yicha matn yozadi va topshiradi.',
  speaking: "Mikrofon tugmasini bosib sinab ko'ring.",
  matching: "Kataklarni bosib, foydalanuvchidek sinab ko'ring.",
}

export function ExerciseWorkspace({ itemName, initialKind, initialJson, onSave, onExit }: Props) {
  const [data, setData] = useState<ExerciseData | null>(() => parseExercise(initialKind, initialJson))
  const [picking, setPicking] = useState(!initialKind)
  const [active, setActive] = useState(0)
  const [dirty, setDirty] = useState(false)
  const [saving, setSaving] = useState(false)
  const [saved, setSaved] = useState(false)
  const [confirm, setConfirm] = useState(false)
  const { toast, setToast } = useToast()

  // DIQQAT: boshlang'ich qiymatlar faqat MOUNT paytida o'qiladi — chaqiruvchi topshiriq
  // almashganda `key={itemId}` bilan komponentni qaytadan yaratadi (saqlagandan keyin
  // tahrirlash holati, tanlangan element va h.k. yo'qolib ketmasin).
  const theme = useMemo(() => kindTheme((data?.kind ?? 'sentence-order') as ExerciseKind), [data?.kind])
  const info = data ? kindInfo(data.kind) : undefined
  const family = data ? kindFamily(data.kind) : 'sentence'

  const change = (next: ExerciseData) => {
    setData(next)
    setDirty(true)
    setSaved(false)
  }

  const pick = (kind: ExerciseKind) => {
    // Tur o'zgarsa mazmun ham almashadi (har turning o'z shakli bor).
    setData((prev) => (prev && prev.kind === kind ? prev : emptyExercise(kind)))
    setPicking(false)
    setActive(0)
    if (!data || data.kind !== kind) setDirty(true)
  }

  const save = async () => {
    if (!data || saving) return
    setSaving(true)
    try {
      await onSave(data.kind, JSON.stringify(data))
      setDirty(false)
      setSaved(true)
      setToast('Mashq saqlandi')
      setTimeout(() => setSaved(false), 2000)
    } finally {
      setSaving(false)
    }
  }

  const exit = () => {
    if (dirty) setConfirm(true)
    else onExit()
  }

  // Tur tanlash — KATTA MODAL KARTA sifatida konstruktor ustida ochiladi.
  const picker = picking ? (
    <ExercisePicker
      current={data?.kind ?? null}
      onPick={pick}
      onClose={() => setPicking(false)}
      subtitle={itemName || 'Yangi mashq'}
    />
  ) : null

  // Tur hali tanlanmagan (yangi topshiriq) — bo'sh holat + tanlash tugmasi.
  if (!data) {
    return (
      <div className="dc-root" style={{ background: '#fff', minHeight: 420, display: 'flex', flexDirection: 'column', borderRadius: 16, overflow: 'hidden', border: `1px solid ${UI.line}` }}>
        <ConstructorHeader subtitle={itemName || 'Yangi mashq'} accent={UI.accent} onCancel={onExit} hideSave />
        <div style={{ flex: 1, display: 'flex', flexDirection: 'column', alignItems: 'center', justifyContent: 'center', gap: 14, padding: '60px 24px', textAlign: 'center' }}>
          <p style={{ margin: 0, fontSize: 15, color: UI.muted }}>Mashq turi hali tanlanmagan.</p>
          <button
            type="button"
            onClick={() => setPicking(true)}
            style={{ ...sans, background: UI.accent, border: 'none', color: '#fff', fontWeight: 600, fontSize: 14.5, padding: '11px 20px', borderRadius: 11, cursor: 'pointer' }}
          >
            Turini tanlash
          </button>
        </div>
        {picker}
      </div>
    )
  }

  const Editor = EDITORS[family] ?? SentenceEditor
  const count = exerciseCount(data)

  return (
    <div className="dc-root" style={{ background: '#fff', minHeight: '100%', display: 'flex', flexDirection: 'column', borderRadius: 16, overflow: 'hidden', border: `1px solid ${UI.line}` }}>
      {toast && <Toast text={toast} />}
      {confirm && <ConfirmExit onStay={() => setConfirm(false)} onLeave={onExit} />}
      {picker}

      <ConstructorHeader subtitle={itemName || 'Yangi mashq'} accent={theme.accent} saving={saving} saved={saved} onCancel={exit} onSave={save} />

      <TypeBanner
        accent={theme.accent}
        icon={info?.type.icon ?? 'blocks'}
        title={info?.cat.label ?? 'Mashq'}
        badge={info?.type.name ?? ''}
        desc={info?.type.desc ?? ''}
        extra={
          <>
            {family === 'fill' && data.fill && (
              <BlankToggle
                value={data.fill.blank}
                accent={theme.accent}
                onChange={(v) => change({ ...data, fill: { blank: v, items: data.fill?.items ?? [] } })}
              />
            )}
            {family === 'wordfind' && data.wordfind && (
              <BlankToggle
                value={data.wordfind.blank}
                accent={theme.accent}
                onChange={(v) => change({ ...data, wordfind: { blank: v, items: data.wordfind?.items ?? [] } })}
              />
            )}
          </>
        }
      />

      <Split>
        <EditorPane>
          <Editor data={data} onChange={change} active={active} setActive={setActive} theme={theme} />
        </EditorPane>

        <PreviewPane accent={theme.accent} hint={PREVIEW_HINT[family] ?? ''}>
          <PhoneFrame>
            <ExercisePlayer data={data} index={Math.min(active, Math.max(0, count - 1))} onIndex={setActive} mode="preview" />
          </PhoneFrame>
        </PreviewPane>
      </Split>
    </div>
  )
}
