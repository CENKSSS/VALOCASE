# Onboarding Çözüm Raporu — Doğrulanmış

Tarih: 2026-08-17
Durum: **UYGULANDI (2026-08-17, 1.0.28).** Aşağıdaki çözümlerden kod tarafında olanların tamamı + opsiyoneller işlendi; 197/197 editör testi geçti. Uygulama notları:

- Çözüm 1 → `FanMadeNoticePopup` artık her dokunuşta kapanıyor (V1); metin kısaltıldı ve TR/EN cihaz diline bağlandı; duvar aktifken popup hiç spawn olmuyor (`UpdateAvailablePopup.IsWallActive`).
- Çözüm 2 → canlı doğrulama (TooShort yazım sırasında susturuluyor), CONFIRM gri yerine soluk kırmızı + okunur etiket, placeholder "Boş bırakabilirsin — sana isim verilir", NICKNAME bandında X/15 sayacı.
- Çözüm 3 → boot artık health'e bloklanmıyor; sürüm kontrolü `GameContext.VersionGate` olarak paralel, 4 sn'lik özel timeout ile 3 deneme (duvarın "panel gelmiyor" sorununun kökü buydu: tek health çağrısı sessizce atlanıyordu). Kayıt POST'una tek otomatik transient retry eklendi (uzun timeout bilinçli olarak eklenmedi).
- Çözüm 4 → duvar-arkası fan notice kirliliği kapatıldı; süreç kuralı geçerli: `application.properties`'teki `valocase.client.latest-version` yayınlanan sürüme (fiilen **1.0.29** — kod-28 paketi Play'e yüklenip geri çekildiği için ad ve kod 29'a alındı) ANCAK store rollout %100 olduktan sonra çekilecek.
- Çözüm 5 → telemetri, transient hatalarla tükenen olayı düşürmek yerine diske park edip sonraki açılışta yeniden deniyor. Crashlytics paketi ve kampanya conversion işaretlemesi konsol/SDK tarafında — kullanıcı aksiyonu.

Orijinal analiz aşağıda değiştirilmeden duruyor.

---

## KESİN TEŞHİS — 17.08.2026, sunucu logu doğrulaması

"~20 kurulum, oyuna giren 0" sorusu sunucu tarafından, Firebase'den bağımsız olarak cevaplandı. Kaynaklar: Kudu docker platform logları (8-9 Ağustos) + `spring.log.2026-08-10` (tam gün) + `onboarding_events`/`accounts` tabloları.

**1. Sunucu sağlam; erişim de sağlam.** 10 Ağustos boyunca sayaç raporu her 5 dakikada saniyesi şaşmadan attı (JVM hiç durmadı), sıfır DB/bellek hatası (tüm ERROR'lar Always On'un `/` ping gürültüsü), ve cihazlardan 23 telemetri olayı kabul edildi. "Sunucuya ulaşamıyorlar" ve "sunucu cevap vermiyor" hipotezlerinin ikisi de yanlışlandı.

**2. "Uyanma" yalnız restart sonrası gerçek.** Docker logları: her container açılışında warmup 76-167 sn sürüyor. 8-9 Ağustos'taki restart'lar sahibinin taşınma denemeleriydi; 9 Ağustos 12:19 UTC deploy'undan sonra hiç restart yok. Kalıcı bir "uyuyan sunucu" sorunu yok.

**3. Funnel'ın öldüğü nokta: fan notice.** Deploy'dan itibaren kümülatif sunucu sayaçları: `app_launched 5→11`, `fan_notice_shown 1→7` gün boyu büyüdü; `fan_notice_accepted` ve sonrası **bütün gün 1'de dondu** (o 1'ler sahibinin kendi cihazının zinciri). Altı gerçek cihaz notice'ı gördüğünü sunucuya raporladı, hiçbiri OK'a basmadı. Alt sebepler: 1.0.26 kohortunda notice kapatılamaz güncelleme duvarının ALTINDA açılıyordu (tıklanamazdı); duvarsız 1.0.27'de ise ~22 sn ölü ekran sonrası gelen tek butonlu yabancı dilde metin terk ediliyor.

**4. Düzeltilen hipotez:** 22-23 sn'lik ölü açılış "sunucu cevapsızdı" diye açıklanmıştı — 10 Ağustos logu sunucunun o saatlerde sağlıklı olduğunu gösterdi; health timeout'unun muhtemel sebebi cihaz tarafı yavaş şebeke. 1.0.29'daki paralel version-gate düzeltmesi iki durumda da geçerli.

**Zincir:** reklam kurulumlarının çoğu hiç açılmıyor → açanlar ~22 sn boş ekran → duvar-altı/itici fan notice → OK yok → kayıt ekranı yok → oyuna giren 0. Kayıt sistemi, sunucu ve DB çalışıyor; oyuncu onlara ulaşamıyor. 1.0.29 değişiklikleri tam bu zinciri hedefliyor; yayın sonrası aynı sayaçlardan doğrulanacak.
Yöntem: Her madde ya koddan (dosya:satır), ya veritabanından (`onboarding_events`), ya da Azure/Play panellerinden doğrulandı. Tahmin içeren hiçbir madde "çözüm" olarak yazılmadı; doğrulanamayanlar açıkça "doğrulanamadı" diye işaretli.

Sorunun dört katmanı (17.08 tarihli araştırmadan):

| Katman | Sorun | Kanıt |
|---|---|---|
| 0 | Kurulumların çoğu uygulamayı hiç açmıyor | Play: ~6,3 edinme/gün (zirve ~35) ↔ Firebase: 28 günde 20 first_open |
| 1 | Açanlar 22-23 sn ölü ekran görüyor | 5 cihazda birebir aynı launch→notice süresi; olay tesliminde 3 dk 47 sn'ye varan gecikme |
| 2 | 9-10 Ağustos 1.0.26 kurulumları güncelleme duvarına kilitlendi | 4 cihaz, `app_version=1.0.26`, hepsi notice'ta ölü; duvar kapatılamaz |
| 3 | Fan notice + nickname ekranı sıcak sunucuda bile oyuncu düşürüyor | `76e2702e` iki oturumda notice'ta, `9626e73b` iki oturumda nickname panelinde hiç tıklamadan çıktı |

---

## ÇÖZÜM 1 — Fan notice: panel dışına (veya herhangi bir yere) basınca kapansın

### Mevcut davranış (doğrulandı)

- Popup'ın kökü tam ekran karartma: `dim.raycastTarget = true` — her dokunuşu yutuyor ama hiçbir tıklama dinleyicisi yok ([FanMadeNoticePopup.cs:100-102](../Scripts/UI/FanMadeNoticePopup.cs)).
- Tek çıkış OK butonu (220×56 px): `OnOkClicked` → `fan_notice_accepted` telemetrisi + `PlayerPrefs` kalıcı bayrak + popup'ı yok edip `FirstLaunchProfilePopup.TryShow()` zinciri ([FanMadeNoticePopup.cs:84-91](../Scripts/UI/FanMadeNoticePopup.cs)).
- Veri: notice'ı gören gerçek kurulumların çoğunluğu OK'a hiç basmadı; `76e2702e` iki gün arayla iki oturumda da basmadan çıktı.

### Çözüm

Karartma (dim) objesine tıklama dinleyicisi ekle ve **aynen `OnOkClicked`'e bağla.** Ayrı bir kapatma yolu YAZMA — kabul bayrağı, telemetri ve profil ekranına zincirleme tek yerden geçmeye devam etsin. Böylece "dışarı tıklayan" oyuncu ile "OK'a basan" oyuncu arasında davranış farkı oluşmaz.

İki varyant (UGUI tıklama olayları hiyerarşide yukarı doğru ilk dinleyiciye gider — bu doğrulanmış motor davranışıdır):

- **V1 — her yere bas kapanır (önerilen):** Dinleyici köke (dim) eklenir. Kartın kendisine tıklamak da köke düşer → kartın gövdesine basmak da kapatır. Senin istediğin davranış tam olarak bu ("boş bir yere bassak bile kapansın").
- **V2 — sadece panel dışı kapatır:** Kartın `Image`'ı tıklamayı tüketen boş bir dinleyici alır; yalnız kart DIŞI dokunuşlar kapatır. Daha muhafazakâr; tercih senin.

### Riskler / notlar

- Bu bir hukuki bilgilendirme ekranı: "gördü ve herhangi bir yere basıp geçti" = kabul sayılacak. `fan_notice_accepted` yine yazıldığı için kayıt açısından fark yok — ürün kararı olarak not düşüldü.
- Güncelleme duvarı açıkken hiçbir dokunuş geçmez; duvar her karede kendini en üste alıyor ([UpdateAvailablePopup.cs:79-83](../Scripts/UI/UpdateAvailablePopup.cs)) — bu çözüm o durumu değiştirmez (o ayrı, bkz. Çözüm 4).
- Çift tetiklenme yok: OK butonu tıklamayı tüketir, kök dinleyiciye düşmez.

### Ek öneri (opsiyonel, doğrulanmış gözleme dayalı)

Notice metni "This is a fan-made game… not affiliated" — reklamdan Valorant sanarak gelen oyuncuya ilk saniyede "bu gerçek değil" diyor. Metni yumuşatmak/kısaltmak (tek satır + küçük yazı) bilinçli vazgeçmeyi azaltabilir. Bu bir ürün kararı; hangi oyuncunun "bilinçli" bıraktığı veriden ayrıştırılamıyor (doğrulanamadı, o yüzden ana çözüm değil).

---

## ÇÖZÜM 2 — Nickname CONFIRM'in griye dönmesi: tespit edilen durumlar ve asıl bug

### Griye düşüren TÜM durumlar (kod dökümü — tam liste)

Buton rengi `RefreshConfirmState` ile belirlenir ([FirstLaunchProfilePopup.cs:295-304](../Scripts/UI/FirstLaunchProfilePopup.cs)): `ready == false` ise gri. `ready`, `ProfileSetupGate.IsReady` ([ProfileSetupGate.cs:24-29](../Scripts/Core/ProfileSetupGate.cs)) → `NicknameValidator.Classify` ([NicknameValidator.cs:115-138](../Scripts/Core/NicknameValidator.cs)) sonucuna bağlı:

| # | Durum | Örnek | Not |
|---|---|---|---|
| G1 | Kayıt sürerken (`_saving`) | — | Normal, sorun değil |
| G2 | **TooShort: 1-2 karakter** | "Ka" | **Her isim yazımının ilk iki tuşunda buton gri.** Yazarken duraklayan oyuncu "buton kapalı" sanıyor |
| G3 | Whitespace: içinde boşluk | "Ahmet Y" | Ad-soyad yazan herkes buna düşer |
| G4 | InvalidCharacter: harf/rakam/`_` dışı her şey | "Kaan-34", "Mr.X", emoji | Tire, nokta, apostrof dahil. Türkçe harfler SERBEST (client [NicknameValidator.cs:197-216] ve backend [AccountService.java:433-438] birebir aynı — doğrulandı) |
| G5 | TooLong: >15 görünür karakter | — | Giriş alanı 60 UTF-16 birime kadar yazmaya İZİN VERİYOR ([FirstLaunchProfilePopup.cs:494](../Scripts/UI/FirstLaunchProfilePopup.cs)) → oyuncu 16+ karakter yazabiliyor ve buton griye düşüyor |

Boş alan gri YAPMAZ: `IsReady("") == true` (Blank kabul), boş panelde buton kırmızı/aktif — doğrulandı.

### Asıl bug: gri butonun sebebi oyuncuya HİÇ söylenmiyor

- Hata mesajı yalnızca butona **tıklanınca** gösteriliyor ([FirstLaunchProfilePopup.cs:170-176](../Scripts/UI/FirstLaunchProfilePopup.cs)). Yazarken hiçbir uyarı çıkmıyor; `OnNicknameChanged` sadece eski hatayı **temizliyor**, yenisini koymuyor ([309-314](../Scripts/UI/FirstLaunchProfilePopup.cs)).
- Buton gri ama tıklanabilir bırakılmış ("tıklarsa sebebini söyleriz" tasarımı, [295-304]) — fakat oyuncular gri butona tıklamıyor. **Veri kanıtı:** `9626e73b` iki ayrı oturumda panelde durdu ve tek bir `nickname_confirm_clicked` / `nickname_rejected` üretmeden çıktı — butona hiç dokunmadı.
- Sonuç: geçersiz bir şey yazan oyuncu, sebepsiz "kapalı" görünen bir butonla baş başa; çıkmazda ve çıkıyor.

### Çözümler

1. **Canlı doğrulama (ana çözüm):** `OnNicknameChanged` içinde `Classify` sonucu `None`/`Blank` dışındaysa `NicknameMessages.For(reason)` metnini hata satırında ANINDA göster. Mesajlar zaten var ve TR/EN lokalize ([NicknameMessages.cs:28-64](../Scripts/Core/NicknameMessages.cs)) — sıfır yeni metin, tek çağrı yönlendirmesi.
   - G2 istisnası: alan odaktayken TooShort'u **gösterme** (daha 2 harf yazan birine "en az 3" demek gürültü); TooShort mesajı odak çıkışında veya buton tıklamasında kalsın. Diğer dört durum (boşluk, geçersiz karakter, çok uzun) anında gösterilsin.
2. **Gri algısını kır:** buton tıklanabilir olduğu sürece "devre dışı" gibi görünmesin — gri yerine soluk/koyu accent ton. (Renkler [FirstLaunchProfilePopup.cs:35-38]'de sabit; tek renk değişimi.)
3. **"Boş bırakabilirsin" mesajını alana taşı:** placeholder "Enter nickname…" ([477](../Scripts/UI/FirstLaunchProfilePopup.cs)) → "Boş bırak, sana isim verelim / Leave empty for a random name". Alt başlık zaten "hepsi isteğe bağlı" diyor ([709-711]) ama alan boşken değil, yazmaya başlayınca sorun başlıyor.
4. **G5 için:** karakter sınırını görünür kıl (ör. "12/15" sayacı) — alan 60 birime kadar yazdırdığı için oyuncu sınırı ancak griden anlıyor.
5. Temizlik (bug değil ama tespit): `NicknameMessages`'taki "boş bırakılamaz" metni bu ekranda artık ulaşılamaz durumda ([OnConfirmClicked:170] Blank'i hata saymıyor) — Settings'teki yeniden adlandırma ekranı kullanıyorsa kalsın, kullanmıyorsa ölü metin.

---

## ÇÖZÜM 3 — 22-23 saniyelik ölü açılış

### Mevcut davranış (doğrulandı)

- Boot zinciri SIRALI: `GetHealth` bitmeden (başarı VEYA 15 sn timeout) fan notice çağrılmıyor ([GameContext.cs:245-265](../Scripts/Core/GameContext.cs)). Timeout tek ve global: her istek 15 sn ([BackendApiClient.cs:426](../Scripts/Services/Backend/BackendApiClient.cs), `GameConfig.requestTimeoutSeconds=15`).
- Veri: sıcak sunucuda notice 3-6 sn'de geliyor; sunucunun cevapsız pencerelerinde 5 ayrı cihazda 22-23 sn (motor ~7 sn + health 15 sn timeout). Sunucu tarafında olay işleme gecikmesi 3 dk 47 sn'ye kadar çıktı (9 Ağu 22:45).
- Always On AÇIK (portalda doğrulandı) → sebep uyku değil; **restart pencereleri veya B1 kaynak tıkanıklığı** (aşağıda teşhis adımı).

### Client çözümleri

1. **Fan notice'ı health'e bekletme.** Token'sız ilk kurulum yolunda notice, health çağrısının SONUCUNA bağlı değil — sadece sırada arkasında. Health paralel koşarken notice hemen gösterilebilir. Güncelleme duvarı sonradan gelirse sorun yok: duvar her karede kendini en üste alıyor ([UpdateAvailablePopup.cs:79-83]) — geç kalmış duvar yine her şeyi kapatır. Bu, ölü ekranı 22-23 sn'den ~5-7 sn'ye indirir (motor açılışı kalır).
2. **Health'e özel kısa timeout (3-5 sn).** `BackendApiClient.Send` şu an tek `_timeoutSeconds` kullanıyor — çağrı başına timeout parametresi eklenmeli. Health'in görevi sürüm duyurusu; 15 sn beklemeye değmez, kaçan duvar sonraki `GetWallet` cevabından da geliyor ([GameContext.cs:292]).
3. **Kayıt POST'una otomatik yeniden deneme.** CONFIRM → timeout olursa oyuncuya "tekrar deneyin" demek yerine SAVING durumunda 2-3 otomatik deneme (mevcut `SetSaving` akışı [FirstLaunchProfilePopup.cs:282-289] spinner görevi görüyor). İlk deneme sunucuyu uyandırıyor; ikincisi genelde geçer.
4. (Opsiyonel) Kayıt isteğine özel daha uzun timeout (ör. 30 sn) — oyuncu zaten SAVING ekranında bekliyor.

### Backend teşhis adımı (çözümden önce şart — henüz doğrulanAMAdı)

Cevapsız pencerelerin sebebi restart mı, kaynak mı — iki bakış yeterli:
- Kudu: `https://valocase-backend-f6btemapa7exb9bd.scm.polandcentral-01.azurewebsites.net` → LogFiles → `*_docker.log` içinde `Started ValocaseApplication` satırlarının saatleri. 9 Ağu 22:45-22:50 ve 10 Ağu 15:51 / 21:48 (TSİ) civarında açılış satırı varsa = restart döngüsü (muhtemel OOM).
- Portal → İzleme → Metrikler: Memory working set + CPU + "Health check / restarts". B1 = 1,75 GB RAM; Java 21 + Spring + App Insights ajanı bu sınıra yakın çalışır.
- Çıkana göre: OOM ise JVM heap sınırı (`-Xmx`) ayarı veya B1→B2/S1 yükseltme. Bu rapor hangisi olduğunu söylemiyor çünkü loglar henüz okunmadı.

### App Insights'ı geri getir (izleme çözümü)

`APPLICATIONINSIGHTS_CONNECTION_STRING` duruyor ama ~8-9 Ağustos'tan beri hiçbir istek kaydedilmiyor (bugünkü isteklerimiz dahil — doğrulandı). Deploy'un ajanı düşürdüğü anlaşılıyor. Portal → Application Insights blade'inden yeniden etkinleştir + restart; düzelmezse GitHub Actions workflow'una ajan paketini ekletmek gerekir. Bu yapılmadan gelecekteki hiçbir sunucu sorunu görünmez.

---

## ÇÖZÜM 4 — Sürüm duvarı süreci (9-10 Ağustos kilidinin tekrarını önleme)

Doğrulanmış mekanizma: duvar kapatılamaz, tam ekran raycast, her karede en üstte ([UpdateAvailablePopup.cs:15-26, 79-83, 171-178]). 9 Ağustos'ta backend `latest-version=1.0.27` derken Play hâlâ 1.0.26 dağıtıyordu → 4 gerçek kurulum kilitlendi (DB kanıtlı). Şu an aktif sorun değil (store 1.0.27'de) ama süreç kuralı yazılmazsa her sürümde tekrarlar:

1. **Kural:** `valocase.client.latest-version` yalnızca Play'de yeni sürüm **%100 rollout + görünür** olduktan sonra yükseltilir (App Service ortam değişkeni; kod değişikliği gerekmez).
2. **Duvar arkasında funnel kirliliği:** duvar açıkken fan notice yine spawn olup `fan_notice_shown` yazıyor ([GameContext.cs:264] duvardan bağımsız çağırıyor; duvar sadece görsel olarak üstte). "Notice'ı gördü" verisi bu yüzden şişiyor. Çözüm: duvar aktifken (`UpdateAvailablePopup` görünürken) `FanMadeNoticePopup.TryShow` ertelensin — telemetri de gerçeği anlatmaya başlar.
3. (Opsiyonel ürün kararı) Duvarı yalnızca N-2 ve daha eski sürümlere göster (bir sürüm tolerans) — rollout gecikmesi penceresini kendiliğinden kapatır.

---

## ÇÖZÜM 5 — Katman 0 (kurulumların çoğu hiç açılmıyor) + ölçüm onarımı

1. **Kampanya dönüşüm hedefi:** Play ~6,3 edinme/gün gösterirken Firebase 28 günde 20 first_open gördü — satın alınan kurulumların çoğu hiç açılmıyor. `AdsConversions` funnel'ı Firebase'e zaten basıyor ([AdsConversions.cs](../Scripts/Services/Backend/AdsConversions.cs), 1.0.27'de canlı — doğrulandı). Yapılacak: Firebase'de `registration_succeeded`'ı (veya `fan_notice_accepted`'ı) conversion işaretle → Google Ads kampanyasını "kurulum hacmi"nden bu olaya optimize et. Kod değişikliği yok; konsol ayarı.
2. **Crashlytics ekle:** Şu an projede yalnız Firebase Analytics var (Assets/Firebase/Plugins altında sadece Analytics dll'leri — doğrulandı). Açılışta çöken/donan cihazlar hiçbir yerde görünmüyor; Play vitals da "veri yok" diyor (eşik altı). Firebase Crashlytics paketi eklenmeden Katman 1'in cihaz tarafı (Firebase 20 first_open → 9 app_launched farkı) hiçbir zaman aydınlanmaz.
3. **Client telemetri dayanıklılığı:** `OnboardingTelemetry` bir olayı 4 başarısız denemeden sonra KALICI düşürüyor ([OnboardingTelemetry.cs:292-298] — kuyruktan çıkarma "delivered, permanently refused, or out of attempts" hepsinde aynı). Sunucunun cevapsız penceresi tam da ölçmek istediğimiz an olduğu için, olaylar tam o anda kayboluyor. Çözüm: deneme hakkı biten TRANSIENT hatalı olay düşürülmesin, diske geri konup SONRAKİ açılışta tekrar denensin (disk kalıcılığı zaten var, [RestoreFromDisk:193-216]). `eventId` idempotent olduğu için çift sayım riski yok ([OnboardingTelemetry.cs:33-34] — doğrulandı).

---

## Soğuk sunucu — portal yapılacakları (17.08 eki)

Client tarafı 1.0.28'de dayanıklı hale getirildi (paralel gate, kısa timeout, kayıt retry) — soğuk pencere artık oyuncuyu kaybettirmez ama sunucudaki kök sebep hâlâ teşhis bekliyor. Chrome uzantısı kapalı olduğu için Kudu'ya erişemedim; sıralı adımlar:

1. **Restart mı, RAM mi?** App Service → Gelişmiş Araçlar (Kudu) → Git → Debug Console → Bash:
   ```
   grep -h "Started ValocaseApplication in" /home/LogFiles/*docker*.log | tail -20
   ```
   Satır sayısı 1'den fazlaysa uygulama yeniden başlıyor demektir; satırlardaki saatler 9 Ağu 22:45-22:50 / 10 Ağu 15:51 / 21:48 (TSİ) pencereleriyle örtüşüyorsa teşhis kesinleşir. Ek: `grep -hiE "OutOfMemory|Killed" /home/LogFiles/*docker*.log | tail -5`.
2. **Metrikler:** İzleme → Metrikler → "Memory working set" + "CPU time". B1 = 1,75 GB; Java 21 + App Insights ajanı sınıra dayanır. Bellek tepeleri restart saatleriyle çakışıyorsa → Ortam değişkenlerine `JAVA_OPTS = -Xmx900m` ekle (heap'i sınırla, container OOM-kill'i kes) veya planı B2/S1'e çıkar.
3. **App Insights'ı geri getir:** Ayarlar → Application Insights → mevcut kaynağı yeniden Uygula + uygulamayı yeniden başlat. Doğrulama: tarayıcıdan `/api/v1/health`'i çağır, 2-3 dk sonra Logs'ta `requests | where timestamp > ago(10m)` satır göstermeli. Göstermiyorsa ajan hâlâ bağlanmıyor demektir — o zaman GitHub Actions workflow'una agent eklenecek (kod tarafı, ayrı iş).
4. **Sağlık denetimi:** App Service → İzleme → Sistem durumu denetimi → yol: `/api/v1/health`. Cevapsız instance'ı platform kendisi yeniden başlatır; 3 dk 47 sn'lik pencereler dakikaya iner.
5. **1.0.28 rollout kuralı:** `valocase-backend/src/main/resources/application.properties:99` → `valocase.client.latest-version=1.0.28` değişikliği ANCAK Play'de 1.0.28 %100 yayıldıktan sonra push'lanacak. Erken çekilirse 9-10 Ağustos kilidi tekrarlanır.

## Öncelik sırası ve beklenen etki

| Sıra | İş | Katman | Beklenen etki |
|---|---|---|---|
| 1 | Fan notice'ı health'e bekletme + health'e kısa timeout (Çözüm 3.1-3.2) | 1 | İlk UI 22-23 sn → ~5-7 sn |
| 2 | Nickname canlı doğrulama + gri algısı + placeholder (Çözüm 2.1-2.3) | 3 | Panele ulaşanın kaybını azaltır (şu an ulaşanların ~yarısı dönmüyor) |
| 3 | Fan notice dışa-tıkla kapat (Çözüm 1) | 3 | Tek dokunuşluk sürtünmeyi sıfırlar |
| 4 | Kayıt POST otomatik retry (Çözüm 3.3) | 1 | Yavaş pencerede CONFIRM ilk denemede geçer |
| 5 | App Insights + Crashlytics + telemetri dayanıklılığı (Çözüm 3/5) | ölçüm | Bundan sonraki her teşhis mümkün olur |
| 6 | Kampanya conversion hedefi (Çözüm 5.1) | 0 | Kurulum kalitesi; en büyük ham kayıp burada |
| 7 | Sürüm duvarı süreci kuralı (Çözüm 4) | 2 | Gelecek sürümlerde tekrarı önler |

## Test listesi ("hata almak istemiyorum")

Her değişiklik sonrası, editör Play Mode QA düzeneğiyle (gerçek dokunuş + stub proxy):

1. Temiz kurulum: notice → dışarı tık → profil paneli açılıyor mu; `fan_notice_accepted` yazılıyor mu.
2. Notice + güncelleme duvarı birlikte: duvar üstte mi, dışarı tık duvarı DELEMİYOR mu.
3. Nickname: "Ka" (kısa), "Ahmet Y" (boşluk), "Kaan-34" (tire), 16+ karakter, boş — her birinde buton rengi + anlık mesaj doğru mu; boşta kırmızı mı.
4. Kayıt: stub ile timeout senaryosu → otomatik retry çalışıyor mu, çift hesap açılmıyor mu (ikinci deneme aynı akış, `RegisterGuestBackend` token varsa erken dönüyor [GameContext.cs:390] — çift kayıt koruması mevcut, doğrulandı).
5. Health timeout senaryosu: notice health'ten önce görünüyor mu; duvar geç gelince yine kapatıyor mu.
6. Regresyon: mevcut hesaplı cihazda (token dolu) hiçbir popup görünmemeli; `profileSetupCompleted` akışı bozulmamalı.
