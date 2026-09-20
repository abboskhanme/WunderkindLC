import { useEffect, useMemo, useState } from 'react'
import { Plus, Trash2, Check, UserX, Snowflake, RotateCcw, UserMinus, Users, Layers, GraduationCap, Briefcase, Wallet, Megaphone, PhoneCall, Archive, Tag } from 'lucide-react'
import type { LucideIcon } from 'lucide-react'
import type { AbsenceReason, ActionReason, LeadSource } from '@/types'
import { getSettings, saveAbsenceReasons } from '@/api/services/settings'
import {
  getActionReasons,
  getActionReasonCategories,
  getOutOfControlCategories,
  createActionReason,
  updateActionReason,
  deleteActionReason,
} from '@/api/services/actionReasons'
import {
  getLeadSources,
  createLeadSource,
  updateLeadSource,
  deleteLeadSource,
} from '@/api/services/leadSources'
import { Card } from '@/components/ui/Card'
import { Button } from '@/components/ui/Button'
import { Loader } from '@/components/ui/Loader'
import { PageHeader } from '@/components/ui/PageHeader'
import { cn, apiErrorMessage } from '@/lib/utils'

/**
 * Amal kategoriyalari — har biri o'z sabablar ro'yxatiga ega.
 *
 * ⚠️ Bu ro'yxat faqat SARLAVHA/IKONKA beradi. Qaysi kategoriyalar borligini SERVER aytadi
 * (`GET /admin/action-reasons/categories`) — shu sababdan bu yerga qo'shishni unutish endi
 * kategoriyani YO'Q QILMAYDI, u shunchaki kalit nomi bilan chiqadi. Ilgari ro'yxat yagona
 * manba edi va `contact` / `archive_student` sahifada umuman ko'rinmasdi: admin ular uchun
 * sabab qo'sha olmas, tanlash ro'yxati esa doim bo'sh chiqardi.
 */
const CATEGORIES: { key: string; title: string; sub: string; icon: LucideIcon }[] = [
  { key: 'freeze', title: 'Talaba muzlatilganda', sub: "Guruh a'zoligini muzlatishda tanlanadi", icon: Snowflake },
  { key: 'return_trial', title: 'Sinovga qaytarilganda', sub: 'Talaba sinov holatiga qaytarilganda', icon: RotateCcw },
  { key: 'remove_active', title: "Aktiv talaba o'chirilganda", sub: 'Aktiv a’zo guruhdan chiqarilganda', icon: UserMinus },
  { key: 'remove_trial', title: "Sinovdagi talaba o'chirilganda", sub: 'Sinovdagi a’zo chiqarilganda', icon: UserX },
  { key: 'remove_frozen', title: "Muzlatilgan talaba o'chirilganda", sub: 'Muzlatilgan a’zo chiqarilganda', icon: UserX },
  // "Bog'lanish kerak" navbatiga qo'shishda tanlanadi (O'quvchilar → Bog'lanish kerak).
  { key: 'contact', title: "Bog'lanish kerak", sub: "O'quvchini bog'lanish navbatiga qo'shishda tanlanadi", icon: PhoneCall },
  { key: 'archive_student', title: "Talaba arxivga olinganda", sub: "O'quvchi arxivga ko'chirilganda tanlanadi", icon: Archive },
  { key: 'lead_delete', title: "Lid o'chirilganda", sub: 'Lid (mijoz) o’chirilganda', icon: Users },
  { key: 'group_delete', title: "Guruh o'chirilganda", sub: 'Guruh o’chirilganda', icon: Layers },
  { key: 'student_delete', title: "Talaba o'chirilganda", sub: "O'quvchi butunlay o'chirilganda", icon: UserMinus },
  { key: 'teacher_delete', title: "O'qituvchi o'chirilganda", sub: "O'qituvchi butunlay o'chirilganda", icon: GraduationCap },
  { key: 'staff_delete', title: "Xodim o'chirilganda", sub: "Xodim akkaunti o'chirilganda", icon: Briefcase },
  { key: 'finance_delete', title: "To'lov o'chirilganda", sub: "Moliyada tranzaksiya/to'lov o'chirilganda", icon: Wallet },
]

const control =
  'rounded-lg border border-slate-200 px-3 py-2 text-sm text-slate-800 outline-none transition-colors focus:border-brand-400'

export function ReasonsPage() {
  const [loading, setLoading] = useState(true)
  const [absence, setAbsence] = useState<AbsenceReason[]>([])
  const [absStatus, setAbsStatus] = useState<'idle' | 'saving' | 'saved'>('idle')
  const [reasons, setReasons] = useState<ActionReason[]>([])
  const [sources, setSources] = useState<LeadSource[]>([])
  /** Serverdagi kategoriya kalitlari — kartochkalar shundan quriladi. */
  const [serverCategories, setServerCategories] = useState<string[]>([])
  /**
   * KETISHGA oid kategoriyalar — faqat shu kartochkalarda «nazoratdan tashqari» belgisi
   * ko'rsatiladi. Ro'yxat SERVERDAN: qoida ikki joyda ayri ketmasin.
   */
  const [leaveCategories, setLeaveCategories] = useState<string[]>([])

  /**
   * Ko'rsatiladigan kategoriyalar: server bergan HAR BIR kalit uchun kartochka. Yorlig'i
   * `CATEGORIES` da bo'lsa — o'sha, bo'lmasa kalit nomi bilan (bo'shliq ko'rinib tursin).
   * Server javob bermasa — eski xatti-harakat (faqat `CATEGORIES`).
   */
  const visibleCategories = useMemo(() => {
    if (serverCategories.length === 0) return CATEGORIES
    return serverCategories.map(
      (key) =>
        CATEGORIES.find((c) => c.key === key) ?? {
          key,
          title: key,
          sub: "Yorlig'i belgilanmagan kategoriya",
          icon: Tag,
        },
    )
  }, [serverCategories])

  useEffect(() => {
    Promise.all([getSettings(), getActionReasons(), getLeadSources()])
      .then(([s, r, src]) => {
        setAbsence(s.absenceReasons)
        setReasons(r)
        setSources(src)
      })
      .finally(() => setLoading(false))
    // Kategoriyalar ALOHIDA: eski serverda bu endpoint bo'lmasligi mumkin — xato bo'lsa
    // sahifa baribir ochiladi va `CATEGORIES` (zaxira ro'yxat) ishlatiladi.
    getActionReasonCategories()
      .then(setServerCategories)
      .catch(() => setServerCategories([]))
    // Eski serverda bu endpoint yo'q — u holda checkbox umuman ko'rsatilmaydi (sahifa ishlaydi).
    getOutOfControlCategories()
      .then(setLeaveCategories)
      .catch(() => setLeaveCategories([]))
  }, [])

  // ---- Davomat (kelmaganlik) sabablari ----
  const addAbsence = () =>
    setAbsence((a) => [...a, { id: crypto.randomUUID(), name: '', short: '', isLate: false, points: 0 } as AbsenceReason])
  const patchAbsence = (i: number, patch: Partial<AbsenceReason>) =>
    setAbsence((a) => a.map((r, x) => (x === i ? { ...r, ...patch } : r)))
  const removeAbsence = (i: number) => setAbsence((a) => a.filter((_, x) => x !== i))
  const saveAbsence = async () => {
    setAbsStatus('saving')
    try {
      await saveAbsenceReasons(absence.filter((r) => r.name.trim()))
      setAbsStatus('saved')
      setTimeout(() => setAbsStatus('idle'), 1500)
    } catch (err) {
      // `try` umuman yo'q edi: xatoda tugma abadiy "Saqlanmoqda..." holatida qotib qolardi.
      setAbsStatus('idle')
      alert(apiErrorMessage(err, "Sabablarni saqlab bo'lmadi"))
    }
  }

  if (loading) return <Loader label="Yuklanmoqda..." />

  return (
    <div>
      <PageHeader
        title="Sabablar"
        sub="Barcha amallar uchun sabablar shu yerda boshqariladi — muzlatish, o'chirish, sinovga qaytarish, lid/guruh va davomat"
      />

      <div className="grid gap-4 lg:grid-cols-2">
        {/* Davomat sabablari */}
        <Card
          title="O'quvchi kelmaganda (davomat)"
          sub="Jurnalda bor/yo'q belgilanganda ishlatiladi"
          actions={
            <Button onClick={saveAbsence} disabled={absStatus === 'saving'}>
              <Check className="h-4 w-4" /> {absStatus === 'saving' ? 'Saqlanmoqda...' : absStatus === 'saved' ? 'Saqlandi' : 'Saqlash'}
            </Button>
          }
        >
          <div className="space-y-2">
            {absence.map((r, i) => (
              <div key={r.id} className="flex flex-wrap items-center gap-2">
                <input
                  value={r.name}
                  onChange={(e) => patchAbsence(i, { name: e.target.value })}
                  placeholder="Sabab nomi (masalan: Kasal)"
                  className={cn(control, 'min-w-[160px] flex-1')}
                />
                <input
                  value={r.short}
                  onChange={(e) => patchAbsence(i, { short: e.target.value })}
                  placeholder="Belgi"
                  maxLength={3}
                  className={cn(control, 'w-16 text-center')}
                />
                <label className="inline-flex cursor-pointer items-center gap-1.5 whitespace-nowrap text-xs text-slate-600" title="Kech keldi — yo'qlik emas">
                  <input
                    type="checkbox"
                    checked={r.isLate}
                    onChange={() => patchAbsence(i, { isLate: !r.isLate })}
                    className="h-4 w-4 rounded border-slate-300 accent-brand-600"
                  />
                  Kech
                </label>
                <button
                  type="button"
                  onClick={() => removeAbsence(i)}
                  className="rounded-lg p-2 text-slate-400 transition-colors hover:bg-red-50 hover:text-red-600"
                >
                  <Trash2 className="h-4 w-4" />
                </button>
              </div>
            ))}
            <button
              type="button"
              onClick={addAbsence}
              className="inline-flex items-center gap-1 text-sm font-medium text-brand-600 hover:text-brand-700"
            >
              <Plus className="h-4 w-4" /> Sabab qo'shish
            </button>
          </div>
        </Card>

        {/* Lid manbalari (CRM) */}
        <LeadSourcesCard sources={sources} onChange={setSources} />

        {/* Amal sabablari — kategoriyalar */}
        {visibleCategories.map((cat) => (
          <CategoryCard
            key={cat.key}
            cat={cat}
            items={reasons.filter((r) => r.category === cat.key)}
            onChange={setReasons}
            showOutOfControl={leaveCategories.includes(cat.key)}
          />
        ))}
      </div>
    </div>
  )
}

function LeadSourcesCard({
  sources,
  onChange,
}: {
  sources: LeadSource[]
  onChange: React.Dispatch<React.SetStateAction<LeadSource[]>>
}) {
  const [adding, setAdding] = useState('')
  const [busy, setBusy] = useState(false)
  const sorted = useMemo(() => [...sources].sort((a, b) => a.order - b.order), [sources])

  const add = async () => {
    const name = adding.trim()
    if (!name || busy) return
    setBusy(true)
    try {
      const created = await createLeadSource(name)
      onChange((prev) => [...prev, created])
      setAdding('')
    } catch (err) {
      alert(apiErrorMessage(err, "Saqlab bo'lmadi"))
    } finally {
      setBusy(false)
    }
  }
  const rename = async (source: LeadSource, name: string) => {
    const trimmed = name.trim()
    if (!trimmed || trimmed === source.name) return
    try {
      await updateLeadSource(source.id, trimmed)
      onChange((prev) => prev.map((s) => (s.id === source.id ? { ...s, name: trimmed } : s)))
    } catch (err) {
      alert(apiErrorMessage(err, "Saqlab bo'lmadi"))
    }
  }
  const remove = async (source: LeadSource) => {
    if (!confirm(`"${source.name}" manbasini o'chirasizmi?`)) return
    try {
      await deleteLeadSource(source.id)
      onChange((prev) => prev.filter((s) => s.id !== source.id))
    } catch (err) {
      alert(apiErrorMessage(err, "O'chirib bo'lmadi"))
    }
  }

  return (
    <Card
      title={
        <span className="flex items-center gap-2">
          <Megaphone className="h-4 w-4 text-brand-500" />
          Lid manbalari
        </span>
      }
      sub="Lidlar qayerdan kelganini belgilash uchun (Instagram, Sayt, Tashrif va h.k.)"
    >
      <div className="space-y-2">
        {sorted.map((s) => (
          <div key={s.id} className="flex items-center gap-2">
            <input
              defaultValue={s.name}
              onBlur={(e) => rename(s, e.target.value)}
              className={cn(control, 'flex-1')}
            />
            <button
              type="button"
              onClick={() => remove(s)}
              className="rounded-lg p-2 text-slate-400 transition-colors hover:bg-red-50 hover:text-red-600"
            >
              <Trash2 className="h-4 w-4" />
            </button>
          </div>
        ))}
        {sorted.length === 0 && <p className="text-xs text-slate-400">Manba yo'q — quyida qo'shing.</p>}

        <div className="flex items-center gap-2 pt-1">
          <input
            value={adding}
            onChange={(e) => setAdding(e.target.value)}
            onKeyDown={(e) => e.key === 'Enter' && add()}
            placeholder="Yangi manba..."
            className={cn(control, 'flex-1')}
          />
          <Button variant="secondary" onClick={add} disabled={busy || !adding.trim()}>
            <Plus className="h-4 w-4" /> Qo'shish
          </Button>
        </div>
        <p className="text-xs text-slate-400">
          Nomni o'zgartirsangiz shu manbadagi lidlar ham yangi nomga ko'chadi.
        </p>
      </div>
    </Card>
  )
}

function CategoryCard({
  cat,
  items,
  onChange,
  showOutOfControl,
}: {
  cat: { key: string; title: string; sub: string; icon: LucideIcon }
  items: ActionReason[]
  onChange: React.Dispatch<React.SetStateAction<ActionReason[]>>
  /** «Nazoratdan tashqari» belgisi shu kategoriyada ma'noli (ketishga oid kategoriyalar) */
  showOutOfControl?: boolean
}) {
  const [adding, setAdding] = useState('')
  const [busy, setBusy] = useState(false)
  const sorted = useMemo(() => [...items].sort((a, b) => a.order - b.order), [items])

  const add = async () => {
    const label = adding.trim()
    if (!label || busy) return
    setBusy(true)
    try {
      const created = await createActionReason(cat.key, label)
      onChange((prev) => [...prev, created])
      setAdding('')
    } catch (err) {
      // `LeadSourcesCard.add` dagi bilan bir xil: sabab qo'shilmasa, NEGA qo'shilmagani ko'rinsin.
      alert(apiErrorMessage(err, "Saqlab bo'lmadi"))
    } finally {
      setBusy(false)
    }
  }
  const save = async (id: string, label: string) => {
    const trimmed = label.trim()
    if (!trimmed) return
    try {
      await updateActionReason(id, trimmed)
    } catch (err) {
      // `onBlur` da chaqiriladi — xato ko'rsatilmasa yangi nom ekranda qolib, saqlangandek tuyulardi.
      alert(apiErrorMessage(err, "Saqlab bo'lmadi"))
      return
    }
    onChange((prev) => prev.map((r) => (r.id === id ? { ...r, label: trimmed } : r)))
  }
  /** Belgini almashtirish — nom TEGILMAYDI (serverga o'sha nom bilan birga yuboriladi). */
  const toggleOutOfControl = async (r: ActionReason) => {
    const next = !r.outOfControl
    // Optimistik: checkbox darhol javob bersin; xato bo'lsa orqaga qaytariladi.
    onChange((prev) => prev.map((x) => (x.id === r.id ? { ...x, outOfControl: next } : x)))
    try {
      await updateActionReason(r.id, r.label, next)
    } catch (err) {
      onChange((prev) => prev.map((x) => (x.id === r.id ? { ...x, outOfControl: !next } : x)))
      alert(apiErrorMessage(err, "Saqlab bo'lmadi"))
    }
  }
  const remove = async (id: string) => {
    try {
      await deleteActionReason(id)
    } catch (err) {
      // Server rad etsa (sabab ishlatilgan bo'lishi mumkin) qator ro'yxatda sababsiz qolardi.
      alert(apiErrorMessage(err, "O'chirib bo'lmadi"))
      return
    }
    onChange((prev) => prev.filter((r) => r.id !== id))
  }

  const Icon = cat.icon
  return (
    <Card
      title={
        <span className="flex items-center gap-2">
          <Icon className="h-4 w-4 text-brand-500" />
          {cat.title}
        </span>
      }
      sub={cat.sub}
    >
      <div className="space-y-2">
        {sorted.map((r) => (
          <div key={r.id} className="flex flex-wrap items-center gap-2">
            <input
              defaultValue={r.label}
              onBlur={(e) => e.target.value.trim() !== r.label && save(r.id, e.target.value)}
              className={cn(control, 'min-w-[140px] flex-1')}
            />
            {showOutOfControl && (
              <label
                className="inline-flex cursor-pointer items-center gap-1.5 whitespace-nowrap text-xs text-slate-600"
                title="Nazoratdan tashqari — chiquvchi adminning ketish foizidan CHIQARILADI (ko'chib ketish, sog'liq, oilaviy sharoit)"
              >
                <input
                  type="checkbox"
                  checked={!!r.outOfControl}
                  onChange={() => toggleOutOfControl(r)}
                  className="h-4 w-4 rounded border-slate-300 accent-brand-600"
                />
                Nazoratdan tashqari
              </label>
            )}
            <button
              type="button"
              onClick={() => remove(r.id)}
              className="rounded-lg p-2 text-slate-400 transition-colors hover:bg-red-50 hover:text-red-600"
            >
              <Trash2 className="h-4 w-4" />
            </button>
          </div>
        ))}
        {sorted.length === 0 && <p className="text-xs text-slate-400">Sabab yo'q — quyida qo'shing.</p>}
        {showOutOfControl && sorted.length > 0 && (
          <p className="text-xs text-slate-400">
            «Nazoratdan tashqari» — xodim ta'sir qila olmaydigan sabab (ko'chib ketish, sog'liq,
            oilaviy sharoit). Bunday ketishlar chiquvchi adminning ketish foiziga KIRMAYDI.
          </p>
        )}

        <div className="flex items-center gap-2 pt-1">
          <input
            value={adding}
            onChange={(e) => setAdding(e.target.value)}
            onKeyDown={(e) => e.key === 'Enter' && add()}
            placeholder="Yangi sabab..."
            className={cn(control, 'flex-1')}
          />
          <Button variant="secondary" onClick={add} disabled={busy || !adding.trim()}>
            <Plus className="h-4 w-4" /> Qo'shish
          </Button>
        </div>
      </div>
    </Card>
  )
}
