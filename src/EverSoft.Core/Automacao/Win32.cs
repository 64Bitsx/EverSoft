using System.Runtime.InteropServices;
using System.Text;

namespace EverSoft.Core.Automacao;

/// <summary>
/// Teclado e área de transferência do Windows (o que o pyautogui/pyperclip faziam na versão Python).
/// </summary>
internal static class Win32
{
    public const ushort VK_SHIFT = 0x10, VK_CONTROL = 0x11, VK_MENU = 0x12, VK_RETURN = 0x0D, VK_DELETE = 0x2E, VK_ESCAPE = 0x1B;
    private const uint INPUT_KEYBOARD = 1, KEYEVENTF_KEYUP = 0x2, KEYEVENTF_UNICODE = 0x4;

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT { public uint type; public InputUnion U; }
    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion { [FieldOffset(0)] public MOUSEINPUT mi; [FieldOffset(0)] public KEYBDINPUT ki; }
    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT { public int dx, dy; public uint mouseData, dwFlags, time; public IntPtr dwExtraInfo; }
    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT { public ushort wVk, wScan; public uint dwFlags, time; public IntPtr dwExtraInfo; }

    [DllImport("user32.dll", SetLastError = true)] private static extern uint SendInput(uint n, INPUT[] inputs, int size);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr h, StringBuilder s, int max);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool OpenClipboard(IntPtr h);
    [DllImport("user32.dll")] private static extern bool CloseClipboard();
    [DllImport("user32.dll")] private static extern bool EmptyClipboard();
    [DllImport("user32.dll")] private static extern IntPtr SetClipboardData(uint fmt, IntPtr h);
    [DllImport("kernel32.dll")] private static extern IntPtr GlobalAlloc(uint flags, UIntPtr bytes);
    [DllImport("kernel32.dll")] private static extern IntPtr GlobalLock(IntPtr h);
    [DllImport("kernel32.dll")] private static extern bool GlobalUnlock(IntPtr h);
    [DllImport("kernel32.dll")] private static extern IntPtr GlobalFree(IntPtr h);

    private static INPUT Tecla(ushort vk, bool solta, ushort scan = 0, uint flags = 0) => new()
    {
        type = INPUT_KEYBOARD,
        U = new InputUnion { ki = new KEYBDINPUT { wVk = vk, wScan = scan, dwFlags = flags | (solta ? KEYEVENTF_KEYUP : 0) } },
    };

    private static void Enviar(List<INPUT> l)
    {
        var arr = l.ToArray();
        if (SendInput((uint)arr.Length, arr, Marshal.SizeOf<INPUT>()) != arr.Length)
            throw new InvalidOperationException("O Windows recusou as teclas (feche janelas de administrador que estejam na frente).");
    }

    /// <summary>Aperta uma combinação: Atalho(VK_CONTROL, VK_MENU, 'N').</summary>
    public static void Atalho(params ushort[] teclas)
    {
        var l = new List<INPUT>();
        foreach (var t in teclas) l.Add(Tecla(t, false));
        foreach (var t in teclas.Reverse()) l.Add(Tecla(t, true));
        Enviar(l);
    }

    /// <summary>Digita o texto letra a letra (como o pyautogui.write).</summary>
    public static void Digitar(string texto, int intervaloMs = 30)
    {
        foreach (char c in texto)
        {
            Enviar(new() { Tecla(0, false, c, KEYEVENTF_UNICODE), Tecla(0, true, c, KEYEVENTF_UNICODE) });
            Thread.Sleep(intervaloMs);
        }
    }

    public static string TituloDaJanelaDaFrente()
    {
        var sb = new StringBuilder(512);
        GetWindowText(GetForegroundWindow(), sb, sb.Capacity);
        return sb.ToString();
    }

    /// <summary>Coloca um texto na área de transferência (como o pyperclip.copy).</summary>
    public static void CopiarTexto(string texto)
    {
        const uint CF_UNICODETEXT = 13, GMEM_MOVEABLE = 0x2;
        byte[] bytes = Encoding.Unicode.GetBytes(texto + "\0");
        for (int tentativa = 0; ; tentativa++)
        {
            if (OpenClipboard(IntPtr.Zero)) break;
            if (tentativa >= 20) throw new InvalidOperationException("A área de transferência está ocupada por outro programa.");
            Thread.Sleep(100);
        }
        try
        {
            EmptyClipboard();
            IntPtr h = GlobalAlloc(GMEM_MOVEABLE, (UIntPtr)bytes.Length);
            IntPtr p = GlobalLock(h);
            Marshal.Copy(bytes, 0, p, bytes.Length);
            GlobalUnlock(h);
            if (SetClipboardData(CF_UNICODETEXT, h) == IntPtr.Zero) { GlobalFree(h); throw new InvalidOperationException("Não foi possível copiar o texto."); }
        }
        finally { CloseClipboard(); }
    }
}
