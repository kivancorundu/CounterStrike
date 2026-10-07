# Yol Haritası

Durum: ✅ bitti · 🔄 sürüyor · ⏳ sırada

## Faz 0 — Çekirdek ve netcode temeli ✅ (bu commit)

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

## Faz 1 — Rekabetçi oynanış ⏳

- Takımlar ve 5v5 maç akışı: donma süresi, 1:55 tur, MR12, devre arası, uzatma.
- Ekonomi: CS2 kuralları (web prototipinde var, çekirdeğe taşınacak); satın alma bölgesi ve süresi.
- C4: kurma (3.2 sn), imha (10/5 sn), bomba düşürme ve alma.
- Bombalar, istemci tahminli atışla: HE, flaş, hacimsel sis, molotof, dekoy.
- Oyuncu-oyuncu çarpışması, silah yere atma ve alma.
- Tam bot yapay zekâsı (web prototipindeki taktik botların çekirdeğe taşınması).
- Sunucu tarafı demo kaydı.

## Faz 2 — Görseller: silahlar ve karakterler ⏳

- Özgün silah modelleri: tüm silahlar için birinci şahıs (viewmodel) ve üçüncü şahıs.
- Özgün karakter modelleri (2 taraf), rig ve animasyonlar: koşma, eğilme, zıplama, şarjör, atış, ölüm.
- Birinci şahıs animasyonları: çekme, inceleme, şarjör.
- Ses: 3B konumsal ayak sesleri ve silah sesleri, malzemeye göre çarpma sesleri.
- PC ve mobil için ayrı LOD ve kalite seviyeleri.

> Model üretimi Blender script'leriyle yapılacak. PC'de CS2 kalitesi hedefleniyorsa, prototip modeller sonrasında bir 3D sanatçının rötuşu önerilir.

## Faz 3 — Gerçek arayüz ⏳

- CS2 tarzı HUD (UI Toolkit): radar, öldürme akışı, satın alma menüsü, skor tablosu, nişangah editörü.
- Mobil arayüz: düzenlenebilir buton yerleşimi, jiroskopla nişan seçeneği.
- Ayarlar: grafik, ses, kontroller, hassasiyet.

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
