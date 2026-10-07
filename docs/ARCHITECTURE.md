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
│ ShotEffects · VexaApp (geçici menü/HUD)             │
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
