# MultiTravel Valiz Challenge — public leaderboard (web)

A single static page (`index.html`) that shows the live ranking of an event on a
TV, a tablet or a phone. No build step, no framework, no external scripts: it
only calls the public `get_leaderboard` RPC of the Supabase backend
(`backend/supabase`), which never returns phone numbers or e-mail addresses.

```
leaderboard-web/
  index.html      the page (vanilla HTML/CSS/JS, Turkish UI)
  config.js       client-safe settings (project URL, anon key, event slug, refresh, top N)
  config.local.js optional, git-ignored override with the real values (same shape)
  serve.ps1       Windows: serve the folder on the LAN and print the URLs
  serve.sh        macOS/Linux: same
  assets/logo.png optional: drop a logo here and it replaces the text wordmark
```

## 1. Configure

Edit `config.js` (or create `config.local.js` so the real values stay out of git):

```js
window.MT_CONFIG = {
  SUPABASE_URL: "https://<project-ref>.supabase.co", // Dashboard > Project Settings > API
  SUPABASE_ANON_KEY: "<anon / publishable key>",      // the PUBLIC key only - never service_role
  EVENT_SLUG: "multitravel-2026",                     // events.slug of the active event
  REFRESH_SECONDS: 15,                                // 3..3600
  TOP_N: 20                                           // 1..1000 rows
};
```

`config.local.js` example (loaded after `config.js` when the file exists):

```js
window.MT_CONFIG = Object.assign(window.MT_CONFIG || {}, {
  SUPABASE_URL: "https://<project-ref>.supabase.co",
  SUPABASE_ANON_KEY: "<anon key>",
  EVENT_SLUG: "multitravel-2026"
});
```

The anon key is safe to ship in a web page: it can only execute the four public
RPCs, and `get_leaderboard` exposes rank, display name, score, time, gender and
completion time — nothing else. See `backend/supabase/README.md`, "Security model".

## 2. Run

Windows (PowerShell):

```powershell
cd leaderboard-web
.\serve.ps1            # or: .\serve.ps1 -Port 9000
```

macOS / Linux:

```sh
cd leaderboard-web
chmod +x serve.sh
./serve.sh             # or: ./serve.sh 9000
```

The script prints `http://localhost:8080/` and the LAN addresses
(`http://192.168.x.y:8080/`). Open one of them on the TV browser and press
`F11` for full screen. The page refreshes itself; the header shows the live
status, the countdown and the last successful update time.

Any static host works as well (IIS, nginx, Netlify, GitHub Pages, an S3 bucket):
upload the folder as is.

## 3. Options

| What | How |
|---|---|
| Show another event on one screen | `http://<host>:8080/?event=<slug>` (overrides `EVENT_SLUG`) |
| Change how names are shown (`Ad Soyad` vs `Ad S.`) | `events.name_display_mode` in the database (backend README) |
| Logo instead of the text wordmark | put `assets/logo.png` next to `index.html` (PNG with transparent background, ~4:1) |
| Manual refresh | press `R` or click **Yeniden dene** in the banner |
| Bigger text on a far TV | browser zoom (`Ctrl` + `+`); the layout scales with the viewport |

## 4. States and messages (Turkish UI)

| State | What the page shows |
|---|---|
| Loading | spinner + `Yükleniyor…` |
| No results yet | `Henüz sonuç yok` |
| Live | green dot `Canlı`, `Son güncelleme: HH:MM:SS`, `Yenileme: n sn` |
| Server / network error | red banner with the reason and **Yeniden dene**; the last good list stays visible |
| Offline | banner `İnternet bağlantısı yok…`, auto-refresh resumes when the browser is back online |
| Wrong slug | `Etkinlik bulunamadı…` (RPC code `EVENT_ACCESS_DENIED`) |
| Event paused | `Etkinlik şu anda aktif değil.` (RPC code `EVENT_INACTIVE`) |
| Missing config | `Yapılandırma eksik…` |

Columns: **Sıra**, **Ad Soyad**, **Puan**, **Süre** (`mm:ss.SS` from
`completion_ms`), **Cinsiyet** (`Kadın` / `Erkek`). The first three rows get
gold / silver / bronze badges. On phones the gender column is hidden to keep the
table readable.

## 5. Troubleshooting

- **`Sunucuya ulaşılamadı`** — check `SUPABASE_URL` (no trailing path), the
  network of the TV, and that the Supabase project is not paused.
- **`Sunucu hatası (HTTP 401)`** — the anon key is wrong or belongs to another project.
- **`Etkinlik bulunamadı`** — the slug does not exist; list events with
  `select slug, is_active from public.events;` in Supabase Studio.
- **Nothing changes after editing `config.js`** — hard refresh (`Ctrl`+`F5`);
  `serve` sends `must-revalidate` headers but the browser may still cache.
- Turkish characters look wrong — the page is UTF-8 and uses the system font
  stack (Segoe UI / Inter / Roboto / Arial); make sure the host serves `.html`
  as `text/html; charset=utf-8` (both bundled servers do).
