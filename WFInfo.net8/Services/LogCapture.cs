using System.IO;
using System.IO.MemoryMappedFiles;
using System.Text;
using System.Windows;
using Mediator;
using Serilog;
using WFInfo.Extensions;
using WFInfo.Services.WarframeProcess;

namespace WFInfo.Services;

public sealed record LogCaptureState(bool Enable) : INotification;

//TODO (rudzen) : Convert to fully event driven
public sealed class LogCapture
    : IDisposable,
        INotificationHandler<LogCaptureState>, ILogCapture
{
    public sealed record LogCaptureLineChange(string Line) : INotification;

    private static readonly ILogger Logger = Log.Logger.ForContext<LogCapture>();

    private static readonly TimeSpan Period = TimeSpan.FromSeconds(10);

    private readonly IProcessFinder _processFinder;
    private readonly IMediator _mediator;
    private readonly MemoryMappedFile? _memoryMappedFile;
    private readonly EventWaitHandle? _bufferReadyEvent;
    private EventWaitHandle? _dataReadyEvent;
    private readonly CancellationTokenSource _tokenSource = new();
    private readonly CancellationToken _token;
    private Timer? _timer;
    private bool _bufferCreated;

    // public Action<string> TextChanged { get; set; }

    public LogCapture(IProcessFinder processFinder, IMediator mediator)
    {
        _processFinder = processFinder;

        _token = _tokenSource.Token;
        _memoryMappedFile = MemoryMappedFile.CreateOrOpen("DBWIN_BUFFER", 4096L);

        const string name = "DBWIN_BUFFER_READY";

        _bufferReadyEvent = new EventWaitHandle(
            initialState: false,
            mode: EventResetMode.AutoReset,
            name: name,
            createdNew: out _bufferCreated
        );

        if (!_bufferCreated)
            Logger.Warning("Event already existed. name={Name}", name);
    }

    public bool IsRunning { get; private set; }

    // public LogCapture(IProcessFinder processFinder)
    // {
    //     _processFinder = processFinder;
    //
    //     _token = _tokenSource.Token;
    //     Logger.Debug("Starting LogCapture");
    //     _memoryMappedFile = MemoryMappedFile.CreateOrOpen("DBWIN_BUFFER", 4096L);
    //
    //     _bufferReadyEvent =
    //         new EventWaitHandle(false, EventResetMode.AutoReset, "DBWIN_BUFFER_READY", out var createdBuffer);
    //
    //     if (!createdBuffer)
    //     {
    //         Logger.Debug("The DBWIN_BUFFER_READY event exists");
    //         return;
    //     }
    //
    //     _timer = new Timer((e) =>
    //     {
    //         GetProcess();
    //     }, null, TimeSpan.Zero, Period);
    // }

    private async Task Run()
    {
        IsRunning = true;
        try
        {
            var timeout = TimeSpan.FromSeconds(1);
            _bufferReadyEvent.Set();
            while (!_token.IsCancellationRequested)
            {
                if (!_dataReadyEvent.WaitOne(timeout))
                    continue;

                if (_processFinder is { Warframe: not null, GameIsStreamed: false })
                {
                    await using var stream = _memoryMappedFile.CreateViewStream();
                    using var reader = new BinaryReader(stream, Encoding.Default);
                    var processId = reader.ReadUInt32();
                    if (processId == _processFinder.Warframe.Id)
                    {
                        var line = GetNewLine(reader);
                        await _mediator.Publish(new LogCaptureLineChange(line), _token);
                        // TextChanged(message.Trim());
                    }
                }

                _bufferReadyEvent.Set();
            }
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Error in LogCapture");
            Application.Current.Dispatcher.InvokeIfRequired(() =>
            {
                _ = new ErrorDialogue(DateTime.Now, 0);
            });
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
        if (!_processFinder.IsRunning)
            return;

        _dataReadyEvent = new EventWaitHandle(false, EventResetMode.AutoReset, "DBWIN_DATA_READY", out var createdData);

        if (!createdData)
        {
            Logger.Debug("The DBWIN_DATA_READY event exists");
            return;
        }

        Task.Factory.StartNew(Run, _token);
        _timer.Change(Timeout.Infinite, Timeout.Infinite);
    }

    private static string GetNewLine(BinaryReader reader)
    {
        var chars = new Span<char>(reader.ReadChars(4092));
        var index = chars.IndexOf('\0');
        return new string(chars[..index]);
    }

    public ValueTask Handle(LogCaptureState logCaptureState, CancellationToken cancellationToken)
    {
        IsRunning = logCaptureState.Enable;
        Logger.Debug("LogCapture state changed to {Enable}", logCaptureState.Enable);

        if (logCaptureState.Enable)
        {
            if (_timer is null)
            {
                _timer = new Timer((e) =>
                {
                    GetProcess();
                }, null, TimeSpan.Zero, Period);
            }
            else
            {
                _timer.Change(TimeSpan.Zero, Period);
            }
        }
        else
        {
            _timer?.Change(Timeout.Infinite, Timeout.Infinite);
        }

        return ValueTask.CompletedTask;
    }

    public void Dispose()
    {
        _memoryMappedFile?.Dispose();
        _bufferReadyEvent?.Dispose();
        _dataReadyEvent?.Dispose();
        if (!_tokenSource.IsCancellationRequested)
            _tokenSource.Cancel();
        _tokenSource.Dispose();
        Logger.Debug("Stopping LogCapture");
    }
}
