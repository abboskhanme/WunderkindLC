import { useEffect, useMemo, useRef, useState } from 'react'
import { Loader2, Search, ShoppingCart, User, X, Banknote, CreditCard, HandCoins } from 'lucide-react'
import type { Book, BookOrder, BookPaymentMethod, BookStudent } from '@/api/services/books'
import { searchBookStudents, sellBookManual } from '@/api/services/books'
import { Modal } from '@/components/ui/Modal'
import { Button } from '@/components/ui/Button'
import { Input, Select } from '@/components/ui/Input'
import { apiErrorMessage, cn, formatMoney, maskPhone } from '@/lib/utils'

interface Props {
  open: boolean
  /** Ombordagi kitoblar — tanlash uchun (faqat sotuvdagilar ko'rsatiladi). */
  books: Book[]
  onClose: () => void
  /** Sotuv muvaffaqiyatli bo'lgach — ro'yxatni yangilash uchun. */
  onSold: (order: BookOrder) => void
}

/** Hozirgi vaqt "HH:mm" — karta to'lovida standart qiymat. */
const nowTime = () => new Date().toTimeString().slice(0, 5)

/**
 * MARKAZDA QO'LDA SOTUV — "Buyurtmalar → Kitob sotish".
 *
 * Botdagi oqimning admin varianti: kitob → soni → (ixtiyoriy) o'quvchi → naqd/karta.
 *
 * O'QUVCHI IXTIYORIY: markazda o'qimaydigan odamga (ota-ona, o'tkinchi) ham kitob sotiladi.
 * Ilgari o'quvchini tanlash majburiy edi va kassir bunday sotuv uchun soxta o'quvchi yaratishga
 * majbur bo'lardi. Ism ham majburiy emas — bo'sh qolsa ro'yxatda "Noma'lum" bo'lib ko'rinadi.
 * Karta bo'lsa to'lov vaqti va kartaning oxirgi 4 raqami kiritiladi (chek rasmi YO'Q — pul
 * kassirning oldida to'langan). Buyurtma DARHOL tasdiqlangan holatda yaratiladi: qoldiq shu
 * zahoti ayiriladi va sotuv analitikaga tushadi.
 *
 * NASIYA: kitob beriladi, pul keyin olinadi. Bunda xaridor MAJBURIY (o'quvchini F.I.Sh. bo'yicha
 * qidirib tanlash yoki ismini yozish) — qarz kimda ekani yozilmasa nasiyaning ma'nosi qolmaydi.
 * Qoldiq baribir shu zahoti ayiriladi (kitob mijozning qo'lida), lekin summa tushumga emas
 * QARZGA tushadi va "Nasiya" tabida "To'landi" bosilguncha shunday qoladi.
 */
export function BookSellModal({ open, books, onClose, onSold }: Props) {
  const [bookId, setBookId] = useState('')
  const [qty, setQty] = useState('1')
  const [method, setMethod] = useState<BookPaymentMethod>('cash')
  const [cardLast4, setCardLast4] = useState('')
  const [paidTime, setPaidTime] = useState(nowTime())
  /** NASIYA: va'da qilingan to'lov sanasi va (o'quvchi tanlanmasa) xaridor telefoni. */
  const [dueDate, setDueDate] = useState('')
  const [buyerPhone, setBuyerPhone] = useState('')

  // O'quvchi qidiruvi
  const [query, setQuery] = useState('')
  const [results, setResults] = useState<BookStudent[]>([])
  const [searching, setSearching] = useState(false)
  const [student, setStudent] = useState<BookStudent | null>(null)
  /** O'quvchi tanlanmaganda — erkin yoziladigan xaridor ismi (ixtiyoriy). */
  const [buyerName, setBuyerName] = useState('')

  const [busy, setBusy] = useState(false)
  const [error, setError] = useState('')

  // Sotuvdagi kitoblar (qoldiq tugagani ham ko'rinadi — lekin tanlab bo'lmaydi).
  const sellable = useMemo(() => books.filter((b) => b.isActive), [books])
  const book = sellable.find((b) => b.id === bookId) ?? null
  const qtyNum = Number(qty)
  const total = book && Number.isFinite(qtyNum) ? book.price * Math.max(0, qtyNum) : 0

  // Oyna har ochilganda forma tozalanadi (oldingi sotuv qoldiqlari qolib ketmasin).
  useEffect(() => {
    if (!open) return
    setBookId('')
    setQty('1')
    setMethod('cash')
    setCardLast4('')
    setPaidTime(nowTime())
    setDueDate('')
    setBuyerPhone('')
    setQuery('')
    setResults([])
    setStudent(null)
    setBuyerName('')
    setError('')
  }, [open])

  // Qidiruv — 300ms debounce, kamida 2 belgi. O'quvchi tanlangach so'rov yuborilmaydi.
  const reqId = useRef(0)
  useEffect(() => {
    if (student) return
    const q = query.trim()
    if (q.length < 2) {
      setResults([])
      setSearching(false)
      return
    }
    setSearching(true)
    const my = ++reqId.current
    const t = setTimeout(async () => {
      try {
        const found = await searchBookStudents(q)
        // Kech kelgan javob yangisining ustiga yozib ketmasin.
        if (my === reqId.current) setResults(found)
      } catch {
        if (my === reqId.current) setResults([])
      } finally {
        if (my === reqId.current) setSearching(false)
      }
    }, 300)
    return () => clearTimeout(t)
  }, [query, student])

  const submit = async () => {
    if (busy) return
    if (!book) return setError('Kitobni tanlang')
    // O'quvchi ATAYIN tekshirilmaydi — u ixtiyoriy (chetdan xaridorga ham sotiladi).
    if (!Number.isInteger(qtyNum) || qtyNum <= 0) return setError('Sonini butun son sifatida kiriting')
    if (qtyNum > book.stock) return setError(`Omborda yetarli emas: qoldiq ${book.stock} dona`)
    if (method === 'card') {
      if (cardLast4.replace(/\D/g, '').length < 4)
        return setError("Karta raqamining oxirgi 4 raqamini kiriting")
      if (!paidTime) return setError("To'lov vaqtini kiriting")
    }
    // NASIYADA xaridor MAJBURIY — qarzni kimdan olish kerakligi yozilmasa nasiya ma'nosini
    // yo'qotadi (server ham shu qoidani tekshiradi).
    if (method === 'credit' && !student && !buyerName.trim())
      return setError(
        "Nasiyada xaridorni ko'rsating — o'quvchini F.I.Sh. bo'yicha qidirib tanlang yoki ismini yozing",
      )

    setBusy(true)
    setError('')
    try {
      const order = await sellBookManual({
        bookId: book.id,
        studentId: student?.id ?? null,
        // O'quvchi tanlansa ismi serverda o'quvchidan olinadi — erkin ism faqat tanlanmaganda.
        ...(student ? {} : { customerName: buyerName.trim() || undefined }),
        qty: qtyNum,
        paymentMethod: method,
        ...(method === 'card' ? { cardLast4: cardLast4.replace(/\D/g, '').slice(-4), paidTime } : {}),
        ...(method === 'credit'
          ? {
              dueDate: dueDate || undefined,
              ...(student ? {} : { customerPhone: buyerPhone.trim() || undefined }),
            }
          : {}),
      })
      onSold(order)
      onClose()
    } catch (err) {
      setError(apiErrorMessage(err, "Sotib bo'lmadi"))
    } finally {
      setBusy(false)
    }
  }

  return (
    <Modal
      open={open}
      onClose={busy ? () => {} : onClose}
      size="md"
      title="Kitob sotish (markazda)"
      footer={
        <>
          <Button variant="secondary" onClick={onClose} disabled={busy}>
            Bekor qilish
          </Button>
          <Button onClick={submit} disabled={busy || !book}>
            {busy ? <Loader2 className="h-4 w-4 animate-spin" /> : <ShoppingCart className="h-4 w-4" />}
            {method === 'credit' ? 'Nasiyaga berish' : 'Sotish'}
          </Button>
        </>
      }
    >
      <div className="space-y-4">
        {/* 1. Kitob */}
        <Select label="Kitob" required value={bookId} onChange={(e) => setBookId(e.target.value)}>
          <option value="">— tanlang —</option>
          {sellable.map((b) => (
            <option key={b.id} value={b.id} disabled={b.stock <= 0}>
              {b.title} — {formatMoney(b.price)} ({b.stock > 0 ? `${b.stock} dona` : 'tugagan'})
            </option>
          ))}
        </Select>

        {/* 2. Soni */}
        <Input
          label="Soni (dona)"
          required
          type="number"
          min={1}
          max={book?.stock || undefined}
          value={qty}
          onChange={(e) => setQty(e.target.value)}
        />
        {book && qtyNum > book.stock && (
          <p className="-mt-2 text-sm text-red-500">
            Omborda {book.stock} dona qolgan.
          </p>
        )}

        {/* 3. O'quvchi — qidirib tanlanadi */}
        <div>
          <label className="mb-1.5 block text-sm font-medium text-slate-600">
            O'quvchi <span className="font-normal text-slate-400">(ixtiyoriy)</span>
          </label>
          {student ? (
            <div className="flex items-center gap-3 rounded-lg border border-brand-200 bg-brand-50/60 px-3 py-2.5">
              <User className="h-4 w-4 shrink-0 text-brand-600" />
              <div className="min-w-0 flex-1">
                <div className="truncate text-sm font-semibold text-slate-800">
                  {student.fullName}
                  {student.isArchived && (
                    <span className="ml-2 rounded bg-slate-200 px-1.5 py-0.5 text-[11px] font-medium text-slate-600">
                      Arxiv
                    </span>
                  )}
                </div>
                <div className="truncate text-xs text-slate-500">
                  {[student.className, maskPhone(student.phone || student.parentPhone)]
                    .filter(Boolean)
                    .join(' · ') || '—'}
                </div>
              </div>
              <button
                type="button"
                onClick={() => {
                  setStudent(null)
                  setQuery('')
                  setResults([])
                }}
                className="shrink-0 rounded-lg p-1.5 text-slate-400 transition-colors hover:bg-white hover:text-slate-600"
                title="Boshqa o'quvchini tanlash"
              >
                <X className="h-4 w-4" />
              </button>
            </div>
          ) : (
            <>
              <div className="relative">
                <Search className="pointer-events-none absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-slate-400" />
                <input
                  autoFocus
                  value={query}
                  onChange={(e) => setQuery(e.target.value)}
                  placeholder="F.I.Sh yoki telefon (kamida 2 belgi)..."
                  className="w-full rounded-lg border border-slate-200 bg-white py-2.5 pl-9 pr-9 text-sm outline-none transition-colors focus:border-brand-400 focus:ring-2 focus:ring-brand-100"
                />
                {searching && (
                  <Loader2 className="absolute right-3 top-1/2 h-4 w-4 -translate-y-1/2 animate-spin text-slate-400" />
                )}
              </div>
              {query.trim().length >= 2 && !searching && results.length === 0 && (
                <p className="mt-2 text-sm text-slate-400">
                  O'quvchi topilmadi — quyiga ism yozib yoki umuman bo'sh qoldirib sotishingiz mumkin.
                </p>
              )}
              {results.length > 0 && (
                <ul className="mt-2 max-h-52 overflow-y-auto rounded-lg border border-slate-200">
                  {results.map((s) => (
                    <li key={s.id}>
                      <button
                        type="button"
                        onClick={() => setStudent(s)}
                        className="flex w-full items-center gap-3 border-b border-slate-100 px-3 py-2 text-left transition-colors last:border-b-0 hover:bg-slate-50"
                      >
                        <div className="min-w-0 flex-1">
                          <div className="truncate text-sm font-medium text-slate-800">
                            {s.fullName}
                            {s.isArchived && (
                              <span className="ml-2 rounded bg-slate-100 px-1.5 py-0.5 text-[11px] text-slate-500">
                                Arxiv
                              </span>
                            )}
                          </div>
                          <div className="truncate text-xs text-slate-400">
                            {[s.className, maskPhone(s.phone || s.parentPhone)].filter(Boolean).join(' · ') || '—'}
                          </div>
                        </div>
                      </button>
                    </li>
                  ))}
                </ul>
              )}
              {/* Markazda o'qimaydigan xaridor — ismi erkin yoziladi (bu ham ixtiyoriy). */}
              <div className="mt-3">
                <Input
                  label="Xaridor ismi (o'quvchi bo'lmasa)"
                  value={buyerName}
                  onChange={(e) => setBuyerName(e.target.value)}
                  placeholder="Masalan: Aziza Karimova (ota-ona)"
                  maxLength={120}
                />
                <p className={cn('mt-1 text-xs', method === 'credit' ? 'text-orange-600' : 'text-slate-400')}>
                  {method === 'credit'
                    ? "Nasiyada shart: o'quvchini tanlang yoki shu yerga xaridor ismini yozing."
                    : 'Bo\'sh qoldirsangiz ham sotish mumkin — ro\'yxatda "Noma\'lum" bo\'lib ko\'rinadi.'}
                </p>
              </div>
            </>
          )}
        </div>

        {/* 4. To'lov turi */}
        <div>
          <label className="mb-1.5 block text-sm font-medium text-slate-600">To'lov turi</label>
          <div className="grid grid-cols-3 gap-2">
            {([
              { value: 'cash' as const, label: 'Naqd', icon: Banknote },
              { value: 'card' as const, label: 'Karta', icon: CreditCard },
              { value: 'credit' as const, label: 'Nasiya', icon: HandCoins },
            ]).map((m) => (
              <button
                key={m.value}
                type="button"
                onClick={() => setMethod(m.value)}
                className={cn(
                  'flex items-center justify-center gap-2 rounded-lg border px-3 py-2.5 text-sm font-medium transition-colors',
                  method === m.value
                    ? 'border-brand-400 bg-brand-50 text-brand-700'
                    : 'border-slate-200 bg-white text-slate-600 hover:bg-slate-50',
                )}
              >
                <m.icon className="h-4 w-4" /> {m.label}
              </button>
            ))}
          </div>
        </div>

        {/* 5. Karta bo'lsa — to'lov vaqti va oxirgi 4 raqam */}
        {method === 'card' && (
          <div className="grid grid-cols-2 gap-3 rounded-lg bg-slate-50 p-3">
            <Input
              label="To'lov vaqti"
              required
              type="time"
              value={paidTime}
              onChange={(e) => setPaidTime(e.target.value)}
            />
            <Input
              label="Karta (oxirgi 4 raqam)"
              required
              inputMode="numeric"
              placeholder="1234"
              value={cardLast4}
              // Faqat raqam; to'liq karta raqami yopishtirilsa ham oxirgi 4 tasi olinadi.
              onChange={(e) => setCardLast4(e.target.value.replace(/\D/g, '').slice(-4))}
            />
            <p className="col-span-2 -mt-1 text-xs text-slate-400">
              To'liq karta raqami saqlanmaydi — faqat oxirgi 4 raqam.
            </p>
          </div>
        )}

        {/* 5b. Nasiya bo'lsa — muddat va (o'quvchi tanlanmagan bo'lsa) telefon */}
        {method === 'credit' && (
          <div className="space-y-3 rounded-lg border border-orange-200 bg-orange-50/60 p-3">
            <p className="text-sm text-orange-800">
              Kitob hoziroq beriladi va ombordan ayiriladi, lekin <b>pul qarzga yoziladi</b>. Pulni
              olgach «Nasiya» tabidan «To'landi» bosasiz — shundan keyin summa tushumga qo'shiladi.
            </p>
            <div className="grid gap-3 sm:grid-cols-2">
              <Input
                label="To'lov muddati (ixtiyoriy)"
                type="date"
                value={dueDate}
                onChange={(e) => setDueDate(e.target.value)}
              />
              {!student && (
                <Input
                  label="Xaridor telefoni (ixtiyoriy)"
                  inputMode="tel"
                  placeholder="90 123 45 67"
                  value={buyerPhone}
                  onChange={(e) => setBuyerPhone(e.target.value)}
                />
              )}
            </div>
            <p className="text-xs text-orange-700">
              Muddat qo'yilsa, o'tib ketgan qarzlar ro'yxatda qizil bo'lib ajralib turadi.
            </p>
          </div>
        )}

        {/* Jami */}
        {book && (
          <div className="flex items-center justify-between rounded-lg bg-slate-800 px-4 py-3 text-white">
            <span className="text-sm text-slate-300">
              {method === 'credit' ? 'Nasiya (qarz)' : 'Jami'}
            </span>
            <span className="font-mono text-lg font-bold">{formatMoney(total)}</span>
          </div>
        )}

        {error && <p className="text-sm text-red-500">{error}</p>}
      </div>
    </Modal>
  )
}
