import { useEffect, useRef, useState } from 'react'
import {
  getStudentChat,
  sendStudentChat,
  type StudentChatMessage,
} from '@/api/services/studentPortal'
import { useAuth } from '@/context/auth-context'
import { Icon, initials, fmtTime } from '@/pages/student/lib'

/* ============================================================
   O'quvchi portali — Guruh chati.
   O'z xabarlari o'ngda (accent), boshqalar chapda (avatar + nom + rol).
   Har 4 sekundda yangi xabarlarni so'raydi (since=oxirgi createdAt).
   ============================================================ */

const roleColor = (r: string) => (r === 'teacher' ? 'var(--accent)' : r === 'admin' ? 'var(--violet)' : r === 'parent' ? 'var(--amber)' : 'var(--green)')
const roleLabel = (r: string) => (r === 'teacher' ? 'O‘qituvchi' : r === 'admin' ? 'Ma’muriyat' : r === 'parent' ? 'Ota-ona' : '')

export function StudentChatScreen() {
  const { user } = useAuth()
  const meId = user?.id
  const [msgs, setMsgs] = useState<StudentChatMessage[]>([])
  const [err, setErr] = useState<string | null>(null)
  const [loading, setLoading] = useState(true)
  const [text, setText] = useState('')
  const [toast, setToast] = useState<string | null>(null)

  const listRef = useRef<HTMLDivElement | null>(null)
  const msgsRef = useRef<StudentChatMessage[]>([])
  msgsRef.current = msgs

  const scrollToBottom = () => {
    const el = listRef.current
    if (el) el.scrollTop = el.scrollHeight
  }

  // Boshlang'ich yuklash
  useEffect(() => {
    let on = true
    getStudentChat()
      .then((m) => {
        if (!on) return
        setMsgs(m)
        setLoading(false)
      })
      .catch((e) => {
        if (!on) return
        setErr(e?.message || String(e))
        setLoading(false)
      })
    return () => {
      on = false
    }
  }, [])

  // Har 4 sekundda yangilarni so'rash
  useEffect(() => {
    const poll = setInterval(async () => {
      const cur = msgsRef.current
      if (!cur.length) return
      try {
        const since = cur[cur.length - 1].createdAt
        const fresh = await getStudentChat(since)
        if (fresh.length) {
          const known = new Set(cur.map((m) => m.id))
          const add = fresh.filter((m) => !known.has(m.id))
          if (add.length) setMsgs((prev) => [...prev, ...add])
        }
      } catch {
        /* jim — keyingi urinishda qayta */
      }
    }, 4000)
    return () => clearInterval(poll)
  }, [])

  // Yangi xabarlar kelganda pastga aylantirish
  useEffect(() => {
    scrollToBottom()
  }, [msgs])

  // Toast avtomatik yashirish
  useEffect(() => {
    if (!toast) return
    const t = setTimeout(() => setToast(null), 2500)
    return () => clearTimeout(t)
  }, [toast])

  const send = async () => {
    const t = text.trim()
    if (!t) return
    setText('')
    try {
      const m = await sendStudentChat(t)
      setMsgs((prev) => [...prev, m])
    } catch (e) {
      setText(t)
      setToast('Yuborilmadi: ' + ((e as Error)?.message || String(e)))
    }
  }

  const header = (
    <div
      className="row gap12"
      style={{ background: 'var(--surface)', borderBottom: '1px solid var(--border)', padding: '8px 16px 12px', flex: 'none' }}
    >
      <div
        style={{
          width: 44,
          height: 44,
          borderRadius: 14,
          background: 'var(--accentSoft)',
          display: 'flex',
          alignItems: 'center',
          justifyContent: 'center',
          flex: 'none',
        }}
      >
        <Icon name="chat" size={24} color="var(--accent)" />
      </div>
      <div>
        <div style={{ fontSize: 16.5, fontWeight: 800 }}>Guruh chati</div>
        <div className="muted" style={{ fontSize: 12.5 }}>
          O‘qituvchilar va ma’muriyat
        </div>
      </div>
    </div>
  )

  const composer = (
    <div
      className="row gap8"
      style={{ background: 'var(--surface)', borderTop: '1px solid var(--border)', padding: '10px 14px calc(12px + env(safe-area-inset-bottom))', flex: 'none' }}
    >
      <input
        value={text}
        onChange={(e) => setText(e.target.value)}
        onKeyDown={(e) => {
          if (e.key === 'Enter') {
            e.preventDefault()
            send()
          }
        }}
        placeholder="Xabar yozing…"
        style={{
          flex: 1,
          background: 'var(--surface2)',
          border: '1px solid var(--border)',
          borderRadius: 22,
          padding: '11px 16px',
          outline: 'none',
          fontSize: 15,
          fontWeight: 500,
        }}
      />
      <button
        className="press"
        onClick={send}
        style={{
          width: 44,
          height: 44,
          borderRadius: 14,
          background: 'var(--accent)',
          display: 'flex',
          alignItems: 'center',
          justifyContent: 'center',
          flex: 'none',
        }}
      >
        <Icon name="send" size={20} color="#fff" />
      </button>
    </div>
  )

  return (
    <div className="screen" style={{ overflowY: 'hidden' }}>
      {header}
      <div ref={listRef} className="scroll" style={{ flex: 1, padding: 14 }}>
        {loading ? (
          <div className="center">
            <div className="spin" />
          </div>
        ) : err ? (
          <Empty title="Yuklab bo'lmadi" sub={err} ic="alert" />
        ) : !msgs.length ? (
          <Empty title="Xabar yo'q" sub="Hozircha guruhda xabar yo'q." ic="chat" />
        ) : (
          msgs.map((m, i) => {
            const prevSame = i > 0 && msgs[i - 1].senderUserId === m.senderUserId
            return <Bubble key={m.id} m={m} mine={m.senderUserId === meId} prevSame={prevSame} />
          })
        )}
      </div>
      {composer}
      {toast && <div className="toast">{toast}</div>}
    </div>
  )
}

function Bubble({ m, mine, prevSame }: { m: StudentChatMessage; mine: boolean; prevSame: boolean }) {
  if (mine) {
    return (
      <div style={{ display: 'flex', justifyContent: 'flex-end', marginBottom: 3 }}>
        <div style={{ maxWidth: '78%' }}>
          <div
            style={{
              background: 'var(--accent)',
              color: '#fff',
              padding: '9px 13px',
              borderRadius: '16px 16px 5px 16px',
              fontSize: 14.5,
              lineHeight: 1.4,
              fontWeight: 500,
              wordBreak: 'break-word',
            }}
          >
            {m.text}
          </div>
          <div className="faint" style={{ fontSize: 10.5, textAlign: 'right', padding: '3px 4px 8px' }}>
            {fmtTime(m.createdAt)}
          </div>
        </div>
      </div>
    )
  }

  const rc = roleColor(m.senderRole)
  const rl = roleLabel(m.senderRole)
  return (
    <div style={{ display: 'flex', gap: 8, marginBottom: 3, maxWidth: '82%' }}>
      <div style={{ width: 32, flex: 'none' }}>
        {!prevSame && (
          <div
            className="subj"
            style={{
              width: 32,
              height: 32,
              borderRadius: '50%',
              background: 'linear-gradient(135deg,var(--accent),#5340c4)',
              color: '#fff',
              fontSize: 12,
            }}
          >
            {initials(m.senderName)}
          </div>
        )}
      </div>
      <div style={{ minWidth: 0 }}>
        {!prevSame && (
          <div className="row gap6" style={{ padding: '0 2px 3px' }}>
            <span style={{ fontSize: 12.5, fontWeight: 800, color: rc }}>{m.senderName}</span>
            {rl && (
              <span
                style={{
                  fontSize: 9.5,
                  fontWeight: 800,
                  color: rc,
                  background: `color-mix(in srgb,${rc} 14%,transparent)`,
                  padding: '1px 6px',
                  borderRadius: 6,
                }}
              >
                {rl}
              </span>
            )}
          </div>
        )}
        <div
          className="card"
          style={{ padding: '9px 13px', borderRadius: '5px 16px 16px 16px', fontSize: 14.5, lineHeight: 1.4, fontWeight: 500, wordBreak: 'break-word' }}
        >
          {m.text}
        </div>
        <div className="faint" style={{ fontSize: 10.5, padding: '3px 2px 8px' }}>
          {fmtTime(m.createdAt)}
        </div>
      </div>
    </div>
  )
}

function Empty({ title, sub, ic = 'sparkle' }: { title: string; sub?: string; ic?: string }) {
  return (
    <div className="empty">
      <div className="empty-ic">
        <Icon name={ic} size={30} />
      </div>
      <div style={{ fontSize: 16, fontWeight: 800 }}>{title}</div>
      {sub && (
        <div className="muted" style={{ fontSize: 13.5, marginTop: 6, lineHeight: 1.5 }}>
          {sub}
        </div>
      )}
    </div>
  )
}
