param(
    [Parameter(Mandatory=$true, Position=0)][long]$DialogHandle,
    [Parameter(Mandatory=$true, Position=1)][string]$MoviePath
)

Add-Type -AssemblyName System.Windows.Forms
Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class Rac1PickerNative {
    [DllImport("user32.dll")]
    public static extern bool IsWindow(IntPtr hWnd);
    [DllImport("user32.dll")]
    public static extern IntPtr GetDlgItem(IntPtr hDlg, int nIDDlgItem);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)]
    public static extern IntPtr FindWindowEx(IntPtr parent, IntPtr childAfter, string className, string windowName);
    [DllImport("user32.dll")]
    public static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")]
    public static extern IntPtr SetFocus(IntPtr hWnd);
    [DllImport("user32.dll")]
    public static extern IntPtr SendMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);
}
'@

$handle = [IntPtr]$DialogHandle
if (-not [Rac1PickerNative]::IsWindow($handle)) { throw 'input-recording picker handle is not valid' }

$absolutePath = [System.IO.Path]::GetFullPath($MoviePath)
if (-not [System.IO.File]::Exists($absolutePath)) {
    throw ('input-recording movie does not exist: ' + $absolutePath)
}

# PCSX2's native QFileDialog remembers directories and virtualizes off-screen
# rows. Target the real filename Edit instead: clear stale text first, type the
# already-resolved absolute path as keyboard input so Qt receives its normal
# change events, then accept it with Enter.
$comboEx = [Rac1PickerNative]::GetDlgItem($handle, 1148)
$combo = [Rac1PickerNative]::FindWindowEx($comboEx, [IntPtr]::Zero, 'ComboBox', $null)
$edit = [Rac1PickerNative]::FindWindowEx($combo, [IntPtr]::Zero, 'Edit', $null)
if ($edit -eq [IntPtr]::Zero) { throw 'input-recording filename edit control not found' }

[Rac1PickerNative]::SetForegroundWindow($handle) | Out-Null
[Rac1PickerNative]::SetFocus($edit) | Out-Null
Start-Sleep -Milliseconds 80
[System.Windows.Forms.SendKeys]::SendWait('^a')
[System.Windows.Forms.SendKeys]::SendWait('{BACKSPACE}')
$escapedPath = $absolutePath -replace '([+^%~()\[\]{}])', '{$1}'
[System.Windows.Forms.SendKeys]::SendWait($escapedPath)
[System.Windows.Forms.SendKeys]::SendWait('{ENTER}')

# Enter normally accepts the ExistingFile path. Preserve the newer source
# fallback for Qt builds that leave the picker open after selection.
for ($i = 0; $i -lt 5 -and [Rac1PickerNative]::IsWindow($handle); $i++) {
    Start-Sleep -Milliseconds 100
}
if ([Rac1PickerNative]::IsWindow($handle)) {
    $open = [Rac1PickerNative]::GetDlgItem($handle, 1)
    if ($open -ne [IntPtr]::Zero) {
        [void][Rac1PickerNative]::SendMessage($open, 0x00F5, [IntPtr]::Zero, [IntPtr]::Zero)
    }
}
for ($i = 0; $i -lt 20 -and [Rac1PickerNative]::IsWindow($handle); $i++) {
    Start-Sleep -Milliseconds 100
}
if ([Rac1PickerNative]::IsWindow($handle)) { throw 'input-recording picker did not close' }
