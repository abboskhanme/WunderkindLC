import { lazy, Suspense, useEffect } from 'react'
import { Navigate, Route, Routes } from 'react-router-dom'
import { getPublicBrand } from '@/api/services/settings'
import { AppLayout } from '@/components/layout/AppLayout'
import { ProtectedRoute, RootRedirect } from '@/components/auth/ProtectedRoute'
import { RequirePerm } from '@/components/auth/RequirePerm'
import { KassaMobileLayout } from '@/components/layout/KassaMobileLayout'
import { TeacherMobileLayout } from '@/components/layout/TeacherMobileLayout'
import { StudentMobileLayout } from '@/components/layout/StudentMobileLayout'
import { Loader } from '@/components/ui/Loader'

// Sahifalar LAZY yuklanadi (route-level code splitting) — boshlang'ich bundle kichik qoladi,
// har bir sahifa o'z chunk'ida faqat ochilganda yuklanadi.
const LoginPage = lazy(() => import('@/pages/LoginPage').then((m) => ({ default: m.LoginPage })))
const AdminDashboard = lazy(() => import('@/pages/admin/AdminDashboard').then((m) => ({ default: m.AdminDashboard })))
const CallCenterPage = lazy(() => import('@/pages/admin/calls/CallCenterPage').then((m) => ({ default: m.CallCenterPage })))
const LocalCallPage = lazy(() => import('@/pages/admin/calls/local/LocalCallPage').then((m) => ({ default: m.LocalCallPage })))
const LeadsPage = lazy(() => import('@/pages/admin/leads/LeadsPage').then((m) => ({ default: m.LeadsPage })))
const CrmStatsPage = lazy(() => import('@/pages/admin/leads/CrmStatsPage').then((m) => ({ default: m.CrmStatsPage })))
const StudentsPage = lazy(() => import('@/pages/admin/students/StudentsPage').then((m) => ({ default: m.StudentsPage })))
const RetentionBonusPage = lazy(() => import('@/pages/admin/students/RetentionBonusPage').then((m) => ({ default: m.RetentionBonusPage })))
const StudentDetailPage = lazy(() => import('@/pages/admin/students/StudentDetailPage').then((m) => ({ default: m.StudentDetailPage })))
const StudentTurnstilePage = lazy(() => import('@/pages/admin/students/StudentTurnstilePage').then((m) => ({ default: m.StudentTurnstilePage })))
const StudentAbsencePage = lazy(() => import('@/pages/admin/students/StudentAbsencePage').then((m) => ({ default: m.StudentAbsencePage })))
const TeachersEntry = lazy(() => import('@/pages/admin/teachers/TeachersEntry').then((m) => ({ default: m.TeachersEntry })))
const TeacherDetailPage = lazy(() => import('@/pages/admin/teachers/TeacherDetailPage').then((m) => ({ default: m.TeacherDetailPage })))
const TeacherAttendancePage = lazy(() => import('@/pages/admin/teachers/TeacherAttendancePage').then((m) => ({ default: m.TeacherAttendancePage })))
const SubstituteTeachersPage = lazy(() => import('@/pages/admin/teachers/SubstituteTeachersPage').then((m) => ({ default: m.SubstituteTeachersPage })))
const ClassesPage = lazy(() => import('@/pages/admin/classes/ClassesPage').then((m) => ({ default: m.ClassesPage })))
const ClassDetailPage = lazy(() => import('@/pages/admin/classes/ClassDetailPage').then((m) => ({ default: m.ClassDetailPage })))
const RoomsPage = lazy(() => import('@/pages/admin/rooms/RoomsPage').then((m) => ({ default: m.RoomsPage })))
const RoomUtilizationPage = lazy(() => import('@/pages/admin/rooms/RoomUtilizationPage').then((m) => ({ default: m.RoomUtilizationPage })))
const TeacherReportsPage = lazy(() => import('@/pages/admin/teacher-reports/TeacherReportsPage').then((m) => ({ default: m.TeacherReportsPage })))
const ContractsPage = lazy(() => import('@/pages/admin/contracts/ContractsPage').then((m) => ({ default: m.ContractsPage })))
const BranchesPage = lazy(() => import('@/pages/admin/branches/BranchesPage').then((m) => ({ default: m.BranchesPage })))
const StaffTasksPage = lazy(() => import('@/pages/admin/staff-tasks/StaffTasksPage').then((m) => ({ default: m.StaffTasksPage })))
const StaffPage = lazy(() => import('@/pages/admin/staff/StaffPage').then((m) => ({ default: m.StaffPage })))
const FeedbackPage = lazy(() => import('@/pages/admin/feedback/FeedbackPage').then((m) => ({ default: m.FeedbackPage })))
const SubjectsPage = lazy(() => import('@/pages/admin/subjects/SubjectsPage').then((m) => ({ default: m.SubjectsPage })))
const CurriculaListPage = lazy(() => import('@/pages/admin/curricula/CurriculaListPage').then((m) => ({ default: m.CurriculaListPage })))
const CurriculumModulesPage = lazy(() => import('@/pages/admin/curricula/CurriculumModulesPage').then((m) => ({ default: m.CurriculumModulesPage })))
const CurriculumTopicsPage = lazy(() => import('@/pages/admin/curricula/CurriculumTopicsPage').then((m) => ({ default: m.CurriculumTopicsPage })))
const CurriculumLessonsPage = lazy(() => import('@/pages/admin/curricula/CurriculumLessonsPage').then((m) => ({ default: m.CurriculumLessonsPage })))
const CurriculumItemsPage = lazy(() => import('@/pages/admin/curricula/CurriculumItemsPage').then((m) => ({ default: m.CurriculumItemsPage })))
const CurriculumItemEditorPage = lazy(() => import('@/pages/admin/curricula/CurriculumItemEditorPage').then((m) => ({ default: m.CurriculumItemEditorPage })))
const ReasonsPage = lazy(() => import('@/pages/admin/reasons/ReasonsPage').then((m) => ({ default: m.ReasonsPage })))
const LandingCmsPage = lazy(() => import('@/pages/admin/landing/LandingCmsPage').then((m) => ({ default: m.LandingCmsPage })))
const TestResultsPage = lazy(() => import('@/pages/admin/tests/TestResultsPage').then((m) => ({ default: m.TestResultsPage })))
const TestGroupPage = lazy(() => import('@/pages/admin/tests/TestGroupPage').then((m) => ({ default: m.TestGroupPage })))
const TestDetailPage = lazy(() => import('@/pages/admin/tests/TestDetailPage').then((m) => ({ default: m.TestDetailPage })))
const CertificateTemplatesPage = lazy(() => import('@/pages/admin/tests/CertificateTemplatesPage').then((m) => ({ default: m.CertificateTemplatesPage })))
const DistrictsPage = lazy(() => import('@/pages/admin/districts/DistrictsPage').then((m) => ({ default: m.DistrictsPage })))
const AiCheckPage = lazy(() => import('@/pages/admin/ai-check/AiCheckPage').then((m) => ({ default: m.AiCheckPage })))
const AiCheckStudentPage = lazy(() => import('@/pages/admin/ai-check/AiCheckStudentPage').then((m) => ({ default: m.AiCheckStudentPage })))
const ArchivePage = lazy(() => import('@/pages/admin/archive/ArchivePage').then((m) => ({ default: m.ArchivePage })))
const GradingCriteriaPage = lazy(() => import('@/pages/admin/grading/GradingCriteriaPage').then((m) => ({ default: m.GradingCriteriaPage })))
const BookSalesPage = lazy(() => import('@/pages/admin/books/BookSalesPage').then((m) => ({ default: m.BookSalesPage })))
const LevelTestsPage = lazy(() => import('@/pages/admin/level-tests/LevelTestsPage').then((m) => ({ default: m.LevelTestsPage })))
const FormsEntry = lazy(() => import('@/pages/admin/forms/FormsEntry').then((m) => ({ default: m.FormsEntry })))
const FormEditorPage = lazy(() => import('@/pages/admin/forms/FormEditorPage').then((m) => ({ default: m.FormEditorPage })))
const FormStatsPage = lazy(() => import('@/pages/admin/forms/FormStatsPage').then((m) => ({ default: m.FormStatsPage })))
const PublicLeadFormPage = lazy(() => import('@/pages/public/PublicLeadFormPage').then((m) => ({ default: m.PublicLeadFormPage })))
const LevelTestEditorPage = lazy(() => import('@/pages/admin/level-tests/LevelTestEditorPage').then((m) => ({ default: m.LevelTestEditorPage })))
const LevelTestStatsPage = lazy(() => import('@/pages/admin/level-tests/LevelTestStatsPage').then((m) => ({ default: m.LevelTestStatsPage })))
const SupportPage = lazy(() => import('@/pages/admin/support/SupportPage').then((m) => ({ default: m.SupportPage })))
const SupportDetailPage = lazy(() => import('@/pages/admin/support/SupportDetailPage').then((m) => ({ default: m.SupportDetailPage })))
const PublicTestPage = lazy(() => import('@/pages/public/PublicTestPage').then((m) => ({ default: m.PublicTestPage })))
const VerifyCertificatePage = lazy(() => import('@/pages/public/VerifyCertificate').then((m) => ({ default: m.VerifyCertificatePage })))
const PrivacyPolicyPage = lazy(() => import('@/pages/public/PrivacyPolicyPage').then((m) => ({ default: m.PrivacyPolicyPage })))
const DataDeletionPage = lazy(() => import('@/pages/public/DataDeletionPage').then((m) => ({ default: m.DataDeletionPage })))
const PublicCertificatesPage = lazy(() => import('@/pages/public/PublicCertificatesPage').then((m) => ({ default: m.PublicCertificatesPage })))
const MessagesPage = lazy(() => import('@/pages/admin/messages/MessagesPage').then((m) => ({ default: m.MessagesPage })))
const GroupChatPage = lazy(() => import('@/pages/admin/chats/GroupChatPage').then((m) => ({ default: m.GroupChatPage })))
const SupportTelegramPage = lazy(() => import('@/pages/admin/messages/SupportTelegramPage').then((m) => ({ default: m.SupportTelegramPage })))
const LocationPage = lazy(() => import('@/pages/admin/locations/LocationPage').then((m) => ({ default: m.LocationPage })))
const CamerasPage = lazy(() => import('@/pages/admin/cameras/CamerasPage').then((m) => ({ default: m.CamerasPage })))
const VacanciesPage = lazy(() => import('@/pages/admin/vacancies/VacanciesPage').then((m) => ({ default: m.VacanciesPage })))
const ParentsPage = lazy(() => import('@/pages/admin/parents/ParentsPage').then((m) => ({ default: m.ParentsPage })))
const TeacherAppPage = lazy(() => import('@/pages/admin/parents/TeacherAppPage').then((m) => ({ default: m.TeacherAppPage })))
const FinancePage = lazy(() => import('@/pages/admin/finance/FinancePage').then((m) => ({ default: m.FinancePage })))
const CashierPaymentsPage = lazy(() => import('@/pages/admin/finance/CashierPaymentsPage').then((m) => ({ default: m.CashierPaymentsPage })))
const KassaPage = lazy(() => import('@/pages/admin/kassa/KassaPage').then((m) => ({ default: m.KassaPage })))
const KassaMyPaymentsPage = lazy(() => import('@/pages/admin/kassa/KassaMyPaymentsPage').then((m) => ({ default: m.KassaMyPaymentsPage })))
const SettingsEntry = lazy(() => import('@/pages/admin/settings/SettingsEntry').then((m) => ({ default: m.SettingsEntry })))
const AuditLogPage = lazy(() => import('@/pages/admin/settings/AuditLogPage').then((m) => ({ default: m.AuditLogPage })))
const ContactQueuePage = lazy(() => import('@/pages/admin/students/contacts/ContactQueuePage').then((m) => ({ default: m.ContactQueuePage })))
const StudentNotesPage = lazy(() => import('@/pages/admin/students/notes/StudentNotesPage').then((m) => ({ default: m.StudentNotesPage })))
const FaceLoginPage = lazy(() => import('@/pages/admin/students/face/FaceLoginPage').then((m) => ({ default: m.FaceLoginPage })))
const CourseAnalyticsPage = lazy(() => import('@/pages/admin/subjects/CourseAnalyticsPage').then((m) => ({ default: m.CourseAnalyticsPage })))
const AccountPage = lazy(() => import('@/pages/admin/account/AccountPage').then((m) => ({ default: m.AccountPage })))
// Marketing — Instagram AI agenti (izoh/DM avtojavobi, lidga aylantirish)
const InstagramDashboard = lazy(() => import('@/pages/admin/marketing/InstagramDashboard').then((m) => ({ default: m.InstagramDashboard })))
const InstagramInbox = lazy(() => import('@/pages/admin/marketing/InstagramInbox').then((m) => ({ default: m.InstagramInbox })))
const InstagramRules = lazy(() => import('@/pages/admin/marketing/InstagramRules').then((m) => ({ default: m.InstagramRules })))
const InstagramFaq = lazy(() => import('@/pages/admin/marketing/InstagramFaq').then((m) => ({ default: m.InstagramFaq })))
const InstagramKnowledge = lazy(() => import('@/pages/admin/marketing/InstagramKnowledge').then((m) => ({ default: m.InstagramKnowledge })))
const InstagramAnalytics = lazy(() => import('@/pages/admin/marketing/InstagramAnalytics').then((m) => ({ default: m.InstagramAnalytics })))
const InstagramAdLeads = lazy(() => import('@/pages/admin/marketing/InstagramAdLeads').then((m) => ({ default: m.InstagramAdLeads })))
const InstagramAdsStats = lazy(() => import('@/pages/admin/marketing/InstagramAdsStats').then((m) => ({ default: m.InstagramAdsStats })))
// Kontent (post joylash) — bitta modal o'rniga ALOHIDA sahifalar. Sub-sahifalar
// sidebar nav'da EMAS: ular `ContentLayout` ichida tugma sifatida turadi (`MkSubnav`).
const ContentLayout = lazy(() => import('@/pages/admin/marketing/content/ContentLayout').then((m) => ({ default: m.ContentLayout })))
const ContentQueue = lazy(() => import('@/pages/admin/marketing/content/ContentQueue').then((m) => ({ default: m.ContentQueue })))
const ContentPublished = lazy(() => import('@/pages/admin/marketing/content/ContentPublished').then((m) => ({ default: m.ContentPublished })))
const ContentStatus = lazy(() => import('@/pages/admin/marketing/content/ContentStatus').then((m) => ({ default: m.ContentStatus })))
const ContentComposer = lazy(() => import('@/pages/admin/marketing/content/ContentComposer').then((m) => ({ default: m.ContentComposer })))
const InstagramQuality = lazy(() => import('@/pages/admin/marketing/InstagramQuality').then((m) => ({ default: m.InstagramQuality })))
const InstagramSettings = lazy(() => import('@/pages/admin/marketing/InstagramSettings').then((m) => ({ default: m.InstagramSettings })))
// O'qituvchi portali (SPA ichida, /teacher/*)
const TeacherDashboard = lazy(() => import('@/pages/teacher/TeacherDashboard').then((m) => ({ default: m.TeacherDashboard })))
const TeacherGroupsPage = lazy(() => import('@/pages/teacher/groups/TeacherGroupsPage').then((m) => ({ default: m.TeacherGroupsPage })))
const TeacherGroupDetailPage = lazy(() => import('@/pages/teacher/groups/TeacherGroupDetailPage').then((m) => ({ default: m.TeacherGroupDetailPage })))
const TeacherMessagesPage = lazy(() => import('@/pages/teacher/messages/MessagesPage').then((m) => ({ default: m.TeacherMessagesPage })))
const TeacherProfilePage = lazy(() => import('@/pages/teacher/TeacherProfilePage').then((m) => ({ default: m.TeacherProfilePage })))
const TeacherSupportPage = lazy(() => import('@/pages/teacher/support/SupportPage').then((m) => ({ default: m.TeacherSupportPage })))
const TeacherFeedbackPage = lazy(() => import('@/pages/teacher/feedback/FeedbackPage').then((m) => ({ default: m.TeacherFeedbackPage })))
const TeacherOwnSalaryPage = lazy(() => import('@/pages/teacher/salary/SalaryPage').then((m) => ({ default: m.TeacherSalaryPage })))
const TeacherAccountPage = lazy(() => import('@/pages/teacher/account/AccountPage').then((m) => ({ default: m.TeacherAccountPage })))
const TeacherRatingPage = lazy(() => import('@/pages/teacher/rating/TeacherRatingPage').then((m) => ({ default: m.TeacherRatingPage })))
const TeacherTestsPage = lazy(() => import('@/pages/teacher/tests/TeacherTestsPage').then((m) => ({ default: m.TeacherTestsPage })))
// O'quvchi portali (SPA ichida, /student/*)
const StudentDashboardScreen = lazy(() => import('@/pages/student/Dashboard').then((m) => ({ default: m.StudentDashboardScreen })))
const StudentProgressScreen = lazy(() => import('@/pages/student/Progress').then((m) => ({ default: m.StudentProgressScreen })))
const SubjectProgressDetailScreen = lazy(() => import('@/pages/student/SubjectProgressDetail').then((m) => ({ default: m.SubjectProgressDetailScreen })))
const StudentGradesScreen = lazy(() => import('@/pages/student/Grades').then((m) => ({ default: m.StudentGradesScreen })))
const StudentAttendanceScreen = lazy(() => import('@/pages/student/Attendance').then((m) => ({ default: m.StudentAttendanceScreen })))
const StudentStatisticsScreen = lazy(() => import('@/pages/student/Statistics').then((m) => ({ default: m.StudentStatisticsScreen })))
const StudentChatScreen = lazy(() => import('@/pages/student/Chat').then((m) => ({ default: m.StudentChatScreen })))
const StudentFinanceScreen = lazy(() => import('@/pages/student/Finance').then((m) => ({ default: m.StudentFinanceScreen })))
const StudentFeedbackScreen = lazy(() => import('@/pages/student/Feedback').then((m) => ({ default: m.StudentFeedbackScreen })))
const StudentProfileScreen = lazy(() => import('@/pages/student/Profile').then((m) => ({ default: m.StudentProfileScreen })))
const StudentSettingsScreen = lazy(() => import('@/pages/student/Settings').then((m) => ({ default: m.StudentSettingsScreen })))
const StudentLocationScreen = lazy(() => import('@/pages/student/Location').then((m) => ({ default: m.StudentLocationScreen })))
const StudentLessonScreen = lazy(() => import('@/pages/student/Lesson').then((m) => ({ default: m.StudentLessonScreen })))
const StudentGradingScreen = lazy(() => import('@/pages/student/Grading').then((m) => ({ default: m.StudentGradingScreen })))
const StudentAiCheckScreen = lazy(() => import('@/pages/student/AiCheck').then((m) => ({ default: m.StudentAiCheckScreen })))
const StudentSupportScreen = lazy(() => import('@/pages/student/Support').then((m) => ({ default: m.StudentSupportScreen })))
const StudentAccountScreen = lazy(() => import('@/pages/student/Account').then((m) => ({ default: m.StudentAccountScreen })))
const CertificatesPage = lazy(() => import('@/pages/student/Certificates').then((m) => ({ default: m.CertificatesPage })))
const StudentContractsScreen = lazy(() => import('@/pages/student/Contracts').then((m) => ({ default: m.StudentContractsScreen })))

// Lazy sahifa chunk'i yuklanayotganda ko'rsatiladigan zaxira ekran.
function PageFallback() {
  return <Loader className="min-h-screen" />
}

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
    <Suspense fallback={<PageFallback />}>
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
    </Suspense>
  )
}
