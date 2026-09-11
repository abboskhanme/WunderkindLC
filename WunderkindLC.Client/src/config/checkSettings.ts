/**
 * To'lov cheki (termal kvitansiya) sozlamalari — frontend modeli.
 * JSON satr sifatida CenterMeta.CheckSettings'da saqlanadi (backend faqat saqlaydi/qaytaradi).
 */

/** Chekda ko'rsatiladigan maydonlar (yoq/o'chir). "Jami" doim ko'rinadi — sozlanmaydi. */
export interface CheckFieldFlags {
  receiptNo: boolean
  datetime: boolean
  student: boolean
  teacher: boolean
  responsible: boolean
  group: boolean
  method: boolean
  comment: boolean
}

/** Bitta chek turi uchun maydonlar + footer (to'lov yoki sinov). */
export interface CheckVariant {
  fields: CheckFieldFlags
  /** Pastki izoh (footer) */
  footerText: string
}

export interface CheckSettings {
  /** Sarlavhada markaz logotipi (ikkala chekka umumiy) */
  showLogo: boolean
  /** Sarlavhada markaz nomi (umumiy) */
  showName: boolean
  /** Pastda markaz telefoni + manzili (umumiy) */
  showContact: boolean
  /** QR kod ko'rsatish (umumiy) */
  showQr: boolean
  /** QR ichidagi matn/havola (bo'sh bo'lsa markaz telefoni) */
  qrText: string
  /** TO'LOV cheki: maydonlar (receiptNo..comment) + footer */
  fields: CheckFieldFlags
  footerText: string
  /** SINOV DARSI cheki (lid): o'z maydonlari + footeri (to'lov/izoh tegishli emas) */
  trial: CheckVariant
}

export const defaultCheckSettings: CheckSettings = {
  showLogo: true,
  showName: true,
  showContact: true,
  showQr: false,
  qrText: '',
  fields: {
    receiptNo: true,
    datetime: true,
    student: true,
    teacher: true,
    responsible: true,
    group: true,
    method: true,
    comment: true,
  },
  footerText: 'Tashrifingiz uchun rahmat!',
  trial: {
    fields: {
      receiptNo: true,
      datetime: true,
      student: true,
      teacher: true,
      responsible: true,
      group: true,
      method: false,
      comment: false,
    },
    footerText: 'Sinov darsiga xush kelibsiz!',
  },
}

/** JSON satrni xavfsiz CheckSettings'ga aylantiradi (bo'sh/buzilgan bo'lsa — standart). */
export function parseCheckSettings(json: string | null | undefined): CheckSettings {
  if (!json) return defaultCheckSettings
  try {
    const p = JSON.parse(json) as Partial<CheckSettings>
    return {
      ...defaultCheckSettings,
      ...p,
      fields: { ...defaultCheckSettings.fields, ...(p.fields ?? {}) },
      trial: {
        fields: { ...defaultCheckSettings.trial.fields, ...(p.trial?.fields ?? {}) },
        footerText: p.trial?.footerText ?? defaultCheckSettings.trial.footerText,
      },
    }
  } catch {
    return defaultCheckSettings
  }
}

/**
 * Chek chizish uchun "amaldagi" sozlama — sinov cheki bo'lsa, sinov maydonlari+footeri
 * (trial) asosiy maydonlar o'rniga qo'yiladi (sarlavha/aloqa/QR umumiy qoladi).
 */
export function resolveCheckSettings(s: CheckSettings, isTrial: boolean): CheckSettings {
  if (!isTrial) return s
  return { ...s, fields: s.trial.fields, footerText: s.trial.footerText }
}

/** Maydon yorliqlari (sozlamalar ro'yxati uchun). */
export const checkFieldLabels: { key: keyof CheckFieldFlags; label: string }[] = [
  { key: 'receiptNo', label: 'Id (chek raqami)' },
  { key: 'datetime', label: 'Vaqt' },
  { key: 'student', label: "O'quvchi" },
  { key: 'teacher', label: "O'qituvchi" },
  { key: 'responsible', label: "Mas'ul" },
  { key: 'group', label: 'Guruh' },
  { key: 'method', label: "To'lov turi" },
  { key: 'comment', label: 'Izoh' },
]

/** Sinov darsi chekida ko'rsatish mumkin bo'lgan maydonlar (to'lov turi/izoh tegishli emas). */
export const checkTrialFieldKeys: (keyof CheckFieldFlags)[] = [
  'receiptNo',
  'datetime',
  'student',
  'teacher',
  'responsible',
  'group',
]
