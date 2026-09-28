import { Stack } from 'expo-router';

import { PlaceholderScreen } from '@/components/placeholder-screen';

export default function SearchScreen() {
  return (
    <>
      <Stack.Screen options={{ title: 'Search' }} />
      <Stack.SearchBar placement="automatic" placeholder="Sermons, events, groups" />
      <PlaceholderScreen title="Search" description="Search across sermons and content." />
    </>
  );
}
