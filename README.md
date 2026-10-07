# WebStrike — Tarayıcıda Taktik FPS

Counter-Strike 2'nin oynanış mekaniklerini tarayıcıya taşıyan, düşük donanımlı bilgisayarlarda da akıcı çalışacak şekilde tasarlanmış bir taktik nişancı oyunu.
Kurulum gerektirmez: Three.js ile yazılmıştır, derleme adımı yoktur, statik dosya olarak her yerde (GitHub Pages dahil) çalışır.

> Tüm görseller, modeller, sesler ve harita bu proje için **prosedürel olarak üretilmiştir**. Valve'a ait hiçbir dosya, logo, ses veya harita kullanılmamıştır. Mekanikler (ekonomi, süreler, hasar değerleri vb.) CS2 ile aynıdır.

## Çalıştırma

```bash
# depo kök dizininde herhangi bir statik sunucu yeterli:
python3 -m http.server 8000
# sonra tarayıcıda: http://localhost:8000
```

(ES modülleri kullanıldığı için `index.html` dosyasını doğrudan çift tıklayarak açmak yerine bir sunucu üzerinden açın.)

## Oyun modları

| Mod | Açıklama |
|---|---|
| **Rekabetçi** | 5v5 botlarla, MR12 (ilk 13 kazanır), devre arası taraf değişimi, uzatmalar (MR3, $12.500) |
| **Sıradan** | İlk 8 kazanır, bedava zırh + kask, CT'lere bedava kit, dost ateşi kapalı |
| **Ölüm Maçı** | Herkes herkese, 10 dakika, anında yeniden doğma, bedava silah, doğma koruması |
| **Antrenman** | Bot yok, sınırsız para, her yerde satın alma, bomba yolu çizgisi |

Bot zorluğu: Kolay / Normal / Zor / Uzman (tepki süresi, nişan hızı, sprey kontrolü, kafa oranı, karşı-strafe, bomba kullanımı değişir).

## Eklenen CS2 mekanikleri

**Hareket (Source motoru fiziği)**
- Sürtünme (5.2), ivmelenme (5.5), hava ivmesi (12, maks. 30 hız) → karşı-strafe, hava strafe'i
- Silaha göre hız (bıçak 250, AK 215, M4 225, AWP 200 / dürbünle 100 …)
- Shift ile sessiz yürüme (%52), eğilme (%34), çömelerek zıplama (crouch-jump)
- Zıplama yüksekliği (≈57 birim), 18 birimlik basamak çıkma, düşme hasarı, iniş yavaşlaması
- Vurulunca yavaşlama (tagging)
- Ayak sesleri (koşarken), sessiz yürüme

**Silahlar (34 silah/ekipman)**
- Tüm tabancalar, SMG'ler, pompalılar, makineli tüfekler, tüfekler ve keskin nişancılar; CS2 fiyatları, hasarı, zırh delme oranı, atış hızı, şarjör/yedek mermi, öldürme ödülü
- Her silah için sabit **sprey deseni** (görüş ×0.9, mermi ×2 sekme — CS2'deki gibi)
- İsabetsizlik modeli: duruş / eğilme / hareket (%34 hız eşiği) / havada / iniş / seri atış birikimi ve toparlanma
- Kafa ×4, mide ×1.25, göğüs-kol ×1, bacak ×0.75; kask sadece kafayı, yelek gövdeyi korur
- Menzil düşüşü (`rangeMod^(mesafe/500)`)
- **Duvar delme**: malzeme yoğunluğuna göre (ahşap kasa kolay, taş duvar zor), hasar azalır
- Dürbün (AWP/SSG 2 kademe, AUG/SG 553 nişangâh), atıştan sonra dürbün kapanıp geri açılır
- Susturucu tak/çıkar (USP-S, M4A1-S), Glock/FAMAS seri atış modu, R8 tetik bekletme + yelpaze atış
- Pompalılarda tek tek fişek doldurma (ateşle kesilebilir)
- Bıçak: hafif (40/25) ve ağır (65) saldırı, sırttan 90/180 hasar
- Zeus x27
- Silah çekme süreleri, şarjör değiştirme, boşken otomatik doldurma, silah inceleme (F)
- Silah yere atma (G), yürüyerek alma, E ile değiştirme

**Bombalar**
- **HE**: 98 hasar, mesafeyle azalır, zırh yarıya indirir, sisi geçici olarak açar (CS2)
- **Flaş**: bakış açısına ve mesafeye göre körlük, tam beyaz ekran + çınlama; sis arkasından etki etmez
- **Sis**: CS2 tarzı hacimsel sis — duvarlara çarpıp koridorlara yayılır, mermiler delik açar, HE ile açılır, 18 sn; görüşü ve botların algısını keser
- **Molotof / Yangın bombası**: yere çarpınca alev alanı (7 sn), sis söndürür, sise atılırsa söner
- **Dekoy**: sahte silah sesleri çıkarır, düşman radarında görünür
- Fırlatma: sol tık güçlü, sağ tık alttan, ikisi birlikte orta; maks. 4 bomba (2 flaş)

**Bomba (C4)**
- Kurma 3.2 sn (sadece A/B bölgesinde, hareketsiz), patlama 40 sn, hızlanan bip sesi
- İmha 10 sn, kitle 5 sn; bomba düşürme/alma, taşıyıcı ölünce düşer
- Patlama hasarı mesafeyle azalır (≈1750 birim yarıçap)

**Ekonomi**
- Başlangıç $800, maks. $16.000
- Galibiyet: eleme / süre $3250, bomba patlatma / imha $3500
- Kayıp bonusu $1400 → $1900 → $2400 → $2900 → $3400; CS2 MR12 kuralı: galibiyette sayaç 1 azalır
- Bomba kurulup kaybedilirse T'lere +$800, kurana +$300, imha edene +$300
- Süre bittiğinde hayatta kalan T'ler para almaz
- Öldürme ödülleri: tüfek/tabanca $300, SMG $600, P90 $300, pompalı $900, AWP $100, bıçak $1500
- Takım arkadaşı öldürme −$300; dost ateşi (rekabetçide %33)
- Satın alma bölgesi + satın alma süresi (20 sn), bu tur alınanları sağ tıkla iade

**Arayüz (CS2 düzenine benzer)**
- Dönen radar (takım arkadaşları, görülen düşmanlar, bomba), bölge isimleri
- Üstte skor, tur süresi, oyuncu kutuları; sağ üstte öldürme akışı (kafa, duvar, dürbünsüz, sis, kör etiketleri)
- Sol altta can/zırh, sağ altta mermi, silah listesi
- Özelleştirilebilir nişangah (statik/dinamik, renk, uzunluk, kalınlık, boşluk, nokta, kontur, T stili)
- Satın alma menüsü (sütun + numara kısayolları), skor tablosu (Ö/A/Ö, MVP, KAFA%, ADR, skor, tur geçmişi)
- Hasar yönü göstergeleri, ölüm paneli (verilen/alınan hasar), takım arkadaşını izleme
- MVP, tur sonu bildirimi, devre arası ve uzatma duyuruları, bot telsiz mesajları

**Botlar**
- Görüş alanı + duvar/sis kontrolü, ses duyma (ayak sesi, silah, bomba), flaşla körleşme
- Tepki süresi, yumuşak nişan, sprey kontrolü, uzak mesafede tek atış / ortada burst / yakında sprey
- Karşı-strafe, ADAD, eğilerek atış, takım arkadaşını vurmama
- T: harita kontrolü → bölgeye giriş (sis/flaş/molotof atar) → bomba kurma → bombayı koruma, düşen bombayı alma
- CT: bölge tutma rolleri, rotasyon, bomba sonrası geri alma, imha
- Ekonomi kararları (eco / force / full buy), tabanca turu alımları

## Kontroller

| Tuş | İşlev |
|---|---|
| WASD | Hareket |
| Fare | Nişan / Sol: ateş / Sağ: alternatif |
| Shift | Sessiz yürü |
| Ctrl veya C | Eğil |
| Boşluk | Zıpla |
| R | Şarjör değiştir |
| 1-5 | Ana / Tabanca / Bıçak-Zeus / Bomba / C4 |
| Q | Son silah |
| G | Yere at |
| E | İmha et / silah al |
| F | İncele |
| B | Satın alma menüsü |
| Tab | Skor tablosu |
| Esc | Menü |

## Performans

Ayarlar → Grafik kalitesi: **Düşük** (gölgeler kapalı, düşük çözünürlük), **Orta** (varsayılan), **Yüksek**.

## Proje yapısı

```
index.html, css/style.css   arayüz
js/main.js        render döngüsü, sahne, ışıklar
js/map.js         de_kasaba haritası (ızgara yükseklik alanı), çarpışma, ışın izleme, A* navigasyon
js/character.js   hareket fiziği, silah durum makinesi, sekme/isabet, hitbox
js/weapons.js     silah verileri ve sprey desenleri
js/combat.js      mermi, duvar delme, hasar/zırh, öldürmeler
js/grenades.js    HE, flaş, sis, molotof, dekoy
js/bots.js        bot yapay zekası ve satın alma
js/game.js        tur akışı, ekonomi, bomba, modlar
js/player.js      kontroller, kamera, birinci şahıs silah modeli
js/hud.js, ui.js, menu.js   HUD, satın alma menüsü, ana menü ve ayarlar
js/audio.js       sentezlenmiş 3D sesler
js/effects.js     parçacıklar, mermi izleri, delikler
```
