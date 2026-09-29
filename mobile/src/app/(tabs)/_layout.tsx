import { NativeTabs } from 'expo-router/unstable-native-tabs';

import { MiniPlayer } from '@/components/mini-player';
import { hasTabAccessory, usePlayerStore } from '@/lib/player';
import { useTheme } from '@/theme/theme';

/**
 * The real system tab bar: Liquid Glass on iOS 26, Material bottom navigation on Android.
 * Search uses the search role, which iOS 26 draws as the separate circular button.
 */
export default function TabLayout() {
  const { palette } = useTheme();
  const playing = usePlayerStore((s) => s.current !== null);
  return (
    <NativeTabs
      tintColor={palette.color.interactive}
      minimizeBehavior="onScrollDown"
      labelStyle={{ color: palette.color.text.secondary }}>
      {hasTabAccessory && playing && (
        <NativeTabs.BottomAccessory>
          <AccessoryPlayer />
        </NativeTabs.BottomAccessory>
      )}
      <NativeTabs.Trigger name="index">
        <NativeTabs.Trigger.Label>Home</NativeTabs.Trigger.Label>
        <NativeTabs.Trigger.Icon sf={{ default: 'house', selected: 'house.fill' }} md="home" />
      </NativeTabs.Trigger>
      <NativeTabs.Trigger name="discover">
        <NativeTabs.Trigger.Label>Discover</NativeTabs.Trigger.Label>
        <NativeTabs.Trigger.Icon sf="square.grid.2x2" md="explore" />
      </NativeTabs.Trigger>
      <NativeTabs.Trigger name="live">
        <NativeTabs.Trigger.Label>Live</NativeTabs.Trigger.Label>
        <NativeTabs.Trigger.Icon sf="dot.radiowaves.left.and.right" md="live_tv" />
      </NativeTabs.Trigger>
      <NativeTabs.Trigger name="community">
        <NativeTabs.Trigger.Label>Community</NativeTabs.Trigger.Label>
        <NativeTabs.Trigger.Icon sf="person.3" md="groups" />
      </NativeTabs.Trigger>
      <NativeTabs.Trigger name="give">
        <NativeTabs.Trigger.Label>Give</NativeTabs.Trigger.Label>
        <NativeTabs.Trigger.Icon sf="heart" md="volunteer_activism" />
      </NativeTabs.Trigger>
      <NativeTabs.Trigger name="search" role="search">
        <NativeTabs.Trigger.Label>Search</NativeTabs.Trigger.Label>
        <NativeTabs.Trigger.Icon sf="magnifyingglass" md="search" />
      </NativeTabs.Trigger>
    </NativeTabs>
  );
}

function AccessoryPlayer() {
  const placement = NativeTabs.BottomAccessory.usePlacement();
  return <MiniPlayer compact={placement === 'inline'} />;
}
