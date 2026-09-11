import { useEffect, useState } from 'react'
import { useNavigate, useParams } from 'react-router-dom'
import {
  ArrowLeft, Plus, Trash2, Copy, Check, ExternalLink, Link2, GripVertical, Save, ListChecks, Users,
  BarChart3, Zap, Wallet,
} from 'lucide-react'
import type { LevelTestDetail, LevelTestSubmission, Subject } from '@/types'
import {
  getLevelTest, updateLevelTest, getLevelTestSubmissions,
  getLevelTestStats, type LevelTestStats,
  getLevelTestInvites, type LevelTestInvite,
} from '@/api/services/levelTests'
import { getSubjects } from '@/api/services/subjects'
import { Card } from '@/components/ui/Card'
import { Button } from '@/components/ui/Button'
import { Loader } from '@/components/ui/Loader'
import { Input, Select, Textarea } from '@/components/ui/Input'
import { PageHeader } from '@/components/ui/PageHeader'
import { LeadStageChip } from '@/components/leads/LeadStageChip'
import { cn, formatDate, formatMoney, apiErrorMessage } from '@/lib/utils'

interface QState {
  id?: string
  text: string
  options: string[]
  correctIndex: number
  kind: 'question' | 'survey'
  multiple: boolean
}
interface BState { id?: string; label: string; minPercent: number }

function testUrl(slug: string) {
  return `${window.location.origin}/test/${slug}`
}

export function LevelTestEditorPage() {
  const { id = '' } = useParams()
  const navigate = useNavigate()

  const [loading, setLoading] = useState(true)
  const [saving, setSaving] = useState(false)
  const [detail, setDetail] = useState<LevelTestDetail | null>(null)
  const [subjects, setSubjects] = useState<Subject[]>([])
  const [tab, setTab] = useState<'edit' | 'results' | 'stats'>('edit')
  const [copied, setCopied] = useState(false)
  const [stats, setStats] = useState<LevelTestStats | null>(null)

  // Forma holati
  const [title, setTitle] = useState('')
  const [courseId, setCourseId] = useState('')
  const [intro, setIntro] = useState('')
  const [isActive, setIsActive] = useState(true)
  const [questions, setQuestions] = useState<QState[]>([])
  const [bands, setBands] = useState<BState[]>([])

  // Natijalar
  const [subs, setSubs] = useState<LevelTestSubmission[] | null>(null)
  const [invites, setInvites] = useState<LevelTestInvite[] | null>(null)

  useEffect(() => {
    Promise.all([getLevelTest(id), getSubjects()])
      .then(([d, s]) => {
        setDetail(d)
        setSubjects(s)
        setTitle(d.title)
        setCourseId(d.courseId)
        setIntro(d.intro)
        setIsActive(d.isActive)
        setQuestions(d.questions.map((q) => ({ id: q.id, text: q.text, options: q.options, correctIndex: q.correctIndex, kind: q.kind, multiple: q.multiple })))
        setBands(d.bands.map((b) => ({ id: b.id, label: b.label, minPercent: b.minPercent })))
      })
      .catch(() => setDetail(null))
      .finally(() => setLoading(false))
  }, [id])

  useEffect(() => {
    if (tab === 'results' && subs === null) getLevelTestSubmissions(id).then(setSubs)
    if (tab === 'stats' && stats === null) getLevelTestStats(id).then(setStats)
    if (tab === 'stats' && invites === null) getLevelTestInvites(id).then(setInvites).catch(() => setInvites([]))
  }, [tab, subs, stats, invites, id])

  const copy = async () => {
    if (!detail) return
    try {
      await navigator.clipboard.writeText(testUrl(detail.slug))
      setCopied(true)
      setTimeout(() => setCopied(false), 1600)
    } catch {
      /* jim */
    }
  }

  // ---- Savollar ----
  const addQuestion = () =>
    setQuestions((qs) => [...qs, { text: '', options: ['', ''], correctIndex: 0, kind: 'question', multiple: false }])
  const addSurvey = () =>
    setQuestions((qs) => [...qs, { text: '', options: ['', ''], correctIndex: 0, kind: 'survey', multiple: true }])
  const removeQuestion = (i: number) => setQuestions((qs) => qs.filter((_, x) => x !== i))
  const patchQuestion = (i: number, patch: Partial<QState>) =>
    setQuestions((qs) => qs.map((q, x) => (x === i ? { ...q, ...patch } : q)))
  const addOption = (i: number) =>
    setQuestions((qs) => qs.map((q, x) => (x === i ? { ...q, options: [...q.options, ''] } : q)))
  const setOption = (i: number, oi: number, val: string) =>
    setQuestions((qs) =>
      qs.map((q, x) => (x === i ? { ...q, options: q.options.map((o, y) => (y === oi ? val : o)) } : q)),
    )
  const removeOption = (i: number, oi: number) =>
    setQuestions((qs) =>
      qs.map((q, x) => {
        if (x !== i) return q
        const options = q.options.filter((_, y) => y !== oi)
        let correctIndex = q.correctIndex
        if (oi === correctIndex) correctIndex = 0
        else if (oi < correctIndex) correctIndex -= 1
        return { ...q, options, correctIndex: Math.max(0, correctIndex) }
      }),
    )

  // ---- Daraja diapazonlari ----
  const addBand = () => setBands((bs) => [...bs, { label: '', minPercent: 0 }])
  const removeBand = (i: number) => setBands((bs) => bs.filter((_, x) => x !== i))
  const patchBand = (i: number, patch: Partial<BState>) =>
    setBands((bs) => bs.map((b, x) => (x === i ? { ...b, ...patch } : b)))

  const handleSave = async () => {
    if (!detail || saving) return
    setSaving(true)
    try {
      const updated = await updateLevelTest(id, {
        title: title.trim(),
        courseId,
        intro: intro.trim(),
        isActive,
        questions: questions
          .map((q) => ({
            id: q.id,
            text: q.text.trim(),
            options: q.options.map((o) => o.trim()).filter((o) => o.length > 0),
            correctIndex: q.correctIndex,
            kind: q.kind,
            multiple: q.multiple,
          }))
          .filter((q) => q.text.length > 0 && q.options.length >= 2),
        bands: bands
          .map((b) => ({ id: b.id, label: b.label.trim(), minPercent: Math.max(0, Math.min(100, b.minPercent || 0)) }))
          .filter((b) => b.label.length > 0),
      })
      setDetail(updated)
      // Tozalangan/qayta tartiblangan ma'lumotni qaytadan yuklaymiz
      setQuestions(updated.questions.map((q) => ({ id: q.id, text: q.text, options: q.options, correctIndex: q.correctIndex, kind: q.kind, multiple: q.multiple })))
      setBands(updated.bands.map((b) => ({ id: b.id, label: b.label, minPercent: b.minPercent })))
    } catch (err) {
      alert(apiErrorMessage(err, "Saqlab bo'lmadi"))
    } finally {
      setSaving(false)
    }
  }

  if (loading) return <Loader label="Yuklanmoqda..." />
  if (!detail)
    return (
      <Card>
        <p className="py-10 text-center text-slate-400">Test topilmadi.</p>
      </Card>
    )

  return (
    <div>
      <PageHeader
        title={
          <span className="flex items-center gap-2">
            <button
              onClick={() => navigate('/admin/level-tests')}
              className="rounded-lg border border-slate-200 p-1.5 text-slate-400 transition-colors hover:bg-slate-50 hover:text-slate-600"
              title="Orqaga"
            >
              <ArrowLeft className="h-5 w-5" />
            </button>
            {detail.title || 'Daraja testi'}
          </span>
        }
        sub={detail.courseName ? `Kurs: ${detail.courseName}` : 'Kurs tanlanmagan'}
        actions={
          tab === 'edit' ? (
            <Button onClick={handleSave} disabled={saving}>
              <Save className="h-4 w-4" /> {saving ? 'Saqlanmoqda...' : 'Saqlash'}
            </Button>
          ) : undefined
        }
      />

      {/* Ommaviy havola */}
      <div className="mb-4 flex flex-wrap items-center gap-2 rounded-xl border border-brand-100 bg-brand-50/50 px-3 py-2.5">
        <Link2 className="h-4 w-4 shrink-0 text-brand-500" />
        <span className="flex-1 truncate font-mono text-sm text-slate-600">{testUrl(detail.slug)}</span>
        <button
          onClick={copy}
          className="inline-flex items-center gap-1.5 rounded-lg bg-white px-2.5 py-1.5 text-xs font-medium text-slate-600 shadow-sm transition-colors hover:text-brand-600"
        >
          {copied ? <Check className="h-3.5 w-3.5 text-emerald-600" /> : <Copy className="h-3.5 w-3.5" />}
          {copied ? 'Nusxalandi' : 'Nusxalash'}
        </button>
        <a
          href={testUrl(detail.slug)}
          target="_blank"
          rel="noreferrer"
          className="inline-flex items-center gap-1.5 rounded-lg bg-white px-2.5 py-1.5 text-xs font-medium text-slate-600 shadow-sm transition-colors hover:text-brand-600"
        >
          <ExternalLink className="h-3.5 w-3.5" /> Ochish
        </a>
      </div>

      {/* Tablar */}
      <div className="mb-4 flex gap-1 rounded-lg bg-slate-100 p-1 text-sm">
        <button
          onClick={() => setTab('edit')}
          className={cn(
            'flex flex-1 items-center justify-center gap-1.5 rounded-md py-1.5 font-medium transition-colors',
            tab === 'edit' ? 'bg-white text-slate-800 shadow-sm' : 'text-slate-500 hover:text-slate-700',
          )}
        >
          <ListChecks className="h-4 w-4" /> Tahrirlash
        </button>
        <button
          onClick={() => setTab('results')}
          className={cn(
            'flex flex-1 items-center justify-center gap-1.5 rounded-md py-1.5 font-medium transition-colors',
            tab === 'results' ? 'bg-white text-slate-800 shadow-sm' : 'text-slate-500 hover:text-slate-700',
          )}
        >
          <Users className="h-4 w-4" /> Natijalar
        </button>
        <button
          onClick={() => setTab('stats')}
          className={cn(
            'flex flex-1 items-center justify-center gap-1.5 rounded-md py-1.5 font-medium transition-colors',
            tab === 'stats' ? 'bg-white text-slate-800 shadow-sm' : 'text-slate-500 hover:text-slate-700',
          )}
        >
          <BarChart3 className="h-4 w-4" /> Statistika
        </button>
      </div>

      {tab === 'edit' ? (
        <div className="space-y-4">
          {/* Asosiy */}
          <Card title="Asosiy ma'lumot">
            <div className="grid gap-3 sm:grid-cols-2">
              <Input label="Test nomi" value={title} onChange={(e) => setTitle(e.target.value)} />
              <Select label="Kurs" value={courseId} onChange={(e) => setCourseId(e.target.value)}>
                <option value="">— Kurs tanlanmagan —</option>
                {subjects.map((s) => (
                  <option key={s.id} value={s.id}>
                    {s.name}
                  </option>
                ))}
              </Select>
            </div>
            <Textarea
              label="Kirish matni (test boshida ko'rinadi)"
              className="mt-3"
              rows={2}
              value={intro}
              onChange={(e) => setIntro(e.target.value)}
              placeholder="Masalan: Bu test darajangizni aniqlaydi. Diqqat bilan javob bering."
            />
            <label className="mt-3 flex cursor-pointer items-center gap-2 text-sm text-slate-600">
              <input
                type="checkbox"
                checked={isActive}
                onChange={(e) => setIsActive(e.target.checked)}
                className="h-4 w-4 rounded border-slate-300 text-brand-600 focus:ring-brand-500"
              />
              Faol (ommaviy havola orqali ochiladi)
            </label>
          </Card>

          {/* Elementlar: savol (baholanadi) + so'rovnoma (checkbox, baholanmaydi) */}
          <Card
            title={`Elementlar (${questions.length})`}
            sub="Savol — to'g'ri javobli (baholanadi). So'rovnoma — checkbox, to'g'ri javobsiz (lid ma'lumoti)."
            actions={
              <div className="flex gap-2">
                <Button variant="secondary" onClick={addQuestion}>
                  <Plus className="h-4 w-4" /> Savol
                </Button>
                <Button variant="secondary" onClick={addSurvey}>
                  <Plus className="h-4 w-4" /> So'rovnoma
                </Button>
              </div>
            }
          >
            {questions.length === 0 ? (
              <p className="py-6 text-center text-sm text-slate-400">Element yo'q. "Savol" yoki "So'rovnoma" tugmasini bosing.</p>
            ) : (
              <div className="space-y-4">
                {questions.map((q, i) => (
                  <div key={i} className="rounded-xl border border-slate-200 p-3">
                    <div className="flex items-start gap-2">
                      <span className="mt-2 flex h-6 w-6 shrink-0 items-center justify-center rounded-full bg-slate-100 text-xs font-semibold text-slate-500">
                        {i + 1}
                      </span>
                      <textarea
                        rows={1}
                        value={q.text}
                        onChange={(e) => patchQuestion(i, { text: e.target.value })}
                        placeholder="Savol matni..."
                        className="min-h-[40px] flex-1 resize-y rounded-lg border border-slate-200 px-3 py-2 text-sm text-slate-800 outline-none focus:border-brand-400"
                      />
                      <button
                        onClick={() => removeQuestion(i)}
                        title="O'chirish"
                        className="mt-1 rounded-lg p-1.5 text-slate-300 transition-colors hover:bg-red-50 hover:text-red-600"
                      >
                        <Trash2 className="h-4 w-4" />
                      </button>
                    </div>

                    <div className="mt-2 flex flex-wrap items-center gap-2 pl-8">
                      <span
                        className={cn(
                          'rounded px-2 py-0.5 text-[11px] font-semibold',
                          q.kind === 'survey' ? 'bg-amber-50 text-amber-700' : 'bg-emerald-50 text-emerald-700',
                        )}
                      >
                        {q.kind === 'survey' ? "So'rovnoma" : 'Savol'}
                      </span>
                      {q.kind === 'survey' ? (
                        <label className="flex cursor-pointer items-center gap-1.5 text-xs text-slate-500">
                          <input
                            type="checkbox"
                            checked={q.multiple}
                            onChange={(e) => patchQuestion(i, { multiple: e.target.checked })}
                            className="h-3.5 w-3.5 rounded border-slate-300 text-brand-600 focus:ring-brand-500"
                          />
                          Ko'p tanlash (checkbox)
                        </label>
                      ) : (
                        <span className="text-[11px] text-slate-400">To'g'ri javobni belgilang (yashil doira)</span>
                      )}
                    </div>

                    <div className="mt-2 space-y-1.5 pl-8">
                      {q.options.map((opt, oi) => (
                        <div key={oi} className="flex items-center gap-2">
                          {q.kind === 'question' ? (
                            <button
                              type="button"
                              onClick={() => patchQuestion(i, { correctIndex: oi })}
                              title="To'g'ri javob"
                              className={cn(
                                'flex h-5 w-5 shrink-0 items-center justify-center rounded-full border-2 transition-colors',
                                q.correctIndex === oi
                                  ? 'border-emerald-500 bg-emerald-500 text-white'
                                  : 'border-slate-300 text-transparent hover:border-emerald-400',
                              )}
                            >
                              <Check className="h-3 w-3" />
                            </button>
                          ) : (
                            <span
                              title="So'rovnoma varianti (to'g'ri javob yo'q)"
                              className={cn(
                                'h-4 w-4 shrink-0 border-2 border-slate-300',
                                q.multiple ? 'rounded' : 'rounded-full',
                              )}
                            />
                          )}
                          <input
                            value={opt}
                            onChange={(e) => setOption(i, oi, e.target.value)}
                            placeholder={`Variant ${oi + 1}`}
                            className="flex-1 rounded-lg border border-slate-200 px-2.5 py-1.5 text-sm text-slate-700 outline-none focus:border-brand-400"
                          />
                          {q.options.length > 2 && (
                            <button
                              onClick={() => removeOption(i, oi)}
                              title="Variantni o'chirish"
                              className="rounded p-1 text-slate-300 transition-colors hover:text-red-500"
                            >
                              <Trash2 className="h-3.5 w-3.5" />
                            </button>
                          )}
                        </div>
                      ))}
                      <button
                        onClick={() => addOption(i)}
                        className="inline-flex items-center gap-1 text-xs font-medium text-brand-600 hover:text-brand-700"
                      >
                        <Plus className="h-3.5 w-3.5" /> Variant qo'shish
                      </button>
                    </div>
                  </div>
                ))}
              </div>
            )}
          </Card>

          {/* Daraja diapazonlari */}
          <Card
            title="Daraja diapazonlari"
            sub="Ball foiziga qarab daraja aniqlanadi (foiz ≥ minimal foiz bo'lgan eng yuqori daraja)"
            actions={
              <Button variant="secondary" onClick={addBand}>
                <Plus className="h-4 w-4" /> Daraja
              </Button>
            }
          >
            {bands.length === 0 ? (
              <p className="py-6 text-center text-sm text-slate-400">Daraja yo'q — natija faqat ball bo'ladi.</p>
            ) : (
              <div className="space-y-2">
                {[...bands]
                  .map((b, i) => ({ b, i }))
                  .sort((a, z) => a.b.minPercent - z.b.minPercent)
                  .map(({ b, i }) => (
                    <div key={b.id ?? i} className="flex items-center gap-2">
                      <GripVertical className="h-4 w-4 shrink-0 text-slate-300" />
                      <input
                        value={b.label}
                        onChange={(e) => patchBand(i, { label: e.target.value })}
                        placeholder="Daraja nomi (masalan B1)"
                        className="flex-1 rounded-lg border border-slate-200 px-2.5 py-1.5 text-sm text-slate-700 outline-none focus:border-brand-400"
                      />
                      <div className="flex items-center gap-1">
                        <span className="text-xs text-slate-400">≥</span>
                        <input
                          type="number"
                          min={0}
                          max={100}
                          value={b.minPercent}
                          onChange={(e) => patchBand(i, { minPercent: Number(e.target.value) })}
                          className="w-16 rounded-lg border border-slate-200 px-2 py-1.5 text-center font-mono text-sm text-slate-700 outline-none focus:border-brand-400"
                        />
                        <span className="text-xs text-slate-400">%</span>
                      </div>
                      <button
                        onClick={() => removeBand(i)}
                        className="rounded p-1.5 text-slate-300 transition-colors hover:bg-red-50 hover:text-red-600"
                      >
                        <Trash2 className="h-4 w-4" />
                      </button>
                    </div>
                  ))}
              </div>
            )}
          </Card>

          <div className="flex justify-end">
            <Button onClick={handleSave} disabled={saving}>
              <Save className="h-4 w-4" /> {saving ? 'Saqlanmoqda...' : 'Saqlash'}
            </Button>
          </div>
        </div>
      ) : tab === 'results' ? (
        <Card title="Testni topshirganlar" sub="Har bir topshiruvchi CRM Lidlar bo'limida yangi lid bo'lib turadi">
          {subs === null ? (
            <Loader label="Yuklanmoqda..." />
          ) : subs.length === 0 ? (
            <p className="py-8 text-center text-sm text-slate-400">Hali hech kim topshirmagan.</p>
          ) : (
            <div className="overflow-x-auto">
              <table className="w-full text-left text-sm">
                <thead className="border-b border-slate-100 text-xs uppercase tracking-wide text-slate-400">
                  <tr>
                    <th className="px-3 py-2">F.I.SH</th>
                    <th className="px-3 py-2">Telefon</th>
                    <th className="px-3 py-2 text-center">Yosh</th>
                    <th className="px-3 py-2 text-center">Ball</th>
                    <th className="px-3 py-2 text-center">Foiz</th>
                    <th className="px-3 py-2">Daraja</th>
                    <th className="px-3 py-2">Sana</th>
                    <th className="px-3 py-2 text-right">Lid</th>
                  </tr>
                </thead>
                <tbody className="divide-y divide-slate-50">
                  {subs.map((s) => (
                    <tr key={s.id} className="hover:bg-slate-50/60">
                      <td className="px-3 py-2 font-medium text-slate-700">
                        {s.fullName}
                        {s.survey?.length > 0 && (
                          <div className="mt-1 space-y-0.5">
                            {s.survey.map((sv, k) => (
                              <div key={k} className="text-[11px] font-normal text-slate-400">
                                <span className="text-slate-500">{sv.question}:</span>{' '}
                                {sv.answers.length ? sv.answers.join(', ') : '—'}
                              </div>
                            ))}
                          </div>
                        )}
                      </td>
                      <td className="px-3 py-2 font-mono text-slate-500">{s.phone}</td>
                      <td className="px-3 py-2 text-center text-slate-500">{s.age || '—'}</td>
                      <td className="px-3 py-2 text-center font-mono text-slate-700">
                        {s.score}/{s.total}
                      </td>
                      <td className="px-3 py-2 text-center font-mono text-slate-700">{s.percent}%</td>
                      <td className="px-3 py-2">
                        {s.level ? (
                          <span className="rounded-md bg-brand-50 px-2 py-0.5 text-xs font-semibold text-brand-700">
                            {s.level}
                          </span>
                        ) : (
                          '—'
                        )}
                      </td>
                      <td className="px-3 py-2 text-slate-500">{formatDate(s.createdAt)}</td>
                      <td className="px-3 py-2 text-right">
                        <button
                          onClick={() => navigate('/admin/leads')}
                          className="text-xs font-medium text-brand-600 hover:text-brand-700"
                        >
                          Lidlarda →
                        </button>
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          )}
        </Card>
      ) : (
        <StatsPanel stats={stats} invites={invites} />
      )}
    </div>
  )
}

// ============================ Statistika paneli ============================

function StatsPanel({ stats, invites }: { stats: LevelTestStats | null; invites: LevelTestInvite[] | null }) {
  if (!stats) return <Card><Loader label="Yuklanmoqda..." /></Card>
  if (stats.total === 0 && (invites?.length ?? 0) === 0)
    return <Card><p className="py-8 text-center text-sm text-slate-400">Hali hech kim topshirmagan.</p></Card>

  // ⚠️ Maxraj — TAKRORSIZ lidlar (`leads`), topshiriqlar soni EMAS: "aktiv"/"to'ladi" ham
  // takrorsiz sanaladi (bir odam testni ikki marta topshirishi mumkin). Ikkalasini aralashtirsak
  // foiz 100 dan oshib ketardi. `leads` bo'sh bo'lsa (eski javob) topshiriqlar soniga qaytamiz.
  const denom = stats.leads > 0 ? stats.leads : stats.total
  const pct = (n: number) => (denom > 0 ? Math.round((n / denom) * 100) : 0)
  const KPI = [
    {
      label: 'Topshirgan', value: stats.total,
      sub: stats.leads > 0 && stats.leads !== stats.total ? `${stats.leads} ta lid` : 'jami lid',
      icon: Users, color: 'text-slate-600', bg: 'bg-slate-100',
    },
    { label: 'Aktiv o’quvchi', value: stats.active, sub: `${pct(stats.active)}%`, icon: Zap, color: 'text-emerald-600', bg: 'bg-emerald-50' },
    // SOTUV: "aktiv o'quvchi" hali pul degani emas — pul to'laganlar alohida ko'rsatiladi.
    {
      label: "To'lov qildi", value: stats.paid,
      sub: stats.revenue > 0 ? `${formatMoney(stats.revenue)} so'm` : `${pct(stats.paid)}%`,
      icon: Wallet, color: 'text-teal-600', bg: 'bg-teal-50',
    },
  ]

  const Dot = ({ on }: { on: boolean }) =>
    on ? <Check className="mx-auto h-4 w-4 text-emerald-600" /> : <span className="text-slate-300">—</span>

  return (
    <div className="space-y-4">
      {/* KPI */}
      <div className="grid grid-cols-2 gap-3 sm:grid-cols-3">
        {KPI.map((k) => {
          const KIcon = k.icon
          return (
            <Card key={k.label} className="text-center">
              <div className={cn('mx-auto mb-2 flex h-9 w-9 items-center justify-center rounded-lg', k.bg, k.color)}>
                <KIcon className="h-5 w-5" />
              </div>
              <div className="font-mono text-2xl font-bold text-slate-800">{k.value}</div>
              <div className="text-[13px] font-semibold text-slate-700">{k.label}</div>
              <div className="text-xs text-slate-400">{k.sub}</div>
            </Card>
          )
        })}
      </div>

      {/* Jadval */}
      <Card
        title="Topshiruvchilar"
        sub="Lid qaysi bosqichda, pul to'ladimi va aktiv o'quvchi bo'ldimi — guruhi va o'qituvchisi bilan"
      >
        <div className="overflow-x-auto">
          <table className="w-full text-left text-sm">
            <thead className="border-b border-slate-100 text-xs uppercase tracking-wide text-slate-400">
              <tr>
                <th className="px-3 py-2">F.I.SH</th>
                <th className="px-3 py-2">Telefon</th>
                <th className="px-3 py-2">Daraja</th>
                <th className="px-3 py-2 text-center">Foiz</th>
                <th className="px-3 py-2">Bosqich</th>
                <th className="px-3 py-2 text-right">To'lov</th>
                <th className="px-3 py-2 text-center">Aktiv</th>
                <th className="px-3 py-2">Guruh</th>
                <th className="px-3 py-2">O'qituvchi</th>
              </tr>
            </thead>
            <tbody className="divide-y divide-slate-50">
              {stats.rows.map((r) => (
                <tr
                  key={r.submissionId}
                  className={cn(
                    'hover:bg-slate-50/60',
                    r.isDeleted && 'bg-red-50/40 line-through',
                  )}
                >
                  <td className={cn('px-3 py-2 font-medium text-slate-700', r.isDeleted && 'text-red-600')}>
                    {r.fullName}
                    {r.isDeleted && <span className="ml-2 text-[11px] font-normal text-red-500">(o'chirilgan)</span>}
                  </td>
                  <td className="px-3 py-2 font-mono text-slate-500">{r.phone}</td>
                  <td className="px-3 py-2">
                    {r.level ? (
                      <span className="rounded-md bg-brand-50 px-2 py-0.5 text-xs font-semibold text-brand-700">{r.level}</span>
                    ) : '—'}
                  </td>
                  <td className="px-3 py-2 text-center font-mono text-slate-600">{r.percent}%</td>
                  <td className="px-3 py-2">
                    <LeadStageChip title={r.stageTitle} color={r.stageColor} />
                  </td>
                  <td className="px-3 py-2 text-right">
                    {r.paid ? (
                      <span className="whitespace-nowrap font-mono text-xs font-semibold text-teal-700">
                        {formatMoney(r.paidTotal)}
                        {r.firstPaidAt && (
                          <span className="block font-sans text-[10px] font-normal text-slate-400">
                            {formatDate(r.firstPaidAt)}
                          </span>
                        )}
                      </span>
                    ) : (
                      <span className="text-slate-300">—</span>
                    )}
                  </td>
                  <td className="px-3 py-2 text-center"><Dot on={r.active} /></td>
                  <td className="px-3 py-2 text-slate-600">{r.groupName || <span className="text-slate-300">—</span>}</td>
                  <td className="px-3 py-2 text-slate-600">{r.teacherName || <span className="text-slate-300">—</span>}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </Card>

      {/* Yuborilgan bir martalik havolalar — SMS holati + ishlangani */}
      {invites && invites.length > 0 && (
        <Card title="Yuborilgan havolalar (SMS)" sub="Lidlarga yuborilgan bir martalik daraja-test havolalari">
          <div className="overflow-x-auto">
            <table className="w-full text-left text-sm">
              <thead className="border-b border-slate-100 text-xs uppercase tracking-wide text-slate-400">
                <tr>
                  <th className="px-3 py-2">Lid</th>
                  <th className="px-3 py-2">Telefon</th>
                  <th className="px-3 py-2 text-center">SMS</th>
                  <th className="px-3 py-2 text-center">Ishlangan</th>
                  <th className="px-3 py-2 text-center">Natija</th>
                  <th className="px-3 py-2">Yuborilgan</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-slate-50">
                {invites.map((i) => (
                  <tr key={i.id} className="hover:bg-slate-50/60">
                    <td className="px-3 py-2 font-medium text-slate-700">{i.leadName}</td>
                    <td className="px-3 py-2 font-mono text-slate-500">{i.phone}</td>
                    <td className="px-3 py-2 text-center">
                      {i.smsStatus === 'sent' ? (
                        <span className="rounded-md bg-emerald-50 px-2 py-0.5 text-xs font-semibold text-emerald-700">Yuborilgan</span>
                      ) : (
                        <span className="rounded-md bg-amber-50 px-2 py-0.5 text-xs font-semibold text-amber-700" title={i.smsStatus}>Xato</span>
                      )}
                    </td>
                    <td className="px-3 py-2 text-center">
                      {i.used ? <Check className="mx-auto h-4 w-4 text-emerald-600" /> : <span className="text-slate-300">—</span>}
                    </td>
                    <td className="px-3 py-2 text-center font-mono text-slate-600">
                      {i.used ? `${i.percent}%${i.level ? ` · ${i.level}` : ''}` : '—'}
                    </td>
                    <td className="px-3 py-2 text-slate-500">{formatDate(i.createdAt)}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </Card>
      )}
    </div>
  )
}
