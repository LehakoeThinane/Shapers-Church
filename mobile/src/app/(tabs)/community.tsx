import { space } from '@shapers/tokens';
import { router } from 'expo-router';
import { Pressable, StyleSheet, View } from 'react-native';

import { Glass } from '@/components/glass';
import { Screen } from '@/components/screen';
import { AppText, Icon } from '@/components/text';
import { useSession } from '@/lib/api';
import { servingDay, useMySchedule } from '@/lib/serving';
import { text } from '@/theme/type';

/** Community: serving, prayer, and (soon) groups and the Shapers Growth Track. */
export default function CommunityScreen() {
  const status = useSession((s) => s.status);
  const schedule = useMySchedule();
  const waiting = schedule.data?.filter((a) => a.status === 'Pending').length ?? 0;
  const next = schedule.data?.find((a) => a.status === 'Accepted');

  return (
    <Screen>
      <AppText style={text.largeTitle}>Community</AppText>
      <View style={styles.list}>
        <Entry
          icon={{ ios: 'person.3.fill', android: 'groups' }}
          title="Serving"
          detail={
            status !== 'signedIn'
              ? 'Sign in to see when you serve.'
              : waiting > 0
                ? `${waiting} ${waiting === 1 ? 'request' : 'requests'} waiting for your answer`
                : next
                  ? `Next: ${next.position}, ${servingDay(next.date)}`
                  : 'Your schedule and days away'
          }
          highlight={waiting > 0}
          onPress={() => router.push('/serving')}
        />
        <Entry icon={{ ios: 'hands.sparkles.fill', android: 'volunteer_activism' }} title="Prayer" detail="The prayer wall and your requests" onPress={() => router.push('/prayer')} />
        <Glass style={styles.card}>
          <AppText tone="secondary">Small groups and the Shapers Growth Track are coming soon.</AppText>
        </Glass>
      </View>
    </Screen>
  );
}

function Entry({ icon, title, detail, highlight, onPress }: { icon: Parameters<typeof Icon>[0]['name']; title: string; detail: string; highlight?: boolean; onPress: () => void }) {
  return (
    <Pressable accessibilityRole="button" accessibilityLabel={`${title}. ${detail}`} onPress={onPress}>
      <Glass style={[styles.card, styles.row]}>
        <Icon name={icon} />
        <View style={styles.flex}>
          <AppText style={text.headline}>{title}</AppText>
          <AppText tone={highlight ? 'accent' : 'secondary'} style={text.callout}>
            {detail}
          </AppText>
        </View>
      </Glass>
    </Pressable>
  );
}

const styles = StyleSheet.create({
  list: { gap: space.sm },
  card: { padding: space.lg, gap: space.sm },
  row: { flexDirection: 'row', alignItems: 'center', gap: space.md },
  flex: { flex: 1, gap: 2 },
});
