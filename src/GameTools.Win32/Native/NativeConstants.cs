namespace GameTools.Win32.Native;

public static class NativeConstants
{
    // Windows Messages
    public const uint WM_NULL = 0x0000;
    public const uint WM_SETTEXT = 0x000C;
    public const uint WM_GETTEXT = 0x000D;
    public const uint WM_GETTEXTLENGTH = 0x000E;
    public const uint WM_CLOSE = 0x0010;
    public const uint WM_KEYDOWN = 0x0100;
    public const uint WM_KEYUP = 0x0101;
    public const uint WM_CHAR = 0x0102;
    public const uint WM_SYSKEYDOWN = 0x0104;
    public const uint WM_SYSKEYUP = 0x0105;
    public const uint WM_HOTKEY = 0x0312;
    public const uint WM_USER = 0x0400;

    // Mouse Messages
    public const uint WM_MOUSEMOVE = 0x0200;
    public const uint WM_LBUTTONDOWN = 0x0201;
    public const uint WM_LBUTTONUP = 0x0202;
    public const uint WM_LBUTTONDBLCLK = 0x0203;
    public const uint WM_RBUTTONDOWN = 0x0204;
    public const uint WM_RBUTTONUP = 0x0205;
    public const uint WM_RBUTTONDBLCLK = 0x0206;
    public const uint WM_MBUTTONDOWN = 0x0207;
    public const uint WM_MBUTTONUP = 0x0208;
    public const uint WM_MOUSEWHEEL = 0x020A;
    public const uint WM_XBUTTONDOWN = 0x020B;
    public const uint WM_XBUTTONUP = 0x020C;
    public const uint WM_MOUSEHWHEEL = 0x020E;

    // Hook Types
    public const int WH_KEYBOARD = 2;
    public const int WH_GETMESSAGE = 3;
    public const int WH_CALLWNDPROC = 4;
    public const int WH_MOUSE = 7;
    public const int WH_KEYBOARD_LL = 13;
    public const int WH_MOUSE_LL = 14;

    // SendInput Types
    public const uint INPUT_MOUSE = 0;
    public const uint INPUT_KEYBOARD = 1;
    public const uint INPUT_HARDWARE = 2;

    // Keyboard Flags
    public const uint KEYEVENTF_EXTENDEDKEY = 0x0001;
    public const uint KEYEVENTF_KEYUP = 0x0002;
    public const uint KEYEVENTF_UNICODE = 0x0004;
    public const uint KEYEVENTF_SCANCODE = 0x0008;

    // Mouse Flags
    public const uint MOUSEEVENTF_MOVE = 0x0001;
    public const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
    public const uint MOUSEEVENTF_LEFTUP = 0x0004;
    public const uint MOUSEEVENTF_RIGHTDOWN = 0x0008;
    public const uint MOUSEEVENTF_RIGHTUP = 0x0010;
    public const uint MOUSEEVENTF_MIDDLEDOWN = 0x0020;
    public const uint MOUSEEVENTF_MIDDLEUP = 0x0040;
    public const uint MOUSEEVENTF_WHEEL = 0x0800;
    public const uint MOUSEEVENTF_ABSOLUTE = 0x8000;

    // Raster Operations
    public const int SRCCOPY = 0x00CC0020;
    public const int CAPTUREBLT = 0x40000000;

    // PrintWindow Flags
    public const uint PW_DEFAULT = 0x00000000;
    public const uint PW_CLIENTONLY = 0x00000001;
    public const uint PW_RENDERFULLCONTENT = 0x00000002;

    // WinEvent Hook Flags
    public const uint WINEVENT_OUTOFCONTEXT = 0x0000;
    public const uint WINEVENT_SKIPOWNPROCESS = 0x0002;
    public const uint EVENT_MIN = 0x00000001;
    public const uint EVENT_MAX = 0x7FFFFFFF;
    public const uint EVENT_SYSTEM_FOREGROUND = 0x0003;
    public const uint EVENT_OBJECT_CREATE = 0x8000;
    public const uint EVENT_OBJECT_DESTROY = 0x8001;
    public const uint EVENT_OBJECT_SHOW = 0x8002;
    public const uint EVENT_OBJECT_HIDE = 0x8003;
    public const uint EVENT_SYSTEM_MINIMIZESTART = 0x0016;
    public const uint EVENT_SYSTEM_MINIMIZEEND = 0x0017;
    public const uint EVENT_OBJECT_REORDER = 0x8004;
    public const uint EVENT_OBJECT_FOCUS = 0x8005;
    public const uint EVENT_OBJECT_SELECTION = 0x8006;
    public const uint EVENT_OBJECT_LOCATIONCHANGE = 0x800B;
    public const uint EVENT_OBJECT_NAMECHANGE = 0x800C;

    // DWM Attributes
    public const int DWMWA_EXTENDED_FRAME_BOUNDS = 9;

    // ShowWindow Commands
    public const int SW_HIDE = 0;
    public const int SW_SHOWNORMAL = 1;
    public const int SW_SHOWNOACTIVATE = 4;
    public const int SW_RESTORE = 9;

    // 控件消息：传统 Win32 控件的文本写入与按钮点击
    // 备注：现代应用（Grok Bot、DeepSeek、Chrome 等 Chromium 内核，WPF/WinUI）
    // 不暴露这些控件，实测 Grok Bot 仅 1 个子窗口且无 Edit/Button，
    // 因此以下消息仅作为遗留 MFC/WinForms/Dialog 程序的兜底路径。
    public const uint BM_CLICK = 0x00F5;
    public const uint BM_GETSTATE = 0x00F2;
    public const uint EM_SETSEL = 0x00B1;
    public const uint EM_REPLACESEL = 0x00C2;

    // 窗口遍历与命中测试
    public const uint GA_ROOT = 2;
    public const uint CWP_SKIPINVISIBLE = 0x00000001;
    public const uint CWP_SKIPTRANSPARENT = 0x00000004;
    public const uint CWP_ALL = 0x000000FF;

    // 常用控件类名：传统控件枚举时的匹配目标
    public const string ClassEdit = "Edit";
    public const string ClassRichEdit = "RICHEDIT50W";
    public const string ClassRichEditClass = "RichEdit20W";
    public const string ClassButton = "Button";

    // Virtual-Key Codes used by background message injection
    public const uint VK_RETURN = 0x0D;
    public const uint VK_TAB = 0x09;
    public const uint VK_ESCAPE = 0x1B;
    public const uint VK_SPACE = 0x20;
    public const uint VK_BACK = 0x08;

    // SendMessageTimeout flags
    public const uint SMTO_BLOCK = 0x0001;
    public const uint SMTO_ABORTIFHUNG = 0x0002;

    // RegisterHotKey error codes
    public const int ERROR_HOTKEY_ALREADY_REGISTERED = 1409;
    public const int ERROR_WINDOW_OF_OTHER_THREAD = 1408;

    // WM_APP range: custom messages must live in this range to avoid collisions with
    // third-party libraries using WM_USER (0x0400..0x07FF) and system messages
    public const uint WM_APP = 0x8000;

    // Custom message owned by the message pump, placed in the WM_APP range
    // to avoid colliding with third-party libraries using WM_USER (0x0400..0x07FF)
    public const uint WM_EXECUTE_ACTION = WM_APP + 1;

    // Special parent handle: message-only window
    public static readonly IntPtr HWND_MESSAGE = new(-3);
}
