import { useEffect, useState } from 'react'
import { useParams } from 'react-router-dom'
import { GraduationCap, ArrowRight, ArrowLeft, Check, Loader2, PartyPopper, AlertCircle } from 'lucide-react'
import type { PublicTest, TestResult } from '@/types'
import { getPublicTest, submitPublicTest, getInviteTest, submitInviteTest } from '@/api/services/publicTest'
import { getPublicBrand, type PublicBrand } from '@/api/services/settings'
import { apiErrorMessage, cn } from '@/lib/utils'
import { inputCls, cardPadX, primaryBtnCls, Field } from './publicFormUi'

type Phase = 'loading' | 'notfound' | 'used' | 'intro' | 'quiz' | 'done'

export function PublicTestPage() {
  const { slug = '', token = '' } = useParams()
  const invite = !!token
  const [phase, setPhase] = useState<Phase>('loading')
  const [test, setTest] = useState<PublicTest | null>(null)

  // Kontakt
  const [fullName, setFullName] = useState('')
  const [phone, setPhone] = useState('')
  const [age, setAge] = useState('')

  // Test
  const [step, setStep] = useState(0)
  const [answers, setAnswers] = useState<Record<string, number>>({})
  const [surveyAnswers, setSurveyAnswers] = useState<Record<string, number[]>>({})
  const [submitting, setSubmitting] = useState(false)
  const [result, setResult] = useState<TestResult | null>(null)
  const [error, setError] = useState('')
  const [brand, setBrand] = useState<PublicBrand>({ name: '', logoUrl: '', phone: '' })

  useEffect(() => {
    if (invite) {
      getInviteTest(token)
        .then((r) => {
          if (r.used || !r.test) {
            setPhase('used')
            return
          }
          setTest(r.test)
          setFullName(r.fullName)
          setPhone(r.phone)
          setPhase('intro')
        })
        .catch((e) => setPhase((e as any)?.response?.status === 410 ? 'used' : 'notfound'))
      return
    }
    getPublicTest(slug)
      .then((t) => {
        setTest(t)
        setPhase('intro')
      })
      .catch(() => setPhase('notfound'))
  }, [slug, token, invite])

  useEffect(() => {
    getPublicBrand()
      .then(setBrand)
      .catch(() => {})
  }, [])

  const start = () => {
    if (!invite) {
      if (!fullName.trim()) return setError('Ism-familiyangizni kiriting')
      const digits = phone.replace(/\D/g, '')
      if (digits.length < 9)
        return setError("Telefon raqamini to'liq kiriting (kamida 9 ta raqam)")
    }
    setError('')
    setPhase('quiz')
  }

  const submit = async () => {
    if (!test || submitting) return
    setSubmitting(true)
    setError('')
    try {
      const r = invite
        ? await submitInviteTest(token, { answers, surveyAnswers })
        : await submitPublicTest(slug, {
            fullName: fullName.trim(),
            phone: phone.trim(),
            age: Number(age) || 0,
            answers,
            surveyAnswers,
          })
      setResult(r)
      setPhase('done')
    } catch (err) {
      setError(apiErrorMessage(err, 'Xatolik yuz berdi. Qayta urinib ko\'ring.'))
    } finally {
      setSubmitting(false)
    }
  }

  const questions = test?.questions ?? []
  const q = questions[step]
  // So'rovnoma ixtiyoriy (har doim "javob berilgan" deb hisoblanadi); savol — variant tanlanishi shart.
  const answered = q ? (q.kind === 'survey' ? true : answers[q.id] !== undefined) : false
  const progress = questions.length ? Math.round(((step + (answered ? 1 : 0)) / questions.length) * 100) : 0

  return (
    <div className="min-h-screen bg-gradient-to-br from-brand-50 via-white to-slate-50 px-4 py-8 sm:py-14">
      <div className="mx-auto w-full max-w-xl">
        {/* Brand */}
        <div className="mb-6 flex items-center justify-center gap-2.5">
          {brand.logoUrl ? (
            <img
              src={brand.logoUrl}
              alt="Logo"
              className="h-10 w-10 rounded-xl object-contain shadow-lg"
            />
          ) : (
            <div className="flex h-10 w-10 items-center justify-center rounded-xl bg-gradient-to-br from-brand-500 to-brand-700 text-white shadow-lg">
              <GraduationCap className="h-6 w-6" />
            </div>
          )}
          <span className="text-lg font-bold tracking-tight text-slate-800">
            {brand.name || 'WunderkindLC'}
          </span>
        </div>

        <div className="overflow-hidden rounded-2xl border border-slate-100 bg-white shadow-xl">
          {phase === 'loading' && (
            <div className="flex flex-col items-center gap-3 py-20 text-slate-400">
              <Loader2 className="h-7 w-7 animate-spin" />
              <p className="text-sm">Yuklanmoqda...</p>
            </div>
          )}

          {phase === 'notfound' && (
            <div className={cn('flex flex-col items-center gap-3 py-20 text-center', cardPadX)}>
              <div className="flex h-14 w-14 items-center justify-center rounded-2xl bg-red-50 text-red-500">
                <AlertCircle className="h-7 w-7" />
              </div>
              <h1 className="text-lg font-bold text-slate-800">Test topilmadi</h1>
              <p className="text-sm text-slate-500">
                Bu havola noto'g'ri yoki test faol emas. Iltimos, markaz bilan bog'laning.
              </p>
            </div>
          )}

          {phase === 'used' && (
            <div className={cn('flex flex-col items-center gap-3 py-20 text-center', cardPadX)}>
              <div className="flex h-14 w-14 items-center justify-center rounded-2xl bg-amber-50 text-amber-500">
                <AlertCircle className="h-7 w-7" />
              </div>
              <h1 className="text-lg font-bold text-slate-800">Havola ishlatilgan</h1>
              <p className="text-sm text-slate-500">
                Bu bir martalik havola allaqachon ishlatilgan — testni qayta topshirib bo'lmaydi.
              </p>
            </div>
          )}

          {/* Kirish + kontakt */}
          {phase === 'intro' && test && (
            <div>
              <div className={cn('bg-gradient-to-br from-brand-500 to-brand-700 py-6 text-white sm:py-7', cardPadX)}>
                <h1 className="break-words text-xl font-bold">{test.title}</h1>
                {test.courseName && <p className="mt-1 text-sm text-white/80">{test.courseName}</p>}
              </div>
              {/* Haqiqiy `<form>`: telefonda klaviatura "davom etish" tugmasini ko'rsatadi va
                  brauzerning avto-to'ldirishi (ism/telefon) ishlaydi. */}
              <form
                onSubmit={(e) => {
                  e.preventDefault()
                  start()
                }}
                className={cn('space-y-4 py-6', cardPadX)}
              >
                {test.intro && <p className="text-sm leading-relaxed text-slate-600">{test.intro}</p>}
                <div className="rounded-xl bg-slate-50 px-4 py-3 text-sm text-slate-500">
                  <p className="font-medium text-slate-700">{questions.length} ta savol</p>
                  <p className="mt-0.5">
                    {invite
                      ? `${fullName ? fullName + ', ' : ''}testni boshlash uchun tugmani bosing. Bu havola bir martalik.`
                      : "Avval o'zingiz haqingizda qisqa ma'lumot qoldiring."}
                  </p>
                </div>

                {!invite && (
                  <div className="space-y-3">
                    <Field label="Ism-familiya *">
                      <input
                        value={fullName}
                        onChange={(e) => setFullName(e.target.value)}
                        placeholder="Masalan: Aliyev Ali"
                        name="name"
                        autoComplete="name"
                        autoCapitalize="words"
                        enterKeyHint="next"
                        className={inputCls}
                      />
                    </Field>
                    <Field label="Telefon raqam *">
                      <input
                        value={phone}
                        onChange={(e) => setPhone(e.target.value)}
                        placeholder="+998 90 123 45 67"
                        type="tel"
                        name="tel"
                        autoComplete="tel"
                        inputMode="tel"
                        enterKeyHint="next"
                        className={inputCls}
                      />
                    </Field>
                    <Field label="Yoshingiz (ixtiyoriy)">
                      <input
                        value={age}
                        onChange={(e) => setAge(e.target.value.replace(/\D/g, ''))}
                        placeholder="18"
                        inputMode="numeric"
                        pattern="[0-9]*"
                        maxLength={3}
                        enterKeyHint="go"
                        className={inputCls}
                      />
                    </Field>
                  </div>
                )}

                {error && <p className="text-sm font-medium text-red-600">{error}</p>}

                <button
                  type="submit"
                  className={cn(primaryBtnCls, 'bg-brand-600 shadow-brand-600/20 hover:bg-brand-700')}
                >
                  Testni boshlash <ArrowRight className="h-4 w-4" />
                </button>
              </form>
            </div>
          )}

          {/* Test */}
          {phase === 'quiz' && test && q && (
            <div>
              {/* Progress */}
              <div className={cn('pt-6', cardPadX)}>
                <div className="mb-2 flex items-center justify-between text-xs font-medium text-slate-400">
                  <span>
                    Savol {step + 1} / {questions.length}
                  </span>
                  <span>{progress}%</span>
                </div>
                <div className="h-1.5 overflow-hidden rounded-full bg-slate-100">
                  <div
                    className="h-full rounded-full bg-brand-500 transition-all duration-300"
                    style={{ width: `${progress}%` }}
                  />
                </div>
              </div>

              <div className={cn('py-6', cardPadX)}>
                <h2 className="text-base font-semibold leading-relaxed text-slate-800">{q.text}</h2>
                {q.kind === 'survey' && (
                  <p className="mt-1 text-xs text-slate-400">
                    So'rovnoma{q.multiple ? ' — bir nechtasini tanlashingiz mumkin' : ''} (ixtiyoriy)
                  </p>
                )}
                <div className="mt-4 space-y-2.5">
                  {q.options.map((opt, oi) => {
                    const isSurvey = q.kind === 'survey'
                    const selected = isSurvey
                      ? (surveyAnswers[q.id]?.includes(oi) ?? false)
                      : answers[q.id] === oi
                    const onPick = () => {
                      if (!isSurvey) {
                        setAnswers((a) => ({ ...a, [q.id]: oi }))
                      } else {
                        setSurveyAnswers((a) => {
                          const cur = a[q.id] ?? []
                          if (q.multiple)
                            return {
                              ...a,
                              [q.id]: cur.includes(oi) ? cur.filter((x) => x !== oi) : [...cur, oi],
                            }
                          return { ...a, [q.id]: [oi] }
                        })
                      }
                    }
                    // checkbox (ko'p tanlash) → kvadrat; radio/savol → doira
                    const square = isSurvey && q.multiple
                    return (
                      <button
                        key={oi}
                        onClick={onPick}
                        className={
                          'flex w-full items-center gap-3 rounded-xl border-2 px-4 py-3 text-left text-sm transition-colors ' +
                          (selected
                            ? 'border-brand-500 bg-brand-50 text-brand-800'
                            : 'border-slate-200 text-slate-700 hover:border-brand-300 hover:bg-slate-50')
                        }
                      >
                        <span
                          className={
                            'flex h-6 w-6 shrink-0 items-center justify-center border-2 text-xs font-bold ' +
                            (square ? 'rounded-md ' : 'rounded-full ') +
                            (selected ? 'border-brand-500 bg-brand-500 text-white' : 'border-slate-300 text-slate-400')
                          }
                        >
                          {selected ? <Check className="h-3.5 w-3.5" /> : isSurvey ? '' : String.fromCharCode(65 + oi)}
                        </span>
                        <span className="flex-1">{opt}</span>
                      </button>
                    )
                  })}
                </div>

                {error && <p className="mt-3 text-sm font-medium text-red-600">{error}</p>}

                <div className="mt-6 flex items-center gap-2">
                  {step > 0 && (
                    <button
                      onClick={() => setStep((s) => s - 1)}
                      className="flex items-center gap-1.5 rounded-xl border border-slate-200 px-4 py-3 text-sm font-medium text-slate-600 sm:py-2.5 transition-colors hover:bg-slate-50"
                    >
                      <ArrowLeft className="h-4 w-4" /> Orqaga
                    </button>
                  )}
                  {step < questions.length - 1 ? (
                    <button
                      onClick={() => setStep((s) => s + 1)}
                      disabled={!answered}
                      className="flex flex-1 items-center justify-center gap-2 rounded-xl bg-brand-600 py-3 text-sm font-semibold text-white transition-colors hover:bg-brand-700 sm:py-2.5 disabled:opacity-40"
                    >
                      Keyingi <ArrowRight className="h-4 w-4" />
                    </button>
                  ) : (
                    <button
                      onClick={submit}
                      disabled={!answered || submitting}
                      className="flex flex-1 items-center justify-center gap-2 rounded-xl bg-emerald-600 py-3 text-sm font-semibold text-white transition-colors hover:bg-emerald-700 sm:py-2.5 disabled:opacity-40"
                    >
                      {submitting ? <Loader2 className="h-4 w-4 animate-spin" /> : <Check className="h-4 w-4" />}
                      Yakunlash
                    </button>
                  )}
                </div>
              </div>
            </div>
          )}

          {/* Natija */}
          {phase === 'done' && result && (
            <div className={cn('flex flex-col items-center gap-4 py-10 text-center', cardPadX)}>
              <div className="flex h-16 w-16 items-center justify-center rounded-2xl bg-emerald-50 text-emerald-500">
                <PartyPopper className="h-8 w-8" />
              </div>
              <h1 className="text-xl font-bold text-slate-800">Rahmat!</h1>

              {result.total > 0 && (
                <div className="flex w-full flex-col items-center gap-4 rounded-2xl bg-slate-50 px-6 py-6">
                  <div className="text-center">
                    <p className="text-xs uppercase tracking-wide text-slate-400">Natija</p>
                    <p className="mt-1 font-mono text-3xl font-bold text-slate-800">
                      {result.score}
                      <span className="text-lg text-slate-400">/{result.total}</span>
                    </p>
                    <p className="font-mono text-sm text-slate-500">{result.percent}%</p>
                  </div>
                  {result.level && (
                    <div className="rounded-full bg-brand-600 px-5 py-1.5 text-sm font-bold text-white">
                      Darajangiz: {result.level}
                    </div>
                  )}
                </div>
              )}

              <p className="max-w-sm text-sm leading-relaxed text-slate-500">{result.message}</p>
            </div>
          )}
        </div>

        <p className="mt-6 text-center text-xs text-slate-400">
          {brand.name || 'WunderkindLC'} · O'quv markazi
        </p>
      </div>
    </div>
  )
}

