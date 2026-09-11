import { PhotoDialog } from '@/components/media/PhotoDialog'

/**
 * O'QUVCHI RASMI oynasi — umumiy {@link PhotoDialog} ustidagi yupqa o'ram.
 *
 * <p>Butun mantiq (kamera, ko'zgu, kvadrat qirqish, yuklash) umumiy komponentda — o'qituvchi
 * rasmi ham AYNAN shu oynani ishlatadi. Bu yerda faqat o'quvchiga xos sarlavha va izoh.</p>
 */
export function StudentPhotoDialog(props: {
  open: boolean
  currentUrl: string | null
  /** true — ochilganda darhol kamera yoqiladi (rasm hali yo'q holat). */
  startWithCamera?: boolean
  onClose: () => void
  /** Yangi rasm manzili; `null` — rasm o'chirildi. */
  onSaved: (url: string | null) => void | Promise<void>
}) {
  return (
    <PhotoDialog
      {...props}
      title="O'quvchi rasmi"
      hint="Doira ichidagi qism o'quvchi profilida dumaloq avatar bo'lib chiqadi."
    />
  )
}
