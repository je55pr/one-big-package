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

foreach ($part in $parts) {
    $nameCondition = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::NameProperty, $part)
    $typeCondition = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
        [System.Windows.Automation.ControlType]::ListItem)
    $condition = New-Object System.Windows.Automation.AndCondition($nameCondition, $typeCondition)
    $item = $dialog.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $condition)
    if (-not $item) { throw ('picker path component not found: ' + $part) }

    $invoke = $item.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern)
    $invoke.Invoke()
    Start-Sleep -Milliseconds 450
}

# Invoking the final ExistingFile list item accepts the QFileDialog.
for ($i = 0; $i -lt 20 -and [Rac1PickerNative]::IsWindow($handle); $i++) {
    Start-Sleep -Milliseconds 100
}
if ([Rac1PickerNative]::IsWindow($handle)) { throw 'input-recording picker did not close' }
