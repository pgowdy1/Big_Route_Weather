import { Injectable, inject } from '@angular/core';
import { DOCUMENT } from '@angular/common';
import { Title, Meta } from '@angular/platform-browser';
import { SITE_NAME, SITE_URL, DEFAULT_OG_IMAGE } from './seo.constants';

export interface SeoMeta {
  title: string;
  description: string;
  path: string;       // e.g. '/', '/peak/mount-whitney'
  noindex?: boolean;
  ogType?: string;    // default 'website'
  jsonLd?: object[];  // structured-data objects to embed
}

@Injectable({ providedIn: 'root' })
export class SeoService {
  private title = inject(Title);
  private meta = inject(Meta);
  private doc = inject(DOCUMENT);

  setMeta(m: SeoMeta): void {
    const url = absoluteUrl(m.path);

    this.title.setTitle(m.title);
    this.meta.updateTag({ name: 'description', content: m.description });
    this.meta.updateTag({ name: 'robots', content: m.noindex ? 'noindex, follow' : 'index, follow' });
    this.setCanonical(url);

    this.meta.updateTag({ property: 'og:title', content: m.title });
    this.meta.updateTag({ property: 'og:description', content: m.description });
    this.meta.updateTag({ property: 'og:url', content: url });
    this.meta.updateTag({ property: 'og:type', content: m.ogType ?? 'website' });
    this.meta.updateTag({ property: 'og:image', content: DEFAULT_OG_IMAGE });
    this.meta.updateTag({ property: 'og:site_name', content: SITE_NAME });

    this.meta.updateTag({ name: 'twitter:card', content: 'summary_large_image' });
    this.meta.updateTag({ name: 'twitter:title', content: m.title });
    this.meta.updateTag({ name: 'twitter:description', content: m.description });
    this.meta.updateTag({ name: 'twitter:image', content: DEFAULT_OG_IMAGE });

    this.setJsonLd(m.jsonLd ?? []);
  }

  private setCanonical(url: string): void {
    let link = this.doc.head.querySelector<HTMLLinkElement>('link[rel="canonical"]');
    if (!link) {
      link = this.doc.createElement('link');
      link.setAttribute('rel', 'canonical');
      this.doc.head.appendChild(link);
    }
    link.setAttribute('href', url);
  }

  private setJsonLd(objects: object[]): void {
    this.doc.head.querySelectorAll('script[type="application/ld+json"][data-seo]').forEach(n => n.remove());
    for (const obj of objects) {
      const script = this.doc.createElement('script');
      script.setAttribute('type', 'application/ld+json');
      script.setAttribute('data-seo', '');
      script.textContent = JSON.stringify(obj).replace(/</g, '\\u003c');
      this.doc.head.appendChild(script);
    }
  }
}

function absoluteUrl(path: string): string {
  const clean = path.startsWith('/') ? path : `/${path}`;
  return clean === '/' ? `${SITE_URL}/` : SITE_URL + clean;
}
