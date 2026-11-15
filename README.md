# GRP Localization Editor

[English](README.en.md)

Ghost Recon Online ve Ghost Recon Phantoms için Windows yerelleştirme editörü.
Oasis metinlerini yan yana düzenler, INF ve INF.ENC arasında dönüşüm yapar ve
desteklenen BAO seslerini çıkarıp yeniden paketler.

## Kaynaktan çalıştırma

Windows ve .NET 10 SDK ile proje klasöründe `dotnet build -c Release` ve
`dotnet run` çalıştır. İngilizce oyun referansı ve Oasis anahtarı depodaki `Assets`
klasöründen uygulamaya gömülür. WAV ve Bink işlemleri için FFmpeg'i aşağıdaki
konuma ekle.

- `Assets/Oasis_English.inf` gömülü İngilizce referans için okunabilir UTF-8 Oasis dosyasıdır.
- `Assets/Oasis.key` Oasis anahtarının 32 ASCII baytını içerir. Dosyaya BOM veya satır sonu ekleme.
- `Tools/ffmpeg/ffmpeg.exe` WAV çıkarma, önizleme, Bink ses çıkarma ve Vorbis kodlama için kullanılır. libvorbis destekli Windows dağıtımı kullan.

## Çeviri editörü

Editör boş açılır. **Orijinal İngilizceyi yükle** ile gömülü GRP metnini
açabilir veya **Referans INF aç** ile başka bir dil dosyası seçebilirsin.
Sol sütun salt okunur kalır ve sağ sütun aynı metnin düzenlenebilir kopyasıdır.
**No** sütunu yalnızca görsel satır numarasıdır.

1. Hedef hücreye çift tıkla veya seçili satırda **F2** ile düzenlemeye başla.
   Uzun metinleri sağ alttaki kutuda da düzenleyebilirsin. **Enter** aynı
   düzenleme alanında sonraki satıra geçer ve metnin tamamını seçer.
2. İlk **Kaydet** işleminde hedef dosya adını seç. Sonraki kayıtlar bu
   dosyayı günceller. **Farklı kaydet** ile başka bir hedefe geçebilirsin.

İki dil tek tabloda birlikte kayar ve satırlar sayısal kodla eşleştirilir.
Tüm metinler tek kaydırılabilir listede yer alır. Arama ve Enter dosyanın
tamamında çalışır.
Kaydetme sırasında hedef dosyanın kodları, sırası, satır sonları ve UTF-8 BOM
durumu korunur. Yalnızca bir dosyada olan kodlar da tabloda görünür. Hedefte
karşılığı olmayan kodun düzenleme alanı kapalıdır. Oyun içi satır sonu için
`\n` yaz ve `%s`, `{0}` gibi yer tutucuları koru.

| Düğme veya tuş | İşlevi |
| --- | --- |
| Referans INF aç | Seçili INF dosyasını iki sütuna yükler. |
| Orijinal İngilizceyi yükle | Gömülü GRP metnini iki sütuna yükler. |
| Kaydet · Ctrl+S | Hedefi mevcut konumuna yazar. Yeni hedefte konum sorar. |
| Farklı kaydet · Ctrl+Shift+S | Hedefi seçtiğin dosyaya yazar. |
| Bul · Ctrl+F | Kod veya metin arar. Arama kutusunda Enter sonucu seçer. |
| Önceki | Önceki düzenlenebilir satıra geçer. |
| F2 veya hedef hücreye çift tık | Satır içinde düzenlemeyi açar. |
| Sonraki · Enter | Sonraki hedef satıra geçer ve metnin tamamını seçer. |
| Esc | Satır içi düzenleme kutusunu kapatır. Yapılan değişiklikler korunur. |
| Kısayollar ve yardım · F1 | Düğmeleri ve klavye kısayollarını açıklar. |

## Dosya dönüştürme

| Düğme | İşlevi |
| --- | --- |
| Şifreyi çöz | `.inf.enc` girdisinden okunabilir UTF-8 `.inf` dosyası üretir. |
| Şifrele | Düzenlenen `.inf` dosyasından Oasis `.inf.enc` çıktısı üretir. |

Şifreleme çıktısı `.inf.enc`, şifre çözme çıktısı `.inf` ile biter.
Program, dosya adına yanlışlıkla eklenen yinelenmiş uzantıları kaldırır.
Girdi ve çıktı için ayrı yollar seç. Dönüştürme Twofish CFB1 ve gömülü Oasis anahtarını kullanır.
Harici `KEY.txt` gerekmez.

## Ses paketleri

1. **Ses klasörü aç** ile oyun kurulum klasörünü seç. Sadece BAO sesleri için
   `Sound_Bin_PC` klasörünü de seçebilirsin.
2. Bir paket seç. Sağdaki liste kayıt kimliğini, ses kimliğini, biçimi, kanal
   sayısını, örnekleme hızını, süreyi ve paketin konumunu gösterir.
3. Kayda çift tıklayarak veya **Dinle** ile önizle, **WAV çıkar** ile düzenlenebilir kopya oluştur veya
   **Ogg çıkar** ile orijinal Vorbis akışını kaydet. **Tümünü WAV Olarak
   Çıkar** ve **Tümünü OGG Olarak Çıkar**, seçili bankanın desteklenen
   kayıtlarını ayrı dosyalara yazar. Atlanan ve hatalı kayıtlar raporlanır.
4. **Sesi değiştir** ile bir WAV veya Ogg Vorbis dosyası seç. **Ses klasörünü
   içe aktar** ile birden fazla kaydı birlikte hazırla. Değişiklikleri
   paketlemeden önce dinleyebilirsin. WAV, orijinal kanal sayısına ve
   örnekleme hızına dönüştürülür. Ogg girdisi bu değerlere uymalıdır.
5. **Mevcut ses bankasının üzerine yaz**, her özgün dosyayı bulunduğu klasöre
   `.bak1`, `.bak2` gibi numaralı uzantılarla yedekler ve bekleyen değişiklikleri
   ilgili paketlere yazar. Bir kayıt işlemindeki tüm yedekler aynı numarayı alır.
   **Ses bankasını farklı kaydet**, banka ve SPK adlarını seçmene izin verir.
   Paket kopyalarını seçtiğin klasör içinde yeni bir alt klasöre kaydeder.

| Dosya türü | İçerik |
| --- | --- |
| `.pk`, `.hpk` | BAO ses bankası. Ses içeride olabilir veya bir dış pakete bağlanabilir. |
| `.spk` | BAO ses verisi. İlgili bankaları ayrıntı alanında bulabilirsin. |
| `.lpk`, `.lhpk` | Dil klasöründeki bankalar veya dış ses bağlantıları. |
| `.bik` | Bink video. Ses kanalları WAV olarak çıkarılabilir. |

Örnek olarak `ABI_english_sadia.pk` bir dış `.spk` paketine bağlanır.
`Fst_Recon.pk` içinde doğrudan ses kayıtları bulunur.
`UI_InGame_PC.pk` oyun içi arayüz sesleri için bir başlangıç noktasıdır.
`Tutorial.bik` dosyasının ikinci ses kanalında İngilizce eğitim anlatımı bulunur.

Yeniden paketleme BAO 28.1.2 biçimindeki tek parçalı, döngüsüz Vorbis kayıtlarını destekler.
**Ses klasörünü içe aktar**, seçili bankadaki kayıtları dosya adının sonundaki sekiz haneli
onaltılık kimliğe göre eşler. Dışa aktarılan adları koru ve her kimlik için bir WAV veya Ogg kullan.
Kaynak paketleri güncellemeden önce oyunu kapat. Ses çıktılarını oyun klasörü içindeki bir alt klasöre de kaydedebilirsin.
Yeni paketleri oyun içinde ayrıca dene.

WAV, dinleme ve Bink çıkarma için kullanılan FFmpeg, hazır paketin
`Tools/ffmpeg` klasöründedir ve otomatik kullanılır. Kaynak koddan ses
özelliklerini çalıştırmak için FFmpeg dağıtımının `ffmpeg.exe` dosyasını,
lisansını ve dağıtım notlarını proje içindeki aynı klasöre yerleştir.

## Kaynak kod ve derleme

Windows'ta .NET 10 SDK ile proje klasöründen çalıştır:

```powershell
dotnet build -c Release
dotnet run
dotnet publish -c Release -r win-x64 --self-contained true -o publish
```

`MainForm.cs` arayüzü, `OasisDocument.cs` metin biçimini,
`OasisCipher.cs` şifrelemeyi ve `SafeFile.cs` dosyaya yazmayı yönetir.
Twofish blok şifresi BouncyCastle.Cryptography paketinden gelir. İngilizce
referans ve Oasis anahtarı `Assets` klasöründen derlemeye gömülür.

## Arayüz dilleri

Sağ üstteki menüden dil seçebilirsin. **Sistem dili**, Windows görüntüleme
dilini kullanır. Örneğin `de-AT` için Almanca bulunursa Almanca, desteklenen
bir eşleşme yoksa İngilizce açılır. Elle seçilen dil sonraki açılışta hatırlanır.
Arayüz dili Oasis oyun dosyalarının dilini değiştirmez.

Yeni çeviri eklemek için hazır paketteki `Languages/en.json` dosyasını aynı
klasörde yeni bir JSON dosyasına kopyala:

1. `Code` alanına dil kodunu, `Name` alanına dilin menüde görünen adını yaz.
   Örneğin `nl` ve `Nederlands`.
2. `Strings` içindeki değerleri çevir. Anahtarları, `{0}` / `{1:N0}` gibi
   biçim alanlarını ve dosya filtrelerindeki `|` ayraçlarını koru.
3. Dosyayı UTF-8 olarak kaydet ve editörü tekrar aç. Eksik metinlerde İngilizce
   gösterilir. Yeniden derleme gerekmez.

## Yapımcı

Harun Ercan · [GitHub](https://github.com/HarunErcan01)


## Kullanılan teknolojiler ve kaynaklar

- C# ve Windows Forms, masaüstü arayüzünü oluşturur. [.NET Windows Forms](https://learn.microsoft.com/en-us/dotnet/desktop/winforms/)
- Bouncy Castle, Oasis dönüşümünde kullanılan Twofish blok şifresini sağlar. [Bouncy Castle for C#](https://github.com/bcgit/bc-csharp) · [NuGet paketi](https://www.nuget.org/packages/BouncyCastle.Cryptography)
- FFmpeg ve libvorbis, WAV ve Ogg dönüşümlerini yapar ve Bink sesini çözer. [FFmpeg](https://ffmpeg.org/) · [Xiph.Org Vorbis](https://xiph.org/vorbis/) · [Gyan Windows derlemeleri](https://www.gyan.dev/ffmpeg/builds/)
- vgmstream kaynak kodu Ubisoft BAO yapısını incelemek ve yeniden paketlenen sesleri bağımsız olarak doğrulamak için kullanıldı. [vgmstream](https://github.com/vgmstream/vgmstream) · [Ubisoft BAO okuyucusu](https://github.com/vgmstream/vgmstream/blob/master/src/meta/ubi_bao.c)
- zeroKilo’nun GROBackendWV wiki’sindeki dil dosyası notları Oasis incelemesine başlangıç sağladı. [Useful binary edits](https://github.com/zeroKilo/GROBackendWV/wiki/Useful-binary-edits)
- Arayüz çevirilerinin ilk taslağında Google Translate kullanıldı. Çeviriler JSON dosyaları olarak düzenlenebilir. [Google Translate](https://translate.google.com/)
