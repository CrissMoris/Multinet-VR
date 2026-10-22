-- 1) 2. GÜN (22.10.2026) OYUN LİSTESİ - tamamlanan her oyun bir satır, puan ve süreye göre sıralı.
-- Supabase -> SQL Editor -> yapıştır -> Run -> sonuç tablosunun altında Download -> CSV.
-- Deneme kayıtları (deneme / denem / rakamlı adlar, ekip denemeleri) çıkarılır.
select row_number() over (order by r.score desc, r.completion_ms asc, r.completed_at asc) as sira,
       p.first_name as ad, p.last_name as soyad, p.title as unvan, p.company as kurum, p.location as lokasyon,
       r.score as puan, round(r.completion_ms / 1000.0, 1) as sure_sn,
       r.correct_count as dogru_urun, r.incorrect_count as yanlis_urun,
       case r.gender when 'female' then 'Kadın' else 'Erkek' end as urun_seti,
       to_char(r.completed_at at time zone 'Europe/Istanbul', 'HH24:MI') as oyun_saati
  from public.results r
  join public.participants p on p.id = r.participant_id
  join public.events e       on e.id = r.event_id
 where e.slug = 'multitravel-2026'
   and r.status = 'completed'
   and (r.completed_at at time zone 'Europe/Istanbul')::date = date '2026-10-22'
   and not (lower(p.first_name || ' ' || p.last_name) ~ '^dene' or (p.first_name || ' ' || p.last_name) ~ '[0-9]'
        or lower(p.first_name || ' ' || p.last_name) in ('anıl altıkat', 'jhdehdfjk fdsf', 'hsddesf duhush', 'duehfd fff',
                                                         'erğyıwş çkhçh', 'nfewfb jfej', 'onur korkmaz'))
 order by sira;
