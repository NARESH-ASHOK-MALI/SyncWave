using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using NAudio.Wave;
using SyncWave.Utils;

namespace SyncWave.Core
{
    /// <summary>
    /// Captures whole-system audio via Windows Process Loopback (upstream of master volume),
    /// excluding SyncWave's own process tree to prevent self-feedback.
    /// Provides a drop-in interface matching AudioCaptureService.
    /// </summary>
    public class ProcessLoopbackCaptureService : ICaptureService
    {
        private readonly object _lock = new();
        private Thread? _captureThread;
        private volatile bool _isCapturing;

        private IAudioClient? _audioClient;
        private IAudioCaptureClient? _captureClient;
        private WaveFormat? _captureFormat;

        #region Enums & Structs

        private enum AUDIOCLIENT_ACTIVATION_TYPE
        {
            DEFAULT = 0,
            PROCESS_LOOPBACK = 1,
        }

        private enum PROCESS_LOOPBACK_MODE
        {
            INCLUDE_TARGET_PROCESS_TREE = 0,
            EXCLUDE_TARGET_PROCESS_TREE = 1,
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct AUDIOCLIENT_PROCESS_LOOPBACK_PARAMS
        {
            public uint TargetProcessId;
            public PROCESS_LOOPBACK_MODE ProcessLoopbackMode;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct AUDIOCLIENT_ACTIVATION_PARAMS
        {
            public AUDIOCLIENT_ACTIVATION_TYPE ActivationType;
            public AUDIOCLIENT_PROCESS_LOOPBACK_PARAMS ProcessLoopbackParams;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct PropVariantBlob
        {
            public ushort vt;           // VT_BLOB = 0x0041
            public ushort wReserved1;
            public ushort wReserved2;
            public ushort wReserved3;
            public uint cbSize;
            public uint _pad;
            public IntPtr pBlobData;
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        private struct WAVEFORMATEX
        {
            public ushort wFormatTag;
            public ushort nChannels;
            public uint nSamplesPerSec;
            public uint nAvgBytesPerSec;
            public ushort nBlockAlign;
            public ushort wBitsPerSample;
            public ushort cbSize;
        }

        #endregion

        #region COM Interfaces

        [ComImport, Guid("41D949AB-9862-444A-80F6-C261334DA5EB")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IActivateAudioInterfaceCompletionHandler
        {
            void ActivateCompleted(IActivateAudioInterfaceAsyncOperation activateOperation);
        }

        [ComImport, Guid("72A22D78-CDE4-431D-B8CC-843A71199B6D")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IActivateAudioInterfaceAsyncOperation
        {
            void GetActivateResult(out int activateResult,
                [MarshalAs(UnmanagedType.IUnknown)] out object activatedInterface);
        }

        [ComImport, Guid("1CB9AD4C-DBFA-4c32-B178-C2F568A703B2")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IAudioClient
        {
            [PreserveSig] int Initialize(int ShareMode, uint StreamFlags,
                long hnsBufferDuration, long hnsPeriodicity, IntPtr pFormat, IntPtr AudioSessionGuid);
            [PreserveSig] int GetBufferSize(out uint pNumBufferFrames);
            [PreserveSig] int GetStreamLatency(out long phnsLatency);
            [PreserveSig] int GetCurrentPadding(out uint pNumPaddingFrames);
            [PreserveSig] int IsFormatSupported(int ShareMode, IntPtr pFormat, out IntPtr ppClosestMatch);
            [PreserveSig] int GetMixFormat(out IntPtr ppDeviceFormat);
            [PreserveSig] int GetDevicePeriod(out long phnsDefault, out long phnsMinimum);
            [PreserveSig] int Start();
            [PreserveSig] int Stop();
            [PreserveSig] int Reset();
            [PreserveSig] int SetEventHandle(IntPtr eventHandle);
            [PreserveSig] int GetService(ref Guid riid, [MarshalAs(UnmanagedType.IUnknown)] out object ppv);
        }

        [ComImport, Guid("C8ADBD64-E71E-48a0-A4DE-185C395CD317")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IAudioCaptureClient
        {
            [PreserveSig] int GetBuffer(out IntPtr ppData, out uint pNumFramesToRead,
                out uint pdwFlags, out ulong pu64DevicePosition, out ulong pu64QPCPosition);
            [PreserveSig] int ReleaseBuffer(uint NumFramesRead);
            [PreserveSig] int GetNextPacketSize(out uint pNumFramesInNextPacket);
        }

        [ComImport, Guid("94EA2B94-E9CC-49E0-C0FF-EE64CA8F5B90")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IAgileObject { }

        #endregion

        #region Native Methods

        private static class NativeMethods
        {
            [DllImport("mmdevapi.dll", PreserveSig = false)]
            public static extern void ActivateAudioInterfaceAsync(
                [MarshalAs(UnmanagedType.LPWStr)] string deviceInterfacePath,
                [MarshalAs(UnmanagedType.LPStruct)] Guid riid,
                ref PropVariantBlob activationParams,
                IActivateAudioInterfaceCompletionHandler completionHandler,
                out IActivateAudioInterfaceAsyncOperation activationOperation);
        }

        #endregion

        #region Completion Handler

        [ComVisible(true)]
        private class ActivationHandler : IActivateAudioInterfaceCompletionHandler, IAgileObject
        {
            private readonly ManualResetEventSlim _event = new(false);
            public int HResult { get; private set; }
            public object? ActivatedInterface { get; private set; }

            public void ActivateCompleted(IActivateAudioInterfaceAsyncOperation activateOperation)
            {
                try
                {
                    activateOperation.GetActivateResult(out int hr, out object iface);
                    HResult = hr;
                    ActivatedInterface = iface;
                }
                catch (Exception ex)
                {
                    HResult = Marshal.GetHRForException(ex);
                }
                finally
                {
                    _event.Set();
                }
            }

            public bool Wait(int timeoutMs = 10000) => _event.Wait(timeoutMs);
        }

        #endregion

        private static readonly Guid IID_IAudioClient = new("1CB9AD4C-DBFA-4c32-B178-C2F568A703B2");
        private static readonly Guid IID_IAudioCaptureClient = new("C8ADBD64-E71E-48a0-A4DE-185C395CD317");

        private const uint AUDCLNT_STREAMFLAGS_LOOPBACK = 0x00020000;
        private const uint AUDCLNT_STREAMFLAGS_AUTOCONVERTPCM = 0x80000000;
        private const uint AUDCLNT_STREAMFLAGS_EVENTCALLBACK = 0x00040000;

        /// <summary>Fired when new audio data is captured. Provides buffer + byte count.</summary>
        public event Action<byte[], int>? DataAvailable;

        /// <summary>Fired when the capture format is determined.</summary>
        public event Action<WaveFormat>? CaptureFormatAvailable;

        /// <summary>Current capture state.</summary>
        public bool IsCapturing => _isCapturing;

        /// <summary>The active capture wave format (set after Start).</summary>
        /// <summary>The active capture wave format (set after Start).</summary>
        public WaveFormat? CaptureFormat => _captureFormat;

        public bool IsHighPerformanceModeEnabled { get; set; }
        private EventWaitHandle? _eventWaitHandle;

        /// <summary>
        /// Starts Windows Process Loopback capture, excluding SyncWave's own process tree.
        /// Blocks synchronously for up to 1000ms while waiting for async COM interface activation.
        /// </summary>
        public void Start()
        {
            lock (_lock)
            {
                if (_isCapturing)
                {
                    Logger.Warn("Process Loopback Capture already running — ignoring Start().");
                    return;
                }

                try
                {
                    int myPid = Environment.ProcessId;
                    Logger.Info($"[Interop] Start() called on thread {Thread.CurrentThread.ManagedThreadId} (apartment={Thread.CurrentThread.GetApartmentState()}), PID={myPid}");

                    // Run all COM activation on an MTA threadpool thread.
                    // The WPF UI thread is STA, which causes E_NOINTERFACE when the CLR
                    // creates RCWs for our privately-defined COM interfaces. The same code
                    // works perfectly from the console smoke test (MTA by default).
                    var task = Task.Run(() =>
                    {
                        Logger.Info($"[Interop] COM init running on thread {Thread.CurrentThread.ManagedThreadId} (apartment={Thread.CurrentThread.GetApartmentState()})");

                        IntPtr pParams = IntPtr.Zero;
                        try
                        {
                            var activationParams = new AUDIOCLIENT_ACTIVATION_PARAMS
                            {
                                ActivationType = AUDIOCLIENT_ACTIVATION_TYPE.PROCESS_LOOPBACK,
                                ProcessLoopbackParams = new AUDIOCLIENT_PROCESS_LOOPBACK_PARAMS
                                {
                                    TargetProcessId = (uint)myPid,
                                    ProcessLoopbackMode = PROCESS_LOOPBACK_MODE.EXCLUDE_TARGET_PROCESS_TREE,
                                }
                            };

                            int paramSize = Marshal.SizeOf<AUDIOCLIENT_ACTIVATION_PARAMS>();
                            pParams = Marshal.AllocCoTaskMem(paramSize);
                            Marshal.StructureToPtr(activationParams, pParams, false);

                            var propVariant = new PropVariantBlob
                            {
                                vt = 0x0041, // VT_BLOB
                                cbSize = (uint)paramSize,
                                pBlobData = pParams,
                            };

                            Logger.Info("[Interop] Calling ActivateAudioInterfaceAsync...");
                            var handler = new ActivationHandler();
                            NativeMethods.ActivateAudioInterfaceAsync(
                                "VAD\\Process_Loopback",
                                IID_IAudioClient,
                                ref propVariant,
                                handler,
                                out _);

                            bool waited = handler.Wait(5000);
                            Logger.Info($"[Interop] ActivateAudioInterfaceAsync completed: waited={waited}, HR=0x{handler.HResult:X8}, hasInterface={handler.ActivatedInterface != null}");

                            if (!waited || handler.HResult < 0)
                            {
                                throw new COMException(
                                    $"Process loopback activation failed or timed out with HR 0x{handler.HResult:X8}.",
                                    handler.HResult);
                            }

                            // ── Obtain IAudioClient via manual QueryInterface ──
                            Logger.Info("[Interop] Getting IUnknown for activated interface...");
                            IntPtr pUnk = Marshal.GetIUnknownForObject(handler.ActivatedInterface!);
                            Logger.Info($"[Interop] IUnknown ptr = 0x{pUnk:X}");
                            try
                            {
                                Guid iidAudioClient = IID_IAudioClient;
                                int hrQI = Marshal.QueryInterface(pUnk, ref iidAudioClient, out IntPtr pAudioClient);
                                Logger.Info($"[Interop] QueryInterface for IAudioClient: HR=0x{hrQI:X8}, ptr=0x{pAudioClient:X}");
                                Marshal.ThrowExceptionForHR(hrQI);

                                Logger.Info("[Interop] Wrapping IAudioClient ptr via GetObjectForIUnknown...");
                                _audioClient = (IAudioClient)Marshal.GetObjectForIUnknown(pAudioClient);
                                Marshal.Release(pAudioClient);
                                Logger.Info("[Interop] IAudioClient acquired successfully.");
                            }
                            finally
                            {
                                Marshal.Release(pUnk);
                            }

                            // ── Initialize IAudioClient ──
                            var nativeFormat = new WAVEFORMATEX
                            {
                                wFormatTag = 3, // WAVE_FORMAT_IEEE_FLOAT
                                nChannels = 2,
                                nSamplesPerSec = 48000,
                                wBitsPerSample = 32,
                                nBlockAlign = 8,
                                nAvgBytesPerSec = 48000 * 8,
                                cbSize = 0,
                            };

                            int fmtSize = Marshal.SizeOf<WAVEFORMATEX>();
                            IntPtr pFmt = Marshal.AllocCoTaskMem(fmtSize);
                            try
                            {
                                Marshal.StructureToPtr(nativeFormat, pFmt, false);
                                uint streamFlags = AUDCLNT_STREAMFLAGS_LOOPBACK | AUDCLNT_STREAMFLAGS_AUTOCONVERTPCM;
                                if (IsHighPerformanceModeEnabled)
                                {
                                    streamFlags |= AUDCLNT_STREAMFLAGS_EVENTCALLBACK;
                                }

                                Logger.Info($"[Interop] Calling IAudioClient.Initialize(shareMode=0, flags=0x{streamFlags:X8})...");
                                int hrInit = _audioClient.Initialize(0, streamFlags, 0, 0, pFmt, IntPtr.Zero);
                                Logger.Info($"[Interop] IAudioClient.Initialize returned HR=0x{hrInit:X8}");
                                Marshal.ThrowExceptionForHR(hrInit);

                                if (IsHighPerformanceModeEnabled)
                                {
                                    _eventWaitHandle = new EventWaitHandle(false, EventResetMode.AutoReset);
                                    int hrEvent = _audioClient.SetEventHandle(_eventWaitHandle.SafeWaitHandle.DangerousGetHandle());
                                    Logger.Info($"[Interop] IAudioClient.SetEventHandle returned HR=0x{hrEvent:X8}");
                                    Marshal.ThrowExceptionForHR(hrEvent);
                                }
                            }
                            finally
                            {
                                Marshal.FreeCoTaskMem(pFmt);
                            }

                            // ── Obtain IAudioCaptureClient via manual QueryInterface ──
                            Logger.Info("[Interop] Calling IAudioClient.GetService for IAudioCaptureClient...");
                            Guid captureGuid = IID_IAudioCaptureClient;
                            int hrGetService = _audioClient.GetService(ref captureGuid, out object captureObj);
                            Logger.Info($"[Interop] GetService returned HR=0x{hrGetService:X8}, hasObj={captureObj != null}");
                            Marshal.ThrowExceptionForHR(hrGetService);

                            Logger.Info("[Interop] Getting IUnknown for captureObj...");
                            IntPtr pCaptureUnk = Marshal.GetIUnknownForObject(captureObj!);
                            Logger.Info($"[Interop] CaptureClient IUnknown ptr = 0x{pCaptureUnk:X}");
                            try
                            {
                                Guid iidCapture = IID_IAudioCaptureClient;
                                int hrCaptureQI = Marshal.QueryInterface(pCaptureUnk, ref iidCapture, out IntPtr pCaptureClient);
                                Logger.Info($"[Interop] QueryInterface for IAudioCaptureClient: HR=0x{hrCaptureQI:X8}, ptr=0x{pCaptureClient:X}");
                                Marshal.ThrowExceptionForHR(hrCaptureQI);

                                Logger.Info("[Interop] Wrapping IAudioCaptureClient ptr via GetObjectForIUnknown...");
                                _captureClient = (IAudioCaptureClient)Marshal.GetObjectForIUnknown(pCaptureClient);
                                Marshal.Release(pCaptureClient);
                                Logger.Info("[Interop] IAudioCaptureClient acquired successfully.");
                            }
                            finally
                            {
                                Marshal.Release(pCaptureUnk);
                            }

                            // ── Start the audio client ──
                            Logger.Info("[Interop] Calling IAudioClient.Start()...");
                            int hrStart = _audioClient.Start();
                            Logger.Info($"[Interop] IAudioClient.Start returned HR=0x{hrStart:X8}");
                            Marshal.ThrowExceptionForHR(hrStart);

                            Logger.Info("[Interop] All COM initialization completed successfully on MTA thread.");
                        }
                        finally
                        {
                            if (pParams != IntPtr.Zero)
                            {
                                Marshal.FreeCoTaskMem(pParams);
                            }
                        }
                    });

                    // Block the calling thread until COM init completes on MTA thread.
                    // Propagates any exception from the Task.
                    task.GetAwaiter().GetResult();

                    // NAudio WaveFormat instance for consumers (safe on any thread)
                    _captureFormat = WaveFormat.CreateIeeeFloatWaveFormat(48000, 2);

                    _isCapturing = true;

                    // Start background capture thread
                    _captureThread = new Thread(CaptureLoop)
                    {
                        Name = "SyncWave_ProcessLoopbackCaptureThread",
                        IsBackground = true
                    };
                    _captureThread.Start();

                    CaptureFormatAvailable?.Invoke(_captureFormat);
                    Logger.Info($"[Interop] Process Loopback capture started successfully ({_captureFormat.SampleRate}Hz, {_captureFormat.BitsPerSample}-bit float, {_captureFormat.Channels}ch, PID: {myPid} excluded).");
                }
                catch (Exception ex)
                {
                    Logger.Error("Failed to start Process Loopback audio capture", ex);
                    Cleanup();
                    throw;
                }
            }
        }

        private const uint AUDCLNT_BUFFERFLAGS_SILENT = 0x00000001;

        private void CaptureLoop()
        {
            WaitHandle[]? waitHandles = null;
            if (IsHighPerformanceModeEnabled && _eventWaitHandle != null)
            {
                waitHandles = new WaitHandle[] { _eventWaitHandle };
            }

            while (_isCapturing && _captureClient != null)
            {
                if (IsHighPerformanceModeEnabled && waitHandles != null)
                {
                    WaitHandle.WaitAny(waitHandles, 100);
                    if (!_isCapturing) break;
                }

                try
                {
                    while (_isCapturing)
                    {
                        int hr = _captureClient.GetNextPacketSize(out uint packetSize);
                        if (hr < 0 || packetSize == 0)
                        {
                            break;
                        }

                        hr = _captureClient.GetBuffer(
                            out IntPtr pData,
                            out uint numFrames,
                            out uint flags,
                            out _,
                            out _);

                        if (hr < 0)
                        {
                            break;
                        }

                        if (numFrames > 0)
                        {
                            int bytesRecorded = (int)numFrames * 8; // 2 channels * 4 bytes/sample (32-bit float)
                            var buffer = new byte[bytesRecorded];

                            // If silent buffer flag is set or pData is null, leave buffer zeroed (silence)
                            if ((flags & AUDCLNT_BUFFERFLAGS_SILENT) == 0 && pData != IntPtr.Zero)
                            {
                                Marshal.Copy(pData, buffer, 0, bytesRecorded);
                            }

                            DataAvailable?.Invoke(buffer, bytesRecorded);
                        }

                        _captureClient.ReleaseBuffer(numFrames);
                    }
                }
                catch (Exception ex)
                {
                    if (_isCapturing)
                    {
                        Logger.Error("Error reading capture buffer in Process Loopback thread", ex);
                    }
                }

                if (!IsHighPerformanceModeEnabled)
                {
                    Thread.Sleep(5);
                }
            }
        }

        /// <summary>
        /// Stops process loopback capture gracefully.
        /// </summary>
        public void Stop()
        {
            lock (_lock)
            {
                if (!_isCapturing)
                    return;

                try
                {
                    _isCapturing = false;
                    
                    if (_captureThread != null && _captureThread.IsAlive)
                    {
                        _captureThread.Join(500);
                        _captureThread = null;
                    }

                    // Perform COM teardown on an MTA thread context to avoid STA/MTA RCW apartment errors
                    Task.Run(() =>
                    {
                        try
                        {
                            if (_audioClient != null)
                            {
                                _audioClient.Stop();
                                _audioClient.Reset();
                            }
                        }
                        catch (Exception ex)
                        {
                            Logger.Warn($"Non-critical error stopping audio client: {ex.Message}");
                        }
                        finally
                        {
                            CleanupComObjects();
                        }
                    }).GetAwaiter().GetResult();

                    Logger.Info("Process Loopback capture stopped.");
                }
                catch (Exception ex)
                {
                    Logger.Error("Error stopping Process Loopback capture", ex);
                }
            }
        }

        private void CleanupComObjects()
        {
            _isCapturing = false;

            if (_captureClient != null)
            {
                try { Marshal.ReleaseComObject(_captureClient); } catch { }
                _captureClient = null;
            }

            if (_audioClient != null)
            {
                try { Marshal.ReleaseComObject(_audioClient); } catch { }
                _audioClient = null;
            }

            if (_eventWaitHandle != null)
            {
                try { _eventWaitHandle.Dispose(); } catch { }
                _eventWaitHandle = null;
            }

            _captureFormat = null;
        }

        private void Cleanup()
        {
            CleanupComObjects();
        }

        public void Dispose()
        {
            Stop();
            Cleanup();
            GC.SuppressFinalize(this);
        }
    }
}
