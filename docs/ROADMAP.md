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
    - Karakter yaklaşık 9 bin, silah 1–4 bin üçgen.
    - 256–1024 px renk ve metal/pürüzsüzlük dokusu.
  - **PC:** CS2 tarzı.
    - Karakter yaklaşık 60 bin üçgen (yaklaşık 0,5 milyon üçgenlik yüksek poligondan pişirilir), silah 5–25 bin üçgen.
    - 4K PBR dokular: renk, metal/pürüzsüzlük, normal haritası.
    - Silahlarda aynı piksel yoğunluğu: tüfek / SMG / ağır silah 4K, tabanca ve bıçak 2K, bombalar 1K.
- ✅ **Dokular Blender'da otomatik pişiriliyor** (`Tools/Blender/vexa_textures.py`):
  - metalde kenar aşınması ve çizikler, ahşapta damar, kumaşta dokuma, kırışık ve bacaklarda toz
  - ekoseli gömlek, örgü kar maskesi, kamuflaj
  - ten (MakeHuman CC0 cilt dokusu + gözenek, sakal, kaş, kısa saç), gerçekçi gözler (iris, gözbebeği)
  - polimerde pütürlü yüzey, girintilerde kir (ambient occlusion)
- ✅ **Özgün silah modelleri:** 33 silah, 6 el bombası, C4, imha kiti ve bıçak.
  - Blender script'iyle parametrik olarak üretiliyor (`Tools/Blender/vexa_weapons.py`).
  - PC detayları gerçek kesiklerle (boolean): kovan çıkış penceresi ve içinde mekanizma, alt/üst gövde ayrım çizgisi, namlu freni ve alev gizleyici yarıkları.
    - El kundağında havalandırma ve M-LOK yuvaları, kızakta tırtıllar, nişangahlarda nokta.
  - Ayrıca: şarjör kaburgaları ve kontrol delikleri, dürbün halkaları ve tırtıllı kuleler.
    - Yuvarlatılmış ahşap/polimer dipçik ve kabzalar (parmak yuvaları).
    - AK tipi: arpacık kulesi, gaz bloğu, temizleme çubuğu, perçinler.
    - AR tipi: ileri itme düğmesi, kovan saptırıcı, kurma kolu.
  - Namlu ucu, destek eli ve kovan çıkışı için işaret noktaları var.
- ✅ **Gerçekçi karakterler (v3):** MakeHuman'ın CC0 temel insan mesh'inden atletik askerler; iki taraf da özgün tasarım.
  - Bol kıyafetler kumaş simülasyonuyla gövdeye dökülür: omuza ve kalçaya oturur, aşağı sarkar, doğal kırışır.
  - **Akıncı** (saldırı): örgü kar maskesi, kolları sıvanmış ekoseli gömlek, açık renk plaka yeleği, haki kargo pantolon, kemer ve tabanca kılıfı.
  - **Muhafız** (savunma): başlık ve tam yüz gaz maskesi (çift cam, yan filtre, kafa kayışları), lacivert üniforma.
    - Cepli plaka yeleği, telsiz ve anten, dizlik, bacak kılıfı, boşaltma çantası.
  - Eller silahı gerçekten kavrar (IK + parmak pozları). Kıyafetler katmanlı, kırışık ve dikişli.
  - Rig'li; 7 animasyonu var: bekleme, yürüme, koşma, eğilme, eğilerek yürüme, zıplama, ölüm. Ölçüler sunucu hitbox'larıyla uyumlu.
  - **Kıyafet fiziği:** anten, çanta, kılıf, kayış uçları ve bez yay benzetimiyle sallanır (mobilde de ucuz).
- ✅ **Birinci şahıs:** silah ve eller.
  - Kollar karakterin kendisinden kesilir: aynı kol, eldiven ve kavrama, 2K doku.
  - Kod ile hareket: sallanma, yürüme salınımı, geri tepme, şarjör, çekme, inceleme, bomba hazırlama.
  - Duvarların içine girmez.
- ✅ **Üçüncü şahıs:** animasyon karışımı ağ pozundan sürülüyor; nişan için üst gövde eğiliyor, silah elin kavrama noktasında.
- ✅ **Ses (geçici, sentez):**
  - 3B konumsal silah ve ayak sesleri (zemine göre; yürüme ve eğilme sessiz).
  - Şarjör, bombalar, giderek hızlanan C4 bipi, isabet ve öldürme geri bildirimi, raund sesleri.
- ⏳ Kaydedilmiş/tasarlanmış ses paketi; el animasyonları için kemikli kollar; silahlar için silah başına elle ayarlanmış model geçişi (CS2 seviyesine yaklaşmak için).

> Modeller tekrar üretilebilir. Önce `pip install bpy`, sonra `python Tools/Blender/vexa_characters.py --out Game/Assets/Vexa/Resources/Models --textures`
> ve aynı şekilde `vexa_weapons.py`. 4K dokularla tüm set CPU'da yaklaşık 1,5–2 saat sürer.
> Karakter gövdesi MakeHuman'ın CC0 verisinden gelir (`Tools/Blender/data/MAKEHUMAN_CC0.md`); kıyafet ve teçhizatın tamamı script ile üretilir.

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
