# Big Route Weather — Free SEO & Marketing Playbook

**Date:** 2026-06-18
**Companion to:** `2026-06-18-seo-foundation-design.md` (the code build)

This is the **free, no-budget** playbook for turning the code SEO foundation into actual traffic. It's advice/process, not code. Work it roughly top-to-bottom — the early items are prerequisites for the later ones paying off.

**Core principle:** the engine is **124 specific peak pages ranking for high-intent long-tail queries** (e.g. "Mount Rainier climbing weather"). Everything here either gets those pages indexed, makes them better, or earns links/shares to them. Don't chase broad head terms; don't spam.

---

## 0. Prerequisite — ship the code foundation

Already specced: prerendered crawlable HTML, per-page titles/descriptions/canonical, Open Graph cards, `Mountain` + `BreadcrumbList` structured data, `sitemap.xml`, `robots.txt`. Without this, the rest underperforms — crawlers and social scrapers can't read a blank SPA. Ship it first.

## 1. Get indexed (do this the day it deploys)

1. **Google Search Console** — add `bigrouteweather.com` as a **Domain property** and verify via the Cloudflare DNS TXT record (you control DNS, so this is the strongest verification). Submit `https://bigrouteweather.com/sitemap.xml`. Use **URL Inspection → Request indexing** for the homepage and 5–10 marquee peaks (Rainier, Whitney, Shasta, Hood, Longs Peak) to prime discovery.
2. **Bing Webmaster Tools** — create an account and **import from Google Search Console** (one click, pulls your verification + sitemap). Bing also feeds DuckDuckGo and ChatGPT/Copilot search, which the outdoors crowd uses.
3. **Confirm crawlability** — in GSC URL Inspection, "View crawled page" on a peak URL should show the **baked content + meta** (proves prerender works for crawlers). Validate one peak URL in Google's **Rich Results Test** (structured data) and a **social card validator** (OG tags render).
4. **Watch Coverage/Pages** weekly for the first month — you want the peak URLs moving to "Indexed."

## 2. Content & on-page (mostly handled by the build — keep it honest)

- **Per-peak = long-tail, focused titles.** `"<Mountain> Weather Forecast & Climbing Conditions"`. Don't stuff "alpine/rock climbing/hiking" into all 124 titles — it reads as spam and dilutes the per-peak relevance that wins.
- **Audience vocabulary in the right places** — weave "climbers, mountaineers, hikers, trail runners" into per-peak **descriptions** (accurately — don't call a 5.9 alpine route a "trail run") and into **homepage/about** positioning.
- **Make each page genuinely useful** — the live forecast + route grade *is* the unique value. That's what earns rankings and repeat visits; Google rewards pages that satisfy the searcher.
- **Internal links** — `/all` links every peak (the crawl hub); peak pages link to range peers. Keep it that way.

## 3. Earn links & referral traffic (the real free-growth work)

Search rankings need signals that you're a trusted, linked-to resource. All free, all value-first — **never drop spam links**:

- **Answer real questions in the communities climbers already use**, linking the *specific relevant peak forecast* when it genuinely helps:
  - Reddit: r/Mountaineering, r/14ers, r/alpinism, r/iceclimbing, r/coloradohikers, regional subs (r/WashingtonHiking, r/CaliforniaHiking).
  - Mountain Project forums, SummitPost, CascadeClimbers, 14ers.com forums.
  - Regional climbing/mountaineering Facebook groups and Discords.
  - When someone asks "what's the weather window looking like on Rainier this weekend?" → link `bigrouteweather.com/peak/mount-rainier`. That's a useful answer, not a spam drop.
- **Get listed in "tools/resources" roundups** — many climbing blogs and club sites maintain "useful weather tools" or "trip-planning resources" pages. Email the maintainers a short, genuine pitch. These are durable backlinks.
- **Offer it to guide services, clubs, and gyms** — a free conditions tool for their members is an easy ask and a natural backlink/referral.
- **Mountaineering subreddit "tool" / "I built this" posts** — a single honest "I built a free summit-weather grade tool for big peaks" post in the right sub, when it adds value, can drive a spike + links. Read each community's self-promo rules first.

## 4. Social cadence (your OG cards now make this look good)

- Post **timely conditions** when they're notable: "Solid summit window on Mount Shasta this weekend — [link]." The OG card makes the share look credible and clickable.
- Pick the platforms climbers actually use (Reddit, Instagram, the climbing Discords) over chasing every network.
- Frequency over reach early — consistent, genuinely-useful posts beat occasional promotional ones.

## 5. Measure & iterate (the compounding loop)

- **GSC Performance report** is your growth engine: find queries where you rank position 5–20 ("almost there"), then improve those specific pages (better description, a bit more useful copy, a relevant internal link). Small pushes on near-winners beat chasing new terms.
- Track **which peaks get impressions/clicks** — double down on what's working (more sharing, better copy for those).
- Re-check **Core Web Vitals** in GSC (the site is already fast with progressive paint; keep it that way — don't regress LCP with heavy additions).

## 6. Don't (these actively hurt)

- Don't buy links or use link farms.
- Don't keyword-stuff titles/descriptions or create near-duplicate thin pages.
- Don't spam-drop links in forums/subreddits — it gets you banned and is a bad look for the brand.
- Don't build doorway pages for terms you can't honestly satisfy.

---

**TL;DR:** Ship the foundation → get indexed (GSC + Bing + sitemap) → be genuinely useful and present in real climbing communities → watch GSC and improve the pages that are almost ranking. The 124 peak pages do the heavy lifting; this playbook just gets them seen.
