import { useCallback, useEffect, useMemo, useState } from 'react'
import { Link, Navigate, useNavigate, useParams } from 'react-router-dom'
import {
  ArrowDown, ArrowLeftToLine, ArrowRightToLine, ArrowUp, ChevronDown, ChevronRight, FolderInput, Lock, LockOpen, Pencil,
  Plus, Settings2, Trash2, Users,
} from 'lucide-react'
import type { WikiNotesTree, WikiPage, WikiTeamMember, WikiTreeNode } from '@/api/services/wiki'
import {
  createMemberNote, createMyNote, deleteMemberNote, deleteMyNote, getMyNotes, getTeamMemberNotes, getTeamMembers,
  lockMemberNote, memberNoteEditSource, moveMyNote, myNotePageSource, renameNote, searchMyNotes, teamNotePageSource,
} from '@/api/services/wiki'
import type { DropdownMenuItem } from '@/components/ui/DropdownMenu'
import { Loader } from '@/components/ui/Loader'
import { usePerm } from '@/lib/permissions'
import { apiErrorMessage, cn } from '@/lib/utils'
import { WikiEmptyState, WikiShell } from './components/WikiShell'
import { WikiPageView } from './components/WikiPageView'
import { WikiSearchResults } from './components/WikiSearchResults'
import { WikiSettingsModal } from './components/WikiSettingsModal'
import { WikiTreeRows } from './components/WikiTreeRows'
import { NewSectionButton, SectionBlock } from './components/WikiSections'
import { useStoredSet } from './lib/useStoredSet'
import { useWikiPage } from './lib/useWikiPage'
import { useWikiSearch } from './lib/useWikiSearch'
import {
  ancestorIds, buildTree, descendantCount, effectiveLocked, formatStamp, HR_ROLE_LABELS, moveTarget, moveToSectionTarget,
  splitSections, type MoveDir,
} from './lib/wikiModel'

/** Rahbar xodim eslatmasini ochib turganda — shu oraliqda yangilanadi ("jonli" kuzatish). */
const LIVE_PAGE_MS = 4000
const LIVE_TREE_MS = 10000

const MOVES: { dir: MoveDir; label: string; icon: DropdownMenuItem['icon'] }[] = [
  { dir: 'up', label: 'Yuqoriga', icon: ArrowUp },
  { dir: 'down', label: 'Pastga', icon: ArrowDown },
  { dir: 'indent', label: 'Yuqoridagi eslatma ichiga', icon: ArrowRightToLine },
  { dir: 'outdent', label: 'Bir daraja tashqariga', icon: ArrowLeftToLine },
]

const initials = (name: string) =>
  name.split(/\s+/).filter(Boolean).slice(0, 2).map((w) => w[0]?.toUpperCase()).join('') || '?'

/** `wiki:sectionsCollapsed` ichida «Mening eslatmalarim» qismining kaliti (bo'lim id'lari GUID — to'qnashmaydi). */
const MY_ROOT_KEY = 'mine'
const NO_IDS: ReadonlySet<string> = new Set()

/** «Xodimlar» tabi manzili (`/:userId/:pageId?`). */
const TEAM_BASE = '/admin/bilimlar/xodimlar'

interface NotesPageProps {
  /** `mine` — «Eslatmalarim» (o'z eslatmalari); `team` — «Xodimlar» (rahbariyat/menejer, `wiki.team`). */
  mode: 'mine' | 'team'
  /** O'z eslatmalari manzili: `/admin/bilimlar/eslatmalar` yoki `/teacher/eslatmalar` (o'qituvchi portali) */
  basePath?: string
  /** Admin panelida — Eslatmalarim / Xodimlar tablari */
  showTabs?: boolean
  heightClass?: string
}

/**
 * ESLATMALARIM va XODIMLAR — ikki tab, bitta komponent (`mode`). Egasining talabi: rahbariyatda tepada
 * «Eslatmalarim | Xodimlar», oddiy xodimda — faqat «Eslatmalarim». Manzillar:
 * `/admin/bilimlar/eslatmalar/:pageId?` — o'z eslatmasi; `/admin/bilimlar/xodimlar/:userId?/:pageId?` — xodimniki.
 *
 * - O'z eslatmasi: egasi yozadi; RAHBARIYAT qulflagan bo'lsa — faqat o'qiydi.
 * - Xodim eslatmasi: rahbariyat va (sozlama bo'yicha) menejer o'qiydi; ochiq turganda «jonli» yangilanadi
 *   (xodim yozayotganini ko'rish). RAHBARIYAT: ⋯ → qulflash, ichiga sahifa qo'shish, o'chirish; xodimga «+» bilan yangi
 *   sahifa; «Tahrirlash» — "o'zgartirasizmi?" tasdig'idan keyin.
 * Hamma qoida serverda (`MyNotesController`, `WikiTeamController`); bu yerda — ko'rinish. `.claude/rules/wiki.md` §9–10.
 */
export function NotesPage({ mode, basePath = '/admin/bilimlar/eslatmalar', showTabs, heightClass }: NotesPageProps) {
  const { pageId, userId } = useParams<{ pageId?: string; userId?: string }>()
  const navigate = useNavigate()
  const { can } = usePerm()
  // «Eslatmalarim» — har bir xodimga ruxsatsiz (egasining talabi, 2026-10-27; server: `MyNotesController.Allowed`).
  const canNotes = mode === 'mine'
  const canTeam = mode === 'team' && can('wiki.team', 'view')
  // «Xodimlar» tabi uchun ro'yxat «Eslatmalarim» da ham olinadi: kalit (`wiki.team`, yalang `wiki` ham) yetmaydi —
  // tab faqat rahbariyatga yoki ko'radigan xodimi BOR menejerga chiqadi (pastda `teamTabVisible`).
  const loadTeam = can('wiki.team', 'view') && (mode === 'team' || !!showTabs)

  // ---------------------------------------------------------------- o'z eslatmalari
  const [myTree, setMyTree] = useState<WikiNotesTree | null>(null)
  const [myError, setMyError] = useState('')
  const [myTick, setMyTick] = useState(0)
  const reloadMine = () => setMyTick((t) => t + 1)
  const myExpanded = useStoredSet('wiki:notesExpanded')
  /** Yig'ilgan BO'LIMLAR (standart — hammasi ochiq). O'z bo'limlari ham, xodimlarniki ham — id'lar takrorlanmaydi. */
  const collapsedSections = useStoredSet('wiki:sectionsCollapsed')
  /** Hozir nomi o'zgartirilayotgan bo'lim (sarlavha o'rnida nom maydoni). */
  const [renamingId, setRenamingId] = useState<string | null>(null)
  const search = useWikiSearch(searchMyNotes)

  // ---------------------------------------------------------------- xodimlar
  const [team, setTeam] = useState<{ isLeader: boolean; members: WikiTeamMember[] } | null>(null)
  const [teamError, setTeamError] = useState('')
  const [teamTick, setTeamTick] = useState(0)
  const [memberTrees, setMemberTrees] = useState<Record<string, WikiNotesTree>>({})
  const [memberErrors, setMemberErrors] = useState<Record<string, string>>({})
  const membersOpen = useStoredSet('wiki:membersExpanded')
  const memberExpanded = useStoredSet('wiki:memberNotesExpanded')
  const [settingsOpen, setSettingsOpen] = useState(false)
  /** Rahbar "o'zgartirasizmi?" ga HA degan xodim eslatmasi (boshqa sahifaga o'tilsa — o'z-o'zidan tugaydi). */
  const [editingId, setEditingId] = useState<string | null>(null)
  const isLeader = team?.isLeader ?? false

  const isMember = mode === 'team' && !!userId
  const editingMember = isMember && !!pageId && editingId === pageId
  const view = useWikiPage(
    isMember ? (editingMember ? memberNoteEditSource : teamNotePageSource) : myNotePageSource,
    pageId,
    (saved) => {
      const rename = (t: WikiNotesTree) => ({ ...t, pages: t.pages.map((n) => (n.id === saved.id ? { ...n, title: saved.title, icon: saved.icon } : n)) })
      if (isMember && userId) setMemberTrees((m) => (m[userId] ? { ...m, [userId]: rename(m[userId]) } : m))
      else setMyTree((t) => t && rename(t))
    },
    isMember && !editingMember ? LIVE_PAGE_MS : 0,
  )

  useEffect(() => {
    if (!canNotes) return
    let alive = true
    getMyNotes()
      .then((t) => {
        if (!alive) return
        setMyTree(t)
        setMyError('')
      })
      .catch((err) => {
        if (alive) setMyError(apiErrorMessage(err, "Eslatmalarni yuklab bo'lmadi"))
      })
    return () => {
      alive = false
    }
  }, [myTick, canNotes])

  useEffect(() => {
    if (!loadTeam) return
    let alive = true
    getTeamMembers()
      .then((t) => {
        if (!alive) return
        setTeam(t)
        setTeamError('')
      })
      .catch((err) => {
        if (alive) setTeamError(apiErrorMessage(err, "Xodimlar ro'yxatini yuklab bo'lmadi"))
      })
    return () => {
      alive = false
    }
  }, [teamTick, loadTeam])
  /** Oddiy xodim (rahbariyat emas, ko'radigan hech kim yo'q) — «Xodimlar» tabi umuman yo'q (egasining talabi). */
  const teamTabVisible = !!team && (team.isLeader || team.members.length > 0)

  const loadMember = useCallback((uid: string) => {
    getTeamMemberNotes(uid)
      .then((t) => {
        setMemberTrees((m) => ({ ...m, [uid]: t }))
        setMemberErrors((e) => (e[uid] ? { ...e, [uid]: '' } : e))
      })
      .catch((err) => setMemberErrors((e) => ({ ...e, [uid]: apiErrorMessage(err, "Eslatmalarni ochib bo'lmadi") })))
  }, [])

  // Ochiq xodimlarning daraxti; tanlangan xodimniki "jonli" (yangi sahifa qo'shsa ham ko'rinsin).
  const openKey = [...membersOpen.set].join('|')
  useEffect(() => {
    if (!canTeam) return
    for (const uid of openKey ? openKey.split('|') : []) loadMember(uid)
    if (userId && !openKey.split('|').includes(userId)) loadMember(userId)
  }, [openKey, userId, canTeam, loadMember])
  useEffect(() => {
    if (!canTeam || !userId) return
    const timer = window.setInterval(() => loadMember(userId), LIVE_TREE_MS)
    return () => window.clearInterval(timer)
  }, [userId, canTeam, loadMember])

  const myNodes = useMemo(() => myTree?.pages ?? [], [myTree])
  const memberNodes = useMemo(() => (userId ? memberTrees[userId]?.pages ?? [] : []), [memberTrees, userId])
  const memberName = (uid: string) =>
    memberTrees[uid]?.ownerName ?? team?.members.find((m) => m.userId === uid)?.fullName ?? ''

  // Ochiq sahifaga olib boradigan yo'l (xodim, bo'lim, ota papkalar) O'Z-O'ZIDAN ochiladi — lekin foydalanuvchi
  // uni YOPA OLADI. Ilgari bu yo'l majburan ochiq ushlanardi: tanlangan xodim yoki ochiq sahifaning papkasi
  // strelka bosilganda yig'ilmasdi. Shu sahifada qo'lda yopilganlar `closedHere` da; boshqa sahifaga o'tilganda
  // unutiladi (yangi ochilgan sahifa daraxtda yana ko'rinsin). Hech qanday effekt yo'q — kalit render'da solishtiriladi.
  const activeNodes = isMember ? memberNodes : myNodes
  const ancestorKey = view.page ? ancestorIds(activeNodes, view.page.id).join('|') : ''
  const navKey = `${mode}|${userId ?? ''}|${pageId ?? ''}|${ancestorKey}`
  const [closedHere, setClosedHere] = useState<{ key: string; ids: Set<string> }>(() => ({ key: '', ids: new Set() }))
  const closed = closedHere.key === navKey ? closedHere.ids : NO_IDS
  const markClosed = (id: string, isClosed: boolean) =>
    setClosedHere((prev) => {
      const ids = new Set(prev.key === navKey ? prev.ids : NO_IDS)
      if (isClosed) ids.add(id)
      else ids.delete(id)
      return { key: navKey, ids }
    })
  /** Yo'l bo'yicha o'z-o'zidan ochilgan va foydalanuvchi hali yopmagan. */
  const revealed = (id: string, onPath: boolean) => onPath && !closed.has(id)
  const onAncestorPath = (id: string) => !!ancestorKey && ancestorKey.split('|').includes(id)

  /** Papkalar: eslab qolinganlar + ochiq sahifaning (yopilmagan) otalari. */
  const withAncestors = (set: Set<string>) => {
    if (!ancestorKey) return set
    const next = new Set(set)
    for (const id of ancestorKey.split('|')) if (!closed.has(id)) next.add(id)
    return next
  }
  /** Papka strelkasi KO'RINIB TURGAN holatni almashtiradi (eslab qolingan to'plamni emas). */
  const togglePath = (stored: { add: (id: string) => void; remove: (id: string) => void }, shown: Set<string>) =>
    (id: string) => {
      const isOpen = shown.has(id)
      if (isOpen) stored.remove(id)
      else stored.add(id)
      markClosed(id, isOpen)
    }
  /** Bo'lim ochiqmi: yig'ilmagan yoki ochiq sahifa uning ichida (va qo'lda yopilmagan). */
  const sectionOpen = (id: string) => !collapsedSections.set.has(id) || revealed(id, onAncestorPath(id))
  const toggleSection = (id: string, isOpen: boolean) => {
    if (isOpen) collapsedSections.add(id)
    else collapsedSections.remove(id)
    markClosed(id, isOpen)
  }
  /** «Mening eslatmalarim» ham bo'limlar kabi yig'iladi; ochiq sahifa uning ichida bo'lsa — o'zi ochiladi. */
  const myRootOpen = () => {
    if (!collapsedSections.set.has(MY_ROOT_KEY)) return true
    if (isMember || !view.page) return false
    const top = ancestorKey ? ancestorKey.split('|')[0] : view.page.id
    return revealed(MY_ROOT_KEY, !myNodes.find((x) => x.id === top)?.isSection)
  }
  /** Xodim: eslab qolingan yoki hozir tanlangan (qo'lda yopilmagan). */
  const memberOpen = (uid: string) => membersOpen.set.has(uid) || revealed(uid, uid === userId)
  const toggleMember = (uid: string) => {
    const isOpen = memberOpen(uid)
    if (isOpen) membersOpen.remove(uid)
    else membersOpen.add(uid)
    markClosed(uid, isOpen)
  }

  // ---------------------------------------------------------------- o'z eslatmalari: amallar
  const openMine = (id: string) => navigate(`${basePath}/${id}`)
  const openMember = (uid: string, id?: string) => navigate(`${TEAM_BASE}/${uid}${id ? `/${id}` : ''}`)

  const addMine = async (parentId: string) => {
    try {
      const p = await createMyNote({ parentId, title: '' })
      if (parentId) {
        myExpanded.add(parentId)
        collapsedSections.remove(parentId)
      }
      reloadMine()
      openMine(p.id)
    } catch (err) {
      alert(apiErrorMessage(err, "Eslatma yaratib bo'lmadi"))
    }
  }

  /** Yangi bo'lim — xato chaqiruvchiga qaytadi (nom maydoni ochiq qolib, sababini ko'rsatadi). */
  const addMySection = async (name: string) => {
    await createMyNote({ parentId: '', title: name, isSection: true })
    reloadMine()
  }

  const renameMySection = async (id: string, name: string) => {
    await renameNote(myNotePageSource, id, name)
    setRenamingId(null)
    reloadMine()
  }

  const moveMine = async (node: WikiTreeNode, target: { parentId: string; index: number }) => {
    try {
      await moveMyNote(node.id, target)
      if (target.parentId) myExpanded.add(target.parentId)
      reloadMine()
    } catch (err) {
      alert(apiErrorMessage(err, "Eslatmani ko'chirib bo'lmadi"))
    }
  }

  const confirmDelete = (nodes: WikiTreeNode[], node: WikiTreeNode, whose = '') => {
    const inner = descendantCount(nodes, node.id)
    const what = node.isSection ? "bo'limi" : 'eslatmasi'
    return confirm(inner > 0
      ? `«${node.title}»${whose} ${what} va uning ichidagi ${inner} ta eslatma o'chiriladi. Davom etasizmi?`
      : `«${node.title}»${whose} ${what}ni o'chirasizmi?`)
  }

  const removeMine = async (node: WikiTreeNode) => {
    if (!confirmDelete(myNodes, node)) return
    const openedInside = !isMember && !!pageId && (pageId === node.id || ancestorIds(myNodes, pageId).includes(node.id))
    if (openedInside) view.stop()
    try {
      await deleteMyNote(node.id)
      if (openedInside) navigate(basePath)
      reloadMine()
    } catch (err) {
      if (openedInside) view.resume()
      alert(apiErrorMessage(err, "Eslatmani o'chirib bo'lmadi"))
    }
  }

  const hasLockedInside = (nodes: WikiTreeNode[], id: string): boolean =>
    nodes.some((n) => n.parentId === id && (n.locked || hasLockedInside(nodes, n.id)))

  const mySections = useMemo(() => myNodes.filter((n) => n.isSection).sort((a, b) => a.order - b.order), [myNodes])

  const myMenu = (node: WikiTreeNode): DropdownMenuItem[] => {
    // Rahbariyat qulflagan eslatma (yoki uning ichi) — egasi uchun faqat o'qish.
    if (effectiveLocked(myNodes, node.id)) return []
    const menu: DropdownMenuItem[] = node.isSection
      ? [{ label: "Nomini o'zgartirish", icon: Pencil, onClick: () => setRenamingId(node.id) }]
      : [{ label: "Ichiga eslatma qo'shish", icon: Plus, onClick: () => void addMine(node.id) }]
    for (const d of MOVES) {
      const t = moveTarget(myNodes, node.id, d.dir)
      if (t && !effectiveLocked(myNodes, t.parentId)) menu.push({ label: d.label, icon: d.icon, onClick: () => void moveMine(node, t) })
    }
    // Boshqa bo'limga (yoki «Mening eslatmalarim» ga) ko'chirish.
    for (const s of [{ id: '', title: 'Mening eslatmalarim' }, ...mySections]) {
      const t = moveToSectionTarget(myNodes, node.id, s.id)
      if (t && !effectiveLocked(myNodes, s.id))
        menu.push({ label: `«${s.title}» ga ko'chirish`, icon: FolderInput, onClick: () => void moveMine(node, t) })
    }
    if (!hasLockedInside(myNodes, node.id))
      menu.push({ label: "O'chirish", icon: Trash2, danger: true, onClick: () => void removeMine(node) })
    return menu
  }

  // ---------------------------------------------------------------- xodim eslatmalari: rahbariyat amallari
  const addForMember = async (uid: string, parentId: string) => {
    try {
      const p = await createMemberNote(uid, { parentId, title: '' })
      membersOpen.add(uid)
      if (parentId) {
        memberExpanded.add(parentId)
        collapsedSections.remove(parentId)
      }
      loadMember(uid)
      setTeamTick((t) => t + 1)
      setEditingId(p.id) // o'zi yaratgan sahifani darhol yozsin — tasdiq so'ralmaydi
      openMember(uid, p.id)
    } catch (err) {
      alert(apiErrorMessage(err, "Sahifa qo'shib bo'lmadi"))
    }
  }

  const toggleMemberLock = async (uid: string, id: string, locked: boolean) => {
    try {
      const p = await lockMemberNote(id, locked)
      if (view.page?.id === id) view.patchPage({ locked: p.locked, effectiveLocked: p.effectiveLocked })
      loadMember(uid)
    } catch (err) {
      alert(apiErrorMessage(err, "Qulfni o'zgartirib bo'lmadi"))
    }
  }

  const removeForMember = async (uid: string, node: WikiTreeNode) => {
    if (!confirmDelete(memberTrees[uid]?.pages ?? [], node, ` (${memberName(uid)})`)) return
    const nodes = memberTrees[uid]?.pages ?? []
    const openedInside = isMember && !!pageId && (pageId === node.id || ancestorIds(nodes, pageId).includes(node.id))
    if (openedInside) view.stop()
    try {
      await deleteMemberNote(node.id)
      if (openedInside) openMember(uid)
      loadMember(uid)
      setTeamTick((t) => t + 1)
    } catch (err) {
      if (openedInside) view.resume()
      alert(apiErrorMessage(err, "Eslatmani o'chirib bo'lmadi"))
    }
  }

  /** Rahbar xodimga BO'LIM ochadi (xato — nom maydonida ko'rsatiladi). */
  const addMemberSection = async (uid: string, name: string) => {
    await createMemberNote(uid, { parentId: '', title: name, isSection: true })
    membersOpen.add(uid)
    loadMember(uid)
  }

  const renameMemberSection = async (uid: string, id: string, name: string) => {
    await renameNote(memberNoteEditSource, id, name)
    setRenamingId(null)
    loadMember(uid)
  }

  const memberMenu = (uid: string) => (node: WikiTreeNode): DropdownMenuItem[] => {
    if (!isLeader) return [] // menejer — faqat o'qiydi
    return [
      node.isSection
        ? { label: "Nomini o'zgartirish", icon: Pencil, onClick: () => setRenamingId(node.id) }
        : { label: "Ichiga sahifa qo'shish", icon: Plus, onClick: () => void addForMember(uid, node.id) },
      node.locked
        ? { label: 'Qulfni ochish', icon: LockOpen, onClick: () => void toggleMemberLock(uid, node.id, false) }
        : {
            label: node.isSection ? 'Qulflash (xodim bo‘limni faqat o‘qiydi)' : 'Qulflash (xodim faqat o‘qiydi)',
            icon: Lock,
            onClick: () => void toggleMemberLock(uid, node.id, true),
          },
      { label: "O'chirish", icon: Trash2, danger: true, onClick: () => void removeForMember(uid, node) },
    ]
  }

  const startEditing = (page: WikiPage) => {
    const ok = confirm(`«${page.title}» — ${memberName(userId ?? '')}ning shaxsiy eslatmasi.\n\nUni o'zgartirasizmi?`)
    if (ok) setEditingId(page.id)
  }

  const stopEditing = () => {
    view.retry() // kutib turgan o'zgarish saqlansin (manba hali tahrir manbai)
    setEditingId(null)
  }

  // ---------------------------------------------------------------- chap panel
  const mySection = canNotes && (
    <div className="mb-2">
      <div className="flex h-8 items-center gap-1 rounded-md px-1 text-[12.5px] font-semibold uppercase tracking-wide text-slate-500 hover:bg-slate-50">
        <button
          type="button"
          onClick={() => toggleSection(MY_ROOT_KEY, myRootOpen())}
          className="flex min-w-0 flex-1 items-center gap-1 text-left"
          aria-expanded={myRootOpen()}
        >
          {myRootOpen()
            ? <ChevronDown size={13} className="shrink-0 text-slate-400" />
            : <ChevronRight size={13} className="shrink-0 text-slate-400" />}
          <span className="truncate">📝 Mening eslatmalarim</span>
        </button>
        <button
          type="button"
          title="Yangi eslatma"
          onClick={() => {
            collapsedSections.remove(MY_ROOT_KEY)
            void addMine('')
          }}
          className="flex h-6 w-6 items-center justify-center rounded text-slate-400 hover:bg-slate-200 hover:text-brand-600"
        >
          <Plus size={15} />
        </button>
      </div>
      {myError && <div className="mb-1 rounded-md bg-red-50 p-2 text-[13px] text-[#d32f2f]">{myError}</div>}
      {!myTree && !myError && <Loader className="h-16" />}
      {myTree && (() => {
        const { pages, sections } = splitSections(buildTree(myNodes))
        const shown = isMember ? myExpanded.set : withAncestors(myExpanded.set)
        const rows = (items: typeof pages) => (
          <WikiTreeRows
            items={items}
            depth={0}
            expanded={shown}
            activeId={isMember ? undefined : pageId}
            onOpen={openMine}
            onToggle={togglePath(myExpanded, shown)}
            menuFor={myMenu}
          />
        )
        return (
          <>
            {myRootOpen() && (pages.length > 0
              ? rows(pages)
              : <div className="px-2 py-1 text-[12.5px] text-slate-400">Hozircha eslatma yo'q — «+» bilan boshlang</div>)}
            {sections.length > 0 && <div className="mt-2" />}
            {sections.map(({ node, children }) => {
              const locked = effectiveLocked(myNodes, node.id)
              return (
                <SectionBlock
                  key={node.id}
                  node={node}
                  open={sectionOpen(node.id)}
                  onToggle={() => toggleSection(node.id, sectionOpen(node.id))}
                  onAdd={locked ? undefined : () => void addMine(node.id)}
                  menu={myMenu(node)}
                  renaming={renamingId === node.id}
                  onRename={(name) => renameMySection(node.id, name)}
                  onRenameCancel={() => setRenamingId(null)}
                >
                  {children.length > 0
                    ? rows(children)
                    : <div className="px-2 py-1 text-[12.5px] text-slate-400">{locked ? 'Bo‘sh' : 'Bo‘sh — «+» bilan eslatma qo‘shing'}</div>}
                </SectionBlock>
              )
            })}
            <NewSectionButton onCreate={addMySection} />
          </>
        )
      })()}
    </div>
  )

  /** Xodimning daraxti: «Mening eslatmalarim» qismi, keyin uning bo'limlari; rahbarga — «Yangi bo'lim». */
  const memberTree = (uid: string, nodes: WikiTreeNode[]) => {
    const { pages, sections } = splitSections(buildTree(nodes))
    const shown = uid === userId ? withAncestors(memberExpanded.set) : memberExpanded.set
    const rows = (items: typeof pages) => (
      <WikiTreeRows
        items={items}
        depth={0}
        expanded={shown}
        activeId={uid === userId ? pageId : undefined}
        onOpen={(id) => openMember(uid, id)}
        onToggle={togglePath(memberExpanded, shown)}
        menuFor={memberMenu(uid)}
      />
    )
    return (
      <>
        {pages.length > 0 && rows(pages)}
        {pages.length === 0 && sections.length === 0 && <div className="px-2 py-0.5 text-[12px] text-slate-400">Hali yozmagan</div>}
        {sections.map(({ node, children }) => (
          <SectionBlock
            key={node.id}
            node={node}
            open={sectionOpen(node.id)}
            onToggle={() => toggleSection(node.id, sectionOpen(node.id))}
            onAdd={isLeader ? () => void addForMember(uid, node.id) : undefined}
            menu={memberMenu(uid)(node)}
            renaming={renamingId === node.id}
            onRename={(name) => renameMemberSection(uid, node.id, name)}
            onRenameCancel={() => setRenamingId(null)}
          >
            {children.length > 0 ? rows(children) : <div className="px-2 py-0.5 text-[12px] text-slate-400">Bo‘sh</div>}
          </SectionBlock>
        ))}
        {isLeader && <NewSectionButton compact onCreate={(name) => addMemberSection(uid, name)} />}
      </>
    )
  }

  const teamSection = canTeam && (
    <div className="border-t border-slate-100 pt-2">
      <div className="group flex h-8 items-center gap-1 px-1 text-[12.5px] font-semibold uppercase tracking-wide text-slate-500">
        <Users size={14} />
        <span className="flex-1">Xodimlar</span>
        {isLeader && (
          <button
            type="button"
            title="Kim nimani ko'radi (sozlama)"
            onClick={() => setSettingsOpen(true)}
            className="flex h-6 w-6 items-center justify-center rounded text-slate-400 hover:bg-slate-200"
          >
            <Settings2 size={14} />
          </button>
        )}
      </div>
      {teamError && <div className="rounded-md bg-red-50 p-2 text-[13px] text-[#d32f2f]">{teamError}</div>}
      {!team && !teamError && <Loader className="h-16" />}
      {team && team.members.length === 0 && (
        <div className="px-2 py-1 text-[12.5px] leading-snug text-slate-400">
          Ko'rinadigan xodim yo'q. Xodimlar «Topshiriqlar → Xodimlar» bo'limida biriktiriladi.
        </div>
      )}
      {team?.members.map((m) => {
        const open = memberOpen(m.userId)
        const tree = memberTrees[m.userId]
        return (
          <div key={m.userId}>
            <div className={cn('group flex h-8 items-center gap-1 rounded-md pr-1', m.userId === userId && !pageId ? 'bg-brand-50' : 'hover:bg-slate-100')}>
              <button
                type="button"
                onClick={() => toggleMember(m.userId)}
                className="flex h-5 w-5 shrink-0 items-center justify-center rounded text-slate-400 hover:bg-slate-200"
                aria-label={open ? 'Yopish' : 'Ochish'}
              >
                {open ? <ChevronDown size={14} /> : <ChevronRight size={14} />}
              </button>
              <Link to={`${TEAM_BASE}/${m.userId}`} className="flex min-w-0 flex-1 items-center gap-1.5 text-[13.5px] text-slate-700">
                <span className="flex h-5 w-5 shrink-0 items-center justify-center rounded-full bg-slate-100 text-[9.5px] font-bold text-slate-600">
                  {initials(m.fullName)}
                </span>
                <span className="truncate">{m.fullName}</span>
              </Link>
              <span className={cn('shrink-0 rounded-full px-1.5 text-[11px] font-semibold', m.noteCount ? 'bg-brand-50 text-brand-700' : 'text-slate-300')}>
                {m.noteCount}
              </span>
              {isLeader && (
                <button
                  type="button"
                  title={`${m.fullName}ga yangi sahifa qo'shish`}
                  onClick={() => void addForMember(m.userId, '')}
                  className="flex h-6 w-6 items-center justify-center rounded text-slate-400 opacity-0 hover:bg-slate-200 hover:text-brand-600 group-hover:opacity-100"
                >
                  <Plus size={14} />
                </button>
              )}
            </div>
            {open && (
              <div className="pl-3">
                {memberErrors[m.userId] && <div className="px-2 text-[12px] text-[#d32f2f]">{memberErrors[m.userId]}</div>}
                {!tree && !memberErrors[m.userId] && <Loader className="h-8" />}
                {tree && memberTree(m.userId, tree.pages)}
              </div>
            )}
          </div>
        )
      })}
    </div>
  )

  const sidebar = search.hits ? (
    <WikiSearchResults hits={search.hits} activeId={pageId} onOpen={openMine} error={search.error} />
  ) : (
    <>
      {mySection}
      {teamSection}
      {!canNotes && !canTeam && <div className="p-2 text-[13px] text-slate-400">Bu bo'limga ruxsatingiz yo'q.</div>}
      {canNotes && !canTeam && (
        <p className="mt-3 px-2 text-[11.5px] leading-snug text-slate-400">
          Eslatmalaringizni faqat siz yozasiz. Ularni rahbariyat va sizga biriktirilgan rahbar o'qiy oladi, hamkasblaringiz — yo'q.
        </p>
      )}
    </>
  )

  // ---------------------------------------------------------------- o'ng panel
  const overview = canTeam && team && team.members.length > 0 ? (
    <div className="flex-1 overflow-y-auto p-5">
      <div className="mb-1 flex items-center gap-2">
        <Users size={18} className="text-brand-600" />
        <h2 className="flex-1 text-[17px] font-bold text-slate-800">Xodimlar eslatmalari</h2>
      </div>
      <p className="mb-4 max-w-2xl text-[13px] text-slate-500">
        {isLeader
          ? "Xodimlarning shaxsiy insaytlari. Xohlagan sahifasini ⋯ menyusidan qulflashingiz (xodim faqat o'qiydi) yoki unga «+» bilan yangi sahifa qo'shishingiz mumkin."
          : "Sizga biriktirilgan xodimlarning eslatmalari — faqat o'qish uchun."}
      </p>
      <div className="grid grid-cols-1 gap-2.5 sm:grid-cols-2 xl:grid-cols-3">
        {team.members.map((m) => (
          <Link
            key={m.userId}
            to={`${TEAM_BASE}/${m.userId}`}
            className="flex items-center gap-3 rounded-xl border border-slate-200 p-3 transition-colors hover:border-brand-300 hover:bg-brand-50/40"
          >
            <span className="flex h-10 w-10 shrink-0 items-center justify-center rounded-full bg-brand-50 text-[13px] font-bold text-brand-700">
              {initials(m.fullName)}
            </span>
            <span className="min-w-0 flex-1">
              <span className="block truncate text-[14px] font-semibold text-slate-800">{m.fullName}</span>
              <span className="block truncate text-[12px] text-slate-500">{[m.position, HR_ROLE_LABELS[m.hrRole]].filter(Boolean).join(' · ')}</span>
              <span className="mt-0.5 block text-[12px] text-slate-400">
                {m.noteCount ? `${m.noteCount} ta eslatma · oxirgisi ${formatStamp(m.lastUpdatedAt)}` : 'Hali yozmagan'}
              </span>
            </span>
            <ChevronRight size={16} className="shrink-0 text-slate-300" />
          </Link>
        ))}
      </div>
    </div>
  ) : (
    <WikiEmptyState title="Shaxsiy eslatmalaringiz" hint="Insaytlar, ish rejasi, kunlik qaydlar — chapdagi «+» bilan yangi eslatma oching." />
  )

  const memberBreadcrumb = (page: WikiPage) => (
    <>
      <Link to={`${TEAM_BASE}/${userId}`} className="shrink-0 hover:text-brand-600">👤 {memberName(userId ?? '')}</Link>
      {crumbs(memberNodes, page.id, (id) => openMember(userId ?? '', id))}
    </>
  )

  /** Otalar zanjiri; BO'LIM — oddiy yozuv (sahifa sifatida ochilmaydi). */
  const crumbs = (nodes: WikiTreeNode[], id: string, open: (id: string) => void) =>
    ancestorIds(nodes, id).map((aid) => {
      const node = nodes.find((x) => x.id === aid)
      return (
        <span key={aid} className="flex min-w-0 items-center gap-1">
          <ChevronRight size={12} className="shrink-0" />
          {node?.isSection ? (
            <span className="truncate">{node.icon || '📁'} {node.title}</span>
          ) : (
            <button type="button" className="truncate hover:text-brand-600" onClick={() => open(aid)}>
              {node?.title}
            </button>
          )}
        </span>
      )
    })

  const myBreadcrumb = (page: WikiPage) => (
    <>
      <span className="shrink-0">📝 Mening eslatmalarim</span>
      {crumbs(myNodes, page.id, openMine)}
    </>
  )

  const memberActions = (page: WikiPage) => (
    <>
      {!editingMember && (
        <span className="flex shrink-0 items-center gap-1 text-[11.5px] text-emerald-600" title="Xodim yozayotgan bo'lsa, matn bir necha soniyada o'zi yangilanadi">
          <span className="h-1.5 w-1.5 animate-pulse rounded-full bg-emerald-500" /> Jonli
        </span>
      )}
      {isLeader && (
        <>
          <button
            type="button"
            onClick={() => (editingMember ? stopEditing() : startEditing(page))}
            className={cn(
              'flex h-7 shrink-0 items-center gap-1 rounded-md border px-2 text-[12px]',
              editingMember ? 'border-brand-300 bg-brand-50 text-brand-700' : 'border-slate-200 text-slate-600 hover:bg-slate-50',
            )}
          >
            <Pencil size={13} /> {editingMember ? 'Tahrirlashni tugatish' : 'Tahrirlash'}
          </button>
          <button
            type="button"
            onClick={() => void toggleMemberLock(userId ?? '', page.id, !page.locked)}
            className={cn(
              'flex h-7 shrink-0 items-center gap-1 rounded-md border px-2 text-[12px]',
              page.locked ? 'border-amber-300 bg-amber-50 text-amber-700' : 'border-slate-200 text-slate-600 hover:bg-slate-50',
            )}
            title={page.locked ? 'Qulfni ochish' : "Qulflash: xodim bu sahifani (ichidagilari bilan) faqat o'qiydi"}
          >
            {page.locked ? <Lock size={13} /> : <LockOpen size={13} />} {page.locked ? 'Qulflangan' : 'Qulflash'}
          </button>
        </>
      )}
    </>
  )

  let main
  if (pageId && isMember) {
    main = (
      <WikiPageView
        view={view}
        readOnly={() => !editingMember}
        readOnlyNote={(p) => `${memberName(userId ?? '')}ning eslatmasi — faqat o‘qish${p.effectiveLocked ? ' · qulflangan' : ''}`}
        breadcrumb={memberBreadcrumb}
        actions={memberActions}
        onBack={() => openMember(userId ?? '')}
      />
    )
  } else if (pageId) {
    main = (
      <WikiPageView
        view={view}
        readOnly={(p) => p.effectiveLocked}
        readOnlyNote={() => 'Rahbariyat qulflagan — faqat o‘qish'}
        breadcrumb={myBreadcrumb}
        onBack={() => navigate(basePath)}
      />
    )
  } else if (isMember) {
    main = (
      <WikiEmptyState
        title={memberName(userId ?? '')}
        hint={`${memberNodes.length} ta eslatma — chapdan tanlang.${isLeader ? ' Xodim nomi yonidagi «+» bilan unga yangi sahifa qo‘shishingiz mumkin.' : ''}`}
      />
    )
  } else if (mode === 'team') {
    main = overview
  } else {
    main = <WikiEmptyState title="Shaxsiy eslatmalaringiz" hint="Insaytlar, ish rejasi, kunlik qaydlar — chapdagi «+» bilan yangi eslatma oching." />
  }

  // Tab yo'q xodim «Xodimlar» manziliga (xatcho'p, eski havola) kelsa — o'z eslatmalariga.
  if (mode === 'team' && team && !teamTabVisible) {
    return <Navigate to="/admin/bilimlar/eslatmalar" replace />
  }

  return (
    <>
      <WikiShell
        title={showTabs ? undefined : 'Eslatmalarim'}
        showTabs={showTabs}
        showTeamTab={teamTabVisible}
        search={canNotes ? { value: search.q, onChange: search.setQ, placeholder: 'Eslatmalarimdan qidirish…' } : undefined}
        sidebar={sidebar}
        hasPage={!!pageId}
        heightClass={heightClass}
        main={main}
      />
      {settingsOpen && (
        <WikiSettingsModal
          onClose={() => setSettingsOpen(false)}
          onSaved={() => {
            setSettingsOpen(false)
            setTeamTick((t) => t + 1)
          }}
        />
      )}
    </>
  )
}
