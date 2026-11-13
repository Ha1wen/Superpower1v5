using System.Diagnostics;
using System.Text.Json;

var root=Path.GetFullPath(args.Length>0 ? args[0] : @"C:\cs2_server");
var artifacts=args.Length>1 ? Path.GetFullPath(args[1]) : Path.Combine(root,@"development\Superpower1v5\artifacts");
Directory.CreateDirectory(artifacts);
var output=Path.Combine(artifacts,"server-smoke-output.txt");
using var log=new StreamWriter(output,false) { AutoFlush=true };
var sync=new object();
void Write(string? line)
{
    if (line is null) return;
    if (line.Contains("CTextConsoleWin::GetLine")) return;
    if (new[] { "sv_setsteamaccount","rcon_password","sv_password","login token" }.Any(x=>line.Contains(x,StringComparison.OrdinalIgnoreCase)))
        line="[credential-related console line omitted]";
    lock(sync) log.WriteLine(line);
}
var start=new ProcessStartInfo(Path.Combine(root,@"game\bin\win64\cs2.exe"))
{
    WorkingDirectory=root,UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden,
    RedirectStandardInput=true,RedirectStandardOutput=true,RedirectStandardError=true
};
start.Environment["SUPERPOWER1V5_PROBE_ARTIFACTS"]=artifacts;
foreach (var arg in new[] { "-basedir",Path.Combine(root,"game"),"-game","csgo","-dedicated","-console","-insecure","-ip","127.0.0.1","-port","27016",
    "+sv_lan","1","+game_alias","competitive","+map","de_dust2","+exec","superpower1v5_smoketest.cfg" }) start.ArgumentList.Add(arg);
using var process=new Process { StartInfo=start,EnableRaisingEvents=true };
process.OutputDataReceived+=(_,e)=>Write(e.Data); process.ErrorDataReceived+=(_,e)=>Write(e.Data);
process.Start(); process.BeginOutputReadLine(); process.BeginErrorReadLine();
File.WriteAllText(Path.Combine(artifacts,"smoke-server-pid.txt"),process.Id.ToString());
File.WriteAllText(Path.Combine(artifacts,"authorized-smoke-pid.txt"),process.Id.ToString());
Console.WriteLine($"Started isolated test server PID {process.Id} on 127.0.0.1:27016.");
async Task Send(string command)
{
    if (process.HasExited) return;
    Write("> "+command);
    try { await process.StandardInput.WriteLineAsync(command); await process.StandardInput.FlushAsync(); }
    catch (IOException) { Write("Console input pipe closed."); }
}
var elapsed=Stopwatch.StartNew();
for (var step=0;step<10 && !process.HasExited;step++)
{
    await Task.Delay(10000);
    if (process.HasExited) break;
    // The optional native probe controls this server in-process; Windows CS2 requires a console input handle.
    Console.WriteLine($"Smoke server alive after {elapsed.Elapsed.TotalSeconds:0}s.");
}
if (!process.HasExited)
{
    await Send("css_sp_debug"); await Send("quit");
    await Task.WhenAny(process.WaitForExitAsync(),Task.Delay(10000));
}
var summary=new { ProcessId=process.Id,Exited=process.HasExited,ExitCode=process.HasExited ? (int?)process.ExitCode : null,
    Seconds=(int)elapsed.Elapsed.TotalSeconds,ConsoleLog=output,
    Note=process.HasExited ? "Only the isolated test server was asked to quit." : "Test server remains running; console pipe did not stop it." };
File.WriteAllText(Path.Combine(artifacts,"server-smoke-process.json"),JsonSerializer.Serialize(summary,new JsonSerializerOptions { WriteIndented=true }));
Console.WriteLine(JsonSerializer.Serialize(summary));
