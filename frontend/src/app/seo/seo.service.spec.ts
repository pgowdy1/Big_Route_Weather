import { TestBed } from '@angular/core/testing';
import { DOCUMENT } from '@angular/common';
import { SeoService } from './seo.service';
import { SITE_URL } from './seo.constants';

describe('SeoService', () => {
  let svc: SeoService;
  let doc: Document;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [SeoService] });
    svc = TestBed.inject(SeoService);
    doc = TestBed.inject(DOCUMENT);
  });

  function meta(sel: string): string | null {
    return doc.head.querySelector(sel)?.getAttribute('content') ?? null;
  }

  it('sets title, description, absolute canonical, OG and Twitter tags', () => {
    svc.setMeta({ title: 'T', description: 'D', path: '/peak/mount-whitney' });

    expect(doc.title).toBe('T');
    expect(meta('meta[name="description"]')).toBe('D');
    expect(doc.head.querySelector('link[rel="canonical"]')?.getAttribute('href'))
      .toBe(`${SITE_URL}/peak/mount-whitney`);
    expect(meta('meta[property="og:title"]')).toBe('T');
    expect(meta('meta[property="og:url"]')).toBe(`${SITE_URL}/peak/mount-whitney`);
    expect(meta('meta[name="twitter:card"]')).toBe('summary_large_image');
    expect(meta('meta[name="robots"]')).toBe('index, follow');
  });

  it('emits noindex when asked and a single canonical across calls', () => {
    svc.setMeta({ title: 'A', description: 'D', path: '/a' });
    svc.setMeta({ title: 'B', description: 'D', path: '/b', noindex: true });

    expect(meta('meta[name="robots"]')).toBe('noindex, follow');
    expect(doc.head.querySelectorAll('link[rel="canonical"]').length).toBe(1);
    expect(doc.head.querySelector('link[rel="canonical"]')?.getAttribute('href'))
      .toBe(`${SITE_URL}/b`);
  });

  it('replaces (not stacks) its JSON-LD between navigations', () => {
    svc.setMeta({ title: 'A', description: 'D', path: '/a', jsonLd: [{ '@type': 'WebSite' }] });
    svc.setMeta({ title: 'B', description: 'D', path: '/b', jsonLd: [{ '@type': 'Mountain' }] });

    const scripts = doc.head.querySelectorAll('script[type="application/ld+json"][data-seo]');
    expect(scripts.length).toBe(1);
    expect(scripts[0].textContent).toContain('Mountain');
  });
});
