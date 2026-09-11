import { useCallback, useEffect, useState } from 'react'
import { Link, useNavigate } from 'react-router-dom'
import { apiErrorMessage } from '@/lib/utils'
import {
  getIgAnalytics, getIgConversations, getIgStatus,
  type IgAnalytics, type IgConversation, type IgStatus,
} from '@/api/services/instagram'
import { ChannelIcon, Icon, MarketingPage, MkCard, MkEmpty, MkError, MkLoading, MkStat } from './mk'

/** Bugungi sana ("yyyy-MM-dd"). */
const today = () => new Date().toISOString().slice(0, 10)

/** Suhbat "qaynoq" hisoblanadigan ball chegarasi (backend bilan bir xil: `IgConst.HotLeadScore`). */
const HOT_SCORE = 70

/**
 * BOSHQARUV PANELI — "hozir nima bo'lyapti" ekrani.
 *
 * Bugungi raqamlar (hodisa, javob, lid, qaynoq), navbat holati (qayta ishlanmagan va xato
 * bo'lgan webhook hodisalari), operator kutayotgan hamda qaynoq suhbatlar.
 * Modul o'chirilgan bo'lsa — hech qanday javob ketmayotgani KATTA ogohlantirish bilan
 * aytiladi (jimgina ishlamay turishi eng yomon holat).
 */
export function InstagramDashboard() {
  const nav = useNavigate()
  const [status, setStatus] = useState<IgStatus | null>(null)
  const [analytics, setAnalytics] = useState<IgAnalytics | null>(null)
  const [convs, setConvs] = useState<IgConversation[]>([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState('')

  const load = useCallback(() => {
    setLoading(true)
    setError('')
    const d = today()
    Promise.all([
      getIgStatus(),
      getIgAnalytics(d, d),
      getIgConversations({ page: 1, pageSize: 50 }),
    ])
      .then(([st, an, list]) => { setStatus(st); setAnalytics(an); setConvs(list.items) })
      .catch((e) => setError(apiErrorMessage(e, "Ma'lumotni yuklab bo'lmadi")))
      .finally(() => setLoading(false))
  }, [])

  useEffect(load, [load])

  if (loading) {
    return (
      <MarketingPage title="Boshqaruv paneli" sub="Instagram AI agenti — bugungi holat">
        <MkLoading />
      </MarketingPage>
    )
  }

  if (error || !status || !analytics) {
    return (
      <MarketingPage title="Boshqaruv paneli" sub="Instagram AI agenti — bugungi holat">
        <MkError text={error || "Ma'lumot yuklanmadi"} onRetry={load} />
      </MarketingPage>
    )
  }

  const t = analytics.totals
  const hot = convs.filter((c) => c.leadScore >= HOT_SCORE).slice(0, 6)
  const needsOperator = convs.filter((c) => c.needsOperator).slice(0, 6)

  /** Bugungi asosiy raqamlar. Rang — MA'NO bo'yicha: lid yaxshi, qaynoq — diqqat talab. */
  const stats: { label: string; value: number; icon: string; tone: 'primary' | 'success' | 'warning' | 'muted' }[] = [
    { label: 'Kelgan hodisalar', value: t.events, icon: 'inbox', tone: 'primary' },
    { label: 'Yuborilgan javoblar', value: t.replies, icon: 'send', tone: 'primary' },
    { label: 'Yangi lidlar', value: t.leads, icon: 'users', tone: 'success' },
    { label: 'Qaynoq lidlar', value: t.hot, icon: 'fire', tone: 'warning' },
  ]

  return (
    <MarketingPage
      title="Boshqaruv paneli"
      sub="Instagram AI agenti — bugungi holat"
      actions={<button className="btn btn-ghost btn-sm" onClick={load}><Icon name="refresh" /> Yangilash</button>}
    >
      <div className="fade-up">
        {/* Modul o'chiq — eng muhim ogohlantirish */}
        {!status.enabled && (
          <div className="mk-alert mk-alert-danger">
            <Icon name="warn" style={{ width: 22, height: 22, flexShrink: 0 }} />
            <div style={{ flex: 1 }}>
              <div className="mk-alert-title">Instagram moduli O'CHIRILGAN</div>
              <div>
                Kelgan izoh va xabarlarga hech qanday javob yuborilmayapti. Yoqish uchun
                Sozlamalar bo'limiga o'ting.
              </div>
            </div>
            <Link className="btn btn-primary btn-sm" to="/admin/marketing/settings">
              <Icon name="settings" /> Sozlamalar
            </Link>
          </div>
        )}

        {status.enabled && !status.connected && (
          <div className="mk-alert">
            <Icon name="warn" style={{ width: 22, height: 22, flexShrink: 0 }} />
            <div style={{ flex: 1 }}>
              <div className="mk-alert-title">Akkaunt ulanmagan</div>
              <div>Modul yoqilgan, lekin Instagram akkaunti ulanmagani uchun hodisa kelmaydi.</div>
            </div>
            <Link className="btn btn-primary btn-sm" to="/admin/marketing/settings">
              <Icon name="link" /> Ulash
            </Link>
          </div>
        )}

        {status.enabled && status.connected && status.knowledgeCount === 0 && (
          <div className="mk-alert">
            <Icon name="warn" style={{ width: 22, height: 22, flexShrink: 0 }} />
            <div style={{ flex: 1 }}>
              <div className="mk-alert-title">Bilim bazasi bo'sh</div>
              <div>AI faqat kiritilgan ma'lumot asosida javob beradi — hozircha javob bera olmaydi.</div>
            </div>
            <Link className="btn btn-primary btn-sm" to="/admin/marketing/knowledge">
              <Icon name="book" /> To'ldirish
            </Link>
          </div>
        )}

        {/* Bugungi raqamlar */}
        <div className="mk-kpi" style={{ marginBottom: 22 }}>
          {stats.map((s) => (
            <MkStat key={s.label} label={s.label} value={s.value.toLocaleString()} icon={s.icon} tone={s.tone} />
          ))}
        </div>

        {/* Uchala blok BITTA moslashuvchan grid'da: keng ekranda yonma-yon turadi,
            tor ekranda ustma-ust tushadi. Ilgari qattiq `1fr 1fr` edi va uchinchi
            blok butun kenglikka cho'zilib ketardi. */}
        <div className="mk-cols2">
          {/* Navbat */}
          <MkCard title="Navbat holati" sub="Webhook navbati va bugungi chegaralar">
            <div className="row-between">
              <div>
                <div className="opt-name">Qayta ishlanmagan hodisalar</div>
                <div className="opt-desc">Webhook'dan kelgan, hali javob berilmagan xabarlar.</div>
              </div>
              <div className="stat-value" style={{ fontSize: 24, margin: 0 }}>{status.pendingEvents}</div>
            </div>
            <div className="row-between">
              <div>
                <div className="opt-name">Xato bo'lgan hodisalar</div>
                <div className="opt-desc">3 martadan keyin ham qayta ishlanmagan — sababi «Sozlamalar» diagnostikasida.</div>
              </div>
              <div className="stat-value" style={{ fontSize: 24, margin: 0, color: status.failedEvents > 0 ? 'var(--danger)' : undefined }}>
                {status.failedEvents}
              </div>
            </div>
            <div className="row-between">
              <div>
                <div className="opt-name">Bugungi javoblar</div>
                <div className="opt-desc">Kunlik chegara: {status.dailyLimit}</div>
              </div>
              <div className="stat-value" style={{ fontSize: 24, margin: 0 }}>{status.todayReplies}</div>
            </div>
            <div className="row-between">
              <div>
                <div className="opt-name">Eskalatsiyalar (bugun)</div>
                <div className="opt-desc">AI o'zi hal qila olmay, odamga uzatgan suhbatlar.</div>
              </div>
              <div className="stat-value" style={{ fontSize: 24, margin: 0 }}>{t.escalations}</div>
            </div>
          </MkCard>

          {/* Operator kerak */}
          <MkCard
            title="Operator kerak"
            sub="AI o'zi hal qila olmagan suhbatlar"
            actions={(
              <button className="link-btn" onClick={() => nav('/admin/marketing/inbox')}>
                Inbox <Icon name="chevRight" style={{ width: 13, height: 13 }} />
              </button>
            )}
          >
            {needsOperator.length === 0
              ? <MkEmpty text="Hozircha odam aralashuvi kerak emas" />
              : needsOperator.map((c) => (
                <div className="feed-item" key={c.id} style={{ cursor: 'pointer' }} onClick={() => nav(`/admin/marketing/inbox?id=${c.id}`)}>
                  <div className="ch-icon ch-instagram"><ChannelIcon /></div>
                  <div className="feed-body" style={{ minWidth: 0 }}>
                    <div style={{ fontWeight: 700, fontSize: 13.5 }}>@{c.username || c.igUserId}</div>
                    <div className="feed-time" style={{ overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }}>
                      {c.needsOperatorReason || 'Sabab ko‘rsatilmagan'}
                    </div>
                  </div>
                  <span className="badge badge-danger">Operator</span>
                </div>
              ))}
          </MkCard>

          {/* Qaynoq suhbatlar */}
          <MkCard
            title="Oxirgi qaynoq suhbatlar"
            sub={`Qiziqish balli ${HOT_SCORE} va undan yuqori`}
            actions={(
              <button className="link-btn" onClick={() => nav('/admin/marketing/inbox')}>
                Barchasi <Icon name="chevRight" style={{ width: 13, height: 13 }} />
              </button>
            )}
          >
            {hot.length === 0
              ? <MkEmpty text="Qaynoq suhbat yo'q" hint="Mijoz aniq qiziqish bildirsa yoki telefon qoldirsa shu yerda chiqadi." />
              : hot.map((c) => (
                <div className="feed-item" key={c.id} style={{ alignItems: 'center', cursor: 'pointer' }} onClick={() => nav(`/admin/marketing/inbox?id=${c.id}`)}>
                  <div className="ch-icon ch-instagram"><ChannelIcon /></div>
                  <div className="feed-body" style={{ minWidth: 0 }}>
                    <div style={{ fontWeight: 700, fontSize: 13.5 }}>@{c.username || c.igUserId}</div>
                    {/* Matn kartochka kengligiga moslashadi — qattiq `maxWidth` olib
                        tashlandi, aks holda tor ustunda qatordan chiqib ketardi. */}
                    <div className="feed-time" style={{ overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }}>
                      {c.lastMessageText || '—'}
                    </div>
                  </div>
                  <div style={{ display: 'flex', gap: 6, alignItems: 'center', flexShrink: 0 }}>
                    {c.leadId && <span className="badge badge-success">Lid</span>}
                    <span className="badge badge-warning"><Icon name="fire" style={{ width: 11, height: 11 }} /> {c.leadScore}</span>
                  </div>
                </div>
              ))}
          </MkCard>
        </div>
      </div>
    </MarketingPage>
  )
}
