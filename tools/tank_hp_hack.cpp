/*
 * tank_hp_hack.cpp  —  SE315.Q21  ESP + Minimap
 * Build: cl /EHsc /W3 /std:c++17 /nologo tank_hp_hack.cpp /Fe:tank_hp_hack.exe /link user32.lib gdi32.lib
 * F8: Bot ON   F9: Bot OFF   ESC: Thoat
 */

#define NOMINMAX
#include <windows.h>
#include <tlhelp32.h>
#include <vector>
#include <thread>
#include <atomic>
#include <mutex>
#include <chrono>
#include <cmath>
#include <cstdio>
#include <cstring>
#include <string>
#include <iostream>

using clk  = std::chrono::steady_clock;
using ms_t = std::chrono::milliseconds;

// ── Snapshot layout ───────────────────────────────────────────────────────────
// Header 14 bytes: matchId(4) opcode(2)=2000 tick(2) tankCount(2) localId(2) timeLeft(2)
// Gap    2 bytes
// Per tank 26 bytes: id(4) x(4) y(4) z(4) yaw(4) hp(2) flags(1) score(2) place(1)
#pragma pack(push,1)
struct SnapHdr { uint32_t matchId; uint16_t opcode, tick, tankCount, localId, timeLeft; };
struct TankRaw { uint32_t id; float x,y,z,yaw; int16_t hp; uint8_t flags; uint16_t score; uint8_t placement; };
#pragma pack(pop)
static_assert(sizeof(SnapHdr)==14 && sizeof(TankRaw)==26, "layout");

static const size_t HDR=sizeof(SnapHdr), TANK=sizeof(TankRaw), GAP=2;

// ── Parsed types ──────────────────────────────────────────────────────────────
struct Tank { uint32_t id; float x,z,yaw; int hp; bool alive,isMe; float dist; };
struct Snap { bool valid=false; uint32_t matchId=0; uint16_t tick=0; std::vector<Tank> tanks; };

// ── AntiCheat simulation flag ─────────────────────────────────────────────────
// When this file exists, cheat is "blocked" (simulates kernel driver blocking)
static const char* AC_FLAG = "D:\\anticheat_active.flag";
static bool acBlocked() { return GetFileAttributesA(AC_FLAG) != INVALID_FILE_ATTRIBUTES; }

// ── Globals ───────────────────────────────────────────────────────────────────
static HANDLE            g_proc    = nullptr;
static HWND              g_gameWnd = nullptr;
static HWND              g_overlay = nullptr;
static std::mutex        g_mtx;
static Snap              g_snap;
static std::atomic<bool> g_running { true };
static std::atomic<bool> g_botOn   { false };
static std::atomic<bool> g_blocked { false };

// ── Process helpers ───────────────────────────────────────────────────────────
static std::vector<DWORD> findPIDs(const wchar_t* n) {
    std::vector<DWORD> v;
    HANDLE s = CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS,0);
    PROCESSENTRY32W e={sizeof(e)};
    if(Process32FirstW(s,&e)) do { if(!_wcsicmp(e.szExeFile,n)) v.push_back(e.th32ProcessID); } while(Process32NextW(s,&e));
    CloseHandle(s); return v;
}
struct EC { DWORD pid; HWND hwnd; };
static BOOL CALLBACK ep(HWND h,LPARAM lp){auto*c=(EC*)lp;DWORD p=0;GetWindowThreadProcessId(h,&p);if(p==c->pid&&IsWindowVisible(h)){c->hwnd=h;return FALSE;}return TRUE;}
static HWND findWin(DWORD pid){EC c={pid,nullptr};EnumWindows(ep,(LPARAM)&c);return c.hwnd;}
static DWORD pickPID(){
    auto pids=findPIDs(L"Tank Legends.exe");
    if(pids.empty()){std::wcerr<<L"[-] Khong thay process\n";return 0;}
    if(pids.size()==1){std::wcout<<L"[+] PID="<<pids[0]<<L"\n";return pids[0];}
    DWORD fg=0;GetWindowThreadProcessId(GetForegroundWindow(),&fg);
    for(size_t i=0;i<pids.size();i++){
        wchar_t t[256]={};HWND hw=findWin(pids[i]);if(hw)GetWindowTextW(hw,t,256);
        std::wcout<<L"  ["<<(i+1)<<L"] PID="<<pids[i]<<L"  \""<<(t[0]?t:L"?")<<L"\""<<(pids[i]==fg?L" <<FOCUS":L"")<<L"\n";
    }
    std::cout<<"Chon: ";size_t c=0;std::cin>>c;
    return(c>=1&&c<=pids.size())?pids[c-1]:0;
}

// ── Snapshot parsing ──────────────────────────────────────────────────────────
// STRICT: dung cho fullScan de loai false-positive trong RAM
static bool parseStrict(const BYTE* buf, SIZE_T n, Snap& out) {
    if (n < HDR+GAP+TANK) return false;
    SnapHdr h; memcpy(&h, buf, HDR);
    if (h.opcode!=2000 || h.tankCount==0 || h.tankCount>8 || h.matchId==0) return false;
    if (h.localId==0 || h.localId>255) return false;
    if (n < HDR+GAP+(size_t)h.tankCount*TANK) return false;
    TankRaw raw[8]; memcpy(raw, buf+HDR+GAP, h.tankCount*TANK);
    bool hasLive=false;
    for(int i=0;i<h.tankCount;i++){
        if(raw[i].hp<0||raw[i].hp>200) return false;
        if(fabsf(raw[i].y)>500.f||fabsf(raw[i].x)>9000.f||fabsf(raw[i].z)>9000.f) return false;
        if(raw[i].hp>0) hasLive=true;
    }
    if(!hasLive) return false;
    float mx=0,mz=0;
    for(int i=0;i<h.tankCount;i++) if(raw[i].id==h.localId){mx=raw[i].x;mz=raw[i].z;break;}
    out={true,h.matchId,h.tick,{}};
    for(int i=0;i<h.tankCount;i++){
        float dx=raw[i].x-mx,dz=raw[i].z-mz;
        out.tanks.push_back({raw[i].id,raw[i].x,raw[i].z,raw[i].yaw,raw[i].hp,
                             (raw[i].flags&1)!=0,(raw[i].id==h.localId),sqrtf(dx*dx+dz*dz)});
    }
    return true;
}

// LOOSE: dung cho readAt — tin tuong vao dia chi, chi check opcode + co ban
static bool parseLoose(const BYTE* buf, SIZE_T n, Snap& out) {
    if (n < HDR+GAP+TANK) return false;
    SnapHdr h; memcpy(&h, buf, HDR);
    if (h.opcode!=2000 || h.tankCount==0 || h.tankCount>8) return false;
    if (h.matchId==0 || h.localId==0 || h.localId>255) return false;
    if (n < HDR+GAP+(size_t)h.tankCount*TANK) return false;
    TankRaw raw[8]; memcpy(raw, buf+HDR+GAP, h.tankCount*TANK);
    float mx=0,mz=0;
    for(int i=0;i<h.tankCount;i++) if(raw[i].id==h.localId){mx=raw[i].x;mz=raw[i].z;break;}
    out={true,h.matchId,h.tick,{}};
    for(int i=0;i<h.tankCount;i++){
        float dx=raw[i].x-mx,dz=raw[i].z-mz;
        int hp=raw[i].hp; if(hp<0)hp=0; if(hp>200)hp=200;
        out.tanks.push_back({raw[i].id,raw[i].x,raw[i].z,raw[i].yaw,hp,
                             (raw[i].flags&1)!=0,(raw[i].id==h.localId),sqrtf(dx*dx+dz*dz)});
    }
    return true;
}

static bool readAt(uintptr_t base, Snap& out) {
    if(g_blocked) return false;
    BYTE buf[HDR+GAP+8*TANK+16]; SIZE_T n=0;
    if(!ReadProcessMemory(g_proc,(LPCVOID)base,buf,sizeof(buf),&n)) return false;
    return parseLoose(buf,n,out);
}

// Scan 1 vung nho (da biet) — nhanh ~10-50ms
static std::pair<uintptr_t,Snap> scanRegion(uintptr_t regBase, size_t regSize) {
    if(!regBase||!regSize) return {0,{}};
    std::vector<BYTE> buf(regSize); SIZE_T n=0;
    if(!ReadProcessMemory(g_proc,(LPCVOID)regBase,buf.data(),regSize,&n)) return {0,{}};
    uintptr_t bestAddr=0; Snap best;
    for(SIZE_T i=0;i+HDR+GAP+TANK<=n;i++){
        uint16_t op; memcpy(&op,&buf[i+4],2);
        if(op!=2000) continue;
        Snap s;
        if(!parseStrict(&buf[i],n-i,s)) continue;
        if(best.valid && s.tick<=best.tick) continue;
        bestAddr=regBase+i; best=s;
    }
    return {bestAddr,best};
}

// Full scan toan bo RAM — chi dung khi region scan that bai
static std::pair<uintptr_t,Snap> fullScan() {
    if(g_blocked) return {0,{}};
    uintptr_t bestAddr=0; Snap best;
    MEMORY_BASIC_INFORMATION mbi; uintptr_t addr=0;
    while(VirtualQueryEx(g_proc,(LPCVOID)addr,&mbi,sizeof(mbi))){
        uintptr_t base=(uintptr_t)mbi.BaseAddress; addr+=mbi.RegionSize;
        if(!(mbi.State==MEM_COMMIT&&(mbi.Protect&PAGE_READWRITE)&&mbi.RegionSize<32u*1024*1024)) continue;
        std::vector<BYTE> buf(mbi.RegionSize); SIZE_T n=0;
        if(!ReadProcessMemory(g_proc,(LPCVOID)base,buf.data(),mbi.RegionSize,&n)) continue;
        for(SIZE_T i=0;i+HDR+GAP+TANK<=n;i++){
            uint16_t op; memcpy(&op,&buf[i+4],2);
            if(op!=2000) continue;
            Snap s;
            if(!parseStrict(&buf[i],n-i,s)) continue;
            if(best.valid && s.tick<=best.tick) continue;
            bestAddr=base+i; best=s;
        }
    }
    return {bestAddr,best};
}

// Lay thong tin vung nho chua dia chi
static void getRegion(uintptr_t addr, uintptr_t& base, size_t& size) {
    MEMORY_BASIC_INFORMATION mbi;
    if(VirtualQueryEx(g_proc,(LPCVOID)addr,&mbi,sizeof(mbi))){
        base=(uintptr_t)mbi.BaseAddress; size=mbi.RegionSize;
    } else { base=0; size=0; }
}

// ── Scan thread ───────────────────────────────────────────────────────────────
// Khi tick khong doi 300ms (game da ghi sang buffer moi):
//   → regionScan truoc (nhanh, tim dia chi moi trong cung vung nho)
//   → neu that bai thi fullScan (cham, nhung giu g_snap cu nen khong flickering)
static void scanLoop() {
    uintptr_t addr=0;
    uintptr_t regBase=0; size_t regSize=0;
    uint16_t  lastTick=0xFFFF;
    clk::time_point lastChange=clk::now();
    int failCnt=0;

    auto doRescan = [&](Snap& snap) {
        // Thu regionScan truoc
        auto[na,ns]=scanRegion(regBase,regSize);
        if(na && ns.tick!=lastTick){
            addr=na; snap=ns; lastTick=ns.tick; lastChange=clk::now();
            getRegion(addr,regBase,regSize);
            return;
        }
        // Region scan khong tim duoc → fullScan
        auto[na2,ns2]=fullScan();
        if(na2){
            addr=na2; snap=ns2; lastTick=ns2.tick; lastChange=clk::now();
            getRegion(addr,regBase,regSize);
        } else {
            addr=0; regBase=0; regSize=0; snap={}; lastTick=0xFFFF;
        }
    };

    while(g_running){
        // Check anticheat flag file every iteration
        bool nowBlocked = acBlocked();
        if(nowBlocked != g_blocked.load()) {
            g_blocked = nowBlocked;
            if(nowBlocked) { addr=0; regBase=0; regSize=0; lastTick=0xFFFF; }
        }
        if(g_blocked) { std::lock_guard<std::mutex> lk(g_mtx); g_snap={}; }
        if(g_blocked) { std::this_thread::sleep_for(ms_t(500)); continue; }

        Snap snap;

        if(addr){
            if(readAt(addr,snap)){
                failCnt=0;
                if(snap.tick!=lastTick){
                    lastTick=snap.tick; lastChange=clk::now();
                } else {
                    auto age=std::chrono::duration_cast<ms_t>(clk::now()-lastChange).count();
                    if(age>300) doRescan(snap);  // 300ms = 6 missed ticks @ 20Hz
                }
            } else {
                failCnt++;
                if(failCnt<5){
                    std::this_thread::sleep_for(ms_t(50)); continue;
                }
                failCnt=0;
                doRescan(snap);
            }
        } else {
            auto[na,ns]=fullScan();
            if(na){
                addr=na; snap=ns; lastTick=ns.tick; lastChange=clk::now();
                getRegion(addr,regBase,regSize);
            }
        }

        { std::lock_guard<std::mutex> lk(g_mtx); g_snap=snap; }
        std::this_thread::sleep_for(ms_t(snap.valid?50:500));
    }
}

// ── Console thread ────────────────────────────────────────────────────────────
static void consoleLoop(){
    bool wasValid=false; uint16_t lastTick=0xFFFF;
    printf("Chua vao match...\nF8=Bot ON  F9=Bot OFF  ESC=Thoat\n");
    while(g_running){
        Snap s; {std::lock_guard<std::mutex> lk(g_mtx); s=g_snap;}
        if(s.valid){
            if(!wasValid||s.tick!=lastTick){
                if(!wasValid) printf("\033[2J");  // xoa man hinh 1 lan khi vao match
                printf("\033[H");
                printf("Match:%-6u  Tick:%-5u  Bot:%s\n",s.matchId,s.tick,g_botOn?"ON ":"OFF");
                for(auto&t:s.tanks)
                    printf("%s ID:%-3u  X:%8.1f  Z:%8.1f  HP:%-3d  %s\n",
                           t.isMe?"[ME ]":"[FOE]",t.id,t.x,t.z,t.hp,t.alive?"alive":"dead ");
                printf("\033[J");  // xoa phan con lai (tranh ghost text)
                wasValid=true; lastTick=s.tick;
            }
        } else if(wasValid){
            printf("\033[2J\033[H""Chua vao match...\n"); wasValid=false;
        }
        std::this_thread::sleep_for(ms_t(50));
    }
}

// ── Bot stub ──────────────────────────────────────────────────────────────────
static void botLoop(){ while(g_botOn) std::this_thread::sleep_for(ms_t(100)); }

// ── GDI helpers ───────────────────────────────────────────────────────────────
static const COLORREF CHROMA=RGB(0,128,0);
static void gFill(HDC dc,int x,int y,int w,int h,COLORREF c){HBRUSH b=CreateSolidBrush(c);RECT r={x,y,x+w,y+h};FillRect(dc,&r,b);DeleteObject(b);}
static void gText(HDC dc,int x,int y,COLORREF c,int sz,const char*s){
    HFONT f=CreateFontA(sz,0,0,0,FW_BOLD,0,0,0,DEFAULT_CHARSET,0,0,ANTIALIASED_QUALITY,0,"Arial");
    HFONT o=(HFONT)SelectObject(dc,f);SetTextColor(dc,c);SetBkMode(dc,TRANSPARENT);
    TextOutA(dc,x,y,s,(int)strlen(s));SelectObject(dc,o);DeleteObject(f);}
static void gCirc(HDC dc,int cx,int cy,int r,COLORREF c,int t=2){
    HPEN p=CreatePen(PS_SOLID,t,c);HPEN op=(HPEN)SelectObject(dc,p);
    HBRUSH b=(HBRUSH)GetStockObject(NULL_BRUSH);HBRUSH ob=(HBRUSH)SelectObject(dc,b);
    Ellipse(dc,cx-r,cy-r,cx+r,cy+r);SelectObject(dc,op);SelectObject(dc,ob);DeleteObject(p);}
static void gLine(HDC dc,int x1,int y1,int x2,int y2,COLORREF c,int t=1){
    HPEN p=CreatePen(PS_SOLID,t,c);HPEN op=(HPEN)SelectObject(dc,p);
    MoveToEx(dc,x1,y1,nullptr);LineTo(dc,x2,y2);SelectObject(dc,op);DeleteObject(p);}

// ── Draw overlay ──────────────────────────────────────────────────────────────
static void drawOverlay(HDC dc, int W, int H){
    gFill(dc,0,0,W,H,CHROMA);
    Snap s; {std::lock_guard<std::mutex> lk(g_mtx); s=g_snap;}

    if(g_blocked){
        gText(dc,10,10,RGB(255,50,50),20,"[ANTICHEAT] MEMORY ACCESS BLOCKED");
        gText(dc,10,36,RGB(255,120,0),14,"OpenProcess -> ERROR_ACCESS_DENIED (0x5)");
        gText(dc,10,56,RGB(200,200,200),13,"Driver: anticheat_km.sys  |  Ring-0 callback active");
        return;
    }
    if(!s.valid){
        gText(dc,10,10,RGB(255,255,0),16,"ESP: Chua vao match...");
        char sb[64]; sprintf_s(sb,"Bot:%s  [F8]ON [F9]OFF [ESC]Thoat",g_botOn?"ON":"OFF");
        gText(dc,10,H-24,RGB(160,160,160),13,sb); return;
    }

    // Minimap (top-right)
    const int MAP=200,MX=W-MAP-15,MY=15; const float R=130.f;
    gFill(dc,MX,MY,MAP,MAP,RGB(10,10,10));
    gLine(dc,MX,MY,MX+MAP,MY,RGB(70,70,70));gLine(dc,MX+MAP,MY,MX+MAP,MY+MAP,RGB(70,70,70));
    gLine(dc,MX+MAP,MY+MAP,MX,MY+MAP,RGB(70,70,70));gLine(dc,MX,MY+MAP,MX,MY,RGB(70,70,70));
    gLine(dc,MX+MAP/2,MY,MX+MAP/2,MY+MAP,RGB(35,35,35));
    gLine(dc,MX,MY+MAP/2,MX+MAP,MY+MAP/2,RGB(35,35,35));
    gText(dc,MX+4,MY+2,RGB(70,70,70),11,"N");

    float myX=0,myZ=0;
    for(auto&t:s.tanks) if(t.isMe){myX=t.x;myZ=t.z;break;}

    for(auto&t:s.tanks){
        int px=MX+MAP/2+(int)((t.x-myX)/R*(MAP/2-10));
        int py=MY+MAP/2-(int)((t.z-myZ)/R*(MAP/2-10));
        px=std::max(MX+4,std::min(MX+MAP-4,px));
        py=std::max(MY+4,std::min(MY+MAP-4,py));
        if(t.isMe){
            gCirc(dc,px,py,7,RGB(0,255,0),2);
            gText(dc,px-8,py-20,RGB(0,255,0),11,"ME");
        } else if(!t.alive){
            gText(dc,px-4,py-6,RGB(100,100,100),13,"x");
        } else {
            gCirc(dc,px,py,6,RGB(255,50,50),2);
            gLine(dc,MX+MAP/2,MY+MAP/2,px,py,RGB(170,40,40),1);
            char lbl[8]; sprintf_s(lbl,"%d",t.hp);
            gText(dc,px+8,py-6,RGB(255,200,0),11,lbl);
        }
    }

    // Player list (top-left)
    char hdr[64]; sprintf_s(hdr,"Match:%-6u  Tick:%-5u",s.matchId,s.tick);
    gText(dc,10,10,RGB(200,200,200),14,hdr);
    int rowY=30;
    for(auto&t:s.tanks){
        char line[160]; COLORREF col;
        if(t.isMe){
            col=RGB(0,255,0);
            sprintf_s(line,"[ME  ]  ID:%-3u  X:%8.1f  Z:%8.1f  HP:%d",t.id,t.x,t.z,t.hp);
        } else {
            float dx=t.x-myX,dz=t.z-myZ;
            float deg=atan2f(dx,dz)*180.f/3.14159265f; if(deg<0)deg+=360.f;
            static const char*dirs[]={"N","NE","E","SE","S","SW","W","NW"};
            col=t.alive?RGB(255,60,60):RGB(120,120,120);
            sprintf_s(line,"[%s]  ID:%-3u  X:%8.1f  Z:%8.1f  HP:%-4d  Dist:%-6.0f  %s",
                      t.alive?"ENEMY":"DEAD ",t.id,t.x,t.z,t.hp,t.dist,dirs[(int)((deg+22.5f)/45.f)%8]);
        }
        gText(dc,10,rowY,col,14,line); rowY+=20;
    }
    char sb[80]; sprintf_s(sb,"[F8]Bot ON  [F9]Bot OFF  [ESC]Thoat  |  Bot:%s",g_botOn?"ON":"OFF");
    gText(dc,10,H-24,g_botOn?RGB(255,200,0):RGB(160,160,160),13,sb);
}

// ── Overlay WndProc ───────────────────────────────────────────────────────────
static LRESULT CALLBACK wndProc(HWND hw,UINT msg,WPARAM wp,LPARAM lp){
    switch(msg){
    case WM_PAINT:{
        PAINTSTRUCT ps; HDC dc=BeginPaint(hw,&ps);
        RECT rc; GetClientRect(hw,&rc);
        HDC mdc=CreateCompatibleDC(dc);
        HBITMAP bmp=CreateCompatibleBitmap(dc,rc.right,rc.bottom);
        HBITMAP ob=(HBITMAP)SelectObject(mdc,bmp);
        drawOverlay(mdc,rc.right,rc.bottom);
        BitBlt(dc,0,0,rc.right,rc.bottom,mdc,0,0,SRCCOPY);
        SelectObject(mdc,ob);DeleteObject(bmp);DeleteDC(mdc);
        EndPaint(hw,&ps);return 0;}
    case WM_TIMER: InvalidateRect(hw,nullptr,FALSE);return 0;
    case WM_DESTROY: PostQuitMessage(0);return 0;
    }
    return DefWindowProcW(hw,msg,wp,lp);
}

// ── Main ──────────────────────────────────────────────────────────────────────
int main(){
    HANDLE hCon=GetStdHandle(STD_OUTPUT_HANDLE);
    DWORD cm=0;GetConsoleMode(hCon,&cm);
    SetConsoleMode(hCon,cm|ENABLE_VIRTUAL_TERMINAL_PROCESSING);
    CONSOLE_CURSOR_INFO ci={1,FALSE};SetConsoleCursorInfo(hCon,&ci);
    std::cout<<"=== TankOnline ESP — SE315.Q21 ===\n\n";

    DWORD pid=pickPID(); if(!pid){system("pause");return 1;}
    g_gameWnd=findWin(pid);
    if(!g_gameWnd){std::cout<<"[-] Khong thay cua so game.\n";system("pause");return 1;}
    g_proc=OpenProcess(PROCESS_ALL_ACCESS,FALSE,pid);
    if(!g_proc){std::cout<<"[-] OpenProcess that bai — chay Administrator.\n";system("pause");return 1;}

    RECT gr; GetWindowRect(g_gameWnd,&gr);
    HINSTANCE hi=GetModuleHandleW(nullptr);
    WNDCLASSEXW wc={sizeof(wc)};wc.lpfnWndProc=wndProc;wc.hInstance=hi;wc.lpszClassName=L"TankESP3";
    RegisterClassExW(&wc);
    g_overlay=CreateWindowExW(WS_EX_TOPMOST|WS_EX_LAYERED|WS_EX_TRANSPARENT|WS_EX_NOACTIVATE,
        L"TankESP3",L"",WS_POPUP,gr.left,gr.top,gr.right-gr.left,gr.bottom-gr.top,
        nullptr,nullptr,hi,nullptr);
    SetLayeredWindowAttributes(g_overlay,CHROMA,0,LWA_COLORKEY);
    ShowWindow(g_overlay,SW_SHOW);SetTimer(g_overlay,1,50,nullptr);

    std::thread tScan(scanLoop), tCon(consoleLoop), tBot;
    auto stopBot =[&](){g_botOn=false;if(tBot.joinable())tBot.join();};
    auto startBot=[&](){stopBot();g_botOn=true;tBot=std::thread(botLoop);};

    bool pF8=false,pF9=false,pEsc=false;
    MSG msg={};
    while(true){
        while(PeekMessageW(&msg,nullptr,0,0,PM_REMOVE)){
            if(msg.message==WM_QUIT) goto done;
            TranslateMessage(&msg);DispatchMessageW(&msg);
        }
        RECT r; GetWindowRect(g_gameWnd,&r);
        SetWindowPos(g_overlay,HWND_TOPMOST,r.left,r.top,r.right-r.left,r.bottom-r.top,SWP_NOACTIVATE);
        bool f8=(GetAsyncKeyState(VK_F8)&0x8000)!=0;
        bool f9=(GetAsyncKeyState(VK_F9)&0x8000)!=0;
        bool esc=(GetAsyncKeyState(VK_ESCAPE)&0x8000)!=0;
        if(f8&&!pF8) startBot();
        if(f9&&!pF9) stopBot();
        if(esc&&!pEsc) break;
        pF8=f8;pF9=f9;pEsc=esc;
        std::this_thread::sleep_for(ms_t(10));
    }
done:
    g_running=false; stopBot();
    tScan.join(); tCon.join();
    DestroyWindow(g_overlay); CloseHandle(g_proc);
    return 0;
}
