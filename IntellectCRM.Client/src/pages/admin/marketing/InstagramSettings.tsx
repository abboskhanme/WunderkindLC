import { useCallback, useEffect, useState } from 'react'
import type { ReactNode } from 'react'
import { Link, useSearchParams } from 'react-router-dom'
import { usePerm } from '@/lib/permissions'
import { apiErrorMessage, formatDateTime } from '@/lib/utils'
import {
  connectIgWithToken, disconnectIg, disconnectIgAdPage, getIgAdStatus, getIgConnectUrl,
  getIgSettings, getIgStatus, refreshIgToken, saveIgAdPage, saveIgSettings, testIgAgent,
  type IgAdStatus, type IgChannel, type IgSettings, type IgStatus, type IgTestAgentResult,
} from '@/api/services/instagram'
import {
  disconnectAdsStatsAccount, getAdsStatsStatus, getCapiStatus, getContentStatus,
  saveAdsStatsAccount, saveCapiSettings, sendCapiNow, syncAdsStatsNow,
  type IgAdsStatsStatus, type IgCapiStatus, type IgContentStatus,
} from '@/api/services/instagramCapi'
import {
  checkMetaConnection, type IgDiagItem, type IgDiagResult,
} from '@/api/services/instagramDiag'
import {
  ChannelIcon, Icon, MarketingPage, MkCopyRow, MkError, MkLoading, MkStatusCard,
} from './mk'

/**
 * SOZLAMALAR — Instagram akkauntni ulash, modul bayroqlari va DIAGNOSTIKA.
 *
 * Sahifaning asosiy vazifasi — "nima ishlayapti, nima yetishmayapti" savoliga bir qarashda
 * javob berish: akkaunt ulanganmi, token necha kun qoldi, webhook obunasi bormi, Gemini
 * sozlanganmi, `.env` kalitlari qo'yilganmi.
 *
 * ⚠️ Maxfiy qiymatlar (token, app secret, verify token) HECH QACHON ko'rsatilmaydi —
 * faqat "sozlangan / sozlanmagan" holati.
 *
 * ═══ SAHIFA ICHIDAGI TABLAR ═══
 *
 * Ilgari bu sahifa BITTA ulkan skroll edi (o'nga yaqin karta): admin kerakli sozlamani
 * topolmasdi va "bu yerda umuman bormi?" degan savol bilan qolardi. Endi kartalar
 * MANTIQIY guruhlarga bo'lingan va tugmalar bilan almashtiriladi:
 *
 * | Tab | Ichida nima bor | Savoli |
 * |---|---|---|
 * | `akkaunt` | Ulanishni tekshirish · Instagram akkaunt · Meta ilovasi · Holat | "ulanganmi va ishlayaptimi?" |
 * | `avtojavob` | Avtojavob bayroqlari · AI va chegaralar | "kimga va qanday javob beramiz?" |
 * | `reklama` | Lead Ads · Ads Insights · CAPI | "pulli reklama tomoni" |
 * | `kontent` | Kontent joylash | "post joylash yoqilganmi?" |
 * | `sinov` | AI javobini sinash | "AI nima deb javob berardi?" |
 *
 * ⚠️ Tablar **SIDEBAR NAV'GA CHIQARILMAYDI** — bular bo'lim emas, bitta sozlama
 * sahifasining bo'laklari (`mk.tsx` dagi `MkSubnav` izohidagi bir xil mantiq).
 *
 * ⚠️ `MkSubnav` ISHLATILMAYDI: u `NavLink` (ya'ni MARSHRUT) bilan ishlaydi, bu yerda esa
 * bitta sahifa ichida qolamiz. Ko'rinish bir xil bo'lishi uchun AYNI o'sha
 * `mk-subnav` / `mk-subnav-item` klasslari tugmalarga qo'yilgan.
 *
 * ⚠️ **BARCHA TABLAR DOIM DOM'DA TURADI**, faol bo'lmagani `display: none` bilan
 * yashiriladi (`TabPanel`). Sabab: bo'limlarning yarmi O'Z state'i va O'Z
 * `useEffect(load)` iga ega ichki komponentlar (`LeadAdsBlock`, `AdsStatsBlock`,
 * `CapiBlock`, `ContentBlock`, `DiagnosticsBlock`, `TestBlock`). Shartli render qilinsa
 * tab almashgan sayin ular QAYTA yaratilib, kiritilgan token/ID matnlari yo'qolardi va
 * har almashishda bekorga API so'rovi ketardi.
 *
 * ⚠️ «Saqlash» tugmasi ATAYIN tablardan TASHQARIDA (yopishqoq sarlavhada) va BUTUN
 * formani saqlaydi — bayroqlar bitta `IgSettings` obyektida. Har tabga alohida saqlash
 * qo'yilsa foydalanuvchi "qaysi tugma nimani saqlaydi" ni bilmasdi.
 *
 * ⚠️ SAQLASH NATIJASI («Sozlamalar saqlandi» yoki xato) ham AYNI SHU yopishqoq blokda,
 * tab tugmalari ostida. Tugma doim ko'rinib turib, javobi sahifaning eng tepasida oddiy
 * oqimda qolsa — uzun tabda pastga tushgan foydalanuvchi hech qanday natija ko'rmasdi va
 * "hech narsa bo'lmadi" deb qayta-qayta bosardi.
 *
 * ⚠️ ISTISNO — «Reklama» tabidagi uchta karta (Lead Ads · Ads Insights · CAPI) O'Z
 * endpointi va O'Z «Saqlash» tugmasiga ega. Har birida buni ochiq aytadigan ogohlantirish
 * turadi: tepadagi umumiy tugma u maydonlarga TEGMAYDI.
 */

/**
 * Tablar katalogi. `key` — URL'dagi qiymat (`?bolim=…`), shuning uchun O'ZBEKCHA va
 * BARQAROR: bir marta ulashilgan havola keyin ham o'sha joyni ochsin.
 */
const TABS = [
  { key: 'akkaunt', label: 'Akkaunt va ulanish', icon: 'link' },
  { key: 'avtojavob', label: 'Avtojavob', icon: 'ai' },
  { key: 'reklama', label: 'Reklama', icon: 'trendUp' },
  { key: 'kontent', label: 'Kontent', icon: 'image' },
  { key: 'sinov', label: 'Sinov', icon: 'play' },
] as const

type TabKey = (typeof TABS)[number]['key']

/**
 * Bitta tabning tanasi.
 *
 * ⚠️ Yashirish `hidden` atributi bilan YETMAYDI: `.mk-cols2` klassi `display: grid`
 * beradi va u UA'ning `[hidden] { display: none }` qoidasidan kuchliroq. Shuning uchun
 * `display` INLINE yoziladi (`hidden` esa skrinrider uchun qoladi).
 *
 * `mk-cols2` — to'liq ekranli grid (`repeat(auto-fit, minmax(430px, 1fr))`): keng
 * monitorda kartalar yonma-yon tushadi, tor ekranda bitta ustunga qaytadi.
 */
function TabPanel({ active, children }: { active: boolean; children: ReactNode }) {
  return (
    <div className="mk-cols2" style={{ display: active ? 'grid' : 'none' }} hidden={!active}>
      {children}
    </div>
  )
}

export function InstagramSettings() {
  const { can } = usePerm()
  const canEdit = can('marketing.settings', 'edit')
  const [params, setParams] = useSearchParams()

  const [status, setStatus] = useState<IgStatus | null>(null)
  const [form, setForm] = useState<IgSettings | null>(null)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState('')
  const [saving, setSaving] = useState(false)
  const [saved, setSaved] = useState('')
  const [busy, setBusy] = useState('')

  /** «Token bilan ulash» paneli ochiqmi — ATAYIN yopiq turadi (asosiy yo'l — «Ulash» tugmasi). */
  const [showManual, setShowManual] = useState(false)
  const [manualToken, setManualToken] = useState('')

  /** OAuth callback'dan qaytganda (`?connected=1`) muvaffaqiyat xabari ko'rsatiladi. */
  const justConnected = params.get('connected') === '1'

  /**
   * FAOL TAB — URL'da (`?bolim=akkaunt`).
   *
   * Nega state emas, URL: sozlamaning aniq joyiga havola ulashish mumkin bo'lsin va F5
   * dan keyin foydalanuvchi o'sha yerda qolsin (uzun formani qidirib qaytadan topmasin).
   *
   * ⚠️ Parametr YO'Q bo'lsa ham, NOMA'LUM qiymat kelsa ham — BIRINCHI tab ochiladi.
   * Boshqa sahifalardagi «Sozlamalarga o'ting» havolalari `/admin/marketing/settings` ga
   * (parametrsiz) keladi, klientdagi eski/xato kalit esa sahifani bo'shatib qo'ymasin.
   */
  const rawTab = params.get('bolim')
  const tab: TabKey = TABS.find((t) => t.key === rawTab)?.key ?? TABS[0].key

  const goTab = (key: TabKey) => {
    // ⚠️ Mavjud parametrlar SAQLANADI (masalan OAuth'dan kelgan `connected=1`) — aks holda
    // tab almashtirilganda "Akkaunt ulandi" xabari sababsiz yo'qolardi.
    const next = new URLSearchParams(params)
    next.set('bolim', key)
    // `replace` — tab almashtirish brauzer tarixini to'ldirmasin ("orqaga" tugmasi
    // sahifadan CHIQSIN, tablar bo'ylab yurmasin).
    setParams(next, { replace: true })
  }

  /**
   * BOSHQA TABDAGI kartaga sakrash ("Akkaunt kartasiga" tugmasi).
   *
   * 🔴 `setTimeout(…, 50)` ISHLATILMAYDI. react-router v7 da navigatsiya
   * `startTransition` ichida bajariladi, ya'ni 50 ms ichida tab almashishi
   * KAFOLATLANMAGAN: sekin qurilmada nishon karta hali `display: none` panelda turadi va
   * `scrollIntoView` JIMGINA hech narsa qilmaydi — aynan tuzatilmoqchi bo'lgan nosozlik
   * qaytadi.
   *
   * Shuning uchun "sakrash kerak" HOLATDA saqlanadi va effekt AYNAN tab chizilgandan
   * keyin ishlaydi (`jump.tab !== tab` bo'lsa kutiladi — vaqtga umuman tayanilmaydi).
   * `requestAnimationFrame` — brauzer yangi joylashuvni hisoblab bo'lgan kadrda surish
   * uchun.
   *
   * ⚠️ `setJump(null)` AYNAN `raf` ICHIDA: tashqarida chaqirilsa qayta chizilish effekt
   * tozalagichini ishga tushirib, `cancelAnimationFrame` hali bajarilmagan sakrashni
   * bekor qilardi.
   */
  const [jump, setJump] = useState<{ tab: TabKey; id: string } | null>(null)

  const goToCard = (key: TabKey, id: string) => {
    goTab(key)
    setJump({ tab: key, id })
  }

  useEffect(() => {
    if (!jump || jump.tab !== tab) return
    const raf = requestAnimationFrame(() => {
      // Karta yopishqoq sarlavha ortida qolmasligi CSS'dagi `scroll-margin-top`
      // yordamchisi bilan ta'minlanadi (`[id]` selektori, `--mk-head-h` dan).
      document.getElementById(jump.id)?.scrollIntoView({ behavior: 'smooth', block: 'start' })
      setJump(null)
    })
    return () => cancelAnimationFrame(raf)
  }, [jump, tab])

  const load = useCallback(() => {
    setLoading(true)
    setError('')
    Promise.all([getIgStatus(), getIgSettings()])
      .then(([st, s]) => { setStatus(st); setForm(s) })
      .catch((e) => setError(apiErrorMessage(e, "Sozlamalarni yuklab bo'lmadi")))
      .finally(() => setLoading(false))
  }, [])

  useEffect(load, [load])

  const patch = (p: Partial<IgSettings>) => setForm((f) => (f ? { ...f, ...p } : f))

  const save = async () => {
    if (!form) return
    setSaving(true)
    setError('')
    setSaved('')
    try {
      const next = await saveIgSettings(form)
      setForm(next)
      setSaved('Sozlamalar saqlandi.')
      setStatus(await getIgStatus())
    } catch (e) {
      setError(apiErrorMessage(e, "Saqlab bo'lmadi"))
    } finally {
      setSaving(false)
    }
  }

  /** OAuth: server `state` yaratadi va bizni Instagram'ga yuboradi. */
  const connect = async () => {
    setBusy('connect')
    setError('')
    try {
      const url = await getIgConnectUrl()
      window.location.href = url
    } catch (e) {
      setError(apiErrorMessage(e, "Ulanish manzilini olib bo'lmadi"))
      setBusy('')
    }
  }

  /**
   * QO'LDA ULASH — OAuth yurmaganda ishlatiladigan ZAXIRA yo'l.
   *
   * ⚠️ Token maydonda SAQLANMAYDI: muvaffaqiyatdan keyin darhol tozalanadi va panel yopiladi.
   * Sabab — u ekranda ochiq turgan jonli kalit; yonidan o'tgan odam ko'chirib olishi mumkin
   * (server ham uni hech qachon qaytarmaydi, ya'ni qayta ko'rsatib bo'lmaydi).
   */
  const connectToken = async () => {
    const value = manualToken.trim()
    if (!value) return
    setBusy('token')
    setError('')
    try {
      const next = await connectIgWithToken(value)
      setStatus(next)
      setManualToken('')
      setShowManual(false)
      setSaved(
        next.webhookSubscribed
          ? `Ulandi: @${next.username}.`
          : `Ulandi: @${next.username}, LEKIN webhook obunasi bo'lmadi — izoh/DM kelmaydi.`,
      )
    } catch (e) {
      setError(apiErrorMessage(e, "Token bilan ulab bo'lmadi"))
    } finally {
      setBusy('')
    }
  }

  const disconnect = async () => {
    if (!window.confirm("Akkaunt uziladi va jonli javoblar to'xtaydi. Davom etamizmi?")) return
    setBusy('disconnect')
    setError('')
    try {
      await disconnectIg()
      setStatus(await getIgStatus())
      setSaved('Akkaunt uzildi.')
    } catch (e) {
      setError(apiErrorMessage(e, "Uzib bo'lmadi"))
    } finally {
      setBusy('')
    }
  }

  const refresh = async () => {
    setBusy('refresh')
    setError('')
    try {
      setStatus(await refreshIgToken())
      setSaved('Token yangilandi.')
    } catch (e) {
      setError(apiErrorMessage(e, "Tokenni yangilab bo'lmadi"))
    } finally {
      setBusy('')
    }
  }

  if (loading) {
    return (
      <MarketingPage title="Sozlamalar" sub="Instagram akkaunt, AI va avtojavob bayroqlari">
        <MkLoading />
      </MarketingPage>
    )
  }

  if (!form || !status) {
    return (
      <MarketingPage title="Sozlamalar" sub="Instagram akkaunt, AI va avtojavob bayroqlari">
        <MkError text={error || "Ma'lumot yuklanmadi"} onRetry={load} />
      </MarketingPage>
    )
  }

  return (
    <MarketingPage
      title="Sozlamalar"
      sub="Instagram akkaunt, AI va avtojavob bayroqlari"
      actions={canEdit && (
        <button className="btn btn-primary" onClick={save} disabled={saving}>
          <Icon name="check" /> {saving ? 'Saqlanmoqda…' : 'Saqlash'}
        </button>
      )}
      subnav={(
        <>
          {/* Tugmalar MARSHRUTGA emas, HOLATGA bog'langan — shuning uchun `MkSubnav` emas,
              lekin ko'rinish bir xil bo'lsin deb AYNI o'sha klasslar ishlatiladi.
              Sarlavha bloki yopishqoq, ya'ni uzun tabda ham qaysi bo'limdaligi ko'rinib
              turadi va «Saqlash» tugmasi doim qo'l ostida bo'ladi.

              ⚠️ `aria-current="page"` ATAYIN OLIB TASHLANDI: `page` qiymati HAVOLA uchun
              ("ochiq sahifa"), bu yerda esa marshrut o'zgarmaydi — bitta sahifa ichida
              qolamiz. To'g'ri semantika — `tablist`/`tab` + `aria-selected`. */}
          <nav className="mk-subnav" role="tablist" aria-label="Sozlamalar bo'limlari">
            {TABS.map((t) => (
              <button
                key={t.key}
                type="button"
                role="tab"
                aria-selected={tab === t.key}
                className={'mk-subnav-item' + (tab === t.key ? ' active' : '')}
                onClick={() => goTab(t.key)}
              >
                <Icon name={t.icon} />
                <span>{t.label}</span>
              </button>
            ))}
          </nav>

          {/* 🔴 SAQLASH NATIJASI — AYNAN SHU YERDA, tab qatori ostida.
              «Saqlash» tugmasi yopishqoq sarlavhada, ya'ni doim ko'rinadi. Xabar esa
              ilgari kontentning eng tepasida, oddiy oqimda turardi: uzun tabda («Akkaunt
              va ulanish», «Reklama») pastga tushib tugmani bosgan odam na «Sozlamalar
              saqlandi» ni, na XATO matnini ko'rardi — "hech narsa bo'lmadi" deb qayta-qayta
              bosardi. Endi xabar tugma bilan BIR blokda va u ham yopishqoq. */}
          {error && (
            <div className="mk-notice tone-danger" style={{ marginBottom: 0 }} role="alert">
              <Icon name="warn" style={{ width: 17, height: 17, flexShrink: 0 }} />
              <span style={{ flex: 1, minWidth: 0 }}>{error}</span>
              <button className="mk-notice-x" onClick={() => setError('')} title="Yopish" aria-label="Yopish">
                <Icon name="close" style={{ width: 15, height: 15 }} />
              </button>
            </div>
          )}
          {saved && !error && (
            <div className="mk-notice tone-success" style={{ marginBottom: 0 }} role="status">
              <Icon name="check" style={{ width: 17, height: 17, flexShrink: 0 }} />
              <span style={{ flex: 1, minWidth: 0 }}>{saved}</span>
              <button className="mk-notice-x" onClick={() => setSaved('')} title="Yopish" aria-label="Yopish">
                <Icon name="close" style={{ width: 15, height: 15 }} />
              </button>
            </div>
          )}
        </>
      )}
    >
      <div className="fade-up">
        {justConnected && (
          <div className="mk-alert" style={{ borderColor: 'var(--success)', background: 'var(--success-soft)', color: '#0d6b4b' }}>
            <Icon name="check" style={{ width: 20, height: 20, flexShrink: 0 }} />
            <div style={{ flex: 1 }}>
              <div className="mk-alert-title">Instagram akkaunt ulandi</div>
              <div>Endi modulni yoqing va avtojavob bayroqlarini tanlang.</div>
            </div>
            <button className="btn btn-ghost btn-sm" onClick={() => { params.delete('connected'); setParams(params, { replace: true }) }}>
              <Icon name="close" /> Yopish
            </button>
          </div>
        )}

        <TabPanel active={tab === 'akkaunt'}>
          {/* ── ULANISHNI TEKSHIRISH ──
              ATAYIN BIRINCHI TABNING eng TEPASIDA: admin sozlamani saqlagach birinchi savoli
              "ishladimi?" bo'ladi, javob esa oxirida turgan bo'lsa topilmasdi. Shu sababdan
              tabning nomi ham «Akkaunt va ulanish» — tekshiruvni qidirib yurish kerak emas.

              ⚠️ TO'LIQ QATOR: natija qatorlari uzun matnli (xabar + maslahat) — yarim
              ustunga siqilsa har qator uch-to'rt satrga bo'linib ketardi. */}
          <div style={{ gridColumn: '1 / -1' }}>
            <DiagnosticsBlock canEdit={canEdit} />
          </div>

          {/* ── AKKAUNT ──
              `id` — «Kontent joylash» kartasidagi «akkauntni qayta ulang» havolasi shu yerga
              olib keladi (yangi ruxsat FAQAT qayta ulashda so'raladi). */}
          <div className="card card-pad" id="ig-account-card">
            <div className="section-head">
              <div className="section-title">Instagram akkaunt</div>
              {status.connected && canEdit && (
                <div style={{ display: 'flex', gap: 8 }}>
                  <button className="btn btn-outline btn-sm" onClick={refresh} disabled={busy === 'refresh'}>
                    <Icon name="refresh" /> Tokenni yangilash
                  </button>
                  <button className="btn btn-outline btn-sm" style={{ color: 'var(--danger)' }} onClick={disconnect} disabled={busy === 'disconnect'}>
                    <Icon name="unlink" /> Uzish
                  </button>
                </div>
              )}
            </div>

            {status.connected ? (
              <div style={{ display: 'flex', alignItems: 'center', gap: 14 }}>
                <div className="ch-icon ch-instagram" style={{ width: 46, height: 46, borderRadius: 13 }}>
                  <ChannelIcon />
                </div>
                <div style={{ flex: 1 }}>
                  <div style={{ fontWeight: 800, fontSize: 16 }}>@{status.username || '—'}</div>
                  <div className="page-sub">{status.name}</div>
                </div>
                <span className="badge badge-success"><span className="badge-dot" /> Ulangan</span>
              </div>
            ) : (
              <div style={{ display: 'flex', alignItems: 'center', gap: 14 }}>
                <div style={{ flex: 1 }}>
                  <div style={{ fontWeight: 700 }}>Akkaunt ulanmagan</div>
                  <div className="field-hint">
                    Instagram professional (Business yoki Creator) akkauntini ulang — izoh va DM'lar
                    shundan keyin keladi.
                  </div>
                </div>
                {canEdit && (
                  <button className="btn btn-primary" onClick={connect} disabled={busy === 'connect'}>
                    <Icon name="link" /> Instagram akkauntni ulash
                  </button>
                )}
              </div>
            )}

            {/* ── TOKEN BILAN QO'LDA ULASH (zaxira yo'l) ──
                ⚠️ ATAYIN YOPIQ va ATAYIN ikkinchi darajali: asosiy yo'l — yuqoridagi «Ulash»
                tugmasi, chunki OAuth tokenni O'ZI oladi va u 45-kunda avtomatik yangilanadi.
                Bu panel OAuth yurmagan holatlar uchun: `Invalid redirect_uri`, domen hali
                ochilmagan, yoki token Meta konsolidagi «Generate token» dan olingan. */}
            {canEdit && (
              <div style={{ marginTop: 14, borderTop: '1px solid var(--border)', paddingTop: 12 }}>
                {!showManual ? (
                  <button className="btn btn-ghost btn-sm" onClick={() => setShowManual(true)}>
                    <Icon name="link" /> Token bilan qo'lda ulash
                  </button>
                ) : (
                  <div className="field" style={{ marginBottom: 0 }}>
                    <label className="field-label">Instagram Login access token</label>
                    <input
                      className="input" type="password" autoComplete="off" spellCheck={false}
                      value={manualToken}
                      onChange={(e) => setManualToken(e.target.value)}
                      placeholder="IGAA..."
                    />
                    <div className="field-hint">
                      Meta konsoli → <b>Instagram → API setup with Instagram login</b> →
                      2-bo'lim <b>Generate token</b> → akkauntni tanlab tokenni ko'chiring.
                      Server uni saqlashdan oldin TEKSHIRADI: qaysi akkauntniki ekanini aniqlaydi,
                      60 kunlik tokenga aylantiradi va webhook obunasini qiladi. Yaroqsiz bo'lsa
                      hech narsa saqlanmaydi.
                      <br />
                      ⚠️ Bu — <b>zaxira</b> yo'l. Iloji bo'lsa «Instagram akkauntni ulash» tugmasidan
                      foydalaning: u tokenni o'zi oladi va qo'lda ko'chirish umuman kerak bo'lmaydi.
                    </div>
                    <div style={{ display: 'flex', gap: 8, marginTop: 10 }}>
                      <button
                        className="btn btn-primary btn-sm" onClick={connectToken}
                        disabled={busy === 'token' || !manualToken.trim()}
                      >
                        <Icon name="link" /> {busy === 'token' ? 'Tekshirilmoqda…' : 'Tekshirib ulash'}
                      </button>
                      <button
                        className="btn btn-ghost btn-sm"
                        onClick={() => { setShowManual(false); setManualToken('') }}
                        disabled={busy === 'token'}
                      >
                        Bekor qilish
                      </button>
                    </div>
                  </div>
                )}
              </div>
            )}
          </div>

          {/* ── META ILOVASI ──
              App ID AYNAN shu yerdan kiritiladi: usisiz «Ulash» tugmasi ishlamaydi (server
              "App ID kiritilmagan" deb qaytaradi). App Secret va Verify Token esa `.env` da —
              ular bu yerda ko'rsatilmaydi ham, so'ralmaydi ham. */}
          <div className="card card-pad">
            <div className="section-head">
              <div>
                <div className="section-title">Meta ilovasi</div>
                <div className="page-sub">developers.facebook.com → Instagram → API setup with Instagram login</div>
              </div>
            </div>
            <div className="field">
              <label className="field-label">Instagram App ID</label>
              <input
                className="input" value={form.instagramAppId} disabled={!canEdit}
                onChange={(e) => patch({ instagramAppId: e.target.value })}
                placeholder="masalan: 1234567890123456"
              />
              <div className="field-hint">
                Meta konsolida <b>Instagram → API setup with Instagram login</b> → 3-bo'limdagi
                raqam. ⚠️ Ilova tepasidagi (App settings → Basic) <b>Meta App ID</b> EMAS —
                u kiritilsa Instagram «Invalid platform app» xatosini beradi.
                Maxfiy emas — OAuth havolasida baribir ochiq ko'rinadi. Saqlanmaguncha
                «Instagram akkauntni ulash» tugmasi ishlamaydi.
              </div>
            </div>
          </div>

          {/* ── DIAGNOSTIKA ──
              ⚠️ TO'LIQ QATOR: ichida o'zining `mk-status-grid` i bor (11 ta kartochka) va
              uzun «Webhook URL» qatorlari — yarim ustunda ular o'qib bo'lmas holga kelardi. */}
          <div className="card card-pad" style={{ gridColumn: '1 / -1' }}>
            <div className="section-head">
              <div>
                <div className="section-title">Holat</div>
                <div className="page-sub">Maxfiy qiymatlar ko'rsatilmaydi — faqat sozlangani belgilanadi</div>
              </div>
              <button className="btn btn-ghost btn-sm" onClick={load}><Icon name="refresh" /> Yangilash</button>
            </div>
            <div className="mk-status-grid">
              <MkStatusCard label="Modul" ok={status.enabled} value={status.enabled ? 'Yoqilgan' : "O'chirilgan"} hint={status.enabled ? undefined : 'Jonli javob ketmaydi'} />
              <MkStatusCard label="Akkaunt" ok={status.connected} value={status.connected ? `@${status.username}` : 'Ulanmagan'} />
              <MkStatusCard
                label="Token muddati"
                ok={status.connected && status.tokenDaysLeft > 15}
                warn={status.connected && status.tokenDaysLeft > 0}
                value={status.connected ? `${status.tokenDaysLeft} kun qoldi` : '—'}
                hint={status.connected && status.tokenDaysLeft <= 15 ? 'Tez orada yangilanadi' : undefined}
              />
              <MkStatusCard label="Webhook obunasi" ok={status.webhookSubscribed} value={status.webhookSubscribed ? 'Faol' : 'Yo‘q'} />
              <MkStatusCard label="INSTAGRAM_APP_SECRET" ok={status.appSecretSet} hint=".env fayldan o'qiladi" />
              <MkStatusCard label="INSTAGRAM_VERIFY_TOKEN" ok={status.verifyTokenSet} hint=".env fayldan o'qiladi" />
              <MkStatusCard label="App ID" ok={status.appIdSet} />
              <MkStatusCard label="Gemini (AI)" ok={status.geminiConfigured} hint={status.geminiConfigured ? undefined : "AI javob bera olmaydi"} />
              <MkStatusCard
                label="Bilim bazasi"
                ok={status.knowledgeCount > 0}
                value={`${status.knowledgeCount} ta bo'lak`}
                hint={status.knowledgeCount > 0 ? undefined : "Bo'sh — AI javob bermaydi"}
              />
              <MkStatusCard
                label="Navbat"
                ok={status.failedEvents === 0}
                warn={status.failedEvents === 0 && status.pendingEvents > 0}
                value={`${status.pendingEvents} kutmoqda · ${status.failedEvents} xato`}
              />
              <MkStatusCard
                label="Bugungi javoblar"
                ok={status.todayReplies < status.dailyLimit}
                value={`${status.todayReplies} / ${status.dailyLimit}`}
                hint="Kunlik chegara"
              />
            </div>

            <div style={{ marginTop: 20 }}>
              <MkCopyRow
                label="Webhook URL"
                value={status.webhookUrl}
                hint="Meta → Webhooks → «Callback URL» maydoniga AYNAN shu manzil (…/webhook) qo'yiladi. Pastdagi OAuth manzili emas!"
              />
              <MkCopyRow
                label="OAuth callback URL"
                value={status.callbackUrl}
                hint="FAQAT «Valid OAuth Redirect URIs» ro'yxati uchun. Webhook maydoniga bu manzil YARAMAYDI."
              />
            </div>
          </div>
        </TabPanel>

        <TabPanel active={tab === 'avtojavob'}>
          {/* ── AVTOJAVOB ── */}
          <div className="card card-pad">
            <div className="section-head"><div className="section-title">Avtojavob</div></div>

            <Toggle
              name="Modul yoqilgan"
              desc="O'chirilgan bo'lsa hech qanday tashqi so'rov ketmaydi va hech kimga javob yozilmaydi."
              on={form.instagramEnabled}
              disabled={!canEdit}
              onToggle={() => patch({ instagramEnabled: !form.instagramEnabled })}
            />
            <Toggle
              name="Izohlarga javob berish"
              desc="Post ostidagi izohlarga AI qisqa javob yozadi (1-2 gap)."
              on={form.instagramAutoReplyComments}
              disabled={!canEdit}
              onToggle={() => patch({ instagramAutoReplyComments: !form.instagramAutoReplyComments })}
            />
            <Toggle
              name="DM (shaxsiy xabar) ga javob berish"
              desc="To'g'ridan-to'g'ri xabarlarga batafsil javob. Instagram qoidasi: oxirgi xabardan 24 soat ichida."
              on={form.instagramAutoReplyDm}
              disabled={!canEdit}
              onToggle={() => patch({ instagramAutoReplyDm: !form.instagramAutoReplyDm })}
            />
            <Toggle
              name="Izohga shaxsiy javob (private reply)"
              desc="Izoh qoldirgan odamga qo'shimcha ravishda DM ham yuboriladi (izohdan keyingi 7 kun ichida)."
              on={form.instagramPrivateReplyEnabled}
              disabled={!canEdit}
              onToggle={() => patch({ instagramPrivateReplyEnabled: !form.instagramPrivateReplyEnabled })}
            />
            <Toggle
              name="Telegram'ga xabar berish"
              desc="Qaynoq lid paydo bo'lsa yoki operator kerak bo'lsa adminlarga Telegram xabari ketadi."
              on={form.instagramNotifyTelegram}
              disabled={!canEdit}
              onToggle={() => patch({ instagramNotifyTelegram: !form.instagramNotifyTelegram })}
            />
          </div>

          {/* ── AI VA CHEGARALAR ── */}
          <div className="card card-pad">
            <div className="section-head"><div className="section-title">AI va chegaralar</div></div>

            <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: 16 }}>
              <div className="field">
                <label className="field-label">Gemini modeli</label>
                <input
                  className="input" value={form.instagramAiModel} disabled={!canEdit}
                  onChange={(e) => patch({ instagramAiModel: e.target.value })}
                  placeholder="bo'sh = tizim default'i"
                />
                <div className="field-hint">Bo'sh qoldirilsa loyihaning standart modeli ishlatiladi.</div>
              </div>

              <div className="field">
                <label className="field-label">Lid manbasi</label>
                <input
                  className="input" value={form.instagramLeadSource} disabled={!canEdit}
                  onChange={(e) => patch({ instagramLeadSource: e.target.value })}
                  placeholder="Instagram"
                />
                <div className="field-hint">Yaratilgan lidlarda «Manba» maydoniga shu nom yoziladi.</div>
              </div>

              <div className="field">
                <label className="field-label">Javob kechikishi (soniya)</label>
                <input
                  className="input" type="number" min={0} max={120} disabled={!canEdit}
                  value={form.instagramReplyDelaySeconds}
                  onChange={(e) => patch({ instagramReplyDelaySeconds: Number(e.target.value) || 0 })}
                />
                <div className="field-hint">Bir zumda kelgan javob robot bo'lib ko'rinadi — kichik pauza tabiiyroq.</div>
              </div>

              <div className="field">
                <label className="field-label">Kunlik javob chegarasi</label>
                <input
                  className="input" type="number" min={0} max={5000} disabled={!canEdit}
                  value={form.instagramDailyReplyLimit}
                  onChange={(e) => patch({ instagramDailyReplyLimit: Number(e.target.value) || 0 })}
                />
                <div className="field-hint">Himoya chegarasi: kutilmagan halqada akkaunt spam sifatida bloklanmasin.</div>
              </div>
            </div>

            <div className="field">
              <label className="field-label">Salomlashuv matni</label>
              <textarea
                className="textarea" value={form.instagramGreeting} disabled={!canEdit}
                onChange={(e) => patch({ instagramGreeting: e.target.value })}
                placeholder="Assalomu alaykum! Men markazning AI yordamchisiman…"
              />
              <div className="field-hint">
                Bu matnda AI ekani OSHKOR qilinishi kerak — Meta qoidasi ham, odob qoidasi ham shuni talab qiladi.
              </div>
            </div>
          </div>
        </TabPanel>

        <TabPanel active={tab === 'reklama'}>
          {/* ── REKLAMA LIDLARI ── */}
          <LeadAdsBlock
            canEdit={canEdit}
            enabled={form.instagramLeadAdsEnabled}
            source={form.instagramAdsLeadSource}
            onPatch={patch}
          />

          {/* ── REKLAMA STATISTIKASI (Ads Insights) ── */}
          <AdsStatsBlock canEdit={canEdit} enabled={form.instagramAdsStatsEnabled} onPatch={patch} />

          {/* ── CAPI: LID SIFATINI META'GA QAYTARISH ── */}
          <CapiBlock canEdit={canEdit} />
        </TabPanel>

        <TabPanel active={tab === 'kontent'}>
          {/* ── KONTENT JOYLASH ── */}
          <ContentBlock
            canEdit={canEdit}
            enabled={form.instagramPublishEnabled}
            onPatch={patch}
            onGoToAccount={() => goToCard('akkaunt', 'ig-account-card')}
          />
        </TabPanel>

        <TabPanel active={tab === 'sinov'}>
          {/* ── SINOV ── */}
          <TestBlock canEdit={canEdit} />
        </TabPanel>
      </div>
    </MarketingPage>
  )
}

/** Sozlamalardagi bitta bayroq qatori (nom + izoh + switch). */
function Toggle({
  name, desc, on, onToggle, disabled,
}: {
  name: string
  desc: string
  on: boolean
  onToggle: () => void
  disabled?: boolean
}) {
  return (
    <div className="row-between">
      <div>
        <div className="opt-name">{name}</div>
        <div className="opt-desc">{desc}</div>
      </div>
      <div
        className={'switch ' + (on ? 'on' : '')}
        style={disabled ? { opacity: .5, cursor: 'not-allowed' } : undefined}
        onClick={() => { if (!disabled) onToggle() }}
      />
    </div>
  )
}

/**
 * REKLAMA LIDLARI (Meta Lead Ads) — Instagram/Facebook reklamasidagi FORMA to'ldirilganda lid
 * CRM'ga avtomatik tushadi.
 *
 * ⚠️ Bu izoh/DM'dan BOSHQA yo'l: lid Facebook Page obyektining `leadgen` webhook'i orqali keladi
 * va Page Access Token talab qiladi. Shu sabab bayroq ham, token ham, webhook manzili ham
 * yuqoridagi Instagram sozlamalaridan AYRI.
 *
 * ⚠️ Token EKRANDA KO'RSATILMAYDI — faqat "sozlangan/sozlanmagan". Maydon bo'sh yuborilsa
 * serverda mavjud token saqlanadi (Page ID'ni tahrirlash uchun tokenni qayta yozish shart emas).
 */
function LeadAdsBlock({
  canEdit, enabled, source, onPatch,
}: {
  canEdit: boolean
  enabled: boolean
  source: string
  onPatch: (p: Partial<IgSettings>) => void
}) {
  const [status, setStatus] = useState<IgAdStatus | null>(null)
  const [pageId, setPageId] = useState('')
  const [token, setToken] = useState('')
  const [busy, setBusy] = useState('')
  const [error, setError] = useState('')
  const [done, setDone] = useState('')

  const load = useCallback(() => {
    getIgAdStatus()
      .then((st) => { setStatus(st); setPageId(st.pageId) })
      .catch((e) => setError(apiErrorMessage(e, "Reklama lidlari holatini yuklab bo'lmadi")))
  }, [])

  useEffect(load, [load])

  const connect = async () => {
    setBusy('save')
    setError('')
    setDone('')
    try {
      const st = await saveIgAdPage(pageId.trim(), token.trim())
      setStatus(st)
      setPageId(st.pageId)
      setToken('')   // token ekranda saqlanib qolmasin
      setDone(st.leadgenSubscribed
        ? 'Sahifa ulandi va obuna qilindi.'
        : 'Sahifa saqlandi, lekin obuna qilinmadi — pastdagi xatoni ko\'ring.')
    } catch (e) {
      setError(apiErrorMessage(e, "Sahifani ulab bo'lmadi"))
    } finally {
      setBusy('')
    }
  }

  const disconnect = async () => {
    if (!window.confirm("Sahifa uziladi va yangi reklama lidlari kelmaydi. Davom etamizmi?")) return
    setBusy('disconnect')
    setError('')
    setDone('')
    try {
      setStatus(await disconnectIgAdPage())
      setToken('')
      setDone('Sahifa uzildi.')
    } catch (e) {
      setError(apiErrorMessage(e, "Uzib bo'lmadi"))
    } finally {
      setBusy('')
    }
  }

  return (
    <div className="card card-pad">
      <div className="section-head">
        <div>
          <div className="section-title">Reklama lidlari (Lead Ads)</div>
          <div className="page-sub">
            Target reklamadagi forma to'ldirilsa — F.I.Sh. va telefon CRM lidiga avtomatik tushadi
          </div>
        </div>
        {status?.pageConnected && canEdit && (
          <button
            className="btn btn-outline btn-sm" style={{ color: 'var(--danger)' }}
            onClick={disconnect} disabled={busy === 'disconnect'}
          >
            <Icon name="unlink" /> Uzish
          </button>
        )}
      </div>

      <Toggle
        name="Reklama lidlari yoqilgan"
        desc="O'chirilgan bo'lsa webhook qabul qilinadi, lekin Meta'ga so'rov ketmaydi va lid yaratilmaydi."
        on={enabled}
        disabled={!canEdit}
        onToggle={() => onPatch({ instagramLeadAdsEnabled: !enabled })}
      />

      {error && <div style={{ marginTop: 12 }}><MkError text={error} /></div>}
      {done && !error && (
        <div className="mk-alert" style={{ borderColor: 'var(--success)', background: 'var(--success-soft)', color: '#0d6b4b' }}>
          <Icon name="check" style={{ width: 18, height: 18, flexShrink: 0 }} />
          <div style={{ flex: 1 }}>{done}</div>
        </div>
      )}

      {status && (
        <div className="mk-status-grid" style={{ marginTop: 16 }}>
          <MkStatusCard label="Modul" ok={status.enabled} value={status.enabled ? 'Yoqilgan' : "O'chirilgan"} hint={status.enabled ? undefined : 'Lid yaratilmaydi'} />
          <MkStatusCard label="Facebook sahifa" ok={status.pageConnected} value={status.pageConnected ? status.pageName || status.pageId : 'Ulanmagan'} />
          <MkStatusCard label="Page Access Token" ok={status.tokenSet} hint="Qiymat ko'rsatilmaydi" />
          <MkStatusCard
            label="Leadgen obunasi"
            ok={status.leadgenSubscribed}
            value={status.leadgenSubscribed ? 'Faol' : "Yo‘q"}
            hint={status.leadgenSubscribed ? undefined : 'Obunasiz Meta hodisa YUBORMAYDI'}
          />
          <MkStatusCard label={status.envKeyAppSecret} ok={status.appSecretSet} hint=".env fayldan o'qiladi" />
          <MkStatusCard label={status.envKeyVerifyToken} ok={status.verifyTokenSet} hint=".env fayldan o'qiladi" />
          <MkStatusCard
            label="Kelgan lidlar"
            ok={status.leadsTotal > 0}
            warn={status.leadsTotal === 0}
            value={`${status.leadsToday} bugun · ${status.leads30Days} (30 kun)`}
            hint={`Jami ${status.leadsTotal} ta`}
          />
          <MkStatusCard
            label="Xato bilan qolgan"
            ok={status.leadsFailed === 0}
            value={`${status.leadsFailed} ta`}
            hint={status.leadsFailed > 0 ? "«Reklama lidlari» sahifasida qayta olish mumkin" : undefined}
          />
        </div>
      )}

      {status?.lastError && (
        <div style={{ marginTop: 14 }}>
          <MkError text={'Oxirgi xato: ' + status.lastError} />
        </div>
      )}

      {/* 🔴 QAYSI TUGMA NIMANI SAQLAYDI — F29.
          Sahifa tepasida DOIM ko'rinib turgan yirik ko'k «Saqlash» bor, bu maydonlar esa
          `IgSettings` da EMAS: ular O'Z endpointida (`ads/page`) va saqlashdan OLDIN
          Meta'da tekshiriladi. Izohsiz qoldirilsa admin Page ID'ni kiritib TEPADAGI
          tugmani bosardi va qiymat jimgina yo'qolardi. */}
      <div className="mk-alert" style={{ marginTop: 18, marginBottom: 0 }}>
        <Icon name="warn" style={{ width: 18, height: 18, flexShrink: 0 }} />
        <div style={{ flex: 1 }}>
          <div className="mk-alert-title">Bu ikki maydon O'Z «Saqlash» tugmasi bilan saqlanadi</div>
          <div>
            <b>Facebook Page ID</b> va <b>Page Access Token</b> quyidagi <b>«Sahifani ulash va
            tekshirish»</b> tugmasi bilan saqlanadi — sahifa tepasidagi umumiy «Saqlash»
            tugmasi bu maydonlarga <b>TEGMAYDI</b>. Yuqoridagi bayroq va pastdagi «Lid
            manbasi» esa aksincha: ular tepadagi umumiy «Saqlash» bilan saqlanadi.
          </div>
        </div>
      </div>

      <div style={{ marginTop: 18, display: 'grid', gridTemplateColumns: '1fr 1fr', gap: 16 }}>
        <div className="field">
          <label className="field-label">Facebook Page ID</label>
          <input
            className="input" value={pageId} disabled={!canEdit}
            onChange={(e) => setPageId(e.target.value)}
            placeholder="masalan: 102938475610293"
          />
          <div className="field-hint">
            Instagram akkaunt BOG'LANGAN Facebook sahifasining ID'si. Reklama lidi aynan shu
            sahifaga tushadi.
          </div>
        </div>

        <div className="field">
          <label className="field-label">Page Access Token</label>
          <input
            className="input" type="password" value={token} disabled={!canEdit}
            onChange={(e) => setToken(e.target.value)}
            placeholder={status?.tokenSet ? 'sozlangan — o‘zgartirish uchun yangisini kiriting' : 'EAAG…'}
            autoComplete="new-password"
          />
          <div className="field-hint">
            <b>System User</b> tokeni tavsiya etiladi — u <b>muddatsiz</b>. `leads_retrieval`
            ruxsati bo'lishi shart. Bo'sh qoldirilsa mavjud token o'zgarmaydi.
          </div>
        </div>
      </div>

      {canEdit && (
        <button className="btn btn-primary" onClick={connect} disabled={busy === 'save' || !pageId.trim()}>
          <Icon name="link" /> {busy === 'save' ? 'Tekshirilmoqda…' : 'Sahifani ulash va tekshirish'}
        </button>
      )}

      <div className="field" style={{ marginTop: 18 }}>
        <label className="field-label">Lid manbasi (reklama)</label>
        <input
          className="input" value={source} disabled={!canEdit}
          onChange={(e) => onPatch({ instagramAdsLeadSource: e.target.value })}
          placeholder="Instagram reklama"
        />
        <div className="field-hint">
          Reklamadan kelgan lidlarda «Manba» shu nom bo'ladi. Izoh/DM lidlaridan ATAYIN
          farqli — voronkada pul to'langan reklama alohida ko'rinsin.
        </div>
      </div>

      {status && (
        <div style={{ marginTop: 6 }}>
          <MkCopyRow
            label="Reklama lidlari webhook URL"
            value={status.leadgenUrl}
            hint="Meta konsolida PAGE obyektining «Callback URL» maydoniga AYNAN shu manzil qo'yiladi (izoh/DM manzilidan boshqa) va `leadgen` maydoni belgilanadi."
          />
        </div>
      )}
    </div>
  )
}

/**
 * REKLAMA STATISTIKASI (Meta Ads Insights) — «reklamaga qancha pul ketdi va u qancha lid
 * berdi» savoliga javob beradigan modulning ULANISHI.
 *
 * 🔴 **ENG KO'P VAQT YEYDIGAN TUZOQ — TOKEN.** Yuqoridagi «Reklama lidlari» kartasidagi
 * **Page Access Token bu yerga YARAMAYDI**: unda `ads_read` ruxsati yo'q va Meta so'rovni
 * rad etadi. Token **Business Manager → System User** dan, **`ads_read`** ruxsati bilan
 * olinadi — u **MUDDATSIZ**, ya'ni bir marta kiritiladi va yangilash mexanizmi kerak emas.
 * Buni ekranda ochiq yozib qo'yilgan: aks holda admin "token noto'g'ri" xatosi bilan bir
 * necha kun ovora bo'lardi.
 *
 * ⚠️ Token HECH QACHON ko'rsatilmaydi — faqat "sozlangan/sozlanmagan". Maydon bo'sh
 * yuborilsa serverda mavjudi saqlanadi (akkaunt ID'sini tuzatish uchun tokenni qayta
 * yozish shart emas — `ads/page` bilan bir xil naqsh).
 *
 * ⚠️ Modul bayrog'i (`InstagramAdsStatsEnabled`) — SAHIFANING umumiy formasida (`IgSettings`),
 * shuning uchun u `onPatch` orqali yuqoriga uzatiladi va tepadagi «Saqlash» tugmasi bilan
 * saqlanadi. Akkaunt/token esa O'Z endpointida (`adsstats/account`) — ya'ni bu kartada
 * ikkita ayri saqlash yo'li bor va bu ATAYIN: bayroq arzon o'zgaradi, akkaunt esa
 * saqlashdan oldin Meta'da tekshiriladi.
 */
function AdsStatsBlock({
  canEdit, enabled, onPatch,
}: {
  canEdit: boolean
  enabled: boolean
  onPatch: (p: Partial<IgSettings>) => void
}) {
  const [status, setStatus] = useState<IgAdsStatsStatus | null>(null)
  const [accountId, setAccountId] = useState('')
  const [token, setToken] = useState('')
  const [busy, setBusy] = useState('')
  const [error, setError] = useState('')
  const [done, setDone] = useState('')

  const load = useCallback(() => {
    getAdsStatsStatus()
      .then((st) => { setStatus(st); setAccountId(st.adAccountId) })
      .catch((e) => setError(apiErrorMessage(e, "Reklama statistikasi holatini yuklab bo'lmadi")))
  }, [])

  useEffect(load, [load])

  const connect = async () => {
    setBusy('save'); setError(''); setDone('')
    try {
      const st = await saveAdsStatsAccount(accountId.trim(), token.trim())
      setStatus(st)
      setAccountId(st.adAccountId)
      setToken('')   // token ekranda saqlanib qolmasin
      setDone(`Akkaunt ulandi: ${st.name || st.adAccountId}. Endi «Hoziroq sinxronlash» bilan ma'lumotni oling.`)
    } catch (e) {
      setError(apiErrorMessage(e, "Akkauntni ulab bo'lmadi"))
    } finally {
      setBusy('')
    }
  }

  const disconnect = async () => {
    if (!window.confirm("Akkaunt uziladi va statistika yangilanmaydi (yig'ilgani saqlanib qoladi). Davom etamizmi?")) return
    setBusy('disconnect'); setError(''); setDone('')
    try {
      setStatus(await disconnectAdsStatsAccount())
      setToken('')
      setDone('Reklama akkaunti uzildi.')
    } catch (e) {
      setError(apiErrorMessage(e, "Uzib bo'lmadi"))
    } finally {
      setBusy('')
    }
  }

  /**
   * ⚠️ Server xatoda ham HTTP **200** qaytaradi (sinxronizatsiya QISMAN bajarilishi mumkin) —
   * shuning uchun `ok` bayrog'i qo'lda tekshiriladi, `catch` ga tayanib bo'lmaydi.
   */
  const sync = async () => {
    setBusy('sync'); setError(''); setDone('')
    try {
      const res = await syncAdsStatsNow()
      setStatus(res.status)
      if (res.ok) setDone(`Sinxronizatsiya tugadi — ${res.rows} ta qator yangilandi.`)
      else setError(res.error || 'Sinxronizatsiya bajarilmadi.')
    } catch (e) {
      setError(apiErrorMessage(e, "Sinxronizatsiya bajarilmadi"))
    } finally {
      setBusy('')
    }
  }

  return (
    <div className="card card-pad">
      <div className="section-head">
        <div>
          <div className="section-title">Reklama statistikasi (Ads Insights)</div>
          <div className="page-sub">
            Meta reklama akkauntidan sarf, ko'rsatish va lid raqamlari — «qaysi reklama pulni
            qaytardi» hisoboti shundan quriladi
          </div>
        </div>
        {status?.connected && canEdit && (
          <button
            className="btn btn-outline btn-sm" style={{ color: 'var(--danger)' }}
            onClick={disconnect} disabled={busy === 'disconnect'}
          >
            <Icon name="unlink" /> Uzish
          </button>
        )}
      </div>

      <Toggle
        name="Reklama statistikasi yoqilgan"
        desc="O'chirilgan bo'lsa Meta'ga hech qanday so'rov ketmaydi va statistika yangilanmaydi."
        on={enabled}
        disabled={!canEdit}
        onToggle={() => onPatch({ instagramAdsStatsEnabled: !enabled })}
      />

      {error && <div style={{ marginTop: 12 }}><MkError text={error} /></div>}
      {done && !error && (
        <div className="mk-alert" style={{ borderColor: 'var(--success)', background: 'var(--success-soft)', color: '#0d6b4b' }}>
          <Icon name="check" style={{ width: 18, height: 18, flexShrink: 0 }} />
          <div style={{ flex: 1 }}>{done}</div>
        </div>
      )}

      {status && (
        <div className="mk-status-grid" style={{ marginTop: 16 }}>
          <MkStatusCard
            label="Modul"
            ok={status.enabled}
            value={status.enabled ? 'Yoqilgan' : "O'chirilgan"}
            hint={status.enabled === enabled
              ? (status.enabled ? undefined : "O'chiq bo'lsa avtomatik sinxronizatsiya ishlamaydi")
              : 'Saqlanmagan — tepadagi «Saqlash» tugmasini bosing'}
          />
          <MkStatusCard
            label="Reklama akkaunti"
            ok={status.connected}
            value={status.connected ? (status.name || status.adAccountId) : 'Ulanmagan'}
            hint={status.connected ? status.adAccountId : undefined}
          />
          <MkStatusCard label="Access Token" ok={status.tokenSet} hint="Qiymat ko'rsatilmaydi" />
          <MkStatusCard
            label="Valyuta va vaqt zonasi"
            ok={status.connected && !!status.currency}
            value={status.connected ? `${status.currency || '—'} · ${status.timezoneName || '—'}` : '—'}
            hint="Statistika kunlari AYNAN reklama akkauntining zonasida kesiladi"
          />
          {/* ⚠️ Kasr xonalari — noto'g'ri bo'lsa butun pul hisobi 100 BAROBAR xato bo'ladi.
              Meta hujjatlari `currency_offset` maydoni bor-yo'qligida zid, shuning uchun kod
              uni ish vaqtida aniqlaydi. Admin qaysi yo'l ishlaganini ko'rib tursin. */}
          <MkStatusCard
            label="Pul kasr xonalari"
            ok={status.connected}
            value={status.connected ? `${status.currencyOffset} xona` : '—'}
            hint={
              status.currencyOffsetSource === 'meta'
                ? "Meta bergan qiymat — eng ishonchlisi"
                : status.connected
                  ? "Valyuta ro'yxatimizdan hisoblangan (Meta bu maydonni bermadi)"
                  : undefined
            }
          />
          <MkStatusCard
            label="Oxirgi sinxronizatsiya"
            ok={!!status.lastSyncAt}
            warn={!status.lastSyncAt && status.connected}
            value={status.lastSyncAt ? formatDateTime(status.lastSyncAt) : 'Hali bo‘lmagan'}
            hint={status.lastStatDate ? `Statistika ${status.lastStatDate} gacha` : undefined}
          />
          <MkStatusCard
            label="Yuklangan qatorlar"
            ok={status.insightRows > 0}
            warn={status.insightRows === 0 && status.connected}
            value={`${status.insightRows} ta kunlik qator`}
            hint={`Reklama obyektlari: ${status.entityRows} ta`}
          />
          <MkStatusCard
            label="Avtomatik yangilash"
            ok={status.enabled}
            value={`Har kuni soat ${status.syncHour}:00`}
            hint={`Birinchi yuklashda ${status.backfillDays} kunlik tarix olinadi`}
          />
          <MkStatusCard
            label="Oxirgi xato"
            ok={!status.lastError}
            value={status.lastError ? 'Bor' : "Yo‘q"}
            hint={status.lastError ? 'Matni pastda' : undefined}
          />
        </div>
      )}

      {status?.lastError && (
        <div style={{ marginTop: 14 }}>
          <MkError text={'Oxirgi xato: ' + status.lastError} />
        </div>
      )}

      {/* 🔴 QAYSI TUGMA NIMANI SAQLAYDI — F29 (yuqoridagi karta bilan bir xil sabab).
          Bayroq `IgSettings` da (umumiy «Saqlash»), akkaunt va token esa O'Z endpointida
          (`adsstats/account`) — chunki ular saqlashdan OLDIN Meta'da tekshiriladi. */}
      <div className="mk-alert" style={{ marginTop: 18, marginBottom: 0 }}>
        <Icon name="warn" style={{ width: 18, height: 18, flexShrink: 0 }} />
        <div style={{ flex: 1 }}>
          <div className="mk-alert-title">Bu ikki maydon O'Z «Saqlash» tugmasi bilan saqlanadi</div>
          <div>
            <b>Reklama akkaunti ID</b> va <b>Access Token</b> quyidagi <b>«Ulash va
            tekshirish»</b> tugmasi bilan saqlanadi — sahifa tepasidagi umumiy «Saqlash»
            tugmasi bu maydonlarga <b>TEGMAYDI</b>. Yuqoridagi bayroq esa aksincha: u
            tepadagi umumiy «Saqlash» bilan saqlanadi.
          </div>
        </div>
      </div>

      <div style={{ marginTop: 18, display: 'grid', gridTemplateColumns: '1fr 1fr', gap: 16 }}>
        <div className="field">
          <label className="field-label">Reklama akkaunti ID (Ad Account ID)</label>
          <input
            className="input" value={accountId} disabled={!canEdit}
            onChange={(e) => setAccountId(e.target.value)}
            placeholder="masalan: act_1234567890"
          />
          <div className="field-hint">
            Ads Manager manzilidagi raqam. <code>act_</code> prefiksisiz (faqat raqamlar)
            kiritilsa ham bo'ladi — server uni o'zi to'ldiradi.
          </div>
        </div>

        <div className="field">
          <label className="field-label">Access Token</label>
          <input
            className="input" type="password" value={token} disabled={!canEdit}
            onChange={(e) => setToken(e.target.value)}
            placeholder={status?.tokenSet ? 'sozlangan — o‘zgartirish uchun yangisini kiriting' : 'EAAG…'}
            autoComplete="new-password"
          />
          <div className="field-hint">
            🔴 <b>Business Manager → System User</b> tokeni, <b>`ads_read`</b> ruxsati bilan —
            u <b>muddatsiz</b>. ⚠️ Yuqoridagi «Reklama lidlari» kartasidagi <b>Page Access
            Token bu yerga YARAMAYDI</b> (unda `ads_read` yo'q va Meta so'rovni rad etadi).
            Bo'sh qoldirilsa mavjud token o'zgarmaydi.
          </div>
        </div>
      </div>

      {canEdit && (
        <div style={{ display: 'flex', alignItems: 'center', gap: 12, flexWrap: 'wrap' }}>
          <button className="btn btn-primary" onClick={connect} disabled={busy !== '' || !accountId.trim()}>
            <Icon name="link" /> {busy === 'save' ? 'Tekshirilmoqda…' : 'Ulash va tekshirish'}
          </button>
          <button className="btn btn-outline" onClick={sync} disabled={busy !== '' || !status?.connected}>
            <Icon name="refresh" /> {busy === 'sync' ? 'Sinxronlanmoqda…' : 'Hoziroq sinxronlash'}
          </button>
          <div className="field-hint" style={{ flex: 1, minWidth: 220 }}>
            ⚠️ Birinchi sinxronizatsiya <b>bir necha daqiqa</b> davom etishi mumkin
            ({status?.backfillDays ?? 90} kunlik tarix yuklanadi) — sahifani yopmang.
            «Ulash va tekshirish» esa token va akkauntni <b>saqlashdan OLDIN</b> Meta'da
            tekshiradi: xato bo'lsa hech narsa saqlanmaydi.
          </div>
        </div>
      )}
    </div>
  )
}

/**
 * CAPI — «lid sifatini Meta'ga QAYTARISH». Hozir Meta faqat "lid keldi"ni biladi; bu modul
 * unga "bu lid sifatli bo'ldi / pul to'ladi"ni ham aytadi va Meta reklamani **haqiqiy mijoz
 * keltiradigan** auditoriyaga optimallashtiradi.
 *
 * 🔴 **ASOSIY TUZOQ — HODISA NOMLARI.** `event_name` — ERKIN MATN va u **Events Manager'dagi
 * bosqich nomi bilan AYNAN bir xil** bo'lishi shart (harfma-harf). Mos kelmasa Meta hodisani
 * tanimaydi va u hech qayerda ko'rinmaydi — xato ham bermaydi. Shu sabab nomlar kodga
 * yozilmagan, sozlamada turibdi.
 *
 * ⚠️ Dataset ID ham, token ham QIYMAT sifatida javobga tushmaydi (maxfiylik) — forma har
 * safar bo'sh ochiladi. Shu sabab ikkalasi ham **bo'sh yuborilsa serverda mavjudi
 * saqlanadi**: faqat bayroqni yoki bosqich nomini o'zgartirish uchun ularni qayta yozish
 * shart emas.
 *
 * ⚠️ Maxfiylik: Meta'ga xom telefon KETMAYDI — faqat SHA-256 hash.
 */
function CapiBlock({ canEdit }: { canEdit: boolean }) {
  const [status, setStatus] = useState<IgCapiStatus | null>(null)
  const [enabled, setEnabled] = useState(false)
  const [datasetId, setDatasetId] = useState('')
  const [token, setToken] = useState('')
  const [stageQualified, setStageQualified] = useState('')
  const [stageWon, setStageWon] = useState('')
  const [busy, setBusy] = useState('')
  const [error, setError] = useState('')
  const [done, setDone] = useState('')

  /** Serverdan kelgan holatni formaga yoyadi. ⚠️ Dataset ID va token QAYTMAYDI (bayroq bor, qiymat yo'q). */
  const apply = (st: IgCapiStatus) => {
    setStatus(st)
    setEnabled(st.enabled)
    setStageQualified(st.stageQualified)
    setStageWon(st.stageWon)
  }

  const load = useCallback(() => {
    getCapiStatus()
      .then(apply)
      .catch((e) => setError(apiErrorMessage(e, "CAPI holatini yuklab bo'lmadi")))
  }, [])

  useEffect(load, [load])

  const save = async () => {
    // Dataset ID ham, token ham BO'SH yuborilsa serverda mavjudi saqlanadi — ya'ni faqat
    // bayroq yoki bosqich nomini o'zgartirish uchun ularni qayta yozish shart emas.
    setBusy('save'); setError(''); setDone('')
    try {
      apply(await saveCapiSettings({
        enabled,
        datasetId: datasetId.trim(),
        token: token.trim(),
        stageQualified: stageQualified.trim(),
        stageWon: stageWon.trim(),
      }))
      setToken('')       // token ekranda saqlanib qolmasin
      setDatasetId('')   // qiymat qaytmaydi — maydonni "kiritilmagan" holatida qoldiramiz
      setDone('CAPI sozlamalari saqlandi.')
    } catch (e) {
      setError(apiErrorMessage(e, "Saqlab bo'lmadi"))
    } finally {
      setBusy('')
    }
  }

  /** ⚠️ Xatoda ham HTTP 200 keladi — `ok` bayrog'i qo'lda tekshiriladi. */
  const send = async () => {
    setBusy('send'); setError(''); setDone('')
    try {
      const res = await sendCapiNow()
      if (res.ok) setDone(`Yuborildi — yangi hodisa: ${res.created}, jo'natilgan: ${res.sent}.`)
      else setError(res.error || 'Hodisalarni yuborib bo‘lmadi.')
      setStatus(await getCapiStatus())
    } catch (e) {
      setError(apiErrorMessage(e, "Yuborib bo'lmadi"))
    } finally {
      setBusy('')
    }
  }

  return (
    <div className="card card-pad">
      <div className="section-head">
        <div>
          <div className="section-title">Lid sifatini Meta'ga qaytarish (CAPI)</div>
          <div className="page-sub">
            «Bu lid sifatli bo'ldi / pul to'ladi» hodisasi Meta'ga qaytariladi — reklama
            haqiqiy mijoz keltiradigan auditoriyaga optimallashadi
          </div>
        </div>
        {canEdit && (
          <button className="btn btn-outline btn-sm" onClick={send} disabled={busy !== ''}>
            <Icon name="send" /> {busy === 'send' ? 'Yuborilmoqda…' : 'Hoziroq yuborish'}
          </button>
        )}
      </div>

      {/* 🔴 QAYSI TUGMA NIMANI SAQLAYDI — F29.
          Bu kartaning HECH BIR maydoni `IgSettings` da emas: bayroq ham, Dataset ID ham,
          token ham, hodisa nomlari ham — hammasi `capi/settings` endpointida va
          kartaning O'Z tugmasi bilan saqlanadi. */}
      <div className="mk-alert" style={{ marginBottom: 16 }}>
        <Icon name="warn" style={{ width: 18, height: 18, flexShrink: 0 }} />
        <div style={{ flex: 1 }}>
          <div className="mk-alert-title">Bu karta butunlay O'Z «Saqlash» tugmasi bilan saqlanadi</div>
          <div>
            Quyidagi bayroq, <b>Dataset ID</b>, <b>Access Token</b> va ikkala <b>hodisa
            nomi</b> kartaning eng ostidagi <b>«CAPI sozlamalarini saqlash»</b> tugmasi bilan
            saqlanadi. Sahifa tepasidagi umumiy «Saqlash» tugmasi bu maydonlarga
            <b> TEGMAYDI</b> — kiritilgan qiymat u bilan saqlanmaydi.
          </div>
        </div>
      </div>

      <Toggle
        name="CAPI yoqilgan"
        desc="O'chirilgan bo'lsa navbat to'ldirilmaydi va Meta'ga hech qanday so'rov ketmaydi."
        on={enabled}
        disabled={!canEdit}
        onToggle={() => setEnabled((v) => !v)}
      />

      {error && <div style={{ marginTop: 12 }}><MkError text={error} /></div>}
      {done && !error && (
        <div className="mk-alert" style={{ borderColor: 'var(--success)', background: 'var(--success-soft)', color: '#0d6b4b' }}>
          <Icon name="check" style={{ width: 18, height: 18, flexShrink: 0 }} />
          <div style={{ flex: 1 }}>{done}</div>
        </div>
      )}

      {status && (
        <div className="mk-status-grid" style={{ marginTop: 16 }}>
          <MkStatusCard label="Modul" ok={status.enabled} value={status.enabled ? 'Yoqilgan' : "O'chirilgan"} />
          <MkStatusCard label="Dataset ID" ok={status.datasetIdSet} hint="Qiymat ko'rsatilmaydi" />
          <MkStatusCard label="Access Token" ok={status.tokenSet} hint="Qiymat ko'rsatilmaydi" />
          <MkStatusCard
            label="Navbatda"
            ok={status.pending === 0}
            warn={status.pending > 0}
            value={`${status.pending} ta kutmoqda`}
            hint="Kuniga bir marta worker o'zi yuboradi"
          />
          <MkStatusCard
            label="Yuborilgan"
            ok={status.sent > 0}
            warn={status.sent === 0}
            value={`${status.sent} ta`}
            hint={status.lastSentAt ? `Oxirgisi: ${formatDateTime(status.lastSentAt)}` : 'Hali yuborilmagan'}
          />
          <MkStatusCard
            label="Xato bilan qolgan"
            ok={status.failed === 0}
            value={`${status.failed} ta`}
            hint={status.failed > 0 ? 'Sabab pastda' : undefined}
          />
          <MkStatusCard
            label="O'tkazib yuborilgan"
            ok
            value={`${status.skipped} ta`}
            hint="Masalan: lid Meta'niki emas yoki hodisa 7 kundan eski"
          />
          <MkStatusCard
            label="Oxirgi xato"
            ok={!status.lastError}
            value={status.lastError ? 'Bor' : "Yo‘q"}
            hint={status.lastError ? 'Matni pastda' : undefined}
          />
        </div>
      )}

      {status?.lastError && (
        <div style={{ marginTop: 14 }}>
          <MkError text={'Oxirgi xato: ' + status.lastError} />
        </div>
      )}

      <div style={{ marginTop: 18, display: 'grid', gridTemplateColumns: '1fr 1fr', gap: 16 }}>
        <div className="field">
          <label className="field-label">Dataset ID</label>
          <input
            className="input" value={datasetId} disabled={!canEdit}
            onChange={(e) => setDatasetId(e.target.value)}
            placeholder={status?.datasetIdSet ? 'sozlangan — o‘zgartirish uchun qaytadan kiriting' : 'Events Manager → Dataset ID'}
          />
          <div className="field-hint">
            Events Manager'dagi dataset (piksel) ID'si. Maxfiylik uchun qiymati ekranga
            qaytarilmaydi — <b>bo'sh qoldirilsa mavjudi o'zgarmaydi</b>.
          </div>
        </div>

        <div className="field">
          <label className="field-label">Access Token</label>
          <input
            className="input" type="password" value={token} disabled={!canEdit}
            onChange={(e) => setToken(e.target.value)}
            placeholder={status?.tokenSet ? 'sozlangan — o‘zgartirish uchun yangisini kiriting' : 'EAAG…'}
            autoComplete="new-password"
          />
          <div className="field-hint">
            Events Manager → Dataset → «Generate access token». Bo'sh qoldirilsa mavjud token
            o'zgarmaydi. ⚠️ Bu <b>dataset</b> tokeni — Page tokeni ham, System User tokeni ham emas.
          </div>
        </div>

        <div className="field">
          <label className="field-label">Hodisa nomi — «Sifatli lid»</label>
          <input
            className="input" value={stageQualified} disabled={!canEdit}
            onChange={(e) => setStageQualified(e.target.value)}
            placeholder="Sifatli lid"
          />
          <div className="field-hint">
            Lid sifatli bosqichga o'tganda (yoki o'quvchiga aylanganda) yuboriladi.
          </div>
        </div>

        <div className="field">
          <label className="field-label">Hodisa nomi — «To'lov qildi»</label>
          <input
            className="input" value={stageWon} disabled={!canEdit}
            onChange={(e) => setStageWon(e.target.value)}
            placeholder="To'lov qildi"
          />
          <div className="field-hint">
            Birinchi o'quv to'lovi tushganda yuboriladi (summa va valyuta bilan).
          </div>
        </div>
      </div>

      {/* 🔴 Nomlar mos kelmasa Meta hodisani JIMGINA tanimaydi — bu eng qimmat xato. */}
      <div className="mk-alert" style={{ borderColor: 'var(--warning)', background: 'var(--warning-soft)' }}>
        <Icon name="warn" style={{ width: 18, height: 18, flexShrink: 0 }} />
        <div style={{ flex: 1 }}>
          <div className="mk-alert-title">Hodisa nomlari harfma-harf mos bo'lishi SHART</div>
          <div>
            Bu nomlar <b>Events Manager'da sozlangan bosqich nomlari bilan AYNAN bir xil</b>
            {' '}bo'lishi kerak (katta-kichik harf, bo'sh joy, apostrof — hammasi). Mos kelmasa
            Meta hodisani tanimaydi va u hisobotlarda umuman ko'rinmaydi — xato ham bermaydi.
          </div>
        </div>
      </div>

      {/* Meta'ning «Conversion Leads» talablari — admin buni oldindan bilsin. */}
      <div className="field" style={{ marginTop: 6 }}>
        <label className="field-label">Meta talablari (Conversion Leads optimizatsiyasi uchun)</label>
        <ul className="field-hint" style={{ paddingLeft: 18, margin: 0, lineHeight: 1.7 }}>
          <li>Oyiga kamida <b>200 ta lid</b> (Instant Form orqali kelgan).</li>
          <li>Lidning maqsadli bosqichga o'tishi <b>28 kun ichida</b> sodir bo'lishi.</li>
          <li>Konversiya darajasi <b>1% – 40%</b> oralig'ida bo'lishi.</li>
          <li>Ma'lumot kuniga kamida bir marta yuborilishi (worker buni o'zi bajaradi).</li>
        </ul>
        <div className="field-hint" style={{ marginTop: 6 }}>
          ⚠️ Talablar bajarilmasa Meta «Conversion Leads» optimizatsiyasini <b>yoqmaydi</b>,
          lekin hodisalarni yuborish <b>baribir foydali</b> — atributsiya hisobotlarida
          "qaysi reklama pul keltirdi" ko'rinadi.
        </div>
        <div className="field-hint" style={{ marginTop: 6 }}>
          🔒 <b>Maxfiylik:</b> Meta'ga xom telefon raqami <b>KETMAYDI</b> — faqat uning
          <b> SHA-256 hash</b>i va Meta'ning o'z lid ID'si. Ism, izoh va suhbat matni umuman
          yuborilmaydi.
        </div>
      </div>

      {canEdit && (
        <button className="btn btn-primary" onClick={save} disabled={busy !== ''}>
          <Icon name="check" /> {busy === 'save' ? 'Saqlanmoqda…' : 'CAPI sozlamalarini saqlash'}
        </button>
      )}
    </div>
  )
}

/**
 * KONTENT JOYLASH — rejalashtirilgan postlar modulining QISQA holati (to'liq boshqaruv
 * «Kontent» sahifasida).
 *
 * 🔴 **ASOSIY TUZOQ — `scopeGranted` `null` bo'lishi mumkin.** OAuth'da berilgan ruxsatlar
 * bazada SAQLANMAYDI, ya'ni "joylash ruxsati bormi" savoliga aniq javob yo'q. Bunday holatda
 * jimgina "hammasi joyida" deb ko'rsatish xato bo'lardi: post joylash paytida ruxsat
 * yo'qligi ma'lum bo'lib, sabab tushunarsiz qolardi. Shuning uchun `null` da ochiq
 * ogohlantirish chiqadi — **akkauntni QAYTA ULASH** kerak (yangi ruxsat aynan qayta ulashda
 * so'raladi).
 *
 * ⚠️ Media manzili **ochiq HTTPS** bo'lishi shart — Meta faylni O'ZI yuklab oladi va
 * login ortidagi `/uploads/...` manzilini ocholmaydi.
 */
function ContentBlock({
  canEdit, enabled, onPatch, onGoToAccount,
}: {
  canEdit: boolean
  enabled: boolean
  onPatch: (p: Partial<IgSettings>) => void
  /**
   * Akkaunt kartasi BOSHQA tabda. Tab almashtirish HAM, kartaga surish HAM sahifaning
   * o'zida (`goToCard`) — bu yerda vaqt bilan o'ynash (`setTimeout`) kerak emas va
   * MUMKIN ham emas: tab qachon chizilishini faqat sahifa biladi.
   */
  onGoToAccount?: () => void
}) {
  const [status, setStatus] = useState<IgContentStatus | null>(null)
  const [error, setError] = useState('')

  useEffect(() => {
    getContentStatus()
      .then(setStatus)
      .catch((e) => setError(apiErrorMessage(e, "Kontent moduli holatini yuklab bo'lmadi")))
  }, [])

  return (
    <div className="card card-pad">
      <div className="section-head">
        <div>
          <div className="section-title">Kontent joylash</div>
          <div className="page-sub">Rejalashtirilgan postlar — to'liq boshqaruv «Kontent» sahifasida</div>
        </div>
        <Link className="btn btn-outline btn-sm" to="/admin/marketing/kontent">
          <Icon name="arrowRight" /> Kontent sahifasi
        </Link>
      </div>

      <Toggle
        name="Kontent joylash yoqilgan"
        desc="Yoqilgani yetmaydi — akkaunt `instagram_business_content_publish` ruxsati bilan QAYTA ulangan bo'lishi kerak."
        on={enabled}
        disabled={!canEdit}
        onToggle={() => onPatch({ instagramPublishEnabled: !enabled })}
      />

      {error && <div style={{ marginTop: 12 }}><MkError text={error} /></div>}

      {status && (
        <>
          <div className="mk-status-grid" style={{ marginTop: 16 }}>
            <MkStatusCard
              label="Modul"
              ok={status.enabled}
              value={status.enabled ? 'Yoqilgan' : "O'chirilgan"}
              hint={status.enabled === enabled
                ? (status.enabled ? undefined : 'Rejalashtirilgan postlar joylanmaydi')
                : 'Saqlanmagan — tepadagi «Saqlash» tugmasini bosing'}
            />
            <MkStatusCard
              label="Instagram akkaunt"
              ok={status.accountConnected}
              value={status.accountConnected ? 'Ulangan' : 'Ulanmagan'}
            />
            <MkStatusCard
              label="Joylash ruxsati"
              /* ⚠️ `null` — "noma'lum": yashil ham, qizil ham emas (sariq). */
              ok={status.scopeGranted === true}
              warn={status.scopeGranted === null}
              value={status.scopeGranted === true ? 'Berilgan'
                : status.scopeGranted === false ? 'Berilmagan' : "Noma'lum"}
              hint={status.publishScope}
            />
            <MkStatusCard
              label="Navbat"
              ok={status.failed === 0}
              warn={status.failed === 0 && status.processing > 0}
              value={`${status.scheduled} rejada · ${status.processing} jarayonda · ${status.failed} xato`}
              hint={`Shu haftada joylangan: ${status.publishedThisWeek} ta`}
            />
          </div>

          {status.scopeGranted === null && (
            <div className="mk-alert" style={{ marginTop: 14, borderColor: 'var(--warning)', background: 'var(--warning-soft)' }}>
              <Icon name="warn" style={{ width: 18, height: 18, flexShrink: 0 }} />
              <div style={{ flex: 1 }}>
                <div className="mk-alert-title">Kontent joylash uchun yangi ruxsat kerak</div>
                <div>
                  Berilgan ruxsatlar saqlanmagani uchun <b><code>{status.publishScope}</code> bor-yo'qligini
                  aniqlab bo'lmaydi</b>. Post joylash ishlamasa — <b>Instagram akkauntini QAYTA
                  ULANG</b> (yuqoridagi «Instagram akkaunt» kartasi): yangi ruxsat aynan qayta
                  ulash paytida so'raladi.
                </div>
              </div>
              <button className="btn btn-outline btn-sm" onClick={() => onGoToAccount?.()}>
                <Icon name="link" /> Akkaunt kartasiga
              </button>
            </div>
          )}
        </>
      )}

      <div className="field-hint" style={{ marginTop: 14 }}>
        ⚠️ <b>Media manzili ochiq HTTPS bo'lishi SHART:</b> Meta rasm/videoni <b>o'zi yuklab
        oladi</b>, ya'ni login talab qiladigan manzil (odatdagi <code>/uploads/…</code>),
        IP cheklov yoki redirect <b>ishlamaydi</b> — post «xato» holatida qoladi.
      </div>
    </div>
  )
}

/**
 * SINOV: matn yoziladi va AI javobi ko'rsatiladi.
 * ⚠️ Javob mijozga JONLI YUBORILMAYDI — faqat shu ekranda ko'rinadi.
 */
function TestBlock({ canEdit }: { canEdit: boolean }) {
  const [channel, setChannel] = useState<IgChannel>('dm')
  const [message, setMessage] = useState('')
  const [result, setResult] = useState<IgTestAgentResult | null>(null)
  const [running, setRunning] = useState(false)
  const [error, setError] = useState('')

  const run = async () => {
    if (!message.trim()) return
    setRunning(true)
    setError('')
    setResult(null)
    try {
      setResult(await testIgAgent(channel, message.trim()))
    } catch (e) {
      setError(apiErrorMessage(e, 'Sinov bajarilmadi'))
    } finally {
      setRunning(false)
    }
  }

  return (
    <div className="card card-pad">
      <div className="section-head">
        <div>
          <div className="section-title">Sinov</div>
          <div className="page-sub">AI javobi faqat shu yerda ko'rinadi — mijozga yuborilmaydi</div>
        </div>
      </div>

      <div className="field">
        <label className="field-label">Kanal</label>
        <div className="seg" style={{ width: 'fit-content' }}>
          {([['dm', 'Shaxsiy xabar'], ['comment', 'Izoh']] as const).map(([k, l]) => (
            <button key={k} className={channel === k ? 'active' : ''} onClick={() => setChannel(k)}>{l}</button>
          ))}
        </div>
      </div>

      <div className="field">
        <label className="field-label">Mijoz xabari</label>
        <textarea
          className="textarea" value={message} onChange={(e) => setMessage(e.target.value)}
          placeholder="Masalan: Ingliz tili kursi qancha turadi?"
        />
      </div>

      <button className="btn btn-primary" onClick={run} disabled={!canEdit || running || !message.trim()}>
        <Icon name="play" /> {running ? 'Tekshirilmoqda…' : 'Sinab ko‘rish'}
      </button>

      {error && <div style={{ marginTop: 14 }}><MkError text={error} /></div>}

      {result && (
        <div style={{ marginTop: 16 }}>
          {result.ok ? (
            <>
              <div className="flow-step">
                <div className="flow-step-label"><Icon name="sparkle" style={{ width: 13, height: 13 }} /> AI javobi</div>
                <div className="reply-preview" style={{ whiteSpace: 'pre-line' }}>{result.reply}</div>
              </div>
              <div style={{ display: 'flex', gap: 8, flexWrap: 'wrap', marginTop: 12 }}>
                <span className="badge badge-ai">Til: {result.language || '—'}</span>
                <span className="badge badge-ai">Niyat: {result.intent || '—'}</span>
                <span className="badge badge-ai">Ball: {result.leadScore}</span>
                {result.isHotLead && <span className="badge badge-warning"><Icon name="fire" style={{ width: 11, height: 11 }} /> Qaynoq lid</span>}
                {result.escalateToHuman && <span className="badge badge-danger">Operator kerak</span>}
              </div>
            </>
          ) : (
            <MkError text={result.error || 'AI javob bermadi'} />
          )}
        </div>
      )}
    </div>
  )
}

/* ═══════════════════════════════════════════════════════════════════════════════════════
   ULANISHNI TEKSHIRISH — «Meta bilan aloqani tekshirish»

   Muammo: to'rtta modul Meta API bilan ishlaydi, lekin sozlama saqlangandan keyin
   "ishladimi yoki yo'qmi" faqat bir necha KUN kutib bilinardi (lid kelmasa, post yiqilsa,
   statistika bo'sh chiqsa). Meta tomonidagi nosozliklar bir xil ko'rinadi ("hech narsa
   kelmayapti"), sabablari esa har xil.

   Bitta tugma har yoqilgan modul uchun eng yengil o'qish so'rovini yuboradi va NIMA QILISH
   kerakligini yozadi.
   ═══════════════════════════════════════════════════════════════════════════════════════ */

/**
 * Qatorning KO'RINISHI.
 *
 * 🔴 `checked === false` bo'lganda YASHIL belgi HECH QACHON chiqmaydi — hatto `ok === true`
 * bo'lsa ham. Aynan shu holat CAPI'da bo'ladi: sozlama to'liq, lekin Meta bilan aloqa
 * sinalmagan (tekshiruv hodisa yuborsa, u Events Manager statistikasiga tushib qolardi).
 * Sinalmagan modulni "ishlayapti" deb ko'rsatish — eng yomon variant.
 */
function diagTone(it: IgDiagItem): { color: string; bg: string; icon: string; word: string } {
  if (!it.enabled) {
    return { color: 'var(--muted)', bg: 'var(--surface-2)', icon: 'close', word: "O'chirilgan" }
  }
  if (!it.checked) {
    return it.ok
      ? { color: 'var(--muted)', bg: 'var(--surface-2)', icon: 'warn', word: 'Sinalmadi' }
      : { color: 'var(--warning)', bg: 'var(--warning-soft)', icon: 'warn', word: 'Sozlanmagan' }
  }
  return it.ok
    ? { color: 'var(--success)', bg: 'var(--success-soft)', icon: 'check', word: 'Aloqa bor' }
    : { color: 'var(--danger)', bg: 'var(--danger-soft)', icon: 'warn', word: 'Nosoz' }
}

/** Bitta modul qatori: belgi · nom · holat · xabar · maslahat. */
function DiagRow({ item }: { item: IgDiagItem }) {
  const t = diagTone(item)
  return (
    <div className="mk-status" style={{ borderColor: t.color, alignItems: 'flex-start' }}>
      <div className="mk-status-dot" style={{ background: t.bg, color: t.color }}>
        <Icon name={t.icon} style={{ width: 15, height: 15 }} />
      </div>
      <div style={{ minWidth: 0, flex: 1 }}>
        <div style={{ display: 'flex', alignItems: 'center', gap: 8, flexWrap: 'wrap' }}>
          <span className="mk-status-value" style={{ fontSize: 14 }}>{item.label}</span>
          <span className="mk-status-label" style={{ color: t.color, fontWeight: 800 }}>{t.word}</span>
        </div>
        <div className="mk-status-label" style={{ marginTop: 3, fontWeight: 500, whiteSpace: 'pre-wrap' }}>
          {item.message}
        </div>
        {/* Maslahat — "nima qilish kerak". Hammasi joyida bo'lsa server bo'sh qaytaradi. */}
        {item.hint && <div className="field-hint" style={{ marginTop: 4 }}>{item.hint}</div>}
      </div>
    </div>
  )
}

/**
 * «Ulanishni tekshirish» kartasi.
 *
 * ⚠️ Natija SAQLANMAYDI — har bosishda yangisi (token muddati tugashi, ruxsatning olib
 * qo'yilishi holatni istalgan daqiqada o'zgartiradi, eski "yashil" natija esa aldardi).
 */
function DiagnosticsBlock({ canEdit }: { canEdit: boolean }) {
  const [result, setResult] = useState<IgDiagResult | null>(null)
  const [running, setRunning] = useState(false)
  const [err, setErr] = useState('')

  const run = async () => {
    setRunning(true)
    setErr('')
    try {
      setResult(await checkMetaConnection())
    } catch (e) {
      setErr(apiErrorMessage(e, "Tekshirib bo'lmadi"))
    } finally {
      setRunning(false)
    }
  }

  return (
    <div className="card card-pad">
      <div className="section-head">
        <div>
          <div className="section-title">Ulanishni tekshirish</div>
          <div className="field-hint" style={{ marginTop: 2 }}>
            Yoqilgan modullar bo'yicha Meta'ga bittadan yengil so'rov yuboriladi — hech narsa
            o'zgartirilmaydi.
          </div>
        </div>
        {canEdit && (
          <button className="btn btn-primary btn-sm" onClick={run} disabled={running}>
            <Icon name={running ? 'clock' : 'zap'} />
            {running ? 'Tekshirilmoqda…' : 'Meta bilan aloqani tekshirish'}
          </button>
        )}
      </div>

      {!canEdit && (
        <div className="field-hint">
          Tekshirish uchun «Sozlamalar» bo'limida tahrirlash ruxsati kerak.
        </div>
      )}

      {err && <div style={{ marginTop: 12 }}><MkError text={err} /></div>}

      {running && !result && (
        <div style={{ marginTop: 12 }}><MkLoading text="Meta'ga so'rov yuborilmoqda…" /></div>
      )}

      {result && (
        <div style={{ marginTop: 14 }}>
          <div className="field-hint" style={{ marginBottom: 10 }}>
            {formatDateTime(result.checkedAt)} · aloqa bor: {result.okCount} · nosoz:{' '}
            {result.failCount} · tekshirilmadi: {result.skippedCount}
          </div>
          <div style={{ display: 'grid', gap: 10 }}>
            {result.items.map((it) => <DiagRow key={it.key} item={it} />)}
          </div>
        </div>
      )}
    </div>
  )
}
