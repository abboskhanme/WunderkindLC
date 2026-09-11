import { Fragment, useCallback, useEffect, useMemo, useState } from 'react'
import type { ReactElement } from 'react'
import { Link, useSearchParams } from 'react-router-dom'
import {
  CartesianGrid, Cell, Line, LineChart, Pie, PieChart,
  ResponsiveContainer, Tooltip, XAxis, YAxis,
} from 'recharts'
import { usePerm } from '@/lib/permissions'
import { apiErrorMessage } from '@/lib/utils'
import {
  adsMoneyMajor, formatAdsMoney, formatRoi,
  getIgAdsCampaigns, getIgAdsOverview, getIgAdsStatus, syncIgAdsStats,
  type IgAdsPlatform, type IgAdsStatus, type IgRoiCampaigns, type IgRoiNode, type IgRoiOverview,
} from '@/api/services/instagramAds'
import { Icon, MarketingPage, MkCard, MkEmpty, MkError, MkLoading, MkNotice, MkStat } from './mk'

/**
 * REKLAMA STATISTIKASI (Meta Ads Insights) — "pul qayerga ketdi va nima olib keldi".
 *
 * Bu ekran markazning ENG QIMMAT savoliga javob beradi: *"Bu oyda reklamaga N so'm
 * sarfladik — nechta lid keldi, bittasi qanchaga tushdi, ulardan nechtasi O'QUVCHI bo'ldi
 * va qancha pul to'ladi?"*. Ads Manager bu zanjirning faqat BIRINCHI yarmini biladi
 * (ko'rsatish, klik, lid soni); "qaysi lid pul to'ladi" esa faqat CRM'da bor. Shuning
 * uchun hisobot shu yerda — Meta'da emas.
 *
 * 🔴 HALOLLIK — ekranning asosiy talabi. Chiroyli, lekin yolg'on raqam bu yerda eng katta
 * zarar keltiradi (byudjet shunga qarab taqsimlanadi), shuning uchun:
 *   • QAMROV TAXMINIY — "≈" belgisi bilan, "kamida … ko'pi bilan …" chegaralari ko'rinadi
 *     (Meta kunlar va platformalar bo'yicha noyob odamlarni dedup QILMAYDI);
 *   • META LIDLARI ≠ CRM LIDLARI — ikkalasi ham ko'rsatiladi va farqi izohlanadi;
 *   • DAROMAD — butun umr bo'yicha, XARAJAT — faqat tanlangan oraliqda: bu
 *     TAQQOSLANMAYDIGAN o'lchov va u ochiq yozib qo'yiladi;
 *   • `notes[]` (backend bergan o'zbekcha ogohlantirishlar) JIM YUTILMAYDI — ekranda
 *     alohida blokda turadi;
 *   • CPL / CAC / ROI `null` bo'lsa "—" chiziladi, `0` YOZILMAYDI ("lid tekinga tushdi"
 *     degan yolg'on xulosa chiqmasin).
 *
 * ⚠️ GRAFIK QOIDALARI (`.claude/rules/course-analytics.md`): bitta grafikda IKKI Y-O'Q
 * ishlatilmaydi (xarajat va lidlar ALOHIDA grafiklarda), yashil/qizil juftlik esa
 * deuteranopiyada ajralmagani uchun umuman olinmaydi.
 *
 * ⚠️ KO'RINISH: sahifa TO'LIQ EKRANDA ochiladi va uzun skroll o'rniga SAHIFA ICHIDAGI
 * tab tugmalariga bo'lingan (`?bolim=…`). Davr/platforma/kampaniya filtrlari hamma tabga
 * tegishli, shuning uchun ular tablardan TASHQARIDA, tepada turadi. `notes[]` ham
 * tashqarida: u raqamlarni QANDAY o'qish kerakligini aytadi, ya'ni har bir tabga taalluqli.
 */

/* ─────────────────────────── Ranglar ─────────────────────────── */

/** Xarajat chizig'i. */
const C_SPEND = '#0284c7'
/** Lid chizig'i (CRM). */
const C_LEADS = '#e11d48'

/**
 * Platforma kesimi ranglari — tekshirilgan kategorial palitradan olingan tuslar
 * (indigo · amber · neytral kulrang). Yangi tus O'YLAB TOPILMAYDI.
 * `all` — Meta bo'linma bermagan qatorlar, ya'ni "ajratilmagan".
 */
const PLATFORM_COLOR: Record<string, string> = {
  instagram: '#6366f1',
  facebook: '#d97706',
  all: '#64748b',
}

const PLATFORM_LABEL: Record<string, string> = {
  instagram: 'Instagram',
  facebook: 'Facebook',
  all: 'Ajratilmagan',
}

/* ─────────────────────────── Sahifa ichidagi bo'limlar ─────────────────────────── */

/**
 * Tablar — nav'da EMAS, sahifaning O'ZIDA.
 * ⚠️ Bo'linish MA'NO bo'yicha: "umumiy manzara" → "qaysi kampaniya" → "qaysi platforma"
 * → "raqamlar qachongi". Adset va e'lon AYRI tab EMAS: ular kampaniya jadvalining
 * ochiladigan qatorlari, ya'ni ajratilsa iyerarxiya yo'qolardi.
 */
const TABS = [
  { key: 'umumiy', label: 'Umumiy', icon: 'analytics' },
  { key: 'kampaniyalar', label: 'Kampaniyalar', icon: 'layers' },
  { key: 'platformalar', label: 'Platformalar', icon: 'globe' },
  { key: 'holat', label: "Ma'lumot holati", icon: 'info' },
] as const

type TabKey = (typeof TABS)[number]['key']

/* ─────────────────────────── Sana yordamchilari ─────────────────────────── */

/**
 * `Date` → "yyyy-MM-dd" MAHALLIY vaqt bo'yicha.
 * ⚠️ `toISOString()` ATAYIN ishlatilmaydi: u UTC beradi va Toshkentda ertalab soat 5 gacha
 * "kecha" ni qaytarardi — "Bugun" tugmasi kechagi kunni tanlab qo'yardi.
 */
function iso(d: Date): string {
  const m = `${d.getMonth() + 1}`.padStart(2, '0')
  const day = `${d.getDate()}`.padStart(2, '0')
  return `${d.getFullYear()}-${m}-${day}`
}

const today = () => iso(new Date())

/** Bugundan N kun oldin. */
function daysAgo(n: number): string {
  const d = new Date()
  d.setDate(d.getDate() - n)
  return iso(d)
}

/** "2026-08-12" → "12.08" (grafik o'qi uchun qisqa yorliq). */
const shortDay = (d: string) => (d?.length >= 10 ? `${d.slice(8, 10)}.${d.slice(5, 7)}` : d)

/** ISO vaqtni "2026-08-20 14:35" ko'rinishida (sekundlarsiz). */
const shortTime = (v: string) => (v ? v.replace('T', ' ').slice(0, 16) : '—')

/* ─────────────────────────── Son formatlash ─────────────────────────── */

/**
 * Dona sonlar — guruh ajratgichi PROBEL, xuddi pul kabi.
 * ⚠️ `toLocaleString()` (madaniyatsiz) ataylab ishlatilmaydi: brauzer tiliga qarab goh
 * vergul, goh probel chiqarib, bitta jadvalda ikki xil ko'rinish berardi.
 */
const num = (n: number) => (n ?? 0).toLocaleString('en-US').replace(/,/g, ' ')

/** Grafik o'qi uchun butun son. */
const axisNum = (v: number) => Math.round(v).toLocaleString('en-US').replace(/,/g, ' ')

/* ═══════════════════════════════ SAHIFA ═══════════════════════════════ */

export function InstagramAdsStats() {
  const { can } = usePerm()
  /** Sinxronizatsiya — YOZISH amali, `marketing.settings` ruxsati bilan darvozalanadi. */
  const canSync = can('marketing.settings', 'edit')

  const [from, setFrom] = useState(() => daysAgo(29))
  const [to, setTo] = useState(today)
  const [platform, setPlatform] = useState<IgAdsPlatform>('all')
  const [campaignId, setCampaignId] = useState('')

  const [status, setStatus] = useState<IgAdsStatus | null>(null)
  const [overview, setOverview] = useState<IgRoiOverview | null>(null)
  const [tree, setTree] = useState<IgRoiCampaigns | null>(null)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState('')

  const [syncing, setSyncing] = useState(false)
  const [syncNote, setSyncNote] = useState<{ ok: boolean; text: string } | null>(null)

  /**
   * Ochiq tab MANZILDA saqlanadi (`?bolim=…`) — sahifa yangilansa yoki havola nusxa
   * qilib berilsa o'sha bo'lim ochilsin.
   * ⚠️ Noma'lum kalit JIMGINA "Umumiy" ga tushadi: eskirgan havola tufayli ekran
   * butunlay bo'shab qolmasin (`?source=` filtridagi bilan bir xil mantiq).
   */
  const [params, setParams] = useSearchParams()
  const rawTab = params.get('bolim') ?? ''
  const tab: TabKey = (TABS.some((t) => t.key === rawTab) ? rawTab : 'umumiy') as TabKey

  const setTab = (k: TabKey) => {
    const next = new URLSearchParams(params)
    // Standart bo'lim manzilni ifloslantirmaydi.
    if (k === 'umumiy') next.delete('bolim')
    else next.set('bolim', k)
    setParams(next, { replace: true })
  }

  /**
   * Kampaniya tanlash ro'yxati. ⚠️ FAQAT filtr bo'sh bo'lganda yangilanadi: kampaniya
   * tanlangach javobda o'sha bitta kampaniya qoladi va ro'yxatni undan qursak, boshqa
   * variantlar yo'qolib, foydalanuvchi "hammasi" dan boshqasiga o'ta olmasdi.
   */
  const [campaignOptions, setCampaignOptions] = useState<{ id: string; name: string }[]>([])

  const loadStatus = useCallback(() => {
    getIgAdsStatus().then(setStatus).catch(() => setStatus(null))
  }, [])

  useEffect(loadStatus, [loadStatus])

  const load = useCallback(() => {
    setLoading(true)
    setError('')
    const filters = { from, to, platform, campaignId }
    Promise.all([getIgAdsOverview(filters), getIgAdsCampaigns(filters)])
      .then(([ov, tr]) => {
        setOverview(ov)
        setTree(tr)
        if (campaignId === '') {
          setCampaignOptions(tr.campaigns.map((c) => ({ id: c.id, name: c.name || c.id })))
        }
      })
      .catch((e) => {
        setOverview(null)
        setTree(null)
        setError(apiErrorMessage(e, "Reklama statistikasini yuklab bo'lmadi"))
      })
      .finally(() => setLoading(false))
  }, [from, to, platform, campaignId])

  useEffect(load, [load])

  /** Tayyor oraliqlar — marketolog sanani qo'lda terib o'tirmasin. */
  const preset = (kind: 'today' | 'd7' | 'd30' | 'month' | 'prevMonth') => {
    const now = new Date()
    if (kind === 'today') { setFrom(today()); setTo(today()); return }
    if (kind === 'd7') { setFrom(daysAgo(6)); setTo(today()); return }
    if (kind === 'd30') { setFrom(daysAgo(29)); setTo(today()); return }
    if (kind === 'month') {
      setFrom(iso(new Date(now.getFullYear(), now.getMonth(), 1)))
      setTo(today())
      return
    }
    // O'tgan oy — TO'LIQ oy (oxirgi kuni ham kiradi).
    setFrom(iso(new Date(now.getFullYear(), now.getMonth() - 1, 1)))
    setTo(iso(new Date(now.getFullYear(), now.getMonth(), 0)))
  }

  const sync = async () => {
    setSyncing(true)
    setSyncNote(null)
    try {
      const res = await syncIgAdsStats()
      setStatus(res.status)
      // ⚠️ HTTP 200 kelgani "bajarildi" degani EMAS — sabab javobning ichida.
      setSyncNote({
        ok: res.ok,
        text: res.ok ? `Yangilandi — ${num(res.rows)} ta qator olindi` : res.error,
      })
      if (res.ok) load()
    } catch (e) {
      setSyncNote({ ok: false, text: apiErrorMessage(e, "Sinxronizatsiya bajarilmadi") })
    } finally {
      setSyncing(false)
    }
  }

  const offset = overview?.currencyOffset ?? status?.currencyOffset ?? 2
  const currency = overview?.currency ?? status?.currency ?? ''
  const totals = overview?.totals

  /** Kunlik grafiklar uchun umumiy qator (xarajat MAJOR songa aylantiriladi). */
  const daily = useMemo(
    () => (overview?.daily ?? []).map((d) => ({
      name: shortDay(d.date),
      Xarajat: adsMoneyMajor(d.spendMinor, offset),
      'CRM lidlari': d.crmLeads,
      'Meta lidlari': d.metaLeads,
    })),
    [overview, offset],
  )

  const platformSlices = useMemo(
    () => (overview?.platforms ?? [])
      .filter((p) => p.spendMinor > 0)
      .map((p) => ({
        key: p.platform,
        label: PLATFORM_LABEL[p.platform] ?? p.platform,
        color: PLATFORM_COLOR[p.platform] ?? PLATFORM_COLOR.all,
        spendMinor: p.spendMinor,
        crmLeads: p.crmLeads,
      })),
    [overview],
  )

  /**
   * Tanlangan kampaniya ro'yxatda bo'lmasligi mumkin (davr o'zgargan bo'lsa) — u holda
   * u ro'yxatga QO'SHILADI, aks holda `select` mos variantsiz qolib, tanlov ko'rinmay
   * ketardi.
   */
  const options = useMemo(() => {
    if (campaignId === '' || campaignOptions.some((o) => o.id === campaignId)) return campaignOptions
    const picked = tree?.campaigns[0]
    return [{ id: campaignId, name: picked?.name || campaignId }, ...campaignOptions]
  }, [campaignOptions, campaignId, tree])

  const connected = overview ? overview.connected : (status?.connected ?? false)

  /**
   * OXIRGI SINXRONIZATSIYA XATOSI — sahifaning eng muhim ogohlantirishi.
   *
   * 🔴 U «Ma'lumot holati» tabiga BERKITILMAYDI: xato bo'lsa ekrandagi barcha
   * raqamlar eskirgan yoki chala bo'lishi mumkin, marketolog esa "Umumiy" tabida turib
   * byudjet qarorini qabul qiladi. Shuning uchun ogohlantirish tab tugmalaridan TEPADA,
   * ya'ni qaysi tab ochiq bo'lishidan qat'i nazar ko'rinadi.
   *
   * ⚠️ Manba IKKITA: `status` (yengil so'rov) va `overview` (hisobot) — `StatusBlock`
   * dagi bilan AYNAN bir xil qoida, aks holda bir ekranning ikki joyida ikki xil xato
   * ko'rinardi.
   */
  const lastError = status?.lastError || overview?.lastError || ''

  return (
    <MarketingPage
      title="Reklama statistikasi"
      sub="Meta reklamasiga sarflangan pul va u keltirgan lidlar, o'quvchilar va daromad"
      actions={(
        <div style={{ display: 'flex', gap: 8 }}>
          <button className="btn btn-ghost btn-sm" onClick={load} disabled={loading}>
            <Icon name="refresh" /> Yangilash
          </button>
          {canSync && (
            <button className="btn btn-outline btn-sm" onClick={sync} disabled={syncing}>
              <Icon name="zap" /> {syncing ? 'Olinmoqda…' : "Meta'dan sinxronlash"}
            </button>
          )}
        </div>
      )}
    >
      <div className="fade-up">
        {/* Modul o'chiq bo'lsa — statistika yangilanmaydi, ekrandagi raqam esa eskiradi. */}
        {status && !status.enabled && (
          <div className="mk-alert">
            <Icon name="warn" style={{ width: 18, height: 18, flexShrink: 0 }} />
            <div>
              <div className="mk-alert-title">Reklama statistikasi moduli o'chiq</div>
              <div>
                Ma'lumot avtomatik yangilanmaydi — quyidagi raqamlar eski bo'lishi mumkin.{' '}
                <Link to="/admin/marketing/settings">Sozlamalar</Link> bo'limidan yoqing.
              </div>
            </div>
          </div>
        )}

        {/* Oxirgi sinxronizatsiya xatosi — eng tepada, TABLARDAN TASHQARIDA: byudjet
            qarori eskirgan raqamga asoslanib qolmasin. */}
        {lastError !== '' && (
          <div className="mk-alert mk-alert-danger">
            <Icon name="warn" style={{ width: 18, height: 18, flexShrink: 0 }} />
            <div style={{ flex: 1 }}>
              <div className="mk-alert-title">Oxirgi sinxronizatsiya xato bilan tugadi</div>
              <div>{lastError}</div>
              <div style={{ marginTop: 4 }}>
                Quyidagi raqamlar <b>eskirgan yoki chala</b> bo'lishi mumkin — qaror
                chiqarishdan oldin sinxronizatsiyani tiklang.
              </div>
            </div>
            <div style={{ display: 'flex', gap: 8, flexWrap: 'wrap' }}>
              {/* Xatoning batafsili (oxirgi sinxronizatsiya vaqti, statistika kuni,
                  qatorlar soni) «Ma'lumot holati» tabida — havola o'sha yerga olib boradi. */}
              {/* ⚠️ Akkaunt ulanmagan bo'lsa tab qatori umuman chizilmaydi — havola ham
                  ko'rsatilmaydi, aks holda u hech qayerga olib bormasdi. */}
              {connected && (
                <button className="btn btn-ghost btn-sm" onClick={() => setTab('holat')}>
                  <Icon name="info" /> Ma'lumot holati
                </button>
              )}
              {canSync && (
                <button className="btn btn-outline btn-sm" onClick={sync} disabled={syncing}>
                  <Icon name="refresh" /> {syncing ? 'Urinilmoqda…' : 'Qayta urinish'}
                </button>
              )}
            </div>
          </div>
        )}

        {/* ───────────── 1. Filtr paneli — TABLARDAN TASHQARIDA ─────────────
            Davr, platforma va kampaniya HAR BIR tabga tegishli, shuning uchun ular
            tab almashganda ham joyida qoladi. */}
        <MkCard>
          <div style={{ display: 'flex', gap: 14, alignItems: 'flex-end', flexWrap: 'wrap' }}>
            <div style={{ minWidth: 150 }}>
              <label className="field-label">Boshlanishi</label>
              <input className="input" type="date" value={from} onChange={(e) => setFrom(e.target.value)} />
            </div>
            <div style={{ minWidth: 150 }}>
              <label className="field-label">Tugashi</label>
              <input className="input" type="date" value={to} onChange={(e) => setTo(e.target.value)} />
            </div>

            <div className="seg">
              <button onClick={() => preset('today')}>Bugun</button>
              <button onClick={() => preset('d7')}>7 kun</button>
              <button onClick={() => preset('d30')}>30 kun</button>
              <button onClick={() => preset('month')}>Bu oy</button>
              <button onClick={() => preset('prevMonth')}>O'tgan oy</button>
            </div>

            <div className="seg">
              {([['all', 'Hammasi'], ['instagram', 'Instagram'], ['facebook', 'Facebook']] as const).map(([k, l]) => (
                <button key={k} className={platform === k ? 'active' : ''} onClick={() => setPlatform(k)}>
                  {l}
                </button>
              ))}
            </div>

            <div style={{ minWidth: 220, flex: 1 }}>
              <label className="field-label">Kampaniya</label>
              <select className="input" value={campaignId} onChange={(e) => setCampaignId(e.target.value)}>
                <option value="">Barcha kampaniyalar</option>
                {options.map((o) => (
                  <option key={o.id} value={o.id}>{o.name}</option>
                ))}
              </select>
            </div>
          </div>
        </MkCard>

        {syncNote && (
          <MkNotice
            text={syncNote.text}
            tone={syncNote.ok ? 'success' : 'danger'}
            onClose={() => setSyncNote(null)}
          />
        )}

        {/* ───────────── Backend ogohlantirishlari — JIM YUTILMAYDI ─────────────
            Tablardan TASHQARIDA: ular raqamlarni qanday o'qish kerakligini aytadi,
            ya'ni "Kampaniyalar" tabida turgan odam ham ko'rishi shart.
            ⚠️ `loading` blokidan ham TASHQARIDA: davr almashtirilganda izohlar bir zumga
            yo'qolib qayta paydo bo'lsa, ostidagi butun sahifa sakrab ketardi. */}
        {!error && connected && overview && overview.notes.length > 0 && (
          <MkCard
            title="Hisobotni to'g'ri o'qish uchun"
            sub="Raqamlarning chegaralari va ularning sabablari"
          >
            <ul style={{ margin: 0, paddingLeft: 18, display: 'grid', gap: 6 }}>
              {overview.notes.map((n) => (
                <li key={n} style={{ fontSize: 12.5, color: 'var(--text-2)', lineHeight: 1.5 }}>{n}</li>
              ))}
            </ul>
          </MkCard>
        )}

        {/* ───────────── Sahifa ichidagi bo'limlar ─────────────
            ⚠️ Tab qatori ATAYIN `loading` shartidan TASHQARIDA (filtrlar bilan bir xil
            mantiq): davr yoki platforma o'zgartirilganda butun qator yo'qolib qayta
            chizilsa, foydalanuvchi endigina bosgan tugma ko'z oldida sakrab ketardi. */}
        {!error && connected && (
          <div className="mk-scroll-x" style={{ marginBottom: 18 }}>
            <div className="seg" role="tablist" aria-label="Reklama statistikasi bo'limlari">
              {TABS.map((t) => (
                <button
                  key={t.key}
                  type="button"
                  role="tab"
                  aria-selected={tab === t.key}
                  className={tab === t.key ? 'active' : ''}
                  onClick={() => setTab(t.key)}
                >
                  <Icon
                    name={t.icon}
                    style={{ width: 14, height: 14, marginRight: 6, verticalAlign: -2 }}
                  />
                  {t.label}
                  {/* Xato belgisi — tabda "ko'rilishi kerak narsa bor" ekani bilinsin.
                      ⚠️ Rang YAGONA kanal emas: xatoning O'ZI tepadagi qizil blokda
                      matn bilan turibdi, bu faqat qo'shimcha ishora. */}
                  {t.key === 'holat' && lastError !== '' && (
                    <span
                      title="Oxirgi sinxronizatsiya xato bilan tugagan"
                      style={{
                        display: 'inline-block', width: 7, height: 7, borderRadius: 99,
                        background: 'var(--danger)', marginLeft: 6, verticalAlign: 'middle',
                      }}
                    />
                  )}
                </button>
              ))}
            </div>
          </div>
        )}

        {loading && <MkLoading />}
        {!loading && error && <MkError text={error} onRetry={load} />}

        {/* Bo'sh holat — "buzildi" emas, "hali sozlanmagan". */}
        {!loading && !error && !connected && (
          <>
            <MkEmpty
              text="Reklama akkaunti ulanmagan"
              hint="Statistika Meta Ads Insights'dan olinadi. Sozlamalar bo'limida reklama akkaunti (act_…) va ads_read ruxsatli System User tokenini kiriting."
            />
            <div style={{ marginTop: 12 }}>
              <Link className="btn btn-primary btn-sm" to="/admin/marketing/settings">
                <Icon name="settings" /> Sozlamalarga o'tish
              </Link>
            </div>
          </>
        )}

        {!loading && !error && connected && overview && totals && (
          <>
            {/* ═════════ TAB: UMUMIY — KPI va kunlik grafiklar ═════════ */}
            {tab === 'umumiy' && (
              <>
                <div className="mk-kpi" style={{ marginBottom: 22 }}>
                  <MkStat
                    label="Xarajat"
                    value={formatAdsMoney(totals.spendMinor, offset, currency)}
                    tone="primary"
                    icon="zap"
                    hint={`${overview.from} … ${overview.to}`}
                  />
                  <MkStat
                    label="Ko'rsatish"
                    value={num(totals.impressions)}
                    icon="eye"
                    hint={`${num(totals.clicks)} klik`}
                  />
                  <MkStat
                    /* ⚠️ "≈" — bu ANIQ son emas. Chegaralar hint'da ochiq yoziladi. */
                    label="Qamrov"
                    value={`≈ ${num(totals.reach)}`}
                    icon="users"
                    hint={`kamida ${num(totals.reach)} · ko'pi bilan ${num(totals.reachUpper)}`}
                  />
                  <MkStat
                    label="CRM lidlari"
                    value={num(totals.crmLeads)}
                    tone="primary"
                    icon="user"
                    hint={`Meta: ${num(totals.metaLeads)} · CRM: ${num(totals.crmLeads)}`}
                  />
                  {/* ⚠️ Lidlar bilan QO'SHILMAYDI — bu AYRI natija turi (Click-to-Direct).
                      Kampaniyada forma bo'lmasa "CRM lidlari" nol turadi-yu, reklama aslida
                      ishlagan bo'ladi: aynan shu son buni ko'rsatadi. */}
                  <MkStat
                    label="Yozishma boshlandi"
                    value={num(totals.msgStarted)}
                    icon="msg"
                    hint="Click-to-Direct natijasi · Meta, 7 kunlik oyna"
                  />
                  <MkStat
                    label="CPL — lid narxi"
                    value={totals.cplMinor == null ? '—' : formatAdsMoney(totals.cplMinor, offset, currency)}
                    tone="warning"
                    icon="gauge"
                    hint={totals.cplMinor == null ? "hisoblab bo'lmadi" : 'xarajat / CRM lidlari'}
                  />
                  <MkStat
                    label="O'quvchi bo'ldi"
                    value={num(totals.converted)}
                    tone="success"
                    icon="check"
                    hint={`${num(totals.paid)} tasi to'lov qildi`}
                  />
                  <MkStat
                    label="ROI"
                    value={formatRoi(totals.roi)}
                    /* ⚠️ Tus — YAGONA kanal emas: qiymatning O'ZI ham ekranda turadi
                       (`formatRoi`), ya'ni rangni ko'rmagan odam ham xulosa chiqara oladi. */
                    tone={totals.roi == null ? 'muted' : totals.roi >= 0 ? 'success' : 'danger'}
                    icon="trendUp"
                    hint={`daromad ${formatAdsMoney(totals.revenueMinor, offset, currency)} — butun umr bo'yicha`}
                  />
                </div>

                {/* ───────────── Grafik 1 — kunlik xarajat ─────────────
                    ⚠️ Lidlar ALOHIDA grafikda: ikki y-o'q TAQIQLANADI. */}
                <ChartCard
                  title="Kunlik xarajat"
                  sub={`Reklamaga sarflangan pul${currency ? ` (${currency})` : ''}`}
                  empty="Bu davrda xarajat yo'q"
                  rows={daily.length}
                >
                  <LineChart data={daily} margin={{ top: 8, right: 8, left: -8, bottom: 0 }}>
                    <CartesianGrid strokeDasharray="3 3" vertical={false} />
                    <XAxis dataKey="name" tick={{ fontSize: 11 }} />
                    <YAxis tick={{ fontSize: 11 }} tickFormatter={(v: number) => axisNum(v)} />
                    <Tooltip formatter={(v) => [`${axisNum(Number(v))}${currency ? ` ${currency}` : ''}`, 'Xarajat']} />
                    <Line type="monotone" dataKey="Xarajat" stroke={C_SPEND} strokeWidth={2} dot={false} />
                  </LineChart>
                </ChartCard>

                {/* ───────────── Grafik 2 — kunlik lidlar ─────────────
                    Ikkala seriya ham "dona", ya'ni BITTA y-o'q halol. Meta va CRM sonlari
                    ATAYIN yonma-yon: farqi ko'rinib tursin. */}
                <ChartCard
                  title="Kunlik lidlar"
                  sub="CRM'ga tushgan takrorsiz lidlar va Meta hisoblagan lidlar"
                  empty="Bu davrda lid yo'q"
                  rows={daily.length}
                >
                  <LineChart data={daily} margin={{ top: 8, right: 8, left: -20, bottom: 0 }}>
                    <CartesianGrid strokeDasharray="3 3" vertical={false} />
                    <XAxis dataKey="name" tick={{ fontSize: 11 }} />
                    <YAxis tick={{ fontSize: 11 }} allowDecimals={false} />
                    <Tooltip formatter={(v, n) => [`${num(Number(v))} ta`, n as string]} />
                    <Line type="monotone" dataKey="CRM lidlari" stroke={C_LEADS} strokeWidth={2} dot={false} />
                    <Line
                      type="monotone" dataKey="Meta lidlari" stroke={C_SPEND}
                      strokeWidth={2} strokeDasharray="4 3" dot={false}
                    />
                  </LineChart>
                </ChartCard>
              </>
            )}

            {/* ═════════ TAB: KAMPANIYALAR — kampaniya → adset → e'lon ═════════ */}
            {tab === 'kampaniyalar' && (
              <CampaignTable data={tree} offset={offset} currency={currency} />
            )}

            {/* ═════════ TAB: PLATFORMALAR ═════════ */}
            {tab === 'platformalar' && (
              <PlatformCard slices={platformSlices} offset={offset} currency={currency} />
            )}

            {/* ═════════ TAB: MA'LUMOT HOLATI ═════════ */}
            {tab === 'holat' && (
              <StatusBlock
                status={status}
                overview={overview}
                canSync={canSync}
                syncing={syncing}
                onSync={sync}
              />
            )}
          </>
        )}
      </div>
    </MarketingPage>
  )
}

/* ═══════════════════════════════ QISMLAR ═══════════════════════════════ */

/** Grafik kartochkasi — bo'sh holat har ikkala grafikda bir xil ko'rinsin. */
function ChartCard({
  title, sub, empty, rows, children,
}: {
  title: string
  sub: string
  empty: string
  rows: number
  children: ReactElement
}) {
  return (
    <MkCard title={title} sub={sub}>
      {rows === 0
        ? <MkEmpty text={empty} />
        : (
          <div style={{ width: '100%', height: 250 }}>
            <ResponsiveContainer>{children}</ResponsiveContainer>
          </div>
        )}
    </MkCard>
  )
}

/** Platforma kesimining bitta bo'lagi (ekranda ko'rsatiladigan shakl). */
type PlatformSlice = {
  key: string
  label: string
  color: string
  spendMinor: number
  crmLeads: number
}

/**
 * PLATFORMA ULUSHI — "xarajat qayerga ketdi".
 *
 * ⚠️ Rang YAGONA kanal bo'lib qolmasin: har qatorda nom, lid soni, summa va foiz
 * MATN bilan ham turadi.
 */
function PlatformCard({
  slices, offset, currency,
}: {
  slices: PlatformSlice[]
  offset: number
  currency: string
}) {
  const total = slices.reduce((s, p) => s + p.spendMinor, 0)

  return (
    <MkCard
      title="Platforma bo'yicha ulush"
      sub="Xarajat qayerga ketdi — Instagram yoki Facebook"
    >
      {slices.length === 0
        ? (
          <MkEmpty
            text="Platforma kesimi yo'q"
            hint="Statistika platformalarga ajratilmagan holda yuklangan bo'lishi mumkin — keyingi sinxronizatsiyadan so'ng ko'rinadi."
          />
        )
        : (
          <div style={{ display: 'flex', gap: 20, alignItems: 'center', flexWrap: 'wrap' }}>
            <div style={{ width: 220, height: 200 }}>
              <ResponsiveContainer>
                <PieChart>
                  <Pie
                    data={slices} dataKey="spendMinor" nameKey="label"
                    cx="50%" cy="50%" innerRadius={54} outerRadius={88}
                    paddingAngle={1} isAnimationActive={false}
                  >
                    {slices.map((p) => <Cell key={p.key} fill={p.color} />)}
                  </Pie>
                  <Tooltip
                    formatter={(v, n) => [formatAdsMoney(Number(v), offset, currency), n as string]}
                  />
                </PieChart>
              </ResponsiveContainer>
            </div>

            {/* Legenda — rang YAGONA kanal bo'lib qolmasin: son va foiz matn bilan ham bor. */}
            <div style={{ flex: 1, minWidth: 240 }}>
              {slices.map((p) => {
                const pct = total > 0 ? Math.round((p.spendMinor / total) * 100) : 0
                return (
                  <div className="metric-row" key={p.key} style={{ gap: 10 }}>
                    <div style={{ flex: 1, minWidth: 0 }}>
                      <div style={{ fontSize: 13, fontWeight: 600 }}>
                        {p.label}
                        <span className="feed-time" style={{ marginLeft: 8 }}>{num(p.crmLeads)} lid</span>
                      </div>
                      <div className="progress-track" style={{ marginTop: 6 }}>
                        <div className="progress-fill" style={{ width: `${pct}%`, background: p.color }} />
                      </div>
                    </div>
                    <div style={{ textAlign: 'right', minWidth: 110 }}>
                      <div className="mk-num">{formatAdsMoney(p.spendMinor, offset, currency)}</div>
                      <div className="feed-time">{pct}%</div>
                    </div>
                  </div>
                )
              })}
            </div>
          </div>
        )}
    </MkCard>
  )
}

/**
 * KAMPANIYA JADVALI — kampaniya → adset → e'lon, ochiladigan qatorlar bilan.
 *
 * ⚠️ "Jami" qatori SERVERDAN keladi (`totals`), ko'rinib turgan qatorlardan QO'SHIB
 * chiqarilmaydi: kampaniyalar 200 tada qirqilishi mumkin va u holda jamlanma noto'g'ri
 * bo'lardi (`books.md` dagi saboq).
 *
 * ⚠️ Jadval 14 ustunli — u gorizontal SKROLL ichida (`mk-scroll-x`) turadi; sahifaning
 * O'ZI hech qachon yon tomonga siljimaydi.
 */
function CampaignTable({
  data, offset, currency,
}: {
  data: IgRoiCampaigns | null
  offset: number
  currency: string
}) {
  const [open, setOpen] = useState<Set<string>>(new Set())

  const toggle = (id: string) => {
    setOpen((prev) => {
      const next = new Set(prev)
      if (next.has(id)) next.delete(id)
      else next.add(id)
      return next
    })
  }

  if (!data) return null

  return (
    <MkCard
      title="Kampaniyalar"
      sub={`Qatorni bosib adset va e'lonlarni oching${data.insightLevel ? ` · statistika «${data.insightLevel}» darajasidan yig'ilgan` : ''}`}
    >
      {data.campaigns.length === 0
        ? <MkEmpty text="Bu davrda kampaniya yo'q" hint="Boshqa oraliq yoki platformani tanlab ko'ring." />
        : (
          <div className="mk-scroll-x">
            <table className="mk-table" style={{ minWidth: 1160 }}>
              <thead>
                <tr>
                  <th>Nomi</th>
                  <th style={{ textAlign: 'right' }}>Xarajat</th>
                  <th style={{ textAlign: 'right' }}>Ko'rsatish</th>
                  <th style={{ textAlign: 'right' }}>Qamrov ≈</th>
                  <th style={{ textAlign: 'right' }}>Meta lid</th>
                  {/* Yozishma — lidlarga QO'SHILMAYDIGAN alohida natija (Click-to-Direct). */}
                  <th style={{ textAlign: 'right' }}>Yozishma</th>
                  <th style={{ textAlign: 'right' }}>CRM lid</th>
                  <th style={{ textAlign: 'right' }}>CPL</th>
                  <th style={{ textAlign: 'right' }}>O'quvchi</th>
                  <th style={{ textAlign: 'right' }}>To'ladi</th>
                  <th style={{ textAlign: 'right' }}>Daromad</th>
                  <th style={{ textAlign: 'right' }}>CAC</th>
                  <th style={{ textAlign: 'right' }}>ROI</th>
                  <th />
                </tr>
              </thead>
              <tbody>
                <TreeRows
                  nodes={data.campaigns} depth={0} campaignId=""
                  open={open} toggle={toggle} offset={offset} currency={currency}
                />
                <tr style={{ fontWeight: 800, background: 'var(--surface-2)' }}>
                  <td>Jami{data.campaignId ? ' (tanlangan kampaniya)' : ''}</td>
                  <td className="mk-num">{formatAdsMoney(data.totals.spendMinor, offset, currency)}</td>
                  <td className="mk-num">{num(data.totals.impressions)}</td>
                  <td className="mk-num">≈ {num(data.totals.reach)}</td>
                  <td className="mk-num">{num(data.totals.metaLeads)}</td>
                  <td className="mk-num">{num(data.totals.msgStarted)}</td>
                  <td className="mk-num">{num(data.totals.crmLeads)}</td>
                  <td className="mk-num">
                    {data.totals.cplMinor == null ? '—' : formatAdsMoney(data.totals.cplMinor, offset, currency)}
                  </td>
                  <td className="mk-num">{num(data.totals.converted)}</td>
                  <td className="mk-num">{num(data.totals.paid)}</td>
                  <td className="mk-num">{formatAdsMoney(data.totals.revenueMinor, offset, currency)}</td>
                  <td className="mk-num">
                    {data.totals.cacMinor == null ? '—' : formatAdsMoney(data.totals.cacMinor, offset, currency)}
                  </td>
                  <td className="mk-num">{formatRoi(data.totals.roi)}</td>
                  <td />
                </tr>
              </tbody>
            </table>
          </div>
        )}

      {/* ───────────── Hisobot ostidagi izoh: ATRIBUTSIYA OYNASI ─────────────
          ⚠️ Qiymat Meta bergan HOLICHA (`7d_click,1d_view`) — TARJIMA QILINMAYDI: bu Ads
          Manager'dagi sozlamaning texnik nomi va marketolog uni o'sha yerda topishi kerak.
          ⚠️ Nega muhim: Meta lidlari qaysi oyna bo'yicha sanalganini bilmasdan ularni CRM
          lidlari bilan taqqoslash noto'g'ri xulosa beradi. */}
      {data.attributionSetting && (
        <div className="field-hint" style={{ marginTop: 12, lineHeight: 1.5 }}>
          Atributsiya oynasi: <strong>{data.attributionSetting}</strong> — Meta konversiyalari
          (lidlar, yozishmalar) shu oyna bo'yicha sanalgan. CRM lidlari esa haqiqiy kelgan
          vaqti bo'yicha, ya'ni ikki son to'g'ridan-to'g'ri taqqoslanmaydi.
        </div>
      )}
    </MkCard>
  )
}

/**
 * Daraxt qatorlari (rekursiv).
 *
 * ⚠️ «Lidlarni ko'rish →» havolasi HAR qatorda o'sha qatorning KAMPANIYASIGA olib boradi
 * (adset/e'lon qatorida ham) — reklama lidlari sahifasi kampaniya bo'yicha filtrlanadi,
 * adset id'sini `campaign` parametriga yozib yuborish bo'sh ro'yxat berardi.
 */
function TreeRows({
  nodes, depth, campaignId, open, toggle, offset, currency,
}: {
  nodes: IgRoiNode[]
  depth: number
  /** Yuqoridan uzatilgan kampaniya id'si (0-darajada tugunning O'ZI kampaniya). */
  campaignId: string
  open: Set<string>
  toggle: (id: string) => void
  offset: number
  currency: string
}) {
  return (
    <>
      {nodes.map((n) => {
        const key = `${n.level}:${n.id}`
        const hasKids = n.children.length > 0
        const expanded = open.has(key)
        const camp = depth === 0 ? n.id : campaignId

        return (
          <Fragment key={key}>
            <tr>
              <td style={{ paddingLeft: 10 + depth * 18, minWidth: 220 }}>
                <div style={{ display: 'flex', alignItems: 'center', gap: 6 }}>
                  {hasKids
                    ? (
                      <button
                        className="btn btn-ghost btn-sm btn-icon-only"
                        onClick={() => toggle(key)}
                        title={expanded ? 'Yopish' : 'Ochish'}
                      >
                        <Icon name={expanded ? 'chevDown' : 'chevRight'} />
                      </button>
                    )
                    : <span style={{ width: 14 }} />}
                  <div style={{ minWidth: 0 }}>
                    <div style={{ fontWeight: depth === 0 ? 700 : 500 }}>{n.name || '—'}</div>
                    <div className="feed-time">
                      {LEVEL_LABEL[n.level] ?? n.level}
                      {n.status && ` · ${n.status}`}
                      {n.crmLeadsDeleted > 0 && ` · ${n.crmLeadsDeleted} ta lid o'chirilgan`}
                    </div>
                  </div>
                </div>
              </td>
              <td className="mk-num">{formatAdsMoney(n.spendMinor, offset, currency)}</td>
              <td className="mk-num">{num(n.impressions)}</td>
              <td className="mk-num" title={`kamida ${num(n.reach)}, ko'pi bilan ${num(n.reachUpper)}`}>
                ≈ {num(n.reach)}
              </td>
              <td className="mk-num">{num(n.metaLeads)}</td>
              <td className="mk-num" title="Meta hisoblagan boshlangan yozishmalar (7 kunlik oyna)">
                {num(n.msgStarted)}
              </td>
              <td className="mk-num" title={`xom qatorlar: ${num(n.adLeadRows)}`}>{num(n.crmLeads)}</td>
              <td className="mk-num">{n.cplMinor == null ? '—' : formatAdsMoney(n.cplMinor, offset, currency)}</td>
              <td className="mk-num">{num(n.converted)}</td>
              <td className="mk-num">{num(n.paid)}</td>
              <td className="mk-num">{formatAdsMoney(n.revenueMinor, offset, currency)}</td>
              <td className="mk-num">{n.cacMinor == null ? '—' : formatAdsMoney(n.cacMinor, offset, currency)}</td>
              <td className="mk-num">{formatRoi(n.roi)}</td>
              <td style={{ whiteSpace: 'nowrap' }}>
                {camp && (
                  <Link className="btn btn-ghost btn-sm" to={`/admin/marketing/reklama-lidlari?campaign=${camp}`}>
                    Lidlarni ko'rish <Icon name="arrowRight" />
                  </Link>
                )}
              </td>
            </tr>

            {expanded && hasKids && (
              <TreeRows
                nodes={n.children} depth={depth + 1} campaignId={camp}
                open={open} toggle={toggle} offset={offset} currency={currency}
              />
            )}
          </Fragment>
        )
      })}
    </>
  )
}

const LEVEL_LABEL: Record<string, string> = {
  campaign: 'Kampaniya',
  adset: 'Adset',
  ad: "E'lon",
  total: 'Jami',
}

/**
 * HOLAT BLOKI — "raqamlar qachongi va nega bunday".
 * Xato bo'lsa qizil chip va «Qayta urinish» tugmasi shu yerda ham turadi.
 */
function StatusBlock({
  status, overview, canSync, syncing, onSync,
}: {
  status: IgAdsStatus | null
  overview: IgRoiOverview
  canSync: boolean
  syncing: boolean
  onSync: () => void
}) {
  const lastSync = status?.lastSyncAt || overview.lastSyncAt
  const lastError = status?.lastError || overview.lastError

  return (
    <MkCard
      title="Ma'lumot holati"
      sub="Statistika Meta'dan kuniga bir marta olinadi"
      actions={canSync && (
        <button className="btn btn-outline btn-sm" onClick={onSync} disabled={syncing}>
          <Icon name="refresh" /> {syncing ? 'Olinmoqda…' : 'Qayta urinish'}
        </button>
      )}
    >
      <div className="row-between">
        <span className="opt-name">Oxirgi sinxronizatsiya</span>
        <span>{shortTime(lastSync)}</span>
      </div>
      <div className="row-between">
        <span className="opt-name">Eng oxirgi statistika kuni</span>
        <span>{status?.lastStatDate || '—'}</span>
      </div>
      <div className="row-between">
        <span className="opt-name">Reklama akkaunti</span>
        <span>
          {overview.adAccountName || overview.adAccountId || '—'}
          {overview.currency && ` · ${overview.currency}`}
        </span>
      </div>
      <div className="row-between">
        <span className="opt-name">Vaqt zonasi</span>
        <span>{overview.timezoneName || '—'}</span>
      </div>
      <div className="row-between">
        <span className="opt-name">Bazadagi qatorlar</span>
        <span>
          {num(status?.insightRows ?? 0)} ta statistika · {num(status?.entityRows ?? 0)} ta e'lon yozuvi
        </span>
      </div>

      {lastError !== '' && (
        <div style={{ marginTop: 12 }}>
          <span className="badge badge-danger">Xato</span>
          <div style={{ marginTop: 6, fontSize: 12.5, color: 'var(--danger)' }}>{lastError}</div>
        </div>
      )}
      {lastError === '' && lastSync !== '' && (
        <div style={{ marginTop: 12 }}>
          <span className="badge badge-success">Oxirgi yangilanish muvaffaqiyatli</span>
        </div>
      )}
    </MkCard>
  )
}
