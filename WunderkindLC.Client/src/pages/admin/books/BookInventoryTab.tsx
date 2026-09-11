import { useCallback, useEffect, useState } from 'react'
import {
  Plus, Pencil, Trash2, BookOpen, PackagePlus, History, FileDown, Loader2, AlertTriangle, EyeOff,
} from 'lucide-react'
import type { Book, BookStockMove } from '@/api/services/books'
import {
  addBookStock, deleteBook, exportBookStockMoves, getBookStockMoves, getBooks,
} from '@/api/services/books'
import { Card } from '@/components/ui/Card'
import { Button } from '@/components/ui/Button'
import { Loader } from '@/components/ui/Loader'
import { Modal } from '@/components/ui/Modal'
import { Input, Select, Textarea } from '@/components/ui/Input'
import { apiErrorMessage, cn, formatMoney } from '@/lib/utils'
import { BookFormModal } from './BookFormModal'
import { stockReasonLabel } from './bookLabels'

interface Props {
  canCreate: boolean
  canEdit: boolean
  canDelete: boolean
}

/** Qoldiq shu qiymatdan kam bo'lsa sariq/qizil ogohlantirish (backend `LowStock` bilan bir xil). */
const LOW_STOCK = 3

/**
 * OMBOR: kitoblar ro'yxati (narx + qoldiq), kitob yaratish/tahrirlash, qoldiqqa kirim qilish va
 * ombor harakatlari tarixi (kitob qachon va qancha miqdorda kirim qilingani).
 */
export function BookInventoryTab({ canCreate, canEdit, canDelete }: Props) {
  const [books, setBooks] = useState<Book[]>([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState('')
  const [formOpen, setFormOpen] = useState(false)
  const [editing, setEditing] = useState<Book | null>(null)
  const [stockFor, setStockFor] = useState<Book | null>(null)
  const [deleting, setDeleting] = useState<Book | null>(null)
  const [busy, setBusy] = useState(false)
  const [historyOpen, setHistoryOpen] = useState(false)

  const load = useCallback(() => {
    setLoading(true)
    getBooks()
      .then(setBooks)
      .catch((err) => setError(apiErrorMessage(err, "Kitoblarni yuklab bo'lmadi")))
      .finally(() => setLoading(false))
  }, [])

  useEffect(load, [load])

  const upsert = (b: Book) =>
    setBooks((prev) => (prev.some((x) => x.id === b.id) ? prev.map((x) => (x.id === b.id ? b : x)) : [...prev, b]))

  const confirmDelete = async () => {
    if (!deleting || busy) return
    setBusy(true)
    setError('')
    try {
      await deleteBook(deleting.id)
      setBooks((prev) => prev.filter((x) => x.id !== deleting.id))
      setDeleting(null)
    } catch (err) {
      setError(apiErrorMessage(err, "O'chirib bo'lmadi"))
    } finally {
      setBusy(false)
    }
  }

  const totalStock = books.reduce((s, b) => s + b.stock, 0)
  const totalValue = books.reduce((s, b) => s + b.stock * b.price, 0)

  return (
    <div className="space-y-4">
      <Card tight>
        <div className="flex flex-wrap items-center gap-3 p-4">
          <div className="text-sm text-slate-500">
            Jami <b className="text-slate-800">{books.length}</b> nomdagi kitob ·{' '}
            qoldiq <b className="text-slate-800">{totalStock}</b> dona ·{' '}
            ombor qiymati <b className="font-mono text-slate-800">{formatMoney(totalValue)}</b> so'm
          </div>
          <div className="ml-auto flex flex-wrap gap-2">
            <Button variant="secondary" onClick={() => setHistoryOpen(true)}>
              <History className="h-4 w-4" /> Kirim tarixi
            </Button>
            {canCreate && (
              <Button
                onClick={() => {
                  setEditing(null)
                  setFormOpen(true)
                }}
              >
                <Plus className="h-4 w-4" /> Yangi kitob
              </Button>
            )}
          </div>
        </div>
      </Card>

      {error && (
        <div className="flex items-center gap-2 rounded-lg bg-red-50 px-4 py-3 text-sm text-red-700">
          <AlertTriangle className="h-4 w-4 shrink-0" /> {error}
        </div>
      )}

      {loading ? (
        <Card>
          <Loader label="Yuklanmoqda..." />
        </Card>
      ) : books.length === 0 ? (
        <Card>
          <div className="state">
            <div className="state-icon">
              <BookOpen className="h-5 w-5" />
            </div>
            <h4>Kitoblar yo'q</h4>
            <p>"Yangi kitob" tugmasi orqali birinchi kitobni qo'shing — narxi va qoldig'i bilan.</p>
          </div>
        </Card>
      ) : (
        <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-3 xl:grid-cols-4">
          {books.map((b) => (
            <div
              key={b.id}
              className={cn(
                'flex flex-col rounded-2xl border bg-white p-4 shadow-[var(--shadow-1)] transition-shadow hover:shadow-[var(--shadow-pop)]',
                b.isActive ? 'border-slate-200' : 'border-dashed border-slate-300 opacity-75',
              )}
            >
              <div className="flex items-start gap-3">
                <div className="flex h-16 w-12 flex-shrink-0 items-center justify-center overflow-hidden rounded-lg bg-brand-50 text-brand-600">
                  {b.coverUrl ? (
                    <img src={b.coverUrl} alt="" className="h-full w-full object-cover" />
                  ) : (
                    <BookOpen className="h-5 w-5" />
                  )}
                </div>
                <div className="min-w-0 flex-1">
                  <p className="truncate text-[15px] font-bold tracking-tight text-slate-800">{b.title}</p>
                  {b.author && <p className="truncate text-xs text-slate-400">{b.author}</p>}
                  <p className="mt-1 text-sm">
                    <span className="font-mono font-semibold text-slate-700">{formatMoney(b.price)}</span>{' '}
                    <span className="text-slate-400">so'm</span>
                  </p>
                </div>
              </div>

              <div className="mt-3 grid grid-cols-3 gap-2 text-center">
                <Metric
                  label="Qoldiq"
                  value={b.stock}
                  cls={b.stock === 0 ? 'text-red-600' : b.stock <= LOW_STOCK ? 'text-amber-600' : 'text-slate-800'}
                />
                <Metric label="Sotilgan" value={b.soldQty} />
                <Metric label="Kutilmoqda" value={b.pendingQty} cls={b.pendingQty > 0 ? 'text-amber-600' : undefined} />
              </div>

              {!b.isActive && (
                <p className="mt-2 inline-flex items-center gap-1 text-xs font-medium text-slate-400">
                  <EyeOff className="h-3.5 w-3.5" /> Botda ko'rinmaydi
                </p>
              )}

              <div className="mt-3 flex items-center gap-2 border-t border-slate-100 pt-3">
                {canEdit && (
                  <button
                    type="button"
                    onClick={() => setStockFor(b)}
                    className="inline-flex flex-1 items-center justify-center gap-1.5 rounded-lg bg-brand-50 px-3 py-2 text-sm font-medium text-brand-700 transition-colors hover:bg-brand-100"
                  >
                    <PackagePlus className="h-4 w-4" /> Kirim
                  </button>
                )}
                {canEdit && (
                  <IconBtn
                    icon={Pencil}
                    title="Tahrirlash"
                    onClick={() => {
                      setEditing(b)
                      setFormOpen(true)
                    }}
                  />
                )}
                {canDelete && (
                  <IconBtn icon={Trash2} title="O'chirish" danger onClick={() => setDeleting(b)} />
                )}
              </div>
            </div>
          ))}
        </div>
      )}

      <BookFormModal
        open={formOpen}
        initial={editing}
        onClose={() => {
          setFormOpen(false)
          setEditing(null)
        }}
        onSaved={(b) => {
          upsert(b)
          setFormOpen(false)
          setEditing(null)
        }}
      />

      <StockModal book={stockFor} onClose={() => setStockFor(null)} onSaved={upsert} />

      <StockHistoryModal open={historyOpen} books={books} onClose={() => setHistoryOpen(false)} />

      <Modal
        open={!!deleting}
        onClose={() => setDeleting(null)}
        size="sm"
        title="Kitobni o'chirish"
        footer={
          <>
            <Button variant="secondary" onClick={() => setDeleting(null)} disabled={busy}>
              Bekor qilish
            </Button>
            <Button variant="danger" onClick={confirmDelete} disabled={busy}>
              {busy ? <Loader2 className="h-4 w-4 animate-spin" /> : <Trash2 className="h-4 w-4" />}
              O'chirish
            </Button>
          </>
        }
      >
        <div className="flex items-start gap-3">
          <div className="flex h-10 w-10 flex-shrink-0 items-center justify-center rounded-full bg-red-50 text-red-600">
            <AlertTriangle className="h-5 w-5" />
          </div>
          <p className="text-sm leading-relaxed text-slate-600">
            <b>"{deleting?.title}"</b> kitobi va uning ombor tarixi o'chiriladi. Buyurtma tarixi bor
            kitobni o'chirib bo'lmaydi — bunday holatda uni tahrirlab "Sotuvda" belgisini o'chiring.
          </p>
        </div>
      </Modal>
    </div>
  )
}

// ============================ Qoldiqqa kirim ============================

interface StockModalProps {
  book: Book | null
  onClose: () => void
  onSaved: (b: Book) => void
}

/** Omborga kirim (+) yoki qo'lda ayirish (−). Har amal "Kirim tarixi"ga yoziladi. */
function StockModal({ book, onClose, onSaved }: StockModalProps) {
  const [qty, setQty] = useState('')
  const [note, setNote] = useState('')
  const [mode, setMode] = useState<'in' | 'out'>('in')
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState('')

  useEffect(() => {
    setQty('')
    setNote('')
    setMode('in')
    setError('')
  }, [book])

  const submit = async () => {
    if (!book || busy) return
    const n = Number(qty)
    if (!Number.isInteger(n) || n <= 0) {
      setError("Miqdorni butun son sifatida kiriting (0 dan katta)")
      return
    }
    setBusy(true)
    setError('')
    try {
      onSaved(await addBookStock(book.id, mode === 'in' ? n : -n, note.trim()))
      onClose()
    } catch (err) {
      setError(apiErrorMessage(err, "Saqlab bo'lmadi"))
    } finally {
      setBusy(false)
    }
  }

  return (
    <Modal
      open={!!book}
      onClose={onClose}
      size="sm"
      title={`"${book?.title}" — ombor`}
      footer={
        <>
          <Button variant="secondary" onClick={onClose} disabled={busy}>
            Bekor qilish
          </Button>
          <Button onClick={submit} disabled={busy}>
            {busy ? <Loader2 className="h-4 w-4 animate-spin" /> : <PackagePlus className="h-4 w-4" />}
            Saqlash
          </Button>
        </>
      }
    >
      <div className="space-y-3">
        <div className="rounded-lg bg-slate-50 px-3 py-2 text-sm text-slate-600">
          Joriy qoldiq: <b className="font-mono text-slate-800">{book?.stock ?? 0}</b> dona
        </div>
        <Select label="Amal" value={mode} onChange={(e) => setMode(e.target.value as 'in' | 'out')}>
          <option value="in">Kirim (+) — yangi kitob keldi</option>
          <option value="out">Ayirish (−) — yo'qolgan/buzilgan</option>
        </Select>
        <Input
          label="Miqdor (dona)"
          required
          type="number"
          min={1}
          value={qty}
          onChange={(e) => setQty(e.target.value)}
        />
        <Textarea
          label="Izoh"
          rows={2}
          placeholder="Masalan: Nashriyotdan 20 dona"
          value={note}
          onChange={(e) => setNote(e.target.value)}
        />
        {error && <p className="text-sm text-red-500">{error}</p>}
      </div>
    </Modal>
  )
}

// ============================ Ombor harakatlari tarixi ============================

interface HistoryProps {
  open: boolean
  books: Book[]
  onClose: () => void
}

/** Kitoblar qachon va qancha miqdorda omborga kirim qilingani (+ sotuv/korreksiya harakatlari). */
function StockHistoryModal({ open, books, onClose }: HistoryProps) {
  const [moves, setMoves] = useState<BookStockMove[]>([])
  const [loading, setLoading] = useState(true)
  const [onlyIn, setOnlyIn] = useState(true)
  const [bookId, setBookId] = useState('')
  const [from, setFrom] = useState('')
  const [to, setTo] = useState('')

  useEffect(() => {
    if (!open) return
    setLoading(true)
    getBookStockMoves({ onlyIn, bookId, from, to })
      .then(setMoves)
      .catch(() => setMoves([]))
      .finally(() => setLoading(false))
  }, [open, onlyIn, bookId, from, to])

  return (
    <Modal
      open={open}
      onClose={onClose}
      size="xl"
      title="Ombor harakatlari tarixi"
      footer={
        <>
          <Button variant="secondary" onClick={() => exportBookStockMoves({ onlyIn, bookId, from, to })}>
            <FileDown className="h-4 w-4" /> Excel
          </Button>
          <Button onClick={onClose}>Yopish</Button>
        </>
      }
    >
      <div className="space-y-3">
        <div className="flex flex-wrap items-end gap-3">
          <div className="tabs">
            <button type="button" className={cn('tab', onlyIn && 'active')} onClick={() => setOnlyIn(true)}>
              Faqat kirim
            </button>
            <button type="button" className={cn('tab', !onlyIn && 'active')} onClick={() => setOnlyIn(false)}>
              Barcha harakat
            </button>
          </div>
          <Select label="Kitob" className="w-auto" value={bookId} onChange={(e) => setBookId(e.target.value)}>
            <option value="">Barcha kitoblar</option>
            {books.map((b) => (
              <option key={b.id} value={b.id}>
                {b.title}
              </option>
            ))}
          </Select>
          <Input label="Sanadan" type="date" className="w-auto" value={from} onChange={(e) => setFrom(e.target.value)} />
          <Input label="Sanagacha" type="date" className="w-auto" value={to} onChange={(e) => setTo(e.target.value)} />
        </div>

        {loading ? (
          <Loader label="Yuklanmoqda..." />
        ) : moves.length === 0 ? (
          <p className="py-6 text-center text-sm text-slate-400">Bu filtr bo'yicha harakat yo'q.</p>
        ) : (
          <div className="max-h-[55vh] overflow-auto">
            <table className="table">
              <thead>
                <tr>
                  <th>Sana</th>
                  <th>Kitob</th>
                  <th className="text-right">Miqdor</th>
                  <th>Turi</th>
                  <th>Izoh</th>
                  <th className="text-right">Qoldiq (keyin)</th>
                  <th>Kim</th>
                </tr>
              </thead>
              <tbody>
                {moves.map((m) => (
                  <tr key={m.id}>
                    <td className="whitespace-nowrap text-slate-500">
                      {m.createdAt.slice(0, 10)}
                      <span className="ml-1 text-xs text-slate-400">{m.createdAt.slice(11, 16)}</span>
                    </td>
                    <td className="text-slate-700">{m.bookTitle}</td>
                    <td
                      className={cn(
                        'text-right font-mono font-semibold',
                        m.qty > 0 ? 'text-emerald-600' : 'text-red-600',
                      )}
                    >
                      {m.qty > 0 ? `+${m.qty}` : m.qty}
                    </td>
                    <td className="text-slate-500">{stockReasonLabel(m.reason)}</td>
                    <td className="max-w-[220px] truncate text-slate-500">{m.note}</td>
                    <td className="text-right font-mono text-slate-700">{m.stockAfter}</td>
                    <td className="text-slate-400">{m.createdBy}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </div>
    </Modal>
  )
}

// ============================ Kichik yordamchilar ============================

function Metric({ label, value, cls }: { label: string; value: number; cls?: string }) {
  return (
    <div className="rounded-lg bg-slate-50 py-1.5">
      <div className={cn('font-mono text-base font-semibold text-slate-800', cls)}>{value}</div>
      <div className="text-[11px] uppercase tracking-wide text-slate-400">{label}</div>
    </div>
  )
}

interface IconBtnProps {
  icon: typeof Pencil
  title: string
  onClick: () => void
  danger?: boolean
}

function IconBtn({ icon: Icon, title, onClick, danger }: IconBtnProps) {
  return (
    <button
      type="button"
      title={title}
      onClick={onClick}
      className={cn(
        'rounded-lg p-1.5 transition-colors',
        danger
          ? 'text-slate-400 hover:bg-red-50 hover:text-red-600'
          : 'text-slate-400 hover:bg-slate-100 hover:text-slate-700',
      )}
    >
      <Icon className="h-4 w-4" />
    </button>
  )
}
