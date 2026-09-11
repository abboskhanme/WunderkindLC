import { useCallback, useEffect, useRef, useState } from 'react'
import { useSearchParams } from 'react-router-dom'
import { usePerm } from '@/lib/permissions'
import { apiErrorMessage } from '@/lib/utils'
import {
  closeIgConversation, createIgLead, getIgConversation, getIgConversations,
  markIgConversationRead, releaseIgConversation, replyIgConversation, takeoverIgConversation,
  type IgConversation, type IgConversationDetail, type IgConversationStatus,
} from '@/api/services/instagram'
import { ChannelIcon, Icon, MarketingPage, MkEmpty, MkError, MkLoading } from './mk'

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
 * ⚠️ DM'ga javob Instagram qoidasi bo'yicha mijozning oxirgi xabaridan keyingi **24 soat**
 * ichida yuboriladi. Oyna yopilgan bo'lsa server 400 qaytaradi — xato matni AYNAN
 * ko'rsatiladi, jimgina yutilmaydi.
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
      const msg = await replyIgConversation(detail.conversation.id, text.trim())
      setDetail({ ...detail, messages: [...detail.messages, msg] })
      setText('')
      loadList()
    } catch (e) {
      // 24 soat oynasi yopiq bo'lsa server aniq sabab qaytaradi — shuni ko'rsatamiz.
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
                    {conv.status !== 'operator'
                      ? (
                        <button className="btn btn-outline btn-sm" onClick={() => runAction(() => takeoverIgConversation(conv.id), "Botni to'xtatib bo'lmadi")}>
                          <Icon name="users" /> Botni to'xtatish
                        </button>
                      )
                      : (
                        <button className="btn btn-outline btn-sm" onClick={() => runAction(() => releaseIgConversation(conv.id), "Botga qaytarib bo'lmadi")}>
                          <Icon name="zap" /> Botga qaytarish
                        </button>
                      )}
                    {conv.status !== 'closed' && (
                      <button className="btn btn-outline btn-sm" onClick={() => runAction(() => closeIgConversation(conv.id), "Yopib bo'lmadi")}>
                        <Icon name="check" /> Yopish
                      </button>
                    )}
                    {!conv.leadId && (
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
                        <div className="mk-alert-title">Lidga bog'langan: {detail.lead.fullName}</div>
                        <div>{detail.lead.phone || 'Telefon yo‘q'} · manba: {detail.lead.source || '—'}</div>
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

              {detail && !detail.dmWindowOpen && (
                <div style={{ padding: '0 18px 10px' }}>
                  <div className="mk-alert" style={{ marginBottom: 0 }}>
                    <Icon name="clock" style={{ width: 18, height: 18, flexShrink: 0 }} />
                    <div style={{ flex: 1 }}>
                      <div className="mk-alert-title">24 soat oynasi yopilgan</div>
                      <div>
                        Instagram qoidasi bo'yicha mijozning oxirgi xabaridan 24 soat o'tgach unga
                        yozib bo'lmaydi. Mijoz yana yozsa oyna qayta ochiladi.
                      </div>
                    </div>
                  </div>
                </div>
              )}

              <div className="chat-input-bar">
                <input
                  className="chat-input"
                  placeholder={canEdit ? 'Javob yozing…' : "Yozish uchun ruxsat yo'q"}
                  value={text}
                  disabled={!canEdit || sending || conv.status === 'closed'}
                  onChange={(e) => setText(e.target.value)}
                  onKeyDown={(e) => { if (e.key === 'Enter' && !e.shiftKey) { e.preventDefault(); void send() } }}
                />
                <button
                  className="btn btn-primary btn-icon-only"
                  style={{ padding: 11 }}
                  disabled={!canEdit || sending || !text.trim() || conv.status === 'closed'}
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
