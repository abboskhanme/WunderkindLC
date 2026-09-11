import type { ReactNode } from 'react'

/**
 * OMMAVIY FORMALAR uchun umumiy ko'rinish bo'laklari — lid formasi (`/forma/{slug}`) va daraja
 * testi (`/test/{slug}`) IKKALASI ham shulardan foydalanadi.
 *
 * <p>TELEFON BIRINCHI: bu sahifalar Instagram/Telegram/Facebook'dagi havoladan ochiladi, ya'ni
 * mijoz deyarli har doim TELEFONDA turadi (kompyuter — istisno). Shu sabab o'lchamlar shu yerda,
 * BITTA joyda saqlanadi: ilgari `inputCls` va `Field` ikkala sahifada ayri-ayri yozilgan edi va
 * birini tuzatib, ikkinchisini unutish oson bo'lardi.</p>
 */

/**
 * Kiritish maydoni (input / select / textarea).
 *
 * ⚠️ Telefonda `text-base` (16px) ATAYIN: iOS Safari shrifti 16px dan KICHIK maydonga bosilganda
 * sahifani o'zi kattalashtiradi (auto-zoom) va foydalanuvchi kattalashgan, gorizontal siljiydigan
 * ko'rinishda qolib ketadi — ariza formasida bu to'g'ridan-to'g'ri yo'qotilgan lid demak.
 * Kengroq ekranda (`sm:`) odatdagi 14px ga qaytadi.
 *
 * ⚠️ `py-3` (≈44px balandlik) — barmoq uchun eng kichik qulay o'lcham; `sm:` da yana ixchamlashadi.
 */
export const inputCls =
  'w-full rounded-xl border border-slate-200 px-3.5 py-3 text-base text-slate-800 outline-none ' +
  'transition-colors focus:border-brand-400 sm:py-2.5 sm:text-sm'

/**
 * Kartochka ichidagi GORIZONTAL padding. Telefonda ataylab torroq: 360px kenglikdagi ekranda
 * sahifaning o'z chekkasi (`px-4`) ustiga kartaning `px-6` si qo'shilib, matnga 280px qolardi.
 */
export const cardPadX = 'px-5 sm:px-6'

/** Asosiy amal tugmasi (yuborish / boshlash) — telefonda to'liq kenglikda va baland. */
export const primaryBtnCls =
  'flex w-full items-center justify-center gap-2 rounded-xl py-3.5 text-sm font-semibold text-white ' +
  'shadow-lg transition-colors disabled:opacity-50 sm:py-3'

/** Maydon yorlig'i. `<label>` butun bo'lakni o'raydi — yorliqqa bosilsa ham maydon fokuslanadi. */
export function Field({ label, children }: { label: string; children: ReactNode }) {
  return (
    <label className="block">
      <span className="mb-1 block text-xs font-medium text-slate-500">{label}</span>
      {children}
    </label>
  )
}
