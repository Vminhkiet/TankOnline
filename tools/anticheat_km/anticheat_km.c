/*
 * anticheat_km.c  —  SE315.Q21  Kernel-Mode AntiCheat Driver
 *
 * Co che:
 *   [1] PsSetCreateProcessNotifyRoutineEx  — theo doi khi game process khoi dong / tat
 *   [2] ObRegisterCallbacks (PsProcessType) — chan handle OpenProcess toi game
 *       voi quyen PROCESS_VM_READ / PROCESS_VM_OPERATION truoc khi user-mode nhan duoc
 *
 * Build: WDK 10 + MSBuild (xem build.bat)
 * Load : loader.exe (chay Administrator)
 */

#include <ntddk.h>
#include <wdm.h>

/* Process access rights — not always exported in km headers from partial WDK installs */
#ifndef PROCESS_TERMINATE
#define PROCESS_TERMINATE         (0x0001)
#define PROCESS_CREATE_THREAD     (0x0002)
#define PROCESS_VM_OPERATION      (0x0008)
#define PROCESS_VM_READ           (0x0010)
#define PROCESS_VM_WRITE          (0x0020)
#endif

// ── Config ────────────────────────────────────────────────────────────────────
#define DRIVER_TAG       'KMCA'
#define DEVICE_NAME      L"\\Device\\TankAntiCheat"
#define SYMLINK_NAME     L"\\DosDevices\\TankAntiCheat"
#define GAME_IMAGE_NAME  L"Tank Legends.exe"

// Quyen bi cam voi process khong phai game
#define STRIP_ACCESS (PROCESS_VM_READ      | \
                      PROCESS_VM_WRITE     | \
                      PROCESS_VM_OPERATION | \
                      PROCESS_CREATE_THREAD)

// ── Globals ───────────────────────────────────────────────────────────────────
static PEPROCESS  g_gameProcess   = NULL;  // EPROCESS cua game
static HANDLE     g_gamePid       = NULL;
static PVOID      g_obHandle      = NULL;  // handle tu ObRegisterCallbacks
static FAST_MUTEX g_lock;                  // bao ve g_gameProcess

// ── Forward declarations ──────────────────────────────────────────────────────
DRIVER_UNLOAD     AcDriverUnload;
DRIVER_DISPATCH   AcDispatchDefault;

static void       AcProcessNotify(PEPROCESS Process, HANDLE ProcessId,
                                  PPS_CREATE_NOTIFY_INFO CreateInfo);
static OB_PREOP_CALLBACK_STATUS AcObPreCallback(
                                  PVOID RegistrationContext,
                                  POB_PRE_OPERATION_INFORMATION Info);

// ── Utility: kiem tra ten image co khop GAME_IMAGE_NAME ──────────────────────
static BOOLEAN IsGameProcess(PPS_CREATE_NOTIFY_INFO Info) {
    if (!Info || !Info->ImageFileName) return FALSE;
    UNICODE_STRING gameName;
    RtlInitUnicodeString(&gameName, GAME_IMAGE_NAME);

    // So sanh phan cuoi cua duong dan (bao gom ten file)
    PCUNICODE_STRING full = Info->ImageFileName;
    if (full->Length < gameName.Length) return FALSE;

    UNICODE_STRING tail;
    tail.Buffer        = (PWCH)((PUCHAR)full->Buffer +
                                full->Length - gameName.Length);
    tail.Length        = gameName.Length;
    tail.MaximumLength = gameName.Length;

    return RtlEqualUnicodeString(&tail, &gameName, TRUE);
}

// ── [1] Process notify callback ───────────────────────────────────────────────
static void AcProcessNotify(PEPROCESS Process, HANDLE ProcessId,
                             PPS_CREATE_NOTIFY_INFO CreateInfo)
{
    ExAcquireFastMutex(&g_lock);

    if (CreateInfo) {
        // Process dang tao
        if (IsGameProcess(CreateInfo)) {
            g_gameProcess = Process;
            g_gamePid     = ProcessId;
            DbgPrint("[AC-KM] Game started: PID=%llu  EPROCESS=%p\n",
                     (ULONG64)(ULONG_PTR)ProcessId, Process);
        }
    } else {
        // Process dang tat
        if (ProcessId == g_gamePid) {
            DbgPrint("[AC-KM] Game exited: PID=%llu\n",
                     (ULONG64)(ULONG_PTR)ProcessId);
            g_gameProcess = NULL;
            g_gamePid     = NULL;
        }
    }

    ExReleaseFastMutex(&g_lock);
}

// ── [2] ObRegisterCallbacks pre-callback ─────────────────────────────────────
// Duoc goi truoc khi kernel cap handle cho caller.
// Neu caller dang mo game process voi quyen doc bo nho → strip quyen do.
static OB_PREOP_CALLBACK_STATUS AcObPreCallback(
    PVOID RegistrationContext,
    POB_PRE_OPERATION_INFORMATION Info)
{
    UNREFERENCED_PARAMETER(RegistrationContext);

    // Chi quan tam tao handle moi hoac duplicate
    if (Info->Operation != OB_OPERATION_HANDLE_CREATE &&
        Info->Operation != OB_OPERATION_HANDLE_DUPLICATE)
        return OB_PREOP_SUCCESS;

    // Lay EPROCESS cua doi tuong dang bi mo handle
    PEPROCESS targetProcess = (PEPROCESS)Info->Object;

    ExAcquireFastMutex(&g_lock);
    PEPROCESS game = g_gameProcess;
    ExReleaseFastMutex(&g_lock);

    if (!game || targetProcess != game) return OB_PREOP_SUCCESS;

    // Caller la chinh game process → cho phep (self-access)
    if (PsGetCurrentProcess() == game) return OB_PREOP_SUCCESS;

    // Kiem tra co yeu cau quyen nguy hiem khong
    ACCESS_MASK desired = (Info->Operation == OB_OPERATION_HANDLE_CREATE)
        ? Info->Parameters->CreateHandleInformation.DesiredAccess
        : Info->Parameters->DuplicateHandleInformation.DesiredAccess;

    if (!(desired & STRIP_ACCESS)) return OB_PREOP_SUCCESS;

    // Strip quyen doc/ghi bo nho
    ACCESS_MASK stripped = desired & ~STRIP_ACCESS;

    if (Info->Operation == OB_OPERATION_HANDLE_CREATE)
        Info->Parameters->CreateHandleInformation.DesiredAccess = stripped;
    else
        Info->Parameters->DuplicateHandleInformation.DesiredAccess = stripped;

    // Log: ten process cua caller
    HANDLE callerPid = PsGetCurrentProcessId();
    DbgPrint("[AC-KM] BLOCKED memory access from PID=%llu  "
             "desired=0x%08X  stripped=0x%08X\n",
             (ULONG64)(ULONG_PTR)callerPid, desired, stripped);

    return OB_PREOP_SUCCESS;
}

// ── Driver Unload ─────────────────────────────────────────────────────────────
VOID AcDriverUnload(PDRIVER_OBJECT DriverObject) {
    DbgPrint("[AC-KM] Unloading...\n");

    // Huy dang ky ObCallbacks truoc
    if (g_obHandle) {
        ObUnRegisterCallbacks(g_obHandle);
        g_obHandle = NULL;
    }

    // Huy dang ky process notify
    PsSetCreateProcessNotifyRoutineEx(AcProcessNotify, TRUE);

    // Xoa symlink va device
    UNICODE_STRING symlink;
    RtlInitUnicodeString(&symlink, SYMLINK_NAME);
    IoDeleteSymbolicLink(&symlink);

    if (DriverObject->DeviceObject)
        IoDeleteDevice(DriverObject->DeviceObject);

    DbgPrint("[AC-KM] Unloaded.\n");
}

NTSTATUS AcDispatchDefault(PDEVICE_OBJECT DeviceObject, PIRP Irp) {
    UNREFERENCED_PARAMETER(DeviceObject);
    Irp->IoStatus.Status      = STATUS_SUCCESS;
    Irp->IoStatus.Information = 0;
    IoCompleteRequest(Irp, IO_NO_INCREMENT);
    return STATUS_SUCCESS;
}

// ── DriverEntry ───────────────────────────────────────────────────────────────
NTSTATUS DriverEntry(PDRIVER_OBJECT DriverObject, PUNICODE_STRING RegistryPath) {
    UNREFERENCED_PARAMETER(RegistryPath);
    NTSTATUS status;

    DbgPrint("[AC-KM] Loading TankAntiCheat driver...\n");

    DriverObject->DriverUnload = AcDriverUnload;
    for (int i = 0; i <= IRP_MJ_MAXIMUM_FUNCTION; i++)
        DriverObject->MajorFunction[i] = AcDispatchDefault;

    ExInitializeFastMutex(&g_lock);

    // Tao device object (de loader giao tiep)
    UNICODE_STRING devName, symName;
    RtlInitUnicodeString(&devName, DEVICE_NAME);
    RtlInitUnicodeString(&symName, SYMLINK_NAME);

    PDEVICE_OBJECT devObj = NULL;
    status = IoCreateDevice(DriverObject, 0, &devName,
                            FILE_DEVICE_UNKNOWN, 0, FALSE, &devObj);
    if (!NT_SUCCESS(status)) {
        DbgPrint("[AC-KM] IoCreateDevice failed: 0x%X\n", status);
        return status;
    }
    IoCreateSymbolicLink(&symName, &devName);

    // [1] Dang ky process notify
    status = PsSetCreateProcessNotifyRoutineEx(AcProcessNotify, FALSE);
    if (!NT_SUCCESS(status)) {
        DbgPrint("[AC-KM] PsSetCreateProcessNotifyRoutineEx failed: 0x%X\n", status);
        IoDeleteSymbolicLink(&symName);
        IoDeleteDevice(devObj);
        return status;
    }

    // [2] Dang ky ObCallbacks cho process handles
    OB_OPERATION_REGISTRATION opReg = {};
    opReg.ObjectType           = PsProcessType;
    opReg.Operations           = OB_OPERATION_HANDLE_CREATE |
                                  OB_OPERATION_HANDLE_DUPLICATE;
    opReg.PreOperation         = AcObPreCallback;
    opReg.PostOperation        = NULL;

    OB_CALLBACK_REGISTRATION cbReg = {};
    cbReg.Version                   = OB_FLT_REGISTRATION_VERSION;
    cbReg.OperationRegistrationCount = 1;
    cbReg.OperationRegistration     = &opReg;
    // Altitude phai la chuoi so duy nhat (khong trung voi driver khac)
    UNICODE_STRING altitude;
    RtlInitUnicodeString(&altitude, L"321321");
    cbReg.Altitude = altitude;

    status = ObRegisterCallbacks(&cbReg, &g_obHandle);
    if (!NT_SUCCESS(status)) {
        DbgPrint("[AC-KM] ObRegisterCallbacks failed: 0x%X  "
                 "(Driver phai duoc ky so de ObCallbacks hoat dong)\n", status);
        PsSetCreateProcessNotifyRoutineEx(AcProcessNotify, TRUE);
        IoDeleteSymbolicLink(&symName);
        IoDeleteDevice(devObj);
        return status;
    }

    DbgPrint("[AC-KM] Loaded OK. Waiting for '%ls'...\n", GAME_IMAGE_NAME);
    return STATUS_SUCCESS;
}
