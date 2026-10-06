import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { StrictMode } from 'react';
import { createRoot } from 'react-dom/client';
import { createBrowserRouter, Navigate, RouterProvider } from 'react-router';
import { Layout } from './components/Layout';
import { ApiError } from './lib/api';
import { PaletteProvider } from './lib/palette';
import { ScopeProvider } from './lib/scope';
import { AuditPage, ChurchPage, DuplicatesPage, SecurityPage } from './pages/AdminPages';
import { RolePage, RolesPage } from './pages/RolesPages';
import { LoginPage, SetPasswordPage } from './pages/LoginPage';
import { NewPersonPage, PeoplePage } from './pages/PeoplePage';
import { PersonPage } from './pages/PersonPage';
import { MediaLibraryPage } from './pages/MediaLibraryPage';
import { SermonEditorPage } from './pages/SermonEditorPage';
import { SermonsPage } from './pages/SermonsPage';
import { ConnectCardsPage } from './pages/ConnectCardsPage';
import { CheckInPage, EventAttendeesPage, EventEditorPage, EventsPage } from './pages/EventPages';
import { PrayerPage } from './pages/PrayerPage';
import { PrivacyPage } from './pages/PrivacyPage';
import { BreachesPage } from './pages/BreachesPage';
import { ContentPage, PageEditorPage, PostEditorPage, TranslationsPage } from './pages/ContentPages';
import { AnnouncementEditorPage, AnnouncementsPage } from './pages/AnnouncementPages';
import { LivestreamConsolePage, LivestreamsPage } from './pages/LivestreamPages';
import { ChatConsolePage, ChatStreamsPage } from './pages/ChatModerationPage';
import { HomePage } from './pages/HomePage';
import { CellMaterialsPage, CellPage, CellReportPage, CellReportsPage, CellsPage, ChurchLessonsPage, MyCellPage, ReportEditorPage } from './pages/CellPages';
import { AssistUsagePage } from './pages/AssistPage';
import { LiveRunSheetPage, MusicStandPage, ServicePlanPage, ServicePlansPage, ServingMatrixPage } from './pages/ServicesPages';
import { ServiceTypesPage, ServingTeamsPage, SongPage, SongsPage } from './pages/ServicesSetupPages';
import './index.css';

const queryClient = new QueryClient({
  defaultOptions: {
    queries: {
      // Don't retry what the server has already refused.
      retry: (failures, error) => !(error instanceof ApiError && error.status < 500) && failures < 2,
      refetchOnWindowFocus: false,
    },
  },
});

const router = createBrowserRouter([
  { path: '/login', element: <LoginPage /> },
  { path: '/set-password', element: <SetPasswordPage /> },
  {
    path: '/',
    element: (
      <ScopeProvider>
        <Layout />
      </ScopeProvider>
    ),
    children: [
      { index: true, element: <HomePage /> },
      { path: 'people', element: <PeoplePage /> },
      { path: 'people/new', element: <NewPersonPage /> },
      { path: 'people/:id', element: <PersonPage /> },
      { path: 'duplicates', element: <DuplicatesPage /> },
      { path: 'sermons', element: <SermonsPage /> },
      { path: 'sermons/new', element: <SermonEditorPage /> },
      { path: 'sermons/library', element: <MediaLibraryPage /> },
      { path: 'sermons/:id', element: <SermonEditorPage /> },
      { path: 'livestreams', element: <LivestreamsPage /> },
      { path: 'livestreams/:id', element: <LivestreamConsolePage /> },
      { path: 'chat', element: <ChatStreamsPage /> },
      { path: 'chat/:id', element: <ChatConsolePage /> },
      { path: 'events', element: <EventsPage /> },
      { path: 'events/new', element: <EventEditorPage /> },
      { path: 'events/:id', element: <EventEditorPage /> },
      { path: 'events/:id/attendees', element: <EventAttendeesPage /> },
      { path: 'events/:id/check-in', element: <CheckInPage /> },
      { path: 'content', element: <ContentPage /> },
      { path: 'content/translations', element: <TranslationsPage /> },
      { path: 'content/pages/new', element: <PageEditorPage /> },
      { path: 'content/pages/:id', element: <PageEditorPage /> },
      { path: 'content/posts/new', element: <PostEditorPage /> },
      { path: 'content/posts/:id', element: <PostEditorPage /> },
      { path: 'announcements', element: <AnnouncementsPage /> },
      { path: 'announcements/new', element: <AnnouncementEditorPage /> },
      { path: 'announcements/:id', element: <AnnouncementEditorPage /> },
      { path: 'cells', element: <CellsPage /> },
      { path: 'cells/reports', element: <CellReportsPage /> },
      { path: 'cells/reports/:reportId', element: <CellReportPage /> },
      { path: 'cells/materials', element: <CellMaterialsPage /> },
      { path: 'cells/lessons', element: <ChurchLessonsPage /> },
      { path: 'assist', element: <AssistUsagePage /> },
      { path: 'services', element: <ServicePlansPage /> },
      { path: 'services/plans/:id', element: <ServicePlanPage /> },
      { path: 'services/plans/:id/live', element: <LiveRunSheetPage /> },
      { path: 'services/plans/:id/stand', element: <MusicStandPage /> },
      { path: 'services/matrix', element: <ServingMatrixPage /> },
      { path: 'services/teams', element: <ServingTeamsPage /> },
      { path: 'services/songs', element: <SongsPage /> },
      { path: 'services/songs/:id', element: <SongPage /> },
      { path: 'services/types', element: <ServiceTypesPage /> },
      { path: 'cells/:id', element: <CellPage /> },
      { path: 'my-cells/:cellId', element: <MyCellPage /> },
      { path: 'my-cells/:cellId/reports/new', element: <ReportEditorPage /> },
      { path: 'my-cells/:cellId/reports/:reportId', element: <ReportEditorPage /> },
      { path: 'prayer', element: <PrayerPage /> },
      { path: 'privacy', element: <PrivacyPage /> },
      { path: 'breaches', element: <BreachesPage /> },
      { path: 'connect', element: <ConnectCardsPage /> },
      { path: 'church', element: <ChurchPage /> },
      { path: 'access', element: <RolesPage /> },
      { path: 'access/roles/new', element: <RolePage /> },
      { path: 'access/roles/:id', element: <RolePage /> },
      { path: 'audit', element: <AuditPage /> },
      { path: 'security', element: <SecurityPage /> },
      { path: '*', element: <Navigate to="/" replace /> },
    ],
  },
]);

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <QueryClientProvider client={queryClient}>
      <PaletteProvider>
        <RouterProvider router={router} />
      </PaletteProvider>
    </QueryClientProvider>
  </StrictMode>,
);
