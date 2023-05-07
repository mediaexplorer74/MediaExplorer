#pragma once

//////////////////////////////////////////////////////////////////////////
//					Injectool.h
//
//	����:			Sonic Guan
//	����:			2008-12-8
//	�汾:			v1.0
//	˵��:			�ײ㸨���⣬��װring3��ĵײ������ʵ���ڴ�dll������ע�룬�����ԣ�API Hook��
//					���ܣ��������ڷǷ���;
//////////////////////////////////////////////////////////////////////////

//////////////////////////////////////////////////////////////////////////
// ���뿪�� 

#define COMPONENT_PE_MGR		0
#define COMPONENT_DEBUGGER		0
#define COMPONENT_MD5			0
#define COMPONENT_SUPERVISOR	0
#define COMPONENT_HARDWARE_INFO	0
#define COMPONENT_API_WRAPPER	0
#define COMPONENT_API_HOOK		1
#define COMPONENT_ANTI_DEBUG	0

#if COMPONENT_SUPERVISOR
#undef COMPONENT_PE_MGR
#define COMPONENT_PE_MGR 1
#undef COMPONENT_MD5
#define COMPONENT_MD5 1
#endif

//////////////////////////////////////////////////////////////////////////
// �������ܣ����ù���
// ��ȫд�ڴ�
extern "C" BOOL WINAPI WriteMem(HANDLE hProcess, LPVOID lpBaseAddr, const char *pBuff, unsigned int nSize);

extern "C" void scMemcpy(void * pDest, const void * pSrc, size_t uSize);

extern "C" void scMemset(void * p, BYTE byVal, size_t uSize);

// �ж��Ƿ�32λ����
extern "C" BOOL Is32Process(HANDLE hProcess);

// bkd �ַ���hash
extern "C" DWORD BKDHash(const char * szText);

// ���ָ�����̵�UI�߳�Id�����Ի�ý���������
extern "C" DWORD WINAPI GetMainThreadId(DWORD dwProcessId, HWND * pWnd = NULL);

// �����߳��ڽ����е�˳�����߳�ID
extern "C" DWORD WINAPI GetThreadIdByIndex(DWORD dwProcessId, DWORD dwIndex = 0);

// ���ָ�����̸����̵�id
extern "C" DWORD WINAPI GetParentProcessId(DWORD dwProcessId);

// MapViewOfSection�ļ򵥷�װ
extern "C" LPVOID WINAPI SimpleMapViewOfSection(HANDLE hObj, HANDLE hProcess, DWORD &dwSize, DWORD dwProtection);

// ���ص��ý����е�ָ��ģ��
extern "C" BOOL WINAPI HideDll(LPCSTR lpszDllName);

// ���ģ���SectionHandle, size, ��ڵ�
extern "C" BOOL WINAPI GetModuleInfo(HMODULE hMod, DWORD * pSize = NULL, HANDLE * pSectoin = NULL, LPVOID * pEOP = NULL);

// ��һ��ģ���ָ��DLL�ĵ��������滻�������hModΪNULL��һ��Ϊ��ִ��exe��handle
// ע��szDllNameΪ.dll��׺����ʽ��"User32.dll"��"Gdi32.dll"����Ҫ��full path
extern "C" LPVOID WINAPI ReplaceIATFunc(LPCSTR szModuleName, LPCSTR szDllName, LPCSTR szFuncName, LPVOID lpNewFunc, DWORD * pFuncAddr = NULL);

// ntdllδ��������
extern "C" LONG WINAPI ZwQueryInformationProcess(IN HANDLE ProcessHandle, IN ULONG ProcessInformationClass, OUT PVOID ProcessInformation, IN ULONG ProcessInformationLength, OUT PULONG ReturnLength); 

#if COMPONENT_API_HOOK
//////////////////////////////////////////////////////////////////////////
// API HOOK���
// ɨ��64λ�����룬���Դ���һ��FUNC_EXT�Ķ����жϺ�������ǿ�ж�
typedef BOOL (* FUNC_EXT)(LPVOID pFun);
extern "C" LPVOID ScanCode(LPVOID pStart, DWORD dwLen, const BYTE pChar[], DWORD dwCharLen, FUNC_EXT pFunc = NULL);

// ����ָ����ַ�����Ų����Է��غ���ͷ���Ҳ�������NULL
extern "C" LPVOID ReverseScanFuncHead(LPVOID p, DWORD dwFindLen);

// �滻һ��������������һ���ڴ棬��ԭ������������
extern "C" LPVOID ReplaceFuncAndCopy(LPVOID lpOldFunc, LPVOID lpNewFunc);
#endif

#if COMPONENT_HARDWARE_INFO
//////////////////////////////////////////////////////////////////////////
// ��ȡӲ����Ϣ���
// ���Ӳ�����к�
extern "C" BOOL WINAPI GetDiskSerialNumber(char szSerial[], UINT uSize);

// �������mac��ַ
extern "C" BOOL WINAPI GetMacInfo(int nMacNum, char szMac[], UINT uSize);
#endif //#if COMPONENT_HARDWARE_INFO

#if COMPONENT_ANTI_DEBUG
//////////////////////////////////////////////////////////////////////////
// ���������
// �����ԣ������̷߳����ԣ�ѭ�����
extern "C" void WINAPI AntiDebug();

// ����Ntδ�������������ԣ����bOnceΪ����Ϊһ���Լ�飬���򴴽������߳�ѭ�����
extern "C" void WINAPI AntiDebugByDebugPort(BOOL bOnce = TRUE);

// ר�����softice������
extern "C" void WINAPI AntiSoftice(BOOL bOnce = TRUE);

// ���ɻָ��������ƻ�ջ
extern "C" void WINAPI FatalError();
#endif

#if COMPONENT_MD5
//////////////////////////////////////////////////////////////////////////
// MD5����
extern "C" int SMd5(const char * inbuff,size_t len, char * outbuff, size_t outlen);
#endif


//////////////////////////////////////////////////////////////////////////
// ���������
#if COMPONENT_DEBUGGER
class CDebugger
{
public:
	typedef BOOL (WINAPI * FUNC_HANDLE_DEBUG_EVENT)(DEBUG_EVENT& de);
public:
	CDebugger();
	virtual ~CDebugger();
	BOOL CreateDebugProcess(LPCSTR lpszFile);
	BOOL Attach(DWORD dwProcessId, BOOL bKillOnExit);
	BOOL Detach();
	BOOL IsAttached();
	BOOL AddDebugEventHandler(FUNC_HANDLE_DEBUG_EVENT pFunc);
	BOOL DelDebugEventHandler(FUNC_HANDLE_DEBUG_EVENT pFunc);
	DWORD GetDebugProcessId();

	static CDebugger * GetInstance();
protected:
	static DWORD WINAPI _ThreadDebugEvent(LPVOID pParam);
	static DWORD WINAPI _ThreadCreateProcess(LPVOID pParam);
	void DebugLoop();
private:
	char m_szPath[MAX_PATH];
	DWORD m_dwProcessId;
	DWORD m_dwHandlerCount;
	BOOL m_bKillOnExit;
	FUNC_HANDLE_DEBUG_EVENT m_Func[10];
};
#endif


#if COMPONENT_API_WRAPPER
//////////////////////////////////////////////////////////////////////////
// API WRAPPER��Detour���ú���ͷ�����ŵ��Ժ;�̬�����
// α�캯��ͷ�Ը��Ŷϵ㣬����ָ��pNew��ָ����λ�ã������ָ�������Լ������ִ�д����
extern "C" LPVOID WINAPI FakeFuncHead(LPVOID pOld, LPVOID pNew = NULL);

class CApiWrapper
{
public:
	typedef struct tagApiShare
	{
		LPVOID pMain;
		LPVOID pSub;
	}API_SHARE;
public:
	CApiWrapper();
	virtual ~CApiWrapper();

	BOOL GetApiShare(API_SHARE & api);
	BOOL SetApiShare(const API_SHARE & api);
public:
	virtual HMODULE GetModuleHandle( IN LPCSTR lpModuleName );
	virtual LPVOID GetProcAddress( IN HMODULE hModule, IN LPCSTR lpProcName);
	HANDLE CreateFileMapping(HANDLE hFile, LPSECURITY_ATTRIBUTES lpAttributes, DWORD flProtect, DWORD dwMaximumSizeHigh, DWORD dwMaximumSizeLow, LPCTSTR lpName);
	LPVOID MapViewOfFile(HANDLE hFileMappingObject, DWORD dwDesiredAccess, DWORD dwFileOffsetHigh, DWORD dwFileOffsetLow, SIZE_T dwNumberOfBytesToMap);
	virtual LPVOID VirtualAlloc( IN LPVOID lpAddress, IN SIZE_T dwSize, IN DWORD flAllocationType, IN DWORD flProtect );
	BOOL UnmapViewOfFile(LPCVOID lpBaseAddress);
	virtual BOOL CloseHandle( IN OUT HANDLE hObject );
	virtual BOOL DebugActiveProcess(DWORD dwProcessId);
	virtual BOOL DebugActiveProcessStop(DWORD dwProcessId);
	virtual BOOL WaitForDebugEvent(LPDEBUG_EVENT lpDebugEvent, DWORD dwMilliseconds);
	virtual BOOL ContinueDebugEvent(DWORD dwProcessId, DWORD dwThreadId, DWORD dwContinueStatus);
	virtual BOOL DebugSetProcessKillOnExit(BOOL KillOnExit);
	virtual void ExitProcess( IN UINT uExitCode );
	virtual BOOL GetThreadContext(HANDLE hThread, LPCONTEXT lpContext);
	virtual HANDLE OpenThread(DWORD dwDesiredAccess, BOOL bInheritHandle, DWORD dwThreadId);
	virtual HANDLE OpenProcess(DWORD dwDesiredAccess, BOOL bInheritHandle, DWORD dwProcessId);
	virtual BOOL CreateProcessA( IN LPCSTR lpApplicationName, IN LPSTR lpCommandLine, IN LPSECURITY_ATTRIBUTES lpProcessAttributes, IN LPSECURITY_ATTRIBUTES lpThreadAttributes, IN BOOL bInheritHandles, IN DWORD dwCreationFlags, IN LPVOID lpEnvironment, IN LPCSTR lpCurrentDirectory, IN LPSTARTUPINFOA lpStartupInfo, OUT LPPROCESS_INFORMATION lpProcessInformation );
	virtual DWORD GetModuleFileNameA(HMODULE hModule, LPSTR lpFilename, DWORD nSize);
	
	//1
	virtual HANDLE CreateThread( IN LPSECURITY_ATTRIBUTES lpThreadAttributes, IN SIZE_T dwStackSize, IN LPTHREAD_START_ROUTINE lpStartAddress, IN LPVOID lpParameter, IN DWORD dwCreationFlags, OUT LPDWORD lpThreadId );
	//2
	virtual DWORD GetTickCount();
	//3
	virtual void Sleep( IN DWORD dwMilliseconds );
	//4
	virtual DWORD WaitForSingleObject(HANDLE hHandle, DWORD dwMilliseconds);
	//5
	virtual BOOL VirtualFree( IN LPVOID lpAddress, IN SIZE_T dwSize, IN DWORD dwFreeType );
	//6
	virtual BOOL SetEvent(HANDLE hEvent);
	//7
	virtual DWORD GetFileSize( IN HANDLE hFile, OUT LPDWORD lpFileSizeHigh );
	//8
	virtual HANDLE CreateFile( IN LPCSTR lpFileName, IN DWORD dwDesiredAccess, IN DWORD dwShareMode, IN LPSECURITY_ATTRIBUTES lpSecurityAttributes, IN DWORD dwCreationDisposition, IN DWORD dwFlagsAndAttributes, IN HANDLE hTemplateFile );
	//9
	virtual BOOL DeviceIoControl( IN HANDLE hDevice, IN DWORD dwIoControlCode, IN LPVOID lpInBuffer, IN DWORD nInBufferSize, OUT LPVOID lpOutBuffer, IN DWORD nOutBufferSize, OUT LPDWORD lpBytesReturned, IN LPOVERLAPPED lpOverlapped );
	//10
	virtual int MessageBoxA(IN HWND hWnd, IN LPCSTR lpText, IN LPCSTR lpCaption, IN UINT uType);
	//11
	virtual BOOL SetEnvironmentVariableA(LPCTSTR lpName, LPCTSTR lpValue);
	//12
	virtual DWORD GetEnvironmentVariableA( IN LPCSTR lpName, OUT LPSTR lpBuffer, IN DWORD nSize );	
	//13
	virtual DWORD GetCurrentProcessId();
    //14
	virtual LONG MapViewOfSection(HANDLE SectionHandle, HANDLE ProcessHandle, PVOID *BaseAddress, ULONG ZeroBits, ULONG CommitSize, PLARGE_INTEGER SectionOffset, PULONG ViewSize, ULONG InheritDisposition, ULONG AllocationType, ULONG Protect);
	//15
	virtual LONG ZwQueryInformationProcess(IN HANDLE ProcessHandle, IN ULONG ProcessInformationClass, OUT PVOID ProcessInformation, IN ULONG ProcessInformationLength, OUT PULONG ReturnLength); 
	//16
	virtual HANDLE CreateToolhelp32Snapshot(DWORD dwFlags, DWORD th32ProcessID);
	//17
	virtual BOOL Thread32First(HANDLE hSnapshot, LPVOID lpte);
	//18
	virtual BOOL Thread32Next( HANDLE hSnapshot, LPVOID lpte);
	//19
	virtual BOOL Process32First(HANDLE hSnapshot, LPVOID lppe);
	//20
	virtual BOOL Process32Next( HANDLE hSnapshot, LPVOID lppe );
	//21
	virtual BOOL TerminateProcess(HANDLE hProcess, UINT uExitCode);
	//22
	virtual DWORD ResumeThread( IN HANDLE hThread );
	//23
	virtual DWORD SuspendThread( IN HANDLE hThread );
	//24
	virtual LONG RegOpenKeyEx(HKEY hKey, LPCTSTR lpSubKey, DWORD ulOptions, REGSAM samDesired, PHKEY phkResult);
	//25
	virtual LONG RegQueryValueEx(HKEY hKey, LPCTSTR lpValueName, LPDWORD lpReserved, LPDWORD lpType, LPBYTE lpData, LPDWORD lpcbData);
	//26
	virtual LONG RegCloseKey(HKEY hKey);
public:
	BOOL InitMain(DWORD dwProcessId);
	BOOL InitSub(DWORD dwProcessId);
	void Uninit();
	static CApiWrapper * GetInstance();
	static BOOL WINAPI OnIntCall(DEBUG_EVENT &de);

private:
	BOOL m_bMain;
	HANDLE m_hMapping;
	LPVOID m_pView;
};
#define FAKE_API CApiWrapper::GetInstance()->
#else
#define FAKE_API
#endif

#if COMPONENT_PE_MGR
//////////////////////////////////////////////////////////////////////////
// PE������ע�����
// ��Ŀ�����ע��һ��DLL
extern "C" int WINAPI InjectDll(DWORD dwProcessId, LPCTSTR strDllName, DWORD dwThreadId = 0);

// ��Ŀ�����ע��һ���ڴ�dll�����uSize > 0��lpszFileָ��һ��buffer��uSizeΪ�䳤�ȣ����uSizeΪ0��lpszFileΪһ���ļ�·�������dwThreadIdΪ0�����Զ�Ѱ�Ҹý�����UI�̣߳�����ָ���߳�ע��
extern "C" int WINAPI InjectMemDll(DWORD dwProcessId, LPCSTR lpszFile, UINT uSize = 0, DWORD dwThreadId = 0);

// ��һ���ڴ�dll���ļ�ӳ������һ��wrapper��ֱ��call����pDst����ַ���ܼ������mem pe������ֵΪ������wrapper buffer����Ч����
extern "C" int WINAPI MakeMemPEWrapper(PBYTE pMemPE, UINT uSrcSize, PBYTE pDst, UINT uDstSize);

// �ڵ�CreateProcess֮ǰ���ã����ú��´ε���CreateProcess�����Ľ��̾ͻᱻע��һ��Mem dll��ע����뽫��PE���֮ǰִ�У���ʱCRT��δ��ʼ��
extern "C" int WINAPI PrepareMemDllForNextProcess(LPVOID pFile, UINT uSize);

class CPEMgr
{
public:
	enum
	{
		OPEN_FILE_READWRITE = 0,
		OPEN_FILE_READONLY,
		OPEN_MEM_FILE,
	};
	enum
	{
		TEST_CAN_INFECT = 0,
		TEST_FILE_IN_USE,
		TEST_ALREADY_INFECTED,
		TEST_UNKNOWN_ERROR,
	};
public:
	CPEMgr();
	~CPEMgr();
	BOOL LoadPE(LPCSTR lpszPath, DWORD dwOpenFlag = 0);
	BOOL LoadMemPE(LPVOID pFileBase);
	PIMAGE_DOS_HEADER GetImageDosHeader();
	PIMAGE_NT_HEADERS GetNtHeaders();
	DWORD GetEntryPoint(BOOL bImage = FALSE);
	PIMAGE_SECTION_HEADER GetSectionHeader(LPCSTR lpszSectionName = NULL);
	BOOL InjectDllByEntryPoint(LPCSTR lpszDllName);
	BOOL InjectPE(LPCSTR lpszVirusPeFile, DWORD dwSize = 0);
	// dest injected must have VirtualAlloc in import table, otherwise fail
	BOOL InjectPEWithoutNewSection(LPCSTR lpszVirusPeFile, DWORD dwSize = 0);
	BOOL AddSection(LPCSTR lpszSectionName, DWORD dwSize, DWORD dwCharacteristics);
	void Clear();
	DWORD GetAlignSize(DWORD dwSize, DWORD dwAlign);
	DWORD RVA2FileOffset(DWORD dwRVA);
	DWORD FileOffset2RVA(DWORD dwOffset);
	BOOL ResetCheckSum();
	BOOL AddFileSize(DWORD dwAdd);

	static LPVOID RunPE(LPCSTR lpszPath, DWORD dwSize = 0, HMODULE hHost = NULL);
	static void SimpleEncrypt(LPVOID pBuff, DWORD dwSize);
	static void SimpleDecrypt(LPVOID pBuff, DWORD dwSize);
	static LPVOID GetProcAddress(HMODULE hMod, LPCSTR lpszFuncName);
	// return entry point offset
	static DWORD CopyInjectPECode(LPVOID pDest, UINT &uSize);
	static int TestCanInjectWithoutSection(LPCSTR lpszFile);

protected:
	static int AsmFuncCenter(DWORD dwFuncId, LPVOID pParam = NULL, DWORD dwReserve1 = 0, DWORD dwReserve2 = 0);
private:
	BYTE * m_pBuff;
	HANDLE m_hFile;
	HANDLE m_hFileMapping;
	char m_szFilePath[260];
	BOOL m_dwOpenFlag;
};
#endif

//////////////////////////////////////////////////////////////////////////
// ɨ���������޸ı���
#if COMPONENT_SUPERVISOR
class CSupervisor
{
public:
	typedef struct
	{
		DWORD dwProcessId;
		HMODULE hMod;
		BYTE szCheckSum[16];
	}PROTECT_NODE;
	typedef struct  
	{
		DWORD dwSupervisorId;
		DWORD dwCount;
		PROTECT_NODE node[1];
	}PROTECT_LIST;
public:
	CSupervisor();
	~CSupervisor();
	int Init();
	void Clear();
	BOOL AddProtectNode(HMODULE hMod, BYTE szCheckSum[16]);
	BOOL DelProtectNode(HMODULE hMod);
	BOOL DoScan();
	BOOL InstallSupervisor(LPCSTR lpszPath, UINT uSize = 0, DWORD dwProcessId = 0);
	BOOL EnumProtectList(LPTHREAD_START_ROUTINE pFunc);
	BOOL ScanTextSection(HANDLE hProcess, HMODULE hMod, BYTE szCheckSum[]);
	BOOL ScanMemBp(DWORD dwProcessId);
	BOOL ScanDebugger(HANDLE hProcess);
	void FoundException(HANDLE hProcess);	
protected:
	static DWORD WINAPI _ThreadScan(LPVOID pParam);
	BOOL ScanList();
	BOOL SetSupervisor(DWORD dwProcessId);
	DWORD GetSupervisor();
    	
private:
	HANDLE m_hFileMapping;
	LPVOID m_pView;
	HANDLE m_hLock;
};
#endif	// #if COMPONENT_SUPERVISOR

enum
{
	INT_API_CALL = 0x3188,
	INT_SELF_CALL = 0x3189,
};


typedef struct tagReserveDllMainData
{
	LPVOID hHost;		// �����ߵ�������ַ
	LPVOID pBaseFile;	// mempe���ڴ��ļ���ַ
	DWORD dwFileSize;	// mempe���ڴ��ļ���С
}RESERVE_DLL_MAIN_DATA;