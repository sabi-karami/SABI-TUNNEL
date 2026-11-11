<div dir="rtl">

#### TrustTunnel اندروید · نسخه‌ی «شب فیروزه»

کلاینت اندروید بر پایه‌ی آخرین نسخه‌ی رسمی سازنده — [TrustTunnel Flutter Client v1.2.0](https://github.com/TrustTunnel/TrustTunnelFlutterClient/releases/tag/v1.2.0) (AdGuard) — با طراحی کاملاً تازه:

- **آسمان شب زنده**: شفق فیروزه‌ای و بنفش که آرام حرکت می‌کند، ستاره‌های چشمک‌زن، شهاب، و نقش گره (خاتم) طلایی کم‌رنگ
- **دروازه‌ی اتصال**: دکمه‌ی اتصال یک طاق جناغی ایرانی داخل ستاره‌ی هشت‌پر است؛ هنگام اتصال طلایی می‌تپد، وقتی وصل شد حلقه‌ی شفق می‌چرخد و نور انتهای تونل روشن می‌شود
- **کارت تونل**: مسیر زنده از دستگاه تو تا سرور با بسته‌های نوری در حال حرکت، پروتکل، آدرس و مدت اتصال با ارقام فارسی
- **فارسی و راست‌به‌چپ** با فونت وزیرمتن؛ همه‌ی متن‌های برنامه ترجمه شده‌اند
- کارت‌های شیشه‌ای، نوار پایین تیره، فرم‌های گرد، آیکون و صفحه‌ی شروع تازه (شامل آیکون تک‌رنگ اندروید ۱۳)
- باز کردن مستقیم لینک‌های `tt://` صفحه‌ی اشتراک پنل

موتور VPN، پروتکل‌ها (HTTP/2 و QUIC)، مسیریابی، مسیرهای مستثنا، گزارش اتصال‌ها و خروجی گزارش‌ها دقیقاً همان نسخه‌ی رسمی ۱٫۲ است.

#### ساخت

ساخت خودکار روی GitHub Actions است: [`.github/workflows/android-app.yml`](../.github/workflows/android-app.yml). برای ساخت دستی:

```bash
cd android-app
export GPR_KEY=<PAT with read:packages>   # کتابخانه‌ی TrustTunnel روی GitHub Packages است
make init
flutter build apk --release --split-per-abi
```

- شناسه‌ی بسته: `com.sabi.trusttunnel` (کنار نسخه‌ی رسمی هم نصب می‌شود)
- حداقل اندروید ۸ (API 26)
- امضا: از سکرت‌های `ANDROID_KEYSTORE_B64`، `ANDROID_KEYSTORE_PASSWORD` و `ANDROID_KEY_ALIAS`

#### مجوز

Apache-2.0، همانند پروژه‌ی اصلی. فهرست تغییرات در [`NOTICE`](NOTICE) و README اصلی در [`README.upstream.md`](README.upstream.md) است. فونت وزیرمتن با مجوز OFL.

</div>
