# Yol Haritası

Durum: ✅ bitti · 🔄 sürüyor · ⏳ sırada

## Faz 0 — Çekirdek ve netcode temeli ✅

- ✅ **Unity'den bağımsız simülasyon çekirdeği.** .NET Standard 2.1, Unity ile birebir aynı derleme ayarları.
- ✅ **Source tarzı hareket:** sürtünme ve ivme, hava strafe'i, basamak çıkma, merdivende yere yapışma, eğilme, çömelerek zıplama, stamina, vurulunca yavaşlama, düşme hasarı.
- ✅ **Silahlar:** 33 silah, CS2 değerleriyle (fiyat, hasar, zırh delme, atış hızı, şarjör, hız, öldürme ödülü).
  - Sabit sprey desenleri; isabetsizlik modeli (hareket / zıplama / iniş / seri atış).
  - Burst modu, susturucu, dürbün, pompalıda tek tek fişek doldurma, R8 tetik bekletme.
- ✅ **Hasar:** hitbox'lar (kafa ×4, mide ×1.25, bacak ×0.75), zırh ve kask, menzil düşüşü, malzemeye göre duvar delme.
- ✅ **Netcode:** istemci tahmini (hareket + silahlar), uzlaştırma, interpolasyon, gecikme telafisi, tohumlanmış dağılım.
- ✅ **Hile korumaları:** komut bütçesi, zaman kredisi, geri sarma sınırı.
- ✅ **Sunucu ve test altyapısı:** saf .NET sunucu (64/128 tick), basit sunucu botları, UDP taşıma (LiteNetLib), test botlu sanal ağ.
- ✅ **Unity prototip istemcisi:** PC ve mobil giriş, kamera, geçici modeller ve efektler, geliştirici menüsü ve HUD.
- ✅ **Harita aracı ve test:** harita dışa aktarma aracı, test haritası.
- ✅ **38 otomatik test** + CI.

## Faz 1 — Rekabetçi oynanış ✅

- ✅ **Maç modları:** Rekabetçi (5v5, MR12, uzatma MR3), Basit (MR8), Ölüm Maçı (10 dk), Antrenman.
  - Isınma, donma süresi, 1:55 raund, devre arasında taraf değişimi, MVP.
- ✅ **CS2 ekonomisi:** kayıp bonusu serisi, kurma bonusu, süre dolunca hayatta kalan T'ye para yok.
  - Satın alma bölgesi ve süresi; rakip parası skor tablosunda gizli.
- ✅ **C4:** kurma (3.2 sn), 40 sn sayaç, imha (10 sn, kitle 5 sn).
  - Bomba düşürme ve alma, patlama hasarı mesafeyle azalır; bomba bilgisi rakipten gizlenir.
- ✅ **El bombaları:** HE, flaş (bakış yönü ve mesafe modeli), hacimsel sis (görüşü keser), molotof/yangın (sis söndürür), dekoy.
- ✅ **Yerdeki silahlar:** ölünce düşer, G ile bırakılır, E ile alınır.
- ✅ **Botlar:** çarpışma dünyasından otomatik üretilen navigasyon ağı ve A*.
  - Ekonomiye göre satın alma (tabanca / eko / zorunlu / tam alım).
  - Bölge planı, bomba kurma, geri alma ve imha; bomba atma; tepki süresi ve sprey kontrolü.
- ✅ **Rekabetçi harita "Kasaba":** iki bomba bölgesi, satın alma bölgeleri, 5'e 5 doğma noktaları.
- ✅ **49 otomatik test**; tam bot maçı Kasaba'da sonuna kadar oynanıyor.
- ⏳ Oyuncu-oyuncu çarpışması, sunucu tarafı demo kaydı (Faz 5'e taşındı).

## Faz 2 — Görseller: silahlar ve karakterler ⏳

- Özgün silah modelleri: tüm silahlar için birinci şahıs (viewmodel) ve üçüncü şahıs.
- Özgün karakter modelleri (2 taraf), rig ve animasyonlar: koşma, eğilme, zıplama, şarjör, atış, ölüm.
- Birinci şahıs animasyonları: çekme, inceleme, şarjör.
- Ses: 3B konumsal ayak sesleri ve silah sesleri, malzemeye göre çarpma sesleri.
- PC ve mobil için ayrı LOD ve kalite seviyeleri.

> Model üretimi Blender script'leriyle yapılacak. PC'de CS2 kalitesi hedefleniyorsa, prototip modeller sonrasında bir 3D sanatçının rötuşu önerilir.

## Faz 3 — Gerçek arayüz 🔄

- ✅ **UI Toolkit arayüzü** (sahne veya asset gerektirmez, tamamen koddan kurulur). Özgün VEXA tasarımı:
  - koyu zemin, asit yeşili vurgu, turuncu saldırı ve mavi savunma rengi, Barlow yazı tipi (OFL lisanslı).
- ✅ **Ana menü:** arkada haritanın üstünde dönen kamera, mod / harita / taraf / bot zorluğu / takım boyutu seçimi.
  - Sunucuya IP ile katılma, yükleme ekranı ve ipuçları.
- ✅ **HUD:** raund çubuğu (skor, süre, canlı oyuncular), haritadan otomatik üretilen dönen radar, para.
  - Öldürme akışı (kafa / duvar / sis / kör etiketleri), raund sonu ve MVP bandı.
  - Can ve zırh, envanter ve mermi, kurma ve imha çubuğu, hasar yönü göstergesi, flaş ve dürbün katmanı, ipuçları.
- ✅ **Satın alma menüsü:** 6 sütun, klavye kısayolları, sahip olunan / pahalı / dolu durumları.
- ✅ **Skor tablosu:** Ö/A/Ö(D), MVP, kafa yüzdesi, raund başına hasar, raund geçmişi.
- ✅ **Duraklatma menüsü** (takım değiştirme), maç sonu ekranı (zafer / mağlubiyet, maçın oyuncuları).
- ✅ **Ayarlar:** hassasiyet (CS ölçeği), nişangah editörü (önizleme, hazır ayarlar, paylaşım kodu), görüntü, FPS sınırı, ses, tuşlar.
- ✅ **Dünya görselleri (geçici):** uçan bombalar, sis, ateş, kurulu bomba (giderek hızlanan ışık), yerdeki silahlar.
- ⏳ Mobil: düzenlenebilir buton yerleşimi, jiroskopla nişan.
- ⏳ Tuş atama ekranı, ses paketi.

## Faz 4 — Haritalar ⏳

- 2–3 rekabetçi haritanın blockout'u ve oynanış testleri, ardından sanat geçişi.
- Eğimli yüzey ve rampa desteği (üçgen mesh çarpışması).

## Faz 5 — Online altyapı ⏳

- Hesaplar (rally.gg ile giriş), arkadaşlar, lobiler.
- Eşleştirme ve rank sistemi (Glicko-2). **PC ve mobil ayrı havuzlarda.**
- Sunucu orkestrasyonu: Docker, bölgesel otomatik ölçekleme.
- Hile koruması 2. seviye: görünürlük filtresi, aimbot istatistikleri, rapor ve inceleme.
- İzleyici (GOTV benzeri, gecikmeli yayın), demo ve tekrar oynatıcı.

## Faz 6 — Turnuva ve rally.gg entegrasyonu ⏳

- **Turnuva sunucu modu:** maç yapılandırması (JSON), bıçak turu, duraklatma, teknik mola, uzatma ayarları, takım isimleri.
- **rally.gg API'si:** maç oluşturma → sunucu ayağa kaldırma → sonuç ve istatistiklerin webhook ile gönderimi.
- Turnuva hakem paneli, izleyici linkleri.

## Faz 7 — Beta ve çıkış ⏳

- Kapalı beta, denge ayarları, performans profilleme.
- PC: Steam ve/veya Epic. Mobil: Google Play ve App Store.

---

**rally.gg entegrasyonu için gerekenler:** sitenin hesap ve turnuva API'si (endpoint'ler, kimlik doğrulama yöntemi) ve maç sonuçlarının nereye yazılacağı.
