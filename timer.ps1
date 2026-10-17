param(
    [int]$Minutes = 45,
    [int]$WarningMinutes = 5,
    [string]$TaskName = "作業",
    [ValidateSet("primary", "second", "all")]
    [string]$Target = "all"
)

Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName PresentationFramework

function Get-TargetScreens {
    param([string]$TargetMode)

    $screens = [System.Windows.Forms.Screen]::AllScreens
    switch ($TargetMode) {
        "primary" { return @([System.Windows.Forms.Screen]::PrimaryScreen) }
        "second" {
            if ($screens.Count -ge 2) { return @($screens[1]) }
            return @([System.Windows.Forms.Screen]::PrimaryScreen)
        }
        "all" { return @($screens) }
    }
}

function Show-ToastLikeWindow {
    param(
        [string]$Title,
        [string]$Message,
        [System.Windows.Forms.Screen[]]$Screens,
        [int]$Width = 360,
        [int]$Height = 110,
        [string]$BackgroundColor = "#2B2B2B",
        [string]$ForegroundColor = "White",
        [int]$FontSize = 16,
        [int]$AutoCloseSeconds = 4,
        [switch]$Center,
        [switch]$RequireOk,
        [string]$ButtonText = "OK"
    )

    $windows = @()

    foreach ($screen in $Screens) {
        if ($Center) {
            $x = $screen.WorkingArea.Left + [math]::Floor(($screen.WorkingArea.Width - $Width) / 2)
            $y = $screen.WorkingArea.Top + [math]::Floor(($screen.WorkingArea.Height - $Height) / 2)
        } else {
            $margin = 20
            $x = $screen.WorkingArea.Right - $Width - $margin
            $y = $screen.WorkingArea.Bottom - $Height - $margin
        }

        [xml]$xaml = @"
<Window
    xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
    Width="$Width"
    Height="$Height"
    WindowStyle="None"
    ResizeMode="NoResize"
    ShowInTaskbar="False"
    Topmost="True"
    AllowsTransparency="True"
    Background="Transparent"
    WindowStartupLocation="Manual"
    Left="$x"
    Top="$y">
    <Border CornerRadius="14" Background="$BackgroundColor" Opacity="0.97" Padding="18">
        <Grid>
            <Grid.RowDefinitions>
                <RowDefinition Height="Auto"/>
                <RowDefinition Height="*"/>
                <RowDefinition Height="Auto"/>
            </Grid.RowDefinitions>

            <TextBlock x:Name="TitleText"
                       Grid.Row="0"
                       Text="$Title"
                       Foreground="$ForegroundColor"
                       FontSize="14"
                       FontWeight="SemiBold"
                       Margin="0,0,0,6"/>

            <TextBlock x:Name="MessageText"
                       Grid.Row="1"
                       Text="$Message"
                       Foreground="$ForegroundColor"
                       FontSize="$FontSize"
                       FontWeight="Bold"
                       TextWrapping="Wrap"
                       VerticalAlignment="Center"/>

            <Button x:Name="OkButton"
                    Grid.Row="2"
                    Content="$ButtonText"
                    Width="90"
                    Height="32"
                    HorizontalAlignment="Right"
                    Margin="0,12,0,0"
                    Visibility="Collapsed"/>
        </Grid>
    </Border>
</Window>
"@

        $reader = New-Object System.Xml.XmlNodeReader $xaml
        $window = [Windows.Markup.XamlReader]::Load($reader)

        $okButton = $window.FindName("OkButton")

        if ($RequireOk) {
            $okButton.Visibility = "Visible"
            $okButton.Add_Click({ $window.Close() })
        } elseif ($AutoCloseSeconds -gt 0) {
            $timer = New-Object System.Windows.Threading.DispatcherTimer
            $timer.Interval = [TimeSpan]::FromSeconds($AutoCloseSeconds)
            $timer.Add_Tick({
                $timer.Stop()
                $window.Close()
            })
            $timer.Start()
        }

        $window.Show()
        $windows += $window
    }

    return ,$windows
}

$screens = Get-TargetScreens -TargetMode $Target
$endTime = (Get-Date).AddMinutes($Minutes)
$warned = $false
$stopped = $false

# コントローラ画面（停止ボタン付き）
$form = New-Object System.Windows.Forms.Form
$form.Text = "Timer - $TaskName"
$form.Size = New-Object System.Drawing.Size(300, 150)
$form.StartPosition = "CenterScreen"
$form.TopMost = $false
$form.FormBorderStyle = "FixedDialog"
$form.MaximizeBox = $false
$form.MinimizeBox = $true
$form.ShowInTaskbar = $true

$labelTask = New-Object System.Windows.Forms.Label
$labelTask.Text = "タスク: $TaskName"
$labelTask.Location = New-Object System.Drawing.Point(20, 15)
$labelTask.Size = New-Object System.Drawing.Size(240, 20)
$form.Controls.Add($labelTask)

$labelRemain = New-Object System.Windows.Forms.Label
$labelRemain.Text = "残り: --:--"
$labelRemain.Font = New-Object System.Drawing.Font("Segoe UI", 16, [System.Drawing.FontStyle]::Bold)
$labelRemain.Location = New-Object System.Drawing.Point(20, 40)
$labelRemain.Size = New-Object System.Drawing.Size(240, 35)
$form.Controls.Add($labelRemain)

$buttonStop = New-Object System.Windows.Forms.Button
$buttonStop.Text = "停止"
$buttonStop.Location = New-Object System.Drawing.Point(20, 85)
$buttonStop.Size = New-Object System.Drawing.Size(100, 30)
$form.Controls.Add($buttonStop)

$buttonHide = New-Object System.Windows.Forms.Button
$buttonHide.Text = "最小化"
$buttonHide.Location = New-Object System.Drawing.Point(140, 85)
$buttonHide.Size = New-Object System.Drawing.Size(100, 30)
$form.Controls.Add($buttonHide)

$buttonHide.Add_Click({
    $form.WindowState = [System.Windows.Forms.FormWindowState]::Minimized
})

$buttonStop.Add_Click({
    $script:stopped = $true
    $form.Close()
})

$form.Add_FormClosing({
    if (-not $script:stopped) {
        # ×で閉じた場合も停止扱い
        $script:stopped = $true
    }
})

$timer = New-Object System.Windows.Forms.Timer
$timer.Interval = 1000

$timer.Add_Tick({
    $remaining = $endTime - (Get-Date)

    if ($remaining.TotalSeconds -le 0) {
        $timer.Stop()
        [console]::Beep(1200, 300)
        Start-Sleep -Milliseconds 120
        [console]::Beep(1200, 300)

        Show-ToastLikeWindow `
            -Title "時間です" `
            -Message "$TaskName の制限時間です。次の工程へ。" `
            -Screens $screens `
            -Width 520 `
            -Height 180 `
            -BackgroundColor "#B00020" `
            -ForegroundColor "White" `
            -FontSize 26 `
            -Center `
            -RequireOk | Out-Null

        $form.Close()
        return
    }

    $mins = [int]$remaining.TotalMinutes
    $secs = $remaining.Seconds
    $labelRemain.Text = ("残り: {0:D2}:{1:D2}" -f $mins, $secs)

    if (-not $warned -and $remaining.TotalMinutes -le $WarningMinutes -and $remaining.TotalSeconds -gt 0) {
        $script:warned = $true
        Show-ToastLikeWindow `
            -Title "タイマー通知" `
            -Message "$TaskName あと${WarningMinutes}分です" `
            -Screens $screens `
            -Width 340 `
            -Height 100 `
            -BackgroundColor "#2B2B2B" `
            -ForegroundColor "White" `
            -FontSize 16 `
            -AutoCloseSeconds 4 | Out-Null
    }
})

$timer.Start()
[void]$form.ShowDialog()
$timer.Stop()
