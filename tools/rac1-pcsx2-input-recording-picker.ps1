param(
    [Parameter(Mandatory=$true, Position=0)][long]$DialogHandle,
    [Parameter(Mandatory=$true, Position=1)][string]$RelativePath
)

Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class Rac1PickerNative {
    [DllImport("user32.dll", CharSet=CharSet.Unicode)]
    public static extern bool SetDlgItemText(IntPtr hDlg, int nIDDlgItem, string lpString);
    [DllImport("user32.dll")]
    public static extern bool IsWindow(IntPtr hWnd);
    [DllImport("user32.dll")]
    public static extern IntPtr GetDlgItem(IntPtr hDlg, int nIDDlgItem);
    [DllImport("user32.dll")]
    public static extern IntPtr SendMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);
}
'@

$handle = [IntPtr]$DialogHandle
if (-not [Rac1PickerNative]::IsWindow($handle)) { throw 'input-recording picker handle is not valid' }

# A native QFileDialog can retain a prior filename. Clear it first so path
# components are never concatenated with stale text.
if (-not [Rac1PickerNative]::SetDlgItemText($handle, 1148, '')) {
    throw 'failed to clear input-recording filename field'
}

$dialog = [System.Windows.Automation.AutomationElement]::FromHandle($handle)
if (-not $dialog) { throw 'UI Automation could not attach to input-recording picker' }

$parts = @($RelativePath -split '[\\/]' | Where-Object { $_.Length -gt 0 })
if ($parts.Count -eq 0) { throw 'relative movie path has no components' }

$started = $false
for ($index = 0; $index -lt $parts.Count; $index++) {
    $part = $parts[$index]
    $nameCondition = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::NameProperty, $part)
    $typeCondition = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
        [System.Windows.Automation.ControlType]::ListItem)
    $condition = New-Object System.Windows.Automation.AndCondition($nameCondition, $typeCondition)
    $item = $dialog.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $condition)
    if (-not $item) {
        # QFileDialog remembers its last directory. Leading components may
        # therefore already be represented by the current directory itself.
        if (-not $started -and $index -lt ($parts.Count - 1)) { continue }
        throw ('picker path component not found: ' + $part)
    }

    $started = $true
    $invoke = $item.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern)
    $invoke.Invoke()
    Start-Sleep -Milliseconds 450
}

# Invoking an ExistingFile item normally accepts the QFileDialog. Some Qt
# builds only select it, so explicitly click the native OK/Open button as a
# deterministic fallback before declaring the replay picker stuck.
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
