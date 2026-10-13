import { useEffect, useState } from 'react'
import {
  Plus, Pencil, Trash2, ClipboardList, ChevronRight, AlertTriangle, Loader2, Award, Send,
} from 'lucide-react'
import type { GroupTest } from '@/types'
import { getGroupTests, createTest, updateTest, deleteTest } from '@/api/services/testResults'
import { getCertificateTemplates } from '@/api/services/testCertificates'
import type { TestCertificateTemplate } from '@/api/services/testCertificates'
import { Card } from '@/components/ui/Card'
import { Button } from '@/components/ui/Button'
import { Loader } from '@/components/ui/Loader'
import { Modal } from '@/components/ui/Modal'
import { NumberInput } from '@/components/ui/NumberInput'
import { apiErrorMessage, cn, formatDate } from '@/lib/utils'
import { usePerm } from '@/lib/permissions'
import { localIsoDate } from '@/lib/localDate'
import { currentMonth } from '@/lib/month'
import { fmtScore, testKindLabel } from '@/lib/ratingPeriods'
import { formatSentAt, MAX_QUESTIONS, MIN_QUESTIONS, TEST_KINDS, type TestKind } from '@/lib/testScoreGrid'
import { TestMonthFilter } from './TestMonthFilter'

import { DateInput } from '@/components/ui/DateInput'
/**
 * Guruh testlari — YAGONA admin komponenti. Ikki joyda AYNAN bir xil ishlaydi:
 *  • "O'quv bo'limi → Testlar natijalari" → guruh sahifasi (`TestGroupPage`);
 *  • guruh (jurnal) sahifasidagi "Imtihonlar" tabi (`ClassDetailPage`).
 * Test faqat NATIJA (CRM U4a, egasining qarori): nom, sana, TURI (oylik/oraliq) va «Savollar soni» — har o'quvchiga
 * bitta raqam (yechgani), foiz o'zi hisoblanadi. Ro'yxat OY bo'yicha (standart — joriy oy) va tur bo'yicha filtrlanadi.
 *
 * Ruxsat: yaratish/tahrir/o'chirish `classes.testResults` sahifasi bo'yicha (`usePerm`) — admin/superadmin
 * cheklovsiz. Server ham AYNAN shu qoidada (`TestResultsController` → `[AdminPerm("classes.testResults")]`),
 * o'qituvchi ilovasi `TeacherPortalController`ning `test-results/*` endpointlari orqali BIR XIL servisga boradi.
 */

const control =
  'w-full rounded-lg border border-slate-200 px-3 py-2 text-sm text-slate-800 outline-none transition-colors focus:border-brand-400'

const todayIso = () => localIsoDate(new Date())

/** Guruh testlari ro'yxati + yaratish/tahrirlash/o'chirish. Test bosilsa — `onOpenTest`. */
export function GroupTestsPanel({
  groupId,
  onOpenTest,
}: {
  groupId: string
  onOpenTest: (testId: string) => void
}) {
  const { can } = usePerm()
  const thisMonth = currentMonth()
  const [month, setMonth] = useState(thisMonth)
  const [kind, setKind] = useState('')
  const [reload, setReload] = useState(0)
  const key = `${groupId}|${month}|${kind}|${reload}`
  const [res, setRes] = useState<{ key: string; tests: GroupTest[]; error: string } | null>(null)
  const [formOpen, setFormOpen] = useState(false)
  const [editing, setEditing] = useState<GroupTest | null>(null)
  const [deleting, setDeleting] = useState<GroupTest | null>(null)
  const [deleteBusy, setDeleteBusy] = useState(false)
  const [actionError, setActionError] = useState('')

  useEffect(() => {
    let active = true
    const k = `${groupId}|${month}|${kind}|${reload}`
    getGroupTests(groupId, { month, kind })
      .then((tests) => active && setRes({ key: k, tests, error: '' }))
      .catch((e) => active && setRes({ key: k, tests: [], error: apiErrorMessage(e, "Yuklab bo'lmadi") }))
    return () => {
      active = false
    }
  }, [groupId, month, kind, reload])

  const loading = res?.key !== key
  const tests = res?.tests ?? []

  const handleSaved = () => {
    setFormOpen(false)
    setReload((r) => r + 1)
  }

  const confirmDelete = async () => {
    if (!deleting) return
    setDeleteBusy(true)
    try {
      await deleteTest(deleting.id)
      setDeleting(null)
      setReload((r) => r + 1)
    } catch (e) {
      setActionError(apiErrorMessage(e, "O'chirib bo'lmadi"))
    } finally {
      setDeleteBusy(false)
    }
  }

  return (
    <div className="space-y-3">
      <div className="flex flex-wrap items-center justify-between gap-2">
        <TestMonthFilter month={month} kind={kind} onMonth={setMonth} onKind={setKind} currentMonth={thisMonth} />
        {can('classes.testResults', 'create') && (
          <Button onClick={() => { setEditing(null); setFormOpen(true) }}>
            <Plus className="h-4 w-4" /> Yangi test
          </Button>
        )}
      </div>

      {(res?.error || actionError) && (
        <Card className="py-3 text-center text-sm text-red-500">{actionError || res?.error}</Card>
      )}

      {loading && !res ? (
        <Loader label="Yuklanmoqda..." />
      ) : tests.length === 0 ? (
        <Card className="py-12 text-center text-slate-400">
          {month ? "Bu oyda test yo'q." : 'Hali test yaratilmagan.'} "Yangi test" tugmasini bosing.
        </Card>
      ) : (
        <div className={cn('space-y-2.5 transition-opacity', loading && 'opacity-60')}>
          {tests.map((t) => {
            const questions = t.questionCount ?? 0
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
                  <div className="flex h-10 w-10 shrink-0 items-center justify-center rounded-xl bg-brand-50 text-brand-600">
                    <ClipboardList className="h-5 w-5" />
                  </div>
                  <div className="min-w-0 flex-1">
                    <p className="flex flex-wrap items-center gap-2 font-semibold text-slate-800">
                      <span className="truncate">{t.name}</span>
                      {testKindLabel(t.kind ?? '') && (
                        <span className="shrink-0 rounded-md bg-indigo-50 px-1.5 py-0.5 text-[10px] font-semibold text-indigo-700">
                          {testKindLabel(t.kind ?? '')}
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
                      {formatDate(t.date)} · {questions > 0 ? `${questions} ta savol` : `Maks: ${t.maxScore} ball`}
                    </p>
                    <div className="mt-1 flex flex-wrap items-center gap-x-3 gap-y-0.5 text-xs text-slate-500">
                      <span>
                        Kiritilgan:{' '}
                        <span className="font-medium text-slate-700">
                          {t.scoredCount}/{t.studentCount}
                        </span>
                      </span>
                      {t.avgPercent != null && (
                        <span>
                          O'rtacha: <span className="font-medium text-slate-700">{fmtScore(t.avgPercent)}%</span>
                        </span>
                      )}
                      {t.resultsSentAt && (
                        <span className="inline-flex items-center gap-1 text-emerald-700">
                          <Send className="h-3 w-3" /> Yuborilgan: {formatSentAt(t.resultsSentAt)}
                        </span>
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
            <b>{deleting?.name}</b> testi va unga kiritilgan barcha natijalar o'chiriladi. Bu amalni
            qaytarib bo'lmaydi.
          </p>
        </div>
      </Modal>
    </div>
  )
}

/**
 * Test yaratish/tahrirlash — «Test nomi», «Sana», «Turi» (Oylik / Oraliq, standart YO'Q), «Savollar soni»
 * (+ ixtiyoriy sertifikat). ESKI (savollar sonisiz) testni tahrirlashda «Maksimal ball» ko'rinadi.
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
  onSaved: () => void
}) {
  const legacy = !!editing && (editing.questionCount ?? 0) === 0
  const [name, setName] = useState(editing?.name ?? '')
  const [date, setDate] = useState(editing?.date ?? todayIso())
  const [kind, setKind] = useState<TestKind | ''>(
    editing?.kind === 'monthly' || editing?.kind === 'interim' ? editing.kind : '',
  )
  const [questions, setQuestions] = useState<number | null>(editing && !legacy ? editing.questionCount ?? null : null)
  const [maxScore, setMaxScore] = useState<number | null>(legacy ? editing!.maxScore : null)

  // --- sertifikat ---
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

  const validationError = (): string | null => {
    if (!name.trim()) return 'Test nomini kiriting'
    if (!date) return 'Sanani tanlang'
    if (!kind) return 'Test turini tanlang'
    if (legacy) return maxScore != null && maxScore > 0 ? null : "Maksimal ball 0 dan katta bo'lsin"
    if (questions == null || questions < MIN_QUESTIONS || questions > MAX_QUESTIONS)
      return "Savollar soni 1 dan 200 gacha bo'lsin"
    return null
  }

  const submit = async () => {
    const v = validationError()
    if (v) {
      setErr(v)
      return
    }
    setBusy(true)
    setErr('')
    const certificateTemplateId = certEnabled ? certTemplateId || null : null
    try {
      if (editing) {
        await updateTest(editing.id, {
          name: name.trim(), date, kind,
          questionCount: legacy ? null : questions,
          maxScore: legacy ? maxScore ?? 0 : 0,
          certificateEnabled: certEnabled, certificateTemplateId,
        })
      } else {
        await createTest({
          groupId, name: name.trim(), date, kind, questionCount: questions,
          certificateEnabled: certEnabled, certificateTemplateId,
        })
      }
      onSaved()
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
          <Button onClick={submit} disabled={busy}>
            {busy ? <Loader2 className="h-4 w-4 animate-spin" /> : 'Saqlash'}
          </Button>
        </>
      }
    >
      <div className="space-y-4">
        <div>
          <label className="mb-1 block text-xs font-medium text-slate-500">Test nomi</label>
          <input
            className={control}
            value={name}
            onChange={(e) => setName(e.target.value)}
            placeholder="Masalan: Unit 5"
            autoFocus
          />
        </div>

        <div>
          <span className="mb-1 block text-xs font-medium text-slate-500">Turi</span>
          <div className="flex gap-2" role="radiogroup" aria-label="Test turi">
            {TEST_KINDS.map((k) => (
              <label
                key={k.kind}
                className={cn(
                  'flex cursor-pointer items-center gap-2 rounded-lg border px-3 py-2 text-sm',
                  kind === k.kind ? 'border-brand-500 bg-brand-50 text-brand-700' : 'border-slate-200 text-slate-600',
                )}
              >
                <input
                  type="radio"
                  name="test-kind"
                  className="h-4 w-4"
                  checked={kind === k.kind}
                  onChange={() => setKind(k.kind)}
                />
                {k.label}
              </label>
            ))}
          </div>
        </div>

        <div className="grid grid-cols-2 gap-3">
          <div>
            <label className="mb-1 block text-xs font-medium text-slate-500">Sana</label>
            <DateInput className={control} value={date} onChange={(e) => setDate(e.target.value)} />
          </div>
          <div>
            {legacy ? (
              <>
                <label className="mb-1 block text-xs font-medium text-slate-500">Maksimal ball</label>
                <NumberInput as="input" className={control} value={maxScore} onChange={setMaxScore} placeholder="100" />
              </>
            ) : (
              <>
                <label className="mb-1 block text-xs font-medium text-slate-500">Savollar soni</label>
                <NumberInput as="input" className={control} value={questions} onChange={setQuestions} placeholder="30" />
              </>
            )}
          </div>
        </div>
        {!legacy && (
          <p className="-mt-2 text-[11px] text-slate-400">
            Har o'quvchiga nechta savolni yechgani kiritiladi — foiz o'zi hisoblanadi (yechgan ÷ savollar).
          </p>
        )}

        {/* SERTIFIKAT */}
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
                Natija kiritilgan har bir o'quvchiga bittadan sertifikat yaratiladi.
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
