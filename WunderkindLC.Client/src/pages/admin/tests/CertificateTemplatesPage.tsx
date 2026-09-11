import { useEffect, useState } from 'react'
import { Link } from 'react-router-dom'
import {
  ArrowLeft, Plus, Pencil, Trash2, FileText, Upload, Loader2, AlertTriangle,
  Star, Eye, EyeOff, Check, Copy, Award, Info, ImageIcon,
} from 'lucide-react'
import type {
  TestCertificateTemplate,
  CertificateToken,
  CertificatePhotoHelp,
  TestCertificateTemplatePayload,
} from '@/api/services/testCertificates'
import {
  getCertificateTokens,
  getCertificateTemplates,
  createCertificateTemplate,
  updateCertificateTemplate,
  deleteCertificateTemplate,
} from '@/api/services/testCertificates'
import { uploadAdminFile } from '@/api/services/students'
import { Card } from '@/components/ui/Card'
import { Button } from '@/components/ui/Button'
import { Loader } from '@/components/ui/Loader'
import { Modal } from '@/components/ui/Modal'
import { Input } from '@/components/ui/Input'
import { Badge } from '@/components/ui/Badge'
import { PageHeader } from '@/components/ui/PageHeader'
import { apiErrorMessage, cn, formatDateTime, copyText } from '@/lib/utils'
import { usePerm } from '@/lib/permissions'

/**
 * "O'quv bo'limi → Testlar natijalari → Sertifikat shablonlari".
 *
 * Sertifikat Word (.docx) andozasidan yasaladi: hujjat ichidagi `@fish`, `@guruh` kabi belgilar
 * sertifikat yaratilganda qiymat bilan almashtiriladi. Andozalarni FAQAT admin boshqaradi
 * (`AdminPerm("classes")` — bu yerda `usePerm` bilan bir xil qoida), o'qituvchi esa test
 * yaratishda tayyor andozani tanlaydi.
 */

/** Faqat Word (.docx) qabul qilinadi — server ham shu formatni kutadi. */
const DOCX_ACCEPT =
  'application/vnd.openxmlformats-officedocument.wordprocessingml.document,.docx'

const isDocx = (file: File) => file.name.toLowerCase().endsWith('.docx')

export function CertificateTemplatesPage() {
  const { can } = usePerm()
  const [templates, setTemplates] = useState<TestCertificateTemplate[]>([])
  const [tokens, setTokens] = useState<CertificateToken[]>([])
  /** O'quvchi surati bo'yicha yo'riqnoma (serverdan — kodda takrorlanmasin). */
  const [photoHelp, setPhotoHelp] = useState<CertificatePhotoHelp | null>(null)
  const [pdfAvailable, setPdfAvailable] = useState(true)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState('')

  const [formOpen, setFormOpen] = useState(false)
  const [editing, setEditing] = useState<TestCertificateTemplate | null>(null)
  const [deleting, setDeleting] = useState<TestCertificateTemplate | null>(null)
  const [deleteBusy, setDeleteBusy] = useState(false)
  const [deleteError, setDeleteError] = useState('')
  /** Qatordagi tezkor amal (standart qilish / faol-nofaol) davom etayotgan andoza id'si */
  const [rowBusy, setRowBusy] = useState('')
  const [copied, setCopied] = useState('')

  const canCreate = can('classes.testResults', 'create')
  const canEdit = can('classes.testResults', 'edit')
  const canDelete = can('classes.testResults', 'delete')

  useEffect(() => {
    let active = true
    Promise.all([getCertificateTemplates(), getCertificateTokens()])
      .then(([list, info]) => {
        if (!active) return
        setTemplates(list)
        setTokens(info.tokens)
        setPhotoHelp(info.photoHelp ?? null)
        setPdfAvailable(info.pdfAvailable)
      })
      .catch((e) => active && setError(apiErrorMessage(e, "Yuklab bo'lmadi")))
      .finally(() => active && setLoading(false))
    return () => {
      active = false
    }
  }, [])

  /** Saqlangan andozani ro'yxatga qo'shadi/yangilaydi. Standart bittagina bo'ladi. */
  const applySaved = (t: TestCertificateTemplate) => {
    setTemplates((prev) => {
      const exists = prev.some((x) => x.id === t.id)
      const next = exists ? prev.map((x) => (x.id === t.id ? t : x)) : [t, ...prev]
      return t.isDefault ? next.map((x) => (x.id === t.id ? x : { ...x, isDefault: false })) : next
    })
  }

  const handleSaved = (t: TestCertificateTemplate) => {
    applySaved(t)
    setFormOpen(false)
  }

  /** Standart qilish / faol-nofaol — bitta maydonni yangilaydigan tezkor amal. */
  const patch = async (
    t: TestCertificateTemplate,
    payload: Omit<TestCertificateTemplatePayload, 'name'>,
  ) => {
    setRowBusy(t.id)
    setError('')
    try {
      const saved = await updateCertificateTemplate(t.id, { name: t.name, ...payload })
      applySaved(saved)
    } catch (e) {
      setError(apiErrorMessage(e, "Saqlab bo'lmadi"))
    } finally {
      setRowBusy('')
    }
  }

  const confirmDelete = async () => {
    if (!deleting) return
    setDeleteBusy(true)
    setDeleteError('')
    try {
      await deleteCertificateTemplate(deleting.id)
      setTemplates((prev) => prev.filter((x) => x.id !== deleting.id))
      setDeleting(null)
    } catch (e) {
      // Server 400 qaytaradi (masalan: shu andoza bo'yicha sertifikat berilgan) — sababini ko'rsatamiz.
      setDeleteError(apiErrorMessage(e, "O'chirib bo'lmadi"))
    } finally {
      setDeleteBusy(false)
    }
  }

  const copyToken = async (token: string) => {
    const ok = await copyText(token)
    if (!ok) return
    setCopied(token)
    setTimeout(() => setCopied(''), 1500)
  }

  return (
    <div>
      <PageHeader
        title="Sertifikat shablonlari"
        sub="Test sertifikati shu Word (.docx) andozalaridan yasaladi"
        actions={
          canCreate && (
            <Button
              onClick={() => {
                setEditing(null)
                setFormOpen(true)
              }}
            >
              <Plus className="h-4 w-4" /> Yangi shablon
            </Button>
          )
        }
      />

      <Link
        to="/admin/test-results"
        className="mb-4 inline-flex items-center gap-1.5 text-sm font-medium text-slate-500 transition-colors hover:text-brand-600"
      >
        <ArrowLeft className="h-4 w-4" /> Testlar natijalariga qaytish
      </Link>

      {loading ? (
        <Loader label="Yuklanmoqda..." />
      ) : (
        <div className="space-y-4">
          {!pdfAvailable && (
            <div className="flex items-start gap-3 rounded-xl border border-amber-200 bg-amber-50 px-4 py-3">
              <AlertTriangle className="mt-0.5 h-5 w-5 shrink-0 text-amber-500" />
              <div className="text-sm text-amber-800">
                <p className="font-semibold">Serverda PDF konvertori (LibreOffice) o'rnatilmagan.</p>
                <p className="mt-0.5 text-amber-700">
                  Sertifikatlar Word (.docx) ko'rinishida saqlanadi — ular baribir yaratiladi va
                  yuklab olinadi, faqat PDF nusxasi bo'lmaydi.
                </p>
              </div>
            </div>
          )}

          {error && <Card className="py-3 text-center text-sm text-red-500">{error}</Card>}

          {/* ---- O'zgaruvchilar (yo'riqnoma) ---- */}
          <Card
            title="O'zgaruvchilar — Word hujjatida nima yozasiz"
            sub="Belgini bosing — nusxalanadi, so'ng andozaga qo'ying"
          >
            <div className="mb-3 flex items-start gap-2.5 rounded-lg bg-slate-50 px-3.5 py-3 text-sm text-slate-600">
              <Info className="mt-0.5 h-4 w-4 shrink-0 text-brand-500" />
              <p>
                Word hujjatida quyidagi belgilarni <b>aynan shu ko'rinishda</b> yozing (masalan{' '}
                <code className="rounded bg-white px-1 py-0.5 font-mono text-[12px] text-brand-600">
                  @fish
                </code>
                ). Sertifikat yaratilganda ular o'quvchining haqiqiy qiymatiga almashtiriladi.
                Noma'lum belgi o'z holicha qoladi. Hujjatning formatlashi — shrift, rang, o'lcham va
                joylashuv — o'zgarmaydi, faqat matn almashadi.
              </p>
            </div>

            {tokens.length === 0 ? (
              <p className="py-4 text-center text-sm text-slate-400">O'zgaruvchilar ro'yxati bo'sh</p>
            ) : (
              <div className="overflow-x-auto">
                <table className="w-full text-sm">
                  <thead>
                    <tr className="border-b border-slate-100 text-left text-xs font-semibold text-slate-400">
                      <th className="py-2 pr-3">Belgi</th>
                      <th className="py-2 pr-3">Ma'nosi</th>
                      <th className="py-2">Namuna</th>
                    </tr>
                  </thead>
                  <tbody>
                    {tokens.map((t) => (
                      <tr key={t.token} className="border-b border-slate-50 last:border-0">
                        <td className="py-1.5 pr-3">
                          <button
                            type="button"
                            onClick={() => void copyToken(t.token)}
                            title="Nusxalash"
                            className={cn(
                              'inline-flex items-center gap-1.5 rounded-lg border px-2 py-1 font-mono text-[13px] font-semibold transition-colors',
                              copied === t.token
                                ? 'border-emerald-200 bg-emerald-50 text-emerald-600'
                                : 'border-slate-200 bg-slate-50 text-brand-600 hover:border-brand-300 hover:bg-brand-50',
                            )}
                          >
                            {t.token}
                            {copied === t.token ? (
                              <Check className="h-3.5 w-3.5" />
                            ) : (
                              <Copy className="h-3.5 w-3.5 opacity-60" />
                            )}
                          </button>
                          {copied === t.token && (
                            <span className="ml-2 text-xs font-medium text-emerald-600">
                              nusxalandi
                            </span>
                          )}
                        </td>
                        <td className="py-1.5 pr-3 text-slate-700">{t.label}</td>
                        <td className="py-1.5 text-slate-400">{t.example}</td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            )}

            {/* ---- O'quvchi surati — matn belgisi EMAS, Word'dagi rasm o'rni ---- */}
            {photoHelp && (
              <div className="mt-4 rounded-lg border border-brand-100 bg-brand-50/50 px-3.5 py-3">
                <div className="flex items-center gap-2 text-sm font-semibold text-brand-700">
                  <ImageIcon className="h-4 w-4" /> {photoHelp.title}
                </div>
                <p className="mt-1 text-sm text-slate-600">
                  Bu <b>belgi bilan yozilmaydi</b> — rasmni Word'ning o'zida qo'yasiz:
                </p>
                <ol className="mt-2 list-decimal space-y-1 pl-5 text-sm text-slate-600 marker:font-semibold marker:text-brand-500">
                  {photoHelp.steps.map((s, i) => (
                    <li key={i}>{s}</li>
                  ))}
                </ol>
                <p className="mt-2 text-xs text-slate-500">{photoHelp.note}</p>
              </div>
            )}
          </Card>

          {/* ---- Shablonlar ro'yxati ---- */}
          {templates.length === 0 ? (
            <Card>
              <div className="state">
                <div className="state-icon">
                  <Award className="h-6 w-6" />
                </div>
                <h4>Hali shablon yuklanmagan</h4>
                <p>
                  {canCreate
                    ? "Word (.docx) andozasini tayyorlang, ichiga yuqoridagi belgilarni yozing va \"Yangi shablon\" tugmasi orqali yuklang."
                    : 'Shablonlarni administrator yuklaydi.'}
                </p>
              </div>
            </Card>
          ) : (
            <div className="space-y-2.5">
              {templates.map((t) => {
                const busy = rowBusy === t.id
                return (
                  <div
                    key={t.id}
                    className={cn(
                      'flex flex-wrap items-center gap-3 rounded-xl border border-slate-200 bg-white p-4 transition-all hover:border-brand-300',
                      !t.isActive && 'opacity-70',
                    )}
                  >
                    <div
                      className={cn(
                        'flex h-10 w-10 shrink-0 items-center justify-center rounded-xl',
                        t.isDefault ? 'bg-amber-50 text-amber-500' : 'bg-brand-50 text-brand-600',
                      )}
                    >
                      <FileText className="h-5 w-5" />
                    </div>

                    <div className="min-w-0 flex-1">
                      <p className="flex flex-wrap items-center gap-2 font-semibold text-slate-800">
                        <span className="truncate">{t.name}</span>
                        {t.isDefault && (
                          <Badge tone="amber">
                            <Star className="h-3 w-3" /> Standart
                          </Badge>
                        )}
                        <Badge tone={t.isActive ? 'green' : 'default'} dot>
                          {t.isActive ? 'Faol' : 'Nofaol'}
                        </Badge>
                      </p>
                      <p className="mt-0.5 truncate text-xs text-slate-400">
                        {t.fileName || 'shablon.docx'}
                      </p>
                      <p className="mt-0.5 text-xs text-slate-400">
                        {formatDateTime(t.createdAt)}
                        {t.createdBy ? ` · ${t.createdBy}` : ''}
                      </p>
                    </div>

                    <div className="flex shrink-0 items-center gap-1">
                      {t.fileUrl && (
                        <a
                          href={t.fileUrl}
                          target="_blank"
                          rel="noreferrer"
                          title="Faylni yuklab olish"
                          className="flex h-8 w-8 items-center justify-center rounded-lg text-slate-400 hover:bg-slate-50 hover:text-brand-600"
                        >
                          <FileText className="h-4 w-4" />
                        </a>
                      )}
                      {canEdit && (
                        <>
                          {!t.isDefault && (
                            <button
                              type="button"
                              disabled={busy}
                              onClick={() => void patch(t, { isDefault: true })}
                              title="Standart qilish"
                              className="flex h-8 w-8 items-center justify-center rounded-lg text-slate-400 hover:bg-amber-50 hover:text-amber-500 disabled:opacity-50"
                            >
                              {busy ? (
                                <Loader2 className="h-4 w-4 animate-spin" />
                              ) : (
                                <Star className="h-4 w-4" />
                              )}
                            </button>
                          )}
                          <button
                            type="button"
                            disabled={busy}
                            onClick={() => void patch(t, { isActive: !t.isActive })}
                            title={t.isActive ? 'Nofaol qilish' : 'Faol qilish'}
                            className="flex h-8 w-8 items-center justify-center rounded-lg text-slate-400 hover:bg-slate-50 hover:text-slate-600 disabled:opacity-50"
                          >
                            {t.isActive ? (
                              <Eye className="h-4 w-4" />
                            ) : (
                              <EyeOff className="h-4 w-4" />
                            )}
                          </button>
                          <button
                            type="button"
                            onClick={() => {
                              setEditing(t)
                              setFormOpen(true)
                            }}
                            title="Tahrirlash"
                            className="flex h-8 w-8 items-center justify-center rounded-lg text-slate-400 hover:bg-slate-50 hover:text-slate-600"
                          >
                            <Pencil className="h-4 w-4" />
                          </button>
                        </>
                      )}
                      {canDelete && (
                        <button
                          type="button"
                          onClick={() => {
                            setDeleteError('')
                            setDeleting(t)
                          }}
                          title="O'chirish"
                          className="flex h-8 w-8 items-center justify-center rounded-lg text-slate-400 hover:bg-red-50 hover:text-red-500"
                        >
                          <Trash2 className="h-4 w-4" />
                        </button>
                      )}
                    </div>
                  </div>
                )
              })}
            </div>
          )}
        </div>
      )}

      {formOpen && (
        <TemplateFormModal
          key={editing?.id ?? 'new'}
          editing={editing}
          onClose={() => setFormOpen(false)}
          onSaved={handleSaved}
        />
      )}

      <Modal
        open={!!deleting}
        onClose={() => !deleteBusy && setDeleting(null)}
        title="Shablonni o'chirish"
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
            <b>{deleting?.name}</b> shabloni o'chiriladi. Bu amalni qaytarib bo'lmaydi.
          </p>
        </div>
        {deleteError && (
          <p className="mt-3 rounded-lg bg-red-50 px-3 py-2 text-sm text-red-600">{deleteError}</p>
        )}
      </Modal>
    </div>
  )
}

/**
 * Shablon yaratish/tahrirlash modali.
 * Yaratishda .docx fayl MAJBURIY; tahrirlashda bo'sh qoldirilsa mavjud fayl o'zgarmaydi.
 */
function TemplateFormModal({
  editing,
  onClose,
  onSaved,
}: {
  editing: TestCertificateTemplate | null
  onClose: () => void
  onSaved: (t: TestCertificateTemplate) => void
}) {
  const [name, setName] = useState(editing?.name ?? '')
  const [fileUrl, setFileUrl] = useState('')
  const [fileName, setFileName] = useState('')
  const [isDefault, setIsDefault] = useState(editing?.isDefault ?? false)
  const [isActive, setIsActive] = useState(editing?.isActive ?? true)
  const [uploading, setUploading] = useState(false)
  const [busy, setBusy] = useState(false)
  const [err, setErr] = useState('')

  const handleFile = async (file: File) => {
    if (!isDocx(file)) {
      setErr('Faqat Word (.docx) fayl yuklanadi')
      return
    }
    setUploading(true)
    setErr('')
    try {
      const up = await uploadAdminFile(file)
      setFileUrl(up.url)
      setFileName(up.name)
    } catch (e) {
      setErr(apiErrorMessage(e, "Faylni yuklab bo'lmadi"))
    } finally {
      setUploading(false)
    }
  }

  const valid = name.trim().length > 0 && (!!editing || !!fileUrl)

  const submit = async () => {
    if (!valid) {
      setErr(editing ? 'Shablon nomini kiriting' : "Shablon nomi va .docx fayl kerak")
      return
    }
    setBusy(true)
    setErr('')
    const payload: TestCertificateTemplatePayload = {
      name: name.trim(),
      isDefault,
      ...(fileUrl ? { fileUrl, fileName } : {}),
      ...(editing ? { isActive } : {}),
    }
    try {
      const saved = editing
        ? await updateCertificateTemplate(editing.id, payload)
        : await createCertificateTemplate(payload)
      onSaved(saved)
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
      title={editing ? 'Shablonni tahrirlash' : 'Yangi shablon'}
      footer={
        <>
          <Button variant="secondary" onClick={onClose} disabled={busy}>
            Bekor
          </Button>
          <Button onClick={submit} disabled={busy || uploading || !valid}>
            {busy ? <Loader2 className="h-4 w-4 animate-spin" /> : 'Saqlash'}
          </Button>
        </>
      }
    >
      <div className="space-y-4">
        <Input
          label="Nomi"
          required
          value={name}
          onChange={(e) => setName(e.target.value)}
          placeholder="Masalan: IELTS kurs sertifikati"
        />

        <div>
          <label className="mb-1 block text-sm font-medium text-slate-600">
            Word fayl (.docx){!editing && <span className="text-red-500"> *</span>}
          </label>
          {fileUrl ? (
            <div className="flex items-center gap-2 rounded-lg border border-slate-200 bg-white px-3 py-2">
              <FileText className="h-4 w-4 shrink-0 text-sky-600" />
              <a
                href={fileUrl}
                target="_blank"
                rel="noreferrer"
                className="min-w-0 flex-1 truncate text-sm font-medium text-brand-600 hover:underline"
              >
                {fileName || 'shablon.docx'}
              </a>
              <button
                type="button"
                onClick={() => {
                  setFileUrl('')
                  setFileName('')
                }}
                title="O'chirish"
                className="shrink-0 rounded-md p-1 text-slate-400 hover:bg-red-50 hover:text-red-500"
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
              {uploading ? 'Yuklanmoqda...' : 'Word (.docx) faylni tanlang'}
              <input
                type="file"
                accept={DOCX_ACCEPT}
                className="hidden"
                onChange={(e) => {
                  const f = e.target.files?.[0]
                  if (f) void handleFile(f)
                  e.target.value = ''
                }}
              />
            </label>
          )}
          <p className="mt-1 text-[11px] text-slate-400">
            {editing
              ? "Bo'sh qoldirilsa fayl o'zgarmaydi. Joriy fayl: "
              : "Hujjat ichiga @fish, @guruh kabi belgilarni yozing."}
            {editing && <b className="text-slate-500">{editing.fileName || 'shablon.docx'}</b>}
          </p>
        </div>

        <label className="flex cursor-pointer items-start gap-2.5">
          <input
            type="checkbox"
            checked={isDefault}
            onChange={(e) => setIsDefault(e.target.checked)}
            className="mt-0.5 h-4 w-4 accent-brand-600"
          />
          <span className="text-sm text-slate-700">
            Standart shablon
            <span className="mt-0.5 block text-xs text-slate-400">
              Testda shablon tanlanmasa — shu ishlatiladi (standart faqat bitta bo'ladi).
            </span>
          </span>
        </label>

        {editing && (
          <label className="flex cursor-pointer items-start gap-2.5">
            <input
              type="checkbox"
              checked={isActive}
              onChange={(e) => setIsActive(e.target.checked)}
              className="mt-0.5 h-4 w-4 accent-brand-600"
            />
            <span className="text-sm text-slate-700">
              Faol
              <span className="mt-0.5 block text-xs text-slate-400">
                Nofaol shablon o'qituvchiga tanlash ro'yxatida ko'rinmaydi.
              </span>
            </span>
          </label>
        )}

        {err && <p className="rounded-lg bg-red-50 px-3 py-2 text-sm text-red-600">{err}</p>}
      </div>
    </Modal>
  )
}
