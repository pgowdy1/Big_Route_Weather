import assert from 'node:assert/strict';
import { buildSitemapUrls, buildSitemapXml } from './generate-sitemap.mjs';

const peaks = [{ slug: 'mount-whitney' }, { slug: 'mount-rainier' }];
const urls = buildSitemapUrls(peaks);

assert.equal(urls.length, 5); // / , /all, /about + 2 peaks
assert.ok(urls.includes('https://bigrouteweather.com/'));
assert.ok(urls.includes('https://bigrouteweather.com/all'));
assert.ok(urls.includes('https://bigrouteweather.com/peak/mount-whitney'));
assert.ok(!urls.some(u => u.includes('/diagnostics')));
assert.ok(urls.every(u => u.startsWith('https://bigrouteweather.com')));

const xml = buildSitemapXml(urls);
assert.ok(xml.includes('<loc>https://bigrouteweather.com/peak/mount-rainier</loc>'));
assert.ok(xml.startsWith('<?xml'));
console.log('sitemap test OK');
