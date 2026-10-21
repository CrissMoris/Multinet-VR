-- MultiTravel: Packing Challenge - günün sonuçları, şirket ve ünvan bilgisiyle.
-- Supabase -> SQL Editor -> bu sorguyu yapıştırın -> Run -> sonuç tablosunun altındaki "Download" -> CSV.
-- Başka bir gün için '2026-10-21' tarihini değiştirin. Saatler İstanbul saatidir.
-- Deneme kayıtları (ekip denemeleri, rastgele harfler) "not in" listesiyle çıkarılır.

select row_number() over (order by r.score desc, r.completion_ms asc, r.completed_at asc) as sira,
       p.first_name  as ad,
       p.last_name   as soyad,
       p.title       as unvan,
       p.company     as kurum,
       p.location    as lokasyon,
       r.score       as puan,
       round(r.completion_ms / 1000.0, 1) as sure_sn,
       r.correct_count   as dogru_urun,
       r.incorrect_count as yanlis_urun,
       case r.gender when 'female' then 'Kadın' else 'Erkek' end as urun_seti,
       to_char(r.completed_at at time zone 'Europe/Istanbul', 'HH24:MI') as oyun_saati
  from public.results r
  join public.participants p on p.id = r.participant_id
  join public.events e       on e.id = r.event_id
 where e.slug = 'multitravel-2026'
   and r.status = 'completed'
   and (r.completed_at at time zone 'Europe/Istanbul')::date = date '2026-10-21'
   and lower(p.first_name || ' ' || p.last_name) not in (
         'anıl altıkat', 'multinet 1', 'deneme 1', 'dene den', 'jhdehdfjk fdsf', 'hsddesf duhush', 'duehfd fff',
         'fhye8y fgeyf', 'erğyıwş çkhçh', 'nfewfb jfej', 'onur korkmaz')
 order by sira;
