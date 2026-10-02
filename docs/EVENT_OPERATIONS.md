# MultiTravel Valiz Challenge — Etkinlik Operasyon Rehberi

Bu rehber etkinlik ekibi içindir. Uygulama bir Windows PC üzerinde çalışır; katılımcı Meta Quest başlığını
Quest Link / Air Link ile PC'ye bağlı olarak kullanır. Operatör, PC monitöründeki ekrandan kayıt ve
akışı yönetir; katılımcı VR içinde oyunu oynar.

## 1. İstasyon gereksinimleri

| Bileşen | Gereksinim |
|---|---|
| PC | Windows 10/11 64-bit, VR-ready GPU (NVIDIA GTX 1070 / RTX 2060 ve üzeri önerilir), 16 GB RAM, USB 3.0 |
| Başlık | Meta Quest 2 / 3 / 3S / Pro, Quest Link kablosu (önerilen) veya Air Link (5 GHz Wi-Fi) |
| Yazılım | **Meta Quest Link** PC uygulaması (eski adıyla Oculus PC app) kurulu ve güncel |
| OpenXR | Meta Quest Link uygulamasında **Settings → General → OpenXR Runtime → "Set Meta Quest Link as active"** |
| El takibi | Başlıkta el takibi açık; Quest Link uygulamasında **Settings → Beta → Developer Runtime Features** ve el takibi (Hand tracking over Link) açık |
| İnternet | Sonuçların merkezi panoya gitmesi için istasyonun internete erişimi olmalı (HTTPS, 443). Bağlantı kesilirse sonuçlar yerelde kuyruklanır ve bağlantı dönünce otomatik gönderilir. |

Monitör: Operatör ekranı 1920×1080 için tasarlanmıştır; daha düşük çözünürlüklerde ölçeklenir.

## 2. Kurulum (her istasyon için bir kez)

1. Build çıktısı klasörünü (`Build/Windows/` içindeki `MultiTravel Valiz Challenge.exe` ve yanındaki
   `MultiTravel Valiz Challenge_Data`, `MonoBleedingEdge`, `UnityPlayer.dll` vb.) PC'ye kopyalayın.
2. Yapılandırma: build alınırken `Deployment/multitravel.config.json` otomatik olarak
   `MultiTravel Valiz Challenge_Data/StreamingAssets/multitravel.config.json` konumuna kopyalanır (geliştirici
   bilgisayarındaki `127.0.0.1` ayarı asla build'e girmez). Dosyayı elle değiştirmek gerekirse
   (örnek: `docs/config.example.json`) zorunlu alanlar:
   - `backend.supabaseUrl` — Supabase proje URL'si (`https://<ref>.supabase.co`)
   - `backend.supabaseAnonKey` — Supabase **anon / publishable** anahtarı (asla service_role değil)
   - `backend.eventSlug` — etkinlik kodu (örn. `multitravel-2026`)
   - `backend.eventAccessCode` — veritabanındaki `events.access_code` ile aynı değer
   - `backend.stationId` — istasyon adı (`VR-01`, `VR-02`, ...). Her istasyonda farklı olmalı.
3. **Her istasyonda `stationId` farklı olmalıdır.** En pratik yol: yalnızca istasyon adını içeren küçük bir dosyayı
   `%USERPROFILE%\AppData\LocalLow\ECR Etkinlik Bilgisayar\MultiTravel Valiz Challenge\multitravel.config.json`
   yoluna koymak; bu dosya StreamingAssets'teki değerleri alan alan geçersiz kılar:
   ```json
   { "backend": { "stationId": "VR-02" } }
   ```
4. Quest Link'i başlatın, başlığı takın, Link'i etkinleştirin (başlık içinde "Quest Link"e girin).
5. `MultiTravel Valiz Challenge.exe` dosyasını çalıştırın. Operatör ekranındaki durum çubuğunda
   **VR: Hazır** ve **Sunucu: Bağlı** görünmelidir.

## 3. Katılımcı akışı (operatör ekranı)

1. **Başla** — yeni oturum açılır.
2. **Kayıt** — Ad, Soyad, Telefon, E-posta girilir. (KVKK onay metni yapılandırmada tanımlıysa onay kutusu görünür.)
3. **Cinsiyet** — Kadın / Erkek. Ürün havuzu buna göre belirlenir.
4. **Talimatlar** — Operatör katılımcıya başlığı taktırır; VR içinde de aynı talimat görünür. **Oyunu Başlat**.
5. **Geri sayım (3-2-1)** VR içinde gösterilir; sayaç tam olarak oyun başladığında çalışır.
6. **Oyun** — Katılımcı ürünlere **elini uzatarak** (el takibi: tutma/pinch; kumanda: grip tuşu) alır ve valize koyar.
   Uzaktan ışınla tutma kapalıdır; ürünlere fiziksel olarak uzanmak gerekir (gerekirse bir adım atılır). El bir ürüne
   yaklaştığında ürünün Türkçe adı üstünde görünür. Doğru ürün +10, yanlış ürün −5 puan; aynı ürün iki kez sayılmaz.
   Ürün valizden geri alınırsa puanı geri alınır (yapılandırılabilir). Valize bırakılan ürünler istiflenir;
   alttaki ürün çıkarılırsa üsttekiler aşağı iner.
7. **Tamamlanma** — Gerekli ürünlerin tamamı valize konduğunda oyun otomatik biter (varsayılan).
   Operatör gerekirse **Zorla Bitir** ile bitirebilir; **Oturumu İptal Et** oturumu panoya göndermeden kapatır.
8. **Sonuç** — Puan, süre ve (gönderim başarılıysa) sıralama görünür. Gönderim başarısızsa **Tekrar Gönder**.
9. **Yeni Katılımcı** — Tüm oyun durumu sıfırlanır; akış 1. adıma döner.

## 4. Merkezi liderlik tablosu

- `leaderboard-web/index.html` dosyası statik bir sayfadır; `config.js` içine Supabase URL, anon anahtar ve etkinlik kodu yazılır.
  Hazır üretim ayarı: `Deployment/leaderboard-config.local.js` dosyasını `leaderboard-web/config.local.js` olarak kopyalayın.
- Aynı ağdaki TV / tablet / telefon için: `leaderboard-web/serve.ps1` çalıştırın, ekrana yazılan LAN adresini açın.
- İnternet üzerinden erişim için klasör herhangi bir statik barındırmaya (ör. Vercel, Netlify, Supabase Storage) yüklenebilir.
- Sayfa yalnızca Ad Soyad, Puan, Süre, Cinsiyet gösterir; telefon ve e-posta hiçbir zaman gösterilmez.
- Sıralama: önce yüksek puan, eşitlikte kısa süre.

## 5. Sorun giderme

| Belirti | Yapılacak |
|---|---|
| Durum çubuğu **VR: Bulunamadı** | Quest Link bağlı mı, OpenXR runtime Meta mı? Düzeltin, **Yeniden Dene**'ye basın. |
| Eller görünmüyor, kumandalar çalışıyor | Link uygulamasında el takibi (Developer Runtime Features) açık mı? Kumandalar her zaman yedek olarak çalışır. |
| **Sunucu: Bağlantı yok** | İnternet / güvenlik duvarı (443). Oyun oynanmaya devam eder; sonuçlar kuyruğa alınır, bağlantı dönünce gönderilir. Bekleyen sayısı durum çubuğunda görünür. |
| Gönderim hatası "Erişim reddedildi" | `eventAccessCode` veritabanındaki `events.access_code` ile aynı değil ya da etkinlik pasif. |
| Ürün yere düştü / ulaşılamıyor | 3 saniye içinde otomatik olarak yerine döner. |
| Katılımcı vazgeçti | **Oturumu İptal Et** → **Yeni Katılımcı**. |
| Uygulama kapandı | Yeniden açın; gönderilmemiş sonuçlar `outbox` klasöründen otomatik gönderilir. |

Yerel kayıtlar: `%USERPROFILE%\AppData\LocalLow\ECR Etkinlik Bilgisayar\MultiTravel Valiz Challenge\`
- `outbox/` — henüz sunucuya ulaşmamış sonuçlar (JSON)
- `results-log.csv` — kişisel veri içermeyen oturum günlüğü (oturum id, puan, süre, cinsiyet, durum)

## 6. Etkinlik öncesi donanım testi (her istasyonda, zorunlu)

Aşağıdakiler geliştirme sırasında başlık olmadan doğrulanamadı; etkinlikten önce Meta Quest ile yapılmalıdır:

1. Uygulama açılınca durum çubuğu **VR: Hazır** gösteriyor; başlıkta oda ve valiz görünüyor.
2. Kafa yüksekliği doğal (oda zemini ayak hizasında); gerekirse Quest'te zemin yüksekliğini / Guardian'ı yeniden ayarlayın.
3. Eller (el takibi) görünüyor; bir ürüne uzanıp tutma (pinch/grab) ile alınabiliyor, bırakınca düşüyor.
4. Kumandalarla da (grip) tutma çalışıyor.
5. Üç raf katının (0,68 / 1,08 / 1,48 m) kısa ve uzun boylu katılımcılar için erişilebilir olduğu deneniyor.
6. Valize konan doğru ürün +10, yanlış ürün −5 yazıyor; ses ve titreşim geri bildirimi geliyor.
7. Gerekli ürünlerin tamamı konunca oyun bitiyor; sonuç operatör ekranında ve liderlik tablosunda görünüyor.
8. **Yeni Katılımcı** sonrası raflar ve valiz tamamen sıfırlanıyor.
9. VR panelindeki yazılar 2 m'den okunabiliyor; kare hızı akıcı (Link'te 72/90 Hz).
10. Test sonuçları canlı panoda görünürse Supabase Studio'dan silin (`results` / `participants`, `station_id` ile).

## 7. Etkinlik sonrası

- Supabase Studio'da `participants` ve `results` tablolarından tam dışa aktarma (`backend/supabase/README.md` içindeki SQL).
- Etkinliği kapatmak için `events.is_active = false` yapın; istasyonlar ve pano artık veri gönderemez/okuyamaz.
