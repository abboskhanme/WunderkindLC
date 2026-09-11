import { useCallback, useEffect, useState } from 'react'
import { Bot, Phone, Plus, Shuffle, Trash2 } from 'lucide-react'
import { Card } from '@/components/ui/Card'
import { Badge } from '@/components/ui/Badge'
import { Button } from '@/components/ui/Button'
import { Modal } from '@/components/ui/Modal'
import { Input, Select, Textarea } from '@/components/ui/Input'
import { Loader } from '@/components/ui/Loader'
import { usePerm } from '@/lib/permissions'
import { cn, apiErrorMessage, formatDate } from '@/lib/utils'
import { monthRange, todayIso } from '@/lib/month'
import { formatMonth } from '@/config/constants'
import {
  deleteKpiTicket,
  getKpiCallSamples,
  getKpiTicketMeta,
  getKpiTickets,
  saveKpiTicket,
  type KpiCallSampleDto,
  type KpiProfileDto,
  type KpiTicketDto,
  type KpiTicketMetaDto,
} from '@/api/services/kpi'
import {
  TICKET_STATUSES,
  callDirectionLabel,
  ticketStatus,
  useKpiParams,
  useKpiStaff,
} from './model'
import { EmptyState, ErrorBanner, KpiShell, MonthPicker, StaffPicker } from './shared'

/**
 * «TIKETLAR» — sifat nazorati.
 *
 * <p>Tiket — bu XATO yozuvi (yashirin mijoz auditining 13 mezoniga bog'langan). Ro'yxat,
 * tiket qo'yish oynasi, xodimning E'TIROZI va rahbarning tasdiqi/bekori shu yerda.</p>
 *
 * <p>⚠️ ENG MUHIM: <b>PULGA faqat «Tasdiqlandi» holati ta'sir qiladi.</b> «Taklif qilindi»
 * va «E'tiroz bildirildi» — hali qaror emas. Bu sahifada har joyda ochiq yoziladi: aks
 * holda xodim har taklifni jarima deb qabul qilib, keraksiz e'tiroz oqimi boshlanardi.</p>
 *
 * <p>⚠️ Tiketni AI O'ZI qo'ymaydi — u faqat «Taklif qilindi» holatida keladi, yakuniy
 * qaror odamniki (`.claude/rules/kpi.md` §8).</p>
 */
export function KpiTicketsPage() {
  const { can } = usePerm()
  const canCreate = can('kpi.tickets', 'create')
  const canEdit = can('kpi.tickets', 'edit')
  const canDelete = can('kpi.tickets', 'delete')

  const { userId, month, setUserId, setMonth } = useKpiParams()
  const { profiles, error: staffError } = useKpiStaff()
  const [status, setStatus] = useState('')

  const [meta, setMeta] = useState<KpiTicketMetaDto | null>(null)
  const [tickets, setTickets] = useState<KpiTicketDto[]>([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const [tick, setTick] = useState(0)

  const [modal, setModal] = useState<{ open: boolean; ticket: KpiTicketDto | null }>({
    open: false,
    ticket: null,
  })
  const [callsOpen, setCallsOpen] = useState(false)

  // Katalog (sabablar + 13 mezon + xodimlar) — bir marta.
  useEffect(() => {
    let alive = true
    getKpiTicketMeta()
      .then((m) => {
        if (alive) setMeta(m)
      })
      .catch(() => {
        /* katalog yuklanmasa ham RO'YXAT ishlaydi — faqat oyna ochilmaydi (pastda aytiladi). */
      })
    return () => {
      alive = false
    }
  }, [])

  useEffect(() => {
    let alive = true
    const { from, to } = monthRange(month)
    getKpiTickets({ from, to, userId, status })
      .then((list) => {
        if (!alive) return
        setTickets(list)
        setError(null)
        setLoading(false)
      })
      .catch((err) => {
        if (!alive) return
        setError(apiErrorMessage(err, 'Tiketlarni yuklab bo‘lmadi'))
        setLoading(false)
      })
    return () => {
      alive = false
    }
  }, [month, userId, status, tick])

  const reload = useCallback(() => setTick((t) => t + 1), [])

  const remove = async (t: KpiTicketDto) => {
    if (!confirm(`«${t.reasonLabel}» tiketi o'chirilsinmi? Bu amal qaytarilmaydi.`)) return
    try {
      await deleteKpiTicket(t.id)
      reload()
    } catch (err) {
      alert(apiErrorMessage(err, 'Tiketni o‘chirib bo‘lmadi'))
    }
  }

  const setStatusOf = async (t: KpiTicketDto, next: string) => {
    const meta2 = ticketStatus(next)
    if (
      meta2.costs &&
      !confirm(
        `Tiket TASDIQLANSIN mi? Tasdiqlangandan keyin u ${t.userName} ning oyligidan jarima ` +
          `bo'lib yechiladi va koeffitsientga ham ta'sir qiladi.`,
      )
    )
      return
    try {
      await saveKpiTicket({
        id: t.id,
        userId: t.userId,
        date: t.date,
        reasonCode: t.reasonCode,
        criterionNo: t.criterionNo,
        callId: t.callId,
        note: t.note,
        status: next,
        disputeNote: t.disputeNote,
      })
      reload()
    } catch (err) {
      alert(apiErrorMessage(err, 'Holatni o‘zgartirib bo‘lmadi'))
    }
  }

  const confirmedCount = tickets.filter((t) => t.status === 'confirmed').length

  return (
    <KpiShell
      sub="Yashirin mijoz auditi va kundalik sifat nazorati"
      actions={
        <>
          <StaffPicker profiles={profiles} value={userId} onChange={setUserId} includeAll />
          <Select value={status} onChange={(e) => setStatus(e.target.value)} className="w-auto">
            <option value="">Barcha holat</option>
            {TICKET_STATUSES.map((s) => (
              <option key={s.value} value={s.value}>
                {s.label}
              </option>
            ))}
          </Select>
          <MonthPicker month={month} onChange={setMonth} />
          {canCreate && (
            <>
              <Button variant="secondary" onClick={() => setCallsOpen(true)}>
                <Shuffle className="h-4 w-4" />
                Haftalik tekshiruv
              </Button>
              <Button onClick={() => setModal({ open: true, ticket: null })}>
                <Plus className="h-4 w-4" />
                Tiket qo'yish
              </Button>
            </>
          )}
        </>
      }
    >
      {staffError && <ErrorBanner text={staffError} />}
      {error && <ErrorBanner text={error} onRetry={reload} />}

      {/* Modulning eng ko'p yanglishtiradigan joyi — shuning uchun ro'yxat TEPASIDA. */}
      <div className="mb-4 rounded-xl border border-slate-200 bg-slate-50 px-4 py-3 text-[12.5px] text-slate-600">
        <b>Faqat «Tasdiqlandi» holati pul yechadi.</b> «Taklif qilindi» va «E'tiroz bildirildi» —
        hali qaror emas va oylikka ta'sir qilmaydi. {formatMonth(month)}: jami{' '}
        <b>{tickets.length}</b> ta tiket, shundan <b>{confirmedCount}</b> tasi tasdiqlangan.
      </div>

      {loading && tickets.length === 0 ? (
        <Loader label="Yuklanmoqda..." />
      ) : tickets.length === 0 ? (
        <Card>
          <EmptyState
            title={`${formatMonth(month)} uchun tiket yo'q`}
            hint={
              canCreate
                ? "Sifat nazoratini «Haftalik tekshiruv» tugmasidan boshlash qulay: tasodifiy 5 qo'ng'iroq tanlanadi."
                : "Tiket qo'yish uchun «KPI → Tiketlar» bo'yicha qo'shish ruxsati kerak."
            }
          />
        </Card>
      ) : (
        <Card tight>
          <div className="divide-y divide-slate-100">
            {tickets.map((t) => (
              <TicketRow
                key={t.id}
                ticket={t}
                canEdit={canEdit}
                canDelete={canDelete}
                onEdit={() => setModal({ open: true, ticket: t })}
                onStatus={(s) => setStatusOf(t, s)}
                onDelete={() => remove(t)}
              />
            ))}
          </div>
        </Card>
      )}

      {/* ⚠️ `key` — oyna har ochilganda QAYTA yaratiladi, boshlang'ich qiymatlar
          `useState` initializer'ida qoladi (effekt ichida setState qilinmaydi). */}
      {modal.open && (
        <TicketModal
          key={modal.ticket?.id ?? 'new'}
          open
          meta={meta}
          ticket={modal.ticket}
          defaultUserId={userId}
          canEdit={canEdit}
          onClose={() => setModal({ open: false, ticket: null })}
          onSaved={() => {
            setModal({ open: false, ticket: null })
            reload()
          }}
        />
      )}

      {callsOpen && (
        <CallSamplesModal
          key={`calls-${userId}`}
          open
          profiles={profiles}
          defaultUserId={userId}
          onClose={() => setCallsOpen(false)}
          onTicket={(callId, forUserId) => {
            setCallsOpen(false)
            setModal({
              open: true,
              ticket: {
                id: '',
                userId: forUserId,
                userName: '',
                date: todayIso(),
                reasonCode: '',
                reasonLabel: '',
                criterionNo: null,
                criterionLabel: null,
                callId,
                note: null,
                status: 'proposed',
                issuedBy: null,
                issuedAt: '',
                disputeNote: null,
                resolvedBy: null,
                resolvedAt: null,
              },
            })
          }}
        />
      )}
    </KpiShell>
  )
}

/** Ro'yxatdagi bitta tiket: sabab · mezon · izoh · e'tiroz · holat tugmalari. */
function TicketRow({
  ticket,
  canEdit,
  canDelete,
  onEdit,
  onStatus,
  onDelete,
}: {
  ticket: KpiTicketDto
  canEdit: boolean
  canDelete: boolean
  onEdit: () => void
  onStatus: (status: string) => void
  onDelete: () => void
}) {
  const st = ticketStatus(ticket.status)
  return (
    <div className={cn('px-[18px] py-3.5', st.costs && 'bg-red-50/40')}>
      <div className="flex flex-wrap items-start justify-between gap-3">
        <div className="min-w-0 flex-1">
          <div className="flex flex-wrap items-center gap-2">
            <span className="text-sm font-bold text-slate-800">{ticket.userName}</span>
            <Badge tone={st.tone}>{st.label}</Badge>
            {ticket.criterionNo != null && (
              <Badge tone="blue">
                {ticket.criterionNo}-mezon
                {ticket.criterionLabel ? `: ${ticket.criterionLabel}` : ''}
              </Badge>
            )}
            {ticket.callId && (
              <Badge tone="default">
                <Phone className="h-3 w-3" />
                qo'ng'iroq
              </Badge>
            )}
          </div>

          <p className="mt-1 text-sm font-medium text-slate-700">{ticket.reasonLabel}</p>
          {ticket.note && <p className="mt-0.5 text-[12.5px] text-slate-500">{ticket.note}</p>}

          {ticket.disputeNote && (
            <div className="mt-2 rounded-lg border border-sky-200 bg-sky-50 px-3 py-2">
              <p className="text-[11px] font-bold uppercase tracking-wide text-sky-700">
                Xodimning e'tirozi
              </p>
              <p className="mt-0.5 text-[12.5px] text-sky-900">{ticket.disputeNote}</p>
            </div>
          )}

          <p className="mt-1.5 text-[11.5px] text-slate-400">
            {formatDate(ticket.date)}
            {ticket.issuedBy && ` · qo'ydi: ${ticket.issuedBy}`}
            {ticket.resolvedBy && ` · hal qildi: ${ticket.resolvedBy}`}
            {' · '}
            {st.hint}
          </p>
        </div>

        <div className="flex shrink-0 flex-wrap items-center gap-1.5">
          {canEdit && ticket.status !== 'confirmed' && (
            <Button variant="secondary" onClick={() => onStatus('confirmed')}>
              Tasdiqlash
            </Button>
          )}
          {canEdit && ticket.status !== 'cancelled' && (
            <Button variant="ghost" onClick={() => onStatus('cancelled')}>
              Bekor qilish
            </Button>
          )}
          {canEdit && (
            <Button variant="ghost" onClick={onEdit}>
              Tahrir
            </Button>
          )}
          {canDelete && (
            <button
              type="button"
              onClick={onDelete}
              title="O'chirish"
              className="flex h-8 w-8 items-center justify-center rounded-lg border border-slate-200 text-slate-400 transition-colors hover:bg-red-50 hover:text-red-600"
            >
              <Trash2 className="h-4 w-4" />
            </button>
          )}
        </div>
      </div>
    </div>
  )
}

/**
 * Tiket qo'yish / tahrirlash oynasi.
 *
 * ⚠️ Boshlang'ich qiymatlar `useState` INITIALIZER'ida — oyna `key` bilan qayta yaratilgani
 * uchun effekt ichida setState qilish kerak emas (`react-hooks/set-state-in-effect`).
 */
function TicketModal({
  open,
  meta,
  ticket,
  defaultUserId,
  canEdit,
  onClose,
  onSaved,
}: {
  open: boolean
  meta: KpiTicketMetaDto | null
  ticket: KpiTicketDto | null
  defaultUserId: string
  canEdit: boolean
  onClose: () => void
  onSaved: () => void
}) {
  const editing = !!ticket?.id
  const [userId, setUserId] = useState(ticket?.userId || defaultUserId)
  const [date, setDate] = useState(ticket?.date || todayIso())
  const [reasonCode, setReasonCode] = useState(ticket?.reasonCode ?? '')
  const [criterionNo, setCriterionNo] = useState(
    ticket?.criterionNo != null ? String(ticket.criterionNo) : '',
  )
  const [callId, setCallId] = useState(ticket?.callId ?? '')
  const [note, setNote] = useState(ticket?.note ?? '')
  const [statusValue, setStatusValue] = useState(ticket?.status || 'proposed')
  const [disputeNote, setDisputeNote] = useState(ticket?.disputeNote ?? '')
  const [saving, setSaving] = useState(false)

  const reasons = meta?.reasons ?? []
  const criteria = meta?.criteria ?? []
  const staff = meta?.staff ?? []
  const role = staff.find((s) => s.userId === userId)?.roleCode ?? ''
  // Sabab ro'yxati XODIMNING ROLIGA qarab toraytiriladi; umumiy sabablar (`roleCode` bo'sh)
  // har doim qoladi — aks holda "Boshqa" tanlab bo'lmasdi.
  const visibleReasons = reasons.filter((r) => !r.roleCode || !role || r.roleCode === role)

  /** Sabab tanlanganda unga bog'langan MEZON o'zi qo'yiladi (qo'lda ham o'zgartirsa bo'ladi). */
  const pickReason = (code: string) => {
    setReasonCode(code)
    const r = reasons.find((x) => x.code === code)
    if (r) setCriterionNo(r.criterionNo != null ? String(r.criterionNo) : '')
  }

  const save = async () => {
    if (!userId) return alert('Xodimni tanlang.')
    if (!reasonCode) return alert('Sababni tanlang.')
    setSaving(true)
    try {
      await saveKpiTicket({
        id: ticket?.id || null,
        userId,
        date,
        reasonCode,
        criterionNo: criterionNo === '' ? null : Number(criterionNo),
        callId: callId.trim() || null,
        note: note.trim() || null,
        status: statusValue,
        disputeNote: disputeNote.trim() || null,
      })
      onSaved()
    } catch (err) {
      alert(apiErrorMessage(err, 'Tiketni saqlab bo‘lmadi'))
    } finally {
      setSaving(false)
    }
  }

  const st = ticketStatus(statusValue)

  return (
    <Modal
      open={open}
      onClose={onClose}
      title={editing ? 'Tiketni tahrirlash' : "Tiket qo'yish"}
      size="lg"
      footer={
        <>
          <Button variant="secondary" onClick={onClose}>
            Bekor qilish
          </Button>
          <Button onClick={save} disabled={saving || !canEdit}>
            {saving ? 'Saqlanmoqda...' : 'Saqlash'}
          </Button>
        </>
      }
    >
      {!meta && (
        <ErrorBanner text="Sabablar katalogi yuklanmadi — sahifani yangilab qayta urinib ko‘ring." />
      )}

      <div className="grid gap-4 sm:grid-cols-2">
        <Select label="Xodim" required value={userId} onChange={(e) => setUserId(e.target.value)}>
          <option value="">— tanlang —</option>
          {staff.map((s) => (
            <option key={s.userId} value={s.userId}>
              {s.userName} — {s.roleLabel}
            </option>
          ))}
        </Select>

        <Input
          label="Sana"
          required
          type="date"
          value={date}
          max={todayIso()}
          onChange={(e) => setDate(e.target.value)}
        />

        <Select
          label="Sabab"
          required
          value={reasonCode}
          onChange={(e) => pickReason(e.target.value)}
        >
          <option value="">— tanlang —</option>
          {visibleReasons.map((r) => (
            <option key={r.code} value={r.code}>
              {r.label}
            </option>
          ))}
        </Select>

        <Select
          label="Audit mezoni"
          value={criterionNo}
          onChange={(e) => setCriterionNo(e.target.value)}
        >
          <option value="">— mezonsiz —</option>
          {criteria.map((c) => (
            <option key={c.no} value={String(c.no)}>
              {c.no}. {c.label}
            </option>
          ))}
        </Select>

        <Input
          label="Qo'ng'iroq ID (ixtiyoriy)"
          value={callId}
          placeholder="Sifat nazoratidagi qo'ng'iroq"
          onChange={(e) => setCallId(e.target.value)}
        />

        <Select
          label="Holat"
          value={statusValue}
          onChange={(e) => setStatusValue(e.target.value)}
        >
          {TICKET_STATUSES.map((s) => (
            <option key={s.value} value={s.value}>
              {s.label}
            </option>
          ))}
        </Select>
      </div>

      <div
        className={cn(
          'mt-3 rounded-lg border px-3.5 py-2.5 text-[12.5px]',
          st.costs
            ? 'border-red-200 bg-red-50 text-red-800'
            : 'border-slate-200 bg-slate-50 text-slate-600',
        )}
      >
        <b>{st.label}:</b> {st.hint}
        {st.costs && <> — jarima summasi «Qoidalar» sahifasidagi <b>Tiket jarimasi</b> qiymati.</>}
      </div>

      <div className="mt-4 space-y-4">
        <Textarea
          label="Izoh (nima bo'ldi)"
          rows={3}
          value={note}
          onChange={(e) => setNote(e.target.value)}
          placeholder="Qisqacha: nima kuzatildi, qaysi holatda"
        />
        <Textarea
          label="Xodimning e'tirozi"
          rows={2}
          value={disputeNote}
          onChange={(e) => setDisputeNote(e.target.value)}
          placeholder="Xodim rozi bo'lmasa — o'z izohini shu yerga yozadi"
        />
      </div>
    </Modal>
  )
}

/**
 * HAFTALIK SIFAT NAZORATI: xodimning tasodifiy 5 qo'ng'irog'i.
 *
 * <p>Har qatorda yozuv, transkript va (bo'lsa) AI tahlili ochiladi — rahbar tinglab,
 * darhol tiket qo'yishi mumkin. Tanlash TASODIFIY: xodim qaysi qo'ng'iroq tekshirilishini
 * oldindan bilmasligi kerak.</p>
 */
function CallSamplesModal({
  open,
  profiles,
  defaultUserId,
  onClose,
  onTicket,
}: {
  open: boolean
  profiles: KpiProfileDto[]
  defaultUserId: string
  onClose: () => void
  onTicket: (callId: string, userId: string) => void
}) {
  const [userId, setUserId] = useState(defaultUserId || profiles[0]?.userId || '')
  const [calls, setCalls] = useState<KpiCallSampleDto[]>([])
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [openId, setOpenId] = useState<string | null>(null)
  const [tick, setTick] = useState(0)
  const [requested, setRequested] = useState(false)

  useEffect(() => {
    if (!requested || !userId) return
    let alive = true
    getKpiCallSamples(userId, 5)
      .then((list) => {
        if (!alive) return
        setCalls(list)
        setError(null)
        setLoading(false)
      })
      .catch((err) => {
        if (!alive) return
        setError(apiErrorMessage(err, 'Qo‘ng‘iroqlarni yuklab bo‘lmadi'))
        setLoading(false)
      })
    return () => {
      alive = false
    }
  }, [userId, tick, requested])

  const request = () => {
    if (!userId) return alert('Xodimni tanlang.')
    setRequested(true)
    setLoading(true)
    setTick((t) => t + 1)
  }

  return (
    <Modal
      open={open}
      onClose={onClose}
      title="Haftalik sifat nazorati — 5 tasodifiy qo'ng'iroq"
      size="xl"
      footer={
        <Button variant="secondary" onClick={onClose}>
          Yopish
        </Button>
      }
    >
      <div className="mb-4 flex flex-wrap items-end gap-2">
        <StaffPicker
          profiles={profiles}
          value={userId}
          onChange={setUserId}
          label="Xodim"
          includeAll
        />
        <Button onClick={request} disabled={loading}>
          <Shuffle className="h-4 w-4" />
          {loading ? 'Tanlanmoqda...' : 'Tasodifiy 5 ta olish'}
        </Button>
      </div>

      {error && <ErrorBanner text={error} onRetry={request} />}

      {!requested ? (
        <EmptyState
          title="Tekshiruv boshlanmagan"
          hint="Xodimni tanlab «Tasodifiy 5 ta olish» tugmasini bosing. Tanlov TASODIFIY: xodim qaysi qo'ng'iroq tinglanishini oldindan bilmasligi kerak."
        />
      ) : loading ? (
        <Loader label="Tanlanmoqda..." />
      ) : calls.length === 0 ? (
        <EmptyState
          title="Bu xodimda tekshiriladigan qo'ng'iroq topilmadi"
          hint="Qo'ng'iroqlar Qo'ng'iroqlar bo'limidan olinadi — yozuv bo'lmasa tekshirish uchun namuna ham bo'lmaydi."
        />
      ) : (
        <div className="divide-y divide-slate-100 rounded-xl border border-slate-200">
          {calls.map((c) => (
            <div key={c.callId} className="px-4 py-3">
              <div className="flex flex-wrap items-center justify-between gap-2">
                <div className="min-w-0">
                  <p className="text-sm font-semibold text-slate-800">
                    {c.phoneNumber}
                    <span className="ml-2 text-[11.5px] font-medium text-slate-400">
                      {callDirectionLabel(c.direction)} ·{' '}
                      {formatDate(c.startedAt.slice(0, 10))} · {formatDuration(c.durationSeconds)}
                    </span>
                  </p>
                  <div className="mt-1 flex flex-wrap gap-1.5">
                    {c.hasRecording ? (
                      <Badge tone="green">yozuv bor</Badge>
                    ) : (
                      <Badge tone="default">yozuv yo'q</Badge>
                    )}
                    {c.transcript && <Badge tone="blue">transkript</Badge>}
                    {c.aiAnalysis && (
                      <Badge tone="violet">
                        <Bot className="h-3 w-3" />
                        AI tahlil
                      </Badge>
                    )}
                  </div>
                </div>
                <div className="flex shrink-0 gap-1.5">
                  {(c.transcript || c.aiAnalysis) && (
                    <Button
                      variant="ghost"
                      onClick={() => setOpenId(openId === c.callId ? null : c.callId)}
                    >
                      {openId === c.callId ? 'Yopish' : 'Ochish'}
                    </Button>
                  )}
                  <Button variant="secondary" onClick={() => onTicket(c.callId, userId)}>
                    Tiket qo'yish
                  </Button>
                </div>
              </div>

              {openId === c.callId && (
                <div className="mt-3 space-y-3">
                  {c.transcript && (
                    <div className="rounded-lg border border-slate-200 bg-slate-50 p-3">
                      <p className="mb-1 text-[11px] font-bold uppercase tracking-wide text-slate-400">
                        Transkript
                      </p>
                      <p className="whitespace-pre-wrap text-[12.5px] text-slate-700">
                        {c.transcript}
                      </p>
                    </div>
                  )}
                  {c.aiAnalysis && (
                    <div className="rounded-lg border border-brand-200 bg-brand-50 p-3">
                      <p className="mb-1 text-[11px] font-bold uppercase tracking-wide text-brand-600">
                        AI tahlil
                      </p>
                      <p className="whitespace-pre-wrap text-[12.5px] text-slate-700">
                        {c.aiAnalysis}
                      </p>
                    </div>
                  )}
                </div>
              )}
            </div>
          ))}
        </div>
      )}

      <p className="mt-4 text-[11.5px] text-slate-400">
        Bu yerdan qo'yilgan tiket <b>«Taklif qilindi»</b> holatida yaratiladi — pulga ta'sir
        qilmaydi. Jarima faqat siz uni ro'yxatda TASDIQLAGANINGIZDAN keyin, «Qoidalar»
        sahifasidagi tiket jarimasi summasida hisobga olinadi.
      </p>
    </Modal>
  )
}

/** Soniyalarni "3:45" ko'rinishiga o'giradi. */
function formatDuration(seconds: number): string {
  if (!Number.isFinite(seconds) || seconds <= 0) return '0:00'
  const m = Math.floor(seconds / 60)
  const s = Math.floor(seconds % 60)
  return `${m}:${String(s).padStart(2, '0')}`
}
