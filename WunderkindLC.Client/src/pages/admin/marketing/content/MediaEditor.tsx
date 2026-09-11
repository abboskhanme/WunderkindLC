import { useEffect, useRef, useState } from 'react'
import type { DragEvent } from 'react'
import { usePerm } from '@/lib/permissions'
import {
  isHttpsUrl,
  IG_LIMITS,
  type IgMediaItem, type IgMediaKind, type IgPostType,
} from '@/api/services/instagramContent'
import { Icon } from '../mk'
import { fmtBytes, mediaKindLocked } from './helpers'

/**
 * 🔴 §5.5 + §5.6 — media talablari OCHIQ yoziladi.
 * Sabab: bu qoidalar buzilsa Meta xatoni faqat konteyner tayyorlangandan keyin qaytaradi,
 * ya'ni post o'z vaqtida chiqmasdi va nima uchunligi kech ma'lum bo'lardi.
 */
export function MediaRequirements({ type }: { type: IgPostType }) {
  const lines: string[] = [
    'Media manzili ochiq HTTPS bo‘lishi SHART — Instagram faylni o‘zi yuklab oladi, shuning uchun login, IP cheklov va yo‘naltirish (redirect) ishlamaydi.',
  ]
  if (type === 'image' || type === 'carousel') {
    lines.push(`Rasm — faqat JPEG (.jpg/.jpeg), ≤${IG_LIMITS.imageMb} MB, kenglik ${IG_LIMITS.feedWidth.min}–${IG_LIMITS.feedWidth.max} px, nisbat 4:5 – 1.91:1.`)
  }
  if (type === 'reels' || type === 'video') {
    lines.push(`Reels/video — MP4 yoki MOV, ≤${IG_LIMITS.reelsMb} MB, ${IG_LIMITS.reelsSeconds.min}–${IG_LIMITS.reelsSeconds.max} soniya, 9:16 (masalan 1080×1920).`)
  }
  if (type === 'story') {
    lines.push(`Story — 9:16. Rasm: JPEG ≤${IG_LIMITS.imageMb} MB. Video: MP4/MOV ≤${IG_LIMITS.storyVideoMb} MB, ${IG_LIMITS.storyVideoSeconds.min}–${IG_LIMITS.storyVideoSeconds.max} soniya.`)
  }
  if (type === 'carousel') {
    lines.push(`Karusel — ${IG_LIMITS.carouselItems.min}–${IG_LIMITS.carouselItems.max} ta element; qolganlari birinchi elementning nisbatiga qirqiladi.`)
  }

  return (
    <div className="mk-alert" style={{ marginBottom: 18 }}>
      <Icon name="warn" style={{ width: 18, height: 18, flexShrink: 0, marginTop: 2 }} />
      <div style={{ fontSize: 12.5, lineHeight: 1.5 }}>
        <div className="mk-alert-title">Media talablari</div>
        <ul style={{ margin: 0, paddingLeft: 18 }}>
          {lines.map((l) => <li key={l}>{l}</li>)}
        </ul>
      </div>
    </div>
  )
}

/**
 * Bitta media elementining FAYL HOLATI — yuklash/o'lchash jarayoni va xabarlari.
 *
 * ⚠️ Bu holat ATAYIN SHU KOMPONENTDA EMAS, `ContentComposer` da (media massivi yonida)
 * saqlanadi. Sabab: `MediaEditor` faqat «Tur va media» bosqichida chiziladi, ya'ni foydalanuvchi
 * «Matn» bosqichiga o'tishi bilan komponent UNMOUNT bo'ladi. Holat ichkarida bo'lganda:
 *   • 40 MB video yuklanayotganda bosqich almashtirilsa, yiqilgan yuklash xatosi O'CHGAN
 *     komponentga tushib, foydalanuvchiga HECH QAYERDA ko'rinmasdi (u faqat bo'sh manzilni
 *     ko'rardi va sababini bilmasdi);
 *   • «yuklanmoqda» bayrog'i nolga qaytgani uchun ikkinchi faylni ham tashlab yuborish mumkin
 *     bo'lardi va birinchi yuklash tugagach POSTDA FOYDALANUVCHI TANLAMAGAN fayl qolardi.
 */
export interface MediaFileState {
  uploading: boolean
  measuring: boolean
  uploadError: string
  measureError: string
  /** Xato EMAS, ma'lumot: "bir vaqtda bitta fayl", "karusel to'ldi" va h.k. */
  notice: string
}

/** Manzil yozilayotganda ko'rinish shuncha ms jim turadi (F19 — har harfda so'rov ketmasin). */
const PREVIEW_DEBOUNCE_MS = 500

/**
 * Bitta media elementi: manzil + texnik ma'lumot + ko'rinish (thumbnail).
 *
 * Fayl berishning UCHTA yo'li bor va ular BOSHQA-BOSHQA ish qiladi (uchalasi ham QOLADI):
 *
 * 1. **«Fayl yuklash» (yoki faylni SUDRAB TASHLASH)** — fayl SERVERGA ketadi
 *    (`POST content/media`) va `/uploads/marketing-public/` ochiq papkasidan tayyor manzil
 *    qaytadi. Bu papka ATAYIN `UploadsGuard` dan tashqarida: Instagram faylni o'zi yuklab
 *    oladi, login talab qiladigan manzil esa Meta uchun 404 bo'lardi (xato kodi `2207052`).
 *    Ruxsat: `marketing.content` (edit).
 * 2. **«Fayldan o'lchash»** — fayl YUKLANMAYDI, faqat brauzerda o'lchanadi. Tashqi CDN'da
 *    turgan fayl uchun kerak: manzil qo'lda yoziladi, o'lchamlar esa shu tugma bilan.
 * 3. **Manzilni QO'LDA yozish** — tashqi CDN uchun.
 *
 * ⚠️ O'lchamlar 0 = "noma'lum" — backend bunday holatda tekshiruvni o'tkazib yuboradi. Shu
 * sababli taxminiy qiymat yozilmaydi: noto'g'ri son to'g'ri media'ni bekorga rad etardi.
 *
 * ⚠️ Komponent YUKLASHNI O'ZI BAJARMAYDI — faylni `onUploadFiles` orqali ota-onaga uzatadi
 * (yuqoridagi `MediaFileState` izohi). Bu yerda faqat KO'RINISH qoladi.
 */
export function MediaEditor({
  item, index, showIndex, type, state, onChange, onRemove, onUploadFiles, onMeasure,
}: {
  item: IgMediaItem
  index: number
  showIndex: boolean
  type: IgPostType
  /** Yuklash/o'lchash holati — ota-onadan (bosqich almashganda yo'qolmasin). */
  state: MediaFileState
  onChange: (patch: Partial<IgMediaItem>) => void
  onRemove?: () => void
  /** Tanlangan/tashlangan fayllar. Bittadan ko'pi bilan nima qilinishini OTA-ONA hal qiladi. */
  onUploadFiles: (files: File[]) => void
  onMeasure: (file: File) => void
}) {
  // ⚠️ Ruxsat SHU YERDA qayta tekshiriladi (sahifa faqat tahrirlash huquqi bilan ochilsa ham):
  // yuklash — YOZISH amali va u alohida darvozalanishi kerak.
  const { can } = usePerm()
  const canUpload = can('marketing.content', 'edit')

  const [over, setOver] = useState(false)

  /**
   * ⚠️ `dragenter`/`dragleave` ICHKI elementlarda ham otiladi — ya'ni sichqoncha zona ichidagi
   * matn ustidan o'tganda ham "chiqib ketdi" hodisasi keladi va ramka MILTILLAB turardi.
   * Shuning uchun oddiy bayroq emas, HISOBLAGICH: nol bo'lgandagina zona so'nadi.
   */
  const dragDepth = useRef(0)

  /**
   * Ko'rinish manzili — KECHIKTIRILGAN (debounce).
   *
   * ⚠️ `isHttpsUrl` `https://a` ni ham to'g'ri deb biladi, ya'ni manzil QO'LDA yozilayotganda
   * har bosilgan harfda yangi rasm/video so'rovi ketardi: konsol xatolarga to'lar, ekranda esa
   * sinuq rasm miltillardi. Endi yozish to'xtagach (500 ms) bir marta so'raladi.
   */
  const [previewUrl, setPreviewUrl] = useState(item.url)
  const [previewFailed, setPreviewFailed] = useState(false)

  useEffect(() => {
    const t = setTimeout(() => setPreviewUrl(item.url), PREVIEW_DEBOUNCE_MS)
    return () => clearTimeout(t)
  }, [item.url])

  // Yangi manzil — yangi imkoniyat: eski xato bayrog'i qolib ketmasin.
  useEffect(() => { setPreviewFailed(false) }, [previewUrl, item.kind])

  const busyFile = state.uploading || state.measuring
  const urlOk = item.url.trim().length === 0 || isHttpsUrl(item.url)
  const canPreview = !!previewUrl.trim() && isHttpsUrl(previewUrl) && !previewFailed

  /** `FileList` → massiv (ro'yxat "jonli" obyekt, uni to'g'ridan-to'g'ri saqlab bo'lmaydi). */
  const pickFiles = (list: FileList | null): File[] => (list ? Array.from(list) : [])

  /**
   * Sudrab tashlangan fayl(lar) — «Fayl yuklash» bilan AYNAN bir xil yo'l (serverga chiqadi).
   *
   * ⚠️ Ruxsati yo'q foydalanuvchida drop JIM tashlanadi: tugma yashiringan bo'lsa,
   * sudrab tashlash orqali uni aylanib o'tib bo'lmasligi kerak.
   */
  const onDrop = (e: DragEvent<HTMLDivElement>) => {
    e.preventDefault()
    dragDepth.current = 0
    setOver(false)
    if (!canUpload || busyFile) return
    const files = pickFiles(e.dataTransfer.files)
    if (files.length > 0) onUploadFiles(files)
  }

  return (
    <div className="mk-media-card">
      {/* ── Sarlavha: raqam · tur tanlovi · o'chirish ── */}
      <div className="mk-media-head">
        <span className="rule-num">
          {showIndex ? index + 1 : <Icon name="link" style={{ width: 13, height: 13 }} />}
        </span>
        <div className="seg">
          {(['image', 'video'] as IgMediaKind[]).map((k) => (
            <button
              key={k}
              type="button"
              className={item.kind === k ? 'active' : ''}
              onClick={() => onChange({ kind: k })}
              disabled={mediaKindLocked(type, k)}
              title={mediaKindLocked(type, k) ? 'Bu post turida bunday media qabul qilinmaydi' : undefined}
            >
              {k === 'image' ? 'Rasm' : 'Video'}
            </button>
          ))}
        </div>
        {onRemove && (
          <button className="btn btn-ghost btn-sm" style={{ marginLeft: 'auto' }} onClick={onRemove}>
            <Icon name="trash" /> Olib tashlash
          </button>
        )}
      </div>

      <div style={{ display: 'flex', gap: 16, alignItems: 'flex-start', flexWrap: 'wrap' }}>
        {/* ── CHAP: ko'rinish ── */}
        <div className="mk-media-thumb">
          {canPreview && item.kind === 'image' && (
            <img
              src={previewUrl}
              alt={item.altText || ''}
              style={{ width: '100%', height: '100%', objectFit: 'cover' }}
              onError={() => setPreviewFailed(true)}
            />
          )}
          {canPreview && item.kind === 'video' && (
            <video
              src={previewUrl}
              poster={item.coverUrl || undefined}
              muted
              preload="metadata"
              style={{ width: '100%', height: '100%', objectFit: 'cover' }}
              onError={() => setPreviewFailed(true)}
            />
          )}
          {!canPreview && (
            <div style={{ color: 'var(--text-3)', display: 'grid', placeItems: 'center', height: '100%' }}>
              <Icon name={item.kind === 'video' ? 'film' : 'image'} style={{ width: 24, height: 24 }} />
            </div>
          )}
        </div>

        {/* ── O'NG: maydonlar ── */}
        <div style={{ flex: 1, minWidth: 260 }}>
          {/* Sudrab tashlash maydoni — «Fayl yuklash» bilan bir xil natija beradi. */}
          <div
            className={`mk-drop${over ? ' over' : ''}${busyFile ? ' busy' : ''}`}
            onDragEnter={(e) => {
              e.preventDefault()
              dragDepth.current += 1
              if (canUpload && !busyFile) setOver(true)
            }}
            onDragOver={(e) => { e.preventDefault() }}
            onDragLeave={() => {
              dragDepth.current = Math.max(0, dragDepth.current - 1)
              if (dragDepth.current === 0) setOver(false)
            }}
            onDrop={onDrop}
          >
            <Icon name="upload" style={{ width: 20, height: 20, color: 'var(--text-3)' }} />
            <div style={{ fontSize: 12.5, fontWeight: 700 }}>
              {state.uploading
                ? 'Yuklanmoqda…'
                : canUpload
                  ? (type === 'carousel'
                    ? 'Fayllarni shu yerga sudrab tashlang (bir nechtasi ham bo‘ladi)'
                    : 'Faylni shu yerga sudrab tashlang')
                  : 'Fayl yuklash uchun tahrirlash ruxsati kerak'}
            </div>
            <div className="field-hint" style={{ margin: 0 }}>JPEG · MP4 · MOV</div>
          </div>

          <div className="field" style={{ marginTop: 12, marginBottom: 12 }}>
            <label className="field-label">Media manzili</label>
            <input
              className="input"
              value={item.url}
              placeholder="https://…/rasm.jpg"
              onChange={(e) => onChange({ url: e.target.value })}
              style={urlOk ? undefined : { borderColor: 'var(--danger)' }}
            />
            <div className="field-hint">
              Ochiq HTTPS manzil — qo‘lda yozsangiz ham bo‘ladi (tashqi CDN uchun). ⚠️ CRM ichidagi oddiy{' '}
              <code>/uploads/…</code> manzillari <b>ishlamaydi</b> — ular login ortida, Instagram esa faylni
              tashqaridan yuklab oladi. «Fayl yuklash» tugmasi faylni maxsus <b>ochiq</b> papkaga qo‘yadi.
            </div>
          </div>

          {/* ⚠️ Sinuq rasm ikonkasi SABABINI aytmaydi — shuning uchun ochiq xabar. Aynan shu
              tekshiruv Meta'ning `2207052` («Media yuklab bo'lmadi») xatosini joylashdan OLDIN
              topib beradi. */}
          {previewFailed && (
            <div className="field-hint" style={{ color: 'var(--danger)' }}>
              Rasmni yuklab bo‘lmadi — manzil ochiq HTTPS’mi? Instagram ham faylni AYNAN shunday,
              tashqaridan va login’siz oladi: brauzer ocha olmasa Meta ham ocha olmaydi.
            </div>
          )}

          <div style={{ display: 'flex', gap: 10, flexWrap: 'wrap', alignItems: 'center' }}>
            {canUpload && (
              <label
                className="btn btn-primary btn-sm"
                style={{ cursor: busyFile ? 'default' : 'pointer', opacity: busyFile ? 0.6 : 1 }}
              >
                <Icon name="upload" />
                {state.uploading ? 'Yuklanmoqda…' : 'Fayl yuklash'}
                <input
                  type="file"
                  accept="image/jpeg,video/mp4,video/quicktime"
                  // Karuselda bir nechta fayl tanlash tabiiy — qolgan turlarda bitta element bor.
                  multiple={type === 'carousel'}
                  style={{ display: 'none' }}
                  disabled={busyFile}
                  onChange={(e) => {
                    const files = pickFiles(e.target.files)
                    if (files.length > 0) onUploadFiles(files)
                    // ⚠️ Tozalash SHART: bir xil faylni qayta tanlaganda `change` otilmasdi.
                    e.target.value = ''
                  }}
                />
              </label>
            )}

            <label className="btn btn-outline btn-sm" style={{ cursor: busyFile ? 'default' : 'pointer' }}>
              <Icon name="search" />
              {state.measuring ? 'O‘lchanmoqda…' : 'Fayldan o‘lchash'}
              <input
                type="file"
                accept="image/jpeg,video/mp4,video/quicktime"
                style={{ display: 'none' }}
                disabled={busyFile}
                onChange={(e) => {
                  const f = e.target.files?.[0]
                  if (f) onMeasure(f)
                  e.target.value = ''
                }}
              />
            </label>
          </div>

          <div className="field-hint" style={{ marginTop: 8 }}>
            <b>Fayl yuklash</b> — fayl serverga chiqadi va manzil o‘zi yoziladi (JPEG, MP4 yoki MOV).
            <b> Fayldan o‘lchash</b> — fayl <b>yuklanmaydi</b>, faqat hajmi, o‘lchami va davomiyligi
            o‘lchanadi: manzili boshqa joyda turgan fayl uchun. Ikkalasida ham xato Instagram’dan emas,
            shu yerda ko‘rinadi.
          </div>

          {state.notice && <div className="field-hint">{state.notice}</div>}
          {state.uploadError && <div className="field-hint" style={{ color: 'var(--danger)' }}>{state.uploadError}</div>}
          {state.measureError && <div className="field-hint" style={{ color: 'var(--danger)' }}>{state.measureError}</div>}
        </div>
      </div>

      {/* ── Texnik o'lchamlar ── */}
      <div className="mk-media-grid" style={{ marginTop: 14 }}>
        <NumField label="Kenglik, px" value={item.width} onChange={(v) => onChange({ width: v })} />
        <NumField label="Balandlik, px" value={item.height} onChange={(v) => onChange({ height: v })} />
        <NumField label="Hajm, bayt" value={item.sizeBytes} onChange={(v) => onChange({ sizeBytes: v })} />
        <NumField
          label="Davomiylik, s"
          value={item.durationSeconds}
          onChange={(v) => onChange({ durationSeconds: v })}
          disabled={item.kind !== 'video'}
        />
      </div>
      <div className="field-hint">
        0 = <b>noma’lum</b>: bunday maydon tekshirilmaydi. Taxminiy son yozmang — noto‘g‘ri qiymat
        to‘g‘ri media’ni bekorga rad etadi. Hozirgi hajm: <b>{fmtBytes(item.sizeBytes)}</b>.
      </div>

      {/* ── Element matni — FAQAT bo'sh bo'lmaganda ──
          ⚠️ Bu maydon ATAYIN doim ko'rsatilmaydi (unga matn yozishga TAKLIF qilmaymiz: karuselda
          u ishlamaydi). Lekin API orqali yaratilgan postda matn bo'lishi mumkin va u «Saqlash» ni
          BLOKLAYDI — maydonsiz foydalanuvchi xatoni hech qanday yo'l bilan tozalay olmasdi
          (boshi berk ko'cha). Shuning uchun matn BOR bo'lsa ko'rsatiladi va tozalash tugmasi
          beriladi. */}
      {item.caption.trim().length > 0 && (
        <div className="mk-alert mk-alert-danger" style={{ marginTop: 12 }}>
          <Icon name="warn" style={{ width: 18, height: 18, flexShrink: 0, marginTop: 2 }} />
          <div style={{ fontSize: 12.5, lineHeight: 1.5, minWidth: 0 }}>
            <div className="mk-alert-title">Bu elementga matn yozilgan</div>
            Karusel elementidagi matn Instagram’da <b>ko‘rinmaydi</b> va backend bunday postni rad
            etadi — matn faqat umumiy «Post matni» maydonidan olinadi.
            <div
              style={{
                margin: '8px 0', padding: 10, borderRadius: 8, background: 'var(--bg-2)',
                whiteSpace: 'pre-wrap', wordBreak: 'break-word',
              }}
            >
              {item.caption}
            </div>
            <button className="btn btn-outline btn-sm" onClick={() => onChange({ caption: '' })}>
              <Icon name="trash" /> Element matnini tozalash
            </button>
          </div>
        </div>
      )}

      {/* ── Video muqovasi ── */}
      {item.kind === 'video' && (
        <div style={{ display: 'grid', gridTemplateColumns: 'minmax(0, 2fr) minmax(0, 1fr)', gap: 12, marginTop: 12 }}>
          <div className="field" style={{ margin: 0 }}>
            <label className="field-label">Muqova manzili (ixtiyoriy)</label>
            <input
              className="input"
              value={item.coverUrl}
              placeholder="https://…/muqova.jpg"
              onChange={(e) => onChange({ coverUrl: e.target.value })}
            />
          </div>
          <div className="field" style={{ margin: 0 }}>
            <label className="field-label">Muqova kadri, ms</label>
            <input
              className="input"
              type="number"
              value={item.thumbOffsetMs < 0 ? '' : item.thumbOffsetMs}
              placeholder="berilmagan"
              onChange={(e) => onChange({ thumbOffsetMs: e.target.value === '' ? -1 : Number(e.target.value) })}
            />
            <div className="field-hint">Bo‘sh = berilmagan; 0 = birinchi kadr.</div>
          </div>
        </div>
      )}

      <div className="field" style={{ marginTop: 12, marginBottom: 0 }}>
        <label className="field-label">Alt matn (ko‘rish imkoniyati cheklanganlar uchun)</label>
        <input
          className="input"
          value={item.altText}
          maxLength={IG_LIMITS.altTextChars}
          onChange={(e) => onChange({ altText: e.target.value })}
        />
      </div>
    </div>
  )
}

/** Butun son maydoni: bo'sh — 0 ("noma'lum"). */
function NumField({
  label, value, onChange, disabled,
}: {
  label: string
  value: number
  onChange: (v: number) => void
  disabled?: boolean
}) {
  return (
    <div>
      <label className="field-label" style={{ fontSize: 11.5 }}>{label}</label>
      <input
        className="input"
        type="number"
        min={0}
        disabled={disabled}
        value={value === 0 ? '' : value}
        placeholder="noma’lum"
        onChange={(e) => onChange(e.target.value === '' ? 0 : Number(e.target.value))}
      />
    </div>
  )
}
