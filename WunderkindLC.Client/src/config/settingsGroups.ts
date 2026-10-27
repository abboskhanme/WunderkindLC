/**
 * SOZLAMALAR KATALOGI — edutizim tuzilishida (foydalanuvchi talabi, 2026-09-27).
 *
 * YAGONA MANBA: yon menyudagi "Sozlamalar" guruhi (`navigation.ts`) ham, har bir guruh sahifasi
 * (`SettingsGroupPage`: chapda bo'limlar kartasi, o'ngda kontent) ham SHU ro'yxatdan quriladi.
 *
 * edutizim: Umumiy sozlamalar · Moliya · O'quv · Sotuv va marketing · Ilova sozlamalari ·
 * Gamifikatsiya. Bizda FAQAT BOR sozlamalar joylashtirilgan; edutizimda bo'lib bizda yo'q
 * bo'limlar (Obuna, Bayram kunlari, Ommaviy oferta, Gamifikatsiya, Yon panel modullari...)
 * ATAYIN ko'rsatilmaydi — soxta "bo'sh" sahifa qilinmaydi.
 *
 * ⚠️ Komponent bu yerga QO'SHILMAYDI (react-refresh): panel faqat KALIT bilan ko'rsatiladi,
 * kalit → komponent xaritasi `SettingsGroupPage` da.
 */
import {
  IconBrandInstagram,
  IconBellRinging,
  IconBuildingCommunity,
  IconChecklist,
  IconClockHour4,
  IconFileText,
  IconGift,
  IconLayoutGrid,
  IconListDetails,
  IconMessages,
  IconMicrophone,
  IconPhoneCall,
  IconMoneybag,
  IconPlugConnected,
  IconReceipt,
  IconSchool,
  IconShieldCheck,
  IconSparkles,
  IconTrophy,
  IconUserCheck,
  IconUserCog,
  IconWorld,
  type Icon,
} from '@tabler/icons-react'

export type SettingsGroupKey = 'umumiy' | 'moliya' | 'oquv' | 'sotuv' | 'ilova'

/** Bo'lim ichida chiziladigan TAYYOR sozlama komponenti (kalit → komponent `SettingsGroupPage` da). */
export type SettingsPanelKey = 'check' | 'school' | 'account' | 'reasons' | 'channels' | 'azure' | 'gemini' | 'mcp' | 'servis'

/** Qatordan ochiladigan oyna (mavjud modallar). */
export type SettingsModalKey =
  | 'journal-edit'
  | 'journal-payment'
  | 'journal-salary'
  | 'retention'
  // Davomat intizomi va «kelmadi» xabarlari (CRM Phase 3, SPEC P3.2) — server `AttendanceSettingsController` (`classes`).
  | 'attendance-discipline'
  | 'absent-notice'
  // Reyting sozlamalari (CRM Phase 4)
  | 'rating'

/** Ruxsat: `perm` — shu kalit (+ `action`), yo'q bo'lsa hamma ko'radi. */
interface Gated {
  perm?: string
  /** `view` (standart) — ko'rish; `edit` — faqat o'zgartira oladiganga (oyna faqat saqlash uchun). */
  action?: 'view' | 'edit'
  /** FAQAT superadmin (ruxsat kaliti bilan ifodalab bo'lmaydi: `can()` admin uchun ham `true`).
   *  Server tomonda ham hard rol (`[Authorize(Roles = SuperAdmin)]`). `perm` bilan birga ishlatilmaydi. */
  superadminOnly?: boolean
}

/** edutizim "Funksionallik" uslubidagi kulrang qator: rangli ikonka + sarlavha + izoh + strelka. */
export interface SettingsRow extends Gated {
  key: string
  title: string
  sub: string
  icon: Icon
  /** Ikonka plitkasining rangi (edutizim ranglari). */
  color: string
  open: { modal: SettingsModalKey } | { to: string }
}

/** Chap kartadagi bitta band. */
export interface SettingsItem extends Gated {
  /** Manzildagi `?bolim=` qiymati (`?tab=` emas: eski xatcho'plarda `tab` "Xabar kanallari"ning
   *  ichki tablari uchun ishlatilgan). */
  key: string
  label: string
  icon: Icon
  /** `rows` — qatorlar ro'yxati; `panel` — tayyor komponent; `link` — alohida sahifaga o'tish. */
  kind: 'rows' | 'panel' | 'link'
  rows?: SettingsRow[]
  panel?: SettingsPanelKey
  to?: string
}

export interface SettingsGroup {
  key: SettingsGroupKey
  label: string
  to: string
  items: SettingsItem[]
}

/* edutizim ranglari (Umumiy sozlamalar → Funksionallik skrinshotidan). */
const GREEN = '#22c55e'
const BLUE = '#3d68ff'
const VIOLET = '#8b5cf6'
const ORANGE = '#f59e0b'
const PINK = '#ec4899'
const CYAN = '#06b6d4'

/** Jurnal siyosati (`JournalPolicyModal`) — server ruxsati `classes` (PUT journal/policy). */
const JOURNAL = { perm: 'classes', action: 'edit' as const }

export const SETTINGS_GROUPS: SettingsGroup[] = [
  {
    key: 'umumiy',
    label: 'Umumiy sozlamalar',
    to: '/admin/sozlamalar/umumiy',
    items: [
      {
        key: 'funksionallik',
        label: 'Funksionallik',
        icon: IconLayoutGrid,
        kind: 'rows',
        rows: [
          {
            key: 'moliya', title: 'Moliya sozlamalari', icon: IconMoneybag, color: GREEN,
            sub: "To'lov qilmagan o'quvchini o'qituvchi jurnalida yashirish",
            open: { modal: 'journal-payment' }, ...JOURNAL,
          },
          {
            key: 'davomat', title: 'Davomat sozlamalari', icon: IconClockHour4, color: BLUE,
            sub: "Baho va davomat kiritish muddati, o'tilgan dars qoidasi",
            open: { modal: 'journal-edit' }, ...JOURNAL,
          },
          {
            key: 'oqituvchi', title: "O'qituvchi holatlari", icon: IconUserCheck, color: VIOLET,
            sub: "Maoshning jurnal bo'yicha hisoblanishi va to'ldirish muhlati",
            open: { modal: 'journal-salary' }, ...JOURNAL,
          },
          {
            key: 'xonalar', title: 'Xonalar holati', icon: IconSchool, color: ORANGE,
            sub: "Xonalar ro'yxati, sig'imi va bandligi",
            open: { to: '/admin/rooms' }, perm: 'classes.rooms',
          },
          {
            key: 'topshiriq', title: 'Topshiriq boshqaruvi', icon: IconFileText, color: PINK,
            sub: 'Baholash mezonlari va ularning ballari',
            open: { to: '/admin/grading' }, perm: 'schedule.grading',
          },
        ],
      },
      { key: 'chek', label: 'Chek', icon: IconReceipt, kind: 'panel', panel: 'check', perm: 'settings.check' },
      { key: 'markaz', label: "Markaz ma'lumotlari", icon: IconBuildingCommunity, kind: 'panel', panel: 'school', perm: 'settings.school' },
      { key: 'foydalanuvchi', label: 'Foydalanuvchi sozlamalari', icon: IconUserCog, kind: 'panel', panel: 'account' },
      // AI ulanish (read-only MCP): holat, manzil, ulangan ilovalar, so'rovlar jurnali (docs/SPEC-mcp.md).
      { key: 'ai', label: 'AI ulanishlar (MCP)', icon: IconPlugConnected, kind: 'panel', panel: 'mcp', superadminOnly: true },
    ],
  },
  {
    key: 'moliya',
    label: 'Moliya',
    to: '/admin/sozlamalar/moliya',
    items: [
      {
        key: 'moliya',
        label: 'Moliya sozlamalari',
        icon: IconMoneybag,
        kind: 'rows',
        rows: [
          {
            key: 'tolov', title: "To'lov nazorati", icon: IconMoneybag, color: GREEN,
            sub: "To'lov qilmagan o'quvchini o'qituvchi jurnalida yashirish",
            open: { modal: 'journal-payment' }, ...JOURNAL,
          },
          {
            key: 'bonus', title: 'Ushlab turish bonusi', icon: IconGift, color: CYAN,
            sub: "O'quvchini ushlab turgan o'qituvchiga beriladigan bonus qoidalari",
            open: { modal: 'retention' }, perm: 'finance.bonus', action: 'edit',
          },
        ],
      },
    ],
  },
  {
    key: 'oquv',
    label: "O'quv",
    to: '/admin/sozlamalar/oquv',
    items: [
      {
        // O'quvchilar reytingi (CRM 4-bosqich) — O'quv bandlarining BIRINCHISI (SPEC §8: birlashtirishda
        // boshqa bosqich qo'shadigan bandlar bilan to'qnashmasin).
        key: 'reyting',
        label: 'Reyting',
        icon: IconTrophy,
        kind: 'rows',
        rows: [
          {
            key: 'reyting', title: 'Reyting sozlamalari', icon: IconTrophy, color: VIOLET,
            sub: 'Testlar va ball ulushi, oylik TOP/BOTTOM soni va qamrovi',
            open: { modal: 'rating' }, perm: 'classes.rating', action: 'edit',
          },
        ],
      },
      { key: 'sabablar', label: 'Sabablar', icon: IconListDetails, kind: 'panel', panel: 'reasons', perm: 'settings.reasons' },
      // «Bog'lanish kerak» (servis) avtomatikasi — kelmaganlar va qarzdorlar (contacts.md §9, §9.5). Ruxsat — navbatniki.
      { key: 'servis', label: 'Servis', icon: IconPhoneCall, kind: 'panel', panel: 'servis', perm: 'contacts' },
      {
        key: 'jurnal',
        label: 'Jurnal va davomat',
        icon: IconChecklist,
        kind: 'rows',
        rows: [
          {
            key: 'davomat', title: 'Davomat sozlamalari', icon: IconClockHour4, color: BLUE,
            sub: "Baho va davomat kiritish muddati, o'tilgan dars qoidasi",
            open: { modal: 'journal-edit' }, ...JOURNAL,
          },
          {
            key: 'davomat-intizomi', title: 'Davomat intizomi', icon: IconShieldCheck, color: ORANGE,
            sub: 'Eslatma, jarima va administratorga xabar',
            open: { modal: 'attendance-discipline' }, ...JOURNAL,
          },
          {
            key: 'davomat-xabarlari', title: 'Davomat xabarlari', icon: IconBellRinging, color: CYAN,
            sub: 'Ota-onaga "kelmadi" xabari vaqti va tasdiqlash',
            open: { modal: 'absent-notice' }, ...JOURNAL,
          },
          {
            key: 'topshiriq', title: 'Baholash mezonlari', icon: IconFileText, color: PINK,
            sub: "Guruhlarda o'quvchilarni baholash mezonlari",
            open: { to: '/admin/grading' }, perm: 'schedule.grading',
          },
        ],
      },
    ],
  },
  {
    key: 'sotuv',
    label: 'Sotuv va marketing',
    to: '/admin/sozlamalar/sotuv',
    items: [
      { key: 'kanallar', label: 'Xabar kanallari', icon: IconMessages, kind: 'panel', panel: 'channels', perm: 'settings.channels' },
      { key: 'instagram', label: 'Instagram', icon: IconBrandInstagram, kind: 'link', to: '/admin/marketing/settings', perm: 'marketing.settings' },
      { key: 'landing', label: 'Landing sahifa', icon: IconWorld, kind: 'link', to: '/admin/landing', perm: 'settings.landing' },
    ],
  },
  {
    key: 'ilova',
    label: 'Ilova sozlamalari',
    to: '/admin/sozlamalar/ilova',
    items: [
      { key: 'azure', label: 'Speaking (Azure)', icon: IconMicrophone, kind: 'panel', panel: 'azure', perm: 'settings.azure-speech' },
      { key: 'gemini', label: 'AI tahlil (Gemini)', icon: IconSparkles, kind: 'panel', panel: 'gemini', perm: 'settings.gemini' },
    ],
  },
]

/** Ruxsat tekshiruvchi: `(perm, action) => boolean` (odatda `usePerm().can`). */
export type CanFn = (perm: string, action: 'view' | 'edit') => boolean

const allowed = (x: Gated, can: CanFn, isSuperAdmin: boolean) =>
  x.superadminOnly ? isSuperAdmin : !x.perm || can(x.perm, x.action ?? 'view')

/** Foydalanuvchiga ko'rinadigan guruhlar: band va qatorlar ruxsat bo'yicha filtrlanadi,
 *  bo'sh qolgan `rows` bandi va bandsiz guruh butunlay tushib qoladi. */
export function visibleSettingsGroups(can: CanFn, isSuperAdmin = false): SettingsGroup[] {
  return SETTINGS_GROUPS.map((g) => ({
    ...g,
    items: g.items
      .filter((i) => allowed(i, can, isSuperAdmin))
      .map((i) => (i.rows ? { ...i, rows: i.rows.filter((r) => allowed(r, can, isSuperAdmin)) } : i))
      .filter((i) => i.kind !== 'rows' || (i.rows?.length ?? 0) > 0),
  })).filter((g) => g.items.length > 0)
}

/**
 * Menyuda guruhni ko'rsatish uchun kalitlar: guruhdagi BIRORTA band yoki qator ruxsati yetarli.
 * Ruxsatsiz band (Foydalanuvchi sozlamalari) bo'lgan guruh uchun `null` — hamma ko'radi.
 */
export function groupPermAny(g: SettingsGroup): string[] | null {
  const perms: string[] = []
  for (const i of g.items) {
    // Faqat superadmin bandi menyu darvozasiga KIRMAYDI (kalitsiz — u guruhni hammaga ochib yubormasin).
    if (i.superadminOnly) continue
    if (!i.perm && i.kind !== 'rows') return null
    if (i.perm) perms.push(i.perm)
    for (const r of i.rows ?? []) if (r.perm) perms.push(r.perm)
  }
  return [...new Set(perms)]
}

/** Eski `/admin/settings/:section` manzillari → yangi guruh sahifasi (xatcho'plar ishlayversin).
 *  ⚠️ `apk` (mobil ilova APK) bo'limi 2026-10 da olib tashlandi — u `SettingsPage` da guruh
 *  sahifasiga (bo'limsiz) yo'naltiriladi, shuning uchun bu ro'yxatda yo'q. */
export const LEGACY_SETTINGS_REDIRECTS: Record<string, string> = {
  school: '/admin/sozlamalar/umumiy?bolim=markaz',
  check: '/admin/sozlamalar/umumiy?bolim=chek',
  reasons: '/admin/sozlamalar/oquv?bolim=sabablar',
  channels: '/admin/sozlamalar/sotuv?bolim=kanallar',
  'azure-speech': '/admin/sozlamalar/ilova?bolim=azure',
  gemini: '/admin/sozlamalar/ilova?bolim=gemini',
}
