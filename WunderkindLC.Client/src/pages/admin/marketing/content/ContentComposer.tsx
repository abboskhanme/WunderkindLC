import { useCallback, useEffect, useMemo, useRef, useState } from 'react'
import { useNavigate, useParams, useSearchParams } from 'react-router-dom'
import { usePerm } from '@/lib/permissions'
import { apiErrorMessage } from '@/lib/utils'
import {
  countHashtags, countMentions, createIgPost, defaultKind, emptyMedia, emptyOptions,
  generateIgCaption, getIgCaptionMeta, getIgPost, isEditable, isHttpsUrl, isJpegUrl, isVideoUrl,
  publishIgPost, updateIgPost, uploadIgMedia,
  IG_LIMITS, IG_POST_TYPES,
  type IgCaptionMeta, type IgMediaItem, type IgPost, type IgPostOptions, type IgPostType,
} from '@/api/services/instagramContent'
import { Icon, MarketingPage, MkCard, MkDialog, MkError, MkLoading, MkNotice, MkSteps } from '../mk'
import { firstPositive, fmtBytes, fmtWhen, isVertical, measureLocalFile, postTypeIcon, trim } from './helpers'
import { MediaEditor, MediaRequirements, type MediaFileState } from './MediaEditor'
import { CaptionAi, type CaptionAiResult } from './CaptionAi'
import { IgPreview } from './IgPreview'

/**
 * POST MUHARRIRI (composer) — TO'LIQ EKRANLI ALOHIDA SAHIFA.
 *
 * Ilgari bu forma 900px'lik MODAL ichida edi: ikkita ustun siqilib, media maydonlari,
 * AI paneli va Instagram ko'rinishi bir-birini itarardi. Endi u alohida marshrut:
 *   `/admin/marketing/kontent/yangi`      — yangi post;
 *   `/admin/marketing/kontent/post/:id`   — mavjud rejani tahrirlash.
 *
 * ⚠️ BOSQICHLAR — SEHRGAR (wizard) EMAS. Ular shunchaki bitta formaning bo'laklari va
 * QULFLANMAYDI: foydalanuvchi istagan bosqichga bosa oladi. Sabab: tahrirlashda odam ko'pincha
 * BITTA narsani (masalan vaqtni) o'zgartirgani keladi — uni to'rtta qadamdan o'tkazish bekorga
 * ish bo'lardi.
 *
 * 🔴 SHUNING UCHUN "UZOQ" HOLAT BOSQICH KOMPONENTLARIDA SAQLANMAYDI. Faol bosqichdan boshqasi
 * umuman chizilmaydi, ya'ni bosqich almashishi bilan uning komponenti UNMOUNT bo'ladi. Fayl
 * yuklash jarayoni (`fileState`) ham, AI so'rovi (`ai*`) ham SHU YERDA — aks holda 40 MB video
 * yuklanayotganda yoki Gemini javobi kutilayotganda bosqichni almashtirish natijani jimgina
 * yo'q qilardi.
 *
 * ⚠️ Faol bosqich URL'da (`?bosqich=matn`) saqlanadi — sahifa yangilansa yoki havola ulashilsa
 * o'sha joy ochiladi.
 *
 * ⚠️ Instagram ko'rinishi (`IgPreview`) o'ng ustunda HAR BOSQICHDA turadi: odam nima
 * yasayotganini doim ko'rib tursin (modal'da u faqat forma yonida siqilib turardi).
 *
 * ⚠️ MEDIA MANZILI OCHIQ HTTPS BO'LISHI SHART — Instagram faylni O'ZI yuklab oladi. Uchta
 * yo'l bor va uchalasi ham QOLADI (batafsil `MediaEditor.tsx` da):
 * 1. «Fayl yuklash» (yoki sudrab tashlash) — ALOHIDA ochiq papkaga;
 * 2. «Fayldan o'lchash» — fayl YUKLANMAYDI, faqat brauzerda o'lchanadi;
 * 3. manzilni QO'LDA yozish.
 *
 * ⚠️ O'lchamlar 0 = "noma'lum" — backend bunday holatda tekshiruvni o'tkazib yuboradi.
 */

/** Bosqichlar — sahifa ichidagi "sub-sahifa" tugmalari. */
const STEPS = [
  { id: 'tur', label: 'Tur va media', hint: 'Nima chiqadi', icon: 'image' },
  { id: 'matn', label: 'Matn', hint: 'Caption va hashtag', icon: 'text' },
  { id: 'vaqt', label: 'Vaqt va sozlamalar', hint: 'Qachon chiqadi', icon: 'calendar' },
  { id: 'tekshir', label: 'Ko‘rib chiqish', hint: 'Xatolarni tekshirish', icon: 'eye' },
]

const STEP_IDS = STEPS.map((s) => s.id)

/** Navbat sahifasining manzili — «← Navbat», «Bekor qilish» va saqlashdan keyingi qaytish. */
const QUEUE = '/admin/marketing/kontent'

/**
 * Media qatori: BARQAROR `uid` + backendga ketadigan elementning O'ZI.
 *
 * 🔴 `uid` PAYLOAD'GA HECH QACHON TUSHMAYDI. U ATAYIN `IgMediaItem` ning ICHIGA emas, undan
 * TASHQARIDA (o'rovchi obyektda) saqlanadi — ya'ni `payload()` `row.item` ni beradi va backend
 * `IgMediaJson` da mavjud bo'lmagan maydonni umuman ko'rmaydi. Snapshot (`dirty`) ham faqat
 * `item` lardan quriladi: aks holda yangi forma har ochilganda `uid` boshqa bo'lib, "saqlanmagan
 * o'zgarish bor" degan yolg'on ogohlantirish chiqardi.
 *
 * Nega kerak: ilgari `MediaEditor` `key={i}` bilan chizilardi. 5 elementli karuselda 2-elementni
 * o'chirsangiz eski 3-element `key=1` bo'lib qolar va oldingi nusxaning holatini (xato matni,
 * sudrash ramkasi) MEROS qilib olardi — xato butunlay boshqa element ostida turib qolardi.
 */
interface MediaRow {
  uid: string
  item: IgMediaItem
}

export function ContentComposer() {
  const { id } = useParams<{ id: string }>()
  const navigate = useNavigate()
  const [params, setParams] = useSearchParams()
  const { can } = usePerm()
  const canEdit = can('marketing.content', 'edit')

  /* ── Forma holati ── */
  const [type, setType] = useState<IgPostType>('image')
  const [caption, setCaption] = useState('')
  const [rows, setRows] = useState<MediaRow[]>(() => [newRow('image')])
  const [options, setOptions] = useState<IgPostOptions>(emptyOptions())
  const [at, setAt] = useState('')

  /**
   * Hammualliflar maydonining XOM matni.
   *
   * ⚠️ Ilgari maydon to'g'ridan-to'g'ri `collaborators.join(', ')` ni ko'rsatib, har bosilgan
   * harfda `split(',')` qilardi. Natijada VERGUL yozib bo'lmasdi: `"ali,"` → `['ali','']` →
   * `filter(Boolean)` → `['ali']` → `join` → `"ali"` va React nazorat qilinadigan qiymatni
   * tiklab, vergulni EKRANDAN O'CHIRARDI. Keyingi harf `b` esa `"alib"` bo'lib ketardi, ya'ni
   * IKKINCHI hammuallifni qo'lda yozishning iloji yo'q edi va post mavjud bo'lmagan
   * username'ga taklif bilan joylanardi.
   */
  const [collabText, setCollabText] = useState('')

  /* ── Media fayllarining holati (uid bo'yicha) ── */
  const [fileState, setFileState] = useState<Record<string, MediaFileState>>({})

  /**
   * Har media uchun "so'nggi boshlangan ish" belgisi — POYGA himoyasi.
   *
   * ⚠️ Faqat OXIRGI boshlangan yuklash/o'lchash natijasi yoziladi. Aks holda: A faylini
   * tashlab, kutmasdan B ni tashlagan odam A tugagach POSTDA O'ZI TANLAMAGAN faylni topardi.
   * Element o'chirilganda kalit ham o'chiriladi — kechikib kelgan natija endi hech qayerga
   * yozilmaydi.
   */
  const jobsRef = useRef<Record<string, number>>({})

  /* ── Yuklash / saqlash ── */
  const [post, setPost] = useState<IgPost | null>(null)
  const [loading, setLoading] = useState(!!id)
  const [loadError, setLoadError] = useState('')
  const [saving, setSaving] = useState(false)
  const [error, setError] = useState('')
  const [askLeave, setAskLeave] = useState(false)
  /** Tasdiq kutayotgan YANGI post turi (media elementlari yo'qoladigan holat). */
  const [askType, setAskType] = useState<IgPostType | null>(null)

  /* ── AI paneli (holati SHU YERDA — `CaptionAi.tsx` izohiga qarang) ── */
  const [aiOpen, setAiOpen] = useState(false)
  const [aiMeta, setAiMeta] = useState<IgCaptionMeta | null>(null)
  const [aiMetaError, setAiMetaError] = useState('')
  const [aiTopic, setAiTopic] = useState('')
  const [aiTone, setAiTone] = useState('')
  const [aiLanguage, setAiLanguage] = useState('')
  const [aiBusy, setAiBusy] = useState(false)
  const [aiError, setAiError] = useState('')
  const [aiResult, setAiResult] = useState<CaptionAiResult | null>(null)
  /** Uslub/til ro'yxati BIR MARTA olinadi (panel har ochilganda qayta so'ralmasin). */
  const aiMetaLoading = useRef(false)
  /** AI so'rovining "so'nggi ish" belgisi — eskirgan javob yangi formaga tushmasin. */
  const aiJob = useRef(0)

  /** Backendga ketadigan sof media massivi (uid'siz) — payload, snapshot va tekshiruvlar uchun. */
  const media = useMemo(() => rows.map((r) => r.item), [rows])

  /**
   * "O'zgardimi" ni bilish uchun BOSHLANG'ICH holat satri.
   *
   * ⚠️ Maydonlarni birma-bir solishtirish o'rniga bitta seriyalangan satr: media massivi
   * ichma-ich obyektlardan iborat va qo'lda solishtirish tez orada haqiqatdan uzilib qolardi
   * (yangi maydon qo'shilsa esa jimgina "o'zgarmagan" deb ko'rinardi).
   */
  const [baseline, setBaseline] = useState(() => snapshot('image', '', [emptyMedia('image')], emptyOptions(), ''))

  /* ── Faol bosqich URL'da ── */
  const rawStep = params.get('bosqich') ?? ''
  const step = STEP_IDS.includes(rawStep) ? rawStep : 'tur'
  const setStep = useCallback((next: string) => {
    const p = new URLSearchParams(params)
    p.set('bosqich', next)
    // `replace` — bosqich almashuvi brauzer tarixini to'ldirmasin: "orqaga" tugmasi
    // foydalanuvchini navbatga qaytarishi kerak, o'n bitta bosqichdan emas.
    setParams(p, { replace: true })
  }, [params, setParams])

  /**
   * ── Mavjud rejani yuklash · YOKI formani BO'SH holatga tiklash ──
   *
   * 🔴 `else` TARMOG'I MAJBURIY. `/kontent/post/:id` va `/kontent/yangi` — ikkita SIBLING
   * marshrut va ikkalasi ham AYNAN shu elementni chizadi, ya'ni React Router komponentni
   * QAYTA MOUNT QILMAYDI (bir xil turdagi element qayta ishlatiladi). Effekt faqat
   * `if (!id) return` bilan boshlanganda A postini tahrirlab, brauzerning «Orqaga» tugmasi
   * bilan `/yangi` ga o'tgan odam A ning to'ldirilgan formasini ko'rardi va `post` hamon A
   * bo'lgani uchun «Saqlash» YANGI post yaratmay, `updateIgPost(A.id, …)` bilan
   * **A NI USTIGA YOZARDI**.
   *
   * ⚠️ `?kun=` shu yerda qo'llanadi (ilgari alohida "faqat mount" effekti bor edi). Sabab:
   * tiklash AYNAN "yangi forma ochildi" hodisasi, ya'ni kalendardan kelingan kun ham o'shanda
   * qo'yilishi kerak. Ikki joyda ayri turganda ular BIR-BIRI BILAN TO'QNASHARDI (tiklash
   * `at` ni bo'shatar, mount effekti esa qayta yozardi) va `/post/A` dan `/yangi?kun=…` ga
   * o'tishda kun umuman qo'llanmasdi. Effekt `[id]` ga bog'langani uchun foydalanuvchi vaqtni
   * o'zgartirgach URL o'sha kunda qolsa ham tanlov QAYTA YOZILMAYDI.
   */
  useEffect(() => {
    if (!id) {
      const fresh = [newRow('image')]
      const opts = emptyOptions()
      const when = dayParamAt(params)
      setPost(null)
      setType('image')
      setCaption('')
      setRows(fresh)
      setOptions(opts)
      setCollabText('')
      setAt(when)
      // Boshlang'ich vaqt "o'zgarish" hisoblanmaydi: foydalanuvchi hali hech narsa yozmagan,
      // shuning uchun darhol chiqib ketsa tasdiq so'ralmasligi kerak.
      setBaseline(snapshot('image', '', fresh.map((r) => r.item), opts, when))
      setLoading(false)
      setLoadError('')
      setError('')
      // Fayl va AI jarayonlari ham yangi formaga o'tmasin: eski so'rov qaytsa tashlanadi.
      setFileState({})
      jobsRef.current = {}
      aiJob.current += 1
      setAiOpen(false)
      setAiTopic('')
      setAiResult(null)
      setAiError('')
      setAiBusy(false)
      return
    }

    let alive = true
    setLoading(true)
    setLoadError('')
    getIgPost(id)
      .then((p) => {
        if (!alive) return
        setPost(p)
        setType(p.postType)
        setCaption(p.caption)
        const loaded = (p.media.length > 0 ? p.media.map((m) => ({ ...m })) : [emptyMedia(defaultKind(p.postType))])
          .map((m) => ({ uid: newUid(), item: m }))
        setRows(loaded)
        const opts = { ...p.options, collaborators: [...p.options.collaborators] }
        setOptions(opts)
        setCollabText(opts.collaborators.join(', '))
        const when = (p.scheduledAt ?? '').slice(0, 16)
        setAt(when)
        setBaseline(snapshot(p.postType, p.caption, loaded.map((r) => r.item), opts, when))
        setFileState({})
        jobsRef.current = {}
      })
      .catch((e) => { if (alive) setLoadError(apiErrorMessage(e, "Rejani yuklab bo'lmadi")) })
      .finally(() => { if (alive) setLoading(false) })
    return () => { alive = false }
    // `params` ATAYIN bog'liqlikda emas: `?kun=` faqat forma tiklanganda (id o'zgarganda)
    // qo'llanadi, keyingi URL o'zgarishlari foydalanuvchi tanlagan vaqtni bosib ketmasin.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [id])

  /**
   * Zonadan TASHQARIGA tushgan fayl — brauzer uni O'ZI ochib, SPA'dan olib chiqib ketardi
   * (saqlanmagan forma bilan birga). Modul foydalanuvchini fayl sudrashga ATAYIN chaqiradi,
   * ya'ni chetga tushirish ehtimoli yuqori — shuning uchun himoya HUJJAT darajasida.
   */
  useEffect(() => {
    const swallow = (e: DragEvent) => e.preventDefault()
    document.addEventListener('dragover', swallow)
    document.addEventListener('drop', swallow)
    return () => {
      document.removeEventListener('dragover', swallow)
      document.removeEventListener('drop', swallow)
    }
  }, [])

  /* ── Sanagichlar (backenddagi qoida bilan bir xil) ── */
  const chars = caption.length
  const tags = countHashtags(caption)
  const mentions = countMentions(caption)

  /** ⚠️ AI javobi kelganda matn AYNAN O'SHA PAYTDA bo'sh ekanini bilish uchun (§ `runCaptionAi`). */
  const captionRef = useRef(caption)
  useEffect(() => { captionRef.current = caption }, [caption])

  /**
   * Hammualliflarning YAKUNIY ro'yxati — xom matndan.
   *
   * ⚠️ Payload ham, "o'zgardimi" solishtiruvi ham SHUNDAN oladi: foydalanuvchi maydondan
   * chiqmasdan (blur qilmasdan) «Saqlash» ni bossa ham yozgani yo'qolmasin.
   */
  const effectiveOptions = useMemo(
    () => ({ ...options, collaborators: parseCollaborators(collabText) }),
    [options, collabText],
  )

  /* ═══════════ MEDIA: qatorlar, fayl holati, yuklash ═══════════ */

  const patchFile = (uid: string, patch: Partial<MediaFileState>) => {
    setFileState((prev) => ({ ...prev, [uid]: { ...emptyFileState(), ...prev[uid], ...patch } }))
  }

  const patchMedia = (uid: string, patch: Partial<IgMediaItem>) => {
    setRows((prev) => prev.map((r) => (r.uid === uid ? { ...r, item: { ...r.item, ...patch } } : r)))
  }

  const addRow = () => setRows((prev) => [...prev, newRow('image')])

  const removeRow = (uid: string) => {
    setRows((prev) => prev.filter((r) => r.uid !== uid))
    setFileState((prev) => {
      const next = { ...prev }
      delete next[uid]
      return next
    })
    // Ish belgisi o'chirilgani uchun kechikib kelgan yuklash natijasi endi yozilmaydi.
    delete jobsRef.current[uid]
  }

  /**
   * Faylni serverga yuklaydi va manzil bilan birga O'LCHAMLARNI ham maydonlarga qo'yadi.
   *
   * ⚠️ SERVER O'LCHOVI USTUN — u faylning o'zidan (JPEG sarlavhasi, MP4 `mvhd`) o'qiladi,
   * ya'ni brauzer bergan qiymatdan ishonchliroq. Lekin server hamma narsani o'qiy olmaydi:
   * VIDEO kengligi/balandligi unda 0 («noma'lum») bo'lib qaytadi. Shuning uchun 0 qiymat
   * brauzer o'lchovi bilan to'ldiriladi — aks holda to'g'ri o'lcham yo'qolib, backend 9:16
   * tekshiruvini umuman o'tkazib yuborardi.
   *
   * ⚠️ Eski qiymatlar SAQLANMAYDI: bu boshqa fayl, undagi o'lcham yangisiga aloqasiz.
   * ⚠️ Brauzer o'lchovi yiqilsa yuklash BEKOR QILINMAYDI: fayl allaqachon serverda.
   */
  const runUpload = async (uid: string, file: File) => {
    const job = (jobsRef.current[uid] ?? 0) + 1
    jobsRef.current[uid] = job
    patchFile(uid, { uploading: true, uploadError: '', measureError: '' })
    try {
      const info = await uploadIgMedia(file)

      let local: Partial<IgMediaItem> = {}
      try { local = await measureLocalFile(file) } catch { /* ixtiyoriy */ }

      // Eskirgan natija (orada boshqa fayl tanlangan yoki element o'chirilgan) — tashlanadi.
      if (jobsRef.current[uid] !== job) return
      patchMedia(uid, {
        url: info.url,
        kind: info.kind,
        sizeBytes: firstPositive(info.sizeBytes, local.sizeBytes),
        width: firstPositive(info.width, local.width),
        height: firstPositive(info.height, local.height),
        durationSeconds: firstPositive(info.durationSeconds, local.durationSeconds),
      })
    } catch (e) {
      if (jobsRef.current[uid] !== job) return
      patchFile(uid, { uploadError: apiErrorMessage(e, "Faylni yuklab bo'lmadi") })
    } finally {
      if (jobsRef.current[uid] === job) patchFile(uid, { uploading: false })
    }
  }

  /** «Fayldan o'lchash» — fayl YUKLANMAYDI, faqat brauzerda o'lchanadi. */
  const runMeasure = async (uid: string, file: File) => {
    const job = (jobsRef.current[uid] ?? 0) + 1
    jobsRef.current[uid] = job
    patchFile(uid, { measuring: true, measureError: '' })
    try {
      const info = await measureLocalFile(file)
      if (jobsRef.current[uid] !== job) return
      patchMedia(uid, info)
    } catch (e) {
      if (jobsRef.current[uid] !== job) return
      patchFile(uid, { measureError: e instanceof Error ? e.message : "Faylni o'qib bo'lmadi" })
    } finally {
      if (jobsRef.current[uid] === job) patchFile(uid, { measuring: false })
    }
  }

  /**
   * Tanlangan/sudrab tashlangan FAYLLAR.
   *
   * ⚠️ Karusel yasayotgan odam 5 ta rasmni birdan tashlashi tabiiy — ilgari qolgani JIMGINA
   * tashlanardi. Endi karuselda qolganlariga yangi elementlar yaratiladi (chegara
   * `carouselItems.max`), boshqa turlarda esa "faqat bittasi olindi" deb OCHIQ aytiladi.
   *
   * ⚠️ Yuklash KETMA-KET: beshta katta faylni bir vaqtda yuborish brauzerning ulanish
   * chegarasiga urilib, hammasini sekinlashtirardi.
   */
  const uploadFiles = async (uid: string, files: File[]) => {
    if (files.length === 0) return
    patchFile(uid, { notice: '' })

    if (type !== 'carousel') {
      if (files.length > 1) {
        patchFile(uid, {
          notice: `Bu post turida bir vaqtda faqat BITTA fayl bo‘ladi — «${files[0].name}» olindi, qolgan ${files.length - 1} tasi olinmadi.`,
        })
      }
      await runUpload(uid, files[0])
      return
    }

    const free = Math.max(0, IG_LIMITS.carouselItems.max - rows.length)
    const extra = files.slice(1, 1 + free)
    const skipped = files.length - 1 - extra.length
    const created = extra.map(() => newRow('image'))
    if (created.length > 0) setRows((prev) => [...prev, ...created])
    if (skipped > 0) {
      patchFile(uid, {
        notice: `Karuselda ko‘pi bilan ${IG_LIMITS.carouselItems.max} ta element bo‘ladi — ${skipped} ta fayl olinmadi.`,
      })
    }

    await runUpload(uid, files[0])
    for (let i = 0; i < created.length; i++) await runUpload(created[i].uid, extra[i])
  }

  /** Tur o'zgarganda media ro'yxati va turi moslashtiriladi (karuselda kamida 2 ta element). */
  const changeType = (next: IgPostType) => {
    setType(next)
    setRows((prev) => {
      const kind = defaultKind(next)
      // ⚠️ Story va karusel IKKALA turni ham qabul qiladi — u yerda foydalanuvchi tanlovi
      // saqlanadi. Qolgan turlarda tur bir xil (reels/video — video, rasm — rasm).
      const keepKind = next === 'story' || next === 'carousel'
      const list = prev.map((r) => (keepKind ? r : { ...r, item: { ...r.item, kind } }))
      if (next === 'carousel') {
        while (list.length < IG_LIMITS.carouselItems.min) list.push(newRow('image'))
        return list.slice(0, IG_LIMITS.carouselItems.max)
      }
      return list.slice(0, 1)
    })
  }

  /**
   * Turni almashtirish so'rovi — TO'LDIRILGAN element yo'qoladigan bo'lsa avval tasdiq.
   *
   * ⚠️ Tur almashganda ortiqcha elementlar HAQIQATAN olib tashlanadi (`slice`), ya'ni
   * yuklangan besh rasmli karuseldan «Rasm» ga o'tish to'rttasini yo'q qiladi. Buni tasdiqsiz
   * qilish bir necha daqiqalik ishni bitta bosishda o'chirardi.
   */
  const requestType = (next: IgPostType) => {
    if (next === type) return
    if (droppedBy(rows, next).some((r) => rowFilled(r.item))) { setAskType(next); return }
    changeType(next)
  }

  /**
   * Klientdagi tekshiruv — SERVERNIKINI almashtirmaydi, faqat oldindan ogohlantiradi.
   * Yakuniy qaror baribir serverda (`InstagramPublishContract.ValidatePost`).
   */
  const localError = useMemo(() => {
    if (chars > IG_LIMITS.captionChars) return `Matn juda uzun: ${chars} belgi (ruxsat ${IG_LIMITS.captionChars}).`
    if (tags > IG_LIMITS.hashtags) return `Hashtag ko‘p: ${tags} ta (ruxsat ${IG_LIMITS.hashtags}).`
    if (mentions > IG_LIMITS.mentions) return `Mention ko‘p: ${mentions} ta (ruxsat ${IG_LIMITS.mentions}).`

    if (type === 'carousel') {
      if (media.length < IG_LIMITS.carouselItems.min || media.length > IG_LIMITS.carouselItems.max) {
        return `Karuselda ${IG_LIMITS.carouselItems.min}–${IG_LIMITS.carouselItems.max} ta element bo‘lishi kerak (hozir ${media.length}).`
      }
      const withCaption = media.findIndex((m) => m.caption.trim().length > 0)
      if (withCaption >= 0) {
        // ⚠️ Yo'l ham AYTILADI: matn maydoni «Tur va media» bosqichida, elementning ostida
        // faqat matn BOR bo'lsa chiqadi (aks holda foydalanuvchi xatoni tozalay olmasdi).
        return `${withCaption + 1}-elementga matn yozilgan: karusel elementlarida matn ishlamaydi, uni umumiy matn maydoniga yozing. «Tur va media» bosqichida shu elementdagi matnni tozalang (yoki elementni olib tashlab qayta qo‘shing).`
      }
    }

    for (let i = 0; i < media.length; i++) {
      const m = media[i]
      const prefix = media.length > 1 ? `${i + 1}-element: ` : ''
      if (!m.url.trim()) return `${prefix}media manzili bo‘sh.`
      if (!isHttpsUrl(m.url)) return `${prefix}manzil ochiq HTTPS bo‘lishi shart — Instagram faylni o‘zi yuklab oladi.`
      if (m.kind === 'image' && !isJpegUrl(m.url)) return `${prefix}rasm faqat JPEG bo‘lishi kerak (.jpg yoki .jpeg).`
      if (m.kind === 'video' && !isVideoUrl(m.url)) return `${prefix}video faqat MP4 yoki MOV bo‘lishi kerak.`
      if (m.coverUrl && !isHttpsUrl(m.coverUrl)) return `${prefix}muqova manzili ham HTTPS bo‘lishi kerak.`
    }
    return ''
  }, [chars, tags, mentions, type, media])

  /**
   * Bosqich "to'ldirilganmi" — MkSteps'dagi ✓ belgisi uchun.
   *
   * ⚠️ `vaqt` HAR DOIM bajarilgan: bo'sh vaqt ham TO'G'RI qiymat (post navbatning keyingi
   * aylanishida joylanadi). Uni "to'ldirilmagan" deb ko'rsatish yolg'on ogohlantirish bo'lardi.
   */
  const done: Record<string, boolean> = {
    tur: media.length > 0 && media.every(mediaUrlOk),
    matn: caption.trim().length > 0
      && chars <= IG_LIMITS.captionChars
      && tags <= IG_LIMITS.hashtags
      && mentions <= IG_LIMITS.mentions,
    vaqt: true,
    tekshir: !localError,
  }

  /* ── Saqlanmagan o'zgarish ── */
  const dirty = snapshot(type, caption, media, effectiveOptions, at) !== baseline

  /**
   * Brauzer darajasidagi himoya (tabni yopish / yangilash).
   *
   * ⚠️ Router navigatsiyasi ATAYIN bloklanmaydi — `useBlocker` bilan har bir havolani
   * ushlash murakkab va sinuvchan. Sahifadan chiqishning IKKALA yo'li ham («← Navbat» va
   * «Bekor qilish») `cancel()` orqali o'tadi va tasdiq so'raydi (pastda).
   */
  useEffect(() => {
    if (!dirty) return
    const onBeforeUnload = (e: BeforeUnloadEvent) => { e.preventDefault(); e.returnValue = '' }
    window.addEventListener('beforeunload', onBeforeUnload)
    return () => window.removeEventListener('beforeunload', onBeforeUnload)
  }, [dirty])

  /* ═══════════ AI CAPTION ═══════════ */

  /** Uslub/til ro'yxati — panel BIRINCHI ochilganda bir marta (keshlanadi). */
  useEffect(() => {
    if (!aiOpen || aiMeta || aiMetaLoading.current) return
    aiMetaLoading.current = true
    let alive = true
    getIgCaptionMeta()
      .then((m) => {
        if (!alive) return
        setAiMeta(m)
        // Foydalanuvchi ro'yxat kelguncha tanlagan bo'lsa — tanlovi qoladi.
        setAiTone((prev) => prev || m.defaultTone)
        setAiLanguage((prev) => prev || m.defaultLanguage)
      })
      .catch((e) => {
        if (!alive) return
        setAiMetaError(apiErrorMessage(e, "Sozlamalarni olib bo'lmadi"))
        // Qayta ochilganda yana urinib ko'rilsin (tarmoq tiklangan bo'lishi mumkin).
        aiMetaLoading.current = false
      })
    return () => { alive = false }
  }, [aiOpen, aiMeta])

  /** AI matnini maydonga qo'yish — «Almashtirish» yoki «Oxiriga qo'shish». */
  const applyAiCaption = (text: string, mode: 'replace' | 'append') => {
    setCaption((prev) => (
      mode === 'append' && prev.trim().length > 0
        ? `${prev.trimEnd()}\n\n${text}`
        : text
    ))
    setAiResult(null)
    setAiOpen(false)
  }

  const runCaptionAi = async () => {
    if (aiBusy) return
    const job = aiJob.current + 1
    aiJob.current = job
    setAiBusy(true)
    setAiError('')
    setAiResult(null)
    try {
      const res = await generateIgCaption({ postType: type, topic: aiTopic.trim(), tone: aiTone, language: aiLanguage })
      // Forma orada tiklangan bo'lsa (yangi post) — eskirgan javob yozilmaydi.
      if (aiJob.current !== job) return
      // ⚠️ Javob 200 bo'lgani MUVAFFAQIYAT DEGANI EMAS: sabab `ok`/`error` da (kalit
      // sozlanmagan, Gemini timeout, format buzuq). `ok` ni tekshirmaslik foydalanuvchiga
      // BO'SH matn qo'yib qo'yardi.
      if (!res.ok) { setAiError(res.error || 'AI matn yoza olmadi.'); return }
      // Maydon bo'sh — yo'qotadigan narsa yo'q, tasdiq ham so'ralmaydi.
      // ⚠️ Qaror JAVOB KELGAN PAYTDAGI matn bo'yicha (`captionRef`): so'rov 10–20 soniya
      // ketadi va shu orada odam matn yozgan bo'lishi mumkin — eski (bo'sh) qiymatga qarab
      // "almashtirish" uning yozganini jimgina o'chirardi.
      if (captionRef.current.trim().length === 0) { applyAiCaption(res.caption, 'replace'); return }
      setAiResult({ caption: res.caption, hashtags: res.hashtags })
    } catch (e) {
      if (aiJob.current !== job) return
      setAiError(apiErrorMessage(e, "AI'ga so'rov yuborib bo'lmadi"))
    } finally {
      if (aiJob.current === job) setAiBusy(false)
    }
  }

  /* ═══════════ SAQLASH ═══════════ */

  /**
   * Saqlash uchun yuboriladigan tana — yaratish ham, tahrirlash ham AYNAN shundan.
   *
   * ⚠️ `uid` bu yerga TUSHMAYDI: `rows` dan faqat `item` olinadi (`media`), ya'ni backendga
   * `IgMediaJson` da mavjud maydonlargina ketadi.
   */
  const payload = () => ({
    postType: type,
    caption,
    // Karuseldan boshqasida faqat BIRINCHI element yuboriladi. `changeType` allaqachon
    // `slice` qiladi, ya'ni bu ikkinchi qavat himoya (tur qo'lda o'zgartirilgan holat uchun).
    media: type === 'carousel' ? media : media.slice(0, 1),
    options: effectiveOptions,
    // Bo'sh bo'lsa backend "hozir" deb oladi — post keyingi worker tsiklida joylanadi.
    scheduledAt: at ? `${at}:00` : '',
  })

  /**
   * Navbatga qaytish + yashil xabar.
   *
   * ⚠️ `state` — Navbat sahifasi bilan KONTRAKT: `mkNotice` (yashil xabar) va `month`
   * ("yyyy-MM", ixtiyoriy).
   *
   * ⚠️ `month` NEGA kerak: Navbat har ochilganda JORIY oyni ko'rsatadi. Sentabrga
   * rejalashtirilgan postni tahrirlab saqlagan odam avgust navbatiga tushar, tepada yashil
   * «Reja yangilandi» turar, post esa ro'yxatda YO'Q edi — ya'ni saqlash "ishlamagandek"
   * ko'rinardi. Endi navbat AYNAN saqlangan post oyini ochadi.
   *
   * ⚠️ Buzuq yoki bo'sh `month` UMUMAN yuborilmaydi (navbat uni baribir jim tashlaydi).
   */
  const backToQueue = (mkNotice: string, month?: string) => (
    navigate(QUEUE, { state: month ? { mkNotice, month } : { mkNotice } })
  )

  const save = async () => {
    // ⚠️ Qayta kirish qulfi: ikki marta bosilgan «Saqlash» IKKITA post yaratardi.
    if (saving) return
    if (localError) { setError(localError); return }
    setSaving(true)
    setError('')
    try {
      if (post) {
        const saved = await updateIgPost(post.id, payload())
        backToQueue('Reja yangilandi.', monthOf(saved.scheduledAt, at))
      } else {
        const saved = await createIgPost(payload())
        backToQueue('Post navbatga qo‘shildi.', monthOf(saved.scheduledAt, at))
      }
      // ⚠️ Muvaffaqiyatda `setSaving(false)` ATAYIN chaqirilmaydi: `navigate` dan keyin bu
      // komponent olib tashlanadi va o'chgan komponentga `setState` bo'lardi.
    } catch (e) {
      setError(apiErrorMessage(e, "Saqlab bo'lmadi"))
      setSaving(false)
    }
  }

  /**
   * «Saqlab, hoziroq joylash» — FAQAT yangi postda.
   *
   * ⚠️ IKKI QADAM: avval `createIgPost`, keyin qaytgan `id` bilan `publishIgPost`. Agar
   * SAQLASH o'tib, JOYLASH yiqilsa — foydalanuvchiga AYNAN shu aytiladi va u navbatga
   * o'tkaziladi. Aks holda odam "saqlanmadi" deb o'ylab, postni IKKI MARTA yaratardi.
   *
   * ⚠️ So'rov joylanishni KUTMAYDI: rasm odatda darhol chiqadi, video/reels esa
   * «Joylanmoqda» bo'lib qoladi va uni worker oxiriga yetkazadi.
   */
  const saveAndPublish = async () => {
    if (saving) return
    if (localError) { setError(localError); return }
    setSaving(true)
    setError('')
    try {
      // 1-qadam — SAQLASH. Bu yerdagi xato oddiy saqlash xatosi: forma ochiq qoladi.
      const created = await createIgPost(payload())

      // 2-qadam — JOYLASH. ALOHIDA `try`: bu yerdagi xato "saqlanmadi" DEGANI EMAS,
      // shuning uchun foydalanuvchi navbatga o'tkaziladi va sabab ochiq aytiladi.
      try {
        const res = await publishIgPost(created.id)
        backToQueue(
          res.status === 'published'
            ? 'Post Instagram’ga joylandi.'
            : 'Post joylashga yuborildi — holati «Joylanmoqda». Video bir necha daqiqa olishi mumkin.',
          // Joylashdan keyin server vaqtni aniqlashtirishi mumkin — u USTUN.
          monthOf(res.scheduledAt, created.scheduledAt, at),
        )
      } catch (e) {
        // ⚠️ Post SAQLANDI (faqat joylash yiqildi) — demak u navbatda turibdi va oyni
        // ko'rsatish shu yerda ham kerak.
        backToQueue(
          `Reja saqlandi, lekin joylab bo‘lmadi: ${apiErrorMessage(e, "noma'lum sabab")}`,
          monthOf(created.scheduledAt, at),
        )
      }
    } catch (e) {
      setError(apiErrorMessage(e, "Saqlab bo'lmadi"))
      setSaving(false)
    }
  }

  /** Sahifadan chiqish — o'zgarish bo'lsa avval tasdiq so'raladi. */
  const cancel = () => {
    if (dirty) { setAskLeave(true); return }
    navigate(QUEUE)
  }

  /* ═══════════ Darvozalar: ruxsat · yuklash · tahrirlab bo'lmaydigan post ═══════════ */

  if (!canEdit) {
    return (
      <MarketingPage
        title={id ? 'Rejani tahrirlash' : 'Yangi post'}
        sub="Post yaratish va tahrirlash uchun ruxsat kerak"
        back={{ to: QUEUE, label: 'Navbat' }}
      >
        <MkError text="Bu sahifa uchun «Marketing → Kontent» bo‘limida TAHRIRLASH ruxsati kerak. Ruxsatni «Xodimlar va rollar» bo‘limidan berish mumkin." />
      </MarketingPage>
    )
  }

  if (loading) {
    return (
      <MarketingPage title="Rejani tahrirlash" sub="Yuklanmoqda…" back={{ to: QUEUE, label: 'Navbat' }}>
        <MkLoading />
      </MarketingPage>
    )
  }

  if (loadError) {
    return (
      <MarketingPage title="Rejani tahrirlash" sub="Reja topilmadi" back={{ to: QUEUE, label: 'Navbat' }}>
        <MkError text={loadError} onRetry={() => navigate(0)} />
      </MarketingPage>
    )
  }

  /**
   * ⚠️ Faqat «Rejalashtirilgan» post tahrirlanadi (§5.9). Backend ham 400 qaytaradi, LEKIN
   * foydalanuvchi buni SAQLASH tugmasini bosishdan OLDIN bilishi kerak — aks holda u butun
   * formani to'ldirib, oxirida xato olardi.
   */
  if (post && !isEditable(post)) {
    return (
      <MarketingPage
        title="Rejani tahrirlash"
        sub={`${post.postTypeLabel} · ${fmtWhen(post.scheduledAt)} · ${post.statusLabel}`}
        back={{ to: QUEUE, label: 'Navbat' }}
      >
        <div className="mk-alert mk-alert-danger fade-up">
          <Icon name="warn" style={{ width: 18, height: 18, flexShrink: 0, marginTop: 2 }} />
          <div style={{ fontSize: 13, lineHeight: 1.55 }}>
            <div className="mk-alert-title">Bu postni endi o‘zgartirib bo‘lmaydi</div>
            Postning holati — <b>{post.statusLabel}</b>. Instagram API’si joylangan (yoki joylanayotgan)
            postni tahrirlashni qo‘llab-quvvatlamaydi: matnni ham, rasmni ham faqat <b>Instagram
            ilovasidan</b> o‘zgartirish mumkin. Tahrirlash faqat <b>«Rejalashtirilgan»</b> holatda ochiq.
            <div style={{ marginTop: 14 }}>
              <button className="btn btn-primary btn-sm" onClick={() => navigate(QUEUE)}>
                <Icon name="arrowLeft" /> Navbatga qaytish
              </button>
            </div>
          </div>
        </div>
      </MarketingPage>
    )
  }

  /* ═══════════ Sarlavha ma'lumoti ═══════════ */

  const typeLabel = IG_POST_TYPES.find((t) => t.id === type)?.label ?? type
  const whenLabel = at ? fmtWhen(`${at}:00`) : 'vaqt belgilanmagan (hoziroq navbatga)'
  const stateLabel = post
    ? (dirty ? 'saqlanmagan o‘zgarish bor' : 'saqlangan')
    : 'hali saqlanmagan'
  const stepIndex = STEP_IDS.indexOf(step)
  const nextStep = STEPS[stepIndex + 1]
  const typeDrop = askType ? droppedBy(rows, askType) : []

  return (
    <MarketingPage
      title={post ? 'Rejani tahrirlash' : 'Yangi post'}
      sub={`${typeLabel} · ${whenLabel} · ${stateLabel}`}
      /* ⚠️ `back` propi ATAYIN BERILMAYDI: u oddiy `<Link>` chizadi va sticky sarlavhada
         turgani uchun eng tabiiy chiqish yo'li bo'lardi — saqlanmagan 20 daqiqalik ish bitta
         bosishda TASDIQSIZ yo'qolardi. O'rniga AYNI shu ko'rinishdagi tugma, lekin `cancel()`
         orqali: sahifadan chiqishning ikkala yo'li ham bir xil himoyalangan. */
      actions={
        <button className="btn btn-ghost btn-sm" onClick={cancel} disabled={saving}>
          <Icon name="arrowLeft" /> Navbat
        </button>
      }
    >
      <div className="fade-up">
        {/* Bosqichlar — QULFLANMAGAN: istalganiga bosish mumkin. */}
        <MkSteps steps={STEPS} active={step} onSelect={setStep} done={done} />

        <div className="mk-composer">
          {/* ── CHAP: faqat FAOL bosqich ── */}
          <div style={{ minWidth: 0 }}>
            {step === 'tur' && (
              <StepMedia
                type={type}
                rows={rows}
                fileState={fileState}
                onChangeType={requestType}
                onPatch={patchMedia}
                onAdd={addRow}
                onRemove={removeRow}
                onUploadFiles={(uid, files) => { void uploadFiles(uid, files) }}
                onMeasure={(uid, file) => { void runMeasure(uid, file) }}
              />
            )}

            {step === 'matn' && (
              <StepCaption
                type={type}
                caption={caption}
                chars={chars}
                tags={tags}
                mentions={mentions}
                aiOpen={aiOpen}
                onToggleAi={() => setAiOpen((v) => !v)}
                onCaption={setCaption}
                ai={{
                  meta: aiMeta,
                  metaError: aiMetaError,
                  topic: aiTopic,
                  tone: aiTone,
                  language: aiLanguage,
                  busy: aiBusy,
                  error: aiError,
                  result: aiResult,
                  onTopic: setAiTopic,
                  onTone: setAiTone,
                  onLanguage: setAiLanguage,
                  onRun: () => { void runCaptionAi() },
                  onApply: applyAiCaption,
                  onAgain: () => setAiResult(null),
                }}
              />
            )}

            {step === 'vaqt' && (
              <StepSchedule
                type={type}
                at={at}
                options={options}
                collabText={collabText}
                onAt={setAt}
                onOptions={setOptions}
                onCollabText={setCollabText}
                onCollabCommit={() => setOptions((prev) => ({ ...prev, collaborators: parseCollaborators(collabText) }))}
              />
            )}

            {step === 'tekshir' && (
              <StepReview type={type} media={media} caption={caption} at={at} localError={localError} />
            )}
          </div>

          {/* ── O'NG: Instagram ko'rinishi HAR BOSQICHDA ── */}
          <div className="mk-side-sticky">
            <div className="field-label">Instagram’da qanday ko‘rinadi</div>
            <IgPreview type={type} media={media} caption={caption} />

            <MkCard title="Tez ma’lumot">
              <div style={{ display: 'grid', gap: 8, fontSize: 12.5 }}>
                <Quick label="Tur" value={typeLabel} />
                <Quick label="Media" value={`${media.length} ta · ${media.filter(mediaUrlOk).length} tasi tayyor`} />
                <Quick
                  label="Matn"
                  value={`${chars} / ${IG_LIMITS.captionChars} belgi`}
                  danger={chars > IG_LIMITS.captionChars}
                />
                <Quick
                  label="Hashtag"
                  value={`${tags} / ${IG_LIMITS.hashtags}`}
                  danger={tags > IG_LIMITS.hashtags}
                />
                <Quick label="Joylash" value={whenLabel} />
              </div>
            </MkCard>
          </div>
        </div>

        {/* ── PASTKI AMALLAR QATORI ── */}
        <div className="mk-actionbar">
          <div style={{ flex: 1, minWidth: 200 }}>
            {/* ⚠️ Server xatosi — QIZIL. To'ldirilmagan joy esa hali XATO emas: u shunchaki
                "saqlash uchun nima yetishmayapti" degan maslahat (yangi forma ochilishi bilan
                qizil blok chiqishi bekorga qo'rqitardi). */}
            {error && (
              <div className="mk-state mk-state-error" style={{ padding: 10 }}>
                <Icon name="warn" style={{ width: 17, height: 17, flexShrink: 0 }} />
                <span>{error}</span>
              </div>
            )}
            {!error && localError && (
              <div className="field-hint" style={{ margin: 0 }}>Saqlash uchun: {localError}</div>
            )}
          </div>

          <div style={{ display: 'flex', gap: 10, flexWrap: 'wrap', justifyContent: 'flex-end' }}>
            <button className="btn btn-ghost" onClick={cancel} disabled={saving}>Bekor qilish</button>

            {nextStep && (
              <button className="btn btn-outline" onClick={() => setStep(nextStep.id)} disabled={saving}>
                Keyingi: {nextStep.label} <Icon name="arrowRight" />
              </button>
            )}

            {!post && (
              <button
                className="btn btn-outline"
                onClick={() => void saveAndPublish()}
                disabled={saving || !!localError}
                title="Reja saqlanadi va darhol Instagram’ga yuboriladi"
              >
                <Icon name="send" /> Saqlab, hoziroq joylash
              </button>
            )}

            <button className="btn btn-primary" onClick={() => void save()} disabled={saving || !!localError}>
              <Icon name="check" /> {saving ? 'Saqlanmoqda…' : post ? 'Saqlash' : 'Navbatga qo‘shish'}
            </button>
          </div>
        </div>
      </div>

      {/* Saqlanmagan o'zgarish — chiqishdan oldin tasdiq. */}
      {askLeave && (
        <MkDialog
          title="O‘zgarishlar saqlanmaydi"
          tone="danger"
          onClose={() => setAskLeave(false)}
          footer={
            <>
              <button className="btn btn-ghost" onClick={() => setAskLeave(false)}>Formaga qaytish</button>
              <button className="btn btn-primary" onClick={() => navigate(QUEUE)}>
                <Icon name="close" /> Ha, chiqilsin
              </button>
            </>
          }
        >
          <div style={{ fontSize: 13.5, lineHeight: 1.55 }}>
            Formada saqlanmagan o‘zgarishlar bor. Chiqsangiz ular <b>yo‘qoladi</b> — yuklangan
            fayllar serverda qoladi, lekin post yozuvi yaratilmaydi.
          </div>
        </MkDialog>
      )}

      {/* Tur almashuvi — to'ldirilgan elementlar yo'qoladigan bo'lsa tasdiq. */}
      {askType && (
        <MkDialog
          title="Media elementlari olib tashlanadi"
          tone="danger"
          onClose={() => setAskType(null)}
          footer={
            <>
              <button className="btn btn-ghost" onClick={() => setAskType(null)}>Bekor qilish</button>
              <button
                className="btn btn-primary"
                onClick={() => { changeType(askType); setAskType(null) }}
              >
                <Icon name="check" /> Ha, turi almashtirilsin
              </button>
            </>
          }
        >
          <div style={{ fontSize: 13.5, lineHeight: 1.55 }}>
            «{IG_POST_TYPES.find((t) => t.id === askType)?.label ?? askType}» turida{' '}
            {askType === 'carousel' ? `${IG_LIMITS.carouselItems.max} ta` : 'bitta'} element bo‘ladi,
            shuning uchun <b>{typeDrop.length} ta to‘ldirilgan element olib tashlanadi</b> (manzil va
            o‘lchamlari bilan). Yuklangan fayllar serverda qoladi, lekin ular postga kirmaydi.
          </div>
        </MkDialog>
      )}
    </MarketingPage>
  )
}

/* ═══════════════════════════════════════ 1) TUR VA MEDIA ═══════════════════════════════════════ */

function StepMedia({
  type, rows, fileState, onChangeType, onPatch, onAdd, onRemove, onUploadFiles, onMeasure,
}: {
  type: IgPostType
  rows: MediaRow[]
  fileState: Record<string, MediaFileState>
  onChangeType: (t: IgPostType) => void
  onPatch: (uid: string, patch: Partial<IgMediaItem>) => void
  onAdd: () => void
  onRemove: (uid: string) => void
  onUploadFiles: (uid: string, files: File[]) => void
  onMeasure: (uid: string, file: File) => void
}) {
  return (
    <>
      <MkCard title="Post turi" sub="Instagram’da qaysi ko‘rinishda chiqishini tanlang">
        <div className="mk-type-grid">
          {IG_POST_TYPES.map((t) => (
            <button
              key={t.id}
              type="button"
              className={`mk-type${type === t.id ? ' sel' : ''}`}
              onClick={() => onChangeType(t.id)}
            >
              <span className="mk-type-ic"><Icon name={postTypeIcon(t.id)} /></span>
              <span className="mk-type-name">{t.label}</span>
              <span className="mk-type-hint">{t.hint}</span>
            </button>
          ))}
        </div>
        {/* ⚠️ Matn AYNAN nima bo'lishini aytadi: ilgari "faqat birinchisi YUBORILADI" deb
            yozilardi, aslida esa qolganlari ro'yxatdan OLIB TASHLANADI. */}
        <div className="field-hint" style={{ marginTop: 10 }}>
          Tur o‘zgarsa media ro‘yxati ham moslashadi: karuselda kamida {IG_LIMITS.carouselItems.min} ta
          element bo‘ladi, qolgan turlarda esa birinchisidan boshqa elementlar <b>olib tashlanadi</b>
          {' '}(to‘ldirilgan element bo‘lsa avval tasdiq so‘raladi).
        </div>
      </MkCard>

      <MediaRequirements type={type} />

      <MkCard
        title="Media"
        sub={type === 'carousel'
          ? `Karusel: ${rows.length} / ${IG_LIMITS.carouselItems.max} ta element`
          : 'Bitta fayl — sudrab tashlang, yuklang yoki ochiq HTTPS manzilni yozing'}
      >
        {rows.map((r, i) => (
          <MediaEditor
            /* ⚠️ `key` — BARQAROR `uid`, indeks EMAS: o'rtadagi element o'chirilganda keyingisi
               oldingisining holatini (xato matni, sudrash ramkasi) meros qilib olmasin. */
            key={r.uid}
            item={r.item}
            index={i}
            showIndex={type === 'carousel'}
            type={type}
            state={fileState[r.uid] ?? emptyFileState()}
            onChange={(patch) => onPatch(r.uid, patch)}
            onRemove={rows.length > 1 ? () => onRemove(r.uid) : undefined}
            onUploadFiles={(files) => onUploadFiles(r.uid, files)}
            onMeasure={(file) => onMeasure(r.uid, file)}
          />
        ))}

        {type === 'carousel' && rows.length < IG_LIMITS.carouselItems.max && (
          <button className="btn btn-outline btn-sm" onClick={onAdd} style={{ marginTop: 12 }}>
            <Icon name="plus" /> Element qo‘shish ({rows.length} / {IG_LIMITS.carouselItems.max})
          </button>
        )}
      </MkCard>
    </>
  )
}

/* ═══════════════════════════════════════ 2) MATN ═══════════════════════════════════════ */

/** AI panelining boshqaruvi — hammasi `ContentComposer` holatidan (bosqich almashsa yo'qolmasin). */
interface CaptionAiProps {
  meta: IgCaptionMeta | null
  metaError: string
  topic: string
  tone: string
  language: string
  busy: boolean
  error: string
  result: CaptionAiResult | null
  onTopic: (v: string) => void
  onTone: (v: string) => void
  onLanguage: (v: string) => void
  onRun: () => void
  onApply: (text: string, mode: 'replace' | 'append') => void
  onAgain: () => void
}

function StepCaption({
  type, caption, chars, tags, mentions, aiOpen, onToggleAi, onCaption, ai,
}: {
  type: IgPostType
  caption: string
  chars: number
  tags: number
  mentions: number
  aiOpen: boolean
  onToggleAi: () => void
  onCaption: (v: string) => void
  ai: CaptionAiProps
}) {
  return (
    <MkCard
      title="Post matni (caption)"
      sub="Hashtaglar shu maydonning ichida yoziladi"
      actions={
        <button className={`btn btn-sm ${aiOpen ? 'btn-outline' : 'btn-ghost'}`} onClick={onToggleAi}>
          <Icon name="sparkle" /> AI bilan yozish
        </button>
      }
    >
      {aiOpen && (
        <CaptionAi
          meta={ai.meta}
          metaError={ai.metaError}
          topic={ai.topic}
          tone={ai.tone}
          language={ai.language}
          busy={ai.busy}
          error={ai.error}
          result={ai.result}
          onTopic={ai.onTopic}
          onTone={ai.onTone}
          onLanguage={ai.onLanguage}
          onRun={ai.onRun}
          onApply={ai.onApply}
          onAgain={ai.onAgain}
          onClose={onToggleAi}
        />
      )}

      <textarea
        className="textarea"
        value={caption}
        rows={14}
        placeholder="Postning matni, hashtaglar bilan…"
        onChange={(e) => onCaption(e.target.value)}
      />

      <div style={{ display: 'flex', gap: 16, flexWrap: 'wrap', marginTop: 8 }}>
        <Counter label="belgi" value={chars} max={IG_LIMITS.captionChars} />
        <Counter label="hashtag" value={tags} max={IG_LIMITS.hashtags} />
        <Counter label="mention" value={mentions} max={IG_LIMITS.mentions} />
      </div>

      {type === 'carousel' && (
        <div className="field-hint">
          ⚠️ Karuselda matn faqat SHU maydondan olinadi — alohida elementlarga yozilgan matn
          Instagram’da ko‘rinmaydi.
        </div>
      )}
    </MkCard>
  )
}

/** Chegara sanagichi — oshib ketsa qizil bo'ladi (backend baribir rad etadi). */
function Counter({ label, value, max }: { label: string; value: number; max: number }) {
  const over = value > max
  return (
    <span style={{ fontSize: 12.5, fontWeight: 700, color: over ? 'var(--danger)' : 'var(--text-3)' }}>
      {value} / {max} {label}
    </span>
  )
}

/* ═══════════════════════════════════════ 3) VAQT VA SOZLAMALAR ═══════════════════════════════════════ */

function StepSchedule({
  type, at, options, collabText, onAt, onOptions, onCollabText, onCollabCommit,
}: {
  type: IgPostType
  at: string
  options: IgPostOptions
  collabText: string
  onAt: (v: string) => void
  onOptions: (o: IgPostOptions) => void
  onCollabText: (v: string) => void
  onCollabCommit: () => void
}) {
  // ⚠️ O'tib ketgan vaqt XATO EMAS: post navbatning keyingi aylanishida joylanadi. Lekin
  // odam buni ADASHIB tanlagan bo'lishi mumkin — shuning uchun sariq MASLAHAT chiqadi.
  const past = !!at && at < localNow()
  const collaborators = parseCollaborators(collabText)

  return (
    <>
      <MkCard title="Joylash vaqti" sub="CRM navbatida saqlanadi — Instagram’da hech narsa band qilinmaydi">
        <div style={{ display: 'flex', gap: 10, flexWrap: 'wrap', alignItems: 'flex-end' }}>
          <div className="field" style={{ margin: 0, minWidth: 240 }}>
            <label className="field-label">Sana va vaqt</label>
            <input
              className="input"
              type="datetime-local"
              value={at}
              onChange={(e) => onAt(e.target.value)}
            />
          </div>
        </div>

        <div style={{ display: 'flex', gap: 8, flexWrap: 'wrap', marginTop: 12 }}>
          {/* «Hoziroq» — maydonni BO'SHATADI: bo'sh vaqt backend uchun "hozir" degani. */}
          <button className="btn btn-outline btn-sm" onClick={() => onAt('')}>Hoziroq</button>
          <button className="btn btn-outline btn-sm" onClick={() => onAt(dayAt(0, '10:00'))}>Bugun 10:00</button>
          <button className="btn btn-outline btn-sm" onClick={() => onAt(dayAt(1, '10:00'))}>Ertaga 10:00</button>
          <button className="btn btn-outline btn-sm" onClick={() => onAt(dayAt(1, '19:00'))}>Ertaga 19:00</button>
        </div>

        {past && (
          <div className="mk-alert" style={{ marginTop: 14, marginBottom: 0 }}>
            <Icon name="clock" style={{ width: 18, height: 18, flexShrink: 0, marginTop: 2 }} />
            <div style={{ fontSize: 12.5, lineHeight: 1.5 }}>
              <div className="mk-alert-title">Tanlangan vaqt allaqachon o‘tgan</div>
              Bu <b>xato emas</b> — bunday post navbatning keyingi aylanishida (bir daqiqa ichida)
              joylanadi. Agar kelajakdagi vaqtni nazarda tutgan bo‘lsangiz, sanani tekshiring.
            </div>
          </div>
        )}

        <div className="field-hint" style={{ marginTop: 12 }}>
          Bo‘sh qoldirilsa post navbatning keyingi aylanishida (bir daqiqa ichida) joylanadi.
          Vaqt CRM navbatida saqlanadi — Instagram’da hech narsa oldindan band qilinmaydi, shuning
          uchun vaqtni istagancha o‘zgartirsa bo‘ladi.
        </div>
      </MkCard>

      {(type === 'reels' || type === 'video') && (
        <MkCard title="Lentaga chiqarish">
          <div className="row-between">
            <div>
              <div className="opt-name">Lentaga ham chiqarilsin</div>
              <div className="opt-desc">Reels profil lentasida ham ko‘rinadi (share_to_feed).</div>
            </div>
            <div
              className={`switch${options.shareToFeed ? ' on' : ''}`}
              onClick={() => onOptions({ ...options, shareToFeed: !options.shareToFeed })}
            />
          </div>
        </MkCard>
      )}

      {/* ⚠️ Ilgari bu blok `<details>` ichida yashiringan edi (modalda joy yo'q edi). Endi
          sahifa to'liq ekranli — sozlamalar OCHIQ turadi va ogohlantirishlar ko'rinadi. */}
      <MkCard title="Qo‘shimcha sozlamalar" sub="Ixtiyoriy — bo‘sh qoldirilsa Instagram’ga yuborilmaydi">
        <div className="field">
          <label className="field-label">Hammualliflar (collaborators)</label>
          {/* ⚠️ Maydon XOM matnni ko'rsatadi, ro'yxatga aylantirish esa faqat maydondan
              chiqqanda (`onBlur`). Har harfda `split`/`join` qilinsa VERGUL yozib bo'lmasdi
              (izohi `ContentComposer` dagi `collabText` da). */}
          <input
            className="input"
            value={collabText}
            placeholder="username1, username2"
            onChange={(e) => onCollabText(e.target.value)}
            onBlur={onCollabCommit}
          />
          <div className="field-hint">
            Vergul bilan ajrating. Hozir tanilgani: <b>{collaborators.length}</b> ta
            {collaborators.length > 0 && ` (${collaborators.join(', ')})`}.
            Ko‘pi bilan {IG_LIMITS.collaborators} ta. ⚠️ Ular taklifni Instagram’da <b>qabul qilishi</b> kerak —
            aks holda post faqat sizning profilingizda qoladi.
          </div>
        </div>

        <div className="field">
          <label className="field-label">Joylashuv ID (location_id)</label>
          <input
            className="input"
            value={options.locationId}
            placeholder="Ixtiyoriy — Facebook Page joylashuv ID’si"
            onChange={(e) => onOptions({ ...options, locationId: e.target.value })}
          />
        </div>

        {(type === 'reels' || type === 'video') && (
          <div className="field" style={{ marginBottom: 0 }}>
            <label className="field-label">Audio nomi (Reels)</label>
            <input
              className="input"
              value={options.audioName}
              onChange={(e) => onOptions({ ...options, audioName: e.target.value })}
            />
            <div className="field-hint">⚠️ Instagram’da audio nomini keyin faqat BIR MARTA o‘zgartirish mumkin.</div>
          </div>
        )}
      </MkCard>
    </>
  )
}

/* ═══════════════════════════════════════ 4) KO'RIB CHIQISH ═══════════════════════════════════════ */

/**
 * Yakuniy tekshiruv.
 *
 * ⚠️ `localError` faqat BITTA (birinchi) muammoni qaytaradi — pastdagi tugmalar uchun shu
 * yetadi. Bu yerda esa har bir shart ALOHIDA ko'rsatiladi: foydalanuvchi "yana nima
 * yetishmaydi" ni bittalab tuzatib yurmasin.
 */
function StepReview({
  type, media, caption, at, localError,
}: {
  type: IgPostType
  media: IgMediaItem[]
  caption: string
  at: string
  localError: string
}) {
  const chars = caption.length
  const tags = countHashtags(caption)
  const mentions = countMentions(caption)
  const rows = type === 'carousel' ? media : media.slice(0, 1)

  const checks: { ok: boolean; label: string; detail: string }[] = [
    {
      ok: true,
      label: 'Post turi tanlandi',
      detail: `${IG_POST_TYPES.find((t) => t.id === type)?.label ?? type} · ${isVertical(type) ? '9:16 (vertikal)' : 'kvadrat/lenta'}`,
    },
    {
      ok: type !== 'carousel'
        || (rows.length >= IG_LIMITS.carouselItems.min && rows.length <= IG_LIMITS.carouselItems.max),
      label: 'Elementlar soni',
      detail: type === 'carousel'
        ? `${rows.length} ta (ruxsat ${IG_LIMITS.carouselItems.min}–${IG_LIMITS.carouselItems.max})`
        : '1 ta element yuboriladi',
    },
    {
      ok: rows.length > 0 && rows.every((m) => !!m.url.trim() && isHttpsUrl(m.url)),
      label: 'Media manzillari ochiq HTTPS',
      detail: 'Instagram faylni o‘zi yuklab oladi — login, IP cheklov va redirect ishlamaydi.',
    },
    {
      ok: rows.every((m) => (m.kind === 'image' ? isJpegUrl(m.url) : isVideoUrl(m.url))),
      label: 'Fayl formati to‘g‘ri',
      detail: 'Rasm — faqat JPEG (.jpg/.jpeg); video — MP4 yoki MOV.',
    },
    {
      ok: rows.every((m) => !m.coverUrl || isHttpsUrl(m.coverUrl)),
      label: 'Muqova manzili (bo‘lsa) HTTPS',
      detail: 'Video muqovasi ham tashqaridan yuklab olinadi.',
    },
    {
      ok: type !== 'carousel' || rows.every((m) => m.caption.trim().length === 0),
      label: 'Karusel elementlarida matn yo‘q',
      detail: 'Karuselda matn faqat umumiy maydondan olinadi — element matnini backend rad etadi.',
    },
    {
      ok: chars <= IG_LIMITS.captionChars && tags <= IG_LIMITS.hashtags && mentions <= IG_LIMITS.mentions,
      label: 'Matn chegaralarda',
      detail: `${chars}/${IG_LIMITS.captionChars} belgi · ${tags}/${IG_LIMITS.hashtags} hashtag · ${mentions}/${IG_LIMITS.mentions} mention`,
    },
    {
      ok: true,
      label: 'Joylash vaqti',
      detail: at ? fmtWhen(`${at}:00`) : 'belgilanmagan — navbatning keyingi aylanishida joylanadi',
    },
  ]

  return (
    <>
      <MkCard
        title="Tekshiruv ro‘yxati"
        sub={localError ? 'Saqlashdan oldin tuzatilishi kerak' : 'Hammasi joyida — saqlash mumkin'}
      >
        <div style={{ display: 'grid', gap: 10 }}>
          {checks.map((c) => (
            <div key={c.label} style={{ display: 'flex', gap: 10, alignItems: 'flex-start' }}>
              <span
                className="rule-num"
                style={{
                  flexShrink: 0,
                  background: c.ok ? 'var(--success-soft)' : 'var(--danger-soft)',
                  color: c.ok ? 'var(--success)' : 'var(--danger)',
                }}
              >
                <Icon name={c.ok ? 'check' : 'close'} style={{ width: 13, height: 13 }} />
              </span>
              <div style={{ minWidth: 0 }}>
                <div style={{ fontSize: 13, fontWeight: 700 }}>{c.label}</div>
                <div className="field-hint" style={{ margin: 0 }}>{c.detail}</div>
              </div>
            </div>
          ))}
        </div>

        {localError && (
          <div style={{ marginTop: 14 }}>
            <MkNotice tone="danger" text={`Birinchi to‘siq: ${localError}`} />
          </div>
        )}
      </MkCard>

      <MkCard title="Media jamlanmasi" sub="0 = noma’lum: bunday maydon tekshirilmaydi">
        <div style={{ display: 'grid', gap: 12 }}>
          {rows.map((m, i) => (
            <div key={i} style={{ display: 'flex', gap: 12, alignItems: 'center' }}>
              <div className="mk-media-thumb" style={{ width: 64, height: 64, flexShrink: 0 }}>
                {m.url && isHttpsUrl(m.url) && m.kind === 'image'
                  ? <img src={m.url} alt="" style={{ width: '100%', height: '100%', objectFit: 'cover' }} />
                  : (
                    <div style={{ display: 'grid', placeItems: 'center', height: '100%', color: 'var(--text-3)' }}>
                      <Icon name={m.kind === 'video' ? 'film' : 'image'} style={{ width: 20, height: 20 }} />
                    </div>
                  )}
              </div>
              <div style={{ minWidth: 0, fontSize: 12.5 }}>
                <div style={{ fontWeight: 700 }}>
                  {rows.length > 1 ? `${i + 1}-element · ` : ''}{m.kind === 'video' ? 'Video' : 'Rasm'}
                </div>
                <div className="field-hint" style={{ margin: 0 }}>
                  {fmtBytes(m.sizeBytes)}
                  {' · '}
                  {m.width > 0 && m.height > 0 ? `${m.width}×${m.height} px` : 'o‘lcham noma’lum'}
                  {m.kind === 'video' && ` · ${m.durationSeconds > 0 ? `${m.durationSeconds} s` : 'davomiylik noma’lum'}`}
                </div>
                <div className="field-hint" style={{ margin: 0, wordBreak: 'break-all' }}>
                  {m.url ? trim(m.url, 70) : 'manzil kiritilmagan'}
                </div>
              </div>
            </div>
          ))}
        </div>
      </MkCard>

      {/* 🔴 §5.9 — SAQLASHDAN OLDIN, chunki bu qaytarib bo'lmaydigan amal haqida. */}
      <div className="mk-alert mk-alert-danger">
        <Icon name="warn" style={{ width: 18, height: 18, flexShrink: 0, marginTop: 2 }} />
        <div style={{ fontSize: 12.5, lineHeight: 1.5 }}>
          <div className="mk-alert-title">Joylangan postni CRM’dan o‘zgartirib bo‘lmaydi</div>
          Instagram API’si joylangan postni tahrirlashni ham, o‘chirishni ham qo‘llab-quvvatlamaydi —
          matnni ham, rasmni ham faqat <b>Instagram ilovasidan</b> o‘zgartirish mumkin. Shu sababli
          tahrirlash faqat <b>«Rejalashtirilgan»</b> postlarda ochiq. Joylangan postni navbatdan
          o‘chirsangiz — <b>faqat CRM yozuvi</b> o‘chadi, Instagram’dagi post o‘z joyida qoladi.
        </div>
      </div>
    </>
  )
}

/* ═══════════════════════════════════════ KICHIK YORDAMCHILAR ═══════════════════════════════════════ */

/** «Tez ma'lumot» kartochkasidagi bitta qator. */
function Quick({ label, value, danger }: { label: string; value: string; danger?: boolean }) {
  return (
    <div style={{ display: 'flex', gap: 10, justifyContent: 'space-between', alignItems: 'baseline' }}>
      <span style={{ color: 'var(--text-3)' }}>{label}</span>
      <span style={{ fontWeight: 700, textAlign: 'right', color: danger ? 'var(--danger)' : undefined }}>
        {value}
      </span>
    </div>
  )
}

/** Yangi media qatorining hisoblagichi — `crypto` bo'lmagan holat uchun zaxira. */
let uidSeq = 0

/**
 * Barqaror `uid`.
 *
 * ⚠️ `crypto.randomUUID` faqat XAVFSIZ kontekstda (https yoki localhost) mavjud — dev serverni
 * tarmoq IP'si orqali ochganda u `undefined` bo'lardi va sahifa yiqilardi. Shuning uchun zaxira
 * hisoblagich (qiymat faqat SHU sahifada, faqat `key` uchun ishlatiladi).
 */
function newUid(): string {
  uidSeq += 1
  return globalThis.crypto?.randomUUID?.() ?? `m${Date.now().toString(36)}-${uidSeq}`
}

/** Bo'sh media qatori (uid bilan). */
function newRow(kind: 'image' | 'video'): MediaRow {
  return { uid: newUid(), item: emptyMedia(kind) }
}

/** Yangi element uchun fayl holati — hammasi bo'sh. */
function emptyFileState(): MediaFileState {
  return { uploading: false, measuring: false, uploadError: '', measureError: '', notice: '' }
}

/** Elementga biror narsa kiritilganmi (tur almashuvida tasdiq so'rash uchun). */
function rowFilled(m: IgMediaItem): boolean {
  return m.url.trim().length > 0
    || m.coverUrl.trim().length > 0
    || m.altText.trim().length > 0
    || m.caption.trim().length > 0
    || m.width > 0 || m.height > 0 || m.sizeBytes > 0 || m.durationSeconds > 0
}

/** Tur `next` ga o'zgarsa RO'YXATDAN CHIQIB ketadigan qatorlar (`changeType` bilan bir xil qoida). */
function droppedBy(rows: MediaRow[], next: IgPostType): MediaRow[] {
  return next === 'carousel' ? rows.slice(IG_LIMITS.carouselItems.max) : rows.slice(1)
}

/**
 * Navbat ochadigan OY ("yyyy-MM") — birinchi TO'G'RI manbadan.
 *
 * ⚠️ Tartib muhim: chaqiruvchi SERVER qaytargan `scheduledAt` ni birinchi beradi (u haqiqat
 * manbai — server vaqtni aniqlashtirgan bo'lishi mumkin), formadagi qiymat esa zaxira.
 * Ikkalasi ham bo'sh/buzuq bo'lsa `undefined` qaytadi va `month` umuman yuborilmaydi.
 */
function monthOf(...sources: (string | null | undefined)[]): string | undefined {
  for (const src of sources) {
    const m = (src ?? '').slice(0, 7)
    if (/^\d{4}-(0[1-9]|1[0-2])$/.test(m)) return m
  }
  return undefined
}

/** "ali, vali" → ['ali','vali']. Bo'sh bo'laklar tashlanadi. */
function parseCollaborators(raw: string): string[] {
  return raw.split(',').map((s) => s.trim()).filter(Boolean)
}

/**
 * Navbat kalendaridan kelingan kun (`?kun=YYYY-MM-DD`) → o'sha kunning 10:00 i.
 * Parametr yo'q yoki buzuq bo'lsa — bo'sh satr ("vaqt belgilanmagan").
 */
function dayParamAt(search: URLSearchParams): string {
  const day = search.get('kun') ?? ''
  return /^\d{4}-\d{2}-\d{2}$/.test(day) ? `${day}T10:00` : ''
}

/**
 * Media manzili "tayyor"mi — bosqich ✓ belgisi va «Tez ma'lumot» sanog'i uchun.
 *
 * ⚠️ Bu YENGIL tekshiruv (manzil + format). To'liq qoida `localError` da qoladi: ikkinchi
 * nusxasini yasash ikki joyda ikki xil javob berish xavfini tug'dirardi.
 */
function mediaUrlOk(m: IgMediaItem): boolean {
  if (!m.url.trim() || !isHttpsUrl(m.url)) return false
  return m.kind === 'image' ? isJpegUrl(m.url) : isVideoUrl(m.url)
}

/** Forma holatining seriyalangan surati — "o'zgardimi" savoli uchun. */
function snapshot(
  type: IgPostType, caption: string, media: IgMediaItem[], options: IgPostOptions, at: string,
): string {
  return JSON.stringify({ type, caption, media, options, at })
}

/**
 * `datetime-local` formatidagi HOZIRGI vaqt ("yyyy-MM-ddTHH:mm").
 *
 * ⚠️ `toISOString()` ATAYIN ishlatilmaydi — u UTC'ga o'tkazadi va O'zbekistonda soatni
 * 5 soatga surib yuborardi ("o'tgan vaqt" ogohlantirishi noto'g'ri chiqardi).
 */
function localNow(): string {
  return stamp(new Date())
}

/** Bugundan `plusDays` keyingi kunning berilgan soati ("yyyy-MM-ddTHH:mm"). */
function dayAt(plusDays: number, time: string): string {
  const d = new Date()
  d.setDate(d.getDate() + plusDays)
  return `${stamp(d).slice(0, 10)}T${time}`
}

function stamp(d: Date): string {
  const p = (n: number) => String(n).padStart(2, '0')
  return `${d.getFullYear()}-${p(d.getMonth() + 1)}-${p(d.getDate())}T${p(d.getHours())}:${p(d.getMinutes())}`
}
