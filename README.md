# Hình nền & Quote – bản 0.2 (M1 + M2 + M3)

App Windows tự đổi hình nền máy tính kèm một câu quote ngẫu nhiên, theo lịch bạn đặt.

## Tải và chạy (không cần cài gì)

1. Vào **Releases** của repo này → bản mới nhất → tải **WallpaperQuote.exe**.
2. Bấm đúp để chạy. Nếu Windows hiện *"Windows protected your PC"* → **More info → Run anyway**.
3. Cửa sổ **Cài đặt** tự mở; app tạo lối tắt **"Hình nền & Quote"** ngoài desktop để mở lại Cài đặt bất cứ lúc nào.
4. Icon xanh dưới khay hệ thống: nháy đúp = làm mới; chuột phải = menu.

## Bản 0.4 có gì mới

- **Ảnh NASA mới**: chuyển sang *NASA Image and Video Library* (thiên hà, tinh vân, Trái Đất nhìn từ không gian…). Không cần API key nữa. Nguồn ảnh thiên văn APOD cũ không còn trả ảnh.
- **Hiệu ứng rơi** (Cài đặt → tab *Hiệu ứng*): tuyết, mưa, hoa rơi chạy liên tục trên hình nền, phía sau các icon desktop. Chọn được mật độ Nhẹ / Vừa / Dày, và có thể tự tạm dừng khi máy chạy pin.
- **Thả một đợt**: chuột phải icon dưới khay → *Hiệu ứng* → *Thả một đợt ngay*, hoặc dùng các nút trong tab *Hiệu ứng*. Hạt rơi dày trong 10 giây.
- Kéo thả vị trí quote (từ 0.3): chuột phải icon dưới khay → *Chỉnh vị trí quote…*

## Bản 0.2 có gì mới

- **Cửa sổ Cài đặt 4 tab**: Ảnh / Quote / Lịch xoay / Chung.
- **Ảnh qua đường link**: dán 1 hoặc nhiều link ảnh trực tiếp (http/https), có trạng thái Sẵn sàng / Lỗi / Chờ tải, Thử lại, Xóa.
- **Ảnh online public**: NASA (ảnh thiên văn) và Wikimedia Commons (ảnh trong ngày), chỉ nhận ảnh Public Domain / CC0, đồng bộ 1 lần/ngày, tối đa 10 ảnh mỗi nguồn.
- **Quote cá nhân**: thêm / sửa / xóa, có tác giả, nguồn trích dẫn, ngôn ngữ; chống trùng; lọc theo nhóm và tìm kiếm.
- **Nhập nhiều quote** (bản 0.2.1): tab Quote → *Nhập nhiều quote* → dán nhiều dòng hoặc chọn file .txt/.csv.
  Mỗi dòng: `Nội dung | Tác giả | Nguồn | vi hoặc en` (chỉ nội dung là bắt buộc). Dán thẳng từ Excel/Google Sheets cũng được.
- **Quote online**: tải từ `catalog/quotes.json` trong repo này (sửa file đó để thêm quote cho mọi máy).
- Nhận thêm ảnh **.HEIC** (cần cài *HEIF Image Extensions* từ Microsoft Store), **.jfif, .gif, .tif**; hạ ngưỡng ảnh tối thiểu còn **800px**.
- **Desktop**: tự hiện lại biểu tượng desktop nếu Windows đang ẩn; tạo lối tắt mở app; mở app lần 2 sẽ bật cửa sổ Cài đặt.

> App không bao giờ xóa, di chuyển hay ẩn file của bạn. Nếu biểu tượng desktop bị ẩn: Cài đặt → tab Chung → **Hiện biểu tượng trên desktop**,
> hoặc chuột phải desktop → View → tick *Show desktop icons*.

---

## Phụ lục: tự build từ code

### Build ra file .exe trên máy Windows

Cần một máy Windows 10/11 (64-bit).

1. Cài **.NET 8 SDK** (khoảng 5 phút). Mở PowerShell và chạy:
   ```
   winget install Microsoft.DotNet.SDK.8
   ```
   Hoặc tải bản cài tại https://dotnet.microsoft.com/download/dotnet/8.0 (mục *SDK 8.0*, *Windows x64*).
2. Giải nén file zip này ra một thư mục, ví dụ `Documents\WallpaperQuote`.
3. Bấm đúp **`build.cmd`**. Lần đầu mất vài phút để tải thư viện.
4. Xong, thư mục `dist` tự mở ra, trong đó có **`WallpaperQuote.exe`** (một file duy nhất, không cần cài thêm gì).

> Nếu build báo lỗi: chụp màn hình hoặc copy phần chữ báo lỗi gửi lại cho Claude để sửa.

**Cách khác – để GitHub build hộ:** đẩy cả thư mục này lên một repo GitHub. File `.github/workflows/build.yml`
sẽ tự build trên máy Windows của GitHub; vào tab *Actions* → lần chạy mới nhất → tải `WallpaperQuote-win-x64`.

## 2. Chạy app

1. Bấm đúp `WallpaperQuote.exe`.
   - Windows có thể hiện *"Windows protected your PC"* vì app chưa ký số → bấm **More info → Run anyway**.
2. Icon xanh xuất hiện dưới khay hệ thống (góc phải thanh taskbar; nếu không thấy, bấm mũi tên **˄**).
3. Lần đầu chưa có ảnh: quote hiện trên nền tối. **Bấm chuột phải vào icon → Thư mục ảnh → Thêm thư mục…** để chọn thư mục ảnh của bạn.

| Thao tác | Cách làm |
|---|---|
| Làm mới tất cả | Nháy đúp icon, hoặc chuột phải → Làm mới → Làm mới tất cả |
| Chỉ đổi ảnh / chỉ đổi quote | Chuột phải → Làm mới → … |
| Tạm dừng / Tiếp tục | Chuột phải → Tạm dừng |
| Chu kỳ | Chuột phải → Đổi ảnh mỗi / Đổi quote mỗi (5 phút → 24 giờ) |
| Ngôn ngữ quote | Chuột phải → Ngôn ngữ quote (Việt / Anh / Cả hai) |
| Ảnh khác tỉ lệ màn hình | Chuột phải → Lấp đầy / Vừa khung / Kéo giãn |
| Khởi động cùng Windows | Chuột phải → tick *Khởi động cùng Windows* |
| Xem ảnh/quote đang hiển thị | Chuột phải → Thông tin ảnh hiện tại |
| Thoát | Chuột phải → Thoát (giữ nguyên hình nền đang hiển thị) |

> Bật *Khởi động cùng Windows* sau khi đã đặt `WallpaperQuote.exe` ở chỗ cố định. Nếu di chuyển file exe, hãy tắt rồi bật lại mục này.

## 3. M1 có gì / chưa có gì

**Có trong M1**
- Thư mục ảnh: thêm/gỡ nhiều thư mục, quét cả thư mục con, tự cập nhật khi thêm/xóa ảnh, bỏ qua ảnh rộng dưới 1280px.
- 38 quote mặc định (20 tiếng Việt, 18 tiếng Anh) có tác giả và nguồn trích dẫn; lọc theo ngôn ngữ.
- Ghép quote lên ảnh: góc dưới phải, chữ trắng trên nền tối mờ, dòng "— Tác giả, *Nguồn*"; quote dài tự thu nhỏ chữ, quá 3 dòng thì cắt "…".
- Hai chu kỳ riêng cho ảnh và quote; random không lặp tới hết vòng; làm mới thủ công 3 kiểu, chu kỳ tính lại từ đầu.
- Máy ngủ dậy đã lỡ lượt → đổi 1 lần ngay. Đổi độ phân giải/màn hình → ghép lại cho đúng kích thước.
- Chỉ cho 1 phiên bản chạy; lưu hình nền gốc ở lần chạy đầu; khởi động cùng Windows.

**Để các mốc sau**
- M2: cửa sổ Cài đặt 4 tab, ảnh qua link, quote cá nhân (thêm/sửa/xóa, tác giả, nguồn).
- M3: ảnh online NASA + Wikimedia (lọc giấy phép), quote online, chế độ offline, cache.
- M4: bộ cài Setup.exe, gỡ cài khôi phục hình nền gốc.
- V1: widget làm mới nổi trên desktop, phím tắt, nhiều kiểu hiển thị quote, mỗi màn hình một ảnh.

## 4. Checklist chạy thử M1

- [ ] Mở app lần đầu: có thông báo "đang chạy", hình nền đổi sang nền tối có quote.
- [ ] Thêm 1 thư mục ảnh: hình nền đổi sang ảnh trong thư mục, quote ở góc dưới phải, không bị taskbar che.
- [ ] Nháy đúp icon: ảnh và quote đổi ngay, không trùng cái vừa hiện.
- [ ] Chỉ đổi quote: ảnh giữ nguyên. Chỉ đổi ảnh: quote giữ nguyên.
- [ ] Đặt "Đổi quote mỗi 5 phút": sau 5 phút quote tự đổi.
- [ ] Tạm dừng: hết chu kỳ không đổi; vẫn nháy đúp làm mới được.
- [ ] Quote tiếng Việt hiển thị đúng dấu; quote có nguồn hiện "— Tác giả, *Nguồn*" (nguồn in nghiêng).
- [ ] Xóa vài ảnh trong thư mục: app không lỗi, không hiện ảnh đã xóa.
- [ ] Rút ổ ngoài / đổi tên thư mục: menu ghi "(không khả dụng)", app vẫn chạy.
- [ ] Màn hình scale 125–150%: ảnh nét, quote không quá nhỏ.
- [ ] Cho máy ngủ quá 1 chu kỳ rồi mở lại: hình nền đổi 1 lần.
- [ ] Mở `WallpaperQuote.exe` lần 2: chỉ hiện thông báo "đang chạy".
- [ ] Bật Khởi động cùng Windows, khởi động lại máy: app tự chạy.

## 5. Dữ liệu của app nằm ở đâu

| Nội dung | Vị trí |
|---|---|
| Cấu hình | `%AppData%\WallpaperQuote\settings.json` |
| Hình nền đã ghép, nhật ký lỗi | `%LocalAppData%\WallpaperQuote\` (chuột phải icon → *Mở thư mục nhật ký và hình nền*) |

App không gửi dữ liệu nào ra ngoài. File ảnh gốc của bạn không bao giờ bị sửa hay xóa.

## 6. Cấu trúc code

```
src/WallpaperQuote/
  Program.cs                 Điểm vào, chặn chạy 2 lần, bắt lỗi toàn cục
  AppHost.cs                 Điều phối: lịch xoay, random, làm mới, quét ảnh, sự kiện ngủ/đổi màn hình
  TrayController.cs          Icon và menu dưới khay
  Core/                      Logic không phụ thuộc giao diện
    Models.cs                Quote, cấu hình
    Infrastructure.cs        Đường dẫn, log, đọc/ghi cấu hình
    ShuffleBag.cs            Random không lặp
    Libraries.cs             Thư viện quote, quét ảnh
  Platform/                  Phần gọi Windows
    WallpaperRenderer.cs     Ghép quote lên ảnh, xuất JPEG
    WindowsServices.cs       Đặt hình nền, khởi động cùng Windows, luồng phụ
  Assets/default-quotes.json Bộ quote mặc định
```

Mã capability trong code (W1.1, W3.3…) khớp với file *WallpaperQuote_Business_Domain_Architecture_v1.1.xlsx*.
