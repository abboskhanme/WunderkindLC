import { useEffect, useMemo, useState } from 'react'
import {
  Plus, Pencil, Trash2, ClipboardList, ChevronRight, AlertTriangle, Loader2,
  Bot, FileText, Upload, Clock, Send, Users, KeyRound, Copy, Globe, Award,
} from 'lucide-react'
import type { GroupTest } from '@/types'
import { getGroupTests, createTest, updateTest, deleteTest } from '@/api/services/testResults'
import { getCertificateTemplates } from '@/api/services/testCertificates'
import type { TestCertificateTemplate } from '@/api/services/testCertificates'
import { uploadAdminFile } from '@/api/services/students'
import { Card } from '@/components/ui/Card'
import { Button } from '@/components/ui/Button'
import { Loader } from '@/components/ui/Loader'
import { Modal } from '@/components/ui/Modal'
import { apiErrorMessage, cn, formatDate } from '@/lib/utils'
import { usePerm } from '@/lib/permissions'

/**
 * Guruh testlari — YAGONA admin komponenti. Ikki joyda AYNAN bir xil ishlaydi:
 *  • "O'quv bo'limi → Testlar natijalari" → guruh sahifasi (`TestGroupPage`);
 *  • guruh (jurnal) sahifasidagi "Imtihonlar" tabi (`ClassDetailPage`).
 * Shu sabab onlayn (bot) test YARATISH ikkala joyda ham bor — avval jurnal ichidagi forma
 * qisqartirilgan (faqat oflayn) edi.
 *
 * Ruxsat: yaratish/tahrir/o'chirish `classes` bo'limi bo'yicha (`usePerm`) — admin/superadmin
 * cheklovsiz, xodimga "Xodimlar va rollar" dan beriladi. Server ham AYNAN shu qoidada
 * (`TestResultsController` → `[AdminPerm("classes")]`), o'qituvchi ilovasi esa
 * `TeacherPortalController`ning `test-results/*` endpointlari orqali BIR XIL servisga
 * (`TestResultService`) boradi.
 */

const control =
  'w-full rounded-lg border border-slate-200 px-3 py-2 text-sm text-slate-800 outline-none transition-colors focus:border-brand-400'

const todayIso = () => new Date().toISOString().slice(0, 10)

/** Javob varianti harflari (A, B, C, ...). */
const LETTERS = ['A', 'B', 'C', 'D', 'E', 'F']

/** "2026-07-22T09:30" → "09:30" (bo'sh/noto'g'ri bo'lsa — zaxira qiymat). */
const timeOf = (iso: string, fallback: string) => (iso && iso.length >= 16 ? iso.slice(11, 16) : fallback)

/** Guruh testlari ro'yxati + yaratish/tahrirlash/o'chirish. Test bosilsa — `onOpenTest`. */
export function GroupTestsPanel({
  groupId,
  onOpenTest,
}: {
  groupId: string
  onOpenTest: (testId: string) => void
}) {
  const { can } = usePerm()
  const [tests, setTests] = useState<GroupTest[]>([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState('')
  const [formOpen, setFormOpen] = useState(false)
  const [editing, setEditing] = useState<GroupTest | null>(null)
  const [deleting, setDeleting] = useState<GroupTest | null>(null)
  const [deleteBusy, setDeleteBusy] = useState(false)

  useEffect(() => {
    let active = true
    setLoading(true)
    getGroupTests(groupId)
      .then((t) => active && setTests(t))
      .catch((e) => active && setError(apiErrorMessage(e, "Yuklab bo'lmadi")))
      .finally(() => active && setLoading(false))
    return () => {
      active = false
    }
  }, [groupId])

  const handleSaved = (t: GroupTest) => {
    setTests((prev) => {
      const exists = prev.some((x) => x.id === t.id)
      const next = exists ? prev.map((x) => (x.id === t.id ? { ...x, ...t } : x)) : [t, ...prev]
      return next.sort((a, b) => b.date.localeCompare(a.date))
    })
    setFormOpen(false)
  }

  const confirmDelete = async () => {
    if (!deleting) return
    setDeleteBusy(true)
    try {
      await deleteTest(deleting.id)
      setTests((prev) => prev.filter((x) => x.id !== deleting.id))
      setDeleting(null)
    } catch (e) {
      setError(apiErrorMessage(e, "O'chirib bo'lmadi"))
    } finally {
      setDeleteBusy(false)
    }
  }

  if (loading) return <Loader label="Yuklanmoqda..." />

  return (
    <div className="space-y-3">
      {can('classes.testResults', 'create') && (
        <div className="flex justify-end">
          <Button onClick={() => { setEditing(null); setFormOpen(true) }}>
            <Plus className="h-4 w-4" /> Yangi test
          </Button>
        </div>
      )}

      {error && <Card className="py-3 text-center text-sm text-red-500">{error}</Card>}

      {tests.length === 0 ? (
        <Card className="py-12 text-center text-slate-400">
          Hali test yaratilmagan. "Yangi test" tugmasini bosing.
        </Card>
      ) : (
        <div className="space-y-2.5">
          {tests.map((t) => {
            const isOnline = t.online?.mode === 'online'
            return (
              <div
                key={t.id}
                className="group flex items-center gap-3 rounded-xl border border-slate-200 bg-white p-4 transition-all hover:border-brand-300"
              >
                <button
                  type="button"
                  onClick={() => onOpenTest(t.id)}
                  className="flex min-w-0 flex-1 items-center gap-3 text-left"
                >
                  <div
                    className={cn(
                      'flex h-10 w-10 shrink-0 items-center justify-center rounded-xl',
                      isOnline ? 'bg-violet-50 text-violet-600' : 'bg-brand-50 text-brand-600',
                    )}
                  >
                    {isOnline ? <Bot className="h-5 w-5" /> : <ClipboardList className="h-5 w-5" />}
                  </div>
                  <div className="min-w-0 flex-1">
                    <p className="flex items-center gap-2 truncate font-semibold text-slate-800">
                      {t.name}
                      {isOnline && (
                        <span className="shrink-0 rounded-md bg-violet-50 px-1.5 py-0.5 text-[10px] font-semibold text-violet-600">
                          ONLAYN
                        </span>
                      )}
                      {isOnline && !t.online.groupOpen && (
                        <span
                          className="shrink-0 rounded-md bg-amber-50 px-1.5 py-0.5 text-[10px] font-semibold text-amber-700"
                          title="Guruhga e'lon qilinmagan — faqat test kodi bilan ishlanadi"
                        >
                          FAQAT KOD
                        </span>
                      )}
                      {isOnline && !!t.online.code && (
                        <span
                          className="shrink-0 rounded-md bg-slate-100 px-1.5 py-0.5 font-mono text-[10px] font-semibold tracking-wider text-slate-600"
                          title="Test kodi"
                        >
                          {t.online.code}
                        </span>
                      )}
                      {t.certificateEnabled && (
                        <span
                          className="inline-flex shrink-0 items-center gap-1 rounded-md bg-emerald-50 px-1.5 py-0.5 text-[10px] font-semibold text-emerald-700"
                          title="Test natijasi bo'yicha sertifikat beriladi"
                        >
                          <Award className="h-3 w-3" />
                          SERTIFIKAT
                          {t.certificateCount > 0 && ` · ${t.certificateCount}`}
                        </span>
                      )}
                    </p>
                    <p className="text-xs text-slate-400">
                      {formatDate(t.date)} · Maks: {t.maxScore} ball
                      {isOnline && ` · ${t.online.questionCount} ta savol`}
                    </p>
                    <div className="mt-1 flex flex-wrap items-center gap-x-3 gap-y-0.5 text-xs text-slate-500">
                      <span>
                        Baholangan:{' '}
                        <span className="font-medium text-slate-700">
                          {t.scoredCount}/{t.studentCount}
                        </span>
                      </span>
                      {t.avgScore != null && (
                        <span>
                          O'rtacha: <span className="font-medium text-slate-700">{t.avgScore}</span>
                        </span>
                      )}
                      {isOnline && (
                        <>
                          <span className="inline-flex items-center gap-1">
                            <Clock className="h-3 w-3" />
                            {timeOf(t.online.startAt, '—')}–{timeOf(t.online.endAt, '—')}
                          </span>
                          <span className="inline-flex items-center gap-1 text-violet-600">
                            <Send className="h-3 w-3" /> Botdan: {t.submittedCount}
                          </span>
                          {t.externalCount > 0 && (
                            <span className="inline-flex items-center gap-1 text-teal-600">
                              <Globe className="h-3 w-3" /> Markazdan tashqari: {t.externalCount}
                            </span>
                          )}
                        </>
                      )}
                    </div>
                  </div>
                </button>
                <div className="flex shrink-0 items-center gap-1">
                  {can('classes.testResults', 'edit') && (
                    <button
                      type="button"
                      onClick={() => { setEditing(t); setFormOpen(true) }}
                      className="flex h-8 w-8 items-center justify-center rounded-lg text-slate-400 hover:bg-slate-50 hover:text-slate-600"
                      title="Tahrirlash"
                    >
                      <Pencil className="h-4 w-4" />
                    </button>
                  )}
                  {can('classes.testResults', 'delete') && (
                    <button
                      type="button"
                      onClick={() => setDeleting(t)}
                      className="flex h-8 w-8 items-center justify-center rounded-lg text-slate-400 hover:bg-red-50 hover:text-red-500"
                      title="O'chirish"
                    >
                      <Trash2 className="h-4 w-4" />
                    </button>
                  )}
                  <ChevronRight className="h-5 w-5 text-slate-300" />
                </div>
              </div>
            )
          })}
        </div>
      )}

      {formOpen && (
        <TestFormModal
          key={editing?.id ?? 'new'}
          groupId={groupId}
          editing={editing}
          onClose={() => setFormOpen(false)}
          onSaved={handleSaved}
        />
      )}

      <Modal
        open={!!deleting}
        onClose={() => !deleteBusy && setDeleting(null)}
        title="Testni o'chirish"
        footer={
          <>
            <Button variant="secondary" onClick={() => setDeleting(null)} disabled={deleteBusy}>
              Bekor
            </Button>
            <Button variant="danger" onClick={confirmDelete} disabled={deleteBusy}>
              {deleteBusy ? <Loader2 className="h-4 w-4 animate-spin" /> : "O'chirish"}
            </Button>
          </>
        }
      >
        <div className="flex items-start gap-3">
          <AlertTriangle className="mt-0.5 h-5 w-5 shrink-0 text-amber-500" />
          <p className="text-sm text-slate-600">
            <b>{deleting?.name}</b> testi va unga kiritilgan barcha ballar o'chiriladi. Bu amalni
            qaytarib bo'lmaydi.
          </p>
        </div>
      </Modal>
    </div>
  )
}

/**
 * Test yaratish/tahrirlash modali — IKKI REJIM:
 *  • Oflayn — eski tizim: nom, sana, maksimal ball (ballni o'qituvchi qo'lda kiritadi).
 *  • Onlayn — bot orqali: savollar PDF'i, savollar soni, javoblar kaliti va vaqt oynasi.
 *    O'quvchi Telegram botdagi «Testni ishlash» tugmasidan PDF'ni oladi, javoblarini yuboradi,
 *    tizim avtomatik tekshirib ballni shu testga yozadi (har savol — 1 ball).
 */
function TestFormModal({
  groupId,
  editing,
  onClose,
  onSaved,
}: {
  groupId: string
  editing: GroupTest | null
  onClose: () => void
  onSaved: (t: GroupTest) => void
}) {
  const initialOnline = editing?.online
  const [mode, setMode] = useState<'offline' | 'online'>(
    initialOnline?.mode === 'online' ? 'online' : 'offline',
  )
  const [name, setName] = useState(editing?.name ?? '')
  const [date, setDate] = useState(editing?.date ?? todayIso())
  const [maxScore, setMaxScore] = useState<string>(editing ? String(editing.maxScore) : '')

  // --- onlayn maydonlari ---
  const [pdfUrl, setPdfUrl] = useState(initialOnline?.pdfUrl ?? '')
  const [pdfName, setPdfName] = useState(initialOnline?.pdfName ?? '')
  const [uploading, setUploading] = useState(false)
  const [count, setCount] = useState<string>(
    initialOnline?.questionCount ? String(initialOnline.questionCount) : '20',
  )
  const [options, setOptions] = useState<number>(initialOnline?.optionCount || 4)
  const [key, setKey] = useState<string[]>(
    (initialOnline?.answerKey ?? '').split('').map((c) => (c === '-' ? '' : c)),
  )
  const [bulkKey, setBulkKey] = useState('')
  const [startTime, setStartTime] = useState(timeOf(initialOnline?.startAt ?? '', '09:00'))
  const [endTime, setEndTime] = useState(timeOf(initialOnline?.endAt ?? '', '11:00'))
  // Test KODI — markazda o'qimaydigan odam ham shu kod bilan botda testni ishlaydi.
  // Bo'sh qoldirilsa server o'zi noyob kod yaratadi.
  const [code, setCode] = useState(initialOnline?.code ?? '')
  // true — guruhga ham e'lon qilinadi; false — FAQAT kod bilan ishlanadi.
  const [groupOpen, setGroupOpen] = useState(initialOnline?.groupOpen ?? true)

  // --- sertifikat (ikkala rejimda ham ishlaydi) ---
  const [certEnabled, setCertEnabled] = useState(editing?.certificateEnabled ?? false)
  const [certTemplateId, setCertTemplateId] = useState(editing?.certificateTemplateId ?? '')
  const [templates, setTemplates] = useState<TestCertificateTemplate[]>([])
  const [templatesLoaded, setTemplatesLoaded] = useState(false)

  const [busy, setBusy] = useState(false)
  const [err, setErr] = useState('')

  // Shablonlar ro'yxati modal ochilganda bir marta yuklanadi.
  useEffect(() => {
    let active = true
    getCertificateTemplates(true)
      .then((list) => active && setTemplates(list))
      .catch(() => active && setTemplates([]))
      .finally(() => active && setTemplatesLoaded(true))
    return () => {
      active = false
    }
  }, [])

  const qCount = useMemo(() => {
    const n = Number(count)
    return Number.isFinite(n) && n > 0 ? Math.min(200, Math.floor(n)) : 0
  }, [count])

  // Savollar soni o'zgarsa kalit massivi moslanadi (kiritilganlar saqlanadi).
  const keys = useMemo(() => {
    const arr = key.slice(0, qCount)
    while (arr.length < qCount) arr.push('')
    return arr
  }, [key, qCount])
  const filled = keys.filter(Boolean).length

  const setAnswer = (i: number, letter: string) =>
    setKey(() => {
      const next = keys.slice()
      next[i] = next[i] === letter ? '' : letter
      return next
    })

  /** Kalitni matndan to'ldirish: "abcdab..." yoki "1a 2b 3c" — harflar tartib bilan olinadi. */
  const applyBulk = () => {
    const allowed = LETTERS.slice(0, options)
    const letters = bulkKey
      .toUpperCase()
      .split('')
      .filter((c) => allowed.includes(c))
    if (letters.length === 0) {
      setErr('Kalit topilmadi — masalan: abcdabcd...')
      return
    }
    const next = letters.slice(0, qCount)
    while (next.length < qCount) next.push('')
    setKey(next)
    setBulkKey('')
    setErr('')
  }

  const handlePdf = async (file: File) => {
    setUploading(true)
    setErr('')
    try {
      const up = await uploadAdminFile(file)
      setPdfUrl(up.url)
      setPdfName(up.name)
    } catch (e) {
      setErr(apiErrorMessage(e, "Faylni yuklab bo'lmadi"))
    } finally {
      setUploading(false)
    }
  }

  const max = useMemo(() => Number(maxScore), [maxScore])
  const validOffline = name.trim().length > 0 && !!date && Number.isFinite(max) && max > 0
  const validOnline =
    name.trim().length > 0 &&
    !!date &&
    qCount > 0 &&
    !!pdfUrl &&
    filled === qCount &&
    startTime < endTime
  const valid = mode === 'online' ? validOnline : validOffline

  const submit = async () => {
    if (!valid) {
      setErr(
        mode === 'online'
          ? "Nom, sana, PDF, savollar soni, to'liq javob kaliti va to'g'ri vaqt oralig'i kerak"
          : 'Nom, sana va 0 dan katta maksimal ball kiriting',
      )
      return
    }
    setBusy(true)
    setErr('')
    const online =
      mode === 'online'
        ? {
            mode: 'online' as const,
            pdfUrl,
            pdfName,
            questionCount: qCount,
            optionCount: options,
            answerKey: keys.join(''),
            startAt: `${date}T${startTime}`,
            endAt: `${date}T${endTime}`,
            code: code.trim(),
            groupOpen,
          }
        : {
            mode: 'offline' as const,
            pdfUrl: '',
            pdfName: '',
            questionCount: 0,
            optionCount: 4,
            answerKey: '',
            startAt: '',
            endAt: '',
            code: '',
            groupOpen: true,
          }
    const finalMax = mode === 'online' ? qCount : max
    const certificateTemplateId = certEnabled ? certTemplateId || null : null
    try {
      if (editing) {
        await updateTest(editing.id, {
          name: name.trim(), date, maxScore: finalMax, online,
          certificateEnabled: certEnabled, certificateTemplateId,
        })
        // Kod maydoni bo'sh qoldirilsa server MAVJUD kodni saqlaydi — ro'yxatda ham shu ko'rinsin.
        const echoed = { ...online, code: online.code || editing.online?.code || '' }
        onSaved({
          ...editing, name: name.trim(), date, maxScore: finalMax, online: echoed,
          certificateEnabled: certEnabled, certificateTemplateId: certificateTemplateId ?? '',
        })
      } else {
        const created = await createTest({
          groupId, name: name.trim(), date, maxScore: finalMax, online,
          certificateEnabled: certEnabled, certificateTemplateId,
        })
        onSaved(created)
      }
    } catch (e) {
      setErr(apiErrorMessage(e, "Saqlab bo'lmadi"))
    } finally {
      setBusy(false)
    }
  }

  return (
    <Modal
      open
      onClose={onClose}
      size="lg"
      title={editing ? 'Testni tahrirlash' : 'Yangi test'}
      footer={
        <>
          <Button variant="secondary" onClick={onClose} disabled={busy}>
            Bekor
          </Button>
          <Button onClick={submit} disabled={busy || !valid}>
            {busy ? <Loader2 className="h-4 w-4 animate-spin" /> : 'Saqlash'}
          </Button>
        </>
      }
    >
      <div className="space-y-4">
        {/* Rejim tanlash */}
        <div className="grid grid-cols-2 gap-2 rounded-xl bg-slate-100 p-1">
          {([
            { v: 'offline', icon: ClipboardList, t: 'Oflayn', s: "Ballni qo'lda kiritasiz" },
            { v: 'online', icon: Bot, t: 'Onlayn (bot)', s: "O'quvchi botdan ishlaydi" },
          ] as const).map((m) => {
            const Icon = m.icon
            const active = mode === m.v
            return (
              <button
                key={m.v}
                type="button"
                onClick={() => setMode(m.v)}
                className={cn(
                  'flex items-center gap-2.5 rounded-lg px-3 py-2.5 text-left transition-colors',
                  active ? 'bg-white shadow-sm' : 'hover:bg-white/60',
                )}
              >
                <Icon className={cn('h-5 w-5 shrink-0', active ? 'text-brand-600' : 'text-slate-400')} />
                <span className="min-w-0">
                  <span className={cn('block text-sm font-semibold', active ? 'text-slate-800' : 'text-slate-500')}>
                    {m.t}
                  </span>
                  <span className="block truncate text-[11px] text-slate-400">{m.s}</span>
                </span>
              </button>
            )
          })}
        </div>

        <div>
          <label className="mb-1 block text-xs font-medium text-slate-500">Test nomi</label>
          <input
            className={control}
            value={name}
            onChange={(e) => setName(e.target.value)}
            placeholder="Masalan: Unit 3 test"
            autoFocus
          />
        </div>

        <div className="grid grid-cols-2 gap-3">
          <div>
            <label className="mb-1 block text-xs font-medium text-slate-500">Sana</label>
            <input type="date" className={control} value={date} onChange={(e) => setDate(e.target.value)} />
          </div>
          {mode === 'offline' ? (
            <div>
              <label className="mb-1 block text-xs font-medium text-slate-500">Maksimal ball</label>
              <input
                type="number"
                min={1}
                className={control}
                value={maxScore}
                onChange={(e) => setMaxScore(e.target.value)}
                placeholder="100"
              />
            </div>
          ) : (
            <div>
              <label className="mb-1 block text-xs font-medium text-slate-500">
                Maksimal ball (avtomatik)
              </label>
              <div className="rounded-lg border border-dashed border-slate-200 bg-slate-50 px-3 py-2 text-sm text-slate-500">
                {qCount || '—'} ball · har savol 1 ball
              </div>
            </div>
          )}
        </div>

        {mode === 'online' && (
          <>
            {/* KIMLAR ISHLAYDI: guruh + kod  |  faqat kod */}
            <div>
              <label className="mb-1.5 block text-xs font-medium text-slate-500">
                Testni kimlar ishlaydi?
              </label>
              <div className="grid gap-2 sm:grid-cols-2">
                {([
                  {
                    v: true,
                    icon: Users,
                    t: 'Guruh + kod',
                    s: "Guruh o'quvchilarida o'zi chiqadi, tashqi odam kod bilan qo'shiladi",
                  },
                  {
                    v: false,
                    icon: KeyRound,
                    t: 'Faqat kod bilan',
                    s: "Guruhga e'lon qilinmaydi — faqat kodni bilgan ishlaydi",
                  },
                ] as const).map((o) => {
                  const Icon = o.icon
                  const active = groupOpen === o.v
                  return (
                    <button
                      key={String(o.v)}
                      type="button"
                      onClick={() => setGroupOpen(o.v)}
                      className={cn(
                        'flex items-start gap-2.5 rounded-lg border px-3 py-2.5 text-left transition-colors',
                        active
                          ? 'border-brand-400 bg-brand-50/50'
                          : 'border-slate-200 hover:border-slate-300',
                      )}
                    >
                      <Icon
                        className={cn('mt-0.5 h-4 w-4 shrink-0', active ? 'text-brand-600' : 'text-slate-400')}
                      />
                      <span className="min-w-0">
                        <span
                          className={cn(
                            'block text-sm font-semibold',
                            active ? 'text-slate-800' : 'text-slate-600',
                          )}
                        >
                          {o.t}
                        </span>
                        <span className="block text-[11px] leading-snug text-slate-400">{o.s}</span>
                      </span>
                    </button>
                  )
                })}
              </div>
            </div>

            {/* TEST KODI */}
            <div>
              <label className="mb-1 block text-xs font-medium text-slate-500">Test kodi</label>
              <div className="flex gap-2">
                <input
                  className={cn(control, 'font-mono uppercase tracking-widest')}
                  value={code}
                  onChange={(e) => setCode(e.target.value.toUpperCase())}
                  placeholder={editing ? '' : 'Avtomatik yaratiladi'}
                  maxLength={32}
                />
                {!!code && (
                  <Button variant="secondary" onClick={() => void navigator.clipboard?.writeText(code)}>
                    <Copy className="h-4 w-4" /> Nusxa
                  </Button>
                )}
              </div>
              <p className="mt-1 text-[11px] text-slate-400">
                Markazda o'qimaydigan odam ham botda «Testni ishlash» → «Test kodi bilan kirish» →
                shu kod → F.I.Sh orqali testni ishlaydi. Bo'sh qoldirsangiz tizim o'zi noyob kod
                yaratadi.
              </p>
            </div>

            {/* PDF */}
            <div>
              <label className="mb-1 block text-xs font-medium text-slate-500">
                Test savollari (PDF)
              </label>
              {pdfUrl ? (
                <div className="flex items-center gap-2 rounded-lg border border-slate-200 bg-white px-3 py-2">
                  <FileText className="h-4 w-4 shrink-0 text-red-500" />
                  <a
                    href={pdfUrl}
                    target="_blank"
                    rel="noreferrer"
                    className="min-w-0 flex-1 truncate text-sm font-medium text-brand-600 hover:underline"
                  >
                    {pdfName || 'test.pdf'}
                  </a>
                  <button
                    type="button"
                    onClick={() => {
                      setPdfUrl('')
                      setPdfName('')
                    }}
                    className="shrink-0 rounded-md p-1 text-slate-400 hover:bg-red-50 hover:text-red-500"
                    title="O'chirish"
                  >
                    <Trash2 className="h-4 w-4" />
                  </button>
                </div>
              ) : (
                <label
                  className={cn(
                    'flex cursor-pointer items-center justify-center gap-2 rounded-lg border-2 border-dashed border-slate-200 px-3 py-4 text-sm font-medium text-slate-500 transition-colors hover:border-brand-300 hover:bg-brand-50/40 hover:text-brand-600',
                    uploading && 'pointer-events-none opacity-60',
                  )}
                >
                  {uploading ? (
                    <Loader2 className="h-4 w-4 animate-spin" />
                  ) : (
                    <Upload className="h-4 w-4" />
                  )}
                  {uploading ? 'Yuklanmoqda...' : 'PDF faylni tanlang (20 MB gacha)'}
                  <input
                    type="file"
                    accept="application/pdf,.pdf"
                    className="hidden"
                    onChange={(e) => {
                      const f = e.target.files?.[0]
                      if (f) void handlePdf(f)
                      e.target.value = ''
                    }}
                  />
                </label>
              )}
              <p className="mt-1 text-[11px] text-slate-400">
                Shu fayl o'quvchiga Telegram botda yuboriladi.
              </p>
            </div>

            {/* Savollar soni / variantlar / vaqt */}
            <div className="grid grid-cols-2 gap-3 sm:grid-cols-4">
              <div>
                <label className="mb-1 block text-xs font-medium text-slate-500">Savollar soni</label>
                <input
                  type="number"
                  min={1}
                  max={200}
                  className={control}
                  value={count}
                  onChange={(e) => setCount(e.target.value)}
                />
              </div>
              <div>
                <label className="mb-1 block text-xs font-medium text-slate-500">Variantlar</label>
                <select
                  className={control}
                  value={options}
                  onChange={(e) => setOptions(Number(e.target.value))}
                >
                  {[2, 3, 4, 5, 6].map((n) => (
                    <option key={n} value={n}>
                      A–{LETTERS[n - 1]} ({n} ta)
                    </option>
                  ))}
                </select>
              </div>
              <div>
                <label className="mb-1 block text-xs font-medium text-slate-500">Boshlanishi</label>
                <input
                  type="time"
                  className={control}
                  value={startTime}
                  onChange={(e) => setStartTime(e.target.value)}
                />
              </div>
              <div>
                <label className="mb-1 block text-xs font-medium text-slate-500">Tugashi</label>
                <input
                  type="time"
                  className={control}
                  value={endTime}
                  onChange={(e) => setEndTime(e.target.value)}
                />
              </div>
            </div>
            <p className="-mt-2 text-[11px] text-slate-400">
              O'quvchi javoblarni faqat shu vaqt oralig'ida yubora oladi ({date} kuni).
            </p>

            {/* Javoblar kaliti */}
            <div>
              <div className="mb-1.5 flex items-center justify-between">
                <label className="text-xs font-medium text-slate-500">To'g'ri javoblar kaliti</label>
                <span
                  className={cn(
                    'text-xs font-medium',
                    filled === qCount && qCount > 0 ? 'text-emerald-600' : 'text-amber-600',
                  )}
                >
                  {filled}/{qCount} to'ldirildi
                </span>
              </div>
              <div className="mb-2 flex gap-2">
                <input
                  className={control}
                  value={bulkKey}
                  onChange={(e) => setBulkKey(e.target.value)}
                  onKeyDown={(e) => {
                    if (e.key === 'Enter') {
                      e.preventDefault()
                      applyBulk()
                    }
                  }}
                  placeholder="Tez to'ldirish: abcdabcd... yoki 1a 2b 3c"
                />
                <Button variant="secondary" onClick={applyBulk} disabled={!bulkKey.trim()}>
                  To'ldirish
                </Button>
              </div>
              <div className="max-h-64 overflow-y-auto rounded-lg border border-slate-100 bg-slate-50/50 p-2">
                <div className="grid grid-cols-1 gap-1.5 sm:grid-cols-2">
                  {keys.map((v, i) => (
                    <div
                      key={i}
                      className="flex items-center gap-1.5 rounded-lg bg-white px-2 py-1.5 ring-1 ring-slate-100"
                    >
                      <span className="w-7 shrink-0 text-right text-xs font-semibold text-slate-400">
                        {i + 1}.
                      </span>
                      <div className="flex flex-1 gap-1">
                        {LETTERS.slice(0, options).map((L) => (
                          <button
                            key={L}
                            type="button"
                            onClick={() => setAnswer(i, L)}
                            className={cn(
                              'h-7 flex-1 rounded-md text-xs font-semibold transition-colors',
                              v === L
                                ? 'bg-brand-600 text-white'
                                : 'bg-slate-100 text-slate-500 hover:bg-slate-200',
                            )}
                          >
                            {L}
                          </button>
                        ))}
                      </div>
                    </div>
                  ))}
                </div>
              </div>
            </div>
          </>
        )}

        {/* SERTIFIKAT — oflayn va onlayn testda bir xil ishlaydi */}
        <div className="rounded-xl border border-slate-200 bg-slate-50/60 p-3">
          <label className="flex cursor-pointer items-start gap-2.5">
            <input
              type="checkbox"
              checked={certEnabled}
              onChange={(e) => setCertEnabled(e.target.checked)}
              className="mt-0.5 h-4 w-4 shrink-0 rounded border-slate-300 text-brand-600 focus:ring-brand-400"
            />
            <span className="min-w-0">
              <span className="flex items-center gap-1.5 text-sm font-semibold text-slate-800">
                <Award className="h-4 w-4 text-emerald-600" />
                Test natijasi bo'yicha sertifikat berilsin
              </span>
              <span className="block text-[11px] leading-snug text-slate-400">
                Ball kiritilgan har bir o'quvchiga bittadan sertifikat yaratiladi.
              </span>
            </span>
          </label>

          {certEnabled && (
            <div className="mt-3">
              {templatesLoaded && templates.length === 0 ? (
                <p className="rounded-lg bg-amber-50 px-3 py-2 text-[12px] text-amber-700">
                  Avval «Testlar natijalari → Sertifikat shablonlari» bo'limida Word shablon yuklang.
                </p>
              ) : (
                <>
                  <label className="mb-1 block text-xs font-medium text-slate-500">
                    Sertifikat shabloni
                  </label>
                  <select
                    className={control}
                    value={certTemplateId}
                    onChange={(e) => setCertTemplateId(e.target.value)}
                    disabled={!templatesLoaded}
                  >
                    <option value="">— standart shablon —</option>
                    {templates.map((tpl) => (
                      <option key={tpl.id} value={tpl.id}>
                        {tpl.name}
                        {tpl.isDefault ? ' (standart)' : ''}
                      </option>
                    ))}
                  </select>
                </>
              )}
            </div>
          )}
        </div>

        {err && <p className="text-sm text-red-500">{err}</p>}
      </div>
    </Modal>
  )
}
