param([Parameter(Mandatory=$true)][string]$RequestFile)
$ErrorActionPreference = 'Stop'
$self = [Diagnostics.Process]::GetCurrentProcess()
$self.PriorityClass = [Diagnostics.ProcessPriorityClass]::Idle
$mask = $self.ProcessorAffinity.ToInt64()
$oneCore = 1L
while (($mask -shr 1) -gt 0) { $mask = $mask -shr 1; $oneCore = $oneCore -shl 1 }
$self.ProcessorAffinity = [IntPtr]$oneCore
$request = Get-Content -LiteralPath $RequestFile -Raw | ConvertFrom-Json
$env:DOTNET_PROCESSOR_COUNT = '1'
$env:DOTNET_GCHeapHardLimit = '0x20000000'
$env:MSBUILDDISABLENODEREUSE = '1'
if ($request.environment) {
    foreach ($item in $request.environment.PSObject.Properties) { [Environment]::SetEnvironmentVariable($item.Name, [string]$item.Value, 'Process') }
}
Add-Type -TypeDefinition @'
using System;
using System.Text;
using System.IO;
using System.Diagnostics;
using System.ComponentModel;
using System.Runtime.InteropServices;
public static class ConstrainedProcess {
    [StructLayout(LayoutKind.Sequential)] struct BasicLimits {
        public long ProcessTime, JobTime; public uint Flags; public UIntPtr MinWorkingSet, MaxWorkingSet;
        public uint ActiveProcesses; public UIntPtr Affinity; public uint Priority, SchedulingClass;
    }
    [StructLayout(LayoutKind.Sequential)] struct IoCounters { public ulong A,B,C,D,E,F; }
    [StructLayout(LayoutKind.Sequential)] struct Limits {
        public BasicLimits Basic; public IoCounters Io;
        public UIntPtr ProcessMemory, JobMemory, PeakProcessMemory, PeakJobMemory;
    }
    [StructLayout(LayoutKind.Sequential)] struct CpuRate { public uint Flags, Rate; }
    [StructLayout(LayoutKind.Sequential, CharSet=CharSet.Unicode)] struct Startup {
        public uint Size; public string Reserved, Desktop, Title;
        public uint X,Y,XSize,YSize,XChars,YChars,Fill,Flags; public ushort Show,ReservedSize;
        public IntPtr ReservedBytes, Input, Output, Error;
    }
    [StructLayout(LayoutKind.Sequential)] struct ProcessInfo { public IntPtr Process,Thread; public uint ProcessId,ThreadId; }
    [DllImport("kernel32",SetLastError=true)] static extern IntPtr CreateJobObject(IntPtr a, string name);
    [DllImport("kernel32",SetLastError=true)] static extern bool SetInformationJobObject(IntPtr job, int type, IntPtr data, uint length);
    [DllImport("kernel32",SetLastError=true)] static extern bool QueryInformationJobObject(IntPtr job, int type, out Limits data, uint length, IntPtr returned);
    [DllImport("kernel32",SetLastError=true)] static extern bool AssignProcessToJobObject(IntPtr job, IntPtr process);
    [DllImport("kernel32",SetLastError=true)] static extern bool SetHandleInformation(IntPtr handle, uint mask, uint flags);
    [DllImport("kernel32",SetLastError=true,CharSet=CharSet.Unicode)] static extern bool CreateProcess(string file, StringBuilder args, IntPtr pa, IntPtr ta, bool inherit, uint flags, IntPtr env, string cwd, ref Startup si, out ProcessInfo pi);
    [DllImport("kernel32",SetLastError=true)] static extern uint ResumeThread(IntPtr thread);
    [DllImport("kernel32",SetLastError=true)] static extern uint WaitForSingleObject(IntPtr handle, uint ms);
    [DllImport("kernel32",SetLastError=true)] static extern bool GetExitCodeProcess(IntPtr process, out uint code);
    [DllImport("kernel32",SetLastError=true)] static extern bool TerminateProcess(IntPtr process, uint code);
    [DllImport("kernel32",SetLastError=true)] static extern bool TerminateJobObject(IntPtr job, uint code);
    [DllImport("kernel32")] static extern bool CloseHandle(IntPtr handle);
    static void Check(bool ok) { if (!ok) throw new Win32Exception(Marshal.GetLastWin32Error()); }
    static void Set<T>(IntPtr job,int type,T value) {
        int size=Marshal.SizeOf(typeof(T)); IntPtr p=Marshal.AllocHGlobal(size);
        try { Marshal.StructureToPtr(value,p,false); Check(SetInformationJobObject(job,type,p,(uint)size)); }
        finally { Marshal.FreeHGlobal(p); }
    }
    static string Quote(string value) {
        var s=new StringBuilder("\""); int slashes=0;
        foreach(char c in value) {
            if(c=='\\') { slashes++; continue; }
            s.Append('\\', c=='"' ? slashes*2+1 : slashes); slashes=0; s.Append(c);
        }
        return s.Append('\\',slashes*2).Append('"').ToString();
    }
    public static int Run(string file,string[] args,string cwd,string log,int seconds,int cpuPercent,long memoryBytes,long affinity) {
        if(cpuPercent<1 || cpuPercent>10 || seconds<1) throw new ArgumentOutOfRangeException();
        var job=CreateJobObject(IntPtr.Zero,null); if(job==IntPtr.Zero) throw new Win32Exception();
        ProcessInfo pi=new ProcessInfo(); var watch=Stopwatch.StartNew(); bool timedOut=false;
        try {
            var limits=new Limits(); limits.Basic.Flags=0x2000|0x200|0x10|0x20;
            limits.Basic.Affinity=(UIntPtr)(ulong)affinity; limits.Basic.Priority=0x40;
            limits.JobMemory=(UIntPtr)(ulong)memoryBytes; Set(job,9,limits);
            Set(job,15,new CpuRate { Flags=1|4, Rate=(uint)(cpuPercent*100) });
            using(var output=new FileStream(log,FileMode.Create,FileAccess.Write,FileShare.ReadWrite))
            using(var error=new FileStream(log+".err",FileMode.Create,FileAccess.Write,FileShare.ReadWrite))
            using(var input=new FileStream(log+".stdin",FileMode.Create,FileAccess.ReadWrite,FileShare.ReadWrite)) {
                IntPtr o=output.SafeFileHandle.DangerousGetHandle(), e=error.SafeFileHandle.DangerousGetHandle(), i=input.SafeFileHandle.DangerousGetHandle();
                Check(SetHandleInformation(o,1,1)); Check(SetHandleInformation(e,1,1)); Check(SetHandleInformation(i,1,1));
                var start=new Startup { Size=(uint)Marshal.SizeOf(typeof(Startup)),Flags=0x101,Show=0,Input=i,Output=o,Error=e };
                var command=new StringBuilder(Quote(file)); foreach(var a in args) command.Append(' ').Append(Quote(a));
                Check(CreateProcess(file,command,IntPtr.Zero,IntPtr.Zero,true,0x08000000|4|0x40,IntPtr.Zero,cwd,ref start,out pi));
                if(!AssignProcessToJobObject(job,pi.Process)) { int failure=Marshal.GetLastWin32Error(); TerminateProcess(pi.Process,125); throw new Win32Exception(failure); }
                if(ResumeThread(pi.Thread)==uint.MaxValue) throw new Win32Exception();
                while(WaitForSingleObject(pi.Process,1000)==258) {
                    if(watch.Elapsed.TotalSeconds>seconds) { timedOut=true; Check(TerminateJobObject(job,124)); break; }
                }
                WaitForSingleObject(pi.Process,5000); uint code; Check(GetExitCodeProcess(pi.Process,out code));
                Limits result; Check(QueryInformationJobObject(job,9,out result,(uint)Marshal.SizeOf(typeof(Limits)),IntPtr.Zero));
                var summary=String.Format("pid={0}, exit={1}, seconds={2:F1}, peakJobMiB={3:F1}, cpuCapPercent={4}, logicalCores=1, priority=Idle, timedOut={5}",pi.ProcessId,code,watch.Elapsed.TotalSeconds,result.PeakJobMemory.ToUInt64()/1048576.0,cpuPercent,timedOut);
                File.WriteAllText(log+".resources.txt",summary+Environment.NewLine); Console.WriteLine(summary);
                return timedOut ? 124 : unchecked((int)code);
            }
        } finally { if(pi.Thread!=IntPtr.Zero) CloseHandle(pi.Thread); if(pi.Process!=IntPtr.Zero) CloseHandle(pi.Process); CloseHandle(job); }
    }
}
'@
$file = (Get-Command $request.file -ErrorAction Stop).Source
$seconds = if ($request.timeoutSeconds) { [int]$request.timeoutSeconds } else { 1800 }
$cpu = if ($request.cpuPercent) { [int]$request.cpuPercent } else { 5 }
$memory = if ($request.memoryMiB) { [long]$request.memoryMiB * 1MB } else { 1536MB }
$exitCode = [ConstrainedProcess]::Run($file,[string[]]$request.arguments,[string]$request.workingDirectory,[string]$request.log,$seconds,$cpu,$memory,$oneCore)
Get-Content -LiteralPath $request.log -Tail 12
if (Test-Path -LiteralPath ($request.log + '.err')) { Get-Content -LiteralPath ($request.log + '.err') -Tail 6 }
exit $exitCode
