-- 3) TÜM KATILIMCILAR - TEMİZ LİSTE (21-22.10.2026): her kişi tek satır.
-- Aynı kişi birden fazla kaydolduysa (aynı e-posta, yoksa aynı telefon) en son kaydı alınır; oyun sayısı ve en iyi puanı
-- tüm kayıtlarından hesaplanır. Oyun oynamadan kaydolanlar da listededir (oyun_sayisi = 0).
-- Supabase -> SQL Editor -> yapıştır -> Run -> Download -> CSV. Deneme kayıtları çıkarılır.
with base as (
  select p.*, coalesce(nullif(lower(trim(p.email)), ''), p.phone) as kisi
    from public.participants p
    join public.events e on e.id = p.event_id
   where e.slug = 'multitravel-2026'
     and (p.created_at at time zone 'Europe/Istanbul')::date between date '2026-10-21' and date '2026-10-22'
   and not (lower(p.first_name || ' ' || p.last_name) ~ '^dene' or (p.first_name || ' ' || p.last_name) ~ '[0-9]'
        or lower(p.first_name || ' ' || p.last_name) in ('anıl altıkat', 'jhdehdfjk fdsf', 'hsddesf duhush', 'duehfd fff',
                                                         'erğyıwş çkhçh', 'nfewfb jfej', 'onur korkmaz'))
),
games as (
  select b.kisi,
         count(r.id) as oyun_sayisi,
         max(r.score) as en_iyi_puan,
         (array_agg(r.completion_ms order by r.score desc, r.completion_ms asc) filter (where r.id is not null))[1] as en_iyi_sure_ms
    from base b
    left join public.results r on r.participant_id = b.id and r.status = 'completed'
   group by b.kisi
),
latest as (
  select distinct on (b.kisi) b.*
    from base b
   order by b.kisi, b.created_at desc
)
select l.first_name as ad, l.last_name as soyad, l.title as unvan, l.company as kurum, l.location as lokasyon,
       l.phone as telefon, l.email as eposta,
       case l.gender when 'female' then 'Kadın' else 'Erkek' end as urun_seti,
       g.oyun_sayisi, g.en_iyi_puan, round(g.en_iyi_sure_ms / 1000.0, 1) as en_iyi_sure_sn,
       to_char(l.created_at at time zone 'Europe/Istanbul', 'DD.MM.YYYY HH24:MI') as kayit_zamani,
       case when l.consent_accepted then 'Evet' else 'Hayır' end as kvkk_onay
  from latest l
  join games g on g.kisi = l.kisi
 order by l.last_name, l.first_name;
