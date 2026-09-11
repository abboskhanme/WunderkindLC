import { useEffect, useRef, useState } from 'react'
import { Loader2, Save, Upload, X, BookOpen } from 'lucide-react'
import type { Book } from '@/api/services/books'
import { createBook, updateBook, uploadBookCover } from '@/api/services/books'
import { Button } from '@/components/ui/Button'
import { Modal } from '@/components/ui/Modal'
import { Input, Textarea } from '@/components/ui/Input'
import { apiErrorMessage, cn } from '@/lib/utils'

interface Props {
  open: boolean
  /** null — yangi kitob; aks holda tahrirlash */
  initial: Book | null
  onClose: () => void
  onSaved: (book: Book) => void
}

/**
 * Kitob yaratish/tahrirlash. Yaratishda BOSHLANG'ICH QOLDIQ ham kiritiladi (kirim tarixiga
 * "boshlang'ich qoldiq" bo'lib yoziladi). Tahrirlashda qoldiq maydoni KO'RSATILMAYDI — u faqat
 * "Kirim" amali orqali o'zgaradi, shunda ombor tarixi to'g'ri qoladi.
 */
export function BookFormModal({ open, initial, onClose, onSaved }: Props) {
  const [title, setTitle] = useState('')
  const [author, setAuthor] = useState('')
  const [price, setPrice] = useState('')
  const [stock, setStock] = useState('')
  const [description, setDescription] = useState('')
  const [coverUrl, setCoverUrl] = useState('')
  const [isActive, setIsActive] = useState(true)
  const [busy, setBusy] = useState(false)
  const [uploading, setUploading] = useState(false)
  const [error, setError] = useState('')
  const fileRef = useRef<HTMLInputElement | null>(null)

  useEffect(() => {
    if (!open) return
    setTitle(initial?.title ?? '')
    setAuthor(initial?.author ?? '')
    setPrice(initial ? String(initial.price) : '')
    setStock('')
    setDescription(initial?.description ?? '')
    setCoverUrl(initial?.coverUrl ?? '')
    setIsActive(initial?.isActive ?? true)
    setError('')
  }, [open, initial])

  const pickCover = async (file: File) => {
    setUploading(true)
    setError('')
    try {
      setCoverUrl(await uploadBookCover(file))
    } catch (err) {
      setError(apiErrorMessage(err, "Rasmni yuklab bo'lmadi"))
    } finally {
      setUploading(false)
    }
  }

  const submit = async () => {
    if (busy) return
    const name = title.trim()
    if (!name) {
      setError('Kitob nomini kiriting')
      return
    }
    const p = Number(price)
    if (!Number.isFinite(p) || p < 0) {
      setError("Narxni to'g'ri kiriting")
      return
    }
    const initialStock = initial ? 0 : Number(stock || 0)
    if (!Number.isInteger(initialStock) || initialStock < 0) {
      setError("Qoldiqni butun son sifatida kiriting")
      return
    }

    setBusy(true)
    setError('')
    try {
      const payload = {
        title: name,
        author: author.trim(),
        price: p,
        description: description.trim(),
        coverUrl,
        isActive,
        initialStock,
      }
      onSaved(initial ? await updateBook(initial.id, payload) : await createBook(payload))
    } catch (err) {
      setError(apiErrorMessage(err, "Saqlab bo'lmadi"))
    } finally {
      setBusy(false)
    }
  }

  return (
    <Modal
      open={open}
      onClose={onClose}
      title={initial ? 'Kitobni tahrirlash' : 'Yangi kitob'}
      footer={
        <>
          <Button variant="secondary" onClick={onClose} disabled={busy}>
            Bekor qilish
          </Button>
          <Button onClick={submit} disabled={busy || uploading}>
            {busy ? <Loader2 className="h-4 w-4 animate-spin" /> : <Save className="h-4 w-4" />}
            Saqlash
          </Button>
        </>
      }
    >
      <div className="space-y-3">
        <Input label="Kitob nomi" required value={title} onChange={(e) => setTitle(e.target.value)} />
        <Input label="Muallif" value={author} onChange={(e) => setAuthor(e.target.value)} />

        <div className="grid gap-3 sm:grid-cols-2">
          <Input
            label="Narxi (so'm)"
            required
            type="number"
            min={0}
            value={price}
            onChange={(e) => setPrice(e.target.value)}
          />
          {initial ? (
            <div>
              <span className="mb-1 block text-sm font-medium text-slate-600">Joriy qoldiq</span>
              <div className="rounded-lg bg-slate-50 px-3 py-2 text-sm text-slate-600">
                <b className="font-mono text-slate-800">{initial.stock}</b> dona —
                o'zgartirish uchun kartadagi <b>«Kirim»</b> tugmasidan foydalaning.
              </div>
            </div>
          ) : (
            <Input
              label="Boshlang'ich qoldiq (dona)"
              type="number"
              min={0}
              placeholder="0"
              value={stock}
              onChange={(e) => setStock(e.target.value)}
            />
          )}
        </div>

        <Textarea
          label="Tavsif (botda ko'rinadi)"
          rows={3}
          value={description}
          onChange={(e) => setDescription(e.target.value)}
        />

        {/* Muqova */}
        <div>
          <span className="mb-1 block text-sm font-medium text-slate-600">Muqova rasmi</span>
          <div className="flex items-start gap-3">
            <div
              className={cn(
                'flex h-24 w-[72px] shrink-0 items-center justify-center overflow-hidden rounded-lg border border-dashed bg-white',
                coverUrl ? 'border-brand-200' : 'border-slate-200 text-slate-300',
              )}
            >
              {uploading ? (
                <Loader2 className="h-5 w-5 animate-spin text-brand-500" />
              ) : coverUrl ? (
                <img src={coverUrl} alt="" className="h-full w-full object-cover" />
              ) : (
                <BookOpen className="h-5 w-5" />
              )}
            </div>
            <div className="flex gap-2">
              <input
                ref={fileRef}
                type="file"
                accept="image/*"
                className="hidden"
                onChange={(e) => {
                  const f = e.target.files?.[0]
                  if (f) pickCover(f)
                  if (fileRef.current) fileRef.current.value = ''
                }}
              />
              <Button type="button" variant="secondary" onClick={() => fileRef.current?.click()} disabled={uploading}>
                <Upload className="h-4 w-4" /> {coverUrl ? 'Yangilash' : 'Rasm yuklash'}
              </Button>
              {coverUrl && (
                <Button type="button" variant="danger" onClick={() => setCoverUrl('')}>
                  <X className="h-4 w-4" /> O'chirish
                </Button>
              )}
            </div>
          </div>
        </div>

        <label className="flex cursor-pointer items-center gap-2 text-sm text-slate-700">
          <input type="checkbox" checked={isActive} onChange={(e) => setIsActive(e.target.checked)} />
          Sotuvda (botda ko'rinadi)
        </label>

        {error && <p className="text-sm text-red-500">{error}</p>}
      </div>
    </Modal>
  )
}
