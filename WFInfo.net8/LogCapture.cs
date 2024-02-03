﻿using System.IO;
using System.IO.MemoryMappedFiles;
using System.Text;
using WFInfo.Services.WarframeProcess;

namespace WFInfo;

public delegate void LogWatcherEventHandler(object sender, string text);

class LogCapture : IDisposable
{
    private readonly MemoryMappedFile memoryMappedFile;
    private readonly EventWaitHandle bufferReadyEvent;
    private EventWaitHandle dataReadyEvent;
    readonly CancellationTokenSource tokenSource = new CancellationTokenSource();
    private CancellationToken token;
    private readonly Timer timer;
    public event LogWatcherEventHandler TextChanged;

    private readonly IProcessFinder _process;

    public LogCapture(IProcessFinder process)
    {
        _process = process;

        token = tokenSource.Token;
        Main.AddLog("Starting LogCapture");
        memoryMappedFile = MemoryMappedFile.CreateOrOpen("DBWIN_BUFFER", 4096L);

        bufferReadyEvent = new EventWaitHandle(false, EventResetMode.AutoReset, "DBWIN_BUFFER_READY", out bool createdBuffer);

        if (!createdBuffer)
        {
            Main.AddLog("The DBWIN_BUFFER_READY event exists.");
            return;
        }

        var startTimeSpan = TimeSpan.Zero;
        var periodTimeSpan = TimeSpan.FromSeconds(10);

        timer = new Timer((e) =>
        {
            GetProcess();
        }, null, startTimeSpan, periodTimeSpan);

    }

    private void Run()
    {

        try
        {
            TimeSpan timeout = TimeSpan.FromSeconds(1.0);
            bufferReadyEvent.Set();
            while (!token.IsCancellationRequested)
            {


                if (!dataReadyEvent.WaitOne(timeout))
                {
                    continue;
                }

                if (_process.Warframe != null && !_process.GameIsStreamed)
                {
                    using (MemoryMappedViewStream stream = memoryMappedFile.CreateViewStream())
                    {
                        using (BinaryReader reader = new BinaryReader(stream, Encoding.Default))
                        {
                            uint processId = reader.ReadUInt32();
                            if (processId == _process.Warframe.Id)
                            {
                                char[] chars = reader.ReadChars(4092);
                                int index = Array.IndexOf(chars, '\0');
                                string message = new string(chars, 0, index);
                                TextChanged(this, message.Trim());
                            }
                        }
                    }
                }
                bufferReadyEvent.Set();
            }
        }
        catch (Exception ex)
        {
            Main.AddLog(ex.ToString());
            Main.RunOnUIThread(() =>
            {
                _ = new ErrorDialogue(DateTime.Now, 0);
            });
        }
        finally
        {
            if (memoryMappedFile != null)
                memoryMappedFile.Dispose();

            if (bufferReadyEvent != null)
                bufferReadyEvent.Dispose();

            if (dataReadyEvent != null)
                dataReadyEvent.Dispose();
        }
    }

    private void GetProcess()
    {
        if (!_process.IsRunning) return;
        dataReadyEvent = new EventWaitHandle(false, EventResetMode.AutoReset, "DBWIN_DATA_READY", out bool createdData);

        if (!createdData)
        {
            Main.AddLog("The DBWIN_DATA_READY event exists.");
            return;
        }

        Task.Factory.StartNew(Run);
        timer.Dispose();
    }

    public void Dispose()
    {
        if (memoryMappedFile != null)
            memoryMappedFile.Dispose();

        if (bufferReadyEvent != null)
            bufferReadyEvent.Dispose();

        if (dataReadyEvent != null)
            dataReadyEvent.Dispose();

        tokenSource.Cancel();
        tokenSource.Dispose();
        Main.AddLog("Stoping LogCapture");
    }
}