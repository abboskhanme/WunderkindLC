import { describe, expect, it } from 'vitest'
import { isEditable, type IgPost, type IgPostStatus } from '../services/instagramContent'

/**
 * Klientdagi `isEditable` serverdagi `InstagramPublishContract.IsEditableStatus` bilan bir xil
 * ro'yxatni qulflaydi (`WunderkindLC.Tests/AuditFixT9Tests.cs` → `Tahrirlanadigan_post_holatlari`).
 *
 * 🔴 Ilgari faqat `scheduled` tahrirlanardi: xato bilan qolgan / bekor qilingan post BOSHI BERK edi,
 * holbuki server xato matnlari "postni tahrirlab, qayta tanlang" derdi.
 */
function post(status: IgPostStatus): IgPost {
  return {
    id: 'p1', postType: 'image', postTypeLabel: 'Rasm', caption: '', media: [],
    options: { shareToFeed: true, locationId: '', collaborators: [], audioName: '' },
    scheduledAt: '2026-10-08T10:00:00', status, statusLabel: '', hasContainer: false,
    containerStatus: '', mediaId: '', permalink: '', attempts: 0, error: '',
    createdBy: '', createdAt: '', publishedAt: '',
  }
}

describe('isEditable — tahrirlanadigan post holatlari', () => {
  it('hali joylanmagan post tahrirlanadi (scheduled · failed · cancelled)', () => {
    expect(isEditable(post('scheduled'))).toBe(true)
    expect(isEditable(post('failed'))).toBe(true)
    expect(isEditable(post('cancelled'))).toBe(true)
  })

  it('joylangan va joylanayotgan post tahrirlanmaydi', () => {
    expect(isEditable(post('published'))).toBe(false)
    expect(isEditable(post('processing'))).toBe(false)
  })
})
