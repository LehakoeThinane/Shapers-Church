import { Stack } from 'expo-router';

import { useTheme } from '@/theme/theme';

export default function AuthLayout() {
  const { palette } = useTheme();
  return (
    <Stack
      screenOptions={{
        headerTransparent: true,
        headerTitle: '',
        headerTintColor: palette.color.interactive,
        contentStyle: { backgroundColor: palette.color.background },
      }}
    />
  );
}
