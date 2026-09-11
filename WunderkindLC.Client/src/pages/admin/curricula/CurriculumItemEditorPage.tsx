import { useEffect, useRef, useState } from 'react'
import { useNavigate, useParams } from 'react-router-dom'
import {
  ArrowLeft, Plus, Trash2, Check, Upload, Loader2, AlertTriangle, FileType, ExternalLink, X,
} from 'lucide-react'
import type { LessonType } from '@/types'
import { getCourseItem, saveItemContent } from '@/api/services/curriculum'
import type { VocabEntry, CourseQuestion, SaveItemContent } from '@/api/services/curriculum'
import { uploadAdminFile } from '@/api/services/students'
import { Card } from '@/components/ui/Card'
import { Button } from '@/components/ui/Button'
import { Loader } from '@/components/ui/Loader'
import { apiErrorMessage, cn } from '@/lib/utils'
import { control, typeMeta, genId } from './shared'
import { ExerciseWorkspace } from '@/components/exercise/ExerciseWorkspace'

/** O'quv dasturi 5-bosqich: bitta topshiriqning to'liq kontent tahrirlovchisi
 *  (video/matn/audio/pdf/lug'at/test — topshiriq yaratishda tanlangan turga mos). */
export function CurriculumItemEditorPage() {
  const { curriculumId = '', moduleId = '', topicId = '', lessonId = '', itemId = '' } = useParams()
  const navigate = useNavigate()

  const backUrl = `/admin/curricula/${curriculumId}/${moduleId}/${topicId}/${lessonId}`

  const [loading, setLoading] = useState(true)
  const [saving, setSaving] = useState(false)
  const [saved, setSaved] = useState(false)
  const [notFound, setNotFound] = useState(false)

  const [text, setText] = useState('')
  const [type, setType] = useState<LessonType>('text')
  const [videoUrl, setVideoUrl] = useState('')
  const [audioUrl, setAudioUrl] = useState('')
  const [textContent, setTextContent] = useState('')
  const [pdfUrl, setPdfUrl] = useState('')
  const [pdfName, setPdfName] = useState('')
  const [meta, setMeta] = useState('')
  const [vocab, setVocab] = useState<VocabEntry[]>([])
  const [questions, setQuestions] = useState<CourseQuestion[]>([])
  /** Interaktiv mashq ("exercise" turi) — konstruktor tanlagan tur va JSON mazmuni. */
  const [exerciseKind, setExerciseKind] = useState('')
  const [exerciseJson, setExerciseJson] = useState('')

  useEffect(() => {
    setLoading(true)
    setNotFound(false)
    getCourseItem(itemId)
      .then((d) => {
        setText(d.text)
        setType(d.type)
        setVideoUrl(d.videoUrl)
        setAudioUrl(d.audioUrl)
        setTextContent(d.textContent)
        setPdfUrl(d.pdfUrl)
        setPdfName(d.pdfName)
        setMeta(d.meta)
        setVocab(d.vocab ?? [])
        setQuestions(d.questions ?? [])
        setExerciseKind(d.exerciseKind ?? '')
        setExerciseJson(d.exerciseJson ?? '')
      })
      .catch(() => setNotFound(true))
      .finally(() => setLoading(false))
  }, [itemId])

  /** Konstruktordan saqlash — faqat mashq maydonlari yangilanadi (nom o'zgarmaydi). */
  const saveExercise = async (kind: string, json: string) => {
    await saveItemContent(itemId, {
      text: text.trim() || 'Topshiriq',
      videoUrl,
      audioUrl,
      textContent,
      pdfUrl,
      pdfName,
      meta,
      vocab,
      questions,
      exerciseKind: kind,
      exerciseJson: json,
    })
    setExerciseKind(kind)
    setExerciseJson(json)
  }

  const save = async () => {
    if (saving) return
    setSaving(true)
    setSaved(false)
    try {
      const payload: SaveItemContent = {
        text: text.trim() || 'Topshiriq',
        videoUrl,
        audioUrl,
        textContent,
        pdfUrl,
        pdfName,
        meta,
        vocab: vocab.filter((v) => v.term.trim() || v.meaning.trim()),
        questions: questions.filter((q) => q.text.trim()).map((q) => ({ ...q, options: q.options })),
      }
      await saveItemContent(itemId, payload)
      setSaved(true)
      setTimeout(() => setSaved(false), 2500)
    } finally {
      setSaving(false)
    }
  }

  if (loading) {
    return (
      <Card>
        <Loader label="Topshiriq yuklanmoqda..." />
      </Card>
    )
  }

  if (notFound) {
    return (
      <Card className="py-12 text-center text-slate-400">
        Topshiriq topilmadi.
        <div className="mt-3">
          <Button variant="secondary" onClick={() => navigate(backUrl)}>
            <ArrowLeft className="h-4 w-4" /> Orqaga
          </Button>
        </div>
      </Card>
    )
  }

  const tMeta = typeMeta(type)

  // INTERAKTIV MASHQ — o'quv dasturining oxirgi bosqichidagi topshiriq konstruktori: tur tanlash
  // (8 kategoriya, 25 tur) va turga mos tahrirlovchi + jonli "foydalanuvchi ko'rinishi".
  if (type === 'exercise') {
    return (
      <div>
        <button
          type="button"
          onClick={() => navigate(backUrl)}
          className="mb-3 inline-flex items-center gap-1.5 text-sm font-medium text-slate-500 hover:text-slate-700"
        >
          <ArrowLeft className="h-4 w-4" /> Topshiriqlarga qaytish
        </button>
        <ExerciseWorkspace
          key={itemId}
          itemName={text}
          initialKind={exerciseKind}
          initialJson={exerciseJson}
          onSave={saveExercise}
          onExit={() => navigate(backUrl)}
        />
      </div>
    )
  }

  return (
    <div>
      <button
        type="button"
        onClick={() => navigate(backUrl)}
        className="mb-3 inline-flex items-center gap-1.5 text-sm font-medium text-slate-500 hover:text-slate-700"
      >
        <ArrowLeft className="h-4 w-4" /> Topshiriqlarga qaytish
      </button>

      <div className="space-y-4">
        <Card
          title="Topshiriq tafsilotlari"
          actions={
            <span className="inline-flex items-center gap-1 rounded-full bg-brand-50 px-2.5 py-1 text-xs font-semibold text-brand-600">
              <tMeta.icon className="h-3.5 w-3.5" /> {tMeta.label}
            </span>
          }
        >
          <label className="mb-1.5 block text-xs font-semibold text-slate-500">Topshiriq nomi</label>
          <input
            value={text}
            onChange={(e) => setText(e.target.value)}
            placeholder="Topshiriq nomi"
            className={control}
          />

          <div className="mt-4">
            {(type === 'video' || type === 'audio') && (
              <MediaEditor
                kind={type}
                url={type === 'video' ? videoUrl : audioUrl}
                onUrl={type === 'video' ? setVideoUrl : setAudioUrl}
                meta={meta}
                onMeta={setMeta}
              />
            )}

            {type === 'text' && (
              <div>
                <label className="mb-1.5 block text-xs font-semibold text-slate-500">Matn</label>
                <textarea
                  value={textContent}
                  onChange={(e) => setTextContent(e.target.value)}
                  placeholder="Topshiriq matnini kiriting..."
                  rows={10}
                  className={cn(control, 'resize-y leading-relaxed')}
                />
              </div>
            )}

            {type === 'pdf' && (
              <PdfEditor
                url={pdfUrl}
                name={pdfName}
                onChange={(u, n) => {
                  setPdfUrl(u)
                  setPdfName(n)
                }}
              />
            )}

            {type === 'vocab' && <VocabEditor vocab={vocab} onChange={setVocab} />}
          </div>
        </Card>

        {type === 'test' && <TestBuilder questions={questions} onChange={setQuestions} />}

        <div className="flex items-center justify-end gap-3">
          {saved && (
            <span className="inline-flex items-center gap-1 text-sm font-medium text-emerald-600">
              <Check className="h-4 w-4" /> Saqlandi
            </span>
          )}
          <Button onClick={save} disabled={saving}>
            {saving ? <Loader2 className="h-4 w-4 animate-spin" /> : <Check className="h-4 w-4" />}
            Saqlash
          </Button>
        </div>
      </div>
    </div>
  )
}

// ============================ Media (video/audio) tahrirlovchi ============================

interface MediaEditorProps {
  kind: 'video' | 'audio'
  url: string
  onUrl: (v: string) => void
  meta: string
  onMeta: (v: string) => void
}

function MediaEditor({ kind, url, onUrl, meta, onMeta }: MediaEditorProps) {
  const fileRef = useRef<HTMLInputElement>(null)
  const [uploading, setUploading] = useState(false)
  const isVideo = kind === 'video'

  const onFile = async (e: React.ChangeEvent<HTMLInputElement>) => {
    const file = e.target.files?.[0]
    e.target.value = ''
    if (!file) return
    setUploading(true)
    try {
      const res = await uploadAdminFile(file)
      onUrl(res.url)
    } finally {
      setUploading(false)
    }
  }

  return (
    <div className="space-y-3">
      <div>
        <label className="mb-1.5 block text-xs font-semibold text-slate-500">
          {isVideo ? 'Video havolasi' : 'Audio havolasi'}
        </label>
        <div className="flex items-center gap-2">
          <input
            value={url}
            onChange={(e) => onUrl(e.target.value)}
            placeholder={isVideo ? 'YouTube yoki MP4 havolasi...' : 'Audio (MP3) havolasi...'}
            className={control}
          />
          <input ref={fileRef} type="file" accept={isVideo ? 'video/*' : 'audio/*'} onChange={onFile} className="hidden" />
          <Button
            variant="secondary"
            type="button"
            onClick={() => fileRef.current?.click()}
            disabled={uploading}
            className="flex-shrink-0"
          >
            {uploading ? <Loader2 className="h-4 w-4 animate-spin" /> : <Upload className="h-4 w-4" />}
            Yuklash
          </Button>
        </div>
        {url && <p className="mt-1 truncate text-xs text-slate-400">{url}</p>}
      </div>

      <div>
        <label className="mb-1.5 block text-xs font-semibold text-slate-500">Davomiyligi (meta)</label>
        <input
          value={meta}
          onChange={(e) => onMeta(e.target.value)}
          placeholder="masalan: 12 daq"
          className={cn(control, 'max-w-[200px]')}
        />
      </div>
    </div>
  )
}

// ============================ PDF tahrirlovchi ============================

interface PdfEditorProps {
  url: string
  name: string
  onChange: (url: string, name: string) => void
}

function PdfEditor({ url, name, onChange }: PdfEditorProps) {
  const fileRef = useRef<HTMLInputElement>(null)
  const [uploading, setUploading] = useState(false)
  const [error, setError] = useState('')

  const onFile = async (e: React.ChangeEvent<HTMLInputElement>) => {
    const file = e.target.files?.[0]
    e.target.value = ''
    if (!file) return
    setError('')
    if (!file.name.toLowerCase().endsWith('.pdf')) {
      setError('Faqat PDF fayl yuklash mumkin')
      return
    }
    if (file.size > 20_000_000) {
      setError('Fayl 20 MB dan katta')
      return
    }
    setUploading(true)
    try {
      const res = await uploadAdminFile(file)
      onChange(res.url, file.name)
    } catch (err) {
      setError(apiErrorMessage(err, 'Yuklashda xato yuz berdi'))
    } finally {
      setUploading(false)
    }
  }

  return (
    <div>
      <label className="mb-1.5 block text-xs font-semibold text-slate-500">PDF fayl</label>
      <input ref={fileRef} type="file" accept=".pdf,application/pdf" onChange={onFile} className="hidden" />

      {url ? (
        <div className="flex items-center gap-3 rounded-xl border border-slate-200 bg-slate-50/70 px-4 py-3">
          <div className="flex h-10 w-10 flex-shrink-0 items-center justify-center rounded-lg bg-red-50 text-red-600">
            <FileType className="h-5 w-5" />
          </div>
          <div className="min-w-0 flex-1">
            <p className="truncate text-sm font-semibold text-slate-800">{name || 'Fayl.pdf'}</p>
            <a
              href={url}
              target="_blank"
              rel="noopener noreferrer"
              className="mt-0.5 inline-flex items-center gap-1 text-xs font-medium text-brand-600 hover:text-brand-700"
            >
              <ExternalLink className="h-3 w-3" /> Ochib ko'rish
            </a>
          </div>
          <Button
            variant="secondary"
            type="button"
            onClick={() => fileRef.current?.click()}
            disabled={uploading}
            className="flex-shrink-0"
          >
            {uploading ? <Loader2 className="h-4 w-4 animate-spin" /> : <Upload className="h-4 w-4" />}
            Almashtirish
          </Button>
          <button
            type="button"
            onClick={() => onChange('', '')}
            className="flex-shrink-0 rounded-lg p-2 text-slate-400 transition-colors hover:bg-red-50 hover:text-red-600"
            title="PDF'ni olib tashlash"
          >
            <X className="h-4 w-4" />
          </button>
        </div>
      ) : (
        <button
          type="button"
          onClick={() => fileRef.current?.click()}
          disabled={uploading}
          className="flex w-full items-center justify-center gap-2.5 rounded-xl border-2 border-dashed border-slate-200 px-4 py-6 text-sm font-medium text-slate-500 transition-colors hover:border-brand-300 hover:bg-brand-50/30 hover:text-brand-600"
        >
          {uploading ? <Loader2 className="h-5 w-5 animate-spin" /> : <Upload className="h-5 w-5" />}
          {uploading ? 'Yuklanmoqda...' : 'PDF fayl tanlash (maks. 20 MB)'}
        </button>
      )}

      {error && (
        <p className="mt-2 flex items-center gap-1.5 text-xs font-medium text-red-600">
          <AlertTriangle className="h-3.5 w-3.5" /> {error}
        </p>
      )}
      <p className="mt-2 text-xs leading-relaxed text-slate-400">
        O'quvchi topshiriqda PDF'ni ichida ko'radi va yuklab olishi mumkin (masalan: qo'llanma,
        mashqlar to'plami, tarqatma material).
      </p>
    </div>
  )
}

// ============================ Lug'at tahrirlovchi ============================

interface VocabEditorProps {
  vocab: VocabEntry[]
  onChange: (v: VocabEntry[]) => void
}

function VocabEditor({ vocab, onChange }: VocabEditorProps) {
  const setRow = (i: number, patch: Partial<VocabEntry>) =>
    onChange(vocab.map((v, idx) => (idx === i ? { ...v, ...patch } : v)))
  const addRow = () => onChange([...vocab, { term: '', meaning: '' }])
  const removeRow = (i: number) => onChange(vocab.filter((_, idx) => idx !== i))

  return (
    <div>
      <label className="mb-1.5 block text-xs font-semibold text-slate-500">So'zlar</label>
      <div className="space-y-2">
        {vocab.length === 0 && <p className="text-xs text-slate-400">Hali so'z qo'shilmagan.</p>}
        {vocab.map((v, i) => (
          <div key={i} className="flex items-center gap-2">
            <input value={v.term} onChange={(e) => setRow(i, { term: e.target.value })} placeholder="So'z" className={control} />
            <input
              value={v.meaning}
              onChange={(e) => setRow(i, { meaning: e.target.value })}
              placeholder="Tarjima"
              className={control}
            />
            <button
              type="button"
              onClick={() => removeRow(i)}
              className="flex-shrink-0 rounded-lg p-2 text-slate-400 transition-colors hover:bg-red-50 hover:text-red-600"
              title="O'chirish"
            >
              <Trash2 className="h-4 w-4" />
            </button>
          </div>
        ))}
      </div>
      <button
        type="button"
        onClick={addRow}
        className="mt-2 inline-flex items-center gap-1.5 rounded-lg px-2 py-1 text-xs font-semibold text-brand-600 transition-colors hover:text-brand-700"
      >
        <Plus className="h-3.5 w-3.5" /> So'z qo'shish
      </button>
    </div>
  )
}

// ============================ Test tuzuvchi ============================

interface TestBuilderProps {
  questions: CourseQuestion[]
  onChange: (q: CourseQuestion[]) => void
}

function TestBuilder({ questions, onChange }: TestBuilderProps) {
  const patchQ = (qid: string, patch: Partial<CourseQuestion>) =>
    onChange(questions.map((q) => (q.id === qid ? { ...q, ...patch } : q)))

  const addQuestion = () => onChange([...questions, { id: genId(), text: '', options: ['', ''], correctIndex: 0 }])

  const removeQuestion = (qid: string) => onChange(questions.filter((q) => q.id !== qid))

  const addOption = (q: CourseQuestion) => patchQ(q.id, { options: [...q.options, ''] })

  const setOption = (q: CourseQuestion, i: number, value: string) =>
    patchQ(q.id, { options: q.options.map((o, idx) => (idx === i ? value : o)) })

  const removeOption = (q: CourseQuestion, i: number) => {
    if (q.options.length <= 2) return
    const options = q.options.filter((_, idx) => idx !== i)
    let correctIndex = q.correctIndex
    if (i === correctIndex) correctIndex = 0
    else if (i < correctIndex) correctIndex -= 1
    patchQ(q.id, { options, correctIndex })
  }

  return (
    <Card
      title="Test tuzuvchi"
      sub="Savollar va variantlar o'quvchiga TASODIFIY tartibda chiqadi. Audio bo'limi ham to'ldirilgan bo'lsa — test o'quvchiga audio bilan birga ('Audio test' — tinglab ishlash) ko'rinadi."
      actions={
        <Button variant="secondary" type="button" onClick={addQuestion}>
          <Plus className="h-4 w-4" /> Savol
        </Button>
      }
    >
      {questions.length === 0 ? (
        <p className="text-sm text-slate-400">Hali savol qo'shilmagan.</p>
      ) : (
        <div className="space-y-4">
          {questions.map((q, qi) => (
            <div key={q.id} className="rounded-xl border border-slate-200 p-3">
              <div className="mb-2 flex items-center justify-between">
                <span className="text-xs font-bold uppercase tracking-wide text-slate-500">{qi + 1}-savol</span>
                <button
                  type="button"
                  onClick={() => removeQuestion(q.id)}
                  className="rounded-md p-1 text-slate-400 transition-colors hover:bg-red-50 hover:text-red-600"
                  title="Savolni o'chirish"
                >
                  <Trash2 className="h-4 w-4" />
                </button>
              </div>

              <input
                value={q.text}
                onChange={(e) => patchQ(q.id, { text: e.target.value })}
                placeholder="Savol matni..."
                className={cn(control, 'font-medium')}
              />

              <div className="mt-2 space-y-2">
                {q.options.map((opt, oi) => {
                  const correct = q.correctIndex === oi
                  return (
                    <div
                      key={oi}
                      className={cn(
                        'flex items-center gap-2 rounded-lg border px-2.5 py-1.5 transition-colors',
                        correct ? 'border-emerald-300 bg-emerald-50' : 'border-slate-200 bg-white',
                      )}
                    >
                      <button
                        type="button"
                        onClick={() => patchQ(q.id, { correctIndex: oi })}
                        title="To'g'ri javob"
                        className={cn(
                          'flex h-5 w-5 flex-shrink-0 items-center justify-center rounded-full border-2 transition-colors',
                          correct
                            ? 'border-emerald-500 bg-emerald-500 text-white'
                            : 'border-slate-300 text-transparent hover:border-emerald-400',
                        )}
                      >
                        <Check className="h-3 w-3" />
                      </button>
                      <input
                        value={opt}
                        onChange={(e) => setOption(q, oi, e.target.value)}
                        placeholder={`Variant ${oi + 1}`}
                        className={cn(
                          'min-w-0 flex-1 bg-transparent text-sm outline-none',
                          correct ? 'font-semibold text-emerald-800' : 'text-slate-700',
                        )}
                      />
                      <button
                        type="button"
                        onClick={() => removeOption(q, oi)}
                        disabled={q.options.length <= 2}
                        className="flex-shrink-0 rounded-md p-1 text-slate-300 transition-colors hover:bg-red-50 hover:text-red-600 disabled:opacity-30 disabled:hover:bg-transparent disabled:hover:text-slate-300"
                        title="Variantni o'chirish"
                      >
                        <Trash2 className="h-3.5 w-3.5" />
                      </button>
                    </div>
                  )
                })}
              </div>

              <button
                type="button"
                onClick={() => addOption(q)}
                className="mt-2 inline-flex items-center gap-1.5 rounded-md px-1.5 py-1 text-xs font-semibold text-brand-600 transition-colors hover:text-brand-700"
              >
                <Plus className="h-3.5 w-3.5" /> variant
              </button>
            </div>
          ))}
        </div>
      )}
    </Card>
  )
}
