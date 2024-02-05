using System.IO;
using System.IO.MemoryMappedFiles;
using System.Text;
using Serilog;
using WFInfo.Services.WarframeProcess;

namespace WFInfo;

public sealed class LogCapture : IDisposable
{
    private static readonly ILogger Logger = Log.Logger.ForContext<LogCapture>();
    
    private readonly MemoryMappedFile? _memoryMappedFile;
    private readonly EventWaitHandle? _bufferReadyEvent;
    private EventWaitHandle? _dataReadyEvent;
    private readonly CancellationTokenSource _tokenSource = new();
    private readonly CancellationToken _token;
    private readonly Timer _timer;
    
    public Action<string> TextChanged { get; set; }

    private readonly IProcessFinder _process;

    public LogCapture(IProcessFinder process)
    {
        _process = process;

        _token = _tokenSource.Token;
        Logger.Debug("Starting LogCapture");
        _memoryMappedFile = MemoryMappedFile.CreateOrOpen("DBWIN_BUFFER", 4096L);

        _bufferReadyEvent =
            new EventWaitHandle(false, EventResetMode.AutoReset, "DBWIN_BUFFER_READY", out var createdBuffer);

        if (!createdBuffer)
        {
            Logger.Debug("The DBWIN_BUFFER_READY event exists");
            return;
        }

        var startTimeSpan = TimeSpan.Zero;
        var periodTimeSpan = TimeSpan.FromSeconds(10);

        _timer = new Timer((e) => { GetProcess(); }, null, startTimeSpan, periodTimeSpan);
    }

    private void Run()
    {
        try
        {
            var timeout = TimeSpan.FromSeconds(1);
            _bufferReadyEvent.Set();
            while (!_token.IsCancellationRequested)
            {
                if (!_dataReadyEvent.WaitOne(timeout))
                    continue;

                if (_process is { Warframe: not null, GameIsStreamed: false })
                {
                    using var stream = _memoryMappedFile.CreateViewStream();
                    using var reader = new BinaryReader(stream, Encoding.Default);
                    var processId = reader.ReadUInt32();
                    if (processId == _process.Warframe.Id)
                    {
                        var chars = reader.ReadChars(4092);
                        var index = Array.IndexOf(chars, '\0');
                        var message = new string(chars, 0, index);
                        TextChanged(message.Trim());
                    }
                }

                _bufferReadyEvent.Set();
            }
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Error in LogCapture");
            Main.RunOnUIThread(() => { _ = new ErrorDialogue(DateTime.Now, 0); });
        }
        finally
        {
            _memoryMappedFile?.Dispose();
            _bufferReadyEvent?.Dispose();
            _dataReadyEvent?.Dispose();
        }
    }

    private void GetProcess()
    {
        if (!_process.IsRunning)
            return;
        
        _dataReadyEvent = new EventWaitHandle(false, EventResetMode.AutoReset, "DBWIN_DATA_READY", out var createdData);

        if (!createdData)
        {
            Logger.Debug("The DBWIN_DATA_READY event exists");
            return;
        }

        Task.Factory.StartNew(Run, _token);
        _timer.Dispose();
    }

    public void Dispose()
    {
        _memoryMappedFile?.Dispose();
        _bufferReadyEvent?.Dispose();
        _dataReadyEvent?.Dispose();
        _tokenSource.Cancel();
        _tokenSource.Dispose();
        Logger.Debug("Stoping LogCapture");
    }
}