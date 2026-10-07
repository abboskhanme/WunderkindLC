// @vitest-environment jsdom
import { act } from 'react'
import { createRoot, type Root } from 'react-dom/client'
import { MemoryRouter } from 'react-router-dom'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { cameBackAfterLoss, eventTypeLabels, isLeadLost } from '../leadLabels'
import { LeadLostModal } from '../LeadLostModal'

/**
 * «YO'QOTILGAN LIDLAR» — klient qoidalari (`.claude/rules/crm-leads.md` «YO'QOTILGAN LIDLAR»):
 * yo'qotilganlik ta'rifi, "qaytib keldi" belgisi va sabab oynasida SABAB MAJBURIYLIGI.
 */

vi.mock('@/api/services/actionReasons', () => ({
  getActionReasons: vi.fn(async () => [
    { id: 'r1', category: 'lead_lost', label: 'Narx qimmatlik qildi', order: 0 },
    { id: 'r2', category: 'lead_lost', label: "Vaqt to'g'ri kelmadi", order: 1 },
    // Boshqa kategoriya — oynada CHIQMASLIGI kerak.
    { id: 'x', category: 'lead_delete', label: 'Takror / spam', order: 0 },
  ]),
}))

;(globalThis as { IS_REACT_ACT_ENVIRONMENT?: boolean }).IS_REACT_ACT_ENVIRONMENT = true

describe("yo'qotilganlik ta'rifi", () => {
  it("faqat lostAt bo'lsa yo'qotilgan", () => {
    expect(isLeadLost({ lostAt: '2026-10-01T10:00:00' })).toBe(true)
    expect(isLeadLost({ lostAt: null })).toBe(false)
    expect(isLeadLost({})).toBe(false)
  })

  it("yo'qotilgandan KEYIN yana murojaat qilgan — «qaytib keldi»", () => {
    const lostAt = '2026-10-01T10:00:00'
    expect(cameBackAfterLoss({ lostAt, lastRepeatAt: '2026-10-03T09:00:00' })).toBe(true)
    // Yo'qotishdan OLDINGI takroriy murojaat hisobga olinmaydi.
    expect(cameBackAfterLoss({ lostAt, lastRepeatAt: '2026-09-20T09:00:00' })).toBe(false)
    expect(cameBackAfterLoss({ lostAt: null, lastRepeatAt: '2026-10-03T09:00:00' })).toBe(false)
  })

  it('tarix lentasida yangi hodisa turlarining yorlig\'i bor', () => {
    expect(eventTypeLabels.lost).toBeTruthy()
    expect(eventTypeLabels.restore).toBeTruthy()
  })
})

describe('LeadLostModal — sabab MAJBURIY', () => {
  let host: HTMLDivElement
  let root: Root

  beforeEach(() => {
    host = document.createElement('div')
    document.body.appendChild(host)
    root = createRoot(host)
  })

  afterEach(() => {
    act(() => root.unmount())
    host.remove()
  })

  async function render(onConfirm: (reasonId: string, comment: string) => Promise<unknown>) {
    await act(async () => {
      root.render(
        <MemoryRouter>
          <LeadLostModal open leadName="Vali" onClose={() => {}} onConfirm={onConfirm} />
        </MemoryRouter>,
      )
    })
  }

  const button = (text: string) =>
    [...host.querySelectorAll('button')].find((b) => b.textContent?.includes(text)) as HTMLButtonElement

  it('faqat lead_lost kategoriyasidagi sabablar chiqadi', async () => {
    await render(async () => {})
    expect(button('Narx qimmatlik qildi')).toBeTruthy()
    expect(button('Takror / spam')).toBeUndefined()
  })

  it('sababsiz tasdiqlanmaydi — so\'rov YUBORILMAYDI, xato ko\'rinadi', async () => {
    const onConfirm = vi.fn(async () => {})
    await render(onConfirm)
    await act(async () => button("Yo'qotilganlarga o'tkazish").click())
    expect(onConfirm).not.toHaveBeenCalled()
    expect(host.textContent).toContain("sababini tanlang")
  })

  it('sabab tanlansa — tanlangan id va izoh bilan chaqiriladi', async () => {
    const onConfirm = vi.fn(async () => {})
    await render(onConfirm)
    await act(async () => button("Vaqt to'g'ri kelmadi").click())
    await act(async () => button("Yo'qotilganlarga o'tkazish").click())
    expect(onConfirm).toHaveBeenCalledWith('r2', '')
  })

  it("server rad etsa — sabab OYNADA ko'rinadi (jimgina yutilmaydi)", async () => {
    const onConfirm = vi.fn(async () => {
      throw { response: { data: { message: 'Lid allaqachon yo\'qotilganlar ro\'yxatida.' } } }
    })
    await render(onConfirm)
    await act(async () => button('Narx qimmatlik qildi').click())
    await act(async () => button("Yo'qotilganlarga o'tkazish").click())
    expect(host.textContent).toContain("allaqachon yo'qotilganlar ro'yxatida")
  })
})
