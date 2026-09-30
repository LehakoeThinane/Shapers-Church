import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { StrictMode } from 'react';
import { createRoot } from 'react-dom/client';
import { createBrowserRouter, Navigate, RouterProvider } from 'react-router';
import { Layout } from './components/Layout';
import { ApiError } from './lib/api';
import { PaletteProvider } from './lib/palette';
import { ScopeProvider } from './lib/scope';
import { AuditPage, ChurchPage, DuplicatesPage, RolesPage, SecurityPage } from './pages/AdminPages';
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
import { AnnouncementEditorPage, AnnouncementsPage } from './pages/AnnouncementPages';
import { LivestreamConsolePage, LivestreamsPage } from './pages/LivestreamPages';
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
      { index: true, element: <Navigate to="/people" replace /> },
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
      { path: 'events', element: <EventsPage /> },
      { path: 'events/new', element: <EventEditorPage /> },
      { path: 'events/:id', element: <EventEditorPage /> },
      { path: 'events/:id/attendees', element: <EventAttendeesPage /> },
      { path: 'events/:id/check-in', element: <CheckInPage /> },
      { path: 'announcements', element: <AnnouncementsPage /> },
      { path: 'announcements/new', element: <AnnouncementEditorPage /> },
      { path: 'announcements/:id', element: <AnnouncementEditorPage /> },
      { path: 'prayer', element: <PrayerPage /> },
      { path: 'privacy', element: <PrivacyPage /> },
      { path: 'breaches', element: <BreachesPage /> },
      { path: 'connect', element: <ConnectCardsPage /> },
      { path: 'church', element: <ChurchPage /> },
      { path: 'access', element: <RolesPage /> },
      { path: 'audit', element: <AuditPage /> },
      { path: 'security', element: <SecurityPage /> },
      { path: '*', element: <Navigate to="/people" replace /> },
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
