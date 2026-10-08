# Mimari

## Temel ilke: oyun Unity'den bağımsız bir çekirdekte çalışır

Oynanışla ilgili her şey `Game/Assets/Vexa/Core` içinde, saf C# (.NET Standard 2.1, `System.Numerics`) olarak yazılı:

- hareket
- silahlar
- sekme ve isabetsizlik
- hasar ve zırh
- duvar delme
- çarpışma
- netcode

Bu kod üç yerde **birebir aynı** şekilde çalışır:

| Yer | Ne yapar |
|---|---|
| **Unity istemcisi** (PC + mobil) | Yerel oyuncuyu tahmin eder |
| **Bağımsız sunucu** (`Server/`, saf .NET, Linux) | Otoritedir; Unity lisansı veya GPU gerektirmez, ucuz ve 128 tick'e hazır |
| **Testler** (`Tests/`) | Her değişiklikte otomatik doğrulanır |

İstemci ve sunucu aynı çarpışma verisini (`.vxmap`) ve aynı simülasyon fonksiyonunu (`PlayerSimulation.Step`) kullandığı için istemci tahmini sunucuyla tutar. Testlerde 90 ms gecikme, ±20 ms jitter ve %5 paket kaybı altında 12 saniyelik yoğun bir oturumda yalnızca 1 düzeltme oluşuyor; o da bağlantı anındaki ilk senkronizasyon.

## Katmanlar

```
┌──────────────── Unity (Vexa.Client) ────────────────┐
│ InputSampler · LocalPlayerCamera · PlayerAvatar     │  sunum katmanı
│ ShotEffects · WorldVisuals · GameSession · VexaApp  │
│ UI/ (UI Toolkit): UiRoot → menüler, HUD, radar,     │
│     satın alma, skor tablosu, ayarlar               │
└───────────────┬─────────────────────────────────────┘
                │ PlayerInput / durum okuma / olaylar
┌───────────────▼──────────── Vexa.Core ──────────────┐
│ ClientGame  ── tahmin, uzlaştırma, interpolasyon     │
│ ServerGame  ── otorite, gecikme telafisi, hile koruması │
│ PlayerSimulation = PlayerMovement + WeaponLogic     │
│ CollisionWorld · Hitboxes · DamageModel · Weapons   │
│ Protocol / NetWriter / NetReader / ITransport       │
└───────────────┬─────────────────────────────────────┘
                │ ITransport
┌───────────────▼──────────── Vexa.Net ───────────────┐
│ LiteNetTransport (UDP, LiteNetLib)                   │
│ LoopbackNetwork (testlerde gecikme/kayıp simülasyonu)│
└─────────────────────────────────────────────────────┘
```

## Netcode (CS / Source modeli)

**1. Komutlar (istemci → sunucu, her tick):** İstemci her tick'te bir `PlayerInput` üretir. İçinde şunlar var:
- tuşlar
- nicemlenmiş bakış açıları
- silah seçimi
- komut numarası
- `InterpTick`: istemcinin diğer oyuncuları o an hangi sunucu zamanında gördüğü

Her paket son 4 komutu tekrar taşır; böylece kayıp paketler telafi edilir.

**2. İstemci taraflı tahmin:** İstemci, komutu gönderirken aynı komutla kendi oyuncusunu hemen simüle eder. Tahmin edilenler:
- hareket
- ateş etme ve mermi sayısı
- şarjör değiştirme
- silah değiştirme
- sekme ve dürbün

Bakış açıları sunucuyla bit-bit aynı olsun diye istemci, açıları önce nicemleyip sonra simüle eder.

**3. Anlık görüntü (sunucu → istemci):** Sunucu her tick şunları gönderir:
- sunucu tick'i
- işlediği son komut numarası (`ack`)
- o oyuncunun **tam** durumu (128 bayt)
- diğer oyuncuların durumu

**4. Uzlaştırma:** İstemci, `ack` anındaki kendi tahminini sunucu durumuyla karşılaştırır. Fark varsa sunucu durumuna geri sarar ve onaylanmamış komutları yeniden oynatır. Küçük düzeltmeler görsel olarak yumuşatılır (`CorrectionOffset`), büyükler (doğma, ışınlanma) anında uygulanır.

**5. Diğer oyuncular:** ~3 tick geride, iki anlık görüntü arasında interpolasyonla çizilir. Ekstrapolasyon yok; adil olsun diye kimse "tahmin edilmiş" bir konumda vurulmaz.

**6. Gecikme telafisi:** Sunucu, her oyuncunun hitbox pozunu son 128 tick boyunca saklar (`PoseHistory`). Bir atış geldiğinde diğer oyuncuları atışı yapanın gördüğü ana (`InterpTick`) geri sarar ve isabeti orada hesaplar. Geri sarma en fazla 250 ms ile sınırlıdır.
- Test: 100 ms gecikmede hareketli hedefe 7/7 isabet; telafi kapatılınca 0/7.

**7. Dağılımın tohumlanması:** Merminin dağılım (spread) yönleri, oyuncu ve atış sayacından üretilen bir tohumla hesaplanır. Bu sayede istemcinin çizdiği mermi izi ve duvar delikleri sunucunun gerçek mermileriyle aynı yerde olur.

## Hile koruması (sunucu otoritesi)

- **Konum, isabet, hasar, mermi ve ateş hızı tamamen sunucuda.** İstemci sadece tuş ve açı gönderir.
- **Komut bütçesi:** Sunucu tick başına ortalama en fazla 1 komut simüle eder (hız hilesi).
  - Test: 3 kat hızlı istemci 192 tick'te sadece 38 komut işletebildi.
- **Zaman kredisi:** Silah zamanlayıcıları komut zamanıyla çalışır. Komut numarası atlayarak hızlı ateş etmeyi önlemek için her komut, sunucu zamanına göre kazanılmış krediden harcar (en fazla 0,5 sn).
  - Test: 600 RPM tüfekle hızlı ateş denemesi 2 saniyede 4 atışta kaldı.
- **Geri sarma penceresi sınırlı** (250 ms). Açılar sınırlandırılmış (pitch ±89°).
- **İleride eklenecekler:**
  - görünürlük filtresi: görünmeyen düşmanı göndermeme (wallhack'e karşı)
  - istatistiksel aimbot tespiti
  - demo kayıtları
  - istemci bütünlük kontrolleri

## Arayüz (UI Toolkit)

Arayüz `Client/UI` altında tamamen C# ile kurulur. UXML/USS dosyası, sahne veya prefab gerekmez; `PanelSettings` çalışma anında oluşturulur (1920×1080 referans, ekrana göre ölçeklenir).

- **`UiRoot`** hangi ekranın görüneceğine karar verir: ana menü → yükleme → oyun (HUD ve üstündeki satın alma, skor, duraklatma, maç sonu katmanları). Esc / B / Tab / M tuşlarını ve imleç kilidini o yönetir. Bir menü açıkken `GameSession.InputBlocked` ile oyun girdisi durur.
- **Ekranlar yalnızca okur:** HUD her karede `ClientGame` durumunu (tahmin edilen oyuncu, maç başlığı, bomba, skorlar) okur ve olaylara (öldürme, isabet, raund sonu, oyun olayları) abone olur. Satın alma gibi eylemler `ClientGame.RequestBuy` / `SelectTeam` ile sunucuya gider; karar her zaman sunucunundur.
- **Radar** her harita için otomatik üretilir: çarpışma dünyasından `NavGrid` kurulur, yürünebilir hücreler yüksekliğe göre gölgelenip dokuya çizilir.
- **Tema** (`Theme`, `Fonts`, `U` yardımcıları) tek yerde. Renkler: zemin `#0A0D12`, vurgu `#D7FF3C`, saldırı `#F2A33A`, savunma `#5AB0FF`. Yazı tipleri: Barlow ve Barlow Condensed (OFL).
- **Ayarlar** (`VexaSettings`) `PlayerPrefs`'te tutulur. Nişangah `VX-...` paylaşım koduna çevrilebilir.

## Turnuva, sohbet ve demo

- **Turnuva katmanı** (`Server/ServerTournament.cs`): hazır olma, bıçak raundu ve taraf seçimi, molalar, kadrolar, takım isimleri, sonuç JSON'u.
  - Hepsi sohbet komutlarıyla sürülür, yani her istemci aynı şekilde kullanabilir.
  - Molalar CS'teki gibi donma süresinde başlar. Teknik duraklatmada saat durur.
  - Ayarlar `MatchConfig.FromJson` ile maç dosyasından okunur (çekirdekte bağımlılıksız küçük bir JSON okuyucu var: `MiniJson`).
- **Sohbet:** sunucu filtreler. Takım kanalı sadece takıma gider. Canlı raundda ölülerin mesajı canlılara ulaşmaz. Saniyede birkaç satırdan fazlası kesilir.
- **Demo:** sunucunun taşıma katmanında bir `DemoTap` vardır. Gizli bir izleyici oyuncuya ("VEXA TV") giden her paket zamanıyla birlikte `.vxdemo` dosyasına yazılır.
  - Oynatma, `DemoPlayback` taşıması ile aynı paketleri sıradan bir `ClientGame`'e verir.
  - Bu yüzden demo, canlı izleyicilikle birebir aynı kodla çizilir; ayrı bir oynatıcı mantığı yoktur.
  - Geri sarma, demoyu baştan hızlıca tekrar oynatır.
- **Oyuncu çarpışması:** diğer oyuncuların gövde kutuları hareket simülasyonuna "dinamik engel" olarak verilir (mermiler ve görüş hattı bunları görmez).
  - İstemci kendi tahmininde rakipleri gördüğü konumda kullanır.

## Görseller ve ses

- **Modeller** `Tools/Blender` script'leriyle üretilip `Resources/Models` altına FBX olarak yazılır. Mobil sürüm ayrı, daha düşük poligonlu dosyalardır.
  - Dokular modelle birlikte pişirilir: `Textures/<ad>_albedo`, `_mask.png` (R metal, A pürüzsüzlük), `_normal` (sadece PC).
    - PC'de albedo ve normal 4K JPEG'dir (depo boyutu için), maske PNG'dir.
  - Oyun bunlardan çalışma anında URP Lit malzemesi kurar.
  - `Editor/VexaModelImport.cs` içe aktarma ayarlarını otomatik yapar: normal haritası, doğrusal maske, PC'de 4K (mobil platformlarda 1K), döngüye giren animasyonlar.
  - `ModelLibrary` bunları yükler. Model yoksa ilkel şekillere geri düşülür; oyun yine çalışır.
- **Karakter hattı (v3)** (`vexa_human` → `vexa_outfit` → `vexa_factions` → `vexa_characters`):
  1. MakeHuman'ın CC0 temel insan mesh'i morph hedefleriyle atletik bir askere şekillenir (hacmi kıyafet ve teçhizat verir).
  2. Geçici bir iskeletle nişan pozuna getirilir: iki kemikli IK ile eller kabza ve el kundağında, parmaklar silahı kavrar.
  3. Kıyafetler gövdeden bir beden büyük kesilir ve Blender'ın kumaş çözücüsüyle (`vexa_cloth`) yerçekimiyle gövdeye dökülür.
     - Yaka, manşet ve bel kenarları sabitlenir; kumaş omuz ve kalçaya oturur, aşağı sarkar, kendi kırışıklarını oluşturur.
     - Üstüne eklem kırışıkları ve dikişler eklenir.
     - Katman sırası: gömlek < pantolon < kemer < yelek < cep.
     - Botlar ayak ve bileğin dışbükey kabuklarından yeniden örülür (parmak izi kalmaz).
  4. Teçhizat ışın atışıyla yüzeye oturtulur. Sonuç ~0,5 milyon üçgenlik yüksek poligonlu kaynaktır.
  5. Oyun mesh'i bundan seyreltilir (PC ~80 bin, mobil ~10 bin üçgen).
     - Görünmeyen iç kabuklar atılır; küçük sert parçalar (mercek, toka) seyreltilmez.
     - Katmanlar birbirine geçmesin diye düzeltilir.
     Ağırlıklar otomatik ağırlıklandırılmış gövdeden aktarılır, tüm dokular yüksek poligondan pişirilir.
  6. UV atlası seyreltmeden önce kurulur: gövde ve kıyafetler MakeHuman'ın vücut UV'sini kullanır, yüz ve eller daha fazla piksel alır.
- **Karakter animasyonu** `PlayableGraph` ile yapılır (Animator Controller gerektirmez): klipler ağdan gelen hız, eğilme ve zemin durumuna göre karıştırılır.
  - Nişan için göğüs kemiği eğilir. Silah `RightGrip` kemiğine (sağ elin kavrama noktası) yerleşir.
  - **Kıyafet fiziği** (`Art/SpringBones`): `Jiggle_*` kemikleri (telsiz anteni, boşaltma çantası, bacak kılıfı, kayış uçları, bez) yay benzetimiyle gövdenin hareketine gecikmeyle uyar, yerçekimiyle sallanır.
- **Birinci şahıs** (`ViewModel`): silah ve kollar 1/4 ölçekte, kameraya 4 kat daha yakın çizilir. Görüntü aynıdır ama model oyuncunun kendi gövdesinin içinde kalır, duvara girmez.
- **Ses** (`Audio/SoundSynth`, `GameAudio`): şimdilik tüm sesler kodla sentezlenir ve 3B konumsal çalınır. Ses paketi gelince aynı isimlerle değiştirilecek.

## Harita hattı

1. Haritalar Unity'de normal objelerle tasarlanır. Çarpışma için BoxCollider kullanılır; malzeme seçimi `VexaSurface` ile, doğuş noktaları `VexaSpawn` ile, bomba bölgeleri ve satın alma alanları `VexaZone` ile işaretlenir.
2. **VEXA → Haritayı Dışa Aktar** menüsü `.vxmap` dosyası üretir. Bunu hem sunucu hem istemci yükler.
3. Görsel model (yüksek detaylı mesh'ler, ışık, dekorasyon) çarpışmadan bağımsızdır. PC ve mobil aynı çarpışmayı, farklı görsel kalitede kullanır.

Şu an çarpışma eksen hizalı kutulardan oluşuyor. Rampalar ve eğik yüzeyler için üçgen mesh desteği yol haritasında.

## Birimler

Çekirdek metre kullanır (Y yukarı, +Z ileri; Unity ile aynı). CS değerleri "hammer unit" olarak yazılır ve `VMath.HU = 0.0254` ile çevrilir:
- koşu hızı 250 HU/s
- zıplama ~57 HU
- basamak 18 HU
