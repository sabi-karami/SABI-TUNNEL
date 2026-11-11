import json, time, threading, http.server, socketserver, os, random, math, sys, html as H
import qrcode, qrcode.image.svg
ST='panel'; OUT='shots'; os.makedirs(OUT,exist_ok=True)
now=int(time.time()); random.seed(4)
hist=[]
for i in range(180):
    t=now-(179-i)*20; base=1.2e6+9e5*abs(math.sin(i/14))+random.random()*4e5
    hist.append([t,int(base*0.18),int(base),random.choice([2,3,3,4,5])])
status={"server_name":"Sabi VPN","tt_host":"tt-oh.sahebkarami.ir","ipv4":"77.112.234.99","ipv6":"2600:1f16:1b1a:3200:8efd:156f:70e5:b388","region":"us-east-2",
 "version":"1.1.0","service_active":True,"metrics_ok":True,"sessions":4,"month":"2026-11","month_in":4.1e9,"month_out":27.6e9,"free_tier_bytes":1e11,
 "cert_expires":now+71*86400,"history":hist,"system":{"mem_used":412e6,"mem_total":951e6,"load":0.18,"uptime":19*86400,"disk_used":4.1e9,"disk_total":12.4e9},
 "dns_upstreams":["1.1.1.1","8.8.8.8"],"users_total":6,"users_active":5}
users=[{"name":"sami","note":"گوشی سامی","enabled":True,"quota_gb":0,"used":18.4e9,"expires":0,"last_seen":now-30,"last_ip":"5.120.44.18","sessions":2,"disabled_reason":None},
 {"name":"sami-pc","note":"لپ‌تاپ","enabled":True,"quota_gb":50,"used":11.2e9,"expires":now+23*86400,"last_seen":now-5,"last_ip":"5.120.44.18","sessions":1,"disabled_reason":None},
 {"name":"maryam","note":"مریم - آیفون","enabled":True,"quota_gb":30,"used":26.9e9,"expires":now+4*86400,"last_seen":now-3600*3,"last_ip":"151.235.10.7","sessions":0,"disabled_reason":None},
 {"name":"reza","note":"","enabled":True,"quota_gb":20,"used":3.3e9,"expires":now+57*86400,"last_seen":now-12,"last_ip":"2.180.3.91","sessions":1,"disabled_reason":None},
 {"name":"baba","note":"تبلت بابا","enabled":True,"quota_gb":10,"used":0.4e9,"expires":now+29*86400,"last_seen":now-86400*2,"last_ip":"","sessions":0,"disabled_reason":None},
 {"name":"guest","note":"مهمان","enabled":False,"quota_gb":5,"used":5.0e9,"expires":now+2*86400,"last_seen":now-86400*6,"last_ip":"","sessions":0,"disabled_reason":"quota"}]
LINK="tt://?DEMO-ONLY-NOT-A-REAL-LINK-AAEBARR0dC1vaC5leGFtcGxlLmNvbQIRMTAuMC4wLjE6NDQz"
qr=qrcode.make(LINK,image_factory=qrcode.image.svg.SvgPathImage,box_size=8,border=2).to_string(encoding='unicode')
def share():
    page=open(ST+'/share.html',encoding='utf-8').read()
    win='<svg viewBox="0 0 24 24"><path d="M3 5.6 10.4 4.6v7H3zM11.6 4.4 21 3v8.6h-9.4zM3 12.8h7.4v7L3 18.8zM11.6 12.8H21V21l-9.4-1.3z"/></svg>'
    dl=f'<a class="dl" data-os="win" href="#"><span class="ic">{win}</span><div><b>ویندوز — نصب‌کننده</b><span>Windows 10/11 · نصب با یک کلیک · 6.5 MB</span></div><span class="go">‹</span></a>'
    v={"SERVER":"Sabi VPN","USER":"sami-pc","LINK":H.escape(LINK),"QR":qr,"STATE":"فعال","STATECLS":"ok","USAGE":"11.2 GB از 50 GB","PCT":"22",
       "EXPIRY":"23 روز مانده","DOWNLOADS":dl,"H2ON":"on","H3ON":"","BASE":"/s","QUOTA":"50 گیگ","USED":"11.2 GB","EXPTS":str(now+23*86400),
       "CREATED":str(now-7*86400),"REGION":"us-east-2"}
    for k,x in v.items(): page=page.replace('{{'+k+'}}',x)
    return page
class Hd(http.server.BaseHTTPRequestHandler):
    def log_message(self,*a): pass
    def send(self,b,ct):
        self.send_response(200); self.send_header('Content-Type',ct); self.end_headers(); self.wfile.write(b)
    def do_GET(self):
        p=self.path.split('?')[0]
        if p=='/': self.send(open(ST+'/index.html','rb').read(),'text/html; charset=utf-8')
        elif p=='/s': self.send(share().encode(),'text/html; charset=utf-8')
        elif p.startswith('/assets/'): self.send(open(ST+p,'rb').read(),'font/woff2')
        elif p.startswith('/x/'): self.send(open(p[3:],'rb').read(),'image/svg+xml' if p.endswith('.svg') else 'text/html; charset=utf-8')
        elif p=='/api/status': self.send(json.dumps(status).encode(),'application/json')
        elif p=='/api/users': self.send(json.dumps(users).encode(),'application/json')
        elif p.endswith('/link'): self.send(json.dumps({"link":LINK,"qr_svg":qr,"share_url":"https://ttpanel-oh.example.com/s/sami-pc/abc"}).encode(),'application/json')
        else: self.send_response(404); self.end_headers()
srv=socketserver.ThreadingTCPServer(('127.0.0.1',8099),Hd); threading.Thread(target=srv.serve_forever,daemon=True).start()
from playwright.sync_api import sync_playwright
errs=[]
JOBS=json.loads(os.environ.get('JOBS','null') or 'null') or [['panel','/',1440,900,True],['panel-mobile','/',400,860,False],['share','/s',430,932,True]]
with sync_playwright() as pw:
    b=pw.chromium.launch()
    for name,url,w,h,full in JOBS:
        pg=b.new_page(viewport={'width':w,'height':h},device_scale_factor=2 if w<500 else 1)
        pg.on('console',lambda m,n=name: errs.append((n,m.type,m.text)) if m.type in('error','warning') else None)
        pg.on('pageerror',lambda e,n=name: errs.append((n,'pageerror',str(e))))
        pg.goto('http://127.0.0.1:8099'+url); pg.wait_for_timeout(2600)
        pg.screenshot(path=f'{OUT}/{name}.png',full_page=full)
        if name=='panel':
            box=pg.locator('#chart').bounding_box(); pg.mouse.move(box['x']+box['width']*.6,box['y']+80); pg.wait_for_timeout(300)
            pg.screenshot(path=f'{OUT}/panel-tip.png',clip={'x':box['x']-20,'y':box['y']-60,'width':box['width']+40,'height':300})
            pg.locator('tr.u button.pri').first.click(); pg.wait_for_timeout(900); pg.screenshot(path=f'{OUT}/panel-link.png')
            pg.evaluate("closeM('m_link');showLogin()"); pg.wait_for_timeout(800); pg.screenshot(path=f'{OUT}/login.png')
    b.close()
print(errs)
print(sorted(os.listdir(OUT)))
