import { space } from '@shapers/tokens';
import { StyleSheet } from 'react-native';

import { Glass } from './glass';
import { Screen } from './screen';
import { AppText } from './text';
import { text } from '@/theme/type';

/** Stand-in for a tab whose features arrive in a later phase. */
export function PlaceholderScreen({ title, description }: { title: string; description: string }) {
  return (
    <Screen>
      <AppText style={text.largeTitle}>{title}</AppText>
      <Glass style={styles.card}>
        <AppText tone="secondary">{description}</AppText>
      </Glass>
    </Screen>
  );
}

const styles = StyleSheet.create({
  card: { padding: space.xl },
});
