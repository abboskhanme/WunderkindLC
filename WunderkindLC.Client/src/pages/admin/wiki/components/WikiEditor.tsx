import '@blocknote/mantine/style.css'
import { useCreateBlockNote } from '@blocknote/react'
import { BlockNoteView } from '@blocknote/mantine'
import { uz } from '@blocknote/core/locales'
import type { PartialBlock } from '@blocknote/core'
import { wikiSchema } from '../lib/wikiSchema'
import { parseContent } from '../lib/wikiModel'

interface WikiEditorProps {
  /** Saqlangan bloklar (JSON matni) — FAQAT boshlang'ich qiymat. Sahifa almashsa komponent
   *  `key` bilan QAYTA yaratiladi (muharrir ichki holatini tashqaridan "itarib" yangilab bo'lmaydi). */
  initialJson: string
  editable: boolean
  /** Har o'zgarishda — joriy bloklar JSON matni (ota avtosaqlashni o'zi boshqaradi). */
  onChange: (json: string) => void
}

/**
 * Notion'ga o'xshash muharrir (BlockNote, o'zbekcha, FAQAT matn — `wikiSchema`).
 * Alohida fayl ATAYIN: sahifa uni `lazy()` bilan yuklaydi — chap paneldagi daraxt darhol
 * chiziladi, ~0,3 MB muharrir esa orqadan keladi va CRM'ning boshqa sahifalariga umuman tushmaydi.
 */
export default function WikiEditor({ initialJson, editable, onChange }: WikiEditorProps) {
  const editor = useCreateBlockNote({
    schema: wikiSchema,
    dictionary: uz,
    initialContent: parseContent(initialJson) as PartialBlock<typeof wikiSchema.blockSchema>[] | undefined,
  })

  return (
    <BlockNoteView
      editor={editor}
      editable={editable}
      theme="light"
      onChange={() => onChange(JSON.stringify(editor.document))}
      className="wiki-editor"
    />
  )
}
