/** Rahbariyat analitikasi (SPEC-backlog F5 D1) — sof yordamchilar. */

/** O'tgan oyga nisbatan o'zgarish: «+12%» / «-5%»; o'tgan oy 0 bo'lsa — ko'rsatilmaydi. */
export function deltaPct(cur: number, prev: number): { value: string; dir: 'up' | 'down' } | undefined {
  if (!prev) return undefined
  const p = Math.round(((cur - prev) / Math.abs(prev)) * 100)
  return { value: `${p > 0 ? '+' : ''}${p}%`, dir: p >= 0 ? 'up' : 'down' }
}

/** Lid konversiyasi % (kelganlardan o'quvchiga aylanganlar); lid bo'lmasa null. */
export const conversionPct = (leads: number, converted: number): number | null =>
  leads > 0 ? Math.round((converted * 100) / leads) : null
