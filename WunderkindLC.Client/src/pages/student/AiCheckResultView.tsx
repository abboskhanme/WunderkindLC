import type { AiCheck, AiCheckIelts, SpeakingWord } from '@/types'
import { Ring, fmtDate } from '@/pages/student/lib'
import { mediaUrl } from '@/api/services/studentAiCheck'

/** IELTS band rangi (0-9). */
function bandColor(b: number): string {
  if (b >= 7) return '#16a34a'
  if (b >= 5.5) return '#2563eb'
  if (b >= 4) return '#f59e0b'
  return '#ef4444'
}

/** IELTS Writing band kartasi — 4 mezon + umumiy band. */
function IeltsCard({ ielts }: { ielts: AiCheckIelts }) {
  const task1 = ielts.taskType === 'ielts_task1'
  const rows: { label: string; value: number }[] = [
    { label: task1 ? 'Task Achievement' : 'Task Response', value: ielts.task },
    { label: 'Coherence & Cohesion', value: ielts.coherence },
    { label: 'Lexical Resource', value: ielts.lexical },
    { label: 'Grammatical Range & Accuracy', value: ielts.grammar },
  ]
  return (
    <div className="card" style={{ background: 'var(--accentSoft)' }}>
      <div className="row sp" style={{ alignItems: 'center', marginBottom: 10 }}>
        <div>
          <div style={{ fontSize: 13, fontWeight: 800 }}>IELTS {task1 ? 'Task 1' : 'Task 2'} — band</div>
          <div className="muted" style={{ fontSize: 11.5 }}>Rasmiy band deskriptorlari bo'yicha</div>
        </div>
        <div className="col" style={{ alignItems: 'center' }}>
          <div className="font-mono" style={{ fontSize: 34, fontWeight: 800, lineHeight: 1, color: bandColor(ielts.overall) }}>
            {ielts.overall.toFixed(1)}
          </div>
          <div className="muted" style={{ fontSize: 10 }}>Overall</div>
        </div>
      </div>
      <div className="col gap8">
        {rows.map((r) => (
          <div key={r.label} className="row sp" style={{ alignItems: 'center' }}>
            <span style={{ fontSize: 12.5 }}>{r.label}</span>
            <span
              className="font-mono"
              style={{
                fontSize: 13, fontWeight: 800, color: bandColor(r.value),
                background: '#fff', borderRadius: 8, padding: '2px 10px', minWidth: 40, textAlign: 'center',
              }}
            >
              {r.value.toFixed(1)}
            </span>
          </div>
        ))}
      </div>
    </div>
  )
}

/** Ball rangi (0-100). */
function scoreColor(v: number): string {
  if (v >= 80) return '#16a34a'
  if (v >= 60) return '#2563eb'
  if (v >= 40) return '#f59e0b'
  return '#ef4444'
}

/** So'z talaffuz rangi: yashil (yaxshi) / sarg'ish / qizil (xato). */
function wordColor(w: SpeakingWord): string {
  if (w.errorType && w.errorType !== 'None') return '#ef4444'
  if (w.accuracy >= 80) return '#16a34a'
  if (w.accuracy >= 60) return '#f59e0b'
  return '#ef4444'
}

/** Bitta so'z — talaffuz aniqligiga qarab rangli chip. */
function WordChip({ w }: { w: SpeakingWord }) {
  const omission = w.errorType === 'Omission'
  const insertion = w.errorType === 'Insertion'
  const color = wordColor(w)
  const acc = Math.round(w.accuracy)
  return (
    <span
      title={`${w.word} — ${acc}%${w.errorType && w.errorType !== 'None' ? ` (${w.errorType})` : ''}`}
      style={{
        display: 'inline-flex',
        alignItems: 'center',
        gap: 4,
        padding: '3px 8px',
        borderRadius: 8,
        background: color + '1a',
        border: `1px solid ${color}`,
        color,
        fontSize: 13.5,
        fontWeight: 700,
        opacity: omission ? 0.6 : 1,
        textDecoration: omission ? 'line-through' : 'none',
      }}
    >
      {w.word}
      {!omission && !insertion ? <span style={{ fontSize: 10, opacity: 0.8 }}>{acc}</span> : null}
      {omission ? <span style={{ fontSize: 9, fontWeight: 600 }}>tushib qoldi</span> : null}
      {insertion ? <span style={{ fontSize: 9, fontWeight: 600 }}>ortiqcha</span> : null}
    </span>
  )
}

/** So'z statistikasi: jami / yaxshi / xato. */
function WordStats({ words }: { words: SpeakingWord[] }) {
  const spoken = words.filter((w) => w.errorType !== 'Omission')
  const good = spoken.filter((w) => (!w.errorType || w.errorType === 'None') && w.accuracy >= 80).length
  const bad = spoken.length - good
  const Item = ({ label, value, color }: { label: string; value: number; color: string }) => (
    <div className="col" style={{ alignItems: 'center', flex: 1 }}>
      <div className="font-mono" style={{ fontSize: 20, fontWeight: 800, color }}>{value}</div>
      <div className="muted" style={{ fontSize: 11 }}>{label}</div>
    </div>
  )
  return (
    <div className="row" style={{ marginTop: 10, gap: 8 }}>
      <Item label="So'z" value={spoken.length} color="var(--accent)" />
      <Item label="Yaxshi" value={good} color="#16a34a" />
      <Item label="Xato/zaif" value={bad} color="#ef4444" />
    </div>
  )
}

/** Matnni so'z + bo'shliq bo'laklariga ajratadi (qayta tiklash mumkin bo'lsin). */
function tokenize(s: string): string[] {
  return s.match(/\s+|[^\s]+/g) ?? []
}
/** Solishtirish uchun normallashtirish: kichik harf + chetdagi tinish belgilarini olib tashlash. */
function normTok(t: string): string {
  if (/^\s+$/.test(t)) return ' '
  return t.toLowerCase().replace(/^[^\p{L}\p{N}]+|[^\p{L}\p{N}]+$/gu, '')
}

/**
 * Asl matn → yaxshilangan matn farqi: yaxshilangan tomonda QO'SHILGAN/O'ZGARTIRILGAN so'zlarni
 * sariq rangda ajratib ko'rsatadi (so'z darajasidagi LCS). O'zgarmagan so'zlar oddiy chiqadi.
 */
function ImprovedDiff({ original, improved }: { original: string; improved: string }) {
  const o = tokenize(original)
  const m = tokenize(improved)
  const no = o.map(normTok)
  const nm = m.map(normTok)
  const n = o.length
  const k = m.length

  // LCS DP (orqaga) — bo'sh normli (faqat tinish belgisi/bo'shliq) tokenlar mos kelmaydi.
  const dp: number[][] = Array.from({ length: n + 1 }, () => new Array(k + 1).fill(0))
  for (let i = n - 1; i >= 0; i--)
    for (let j = k - 1; j >= 0; j--)
      dp[i][j] = no[i] === nm[j] && no[i] !== '' && no[i] !== ' '
        ? dp[i + 1][j + 1] + 1
        : Math.max(dp[i + 1][j], dp[i][j + 1])

  // Backtrack — yaxshilangan tomonda qaysi tokenlar mos (o'zgarmagan).
  const matched = new Array(k).fill(false)
  let i = 0
  let j = 0
  while (i < n && j < k) {
    if (no[i] === nm[j] && no[i] !== '' && no[i] !== ' ') { matched[j] = true; i++; j++ }
    else if (dp[i + 1][j] >= dp[i][j + 1]) i++
    else j++
  }

  return (
    <div style={{ fontSize: 14, lineHeight: 1.7, whiteSpace: 'pre-wrap' }}>
      {m.map((tok, idx) => {
        const plain = /^\s+$/.test(tok) || normTok(tok) === '' || matched[idx]
        return plain ? (
          <span key={idx}>{tok}</span>
        ) : (
          <mark
            key={idx}
            style={{ background: '#fde68a', color: '#92400e', borderRadius: 4, padding: '0 2px', fontWeight: 600 }}
          >
            {tok}
          </mark>
        )
      })}
    </div>
  )
}

function Bar({ label, value }: { label: string; value: number }) {
  return (
    <div style={{ marginBottom: 8 }}>
      <div className="row sp" style={{ marginBottom: 3 }}>
        <span style={{ fontSize: 13, fontWeight: 700 }}>{label}</span>
        <span className="font-mono" style={{ fontSize: 13, fontWeight: 800, color: scoreColor(value) }}>{value}</span>
      </div>
      <div className="progress">
        <div style={{ width: `${Math.max(0, Math.min(100, value))}%`, background: scoreColor(value) }} />
      </div>
    </div>
  )
}

/**
 * AI tekshiruv natijasi — diagramma + tahlil. O'quvchi ekrani VA admin ko'rinishida
 * bir xil (admin sahifa `.student-app` ichida o'raydi). Speaking bo'lsa ovoz + Azure ballari.
 */
export function AiCheckResultView({ rec }: { rec: AiCheck }) {
  const a = rec.analysis
  const sp = rec.speech
  const isSpeaking = rec.type === 'speaking'
  // Yaxshilangan variantni solishtirish uchun asl manba: writing — o'quvchi matni, speaking — tanilgan nutq.
  const improvedSource = ((isSpeaking ? rec.recognizedText : rec.inputText) ?? '').trim()

  return (
    <div className="col gap12" style={{ padding: '4px 0' }}>
      {/* Umumiy ball */}
      <div className="card">
        <div className="row gap12">
          <Ring value={a?.overall ?? Math.round(rec.score)} size={84} stroke={8} color={scoreColor(a?.overall ?? rec.score)}>
            <div style={{ fontSize: 22, fontWeight: 800 }}>{a?.overall ?? Math.round(rec.score)}</div>
            <div className="faint" style={{ fontSize: 10 }}>/ 100</div>
          </Ring>
          <div className="col" style={{ gap: 4 }}>
            <span className="chip" style={{ background: 'var(--accentSoft)', color: 'var(--accent)', alignSelf: 'flex-start' }}>
              {isSpeaking ? '🎤 Speaking' : '✍️ Writing'}
            </span>
            {a?.level ? <div style={{ fontSize: 18, fontWeight: 800 }}>Daraja: {a.level}</div> : null}
            <div className="muted" style={{ fontSize: 12 }}>{fmtDate(rec.createdAt, true)}</div>
            {rec.prompt ? <div className="muted" style={{ fontSize: 12.5 }}>Mavzu: {rec.prompt}</div> : null}
          </div>
        </div>
      </div>

      {/* IELTS Writing band bahosi */}
      {a?.ielts ? <IeltsCard ielts={a.ielts} /> : null}

      {/* Speaking: ovozni qayta eshitish + Azure talaffuz ballari + per-so'z (yashil/qizil) */}
      {isSpeaking && (
        <div className="card">
          <div className="sh-title" style={{ marginBottom: 8 }}>Talaffuz (Azure)</div>
          {rec.audioUrl ? (
            <audio controls src={mediaUrl(rec.audioUrl)} style={{ width: '100%', marginBottom: 12 }} />
          ) : null}
          {sp ? (
            <>
              <Bar label="Talaffuz" value={Math.round(sp.pronScore)} />
              <Bar label="Aniqlik" value={Math.round(sp.accuracy)} />
              <Bar label="Ravonlik" value={Math.round(sp.fluency)} />
              <Bar label="To'liqlik" value={Math.round(sp.completeness)} />
              <Bar label="Ohang (prosody)" value={Math.round(sp.prosody)} />
              <WordStats words={sp.words} />
            </>
          ) : null}

          {/* Talaffuz bahosi kelmagan — ovoz tiniq bo'lmagani uchun bo'lishi mumkin */}
          {!sp ? (
            <div style={{ fontSize: 12.5, color: '#d97706', marginTop: 4, lineHeight: 1.5 }}>
              ⚠️ Talaffuz bahosi kelmadi — ovoz aniq tanilmadi. Balandroq va tiniqroq gapiring, mikrofon
              ruxsatini tekshiring. Quyida faqat tanilgan matn + AI tahlili.
            </div>
          ) : null}

          {/* Har so'z — yashil (yaxshi) / sarg'ish / qizil (xato) */}
          {sp && sp.words.length > 0 ? (
            <div style={{ marginTop: 10 }}>
              <div className="muted" style={{ fontSize: 12, marginBottom: 6 }}>
                So'zlar bo'yicha talaffuz (rang — aniqlik):
              </div>
              <div style={{ display: 'flex', flexWrap: 'wrap', gap: 6 }}>
                {sp.words.map((w, i) => <WordChip key={`${w.word}-${i}`} w={w} />)}
              </div>
            </div>
          ) : rec.recognizedText ? (
            <div style={{ marginTop: 8 }}>
              <div className="muted" style={{ fontSize: 12, marginBottom: 3 }}>Tanilgan matn:</div>
              <div style={{ fontSize: 14, lineHeight: 1.5 }}>{rec.recognizedText}</div>
            </div>
          ) : null}
        </div>
      )}

      {/* Writing: o'quvchi yozgan matn */}
      {!isSpeaking && rec.inputText ? (
        <div className="card">
          <div className="sh-title" style={{ marginBottom: 6 }}>Sizning matningiz</div>
          <div style={{ fontSize: 14, lineHeight: 1.6, whiteSpace: 'pre-wrap' }}>{rec.inputText}</div>
        </div>
      ) : null}

      {a ? (
        <>
          {/* Ballar diagramma */}
          <div className="card">
            <div className="sh-title" style={{ marginBottom: 10 }}>Baholar</div>
            <Bar label="Grammatika" value={a.scores.grammar} />
            <Bar label="So'z boyligi" value={a.scores.vocabulary} />
            <Bar label="Bog'lanish (coherence)" value={a.scores.coherence} />
            <Bar label="Mavzuga mosligi" value={a.scores.task} />
            {!isSpeaking ? <Bar label="Imlo/punktuatsiya" value={a.scores.mechanics} /> : null}
          </div>

          {/* Xulosa */}
          {a.summary ? (
            <div className="card">
              <div className="sh-title" style={{ marginBottom: 6 }}>Umumiy xulosa</div>
              <div style={{ fontSize: 14, lineHeight: 1.6 }}>{a.summary}</div>
            </div>
          ) : null}

          {/* Kuchli / zaif tomonlar */}
          {(a.strengths.length > 0 || a.weaknesses.length > 0) && (
            <div className="card">
              {a.strengths.length > 0 && (
                <>
                  <div className="sh-title" style={{ marginBottom: 6, color: '#16a34a' }}>✓ Kuchli tomonlar</div>
                  <ul style={{ margin: '0 0 10px', paddingLeft: 18, fontSize: 13.5, lineHeight: 1.6 }}>
                    {a.strengths.map((s, i) => <li key={i}>{s}</li>)}
                  </ul>
                </>
              )}
              {a.weaknesses.length > 0 && (
                <>
                  <div className="sh-title" style={{ marginBottom: 6, color: '#ef4444' }}>△ Yaxshilash kerak</div>
                  <ul style={{ margin: 0, paddingLeft: 18, fontSize: 13.5, lineHeight: 1.6 }}>
                    {a.weaknesses.map((s, i) => <li key={i}>{s}</li>)}
                  </ul>
                </>
              )}
            </div>
          )}

          {/* Tuzatishlar */}
          {a.corrections.length > 0 && (
            <div className="card">
              <div className="sh-title" style={{ marginBottom: 8 }}>Tuzatishlar</div>
              <div className="col gap10">
                {a.corrections.map((c, i) => (
                  <div key={i} style={{ borderLeft: '3px solid var(--accent)', paddingLeft: 10 }}>
                    <div style={{ fontSize: 13.5 }}>
                      <span style={{ textDecoration: 'line-through', color: '#ef4444' }}>{c.original}</span>
                      {' → '}
                      <span style={{ color: '#16a34a', fontWeight: 700 }}>{c.suggestion}</span>
                    </div>
                    {c.explanation ? <div className="muted" style={{ fontSize: 12.5, marginTop: 2 }}>{c.explanation}</div> : null}
                  </div>
                ))}
              </div>
            </div>
          )}

          {/* So'z tahlili */}
          {a.vocabulary.length > 0 && (
            <div className="card">
              <div className="sh-title" style={{ marginBottom: 8 }}>So'z boyligi tavsiyalari</div>
              <div className="col gap8">
                {a.vocabulary.map((v, i) => (
                  <div key={i} className="row gap8" style={{ alignItems: 'flex-start' }}>
                    <span className="chip" style={{ background: 'var(--surface3)' }}>{v.word}</span>
                    <div style={{ flex: 1 }}>
                      {v.suggestion ? <span style={{ fontSize: 13.5, fontWeight: 700, color: 'var(--accent)' }}>{v.suggestion}</span> : null}
                      {v.note ? <div className="muted" style={{ fontSize: 12.5 }}>{v.note}</div> : null}
                    </div>
                  </div>
                ))}
              </div>
            </div>
          )}

          {/* Yaxshilangan variant — o'zgargan joylar sariq rangda ajratiladi */}
          {a.improved ? (
            <div className="card">
              <div className="sh-title" style={{ marginBottom: 6 }}>Yaxshilangan variant</div>
              {improvedSource ? (
                <>
                  <div className="muted" style={{ fontSize: 12, marginBottom: 8 }}>
                    <mark style={{ background: '#fde68a', color: '#92400e', borderRadius: 4, padding: '0 4px', fontWeight: 600 }}>
                      sariq
                    </mark>{' '}
                    — o'zgartirilgan yoki qo'shilgan joylar
                  </div>
                  <ImprovedDiff original={improvedSource} improved={a.improved} />
                </>
              ) : (
                <div style={{ fontSize: 14, lineHeight: 1.6, whiteSpace: 'pre-wrap' }}>{a.improved}</div>
              )}
            </div>
          ) : null}

          {/* Tavsiyalar */}
          {a.recommendations.length > 0 && (
            <div className="card">
              <div className="sh-title" style={{ marginBottom: 6 }}>Tavsiyalar</div>
              <ul style={{ margin: 0, paddingLeft: 18, fontSize: 13.5, lineHeight: 1.6 }}>
                {a.recommendations.map((s, i) => <li key={i}>{s}</li>)}
              </ul>
            </div>
          )}
        </>
      ) : (
        <div className="card">
          <div className="muted" style={{ fontSize: 13.5 }}>
            AI matn tahlili mavjud emas{isSpeaking ? ' (faqat talaffuz bahosi)' : ''}.
          </div>
        </div>
      )}
    </div>
  )
}
