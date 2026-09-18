# edutizim UI inventory (reference for rebuilding the look)

Source: `https://wunderkindyaypan.edutizim.uz` (read-only walkthrough, 2026-09-18).
Structure only — no row data. UI labels are quoted exactly as shown (Uzbek).
Screenshots (contain PII, gitignored): `docs/edutizim/screens/`.

---

## 1. Design tokens

### Tech stack (as observed in the DOM)
- **MUI v5** (Emotion `css-*` classes) for almost everything: `MuiAppBar`, `MuiListItemButton`,
  `MuiButton`, `MuiIconButton`, `MuiOutlinedInput`, `MuiAutocomplete`, `MuiSelect`, `MuiTabs`,
  `MuiToggleButtonGroup`, `MuiCard`, `MuiBadge`, `MuiFab`, `MuiPagination`, `MuiChip`,
  **MUI X `DataGrid`** for list tables.
- **styled-components** (`sc-xxxx` classes) for layout wrappers.
- **react-bootstrap Popover/Tooltip** (`popover bs-popover-end`, `tooltip bs-tooltip-bottom`)
  for the sidebar flyouts and the "Tezkor bo'limlar" menu.
- **Tailwind** utility classes in some newer pieces (home schedule grid `<table>`, pagination
  footer: `flex items-center`, `dark:bg-gray-800`).
- **Icons: Tabler Icons (outline)** via `react-icons` (`Tb*`): `viewBox 0 0 24 24`,
  `stroke="currentColor"`, `fill="none"`, `stroke-width="2"`, round caps/joins (settings path
  `M10.325 4.317c.426 -1.756…` = `TbSettings`, graduation cap = `TbSchool`).
  Sidebar icons 20px; top-bar icons 20px; stat-card icons 28px.

### Typography
- Font family: **Nunito** everywhere (body 16px/24px, color `#212529`).
- Sidebar item label: 14px / 600, color `#333333`, line-height 21px, `noWrap`.
- Flyout (popover) item: 14px / 600, color `#333333`.
- Page title (`MuiTypography-h5`): **18px / 600**, `#000`.
- Stat card label (`caption`): 12px / 400, `#000`; stat value (`h5`): 18px / 600.
- DataGrid column header: **12px / 600, UPPERCASE**, `#000` (header cell 12px/700 `#333`).
- DataGrid cell: **13px / 500**, `#000`.
- Buttons (`MuiButton`): **12px / 500**, `text-transform: none`.
- Inputs: 13–14px / 400 (filter input text 13px; top-bar search 14px).
- Schedule grid: time column 13px/500 `#6B7280`; room header 14px/600 `#374151`;
  lesson card title 11px/700, lines 10px/400–600.
- "Umumiy soni:" counter pill: 13px / 500 `#333`.
- Badge: 10px/500 (sidebar), 12px/500 (top bar).

### Colors
| Token | Value |
|---|---|
| Primary | **`#3D68FF`** rgb(61,104,255) — buttons, active day tab, icons, links, FAB |
| Primary tint (icon-button bg) | `rgba(61,104,255,0.10)` + border `rgba(61,104,255,0.20)` |
| Page background (content area) | **`#F0F2F2`** rgb(240,242,242) |
| Card / surface | `#FFFFFF` |
| Card border | `1px solid #DBE0E6` rgb(219,224,230) |
| Card shadow | `0 1px 2px rgba(0,0,0,0.05)` |
| Top bar | `#FFF`, bottom border `1px #DBE0E6`, shadow `0 1px 2px rgba(0,0,0,0.04)` |
| Sidebar bg | `#FFFFFF` |
| Sidebar item hover / active bg | **`#E6EAEA`** rgb(230,234,234); text + icon turn primary |
| Sidebar active marker | 3px primary bar on the item's left edge (visible on hovered/active item) |
| Input border | `#DBE0E6` (top bar) / `rgba(0,0,0,0.23)` (filter inputs, MUI default) |
| DataGrid header bottom border | `#E0E0E0` |
| Unselected day tab bg | `#F0F2F2` |
| Badge (danger) | `#D32F2F` (MUI error) |
| Schedule grid time column bg | `#F9FAFB`, borders `#E5E7EB` |
| "Attendance not taken" row highlight (Guruhlar grid) | **`#FFFF04`** bright yellow (`custom-table-row-attendance`) |
| Text | `#000` (titles), `#333` (sidebar), `#212529` (body) |
| Muted text (kbd hint) | `#949494` |

**Stat-card icon tile colors (Home):** tile 40×40, radius 8px, white 28px icon.
| Card | Tile color |
|---|---|
| Buyurtmalar | `#00D66F` green |
| Birinchi darsga keladiganlar | `#3D68FF` primary |
| Yangi o'quvchilar | `#9747FF` purple |
| Aktiv o'quvchilar | `#00D66F` green |
| Buyurtmadan ketganlar | `#DE4141` red |
| Yangi o'quvchidan ketganlar | `#DE4141` red |
| Aktiv o'quvchidan ketganlar | `#DE4141` red |
| Qarzdorlar | `#1D1D1D` near-black |
| Guruhlar | `#019EF7` sky blue |
| Birinchi to'lovni qilganlar | `#FBC400` yellow |
| Muzlatilgan | `#019EF7` sky blue |
| Arxivlar | `#BDBDBD` grey |

### Radii, sizes, spacing
| Element | Value |
|---|---|
| Top bar height | **56px** (toolbar 55 + 1px border), right padding 6px |
| Sidebar width | **173px** expanded / **72px** collapsed; inner padding `0 5px`, list top padding 10px |
| Sidebar item | 163×45, radius **10px**, padding `8px 12px`, icon 20px (icon box 20px wide) |
| Content padding | `10px 15px` on the grey content area |
| Card radius | **12px** |
| Button (`MuiButton`, small) | height **31px**, padding `4px 12px`, radius **8px**, 20px leading icon |
| Icon button (tinted) | 30–32px square, radius 8px, padding 5px |
| Filter input | height **34px**, radius 8px |
| Top-bar search | 340×36, radius 8px, `⌘K` kbd chip (11px/600, radius 6, 1px border) |
| Branch select | 200×36, radius 8px |
| DataGrid | radius `12px 12px 0 0`, 1px border `#DBE0E6`, header row **56px**, body row **52px**, cell padding `0 10px`, density "standard" |
| Pagination item | 32×32, radius 8px, outlined |
| Day tabs (Home) | 32px high, radius 8px, padding `4px 20px`, group in a white 12px-radius card with 4px padding |
| Toggle button group | white box, radius 8px, padding 4px, each button 36×28 radius 8px; selected = primary bg + white icon |
| Stat card | 210×76, content padding `8px 8px 4px`, gap 6px, whole card clickable (cursor pointer) |
| Flyout popover | width 220px, radius **12px**, padding `6px 0`, border `1px #DBE0E6`, shadow `0 6px 20px -4px rgba(24,39,75,.08), 0 12px 48px -4px rgba(24,39,75,.10)`; items are `MuiMenuItem` 36px high, padding `4px 10px`, margin-top 4px |
| FAB | 48px circle, primary, MUI elevation-6 shadow |
| Avatar (top bar) | 38px circle, 2px `#DBE0E6` border, fallback bg `#ECF0FF` text primary |
| Badge | pill radius 10px, height 16 (sidebar) / 20 (top bar) |

---

## 2. Shell

Screenshots: `screens/home.jpg`, `screens/shell-sozlamalar-flyout.jpg`, `screens/shell-sidebar-collapsed.jpg`.

### Layout
- Fixed white top bar across the full width (56px). Sidebar (173px, white, full height below
  the logo) on the left. Content area to the right with grey `#F0F2F2` background.
- Logo "Edu tizim" (blue stacked-lines mark + wordmark, link to `/home`) sits in the top bar's
  left corner above the sidebar.

### Sidebar (top → bottom)
Every item is a `MuiListItemButton` (icon + label). Only **Topshiriqlar** has no flyout
(direct navigation, rendered as a clickable div, not `<a>`). Every other item opens a
**flyout popover to the right on hover** (Bootstrap popover, fade, rendered only while hovered);
the flyout items are `<a href>` links. Lidlar carries a red count badge on its icon (e.g. "2").
Hover/active item: bg `#E6EAEA`, label+icon primary, 3px primary bar at the left edge.

| Item | Icon (Tabler) | Flyout children → URL |
|---|---|---|
| Topshiriqlar | clipboard-check | — (direct link) |
| Lidlar (badge) | clipboard-list | Buyurtmalar ro'yxati → `/orders/order-list/table` · Birinchi darsga yozilganlar → `/orders/come-orders` |
| Guruh | users | Guruh → `/group/groups` · Barcha vazifalar → `/group/tasks` · Dars jadvali → `/group/class-schedule` · Xonalar → `/group/rooms` · Guruh o'quvchilari → `/group/group-students` |
| O'quvchilar | school (graduation cap) | Yangi o'quvchilar → `/orders/new-student-list` · Aktiv o'quvchilar → `/students/active` · Arxiv o'quvchilar → `/students/archive` · O'quvchilar ro'yxati → `/students/student-list` · Ota-ona → `/students/parent` · Joriy oyda obunasi tugaydiganlar → `/students/debt-risk` · O'quvchilar manzillari → `/students/locations` |
| Gamifikatsiya | trophy | Mahsulotlar → `/gamification/products` · Kategoriyalar → `/gamification/categories` · Buyurtmalar → `/gamification/orders` · Sabablar → `/gamification/reasons` · Coinlar tarixi → `/gamification/coin-history` · Coinlar hisoboti → `/gamification/coin-report` · Reyting ro'yxati → `/gamification/ranking-list` |
| O'quv bo'limi | book | Oflayn kurslar → `/course/courses` · Onlayn kurs → `/online-course` · Kategoriya → `/online-course-category` · Mavsumiy baholash → `/seasonal-assessment` · Shartnoma → `/course/contracts` |
| Moliya | wallet | Kassalar → `/finance/cash` · Bonus → `/finance/bonus` · Jarima → `/finance/penalty` · Oylik chiqarish → `/finance/salary` · Kirim chiqim → `/analytics/finance` · Tushum rejasi → `/analytics/income-plan` · Moliya analitikasi → `/analytics/financial-analytics` · Moliya hisobotlari → `/analytics/financial-reports` · Moliya hisobotlari (P&L) → `/finance/pnl-reports` · Pul oqimi → `/finance/cashflow` · Tranzaksiya turi → `/finance/payment-type` · Tranzakisyalar (sic) → `/finance/transactions` · Rejalashtirilgan xarajatlar → `/finance/planned-expense` · Shartnoma → `/finance/contract` |
| Nazorat | shield-check | Davomat → `/students/attendance` · Davomat analitikasi → `/analytics/attendance` · Fikr-mulohaza → `/feedback` · Xodimlar reytingi → `/hr/employees/ratings` · Davomat qilinmagan guruhlar → `/analytics/not-attended` · Filiallar holati → `/monitoring/statistics` |
| Boshqaruv | briefcase | Xodimlar → `/hr/employees` · Rollar → `/hr/roles` · Filiallar → `/branches` |
| Sotuv va marketing | speakerphone | Marketing → `/marketing/surveys` · Savdo plani → `/marketing/sales-plan` · Yangiliklar → `/news/news-list` · Hikoya → `/story/pagin` · SMS shablonlari → `/news/sms-template` · Xabarlar ro'yhati → `/news/sms` |
| Hisobotlar | chart-pie | Sotuv voronkasi → `/analytics/order` · Kirim chiqim → `/reports/analytics/finance` · Ketish sabablari → `/statistics` · Xonalar analitikasi → `/analytics/rooms` · O'quv markazga ishlab berilgan pul → `/analytics/earned-analytics` · Filiallar holati → `/reports/monitoring/statistics` · Xodimlar reytingi → `/reports/hr/employees/ratings` |
| Sozlamalar | settings | Umumiy sozlamalar → `/settings/system?status=general` · Moliya → `/settings/finance?status=partners` · O'quv → `/settings/study?status=reasons` · Sotuv va marketing → `/settings/sale-marketing?status=color-list` · Ilova sozlamalari → `/settings/app-settings?status=content` · Gamifikatsiya → `/settings/gamification` (Sozlamalar flyout items are click handlers, not `<a>`) |

Flyout: white card 220px wide, radius 12, soft double shadow, small arrow pointing to the
sidebar item, items stacked 36px high; hovered flyout item gets grey rounded bg + primary text.

**Collapse button:** 28×28 white rounded (r8) icon button with `<` chevron, floating on the
sidebar's right edge just under the top bar (x≈160, y≈60), 1px `#DBE0E6` border, shadow
`0 2px 8px rgba(0,0,0,.06)`. Click → sidebar collapses to **72px icon-only rail** (labels
hidden, items 62×40, flyouts still on hover), chevron flips to `>`; content wrapper class
switches `rightContentSmall` ↔ `rightContent`.

**Footer:** `sidebar-footer` strip (top border `#DBE0E6`, padding 6px) with a headset icon +
"TEXNIK YORDAM" (12px/600 uppercase, primary) linking to a Telegram support account.
An animated **robot chat-bot avatar** floats over the footer (bottom-left).

### Top bar (left → right)
| # | Element | Details |
|---|---|---|
| 1 | Logo | "Edu tizim" → `/home` |
| 2 | Back button | tinted icon button (`aria-label="Orqaga"`), arrow-back-up icon |
| 3 | Branch selector | `MuiAutocomplete` 200px, building icon prefix, value "Yaypan", caret; (not changed) |
| 4 | Global search | 340px outlined input, search icon, placeholder "Qidirish...", `⌘K` chip at right; focus = primary border; nothing opens until typing |
| 5 | Subscription pill | "Obuna tugashiga N kun qoldi" — primary gradient pill, radius 20, 14px/800 white, glow shadow `0 4px 15px rgba(102,126,234,.4)` |
| 6 | Language select | flag + "O'zb" + caret (`MuiSelect`, not opened) |
| 7 | "Mavzu" | moon icon — theme (dark mode) toggle (not clicked) |
| 8 | "Tug'ilgan kunlar" | calendar icon → navigates to `/hr/birthdays?type=monthly&month=9` (see below) |
| 9 | "Yangiliklar" | news icon → popover titled "Yangiliklar": list of product-update posts (unread ones with a left primary border) |
| 10 | "Qanday ishlaydi?" | video icon → popover titled "Qanday ishlaydi?": scrollable list of help-video titles per module |
| 11 | "Tezkor bo'limlar" | plus-circle icon → small dropdown: "Buyurtma yaratish", "Moliya bo'limi" |
| 12 | "Bildirishnomalar" | bell icon with red "99+" badge → popover "Bildirishnomalar" with a double-check (mark all read) icon; items: group name (bold, 2 lines) + time range on the right + grey line "Guruh uchun yoqlama qiling", green unread dot |
| 13 | Avatar | 38px → menu: header (avatar, full name, phone), "Aktiv qurilmalar" (monitor icon), "Qulflash" (lock icon), "Chiqish" (logout icon, red) |

All top-bar icon buttons: 32×32, radius 8, primary icon 20px, dark MUI tooltip below with the
label (e.g. "Tug'ilgan kunlar").

### Floating elements
- **Lightning FAB** bottom-right (48px primary circle, white bolt, `aria-label="UI version switcher"`);
  no tooltip on hover. Not clicked.
- **Robot chat-bot** bubble bottom-left over the sidebar footer. Not clicked.

### Tug'ilgan kunlar (reached from the top bar) `/hr/birthdays?type=monthly&month=9`
Page title "Tug'ilgan kunlar". Right side: year picker ("2026", calendar icon), month picker
("09"), segmented "Hammasi | O'quvchilar | Xodimlar" (selected = primary filled), segmented
"Oylik | Yillik". Body: 7-column month calendar (Dushanba … Yakshanba header row on grey),
each day a light-grey rounded cell with day number and white name chips (primary text links),
"+N Ko'proq" link when overflowing; today's number in a primary circle and white cell.

---

## 3. Home

### Home → Dars jadvali   `/home?fromHour=08:00&toHour=22:00&day=6&dayName=Juma&groupBy=room`
Page title: "Dars jadvali" (18px/600, top-left of content).
Header/actions (left→right, right-aligned): "Statistika" (contained primary, chart-bar icon) ·
"Filtr" (contained primary, filter icon). Below the cards, right-aligned: "Export" (contained
primary, file-export icon).
- **Statistika** = toggle: hides/shows the stat-card block (no panel).
- **Filtr** = toggle: reveals a right-aligned row of 5 compact selects (≈165px each, 34px, chevron):
  "O'qituvchi" · "Guruh" · "Xona" · "Kurs" · "Holati".

Summary widgets: 12 stat cards in a 6-column grid (2 rows), each white card r12 with a
40×40 colored icon tile (colors in §1) + caption label + bold number; whole card clickable:
Row 1: "Buyurtmalar" · "Birinchi darsga keladiganlar" · "Yangi o'quvchilar" · "Aktiv o'quvchilar" ·
"Buyurtmadan ketganlar" · "Yangi o'quvchidan ketganlar".
Row 2: "Aktiv o'quvchidan ketganlar" · "Qarzdorlar" · "Guruhlar" · "Birinchi to'lovni qilganlar" ·
"Muzlatilgan" · "Arxivlar".

Schedule card (white, r12):
- Left: day tabs "Yak | Du | Se | Chor | Pa | Ju | Sha" (grey pills, selected = primary filled
  white text) inside a bordered white box.
- Right: toggle group 1 — "Group by room" (building icon) / "Group by teacher" (user icon);
  toggle group 2 — "Grid view" (grid icon) / "List view" (rows icon); tinted icon buttons
  "Jadval ko'rinishi sozlamalari" (gear) and "Katta xolatda ko'rish" (maximize).
- Grid (plain `<table>`): first column = 30-minute time slots "07:00 - 07:30" … (grey bg
  `#F9FAFB`, 13px/500 grey text), columns = rooms ("1-bino 1-xona", truncated with "…") or
  teachers (by-teacher mode). Empty slots show a light "—". Lessons are absolutely positioned
  colored cards spanning their slots (r8, padding 8, shadow `0 4px 12px rgba(0,0,0,.15)`),
  background = the group's color (orange, mint, red, green…), text white or dark:
  line 1 time "08:00 - 10:00" (11px/700), then course (bold 10px), teacher, "Xona: …",
  "Toq kunlar", group name, bottom row book icon + "7/52" (lessons done / total).
- **List view**: axes swapped — time slots across the top, rooms/teachers down the left;
  lessons are horizontal bars with one-line text "Course • Teacher • Xona: … • Toq kunlar • Group"
  and "N/M" at the right end.
- Hover on a lesson → dark tooltip with key/value rows: "Guruh", "Xona", "Dars vaqti",
  "O'qituvchi", "Kurs", "Daraja", "Sana", "Boshlangan vaqti", "Tugash vaqti".
- Gear opens a **right drawer** "Jadval ko'rinishi sozlamalari" (subtitle "Har bir qator uchun
  maydon tanlang", close X): selects "1-sarlavha" (Dars vaqti) · "2-sarlavha" (Kurs nomi) ·
  "3-sarlavha" (O'qituvchi) · "1-qator" (Xona) · "2-qator" (Dars kunlari) · "3-qator" (Guruh nomi) ·
  "Pastki qator" (Darslar soni); "Saqlash" button bottom-right. Drawer ≈400px wide.
Pagination: none. Empty state: n/a.
Screenshots: `screens/home.jpg`, `screens/home-filter-open.jpg`, `screens/home-by-teacher.jpg`, `screens/home-list-view.jpg`

---

## 4. Lidlar

### Shared list-page pattern (used by almost every list page)
- **No page title** on list pages; the toolbar row is the first thing in the content area.
- Toolbar right side: tinted 30–32px icon buttons — `toggle-filters` (funnel; shows/hides the
  filter grid; pressed = darker tint), `customize-filter` (sliders-in-circle; dropdown titled
  "Sozlash" with a checkbox list of every filter to show/hide), `filter-appearance-settings`
  (adjustments icon; navigates to `/user-filter-settings/<table-key>?returnTo=…`), kebab `⋮`
  (toggles row-selection mode) — followed by the primary "… qo'shish" button (+ icon).
- **Filter grid**: 5 equal columns, 34px outlined inputs/autocompletes with a caret, white bg,
  gap ≈ 8px; placeholders act as labels (no floating labels).
- Above the grid, right-aligned counter pill "Umumiy soni: **N**" (white, border, r8, 13px/500).
- **MUI X DataGrid**: UPPERCASE 12px/600 headers, 52px rows, horizontal scroll, sticky header.
  Every column header has a hidden column menu ("<Col> ustun menyusi") and sortable ones a
  "Saralash" icon.
- **Footer** (bottom-right): outlined "≡ 50 qator" rows-per-page button + outlined MUI
  Pagination with first/prev/page/next/last.
- Empty: tray icon + "Ma'lumotlar topilmadi" + small grey "Ma'lumotlar topilmadi. Filterni o'zgartirib ko'ring."

**Filter appearance settings page** `/user-filter-settings/orders_pagin?returnTo=/orders/order-list/table`:
title "Filter" + small primary chip with the table name ("Buyurtmalar"); subtitle "Jadvaldagi
filtrlar qanday ko'rinishda chiqishini va qaysi tartibda joylashishini belgilang."; section
"Filter ko'rinishi" with 4 radio cards: "Statik" (Filtrlar yopiq holatda, tugma orqali ochiladi)
+ inner switch row "Filter ochiq bo'lsin"; "Yig'iladigan" (Barcha filtrlar bitta dropdown ichida);
"Ochiladigan" (Har bir filtr alohida dropdown ko'rinishda); "Modal (oynada)" (Filtrlar alohida
oynada ochiladi). Selected card = primary border + light-blue bg. "Saqlash" (disabled until change).

### Lidlar → Buyurtmalar ro'yxati   `/orders/order-list/table?limit=50&page=1&sortBy=_id&sortOrder=-1`
Page title: none.
Header/actions (left→right): view toggle group [Table view (list icon) | Kanban view (columns icon)]
· … right: toggle-filters · customize-filter · filter-appearance-settings · ⋮ · "Buyurtma qo'shish" (contained, +).
View toggles: Table / Kanban (`/orders/order-list/kanban`).
Filters (hidden until funnel clicked; order): "Qidiruv" (text) · "Sana" (date-range, calendar
icon) · "MM/DD/YYYY" (date) · "Holatlar" · "Kurs" · "Tanlang" (level, disabled until course) ·
"Guruh" · "O'qituvchi" · "Moderator" · "Status" · "Manba" · "Ichki manba" ·
"Qaysi filialdan o'tkazilgan" · "Qaysi filialga o'tkazilgan" · "Kun" · "So'rovnoma" · "Kategoriya"
(all autocompletes). customize-filter list also includes "Kelgan sana", "Ichki kurs".
Summary widgets: "Umumiy soni: N".
Table columns: № · ID (sortable) · O'QUVCHINI ISMI · TELEFON RAQAM · YARATILGAN SANASI (sortable)
· O'QITUVCHI · KURS · KURS DARAJASI · MODERATOR · IZOH. Name = text button (hover → primary
underline) that opens the **student profile** `/students/student-list/edit/:id`; phone shown in a
grey rounded pill (chip); moderator is a link to `/hr/employees/profile/:id`; date "dd.mm.yyyy | HH:mm".
Row click behaviour: only the name/moderator are clickable; row hover grey.
Pagination: "50 qator" + pagination.
Screenshot: screens/leads-table.jpg, screens/leads-table-filters.jpg

### Lidlar → Buyurtmalar (Kanban)   `/orders/order-list/kanban`
Header: view toggle · full-width search "Qidirish" · ⋮ · "Qo'shish" (contained +, → `/orders/add`).
Columns (pipeline stages, each 350px, grey `#F0F2F2` r8 column body): "ISHLANMAGAN BUYURTMA" ·
"YANGI LID" · "DASTLABKI ALOQA" · "TELEFONINI KO'TARMADI" · "MA'LUMOT BERILDI" · "O'YLAB KO'RADI" ·
"KONSULTATSIYA" · "WUNDERKINDDA AKTIV" · "BEKOR QILINDI". Header = bold UPPERCASE title + grey
11px "N Lidlar", then a 2px colored rule in the stage color (grey, yellow, red, …).
Cards: white r8, small shadow: line 1 "Name , Course" (course grey), line 2 moderator, divider,
footer "dd.mm.yyyy HH:mm" (grey) + right "Topshiriq yo'q" (orange `#ED6C02` 11px/500 + dot).
Cards are drag-and-drop (Atlassian pragmatic-dnd); click → `/orders/info/:id`.
Bottom-right: mini-map of all columns (grey blocks) for horizontal navigation; horizontal scrollbar.
Screenshot: screens/leads-kanban.jpg

### Lead / order detail   `/orders/info/:id`  (create = `/orders/add`, same layout)
Two-pane card layout:
- **Left pane (≈385px, white card)**: "Bosqichni tanlang" stage dropdown (bold, chevron) + kebab
  "Ko'proq amal" → menu with tinted-icon items "Guruhga qoshish", "Transfer", "SMS yuborish";
  thin grey progress bar under the stage. Tabs "Asosiy" | "Sozlamalar" (primary pill = selected).
  - Asosiy: section "Buyurtma ma'lumotlari" (clipboard icon tile): inline label/value rows
    "Mas'ul shaxs:" (select) · "Kurs*" (select, required red *) · "O'qituvchi:" (select) ·
    "Referal bergan o'quvchi:" (select) · "WET:" (small input) — custom fields appear here;
    divider; section "O'quvchi ma'lumotlari" (users icon): "Ism:" · "Familiya:" · "Telefon:".
    Empty values show "...". Bottom-left "Saqlash" (small contained).
  - Sozlamalar: "Buyurtma maydonlari" list (e.g. "WET") + outlined "+ Maydon qo'shish";
    "O'quvchi maydonlari" + "+ Maydon qo'shish" (custom field builder).
- **Right pane**: tab "Umumiy" (primary pill); activity timeline: centered date pill
  ("17.09.2026") on a horizontal rule, then event lines "dd.mm.yyyy HH:mm - <user> tomonidan
  buyurtma yaratildi" (grey 12px). Bottom bar (grey) with link "Eslatma:" (add a note).
Screenshot: screens/lead-detail.jpg, screens/lead-add.jpg

### Lidlar → Birinchi darsga yozilganlar   `/orders/come-orders?limit=50&page=1&sortBy=_id&sortOrder=-1`
Page title: none. Header/actions: none (no add button).
Filters (always visible, 5-col grid of 35px custom selects — styled-components, not MUI):
"Sanani tanlang" (date, calendar icon) · "Oraliqni tanlang" (date range, calendar icon) · "Kurs" ·
"Daraja" · "Ranglar bo'yicha" · "Kun" · "Toq/Juft kunlar" · "Moderator" · "O'qituvchi" ·
"Qidirish" (search icon prefix).
Summary widgets: "Umumiy soni: N".
Table columns: [checkbox] · № · ID · O'QUVCHINI ISMI · TELEFON RAQAM · YARATILGAN SANASI ·
BIRINCHI DARSGA KELISH SANASI · O'QITUVCHI · KURS · KURS DARAJASI · MODERATOR · IZOH.
Row selection checkboxes (bulk actions). Rows whose first-lesson date has passed are tinted
**`#FFCACA`** (class `custom-table-row-past`).
Pagination: "50 qator" + pagination.
Screenshot: screens/leads-come-orders.jpg

---

## 5. Guruh

### Guruh → Guruh   `/group/groups?limit=50&page=1&sortBy=_id&sortOrder=-1&state=active`
Page title: none.
Header/actions: left "Qo'shish" (contained +); right toggle-filters · customize-filter ·
filter-appearance-settings · ⋮.
Filters (visible by default here, 5-col grid): "Qidiruv" (text) · "Aktiv" (status autocomplete,
default Aktiv) · "O'qituvchi" · "Kurs" · "Tanlang" (level, disabled until course) · "Xona" · "Kun" ·
"Juft/toq kunlar" · "Dars vaqti" (time, clock icon prefix).
Summary: grey line under filters "Jami o'quvchilar soni: **N** Muzlatilgan o'quvchilar soni : **N**"
(13px grey, numbers bold) + "Umumiy soni: N" pill.
Table columns: № · GURUH NOMI (link → `/group/groups/details/:id`) · KURS · DARAJASI · KUN
("Toq kunlar"/"Juft kunlar") · DARS VAQTI ("07:00 - 09:00") · GURUH VAQTI (grey pill chip
"dd.mm.yyyy - dd.mm.yyyy") · O'QUVCHILAR (count) · O'QITUVCHI (link → employee profile) · XONA.
Every header has a column menu; "Saralash" sort icon.
Row highlight: rows of groups whose attendance is not taken today are **bright yellow `#FFFF04`**.
Row click: group name link. Pagination: "50 qator" + pagination.
Create modal (react-bootstrap modal, 500px, r12, backdrop rgba(0,0,0,.4)): title "Yangi guruh
qo'shish" (18px/600), sub "* Zarurligini bildiradi"; fields (label 15px/500 above, grey filled
36px inputs): "Guruh nomi*" (text) · "Guruh holati*" (select "Tanlang") · "Kurs*" (select) ·
"Ta'lim turi*" (select) · "Telegram guruh havolasi" (text) · "Boshlanish sanasi" (date dd/mm/yyyy) ·
"Bitkazish sanasi" (date). Footer right: "Orqaga" (text btn) · "Saqlash" (primary).
Screenshot: screens/groups-list.jpg, screens/group-add-modal.jpg

### Group detail   `/group/groups/details/:id?…&status=students|tasks|attach|history|attendance`
Layout: **left info card (≈350px)** + **right tabbed panel**.
Left card "Guruh ma'lumotlari" (title 16px/600 with grid icon; collapse-panel icon top-right).
Rows = icon + grey label left, bold value right:
"Guruh nomi" · "Daraja" · "Ta'lim turi" (green pill chip "offline") · "Xona / Platforma";
section "Dars jadvali": "Dars vaqti" (grey pill "08:00 - 10:00") · "Dars kunlari" (grey pills
"Dushanba" "Chorshanba" "Juma") · "Dars davomiyligi" ("2 soat");
section "Akademik ma'lumot": "Kurs / Fan" · "O'qituvchi" (short form "Firstname L.");
section "Guruh faoliyat muddati": "Boshlanish sanasi" · "Tugash kuni" (bold "2-September 2026").
Sections separated by 1px dividers. Card footer: "Guruhni arxivlash" (red `#E34A29` contained,
archive icon) · "Tahrirlash" (primary, pencil).
Right tabs (MuiTabs rendered as pill buttons with icons, selected = primary filled):
"O'quvchilar" · "Topshiriqlar" · "Mashg'ulot qo'shish" · "Guruh tarixi ma'lumotlari" · "Davomat".

**Tab O'quvchilar** (`status=students`): toolbar "O'quvchi qo'shish" (primary +) … right
"Arxiv o'quvchilar" + switch, search "Qidirish" (search icon), ⋮ menu → "Import".
Columns: [checkbox] · № · O'QUVCHINI ISMI (sortable; link → student profile; small green chip
"2 ta Kurs" under the name when enrolled in several courses) · QO'SHILGAN SANASI (sortable,
"dd.mm.yyyy | HH:mm") · TELEFON RAQAM · BALANS ("-10 000 UZS", negative allowed) · NARXI ·
COIN · OXIRGI IZOH ("—" when empty) · SERTIFIKAT FAYLINI YUKLANG (cloud-upload icon) ·
4 icon columns: "Coin qo'shish" (plus-circle) · "Oila va Yaqinlar" (users) · comment (message) ·
⋮ → opens an **inline floating icon toolbar over the row** (white pill, shadow) with tooltips:
"O'quvchini guruhdan muzlatish" (info-circle) · "Topshiriqlar" (list) · "Ma'lumot" (dots-circle) ·
comment · "Transfer" (arrows) · "Bitiruvchi" (graduation cap) · "O'quvchini tarixi" (history) ·
"Guruhdan chiqarish" (trash) · "Lidga qaytarish" (return arrow) + "X" to close.
Add-student modal: title "O'quvchini tanlang", one select "O'quvchini tanlang" (Tanlang), "Saqlash".
**Tab Topshiriqlar** (`status=tasks`): "Topshiriq qo'shish" (primary +), date-range chip
"01/09/26 - 18/09/26" with clear X, right list/grid view toggle. Columns: № · TURI · NOMI ·
TOPSHIRISH MUDDATI · O'QITUVCHI · GURUH · MAKSIMAL BALL · IZOH · YARATILGAN SANASI · [actions].
**Tab Mashg'ulot qo'shish** (`status=attach`): simple table № · MASHG'ULOT NOMI ·
YORDAMCHI O'QITUVCHILAR; empty "Ma'lumotlar topilmadi".
**Tab Guruh tarixi ma'lumotlari** (`status=history`): date range "01/09/26 - 18/09/26"; columns
№ · O'QUVCHILAR · MODERATOR · ESKI O'QITUVCHI · O'QITUVCHI · DARS SANASI · TURI (event text,
e.g. "O'quvchi guruhga qo'shildi", "O'quvchi ketdi").
**Tab Davomat** (`status=attendance&month=9&year=2026`): header "Guruh nomi" (small grey) +
group name (bold 16px) + "Export" (primary, download icon); segmented "Ism bo'yicha |
Qo'shilgan sana bo'yicha" + sort-direction icon button ("O'sish tartibida") + year select
("2026") + month select ("Sentyabr"). Section headers with a left primary bar:
"Aktiv o'quvchi" + count badge (primary pill), later "Arxivlangan o'quvchilar" + count.
Plain `<table>` (16px text, 49px rows, no vertical borders): № · Ism · Telefon raqam · Balans ·
To'lov sanasi · Davomatni bekor qilish (red tinted trash button) · one column per lesson
("1-dars" small primary label above "02.09") · … · O'rtacha baho · Izoh. Cells: green outlined
check-circle = attended, red minus-circle = absent, empty grey circle = not marked.
Edit modal "Guruhni tahrirlash": "Guruh nomi*" · "Guruh holati*" · "Kurs*" · "Kurs darajasi*" ·
"Dars kunini tanlang *" (e.g. "Toq kunlar") · "Boshlanish vaqti" · "Tugash vaqti" (time with X) ·
"O'qituvchi*" · "Yordamchi o'qituvchilar" (multi) · "Ta'lim turi*" · "Xona*" ·
"Telegram guruh havolasi" · "Boshlanish sanasi" · "Bitkazish sanasi"; "Orqaga" / "Saqlash".
Selects show a clear "×" and chevron.
Screenshots: screens/group-detail-students.jpg, group-detail-row-actions.jpg, group-detail-tasks.jpg,
group-detail-mashgulot.jpg, group-detail-history.jpg, group-detail-attendance.jpg, group-edit-modal.jpg

### Guruh → Barcha vazifalar   `/group/tasks?limit=50&page=1&sortBy=_id&sortOrder=-1`
Header/actions: "Imtihon qo'shish" (primary +). No filters.
Columns: № · TURI · NOMI · TOPSHIRISH MUDDATI · O'QITUVCHI · GURUH · MAKSIMAL BALL · IZOH · YARATILGAN SANASI · [actions].
Create modal (no title): "Turi*" (select, default "Imtihon") · "Nomi*" · "Topshirish muddati*"
(datetime-local) · "Maksimal ball*" (number) · "Izoh" (textarea) · "Fayl tanlang" (file); "Orqaga"/"Saqlash".
Empty state: tray icon + "Ma'lumotlar topilmadi" / "Ma'lumotlar topilmadi. Filterni o'zgartirib ko'ring."
Screenshot: screens/group-tasks.jpg

### Guruh → Dars jadvali   `/group/class-schedule?fromHour=08:00&toHour=22:00&day=6&dayName=Juma&groupBy=room`
Same schedule component as Home (§3) without the stat cards: top-right "Export" + "Filtr";
day tabs; room/teacher and grid/list toggles; gear; maximize.
Screenshot: screens/group-class-schedule.jpg

### Guruh → Xonalar   `/group/rooms?limit=50&page=1&sortBy=_id&sortOrder=-1`
Tabs (pill tabs in a white bar): "Xonalar" | "Analitika".
Xonalar: "Xona qo'shish" (primary +) … right search "Qidirish" + ⋮. Columns: № · SARLAVHA
(sortable) · O'QUVCHI SIG'IMI (sortable) · IZOH · JIHOZLAR SONI · TAXMINIY QIYMATI · MAS'UL SHAXS ·
actions: edit (pencil, primary) · print (printer) · tag (label) · delete (trash, red).
Create modal "Xona qo'shish": "Xona nomi" · "O'quvchi sig'imi" (number) · "Mas'ul shaxs" (select) · "Izoh"; "Orqaga"/"Saqlash".
Analitika: 4 KPI cards (tinted 40px icon tile, UPPERCASE 11px grey label, big bold number + small unit):
"JAMI XONALAR" (… xona, blue) · "JAMI JIHOZLAR SONI" (… dona, green) · "TA'MIRTALAB & SINGAN"
(orange) · "UMUMIY QIYMATI" (… UZS, red). Two cards: "Texnik holati bo'yicha" (donut + legend
"Alo" green / "Ta'mirtalab" orange / "Yaroqsiz / Singan" red; table Texnik holati · Soni · Taxminiy
qiymati with colored status chips) and "Xonalar bo'yicha" (bar chart; table Xonalar · Jihozlar soni ·
Taxminiy qiymati; empty "Jihozlar mavjud emas").
Screenshot: screens/group-rooms.jpg, screens/group-rooms-analytics.jpg

### Guruh → Guruh o'quvchilari   `/group/group-students?limit=50&page=1&sortBy=_id&sortOrder=-1`
Filters (right-aligned single row): switch "Muzlatilgan" · "O'qituvchi" (select) ·
"Guruh holati" (select) · "Oraliqni tanlang" (date range, calendar icon).
Columns: № · ID (sortable) · ISM · GURUHLAR · O'QITUVCHI · HOLATI (raw status text: "active", "new").
Pagination: "50 qator" + numbered pages "1 2 3 4 5 … 58" + next/last. "Umumiy soni: 2,863" (thousands comma).
Screenshot: screens/group-students.jpg

---

## 6. O'quvchilar

All student list pages share the list pattern from §4 (toolbar icons, 5-col filter grid,
DataGrid, "Umumiy soni", "50 qator" + numbered pagination "1 2 3 4 5 … N"). Most add a
**balance summary line** above the grid (left, 13px): red label "Qarzdor" + total
("-12 345 678 UZS", grey) · thin divider · green label "Haqdor" + total. Money in grid cells is
formatted "100,000" (list pages) or "100 000 UZS" (profile/group pages). Phone numbers render
in grey rounded pills. Row checkboxes enable bulk actions (not exercised).

### O'quvchilar → Yangi o'quvchilar   `/orders/new-student-list?limit=50&page=1&sortBy=_id&sortOrder=-1`
Header: toggle-filters · customize-filter · filter-appearance-settings · ⋮ (no add button).
Filters: "Qidiruv" · "Sana" (range) · "MM/DD/YYYY" · "Balans" (read-only popover range) · "Kurs" ·
"Guruh" · "Tanlang" (level) · "Manba" · "Ichki manba" · "Guruhlar soni" · "Kun" · "Juft/toq kunlar" ·
"Teglar" · "Status" · "Referral o'quvchilar" · "Moderator" · "O'qituvchi" · "Kategoriya" ·
"Oldingi holat" · "Ilova holati" · "Bloklangan" · "Oferta qabul qilingan".
Summary: "Qarzdor … | Haqdor …" + "Umumiy soni".
Columns: [chk] · № · ID^ · O'QUVCHI ISMI^ · TELEFON RAQAM · BALANS^ · GURUH · O'QITUVCHI ·
MODERATOR · ILOVANI YUKLAB OLISH SANASI^ · SHARTNOMA.
Screenshot: screens/students-new.jpg

### O'quvchilar → Aktiv o'quvchilar   `/students/active?limit=50&page=1&sortBy=_id&sortOrder=-1`
Header: toolbar icons only. Extra icon button at the right of the summary line (calendar icon,
tooltip "Haftaning ayni bir kunida keladigan o'quvchilar").
Filters: "Qidiruv" · "Sana" · "MM/DD/YYYY" · "Kurs" · "Guruh" · "Tanlang" · "Balans" · "Moderator" ·
"O'qituvchi" · "Kategoriya" · "Status" · "Manba" · "O'quvchi" · "Referral o'quvchilar" ·
"Guruhlar soni" · "Kun" · "Juft/toq kunlar" · "Teglar" · "Oldingi holat" · "Ilova holati" ·
"Tug'ilgan kun" (date) · "Muzlatilgan" · "Oferta qabul qilingan".
Columns: [chk] · № · O'QUVCHI ISMI^ · TELEFON RAQAM · BALANS^ · TO'LOV SANASI^ ·
YARATILGAN SANASI^ · MODERATOR · TAKLIF QILGANLARI^ · ILOVANI YUKLAB OLISH SANASI^.
Name → student profile. Screenshot: screens/students-active.jpg

### O'quvchilar → Arxiv o'quvchilar   `/students/archive?limit=50&page=1&sortBy=_id&sortOrder=-1`
Filters as Aktiv plus "Sabab" (archive reason). Summary "Qarzdor | Haqdor".
Columns: [chk] · № · ID^ · O'QUVCHINI ISMI^ · TELEFON RAQAM · BALANS^ · ARXIVLANGAN GURUH ·
ARXIV O'QITUVCHISI · YARATILGAN SANASI^ · MODERATOR.
Screenshot: screens/students-archive.jpg

### O'quvchilar → O'quvchilar ro'yxati   `/students/student-list?limit=50&page=1&sortBy=column_coins_number&sortOrder=-1`
Header: "O'quvchi qo'shish" (primary +) left; toolbar icons right.
Filters: "Qidiruv" · "Tug'ilgan kun" · "Sana" · "MM/DD/YYYY" · "Balans" · "Kurs" · "Guruh" ·
"Tanlang" · "Manba" · "Ichki manba" · "Moderator" · "O'qituvchi" · "O'quvchi" · "Kategoriya" ·
"Status" · "Guruhlar soni" · "Kun" · "Juft/toq kunlar" · "Ilova holati" · "Teglar" ·
"Referral o'quvchilar" · "Bloklangan" · "Oferta qabul qilingan" · "Oldingi holat" · "Shartnomasi bor".
Columns: [chk] · № · ID^ · ISM^ · COIN^ (active sort shows "↓" arrow next to header) · TELEFON RAQAM ·
BALANS^ · TO'LOV SANASI^ · YARATILGAN SANASI^ · MANBA · MODERATOR.
Create modal "Yangi o'quvchi qo'shish" (500px, scrolls): "* Zarurligini bildiradi"; "Ism*" ·
"Familiya" · "Otasining ismi" · "Telefon raqam" (flag country selector + "+998") ·
"Kategoriyani tanlang" (select) · "Tug'ilgan sanasi" (date) · "O'quvchining pul to'lash sanasi" (date) ·
"Marketing so'rovnomasi" (select) · checkbox "Qo'shimcha ma'lumotlar" (reveals extra fields) · "Saqlash".
Screenshot: screens/students-list.jpg, screens/student-add-modal.jpg

### O'quvchilar → Ota-ona   `/students/parent?limit=50&page=1&sortBy=_id&sortOrder=-1`
Filters: "Qidiruv" · "Sana" · "Balans" · "Kurs" · "Tanlang" · "O'qituvchi" · "Moderator" · "Kategoriya" ·
"Status" · "Holat" · "Ilova holati" · "Tug'ilgan kun".
Columns: [chk] · № · ID^ · O'QUVCHINI ISMI (link) · OTASINING ISMI · TELEFON RAQAM · ONASINING ISMI ·
TELEFON RAQAM · BALANS^ ("100 000 UZS") · OTASI ILOVANI YUKLAB OLISH SANASI^ ·
ONASI ILOVANI YUKLAB OLISH SANASI^ · [2 icon actions].
Screenshot: screens/students-parent.jpg

### O'quvchilar → Joriy oyda obunasi tugaydiganlar   `/students/debt-risk?limit=50&page=1&sortBy=_id&sortOrder=-1`
Top line (left, small): "Jami darslar narxi N UZS | Joriy balans N UZS | Kutilayotgan balans N UZS"
(bold labels, grey values); right: "Statusi" select.
Columns: № · O'QUVCHINI ISMI · TELEFON RAQAM · JAMI DARSLAR NARXI^ · JORIY BALANS^ ·
KUTILAYOTGAN BALANS^ · [action: small primary underlined link "Batafsil"].
Screenshot: screens/students-debt-risk.jpg

### STUDENT PROFILE   `/students/student-list/edit/:id`
Reached from any student-name link (lead list, group students, student lists).
Layout: **left profile card (≈280px, white)** + **right panel with tab pills + content card**.

Left card: 96px circular avatar (default cartoon avatar, grey ring) with a small camera
badge (change photo); full name (16px/600, centered); phone (grey 12px) + copy icon; row of 3
primary icon buttons: message (SMS) · "$" (tooltip "Balans" → navigates to
`/group/student-total-price/:id?fromDate&toDate`: date-range picker + table № · GURUH · XODIM ·
KURS · UMUMIY XARAJATLAR) · phone (tooltip "Qo'ng'iroq qilish").
Stat list (each: 32px light-blue tinted icon tile + grey label + bold value):
"Qolgan darslar soni" · "To'lash kerak" ("0 UZS") · "Balans" ("100 000 UZS") · "Coinlar soni".
Block "Balans amal qilish muddati" (calendar icon) + month pill "Sentyabr 2026", green progress
bar with "6/6", then a row of green circles with lesson day numbers (18 21 23 25 28 30).

Tabs (wrap to 3 rows; pill buttons 32px, radius 8, grey `#F0F2F2` bg, selected = primary white text):
"Tahrirlash" · "Parol o'rnatish" · "Moderatorni tahrirlash" · "Qo'ng'iroqlar tarixi" · "Guruh" ·
"Qarzdorlik limiti" · "Vazifa" · "Coin tarixi" · "Blok xolatini tekshirish" · "Tranzaksiyalar tarixi" ·
"Buyurtmalar" · "Harakatlar tarixi" · "LTV" · "SMS" · "Shartnoma biriktirish" · "Manzil" ·
"Shartnomalar" · "Ko'proq ⋮" (menu: "Sozlash" with gear icon).

- **Tahrirlash** (default): "* Zarurligini bildiradi"; 3-column form (labels 15px/500 above,
  grey filled 36px inputs `#F0F2F2`, border `#DBE0E6`, r8): "Ism*" · "Familiya" · "Telefon raqam"
  (flag + "+998") · "Teglar" (select) · "Elektron pochta" (placeholder "example@gmail.com") ·
  "Tug'ilgan sanasi" (date) · "Dars vaqti" (select "Dars shaklini tanlang") · "O'quvchi kategoriyasi"
  (select with ×) · "O'quvchining pul to'lash sanasi" (date) · "O'qish tili" (select) ·
  "Marketing so'rovnomasi" (select) · "Maqsadidagi universiteti" · "Otasining ismi" · "Telefon raqam" ·
  "Otasining ish joyi" · "Onasining ismi" · "Telefon raqam" · "Onasining ish joyi" · "Uy adresi" ·
  "O'qish joyi" · "Izoh". Footer: "Maxsus maydon qo'shish" · "O'chirish" (red `#E34A29`) ·
  "Orqaga" (outlined) · "Saqlash" (primary).
- **Parol o'rnatish**: segmented "O'quvchi | Ota-ona"; "Login" · "Parol" (eye toggle); "Saqlash".
- **Moderatorni tahrirlash**: "Moderator" select (with ×) + "Saqlash".
- **Qo'ng'iroqlar tarixi**: grid № · SANA · TURI · XODIM · DAVOMIYLIGI · AUDIO.
- **Guruh**: section header with left purple bar "Guruhlar" + count badge; group cards
  (white, 1px border, r8): group name bold + lavender chip with price ("10 000 UZS").
  Empty: "Guruhlar topilmadi".
- **Qarzdorlik limiti**: single input "Qarzdorlik limiti" + "Saqlash".
- **Vazifa**: link-button "Eslatma qo'shish"; status select ("Jarayonda"); empty card "Eslatmalar yo'q".
- **Coin tarixi**: grid № · KIM TOMONIDAN · TURI · MIQDORI · AVVALGI BALANS · YANGI BALANS · YARATILDI · SABABI · IZOH.
- **Tranzaksiyalar tarixi**: toolbar icons + filters "Sana" · "Tranzaksiya turi" · "Holat" · "Guruh";
  grid № · SANA ("dd.mm.yyyy | HH:mm") · MIQDORI · OLDINGI MIQDOR · KEYINGI MIQDOR · TRANZAKSIYA TURI
  ("Daromad") · TO'LOV TURI ("Naqd") · TRANZAKSIYA NOMI.
- **Buyurtmalar** (shop orders): grid № · SANA · O'QUVCHI · TELEFON RAQAM · UMUMIY MAHSULOTLAR · HOLATI · MAHSULOT NOMI · HARAKATLAR.
- **Harakatlar tarixi** (audit log): stacked white cards, each = actor name (bold) + date/time
  with calendar & clock icons, then a small table "Ism | Harakatlar | Xodim | Turi | Qurilma nomi |
  IP manzil"; "Harakatlar" cell shows "old → new" with a down arrow; Bootstrap pagination "‹ Previous … Next ›".
- **LTV**: table № · GURUH NOMI · GURUHDA O'QIGAN DAVRI · HOLATI · UMUMIY TO'LASHI KERAK BO'LGAN SUMMA ·
  UMUMIY TO'LAGAN SUMMA · QARZ · O'QITUVCHI UCHUN AJRATILGAN SUMMA; last row "Jami".
- **SMS**: grid № · YARATILGAN SANASI · MODERATOR · HOLATI · XABAR.
- **Shartnoma biriktirish**: left form ("Shartnoma turi" select + the student's fields pre-filled),
  right a SunEditor rich-text editor (toolbar: undo/redo, Font, Size, Formats, paragraph, quote,
  B/U/I/S, sub/sup, colors, align, lists, table, link, image, video, fullscreen, code view, preview, print).
- **Manzil**: two cards — "Manzil qo'shish" ("Manzil nomi" search "Manzil qidirish", "Manzil turi"
  select, "Xarita tanlash" with hint "Manzilni tanlash uchun xaritaga bosing" + Leaflet/OSM map with
  +/− zoom, "Qo'shish") and "Manzillar" (empty italic "Hozircha manzil qo'shilmagan").
- **Shartnomalar**: "Qo'shish" (primary, right); grid № · SHARTNOMA RAQAMI · SHARTNOMA TURI ·
  SHARTNOMA TURI · YARATILGAN SANA · YUKLAB OLISH · [actions].
- "Blok xolatini tekshirish" was not opened (may trigger a check).
Screenshots: screens/student-profile-edit.jpg, student-profile-groups.jpg, student-profile-transactions.jpg,
student-profile-actions-history.jpg, student-profile-ltv.jpg, student-total-price.jpg

---

## 7. Moliya

### Moliya → Kassalar   `/finance/cash?limit=50&page=1&sortBy=_id&sortOrder=-1&dateFrom=…&dateTo=…&cashboxId=:id`
Two-column layout, no page title.
**Left column (≈400px, white card, scrolls):** "Yangi kassa qo'shish" (full-width primary +) +
tinted eye-off icon button "Summalarni yashirish"; status select "Aktiv"; stack of **cashbox cards**
(396×112, r12): selected card = solid **`#095379`** (dark teal-navy) with white text — name,
amount ("123 456 789.6 UZS"), owner name, icon buttons edit (pencil) & crown in white outlined
circles, and a vertical action stack **"Kirim"** (green `#2E7D32`, +) · **"Chiqim"** (red `#E34A29`, −) ·
**"Ko'chirish"** (sky `#26ADF8`) + small "More" link. Unselected cards = same color at 50% opacity
(`rgba(9,83,121,.5)`), click selects.
**Right column:** payment-method summary cards (200×86, `#095379`, r12, white text): "Naqd" + total +
small "Ko'chirish" link; "Plastik" + total. Filters (3-col grid, 34px): date range
"18/09/26 - 18/09/26" (calendar icon, clear ×) · "To'lov sanasi" (date) · "Tranzaksiya" (select) ·
[⋮ tinted toggle for more filters] · "Tranzaksiya turi" · "O'quvchi" · "To'lov turi" · "O'qituvchi".
Sub-tabs (pill): "Tranzaksiya" (selected, primary) | "Ilova Orqali To'lov"; right side mini totals
"↙ 0 UZS" (in) and "↗ 0 UZS" (out) in bordered boxes + bell icon with "99+" badge.
Grid (Tranzaksiya): № · SANA ("dd.mm.yyyy | HH:mm") · KIM · IZOH (grey chip button "Izoh") ·
TRANZAKSIYA NOMI · MIQDORI ("1 000 000") · HOLATI (green `#349C00` text "Qabul qilindi") ·
TRANZAKSIYA TURI. Grid (Ilova Orqali To'lov): № · INTEGRATSIYA NOMI · YARATILGAN SANASI · MIQDORI ·
O'QUVCHINI ISMI · [actions].
**Payment drawers** (react-bootstrap Offcanvas, right, 400px; header bar primary `#3D68FF` 70px with
white back-arrow + title; body labels 15px/500, grey filled inputs; footer "Orqaga" (outlined) +
"Saqlash" (primary)):
- **Kirim** (= "to'lov qilish" / take payment): "Tranzaksiya" (select, default "O'quvchi to'ladi", ×) ·
  "O'qituvchini tanlang" · "O'quvchini tanlang" · "Qiymat" · "To'lov turi" · "Sanani tanlang"
  (date, default today) · "Izoh".
- **Chiqim**: "Tranzaksiya" · "O'quvchini tanlang" · "Qiymat" · "Oyni tanlang *" · "Umumiy summa" ·
  "To'lov turi" · "Sanani tanlang" · "Izoh".
- **Ko'chirish** (transfer between cashboxes): "Moliya bo'limi" (target cashbox) · "Qiymat" ·
  "To'lov turi" · "Sanani tanlang" · "Izoh".
Screenshot: screens/finance-cash.jpg, screens/finance-cash-kirim-drawer.jpg

### Moliya → Kirim chiqim   `/analytics/finance?type=payIn&limit=10&page=1&fromDate=…&toDate=…`
Tabs (pill group, left): "Kirim" | "Chiqim" | "Bonus" | "Jarima" (type=payIn/…).
Right filters: "Kassa" (select) · date range "01/09/26 - 18/09/26" · "To'lov turi" (select) · ⋮.
Left card: large **donut chart** (thick ring, category colors: Kirim = bright green `#00E01A`-ish;
Chiqim = yellow-lime, red, salmon, black…) with the total in the centre ("12 345 000 UZS", 18px/600)
and % labels on segments.
Right card: table № · Turlari · Summa (right-aligned); each row's text colored with its segment color;
footer row "Jami" + total. Rows clickable (drill-down).
Screenshot: screens/finance-kirim-chiqim.jpg, screens/finance-kirim-chiqim-chiqim.jpg

### Moliya → Tranzakisyalar   `/finance/transactions?limit=50&page=1&sortBy=_id&sortOrder=-1`
Filters (right-aligned single row, 34px): "Kassa" · "Turi" · "Holati" · "Oraliqni tanlang" (date range) · "O'quvchi".
Columns: № · SANA^ · O'QUVCHI (link → student profile) · MIQDORI^ ("100,000") · OLDINGI MIQDOR ·
KEYINGI MIQDOR · TRANZAKSIYA TURI (raw "payIn") · TRANZAKSIYA NOMI ("O'quvchi to'ladi") · TO'LOV TURI · GURUH.
Some rows tinted **`#FFB9B9`** (class `custom-row`, flagged/cancelled transaction).
Pagination numbered "1 2 3 4 5 … 69". "Umumiy soni: 3,423".
Screenshot: screens/finance-transactions.jpg

### Moliya → Oylik chiqarish   `/finance/salary?limit=50&page=1&sortBy=_id&sortOrder=-1`
Header: "Oylik chiqarish" (primary). Columns: № · OYLIK · DAVOMAT · DAVOMATDAN FOIZI · BONUS · AVANS ·
JARIMA · AKLADI · TO'LANMAGAN · SANA. Empty: "Ma'lumotlar topilmadi".
"Oylik chiqarish" → `/finance/salary/create`: grid with row checkboxes: [chk] · № · TO'LIQ ISMI ·
TELEFON RAQAM · ISH HAQI ("-1 000 000 UZS") · DAVOMAT · DAVOMATDAN FOIZI · BONUS · AVANS · JARIMA ·
AKLADI · TO'LANMAGAN (select employees then act; not exercised).
Screenshot: screens/finance-salary.jpg, screens/finance-salary-create.jpg

### Moliya → Bonus   `/finance/bonus?limit=50&page=1&sortBy=_id&sortOrder=-1`
Header: "Bonus yaratish" (primary +) · toolbar icons. Filters: "Bonus turi" · "O'quvchi" · "To'lov" (autocompletes).
Summary: "Umumiy bonuslar 0 UZS" (bold label, grey value) + "Umumiy soni".
Columns: № · BONUS TURI^ · TO'LIQ ISMI · KIM TOMONIDAN · OLDINGI MIQDOR^ · MIQDOR^ · KEYINGI MIQDOR^ · IZOH · SABABI · TRANZAKSIYA TURI.
Create = right Offcanvas (no header bar): "Tranzaksiya turi" (select) · "Qiymat" · "Izoh"; "Orqaga" / "Saqlash" (full-width pair).
Screenshot: screens/finance-bonus.jpg

### Moliya → Tranzaksiya turi   `/finance/payment-type`
White card, title "Tranzaksiya turi" (16px/600) + right "Tranzaksiya turini qo'shish" (primary +).
Filter chips (soft tinted pills): "Kirim" (green tint) · "Chiqim" (red tint) · "Vaucher" (blue tint) · "Jarima" (grey).
Tree list with dotted connector lines on the left; bordered rows: name · type ("Kirim") · edit (pencil) + delete (trash) primary icons.
Items seen: "O'quvchi to'ladi", "Kitob uchun", "Mock to'lov", "Oldingi oydagi qarzi", "Fond", "SAT oylik to'lovlari".
Screenshot: screens/finance-payment-type.jpg

### Moliya → Jarima   `/finance/penalty`
Header: "Jarima qo'shish" (primary +). "Umumiy soni: N".
Columns: № · TO'LIQ ISMI · OLDINGI MIQDOR · MIQDORI · KEYINGI MIQDOR · IZOH · SABABI ·
TRANZAKSIYA TURI · HOLATI. Empty: "Ma'lumotlar topilmadi. Filterni o'zgartirib ko'ring."
(Bonus sahifasining aynan ko'zgusi — faqat manfiy tomoni.)

### Moliya → Tushum rejasi   `/analytics/income-plan?type=active&dateString=…`
Header (left): date picker ("17/09/26") + status autocomplete chip "Aktiv ×"; under it the
period caption "01.09.2026 - 01.10.2026" (12px grey).
Single white table, 3 columns: TUSHUM REJASI · O'QUVCHI SONI · UMUMIY KUTILAYOTGAN SUMMA.
Rows (fixed, in this order): the picked date ("17/09/2026") · "Eski oydan qarzdor bo'lib o'tgan
o'quvchilar summasi" (negative) · "Eski oydan o'quvchilar to'lab o'tgan summa" · "Shu oyda
to'lagan summa" · "Qolgan kutilayotgan tushum" (last row has no student count).

### Moliya → Moliya analitikasi   `/analytics/financial-analytics`
Two panes. **Left (≈300px)**: a violet gradient card "Umumiy filiallar summasi" + big amount +
chevron; under it "Filiallar" list — one white row per branch (name + amount + ▶). Small
round icon button top-right of the pane (collapse).
**Right**: header "Kalendar" + "Barcha filiallar" select; year and month pickers; two toggle
icon buttons (calendar view | chart view). Month grid Dush…Yak: each day cell shows the day
number, a green "+N" delta when money came in, and the running balance at the bottom right
(grey, "77 045 628 UZS"). Empty cells for days outside the month.

### Moliya → Moliya hisobotlari   `/analytics/financial-reports?fromDate&toDate`
Filters: date range ("01/09/26 - 18/09/26" with ×) · "Kassa" select · "To'lov turi" select.
Three KPI cards: "Kirim" / "Chiqim" / "Qoldiq" — big amount, a small colored ring on the right,
and a ▼/▲ percentage vs the previous period (red/green).
Card "Grafik" (line chart, two lines "Kirim" green / "Chiqim" red, x = dates) with two toggle
icon buttons (line | bar).
Card "Tranzaksiya bo'yicha": radio "Kirim"/"Chiqim", donut with the total in the centre and
"N ta tranzaksiya" under it, legend below (per transaction type).
Bottom: two cards "Kirim" and "Chiqim", each with radios "Tranzaksiya turi" / "To'lov usuli" and
rows: label · horizontal share bar with the % inside · amount on the right.

### Moliya → Moliya hisobotlari (P&L)   `/finance/pnl-reports?year=`
Title "Moliya hisobotlari (P&L)"; right: year select ("2026") · "Oyni tanlang" · "Eksport" (primary).
Wide table: first column "Kategoriya", then one column per month (Yanvar … Dekabr), horizontally
scrollable. Rows: **"Jami daromad"** (green tinted row, bold) → "Dars bo'yicha daromad",
"Boshqa daromad"; **"Jami xarajat"** (red tinted, bold) → "Boshqa xarajat"; **"Sof foyda"**
(amber tinted, bold). Amounts "113 750 UZS", zeros as "0".

### Moliya → Pul oqimi   `/finance/cashflow?year=`
Title "Pul oqimi hisoboti"; right: year select · "Oyni tanlang" · "Eksport".
Same wide month-column table. Rows: "Boshlang'ich balans" (green tint) · section label
**"Operatsion faoliyat"** · "Kirim — Jami" (green tint, bold) + one row per income type
(O'quvchi to'ladi, Kitob uchun, Mock to'lov …) · "Chiqim — Jami" (red tint, bold) + one row per
expense type (Ona Famky, Arenda, Boshqa, Hodimga avans, Kommunal to'lovlar, "Adashib kiritilgan
pulni balansdan chiqarish", Internet va telefon, Hodimga oylik, Marketing, Uyga xarajatlar,
Vaucher, Marker …) · "Sof" (amber tint) · section **"Investitsion faoliyat"** with the same
three rows · (and a financing section below).

### Moliya → Rejalashtirilgan xarajatlar   `/finance/planned-expense`
Title "Rejalashtirilgan xarajatlar"; right "Qo'shish" (primary). "Umumiy soni: N".
Columns: № · NOMI · MIQDORI · TURI · BOSHLANISH SANASI · TUGASH SANASI · HOLATI · [actions].
Footer "50 qator". Empty state as everywhere.

### Moliya → Shartnoma   `/finance/contract?limit=50&page=1&stateName=Aktiv&state=active`
Header: "Shartnoma yaratish" (primary, left); filters right: status select ("Aktiv") ·
"Oraliqni tanlang" (date range) · "Guruh" · "O'quvchi". "Umumiy soni: N".
Columns: № · O'QUVCHI · BALANS · MODERATOR · MIQDORI · KUTILAYOTGAN TO'… · TO'LANGAN MIQD… ·
YARATILGAN SANA… · IZOH · [actions]. (Payment-plan contracts: expected vs paid amount.)

---

## 8. Skipped (out of scope per user)

Not catalogued — menu label → URL only:
- Gamifikatsiya: Mahsulotlar `/gamification/products` · Kategoriyalar `/gamification/categories` · Buyurtmalar `/gamification/orders` · Sabablar `/gamification/reasons` · Coinlar tarixi `/gamification/coin-history` · Coinlar hisoboti `/gamification/coin-report` · Reyting ro'yxati `/gamification/ranking-list`
- O'quv bo'limi: Oflayn kurslar `/course/courses` · Onlayn kurs `/online-course` · Kategoriya `/online-course-category` · Mavsumiy baholash `/seasonal-assessment` · Shartnoma `/course/contracts`
- O'quvchilar: O'quvchilar manzillari `/students/locations`
- Moliya: Jarima `/finance/penalty` · Tushum rejasi `/analytics/income-plan` · Moliya analitikasi `/analytics/financial-analytics` · Moliya hisobotlari `/analytics/financial-reports` · Moliya hisobotlari (P&L) `/finance/pnl-reports` · Pul oqimi `/finance/cashflow` · Rejalashtirilgan xarajatlar `/finance/planned-expense` · Shartnoma `/finance/contract`
- Nazorat: Davomat `/students/attendance` · Davomat analitikasi `/analytics/attendance` · Fikr-mulohaza `/feedback` · Xodimlar reytingi `/hr/employees/ratings` · Davomat qilinmagan guruhlar `/analytics/not-attended` · Filiallar holati `/monitoring/statistics`
- Boshqaruv: Xodimlar `/hr/employees` (+ employee profile `/hr/employees/profile/:id`) · Rollar `/hr/roles` · Filiallar `/branches`
- Sotuv va marketing: Marketing `/marketing/surveys` · Savdo plani `/marketing/sales-plan` · Yangiliklar `/news/news-list` · Hikoya `/story/pagin` · SMS shablonlari `/news/sms-template` · Xabarlar ro'yhati `/news/sms`
- Hisobotlar: Sotuv voronkasi `/analytics/order` · Kirim chiqim `/reports/analytics/finance` · Ketish sabablari `/statistics` · Xonalar analitikasi `/analytics/rooms` · O'quv markazga ishlab berilgan pul `/analytics/earned-analytics` · Filiallar holati `/reports/monitoring/statistics` · Xodimlar reytingi `/reports/hr/employees/ratings`
- Sozlamalar: Umumiy sozlamalar `/settings/system?status=general` · Moliya `/settings/finance?status=partners` · O'quv `/settings/study?status=reasons` · Sotuv va marketing `/settings/sale-marketing?status=color-list` · Ilova sozlamalari `/settings/app-settings?status=content` · Gamifikatsiya `/settings/gamification`.
  (Seen in passing, `screens/settings-general.jpg`: left white nav card with icon rows "Funksionallik" (selected, primary) · "Chek" · "Obuna" · "Bayram kunlari" · "Ommaviy oferta" · "Foydalanuvchi sozlamalari"; right a list of grey `#F0F2F2` r8 rows, each = 44px colored icon tile + bold title + grey subtitle + chevron: "Moliya sozlamalari", "Davomat sozlamalari", "O'qituvchi holatlari", "Xonalar holati", "Topshiriq boshqaruvi", "Foydalanuvchi nazorati", "Yon panel modullari", "Tizim va jadval sozlamalari".)

## 9. Not exercised on purpose (read-only rule)

- Student profile tab "Blok xolatini tekshirish" (might trigger a server check).
- "Export", "Import", "Guruhni arxivlash", attendance circles in the group Davomat tab, row
  checkboxes / bulk actions, "Arxiv o'quvchilar" and "Muzlatilgan" switches, language switcher,
  dark-mode toggle, lightning FAB (`aria-label="UI version switcher"`), chat-bot.
- Lead stage dropdown ("Bosqichni tanlang") — did not open on click; stage list = kanban columns (§4).

## 10. Pages that failed to load

None.
