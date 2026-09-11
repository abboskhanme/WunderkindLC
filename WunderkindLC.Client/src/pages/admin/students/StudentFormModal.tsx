import { useEffect, useMemo, useRef, useState } from 'react'
import { Upload, X, FileText, Loader2, AlertTriangle, Camera } from 'lucide-react'
import type { Student } from '@/types'
import type { StudentPayload, PhoneMatch } from '@/api/services/students'
import { uploadAdminFile, getStudentCredentials, checkStudentPhones } from '@/api/services/students'
import { getClasses } from '@/api/services/classes'
import { getTeachers } from '@/api/services/teachers'
import type { Group, Teacher } from '@/types'
import { getDistricts } from '@/api/services/districts'
import type { District } from '@/types'
import { Modal } from '@/components/ui/Modal'
import { Button } from '@/components/ui/Button'
import { Input, Select } from '@/components/ui/Input'
import { PhoneInput } from '@/components/ui/PhoneInput'
import { Badge } from '@/components/ui/Badge'
import { genderOptions } from '@/config/constants'
import { randomPassword, cn, apiErrorMessage } from '@/lib/utils'
import { StudentPhotoDialog } from './StudentPhotoDialog'

interface Props {
  open: boolean
  onClose: () => void
  onSubmit: (values: StudentPayload) => void
  /** Tahrirlash uchun mavjud o'quvchi, qo'shish uchun null */
  initial?: Student | null
}

const empty: StudentPayload = {
  fullName: '',
  lastName: '',
  firstName: '',
  middleName: '',
  birthDate: '',
  birthCertificateUrl: null,
  address: '',
  gender: 'male',
  phone: '',
  fatherFullName: '',
  fatherPhone: '',
  motherFullName: '',
  motherPhone: '',
  className: '',
  districtId: '',
  schoolId: '',
  enrollmentDate: new Date().toISOString().slice(0, 10),
  discountPct: 0,
  discountAmount: 0,
  discountNote: '',
  discountStartMonth: '',
  discountEndMonth: '',
  discountGroupId: '',
}

/** "Familiya Ism Sharifi" stringidan parts. Eski yozuvlarni tahrirda taqsimlaymiz. */
function splitFullName(full: string): { last: string; first: string; middle: string } {
  const parts = (full ?? '').trim().split(/\s+/).filter(Boolean)
  return {
    last: parts[0] ?? '',
    first: parts[1] ?? '',
    middle: parts.slice(2).join(' '),
  }
}

function joinName(last?: string, first?: string, middle?: string): string {
  return [last, first, middle]
    .map((p) => (p ?? '').trim())
    .filter((p) => p !== '')
    .join(' ')
}

export function StudentFormModal({ open, onClose, onSubmit, initial }: Props) {
  const [form, setForm] = useState<StudentPayload>(empty)
  const [groups, setGroups] = useState<Group[]>([])
  const [teachers, setTeachers] = useState<Teacher[]>([])
  /** "Guruhga biriktirish" bosqichi: avval o'qituvchi, keyin o'sha o'qituvchining guruhi. */
  const [teacherId, setTeacherId] = useState('')
  const [districts, setDistricts] = useState<District[]>([])
  /** Fayl yuklash holatlari (har maydon uchun alohida). */
  const [uploading, setUploading] = useState<{ birth?: boolean }>({})
  /** Tahrirlanayotgan o'quvchining login (username)i — backend'dan olinadi, faqat ko'rsatish uchun. */
  const [login, setLogin] = useState('')
  /** Telefon dublikati tekshiruvi holati. */
  const [checking, setChecking] = useState(false)
  /** Topilgan dublikatlar (bo'lsa — tasdiq modali ochiladi). */
  const [dupes, setDupes] = useState<PhoneMatch[]>([])
  /** Dublikat tasdig'ini kutayotgan payload ("Baribir saqlash" uchun). */
  const [pending, setPending] = useState<StudentPayload | null>(null)

  /**
   * Guruh/o'qituvchi ro'yxati YUKLANMADI. ⚠️ Bo'sh ro'yxatdan FARQ QILADI: bo'sh — "markazda
   * guruh yo'q", xato esa — "bilmaymiz". Ilgari ikkalasi bir xil ko'rinardi (o'qituvchi
   * tanlovida faqat «— guruhsiz —», guruh tanlovi esa abadiy o'chiq) va foydalanuvchi buni
   * NOSOZLIK deb emas, "guruh yo'q ekan" deb tushunardi.
   */
  const [refError, setRefError] = useState('')
  /** "Qayta urinish" — ro'yxatlarni qaytadan so'rash uchun hisoblagich. */
  const [refTick, setRefTick] = useState(0)

  useEffect(() => {
    if (!open) return
    let alive = true
    Promise.all([getClasses(), getTeachers()])
      .then(([gs, ts]) => {
        if (!alive) return
        setGroups(gs.filter((g) => !g.isArchived))
        setTeachers(ts.filter((t) => !t.isArchived))
        setRefError('')
      })
      .catch((e) => {
        if (!alive) return
        setGroups([])
        setTeachers([])
        setRefError(apiErrorMessage(e, "Guruhlar va o'qituvchilar ro'yxati yuklanmadi"))
      })
    return () => {
      alive = false
    }
  }, [open, refTick])

  /**
   * O'quvchining GURUH A'ZOLIGI (M2M) bormi. Bor bo'lsa forma guruh tanlovini KO'RSATMAYDI —
   * pastdagi izohga qarang: bu forma a'zolikni ko'chira olmaydi, faqat eski `className`
   * yorlig'ini yozib qo'yardi.
   */
  const hasMemberships = (initial?.groupStates?.length ?? 0) > 0

  // Tahrirda (a'zoligi YO'Q eski yozuvlar uchun): joriy guruh nomiga mos o'qituvchini avtomatik
  // tanlab qo'yamiz — kaskad ochilsin.
  useEffect(() => {
    if (!open) return
    if (hasMemberships || !initial?.className) {
      setTeacherId('')
      return
    }
    const g = groups.find((x) => x.name === initial.className)
    setTeacherId(g?.teacherId ?? '')
  }, [open, initial, groups, hasMemberships])

  // Faqat guruhi bor o'qituvchilar tanlov ro'yxatida ko'rinadi.
  const teacherOptions = useMemo(
    () =>
      teachers
        .filter((t) => groups.some((g) => g.teacherId === t.id))
        .sort((a, b) => a.fullName.localeCompare(b.fullName)),
    [teachers, groups],
  )
  const groupsForTeacher = useMemo(
    () => groups.filter((g) => g.teacherId === teacherId),
    [groups, teacherId],
  )

  useEffect(() => {
    if (open) getDistricts().then(setDistricts).catch(() => { /* tarmoq/mok — tuman ro'yxati bo'sh qoladi */ })
  }, [open])

  // Tahrirda o'quvchining login(username)ini yuklab ko'rsatamiz.
  useEffect(() => {
    if (!open || !initial) return
    // eslint-disable-next-line react-hooks/set-state-in-effect -- avvalgi loginni tozalash (maqsadli)
    setLogin('')
    let active = true
    getStudentCredentials(initial.id)
      .then((c) => { if (active) setLogin(c.login) })
      .catch(() => { /* tarmoq/mok xatosi — login ko'rsatilmaydi */ })
    return () => { active = false }
  }, [open, initial])

  useEffect(() => {
    if (!open) return
    // eslint-disable-next-line react-hooks/set-state-in-effect -- modal qayta ochilganda dublikat holatini tozalash (maqsadli)
    setDupes([])
    setPending(null)
    setChecking(false)
    if (initial) {
      // Tahrirda: agar parts saqlanmagan bo'lsa, FullName'dan parse qilamiz (eski o'quvchilar).
      const sParts = initial.lastName || initial.firstName || initial.middleName
        ? { last: initial.lastName ?? '', first: initial.firstName ?? '', middle: initial.middleName ?? '' }
        : splitFullName(initial.fullName)
      // eslint-disable-next-line react-hooks/set-state-in-effect -- modal ochilganda formani initial bilan sinxronlash (maqsadli)
      setForm({
        fullName: initial.fullName,
        lastName: sParts.last,
        firstName: sParts.first,
        middleName: sParts.middle,
        birthDate: initial.birthDate,
        birthCertificateUrl: initial.birthCertificateUrl ?? null,
        address: initial.address,
        gender: initial.gender,
        phone: initial.phone ?? '',
        fatherFullName: initial.fatherFullName ?? '',
        fatherPhone: initial.fatherPhone ?? '',
        motherFullName: initial.motherFullName ?? '',
        motherPhone: initial.motherPhone ?? '',
        className: initial.className,
        districtId: initial.districtId ?? '',
        schoolId: initial.schoolId ?? '',
        enrollmentDate: initial.enrollmentDate,
        discountPct: initial.discountPct,
        discountAmount: initial.discountAmount,
        discountNote: initial.discountNote,
        discountStartMonth: initial.discountStartMonth ?? '',
        discountEndMonth: initial.discountEndMonth ?? '',
        discountGroupId: initial.discountGroupId ?? '',
      })
    } else {
      // eslint-disable-next-line react-hooks/set-state-in-effect -- yangi forma boshlash (maqsadli)
      setForm(empty)
    }
  }, [open, initial])

  const update =<K extends keyof StudentPayload>(key: K, value: StudentPayload[K]) =>
    setForm((f) => ({ ...f, [key]: value }))

  /** «Kameradan olish» dialogi ochiqmi (rasm shu yerda ham yuklanadi). */
  const [photoOpen, setPhotoOpen] = useState(false)

  /** Fayl yuklash — admin uploads endpoint'iga uzatib, qaytgan URL'ni formaga yozadi. */
  const handleUpload = async (key: 'birthCertificateUrl', file: File) => {
    setUploading((u) => ({ ...u, birth: true }))
    try {
      const res = await uploadAdminFile(file)
      update(key, res.url)
      // Muvaffaqiyat feedback (ixtiyoriy, silent o'tish mumkin — UI indikatori bor)
    } catch (err) {
      alert(`❌ ${apiErrorMessage(err, 'Fayl yuklab boʻlmadi (error)')}`)
    } finally {
      setUploading((u) => ({ ...u, birth: false }))
    }
  }

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault()
    const last = (form.lastName ?? '').trim()
    const first = (form.firstName ?? '').trim()
    const middle = (form.middleName ?? '').trim()
    if (!last && !first && !middle) return
    const pwd = (form.newPassword ?? '').trim()
    if (pwd.length > 0 && pwd.length < 8) {
      alert("Parol kamida 8 belgidan iborat bo'lsin")
      return
    }
    const fullName = joinName(last, first, middle)
    const payload = { ...form, fullName }

    // Telefon dublikatini tekshiramiz (o'quvchi/ota/ona raqami — arxivdagilar ham).
    setChecking(true)
    try {
      const found = await checkStudentPhones({
        phone: form.phone ?? undefined,
        fatherPhone: form.fatherPhone ?? undefined,
        motherPhone: form.motherPhone ?? undefined,
        excludeId: initial?.id,
      })
      // Tahrirlashda o'quvchining O'ZI dublikat sifatida chiqmasligi kafolatlanadi
      // (backend excludeId'dan tashqari mijoz tarafida ham filtrlaymiz).
      const matches = initial?.id ? found.filter((m) => m.studentId !== initial.id) : found
      if (matches.length > 0) {
        setPending(payload)
        setDupes(matches)
        return // tasdiq modalida "Baribir saqlash" kutiladi
      }
    } catch {
      // Tekshiruv ishlamasa (tarmoq/mok) — saqlashni bloklamaymiz.
    } finally {
      setChecking(false)
    }

    onSubmit(payload)
  }

  return (
    <>
    <Modal
      open={open}
      onClose={onClose}
      size="lg"
      title={initial ? "O'quvchini tahrirlash" : "Yangi o'quvchi"}
      footer={
        <>
          <Button variant="secondary" onClick={onClose}>
            Bekor qilish
          </Button>
          <Button type="submit" form="student-form" disabled={checking}>
            {checking ? 'Tekshirilmoqda...' : 'Saqlash'}
          </Button>
        </>
      }
    >
      <form id="student-form" onSubmit={handleSubmit} className="space-y-5">
        {/* ---------- O'quvchi ---------- */}
        <Section title="O'quvchi">
          <div className="grid grid-cols-1 gap-3 md:grid-cols-3">
            <Input
              label="Familiya"
              required
              value={form.lastName ?? ''}
              onChange={(e) => update('lastName', e.target.value)}
            />
            <Input
              label="Ism"
              required
              value={form.firstName ?? ''}
              onChange={(e) => update('firstName', e.target.value)}
            />
            <Input
              label="Otasining ismi"
              value={form.middleName ?? ''}
              onChange={(e) => update('middleName', e.target.value)}
            />
          </div>
          <div className="mt-3 grid grid-cols-1 gap-3 md:grid-cols-2">
            <Input
              label="Tug'ilgan kun"
              type="date"
              value={form.birthDate}
              onChange={(e) => update('birthDate', e.target.value)}
            />
            <Select
              label="Jinsi"
              value={form.gender}
              onChange={(e) => update('gender', e.target.value as StudentPayload['gender'])}
            >
              {genderOptions.map((g) => (
                <option key={g.value} value={g.value}>
                  {g.label}
                </option>
              ))}
            </Select>
          </div>
          <div className="mt-3">
            <PhoneInput
              label="O'z telefon raqami"
              value={form.phone ?? ''}
              onChange={(phone) => update('phone', phone)}
            />
          </div>
          <div className="mt-3">
            <FileField
              label="O'quvchi rasmi"
              url={form.birthCertificateUrl ?? null}
              uploading={uploading.birth}
              onUpload={(f) => handleUpload('birthCertificateUrl', f)}
              onClear={() => update('birthCertificateUrl', null)}
            />
            {/* KAMERADAN olish — noutbuk/telefon kamerasi bilan darhol suratga olish. */}
            <button
              type="button"
              onClick={() => setPhotoOpen(true)}
              className="mt-1.5 inline-flex items-center gap-1.5 text-xs font-medium text-brand-600 hover:underline"
            >
              <Camera className="h-3.5 w-3.5" /> Kameradan olish
            </button>
          </div>
        </Section>

        {/* ---------- Ota-ona ---------- */}
        <Section title="Ota-ona">
          <div className="grid grid-cols-1 gap-3 md:grid-cols-2">
            <Input
              label="Otasi F.I.SH"
              value={form.fatherFullName ?? ''}
              onChange={(e) => update('fatherFullName', e.target.value)}
            />
            <PhoneInput
              label="Otasi raqami"
              value={form.fatherPhone ?? ''}
              onChange={(phone) => update('fatherPhone', phone)}
            />
          </div>
          <div className="mt-3 grid grid-cols-1 gap-3 md:grid-cols-2">
            <Input
              label="Onasi F.I.SH"
              value={form.motherFullName ?? ''}
              onChange={(e) => update('motherFullName', e.target.value)}
            />
            <PhoneInput
              label="Onasi raqami"
              value={form.motherPhone ?? ''}
              onChange={(phone) => update('motherPhone', phone)}
            />
          </div>
        </Section>

        {/* ---------- Boshqa ma'lumotlar ---------- */}
        <Section title="Boshqa ma'lumotlar">
          <Input
            label="Manzil"
            value={form.address}
            onChange={(e) => update('address', e.target.value)}
          />
          {/*
            GURUH — bu forma FAQAT o'quvchi hali hech qaysi guruhga qo'shilmaganda "biriktirish"
            vositasi bo'la oladi.

            ⚠️ A'zoligi BOR o'quvchida bu maydonlar KO'RSATILMAYDI. Sabab: server
            (`StudentsController.Update`) a'zolik bor bo'lsa yangi a'zolik YARATMAYDI va hech
            kimni ko'chirmaydi — u faqat `Student.ClassName` YORLIG'INI ustidan yozadi. Ya'ni
            admin bu yerdan "guruhni o'zgartirsa" hech narsa ko'chmas, lekin butun tizim
            ishonadigan yorliq YOLG'ON qiymatga o'tib ketardi (hisobotlar, ilova, chat va
            arxivlash o'sha yorliqqa qarardi). Guruh a'zoligi profil sahifasidagi «Guruhlar»
            tabidan boshqariladi (qo'shish / aktivlashtirish / muzlatish / ko'chirish).
          */}
          {hasMemberships ? (
            <div className="mt-3 rounded-lg border border-slate-200 bg-slate-50 px-3 py-2.5">
              <span className="block text-sm text-slate-400">Guruhlar</span>
              <div className="mt-1.5 flex flex-wrap gap-1.5">
                {(initial?.groupStates ?? []).map((g) => (
                  <Badge key={g.groupId} tone={g.status === 'active' ? 'green' : g.status === 'trial' ? 'violet' : 'blue'}>
                    {g.name} · {g.status === 'active' ? 'aktiv' : g.status === 'trial' ? 'sinov'
                      : g.yearFreeze ? 'aktiv muzlatilgan' : 'muzlatilgan'}
                  </Badge>
                ))}
              </div>
              <p className="mt-2 text-xs text-slate-500">
                Guruh a'zoligi bu formadan o'zgartirilmaydi — profil sahifasidagi «Guruhlar» tabidan
                (qo'shish, aktivlashtirish, muzlatish, boshqa guruhga ko'chirish).
              </p>
            </div>
          ) : (
            <div className="mt-3">
              {/* ⚠️ RO'YXAT YUKLANMADI — jimgina "guruh yo'q" bo'lib ko'rinmasin. */}
              {refError && (
                <div className="mb-2 flex flex-wrap items-center justify-between gap-2 rounded-lg border border-amber-200 bg-amber-50 px-3 py-2 text-sm text-amber-700">
                  <span>{refError} — guruhga biriktirish hozir ishlamaydi.</span>
                  <button
                    type="button"
                    onClick={() => setRefTick((t) => t + 1)}
                    className="rounded-md px-2 py-1 text-xs font-semibold text-amber-800 hover:bg-amber-100"
                  >
                    Qayta urinish
                  </button>
                </div>
              )}
              <div className="grid grid-cols-1 gap-3 md:grid-cols-2">
              <Select
                label="O'qituvchi"
                value={teacherId}
                onChange={(e) => {
                  // O'qituvchi o'zgarsa, oldingi guruh tanlovi tozalanadi (boshqa o'qituvchiga tegishli edi).
                  setTeacherId(e.target.value)
                  update('className', '')
                }}
              >
                <option value="">— guruhsiz —</option>
                {teacherOptions.map((t) => (
                  <option key={t.id} value={t.id}>
                    {t.fullName}
                  </option>
                ))}
              </Select>
              <Select
                label="Guruhga biriktirish"
                value={form.className}
                disabled={!teacherId}
                onChange={(e) => update('className', e.target.value)}
              >
                <option value="">{teacherId ? '— guruh tanlang —' : '— avval o\'qituvchini tanlang —'}</option>
                {groupsForTeacher.map((g) => (
                  <option key={g.id} value={g.name}>
                    {g.name}
                  </option>
                ))}
              </Select>
              </div>
            </div>
          )}
          <div className="mt-3">
            <Input
              label="Markazga kelgan sana"
              type="date"
              value={form.enrollmentDate}
              onChange={(e) => update('enrollmentDate', e.target.value)}
            />
          </div>
          <div className="mt-3 grid grid-cols-1 gap-3 md:grid-cols-2">
            <Select
              label="Tuman"
              value={form.districtId ?? ''}
              onChange={(e) => {
                // Tuman o'zgarsa, oldingi maktab tanlovi tozalanadi.
                setForm((f) => ({ ...f, districtId: e.target.value, schoolId: '' }))
              }}
            >
              <option value="">— tanlanmagan —</option>
              {districts.map((d) => (
                <option key={d.id} value={d.id}>
                  {d.name}
                </option>
              ))}
            </Select>
            <Select
              label="Maktab"
              value={form.schoolId ?? ''}
              disabled={!form.districtId}
              onChange={(e) => update('schoolId', e.target.value)}
            >
              <option value="">
                {form.districtId ? '— tanlanmagan —' : '— avval tumanni tanlang —'}
              </option>
              {(districts.find((d) => d.id === form.districtId)?.schools ?? []).map((s) => (
                <option key={s.id} value={s.id}>
                  {s.name}
                </option>
              ))}
            </Select>
          </div>
        </Section>

        {/* ---------- Chegirma ----------
            ⚠️ Maydonlar OLIB TASHLANDI: chegirma endi HAR FAN uchun alohida beriladi va
            server `PUT /students/{id}` dagi chegirma maydonlarini E'TIBORGA OLMAYDI — forma
            qoldirilsa u JIMGINA ishlamaydigan bo'lib qolardi. */}
        <Section title="Chegirma">
          <p className="text-sm text-slate-500">
            Chegirma o'quvchi profilidagi «Chegirma» bo'limidan beriladi — har fan uchun alohida.
          </p>
        </Section>

        {/* ---------- Login va parol (faqat tahrirda) ---------- */}
        {initial && (
          <Section title="Login va parol">
            <Input
              label="Login (username)"
              value={login}
              readOnly
              placeholder="Yuklanmoqda..."
              className="bg-slate-100 text-slate-600"
            />
            <div className="mt-3 flex items-end gap-2">
              <div className="flex-1">
                <Input
                  label="Yangi parol"
                  type="text"
                  autoComplete="new-password"
                  placeholder="Bo'sh qoldirilsa — parol o'zgarmaydi"
                  value={form.newPassword ?? ''}
                  onChange={(e) => update('newPassword', e.target.value)}
                />
              </div>
              <Button
                type="button"
                variant="secondary"
                onClick={() => update('newPassword', randomPassword(8))}
              >
                Generatsiya
              </Button>
            </div>
            <p className="mt-1 text-xs text-slate-400">
              Login o'zgarmaydi. Yangi parol kamida 8 belgi — kiriting yoki generatsiya qiling;
              saqlangach o'quvchiga topshiring.
            </p>
          </Section>
        )}
      </form>
    </Modal>

    {/* O'quvchi rasmi — kameradan olish yoki fayldan tanlash. Bu yerda faqat FORMAGA
        yoziladi, serverga esa forma saqlanganda umumiy payload bilan ketadi. */}
    <StudentPhotoDialog
      open={photoOpen}
      currentUrl={form.birthCertificateUrl ?? null}
      startWithCamera
      onClose={() => setPhotoOpen(false)}
      onSaved={(url) => update('birthCertificateUrl', url)}
    />

    {/* Telefon dublikati ogohlantirishi — "Baribir saqlash" / "Bekor qilish" */}
    <Modal
      open={dupes.length > 0}
      onClose={() => {
        setDupes([])
        setPending(null)
      }}
      size="md"
      title="Bunday raqam allaqachon mavjud"
      footer={
        <>
          <Button
            variant="secondary"
            onClick={() => {
              setDupes([])
              setPending(null)
            }}
          >
            Bekor qilish
          </Button>
          <Button
            variant="danger"
            onClick={() => {
              const p = pending
              setDupes([])
              setPending(null)
              if (p) onSubmit(p)
            }}
          >
            Baribir saqlash
          </Button>
        </>
      }
    >
      <div className="space-y-3">
        <div className="flex items-start gap-2.5 rounded-lg bg-amber-50 px-3 py-2.5 text-sm text-amber-700">
          <AlertTriangle className="mt-0.5 h-4 w-4 flex-shrink-0" />
          <p>
            Kiritilgan raqam(lar) allaqachon quyidagi o'quvchi(lar)da ishlatilgan. Baribir saqlashni
            xohlaysizmi?
          </p>
        </div>
        <ul className="divide-y divide-slate-100 rounded-lg border border-slate-200">
          {dupes.map((d, i) => (
            <li key={`${d.studentId}-${i}`} className="flex items-center gap-3 px-3 py-2.5">
              <div className="min-w-0 flex-1">
                <p className="truncate text-sm font-medium text-slate-800">
                  {d.fullName}
                  {d.isArchived && (
                    <span className="ml-2 rounded bg-slate-100 px-1.5 py-0.5 text-[11px] font-semibold text-slate-500">
                      arxivda
                    </span>
                  )}
                </p>
                <p className="truncate text-xs text-slate-400">
                  {d.className || 'guruhsiz'} · {d.role} raqami
                </p>
              </div>
              <span className="font-mono text-sm text-slate-600">{d.phone}</span>
            </li>
          ))}
        </ul>
      </div>
    </Modal>
    </>
  )
}

function Section({ title, children }: { title: string; children: React.ReactNode }) {
  return (
    <div className="rounded-xl border border-slate-200 bg-slate-50/40 p-3">
      <h4 className="mb-3 text-xs font-semibold uppercase tracking-wide text-slate-500">{title}</h4>
      {children}
    </div>
  )
}

function FileField({
  label,
  url,
  uploading,
  onUpload,
  onClear,
}: {
  label: string
  url: string | null
  uploading: boolean | undefined
  onUpload: (file: File) => void
  onClear: () => void
}) {
  const ref = useRef<HTMLInputElement | null>(null)
  const isImage = url && /\.(png|jpe?g|webp|gif|bmp)$/i.test(url)
  return (
    <div>
      <span className="mb-1 block text-sm font-medium text-slate-600">{label}</span>
      <div className="flex items-start gap-3">
        <div
          className={cn(
            'flex h-20 w-20 shrink-0 items-center justify-center rounded-lg border border-dashed bg-white',
            url ? 'border-brand-200' : 'border-slate-200 text-slate-300',
          )}
        >
          {uploading ? (
            <Loader2 className="h-5 w-5 animate-spin text-brand-500" />
          ) : isImage ? (
            <img src={url} alt="" className="h-full w-full rounded-lg object-cover" />
          ) : url ? (
            <FileText className="h-7 w-7 text-brand-500" />
          ) : (
            <Upload className="h-5 w-5" />
          )}
        </div>
        <div className="flex flex-1 flex-col gap-2">
          <input
            ref={ref}
            type="file"
            accept="image/*"
            className="hidden"
            onChange={(e) => {
              const f = e.target.files?.[0]
              if (f) onUpload(f)
              if (ref.current) ref.current.value = ''
            }}
          />
          <div className="flex gap-2">
            <Button
              type="button"
              variant="secondary"
              onClick={() => ref.current?.click()}
              disabled={uploading}
            >
              <Upload className="h-4 w-4" />
              {url ? 'Yangilash' : 'Yuklash'}
            </Button>
            {url && (
              <>
                <Button
                  type="button"
                  variant="secondary"
                  onClick={() => window.open(url, '_blank', 'noopener,noreferrer')}
                >
                  Ko'rish
                </Button>
                <Button type="button" variant="danger" onClick={onClear}>
                  <X className="h-4 w-4" /> O'chirish
                </Button>
              </>
            )}
          </div>
          {url && <p className="text-xs text-slate-400 break-all">{url}</p>}
        </div>
      </div>
    </div>
  )
}
