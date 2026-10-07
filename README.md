# VEXA

E-spora yönelik, rekabetçi 5v5 taktik FPS. Unity ile geliştiriliyor.

- **PC sürümü:** yüksek grafik kalitesi, 64/128 tick sunucular.
- **Mobil sürüm:** düşük poligon ama "küp küp" olmayan görünüm, dokunmatik kontroller.
- İki sürüm **aynı oyun kodunu** paylaşıyor ama **ayrı oyuncu havuzlarında** oynanıyor (çapraz platform yok).
- Turnuvalar [rally.gg](https://rallygg.com) üzerinden düzenlenecek.

> "VEXA" çalışma adıdır. Mağaza ve marka tescilinden önce isim araştırması yapılmalı.

## Depo yapısı

```
Game/                       Unity projesi (Unity 6, URP)
  Assets/Vexa/Core/         Oyun çekirdeği: saf C#, Unity'ye bağımlı değil (hareket, silahlar, hasar,
                            çarpışma, netcode, sunucu, istemci tahmini)
  Assets/Vexa/Net/          UDP taşıma (LiteNetLib)
  Assets/Vexa/Client/       Unity katmanı: giriş, kamera, görseller, efektler, geçici menü/HUD
  Assets/Vexa/Editor/       Harita dışa aktarma aracı (VEXA menüsü)
  Assets/StreamingAssets/Maps/  .vxmap çarpışma haritaları (sunucu + istemci ortak)
Server/Vexa.Server/         Bağımsız (headless) Linux/Windows sunucusu, Unity gerektirmez
Tests/Vexa.Tests/           Otomatik testler (hareket, silahlar, hasar, netcode, hile korumaları)
Tools/                      Unity olmadan derleme kontrolleri
docs/                       Mimari ve yol haritası
archive/web-prototype/      Eski tarayıcı prototipi (referans için saklandı)
```

## Hızlı başlangıç

### 1) Testler (Unity gerekmez)
```bash
dotnet test Tests/Vexa.Tests
```

### 2) Sunucuyu çalıştır
```bash
dotnet run --project Server/Vexa.Server -c Release -- --port 27015 --tick 64 --map training --bots 4
```

### 3) Unity'de aç
1. Unity Hub ile **Unity 6 LTS** (6000.0.x) kur. Mobil için Android/iOS modüllerini de ekle.
2. Unity Hub → **Add project from disk** → `Game` klasörünü seç. İlk açılışta paketler indirilir; farklı bir 6000.0 sürümün varsa yükseltmeyi kabul et.
3. **Edit → Project Settings → Player → Active Input Handling = Both**.
4. Proje URP grafik ayarı istemezse: *Assets → Create → Rendering → URP Asset (with Universal Renderer)*. Sonra *Project Settings → Graphics* ve *Quality* bölümlerinde bu asset'i seç.
5. Herhangi bir sahnede (boş sahne de olur) **Play**'e bas. VEXA menüsü kendiliğinden açılır.
6. **"Antrenman / Sunucu kur"** seçeneği oyunu botlarla başlatır. **"Sunucuya bağlan"** ise 2. adımdaki sunucuya bağlanır.

**Kontroller (PC):**

| Tuş | İşlev |
|---|---|
| WASD | Hareket |
| Fare | Bakış / ateş |
| Shift | Sessiz yürü |
| Ctrl veya C | Eğil |
| Boşluk | Zıpla |
| R | Şarjör değiştir |
| 1 / 2 / 3 | Silah seç |
| Q | Son silah |
| B | Satın al (prototipte ücretsiz) |
| Tab | Skor tablosu |
| Esc | Menü |

**Mobil:** sol yarıda joystick, sağ yarıda sürükleyerek bakış, ayrıca ekran butonları.

> Not: Bu repo, Unity'nin kurulu olmadığı bir ortamda geliştiriliyor. Çekirdek kod otomatik testlerle doğrulanıyor. Unity scriptleri de Unity referans kütüphanelerine karşı derleniyor. Unity'de ilk açılışta bir derleme hatası çıkarsa Console çıktısını paylaşman yeterli.

## Durum

Mevcut kilometre taşı: **çekirdek + netcode temeli**. Ayrıntılar için [docs/ROADMAP.md](docs/ROADMAP.md) ve [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md).
