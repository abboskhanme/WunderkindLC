import { useEffect, useRef, useState } from 'react'
import { BookOpen, CheckCircle2 } from 'lucide-react'
import { getBooks, sellBookManual } from '@/api/services/books'
import { getKassaBooks, sellKassaBook, type KassaBook } from '@/api/services/kassa'
import { Button } from '@/components/ui/Button'
import { DrawerChoice, DrawerField, DrawerInput, DrawerSelect } from '@/components/ui/RightDrawer'
import { NumberInput } from '@/components/ui/NumberInput'
import { apiErrorMessage, formatMoney } from '@/lib/utils'
import { toast } from '@/lib/toast'

/**
 * O'quvchi to'lov oynasidagi «KITOB SOTUVI» turi (2026-10-13, owner: "to'lov turlari ichida kitob sotuvi bo'lishi
 * kerak"). Kitob + soni + naqd/karta → shu o'quvchiga qo'lda sotuv (server `BookManualSale` — «Kitoblar» bo'limidagi
 * «Kitob sotish» bilan BITTA mantiq): qoldiq ayiriladi, pul kassaga kirim (`book`), o'quvchi BALANSIGA tegmaydi.
 * Kanal: `kassa` — kassa portali (`kassa` ruxsati), `admin` — admin paneli («Kitoblar» ruxsati). Nasiya — «Kitoblar»da.
 */
export function StudentBookSale({ studentId, channel }: { studentId: string; channel: 'admin' | 'kassa' }) {
  const [books, setBooks] = useState<KassaBook[] | null>(null)
  const [loadError, setLoadError] = useState<string | null>(null)
  const [tick, setTick] = useState(0)
  const [bookId, setBookId] = useState('')
  const [qty, setQty] = useState<number | null>(1)
  const [method, setMethod] = useState<'cash' | 'card'>('cash')
  const [cardLast4, setCardLast4] = useState('')
  const [paidTime, setPaidTime] = useState(() => {
    const d = new Date()
    return `${String(d.getHours()).padStart(2, '0')}:${String(d.getMinutes()).padStart(2, '0')}`
  })
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [sold, setSold] = useState<string | null>(null)
  // Sinxron qulf — tez ikki bosishda kitob ikki marta sotilmasin.
  const inFlight = useRef(false)

  useEffect(() => {
    let alive = true
    const load =
      channel === 'kassa'
        ? getKassaBooks()
        : getBooks(true).then((bs) => bs.map((b) => ({ id: b.id, title: b.title, price: b.price, stock: b.stock })))
    load
      .then((bs) => alive && setBooks(bs))
      .catch((err: unknown) => alive && setLoadError(apiErrorMessage(err, "Kitoblar ro'yxatini yuklab bo'lmadi")))
    return () => {
      alive = false
    }
  }, [channel, tick])

  const book = books?.find((b) => b.id === bookId)
  const total = book && qty ? book.price * qty : 0
  const tooMany = !!book && !!qty && qty > book.stock
  const canSell =
    !!book && !!qty && qty > 0 && !tooMany && !busy && (method === 'cash' || (cardLast4.length === 4 && !!paidTime))

  const sell = async () => {
    if (!canSell || inFlight.current || !book || !qty) return
    inFlight.current = true
    setBusy(true)
    setError(null)
    try {
      const card = method === 'card' ? { cardLast4, paidTime } : {}
      const r =
        channel === 'kassa'
          ? await sellKassaBook(studentId, { bookId: book.id, qty, paymentMethod: method, ...card })
          : await sellBookManual({ bookId: book.id, studentId, qty, paymentMethod: method, ...card })
      const line = `${r.bookTitle} × ${r.qty} — ${formatMoney(r.total)} (buyurtma #${r.number})`
      setSold(line)
      toast.success('Kitob sotildi', line)
      setQty(1)
      setTick((t) => t + 1) // qoldiq yangilansin
    } catch (err: unknown) {
      setError(apiErrorMessage(err, "Kitobni sotib bo'lmadi"))
    } finally {
      inFlight.current = false
      setBusy(false)
    }
  }

  if (loadError) return <p className="rounded-lg bg-red-50 px-3 py-2 text-sm text-red-700">{loadError}</p>
  if (!books) return <p className="text-sm text-[#6b7280]">Kitoblar yuklanmoqda…</p>
  if (books.length === 0)
    return <p className="rounded-lg bg-amber-50 px-3 py-2 text-sm text-amber-700">Sotuvdagi kitob yo'q («Kitoblar sotuvi» bo'limida qo'shiladi).</p>

  return (
    <div className="space-y-4">
      {sold && (
        <div className="flex items-start gap-2 rounded-lg bg-emerald-50 px-3 py-2 text-sm text-emerald-700">
          <CheckCircle2 className="mt-0.5 h-4 w-4 shrink-0" />
          <span>
            Sotildi: {sold}. Pul kassaga kirim bo'lib tushdi, o'quvchi balansi o'zgarmadi.
          </span>
        </div>
      )}

      <DrawerField label="Kitob" required>
        <DrawerSelect value={bookId} onChange={(e) => setBookId(e.target.value)}>
          <option value="">— kitobni tanlang —</option>
          {books.map((b) => (
            <option key={b.id} value={b.id} disabled={b.stock <= 0}>
              {b.title} — {formatMoney(b.price)} {b.stock <= 0 ? '(qolmagan)' : `(qoldiq ${b.stock})`}
            </option>
          ))}
        </DrawerSelect>
      </DrawerField>

      <DrawerField
        label="Soni"
        required
        hint={tooMany ? <span className="text-red-600">Omborda {book?.stock} dona qolgan</span> : undefined}
      >
        <NumberInput as={DrawerInput} value={qty} onChange={setQty} />
      </DrawerField>

      <DrawerField label="Jami">
        <p className="flex items-center gap-2 text-[15px] font-semibold text-black">
          <BookOpen className="h-4 w-4 text-[#6b7280]" />
          {formatMoney(total)}
        </p>
      </DrawerField>

      <DrawerField label="To'lov usuli" group>
        <DrawerChoice
          options={[
            { value: 'cash' as const, label: 'Naqd' },
            { value: 'card' as const, label: 'Karta' },
          ]}
          value={method}
          onChange={setMethod}
        />
      </DrawerField>

      {method === 'card' && (
        <>
          <DrawerField label="Karta raqami (oxirgi 4 raqam)" required hint="Faqat oxirgi 4 raqam saqlanadi.">
            <DrawerInput
              inputMode="numeric"
              value={cardLast4}
              onChange={(e) => setCardLast4(e.target.value.replace(/\D/g, '').slice(-4))}
              placeholder="1234"
              maxLength={4}
            />
          </DrawerField>
          <DrawerField label="To'lov vaqti" required>
            <DrawerInput type="time" value={paidTime} onChange={(e) => setPaidTime(e.target.value)} />
          </DrawerField>
        </>
      )}

      {error && <p className="rounded-lg border border-red-200 bg-red-50 px-3 py-2 text-sm text-red-700">{error}</p>}

      <Button className="w-full justify-center" disabled={!canSell} onClick={() => void sell()}>
        <BookOpen className="h-4 w-4" /> {busy ? 'Sotilmoqda…' : `Kitobni sotish${total > 0 ? ` — ${formatMoney(total)}` : ''}`}
      </Button>
    </div>
  )
}
