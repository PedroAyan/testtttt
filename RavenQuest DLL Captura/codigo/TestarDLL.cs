using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

// Valida somente a janela criada por este executavel. Nao acessa RavenQuest.
internal static class Program
{
    private const uint WdaNone = 0;
    private const uint WdaMonitor = 1;
    private const uint UnknownAffinity = UInt32.MaxValue;
    private const uint LoadLibrarySearchDllLoadDir = 0x00000100;
    private const uint LoadLibrarySearchSystem32 = 0x00000800;
    private const uint GaRoot = 2;

    [StructLayout(LayoutKind.Sequential)]
    private struct CaptureRestoreResult
    {
        public uint size;
        public uint version;
        public uint status;
        public uint caller_pid;
        public uint owner_pid;
        public uint before_affinity;
        public uint after_affinity;
        public uint before_read_ok;
        public uint set_ok;
        public uint after_read_ok;
        public uint win32_error;
        public uint reserved;
    }

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate uint RestoreCaptureDelegate(IntPtr hwnd, ref CaptureRestoreResult result);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr LoadLibraryEx(string fileName, IntPtr file, uint flags);

    [DllImport("kernel32.dll", CharSet = CharSet.Ansi, ExactSpelling = true, SetLastError = true)]
    private static extern IntPtr GetProcAddress(IntPtr module, string name);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool FreeLibrary(IntPtr module);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowDisplayAffinity(IntPtr hwnd, uint affinity);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowDisplayAffinity(IntPtr hwnd, out uint affinity);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);

    [DllImport("user32.dll")]
    private static extern IntPtr GetAncestor(IntPtr hwnd, uint flags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindow(IntPtr hwnd);

    [DllImport("dwmapi.dll")]
    private static extern int DwmIsCompositionEnabled([MarshalAs(UnmanagedType.Bool)] out bool enabled);

    private sealed class Step
    {
        public string name;
        public string time;
        public bool passed;
        public bool setAttempted;
        public bool setOk;
        public int setError;
        public uint expectedAffinity;
        public bool readOk;
        public uint observedAffinity = UnknownAffinity;
        public int readError;
        public bool dllCalled;
        public uint dllReturn;
        public CaptureRestoreResult native;
    }

    private static readonly List<Step> Steps = new List<Step>();
    private static readonly uint CurrentPid = (uint)Process.GetCurrentProcess().Id;
    private static string Started;
    private static string DllPath;
    private static string LogPath;
    private static string FatalError;
    private static int LoadError;
    private static IntPtr WindowHandle;
    private static uint WindowOwner;
    private static bool IsTopLevel;
    private static bool DwmEnabled;
    private static int DwmResult;
    private static bool TestCompleted;
    private static bool TestPassed;
    private static bool CleanupPassed;
    private static bool LibraryFreed;
    private static RestoreCaptureDelegate Restore;

    [STAThread]
    private static int Main(string[] args)
    {
        bool selfTest = Array.Exists(args, delegate(string item) { return item == "/self-test"; });
        Started = Now();
        DllPath = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "CaptureRestore.dll"));
        LogPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "resultado_teste.json");
        IntPtr library = IntPtr.Zero;
        Form form = null;
        System.Windows.Forms.Timer timer = null;
        try
        {
            if (IntPtr.Size != 8)
                throw new InvalidOperationException("Este host e a DLL precisam executar em x64.");
            if (Marshal.SizeOf(typeof(CaptureRestoreResult)) != 48)
                throw new InvalidOperationException("O contrato da estrutura deve ter 48 bytes.");
            library = LoadLibraryEx(DllPath, IntPtr.Zero,
                LoadLibrarySearchDllLoadDir | LoadLibrarySearchSystem32);
            if (library == IntPtr.Zero)
            {
                LoadError = Marshal.GetLastWin32Error();
                throw new Win32Exception(LoadError, "Nao foi possivel carregar " + DllPath);
            }
            IntPtr entry = GetProcAddress(library, "RestoreCaptureForOwnedWindow");
            if (entry == IntPtr.Zero)
            {
                LoadError = Marshal.GetLastWin32Error();
                throw new Win32Exception(LoadError, "Export RestoreCaptureForOwnedWindow nao encontrado.");
            }
            Restore = (RestoreCaptureDelegate)Marshal.GetDelegateForFunctionPointer(entry,
                typeof(RestoreCaptureDelegate));
            DwmResult = DwmIsCompositionEnabled(out DwmEnabled);

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            form = new Form();
            form.Text = "Teste da DLL - janela propria";
            form.StartPosition = FormStartPosition.CenterScreen;
            form.ClientSize = new Size(510, 170);
            form.FormBorderStyle = FormBorderStyle.FixedDialog;
            form.MaximizeBox = false;
            Label label = new Label();
            label.Location = new Point(16, 16);
            label.Size = new Size(475, 95);
            label.Text = "Esta janela pertence somente ao teste.\r\n" +
                "A DLL sera chamada aqui para restaurar a captura.\r\n" +
                "O teste verifica os valores da API; nao tira prints.";
            form.Controls.Add(label);
            Button start = new Button();
            start.Text = "Executar teste da DLL";
            start.Location = new Point(16, 125);
            start.Size = new Size(185, 30);
            form.Controls.Add(start);

            timer = new System.Windows.Forms.Timer();
            timer.Interval = 300;
            int phase = 0;
            Action startTest = delegate
            {
                start.Enabled = false;
                phase = 0;
                timer.Start();
                label.Text = "Executando o teste somente nesta janela...\r\n" +
                    "1. Ativar 0x1\r\n2. Restaurar pela DLL para 0x0\r\n3. Repetir a chamada";
            };
            Action finish = delegate
            {
                timer.Stop();
                TestCompleted = true;
                TestPassed = AllStepsPassed();
                label.Text = TestPassed ? "Valores da API confirmados: 0x1 -> 0x0 -> 0x0."
                    : "O teste falhou. Consulte resultado_teste.json.";
                if (selfTest) form.Close();
            };
            timer.Tick += delegate
            {
                try
                {
                    if (!OwnWindowStillValid())
                        throw new InvalidOperationException("A janela de teste deixou de ser valida ou propria.");
                    if (phase == 0)
                    {
                        Steps.Add(SetAndRead("ativar_bloqueio", WdaMonitor));
                        if (!Steps[Steps.Count - 1].passed) { finish(); return; }
                    }
                    else if (phase == 1)
                    {
                        Steps.Add(CallDllAndRead("restaurar_pela_dll", WdaMonitor));
                        if (!Steps[Steps.Count - 1].passed) { finish(); return; }
                    }
                    else if (phase == 2)
                    {
                        Steps.Add(CallDllAndRead("repetir_restauracao", WdaNone));
                        finish();
                        return;
                    }
                    phase++;
                }
                catch (Exception ex)
                {
                    FatalError = ex.ToString();
                    TestPassed = false;
                    timer.Stop();
                    if (selfTest) form.Close();
                    else label.Text = "Falha no teste. Feche a janela para salvar o log.";
                }
            };
            start.Click += delegate { startTest(); };
            form.Shown += delegate
            {
                try
                {
                    WindowHandle = form.Handle;
                    uint threadId = GetWindowThreadProcessId(WindowHandle, out WindowOwner);
                    IsTopLevel = GetAncestor(WindowHandle, GaRoot) == WindowHandle;
                    if (threadId == 0 || WindowOwner != CurrentPid || !IsTopLevel)
                        throw new InvalidOperationException("A janela precisa ser propria e de nivel superior.");
                    if (selfTest) startTest();
                }
                catch (Exception ex)
                {
                    FatalError = ex.ToString();
                    TestPassed = false;
                    start.Enabled = false;
                    label.Text = "Nao foi possivel validar a janela propria. Feche para salvar o log.";
                    if (selfTest) form.Close();
                }
            };
            form.FormClosing += delegate
            {
                timer.Stop();
                if (OwnWindowStillValid())
                {
                    Step cleanup = SetAndRead("restaurar_antes_de_fechar", WdaNone);
                    Steps.Add(cleanup);
                    CleanupPassed = cleanup.passed;
                }
                else CleanupPassed = false;
            };
            Application.Run(form);
        }
        catch (Exception ex)
        {
            FatalError = ex.ToString();
            TestPassed = false;
            if (!selfTest)
                MessageBox.Show(ex.Message, "Falha no teste da DLL", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            if (timer != null) timer.Stop();
            if (form != null && !form.IsDisposed)
            {
                form.Close();
                form.Dispose();
            }
            if (timer != null) timer.Dispose();
            if (library != IntPtr.Zero) LibraryFreed = FreeLibrary(library);
            try { File.WriteAllText(LogPath, BuildJson(), new UTF8Encoding(false)); }
            catch (Exception ex)
            {
                Console.Error.WriteLine("Nao foi possivel gravar o log: " + ex.Message);
                returnCode = 3;
            }
        }
        if (returnCode != 0) return returnCode;
        return TestCompleted && TestPassed && CleanupPassed && LibraryFreed && FatalError == null ? 0 : 1;
    }

    private static int returnCode;

    private static bool OwnWindowStillValid()
    {
        if (WindowHandle == IntPtr.Zero || !IsWindow(WindowHandle)) return false;
        uint owner;
        return GetWindowThreadProcessId(WindowHandle, out owner) != 0 && owner == CurrentPid &&
            GetAncestor(WindowHandle, GaRoot) == WindowHandle;
    }

    private static Step SetAndRead(string name, uint affinity)
    {
        Step step = new Step();
        step.name = name;
        step.time = Now();
        step.expectedAffinity = affinity;
        step.setAttempted = true;
        step.setOk = SetWindowDisplayAffinity(WindowHandle, affinity);
        step.setError = step.setOk ? 0 : Marshal.GetLastWin32Error();
        ReadWindow(step);
        step.passed = step.setOk && step.readOk && step.observedAffinity == affinity;
        return step;
    }

    private static Step CallDllAndRead(string name, uint expectedBefore)
    {
        Step step = new Step();
        step.name = name;
        step.time = Now();
        step.expectedAffinity = WdaNone;
        CaptureRestoreResult result = new CaptureRestoreResult();
        result.size = 48;
        result.version = 1;
        step.dllReturn = Restore(WindowHandle, ref result);
        step.dllCalled = true;
        step.native = result;
        ReadWindow(step);
        step.passed = step.dllReturn == 0 && result.status == 0 && result.size == 48 &&
            result.version == 1 && result.caller_pid == CurrentPid && result.owner_pid == CurrentPid &&
            result.before_read_ok == 1 && result.before_affinity == expectedBefore && result.set_ok == 1 &&
            result.after_read_ok == 1 && result.after_affinity == WdaNone && result.win32_error == 0 &&
            step.readOk && step.observedAffinity == WdaNone;
        return step;
    }

    private static void ReadWindow(Step step)
    {
        uint affinity;
        step.readOk = GetWindowDisplayAffinity(WindowHandle, out affinity);
        step.readError = step.readOk ? 0 : Marshal.GetLastWin32Error();
        step.observedAffinity = step.readOk ? affinity : UnknownAffinity;
    }

    private static bool AllStepsPassed()
    {
        if (Steps.Count < 3) return false;
        foreach (Step step in Steps) if (!step.passed) return false;
        return true;
    }

    private static string Now() { return DateTimeOffset.Now.ToString("o", CultureInfo.InvariantCulture); }

    private static string JsonString(string value)
    {
        if (value == null) return "null";
        StringBuilder text = new StringBuilder("\"");
        foreach (char ch in value)
        {
            switch (ch)
            {
                case '\\': text.Append("\\\\"); break;
                case '"': text.Append("\\\""); break;
                case '\r': text.Append("\\r"); break;
                case '\n': text.Append("\\n"); break;
                case '\t': text.Append("\\t"); break;
                default:
                    if (ch < 32) text.Append("\\u" + ((int)ch).ToString("x4", CultureInfo.InvariantCulture));
                    else text.Append(ch);
                    break;
            }
        }
        return text.Append('"').ToString();
    }

    private static string Bool(bool value) { return value ? "true" : "false"; }
    private static string Number(uint value) { return value.ToString(CultureInfo.InvariantCulture); }
    private static string Number(int value) { return value.ToString(CultureInfo.InvariantCulture); }

    private static string BuildJson()
    {
        StringBuilder text = new StringBuilder();
        text.Append("{\n  \"schema_version\": 1,\n  \"scope\": \"own_window_only\",\n");
        text.Append("  \"started\": " + JsonString(Started) + ",\n  \"finished\": " + JsonString(Now()) + ",\n");
        text.Append("  \"dll_path\": " + JsonString(DllPath) + ",\n  \"pid\": " + Number(CurrentPid) + ",\n");
        text.Append("  \"hwnd\": " + JsonString("0x" + WindowHandle.ToInt64().ToString("X")) + ",\n");
        text.Append("  \"window_owner_pid\": " + Number(WindowOwner) + ",\n  \"top_level\": " + Bool(IsTopLevel) + ",\n");
        text.Append("  \"dwm_enabled\": " + Bool(DwmEnabled) + ",\n  \"dwm_hresult\": " + Number(DwmResult) + ",\n");
        text.Append("  \"test_completed\": " + Bool(TestCompleted) + ",\n  \"test_passed\": " + Bool(TestPassed) + ",\n");
        text.Append("  \"cleanup_passed\": " + Bool(CleanupPassed) + ",\n  \"library_freed\": " + Bool(LibraryFreed) + ",\n");
        text.Append("  \"visual_capture_tested\": false,\n  \"load_error\": " + Number(LoadError) + ",\n");
        text.Append("  \"fatal_error\": " + JsonString(FatalError) + ",\n  \"steps\": [\n");
        for (int index = 0; index < Steps.Count; index++)
        {
            Step step = Steps[index];
            text.Append("    {\"name\": " + JsonString(step.name) + ", \"time\": " + JsonString(step.time));
            text.Append(", \"passed\": " + Bool(step.passed) + ", \"expected_affinity\": " + Number(step.expectedAffinity));
            text.Append(", \"set_attempted\": " + Bool(step.setAttempted) + ", \"set_ok\": " + Bool(step.setOk));
            text.Append(", \"set_error\": " + Number(step.setError) + ", \"read_ok\": " + Bool(step.readOk));
            text.Append(", \"observed_affinity\": " + Number(step.observedAffinity) + ", \"read_error\": " + Number(step.readError));
            text.Append(", \"dll_called\": " + Bool(step.dllCalled));
            if (step.dllCalled)
            {
                CaptureRestoreResult n = step.native;
                text.Append(", \"dll_return\": " + Number(step.dllReturn) + ", \"dll_result\": {");
                text.Append("\"size\": " + Number(n.size) + ", \"version\": " + Number(n.version));
                text.Append(", \"status\": " + Number(n.status) + ", \"caller_pid\": " + Number(n.caller_pid));
                text.Append(", \"owner_pid\": " + Number(n.owner_pid) + ", \"before_affinity\": " + Number(n.before_affinity));
                text.Append(", \"after_affinity\": " + Number(n.after_affinity) + ", \"before_read_ok\": " + Number(n.before_read_ok));
                text.Append(", \"set_ok\": " + Number(n.set_ok) + ", \"after_read_ok\": " + Number(n.after_read_ok));
                text.Append(", \"win32_error\": " + Number(n.win32_error) + ", \"reserved\": " + Number(n.reserved) + "}");
            }
            text.Append("}");
            text.Append(index + 1 < Steps.Count ? ",\n" : "\n");
        }
        text.Append("  ]\n}\n");
        return text.ToString();
    }
}
