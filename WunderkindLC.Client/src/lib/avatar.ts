/**
 * AVATAR yordamchilari — ismdan bosh harflar va BARQAROR fon rangi.
 *
 * Lidlar taxtasi va "Bog'lanish kerak" taxtasi bir xil ko'rinishda bo'lishi uchun yagona manba
 * (ilgari nusxasi `LeadCard.tsx` ichida edi).
 */

/** Ism-sharifdan bosh harflar ("Ali Valiyev" → "AV"). */
export function initials(name: string): string {
  const parts = name.trim().split(/\s+/).filter(Boolean)
  if (parts.length === 0) return '?'
  if (parts.length === 1) return parts[0].slice(0, 2).toUpperCase()
  return (parts[0][0] + parts[1][0]).toUpperCase()
}

/** Ismdan BARQAROR fon rangi — bir odam har doim bir xil rangda ko'rinadi. */
const AVATAR_BG = [
  'oklch(0.7 0.12 30)',
  'oklch(0.65 0.14 350)',
  'oklch(0.6 0.18 282)',
  'oklch(0.62 0.14 158)',
  'oklch(0.65 0.13 230)',
  'oklch(0.72 0.14 70)',
]

export function avatarColor(name: string): string {
  let h = 0
  for (let i = 0; i < name.length; i++) h = (h * 31 + name.charCodeAt(i)) | 0
  return AVATAR_BG[Math.abs(h) % AVATAR_BG.length]
}
