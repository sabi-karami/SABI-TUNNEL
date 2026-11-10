# TrustTunnel Stack

<div dir="rtl">

سرور شخصی VPN بر پایه‌ی پروتکل متن‌باز [TrustTunnel](https://github.com/TrustTunnel/TrustTunnel) (ساخت AdGuard) به همراه **پنل مدیریت وب فارسی**، با یک دستور روی AWS EC2 و Cloudflare DNS.

## چه چیزی می‌سازد؟

```
 گوشی/لپ‌تاپ (اپ TrustTunnel)
        │  HTTPS واقعی روی 443  (HTTP/2 روی TCP  یا  HTTP/3 روی QUIC/UDP)
        ▼
 tt-oh.example.com  ──►  EC2 (t4g.micro, Ubuntu 24.04, IPv4 + IPv6)
                          ├─ trusttunnel_endpoint v1.1.0   [::]:443 tcp+udp
                          │     └─ SNI = ttpanel-oh.example.com ─► reverse proxy ─► 127.0.0.1:8080
                          ├─ ttpanel (FastAPI)  127.0.0.1:8080   ← پنل وب
                          ├─ metrics           127.0.0.1:1987   ← آمار مصرف هر کاربر
                          └─ certbot (Let's Encrypt، تمدید خودکار)
```

- ترافیک از بیرون دقیقاً شبیه یک سایت HTTPS معمولی با گواهی معتبر است؛ پنل هم روی همان پورت 443 و با SNI جدا سرو می‌شود (پورت اضافه‌ای باز نیست).
- دو پشته‌ی کامل: سرور هم با IPv4 و هم با IPv6 در دسترس است و ترافیک خروجی کاربران هم روی هر دو کار می‌کند.
- تنظیمات کرنل: BBR + fq، بافرهای بزرگ UDP برای QUIC، swap یک گیگ.

## امکانات پنل

- ورود امن (PBKDF2، کوکی HttpOnly/Secure/SameSite، محدودیت تلاش ناموفق)
- افزودن / ویرایش / غیرفعال‌سازی / حذف کاربر، تولید مجدد رمز
- **سهمیه حجم** (گیگابایت) و **تاریخ انقضا**؛ کاربر خودکار غیرفعال می‌شود
- لینک `tt://` و **QR کد** هر کاربر، با انتخاب HTTP/2 یا HTTP/3 و آدرس IPv4/IPv6/هر دو/دامنه
- نمودار زنده‌ی ترافیک، نشست‌های فعال، آخرین IP، مصرف تجمعی هر کاربر
- برآورد مصرف ماهانه نسبت به ۱۰۰ گیگ ترافیک رایگان AWS، وضعیت CPU/RAM/دیسک، تاریخ انقضای گواهی

## نصب با یک دستور

پیش‌نیاز روی سیستم خودتان: `python3`، `ssh`، دسترسی AWS (`AWS_PROFILE` یا کلیدها) و یک توکن Cloudflare با دسترسی `Zone → DNS → Edit`.

```bash
git clone https://github.com/sabi-karami/trusttunnel-stack && cd trusttunnel-stack
export CF_API_TOKEN=...      # توکن Cloudflare
./deploy.sh --region us-east-2 --code oh --zone sahebkarami.ir --server-name "Sabi VPN" --first-user sami
```

خروجی: آدرس پنل `https://ttpanel-oh.<zone>` و لینک کاربر اول. رمز ادمین در سرور در فایل `/root/ttpanel-admin.txt` است.

اجرای دوباره‌ی همین دستور امن است (idempotent): منابع با تگ `Project=trusttunnel-stack` پیدا و دوباره استفاده می‌شوند و فقط کانفیگ/نرم‌افزار به‌روز می‌شود.

### نصب روی یک سرور موجود (بدون AWS)

```bash
sudo TT_HOST=tt.example.com PANEL_HOST=panel.example.com FIRST_USER=me ./server/install.sh
```

رکوردهای DNS باید از قبل به IP سرور اشاره کنند و پورت‌های 80/tcp و 443/tcp+udp باز باشند.

## افزودن منطقه‌ی دوم (Multi-region)

هر منطقه یک سرور مستقل با پنل خودش است:

```bash
./deploy.sh --region eu-central-1 --code fra --zone sahebkarami.ir --server-name "Sabi FRA"
```

پیشنهاد برای کاربران ایران: `eu-central-1` (فرانکفورت) یا `eu-south-1` (میلان) معمولاً پینگ کمتری از `us-east-2` دارند.

## هزینه (تقریبی، هر سرور)

| مورد | ماهانه |
|---|---|
| t4g.micro (on-demand) | ~ ۶٫۱ دلار |
| Elastic IPv4 عمومی | ~ ۳٫۶ دلار |
| دیسک gp3 ‏۱۲ گیگ | ~ ۱ دلار |
| ترافیک خروجی | ۱۰۰ گیگ اول رایگان، سپس ~ ۰٫۰۹ دلار/گیگ |
| **جمع** | **~ ۱۰٫۴ دلار** |

CPU credits روی حالت `standard` است تا هزینه‌ی پنهان «unlimited» نداشته باشید. پنل مصرف ماه را نشان می‌دهد.

## نکات مهم

- رکوردهای DNS باید **DNS only (ابر خاکستری)** باشند؛ پراکسی Cloudflare تونل CONNECT را عبور نمی‌دهد.
- نسخه‌ی endpoint روی `1.1.0` پین شده و لینک‌ها با فرمت deep-link نسخه‌ی ۱ ساخته می‌شوند تا با اپ پایدار موبایل سازگار باشند. برای ارتقا: `TT_VERSION=...` در نصب.
- reverse proxy نسخه‌ی 1.1 بدنه‌ی درخواست POST را عبور نمی‌دهد؛ برای همین UI پنل JSON را در هدر `X-Body` (base64) می‌فرستد.
- چون endpoint رمزها را hot-reload نمی‌کند، با تغییر کاربران سرویس چند ثانیه ری‌استارت می‌شود (اتصال‌ها خودکار وصل می‌شوند).
- SSH (پورت 22) باز است و فقط با کلید کار می‌کند؛ اگر لازم نیست در Security Group ببندید.
- بازیابی رمز ادمین: `cd /opt/ttpanel && sudo ./venv/bin/python app.py reset-admin`
- لینک یک کاربر از ترمینال: `sudo ./venv/bin/python app.py link <user>`

## ساختار مخزن

```
deploy.sh               نصب یک‌دستوری (AWS + Cloudflare + SSH)
infra/aws_deploy.py     VPC دو پشته، SG، کلید، EC2، Elastic IP (boto3)
infra/cloudflare_dns.py رکوردهای A/AAAA بدون پراکسی
server/install.sh       نصب‌کننده‌ی سرور (endpoint، گواهی، پنل، systemd)
panel/app.py            بک‌اند پنل (FastAPI) + CLI
panel/index.html        رابط کاربری فارسی
panel/deeplink.py       سازنده‌ی لینک tt:// نسخه ۱
```

## حذف کامل

از کنسول AWS منابع با تگ `Project=trusttunnel-stack` (Instance، Elastic IP، VPC، Key pair) را حذف کنید و رکوردهای DNS با کامنت `trusttunnel-stack` را از Cloudflare پاک کنید. **Elastic IP رها‌شده هزینه دارد.**

</div>
