/**
 * MARKETING → KONTENT → «JOYLANGANLAR».
 *
 * Sahifa savoli: <b>nima chiqdi va u qanday ko'rinadi</b>. Navbat KELAJAKKA qaraydi, bu yerda
 * esa O'TMISH — shuning uchun ko'rinish ham boshqacha: ro'yxat emas, <b>galereya</b>. Instagram
 * profilining o'zi ham galereya, ya'ni "biz nima chiqardik" savoliga eng tez javob beradigan
 * shakl aynan shu.
 *
 * ⚠️ MA'LUMOT BITTA SO'ROVDA, KESIM esa KLIENTDA (`status: 'all'`).
 * Sabab: backend jamlanmani (`IgPostTotals`) status filtri QO'LLANGANDAN KEYIN hisoblaydi —
 * ya'ni `status=published` bilan so'ralsa `failed` maydoni har doim 0 chiqadi. Kesimni serverga
 * yuborsak "shu oyda nechta xato" degan jamlanma yolg'on nol ko'rsatardi (loyihadagi eng
 * qattiq qoidalardan biri — ekrandagi ikki son bir-biriga mos kelishi shart). Yon ta'sir:
 * kesim almashganda qayta so'rov ketmaydi (galereya darhol chiziladi).
 *
 * ⚠️ Oylik chegara (`MAX_PAGES`) barcha holatlarga BIRGA taalluqli bo'lib qoladi — chegaradan
 * oshgani JIM tashlanmaydi, ro'yxat ostida ochiq yoziladi.
 */
import { useCallback, useEffect, useMemo, useState } from 'react'
import { useOutletContext } from 'react-router-dom'
import { MonthDayStrip } from '@/components/ui/MonthDayStrip'
import { currentMonth, monthRange } from '@/lib/month'
import { usePerm } from '@/lib/permissions'
import { apiErrorMessage } from '@/lib/utils'
import {
  isHttpsUrl, publishIgPost,
  type IgPost, type IgPostStatus, type IgPostTotals,
} from '@/api/services/instagramContent'
import { Icon, MkCard, MkEmpty, MkError, MkLoading, MkNotice, MkStat } from '../mk'
import { STATUS_STYLE, countsByDay, fmtWhen, loadAllPosts, postTypeIcon, trim } from './helpers'
import type { ContentOutlet } from './ContentLayout'

/** Ko'rinadigan kesimlar — bu sahifada FAQAT yakunlangan holatlar. */
const TABS: ReadonlyArray<{ id: IgPostStatus; label: string }> = [
  { id: 'published', label: 'Joylandi' },
  { id: 'failed', label: 'Xato' },
  { id: 'cancelled', label: 'Bekor qilingan' },
]

export function ContentPublished() {
  const { can } = usePerm()
  const canEdit = can('marketing.content', 'edit')
  const { reloadKey, refreshCounts } = useOutletContext<ContentOutlet>()

  const [month, setMonth] = useState(currentMonth())
  const [day, setDay] = useState('')
  const [tab, setTab] = useState<IgPostStatus>('published')

  const [items, setItems] = useState<IgPost[]>([])
  const [totals, setTotals] = useState<IgPostTotals | null>(null)
  const [truncated, setTruncated] = useState(0)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState('')

  const [busy, setBusy] = useState('')
  const [notice, setNotice] = useState('')
  /**
   * ⚠️ AMAL xatosi («Qayta urinish») YUKLASH xatosidan ATAYIN ajratilgan: ilgari ikkalasi
   * bitta `error` da edi va bitta muvaffaqiyatsiz urinish BUTUN galereyani ekrandan olib
   * tashlardi (`{!loading && !error && …}`) — go'yo hamma post yo'qolgandek ko'rinardi.
   * (Navbat sahifasida ham aynan shu ajratish qilingan — bitta modulda xulq bir xil bo'lsin.)
   */
  const [actionError, setActionError] = useState('')

  const load = useCallback(async () => {
    setLoading(true)
    setError('')
    const { from, to } = monthRange(month)
    try {
      // ⚠️ `status: 'all'` — sabab fayl boshidagi izohda (jamlanma to'g'ri chiqishi uchun).
      const res = await loadAllPosts({ from, to, status: 'all' })
      setItems(res.items)
      setTotals(res.totals)
      setTruncated(res.truncated)
    } catch (e) {
      setError(apiErrorMessage(e, "Joylangan postlarni yuklab bo'lmadi"))
    } finally {
      setLoading(false)
    }
    // `reloadKey` — layoutdagi «Yangilash» tugmasining signali. Qiymati ishlatilmaydi,
    // faqat funksiya kimligini o'zgartirib, quyidagi effektni qayta ishga tushiradi.
    void reloadKey
  }, [month, reloadKey])

  useEffect(() => { void load() }, [load])

  /** Tanlangan kesim (va kun) bo'yicha postlar — eng yangisi BIRINCHI. */
  const shown = useMemo(() => {
    const rows = items.filter((p) => p.status === tab && (!day || p.scheduledAt.slice(0, 10) === day))
    // ⚠️ Bu yerda tartib TESKARI (navbatdan farqli): o'tmishga qaraganda oxirgi chiqqan post
    // eng qiziq bo'ladi. ISO satrlar leksikografik solishtiriladi — `Date` shart emas.
    return [...rows].sort((a, b) => (b.scheduledAt || '').localeCompare(a.scheduledAt || ''))
  }, [items, tab, day])

  /** Kalendar kataklaridagi son — AYNAN tanlangan kesim bo'yicha (kun filtriga bog'liq emas). */
  const counts = useMemo(
    () => countsByDay(items.filter((p) => p.status === tab)),
    [items, tab],
  )

  const retry = async (post: IgPost) => {
    setBusy(post.id)
    setActionError('')
    setNotice('')
    try {
      const res = await publishIgPost(post.id)
      setNotice(
        res.status === 'published'
          ? 'Post Instagram’ga joylandi.'
          : 'Post joylashga yuborildi — holati «Joylanmoqda». Video bir necha daqiqa olishi mumkin.',
      )
      await load()
      refreshCounts()
    } catch (e) {
      setActionError(apiErrorMessage(e, "Postni joylab bo'lmadi"))
    } finally {
      setBusy('')
    }
  }

  const periodTitle = day ? `${day.slice(8, 10)}.${day.slice(5, 7)} kuni` : 'Butun oy'
  const tabLabel = TABS.find((t) => t.id === tab)?.label ?? ''

  return (
    <div className="fade-up">
      {notice && <MkNotice text={notice} tone="success" onClose={() => setNotice('')} />}

      {totals && (
        <div className="mk-kpi">
          <MkStat
            label="Shu oyda joylandi" value={totals.published} tone="success" icon="check"
            hint="Instagram’ga haqiqatan chiqqan postlar"
          />
          <MkStat
            label="Xato" value={totals.failed} tone="danger" icon="warn"
            hint="Sabab har postning ostida yozilgan"
          />
          <MkStat label="Bekor qilingan" value={totals.cancelled} icon="close" />
        </div>
      )}

      <MkCard>
        <div className="seg" style={{ marginBottom: 14 }}>
          {TABS.map((t) => (
            <button key={t.id} className={tab === t.id ? 'active' : ''} onClick={() => setTab(t.id)}>
              {t.label}
            </button>
          ))}
        </div>

        {/* Kalendar navbatdagi bilan AYNAN bir xil — ikki sahifada ikki xil sana tanlash
            usuli bo'lsa foydalanuvchi har safar qaytadan o'rganishga majbur bo'lardi. */}
        <MonthDayStrip
          month={month}
          onMonthChange={(m) => { setMonth(m); setDay('') }}
          selected={day}
          onSelect={setDay}
          counts={counts}
          hint="Katakdagi son — o‘sha kundagi postlar (tanlangan kesim bo‘yicha). Kun bosilsa faqat o‘sha kun ko‘rsatiladi."
        />

        {/* 🔴 Chegaraga yetilgan oyda kalendar YOLG'ON gapiradi: backend eng YANGISIDAN
            beradi, ya'ni sig'magani — oyning BOSHIDAGI kunlar va ular katagida jimgina "0"
            turardi ("1–15 avgustda post bo'lmagan" degan noto'g'ri xulosa). `status: 'all'`
            bo'lgani uchun chegara barcha kesimlarga BIRGA taalluqli. */}
        {truncated > 0 && (
          <div className="mk-alert" style={{ marginTop: 12, marginBottom: 0 }}>
            <Icon name="warn" style={{ width: 18, height: 18, flexShrink: 0, marginTop: 2 }} />
            <div style={{ fontSize: 12.5, lineHeight: 1.5 }}>
              <div className="mk-alert-title">Kalendar sonlari to‘liq emas</div>
              Bu oyda postlar ko‘p ({truncated} tasi yuklanmadi) — galereyaga eng <b>yangilari</b> tushdi,
              ya'ni <b>oyning boshidagi kunlar</b> to‘liq bo‘lmasligi mumkin va ularning katagidagi son
              haqiqiydan kam ko‘rinadi.
            </div>
          </div>
        )}

        {day && (
          <button className="btn btn-outline btn-sm" style={{ marginTop: 12 }} onClick={() => setDay('')}>
            <Icon name="close" /> Kun filtri
          </button>
        )}
      </MkCard>

      {loading && <MkLoading />}
      {/* ⚠️ FAQAT yuklash xatosi galereyani almashtiradi — ko'rsatadigan ma'lumotning o'zi yo'q. */}
      {!loading && error && <MkError text={error} onRetry={() => void load()} />}

      {!loading && !error && actionError && (
        <MkNotice text={actionError} tone="danger" onClose={() => setActionError('')} />
      )}

      {!loading && !error && (
        <MkCard
          title={`${tabLabel} — ${periodTitle}`}
          sub="Eng oxirgi chiqqan post birinchi bo‘lib turadi"
          actions={<span className="mk-num">{shown.length} ta</span>}
          pad={false}
        >
          {/* ⚠️ Kesim ham, kun ham KLIENTDA filtrlanadi — ular yuklanmagan postlarni QAYTARIB
              KELMAYDI, shuning uchun bu yerda "filtrlang" deb maslahat berilmaydi. */}
          {truncated > 0 && (
            <div className="field-hint" style={{ padding: '10px 14px 0' }}>
              Bu oyda postlar ko‘p: yana {truncated} tasi ro‘yxatga sig‘madi — bu yerda oyning eng
              <b> yangi</b> postlari ko‘rsatilgan, oy boshidagilari tushib qolgan bo‘lishi mumkin.
            </div>
          )}

          {shown.length === 0
            ? <MkEmpty text={`Bu davrda «${tabLabel}» holatidagi post yo‘q`} hint="Boshqa oyni yoki kesimni tanlab ko‘ring." />
            : (
              <div className="mk-gallery">
                {shown.map((p) => (
                  <PostTile
                    key={p.id}
                    post={p}
                    canEdit={canEdit}
                    busy={busy === p.id}
                    onRetry={() => void retry(p)}
                  />
                ))}
              </div>
            )}
        </MkCard>
      )}
    </div>
  )
}

/* ═══════════════════════════════════════ GALEREYA KARTOCHKASI ═══════════════════════════════════════ */

/**
 * Bitta post kartochkasi.
 *
 * ⚠️ Media FAQAT manzil ochiq HTTPS bo'lganda chiziladi — `/uploads/…` yoki noto'g'ri manzil
 * sinuq rasm bergan bo'lardi. Bunday holatda post TURI ikonkasi bilan bo'sh holat ko'rsatiladi.
 *
 * ⚠️ «Qayta urinish» faqat `failed` da va faqat yozish ruxsati bilan: joylangan postni qayta
 * joylash Instagram profilida DUBLIKAT yaratardi (backend ham buni to'sadi).
 */
function PostTile({
  post, canEdit, busy, onRetry,
}: {
  post: IgPost
  canEdit: boolean
  busy: boolean
  onRetry: () => void
}) {
  const first = post.media[0]
  const show = !!first?.url && isHttpsUrl(first.url)
  const st = STATUS_STYLE[post.status] ?? STATUS_STYLE.published

  return (
    <div className="mk-tile" style={{ ['--rail' as string]: st.color }}>
      <div className="mk-tile-media">
        {show && first.kind === 'image' && <img src={first.url} alt="" loading="lazy" />}
        {show && first.kind === 'video' && (
          // Video KO'RILADI (`controls`) — "u qanday ko'rinadi" savoli aynan shu sahifada.
          <video src={first.url} poster={first.coverUrl || undefined} controls preload="metadata" />
        )}
        {!show && (
          <div className="mk-state" style={{ textAlign: 'center' }}>
            <Icon name={postTypeIcon(post.postType)} style={{ width: 24, height: 24 }} />
          </div>
        )}

        <div className="mk-tile-badge">
          <Icon name={postTypeIcon(post.postType)} style={{ width: 13, height: 13 }} />
          {post.postTypeLabel}
          {post.media.length > 1 && ` · ${post.media.length}`}
        </div>
      </div>

      <div className="mk-tile-body">
        <div style={{ display: 'flex', alignItems: 'center', gap: 8, flexWrap: 'wrap' }}>
          <span style={{ fontSize: 12.5, fontWeight: 700 }}>{fmtWhen(post.scheduledAt)}</span>
          {post.status !== 'published' && (
            <span className="badge" style={{ background: st.bg, color: st.color }}>{post.statusLabel}</span>
          )}
        </div>

        <div className="mk-tile-cap">
          {post.caption ? trim(post.caption, 220) : <i>matnsiz post</i>}
        </div>

        {/* Sabab backenddan O'ZBEKCHA keladi (Meta xato kodi tarjima qilingan). */}
        {post.error && (
          <div style={{ color: 'var(--danger)', fontSize: 12, lineHeight: 1.45 }}>
            {post.error}
          </div>
        )}

        <div style={{ display: 'flex', alignItems: 'center', gap: 10, flexWrap: 'wrap', marginTop: 'auto' }}>
          {post.permalink && (
            <a className="link-btn" href={post.permalink} target="_blank" rel="noreferrer">
              <Icon name="link" style={{ width: 13, height: 13 }} /> Instagram’da ochish
            </a>
          )}
          {post.createdBy && <span className="field-hint" style={{ margin: 0 }}>{post.createdBy}</span>}

          {post.status === 'failed' && canEdit && (
            <button className="btn btn-outline btn-sm" style={{ marginLeft: 'auto' }} onClick={onRetry} disabled={busy}>
              <Icon name="refresh" /> {busy ? 'Yuborilmoqda…' : 'Qayta urinish'}
            </button>
          )}
        </div>
      </div>
    </div>
  )
}
