import { useEffect } from 'react'
import { Navigate, Route, Routes } from 'react-router-dom'
import { getPublicBrand } from '@/api/services/settings'
import { AppLayout } from '@/components/layout/AppLayout'
import { ProtectedRoute, RootRedirect } from '@/components/auth/ProtectedRoute'
import { RequirePerm } from '@/components/auth/RequirePerm'
import { LoginPage } from '@/pages/LoginPage'
import { AdminDashboard } from '@/pages/admin/AdminDashboard'
import { CallCenterPage } from '@/pages/admin/calls/CallCenterPage'
import { LocalCallPage } from '@/pages/admin/calls/local/LocalCallPage'
import { LeadsPage } from '@/pages/admin/leads/LeadsPage'
import { CrmStatsPage } from '@/pages/admin/leads/CrmStatsPage'
import { StudentsPage } from '@/pages/admin/students/StudentsPage'
import { RetentionBonusPage } from '@/pages/admin/students/RetentionBonusPage'
import { StudentDetailPage } from '@/pages/admin/students/StudentDetailPage'
import { StudentTurnstilePage } from '@/pages/admin/students/StudentTurnstilePage'
import { StudentAbsencePage } from '@/pages/admin/students/StudentAbsencePage'
import { TeachersEntry } from '@/pages/admin/teachers/TeachersEntry'
import { TeacherDetailPage } from '@/pages/admin/teachers/TeacherDetailPage'
import { TeacherAttendancePage } from '@/pages/admin/teachers/TeacherAttendancePage'
import { SubstituteTeachersPage } from '@/pages/admin/teachers/SubstituteTeachersPage'
import { ClassesPage } from '@/pages/admin/classes/ClassesPage'
import { ClassDetailPage } from '@/pages/admin/classes/ClassDetailPage'
import { RoomsPage } from '@/pages/admin/rooms/RoomsPage'
import { RoomUtilizationPage } from '@/pages/admin/rooms/RoomUtilizationPage'
import { TeacherReportsPage } from '@/pages/admin/teacher-reports/TeacherReportsPage'
import { ContractsPage } from '@/pages/admin/contracts/ContractsPage'
import { BranchesPage } from '@/pages/admin/branches/BranchesPage'
import { StaffTasksPage } from '@/pages/admin/staff-tasks/StaffTasksPage'
import { StaffPage } from '@/pages/admin/staff/StaffPage'
import { FeedbackPage } from '@/pages/admin/feedback/FeedbackPage'
import { SubjectsPage } from '@/pages/admin/subjects/SubjectsPage'
import { CurriculaListPage } from '@/pages/admin/curricula/CurriculaListPage'
import { CurriculumModulesPage } from '@/pages/admin/curricula/CurriculumModulesPage'
import { CurriculumTopicsPage } from '@/pages/admin/curricula/CurriculumTopicsPage'
import { CurriculumLessonsPage } from '@/pages/admin/curricula/CurriculumLessonsPage'
import { CurriculumItemsPage } from '@/pages/admin/curricula/CurriculumItemsPage'
import { CurriculumItemEditorPage } from '@/pages/admin/curricula/CurriculumItemEditorPage'
import { ReasonsPage } from '@/pages/admin/reasons/ReasonsPage'
import { LandingCmsPage } from '@/pages/admin/landing/LandingCmsPage'
import { TestResultsPage } from '@/pages/admin/tests/TestResultsPage'
import { TestGroupPage } from '@/pages/admin/tests/TestGroupPage'
import { TestDetailPage } from '@/pages/admin/tests/TestDetailPage'
import { CertificateTemplatesPage } from '@/pages/admin/tests/CertificateTemplatesPage'
import { DistrictsPage } from '@/pages/admin/districts/DistrictsPage'
import { AiCheckPage } from '@/pages/admin/ai-check/AiCheckPage'
import { AiCheckStudentPage } from '@/pages/admin/ai-check/AiCheckStudentPage'
import { ArchivePage } from '@/pages/admin/archive/ArchivePage'
import { GradingCriteriaPage } from '@/pages/admin/grading/GradingCriteriaPage'
import { BookSalesPage } from '@/pages/admin/books/BookSalesPage'
import { LevelTestsPage } from '@/pages/admin/level-tests/LevelTestsPage'
import { FormsEntry } from '@/pages/admin/forms/FormsEntry'
import { FormEditorPage } from '@/pages/admin/forms/FormEditorPage'
import { FormStatsPage } from '@/pages/admin/forms/FormStatsPage'
import { PublicLeadFormPage } from '@/pages/public/PublicLeadFormPage'
import { LevelTestEditorPage } from '@/pages/admin/level-tests/LevelTestEditorPage'
import { LevelTestStatsPage } from '@/pages/admin/level-tests/LevelTestStatsPage'
import { SupportPage } from '@/pages/admin/support/SupportPage'
import { SupportDetailPage } from '@/pages/admin/support/SupportDetailPage'
import { PublicTestPage } from '@/pages/public/PublicTestPage'
import { VerifyCertificatePage } from '@/pages/public/VerifyCertificate'
import { PrivacyPolicyPage } from '@/pages/public/PrivacyPolicyPage'
import { DataDeletionPage } from '@/pages/public/DataDeletionPage'
import { PublicCertificatesPage } from '@/pages/public/PublicCertificatesPage'
import { MessagesPage } from '@/pages/admin/messages/MessagesPage'
import { GroupChatPage } from '@/pages/admin/chats/GroupChatPage'
import { SupportTelegramPage } from '@/pages/admin/messages/SupportTelegramPage'
import { LocationPage } from '@/pages/admin/locations/LocationPage'
import { CamerasPage } from '@/pages/admin/cameras/CamerasPage'
import { VacanciesPage } from '@/pages/admin/vacancies/VacanciesPage'
import { ParentsPage } from '@/pages/admin/parents/ParentsPage'
import { TeacherAppPage } from '@/pages/admin/parents/TeacherAppPage'
import { FinancePage } from '@/pages/admin/finance/FinancePage'
import { CashierPaymentsPage } from '@/pages/admin/finance/CashierPaymentsPage'
import { KassaPage } from '@/pages/admin/kassa/KassaPage'
import { KassaMyPaymentsPage } from '@/pages/admin/kassa/KassaMyPaymentsPage'
import { KassaMobileLayout } from '@/components/layout/KassaMobileLayout'
import { SettingsEntry } from '@/pages/admin/settings/SettingsEntry'
import { AuditLogPage } from '@/pages/admin/settings/AuditLogPage'
import { ContactQueuePage } from '@/pages/admin/students/contacts/ContactQueuePage'
import { StudentNotesPage } from '@/pages/admin/students/notes/StudentNotesPage'
import { FaceLoginPage } from '@/pages/admin/students/face/FaceLoginPage'
import { CourseAnalyticsPage } from '@/pages/admin/subjects/CourseAnalyticsPage'
import { AccountPage } from '@/pages/admin/account/AccountPage'
// Marketing — Instagram AI agenti (izoh/DM avtojavobi, lidga aylantirish)
import { InstagramDashboard } from '@/pages/admin/marketing/InstagramDashboard'
import { InstagramInbox } from '@/pages/admin/marketing/InstagramInbox'
import { InstagramRules } from '@/pages/admin/marketing/InstagramRules'
import { InstagramFaq } from '@/pages/admin/marketing/InstagramFaq'
import { InstagramKnowledge } from '@/pages/admin/marketing/InstagramKnowledge'
import { InstagramAnalytics } from '@/pages/admin/marketing/InstagramAnalytics'
import { InstagramAdLeads } from '@/pages/admin/marketing/InstagramAdLeads'
import { InstagramAdsStats } from '@/pages/admin/marketing/InstagramAdsStats'
// Kontent (post joylash) — bitta modal o'rniga ALOHIDA sahifalar. Sub-sahifalar
// sidebar nav'da EMAS: ular `ContentLayout` ichida tugma sifatida turadi (`MkSubnav`).
import { ContentLayout } from '@/pages/admin/marketing/content/ContentLayout'
import { ContentQueue } from '@/pages/admin/marketing/content/ContentQueue'
import { ContentPublished } from '@/pages/admin/marketing/content/ContentPublished'
import { ContentStatus } from '@/pages/admin/marketing/content/ContentStatus'
import { ContentComposer } from '@/pages/admin/marketing/content/ContentComposer'
import { InstagramQuality } from '@/pages/admin/marketing/InstagramQuality'
import { InstagramSettings } from '@/pages/admin/marketing/InstagramSettings'
// O'qituvchi portali (SPA ichida, /teacher/*)
import { TeacherDashboard } from '@/pages/teacher/TeacherDashboard'
import { TeacherGroupsPage } from '@/pages/teacher/groups/TeacherGroupsPage'
import { TeacherGroupDetailPage } from '@/pages/teacher/groups/TeacherGroupDetailPage'
import { TeacherMessagesPage } from '@/pages/teacher/messages/MessagesPage'
import { TeacherProfilePage } from '@/pages/teacher/TeacherProfilePage'
import { TeacherSupportPage } from '@/pages/teacher/support/SupportPage'
import { TeacherFeedbackPage } from '@/pages/teacher/feedback/FeedbackPage'
import { TeacherSalaryPage as TeacherOwnSalaryPage } from '@/pages/teacher/salary/SalaryPage'
import { TeacherAccountPage } from '@/pages/teacher/account/AccountPage'
import { TeacherRatingPage } from '@/pages/teacher/rating/TeacherRatingPage'
import { TeacherTestsPage } from '@/pages/teacher/tests/TeacherTestsPage'
import { TeacherMobileLayout } from '@/components/layout/TeacherMobileLayout'
// O'quvchi portali (SPA ichida, /student/*)
import { StudentMobileLayout } from '@/components/layout/StudentMobileLayout'
import { StudentDashboardScreen } from '@/pages/student/Dashboard'
import { StudentProgressScreen } from '@/pages/student/Progress'
import { SubjectProgressDetailScreen } from '@/pages/student/SubjectProgressDetail'
import { StudentGradesScreen } from '@/pages/student/Grades'
import { StudentAttendanceScreen } from '@/pages/student/Attendance'
import { StudentStatisticsScreen } from '@/pages/student/Statistics'
import { StudentChatScreen } from '@/pages/student/Chat'
import { StudentFinanceScreen } from '@/pages/student/Finance'
import { StudentFeedbackScreen } from '@/pages/student/Feedback'
import { StudentProfileScreen } from '@/pages/student/Profile'
import { StudentSettingsScreen } from '@/pages/student/Settings'
import { StudentLocationScreen } from '@/pages/student/Location'
import { StudentLessonScreen } from '@/pages/student/Lesson'
import { StudentGradingScreen } from '@/pages/student/Grading'
import { StudentAiCheckScreen } from '@/pages/student/AiCheck'
import { StudentSupportScreen } from '@/pages/student/Support'
import { StudentAccountScreen } from '@/pages/student/Account'
import { CertificatesPage } from '@/pages/student/Certificates'
import { StudentContractsScreen } from '@/pages/student/Contracts'

export default function App() {
  // Brauzer TAB'i — markaz brendingi: nom → sarlavha, logo → favicon (sozlangach avtomatik).
  useEffect(() => {
    getPublicBrand()
      .then((b) => {
        if (b.name) document.title = b.name
        if (b.logoUrl) {
          let link = document.querySelector<HTMLLinkElement>("link[rel='icon']")
          if (!link) {
            link = document.createElement('link')
            link.rel = 'icon'
            document.head.appendChild(link)
          }
          link.removeAttribute('type') // logo png/jpg bo'lishi mumkin — brauzer o'zi aniqlaydi
          link.href = b.logoUrl
          // iOS "Bosh ekranga qo'shish" ikonkasi ham markaz logosi bo'lsin (apple-touch-icon).
          let apple = document.querySelector<HTMLLinkElement>("link[rel='apple-touch-icon']")
          if (!apple) {
            apple = document.createElement('link')
            apple.rel = 'apple-touch-icon'
            document.head.appendChild(apple)
          }
          apple.href = b.logoUrl
        }
      })
      .catch(() => {})
  }, [])

  return (
    <Routes>
      {/* Ochiq sahifa */}
      <Route path="/login" element={<LoginPage />} />
      {/* Ommaviy daraja testi (autentifikatsiyasiz) — topshirilsa CRM'da lid bo'ladi */}
      <Route path="/test/invite/:token" element={<PublicTestPage />} />
      <Route path="/test/:slug" element={<PublicTestPage />} />
      {/* Ommaviy LID FORMASI (autentifikatsiyasiz) — ijtimoiy tarmoqdagi havola, to'ldirilsa lid bo'ladi */}
      <Route path="/forma/:slug" element={<PublicLeadFormPage />} />
      {/* Sertifikat tekshiruvi (autentifikatsiyasiz) */}
      <Route path="/verify-certificate/:id" element={<VerifyCertificatePage />} />
      {/* Maxfiylik siyosati (autentifikatsiyasiz) — Google Play / App Store uchun */}
      <Route path="/privacy" element={<PrivacyPolicyPage />} />
      <Route path="/privacy-policy" element={<Navigate to="/privacy" replace />} />
      {/* Ma'lumotni o'chirish — Meta App sozlamalaridagi «Data Deletion Instructions URL»
          MAJBURIY maydoni (Instagram moduli shusiz sozlanmaydi) va Google Play ham so'raydi.
          Manzil AYNAN shu bo'lishi kerak (`.claude/rules/marketing-instagram.md` §14).
          ⚠️ Sahifa ochiq, lekin hech qanday CRM ma'lumotini ko'rsatmaydi. */}
      <Route path="/data-deletion" element={<DataDeletionPage />} />
      <Route path="/data-deletion.html" element={<Navigate to="/data-deletion" replace />} />
      {/* Ommaviy sertifikatlar katalogi (React SPA) */}
      <Route path="/sertifikatlar" element={<PublicCertificatesPage />} />
      <Route path="/sertifikatlar.html" element={<Navigate to="/sertifikatlar" replace />} />

      <Route path="/" element={<RootRedirect />} />

      {/* Administrator paneli */}
      <Route element={<ProtectedRoute role="admin" />}>
        <Route path="/admin" element={<AppLayout />}>
          <Route index element={<AdminDashboard />} />
          {/* Marketing — Instagram AI agenti */}
          <Route path="marketing" element={<RequirePerm perm="marketing.dashboard"><InstagramDashboard /></RequirePerm>} />
          <Route path="marketing/inbox" element={<RequirePerm perm="marketing.inbox"><InstagramInbox /></RequirePerm>} />
          <Route path="marketing/rules" element={<RequirePerm perm="marketing.rules"><InstagramRules /></RequirePerm>} />
          {/* FAQ tugmalari (ice breakers) — qoidalar bilan BITTA ruxsat (`marketing.rules`):
              ikkalasi ham "tayyor javob" sozlamasi, yangi kalit ATAYIN ochilmagan. */}
          <Route path="marketing/faq" element={<RequirePerm perm="marketing.rules"><InstagramFaq /></RequirePerm>} />
          <Route path="marketing/knowledge" element={<RequirePerm perm="marketing.knowledge"><InstagramKnowledge /></RequirePerm>} />
          <Route path="marketing/analytics" element={<RequirePerm perm="marketing.analytics"><InstagramAnalytics /></RequirePerm>} />
          <Route path="marketing/reklama-lidlari" element={<RequirePerm perm="marketing.leadads"><InstagramAdLeads /></RequirePerm>} />
          <Route path="marketing/reklama-statistikasi" element={<RequirePerm perm="marketing.adsstats"><InstagramAdsStats /></RequirePerm>} />
          {/* Kontent — post joylash. Navbat/Joylanganlar/Holat BITTA layout ichida (sub-nav
              sahifa ichidagi tugmalar), post yaratish/tahrirlash esa TO'LIQ EKRANLI alohida
              sahifa (ilgari kichik modal oyna edi). */}
          <Route path="marketing/kontent" element={<RequirePerm perm="marketing.content"><ContentLayout /></RequirePerm>}>
            <Route index element={<ContentQueue />} />
            <Route path="joylangan" element={<ContentPublished />} />
            <Route path="holat" element={<ContentStatus />} />
          </Route>
          <Route path="marketing/kontent/yangi" element={<RequirePerm perm="marketing.content"><ContentComposer /></RequirePerm>} />
          <Route path="marketing/kontent/post/:id" element={<RequirePerm perm="marketing.content"><ContentComposer /></RequirePerm>} />
          <Route path="marketing/javob-sifati" element={<RequirePerm perm="marketing.quality"><InstagramQuality /></RequirePerm>} />
          <Route path="marketing/settings" element={<RequirePerm perm="marketing.settings"><InstagramSettings /></RequirePerm>} />
          <Route path="leads" element={<RequirePerm perm="leads.list"><LeadsPage /></RequirePerm>} />
          <Route path="calls" element={<RequirePerm perm="calls.cloud"><CallCenterPage /></RequirePerm>} />
          <Route path="calls/local" element={<RequirePerm perm="calls.local"><LocalCallPage /></RequirePerm>} />
          <Route path="crm-stats" element={<RequirePerm perm="leads.stats"><CrmStatsPage /></RequirePerm>} />
          <Route path="students" element={<RequirePerm perm="students.list"><StudentsPage /></RequirePerm>} />
          <Route path="students/turniket" element={<RequirePerm perm="students.turnstile"><StudentTurnstilePage /></RequirePerm>} />
          {/* Bog'lanish kerak — O'quvchilar bo'limi ICHIDA, lekin ruxsati alohida (`contacts`). */}
          <Route path="students/boglanish" element={<RequirePerm perm="contacts"><ContactQueuePage /></RequirePerm>} />
          {/* Izohlarga javoblar — profillarga yozilgan izohlar bir ro'yxatda. */}
          <Route path="students/izohlar" element={<RequirePerm perm="students.notes"><StudentNotesPage /></RequirePerm>} />
          <Route path="students/davomat" element={<RequirePerm perm="students.attendance"><StudentAbsencePage /></RequirePerm>} />
          <Route path="students/bonus" element={<RequirePerm perm="finance.bonus"><RetentionBonusPage /></RequirePerm>} />
          {/* Yuz bilan kirish — `students/:id` dan OLDIN turishi shart emas (statik yo'l dinamikdan
              ustun), lekin qolgan o'quvchi sahifalari bilan bir joyda tursin. */}
          <Route path="students/yuz" element={<RequirePerm perm="students.face"><FaceLoginPage /></RequirePerm>} />
          <Route path="students/:id" element={<RequirePerm perm="students.list"><StudentDetailPage /></RequirePerm>} />
          <Route path="teachers" element={<TeachersEntry />} />
          <Route path="teachers/substitutions" element={<RequirePerm perm="teachers.substitutions"><SubstituteTeachersPage /></RequirePerm>} />
          <Route path="teachers/:id" element={<RequirePerm perm="teachers.list"><TeacherDetailPage /></RequirePerm>} />
          <Route path="teachers/attendance" element={<RequirePerm perm="teachers.attendance"><TeacherAttendancePage /></RequirePerm>} />
          <Route path="classes" element={<RequirePerm perm="classes.list"><ClassesPage /></RequirePerm>} />
          <Route path="classes/:id" element={<RequirePerm perm="classes.list"><ClassDetailPage /></RequirePerm>} />
          <Route path="rooms" element={<RequirePerm perm="classes.rooms"><RoomsPage /></RequirePerm>} />
          <Route path="rooms/utilization" element={<RequirePerm perm="classes.rooms"><RoomUtilizationPage /></RequirePerm>} />
          <Route path="subjects" element={<RequirePerm perm="schedule.courses"><SubjectsPage /></RequirePerm>} />
          {/* Kurslar analitikasi — O'quv bo'limi ichida, "Kurslar" ruxsati (`schedule`) bilan. */}
          <Route path="subjects/analitika" element={<RequirePerm perm="schedule.analytics"><CourseAnalyticsPage /></RequirePerm>} />
          <Route path="curricula" element={<RequirePerm perm="schedule.curricula"><CurriculaListPage /></RequirePerm>} />
          <Route path="curricula/:curriculumId" element={<RequirePerm perm="schedule.curricula"><CurriculumModulesPage /></RequirePerm>} />
          <Route path="curricula/:curriculumId/:moduleId" element={<RequirePerm perm="schedule.curricula"><CurriculumTopicsPage /></RequirePerm>} />
          <Route path="curricula/:curriculumId/:moduleId/:topicId" element={<RequirePerm perm="schedule.curricula"><CurriculumLessonsPage /></RequirePerm>} />
          <Route path="curricula/:curriculumId/:moduleId/:topicId/:lessonId" element={<RequirePerm perm="schedule.curricula"><CurriculumItemsPage /></RequirePerm>} />
          <Route path="curricula/:curriculumId/:moduleId/:topicId/:lessonId/:itemId" element={<RequirePerm perm="schedule.curricula"><CurriculumItemEditorPage /></RequirePerm>} />
          <Route path="reasons" element={<RequirePerm perm="settings.reasons"><ReasonsPage /></RequirePerm>} />
          <Route path="test-results" element={<RequirePerm perm="classes.testResults"><TestResultsPage /></RequirePerm>} />
          <Route path="test-results/certificate-templates" element={<RequirePerm perm="classes.testResults"><CertificateTemplatesPage /></RequirePerm>} />
          <Route path="test-results/:groupId" element={<RequirePerm perm="classes.testResults"><TestGroupPage /></RequirePerm>} />
          <Route path="test-results/:groupId/tests/:testId" element={<RequirePerm perm="classes.testResults"><TestDetailPage /></RequirePerm>} />
          <Route path="districts" element={<RequirePerm perm="settings.districts"><DistrictsPage /></RequirePerm>} />
          <Route path="archive" element={<RequirePerm perm="settings.archive"><ArchivePage /></RequirePerm>} />
          <Route path="grading" element={<RequirePerm perm="schedule.grading"><GradingCriteriaPage /></RequirePerm>} />
          <Route path="books" element={<RequirePerm perm="books"><BookSalesPage /></RequirePerm>} />
          {/* Formalar — "Lid formalari" (`leads`) va "Daraja testlari" (`schedule`) bitta bo'limda.
              Ruxsatlari har xil bo'lgani uchun marshrutlar ham alohida darvozalangan. */}
          {/* `FormsEntry` — faqat `schedule` ruxsati bor xodimni daraja testlariga yo'naltiradi
              (aks holda u menyudan kelib "ruxsat yo'q" da qolib ketardi). */}
          <Route path="forms" element={<FormsEntry />} />
          <Route path="forms/statistika" element={<RequirePerm perm="leads.forms"><FormStatsPage /></RequirePerm>} />
          <Route path="forms/:id" element={<RequirePerm perm="leads.forms"><FormEditorPage /></RequirePerm>} />
          <Route path="level-tests" element={<RequirePerm perm="schedule.levelTests"><LevelTestsPage /></RequirePerm>} />
          <Route path="level-tests/stats" element={<RequirePerm perm="schedule.levelTests"><LevelTestStatsPage /></RequirePerm>} />
          <Route path="level-tests/:id" element={<RequirePerm perm="schedule.levelTests"><LevelTestEditorPage /></RequirePerm>} />
          <Route path="support" element={<RequirePerm perm="app.support"><SupportPage /></RequirePerm>} />
          <Route path="support/:id" element={<RequirePerm perm="app.support"><SupportDetailPage /></RequirePerm>} />
          <Route path="ai-check" element={<RequirePerm perm="app.aiCheck"><AiCheckPage /></RequirePerm>} />
          <Route path="ai-check/:studentId" element={<RequirePerm perm="app.aiCheck"><AiCheckStudentPage /></RequirePerm>} />
          <Route path="messages" element={<RequirePerm perm="messages.broadcast"><MessagesPage /></RequirePerm>} />
          {/* Chats — guruh chati "Xabarlar"dan ajratilgan alohida sahifa */}
          <Route path="chats" element={<RequirePerm perm="messages.chat"><GroupChatPage /></RequirePerm>} />
          <Route path="support-telegram" element={<RequirePerm perm="messages.support"><SupportTelegramPage /></RequirePerm>} />
          <Route path="teacher-reports" element={<RequirePerm perm="teacherReports"><TeacherReportsPage /></RequirePerm>} />
          <Route path="contracts" element={<RequirePerm perm="contracts"><ContractsPage /></RequirePerm>} />
          <Route path="locations" element={<RequirePerm perm="app.locations"><LocationPage /></RequirePerm>} />
          <Route path="parents" element={<RequirePerm perm="app.parents"><ParentsPage /></RequirePerm>} />
          <Route path="app/teachers" element={<RequirePerm perm="app.teachers"><TeacherAppPage /></RequirePerm>} />
          <Route path="kassa" element={<RequirePerm perm="kassa"><KassaPage /></RequirePerm>} />
          <Route path="finance" element={<RequirePerm perm="finance.main"><FinancePage /></RequirePerm>} />
          {/* Bitta kassir qabul qilgan to'lovlar — alohida sahifa (Moliya → Kassirlar qatoridan). */}
          <Route path="finance/cashiers/:key" element={<RequirePerm perm="finance.main"><CashierPaymentsPage /></RequirePerm>} />
          <Route path="settings" element={<Navigate to="/admin/settings/school" replace />} />
          <Route path="landing" element={<RequirePerm perm="settings.landing"><LandingCmsPage /></RequirePerm>} />
          {/* O'zgarishlar tarixi — Sozlamalar ICHIDA, lekin ruxsati boshqa (`audit`). Statik
              segment `settings/:section` dinamikasidan ustun turadi (React Router reyting), ya'ni
              bu marshrut `settings/:section` dan OLDIN yozilishi shart emas, lekin qo'shni tursin. */}
          <Route path="settings/history" element={<RequirePerm perm="audit"><AuditLogPage /></RequirePerm>} />
          <Route path="settings/:section" element={<SettingsEntry />} />
          <Route path="account" element={<AccountPage />} />

          {/* Boshqaruv */}
          <Route path="boshqaruv/vacancies" element={<RequirePerm perm="vacancies"><VacanciesPage /></RequirePerm>} />
          <Route path="boshqaruv/cameras" element={<RequirePerm perm="cameras"><CamerasPage /></RequirePerm>} />
          <Route path="boshqaruv/staff" element={<RequirePerm perm="staff"><StaffPage /></RequirePerm>} />
          <Route path="boshqaruv/feedback" element={<RequirePerm perm="feedback"><FeedbackPage /></RequirePerm>} />
          {/* Rollar endi "Xodimlar va rollar" sahifasiga birlashtirildi */}
          <Route path="boshqaruv/roles" element={<Navigate to="/admin/boshqaruv/staff" replace />} />
          <Route element={<ProtectedRoute role="superadmin" />}>
            <Route path="boshqaruv/branches" element={<BranchesPage />} />
            <Route path="boshqaruv/staff-tasks" element={<StaffTasksPage />} />
          </Route>
        </Route>
      </Route>

      {/* KASSA portali — TELEFON uchun (kassirning yagona ish o'rni: bosh sahifa/yon menyu YO'Q).
          Admin/superadmin ham kira oladi; kassa-only xodim login'dan keyin shu yerga tushadi. */}
      <Route element={<ProtectedRoute role="admin" />}>
        <Route path="/kassa" element={<RequirePerm perm="kassa"><KassaMobileLayout /></RequirePerm>}>
          <Route index element={<KassaPage />} />
          <Route path="payments" element={<KassaMyPaymentsPage />} />
        </Route>
      </Route>

      {/* O'qituvchi portali — MOBIL ilova qobig'i (telefon, Flutter WebView orqali).
          Admin Sidebar/Topbar O'RNIGA pastki tab navigatsiya (TeacherMobileLayout). */}
      <Route element={<ProtectedRoute role="teacher" />}>
        <Route path="/teacher" element={<TeacherMobileLayout />}>
          <Route index element={<TeacherDashboard />} />
          <Route path="journal" element={<RequirePerm perm="journal"><TeacherGroupsPage /></RequirePerm>} />
          <Route path="groups/:id" element={<RequirePerm perm="journal"><TeacherGroupDetailPage /></RequirePerm>} />
          <Route path="messages" element={<RequirePerm perm="messages"><TeacherMessagesPage /></RequirePerm>} />
          <Route path="feedback" element={<TeacherFeedbackPage />} />
          <Route path="support" element={<TeacherSupportPage />} />
          <Route path="salary" element={<TeacherOwnSalaryPage />} />
          <Route path="rating" element={<TeacherRatingPage />} />
          <Route path="tests" element={<RequirePerm perm="journal"><TeacherTestsPage /></RequirePerm>} />
          <Route path="account" element={<TeacherAccountPage />} />
          <Route path="profile" element={<TeacherProfilePage />} />
          <Route path="account" element={<AccountPage />} />
        </Route>
      </Route>

      {/* O'quvchi/ota-ona portali — MOBIL web ilova (student.html dizayni, blue).
          Pastki 5-tab navigatsiya (StudentMobileLayout). */}
      <Route element={<ProtectedRoute role="student" />}>
        <Route path="/student" element={<StudentMobileLayout />}>
          <Route index element={<StudentDashboardScreen />} />
          <Route path="progress" element={<StudentProgressScreen />} />
          <Route path="progress/subject/:id" element={<SubjectProgressDetailScreen />} />
          <Route path="grades" element={<StudentGradesScreen />} />
          <Route path="attendance" element={<StudentAttendanceScreen />} />
          <Route path="statistics" element={<StudentStatisticsScreen />} />
          <Route path="chat" element={<StudentChatScreen />} />
          <Route path="finance" element={<StudentFinanceScreen />} />
          <Route path="feedback" element={<StudentFeedbackScreen />} />
          <Route path="profile" element={<StudentProfileScreen />} />
          <Route path="settings" element={<StudentSettingsScreen />} />
          <Route path="location" element={<StudentLocationScreen />} />
          <Route path="lesson/:id" element={<StudentLessonScreen />} />
          <Route path="grading" element={<StudentGradingScreen />} />
          <Route path="ai-check" element={<StudentAiCheckScreen />} />
          <Route path="support" element={<StudentSupportScreen />} />
          <Route path="account" element={<StudentAccountScreen />} />
          <Route path="certificates" element={<CertificatesPage />} />
          <Route path="contracts" element={<StudentContractsScreen />} />
        </Route>
      </Route>

      <Route path="*" element={<RootRedirect />} />
    </Routes>
  )
}
