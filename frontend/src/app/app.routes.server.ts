import { RenderMode, ServerRoute } from '@angular/ssr';
import { ALL_PEAKS } from './seo/peaks-catalog';

export const serverRoutes: ServerRoute[] = [
  {
    path: 'peak/:slug',
    renderMode: RenderMode.Prerender,
    getPrerenderParams: async () => ALL_PEAKS.map(p => ({ slug: p.slug })),
  },
  // Everything else (home, /all, /about, /diagnostics) prerenders as-is.
  { path: '**', renderMode: RenderMode.Prerender },
];
