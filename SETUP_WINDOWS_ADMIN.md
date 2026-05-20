# Windows Admin Setup (chạy PowerShell Admin)

## Lần đầu setup (chỉ cần chạy 1 lần)

```powershell
# 1. Dừng MiniTool ShadowMaker (đang chiếm TCP port 8080)
Stop-Service -Name MTAgentService -Force
Set-Service -Name MTAgentService -StartupType Disabled

# 2. Port forwarding: WiFi + Hotspot → WSL2 API Gateway
netsh interface portproxy add v4tov4 listenaddress=0.0.0.0 listenport=8080 connectaddress=172.25.203.168 connectport=8080

# 3. Port forwarding IPv6 (Unity Editor dùng localhost → [::1])
netsh interface portproxy add v6tov4 listenaddress=::1 listenport=8080 connectaddress=172.25.203.168 connectport=8080

# 4. Firewall rules
New-NetFirewallRule -DisplayName "WSL2-API-8080"      -Direction Inbound -Protocol TCP -LocalPort 8080 -Action Allow
New-NetFirewallRule -DisplayName "WSL2-Frontend-5173" -Direction Inbound -Protocol TCP -LocalPort 5173 -Action Allow
```

---

## Mỗi lần khởi động lại máy

```powershell
# WSL2 IP có thể đổi sau reboot — cập nhật port proxy
$WSL2_IP = (wsl hostname -I).Trim().Split(' ')[0]

netsh interface portproxy delete v4tov4 listenaddress=0.0.0.0 listenport=8080
netsh interface portproxy add    v4tov4 listenaddress=0.0.0.0 listenport=8080 connectaddress=$WSL2_IP connectport=8080

netsh interface portproxy delete v6tov4 listenaddress=::1 listenport=8080
netsh interface portproxy add    v6tov4 listenaddress=::1 listenport=8080 connectaddress=$WSL2_IP connectport=8080

Write-Host "WSL2 IP: $WSL2_IP"
netsh interface portproxy show all
```

---

## IP mobile dùng để kết nối

| Mobile kết nối qua | IP server cần dùng |
|--------------------|-------------------|
| **Cùng WiFi router** với laptop | `10.11.1.149:8080` (đổi theo WiFi) |
| **Hotspot từ laptop** | `192.168.137.1:8080` (cố định) |

> Lấy WiFi IP hiện tại:
> ```powershell
> (Get-NetIPAddress -AddressFamily IPv4 -InterfaceAlias Wi-Fi).IPAddress
> ```

---

## Kiểm tra port proxy hiện tại

```powershell
netsh interface portproxy show all
```

## Xem port 8080 đang bị chiếm bởi process nào

```powershell
netstat -ano | findstr ":8080"
Get-Process -Id <PID>
```
