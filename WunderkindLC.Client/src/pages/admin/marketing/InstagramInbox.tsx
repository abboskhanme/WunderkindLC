import { useCallback, useEffect, useRef, useState } from 'react'
import { useSearchParams } from 'react-router-dom'
import { usePerm } from '@/lib/permissions'
import { apiErrorMessage } from '@/lib/utils'
import {
  closeIgConversation, createIgLead, getIgConversation, getIgConversations,
  markIgConversationRead, releaseIgConversation, reopenIgConversation, replyIgConversation,
  takeoverIgConversation,
  type IgConversation, type IgConversationDetail, type IgConversationStatus, type IgReplyMode,
} from '@/api/services/instagram'
import { ChannelIcon, Icon, MarketingPage, MkEmpty, MkError, MkLoading } from './mk'
import { formatPhone } from '@/lib/numberFormat'

/** Suhbat holati yorliqlari — xom kalit ekranda ko'rinmasin. */
/** Avtomatik yangilash oralig'i (ms). Instagram javobi bir zumda kelmaydi — 15 soniya yetarli,
 *  bundan tez-tez so'rash serverni ham, xodimning trafigini ham bekorga yeydi. */
const REFRESH_MS = 15_000

const STATUS_LABEL: Record<IgConversationStatus, string> = {
  bot: 'AI javob bermoqda',
  operator: 'Operator qo‘lida',
  closed: 'Yopilgan',
}

/**
 * 🔴 REKLAMA ATRIBUTSIYASI TOOLTIPI — hech qayerda "aniq" deb ko'rsatilmaydi.
 *
 * Instagram Login yo'lidagi izoh webhook'ida `ad_id` UMUMAN YO'Q; bog'lanish media id'sini
 * reklama creative'i bilan solishtirib TIKLANADI. Boostlangan postda ishlaydi, "dark post" va
 * dinamik (katalog) reklamada ishlamaydi — ya'ni belgining YO'QLIGI "organik" degani emas.
 * Shu sabab yorliqda "taxminiy" so'zi KO'RINIB turadi (faqat tooltipda emas).
 */
const AD_HINT = "Reklama izohi (taxminiy — media bo'yicha aniqlangan). "
  + "Boostlangan postda ishlaydi; \"dark post\" va dinamik reklamada aniqlanmaydi, "
  + "ya'ni belgisi yo'q suhbat ham reklamadan kelgan bo'lishi mumkin."

/** Xabar kanali yorliqlari. */
const CHANNEL_LABEL: Record<string, string> = {
  comment: 'Izoh',
  dm: 'Shaxsiy xabar',
  private_reply: 'Izohga shaxsiy javob',
}

/**
 * OPERATOR JAVOBI rejimlari. Izoh suhbatida operator izohga OCHIQ (post ostida) yoki YOPIQ
 * (private reply) javob ham yoza oladi — ilgari javob har doim DM bo'lib ketardi va DM ochmagan
 * odamga umuman yetib bormasdi.
 */
const REPLY_MODES: { id: IgReplyMode; label: string }[] = [
  { id: 'dm', label: 'Shaxsiy xabar (DM)' },
  { id: 'comment', label: 'Izohga javob (ochiq)' },
  { id: 'private_reply', label: 'Izohga shaxsiy javob' },
]

/** Suhbat ochilganda standart rejim: oxirgi kiruvchi xabar izoh bo'lsa — izohga javob. */
function defaultReplyMode(d: IgConversationDetail): IgReplyMode {
  if (!d.latestCommentId) return 'dm'
  const lastIn = [...d.messages].reverse().find((m) => m.direction === 'in')
  return lastIn?.channel === 'comment' ? 'comment' : 'dm'
}

/** ISO vaqtdan "HH:mm" (satrni to'g'ridan-to'g'ri o'qiymiz — vaqt mintaqasi siljitmasin). */
function timeOf(iso: string): string {
  const m = /T(\d{2}):(\d{2})/.exec(iso ?? '')
  return m ? `${m[1]}:${m[2]}` : ''
}

/** ISO vaqtdan "dd.MM HH:mm". */
function stampOf(iso: string): string {
  const m = /^(\d{4})-(\d{2})-(\d{2})T(\d{2}):(\d{2})/.exec(iso ?? '')
  return m ? `${m[3]}.${m[2]} ${m[4]}:${m[5]}` : (iso ?? '')
}

/**
 * INBOX — Instagram suhbatlari.
 *
 * Chapda ro'yxat (qidiruv, holat filtri, o'qilmagan belgisi, «Operator kerak» qizil chipi),
 * o'ngda xabarlar lentasi va operator javobi.
 *
 * ⚠️ DM'ga javob mijozning oxirgi xabaridan keyingi **24 soat** ichida oddiy, **7 kungacha**
 * esa `HUMAN_AGENT` tegi bilan yuboriladi (server o'zi tanlaydi). 7 kundan keyin server 400
 * qaytaradi — xato matni AYNAN ko'rsatiladi, jimgina yutilmaydi. Izohga javobda oyna yo'q.
 */
export function InstagramInbox() {
  const { can } = usePerm()
  const canEdit = can('marketing.inbox', 'edit')
  const [params, setParams] = useSearchParams()

  const [items, setItems] = useState<IgConversation[]>([])
  const [total, setTotal] = useState(0)
  const [listLoading, setListLoading] = useState(true)
  const [listError, setListError] = useState('')

  const [q, setQ] = useState('')
  const [status, setStatus] = useState<IgConversationStatus | ''>('')
  const [onlyOperator, setOnlyOperator] = useState(false)
  /** «Reklamadan» chipi — atributsiyasi TOPILGAN suhbatlar (`?source=ads`). */
  const [onlyAds, setOnlyAds] = useState(false)

  const activeId = params.get('id') ?? ''
  const [detail, setDetail] = useState<IgConversationDetail | null>(null)
  const [detailLoading, setDetailLoading] = useState(false)
  const [detailError, setDetailError] = useState('')

  const [text, setText] = useState('')
  // Avtoyangilash effekti `text` ga BOG'LANMASIN (har harfda taymer qayta qurilardi) —
  // shuning uchun joriy qiymat ref orqali o'qiladi.
  const textRef = useRef('')
  textRef.current = text
  const [sending, setSending] = useState(false)
  const [mode, setMode] = useState<IgReplyMode>('dm')
  // Rejim FAQAT boshqa suhbat ochilganda tiklanadi — 15 soniyalik avtoyangilanish operator
  // tanlagan rejimni qaytarib yubormasin.
  const modeConvRef = useRef('')
  const [sendError, setSendError] = useState('')
  const [actionError, setActionError] = useState('')
  const bodyRef = useRef<HTMLDivElement>(null)

  const loadList = useCallback(() => {
    setListLoading(true)
    setListError('')
    getIgConversations({
      q: q.trim(),
      status,
      needsOperator: onlyOperator ? true : undefined,
      source: onlyAds ? 'ads' : '',
      page: 1,
      pageSize: 100,
    })
      .then((r) => { setItems(r.items); setTotal(r.total) })
      .catch((e) => setListError(apiErrorMessage(e, "Suhbatlarni yuklab bo'lmadi")))
      .finally(() => setListLoading(false))
  }, [q, status, onlyOperator, onlyAds])

  // Qidiruv yozilayotganda har harfda so'rov ketmasin.
  useEffect(() => {
    const t = setTimeout(loadList, 300)
    return () => clearTimeout(t)
  }, [loadList])

  const openConv = useCallback((id: string) => {
    setDetailLoading(true)
    setDetailError('')
    setSendError('')
    setActionError('')
    getIgConversation(id)
      .then((d) => {
        setDetail(d)
        if (modeConvRef.current !== d.conversation.id) {
          modeConvRef.current = d.conversation.id
          setMode(defaultReplyMode(d))
        }
        if (d.conversation.unread) {
          markIgConversationRead(id)
            .then(() => setItems((xs) => xs.map((x) => (x.id === id ? { ...x, unread: false } : x))))
            .catch(() => { /* o'qilgan belgisi — ikkilamchi, xato ko'rsatilmaydi */ })
        }
      })
      .catch((e) => setDetailError(apiErrorMessage(e, "Suhbatni ochib bo'lmadi")))
      .finally(() => setDetailLoading(false))
  }, [])

  useEffect(() => {
    if (activeId) openConv(activeId)
    else setDetail(null)
  }, [activeId, openConv])

  /**
   * AVTOMATIK YANGILANISH — Instagram xabari webhook orqali fonda keladi, ya'ni sahifada hech
   * qanday "hodisa" bo'lmaydi. Yangilanishsiz operator yangi murojaatni sahifani QO'LDA
   * yangilamaguncha ko'rmasdi (Telegram signali bo'lsa ham, "Inbox" degan bo'lim buni kutdiradi).
   *
   * ⚠️ Sahifa KO'RINMAYOTGANDA so'rov yubormaymiz (`visibilitychange`): ochiq qolgan tab
   * kechasi ham serverga har 15 soniyada so'rov yuborib turardi.
   * ⚠️ Ochiq suhbat ham yangilanadi, lekin FAQAT operator matn yozmayotgan bo'lsa —
   * aks holda javob yozayotganda lenta pastga sakrab, yozuvni chalg'itardi.
   */
  useEffect(() => {
    const tick = () => {
      if (document.visibilityState !== 'visible') return
      loadList()
      if (activeId && !textRef.current) openConv(activeId)
    }
    const id = setInterval(tick, REFRESH_MS)
    document.addEventListener('visibilitychange', tick)
    return () => {
      clearInterval(id)
      document.removeEventListener('visibilitychange', tick)
    }
  }, [loadList, activeId, openConv])

  // Yangi xabar kelganda lenta pastiga tushsin.
  useEffect(() => {
    if (bodyRef.current) bodyRef.current.scrollTop = bodyRef.current.scrollHeight
  }, [detail])

  const select = (id: string) => {
    params.set('id', id)
    setParams(params, { replace: true })
  }

  const send = async () => {
    if (!detail || !text.trim()) return
    setSending(true)
    setSendError('')
    try {
      const msg = await replyIgConversation(detail.conversation.id, text.trim(), mode)
      setDetail({ ...detail, messages: [...detail.messages, msg] })
      setText('')
      loadList()
      // Yopiq javob bir marta — holat (va boshqa bayroqlar) serverdan yangilansin.
      if (mode !== 'dm') openConv(detail.conversation.id)
    } catch (e) {
      // Oyna yopiq / yopiq javob ishlatilgan bo'lsa server aniq sabab qaytaradi — shuni ko'rsatamiz.
      setSendError(apiErrorMessage(e, "Javob yuborilmadi"))
    } finally {
      setSending(false)
    }
  }

  const runAction = async (fn: () => Promise<unknown>, fallback: string) => {
    setActionError('')
    try {
      await fn()
      if (activeId) openConv(activeId)
      loadList()
    } catch (e) {
      setActionError(apiErrorMessage(e, fallback))
    }
  }

  const conv = detail?.conversation
  // Joriy rejimda yuborish mumkinmi (server ham baribir tekshiradi — bu faqat oldindan aytish).
  const modeBlocked = !detail
    ? true
    : mode === 'dm'
      ? !detail.humanAgentWindowOpen
      : mode === 'private_reply'
        ? !detail.latestCommentId || !detail.privateReplyAvailable
        : !detail.latestCommentId

  return (
    <MarketingPage title="Inbox" sub={`Instagram suhbatlari${total ? ` · ${total} ta` : ''}`}>
      <div className="inbox fade-up">
        {/* ── SUHBATLAR RO'YXATI ── */}
        <div className="conv-list">
          <div className="conv-list-head">
            <div className="mk-search">
              <Icon name="search" style={{ width: 16, height: 16 }} />
              <input
                placeholder="Username yoki matn bo'yicha…"
                value={q}
                onChange={(e) => setQ(e.target.value)}
              />
            </div>
            <div className="conv-filters">
              {([['', 'Barchasi'], ['bot', 'Botda'], ['operator', 'Operatorda'], ['closed', 'Yopilgan']] as const).map(([k, l]) => (
                <button
                  key={k || 'all'}
                  className={'conv-filter ' + (status === k ? 'active' : '')}
                  onClick={() => setStatus(k)}
                >{l}</button>
              ))}
            </div>
            <div className="conv-filters">
              <button
                className={'conv-filter ' + (onlyOperator ? 'active' : '')}
                onClick={() => setOnlyOperator(!onlyOperator)}
              >
                Operator kerak
              </button>
              {/* ⚠️ Yorliqda "taxminiy" ATAYIN yozilgan: bu kesim "reklamadan kelganlarning
                  HAMMASI" emas, "ANIQLANGANLARI". */}
              <button
                className={'conv-filter ' + (onlyAds ? 'active' : '')}
                onClick={() => setOnlyAds(!onlyAds)}
                title={AD_HINT}
              >
                📢 Reklamadan (taxminiy)
              </button>
            </div>
          </div>

          <div className="conv-scroll">
            {listLoading && <div style={{ padding: 16 }}><MkLoading /></div>}
            {!listLoading && listError && <div style={{ padding: 16 }}><MkError text={listError} onRetry={loadList} /></div>}
            {!listLoading && !listError && items.length === 0 && (
              <div style={{ padding: 16 }}>
                <MkEmpty text="Suhbat topilmadi" hint="Filtrni o'zgartiring yoki modul sozlanganini tekshiring." />
              </div>
            )}
            {!listLoading && !listError && items.map((c) => (
              <div
                key={c.id}
                className={'conv-item ' + (activeId === c.id ? 'active' : '')}
                onClick={() => select(c.id)}
              >
                <div className="conv-avatar" style={{ background: 'var(--c-instagram)' }}>
                  {(c.username || '?').slice(0, 2).toUpperCase()}
                  <div className="conv-ch-badge ch-instagram"><ChannelIcon /></div>
                </div>
                <div className="conv-main">
                  <div className="conv-name-row">
                    <span className="conv-name">@{c.username || c.igUserId}</span>
                    <span className="conv-time">{stampOf(c.lastInboundAt || c.createdAt)}</span>
                  </div>
                  <div className="conv-snippet">{c.lastMessageText || '—'}</div>
                  <div className="conv-flags">
                    {c.needsOperator && <span className="badge badge-danger">Operator kerak</span>}
                    {c.status === 'operator' && <span className="badge badge-warning">Operatorda</span>}
                    {c.status === 'closed' && <span className="badge" style={{ background: 'var(--surface-2)', color: 'var(--text-3)' }}>Yopilgan</span>}
                    {c.adId && (
                      <span
                        className="badge"
                        style={{ background: 'var(--surface-2)', color: 'var(--text-2)' }}
                        title={AD_HINT}
                      >
                        📢 {c.adCampaignName || 'Reklama'} · taxminiy
                      </span>
                    )}
                    {c.leadId && <span className="badge badge-success">Lid</span>}
                    {c.leadScore > 0 && <span className="badge badge-ai">Ball {c.leadScore}</span>}
                  </div>
                </div>
                {c.unread && <div className="conv-unread">•</div>}
              </div>
            ))}
          </div>
        </div>

        {/* ── SUHBAT ── */}
        <div className="chat">
          {!activeId && (
            <div style={{ margin: 'auto', padding: 30 }}>
              <MkEmpty text="Suhbat tanlanmagan" hint="Chapdagi ro'yxatdan birini tanlang." />
            </div>
          )}

          {activeId && detailLoading && <div style={{ margin: 'auto', padding: 30 }}><MkLoading /></div>}

          {activeId && !detailLoading && detailError && (
            <div style={{ margin: 'auto', padding: 30 }}>
              <MkError text={detailError} onRetry={() => openConv(activeId)} />
            </div>
          )}

          {conv && !detailLoading && (
            <>
              <div className="chat-head">
                <div className="conv-avatar" style={{ width: 40, height: 40, background: 'var(--c-instagram)' }}>
                  {(conv.username || '?').slice(0, 2).toUpperCase()}
                  <div className="conv-ch-badge ch-instagram"><ChannelIcon /></div>
                </div>
                <div className="chat-head-info">
                  {/* Username aniqlangan bo'lsa — Instagram profiliga havola. DM'da username
                      webhook'da kelmaydi, u serverda profil so'rovidan olinadi; aniqlanmagan
                      holatda (profil yopiq / so'rov yiqilgan) havolasiz raqam ko'rsatiladi. */}
                  <div className="chat-head-name">
                    {conv.username
                      ? (
                        <a
                          href={`https://instagram.com/${conv.username}`}
                          target="_blank"
                          rel="noreferrer noopener"
                          title="Instagram profilini ochish"
                        >
                          @{conv.username}
                        </a>
                      )
                      : <>@{conv.igUserId}</>}
                  </div>
                  <div className="chat-head-status">
                    {STATUS_LABEL[conv.status]}
                    {conv.intent && <> · {conv.intent}</>}
                    {conv.language && <> · {conv.language}</>}
                    {conv.leadScore > 0 && <> · ball {conv.leadScore}</>}
                  </div>
                </div>
                {canEdit && (
                  <div className="chat-actions">
                    {/* Yopilgan suhbat — faqat «Qayta ochish» (mijoz yangi yozsa server o'zi ochadi). */}
                    {conv.status === 'closed' && (
                      <button className="btn btn-outline btn-sm" onClick={() => runAction(() => reopenIgConversation(conv.id), "Qayta ochib bo'lmadi")}>
                        <Icon name="refresh" /> Qayta ochish
                      </button>
                    )}
                    {conv.status === 'bot' && (
                      <button className="btn btn-outline btn-sm" onClick={() => runAction(() => takeoverIgConversation(conv.id), "Botni to'xtatib bo'lmadi")}>
                        <Icon name="users" /> Botni to'xtatish
                      </button>
                    )}
                    {conv.status === 'operator' && (
                      <button className="btn btn-outline btn-sm" onClick={() => runAction(() => releaseIgConversation(conv.id), "Botga qaytarib bo'lmadi")}>
                        <Icon name="zap" /> Botga qaytarish
                      </button>
                    )}
                    {conv.status !== 'closed' && (
                      <button className="btn btn-outline btn-sm" onClick={() => runAction(() => closeIgConversation(conv.id), "Yopib bo'lmadi")}>
                        <Icon name="check" /> Yopish
                      </button>
                    )}
                    {/* ⚠️ `detail.lead` bo'yicha (`conv.leadId` EMAS): bog'langan lid CRM'dan
                        o'chirilgan bo'lsa ham tugma qaytadi. O'quvchiga AYLANTIRILGAN lid ham
                        "bog'lanmagan" — qaytgan o'quvchi uchun YANGI lid ochiladi (CRM-1). */}
                    {(!detail?.lead || detail.lead.converted) && (
                      <button className="btn btn-primary btn-sm" onClick={() => runAction(() => createIgLead(conv.id), "Lid yaratilmadi")}>
                        <Icon name="user" /> Lidga aylantirish
                      </button>
                    )}
                  </div>
                )}
              </div>

              {(conv.needsOperator || actionError || detail?.lead || conv.adId) && (
                <div style={{ padding: '12px 18px 0' }}>
                  {conv.needsOperator && (
                    <div className="mk-alert" style={{ marginBottom: 10 }}>
                      <Icon name="warn" style={{ width: 18, height: 18, flexShrink: 0 }} />
                      <div style={{ flex: 1 }}>
                        <div className="mk-alert-title">Operator aralashuvi kerak</div>
                        <div>{conv.needsOperatorReason || 'Sabab ko‘rsatilmagan'}</div>
                      </div>
                    </div>
                  )}
                  {/* 🔴 TAXMINIY atributsiya — "aniq" deb ko'rsatilmaydi va cheklovlari
                      ochiq yozib qo'yiladi (tooltip yagona kanal bo'lib qolmasin). */}
                  {conv.adId && (
                    <div className="mk-alert" style={{ marginBottom: 10 }} title={AD_HINT}>
                      <span style={{ fontSize: 17, lineHeight: 1, flexShrink: 0 }}>📢</span>
                      <div style={{ flex: 1 }}>
                        <div className="mk-alert-title">
                          Reklama izohidan kelgan — taxminiy
                        </div>
                        <div>
                          Kampaniya: {conv.adCampaignName || conv.adCampaignId || '—'}
                          {conv.adId && <> · e'lon: {conv.adId}</>}
                        </div>
                        <div style={{ marginTop: 4, fontSize: 12 }}>
                          Bog'lanish media bo'yicha aniqlangan: boostlangan postda ishlaydi,
                          "dark post" va dinamik reklamada aniqlanmaydi. Belgining yo'qligi
                          "reklamadan kelmagan" degani emas.
                        </div>
                      </div>
                    </div>
                  )}
                  {detail?.lead && (
                    <div className="mk-alert" style={{ borderColor: 'var(--success)', background: 'var(--success-soft)', color: '#0d6b4b', marginBottom: 10 }}>
                      <Icon name="user" style={{ width: 18, height: 18, flexShrink: 0 }} />
                      <div style={{ flex: 1 }}>
                        <div className="mk-alert-title">
                          Lidga bog'langan: {detail.lead.fullName}
                          {detail.lead.converted && <> · o'quvchiga aylantirilgan</>}
                        </div>
                        <div>{detail.lead.phone ? formatPhone(detail.lead.phone) : 'Telefon yo‘q'} · manba: {detail.lead.source || '—'}</div>
                        {detail.lead.converted && (
                          <div style={{ marginTop: 4, fontSize: 12 }}>
                            Yangi murojaat bo'lsa «Lidga aylantirish» YANGI lid ochadi — eski lid va uning
                            tarixi o'zgarmaydi.
                          </div>
                        )}
                      </div>
                    </div>
                  )}
                  {actionError && <MkError text={actionError} />}
                </div>
              )}

              <div className="chat-body" ref={bodyRef}>
                {detail && detail.messages.length === 0 && <MkEmpty text="Xabar yo'q" />}
                {detail?.messages.map((m) => (
                  <div key={m.id} className={'msg-row ' + (m.direction === 'out' ? 'out' : 'in')}>
                    <div>
                      <div className="msg-bubble" style={{ whiteSpace: 'pre-line' }}>{m.text}</div>
                      <div className="msg-meta">
                        {m.isAi && <span className="auto-tag"><Icon name="sparkle" style={{ width: 10, height: 10 }} /> AI</span>}
                        {!m.isAi && m.direction === 'out' && m.actorName && <span className="auto-tag">{m.actorName}</span>}
                        <span>{CHANNEL_LABEL[m.channel] ?? m.channel}</span>
                        <span>{timeOf(m.createdAt)}</span>
                      </div>
                      {m.error && <div className="msg-error">Yuborishda xato: {m.error}</div>}
                    </div>
                  </div>
                ))}
              </div>

              {sendError && (
                <div style={{ padding: '0 18px 10px' }}><MkError text={sendError} /></div>
              )}

              {/* JAVOB OYNASI — rejimga qarab. ⚠️ 24 soatdan keyin ham 7 kun ichida operator
                  HUMAN_AGENT tegi bilan yoza oladi (server o'zi qo'yadi) — "yopildi" faqat 7 kundan keyin. */}
              {detail && mode === 'dm' && !detail.dmWindowOpen && (
                <div style={{ padding: '0 18px 10px' }}>
                  <div className="mk-alert" style={{ marginBottom: 0 }}>
                    <Icon name="clock" style={{ width: 18, height: 18, flexShrink: 0 }} />
                    <div style={{ flex: 1 }}>
                      {detail.humanAgentWindowOpen
                        ? (
                          <>
                            <div className="mk-alert-title">24 soat o'tdi — operator javobi hali mumkin</div>
                            <div>
                              Mijoz yozganidan 24 soat o'tdi: bot endi javob bermaydi, lekin sizning javobingiz
                              7 kun ichida «odam javobi» (HUMAN_AGENT) belgisi bilan yetkaziladi.
                            </div>
                          </>
                        )
                        : (
                          <>
                            <div className="mk-alert-title">Javob oynasi yopilgan (7 kun)</div>
                            <div>
                              Mijozning oxirgi xabaridan 7 kundan ko'p vaqt o'tdi — Instagram shaxsiy xabarga
                              ruxsat bermaydi. Mijoz yana yozsa oyna qayta ochiladi
                              {detail.latestCommentId ? '; izohiga esa ochiq javob yozish mumkin.' : '.'}
                            </div>
                          </>
                        )}
                    </div>
                  </div>
                </div>
              )}
              {detail && mode === 'comment' && (
                <div style={{ padding: '0 18px 10px' }}>
                  <div className="field-hint" style={{ margin: 0 }}>
                    Javob mijozning oxirgi izohi ostida <b>ochiq</b> chiqadi — uni hamma ko'radi. Telefon
                    yoki shaxsiy ma'lumot yozmang.
                  </div>
                </div>
              )}
              {detail && mode === 'private_reply' && (
                <div style={{ padding: '0 18px 10px' }}>
                  {detail.privateReplyAvailable
                    ? (
                      <div className="field-hint" style={{ margin: 0 }}>
                        Izoh muallifiga shaxsiy xabar boradi. Instagram har izohga faqat <b>bir marta</b> va
                        7 kun ichida ruxsat beradi.
                      </div>
                    )
                    : <MkError text={detail.privateReplyBlockedReason || "Bu izohga yopiq javob yuborib bo'lmaydi."} />}
                </div>
              )}

              {/* Izoh suhbatida — javob turini tanlash (DM ochmagan odamga ham javob yetib borsin). */}
              {detail?.latestCommentId && canEdit && conv.status !== 'closed' && (
                <div className="conv-filters" style={{ padding: '0 18px 8px' }}>
                  {REPLY_MODES.map((m) => (
                    <button
                      key={m.id}
                      className={'conv-filter ' + (mode === m.id ? 'active' : '')}
                      onClick={() => { setMode(m.id); setSendError('') }}
                      title={m.id === 'private_reply' && !detail.privateReplyAvailable
                        ? detail.privateReplyBlockedReason
                        : undefined}
                    >{m.label}</button>
                  ))}
                </div>
              )}

              <div className="chat-input-bar">
                <input
                  className="chat-input"
                  placeholder={!canEdit
                    ? "Yozish uchun ruxsat yo'q"
                    : conv.status === 'closed'
                      ? 'Suhbat yopilgan — «Qayta ochish» bosing'
                      : mode === 'comment' ? 'Izohga ochiq javob…' : mode === 'private_reply' ? 'Izohga shaxsiy javob…' : 'Javob yozing…'}
                  value={text}
                  disabled={!canEdit || sending || conv.status === 'closed'}
                  onChange={(e) => setText(e.target.value)}
                  onKeyDown={(e) => { if (e.key === 'Enter' && !e.shiftKey) { e.preventDefault(); void send() } }}
                />
                <button
                  className="btn btn-primary btn-icon-only"
                  style={{ padding: 11 }}
                  disabled={!canEdit || sending || !text.trim() || conv.status === 'closed' || modeBlocked}
                  onClick={send}
                  title="Yuborish"
                >
                  <Icon name="send" />
                </button>
              </div>
            </>
          )}
        </div>
      </div>
    </MarketingPage>
  )
}
