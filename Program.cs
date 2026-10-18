using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using System.Windows.Forms;

static class Program
{
    delegate IntPtr HookProc(int code, IntPtr wParam, IntPtr lParam);
    delegate IntPtr SubclassProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam, UIntPtr id, UIntPtr data);

    [DllImport("user32.dll")] static extern IntPtr SetWindowsHookEx(int id, HookProc proc, IntPtr hMod, uint threadId);
    [DllImport("user32.dll")] static extern bool UnhookWindowsHookEx(IntPtr hhk);
    [DllImport("user32.dll")] static extern IntPtr CallNextHookEx(IntPtr hhk, int code, IntPtr w, IntPtr l);
    [DllImport("kernel32.dll")] static extern uint GetCurrentThreadId();
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassName(IntPtr h, StringBuilder sb, int n);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr FindWindow(string cls, string title);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr h, int msg, IntPtr w, IntPtr l);
    [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr h, int idx);
    [DllImport("user32.dll")] static extern int SetWindowLong(IntPtr h, int idx, int val);
    [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr h, int cmd);
    [DllImport("comctl32.dll")] static extern bool SetWindowSubclass(IntPtr h, SubclassProc proc, UIntPtr id, UIntPtr data);
    [DllImport("comctl32.dll")] static extern bool RemoveWindowSubclass(IntPtr h, SubclassProc proc, UIntPtr id);
    [DllImport("comctl32.dll")] static extern IntPtr DefSubclassProc(IntPtr h, uint msg, IntPtr w, IntPtr l);

    [StructLayout(LayoutKind.Sequential)]
    struct RECT { public int L, T, R, B; }

    [StructLayout(LayoutKind.Sequential)]
    struct CBT_CREATEWND { public IntPtr lpcs; public IntPtr hwndInsertAfter; }

    [StructLayout(LayoutKind.Sequential)]
    struct CREATESTRUCT
    {
        public IntPtr lpCreateParams, hInstance, hMenu, hwndParent;
        public int cy, cx, y, x, style;
        public IntPtr lpszName, lpszClass;
        public uint dwExStyle;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct WINDOWPOS
    {
        public IntPtr hwnd, hwndInsertAfter;
        public int x, y, cx, cy;
        public uint flags;
    }

    const int OFFSCREEN = -32000;
    const uint WM_WINDOWPOSCHANGING = 0x0046;
    const uint SWP_NOMOVE = 0x0002;

    static IntPtr hook;
    static bool holdOffscreen = true;
    static readonly List<IntPtr> subclassed = new List<IntPtr>();

    // ダイアログ自身が置こうとした位置
    static bool hasIntended;
    static int intendedX, intendedY;
    static int lastPosChange = Environment.TickCount;

    static Icon exeIcon; // GCでハンドルが無効にならないよう保持する

    // デリゲートはGCに回収されないよう、staticフィールドで保持する
    static readonly SubclassProc subclassProc = (h, msg, w, l, id, data) =>
    {
        if (msg == WM_WINDOWPOSCHANGING && holdOffscreen)
        {
            var wp = (WINDOWPOS)Marshal.PtrToStructure(l, typeof(WINDOWPOS));
            if ((wp.flags & SWP_NOMOVE) == 0 && wp.x != OFFSCREEN)
            {
                intendedX = wp.x;
                intendedY = wp.y;
                hasIntended = true;
            }
            lastPosChange = Environment.TickCount;

            wp.x = OFFSCREEN;
            wp.y = OFFSCREEN;
            wp.flags &= ~SWP_NOMOVE;
            Marshal.StructureToPtr(wp, l, false);
        }
        return DefSubclassProc(h, msg, w, l);
    };

    static readonly HookProc cbtProc = (code, w, l) =>
    {
        if (code == 3) // HCBT_CREATEWND
        {
            var cw = (CBT_CREATEWND)Marshal.PtrToStructure(l, typeof(CBT_CREATEWND));
            var cs = (CREATESTRUCT)Marshal.PtrToStructure(cw.lpcs, typeof(CREATESTRUCT));
            if ((cs.style & 0x40000000) == 0) // WS_CHILDでない = トップレベル
            {
                var sb = new StringBuilder(32);
                GetClassName(w, sb, 32);
                if (sb.ToString() == "#32770")
                {
                    SetWindowSubclass(w, subclassProc, (UIntPtr)1, UIntPtr.Zero);
                    subclassed.Add(w);
                }
            }
        }
        return CallNextHookEx(hook, code, w, l);
    };

    static bool OnAnyScreen(Rectangle rect)
    {
        foreach (var s in Screen.AllScreens)
            if (s.WorkingArea.IntersectsWith(rect)) return true;
        return false;
    }

    // 画面外にいる間に、アイコン設定とタスクバー用の再表示を行う
    static void Decorate(IntPtr h)
    {
        if (exeIcon == null)
            exeIcon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        SendMessage(h, 0x0080, (IntPtr)0, exeIcon.Handle); // WM_SETICON, ICON_SMALL
        SendMessage(h, 0x0080, (IntPtr)1, exeIcon.Handle); // WM_SETICON, ICON_BIG

        const int GWL_EXSTYLE = -20, WS_EX_APPWINDOW = 0x40000;
        SetWindowLong(h, GWL_EXSTYLE, GetWindowLong(h, GWL_EXSTYLE) | WS_EX_APPWINDOW);
        ShowWindow(h, 0); // SW_HIDE
        ShowWindow(h, 5); // SW_SHOW
    }

    // 画面外固定を解除して、本来の位置（なければ中央）に戻す
    static void Release(IntPtr h)
    {
        holdOffscreen = false;
        UnhookWindowsHookEx(hook);
        foreach (var w in subclassed)
            RemoveWindowSubclass(w, subclassProc, (UIntPtr)1);

        if (h == IntPtr.Zero) return;
        RECT r;
        GetWindowRect(h, out r);
        int cx = r.R - r.L, cy = r.B - r.T;

        int x, y;
        if (hasIntended && OnAnyScreen(new Rectangle(intendedX, intendedY, cx, cy)))
        {
            x = intendedX;
            y = intendedY;
        }
        else
        {
            var wa = Screen.PrimaryScreen.WorkingArea;
            x = wa.Left + (wa.Width - cx) / 2;
            y = wa.Top + (wa.Height - cy) / 2;
        }
        SetWindowPos(h, IntPtr.Zero, x, y, 0, 0, 0x0001 | 0x0004); // NOSIZE | NOZORDER
    }

    [STAThread]
    static void Main(string[] args)
    {
        Application.EnableVisualStyles(); // SetWindowSubclass(comctl32 v6)に必要

        using (var dlg = new OpenFileDialog())
        {
            var id = WindowsIdentity.GetCurrent();
            string title = "[" + id.Name + "] Stand Alone Explorer";
            dlg.Title = title;
            dlg.Filter = "すべてのファイル (*.*)|*.*";
            dlg.Multiselect = true;
            dlg.DereferenceLinks = false; // .lnkをリンク先に解決せず、そのまま開く
            dlg.InitialDirectory = args.Length > 0 ? args[0] : @"C:\";

            dlg.FileOk += (s, e) =>
            {
                e.Cancel = true; // ダイアログを閉じない
                foreach (var f in dlg.FileNames)
                {
                    try { Process.Start(new ProcessStartInfo(f) { UseShellExecute = true }); }
                    catch (Exception ex)
                    {
                        MessageBox.Show(ex.Message, "開けません",
                            MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    }
                }
            };

            hook = SetWindowsHookEx(5, cbtProc, IntPtr.Zero, GetCurrentThreadId()); // WH_CBT

            bool decorated = false;
            int start = Environment.TickCount;
            var timer = new System.Windows.Forms.Timer { Interval = 10 };
            timer.Tick += (s, e) =>
            {
                bool timeout = Environment.TickCount - start > 3000; // 念のための上限
                IntPtr h = FindWindow("#32770", title);
                if (h == IntPtr.Zero || !IsWindowVisible(h))
                {
                    if (!timeout) return;
                    timer.Stop();
                    Release(h);
                    return;
                }

                if (!decorated)
                {
                    Decorate(h);
                    decorated = true;
                    return;
                }

                // 位置変更の要求が22ms途切れたら（ダイアログの配置が落ち着いたら）戻す
                if (Environment.TickCount - lastPosChange < 22 && !timeout) return;

                timer.Stop();
                Release(h);
            };
            timer.Start();

            dlg.ShowDialog();
        }
    }
}