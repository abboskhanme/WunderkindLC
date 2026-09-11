import { useEffect, useState } from 'react'
import type { CSSProperties } from 'react'
import { isHttpsUrl, type IgMediaItem, type IgPostType } from '@/api/services/instagramContent'
import { Icon } from '../mk'
import { isVertical } from './helpers'

/**
 * INSTAGRAM KO'RINISHI — composer'ning o'ng ustunida DOIM turadigan "telefon" maketi.
 *
 * Maqsad — foydalanuvchi nima yasayotganini KO'RIB tursin: nisbat (kvadrat yoki 9:16),
 * matnning qayerda qirqilishi va karuselda nechta element borligi. Piksel-aniq nusxa
 * yasash maqsad EMAS.
 *
 * ⚠️ Bu TAXMINIY ko'rinish: Instagram rasmni o'z nisbatiga qirqishi va sRGB bo'lmagan
 * ranglarni o'zgartirishi mumkin — shu izoh ostida OCHIQ yozib qo'yilgan.
 *
 * ⚠️ KARUSELDA endi elementlar ORASIDA yurish mumkin (strelka + nuqtalar). Ilgari faqat
 * birinchi element ko'rinardi va foydalanuvchi 2–10-elementni umuman tekshira olmasdi.
 *
 * ⚠️ MANZIL KECHIKTIRIB (debounce) chiziladi va yuklanmasa SABABI yoziladi — pastdagi
 * `PREVIEW_DEBOUNCE_MS` izohiga qarang.
 */
export function IgPreview({ type, media, caption }: { type: IgPostType; media: IgMediaItem[]; caption: string }) {
  const vertical = isVertical(type)
  const [index, setIndex] = useState(0)

  // Element o'chirilsa yoki tur almashsa indeks ro'yxatdan tashqarida qolib ketmasin —
  // aks holda preview bo'sh (undefined) ko'rinardi.
  useEffect(() => {
    if (index > media.length - 1) setIndex(Math.max(0, media.length - 1))
  }, [media.length, index])

  const current = media[Math.min(index, Math.max(0, media.length - 1))]
  const many = media.length > 1

  /**
   * Chiziladigan manzil — KECHIKTIRILGAN.
   *
   * ⚠️ `isHttpsUrl` `https://a` ni ham "to'g'ri" deb biladi, ya'ni manzil QO'LDA yozilayotganda
   * har bosilgan harfda yangi `<img>`/`<video>` so'rovi ketardi: konsol xatolarga to'lar,
   * ekranda esa sinuq rasm miltillardi. Endi yozish to'xtagach bir marta so'raladi.
   */
  const rawUrl = current?.url ?? ''
  const [shownUrl, setShownUrl] = useState(rawUrl)
  const [failed, setFailed] = useState(false)

  useEffect(() => {
    const t = setTimeout(() => setShownUrl(rawUrl), PREVIEW_DEBOUNCE_MS)
    return () => clearTimeout(t)
  }, [rawUrl])

  // Manzil (yoki media turi) o'zgardi — eski xato bayrog'i qolib ketmasin.
  useEffect(() => { setFailed(false) }, [shownUrl, current?.kind])

  // "Ko'rsatsa bo'ladimi" — faqat HTTPS manzil chiziladi: boshqa sxema baribir ishlamaydi.
  const show = !!shownUrl && isHttpsUrl(shownUrl) && !failed

  return (
    <div className="mk-phone">
      {/* ── Profil qatori ── */}
      <div className="mk-phone-head">
        <div className="ch-icon ch-instagram" style={{ width: 30, height: 30, borderRadius: 9 }}>
          <Icon name="user" style={{ width: 15, height: 15 }} />
        </div>
        <div style={{ flex: 1, minWidth: 0 }}>
          <div style={{ fontSize: 12.5, fontWeight: 800, lineHeight: 1.2 }}>markazingiz</div>
          <div style={{ fontSize: 10.5, color: 'var(--text-3)' }}>Homiylik · Toshkent</div>
        </div>
        <Icon name="dots" style={{ width: 16, height: 16, color: 'var(--text-3)', flexShrink: 0 }} />
      </div>

      {/* ── Media ── */}
      <div
        className="mk-phone-media"
        style={{
          position: 'relative',
          width: '100%',
          // Nisbat post turidan: story/reels/video — 9:16, qolgani — kvadrat.
          aspectRatio: vertical ? '9 / 16' : '1 / 1',
          background: 'var(--surface-3)',
          display: 'grid',
          placeItems: 'center',
          overflow: 'hidden',
        }}
      >
        {show && current && current.kind === 'image' && (
          <img
            src={shownUrl}
            alt={current.altText || ''}
            style={{ width: '100%', height: '100%', objectFit: 'cover' }}
            onError={() => setFailed(true)}
          />
        )}
        {show && current && current.kind === 'video' && (
          <video
            src={shownUrl}
            poster={current.coverUrl || undefined}
            controls
            style={{ width: '100%', height: '100%', objectFit: 'cover' }}
            onError={() => setFailed(true)}
          />
        )}

        {/* ⚠️ Brauzerning sinuq rasm ikonkasi SABABINI aytmaydi — shuning uchun ochiq matn.
            Bu tekshiruv Meta'ning `2207052` («Media yuklab bo'lmadi») xatosini joylashdan
            OLDIN topib beradi: Instagram ham faylni AYNAN shunday, tashqaridan oladi. */}
        {!show && failed && (
          <div style={{ textAlign: 'center', color: 'var(--danger)', fontSize: 12, padding: 16 }}>
            <Icon name="warn" style={{ width: 26, height: 26 }} />
            <div style={{ marginTop: 8, fontWeight: 700 }}>Rasmni yuklab bo‘lmadi</div>
            <div style={{ marginTop: 3, fontSize: 11.5 }}>
              Manzil ochiq HTTPS’mi? Brauzer ocha olmasa Instagram ham ocha olmaydi.
            </div>
          </div>
        )}

        {!show && !failed && (
          <div style={{ textAlign: 'center', color: 'var(--text-3)', fontSize: 12, padding: 16 }}>
            <Icon name="image" style={{ width: 26, height: 26 }} />
            <div style={{ marginTop: 8, fontWeight: 700 }}>Media manzili kiritilmagan</div>
            <div style={{ marginTop: 3, fontSize: 11.5 }}>
              «Tur va media» bosqichida fayl yuklang yoki ochiq HTTPS manzilni yozing.
            </div>
          </div>
        )}

        {/* Story — 24 soatlik ekani darhol ko'rinsin (lenta postidan farqi shu). */}
        {type === 'story' && (
          <div
            className="badge"
            style={{ position: 'absolute', top: 10, left: 10, background: 'rgba(0,0,0,.55)', color: '#fff' }}
          >
            Story · 24 soat
          </div>
        )}

        {/* Karusel hisoblagichi — Instagram'dagidek o'ng yuqorida. */}
        {many && (
          <div
            className="badge"
            style={{ position: 'absolute', top: 10, right: 10, background: 'rgba(0,0,0,.55)', color: '#fff' }}
          >
            {index + 1}/{media.length}
          </div>
        )}

        {/* Elementlar orasida yurish — faqat karusel/ko'p elementli postda. */}
        {many && (
          <>
            <button
              type="button"
              className="icon-btn"
              aria-label="Oldingi element"
              onClick={() => setIndex((i) => (i - 1 + media.length) % media.length)}
              style={arrowStyle('left')}
            >
              <Icon name="chevLeft" style={{ width: 16, height: 16 }} />
            </button>
            <button
              type="button"
              className="icon-btn"
              aria-label="Keyingi element"
              onClick={() => setIndex((i) => (i + 1) % media.length)}
              style={arrowStyle('right')}
            >
              <Icon name="chevRight" style={{ width: 16, height: 16 }} />
            </button>
          </>
        )}
      </div>

      {/* Nuqtalar — nechanchi elementdaligi bir qarashda ko'rinsin. */}
      {many && (
        <div style={{ display: 'flex', gap: 5, justifyContent: 'center', padding: '8px 0 2px' }}>
          {media.map((_, i) => (
            <button
              key={i}
              type="button"
              aria-label={`${i + 1}-element`}
              onClick={() => setIndex(i)}
              style={{
                width: 6, height: 6, borderRadius: 999, border: 0, padding: 0, cursor: 'pointer',
                background: i === index ? 'var(--primary)' : 'var(--border)',
              }}
            />
          ))}
        </div>
      )}

      {/* ── Amallar qatori (haqiqiy Instagram ko'rinishiga yaqin) ── */}
      <div className="mk-phone-bar">
        <Icon name="heart" style={{ width: 20, height: 20 }} />
        <Icon name="comment" style={{ width: 20, height: 20 }} />
        <Icon name="send" style={{ width: 20, height: 20 }} />
        <Icon name="bookmark" style={{ width: 20, height: 20, marginLeft: 'auto' }} />
      </div>

      {/* ── Matn ── */}
      <div className="mk-phone-cap">
        {caption.trim()
          ? <CaptionText text={caption} />
          : <span style={{ color: 'var(--text-3)' }}>Matn kiritilmagan</span>}
      </div>

      <div className="field-hint" style={{ padding: '0 12px 12px', margin: 0 }}>
        ⚠️ Bu taxminiy ko‘rinish. Haqiqiy natijada Instagram rasmni o‘z nisbatiga qirqishi va sRGB
        bo‘lmagan ranglarni o‘zgartirishi mumkin.
      </div>
    </div>
  )
}

/** Media ustidagi strelka tugmasi — chap/o'ng bir xil bo'lsin deb bitta joyda. */
function arrowStyle(side: 'left' | 'right'): CSSProperties {
  return {
    position: 'absolute',
    top: '50%',
    transform: 'translateY(-50%)',
    left: side === 'left' ? 6 : undefined,
    right: side === 'right' ? 6 : undefined,
    background: 'rgba(0,0,0,.45)',
    color: '#fff',
    borderRadius: 999,
  }
}

/** Manzil yozilayotganda ko'rinish shuncha ms jim turadi (har harfda so'rov ketmasin). */
const PREVIEW_DEBOUNCE_MS = 500

/** Instagram matnida "yana" bilan qirqiladigan chegara (taxminiy). */
const CAPTION_CUT = 180

/**
 * Caption ko'rinishi: `markazingiz` + matn, hashtaglar AJRATIB (ko'k) chiziladi.
 *
 * ⚠️ Uzun matn Instagram'da «yana» bilan yig'iladi — shu yerda ham xuddi shunday, chunki
 * savol "matnning qaysi qismi darhol ko'rinadi". «yana» bosilsa to'liq matn ochiladi.
 */
function CaptionText({ text }: { text: string }) {
  const [full, setFull] = useState(false)
  const long = text.length > CAPTION_CUT
  const shown = full || !long ? text : `${text.slice(0, CAPTION_CUT)}…`

  return (
    <div style={{ whiteSpace: 'pre-wrap', wordBreak: 'break-word' }}>
      <b>markazingiz</b>{' '}
      {highlight(shown)}
      {long && (
        <button
          type="button"
          onClick={() => setFull((v) => !v)}
          style={{
            border: 0, background: 'none', padding: 0, marginLeft: 4, cursor: 'pointer',
            color: 'var(--text-3)', fontSize: 'inherit', fontWeight: 700,
          }}
        >
          {full ? 'yopish' : 'yana'}
        </button>
      )}
    </div>
  )
}

/**
 * Hashtag va mention'larni rangli qilib chizadi.
 *
 * ⚠️ Bu FAQAT ko'rinish uchun — sanoq (`countHashtags`/`countMentions`) baribir servisdagi
 * qoidadan olinadi. Ikki joyda ikki xil qoida bo'lmasligi uchun bu yerda hech narsa
 * SANALMAYDI, faqat bo'yaladi.
 */
function highlight(text: string) {
  return text.split(/(\s+)/).map((chunk, i) => (
    /^[#@][\p{L}\p{N}_]+/u.test(chunk)
      ? <span key={i} style={{ color: 'var(--primary)' }}>{chunk}</span>
      : <span key={i}>{chunk}</span>
  ))
}
