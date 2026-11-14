import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';

// base './' keeps every link relative, so the build works at the root of the S3 bucket or in
// any folder under it. The globe's textures come from assets/ at the repo root, one level up.
export default defineConfig({
  base: './',
  plugins: [react()],
  server: { fs: { allow: ['..'] } },
  // Built files go in viewer/, not assets/: the bucket already has the repo's own assets/ folder.
  build: { assetsDir: 'viewer' },
});
