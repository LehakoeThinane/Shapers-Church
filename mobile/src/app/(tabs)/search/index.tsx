import { space } from '@shapers/tokens';
import { Stack } from 'expo-router';
import { useEffect, useState } from 'react';
import { StyleSheet, View } from 'react-native';

import { Screen } from '@/components/screen';
import { SermonRow } from '@/components/sermon-bits';
import { AppText } from '@/components/text';
import { useSermons } from '@/lib/media';
import { text } from '@/theme/type';

export default function SearchScreen() {
  const [query, setQuery] = useState('');
  const [debounced, setDebounced] = useState('');
  useEffect(() => {
    const t = setTimeout(() => setDebounced(query.trim()), 350);
    return () => clearTimeout(t);
  }, [query]);

  const results = useSermons({ q: debounced, pageSize: 30 });
  const searching = debounced.length > 0;

  return (
    <>
      <Stack.Screen options={{ title: 'Search' }} />
      <Stack.SearchBar placement="automatic" placeholder="Sermons, speakers, scripture" onChangeText={(e) => setQuery(e.nativeEvent.text)} />
      <Screen>
        <AppText tone="tertiary" style={text.caption}>
          {searching
            ? results.data
              ? `${results.data.total} ${results.data.total === 1 ? 'result' : 'results'}`
              : 'Searching…'
            : 'Try a book of the Bible ("Isaiah"), a speaker, or a topic like "prayer".'}
        </AppText>
        <View style={styles.list}>
          {(searching ? results.data?.items : [])?.map((s) => (
            <SermonRow key={s.id} sermon={s} />
          ))}
        </View>
        {searching && results.isError && <AppText tone="secondary">Search needs a connection.</AppText>}
      </Screen>
    </>
  );
}

const styles = StyleSheet.create({
  list: { gap: space.sm },
});
