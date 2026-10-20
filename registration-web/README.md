# MultiTravel: Packing Challenge — kayıt sayfası (web)

Katılımcılar oyuna bu sayfadan kaydolur; VR istasyonunun görevli ekranı (TV'ye yansıtılan ekran) hiçbir kişisel
veri göstermez, sadece "Sıradaki Katılımcılar" listesini ("Ayşe Y.") gösterir.

- Canlı adres: https://multitravel-kayit.vercel.app (Vercel projesi `multitravel-kayit`)
- Sunucu: aynı Supabase projesi; `backend/supabase/migrations/20261020120000_web_registration.sql`
  (SQL Editor için tek dosya: `backend/UPGRADE_20261020_WEB_REGISTRATION.sql`).
- Akış: `enqueue_participant` (sayfa) → `list_waiting_participants` / `claim_participant` (VR istasyonu) →
  oyun sonucu aynı `client_session_id` ile `submit_result`.

## Cihaz kurulumu

Sayfa ilk açılışta **etkinlik kodunu** ister (Supabase `events.access_code`). Kod o tarayıcıda saklanır; kodu bilmeyen
kayıt yapamaz. Kodu değiştirmek için sayfanın altındaki "Cihaz ayarı".

## Yayınlama

`config.js` git'te boş durur. Gerçek değerler `Deployment/registration.config.js` dosyasındadır (git'e girmez):

```sh
tools/deploy-registration.sh
```
