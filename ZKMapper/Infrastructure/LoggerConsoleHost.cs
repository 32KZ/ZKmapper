using System.Diagnostics;
using System.Text;

namespace ZKMapper.Infrastructure;

internal static class LoggerConsoleHost
{
    public static void Start(string logFilePath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(logFilePath)!);
        if (!File.Exists(logFilePath))
        {
            using var stream = new FileStream(logFilePath, FileMode.OpenOrCreate, FileAccess.Write, FileShare.ReadWrite);
        }

        var escapedPath = logFilePath.Replace("'", "''", StringComparison.Ordinal);
        var powerShellCommand = $@"
$OutputEncoding = [Console]::OutputEncoding = [System.Text.UTF8Encoding]::new()
$pattern = '^(?<ts>\S+\s+\S+\s+\[[A-Z]+\])\s+(?<msg>.*?)\s+step=(?<step>.*?)\s+action=(?<action>.*?)\s+data=(?<data>.*?)\s+duration=(?<duration>.*?)\s+Company=(?<company>.*?)\s+Query=(?<query>.*?)\s+ProfileUrl=(?<profile>.*)$'
function Write-LogLine([string]$line) {{
    if ($line -match $pattern) {{
        $ts = $Matches['ts']
        $msg = $Matches['msg']
        $step = $Matches['step']
        $action = $Matches['action']
        $data = $Matches['data']
        $duration = $Matches['duration']
        $company = $Matches['company']
        $query = $Matches['query']
        $profile = $Matches['profile']

        $color = 'Gray'
        if ($msg -match '\[ERROR\]|\[ERR\]') {{ $color = 'Red' }}
        elseif ($msg -match '\[WARN\]|\[WRN\]') {{ $color = 'Yellow' }}
        elseif ($msg -match '\[DEBUG\]|\[DBG\]') {{ $color = 'Cyan' }}
        elseif ($msg -match '\[TRACE\]|\[VRB\]') {{ $color = 'DarkGray' }}
        elseif ($msg -match '\[RESULT\]') {{ $color = 'Green' }}
        elseif ($msg -match '\[STEP\]|\[ACTION\]|\[NEXT\]') {{ $color = 'Magenta' }}
        elseif ($msg -match '\[DATA\]|\[INPUT\]') {{ $color = 'DarkCyan' }}

        Write-Host ""$ts  $msg"" -ForegroundColor $color

        $meta = @()
        if ($step) {{ $meta += ""step=$step"" }}
        if ($action) {{ $meta += ""action=$action"" }}
        if ($data) {{ $meta += ""data=$data"" }}
        if ($duration) {{ $meta += ""duration=$duration"" }}
        if ($company) {{ $meta += ""company=$company"" }}
        if ($query) {{ $meta += ""query=$query"" }}
        if ($profile) {{ $meta += ""profile=$profile"" }}

        if ($meta.Count -gt 0) {{
            Write-Host (""  "" + ($meta -join "" | "")) -ForegroundColor DarkGray
        }}
        return
    }}

    Write-Host $line
}}

Get-Content -LiteralPath '{escapedPath}' -Wait -Tail 0 | ForEach-Object {{
    Write-LogLine $_
}}
";
        var encodedCommand = Convert.ToBase64String(Encoding.Unicode.GetBytes(powerShellCommand));

        var startCommand =
            $"start \"ZKMapper Logs\" cmd.exe /k \"title ZKMapper Logs && mode con cols=180 lines=9999 && powershell.exe -NoLogo -NoExit -EncodedCommand {encodedCommand}\"";

        Process.Start(new ProcessStartInfo
        {
            FileName = "cmd.exe",
            Arguments = $"/c {startCommand}",
            UseShellExecute = true,
            CreateNoWindow = false,
            WindowStyle = ProcessWindowStyle.Normal
        });
    }
}
