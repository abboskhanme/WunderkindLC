import { useEffect, useRef, useState } from 'react'
import {
  Upload,
  FileText,
  FilePlus2,
  Pencil,
  Trash2,
  Plus,
  X,
  Send,
  Download,
  CheckCircle2,
  XCircle,
  AlertTriangle,
  Eye,
  EyeOff,
  Info,
  Search,
} from 'lucide-react'
import type {
  ContractTemplate,
  ContractField,
  ContractDoc,
  StudentRecipient,
  StaffRecipient,
  SendResult,
} from '@/types'
import {
  getTemplates,
  createTemplate,
  createCustomTemplate,
  updateCustomTemplate,
  deleteTemplate,
  getStudentRecipients,
  getStaffRecipients,
  sendContracts,
  downloadContract,
  getContracts,
  uploadContractPdf,
  setContractVisibility,
  deleteContract,
} from '@/api/services/contracts'
import { uploadAdminFile } from '@/api/services/students'
import { Card } from '@/components/ui/Card'
import { Button } from '@/components/ui/Button'
import { Badge } from '@/components/ui/Badge'
import { Modal } from '@/components/ui/Modal'
import { PageHeader } from '@/components/ui/PageHeader'
import { Loader } from '@/components/ui/Loader'
import { cn, formatDate, apiErrorMessage } from '@/lib/utils'
import { usePerm } from '@/lib/permissions'

type Target = 'parent' | 'staff'

const TOKENS: Record<Target, string[]> = {
  parent: [
    '@oquvchi',
    '@tugilgan_kun',
    '@manzil',
    '@oquvchi_telefon',
    '@guruh',
    '@guruhlar',
    '@kurs',
    '@oqituvchi',
    '@oylik_tolov',
    '@chegirma',
    '@qabul_sana',
    '@ota_ona',
    '@telefon',
    '@otasi',
    '@otasi_telefon',
    '@onasi',
    '@onasi_telefon',
    '@markaz',
    '@direktor',
    '@markaz_telefon',
    '@markaz_manzil',
    '@sana',
    '@raqam',
  ],
  staff: [
    '@fish',
    '@telefon',
    '@lavozim',
    '@fanlar',
    '@guruhlar',
    '@tugilgan_kun',
    '@manzil',
    '@oylik',
    '@oylik_foiz',
    '@ish_boshlagan',
    '@markaz',
    '@direktor',
    '@markaz_telefon',
    '@markaz_manzil',
    '@sana',
    '@raqam',
  ],
}

const TOKEN_LABELS: Record<string, string> = {
  '@oquvchi': "O'quvchi F.I.SH",
  '@oquvchi_telefon': "O'quvchi telefoni",
  '@tugilgan_kun': "Tug'ilgan kun",
  '@manzil': 'Manzil',
  '@guruh': 'Asosiy guruh',
  '@guruhlar': 'Barcha faol guruhlar',
  '@kurs': 'Kurs(lar)',
  '@oqituvchi': "O'qituvchi(lar)",
  '@oylik_tolov': "Oylik to'lov (chegirma bilan)",
  '@chegirma': 'Chegirma',
  '@qabul_sana': 'Qabul qilingan sana',
  '@ota_ona': 'Ota-ona F.I.SH',
  '@telefon': 'Telefon (ota-ona/xodim)',
  '@otasi': 'Otasi F.I.SH',
  '@otasi_telefon': 'Otasi telefoni',
  '@onasi': 'Onasi F.I.SH',
  '@onasi_telefon': 'Onasi telefoni',
  '@fish': 'Xodim F.I.SH',
  '@lavozim': 'Lavozim',
  '@fanlar': 'Fanlar/kurslar',
  '@oylik': 'Oylik maosh',
  '@oylik_foiz': 'Maosh foizi (%)',
  '@ish_boshlagan': 'Ishga kirgan sana',
  '@markaz': 'Markaz nomi',
  '@direktor': 'Direktor F.I.SH',
  '@markaz_telefon': 'Markaz telefoni',
  '@markaz_manzil': 'Markaz manzili',
  '@sana': 'Bugungi sana',
  '@raqam': 'Shartnoma raqami',
}

const DOCX = '.docx,application/vnd.openxmlformats-officedocument.wordprocessingml.document'

/** Sahifa bo'limlari: shartnoma tuzish (andoza + oluvchilar) yoki tuzilganlar tarixi. */
type View = 'build' | 'history'

export function ContractsPage() {
  const { can } = usePerm()
  const [view, setView] = useState<View>('build')
  const [target, setTarget] = useState<Target>('staff')
  const [templates, setTemplates] = useState<ContractTemplate[]>([])
  const [selectedTpl, setSelectedTpl] = useState('')
  const [students, setStudents] = useState<StudentRecipient[]>([])
  const [staff, setStaff] = useState<StaffRecipient[]>([])
  const [checked, setChecked] = useState<Set<string>>(new Set())
  const [loading, setLoading] = useState(true)
  const [uploading, setUploading] = useState(false)
  const [sending, setSending] = useState(false)
  const [downloadingKey, setDownloadingKey] = useState<string | null>(null)
  const [results, setResults] = useState<SendResult[] | null>(null)
  const [editor, setEditor] = useState<ContractTemplate | 'new' | null>(null)
  // .docx yuklab olingandan keyingi qisqa eslatma (PDF nusxani qayerdan yuklash haqida)
  const [notice, setNotice] = useState<string | null>(null)

  useEffect(() => {
    // eslint-disable-next-line react-hooks/set-state-in-effect -- target almashganda qayta yuklash (maqsadli)
    setLoading(true)
    setChecked(new Set())
    setResults(null)
    setNotice(null)
    setSelectedTpl('')
    const recipients = target === 'staff' ? getStaffRecipients() : getStudentRecipients()
    Promise.all([getTemplates(target), recipients])
      .then(([tpls, recs]) => {
        setTemplates(tpls)
        setSelectedTpl(tpls[0]?.id ?? '')
        if (target === 'staff') setStaff(recs as StaffRecipient[])
        else setStudents(recs as StudentRecipient[])
      })
      .finally(() => setLoading(false))
  }, [target])

  const refreshRecipients = async () => {
    if (target === 'staff') setStaff(await getStaffRecipients())
    else setStudents(await getStudentRecipients())
  }

  const handleUpload = async (e: React.ChangeEvent<HTMLInputElement>) => {
    const f = e.target.files?.[0]
    if (!f) return
    setUploading(true)
    try {
      const up = await uploadAdminFile(f)
      const tpl = await createTemplate(target, f.name, up.url, f.name)
      setTemplates((prev) => [tpl, ...prev])
      setSelectedTpl(tpl.id)
    } finally {
      setUploading(false)
      e.target.value = ''
    }
  }

  const handleEditorSaved = (tpl: ContractTemplate, isNew: boolean) => {
    setTemplates((prev) => (isNew ? [tpl, ...prev] : prev.map((t) => (t.id === tpl.id ? tpl : t))))
    setSelectedTpl(tpl.id)
    setEditor(null)
  }

  const handleDeleteTpl = async (id: string) => {
    if (!confirm('Andozani o\'chirasizmi?')) return
    await deleteTemplate(id)
    setTemplates((prev) => prev.filter((t) => t.id !== id))
    if (selectedTpl === id) setSelectedTpl('')
  }

  // Oluvchilar ro'yxatini target bo'yicha umumiy ko'rinishga keltiramiz.
  const rows = target === 'staff'
    ? staff.map((s) => ({
        key: s.teacherId,
        name: s.fullName,
        sub: s.phone || '—',
        registered: s.registered,
        lastNumber: s.lastNumber,
      }))
    : students.map((s) => ({
        key: s.studentId,
        name: s.fullName || '(nomsiz)',
        sub: [s.parentName || '—', s.phone || '—', s.groups].filter(Boolean).join(' · '),
        registered: s.registered,
        lastNumber: s.lastNumber,
      }))

  const selectableKeys = rows.filter((r) => r.registered).map((r) => r.key)
  const allChecked = selectableKeys.length > 0 && selectableKeys.every((k) => checked.has(k))

  const toggle = (key: string) =>
    setChecked((prev) => {
      const next = new Set(prev)
      if (next.has(key)) next.delete(key)
      else next.add(key)
      return next
    })

  const toggleAll = () =>
    setChecked(allChecked ? new Set() : new Set(selectableKeys))

  const handleSend = async () => {
    if (!selectedTpl || checked.size === 0) return
    setSending(true)
    setResults(null)
    try {
      const res = await sendContracts(target, selectedTpl, [...checked])
      setResults(res)
      setChecked(new Set())
      await refreshRecipients()
    } finally {
      setSending(false)
    }
  }

  /** "Shartnoma tuzish" — tanlangan andozadan to'ldirilgan Word faylni yuklab olish. */
  const handleDownload = async (key: string) => {
    if (!selectedTpl || downloadingKey) return
    setDownloadingKey(key)
    setNotice(null)
    try {
      await downloadContract(target, selectedTpl, key)
      setNotice(
        "Word (.docx) fayl yuklab olindi. Hujjatni yakunlab, PDF qiling va uni " +
          '"Tuzilgan shartnomalar" bo\'limidagi shu shartnoma qatoridan "PDF yuklash" tugmasi ' +
          'orqali yuklang — PDF yuklanmaguncha shartnoma oluvchining ilovasida ko\'rinmaydi.',
      )
      await refreshRecipients()
    } catch (e) {
      alert(e instanceof Error ? e.message : 'Yuklab olishda xatolik')
    } finally {
      setDownloadingKey(null)
    }
  }

  const sentOk = results?.filter((r) => r.ok).length ?? 0
  const sentFail = results?.filter((r) => !r.ok).length ?? 0

  return (
    <div>
      <PageHeader
        title="Shartnomalar"
        sub={
          view === 'build'
            ? "Andoza tanlang, o'quvchi yoki xodimni tanlab shartnomani Word (.docx) qilib yuklab oling — @-o'rinbosarlar haqiqiy ma'lumot bilan to'ldiriladi"
            : "Tuzilgan shartnomalar tarixi: DOCX nusxa, tayyor PDF'ni yuklash va ilovada ko'rinishi"
        }
        actions={
          <>
            <div className="tabs" role="tablist">
              <button
                type="button"
                className={cn('tab', view === 'build' && 'active')}
                onClick={() => setView('build')}
              >
                Shartnoma tuzish
              </button>
              <button
                type="button"
                className={cn('tab', view === 'history' && 'active')}
                onClick={() => setView('history')}
              >
                Tuzilgan shartnomalar
              </button>
            </div>
            <div className="tabs" role="tablist">
              <button
                type="button"
                className={cn('tab', target === 'staff' && 'active')}
                onClick={() => setTarget('staff')}
              >
                Xodimlar
              </button>
              <button
                type="button"
                className={cn('tab', target === 'parent' && 'active')}
                onClick={() => setTarget('parent')}
              >
                O'quvchilar
              </button>
            </div>
          </>
        }
      />

      {view === 'history' ? (
        <ContractsHistory target={target} />
      ) : loading ? (
        <Card>
          <Loader label="Yuklanmoqda..." />
        </Card>
      ) : (
        <div className="space-y-5">
          {/* .docx yuklab olingandan keyingi eslatma */}
          {notice && (
            <Card className="border border-brand-200 bg-brand-50/60">
              <div className="flex items-start gap-2">
                <Info className="mt-0.5 h-4 w-4 shrink-0 text-brand-600" />
                <p className="flex-1 text-sm text-slate-700">{notice}</p>
                <button
                  type="button"
                  title="Yopish"
                  onClick={() => setNotice(null)}
                  className="rounded-lg p-1 text-slate-400 transition-colors hover:bg-white hover:text-slate-600"
                >
                  <X className="h-4 w-4" />
                </button>
              </div>
            </Card>
          )}

          {/* Andoza paneli */}
          <Card
            title="Andozalar"
            actions={
              <div className="flex flex-wrap items-center gap-2">
                {can('contracts', 'create') && (
                  <>
                    <button
                      type="button"
                      onClick={() => setEditor('new')}
                      className="inline-flex cursor-pointer items-center gap-2 rounded-lg border border-brand-200 bg-brand-50 px-4 py-2 text-sm font-medium text-brand-700 transition-colors hover:bg-brand-100"
                    >
                      <FilePlus2 className="h-4 w-4" />
                      Matnli andoza yaratish
                    </button>
                    <label
                      className={cn(
                        'inline-flex cursor-pointer items-center gap-2 rounded-lg bg-brand-600 px-4 py-2 text-sm font-medium text-white transition-colors hover:bg-brand-700',
                        uploading && 'pointer-events-none opacity-60',
                      )}
                    >
                      <Upload className="h-4 w-4" />
                      {uploading ? 'Yuklanmoqda...' : 'Word yuklash (.docx)'}
                      <input type="file" accept={DOCX} hidden onChange={handleUpload} />
                    </label>
                  </>
                )}
              </div>
            }
          >
            <div className="space-y-2">
              {templates.map((t) => (
                <label
                  key={t.id}
                  className={cn(
                    'flex cursor-pointer items-center gap-3 rounded-xl border p-3 transition-colors',
                    selectedTpl === t.id
                      ? 'border-brand-300 bg-brand-50'
                      : 'border-slate-100 hover:bg-slate-50',
                  )}
                >
                  <input
                    type="radio"
                    name="tpl"
                    checked={selectedTpl === t.id}
                    onChange={() => setSelectedTpl(t.id)}
                    className="h-4 w-4 accent-brand-600"
                  />
                  {t.body ? (
                    <FilePlus2 className="h-5 w-5 text-brand-500" />
                  ) : (
                    <FileText className="h-5 w-5 text-slate-400" />
                  )}
                  <div className="min-w-0 flex-1">
                    <p className="flex items-center gap-2 truncate text-sm font-medium text-slate-800">
                      {t.name || t.fileName || 'Andoza'}
                      {t.body && <Badge tone="violet">Matnli</Badge>}
                    </p>
                    <p className="truncate text-xs text-slate-400">
                      {t.body ? t.body.slice(0, 80) || 'Matnli andoza' : t.fileName}
                    </p>
                  </div>
                  {t.body && can('contracts', 'edit') && (
                    <button
                      type="button"
                      title="Tahrirlash"
                      onClick={(e) => {
                        e.preventDefault()
                        setEditor(t)
                      }}
                      className="rounded-lg p-1.5 text-slate-400 transition-colors hover:bg-brand-50 hover:text-brand-600"
                    >
                      <Pencil className="h-4 w-4" />
                    </button>
                  )}
                  {can('contracts', 'delete') && (
                    <button
                      type="button"
                      title="O'chirish"
                      onClick={(e) => {
                        e.preventDefault()
                        handleDeleteTpl(t.id)
                      }}
                      className="rounded-lg p-1.5 text-slate-400 transition-colors hover:bg-red-50 hover:text-red-600"
                    >
                      <Trash2 className="h-4 w-4" />
                    </button>
                  )}
                </label>
              ))}
              {templates.length === 0 && (
                <p className="py-4 text-center text-sm text-slate-400">
                  Hali andoza yo'q. "Matnli andoza yaratish" tugmasi orqali matn yozing yoki Word (.docx) yuklang.
                </p>
              )}
            </div>

            {/* Token yordami — barcha o'rinbosarlar izohi bilan */}
            <div className="mt-3 rounded-lg bg-slate-50 p-3">
              <p className="mb-2 text-xs font-medium text-slate-500">
                Andozada quyidagi o'rinbosarlardan foydalaning — shartnoma tuzilganda haqiqiy ma'lumotga almashtiriladi:
              </p>
              <div className="grid grid-cols-1 gap-x-4 gap-y-1 sm:grid-cols-2 lg:grid-cols-3">
                {TOKENS[target].map((tok) => (
                  <div key={tok} className="flex items-center gap-2 text-xs">
                    <code className="shrink-0 rounded bg-white px-1.5 py-0.5 font-mono font-medium text-brand-700 ring-1 ring-slate-200">
                      {tok}
                    </code>
                    <span className="truncate text-slate-500">{TOKEN_LABELS[tok]}</span>
                  </div>
                ))}
              </div>
            </div>
          </Card>

          {/* Yuborish natijasi */}
          {results && (
            <Card className={cn('border', sentFail ? 'border-amber-200' : 'border-emerald-200')}>
              <p className="text-sm font-medium text-slate-700">
                <span className="font-mono">{sentOk}</span> ta yuborildi
                {sentFail > 0 && (
                  <>
                    , <span className="font-mono">{sentFail}</span> ta yuborilmadi
                  </>
                )}
              </p>
              {sentFail > 0 && (
                <ul className="mt-2 space-y-1 text-xs text-slate-500">
                  {results
                    .filter((r) => !r.ok)
                    .map((r) => (
                      <li key={r.recipientKey} className="flex items-center gap-1">
                        <XCircle className="h-3 w-3 text-red-500" /> {r.message}
                      </li>
                    ))}
                </ul>
              )}
            </Card>
          )}

          {/* Oluvchilar */}
          <Card
            tight
            title={`Oluvchilar ${target === 'staff' ? '(xodimlar)' : "(o'quvchilar)"}`}
            actions={
              <Button onClick={handleSend} disabled={!selectedTpl || checked.size === 0 || sending}>
                <Send className="h-4 w-4" />
                {sending ? 'Yuborilmoqda...' : `Telegram orqali yuborish (${checked.size})`}
              </Button>
            }
          >
            <div className="table-wrap">
              <table className="table">
                <thead>
                  <tr>
                    <th className="w-10">
                      <input
                        type="checkbox"
                        checked={allChecked}
                        onChange={toggleAll}
                        disabled={selectableKeys.length === 0}
                        className="h-4 w-4 accent-brand-600"
                      />
                    </th>
                    <th>{target === 'staff' ? 'F.I.SH' : "O'quvchi"}</th>
                    <th>{target === 'staff' ? 'Telefon' : 'Ota-ona · telefon · guruh'}</th>
                    <th>Telegram</th>
                    <th className="num">Oxirgi raqam</th>
                    <th className="w-40">Shartnoma</th>
                  </tr>
                </thead>
                <tbody>
                  {rows.map((r) => (
                    <tr key={r.key} className={cn(!r.registered && 'opacity-60')}>
                      <td>
                        <input
                          type="checkbox"
                          checked={checked.has(r.key)}
                          disabled={!r.registered}
                          onChange={() => toggle(r.key)}
                          className="h-4 w-4 accent-brand-600 disabled:cursor-not-allowed"
                        />
                      </td>
                      <td className="font-medium text-slate-800">{r.name}</td>
                      <td className="text-slate-500">{r.sub}</td>
                      <td>
                        {r.registered ? (
                          <Badge tone="green">
                            <CheckCircle2 className="h-3 w-3" /> Ro'yxatda
                          </Badge>
                        ) : (
                          <Badge tone="amber">
                            <AlertTriangle className="h-3 w-3" /> Ro'yxatdan o'tmagan
                          </Badge>
                        )}
                      </td>
                      <td className="num text-slate-600">
                        {r.lastNumber != null ? `№ ${r.lastNumber}` : '—'}
                      </td>
                      <td>
                        <button
                          type="button"
                          onClick={() => handleDownload(r.key)}
                          disabled={!selectedTpl || downloadingKey !== null}
                          title={
                            selectedTpl
                              ? "Shartnomani to'ldirib Word (.docx) yuklab olish"
                              : 'Avval andoza tanlang'
                          }
                          className="inline-flex items-center gap-1.5 rounded-lg border border-brand-200 bg-brand-50 px-2.5 py-1.5 text-xs font-medium text-brand-700 transition-colors hover:bg-brand-100 disabled:cursor-not-allowed disabled:opacity-50"
                        >
                          <Download className="h-3.5 w-3.5" />
                          {downloadingKey === r.key ? 'Tayyorlanmoqda...' : 'Shartnoma tuzish'}
                        </button>
                      </td>
                    </tr>
                  ))}
                  {rows.length === 0 && (
                    <tr>
                      <td colSpan={6} className="px-4 py-12 text-center text-slate-400">
                        Oluvchi yo'q
                      </td>
                    </tr>
                  )}
                </tbody>
              </table>
            </div>
            <p className="border-t border-slate-100 px-4 py-3 text-xs text-slate-400">
              "Shartnoma tuzish" — tanlangan andozadan to'ldirilgan Word (.docx) faylni yuklab beradi
              (Telegram shart emas). Telegram orqali yuborish esa faqat botda ro'yxatdan o'tganlarga ishlaydi
              ({target === 'staff' ? 'xodim telefoni' : 'ota-ona telefoni'} bilan moslashadi).
            </p>
          </Card>
        </div>
      )}

      {editor && (
        <CustomTemplateModal
          target={target}
          template={editor === 'new' ? null : editor}
          onClose={() => setEditor(null)}
          onSaved={handleEditorSaved}
        />
      )}
    </div>
  )
}

/**
 * "Tuzilgan shartnomalar" bo'limi — saqlangan nusxalar tarixi.
 * DOCX/PDF yuklab olish, tayyor PDF'ni yuklash, ilovada ko'rinishini boshqarish, o'chirish.
 */
function ContractsHistory({ target }: { target: Target }) {
  const { can } = usePerm()
  const [docs, setDocs] = useState<ContractDoc[]>([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const [q, setQ] = useState('')
  const [query, setQuery] = useState('') // qidiruv tugmasi/Enter bosilganda qo'llanadi
  const [busyId, setBusyId] = useState<string | null>(null)
  const [toDelete, setToDelete] = useState<ContractDoc | null>(null)
  const [deleting, setDeleting] = useState(false)
  // Server javobida `visible` bo'lmasa (DTO'da ixtiyoriy), oxirgi amalni shu yerda eslab qolamiz.
  const [visOverride, setVisOverride] = useState<Record<string, boolean>>({})

  /** Yozuv ilovada ko'rinadimi: server qiymati ustun, bo'lmasa lokal o'zgarish, standart — ko'rinadi. */
  const isVisible = (d: ContractDoc) => d.visible ?? visOverride[d.id] ?? true

  useEffect(() => {
    let alive = true
    // eslint-disable-next-line react-hooks/set-state-in-effect -- target/qidiruv almashganda qayta yuklash (maqsadli)
    setLoading(true)
    setError(null)
    getContracts({ target, q: query || undefined })
      .then((d) => alive && setDocs(d))
      .catch((e) => alive && setError(apiErrorMessage(e, 'Yuklab bo\'lmadi')))
      .finally(() => alive && setLoading(false))
    return () => {
      alive = false
    }
  }, [target, query])

  /** Yuklangan/o'zgargan yozuvni ro'yxatda almashtirish. */
  const replaceDoc = (doc: ContractDoc) =>
    setDocs((prev) => prev.map((d) => (d.id === doc.id ? doc : d)))

  /** Tayyor PDF'ni yuklash — avval faylni serverga yuklaymiz, keyin yozuvga bog'laymiz. */
  const handleUploadPdf = async (doc: ContractDoc, e: React.ChangeEvent<HTMLInputElement>) => {
    const f = e.target.files?.[0]
    e.target.value = ''
    if (!f) return
    // Faqat PDF: boshqa tur tanlansa yuklashga urinmaymiz.
    const isPdf = f.type === 'application/pdf' || f.name.toLowerCase().endsWith('.pdf')
    if (!isPdf) {
      alert('Faqat PDF fayl yuklash mumkin')
      return
    }
    setBusyId(doc.id)
    try {
      const up = await uploadAdminFile(f)
      replaceDoc(await uploadContractPdf(doc.id, up.url, f.name))
    } catch (err) {
      alert(apiErrorMessage(err, 'Yuklashda xatolik'))
    } finally {
      setBusyId(null)
    }
  }

  /** Ilovada ko'rinishini yoqish/o'chirish. */
  const handleVisibility = async (doc: ContractDoc) => {
    const next = !isVisible(doc)
    setBusyId(doc.id)
    try {
      replaceDoc(await setContractVisibility(doc.id, next))
      setVisOverride((prev) => ({ ...prev, [doc.id]: next }))
    } catch (err) {
      alert(apiErrorMessage(err, "O'zgartirib bo'lmadi"))
    } finally {
      setBusyId(null)
    }
  }

  const handleDelete = async () => {
    if (!toDelete) return
    setDeleting(true)
    try {
      await deleteContract(toDelete.id)
      setDocs((prev) => prev.filter((d) => d.id !== toDelete.id))
      setToDelete(null)
    } catch (err) {
      alert(apiErrorMessage(err, "O'chirib bo'lmadi"))
    } finally {
      setDeleting(false)
    }
  }

  return (
    <>
      <Card
        tight
        title={`Tuzilgan shartnomalar ${target === 'staff' ? '(xodimlar)' : "(o'quvchilar)"}`}
        sub={`Jami: ${docs.length} ta`}
        actions={
          <form
            className="flex items-center gap-2"
            onSubmit={(e) => {
              e.preventDefault()
              setQuery(q.trim())
            }}
          >
            <div className="relative">
              <Search className="pointer-events-none absolute left-2.5 top-1/2 h-4 w-4 -translate-y-1/2 text-slate-400" />
              <input
                value={q}
                onChange={(e) => setQ(e.target.value)}
                placeholder="Oluvchi yoki raqam..."
                className="w-56 rounded-lg border border-slate-200 py-1.5 pl-8 pr-3 text-sm outline-none focus:border-brand-400 focus:ring-2 focus:ring-brand-100"
              />
            </div>
            <Button type="submit" variant="ghost">
              Qidirish
            </Button>
          </form>
        }
      >
        {loading ? (
          <div className="p-5">
            <Loader label="Yuklanmoqda..." />
          </div>
        ) : error ? (
          <p className="px-4 py-12 text-center text-sm text-red-500">{error}</p>
        ) : (
          <div className="table-wrap">
            <table className="table">
              <thead>
                <tr>
                  <th className="num w-16">№</th>
                  <th>Oluvchi</th>
                  <th>Andoza</th>
                  <th className="w-28">Sana</th>
                  <th className="w-56">Holat</th>
                  <th className="w-52">Fayllar</th>
                  <th className="w-52">Amallar</th>
                </tr>
              </thead>
              <tbody>
                {docs.map((d) => {
                  const visible = isVisible(d)
                  const busy = busyId === d.id
                  return (
                    <tr key={d.id} className={cn(!visible && 'opacity-60')}>
                      <td className="num font-mono text-slate-600">{d.number}</td>
                      <td className="font-medium text-slate-800">{d.recipientName || '—'}</td>
                      <td className="text-slate-500">{d.templateName || '—'}</td>
                      <td className="text-slate-500">{d.date ? formatDate(d.date) : '—'}</td>
                      <td>
                        <div className="flex flex-wrap items-center gap-1">
                          {d.pdfUrl ? (
                            <Badge tone="green">
                              <CheckCircle2 className="h-3 w-3" /> PDF yuklangan
                            </Badge>
                          ) : (
                            <Badge tone="amber">PDF yuklanmagan</Badge>
                          )}
                          {d.delivered && <Badge tone="blue">Yuborilgan</Badge>}
                          {!visible && (
                            <Badge tone="default">
                              <EyeOff className="h-3 w-3" /> Yashirilgan
                            </Badge>
                          )}
                        </div>
                      </td>
                      <td>
                        <div className="flex flex-wrap items-center gap-2 text-xs font-medium">
                          {d.docxUrl ? (
                            <a
                              href={d.docxUrl}
                              target="_blank"
                              rel="noreferrer"
                              className="inline-flex items-center gap-1 text-brand-600 hover:underline"
                            >
                              <Download className="h-3.5 w-3.5" /> DOCX
                            </a>
                          ) : (
                            <span className="text-slate-300">DOCX yo'q</span>
                          )}
                          {d.pdfUrl ? (
                            <a
                              href={d.pdfUrl}
                              target="_blank"
                              rel="noreferrer"
                              className="inline-flex items-center gap-1 text-emerald-600 hover:underline"
                            >
                              <Download className="h-3.5 w-3.5" /> PDF
                            </a>
                          ) : (
                            <span className="text-slate-300">PDF yo'q</span>
                          )}
                        </div>
                      </td>
                      <td>
                        <div className="flex flex-wrap items-center gap-1.5">
                          {can('contracts', 'edit') && (
                            <>
                              <label
                                title={
                                  d.pdfUrl
                                    ? "Yuklangan PDF'ni yangisiga almashtirish"
                                    : 'Tayyor PDF nusxani yuklash'
                                }
                                className={cn(
                                  'inline-flex cursor-pointer items-center gap-1 rounded-lg border border-brand-200 bg-brand-50 px-2 py-1.5 text-xs font-medium text-brand-700 transition-colors hover:bg-brand-100',
                                  busy && 'pointer-events-none opacity-60',
                                )}
                              >
                                <Upload className="h-3.5 w-3.5" />
                                {busy
                                  ? 'Yuklanmoqda...'
                                  : d.pdfUrl
                                    ? 'PDF almashtirish'
                                    : 'PDF yuklash'}
                                <input
                                  type="file"
                                  accept="application/pdf"
                                  hidden
                                  onChange={(e) => handleUploadPdf(d, e)}
                                />
                              </label>
                              <button
                                type="button"
                                title={visible ? "Ilovada yashirish" : "Ilovada ko'rsatish"}
                                disabled={busy}
                                onClick={() => handleVisibility(d)}
                                className="rounded-lg p-1.5 text-slate-400 transition-colors hover:bg-brand-50 hover:text-brand-600 disabled:opacity-50"
                              >
                                {visible ? <Eye className="h-4 w-4" /> : <EyeOff className="h-4 w-4" />}
                              </button>
                            </>
                          )}
                          {can('contracts', 'delete') && (
                            <button
                              type="button"
                              title="O'chirish"
                              disabled={busy}
                              onClick={() => setToDelete(d)}
                              className="rounded-lg p-1.5 text-slate-400 transition-colors hover:bg-red-50 hover:text-red-600 disabled:opacity-50"
                            >
                              <Trash2 className="h-4 w-4" />
                            </button>
                          )}
                        </div>
                      </td>
                    </tr>
                  )
                })}
                {docs.length === 0 && (
                  <tr>
                    <td colSpan={7} className="px-4 py-12 text-center text-slate-400">
                      Shartnoma hali tuzilmagan
                    </td>
                  </tr>
                )}
              </tbody>
            </table>
          </div>
        )}
        <p className="border-t border-slate-100 px-4 py-3 text-xs text-slate-400">
          PDF'ni tizim hosil qilmaydi: hujjatni Word'da yakunlab, o'zingiz PDF qiling va shu yerdan yuklang.
          Shartnoma o'quvchi/o'qituvchi ilovasida faqat PDF yuklangandan keyin ko'rinadi;
          "ko'z" belgisi esa ko'rinishni qo'lda yoqib/o'chirib turadi.
        </p>
      </Card>

      {/* O'chirishni tasdiqlash */}
      {toDelete && (
        <Modal
          open
          onClose={() => setToDelete(null)}
          title="Shartnomani o'chirish"
          footer={
            <>
              <Button variant="ghost" onClick={() => setToDelete(null)}>
                Bekor qilish
              </Button>
              <Button variant="danger" onClick={handleDelete} disabled={deleting}>
                {deleting ? "O'chirilmoqda..." : "O'chirish"}
              </Button>
            </>
          }
        >
          <p className="text-sm text-slate-600">
            <span className="font-medium text-slate-800">
              {toDelete.title || `Shartnoma № ${toDelete.number}`}
            </span>{' '}
            ({toDelete.recipientName}) yozuvi va saqlangan fayllari butunlay o'chiriladi.
            Bu amalni qaytarib bo'lmaydi.
          </p>
        </Modal>
      )}
    </>
  )
}

/** Custom (matnli) andoza yaratish/tahrirlash modali — token palitrasi bilan. */
function CustomTemplateModal({
  target,
  template,
  onClose,
  onSaved,
}: {
  target: Target
  template: ContractTemplate | null
  onClose: () => void
  onSaved: (tpl: ContractTemplate, isNew: boolean) => void
}) {
  const isNew = template === null
  const [name, setName] = useState(template?.name ?? '')
  const [body, setBody] = useState(template?.body ?? '')
  const [fields, setFields] = useState<ContractField[]>(template?.fields ?? [])
  const [saving, setSaving] = useState(false)
  const bodyRef = useRef<HTMLTextAreaElement>(null)

  // Token kalitini normallashtirish: bitta "@" + faqat harf/pastki chiziq (backend regex bilan mos).
  const cleanKey = (raw: string) => {
    const c = raw.replace(/^@+/, '').replace(/[^A-Za-z_]/g, '')
    return c ? '@' + c : ''
  }

  // Andoza matniga kiritish mumkin bo'lgan custom kalitlar (bo'sh bo'lmaganlari).
  const customKeys = fields.map((f) => cleanKey(f.key)).filter((k) => k.length > 1)

  const insertToken = (tok: string) => {
    const el = bodyRef.current
    if (!el) {
      setBody((b) => b + tok)
      return
    }
    const start = el.selectionStart
    const end = el.selectionEnd
    setBody((b) => b.slice(0, start) + tok + b.slice(end))
    // Kursorni qo'shilgan token oxiriga qo'yamiz.
    requestAnimationFrame(() => {
      el.focus()
      el.selectionStart = el.selectionEnd = start + tok.length
    })
  }

  const addField = () => setFields((f) => [...f, { key: '', value: '' }])
  const updateField = (i: number, patch: Partial<ContractField>) =>
    setFields((f) => f.map((x, idx) => (idx === i ? { ...x, ...patch } : x)))
  const removeField = (i: number) => setFields((f) => f.filter((_, idx) => idx !== i))

  const handleSave = async () => {
    if (!body.trim()) return
    // Tozalangan custom o'rinbosarlar (bo'sh kalit chiqarib tashlanadi).
    const cleanFields = fields
      .map((f) => ({ key: cleanKey(f.key), value: f.value.trim() }))
      .filter((f) => f.key.length > 1)
    setSaving(true)
    try {
      const tpl = isNew
        ? await createCustomTemplate(target, name.trim(), body.trim(), cleanFields)
        : await updateCustomTemplate(template.id, target, name.trim(), body.trim(), cleanFields)
      onSaved(tpl, isNew)
    } catch (e) {
      alert(e instanceof Error ? e.message : 'Saqlashda xatolik')
      setSaving(false)
    }
  }

  return (
    <Modal
      open
      onClose={onClose}
      size="lg"
      title={isNew ? 'Matnli andoza yaratish' : 'Matnli andozani tahrirlash'}
      footer={
        <>
          <Button variant="ghost" onClick={onClose}>
            Bekor qilish
          </Button>
          <Button onClick={handleSave} disabled={!body.trim() || saving}>
            {saving ? 'Saqlanmoqda...' : 'Saqlash'}
          </Button>
        </>
      }
    >
      <div className="space-y-4">
        <div>
          <label className="mb-1 block text-sm font-medium text-slate-700">Andoza nomi</label>
          <input
            value={name}
            onChange={(e) => setName(e.target.value)}
            placeholder="Masalan: O'quvchi shartnomasi"
            className="w-full rounded-lg border border-slate-200 px-3 py-2 text-sm outline-none focus:border-brand-400 focus:ring-2 focus:ring-brand-100"
          />
        </div>

        <div>
          <p className="mb-1.5 text-xs font-medium text-slate-500">
            O'rinbosar qo'shish uchun bosing (shartnoma tuzilganda haqiqiy ma'lumotga almashtiriladi):
          </p>
          <div className="flex flex-wrap gap-1.5">
            {TOKENS[target].map((tok) => (
              <button
                key={tok}
                type="button"
                onClick={() => insertToken(tok)}
                title={TOKEN_LABELS[tok]}
                className="rounded bg-brand-50 px-2 py-1 font-mono text-xs font-medium text-brand-700 ring-1 ring-brand-200 transition-colors hover:bg-brand-100"
              >
                {tok}
              </button>
            ))}
            {customKeys.map((tok) => (
              <button
                key={tok}
                type="button"
                onClick={() => insertToken(tok)}
                title="Qo'shimcha o'rinbosar"
                className="rounded bg-violet-100 px-2 py-1 font-mono text-xs font-medium text-violet-700 ring-1 ring-violet-200 transition-colors hover:bg-violet-200"
              >
                {tok}
              </button>
            ))}
          </div>
        </div>

        {/* Foydalanuvchi aniqlagan qo'shimcha o'rinbosarlar (doimiy qiymat) */}
        <div className="rounded-xl border border-slate-100 bg-slate-50/60 p-3">
          <div className="mb-2 flex items-center justify-between">
            <div>
              <p className="text-sm font-medium text-slate-700">Qo'shimcha o'rinbosarlar</p>
              <p className="text-xs text-slate-400">
                O'zingiz nomlagan @-token + doimiy qiymat (masalan @direktor = "Aliyev A.")
              </p>
            </div>
            <button
              type="button"
              onClick={addField}
              className="inline-flex items-center gap-1 rounded-lg border border-brand-200 bg-white px-2.5 py-1.5 text-xs font-medium text-brand-700 transition-colors hover:bg-brand-50"
            >
              <Plus className="h-3.5 w-3.5" /> Qo'shish
            </button>
          </div>
          {fields.length === 0 ? (
            <p className="py-1 text-center text-xs text-slate-400">
              Qo'shimcha o'rinbosar yo'q. Kerak bo'lsa "Qo'shish" bosing.
            </p>
          ) : (
            <div className="space-y-2">
              {fields.map((f, i) => (
                <div key={i} className="flex items-center gap-2">
                  <div className="relative w-44 shrink-0">
                    <span className="pointer-events-none absolute left-2.5 top-1/2 -translate-y-1/2 font-mono text-sm text-slate-400">
                      @
                    </span>
                    <input
                      value={f.key.replace(/^@+/, '')}
                      onChange={(e) => updateField(i, { key: cleanKey(e.target.value) })}
                      placeholder="direktor"
                      className="w-full rounded-lg border border-slate-200 py-1.5 pl-6 pr-2 font-mono text-sm outline-none focus:border-brand-400 focus:ring-2 focus:ring-brand-100"
                    />
                  </div>
                  <input
                    value={f.value}
                    onChange={(e) => updateField(i, { value: e.target.value })}
                    placeholder="Qiymati (masalan: Aliyev A.)"
                    className="min-w-0 flex-1 rounded-lg border border-slate-200 px-3 py-1.5 text-sm outline-none focus:border-brand-400 focus:ring-2 focus:ring-brand-100"
                  />
                  <button
                    type="button"
                    title="O'chirish"
                    onClick={() => removeField(i)}
                    className="rounded-lg p-1.5 text-slate-400 transition-colors hover:bg-red-50 hover:text-red-600"
                  >
                    <X className="h-4 w-4" />
                  </button>
                </div>
              ))}
            </div>
          )}
        </div>

        <div>
          <label className="mb-1 block text-sm font-medium text-slate-700">Andoza matni</label>
          <textarea
            ref={bodyRef}
            value={body}
            onChange={(e) => setBody(e.target.value)}
            rows={14}
            placeholder={
              target === 'staff'
                ? "SHARTNOMA № @raqam\n\nSana: @sana\n\nUshbu shartnoma bir tomondan @markaz (direktor: @direktor) va ikkinchi tomondan\n@fish (tel: @telefon) o'rtasida tuzildi.\n\nLavozim: @lavozim. Oylik maosh: @oylik so'm."
                : "SHARTNOMA № @raqam\n\nSana: @sana\n\nUshbu shartnoma bir tomondan @markaz (direktor: @direktor) va ikkinchi tomondan\n@ota_ona (tel: @telefon) o'rtasida tuzildi.\n\nO'quvchi: @oquvchi. Kurs: @kurs (@guruh guruhi).\nOylik to'lov: @oylik_tolov so'm."
            }
            className="w-full rounded-lg border border-slate-200 px-3 py-2 font-mono text-sm leading-relaxed outline-none focus:border-brand-400 focus:ring-2 focus:ring-brand-100"
          />
          <p className="mt-1 text-xs text-slate-400">
            Har bir qator alohida paragrafga aylanadi. Shartnoma tuzilganda matndan .docx hosil qilinadi.
          </p>
        </div>
      </div>
    </Modal>
  )
}
