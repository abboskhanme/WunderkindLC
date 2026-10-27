import { BlockNoteSchema, defaultBlockSpecs } from '@blocknote/core'
import { WIKI_BLOCK_TYPES } from './wikiModel'

/**
 * Muharrir sxemasi — FAQAT MATN bloklari (`WIKI_BLOCK_TYPES`). Rasm, video, audio va fayl
 * bloklari sxemada YO'Q, shuning uchun ular "/" menyusida ham chiqmaydi, joylashtirilgan
 * (paste) rasm ham tushib qoladi. Server ham ularni rad etadi (`WikiRules.AllowedBlockTypes`).
 */
const textBlockSpecs = Object.fromEntries(
  Object.entries(defaultBlockSpecs).filter(([type]) => (WIKI_BLOCK_TYPES as readonly string[]).includes(type)),
) as Pick<typeof defaultBlockSpecs, (typeof WIKI_BLOCK_TYPES)[number]>

export const wikiSchema = BlockNoteSchema.create({ blockSpecs: textBlockSpecs })
