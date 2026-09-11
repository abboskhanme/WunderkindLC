import { useEffect, useState, type CSSProperties } from 'react'
import { Modal } from '@/components/ui/Modal'
import { Button } from '@/components/ui/Button'
import { completeAndTransferClass, type CompleteAndTransferResult } from '@/api/services/classes'
import { getSubjects } from '@/api/services/subjects'
import type { Subject } from '@/types'
import posthog from '@/lib/posthog'

/** "YYYY-MM-DD" bugungi sana */
const today = () => new Date().toISOString().slice(0, 10)

export function CompleteAndTransferModal({
  open,
  onClose,
  groupId,
  currentGroupName,
  currentCourseId,
  activeCount = 0,
  inactiveCount = 0,
  onSuccess,
}: {
  open: boolean
  onClose: () => void
  groupId: string
  currentGroupName: string
  currentCourseId?: string
  /** Guruhdagi AKTIV a'zolar soni — faqat shular yangi guruhga ko'chiriladi. */
  activeCount?: number
  /** A'zoligi bor, ammo aktiv EMAS (sinov/muzlatilgan) — ko'chirilmaydi. */
  inactiveCount?: number
  onSuccess?: (result: CompleteAndTransferResult) => void
}) {
  const [courses, setCourses] = useState<Subject[]>([])
  const [targetCourseId, setTargetCourseId] = useState<string>('')
  const [newGroupName, setNewGroupName] = useState('')
  const [notes, setNotes] = useState('')
  const [autoEnroll, setAutoEnroll] = useState(true)
  const [closeDate, setCloseDate] = useState(today())
  const [activateDate, setActivateDate] = useState(today())
  const [activateInNew, setActivateInNew] = useState(true)
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState<string | null>(null)

  // Load courses when modal opens
  useEffect(() => {
    if (!open) return
    // eslint-disable-next-line react-hooks/set-state-in-effect -- modal ochilganda sanalarni tiklash (maqsadli)
    setCloseDate(today())
    setActivateDate(today())
    getSubjects()
      .then((data) => {
        setCourses(data)
        // Pre-select current course as default if available
        if (currentCourseId) setTargetCourseId(currentCourseId)
      })
      .catch(() => setCourses([]))
  }, [open, currentCourseId])

  const selectedCourse = courses.find((c) => c.id === targetCourseId)
  const resolvedNewName = newGroupName.trim() || currentGroupName
  const datesInvalid = !closeDate || (autoEnroll && activateInNew && (!activateDate || activateDate < closeDate))

  async function handleSubmit() {
    if (datesInvalid) {
      setError("Aktivlashtirish sanasi yopish sanasidan oldin bo'lmasligi kerak")
      return
    }
    setLoading(true)
    setError(null)
    try {
      const result = await completeAndTransferClass(groupId, {
        autoEnrollNewGroup: autoEnroll,
        newGroupName: newGroupName.trim() || undefined,
        completionNotes: notes.trim() || undefined,
        targetCourseId: targetCourseId || undefined,
        closeDate,
        activateDate: activateDate || closeDate,
        activateInNewGroup: autoEnroll && activateInNew,
      })
      posthog.capture('class_completed_and_transferred', {
        auto_enrolled_active_students: autoEnroll,
        activated_in_new_group: autoEnroll && activateInNew,
        used_current_course: !targetCourseId || targetCourseId === currentCourseId,
      })
      onSuccess?.(result)
      onClose()
    } catch (err: any) {
      setError(err?.response?.data?.message || err?.message || 'Xatolik yuz berdi')
    } finally {
      setLoading(false)
    }
  }

  return (
    <Modal open={open} onClose={onClose} size="md">
      <div style={{ padding: '20px 24px' }}>
        {/* Header */}
        <div style={{ marginBottom: 20 }}>
          <div style={{ fontSize: 18, fontWeight: 800, marginBottom: 4 }}>
            Guruhni tugatish va yangi kursga o'tkazish
          </div>
          <div style={{ fontSize: 13, color: 'var(--mute)' }}>
            Eski guruh arxivlanadi, sertifikatlar beriladi va yangi kursga yangi guruh ochiladi.
          </div>
        </div>

        {/* Current group */}
        <div style={{ marginBottom: 16 }}>
          <div style={{ fontSize: 12, fontWeight: 700, color: 'var(--mute)', marginBottom: 4 }}>
            ARXIVLANADIGAN GURUH
          </div>
          <div
            style={{
              padding: '10px 12px',
              borderRadius: 8,
              background: 'var(--panel)',
              fontSize: 14,
              fontWeight: 600,
            }}
          >
            {currentGroupName}
            <div style={{ marginTop: 4, fontSize: 12, fontWeight: 500, color: 'var(--mute)' }}>
              {activeCount} ta aktiv a'zo
              {inactiveCount > 0 && ` · ${inactiveCount} ta sinov/muzlatilgan (ko'chirilmaydi)`}
            </div>
          </div>
        </div>

        {/* Yopish sanasi — eski guruh hisobi AYNAN shu sanagacha */}
        <div style={{ marginBottom: 16 }}>
          <label
            style={{ fontSize: 12, fontWeight: 700, color: 'var(--mute)', display: 'block', marginBottom: 6 }}
          >
            GURUH YOPILADIGAN SANA
          </label>
          <input
            type="date"
            value={closeDate}
            onChange={(e) => setCloseDate(e.target.value)}
            style={dateInputStyle}
          />
          <div style={{ fontSize: 11, color: 'var(--mute)', marginTop: 4 }}>
            A'zoliklar shu sanadan muzlatiladi: <strong>eski guruhga</strong> shu sanagacha o'qilgan
            darslar uchun oylik yoziladi, keyingi oylar hisobi bekor qilinadi.
          </div>
        </div>

        {/* Target course selector */}
        <div style={{ marginBottom: 16 }}>
          <label
            style={{ fontSize: 12, fontWeight: 700, color: 'var(--mute)', display: 'block', marginBottom: 6 }}
          >
            YANGI GURUH KURSI
          </label>
          <select
            value={targetCourseId}
            onChange={(e) => setTargetCourseId(e.target.value)}
            style={{
              width: '100%',
              padding: '10px 12px',
              borderRadius: 8,
              border: '1.5px solid var(--border)',
              fontSize: 14,
              fontFamily: 'inherit',
              outline: 'none',
              boxSizing: 'border-box',
              background: 'var(--paper)',
              color: 'var(--ink)',
              cursor: 'pointer',
            }}
          >
            <option value="">-- Kurs tanlang (ixtiyoriy) --</option>
            {courses.map((c) => (
              <option key={c.id} value={c.id}>
                {c.name}
                {c.price ? ` — ${c.price.toLocaleString()} so'm/oy` : ''}
              </option>
            ))}
          </select>
          {selectedCourse && (
            <div
              style={{
                marginTop: 6,
                padding: '8px 12px',
                borderRadius: 8,
                background: '#F0FDF4',
                fontSize: 12,
                color: '#166534',
                display: 'flex',
                gap: 12,
              }}
            >
              <span>
                <strong>{selectedCourse.name}</strong>
              </span>
              {selectedCourse.price > 0 && (
                <span>Oylik: {selectedCourse.price.toLocaleString()} so'm</span>
              )}
            </div>
          )}
          {!targetCourseId && (
            <div style={{ fontSize: 11, color: 'var(--mute)', marginTop: 4 }}>
              Bo'sh qolsa: eski guruh kursi qayta ishlatiladi
            </div>
          )}
        </div>

        {/* New group name */}
        <div style={{ marginBottom: 16 }}>
          <label
            style={{ fontSize: 12, fontWeight: 700, color: 'var(--mute)', display: 'block', marginBottom: 6 }}
          >
            YANGI GURUH NOMI (ixtiyoriy)
          </label>
          <input
            type="text"
            value={newGroupName}
            onChange={(e) => setNewGroupName(e.target.value)}
            placeholder={`Default: ${currentGroupName}`}
            maxLength={120}
            style={{
              width: '100%',
              padding: '10px 12px',
              borderRadius: 8,
              border: '1.5px solid var(--border)',
              fontSize: 14,
              fontFamily: 'inherit',
              outline: 'none',
              boxSizing: 'border-box',
            }}
          />
          <div style={{ fontSize: 11, color: 'var(--mute)', marginTop: 4 }}>
            Bo'sh qolsa: <strong>{currentGroupName}</strong>
          </div>
        </div>

        {/* Auto-enroll toggle */}
        <div
          style={{
            display: 'flex',
            alignItems: 'center',
            gap: 10,
            padding: '12px 14px',
            borderRadius: 8,
            border: '1.5px solid var(--border)',
            marginBottom: 16,
            cursor: 'pointer',
          }}
          onClick={() => setAutoEnroll((v) => !v)}
        >
          <input
            type="checkbox"
            checked={autoEnroll}
            onChange={(e) => setAutoEnroll(e.target.checked)}
            // Aks holda hodisa o'ram <div>ning onClick'iga ham yetib borib, ptichka IKKI marta
            // almashadi (ya'ni to'g'ridan-to'g'ri ptichkani bosish hech narsa qilmaydi).
            onClick={(e) => e.stopPropagation()}
            style={{ width: 16, height: 16, cursor: 'pointer' }}
          />
          <div>
            <div style={{ fontSize: 13, fontWeight: 700 }}>
              AKTIV o'quvchilarni yangi guruhga avtomatik qo'shish
            </div>
            <div style={{ fontSize: 12, color: 'var(--mute)' }}>
              Faqat aktiv a'zolar ({activeCount} ta) "{resolvedNewName}" guruhiga ko'chiriladi —
              sinovdagi va muzlatilganlar ko'chirilmaydi
            </div>
          </div>
        </div>

        {/* Yangi guruhda aktivlashtirish (sana bilan) */}
        {autoEnroll && (
          <div
            style={{
              padding: '12px 14px',
              borderRadius: 8,
              border: '1.5px solid var(--border)',
              marginBottom: 16,
            }}
          >
            <div
              style={{ display: 'flex', alignItems: 'center', gap: 10, cursor: 'pointer' }}
              onClick={() => setActivateInNew((v) => !v)}
            >
              <input
                type="checkbox"
                checked={activateInNew}
                onChange={(e) => setActivateInNew(e.target.checked)}
                onClick={(e) => e.stopPropagation()}
                style={{ width: 16, height: 16, cursor: 'pointer' }}
              />
              <div>
                <div style={{ fontSize: 13, fontWeight: 700 }}>
                  Yangi guruhda darhol aktivlashtirish
                </div>
                <div style={{ fontSize: 12, color: 'var(--mute)' }}>
                  O'chirilsa — ko'chirilganlar "sinov" statusida qoladi va to'lov hisoblanmaydi
                </div>
              </div>
            </div>

            {activateInNew && (
              <div style={{ marginTop: 12 }}>
                <label
                  style={{ fontSize: 12, fontWeight: 700, color: 'var(--mute)', display: 'block', marginBottom: 6 }}
                >
                  YANGI GURUHDA AKTIVLASHTIRISH SANASI
                </label>
                <input
                  type="date"
                  value={activateDate}
                  min={closeDate || undefined}
                  onChange={(e) => setActivateDate(e.target.value)}
                  style={dateInputStyle}
                />
                <div style={{ fontSize: 11, color: 'var(--mute)', marginTop: 4 }}>
                  Yangi guruh oyligi shu sanadan hisoblanadi. Eski guruhga ortiqcha to'langan pul
                  yangi guruhga ko'chiriladi.
                </div>
              </div>
            )}
          </div>
        )}

        {/* Notes */}
        <div style={{ marginBottom: 20 }}>
          <label
            style={{ fontSize: 12, fontWeight: 700, color: 'var(--mute)', display: 'block', marginBottom: 6 }}
          >
            TUGATISH IZOHI (ixtiyoriy)
          </label>
          <textarea
            value={notes}
            onChange={(e) => setNotes(e.target.value)}
            placeholder="Masalan: 'A1 darajasini muvaffaqiyatli yakunladi'"
            maxLength={500}
            style={{
              width: '100%',
              padding: '10px 12px',
              borderRadius: 8,
              border: '1.5px solid var(--border)',
              fontSize: 14,
              fontFamily: 'inherit',
              resize: 'vertical',
              minHeight: '72px',
              outline: 'none',
              boxSizing: 'border-box',
            }}
          />
          <div style={{ fontSize: 11, color: 'var(--mute)', marginTop: 4 }}>
            {notes.length}/500
          </div>
        </div>

        {/* Error message */}
        {error && (
          <div
            style={{
              padding: '10px 12px',
              borderRadius: 8,
              background: '#FEE2E2',
              color: '#DC2626',
              fontSize: 13,
              marginBottom: 16,
            }}
          >
            {error}
          </div>
        )}

        {/* Info box */}
        <div
          style={{
            padding: '12px 14px',
            borderRadius: 8,
            background: '#F0F9FF',
            fontSize: 12,
            lineHeight: 1.5,
            color: '#0369A1',
            marginBottom: 20,
          }}
        >
          <div style={{ fontWeight: 700, marginBottom: 4 }}>Nima bo'ladi?</div>
          <ul style={{ margin: '4px 0 0 16px', padding: 0 }}>
            <li>Barcha faol a'zolar sertifikat oladi (eski kurs uchun)</li>
            <li>
              A'zoliklar <strong>{closeDate || '—'}</strong> sanasidan muzlatiladi — shu sanagacha
              o'qilgan darslar uchun oylik <strong>eski guruhga</strong> yoziladi, keyingi oylar
              hisobi bekor qilinadi
            </li>
            <li>Sinovdagi a'zoliklar yakunlanadi (ularda hisob ochilmagan)</li>
            <li>Eski guruh arxivga o'tadi (o'quvchilar arxivlanmaydi, to'lov qabul qilinaveradi)</li>
            <li>
              {selectedCourse
                ? <><strong>{selectedCourse.name}</strong> kursi bilan yangi guruh "{resolvedNewName}" ochiladi</>
                : <>Eski kurs bilan yangi guruh "{resolvedNewName}" ochiladi</>
              }
            </li>
            {autoEnroll && (
              <>
                <li>
                  Yangi guruhga <strong>faqat aktiv a'zolar</strong> ({activeCount} ta) ko'chiriladi
                  {inactiveCount > 0 && (
                    <> — sinovdagi/muzlatilgan {inactiveCount} ta a'zo ko'chirilmaydi (eski guruhda yakunlanadi)</>
                  )}
                </li>
                {activateInNew
                  ? <li>Ular yangi guruhda <strong>{activateDate || closeDate || '—'}</strong> sanasidan aktivlashtiriladi</li>
                  : <li>Ular yangi guruhga "sinov" statusida qo'shiladi</li>}
              </>
            )}
          </ul>
        </div>

        {/* Buttons */}
        <div style={{ display: 'flex', gap: 8 }}>
          <Button variant="ghost" onClick={onClose} style={{ flex: 1 }} disabled={loading}>
            Bekor qilish
          </Button>
          <Button onClick={handleSubmit} style={{ flex: 1 }} disabled={loading || datesInvalid}>
            {loading ? 'Yuklanmoqda...' : 'Tugatish'}
          </Button>
        </div>
      </div>
    </Modal>
  )
}

/** Sana maydonlari uchun umumiy ko'rinish (modaldagi boshqa inputlar bilan bir xil). */
const dateInputStyle: CSSProperties = {
  width: '100%',
  padding: '10px 12px',
  borderRadius: 8,
  border: '1.5px solid var(--border)',
  fontSize: 14,
  fontFamily: 'inherit',
  outline: 'none',
  boxSizing: 'border-box',
  background: 'var(--paper)',
  color: 'var(--ink)',
}
