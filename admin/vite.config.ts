import react from '@vitejs/plugin-react';
import { defineConfig } from 'vite';

// The dev server proxies /api to the local backend, so the session cookie is same-origin.
export default defineConfig({
  plugins: [react()],
  // node_modules is flat for the mobile app, which pins its own React at the root, so libraries
  // such as react-query can end up with a nested copy. One React instance is required for hooks.
  resolve: {
    dedupe: ['react', 'react-dom'],
  },
  server: {
    port: 5173,
    proxy: {
      '/api': 'http://localhost:5080',
      // Development file storage and the podcast feed are served by the API too.
      '/media-files': 'http://localhost:5080',
      '/podcast.xml': 'http://localhost:5080',
    },
  },
});
