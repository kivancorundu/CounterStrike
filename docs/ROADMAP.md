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

## Faz 2 — Görseller: silahlar ve karakterler 🔄

- ✅ **İki detay seviyesi:**
  - **Mobil:** orta poligon (CS 1.6 yoğunluğu ama yuvarlatılmış köşeler).
    - Karakter yaklaşık 6–8 bin, silah 1–4 bin üçgen.
    - 512–1024 px renk ve metal/pürüzsüzlük dokusu.
  - **PC:** CS2 tarzı.
    - Karakter yaklaşık 28–35 bin, silah 3–12 bin üçgen.
    - İnce detaylar (vida, pim, kabze dokusu, şarjör kanalları, seçici kol).
    - 1024–2048 px PBR dokular: renk, metal/pürüzsüzlük, normal haritası.
- ✅ **Dokular Blender'da otomatik pişiriliyor** (`Tools/Blender/vexa_textures.py`):
  - metalde kenar aşınması ve çizikler, ahşapta damar, kumaşta dokuma ve kırışıklar
  - polimerde pütürlü yüzey, girintilerde kir (ambient occlusion)
- ✅ **Özgün silah modelleri:** 33 silah, 6 el bombası, C4, imha kiti ve bıçak.
  - Blender script'iyle parametrik olarak üretiliyor (`Tools/Blender/vexa_weapons.py`).
  - Namlu ucu, destek eli ve kovan çıkışı için işaret noktaları var.
- ✅ **Özgün karakterler:** iki taraf. Gövde tek parça organik bir mesh (skin modifier + subdivision), iskelete otomatik ağırlıklandırılmış; kafa ayrı heykellenmiş (kafatası, çene, burun, kulaklar).
  - **Akıncı** (saldırı): kum rengi ceket, kep ve gözlük.
  - **Muhafız** (savunma): lacivert üniforma, vizörlü kask ve plaka yeleği.
  - İkisi de rig'li; 7 animasyonu var: bekleme, yürüme, koşma, eğilme, eğilerek yürüme, zıplama, ölüm.
  - Ölçüler sunucu hitbox'larıyla birebir aynı.
- ✅ **Birinci şahıs:** silah ve eller.
  - Kod ile hareket: sallanma, yürüme salınımı, geri tepme, şarjör, çekme, inceleme, bomba hazırlama.
  - Duvarların içine girmez.
- ✅ **Üçüncü şahıs:** animasyon karışımı ağ pozundan sürülüyor; nişan için üst gövde eğiliyor, silah elde.
- ✅ **Ses (geçici, sentez):**
  - 3B konumsal silah ve ayak sesleri (zemine göre; yürüme ve eğilme sessiz).
  - Şarjör, bombalar, giderek hızlanan C4 bipi, isabet ve öldürme geri bildirimi, raund sesleri.
- ⏳ Kaydedilmiş/tasarlanmış ses paketi; el animasyonları için kemikli kollar; bir 3D sanatçıdan yüz ve kumaş rötuşu.

> Modeller tekrar üretilebilir. Önce `pip install bpy`, sonra `python Tools/Blender/vexa_weapons.py --out Game/Assets/Vexa/Resources/Models --textures`
> ve aynı şekilde `vexa_characters.py`. Dokularla birlikte tüm set yaklaşık 1 saat sürer. PC'de CS2 seviyesi için bu modeller bir 3D sanatçının rötuşu ve dokularıyla tamamlanmalı.

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
- ✅ **Mobil:** sürükle-bırak buton yerleşimi düzenleyici, buton opaklığı, jiroskopla nişan (ters çevirme seçenekleri).
- ✅ **Tuş atama ekranı:** her eylem değiştirilebilir, fare tuşları dahil.
- ✅ **Sohbet arayüzü, izleyici paneli, turnuva durum şeridi, demo oynatıcı ve demo listesi.**

## Faz 4 — Haritalar ⏳

- 2–3 rekabetçi haritanın blockout'u ve oynanış testleri, ardından sanat geçişi.
- Eğimli yüzey ve rampa desteği (üçgen mesh çarpışması).

## Faz 5 — Online altyapı 🔄

- ✅ **Oyuncu-oyuncu çarpışması** (istemci tahminli).
- ✅ **Sohbet:** herkes ve takım kanalları; ölüler canlı raundda sadece ölülerle konuşur.
- ✅ **İzleyici modu:** ölünce takım arkadaşlarını izleme (göz / üçüncü şahıs); izleyiciler herkesi izler.
- ✅ **Demo:** sunucu "VEXA TV" kaydı (.vxdemo).
  - Oyunda DEMOLAR menüsünden oynatılıyor: duraklatma, 0.25–4× hız, raund atlama, zaman çubuğu.
- ⏳ Hesaplar (rally.gg ile giriş), arkadaşlar, lobiler.
- ⏳ Eşleştirme ve rank sistemi (Glicko-2). **PC ve mobil ayrı havuzlarda.**
- ⏳ Sunucu orkestrasyonu: Docker, bölgesel otomatik ölçekleme.
- ⏳ Hile koruması 2. seviye: görünürlük filtresi, aimbot istatistikleri, rapor ve inceleme.
- ⏳ Canlı yayın için gecikmeli izleyici sunucusu.

## Faz 6 — Turnuva ve rally.gg entegrasyonu 🔄

- ✅ **Turnuva sunucu modu** (`--config maç.json`, örnek: `Server/Vexa.Server/Examples/tournament-match.json`):
  - takım isimleri, kadrolar (listede olmayan izleyici olur), hazır olma (`.ready`)
  - bıçak raundu ve taraf seçimi (`.stay` / `.switch`)
  - taktik mola (`.tac`, takım başına 3) ve teknik duraklatma (`.tech` / `.unpause`); ikisi de donma süresinde başlar
  - uzatma ve raund ayarları
- ✅ **Sonuç dosyası (JSON):** takımlar, skorlar, oyuncu istatistikleri, raund raund geçmiş. Kodda `MatchFinished` olayı da var.
- ✅ **Sunucu konsolu:** `status`, `say`, `pause`, `unpause`, `forceready`, `restart`, `kick`, `record`.
- ⏳ **rally.gg API'si:** maç oluşturma → sunucu ayağa kaldırma → sonucun webhook ile gönderimi (API bilgisi bekleniyor; sonuç JSON'u hazır).
- ⏳ Turnuva hakem paneli, izleyici linkleri.

## Faz 7 — Beta ve çıkış ⏳

- Kapalı beta, denge ayarları, performans profilleme.
- PC: Steam ve/veya Epic. Mobil: Google Play ve App Store.

---

**rally.gg entegrasyonu için gerekenler:** sitenin hesap ve turnuva API'si (endpoint'ler, kimlik doğrulama yöntemi) ve maç sonuçlarının nereye yazılacağı.
